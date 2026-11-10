using System;
using System.Collections.Generic;
using Realm.Ecs.Components.Terrain;
using Realm.Shared.Distribution;

namespace Realm.Client.Services;

/// <summary>
///     Reading a saved map's terrain layers into the terrain state: heights at the saved resolution or
///     resampled to the map's current one, the water mask, and the pathing codes with their default when
///     the map carries none.
/// </summary>
public static class MapTerrainLoad
{
	public static void LoadTerrainHeights(string directory, int width, int depth, ref TerrainState ts, ref TerrainCell[,]? unscaledCells, ref int unscaledW, ref int unscaledH)
	{
		float[]? floatData = MapTerrainFiles.TryReadHeights(directory, out int imgW, out int imgH);
		if (floatData == null) return;
		if (ts.Cells == null) return;

		if (imgW == width && imgH == depth)
		{
			for (int z = 0; z < depth; z++)
			{
				for (int x = 0; x < width; x++)
				{
					int baseIdx = (z * imgW + x) * 4;
					ts.Cells[x, z] = new TerrainCell(floatData[baseIdx], floatData[baseIdx + 1], floatData[baseIdx + 2], floatData[baseIdx + 3]);
				}
			}
		}
		else
		{
			unscaledW = imgW;
			unscaledH = imgH;
			unscaledCells = MapTerrainFiles.CellsFromHeights(floatData, imgW, imgH);

			ScaleTerrainHeights(width, depth, imgW, imgH, floatData, ref ts);
		}
	}

	private static void ScaleTerrainHeights(int width, int depth, int imgW, int imgH, ReadOnlySpan<float> floatData, ref TerrainState ts)
	{
		if (ts.Cells == null) return;
		var cells = ts.Cells;

		float[,] newGridHeights = new float[width + 1, depth + 1];
		for (int vz = 0; vz <= depth; vz++)
		{
			for (int vx = 0; vx <= width; vx++)
			{
				int srcVx = Math.Clamp((int)Math.Round(vx * (float)imgW / width), 0, imgW);
				int srcVz = Math.Clamp((int)Math.Round(vz * (float)imgH / depth), 0, imgH);
				newGridHeights[vx, vz] = GetInterpolatedHeight(srcVx, srcVz, imgW, imgH, floatData);
			}
		}

		for (int z = 0; z < depth; z++)
		{
			for (int x = 0; x < width; x++)
			{
				cells[x, z] = new TerrainCell(newGridHeights[x, z], newGridHeights[x + 1, z], newGridHeights[x + 1, z + 1], newGridHeights[x, z + 1]);
			}
		}
	}

	private static float GetInterpolatedHeight(int srcVx, int srcVz, int imgW, int imgH, ReadOnlySpan<float> floatData)
	{
		if (srcVx < imgW && srcVz < imgH) return floatData[(srcVz * imgW + srcVx) * 4 + 0];
		if (srcVx >= imgW && srcVz >= imgH) return floatData[((imgH - 1) * imgW + (imgW - 1)) * 4 + 2];
		if (srcVx >= imgW) return floatData[(srcVz * imgW + (imgW - 1)) * 4 + 1];
		if (srcVz >= imgH) return floatData[((imgH - 1) * imgW + srcVx) * 4 + 3];
		return 0f;
	}

	public static void LoadTerrainWater(string directory, int width, int depth, ref TerrainState ts, ref TerrainCell[,]? unscaledCells, ref int unscaledW, ref int unscaledH)
	{
		float[]? waterFloatData = MapTerrainFiles.TryReadWater(directory, out int imgW, out int imgH);
		if (waterFloatData == null) return;
		if (ts.Cells == null) return;

		if (imgW == width && imgH == depth && unscaledCells == null)
		{
			MapTerrainFiles.ApplyWater(waterFloatData, imgW, imgH, ts.Cells);
		}
		else
		{
			if (unscaledCells == null || unscaledW != imgW || unscaledH != imgH)
			{
				unscaledCells = new TerrainCell[imgW, imgH];
				unscaledW = imgW;
				unscaledH = imgH;
			}

			MapTerrainFiles.ApplyWater(waterFloatData, imgW, imgH, unscaledCells);
		}
	}

	public static void LoadTerrainPathing(string directory, int width, int depth, ref TerrainState ts)
	{
		byte[]? byteData = MapTerrainFiles.TryReadPathing(directory, out int imgW, out int imgH);
		if (byteData == null)
		{
			SetDefaultPathing(width, depth, ref ts);
			return;
		}

		var pathingCodes = ts.PathingCodes;
		if (pathingCodes == null)
		{
			SetDefaultPathing(width, depth, ref ts);
			return;
		}

		MapTerrainFiles.ApplyPathing(byteData, imgW, imgH, pathingCodes, width, depth);
	}

	private static void SetDefaultPathing(int width, int depth, ref TerrainState ts)
	{
		if (ts.PathingCodes == null) return;

		int defaultPathing = TerrainPathing.DefaultPathingCodeByWaterType(WaterType.None);
		for (int z = 0; z < depth; z++)
		{
			for (int x = 0; x < width; x++)
			{
				ts.PathingCodes[x, z] = defaultPathing;
			}
		}
	}
}
