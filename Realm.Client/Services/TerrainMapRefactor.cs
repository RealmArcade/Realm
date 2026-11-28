using System;
using System.Collections.Generic;
using Realm.Client.Utils;
using Realm.Ecs.Components.Terrain;
using Realm.Shared.Terrain;

namespace Realm.Client.Services;

public static class TerrainMapRefactor
{
	public const float HeightRangeTolerance = 0.001f;
	public const float PlaneHeightStep = 1.0f;
	public const int TextureIndicesPerHeightStep = 2;

	private readonly struct WaterBody
	{
		public readonly int SeedX;
		public readonly int SeedZ;
		public readonly WaterType WaterMode;
		public readonly byte WaterProfileIndex;
		public readonly float WaterHeight;
		public readonly List<(int X, int Z)> Cells;

		public WaterBody(int seedX, int seedZ, WaterType waterMode, byte waterProfileIndex, float waterHeight, List<(int X, int Z)> cells)
		{
			SeedX = seedX;
			SeedZ = seedZ;
			WaterMode = waterMode;
			WaterProfileIndex = waterProfileIndex;
			WaterHeight = waterHeight;
			Cells = cells;
		}
	}

	public static (float Lowest, float Highest) MeasureHeightSpan(TerrainCell[,] cells)
	{
		int width = cells.GetLength(0);
		int depth = cells.GetLength(1);
		if (width == 0 || depth == 0) return (0f, 0f);

		float lowest = float.MaxValue;
		float highest = float.MinValue;

		for (int z = 0; z < depth; z++)
		{
			for (int x = 0; x < width; x++)
			{
				TerrainCell cell = cells[x, z];
				lowest = MathF.Min(lowest, MathF.Min(MathF.Min(cell.Y_NW, cell.Y_NE), MathF.Min(cell.Y_SE, cell.Y_SW)));
				highest = MathF.Max(highest, MathF.Max(MathF.Max(cell.Y_NW, cell.Y_NE), MathF.Max(cell.Y_SE, cell.Y_SW)));
			}
		}

		if (highest < lowest) return (0f, 0f);

		return (lowest, highest);
	}

	public static void ScaleHeights(TerrainCell[,] cells, float targetRange)
	{
		(float lowest, float highest) = MeasureHeightSpan(cells);
		float originalRange = highest - lowest;

		if (originalRange <= 0f || targetRange <= 0f) return;
		if (MathF.Abs(targetRange - originalRange) <= HeightRangeTolerance) return;

		float ratio = targetRange / originalRange;

		List<WaterBody> waterBodies = ExtractWaterBodies(cells);

		int width = cells.GetLength(0);
		int depth = cells.GetLength(1);
		for (int z = 0; z < depth; z++)
		{
			for (int x = 0; x < width; x++)
			{
				TerrainCell cell = cells[x, z];
				cells[x, z] = new TerrainCell(
					ScaleHeight(cell.Y_NW, lowest, ratio),
					ScaleHeight(cell.Y_NE, lowest, ratio),
					ScaleHeight(cell.Y_SE, lowest, ratio),
					ScaleHeight(cell.Y_SW, lowest, ratio));
			}
		}

		RestoreWaterBodies(cells, waterBodies, lowest, ratio);
	}

	public static void ResetTexturesByHeight(TerrainCell[,] cells, TerrainSplatWeights[,] ground, TerrainSplatWeights[,]? cliff, IReadOnlyList<int> swatchOrder)
	{
		if (swatchOrder.Count == 0) return;
		if (cells.GetLength(0) == 0 || cells.GetLength(1) == 0) return;

		Dictionary<float, int> heightRanks = BuildHeightRanks(cells);

		int width = ground.GetLength(0);
		int depth = ground.GetLength(1);
		int cliffWidth = cliff != null ? Math.Min(width, cliff.GetLength(0)) : 0;
		int cliffDepth = cliff != null ? Math.Min(depth, cliff.GetLength(1)) : 0;

		for (int z = 0; z < depth; z++)
		{
			for (int x = 0; x < width; x++)
			{
				int rank = heightRanks[SampleCellHeight(cells, x, z)];
				int groundIndex = swatchOrder[(rank * TextureIndicesPerHeightStep) % swatchOrder.Count];
				int cliffIndex = swatchOrder[(rank * TextureIndicesPerHeightStep + 1) % swatchOrder.Count];

				ground[x, z] = TerrainSplatWeights.CreateSolid(groundIndex);
				if (cliff != null && x < cliffWidth && z < cliffDepth)
				{
					cliff[x, z] = TerrainSplatWeights.CreateSolid(cliffIndex);
				}
			}
		}
	}

