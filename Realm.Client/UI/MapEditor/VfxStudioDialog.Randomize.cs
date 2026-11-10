using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json.Nodes;
using Godot;
using Realm.Client.Core;
using Realm.Client.Services;
using Realm.Client.Utils;
using Realm.Client.VFX;

namespace Realm.Client.UI.MapEditor;

public partial class VfxStudioDialog
{
	private void RandomizeAllParameters()
	{
		bool isParticle = _currentConfig.PrimitiveType == VfxPrimitiveType.ParticleSystem;

		if (isParticle)
		{
			EnsureParticleConfig();

			var shapes = Enum.GetValues<SpellParticleShape>();
			Particle.EmitterShape = shapes.GetValue(Random.Shared.Next(0, shapes.Length)) is SpellParticleShape s ? s : SpellParticleShape.Sphere;

			Particle.Amount = Random.Shared.Next(12, 180);
			Particle.Lifetime = (float)Math.Round(Random.Shared.NextDouble() * (3.0 - 0.4) + 0.4, 2);
			Particle.Explosiveness = (float)Math.Round(Random.Shared.NextDouble() * 0.9, 2);
			Particle.LocalCoords = Random.Shared.NextDouble() > 0.6;

			float dirX = (float)Math.Round((Random.Shared.NextDouble() * 2.0 - 1.0), 2);
			float dirY = (float)Math.Round(Random.Shared.NextDouble() * 1.5, 2);
			float dirZ = (float)Math.Round((Random.Shared.NextDouble() * 2.0 - 1.0), 2);
			Particle.Direction = new Vector3(dirX, dirY, dirZ).Normalized().ToVector3Data();

			Particle.SpreadDegrees = (float)Math.Round(Random.Shared.NextDouble() * 80.0 + 10.0, 1);

			float vMin = (float)Math.Round(Random.Shared.NextDouble() * 4.0 + 0.5, 2);
			float vMax = (float)Math.Round(vMin + Random.Shared.NextDouble() * 8.0 + 0.5, 2);
			Particle.InitialVelocityMin = vMin;
			Particle.InitialVelocityMax = vMax;

			float gravY = (float)Math.Round(Random.Shared.NextDouble() * 12.0 - 6.0, 2);
			Particle.Gravity = new Vector3(0.0f, gravY, 0.0f).ToVector3Data();

			Particle.Damping = (float)Math.Round(Random.Shared.NextDouble() * 4.0, 2);
			Particle.RadialAccel = (float)Math.Round((Random.Shared.NextDouble() * 2.0 - 1.0) * 15.0, 2);
			Particle.TangentialAccel = (float)Math.Round((Random.Shared.NextDouble() * 2.0 - 1.0) * 15.0, 2);

			float sMin = (float)Math.Round(Random.Shared.NextDouble() * 0.4 + 0.05, 2);
			float sMax = (float)Math.Round(sMin + Random.Shared.NextDouble() * 0.6 + 0.1, 2);
			Particle.InitialScaleMin = sMin;
			Particle.InitialScaleMax = sMax;
			Particle.EndScaleRatio = (float)Math.Round(Random.Shared.NextDouble() * 2.0, 2);

			float baseHue = (float)Random.Shared.NextDouble();
			Color colStart = Color.FromHsv(baseHue, (float)(Random.Shared.NextDouble() * 0.4 + 0.6), 1.0f);
			Color colMid = Color.FromHsv((baseHue + 0.08f) % 1.0f, (float)(Random.Shared.NextDouble() * 0.4 + 0.6), (float)(Random.Shared.NextDouble() * 0.4 + 0.6));
			Color colEnd = Color.FromHsv((baseHue + 0.16f) % 1.0f, 1.0f, (float)(Random.Shared.NextDouble() * 0.3 + 0.1));

			Particle.ColorStart = "#" + colStart.ToHtml(false);
			Particle.ColorMid = "#" + colMid.ToHtml(false);
			Particle.ColorEnd = "#" + colEnd.ToHtml(false);
			Particle.EmissionEnergy = (float)Math.Round(Random.Shared.NextDouble() * 9.0 + 1.0, 1);
		}
		else
		{
			string chosenNoise = GenerateHeadlessRandomNoise();
			if (!string.IsNullOrEmpty(chosenNoise))
			{
				_currentConfig.NoiseTexture = chosenNoise;
			}

			_currentConfig.LuminanceToAlpha = Random.Shared.NextDouble() > 0.25;
			_currentConfig.LuminanceThreshold = (float)Math.Round(Random.Shared.NextDouble() * 0.35 + 0.01, 2);
			_currentConfig.LuminanceSmoothness = (float)Math.Round(Random.Shared.NextDouble() * 0.2 + 0.01, 2);
			_currentConfig.UseGrayscale = Random.Shared.NextDouble() > 0.4;
			_currentConfig.InvertMask = Random.Shared.NextDouble() > 0.75;
			_currentConfig.HighPassCutoff = (float)Math.Round(Random.Shared.NextDouble() * 0.3, 2);

			_currentConfig.BaseUvScroll = new Vector2(
				(float)Math.Round((Random.Shared.NextDouble() * 2.0 - 1.0) * 1.5, 2),
				(float)Math.Round((Random.Shared.NextDouble() * 2.0 - 1.0) * 1.5, 2)
			).ToVector2Data();
			_currentConfig.BaseUvScale = new Vector2(
				(float)Math.Round(Random.Shared.NextDouble() * 2.5 + 0.5, 2),
				(float)Math.Round(Random.Shared.NextDouble() * 2.5 + 0.5, 2)
			).ToVector2Data();

			_currentConfig.DistortionStrength = (float)Math.Round(Random.Shared.NextDouble() * 0.85 + 0.05, 2);
			_currentConfig.NoiseUvScroll = new Vector2(
				(float)Math.Round((Random.Shared.NextDouble() * 2.0 - 1.0) * 1.5, 2),
				(float)Math.Round((Random.Shared.NextDouble() * 2.0 - 1.0) * 1.5, 2)
			).ToVector2Data();
			_currentConfig.NoiseUvScale = new Vector2(
				(float)Math.Round(Random.Shared.NextDouble() * 2.5 + 0.5, 2),
				(float)Math.Round(Random.Shared.NextDouble() * 2.5 + 0.5, 2)
			).ToVector2Data();

			float baseHue = (float)Random.Shared.NextDouble();
			Color colBase = Color.FromHsv(baseHue, (float)(Random.Shared.NextDouble() * 0.3 + 0.7), 1.0f);
			Color colSec = Color.FromHsv((baseHue + 0.12f) % 1.0f, (float)(Random.Shared.NextDouble() * 0.3 + 0.7), (float)(Random.Shared.NextDouble() * 0.5 + 0.3));
			Color colCore = Color.FromHsv((baseHue + 0.95f) % 1.0f, (float)(Random.Shared.NextDouble() * 0.2), 1.0f);

			_currentConfig.BaseColor = "#" + colBase.ToHtml(false);
			_currentConfig.SecondaryColor = "#" + colSec.ToHtml(false);
			_currentConfig.CoreColor = "#" + colCore.ToHtml(false);
			_currentConfig.ColorMixRatio = (float)Math.Round(Random.Shared.NextDouble() * 0.8 + 0.2, 2);
			_currentConfig.EmissionBoost = (float)Math.Round(Random.Shared.NextDouble() * 7.0 + 1.5, 1);
			_currentConfig.CoreThreshold = (float)Math.Round(Random.Shared.NextDouble() * 0.6 + 0.3, 2);

			_currentConfig.EnableRadialFalloff = Random.Shared.NextDouble() > 0.3;
			float radStart = (float)Math.Round(Random.Shared.NextDouble() * 0.5 + 0.3, 2);
			_currentConfig.RadialFalloffStart = radStart;
			_currentConfig.RadialFalloffEnd = (float)Math.Round(radStart + Random.Shared.NextDouble() * 0.4 + 0.1, 2);

			_currentConfig.EnableLengthFade = Random.Shared.NextDouble() > 0.5;
			float lenStart = (float)Math.Round(Random.Shared.NextDouble() * 0.4 + 0.1, 2);
			_currentConfig.LengthFadeStart = lenStart;
			_currentConfig.LengthFadeEnd = (float)Math.Round(lenStart + Random.Shared.NextDouble() * 0.5 + 0.2, 2);
			_currentConfig.ErosionProgress = (float)Math.Round(Random.Shared.NextDouble() * 0.2, 2);

			_currentConfig.EnableFresnel = Random.Shared.NextDouble() > 0.6;
			_currentConfig.FresnelPower = (float)Math.Round(Random.Shared.NextDouble() * 4.0 + 1.0, 1);
			_currentConfig.FresnelIntensity = (float)Math.Round(Random.Shared.NextDouble() * 4.0 + 0.5, 1);

			_currentConfig.EnableDepthFade = Random.Shared.NextDouble() > 0.2;
			_currentConfig.DepthFadeDistance = (float)Math.Round(Random.Shared.NextDouble() * 1.2 + 0.1, 2);
		}

		UpdateUIFromCurrentConfig();
		RestartPreviewVfx();
	}

