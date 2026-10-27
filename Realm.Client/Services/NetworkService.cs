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
using Realm.MapAPI;
using Realm.Ecs.Replication;
using Realm.Shared.Network;
using HttpClient = System.Net.Http.HttpClient;

namespace Realm.Client.Services;

public class NetworkService
{
	private const int MaximumQueuedDeltas = 512;

	private readonly List<WorldSnapshot> _queuedDeltas = new();
	private const int BaselineSafetyIntervalSequences = 900;
	/// <summary>
	/// How often a peer that has never acknowledged anything is sent another full baseline. The first one is immediate,
	/// because a joining peer needs it; repeating it every two seconds was the expensive case, since a peer that cannot
	/// apply a snapshot pays ~138 KB each time and a baseline is not what unblocks it. Ten seconds still recovers a peer
	/// that recovered on its own, at a fifth of the cost, and health is measured by the acknowledgement, not by the retry.
	/// </summary>
	private const int BaselineRepeatWhileUnacknowledgedSequences = 300;
	private const int MinimumBaselineRequestSpacingSequences = 30;
	/// <summary>
	/// How long a peer's acknowledgement may stand still before the host answers with a full baseline. This is a stall
	/// window, not a distance from the newest sequence: a peer that is far behind but still confirms every snapshot it
	/// applies is receiving the stream, and a baseline does not unblock it because nothing is blocking it.
	/// </summary>
	private const int BaselineAckStallSequences = 600;

	private const int MaximumPendingComponentAcks = 8192;

	private readonly UnitVisibilityIndex _visibilityIndex = new();
	private readonly Dictionary<int, Vector3> _clientCameraPositions = new();
	private readonly Dictionary<int, int> _lastBaselineSentSequence = new();
	private readonly List<int> _baselinePeerIds = new(8);
	private readonly Dictionary<int, ClientSnapshotState> _clientSnapshotStates = new();
	private int _currentSnapshotSequence;

	private class ClientSnapshotState
	{
		public int AckedSequence = -1;
		public int LastAckAdvanceSequence = -1;
		public AppliedSequenceWindow Applied;
		public int LastBaselineSequence = -1;
		public int AcknowledgedBaselineSequence = -1;
		public bool HasSentBaseline;
		public bool SentCatalog;
		public int BaselineBackoffAttempts;
		public readonly Dictionary<int, UnitSnapshot> AckedUnits = new();
		public readonly Dictionary<int, UnitSnapshot> SentUnits = new();
		public readonly Dictionary<int, int> SentUnitSequences = new();
		public readonly Dictionary<(byte Scope, int Key, int TypeId), byte[]> AckedComponents = new();
		public readonly Dictionary<(byte Scope, int Key, int TypeId), SentComponentEntry> SentComponents = new();
		public readonly List<SentComponentEntry> PendingComponentAcks = new();
		public readonly List<SentUnitEntry> PendingUnitAcks = new();
		public readonly List<UnitComponentPresence> UnitComponentPresence = new();
	}

	private struct SentComponentEntry
	{
		public (byte Scope, int Key, int TypeId) Key;
		public byte[] Bytes;
		public int Sequence;
		public bool Removed;
	}

	private struct SentUnitEntry
	{
		public int EntityId;
		public UnitSnapshot Snapshot;
		public int Sequence;
	}
	private readonly ReplicatedStateMirror _mirror = new();
	private int _appliedComponentDeltaCount;
	private int _appliedColdPayloadCount;
	private int _appliedAbilityStateCount;
	private int _appliedStaticTextCount;
	private int _appliedCatalogCount;
	private bool _fingerprintMatchLogged;
	private readonly List<(int ServerEntityId, int ComponentTypeId, byte[] Bytes)> _fingerprintComponentRecords = new();
	private readonly Dictionary<int, List<DelayedPacket>> _spectatorDelayedPackets = new();

	private readonly Dictionary<int, UnitSnapshot> _pendingUnitSpawns = new();
	private readonly List<Entity> _pendingUnitKills = new();
	private readonly List<Entity> _pendingUnitDespawns = new();
	private readonly List<int> _untrackedServerIds = new();
	private readonly HashSet<int> _baselineLootServerIds = new();
	private readonly List<int> _staleLootServerIds = new();
	private readonly Dictionary<int, ulong> _baselineUnitPresence = new();
	private readonly HashSet<(int Key, int TypeId)> _baselineSentUnitComponents = new();
	private byte[] _baselineReconcileScratch = new byte[256];


	public bool IsPaused { get; set; } = false;
	public int LastAppliedSnapshotSequenceForDiagnostics { get; private set; } = -1;
	public int LastAppliedBaselineSequenceForDiagnostics { get; private set; } = -1;
	public int ResumeCountdownSeconds { get; set; } = -1;
	public float ResumeCountdownTimer { get; set; } = 0f;

	public bool CountdownForcedByHost { get; set; } = false;
	public readonly Dictionary<int, bool> PlayerReadyStates = new();
	public readonly Dictionary<int, bool> DisallowedPausePeers = new();

	public void BroadcastCountdownCancelDomain()
	{
		ResumeCountdownSeconds = -1;
		CountdownForcedByHost = false;
	}
	public void GetPlayerReadyStateDomain(int peerId, out bool ready)
	{
		PlayerReadyStates.TryGetValue(peerId, out ready);
	}
	public void GetPlayerDisallowPauseDomain(int peerId, out bool disallowed)
	{
		DisallowedPausePeers.TryGetValue(peerId, out disallowed);
	}




	public Entity GetSpellCasterEntityDomain(NetworkCommand cmd, List<Unit3D> allUnits)
	{
		if (cmd.UnitEntityIds.Count == 0) return Entity.Null;

		var casterEntity = FindServerEntity(cmd.UnitEntityIds[0], allUnits);
		if (casterEntity != Entity.Null && GameContext.World.IsAlive(casterEntity))
		{
			return casterEntity;
		}

		return Entity.Null;
	}

