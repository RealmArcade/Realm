using System;
using System.Collections.Generic;
using System.Net;
using System.Text.Json;
using System.Threading.Tasks;
using Realm.Ecs;
using Godot;
using MemoryPack;
using Realm.Client.Core;
using Realm.Client.Network;
using Realm.Client.Testing;
using Realm.Ecs.Common;
using Realm.Ecs.Components.Combat;
using Realm.Ecs.Components.Core;
using Realm.Ecs.Components.Meta;
using Realm.Ecs.Components.Movement;
using Realm.Ecs.Components.Resources;
using Realm.Ecs.Components.Tags;
using Realm.Ecs.Components.Terrain;
using Realm.Ecs.Services;
using Realm.Client.UI;
using Realm.Ecs.Replication;
using Realm.Shared.Network;
using HttpClient = System.Net.Http.HttpClient;

namespace Realm.Client.Services;

public class NetworkService
{
	private const int MaximumQueuedDeltas = 512;

	private readonly List<WorldSnapshot> _queuedDeltas = new();

	private readonly UnitVisibilityIndex _visibilityIndex = new();
	private readonly ReplicatedStateMirror _mirror = new();
	private int _appliedComponentDeltaCount;
	private int _appliedColdPayloadCount;
	private int _appliedAbilityStateCount;
	private int _appliedStaticTextCount;
	private int _appliedCatalogCount;
	private bool _fingerprintMatchLogged;

	private readonly Dictionary<int, UnitSnapshot> _pendingUnitSpawns = new();
	private readonly List<Entity> _pendingUnitKills = new();
	private readonly List<Entity> _pendingUnitDespawns = new();

	/// <summary>
	///     Component changes for a unit whose replica does not exist yet. A unit the authority has just started can be
	///     described an instant away from the components it also carries, so a change that arrives while the replica is
	///     still missing is kept here rather than dropped, and placed once the replica exists. A change that outlives the
	///     grace is state this screen was never told it had; the peer asks for a baseline instead of waiting for the
	///     match's own repair, which is rate limited to one baseline a second. A baseline empties this, because a baseline
	///     restates every component anyway.
	/// </summary>
	private readonly Dictionary<(int Key, int ComponentTypeId), PendingUnitComponentDelta> _pendingUnitComponentDeltas = new();
	private readonly List<(int Key, int ComponentTypeId)> _pendingUnitComponentDeltaKeys = new();

	/// <summary>
	///     How many sequences a change waits for the replica it names before this screen stops expecting one. A change is
	///     not unplaceable the moment its replica is missing, because a unit and its components can be described a
	///     sequence apart, and one still unplaceable after this many sequences will not be placed by a later delta.
	/// </summary>
	private const int UnplacedUnitDeltaGraceSequences = 4;

	private struct PendingUnitComponentDelta
	{
		public ComponentDelta Delta;
		public int FirstSeenSequence;
	}
	private readonly List<int> _untrackedServerIds = new();
	private readonly HashSet<int> _baselineLootServerIds = new();
	private readonly List<int> _staleLootServerIds = new();
	private readonly Dictionary<int, ulong> _baselineUnitPresence = new();
	private readonly HashSet<(int Key, int TypeId)> _baselineSentUnitComponents = new();
	private byte[] _baselineReconcileScratch = new byte[256];


	public bool IsPaused { get; set; } = false;
	public int LastAppliedSnapshotSequenceForDiagnostics { get; private set; } = -1;
	public int LastAppliedBaselineSequenceForDiagnostics { get; private set; } = -1;

	/// <summary>
	/// The player slot that asked the match to pause, or -1 when the match paused itself. The match owns the pause, so
	/// this is only ever what the authority last reported.
	/// </summary>
	public int PausedByPlayerIndex { get; set; } = -1;

	public void RemoveClientToServerMapping(int localEntityId)
	{
		ForgetReferencesHeldBy(localEntityId);

		Entity worldEntity = GameContext.Simulation.WorldEntity;
		if (worldEntity == Entity.Null || !GameContext.World.Has<NetworkMappingState>(worldEntity)) return;

		var mapping = GameContext.World.Get<NetworkMappingState>(worldEntity);
		if (mapping.ClientToServerEntityMap.TryGetValue(localEntityId, out int serverEntityId))
		{
			mapping.ServerToClientEntityMap.Remove(serverEntityId);
			ReplicatedEntityReferences.Unregister(serverEntityId);
		}

		mapping.ClientToServerEntityMap.Remove(localEntityId);
	}


	public void Clear()
	{
		IsPaused = false;
		PausedByPlayerIndex = -1;
		_queuedDeltas.Clear();
		_pendingUnitSpawns.Clear();
		_pendingUnitKills.Clear();
		_pendingUnitDespawns.Clear();
		_pendingUnitComponentDeltas.Clear();
		_referrersByServerEntityId.Clear();
		_referencedServerEntityIds.Clear();
		_referencedComponentTypesByLocalEntity.Clear();
		_untrackedServerIds.Clear();
		_playerTeams.Clear();
		_alliances.Clear();
		ReplicatedEntityReferences.Clear();
	}

	public bool TryGetServerEntityId(int localEntityId, out int serverEntityId)
	{
		serverEntityId = -1;
		Entity worldEntity = FindWorldEntity();
		if (worldEntity == Entity.Null || !GameContext.World.Has<NetworkMappingState>(worldEntity)) return false;
		var mapping = GameContext.World.Get<NetworkMappingState>(worldEntity);
		return mapping.ClientToServerEntityMap.TryGetValue(localEntityId, out serverEntityId);
	}

	/// <summary>
	///     The authoritative entity a local one replicates, or nothing when it replicates none. The mapping is the only
	///     truth: this screen holds no world of its own, so a local id is not a server id and an entity the authority never
	///     described has no server entity at all. Answering with the local id made a local entity indistinguishable from
	///     the authoritative unit that happened to carry that number, which is how an order could name a unit the match
	///     does not have.
	/// </summary>
	public int GetServerEntityId(int localEntityId)
	{
		Entity worldEntity = FindWorldEntity();
		if (worldEntity != Entity.Null && GameContext.World.Has<NetworkMappingState>(worldEntity))
		{
			var mapping = GameContext.World.Get<NetworkMappingState>(worldEntity);
			if (mapping.ClientToServerEntityMap.TryGetValue(localEntityId, out int serverId))
			{
				return serverId;
			}
		}

		return -1;
	}

	public Entity FindServerEntity(int entityId, List<Unit3D> allUnits)
	{
		foreach (var unit in allUnits)
		{
			if (unit.Entity.Id == entityId)
			{
				return unit.Entity;
			}
		}

		return Entity.Null;
	}

	

	public int GetOwnerPlayerIndex(Entity unitEntity)
	{
		if (unitEntity == Entity.Null || GameContext.World == null || !GameContext.World.IsAlive(unitEntity)) return -1;
		if (!GameContext.World.Has<UnitOwnerPlayer>(unitEntity)) return -1;
		return GameContext.World.Get<UnitOwnerPlayer>(unitEntity).PlayerIndex;
	}

	public int GetOwnerPeerId(Entity unitEntity)
	{
		Entity worldEntity = FindWorldEntity();
		if (worldEntity == Entity.Null || !GameContext.World.Has<NetworkMappingState>(worldEntity)) return -1;
		var mapping = GameContext.World.Get<NetworkMappingState>(worldEntity);

		if (GameContext.World.Has<UnitOwnerPlayer>(unitEntity))
		{
			int playerIndex = GameContext.World.Get<UnitOwnerPlayer>(unitEntity).PlayerIndex;
			var lobby = GameContext.Lobby;
			if (lobby != null)
			{
				var players = lobby.PlayerList;
				for (int i = 0; i < players.Count; i++)
				{
					if (players[i].Slot == playerIndex)
					{
						return players[i].PeerId;
					}
				}
			}
		}

		if (!GameContext.World.Has<Owner>(unitEntity)) return -1;
		var owner = GameContext.World.Get<Owner>(unitEntity).PlayerEntity;
		foreach (var kvp in mapping.PeerIdToPlayerEntityMap)
		{
			if (kvp.Value == owner.Value)
			{
				return kvp.Key;
			}
		}

		return -1;
	}

	

	public bool IsUnitVisibleToPlayer(Entity playerEntity, Entity unitEntity)
	{
		if (IsShroudRevealed())
		{
			return true;
		}

		EcsWorld? world = GameContext.World;
		if (world == null || !world.IsAlive(unitEntity)) return false;

		if (world.Has<Owner>(unitEntity) && world.Get<Owner>(unitEntity).PlayerEntity.Value == playerEntity)
		{
			return true;
		}

		if (!world.Has<Position>(unitEntity)) return false;
		System.Numerics.Vector3 unitPosition = world.Get<Position>(unitEntity).Value;
		return _visibilityIndex.IsVisibleToPlayer(playerEntity.Id, unitPosition);
	}

	public bool IsShroudRevealed()
	{
		if (GameContext.World == null) return false;
		Entity worldEntity = FindWorldEntity();
		if (worldEntity == Entity.Null || !GameContext.World.IsAlive(worldEntity)) return false;
		if (!GameContext.World.Has<ShroudState>(worldEntity)) return false;
		return GameContext.World.Get<ShroudState>(worldEntity).VisibilityMode == ShroudVisibilityMode.Revealed;
	}

