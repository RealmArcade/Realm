using System;
using System.Collections.Generic;

namespace Realm.MapGeneration;

public static class MinimapTerrainReconstructor
{
	public static ReconstructedTerrain Reconstruct(MinimapImage image, MapGenerationPalette palette, PropCatalog catalog, TerrainReconstructionOptions options)
	{
		int width = Math.Max(8, options.Width);
		int depth = Math.Max(8, options.Depth);
		int cellCount = width * depth;

		var grid = new MinimapFeatureGrid(image, width, depth);
		bool[] waterMask = BuildWaterMask(grid, width, depth, options);
		TerrainSurfaceKind[] kinds = ClassifyCells(grid, waterMask, width, depth);
		float[] cellHeights = BuildCellHeights(grid, kinds, waterMask, width, depth, options);
		(float[] waterSurface, byte[] waterModes) = BuildWater(cellHeights, waterMask, width, depth, options);
		float[] vertexHeights = BuildVertexHeights(cellHeights, width, depth);
		float[] forestMask = BuildForestMask(grid, kinds, waterMask, width, depth);

		var terrain = new ReconstructedTerrain
		{
			Width = width,
			Depth = depth,
			QuadSize = options.QuadSize,
			TierHeight = options.TierHeight,
			VertexHeights = vertexHeights,
			CellHeights = cellHeights,
			WaterSurfaceHeights = waterSurface,
			WaterModes = waterModes,
			SplatIndices = new int[(width + 1) * (depth + 1)],
			CliffSplatIndices = new int[(width + 1) * (depth + 1)],
			PathingCodes = new byte[cellCount],
			CellKinds = kinds,
			WaterMask = waterMask,
			ForestMask = new bool[cellCount],
			FeatureColours = BuildFeatureColours(grid),
			WaterProfileIndex = palette.WaterProfileIndex
		};

		for (int index = 0; index < cellCount; index++)
		{
			terrain.ForestMask[index] = forestMask[index] > 0.5f;
		}

		AssignSplats(terrain, palette, options);
		AssignPathing(terrain, palette);
		if (options.PlaceProps)
		{
			List<GeneratedProp> detectedProps = MinimapObjectDetector.Detect(image, grid.Cells, waterMask, width, depth, options.QuadSize, catalog, options);
			for (int propIndex = 0; propIndex < detectedProps.Count; propIndex++)
			{
				GeneratedProp prop = detectedProps[propIndex];
				float targetY = terrain.CellHeights[CellIndexForWorld(prop, width, depth, options.QuadSize)];
				terrain.Props.Add(prop with { Y = targetY });
			}
		}

		return terrain;
	}

	private static float[] BuildFeatureColours(MinimapFeatureGrid grid)
	{
		var colours = new float[grid.Cells.Length * 3];
		for (int index = 0; index < grid.Cells.Length; index++)
		{
			colours[index * 3] = grid.Cells[index].Red;
			colours[index * 3 + 1] = grid.Cells[index].Green;
			colours[index * 3 + 2] = grid.Cells[index].Blue;
		}

		return colours;
	}

	private static bool[] BuildWaterMask(MinimapFeatureGrid grid, int width, int depth, TerrainReconstructionOptions options)
	{
		var raw = new bool[width * depth];
		for (int index = 0; index < raw.Length; index++)
		{
			raw[index] = IsWaterColor(grid.Cells[index]);
		}

		bool[] filtered = MinimapMaskOperations.MajorityFilter(raw, width, depth, 2);
		int minimumCells = Math.Max(6, (int)(width * depth * options.MinimumLakeFraction));
		List<List<int>> components = MinimapMaskOperations.LabelComponents(filtered, width, depth, minimumCells);
		var mask = new bool[width * depth];
		for (int componentIndex = 0; componentIndex < components.Count; componentIndex++)
		{
			List<int> cells = components[componentIndex];
			float detailSum = 0f;
			float contrastSum = 0f;
			for (int cellIndex = 0; cellIndex < cells.Count; cellIndex++)
			{
				detailSum += grid.Cells[cells[cellIndex]].TextureDetail;
				contrastSum += grid.Cells[cells[cellIndex]].LocalContrast;
			}

			if (detailSum / Math.Max(1, cells.Count) > 0.05f || contrastSum / Math.Max(1, cells.Count) > 0.06f)
			{
				continue;
			}

			float blueRedSum = 0f;
			float valueSum = 0f;
			for (int cellIndex = 0; cellIndex < cells.Count; cellIndex++)
			{
				MinimapCellFeatures cell = grid.Cells[cells[cellIndex]];
				blueRedSum += cell.Blue - cell.Red;
				valueSum += cell.Value;
			}

			if (blueRedSum / Math.Max(1, cells.Count) < 0.055f || valueSum / Math.Max(1, cells.Count) < 0.14f)
			{
				continue;
			}

			for (int cellIndex = 0; cellIndex < cells.Count; cellIndex++)
			{
				mask[cells[cellIndex]] = true;
			}
		}

		int wetCells = 0;
		for (int index = 0; index < mask.Length; index++)
		{
			if (mask[index])
			{
				wetCells++;
			}
		}

		if (wetCells < mask.Length * options.MinimumWaterFraction)
		{
			Array.Clear(mask);
		}

		return mask;
	}

