using Arch.Core;
using Godot;
using MemoryPack;
using Realm.Ecs.Common;
using Realm.Ecs.Components.Combat;
using Realm.Ecs.Components.Core;
using Realm.Ecs.Components.Meta;
using Realm.Ecs.Components.Movement;
using Realm.Ecs.Components.Resources;
using Realm.Ecs.Components.Tags;
using Realm.Ecs.Services;
using System.Collections.Generic;

namespace Realm.Client.Services;

public class NetworkService
{
	private readonly WorldAccessor _ecsWorldAccessor;
	private World EcsWorld => _ecsWorldAccessor.Current;

	private readonly List<NetworkCommand> _unacknowledgedCommands = new();
	private readonly List<Realm.Client.WorldSnapshot> _queuedDeltas = new();
	private readonly Dictionary<int, Vector3> _clientCameraPositions = new();
	private readonly Dictionary<int, Dictionary<int, Realm.Client.UnitSnapshot>> _lastBaselineSnapshotsPerClient = new();
	private readonly Dictionary<int, List<DelayedPacket>> _spectatorDelayedPackets = new();

	private readonly List<Realm.Client.UnitSnapshot> _pendingUnitSpawns = new();
	private readonly List<Entity> _pendingUnitKills = new();

	private struct DelayedPacket
	{
		public string FunctionName;
		public object[] Arguments;
		public double SendTime;
	}

	public NetworkService(WorldAccessor ecsWorldAccessor)
	{
		_ecsWorldAccessor = ecsWorldAccessor;
	}

