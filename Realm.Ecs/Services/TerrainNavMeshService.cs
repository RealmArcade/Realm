using Arch.Core;
using DotRecast.Core.Numerics;
using DotRecast.Detour;
using DotRecast.Recast;
using DotRecast.Recast.Geom;
using Realm.Ecs.Common;
using Realm.Ecs.Components.Core;
using Realm.Ecs.Components.Movement;
using Realm.Ecs.Components.Terrain;
using System;
using System.Collections.Generic;

namespace Realm.Ecs.Services;

internal class TerrainNavMeshService
{
	// Recast/Detour bake tuning. Smaller cell sizes improve path fidelity but increase bake cost.
	private const float NavMeshCellHeight = 0.1f;
	private const float AgentRadius = 0.1f;
	private const float AgentHeight = 2.5f;
	private const float AgentMaxClimb = 0.9f;
	private const float AgentMaxSlope = 55.0f;

	private readonly WorldAccessor _ecsWorldAccessor;
	private readonly ForEachWithEntity<Position, MoveTo, PathFollow> _invalidatePathsDelegate;
	private System.Numerics.Vector3 _invalidateCenter;
	private float _invalidateRadiusSq;

	private readonly ForEachWithEntity<Position, CollisionRadius> _checkOtherObstaclesDelegate;
	private float _checkCellX;
	private float _checkCellZ;
	private bool _isCellBlockedByOtherObstacle;

