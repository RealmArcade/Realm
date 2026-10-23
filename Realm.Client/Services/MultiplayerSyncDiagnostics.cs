using System;
using System.Collections.Generic;
using System.IO;
using Realm.Ecs;
using Realm.Ecs.Components.Core;
using Realm.Ecs.Components.Movement;
using Realm.Ecs.Components.Tags;
using Realm.Ecs.Services;

namespace Realm.Client.Services;

public static class MultiplayerSyncDiagnostics
{
	private const uint FileMagic = 0x504E5352;
	private const int FileVersion = 1;
	private const int BaselineIntervalTicks = 30;

	private static BinaryWriter? _writer;
	private static byte[] _fileHeader = Array.Empty<byte>();
	private static string _outputDirectory = string.Empty;
	private static bool _isHost;
	private static int _writtenRecordCount;
	private static int _lastRecordedSequence = -1;

	public static bool IsRecording => _writer != null;
	public static int WrittenRecordCount => _writtenRecordCount;
	public static string OutputDirectory => _outputDirectory;

	public static void Start(string outputDirectory, bool isHost)
	{
		Stop();
		Directory.CreateDirectory(outputDirectory);
		_outputDirectory = outputDirectory;
		_isHost = isHost;
		_writtenRecordCount = 0;
		_lastRecordedSequence = -1;

		string path = Path.Combine(outputDirectory, isHost ? "ECS_host_sync.bin" : "ECS_client_sync.bin");
		using (MemoryStream headerStream = new())
		{
			using (BinaryWriter headerWriter = new(headerStream, System.Text.Encoding.UTF8, leaveOpen: true))
			{
				headerWriter.Write(FileMagic);
				headerWriter.Write(FileVersion);
				headerWriter.Write(isHost);
			}

			_fileHeader = headerStream.ToArray();
		}

		FileStream stream = new(path, FileMode.Create, FileAccess.Write, FileShare.Read, 1 << 16);
		_writer = new BinaryWriter(stream);
		_writer.Write(_fileHeader);
		_writer.Flush();
	}

	public static void Stop()
	{
		if (_writer == null) return;
		_writer.Flush();
		_writer.Dispose();
		_writer = null;
	}

	public static void RecordHostSnapshot(WorldSnapshot snapshot)
	{
		if (_writer == null || !_isHost) return;
		if (snapshot.Units == null) return;

		List<UnitSnapshot> units = snapshot.Units;
		byte[] record = BuildRecord(snapshot.Sequence, snapshot.IsBaseline, writer =>
		{
			List<int> orderedIndices = new(units.Count);
			for (int i = 0; i < units.Count; i++)
			{
				orderedIndices.Add(i);
			}

			orderedIndices.Sort((left, right) => units[left].EntityId.CompareTo(units[right].EntityId));

			writer.Write(orderedIndices.Count);
			foreach (int index in orderedIndices)
			{
				UnitSnapshot unit = units[index];
				writer.Write(unit.EntityId);
				writer.Write(unit.OwnerPlayerIndex);
				writer.Write(unit.Position.X);
				writer.Write(unit.Position.Y);
				writer.Write(unit.Position.Z);
				writer.Write(unit.RotationY);
				writer.Write(unit.CurrentHp);
				writer.Write(unit.MaxHp);
				writer.Write(BuildFlags(unit.IsDead, unit.IsBuilding, unit.IsDetailed));
			}
		});

		AppendRecord(record, snapshot.Sequence, snapshot.IsBaseline);
	}

