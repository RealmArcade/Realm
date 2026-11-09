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
public static class EditorTerrainQueries
{
	public static float GetHeightAt(in TerrainState terrain, Vector3 worldPos)
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

	/// <summary>
	/// The same sample for callers that do not already hold the terrain: resolves the editor's terrain state
	/// first and returns zero when there is none, which is what a cursor readout before a map loads sees.
	/// </summary>
	public static float GetHeightAt(Vector3 worldPos)
	{
		if (!EditorService.TryGetTerrainState(out _))
		{
			return 0.0f;
		}

		ref var terrain = ref EditorService.GetTerrainState();
		return GetHeightAt(in terrain, worldPos);
	}

	public static WaterType GetWaterModeAt(in TerrainState terrain, Vector3 worldPos)
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

	public static byte GetWaterProfileIndexAt(in TerrainState terrain, Vector3 worldPos)
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

	public static float GetWaterHeightAt(in TerrainState terrain, Vector3 worldPos)
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

	public static float GetMinHeightInBrushBounds(Vector3 worldPos, float brushRadius, bool brushIsSquare)
	{
		ref var terrain = ref EditorService.GetTerrainState();
		if (terrain.Cells == null) return GetHeightAt(worldPos);

		float quadSize = terrain.QuadSize;
		int width = terrain.Width;
		int depth = terrain.Depth;
		float minHeight = float.MaxValue;
		bool foundAny = false;

		for (int z = 0; z <= depth; z++)
		{
			for (int x = 0; x <= width; x++)
			{
				float vx = (x - width / 2.0f) * quadSize;
				float vz = (z - depth / 2.0f) * quadSize;

				bool inBounds = false;
				if (brushRadius < 0)
				{
					inBounds = true;
				}
				else if (brushIsSquare)
				{
					float dx = Mathf.Abs(vx - worldPos.X);
					float dz = Mathf.Abs(vz - worldPos.Z);
					inBounds = dx <= brushRadius && dz <= brushRadius;
				}
				else
				{
					float dist = new Vector2(vx - worldPos.X, vz - worldPos.Z).Length();
					inBounds = dist <= brushRadius;
				}

				if (inBounds)
				{
					float h = EditorTerrainGeometry.GetGridNodeHeight(in terrain, x, z);
					if (h < minHeight)
					{
						minHeight = h;
						foundAny = true;
					}
				}
			}
		}

		return foundAny ? minHeight : GetHeightAt(worldPos);
	}
}
