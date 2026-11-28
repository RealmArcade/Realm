using System;
using System.Collections.Generic;

namespace Realm.MapGeneration;

public class MinimapTerrainSegments
{
	public int Width { get; }

	public int Depth { get; }

	public int SegmentCount { get; }

	public int[] Labels { get; }

	public TerrainSurfaceKind[] Kinds { get; }

	public MinimapCellFeatures[] Cells { get; }

	public MinimapTerrainSegments(int width, int depth, int segmentCount, int[] labels, TerrainSurfaceKind[] kinds, MinimapCellFeatures[] cells)
	{
		Width = width;
		Depth = depth;
		SegmentCount = segmentCount;
		Labels = labels;
		Kinds = kinds;
		Cells = cells;
	}
}

public static class MinimapTerrainSegmenter
{
	private const int KindFilterPasses = 2;
	private const int MinimumSegmentCells = 6;
	private const int MinimumIsolatedCells = 3;
	private const float MergeColourTolerance = 0.10f;
	private const int MergePasses = 4;

	public static MinimapTerrainSegments Build(MinimapFeatureGrid grid, bool[] waterMask, int width, int depth)
	{
		int cellCount = width * depth;
		var kinds = new TerrainSurfaceKind[cellCount];
		for (int index = 0; index < cellCount; index++)
		{
			kinds[index] = waterMask[index] ? TerrainSurfaceKind.Water : TerrainSurfaceClassifier.ClassifyCell(grid.Cells[index]);
		}

		kinds = MinimapMaskOperations.MajorityFilterKinds(kinds, width, depth, KindFilterPasses);
		for (int index = 0; index < cellCount; index++)
		{
			if (waterMask[index])
			{
				kinds[index] = TerrainSurfaceKind.Water;
			}
		}

		var labels = new int[cellCount];
		var segmentKinds = new List<TerrainSurfaceKind>();
		var segmentCells = new List<List<int>>();
		LabelSegments(kinds, waterMask, width, depth, labels, segmentKinds, segmentCells);
		MergeSmallSegments(grid, width, depth, labels, segmentCells);
		int[] compactLabels = CompactLabels(labels, segmentKinds.Count, segmentKinds, out TerrainSurfaceKind[] compactKinds, out int segmentCount);
		MinimapCellFeatures[] cells = BuildCellFeatures(grid, compactLabels, segmentCount, waterMask);
		return new MinimapTerrainSegments(width, depth, segmentCount, compactLabels, compactKinds, cells);
	}

	private static void LabelSegments(TerrainSurfaceKind[] kinds, bool[] waterMask, int width, int depth, int[] labels, List<TerrainSurfaceKind> segmentKinds, List<List<int>> segmentCells)
	{
		var queue = new Queue<int>();
		for (int start = 0; start < labels.Length; start++)
		{
			if (waterMask[start] || labels[start] != 0)
			{
				continue;
			}

			int label = segmentKinds.Count + 1;
			TerrainSurfaceKind kind = kinds[start];
			var cells = new List<int>();
			labels[start] = label;
			queue.Enqueue(start);
			while (queue.Count > 0)
			{
				int cell = queue.Dequeue();
				cells.Add(cell);
				int cellX = cell % width;
				int cellZ = cell / width;
				Visit(cellX - 1, cellZ, kind, label);
				Visit(cellX + 1, cellZ, kind, label);
				Visit(cellX, cellZ - 1, kind, label);
				Visit(cellX, cellZ + 1, kind, label);
			}

			segmentKinds.Add(kind);
			segmentCells.Add(cells);
		}

		return;

		void Visit(int x, int z, TerrainSurfaceKind kind, int label)
		{
			if (x < 0 || x >= width || z < 0 || z >= depth)
			{
				return;
			}

			int index = z * width + x;
			if (waterMask[index] || labels[index] != 0 || kinds[index] != kind)
			{
				return;
			}

			labels[index] = label;
			queue.Enqueue(index);
		}
	}

	private static void MergeSmallSegments(MinimapFeatureGrid grid, int width, int depth, int[] labels, List<List<int>> segmentCells)
	{
		int segmentCount = segmentCells.Count;
		var sumRed = new double[segmentCount];
		var sumGreen = new double[segmentCount];
		var sumBlue = new double[segmentCount];
		for (int segmentIndex = 0; segmentIndex < segmentCount; segmentIndex++)
		{
			List<int> cells = segmentCells[segmentIndex];
			for (int cellIndex = 0; cellIndex < cells.Count; cellIndex++)
			{
				MinimapCellFeatures cell = grid.Cells[cells[cellIndex]];
				sumRed[segmentIndex] += cell.Red;
				sumGreen[segmentIndex] += cell.Green;
				sumBlue[segmentIndex] += cell.Blue;
			}
		}

		for (int pass = 0; pass < MergePasses; pass++)
		{
			bool merged = false;
			for (int segmentIndex = 0; segmentIndex < segmentCount; segmentIndex++)
			{
				List<int> cells = segmentCells[segmentIndex];
				if (cells.Count == 0 || cells.Count >= MinimumSegmentCells)
				{
					continue;
				}

				int target = FindClosestNeighbour(width, depth, labels, segmentCells, sumRed, sumGreen, sumBlue, segmentIndex, out float distance);
				if (target < 0 || (distance > MergeColourTolerance && cells.Count > MinimumIsolatedCells))
				{
					continue;
				}

				MergeSegments(labels, segmentCells, sumRed, sumGreen, sumBlue, segmentIndex, target);
				merged = true;
			}

			if (!merged)
			{
				break;
			}
		}
	}

