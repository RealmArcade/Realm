using System;
using System.Collections.Generic;
using System.IO;

namespace Realm.Shared.Metadata;

public static class ProceduralSwayRegionResolver
{
	public const float AutomaticFoliageMinY = 0.5f;
	public const float AutomaticFoliageFeather = 0.15f;

	public static ProceduralSwayRegionResolution Resolve(ProceduralAnimationConfig? config, string? modelKey)
	{
		if (config == null)
		{
			return new ProceduralSwayRegionResolution();
		}

		ProceduralSwayRegionConfig? declaredRegion = SelectDeclaredRegion(config.SwayRegions, modelKey);
		if (declaredRegion != null)
		{
			return FromDeclaredRegion(declaredRegion);
		}

		if (config.MotionType == ProceduralMotionType.FoliageWind)
		{
			return new ProceduralSwayRegionResolution
			{
				RegionName = "Automatic Foliage Canopy",
				Source = ProceduralSwayRegionSource.MotionTypeFallback,
				NormalizedMin = new(0.0f, AutomaticFoliageMinY, 0.0f),
				NormalizedMax = new(1.0f, 1.0f, 1.0f),
				Feather = AutomaticFoliageFeather,
				MaskPower = 1.0f,
				MaskInvert = false
			};
		}

		return new ProceduralSwayRegionResolution
		{
			RegionName = string.Empty,
			Source = ProceduralSwayRegionSource.PresetMask,
			MaskPower = config.MaskPower,
			MaskInvert = config.MaskInvert
		};
	}

	public static ProceduralSwayRegionConfig? SelectDeclaredRegion(IReadOnlyList<ProceduralSwayRegionConfig>? regions, string? modelKey)
	{
		if (regions == null || regions.Count == 0) return null;

		ProceduralSwayRegionConfig? wildcardRegion = null;
		for (int i = 0; i < regions.Count; i++)
		{
			ProceduralSwayRegionConfig region = regions[i];
			if (region == null) continue;

			if (string.IsNullOrWhiteSpace(region.ModelKey))
			{
				wildcardRegion ??= region;
				continue;
			}

			if (MatchesModelKey(region.ModelKey, modelKey))
			{
				return region;
			}
		}

		return wildcardRegion;
	}

	public static bool MatchesModelKey(string regionModelKey, string? modelKey)
	{
		if (string.IsNullOrWhiteSpace(regionModelKey)) return true;
		if (string.IsNullOrWhiteSpace(modelKey)) return false;

		string regionKey = regionModelKey.Trim();
		string candidate = modelKey.Trim();

		if (string.Equals(regionKey, candidate, StringComparison.OrdinalIgnoreCase)) return true;
		if (string.Equals(Path.GetFileName(regionKey), Path.GetFileName(candidate), StringComparison.OrdinalIgnoreCase)) return true;
		if (string.Equals(Path.GetFileNameWithoutExtension(regionKey), Path.GetFileNameWithoutExtension(candidate), StringComparison.OrdinalIgnoreCase)) return true;

		return candidate.Contains(regionKey, StringComparison.OrdinalIgnoreCase)
		       || regionKey.Contains(candidate, StringComparison.OrdinalIgnoreCase);
	}

	public static bool MatchesAnyPattern(IReadOnlyList<string>? patterns, string? candidate)
	{
		if (patterns == null || patterns.Count == 0) return false;
		if (string.IsNullOrWhiteSpace(candidate)) return false;

		for (int i = 0; i < patterns.Count; i++)
		{
			string pattern = patterns[i];
			if (string.IsNullOrWhiteSpace(pattern)) continue;
			if (candidate.Contains(pattern.Trim(), StringComparison.OrdinalIgnoreCase)) return true;
		}

		return false;
	}

	private static ProceduralSwayRegionResolution FromDeclaredRegion(ProceduralSwayRegionConfig region)
	{
		return new ProceduralSwayRegionResolution
		{
			RegionName = region.RegionName,
			Source = ProceduralSwayRegionSource.DeclaredModelRegion,
			NormalizedMin = region.NormalizedMin,
			NormalizedMax = region.NormalizedMax,
			Feather = region.Feather,
			MaskPower = region.MaskPower,
			MaskInvert = region.MaskInvert,
			MeshNamePatterns = region.MeshNamePatterns ?? new List<string>(),
			MaterialNamePatterns = region.MaterialNamePatterns ?? new List<string>()
		};
	}
}
