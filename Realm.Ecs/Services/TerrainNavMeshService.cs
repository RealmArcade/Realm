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

public class TerrainNavMeshService
{
	public Dictionary<string, float> ObstacleRadiusCache { get; set; } = new();

	private const float NavMeshCellHeight = 0.1f;
	private const float AgentRadius = 1.2f;
	private const float AgentHeight = 2.5f;
	private const float AgentMaxClimb = 0.9f;
	private const float AgentMaxSlope = 55.0f;

	private readonly EntityCallback<Position, MoveTo, PathFollow> _invalidatePathsDelegate;
	private Vector3 _invalidateCenter;
	private float _invalidateRadiusSq;

	private readonly EntityCallback<Position, CollisionRadius, PropIdentity> _collectPropObstacleDelegate;
	private readonly EntityCallback<Position, CollisionRadius, Building> _collectBuildingObstacleDelegate;
	private readonly EntityCallback<Position, CollisionRadius, PropIdentity> _checkPropObstacleDelegate;
	private readonly EntityCallback<Position, CollisionRadius, Building> _checkBuildingObstacleDelegate;
	private readonly List<(Vector3 Pos, float Radius)> _navMeshObstacles = new();
	private float _checkCellX;
	private float _checkCellZ;
	private bool _isCellBlockedByOtherObstacle;

	public TerrainNavMeshService()
	{
		_invalidatePathsDelegate = InvalidatePathsQueryAction;
		_collectPropObstacleDelegate = CollectPropObstacle;
		_collectBuildingObstacleDelegate = CollectBuildingObstacle;
		_checkPropObstacleDelegate = CheckPropObstacle;
		_checkBuildingObstacleDelegate = CheckBuildingObstacle;
	}

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
		ECSContext.World?.Query(in QueryCache.AllPositionAndMoveToAndPathFollowNoneDeadQuery, _invalidatePathsDelegate);
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

	private static float ResolveObstacleRadius(Entity entity, float authoredRadius)
	{
		float scale = (ECSContext.World != null && ECSContext.World.Has<CollisionScale>(entity)) ? ECSContext.World.Get<CollisionScale>(entity).Value : 1.0f;
		return authoredRadius * scale;
	}

