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

/// <summary>attack, follow and the orders that take a target.</summary>
public partial class CommandAuthorityService
	{
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
			if (RejectsPlayerOrder(entity, serverId, rejectedServerEntityIds, ref rejectionReason)) continue;

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
}
