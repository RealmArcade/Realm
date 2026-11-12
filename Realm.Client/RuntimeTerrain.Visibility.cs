using Realm.Shared.Maps;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text.Json.Nodes;
using DotRecast.Detour;
using Godot;
using Godot.Collections;
using Realm.Client.Core;
using Realm.Client.Services;
using Realm.Client.UI;
using Realm.Ecs;
using Realm.Ecs.Components.Terrain;
using Realm.Ecs.Services;
using Realm.Shared.Services;
using Realm.Shared.Textures;
using Array = Godot.Collections.Array;

namespace Realm.Client;
public partial class RuntimeTerrain
{
	public void SetAllChunksVisible(bool visible)
	{
		foreach (var chunk in _chunks)
		{
			if (GodotObject.IsInstanceValid(chunk.MeshInstance) && chunk.MeshInstance.Visible != visible) chunk.MeshInstance.Visible = visible;
			if (chunk.ProfileWaterMeshes != null)
			{
				foreach (var (pMesh, _) in chunk.ProfileWaterMeshes.Values)
				{
					if (GodotObject.IsInstanceValid(pMesh) && pMesh.Visible != visible) pMesh.Visible = visible;
				}
			}
		}
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static bool IntersectsFrustum(ReadOnlySpan<Plane> frustumPlanes, Aabb aabb)
	{
		aabb = aabb.Grow(8.0f);
		Vector3 min = aabb.Position;
		Vector3 max = aabb.End;

		for (int i = 0; i < frustumPlanes.Length; i++)
		{
			ref readonly Plane plane = ref frustumPlanes[i];
			Vector3 n = new Vector3(
				plane.Normal.X >= 0 ? min.X : max.X,
				plane.Normal.Y >= 0 ? min.Y : max.Y,
				plane.Normal.Z >= 0 ? min.Z : max.Z
			);

			if (plane.DistanceTo(n) > 0)
			{
				return false;
			}
		}

		return true;
	}

	public static bool IntersectsFrustum(Array<Plane> frustumPlanes, Aabb aabb)
	{
		if (frustumPlanes == null || frustumPlanes.Count == 0) return true;
		Span<Plane> span = stackalloc Plane[Math.Min(frustumPlanes.Count, 32)];
		for (int i = 0; i < span.Length; i++) span[i] = frustumPlanes[i];
		return IntersectsFrustum((ReadOnlySpan<Plane>)span, aabb);
	}

	private Vector3 GetWorldPosition(float gridX, float gridZ, float height, float halfWQuadSize, float halfDQuadSize, float quadSize)
	{
		float lx = gridX * quadSize - halfWQuadSize;
		float lz = gridZ * quadSize - halfDQuadSize;
		return new Vector3(lx, height, lz);
	}

	private Vector3 GetWorldPosition(float gridX, float gridZ, float height)
	{
		return GetWorldPosition(gridX, gridZ, height, (Width / 2.0f) * QuadSize, (Depth / 2.0f) * QuadSize, QuadSize);
	}

	private void CountQuadElements(
		int x, int z,
		TerrainCell[,] cells,
		int w, int d,
		ref int totalVertices,
		ref int totalIndices)
	{
		if (cells == null) return;
		int cellW = cells.GetLength(0);
		int cellD = cells.GetLength(1);
		if (x < 0 || x >= cellW || z < 0 || z >= cellD) return;

		int perimeterEdgeCount = CountPerimeterEdges(x, z, w, d);

		totalVertices += 12 + perimeterEdgeCount * 4;
		totalIndices += 12 + perimeterEdgeCount * 6;
	}

	private const float PerimeterSkirtOvershoot = 0.5f;
	private float _perimeterSkirtBottomY = -PerimeterSkirtOvershoot;

	private float ComputePerimeterSkirtBottomY(TerrainCell[,] cells, int w, int d)
	{
		float lowestEdgeHeight = 0.0f;

		for (int x = 0; x <= w; x++)
		{
			lowestEdgeHeight = Math.Min(lowestEdgeHeight, GetGridNodeHeight(x, 0, cells, w, d));
			lowestEdgeHeight = Math.Min(lowestEdgeHeight, GetGridNodeHeight(x, d, cells, w, d));
		}

		for (int z = 0; z <= d; z++)
		{
			lowestEdgeHeight = Math.Min(lowestEdgeHeight, GetGridNodeHeight(0, z, cells, w, d));
			lowestEdgeHeight = Math.Min(lowestEdgeHeight, GetGridNodeHeight(w, z, cells, w, d));
		}

		return lowestEdgeHeight - PerimeterSkirtOvershoot;
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	private static int CountPerimeterEdges(int x, int z, int w, int d)
	{
		int edgeCount = 0;
		if (z == 0) edgeCount++;
		if (z == d - 1) edgeCount++;
		if (x == 0) edgeCount++;
		if (x == w - 1) edgeCount++;
		return edgeCount;
	}

}
