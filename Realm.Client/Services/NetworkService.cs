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
using Realm.Ecs.Services;
using Realm.Client.UI;
using HttpClient = System.Net.Http.HttpClient;

namespace Realm.Client.Services;

public class NetworkService
{
	private readonly List<NetworkCommand> _unacknowledgedCommands = new();
	private readonly List<WorldSnapshot> _queuedDeltas = new();
	private readonly Dictionary<int, Vector3> _clientCameraPositions = new();
	private readonly Dictionary<int, Dictionary<int, UnitSnapshot>> _lastBaselineSnapshotsPerClient = new();
	private readonly Dictionary<int, List<DelayedPacket>> _spectatorDelayedPackets = new();

	private readonly List<UnitSnapshot> _pendingUnitSpawns = new();
	private readonly List<Entity> _pendingUnitKills = new();


	public bool IsPaused { get; set; } = false;
	public int ResumeCountdownSeconds { get; set; } = -1;
	public float ResumeCountdownTimer { get; set; } = 0f;

	public NetworkService()
	{
	}

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

	public bool TryGetReconnectedWorldState(out float gameElapsedTime, out int timeOfDayIndex, out float timeOfDayTimer, out bool dayNightCycleEnabled)
	{
		gameElapsedTime = 0f;
		timeOfDayIndex = 0;
		timeOfDayTimer = 0f;
		dayNightCycleEnabled = false;

		if (ECSContext.WorldEntity == Entity.Null || GameContext.World == null || !GameContext.World.Has<WorldState>(ECSContext.WorldEntity)) return false;

		var ws = GameContext.World.Get<WorldState>(ECSContext.WorldEntity);
		gameElapsedTime = ws.GameElapsedTime;
		timeOfDayIndex = ws.TimeOfDayIndex;
		timeOfDayTimer = ws.TimeOfDayTimer;
		dayNightCycleEnabled = ws.DayNightCycleEnabled;
		return true;
	}

