using Godot;

namespace Realm.Client.UI;

public static class MapTagCatalog
{
	public const string AssetPackTag = "AssetPack";

	public static readonly string[] MapFilters =
	{
		"Campaign",
		"Melee",
		"Tutorial",
		"Arcade",
		"Tower Defense",
		"Survival",
		AssetPackTag
	};

	public static readonly string[] AssetPackCategories =
	{
		"3D Models",
		"Audio",
		"Code Scripts",
		"Terrain",
		"UI Components"
	};

	public static string GetDisplayName(string tag)
	{
		return tag switch
		{
			"Campaign" => TranslationServer.Translate("Campaign"),
			"Melee" => TranslationServer.Translate("Melee"),
			"Tutorial" => TranslationServer.Translate("Tutorial"),
			"Arcade" => TranslationServer.Translate("Arcade"),
			"Tower Defense" => TranslationServer.Translate("Tower Defense"),
			"Survival" => TranslationServer.Translate("Survival"),
			"AssetPack" => TranslationServer.Translate("Asset Packs"),
			_ => tag
		};
	}
}
