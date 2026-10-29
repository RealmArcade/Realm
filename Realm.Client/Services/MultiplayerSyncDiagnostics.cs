using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using Godot;
using Realm.Client.Core;
using Realm.Ecs;
using Realm.Ecs.Components.Core;
using Realm.Ecs.Components.Movement;
using Realm.Ecs.Components.Tags;
using Realm.Ecs.Replication;
using Realm.Ecs.Services;
using Realm.Shared.Diagnostics;
using Realm.Shared.Network;
using FileAccess = System.IO.FileAccess;

namespace Realm.Client.Services;

public static class MultiplayerSyncDiagnostics
{
	private const int BaselineIntervalTicks = 30;

	private static BinaryWriter? _writer;
	private static byte[] _fileHeader = Array.Empty<byte>();
	private static BinaryWriter? _presentationWriter;
	private static byte[] _presentationHeader = Array.Empty<byte>();
	private static string _outputDirectory = string.Empty;
	private static int _writtenRecordCount;
	private static int _lastRecordedSequence = -1;
	private static int _lastPresentedSequence = -1;
	private static float _presentationWindowStartTime = -1f;
	private static int _presentationAnchorSequence = -1;
	private static int _fingerprintMismatchCount;
	private static string _fingerprintMismatchSummary = string.Empty;
	private static readonly HashSet<int> _clientSeenSequences = new();
	private static readonly List<(int UniqueId, Entity Entity)> _lootBuffer = new();

	public static void RecordClientSeenSequence(int sequence)
	{
		_clientSeenSequences.Add(sequence);
	}

	private static int _snapshotDiscardRequested;
	private static int _discardedSnapshotSequence = -1;

	public static int DiscardedSnapshotSequence => Volatile.Read(ref _discardedSnapshotSequence);

	public static void RequestSnapshotDiscard() => Interlocked.Exchange(ref _snapshotDiscardRequested, 1);

	public static bool ConsumeSnapshotDiscardRequest() => Interlocked.Exchange(ref _snapshotDiscardRequested, 0) == 1;

	public static void RecordDiscardedSnapshotSequence(int sequence) => Volatile.Write(ref _discardedSnapshotSequence, sequence);

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

	private static int _removedUnitComponentCount;
	private static string _removedUnitComponentSummary = string.Empty;
	private static int _unreconciledUnitCount;
	private static string _unreconciledUnitSummary = string.Empty;

	/// <summary>How many components a baseline's presence said the authority dropped, which the peer then dropped too.</summary>
	public static int RemovedUnitComponentCount => _removedUnitComponentCount;
	public static string RemovedUnitComponentSummary => _removedUnitComponentSummary;

	/// <summary>
	///     How many replicas a baseline never named while the peer still held them. A baseline states everything the peer
	///     can see, so a unit it leaves out is one the peer must stop holding; a replica that survives one is state the
	///     authority can no longer correct, and the components it carries read as differences for the rest of the match.
	/// </summary>
	public static int UnreconciledUnitCount => _unreconciledUnitCount;
	public static string UnreconciledUnitSummary => _unreconciledUnitSummary;

	public static void RecordRemovedUnitComponent(int serverEntityId, string componentName)
	{
		_removedUnitComponentCount++;
		if (_removedUnitComponentCount > 20) return;
		_removedUnitComponentSummary += $"{serverEntityId}:{componentName}; ";
	}

