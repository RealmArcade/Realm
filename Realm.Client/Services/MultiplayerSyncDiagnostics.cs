using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using Godot;
using Realm.Client.Core;
using Realm.Ecs;
using Realm.Ecs.Components.Combat;
using Realm.Ecs.Components.Core;
using Realm.Ecs.Components.Meta;
using Realm.Ecs.Components.Movement;
using Realm.Ecs.Components.Resources;
using Realm.Ecs.Components.Tags;
using Realm.Ecs.Replication;
using Realm.Ecs.Services;
using FileAccess = System.IO.FileAccess;

namespace Realm.Client.Services;

public static class MultiplayerSyncDiagnostics
{
	private const uint FileMagic = 0x504E5352;
	private const int FileVersion = 4;
	private const int BaselineIntervalTicks = 30;
	private const int PlayerSlotCount = 25;

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
	private static int _presentationAnchorSequence = -1;
	private static int _fingerprintMismatchCount;
	private static string _fingerprintMismatchSummary = string.Empty;
	private static readonly HashSet<int> _clientSeenSequences = new();
	private static readonly List<ComponentDigest> _digestBuffer = new();
	private static readonly List<(int UniqueId, Entity Entity)> _lootBuffer = new();
	private static readonly Dictionary<int, int> _entityReferencePayloadOffsets = new()
	{
		{ ComponentType<AttackTarget>.Id, 0 },
		{ ComponentType<LastAttacker>.Id, 0 },
		{ ComponentType<Follow>.Id, 0 },
		{ ComponentType<BuildTask>.Id, 0 }
	};
	private static byte[] _codecScratch = new byte[1024];