	private bool IsPointCoveredByAnyObstacle(float px, float pz)
	{
		_checkCellX = px;
		_checkCellZ = pz;
		_isCellBlockedByOtherObstacle = false;
		var world = ECSContext.World;
		if (world == null) return false;

		world.Query(in QueryCache.AllPositionAndCollisionRadiusAndPropIdentityQuery, _checkPropObstacleDelegate);
		world.Query(in QueryCache.AllPositionAndCollisionRadiusAndBuildingQuery, _checkBuildingObstacleDelegate);
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

	public void CarveObstacle(ref TerrainState state, Vector3 pos, float radius)
	{
		if (state.NavMesh == null) return;
		int width = state.Width;
		int depth = state.Depth;
		float quadSize = state.QuadSize;
		GetGridBounds(width, depth, quadSize, pos, radius, radius, out int minX, out int maxX, out int minZ, out int maxZ);

		CarvePathingGrid(ref state, pos, radius, minX, maxX, minZ, maxZ, width, depth, quadSize);
		CarveNavMeshTiles(ref state, pos, radius, quadSize);

		InvalidateIntersectingPaths(pos, radius + quadSize * 1.5f);
	}

	public void CarveObstacleBox(ref TerrainState state, Vector3 pos, float halfWidth, float halfDepth)
	{
		if (state.NavMesh == null) return;
		int width = state.Width;
		int depth = state.Depth;
		float quadSize = state.QuadSize;
		GetGridBounds(width, depth, quadSize, pos, halfWidth, halfDepth, out int minX, out int maxX, out int minZ, out int maxZ);

		CarvePathingGridBox(ref state, pos, halfWidth, halfDepth, minX, maxX, minZ, maxZ, width, depth, quadSize);
		CarveNavMeshTilesBox(ref state, pos, halfWidth, halfDepth, quadSize);

		float maxDim = Math.Max(halfWidth, halfDepth);
		InvalidateIntersectingPaths(pos, maxDim + quadSize * 1.5f);
	}

	private void ProcessPathingGrid(ref TerrainState state, Vector3 pos, float radius, int minX, int maxX, int minZ, int maxZ, int width, int depth, float quadSize, ProcessPathingCellBoxDelegate processCell)
	{
		if (state.PathingCodes == null) return;
		for (int z = minZ; z <= maxZ; z++)
		{
			for (int x = minX; x <= maxX; x++)
			{
				float lx = (x + 0.5f - width / 2.0f) * quadSize;
				float lz = (z + 0.5f - depth / 2.0f) * quadSize;
				float dx = lx - pos.X;
				float dz = lz - pos.Z;
				if (dx * dx + dz * dz <= radius * radius)
				{
					processCell(ref state, x, z);
				}
			}
		}
	}

	private void CarvePathingGrid(ref TerrainState state, Vector3 pos, float radius, int minX, int maxX, int minZ, int maxZ, int width, int depth, float quadSize)
	{
		ProcessPathingGrid(ref state, pos, radius, minX, maxX, minZ, maxZ, width, depth, quadSize, (ref TerrainState s, int x, int z) =>
		{
			s.PathingCodes[x, z] &= ~(int)(TerrainPathingFlags.Ground | TerrainPathingFlags.Buildable);
		});
	}

	private delegate void ProcessPolyDelegate(ref TerrainState state, DtMeshTile tile, DtPoly poly, long polyRefBase, int p);

	private void IterateNavMeshPolys(ref TerrainState state, ProcessPolyDelegate processPoly)
	{
		int maxTiles = state.NavMesh.GetMaxTiles();
		for (int t = 0; t < maxTiles; t++)
		{
			var tile = state.NavMesh.GetTile(t);
			if (tile?.data?.header == null) continue;
			long polyRefBase = state.NavMesh.GetPolyRefBase(tile);
			for (int p = 0; p < tile.data.header.polyCount; p++)
			{
				var poly = tile.data.polys[p];
				processPoly(ref state, tile, poly, polyRefBase, p);
			}
		}
	}

	private void ProcessNavMeshTiles(ref TerrainState state, Vector3 pos, float radius, float quadSize, ProcessPolyDelegate processPoly)
	{
		float effRadiusSq = (radius + quadSize * 0.5f) * (radius + quadSize * 0.5f);
		IterateNavMeshPolys(ref state, (ref TerrainState s, DtMeshTile tile, DtPoly poly, long polyRefBase, int p) =>
		{
			GetPolyCenter(tile, poly, out float avgX, out float avgZ);
			float dx = avgX - pos.X;
			float dz = avgZ - pos.Z;

			if (dx * dx + dz * dz <= effRadiusSq)
			{
				processPoly(ref s, tile, poly, polyRefBase, p);
			}
		});
	}

	private void CarveNavMeshTiles(ref TerrainState state, Vector3 pos, float radius, float quadSize)
	{
		int maxTiles = state.NavMesh.GetMaxTiles();
		float effRadiusSq = (radius + quadSize * 0.5f) * (radius + quadSize * 0.5f);
		for (int t = 0; t < maxTiles; t++)
		{
			var tile = state.NavMesh.GetTile(t);
			if (tile?.data?.header == null) continue;
			long polyRefBase = state.NavMesh.GetPolyRefBase(tile);
			for (int p = 0; p < tile.data.header.polyCount; p++)
			{
				var poly = tile.data.polys[p];
				float sumX = 0f;
				float sumZ = 0f;
				int nv = poly.vertCount;
				for (int j = 0; j < nv; j++)
				{
					int vIdx = poly.verts[j];
					sumX += tile.data.verts[vIdx * 3];
					sumZ += tile.data.verts[vIdx * 3 + 2];
				}

				float avgX = nv > 0 ? sumX / nv : 0f;
				float avgZ = nv > 0 ? sumZ / nv : 0f;
				float dx = avgX - pos.X;
				float dz = avgZ - pos.Z;
				if (dx * dx + dz * dz <= effRadiusSq)
				{
					long polyRef = polyRefBase | (long)p;
					state.NavMesh.SetPolyFlags(polyRef, 0);
					state.NavMesh.SetPolyArea(polyRef, (char)0);
				}
			}
		}
	}

	private delegate void ProcessPathingCellBoxDelegate(ref TerrainState state, int x, int z);

	private void ProcessPathingGridBox(ref TerrainState state, Vector3 pos, float halfWidth, float halfDepth, int minX, int maxX, int minZ, int maxZ, int width, int depth, float quadSize, ProcessPathingCellBoxDelegate processCell)
	{
		if (state.PathingCodes == null) return;
		for (int z = minZ; z <= maxZ; z++)
		{
			for (int x = minX; x <= maxX; x++)
			{
				float lx = (x + 0.5f - width / 2.0f) * quadSize;
				float lz = (z + 0.5f - depth / 2.0f) * quadSize;
				if (IsPointInBox(lx, lz, pos, halfWidth, halfDepth))
				{
					processCell(ref state, x, z);
				}
			}
		}
	}

	private void CarvePathingGridBox(ref TerrainState state, Vector3 pos, float halfWidth, float halfDepth, int minX, int maxX, int minZ, int maxZ, int width, int depth, float quadSize)
	{
		ProcessPathingGridBox(ref state, pos, halfWidth, halfDepth, minX, maxX, minZ, maxZ, width, depth, quadSize, (ref TerrainState s, int x, int z) =>
		{
			s.PathingCodes[x, z] &= ~(int)(TerrainPathingFlags.Ground | TerrainPathingFlags.Buildable);
		});
	}

	private bool IsPolyInBox(float avgX, float avgZ, Vector3 pos, float halfWidth, float halfDepth, float quadSize)
	{
		return Math.Abs(avgX - pos.X) <= halfWidth + quadSize * 0.5f && Math.Abs(avgZ - pos.Z) <= halfDepth + quadSize * 0.5f;
	}

	private bool IsPointInBox(float lx, float lz, Vector3 pos, float halfWidth, float halfDepth)
	{
		return Math.Abs(lx - pos.X) <= halfWidth && Math.Abs(lz - pos.Z) <= halfDepth;
	}

	private void GetPolyCenter(DtMeshTile tile, DtPoly poly, out float avgX, out float avgZ)
	{
		float sumX = 0f;
		float sumZ = 0f;
		int nv = poly.vertCount;
		for (int j = 0; j < nv; j++)
		{
			int vIdx = poly.verts[j];
			sumX += tile.data.verts[vIdx * 3];
			sumZ += tile.data.verts[vIdx * 3 + 2];
		}

		avgX = nv > 0 ? sumX / nv : 0f;
		avgZ = nv > 0 ? sumZ / nv : 0f;
	}

	private void ProcessNavMeshTilesBox(ref TerrainState state, Vector3 pos, float halfWidth, float halfDepth, float quadSize, ProcessPolyDelegate processPoly)
	{
		IterateNavMeshPolys(ref state, (ref TerrainState s, DtMeshTile tile, DtPoly poly, long polyRefBase, int p) =>
		{
			GetPolyCenter(tile, poly, out float avgX, out float avgZ);

			if (IsPolyInBox(avgX, avgZ, pos, halfWidth, halfDepth, quadSize))
			{
				processPoly(ref s, tile, poly, polyRefBase, p);
			}
		});
	}

	private void CarveNavMeshTilesBox(ref TerrainState state, Vector3 pos, float halfWidth, float halfDepth, float quadSize)
	{
		ProcessNavMeshTilesBox(ref state, pos, halfWidth, halfDepth, quadSize, (ref TerrainState s, DtMeshTile tile, DtPoly poly, long polyRefBase, int p) =>
		{
			long polyRef = polyRefBase | (long)p;
			s.NavMesh.SetPolyFlags(polyRef, 0);
			s.NavMesh.SetPolyArea(polyRef, (char)0);
		});
	}

	public void UncarveObstacle(ref TerrainState state, Vector3 pos, float radius)
	{
		if (state.NavMesh == null) return;
		int width = state.Width;
		int depth = state.Depth;
		float quadSize = state.QuadSize;
		GetGridBounds(width, depth, quadSize, pos, radius, radius, out int minX, out int maxX, out int minZ, out int maxZ);

		UncarvePathingGrid(ref state, pos, radius, minX, maxX, minZ, maxZ, width, depth, quadSize);
		UncarveNavMeshTiles(ref state, pos, radius, width, depth, quadSize);

		InvalidateIntersectingPaths(pos, radius + quadSize * 2f);
	}

	public void UncarveObstacleBox(ref TerrainState state, Vector3 pos, float halfWidth, float halfDepth)
	{
		if (state.NavMesh == null) return;
		int width = state.Width;
		int depth = state.Depth;
		float quadSize = state.QuadSize;
		GetGridBounds(width, depth, quadSize, pos, halfWidth, halfDepth, out int minX, out int maxX, out int minZ, out int maxZ);

		UncarvePathingGridBox(ref state, pos, halfWidth, halfDepth, minX, maxX, minZ, maxZ, width, depth, quadSize);
		UncarveNavMeshTilesBox(ref state, pos, halfWidth, halfDepth, width, depth, quadSize);

		float maxDim = Math.Max(halfWidth, halfDepth);
		InvalidateIntersectingPaths(pos, maxDim + quadSize * 2f);
	}

	private void UncarvePathingGrid(ref TerrainState state, Vector3 pos, float radius, int minX, int maxX, int minZ, int maxZ, int width, int depth, float quadSize)
	{
		ProcessPathingGrid(ref state, pos, radius, minX, maxX, minZ, maxZ, width, depth, quadSize, (ref TerrainState s, int x, int z) =>
		{
			float lx = (x + 0.5f - width / 2.0f) * quadSize;
			float lz = (z + 0.5f - depth / 2.0f) * quadSize;
			if (!IsPointCoveredByAnyObstacle(lx, lz))
			{
				RestorePathingCell(ref s, x, z);
			}
		});
	}

	private void UncarveNavMeshTiles(ref TerrainState state, Vector3 pos, float radius, int width, int depth, float quadSize)
	{
		ProcessNavMeshTiles(ref state, pos, radius, quadSize, (ref TerrainState s, DtMeshTile tile, DtPoly poly, long polyRefBase, int p) =>
		{
			GetPolyCenter(tile, poly, out float avgX, out float avgZ);
			if (IsPointCoveredByAnyObstacle(avgX, avgZ))
				return;

			RestoreNavMeshPoly(ref s, tile, poly, polyRefBase, p, width, depth, quadSize);
		});
	}

	private void UncarvePathingGridBox(ref TerrainState state, Vector3 pos, float halfWidth, float halfDepth, int minX, int maxX, int minZ, int maxZ, int width, int depth, float quadSize)
	{
		ProcessPathingGridBox(ref state, pos, halfWidth, halfDepth, minX, maxX, minZ, maxZ, width, depth, quadSize, (ref TerrainState s, int x, int z) =>
		{
			float lx = (x + 0.5f - width / 2.0f) * quadSize;
			float lz = (z + 0.5f - depth / 2.0f) * quadSize;
			if (!IsPointCoveredByAnyObstacle(lx, lz))
			{
				RestorePathingCell(ref s, x, z);
			}
		});
	}

	private void UncarveNavMeshTilesBox(ref TerrainState state, Vector3 pos, float halfWidth, float halfDepth, int width, int depth, float quadSize)
	{
		ProcessNavMeshTilesBox(ref state, pos, halfWidth, halfDepth, quadSize, (ref TerrainState s, DtMeshTile tile, DtPoly poly, long polyRefBase, int p) =>
		{
			GetPolyCenter(tile, poly, out float avgX, out float avgZ);
			if (!IsPointCoveredByAnyObstacle(avgX, avgZ))
			{
				RestoreNavMeshPoly(ref s, tile, poly, polyRefBase, p, width, depth, quadSize);
			}
		});
	}

	private void GetRegionGridBounds(int width, int depth, float quadSize, float minX, float minZ, float maxX, float maxZ, out float x1, out float x2, out float z1, out float z2, out int startX, out int endX, out int startZ, out int endZ, out float halfW, out float halfD)
	{
		x1 = Math.Min(minX, maxX);
		x2 = Math.Max(minX, maxX);
		z1 = Math.Min(minZ, maxZ);
		z2 = Math.Max(minZ, maxZ);

		halfW = width / 2.0f * quadSize;
		halfD = depth / 2.0f * quadSize;

		startX = Math.Clamp((int)Math.Floor((x1 + halfW) / quadSize), 0, width - 1);
		endX = Math.Clamp((int)Math.Ceiling((x2 + halfW) / quadSize), 0, width - 1);
		startZ = Math.Clamp((int)Math.Floor((z1 + halfD) / quadSize), 0, depth - 1);
		endZ = Math.Clamp((int)Math.Ceiling((z2 + halfD) / quadSize), 0, depth - 1);
	}

	public void SetRegionPathing(ref TerrainState state, float minX, float minZ, float maxX, float maxZ, int flags, bool blocked)
	{
		if (state.PathingCodes == null) return;

		int width = state.Width;
		int depth = state.Depth;
		float quadSize = state.QuadSize;
		GetRegionGridBounds(width, depth, quadSize, minX, minZ, maxX, maxZ, out float x1, out float x2, out float z1, out float z2, out int startX, out int endX, out int startZ, out int endZ, out float halfW, out float halfD);

		for (int z = startZ; z <= endZ; z++)
		{
			for (int x = startX; x <= endX; x++)
			{
				if (blocked)
				{
					state.PathingCodes[x, z] &= ~flags;
				}
				else
				{
					state.PathingCodes[x, z] |= flags;
				}
			}
		}

		if (state.NavMesh != null)
		{
			float pad = quadSize * 0.5f;
			IterateNavMeshPolys(ref state, (ref TerrainState s, DtMeshTile tile, DtPoly poly, long polyRefBase, int p) =>
			{
				GetPolyCenter(tile, poly, out float avgX, out float avgZ);

				if (avgX < x1 - pad || avgX > x2 + pad || avgZ < z1 - pad || avgZ > z2 + pad)
					return;

				Span<Vector2> polygonVertices = stackalloc Vector2[poly.vertCount];
				int vertexCount = CollectPolygonVertices(tile, poly, polygonVertices);
				int cellFlags = ClassifyPolygonPathingFlags(s.PathingCodes, width, depth, quadSize, polygonVertices[..vertexCount]);

				long polyRef = polyRefBase | (long)p;
				s.NavMesh.SetPolyFlags(polyRef, cellFlags);
				bool isWalkable = (cellFlags & (int)TerrainPathingFlags.Ground) != 0;
				s.NavMesh.SetPolyArea(polyRef, isWalkable ? (char)1 : (char)0);
			});
		}

		float cx = (x1 + x2) * 0.5f;
		float cz = (z1 + z2) * 0.5f;
		float halfWidth = (x2 - x1) * 0.5f;
		float halfDepth = (z2 - z1) * 0.5f;
		float maxDim = Math.Max(halfWidth, halfDepth);
		InvalidateIntersectingPaths(new Vector3(cx, 0, cz), maxDim + quadSize * 2f);
	}

	public int GetRegionPathing(in TerrainState state, float minX, float minZ, float maxX, float maxZ)
	{
		if (state.PathingCodes == null) return (int)(TerrainPathingFlags.Ground | TerrainPathingFlags.Buildable | TerrainPathingFlags.Flying);

		int width = state.Width;
		int depth = state.Depth;
		float quadSize = state.QuadSize;
		GetRegionGridBounds(width, depth, quadSize, minX, minZ, maxX, maxZ, out float x1, out float x2, out float z1, out float z2, out int startX, out int endX, out int startZ, out int endZ, out float halfW, out float halfD);

		if (startX > endX || startZ > endZ) return 0;

		int combinedFlags = 0xFF;
		for (int z = startZ; z <= endZ; z++)
		{
			for (int x = startX; x <= endX; x++)
			{
				combinedFlags &= state.PathingCodes[x, z];
			}
		}

		return combinedFlags;
	}

	private void RestorePathingCell(ref TerrainState state, int x, int z)
	{
		if (state.PathingCodes == null) return;
		var waterMode = state.Cells != null ? state.Cells[x, z].WaterMode : WaterType.None;
		int defaultPath = waterMode switch
		{
			WaterType.Shallow => (int)(TerrainPathingFlags.ShallowWater | TerrainPathingFlags.Flying),
			WaterType.Deep => (int)(TerrainPathingFlags.DeepWater | TerrainPathingFlags.Flying),
			_ => (int)(TerrainPathingFlags.Ground | TerrainPathingFlags.Buildable | TerrainPathingFlags.Flying)
		};
		state.PathingCodes[x, z] = defaultPath;
	}

	private void RestoreNavMeshPoly(ref TerrainState state, DtMeshTile tile, DtPoly poly, long polyRefBase, int polyIndex, int width, int depth, float quadSize)
	{
		Span<Vector2> polygonVertices = stackalloc Vector2[poly.vertCount];
		int vertexCount = CollectPolygonVertices(tile, poly, polygonVertices);
		int pathFlags = ClassifyPolygonPathingFlags(state.PathingCodes, width, depth, quadSize, polygonVertices[..vertexCount]);
		long polyRef = polyRefBase | (long)polyIndex;
		state.NavMesh.SetPolyFlags(polyRef, pathFlags);
		state.NavMesh.SetPolyArea(polyRef, (char)1);
	}

	public void BakeNavMesh(ref TerrainState state)
	{
		int width = state.Width;
		int depth = state.Depth;
		float quadSize = state.QuadSize;
		float halfW = width / 2.0f * quadSize;
		float halfD = depth / 2.0f * quadSize;

		var obstacles = GetNavMeshObstacles();
		if (state.Cells == null) return;
		var bakeHeights = GetBakeHeights(in state, width, depth);

		var geom = CreateInputGeom(bakeHeights, width, depth, quadSize);
		AddObstacleVolumes(obstacles, geom, bakeHeights, width, depth, quadSize, halfW, halfD);
		AddUnwalkableVolumes(in state, geom, width, depth, quadSize);

		BuildNavMesh(ref state, geom, width, depth, quadSize);
	}

	private List<(Vector3 Pos, float Radius)> GetNavMeshObstacles()
	{
		_navMeshObstacles.Clear();
		var world = ECSContext.World;
		if (world == null) return _navMeshObstacles;

		world.Query(in QueryCache.AllPositionAndCollisionRadiusAndPropIdentityQuery, _collectPropObstacleDelegate);
		world.Query(in QueryCache.AllPositionAndCollisionRadiusAndBuildingQuery, _collectBuildingObstacleDelegate);
		return _navMeshObstacles;
	}

	private float[,] GetBakeHeights(in TerrainState state, int width, int depth)
	{
		var bakeHeights = new float[width + 1, depth + 1];
		for (int z = 0; z <= depth; z++)
		{
			for (int x = 0; x <= width; x++)
			{
				bakeHeights[x, z] = GetVertexHeight(in state, x, z);
			}
		}

		return bakeHeights;
	}

	private void AddObstacleVolumes(List<(Vector3 Pos, float Radius)> obstacles, SimpleInputGeomProvider geom, float[,] bakeHeights, int width, int depth, float quadSize, float halfW, float halfD)
	{
		const int segments = 16;
		foreach (var obs in obstacles)
		{
			float radius = obs.Radius;
			if (radius <= GameplayConstants.MinimumAuthoredCollisionRadius) continue;

			float effectiveRadius = Math.Max(quadSize * 0.5f, radius - quadSize * 0.51f);
			float ground = SampleBakeHeight(bakeHeights, obs.Pos.X, obs.Pos.Z, width, depth, quadSize, halfW, halfD);

			var volumeVerts = new float[segments * 3];
			for (int i = 0; i < segments; i++)
			{
				float angle = MathF.Tau * i / segments;
				volumeVerts[i * 3] = obs.Pos.X + MathF.Cos(angle) * effectiveRadius;
				volumeVerts[i * 3 + 1] = ground;
				volumeVerts[i * 3 + 2] = obs.Pos.Z + MathF.Sin(angle) * effectiveRadius;
			}

			var volume = new RcConvexVolume
			{
				verts = volumeVerts,
				hmin = ground - 2.0f,
				hmax = ground + AgentHeight,
				areaMod = new RcAreaModification(0)
			};
			geom.AddConvexVolume(volume);
		}
	}

	private static float SampleBakeHeight(float[,] bakeHeights, float worldX, float worldZ, int width, int depth, float quadSize, float halfW, float halfD)
	{
		float gridX = Math.Clamp((worldX + halfW) / quadSize, 0f, width);
		float gridZ = Math.Clamp((worldZ + halfD) / quadSize, 0f, depth);
		int x0 = Math.Min((int)gridX, width - 1);
		int z0 = Math.Min((int)gridZ, depth - 1);
		float fractionX = gridX - x0;
		float fractionZ = gridZ - z0;

		float northWest = bakeHeights[x0, z0];
		float northEast = bakeHeights[x0 + 1, z0];
		float southWest = bakeHeights[x0, z0 + 1];
		float southEast = bakeHeights[x0 + 1, z0 + 1];
		float north = northWest + (northEast - northWest) * fractionX;
		float south = southWest + (southEast - southWest) * fractionX;
		return north + (south - north) * fractionZ;
	}

	private SimpleInputGeomProvider CreateInputGeom(float[,] bakeHeights, int width, int depth, float quadSize)
	{
		var meshVerts = new List<float>();
		for (int z = 0; z <= depth; z++)
		{
			for (int x = 0; x <= width; x++)
			{
				float lx = (x - width / 2.0f) * quadSize;
				float lz = (z - depth / 2.0f) * quadSize;
				meshVerts.Add(lx);
				meshVerts.Add(bakeHeights[x, z]);
				meshVerts.Add(lz);
			}
		}

		var indices = new List<int>();
		for (int z = 0; z < depth; z++)
		{
			for (int x = 0; x < width; x++)
			{
				int v00 = z * (width + 1) + x;
				int v10 = z * (width + 1) + (x + 1);
				int v01 = (z + 1) * (width + 1) + x;
				int v11 = (z + 1) * (width + 1) + (x + 1);
				indices.Add(v00);
				indices.Add(v01);
				indices.Add(v10);
				indices.Add(v10);
				indices.Add(v01);
				indices.Add(v11);
			}
		}

		return new SimpleInputGeomProvider(meshVerts, indices);
	}

	private void AddUnwalkableVolumes(in TerrainState state, SimpleInputGeomProvider geom, int width, int depth, float quadSize)
	{
		for (int z = 0; z < depth; z++)
		{
			for (int x = 0; x < width; x++)
			{
				var pathingCode = state.PathingCodes != null ? (TerrainPathingFlags)state.PathingCodes[x, z] : TerrainPathingFlags.Ground | TerrainPathingFlags.Buildable;
				bool isWalkable = pathingCode != TerrainPathingFlags.None;
				if (!isWalkable)
				{
					float lx = (x + 0.5f - width / 2.0f) * quadSize;
					float lz = (z + 0.5f - depth / 2.0f) * quadSize;
					float h = GetVertexHeight(in state, x, z);
					float halfS = quadSize * 0.45f;
					var vol = new RcConvexVolume
					{
						verts = new float[]
						{
							lx - halfS, h, lz - halfS,
							lx + halfS, h, lz - halfS,
							lx + halfS, h, lz + halfS,
							lx - halfS, h, lz + halfS
						},
						hmin = h - 5.0f,
						hmax = h + 5.0f,
						areaMod = new RcAreaModification(0)
					};
					geom.AddConvexVolume(vol);
				}
			}
		}
	}

	private void InitializeNavMeshPolys(DtNavMeshCreateParams pars, RcPolyMesh mesh, RcVec3f bmin, float quadSize, int width, int depth, in TerrainState state)
	{
		Span<Vector2> polyVerts = stackalloc Vector2[12];
		for (int i = 0; i < mesh.npolys; i++)
		{
			pars.polyAreas[i] = mesh.areas[i];

			int nv = 0;
			for (int j = 0; j < mesh.nvp; j++)
			{
				int vIdx = mesh.polys[i * mesh.nvp * 2 + j];
				if (vIdx < 0 || vIdx >= mesh.nverts)
					break;

				float wx = bmin.X + mesh.verts[vIdx * 3] * mesh.cs;
				float wz = bmin.Z + mesh.verts[vIdx * 3 + 2] * mesh.cs;

				if (nv < polyVerts.Length)
					polyVerts[nv++] = new Vector2(wx, wz);
			}

			pars.polyFlags[i] = ClassifyPolygonPathingFlags(state.PathingCodes, width, depth, quadSize, polyVerts[..nv]);
		}
	}

	private static int ClassifyPolygonPathingFlags(int[,]? pathingCodes, int width, int depth, float quadSize, ReadOnlySpan<Vector2> polygonVertices)
	{
		if (pathingCodes == null) return (int)(TerrainPathingFlags.Ground | TerrainPathingFlags.Buildable);
		if (polygonVertices.Length == 0) return 0;

		float halfW = width / 2.0f * quadSize;
		float halfD = depth / 2.0f * quadSize;
		float minX = float.MaxValue;
		float maxX = float.MinValue;
		float minZ = float.MaxValue;
		float maxZ = float.MinValue;
		float sumX = 0f;
		float sumZ = 0f;
		for (int i = 0; i < polygonVertices.Length; i++)
		{
			var vertex = polygonVertices[i];
			if (vertex.X < minX) minX = vertex.X;
			if (vertex.X > maxX) maxX = vertex.X;
			if (vertex.Y < minZ) minZ = vertex.Y;
			if (vertex.Y > maxZ) maxZ = vertex.Y;
			sumX += vertex.X;
			sumZ += vertex.Y;
		}

		int startX = Math.Clamp((int)Math.Floor((minX + halfW) / quadSize), 0, width - 1);
		int endX = Math.Clamp((int)Math.Floor((maxX + halfW) / quadSize), 0, width - 1);
		int startZ = Math.Clamp((int)Math.Floor((minZ + halfD) / quadSize), 0, depth - 1);
		int endZ = Math.Clamp((int)Math.Floor((maxZ + halfD) / quadSize), 0, depth - 1);

		int combinedFlags = 0;
		for (int z = startZ; z <= endZ; z++)
		{
			float centreZ = (z + 0.5f - depth / 2.0f) * quadSize;
			for (int x = startX; x <= endX; x++)
			{
				float centreX = (x + 0.5f - width / 2.0f) * quadSize;
				if (!IsPointInsidePolygon(polygonVertices, centreX, centreZ)) continue;
				combinedFlags |= pathingCodes[x, z];
			}
		}

		int centroidGridX = Math.Clamp((int)Math.Floor((sumX / polygonVertices.Length + halfW) / quadSize), 0, width - 1);
		int centroidGridZ = Math.Clamp((int)Math.Floor((sumZ / polygonVertices.Length + halfD) / quadSize), 0, depth - 1);
		combinedFlags |= pathingCodes[centroidGridX, centroidGridZ];

		return combinedFlags;
	}

	private static bool IsPointInsidePolygon(ReadOnlySpan<Vector2> polygonVertices, float pointX, float pointZ)
	{
		bool inside = false;
		int count = polygonVertices.Length;
		for (int i = 0, j = count - 1; i < count; j = i++)
		{
			var current = polygonVertices[i];
			var previous = polygonVertices[j];
			if ((current.Y > pointZ) == (previous.Y > pointZ)) continue;
			if (pointX >= (previous.X - current.X) * (pointZ - current.Y) / (previous.Y - current.Y) + current.X) continue;
			inside = !inside;
		}

		return inside;
	}

	private static int CollectPolygonVertices(DtMeshTile tile, DtPoly poly, Span<Vector2> destination)
	{
		int count = Math.Min(poly.vertCount, destination.Length);
		for (int i = 0; i < count; i++)
		{
			int vertexIndex = poly.verts[i];
			destination[i] = new Vector2(tile.data.verts[vertexIndex * 3], tile.data.verts[vertexIndex * 3 + 2]);
		}

		return count;
	}

	private void BuildNavMesh(ref TerrainState state, SimpleInputGeomProvider geom, int width, int depth, float quadSize)
	{
		RcConfig cfg = new RcConfig(
			RcPartition.WATERSHED,
			state.CellSize > 0.0001f ? state.CellSize : TerrainState.DefaultCellSize, NavMeshCellHeight,
			AgentMaxSlope, AgentHeight, AgentRadius, AgentMaxClimb,
			8, 20,
			3.0f, 1.3f,
			6,
			6.0f, 1.0f,
			true, true, true,
			new RcAreaModification(1),
			true
		);
		RcVec3f bmin = geom.GetMeshBoundsMin();
		RcVec3f bmax = geom.GetMeshBoundsMax();
		bmin.Y -= 10f;
		bmax.Y += 50f;
		var bcfg = new RcBuilderConfig(cfg, bmin, bmax);
		var builder = new RcBuilder();
		var result = builder.Build(geom, bcfg, true);

		if (result.Mesh == null || result.Mesh.npolys == 0)
		{
			Console.Error.WriteLine($"[BakeNavMesh] BUILDER FAILED: no polys generated. width={width} depth={depth} quadSize={quadSize}");
			return;
		}

		var pars = new DtNavMeshCreateParams();
		pars.verts = result.Mesh.verts;
		pars.vertCount = result.Mesh.nverts;
		pars.polys = result.Mesh.polys;
		pars.polyCount = result.Mesh.npolys;
		pars.nvp = result.Mesh.nvp;
		pars.bmin = result.Mesh.bmin;
		pars.bmax = result.Mesh.bmax;
		pars.cs = result.Mesh.cs;
		pars.ch = result.Mesh.ch;
		pars.buildBvTree = true;
		pars.walkableHeight = AgentHeight;
		pars.walkableRadius = AgentRadius;
		pars.walkableClimb = AgentMaxClimb;
		pars.polyAreas = new int[result.Mesh.npolys];
		pars.polyFlags = new int[result.Mesh.npolys];

		InitializeNavMeshPolys(pars, result.Mesh, result.Mesh.bmin, quadSize, width, depth, in state);

		if (result.MeshDetail != null)
		{
			pars.detailMeshes = result.MeshDetail.meshes;
			pars.detailVerts = result.MeshDetail.verts;
			pars.detailVertsCount = result.MeshDetail.nverts;
			pars.detailTris = result.MeshDetail.tris;
			pars.detailTriCount = result.MeshDetail.ntris;
		}

		var navMeshData = DtNavMeshBuilder.CreateNavMeshData(pars);
		if (navMeshData != null)
		{
			state.NavMesh = new DtNavMesh();
			state.NavMesh.Init(navMeshData, pars.nvp, 0);
			state.NavMeshQuery = new DtNavMeshQuery(state.NavMesh);
		}
	}

	private float GetVertexHeight(in TerrainState state, int x, int z)
	{
		var cells = state.Cells;
		if (cells == null) return 0.0f;
		int w = state.Width;
		int d = state.Depth;
		if (w <= 0 || d <= 0) return 0.0f;
		int cellX = Math.Clamp(x, 0, w - 1);
		int cellZ = Math.Clamp(z, 0, d - 1);
		if (x < w && z < d) return cells[cellX, cellZ].Y_NW;
		if (x == w && z < d) return cells[w - 1, cellZ].Y_NE;
		if (x < w && z == d) return cells[cellX, d - 1].Y_SW;
		return cells[w - 1, d - 1].Y_SE;
	}

	public void GetHeightAndNormal(in TerrainState state, float worldX, float worldZ, out float height, out Vector3 normal)
	{
		float halfW = state.Width / 2.0f * state.QuadSize;
		float halfD = state.Depth / 2.0f * state.QuadSize;
		float gridX = (worldX + halfW) / state.QuadSize;
		float gridZ = (worldZ + halfD) / state.QuadSize;
		int x0 = (int)Math.Floor(gridX);
		int z0 = (int)Math.Floor(gridZ);
		x0 = Math.Max(0, Math.Min(state.Width - 1, x0));
		z0 = Math.Max(0, Math.Min(state.Depth - 1, z0));
		float tx = gridX - x0;
		float tz = gridZ - z0;
		float h00 = GetVertexHeight(in state, x0, z0);
		float h10 = GetVertexHeight(in state, Math.Min(state.Width, x0 + 1), z0);
		float h01 = GetVertexHeight(in state, x0, Math.Min(state.Depth, z0 + 1));
		float h11 = GetVertexHeight(in state, Math.Min(state.Width, x0 + 1), Math.Min(state.Depth, z0 + 1));
		height = (1 - tx) * (1 - tz) * h00 + tx * (1 - tz) * h10 + (1 - tx) * tz * h01 + tx * tz * h11;
		Vector3 n00 = GetVertexNormal(in state, x0, z0);
		Vector3 n10 = GetVertexNormal(in state, Math.Min(state.Width, x0 + 1), z0);
		Vector3 n01 = GetVertexNormal(in state, x0, Math.Min(state.Depth, z0 + 1));
		Vector3 n11 = GetVertexNormal(in state, Math.Min(state.Width, x0 + 1), Math.Min(state.Depth, z0 + 1));
		normal = Vector3.Normalize((1 - tx) * (1 - tz) * n00 + tx * (1 - tz) * n10 + (1 - tx) * tz * n01 + tx * tz * n11);
	}

	private Vector3 GetVertexNormal(in TerrainState state, int x, int z)
	{
		var cells = state.Cells;
		if (cells == null) return Vector3.UnitY;
		float h = GetVertexHeight(in state, x, z);
		float cliffThreshold = 0.95f * Math.Max(0.1f, state.QuadSize);

		float dx = GetSlopeDx(in state, x, z, h, cliffThreshold);
		float dz = GetSlopeDz(in state, x, z, h, cliffThreshold);

		if (Math.Abs(dx) < 0.001f && Math.Abs(dz) < 0.001f)
		{
			return Vector3.UnitY;
		}

		Vector3 tangentX = Vector3.Normalize(new Vector3(state.QuadSize, dx, 0.0f));
		Vector3 tangentZ = Vector3.Normalize(new Vector3(0.0f, dz, state.QuadSize));
		return Vector3.Normalize(Vector3.Cross(tangentZ, tangentX));
	}

	private static bool IsCliff(bool hasNeighbor, float delta, float cliffThreshold)
	{
		if (!hasNeighbor) return false;
		return Math.Abs(delta) >= cliffThreshold;
	}

	private static float ResolveSingleSide(bool hasNeighbor, bool neighborIsCliff, float neighborDelta)
	{
		if (!hasNeighbor) return 0.0f;
		if (neighborIsCliff) return 0.0f;
		return neighborDelta;
	}

	private static float ResolveNormalSlope(bool hasPositive, bool hasNegative, float heightPositive, float heightNegative, float deltaPositive, float deltaNegative)
	{
		if (hasPositive)
		{
			if (hasNegative)
			{
				return (heightPositive - heightNegative) * 0.5f;
			}

			return deltaPositive;
		}

		if (hasNegative)
		{
			return deltaNegative;
		}

		return 0.0f;
	}

	private static float ResolveSlope(bool hasPositive, bool hasNegative, float deltaPositive, float deltaNegative, bool positiveIsCliff, bool negativeIsCliff, float heightPositive, float heightNegative)
	{
		if (positiveIsCliff)
		{
			if (negativeIsCliff) return 0.0f;
			return ResolveSingleSide(hasNegative, negativeIsCliff, deltaNegative);
		}

		if (negativeIsCliff)
		{
			return ResolveSingleSide(hasPositive, positiveIsCliff, deltaPositive);
		}

		return ResolveNormalSlope(hasPositive, hasNegative, heightPositive, heightNegative, deltaPositive, deltaNegative);
	}

	private float GetSlopeDx(in TerrainState state, int x, int z, float h, float cliffThreshold)
	{
		bool hasRight = x < state.Width;
		bool hasLeft = x > 0;

		float heightRight = hasRight ? GetVertexHeight(in state, x + 1, z) : 0.0f;
		float heightLeft = hasLeft ? GetVertexHeight(in state, x - 1, z) : 0.0f;

		float deltaRight = hasRight ? heightRight - h : 0.0f;
		float deltaLeft = hasLeft ? h - heightLeft : 0.0f;

		bool rightIsCliff = IsCliff(hasRight, deltaRight, cliffThreshold);
		bool leftIsCliff = IsCliff(hasLeft, deltaLeft, cliffThreshold);

		return ResolveSlope(hasRight, hasLeft, deltaRight, deltaLeft, rightIsCliff, leftIsCliff, heightRight, heightLeft);
	}


	private float GetSlopeDz(in TerrainState state, int x, int z, float h, float cliffThreshold)
	{
		bool hasUp = z < state.Depth;
		bool hasDown = z > 0;

		float heightUp = hasUp ? GetVertexHeight(in state, x, z + 1) : 0.0f;
		float heightDown = hasDown ? GetVertexHeight(in state, x, z - 1) : 0.0f;

		float deltaUp = hasUp ? heightUp - h : 0.0f;
		float deltaDown = hasDown ? h - heightDown : 0.0f;

		bool upIsCliff = IsCliff(hasUp, deltaUp, cliffThreshold);
		bool downIsCliff = IsCliff(hasDown, deltaDown, cliffThreshold);

		return ResolveSlope(hasUp, hasDown, deltaUp, deltaDown, upIsCliff, downIsCliff, heightUp, heightDown);
	}
}