	public static void QuantizeTextures(TerrainCell[,] cells, TerrainSplatWeights[,] ground, TerrainSplatWeights[,]? cliff, int radius)
	{
		if (radius <= 0) return;
		if (cells.GetLength(0) == 0 || cells.GetLength(1) == 0) return;

		int width = cliff != null ? Math.Min(ground.GetLength(0), cliff.GetLength(0)) : ground.GetLength(0);
		int depth = cliff != null ? Math.Min(ground.GetLength(1), cliff.GetLength(1)) : ground.GetLength(1);
		if (width == 0 || depth == 0) return;

		int[,] planes = BuildHeightPlanes(cells, width, depth);

		int[,] quantizedGround = QuantizeLayer(ground, planes, radius, width, depth);
		int[,]? quantizedCliff = cliff != null ? QuantizeLayer(cliff, planes, radius, width, depth) : null;

		ApplyQuantizedLayer(ground, quantizedGround);
		if (cliff != null && quantizedCliff != null)
		{
			ApplyQuantizedLayer(cliff, quantizedCliff);
		}
	}

	private static float ScaleHeight(float height, float lowest, float ratio)
	{
		return lowest + (height - lowest) * ratio;
	}

	private static float SampleCellHeight(TerrainCell[,] cells, int x, int z)
	{
		int cellX = Math.Clamp(x, 0, cells.GetLength(0) - 1);
		int cellZ = Math.Clamp(z, 0, cells.GetLength(1) - 1);

		return cells[cellX, cellZ].CenterHeight;
	}

	private static Dictionary<float, int> BuildHeightRanks(TerrainCell[,] cells)
	{
		int width = cells.GetLength(0);
		int depth = cells.GetLength(1);

		var distinctHeights = new List<float>();
		var seenHeights = new HashSet<float>();

		for (int z = 0; z < depth; z++)
		{
			for (int x = 0; x < width; x++)
			{
				float height = cells[x, z].CenterHeight;
				if (seenHeights.Add(height))
				{
					distinctHeights.Add(height);
				}
			}
		}

		distinctHeights.Sort();

		var ranks = new Dictionary<float, int>(distinctHeights.Count);
		for (int index = 0; index < distinctHeights.Count; index++)
		{
			ranks[distinctHeights[index]] = index;
		}

		return ranks;
	}

	private static List<WaterBody> ExtractWaterBodies(TerrainCell[,] cells)
	{
		int width = cells.GetLength(0);
		int depth = cells.GetLength(1);

		var liquidCells = new List<(int X, int Z, float Height)>();
		for (int z = 0; z < depth; z++)
		{
			for (int x = 0; x < width; x++)
			{
				if (cells[x, z].WaterMode != WaterType.None)
				{
					liquidCells.Add((x, z, cells[x, z].CenterHeight));
				}
			}
		}

		liquidCells.Sort((left, right) => left.Height.CompareTo(right.Height));

		var removed = new bool[width, depth];
		var bodies = new List<WaterBody>();
		var pending = new Queue<(int X, int Z)>();

		for (int index = 0; index < liquidCells.Count; index++)
		{
			(int seedX, int seedZ, float _) = liquidCells[index];
			if (removed[seedX, seedZ]) continue;

			TerrainCell seed = cells[seedX, seedZ];
			var members = new List<(int X, int Z)>();

			removed[seedX, seedZ] = true;
			pending.Enqueue((seedX, seedZ));

			while (pending.Count > 0)
			{
				(int x, int z) = pending.Dequeue();
				members.Add((x, z));
				RemoveWater(ref cells[x, z]);

				if (x > 0) QueueWaterCell(x - 1, z, cells, removed, pending);
				if (x + 1 < width) QueueWaterCell(x + 1, z, cells, removed, pending);
				if (z > 0) QueueWaterCell(x, z - 1, cells, removed, pending);
				if (z + 1 < depth) QueueWaterCell(x, z + 1, cells, removed, pending);
			}

			bodies.Add(new WaterBody(seedX, seedZ, seed.WaterMode, seed.WaterProfileIndex, seed.WaterHeight, members));
		}

		return bodies;
	}

	private static void QueueWaterCell(int nextX, int nextZ, TerrainCell[,] cells, bool[,] removed, Queue<(int X, int Z)> pending)
	{
		if (removed[nextX, nextZ]) return;
		if (cells[nextX, nextZ].WaterMode == WaterType.None) return;

		removed[nextX, nextZ] = true;
		pending.Enqueue((nextX, nextZ));
	}

	private static void RestoreWaterBodies(TerrainCell[,] cells, List<WaterBody> waterBodies, float lowest, float ratio)
	{
		int width = cells.GetLength(0);
		int depth = cells.GetLength(1);
		var pending = new Queue<(int X, int Z)>();

		foreach (WaterBody body in waterBodies)
		{
			if (body.Cells.Count == 0) continue;

			float waterHeight = ScaleHeight(body.WaterHeight, lowest, ratio);
			var unvisited = new HashSet<(int X, int Z)>(body.Cells);

			if (!unvisited.Remove((body.SeedX, body.SeedZ))) continue;

			ApplyWater(ref cells[body.SeedX, body.SeedZ], body, waterHeight);
			pending.Enqueue((body.SeedX, body.SeedZ));

			while (pending.Count > 0)
			{
				(int x, int z) = pending.Dequeue();

				if (x > 0) QueueWaterMember(x - 1, z, cells, unvisited, body, waterHeight, pending);
				if (x + 1 < width) QueueWaterMember(x + 1, z, cells, unvisited, body, waterHeight, pending);
				if (z > 0) QueueWaterMember(x, z - 1, cells, unvisited, body, waterHeight, pending);
				if (z + 1 < depth) QueueWaterMember(x, z + 1, cells, unvisited, body, waterHeight, pending);
			}
		}
	}

