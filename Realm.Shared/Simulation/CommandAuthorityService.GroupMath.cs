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

/// <summary>splitting a group order across the units and scattering their destinations.</summary>
public partial class CommandAuthorityService
	{
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
		if (_context.World.Has<RepairTask>(entity)) _context.World.Remove<RepairTask>(entity);
		if (_context.World.Has<PickupTask>(entity)) _context.World.Remove<PickupTask>(entity);
		if (_context.World.Has<DropItemTask>(entity)) _context.World.Remove<DropItemTask>(entity);

		// A move or an attack is a new order, so work at a building ends with the orders it was given under. Leaving the
		// build task behind let the construction tick walk the unit back to the site on the next frame, which made a site
		// keep rising after its builder had been sent somewhere else.
		if (_context.World.Has<BuildTask>(entity)) _context.World.Remove<BuildTask>(entity);
	}
}
