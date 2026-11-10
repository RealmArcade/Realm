// Extracted from MovementAndPathfindingService.cs (task 6 decomposition).
using System;
using System.Collections.Generic;
using System.Numerics;
using System.Threading;
using Realm.Ecs;
using DotRecast.Core.Numerics;
using DotRecast.Detour;
using Realm.Ecs.Common;
using Realm.Ecs.Components.Core;
using Realm.Ecs.Components.Meta;
using Realm.Ecs.Components.Movement;
using Realm.Ecs.Components.Tags;
using Realm.Ecs.Components.Terrain;
using Realm.Ecs.Services;
using PathingFlags = Realm.Ecs.Components.Movement.PathingFlags;

namespace Realm.Ecs.Services
{
	public partial class MovementAndPathfindingService
	{
		private bool TryGetMovementContext(Entity entity, Vector3 pos, Vector3 target, out ushort pathingFlags, out bool isFlying, out bool hasPf, out PathFollow pf)
		{
			if (TryHandleStun(entity))
			{
				pathingFlags = 0;
				isFlying = false;
				hasPf = false;
				pf = default;
				return false;
			}

			pathingFlags = (ushort)(_context.World.Has<PathingFlags>(entity) ? _context.World.Get<PathingFlags>(entity).Value : 8);
			isFlying = ((TerrainPathingFlags)pathingFlags & TerrainPathingFlags.Flying) != 0;
			hasPf = _context.World.Has<PathFollow>(entity);
			pf = GetOrCreatePathFollow(hasPf, entity, pos, target);

			return true;
		}

		private void CollectMovementWorkItemAction(Entity entity, ref Position pos, ref MoveTo moveTo, ref MovementStats stats)
		{
			if (!ShouldSimulateEntity(entity)) return;

			if (_context.World.Has<PreviewMoveTo>(entity)) return;

			if (_context.World.Has<CommandsFrozen>(entity)) return;

			if (!TryGetMovementContext(entity, pos.Value, moveTo.Target, out ushort pathingFlags, out bool isFlying, out bool hasPf, out PathFollow pf)) return;

			if (_movementWorkCount >= _movementWorkItems.Length)
			{
				Array.Resize(ref _movementWorkItems, _movementWorkItems.Length * 2);
			}

			_movementWorkItems[_movementWorkCount++] = new MovementWorkItem
			{
				Entity = entity,
				PosValue = pos.Value,
				MoveTarget = moveTo.Target,
				Stats = stats,
				PathingFlags = pathingFlags,
				IsFlying = isFlying,
				HasPf = hasPf,
				Pf = pf,
				ActualSpeed = GetActualSpeed(entity, stats),
				IsPlanOnly = _planOnlyLocallyOwnedMovement
			};
		}

		/// <summary>
		///     Plans the route a click drew before the authority answered it. The order it stands for has not been decided
		///     yet, so the route is only ever drawn: <see cref="MovementWorkItem.IsPlanOnly" /> is set for it, which is what
		///     keeps the planning pass from writing a position. A unit that still carries the order it is replacing is
		///     planned from the new destination instead of the old one, because this runs after the order pass and the route
		///     it saves is the last one written.
		/// </summary>
		private void CollectPreviewMovementWorkItemAction(Entity entity, ref Position pos, ref PreviewMoveTo previewMoveTo, ref MovementStats stats)
		{
			if (!ShouldSimulateEntity(entity)) return;

			if (_context.World.Has<CommandsFrozen>(entity)) return;

			if (!TryGetMovementContext(entity, pos.Value, previewMoveTo.Target, out ushort pathingFlags, out bool isFlying, out bool hasPf, out PathFollow pf)) return;

			if (_movementWorkCount >= _movementWorkItems.Length)
			{
				Array.Resize(ref _movementWorkItems, _movementWorkItems.Length * 2);
			}

			_movementWorkItems[_movementWorkCount++] = new MovementWorkItem
			{
				Entity = entity,
				PosValue = pos.Value,
				MoveTarget = previewMoveTo.Target,
				Stats = stats,
				PathingFlags = pathingFlags,
				IsFlying = isFlying,
				HasPf = hasPf,
				Pf = pf,
				ActualSpeed = GetActualSpeed(entity, stats),
				IsPlanOnly = true
			};
		}

		/// <summary>
		///     How far a point is from the navigation mesh this process was baked with, which is the question a screen asks of a
		///     position it did not compute - a replicated unit it is drawing between two descriptions. Answers false when this
		///     process has no mesh to ask, so a caller can tell "on the mesh" from "nothing to compare against".
		/// </summary>
		public bool TryResolveDistanceOffNavMesh(Vector3 position, out float distanceOffNavMesh)
		{
			distanceOffNavMesh = 0f;

			var navQuery = GetThreadLocalNavMeshQuery() ?? _currentTerrainState.NavMeshQuery;
			if (navQuery == null) return false;

			var pathfinder = _threadLocalPathfinder.Value!;
			var sampledPosition = new RcVec3f(position.X, position.Y, position.Z);
			navQuery.FindNearestPoly(sampledPosition, NavMeshSnapExtents, pathfinder.Filter, out long polygonRef, out RcVec3f nearestPoint, out _);
			if (polygonRef == 0)
			{
				distanceOffNavMesh = float.MaxValue;
				return true;
			}

			float offsetX = nearestPoint.X - position.X;
			float offsetZ = nearestPoint.Z - position.Z;
			distanceOffNavMesh = MathF.Sqrt(offsetX * offsetX + offsetZ * offsetZ);
			return true;
		}

		private DtNavMeshQuery? GetThreadLocalNavMeshQuery()
		{
			if (!_hasTerrainState || _currentTerrainState.NavMesh == null)
				return null;

			var targetMesh = _currentTerrainState.NavMesh;
			var local = _threadLocalNavQuery.Value;
			if (!ReferenceEquals(local.Mesh, targetMesh) || local.Query == null)
			{
				local = (targetMesh, new DtNavMeshQuery(targetMesh));
				_threadLocalNavQuery.Value = local;
			}
			return local.Query;
		}
	}
}
