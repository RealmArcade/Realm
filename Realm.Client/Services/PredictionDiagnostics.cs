using System;
using System.Collections.Generic;
using System.IO;
using System.Numerics;
using Realm.Ecs;
using Realm.Ecs.Common;
using Realm.Ecs.Components.Core;
using Realm.Ecs.Components.Movement;
using Realm.Ecs.Components.Tags;
using Realm.Ecs.Services;

namespace Realm.Client.Services;

public static class PredictionDiagnostics
{
	public const uint FileMagic = 0x50445243;
	public const int FileVersion = 3;
	public const int MaximumRecordedWaypoints = 16;

	public const byte KindClientTick = 1;
	public const byte KindCommandRegistered = 2;
	public const byte KindCommandAccepted = 3;
	public const byte KindRejection = 4;
	public const byte KindRollback = 5;
	public const byte KindSnap = 6;
	public const byte KindCorrection = 7;
	public const byte KindPredictionStopped = 8;
	public const byte KindScriptOrder = 9;
	public const byte KindScriptUpdate = 10;
	public const byte KindScenario = 11;
	public const byte KindAllocation = 12;
	public const byte KindSnapshotOrderingViolation = 13;
	public const byte KindHostSnapshot = 20;
	public const byte KindAppliedOrder = 21;

	private static BinaryWriter? _predictionWriter;
	private static BinaryWriter? _authorityWriter;
	private static string _outputDirectory = string.Empty;
	private static int _lastClientTick = -1;
	private static EcsWorld? _recordingWorld;
	private static MovementPredictionService? _recordingPrediction;
	private static int _recordingLocalPlayerIndex = -1;
	private static int _remoteRecordCount;
	private static bool _recordRemoteUnits = true;
	private static readonly EntityCallback<Position, UnitOwnerPlayer> _countRemoteUnitsDelegate = CountRemoteUnitAction;
	private static readonly EntityCallback<Position, UnitOwnerPlayer> _writeRemoteUnitsDelegate = WriteRemoteUnitAction;

	public static bool IsRecording => _predictionWriter != null;
	public static bool IsHostRecording => _authorityWriter != null;
	public static string OutputDirectory => _outputDirectory;

	/// <summary>
	/// Records an order the client's ECS took from a snapshot, keyed by the sequence that carried it. The per-tick record
	/// samples the sequence axis at the prediction rate while snapshots arrive at the authority's rate, so an order the
	/// authority published for a single sequence can be applied and replaced inside one client tick and never appear in
	/// that record. This event states whether the snapshot stream carried the order at all, independently of how the tick
	/// sampling happens to line up with it.
	/// </summary>
	public static void RecordAppliedOrder(int snapshotSequence, int serverEntityId, bool hasTarget, Vector3 target)
	{
		var writer = _predictionWriter;
		if (writer == null) return;

		writer.Write(KindAppliedOrder);
		writer.Write(snapshotSequence);
		writer.Write(serverEntityId);
		writer.Write(hasTarget);
		WriteVector(writer, target);
		writer.Flush();
	}

	/// <summary>
	/// Opens the prediction record streams. A remote client records prediction only; the authority records the snapshot
	/// stream only; a single process that is both (single player, offline, or the hosting player) records both.
	/// </summary>
	public static void Start(string outputDirectory, bool recordAuthoritySnapshots, bool recordPrediction)
	{
		Stop();
		_outputDirectory = outputDirectory;
		_lastClientTick = -1;

		if (recordAuthoritySnapshots) _authorityWriter = OpenWriter(outputDirectory, "prediction_host.bin", true);
		if (recordPrediction) _predictionWriter = OpenWriter(outputDirectory, "prediction_client.bin", false);
	}

	private static BinaryWriter OpenWriter(string outputDirectory, string fileName, bool isAuthority)
	{
		string path = Path.Combine(outputDirectory, fileName);
		FileStream stream = new(path, FileMode.Create, FileAccess.Write, FileShare.Read, 1 << 16);
		var writer = new BinaryWriter(stream);
		writer.Write(FileMagic);
		writer.Write(FileVersion);
		writer.Write(isAuthority);
		writer.Flush();
		return writer;
	}

	public static void Stop()
	{
		_predictionWriter?.Flush();
		_predictionWriter?.Dispose();
		_predictionWriter = null;

		_authorityWriter?.Flush();
		_authorityWriter?.Dispose();
		_authorityWriter = null;
	}

