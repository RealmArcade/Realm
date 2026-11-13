using System.Collections.Generic;
using System.Numerics;
using Realm.Ecs;
using Realm.Ecs.Components.Core;
using Realm.Ecs.Components.Movement;
using Realm.Ecs.Components.Resources;
using Realm.Ecs.Utils;
using Realm.Shared.Network;

namespace Realm.Shared.Simulation;

/// <summary>drop orders: a unit walking an item it carries to a spot and putting it down there.</summary>
public partial class CommandAuthorityService
{
	private void HandleDropItemCommand(int peerId, NetworkCommand cmd, List<int> rejectedServerEntityIds, ref string rejectionReason)
	{
		DropItemArgument.Parse(cmd.ArgString, out string itemId, out int charges);
		var targetPosition = new Vector3(cmd.TargetPosition.X, cmd.TargetPosition.Y, cmd.TargetPosition.Z);

		if (itemId.Length == 0)
		{
			const string noItemReason = "no item was named";
			for (int index = 0; index < cmd.UnitEntityIds.Count; index++)
			{
				RecordCommandRejection(rejectedServerEntityIds, cmd.UnitEntityIds[index], noItemReason);
			}

			if (string.IsNullOrEmpty(rejectionReason)) rejectionReason = noItemReason;
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

			if (RejectsPlayerOrder(entity, serverId, rejectedServerEntityIds, ref rejectionReason)) continue;

			if (!_context.World.Has<Inventory>(entity) || !_context.World.Get<Inventory>(entity).HasItem(itemId))
			{
				RecordCommandRejection(rejectedServerEntityIds, serverId, $"entity {serverId} does not carry that item");
				if (string.IsNullOrEmpty(rejectionReason)) rejectionReason = $"entity {serverId} does not carry that item";
				continue;
			}

			ApplyDropOrder(entity, itemId, charges, targetPosition);
		}
	}

	private void ApplyDropOrder(Entity entity, string itemId, int charges, Vector3 targetPosition)
	{
		ClearUnitOrders(entity);
		_context.World.SetOrAdd(entity, new DropItemTask(itemId, charges, targetPosition));
		_context.World.SetOrAdd(entity, new MoveTo(targetPosition));
	}
}
