using Godot;
using System;
using System.IO;
using System.Text.Json.Nodes;
using Realm.Shared.Metadata;
using Realm.Shared.Textures;

namespace Realm.Godot.Services;

public static class NoiseTextureGenerator
{
	public static Image GenerateNoiseImage(JsonObject config, int? overrideWidth = null, int? overrideHeight = null)
	{
		int width = overrideWidth ?? (config.TryGetPropertyValue("width", out var wNode) && int.TryParse(wNode?.ToString(), out int w) ? w : 512);
		int height = overrideHeight ?? (config.TryGetPropertyValue("height", out var hNode) && int.TryParse(hNode?.ToString(), out int h) ? h : 512);
		width = Math.Clamp(width, 32, 2048);
		height = Math.Clamp(height, 32, 2048);

		var noise = new FastNoiseLite();

		if (config.TryGetPropertyValue("noise_type", out var ntNode) && Enum.TryParse<FastNoiseLite.NoiseTypeEnum>(ntNode?.ToString(), true, out var noiseType))
		{
			noise.NoiseType = noiseType;
		}
		else
		{
			noise.NoiseType = FastNoiseLite.NoiseTypeEnum.Perlin;
		}

		if (config.TryGetPropertyValue("seed", out var seedNode) && int.TryParse(seedNode?.ToString(), out int seed))
		{
			noise.Seed = seed;
		}
		else
		{
			noise.Seed = 1337;
		}

		if (config.TryGetPropertyValue("frequency", out var freqNode) && float.TryParse(freqNode?.ToString(), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float freq))
		{
			noise.Frequency = freq;
		}
		else
		{
			noise.Frequency = 0.015f;
		}

		if (config.TryGetPropertyValue("fractal_type", out var ftNode) && Enum.TryParse<FastNoiseLite.FractalTypeEnum>(ftNode?.ToString(), true, out var fractalType))
		{
			noise.FractalType = fractalType;
		}
		else
		{
			noise.FractalType = FastNoiseLite.FractalTypeEnum.Fbm;
		}

		if (config.TryGetPropertyValue("fractal_octaves", out var octNode) && int.TryParse(octNode?.ToString(), out int octaves))
		{
			noise.FractalOctaves = Math.Clamp(octaves, 1, 10);
		}
		else
		{
			noise.FractalOctaves = 5;
		}

		if (config.TryGetPropertyValue("fractal_lacunarity", out var lacNode) && float.TryParse(lacNode?.ToString(), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float lac))
		{
			noise.FractalLacunarity = lac;
		}
		else
		{
			noise.FractalLacunarity = 2.0f;
		}

		if (config.TryGetPropertyValue("fractal_gain", out var gainNode) && float.TryParse(gainNode?.ToString(), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float gain))
		{
			noise.FractalGain = gain;
		}
		else
		{
			noise.FractalGain = 0.5f;
		}

		if (config.TryGetPropertyValue("fractal_weighted_strength", out var wsNode) && float.TryParse(wsNode?.ToString(), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float ws))
		{
			noise.FractalWeightedStrength = ws;
		}

		if (config.TryGetPropertyValue("cellular_distance_function", out var cdfNode) && Enum.TryParse<FastNoiseLite.CellularDistanceFunctionEnum>(cdfNode?.ToString(), true, out var cdf))
		{
			noise.CellularDistanceFunction = cdf;
		}

		if (config.TryGetPropertyValue("cellular_return_type", out var crtNode) && Enum.TryParse<FastNoiseLite.CellularReturnTypeEnum>(crtNode?.ToString(), true, out var crt))
		{
			noise.CellularReturnType = crt;
		}

		if (config.TryGetPropertyValue("cellular_jitter", out var cjNode) && float.TryParse(cjNode?.ToString(), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float cj))
		{
			noise.CellularJitter = cj;
		}

		if (config.TryGetPropertyValue("domain_warp_enabled", out var dweNode) && bool.TryParse(dweNode?.ToString(), out bool dwe))
		{
			noise.DomainWarpEnabled = dwe;
		}

		if (config.TryGetPropertyValue("domain_warp_type", out var dwtNode) && Enum.TryParse<FastNoiseLite.DomainWarpTypeEnum>(dwtNode?.ToString(), true, out var dwt))
		{
			noise.DomainWarpType = dwt;
		}

		if (config.TryGetPropertyValue("domain_warp_amplitude", out var dwaNode) && float.TryParse(dwaNode?.ToString(), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float dwa))
		{
			noise.DomainWarpAmplitude = dwa;
		}

		if (config.TryGetPropertyValue("domain_warp_frequency", out var dwfNode) && float.TryParse(dwfNode?.ToString(), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float dwf))
		{
			noise.DomainWarpFrequency = dwf;
		}

		if (config.TryGetPropertyValue("domain_warp_fractal_octaves", out var dwoNode) && int.TryParse(dwoNode?.ToString(), out int dwo))
		{
			noise.DomainWarpFractalOctaves = Math.Clamp(dwo, 1, 10);
		}

		if (config.TryGetPropertyValue("domain_warp_fractal_lacunarity", out var dwlNode) && float.TryParse(dwlNode?.ToString(), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float dwl))
		{
			noise.DomainWarpFractalLacunarity = dwl;
		}

		if (config.TryGetPropertyValue("domain_warp_fractal_gain", out var dwgNode) && float.TryParse(dwgNode?.ToString(), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float dwg))
		{
			noise.DomainWarpFractalGain = dwg;
		}

		bool invert = config.TryGetPropertyValue("invert", out var invNode) && bool.TryParse(invNode?.ToString(), out bool inv) && inv;
		bool normalize = !config.TryGetPropertyValue("normalize", out var normNode) || !bool.TryParse(normNode?.ToString(), out bool nrm) || nrm;
		bool isFlowMap = (config.TryGetPropertyValue("is_flow_map", out var flowNode) && bool.TryParse(flowNode?.ToString(), out bool fm1) && fm1)
			|| (config.TryGetPropertyValue("flow_map", out var fmNode) && bool.TryParse(fmNode?.ToString(), out bool fm2) && fm2);

		if (isFlowMap)
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

		Image baseImage = noise.GetImage(width, height, invert, false, normalize);

		string colorMode = config["color_mode"]?.ToString() ?? "Grayscale";
		if (string.Equals(colorMode, "ColorRamp", StringComparison.OrdinalIgnoreCase))
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

		return baseImage;
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
			var assetsObj = Realm.Godot.Utils.MapAssetHelper.LoadUnionedAssets(workspacePath);
			if (assetsObj == null || !assetsObj.ContainsKey("noise_textures") || assetsObj["noise_textures"] is not JsonObject noiseObj)
			{
				return;
			}

			string noiseDir = Path.Combine(workspacePath, "Assets", "noise");
			Directory.CreateDirectory(noiseDir);

			bool manifestModified = false;

			foreach (var kvp in noiseObj)
			{
				string fileName = kvp.Key;
				if (!fileName.EndsWith(".rtex", StringComparison.OrdinalIgnoreCase))
				{
					fileName += ".rtex";
				}

				if (kvp.Value is JsonObject itemConfig)
				{
					bool isProcedural = string.Equals(itemConfig["generator"]?.ToString(), "FastNoiseLite", StringComparison.OrdinalIgnoreCase)
						|| itemConfig.ContainsKey("noise_type");

					if (isProcedural)
					{
						string rtexPath = Path.Combine(noiseDir, fileName);
						if (!File.Exists(rtexPath))
						{
							try
							{
								string hash = GenerateAndSaveRtex(itemConfig, rtexPath);
								itemConfig["hash"] = hash;
								itemConfig["generator"] = "FastNoiseLite";
								manifestModified = true;
								GD.Print($"[NoiseTextureGenerator] Idempotently generated procedural noise texture: {fileName}");
							}
							catch (Exception ex)
							{
								GD.PrintErr($"[NoiseTextureGenerator] Failed to generate noise texture {fileName}: {ex.Message}");
							}
						}
					}
				}
			}

			if (manifestModified)
			{
				Realm.Godot.Utils.MapAssetHelper.SaveAssetsToManifest(workspacePath, assetsObj, removeFromMetadata: true);
			}
		}
		catch (Exception ex)
		{
			GD.PrintErr($"[NoiseTextureGenerator] EnsureAllNoiseTexturesGenerated error: {ex.Message}");
		}
	}
}
