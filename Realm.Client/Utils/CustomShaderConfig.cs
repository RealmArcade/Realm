using Godot;
using System;
using System.Text.Json.Nodes;

namespace Realm.Client.Utils;

public class CustomShaderConfig
{
	public string Key { get; set; } = "custom_shader";
	public string Name { get; set; } = "Custom Shader";
	public int TransitionMode { get; set; } = 1;
	public int Direction { get; set; } = 0;
	public Color EdgeColor { get; set; } = new Color(1.0f, 0.4f, 0.1f, 1.0f);
	public float EdgeWidth { get; set; } = 0.06f;
	public float EdgeEmission { get; set; } = 5.0f;
	public float NoiseScale { get; set; } = 12.0f;
	public float NoiseRoughness { get; set; } = 0.5f;
	public float FresnelPower { get; set; } = 2.5f;
	public float VertexDisplacement { get; set; } = 0.0f;
	public float AlphaFade { get; set; } = 1.0f;
	public float Duration { get; set; } = 1.2f;
	public string? AssetType { get; set; } = "SpawnShader";

	public CustomShaderConfig Clone()
	{
		return new CustomShaderConfig
		{
			Key = this.Key,
			Name = this.Name,
			TransitionMode = this.TransitionMode,
			Direction = this.Direction,
			EdgeColor = this.EdgeColor,
			EdgeWidth = this.EdgeWidth,
			EdgeEmission = this.EdgeEmission,
			NoiseScale = this.NoiseScale,
			NoiseRoughness = this.NoiseRoughness,
			FresnelPower = this.FresnelPower,
			VertexDisplacement = this.VertexDisplacement,
			AlphaFade = this.AlphaFade,
			Duration = this.Duration,
			AssetType = this.AssetType
		};
	}

	public JsonObject ToJsonObject()
	{
		return new JsonObject
		{
			["name"] = Name,
			["transition_mode"] = TransitionMode,
			["direction"] = Direction,
			["edge_color"] = "#" + EdgeColor.ToHtml(true),
			["edge_width"] = EdgeWidth,
			["edge_emission"] = EdgeEmission,
			["noise_scale"] = NoiseScale,
			["noise_roughness"] = NoiseRoughness,
			["fresnel_power"] = FresnelPower,
			["vertex_displacement"] = VertexDisplacement,
			["alpha_fade"] = AlphaFade,
			["duration"] = Duration,
			["asset_type"] = "SpawnShader"
		};
	}

	private static string ParseString(JsonObject obj, string propName, string defaultVal)
	{
		if (obj.TryGetPropertyValue(propName, out var val) && !string.IsNullOrWhiteSpace(val?.ToString()))
		{
			return val.ToString();
		}
		return defaultVal;
	}

	private static int ParseIntClamp(JsonObject obj, string propName, int defaultVal, int min, int max)
	{
		if (obj.TryGetPropertyValue(propName, out var val) && int.TryParse(val?.ToString(), out int parsed))
		{
			return Math.Clamp(parsed, min, max);
		}
		return defaultVal;
	}

	private static float ParseFloat(JsonObject obj, string propName, float defaultVal)
	{
		if (obj.TryGetPropertyValue(propName, out var val) && float.TryParse(val?.ToString(), out float parsed))
		{
			return parsed;
		}
		return defaultVal;
	}

	private static Color ParseColor(JsonObject obj, string propName, Color defaultVal)
	{
		if (obj.TryGetPropertyValue(propName, out var val) && !string.IsNullOrWhiteSpace(val?.ToString()))
		{
			return Color.FromHtml(val.ToString());
		}
		return defaultVal;
	}

	public static CustomShaderConfig FromJson(string key, JsonNode node)
	{
		var config = new CustomShaderConfig { Key = key, Name = key };
		if (node is not JsonObject obj)
		{
			return config;
		}

		config.Name = ParseString(obj, "name", config.Name);
		config.TransitionMode = ParseIntClamp(obj, "transition_mode", config.TransitionMode, 0, 6);
		config.Direction = ParseIntClamp(obj, "direction", config.Direction, 0, 3);
		config.EdgeColor = ParseColor(obj, "edge_color", config.EdgeColor);
		config.EdgeWidth = ParseFloat(obj, "edge_width", config.EdgeWidth);
		config.EdgeEmission = ParseFloat(obj, "edge_emission", config.EdgeEmission);
		config.NoiseScale = ParseFloat(obj, "noise_scale", config.NoiseScale);
		config.NoiseRoughness = ParseFloat(obj, "noise_roughness", config.NoiseRoughness);
		config.FresnelPower = ParseFloat(obj, "fresnel_power", config.FresnelPower);
		config.VertexDisplacement = ParseFloat(obj, "vertex_displacement", config.VertexDisplacement);
		config.AlphaFade = ParseFloat(obj, "alpha_fade", config.AlphaFade);
		config.Duration = ParseFloat(obj, "duration", config.Duration);

		return config;
	}
}