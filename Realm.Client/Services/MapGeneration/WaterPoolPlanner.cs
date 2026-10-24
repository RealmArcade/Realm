using System;
using System.Collections.Generic;
using Realm.Ecs.Components.Terrain;

namespace Realm.Client.Services.MapGeneration;

public readonly record struct WaterPoolSelection(int SeedX, int SeedZ, float SurfaceY, int CellCount);

public static class WaterPoolPlanner
{
	public const int MinimumPoolCells = 1;

	private const float SurfaceClearance = 0.05f;

	public static bool TryFindNextPool(
		ReadOnlySpan<sbyte> tiers,
		bool[] visited,
		int width,
		int depth,
		int minimumPoolCells,
		ref int scanIndex,
		List<int> regionScratch,
		out WaterPoolSelection pool)
	{
		pool = default;
		int cellCount = width * depth;

		while (scanIndex < cellCount)
		{
			int cellIndex = scanIndex;
			scanIndex++;
			if (visited[cellIndex] || tiers[cellIndex] >= 0)
			{
				continue;
			}

			regionScratch.Clear();
			CollectRegion(tiers, visited, width, depth, cellIndex, regionScratch);
			if (regionScratch.Count < minimumPoolCells)
			{
				continue;
			}

			pool = DescribeRegion(tiers, width, depth, regionScratch);
			return true;
		}

		return false;
	}

	private static void CollectRegion(ReadOnlySpan<sbyte> tiers, bool[] visited, int width, int depth, int seedIndex, List<int> regionScratch)
	{
		var queue = new Queue<int>();
		queue.Enqueue(seedIndex);
		visited[seedIndex] = true;

		while (queue.Count > 0)
		{
			int cellIndex = queue.Dequeue();
			regionScratch.Add(cellIndex);
			int cellX = cellIndex % width;
			int cellZ = cellIndex / width;

			if (cellX > 0) EnqueueWaterNeighbour(tiers, visited, width, depth, cellIndex - 1, queue);
			if (cellX < width - 1) EnqueueWaterNeighbour(tiers, visited, width, depth, cellIndex + 1, queue);
			if (cellZ > 0) EnqueueWaterNeighbour(tiers, visited, width, depth, cellIndex - width, queue);
			if (cellZ < depth - 1) EnqueueWaterNeighbour(tiers, visited, width, depth, cellIndex + width, queue);
		}
	}

	private static void EnqueueWaterNeighbour(ReadOnlySpan<sbyte> tiers, bool[] visited, int width, int depth, int neighbourIndex, Queue<int> queue)
	{
		if (visited[neighbourIndex] || tiers[neighbourIndex] >= 0)
		{
			return;
		}

		visited[neighbourIndex] = true;
		queue.Enqueue(neighbourIndex);
	}

	private static WaterPoolSelection DescribeRegion(ReadOnlySpan<sbyte> tiers, int width, int depth, List<int> regionCells)
	{
		int deepestIndex = regionCells[0];
		int deepestTier = tiers[deepestIndex];
		int borderTier = int.MaxValue;

		foreach (int cellIndex in regionCells)
		{
			int cellTier = tiers[cellIndex];
			if (cellTier < deepestTier)
			{
				deepestTier = cellTier;
				deepestIndex = cellIndex;
			}

			int cellX = cellIndex % width;
			int cellZ = cellIndex / width;

			if (cellX == 0 || cellZ == 0 || cellX == width - 1 || cellZ == depth - 1)
			{
				borderTier = Math.Min(borderTier, 0);
			}

			if (cellX > 0) borderTier = Math.Min(borderTier, Math.Max(0, (int)tiers[cellIndex - 1]));
			if (cellX < width - 1) borderTier = Math.Min(borderTier, Math.Max(0, (int)tiers[cellIndex + 1]));
			if (cellZ > 0) borderTier = Math.Min(borderTier, Math.Max(0, (int)tiers[cellIndex - width]));
			if (cellZ < depth - 1) borderTier = Math.Min(borderTier, Math.Max(0, (int)tiers[cellIndex + width]));
		}

		if (borderTier == int.MaxValue)
		{
			borderTier = 0;
		}

		int surfaceTier = Math.Min(borderTier, 0);
		float surfaceY = surfaceTier * TerrainCell.TIER_HEIGHT - SurfaceClearance;
		return new WaterPoolSelection(deepestIndex % width, deepestIndex / width, surfaceY, regionCells.Count);
	}
}