	public bool IsServerEntityVisibleToEveryRemoteClient(int serverEntityId)
	{
		GameHost? gameHost = GameContext.Host;
		if (gameHost == null) return true;

		LobbyManager? lobby = GameContext.Lobby;
		if (lobby == null) return true;

		Entity worldEntity = FindWorldEntity();
		if (worldEntity == Entity.Null || !GameContext.World.Has<NetworkMappingState>(worldEntity)) return true;

		var mapping = GameContext.World.Get<NetworkMappingState>(worldEntity);
		Entity unitEntity = new(serverEntityId);
		int localPeerId = gameHost.Multiplayer.GetUniqueId();
		int remoteClientCount = 0;

		foreach (PlayerInfo player in lobby.PlayerList)
		{
			if (player.PeerId <= 0 || player.PeerId == localPeerId) continue;
			if (player.IsDisconnected) continue;
			if (!mapping.PeerIdToPlayerEntityMap.TryGetValue(player.PeerId, out Entity playerEntity)) continue;

			remoteClientCount++;
			if (!IsUnitVisibleToPlayer(playerEntity, unitEntity)) return false;
		}

		return remoteClientCount > 0;
	}

	private bool TryChargeBuildingCost(Entity playerEntity, UnitMetadata meta)
	{
		if (!GameContext.World.TryGet<PlayerResources>(playerEntity, out var res)) return false;

		int costGold = (int)meta.CostGold;
		int costWood = (int)meta.CostWood;
		int costStone = (int)meta.CostStone;

		if (res.Gold < costGold || res.Wood < costWood || res.Stone < costStone) return false;

		res.Gold -= costGold;
		res.Wood -= costWood;
		res.Stone -= costStone;
		GameContext.World.Set(playerEntity, res);
		return true;
	}

	public void UpdateClientTickDomain()
	{
		
		GameContext.World?.Mutate<NetworkState>(GameContext.Simulation.WorldEntity, (ref NetworkState netState) =>
			netState.DynamicInterpolationFactor = ComputeDynamicInterpolationFactor());

		VerifyReplicatedEcsDriftDomain();
	}

	public float ComputeDynamicInterpolationFactor()
	{
		float bufferTime = MatchTiming.MinimumInterpolationBufferSeconds;
		NetworkLatencyService? latency = GameContext.NetworkLatencyService;
		if (latency != null && latency.HasSample && float.IsFinite(latency.SmoothedRoundTripMilliseconds))
		{
			bufferTime = Mathf.Max(MatchTiming.MinimumInterpolationBufferSeconds, (latency.SmoothedRoundTripMilliseconds / 1000f) * 1.5f);
		}

		return 1f / bufferTime;
	}

	public (int CommandId, byte[] Payload) BuildClientCommand(string commandType, List<int> unitIds, System.Numerics.Vector3 targetPos, int targetEntityId, string argString)
	{
		int commandId = 1;
		Entity worldEntity = FindWorldEntity();
		if (worldEntity != Entity.Null && GameContext.World.Has<NetworkState>(worldEntity))
		{
			commandId = GameContext.World.Get<NetworkState>(worldEntity).NextCommandId;
			ref var ns = ref GameContext.World.Get<NetworkState>(worldEntity);
			ns.NextCommandId++;
		}

		var cmd = new NetworkCommand
		{
			CommandId = commandId,
			CommandType = commandType,
			UnitEntityIds = unitIds,
			TargetPosition = new NetworkVector3(targetPos.X, targetPos.Y, targetPos.Z),
			TargetEntityId = targetEntityId,
			ArgString = argString
		};
		return (cmd.CommandId, MemoryPackSerializer.Serialize(cmd));
	}

	public void RecordSnapshotReceived(byte[] payload) => RecordSnapshotReceived(MemoryPackSerializer.Deserialize<WorldSnapshot>(SnapshotWireFormat.Decode(payload)));

	public void RecordSnapshotReceived(WorldSnapshot snapshot)
	{
		Entity worldEntity = FindWorldEntity();
		if (worldEntity == Entity.Null || !GameContext.World.Has<NetworkState>(worldEntity)) return;
		GameContext.World.Get<NetworkState>(worldEntity).LastSnapshotReceivedTime = Time.GetTicksMsec();
		ProcessSnapshot(snapshot);

		Entity currentWorldEntity = FindWorldEntity();
		if (currentWorldEntity == Entity.Null || !GameContext.World.Has<NetworkState>(currentWorldEntity)) return;
		if (!GameContext.World.Get<NetworkState>(currentWorldEntity).HasReceivedInitialBaseline) RequestSnapshotBaseline();
	}

	private ulong _lastBaselineRequestTime;

	private void RequestSnapshotBaseline()
	{
		ulong now = Time.GetTicksMsec();
		if (now - _lastBaselineRequestTime < 500ul) return;

		_lastBaselineRequestTime = now;
		GameContext.Host?.RequestSnapshotBaseline();
	}

	private void ProcessSnapshot(WorldSnapshot snapshot)
	{
		if (MultiplayerSyncDiagnostics.IsRecording) MultiplayerSyncDiagnostics.RecordClientSeenSequence(snapshot.Sequence);
		Entity worldEntity = FindWorldEntity();
		if (worldEntity == Entity.Null || !GameContext.World.Has<NetworkState>(worldEntity)) return;

		ref var networkState = ref GameContext.World.Get<NetworkState>(worldEntity);

		if (snapshot.IsBaseline)
		{
			if (snapshot.Sequence <= networkState.LastReceivedBaselineSeq) return;

			// A baseline is larger than a delta and travels on the same reliable stream, so it can arrive after deltas that
			// already describe a newer world. Applying it would move every replicated unit backwards, and a predicted unit
			// would then be corrected toward positions the client has already moved past. The baseline is stale, not useful.
			if (snapshot.Sequence <= networkState.LastAppliedSnapshotSequence)
			{
				// The baseline is stale for the units, but it is still the base every later delta is written against, so it
				// is recorded and the deltas that were waiting for it are applied here. Returning without draining left them
				// queued until some future baseline arrived, which on a delayed link was several seconds of a frozen world.
				networkState.LastReceivedBaselineSeq = snapshot.Sequence;
				DrainQueuedDeltas(ref networkState);
				return;
			}

			networkState.LastReceivedBaselineSeq = snapshot.Sequence;
			networkState.HasReceivedInitialBaseline = true;
			networkState.LastAppliedSnapshotSequence = snapshot.Sequence;
			_queuedDeltas.RemoveAll(d => d.Sequence <= snapshot.Sequence);
			// A baseline restates the whole world, so every sequence before it is superseded rather than missing. Keeping
			// the earlier mask left the hole that caused this baseline in the acknowledgement window for another two
			// seconds, which reported the peer as still missing state it had just been given and repaired it again on
			// every cooldown.
			_appliedSnapshots.Reset();
			ApplyWorldSnapshot(snapshot);

			DrainQueuedDeltas(ref networkState);
			return;
		}

		if (snapshot.Sequence <= networkState.LastAppliedSnapshotSequence) return;

		// A delta names the oldest world state it assumes, not the newest baseline the host has started sending. It used
		// to require an exact match, so every delta the host built on a baseline that was still in flight was queued until
		// that baseline arrived: with a 138 KB baseline on the reliable channel that stalled the client for one burst per
		// baseline interval, which is what the rendered transforms and the prediction record showed as a lag that grew
		// and healed once a second. A delta is absolute per unit, so a base the client has already superseded is harmless.
		if (!networkState.HasReceivedInitialBaseline || snapshot.BaseSequence > networkState.LastReceivedBaselineSeq)
		{
			// A delta the client cannot place is a delta whose base it never applied. Only waiting for one used to be
			// enough because the base could not move without the baseline that moved it, but a baseline that is superseded
			// in flight leaves the client queuing against a base nobody sent it any more: the match then ran for seconds
			// on a frozen sequence, which is exactly the state prediction must never be left to reconcile against. Asking
			// for the world the deltas are written against bounds the recovery to one round trip.
			QueueDelta(snapshot);
			RequestSnapshotBaseline();
			return;
		}

		networkState.LastAppliedSnapshotSequence = snapshot.Sequence;
		ApplyWorldSnapshot(snapshot);
		DrainQueuedDeltas(ref networkState);
	}

	/// <summary>
	/// Applies every queued delta that the client now has the base for, in sequence order. A burst arrives when a lost
	/// baseline is finally delivered, and the deltas behind it are still newer than what the client has applied, so
	/// leaving them queued would discard the world state the transport went to the trouble of recovering.
	/// </summary>
	private void DrainQueuedDeltas(ref NetworkState networkState)
	{
		while (_queuedDeltas.Count > 0)
		{
			WorldSnapshot nextDelta = _queuedDeltas[0];
			if (nextDelta.BaseSequence > networkState.LastReceivedBaselineSeq) break;
			_queuedDeltas.RemoveAt(0);
			if (nextDelta.Sequence <= networkState.LastAppliedSnapshotSequence) continue;

			networkState.LastAppliedSnapshotSequence = nextDelta.Sequence;
			ApplyWorldSnapshot(nextDelta);
		}
	}

	private AppliedSequenceWindow _appliedSnapshots;

	private void AcknowledgeAppliedSnapshot(int sequence, bool fullyApplied)
	{
		if (GameContext.Host == null) return;

		if (fullyApplied && MultiplayerSyncDiagnostics.ConsumeSnapshotDiscardRequest())
		{
			fullyApplied = false;
			MultiplayerSyncDiagnostics.RecordDiscardedSnapshotSequence(sequence);
		}

		if (fullyApplied) _appliedSnapshots.MarkApplied(sequence);
		else _appliedSnapshots.Advance(sequence);
		if (_appliedSnapshots.Highest < 0) return;

		GameContext.Host.SendSnapshotAck(_appliedSnapshots.Highest, _appliedSnapshots.BuildMask());
	}

