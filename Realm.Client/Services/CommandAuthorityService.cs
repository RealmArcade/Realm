using System;
using System.Collections.Generic;
using Godot;
using Realm.Client.Core;
using Realm.Client.Network;
using Realm.Client.UI;
using Realm.Ecs;
using Realm.Ecs.Common;
using Realm.Ecs.Components.Combat;
using Realm.Ecs.Components.Core;
using Realm.Ecs.Components.Meta;
using Realm.Ecs.Components.Movement;
using Realm.Ecs.Components.Resources;
using Realm.Ecs.Components.Tags;
using Realm.Ecs.Services;
using Realm.MapAPI;

namespace Realm.Client.Services;

public class CommandAuthorityService
{
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
		Entity worldEntity = ECSContext.WorldEntity;
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

		casterEntity = GameContext.NetworkService.GetSpellCasterEntityDomain(cmd, allUnits);
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
			var entity = GameContext.NetworkService.FindServerEntity(serverId, allUnits);
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
		var targetEntity = GameContext.NetworkService.FindServerEntity(cmd.TargetEntityId, allUnits);
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
			var entity = GameContext.NetworkService.FindServerEntity(serverId, allUnits);
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
		Prop3D prop = GameContext.NetworkService.FindClosestProp(targetPos, "", allProps);
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
			var entity = GameContext.NetworkService.FindServerEntity(serverId, allUnits);
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
			var entity = GameContext.NetworkService.FindServerEntity(serverId, allUnits);
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
			var entity = GameContext.NetworkService.FindServerEntity(serverId, allUnits);
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
			var entity = GameContext.NetworkService.FindServerEntity(serverId, allUnits);
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
			var entity = GameContext.NetworkService.FindServerEntity(serverId, allUnits);
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
			var entity = GameContext.NetworkService.FindServerEntity(serverId, allUnits);
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

public bool IsClientAuthorized(int peerId, Entity unitEntity)
	{
		if (!GameContext.World.Has<Owner>(unitEntity)) return false;
		var ownerComp = GameContext.World.Get<Owner>(unitEntity);
		Entity worldEntity = ECSContext.WorldEntity;
		if (worldEntity == Entity.Null || !GameContext.World.Has<NetworkMappingState>(worldEntity)) return false;
		var mapping = GameContext.World.Get<NetworkMappingState>(worldEntity);
		if (mapping.PeerIdToPlayerEntityMap.TryGetValue(peerId, out var playerEntity))
		{
			return ownerComp.PlayerEntity.Value == playerEntity;
		}

		return false;
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
}
