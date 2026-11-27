using System;
using System.Collections.Generic;

namespace Realm.MapGeneration;

public static class TerrainSurfaceClassifier
{
	private static readonly string[] SandKeywords = { "sand", "silt", "scree", "dune", "beach", "shore", "dust", "salt" };
	private static readonly string[] SoilKeywords = { "soil", "dirt", "mud", "clay", "path", "earth", "ground", "loam", "gravel", "crumb" };
	private static readonly string[] GrassKeywords = { "grass", "moss", "fern", "jungle", "meadow", "foliage", "leaf", "tundra", "verdant", "sod" };
	private static readonly string[] RockKeywords = { "rock", "stone", "granite", "basalt", "cliff", "crag", "slate", "scab", "tuff", "crushed", "boulder", "cobble", "ore", "crystal", "trench", "abyssal", "reef", "hydro", "submerged" };
	private static readonly string[] SnowKeywords = { "snow", "ice", "frost", "glacier", "frozen", "rime" };

	public static TerrainSurfaceKind Classify(string? name)
	{
		if (string.IsNullOrWhiteSpace(name))
		{
			return TerrainSurfaceKind.Unknown;
		}

		string lowered = name.ToLowerInvariant();
		if (ContainsAny(lowered, SnowKeywords)) return TerrainSurfaceKind.Snow;
		if (ContainsAny(lowered, SandKeywords)) return TerrainSurfaceKind.Sand;
		if (ContainsAny(lowered, RockKeywords)) return TerrainSurfaceKind.Rock;
		if (ContainsAny(lowered, SoilKeywords)) return TerrainSurfaceKind.Soil;
		if (ContainsAny(lowered, GrassKeywords)) return TerrainSurfaceKind.Grass;
		return TerrainSurfaceKind.Unknown;
	}

	public static TerrainSurfaceKind ClassifyColor(float red, float green, float blue)
	{
		float maximum = MathF.Max(red, MathF.Max(green, blue));
		float minimum = MathF.Min(red, MathF.Min(green, blue));
		float saturation = maximum <= 0.0001f ? 0f : (maximum - minimum) / maximum;

		if (blue > red * 1.12f && blue > green * 0.98f && saturation > 0.12f)
		{
			return TerrainSurfaceKind.Water;
		}

		if (maximum > 0.82f && saturation < 0.18f)
		{
			return TerrainSurfaceKind.Snow;
		}

		if (green > red * 1.04f && green >= blue * 0.98f)
		{
			return maximum < 0.42f ? TerrainSurfaceKind.Forest : TerrainSurfaceKind.Grass;
		}

		if (saturation < 0.14f)
		{
			return TerrainSurfaceKind.Rock;
		}

		if (red >= green && green >= blue)
		{
			if (maximum > 0.62f && saturation < 0.42f) return TerrainSurfaceKind.Sand;
			if (maximum < 0.42f) return TerrainSurfaceKind.Soil;
			return TerrainSurfaceKind.Soil;
		}

		return TerrainSurfaceKind.Rock;
	}

	private static bool ContainsAny(string value, IReadOnlyList<string> keywords)
	{
		for (int index = 0; index < keywords.Count; index++)
		{
			if (value.Contains(keywords[index], StringComparison.Ordinal))
			{
				return true;
			}
		}

		return false;
	}
}