	private void QueueDelta(WorldSnapshot snapshot)
	{
		if (_queuedDeltas.Count >= MaximumQueuedDeltas) _queuedDeltas.RemoveAt(0);

		int insertIndex = _queuedDeltas.Count;
		while (insertIndex > 0 && _queuedDeltas[insertIndex - 1].Sequence > snapshot.Sequence) insertIndex--;
		_queuedDeltas.Insert(insertIndex, snapshot);
	}

	private void ApplyWorldSnapshot(WorldSnapshot snapshot)
	{
		Entity worldEntity = FindWorldEntity();
		if (worldEntity == Entity.Null || !GameContext.World.Has<NetworkMappingState>(worldEntity)) return;
		var mapping = GameContext.World.Get<NetworkMappingState>(worldEntity);

		MultiplayerSyncDiagnostics.RecordClientSnapshotUnitPresence(snapshot.Sequence, snapshot.Units);

		ApplyReplicatedColdPayloads(snapshot);

		if (snapshot.IsBaseline)
		{
			// A baseline restates every component of every unit it describes, so the changes held for a replica that never
			// arrived are answered here and holding them further would only place them again on top of the restatement.
			_pendingUnitComponentDeltas.Clear();

			ProcessBaselineSnapshotMismatches(snapshot, mapping);
			ProcessBaselineLootReconciliation(snapshot, mapping);
			ProcessBaselineComponentReconciliation(snapshot, mapping);
		}

		var prediction = GameSettings.MovementPrediction ? GameContext.MovementPredictionService : null;
		if (prediction != null && GameContext.World != null)
		{
			prediction.BeginSnapshotReconcile(snapshot.Sequence);
			foreach (var snap in snapshot.Units)
			{
				if (!mapping.ServerToClientEntityMap.TryGetValue(snap.EntityId, out var localEntity)) continue;
				prediction.ReconcileAuthoritativePosition(GameContext.World, localEntity.Id, snap.Position.ToNumerics());
			}

			prediction.EndSnapshotReconcile();
		}

		// Published before the units are applied rather than after: a unit the apply spawns is anchored to this sequence
		// as its first authoritative sample, and a spawn that read the previous value anchored the unit to -1 instead -
		// which the sample gap arithmetic then treated as "no previous sample" for that unit.
		LastAppliedSnapshotSequenceForDiagnostics = snapshot.Sequence;

		foreach (var snap in snapshot.Units)
		{
			ApplySnapshotToUnit(snap, snapshot.Sequence, mapping);
		}

		GameContext.Host?.FlushReplicatedSnapshotEvents();
		bool componentsFullyApplied = ApplyReplicatedComponentState(snapshot);
		ApplyPendingUnitComponentDeltas(snapshot.Sequence);

		if (snapshot.IsBaseline) LastAppliedBaselineSequenceForDiagnostics = snapshot.Sequence;
		if (snapshot.IsBaseline || GameContext.World.Get<NetworkState>(worldEntity).HasReceivedInitialBaseline)
		{
			MultiplayerSyncDiagnostics.RecordClientSnapshot(snapshot.IsBaseline);
		}
		// The acknowledgement names the exact sequences this client applied, so a snapshot that carried a delta it could not
		// place simply leaves its own bit clear: the host repeats that change instead of treating it as delivered, and the
		// rest of the stream is still confirmed.
		AcknowledgeAppliedSnapshot(snapshot.Sequence, componentsFullyApplied);
	}

	private void ProcessBaselineSnapshotMismatches(WorldSnapshot snapshot, NetworkMappingState mapping)
	{
		var serverIdsInSnapshot = new HashSet<int>(snapshot.Units.Count);
		for (int i = 0; i < snapshot.Units.Count; i++)
		{
			serverIdsInSnapshot.Add(snapshot.Units[i].EntityId);
		}

		_untrackedServerIds.Clear();
		foreach (var kvp in mapping.ServerToClientEntityMap)
		{
			if (serverIdsInSnapshot.Contains(kvp.Key)) continue;

			_untrackedServerIds.Add(kvp.Key);
		}			for (int i = 0; i < _untrackedServerIds.Count; i++)
			{
				int serverEntityId = _untrackedServerIds[i];
				if (!mapping.ServerToClientEntityMap.TryGetValue(serverEntityId, out var localEntity)) continue;				if (GameContext.World.Has<GroundLoot>(localEntity)) continue;

				RetireClientEntityForUntrackedServerId(mapping, localEntity, serverEntityId);
		}
	}

	/// <summary>
	/// Removes the unit components the authority does not have. What the authority has is the presence mask it sends with
	/// every baselined unit, not the component records that happened to travel in this baseline: a baseline no longer
	/// restates a component the peer already acknowledged, so treating "not in this payload" as "gone" would delete the
	/// peer's copy of every unchanged component on every baseline.
	/// </summary>
	private void ProcessBaselineComponentReconciliation(WorldSnapshot snapshot, NetworkMappingState mapping)
	{
		if (snapshot.ComponentDeltas == null || snapshot.Units == null || snapshot.UnitComponentPresence == null) return;

		_baselineUnitPresence.Clear();
		for (int index = 0; index < snapshot.UnitComponentPresence.Count; index++)
		{
			var presence = snapshot.UnitComponentPresence[index];
			_baselineUnitPresence[presence.ServerEntityId] = presence.Mask;
		}

		// A component that is new to the peer travels in this same baseline, and the reconciliation runs before the
		// component deltas are applied, so only a component the payload did not carry can be missing at this point.
		_baselineSentUnitComponents.Clear();
		for (int index = 0; index < snapshot.ComponentDeltas.Count; index++)
		{
			var delta = snapshot.ComponentDeltas[index];
			if ((ReplicatedScope)delta.Scope != ReplicatedScope.Unit) continue;
			if (delta.Bytes == null || delta.Bytes.Length == 0) continue;
			_baselineSentUnitComponents.Add((delta.Key, delta.ComponentTypeId));
		}

		IReadOnlyList<IReplicatedComponentCodec> codecs = ReplicatedUnitComponentSlots.Slots;
		foreach (var kvp in mapping.ServerToClientEntityMap)
		{
			int serverEntityId = kvp.Key;
			if (!_baselineUnitPresence.TryGetValue(serverEntityId, out ulong presenceMask)) continue;

			Entity localEntity = kvp.Value;
			if (!GameContext.World.IsAlive(localEntity)) continue;

			for (int slot = 0; slot < codecs.Count; slot++)
			{
				var codec = codecs[slot];
				if (_baselineReconcileScratch.Length < codec.SizeInBytes) _baselineReconcileScratch = new byte[codec.SizeInBytes];

				bool peerHasComponent = codec.TryWrite(GameContext.World, localEntity, _baselineReconcileScratch.AsSpan(0, codec.SizeInBytes));
				if ((presenceMask & (1UL << slot)) != 0)
				{
					if (!peerHasComponent && !_baselineSentUnitComponents.Contains((serverEntityId, codec.ComponentTypeId)))
					{
						MultiplayerSyncDiagnostics.RecordMissingUnitComponent(serverEntityId, codec.ComponentName);
					}

					continue;
				}

				if (!peerHasComponent) continue;
				codec.Remove(GameContext.World, localEntity);
			}
		}
	}

	private void ProcessBaselineLootReconciliation(WorldSnapshot snapshot, NetworkMappingState mapping)
	{
		if (snapshot.ComponentDeltas == null) return;

		int lootComponentTypeId = ComponentType<GroundLoot>.Id;
		_baselineLootServerIds.Clear();
		for (int index = 0; index < snapshot.ComponentDeltas.Count; index++)
		{
			var delta = snapshot.ComponentDeltas[index];
			if (delta.ComponentTypeId != lootComponentTypeId) continue;
			if (delta.Bytes == null || delta.Bytes.Length == 0) continue;
			_baselineLootServerIds.Add(delta.Key);
		}

		_staleLootServerIds.Clear();
		foreach (var kvp in mapping.ServerToClientEntityMap)
		{
			if (_baselineLootServerIds.Contains(kvp.Key)) continue;
			if (!GameContext.World.Has<GroundLoot>(kvp.Value)) continue;
			_staleLootServerIds.Add(kvp.Key);
		}

		for (int index = 0; index < _staleLootServerIds.Count; index++)
		{
			int serverEntityId = _staleLootServerIds[index];
			if (!mapping.ServerToClientEntityMap.TryGetValue(serverEntityId, out Entity localEntity)) continue;
			DestroyReplicatedLootEntity(serverEntityId, localEntity, mapping);
		}
	}

	private void RetireClientEntityForUntrackedServerId(NetworkMappingState mapping, Entity localEntity, int serverEntityId)
	{
		if (GameContext.World.IsAlive(localEntity) && !GameContext.World.Has<Dead>(localEntity))
		{
			_pendingUnitDespawns.Add(localEntity);
		}

		ForgetReferencesHeldBy(localEntity.Id);
		mapping.ServerToClientEntityMap.Remove(serverEntityId);
		mapping.ClientToServerEntityMap.Remove(localEntity.Id);
		ReplicatedEntityReferences.Unregister(serverEntityId);
	}

