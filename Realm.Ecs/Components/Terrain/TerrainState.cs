using DotRecast.Detour;

namespace Realm.Ecs.Components.Terrain
{
	/// <summary>
	/// Represents the simulation state and configuration of the map terrain, heightfield, pathing flags, and navigation mesh.
	/// </summary>
	internal struct TerrainState
	{
		public const float DefaultQuadSize = 2.0f;
		public const float DefaultCellSize = 0.5f;

		public int Width;
		public int Depth;
		public float QuadSize;
		public float CellSize;
		public TerrainCell[,]? Cells;
		public int[,]? PathingCodes;
		public TerrainSwatchConfig[]? SwatchConfigs;
		public DtNavMesh NavMesh;
		public DtNavMeshQuery NavMeshQuery;

		public float[,]? Heights => CalculateHeights(Width, Depth, Cells);

		public static float[,]? CalculateHeights(int width, int depth, TerrainCell[,]? cells)
		{
			if (cells == null) return null;
			int w = System.Math.Max(1, width);
			int d = System.Math.Max(1, depth);
			float[,] result = new float[w + 1, d + 1];
			for (int z = 0; z < d; z++)
			{
				for (int x = 0; x < w; x++)
				{
					result[x, z] = cells[x, z].Y_NW;
				}
				result[w, z] = cells[w - 1, z].Y_NE;
			}
			for (int x = 0; x < w; x++)
			{
				result[x, d] = cells[x, d - 1].Y_SW;
			}
			result[w, d] = cells[w - 1, d - 1].Y_SE;
			return result;
		}

		public static TerrainCell[,]? CalculateCells(int width, int depth, float[,]? heights, TerrainCell[,]? existingCells = null)
		{
			if (heights == null)
			{
				return null;
			}
			var cells = new TerrainCell[width, depth];
			int sW = heights.GetLength(0);
			int sD = heights.GetLength(1);
			int existingW = existingCells != null ? existingCells.GetLength(0) : 0;
			int existingD = existingCells != null ? existingCells.GetLength(1) : 0;

			for (int z = 0; z < depth; z++)
			{
				int z0 = System.Math.Clamp(z, 0, sD - 1);
				int z1 = System.Math.Clamp(z + 1, 0, sD - 1);
				for (int x = 0; x < width; x++)
				{
					int x0 = System.Math.Clamp(x, 0, sW - 1);
					int x1 = System.Math.Clamp(x + 1, 0, sW - 1);

					float nw = heights[x0, z0];
					float ne = heights[x1, z0];
					float sw = heights[x0, z1];
					float se = heights[x1, z1];

					WaterType wMode = WaterType.None;
					byte wProfile = 0;
					float wHeight = 0f;
					if (existingCells != null && x < existingW && z < existingD)
					{
						wMode = existingCells[x, z].WaterMode;
						wProfile = existingCells[x, z].WaterProfileIndex;
						wHeight = existingCells[x, z].WaterHeight;
					}

					cells[x, z] = new TerrainCell(nw, ne, se, sw, wMode, wProfile, wHeight);
				}
			}
			return cells;
		}

		public void SetHeights(float[,]? heights)
		{
			if (heights == null) return;
			Cells = CalculateCells(Width, Depth, heights, Cells);
		}

		public TerrainState(
			int width,
			int depth,
			float quadSize,
			float cellSize,
			TerrainCell[,]? cells,
			int[,]? pathingCodes,
			DtNavMesh navMesh,
			DtNavMeshQuery navMeshQuery)
		{
			Width = width;
			Depth = depth;
			QuadSize = quadSize;
			CellSize = cellSize;
			Cells = cells;
			PathingCodes = pathingCodes;
			NavMesh = navMesh;
			NavMeshQuery = navMeshQuery;
			SwatchConfigs = null;
		}

		public TerrainState(
			int width,
			int depth,
			float quadSize,
			float cellSize,
			float[,]? heights,
			int[,]? pathingCodes,
			DtNavMesh navMesh,
			DtNavMeshQuery navMeshQuery)
		{
			Width = width;
			Depth = depth;
			QuadSize = quadSize;
			CellSize = cellSize;
			PathingCodes = pathingCodes;
			NavMesh = navMesh;
			NavMeshQuery = navMeshQuery;
			SwatchConfigs = null;
			Cells = (heights != null) ? CalculateCells(width, depth, heights) : null;
		}
	}
}
