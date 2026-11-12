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
	public void UpdateMeshAndPhysics(bool rebuildPhysics = true, bool rebuildNavMesh = true, Rect2I? affectedRegion = null, bool rebuildWater = true)
	{
		UpdateMeshAndPhysics(rebuildPhysics, rebuildNavMesh, affectedRegion.HasValue ? new[] { affectedRegion.Value } : (IEnumerable<Rect2I>?)null, rebuildWater);
	}

	public void UpdateMeshAndPhysics(bool rebuildPhysics, bool rebuildNavMesh, IEnumerable<Rect2I>? affectedRegions, bool rebuildWater = true)
	{
		int w = Width;
		int d = Depth;

		EnsureSplatMaps(w, d);

		if (_chunks.Count == 0 || _chunkedWidth != w || _chunkedDepth != d)
		{
			CreateChunks();
		}
		else if (_material != null)
		{
			_material.SetShaderParameter("terrain_size", new Vector2(w * QuadSize, d * QuadSize));
			_material.SetShaderParameter("grid_spacing", QuadSize);
		}

		UpdateAffectedChunks(rebuildPhysics, affectedRegions);

		if (rebuildWater)
		{
			RegenerateWaterMesh();
		}
	}

	private void EnsureSplatMaps(int w, int d)
	{
		if (SplatMap == null || SplatMap.GetLength(0) < w + 1 || SplatMap.GetLength(1) < d + 1)
		{
			var newSplatMap = new TerrainSplatWeights[w + 1, d + 1];
			CopyOrInitSplatMap(SplatMap, newSplatMap, w, d, 0);
			SplatMap = newSplatMap;
		}

		if (CliffSplatMap == null || CliffSplatMap.GetLength(0) < w + 1 || CliffSplatMap.GetLength(1) < d + 1)
		{
			var newCliffSplatMap = new TerrainSplatWeights[w + 1, d + 1];
			CopyOrInitSplatMap(CliffSplatMap, newCliffSplatMap, w, d, 1);
			CliffSplatMap = newCliffSplatMap;
		}
	}

	private void CopyOrInitSplatMap(TerrainSplatWeights[,]? source, TerrainSplatWeights[,] target, int w, int d, int defaultIndex)
	{
		for (int z = 0; z <= d; z++)
		{
			for (int x = 0; x <= w; x++)
			{
				if (source != null && x < source.GetLength(0) && z < source.GetLength(1))
					target[x, z] = source[x, z];
				else
					target[x, z] = TerrainSplatWeights.CreateSolid(defaultIndex);
			}
		}
	}

	private void UpdateAffectedChunks(bool rebuildPhysics, IEnumerable<Rect2I>? affectedRegions)
	{
		var cells = Cells;
		if (cells != null)
		{
			_perimeterSkirtBottomY = ComputePerimeterSkirtBottomY(cells, Width, Depth);
		}

		foreach (var chunk in _chunks)
		{
			if (affectedRegions != null && !ChunkIntersectsAnyRegion(chunk, affectedRegions))
			{
				continue;
			}

			UpdateChunkMesh(chunk, rebuildPhysics);
		}
	}

	private bool ChunkIntersectsAnyRegion(TerrainChunk chunk, IEnumerable<Rect2I> regions)
	{
		foreach (var region in regions)
		{
			if (!(chunk.EndX < region.Position.X || chunk.StartX > region.Position.X + region.Size.X ||
			      chunk.EndZ < region.Position.Y || chunk.StartZ > region.Position.Y + region.Size.Y))
			{
				return true;
			}
		}

		return false;
	}

	public void SanitizeCornerHeights()
	{
		var cells = Cells;
		if (cells == null) return;

		int w = Width;
		int d = Depth;

		for (int gz = 0; gz <= d; gz++)
		{
			for (int gx = 0; gx <= w; gx++)
			{
				UpdateCornerHeightsAtNode(gx, gz, cells, w, d);
			}
		}
	}

	private void UpdateCornerHeightsAtNode(int gx, int gz, TerrainCell[,] cells, int w, int d)
	{
		float h = GetGridNodeHeight(gx, gz, cells, w, d);
		ApplyCornerHeightSE(gx, gz, cells, w, d, h);
		ApplyCornerHeightSW(gx, gz, cells, w, d, h);
		ApplyCornerHeightNE(gx, gz, cells, w, d, h);
		ApplyCornerHeightNW(gx, gz, cells, w, d, h);
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	private static void ApplyCornerHeightSE(int gx, int gz, TerrainCell[,] cells, int w, int d, float h)
	{
		if (gx > 0 && gx - 1 < w && gz > 0 && gz - 1 < d) cells[gx - 1, gz - 1].Y_SE = h;
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	private static void ApplyCornerHeightSW(int gx, int gz, TerrainCell[,] cells, int w, int d, float h)
	{
		if (gx < w && gz > 0 && gz - 1 < d) cells[gx, gz - 1].Y_SW = h;
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	private static void ApplyCornerHeightNE(int gx, int gz, TerrainCell[,] cells, int w, int d, float h)
	{
		if (gx > 0 && gx - 1 < w && gz < d) cells[gx - 1, gz].Y_NE = h;
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	private static void ApplyCornerHeightNW(int gx, int gz, TerrainCell[,] cells, int w, int d, float h)
	{
		if (gx < w && gz < d) cells[gx, gz].Y_NW = h;
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public float GetGridNodeHeight(int gx, int gz)
	{
		return GetGridNodeHeight(gx, gz, Cells, Width, Depth);
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static float GetGridNodeHeight(int gx, int gz, TerrainCell[,] cells, int w, int d)
	{
		if (cells == null || w <= 0 || d <= 0) return 0f;
		if ((uint)gx < (uint)w && (uint)gz < (uint)d) return cells[gx, gz].Y_NW;
		int cellX = Math.Clamp(gx, 0, w - 1);
		int cellZ = Math.Clamp(gz, 0, d - 1);
		var cell = cells[cellX, cellZ];
		if (gx >= w && gz >= d) return cell.Y_SE;
		if (gx >= w) return cell.Y_NE;
		if (gz >= d) return cell.Y_SW;
		return cell.Y_NW;
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static float GetGridNodeHeight(int gx, int gz, float[,] heights, int w, int d)
	{
		if (heights == null || w <= 0 || d <= 0) return 0f;
		int hW = heights.GetLength(0);
		int hD = heights.GetLength(1);
		int cellX = Math.Clamp(gx, 0, hW - 1);
		int cellZ = Math.Clamp(gz, 0, hD - 1);
		return heights[cellX, cellZ];
	}

	public virtual void SetPathingVisible(bool visible)
	{
	}

	public virtual void SetWireframeMode(bool enabled)
	{
	}

	public virtual void ToggleWireframeMode()
	{
	}

	public virtual void UpdatePathingTexture()
	{
	}

	public virtual void ResizeTerrain(int newWidth, int newDepth)
	{
	}

	public virtual void ScaleTerrainData(int newWidth, int newDepth)
	{
	}

	public virtual void RemapSplatIndices(IReadOnlyDictionary<int, int> remap)
	{
	}

	public virtual void RestoreTerrainFromSnapshot(int newWidth, int newDepth, float quadSize, TerrainCell[,]? cells, int[,] pathingCodes, TerrainSplatWeights[,] splatMap, TerrainSplatWeights[,]? cliffSplatMap = null)
	{
	}

	public virtual void RestoreTerrainFromSnapshot(int newWidth, int newDepth, float quadSize, float[,] heights, int[,] pathingCodes, TerrainSplatWeights[,] splatMap, TerrainSplatWeights[,]? cliffSplatMap = null)
	{
	}

	public virtual void ProcessAndSaveRawTexture(string rawPngPath, string outputRtexPath)
	{
	}

}