	public static void RecordUnreconciledUnit(int serverEntityId)
	{
		_unreconciledUnitCount++;
		if (_unreconciledUnitCount > 20) return;
		_unreconciledUnitSummary += $"{serverEntityId}; ";
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
	public static void RecordClientSnapshotUnitPresence(int sequence, IReadOnlyList<UnitSnapshot> units)
	{
		if (!IsRecording) return;

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

	/// <summary>
	///     Starts recording what this screen applies. Every process that plays the match records this way, because each
	///     of them is a peer of the authority now; the account it is judged against is the authority's own record, which
	///     the authority writes into the same folder.
	/// </summary>
	public static void Start(string outputDirectory)
	{
		Stop();
		Directory.CreateDirectory(outputDirectory);
		_outputDirectory = outputDirectory;
		_writtenRecordCount = 0;
		_lastRecordedSequence = -1;
		_lastPresentedSequence = -1;
		_presentationWindowStartTime = -1f;
		_presentationAnchorSequence = -1;
		_fingerprintMismatchCount = 0;
		_fingerprintMismatchSummary = string.Empty;
		_missingUnitComponentCount = 0;
		_missingUnitComponentSummary = string.Empty;
		_removedUnitComponentCount = 0;
		_removedUnitComponentSummary = string.Empty;
		_unreconciledUnitCount = 0;
		_unreconciledUnitSummary = string.Empty;
		_clientUnitGapCount = 0;
		WorstOffNavMeshMeters = 0f;
		OffNavMeshCount = 0;
		_presentationGaps.Clear();
		_presentationStepDetails.Clear();
		_clientUnitLastPresentSequence.Clear();
		_clientUnitLastUpdateInterval.Clear();
		_clientUnitGaps.Clear();
		_clientSeenSequences.Clear();
		string path = Path.Combine(outputDirectory, "ECS_client_sync.bin");
		using (MemoryStream headerStream = new())
		{
			using (BinaryWriter headerWriter = new(headerStream, System.Text.Encoding.UTF8, leaveOpen: true))
			{
				SyncRecordFormat.WriteHeader(headerWriter, false);
			}

			_fileHeader = headerStream.ToArray();
		}

		FileStream stream = new(path, FileMode.Create, FileAccess.Write, FileShare.Read, 1 << 16);
		_writer = new BinaryWriter(stream);
		_writer.Write(_fileHeader);
		_writer.Flush();

		PositionWriteTrace.IsEnabled = true;

		string presentationPath = Path.Combine(outputDirectory, "ECS_client_presentation.bin");
		using (MemoryStream presentationHeaderStream = new())
		{
			using (BinaryWriter presentationHeaderWriter = new(presentationHeaderStream, System.Text.Encoding.UTF8, leaveOpen: true))
			{
				SyncRecordFormat.WriteHeader(presentationHeaderWriter, false);
			}

			_presentationHeader = presentationHeaderStream.ToArray();
		}

		FileStream presentationStream = new(presentationPath, FileMode.Create, FileAccess.Write, FileShare.Read, 1 << 16);
		_presentationWriter = new BinaryWriter(presentationStream);
		_presentationWriter.Write(_presentationHeader);
		_presentationWriter.Flush();
	}

	/// <summary>
	///     How far a replicated unit's position may differ from the one the presentation step wrote before something other
	///     than that step has moved it. It is a comparison of one write against the read of it, not a tolerance on how far
	///     the unit has travelled since it was described, which is the buffer's delay and entirely expected.
	/// </summary>
	private const float PresentationMatchToleranceMeters = 0.01f;

	private static readonly List<string> _presentationGaps = new();

	/// <summary>
	///     How far from the navigation mesh a drawn position may be before it counts as a unit inside geometry. A mesh is
	///     triangulated, so a point on it is on a polygon exactly; this is the room a quantized description and a sample
	///     between two of them are allowed.
	/// </summary>
	public const float OffNavMeshToleranceMeters = 0.5f;

	/// <summary>The worst distance from the navigation mesh any drawn unit reached during the run.</summary>
	public static float WorstOffNavMeshMeters { get; private set; }

	/// <summary>How many drawn units were further from the navigation mesh than the tolerance allows.</summary>
	public static int OffNavMeshCount { get; private set; }
	private static readonly HashSet<int> _presentationSteppedEntities = new();
	private static int _presentationSteppedSequence = -1;

	private static readonly List<string> _presentationStepDetails = new();

	/// <summary>
	/// Records what one presentation step actually computed for a unit that is far from its newest description: the
	/// position it started from, the position sampled on the buffer, the fraction and the value it wrote. A unit that is
	/// visited by the step, computes a new position and is still drawn somewhere else can only be one whose write is
	/// undone or whose read is of another entity, and those two are indistinguishable without these numbers.
	/// </summary>
	public static void RecordPresentationStepDetail(int entityId, System.Numerics.Vector3 currentPosition, System.Numerics.Vector3 targetPosition, float stepFraction, System.Numerics.Vector3 writtenPosition)
	{
		if (!IsRecording || _presentationStepDetails.Count >= 240) return;
		_presentationStepDetails.Add($"step={entityId}:{_presentationSteppedSequence}:from={currentPosition.X.ToString("F2", System.Globalization.CultureInfo.InvariantCulture)},{currentPosition.Z.ToString("F2", System.Globalization.CultureInfo.InvariantCulture)}:to={targetPosition.X.ToString("F2", System.Globalization.CultureInfo.InvariantCulture)},{targetPosition.Z.ToString("F2", System.Globalization.CultureInfo.InvariantCulture)}:fraction={stepFraction.ToString("F4", System.Globalization.CultureInfo.InvariantCulture)}:wrote={writtenPosition.X.ToString("F2", System.Globalization.CultureInfo.InvariantCulture)},{writtenPosition.Z.ToString("F2", System.Globalization.CultureInfo.InvariantCulture)}");
	}

	private static int _duplicateMappingsReported;
	private static ulong _presentationFrame = ulong.MaxValue;
	private static int _presentationAppliesInFrame;

	/// <summary>
	/// Notes that the presentation step ran for an entity, so a unit the step never visited can be told apart from one it
	/// placed and something else moved afterwards. Recording only: the check is one boolean when no run is recorded.
	/// </summary>
	public static void RecordPresentationStep(int entityId, int sequence)
	{
		if (!IsRecording) return;
		if (sequence != _presentationSteppedSequence)
		{
			_presentationSteppedSequence = sequence;
			_presentationSteppedEntities.Clear();
		}

		_presentationSteppedEntities.Add(entityId);
	}

	/// <summary>
	/// Records a replicated unit whose position is not the one the presentation step wrote, which is the one thing this
	/// model does not allow: exactly one writer places a replicated unit, and it places it from the authority's own
	/// timeline. Being behind the newest description is not a fault - it is the delay the buffer holds - so the check is
	/// the write read back against the read that follows it, not a distance to a target. It records the local id as well,
	/// so a mapping that puts two local entities on one server id becomes visible rather than looking like one unit
	/// fighting itself.
	/// </summary>
	/// <summary>
	/// Records how far the position a unit was drawn at is from the navigation mesh this screen baked. Between two
	/// descriptions a unit is drawn on the straight line joining them, and a corner of a building makes that line cut
	/// through it, so this is the measurement of whether the presentation can put a unit inside geometry.
	///     A building is an obstacle rather than something that walks the mesh, and a unit that does not move on the ground
	///     has no mesh to be on, so neither is measured: counting them would report the map's shape as a defect.
	/// </summary>
	private static void RecordNavMeshPlacement(EcsWorld world, Entity localEntity, bool isBuilding, System.Numerics.Vector3 presentedPosition)
	{
		if (isBuilding) return;
		if (world.Has<MovementStats>(localEntity) && world.Get<MovementStats>(localEntity).Type != MovementType.Ground) return;

		var movementService = GameContext.Simulation?.MovementPathfindingService;
		if (movementService == null) return;
		if (!movementService.TryResolveDistanceOffNavMesh(presentedPosition, out float distanceOffNavMesh)) return;

		if (distanceOffNavMesh > WorstOffNavMeshMeters) WorstOffNavMeshMeters = distanceOffNavMesh;
		if (distanceOffNavMesh > OffNavMeshToleranceMeters) OffNavMeshCount++;
	}

	private static void RecordPresentationGap(EcsWorld world, int sequence, int localEntityId, int serverEntityId, Entity localEntity, System.Numerics.Vector3 presentedPosition, bool isCulled)
	{
		if (!PositionWriteTrace.IsEnabled) return;
		if (_presentationGaps.Count >= 240) return;

		// A unit the presentation step did not visit this record cannot be judged by what it wrote: the record is of a unit
		// that was described and drawn for the first time on this tick, which is the spawn's own placement. That one is
		// legitimate and the step takes it over from the next tick.
		bool wasStepped = _presentationSteppedEntities.Contains(localEntityId);
		if (!wasStepped) return;

		System.Numerics.Vector3 writtenPosition = PositionWriteTrace.LastPresentationWrite(localEntity);
		float presentationDrift = (writtenPosition - presentedPosition).Length();
		bool writtenByPresentation = PositionWriteTrace.Last(localEntity) == PositionWriter.Interpolation;
		if (writtenByPresentation && presentationDrift <= PresentationMatchToleranceMeters) return;

		bool hasTarget = world.Has<LatestDescription>(localEntity);
		System.Numerics.Vector3 targetPosition = hasTarget ? world.Get<LatestDescription>(localEntity).Position : presentedPosition;
		bool hasPosition = world.Has<Position>(localEntity);
		float cursorSequence = GameContext.SnapshotTimeline.CursorSequence;
		float lagSequences = GameContext.SnapshotTimeline.NewestSequence - cursorSequence;
		float roundTripMilliseconds = GameContext.NetworkLatencyService?.SmoothedRoundTripMilliseconds ?? -1f;
		_presentationGaps.Add($"appliesInFrame={_presentationAppliesInFrame}:unit={serverEntityId}:{sequence}:local={localEntityId}:drift={presentationDrift.ToString("F4", System.Globalization.CultureInfo.InvariantCulture)}:writer={PositionWriteTrace.Last(localEntity)}:hasDescription={(hasTarget ? 1 : 0)}:hasPos={(hasPosition ? 1 : 0)}:stepped={(wasStepped ? 1 : 0)}:cursor={cursorSequence.ToString("F2", System.Globalization.CultureInfo.InvariantCulture)}:behind={lagSequences.ToString("F2", System.Globalization.CultureInfo.InvariantCulture)}:delay={GameContext.SnapshotTimeline.DelaySequences}:rtt={roundTripMilliseconds.ToString("F0", System.Globalization.CultureInfo.InvariantCulture)}:newest={targetPosition.X.ToString("F2", System.Globalization.CultureInfo.InvariantCulture)},{targetPosition.Z.ToString("F2", System.Globalization.CultureInfo.InvariantCulture)}:presented={presentedPosition.X.ToString("F2", System.Globalization.CultureInfo.InvariantCulture)},{presentedPosition.Z.ToString("F2", System.Globalization.CultureInfo.InvariantCulture)}:wrote={writtenPosition.X.ToString("F2", System.Globalization.CultureInfo.InvariantCulture)},{writtenPosition.Z.ToString("F2", System.Globalization.CultureInfo.InvariantCulture)}:age={Godot.Engine.GetPhysicsFrames() - PositionWriteTrace.LastWriteFrame(localEntity)}:culled={(isCulled ? 1 : 0)}:frac={PositionWriteTrace.LastPresentationFraction(localEntity).ToString("F4", System.Globalization.CultureInfo.InvariantCulture)}:cur={PositionWriteTrace.LastPresentationFrom(localEntity).X.ToString("F2", System.Globalization.CultureInfo.InvariantCulture)},{PositionWriteTrace.LastPresentationFrom(localEntity).Z.ToString("F2", System.Globalization.CultureInfo.InvariantCulture)}:back={PositionWriteTrace.LastReadBack(localEntity).X.ToString("F2", System.Globalization.CultureInfo.InvariantCulture)},{PositionWriteTrace.LastReadBack(localEntity).Z.ToString("F2", System.Globalization.CultureInfo.InvariantCulture)}");
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
		if (_clientSeenSequences.Count > 0)
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
			string summary = $"fingerprint_mismatches={_fingerprintMismatchCount}\nmissing_unit_components={_missingUnitComponentCount}\nremoved_unit_components={_removedUnitComponentCount}\nunreconciled_units={_unreconciledUnitCount}\nbuffer_starvations={GameContext.SnapshotTimeline.StarvationCount}\ninterpolation_delay_sequences={GameContext.SnapshotTimeline.DelaySequences}\nworst_off_navmesh_meters={WorstOffNavMeshMeters.ToString("F3", System.Globalization.CultureInfo.InvariantCulture)}\noff_navmesh_units={OffNavMeshCount}\nshadow_baselines={ShadowClientVerifier.VerifiedBaselines}\nshadow_replayed_deltas={ShadowClientVerifier.ReplayedDeltaCount}\nshadow_structural_failures={ShadowClientVerifier.StructuralFailures}\nshadow_fingerprint_failures={ShadowClientVerifier.FingerprintFailures}\n{_fingerprintMismatchSummary}{_missingUnitComponentSummary}{_removedUnitComponentSummary}{_unreconciledUnitSummary}\n";
			File.WriteAllText(Path.Combine(_outputDirectory, "ECS_client_replication.txt"), summary);
		}
		catch (IOException)
		{
		}

		try
		{
			using StreamWriter presentationWriter = new(Path.Combine(_outputDirectory, "ECS_client_presentation_gaps.txt"), false);
			presentationWriter.WriteLine($"count={_presentationGaps.Count}");
			presentationWriter.WriteLine($"tolerance_meters={PresentationMatchToleranceMeters.ToString(System.Globalization.CultureInfo.InvariantCulture)}");
			presentationWriter.WriteLine($"delay_sequences={GameContext.SnapshotTimeline.DelaySequences}");
			presentationWriter.WriteLine($"starvations={GameContext.SnapshotTimeline.StarvationCount}");
			presentationWriter.WriteLine($"extrapolated_samples={GameContext.SnapshotTimeline.ExtrapolatedSampleCount}");
			for (int index = 0; index < _presentationGaps.Count; index++) presentationWriter.WriteLine(_presentationGaps[index]);
			for (int index = 0; index < _presentationStepDetails.Count; index++) presentationWriter.WriteLine(_presentationStepDetails[index]);
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

	public static void RecordClientSnapshot(bool isBaseline)
	{
		if (_writer == null) return;

		EcsWorld? world = GameContext.World;
		if (world == null) return;
		Entity worldEntity = GameContext.Simulation.WorldEntity;
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
			if (!world.Has<LatestDescription>(localEntity)) continue;

			LatestDescription target = world.Get<LatestDescription>(localEntity);
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
				byte flags = 0;
				if (unit.IsDead) flags |= SyncRecordFormat.FlagDead;
				if (unit.IsBuilding) flags |= SyncRecordFormat.FlagBuilding;
				flags |= SyncRecordFormat.FlagDetailed;
				flags |= SyncRecordFormat.FlagVisibleToRemoteClient;

				System.Numerics.Vector3 position = new(unit.PositionX, unit.PositionY, unit.PositionZ);
				SyncRecordFormat.SyncUnitRecord unitRecord = new(unit.ServerEntityId, unit.OwnerPlayerIndex, position, unit.RotationY, unit.CurrentHp, unit.MaxHp, position, flags);
				_comparabilityLocalEntityId = unit.LocalEntityId;
				SyncRecordFormat.WriteUnit(writer, in unitRecord, world, new Entity(unit.LocalEntityId), ReplicatedScope.Unit, mapping.ClientToServerEntityMap, ClientComponentComparability);
			}

			AppendPlayerScopeState(writer, world, mapping.ClientToServerEntityMap);
			AppendNavigationState(writer);
		});

		AppendRecord(record, sequence, isBaseline);
	}