	private string GenerateHeadlessRandomNoise()
	{
		string wsPath = GameContext.EditorService._editingDirectory;
		string noiseDir = Path.Combine(wsPath, "Assets", "noise");
		Directory.CreateDirectory(noiseDir);

		if (!string.IsNullOrEmpty(_tempGeneratedNoiseFileName))
		{
			string oldPath = Path.Combine(noiseDir, _tempGeneratedNoiseFileName);
			if (File.Exists(oldPath))
			{
				try
				{
					File.Delete(oldPath);
				}
				catch
				{
				}
			}

			_tempGeneratedNoiseFileName = null;
			_tempGeneratedNoiseConfig = null;
		}

		int dedupeIndex = 1;
		string fileName;
		while (true)
		{
			fileName = $"random_noise_{dedupeIndex}.rtex";
			string fullPath = Path.Combine(noiseDir, fileName);
			if (!File.Exists(fullPath))
			{
				break;
			}

			dedupeIndex++;
		}

		string[] noiseTypes = new[] { "Perlin", "Simplex", "SimplexSmooth", "Cellular", "ValueCubic" };
		string[] fractalTypes = new[] { "Fbm", "Ridged", "PingPong" };

		var config = new JsonObject
		{
			["generator"] = "FastNoiseLite",
			["noise_type"] = noiseTypes[Random.Shared.Next(0, noiseTypes.Length)],
			["seed"] = Random.Shared.Next(1, 999999),
			["frequency"] = (float)Math.Round(Random.Shared.NextDouble() * (0.05 - 0.005) + 0.005, 4),
			["fractal_type"] = fractalTypes[Random.Shared.Next(0, fractalTypes.Length)],
			["fractal_octaves"] = Random.Shared.Next(2, 6),
			["fractal_lacunarity"] = (float)Math.Round(Random.Shared.NextDouble() * (3.0 - 1.5) + 1.5, 2),
			["fractal_gain"] = (float)Math.Round(Random.Shared.NextDouble() * (0.8 - 0.2) + 0.2, 2),
			["fractal_weighted_strength"] = (float)Math.Round(Random.Shared.NextDouble() * 0.6, 2),
			["invert"] = false,
			["normalize"] = true,
			["width"] = 512,
			["height"] = 512,
			["color_mode"] = "Grayscale"
		};

		string outputRtex = Path.Combine(noiseDir, fileName);
		try
		{
			string blake3Hash = NoiseTextureGenerator.GenerateAndSaveRtex(config, outputRtex);
			config["hash"] = blake3Hash;

			_tempGeneratedNoiseFileName = fileName;
			_tempGeneratedNoiseConfig = config;
			return fileName;
		}
		catch (Exception ex)
		{
			GameLog.LogError($"[VfxStudioDialog] Failed to generate random noise: {ex.Message}");
			return string.Empty;
		}
	}

	private void CleanupTransientNoiseFile()
	{
		if (!string.IsNullOrEmpty(_tempGeneratedNoiseFileName))
		{
			try
			{
				string wsPath = GameContext.EditorService._editingDirectory;
				string filePath = Path.Combine(wsPath, "Assets", "noise", _tempGeneratedNoiseFileName);
				if (File.Exists(filePath))
				{
					File.Delete(filePath);
				}
			}
			catch
			{
			}

			_tempGeneratedNoiseFileName = null;
			_tempGeneratedNoiseConfig = null;
		}
	}

}
