using System;
using System.Collections.Generic;
using System.IO;
using Realm.Ecs;
using Godot;
using Realm.Client.Core;
using Realm.Client.Services;
using Realm.Client.Services.ModelOptimization;
using Realm.Client.Utils;
using Realm.Client.VFX;
using Realm.Ecs.Components.Core;
using Realm.Ecs.Services;
using Realm.Shared.Textures;
using FileAccess = Godot.FileAccess;

namespace Realm.Client;

public partial class VisualProjectile3D : Node3D
{
	private void UpdateVisualTransform()
	{
		_visualTransformContainer.Position = _weapon.MeshTranslationOffset.ToGodotVector3();

		Vector3 baseEuler = GetForwardAxisEulerDegrees(_weapon.ForwardAxisPreset);
		Vector3 totalEuler = baseEuler + _weapon.MeshRotationOffset.ToGodotVector3();
		_visualTransformContainer.Rotation = new Vector3(
			Mathf.DegToRad(totalEuler.X),
			Mathf.DegToRad(totalEuler.Y),
			Mathf.DegToRad(totalEuler.Z)
		);

		Vector3 meshScale = _weapon.MeshScaleOffset.ToGodotVector3();
		Vector3 baseScale = (meshScale == Vector3.Zero) ? Vector3.One : meshScale;
		baseScale = SafeScale(baseScale);
		float initialScaleFactor = Mathf.Max(0.001f, ProjectileFlightMath.ScaleOverLifetime(0.0f, _scaleCurve));
		_visualTransformContainer.Scale = SafeScale(baseScale * initialScaleFactor);
	}

	private static Vector3 SafeScale(Vector3 scale)
	{
		return new Vector3(
			Mathf.Max(0.001f, Mathf.Abs(scale.X)),
			Mathf.Max(0.001f, Mathf.Abs(scale.Y)),
			Mathf.Max(0.001f, Mathf.Abs(scale.Z))
		);
	}

	private static Vector3 GetForwardAxisEulerDegrees(string preset)
	{
		if (string.IsNullOrEmpty(preset)) return Vector3.Zero;
		switch (preset.Trim().ToUpperInvariant())
		{
			case "+Z":
				return new Vector3(0f, 180f, 0f);
			case "+X":
				return new Vector3(0f, -90f, 0f);
			case "-X":
				return new Vector3(0f, 90f, 0f);
			case "+Y":
				return new Vector3(-90f, 0f, 0f);
			case "-Y":
				return new Vector3(90f, 0f, 0f);
			case "-Z":
			default:
				return Vector3.Zero;
		}
	}

	private void HideCustomModelInstance()
	{
		if (_customModelInstance != null && GodotObject.IsInstanceValid(_customModelInstance))
		{
			_customModelInstance.Visible = false;
			_customModelInstance = null;
		}
	}

	private void EnableFallbackMesh()
	{
		_fallbackMeshInstance.Visible = true;
		_uberShaderMaterial.SetShaderParameter("use_albedo_texture", false);
		_fallbackMeshInstance.MaterialOverride = _uberShaderMaterial;
	}

	private Node3D? LoadModelPath(string modelPath)
	{
		if (modelPath.StartsWith("vfx:", StringComparison.OrdinalIgnoreCase) ||
		    (GameContext.RegistryService.VfxRegistry != null && GameContext.RegistryService.VfxRegistry.ContainsKey(modelPath)))
		{
			return Unit3D.ResolveAndInstantiateAttachment(modelPath, out _, out _, out _);
		}

		var loaded = ModelCache.GetModel(modelPath);
		if (loaded is Node3D n)
		{
			return n;
		}
		loaded?.QueueFree();
		return null;
	}

	private void UpdateModel()
	{
		string? modelPath = _weapon.ProjectileModelPath;
		if (string.IsNullOrEmpty(modelPath))
		{
			HideCustomModelInstance();
			_currentLoadedModelPath = null;
			EnableFallbackMesh();
			return;
		}

		bool needsReload = modelPath != _currentLoadedModelPath || _customModelInstance == null || !GodotObject.IsInstanceValid(_customModelInstance);
		if (!needsReload && _customModelInstance is { } customModel)
		{
			customModel.Visible = true;
			_fallbackMeshInstance.Visible = false;
			return;
		}

		HideCustomModelInstance();
		_currentLoadedModelPath = modelPath;

		if (_loadedModelsByPath.TryGetValue(modelPath, out var cachedNode) && cachedNode != null && GodotObject.IsInstanceValid(cachedNode))
		{
			_customModelInstance = cachedNode;
			if (cachedNode is ProceduralVfxInstance3D cachedVfx)
			{
				cachedVfx.PlayerIndex = PlayerIndex;
			}
			cachedNode.Visible = true;
			_fallbackMeshInstance.Visible = false;
			GltfDocumentExtensionMsftLod.UpdateLodVisibilityRanges(cachedNode, Math.Max(0.01f, _visualTransformContainer.Scale.Y));
			return;
		}

		Node3D? node3D = LoadModelPath(modelPath);
		if (node3D == null)
		{
			EnableFallbackMesh();
			return;
		}

		if (node3D is ProceduralVfxInstance3D projectileVfx)
		{
			projectileVfx.PlayerIndex = PlayerIndex;
		}

		_loadedModelsByPath[modelPath] = node3D;
		_customModelInstance = node3D;
		_visualTransformContainer.AddChild(_customModelInstance);
		_fallbackMeshInstance.Visible = false;
		GltfDocumentExtensionMsftLod.UpdateLodVisibilityRanges(_customModelInstance, Math.Max(0.01f, _visualTransformContainer.Scale.Y));

		if (node3D is not ProceduralVfxInstance3D)
		{
			ApplyUberMaterialRecursively(_customModelInstance);
		}
	}

