using System;
using Godot;
using Realm.Ecs.Components.Terrain;

namespace Realm.Client.Services;

/// <summary>
///     Sampling a terrain grid at a world position: the interpolated ground height across the cell's
///     four corners, and the water mode, profile and height of the cell the position falls in. Every
///     member is a pure function of the grid it is handed, so the brush paths and the cursor readout
///     ask the same questions of the same terrain without touching editor state.
/// </summary>
internal static class EditorTerrainQueries
{
	internal static float GetHeightAt(in TerrainState terrain, Vector3 worldPos)
	{
		var cells = terrain.Cells;
		if (cells == null) return 0.0f;

		int cellW = cells.GetLength(0);
		int cellD = cells.GetLength(1);
		if (cellW <= 0 || cellD <= 0) return 0.0f;

		int width = terrain.Width;
		int depth = terrain.Depth;
		float quadSize = terrain.QuadSize;

		float fx = worldPos.X / quadSize + width / 2.0f;
		float fz = worldPos.Z / quadSize + depth / 2.0f;

		int x = Math.Clamp((int)Math.Floor(fx), 0, cellW - 1);
		int z = Math.Clamp((int)Math.Floor(fz), 0, cellD - 1);
		float tx = Math.Clamp(fx - x, 0f, 1f);
		float tz = Math.Clamp(fz - z, 0f, 1f);

		var cell = cells[x, z];
		float hNW = cell.Y_NW;
		float hNE = cell.Y_NE;
		float hSW = cell.Y_SW;
		float hSE = cell.Y_SE;

		return (1 - tx) * (1 - tz) * hNW + tx * (1 - tz) * hNE + (1 - tx) * tz * hSW + tx * tz * hSE;
	}

	internal static WaterType GetWaterModeAt(in TerrainState terrain, Vector3 worldPos)
	{
		var cells = terrain.Cells;
		if (cells == null) return WaterType.None;

		int cellW = cells.GetLength(0);
		int cellD = cells.GetLength(1);
		if (cellW <= 0 || cellD <= 0) return WaterType.None;

		int width = terrain.Width;
		int depth = terrain.Depth;
		float quadSize = terrain.QuadSize;

		int x = Math.Clamp((int)Math.Floor(worldPos.X / quadSize + width / 2.0f), 0, cellW - 1);
		int z = Math.Clamp((int)Math.Floor(worldPos.Z / quadSize + depth / 2.0f), 0, cellD - 1);

		return cells[x, z].WaterMode;
	}

	internal static byte GetWaterProfileIndexAt(in TerrainState terrain, Vector3 worldPos)
	{
		var cells = terrain.Cells;
		if (cells == null) return 0;

		int cellW = cells.GetLength(0);
		int cellD = cells.GetLength(1);
		if (cellW <= 0 || cellD <= 0) return 0;

		int width = terrain.Width;
		int depth = terrain.Depth;
		float quadSize = terrain.QuadSize;

		int x = Math.Clamp((int)Math.Floor(worldPos.X / quadSize + width / 2.0f), 0, cellW - 1);
		int z = Math.Clamp((int)Math.Floor(worldPos.Z / quadSize + depth / 2.0f), 0, cellD - 1);

		return cells[x, z].WaterProfileIndex;
	}

	internal static float GetWaterHeightAt(in TerrainState terrain, Vector3 worldPos)
	{
		var cells = terrain.Cells;
		if (cells == null) return 0f;

		int cellW = cells.GetLength(0);
		int cellD = cells.GetLength(1);
		if (cellW <= 0 || cellD <= 0) return 0f;

		int width = terrain.Width;
		int depth = terrain.Depth;
		float quadSize = terrain.QuadSize;

		int x = Math.Clamp((int)Math.Floor(worldPos.X / quadSize + width / 2.0f), 0, cellW - 1);
		int z = Math.Clamp((int)Math.Floor(worldPos.Z / quadSize + depth / 2.0f), 0, cellD - 1);

		return cells[x, z].WaterHeight;
	}
}
