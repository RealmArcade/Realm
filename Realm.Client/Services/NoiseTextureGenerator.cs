using Godot;
using Realm.Shared.Textures;
using System;
using System.IO;
using System.Text.Json.Nodes;

namespace Realm.Client.Services;

public static class NoiseTextureGenerator
{
	public static Image GenerateNoiseImage(JsonObject config, int? overrideWidth = null, int? overrideHeight = null)
	{
		int width = overrideWidth ?? GetIntProperty(config, "width", 512);
		int height = overrideHeight ?? GetIntProperty(config, "height", 512);
		width = Math.Clamp(width, 32, 2048);
		height = Math.Clamp(height, 32, 2048);

		var noise = new FastNoiseLite();
		ApplyNoiseConfig(noise, config);

		bool invert = GetBoolProperty(config, "invert", false);
		bool normalize = GetBoolProperty(config, "normalize", true);
		bool isFlowMap = GetBoolProperty(config, "is_flow_map", false) || GetBoolProperty(config, "flow_map", false);

		if (isFlowMap)
		{
			return GenerateFlowMap(noise, width, height, invert);
		}

		Image baseImage = noise.GetImage(width, height, invert, false, normalize);

		string colorMode = config["color_mode"]?.ToString() ?? "Grayscale";
		if (string.Equals(colorMode, "ColorRamp", StringComparison.OrdinalIgnoreCase))
		{
			return ApplyColorRamp(baseImage, config, width, height);
		}

		return baseImage;
	}

	private static void ApplyNoiseConfig(FastNoiseLite noise, JsonObject config)
	{
		noise.NoiseType = GetEnumProperty(config, "noise_type", FastNoiseLite.NoiseTypeEnum.Perlin);
		noise.Seed = GetIntProperty(config, "seed", 1337);
		noise.Frequency = GetFloatProperty(config, "frequency", 0.015f);
		noise.FractalType = GetEnumProperty(config, "fractal_type", FastNoiseLite.FractalTypeEnum.Fbm);
		noise.FractalOctaves = Math.Clamp(GetIntProperty(config, "fractal_octaves", 5), 1, 10);
		noise.FractalLacunarity = GetFloatProperty(config, "fractal_lacunarity", 2.0f);
		noise.FractalGain = GetFloatProperty(config, "fractal_gain", 0.5f);
		noise.FractalWeightedStrength = GetFloatProperty(config, "fractal_weighted_strength", noise.FractalWeightedStrength);
		noise.CellularDistanceFunction = GetEnumProperty(config, "cellular_distance_function", noise.CellularDistanceFunction);
		noise.CellularReturnType = GetEnumProperty(config, "cellular_return_type", noise.CellularReturnType);
		noise.CellularJitter = GetFloatProperty(config, "cellular_jitter", noise.CellularJitter);
		noise.DomainWarpEnabled = GetBoolProperty(config, "domain_warp_enabled", noise.DomainWarpEnabled);
		noise.DomainWarpType = GetEnumProperty(config, "domain_warp_type", noise.DomainWarpType);
		noise.DomainWarpAmplitude = GetFloatProperty(config, "domain_warp_amplitude", noise.DomainWarpAmplitude);
		noise.DomainWarpFrequency = GetFloatProperty(config, "domain_warp_frequency", noise.DomainWarpFrequency);
		noise.DomainWarpFractalOctaves = Math.Clamp(GetIntProperty(config, "domain_warp_fractal_octaves", noise.DomainWarpFractalOctaves), 1, 10);
		noise.DomainWarpFractalLacunarity = GetFloatProperty(config, "domain_warp_fractal_lacunarity", noise.DomainWarpFractalLacunarity);
		noise.DomainWarpFractalGain = GetFloatProperty(config, "domain_warp_fractal_gain", noise.DomainWarpFractalGain);
	}

	private static Image GenerateFlowMap(FastNoiseLite noise, int width, int height, bool invert)
	{
		var flowImage = Image.CreateEmpty(width, height, false, Image.Format.Rgba8);
		for (int y = 0; y < height; y++)
		{
			for (int x = 0; x < width; x++)
			{
				float dx = (noise.GetNoise2D(x + 1f, y) - noise.GetNoise2D(x - 1f, y)) * 0.5f;
				float dy = (noise.GetNoise2D(x, y + 1f) - noise.GetNoise2D(x, y - 1f)) * 0.5f;
				if (invert)
				{
					dx = -dx;
					dy = -dy;
				}

				float vx = dy;
				float vy = -dx;

				float len = MathF.Sqrt(vx * vx + vy * vy);
				if (len > 0.00001f)
				{
					vx /= len;
					vy /= len;
				}
				else
				{
					vx = 0f;
					vy = 0f;
				}

				float r = Math.Clamp(vx * 0.5f + 0.5f, 0f, 1f);
				float g = Math.Clamp(vy * 0.5f + 0.5f, 0f, 1f);
				flowImage.SetPixel(x, y, new Color(r, g, 1.0f, 1.0f));
			}
		}
		return flowImage;
	}

