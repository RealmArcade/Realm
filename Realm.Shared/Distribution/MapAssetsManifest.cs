using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace Realm.Shared.Distribution;

public class MapAssetsManifest
{
	[JsonPropertyName("Animation")]
	public Dictionary<string, string> Animation { get; set; } = new(StringComparer.OrdinalIgnoreCase);

	[JsonPropertyName("Building")]
	public Dictionary<string, string> Building { get; set; } = new(StringComparer.OrdinalIgnoreCase);

	[JsonPropertyName("Character")]
	public Dictionary<string, string> Character { get; set; } = new(StringComparer.OrdinalIgnoreCase);

	[JsonPropertyName("Decal")]
	public Dictionary<string, string> Decal { get; set; } = new(StringComparer.OrdinalIgnoreCase);

	[JsonPropertyName("Icon")]
	public Dictionary<string, string> Icon { get; set; } = new(StringComparer.OrdinalIgnoreCase);

	[JsonPropertyName("Item")]
	public Dictionary<string, string> Item { get; set; } = new(StringComparer.OrdinalIgnoreCase);

	[JsonPropertyName("Music")]
	public Dictionary<string, string> Music { get; set; } = new(StringComparer.OrdinalIgnoreCase);

	[JsonPropertyName("Noise")]
	public Dictionary<string, string> Noise { get; set; } = new(StringComparer.OrdinalIgnoreCase);

	[JsonPropertyName("Prop")]
	public Dictionary<string, string> Prop { get; set; } = new(StringComparer.OrdinalIgnoreCase);

	[JsonPropertyName("Ribbon")]
	public Dictionary<string, string> Ribbon { get; set; } = new(StringComparer.OrdinalIgnoreCase);

	[JsonPropertyName("Shader")]
	public Dictionary<string, string> Shader { get; set; } = new(StringComparer.OrdinalIgnoreCase);

	[JsonPropertyName("Skybox")]
	public Dictionary<string, string> Skybox { get; set; } = new(StringComparer.OrdinalIgnoreCase);

	[JsonPropertyName("SoundEffect")]
	public Dictionary<string, string> SoundEffect { get; set; } = new(StringComparer.OrdinalIgnoreCase);

	[JsonPropertyName("Spritesheet")]
	public Dictionary<string, string> Spritesheet { get; set; } = new(StringComparer.OrdinalIgnoreCase);

	[JsonPropertyName("Terrain")]
	public Dictionary<string, string> Terrain { get; set; } = new(StringComparer.OrdinalIgnoreCase);

	[JsonPropertyName("vfx_radial")]
	public Dictionary<string, string> VfxRadial { get; set; } = new(StringComparer.OrdinalIgnoreCase);

	[JsonPropertyName("vfx_vertical")]
	public Dictionary<string, string> VfxVertical { get; set; } = new(StringComparer.OrdinalIgnoreCase);

	[JsonExtensionData]
	public Dictionary<string, JsonElement>? ExtensionData { get; set; }

	public Dictionary<string, string> GetCategory(string category)
	{
		return category switch
		{
			"Animation" => Animation,
			"Building" => Building,
			"Character" => Character,
			"Decal" => Decal,
			"Icon" => Icon,
			"Item" => Item,
			"Music" => Music,
			"Noise" => Noise,
			"Prop" => Prop,
			"Ribbon" => Ribbon,
			"Shader" => Shader,
			"Skybox" => Skybox,
			"SoundEffect" => SoundEffect,
			"Spritesheet" => Spritesheet,
			"Terrain" => Terrain,
			"vfx_radial" => VfxRadial,
			"vfx_vertical" => VfxVertical,
			_ => GetOrAddExtensionCategory(category)
		};
	}

	private Dictionary<string, string> GetOrAddExtensionCategory(string category)
	{
		ExtensionData ??= new Dictionary<string, JsonElement>(StringComparer.OrdinalIgnoreCase);
		if (ExtensionData.TryGetValue(category, out var element) && element.ValueKind == JsonValueKind.Object)
		{
			var dict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
			foreach (var prop in element.EnumerateObject())
			{
				dict[prop.Name] = prop.Value.ToString();
			}
			return dict;
		}
		return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
	}

	public JsonObject ToJsonObject()
	{
		var node = JsonSerializer.SerializeToNode(this) as JsonObject;
		return node ?? new JsonObject();
	}

	public static MapAssetsManifest FromJsonObject(JsonObject? obj)
	{
		if (obj == null) return new MapAssetsManifest();
		string json = obj.ToJsonString();
		return JsonSerializer.Deserialize<MapAssetsManifest>(json) ?? new MapAssetsManifest();
	}
}
