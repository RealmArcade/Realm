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

		DtNavMesh? regionNavMesh = state.NavMesh;
		if (regionNavMesh != null)
		{
			float pad = quadSize * 0.5f;
			IterateNavMeshPolys(ref state, regionNavMesh, (ref TerrainState s, DtMeshTile tile, DtPoly poly, long polyRefBase, int p) =>
			{
				GetPolyCenter(tile, poly, out float avgX, out float avgZ);

				if (avgX < x1 - pad || avgX > x2 + pad || avgZ < z1 - pad || avgZ > z2 + pad)
					return;

				Span<Vector2> polygonVertices = stackalloc Vector2[poly.vertCount];
				int vertexCount = CollectPolygonVertices(tile, poly, polygonVertices);
				int cellFlags = ClassifyPolygonPathingFlags(s.PathingCodes, width, depth, quadSize, polygonVertices[..vertexCount]);

				long polyRef = polyRefBase | (long)p;
				regionNavMesh.SetPolyFlags(polyRef, cellFlags);
				bool isWalkable = (cellFlags & (int)TerrainPathingFlags.Ground) != 0;
				regionNavMesh.SetPolyArea(polyRef, isWalkable ? (char)1 : (char)0);
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

	private void RestoreNavMeshPoly(ref TerrainState state, DtNavMesh navMesh, DtMeshTile tile, DtPoly poly, long polyRefBase, int polyIndex, int width, int depth, float quadSize)
	{
		Span<Vector2> polygonVertices = stackalloc Vector2[poly.vertCount];
		int vertexCount = CollectPolygonVertices(tile, poly, polygonVertices);
		int pathFlags = ClassifyPolygonPathingFlags(state.PathingCodes, width, depth, quadSize, polygonVertices[..vertexCount]);
		long polyRef = polyRefBase | (long)polyIndex;
		navMesh.SetPolyFlags(polyRef, pathFlags);
		navMesh.SetPolyArea(polyRef, (char)1);
	}
}