	/// <summary>
	///     How fast a description says a unit is going, worked out from the distance it covered since the description
	///     before it. A unit is described wherever the authority put it, and not every one of those descriptions reaches
	///     this client, so the span is counted in sequences rather than assumed to be one snapshot.
	/// </summary>
	private static System.Numerics.Vector3 ResolveDescribedVelocity(System.Numerics.Vector3 displacement, int sequences, bool discontinuous)
	{
		if (discontinuous || sequences <= 0) return System.Numerics.Vector3.Zero;

		float seconds = sequences * MatchTiming.SnapshotIntervalSeconds;
		return seconds > 0f ? displacement / seconds : System.Numerics.Vector3.Zero;
	}

	private void ApplySnapshotToUnit(UnitSnapshot snap, int sequence, NetworkMappingState mapping)
	{
		if (!mapping.ServerToClientEntityMap.TryGetValue(snap.EntityId, out var localEntity))
		{
			if (!snap.IsDead) _pendingUnitSpawns[snap.EntityId] = snap;
			return;
		}

		if (!GameContext.World.IsAlive(localEntity) || IsServerEntityIdRecycled(snap, localEntity))
		{
			RetireClientEntityForRecycledServerId(mapping, localEntity, snap.EntityId);
			if (!snap.IsDead) _pendingUnitSpawns[snap.EntityId] = snap;
			return;
		}

		if (snap.IsDead)
		{
			if (!GameContext.World.Has<Dead>(localEntity))
			{
				GameContext.World.Add<Dead>(localEntity);
				_pendingUnitKills.Add(localEntity);
			}

			return;
		}

		if (GameContext.World.Has<Health>(localEntity))
		{
			var hp = GameContext.World.Get<Health>(localEntity);
			hp.Current = snap.CurrentHp;
			hp.Max = snap.MaxHp;
			GameContext.World.Set(localEntity, hp);
		}

		// What the last description asked this client to walk toward, read before the new one replaces it. A step larger
		// than a unit can travel between two applied snapshots is a discontinuity in what the authority is telling this
		// client, not movement: the client stalled and missed descriptions, drained a burst in one tick, or received a
		// reconnect baseline, and a script can also move a unit outright. Smoothing across it means the unit visibly
		// trails the authority for the better part of a second, which is the six metre placement gap the lossy profile
		// used to report at the start of a run. The condition is the cause stated directly - how far the target stepped -
		// rather than a lag bound with a round trip in it.
		System.Numerics.Vector3 describedPosition = snap.Position.ToNumerics();
		System.Numerics.Vector3 previousTargetPosition = GameContext.World.Has<InterpolationTarget>(localEntity)
			? GameContext.World.Get<InterpolationTarget>(localEntity).Position
			: describedPosition;
		bool targetJumped = (describedPosition - previousTargetPosition).Length() > MaximumPlausibleTargetStepMeters;

		// The authority only describes a unit this peer can see, so a unit that leaves vision goes unmentioned for as long
		// as it is away and comes back wherever it actually walked to. Gliding from the position the client last heard
		// leaves the unit visibly trailing the authority for as long as the smoothing takes to cover that distance - the
		// six to eight metres measured on the lossy profile - and there is nothing to be continuous with: the client was
		// not watching the unit in between. The unit is placed where the authority says it is instead, and the smoothing
		// resumes from there on the next description. A target that jumped is placed for the same reason: the follower's
		// own lag would carry a distance the client never watched it walk.
		int lastDescribedSequence = GameContext.World.Has<DescribedAtSequence>(localEntity)
			? GameContext.World.Get<DescribedAtSequence>(localEntity).Sequence
			: -1;
		bool descriptionResumed = lastDescribedSequence >= 0 && sequence - lastDescribedSequence > UnobservedUnitGapSequences;

		// How fast a unit is going is not on the wire: two descriptions of where it is say it, and carrying it as well
		// cost half of what a description of a moving unit costs, because it is the part of a record that compression
		// cannot fold away. A description that continues the one before it states the speed it covered that distance at;
		// one that follows a gap or a jump states none, because the distance between them is not walking.
		var target = new InterpolationTarget
		{
			Position = describedPosition,
			Velocity = ResolveDescribedVelocity(describedPosition - previousTargetPosition, sequence - lastDescribedSequence, targetJumped || descriptionResumed),
			RotationY = snap.RotationY
		};

		GameContext.World.SetOrAdd(localEntity, target);
		if ((descriptionResumed || targetJumped) && !IsUnitOwnedByPrediction(localEntity) && GameContext.World.Has<Position>(localEntity))
		{
			PositionWriteTrace.Mark(localEntity, PositionWriter.Placement);
			GameContext.World.Set(localEntity, new Position(target.Position));
		}

		GameContext.World.SetOrAdd(localEntity, new DescribedAtSequence(sequence));
	}

	/// <summary>
	/// How many sequences a unit can go undescribed before its next description counts as a fresh sighting rather than a
	/// step. The far-unit resend cadence tells a peer about a moving unit every few sequences and a stationary one not at
	/// all, so a longer silence than this means the authority had nothing to say about it - it was outside this peer's
	/// vision - and the position it names is a sighting, not a continuation.
	/// </summary>
	public const int UnobservedUnitGapSequences = 10;

	/// <summary>
	/// How far a unit's described position can move between two applied snapshots before the step is a discontinuity
	/// rather than movement. A detailed unit steps a few centimetres per snapshot and a far one steps at the resend
	/// threshold plus the movement during its round trip - about one metre on the profiles that keep the game playable -
	/// so two metres is above anything continuous and below the distance a stalled client has to cover.
	/// </summary>
	public const float MaximumPlausibleTargetStepMeters = 2f;

	/// <summary>
	/// How much further than its smoothing can explain a follower may trail a description before this client stops
	/// gliding at the interpolation rate and catches the excess up deliberately. Margin on top of the catch-up the rate
	/// allows, so a follower that is merely smoothing never uses the faster path.
	/// </summary>
	public const float FollowerLagMarginMeters = 0.5f;

	/// <summary>
	/// The distance a unit may be moved before the authority resends it while it is outside the peer's detailed radius.
	/// A follower is allowed to be one resend step behind the described position for that reason alone.
	/// </summary>
	public const float FarUnitResendStepMeters = 1f;

	/// <summary>
	/// How far the applied position trails the description before interpolation stops gliding at its rate and closes the
	/// excess deliberately. A peer applies a snapshot when the packet arrives, not on its next physics tick, so several
	/// descriptions can land between two ticks and move the described position further than one smoothing step covers.
	/// The excess is bounded by the speed the description carries, the resend step and a margin, which is what a follower
	/// that is merely smoothing can account for; anything beyond it is time the client missed and must make up.
	/// </summary>
	public static float ResolveDefensibleFollowerLagMeters(System.Numerics.Vector3 describedVelocity, float interpolationRate)
	{
		if (!(interpolationRate > 0f)) return float.MaxValue;

		float smoothingLagMeters = describedVelocity.Length() / interpolationRate;
		return smoothingLagMeters + FarUnitResendStepMeters + FollowerLagMarginMeters;
	}

	private static bool IsUnitOwnedByPrediction(Entity localEntity)
	{
		return GameSettings.MovementPrediction
			&& GameContext.MovementPredictionService != null
			&& GameContext.MovementPredictionService.IsEntityTracked(localEntity.Id);
	}

	private static bool IsServerEntityIdRecycled(UnitSnapshot snap, Entity localEntity)
	{
		if (GameContext.World.Has<DefinitionId>(localEntity) && GameContext.World.Get<DefinitionId>(localEntity).Value != snap.UnitId) return true;
		if (GameContext.World.Has<UnitOwnerPlayer>(localEntity)
			&& snap.OwnerPlayerIndex >= 0
			&& GameContext.World.Get<UnitOwnerPlayer>(localEntity).PlayerIndex != snap.OwnerPlayerIndex) return true;
		if (GameContext.World.Has<Building>(localEntity) != snap.IsBuilding) return true;
		return GameContext.World.Has<Dead>(localEntity);
	}

	private void RetireClientEntityForRecycledServerId(NetworkMappingState mapping, Entity localEntity, int serverEntityId)
	{
		RetireClientEntityForUntrackedServerId(mapping, localEntity, serverEntityId);
	}

	public IReadOnlyList<UnitSnapshot> FlushPendingUnitSpawns()
	{
		if (_pendingUnitSpawns.Count == 0) return Array.Empty<UnitSnapshot>();

		var result = new List<UnitSnapshot>(_pendingUnitSpawns.Values);
		_pendingUnitSpawns.Clear();
		return result;
	}

	public IReadOnlyList<Entity> FlushPendingUnitKills()
	{
		var result = new List<Entity>(_pendingUnitKills);
		_pendingUnitKills.Clear();
		return result;
	}

	public IReadOnlyList<Entity> FlushPendingUnitDespawns()
	{
		var result = new List<Entity>(_pendingUnitDespawns);
		_pendingUnitDespawns.Clear();
		return result;
	}

