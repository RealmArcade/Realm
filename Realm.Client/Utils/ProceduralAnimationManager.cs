using System;
using System.Collections.Generic;
using System.IO;
using Godot;
using Realm.Client.Services;
using Realm.Client.VFX;
using Realm.Shared.Metadata;
using Realm.Shared.Services;
using Realm.Shared.Terrain;

namespace Realm.Client.Utils;

public static class ProceduralAnimationManager
{
	private static readonly Dictionary<string, ProceduralAnimationConfig> _defaultPresets = new(StringComparer.OrdinalIgnoreCase)
	{
		["cloth_cape"] = new ProceduralAnimationConfig
		{
			Id = "cloth_cape",
			Name = "Cloth Cape Sway",
			MotionType = ProceduralMotionType.ClothSway,
			MaskMode = SpatialMaskMode.HeightGradient,
			MaskMin = 0.0f,
			MaskMax = 1.6f,
			MaskPower = 1.5f,
			MaskInvert = true,
			SwayFrequency = 1.8f,
			SwayAmplitude = 0.12f,
			FlutterFrequency = 6.0f,
			FlutterAmplitude = 0.04f,
			WindInfluence = 0.8f,
			VelocityDragInfluence = 1.2f,
			WaveTurbulence = 0.5f,
			ImpulseDecay = 4.0f,
			ImpulseFrequency = 12.0f,
			ImpulseAmplitude = 0.25f,
			ImpulseDuration = 0.6f
		},
		["cloth_banner"] = new ProceduralAnimationConfig
		{
			Id = "cloth_banner",
			Name = "Cloth Banner / Flag",
			MotionType = ProceduralMotionType.ClothSway,
			MaskMode = SpatialMaskMode.HeightGradient,
			MaskMin = 0.0f,
			MaskMax = 2.5f,
			MaskPower = 1.0f,
			MaskInvert = true,
			SwayFrequency = 1.2f,
			SwayAmplitude = 0.25f,
			FlutterFrequency = 5.0f,
			FlutterAmplitude = 0.06f,
			WindInfluence = 1.5f,
			VelocityDragInfluence = 0.0f,
			WaveTurbulence = 0.6f,
			ImpulseDecay = 3.5f,
			ImpulseFrequency = 10.0f,
			ImpulseAmplitude = 0.30f,
			ImpulseDuration = 0.6f
		},
		["oak_tree_sway"] = new ProceduralAnimationConfig
		{
			Id = "oak_tree_sway",
			Name = "Oak Tree Canopy Sway",
			MotionType = ProceduralMotionType.FoliageWind,
			MaskMode = SpatialMaskMode.HeightGradient,
			MaskMin = 0.5f,
			MaskMax = 4.0f,
			MaskPower = 2.0f,
			MaskInvert = false,
			SwayFrequency = 0.8f,
			SwayAmplitude = 0.15f,
			FlutterFrequency = 4.5f,
			FlutterAmplitude = 0.05f,
			WindInfluence = 1.0f,
			VelocityDragInfluence = 0.0f,
			WaveTurbulence = 0.4f,
			ImpulseDecay = 3.5f,
			ImpulseFrequency = 10.0f,
			ImpulseAmplitude = 0.35f,
			ImpulseDuration = 0.6f,
			SwayRegions = new List<ProceduralSwayRegionConfig>
			{
				new()
				{
					ModelKey = string.Empty,
					RegionName = "Canopy",
					NormalizedMin = new Vector3Data(0.0f, 0.55f, 0.0f),
					NormalizedMax = new Vector3Data(1.0f, 1.0f, 1.0f),
					Feather = 0.18f,
					MaskPower = 1.5f,
					MeshNamePatterns = new List<string> { "canopy", "leaf", "leaves", "foliage" },
					MaterialNamePatterns = new List<string> { "canopy", "leaf", "leaves", "foliage" }
				}
			}
		},
		["palm_tree_wind"] = new ProceduralAnimationConfig
		{
			Id = "palm_tree_wind",
			Name = "Palm Tree Fronds",
			MotionType = ProceduralMotionType.FoliageWind,
			MaskMode = SpatialMaskMode.HeightGradient,
			MaskMin = 2.0f,
			MaskMax = 6.0f,
			MaskPower = 1.5f,
			MaskInvert = false,
			SwayFrequency = 1.2f,
			SwayAmplitude = 0.30f,
			FlutterFrequency = 5.0f,
			FlutterAmplitude = 0.08f,
			WindInfluence = 1.3f,
			VelocityDragInfluence = 0.0f,
			WaveTurbulence = 0.5f,
			ImpulseDecay = 3.0f,
			ImpulseFrequency = 8.0f,
			ImpulseAmplitude = 0.40f,
			ImpulseDuration = 0.7f,
			SwayRegions = new List<ProceduralSwayRegionConfig>
			{
				new()
				{
					ModelKey = string.Empty,
					RegionName = "Fronds",
					NormalizedMin = new Vector3Data(0.0f, 0.45f, 0.0f),
					NormalizedMax = new Vector3Data(1.0f, 1.0f, 1.0f),
					Feather = 0.15f,
					MaskPower = 1.2f,
					MeshNamePatterns = new List<string> { "frond", "leaf", "palm" },
					MaterialNamePatterns = new List<string> { "frond", "leaf", "palm" }
				}
			}
		},
		["creature_wings"] = new ProceduralAnimationConfig
		{
			Id = "creature_wings",
			Name = "Creature Wings Flap",
			MotionType = ProceduralMotionType.WingFlap,
			MaskMode = SpatialMaskMode.RadialAxis,
			MaskMin = 0.2f,
			MaskMax = 2.5f,
			MaskPower = 1.2f,
			MaskInvert = false,
			SwayFrequency = 2.5f,
			SwayAmplitude = 0.35f,
			FlutterFrequency = 8.0f,
			FlutterAmplitude = 0.05f,
			WindInfluence = 0.2f,
			VelocityDragInfluence = 0.5f,
			WaveTurbulence = 0.3f,
			ImpulseDecay = 4.0f,
			ImpulseFrequency = 14.0f,
			ImpulseAmplitude = 0.20f,
			ImpulseDuration = 0.5f
		},
		["resource_harvest_shake"] = new ProceduralAnimationConfig
		{
			Id = "resource_harvest_shake",
			Name = "Resource Impact Shake",
			MotionType = ProceduralMotionType.TransientShake,
			MaskMode = SpatialMaskMode.HeightGradient,
			MaskMin = 0.0f,
			MaskMax = 5.0f,
			MaskPower = 1.0f,
			MaskInvert = false,
			SwayFrequency = 0.0f,
			SwayAmplitude = 0.0f,
			FlutterFrequency = 0.0f,
			FlutterAmplitude = 0.0f,
			WindInfluence = 0.0f,
			VelocityDragInfluence = 0.0f,
			WaveTurbulence = 0.0f,
			ImpulseDecay = 4.0f,
			ImpulseFrequency = 14.0f,
			ImpulseAmplitude = 0.30f,
			ImpulseDuration = 0.5f
		}
	};

