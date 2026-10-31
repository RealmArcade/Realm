using System;
using System.Collections.Generic;
using System.Numerics;
using Realm.Ecs;
using Realm.Ecs.Common;
using Realm.Ecs.Components.Combat;
using Realm.Ecs.Components.Core;
using Realm.Ecs.Components.Meta;
using Realm.Ecs.Components.Movement;
using Realm.Ecs.Components.Resources;
using Realm.Ecs.Components.Tags;
using Realm.Ecs.Components.Terrain;
using Realm.Ecs.Services;
using Realm.Ecs.Utils;
using Realm.Shared.Metadata;
using Realm.Shared.Network;

namespace Realm.Shared.Simulation;

public class CommandAuthorityService
{
	private readonly SimulationContext _context;
	private readonly CommandTargetResolver _targets;

	public Func<string, AbilityRules?>? AbilityRulesProvider { get; set; }

	public Func<string, ProductionCosts?>? ProductionCostsProvider { get; set; }

	public Func<string, UpgradeRules?>? UpgradeRulesProvider { get; set; }

	public Func<int, string, int>? TechLevelReader { get; set; }

	public Action<int, string, int>? TechLevelWriter { get; set; }

	public Func<string, ConsumableMetadata?>? ConsumableProvider { get; set; }

	public Action<string>? DroppedOrderSink { get; set; }

	public Action<int, float>? CostsPaidSink { get; set; }

	public CommandTargetResolver Targets => _targets;

	public CommandAuthorityService(SimulationContext context)
	{
		_context = context;
		_targets = new CommandTargetResolver(context);
	}

	private bool TryGetAbilityRules(string? spellId, out AbilityRules rules)
	{
		rules = default;
		if (string.IsNullOrEmpty(spellId)) return false;
		if (AbilityRulesProvider == null) return false;

		AbilityRules? resolved = AbilityRulesProvider(spellId);
		if (resolved == null) return false;

		rules = resolved.Value;
		return true;
	}

public ServerCommandResult ExecuteServerCommand(int peerId, NetworkCommand cmd)
	{
		var stopVelocityEntityIds = new List<int>();
		var holdVelocityEntityIds = new List<int>();
		var rejectedServerEntityIds = new List<int>();
		string rejectionReason = string.Empty;

		DispatchServerCommand(peerId, cmd, stopVelocityEntityIds, holdVelocityEntityIds, rejectedServerEntityIds, ref rejectionReason);

		bool needsBuildUnit = cmd.CommandType == "build";
		string buildUnitType = needsBuildUnit ? cmd.ArgString : null;
		Vector3 buildPosition = needsBuildUnit ? new Vector3(cmd.TargetPosition.X, cmd.TargetPosition.Y, cmd.TargetPosition.Z) : Vector3.Zero;
		int buildPeerOwner = needsBuildUnit ? peerId : -1;
		int buildWorkerServerEntityId = needsBuildUnit && cmd.UnitEntityIds.Count > 0 ? cmd.UnitEntityIds[0] : -1;

		bool needsSpellEffect = cmd.CommandType == "spell";
		if (needsSpellEffect && !TryAuthorizeSpellCommand(peerId, cmd, out string spellRejection))
		{
			needsSpellEffect = false;
			if (string.IsNullOrEmpty(rejectionReason)) rejectionReason = spellRejection;
			if (cmd.UnitEntityIds.Count > 0) RecordCommandRejection(rejectedServerEntityIds, cmd.UnitEntityIds[0], spellRejection);
		}

		string spellId = needsSpellEffect ? cmd.ArgString : null;
		Vector3 spellPosition = needsSpellEffect ? new Vector3(cmd.TargetPosition.X, cmd.TargetPosition.Y, cmd.TargetPosition.Z) : Vector3.Zero;

		return new ServerCommandResult(
			needsBuildUnit, buildUnitType, buildPosition, buildPeerOwner, buildWorkerServerEntityId,
			needsSpellEffect, spellId, spellPosition,
			stopVelocityEntityIds, holdVelocityEntityIds,
			rejectedServerEntityIds, rejectionReason);
	}

	public bool TryGetPlayerEntityForPeer(int peerId, out Entity playerEntity)
	{
		playerEntity = Entity.Null;
		Entity worldEntity = _context.WorldEntity;
		if (worldEntity == Entity.Null || !_context.World.Has<NetworkMappingState>(worldEntity)) return false;
		var mapping = _context.World.Get<NetworkMappingState>(worldEntity);
		if (!mapping.PeerIdToPlayerEntityMap.TryGetValue(peerId, out playerEntity)) return false;
		return playerEntity != Entity.Null && _context.World.IsAlive(playerEntity);
	}

	public bool ValidateSpellCommandForHarness(int peerId, NetworkCommand cmd, out string rejectionReason)
	{
		return ValidateSpellCommand(peerId, cmd, out _, out rejectionReason);
	}

