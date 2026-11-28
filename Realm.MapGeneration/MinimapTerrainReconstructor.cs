using System;
using System.Collections.Generic;
using Realm.Ecs.Components.Terrain;

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
		MinimapTerrainSegments segments = MinimapTerrainSegmenter.Build(grid, waterMask, width, depth);
		int[] groundSwatches = ChooseGroundSwatches(grid, segments, palette);
		TerrainSurfaceKind[] kinds = BuildCellKinds(segments, waterMask);
		float[] cellHeights = BuildCellHeights(segments.Cells, kinds, waterMask, width, depth, options);
		(float[] waterSurface, byte[] waterModes) = BuildWater(cellHeights, waterMask, width, depth, options);
		float[] vertexHeights = BuildVertexHeights(cellHeights, width, depth);
		float[] forestMask = BuildForestMask(segments.Cells, kinds, waterMask, width, depth);

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
			RegionColours = BuildRegionColours(grid, segments),
			SegmentLabels = segments.Labels,
			WaterProfileIndex = palette.WaterProfileIndex
		};

		for (int index = 0; index < cellCount; index++)
		{
			terrain.ForestMask[index] = forestMask[index] > 0.5f;
		}

		AssignSplats(terrain, segments.Labels, groundSwatches, palette, options);
		AssignPathing(terrain);
		if (options.PlaceProps)
		{
			List<GeneratedProp> detectedProps = MinimapObjectDetector.Detect(image, segments.Cells, waterMask, width, depth, options.QuadSize, catalog, options);
			for (int propIndex = 0; propIndex < detectedProps.Count; propIndex++)
			{
				GeneratedProp prop = detectedProps[propIndex];
				float targetY = terrain.CellHeights[CellIndexForWorld(prop, width, depth, options.QuadSize)];
				terrain.Props.Add(prop with { Y = targetY });
			}
		}

		return terrain;
	}

	private static TerrainSurfaceKind[] BuildCellKinds(MinimapTerrainSegments segments, bool[] waterMask)
	{
		var kinds = new TerrainSurfaceKind[segments.Labels.Length];
		for (int index = 0; index < kinds.Length; index++)
		{
			int label = segments.Labels[index];
			kinds[index] = waterMask[index]
				? TerrainSurfaceKind.Water
				: label > 0 ? segments.Kinds[label] : TerrainSurfaceClassifier.ClassifyCell(segments.Cells[index]);
		}

		return kinds;
	}

	private static float[] BuildRegionColours(MinimapFeatureGrid grid, MinimapTerrainSegments segments)
	{
		var colours = new float[grid.Cells.Length * 3];
		for (int index = 0; index < grid.Cells.Length; index++)
		{
			MinimapCellFeatures cell = segments.Labels[index] > 0 ? segments.Cells[index] : grid.Cells[index];
			colours[index * 3] = cell.Red;
			colours[index * 3 + 1] = cell.Green;
			colours[index * 3 + 2] = cell.Blue;
		}

		return colours;
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

	private static float[] BuildForestMask(MinimapCellFeatures[] cells, TerrainSurfaceKind[] kinds, bool[] waterMask, int width, int depth)
	{
		var forest = new float[width * depth];
		for (int index = 0; index < forest.Length; index++)
		{
			if (waterMask[index])
			{
				continue;
			}

			MinimapCellFeatures cell = cells[index];
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

	private static float[] BuildCellHeights(MinimapCellFeatures[] cells, TerrainSurfaceKind[] kinds, bool[] waterMask, int width, int depth, TerrainReconstructionOptions options)
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
			MinimapCellFeatures cell = cells[index];
			float relief = Math.Clamp(cell.LocalContrast / 0.05f, 0f, 1f) * options.TierHeight * 0.5f;
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

	private static void AssignSplats(ReconstructedTerrain terrain, int[] segmentLabels, int[] groundSwatches, MapGenerationPalette palette, TerrainReconstructionOptions options)
	{
		int width = terrain.Width;
		int depth = terrain.Depth;
		int[] cliffSwatches = ChooseCliffSwatches(terrain, segmentLabels, palette);
		for (int z = 0; z <= depth; z++)
		{
			for (int x = 0; x <= width; x++)
			{
				int cellIndex = DominantVertexCell(terrain, x, z);
				int nodeIndex = z * (width + 1) + x;
				if (terrain.WaterMask[cellIndex])
				{
					float depthValue = terrain.WaterSurfaceHeights[cellIndex] - terrain.CellHeights[cellIndex];
					TerrainSurfaceKind kind = depthValue < options.TierHeight * 0.6f ? TerrainSurfaceKind.Sand : TerrainSurfaceKind.Rock;
					float red = terrain.RegionColours[cellIndex * 3];
					float green = terrain.RegionColours[cellIndex * 3 + 1];
					float blue = terrain.RegionColours[cellIndex * 3 + 2];
					TerrainSwatch swatch = palette.FindForKind(kind, 0, red, green, blue, true);
					terrain.SplatIndices[nodeIndex] = swatch.Index;
					terrain.CliffSplatIndices[nodeIndex] = swatch.Index;
					continue;
				}

				int label = segmentLabels[cellIndex];
				if (label > 0 && label < groundSwatches.Length && groundSwatches[label] >= 0)
				{
					terrain.SplatIndices[nodeIndex] = groundSwatches[label];
					terrain.CliffSplatIndices[nodeIndex] = cliffSwatches[label] >= 0 ? cliffSwatches[label] : groundSwatches[label];
					continue;
				}

				float fallbackRed = terrain.RegionColours[cellIndex * 3];
				float fallbackGreen = terrain.RegionColours[cellIndex * 3 + 1];
				float fallbackBlue = terrain.RegionColours[cellIndex * 3 + 2];
				TerrainSwatch fallback = palette.FindClosest(terrain.CellKinds[cellIndex], fallbackRed, fallbackGreen, fallbackBlue, 0);
				terrain.SplatIndices[nodeIndex] = fallback.Index;
				terrain.CliffSplatIndices[nodeIndex] = fallback.Index;
			}
		}
	}

	private static int[] ChooseGroundSwatches(MinimapFeatureGrid grid, MinimapTerrainSegments segments, MapGenerationPalette palette)
	{
		int segmentCount = segments.SegmentCount;
		var chosen = new int[segmentCount + 1];
		Array.Fill(chosen, -1);
		if (segmentCount == 0 || palette.Swatches.Count == 0)
		{
			return chosen;
		}

		List<int>[] segmentCells = BuildSegmentCellLists(segments);
		List<int>[] neighbours = BuildSegmentNeighbours(segments.Labels, segments.Width, segments.Depth, segmentCount);
		var order = new int[segmentCount];
		int orderCount = 0;
		for (int label = 1; label <= segmentCount; label++)
		{
			if (segmentCells[label].Count > 0)
			{
				order[orderCount++] = label;
			}
		}

		Array.Sort(order, 0, orderCount, Comparer<int>.Create((left, right) => segmentCells[right].Count.CompareTo(segmentCells[left].Count)));
		for (int orderIndex = 0; orderIndex < orderCount; orderIndex++)
		{
			int label = order[orderIndex];
			List<int> cells = segmentCells[label];
			var votes = new Dictionary<int, int>();
			for (int cellIndex = 0; cellIndex < cells.Count; cellIndex++)
			{
				MinimapCellFeatures cell = grid.Cells[cells[cellIndex]];
				TerrainSwatch nearest = palette.FindNearestColourOfKind(segments.Kinds[label], cell.Red, cell.Green, cell.Blue);
				votes.TryGetValue(nearest.Index, out int count);
				votes[nearest.Index] = count + 1;
			}

			List<TerrainSwatch> candidates = OrderByVotes(votes, palette);
			chosen[label] = SelectDistinctSwatch(candidates, chosen, neighbours[label], palette);
		}

		return chosen;
	}

	private static List<int>[] BuildSegmentCellLists(MinimapTerrainSegments segments)
	{
		var cells = new List<int>[segments.SegmentCount + 1];
		for (int label = 1; label <= segments.SegmentCount; label++)
		{
			cells[label] = new List<int>();
		}

		for (int index = 0; index < segments.Labels.Length; index++)
		{
			int label = segments.Labels[index];
			if (label > 0)
			{
				cells[label].Add(index);
			}
		}

		return cells;
	}

	private static List<TerrainSwatch> OrderByVotes(Dictionary<int, int> votes, MapGenerationPalette palette)
	{
		var candidates = new List<TerrainSwatch>(votes.Count);
		foreach (KeyValuePair<int, int> vote in votes)
		{
			candidates.Add(palette.FindByIndex(vote.Key));
		}

		candidates.Sort((left, right) => votes[right.Index].CompareTo(votes[left.Index]));
		return candidates;
	}

	private static int SelectDistinctSwatch(List<TerrainSwatch> candidates, int[] chosen, List<int> neighbours, MapGenerationPalette palette)
	{
		for (int candidateIndex = 0; candidateIndex < candidates.Count; candidateIndex++)
		{
			TerrainSwatch candidate = candidates[candidateIndex];
			bool taken = false;
			for (int neighbourIndex = 0; neighbourIndex < neighbours.Count; neighbourIndex++)
			{
				int neighbourSwatch = chosen[neighbours[neighbourIndex]];
				if (neighbourSwatch < 0 || neighbourSwatch != candidate.Index)
				{
					continue;
				}

				if (palette.FindByIndex(neighbourSwatch).Kind == candidate.Kind)
				{
					taken = true;
					break;
				}
			}

			if (!taken)
			{
				return candidate.Index;
			}
		}

		return candidates.Count > 0 ? candidates[0].Index : -1;
	}

	private static int[] ChooseCliffSwatches(ReconstructedTerrain terrain, int[] segmentLabels, MapGenerationPalette palette)
	{
		int segmentCount = 0;
		for (int index = 0; index < segmentLabels.Length; index++)
		{
			segmentCount = Math.Max(segmentCount, segmentLabels[index]);
		}

		var chosen = new int[segmentCount + 1];
		Array.Fill(chosen, -1);
		var seen = new bool[segmentCount + 1];
		for (int index = 0; index < segmentLabels.Length; index++)
		{
			int label = segmentLabels[index];
			if (label <= 0 || seen[label])
			{
				continue;
			}

			seen[label] = true;
			float red = terrain.RegionColours[index * 3];
			float green = terrain.RegionColours[index * 3 + 1];
			float blue = terrain.RegionColours[index * 3 + 2];
			IReadOnlyList<TerrainSwatch> ranked = palette.RankForKind(TerrainSurfaceKind.Rock, red, green, blue);
			if (ranked.Count > 0)
			{
				chosen[label] = ranked[0].Index;
			}
		}

		return chosen;
	}

	private static List<int>[] BuildSegmentNeighbours(int[] segmentLabels, int width, int depth, int segmentCount)
	{
		var neighbours = new List<int>[segmentCount + 1];
		for (int label = 1; label <= segmentCount; label++)
		{
			neighbours[label] = new List<int>();
		}

		for (int z = 0; z < depth; z++)
		{
			for (int x = 0; x < width; x++)
			{
				int label = segmentLabels[z * width + x];
				if (label <= 0)
				{
					continue;
				}

				if (x + 1 < width)
				{
					LinkNeighbours(neighbours, label, segmentLabels[z * width + x + 1]);
				}

				if (z + 1 < depth)
				{
					LinkNeighbours(neighbours, label, segmentLabels[(z + 1) * width + x]);
				}
			}
		}

		return neighbours;
	}

	private static void LinkNeighbours(List<int>[] neighbours, int label, int otherLabel)
	{
		if (otherLabel <= 0 || otherLabel == label || otherLabel >= neighbours.Length)
		{
			return;
		}

		if (!neighbours[label].Contains(otherLabel))
		{
			neighbours[label].Add(otherLabel);
		}

		if (!neighbours[otherLabel].Contains(label))
		{
			neighbours[otherLabel].Add(label);
		}
	}

	private static int DominantVertexCell(ReconstructedTerrain terrain, int vertexX, int vertexZ)
	{
		int width = terrain.Width;
		int depth = terrain.Depth;
		Span<int> candidates = stackalloc int[4];
		int count = 0;
		for (int offsetZ = -1; offsetZ <= 0; offsetZ++)
		{
			for (int offsetX = -1; offsetX <= 0; offsetX++)
			{
				int x = vertexX + offsetX;
				int z = vertexZ + offsetZ;
				if (x < 0 || x >= width || z < 0 || z >= depth)
				{
					continue;
				}

				int index = z * width + x;
				if (terrain.WaterMask[index])
				{
					continue;
				}

				candidates[count++] = index;
			}
		}

		if (count == 0)
		{
			int fallbackX = Math.Min(vertexX, width - 1);
			int fallbackZ = Math.Min(vertexZ, depth - 1);
			return fallbackZ * width + fallbackX;
		}

		int best = candidates[0];
		int bestVotes = 0;
		for (int candidateIndex = 0; candidateIndex < count; candidateIndex++)
		{
			int votes = 0;
			for (int otherIndex = 0; otherIndex < count; otherIndex++)
			{
				if (candidates[otherIndex] == candidates[candidateIndex])
				{
					votes++;
				}
			}

			if (votes > bestVotes)
			{
				bestVotes = votes;
				best = candidates[candidateIndex];
			}
		}

		return best;
	}

	private static void AssignPathing(ReconstructedTerrain terrain)
	{
		for (int index = 0; index < terrain.PathingCodes.Length; index++)
		{
			terrain.PathingCodes[index] = (byte)TerrainPathing.AutomaticPathingCode(terrain.WaterMask[index]);
		}
	}

	private static int CellIndexForWorld(GeneratedProp prop, int width, int depth, float quadSize)
	{
		int cellX = Math.Clamp((int)(prop.X / quadSize + width * 0.5f), 0, width - 1);
		int cellZ = Math.Clamp((int)(prop.Z / quadSize + depth * 0.5f), 0, depth - 1);
		return cellZ * width + cellX;
	}

}
