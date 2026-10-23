using System;
using System.Collections.Generic;
using System.IO;
using Godot;
using Realm.Client.Core;
using Realm.Ecs;
using Realm.Ecs.Components.Core;
using Realm.Ecs.Components.Movement;
using Realm.Ecs.Components.Tags;
using Realm.Ecs.Services;
using FileAccess = System.IO.FileAccess;

namespace Realm.Client.Services;

public static class MultiplayerSyncDiagnostics
{
	private const uint FileMagic = 0x504E5352;
	private const int FileVersion = 1;
	private const int BaselineIntervalTicks = 30;

	private static BinaryWriter? _writer;
	private static byte[] _fileHeader = Array.Empty<byte>();
	private static BinaryWriter? _presentationWriter;
	private static byte[] _presentationHeader = Array.Empty<byte>();
	private static string _outputDirectory = string.Empty;
	private static bool _isHost;
	private static int _writtenRecordCount;
	private static int _lastRecordedSequence = -1;
	private static int _lastPresentedSequence = -1;
	private static float _presentationWindowStartTime = -1f;
	private static int _fingerprintMismatchCount;
	private static string _fingerprintMismatchSummary = string.Empty;

	public static int FingerprintMismatchCount => _fingerprintMismatchCount;
	public static string FingerprintMismatchSummary => _fingerprintMismatchSummary;

	private static int _ecsDriftCount;
	private static string _ecsDriftSummary = string.Empty;

	public static int EcsDriftCount => _ecsDriftCount;
	public static string EcsDriftSummary => _ecsDriftSummary;

	public static void RecordEcsDrift(int playerSlot, string componentName)
	{
		_ecsDriftCount++;
		if (_ecsDriftCount > 40) return;
		_ecsDriftSummary += $"{playerSlot}:{componentName}; ";
	}

	public static void RecordFingerprintMismatch(int sequence, uint hostFingerprint, uint clientFingerprint)
	{
		_fingerprintMismatchCount++;
		if (_fingerprintMismatchCount > 20) return;
		_fingerprintMismatchSummary += $"{sequence}:{hostFingerprint:x8}:{clientFingerprint:x8}; ";
	}

	public static float PresentationWindowSeconds { get; set; } = 2f;
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
		_lastPresentedSequence = -1;
		_presentationWindowStartTime = -1f;
		_fingerprintMismatchCount = 0;
		_fingerprintMismatchSummary = string.Empty;

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

		if (isHost) return;

		string presentationPath = Path.Combine(outputDirectory, "ECS_client_presentation.bin");
		using (MemoryStream presentationHeaderStream = new())
		{
			using (BinaryWriter presentationHeaderWriter = new(presentationHeaderStream, System.Text.Encoding.UTF8, leaveOpen: true))
			{
				presentationHeaderWriter.Write(FileMagic);
				presentationHeaderWriter.Write(FileVersion);
				presentationHeaderWriter.Write(false);
			}

			_presentationHeader = presentationHeaderStream.ToArray();
		}