	private static Image ApplyColorRamp(Image baseImage, JsonObject config, int width, int height)
	{
		Color colorA = Colors.Black;
		Color colorB = Colors.White;

		if (config.TryGetPropertyValue("color_a", out var caNode) && !string.IsNullOrEmpty(caNode?.ToString()))
		{
			colorA = Color.FromHtml(caNode.ToString());
		}

		if (config.TryGetPropertyValue("color_b", out var cbNode) && !string.IsNullOrEmpty(cbNode?.ToString()))
		{
			colorB = Color.FromHtml(cbNode.ToString());
		}

		var coloredImage = Image.CreateEmpty(width, height, false, Image.Format.Rgba8);
		for (int y = 0; y < height; y++)
		{
			for (int x = 0; x < width; x++)
			{
				float gray = baseImage.GetPixel(x, y).R;
				Color blended = colorA.Lerp(colorB, gray);
				coloredImage.SetPixel(x, y, blended);
			}
		}
		return coloredImage;
	}

	private static int GetIntProperty(JsonObject config, string key, int defaultValue)
	{
		if (config.TryGetPropertyValue(key, out var node) && int.TryParse(node?.ToString(), out int value))
			return value;
		return defaultValue;
	}

	private static float GetFloatProperty(JsonObject config, string key, float defaultValue)
	{
		if (config.TryGetPropertyValue(key, out var node) && float.TryParse(node?.ToString(), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float value))
			return value;
		return defaultValue;
	}

	private static bool GetBoolProperty(JsonObject config, string key, bool defaultValue)
	{
		if (config.TryGetPropertyValue(key, out var node) && bool.TryParse(node?.ToString(), out bool value))
			return value;
		return defaultValue;
	}

	private static TEnum GetEnumProperty<TEnum>(JsonObject config, string key, TEnum defaultValue) where TEnum : struct
	{
		if (config.TryGetPropertyValue(key, out var node) && Enum.TryParse<TEnum>(node?.ToString(), true, out var value))
			return value;
		return defaultValue;
	}

	public static string GenerateAndSaveRtex(JsonObject config, string outputRtexPath)
	{
		string targetDir = Path.GetDirectoryName(outputRtexPath);
		if (!string.IsNullOrEmpty(targetDir) && !Directory.Exists(targetDir))
		{
			Directory.CreateDirectory(targetDir);
		}

		Image image = GenerateNoiseImage(config);
		string tempPngPath = Path.Combine(Path.GetTempPath(), $"realm_noise_{Guid.NewGuid():N}.png");
		try
		{
			image.SavePng(tempPngPath);
			var customMeta = new JsonObject
			{
				["FastNoiseLiteParams"] = config.DeepClone()
			};
			var convResult = TextureConverter.ProcessAndSaveSingleLayerTexture(
				tempPngPath,
				outputRtexPath,
				"noise_texture",
				false,
				customMeta.ToJsonString());
			if (!convResult.Success)
			{
				throw new InvalidOperationException($"Failed to encode noise texture to RTEX: {convResult.ErrorMessage}");
			}

			byte[] rtexBytes = File.ReadAllBytes(outputRtexPath);
			return RealmMetadataHelper.ComputeBlake3(rtexBytes, ".rtex");
		}
		finally
		{
			if (File.Exists(tempPngPath))
			{
				try { File.Delete(tempPngPath); } catch { }
			}
		}
	}

	public static void EnsureAllNoiseTexturesGenerated(string workspacePath)
	{
		if (string.IsNullOrEmpty(workspacePath)) return;

		try
		{
			var metadata = Realm.Shared.Services.MapFileService.LoadMetadata(workspacePath);
			if (metadata?.NoiseTextures == null) return;

			string noiseDir = Path.Combine(workspacePath, "Assets", "noise");
			Directory.CreateDirectory(noiseDir);

			foreach (var kvp in metadata.NoiseTextures)
			{
				string fileName = kvp.Key;
				if (!fileName.EndsWith(".rtex", StringComparison.OrdinalIgnoreCase))
				{
					fileName += ".rtex";
				}

				if (kvp.Value == null || string.IsNullOrEmpty(kvp.Value.NoiseConfig))
					continue;

				GenerateSingleNoiseTexture(noiseDir, fileName, kvp.Value.NoiseConfig);
			}
		}
		catch (Exception ex)
		{
			GD.PrintErr($"[NoiseTextureGenerator] EnsureAllNoiseTexturesGenerated error: {ex.Message}");
		}
	}

	private static void GenerateSingleNoiseTexture(string noiseDir, string fileName, string noiseConfig)
	{
		try
		{
			var itemConfig = JsonNode.Parse(noiseConfig) as JsonObject;
			if (itemConfig == null) return;

			string rtexPath = Path.Combine(noiseDir, fileName);
			if (File.Exists(rtexPath)) return;

			GenerateAndSaveRtex(itemConfig, rtexPath);
			GD.Print($"[NoiseTextureGenerator] Idempotently generated procedural noise texture: {fileName}");
		}
		catch (Exception ex)
		{
			GD.PrintErr($"[NoiseTextureGenerator] Failed to generate noise texture {fileName}: {ex.Message}");
		}
	}
}
