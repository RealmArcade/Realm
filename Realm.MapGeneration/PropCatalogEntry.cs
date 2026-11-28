using System;

namespace Realm.MapGeneration;

public readonly record struct PropCatalogEntry(string TemplateId, string Name, PropCategory Category, float Scale)
{
	public static PropCategory Classify(string? templateId, string? name)
	{
		string text = $"{templateId} {name}".ToLowerInvariant();
		if (string.IsNullOrWhiteSpace(text.Trim()))
		{
			return PropCategory.Other;
		}

		if (Contains(text, "tree") || Contains(text, "conifer") || Contains(text, "palm") || Contains(text, "pine") ||
		    Contains(text, "oak") || Contains(text, "bush") || Contains(text, "shrub") || Contains(text, "sapling") ||
		    Contains(text, "canopy"))
		{
			return PropCategory.Tree;
		}

		if (Contains(text, "leaf") || Contains(text, "fern") || Contains(text, "flower") || Contains(text, "mushroom") ||
		    Contains(text, "plant") || Contains(text, "cactus") || Contains(text, "grass") || Contains(text, "vine") ||
		    Contains(text, "root") || Contains(text, "bamboo") || Contains(text, "reed"))
		{
			return PropCategory.Nature;
		}

		if (Contains(text, "rock") || Contains(text, "boulder") || Contains(text, "stone") || Contains(text, "crystal") ||
		    Contains(text, "ore") || Contains(text, "geode") || Contains(text, "pebble") || Contains(text, "rubble") ||
		    Contains(text, "skull"))
		{
			return PropCategory.Rock;
		}

		if (Contains(text, "column") || Contains(text, "pillar") || Contains(text, "statue") || Contains(text, "wall") ||
		    Contains(text, "gate") || Contains(text, "tower") || Contains(text, "altar") || Contains(text, "shrine") ||
		    Contains(text, "cauldron") || Contains(text, "stake") || Contains(text, "fence") || Contains(text, "cart") ||
		    Contains(text, "barrel") || Contains(text, "crate") || Contains(text, "ruin") || Contains(text, "anvil") ||
		    Contains(text, "lantern") || Contains(text, "torch") || Contains(text, "gravestone") || Contains(text, "banner") ||
		    Contains(text, "tent") || Contains(text, "bridge"))
		{
			return PropCategory.Structure;
		}

		return PropCategory.Other;
	}

	private static bool Contains(string text, string keyword)
	{
		return text.Contains(keyword, StringComparison.Ordinal);
	}
}