	public static void RecordClientSnapshot()
	{
		if (_writer == null || _isHost) return;

		EcsWorld? world = GameContext.World;
		if (world == null) return;
		Entity worldEntity = ECSContext.WorldEntity;
		if (worldEntity == Entity.Null || !world.IsAlive(worldEntity)) return;
		if (!world.Has<NetworkState>(worldEntity) || !world.Has<NetworkMappingState>(worldEntity)) return;

		int sequence = GameContext.NetworkService.LastAppliedSnapshotSequenceForDiagnostics;
		if (sequence < 0 || sequence == _lastRecordedSequence) return;

		NetworkMappingState mapping = world.Get<NetworkMappingState>(worldEntity);
		var units = new List<ClientUnitState>();
		foreach (var kvp in mapping.ClientToServerEntityMap)
		{
			Entity localEntity = new(kvp.Key);
			if (!world.IsAlive(localEntity)) continue;
			if (!world.Has<InterpolationTarget>(localEntity)) continue;

			InterpolationTarget target = world.Get<InterpolationTarget>(localEntity);
			float currentHp = world.Has<Health>(localEntity) ? world.Get<Health>(localEntity).Current : 0f;
			float maxHp = world.Has<Health>(localEntity) ? world.Get<Health>(localEntity).Max : 0f;
			bool isDead = world.Has<Dead>(localEntity);
			bool isBuilding = world.Has<Building>(localEntity);
			int ownerPlayerIndex = ResolveOwnerPlayerIndex(world, localEntity);

			units.Add(new ClientUnitState(kvp.Value, ownerPlayerIndex, target, currentHp, maxHp, isDead, isBuilding));
		}

		units.Sort((left, right) => left.ServerEntityId.CompareTo(right.ServerEntityId));

		bool isBaseline = sequence % BaselineIntervalTicks == 0;
		byte[] record = BuildRecord(sequence, isBaseline, writer =>
		{
			writer.Write(units.Count);
			foreach (ClientUnitState unit in units)
			{
				writer.Write(unit.ServerEntityId);
				writer.Write(unit.OwnerPlayerIndex);
				writer.Write(unit.PositionX);
				writer.Write(unit.PositionY);
				writer.Write(unit.PositionZ);
				writer.Write(unit.RotationY);
				writer.Write(unit.CurrentHp);
				writer.Write(unit.MaxHp);
				writer.Write(BuildFlags(unit.IsDead, unit.IsBuilding, true));
			}
		});

		AppendRecord(record, sequence, isBaseline);
	}

	private static int ResolveOwnerPlayerIndex(EcsWorld world, Entity localEntity)
	{
		if (!world.Has<UnitOwnerPlayer>(localEntity)) return -1;
		return world.Get<UnitOwnerPlayer>(localEntity).PlayerIndex;
	}

	private static byte BuildFlags(bool isDead, bool isBuilding, bool isDetailed)
	{
		byte flags = 0;
		if (isDead) flags |= 1;
		if (isBuilding) flags |= 2;
		if (isDetailed) flags |= 4;
		return flags;
	}

	private static byte[] BuildRecord(int sequence, bool isBaseline, Action<BinaryWriter> writeUnits)
	{
		using MemoryStream stream = new(4096);
		using (BinaryWriter writer = new(stream, System.Text.Encoding.UTF8, leaveOpen: true))
		{
			writer.Write(sequence);
			writer.Write(isBaseline);
			writeUnits(writer);
		}

		return stream.ToArray();
	}

	private static void AppendRecord(byte[] record, int sequence, bool isBaseline)
	{
		if (_writer == null) return;
		_writer.Write(record);
		_writer.Flush();
		_writtenRecordCount++;
		_lastRecordedSequence = sequence;

		if (!isBaseline) return;

		string fileName = _isHost ? $"ECS_host_snapshot_{sequence}.bin" : $"ECS_client_snapshot_{sequence}.bin";
		byte[] snapshotFile = new byte[_fileHeader.Length + record.Length];
		_fileHeader.CopyTo(snapshotFile, 0);
		record.CopyTo(snapshotFile, _fileHeader.Length);
		File.WriteAllBytes(Path.Combine(_outputDirectory, fileName), snapshotFile);
	}

	private readonly struct ClientUnitState
	{
		public readonly int ServerEntityId;
		public readonly int OwnerPlayerIndex;
		public readonly float PositionX;
		public readonly float PositionY;
		public readonly float PositionZ;
		public readonly float RotationY;
		public readonly float CurrentHp;
		public readonly float MaxHp;
		public readonly bool IsDead;
		public readonly bool IsBuilding;

		public ClientUnitState(int serverEntityId, int ownerPlayerIndex, InterpolationTarget target, float currentHp, float maxHp, bool isDead, bool isBuilding)
		{
			ServerEntityId = serverEntityId;
			OwnerPlayerIndex = ownerPlayerIndex;
			PositionX = target.Position.X;
			PositionY = target.Position.Y;
			PositionZ = target.Position.Z;
			RotationY = target.RotationY;
			CurrentHp = currentHp;
			MaxHp = maxHp;
			IsDead = isDead;
			IsBuilding = isBuilding;
		}
	}
}
