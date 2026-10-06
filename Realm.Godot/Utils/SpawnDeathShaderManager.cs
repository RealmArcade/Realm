using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json.Nodes;
using Godot;
using Realm.Godot.Services;
using Realm.Shared.Metadata;
using Realm.Shared.Services;

namespace Realm.Godot.Utils;

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
	public string? AssetType { get; set; } = "Shader";
	public string? Hash { get; set; }

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
			Duration = this.Duration
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
			["asset_type"] = "Shader"
		};
	}

	public static CustomShaderConfig FromJson(string key, JsonNode node)
	{
		var config = new CustomShaderConfig { Key = key, Name = key };
		if (node is JsonObject obj)
		{
			if (obj.TryGetPropertyValue("name", out var nameVal) && !string.IsNullOrWhiteSpace(nameVal?.ToString()))
			{
				config.Name = nameVal.ToString();
			}
			if (obj.TryGetPropertyValue("transition_mode", out var tmVal) && int.TryParse(tmVal?.ToString(), out int tm))
			{
				config.TransitionMode = Math.Clamp(tm, 0, 6);
			}
			if (obj.TryGetPropertyValue("direction", out var dirVal) && int.TryParse(dirVal?.ToString(), out int dir))
			{
				config.Direction = Math.Clamp(dir, 0, 3);
			}
			if (obj.TryGetPropertyValue("edge_color", out var colVal) && !string.IsNullOrWhiteSpace(colVal?.ToString()))
			{
				config.EdgeColor = Color.FromHtml(colVal.ToString());
			}
			if (obj.TryGetPropertyValue("edge_width", out var ewVal) && float.TryParse(ewVal?.ToString(), out float ew))
			{
				config.EdgeWidth = ew;
			}
			if (obj.TryGetPropertyValue("edge_emission", out var eeVal) && float.TryParse(eeVal?.ToString(), out float ee))
			{
				config.EdgeEmission = ee;
			}
			if (obj.TryGetPropertyValue("noise_scale", out var nsVal) && float.TryParse(nsVal?.ToString(), out float ns))
			{
				config.NoiseScale = ns;
			}
			if (obj.TryGetPropertyValue("noise_roughness", out var nrVal) && float.TryParse(nrVal?.ToString(), out float nr))
			{
				config.NoiseRoughness = nr;
			}
			if (obj.TryGetPropertyValue("fresnel_power", out var fpVal) && float.TryParse(fpVal?.ToString(), out float fp))
			{
				config.FresnelPower = fp;
			}
			if (obj.TryGetPropertyValue("vertex_displacement", out var vdVal) && float.TryParse(vdVal?.ToString(), out float vd))
			{
				config.VertexDisplacement = vd;
			}
			if (obj.TryGetPropertyValue("alpha_fade", out var afVal) && float.TryParse(afVal?.ToString(), out float af))
			{
				config.AlphaFade = af;
			}
			if (obj.TryGetPropertyValue("duration", out var durVal) && float.TryParse(durVal?.ToString(), out float dur))
			{
				config.Duration = dur;
			}
		}
		return config;
	}
}

public static class SpawnDeathShaderManager
{
	private const string ShaderPath = "res://Assets/shaders/universal_dissolve_spatial.gdshader";
	private static Shader _shader;

