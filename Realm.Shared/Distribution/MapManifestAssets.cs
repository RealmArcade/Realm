using System.Text.Json.Serialization;

namespace Realm.Shared.Distribution;

public class MapManifestAssets
{
	[JsonPropertyName("Animation")]
	public Dictionary<string, string>? Animation { get; set; }

	[JsonPropertyName("Building")]
	public Dictionary<string, string>? Building { get; set; }

	[JsonPropertyName("Character")]
	public Dictionary<string, string>? Character { get; set; }

	[JsonPropertyName("Decal")]
	public Dictionary<string, string>? Decal { get; set; }

	[JsonPropertyName("Icon")]
	public Dictionary<string, string>? Icon { get; set; }

	[JsonPropertyName("Item")]
	public Dictionary<string, string>? Item { get; set; }

	[JsonPropertyName("Music")]
	public Dictionary<string, string>? Music { get; set; }

	[JsonPropertyName("Noise")]
	public Dictionary<string, string>? Noise { get; set; }

	[JsonPropertyName("Prop")]
	public Dictionary<string, string>? Prop { get; set; }

	[JsonPropertyName("Ribbon")]
	public Dictionary<string, string>? Ribbon { get; set; }

	[JsonPropertyName("Shader")]
	public Dictionary<string, string>? Shader { get; set; }

	[JsonPropertyName("Skybox")]
	public Dictionary<string, string>? Skybox { get; set; }

	[JsonPropertyName("SoundEffect")]
	public Dictionary<string, string>? SoundEffect { get; set; }

	[JsonPropertyName("Spritesheet")]
	public Dictionary<string, string>? Spritesheet { get; set; }

	[JsonPropertyName("Terrain")]
	public Dictionary<string, string>? Terrain { get; set; }

	[JsonPropertyName("vfx_radial")]
	public Dictionary<string, string>? VfxRadial { get; set; }

	[JsonPropertyName("vfx_vertical")]
	public Dictionary<string, string>? VfxVertical { get; set; }

	[JsonPropertyName("Other")]
	[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	public Dictionary<string, string>? Other { get; set; }

	[JsonExtensionData]
	public Dictionary<string, System.Text.Json.JsonElement>? AdditionalProperties { get; set; }

	[System.Runtime.CompilerServices.IndexerName("CategoryItems")]
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
		{ "Animation", a => a.Animation }, { "animations", a => a.Animation },
		{ "Building", a => a.Building }, { "buildings", a => a.Building },
		{ "Character", a => a.Character }, { "characters", a => a.Character }, { "units", a => a.Character },
		{ "Decal", a => a.Decal }, { "decals", a => a.Decal },
		{ "Icon", a => a.Icon }, { "icons", a => a.Icon },
		{ "Item", a => a.Item }, { "items", a => a.Item }, { "projectiles", a => a.Item }, { "attachments", a => a.Item }, { "weapons", a => a.Item },
		{ "Music", a => a.Music },
		{ "Noise", a => a.Noise }, { "noise_textures", a => a.Noise },
		{ "Other", a => a.Other },
		{ "Prop", a => a.Prop }, { "props", a => a.Prop }, { "resources", a => a.Prop },
		{ "Ribbon", a => a.Ribbon }, { "ribbons", a => a.Ribbon }, { "ribbon_textures", a => a.Ribbon },
		{ "Shader", a => a.Shader }, { "shaders", a => a.Shader },
		{ "Skybox", a => a.Skybox }, { "skyboxes", a => a.Skybox },
		{ "SoundEffect", a => a.SoundEffect }, { "sfx", a => a.SoundEffect }, { "audio", a => a.SoundEffect }, { "sounds", a => a.SoundEffect },
		{ "Spritesheet", a => a.Spritesheet }, { "spritesheets", a => a.Spritesheet }, { "vfx", a => a.Spritesheet }, { "vfx_spritesheets", a => a.Spritesheet },
		{ "Terrain", a => a.Terrain }, { "textures", a => a.Terrain },
		{ "vfx_radial", a => a.VfxRadial },
		{ "vfx_vertical", a => a.VfxVertical }
	};

	private static readonly Dictionary<string, Action<MapManifestAssets, Dictionary<string, string>>> CategorySetters = new(StringComparer.OrdinalIgnoreCase)
	{
		{ "Animation", (a, d) => a.Animation = d },
		{ "Building", (a, d) => a.Building = d },
		{ "Character", (a, d) => a.Character = d },
		{ "Decal", (a, d) => a.Decal = d },
		{ "Icon", (a, d) => a.Icon = d },
		{ "Item", (a, d) => a.Item = d },
		{ "Music", (a, d) => a.Music = d },
		{ "Noise", (a, d) => a.Noise = d },
		{ "Other", (a, d) => a.Other = d },
		{ "Prop", (a, d) => a.Prop = d },
		{ "Ribbon", (a, d) => a.Ribbon = d },
		{ "Shader", (a, d) => a.Shader = d },
		{ "Skybox", (a, d) => a.Skybox = d },
		{ "SoundEffect", (a, d) => a.SoundEffect = d },
		{ "Spritesheet", (a, d) => a.Spritesheet = d },
		{ "Terrain", (a, d) => a.Terrain = d },
		{ "vfx_radial", (a, d) => a.VfxRadial = d },
		{ "vfx_vertical", (a, d) => a.VfxVertical = d }
	};

	private static readonly string[] CategoryProperties = {
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
        
		AdditionalProperties ??= new Dictionary<string, System.Text.Json.JsonElement>(StringComparer.OrdinalIgnoreCase);
		AdditionalProperties[category] = System.Text.Json.JsonSerializer.SerializeToElement(dictionary);
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
		if (AdditionalProperties != null && AdditionalProperties.TryGetValue(category, out var element) && element.ValueKind == System.Text.Json.JsonValueKind.Object)
		{
			var dict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
			foreach (var prop in element.EnumerateObject())
			{
				dict[prop.Name] = prop.Value.ValueKind == System.Text.Json.JsonValueKind.String ? prop.Value.GetString() ?? string.Empty : prop.Value.ToString();
			}
			return dict;
		}
		return null;
	}
}