	public static void RecordClientSeenSequence(int sequence)
	{
		_clientSeenSequences.Add(sequence);
	}

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
	public static int PresentationWindowSequences { get; set; } = 60;
	public static int PresentationSettleSequences { get; set; } = 15;
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
		_presentationAnchorSequence = -1;
		_fingerprintMismatchCount = 0;
		_fingerprintMismatchSummary = string.Empty;
		_clientSeenSequences.Clear();
		_digestBuffer.Clear();

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
		if (!_isHost && _clientSeenSequences.Count > 0)
		{
			try
			{
				int[] seenSequences = new int[_clientSeenSequences.Count];
				_clientSeenSequences.CopyTo(seenSequences);
				Array.Sort(seenSequences);
				using FileStream seenStream = new(Path.Combine(_outputDirectory, "ECS_client_seen_sequences.bin"), FileMode.Create, FileAccess.Write, FileShare.Read);
				using BinaryWriter seenWriter = new(seenStream);
				seenWriter.Write(seenSequences.Length);
				for (int index = 0; index < seenSequences.Length; index++) seenWriter.Write(seenSequences[index]);
			}
			catch (IOException)
			{
			}
		}

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
				RecordAuthoritativeUnitState(unit, out System.Numerics.Vector3 position, out float rotationY, out float currentHp, out float maxHp);
				writer.Write(unit.EntityId);
				writer.Write(unit.OwnerPlayerIndex);
				writer.Write(position.X);
				writer.Write(position.Y);
				writer.Write(position.Z);
				writer.Write(rotationY);
				writer.Write(currentHp);
				writer.Write(maxHp);
				writer.Write(BuildFlags(unit.IsDead, unit.IsBuilding, unit.IsDetailed, IsVisibleToRemoteClient(unit.EntityId)));
				WriteComponentDigests(writer, GameContext.World, new Entity(unit.EntityId), ReplicatedScope.Unit, null);
			}

			AppendPlayerScopeState(writer, GameContext.World, null);
			AppendNavigationState(writer);
		});

		AppendRecord(record, snapshot.Sequence, snapshot.IsBaseline);
	}

	private static void RecordAuthoritativeUnitState(UnitSnapshot unit, out System.Numerics.Vector3 position, out float rotationY, out float currentHp, out float maxHp)
	{
		EcsWorld? world = GameContext.World;
		Entity entity = new(unit.EntityId);
		position = new System.Numerics.Vector3(unit.Position.X, unit.Position.Y, unit.Position.Z);
		rotationY = unit.RotationY;
		currentHp = unit.CurrentHp;
		maxHp = unit.MaxHp;

		if (world == null || !world.IsAlive(entity)) return;

		if (world.Has<Position>(entity)) position = world.Get<Position>(entity).Value;
		if (world.Has<RotationY>(entity)) rotationY = world.Get<RotationY>(entity).Value;
		if (!world.Has<Health>(entity)) return;

		Health health = world.Get<Health>(entity);
		currentHp = health.Current;
		maxHp = health.Max;
	}

	private static bool IsVisibleToRemoteClient(int serverEntityId)
	{
		NetworkService? networkService = GameContext.NetworkService;
		if (networkService == null) return true;
		return networkService.IsServerEntityVisibleToAnyRemoteClient(serverEntityId);
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

			units.Add(new ClientUnitState(kvp.Value, localEntity.Id, ownerPlayerIndex, target, currentHp, maxHp, isDead, isBuilding));
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
				writer.Write(BuildFlags(unit.IsDead, unit.IsBuilding, true, true));
				WriteComponentDigests(writer, world, new Entity(unit.LocalEntityId), ReplicatedScope.Unit, mapping.ClientToServerEntityMap);
			}

			AppendPlayerScopeState(writer, world, mapping.ClientToServerEntityMap);
			AppendNavigationState(writer);
		});

		AppendRecord(record, sequence, isBaseline);
	}

	private static void WriteComponentDigests(BinaryWriter writer, EcsWorld? world, Entity entity, ReplicatedScope scope, Dictionary<int, int>? clientToServerEntityMap)
	{
		_digestBuffer.Clear();

		if (world != null && entity != Entity.Null && world.IsAlive(entity))
		{
			IReadOnlyList<IReplicatedComponentCodec> codecs = ReplicatedComponents.All;
			for (int index = 0; index < codecs.Count; index++)
			{
				IReplicatedComponentCodec codec = codecs[index];
				if (codec.Scope != scope) continue;

				int size = codec.SizeInBytes;
				if (_codecScratch.Length < size) Array.Resize(ref _codecScratch, size);
				Span<byte> payload = _codecScratch.AsSpan(0, size);
				payload.Clear();
				if (!codec.TryWrite(world, entity, payload)) continue;

				NormalizeEntityReferences(payload, codec.ComponentTypeId, clientToServerEntityMap);
				_digestBuffer.Add(new ComponentDigest(codec.ComponentTypeId, HashPayload(payload)));
			}
		}

		_digestBuffer.Sort(static (left, right) => left.ComponentTypeId.CompareTo(right.ComponentTypeId));
		writer.Write(_digestBuffer.Count);
		for (int index = 0; index < _digestBuffer.Count; index++)
		{
			writer.Write(_digestBuffer[index].ComponentTypeId);
			writer.Write(_digestBuffer[index].Hash);
		}
	}

	private static void NormalizeEntityReferences(Span<byte> payload, int componentTypeId, Dictionary<int, int>? clientToServerEntityMap)
	{
		if (clientToServerEntityMap == null) return;
		if (!_entityReferencePayloadOffsets.TryGetValue(componentTypeId, out int offset)) return;
		if (payload.Length < offset + sizeof(int)) return;

		int localEntityId = MemoryMarshal.Read<int>(payload.Slice(offset, sizeof(int)));
		int serverEntityId = -1;
		if (localEntityId >= 0 && !clientToServerEntityMap.TryGetValue(localEntityId, out serverEntityId)) serverEntityId = -1;
		MemoryMarshal.Write(payload.Slice(offset, sizeof(int)), in serverEntityId);
	}

	private static uint HashPayload(ReadOnlySpan<byte> payload)
	{
		uint hash = 2166136261u;
		for (int index = 0; index < payload.Length; index++)
		{
			hash ^= payload[index];
			hash *= 16777619u;
		}

		return hash;
	}

	private static void AppendPlayerScopeState(BinaryWriter writer, EcsWorld? world, Dictionary<int, int>? clientToServerEntityMap)
	{
		Entity worldEntity = ECSContext.WorldEntity;
		Entity[]? playerEntities = null;
		if (world != null && worldEntity != Entity.Null && world.IsAlive(worldEntity) && world.Has<NetworkMappingState>(worldEntity))
		{
			playerEntities = world.Get<NetworkMappingState>(worldEntity).PlayerEntities;
		}

		int playerCount = playerEntities?.Length ?? 0;
		if (playerCount > PlayerSlotCount) playerCount = PlayerSlotCount;
		writer.Write(playerCount);
		for (int playerIndex = 0; playerIndex < playerCount; playerIndex++)
		{
			writer.Write(playerIndex);
			WriteComponentDigests(writer, world, playerEntities![playerIndex], ReplicatedScope.Player, clientToServerEntityMap);
		}

		WriteComponentDigests(writer, world, worldEntity, ReplicatedScope.World, clientToServerEntityMap);
		AppendLootState(writer, world, clientToServerEntityMap);
	}

	private static void AppendLootState(BinaryWriter writer, EcsWorld? world, Dictionary<int, int>? clientToServerEntityMap)
	{
		_lootBuffer.Clear();
		GameHost? host = GameContext.Host;
		if (host != null)
		{
			List<LootItem3D> lootItems = host.AllLootItems;
			for (int index = 0; index < lootItems.Count; index++)
			{
				LootItem3D loot = lootItems[index];
				if (loot == null || !GodotObject.IsInstanceValid(loot) || loot.IsDespawned) continue;
				if (loot.Entity == Entity.Null) continue;
				_lootBuffer.Add((loot.UniqueId, loot.Entity));
			}
		}

		_lootBuffer.Sort(static (left, right) => left.UniqueId.CompareTo(right.UniqueId));
		writer.Write(_lootBuffer.Count);
		for (int index = 0; index < _lootBuffer.Count; index++)
		{
			writer.Write(_lootBuffer[index].UniqueId);
			WriteComponentDigests(writer, world, _lootBuffer[index].Entity, ReplicatedScope.Unit, clientToServerEntityMap);
		}
	}

	private static void AppendNavigationState(BinaryWriter writer)
	{
		NavigationParityDigest digest = NavigationParityDigest.Capture(GameContext.World, ECSContext.WorldEntity);
		writer.Write(digest.HasTerrainState);
		writer.Write(digest.HasNavMesh);
		writer.Write(digest.Width);
		writer.Write(digest.Depth);
		writer.Write(digest.PathingHash);
		writer.Write(digest.WalkableCellCount);
		writer.Write(digest.BlockedCellCount);
		writer.Write(digest.PolyCount);
		writer.Write(digest.GroundPolyCount);
		writer.Write(digest.PolyHash);
	}

	public static void RecordClientPresentation()
	{
		if (_presentationWriter == null || _isHost) return;

		GameHost? gameHost = GameContext.Host;
		if (gameHost == null) return;

		EcsWorld? world = GameContext.World;
		if (world == null) return;
		Entity worldEntity = ECSContext.WorldEntity;
		if (worldEntity == Entity.Null || !world.IsAlive(worldEntity)) return;
		if (!world.Has<NetworkState>(worldEntity) || !world.Has<NetworkMappingState>(worldEntity)) return;

		int sequence = GameContext.NetworkService.LastAppliedSnapshotSequenceForDiagnostics;
		if (sequence < 0 || sequence == _lastPresentedSequence) return;

		if (_presentationWindowStartTime < 0f)
		{
			if (sequence % BaselineIntervalTicks != 0) return;
			_presentationWindowStartTime = gameHost.GameElapsedTime;
			_presentationAnchorSequence = sequence;
		}

		if (sequence - _presentationAnchorSequence < PresentationSettleSequences) return;
		if (sequence - _presentationAnchorSequence > PresentationWindowSequences) return;

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

			bool isPredicted = GameContext.MovementPredictionService != null
				&& GameContext.MovementPredictionService.IsEntityTracked(localEntity.Id);
			units.Add(new PresentedUnitState(kvp.Value, ownerPlayerIndex, unit3D.GlobalPosition, unit3D.GlobalRotation.Y, currentHp, maxHp, isDead, isBuilding, isPredicted));
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
				writer.Write(BuildFlags(unit.IsDead, unit.IsBuilding, false, true, unit.IsPredicted));
				writer.Write(0);
			}

			writer.Write(0);
			writer.Write(0);
			writer.Write(0);
			AppendNavigationState(writer);
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

	private static byte BuildFlags(bool isDead, bool isBuilding, bool isDetailed, bool isVisibleToRemoteClient, bool isLocallyPredicted = false)
	{
		byte flags = 0;
		if (isDead) flags |= 1;
		if (isBuilding) flags |= 2;
		if (isDetailed) flags |= 4;
		if (isVisibleToRemoteClient) flags |= 8;
		if (isLocallyPredicted) flags |= 16;
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

	private readonly struct ComponentDigest
	{
		public readonly int ComponentTypeId;
		public readonly uint Hash;

		public ComponentDigest(int componentTypeId, uint hash)
		{
			ComponentTypeId = componentTypeId;
			Hash = hash;
		}
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
		public readonly bool IsPredicted;

		public PresentedUnitState(int serverEntityId, int ownerPlayerIndex, Vector3 position, float rotationY, float currentHp, float maxHp, bool isDead, bool isBuilding, bool isPredicted)
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
			IsPredicted = isPredicted;
		}
	}

	private readonly struct ClientUnitState
	{
		public readonly int ServerEntityId;
		public readonly int LocalEntityId;
		public readonly int OwnerPlayerIndex;
		public readonly float PositionX;
		public readonly float PositionY;
		public readonly float PositionZ;
		public readonly float RotationY;
		public readonly float CurrentHp;
		public readonly float MaxHp;
		public readonly bool IsDead;
		public readonly bool IsBuilding;

		public ClientUnitState(int serverEntityId, int localEntityId, int ownerPlayerIndex, InterpolationTarget target, float currentHp, float maxHp, bool isDead, bool isBuilding)
		{
			ServerEntityId = serverEntityId;
			LocalEntityId = localEntityId;
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