	public static Dictionary<string, ProceduralAnimationConfig> GetDefaultPresets() => _defaultPresets;

	public static Dictionary<string, ProceduralAnimationConfig> LoadAllConfigs(string? workspacePath = null)
	{
		var result = new Dictionary<string, ProceduralAnimationConfig>(StringComparer.OrdinalIgnoreCase);

		foreach (var kvp in _defaultPresets)
		{
			result[kvp.Key] = kvp.Value.Clone();
		}

		string wsPath = workspacePath ?? MapWorkspaceService.GetActiveWorkspacePath();
		try
		{
			var metadata = MapFileService.LoadMetadata(wsPath);
			if (metadata.CustomProceduralAnimations != null)
			{
				foreach (var cfg in metadata.CustomProceduralAnimations)
				{
					if (cfg != null && !string.IsNullOrEmpty(cfg.Id))
					{
						BackfillDefaultSwayRegions(cfg);
						result[cfg.Id] = cfg;
					}
				}
			}
		}
		catch (Exception ex)
		{
			GD.PrintErr($"[ProceduralAnimationManager] LoadAllConfigs error: {ex.Message}");
		}

		return result;
	}

	private static void BackfillDefaultSwayRegions(ProceduralAnimationConfig config)
	{
		if (config.SwayRegions != null && config.SwayRegions.Count > 0) return;
		if (!_defaultPresets.TryGetValue(config.Id, out ProceduralAnimationConfig? builtinPreset)) return;
		if (builtinPreset.SwayRegions == null || builtinPreset.SwayRegions.Count == 0) return;

		config.SwayRegions = builtinPreset.Clone().SwayRegions;
	}

