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
	public void CarveObstacle(ref TerrainState state, Vector3 pos, float radius)
	{
		DtNavMesh? navMesh = state.NavMesh;
		if (navMesh == null) return;
		int width = state.Width;
		int depth = state.Depth;
		float quadSize = state.QuadSize;
		GetGridBounds(width, depth, quadSize, pos, radius, radius, out int minX, out int maxX, out int minZ, out int maxZ);

		CarvePathingGrid(ref state, pos, radius, minX, maxX, minZ, maxZ, width, depth, quadSize);
		CarveNavMeshTiles(ref state, navMesh, pos, radius, quadSize);

		InvalidateIntersectingPaths(pos, radius + quadSize * 1.5f);
	}

	public void CarveObstacleBox(ref TerrainState state, Vector3 pos, float halfWidth, float halfDepth)
	{
		DtNavMesh? navMesh = state.NavMesh;
		if (navMesh == null) return;
		int width = state.Width;
		int depth = state.Depth;
		float quadSize = state.QuadSize;
		GetGridBounds(width, depth, quadSize, pos, halfWidth, halfDepth, out int minX, out int maxX, out int minZ, out int maxZ);

		CarvePathingGridBox(ref state, pos, halfWidth, halfDepth, minX, maxX, minZ, maxZ, width, depth, quadSize);
		CarveNavMeshTilesBox(ref state, navMesh, pos, halfWidth, halfDepth, quadSize);

		float maxDim = Math.Max(halfWidth, halfDepth);
		InvalidateIntersectingPaths(pos, maxDim + quadSize * 1.5f);
	}

	private void ProcessPathingGrid(ref TerrainState state, int[,] pathingCodes, Vector3 pos, float radius, int minX, int maxX, int minZ, int maxZ, int width, int depth, float quadSize, ProcessPathingCellBoxDelegate processCell)
	{
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
		int[,]? pathingCodes = state.PathingCodes;
		if (pathingCodes == null) return;

		ProcessPathingGrid(ref state, pathingCodes, pos, radius, minX, maxX, minZ, maxZ, width, depth, quadSize, (ref TerrainState s, int x, int z) =>
		{
			pathingCodes[x, z] &= ~(int)(TerrainPathingFlags.Ground | TerrainPathingFlags.Buildable);
		});
	}

	private delegate void ProcessPolyDelegate(ref TerrainState state, DtMeshTile tile, DtPoly poly, long polyRefBase, int p);

	private void IterateNavMeshPolys(ref TerrainState state, DtNavMesh navMesh, ProcessPolyDelegate processPoly)
	{
		int maxTiles = navMesh.GetMaxTiles();
		for (int t = 0; t < maxTiles; t++)
		{
			var tile = navMesh.GetTile(t);
			if (tile?.data?.header == null) continue;
			long polyRefBase = navMesh.GetPolyRefBase(tile);
			for (int p = 0; p < tile.data.header.polyCount; p++)
			{
				var poly = tile.data.polys[p];
				processPoly(ref state, tile, poly, polyRefBase, p);
			}
		}
	}

	private void ProcessNavMeshTiles(ref TerrainState state, DtNavMesh navMesh, Vector3 pos, float radius, float quadSize, ProcessPolyDelegate processPoly)
	{
		float effRadiusSq = (radius + quadSize * 0.5f) * (radius + quadSize * 0.5f);
		IterateNavMeshPolys(ref state, navMesh, (ref TerrainState s, DtMeshTile tile, DtPoly poly, long polyRefBase, int p) =>
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

	private void CarveNavMeshTiles(ref TerrainState state, DtNavMesh navMesh, Vector3 pos, float radius, float quadSize)
	{
		int maxTiles = navMesh.GetMaxTiles();
		float effRadiusSq = (radius + quadSize * 0.5f) * (radius + quadSize * 0.5f);
		for (int t = 0; t < maxTiles; t++)
		{
			var tile = navMesh.GetTile(t);
			if (tile?.data?.header == null) continue;
			long polyRefBase = navMesh.GetPolyRefBase(tile);
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
					navMesh.SetPolyFlags(polyRef, 0);
					navMesh.SetPolyArea(polyRef, (char)0);
				}
			}
		}
	}

	private delegate void ProcessPathingCellBoxDelegate(ref TerrainState state, int x, int z);

	private void ProcessPathingGridBox(ref TerrainState state, int[,] pathingCodes, Vector3 pos, float halfWidth, float halfDepth, int minX, int maxX, int minZ, int maxZ, int width, int depth, float quadSize, ProcessPathingCellBoxDelegate processCell)
	{
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
		int[,]? pathingCodes = state.PathingCodes;
		if (pathingCodes == null) return;

		ProcessPathingGridBox(ref state, pathingCodes, pos, halfWidth, halfDepth, minX, maxX, minZ, maxZ, width, depth, quadSize, (ref TerrainState s, int x, int z) =>
		{
			pathingCodes[x, z] &= ~(int)(TerrainPathingFlags.Ground | TerrainPathingFlags.Buildable);
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

	private void ProcessNavMeshTilesBox(ref TerrainState state, DtNavMesh navMesh, Vector3 pos, float halfWidth, float halfDepth, float quadSize, ProcessPolyDelegate processPoly)
	{
		IterateNavMeshPolys(ref state, navMesh, (ref TerrainState s, DtMeshTile tile, DtPoly poly, long polyRefBase, int p) =>
		{
			GetPolyCenter(tile, poly, out float avgX, out float avgZ);

			if (IsPolyInBox(avgX, avgZ, pos, halfWidth, halfDepth, quadSize))
			{
				processPoly(ref s, tile, poly, polyRefBase, p);
			}
		});
	}

	private void CarveNavMeshTilesBox(ref TerrainState state, DtNavMesh navMesh, Vector3 pos, float halfWidth, float halfDepth, float quadSize)
	{
		ProcessNavMeshTilesBox(ref state, navMesh, pos, halfWidth, halfDepth, quadSize, (ref TerrainState s, DtMeshTile tile, DtPoly poly, long polyRefBase, int p) =>
		{
			long polyRef = polyRefBase | (long)p;
			navMesh.SetPolyFlags(polyRef, 0);
			navMesh.SetPolyArea(polyRef, (char)0);
		});
	}

	public void UncarveObstacle(ref TerrainState state, Vector3 pos, float radius)
	{
		DtNavMesh? navMesh = state.NavMesh;
		if (navMesh == null) return;
		int width = state.Width;
		int depth = state.Depth;
		float quadSize = state.QuadSize;
		GetGridBounds(width, depth, quadSize, pos, radius, radius, out int minX, out int maxX, out int minZ, out int maxZ);

		UncarvePathingGrid(ref state, pos, radius, minX, maxX, minZ, maxZ, width, depth, quadSize);
		UncarveNavMeshTiles(ref state, navMesh, pos, radius, width, depth, quadSize);

		InvalidateIntersectingPaths(pos, radius + quadSize * 2f);
	}

	public void UncarveObstacleBox(ref TerrainState state, Vector3 pos, float halfWidth, float halfDepth)
	{
		DtNavMesh? navMesh = state.NavMesh;
		if (navMesh == null) return;
		int width = state.Width;
		int depth = state.Depth;
		float quadSize = state.QuadSize;
		GetGridBounds(width, depth, quadSize, pos, halfWidth, halfDepth, out int minX, out int maxX, out int minZ, out int maxZ);

		UncarvePathingGridBox(ref state, pos, halfWidth, halfDepth, minX, maxX, minZ, maxZ, width, depth, quadSize);
		UncarveNavMeshTilesBox(ref state, navMesh, pos, halfWidth, halfDepth, width, depth, quadSize);

		float maxDim = Math.Max(halfWidth, halfDepth);
		InvalidateIntersectingPaths(pos, maxDim + quadSize * 2f);
	}

	private void UncarvePathingGrid(ref TerrainState state, Vector3 pos, float radius, int minX, int maxX, int minZ, int maxZ, int width, int depth, float quadSize)
	{
		int[,]? pathingCodes = state.PathingCodes;
		if (pathingCodes == null) return;

		ProcessPathingGrid(ref state, pathingCodes, pos, radius, minX, maxX, minZ, maxZ, width, depth, quadSize, (ref TerrainState s, int x, int z) =>
		{
			float lx = (x + 0.5f - width / 2.0f) * quadSize;
			float lz = (z + 0.5f - depth / 2.0f) * quadSize;
			if (!IsPointCoveredByAnyObstacle(lx, lz))
			{
				RestorePathingCell(ref s, x, z);
			}
		});
	}

	private void UncarveNavMeshTiles(ref TerrainState state, DtNavMesh navMesh, Vector3 pos, float radius, int width, int depth, float quadSize)
	{
		ProcessNavMeshTiles(ref state, navMesh, pos, radius, quadSize, (ref TerrainState s, DtMeshTile tile, DtPoly poly, long polyRefBase, int p) =>
		{
			GetPolyCenter(tile, poly, out float avgX, out float avgZ);
			if (IsPointCoveredByAnyObstacle(avgX, avgZ))
				return;

			RestoreNavMeshPoly(ref s, navMesh, tile, poly, polyRefBase, p, width, depth, quadSize);
		});
	}

	private void UncarvePathingGridBox(ref TerrainState state, Vector3 pos, float halfWidth, float halfDepth, int minX, int maxX, int minZ, int maxZ, int width, int depth, float quadSize)
	{
		int[,]? pathingCodes = state.PathingCodes;
		if (pathingCodes == null) return;

		ProcessPathingGridBox(ref state, pathingCodes, pos, halfWidth, halfDepth, minX, maxX, minZ, maxZ, width, depth, quadSize, (ref TerrainState s, int x, int z) =>
		{
			float lx = (x + 0.5f - width / 2.0f) * quadSize;
			float lz = (z + 0.5f - depth / 2.0f) * quadSize;
			if (!IsPointCoveredByAnyObstacle(lx, lz))
			{
				RestorePathingCell(ref s, x, z);
			}
		});
	}

	private void UncarveNavMeshTilesBox(ref TerrainState state, DtNavMesh navMesh, Vector3 pos, float halfWidth, float halfDepth, int width, int depth, float quadSize)
	{
		ProcessNavMeshTilesBox(ref state, navMesh, pos, halfWidth, halfDepth, quadSize, (ref TerrainState s, DtMeshTile tile, DtPoly poly, long polyRefBase, int p) =>
		{
			GetPolyCenter(tile, poly, out float avgX, out float avgZ);
			if (!IsPointCoveredByAnyObstacle(avgX, avgZ))
			{
				RestoreNavMeshPoly(ref s, navMesh, tile, poly, polyRefBase, p, width, depth, quadSize);
			}
		});
	}
}