	private static void QueueWaterMember(int nextX, int nextZ, TerrainCell[,] cells, HashSet<(int X, int Z)> unvisited, in WaterBody body, float waterHeight, Queue<(int X, int Z)> pending)
	{
		if (!unvisited.Remove((nextX, nextZ))) return;

		ApplyWater(ref cells[nextX, nextZ], body, waterHeight);
		pending.Enqueue((nextX, nextZ));
	}

	private static void RemoveWater(ref TerrainCell cell)
	{
		cell.WaterMode = WaterType.None;
		cell.WaterProfileIndex = 0;
		cell.WaterHeight = 0f;
	}

	private static void ApplyWater(ref TerrainCell cell, in WaterBody body, float waterHeight)
	{
		cell.WaterMode = body.WaterMode;
		cell.WaterProfileIndex = body.WaterProfileIndex;
		cell.WaterHeight = waterHeight;
	}

	private static int[,] BuildHeightPlanes(TerrainCell[,] cells, int width, int depth)
	{
		var planes = new int[width, depth];
		for (int z = 0; z < depth; z++)
		{
			for (int x = 0; x < width; x++)
			{
				planes[x, z] = -1;
			}
		}

		var pending = new Queue<(int X, int Z)>();
		int plane = 0;

		for (int z = 0; z < depth; z++)
		{
			for (int x = 0; x < width; x++)
			{
				if (planes[x, z] >= 0) continue;

				planes[x, z] = plane;
				pending.Enqueue((x, z));

				while (pending.Count > 0)
				{
					(int currentX, int currentZ) = pending.Dequeue();
					float height = SampleCellHeight(cells, currentX, currentZ);

					if (currentX > 0) QueuePlaneCell(currentX - 1, currentZ, height, cells, planes, plane, pending);
					if (currentX + 1 < width) QueuePlaneCell(currentX + 1, currentZ, height, cells, planes, plane, pending);
					if (currentZ > 0) QueuePlaneCell(currentX, currentZ - 1, height, cells, planes, plane, pending);
					if (currentZ + 1 < depth) QueuePlaneCell(currentX, currentZ + 1, height, cells, planes, plane, pending);
				}

				plane++;
			}
		}

		return planes;
	}

	private static void QueuePlaneCell(int nextX, int nextZ, float height, TerrainCell[,] cells, int[,] planes, int plane, Queue<(int X, int Z)> pending)
	{
		if (planes[nextX, nextZ] >= 0) return;
		if (MathF.Abs(SampleCellHeight(cells, nextX, nextZ) - height) >= PlaneHeightStep) return;

		planes[nextX, nextZ] = plane;
		pending.Enqueue((nextX, nextZ));
	}

	private static int[,] QuantizeLayer(TerrainSplatWeights[,] layer, int[,] planes, int radius, int width, int depth)
	{
		var quantized = new int[width, depth];
		var counts = new int[TextureSwatchSlots.MaxSlots];
		var touched = new List<int>();

		for (int z = 0; z < depth; z++)
		{
			for (int x = 0; x < width; x++)
			{
				int plane = planes[x, z];
				int currentIndex = layer[x, z].GetDominantIndex();
				int bestIndex = currentIndex;
				int bestCount = 0;

				touched.Clear();

				int minX = Math.Max(0, x - radius);
				int maxX = Math.Min(width - 1, x + radius);
				int minZ = Math.Max(0, z - radius);
				int maxZ = Math.Min(depth - 1, z + radius);

				for (int sampleZ = minZ; sampleZ <= maxZ; sampleZ++)
				{
					for (int sampleX = minX; sampleX <= maxX; sampleX++)
					{
						if (planes[sampleX, sampleZ] != plane) continue;

						int sampleIndex = layer[sampleX, sampleZ].GetDominantIndex();
						if (sampleIndex < 0 || sampleIndex >= counts.Length) continue;

						if (counts[sampleIndex] == 0) touched.Add(sampleIndex);
						counts[sampleIndex]++;
					}
				}

				foreach (int candidateIndex in touched)
				{
					int candidateCount = counts[candidateIndex];
					if (candidateCount > bestCount || (candidateCount == bestCount && candidateIndex == currentIndex))
					{
						bestCount = candidateCount;
						bestIndex = candidateIndex;
					}
				}

				foreach (int candidateIndex in touched)
				{
					counts[candidateIndex] = 0;
				}

				quantized[x, z] = bestIndex;
			}
		}

		return quantized;
	}

	private static void ApplyQuantizedLayer(TerrainSplatWeights[,] layer, int[,] quantized)
	{
		int width = Math.Min(layer.GetLength(0), quantized.GetLength(0));
		int depth = Math.Min(layer.GetLength(1), quantized.GetLength(1));

		for (int z = 0; z < depth; z++)
		{
			for (int x = 0; x < width; x++)
			{
				layer[x, z] = TerrainSplatWeights.CreateSolid(quantized[x, z]);
			}
		}
	}
}
