using System.Collections.Generic;
using System.Numerics;
using Realm.Ecs;
using Realm.Ecs.Components.Core;
using Realm.Ecs.Components.Movement;
using Realm.Ecs.Components.Resources;
using Realm.Ecs.Components.Tags;
using Realm.Ecs.Utils;
using Realm.Shared.Network;

namespace Realm.Shared.Simulation;

/// <summary>pickup orders and the ground item they collect.</summary>
public partial class CommandAuthorityService
{
	private void HandlePickupCommand(int peerId, NetworkCommand cmd, bool isQueued, List<int> rejectedServerEntityIds, ref string rejectionReason)
	{
		Entity lootEntity = _targets.Resolve(cmd.TargetEntityId);
		if (lootEntity == Entity.Null || !_context.World.Has<GroundLoot>(lootEntity))
		{
			const string goneReason = "the ground item is gone";
			for (int index = 0; index < cmd.UnitEntityIds.Count; index++)
			{
				RecordCommandRejection(rejectedServerEntityIds, cmd.UnitEntityIds[index], goneReason);
			}

			if (string.IsNullOrEmpty(rejectionReason)) rejectionReason = goneReason;
			return;
		}

		Vector3 lootPosition = _context.World.TryGet<Position>(lootEntity, out Position lootPositionComponent)
			? lootPositionComponent.Value
			: new Vector3(cmd.TargetPosition.X, cmd.TargetPosition.Y, cmd.TargetPosition.Z);

		foreach (int serverId in cmd.UnitEntityIds)
		{
			var entity = _targets.Resolve(serverId);
			if (entity == Entity.Null || !IsClientAuthorized(peerId, entity))
			{
				RecordCommandRejection(rejectedServerEntityIds, serverId, $"entity {serverId} is not owned by peer {peerId}");
				if (string.IsNullOrEmpty(rejectionReason)) rejectionReason = $"entity {serverId} is not owned by peer {peerId}";
				continue;
			}

			if (RejectsPlayerOrder(entity, serverId, rejectedServerEntityIds, ref rejectionReason)) continue;

			if (!_context.World.Has<Unit>(entity) || _context.World.Has<Building>(entity))
			{
				RecordCommandRejection(rejectedServerEntityIds, serverId, $"entity {serverId} cannot pick anything up");
				if (string.IsNullOrEmpty(rejectionReason)) rejectionReason = $"entity {serverId} cannot pick anything up";
				continue;
			}

			ApplyPickupOrder(entity, lootEntity, lootPosition, isQueued);
		}
	}

	private void ApplyPickupOrder(Entity entity, Entity lootEntity, Vector3 lootPosition, bool isQueued)
	{
		if (isQueued && UnitActivityHelper.IsUnitActive(_context.World, entity))
		{
			EnqueueCommand(entity, "pickup", lootPosition, lootEntity);
			return;
		}

		if (!isQueued) ClearUnitOrders(entity);
		_context.World.SetOrAdd(entity, new PickupTask(lootEntity));
		_context.World.SetOrAdd(entity, new MoveTo(lootPosition));
	}
}