		FileStream presentationStream = new(presentationPath, FileMode.Create, FileAccess.Write, FileShare.Read, 1 << 16);
		_presentationWriter = new BinaryWriter(presentationStream);
		_presentationWriter.Write(_presentationHeader);
		_presentationWriter.Flush();
	}

	public static void Stop()
	{
		if (_presentationWriter != null)
		{
			_presentationWriter.Flush();
			_presentationWriter.Dispose();
			_presentationWriter = null;
		}

		if (_writer == null) return;
		_writer.Flush();
		_writer.Dispose();
		_writer = null;

		if (string.IsNullOrEmpty(_outputDirectory)) return;
		try
		{
			string summary = $"fingerprint_mismatches={_fingerprintMismatchCount}\nshadow_baselines={ShadowClientVerifier.VerifiedBaselines}\nshadow_replayed_deltas={ShadowClientVerifier.ReplayedDeltaCount}\nshadow_structural_failures={ShadowClientVerifier.StructuralFailures}\nshadow_fingerprint_failures={ShadowClientVerifier.FingerprintFailures}\n{_fingerprintMismatchSummary}\n";
			File.WriteAllText(Path.Combine(_outputDirectory, _isHost ? "ECS_host_replication.txt" : "ECS_client_replication.txt"), summary);
		}
		catch (IOException)
		{
		}
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

	public static void RecordClientPresentation()
	{
		if (_presentationWriter == null || _isHost) return;

		GameHost? gameHost = GameHost.Instance;
		if (gameHost == null) return;
		if (_presentationWindowStartTime < 0f)
		{
			_presentationWindowStartTime = gameHost.GameElapsedTime;
		}

		if (gameHost.GameElapsedTime - _presentationWindowStartTime > PresentationWindowSeconds) return;

		EcsWorld? world = GameContext.World;
		if (world == null) return;
		Entity worldEntity = ECSContext.WorldEntity;
		if (worldEntity == Entity.Null || !world.IsAlive(worldEntity)) return;
		if (!world.Has<NetworkState>(worldEntity) || !world.Has<NetworkMappingState>(worldEntity)) return;

		int sequence = GameContext.NetworkService.LastAppliedSnapshotSequenceForDiagnostics;
		if (sequence < 0 || sequence == _lastPresentedSequence) return;

		NetworkMappingState mapping = world.Get<NetworkMappingState>(worldEntity);
		var units = new List<PresentedUnitState>();
		foreach (var kvp in mapping.ClientToServerEntityMap)
		{
			Entity localEntity = new(kvp.Key);
			if (!world.IsAlive(localEntity)) continue;
			if (!GameHost.EntityIdToUnit3D.TryGetValue(localEntity.Id, out Unit3D? unit3D)) continue;
			if (!GodotObject.IsInstanceValid(unit3D)) continue;

			float currentHp = world.Has<Health>(localEntity) ? world.Get<Health>(localEntity).Current : 0f;
			float maxHp = world.Has<Health>(localEntity) ? world.Get<Health>(localEntity).Max : 0f;
			bool isDead = world.Has<Dead>(localEntity);
			bool isBuilding = world.Has<Building>(localEntity);
			int ownerPlayerIndex = ResolveOwnerPlayerIndex(world, localEntity);

			units.Add(new PresentedUnitState(kvp.Value, ownerPlayerIndex, unit3D.GlobalPosition, unit3D.GlobalRotation.Y, currentHp, maxHp, isDead, isBuilding));
		}

		units.Sort((left, right) => left.ServerEntityId.CompareTo(right.ServerEntityId));

		bool isBaseline = sequence % BaselineIntervalTicks == 0;
		byte[] record = BuildRecord(sequence, isBaseline, writer =>
		{
			writer.Write(units.Count);
			foreach (PresentedUnitState unit in units)
			{
				writer.Write(unit.ServerEntityId);
				writer.Write(unit.OwnerPlayerIndex);
				writer.Write(unit.PositionX);
				writer.Write(unit.PositionY);
				writer.Write(unit.PositionZ);
				writer.Write(unit.RotationY);
				writer.Write(unit.CurrentHp);
				writer.Write(unit.MaxHp);
				writer.Write(BuildFlags(unit.IsDead, unit.IsBuilding, false));
			}
		});

		_presentationWriter.Write(record);
		_presentationWriter.Flush();
		_lastPresentedSequence = sequence;
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

	private readonly struct PresentedUnitState
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

		public PresentedUnitState(int serverEntityId, int ownerPlayerIndex, Vector3 position, float rotationY, float currentHp, float maxHp, bool isDead, bool isBuilding)
		{
			ServerEntityId = serverEntityId;
			OwnerPlayerIndex = ownerPlayerIndex;
			PositionX = position.X;
			PositionY = position.Y;
			PositionZ = position.Z;
			RotationY = rotationY;
			CurrentHp = currentHp;
			MaxHp = maxHp;
			IsDead = isDead;
			IsBuilding = isBuilding;
		}
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