	public Entity SpawnUnitFromSnapshot(UnitSnapshot snap, Func<string, bool, string> getFallbackModelPath, out string modelPath, out int playerIndex)
	{
		modelPath = string.Empty;
		playerIndex = 0;

		if (!GameHost.UnitRegistry.TryGetValue(snap.UnitId, out var meta) && !GameHost.BuildingRegistry.TryGetValue(snap.UnitId, out meta))
		{
			GD.PushWarning($"[Network] Cannot spawn replicated entity {snap.EntityId}: unit id '{snap.UnitId}' is not registered on this client.");
			return Entity.Null;
		}

		Entity worldEntity = FindWorldEntity();
		if (worldEntity == Entity.Null || !GameContext.World.Has<NetworkMappingState>(worldEntity)) return Entity.Null;

		ref var mapping = ref GameContext.World.Get<NetworkMappingState>(worldEntity);
		if (mapping.ServerToClientEntityMap.TryGetValue(snap.EntityId, out Entity alreadySpawned))
		{
			GD.PushWarning($"[Network] Replicated entity {snap.EntityId} ('{snap.UnitId}') is already local entity {alreadySpawned.Id} at sequence {LastAppliedSnapshotSequenceForDiagnostics}; the duplicate spawn is ignored.");
			modelPath = string.Empty;
			return Entity.Null;
		}

		Entity spawned = ApplySnapshotToEntity(snap.UnitId, snap.EntityId, snap.OwnerPlayerEntityId, snap.Position.ToNumerics(), System.Numerics.Vector3.Zero, snap.RotationY, snap.CurrentHp, snap.MaxHp, snap.IsBuilding, meta, ref mapping, getFallbackModelPath, out modelPath, out playerIndex, snap.OwnerPlayerIndex);
		if (spawned != Entity.Null)
		{
			// The mapping exists only after this call, so the reconciliation pass that runs before the snapshot is applied
			// could not have sampled the unit. Anchoring it here gives prediction an authoritative position from the
			// moment the unit exists on the client.
			GameContext.MovementPredictionService?.AdoptSpawnedEntity(spawned.Id, snap.Position.ToNumerics(), LastAppliedSnapshotSequenceForDiagnostics);
		}

		return spawned;
	}

	public static Entity ApplySnapshotToEntity(string unitId, int entityId, int ownerPlayerEntityId, System.Numerics.Vector3 position, System.Numerics.Vector3 velocity, float rotationY, float currentHp, float maxHp, bool isBuilding, UnitMetadata meta, ref NetworkMappingState mapping, Func<string, bool, string> getFallbackModelPath, out string modelPath, out int playerIndex, int ownerPlayerIndex = -1)
	{
		Entity ownerPlayerEntity = mapping.PlayerEntities != null && mapping.PlayerEntities.Length > 1 ? mapping.PlayerEntities[1] : Entity.Null;
		if (ownerPlayerIndex >= 0 && mapping.PlayerEntities != null && ownerPlayerIndex < mapping.PlayerEntities.Length)
		{
			ownerPlayerEntity = mapping.PlayerEntities[ownerPlayerIndex];
		}
		else if (ownerPlayerEntityId >= 0 && mapping.PlayerEntities != null && ownerPlayerEntityId < mapping.PlayerEntities.Length)
		{
			ownerPlayerEntity = mapping.PlayerEntities[ownerPlayerEntityId];
		}

		if (ownerPlayerIndex < 0 && mapping.PeerIdToPlayerEntityMap.TryGetValue(ownerPlayerEntityId, out var pe))
		{
			ownerPlayerEntity = pe;
		}

		if (ownerPlayerEntity == Entity.Null && mapping.PlayerEntities != null && mapping.PlayerEntities.Length > 1)
		{
			ownerPlayerEntity = mapping.PlayerEntities[1];
		}

		playerIndex = ownerPlayerIndex >= 0
			? ownerPlayerIndex
			: ((ownerPlayerEntity != Entity.Null && GameContext.World.Has<UnitOwnerPlayer>(ownerPlayerEntity))
				? GameContext.World.Get<UnitOwnerPlayer>(ownerPlayerEntity).PlayerIndex
				: (ownerPlayerEntityId > 0 ? ownerPlayerEntityId - 1 : 0));

		var entity = GameContext.World.Create();
		GameContext.World.Add(entity, new DefinitionId(unitId));
		GameContext.World.Add(entity, new Name(meta.Name));
		PositionWriteTrace.Mark(entity, PositionWriter.Spawn);
		GameContext.World.Add(entity, new Position(position));
		GameContext.World.Add(entity, new Owner(ownerPlayerEntity.AsPlayerEntity(GameContext.World)));
		GameContext.World.Add(entity, new UnitOwnerPlayer(playerIndex));
		GameContext.World.Add(entity, new Health(currentHp, maxHp));
		if (meta.Damage > 0)
		{
			GameContext.World.Add(entity, new Attack(meta.Damage, meta.Range, meta.AttackCooldown));
		}

		GameContext.World.Add(entity, new Resistances(meta.Armor, meta.MagicResistance));
		if (isBuilding)
		{
			GameContext.World.Add(entity, new Building());
		}
		else
		{
			GameContext.World.Add(entity, new MovementStats(meta.Speed, 20f, 10f));
			GameContext.World.Add(entity, new Movable());
			GameContext.World.Add(entity, new Inventory());
			GameContext.World.Add(entity, new Realm.Ecs.Components.Movement.PathingFlags(GameHost.GetUnitPathingFlags(meta)));
		}

		var interpolationTarget = new InterpolationTarget
		{
			Position = position,
			Velocity = velocity,
			RotationY = rotationY
		};
		GameContext.World.Add(entity, interpolationTarget);

		mapping.ServerToClientEntityMap[entityId] = entity;
		mapping.ClientToServerEntityMap[entity.Id] = entityId;
		Realm.Ecs.Replication.ReplicatedEntityReferences.Register(entityId, entity.Id);
		GameContext.NetworkService?.RefixReplicatedReferences(entityId);

		string targetModel = !string.IsNullOrEmpty(meta.ModelPath) ? meta.ModelPath : unitId;
		modelPath = getFallbackModelPath(targetModel, isBuilding);
		return entity;
	}

	private void ApplyReplicatedColdPayloads(WorldSnapshot snapshot)
	{
		if (snapshot.ColdPayloads == null) return;

		for (int i = 0; i < snapshot.ColdPayloads.Count; i++)
		{
			ApplyColdPayloadDelta(snapshot.ColdPayloads[i]);
		}
	}



	private bool ApplyReplicatedComponentState(WorldSnapshot snapshot)
	{
		bool allApplied = true;
		if (snapshot.ComponentDeltas != null)
		{
			for (int i = 0; i < snapshot.ComponentDeltas.Count; i++)
			{
				if (!ApplyComponentDelta(snapshot.ComponentDeltas[i], snapshot.Sequence)) allApplied = false;
			}
		}

		if (snapshot.AbilityStates != null)
		{
			for (int i = 0; i < snapshot.AbilityStates.Count; i++)
			{
				ApplyAbilityStateDelta(snapshot.AbilityStates[i]);
			}
			GameContext.RegistryService?.ClearAbilityStateDirty();
		}

		if (snapshot.StaticTexts != null)
		{
			for (int i = 0; i < snapshot.StaticTexts.Count; i++)
			{
				ApplyStaticTextDelta(snapshot.StaticTexts[i]);
			}
		}

		if (snapshot.Catalog != null)
		{
			var registry = GameContext.RegistryService;
			if (registry != null)
			{
				for (int i = 0; i < snapshot.Catalog.Count; i++)
				{
					var entry = snapshot.Catalog[i];
					registry.ApplyCatalogEntry(entry.Channel, entry.Key, entry.Value, entry.Number);
					_appliedCatalogCount++;
				}
			}
		}

		if (snapshot.IsBaseline && snapshot.StateFingerprint != 0u)
		{
			VerifyReplicatedStateFingerprint(snapshot.Sequence, snapshot.StateFingerprint);
		}

		return allApplied;
	}

	/// <summary>
	///     Applies the changes that arrived for a unit before its replica existed. The spawns are flushed just before this
	///     runs, so a unit of this same snapshot has a replica by now and its changes land as they normally would. One
	///     whose replica is still missing is retried for the grace and then given up on, and the resulting baseline is
	///     asked for here rather than waited for: the match's repair of a peer that acknowledged a hole is rate limited,
	///     so a peer that waited on it held state it could have replaced in one round trip.
	/// </summary>
	private void ApplyPendingUnitComponentDeltas(int sequence)
	{
		if (_pendingUnitComponentDeltas.Count == 0) return;

		_pendingUnitComponentDeltaKeys.Clear();
		foreach ((int Key, int ComponentTypeId) key in _pendingUnitComponentDeltas.Keys) _pendingUnitComponentDeltaKeys.Add(key);

		bool anUnplaceableChangeWasDropped = false;
		for (int index = 0; index < _pendingUnitComponentDeltaKeys.Count; index++)
		{
			(int Key, int ComponentTypeId) key = _pendingUnitComponentDeltaKeys[index];
			if (!_pendingUnitComponentDeltas.TryGetValue(key, out PendingUnitComponentDelta pending)) continue;

			if (TryGetLocalEntity(key.Key, out _))
			{
				_pendingUnitComponentDeltas.Remove(key);
				ApplyComponentDelta(pending.Delta, sequence);
				continue;
			}

			if (sequence - pending.FirstSeenSequence < UnplacedUnitDeltaGraceSequences) continue;

			_pendingUnitComponentDeltas.Remove(key);
			anUnplaceableChangeWasDropped = true;
		}

		if (anUnplaceableChangeWasDropped) RequestSnapshotBaseline();
	}