	public static ProceduralAnimationConfig? GetConfig(string id, string? workspacePath = null)
	{
		if (string.IsNullOrEmpty(id)) return null;
		var all = LoadAllConfigs(workspacePath);
		if (all.TryGetValue(id, out var cfg))
		{
			return cfg;
		}

		string clean = Path.GetFileNameWithoutExtension(id);
		if (all.TryGetValue(clean, out var cleanCfg))
		{
			return cleanCfg;
		}

		return null;
	}

	public static void SaveConfig(ProceduralAnimationConfig config, string? workspacePath = null)
	{
		if (config == null || string.IsNullOrWhiteSpace(config.Id)) return;

		string wsPath = workspacePath ?? MapWorkspaceService.GetActiveWorkspacePath();
		try
		{
			var metadata = MapFileService.LoadMetadata(wsPath);
			metadata.CustomProceduralAnimations ??= new List<ProceduralAnimationConfig>();

			int idx = metadata.CustomProceduralAnimations.FindIndex(a => string.Equals(a.Id, config.Id, StringComparison.OrdinalIgnoreCase));
			if (idx >= 0)
			{
				metadata.CustomProceduralAnimations[idx] = config;
			}
			else
			{
				metadata.CustomProceduralAnimations.Add(config);
			}

			GameContext.MetadataService.SaveMetadata(wsPath, metadata);
			VfxShaderManager.ClearMotionCache();
		}
		catch (Exception ex)
		{
			GD.PrintErr($"[ProceduralAnimationManager] SaveConfig error: {ex.Message}");
		}
	}

	public static void DeleteConfig(string id, string? workspacePath = null)
	{
		if (string.IsNullOrWhiteSpace(id)) return;

		string wsPath = workspacePath ?? MapWorkspaceService.GetActiveWorkspacePath();
		try
		{
			var metadata = MapFileService.LoadMetadata(wsPath);
			if (metadata.CustomProceduralAnimations != null)
			{
				metadata.CustomProceduralAnimations.RemoveAll(a => string.Equals(a.Id, id, StringComparison.OrdinalIgnoreCase));
				GameContext.MetadataService.SaveMetadata(wsPath, metadata);
				VfxShaderManager.ClearMotionCache();
			}
		}
		catch (Exception ex)
		{
			GD.PrintErr($"[ProceduralAnimationManager] DeleteConfig error: {ex.Message}");
		}
	}

	public static void ApplyPreviewToNode(Node3D targetNode, ProceduralAnimationConfig config, Vector3 velocity, float impulseStrength, float impulseTime, bool showDebugMask, string? modelKey = null)
	{
		if (targetNode == null || !GodotObject.IsInstanceValid(targetNode)) return;
		ModelShaderManager.SetProceduralAnimation(targetNode, config, velocity, impulseStrength, impulseTime, showDebugMask, modelKey);
	}

	public static ProceduralSwayRegionResolution ResolveSwayRegion(ProceduralAnimationConfig config, string? modelKey)
	{
		return ProceduralSwayRegionResolver.Resolve(config, modelKey);
	}

	public static ProceduralShaderParameters BuildShaderParameters(ProceduralAnimationConfig config)
	{
		if (config == null)
		{
			return new ProceduralShaderParameters(Vector4.Zero, Vector4.Zero, Vector4.Zero, Vector4.Zero);
		}

		float signedPower = config.MaskInvert ? -MathF.Abs(config.MaskPower) : MathF.Abs(config.MaskPower);
		Vector4 parameters1 = new((float)config.MotionType, (float)config.MaskMode, config.MaskMin, config.MaskMax);
		Vector4 parameters2 = new(signedPower, config.SwayFrequency, config.SwayAmplitude, config.FlutterFrequency);
		Vector4 parameters3 = new(config.FlutterAmplitude, config.WindInfluence, config.VelocityDragInfluence, config.WaveTurbulence);
		Vector4 shake = new(config.ImpulseAmplitude, config.ImpulseFrequency, config.ImpulseDuration, config.ImpulseDecay);

		return new ProceduralShaderParameters(parameters1, parameters2, parameters3, shake);
	}
}