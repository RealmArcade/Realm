using System;
using System.Collections.Generic;

namespace Realm.MapGeneration;

public static class MapReconstructionMetrics
{
	public static MapAccuracyReport Compare(MapFolderTerrain original, MapFolderTerrain generated, float propMatchRadius)
	{
		var report = new MapAccuracyReport
		{
			MapName = System.IO.Path.GetFileName(System.IO.Path.TrimEndingDirectorySeparator(original.FolderPath)),
			Width = original.Width,
			Depth = original.Depth,
			OriginalPropCount = original.Props.Count,
			GeneratedPropCount = generated.Props.Count
		};

		int width = Math.Min(original.Width, generated.Width);
		int depth = Math.Min(original.Depth, generated.Depth);
		CompareHeights(original, generated, width, depth, report);
		CompareWater(original, generated, width, depth, report);
		CompareProps(original, generated, propMatchRadius, report);
		CompareTextures(original, generated, width, depth, report);
		CompareAssets(generated, report);
		return report;
	}

	private static void CompareHeights(MapFolderTerrain original, MapFolderTerrain generated, int width, int depth, MapAccuracyReport report)
	{
		var originalHeights = new float[width * depth];
		var generatedHeights = new float[width * depth];
		for (int z = 0; z < depth; z++)
		{
			for (int x = 0; x < width; x++)
			{
				int index = z * width + x;
				originalHeights[index] = original.CellHeights.Length > index ? original.CellHeights[index] : 0f;
				generatedHeights[index] = generated.CellHeights.Length > index ? generated.CellHeights[index] : 0f;
			}
		}

		report.HeightCorrelation = Correlation(originalHeights, generatedHeights);
		(float slope, float intercept) = FitAffine(originalHeights, generatedHeights);
		report.HeightRmse = AffineRmse(originalHeights, generatedHeights, slope, intercept);
		report.HeightBandAccuracy = BandAccuracy(original, generated, width, depth);
	}

	private static float BandAccuracy(MapFolderTerrain original, MapFolderTerrain generated, int width, int depth)
	{
		float originalLandLevel = MedianLandLevel(original);
		float generatedLandLevel = MedianLandLevel(generated);
		float tier = 3f;
		int matches = 0;
		for (int z = 0; z < depth; z++)
		{
			for (int x = 0; x < width; x++)
			{
				int index = z * width + x;
				float originalHeight = original.CellHeights.Length > index ? original.CellHeights[index] : 0f;
				float generatedHeight = generated.CellHeights.Length > index ? generated.CellHeights[index] : 0f;
				bool originalWater = original.WaterModes.Length > index && original.WaterModes[index] > 0;
				bool generatedWater = generated.WaterModes.Length > index && generated.WaterModes[index] > 0;
				int originalBand = originalWater ? 0 : Band((originalHeight - originalLandLevel) / tier);
				int generatedBand = generatedWater ? 0 : Band((generatedHeight - generatedLandLevel) / tier);
				if (originalBand == generatedBand)
				{
					matches++;
				}
			}
		}

		return matches / (float)Math.Max(1, width * depth);
	}

	private static int Band(float tierOffset)
	{
		if (tierOffset < -0.5f) return 1;
		if (tierOffset < 0.5f) return 2;
		if (tierOffset < 1.5f) return 3;
		if (tierOffset < 2.5f) return 4;
		return 5;
	}

	private static float MedianLandLevel(MapFolderTerrain terrain)
	{
		var heights = new List<float>();
		for (int index = 0; index < terrain.CellHeights.Length; index++)
		{
			if (terrain.WaterModes.Length > index && terrain.WaterModes[index] > 0)
			{
				continue;
			}

			heights.Add(terrain.CellHeights[index]);
		}

		if (heights.Count == 0)
		{
			return 0f;
		}

		heights.Sort();
		return heights[heights.Count / 2];
	}

	private static float MedianWaterLevel(MapFolderTerrain terrain)
	{
		var heights = new List<float>();
		if (terrain.WaterModes.Length == terrain.WaterSurfaceHeights.Length)
		{
			for (int index = 0; index < terrain.WaterModes.Length; index++)
			{
				if (terrain.WaterModes[index] > 0)
				{
					heights.Add(terrain.WaterSurfaceHeights[index]);
				}
			}
		}

		if (heights.Count == 0)
		{
			return 0f;
		}

		heights.Sort();
		return heights[heights.Count / 2];
	}