	private int GetMaskSource(string maskSource)
	{
		if (string.IsNullOrEmpty(maskSource)) return 0;

		return maskSource.Trim().ToLowerInvariant() switch
		{
			"vertex_color" or "vertex_color_spikes" or "vertex color / spikes" or "spikes" => 1,
			"fresnel" or "fresnel_only" or "fresnel only" => 2,
			"texture_alpha" or "texture alpha" => 3,
			_ => 0,
		};
	}

	private void ConfigureNoiseTexture(ShaderMaterial mat)
	{
		if (string.IsNullOrEmpty(_weapon.NoiseTexture))
		{
			mat.SetShaderParameter("use_procedural_noise", true);
			return;
		}

		var tex = LoadTextureSafe(_weapon.NoiseTexture);
		mat.SetShaderParameter("noise_texture", tex != null ? (Variant)tex : default(Variant));
		mat.SetShaderParameter("use_procedural_noise", tex == null);
	}

	private void ConfigureShaderMaterial(ShaderMaterial mat, Texture2D? albedoTex)
	{
		EnsureSharedShader();
		mat.Shader ??= _sharedUberShader;

		Color baseCol = ParseColor(_weapon.BaseColor, new Color(0.15f, 0.12f, 0.1f));
		Color emissiveCol = ParseColor(_weapon.EmissionColor, new Color(1.0f, 0.4f, 0.05f));
		Color fresnelCol = ParseColor(_weapon.FresnelColor, new Color(1.0f, 0.6f, 0.1f));

		bool effectEnabled = !string.Equals(_weapon.ShaderEffectType, "none", StringComparison.OrdinalIgnoreCase);
		mat.SetShaderParameter("effect_enabled", effectEnabled);
		mat.SetShaderParameter("base_color", baseCol);
		mat.SetShaderParameter("emission_color", emissiveCol);
		mat.SetShaderParameter("emission_energy", _weapon.EmissionEnergy);
		mat.SetShaderParameter("fresnel_power", _weapon.FresnelPower > 0.01f ? _weapon.FresnelPower : 3.0f);
		mat.SetShaderParameter("fresnel_color", fresnelCol);
		mat.SetShaderParameter("fresnel_factor", _weapon.FresnelFactor);
		mat.SetShaderParameter("noise_scale", _weapon.NoiseScale > 0.01f ? _weapon.NoiseScale : 3.0f);
		mat.SetShaderParameter("uv_scroll_speed_1", _weapon.UvScrollSpeed1.ToGodotVector2());
		mat.SetShaderParameter("uv_scroll_speed_2", _weapon.UvScrollSpeed2.ToGodotVector2());
		mat.SetShaderParameter("threshold_cutoff", _weapon.ThresholdCutoff);
		mat.SetShaderParameter("threshold_smoothness", _weapon.ThresholdSmoothness > 0.001f ? _weapon.ThresholdSmoothness : 0.1f);

		mat.SetShaderParameter("emission_mask_source", GetMaskSource(_weapon.EmissionMaskSource));
		ConfigureNoiseTexture(mat);

		mat.SetShaderParameter("albedo_texture", albedoTex != null ? (Variant)albedoTex : default(Variant));
		mat.SetShaderParameter("use_albedo_texture", albedoTex != null);
	}

	private Texture2D? FindAlbedoTexture(MeshInstance3D meshInst)
	{
		int surfCount = meshInst.Mesh?.GetSurfaceCount() ?? 0;
		for (int s = 0; s < surfCount; s++)
		{
			var surfMat = meshInst.GetSurfaceOverrideMaterial(s) ?? meshInst.Mesh?.SurfaceGetMaterial(s);
			if (surfMat is BaseMaterial3D baseMat && baseMat.AlbedoTexture != null)
			{
				return baseMat.AlbedoTexture;
			}
		}

		return null;
	}

	private void ApplyUberMaterialToMesh(MeshInstance3D meshInst)
	{
		if (string.Equals(_weapon.ShaderEffectType, "none", StringComparison.OrdinalIgnoreCase))
		{
			meshInst.MaterialOverride = null;
			return;
		}

		Texture2D? albedoTex = FindAlbedoTexture(meshInst);
		var matInstance = meshInst.MaterialOverride as ShaderMaterial ?? new ShaderMaterial();
		ConfigureShaderMaterial(matInstance, albedoTex);
		meshInst.MaterialOverride = matInstance;
	}

	private void ApplyUberMaterialRecursively(Node node)
	{
		if (node is MeshInstance3D meshInst)
		{
			ApplyUberMaterialToMesh(meshInst);
		}

		foreach (Node child in node.GetChildren())
		{
			ApplyUberMaterialRecursively(child);
		}
	}

	private void UpdateShaderMaterial()
	{
		ConfigureShaderMaterial(_uberShaderMaterial, null);

		if (_customModelInstance != null && GodotObject.IsInstanceValid(_customModelInstance))
		{
			ApplyUberMaterialRecursively(_customModelInstance);
		}
	}

	private Color _parsedRibbonColor = new(1.0f, 0.65f, 0.2f);
	private bool _hasRibbonTrail = true;

	private void UpdateRibbonTrail()
	{
		_parsedRibbonColor = ParseColor(_weapon.RibbonColor, new Color(1.0f, 0.65f, 0.2f));
		_hasRibbonTrail = _weapon.RibbonLifetime >= 0f && _weapon.RibbonWidth >= 0f && !string.Equals(_weapon.RibbonColor, "none", StringComparison.OrdinalIgnoreCase);
	}

}