	private static bool IsWaterColor(MinimapCellFeatures cell)
	{
		bool blueHue = cell.Hue >= 165f && cell.Hue <= 265f;
		if (blueHue && cell.Saturation >= 0.16f && cell.Value > 0.06f)
		{
			return true;
		}

		if (cell.Blue > cell.Red * 1.10f && cell.Blue > cell.Green * 0.98f && cell.Blue - cell.Red > 0.05f)
		{
			return true;
		}

		if (cell.Saturation < 0.14f && cell.Value < 0.34f && cell.Blue > cell.Red * 1.06f)
		{
			return true;
		}

		return false;
	}

	private static TerrainSurfaceKind[] ClassifyCells(MinimapFeatureGrid grid, bool[] waterMask, int width, int depth)
	{
		var kinds = new TerrainSurfaceKind[width * depth];
		for (int index = 0; index < kinds.Length; index++)
		{
			if (waterMask[index])
			{
				kinds[index] = TerrainSurfaceKind.Water;
				continue;
			}

			MinimapCellFeatures cell = grid.Cells[index];
			kinds[index] = ClassifyLandCell(cell);
		}

		return kinds;
	}

	private static TerrainSurfaceKind ClassifyLandCell(MinimapCellFeatures cell)
	{
		bool greenDominant = cell.Green >= cell.Red * 1.03f && cell.Green >= cell.Blue * 0.97f;
		if (greenDominant)
		{
			bool dark = cell.Value < 0.40f;
			bool textured = cell.LocalContrast > 0.055f;
			return dark || textured ? TerrainSurfaceKind.Forest : TerrainSurfaceKind.Grass;
		}

		if (cell.Saturation < 0.12f)
		{
			return cell.Value > 0.80f ? TerrainSurfaceKind.Snow : TerrainSurfaceKind.Rock;
		}

		if (cell.Hue >= 18f && cell.Hue <= 68f)
		{
			if (cell.Value > 0.60f && cell.Saturation < 0.52f)
			{
				return TerrainSurfaceKind.Sand;
			}

			return TerrainSurfaceKind.Soil;
		}

		if (cell.Hue >= 68f && cell.Hue <= 165f)
		{
			return TerrainSurfaceKind.Grass;
		}

		if (cell.Value < 0.30f)
		{
			return TerrainSurfaceKind.Rock;
		}

		return TerrainSurfaceKind.Soil;
	}

	private static float[] BuildForestMask(MinimapFeatureGrid grid, TerrainSurfaceKind[] kinds, bool[] waterMask, int width, int depth)
	{
		var forest = new float[width * depth];
		for (int index = 0; index < forest.Length; index++)
		{
			if (waterMask[index])
			{
				continue;
			}

			MinimapCellFeatures cell = grid.Cells[index];
			bool greenDominant = cell.Green >= cell.Red * 1.02f && cell.Green > cell.Blue * 0.97f;
			if (!greenDominant)
			{
				continue;
			}

			if (kinds[index] == TerrainSurfaceKind.Forest)
			{
				forest[index] = 1f;
				continue;
			}

			if (cell.Value < 0.42f && cell.Saturation > 0.18f)
			{
				forest[index] = 1f;
			}
		}

		bool[] mask = new bool[forest.Length];
		for (int index = 0; index < forest.Length; index++)
		{
			mask[index] = forest[index] > 0.5f;
		}

		bool[] cleaned = MinimapMaskOperations.MajorityFilter(mask, width, depth, 1);
		var result = new float[forest.Length];
		for (int index = 0; index < result.Length; index++)
		{
			result[index] = cleaned[index] ? 1f : 0f;
		}

		return result;
	}

