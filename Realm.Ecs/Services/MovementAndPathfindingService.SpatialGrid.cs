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
		private void RebuildSpatialGrid()
		{
			foreach (var list in _unitGrid.Values)
			{
				list.Clear();
				_listPool.Add(list);
			}

			_unitGrid.Clear();

			var world = _context.World;
			world.Query(in _unitSpatialQuery, _unitSpatialQueryDelegate);

			int currentObstacleCount = world.CountEntities(in _propObstacleQuery) + world.CountEntities(in _buildingObstacleQuery);
			if (_obstacleGridDirty || !ReferenceEquals(_lastObstacleWorld, world) || currentObstacleCount != _lastObstacleCount)
			{
				_obstacleGridDirty = false;
				_lastObstacleWorld = world;
				_lastObstacleCount = currentObstacleCount;

				foreach (var list in _obstacleGrid.Values)
				{
					list.Clear();
					_listPool.Add(list);
				}

				_obstacleGrid.Clear();

				world.Query(in _propObstacleQuery, _propObstacleQueryDelegate);
				world.Query(in _buildingObstacleQuery, _buildingObstacleQueryDelegate);
			}
		}

		private void UnitSpatialGridQueryAction(Entity entity, ref Position p)
		{
			var world = _context.World;
			Vector3 pos = p.Value;
			long key = GetCellKey(pos.X, pos.Z);

			if (!_unitGrid.TryGetValue(key, out var list))
			{
				list = GetEntityListFromPool();
				_unitGrid[key] = list;
			}

			float scale = world.Has<CollisionScale>(entity) ? world.Get<CollisionScale>(entity).Value : 1.0f;
			float authoredRadius = world.Has<CollisionRadius>(entity) ? world.Get<CollisionRadius>(entity).Value : 0f;
			float radius = GameplayConstants.ResolveUnitContactRadius(authoredRadius, scale);
			bool hasVel = world.Has<Velocity>(entity);
			Vector3 vel = hasVel ? world.Get<Velocity>(entity).Value : Vector3.Zero;

			list.Add(new SpatialGridEntry(entity, pos, vel, radius, hasVel));
		}

		private void PropObstacleQueryAction(Entity entity, ref Position p)
		{
			var world = _context.World;
			float scale = world.Has<CollisionScale>(entity) ? world.Get<CollisionScale>(entity).Value : 1.0f;
			float radius = world.Has<CollisionRadius>(entity)
				? world.Get<CollisionRadius>(entity).Value * scale
				: scale * GameplayConstants.DefaultPropCollisionRadius;

			AddObstacleEntry(entity, p.Value, radius);
		}

		/// <summary>
		///     Registers a building as an obstacle body the tick after it is spawned and leaves its body out again the tick
		///     it is destroyed, which is the same lifecycle a unit's body has; the only difference is that a building is not
		///     rebuilt from a moving body every tick. A building that carries no authored radius - a template the map draws
		///     as a flat ground marker - blocks nothing, exactly as it contributes no obstacle volume to the navigation bake.
		/// </summary>
		private void BuildingObstacleQueryAction(Entity entity, ref Position p)
		{
			var world = _context.World;
			if (!world.Has<CollisionRadius>(entity)) return;

			float scale = world.Has<CollisionScale>(entity) ? world.Get<CollisionScale>(entity).Value : 1.0f;
			float radius = world.Get<CollisionRadius>(entity).Value * scale;
			if (radius <= GameplayConstants.MinimumAuthoredCollisionRadius) return;

			AddObstacleEntry(entity, p.Value, radius);
		}

		private void AddObstacleEntry(Entity entity, Vector3 pos, float radius)
		{
			var world = _context.World;
			if (world.Has<Dead>(entity)) return;

			long key = GetCellKey(pos.X, pos.Z);

			if (!_obstacleGrid.TryGetValue(key, out var list))
			{
				list = GetEntityListFromPool();
				_obstacleGrid[key] = list;
			}

			list.Add(new SpatialGridEntry(entity, pos, Vector3.Zero, radius, false));
		}
	}
}
