using Godot;
using Realm.Client.Services;
using Realm.Shared.Services;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Realm.Client.Utils;

public static class SpawnDeathShaderManager
{
	private const string ShaderPath = "res://Assets/shaders/universal_dissolve_spatial.gdshader";
	private static Shader _shader;

	public static Shader GetOrCreateShader()
	{
		if (_shader != null && GodotObject.IsInstanceValid(_shader))
		{
			return _shader;
		}

		_shader = GD.Load<Shader>(ShaderPath);
		return _shader;
	}

	public static Dictionary<string, CustomShaderConfig> LoadAllCustomShaders(string workspacePath = null)
	{
		var result = new Dictionary<string, CustomShaderConfig>(StringComparer.OrdinalIgnoreCase);

		string wsPath = !string.IsNullOrEmpty(workspacePath)
			? workspacePath
			: Services.MapWorkspaceService.GetActiveWorkspacePath();

		try
		{
			var metadata = MapFileService.LoadMetadata(wsPath);
			if (metadata?.SpawnShaders != null)
			{
				foreach (var kvp in metadata.SpawnShaders)
				{
					if (kvp.Value != null)
					{
						var s = kvp.Value;
						var cfg = new CustomShaderConfig
						{
							Key = kvp.Key,
							Name = !string.IsNullOrWhiteSpace(s.Name) ? s.Name : kvp.Key,
							TransitionMode = Math.Clamp(s.TransitionMode, 0, 6),
							Direction = Math.Clamp(s.Direction, 0, 3),
							EdgeColor = !string.IsNullOrWhiteSpace(s.EdgeColor) ? Color.FromHtml(s.EdgeColor) : new Color(1.0f, 0.4f, 0.1f, 1.0f),
							EdgeWidth = s.EdgeWidth,
							EdgeEmission = s.EdgeEmission,
							NoiseScale = s.NoiseScale,
							NoiseRoughness = s.NoiseRoughness,
							FresnelPower = s.FresnelPower,
							VertexDisplacement = s.VertexDisplacement,
							AlphaFade = s.AlphaFade,
							Duration = s.Duration,
							AssetType = !string.IsNullOrWhiteSpace(s.AssetType) ? s.AssetType : "SpawnShader"
						};
						result[kvp.Key] = cfg;
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
		string normalizedKey = TemplateIDHelper.NormalizeTemplateID("SpawnShader", shaderKey);
		if (all.TryGetValue(normalizedKey, out var normCfg))
		{
			return normCfg;
		}
		return null;
	}

	public static void SaveCustomShader(CustomShaderConfig config, string workspacePath = null)
	{
		if (config == null || string.IsNullOrWhiteSpace(config.Key)) return;

		string wsPath = !string.IsNullOrEmpty(workspacePath)
			? workspacePath
			: Services.MapWorkspaceService.GetActiveWorkspacePath();

		MetadataService.Instance.UpdateMetadata(wsPath, m =>
		{
			m.SpawnShaders ??= new(StringComparer.OrdinalIgnoreCase);
			m.SpawnShaders[config.Key] = new SpawnShaderMetadata
			{
				Name = config.Name,
				TransitionMode = config.TransitionMode,
				Direction = config.Direction,
				EdgeColor = "#" + config.EdgeColor.ToHtml(true),
				EdgeWidth = config.EdgeWidth,
				EdgeEmission = config.EdgeEmission,
				NoiseScale = config.NoiseScale,
				NoiseRoughness = config.NoiseRoughness,
				FresnelPower = config.FresnelPower,
				VertexDisplacement = config.VertexDisplacement,
				AlphaFade = config.AlphaFade,
				Duration = config.Duration,
				AssetType = !string.IsNullOrWhiteSpace(config.AssetType) ? config.AssetType : "SpawnShader"
			};
		});
	}

	public static void DeleteCustomShader(string shaderKey, string workspacePath = null)
	{
		if (string.IsNullOrWhiteSpace(shaderKey)) return;

		string wsPath = !string.IsNullOrEmpty(workspacePath)
			? workspacePath
			: Services.MapWorkspaceService.GetActiveWorkspacePath();

		try
		{
			Realm.Client.Utils.MapAssetHelper.RemoveManifestAsset(wsPath, "shaders", shaderKey);
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

	private static Texture2D GetAlbedoTexture(MeshInstance3D mesh)
	{
		var activeMat = mesh.GetActiveMaterial(0);
		if (activeMat is StandardMaterial3D stdMat) return stdMat.AlbedoTexture;
		if (activeMat is OrmMaterial3D ormMat) return ormMat.AlbedoTexture;
		if (activeMat is ShaderMaterial sMat) return sMat.GetShaderParameter("texture_albedo").As<Texture2D>();

		if (mesh.Mesh != null && mesh.Mesh.GetSurfaceCount() > 0)
		{
			var surfMat = mesh.Mesh.SurfaceGetMaterial(0);
			if (surfMat is StandardMaterial3D sm) return sm.AlbedoTexture;
			if (surfMat is OrmMaterial3D om) return om.AlbedoTexture;
			if (surfMat is ShaderMaterial shm) return shm.GetShaderParameter("texture_albedo").As<Texture2D>();
		}

		return null;
	}

	private static void ApplyShaderToMeshNode(Node node, CustomShaderConfig config, Aabb aabb, float progress, Shader shader)
	{
		if (IsExcludedMesh(node)) return;

		if (node is MeshInstance3D mesh)
		{
			ShaderMaterial mat = mesh.MaterialOverride as ShaderMaterial;
			if (mat == null || mat.Shader != shader)
			{
				Texture2D albedo = GetAlbedoTexture(mesh);
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
			ApplyShaderToMeshNode(child, config, aabb, progress, shader);
		}
	}

	public static void ApplyShaderPreview(Node3D targetNode, CustomShaderConfig config, float progress)
	{
		if (targetNode == null || !GodotObject.IsInstanceValid(targetNode)) return;

		var aabb = CalculateNodeAabb(targetNode);
		var shader = GetOrCreateShader();

		ApplyShaderToMeshNode(targetNode, config, aabb, progress, shader);
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

	private static CustomShaderConfig GetTransitionConfig(string shaderKey, bool isSpawn)
	{
		var fallbackKey = isSpawn ? "SpawnShader/magic_blueprint" : "SpawnShader/fire_demolish";
		var config = GetShaderConfig(shaderKey) ?? GetShaderConfig(fallbackKey) ?? LoadAllCustomShaders().Values.FirstOrDefault();

		if (config != null) return config;

		return new CustomShaderConfig
		{
			Key = fallbackKey,
			Name = isSpawn ? "Magic Blueprint" : "Fire Ember Dissolve",
			TransitionMode = isSpawn ? 0 : 1,
			Duration = 1.0f
		};
	}

	public static void AnimateTransition(Node3D targetNode, string shaderKey, bool isSpawn, float? durationOverride = null, Action onComplete = null)
	{
		if (!IsValidForTransition(targetNode))
		{
			if (onComplete != null)
			{
				onComplete();
			}
			return;
		}

		var config = GetTransitionConfig(shaderKey, isSpawn);

		float duration = Math.Max(durationOverride.GetValueOrDefault(config.Duration), 0.05f);
		float startProgress = isSpawn ? 0.0f : 1.0f;
		float endProgress = isSpawn ? 1.0f : 0.0f;

		ApplyShaderPreview(targetNode, config, startProgress);

		var tween = targetNode.CreateTween();

		tween.TweenMethod(Callable.From((float prog) => UpdateTransitionProgress(targetNode, config, prog)), startProgress, endProgress, duration);

		tween.TweenCallback(Callable.From(() => CompleteTransition(targetNode, isSpawn, onComplete)));
	}

	private static bool IsValidForTransition(Node3D targetNode)
	{
		if (targetNode == null) return false;
		if (!GodotObject.IsInstanceValid(targetNode)) return false;
		if (targetNode.GetTree() == null) return false;
		return true;
	}

	private static void UpdateTransitionProgress(Node3D targetNode, CustomShaderConfig config, float prog)
	{
		if (GodotObject.IsInstanceValid(targetNode))
		{
			ApplyShaderPreview(targetNode, config, prog);
		}
	}

	private static void CompleteTransition(Node3D targetNode, bool isSpawn, Action onComplete)
	{
		if (GodotObject.IsInstanceValid(targetNode))
		{
			if (isSpawn)
			{
				ClearShaderOverride(targetNode);
			}
		}

		if (onComplete != null)
		{
			onComplete();
		}
	}
}