	private static float[] BuildCellHeights(MinimapFeatureGrid grid, TerrainSurfaceKind[] kinds, bool[] waterMask, int width, int depth, TerrainReconstructionOptions options)
	{
		var heights = new float[width * depth];
		float landBase = options.BaseHeightTier * options.TierHeight;

		var offsets = new float[width * depth];
		for (int index = 0; index < offsets.Length; index++)
		{
			offsets[index] = KindHeightOffset(kinds[index], options.TierHeight);
		}

		float[] broad = BoxBlur(offsets, width, depth, Math.Max(2, Math.Min(width, depth) / 14) | 1);
		for (int index = 0; index < heights.Length; index++)
		{
			MinimapCellFeatures cell = grid.Cells[index];
			float relief = Math.Clamp(cell.LocalContrast * 0.6f, 0f, 0.25f);
			heights[index] = landBase + offsets[index] * 0.35f + broad[index] * 0.25f + relief;
		}

		int landShoreFalloff = Math.Max(1, Math.Min(width, depth) / 24);
		for (int index = 0; index < heights.Length; index++)
		{
			if (waterMask[index])
			{
				heights[index] = 0f;
				continue;
			}

			if (IsNearBorder(index % width, index / width, width, depth, landShoreFalloff) && BorderIsWater(waterMask, width, depth))
			{
				heights[index] -= options.TierHeight * 0.35f;
			}
		}

		if (!options.QuantizeHeights)
		{
			return heights;
		}

		for (int index = 0; index < heights.Length; index++)
		{
			if (waterMask[index])
			{
				continue;
			}

			float tierOffset = MathF.Round((heights[index] - landBase) / (options.TierHeight * 0.5f)) * 0.5f;
			heights[index] = landBase + tierOffset * options.TierHeight;
		}

		return heights;
	}

	private static bool BorderIsWater(bool[] waterMask, int width, int depth)
	{
		int wet = 0;
		int total = 0;
		for (int x = 0; x < width; x++)
		{
			if (waterMask[x]) wet++;
			if (waterMask[(depth - 1) * width + x]) wet++;
			total += 2;
		}

		for (int z = 0; z < depth; z++)
		{
			if (waterMask[z * width]) wet++;
			if (waterMask[z * width + width - 1]) wet++;
			total += 2;
		}

		return wet > total / 3;
	}

	private static bool IsNearBorder(int x, int z, int width, int depth, int margin)
	{
		return x < margin || z < margin || x >= width - margin || z >= depth - margin;
	}

	private static float KindHeightOffset(TerrainSurfaceKind kind, float tierHeight)
	{
		return kind switch
		{
			TerrainSurfaceKind.Water => -tierHeight,
			TerrainSurfaceKind.Sand => -tierHeight * 0.16f,
			TerrainSurfaceKind.Soil => -tierHeight * 0.03f,
			TerrainSurfaceKind.Grass => 0f,
			TerrainSurfaceKind.Forest => tierHeight * 0.03f,
			TerrainSurfaceKind.Rock => tierHeight * 0.35f,
			TerrainSurfaceKind.Snow => tierHeight * 0.9f,
			_ => 0f
		};
	}