	private static void CompareWater(MapFolderTerrain original, MapFolderTerrain generated, int width, int depth, MapAccuracyReport report)
	{
		int intersection = 0;
		int union = 0;
		int generatedWet = 0;
		int originalWet = 0;
		int consistent = 0;
		for (int index = 0; index < width * depth; index++)
		{
			bool originalWater = original.WaterModes.Length > index && original.WaterModes[index] > 0;
			bool generatedWater = generated.WaterModes.Length > index && generated.WaterModes[index] > 0;
			if (originalWater) originalWet++;
			if (generatedWater) generatedWet++;
			if (originalWater && generatedWater) intersection++;
			if (originalWater || generatedWater) union++;
			if (generatedWater)
			{
				float surface = generated.WaterSurfaceHeights.Length > index ? generated.WaterSurfaceHeights[index] : 0f;
				float ground = generated.CellHeights.Length > index ? generated.CellHeights[index] : 0f;
				if (surface > ground)
				{
					consistent++;
				}
			}
		}

		report.WaterIou = union > 0 ? intersection / (float)union : 1f;
		report.WaterPrecision = generatedWet > 0 ? intersection / (float)generatedWet : 1f;
		report.WaterRecall = originalWet > 0 ? intersection / (float)originalWet : 1f;
		report.WaterSurfaceConsistency = generatedWet > 0 ? consistent / (float)generatedWet : 1f;
	}

	private static void CompareProps(MapFolderTerrain original, MapFolderTerrain generated, float matchRadius, MapAccuracyReport report)
	{
		var originalByCategory = GroupProps(original);
		var generatedByCategory = GroupProps(generated);
		var categories = new HashSet<PropCategory>(originalByCategory.Keys);
		foreach (PropCategory category in generatedByCategory.Keys)
		{
			categories.Add(category);
		}

		int totalOriginal = 0;
		int totalGenerated = 0;
		int totalMatched = 0;
		foreach (PropCategory category in categories)
		{
			originalByCategory.TryGetValue(category, out List<MapPlacedProp>? originals);
			generatedByCategory.TryGetValue(category, out List<MapPlacedProp>? generatedList);
			originals ??= new List<MapPlacedProp>();
			generatedList ??= new List<MapPlacedProp>();

			int matched = MatchProps(originals, generatedList, matchRadius);
			report.PropByCategory[category] = new PropCategoryScore
			{
				OriginalCount = originals.Count,
				GeneratedCount = generatedList.Count,
				Matched = matched
			};
			totalOriginal += originals.Count;
			totalGenerated += generatedList.Count;
			totalMatched += matched;
		}

		report.PropPrecision = totalGenerated > 0 ? totalMatched / (float)totalGenerated : 1f;
		report.PropRecall = totalOriginal > 0 ? totalMatched / (float)totalOriginal : 1f;
		report.PropF1 = report.PropPrecision + report.PropRecall > 0f
			? 2f * report.PropPrecision * report.PropRecall / (report.PropPrecision + report.PropRecall)
			: 0f;
		report.PropCountRatio = totalOriginal > 0 ? totalGenerated / (float)totalOriginal : 0f;
	}

	private static Dictionary<PropCategory, List<MapPlacedProp>> GroupProps(MapFolderTerrain terrain)
	{
		var groups = new Dictionary<PropCategory, List<MapPlacedProp>>();
		for (int index = 0; index < terrain.Props.Count; index++)
		{
			MapPlacedProp prop = terrain.Props[index];
			PropCategory category = PropCatalogEntry.Classify(prop.TemplateId, prop.TemplateId);
			if (!groups.TryGetValue(category, out List<MapPlacedProp>? list))
			{
				list = new List<MapPlacedProp>();
				groups[category] = list;
			}

			list.Add(prop);
		}

		return groups;
	}

	private static int MatchProps(List<MapPlacedProp> originals, List<MapPlacedProp> generated, float radius)
	{
		if (originals.Count == 0 || generated.Count == 0)
		{
			return 0;
		}

		var used = new bool[generated.Count];
		float radiusSquared = radius * radius;
		int matched = 0;
		for (int originalIndex = 0; originalIndex < originals.Count; originalIndex++)
		{
			MapPlacedProp original = originals[originalIndex];
			int bestIndex = -1;
			float bestDistance = radiusSquared;
			for (int generatedIndex = 0; generatedIndex < generated.Count; generatedIndex++)
			{
				if (used[generatedIndex]) continue;
				MapPlacedProp candidate = generated[generatedIndex];
				float deltaX = candidate.X - original.X;
				float deltaZ = candidate.Z - original.Z;
				float distance = deltaX * deltaX + deltaZ * deltaZ;
				if (distance < bestDistance)
				{
					bestDistance = distance;
					bestIndex = generatedIndex;
				}
			}

			if (bestIndex >= 0)
			{
				used[bestIndex] = true;
				matched++;
			}
		}

		return matched;
	}