	public static void RecordClientTick(EcsWorld world, int clientTick, MovementPredictionService prediction)
	{
		var _writer = _predictionWriter;
		if (_writer == null) return;
		if (clientTick == _lastClientTick) return;
		_lastClientTick = clientTick;

		int localPlayerIndex = GameContext.Host?.ObservingPlayerIndex ?? -1;
		ReadOnlySpan<int> units = prediction.TrackedEntityIds;

		// The count has to describe the units that are actually written. A tracked unit can die between the prune and
		// this record, and a count that includes it desynchronises every reader for the rest of the stream, which turns
		// the report into contradictory data instead of a measurement.
		int aliveCount = 0;
		for (int index = 0; index < units.Length; index++)
		{
			if (world.IsAlive(new Entity(units[index]))) aliveCount++;
		}

		// A unit the client does not own is the control against which prediction is measured: it must never lead the
		// authority, and an order the client could not anticipate must appear only as fast as a snapshot can carry it.
		// Recording it costs one extra query per tick and is the only way to state either claim as a number.
		_recordingWorld = world;
		_recordingPrediction = prediction;
		_recordingLocalPlayerIndex = localPlayerIndex;
		_remoteRecordCount = 0;
		if (_recordRemoteUnits)
		{
			world.Query(in QueryCache.AllPositionAndUnitOwnerPlayerNoneDeadQuery, _countRemoteUnitsDelegate);
			aliveCount += _remoteRecordCount;
		}

		_writer.Write(KindClientTick);
		_writer.Write(clientTick);
		_writer.Write(prediction.LastSnapshotSequence);
		_writer.Write(localPlayerIndex);
		_writer.Write(aliveCount);

		for (int index = 0; index < units.Length; index++)
		{
			var entity = new Entity(units[index]);
			if (!world.IsAlive(entity)) continue;
			WriteUnitRecord(world, prediction, localPlayerIndex, entity);
		}

		if (_recordRemoteUnits) world.Query(in QueryCache.AllPositionAndUnitOwnerPlayerNoneDeadQuery, _writeRemoteUnitsDelegate);

		_writer.Flush();
	}

	private static void WriteUnitRecord(EcsWorld world, MovementPredictionService prediction, int localPlayerIndex, Entity entity)
	{
		var _writer = _predictionWriter;
		if (_writer == null) return;
		int entityId = entity.Id;

		bool hasPosition = world.Has<Position>(entity);
		Vector3 predicted = hasPosition ? world.Get<Position>(entity).Value : Vector3.Zero;
		Vector3 previous = world.Has<PreviousPosition>(entity) ? world.Get<PreviousPosition>(entity).Value : predicted;
		bool hasPredictedOrder = world.Has<PredictedMoveTo>(entity);
		bool hasOrder = hasPredictedOrder || world.Has<MoveTo>(entity);
		Vector3 moveTarget = hasPredictedOrder
			? world.Get<PredictedMoveTo>(entity).Target
			: (hasOrder ? world.Get<MoveTo>(entity).Target : Vector3.Zero);
		int ownerPlayerIndex = world.Has<UnitOwnerPlayer>(entity) ? world.Get<UnitOwnerPlayer>(entity).PlayerIndex : -1;
		bool hasPathFollow = world.Has<PathFollow>(entity);
		PathFollow pathFollow = hasPathFollow ? world.Get<PathFollow>(entity) : default;

		prediction.GetAuthoritativeSample(entityId, out Vector3 authoritative, out int authoritativeSequence);
		int serverEntityId = GameContext.NetworkService?.GetServerEntityId(entityId) ?? -1;

		_writer.Write(entityId);
		_writer.Write(serverEntityId);
		_writer.Write(prediction.ResolveCommandId(entityId));
		_writer.Write(ownerPlayerIndex);
		_writer.Write(localPlayerIndex);
		_writer.Write(hasPredictedOrder);
		_writer.Write(hasOrder);
		_writer.Write(hasPathFollow);
		_writer.Write(pathFollow.CurrentWaypointIndex);
		_writer.Write(pathFollow.WaypointCount);
		WriteVector(_writer, predicted);
		WriteVector(_writer, previous);
		WriteVector(_writer, authoritative);
		WriteVector(_writer, moveTarget);
		_writer.Write(authoritativeSequence);

		int waypointCount = hasPathFollow ? Math.Min(pathFollow.WaypointCount, MaximumRecordedWaypoints) : 0;
		_writer.Write(waypointCount);
		for (int waypointIndex = 0; waypointIndex < waypointCount; waypointIndex++)
		{
			WriteVector(_writer, pathFollow.Waypoints[waypointIndex]);
		}
	}