	private static (float[] SurfaceHeights, byte[] Modes) BuildWater(float[] cellHeights, bool[] waterMask, int width, int depth, TerrainReconstructionOptions options)
	{
		var surfaceHeights = new float[width * depth];
		var modes = new byte[width * depth];
		List<List<int>> components = MinimapMaskOperations.LabelComponents(waterMask, width, depth, 1);
		int[] distanceToLand = MinimapMaskOperations.DistanceToMask(Invert(waterMask), width, depth);
		float maximumDistance = Math.Max(2f, Math.Min(width, depth) * 0.10f);

		var componentSurfaces = new float[components.Count];
		for (int componentIndex = 0; componentIndex < components.Count; componentIndex++)
		{
			List<int> cells = components[componentIndex];
			var shoreHeights = new List<float>();
			for (int cellIndex = 0; cellIndex < cells.Count; cellIndex++)
			{
				int cell = cells[cellIndex];
				int cellX = cell % width;
				int cellZ = cell / width;
				CollectShoreHeights(cellHeights, waterMask, width, depth, cellX - 1, cellZ, shoreHeights);
				CollectShoreHeights(cellHeights, waterMask, width, depth, cellX + 1, cellZ, shoreHeights);
				CollectShoreHeights(cellHeights, waterMask, width, depth, cellX, cellZ - 1, shoreHeights);
				CollectShoreHeights(cellHeights, waterMask, width, depth, cellX, cellZ + 1, shoreHeights);
			}

			float surface = shoreHeights.Count > 0 ? Median(shoreHeights) - options.WaterSurfaceDrop : -options.WaterSurfaceDrop;
			componentSurfaces[componentIndex] = surface;
		}

		int[] labels = LabelWaterCells(waterMask, width, depth);
		for (int index = 0; index < waterMask.Length; index++)
		{
			if (!waterMask[index])
			{
				continue;
			}

			int label = labels[index];
			float surface = label > 0 && label <= componentSurfaces.Length ? componentSurfaces[label - 1] : -options.WaterSurfaceDrop;
			float normalisedDepth = Math.Clamp(distanceToLand[index] / maximumDistance, 0f, 1f);
			float bottom = surface - (options.TierHeight * 0.35f + normalisedDepth * options.TierHeight * 1.1f);
			cellHeights[index] = bottom;
			surfaceHeights[index] = surface;
			modes[index] = normalisedDepth > 0.45f ? (byte)2 : (byte)1;
		}

		return (surfaceHeights, modes);
	}

	private static int[] LabelWaterCells(bool[] waterMask, int width, int depth)
	{
		var labels = new int[waterMask.Length];
		var queue = new Queue<int>();
		int label = 0;
		for (int start = 0; start < waterMask.Length; start++)
		{
			if (!waterMask[start] || labels[start] != 0) continue;
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
			if (!waterMask[index] || labels[index] != 0) return;
			labels[index] = label;
			queue.Enqueue(index);
		}
	}

	private static void CollectShoreHeights(float[] cellHeights, bool[] waterMask, int width, int depth, int x, int z, List<float> heights)
	{
		if (x < 0 || x >= width || z < 0 || z >= depth) return;
		int index = z * width + x;
		if (waterMask[index]) return;
		heights.Add(cellHeights[index]);
	}

	private static float Median(List<float> values)
	{
		values.Sort();
		return values[values.Count / 2];
	}

	private static bool[] Invert(bool[] mask)
	{
		var inverted = new bool[mask.Length];
		for (int index = 0; index < mask.Length; index++)
		{
			inverted[index] = !mask[index];
		}

		return inverted;
	}

	private static float[] BuildVertexHeights(float[] cellHeights, int width, int depth)
	{
		var vertices = new float[(width + 1) * (depth + 1)];
		for (int z = 0; z <= depth; z++)
		{
			for (int x = 0; x <= width; x++)
			{
				double sum = 0;
				int count = 0;
				Accumulate(cellHeights, width, depth, x - 1, z - 1, ref sum, ref count);
				Accumulate(cellHeights, width, depth, x, z - 1, ref sum, ref count);
				Accumulate(cellHeights, width, depth, x - 1, z, ref sum, ref count);
				Accumulate(cellHeights, width, depth, x, z, ref sum, ref count);
				vertices[z * (width + 1) + x] = (float)(sum / Math.Max(1, count));
			}
		}

		return vertices;
	}

	private static void Accumulate(float[] cellHeights, int width, int depth, int x, int z, ref double sum, ref int count)
	{
		if (x < 0 || x >= width || z < 0 || z >= depth) return;
		sum += cellHeights[z * width + x];
		count++;
	}