	public TerrainNavMeshService(WorldAccessor ecsWorldAccessor)
	{
		_ecsWorldAccessor = ecsWorldAccessor;
		_invalidatePathsDelegate = InvalidatePathsQueryAction;
		_checkOtherObstaclesDelegate = CheckOtherObstaclesQueryAction;
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

	public void InvalidateIntersectingPaths(System.Numerics.Vector3 center, float radius)
	{
		_invalidateCenter = center;
		_invalidateRadiusSq = radius * radius;
		_ecsWorldAccessor.Current.Query(in QueryCache.AllPositionAndMoveToAndPathFollowNoneDeadQuery, _invalidatePathsDelegate);
	}

	private void CheckOtherObstaclesQueryAction(Entity entity, ref Position pos, ref CollisionRadius colRad)
	{
		if (_isCellBlockedByOtherObstacle) return;
		float scale = _ecsWorldAccessor.Current.Has<CollisionScale>(entity) ? _ecsWorldAccessor.Current.Get<CollisionScale>(entity).Value : 1.0f;
		float radius = colRad.Value * scale;
		float dx = _checkCellX - pos.Value.X;
		float dz = _checkCellZ - pos.Value.Z;
		if (dx * dx + dz * dz <= radius * radius)
		{
			_isCellBlockedByOtherObstacle = true;
		}
	}

	private bool IsPointCoveredByAnyObstacle(float px, float pz)
	{
		_checkCellX = px;
		_checkCellZ = pz;
		_isCellBlockedByOtherObstacle = false;
		_ecsWorldAccessor.Current.Query(in QueryCache.AllPositionAndCollisionRadiusQuery, _checkOtherObstaclesDelegate);
		return _isCellBlockedByOtherObstacle;
	}

	public void CarveObstacle(ref TerrainState state, System.Numerics.Vector3 pos, float radius)
	{
		if (state.NavMesh == null) return;
		int width = state.Width;
		int depth = state.Depth;
		float quadSize = state.QuadSize;
		float halfW = width / 2.0f * quadSize;
		float halfD = depth / 2.0f * quadSize;

		int minX = Math.Clamp((int)Math.Floor((pos.X - radius + halfW) / quadSize), 0, width - 1);
		int maxX = Math.Clamp((int)Math.Ceiling((pos.X + radius + halfW) / quadSize), 0, width - 1);
		int minZ = Math.Clamp((int)Math.Floor((pos.Z - radius + halfD) / quadSize), 0, depth - 1);
		int maxZ = Math.Clamp((int)Math.Ceiling((pos.Z + radius + halfD) / quadSize), 0, depth - 1);

		CarvePathingGrid(ref state, pos, radius, minX, maxX, minZ, maxZ, width, depth, quadSize);
		CarveNavMeshTiles(ref state, pos, radius, quadSize);

		InvalidateIntersectingPaths(pos, radius + quadSize * 1.5f);
	}

	public void CarveObstacleBox(ref TerrainState state, System.Numerics.Vector3 pos, float halfWidth, float halfDepth)
	{
		if (state.NavMesh == null) return;
		int width = state.Width;
		int depth = state.Depth;
		float quadSize = state.QuadSize;
		float halfW = width / 2.0f * quadSize;
		float halfD = depth / 2.0f * quadSize;

		int minX = Math.Clamp((int)Math.Floor((pos.X - halfWidth + halfW) / quadSize), 0, width - 1);
		int maxX = Math.Clamp((int)Math.Ceiling((pos.X + halfWidth + halfW) / quadSize), 0, width - 1);
		int minZ = Math.Clamp((int)Math.Floor((pos.Z - halfDepth + halfD) / quadSize), 0, depth - 1);
		int maxZ = Math.Clamp((int)Math.Ceiling((pos.Z + halfDepth + halfD) / quadSize), 0, depth - 1);

		CarvePathingGridBox(ref state, pos, halfWidth, halfDepth, minX, maxX, minZ, maxZ, width, depth, quadSize);
		CarveNavMeshTilesBox(ref state, pos, halfWidth, halfDepth, quadSize);

		float maxDim = Math.Max(halfWidth, halfDepth);
		InvalidateIntersectingPaths(pos, maxDim + quadSize * 1.5f);
	}

	private void CarvePathingGrid(ref TerrainState state, System.Numerics.Vector3 pos, float radius, int minX, int maxX, int minZ, int maxZ, int width, int depth, float quadSize)
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
					state.PathingCodes[x, z] &= ~(int)(TerrainPathingFlags.Ground | TerrainPathingFlags.Buildable);
				}
			}
		}
	}

	private void CarveNavMeshTiles(ref TerrainState state, System.Numerics.Vector3 pos, float radius, float quadSize)
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

	private void CarvePathingGridBox(ref TerrainState state, System.Numerics.Vector3 pos, float halfWidth, float halfDepth, int minX, int maxX, int minZ, int maxZ, int width, int depth, float quadSize)
	{
		if (state.PathingCodes == null) return;
		for (int z = minZ; z <= maxZ; z++)
		{
			for (int x = minX; x <= maxX; x++)
			{
				float lx = (x + 0.5f - width / 2.0f) * quadSize;
				float lz = (z + 0.5f - depth / 2.0f) * quadSize;
				if (Math.Abs(lx - pos.X) <= halfWidth && Math.Abs(lz - pos.Z) <= halfDepth)
				{
					state.PathingCodes[x, z] &= ~(int)(TerrainPathingFlags.Ground | TerrainPathingFlags.Buildable);
				}
			}
		}
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

	private void CarveNavMeshTilesBox(ref TerrainState state, System.Numerics.Vector3 pos, float halfWidth, float halfDepth, float quadSize)
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
					GetPolyCenter(tile, poly, out float avgX, out float avgZ);

					if (Math.Abs(avgX - pos.X) > halfWidth + quadSize * 0.5f || Math.Abs(avgZ - pos.Z) > halfDepth + quadSize * 0.5f)
						continue;

					long polyRef = polyRefBase | (long)p;
					state.NavMesh.SetPolyFlags(polyRef, 0);
					state.NavMesh.SetPolyArea(polyRef, (char)0);
			}
		}
	}

	public void UncarveObstacle(ref TerrainState state, System.Numerics.Vector3 pos, float radius)
	{
		if (state.NavMesh == null) return;
		int width = state.Width;
		int depth = state.Depth;
		float quadSize = state.QuadSize;
		float halfW = width / 2.0f * quadSize;
		float halfD = depth / 2.0f * quadSize;

		int minX = Math.Clamp((int)Math.Floor((pos.X - radius + halfW) / quadSize), 0, width - 1);
		int maxX = Math.Clamp((int)Math.Ceiling((pos.X + radius + halfW) / quadSize), 0, width - 1);
		int minZ = Math.Clamp((int)Math.Floor((pos.Z - radius + halfD) / quadSize), 0, depth - 1);
		int maxZ = Math.Clamp((int)Math.Ceiling((pos.Z + radius + halfD) / quadSize), 0, depth - 1);

		UncarvePathingGrid(ref state, pos, radius, minX, maxX, minZ, maxZ, width, depth, quadSize);
		UncarveNavMeshTiles(ref state, pos, radius, width, depth, quadSize);

		InvalidateIntersectingPaths(pos, radius + quadSize * 2f);
	}

	public void UncarveObstacleBox(ref TerrainState state, System.Numerics.Vector3 pos, float halfWidth, float halfDepth)
	{
		if (state.NavMesh == null) return;
		int width = state.Width;
		int depth = state.Depth;
		float quadSize = state.QuadSize;
		float halfW = width / 2.0f * quadSize;
		float halfD = depth / 2.0f * quadSize;

		int minX = Math.Clamp((int)Math.Floor((pos.X - halfWidth + halfW) / quadSize), 0, width - 1);
		int maxX = Math.Clamp((int)Math.Ceiling((pos.X + halfWidth + halfW) / quadSize), 0, width - 1);
		int minZ = Math.Clamp((int)Math.Floor((pos.Z - halfDepth + halfD) / quadSize), 0, depth - 1);
		int maxZ = Math.Clamp((int)Math.Ceiling((pos.Z + halfDepth + halfD) / quadSize), 0, depth - 1);

		UncarvePathingGridBox(ref state, pos, halfWidth, halfDepth, minX, maxX, minZ, maxZ, width, depth, quadSize);
		UncarveNavMeshTilesBox(ref state, pos, halfWidth, halfDepth, width, depth, quadSize);

		float maxDim = Math.Max(halfWidth, halfDepth);
		InvalidateIntersectingPaths(pos, maxDim + quadSize * 2f);
	}

	private void UncarvePathingGrid(ref TerrainState state, System.Numerics.Vector3 pos, float radius, int minX, int maxX, int minZ, int maxZ, int width, int depth, float quadSize)
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
					if (!IsPointCoveredByAnyObstacle(lx, lz))
					{
						RestorePathingCell(ref state, x, z);
					}
				}
			}
		}
	}

	private void UncarveNavMeshTiles(ref TerrainState state, System.Numerics.Vector3 pos, float radius, int width, int depth, float quadSize)
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
					GetPolyCenter(tile, poly, out float avgX, out float avgZ);
				float dx = avgX - pos.X;
				float dz = avgZ - pos.Z;

					if (dx * dx + dz * dz > effRadiusSq || IsPointCoveredByAnyObstacle(avgX, avgZ))
						continue;

					RestoreNavMeshPoly(ref state, polyRefBase, p, avgX, avgZ, width, depth, quadSize);
			}
		}
	}

	private void UncarvePathingGridBox(ref TerrainState state, System.Numerics.Vector3 pos, float halfWidth, float halfDepth, int minX, int maxX, int minZ, int maxZ, int width, int depth, float quadSize)
	{
		if (state.PathingCodes == null) return;
		for (int z = minZ; z <= maxZ; z++)
		{
			for (int x = minX; x <= maxX; x++)
			{
				float lx = (x + 0.5f - width / 2.0f) * quadSize;
				float lz = (z + 0.5f - depth / 2.0f) * quadSize;
				if (Math.Abs(lx - pos.X) <= halfWidth && Math.Abs(lz - pos.Z) <= halfDepth)
				{
					if (!IsPointCoveredByAnyObstacle(lx, lz))
					{
						RestorePathingCell(ref state, x, z);
					}
				}
			}
		}
	}

	private void UncarveNavMeshTilesBox(ref TerrainState state, System.Numerics.Vector3 pos, float halfWidth, float halfDepth, int width, int depth, float quadSize)
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
					GetPolyCenter(tile, poly, out float avgX, out float avgZ);

					if (Math.Abs(avgX - pos.X) > halfWidth + quadSize * 0.5f || Math.Abs(avgZ - pos.Z) > halfDepth + quadSize * 0.5f)
						continue;

					if (!IsPointCoveredByAnyObstacle(avgX, avgZ))
				{
						RestoreNavMeshPoly(ref state, polyRefBase, p, avgX, avgZ, width, depth, quadSize);
				}
			}
		}
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

	private void RestoreNavMeshPoly(ref TerrainState state, long polyRefBase, int polyIndex, float avgX, float avgZ, int width, int depth, float quadSize)
	{
		int xGrid = Math.Clamp((int)Math.Floor(avgX / quadSize + width / 2.0f), 0, width - 1);
		int zGrid = Math.Clamp((int)Math.Floor(avgZ / quadSize + depth / 2.0f), 0, depth - 1);
		var pathFlags = state.PathingCodes != null ? (TerrainPathingFlags)state.PathingCodes[xGrid, zGrid] : TerrainPathingFlags.Ground | TerrainPathingFlags.Buildable;
		long polyRef = polyRefBase | (long)polyIndex;
		state.NavMesh.SetPolyFlags(polyRef, (int)pathFlags);
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
		ApplyObstaclesToHeights(bakeHeights, obstacles, width, depth, quadSize, halfW, halfD);

		var geom = CreateInputGeom(bakeHeights, width, depth, quadSize);
		AddUnwalkableVolumes(in state, geom, width, depth, quadSize);

		BuildNavMesh(ref state, geom, width, depth, quadSize);
	}

	private List<(System.Numerics.Vector3 Pos, float Radius)> GetNavMeshObstacles()
	{
		var obstacles = new List<(System.Numerics.Vector3 Pos, float Radius)>();
		var obstacleQuery = QueryCache.AllPositionAndCollisionRadiusQuery;
		_ecsWorldAccessor.Current.Query(in obstacleQuery, (Entity ent, ref Position pos, ref CollisionRadius colRad) =>
		{
			float scale = _ecsWorldAccessor.Current.Has<CollisionScale>(ent) ? _ecsWorldAccessor.Current.Get<CollisionScale>(ent).Value : 1.0f;
			float radius = colRad.Value * scale;
			obstacles.Add((pos.Value, radius));
		});
		return obstacles;
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

	private void ApplyObstaclesToHeights(float[,] bakeHeights, List<(System.Numerics.Vector3 Pos, float Radius)> obstacles, int width, int depth, float quadSize, float halfW, float halfD)
	{
		foreach (var obs in obstacles)
		{
			int nearestX = Math.Clamp((int)Math.Round((obs.Pos.X + halfW) / quadSize), 0, width);
			int nearestZ = Math.Clamp((int)Math.Round((obs.Pos.Z + halfD) / quadSize), 0, depth);
			bakeHeights[nearestX, nearestZ] = 20.0f;

			float radius = obs.Radius;
			float effectiveRadius = Math.Max(quadSize * 0.5f, radius - quadSize * 0.51f);
			int minX = Math.Clamp((int)Math.Floor((obs.Pos.X - radius + halfW) / quadSize), 0, width);
			int maxX = Math.Clamp((int)Math.Ceiling((obs.Pos.X + radius + halfW) / quadSize), 0, width);
			int minZ = Math.Clamp((int)Math.Floor((obs.Pos.Z - radius + halfD) / quadSize), 0, depth);
			int maxZ = Math.Clamp((int)Math.Ceiling((obs.Pos.Z + radius + halfD) / quadSize), 0, depth);

			for (int z = minZ; z <= maxZ; z++)
			{
				for (int x = minX; x <= maxX; x++)
				{
					float lx = (x - width / 2.0f) * quadSize;
					float lz = (z - depth / 2.0f) * quadSize;
					float dx = lx - obs.Pos.X;
					float dz = lz - obs.Pos.Z;
					if (dx * dx + dz * dz <= effectiveRadius * effectiveRadius)
					{
						bakeHeights[x, z] = 20.0f;
					}
				}
			}
		}
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
		Span<System.Numerics.Vector2> polyVerts = stackalloc System.Numerics.Vector2[12];
		for (int i = 0; i < mesh.npolys; i++)
		{
			pars.polyAreas[i] = mesh.areas[i];

			float sumX = 0f;
			float sumZ = 0f;
			int nv = 0;
			for (int j = 0; j < mesh.nvp; j++)
			{
				int vIdx = mesh.polys[i * mesh.nvp * 2 + j];
				if (vIdx < 0 || vIdx >= mesh.nverts)
					break;

				float wx = bmin.X + mesh.verts[vIdx * 3] * mesh.cs;
				float wz = bmin.Z + mesh.verts[vIdx * 3 + 2] * mesh.cs;
				sumX += wx;
				sumZ += wz;

				if (nv < polyVerts.Length)
					polyVerts[nv++] = new System.Numerics.Vector2(wx, wz);
			}

			float avgX = nv > 0 ? sumX / nv : 0f;
			float avgZ = nv > 0 ? sumZ / nv : 0f;

			int xGrid = Math.Clamp((int)Math.Floor(avgX / quadSize + width / 2.0f), 0, width - 1);
			int zGrid = Math.Clamp((int)Math.Floor(avgZ / quadSize + depth / 2.0f), 0, depth - 1);
			var pathFlags = state.PathingCodes != null ? (TerrainPathingFlags)state.PathingCodes[xGrid, zGrid] : TerrainPathingFlags.Ground | TerrainPathingFlags.Buildable;
			pars.polyFlags[i] = (int)pathFlags;
		}
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

		InitializeNavMeshPolys(pars, result.Mesh, bmin, quadSize, width, depth, in state);

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

	public void GetHeightAndNormal(in TerrainState state, float worldX, float worldZ, out float height, out System.Numerics.Vector3 normal)
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
		System.Numerics.Vector3 n00 = GetVertexNormal(in state, x0, z0);
		System.Numerics.Vector3 n10 = GetVertexNormal(in state, Math.Min(state.Width, x0 + 1), z0);
		System.Numerics.Vector3 n01 = GetVertexNormal(in state, x0, Math.Min(state.Depth, z0 + 1));
		System.Numerics.Vector3 n11 = GetVertexNormal(in state, Math.Min(state.Width, x0 + 1), Math.Min(state.Depth, z0 + 1));
		normal = System.Numerics.Vector3.Normalize((1 - tx) * (1 - tz) * n00 + tx * (1 - tz) * n10 + (1 - tx) * tz * n01 + tx * tz * n11);
	}

	private System.Numerics.Vector3 GetVertexNormal(in TerrainState state, int x, int z)
	{
		var cells = state.Cells;
		if (cells == null) return System.Numerics.Vector3.UnitY;
		float h = GetVertexHeight(in state, x, z);
		float cliffThreshold = 0.95f * Math.Max(0.1f, state.QuadSize);

		float dx = GetSlopeDx(in state, x, z, h, cliffThreshold);
		float dz = GetSlopeDz(in state, x, z, h, cliffThreshold);

		if (Math.Abs(dx) < 0.001f && Math.Abs(dz) < 0.001f)
		{
			return System.Numerics.Vector3.UnitY;
		}

		System.Numerics.Vector3 tangentX = System.Numerics.Vector3.Normalize(new System.Numerics.Vector3(state.QuadSize, dx, 0.0f));
		System.Numerics.Vector3 tangentZ = System.Numerics.Vector3.Normalize(new System.Numerics.Vector3(0.0f, dz, state.QuadSize));
		return System.Numerics.Vector3.Normalize(System.Numerics.Vector3.Cross(tangentZ, tangentX));
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
