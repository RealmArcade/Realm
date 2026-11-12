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

/// <summary>move, queued move and attack move.</summary>
public partial class CommandAuthorityService
	{
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
}
