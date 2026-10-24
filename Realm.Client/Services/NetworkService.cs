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
using HttpClient = System.Net.Http.HttpClient;

namespace Realm.Client.Services;

public class NetworkService
{
	private readonly List<NetworkCommand> _unacknowledgedCommands = new();
	private const int MaximumQueuedDeltas = 512;

	private readonly List<WorldSnapshot> _queuedDeltas = new();
	private readonly Dictionary<int, Vector3> _clientCameraPositions = new();
	private readonly Dictionary<int, Dictionary<int, UnitSnapshot>> _lastBaselineSnapshotsPerClient = new();
	private readonly Dictionary<int, Dictionary<(byte Scope, int Key, int TypeId), byte[]>> _lastComponentSnapshotsPerClient = new();
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
	private readonly HashSet<int> _baselineUnitServerIds = new();
	private readonly HashSet<(int Key, int TypeId)> _baselineUnitComponents = new();
	private byte[] _baselineReconcileScratch = new byte[256];


	public bool IsPaused { get; set; } = false;
	public int LastAppliedSnapshotSequenceForDiagnostics { get; private set; } = -1;
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
		Entity worldEntity = ECSContext.WorldEntity;
		if (worldEntity != Entity.Null && GameContext.World.Has<NetworkMappingState>(worldEntity))
		{
			GameContext.World.Mutate<NetworkMappingState>(worldEntity, (ref NetworkMappingState mapping) =>
			{
				if (mapping.ClientToServerEntityMap.TryGetValue(localEntityId, out int serverId))
				{
					mapping.ServerToClientEntityMap.Remove(serverId);
				}

				mapping.ClientToServerEntityMap.Remove(localEntityId);
			});
		}
	}


	public void SetGameOver()
	{
		ECSContext.IsGameOver = true;
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
		if (ECSContext.WorldEntity == Entity.Null || GameContext.World == null || !GameContext.World.Has<NetworkMappingState>(ECSContext.WorldEntity)) return;

		if (playerEntity != Entity.Null)
		{
			var mapping = GameContext.World.Get<NetworkMappingState>(ECSContext.WorldEntity);
			mapping.PeerIdToPlayerEntityMap.Remove(oldPeerId);
			mapping.PeerIdToPlayerEntityMap[newPeerId] = playerEntity;
		}
	}

	public bool TryGetReconnectedCountdown(out float duration, out string text)
	{
		duration = 0f;
		text = string.Empty;

		if (ECSContext.WorldEntity == Entity.Null || GameContext.World == null || !GameContext.World.Has<CountdownState>(ECSContext.WorldEntity)) return false;

		var countdown = GameContext.World.Get<CountdownState>(ECSContext.WorldEntity);
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
		if (ECSContext.WorldEntity == Entity.Null || GameContext.World == null || !GameContext.World.Has<NetworkMappingState>(ECSContext.WorldEntity)) return;

		var mapping = GameContext.World.Get<NetworkMappingState>(ECSContext.WorldEntity);
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
		_unacknowledgedCommands.Clear();
		_queuedDeltas.Clear();
		_clientCameraPositions.Clear();
		_lastBaselineSnapshotsPerClient.Clear();
		_spectatorDelayedPackets.Clear();
		_pendingUnitSpawns.Clear();
		_pendingUnitKills.Clear();
		_pendingUnitDespawns.Clear();
		_hiddenOwnEntityIds.Clear();
		_untrackedServerIds.Clear();
		_playerTeams.Clear();
		_alliances.Clear();
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

	private void EnqueueCommand(Entity entity, string type, System.Numerics.Vector3 position, Entity target = default)
	{
		if (!GameContext.World.Has<BuildQueue>(entity))
		{
			GameContext.World.Add(entity, new BuildQueue());
		}

		ref var q = ref GameContext.World.Get<BuildQueue>(entity);
		q.TryEnqueue(type, position, target);
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

	public bool IsClientAuthorized(int peerId, Entity unitEntity)
	{
		if (!GameContext.World.Has<Owner>(unitEntity)) return false;
		var ownerComp = GameContext.World.Get<Owner>(unitEntity);
		Entity worldEntity = FindWorldEntity();
		if (worldEntity == Entity.Null || !GameContext.World.Has<NetworkMappingState>(worldEntity)) return false;
		var mapping = GameContext.World.Get<NetworkMappingState>(worldEntity);
		if (mapping.PeerIdToPlayerEntityMap.TryGetValue(peerId, out var playerEntity))
		{
			return ownerComp.PlayerEntity.Value == playerEntity;
		}

		return false;
	}

	public bool IsUnitVisibleToPlayer(Entity playerEntity, Entity unitEntity, List<Unit3D> allUnits)
	{
		if (IsShroudRevealed())
		{
			return true;
		}

		if (GameContext.World.Has<Owner>(unitEntity) && GameContext.World.Get<Owner>(unitEntity).PlayerEntity.Value == playerEntity)
		{
			return true;
		}

		Vector3 unitPos = Vector3.Zero;
		foreach (var unit in allUnits)
		{
			if (unit.Entity == unitEntity)
			{
				unitPos = unit.GlobalPosition;
				break;
			}
		}

		foreach (var unit in allUnits)
		{
			if (GameContext.World.Has<Owner>(unit.Entity) && GameContext.World.Get<Owner>(unit.Entity).PlayerEntity.Value == playerEntity)
			{
				if (unit.GlobalPosition.DistanceTo(unitPos) <= 15.0f)
				{
					return true;
				}
			}
		}

		return false;
	}

	public bool IsShroudRevealed()
	{
		if (GameContext.World == null) return false;
		Entity worldEntity = FindWorldEntity();
		if (worldEntity == Entity.Null || !GameContext.World.IsAlive(worldEntity)) return false;
		if (!GameContext.World.Has<ShroudState>(worldEntity)) return false;
		return GameContext.World.Get<ShroudState>(worldEntity).VisibilityMode == ShroudVisibilityMode.Revealed;
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
		List<Unit3D> allUnits = gameHost.AllUnits;
		Entity unitEntity = new(serverEntityId);
		int localPeerId = gameHost.Multiplayer.GetUniqueId();
		bool hasRemoteClient = false;

		foreach (PlayerInfo player in lobby.PlayerList)
		{
			if (player.PeerId <= 0 || player.PeerId == localPeerId) continue;
			if (player.IsDisconnected) continue;
			if (!mapping.PeerIdToPlayerEntityMap.TryGetValue(player.PeerId, out Entity playerEntity)) continue;

			hasRemoteClient = true;
			if (IsUnitVisibleToPlayer(playerEntity, unitEntity, allUnits)) return true;
		}

		return !hasRemoteClient;
	}

	public (bool Success, string BuildUnitType, string TargetModel, PlayerEntity PlayerOwner, System.Numerics.Vector3 BuildPosition, string Name, float MaxHp, float Damage, float Range, float Armor) ProcessServerCommandBuildDomain(
		ServerCommandResult result,
		Dictionary<string, UnitMetadata> unitRegistry,
		Dictionary<long, Entity> peerIdToPlayerEntityMap,
		Entity buildWorkerEntity)
	{
		if (!result.NeedsBuildUnit || !unitRegistry.TryGetValue(result.BuildUnitType, out var meta)) return (false, "", "", default, default, "", 0, 0, 0, 0);

		var playerOwner = peerIdToPlayerEntityMap[result.BuildPeerOwner].AsPlayerEntity(GameContext.World);
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
		
		GameContext.World?.Mutate<NetworkState>(ECSContext.WorldEntity, (ref NetworkState netState) =>
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
		_unacknowledgedCommands.Add(cmd);
		return (cmd.CommandId, MemoryPackSerializer.Serialize(cmd));
	}

	public IReadOnlyList<byte[]> GetUnacknowledgedCommandPayloads()
	{
		var payloads = new List<byte[]>(_unacknowledgedCommands.Count);
		foreach (var cmd in _unacknowledgedCommands)
		{
			payloads.Add(MemoryPackSerializer.Serialize(cmd));
		}

		return payloads;
	}

	public void AcknowledgeCommand(int commandId)
	{
		_unacknowledgedCommands.RemoveAll(c => c.CommandId == commandId);
	}

	public void RecordClientCameraPosition(int peerId, Vector3 position)
	{
		_clientCameraPositions[peerId] = position;
	}

	public void RecordSnapshotReceived(byte[] payload)
	{
		Entity worldEntity = FindWorldEntity();
		if (worldEntity == Entity.Null || !GameContext.World.Has<NetworkState>(worldEntity)) return;
		ref var ns = ref GameContext.World.Get<NetworkState>(worldEntity);
		ns.LastSnapshotReceivedTime = Time.GetTicksMsec();
		ProcessSnapshotDirect(payload);
	}

	private void ProcessSnapshotDirect(byte[] payload)
	{
		var snapshot = MemoryPackSerializer.Deserialize<WorldSnapshot>(payload);
		if (MultiplayerSyncDiagnostics.IsRecording) MultiplayerSyncDiagnostics.RecordClientSeenSequence(snapshot.Sequence);
		Entity worldEntity = FindWorldEntity();
		if (worldEntity == Entity.Null || !GameContext.World.Has<NetworkState>(worldEntity)) return;

		ref var networkState = ref GameContext.World.Get<NetworkState>(worldEntity);

		if (snapshot.IsBaseline)
		{
			if (snapshot.Sequence <= networkState.LastReceivedBaselineSeq) return;

			networkState.LastReceivedBaselineSeq = snapshot.Sequence;
			networkState.HasReceivedInitialBaseline = true;
			networkState.LastAppliedSnapshotSequence = snapshot.Sequence;
			_queuedDeltas.RemoveAll(d => d.Sequence <= snapshot.Sequence);
			ApplyWorldSnapshot(snapshot);

			while (_queuedDeltas.Count > 0 && _queuedDeltas[0].BaseSequence == snapshot.Sequence)
			{
				var nextDelta = _queuedDeltas[0];
				_queuedDeltas.RemoveAt(0);
				networkState.LastAppliedSnapshotSequence = nextDelta.Sequence;
				ApplyWorldSnapshot(nextDelta);
			}

			return;
		}

		if (snapshot.Sequence <= networkState.LastAppliedSnapshotSequence) return;

		if (!networkState.HasReceivedInitialBaseline || snapshot.BaseSequence != networkState.LastReceivedBaselineSeq)
		{
			QueueDelta(snapshot);
			return;
		}

		networkState.LastAppliedSnapshotSequence = snapshot.Sequence;
		ApplyWorldSnapshot(snapshot);
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

		foreach (var snap in snapshot.Units)
		{
			ApplySnapshotToUnit(snap, snapshot.Sequence, mapping);
		}

		ApplyReplicatedComponentState(snapshot);

		LastAppliedSnapshotSequenceForDiagnostics = snapshot.Sequence;
		MultiplayerSyncDiagnostics.RecordClientSnapshot();
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
				if (!mapping.ServerToClientEntityMap.TryGetValue(serverEntityId, out var localEntity)) continue;
				if (GameContext.World.Has<GroundLoot>(localEntity)) continue;

				if (IsLocallyOwnedEntity(localEntity))
			{
				HideUntrackedOwnEntity(localEntity);
				continue;
			}

			RetireClientEntityForUntrackedServerId(mapping, localEntity, serverEntityId);
		}
	}

	private void ProcessBaselineComponentReconciliation(WorldSnapshot snapshot, NetworkMappingState mapping)
	{
		if (snapshot.ComponentDeltas == null || snapshot.Units == null) return;

		_baselineUnitServerIds.Clear();
		for (int index = 0; index < snapshot.Units.Count; index++)
		{
			_baselineUnitServerIds.Add(snapshot.Units[index].EntityId);
		}

		_baselineUnitComponents.Clear();
		for (int index = 0; index < snapshot.ComponentDeltas.Count; index++)
		{
			var delta = snapshot.ComponentDeltas[index];
			if ((ReplicatedScope)delta.Scope != ReplicatedScope.Unit) continue;
			if (delta.Bytes == null || delta.Bytes.Length == 0) continue;
			_baselineUnitComponents.Add((delta.Key, delta.ComponentTypeId));
		}

		IReadOnlyList<IReplicatedComponentCodec> codecs = ReplicatedComponents.All;
		foreach (var kvp in mapping.ServerToClientEntityMap)
		{
			int serverEntityId = kvp.Key;
			if (!_baselineUnitServerIds.Contains(serverEntityId)) continue;

			Entity localEntity = kvp.Value;
			if (!GameContext.World.IsAlive(localEntity)) continue;

			for (int codecIndex = 0; codecIndex < codecs.Count; codecIndex++)
			{
				var codec = codecs[codecIndex];
				if (codec.Scope != ReplicatedScope.Unit) continue;
				if (_baselineUnitComponents.Contains((serverEntityId, codec.ComponentTypeId))) continue;

				if (_baselineReconcileScratch.Length < codec.SizeInBytes) _baselineReconcileScratch = new byte[codec.SizeInBytes];
				if (!codec.TryWrite(GameContext.World, localEntity, _baselineReconcileScratch.AsSpan(0, codec.SizeInBytes))) continue;
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

	private readonly HashSet<int> _hiddenOwnEntityIds = new();

	private bool IsLocallyOwnedEntity(Entity entity)
	{
		int localPlayerIndex = GameContext.Host?.LocalPlayerIndex ?? -1;
		if (localPlayerIndex < 0) return false;
		if (GameContext.World == null || !GameContext.World.IsAlive(entity)) return false;

		return GameContext.World.Has<UnitOwnerPlayer>(entity) && GameContext.World.Get<UnitOwnerPlayer>(entity).PlayerIndex == localPlayerIndex;
	}

	private void HideUntrackedOwnEntity(Entity localEntity)
	{
		if (!_hiddenOwnEntityIds.Add(localEntity.Id)) return;
		GameContext.Host?.SetReplicatedEntityVisible(localEntity, false);
	}

	private void RestoreHiddenOwnEntity(Entity localEntity)
	{
		if (!_hiddenOwnEntityIds.Remove(localEntity.Id)) return;
		GameContext.Host?.SetReplicatedEntityVisible(localEntity, true);
	}

	private void RetireClientEntityForUntrackedServerId(NetworkMappingState mapping, Entity localEntity, int serverEntityId)
	{
		if (GameContext.World.IsAlive(localEntity) && !GameContext.World.Has<Dead>(localEntity))
		{
			_pendingUnitDespawns.Add(localEntity);
		}

		_hiddenOwnEntityIds.Remove(localEntity.Id);
		mapping.ServerToClientEntityMap.Remove(serverEntityId);
		mapping.ClientToServerEntityMap.Remove(localEntity.Id);
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

		RestoreHiddenOwnEntity(localEntity);

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

		var target = new InterpolationTarget
		{
			Position = snap.Position.ToNumerics(),
			Velocity = snap.Velocity.ToNumerics(),
			RotationY = snap.RotationY
		};
		GameContext.World.SetOrAdd(localEntity, target);
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
		return ApplySnapshotToEntity(snap.UnitId, snap.EntityId, snap.OwnerPlayerEntityId, snap.Position.ToNumerics(), snap.Velocity.ToNumerics(), snap.RotationY, snap.CurrentHp, snap.MaxHp, snap.IsBuilding, meta, ref mapping, getFallbackModelPath, out modelPath, out playerIndex, snap.OwnerPlayerIndex);
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
		GameContext.World.Add(entity, new Position(position));
		GameContext.World.Add(entity, new Owner(ownerPlayerEntity.AsPlayerEntity(GameContext.World)));
		GameContext.World.Add(entity, new UnitOwnerPlayer(playerIndex));
		GameContext.World.Add(entity, new Health(currentHp, maxHp));
		if (meta.Damage > 0)
		{
			GameContext.World.Add(entity, new Attack(meta.Damage, meta.Range, meta.AttackCooldown));
		}

		GameContext.World.Add(entity, new Armor(meta.Armor));
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

	private void ApplyReplicatedComponentState(WorldSnapshot snapshot)
	{
		if (snapshot.ComponentDeltas != null)
		{
			for (int i = 0; i < snapshot.ComponentDeltas.Count; i++)
			{
				ApplyComponentDelta(snapshot.ComponentDeltas[i]);
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

	private void ApplyComponentDelta(ComponentDelta delta)
	{
		var codec = ReplicatedComponents.Find(delta.ComponentTypeId);
		if (codec == null)
		{
			GD.PushWarning($"[Network] Received delta for unregistered component type {delta.ComponentTypeId}");
			return;
		}

		if (delta.Bytes == null || (delta.Bytes.Length != 0 && delta.Bytes.Length != codec.SizeInBytes))
		{
			GD.PushWarning($"[Network] Discarded {codec.ComponentName} delta with {(delta.Bytes?.Length ?? 0)} bytes, expected {codec.SizeInBytes}");
			return;
		}

		Entity targetEntity = Entity.Null;
		if ((ReplicatedScope)delta.Scope == ReplicatedScope.Player)
		{
			Entity worldEntity = FindWorldEntity();
			if (worldEntity == Entity.Null || !GameContext.World.Has<NetworkMappingState>(worldEntity)) return;
			var mapping = GameContext.World.Get<NetworkMappingState>(worldEntity);
			if (mapping.PlayerEntities == null || delta.Key < 0 || delta.Key >= mapping.PlayerEntities.Length) return;
			targetEntity = mapping.PlayerEntities[delta.Key];
		}
		else if ((ReplicatedScope)delta.Scope == ReplicatedScope.Unit)
		{
			if (!TryGetLocalEntity(delta.Key, out targetEntity))
			{
				if (codec.ComponentTypeId != ComponentType<GroundLoot>.Id || delta.Bytes.Length == 0) return;
				Entity worldEntity = FindWorldEntity();
				if (worldEntity == Entity.Null || !GameContext.World.Has<NetworkMappingState>(worldEntity)) return;
				targetEntity = CreateReplicatedLootEntity(delta.Key, GameContext.World.Get<NetworkMappingState>(worldEntity));
			}
		}
		else if ((ReplicatedScope)delta.Scope == ReplicatedScope.World)
		{
			targetEntity = ECSContext.WorldEntity;
		}

		if (targetEntity == Entity.Null || !GameContext.World.IsAlive(targetEntity)) return;
		if (delta.Bytes.Length == 0)
		{
			codec.Remove(GameContext.World, targetEntity);
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
			if (codec.ComponentTypeId == ComponentType<GroundLoot>.Id) GameContext.Host?.SyncReplicatedLootNode(delta.Key, targetEntity);
		}

		_mirror.RecordComponent(delta);
		_appliedComponentDeltaCount++;
	}

	private static Entity CreateReplicatedLootEntity(int serverEntityId, NetworkMappingState mapping)
	{
		Entity localEntity = GameContext.World.Create();
		mapping.ServerToClientEntityMap[serverEntityId] = localEntity;
		mapping.ClientToServerEntityMap[localEntity.Id] = serverEntityId;
		ReplicatedEntityReferences.Register(serverEntityId, localEntity.Id);
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
		ApplyAbilityStateToUnit(delta.ServerEntityId, delta.AbilityId, delta.Flags);
	}

	private void ApplyAbilityStateToUnit(int serverEntityId, string abilityId, byte flags)
	{
		if (!TryGetLocalEntity(serverEntityId, out var localEntity)) return;
		GameContext.RegistryService?.SetAbilityState(new UnitId(localEntity.Id), abilityId, (flags & 1) != 0, (flags & 2) != 0);
	}

	public void ReapplyAbilityStatesToUnits()
	{
		if (_mirror.Abilities.Count == 0) return;
		foreach (var kvp in _mirror.Abilities)
		{
			ApplyAbilityStateToUnit(kvp.Key.ServerEntityId, kvp.Key.AbilityId, kvp.Value);
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
		_lastComponentSnapshotsPerClient.Clear();
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


	public ServerCommandResult ExecuteServerCommand(int peerId, NetworkCommand cmd, List<Unit3D> allUnits, List<Prop3D> allProps)
	{
		var stopVelocityEntityIds = new List<int>();
		var holdVelocityEntityIds = new List<int>();
		var rejectedServerEntityIds = new List<int>();
		string rejectionReason = string.Empty;

		DispatchServerCommand(peerId, cmd, allUnits, allProps, stopVelocityEntityIds, holdVelocityEntityIds, rejectedServerEntityIds, ref rejectionReason);

		bool needsBuildUnit = cmd.CommandType == "build";
		string buildUnitType = needsBuildUnit ? cmd.ArgString : null;
		Vector3 buildPosition = needsBuildUnit ? cmd.TargetPosition.ToGodot() : Vector3.Zero;
		int buildPeerOwner = needsBuildUnit ? peerId : -1;
		int buildWorkerServerEntityId = needsBuildUnit && cmd.UnitEntityIds.Count > 0 ? cmd.UnitEntityIds[0] : -1;

		bool needsSpellEffect = cmd.CommandType == "spell";
		if (needsSpellEffect && !TryAuthorizeSpellCommand(peerId, cmd, allUnits, out string spellRejection))
		{
			needsSpellEffect = false;
			if (string.IsNullOrEmpty(rejectionReason)) rejectionReason = spellRejection;
			if (cmd.UnitEntityIds.Count > 0) RecordCommandRejection(rejectedServerEntityIds, cmd.UnitEntityIds[0], spellRejection);
		}

		string spellId = needsSpellEffect ? cmd.ArgString : null;
		Vector3 spellPosition = needsSpellEffect ? cmd.TargetPosition.ToGodot() : Vector3.Zero;

		return new ServerCommandResult(
			needsBuildUnit, buildUnitType, buildPosition, buildPeerOwner, buildWorkerServerEntityId,
			needsSpellEffect, spellId, spellPosition,
			stopVelocityEntityIds, holdVelocityEntityIds,
			rejectedServerEntityIds, rejectionReason);
	}

	public bool TryGetPlayerEntityForPeer(int peerId, out Entity playerEntity)
	{
		playerEntity = Entity.Null;
		Entity worldEntity = FindWorldEntity();
		if (worldEntity == Entity.Null || !GameContext.World.Has<NetworkMappingState>(worldEntity)) return false;
		var mapping = GameContext.World.Get<NetworkMappingState>(worldEntity);
		if (!mapping.PeerIdToPlayerEntityMap.TryGetValue(peerId, out playerEntity)) return false;
		return playerEntity != Entity.Null && GameContext.World.IsAlive(playerEntity);
	}

	public bool ValidateSpellCommandForHarness(int peerId, NetworkCommand cmd, out string rejectionReason)
	{
		return ValidateSpellCommand(peerId, cmd, GameContext.Host?.AllUnits ?? new List<Unit3D>(), out _, out rejectionReason);
	}

	private bool TryAuthorizeSpellCommand(int peerId, NetworkCommand cmd, List<Unit3D> allUnits, out string rejectionReason)
	{
		if (!ValidateSpellCommand(peerId, cmd, allUnits, out Entity casterEntity, out rejectionReason)) return false;

		string spellId = cmd.ArgString ?? string.Empty;
		GameContext.RegistryService.AbilityDefinitions.TryGetValue(spellId, out AbilityDefinition? definition);
		if (definition == null) return false;

		if (!TryGetPlayerEntityForPeer(peerId, out Entity playerEntity)) return false;

		if (definition.ManaCost > 0f && GameContext.World.Has<Mana>(casterEntity))
		{
			ref var mana = ref GameContext.World.Get<Mana>(casterEntity);
			mana.Current -= definition.ManaCost;
		}

		if (definition.Cooldown > 0f && GameContext.World.Has<SpellCooldowns>(playerEntity))
		{
			ref var cooldowns = ref GameContext.World.Get<SpellCooldowns>(playerEntity);
			cooldowns.Set(spellId, definition.Cooldown, definition.Cooldown);
		}

		return true;
	}

	private bool ValidateSpellCommand(int peerId, NetworkCommand cmd, List<Unit3D> allUnits, out Entity casterEntity, out string rejectionReason)
	{
		casterEntity = Entity.Null;
		rejectionReason = string.Empty;

		if (cmd.UnitEntityIds == null || cmd.UnitEntityIds.Count == 0)
		{
			rejectionReason = "spell command does not name a caster";
			return false;
		}

		string spellId = cmd.ArgString ?? string.Empty;
		if (!GameContext.RegistryService.AbilityDefinitions.TryGetValue(spellId, out AbilityDefinition? definition) || definition == null)
		{
			rejectionReason = $"spell '{spellId}' is not defined";
			return false;
		}

		casterEntity = GetSpellCasterEntityDomain(cmd, allUnits);
		if (casterEntity == Entity.Null)
		{
			rejectionReason = $"caster {cmd.UnitEntityIds[0]} is not a live unit";
			return false;
		}

		if (!IsClientAuthorized(peerId, casterEntity))
		{
			rejectionReason = $"caster {cmd.UnitEntityIds[0]} is not owned by peer {peerId}";
			return false;
		}

		if (!TryGetPlayerEntityForPeer(peerId, out Entity playerEntity))
		{
			rejectionReason = $"peer {peerId} has no player entity";
			return false;
		}

		if (definition.Cooldown > 0f && GameContext.World.Has<SpellCooldowns>(playerEntity))
		{
			ref var cooldowns = ref GameContext.World.Get<SpellCooldowns>(playerEntity);
			if (cooldowns.TryGetValue(spellId, out float remaining) && remaining > 0f)
			{
				rejectionReason = $"spell '{spellId}' is on cooldown for {remaining:F1} s";
				return false;
			}
		}

		if (definition.ManaCost > 0f && GameContext.World.Has<Mana>(casterEntity))
		{
			ref var mana = ref GameContext.World.Get<Mana>(casterEntity);
			if (mana.Current < definition.ManaCost)
			{
				rejectionReason = $"caster needs {definition.ManaCost:F0} mana and has {mana.Current:F0}";
				return false;
			}
		}

		if (definition.TargetRange > 0f && GameContext.World.Has<Position>(casterEntity))
		{
			System.Numerics.Vector3 casterPosition = GameContext.World.Get<Position>(casterEntity).Value;
			System.Numerics.Vector3 targetPosition = cmd.TargetPosition.ToNumerics();
			float distance = System.Numerics.Vector3.Distance(casterPosition, targetPosition);
			if (distance > definition.TargetRange)
			{
				rejectionReason = $"target is {distance:F1} m away, beyond the {definition.TargetRange:F1} m range of '{spellId}'";
				return false;
			}
		}

		return true;
	}

	private void DispatchServerCommand(int peerId, NetworkCommand cmd, List<Unit3D> allUnits, List<Prop3D> allProps, List<int> stopVelocityEntityIds, List<int> holdVelocityEntityIds, List<int> rejectedServerEntityIds, ref string rejectionReason)
	{
		if (DispatchMovementCommand(peerId, cmd, allUnits, stopVelocityEntityIds, holdVelocityEntityIds, rejectedServerEntityIds, ref rejectionReason)) return;
		if (DispatchActionCommand(peerId, cmd, allUnits, allProps, rejectedServerEntityIds, ref rejectionReason)) return;
		DispatchTrainingCommand(peerId, cmd, allUnits, rejectedServerEntityIds, ref rejectionReason);
	}

	private bool DispatchMovementCommand(int peerId, NetworkCommand cmd, List<Unit3D> allUnits, List<int> stopVelocityEntityIds, List<int> holdVelocityEntityIds, List<int> rejectedServerEntityIds, ref string rejectionReason)
	{
		switch (cmd.CommandType)
		{
			case "move":
				HandleMoveCommand(peerId, cmd, allUnits, rejectedServerEntityIds, ref rejectionReason);
				return true;
			case "move_queued":
				HandleMoveQueuedCommand(peerId, cmd, allUnits, rejectedServerEntityIds, ref rejectionReason);
				return true;
			case "attack_move":
				HandleAttackMoveCommand(peerId, cmd, allUnits, false, rejectedServerEntityIds, ref rejectionReason);
				return true;
			case "attack_move_queued":
				HandleAttackMoveCommand(peerId, cmd, allUnits, true, rejectedServerEntityIds, ref rejectionReason);
				return true;
			case "stop":
				HandleStopCommand(peerId, cmd, allUnits, stopVelocityEntityIds, rejectedServerEntityIds, ref rejectionReason);
				return true;
			case "hold":
				HandleHoldCommand(peerId, cmd, allUnits, holdVelocityEntityIds, rejectedServerEntityIds, ref rejectionReason);
				return true;
			case "patrol":
			case "patrol_queued":
				HandlePatrolCommand(peerId, cmd, allUnits, cmd.CommandType == "patrol_queued", rejectedServerEntityIds, ref rejectionReason);
				return true;
		}

		return false;
	}

	private bool DispatchActionCommand(int peerId, NetworkCommand cmd, List<Unit3D> allUnits, List<Prop3D> allProps, List<int> rejectedServerEntityIds, ref string rejectionReason)
	{
		bool isQueued = cmd.CommandType.EndsWith("_queued");
		switch (cmd.CommandType)
		{
			case "attack":
			case "attack_queued":
				HandleAttackCommand(peerId, cmd, allUnits, isQueued, rejectedServerEntityIds, ref rejectionReason);
				return true;
			case "follow":
			case "follow_queued":
				HandleFollowCommand(peerId, cmd, allUnits, isQueued, rejectedServerEntityIds, ref rejectionReason);
				return true;
			case "gather":
			case "gather_queued":
				HandleGatherCommand(peerId, cmd, allUnits, allProps, isQueued, rejectedServerEntityIds, ref rejectionReason);
				return true;
		}

		return false;
	}

	private void DispatchTrainingCommand(int peerId, NetworkCommand cmd, List<Unit3D> allUnits, List<int> rejectedServerEntityIds, ref string rejectionReason)
	{
		switch (cmd.CommandType)
		{
			case "train": HandleTrainCommand(peerId, cmd, allUnits, rejectedServerEntityIds, ref rejectionReason); break;
			case "cancel_train": HandleCancelTrainCommand(peerId, cmd, allUnits); break;
		}
	}

	private struct GroupMovementContext
	{
		public System.Numerics.Vector3 MoveDir;
		public System.Numerics.Vector3 Right;
		public int Cols;
		public float Spacing;
	}

	private GroupMovementContext CreateMovementContext(int peerId, NetworkCommand cmd, List<Unit3D> allUnits)
	{
		CalculateGroupMovement(peerId, cmd.UnitEntityIds, allUnits, cmd.TargetPosition, out System.Numerics.Vector3 moveDir, out System.Numerics.Vector3 right);
		return new GroupMovementContext
		{
			MoveDir = moveDir,
			Right = right,
			Cols = Mathf.CeilToInt(Mathf.Sqrt(cmd.UnitEntityIds.Count)),
			Spacing = 2.2f
		};
	}

	private delegate void GroupCommandAction(Entity entity, System.Numerics.Vector3 scattered);

	private void ProcessGroupMovementCommand(int peerId, NetworkCommand cmd, List<Unit3D> allUnits, GroupCommandAction processAction, List<int> rejectedServerEntityIds, ref string rejectionReason, bool requireMovable = true)
	{
		var ctx = CreateMovementContext(peerId, cmd, allUnits);
		int unitIndex = 0;
		string? rejection = null;

		foreach (int serverId in cmd.UnitEntityIds)
		{
			var entity = FindServerEntity(serverId, allUnits);
			if (entity == Entity.Null)
			{
				rejection ??= $"entity {serverId} is not in the host unit list";
				RecordCommandRejection(rejectedServerEntityIds, serverId, rejection);
				continue;
			}

			if (!IsClientAuthorized(peerId, entity))
			{
				rejection ??= $"entity {serverId} is not owned by peer {peerId}";
				RecordCommandRejection(rejectedServerEntityIds, serverId, rejection);
				continue;
			}

			if (requireMovable && !GameContext.World.Has<Movable>(entity))
			{
				rejection ??= $"entity {serverId} is not movable";
				RecordCommandRejection(rejectedServerEntityIds, serverId, rejection);
				continue;
			}

			var scattered = CalculateScatteredPosition(cmd.TargetPosition, ctx.MoveDir, ctx.Right, unitIndex, ctx.Cols, ctx.Spacing);
			processAction(entity, scattered);
			unitIndex++;
		}

		if (rejection != null && string.IsNullOrEmpty(rejectionReason)) rejectionReason = rejection;

		if (unitIndex == 0 && cmd.UnitEntityIds.Count > 0)
		{
			GD.PushWarning($"[Network] Dropped '{cmd.CommandType}' from peer {peerId}: none of its {cmd.UnitEntityIds.Count} units could take the order ({rejection}).");
		}
	}

	private static void RecordCommandRejection(List<int> rejectedServerEntityIds, int serverId, string reason)
	{
		if (!rejectedServerEntityIds.Contains(serverId)) rejectedServerEntityIds.Add(serverId);
		_ = reason;
	}

	private void HandleMoveCommand(int peerId, NetworkCommand cmd, List<Unit3D> allUnits, List<int> rejectedServerEntityIds, ref string rejectionReason)
	{
		ProcessGroupMovementCommand(peerId, cmd, allUnits, (entity, scattered) =>
		{
			ClearUnitOrders(entity);
			var moveTo = new MoveTo(scattered);
			GameContext.World.SetOrAdd(entity, moveTo);
		}, rejectedServerEntityIds, ref rejectionReason, requireMovable: false);
	}

	private void HandleMoveQueuedCommand(int peerId, NetworkCommand cmd, List<Unit3D> allUnits, List<int> rejectedServerEntityIds, ref string rejectionReason)
	{
		ProcessGroupMovementCommand(peerId, cmd, allUnits, (entity, scattered) =>
		{
			ProcessQueuedMoveForEntity(entity, scattered);
		}, rejectedServerEntityIds, ref rejectionReason);
	}

	private void HandleAttackMoveCommand(int peerId, NetworkCommand cmd, List<Unit3D> allUnits, bool isQueued, List<int> rejectedServerEntityIds, ref string rejectionReason)
	{
		ProcessGroupMovementCommand(peerId, cmd, allUnits, (entity, scattered) =>
		{
			if (isQueued && Realm.Client.Utils.UnitActivityHelper.IsUnitActive(entity))
			{
				EnqueueCommand(entity, "attackmove", scattered);
				return;
			}

			ClearUnitOrders(entity);
			GameContext.World.SetOrAdd(entity, new AttackMove(scattered));
			GameContext.World.SetOrAdd(entity, new MoveTo(scattered));
		}, rejectedServerEntityIds, ref rejectionReason);
	}

	private void ProcessQueuedMoveForEntity(Entity entity, System.Numerics.Vector3 scattered)
	{
		bool hasNonMoveTasks = GameContext.World.Has<BuildTask>(entity) ||
		                       GameContext.World.Has<AttackTarget>(entity) ||
		                       GameContext.World.Has<AttackMove>(entity) ||
		                       GameContext.World.Has<Follow>(entity) ||
		                       GameContext.World.Has<Patrol>(entity) ||
		                       GameContext.World.Has<Gatherer>(entity);

		if (hasNonMoveTasks)
		{
			EnqueueCommand(entity, "move", scattered);
			return;
		}

		if (GameContext.World.Has<MoveTo>(entity))
		{
			if (GameContext.World.Has<WaypointQueue>(entity))
			{
				var q = GameContext.World.Get<WaypointQueue>(entity);
				q.Add(scattered);
				GameContext.World.Set(entity, q);
			}
			else
			{
				GameContext.World.Add(entity, new WaypointQueue(scattered));
			}
		}
		else
		{
			ClearUnitOrders(entity);
			GameContext.World.SetOrAdd(entity, new MoveTo(scattered));
		}
	}

	private void HandleTargetedCommand(int peerId, NetworkCommand cmd, List<Unit3D> allUnits, bool isQueued, List<int> rejectedServerEntityIds, ref string rejectionReason, Action<Entity, Entity, System.Numerics.Vector3, bool> applyAction)
	{
		var targetEntity = FindServerEntity(cmd.TargetEntityId, allUnits);
		if (targetEntity == Entity.Null)
		{
			for (int index = 0; index < cmd.UnitEntityIds.Count; index++)
			{
				RecordCommandRejection(rejectedServerEntityIds, cmd.UnitEntityIds[index], "target entity is not on the host");
			}

			if (string.IsNullOrEmpty(rejectionReason)) rejectionReason = $"target entity {cmd.TargetEntityId} is not on the host";
			return;
		}

		var targetPos = GameContext.World.Has<Position>(targetEntity) ? GameContext.World.Get<Position>(targetEntity).Value : System.Numerics.Vector3.Zero;
		foreach (int serverId in cmd.UnitEntityIds)
		{
			var entity = FindServerEntity(serverId, allUnits);
			if (entity == Entity.Null || !IsClientAuthorized(peerId, entity))
			{
				RecordCommandRejection(rejectedServerEntityIds, serverId, $"entity {serverId} is not owned by peer {peerId}");
				if (string.IsNullOrEmpty(rejectionReason)) rejectionReason = $"entity {serverId} is not owned by peer {peerId}";
				continue;
			}

			if (entity == targetEntity) continue;

			applyAction(entity, targetEntity, targetPos, isQueued);
		}
	}

	private void HandleAttackCommand(int peerId, NetworkCommand cmd, List<Unit3D> allUnits, bool isQueued, List<int> rejectedServerEntityIds, ref string rejectionReason)
	{
		HandleTargetedCommand(peerId, cmd, allUnits, isQueued, rejectedServerEntityIds, ref rejectionReason, (entity, targetEntity, targetPos, queued) =>
		{
			if (queued && Realm.Client.Utils.UnitActivityHelper.IsUnitActive(entity))
			{
				EnqueueCommand(entity, "attack", targetPos, targetEntity);
			}
			else
			{
				if (!queued) ClearUnitOrders(entity);
				GameContext.World.SetOrAdd(entity, new AttackTarget(targetEntity));
			}
		});
	}

	private void HandleFollowCommand(int peerId, NetworkCommand cmd, List<Unit3D> allUnits, bool isQueued, List<int> rejectedServerEntityIds, ref string rejectionReason)
	{
		HandleTargetedCommand(peerId, cmd, allUnits, isQueued, rejectedServerEntityIds, ref rejectionReason, ApplyFollowOrder);
	}

	private void ApplyFollowOrder(Entity entity, Entity targetEntity, System.Numerics.Vector3 targetPos, bool isQueued)
	{
			if (isQueued && Realm.Client.Utils.UnitActivityHelper.IsUnitActive(entity))
		{
			EnqueueCommand(entity, "follow", targetPos, targetEntity);
			return;
		}

		if (!isQueued) ClearUnitOrders(entity);

		if (GameContext.World.Has<Movable>(entity))
		{
			GameContext.World.SetOrAdd(entity, new Follow(targetEntity));
		}
	}

	private void HandleGatherCommand(int peerId, NetworkCommand cmd, List<Unit3D> allUnits, List<Prop3D> allProps, bool isQueued, List<int> rejectedServerEntityIds, ref string rejectionReason)
	{
		var targetPos = new System.Numerics.Vector3(cmd.TargetPosition.X, cmd.TargetPosition.Y, cmd.TargetPosition.Z);
		Prop3D prop = FindClosestProp(targetPos, "", allProps);
		if (prop == null)
		{
			for (int index = 0; index < cmd.UnitEntityIds.Count; index++)
			{
				RecordCommandRejection(rejectedServerEntityIds, cmd.UnitEntityIds[index], "no resource node near the target");
			}

			if (string.IsNullOrEmpty(rejectionReason)) rejectionReason = "no resource node near the target";
			return;
		}

		string resType = GetPropResourceType(prop.PropId);
		if (resType == null)
		{
			for (int index = 0; index < cmd.UnitEntityIds.Count; index++)
			{
				RecordCommandRejection(rejectedServerEntityIds, cmd.UnitEntityIds[index], "the resource node is not harvestable");
			}

			if (string.IsNullOrEmpty(rejectionReason)) rejectionReason = "the resource node is not harvestable";
			return;
		}

		var propPos = new System.Numerics.Vector3(prop.GlobalPosition.X, prop.GlobalPosition.Y, prop.GlobalPosition.Z);
		foreach (int serverId in cmd.UnitEntityIds)
		{
			var entity = FindServerEntity(serverId, allUnits);
			if (entity == Entity.Null || !IsClientAuthorized(peerId, entity))
			{
				RecordCommandRejection(rejectedServerEntityIds, serverId, $"entity {serverId} is not owned by peer {peerId}");
				if (string.IsNullOrEmpty(rejectionReason)) rejectionReason = $"entity {serverId} is not owned by peer {peerId}";
				continue;
			}

			if (GameContext.World.Has<DefinitionId>(entity) && GameContext.World.Get<DefinitionId>(entity).Value != "worker")
			{
				RecordCommandRejection(rejectedServerEntityIds, serverId, $"entity {serverId} cannot gather");
				if (string.IsNullOrEmpty(rejectionReason)) rejectionReason = $"entity {serverId} cannot gather";
				continue;
			}

			ApplyGatherOrder(entity, prop.Entity, propPos, resType, isQueued);
		}
	}

	private void ApplyGatherOrder(Entity entity, Entity propEntity, System.Numerics.Vector3 propPos, string resType, bool isQueued)
	{
			if (isQueued && Realm.Client.Utils.UnitActivityHelper.IsUnitActive(entity))
		{
			EnqueueCommand(entity, "gather", propPos, propEntity);
			return;
		}

		if (!isQueued) ClearUnitOrders(entity);
		GameContext.World.SetOrAdd(entity, new Gatherer(resType, propEntity));
		GameContext.World.SetOrAdd(entity, new MoveTo(propPos));
	}

	private string GetPropResourceType(string propId)
	{
		return GameContext.RegistryService.GetResourceTypeForNode(propId);
	}

	private void HandleStopCommand(int peerId, NetworkCommand cmd, List<Unit3D> allUnits, List<int> stopVelocityEntityIds, List<int> rejectedServerEntityIds, ref string rejectionReason)
	{
		foreach (int serverId in cmd.UnitEntityIds)
		{
			var entity = FindServerEntity(serverId, allUnits);
			if (entity == Entity.Null || !IsClientAuthorized(peerId, entity))
			{
				RecordCommandRejection(rejectedServerEntityIds, serverId, $"entity {serverId} is not owned by peer {peerId}");
				if (string.IsNullOrEmpty(rejectionReason)) rejectionReason = $"entity {serverId} is not owned by peer {peerId}";
				continue;
			}

			ClearUnitOrders(entity);
			stopVelocityEntityIds.Add(serverId);
		}
	}

	private void HandleHoldCommand(int peerId, NetworkCommand cmd, List<Unit3D> allUnits, List<int> holdVelocityEntityIds, List<int> rejectedServerEntityIds, ref string rejectionReason)
	{
		foreach (int serverId in cmd.UnitEntityIds)
		{
			var entity = FindServerEntity(serverId, allUnits);
			if (entity == Entity.Null || !IsClientAuthorized(peerId, entity))
			{
				RecordCommandRejection(rejectedServerEntityIds, serverId, $"entity {serverId} is not owned by peer {peerId}");
				if (string.IsNullOrEmpty(rejectionReason)) rejectionReason = $"entity {serverId} is not owned by peer {peerId}";
				continue;
			}

			ClearUnitOrders(entity);
			holdVelocityEntityIds.Add(serverId);
			if (!GameContext.World.Has<HoldPosition>(entity))
			{
				GameContext.World.Add<HoldPosition>(entity);
			}
		}
	}

	private void HandlePatrolCommand(int peerId, NetworkCommand cmd, List<Unit3D> allUnits, bool isQueued, List<int> rejectedServerEntityIds, ref string rejectionReason)
	{
		ProcessGroupMovementCommand(peerId, cmd, allUnits, (entity, scattered) =>
		{
			var unitPos = GetUnitPosition(entity, allUnits);
			var patrolA = new System.Numerics.Vector3(unitPos.X, unitPos.Y, unitPos.Z);

				if (isQueued && Realm.Client.Utils.UnitActivityHelper.IsUnitActive(entity))
			{
				EnqueueCommand(entity, "patrol", scattered);
			}
			else
			{
				if (!isQueued) ClearUnitOrders(entity);
				GameContext.World.SetOrAdd(entity, new Patrol(patrolA, scattered));
				GameContext.World.SetOrAdd(entity, new MoveTo(scattered));
			}
		}, rejectedServerEntityIds, ref rejectionReason);
	}

	private Vector3 GetUnitPosition(Entity entity, List<Unit3D> allUnits)
	{
		foreach (var u in allUnits)
		{
			if (u.Entity == entity) return u.GlobalPosition;
		}

		return Vector3.Zero;
	}

	private void HandleTrainCommand(int peerId, NetworkCommand cmd, List<Unit3D> allUnits, List<int> rejectedServerEntityIds, ref string rejectionReason)
	{
		string unitId = cmd.ArgString;
		if (!GameHost.UnitRegistry.TryGetValue(unitId, out var meta))
		{
			for (int index = 0; index < cmd.UnitEntityIds.Count; index++)
			{
				RecordCommandRejection(rejectedServerEntityIds, cmd.UnitEntityIds[index], $"unit type '{unitId}' is not registered");
			}

			if (string.IsNullOrEmpty(rejectionReason)) rejectionReason = $"unit type '{unitId}' is not registered";
			return;
		}

		foreach (int serverId in cmd.UnitEntityIds)
		{
			var entity = FindServerEntity(serverId, allUnits);
			if (entity == Entity.Null || !IsClientAuthorized(peerId, entity))
			{
				RecordCommandRejection(rejectedServerEntityIds, serverId, $"entity {serverId} is not owned by peer {peerId}");
				if (string.IsNullOrEmpty(rejectionReason)) rejectionReason = $"entity {serverId} is not owned by peer {peerId}";
				continue;
			}

			var ownerComp = GameContext.World.Get<Owner>(entity);
			var ownerEntity = ownerComp.PlayerEntity.Value;
			if (!GameContext.World.TryGet<PlayerResources>(ownerEntity, out var res))
			{
				RecordCommandRejection(rejectedServerEntityIds, serverId, "the owning player has no resource pool");
				if (string.IsNullOrEmpty(rejectionReason)) rejectionReason = "the owning player has no resource pool";
				continue;
			}

			if (!TryQueueProduction(entity, ownerEntity, unitId, meta, ref res))
			{
				RecordCommandRejection(rejectedServerEntityIds, serverId, "not enough resources");
				if (string.IsNullOrEmpty(rejectionReason)) rejectionReason = "not enough resources";
			}
		}
	}

	private bool TryQueueProduction(Entity entity, Entity ownerEntity, string unitId, UnitMetadata meta, ref PlayerResources res)
	{
		var goldResourceId = new ResourceId("Gold");
		var woodResourceId = new ResourceId("Wood");
		var stoneResourceId = new ResourceId("Stone");

		int costGold = (int)meta.CostGold;
		int costWood = (int)meta.CostWood;
		int costStone = (int)meta.CostStone;

		if (res[goldResourceId] < costGold || res[woodResourceId] < costWood || res[stoneResourceId] < costStone)
			return false;

		res[goldResourceId] -= costGold;
		res[woodResourceId] -= costWood;
		res[stoneResourceId] -= costStone;
		GameContext.World.Set(ownerEntity, res);

		if (!GameContext.World.Has<ProductionQueue>(entity)) GameContext.World.Add(entity, new ProductionQueue());

		ref var prod = ref GameContext.World.Get<ProductionQueue>(entity);
		prod.Enqueue(unitId);
		if (prod.Count == 1)
		{
			prod.BuildTime = meta.ProductionTime;
			prod.CurrentProgress = 0f;
		}

		return true;
	}

	private void HandleCancelTrainCommand(int peerId, NetworkCommand cmd, List<Unit3D> allUnits)
	{
		foreach (int serverId in cmd.UnitEntityIds)
		{
			var entity = FindServerEntity(serverId, allUnits);
			if (entity == Entity.Null || !IsClientAuthorized(peerId, entity)) continue;
			if (!GameContext.World.Has<ProductionQueue>(entity)) continue;

			CancelProductionQueueItem(entity, cmd.TargetEntityId);
		}
	}

	private void CancelProductionQueueItem(Entity entity, int idx)
	{
		ref var prod = ref GameContext.World.Get<ProductionQueue>(entity);
		if (idx < 0 || idx >= prod.Count) return;

		string? unitId = prod.RemoveAt(idx);

		if (idx == 0)
		{
			prod.CurrentProgress = 0f;
			string? nextUnitId = prod.Get(0);
			if (nextUnitId != null && GameHost.UnitRegistry.TryGetValue(nextUnitId, out var nextMeta))
			{
				prod.BuildTime = nextMeta.ProductionTime;
			}
		}

		if (unitId != null && GameHost.UnitRegistry.TryGetValue(unitId, out var regMeta))
		{
			RefundCanceledProduction(entity, regMeta);
		}
	}

	private void RefundCanceledProduction(Entity entity, UnitMetadata regMeta)
	{
		var ownerComp = GameContext.World.Get<Owner>(entity);
		var ownerEntity = ownerComp.PlayerEntity.Value;

		if (GameContext.World.TryGet<PlayerResources>(ownerEntity, out var res))
		{
			res[new ResourceId("Gold")] += (int)regMeta.CostGold;
			res[new ResourceId("Wood")] += (int)regMeta.CostWood;
			res[new ResourceId("Stone")] += (int)regMeta.CostStone;
			GameContext.World.Set(ownerEntity, res);
		}

	}

	private void CalculateGroupMovement(int peerId, List<int> unitEntityIds, List<Unit3D> allUnits, NetworkVector3 targetPosition, out System.Numerics.Vector3 moveDir, out System.Numerics.Vector3 right)
	{
		System.Numerics.Vector3 groupCenter = System.Numerics.Vector3.Zero;
		int movableCount = 0;
		foreach (int serverId in unitEntityIds)
		{
			var entity = FindServerEntity(serverId, allUnits);
			if (entity == Entity.Null || !IsClientAuthorized(peerId, entity)) continue;
			if (GameContext.World.Has<Position>(entity))
			{
				groupCenter += GameContext.World.Get<Position>(entity).Value;
				movableCount++;
			}
		}

		if (movableCount > 0) groupCenter /= movableCount;

		moveDir = new System.Numerics.Vector3(targetPosition.X, targetPosition.Y, targetPosition.Z) - groupCenter;
		moveDir.Y = 0f;
		if (moveDir.LengthSquared() > 0.01f)
		{
			moveDir = System.Numerics.Vector3.Normalize(moveDir);
		}
		else
		{
			moveDir = new System.Numerics.Vector3(0f, 0f, -1f);
		}

		right = new System.Numerics.Vector3(-moveDir.Z, 0f, moveDir.X);
	}

	private System.Numerics.Vector3 CalculateScatteredPosition(NetworkVector3 targetPos, System.Numerics.Vector3 moveDir, System.Numerics.Vector3 right, int unitIndex, int cols, float spacing)
	{
		int row = unitIndex / cols;
		int col = unitIndex % cols;
		float offsetX = (col - cols * 0.5f + 0.5f) * spacing;
		float offsetZ = -row * spacing;
		var tPos = new System.Numerics.Vector3(targetPos.X, targetPos.Y, targetPos.Z);
		return tPos + right * offsetX + moveDir * offsetZ;
	}

	public List<(int PeerId, byte[] Payload, bool IsBaseline)> BuildServerSnapshots(int localPeerId, List<Unit3D> allUnits)
	{
		var results = new List<(int PeerId, byte[] Payload, bool IsBaseline)>();
		Entity worldEntity = FindWorldEntity();
		if (worldEntity == Entity.Null || !GameContext.World.Has<NetworkState>(worldEntity) || !GameContext.World.Has<NetworkMappingState>(worldEntity)) return results;

		ref var networkState = ref GameContext.World.Get<NetworkState>(worldEntity);
		networkState.SnapshotSequence++;
		int snapshotSequence = networkState.SnapshotSequence;
		bool isBaseline = (snapshotSequence % 30 == 0);

		if (MultiplayerSyncDiagnostics.IsRecording)
		{
			MultiplayerSyncDiagnostics.RecordHostSnapshot(BuildDiagnosticAuthoritativeSnapshot(allUnits, snapshotSequence, isBaseline));
		}

		if (GameContext.Lobby != null)
		{
			var mapping = GameContext.World.Get<NetworkMappingState>(worldEntity);
			var abilityStates = CollectHostAbilityStateDeltas(isBaseline);
			var staticTexts = CollectHostStaticTextDeltas(isBaseline);
			ProcessLobbyPlayersForSnapshots(localPeerId, allUnits, results, snapshotSequence, isBaseline, mapping, abilityStates, staticTexts);
		}

		return results;
	}

	private void ProcessLobbyPlayersForSnapshots(int localPeerId, List<Unit3D> allUnits, List<(int PeerId, byte[] Payload, bool IsBaseline)> results, int snapshotSequence, bool isBaseline, NetworkMappingState mapping, List<AbilityStateDelta> abilityStates, List<StaticTextDelta> staticTexts)
	{
		foreach (var p in GameContext.Lobby.PlayerList)
		{
			if (p.PeerId == localPeerId || p.PeerId < 0) continue;
			if (!mapping.PeerIdToPlayerEntityMap.TryGetValue(p.PeerId, out var playerEntity)) continue;

			bool hasBaseline = _lastBaselineSnapshotsPerClient.TryGetValue(p.PeerId, out var lastBaselineMap);
			if (!hasBaseline && !isBaseline) continue;

			var payload = BuildClientSnapshotPayload(p.PeerId, playerEntity, allUnits, isBaseline, snapshotSequence, lastBaselineMap, abilityStates, staticTexts);
			results.Add((p.PeerId, payload, isBaseline));
		}
	}

	private byte[] BuildClientSnapshotPayload(int peerId, Entity playerEntity, List<Unit3D> allUnits, bool isBaseline, int snapshotSequence, Dictionary<int, UnitSnapshot> lastBaselineMap, List<AbilityStateDelta> abilityStates, List<StaticTextDelta> staticTexts)
	{
		Vector3 cameraPos = _clientCameraPositions.TryGetValue(peerId, out var cam) ? cam : Vector3.Zero;
		if (!_snapshotUnitsPerClient.TryGetValue(peerId, out var snapshotUnits))
		{
			snapshotUnits = new List<UnitSnapshot>(64);
			_snapshotUnitsPerClient[peerId] = snapshotUnits;
		}

		snapshotUnits.Clear();
		var nextBaselineMap = isBaseline ? new Dictionary<int, UnitSnapshot>() : null;

		foreach (var unit in allUnits)
		{
			if (!GodotObject.IsInstanceValid(unit)) continue;
			if (!IsUnitVisibleToPlayer(playerEntity, unit.Entity, allUnits)) continue;

			var currentSnap = CreateUnitSnapshot(unit, cameraPos, out bool isDetailed);

			if (isBaseline)
			{
				snapshotUnits.Add(currentSnap);
				nextBaselineMap[unit.Entity.Id] = currentSnap;
			}
			else if (HasUnitChanged(unit, ref currentSnap, isDetailed, lastBaselineMap))
			{
				snapshotUnits.Add(currentSnap);
			}
		}

		if (isBaseline)
		{
			_lastBaselineSnapshotsPerClient[peerId] = nextBaselineMap;
		}

		var coldPayloadCandidates = BuildColdPayloadCandidates(snapshotSequence);
		var coldPayloads = BuildColdPayloadDeltas(peerId, coldPayloadCandidates, isBaseline);
		var componentDeltas = BuildComponentDeltas(peerId, playerEntity, allUnits, isBaseline);
		var catalog = isBaseline ? BuildCatalogEntriesWithStringTable(peerId) : BuildStringTableEntries(peerId, false);
		uint stateFingerprint = isBaseline ? ComputeHostStateFingerprint(abilityStates, staticTexts, coldPayloadCandidates) : 0u;

		var worldSnapshot = new WorldSnapshot
		{
			Sequence = snapshotSequence,
			IsBaseline = isBaseline,
			BaseSequence = isBaseline ? snapshotSequence : (snapshotSequence / 30) * 30,
			Units = snapshotUnits,
			ColdPayloads = coldPayloads,
			ComponentDeltas = componentDeltas,
			AbilityStates = abilityStates,
			StaticTexts = staticTexts,
			Catalog = catalog,
			StateFingerprint = stateFingerprint
		};
		return MemoryPackSerializer.Serialize(worldSnapshot);
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

	private List<ColdPayloadDelta> BuildColdPayloadDeltas(int peerId, List<ColdPayloadDelta> candidates, bool isBaseline)
	{
		if (!isBaseline) return _emptyColdPayloadDeltas;

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

			if (!isBaseline && previousContentIds.TryGetValue(key, out int previousContentId) && previousContentId == candidate.ContentId) continue;

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

	private List<ComponentDelta> BuildComponentDeltas(int peerId, Entity viewerEntity, List<Unit3D> allUnits, bool isBaseline)
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
		if (!_lastComponentSnapshotsPerClient.TryGetValue(peerId, out var previous) || isBaseline)
		{
			previous = new Dictionary<(byte Scope, int Key, int TypeId), byte[]>();
			_lastComponentSnapshotsPerClient[peerId] = previous;
		}

		var codecs = ReplicatedComponents.All;
		for (int codecIndex = 0; codecIndex < codecs.Count; codecIndex++)
		{
			var codec = codecs[codecIndex];
			if (codec.Scope != ReplicatedScope.World) continue;

			TryAppendComponentDelta(worldEntity, codec, (byte)ReplicatedScope.World, 0, previous, deltas, isBaseline);
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

				TryAppendComponentDelta(playerEntity, codec, (byte)ReplicatedScope.Player, slot, previous, deltas, isBaseline);
			}
		}

		for (int unitIndex = 0; unitIndex < allUnits.Count; unitIndex++)
		{
			var unit = allUnits[unitIndex];
			if (!GodotObject.IsInstanceValid(unit)) continue;
			if (!IsUnitVisibleToPlayer(viewerEntity, unit.Entity, allUnits)) continue;

			for (int codecIndex = 0; codecIndex < codecs.Count; codecIndex++)
			{
				var codec = codecs[codecIndex];
				if (codec.Scope != ReplicatedScope.Unit) continue;

				TryAppendComponentDelta(unit.Entity, codec, (byte)ReplicatedScope.Unit, unit.Entity.Id, previous, deltas, isBaseline);
			}
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

				TryAppendComponentDelta(loot.Entity, lootCodec, (byte)ReplicatedScope.Unit, loot.Entity.Id, previous, deltas, isBaseline);
			}
		}

		return deltas;
	}

	private void TryAppendComponentDelta(Entity entity, IReplicatedComponentCodec codec, byte scope, int key, Dictionary<(byte Scope, int Key, int TypeId), byte[]> previous, List<ComponentDelta> deltas, bool isBaseline)
	{
		if (_componentWriteScratch.Length < codec.SizeInBytes) _componentWriteScratch = new byte[codec.SizeInBytes];

		var scratch = _componentWriteScratch.AsSpan(0, codec.SizeInBytes);
		var deltaKey = (scope, key, codec.ComponentTypeId);
		if (!codec.TryWrite(GameContext.World, entity, scratch))
		{
			if (!previous.Remove(deltaKey)) return;

			deltas.Add(new ComponentDelta
			{
				Key = key,
				ComponentTypeId = codec.ComponentTypeId,
				Scope = scope,
				Bytes = Array.Empty<byte>()
			});
			return;
		}

		if (!previous.TryGetValue(deltaKey, out var storedBytes))
		{
			storedBytes = new byte[codec.SizeInBytes];
			previous[deltaKey] = storedBytes;
		}
		else if (!isBaseline && storedBytes.AsSpan().SequenceEqual(scratch))
		{
			return;
		}

		scratch.CopyTo(storedBytes);
		deltas.Add(new ComponentDelta
		{
			Key = key,
			ComponentTypeId = codec.ComponentTypeId,
			Scope = scope,
			Bytes = storedBytes
		});
	}

	private List<AbilityStateDelta> CollectHostAbilityStateDeltas(bool includeAll, bool clearDirty = true)
	{
		var host = GameContext.Host;
		if (host == null) return new List<AbilityStateDelta>();
		var deltas = host.CollectAbilityStateDeltas(includeAll, clearDirty);
		deltas.Sort(static (left, right) =>
		{
			int byEntity = left.ServerEntityId.CompareTo(right.ServerEntityId);
			return byEntity != 0 ? byEntity : string.CompareOrdinal(left.AbilityId, right.AbilityId);
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
			hash = ReplicatedStateFingerprint.Combine(hash, ability.AbilityId);
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

	private WorldSnapshot BuildDiagnosticAuthoritativeSnapshot(List<Unit3D> allUnits, int snapshotSequence, bool isBaseline)
	{
		var snapshotUnits = new List<UnitSnapshot>(allUnits.Count);
		foreach (var unit in allUnits)
		{
			if (!GodotObject.IsInstanceValid(unit)) continue;
			snapshotUnits.Add(CreateUnitSnapshot(unit, Vector3.Zero, out _));
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
		isDetailed = distToCamera <= 35.0f;
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

	private bool HasUnitChanged(Unit3D unit, ref UnitSnapshot currentSnap, bool isDetailed, Dictionary<int, UnitSnapshot> lastBaselineMap)
	{
		if (!lastBaselineMap.TryGetValue(unit.Entity.Id, out var baseSnap)) return true;

		if (isDetailed)
		{
			bool posChanged = baseSnap.Position.ToGodot().DistanceTo(currentSnap.Position.ToGodot()) > 0.05f;
			bool rotChanged = Mathf.Abs(baseSnap.RotationY - currentSnap.RotationY) > 0.05f;
			bool hpChanged = Mathf.Abs(baseSnap.CurrentHp - currentSnap.CurrentHp) > 0.1f;
			bool deadChanged = baseSnap.IsDead != currentSnap.IsDead;
			return posChanged || rotChanged || hpChanged || deadChanged;
		}

		bool posFarChanged = baseSnap.Position.ToGodot().DistanceTo(currentSnap.Position.ToGodot()) > 1.0f;
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
		var nextBaselineMap = new Dictionary<int, UnitSnapshot>();

		foreach (var unit in allUnits)
		{
			ProcessUnitForBaselineSnapshot(unit, playerEntity, cameraPos, allUnits, snapshotUnits, nextBaselineMap);
		}

		_lastBaselineSnapshotsPerClient[targetPeerId] = nextBaselineMap;

		var abilityStates = CollectHostAbilityStateDeltas(true, clearDirty: false);
		var staticTexts = CollectHostStaticTextDeltas(true, clearDirty: false);
		var coldPayloadCandidates = BuildColdPayloadCandidates(networkState.SnapshotSequence);

		var worldSnapshot = new WorldSnapshot
		{
			Sequence = networkState.SnapshotSequence,
			IsBaseline = true,
			BaseSequence = networkState.SnapshotSequence,
			Units = snapshotUnits,
			ColdPayloads = BuildColdPayloadDeltas(targetPeerId, coldPayloadCandidates, true),
			ComponentDeltas = BuildComponentDeltas(targetPeerId, playerEntity, allUnits, true),
			AbilityStates = abilityStates,
			StaticTexts = staticTexts,
			Catalog = BuildCatalogEntriesWithStringTable(targetPeerId),
			StateFingerprint = ComputeHostStateFingerprint(abilityStates, staticTexts, coldPayloadCandidates)
		};
		return MemoryPackSerializer.Serialize(worldSnapshot);
	}

	private void ProcessUnitForBaselineSnapshot(Unit3D unit, Entity playerEntity, Vector3 cameraPos, List<Unit3D> allUnits, List<UnitSnapshot> snapshotUnits, Dictionary<int, UnitSnapshot> nextBaselineMap)
	{
		if (!GodotObject.IsInstanceValid(unit)) return;
		if (!IsUnitVisibleToPlayer(playerEntity, unit.Entity, allUnits)) return;

		var currentSnap = CreateUnitSnapshot(unit, cameraPos, out _);
		snapshotUnits.Add(currentSnap);
		nextBaselineMap[unit.Entity.Id] = currentSnap;
	}

	public void QueueSpectatorDelayedPacket(int peerId, string functionName, object[] arguments)
	{
		double sendTime = (Time.GetTicksMsec() / 1000.0) + 300.0;
		if (!_spectatorDelayedPackets.TryGetValue(peerId, out var list))
		{
			list = new List<DelayedPacket>();
			_spectatorDelayedPackets[peerId] = list;
		}

		list.Add(new DelayedPacket
		{
			FunctionName = functionName,
			Arguments = arguments,
			SendTime = sendTime
		});
	}


	public List<ReadyPacket> FlushReadySpectatorPackets()
	{
		if (_spectatorDelayedPackets.Count == 0) return new List<ReadyPacket>();

		double currentTime = Time.GetTicksMsec() / 1000.0;
		var disconnectedPeers = new List<int>();
		foreach (var key in _spectatorDelayedPackets.Keys)
		{
			if (GameContext.Lobby == null || !GameContext.Lobby.PlayerList.Exists(p => p.PeerId == key))
			{
				disconnectedPeers.Add(key);
			}
		}

		foreach (var peerId in disconnectedPeers)
		{
			_spectatorDelayedPackets.Remove(peerId);
		}

		var readyPackets = new List<ReadyPacket>();
		foreach (var kvp in _spectatorDelayedPackets)
		{
			int peerId = kvp.Key;
			var list = kvp.Value;
			int sentCount = 0;
			for (int i = 0; i < list.Count; i++)
			{
				if (list[i].SendTime <= currentTime)
				{
					readyPackets.Add(new ReadyPacket(peerId, list[i].FunctionName, list[i].Arguments));
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

		return readyPackets;
	}

	public bool IsSpectatorWithDelay(int peerId)
	{
		if (GameContext.Lobby == null) return false;
		var p = GameContext.Lobby.PlayerList.Find(x => x.PeerId == peerId);
		return p != null && p.Team == "Spectator" && GameContext.Lobby.SpectatorDelay;
	}

	private void ClearUnitOrders(Entity entity)
	{
		ClearMovementOrders(entity);
		ClearActionOrders(entity);
	}

	private void ClearMovementOrders(Entity entity)
	{
		if (GameContext.World.Has<MoveTo>(entity)) GameContext.World.Remove<MoveTo>(entity);
		if (GameContext.World.Has<PathFollow>(entity)) GameContext.World.Remove<PathFollow>(entity);
		if (GameContext.World.Has<HoldPosition>(entity)) GameContext.World.Remove<HoldPosition>(entity);
		if (GameContext.World.Has<WaypointQueue>(entity)) GameContext.World.Remove<WaypointQueue>(entity);
		if (GameContext.World.Has<Follow>(entity)) GameContext.World.Remove<Follow>(entity);
	}

	private void ClearActionOrders(Entity entity)
	{
		if (GameContext.World.Has<AttackTarget>(entity)) GameContext.World.Remove<AttackTarget>(entity);
		if (GameContext.World.Has<AttackMove>(entity)) GameContext.World.Remove<AttackMove>(entity);
		if (GameContext.World.Has<Patrol>(entity)) GameContext.World.Remove<Patrol>(entity);
		if (GameContext.World.Has<HealingTarget>(entity)) GameContext.World.Remove<HealingTarget>(entity);
		if (GameContext.World.Has<Gatherer>(entity)) GameContext.World.Remove<Gatherer>(entity);
	}

	private Entity FindWorldEntity()
	{
		Entity worldEntity = Entity.Null;
		var query = QueryCache.AllNetworkStateQuery;
		GameContext.World.Query(in query, (Entity entity) => worldEntity = entity);
		return worldEntity;
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
		_unacknowledgedCommands.Clear();
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

	public void BroadcastMessageDomainLogic(string message, bool isMultiplayerActive, bool isServerActive)
	{
		string formatted = $"[HOST BROADCAST] {message}";
		WasmRuntime.LogToConsole(formatted);
		if (isMultiplayerActive && isServerActive)
		{
			GameContext.Lobby?.SendChatMessage("System", message, false);
		}
	}

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

		var mappingEntity = ECSContext.WorldEntity;
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
		if (GameContext.World.TryGet<NetworkMappingState>(ECSContext.WorldEntity, out var mapState))
		{
			GameContext.World.Mutate<NetworkMappingState>(ECSContext.WorldEntity, (ref NetworkMappingState state) =>
			{
				if (state.ClientToServerEntityMap.TryGetValue(entityId, out int serverId))
				{
					state.ServerToClientEntityMap.Remove(serverId);
				}

				state.ClientToServerEntityMap.Remove(entityId);
			});
		}
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