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

/// <summary>gather orders and the resource a prop yields.</summary>
public partial class CommandAuthorityService
	{
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

		string? resType = GetPropResourceType(closestPropId);
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

			string definitionId = _context.World.TryGet<DefinitionId>(entity, out DefinitionId definition) ? definition.Value : string.Empty;
			if (!_context.MapRules.IsResourceWorkerUnit(definitionId))
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

	private string? GetPropResourceType(string propId)
	{
		return _context.MapRules.GetResourceTypeForNode(propId);
	}
}
