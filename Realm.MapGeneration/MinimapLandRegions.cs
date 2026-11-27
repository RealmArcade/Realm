using System;
using System.Collections.Generic;

namespace Realm.MapGeneration;

public static class MinimapLandRegions
{
	private const int MaximumRegions = 8;
	private const int MinimumRegions = 3;
	private const int Iterations = 12;
	private const float MinimumRegionDistance = 0.02f;

	public readonly record struct Result(TerrainSurfaceKind[] Kinds, float[] RegionColours);

	public static Result Classify(MinimapFeatureGrid grid, bool[] waterMask, bool[] voidCells, int width, int depth)
	{
		var kinds = new TerrainSurfaceKind[width * depth];
		var regionColours = new float[width * depth * 3];
		var land = new List<int>();
		for (int index = 0; index < kinds.Length; index++)
		{
			if (voidCells[index])
			{
				kinds[index] = TerrainSurfaceKind.Rock;
				CopyColour(grid, index, regionColours);
				continue;
			}

			if (waterMask[index])
			{
				kinds[index] = TerrainSurfaceKind.Water;
				CopyColour(grid, index, regionColours);
				continue;
			}

			land.Add(index);
			CopyColour(grid, index, regionColours);
		}

		if (land.Count == 0)
		{
			return new Result(kinds, regionColours);
		}

		int regionCount = Math.Clamp((int)MathF.Round(MathF.Sqrt(land.Count / 40f)), MinimumRegions, MaximumRegions);
		regionCount = Math.Min(regionCount, land.Count);
		float[][] centres = SeedCentres(grid, land, regionCount);
		var assignments = new int[land.Count];

		for (int iteration = 0; iteration < Iterations; iteration++)
		{
			bool changed = false;
			for (int position = 0; position < land.Count; position++)
			{
				MinimapCellFeatures cell = grid.Cells[land[position]];
				int best = 0;
				float bestDistance = float.MaxValue;
				for (int region = 0; region < centres.Length; region++)
				{
					float deltaRed = cell.Red - centres[region][0];
					float deltaGreen = cell.Green - centres[region][1];
					float deltaBlue = cell.Blue - centres[region][2];
					float distance = deltaRed * deltaRed + deltaGreen * deltaGreen + deltaBlue * deltaBlue;
					if (distance < bestDistance)
					{
						bestDistance = distance;
						best = region;
					}
				}

				if (assignments[position] != best)
				{
					assignments[position] = best;
					changed = true;
				}
			}

			var totals = new float[centres.Length * 3];
			var counts = new int[centres.Length];
			for (int position = 0; position < land.Count; position++)
			{
				MinimapCellFeatures cell = grid.Cells[land[position]];
				int region = assignments[position];
				totals[region * 3] += cell.Red;
				totals[region * 3 + 1] += cell.Green;
				totals[region * 3 + 2] += cell.Blue;
				counts[region]++;
			}

			for (int region = 0; region < centres.Length; region++)
			{
				if (counts[region] == 0)
				{
					continue;
				}

				centres[region][0] = totals[region * 3] / counts[region];
				centres[region][1] = totals[region * 3 + 1] / counts[region];
				centres[region][2] = totals[region * 3 + 2] / counts[region];
			}

			if (!changed)
			{
				break;
			}
		}

		MergeSimilarCentres(centres, assignments, land, grid);

		var regionKinds = new TerrainSurfaceKind[centres.Length];
		for (int region = 0; region < centres.Length; region++)
		{
			regionKinds[region] = MinimapTerrainReconstructor.ClassifyLandCell(AggregateFeatures(grid, land, assignments, region, centres[region]));
		}

		DiversifyKinds(regionKinds);
		for (int position = 0; position < land.Count; position++)
		{
			int index = land[position];
			int region = assignments[position];
			kinds[index] = regionKinds[region];
			regionColours[index * 3] = centres[region][0];
			regionColours[index * 3 + 1] = centres[region][1];
			regionColours[index * 3 + 2] = centres[region][2];
		}

		return new Result(kinds, regionColours);
	}

