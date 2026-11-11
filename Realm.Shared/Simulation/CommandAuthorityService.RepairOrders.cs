using System.Collections.Generic;
using System.Numerics;
using Realm.Ecs;
using Realm.Ecs.Components.Core;
using Realm.Ecs.Components.Movement;
using Realm.Ecs.Components.Resources;
using Realm.Ecs.Components.Tags;
using Realm.Ecs.Services;
using Realm.Ecs.Utils;
using Realm.Shared.Network;

namespace Realm.Shared.Simulation;

/// <summary>repair orders: a unit the map lets work on buildings mending one or raising a site.</summary>
public partial class CommandAuthorityService
{
	private void HandleRepairCommand(int peerId, NetworkCommand cmd, bool isQueued, List<int> rejectedServerEntityIds, ref string rejectionReason)
	{
		Entity buildingEntity = _targets.Resolve(cmd.TargetEntityId);
		if (!IsWorkTarget(buildingEntity))
		{
			for (int index = 0; index < cmd.UnitEntityIds.Count; index++)
			{
				RecordCommandRejection(rejectedServerEntityIds, cmd.UnitEntityIds[index], "no building that can be repaired is there");
			}

			if (string.IsNullOrEmpty(rejectionReason)) rejectionReason = "no building that can be repaired is there";
			return;
		}

		Vector3 buildingPosition = _context.World.Get<Position>(buildingEntity).Value;
		int buildingPlayer = ReadPlayerIndex(buildingEntity);

		foreach (int serverId in cmd.UnitEntityIds)
		{
			Entity entity = _targets.Resolve(serverId);
			if (entity == Entity.Null || !IsClientAuthorized(peerId, entity))
			{
				RecordCommandRejection(rejectedServerEntityIds, serverId, $"entity {serverId} is not owned by peer {peerId}");
				if (string.IsNullOrEmpty(rejectionReason)) rejectionReason = $"entity {serverId} is not owned by peer {peerId}";
				continue;
			}

			if (RejectsPlayerOrder(entity, serverId, rejectedServerEntityIds, ref rejectionReason)) continue;

			if (!CanRepair(entity))
			{
				RecordCommandRejection(rejectedServerEntityIds, serverId, $"entity {serverId} cannot repair");
				if (string.IsNullOrEmpty(rejectionReason)) rejectionReason = $"entity {serverId} cannot repair";
				continue;
			}

			if (!AreAlliedPlayers(ReadPlayerIndex(entity), buildingPlayer))
			{
				RecordCommandRejection(rejectedServerEntityIds, serverId, $"entity {serverId} cannot repair a building that is not allied");
				if (string.IsNullOrEmpty(rejectionReason)) rejectionReason = $"entity {serverId} cannot repair a building that is not allied";
				continue;
			}

			ApplyRepairOrder(entity, buildingEntity, buildingPosition, isQueued);
		}
	}

	/// <summary>
	///     Answers whether a building is something a worker can be sent to work on: a site still being raised, or a standing
	///     building that has lost health. A building at full health is not a work target, so a click that reaches it is not a
	///     repair order.
	/// </summary>
	private bool IsWorkTarget(Entity buildingEntity)
	{
		if (buildingEntity == Entity.Null
		    || !_context.World.IsAlive(buildingEntity)
		    || !_context.World.Has<Building>(buildingEntity)
		    || !_context.World.Has<Position>(buildingEntity))
		{
			return false;
		}

		if (_context.World.Has<UnderConstruction>(buildingEntity)) return _context.World.Has<ConstructionState>(buildingEntity);
		if (!_context.World.Has<RepairableBuilding>(buildingEntity)) return false;

		return _context.World.TryGet<Health>(buildingEntity, out Health health) && health.Current < health.Max;
	}

	private void ApplyRepairOrder(Entity entity, Entity buildingEntity, Vector3 buildingPosition, bool isQueued)
	{
		if (isQueued && UnitActivityHelper.IsUnitActive(_context.World, entity))
		{
			EnqueueCommand(entity, "repair", buildingPosition, buildingEntity);
			return;
		}

		if (!isQueued) ClearUnitOrders(entity);

		if (_context.World.Has<UnderConstruction>(buildingEntity) && _context.World.TryGet<ConstructionState>(buildingEntity, out ConstructionState construction))
		{
			_context.World.SetOrAdd(entity, new BuildTask(buildingEntity, construction.TotalBuildTime));
		}
		else
		{
			_context.World.SetOrAdd(entity, new RepairTask(buildingEntity));
		}

		_context.World.SetOrAdd(entity, new MoveTo(buildingPosition));
	}

	/// <summary>
	///     Answers whether this unit may take a repair order at all: the map has to have given it a repair rate, which is
	///     what makes working on buildings a job rather than something any unit can do.
	/// </summary>
	private bool CanRepair(Entity entity)
	{
		return _context.World.TryGet<RepairWorker>(entity, out RepairWorker repairWorker) && repairWorker.HealthPercentPerSecond > 0f;
	}

	private int ReadPlayerIndex(Entity entity)
	{
		return _context.World.TryGet<UnitOwnerPlayer>(entity, out UnitOwnerPlayer owner) ? owner.Value : 0;
	}

	private bool AreAlliedPlayers(int playerIndex, int otherPlayerIndex)
	{
		if (playerIndex == otherPlayerIndex) return true;

		Entity worldEntity = _context.WorldEntity;
		if (worldEntity == Entity.Null || !_context.World.Has<PlayerRelationshipMatrix>(worldEntity)) return false;

		return _context.World.Get<PlayerRelationshipMatrix>(worldEntity).AreAllied(playerIndex, otherPlayerIndex);
	}
}