	private void ApplyColdPayloadDelta(ColdPayloadDelta delta)
	{
		_mirror.RecordColdPayload(delta);
		if (delta.Destroyed != 0) return;

		switch ((ColdPayloadKind)delta.Kind)
		{
			case ColdPayloadKind.ContentIdPayload:
			{
				int registeredContentId = ContentIdStore.GetOrAddBytes(delta.ContentKind, delta.Bytes);
				if (registeredContentId != delta.ContentId)
				{
					GD.PushWarning($"[Network] Cold payload content id {delta.ContentId} registered as {registeredContentId}; the payload bytes disagree with the sender's handle.");
				}

				break;
			}
			case ColdPayloadKind.LeaderboardBoards:
			{
				ColdPayloadCodecs.ApplyLeaderboards(GameContext.World, FindWorldEntity(), delta.Bytes);
				GameContext.Host?.RefreshReplicatedOverlayUi();
				break;
			}
			case ColdPayloadKind.SummaryTable:
			{
				ColdPayloadCodecs.ApplySummaryTable(GameContext.World, FindWorldEntity(), delta.Bytes);
				GameContext.Host?.RefreshReplicatedOverlayUi();
				break;
			}
		}

		_appliedColdPayloadCount++;
	}

	private bool ApplyComponentDelta(ComponentDelta delta, int snapshotSequence)
	{
		var codec = ReplicatedComponents.Find(delta.ComponentTypeId);
		if (codec == null)
		{
			GD.PushWarning($"[Network] Received delta for unregistered component type {delta.ComponentTypeId}");
			return false;
		}

		if (delta.Bytes == null || (delta.Bytes.Length != 0 && delta.Bytes.Length != codec.SizeInBytes))
		{
			GD.PushWarning($"[Network] Discarded {codec.ComponentName} delta with {(delta.Bytes?.Length ?? 0)} bytes, expected {codec.SizeInBytes}");
			return false;
		}

		Entity targetEntity = Entity.Null;
		if ((ReplicatedScope)delta.Scope == ReplicatedScope.Player)
		{
			Entity worldEntity = FindWorldEntity();
			if (worldEntity == Entity.Null || !GameContext.World.Has<NetworkMappingState>(worldEntity)) return false;
			var mapping = GameContext.World.Get<NetworkMappingState>(worldEntity);
			if (mapping.PlayerEntities == null || delta.Key < 0 || delta.Key >= mapping.PlayerEntities.Length) return false;
			targetEntity = mapping.PlayerEntities[delta.Key];
		}
		else if ((ReplicatedScope)delta.Scope == ReplicatedScope.Unit)
		{
			if (!TryGetLocalEntity(delta.Key, out targetEntity))
			{
				if (codec.ComponentTypeId != ComponentType<GroundLoot>.Id || delta.Bytes.Length == 0)
				{
					if (!_pendingUnitComponentDeltas.ContainsKey((delta.Key, delta.ComponentTypeId)))
					{
						_pendingUnitComponentDeltas[(delta.Key, delta.ComponentTypeId)] = new PendingUnitComponentDelta
						{
							Delta = delta,
							FirstSeenSequence = snapshotSequence
						};
					}

					return false;
				}
				Entity worldEntity = FindWorldEntity();
				if (worldEntity == Entity.Null || !GameContext.World.Has<NetworkMappingState>(worldEntity)) return false;
				targetEntity = CreateReplicatedLootEntity(delta.Key, GameContext.World.Get<NetworkMappingState>(worldEntity));
			}
		}
		else if ((ReplicatedScope)delta.Scope == ReplicatedScope.World)
		{
			targetEntity = GameContext.Simulation.WorldEntity;
		}

		if (targetEntity == Entity.Null || !GameContext.World.IsAlive(targetEntity)) return false;
		bool isOrderState = delta.ComponentTypeId == ComponentType<MoveTo>.Id;
		if (delta.Bytes.Length == 0)
		{

			codec.Remove(GameContext.World, targetEntity);
			if (isOrderState && PredictionDiagnostics.IsRecording)
			{
				PredictionDiagnostics.RecordAppliedOrder(snapshotSequence, delta.Key, false, System.Numerics.Vector3.Zero);
			}

			if (codec.ComponentTypeId == ComponentType<GroundLoot>.Id)
			{
				Entity worldEntity = FindWorldEntity();
				if (worldEntity != Entity.Null && GameContext.World.Has<NetworkMappingState>(worldEntity))
				{
					DestroyReplicatedLootEntity(delta.Key, targetEntity, GameContext.World.Get<NetworkMappingState>(worldEntity));
				}
			}
		}
		else
		{
			codec.Apply(GameContext.World, targetEntity, delta.Bytes);
			if (isOrderState && PredictionDiagnostics.IsRecording && GameContext.World.Has<MoveTo>(targetEntity))
			{
				PredictionDiagnostics.RecordAppliedOrder(snapshotSequence, delta.Key, true, GameContext.World.Get<MoveTo>(targetEntity).Target);
			}

			if (codec.ComponentTypeId == ComponentType<GroundLoot>.Id) GameContext.Host?.SyncReplicatedLootNode(delta.Key, targetEntity);
		}

		RecordReplicatedReference(delta, targetEntity, codec);

		_mirror.RecordComponent(delta);
		_appliedComponentDeltaCount++;
		// A delta names a slot in the unit's ability list, so a list that was replaced renumbers every slot. The peer
		// drops the flags it attached to the old numbering and takes the authority's exception set for the new list.
		if (delta.ComponentTypeId == ComponentType<AbilityState>.Id && (ReplicatedScope)delta.Scope == ReplicatedScope.Unit)
		{
			GameContext.RegistryService?.ClearAbilityStateForUnit(new UnitId(targetEntity.Id));
			_mirror.RemoveAbilitiesForEntity(delta.Key);
		}
		return true;
	}

	private void RecordReplicatedReference(ComponentDelta delta, Entity referrer, IReplicatedComponentCodec codec)
	{
		if (codec is not INormalizingComponentCodec normalizing) return;

		if (delta.Bytes == null || delta.Bytes.Length == 0)
		{
			ForgetReplicatedReference(referrer.Id, codec.ComponentTypeId);
			return;
		}

		int referencedServerEntityId = normalizing.ReadReferencedServerEntityId(delta.Bytes);
		var referenceKey = (referrer.Id, codec.ComponentTypeId);
		if (_referencedServerEntityIds.TryGetValue(referenceKey, out int previousServerEntityId))
		{
			if (previousServerEntityId == referencedServerEntityId) return;
			ForgetReplicatedReference(referrer.Id, codec.ComponentTypeId);
		}

		if (referencedServerEntityId < 0) return;

		if (!_referrersByServerEntityId.TryGetValue(referencedServerEntityId, out var referrers))
		{
			referrers = new List<(int LocalEntityId, IReplicatedComponentCodec Codec)>(4);
			_referrersByServerEntityId[referencedServerEntityId] = referrers;
		}

		referrers.Add((referrer.Id, codec));
		_referencedServerEntityIds[referenceKey] = referencedServerEntityId;

		if (!_referencedComponentTypesByLocalEntity.TryGetValue(referrer.Id, out List<int>? componentTypeIds))
		{
			componentTypeIds = new List<int>(2);
			_referencedComponentTypesByLocalEntity[referrer.Id] = componentTypeIds;
		}

		if (!componentTypeIds.Contains(codec.ComponentTypeId)) componentTypeIds.Add(codec.ComponentTypeId);
	}

	/// <summary>
	///     Forgets every reference one local entity holds. A local entity that is retired from the world stops being the
	///     thing its references describe, and the identifier it used is handed to the next entity this screen creates. A
	///     reference kept past that point is applied to whoever took the identifier the next time its target is described,
	///     which put a target and an attacker on units the authority never said had either.
	/// </summary>
	/// <summary>
	///     Whether this screen holds a reference it was told about but cannot name. The authority names a unit by the
	///     identifier both processes know it by, and the peer translates it into the entity it holds for that unit; a unit
	///     the peer was never told about has no such entity, so the reference stays empty. That is the shroud working, not
	///     a missing update: the peer is not allowed to know which of the units it cannot see attacked it.
	/// </summary>
	public bool IsReferenceUnresolved(int localEntityId, int componentTypeId)
	{
		if (!_referencedServerEntityIds.TryGetValue((localEntityId, componentTypeId), out int serverEntityId)) return false;

		return ReplicatedEntityReferences.Resolve(new Entity(serverEntityId)) == Entity.Null;
	}

	public void ForgetReferencesHeldBy(int localEntityId)
	{
		if (!_referencedComponentTypesByLocalEntity.Remove(localEntityId, out List<int>? componentTypeIds)) return;

		for (int index = 0; index < componentTypeIds.Count; index++) ForgetReplicatedReference(localEntityId, componentTypeIds[index]);
	}

	private void ForgetReplicatedReference(int localEntityId, int componentTypeId)
	{
		var referenceKey = (localEntityId, componentTypeId);

		if (_referencedComponentTypesByLocalEntity.TryGetValue(localEntityId, out List<int>? heldComponentTypeIds))
		{
			heldComponentTypeIds.Remove(componentTypeId);
			if (heldComponentTypeIds.Count == 0) _referencedComponentTypesByLocalEntity.Remove(localEntityId);
		}

		if (!_referencedServerEntityIds.TryGetValue(referenceKey, out int serverEntityId)) return;

		_referencedServerEntityIds.Remove(referenceKey);
		if (!_referrersByServerEntityId.TryGetValue(serverEntityId, out var referrers)) return;

		for (int index = referrers.Count - 1; index >= 0; index--)
		{
			if (referrers[index].LocalEntityId != localEntityId || referrers[index].Codec.ComponentTypeId != componentTypeId) continue;
			referrers.RemoveAt(index);
		}

		if (referrers.Count == 0) _referrersByServerEntityId.Remove(serverEntityId);
	}