	private static readonly Dictionary<string, CustomShaderConfig> _defaultPresets = new(StringComparer.OrdinalIgnoreCase)
	{
		["magic_blueprint"] = new CustomShaderConfig
		{
			Key = "magic_blueprint",
			Name = "Magic Blueprint",
			TransitionMode = 0,
			Direction = 0,
			EdgeColor = new Color(0.0f, 0.9f, 1.0f, 1.0f),
			EdgeWidth = 0.06f,
			EdgeEmission = 6.0f,
			NoiseScale = 12.0f,
			NoiseRoughness = 0.4f,
			FresnelPower = 3.0f,
			VertexDisplacement = 0.0f,
			AlphaFade = 0.9f,
			Duration = 1.2f
		},
		["fire_demolish"] = new CustomShaderConfig
		{
			Key = "fire_demolish",
			Name = "Fire Ember Dissolve",
			TransitionMode = 1,
			Direction = 1,
			EdgeColor = new Color(1.0f, 0.35f, 0.05f, 1.0f),
			EdgeWidth = 0.08f,
			EdgeEmission = 7.0f,
			NoiseScale = 16.0f,
			NoiseRoughness = 0.7f,
			FresnelPower = 1.5f,
			VertexDisplacement = 0.15f,
			AlphaFade = 1.0f,
			Duration = 1.5f
		},
		["hologram_warp"] = new CustomShaderConfig
		{
			Key = "hologram_warp",
			Name = "Hologram Scanlines",
			TransitionMode = 2,
			Direction = 0,
			EdgeColor = new Color(0.4f, 1.0f, 0.2f, 1.0f),
			EdgeWidth = 0.04f,
			EdgeEmission = 4.0f,
			NoiseScale = 20.0f,
			NoiseRoughness = 0.2f,
			FresnelPower = 4.0f,
			VertexDisplacement = 0.02f,
			AlphaFade = 0.75f,
			Duration = 1.0f
		},
		["earth_crumble"] = new CustomShaderConfig
		{
			Key = "earth_crumble",
			Name = "Earth Ground Crumble",
			TransitionMode = 3,
			Direction = 1,
			EdgeColor = new Color(0.6f, 0.45f, 0.3f, 1.0f),
			EdgeWidth = 0.05f,
			EdgeEmission = 2.0f,
			NoiseScale = 8.0f,
			NoiseRoughness = 0.8f,
			FresnelPower = 1.0f,
			VertexDisplacement = 0.25f,
			AlphaFade = 1.0f,
			Duration = 1.1f
		},
		["frost_crystallize"] = new CustomShaderConfig
		{
			Key = "frost_crystallize",
			Name = "Frost Crystallize",
			TransitionMode = 4,
			Direction = 2,
			EdgeColor = new Color(0.7f, 0.9f, 1.0f, 1.0f),
			EdgeWidth = 0.05f,
			EdgeEmission = 5.0f,
			NoiseScale = 25.0f,
			NoiseRoughness = 0.6f,
			FresnelPower = 3.5f,
			VertexDisplacement = 0.03f,
			AlphaFade = 0.95f,
			Duration = 1.3f
		},
		["shadow_void"] = new CustomShaderConfig
		{
			Key = "shadow_void",
			Name = "Shadow Void Collapse",
			TransitionMode = 5,
			Direction = 3,
			EdgeColor = new Color(0.7f, 0.1f, 1.0f, 1.0f),
			EdgeWidth = 0.07f,
			EdgeEmission = 8.0f,
			NoiseScale = 14.0f,
			NoiseRoughness = 0.9f,
			FresnelPower = 2.0f,
			VertexDisplacement = 0.18f,
			AlphaFade = 1.0f,
			Duration = 1.4f
		}
	};

	public static Shader GetOrCreateShader()
	{
		if (_shader != null && GodotObject.IsInstanceValid(_shader))
		{
			return _shader;
		}

		_shader = GD.Load<Shader>(ShaderPath);
		return _shader;
	}

	public static Dictionary<string, CustomShaderConfig> GetDefaultPresets() => _defaultPresets;

	public static Dictionary<string, CustomShaderConfig> LoadAllCustomShaders(string workspacePath = null)
	{
		var result = new Dictionary<string, CustomShaderConfig>(StringComparer.OrdinalIgnoreCase);

		foreach (var kvp in _defaultPresets)
		{
			result[kvp.Key] = kvp.Value.Clone();
		}

		string wsPath = !string.IsNullOrEmpty(workspacePath)
			? workspacePath
			: MapWorkspaceService.GetActiveWorkspacePath();

		try
		{
			var metadata = MapFileService.LoadMetadata(wsPath);
			if (metadata?.Shaders != null)
			{
				foreach (var kvp in metadata.Shaders)
				{
					if (!string.IsNullOrEmpty(kvp.Value?.ConfigJson))
					{
						try
						{
							var cfgNode = JsonNode.Parse(kvp.Value.ConfigJson);
							if (cfgNode != null)
							{
								var cfg = CustomShaderConfig.FromJson(kvp.Key, cfgNode);
								if (cfg != null)
								{
									result[kvp.Key] = cfg;
								}
							}
						}
						catch { }
					}
				}
			}
		}
		catch (Exception ex)
		{
			GD.PrintErr($"[SpawnDeathShaderManager] LoadAllCustomShaders error: {ex.Message}");
		}

		return result;
	}

	public static CustomShaderConfig GetShaderConfig(string shaderKey, string workspacePath = null)
	{
		if (string.IsNullOrEmpty(shaderKey)) return null;
		var all = LoadAllCustomShaders(workspacePath);
		if (all.TryGetValue(shaderKey, out var cfg))
		{
			return cfg;
		}
		if (_defaultPresets.TryGetValue(shaderKey, out var def))
		{
			return def.Clone();
		}
		return null;
	}