	private static void CountRemoteUnitAction(Entity entity, ref Position position, ref UnitOwnerPlayer owner)
	{
		MovementPredictionService? prediction = _recordingPrediction;
		if (prediction != null && prediction.IsEntityTracked(entity.Id)) return;
		_remoteRecordCount++;
	}

	private static void WriteRemoteUnitAction(Entity entity, ref Position position, ref UnitOwnerPlayer owner)
	{
		EcsWorld? world = _recordingWorld;
		MovementPredictionService? prediction = _recordingPrediction;
		if (world == null || prediction == null) return;
		if (prediction.IsEntityTracked(entity.Id)) return;
		WriteUnitRecord(world, prediction, _recordingLocalPlayerIndex, entity);
	}

	public static void RecordCommandRegistered(int commandId, IReadOnlyList<int> entityIds)
	{
		var _writer = _predictionWriter;
		if (_writer == null) return;
		_writer.Write(KindCommandRegistered);
		_writer.Write(commandId);
		_writer.Write(_lastClientTick);
		_writer.Write(entityIds.Count);
		for (int index = 0; index < entityIds.Count; index++) _writer.Write(entityIds[index]);
	}

	public static void RecordCommandAccepted(int commandId)
	{
		var _writer = _predictionWriter;
		if (_writer == null) return;
		_writer.Write(KindCommandAccepted);
		_writer.Write(commandId);
		_writer.Write(_lastClientTick);
	}

	public static void RecordRejection(int commandId, string reason)
	{
		if (_predictionWriter != null)
		{
			_predictionWriter.Write(KindRejection);
			_predictionWriter.Write(false);
			_predictionWriter.Write(commandId);
			_predictionWriter.Write(_lastClientTick);
			_predictionWriter.Write(reason);
			_predictionWriter.Flush();
		}

		if (_authorityWriter != null)
		{
			_authorityWriter.Write(KindRejection);
			_authorityWriter.Write(true);
			_authorityWriter.Write(commandId);
			_authorityWriter.Write(_lastClientTick);
			_authorityWriter.Write(reason);
			_authorityWriter.Flush();
		}
	}

	public static void RecordRollback(int commandId, int entityId, string reason, int sequence)
	{
		var _writer = _predictionWriter;
		if (_writer == null) return;
		_writer.Write(KindRollback);
		_writer.Write(commandId);
		_writer.Write(entityId);
		_writer.Write(sequence);
		_writer.Write(_lastClientTick);
		_writer.Write(reason);
		_writer.Flush();
	}

	public static void RecordSnap(int entityId, int sequence, Vector3 predicted, Vector3 authoritative, bool explained, string reason)
	{
		var _writer = _predictionWriter;
		if (_writer == null) return;
		_writer.Write(KindSnap);
		_writer.Write(entityId);
		_writer.Write(sequence);
		_writer.Write(_lastClientTick);
		_writer.Write(explained);
		WriteVector(_writer, predicted);
		WriteVector(_writer, authoritative);
		_writer.Write(reason);
		_writer.Flush();
	}

	public static void RecordCorrection(int entityId, Vector3 predicted, Vector3 corrected, Vector3 authoritative, int sequence)
	{
		var _writer = _predictionWriter;
		if (_writer == null) return;
		_writer.Write(KindCorrection);
		_writer.Write(entityId);
		_writer.Write(sequence);
		_writer.Write(_lastClientTick);
		WriteVector(_writer, predicted);
		WriteVector(_writer, corrected);
		WriteVector(_writer, authoritative);
	}

	public static void RecordPredictionStopped(int entityId, string reason)
	{
		var _writer = _predictionWriter;
		if (_writer == null) return;
		_writer.Write(KindPredictionStopped);
		_writer.Write(entityId);
		_writer.Write(_lastClientTick);
		_writer.Write(reason);
	}

	public static void RecordScriptOrder(int entityId, Vector3 target, bool localOwned)
	{
		var _writer = _predictionWriter;
		if (_writer == null) return;
		_writer.Write(KindScriptOrder);
		_writer.Write(entityId);
		_writer.Write(_lastClientTick);
		_writer.Write(localOwned);
		WriteVector(_writer, target);
	}

