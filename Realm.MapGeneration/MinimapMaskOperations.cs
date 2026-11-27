using System;
using System.Collections.Generic;

namespace Realm.MapGeneration;

public static class MinimapMaskOperations
{
	public static bool[] MajorityFilter(bool[] mask, int width, int depth, int passes)
	{
		var current = (bool[])mask.Clone();
		var next = new bool[mask.Length];
		for (int pass = 0; pass < passes; pass++)
		{
			for (int z = 0; z < depth; z++)
			{
				for (int x = 0; x < width; x++)
				{
					int votes = 0;
					int count = 0;
					for (int offsetZ = -1; offsetZ <= 1; offsetZ++)
					{
						int sampleZ = z + offsetZ;
						if (sampleZ < 0 || sampleZ >= depth) continue;
						for (int offsetX = -1; offsetX <= 1; offsetX++)
						{
							int sampleX = x + offsetX;
							if (sampleX < 0 || sampleX >= width) continue;
							if (current[sampleZ * width + sampleX]) votes++;
							count++;
						}
					}

					next[z * width + x] = votes * 2 > count;
				}
			}

			(current, next) = (next, current);
		}

		return current;
	}

	public static List<List<int>> LabelComponents(bool[] mask, int width, int depth, int minimumSize)
	{
		var labels = new int[mask.Length];
		var components = new List<List<int>>();
		var queue = new Queue<int>();
		int nextLabel = 0;

		for (int start = 0; start < mask.Length; start++)
		{
			if (!mask[start] || labels[start] != 0)
			{
				continue;
			}

			nextLabel++;
			var cells = new List<int>();
			queue.Enqueue(start);
			labels[start] = nextLabel;
			while (queue.Count > 0)
			{
				int cell = queue.Dequeue();
				cells.Add(cell);
				int cellX = cell % width;
				int cellZ = cell / width;
				Enqueue(cellX - 1, cellZ);
				Enqueue(cellX + 1, cellZ);
				Enqueue(cellX, cellZ - 1);
				Enqueue(cellX, cellZ + 1);
			}

			if (cells.Count >= minimumSize)
			{
				components.Add(cells);
			}
			else
			{
				for (int index = 0; index < cells.Count; index++)
				{
					labels[cells[index]] = 0;
				}
			}
		}

		return components;

		void Enqueue(int x, int z)
		{
			if (x < 0 || x >= width || z < 0 || z >= depth) return;
			int index = z * width + x;
			if (!mask[index] || labels[index] != 0) return;
			labels[index] = nextLabel;
			queue.Enqueue(index);
		}
	}

	public static int[] KeepLargeComponents(bool[] mask, int width, int depth, int minimumSize)
	{
		var filtered = new bool[mask.Length];
		List<List<int>> components = LabelComponents(mask, width, depth, minimumSize);
		for (int componentIndex = 0; componentIndex < components.Count; componentIndex++)
		{
			List<int> cells = components[componentIndex];
			for (int cellIndex = 0; cellIndex < cells.Count; cellIndex++)
			{
				filtered[cells[cellIndex]] = true;
			}
		}

		var labels = new int[mask.Length];
		int label = 0;
		var queue = new Queue<int>();
		for (int start = 0; start < filtered.Length; start++)
		{
			if (!filtered[start] || labels[start] != 0) continue;
			label++;
			queue.Enqueue(start);
			labels[start] = label;
			while (queue.Count > 0)
			{
				int cell = queue.Dequeue();
				int cellX = cell % width;
				int cellZ = cell / width;
				Visit(cellX - 1, cellZ);
				Visit(cellX + 1, cellZ);
				Visit(cellX, cellZ - 1);
				Visit(cellX, cellZ + 1);
			}
		}

		return labels;

		void Visit(int x, int z)
		{
			if (x < 0 || x >= width || z < 0 || z >= depth) return;
			int index = z * width + x;
			if (!filtered[index] || labels[index] != 0) return;
			labels[index] = label;
			queue.Enqueue(index);
		}
	}

	public static int[] DistanceToMask(bool[] mask, int width, int depth)
	{
		int maximum = width + depth;
		var distance = new int[mask.Length];
		var queue = new Queue<int>();
		for (int index = 0; index < mask.Length; index++)
		{
			if (mask[index])
			{
				distance[index] = 0;
				queue.Enqueue(index);
			}
			else
			{
				distance[index] = maximum;
			}
		}

		while (queue.Count > 0)
		{
			int cell = queue.Dequeue();
			int cellX = cell % width;
			int cellZ = cell / width;
			int next = distance[cell] + 1;
			Visit(cellX - 1, cellZ, next);
			Visit(cellX + 1, cellZ, next);
			Visit(cellX, cellZ - 1, next);
			Visit(cellX, cellZ + 1, next);
		}

		return distance;

		void Visit(int x, int z, int next)
		{
			if (x < 0 || x >= width || z < 0 || z >= depth) return;
			int index = z * width + x;
			if (distance[index] <= next) return;
			distance[index] = next;
			queue.Enqueue(index);
		}
	}
}