	public bool TryGetReconnectedPlayerResources(Entity playerEntity, out float gold, out float wood, out float stone)
	{
		gold = 0;
		wood = 0;
		stone = 0;

		if (playerEntity == Entity.Null || GameContext.World == null || !GameContext.World.IsAlive(playerEntity) || !GameContext.World.TryGet<PlayerResources>(playerEntity, out var res)) return false;

		gold = res.Gold;
		wood = res.Wood;
		stone = res.Stone;
		return true;
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
		_playerTeams.Clear();
		_alliances.Clear();
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

	private bool IsUnitActive(Entity entity)
	{
		return GameContext.World.Has<MoveTo>(entity) ||
		       GameContext.World.Has<BuildTask>(entity) ||
		       GameContext.World.Has<AttackTarget>(entity) ||
		       GameContext.World.Has<AttackMove>(entity) ||
		       GameContext.World.Has<Follow>(entity) ||
		       GameContext.World.Has<Patrol>(entity) ||
		       GameContext.World.Has<Gatherer>(entity) ||
		       GameContext.World.Has<WaypointQueue>(entity) ||
		       (GameContext.World.Has<BuildQueue>(entity) && GameContext.World.Get<BuildQueue>(entity).Count > 0);
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

	public int GetOwnerPeerId(Entity unitEntity)
	{
		Entity worldEntity = FindWorldEntity();
		if (worldEntity == Entity.Null || !GameContext.World.Has<NetworkMappingState>(worldEntity)) return -1;
		var mapping = GameContext.World.Get<NetworkMappingState>(worldEntity);

		if (GameContext.World.Has<UnitOwnerPlayer>(unitEntity))
		{
			int playerIndex = GameContext.World.Get<UnitOwnerPlayer>(unitEntity).PlayerIndex;
			var lobby = LobbyManager.Instance;
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

	public (bool Success, string BuildUnitType, string TargetModel, PlayerEntity PlayerOwner, System.Numerics.Vector3 BuildPosition, string Name, float MaxHp, float Damage, float Range, float Armor) ProcessServerCommandBuildDomain(
		ServerCommandResult result,
		Dictionary<string, UnitMetadata> unitRegistry,
		Dictionary<long, Entity> peerIdToPlayerEntityMap)
	{
		if (!result.NeedsBuildUnit || !unitRegistry.TryGetValue(result.BuildUnitType, out var meta)) return (false, "", "", default, default, "", 0, 0, 0, 0);

		var playerOwner = peerIdToPlayerEntityMap[result.BuildPeerOwner].AsPlayerEntity(GameContext.World);
		string targetModel = !string.IsNullOrEmpty(meta.ModelPath) ? meta.ModelPath : result.BuildUnitType;

		return (true, result.BuildUnitType, targetModel, playerOwner, new System.Numerics.Vector3(result.BuildPosition.X, result.BuildPosition.Y, result.BuildPosition.Z), meta.Name, meta.MaxHp, meta.Damage, meta.Range, meta.Armor);
	}

	public void UpdateClientTickDomain()
	{
		
		GameContext.World?.Mutate<NetworkState>(ECSContext.WorldEntity, (ref NetworkState netState) =>
			netState.DynamicInterpolationFactor = ComputeDynamicInterpolationFactor());
	}

	public float ComputeDynamicInterpolationFactor()
	{
		float bufferTime = 0.1f;
		if (LobbyManager.Instance?.PlayerList != null)
		{
			var localPlayer = LobbyManager.Instance.PlayerList.Find(x => x.PeerId == LocalPeerId);
			if (localPlayer != null)
			{
				string latStr = localPlayer.Latency;
				if (!string.IsNullOrEmpty(latStr) && latStr != "--" && latStr.Contains(" ms"))
				{
					string numStr = latStr.Replace(" ms", "").Trim();
					if (float.TryParse(numStr, out float rttMs))
					{
						bufferTime = Mathf.Max(0.1f, (rttMs / 1000f) * 1.5f);
					}
				}
			}
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
		GD.Print($"[CLIENT_CMD_SENT] CommandType={commandType} Units={string.Join(",", unitIds)} Target={targetPos}");
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
		Entity worldEntity = FindWorldEntity();
		if (worldEntity == Entity.Null || !GameContext.World.Has<NetworkState>(worldEntity)) return;

		ref var networkState = ref GameContext.World.Get<NetworkState>(worldEntity);
		if (snapshot.Sequence <= networkState.LastAppliedSnapshotSequence) return;
		networkState.LastAppliedSnapshotSequence = snapshot.Sequence;

		if (snapshot.IsBaseline)
		{
			networkState.LastReceivedBaselineSeq = snapshot.Sequence;
			networkState.HasReceivedInitialBaseline = true;
			_queuedDeltas.RemoveAll(d => d.Sequence <= snapshot.Sequence);
			ApplyWorldSnapshot(snapshot);
			while (_queuedDeltas.Count > 0 && _queuedDeltas[0].BaseSequence == snapshot.Sequence)
			{
				var nextDelta = _queuedDeltas[0];
				_queuedDeltas.RemoveAt(0);
				ApplyWorldSnapshot(nextDelta);
			}
		}
		else
		{
			if (!networkState.HasReceivedInitialBaseline || snapshot.BaseSequence != networkState.LastReceivedBaselineSeq)
			{
				_queuedDeltas.Add(snapshot);
				_queuedDeltas.Sort((a, b) => a.Sequence.CompareTo(b.Sequence));
			}
			else
			{
				ApplyWorldSnapshot(snapshot);
			}
		}
	}

	private void ApplyWorldSnapshot(WorldSnapshot snapshot)
	{
		Entity worldEntity = FindWorldEntity();
		if (worldEntity == Entity.Null || !GameContext.World.Has<NetworkMappingState>(worldEntity)) return;
		var mapping = GameContext.World.Get<NetworkMappingState>(worldEntity);

		if (snapshot.IsBaseline)
		{
			ProcessBaselineSnapshotMismatches(snapshot, mapping);
		}

		if (GameSettings.MovementPrediction && GameContext.World != null)
		{
			var serverPositions = new Dictionary<int, System.Numerics.Vector3>();
			foreach (var snap in snapshot.Units)
			{
				if (mapping.ServerToClientEntityMap.TryGetValue(snap.EntityId, out var localEntity))
				{
					serverPositions[localEntity.Id] = snap.Position.ToNumerics();
				}
			}

			GameContext.MovementPredictionService?.ReconcileServerSnapshot(GameContext.World, snapshot.Sequence, serverPositions);
		}

		foreach (var snap in snapshot.Units)
		{
			ApplySnapshotToUnit(snap, snapshot.Sequence, mapping);
		}
	}

	private void ProcessBaselineSnapshotMismatches(WorldSnapshot snapshot, NetworkMappingState mapping)
	{
		var serverIdsInSnapshot = new HashSet<int>(snapshot.Units.Count);
		for (int i = 0; i < snapshot.Units.Count; i++)
		{
			serverIdsInSnapshot.Add(snapshot.Units[i].EntityId);
		}

		foreach (var kvp in mapping.ServerToClientEntityMap)
		{
			if (serverIdsInSnapshot.Contains(kvp.Key)) continue;

			var localEnt = kvp.Value;
			if (!GameContext.World.IsAlive(localEnt) || GameContext.World.Has<Dead>(localEnt)) continue;

			GameContext.World.Add<Dead>(localEnt);
			_pendingUnitKills.Add(localEnt);
		}
	}

	private void ApplySnapshotToUnit(UnitSnapshot snap, int sequence, NetworkMappingState mapping)
	{
		if (!mapping.ServerToClientEntityMap.TryGetValue(snap.EntityId, out var localEntity))
		{
			if (!snap.IsDead) _pendingUnitSpawns.Add(snap);
			return;
		}

		if (!GameContext.World.IsAlive(localEntity)) return;

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
		GD.Print($"[CLIENT_SNAPSHOT_APPLIED] Sequence={sequence} Unit={snap.EntityId} ServerPos={snap.Position.ToGodot()}");
	}

	public IReadOnlyList<UnitSnapshot> FlushPendingUnitSpawns()
	{
		var result = new List<UnitSnapshot>(_pendingUnitSpawns);
		_pendingUnitSpawns.Clear();
		return result;
	}

	public IReadOnlyList<Entity> FlushPendingUnitKills()
	{
		var result = new List<Entity>(_pendingUnitKills);
		_pendingUnitKills.Clear();
		return result;
	}

	public Entity SpawnUnitFromSnapshot(UnitSnapshot snap, Func<string, bool, string> getFallbackModelPath, out string modelPath, out int playerIndex)
	{
		modelPath = string.Empty;
		playerIndex = 0;

		if (!GameHost.UnitRegistry.TryGetValue(snap.UnitId, out var meta))
		{
			return Entity.Null;
		}

		Entity worldEntity = FindWorldEntity();
		if (worldEntity == Entity.Null || !GameContext.World.Has<NetworkMappingState>(worldEntity)) return Entity.Null;

		ref var mapping = ref GameContext.World.Get<NetworkMappingState>(worldEntity);

		Entity ownerPlayerEntity = mapping.PlayerEntities != null && mapping.PlayerEntities.Length > 1 ? mapping.PlayerEntities[1] : Entity.Null;
		if (mapping.PeerIdToPlayerEntityMap.TryGetValue(snap.OwnerPlayerEntityId, out var pe))
		{
			ownerPlayerEntity = pe;
		}

		playerIndex = (ownerPlayerEntity != Entity.Null && GameContext.World.Has<UnitOwnerPlayer>(ownerPlayerEntity))
			? GameContext.World.Get<UnitOwnerPlayer>(ownerPlayerEntity).PlayerIndex
			: (snap.OwnerPlayerEntityId > 0 ? snap.OwnerPlayerEntityId - 1 : 0);

		var entity = GameContext.World.Create();
		GameContext.World.Add(entity, new DefinitionId(snap.UnitId));
		GameContext.World.Add(entity, new Name(meta.Name));
		GameContext.World.Add(entity, new Position(snap.Position.ToNumerics()));
		GameContext.World.Add(entity, new Owner(ownerPlayerEntity.AsPlayerEntity(GameContext.World)));
		GameContext.World.Add(entity, new UnitOwnerPlayer(playerIndex));
		GameContext.World.Add(entity, new Health(snap.CurrentHp, snap.MaxHp));
		if (meta.Damage > 0)
		{
			GameContext.World.Add(entity, new Attack(meta.Damage, meta.Range, meta.AttackCooldown));
		}

		GameContext.World.Add(entity, new Armor(meta.Armor));
		if (meta.Speed > 0)
		{
			GameContext.World.Add(entity, new MovementStats(meta.Speed, 20f, 10f));
			GameContext.World.Add(entity, new Movable());
			GameContext.World.Add(entity, new Inventory());
		}
		else
		{
			GameContext.World.Add(entity, new Building());
		}

		var interpolationTarget = new InterpolationTarget
		{
			Position = snap.Position.ToNumerics(),
			Velocity = snap.Velocity.ToNumerics(),
			RotationY = snap.RotationY
		};
		GameContext.World.Add(entity, interpolationTarget);

		mapping.ServerToClientEntityMap[snap.EntityId] = entity;
		mapping.ClientToServerEntityMap[entity.Id] = snap.EntityId;

		string targetModel = !string.IsNullOrEmpty(meta.ModelPath) ? meta.ModelPath : snap.UnitId;
		modelPath = getFallbackModelPath(targetModel, snap.IsBuilding);
		return entity;
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

		DispatchServerCommand(peerId, cmd, allUnits, allProps, stopVelocityEntityIds, holdVelocityEntityIds);

		bool needsBuildUnit = cmd.CommandType == "build";
		string buildUnitType = needsBuildUnit ? cmd.ArgString : null;
		Vector3 buildPosition = needsBuildUnit ? cmd.TargetPosition.ToGodot() : Vector3.Zero;
		int buildPeerOwner = needsBuildUnit ? peerId : -1;

		bool needsSpellEffect = cmd.CommandType == "spell";
		string spellId = needsSpellEffect ? cmd.ArgString : null;
		Vector3 spellPosition = needsSpellEffect ? cmd.TargetPosition.ToGodot() : Vector3.Zero;

		return new ServerCommandResult(
			needsBuildUnit, buildUnitType, buildPosition, buildPeerOwner,
			needsSpellEffect, spellId, spellPosition,
			stopVelocityEntityIds, holdVelocityEntityIds);
	}

	private void DispatchServerCommand(int peerId, NetworkCommand cmd, List<Unit3D> allUnits, List<Prop3D> allProps, List<int> stopVelocityEntityIds, List<int> holdVelocityEntityIds)
	{
		if (DispatchMovementCommand(peerId, cmd, allUnits, stopVelocityEntityIds, holdVelocityEntityIds)) return;
		if (DispatchActionCommand(peerId, cmd, allUnits, allProps)) return;
		DispatchTrainingCommand(peerId, cmd, allUnits);
	}

	private bool DispatchMovementCommand(int peerId, NetworkCommand cmd, List<Unit3D> allUnits, List<int> stopVelocityEntityIds, List<int> holdVelocityEntityIds)
	{
		switch (cmd.CommandType)
		{
			case "move":
				HandleMoveCommand(peerId, cmd, allUnits);
				return true;
			case "move_queued":
				HandleMoveQueuedCommand(peerId, cmd, allUnits);
				return true;
			case "stop":
				HandleStopCommand(peerId, cmd, allUnits, stopVelocityEntityIds);
				return true;
			case "hold":
				HandleHoldCommand(peerId, cmd, allUnits, holdVelocityEntityIds);
				return true;
			case "patrol":
			case "patrol_queued":
				HandlePatrolCommand(peerId, cmd, allUnits, cmd.CommandType == "patrol_queued");
				return true;
		}

		return false;
	}

	private bool DispatchActionCommand(int peerId, NetworkCommand cmd, List<Unit3D> allUnits, List<Prop3D> allProps)
	{
		bool isQueued = cmd.CommandType.EndsWith("_queued");
		switch (cmd.CommandType)
		{
			case "attack":
			case "attack_queued":
				HandleAttackCommand(peerId, cmd, allUnits, isQueued);
				return true;
			case "follow":
			case "follow_queued":
				HandleFollowCommand(peerId, cmd, allUnits, isQueued);
				return true;
			case "gather":
			case "gather_queued":
				HandleGatherCommand(peerId, cmd, allUnits, allProps, isQueued);
				return true;
		}

		return false;
	}

	private void DispatchTrainingCommand(int peerId, NetworkCommand cmd, List<Unit3D> allUnits)
	{
		switch (cmd.CommandType)
		{
			case "train": HandleTrainCommand(peerId, cmd, allUnits); break;
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

	private void HandleMoveCommand(int peerId, NetworkCommand cmd, List<Unit3D> allUnits)
	{
		var ctx = CreateMovementContext(peerId, cmd, allUnits);
		int unitIndex = 0;

		foreach (int serverId in cmd.UnitEntityIds)
		{
			var entity = FindServerEntity(serverId, allUnits);
			if (entity == Entity.Null || !IsClientAuthorized(peerId, entity)) continue;

			ClearUnitOrders(entity);
			var scattered = CalculateScatteredPosition(cmd.TargetPosition, ctx.MoveDir, ctx.Right, unitIndex, ctx.Cols, ctx.Spacing);
			var moveTo = new MoveTo(scattered);
			GameContext.World.SetOrAdd(entity, moveTo);
			unitIndex++;
		}
	}

	private void HandleMoveQueuedCommand(int peerId, NetworkCommand cmd, List<Unit3D> allUnits)
	{
		var ctx = CreateMovementContext(peerId, cmd, allUnits);
		int unitIndex = 0;

		foreach (int serverId in cmd.UnitEntityIds)
		{
			var entity = FindServerEntity(serverId, allUnits);
			if (entity == Entity.Null || !IsClientAuthorized(peerId, entity)) continue;
			if (!GameContext.World.Has<Movable>(entity)) continue;

			var scattered = CalculateScatteredPosition(cmd.TargetPosition, ctx.MoveDir, ctx.Right, unitIndex, ctx.Cols, ctx.Spacing);
			ProcessQueuedMoveForEntity(entity, scattered);
			unitIndex++;
		}
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

	private void HandleAttackCommand(int peerId, NetworkCommand cmd, List<Unit3D> allUnits, bool isQueued)
	{
		var targetEntity = FindServerEntity(cmd.TargetEntityId, allUnits);
		if (targetEntity == Entity.Null) return;

		var targetPos = GameContext.World.Has<Position>(targetEntity) ? GameContext.World.Get<Position>(targetEntity).Value : System.Numerics.Vector3.Zero;
		foreach (int serverId in cmd.UnitEntityIds)
		{
			var entity = FindServerEntity(serverId, allUnits);
			if (entity == Entity.Null || !IsClientAuthorized(peerId, entity)) continue;

			if (isQueued && IsUnitActive(entity))
			{
				EnqueueCommand(entity, "attack", targetPos, targetEntity);
			}
			else
			{
				if (!isQueued) ClearUnitOrders(entity);
				GameContext.World.SetOrAdd(entity, new AttackTarget(targetEntity));
			}
		}
	}

	private void HandleFollowCommand(int peerId, NetworkCommand cmd, List<Unit3D> allUnits, bool isQueued)
	{
		var targetEntity = FindServerEntity(cmd.TargetEntityId, allUnits);
		if (targetEntity == Entity.Null) return;

		var targetPos = GameContext.World.Has<Position>(targetEntity) ? GameContext.World.Get<Position>(targetEntity).Value : System.Numerics.Vector3.Zero;
		foreach (int serverId in cmd.UnitEntityIds)
		{
			var entity = FindServerEntity(serverId, allUnits);
			if (entity == Entity.Null || !IsClientAuthorized(peerId, entity) || entity == targetEntity) continue;

			ApplyFollowOrder(entity, targetEntity, targetPos, isQueued);
		}
	}

	private void ApplyFollowOrder(Entity entity, Entity targetEntity, System.Numerics.Vector3 targetPos, bool isQueued)
	{
		if (isQueued && IsUnitActive(entity))
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

	private void HandleGatherCommand(int peerId, NetworkCommand cmd, List<Unit3D> allUnits, List<Prop3D> allProps, bool isQueued)
	{
		var targetPos = new System.Numerics.Vector3(cmd.TargetPosition.X, cmd.TargetPosition.Y, cmd.TargetPosition.Z);
		Prop3D prop = FindClosestProp(targetPos, "", allProps);
		if (prop == null) return;

		string resType = GetPropResourceType(prop.PropId);
		if (resType == null) return;

		var propPos = new System.Numerics.Vector3(prop.GlobalPosition.X, prop.GlobalPosition.Y, prop.GlobalPosition.Z);
		foreach (int serverId in cmd.UnitEntityIds)
		{
			var entity = FindServerEntity(serverId, allUnits);
			if (entity == Entity.Null || !IsClientAuthorized(peerId, entity)) continue;
			if (GameContext.World.Has<DefinitionId>(entity) && GameContext.World.Get<DefinitionId>(entity).Value != "worker") continue;

			ApplyGatherOrder(entity, prop.Entity, propPos, resType, isQueued);
		}
	}

	private void ApplyGatherOrder(Entity entity, Entity propEntity, System.Numerics.Vector3 propPos, string resType, bool isQueued)
	{
		if (isQueued && IsUnitActive(entity))
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

	private void HandleStopCommand(int peerId, NetworkCommand cmd, List<Unit3D> allUnits, List<int> stopVelocityEntityIds)
	{
		foreach (int serverId in cmd.UnitEntityIds)
		{
			var entity = FindServerEntity(serverId, allUnits);
			if (entity == Entity.Null || !IsClientAuthorized(peerId, entity)) continue;
			ClearUnitOrders(entity);
			stopVelocityEntityIds.Add(serverId);
		}
	}

	private void HandleHoldCommand(int peerId, NetworkCommand cmd, List<Unit3D> allUnits, List<int> holdVelocityEntityIds)
	{
		foreach (int serverId in cmd.UnitEntityIds)
		{
			var entity = FindServerEntity(serverId, allUnits);
			if (entity == Entity.Null || !IsClientAuthorized(peerId, entity)) continue;
			ClearUnitOrders(entity);
			holdVelocityEntityIds.Add(serverId);
			if (!GameContext.World.Has<HoldPosition>(entity))
			{
				GameContext.World.Add<HoldPosition>(entity);
			}
		}
	}

	private void HandlePatrolCommand(int peerId, NetworkCommand cmd, List<Unit3D> allUnits, bool isQueued)
	{
		var ctx = CreateMovementContext(peerId, cmd, allUnits);
		int unitIndex = 0;

		foreach (int serverId in cmd.UnitEntityIds)
		{
			var entity = FindServerEntity(serverId, allUnits);
			if (entity == Entity.Null || !IsClientAuthorized(peerId, entity)) continue;
			if (!GameContext.World.Has<Movable>(entity)) continue;

			var scattered = CalculateScatteredPosition(cmd.TargetPosition, ctx.MoveDir, ctx.Right, unitIndex, ctx.Cols, ctx.Spacing);
			var unitPos = GetUnitPosition(entity, allUnits);
			var patrolA = new System.Numerics.Vector3(unitPos.X, unitPos.Y, unitPos.Z);

			if (isQueued && IsUnitActive(entity))
			{
				EnqueueCommand(entity, "patrol", scattered);
			}
			else
			{
				if (!isQueued) ClearUnitOrders(entity);
				GameContext.World.SetOrAdd(entity, new Patrol(patrolA, scattered));
				GameContext.World.SetOrAdd(entity, new MoveTo(scattered));
			}

			unitIndex++;
		}
	}

	private Vector3 GetUnitPosition(Entity entity, List<Unit3D> allUnits)
	{
		foreach (var u in allUnits)
		{
			if (u.Entity == entity) return u.GlobalPosition;
		}

		return Vector3.Zero;
	}

	private void HandleTrainCommand(int peerId, NetworkCommand cmd, List<Unit3D> allUnits)
	{
		string unitId = cmd.ArgString;
		if (!GameHost.UnitRegistry.TryGetValue(unitId, out var meta)) return;

		foreach (int serverId in cmd.UnitEntityIds)
		{
			var entity = FindServerEntity(serverId, allUnits);
			if (entity == Entity.Null || !IsClientAuthorized(peerId, entity)) continue;

			var ownerComp = GameContext.World.Get<Owner>(entity);
			var ownerEntity = ownerComp.PlayerEntity.Value;
			if (!GameContext.World.TryGet<PlayerResources>(ownerEntity, out var res)) continue;

			TryQueueProduction(entity, ownerEntity, unitId, meta, ref res);
		}
	}

	private void TryQueueProduction(Entity entity, Entity ownerEntity, string unitId, UnitMetadata meta, ref PlayerResources res)
	{
		var goldResourceId = new ResourceId("Gold");
		var woodResourceId = new ResourceId("Wood");
		var stoneResourceId = new ResourceId("Stone");

		int costGold = (int)meta.CostGold;
		int costWood = (int)meta.CostWood;
		int costStone = (int)meta.CostStone;

		if (res[goldResourceId] < costGold || res[woodResourceId] < costWood || res[stoneResourceId] < costStone)
			return;

		res[goldResourceId] -= costGold;
		res[woodResourceId] -= costWood;
		res[stoneResourceId] -= costStone;
		GameContext.World.Set(ownerEntity, res);

		if (!GameContext.World.Has<ProductionQueue>(entity)) GameContext.World.Add(entity, new ProductionQueue());

		ref var prod = ref GameContext.World.Get<ProductionQueue>(entity);
		prod.UnitIds.Add(unitId);
		if (prod.UnitIds.Count == 1)
		{
			prod.BuildTime = meta.ProductionTime;
			prod.CurrentProgress = 0f;
		}
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
		if (idx < 0 || idx >= prod.UnitIds.Count) return;

		string unitId = prod.UnitIds[idx];
		prod.UnitIds.RemoveAt(idx);

		if (idx == 0)
		{
			prod.CurrentProgress = 0f;
			if (prod.UnitIds.Count > 0 && GameHost.UnitRegistry.TryGetValue(prod.UnitIds[0], out var nextMeta))
			{
				prod.BuildTime = nextMeta.ProductionTime;
			}
		}

		if (GameHost.UnitRegistry.TryGetValue(unitId, out var regMeta))
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

		if (regMeta.PopCost > 0 && GameContext.World.IsAlive(ownerEntity) && GameContext.World.Has<PlayerPopulation>(ownerEntity))
		{
			ref var pop = ref GameContext.World.Get<PlayerPopulation>(ownerEntity);
			pop.Current = Math.Max(0, pop.Current - regMeta.PopCost);
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

	public List<(int PeerId, byte[] Payload)> BuildServerSnapshots(int localPeerId, List<Unit3D> allUnits)
	{
		var results = new List<(int PeerId, byte[] Payload)>();
		Entity worldEntity = FindWorldEntity();
		if (worldEntity == Entity.Null || !GameContext.World.Has<NetworkState>(worldEntity) || !GameContext.World.Has<NetworkMappingState>(worldEntity)) return results;

		ref var networkState = ref GameContext.World.Get<NetworkState>(worldEntity);
		networkState.SnapshotSequence++;
		int snapshotSequence = networkState.SnapshotSequence;
		bool isBaseline = (snapshotSequence % 30 == 0);

		if (LobbyManager.Instance != null)
		{
			var mapping = GameContext.World.Get<NetworkMappingState>(worldEntity);
			ProcessLobbyPlayersForSnapshots(localPeerId, allUnits, results, snapshotSequence, isBaseline, mapping);
		}

		foreach (var unit in allUnits)
		{
			if (GodotObject.IsInstanceValid(unit))
			{
				GD.Print($"[SERVER_STATE] Unit={unit.Entity.Id} Pos={unit.GlobalPosition}");
			}
		}

		return results;
	}

	private void ProcessLobbyPlayersForSnapshots(int localPeerId, List<Unit3D> allUnits, List<(int PeerId, byte[] Payload)> results, int snapshotSequence, bool isBaseline, NetworkMappingState mapping)
	{
		foreach (var p in LobbyManager.Instance.PlayerList)
		{
			if (p.PeerId == localPeerId || p.PeerId < 0) continue;
			if (!mapping.PeerIdToPlayerEntityMap.TryGetValue(p.PeerId, out var playerEntity)) continue;

			bool hasBaseline = _lastBaselineSnapshotsPerClient.TryGetValue(p.PeerId, out var lastBaselineMap);
			if (!hasBaseline && !isBaseline) continue;

			var payload = BuildClientSnapshotPayload(p.PeerId, playerEntity, allUnits, isBaseline, snapshotSequence, lastBaselineMap);
			results.Add((p.PeerId, payload));
		}
	}

	private byte[] BuildClientSnapshotPayload(int peerId, Entity playerEntity, List<Unit3D> allUnits, bool isBaseline, int snapshotSequence, Dictionary<int, UnitSnapshot> lastBaselineMap)
	{
		Vector3 cameraPos = _clientCameraPositions.TryGetValue(peerId, out var cam) ? cam : Vector3.Zero;
		var snapshotUnits = new List<UnitSnapshot>();
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

		var worldSnapshot = new WorldSnapshot
		{
			Sequence = snapshotSequence,
			IsBaseline = isBaseline,
			BaseSequence = isBaseline ? snapshotSequence : (snapshotSequence / 30) * 30,
			Units = snapshotUnits
		};
		return MemoryPackSerializer.Serialize(worldSnapshot);
	}

	private UnitSnapshot CreateUnitSnapshot(Unit3D unit, Vector3 cameraPos, out bool isDetailed)
	{
		float distToCamera = unit.GlobalPosition.DistanceTo(cameraPos);
		isDetailed = distToCamera <= 35.0f;
		return new UnitSnapshot
		{
			EntityId = unit.Entity.Id,
			UnitId = unit.UnitId,
			OwnerPlayerEntityId = GetOwnerPeerId(unit.Entity),
			Position = new NetworkVector3(unit.GlobalPosition),
			RotationY = unit.GlobalRotation.Y,
			CurrentHp = GameContext.World.Has<Health>(unit.Entity) ? GameContext.World.Get<Health>(unit.Entity).Current : 0f,
			MaxHp = GameContext.World.Has<Health>(unit.Entity) ? GameContext.World.Get<Health>(unit.Entity).Max : 0f,
			IsDead = GameContext.World.Has<Dead>(unit.Entity),
			IsBuilding = unit.IsBuilding,
			IsDetailed = isDetailed,
			Velocity = new NetworkVector3(unit.Velocity)
		};
	}

	private bool HasUnitChanged(Unit3D unit, ref UnitSnapshot currentSnap, bool isDetailed, Dictionary<int, UnitSnapshot> lastBaselineMap)
	{
		if (!lastBaselineMap.TryGetValue(unit.Entity.Id, out var baseSnap)) return true;

		if (isDetailed)
		{
			bool posChanged = baseSnap.Position.ToGodot().DistanceTo(unit.GlobalPosition) > 0.05f;
			bool rotChanged = Mathf.Abs(baseSnap.RotationY - unit.GlobalRotation.Y) > 0.05f;
			bool hpChanged = Mathf.Abs(baseSnap.CurrentHp - currentSnap.CurrentHp) > 0.1f;
			bool deadChanged = baseSnap.IsDead != currentSnap.IsDead;
			return posChanged || rotChanged || hpChanged || deadChanged;
		}

		bool posFarChanged = baseSnap.Position.ToGodot().DistanceTo(unit.GlobalPosition) > 1.0f;
		bool deadFarChanged = baseSnap.IsDead != currentSnap.IsDead;

		currentSnap.RotationY = 0f;
		currentSnap.CurrentHp = 0f;
		currentSnap.MaxHp = 0f;
		currentSnap.Velocity = new NetworkVector3(0f, 0f, 0f);

		return posFarChanged || deadFarChanged;
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

		var worldSnapshot = new WorldSnapshot
		{
			Sequence = networkState.SnapshotSequence,
			IsBaseline = true,
			BaseSequence = networkState.SnapshotSequence,
			Units = snapshotUnits
		};
		return MemoryPackSerializer.Serialize(worldSnapshot);
	}

	private void ProcessUnitForBaselineSnapshot(Unit3D unit, Entity playerEntity, Vector3 cameraPos, List<Unit3D> allUnits, List<UnitSnapshot> snapshotUnits, Dictionary<int, UnitSnapshot> nextBaselineMap)
	{
		if (!GodotObject.IsInstanceValid(unit)) return;
		if (!IsUnitVisibleToPlayer(playerEntity, unit.Entity, allUnits)) return;

		float distToCamera = unit.GlobalPosition.DistanceTo(cameraPos);
		bool isDetailed = distToCamera <= 35.0f;
		var currentSnap = new UnitSnapshot
		{
			EntityId = unit.Entity.Id,
			UnitId = unit.UnitId,
			OwnerPlayerEntityId = GetOwnerPeerId(unit.Entity),
			Position = new NetworkVector3(unit.GlobalPosition),
			RotationY = unit.GlobalRotation.Y,
			CurrentHp = GameContext.World.Has<Health>(unit.Entity) ? GameContext.World.Get<Health>(unit.Entity).Current : 0f,
			MaxHp = GameContext.World.Has<Health>(unit.Entity) ? GameContext.World.Get<Health>(unit.Entity).Max : 0f,
			IsDead = GameContext.World.Has<Dead>(unit.Entity),
			IsBuilding = unit.IsBuilding,
			IsDetailed = isDetailed,
			Velocity = new NetworkVector3(unit.Velocity)
		};
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
			if (LobbyManager.Instance == null || !LobbyManager.Instance.PlayerList.Exists(p => p.PeerId == key))
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
		if (LobbyManager.Instance == null) return false;
		var p = LobbyManager.Instance.PlayerList.Find(x => x.PeerId == peerId);
		return p != null && p.Team == "Spectator" && LobbyManager.Instance.SpectatorDelay;
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
		if (LobbyManager.Instance == null) return peers;

		foreach (var p in LobbyManager.Instance.PlayerList)
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
			LobbyManager.Instance?.SendChatMessage("System", message, false);
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

		if (LobbyManager.Instance?.PlayerList != null)
		{
			var p1 = LobbyManager.Instance.PlayerList.Find(x => x.PeerId == peerId1);
			var p2 = LobbyManager.Instance.PlayerList.Find(x => x.PeerId == peerId2);
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
		if (LobbyManager.Instance == null) return false;
		if (LobbyManager.Instance.PlayerList == null) return false;
		return LobbyManager.Instance.PlayerList.Count > 0;
	}

	private static int GetTeamIndexForPeer(int peerId, int defaultTeam)
	{
		var list = LobbyManager.Instance.PlayerList;
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
		var list = LobbyManager.Instance.PlayerList;
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

	public void SyncProductionQueue(int producerServerEntityId, string[] unitIds, float currentProgress, float buildTime)
	{
		if (ECSContext.WorldEntity == Entity.Null || !GameContext.World.Has<NetworkMappingState>(ECSContext.WorldEntity)) return;
		var mapping = GameContext.World.Get<NetworkMappingState>(ECSContext.WorldEntity);
		if (mapping.ServerToClientEntityMap.TryGetValue(producerServerEntityId, out var localEntity))
		{
			if (GameContext.World.IsAlive(localEntity))
			{
				if (!GameContext.World.Has<ProductionQueue>(localEntity))
				{
					GameContext.World.Add(localEntity, new ProductionQueue());
				}

				ref var prod = ref GameContext.World.Get<ProductionQueue>(localEntity);
				prod.UnitIds.Clear();
				prod.UnitIds.AddRange(unitIds);
				prod.CurrentProgress = currentProgress;
				prod.BuildTime = buildTime;
			}
		}
	}

	public List<(int PeerId, float Gold, float Wood, float Stone)> GetServerPlayerResources(Dictionary<int, Entity> peerIdToPlayerEntityMap, int localPeerId)
	{
		var result = new List<(int, float, float, float)>();

		foreach (var kvp in peerIdToPlayerEntityMap)
		{
			int peerId = kvp.Key;
			if (peerId == localPeerId) continue;

			if (GameContext.World.IsAlive(kvp.Value) && GameContext.World.TryGet<PlayerResources>(kvp.Value, out var res))
			{
				float gold = res.TryGetValue(ECSContext.PlayerResourceService.GoldResourceId, out var g) ? g : 0;
				float wood = res.TryGetValue(ECSContext.PlayerResourceService.WoodResourceId, out var w) ? w : 0;
				float stone = res.TryGetValue(ECSContext.PlayerResourceService.StoneResourceId, out var s) ? s : 0;
				result.Add((peerId, gold, wood, stone));
			}
		}

		return result;
	}

	public List<(int EntityId, string[] UnitIds, float CurrentProgress, float BuildTime)> GetServerProductionQueues(IEnumerable<Entity> unitEntities)
	{
		var result = new List<(int, string[], float, float)>();

		foreach (var entity in unitEntities)
		{
			if (!GameContext.World.IsAlive(entity) || !GameContext.World.Has<ProductionQueue>(entity)) continue;

			var prod = GameContext.World.Get<ProductionQueue>(entity);
			result.Add((entity.Id, prod.UnitIds.ToArray(), prod.CurrentProgress, prod.BuildTime));
		}

		return result;
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