	private void RefixReplicatedReferences(int serverEntityId)
	{
		if (!_referrersByServerEntityId.TryGetValue(serverEntityId, out var referrers)) return;

		Span<byte> payload = stackalloc byte[sizeof(int)];
		BitConverter.TryWriteBytes(payload, serverEntityId);

		for (int index = 0; index < referrers.Count; index++)
		{
			var referrer = referrers[index];
			Entity localEntity = new(referrer.LocalEntityId);
			if (!GameContext.World.IsAlive(localEntity)) continue;

			referrer.Codec.Apply(GameContext.World, localEntity, payload);
		}
	}

	private static Entity CreateReplicatedLootEntity(int serverEntityId, NetworkMappingState mapping)
	{
		Entity localEntity = GameContext.World.Create();
		mapping.ServerToClientEntityMap[serverEntityId] = localEntity;
		mapping.ClientToServerEntityMap[localEntity.Id] = serverEntityId;
		ReplicatedEntityReferences.Register(serverEntityId, localEntity.Id);
		GameContext.NetworkService?.RefixReplicatedReferences(serverEntityId);
		return localEntity;
	}

	private static void DestroyReplicatedLootEntity(int serverEntityId, Entity localEntity, NetworkMappingState mapping)
	{
		GameContext.Host?.RemoveLootItemLocal(serverEntityId);
		ReplicatedEntityReferences.Unregister(serverEntityId);
		mapping.ServerToClientEntityMap.Remove(serverEntityId);
		mapping.ClientToServerEntityMap.Remove(localEntity.Id);
		if (GameContext.World.IsAlive(localEntity)) GameContext.World.Destroy(localEntity);
	}

	private void ApplyAbilityStateDelta(AbilityStateDelta delta)
	{
		_mirror.RecordAbility(delta);
		_appliedAbilityStateCount++;
		ApplyAbilityStateToUnit(delta.ServerEntityId, delta.AbilityIndex, delta.Flags, delta.AbilityId);
	}

	private void ApplyAbilityStateToUnit(int serverEntityId, short abilityIndex, byte flags, string? namedAbilityId = null)
	{
		if (!TryGetLocalEntity(serverEntityId, out var localEntity)) return;

		// A change about an ability the unit does not list is named rather than slotted, which is how a build option the
		// map withheld reaches a screen even though the unit can never cast it.
		string? abilityId = string.IsNullOrEmpty(namedAbilityId) ? ResolveAbilityIdAt(localEntity, abilityIndex) : namedAbilityId;
		if (abilityId == null) return;

		GameContext.RegistryService?.SetAbilityState(new UnitId(localEntity.Id), abilityId, (flags & 1) != 0, (flags & 2) != 0);
	}

	private static string? ResolveAbilityIdAt(Entity localEntity, short abilityIndex)
	{
		if (!GameContext.World.Has<AbilityState>(localEntity)) return null;

		return GameContext.World.Get<AbilityState>(localEntity).Get(abilityIndex);
	}

	public void ReapplyAbilityStatesToUnits()
	{
		if (_mirror.Abilities.Count == 0 && _mirror.AbilitiesById.Count == 0) return;

		foreach (var kvp in _mirror.Abilities)
		{
			ApplyAbilityStateToUnit(kvp.Key.ServerEntityId, kvp.Key.AbilityIndex, kvp.Value);
		}

		foreach (var kvp in _mirror.AbilitiesById)
		{
			ApplyAbilityStateToUnit(kvp.Key.ServerEntityId, -1, kvp.Value, kvp.Key.AbilityId);
		}

		GameContext.RegistryService?.ClearAbilityStateDirty();
	}

	private void ApplyStaticTextDelta(StaticTextDelta delta)
	{
		_mirror.RecordText(delta);
		_appliedStaticTextCount++;
		GameContext.Host?.ApplyStaticTextDelta(delta);
	}

	private void VerifyReplicatedStateFingerprint(int sequence, uint hostFingerprint)
	{
		uint localFingerprint = _mirror.ComputeFingerprint(ReadPlayerComponentRecordsFromEcs());
		localFingerprint = ReplicatedStateFingerprint.CombineWeatherState(localFingerprint, GameContext.World, FindWorldEntity());
		if (localFingerprint == hostFingerprint)
		{
			if (!_fingerprintMatchLogged)
			{
				_fingerprintMatchLogged = true;
				GD.Print($"[Replication] client fingerprint match at sequence {sequence} (deltas={_appliedComponentDeltaCount}, abilities={_appliedAbilityStateCount}, texts={_appliedStaticTextCount}, catalog={_appliedCatalogCount})");
			}
			return;
		}

		GD.PushError($"[Network] Replicated state fingerprint mismatch at sequence {sequence}: host={hostFingerprint:x8} client={localFingerprint:x8}");
		MultiplayerSyncDiagnostics.RecordFingerprintMismatch(sequence, hostFingerprint, localFingerprint);
	}

	private int _replicatedEcsDriftCheckCounter;

	public void VerifyReplicatedEcsDriftDomain()
	{
		_replicatedEcsDriftCheckCounter++;
		if (_replicatedEcsDriftCheckCounter % 30 != 0) return;

		Entity worldEntity = FindWorldEntity();
		if (worldEntity == Entity.Null || !GameContext.World.Has<NetworkMappingState>(worldEntity)) return;
		var mapping = GameContext.World.Get<NetworkMappingState>(worldEntity);

		var codecs = ReplicatedComponents.All;
		for (int codecIndex = 0; codecIndex < codecs.Count; codecIndex++)
		{
			var codec = codecs[codecIndex];
			if (codec.Scope != ReplicatedScope.Player) continue;

			for (int slot = 0; slot < Realm.Ecs.EcsWorld.MAX_PLAYERS; slot++)
			{
				if (mapping.PlayerEntities == null || slot >= mapping.PlayerEntities.Length) continue;
				if (!_mirror.Components.TryGetValue(((byte)ReplicatedScope.Player, slot, codec.ComponentTypeId), out var expected)) continue;

				Entity playerEntity = mapping.PlayerEntities[slot];
				if (playerEntity == Entity.Null || !GameContext.World.IsAlive(playerEntity)) continue;

				if (_replicatedEcsDriftBuffer.Length < codec.SizeInBytes) _replicatedEcsDriftBuffer = new byte[codec.SizeInBytes];
				var actual = _replicatedEcsDriftBuffer.AsSpan(0, codec.SizeInBytes);
				if (!codec.TryWrite(GameContext.World, playerEntity, actual))
				{
					ReportReplicatedEcsDrift(slot, codec.ComponentName, "component missing");
					continue;
				}

				if (!actual.SequenceEqual(expected))
			{
					ReportReplicatedEcsDrift(slot, codec.ComponentName, "bytes differ from the last authoritative value");
				}
			}
		}
	}

	private void ReportReplicatedEcsDrift(int slot, string componentName, string reason)
	{
		if (_reportedEcsDrift.Contains((slot, componentName))) return;
		_reportedEcsDrift.Add((slot, componentName));
		GD.PushError($"[Replication] local write clobbered replicated state: {componentName} for player slot {slot} ({reason})");
		MultiplayerSyncDiagnostics.RecordEcsDrift(slot, componentName);
	}

	private readonly HashSet<(int Slot, string ComponentName)> _reportedEcsDrift = new();
	private byte[] _replicatedEcsDriftBuffer = new byte[16];

	private List<(int Key, int ComponentTypeId, byte[] Bytes)> ReadPlayerComponentRecordsFromEcs()
	{
		var records = new List<(int Key, int ComponentTypeId, byte[] Bytes)>();
		Entity worldEntity = FindWorldEntity();
		if (worldEntity == Entity.Null || !GameContext.World.Has<NetworkMappingState>(worldEntity)) return records;

		var mapping = GameContext.World.Get<NetworkMappingState>(worldEntity);
		var codecs = ReplicatedComponents.All;
		for (int codecIndex = 0; codecIndex < codecs.Count; codecIndex++)
		{
			var codec = codecs[codecIndex];
			if (codec.Scope != ReplicatedScope.Player) continue;

			for (int slot = 0; slot < Realm.Ecs.EcsWorld.MAX_PLAYERS; slot++)
			{
				if (mapping.PlayerEntities == null || slot >= mapping.PlayerEntities.Length) continue;
				Entity playerEntity = mapping.PlayerEntities[slot];
				if (playerEntity == Entity.Null || !GameContext.World.IsAlive(playerEntity)) continue;

				var bytes = new byte[codec.SizeInBytes];
				if (!codec.TryWrite(GameContext.World, playerEntity, bytes)) continue;
				records.Add((slot, codec.ComponentTypeId, bytes));
			}
		}

		return records;
	}

	public bool TryGetLocalEntity(int serverEntityId, out Entity localEntity)
	{
		localEntity = Entity.Null;
		Entity worldEntity = FindWorldEntity();
		if (worldEntity == Entity.Null || !GameContext.World.Has<NetworkMappingState>(worldEntity)) return false;
		var mapping = GameContext.World.Get<NetworkMappingState>(worldEntity);
		return mapping.ServerToClientEntityMap.TryGetValue(serverEntityId, out localEntity);
	}