	private static float[] BoxBlur(float[] values, int width, int depth, int radius)
	{
		int effectiveRadius = Math.Max(1, radius / 2);
		var horizontal = new float[values.Length];
		var result = new float[values.Length];
		for (int z = 0; z < depth; z++)
		{
			for (int x = 0; x < width; x++)
			{
				float sum = 0;
				int count = 0;
				for (int offset = -effectiveRadius; offset <= effectiveRadius; offset++)
				{
					int sampleX = x + offset;
					if (sampleX < 0 || sampleX >= width) continue;
					sum += values[z * width + sampleX];
					count++;
				}

				horizontal[z * width + x] = sum / Math.Max(1, count);
			}
		}

		for (int z = 0; z < depth; z++)
		{
			for (int x = 0; x < width; x++)
			{
				float sum = 0;
				int count = 0;
				for (int offset = -effectiveRadius; offset <= effectiveRadius; offset++)
				{
					int sampleZ = z + offset;
					if (sampleZ < 0 || sampleZ >= depth) continue;
					sum += horizontal[sampleZ * width + x];
					count++;
				}

				result[z * width + x] = sum / Math.Max(1, count);
			}
		}

		return result;
	}

	private static void AssignSplats(ReconstructedTerrain terrain, MapGenerationPalette palette, TerrainReconstructionOptions options)
	{
		int width = terrain.Width;
		int depth = terrain.Depth;
		for (int z = 0; z <= depth; z++)
		{
			for (int x = 0; x <= width; x++)
			{
				int cellX = Math.Min(x, width - 1);
				int cellZ = Math.Min(z, depth - 1);
				int cellIndex = cellZ * width + cellX;
				TerrainSurfaceKind kind = terrain.CellKinds[cellIndex];
				if (terrain.WaterMask[cellIndex])
				{
					float depthValue = terrain.WaterSurfaceHeights[cellIndex] - terrain.CellHeights[cellIndex];
					kind = depthValue < options.TierHeight * 0.6f ? TerrainSurfaceKind.Sand : TerrainSurfaceKind.Rock;
				}

				MinimapCellFeatures cell = CellFeaturesAt(terrain, cellIndex);
				uint hash = DeterministicHash.Combine(x, z, options.Seed);
				TerrainSwatch swatch = terrain.WaterMask[cellIndex]
					? palette.FindForKind(kind, (int)hash, cell.Red, cell.Green, cell.Blue, true)
					: palette.FindClosest(kind, cell.Red, cell.Green, cell.Blue, (int)hash);
				TerrainSwatch cliff = palette.FindForKind(TerrainSurfaceKind.Rock, (int)(hash >> 8));
				int nodeIndex = z * (width + 1) + x;
				terrain.SplatIndices[nodeIndex] = swatch.Index;
				terrain.CliffSplatIndices[nodeIndex] = cliff.Index;
			}
		}
	}

	private static MinimapCellFeatures CellFeaturesAt(ReconstructedTerrain terrain, int cellIndex)
	{
		return terrain.FeatureColours.Length > cellIndex * 3 ? new MinimapCellFeatures(
			terrain.FeatureColours[cellIndex * 3],
			terrain.FeatureColours[cellIndex * 3 + 1],
			terrain.FeatureColours[cellIndex * 3 + 2],
			0f, 0f, 0f, 0f, 0f) : default;
	}

	private static void AssignPathing(ReconstructedTerrain terrain, MapGenerationPalette palette)
	{
		int width = terrain.Width;
		int depth = terrain.Depth;
		for (int z = 0; z < depth; z++)
		{
			for (int x = 0; x < width; x++)
			{
				int index = z * width + x;
				if (terrain.WaterMask[index])
				{
					terrain.PathingCodes[index] = terrain.WaterModes[index] == 2 ? TerrainPathingCodes.DeepWater : TerrainPathingCodes.ShallowWater;
					continue;
				}

				TerrainSwatch swatch = palette.FindForKind(terrain.CellKinds[index], (int)DeterministicHash.Combine(x, z, 7));
				int code = swatch.DefaultPathingCode;
				terrain.PathingCodes[index] = code <= 0 || code == 44 ? TerrainPathingCodes.Ground : (byte)code;
			}
		}
	}

	private static int CellIndexForWorld(GeneratedProp prop, int width, int depth, float quadSize)
	{
		int cellX = Math.Clamp((int)(prop.X / quadSize + width * 0.5f), 0, width - 1);
		int cellZ = Math.Clamp((int)(prop.Z / quadSize + depth * 0.5f), 0, depth - 1);
		return cellZ * width + cellX;
	}

}
