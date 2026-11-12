// Extracted from TerrainNavMeshService.cs (task 6 decomposition).
using System;
using System.Collections.Generic;
using System.Numerics;
using DotRecast.Core.Numerics;
using DotRecast.Detour;
using DotRecast.Recast;
using DotRecast.Recast.Geom;
using Realm.Ecs.Common;
using Realm.Ecs.Components.Core;
using Realm.Ecs.Components.Movement;
using Realm.Ecs.Components.Tags;
using Realm.Ecs.Components.Terrain;

namespace Realm.Ecs.Services;

public partial class TerrainNavMeshService
{
	private void InvalidatePathsQueryAction(Entity entity, ref Position pos, ref MoveTo moveTo, ref PathFollow pf)
	{
		if (pf.WaypointCount <= 0) return;

		float dx = pos.Value.X - _invalidateCenter.X;
		float dz = pos.Value.Z - _invalidateCenter.Z;
		if (dx * dx + dz * dz <= _invalidateRadiusSq)
		{
			pf.WaypointCount = 0;
			pf.HasValidCorridor = false;
			pf.TimeSinceLastReplan = 10f;
			return;
		}

		for (int i = pf.CurrentWaypointIndex; i < pf.WaypointCount; i++)
		{
			var wp = pf.Waypoints[i];
			float wx = wp.X - _invalidateCenter.X;
			float wz = wp.Z - _invalidateCenter.Z;
			if (wx * wx + wz * wz <= _invalidateRadiusSq)
			{
				pf.WaypointCount = 0;
				pf.HasValidCorridor = false;
				pf.TimeSinceLastReplan = 10f;
				return;
			}
		}
	}

	public void InvalidateIntersectingPaths(Vector3 center, float radius)
	{
		_invalidateCenter = center;
		_invalidateRadiusSq = radius * radius;
		_context.World?.Query(in QueryCache.AllPositionAndMoveToAndPathFollowNoneDeadQuery, _invalidatePathsDelegate);
	}

	private void CollectPropObstacle(Entity entity, ref Position pos, ref CollisionRadius colRad, ref PropIdentity propIdentity)
	{
		CollectObstacle(entity, pos.Value, colRad.Value);
	}

	private void CollectBuildingObstacle(Entity entity, ref Position pos, ref CollisionRadius colRad, ref Building building)
	{
		CollectObstacle(entity, pos.Value, colRad.Value);
	}

	private void CollectObstacle(Entity entity, Vector3 position, float authoredRadius)
	{
		float radius = ResolveObstacleRadius(entity, authoredRadius);
		if (radius <= GameplayConstants.MinimumAuthoredCollisionRadius) return;
		_navMeshObstacles.Add((position, radius));
	}

	private void CheckPropObstacle(Entity entity, ref Position pos, ref CollisionRadius colRad, ref PropIdentity propIdentity)
	{
		CheckObstacle(entity, pos.Value, colRad.Value);
	}

	private void CheckBuildingObstacle(Entity entity, ref Position pos, ref CollisionRadius colRad, ref Building building)
	{
		CheckObstacle(entity, pos.Value, colRad.Value);
	}

	private void CheckObstacle(Entity entity, Vector3 position, float authoredRadius)
	{
		if (_isCellBlockedByOtherObstacle) return;
		float radius = ResolveObstacleRadius(entity, authoredRadius);
		if (radius <= GameplayConstants.MinimumAuthoredCollisionRadius) return;
		float dx = _checkCellX - position.X;
		float dz = _checkCellZ - position.Z;
		if (dx * dx + dz * dz <= radius * radius)
		{
			_isCellBlockedByOtherObstacle = true;
		}
	}

	private float ResolveObstacleRadius(Entity entity, float authoredRadius)
	{
		EcsWorld? world = _context.World;
		float scale = (world != null && world.Has<CollisionScale>(entity)) ? world.Get<CollisionScale>(entity).Value : 1.0f;
		return authoredRadius * scale;
	}

	private bool IsPointCoveredByAnyObstacle(float px, float pz)
	{
		_checkCellX = px;
		_checkCellZ = pz;
		_isCellBlockedByOtherObstacle = false;
		var world = _context.World;
		if (world == null) return false;

		world.Query(in QueryCache.AllPositionAndCollisionRadiusAndPropIdentityNoneDeadQuery, _checkPropObstacleDelegate);
		world.Query(in QueryCache.AllPositionAndCollisionRadiusAndBuildingNoneDeadQuery, _checkBuildingObstacleDelegate);
		return _isCellBlockedByOtherObstacle;
	}

	private void GetGridBounds(int width, int depth, float quadSize, Vector3 pos, float halfWidth, float halfDepth, out int minX, out int maxX, out int minZ, out int maxZ)
	{
		float halfW = width / 2.0f * quadSize;
		float halfD = depth / 2.0f * quadSize;

		minX = Math.Clamp((int)Math.Floor((pos.X - halfWidth + halfW) / quadSize), 0, width - 1);
		maxX = Math.Clamp((int)Math.Ceiling((pos.X + halfWidth + halfW) / quadSize), 0, width - 1);
		minZ = Math.Clamp((int)Math.Floor((pos.Z - halfDepth + halfD) / quadSize), 0, depth - 1);
		maxZ = Math.Clamp((int)Math.Ceiling((pos.Z + halfDepth + halfD) / quadSize), 0, depth - 1);
	}
}