	public void SetBackupResources(float gold, float wood, float stone)
	{
		var worldQuery = QueryCache.AllReplayStateQuery;
		GameContext.World.Query(in worldQuery, (Entity entity) =>
		{
			ref var state = ref GameContext.World.Get<ReplayState>(entity);
			state.GoldBackup = gold;
			state.WoodBackup = wood;
			state.StoneBackup = stone;
		});
	}


	
	public ServerCommandResult ExecuteServerCommand(int peerId, NetworkCommand cmd)
	{
		return GameContext.CommandAuthority.ExecuteServerCommand(peerId, cmd);
	}

	public bool TryGetPlayerEntityForPeer(int peerId, out Entity playerEntity)
	{
		return GameContext.CommandAuthority.TryGetPlayerEntityForPeer(peerId, out playerEntity);
	}

	public bool ValidateSpellCommandForHarness(int peerId, NetworkCommand cmd, out string rejectionReason)
	{
		return GameContext.CommandAuthority.ValidateSpellCommandForHarness(peerId, cmd, out rejectionReason);
	}

	private readonly Dictionary<int, List<(int LocalEntityId, IReplicatedComponentCodec Codec)>> _referrersByServerEntityId = new();
	private readonly Dictionary<(int LocalEntityId, int ComponentTypeId), int> _referencedServerEntityIds = new();

	/// <summary>
	///     The components each local entity holds a reference through, so retiring it forgets exactly those without a
	///     walk of every reference this screen keeps. A local entity identifier is recycled the moment the entity is
	///     destroyed, so a reference that outlives its holder is not merely stale: it is a reference the next entity to
	///     take that identifier will be given when the server entity it names is described again.
	/// </summary>
	private readonly Dictionary<int, List<int>> _referencedComponentTypesByLocalEntity = new();

	private Entity FindWorldEntity()
	{
		Entity worldEntity = GameContext.Simulation.WorldEntity;
		if (GameContext.World == null || worldEntity == Entity.Null) return Entity.Null;
		if (!GameContext.World.IsAlive(worldEntity)) return Entity.Null;
		return GameContext.World.Has<NetworkState>(worldEntity) ? worldEntity : Entity.Null;
	}

	public bool WasClientInMultiplayer { get; private set; } = false;
	public bool IsConnectionLost { get; private set; } = false;

	public int LocalPeerId
	{
		get
		{
			var worldEntity = FindWorldEntity();
			return worldEntity != Entity.Null && GameContext.World.Has<NetworkState>(worldEntity)
				? GameContext.World.Get<NetworkState>(worldEntity).LocalPeerId
				: 1;
		}
		set
		{
			var worldEntity = FindWorldEntity();
			if (worldEntity != Entity.Null && GameContext.World.Has<NetworkState>(worldEntity))
			{
				ref var state = ref GameContext.World.Get<NetworkState>(worldEntity);
				state.LocalPeerId = value;
			}
		}
	}

	public float CommandSendTimer
	{
		get
		{
			var worldEntity = FindWorldEntity();
			return worldEntity != Entity.Null && GameContext.World.Has<NetworkState>(worldEntity)
				? GameContext.World.Get<NetworkState>(worldEntity).CommandSendTimer
				: 0f;
		}
		set
		{
			var worldEntity = FindWorldEntity();
			if (worldEntity != Entity.Null && GameContext.World.Has<NetworkState>(worldEntity))
			{
				ref var state = ref GameContext.World.Get<NetworkState>(worldEntity);
				state.CommandSendTimer = value;
			}
		}
	}

	public ulong LastSnapshotReceivedTime
	{
		get
		{
			var worldEntity = FindWorldEntity();
			return worldEntity != Entity.Null && GameContext.World.Has<NetworkState>(worldEntity)
				? GameContext.World.Get<NetworkState>(worldEntity).LastSnapshotReceivedTime
				: 0;
		}
		set
		{
			var worldEntity = FindWorldEntity();
			if (worldEntity != Entity.Null && GameContext.World.Has<NetworkState>(worldEntity))
			{
				ref var state = ref GameContext.World.Get<NetworkState>(worldEntity);
				state.LastSnapshotReceivedTime = value;
			}
		}
	}

	public void MarkClientEnteredMultiplayer()
	{
		WasClientInMultiplayer = true;
		LastSnapshotReceivedTime = Time.GetTicksMsec();
	}

	public void BroadcastMessageDomainLogic(string message) => GameLog.LogToConsole($"[HOST BROADCAST] {message}");

	/// <summary>
	///     Marks the match lost because the process running it ended. The screen shows this instead of waiting for a
	///     snapshot that will never arrive.
	/// </summary>
	public void ReportAuthorityLost() => IsConnectionLost = true;

	public void UpdateConnectionStatus(bool multiplayerActive, bool isServer)
	{
		bool isLost;
		if (multiplayerActive && !isServer)
		{
			ulong now = Time.GetTicksMsec();
			ulong lastReceived = LastSnapshotReceivedTime;
			if (lastReceived > 0)
			{
				double timeSinceLastSnapshot = (now - lastReceived) / 1000.0;
				isLost = timeSinceLastSnapshot > 30.0;
			}
			else
			{
				isLost = false;
			}
		}
		else
		{
			isLost = false;
		}

		IsConnectionLost = isLost;
	}

	private static readonly Dictionary<int, int> _playerTeams = new();
	private static readonly Dictionary<(int, int), bool> _alliances = new();

	public static void SetPlayerTeam(int playerIndex, int teamIndex)
	{
		_playerTeams[playerIndex] = teamIndex;
	}

	public static int GetPlayerTeam(int playerIndex)
	{
		if (_playerTeams.TryGetValue(playerIndex, out int team)) return team;
		return playerIndex;
	}

	public static void SetPlayersAllied(int playerIndex1, int playerIndex2, bool allied)
	{
		_alliances[(playerIndex1, playerIndex2)] = allied;
		_alliances[(playerIndex2, playerIndex1)] = allied;
	}

	public static bool ArePlayerIndicesEnemies(int playerIndex1, int playerIndex2)
	{
		if (playerIndex1 == playerIndex2) return false;

		if (GameContext.SimulationService != null)
		{
			return GameContext.SimulationService.ArePlayersEnemies(playerIndex1, playerIndex2);
		}

		if (!HasActiveLobby()) return playerIndex1 != playerIndex2;

		int t1 = GetTeamIndexForPlayerIndex(playerIndex1, playerIndex1);
		int t2 = GetTeamIndexForPlayerIndex(playerIndex2, playerIndex2);
		return t1 != t2;
	}

	private static bool HasActiveLobby()
	{
		if (GameContext.Lobby == null) return false;
		if (GameContext.Lobby.PlayerList == null) return false;
		return GameContext.Lobby.PlayerList.Count > 0;
	}

	private static int GetTeamIndexForPeer(int peerId, int defaultTeam)
	{
		var list = GameContext.Lobby.PlayerList;
		for (int i = 0; i < list.Count; i++)
		{
			if (list[i].PeerId == peerId)
			{
				return list[i].TeamIndex;
			}
		}
		return defaultTeam;
	}

	private static int GetTeamIndexForPlayerIndex(int playerIndex, int defaultTeam)
	{
		var list = GameContext.Lobby.PlayerList;
		for (int i = 0; i < list.Count; i++)
		{
			if (list[i].Slot == playerIndex)
			{
				return list[i].TeamIndex;
			}
		}
		return defaultTeam;
	}

	public void StartCountdownTimer(float duration, string label, bool multiplayerActive, bool isServerActive, Action<float, string> startView, Action<float, string> rpcAction)
	{
		startView?.Invoke(duration, label);
		if (multiplayerActive && isServerActive)
		{
			rpcAction?.Invoke(duration, label);
		}
	}

	public void BroadcastActionIfServer(bool isServer, Action rpcAction)
	{
		if (isServer && rpcAction != null)
		{
			rpcAction.Invoke();
		}
	}

	public void SetWeather(string weatherType, bool isMultiplayer, bool isServer, Action<string> viewAction, Action<string> rpcAction)
	{
		viewAction?.Invoke(weatherType);
		if (isMultiplayer && isServer) rpcAction?.Invoke(weatherType);
	}

	public void RemoveMultiplayerMapping(int entityId)
	{
		ForgetReferencesHeldBy(entityId);

		if (!GameContext.World.TryGet<NetworkMappingState>(GameContext.Simulation.WorldEntity, out var mapState)) return;

		if (mapState.ClientToServerEntityMap.TryGetValue(entityId, out int serverEntityId))
		{
			mapState.ServerToClientEntityMap.Remove(serverEntityId);
			ReplicatedEntityReferences.Unregister(serverEntityId);
		}

		mapState.ClientToServerEntityMap.Remove(entityId);
	}

	public async Task CheckCreatorRegistrationAsync(string pubKeyStr, string seedServerUrl, Action<string> onNotFound)
	{
		try
		{
			using (var httpClient = new HttpClient())
			{
				httpClient.Timeout = TimeSpan.FromSeconds(3);
				var res = await httpClient.GetAsync(seedServerUrl + "/api/creators/check/" + Uri.EscapeDataString(pubKeyStr));
				if (res.StatusCode == HttpStatusCode.NotFound)
				{
					onNotFound?.Invoke(pubKeyStr);
				}
			}
		}
		catch (Exception ex)
		{
			GD.Print($"[NetworkService] Offline or registry server unreachable, skipping creator registration prompt: {ex.Message}");
		}
	}
}