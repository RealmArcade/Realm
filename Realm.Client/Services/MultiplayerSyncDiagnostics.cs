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
	private const int FileVersion = 8;
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

	private static int _missingUnitComponentCount;
	private static string _missingUnitComponentSummary = string.Empty;

	public static int MissingUnitComponentCount => _missingUnitComponentCount;

	/// <summary>
	/// A baseline's presence mask said the authority has a component the peer does not hold. The peer keeps what it has
	/// and reports it rather than inventing the value, because the mask and the component bytes disagree and only the
	/// authority can say which one is right.
	/// </summary>
	public static void RecordMissingUnitComponent(int serverEntityId, string componentName)
	{
		_missingUnitComponentCount++;
		if (_missingUnitComponentCount > 20) return;
		_missingUnitComponentSummary += $"{serverEntityId}:{componentName}; ";
	}


	/// <summary>
	/// How many sequences a unit can go unmentioned by the host before the client's copy of it counts as withheld rather
	/// than merely quiet. A unit the host is telling this peer about is resent within a few sequences of moving, so a
	/// longer silence means the visibility filter stopped describing it - which the client cannot distinguish from the
	/// unit holding still, and which decides whether a placement gap is the protocol doing its job or a defect.
	/// </summary>
	public const int ClientUnitGapAlertSequences = 8;

	private static readonly Dictionary<int, int> _clientUnitLastPresentSequence = new();
	private static readonly Dictionary<int, int> _clientUnitLastUpdateInterval = new();
	private static readonly List<(int EntityId, int FromSequence, int ToSequence)> _clientUnitGaps = new();
	private static int _clientUnitGapCount;

	public static int ClientUnitGapCount => _clientUnitGapCount;

	/// <summary>
	/// Records which units each applied snapshot actually mentioned, so the recorded placement series can be read against
	/// what the host told this peer. A unit absent from the payloads is not a replication mismatch: the client is holding a
	/// last-known state on purpose, and comparing it to the authority would blame the client for the vision filter.
	/// </summary>
	public static void RecordClientSnapshotUnitPresence(int sequence, List<UnitSnapshot> units)
	{
		if (!IsRecording || _isHost) return;

		for (int index = 0; index < units.Count; index++)
		{
			int entityId = units[index].EntityId;
			if (_clientUnitLastPresentSequence.TryGetValue(entityId, out int lastPresentSequence))
			{
				int gap = sequence - lastPresentSequence;
				_clientUnitLastUpdateInterval[entityId] = gap;
				if (gap > ClientUnitGapAlertSequences)
				{
					_clientUnitGapCount++;
					if (_clientUnitGaps.Count < 128) _clientUnitGaps.Add((entityId, lastPresentSequence, sequence));
				}
			}

			_clientUnitLastPresentSequence[entityId] = sequence;
		}
	}

	public static float PresentationWindowSeconds { get; set; } = 2f;
	public static float PresentationSettleSeconds { get; set; } = 0.5f;
	public static bool IsRecording => _writer != null;

	private static bool _lastAppliedWasBaseline;
	private static int _presentationAnchorBaselineSequence = -1;
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
		_clientUnitGapCount = 0;
		_followerDiagnostics.Clear();
		_interpolationStepDetails.Clear();
		_clientUnitLastPresentSequence.Clear();
		_clientUnitLastUpdateInterval.Clear();
		_clientUnitGaps.Clear();
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

		PositionWriteTrace.IsEnabled = true;

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

	private const float FollowerGapAlertMeters = 1.5f;
	private static readonly List<string> _followerDiagnostics = new();
	private static readonly HashSet<int> _interpolationSteppedEntities = new();
	private static int _interpolationSteppedSequence = -1;

	private static readonly List<string> _interpolationStepDetails = new();

	/// <summary>
	/// Records what one interpolation step actually computed for a follower that is far from its target: the position it
	/// started from, the target, the fraction it applied and the value it wrote. A follower that is visited by the step,
	/// computes a new position and still does not move can only be one whose write is undone or whose read is of another
	/// entity, and those two are indistinguishable without these numbers.
	/// </summary>
	public static void RecordInterpolationStepDetail(int entityId, System.Numerics.Vector3 currentPosition, System.Numerics.Vector3 targetPosition, float stepFraction, System.Numerics.Vector3 writtenPosition)
	{
		if (!IsRecording || _interpolationStepDetails.Count >= 240) return;
		_interpolationStepDetails.Add($"step={entityId}:{_interpolationSteppedSequence}:from={currentPosition.X.ToString("F2", System.Globalization.CultureInfo.InvariantCulture)},{currentPosition.Z.ToString("F2", System.Globalization.CultureInfo.InvariantCulture)}:to={targetPosition.X.ToString("F2", System.Globalization.CultureInfo.InvariantCulture)},{targetPosition.Z.ToString("F2", System.Globalization.CultureInfo.InvariantCulture)}:fraction={stepFraction.ToString("F4", System.Globalization.CultureInfo.InvariantCulture)}:wrote={writtenPosition.X.ToString("F2", System.Globalization.CultureInfo.InvariantCulture)},{writtenPosition.Z.ToString("F2", System.Globalization.CultureInfo.InvariantCulture)}");
	}

	private static int _duplicateMappingsReported;
	private static ulong _presentationFrame = ulong.MaxValue;
	private static int _presentationAppliesInFrame;

	/// <summary>
	/// Notes that the interpolation step ran for an entity, so a follower that is not closing its gap can be told apart
	/// from one the step never visited. Recording only: the check is one boolean when no run is being recorded.
	/// </summary>
	public static void RecordInterpolationStep(int entityId, int sequence)
	{
		if (!IsRecording) return;
		if (sequence != _interpolationSteppedSequence)
		{
			_interpolationSteppedSequence = sequence;
			_interpolationSteppedEntities.Clear();
		}

		_interpolationSteppedEntities.Add(entityId);
	}

	/// <summary>
	/// Records a unit whose follower is far from the position it was last told to walk toward, with the target it has and
	/// the local entity that owns it. The placement check reports applied-versus-authority, which cannot distinguish a
	/// client whose follower stopped advancing from one that was never told anything; this separates them, and it records
	/// the local id so a mapping that puts two local entities on one server id becomes visible instead of looking like a
	/// frozen follower.
	/// </summary>
	private static void RecordFollowerDiagnostic(EcsWorld world, int sequence, int localEntityId, int serverEntityId, Entity localEntity, System.Numerics.Vector3 appliedPosition, bool isPredicted, bool isCulled)
	{
		if (_followerDiagnostics.Count >= 240) return;

		bool hasTarget = world.Has<InterpolationTarget>(localEntity);
		System.Numerics.Vector3 targetPosition = hasTarget ? world.Get<InterpolationTarget>(localEntity).Position : appliedPosition;
		float followerGap = (targetPosition - appliedPosition).Length();
		if (followerGap < FollowerGapAlertMeters) return;

		bool hasPosition = world.Has<Position>(localEntity);
		bool wasStepped = _interpolationSteppedEntities.Contains(localEntityId);
		Entity worldEntity = ECSContext.WorldEntity;
		float interpolationFactor = world.Has<NetworkState>(worldEntity) ? world.Get<NetworkState>(worldEntity).DynamicInterpolationFactor : -1f;
		float roundTripMilliseconds = GameContext.NetworkLatencyService?.SmoothedRoundTripMilliseconds ?? -1f;
		_followerDiagnostics.Add($"appliesInFrame={_presentationAppliesInFrame}:follower={serverEntityId}:{sequence}:local={localEntityId}:gap={followerGap.ToString("F2", System.Globalization.CultureInfo.InvariantCulture)}:hasTarget={(hasTarget ? 1 : 0)}:hasPos={(hasPosition ? 1 : 0)}:stepped={(wasStepped ? 1 : 0)}:rate={interpolationFactor.ToString("F3", System.Globalization.CultureInfo.InvariantCulture)}:rtt={roundTripMilliseconds.ToString("F0", System.Globalization.CultureInfo.InvariantCulture)}:target={targetPosition.X.ToString("F2", System.Globalization.CultureInfo.InvariantCulture)},{targetPosition.Z.ToString("F2", System.Globalization.CultureInfo.InvariantCulture)}:applied={appliedPosition.X.ToString("F2", System.Globalization.CultureInfo.InvariantCulture)},{appliedPosition.Z.ToString("F2", System.Globalization.CultureInfo.InvariantCulture)}:predicted={(isPredicted ? 1 : 0)}:culled={(isCulled ? 1 : 0)}:writer={PositionWriteTrace.Last(localEntity)}:frac={PositionWriteTrace.LastInterpolationFraction(localEntity).ToString("F4", System.Globalization.CultureInfo.InvariantCulture)}:wrote={PositionWriteTrace.LastInterpolationWrite(localEntity).X.ToString("F2", System.Globalization.CultureInfo.InvariantCulture)},{PositionWriteTrace.LastInterpolationWrite(localEntity).Z.ToString("F2", System.Globalization.CultureInfo.InvariantCulture)}:age={Godot.Engine.GetPhysicsFrames() - PositionWriteTrace.LastWriteFrame(localEntity)}:cur={PositionWriteTrace.LastInterpolationFrom(localEntity).X.ToString("F2", System.Globalization.CultureInfo.InvariantCulture)},{PositionWriteTrace.LastInterpolationFrom(localEntity).Z.ToString("F2", System.Globalization.CultureInfo.InvariantCulture)}:tgt={PositionWriteTrace.LastInterpolationTarget(localEntity).X.ToString("F2", System.Globalization.CultureInfo.InvariantCulture)},{PositionWriteTrace.LastInterpolationTarget(localEntity).Z.ToString("F2", System.Globalization.CultureInfo.InvariantCulture)}:back={PositionWriteTrace.LastReadBack(localEntity).X.ToString("F2", System.Globalization.CultureInfo.InvariantCulture)},{PositionWriteTrace.LastReadBack(localEntity).Z.ToString("F2", System.Globalization.CultureInfo.InvariantCulture)}");
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
			string summary = $"fingerprint_mismatches={_fingerprintMismatchCount}\nmissing_unit_components={_missingUnitComponentCount}\nfollower_catch_ups={GameContext.SimulationService?.FollowerCatchUpCount ?? 0}\nshadow_baselines={ShadowClientVerifier.VerifiedBaselines}\nshadow_replayed_deltas={ShadowClientVerifier.ReplayedDeltaCount}\nshadow_structural_failures={ShadowClientVerifier.StructuralFailures}\nshadow_fingerprint_failures={ShadowClientVerifier.FingerprintFailures}\n{_fingerprintMismatchSummary}{_missingUnitComponentSummary}\n";
			File.WriteAllText(Path.Combine(_outputDirectory, _isHost ? "ECS_host_replication.txt" : "ECS_client_replication.txt"), summary);
		}
		catch (IOException)
		{
		}

		if (_isHost) return;

		try
		{
			using StreamWriter followerWriter = new(Path.Combine(_outputDirectory, "ECS_client_follower_gaps.txt"), false);
			followerWriter.WriteLine($"count={_followerDiagnostics.Count}");
			followerWriter.WriteLine($"alert_meters={FollowerGapAlertMeters.ToString(System.Globalization.CultureInfo.InvariantCulture)}");
			for (int index = 0; index < _followerDiagnostics.Count; index++) followerWriter.WriteLine(_followerDiagnostics[index]);
			for (int index = 0; index < _interpolationStepDetails.Count; index++) followerWriter.WriteLine(_interpolationStepDetails[index]);
		}
		catch (IOException)
		{
		}

		try
		{
			using StreamWriter gapWriter = new(Path.Combine(_outputDirectory, "ECS_client_unit_gaps.txt"), false);
			gapWriter.WriteLine($"count={_clientUnitGapCount}");
			gapWriter.WriteLine($"alert_sequences={ClientUnitGapAlertSequences}");
			for (int index = 0; index < _clientUnitGaps.Count; index++)
			{
				(int entityId, int fromSequence, int toSequence) = _clientUnitGaps[index];
				gapWriter.WriteLine($"gap={entityId}:{fromSequence}:{toSequence}");
			}
		}
		catch (IOException)
		{
		}
	}

	public static void RecordHostSnapshot(WorldSnapshot snapshot, IReadOnlyList<int> baselinePeerIds)
	{
		if (_writer == null || !_isHost) return;
		if (snapshot.Units == null) return;

		List<UnitSnapshot> units = snapshot.Units;
		byte[] record = BuildRecord(snapshot.Sequence, snapshot.IsBaseline, baselinePeerIds ?? Array.Empty<int>(), writer =>
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
				writer.Write(position.X);
				writer.Write(position.Y);
				writer.Write(position.Z);
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
		return networkService.IsServerEntityVisibleToEveryRemoteClient(serverEntityId);
	}

	public static void RecordClientSnapshot(bool isBaseline)
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

		_lastAppliedWasBaseline = isBaseline;
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
				writer.Write(unit.PositionX);
				writer.Write(unit.PositionY);
				writer.Write(unit.PositionZ);
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
		writer.Write(digest.ObstacleVolumeCount);
		writer.Write(digest.ObstacleHash);
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

		ulong presentationFrame = Godot.Engine.GetPhysicsFrames();
		_presentationAppliesInFrame = presentationFrame == _presentationFrame ? _presentationAppliesInFrame + 1 : 1;
		_presentationFrame = presentationFrame;

		int baselineSequence = GameContext.NetworkService.LastAppliedBaselineSequenceForDiagnostics;
		if (baselineSequence != _presentationAnchorBaselineSequence)
		{
			_presentationAnchorBaselineSequence = baselineSequence;
			_presentationWindowStartTime = gameHost.GameElapsedTime;
			_presentationAnchorSequence = sequence;
		}

		float presentationElapsed = gameHost.GameElapsedTime - _presentationWindowStartTime;
		if (presentationElapsed < PresentationSettleSeconds) return;
		if (presentationElapsed > PresentationWindowSeconds) return;

		NetworkMappingState mapping = world.Get<NetworkMappingState>(worldEntity);
		var units = new List<PresentedUnitState>();
		HashSet<int>? recordServerIds = _duplicateMappingsReported < 20 ? new HashSet<int>() : null;
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
			bool wasSentAtDetailedPrecision = GameContext.NetworkService != null
				&& GameContext.NetworkService.WasUnitSentAtDetailedPrecision(kvp.Value);
			System.Numerics.Vector3 appliedPosition = world.Has<Position>(localEntity) ? world.Get<Position>(localEntity).Value : new System.Numerics.Vector3(unit3D.GlobalPosition.X, unit3D.GlobalPosition.Y, unit3D.GlobalPosition.Z);
			RecordFollowerDiagnostic(world, sequence, kvp.Key, kvp.Value, localEntity, appliedPosition, isPredicted, unit3D.IsCulled);			if (recordServerIds != null)
			{
				if (!recordServerIds.Add(kvp.Value) && _duplicateMappingsReported < 20)
				{
					_duplicateMappingsReported++;
					_followerDiagnostics.Add($"duplicate={kvp.Value}:{sequence}:a second local entity {kvp.Key} maps to this server entity, two records for one unit will be written");
				}
			}
			units.Add(new PresentedUnitState(kvp.Value, ownerPlayerIndex, unit3D.GlobalPosition, unit3D.GlobalRotation.Y, currentHp, maxHp, isDead, isBuilding, isPredicted, unit3D.IsCulled, appliedPosition, wasSentAtDetailedPrecision));
		}

		units.Sort((left, right) => left.ServerEntityId.CompareTo(right.ServerEntityId));

		byte[] record = BuildRecord(sequence, _lastAppliedWasBaseline, writer =>
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
				writer.Write(unit.AppliedPositionX);
				writer.Write(unit.AppliedPositionY);
				writer.Write(unit.AppliedPositionZ);
				writer.Write(BuildFlags(unit.IsDead, unit.IsBuilding, unit.IsDetailed, true, unit.IsPredicted, unit.IsCulled));
				writer.Write(0);
			}

			writer.Write(0);
			writer.Write(0);
			writer.Write(0);
			AppendNavigationState(writer);
		});

		_presentationWriter.Write(record);
		_lastPresentedSequence = sequence;
	}

	private static int ResolveOwnerPlayerIndex(EcsWorld world, Entity localEntity)
	{
		if (!world.Has<UnitOwnerPlayer>(localEntity)) return -1;
		return world.Get<UnitOwnerPlayer>(localEntity).PlayerIndex;
	}

	private static byte BuildFlags(bool isDead, bool isBuilding, bool isDetailed, bool isVisibleToRemoteClient, bool isLocallyPredicted = false, bool isCulled = false)
	{
		byte flags = 0;
		if (isDead) flags |= 1;
		if (isBuilding) flags |= 2;
		if (isDetailed) flags |= 4;
		if (isVisibleToRemoteClient) flags |= 8;
		if (isLocallyPredicted) flags |= 16;
		if (isCulled) flags |= 32;
		return flags;
	}

	private static byte[] BuildRecord(int sequence, bool isBaseline, Action<BinaryWriter> writeUnits)
	{
		return BuildRecord(sequence, isBaseline, Array.Empty<int>(), writeUnits);
	}

	/// <summary>
	/// A baseline is decided per peer, not per sequence: a peer that needs a full world dump gets one while the peers whose
	/// acknowledged state is healthy keep receiving deltas. The record therefore carries the peers this sequence was a
	/// baseline for, so the verifier can compare a client's own flag against what it was actually sent rather than against
	/// a single shared flag that no longer describes the stream.
	/// </summary>
	private static byte[] BuildRecord(int sequence, bool isBaseline, IReadOnlyList<int> baselinePeerIds, Action<BinaryWriter> writeUnits)
	{
		using MemoryStream stream = new(4096);
		using (BinaryWriter writer = new(stream, System.Text.Encoding.UTF8, leaveOpen: true))
		{
			writer.Write(sequence);
			writer.Write(isBaseline);
			writer.Write(baselinePeerIds.Count);
			for (int index = 0; index < baselinePeerIds.Count; index++) writer.Write(baselinePeerIds[index]);
			// Snapshots are applied when the packet arrives, not on the next physics tick, so one tick can produce several
			// records of the same instant. Readers that judge a per-tick quantity - how far the renderer has followed, how
			// far the follower has advanced - need to know which records share a tick, or one instant is counted as dozens.
			writer.Write(Godot.Engine.GetPhysicsFrames());
			writeUnits(writer);
		}

		return stream.ToArray();
	}

	private static void AppendRecord(byte[] record, int sequence, bool isBaseline)
	{
		if (_writer == null) return;
		_writer.Write(record);
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
		public readonly bool IsCulled;
		public readonly bool IsDetailed;
		public readonly float AppliedPositionX;
		public readonly float AppliedPositionY;
		public readonly float AppliedPositionZ;

		public PresentedUnitState(int serverEntityId, int ownerPlayerIndex, Vector3 position, float rotationY, float currentHp, float maxHp, bool isDead, bool isBuilding, bool isPredicted, bool isCulled, System.Numerics.Vector3 appliedPosition, bool isDetailed)
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
			IsCulled = isCulled;
			IsDetailed = isDetailed;
			AppliedPositionX = appliedPosition.X;
			AppliedPositionY = appliedPosition.Y;
			AppliedPositionZ = appliedPosition.Z;
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
