using Realm.EditorAPI;
using Realm.Shared.Metadata;
using Realm.Shared.Serialization;
using System.Runtime.CompilerServices;
using System.Text.Json.Serialization;

namespace Realm.Shared.Distribution;

[SchemaDescription("Every asset a map ships, grouped by kind. Each category maps a file name or map-relative path to the content hash the file is stored under.")]
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public class MapManifestAssets
{
	[JsonPropertyName("Animation")]
	[SchemaDescription("The map's animation files, keyed by file name and hashed by content.")]
	public Dictionary<string, string>? Animation { get; set; }

	[JsonPropertyName("Building")]
	[SchemaDescription("The map's building models, keyed by file name and hashed by content.")]
	public Dictionary<string, string>? Building { get; set; }

	[JsonPropertyName("Character")]
	[SchemaDescription("The map's unit and character models, keyed by file name and hashed by content.")]
	public Dictionary<string, string>? Character { get; set; }

	[JsonPropertyName("Decal")]
	[SchemaDescription("The map's decal textures, keyed by file name and hashed by content.")]
	public Dictionary<string, string>? Decal { get; set; }

	[JsonPropertyName("Icon")]
	[SchemaDescription("The map's command card icons, keyed by file name and hashed by content.")]
	public Dictionary<string, string>? Icon { get; set; }

	[JsonPropertyName("Item")]
	[SchemaDescription("The map's item, attachment and projectile models, keyed by file name and hashed by content.")]
	public Dictionary<string, string>? Item { get; set; }

	[JsonPropertyName("Music")]
	[SchemaDescription("The map's music tracks, keyed by their path inside the map's audio folder and hashed by content.")]
	public Dictionary<string, string>? Music { get; set; }

	[JsonPropertyName("Noise")]
	[SchemaDescription("The map's noise textures, keyed by file name and hashed by content.")]
	public Dictionary<string, string>? Noise { get; set; }

	[JsonPropertyName("Prop")]
	[SchemaDescription("The map's prop and resource models, keyed by file name and hashed by content.")]
	public Dictionary<string, string>? Prop { get; set; }

	[JsonPropertyName("Ribbon")]
	[SchemaDescription("The map's ribbon trail textures, keyed by file name and hashed by content.")]
	public Dictionary<string, string>? Ribbon { get; set; }

	[JsonPropertyName("Shader")]
	[SchemaDescription("The map's shader configurations, keyed by file name and hashed by content.")]
	public Dictionary<string, string>? Shader { get; set; }

	[JsonPropertyName("Skybox")]
	[SchemaDescription("The map's skyboxes, keyed by file name and hashed by content.")]
	public Dictionary<string, string>? Skybox { get; set; }

	[JsonPropertyName("SoundEffect")]
	[SchemaDescription("The map's sound effects, keyed by their path inside the map's audio folder and hashed by content.")]
	public Dictionary<string, string>? SoundEffect { get; set; }

	[JsonPropertyName("Spritesheet")]
	[SchemaDescription("The map's visual effect sprite sheets, keyed by file name and hashed by content.")]
	public Dictionary<string, string>? Spritesheet { get; set; }

	[JsonPropertyName("Terrain")]
	[SchemaDescription("The map's terrain textures, keyed by file name and hashed by content.")]
	public Dictionary<string, string>? Terrain { get; set; }

	[JsonPropertyName("vfx_radial")]
	[SchemaDescription("The map's radial visual effect textures, keyed by file name and hashed by content.")]
	public Dictionary<string, string>? VfxRadial { get; set; }

	[JsonPropertyName("vfx_vertical")]
	[SchemaDescription("The map's vertical visual effect textures, keyed by file name and hashed by content.")]
	public Dictionary<string, string>? VfxVertical { get; set; }

	[JsonPropertyName("Other")]
	[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	[SchemaDescription("The files that belong to no asset category, keyed by their map-relative path and hashed by content.")]
	public Dictionary<string, string>? Other { get; set; }

	[IndexerName("CategoryItems")]
	public Dictionary<string, string>? this[string category]
	{
		get => GetCategory(category);
		set
		{
			if (value != null) SetCategory(category, value);
		}
	}

	public bool ContainsCategory(string category)
	{
		return GetCategory(category) != null;
	}

	private static readonly Dictionary<string, Func<MapManifestAssets, Dictionary<string, string>?>> CategoryGetters = new(StringComparer.OrdinalIgnoreCase)
	{
		{ AssetType.Animation.ToCanonicalString(), a => a.Animation }, { "animations", a => a.Animation },
		{ AssetType.Building.ToCanonicalString(), a => a.Building }, { "buildings", a => a.Building },
		{ AssetType.Character.ToCanonicalString(), a => a.Character }, { "characters", a => a.Character }, { "units", a => a.Character },
		{ AssetType.Decal.ToCanonicalString(), a => a.Decal }, { "decals", a => a.Decal },
		{ AssetType.Icon.ToCanonicalString(), a => a.Icon }, { "icons", a => a.Icon },
		{ AssetType.Item.ToCanonicalString(), a => a.Item }, { "items", a => a.Item }, { "projectiles", a => a.Item }, { "attachments", a => a.Item }, { "weapons", a => a.Item },
		{ AssetType.Music.ToCanonicalString(), a => a.Music },
		{ AssetType.Noise.ToCanonicalString(), a => a.Noise }, { "noise_textures", a => a.Noise },
		{ "Other", a => a.Other },
		{ AssetType.Prop.ToCanonicalString(), a => a.Prop }, { "props", a => a.Prop }, { "resources", a => a.Prop },
		{ AssetType.Ribbon.ToCanonicalString(), a => a.Ribbon }, { "ribbons", a => a.Ribbon }, { "ribbon_textures", a => a.Ribbon },
		{ AssetType.Shader.ToCanonicalString(), a => a.Shader }, { "shaders", a => a.Shader },
		{ AssetType.Skybox.ToCanonicalString(), a => a.Skybox }, { "skyboxes", a => a.Skybox },
		{ AssetType.SoundEffect.ToCanonicalString(), a => a.SoundEffect }, { "sfx", a => a.SoundEffect }, { "audio", a => a.SoundEffect }, { "sounds", a => a.SoundEffect },
		{ AssetType.Spritesheet.ToCanonicalString(), a => a.Spritesheet }, { "spritesheets", a => a.Spritesheet }, { "vfx", a => a.Spritesheet }, { "vfx_spritesheets", a => a.Spritesheet },
		{ AssetType.Terrain.ToCanonicalString(), a => a.Terrain }, { "textures", a => a.Terrain },
		{ AssetType.VfxRadial.ToCanonicalString(), a => a.VfxRadial },
		{ AssetType.VfxVertical.ToCanonicalString(), a => a.VfxVertical }
	};

	private static readonly Dictionary<string, Action<MapManifestAssets, Dictionary<string, string>>> CategorySetters = new(StringComparer.OrdinalIgnoreCase)
	{
		{ AssetType.Animation.ToCanonicalString(), (a, d) => a.Animation = d },
		{ AssetType.Building.ToCanonicalString(), (a, d) => a.Building = d },
		{ AssetType.Character.ToCanonicalString(), (a, d) => a.Character = d },
		{ AssetType.Decal.ToCanonicalString(), (a, d) => a.Decal = d },
		{ AssetType.Icon.ToCanonicalString(), (a, d) => a.Icon = d },
		{ AssetType.Item.ToCanonicalString(), (a, d) => a.Item = d },
		{ AssetType.Music.ToCanonicalString(), (a, d) => a.Music = d },
		{ AssetType.Noise.ToCanonicalString(), (a, d) => a.Noise = d },
		{ "Other", (a, d) => a.Other = d },
		{ AssetType.Prop.ToCanonicalString(), (a, d) => a.Prop = d },
		{ AssetType.Ribbon.ToCanonicalString(), (a, d) => a.Ribbon = d },
		{ AssetType.Shader.ToCanonicalString(), (a, d) => a.Shader = d },
		{ AssetType.Skybox.ToCanonicalString(), (a, d) => a.Skybox = d },
		{ AssetType.SoundEffect.ToCanonicalString(), (a, d) => a.SoundEffect = d },
		{ AssetType.Spritesheet.ToCanonicalString(), (a, d) => a.Spritesheet = d },
		{ AssetType.Terrain.ToCanonicalString(), (a, d) => a.Terrain = d },
		{ AssetType.VfxRadial.ToCanonicalString(), (a, d) => a.VfxRadial = d },
		{ AssetType.VfxVertical.ToCanonicalString(), (a, d) => a.VfxVertical = d }
	};

	private static readonly string[] CategoryProperties =
	{
		AssetType.Animation.ToCanonicalString(),
		AssetType.Building.ToCanonicalString(),
		AssetType.Character.ToCanonicalString(),
		AssetType.Decal.ToCanonicalString(),
		AssetType.Icon.ToCanonicalString(),
		AssetType.Item.ToCanonicalString(),
		AssetType.Music.ToCanonicalString(),
		AssetType.Noise.ToCanonicalString(),
		"other",
		AssetType.Prop.ToCanonicalString(),
		AssetType.Ribbon.ToCanonicalString(),
		AssetType.Shader.ToCanonicalString(),
		AssetType.Skybox.ToCanonicalString(),
		AssetType.SoundEffect.ToCanonicalString(),
		AssetType.Spritesheet.ToCanonicalString(),
		AssetType.Terrain.ToCanonicalString(),
		AssetType.VfxRadial.ToCanonicalString(),
		AssetType.VfxVertical.ToCanonicalString()
	};

	public Dictionary<string, string>? GetCategory(string category)
	{
		return CategoryGetters.TryGetValue(category, out var getter) ? getter(this) : null;
	}

	public void SetCategory(string category, Dictionary<string, string> dictionary)
	{
		if (!CategorySetters.TryGetValue(category, out var setter))
		{
			throw new ArgumentException($"'{category}' is not an asset category the manifest can hold.", nameof(category));
		}

		setter(this, dictionary);
	}

	public IEnumerable<KeyValuePair<string, Dictionary<string, string>>> GetAllCategories()
	{
		foreach (var prop in CategoryProperties)
		{
			var dict = CategoryGetters[prop](this);
			if (dict != null) yield return new(prop, dict);
		}
	}

	public void MergeCategoryInto(string category, System.Text.Json.Nodes.JsonObject sourceObject)
	{
		var categoryTarget = GetCategory(category);
		if (categoryTarget == null)
		{
			categoryTarget = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
			SetCategory(category, categoryTarget);
		}

		foreach (var pair in sourceObject)
		{
			if (pair.Value is System.Text.Json.Nodes.JsonObject sourceObj)
			{
				string hash = sourceObj["hash"]?.ToString() ?? sourceObj[AssetMetadataKeys.Blake3Hash]?.ToString() ?? string.Empty;
				if (!string.IsNullOrEmpty(hash))
				{
					categoryTarget[pair.Key] = hash;
				}
			}
			else if (pair.Value != null)
			{
				categoryTarget[pair.Key] = pair.Value.ToString();
			}
		}
	}
}