	private static void AppendPlayerScopeState(BinaryWriter writer, EcsWorld? world, Dictionary<int, int>? clientToServerEntityMap)
	{
		Entity worldEntity = GameContext.Simulation.WorldEntity;
		SyncRecordFormat.WritePlayerScope(writer, world, worldEntity, ReplicatedScope.Player, clientToServerEntityMap);
		AppendLootState(writer, world);
	}

	private static void AppendLootState(BinaryWriter writer, EcsWorld? world)
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
			SyncRecordFormat.WriteLootItem(writer, _lootBuffer[index].UniqueId, SyncRecordFormat.NamedByEveryPeer, world, _lootBuffer[index].Entity);
		}
	}

	private static void AppendNavigationState(BinaryWriter writer)
	{
		SyncRecordFormat.WriteNavigationState(writer, NavigationParityDigest.Capture(GameContext.World, GameContext.Simulation.WorldEntity, GameContext.Simulation.TerrainNavMeshService));
	}

	public static void RecordClientPresentation()
	{
		if (_presentationWriter == null) return;

		GameHost? gameHost = GameContext.Host;
		if (gameHost == null) return;

		EcsWorld? world = GameContext.World;
		if (world == null) return;
		Entity worldEntity = GameContext.Simulation.WorldEntity;
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

			System.Numerics.Vector3 appliedPosition = world.Has<Position>(localEntity) ? world.Get<Position>(localEntity).Value : new System.Numerics.Vector3(unit3D.GlobalPosition.X, unit3D.GlobalPosition.Y, unit3D.GlobalPosition.Z);
			RecordPresentationGap(world, sequence, kvp.Key, kvp.Value, localEntity, appliedPosition, unit3D.IsCulled);
			RecordNavMeshPlacement(world, localEntity, isBuilding, appliedPosition);

			if (recordServerIds != null)
			{
				if (!recordServerIds.Add(kvp.Value) && _duplicateMappingsReported < 20)
				{
					_duplicateMappingsReported++;
					_presentationGaps.Add($"duplicate={kvp.Value}:{sequence}:a second local entity {kvp.Key} maps to this server entity, two records for one unit will be written");
				}
			}
			units.Add(new PresentedUnitState(kvp.Value, ownerPlayerIndex, unit3D.GlobalPosition, unit3D.GlobalRotation.Y, currentHp, maxHp, isDead, isBuilding, unit3D.IsCulled, appliedPosition));
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
				byte flags = 0;
				if (unit.IsDead) flags |= SyncRecordFormat.FlagDead;
				if (unit.IsBuilding) flags |= SyncRecordFormat.FlagBuilding;
				// The match states a unit at one precision, so a transform this screen rendered came from a full description
				// of where the match holds the unit.
				flags |= SyncRecordFormat.FlagDetailed;
				flags |= SyncRecordFormat.FlagVisibleToRemoteClient;
				if (unit.IsCulled) flags |= SyncRecordFormat.FlagCulled;
				writer.Write(flags);
				// A presented transform is this screen's own account of a unit it holds, so every peer could name it as far
				// as this record speaks, and the digest count that follows states none: the presentation stream is read by
				// the same reader as the applied state and both keep one unit header.
				writer.Write(SyncRecordFormat.NamedByEveryPeer);
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

	/// <summary>
	///     The entity the record being written describes, read by <see cref="ClientComponentComparability" />. A recording
	///     pass writes one unit at a time and allocates nothing per unit, so the comparability rule is a static delegate
	///     reading the identifier the loop just set rather than a closure over it.
	/// </summary>
	private static int _comparabilityLocalEntityId;

	private static readonly SyncRecordFormat.ComponentComparabilityMask ClientComponentComparability = IsClientComponentComparable;

	/// <summary>
	///     Whether this screen can state one component of the unit in hand. A reference the authority named and this
	///     screen could not resolve is one it must leave out: it was told to point at a unit it was never given, and the
	///     authority holds the identifier of a unit this peer is not allowed to know. Writing that as a hash difference
	///     reports the shroud as a defect.
	/// </summary>
	private static bool IsClientComponentComparable(int componentTypeId, ReadOnlySpan<byte> payload, out ulong namedByPeers)
	{
		namedByPeers = SyncRecordFormat.NamedByEveryPeer;
		NetworkService? networkService = GameContext.NetworkService;
		if (networkService == null) return true;

		// A screen speaks only for itself: a reference it can name is a statement about its own copy of the world, which
		// the reader holds this screen to. One it cannot name is left out rather than written as a value it invented.
		return !networkService.IsReferenceUnresolved(_comparabilityLocalEntityId, componentTypeId);
	}

	private static int ResolveOwnerPlayerIndex(EcsWorld world, Entity localEntity)
	{
		if (!world.Has<UnitOwnerPlayer>(localEntity)) return -1;
		return world.Get<UnitOwnerPlayer>(localEntity).PlayerIndex;
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
			// records of the same instant. Readers that judge a per-tick quantity - how far the renderer travelled, where the
			// cursor stood - need to know which records share a tick, or one instant is counted as dozens.
			writer.Write(Godot.Engine.GetPhysicsFrames());
			// A screen describes only itself, so there is no peer whose bit a reader would look up here; the authority's
			// record is the one that states the order its reference masks count peers in.
			writer.Write(0);
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

		string fileName = $"ECS_client_snapshot_{sequence}.bin";
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
		public readonly bool IsCulled;
		public readonly float AppliedPositionX;
		public readonly float AppliedPositionY;
		public readonly float AppliedPositionZ;

		public PresentedUnitState(int serverEntityId, int ownerPlayerIndex, Vector3 position, float rotationY, float currentHp, float maxHp, bool isDead, bool isBuilding, bool isCulled, System.Numerics.Vector3 appliedPosition)
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
			IsCulled = isCulled;
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

		public ClientUnitState(int serverEntityId, int localEntityId, int ownerPlayerIndex, LatestDescription target, float currentHp, float maxHp, bool isDead, bool isBuilding)
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