	public bool CountdownForcedByHost { get; set; } = false;
	public readonly Dictionary<int, bool> PlayerReadyStates = new();
	public readonly Dictionary<int, bool> DisallowedPausePeers = new();

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
	}

	public int GetServerEntityId(int localEntityId)
	{
		Entity worldEntity = FindWorldEntity();
		if (worldEntity != Entity.Null && EcsWorld.Has<NetworkMappingState>(worldEntity))
		{
			var mapping = EcsWorld.Get<NetworkMappingState>(worldEntity);
			if (mapping.ClientToServerEntityMap.TryGetValue(localEntityId, out int serverId))
			{
				return serverId;
			}
		}
		return localEntityId;
	}

	public Entity FindServerEntity(int entityId, List<Realm.Client.Unit3D> allUnits)
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

	public Realm.Client.Prop3D FindClosestProp(System.Numerics.Vector3 position, string propIdType, List<Realm.Client.Prop3D> allProps)
	{
		Realm.Client.Prop3D closest = null;
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
		return EcsWorld.Has<MoveTo>(entity) ||
		       EcsWorld.Has<Realm.Ecs.Components.Resources.BuildTask>(entity) ||
		       EcsWorld.Has<AttackTarget>(entity) ||
		       EcsWorld.Has<Realm.Ecs.Components.Movement.AttackMove>(entity) ||
		       EcsWorld.Has<Realm.Ecs.Components.Movement.Follow>(entity) ||
		       EcsWorld.Has<Realm.Ecs.Components.Movement.Patrol>(entity) ||
		       EcsWorld.Has<Gatherer>(entity) ||
		       EcsWorld.Has<WaypointQueue>(entity) ||
		       (EcsWorld.Has<BuildQueue>(entity) && EcsWorld.Get<BuildQueue>(entity).Count > 0);
	}

	private void EnqueueCommand(Entity entity, string type, System.Numerics.Vector3 position, Entity target = default)
	{
		if (!EcsWorld.Has<BuildQueue>(entity))
		{
			EcsWorld.Add(entity, new BuildQueue());
		}
		ref var q = ref EcsWorld.Get<BuildQueue>(entity);
		q.TryEnqueue(type, position, target);
	}

	public int GetOwnerPeerId(Entity unitEntity)
	{
		if (!EcsWorld.Has<Owner>(unitEntity)) return -1;
		var owner = EcsWorld.Get<Owner>(unitEntity).PlayerEntity;
		Entity worldEntity = FindWorldEntity();
		if (worldEntity == Entity.Null || !EcsWorld.Has<NetworkMappingState>(worldEntity)) return -1;
		var mapping = EcsWorld.Get<NetworkMappingState>(worldEntity);
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
		if (!EcsWorld.Has<Owner>(unitEntity)) return false;
		var ownerComp = EcsWorld.Get<Owner>(unitEntity);
		Entity worldEntity = FindWorldEntity();
		if (worldEntity == Entity.Null || !EcsWorld.Has<NetworkMappingState>(worldEntity)) return false;
		var mapping = EcsWorld.Get<NetworkMappingState>(worldEntity);
		if (mapping.PeerIdToPlayerEntityMap.TryGetValue(peerId, out var playerEntity))
		{
			return ownerComp.PlayerEntity.Value == playerEntity;
		}
		return false;
	}

	public bool IsUnitVisibleToPlayer(Entity playerEntity, Entity unitEntity, List<Realm.Client.Unit3D> allUnits)
	{
		if (EcsWorld.Has<Owner>(unitEntity) && EcsWorld.Get<Owner>(unitEntity).PlayerEntity.Value == playerEntity)
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
			if (EcsWorld.Has<Owner>(unit.Entity) && EcsWorld.Get<Owner>(unit.Entity).PlayerEntity.Value == playerEntity)
			{
				if (unit.GlobalPosition.DistanceTo(unitPos) <= 15.0f)
				{
					return true;
				}
			}
		}
		return false;
	}

	public float ComputeDynamicInterpolationFactor()
	{
		float bufferTime = 0.1f;
		if (Network.LobbyManager.Instance != null && Network.LobbyManager.Instance.LocalPlayer != null)
		{
			string latStr = Network.LobbyManager.Instance.LocalPlayer.Latency;
			if (!string.IsNullOrEmpty(latStr) && latStr != "--" && latStr.Contains(" ms"))
			{
				string numStr = latStr.Replace(" ms", "").Trim();
				if (float.TryParse(numStr, out float rttMs))
				{
					bufferTime = Mathf.Max(0.1f, (rttMs / 1000f) * 1.5f);
				}
			}
		}
		return 1f / bufferTime;
	}

	public (int CommandId, byte[] Payload) BuildClientCommand(string commandType, List<int> unitIds, System.Numerics.Vector3 targetPos, int targetEntityId, string argString)
	{
		int commandId = 1;
		Entity worldEntity = FindWorldEntity();
		if (worldEntity != Entity.Null && EcsWorld.Has<NetworkState>(worldEntity))
		{
			commandId = EcsWorld.Get<NetworkState>(worldEntity).NextCommandId;
			ref var ns = ref EcsWorld.Get<NetworkState>(worldEntity);
			ns.NextCommandId++;
		}

		var cmd = new NetworkCommand
		{
			CommandId = commandId,
			CommandType = commandType,
			UnitEntityIds = unitIds,
			TargetPosition = new Realm.Client.NetworkVector3(targetPos.X, targetPos.Y, targetPos.Z),
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
		if (worldEntity == Entity.Null || !EcsWorld.Has<NetworkState>(worldEntity)) return;
		ref var ns = ref EcsWorld.Get<NetworkState>(worldEntity);
		ns.LastSnapshotReceivedTime = global::Godot.Time.GetTicksMsec();
		ProcessSnapshotDirect(payload);
	}

	private void ProcessSnapshotDirect(byte[] payload)
	{
		var snapshot = MemoryPackSerializer.Deserialize<Realm.Client.WorldSnapshot>(payload);
		Entity worldEntity = FindWorldEntity();
		if (worldEntity == Entity.Null || !EcsWorld.Has<NetworkState>(worldEntity)) return;

		ref var networkState = ref EcsWorld.Get<NetworkState>(worldEntity);
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

	private void ApplyWorldSnapshot(Realm.Client.WorldSnapshot snapshot)
	{
		Entity worldEntity = FindWorldEntity();
		if (worldEntity == Entity.Null || !EcsWorld.Has<NetworkMappingState>(worldEntity)) return;
		var mapping = EcsWorld.Get<NetworkMappingState>(worldEntity);

		if (snapshot.IsBaseline)
		{
			ProcessBaselineSnapshotMismatches(snapshot, mapping);
		}

		foreach (var snap in snapshot.Units)
		{
			ApplySnapshotToUnit(snap, snapshot.Sequence, mapping);
		}
	}

	private void ProcessBaselineSnapshotMismatches(Realm.Client.WorldSnapshot snapshot, NetworkMappingState mapping)
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
			if (!EcsWorld.IsAlive(localEnt) || EcsWorld.Has<Dead>(localEnt)) continue;

			EcsWorld.Add<Dead>(localEnt);
			_pendingUnitKills.Add(localEnt);
		}
	}

	private void ApplySnapshotToUnit(Realm.Client.UnitSnapshot snap, int sequence, NetworkMappingState mapping)
	{
		if (!mapping.ServerToClientEntityMap.TryGetValue(snap.EntityId, out var localEntity))
		{
			if (!snap.IsDead) _pendingUnitSpawns.Add(snap);
			return;
		}

		if (!EcsWorld.IsAlive(localEntity)) return;

		if (snap.IsDead)
		{
			if (!EcsWorld.Has<Dead>(localEntity))
			{
				EcsWorld.Add<Dead>(localEntity);
				_pendingUnitKills.Add(localEntity);
			}
			return;
		}

		if (EcsWorld.Has<Health>(localEntity))
		{
			var hp = EcsWorld.Get<Health>(localEntity);
			hp.Current = snap.CurrentHp;
			hp.Max = snap.MaxHp;
			EcsWorld.Set(localEntity, hp);
		}

		var target = new InterpolationTarget
		{
			Position = snap.Position.ToNumerics(),
			Velocity = snap.Velocity.ToNumerics(),
			RotationY = snap.RotationY
		};
		EcsWorld.SetOrAdd(localEntity, target);
		GD.Print($"[CLIENT_SNAPSHOT_APPLIED] Sequence={sequence} Unit={snap.EntityId} ServerPos={snap.Position.ToGodot()}");
	}

	public IReadOnlyList<Realm.Client.UnitSnapshot> FlushPendingUnitSpawns()
	{
		var result = new List<Realm.Client.UnitSnapshot>(_pendingUnitSpawns);
		_pendingUnitSpawns.Clear();
		return result;
	}

	public IReadOnlyList<Entity> FlushPendingUnitKills()
	{
		var result = new List<Entity>(_pendingUnitKills);
		_pendingUnitKills.Clear();
		return result;
	}

	public Entity SpawnUnitFromSnapshot(Realm.Client.UnitSnapshot snap, System.Func<string, bool, string> getFallbackModelPath, out string modelPath, out bool isEnemy)
	{
		modelPath = string.Empty;
		isEnemy = false;

		if (!Realm.Client.Core.GameHost.UnitRegistry.TryGetValue(snap.UnitId, out var meta))
		{
			return Entity.Null;
		}

		Entity worldEntity = FindWorldEntity();
		if (worldEntity == Entity.Null || !EcsWorld.Has<NetworkMappingState>(worldEntity)) return Entity.Null;

		ref var mapping = ref EcsWorld.Get<NetworkMappingState>(worldEntity);

		Entity ownerPlayerEntity = mapping.PlayerEntity;
		if (mapping.PeerIdToPlayerEntityMap.TryGetValue(snap.OwnerPlayerEntityId, out var pe))
		{
			ownerPlayerEntity = pe;
		}
		int localPeerId = 1;
		var mainLoop = Engine.GetMainLoop();
		if (mainLoop is SceneTree tree)
		{
			localPeerId = tree.GetMultiplayer().GetUniqueId();
		}
		isEnemy = ArePeersEnemies(localPeerId, snap.OwnerPlayerEntityId);

		var entity = EcsWorld.Create();
		EcsWorld.Add(entity, new DefinitionId(snap.UnitId));
		EcsWorld.Add(entity, new Name(meta.Name));
		EcsWorld.Add(entity, new Position(snap.Position.ToNumerics()));
		EcsWorld.Add(entity, new Owner(ownerPlayerEntity.AsPlayerEntity(EcsWorld)));
		EcsWorld.Add(entity, new Health(snap.CurrentHp, snap.MaxHp));
		if (meta.Damage > 0 || snap.UnitId == "priest")
		{
			EcsWorld.Add(entity, new Attack(meta.Damage, meta.Range, meta.AttackCooldown));
		}
		EcsWorld.Add(entity, new Armor(meta.Armor));
		if (meta.Speed > 0)
		{
			EcsWorld.Add(entity, new MovementStats(meta.Speed, 20f, 10f));
			EcsWorld.Add(entity, new Movable());
			EcsWorld.Add(entity, new Inventory());
		}
		else
		{
			EcsWorld.Add(entity, new Building());
		}
		var interpolationTarget = new InterpolationTarget
		{
			Position = snap.Position.ToNumerics(),
			Velocity = snap.Velocity.ToNumerics(),
			RotationY = snap.RotationY
		};
		EcsWorld.Add(entity, interpolationTarget);
		EcsWorld.Add(entity, new UnitFaction(isEnemy));

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
		if (worldEntity == Entity.Null || !EcsWorld.Has<NetworkMappingState>(worldEntity)) return false;
		var mapping = EcsWorld.Get<NetworkMappingState>(worldEntity);
		return mapping.ServerToClientEntityMap.TryGetValue(serverEntityId, out localEntity);
	}

	public void SetBackupResources(float gold, float wood, float stone)
	{
		var worldQuery = Realm.Ecs.Common.QueryCache.AllReplayStateQuery;
		EcsWorld.Query(in worldQuery, (Entity entity) =>
		{
			ref var state = ref EcsWorld.Get<ReplayState>(entity);
			state.GoldBackup = gold;
			state.WoodBackup = wood;
			state.StoneBackup = stone;
		});
	}

	public readonly struct ServerCommandResult
	{
		public readonly bool NeedsBuildUnit;
		public readonly string BuildUnitType;
		public readonly Vector3 BuildPosition;
		public readonly int BuildPeerOwner;

		public readonly bool NeedsSpellEffect;
		public readonly string SpellId;
		public readonly Vector3 SpellPosition;

		public readonly List<int> StopVelocityEntityIds;
		public readonly List<int> HoldVelocityEntityIds;

		public ServerCommandResult(
			bool needsBuildUnit, string buildUnitType, Vector3 buildPosition, int buildPeerOwner,
			bool needsSpellEffect, string spellId, Vector3 spellPosition,
			List<int> stopVelocityEntityIds, List<int> holdVelocityEntityIds)
		{
			NeedsBuildUnit = needsBuildUnit;
			BuildUnitType = buildUnitType;
			BuildPosition = buildPosition;
			BuildPeerOwner = buildPeerOwner;
			NeedsSpellEffect = needsSpellEffect;
			SpellId = spellId;
			SpellPosition = spellPosition;
			StopVelocityEntityIds = stopVelocityEntityIds;
			HoldVelocityEntityIds = holdVelocityEntityIds;
		}
	}

	public ServerCommandResult ExecuteServerCommand(int peerId, NetworkCommand cmd, List<Realm.Client.Unit3D> allUnits, List<Realm.Client.Prop3D> allProps)
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

	private void DispatchServerCommand(int peerId, NetworkCommand cmd, List<Realm.Client.Unit3D> allUnits, List<Realm.Client.Prop3D> allProps, List<int> stopVelocityEntityIds, List<int> holdVelocityEntityIds)
	{
		if (DispatchMovementCommand(peerId, cmd, allUnits, stopVelocityEntityIds, holdVelocityEntityIds)) return;
		if (DispatchActionCommand(peerId, cmd, allUnits, allProps)) return;
		DispatchTrainingCommand(peerId, cmd, allUnits);
	}

	private bool DispatchMovementCommand(int peerId, NetworkCommand cmd, List<Realm.Client.Unit3D> allUnits, List<int> stopVelocityEntityIds, List<int> holdVelocityEntityIds)
	{
		switch (cmd.CommandType)
		{
			case "move": HandleMoveCommand(peerId, cmd, allUnits); return true;
			case "move_queued": HandleMoveQueuedCommand(peerId, cmd, allUnits); return true;
			case "stop": HandleStopCommand(peerId, cmd, allUnits, stopVelocityEntityIds); return true;
			case "hold": HandleHoldCommand(peerId, cmd, allUnits, holdVelocityEntityIds); return true;
			case "patrol":
			case "patrol_queued": HandlePatrolCommand(peerId, cmd, allUnits, cmd.CommandType == "patrol_queued"); return true;
		}
		return false;
	}

	private bool DispatchActionCommand(int peerId, NetworkCommand cmd, List<Realm.Client.Unit3D> allUnits, List<Realm.Client.Prop3D> allProps)
	{
		bool isQueued = cmd.CommandType.EndsWith("_queued");
		switch (cmd.CommandType)
		{
			case "attack":
			case "attack_queued": HandleAttackCommand(peerId, cmd, allUnits, isQueued); return true;
			case "follow":
			case "follow_queued": HandleFollowCommand(peerId, cmd, allUnits, isQueued); return true;
			case "gather":
			case "gather_queued": HandleGatherCommand(peerId, cmd, allUnits, allProps, isQueued); return true;
		}
		return false;
	}

	private void DispatchTrainingCommand(int peerId, NetworkCommand cmd, List<Realm.Client.Unit3D> allUnits)
	{
		switch (cmd.CommandType)
		{
			case "train": HandleTrainCommand(peerId, cmd, allUnits); break;
			case "cancel_train": HandleCancelTrainCommand(peerId, cmd, allUnits); break;
		}
	}

	private void HandleMoveCommand(int peerId, NetworkCommand cmd, List<Realm.Client.Unit3D> allUnits)
	{
		CalculateGroupMovement(peerId, cmd.UnitEntityIds, allUnits, cmd.TargetPosition, out System.Numerics.Vector3 moveDir, out System.Numerics.Vector3 right);
		int cols = Mathf.CeilToInt(Mathf.Sqrt(cmd.UnitEntityIds.Count));
		float spacing = 2.2f;
		int unitIndex = 0;

		foreach (int serverId in cmd.UnitEntityIds)
		{
			var entity = FindServerEntity(serverId, allUnits);
			if (entity == Entity.Null || !IsClientAuthorized(peerId, entity)) continue;
			
			ClearUnitOrders(entity);
			var scattered = CalculateScatteredPosition(cmd.TargetPosition, moveDir, right, unitIndex, cols, spacing);
			var moveTo = new MoveTo(scattered);
			EcsWorld.SetOrAdd(entity, moveTo);
			unitIndex++;
		}
	}

	private void HandleMoveQueuedCommand(int peerId, NetworkCommand cmd, List<Realm.Client.Unit3D> allUnits)
	{
		CalculateGroupMovement(peerId, cmd.UnitEntityIds, allUnits, cmd.TargetPosition, out System.Numerics.Vector3 moveDir, out System.Numerics.Vector3 right);
		int cols = Mathf.CeilToInt(Mathf.Sqrt(cmd.UnitEntityIds.Count));
		float spacing = 2.2f;
		int unitIndex = 0;

		foreach (int serverId in cmd.UnitEntityIds)
		{
			var entity = FindServerEntity(serverId, allUnits);
			if (entity == Entity.Null || !IsClientAuthorized(peerId, entity)) continue;
			if (!EcsWorld.Has<Movable>(entity)) continue;

			var scattered = CalculateScatteredPosition(cmd.TargetPosition, moveDir, right, unitIndex, cols, spacing);
			ProcessQueuedMoveForEntity(entity, scattered);
			unitIndex++;
		}
	}

	private void ProcessQueuedMoveForEntity(Entity entity, System.Numerics.Vector3 scattered)
	{
		bool hasNonMoveTasks = EcsWorld.Has<Realm.Ecs.Components.Resources.BuildTask>(entity) ||
		                       EcsWorld.Has<AttackTarget>(entity) ||
		                       EcsWorld.Has<Realm.Ecs.Components.Movement.AttackMove>(entity) ||
		                       EcsWorld.Has<Realm.Ecs.Components.Movement.Follow>(entity) ||
		                       EcsWorld.Has<Patrol>(entity) ||
		                       EcsWorld.Has<Gatherer>(entity);

		if (hasNonMoveTasks)
		{
			EnqueueCommand(entity, "move", scattered);
			return;
		}

		if (EcsWorld.Has<MoveTo>(entity))
		{
			if (EcsWorld.Has<WaypointQueue>(entity))
			{
				var q = EcsWorld.Get<WaypointQueue>(entity);
				q.Add(scattered);
				EcsWorld.Set(entity, q);
			}
			else
			{
				EcsWorld.Add(entity, new WaypointQueue(scattered));
			}
		}
		else
		{
			ClearUnitOrders(entity);
			EcsWorld.SetOrAdd(entity, new MoveTo(scattered));
		}
	}

	private void HandleAttackCommand(int peerId, NetworkCommand cmd, List<Realm.Client.Unit3D> allUnits, bool isQueued)
	{
		var targetEntity = FindServerEntity(cmd.TargetEntityId, allUnits);
		if (targetEntity == Entity.Null) return;

		var targetPos = EcsWorld.Has<Position>(targetEntity) ? EcsWorld.Get<Position>(targetEntity).Value : System.Numerics.Vector3.Zero;
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
				EcsWorld.SetOrAdd(entity, new AttackTarget(targetEntity));
			}
		}
	}

	private void HandleFollowCommand(int peerId, NetworkCommand cmd, List<Realm.Client.Unit3D> allUnits, bool isQueued)
	{
		var targetEntity = FindServerEntity(cmd.TargetEntityId, allUnits);
		if (targetEntity == Entity.Null) return;

		var targetPos = EcsWorld.Has<Position>(targetEntity) ? EcsWorld.Get<Position>(targetEntity).Value : System.Numerics.Vector3.Zero;
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

		if (EcsWorld.Has<DefinitionId>(entity) && EcsWorld.Get<DefinitionId>(entity).Value == "priest")
		{
			EcsWorld.SetOrAdd(entity, new HealingTarget(targetEntity));
		}
		else if (EcsWorld.Has<Movable>(entity))
		{
			EcsWorld.SetOrAdd(entity, new Realm.Ecs.Components.Movement.Follow(targetEntity));
		}
	}

	private void HandleGatherCommand(int peerId, NetworkCommand cmd, List<Realm.Client.Unit3D> allUnits, List<Realm.Client.Prop3D> allProps, bool isQueued)
	{
		var targetPos = new System.Numerics.Vector3(cmd.TargetPosition.X, cmd.TargetPosition.Y, cmd.TargetPosition.Z);
		Realm.Client.Prop3D prop = FindClosestProp(targetPos, "", allProps);
		if (prop == null) return;

		string resType = GetPropResourceType(prop.PropId);
		if (resType == null) return;

		var propPos = new System.Numerics.Vector3(prop.GlobalPosition.X, prop.GlobalPosition.Y, prop.GlobalPosition.Z);
		foreach (int serverId in cmd.UnitEntityIds)
		{
			var entity = FindServerEntity(serverId, allUnits);
			if (entity == Entity.Null || !IsClientAuthorized(peerId, entity)) continue;
			if (EcsWorld.Has<DefinitionId>(entity) && EcsWorld.Get<DefinitionId>(entity).Value != "worker") continue;

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
		EcsWorld.SetOrAdd(entity, new Gatherer(resType, propEntity));
		EcsWorld.SetOrAdd(entity, new MoveTo(propPos));
	}

	private string GetPropResourceType(string propId)
	{
		return propId switch
		{
			"goldmine" => "gold",
			"tree" => "wood",
			"rock" => "stone",
			_ => null
		};
	}

	private void HandleStopCommand(int peerId, NetworkCommand cmd, List<Realm.Client.Unit3D> allUnits, List<int> stopVelocityEntityIds)
	{
		foreach (int serverId in cmd.UnitEntityIds)
		{
			var entity = FindServerEntity(serverId, allUnits);
			if (entity == Entity.Null || !IsClientAuthorized(peerId, entity)) continue;
			ClearUnitOrders(entity);
			stopVelocityEntityIds.Add(serverId);
		}
	}

	private void HandleHoldCommand(int peerId, NetworkCommand cmd, List<Realm.Client.Unit3D> allUnits, List<int> holdVelocityEntityIds)
	{
		foreach (int serverId in cmd.UnitEntityIds)
		{
			var entity = FindServerEntity(serverId, allUnits);
			if (entity == Entity.Null || !IsClientAuthorized(peerId, entity)) continue;
			ClearUnitOrders(entity);
			holdVelocityEntityIds.Add(serverId);
			if (!EcsWorld.Has<Realm.Ecs.Components.Movement.HoldPosition>(entity))
			{
				EcsWorld.Add<Realm.Ecs.Components.Movement.HoldPosition>(entity);
			}
		}
	}

	private void HandlePatrolCommand(int peerId, NetworkCommand cmd, List<Realm.Client.Unit3D> allUnits, bool isQueued)
	{
		CalculateGroupMovement(peerId, cmd.UnitEntityIds, allUnits, cmd.TargetPosition, out System.Numerics.Vector3 moveDir, out System.Numerics.Vector3 right);
		int cols = Mathf.CeilToInt(Mathf.Sqrt(cmd.UnitEntityIds.Count));
		float spacing = 2.2f;
		int unitIndex = 0;

		foreach (int serverId in cmd.UnitEntityIds)
		{
			var entity = FindServerEntity(serverId, allUnits);
			if (entity == Entity.Null || !IsClientAuthorized(peerId, entity)) continue;
			if (!EcsWorld.Has<Movable>(entity)) continue;

			var scattered = CalculateScatteredPosition(cmd.TargetPosition, moveDir, right, unitIndex, cols, spacing);
			var unitPos = GetUnitPosition(entity, allUnits);
			var patrolA = new System.Numerics.Vector3(unitPos.X, unitPos.Y, unitPos.Z);

			if (isQueued && IsUnitActive(entity))
			{
				EnqueueCommand(entity, "patrol", scattered);
			}
			else
			{
				if (!isQueued) ClearUnitOrders(entity);
				EcsWorld.SetOrAdd(entity, new Patrol(patrolA, scattered));
				EcsWorld.SetOrAdd(entity, new MoveTo(scattered));
			}
			unitIndex++;
		}
	}

	private Vector3 GetUnitPosition(Entity entity, List<Realm.Client.Unit3D> allUnits)
	{
		foreach (var u in allUnits)
		{
			if (u.Entity == entity) return u.GlobalPosition;
		}
		return Vector3.Zero;
	}

	private void HandleTrainCommand(int peerId, NetworkCommand cmd, List<Realm.Client.Unit3D> allUnits)
	{
		string unitId = cmd.ArgString;
		if (!Realm.Client.Core.GameHost.UnitRegistry.TryGetValue(unitId, out var meta)) return;

		foreach (int serverId in cmd.UnitEntityIds)
		{
			var entity = FindServerEntity(serverId, allUnits);
			if (entity == Entity.Null || !IsClientAuthorized(peerId, entity)) continue;

			var ownerComp = EcsWorld.Get<Owner>(entity);
			var ownerEntity = ownerComp.PlayerEntity.Value;
			if (!EcsWorld.TryGet<PlayerResources>(ownerEntity, out var res)) continue;

			TryQueueProduction(entity, ownerEntity, unitId, meta, ref res);
		}
	}

	private void TryQueueProduction(Entity entity, Entity ownerEntity, string unitId, Realm.Shared.Metadata.UnitMetadata meta, ref PlayerResources res)
	{
		var goldResourceId = new ResourceId("Gold");
		var woodResourceId = new ResourceId("Wood");
		var stoneResourceId = new ResourceId("Stone");

		int costGold = (int)meta.CostGold;
		int costWood = (int)meta.CostWood;
		int costStone = (int)meta.CostStone;
		
		if (res.Value[goldResourceId] < costGold || res.Value[woodResourceId] < costWood || res.Value[stoneResourceId] < costStone)
			return;

		res.Value[goldResourceId] -= costGold;
		res.Value[woodResourceId] -= costWood;
		res.Value[stoneResourceId] -= costStone;
		EcsWorld.Set(ownerEntity, res);

		if (!EcsWorld.Has<ProductionQueue>(entity)) EcsWorld.Add(entity, new ProductionQueue());

		ref var prod = ref EcsWorld.Get<ProductionQueue>(entity);
		prod.UnitIds.Add(unitId);
		if (prod.UnitIds.Count == 1)
		{
			prod.BuildTime = meta.ProductionTime;
			prod.CurrentProgress = 0f;
		}
	}

	private void HandleCancelTrainCommand(int peerId, NetworkCommand cmd, List<Realm.Client.Unit3D> allUnits)
	{
		foreach (int serverId in cmd.UnitEntityIds)
		{
			var entity = FindServerEntity(serverId, allUnits);
			if (entity == Entity.Null || !IsClientAuthorized(peerId, entity)) continue;
			if (!EcsWorld.Has<ProductionQueue>(entity)) continue;

			CancelProductionQueueItem(entity, cmd.TargetEntityId);
		}
	}

	private void CancelProductionQueueItem(Entity entity, int idx)
	{
		ref var prod = ref EcsWorld.Get<ProductionQueue>(entity);
		if (idx < 0 || idx >= prod.UnitIds.Count) return;

		string unitId = prod.UnitIds[idx];
		prod.UnitIds.RemoveAt(idx);

		if (idx == 0)
		{
			prod.CurrentProgress = 0f;
			if (prod.UnitIds.Count > 0 && Realm.Client.Core.GameHost.UnitRegistry.TryGetValue(prod.UnitIds[0], out var nextMeta))
			{
				prod.BuildTime = nextMeta.ProductionTime;
			}
		}

		if (Realm.Client.Core.GameHost.UnitRegistry.TryGetValue(unitId, out var regMeta))
		{
			RefundCanceledProduction(entity, regMeta);
		}
	}

	private void RefundCanceledProduction(Entity entity, Realm.Shared.Metadata.UnitMetadata regMeta)
	{
		var ownerComp = EcsWorld.Get<Owner>(entity);
		var ownerEntity = ownerComp.PlayerEntity.Value;
		
		if (EcsWorld.TryGet<PlayerResources>(ownerEntity, out var res))
		{
			res.Value[new ResourceId("Gold")] += (int)regMeta.CostGold;
			res.Value[new ResourceId("Wood")] += (int)regMeta.CostWood;
			res.Value[new ResourceId("Stone")] += (int)regMeta.CostStone;
			EcsWorld.Set(ownerEntity, res);
		}

		if (regMeta.PopCost > 0 && EcsWorld.IsAlive(ownerEntity) && EcsWorld.Has<PlayerPopulation>(ownerEntity))
		{
			ref var pop = ref EcsWorld.Get<PlayerPopulation>(ownerEntity);
			pop.Current = System.Math.Max(0, pop.Current - regMeta.PopCost);
		}
	}

	private void CalculateGroupMovement(int peerId, List<int> unitEntityIds, List<Realm.Client.Unit3D> allUnits, Realm.Client.NetworkVector3 targetPosition, out System.Numerics.Vector3 moveDir, out System.Numerics.Vector3 right)
	{
		System.Numerics.Vector3 groupCenter = System.Numerics.Vector3.Zero;
		int movableCount = 0;
		foreach (int serverId in unitEntityIds)
		{
			var entity = FindServerEntity(serverId, allUnits);
			if (entity == Entity.Null || !IsClientAuthorized(peerId, entity)) continue;
			if (EcsWorld.Has<Position>(entity))
			{
				groupCenter += EcsWorld.Get<Position>(entity).Value;
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

	private System.Numerics.Vector3 CalculateScatteredPosition(Realm.Client.NetworkVector3 targetPos, System.Numerics.Vector3 moveDir, System.Numerics.Vector3 right, int unitIndex, int cols, float spacing)
	{
		int row = unitIndex / cols;
		int col = unitIndex % cols;
		float offsetX = (col - cols * 0.5f + 0.5f) * spacing;
		float offsetZ = -row * spacing;
		var tPos = new System.Numerics.Vector3(targetPos.X, targetPos.Y, targetPos.Z);
		return tPos + right * offsetX + moveDir * offsetZ;
	}

	public List<(int PeerId, byte[] Payload)> BuildServerSnapshots(int localPeerId, List<Realm.Client.Unit3D> allUnits)
	{
		var results = new List<(int PeerId, byte[] Payload)>();
		Entity worldEntity = FindWorldEntity();
		if (worldEntity == Entity.Null || !EcsWorld.Has<NetworkState>(worldEntity) || !EcsWorld.Has<NetworkMappingState>(worldEntity)) return results;

		ref var networkState = ref EcsWorld.Get<NetworkState>(worldEntity);
		networkState.SnapshotSequence++;
		int snapshotSequence = networkState.SnapshotSequence;
		bool isBaseline = (snapshotSequence % 30 == 0);

		if (Network.LobbyManager.Instance != null)
		{
			var mapping = EcsWorld.Get<NetworkMappingState>(worldEntity);
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

	private void ProcessLobbyPlayersForSnapshots(int localPeerId, List<Realm.Client.Unit3D> allUnits, List<(int PeerId, byte[] Payload)> results, int snapshotSequence, bool isBaseline, NetworkMappingState mapping)
	{
		foreach (var p in Network.LobbyManager.Instance.PlayerList)
		{
			if (p.PeerId == localPeerId || p.PeerId < 0) continue;
			if (!mapping.PeerIdToPlayerEntityMap.TryGetValue(p.PeerId, out var playerEntity)) continue;

			bool hasBaseline = _lastBaselineSnapshotsPerClient.TryGetValue(p.PeerId, out var lastBaselineMap);
			if (!hasBaseline && !isBaseline) continue;

			var payload = BuildClientSnapshotPayload(p.PeerId, playerEntity, allUnits, isBaseline, snapshotSequence, lastBaselineMap);
			results.Add((p.PeerId, payload));
		}
	}

	private byte[] BuildClientSnapshotPayload(int peerId, Entity playerEntity, List<Realm.Client.Unit3D> allUnits, bool isBaseline, int snapshotSequence, Dictionary<int, Realm.Client.UnitSnapshot> lastBaselineMap)
	{
		Vector3 cameraPos = _clientCameraPositions.TryGetValue(peerId, out var cam) ? cam : Vector3.Zero;
		var snapshotUnits = new List<Realm.Client.UnitSnapshot>();
		var nextBaselineMap = isBaseline ? new Dictionary<int, Realm.Client.UnitSnapshot>() : null;

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

		var worldSnapshot = new Realm.Client.WorldSnapshot
		{
			Sequence = snapshotSequence,
			IsBaseline = isBaseline,
			BaseSequence = isBaseline ? snapshotSequence : (snapshotSequence / 30) * 30,
			Units = snapshotUnits
		};
		return MemoryPackSerializer.Serialize(worldSnapshot);
	}

	private Realm.Client.UnitSnapshot CreateUnitSnapshot(Realm.Client.Unit3D unit, Vector3 cameraPos, out bool isDetailed)
	{
		float distToCamera = unit.GlobalPosition.DistanceTo(cameraPos);
		isDetailed = distToCamera <= 35.0f;
		return new Realm.Client.UnitSnapshot
		{
			EntityId = unit.Entity.Id,
			UnitId = unit.UnitId,
			OwnerPlayerEntityId = GetOwnerPeerId(unit.Entity),
			Position = new Realm.Client.NetworkVector3(unit.GlobalPosition),
			RotationY = unit.GlobalRotation.Y,
			CurrentHp = EcsWorld.Has<Health>(unit.Entity) ? EcsWorld.Get<Health>(unit.Entity).Current : 0f,
			MaxHp = EcsWorld.Has<Health>(unit.Entity) ? EcsWorld.Get<Health>(unit.Entity).Max : 0f,
			IsDead = EcsWorld.Has<Dead>(unit.Entity),
			IsBuilding = unit.IsBuilding,
			IsDetailed = isDetailed,
			Velocity = new Realm.Client.NetworkVector3(unit.Velocity)
		};
	}

	private bool HasUnitChanged(Realm.Client.Unit3D unit, ref Realm.Client.UnitSnapshot currentSnap, bool isDetailed, Dictionary<int, Realm.Client.UnitSnapshot> lastBaselineMap)
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
		currentSnap.Velocity = new Realm.Client.NetworkVector3(0f, 0f, 0f);

		return posFarChanged || deadFarChanged;
	}

	public byte[] BuildExplicitBaselineSnapshot(int targetPeerId, List<Realm.Client.Unit3D> allUnits)
	{
		Entity worldEntity = FindWorldEntity();
		if (worldEntity == Entity.Null || !EcsWorld.Has<NetworkState>(worldEntity) || !EcsWorld.Has<NetworkMappingState>(worldEntity)) return System.Array.Empty<byte>();

		ref var networkState = ref EcsWorld.Get<NetworkState>(worldEntity);
		var mapping = EcsWorld.Get<NetworkMappingState>(worldEntity);
		if (!mapping.PeerIdToPlayerEntityMap.TryGetValue(targetPeerId, out var playerEntity)) return System.Array.Empty<byte>();

		Vector3 cameraPos = _clientCameraPositions.TryGetValue(targetPeerId, out var cam) ? cam : Vector3.Zero;
		var snapshotUnits = new List<Realm.Client.UnitSnapshot>();
		var nextBaselineMap = new Dictionary<int, Realm.Client.UnitSnapshot>();

		foreach (var unit in allUnits)
		{
			ProcessUnitForBaselineSnapshot(unit, playerEntity, cameraPos, allUnits, snapshotUnits, nextBaselineMap);
		}

		_lastBaselineSnapshotsPerClient[targetPeerId] = nextBaselineMap;

		var worldSnapshot = new Realm.Client.WorldSnapshot
		{
			Sequence = networkState.SnapshotSequence,
			IsBaseline = true,
			BaseSequence = networkState.SnapshotSequence,
			Units = snapshotUnits
		};
		return MemoryPackSerializer.Serialize(worldSnapshot);
	}

	private void ProcessUnitForBaselineSnapshot(Realm.Client.Unit3D unit, Entity playerEntity, Vector3 cameraPos, List<Realm.Client.Unit3D> allUnits, List<Realm.Client.UnitSnapshot> snapshotUnits, Dictionary<int, Realm.Client.UnitSnapshot> nextBaselineMap)
	{
		if (!GodotObject.IsInstanceValid(unit)) return;
		if (!IsUnitVisibleToPlayer(playerEntity, unit.Entity, allUnits)) return;

		float distToCamera = unit.GlobalPosition.DistanceTo(cameraPos);
		bool isDetailed = distToCamera <= 35.0f;
		var currentSnap = new Realm.Client.UnitSnapshot
		{
			EntityId = unit.Entity.Id,
			UnitId = unit.UnitId,
			OwnerPlayerEntityId = GetOwnerPeerId(unit.Entity),
			Position = new Realm.Client.NetworkVector3(unit.GlobalPosition),
			RotationY = unit.GlobalRotation.Y,
			CurrentHp = EcsWorld.Has<Health>(unit.Entity) ? EcsWorld.Get<Health>(unit.Entity).Current : 0f,
			MaxHp = EcsWorld.Has<Health>(unit.Entity) ? EcsWorld.Get<Health>(unit.Entity).Max : 0f,
			IsDead = EcsWorld.Has<Dead>(unit.Entity),
			IsBuilding = unit.IsBuilding,
			IsDetailed = isDetailed,
			Velocity = new Realm.Client.NetworkVector3(unit.Velocity)
		};
		snapshotUnits.Add(currentSnap);
		nextBaselineMap[unit.Entity.Id] = currentSnap;
	}

	public void QueueSpectatorDelayedPacket(int peerId, string functionName, object[] arguments)
	{
		double sendTime = (global::Godot.Time.GetTicksMsec() / 1000.0) + 300.0;
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

	public readonly struct ReadyPacket
	{
		public readonly int PeerId;
		public readonly string FunctionName;
		public readonly object[] Arguments;

		public ReadyPacket(int peerId, string functionName, object[] arguments)
		{
			PeerId = peerId;
			FunctionName = functionName;
			Arguments = arguments;
		}
	}

	public List<ReadyPacket> FlushReadySpectatorPackets()
	{
		if (_spectatorDelayedPackets.Count == 0) return new List<ReadyPacket>();

		double currentTime = global::Godot.Time.GetTicksMsec() / 1000.0;
		var disconnectedPeers = new List<int>();
		foreach (var key in _spectatorDelayedPackets.Keys)
		{
			if (Network.LobbyManager.Instance == null || !Network.LobbyManager.Instance.PlayerList.Exists(p => p.PeerId == key))
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
		if (Network.LobbyManager.Instance == null) return false;
		var p = Network.LobbyManager.Instance.PlayerList.Find(x => x.PeerId == peerId);
		return p != null && p.Team == "Spectator" && Network.LobbyManager.Instance.SpectatorDelay;
	}

	private void ClearUnitOrders(Entity entity)
	{
		ClearMovementOrders(entity);
		ClearActionOrders(entity);
	}

	private void ClearMovementOrders(Entity entity)
	{
		if (EcsWorld.Has<MoveTo>(entity)) EcsWorld.Remove<MoveTo>(entity);
		if (EcsWorld.Has<PathFollow>(entity)) EcsWorld.Remove<PathFollow>(entity);
		if (EcsWorld.Has<Realm.Ecs.Components.Movement.HoldPosition>(entity)) EcsWorld.Remove<Realm.Ecs.Components.Movement.HoldPosition>(entity);
		if (EcsWorld.Has<WaypointQueue>(entity)) EcsWorld.Remove<WaypointQueue>(entity);
		if (EcsWorld.Has<Realm.Ecs.Components.Movement.Follow>(entity)) EcsWorld.Remove<Realm.Ecs.Components.Movement.Follow>(entity);
	}

	private void ClearActionOrders(Entity entity)
	{
		if (EcsWorld.Has<AttackTarget>(entity)) EcsWorld.Remove<AttackTarget>(entity);
		if (EcsWorld.Has<Realm.Ecs.Components.Movement.AttackMove>(entity)) EcsWorld.Remove<Realm.Ecs.Components.Movement.AttackMove>(entity);
		if (EcsWorld.Has<Patrol>(entity)) EcsWorld.Remove<Patrol>(entity);
		if (EcsWorld.Has<HealingTarget>(entity)) EcsWorld.Remove<HealingTarget>(entity);
		if (EcsWorld.Has<Gatherer>(entity)) EcsWorld.Remove<Gatherer>(entity);
	}

	private Entity FindWorldEntity()
	{
		Entity worldEntity = Entity.Null;
		var query = Realm.Ecs.Common.QueryCache.AllNetworkStateQuery;
		EcsWorld.Query(in query, (Entity entity) => worldEntity = entity);
		return worldEntity;
	}

	public bool WasClientInMultiplayer { get; private set; } = false;
	public bool IsConnectionLost { get; private set; } = false;

	public int LocalPeerId
	{
		get
		{
			var worldEntity = FindWorldEntity();
			return worldEntity != Entity.Null && EcsWorld.Has<NetworkState>(worldEntity)
				? EcsWorld.Get<NetworkState>(worldEntity).LocalPeerId
				: 1;
		}
		set
		{
			var worldEntity = FindWorldEntity();
			if (worldEntity != Entity.Null && EcsWorld.Has<NetworkState>(worldEntity))
			{
				ref var state = ref EcsWorld.Get<NetworkState>(worldEntity);
				state.LocalPeerId = value;
			}
		}
	}

	public float CommandSendTimer
	{
		get
		{
			var worldEntity = FindWorldEntity();
			return worldEntity != Entity.Null && EcsWorld.Has<NetworkState>(worldEntity)
				? EcsWorld.Get<NetworkState>(worldEntity).CommandSendTimer
				: 0f;
		}
		set
		{
			var worldEntity = FindWorldEntity();
			if (worldEntity != Entity.Null && EcsWorld.Has<NetworkState>(worldEntity))
			{
				ref var state = ref EcsWorld.Get<NetworkState>(worldEntity);
				state.CommandSendTimer = value;
			}
		}
	}

	public ulong LastSnapshotReceivedTime
	{
		get
		{
			var worldEntity = FindWorldEntity();
			return worldEntity != Entity.Null && EcsWorld.Has<NetworkState>(worldEntity)
				? EcsWorld.Get<NetworkState>(worldEntity).LastSnapshotReceivedTime
				: 0;
		}
		set
		{
			var worldEntity = FindWorldEntity();
			if (worldEntity != Entity.Null && EcsWorld.Has<NetworkState>(worldEntity))
			{
				ref var state = ref EcsWorld.Get<NetworkState>(worldEntity);
				state.LastSnapshotReceivedTime = value;
			}
		}
	}

	public void MarkClientEnteredMultiplayer()
	{
		WasClientInMultiplayer = true;
		LastSnapshotReceivedTime = global::Godot.Time.GetTicksMsec();
	}

	public void ResetReconnectionState()
	{
		_unacknowledgedCommands.Clear();
		_queuedDeltas.Clear();
		Entity worldEntity = FindWorldEntity();
		if (worldEntity != Entity.Null && EcsWorld.Has<NetworkState>(worldEntity))
		{
			ref var ns = ref EcsWorld.Get<NetworkState>(worldEntity);
			ns.HasReceivedInitialBaseline = false;
			ns.LastReceivedBaselineSeq = -1;
			ns.LastAppliedSnapshotSequence = -1;
			ns.LastSnapshotReceivedTime = global::Godot.Time.GetTicksMsec();
		}
		IsConnectionLost = false;
	}

	public void UpdateConnectionStatus(bool multiplayerActive, bool isServer)
	{
		bool isLost;
		if (multiplayerActive && !isServer)
		{
			ulong now = global::Godot.Time.GetTicksMsec();
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

	public static bool ArePeersEnemies(int peerId1, int peerId2)
	{
		if (peerId1 == peerId2) return false;
		if (!HasActiveLobby()) return true;

		string t1 = GetTeamForPeer(peerId1, "Team 1");
		string t2 = GetTeamForPeer(peerId2, "Team 2");
		return t1 != t2;
	}

	public static bool ArePlayerIndicesEnemies(int playerIndex1, int playerIndex2)
	{
		if (playerIndex1 == playerIndex2) return false;
		if (!HasActiveLobby()) return playerIndex1 != playerIndex2;

		string t1 = GetTeamForPlayerIndex(playerIndex1, $"Team {playerIndex1 + 1}");
		string t2 = GetTeamForPlayerIndex(playerIndex2, $"Team {playerIndex2 + 1}");
		return t1 != t2;
	}

	private static bool HasActiveLobby()
	{
		if (Network.LobbyManager.Instance == null) return false;
		if (Network.LobbyManager.Instance.PlayerList == null) return false;
		return Network.LobbyManager.Instance.PlayerList.Count > 0;
	}

	private static string GetTeamForPeer(int peerId, string defaultTeam)
	{
		var p = Network.LobbyManager.Instance.PlayerList.Find(x => x.PeerId == peerId);
		if (p == null) return defaultTeam;
		return p.Team;
	}

	private static string GetTeamForPlayerIndex(int playerIndex, string defaultTeam)
	{
		var p = Network.LobbyManager.Instance.PlayerList.Find(x => x.Slot == playerIndex);
		if (p == null) return defaultTeam;
		return p.Team;
	}

	public static bool IsPlayerEnemy(int playerIndex, int localPlayerIndex = 0)
	{
		return ArePlayerIndicesEnemies(localPlayerIndex, playerIndex);
	}
}