	public static void RecordScriptUpdate()
	{
		var _writer = _predictionWriter;
		if (_writer == null) return;
		_writer.Write(KindScriptUpdate);
		_writer.Write(_lastClientTick);
	}

	public static void RecordScenarioOrder(string stage, int tick, int entityId, Vector3 target)
	{
		var _writer = _predictionWriter;
		if (_writer == null) return;
		_writer.Write(KindScenario);
		_writer.Write(stage);
		_writer.Write(tick);
		_writer.Write(entityId);
		WriteVector(_writer, target);
		_writer.Flush();
	}

	public static void RecordAllocation(int tick, long predictionAllocatedBytes, long sharedMovementAllocatedBytes)
	{
		var _writer = _predictionWriter;
		if (_writer == null) return;
		_writer.Write(KindAllocation);
		_writer.Write(tick);
		_writer.Write(predictionAllocatedBytes);
		_writer.Write(sharedMovementAllocatedBytes);
	}

	public static void RecordSnapshotOrderingViolation(int rejectedSequence, int lastAppliedSequence)
	{
		var _writer = _predictionWriter;
		if (_writer == null) return;
		_writer.Write(KindSnapshotOrderingViolation);
		_writer.Write(rejectedSequence);
		_writer.Write(lastAppliedSequence);
		_writer.Write(_lastClientTick);
		_writer.Flush();
	}

	public static void RecordHostSnapshot(int sequence, EcsWorld world, List<Unit3D> units)
	{
		var _writer = _authorityWriter;
		if (_writer == null) return;
		if (units == null) return;

		int recordedCount = 0;
		for (int index = 0; index < units.Count; index++)
		{
			var unit = units[index];
			if (unit == null || !world.IsAlive(unit.Entity)) continue;
			if (!ShouldRecordHostUnit(unit)) continue;
			recordedCount++;
		}

		_writer.Write(KindHostSnapshot);
		_writer.Write(sequence);
		_writer.Write(recordedCount);
		for (int index = 0; index < units.Count; index++)
		{
			var unit = units[index];
			if (unit == null || !world.IsAlive(unit.Entity)) continue;
			if (!ShouldRecordHostUnit(unit)) continue;

			var entity = unit.Entity;
			Vector3 position = world.Has<Position>(entity) ? world.Get<Position>(entity).Value : Vector3.Zero;
			bool hasMoveTo = world.Has<MoveTo>(entity);
			Vector3 moveTarget = hasMoveTo ? world.Get<MoveTo>(entity).Target : Vector3.Zero;
			bool hasPathFollow = world.Has<PathFollow>(entity);
			PathFollow pathFollow = hasPathFollow ? world.Get<PathFollow>(entity) : default;
			int ownerPlayerIndex = world.Has<UnitOwnerPlayer>(entity) ? world.Get<UnitOwnerPlayer>(entity).PlayerIndex : -1;
			int serverEntityId = GameContext.NetworkService?.GetServerEntityId(entity.Id) ?? entity.Id;

			_writer.Write(serverEntityId);
			_writer.Write(ownerPlayerIndex);
			_writer.Write(hasMoveTo);
			_writer.Write(hasPathFollow);
			_writer.Write(pathFollow.CurrentWaypointIndex);
			_writer.Write(pathFollow.WaypointCount);
			WriteVector(_writer, position);
			WriteVector(_writer, moveTarget);

			int waypointCount = hasPathFollow ? Math.Min(pathFollow.WaypointCount, MaximumRecordedWaypoints) : 0;
			_writer.Write(waypointCount);
			for (int waypointIndex = 0; waypointIndex < waypointCount; waypointIndex++)
			{
				WriteVector(_writer, pathFollow.Waypoints[waypointIndex]);
			}
		}

		_writer.Flush();
	}

	private static bool ShouldRecordHostUnit(Unit3D unit)
	{
		if (unit.IsBuilding) return false;
		if (GameContext.World.Has<Dead>(unit.Entity)) return false;
		return GameContext.NetworkService?.GetServerEntityId(unit.Entity.Id) > 0;
	}

	private static void WriteVector(BinaryWriter writer, Vector3 value)
	{
		writer.Write(value.X);
		writer.Write(value.Y);
		writer.Write(value.Z);
	}
}
