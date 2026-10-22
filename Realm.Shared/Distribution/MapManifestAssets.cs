using Realm.EditorAPI;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Realm.Shared.Distribution;

public class MapManifestAssets
{
	[JsonPropertyName("Animation")] public Dictionary<string, string>? Animation { get; set; }

	[JsonPropertyName("Building")] public Dictionary<string, string>? Building { get; set; }

	[JsonPropertyName("Character")] public Dictionary<string, string>? Character { get; set; }

	[JsonPropertyName("Decal")] public Dictionary<string, string>? Decal { get; set; }

	[JsonPropertyName("Icon")] public Dictionary<string, string>? Icon { get; set; }

	[JsonPropertyName("Item")] public Dictionary<string, string>? Item { get; set; }

	[JsonPropertyName("Music")] public Dictionary<string, string>? Music { get; set; }

	[JsonPropertyName("Noise")] public Dictionary<string, string>? Noise { get; set; }

	[JsonPropertyName("Prop")] public Dictionary<string, string>? Prop { get; set; }

	[JsonPropertyName("Ribbon")] public Dictionary<string, string>? Ribbon { get; set; }

	[JsonPropertyName("Shader")] public Dictionary<string, string>? Shader { get; set; }

	[JsonPropertyName("Skybox")] public Dictionary<string, string>? Skybox { get; set; }

	[JsonPropertyName("SoundEffect")] public Dictionary<string, string>? SoundEffect { get; set; }

	[JsonPropertyName("Spritesheet")] public Dictionary<string, string>? Spritesheet { get; set; }

	[JsonPropertyName("Terrain")] public Dictionary<string, string>? Terrain { get; set; }

	[JsonPropertyName("vfx_radial")] public Dictionary<string, string>? VfxRadial { get; set; }

	[JsonPropertyName("vfx_vertical")] public Dictionary<string, string>? VfxVertical { get; set; }

	[JsonPropertyName("Other")]
	[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	public Dictionary<string, string>? Other { get; set; }

	[JsonExtensionData] public Dictionary<string, JsonElement>? AdditionalProperties { get; set; }

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
		"Animation", "Building", "Character", "Decal", "Icon", "Item", "Music", "Noise", "other",
		"Prop", "Ribbon", "Shader", "Skybox", "SoundEffect", "Spritesheet", "Terrain", "vfx_radial", "vfx_vertical"
	};

	public Dictionary<string, string>? GetCategory(string category)
	{
		if (CategoryGetters.TryGetValue(category, out var getter))
		{
			return getter(this);
		}

		return GetFromAdditionalProperties(category);
	}

	public void SetCategory(string category, Dictionary<string, string> dictionary)
	{
		if (CategorySetters.TryGetValue(category, out var setter))
		{
			setter(this, dictionary);
			return;
		}

		AdditionalProperties ??= new Dictionary<string, JsonElement>(StringComparer.OrdinalIgnoreCase);
		AdditionalProperties[category] = JsonSerializer.SerializeToElement(dictionary);
	}

	public IEnumerable<KeyValuePair<string, Dictionary<string, string>>> GetAllCategories()
	{
		foreach (var prop in CategoryProperties)
		{
			var dict = CategoryGetters[prop](this);
			if (dict != null) yield return new(prop, dict);
		}

		if (AdditionalProperties != null)
		{
			foreach (var kvp in AdditionalProperties)
			{
				var dict = GetFromAdditionalProperties(kvp.Key);
				if (dict != null) yield return new(kvp.Key, dict);
			}
		}
	}

	private Dictionary<string, string>? GetFromAdditionalProperties(string category)
	{
		if (AdditionalProperties != null && AdditionalProperties.TryGetValue(category, out var element) && element.ValueKind == JsonValueKind.Object)
		{
			var dict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
			foreach (var prop in element.EnumerateObject())
			{
				dict[prop.Name] = prop.Value.ValueKind == JsonValueKind.String ? prop.Value.GetString() ?? string.Empty : prop.Value.ToString();
			}

			return dict;
		}

		return null;
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
				string hash = sourceObj["hash"]?.ToString() ?? sourceObj["blake3"]?.ToString() ?? string.Empty;
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