	private static int FindClosestNeighbour(int width, int depth, int[] labels, List<List<int>> segmentCells, double[] sumRed, double[] sumGreen, double[] sumBlue, int segmentIndex, out float distance)
	{
		float bestDistance = float.MaxValue;
		List<int> cells = segmentCells[segmentIndex];
		int cellCount = cells.Count;
		if (cellCount == 0)
		{
			distance = 0f;
			return -1;
		}

		float red = (float)(sumRed[segmentIndex] / cellCount);
		float green = (float)(sumGreen[segmentIndex] / cellCount);
		float blue = (float)(sumBlue[segmentIndex] / cellCount);
		int best = -1;
		for (int cellIndex = 0; cellIndex < cellCount; cellIndex++)
		{
			int cell = cells[cellIndex];
			int cellX = cell % width;
			int cellZ = cell / width;
			Consider(cellX - 1, cellZ);
			Consider(cellX + 1, cellZ);
			Consider(cellX, cellZ - 1);
			Consider(cellX, cellZ + 1);
		}

		distance = bestDistance;
		return best;

		void Consider(int x, int z)
		{
			if (x < 0 || x >= width || z < 0 || z >= depth)
			{
				return;
			}

			int neighbourLabel = labels[z * width + x];
			if (neighbourLabel == 0 || neighbourLabel == segmentIndex + 1)
			{
				return;
			}

			int candidate = neighbourLabel - 1;
			int candidateCount = segmentCells[candidate].Count;
			if (candidateCount == 0)
			{
				return;
			}

			float candidateRed = (float)(sumRed[candidate] / candidateCount);
			float candidateGreen = (float)(sumGreen[candidate] / candidateCount);
			float candidateBlue = (float)(sumBlue[candidate] / candidateCount);
			float deltaRed = candidateRed - red;
			float deltaGreen = candidateGreen - green;
			float deltaBlue = candidateBlue - blue;
			float candidateDistance = MathF.Sqrt(deltaRed * deltaRed + deltaGreen * deltaGreen + deltaBlue * deltaBlue);
			if (candidateDistance >= bestDistance)
			{
				return;
			}

			bestDistance = candidateDistance;
			best = candidate;
		}
	}

	private static void MergeSegments(int[] labels, List<List<int>> segmentCells, double[] sumRed, double[] sumGreen, double[] sumBlue, int source, int target)
	{
		List<int> cells = segmentCells[source];
		int targetLabel = target + 1;
		for (int cellIndex = 0; cellIndex < cells.Count; cellIndex++)
		{
			int cell = cells[cellIndex];
			labels[cell] = targetLabel;
			segmentCells[target].Add(cell);
		}

		sumRed[target] += sumRed[source];
		sumGreen[target] += sumGreen[source];
		sumBlue[target] += sumBlue[source];
		sumRed[source] = 0d;
		sumGreen[source] = 0d;
		sumBlue[source] = 0d;
		cells.Clear();
	}

	private static int[] CompactLabels(int[] labels, int labelCount, List<TerrainSurfaceKind> segmentKinds, out TerrainSurfaceKind[] compactKinds, out int segmentCount)
	{
		var remap = new int[labelCount + 1];
		segmentCount = 0;
		for (int index = 0; index < labels.Length; index++)
		{
			int label = labels[index];
			if (label == 0 || remap[label] != 0)
			{
				continue;
			}

			remap[label] = ++segmentCount;
		}

		compactKinds = new TerrainSurfaceKind[segmentCount + 1];
		var compact = new int[labels.Length];
		for (int index = 0; index < labels.Length; index++)
		{
			int label = labels[index];
			if (label == 0)
			{
				continue;
			}

			int target = remap[label];
			compact[index] = target;
			compactKinds[target] = segmentKinds[label - 1];
		}

		return compact;
	}

	private static MinimapCellFeatures[] BuildCellFeatures(MinimapFeatureGrid grid, int[] labels, int segmentCount, bool[] waterMask)
	{
		var cells = new MinimapCellFeatures[labels.Length];
		var sumRed = new double[segmentCount + 1];
		var sumGreen = new double[segmentCount + 1];
		var sumBlue = new double[segmentCount + 1];
		var sumContrast = new double[segmentCount + 1];
		var sumDetail = new double[segmentCount + 1];
		var counts = new int[segmentCount + 1];
		for (int index = 0; index < labels.Length; index++)
		{
			MinimapCellFeatures cell = grid.Cells[index];
			cells[index] = cell;
			int label = labels[index];
			if (label <= 0 || waterMask[index])
			{
				continue;
			}

			sumRed[label] += cell.Red;
			sumGreen[label] += cell.Green;
			sumBlue[label] += cell.Blue;
			sumContrast[label] += cell.LocalContrast;
			sumDetail[label] += cell.TextureDetail;
			counts[label]++;
		}

		var segmentCells = new MinimapCellFeatures[segmentCount + 1];
		for (int label = 1; label <= segmentCount; label++)
		{
			int count = counts[label];
			if (count == 0)
			{
				continue;
			}

			segmentCells[label] = MinimapCellFeatures.FromColour(
				(float)(sumRed[label] / count),
				(float)(sumGreen[label] / count),
				(float)(sumBlue[label] / count),
				(float)(sumContrast[label] / count),
				(float)(sumDetail[label] / count));
		}

		for (int index = 0; index < labels.Length; index++)
		{
			int label = labels[index];
			if (label <= 0 || waterMask[index] || counts[label] == 0)
			{
				continue;
			}

			cells[index] = segmentCells[label];
		}

		return cells;
	}
}