	public void RemoveClientToServerMapping(int localEntityId)
	{
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


	public void SetGameOver()
	{
		GameContext.Simulation.IsGameOver = true;
	}

	public Entity ResolveReconnectedPlayerEntity(int oldPeerId, int newPeerId, Dictionary<int, Entity> peerIdToPlayerEntityMap)
	{
		Entity playerEntity = Entity.Null;
		if (peerIdToPlayerEntityMap.TryGetValue(oldPeerId, out var existingEntity))
		{
			playerEntity = existingEntity;
			peerIdToPlayerEntityMap.Remove(oldPeerId);
		}
		else if (peerIdToPlayerEntityMap.TryGetValue(newPeerId, out var directEntity))
		{
			playerEntity = directEntity;
		}

		if (playerEntity != Entity.Null)
		{
			peerIdToPlayerEntityMap[newPeerId] = playerEntity;
		}

		return playerEntity;
	}

	public void UpdateReconnectedNetworkMapping(int oldPeerId, int newPeerId, Entity playerEntity)
	{
		if (GameContext.Simulation.WorldEntity == Entity.Null || GameContext.World == null || !GameContext.World.Has<NetworkMappingState>(GameContext.Simulation.WorldEntity)) return;

		if (playerEntity != Entity.Null)
		{
			var mapping = GameContext.World.Get<NetworkMappingState>(GameContext.Simulation.WorldEntity);
			mapping.PeerIdToPlayerEntityMap.Remove(oldPeerId);
			mapping.PeerIdToPlayerEntityMap[newPeerId] = playerEntity;
		}
	}

	public bool TryGetReconnectedCountdown(out float duration, out string text)
	{
		duration = 0f;
		text = string.Empty;

		if (GameContext.Simulation.WorldEntity == Entity.Null || GameContext.World == null || !GameContext.World.Has<CountdownState>(GameContext.Simulation.WorldEntity)) return false;

		var countdown = GameContext.World.Get<CountdownState>(GameContext.Simulation.WorldEntity);
		if (countdown.Active)
		{
			duration = countdown.Duration;
			text = countdown.Text;
			return true;
		}

		return false;
	}

	public Entity AssignReconnectedPlayerEntity(int slot, IEnumerable<PlayerInfo> playerList, int localPeerId, Dictionary<int, Entity> peerIdToPlayerEntityMap)
	{
		Entity playerEntity = Entity.Null;
		foreach (var p in playerList)
		{
			if (p.Slot == slot || p.PeerId == localPeerId)
			{
				if (peerIdToPlayerEntityMap.TryGetValue(localPeerId, out var myEntity))
				{
					playerEntity = myEntity;
				}
			}
		}

		return playerEntity;
	}

	public void UpdateReconnectedClientMapping(Entity playerEntity, int localPeerId)
	{
		if (GameContext.Simulation.WorldEntity == Entity.Null || GameContext.World == null || !GameContext.World.Has<NetworkMappingState>(GameContext.Simulation.WorldEntity)) return;

		var mapping = GameContext.World.Get<NetworkMappingState>(GameContext.Simulation.WorldEntity);
		if (playerEntity != Entity.Null)
		{
			mapping.PeerIdToPlayerEntityMap[localPeerId] = playerEntity;
		}
	}

	public void Clear()
	{
		CountdownForcedByHost = false;
		PlayerReadyStates.Clear();
		DisallowedPausePeers.Clear();
		_queuedDeltas.Clear();
		_clientCameraPositions.Clear();
		_clientSnapshotStates.Clear();
		_coldPayloadContentPerClient.Clear();
		_spectatorDelayedPackets.Clear();
		_pendingUnitSpawns.Clear();
		_pendingUnitKills.Clear();
		_pendingUnitDespawns.Clear();
		_referrersByServerEntityId.Clear();
		_referencedServerEntityIds.Clear();
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

		return localEntityId;
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

	public Prop3D FindClosestProp(System.Numerics.Vector3 position, string propIdType, List<Prop3D> allProps)
	{
		Prop3D closest = null;
		float closestDist = float.MaxValue;
		foreach (var prop in allProps)
		{
			if (GodotObject.IsInstanceValid(prop))
			{
				if (!string.IsNullOrEmpty(propIdType) && prop.PropId != propIdType)
				{
					continue;
				}

				Vector3 godotPos = new Vector3(position.X, position.Y, position.Z);
				float dist = prop.GlobalPosition.DistanceTo(godotPos);
				if (dist < closestDist)
				{
					closestDist = dist;
					closest = prop;
				}
			}
		}

		return closest;
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

	public bool IsServerEntityVisibleToAnyRemoteClient(int serverEntityId)
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
		bool hasRemoteClient = false;

		foreach (PlayerInfo player in lobby.PlayerList)
		{
			if (player.PeerId <= 0 || player.PeerId == localPeerId) continue;
			if (player.IsDisconnected) continue;
			if (!mapping.PeerIdToPlayerEntityMap.TryGetValue(player.PeerId, out Entity playerEntity)) continue;

			hasRemoteClient = true;
			if (IsUnitVisibleToPlayer(playerEntity, unitEntity)) return true;
		}

		return !hasRemoteClient;
	}

	public (bool Success, string BuildUnitType, string TargetModel, PlayerEntity PlayerOwner, System.Numerics.Vector3 BuildPosition, string Name, float MaxHp, float Damage, float Range, float Armor) ProcessServerCommandBuildDomain(
		ServerCommandResult result,
		UnitMetadata? buildMetadata,
		Entity buildPeerOwnerEntity,
		Entity buildWorkerEntity)
	{
		if (!result.NeedsBuildUnit || buildMetadata == null || buildPeerOwnerEntity == Entity.Null) return (false, "", "", default, default, "", 0, 0, 0, 0);

		var meta = buildMetadata;
		var playerOwner = buildPeerOwnerEntity.AsPlayerEntity(GameContext.World);
		string targetModel = !string.IsNullOrEmpty(meta.ModelPath) ? meta.ModelPath : result.BuildUnitType;

		if (buildWorkerEntity != Entity.Null)
		{
			if (!GameContext.World.Has<Owner>(buildWorkerEntity)) return (false, "", "", default, default, "", 0, 0, 0, 0);
			if (GameContext.World.Get<Owner>(buildWorkerEntity).PlayerEntity.Value != playerOwner.Value) return (false, "", "", default, default, "", 0, 0, 0, 0);
		}

		if (!TryChargeBuildingCost(playerOwner.Value, meta))
		{
			GD.PushWarning($"[Build] Rejected '{result.BuildUnitType}' for player {playerOwner.Value.Id}: insufficient resources or missing PlayerResources.");
			return (false, "", "", default, default, "", 0, 0, 0, 0);
		}

		return (true, result.BuildUnitType, targetModel, playerOwner, new System.Numerics.Vector3(result.BuildPosition.X, result.BuildPosition.Y, result.BuildPosition.Z), meta.Name, meta.MaxHp, meta.Damage, meta.Range, meta.Armor);
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
		float bufferTime = 0.1f;
		NetworkLatencyService? latency = GameContext.NetworkLatencyService;
		if (latency != null && latency.HasSample && float.IsFinite(latency.SmoothedRoundTripMilliseconds))
		{
			bufferTime = Mathf.Max(0.1f, (latency.SmoothedRoundTripMilliseconds / 1000f) * 1.5f);
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

	public void RecordClientCameraPosition(int peerId, Vector3 position)
	{
		_clientCameraPositions[peerId] = position;
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
				networkState.LastReceivedBaselineSeq = snapshot.Sequence;
				return;
			}

			networkState.LastReceivedBaselineSeq = snapshot.Sequence;
			networkState.HasReceivedInitialBaseline = true;
			networkState.LastAppliedSnapshotSequence = snapshot.Sequence;
			_queuedDeltas.RemoveAll(d => d.Sequence <= snapshot.Sequence);
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
			QueueDelta(snapshot);
			return;
		}

		networkState.LastAppliedSnapshotSequence = snapshot.Sequence;
		ApplyWorldSnapshot(snapshot);
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

	// The host describes a unit in two precisions: inside the detailed radius of the peer's camera every movement above
	// five centimetres is sent, while outside it a unit is only resent once it has moved more than a metre from what the
	// peer acknowledged. The client cannot tell the two apart from the snapshot it already applied, so the precision is
	// remembered here for the harness to judge the rendered transform against what the host actually promised.
	private readonly HashSet<int> _unitsSentAtDetailedPrecision = new();

	public bool WasUnitSentAtDetailedPrecision(int serverEntityId) => _unitsSentAtDetailedPrecision.Contains(serverEntityId);

	private void RetireClientEntityForUntrackedServerId(NetworkMappingState mapping, Entity localEntity, int serverEntityId)
	{
		if (GameContext.World.IsAlive(localEntity) && !GameContext.World.Has<Dead>(localEntity))
		{
			_pendingUnitDespawns.Add(localEntity);
		}

		_unitsSentAtDetailedPrecision.Remove(serverEntityId);
		mapping.ServerToClientEntityMap.Remove(serverEntityId);
		mapping.ClientToServerEntityMap.Remove(localEntity.Id);
		ReplicatedEntityReferences.Unregister(serverEntityId);
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
		}		var target = new InterpolationTarget
		{
			Position = snap.Position.ToNumerics(),
			Velocity = snap.Velocity.ToNumerics(),
			RotationY = snap.RotationY
		};

		// What the last description asked this client to walk toward, read before the new one replaces it. A step larger
		// than a unit can travel between two applied snapshots is a discontinuity in what the authority is telling this
		// client, not movement: the client stalled and missed descriptions, drained a burst in one tick, or received a
		// reconnect baseline, and a script can also move a unit outright. Smoothing across it means the unit visibly
		// trails the authority for the better part of a second, which is the six metre placement gap the lossy profile
		// used to report at the start of a run. The condition is the cause stated directly - how far the target stepped -
		// rather than a lag bound with a round trip in it.
		System.Numerics.Vector3 previousTargetPosition = GameContext.World.Has<InterpolationTarget>(localEntity)
			? GameContext.World.Get<InterpolationTarget>(localEntity).Position
			: target.Position;
		bool targetJumped = (target.Position - previousTargetPosition).Length() > MaximumPlausibleTargetStepMeters;

		GameContext.World.SetOrAdd(localEntity, target);

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
		if ((descriptionResumed || targetJumped) && !IsUnitOwnedByPrediction(localEntity) && GameContext.World.Has<Position>(localEntity))
		{
			PositionWriteTrace.Mark(localEntity, PositionWriter.Placement);
			GameContext.World.Set(localEntity, new Position(target.Position));
		}

		GameContext.World.SetOrAdd(localEntity, new DescribedAtSequence(sequence));

		if (snap.IsDetailed) _unitsSentAtDetailedPrecision.Add(snap.EntityId);
		else _unitsSentAtDetailedPrecision.Remove(snap.EntityId);
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
		Entity spawned = ApplySnapshotToEntity(snap.UnitId, snap.EntityId, snap.OwnerPlayerEntityId, snap.Position.ToNumerics(), snap.Velocity.ToNumerics(), snap.RotationY, snap.CurrentHp, snap.MaxHp, snap.IsBuilding, meta, ref mapping, getFallbackModelPath, out modelPath, out playerIndex, snap.OwnerPlayerIndex);
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

	public void ApplyReplicatedState(WorldSnapshot snapshot)
	{
		ApplyReplicatedColdPayloads(snapshot);
		ApplyReplicatedComponentState(snapshot);
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

	private void ApplyColdPayloadDelta(ColdPayloadDelta delta)
	{
		_mirror.RecordColdPayload(delta);
		if (delta.Destroyed != 0) return;

		switch ((ColdPayloadKind)delta.Kind)
		{
			case ColdPayloadKind.InternedPayload:
			{
				int registeredContentId = InternedValueStore.GetOrAddBytes(delta.InternedKind, delta.Bytes);
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
				if (codec.ComponentTypeId != ComponentType<GroundLoot>.Id || delta.Bytes.Length == 0) return false;
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
	}

	private void ForgetReplicatedReference(int localEntityId, int componentTypeId)
	{
		var referenceKey = (localEntityId, componentTypeId);
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
		ApplyAbilityStateToUnit(delta.ServerEntityId, delta.AbilityIndex, delta.Flags);
	}

	private void ApplyAbilityStateToUnit(int serverEntityId, short abilityIndex, byte flags)
	{
		if (!TryGetLocalEntity(serverEntityId, out var localEntity)) return;
		string? abilityId = ResolveAbilityIdAt(localEntity, abilityIndex);
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
		if (_mirror.Abilities.Count == 0) return;
		foreach (var kvp in _mirror.Abilities)
		{
			ApplyAbilityStateToUnit(kvp.Key.ServerEntityId, kvp.Key.AbilityIndex, kvp.Value);
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

	public void ResetReplicatedStateMirrors()
	{
		_mirror.Clear();
		_clientSnapshotStates.Clear();
		_coldPayloadContentPerClient.Clear();
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

	public List<(int PeerId, byte[] Payload, bool IsBaseline)> BuildServerSnapshots(int localPeerId, List<Unit3D> allUnits)
	{
		var results = new List<(int PeerId, byte[] Payload, bool IsBaseline)>();
		Entity worldEntity = FindWorldEntity();
		if (worldEntity == Entity.Null || !GameContext.World.Has<NetworkState>(worldEntity) || !GameContext.World.Has<NetworkMappingState>(worldEntity)) return results;

		ref var networkState = ref GameContext.World.Get<NetworkState>(worldEntity);
		networkState.SnapshotSequence++;
		int snapshotSequence = networkState.SnapshotSequence;
		_currentSnapshotSequence = snapshotSequence;
		if (MultiplayerBandwidthMeter.IsRecording) MultiplayerBandwidthMeter.RecordHostSnapshotSequence(snapshotSequence);

		bool anyPeerNeedsBaseline = false;
		int snapshotPeerCount = 0;
		if (GameContext.Lobby != null)
		{
			var mapping = GameContext.World.Get<NetworkMappingState>(worldEntity);
			foreach (var player in GameContext.Lobby.PlayerList)
			{
				if (player.PeerId == localPeerId || player.PeerId < 0) continue;
				if (!mapping.PeerIdToPlayerEntityMap.ContainsKey(player.PeerId)) continue;

				snapshotPeerCount++;
				if (ShouldSendBaseline(GetClientSnapshotState(player.PeerId), snapshotSequence)) anyPeerNeedsBaseline = true;
			}
		}

		if (GameContext.Lobby != null && snapshotPeerCount > 0)
		{
			var mapping = GameContext.World.Get<NetworkMappingState>(worldEntity);

			// Ability and static-text state is collected once per tick, but only a peer that is receiving a baseline needs
			// the whole set: every other peer already has it from its last baseline and only needs what changed. Passing one
			// list built from "any peer needs a baseline" put the complete ability state in every peer's delta on any tick
			// where a single peer was due a baseline, which with eight peers is almost every tick.
			var abilityStates = CollectHostAbilityStateDeltas(false);
			var staticTexts = CollectHostStaticTextDeltas(false);
			List<AbilityStateDelta>? baselineAbilityStates = null;
			List<StaticTextDelta>? baselineStaticTexts = null;
			if (anyPeerNeedsBaseline)
			{
				baselineAbilityStates = CollectHostAbilityStateDeltas(true, clearDirty: false);
				baselineStaticTexts = CollectHostStaticTextDeltas(true, clearDirty: false);
			}

			ProcessLobbyPlayersForSnapshots(localPeerId, allUnits, results, snapshotSequence, mapping, abilityStates, staticTexts, baselineAbilityStates, baselineStaticTexts);
		}

		if (MultiplayerSyncDiagnostics.IsRecording)
		{
			_baselinePeerIds.Clear();
			for (int index = 0; index < results.Count; index++)
			{
				if (results[index].IsBaseline) _baselinePeerIds.Add(results[index].PeerId);
			}

			MultiplayerSyncDiagnostics.RecordHostSnapshot(BuildDiagnosticAuthoritativeSnapshot(allUnits, snapshotSequence, _baselinePeerIds.Count > 0), _baselinePeerIds);
		}

		return results;
	}

	/// <summary>
	/// Rewrites the host's diagnostic record for the current sequence as a baseline, because the reconnect baseline goes
	/// to one peer outside the per-tick fan-out and would otherwise be recorded as a delta. The verifier pairs records by
	/// sequence and keeps the last one, so this replaces the tick's own record.
	/// </summary>
	public void RecordReconnectBaselineForDiagnostics(List<Unit3D> allUnits, int reconnectedPeerId)
	{
		if (!MultiplayerSyncDiagnostics.IsRecording) return;

		int sequence = ResolveCurrentSnapshotSequence();
		_baselinePeerIds.Clear();
		_baselinePeerIds.Add(reconnectedPeerId);
		MultiplayerSyncDiagnostics.RecordHostSnapshot(BuildDiagnosticAuthoritativeSnapshot(allUnits, sequence, true), _baselinePeerIds);
	}

	private ClientSnapshotState GetClientSnapshotState(int peerId)
	{
		if (!_clientSnapshotStates.TryGetValue(peerId, out var state))
		{
			state = new ClientSnapshotState();
			_clientSnapshotStates[peerId] = state;
		}

		return state;
	}

	public void ResetClientSnapshotState(int peerId)
	{
		_clientSnapshotStates.Remove(peerId);
		_coldPayloadContentPerClient.Remove(peerId);
		_stringTableCursorPerClient.Remove(peerId);
	}

	public void HandleClientBaselineRequest(int peerId)
	{
		int currentSequence = ResolveCurrentSnapshotSequence();

		// The spacing is tracked outside the per-peer snapshot state because a reset creates fresh state with no record of
		// the baseline that was just sent, which let a client that keeps requesting one refresh that state every tick.
		if (_lastBaselineSentSequence.TryGetValue(peerId, out int lastSentSequence)
			&& currentSequence - lastSentSequence < MinimumBaselineRequestSpacingSequences) return;

		ResetClientSnapshotState(peerId);
	}

	private int ResolveCurrentSnapshotSequence()
	{
		Entity worldEntity = FindWorldEntity();
		if (worldEntity == Entity.Null || !GameContext.World.Has<NetworkState>(worldEntity)) return 0;
		return GameContext.World.Get<NetworkState>(worldEntity).SnapshotSequence;
	}

	public void RecordClientSnapshotAck(int peerId, int sequence, ulong appliedMask)
	{
		if (sequence < 0) return;

		ClientSnapshotState state = GetClientSnapshotState(peerId);
		state.Applied.Merge(sequence, appliedMask);
		if (sequence > state.AckedSequence)
		{
			state.AckedSequence = sequence;
			state.LastAckAdvanceSequence = _currentSnapshotSequence;
			// The peer is applying what it receives, so it is not stuck and the next recovery starts from one attempt again.
			state.BaselineBackoffAttempts = 0;
		}

		// The peer confirms the baseline that carried the world its later deltas are built on, so that baseline becomes
		// the base those deltas name. An unacknowledged baseline is still in flight and must not gate the stream.
		if (state.LastBaselineSequence >= 0 && state.AcknowledgedBaselineSequence < state.LastBaselineSequence && state.Applied.Contains(state.LastBaselineSequence))
		{
			state.AcknowledgedBaselineSequence = state.LastBaselineSequence;
		}

		// A pending entry is confirmed only by the sequence that actually carried it. An acknowledgement for a later
		// snapshot used to confirm everything before it, so a dropped spawn or rotation change left the host believing the
		// peer had it and never resending it.
		List<SentUnitEntry> pendingUnits = state.PendingUnitAcks;
		int writeUnits = 0;
		for (int read = 0; read < pendingUnits.Count; read++)
		{
			SentUnitEntry entry = pendingUnits[read];
			if (state.Applied.Contains(entry.Sequence))
			{
				state.AckedUnits[entry.EntityId] = entry.Snapshot;
				if (state.SentUnitSequences.TryGetValue(entry.EntityId, out int latest) && latest == entry.Sequence)
				{
					state.SentUnits.Remove(entry.EntityId);
					state.SentUnitSequences.Remove(entry.EntityId);
				}

				continue;
			}

			if (state.Applied.Highest - entry.Sequence >= AppliedSequenceWindow.WindowSize)
			{
				if (state.SentUnitSequences.TryGetValue(entry.EntityId, out int staleLatest) && staleLatest == entry.Sequence)
				{
					state.SentUnits.Remove(entry.EntityId);
					state.SentUnitSequences.Remove(entry.EntityId);
				}

				continue;
			}

			if (writeUnits != read) pendingUnits[writeUnits] = entry;
			writeUnits++;
		}

		if (writeUnits < pendingUnits.Count) pendingUnits.RemoveRange(writeUnits, pendingUnits.Count - writeUnits);

		List<SentComponentEntry> pending = state.PendingComponentAcks;
		int writeComponents = 0;
		for (int read = 0; read < pending.Count; read++)
		{
			SentComponentEntry entry = pending[read];
			if (state.Applied.Contains(entry.Sequence))
			{
				if (entry.Removed)
				{
					state.AckedComponents.Remove(entry.Key);
				}
				else
				{
					state.AckedComponents[entry.Key] = entry.Bytes;
				}

				if (state.SentComponents.TryGetValue(entry.Key, out var latest) && latest.Sequence == entry.Sequence)
				{
					state.SentComponents.Remove(entry.Key);
				}

				continue;
			}

			if (state.Applied.Highest - entry.Sequence >= AppliedSequenceWindow.WindowSize)
			{
				if (state.SentComponents.TryGetValue(entry.Key, out var staleLatest) && staleLatest.Sequence == entry.Sequence)
				{
					state.SentComponents.Remove(entry.Key);
				}

				continue;
			}

			if (writeComponents != read) pending[writeComponents] = entry;
			writeComponents++;
		}

		if (writeComponents < pending.Count) pending.RemoveRange(writeComponents, pending.Count - writeComponents);
	}

	private static void TrackSentUnit(ClientSnapshotState state, UnitSnapshot snapshot, int snapshotSequence)
	{
		state.SentUnits[snapshot.EntityId] = snapshot;
		state.SentUnitSequences[snapshot.EntityId] = snapshotSequence;
		state.PendingUnitAcks.Add(new SentUnitEntry
		{
			EntityId = snapshot.EntityId,
			Snapshot = snapshot,
			Sequence = snapshotSequence
		});

		if (state.PendingUnitAcks.Count > MaximumPendingComponentAcks)
		{
			state.PendingUnitAcks.RemoveRange(0, state.PendingUnitAcks.Count - MaximumPendingComponentAcks);
		}
	}

	/// <summary>
	/// The sequence a delta is computed on top of. It has to be a baseline the peer has acknowledged, not the newest one
	/// the host has sent: naming an in-flight baseline makes the client drop everything sent until that baseline lands,
	/// which costs one stall per baseline interval even on a healthy link and turns into a freeze under loss.
	/// </summary>
	private static int ResolveDeltaBaseSequence(ClientSnapshotState state)
	{
		if (state.AcknowledgedBaselineSequence >= 0) return state.AcknowledgedBaselineSequence;
		return state.LastBaselineSequence;
	}

	/// <summary>
	/// Whether this peer needs a full baseline instead of a delta. A baseline only helps a peer that cannot place what it
	/// receives, so the recovery branch is keyed on the acknowledgement standing still, not on its distance from the newest
	/// sequence. Distance measured the wrong thing: on the eight-peer loss run a peer that applied 98 % of its deltas and
	/// acknowledged continuously sat 1 364 sequences behind — late, not stuck — and was answered with 47 full baselines,
	/// 3.9 KB/s of its 22.1 KB/s, none of which unblocked anything. The periodic safety baseline is what bounds drift.
	/// </summary>
	private bool ShouldSendBaseline(ClientSnapshotState state, int snapshotSequence)
	{
		if (state.AckedSequence < 0)
		{
			if (state.LastBaselineSequence < 0) return true;
			return snapshotSequence - state.LastBaselineSequence >= ResolveBaselineSpacing(state, BaselineRepeatWhileUnacknowledgedSequences);
		}

		if (snapshotSequence - state.LastBaselineSequence >= BaselineSafetyIntervalSequences) return true;
		if (snapshotSequence - state.LastAckAdvanceSequence < BaselineAckStallSequences) return false;
		return snapshotSequence - state.LastBaselineSequence >= ResolveBaselineSpacing(state, MinimumBaselineRequestSpacingSequences);
	}

	/// <summary>
	/// How long to wait before re-sending a full baseline to a peer that is not acknowledging. A peer that is stuck because
	/// it missed a delta is unblocked by one baseline; a peer that is stuck because its acknowledgements are not arriving,
	/// or because it cannot apply what it receives, is not unblocked by any number of them, and each attempt costs the
	/// whole world. Repeating the attempt every second turned a single stuck peer into 139 KB/s of traffic that then
	/// competed with the acknowledgements it depended on. The wait doubles per attempt up to the safety interval, and any
	/// acknowledgement that advances clears it, so a peer that recovers is served at once.
	/// </summary>
	private static int ResolveBaselineSpacing(ClientSnapshotState state, int baseSpacingSequences)
	{
		int spacing = baseSpacingSequences;
		for (int attempt = 0; attempt < state.BaselineBackoffAttempts && spacing < BaselineSafetyIntervalSequences; attempt++)
		{
			spacing *= 2;
		}

		return Math.Min(spacing, BaselineSafetyIntervalSequences);
	}

	private static BaselineSendReason ResolveBaselineReason(ClientSnapshotState state, int snapshotSequence)
	{
		if (state.AckedSequence < 0) return state.LastBaselineSequence < 0 ? BaselineSendReason.Initial : BaselineSendReason.Unacknowledged;
		if (snapshotSequence - state.LastBaselineSequence >= BaselineSafetyIntervalSequences) return BaselineSendReason.Safety;
		return BaselineSendReason.Resync;
	}

	private void ProcessLobbyPlayersForSnapshots(int localPeerId, List<Unit3D> allUnits, List<(int PeerId, byte[] Payload, bool IsBaseline)> results, int snapshotSequence, NetworkMappingState mapping, List<AbilityStateDelta> abilityStates, List<StaticTextDelta> staticTexts, List<AbilityStateDelta>? baselineAbilityStates, List<StaticTextDelta>? baselineStaticTexts)
	{
		foreach (var p in GameContext.Lobby.PlayerList)
		{
			if (p.PeerId == localPeerId || p.PeerId < 0) continue;
			if (p.IsDisconnected) continue;
			if (!mapping.PeerIdToPlayerEntityMap.TryGetValue(p.PeerId, out var playerEntity)) continue;

			ClientSnapshotState state = GetClientSnapshotState(p.PeerId);
			bool sendBaseline = ShouldSendBaseline(state, snapshotSequence);
			if (!sendBaseline && state.AckedSequence < 0) continue;
			BaselineSendReason baselineReason = sendBaseline ? ResolveBaselineReason(state, snapshotSequence) : BaselineSendReason.None;
			// A peer that has never acknowledged anything has no lag to report: recording its distance from the current
			// sequence as a lag made a joining peer look twenty seconds behind when it had simply not been sent anything yet.
			int ackLagSequences = state.AckedSequence < 0 ? -1 : snapshotSequence - state.AckedSequence;
			// How long the acknowledgement has stood still, which is what the recovery branch is keyed on: lag says how far
			// behind the peer is, not whether anything is reaching it.
			int ackStallSequences = state.LastAckAdvanceSequence < 0 ? -1 : snapshotSequence - state.LastAckAdvanceSequence;
			if (baselineReason == BaselineSendReason.Resync || baselineReason == BaselineSendReason.Unacknowledged) state.BaselineBackoffAttempts++;
			List<AbilityStateDelta> peerAbilityStates = sendBaseline && baselineAbilityStates != null ? baselineAbilityStates : abilityStates;
			List<StaticTextDelta> peerStaticTexts = sendBaseline && baselineStaticTexts != null ? baselineStaticTexts : staticTexts;
			var payload = BuildClientSnapshotPayload(p.PeerId, playerEntity, allUnits, sendBaseline, snapshotSequence, state, peerAbilityStates, peerStaticTexts);
			MultiplayerBandwidthMeter.RecordSnapshotPayload(p.PeerId, payload.Length, sendBaseline, baselineReason, ackLagSequences, ackStallSequences);
			MultiplayerBandwidthMeter.RecordSnapshotSequence(p.PeerId, snapshotSequence);
			results.Add((p.PeerId, payload, sendBaseline));
		}
	}

	private byte[] BuildClientSnapshotPayload(int peerId, Entity playerEntity, List<Unit3D> allUnits, bool sendBaseline, int snapshotSequence, ClientSnapshotState state, List<AbilityStateDelta> abilityStates, List<StaticTextDelta> staticTexts)
	{
		_compositionSampling = ShouldSampleSnapshotComposition(peerId, sendBaseline);
		_compositionComponentBytes.Clear();
		Vector3 cameraPos = _clientCameraPositions.TryGetValue(peerId, out var cam) ? cam : Vector3.Zero;
		if (!_snapshotUnitsPerClient.TryGetValue(peerId, out var snapshotUnits))
		{
			snapshotUnits = new List<UnitSnapshot>(64);
			_snapshotUnitsPerClient[peerId] = snapshotUnits;
		}

		snapshotUnits.Clear();
		_visibleServerEntityIds.Clear();
		if (sendBaseline)
		{
			state.SentUnits.Clear();
			state.SentUnitSequences.Clear();
			state.PendingUnitAcks.Clear();
		}

		foreach (var unit in allUnits)
		{
			if (!GodotObject.IsInstanceValid(unit)) continue;
			if (!IsUnitVisibleToPlayer(playerEntity, unit.Entity)) continue;

			var currentSnap = CreateUnitSnapshot(unit, cameraPos, out bool isDetailed);
			if (!isDetailed && GetOwnerPeerId(unit.Entity) == peerId)
			{
				isDetailed = true;
				currentSnap.IsDetailed = true;
			}

			if (sendBaseline)
			{
				snapshotUnits.Add(currentSnap);
				_visibleServerEntityIds.Add(currentSnap.EntityId);
				TrackSentUnit(state, currentSnap, snapshotSequence);
				continue;
			}

			// What the peer was last told about this unit, not what it last acknowledged, decides whether it needs telling
			// again. Comparing against the acknowledgement let the peer's own round trip inflate the step: a unit moving at
			// 8 m/s travelled another two metres while the state sent to it was in flight, and the resend that followed
			// landed as one 2.1 m jump instead of the 1 m the threshold promises. The client then has to cover that step on
			// top of the lag its smoothing already explains, which is what pushed fast far units past the placement bound.
			// A send still inside the retransmit window is exactly the state the peer is waiting on, so it is the baseline
			// for the next step; once it has been acknowledged the next step is measured from that instead.
			bool hasUnacknowledgedSend = state.SentUnits.TryGetValue(unit.Entity.Id, out UnitSnapshot sentSnap)
				&& state.SentUnitSequences.TryGetValue(unit.Entity.Id, out int sentSequence)
				&& !state.Applied.Contains(sentSequence)
				&& snapshotSequence - sentSequence < UnacknowledgedRetransmitSequences;
			UnitSnapshot referenceSnapshot = default;
			bool hasReferenceSnapshot = false;
			if (hasUnacknowledgedSend)
			{
				referenceSnapshot = sentSnap!;
				hasReferenceSnapshot = true;
			}
			else if (state.AckedUnits.TryGetValue(unit.Entity.Id, out UnitSnapshot ackedSnap))
			{
				referenceSnapshot = ackedSnap;
				hasReferenceSnapshot = true;
			}

			if (hasReferenceSnapshot)
			{
				bool shouldSend = HasUnitChanged(unit, ref currentSnap, isDetailed, referenceSnapshot);
				if (!shouldSend && sendBaseline) shouldSend = HealthDiffersFromPeer(currentSnap, referenceSnapshot);
				if (!shouldSend) continue;
			}

			snapshotUnits.Add(currentSnap);
			TrackSentUnit(state, currentSnap, snapshotSequence);
		}

		// A peer retires a unit the authority stops describing, so the components acknowledged for it no longer exist
		// in the peer's world. Forgetting them here is what makes the component set that comes back with the unit a
		// complete one instead of what happened to differ from a record of an entity the peer no longer has.
		if (sendBaseline) PruneUnmentionedPeerState(state);

		var coldPayloadCandidates = BuildColdPayloadCandidates(snapshotSequence);
		var coldPayloads = BuildColdPayloadDeltas(peerId, coldPayloadCandidates);
		var componentDeltas = BuildComponentDeltas(peerId, playerEntity, allUnits, state, snapshotSequence, sendBaseline);
		List<CatalogEntry> catalog;
		if (state.SentCatalog)
		{
			catalog = BuildStringTableEntries(peerId, false);
		}
		else
		{
			catalog = BuildCatalogEntriesWithStringTable(peerId);
			state.SentCatalog = true;
		}

		uint stateFingerprint = sendBaseline ? ComputeHostStateFingerprint(abilityStates, staticTexts, coldPayloadCandidates) : 0u;

		var worldSnapshot = new WorldSnapshot
		{
			Sequence = snapshotSequence,
			IsBaseline = sendBaseline,
			BaseSequence = sendBaseline ? snapshotSequence : ResolveDeltaBaseSequence(state),
			Units = snapshotUnits,
			ColdPayloads = coldPayloads,
			ComponentDeltas = componentDeltas,
			AbilityStates = abilityStates,
			StaticTexts = staticTexts,
			Catalog = catalog,
			UnitComponentPresence = sendBaseline ? state.UnitComponentPresence : null,
			StateFingerprint = stateFingerprint
		};

		if (sendBaseline)
		{
			state.LastBaselineSequence = snapshotSequence;
			state.HasSentBaseline = true;
			_lastBaselineSentSequence[peerId] = snapshotSequence;
		}

		RecordSnapshotComposition(peerId, worldSnapshot);
		byte[] serialized = MemoryPackSerializer.Serialize(worldSnapshot);

		// Framing costs a millisecond or two on the tick that sends it, which is worth stating because that cost is what
		// decides how small a payload can be made before the host feels it.
		long framingStartTicks = System.Diagnostics.Stopwatch.GetTimestamp();
		byte[] framed = sendBaseline ? SnapshotWireFormat.EncodeBaseline(serialized) : SnapshotWireFormat.EncodeDelta(serialized);
		double framingMilliseconds = System.Diagnostics.Stopwatch.GetElapsedTime(framingStartTicks).TotalMilliseconds;
		MultiplayerBandwidthMeter.RecordSnapshotFraming(peerId, sendBaseline, serialized.Length, framed.Length, framingMilliseconds, snapshotSequence);
		return framed;
	}

	private int _compositionSampleCounter;
	private int _compositionSamplePeerId = -1;
	private bool _compositionSampling;
	private readonly Dictionary<string, int> _compositionComponentBytes = new();

	private bool ShouldSampleSnapshotComposition(int peerId, bool isBaseline)
	{
		if (!MultiplayerBandwidthMeter.IsRecording) return false;
		if (_compositionSamplePeerId < 0) _compositionSamplePeerId = peerId;
		if (peerId != _compositionSamplePeerId) return false;
		return isBaseline || ++_compositionSampleCounter % 20 == 0;
	}

	private void RecordSnapshotComposition(int peerId, WorldSnapshot snapshot)
	{
		if (!_compositionSampling) return;
		_compositionSampling = false;

		int emptyBytes = MeasureCompositionMember(snapshot, 0);
		MultiplayerBandwidthMeter.RecordSnapshotComposition(
			snapshot.IsBaseline,
			MeasureCompositionMember(snapshot, 1) - emptyBytes,
			MeasureCompositionMember(snapshot, 2) - emptyBytes,
			MeasureCompositionMember(snapshot, 3) - emptyBytes,
			MeasureCompositionMember(snapshot, 4) - emptyBytes,
			MeasureCompositionMember(snapshot, 5) - emptyBytes,
			MeasureCompositionMember(snapshot, 6) - emptyBytes,
			snapshot.Units?.Count ?? 0,
			snapshot.ComponentDeltas?.Count ?? 0,
			snapshot.ColdPayloads?.Count ?? 0);

		foreach (var pair in _compositionComponentBytes)
		{
			MultiplayerBandwidthMeter.RecordComponentBytes(snapshot.IsBaseline, pair.Key, pair.Value);
		}

		_compositionComponentBytes.Clear();
	}

	private void RecordComponentBytesForSample(IReplicatedComponentCodec codec, int byteCount)
	{
		if (!_compositionSampling) return;
		string name = codec.GetType().Name;
		int existing = _compositionComponentBytes.TryGetValue(name, out int stored) ? stored : 0;
		_compositionComponentBytes[name] = existing + byteCount;
	}

	private static int MeasureCompositionMember(WorldSnapshot source, int member)
	{
		var measured = new WorldSnapshot
		{
			Sequence = source.Sequence,
			IsBaseline = source.IsBaseline,
			BaseSequence = source.BaseSequence,
			StateFingerprint = source.StateFingerprint,
			Units = member == 1 ? source.Units : null,
			ComponentDeltas = member == 2 ? source.ComponentDeltas : null,
			ColdPayloads = member == 3 ? source.ColdPayloads : null,
			AbilityStates = member == 4 ? source.AbilityStates : null,
			StaticTexts = member == 5 ? source.StaticTexts : null,
			Catalog = member == 6 ? source.Catalog : null
		};
		return MemoryPackSerializer.Serialize(measured).Length;
	}

	private const string StringTableChannel = "string";

	private readonly List<(int Id, string Text)> _stringTableEntries = new();
	private readonly Dictionary<int, int> _stringTableCursorPerClient = new();

	private List<CatalogEntry> BuildCatalogEntriesWithStringTable(int peerId)
	{
		var entries = BuildCatalogEntries();
		entries.AddRange(BuildStringTableEntries(peerId, true));
		return entries;
	}

	private List<CatalogEntry> BuildStringTableEntries(int peerId, bool includeAllEntries)
	{
		int entryCount = Realm.MapAPI.StringRegistry.EntryCount;
		if (!_stringTableCursorPerClient.TryGetValue(peerId, out int cursor) || cursor > entryCount) cursor = 0;

		_stringTableEntries.Clear();
		int nextCursor = Realm.MapAPI.StringRegistry.GetEntriesSince(includeAllEntries ? 0 : cursor, _stringTableEntries);

		var entries = new List<CatalogEntry>(_stringTableEntries.Count);
		for (int index = 0; index < _stringTableEntries.Count; index++)
		{
			var entry = _stringTableEntries[index];
			entries.Add(new CatalogEntry
			{
				Channel = StringTableChannel,
				Key = entry.Id.ToString(System.Globalization.CultureInfo.InvariantCulture),
				Value = entry.Text
			});
		}

		_stringTableCursorPerClient[peerId] = nextCursor;
		return entries;
	}

	private List<CatalogEntry> BuildCatalogEntries()
	{
		var entries = new List<CatalogEntry>();
		var registry = GameContext.RegistryService;
		if (registry == null) return entries;

		foreach (var rule in registry.PlacementRadiusRules)
		{
			entries.Add(new CatalogEntry { Channel = "placement_radius", Key = rule.DefinitionKey, Number = rule.Radius });
		}
		foreach (string key in registry.ResourceDropoffBuildingKeys)
		{
			entries.Add(new CatalogEntry { Channel = "dropoff", Key = key });
		}
		foreach (string key in registry.ResourceWorkerUnitKeys)
		{
			entries.Add(new CatalogEntry { Channel = "worker", Key = key });
		}
		foreach (string key in registry.PrimaryBuildingIds)
		{
			entries.Add(new CatalogEntry { Channel = "primary", Key = key });
		}
		foreach (var pair in registry.ResourceNodeTypes)
		{
			entries.Add(new CatalogEntry { Channel = "resource_node_type", Key = pair.Key, Value = pair.Value });
		}
		return entries;
	}

	private readonly List<ColdPayloadDelta> _coldPayloadCandidates = new();
	private readonly List<ColdPayloadDelta> _emptyColdPayloadDeltas = new();
	private readonly HashSet<(byte Scope, int Key, byte Kind, byte Role)> _coldPayloadSeenKeys = new();
	private readonly List<(byte Scope, int Key, byte Kind, byte Role)> _coldPayloadExpiredKeys = new();
	private readonly Dictionary<int, Dictionary<(byte Scope, int Key, byte Kind, byte Role), int>> _coldPayloadContentPerClient = new();
	private readonly Dictionary<int, (byte InternedKind, byte[] Bytes)> _internedPayloadBytesCache = new();
	private int _coldPayloadCandidatesSequence = -1;
	private byte[] _leaderboardPayloadBytes = Array.Empty<byte>();
	private int _leaderboardPayloadContentId;
	private int _leaderboardPayloadRevision = -1;
	private byte[] _summaryTablePayloadBytes = Array.Empty<byte>();
	private int _summaryTablePayloadContentId;
	private int _summaryTablePayloadVersion = int.MinValue;

	private List<ColdPayloadDelta> BuildColdPayloadCandidates(int snapshotSequence)
	{
		if (_coldPayloadCandidatesSequence == snapshotSequence) return _coldPayloadCandidates;

		_coldPayloadCandidatesSequence = snapshotSequence;
		_coldPayloadCandidates.Clear();

		Entity worldEntity = FindWorldEntity();
		var world = GameContext.World;
		if (world == null) return _coldPayloadCandidates;

		if (worldEntity != Entity.Null && world.IsAlive(worldEntity))
		{
			if (world.Has<LeaderboardState>(worldEntity))
			{
				var boards = world.Get<LeaderboardState>(worldEntity).Boards;
				int revision = boards?.Revision ?? 0;
				if (revision != _leaderboardPayloadRevision)
				{
					_leaderboardPayloadRevision = revision;
					_leaderboardPayloadBytes = ColdPayloadCodecs.SerializeLeaderboards(boards);
					_leaderboardPayloadContentId = ColdPayloadCodecs.ComputeContentId(_leaderboardPayloadBytes);
				}

				AddColdPayloadCandidate((byte)ReplicatedScope.World, 0, ColdPayloadKind.LeaderboardBoards, ColdPayloadRole.None, 0, _leaderboardPayloadContentId, _leaderboardPayloadBytes);
			}

			if (world.Has<SummaryTableState>(worldEntity))
			{
				ref var summaryTable = ref world.Get<SummaryTableState>(worldEntity);
				int version = ComputeSummaryTableVersion(in summaryTable);
				if (version != _summaryTablePayloadVersion)
				{
					_summaryTablePayloadVersion = version;
					_summaryTablePayloadBytes = ColdPayloadCodecs.SerializeSummaryTable(in summaryTable);
					_summaryTablePayloadContentId = ColdPayloadCodecs.ComputeContentId(_summaryTablePayloadBytes);
				}

				AddColdPayloadCandidate((byte)ReplicatedScope.World, 0, ColdPayloadKind.SummaryTable, ColdPayloadRole.None, 0, _summaryTablePayloadContentId, _summaryTablePayloadBytes);
			}

			if (world.Has<ScriptZonesState>(worldEntity))
			{
				AddInternedPayloadCandidate((byte)ReplicatedScope.World, 0, ColdPayloadRole.ZoneList, world.Get<ScriptZonesState>(worldEntity).Handle);
			}
		}

		foreach (var entity in world.Query<ProductionQueue>())
		{
			ref var queue = ref world.Get<ProductionQueue>(entity);
			if (queue.Count <= 0 || queue.Handle == 0) continue;

			AddInternedPayloadCandidate((byte)ReplicatedScope.Unit, GetServerEntityId(entity.Id), ColdPayloadRole.ProductionQueue, queue.Handle);
		}

		foreach (var entity in world.Query<AbilityState>())
		{
			ref var abilityState = ref world.Get<AbilityState>(entity);
			if (abilityState.Count <= 0 || abilityState.Handle == 0) continue;

			AddInternedPayloadCandidate((byte)ReplicatedScope.Unit, GetServerEntityId(entity.Id), ColdPayloadRole.AbilityList, abilityState.Handle);
		}

		return _coldPayloadCandidates;
	}

	private void AddInternedPayloadCandidate(byte scope, int key, ColdPayloadRole role, int handle)
	{
		if (handle == 0) return;

		if (!_internedPayloadBytesCache.TryGetValue(handle, out var cached))
		{
			if (!InternedValueStore.TryGetPayload(handle, out byte internedKind, out ReadOnlySpan<byte> payload)) return;

			cached = (internedKind, payload.ToArray());
			_internedPayloadBytesCache[handle] = cached;
		}

		AddColdPayloadCandidate(scope, key, ColdPayloadKind.InternedPayload, role, cached.InternedKind, handle, cached.Bytes);
	}

	private void AddColdPayloadCandidate(byte scope, int key, ColdPayloadKind kind, ColdPayloadRole role, byte internedKind, int contentId, byte[] bytes)
	{
		_coldPayloadCandidates.Add(new ColdPayloadDelta
		{
			Scope = scope,
			Key = key,
			Kind = (byte)kind,
			Role = (byte)role,
			InternedKind = internedKind,
			ContentId = contentId,
			Destroyed = 0,
			Bytes = bytes
		});
	}

	private static int ComputeSummaryTableVersion(in SummaryTableState state)
	{
		uint hash = ReplicatedStateFingerprint.Seed;
		hash = ReplicatedStateFingerprint.Combine(hash, state.VisibleByte);
		hash = ReplicatedStateFingerprint.Combine(hash, state.TitleStringId);

		string[] headers = state.Headers;
		if (headers != null)
		{
			for (int index = 0; index < headers.Length; index++)
			{
				hash = ReplicatedStateFingerprint.Combine(hash, headers[index]);
			}
		}

		Dictionary<string, string[]> rows = state.Rows;
		if (rows != null)
		{
			foreach (var pair in rows)
			{
				hash = ReplicatedStateFingerprint.Combine(hash, pair.Key);
				string[] values = pair.Value;
				if (values == null) continue;

				for (int index = 0; index < values.Length; index++)
				{
					hash = ReplicatedStateFingerprint.Combine(hash, values[index]);
				}
			}
		}

		return unchecked((int)hash);
	}

	private List<ColdPayloadDelta> BuildColdPayloadDeltas(int peerId, List<ColdPayloadDelta> candidates)
	{
		if (!_coldPayloadContentPerClient.TryGetValue(peerId, out var previousContentIds))
		{
			previousContentIds = new Dictionary<(byte Scope, int Key, byte Kind, byte Role), int>();
			_coldPayloadContentPerClient[peerId] = previousContentIds;
		}

		var deltas = new List<ColdPayloadDelta>();
		_coldPayloadSeenKeys.Clear();
		for (int index = 0; index < candidates.Count; index++)
		{
			var candidate = candidates[index];
			var key = (candidate.Scope, candidate.Key, candidate.Kind, candidate.Role);
			_coldPayloadSeenKeys.Add(key);

			if (previousContentIds.TryGetValue(key, out int previousContentId) && previousContentId == candidate.ContentId) continue;

			previousContentIds[key] = candidate.ContentId;
			deltas.Add(candidate);
		}

		_coldPayloadExpiredKeys.Clear();
		foreach (var key in previousContentIds.Keys)
		{
			if (!_coldPayloadSeenKeys.Contains(key)) _coldPayloadExpiredKeys.Add(key);
		}

		for (int index = 0; index < _coldPayloadExpiredKeys.Count; index++)
		{
			var key = _coldPayloadExpiredKeys[index];
			previousContentIds.Remove(key);
			deltas.Add(new ColdPayloadDelta
			{
				Scope = key.Scope,
				Key = key.Key,
				Kind = key.Kind,
				Role = key.Role,
				Destroyed = 1,
				Bytes = Array.Empty<byte>()
			});
		}

		return deltas;
	}

	private readonly Dictionary<int, List<ComponentDelta>> _componentDeltasPerClient = new();
	private readonly Dictionary<int, List<UnitSnapshot>> _snapshotUnitsPerClient = new();
	private byte[] _componentWriteScratch = Array.Empty<byte>();

	private List<ComponentDelta> BuildComponentDeltas(int peerId, Entity viewerEntity, List<Unit3D> allUnits, ClientSnapshotState state, int snapshotSequence, bool sendBaseline)
	{
		if (!_componentDeltasPerClient.TryGetValue(peerId, out var deltas))
		{
			deltas = new List<ComponentDelta>(64);
			_componentDeltasPerClient[peerId] = deltas;
		}

		deltas.Clear();
		Entity worldEntity = FindWorldEntity();
		if (worldEntity == Entity.Null || !GameContext.World.Has<NetworkMappingState>(worldEntity)) return deltas;

		var mapping = GameContext.World.Get<NetworkMappingState>(worldEntity);
		if (sendBaseline) state.SentComponents.Clear();
		if (sendBaseline) state.UnitComponentPresence.Clear();

		var codecs = ReplicatedComponents.All;
		for (int codecIndex = 0; codecIndex < codecs.Count; codecIndex++)
		{
			var codec = codecs[codecIndex];
			if (codec.Scope != ReplicatedScope.World) continue;

			TryAppendComponentDelta(worldEntity, codec, (byte)ReplicatedScope.World, 0, state, deltas, snapshotSequence, sendBaseline, false);
		}

		for (int codecIndex = 0; codecIndex < codecs.Count; codecIndex++)
		{
			var codec = codecs[codecIndex];
			if (codec.Scope != ReplicatedScope.Player) continue;

			for (int slot = 0; slot < Realm.Ecs.EcsWorld.MAX_PLAYERS; slot++)
			{
				if (mapping.PlayerEntities == null || slot >= mapping.PlayerEntities.Length) continue;
				Entity playerEntity = mapping.PlayerEntities[slot];
				if (playerEntity == Entity.Null || !GameContext.World.IsAlive(playerEntity)) continue;

				TryAppendComponentDelta(playerEntity, codec, (byte)ReplicatedScope.Player, slot, state, deltas, snapshotSequence, sendBaseline, false);
			}
		}

		for (int unitIndex = 0; unitIndex < allUnits.Count; unitIndex++)
		{
			var unit = allUnits[unitIndex];
			if (!GodotObject.IsInstanceValid(unit)) continue;
			if (!IsUnitVisibleToPlayer(viewerEntity, unit.Entity)) continue;

			// A peer writes order state for its own units before the authority confirms it, so what it holds and what it
			// acknowledged can differ. Those units are restated in full on a baseline, which is what repairs a prediction
			// the authority rejected; every other unit only needs what changed. A unit instance the peer would re-create
			// needs its whole component set on a delta too: the id is the same id, but the entity behind it is not the one
			// the peer acknowledged.
			bool forceSend = IsNewEntityInstanceForPeer(state, unit) || (sendBaseline && GetOwnerPeerId(unit.Entity) == peerId);
			ulong presenceMask = 0;
			for (int codecIndex = 0; codecIndex < codecs.Count; codecIndex++)
			{
				var codec = codecs[codecIndex];
				if (codec.Scope != ReplicatedScope.Unit) continue;

				if (!TryAppendComponentDelta(unit.Entity, codec, (byte)ReplicatedScope.Unit, unit.Entity.Id, state, deltas, snapshotSequence, sendBaseline, forceSend)) continue;
				if (!ReplicatedUnitComponentSlots.TryGetSlot(codec.ComponentTypeId, out int presenceSlot)) continue;

				presenceMask |= 1UL << presenceSlot;
			}

			if (sendBaseline) state.UnitComponentPresence.Add(new UnitComponentPresence { ServerEntityId = unit.Entity.Id, Mask = presenceMask });
		}

		var lootCodec = ReplicatedComponents.Find(ComponentType<GroundLoot>.Id);
		var host = GameContext.Host;
		if (lootCodec != null && host != null)
		{
			List<LootItem3D> lootItems = host.AllLootItems;
			for (int lootIndex = 0; lootIndex < lootItems.Count; lootIndex++)
			{
				LootItem3D loot = lootItems[lootIndex];
				if (loot == null || !GodotObject.IsInstanceValid(loot) || loot.IsDespawned) continue;
				if (loot.Entity == Entity.Null || !GameContext.World.IsAlive(loot.Entity)) continue;

				TryAppendComponentDelta(loot.Entity, lootCodec, (byte)ReplicatedScope.Unit, loot.Entity.Id, state, deltas, snapshotSequence, sendBaseline, sendBaseline);
			}
		}

		return deltas;
	}

	private bool TryAppendComponentDelta(Entity entity, IReplicatedComponentCodec codec, byte scope, int key, ClientSnapshotState state, List<ComponentDelta> deltas, int snapshotSequence, bool sendBaseline, bool forceSend)
	{
		if (_componentWriteScratch.Length < codec.SizeInBytes) _componentWriteScratch = new byte[codec.SizeInBytes];

		var scratch = _componentWriteScratch.AsSpan(0, codec.SizeInBytes);
		var deltaKey = (scope, key, codec.ComponentTypeId);
		if (!codec.TryWrite(GameContext.World, entity, scratch))
		{
			if (!state.AckedComponents.ContainsKey(deltaKey))
			{
				state.SentComponents.Remove(deltaKey);
				return false;
			}

			if (IsInFlight(state, deltaKey, snapshotSequence, out var inFlightRemoval) && inFlightRemoval) return false;

			EmitComponentDelta(deltaKey, Array.Empty<byte>(), snapshotSequence, true, state, deltas);
			RecordComponentBytesForSample(codec, 0);
			return false;
		}

		// A baseline is not a reason to restate what the peer confirmed it already holds. The peer keeps its units
		// across a baseline; only a peer being handed a unit instance it does not have needs the whole component set,
		// and that is what forceSend marks - including a send that is still in flight, because that send described the
		// instance the peer is replacing.
		if (!forceSend)
		{
			if (state.AckedComponents.TryGetValue(deltaKey, out var ackedBytes) && scratch.SequenceEqual(ackedBytes)) return true;

			if (!sendBaseline
				&& IsInFlight(state, deltaKey, snapshotSequence, out _)
				&& state.SentComponents.TryGetValue(deltaKey, out var pendingEntry)
				&& !pendingEntry.Removed
				&& scratch.SequenceEqual(pendingEntry.Bytes)) return true;
		}

		byte[] bytes;
		if (state.SentComponents.TryGetValue(deltaKey, out var previousEntry) && !previousEntry.Removed && previousEntry.Bytes.Length == codec.SizeInBytes && scratch.SequenceEqual(previousEntry.Bytes))
		{
			bytes = previousEntry.Bytes;
		}
		else
		{
			bytes = new byte[codec.SizeInBytes];
			scratch.CopyTo(bytes);
		}

		EmitComponentDelta(deltaKey, bytes, snapshotSequence, false, state, deltas);
		RecordComponentBytesForSample(codec, bytes.Length);
		return true;
	}

	/// <summary>
	/// Whether this peer would treat the unit as a new instance: it has never acknowledged a description of the id, or
	/// what it acknowledged was a different unit, dead, or of another owner. The peer's apply path retires and re-creates
	/// its copy in exactly those cases, so the components it held for the old instance are not the ones it needs now.
	/// </summary>
	private bool IsNewEntityInstanceForPeer(ClientSnapshotState state, Unit3D unit)
	{
		if (!state.AckedUnits.TryGetValue(unit.Entity.Id, out UnitSnapshot ackedUnit)) return true;

		if (ackedUnit.IsDead != GameContext.World.Has<Dead>(unit.Entity)) return true;
		if (ackedUnit.IsBuilding != unit.IsBuilding) return true;
		if (ackedUnit.OwnerPlayerIndex != GetOwnerPlayerIndex(unit.Entity)) return true;
		return !string.Equals(ackedUnit.UnitId, unit.UnitId, StringComparison.Ordinal);
	}

	private readonly HashSet<int> _visibleServerEntityIds = new();
	private readonly List<(byte Scope, int Key, int TypeId)> _prunedComponentKeys = new();
	private readonly List<int> _prunedUnitIds = new();
	private readonly Dictionary<int, List<(int LocalEntityId, IReplicatedComponentCodec Codec)>> _referrersByServerEntityId = new();
	private readonly Dictionary<(int LocalEntityId, int ComponentTypeId), int> _referencedServerEntityIds = new();

	private void PruneUnmentionedPeerState(ClientSnapshotState state)
	{
		_prunedComponentKeys.Clear();
		foreach (var pair in state.AckedComponents)
		{
			if (pair.Key.Scope != (byte)ReplicatedScope.Unit) continue;
			if (_visibleServerEntityIds.Contains(pair.Key.Key)) continue;

			_prunedComponentKeys.Add(pair.Key);
		}

		for (int index = 0; index < _prunedComponentKeys.Count; index++)
		{
			state.AckedComponents.Remove(_prunedComponentKeys[index]);
			state.SentComponents.Remove(_prunedComponentKeys[index]);
		}

		_prunedUnitIds.Clear();
		foreach (var pair in state.AckedUnits)
		{
			if (_visibleServerEntityIds.Contains(pair.Key)) continue;

			_prunedUnitIds.Add(pair.Key);
		}

		for (int index = 0; index < _prunedUnitIds.Count; index++)
		{
			state.AckedUnits.Remove(_prunedUnitIds[index]);
		}
	}

	/// <summary>
	/// Safety net for a change no acknowledgement confirms. Acknowledgements travel reliably, so an ordinary lost
	/// snapshot is repaired by the acknowledgement of the next one, which is one round trip away; this bound only
	/// catches a peer that stops acknowledging altogether (a client that cannot apply a snapshot, or one that is
	/// disconnecting) and keeps its state from stalling until the next baseline.
	/// </summary>
	public const int UnacknowledgedRetransmitSequences = 15;

	private static bool IsInFlight(ClientSnapshotState state, (byte Scope, int Key, int TypeId) deltaKey, int snapshotSequence, out bool removed)
	{
		removed = false;
		if (!state.SentComponents.TryGetValue(deltaKey, out var entry)) return false;
		if (state.Applied.Contains(entry.Sequence)) return false;
		if (snapshotSequence - entry.Sequence >= UnacknowledgedRetransmitSequences) return false;
		removed = entry.Removed;
		return true;
	}

	private static void EmitComponentDelta((byte Scope, int Key, int TypeId) deltaKey, byte[] bytes, int snapshotSequence, bool removed, ClientSnapshotState state, List<ComponentDelta> deltas)
	{
		deltas.Add(new ComponentDelta
		{
			Key = deltaKey.Key,
			ComponentTypeId = deltaKey.TypeId,
			Scope = deltaKey.Scope,
			Bytes = bytes
		});

		var entry = new SentComponentEntry
		{
			Key = deltaKey,
			Bytes = bytes,
			Sequence = snapshotSequence,
			Removed = removed
		};

		state.SentComponents[deltaKey] = entry;
		state.PendingComponentAcks.Add(entry);
		if (state.PendingComponentAcks.Count > MaximumPendingComponentAcks)
		{
			state.PendingComponentAcks.RemoveRange(0, state.PendingComponentAcks.Count - MaximumPendingComponentAcks);
		}
	}

	private List<AbilityStateDelta> CollectHostAbilityStateDeltas(bool includeAll, bool clearDirty = true)
	{
		var host = GameContext.Host;
		if (host == null) return new List<AbilityStateDelta>();
		var deltas = host.CollectAbilityStateDeltas(includeAll, clearDirty);
		deltas.Sort(static (left, right) =>
		{
			int byEntity = left.ServerEntityId.CompareTo(right.ServerEntityId);
			return byEntity != 0 ? byEntity : left.AbilityIndex.CompareTo(right.AbilityIndex);
		});
		return deltas;
	}

	private List<StaticTextDelta> CollectHostStaticTextDeltas(bool includeAll, bool clearDirty = true)
	{
		var host = GameContext.Host;
		if (host == null) return new List<StaticTextDelta>();
		var deltas = host.CollectStaticTextDeltas(includeAll, clearDirty);
		deltas.Sort(static (left, right) => left.Handle.CompareTo(right.Handle));
		return deltas;
	}

	private uint ComputeHostStateFingerprint(List<AbilityStateDelta> abilityStates, List<StaticTextDelta> staticTexts, List<ColdPayloadDelta> coldPayloads)
	{
		_fingerprintComponentRecords.Clear();
		Entity worldEntity = FindWorldEntity();
		if (worldEntity != Entity.Null && GameContext.World.Has<NetworkMappingState>(worldEntity))
		{
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
					_fingerprintComponentRecords.Add((slot, codec.ComponentTypeId, bytes));
				}
			}
		}

		_fingerprintComponentRecords.Sort(static (left, right) =>
		{
			int byKey = left.ServerEntityId.CompareTo(right.ServerEntityId);
			return byKey != 0 ? byKey : left.ComponentTypeId.CompareTo(right.ComponentTypeId);
		});

		uint hash = ReplicatedStateFingerprint.Seed;
		for (int i = 0; i < _fingerprintComponentRecords.Count; i++)
		{
			var record = _fingerprintComponentRecords[i];
			hash = ReplicatedStateFingerprint.Combine(hash, 1);
			hash = ReplicatedStateFingerprint.Combine(hash, record.ServerEntityId);
			hash = ReplicatedStateFingerprint.Combine(hash, record.ComponentTypeId);
			hash = ReplicatedStateFingerprint.Combine(hash, record.Bytes);
		}
		for (int i = 0; i < abilityStates.Count; i++)
		{
			var ability = abilityStates[i];
			hash = ReplicatedStateFingerprint.Combine(hash, 3);
			hash = ReplicatedStateFingerprint.Combine(hash, ability.ServerEntityId);
			hash = ReplicatedStateFingerprint.Combine(hash, ability.AbilityIndex);
			hash = ReplicatedStateFingerprint.Combine(hash, ability.Flags);
		}
		for (int i = 0; i < staticTexts.Count; i++)
		{
			var text = staticTexts[i];
			hash = ReplicatedStateFingerprint.Combine(hash, 4);
			hash = ReplicatedStateFingerprint.Combine(hash, text.Handle);
			hash = ReplicatedStateFingerprint.Combine(hash, text.Text);
			hash = ReplicatedStateFingerprint.Combine(hash, BitConverter.SingleToInt32Bits(text.PositionX));
			hash = ReplicatedStateFingerprint.Combine(hash, BitConverter.SingleToInt32Bits(text.PositionY));
			hash = ReplicatedStateFingerprint.Combine(hash, BitConverter.SingleToInt32Bits(text.PositionZ));
			hash = ReplicatedStateFingerprint.Combine(hash, text.Visible);
		}
		for (int i = 0; i < coldPayloads.Count; i++)
		{
			var payload = coldPayloads[i];
			hash = ReplicatedStateFingerprint.CombineColdPayload(hash, payload.Scope, payload.Key, payload.Kind, payload.Role, payload.ContentId);
		}
		return ReplicatedStateFingerprint.CombineWeatherState(hash, GameContext.World, worldEntity);
	}

	/// <summary>
	/// How close a peer's camera has to be for a unit to be described at detailed precision. Inside it every movement
	/// above five centimetres is sent; outside it a unit is resent once it has moved a metre from what the peer holds.
	/// </summary>
	private const float DetailedPrecisionRadiusMeters = 35.0f;

	/// <summary>
	/// Whether this unit was described at detailed precision to every remote peer, which is what lets the verifier hold a
	/// baseline to exact equality. Measured against the world origin, the old flag said nothing about any peer: a unit
	/// near the origin was called detailed and then held to exactness by a client that had been sent the one metre resend
	/// cadence, which is what made two freshly produced units near the origin look like state divergences at eight peers.
	/// </summary>
	private bool IsUnitDetailedForEveryRemotePeer(Unit3D unit)
	{
		if (_clientCameraPositions.Count == 0) return false;

		foreach (var cameraPosition in _clientCameraPositions.Values)
		{
			if (unit.GlobalPosition.DistanceTo(cameraPosition) > DetailedPrecisionRadiusMeters) return false;
		}

		return true;
	}

	private WorldSnapshot BuildDiagnosticAuthoritativeSnapshot(List<Unit3D> allUnits, int snapshotSequence, bool isBaseline)
	{
		var snapshotUnits = new List<UnitSnapshot>(allUnits.Count);
		foreach (var unit in allUnits)
		{
			if (!GodotObject.IsInstanceValid(unit)) continue;
			UnitSnapshot snapshot = CreateUnitSnapshot(unit, Vector3.Zero, out _);
			snapshot.IsDetailed = IsUnitDetailedForEveryRemotePeer(unit);
			snapshotUnits.Add(snapshot);
		}

		return new WorldSnapshot
		{
			Sequence = snapshotSequence,
			IsBaseline = isBaseline,
			BaseSequence = isBaseline ? snapshotSequence : (snapshotSequence / 30) * 30,
			Units = snapshotUnits,
			ComponentDeltas = new List<ComponentDelta>(),
			AbilityStates = new List<AbilityStateDelta>(),
			StaticTexts = new List<StaticTextDelta>(),
			Catalog = new List<CatalogEntry>()
		};
	}

	private UnitSnapshot CreateUnitSnapshot(Unit3D unit, Vector3 cameraPos, out bool isDetailed)
	{
		var world = GameContext.World;
		float distToCamera = unit.GlobalPosition.DistanceTo(cameraPos);
		isDetailed = distToCamera <= DetailedPrecisionRadiusMeters;
		System.Numerics.Vector3 position = world.Has<Position>(unit.Entity) ? world.Get<Position>(unit.Entity).Value : new System.Numerics.Vector3(unit.GlobalPosition.X, unit.GlobalPosition.Y, unit.GlobalPosition.Z);
		System.Numerics.Vector3 velocity = world.Has<Velocity>(unit.Entity) ? world.Get<Velocity>(unit.Entity).Value : new System.Numerics.Vector3(unit.Velocity.X, unit.Velocity.Y, unit.Velocity.Z);
		return new UnitSnapshot
		{
			EntityId = unit.Entity.Id,
			UnitId = unit.UnitId,
			OwnerPlayerEntityId = GetOwnerPeerId(unit.Entity),
			OwnerPlayerIndex = GetOwnerPlayerIndex(unit.Entity),
			Position = new NetworkVector3(position.X, position.Y, position.Z),
			RotationY = world.Has<RotationY>(unit.Entity) ? world.Get<RotationY>(unit.Entity).Value : unit.GlobalRotation.Y,
			CurrentHp = world.Has<Health>(unit.Entity) ? world.Get<Health>(unit.Entity).Current : 0f,
			MaxHp = world.Has<Health>(unit.Entity) ? world.Get<Health>(unit.Entity).Max : 0f,
			IsDead = world.Has<Dead>(unit.Entity),
			IsBuilding = unit.IsBuilding,
			IsDetailed = isDetailed,
			Velocity = new NetworkVector3(velocity.X, velocity.Y, velocity.Z)
		};
	}

	/// <summary>
	/// Whether this unit's health differs at all from what the peer holds. A baseline is a recovery point, so it restates
	/// health even when the difference is under the threshold a delta uses to decide a unit is worth resending: a
	/// regenerating unit drifts by a few hundredths, never crosses that threshold, and the peer is left holding a value the
	/// authority has already moved past.
	/// </summary>
	private static bool HealthDiffersFromPeer(in UnitSnapshot current, in UnitSnapshot peerSnapshot)
	{
		return MathF.Abs(current.CurrentHp - peerSnapshot.CurrentHp) > 0.001f
			|| MathF.Abs(current.MaxHp - peerSnapshot.MaxHp) > 0.001f;
	}

	private bool HasUnitChanged(Unit3D unit, ref UnitSnapshot currentSnap, bool isDetailed, UnitSnapshot baseSnap)
	{
		if (isDetailed)
		{
			bool posChanged = System.Numerics.Vector3.Distance(baseSnap.Position.ToNumerics(), currentSnap.Position.ToNumerics()) > 0.05f;
			bool rotChanged = Mathf.Abs(baseSnap.RotationY - currentSnap.RotationY) > 0.05f;
			bool hpChanged = Mathf.Abs(baseSnap.CurrentHp - currentSnap.CurrentHp) > 0.1f;
			bool deadChanged = baseSnap.IsDead != currentSnap.IsDead;
			return posChanged || rotChanged || hpChanged || deadChanged;
		}

		bool posFarChanged = System.Numerics.Vector3.Distance(baseSnap.Position.ToNumerics(), currentSnap.Position.ToNumerics()) > 1.0f;
		bool deadFarChanged = baseSnap.IsDead != currentSnap.IsDead;
		bool rotFarChanged = Mathf.Abs(Mathf.AngleDifference(baseSnap.RotationY, currentSnap.RotationY)) > 0.15f;

		return posFarChanged || deadFarChanged || rotFarChanged || Mathf.Abs(baseSnap.CurrentHp - currentSnap.CurrentHp) > 1.0f;
	}

	public byte[] BuildExplicitBaselineSnapshot(int targetPeerId, List<Unit3D> allUnits)
	{
		Entity worldEntity = FindWorldEntity();
		if (worldEntity == Entity.Null || !GameContext.World.Has<NetworkState>(worldEntity) || !GameContext.World.Has<NetworkMappingState>(worldEntity)) return Array.Empty<byte>();

		ref var networkState = ref GameContext.World.Get<NetworkState>(worldEntity);
		var mapping = GameContext.World.Get<NetworkMappingState>(worldEntity);
		if (!mapping.PeerIdToPlayerEntityMap.TryGetValue(targetPeerId, out var playerEntity)) return Array.Empty<byte>();

		Vector3 cameraPos = _clientCameraPositions.TryGetValue(targetPeerId, out var cam) ? cam : Vector3.Zero;
		var snapshotUnits = new List<UnitSnapshot>();
		ResetClientSnapshotState(targetPeerId);
		ClientSnapshotState state = GetClientSnapshotState(targetPeerId);

		foreach (var unit in allUnits)
		{
			ProcessUnitForBaselineSnapshot(unit, playerEntity, cameraPos, allUnits, snapshotUnits, state.AckedUnits);
		}

		var abilityStates = CollectHostAbilityStateDeltas(true, clearDirty: false);
		var staticTexts = CollectHostStaticTextDeltas(true, clearDirty: false);
		var coldPayloadCandidates = BuildColdPayloadCandidates(networkState.SnapshotSequence);
		var coldPayloads = BuildColdPayloadDeltas(targetPeerId, coldPayloadCandidates);
		var componentDeltas = BuildComponentDeltas(targetPeerId, playerEntity, allUnits, state, networkState.SnapshotSequence, true);

		state.LastBaselineSequence = networkState.SnapshotSequence;
		state.HasSentBaseline = true;
		state.SentCatalog = true;

		var worldSnapshot = new WorldSnapshot
		{
			Sequence = networkState.SnapshotSequence,
			IsBaseline = true,
			BaseSequence = networkState.SnapshotSequence,
			Units = snapshotUnits,
			ColdPayloads = coldPayloads,
			ComponentDeltas = componentDeltas,
			AbilityStates = abilityStates,
			StaticTexts = staticTexts,
			Catalog = BuildCatalogEntriesWithStringTable(targetPeerId),
			UnitComponentPresence = state.UnitComponentPresence,
			StateFingerprint = ComputeHostStateFingerprint(abilityStates, staticTexts, coldPayloadCandidates)
		};
		return SnapshotWireFormat.EncodeBaseline(MemoryPackSerializer.Serialize(worldSnapshot));
	}

	private void ProcessUnitForBaselineSnapshot(Unit3D unit, Entity playerEntity, Vector3 cameraPos, List<Unit3D> allUnits, List<UnitSnapshot> snapshotUnits, Dictionary<int, UnitSnapshot> baselinedUnits)
	{
		if (!GodotObject.IsInstanceValid(unit)) return;
		if (!IsUnitVisibleToPlayer(playerEntity, unit.Entity)) return;

		var currentSnap = CreateUnitSnapshot(unit, cameraPos, out _);
		snapshotUnits.Add(currentSnap);
		baselinedUnits[unit.Entity.Id] = currentSnap;
	}

	private readonly List<DelayedPacket> _readySpectatorPackets = new();
	private readonly List<int> _disconnectedSpectatorPeerIds = new();

	public void QueueSpectatorDelayedPacket(DelayedPacket packet)
	{
		packet.SendTime = (Time.GetTicksMsec() / 1000.0) + 300.0;
		if (!_spectatorDelayedPackets.TryGetValue(packet.PeerId, out var list))
		{
			list = new List<DelayedPacket>();
			_spectatorDelayedPackets[packet.PeerId] = list;
		}

		list.Add(packet);
	}

	public List<DelayedPacket> FlushReadySpectatorPackets()
	{
		_readySpectatorPackets.Clear();
		if (_spectatorDelayedPackets.Count == 0) return _readySpectatorPackets;

		double currentTime = Time.GetTicksMsec() / 1000.0;
		_disconnectedSpectatorPeerIds.Clear();
		foreach (var key in _spectatorDelayedPackets.Keys)
		{
			if (GameContext.Lobby == null || !GameContext.Lobby.PlayerList.Exists(p => p.PeerId == key))
			{
				_disconnectedSpectatorPeerIds.Add(key);
			}
		}

		foreach (var peerId in _disconnectedSpectatorPeerIds)
		{
			_spectatorDelayedPackets.Remove(peerId);
		}

		foreach (var kvp in _spectatorDelayedPackets)
		{
			var list = kvp.Value;
			int sentCount = 0;
			for (int i = 0; i < list.Count; i++)
			{
				if (list[i].SendTime <= currentTime)
				{
					_readySpectatorPackets.Add(list[i]);
					sentCount++;
				}
				else
				{
					break;
				}
			}

			if (sentCount > 0)
			{
				list.RemoveRange(0, sentCount);
			}
		}

		return _readySpectatorPackets;
	}

	public bool IsSpectatorWithDelay(int peerId)
	{
		if (GameContext.Lobby == null) return false;
		var p = GameContext.Lobby.PlayerList.Find(x => x.PeerId == peerId);
		return p != null && p.Team == "Spectator" && GameContext.Lobby.SpectatorDelay;
	}

	

	

	

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

	public List<int> GetPeersToBroadcastSpellEffect(int localPeerId)
	{
		var peers = new List<int>();
		if (GameContext.Lobby == null) return peers;

		foreach (var p in GameContext.Lobby.PlayerList)
		{
			if (p.PeerId == localPeerId || p.PeerId < 0) continue;
			peers.Add(p.PeerId);
		}

		return peers;
	}

	public void MarkClientEnteredMultiplayer()
	{
		WasClientInMultiplayer = true;
		LastSnapshotReceivedTime = Time.GetTicksMsec();
	}

	public void ResetReconnectionState()
	{
		_unitsSentAtDetailedPrecision.Clear();
		_queuedDeltas.Clear();
		Entity worldEntity = FindWorldEntity();
		if (worldEntity != Entity.Null && GameContext.World.Has<NetworkState>(worldEntity))
		{
			ref var ns = ref GameContext.World.Get<NetworkState>(worldEntity);
			ns.HasReceivedInitialBaseline = false;
			ns.LastReceivedBaselineSeq = -1;
			ns.LastAppliedSnapshotSequence = -1;
			ns.LastSnapshotReceivedTime = Time.GetTicksMsec();
		}

		IsConnectionLost = false;
	}

	public void BroadcastMessageDomainLogic(string message) => WasmRuntime.LogToConsole($"[HOST BROADCAST] {message}");

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

	public static bool ArePeersEnemies(int peerId1, int peerId2)
	{
		if (peerId1 == peerId2) return false;

		if (GameContext.Lobby?.PlayerList != null)
		{
			var p1 = GameContext.Lobby.PlayerList.Find(x => x.PeerId == peerId1);
			var p2 = GameContext.Lobby.PlayerList.Find(x => x.PeerId == peerId2);
			if (p1 != null && p2 != null)
			{
				return ArePlayerIndicesEnemies(p1.Slot, p2.Slot);
			}
		}

		if (!HasActiveLobby()) return true;

		int t1 = GetTeamIndexForPeer(peerId1, 1);
		int t2 = GetTeamIndexForPeer(peerId2, 2);
		return t1 != t2;
	}


	public int DetermineOwnerPeerId(bool isEnemy, int localPeerId)
	{
		if (!isEnemy) return localPeerId;

		var mappingEntity = GameContext.Simulation.WorldEntity;
		if (mappingEntity == Entity.Null || !GameContext.World.Has<NetworkMappingState>(mappingEntity)) return -1;

		var mapping = GameContext.World.Get<NetworkMappingState>(mappingEntity);
		foreach (var kvp in mapping.PeerIdToPlayerEntityMap)
		{
			if (kvp.Key != localPeerId) return kvp.Key;
		}

		return -1;
	}

	public Entity DeterminePlayerOwnerEntity(int ownerPeerId, bool actualIsEnemy, Dictionary<int, Entity> peerIdToPlayerEntityMap, Entity[] playerEntities, int localSlot)
	{
		if (peerIdToPlayerEntityMap != null && peerIdToPlayerEntityMap.TryGetValue(ownerPeerId, out var pe) && GameContext.World.IsAlive(pe))
		{
			return pe;
		}

		if (playerEntities != null && playerEntities.Length > 0)
		{
			if (actualIsEnemy)
			{
				if (playerEntities.Length > 2 && playerEntities[2] != Entity.Null && GameContext.World.IsAlive(playerEntities[2]))
					return playerEntities[2];
			}
			else
			{
				if (localSlot >= 0 && localSlot < playerEntities.Length && playerEntities[localSlot] != Entity.Null && GameContext.World.IsAlive(playerEntities[localSlot]))
					return playerEntities[localSlot];
			}
		}

		return Entity.Null;
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

	public static bool IsPlayerEnemy(int playerIndex, int localPlayerIndex)
	{
		return ArePlayerIndicesEnemies(localPlayerIndex, playerIndex);
	}

	public void ToggleLocalPause()
	{
		IsPaused = !IsPaused;
		if (!IsPaused)
		{
			ResumeCountdownSeconds = -1;
		}
	}

	public void StopCountdownTimerDomain(bool isServer, Action rpcAction)
	{
		BroadcastActionIfServer(isServer, rpcAction);
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

	public void SetEnvironmentPreset(string presetId, bool isMultiplayer, bool isServer, Action<string> viewAction, Action<string> rpcAction)
	{
		viewAction?.Invoke(presetId);
		if (isMultiplayer && isServer) rpcAction?.Invoke(presetId);
	}

	public void TransitionEnvironmentPreset(string presetId, float durationSeconds, bool isMultiplayer, bool isServer, Action<string, float> viewAction, Action<string, float> rpcAction)
	{
		viewAction?.Invoke(presetId, durationSeconds);
		if (isMultiplayer && isServer) rpcAction?.Invoke(presetId, durationSeconds);
	}

	public void SetWeather(string weatherType, bool isMultiplayer, bool isServer, Action<string> viewAction, Action<string> rpcAction)
	{
		viewAction?.Invoke(weatherType);
		if (isMultiplayer && isServer) rpcAction?.Invoke(weatherType);
	}

	public void RemoveMultiplayerMapping(int entityId)
	{
		if (!GameContext.World.TryGet<NetworkMappingState>(GameContext.Simulation.WorldEntity, out var mapState)) return;

		if (mapState.ClientToServerEntityMap.TryGetValue(entityId, out int serverEntityId))
		{
			mapState.ServerToClientEntityMap.Remove(serverEntityId);
			ReplicatedEntityReferences.Unregister(serverEntityId);
		}

		mapState.ClientToServerEntityMap.Remove(entityId);
	}

	public void ProcessRequestPause(int peerId, bool isServer, Action<bool, int, bool> broadcastPauseStateRpc, Action<bool> startCountdown)
	{
		if (!isServer) return;

		if (DisallowedPausePeers.TryGetValue(peerId, out bool disallowed) && disallowed)
		{
			return;
		}

		if (ResumeCountdownSeconds >= 0)
		{
			ResumeCountdownSeconds = -1;
			CountdownForcedByHost = false;
			IsPaused = true;
			broadcastPauseStateRpc?.Invoke(true, peerId, false);
			return;
		}

		IsPaused = !IsPaused;
		if (IsPaused)
		{
			ResumeCountdownSeconds = -1;
			CountdownForcedByHost = false;
			PlayerReadyStates.Clear();
			broadcastPauseStateRpc?.Invoke(true, peerId, false);
		}
		else
		{
			startCountdown?.Invoke(peerId == 1);
		}
	}

	public void ProcessRequestToggleReady(int peerId, bool ready, bool isServer, Func<bool> checkAllPlayersReady, Action<bool> startCountdown, Action<string> broadcastReadyStatesRpc, Action broadcastCountdownCancelRpc)
	{
		if (!isServer) return;
		if (!IsPaused) return;

		PlayerReadyStates[peerId] = ready;

		var serializedReady = JsonSerializer.Serialize(PlayerReadyStates);
		broadcastReadyStatesRpc?.Invoke(serializedReady);

		if (checkAllPlayersReady?.Invoke() == true)
		{
			startCountdown?.Invoke(false);
		}
		else if (ResumeCountdownSeconds >= 0 && !CountdownForcedByHost)
		{
			ResumeCountdownSeconds = -1;
			broadcastCountdownCancelRpc?.Invoke();
		}
	}

	public void ProcessRequestSetDisallowPause(int peerId, bool disallowed, bool isServer, int senderId, Action<string> broadcastDisallowedPausePeersRpc)
	{
		if (!isServer) return;
		if (senderId != 1) return;

		DisallowedPausePeers[peerId] = disallowed;

		var serializedDisallowed = JsonSerializer.Serialize(DisallowedPausePeers);
		broadcastDisallowedPausePeersRpc?.Invoke(serializedDisallowed);
	}

	public void ProcessRequestForceResume(bool isServer, int senderId, Action<bool> startCountdown)
	{
		if (!isServer) return;
		if (senderId != 1) return;

		startCountdown?.Invoke(true);
	}

	public void ProcessStartCountdown(bool forcedByHost, Action<int, bool> broadcastCountdownStateRpc)
	{
		ResumeCountdownSeconds = 5;
		ResumeCountdownTimer = 0f;
		CountdownForcedByHost = forcedByHost;
		broadcastCountdownStateRpc?.Invoke(5, forcedByHost);
	}

	public bool ProcessCheckAllPlayersReady(IEnumerable<PlayerInfo> playerList)
	{
		if (playerList == null) return true;
		foreach (var player in playerList)
		{
			if (player.Team == "Spectator") continue;
			PlayerReadyStates.TryGetValue(player.PeerId, out bool ready);
			if (!ready) return false;
		}

		return true;
	}

	public void ProcessBroadcastPauseState(bool paused, bool instantResume, Action updatePauseUI)
	{
		IsPaused = paused;
		if (paused || instantResume)
		{
			ResumeCountdownSeconds = -1;
		}

		updatePauseUI?.Invoke();
	}

	public void ProcessBroadcastReadyStates(string serializedReadyStates, Action updatePauseUI)
	{
		var states = JsonSerializer.Deserialize<Dictionary<int, bool>>(serializedReadyStates);
		if (states != null)
		{
			PlayerReadyStates.Clear();
			foreach (var kvp in states)
			{
				PlayerReadyStates[kvp.Key] = kvp.Value;
			}
		}

		updatePauseUI?.Invoke();
	}

	public void ProcessBroadcastDisallowedPausePeers(string serializedDisallowed, Action updatePauseUI)
	{
		var states = JsonSerializer.Deserialize<Dictionary<int, bool>>(serializedDisallowed);
		if (states != null)
		{
			DisallowedPausePeers.Clear();
			foreach (var kvp in states)
			{
				DisallowedPausePeers[kvp.Key] = kvp.Value;
			}
		}

		updatePauseUI?.Invoke();
	}

	public void ProcessBroadcastCountdownState(int seconds, bool forcedByHost, Action updatePauseUI)
	{
		ResumeCountdownSeconds = seconds;
		CountdownForcedByHost = forcedByHost;
		updatePauseUI?.Invoke();
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