	private bool TryAuthorizeSpellCommand(int peerId, NetworkCommand cmd, out string rejectionReason)
	{
		if (!ValidateSpellCommand(peerId, cmd, out Entity casterEntity, out rejectionReason)) return false;

		string spellId = cmd.ArgString ?? string.Empty;
		if (!TryGetAbilityRules(spellId, out AbilityRules definition)) return false;

		if (!TryGetPlayerEntityForPeer(peerId, out Entity playerEntity)) return false;

		if (_context.UnlimitedPowerEnabled) return true;

		if (definition.ManaCost > 0f && _context.World.Has<Mana>(casterEntity))
		{
			ref var mana = ref _context.World.Get<Mana>(casterEntity);
			mana.Current -= definition.ManaCost;
		}

		if (definition.Cooldown > 0f && _context.World.Has<SpellCooldowns>(playerEntity))
		{
			ref var cooldowns = ref _context.World.Get<SpellCooldowns>(playerEntity);
			cooldowns.Set(spellId, definition.Cooldown, definition.Cooldown);
		}

		return true;
	}

	private bool ValidateSpellCommand(int peerId, NetworkCommand cmd, out Entity casterEntity, out string rejectionReason)
	{
		casterEntity = Entity.Null;
		rejectionReason = string.Empty;

		if (cmd.UnitEntityIds == null || cmd.UnitEntityIds.Count == 0)
		{
			rejectionReason = "spell command does not name a caster";
			return false;
		}

		string spellId = cmd.ArgString ?? string.Empty;
		if (!TryGetAbilityRules(spellId, out AbilityRules definition))
		{
			rejectionReason = $"spell '{spellId}' is not defined";
			return false;
		}

		casterEntity = _targets.Resolve(cmd.UnitEntityIds[0]);
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

		if (!_context.UnlimitedPowerEnabled && definition.Cooldown > 0f && _context.World.Has<SpellCooldowns>(playerEntity))
		{
			ref var cooldowns = ref _context.World.Get<SpellCooldowns>(playerEntity);
			if (cooldowns.TryGetValue(spellId, out float remaining) && remaining > 0f)
			{
				rejectionReason = $"spell '{spellId}' is on cooldown for {remaining:F1} s";
				return false;
			}
		}

		if (!_context.UnlimitedPowerEnabled && definition.ManaCost > 0f && _context.World.Has<Mana>(casterEntity))
		{
			ref var mana = ref _context.World.Get<Mana>(casterEntity);
			if (mana.Current < definition.ManaCost)
			{
				rejectionReason = $"caster needs {definition.ManaCost:F0} mana and has {mana.Current:F0}";
				return false;
			}
		}

		if (definition.TargetRange > 0f && _context.World.Has<Position>(casterEntity))
		{
			System.Numerics.Vector3 casterPosition = _context.World.Get<Position>(casterEntity).Value;
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

	private void DispatchServerCommand(int peerId, NetworkCommand cmd, List<int> stopVelocityEntityIds, List<int> holdVelocityEntityIds, List<int> rejectedServerEntityIds, ref string rejectionReason)
	{
		if (DispatchMovementCommand(peerId, cmd, stopVelocityEntityIds, holdVelocityEntityIds, rejectedServerEntityIds, ref rejectionReason)) return;
		if (DispatchActionCommand(peerId, cmd, rejectedServerEntityIds, ref rejectionReason)) return;
		if (DispatchTrainingCommand(peerId, cmd, rejectedServerEntityIds, ref rejectionReason)) return;
		if (DispatchResearchCommand(peerId, cmd, ref rejectionReason)) return;
		if (DispatchConsumableCommand(peerId, cmd, ref rejectionReason)) return;
		DispatchCheatCommand(peerId, cmd, ref rejectionReason);
	}

	private bool DispatchMovementCommand(int peerId, NetworkCommand cmd, List<int> stopVelocityEntityIds, List<int> holdVelocityEntityIds, List<int> rejectedServerEntityIds, ref string rejectionReason)
	{
		switch (cmd.CommandType)
		{
			case "move":
				HandleMoveCommand(peerId, cmd, rejectedServerEntityIds, ref rejectionReason);
				return true;
			case "move_queued":
				HandleMoveQueuedCommand(peerId, cmd, rejectedServerEntityIds, ref rejectionReason);
				return true;
			case "attack_move":
				HandleAttackMoveCommand(peerId, cmd, false, rejectedServerEntityIds, ref rejectionReason);
				return true;
			case "attack_move_queued":
				HandleAttackMoveCommand(peerId, cmd, true, rejectedServerEntityIds, ref rejectionReason);
				return true;
			case "stop":
				HandleStopCommand(peerId, cmd, stopVelocityEntityIds, rejectedServerEntityIds, ref rejectionReason);
				return true;
			case "hold":
				HandleHoldCommand(peerId, cmd, holdVelocityEntityIds, rejectedServerEntityIds, ref rejectionReason);
				return true;
			case "patrol":
			case "patrol_queued":
				HandlePatrolCommand(peerId, cmd, cmd.CommandType == "patrol_queued", rejectedServerEntityIds, ref rejectionReason);
				return true;
		}

		return false;
	}

	private bool DispatchActionCommand(int peerId, NetworkCommand cmd, List<int> rejectedServerEntityIds, ref string rejectionReason)
	{
		bool isQueued = cmd.CommandType.EndsWith("_queued");
		switch (cmd.CommandType)
		{
			case "attack":
			case "attack_queued":
				HandleAttackCommand(peerId, cmd, isQueued, rejectedServerEntityIds, ref rejectionReason);
				return true;
			case "follow":
			case "follow_queued":
				HandleFollowCommand(peerId, cmd, isQueued, rejectedServerEntityIds, ref rejectionReason);
				return true;
			case "gather":
			case "gather_queued":
				HandleGatherCommand(peerId, cmd, isQueued, rejectedServerEntityIds, ref rejectionReason);
				return true;
		}

		return false;
	}

	private bool DispatchTrainingCommand(int peerId, NetworkCommand cmd, List<int> rejectedServerEntityIds, ref string rejectionReason)
	{
		switch (cmd.CommandType)
		{
			case "train":
				HandleTrainCommand(peerId, cmd, rejectedServerEntityIds, ref rejectionReason);
				return true;
			case "cancel_train":
				HandleCancelTrainCommand(peerId, cmd);
				return true;
		}

		return false;
	}

	private bool DispatchResearchCommand(int peerId, NetworkCommand cmd, ref string rejectionReason)
	{
		if (cmd.CommandType != "research") return false;

		HandleResearchCommand(peerId, cmd, ref rejectionReason);
		return true;
	}

	private struct GroupMovementContext
	{
		public System.Numerics.Vector3 MoveDir;
		public System.Numerics.Vector3 Right;
		public int Cols;
		public float Spacing;
	}

	private GroupMovementContext CreateMovementContext(int peerId, NetworkCommand cmd)
	{
		CalculateGroupMovement(peerId, cmd.UnitEntityIds, cmd.TargetPosition, out System.Numerics.Vector3 moveDir, out System.Numerics.Vector3 right);
		return new GroupMovementContext
		{
			MoveDir = moveDir,
			Right = right,
			Cols = (int)MathF.Ceiling(MathF.Sqrt(cmd.UnitEntityIds.Count)),
			Spacing = 2.2f
		};
	}

	private delegate void GroupCommandAction(Entity entity, System.Numerics.Vector3 scattered);

	private void ProcessGroupMovementCommand(int peerId, NetworkCommand cmd, GroupCommandAction processAction, List<int> rejectedServerEntityIds, ref string rejectionReason, bool requireMovable = true)
	{
		var ctx = CreateMovementContext(peerId, cmd);
		int unitIndex = 0;
		string? rejection = null;

		foreach (int serverId in cmd.UnitEntityIds)
		{
			var entity = _targets.Resolve(serverId);
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

			if (requireMovable && !_context.World.Has<Movable>(entity))
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
			DroppedOrderSink?.Invoke($"[Network] Dropped '{cmd.CommandType}' from peer {peerId}: none of its {cmd.UnitEntityIds.Count} units could take the order ({rejection}).");
		}
	}

	private static void RecordCommandRejection(List<int> rejectedServerEntityIds, int serverId, string reason)
	{
		if (!rejectedServerEntityIds.Contains(serverId)) rejectedServerEntityIds.Add(serverId);
		_ = reason;
	}

	private void HandleMoveCommand(int peerId, NetworkCommand cmd, List<int> rejectedServerEntityIds, ref string rejectionReason)
	{
		ProcessGroupMovementCommand(peerId, cmd, (entity, scattered) =>
		{
			ClearUnitOrders(entity);
			var moveTo = new MoveTo(scattered);
			_context.World.SetOrAdd(entity, moveTo);
		}, rejectedServerEntityIds, ref rejectionReason, requireMovable: false);
	}

	private void HandleMoveQueuedCommand(int peerId, NetworkCommand cmd, List<int> rejectedServerEntityIds, ref string rejectionReason)
	{
		ProcessGroupMovementCommand(peerId, cmd, (entity, scattered) =>
		{
			ProcessQueuedMoveForEntity(entity, scattered);
		}, rejectedServerEntityIds, ref rejectionReason);
	}

	private void HandleAttackMoveCommand(int peerId, NetworkCommand cmd, bool isQueued, List<int> rejectedServerEntityIds, ref string rejectionReason)
	{
		ProcessGroupMovementCommand(peerId, cmd, (entity, scattered) =>
		{
			if (isQueued && UnitActivityHelper.IsUnitActive(_context.World, entity))
			{
				EnqueueCommand(entity, "attackmove", scattered);
				return;
			}

			ClearUnitOrders(entity);
			_context.World.SetOrAdd(entity, new AttackMove(scattered));
			_context.World.SetOrAdd(entity, new MoveTo(scattered));
		}, rejectedServerEntityIds, ref rejectionReason);
	}

	private void ProcessQueuedMoveForEntity(Entity entity, System.Numerics.Vector3 scattered)
	{
		bool hasNonMoveTasks = _context.World.Has<BuildTask>(entity) ||
		                       _context.World.Has<AttackTarget>(entity) ||
		                       _context.World.Has<AttackMove>(entity) ||
		                       _context.World.Has<Follow>(entity) ||
		                       _context.World.Has<Patrol>(entity) ||
		                       _context.World.Has<Gatherer>(entity);

		if (hasNonMoveTasks)
		{
			EnqueueCommand(entity, "move", scattered);
			return;
		}

		if (_context.World.Has<MoveTo>(entity))
		{
			if (_context.World.Has<WaypointQueue>(entity))
			{
				var q = _context.World.Get<WaypointQueue>(entity);
				q.Add(scattered);
				_context.World.Set(entity, q);
			}
			else
			{
				_context.World.Add(entity, new WaypointQueue(scattered));
			}
		}
		else
		{
			ClearUnitOrders(entity);
			_context.World.SetOrAdd(entity, new MoveTo(scattered));
		}
	}

	private void HandleTargetedCommand(int peerId, NetworkCommand cmd, bool isQueued, List<int> rejectedServerEntityIds, ref string rejectionReason, Action<Entity, Entity, System.Numerics.Vector3, bool> applyAction)
	{
		var targetEntity = _targets.Resolve(cmd.TargetEntityId);
		if (targetEntity == Entity.Null)
		{
			for (int index = 0; index < cmd.UnitEntityIds.Count; index++)
			{
				RecordCommandRejection(rejectedServerEntityIds, cmd.UnitEntityIds[index], "target entity is not on the host");
			}

			if (string.IsNullOrEmpty(rejectionReason)) rejectionReason = $"target entity {cmd.TargetEntityId} is not on the host";
			return;
		}

		var targetPos = _context.World.Has<Position>(targetEntity) ? _context.World.Get<Position>(targetEntity).Value : System.Numerics.Vector3.Zero;
		foreach (int serverId in cmd.UnitEntityIds)
		{
			var entity = _targets.Resolve(serverId);
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

	private void HandleAttackCommand(int peerId, NetworkCommand cmd, bool isQueued, List<int> rejectedServerEntityIds, ref string rejectionReason)
	{
		HandleTargetedCommand(peerId, cmd, isQueued, rejectedServerEntityIds, ref rejectionReason, (entity, targetEntity, targetPos, queued) =>
		{
			if (queued && UnitActivityHelper.IsUnitActive(_context.World, entity))
			{
				EnqueueCommand(entity, "attack", targetPos, targetEntity);
			}
			else
			{
				if (!queued) ClearUnitOrders(entity);
				_context.World.SetOrAdd(entity, new AttackTarget(targetEntity));
			}
		});
	}

	private void HandleFollowCommand(int peerId, NetworkCommand cmd, bool isQueued, List<int> rejectedServerEntityIds, ref string rejectionReason)
	{
		HandleTargetedCommand(peerId, cmd, isQueued, rejectedServerEntityIds, ref rejectionReason, ApplyFollowOrder);
	}

	private void ApplyFollowOrder(Entity entity, Entity targetEntity, System.Numerics.Vector3 targetPos, bool isQueued)
	{
			if (isQueued && UnitActivityHelper.IsUnitActive(_context.World, entity))
		{
			EnqueueCommand(entity, "follow", targetPos, targetEntity);
			return;
		}

		if (!isQueued) ClearUnitOrders(entity);

		if (_context.World.Has<Movable>(entity))
		{
			_context.World.SetOrAdd(entity, new Follow(targetEntity));
		}
	}

	private void HandleGatherCommand(int peerId, NetworkCommand cmd, bool isQueued, List<int> rejectedServerEntityIds, ref string rejectionReason)
	{
		var targetPos = new Vector3(cmd.TargetPosition.X, cmd.TargetPosition.Y, cmd.TargetPosition.Z);
		if (!_targets.TryFindClosestProp(targetPos, "", out Entity closestPropEntity, out string closestPropId, out Vector3 closestPropPos))
		{
			for (int index = 0; index < cmd.UnitEntityIds.Count; index++)
			{
				RecordCommandRejection(rejectedServerEntityIds, cmd.UnitEntityIds[index], "no resource node near the target");
			}

			if (string.IsNullOrEmpty(rejectionReason)) rejectionReason = "no resource node near the target";
			return;
		}

		string resType = GetPropResourceType(closestPropId);
		if (resType == null)
		{
			for (int index = 0; index < cmd.UnitEntityIds.Count; index++)
			{
				RecordCommandRejection(rejectedServerEntityIds, cmd.UnitEntityIds[index], "the resource node is not harvestable");
			}

			if (string.IsNullOrEmpty(rejectionReason)) rejectionReason = "the resource node is not harvestable";
			return;
		}

		Vector3 propPos = closestPropPos;
		foreach (int serverId in cmd.UnitEntityIds)
		{
			var entity = _targets.Resolve(serverId);
			if (entity == Entity.Null || !IsClientAuthorized(peerId, entity))
			{
				RecordCommandRejection(rejectedServerEntityIds, serverId, $"entity {serverId} is not owned by peer {peerId}");
				if (string.IsNullOrEmpty(rejectionReason)) rejectionReason = $"entity {serverId} is not owned by peer {peerId}";
				continue;
			}

			if (_context.World.Has<DefinitionId>(entity) && _context.World.Get<DefinitionId>(entity).Value != "worker")
			{
				RecordCommandRejection(rejectedServerEntityIds, serverId, $"entity {serverId} cannot gather");
				if (string.IsNullOrEmpty(rejectionReason)) rejectionReason = $"entity {serverId} cannot gather";
				continue;
			}

			ApplyGatherOrder(entity, closestPropEntity, propPos, resType, isQueued);
		}
	}

	private void ApplyGatherOrder(Entity entity, Entity propEntity, System.Numerics.Vector3 propPos, string resType, bool isQueued)
	{
			if (isQueued && UnitActivityHelper.IsUnitActive(_context.World, entity))
		{
			EnqueueCommand(entity, "gather", propPos, propEntity);
			return;
		}

		if (!isQueued) ClearUnitOrders(entity);
		_context.World.SetOrAdd(entity, new Gatherer(resType, propEntity));
		_context.World.SetOrAdd(entity, new MoveTo(propPos));
	}

	private string GetPropResourceType(string propId)
	{
		return _context.MapRules.GetResourceTypeForNode(propId);
	}

	private void HandleStopCommand(int peerId, NetworkCommand cmd, List<int> stopVelocityEntityIds, List<int> rejectedServerEntityIds, ref string rejectionReason)
	{
		foreach (int serverId in cmd.UnitEntityIds)
		{
			var entity = _targets.Resolve(serverId);
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

	private void HandleHoldCommand(int peerId, NetworkCommand cmd, List<int> holdVelocityEntityIds, List<int> rejectedServerEntityIds, ref string rejectionReason)
	{
		foreach (int serverId in cmd.UnitEntityIds)
		{
			var entity = _targets.Resolve(serverId);
			if (entity == Entity.Null || !IsClientAuthorized(peerId, entity))
			{
				RecordCommandRejection(rejectedServerEntityIds, serverId, $"entity {serverId} is not owned by peer {peerId}");
				if (string.IsNullOrEmpty(rejectionReason)) rejectionReason = $"entity {serverId} is not owned by peer {peerId}";
				continue;
			}

			ClearUnitOrders(entity);
			holdVelocityEntityIds.Add(serverId);
			if (!_context.World.Has<HoldPosition>(entity))
			{
				_context.World.Add<HoldPosition>(entity);
			}
		}
	}

	private void HandlePatrolCommand(int peerId, NetworkCommand cmd, bool isQueued, List<int> rejectedServerEntityIds, ref string rejectionReason)
	{
		ProcessGroupMovementCommand(peerId, cmd, (entity, scattered) =>
		{
			var unitPos = GetUnitPosition(entity);
			var patrolA = new System.Numerics.Vector3(unitPos.X, unitPos.Y, unitPos.Z);

				if (isQueued && UnitActivityHelper.IsUnitActive(_context.World, entity))
			{
				EnqueueCommand(entity, "patrol", scattered);
			}
			else
			{
				if (!isQueued) ClearUnitOrders(entity);
				_context.World.SetOrAdd(entity, new Patrol(patrolA, scattered));
				_context.World.SetOrAdd(entity, new MoveTo(scattered));
			}
		}, rejectedServerEntityIds, ref rejectionReason);
	}

	private Vector3 GetUnitPosition(Entity entity)
	{
		return _context.World.Has<Position>(entity) ? _context.World.Get<Position>(entity).Value : Vector3.Zero;
	}

	private void HandleTrainCommand(int peerId, NetworkCommand cmd, List<int> rejectedServerEntityIds, ref string rejectionReason)
	{
		string unitId = cmd.ArgString;
		if (!TryGetProductionCosts(unitId, out ProductionCosts costs))
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
			var entity = _targets.Resolve(serverId);
			if (entity == Entity.Null || !IsClientAuthorized(peerId, entity))
			{
				RecordCommandRejection(rejectedServerEntityIds, serverId, $"entity {serverId} is not owned by peer {peerId}");
				if (string.IsNullOrEmpty(rejectionReason)) rejectionReason = $"entity {serverId} is not owned by peer {peerId}";
				continue;
			}

			var ownerComp = _context.World.Get<Owner>(entity);
			var ownerEntity = ownerComp.PlayerEntity.Value;
			if (!_context.World.TryGet<PlayerResources>(ownerEntity, out var res))
			{
				RecordCommandRejection(rejectedServerEntityIds, serverId, "the owning player has no resource pool");
				if (string.IsNullOrEmpty(rejectionReason)) rejectionReason = "the owning player has no resource pool";
				continue;
			}

			if (!TryQueueProduction(entity, ownerEntity, unitId, costs, ref res))
			{
				RecordCommandRejection(rejectedServerEntityIds, serverId, "not enough resources");
				if (string.IsNullOrEmpty(rejectionReason)) rejectionReason = "not enough resources";
			}
		}
	}

	private bool TryGetProductionCosts(string? unitId, out ProductionCosts costs)
	{
		costs = default;
		if (string.IsNullOrEmpty(unitId)) return false;
		if (ProductionCostsProvider == null) return false;

		ProductionCosts? resolved = ProductionCostsProvider(unitId);
		if (resolved == null) return false;

		costs = resolved.Value;
		return true;
	}

	private bool TryQueueProduction(Entity entity, Entity ownerEntity, string unitId, ProductionCosts costs, ref PlayerResources res)
	{
		var goldResourceId = new ResourceId("Gold");
		var woodResourceId = new ResourceId("Wood");
		var stoneResourceId = new ResourceId("Stone");

		int costGold = (int)costs.Gold;
		int costWood = (int)costs.Wood;
		int costStone = (int)costs.Stone;

		if (res[goldResourceId] < costGold || res[woodResourceId] < costWood || res[stoneResourceId] < costStone)
			return false;

		res[goldResourceId] -= costGold;
		res[woodResourceId] -= costWood;
		res[stoneResourceId] -= costStone;
		_context.World.Set(ownerEntity, res);

		if (!_context.World.Has<ProductionQueue>(entity)) _context.World.Add(entity, new ProductionQueue());

		ref var prod = ref _context.World.Get<ProductionQueue>(entity);
		prod.Enqueue(unitId);
		if (prod.Count == 1)
		{
			prod.BuildTime = costs.ProductionTime;
			prod.CurrentProgress = 0f;
		}

		return true;
	}

	private void HandleCancelTrainCommand(int peerId, NetworkCommand cmd)
	{
		foreach (int serverId in cmd.UnitEntityIds)
		{
			var entity = _targets.Resolve(serverId);
			if (entity == Entity.Null || !IsClientAuthorized(peerId, entity)) continue;
			if (!_context.World.Has<ProductionQueue>(entity)) continue;

			CancelProductionQueueItem(entity, cmd.TargetEntityId);
		}
	}

	private void CancelProductionQueueItem(Entity entity, int idx)
	{
		ref var prod = ref _context.World.Get<ProductionQueue>(entity);
		if (idx < 0 || idx >= prod.Count) return;

		string? unitId = prod.RemoveAt(idx);

		if (idx == 0)
		{
			prod.CurrentProgress = 0f;
			string? nextUnitId = prod.Get(0);
			if (TryGetProductionCosts(nextUnitId, out ProductionCosts nextCosts))
			{
				prod.BuildTime = nextCosts.ProductionTime;
			}
		}

		if (TryGetProductionCosts(unitId, out ProductionCosts refunded)) RefundCanceledProduction(entity, refunded);
	}

	private void RefundCanceledProduction(Entity entity, ProductionCosts refunded)
	{
		var ownerComp = _context.World.Get<Owner>(entity);
		var ownerEntity = ownerComp.PlayerEntity.Value;

		if (_context.World.TryGet<PlayerResources>(ownerEntity, out var res))
		{
			res[new ResourceId("Gold")] += (int)refunded.Gold;
			res[new ResourceId("Wood")] += (int)refunded.Wood;
			res[new ResourceId("Stone")] += (int)refunded.Stone;
			_context.World.Set(ownerEntity, res);
		}

	}

	private void HandleResearchCommand(int peerId, NetworkCommand cmd, ref string rejectionReason)
	{
		string requestedId = cmd.ArgString ?? string.Empty;
		if (string.IsNullOrEmpty(requestedId))
		{
			rejectionReason = "a research order names no technology";
			return;
		}

		if (UpgradeRulesProvider == null || TechLevelReader == null || TechLevelWriter == null)
		{
			rejectionReason = $"'{requestedId}' cannot be researched in this match";
			return;
		}

		UpgradeRules? resolved = UpgradeRulesProvider(requestedId);
		if (resolved == null)
		{
			rejectionReason = $"the map declares no technology '{requestedId}'";
			return;
		}

		UpgradeRules rules = resolved.Value;
		if (!TryGetPlayerEntityForPeer(peerId, out Entity playerEntity))
		{
			rejectionReason = $"peer {peerId} has no player entity";
			return;
		}

		int playerIndex = _context.PlayerSlotForEntity(playerEntity);
		int currentLevel = TechLevelReader(playerIndex, rules.CanonicalId);
		if (currentLevel >= rules.MaxLevel)
		{
			rejectionReason = $"technology '{rules.CanonicalId}' is already at its last level ({currentLevel})";
			return;
		}

		if (!TryPayCosts(playerEntity, rules.Gold, rules.Wood, rules.Stone))
		{
			rejectionReason = $"technology '{rules.CanonicalId}' costs more than the player has";
			return;
		}

		TechLevelWriter(playerIndex, rules.CanonicalId, currentLevel + 1);
	}

	private bool TryPayCosts(Entity playerEntity, float gold, float wood, float stone)
	{
		if (!_context.World.TryGet<PlayerResources>(playerEntity, out PlayerResources resources)) return false;

		var goldResourceId = new ResourceId("Gold");
		var woodResourceId = new ResourceId("Wood");
		var stoneResourceId = new ResourceId("Stone");

		int costGold = (int)gold;
		int costWood = (int)wood;
		int costStone = (int)stone;

		if (resources[goldResourceId] < costGold || resources[woodResourceId] < costWood || resources[stoneResourceId] < costStone) return false;

		resources[goldResourceId] -= costGold;
		resources[woodResourceId] -= costWood;
		resources[stoneResourceId] -= costStone;
		_context.World.Set(playerEntity, resources);
		CostsPaidSink?.Invoke(_context.PlayerSlotForEntity(playerEntity), costGold);
		return true;
	}

	private bool DispatchCheatCommand(int peerId, NetworkCommand cmd, ref string rejectionReason)
	{
		if (cmd.CommandType != "cheat") return false;

		HandleCheatCommand(peerId, cmd, ref rejectionReason);
		return true;
	}

	private bool DispatchConsumableCommand(int peerId, NetworkCommand cmd, ref string rejectionReason)
	{
		if (cmd.CommandType != "use_consumable") return false;

		HandleConsumableCommand(peerId, cmd, ref rejectionReason);
		return true;
	}

	private void HandleConsumableCommand(int peerId, NetworkCommand cmd, ref string rejectionReason)
	{
		string cardId = cmd.ArgString ?? string.Empty;
		if (string.IsNullOrEmpty(cardId))
		{
			rejectionReason = "a purchase order names no consumable";
			return;
		}

		if (ConsumableProvider == null)
		{
			rejectionReason = $"nothing is sold as '{cardId}' in this match";
			return;
		}

		ConsumableMetadata? consumable = ConsumableProvider(cardId);
		if (consumable == null)
		{
			rejectionReason = $"the map sells no consumable '{cardId}'";
			return;
		}

		if (cmd.UnitEntityIds == null || cmd.UnitEntityIds.Count == 0)
		{
			rejectionReason = "a purchase order names no building to buy from";
			return;
		}

		Entity building = _targets.Resolve(cmd.UnitEntityIds[0]);
		if (building == Entity.Null || !IsClientAuthorized(peerId, building))
		{
			rejectionReason = $"building {cmd.UnitEntityIds[0]} is not owned by peer {peerId}";
			return;
		}

		if (!TryGetPlayerEntityForPeer(peerId, out Entity playerEntity))
		{
			rejectionReason = $"peer {peerId} has no player entity";
			return;
		}

		Entity target = FindConsumableTarget(building, playerEntity, consumable, cmd.TargetPosition.ToNumerics());
		if (target == Entity.Null)
		{
			rejectionReason = consumable.ClearsSlot
				? "nothing of the buyer's is close enough to purge"
				: "no unit of the buyer's is close enough to receive it";
			return;
		}

		bool carriesOne = CarriesAttachment(target, consumable);
		if (consumable.ClearsSlot && !carriesOne)
		{
			rejectionReason = "that unit is not carrying one";
			return;
		}

		if (!consumable.ClearsSlot && carriesOne)
		{
			rejectionReason = "that unit already carries one";
			return;
		}

		if (!TryPayCosts(playerEntity, consumable.CostGold, consumable.CostWood, consumable.CostStone))
		{
			rejectionReason = $"'{consumable.TemplateID}' costs more than the player has";
			return;
		}

		if (consumable.ClearsSlot)
		{
			_context.World.Remove<ConsumableAttachment>(target);
			return;
		}

		_context.World.SetOrAdd(target, new ConsumableAttachment(consumable.TemplateID, consumable.SlotGroup, _context.PlayerSlotForEntity(playerEntity)));
	}

	private Entity FindConsumableTarget(Entity building, Entity playerEntity, ConsumableMetadata consumable, Vector3 position)
	{
		bool ownOnly = !string.Equals(consumable.TargetScope, "AnyUnit", StringComparison.OrdinalIgnoreCase);
		float bestDistance = consumable.PurchaseRange;
		Entity best = Entity.Null;

		var query = QueryCache.AllPositionAndOwnerNoneDeadQuery;
		_context.World.Query(in query, (Entity candidate, ref Position candidatePosition, ref Owner owner) =>
		{
			if (candidate == building) return;
			if (ownOnly && owner.PlayerEntity.Value != playerEntity) return;

			float distance = (candidatePosition.Value - position).Length();
			if (distance > bestDistance) return;

			bestDistance = distance;
			best = candidate;
		});

		return best;
	}

	private bool CarriesAttachment(Entity unit, ConsumableMetadata consumable)
	{
		if (!_context.World.TryGet<ConsumableAttachment>(unit, out ConsumableAttachment attachment)) return false;
		if (!attachment.HasDefinition) return false;

		return attachment.IsInGroup(consumable.SlotGroup);
	}

	private void HandleCheatCommand(int peerId, NetworkCommand cmd, ref string rejectionReason)
	{
		string key = (cmd.ArgString ?? string.Empty).Trim().ToLowerInvariant();
		if (key.Length == 0)
		{
			rejectionReason = "a cheat order names no cheat";
			return;
		}

		if (!_context.CheatsEnabled)
		{
			rejectionReason = "this match does not allow cheats";
			return;
		}

		if (!TryGetPlayerEntityForPeer(peerId, out Entity playerEntity))
		{
			rejectionReason = $"peer {peerId} has no player entity";
			return;
		}

		switch (key)
		{
			case "securethebag":
				ApplySecureTheBag(playerEntity);
				break;
			case "gigachad":
				ApplyGigachad(playerEntity);
				break;
			case "needforspeed":
				ApplyNeedForSpeed(peerId, cmd);
				break;
			case "thanossnap":
				ApplyThanosSnap();
				break;
			case "ezclap":
				_context.IsGameOver = true;
				break;
			case "aura":
				ApplyAura();
				break;
			case "speedrun":
				_context.FastBuildEnabled = !_context.FastBuildEnabled;
				break;
			case "skibidi":
				ApplySkibidi(playerEntity);
				break;
			default:
				rejectionReason = $"'{key}' is not a cheat this build knows";
				break;
		}
	}

	private void ApplySecureTheBag(Entity playerEntity)
	{
		if (!_context.World.TryGet<PlayerResources>(playerEntity, out PlayerResources resources)) return;

		const float ceiling = 9999f;
		var goldResourceId = new ResourceId("Gold");
		var woodResourceId = new ResourceId("Wood");
		var stoneResourceId = new ResourceId("Stone");

		if (resources.TryGetValue(goldResourceId, out int gold)) resources[goldResourceId] = (int)Math.Min(ceiling, gold + 10000f);
		if (resources.TryGetValue(woodResourceId, out int wood)) resources[woodResourceId] = (int)Math.Min(ceiling, wood + 10000f);
		if (resources.TryGetValue(stoneResourceId, out int stone)) resources[stoneResourceId] = (int)Math.Min(ceiling, stone + 10000f);

		_context.World.Set(playerEntity, resources);
	}

	private void ApplyGigachad(Entity playerEntity)
	{
		var query = QueryCache.AllHealthAndOwnerQuery;
		_context.World.Query(in query, (Entity entity, ref Owner owner) =>
		{
			if (owner.PlayerEntity.Value != playerEntity) return;

			if (_context.World.TryGet<Health>(entity, out Health _)) _context.World.Set(entity, new Health(9000f, 9000f));
			if (_context.World.TryGet<Attack>(entity, out Attack attack)) _context.World.Set(entity, new Attack(9001f, attack.Range, attack.Cooldown, attack.CurrentCooldown));
		});
	}

	private void ApplyNeedForSpeed(int peerId, NetworkCommand cmd)
	{
		if (cmd.UnitEntityIds == null) return;

		foreach (int serverId in cmd.UnitEntityIds)
		{
			Entity entity = _targets.Resolve(serverId);
			if (entity == Entity.Null || !IsClientAuthorized(peerId, entity)) continue;
			if (!_context.World.TryGet<MovementStats>(entity, out MovementStats movement)) continue;

			_context.World.Set(entity, new MovementStats(25f, movement.Acceleration, movement.TurnRate));
		}
	}

	private void ApplyThanosSnap()
	{
		var targets = new List<Entity>();
		var query = QueryCache.AllHealthAndOwnerQuery;

		_context.World.Query(in query, (Entity entity, ref Owner owner) =>
		{
			Entity ownerEntity = owner.PlayerEntity.Value;
			if (!_context.World.IsAlive(ownerEntity)) return;
			if (!_context.World.TryGet<Name>(ownerEntity, out Name name)) return;
			if (!string.Equals(name.Value, "Enemy_AI", StringComparison.Ordinal)) return;

			targets.Add(entity);
		});

		for (int index = 0; index < targets.Count; index++)
		{
			Entity entity = targets[index];
			if (!_context.World.IsAlive(entity)) continue;

			if (_context.World.TryGet<Health>(entity, out Health health)) _context.World.Set(entity, new Health(0f, health.Max));
			if (!_context.World.Has<Dead>(entity)) _context.World.Add<Dead>(entity);
		}
	}

	private void ApplyAura()
	{
		Entity worldEntity = _context.WorldEntity;
		if (worldEntity == Entity.Null || !_context.World.IsAlive(worldEntity)) return;

		_context.World.Mutate<ShroudState>(worldEntity, (ref ShroudState shroud) => shroud.VisibilityMode = ShroudVisibilityMode.Revealed);
	}

	private void ApplySkibidi(Entity playerEntity)
	{
		_context.UnlimitedPowerEnabled = !_context.UnlimitedPowerEnabled;
		if (!_context.UnlimitedPowerEnabled) return;

		_context.World.Mutate<SpellCooldowns>(playerEntity, (ref SpellCooldowns cooldowns) => cooldowns.Clear());
	}

	private void CalculateGroupMovement(int peerId, List<int> unitEntityIds, NetworkVector3 targetPosition, out System.Numerics.Vector3 moveDir, out System.Numerics.Vector3 right)
	{
		System.Numerics.Vector3 groupCenter = System.Numerics.Vector3.Zero;
		int movableCount = 0;
		foreach (int serverId in unitEntityIds)
		{
			var entity = _targets.Resolve(serverId);
			if (entity == Entity.Null || !IsClientAuthorized(peerId, entity)) continue;
			if (_context.World.Has<Position>(entity))
			{
				groupCenter += _context.World.Get<Position>(entity).Value;
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
		if (!_context.World.Has<Owner>(unitEntity)) return false;
		var ownerComp = _context.World.Get<Owner>(unitEntity);
		Entity worldEntity = _context.WorldEntity;
		if (worldEntity == Entity.Null || !_context.World.Has<NetworkMappingState>(worldEntity)) return false;
		var mapping = _context.World.Get<NetworkMappingState>(worldEntity);
		if (mapping.PeerIdToPlayerEntityMap.TryGetValue(peerId, out var playerEntity))
		{
			return ownerComp.PlayerEntity.Value == playerEntity;
		}

		return false;
	}

private void EnqueueCommand(Entity entity, string type, System.Numerics.Vector3 position, Entity target = default)
	{
		if (!_context.World.Has<BuildQueue>(entity))
		{
			_context.World.Add(entity, new BuildQueue());
		}

		ref var q = ref _context.World.Get<BuildQueue>(entity);
		q.TryEnqueue(type, position, target);
	}

private void ClearUnitOrders(Entity entity)
	{
		ClearMovementOrders(entity);
		ClearActionOrders(entity);
	}

private void ClearMovementOrders(Entity entity)
	{
		if (_context.World.Has<MoveTo>(entity)) _context.World.Remove<MoveTo>(entity);
		if (_context.World.Has<PathFollow>(entity)) _context.World.Remove<PathFollow>(entity);
		if (_context.World.Has<HoldPosition>(entity)) _context.World.Remove<HoldPosition>(entity);
		if (_context.World.Has<WaypointQueue>(entity)) _context.World.Remove<WaypointQueue>(entity);
		if (_context.World.Has<Follow>(entity)) _context.World.Remove<Follow>(entity);
	}

private void ClearActionOrders(Entity entity)
	{
		if (_context.World.Has<AttackTarget>(entity)) _context.World.Remove<AttackTarget>(entity);
		if (_context.World.Has<AttackMove>(entity)) _context.World.Remove<AttackMove>(entity);
		if (_context.World.Has<Patrol>(entity)) _context.World.Remove<Patrol>(entity);
		if (_context.World.Has<HealingTarget>(entity)) _context.World.Remove<HealingTarget>(entity);
		if (_context.World.Has<Gatherer>(entity)) _context.World.Remove<Gatherer>(entity);
	}
}
