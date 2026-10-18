using System;
using System.Runtime.InteropServices;
using DotRecast.Detour;

namespace Realm.Ecs.Components.Terrain;

[StructLayout(LayoutKind.Sequential, Pack = 4)]
public struct TerrainState
{
	public const float DefaultQuadSize = 1.0f;
	public const float DefaultCellSize = 1.0f;

	public int Width;
	public int Depth;
	public float QuadSize;
	public float CellSize;
	public int CellsHandle;
	public int PathingCodesHandle;
	public int NavMeshHandle;
	public int NavMeshQueryHandle;
	public int SwatchConfigsHandle;

	public TerrainCell[,]? Cells
	{
		get => ColdPayloadRegistry<TerrainCell[,]>.Get(CellsHandle);
		set => CellsHandle = ColdPayloadRegistry<TerrainCell[,]>.Register(value);
	}

	public int[,]? PathingCodes
	{
		get => ColdPayloadRegistry<int[,]>.Get(PathingCodesHandle);
		set => PathingCodesHandle = ColdPayloadRegistry<int[,]>.Register(value);
	}

	public DtNavMesh? NavMesh
	{
		get => ColdPayloadRegistry<DtNavMesh>.Get(NavMeshHandle);
		set => NavMeshHandle = ColdPayloadRegistry<DtNavMesh>.Register(value);
	}

	public DtNavMeshQuery? NavMeshQuery
	{
		get => ColdPayloadRegistry<DtNavMeshQuery>.Get(NavMeshQueryHandle);
		set => NavMeshQueryHandle = ColdPayloadRegistry<DtNavMeshQuery>.Register(value);
	}

	public TerrainSwatchConfig[]? SwatchConfigs
	{
		get => ColdPayloadRegistry<TerrainSwatchConfig[]>.Get(SwatchConfigsHandle);
		set => SwatchConfigsHandle = ColdPayloadRegistry<TerrainSwatchConfig[]>.Register(value);
	}

	public float[,]? Heights => CalculateHeights(Width, Depth, Cells);

	public static float[,]? CalculateHeights(int width, int depth, TerrainCell[,]? cells)
	{
		if (cells == null || width <= 0 || depth <= 0) return null;
		int w = cells.GetLength(0);
		int d = cells.GetLength(1);
		float[,] result = new float[w + 1, d + 1];
		for (int x = 0; x < w; x++)
		{
			for (int z = 0; z < d; z++)
			{
				result[x, z] = cells[x, z].Y_NW;
			}
			result[x, d] = cells[x, d - 1].Y_SW;
		}
		for (int z = 0; z < d; z++)
		{
			result[w, z] = cells[w - 1, z].Y_NE;
		}
		result[w, d] = cells[w - 1, d - 1].Y_SE;
		return result;
	}

	public static TerrainCell[,]? CalculateCells(int width, int depth, float[,]? heights, TerrainCell[,]? existingCells = null)
	{
		if (heights == null || width <= 0 || depth <= 0) return null;
		var cells = new TerrainCell[width, depth];
		for (int x = 0; x < width; x++)
		{
			for (int z = 0; z < depth; z++)
			{
				int x0 = Math.Min(x, heights.GetLength(0) - 1);
				int z0 = Math.Min(z, heights.GetLength(1) - 1);
				int x1 = Math.Min(x + 1, heights.GetLength(0) - 1);
				int z1 = Math.Min(z + 1, heights.GetLength(1) - 1);

				float nw = heights[x0, z0];
				float ne = heights[x1, z0];
				float sw = heights[x0, z1];
				float se = heights[x1, z1];

				WaterType wMode = WaterType.None;
				byte wProfile = 0;
				float wHeight = 0f;

				if (existingCells != null && x < existingCells.GetLength(0) && z < existingCells.GetLength(1))
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

	public TerrainState(
		int width,
		int depth,
		float quadSize,
		float cellSize,
		TerrainCell[,]? cells,
		int[,]? pathingCodes,
		DtNavMesh? navMesh = null,
		DtNavMeshQuery? navMeshQuery = null)
	{
		Width = width;
		Depth = depth;
		QuadSize = quadSize;
		CellSize = cellSize;
		CellsHandle = ColdPayloadRegistry<TerrainCell[,]>.Register(cells);
		PathingCodesHandle = ColdPayloadRegistry<int[,]>.Register(pathingCodes);
		NavMeshHandle = ColdPayloadRegistry<DtNavMesh>.Register(navMesh);
		NavMeshQueryHandle = ColdPayloadRegistry<DtNavMeshQuery>.Register(navMeshQuery);
		SwatchConfigsHandle = 0;
	}

	public TerrainState(
		int width,
		int depth,
		float quadSize,
		float cellSize,
		float[,] heights,
		int[,]? pathingCodes,
		DtNavMesh? navMesh = null,
		DtNavMeshQuery? navMeshQuery = null,
		TerrainCell[,]? existingCells = null)
	{
		Width = width;
		Depth = depth;
		QuadSize = quadSize;
		CellSize = cellSize;
		var cells = CalculateCells(width, depth, heights, existingCells);
		CellsHandle = ColdPayloadRegistry<TerrainCell[,]>.Register(cells);
		PathingCodesHandle = ColdPayloadRegistry<int[,]>.Register(pathingCodes);
		NavMeshHandle = ColdPayloadRegistry<DtNavMesh>.Register(navMesh);
		NavMeshQueryHandle = ColdPayloadRegistry<DtNavMeshQuery>.Register(navMeshQuery);
		SwatchConfigsHandle = 0;
	}

	public void SetHeights(float[,]? heights)
	{
		if (heights == null) return;
		Cells = CalculateCells(Width, Depth, heights, Cells);
	}
}
