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

/// <summary>stop, hold and patrol.</summary>
public partial class CommandAuthorityService
	{
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
}