	public static void SaveCustomShader(CustomShaderConfig config, string workspacePath = null)
	{
		if (config == null || string.IsNullOrWhiteSpace(config.Key)) return;

		string wsPath = !string.IsNullOrEmpty(workspacePath)
			? workspacePath
			: MapWorkspaceService.GetActiveWorkspacePath();

		MetadataService.Instance.UpdateMetadata(wsPath, m =>
		{
			m.Shaders ??= new(StringComparer.OrdinalIgnoreCase);
			m.Shaders[config.Key] = new ShaderMetadata
			{
				ConfigJson = config.ToJsonObject().ToJsonString()
			};
		});
	}

	public static void DeleteCustomShader(string shaderKey, string workspacePath = null)
	{
		if (string.IsNullOrWhiteSpace(shaderKey)) return;

		string wsPath = !string.IsNullOrEmpty(workspacePath)
			? workspacePath
			: MapWorkspaceService.GetActiveWorkspacePath();

		try
		{
			Realm.Godot.Utils.MapAssetHelper.RemoveManifestAsset(wsPath, "shaders", shaderKey);
		}
		catch (Exception ex)
		{
			GD.PrintErr($"[SpawnDeathShaderManager] DeleteCustomShader error: {ex.Message}");
		}
	}

	public static ShaderMaterial CreateShaderMaterial(CustomShaderConfig config, Aabb aabb, Texture2D albedoTex = null)
	{
		var shader = GetOrCreateShader();
		var mat = new ShaderMaterial();
		mat.Shader = shader;

		if (config != null)
		{
			mat.SetShaderParameter("transition_mode", config.TransitionMode);
			mat.SetShaderParameter("direction", config.Direction);
			mat.SetShaderParameter("edge_color", config.EdgeColor);
			mat.SetShaderParameter("edge_width", config.EdgeWidth);
			mat.SetShaderParameter("edge_emission", config.EdgeEmission);
			mat.SetShaderParameter("noise_scale", config.NoiseScale);
			mat.SetShaderParameter("noise_roughness", config.NoiseRoughness);
			mat.SetShaderParameter("fresnel_power", config.FresnelPower);
			mat.SetShaderParameter("vertex_displacement", config.VertexDisplacement);
			mat.SetShaderParameter("alpha_fade", config.AlphaFade);
		}

		if (albedoTex != null)
		{
			mat.SetShaderParameter("texture_albedo", albedoTex);
		}

		mat.SetShaderParameter("model_bounds_min", aabb.Position);
		mat.SetShaderParameter("model_bounds_max", aabb.Position + aabb.Size);

		return mat;
	}

	private static bool IsExcludedMesh(Node node)
	{
		if (node == null) return true;
		string nodeName = node.Name.ToString();
		return nodeName.StartsWith("_selection", StringComparison.OrdinalIgnoreCase)
			|| nodeName.StartsWith("Selection", StringComparison.OrdinalIgnoreCase)
			|| nodeName.StartsWith("_hover", StringComparison.OrdinalIgnoreCase)
			|| nodeName.StartsWith("Hover", StringComparison.OrdinalIgnoreCase)
			|| nodeName.StartsWith("BrushIndicator", StringComparison.OrdinalIgnoreCase)
			|| nodeName.StartsWith("DropShadow", StringComparison.OrdinalIgnoreCase)
			|| nodeName.Contains("SelectionRing", StringComparison.OrdinalIgnoreCase)
			|| nodeName.Contains("HoverRing", StringComparison.OrdinalIgnoreCase);
	}

	public static Aabb CalculateNodeAabb(Node3D root)
	{
		Aabb combined = new Aabb(Vector3.Zero, Vector3.One);
		bool first = true;

		void Traverse(Node node)
		{
			if (IsExcludedMesh(node)) return;
			if (node is MeshInstance3D mesh && mesh.Mesh != null)
			{
				var aabb = mesh.GetAabb();
				if (first)
				{
					combined = aabb;
					first = false;
				}
				else
				{
					combined = combined.Merge(aabb);
				}
			}
			foreach (Node child in node.GetChildren())
			{
				Traverse(child);
			}
		}

		Traverse(root);
		if (combined.Size.Y <= 0.01f)
		{
			combined.Size = new Vector3(combined.Size.X, 1.0f, combined.Size.Z);
		}
		return combined;
	}

