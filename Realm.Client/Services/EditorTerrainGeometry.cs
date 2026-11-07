using System;
using System.Runtime.CompilerServices;
using Realm.Ecs.Components.Core;
using Realm.Ecs.Components.Terrain;

namespace Realm.Client.Services;

/// <summary>
///     Reading and writing a terrain grid's corner heights, plus the cell and splat-quad rotations and reflections a
///     paste applies. Every member is a pure function of the grid it is handed, so the drawing path that closes a brush
///     stroke and the paste path that rotates a copied block both use it without touching editor state.
/// </summary>
internal static class EditorTerrainGeometry
{
	internal static void SetGridNodeHeight(ref TerrainState terrain, int gx, int gz, float height)
	{
		var cells = terrain.Cells;
		if (cells == null) return;
		int w = terrain.Width;
		int d = terrain.Depth;
		if (gx > 0 && gz > 0 && gx - 1 < w && gz - 1 < d)
		{
			ref var c = ref cells[gx - 1, gz - 1];
			c.Y_SE = height;
		}

		if (gx < w && gz > 0 && gz - 1 < d)
		{
			ref var c = ref cells[gx, gz - 1];
			c.Y_SW = height;
		}

		if (gx > 0 && gz < d && gx - 1 < w)
		{
			ref var c = ref cells[gx - 1, gz];
			c.Y_NE = height;
		}

		if (gx < w && gz < d)
		{
			ref var c = ref cells[gx, gz];
			c.Y_NW = height;
		}
	}

	internal static float GetGridNodeHeight(in TerrainState terrain, int gx, int gz)
	{
		var cells = terrain.Cells;
		if (cells == null) return 0f;
		int w = terrain.Width;
		int d = terrain.Depth;
		int cellX = Math.Clamp(gx, 0, w - 1);
		int cellZ = Math.Clamp(gz, 0, d - 1);
		if (gx < w && gz < d) return cells[cellX, cellZ].Y_NW;
		if (gx == w && gz < d) return cells[w - 1, cellZ].Y_NE;
		if (gx < w && gz == d) return cells[cellX, d - 1].Y_SW;
		return cells[w - 1, d - 1].Y_SE;
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	internal static TerrainCell RotateCell(in TerrainCell cell, int rotSteps)
	{
		rotSteps = (rotSteps % 4 + 4) % 4;
		switch (rotSteps)
		{
			case 1:
				return new TerrainCell(cell.Y_SW, cell.Y_NW, cell.Y_NE, cell.Y_SE, cell.WaterMode, cell.WaterProfileIndex, cell.WaterHeight);
			case 2:
				return new TerrainCell(cell.Y_SE, cell.Y_SW, cell.Y_NW, cell.Y_NE, cell.WaterMode, cell.WaterProfileIndex, cell.WaterHeight);
			case 3:
				return new TerrainCell(cell.Y_NE, cell.Y_SE, cell.Y_SW, cell.Y_NW, cell.WaterMode, cell.WaterProfileIndex, cell.WaterHeight);
			case 0:
			default:
				return cell;
		}
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	internal static TerrainCell MirrorCell(in TerrainCell cell, MirrorMode mode)
	{
		switch (mode)
		{
			case MirrorMode.Horizontal:
				return new TerrainCell(cell.Y_NE, cell.Y_NW, cell.Y_SW, cell.Y_SE, cell.WaterMode, cell.WaterProfileIndex, cell.WaterHeight);
			case MirrorMode.Vertical:
				return new TerrainCell(cell.Y_SW, cell.Y_SE, cell.Y_NE, cell.Y_NW, cell.WaterMode, cell.WaterProfileIndex, cell.WaterHeight);
			case MirrorMode.Both:
				return new TerrainCell(cell.Y_SE, cell.Y_SW, cell.Y_NW, cell.Y_NE, cell.WaterMode, cell.WaterProfileIndex, cell.WaterHeight);
			case MirrorMode.None:
			default:
				return cell;
		}
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	internal static (TerrainSplatWeights NW, TerrainSplatWeights NE, TerrainSplatWeights SE, TerrainSplatWeights SW) MirrorSplatQuad(
		in TerrainSplatWeights nw, in TerrainSplatWeights ne, in TerrainSplatWeights se, in TerrainSplatWeights sw, MirrorMode mode)
	{
		switch (mode)
		{
			case MirrorMode.Horizontal:
				return (ne, nw, sw, se);
			case MirrorMode.Vertical:
				return (sw, se, ne, nw);
			case MirrorMode.Both:
				return (se, sw, nw, ne);
			case MirrorMode.None:
			default:
				return (nw, ne, se, sw);
		}
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	internal static (TerrainSplatWeights NW, TerrainSplatWeights NE, TerrainSplatWeights SE, TerrainSplatWeights SW) RotateSplatQuad(
		in TerrainSplatWeights nw, in TerrainSplatWeights ne, in TerrainSplatWeights se, in TerrainSplatWeights sw, int rotSteps)
	{
		rotSteps = (rotSteps % 4 + 4) % 4;
		switch (rotSteps)
		{
			case 1:
				return (sw, nw, ne, se);
			case 2:
				return (se, sw, nw, ne);
			case 3:
				return (ne, se, sw, nw);
			case 0:
			default:
				return (nw, ne, se, sw);
		}
	}

	internal static void TrySetSplat(TerrainSplatWeights[,] map, int x, int z, int w, int d, in TerrainSplatWeights val)
	{
		if (x >= 0 && x < w && z >= 0 && z < d) map[x, z] = val;
	}

	internal static void SanitizeCellCorners(TerrainCell[,] cells, int gx, int gz, int w, int d, float h)
	{
		bool xm1 = gx > 0 && gx - 1 < w;
		bool zm1 = gz > 0 && gz - 1 < d;
		bool x0 = gx < w;
		bool z0 = gz < d;

		if (xm1 && zm1) cells[gx - 1, gz - 1].Y_SE = h;
		if (x0 && zm1) cells[gx, gz - 1].Y_SW = h;
		if (xm1 && z0) cells[gx - 1, gz].Y_NE = h;
		if (x0 && z0) cells[gx, gz].Y_NW = h;
	}
}