	private static float[][] SeedCentres(MinimapFeatureGrid grid, List<int> land, int regionCount)
	{
		var ordered = new List<int>(land);
		ordered.Sort((left, right) =>
		{
			MinimapCellFeatures a = grid.Cells[left];
			MinimapCellFeatures b = grid.Cells[right];
			float luminanceA = a.Red * 0.2126f + a.Green * 0.7152f + a.Blue * 0.0722f;
			float luminanceB = b.Red * 0.2126f + b.Green * 0.7152f + b.Blue * 0.0722f;
			return luminanceA.CompareTo(luminanceB);
		});

		var centres = new float[regionCount][];
		for (int region = 0; region < regionCount; region++)
		{
			int position = (int)((region + 0.5f) / regionCount * ordered.Count);
			MinimapCellFeatures cell = grid.Cells[ordered[Math.Min(ordered.Count - 1, position)]];
			centres[region] = new[] { cell.Red, cell.Green, cell.Blue };
		}

		return centres;
	}

	private static void MergeSimilarCentres(float[][] centres, int[] assignments, List<int> land, MinimapFeatureGrid grid)
	{
		for (int left = 0; left < centres.Length; left++)
		{
			for (int right = left + 1; right < centres.Length; right++)
			{
				float deltaRed = centres[left][0] - centres[right][0];
				float deltaGreen = centres[left][1] - centres[right][1];
				float deltaBlue = centres[left][2] - centres[right][2];
				if (MathF.Sqrt(deltaRed * deltaRed + deltaGreen * deltaGreen + deltaBlue * deltaBlue) > MinimumRegionDistance)
				{
					continue;
				}

				for (int position = 0; position < land.Count; position++)
				{
					if (assignments[position] == right)
					{
						assignments[position] = left;
					}
				}

				centres[right] = new[] { centres[left][0], centres[left][1], centres[left][2] };
				break;
			}
		}
	}

	private static MinimapCellFeatures AggregateFeatures(MinimapFeatureGrid grid, List<int> land, int[] assignments, int region, float[] centre)
	{
		float hue = 0f;
		float saturation = 0f;
		float value = 0f;
		float contrast = 0f;
		float detail = 0f;
		int count = 0;
		for (int position = 0; position < land.Count; position++)
		{
			if (assignments[position] != region)
			{
				continue;
			}

			MinimapCellFeatures cell = grid.Cells[land[position]];
			hue += cell.Hue;
			saturation += cell.Saturation;
			value += cell.Value;
			contrast += cell.LocalContrast;
			detail += cell.TextureDetail;
			count++;
		}

		if (count == 0)
		{
			return new MinimapCellFeatures(centre[0], centre[1], centre[2], 0f, 0f, 0f, 0f, 0f);
		}

		return new MinimapCellFeatures(
			centre[0],
			centre[1],
			centre[2],
			hue / count,
			saturation / count,
			value / count,
			contrast / count,
			detail / count);
	}

	private static void DiversifyKinds(TerrainSurfaceKind[] regionKinds)
	{
		var used = new bool[16];
		for (int region = 0; region < regionKinds.Length; region++)
		{
			TerrainSurfaceKind kind = regionKinds[region];
			int code = (int)kind;
			if (!used[code])
			{
				used[code] = true;
				continue;
			}

			TerrainSurfaceKind alternate = kind switch
			{
				TerrainSurfaceKind.Grass => TerrainSurfaceKind.Forest,
				TerrainSurfaceKind.Forest => TerrainSurfaceKind.Grass,
				TerrainSurfaceKind.Soil => TerrainSurfaceKind.Sand,
				TerrainSurfaceKind.Sand => TerrainSurfaceKind.Soil,
				TerrainSurfaceKind.Rock => TerrainSurfaceKind.Unknown,
				_ => kind
			};

			regionKinds[region] = alternate;
		}
	}

	private static void CopyColour(MinimapFeatureGrid grid, int index, float[] colours)
	{
		MinimapCellFeatures cell = grid.Cells[index];
		colours[index * 3] = cell.Red;
		colours[index * 3 + 1] = cell.Green;
		colours[index * 3 + 2] = cell.Blue;
	}
}