	public static void ApplyShaderPreview(Node3D targetNode, CustomShaderConfig config, float progress)
	{
		if (targetNode == null || !GodotObject.IsInstanceValid(targetNode)) return;

		var aabb = CalculateNodeAabb(targetNode);

		void ApplyToMesh(Node node)
		{
			if (IsExcludedMesh(node)) return;
			if (node is MeshInstance3D mesh)
			{
				ShaderMaterial mat = mesh.MaterialOverride as ShaderMaterial;
				if (mat == null || mat.Shader != GetOrCreateShader())
				{
					Texture2D albedo = null;
					if (mesh.GetActiveMaterial(0) is StandardMaterial3D stdMat)
					{
						albedo = stdMat.AlbedoTexture;
					}
					else if (mesh.GetActiveMaterial(0) is OrmMaterial3D ormMat)
					{
						albedo = ormMat.AlbedoTexture;
					}
					else if (mesh.GetActiveMaterial(0) is ShaderMaterial sMat)
					{
						albedo = sMat.GetShaderParameter("texture_albedo").As<Texture2D>();
					}
					else if (mesh.Mesh != null && mesh.Mesh.GetSurfaceCount() > 0)
					{
						var surfMat = mesh.Mesh.SurfaceGetMaterial(0);
						if (surfMat is StandardMaterial3D sm) albedo = sm.AlbedoTexture;
						else if (surfMat is OrmMaterial3D om) albedo = om.AlbedoTexture;
						else if (surfMat is ShaderMaterial shm) albedo = shm.GetShaderParameter("texture_albedo").As<Texture2D>();
					}
					mat = CreateShaderMaterial(config, aabb, albedo);
					mesh.MaterialOverride = mat;
				}

				mat.SetShaderParameter("progress", Mathf.Clamp(progress, 0.0f, 1.0f));
				mat.SetShaderParameter("transition_mode", config.TransitionMode);
				mat.SetShaderParameter("direction", config.Direction);
				mat.SetShaderParameter("edge_color", config.EdgeColor);
				mat.SetShaderParameter("edge_width", config.EdgeWidth);
				mat.SetShaderParameter("edge_emission", config.EdgeEmission);
				mat.SetShaderParameter("noise_scale", config.NoiseScale);
				mat.SetShaderParameter("noise_roughness", config.NoiseRoughness);
				mat.SetShaderParameter("fresnel_power", config.FresnelPower);
				mat.SetShaderParameter("vertex_displacement", config.VertexDisplacement);
				mat.SetShaderParameter("alpha_fade", config.AlphaFade);
				mat.SetShaderParameter("model_bounds_min", aabb.Position);
				mat.SetShaderParameter("model_bounds_max", aabb.Position + aabb.Size);
			}

			foreach (Node child in node.GetChildren())
			{
				ApplyToMesh(child);
			}
		}

		ApplyToMesh(targetNode);
	}

	public static void ClearShaderOverride(Node3D targetNode)
	{
		if (targetNode == null || !GodotObject.IsInstanceValid(targetNode)) return;

		void ClearMesh(Node node)
		{
			if (IsExcludedMesh(node)) return;
			if (node is MeshInstance3D mesh)
			{
				mesh.MaterialOverride = null;
			}
			foreach (Node child in node.GetChildren())
			{
				ClearMesh(child);
			}
		}

		ClearMesh(targetNode);
	}

	public static void AnimateTransition(Node3D targetNode, string shaderKey, bool isSpawn, float? durationOverride = null, Action onComplete = null)
	{
		if (targetNode == null || !GodotObject.IsInstanceValid(targetNode))
		{
			onComplete?.Invoke();
			return;
		}

		var config = GetShaderConfig(shaderKey) ?? _defaultPresets[isSpawn ? "magic_blueprint" : "fire_demolish"];
		float duration = durationOverride ?? config.Duration;
		if (duration <= 0.05f) duration = 0.05f;

		var aabb = CalculateNodeAabb(targetNode);
		var tree = targetNode.GetTree();
		if (tree == null)
		{
			onComplete?.Invoke();
			return;
		}

		var tween = targetNode.CreateTween();
		float startProgress = isSpawn ? 0.0f : 1.0f;
		float endProgress = isSpawn ? 1.0f : 0.0f;

		ApplyShaderPreview(targetNode, config, startProgress);

		var callable = Callable.From((float prog) =>
		{
			if (GodotObject.IsInstanceValid(targetNode))
			{
				ApplyShaderPreview(targetNode, config, prog);
			}
		});

		tween.TweenMethod(callable, startProgress, endProgress, duration);
		tween.TweenCallback(Callable.From(() =>
		{
			if (GodotObject.IsInstanceValid(targetNode))
			{
				if (isSpawn)
				{
					ClearShaderOverride(targetNode);
				}
			}
			onComplete?.Invoke();
		}));
	}
}