	private static void CompareTextures(MapFolderTerrain original, MapFolderTerrain generated, int width, int depth, MapAccuracyReport report)
	{
		Dictionary<int, TerrainSurfaceKind> originalKinds = SwatchKinds(original);
		Dictionary<int, TerrainSurfaceKind> generatedKinds = SwatchKinds(generated);

		int matches = 0;
		int total = 0;
		var perKindTotal = new Dictionary<TerrainSurfaceKind, int>();
		var perKindMatches = new Dictionary<TerrainSurfaceKind, int>();
		for (int z = 0; z < depth; z++)
		{
			for (int x = 0; x < width; x++)
			{
				TerrainSurfaceKind originalKind = DominantKind(original, originalKinds, x, z);
				TerrainSurfaceKind generatedKind = DominantKind(generated, generatedKinds, x, z);
				total++;
				if (!perKindTotal.TryGetValue(originalKind, out int count))
				{
					count = 0;
				}

				perKindTotal[originalKind] = count + 1;
				if (originalKind == generatedKind)
				{
					matches++;
					perKindMatches.TryGetValue(originalKind, out int matchedCount);
					perKindMatches[originalKind] = matchedCount + 1;
				}
			}
		}

		report.TextureKindAccuracy = total > 0 ? matches / (float)total : 0f;
		foreach (KeyValuePair<TerrainSurfaceKind, int> pair in perKindTotal)
		{
			perKindMatches.TryGetValue(pair.Key, out int matched);
			report.TextureKindAgreement[pair.Key] = pair.Value > 0 ? matched / (float)pair.Value : 0f;
		}
	}

	private static Dictionary<int, TerrainSurfaceKind> SwatchKinds(MapFolderTerrain terrain)
	{
		var kinds = new Dictionary<int, TerrainSurfaceKind>();
		for (int index = 0; index < terrain.Textures.Count; index++)
		{
			MapTextureSource texture = terrain.Textures[index];
			kinds[texture.SwatchIndex] = texture.Kind;
		}

		return kinds;
	}

	private static TerrainSurfaceKind DominantKind(MapFolderTerrain terrain, Dictionary<int, TerrainSurfaceKind> kinds, int x, int z)
	{
		if (terrain.SplatIndices.Length == 0)
		{
			return TerrainSurfaceKind.Unknown;
		}

		int nodeX = Math.Min(x, terrain.Width);
		int nodeZ = Math.Min(z, terrain.Depth);
		int swatchIndex = terrain.SplatIndices[nodeZ * (terrain.Width + 1) + nodeX];
		return kinds.TryGetValue(swatchIndex, out TerrainSurfaceKind kind) ? kind : TerrainSurfaceKind.Unknown;
	}

	private static void CompareAssets(MapFolderTerrain generated, MapAccuracyReport report)
	{
		var distinct = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
		int classified = 0;
		for (int index = 0; index < generated.Props.Count; index++)
		{
			MapPlacedProp prop = generated.Props[index];
			PropCategory category = PropCatalogEntry.Classify(prop.TemplateId, prop.TemplateId);
			if (category != PropCategory.Other)
			{
				classified++;
			}

			if (!string.IsNullOrWhiteSpace(prop.TemplateId))
			{
				distinct.Add(prop.TemplateId);
			}
		}

		report.AssetCategoryCoverage = generated.Props.Count > 0 ? classified / (float)generated.Props.Count : 0f;
		report.GeneratedDistinctAssets = distinct.Count;
	}

	private static float Correlation(float[] left, float[] right)
	{
		int count = Math.Min(left.Length, right.Length);
		double sumLeft = 0;
		double sumRight = 0;
		for (int index = 0; index < count; index++)
		{
			sumLeft += left[index];
			sumRight += right[index];
		}

		double meanLeft = sumLeft / Math.Max(1, count);
		double meanRight = sumRight / Math.Max(1, count);
		double covariance = 0;
		double varianceLeft = 0;
		double varianceRight = 0;
		for (int index = 0; index < count; index++)
		{
			double deltaLeft = left[index] - meanLeft;
			double deltaRight = right[index] - meanRight;
			covariance += deltaLeft * deltaRight;
			varianceLeft += deltaLeft * deltaLeft;
			varianceRight += deltaRight * deltaRight;
		}

		double denominator = Math.Sqrt(varianceLeft * varianceRight);
		return denominator <= 0.000001 ? 0f : (float)(covariance / denominator);
	}

	private static (float Slope, float Intercept) FitAffine(float[] reference, float[] target)
	{
		int count = Math.Min(reference.Length, target.Length);
		double sumReference = 0;
		double sumTarget = 0;
		for (int index = 0; index < count; index++)
		{
			sumReference += reference[index];
			sumTarget += target[index];
		}

		double meanReference = sumReference / Math.Max(1, count);
		double meanTarget = sumTarget / Math.Max(1, count);
		double covariance = 0;
		double variance = 0;
		for (int index = 0; index < count; index++)
		{
			double delta = reference[index] - meanReference;
			covariance += delta * (target[index] - meanTarget);
			variance += delta * delta;
		}

		float slope = variance <= 0.000001 ? 0f : (float)(covariance / variance);
		float intercept = (float)(meanTarget - slope * meanReference);
		return (slope, intercept);
	}

	private static float AffineRmse(float[] reference, float[] target, float slope, float intercept)
	{
		int count = Math.Min(reference.Length, target.Length);
		double sum = 0;
		for (int index = 0; index < count; index++)
		{
			double predicted = slope * reference[index] + intercept;
			double delta = target[index] - predicted;
			sum += delta * delta;
		}

		return (float)Math.Sqrt(sum / Math.Max(1, count));
	}
}
