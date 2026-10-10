using Godot;
using System;
using System.Collections.Generic;

namespace Realm.Client.Utils;

public static class ModelShaderManager
{
	private static Shader _sharedShader;
	private static readonly Dictionary<string, ShaderMaterial> _materialCache = new(StringComparer.Ordinal);
	private static readonly Dictionary<ulong, Texture2D> _normalizedAlbedoCache = new();
	private static readonly Dictionary<ulong, bool> _playerMaskCheckCache = new();
	private static readonly float[] SrgbToLinearLut = new float[256];
	private static readonly byte[] LinearToSrgbLut = PrecomputeLinearToSrgbLut();

	private static byte[] PrecomputeLinearToSrgbLut()
	{
		byte[] table = new byte[65536];
		for (int i = 0; i < 65536; i++)
		{
			float linear = i / 65535.0f;
			float srgb = linear <= 0.0031308f
				? 12.92f * linear
				: 1.055f * MathF.Pow(linear, 1.0f / 2.4f) - 0.055f;
			table[i] = (byte)Math.Clamp((int)(srgb * 255.0f + 0.5f), 0, 255);
		}
		return table;
	}
	private static readonly StringName _paramPlayerColor = new("player_color");
	private static readonly StringName _paramModelBrightness = new("model_brightness");
	private static readonly StringName _paramModelColorTint = new("model_color_tint");
	private static readonly StringName _paramIgnorePlayerColor = new("ignore_player_color");
	private static readonly StringName _paramUnitAmbientBoost = new("unit_ambient_boost");
	private static readonly StringName _paramUnitRimIntensity = new("unit_rim_intensity");
	private static readonly StringName _paramHideInShroud = new("hide_in_shroud");
	private static readonly StringName _paramShroudTexture = new("shroud_texture");
	private static readonly StringName _paramShroudWorldMin = new("shroud_world_min");
	private static readonly StringName _paramShroudWorldSize = new("shroud_world_size");
	private static readonly StringName _paramShroudEnabled = new("shroud_enabled");
	private static readonly StringName _paramProcAnimParams1 = new("proc_anim_params1");
	private static readonly StringName _paramProcAnimParams2 = new("proc_anim_params2");
	private static readonly StringName _paramProcAnimParams3 = new("proc_anim_params3");
	private static readonly StringName _paramProcAnimImpulse = new("proc_anim_impulse");
	private static readonly StringName _paramProcAnimVelocity = new("proc_anim_velocity");

	private static Texture2D _currentShroudTexture;
	private static Vector2 _currentShroudWorldMin = new(-125.0f, -125.0f);
	private static Vector2 _currentShroudWorldSize = new(250.0f, 250.0f);
	private static bool _currentShroudEnabled = false;

	private const string ShaderPath = "res://Assets/shaders/player_color_spatial.gdshader";

	static ModelShaderManager()
	{
		for (int i = 0; i < 256; i++)
		{
			float srgb = i / 255.0f;
			SrgbToLinearLut[i] = srgb <= 0.04045f ? srgb / 12.92f : MathF.Pow((srgb + 0.055f) / 1.055f, 2.4f);
		}
	}

	public static Shader GetOrCreateShader()
	{
		if (_sharedShader != null && GodotObject.IsInstanceValid(_sharedShader))
		{
			return _sharedShader;
		}

		_sharedShader = GD.Load<Shader>(ShaderPath);
		return _sharedShader;
	}

	public static Texture2D GetOrCreateNormalizedAlbedoTexture(Texture2D sourceTexture, float targetLinearLuminance = 0.22f, float minScaleFactor = 0.2f, float maxScaleFactor = 8.0f)
	{
		if (sourceTexture == null) return null;
		ulong id = sourceTexture.GetInstanceId();
		if (_normalizedAlbedoCache.TryGetValue(id, out var cached) && GodotObject.IsInstanceValid(cached))
		{
			return cached;
		}

		var normalized = NormalizeAlbedoTexture(sourceTexture, targetLinearLuminance, minScaleFactor, maxScaleFactor);
		_normalizedAlbedoCache[id] = normalized;
		return normalized;
	}

	public static Texture2D NormalizeAlbedoTexture(Texture2D sourceTexture, float targetLinearLuminance = 0.22f, float minScaleFactor = 0.2f, float maxScaleFactor = 8.0f)
	{
		if (sourceTexture == null) return null;
		Image img = sourceTexture.GetImage();
		if (img == null) return sourceTexture;

		Image normalizedImg = NormalizeAlbedoImage(img, targetLinearLuminance, minScaleFactor, maxScaleFactor);
		if (normalizedImg == img)
		{
			return sourceTexture;
		}

		return ImageTexture.CreateFromImage(normalizedImg);
	}

	private static Image PrepareWorkingImage(Image sourceImage, out Image.Format fmt)
	{
		Image workingImage = (Image)sourceImage.Duplicate();
		if (workingImage.IsCompressed())
		{
			workingImage.Decompress();
		}

		if (workingImage.HasMipmaps())
		{
			workingImage.ClearMipmaps();
		}

		fmt = workingImage.GetFormat();
		if (fmt != Image.Format.Rgba8 && fmt != Image.Format.Rgb8)
		{
			workingImage.Convert(Image.Format.Rgba8);
			fmt = Image.Format.Rgba8;
		}

		return workingImage;
	}

	private static double CalculateTotalLinearLuminance(byte[] data, int stride, int channels, bool allowBlackPixels, out long validPixelCount)
	{
		double totalLinearLuminance = 0.0;
		validPixelCount = 0;

		for (int i = 0; i < data.Length; i += stride)
		{
			byte r = data[i];
			byte g = data[i + 1];
			byte b = data[i + 2];
			byte a = channels >= 4 ? data[i + 3] : (byte)255;

			if (a < 13) continue;
			if (!allowBlackPixels && r == 0 && g == 0 && b == 0) continue;

			float rLin = SrgbToLinearLut[r];
			float gLin = SrgbToLinearLut[g];
			float bLin = SrgbToLinearLut[b];

			totalLinearLuminance += (0.2126f * rLin) + (0.7152f * gLin) + (0.0722f * bLin);
			validPixelCount++;
		}

		return totalLinearLuminance;
	}

	private static Image ApplyLuminanceScaleFactor(byte[] data, int channels, float scaleFactor, int w, int h, Image.Format fmt)
	{
		byte[] resultData = new byte[data.Length];
		for (int i = 0; i < data.Length; i += channels)
		{
			byte r = data[i];
			byte g = data[i + 1];
			byte b = data[i + 2];

			float rLin = SrgbToLinearLut[r] * scaleFactor;
			float gLin = SrgbToLinearLut[g] * scaleFactor;
			float bLin = SrgbToLinearLut[b] * scaleFactor;

			resultData[i] = LinearToSrgbByte(rLin);
			resultData[i + 1] = LinearToSrgbByte(gLin);
			resultData[i + 2] = LinearToSrgbByte(bLin);

			if (channels >= 4)
			{
				resultData[i + 3] = data[i + 3];
			}
		}

		Image result = Image.CreateFromData(w, h, false, fmt, resultData);
		result.GenerateMipmaps();

		return result;
	}

	public static Image NormalizeAlbedoImage(Image sourceImage, float targetLinearLuminance = 0.22f, float minScaleFactor = 0.2f, float maxScaleFactor = 8.0f)
	{
		if (sourceImage == null) return null;

		Image workingImage = PrepareWorkingImage(sourceImage, out Image.Format fmt);
		int w = workingImage.GetWidth();
		int h = workingImage.GetHeight();
		byte[] data = workingImage.GetData();
		int channels = fmt == Image.Format.Rgba8 ? 4 : 3;

		int step = (w * h > 262144) ? 4 : 1;
		int stride = channels * step;

		double totalLinearLuminance = CalculateTotalLinearLuminance(data, stride, channels, false, out long validPixelCount);

		if (validPixelCount == 0)
		{
			totalLinearLuminance = CalculateTotalLinearLuminance(data, stride, channels, true, out validPixelCount);
		}

		if (validPixelCount == 0) return sourceImage;

		float avgLuminance = (float)(totalLinearLuminance / validPixelCount);
		if (avgLuminance <= 0.0001f) return sourceImage;

		float rawScaleFactor = targetLinearLuminance / avgLuminance;
		float scaleFactor = Mathf.Clamp(rawScaleFactor, minScaleFactor, maxScaleFactor);

		if (MathF.Abs(scaleFactor - 1.0f) < 0.01f)
		{
			return sourceImage;
		}

		return ApplyLuminanceScaleFactor(data, channels, scaleFactor, w, h, fmt);
	}

	[System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
	private static byte LinearToSrgbByte(float lin)
	{
		int idx = (int)(lin * 65535.0f);
		if ((uint)idx >= 65536)
		{
			return lin <= 0.0f ? (byte)0 : (byte)255;
		}
		return LinearToSrgbLut[idx];
	}

	private static bool EvaluateMaskPresence(byte[] data, int stride)
	{
		int maskCount = 0;
		int unmaskCount = 0;

		for (int i = 0; i < data.Length; i += stride)
		{
			if (data[i] > 32)
			{
				maskCount++;
			}
			else
			{
				unmaskCount++;
			}
		}

		return maskCount > 5 && unmaskCount > 5;
	}

	private static bool UpdatePlayerMaskCache(ulong ormId, bool hasMask)
	{
		_playerMaskCheckCache[ormId] = hasMask;
		return hasMask;
	}

	public static bool CheckHasPlayerMask(Texture2D ormTexture, Material sourceMaterial)
	{
		if (ormTexture == null) return false;

		ulong ormId = ormTexture.GetInstanceId();
		if (_playerMaskCheckCache.TryGetValue(ormId, out bool cached))
		{
			return cached;
		}

		Image img = ormTexture.GetImage();
		if (img == null) return UpdatePlayerMaskCache(ormId, false);

		Image workingImg = GetMaskWorkingImage(img, out Image.Format fmt);
		byte[] data = GetMaskDataFromImage(workingImg, fmt, out int stride);
		
		if (data == null) return UpdatePlayerMaskCache(ormId, false);

		bool hasMask = EvaluateMaskPresence(data, stride);
		return UpdatePlayerMaskCache(ormId, hasMask);
	}

	private static Image GetMaskWorkingImage(Image img, out Image.Format fmt)
	{
		Image workingImg = (Image)img.Duplicate();
		if (workingImg.IsCompressed()) workingImg.Decompress();
		if (workingImg.HasMipmaps()) workingImg.ClearMipmaps();

		fmt = workingImg.GetFormat();
		if (fmt != Image.Format.Rgba8 && fmt != Image.Format.Rgb8 && fmt != Image.Format.R8)
		{
			workingImg.Convert(Image.Format.Rgba8);
			fmt = Image.Format.Rgba8;
		}
		return workingImg;
	}

	private static byte[] GetMaskDataFromImage(Image workingImg, Image.Format fmt, out int stride)
	{
		int totalPixels = workingImg.GetWidth() * workingImg.GetHeight();
		if (totalPixels == 0)
		{
			stride = 0;
			return null;
		}

		int channels = fmt == Image.Format.Rgba8 ? 4 : (fmt == Image.Format.Rgb8 ? 3 : 1);
		int step = totalPixels > 262144 ? 4 : 1;
		stride = channels * step;

		return workingImg.GetData();
	}

	public static bool ModelHasPlayerMask(Node rootNode)
	{
		if (rootNode == null || !GodotObject.IsInstanceValid(rootNode)) return false;
		return ModelHasPlayerMaskRecursive(rootNode);
	}

	private static Texture2D GetOrmTextureFromMaterial(Material srcMat)
	{
		if (srcMat is OrmMaterial3D ormMat)
		{
			return ormMat.OrmTexture;
		}
		else if (srcMat is BaseMaterial3D baseMat)
		{
			return baseMat.RoughnessTexture ?? baseMat.MetallicTexture;
		}
		else if (srcMat is ShaderMaterial sm)
		{
			var ormVar = sm.GetShaderParameter("texture_orm");
			return ormVar.VariantType != Variant.Type.Nil ? ormVar.As<Texture2D>() : null;
		}
		return null;
	}

	private static bool CheckMeshInstanceForPlayerMask(MeshInstance3D meshInst)
	{
		if (IsExcludedMesh(meshInst)) return false;

		if (CheckSurfaceMaterialForPlayerMask(meshInst)) return true;
		if (CheckOverrideMaterialForPlayerMask(meshInst)) return true;

		return false;
	}

	private static bool CheckSurfaceMaterialForPlayerMask(MeshInstance3D meshInst)
	{
		int surfaceCount = meshInst.Mesh != null ? meshInst.Mesh.GetSurfaceCount() : 1;
		for (int i = 0; i < surfaceCount; i++)
		{
			Material srcMat = meshInst.GetSurfaceOverrideMaterial(i);
			if (srcMat == null && meshInst.Mesh != null)
			{
				srcMat = meshInst.Mesh.SurfaceGetMaterial(i);
			}

			Texture2D ormTexture = GetOrmTextureFromMaterial(srcMat);
			if (ormTexture != null && CheckHasPlayerMask(ormTexture, srcMat)) return true;
		}
		return false;
	}

	private static bool CheckOverrideMaterialForPlayerMask(MeshInstance3D meshInst)
	{
		if (meshInst.MaterialOverride == null) return false;

		Texture2D ormTexture = GetOrmTextureFromMaterial(meshInst.MaterialOverride);
		return ormTexture != null && CheckHasPlayerMask(ormTexture, meshInst.MaterialOverride);
	}

	private static bool ModelHasPlayerMaskRecursive(Node node)
	{
		if (node is MeshInstance3D meshInst)
		{
			if (CheckMeshInstanceForPlayerMask(meshInst))
			{
				return true;
			}
		}

		foreach (var child in node.GetChildren())
		{
			if (child is Node childNode && !IsAttachmentNode(childNode) && ModelHasPlayerMaskRecursive(childNode))
			{
				return true;
			}
		}

		return false;
	}

	private struct MaterialParameters
	{
		public Texture2D RawAlbedoTexture;
		public Texture2D OrmTexture;
		public Texture2D NormalTexture;
		public Texture2D EmissionTexture;
		public Color AlbedoColor;
		public Color EmissionColor;
		public float EmissionEnergy;
		public float Roughness;
		public float Metallic;
		public float Specular;
		public Vector3 Uv1Scale;
		public Vector3 Uv1Offset;
		public bool UseAlphaBlend;
		public bool UseAlphaScissor;
		public float AlphaScissorThreshold;

		public static MaterialParameters Default => new MaterialParameters
		{
			AlbedoColor = new Color(1f, 1f, 1f, 1f),
			EmissionColor = new Color(0f, 0f, 0f, 1f),
			EmissionEnergy = 1f,
			Roughness = 1f,
			Metallic = 0f,
			Specular = 0.5f,
			Uv1Scale = Vector3.One,
			Uv1Offset = Vector3.Zero,
			AlphaScissorThreshold = 0.5f
		};
	}

	private static MaterialParameters ExtractBaseMaterialParameters(BaseMaterial3D baseMat)
	{
		var p = MaterialParameters.Default;
		p.RawAlbedoTexture = baseMat.AlbedoTexture;
		p.OrmTexture = baseMat is OrmMaterial3D ormMat ? ormMat.OrmTexture : (baseMat.RoughnessTexture ?? baseMat.MetallicTexture);
		p.NormalTexture = baseMat.NormalEnabled ? baseMat.NormalTexture : null;
		p.EmissionTexture = baseMat.EmissionEnabled ? baseMat.EmissionTexture : null;
		p.AlbedoColor = baseMat.AlbedoColor;
		p.EmissionColor = baseMat.EmissionEnabled ? baseMat.Emission : new Color(0f, 0f, 0f, 1f);
		p.EmissionEnergy = baseMat.EmissionEnabled ? baseMat.EmissionEnergyMultiplier : 1f;
		p.Roughness = baseMat.Roughness;
		p.Metallic = baseMat.Metallic;
		p.Specular = baseMat.MetallicSpecular;
		p.Uv1Scale = baseMat.Uv1Scale;
		p.Uv1Offset = baseMat.Uv1Offset;

		if (baseMat.Transparency == BaseMaterial3D.TransparencyEnum.Alpha || baseMat.Transparency == BaseMaterial3D.TransparencyEnum.AlphaDepthPrePass)
		{
			p.UseAlphaBlend = true;
		}
		else if (baseMat.Transparency == BaseMaterial3D.TransparencyEnum.AlphaScissor)
		{
			p.UseAlphaScissor = true;
			p.AlphaScissorThreshold = baseMat.AlphaScissorThreshold;
		}

		return p;
	}

	private static MaterialParameters ExtractShaderMaterialParameters(ShaderMaterial sm)
	{
		var p = MaterialParameters.Default;
		p.RawAlbedoTexture = GetShaderTexture(sm, "texture_albedo");
		p.OrmTexture = GetShaderTexture(sm, "texture_orm");
		p.NormalTexture = GetShaderTexture(sm, "texture_normal");
		p.EmissionTexture = GetShaderTexture(sm, "texture_emission");
		
		p.AlbedoColor = GetShaderColor(sm, "albedo_color", p.AlbedoColor);
		p.EmissionColor = GetShaderColor(sm, "emission_color", p.EmissionColor);
		
		p.EmissionEnergy = GetShaderFloat(sm, "emission_energy", p.EmissionEnergy);
		p.Roughness = GetShaderFloat(sm, "roughness_value", p.Roughness);
		p.Metallic = GetShaderFloat(sm, "metallic_value", p.Metallic);
		p.Specular = GetShaderFloat(sm, "specular_value", p.Specular);
		p.AlphaScissorThreshold = GetShaderFloat(sm, "alpha_scissor_threshold", p.AlphaScissorThreshold);
		
		p.Uv1Scale = GetShaderVector3(sm, "uv1_scale", p.Uv1Scale);
		p.Uv1Offset = GetShaderVector3(sm, "uv1_offset", p.Uv1Offset);
		
		p.UseAlphaBlend = GetShaderBool(sm, "use_alpha_blend", p.UseAlphaBlend);
		p.UseAlphaScissor = GetShaderBool(sm, "use_alpha_scissor", p.UseAlphaScissor);

		return p;
	}

	private static Texture2D GetShaderTexture(ShaderMaterial sm, string paramName)
	{
		var variant = sm.GetShaderParameter(paramName);
		return variant.VariantType != Variant.Type.Nil ? variant.As<Texture2D>() : null;
	}

	private static Color GetShaderColor(ShaderMaterial sm, string paramName, Color defaultValue)
	{
		var variant = sm.GetShaderParameter(paramName);
		return variant.VariantType != Variant.Type.Nil ? variant.As<Color>() : defaultValue;
	}

	private static float GetShaderFloat(ShaderMaterial sm, string paramName, float defaultValue)
	{
		var variant = sm.GetShaderParameter(paramName);
		return variant.VariantType != Variant.Type.Nil ? variant.As<float>() : defaultValue;
	}

	private static Vector3 GetShaderVector3(ShaderMaterial sm, string paramName, Vector3 defaultValue)
	{
		var variant = sm.GetShaderParameter(paramName);
		return variant.VariantType != Variant.Type.Nil ? variant.As<Vector3>() : defaultValue;
	}

	private static bool GetShaderBool(ShaderMaterial sm, string paramName, bool defaultValue)
	{
		var variant = sm.GetShaderParameter(paramName);
		return variant.VariantType != Variant.Type.Nil ? variant.As<bool>() : defaultValue;
	}

	private static MaterialParameters ExtractMaterialParameters(Material sourceMaterial)
	{
		if (sourceMaterial is BaseMaterial3D baseMat)
		{
			return ExtractBaseMaterialParameters(baseMat);
		}
		else if (sourceMaterial is ShaderMaterial sm)
		{
			return ExtractShaderMaterialParameters(sm);
		}
		return MaterialParameters.Default;
	}

	private static void ApplyMaterialParametersToShader(ShaderMaterial material, MaterialParameters p, Texture2D albedoTexture, bool hasPlayerMask)
	{
		if (albedoTexture != null)
		{
			material.SetShaderParameter("texture_albedo", albedoTexture);
		}

		if (p.OrmTexture != null)
		{
			material.SetShaderParameter("texture_orm", p.OrmTexture);
			material.SetShaderParameter("has_orm_texture", true);
			material.SetShaderParameter("has_player_mask", hasPlayerMask);
		}
		else
		{
			material.SetShaderParameter("has_orm_texture", false);
			material.SetShaderParameter("has_player_mask", false);
		}

		if (p.NormalTexture != null)
		{
			material.SetShaderParameter("texture_normal", p.NormalTexture);
			material.SetShaderParameter("has_normal_texture", true);
		}
		else
		{
			material.SetShaderParameter("has_normal_texture", false);
		}

		if (p.EmissionTexture != null)
		{
			material.SetShaderParameter("texture_emission", p.EmissionTexture);
			material.SetShaderParameter("has_emission_texture", true);
		}
		else
		{
			material.SetShaderParameter("has_emission_texture", false);
		}

		material.SetShaderParameter("use_alpha_blend", p.UseAlphaBlend);
		material.SetShaderParameter("use_alpha_scissor", p.UseAlphaScissor);
		material.SetShaderParameter("alpha_scissor_threshold", p.AlphaScissorThreshold);

		material.SetShaderParameter("albedo_color", p.AlbedoColor);
		material.SetShaderParameter("emission_color", p.EmissionColor);
		material.SetShaderParameter("emission_energy", p.EmissionEnergy);
		material.SetShaderParameter("roughness_value", p.Roughness);
		material.SetShaderParameter("metallic_value", p.Metallic);
		material.SetShaderParameter("specular_value", p.Specular);
		material.SetShaderParameter("uv1_scale", p.Uv1Scale);
		material.SetShaderParameter("uv1_offset", p.Uv1Offset);

		if (_currentShroudTexture != null)
		{
			material.SetShaderParameter(_paramShroudTexture, _currentShroudTexture);
		}
		material.SetShaderParameter(_paramShroudWorldMin, _currentShroudWorldMin);
		material.SetShaderParameter(_paramShroudWorldSize, _currentShroudWorldSize);
		material.SetShaderParameter(_paramShroudEnabled, _currentShroudEnabled);
	}

	public static ShaderMaterial GetOrCreateShaderMaterial(Material sourceMaterial, bool normalizeLuminance = true)
	{
		var shader = GetOrCreateShader();

		MaterialParameters p = ExtractMaterialParameters(sourceMaterial);

		Texture2D albedoTexture = normalizeLuminance ? GetOrCreateNormalizedAlbedoTexture(p.RawAlbedoTexture) : p.RawAlbedoTexture;
		bool hasPlayerMask = CheckHasPlayerMask(p.OrmTexture, sourceMaterial);

		ulong rawAlbedoId = p.RawAlbedoTexture != null ? p.RawAlbedoTexture.GetInstanceId() : 0;
		ulong ormId = p.OrmTexture != null ? p.OrmTexture.GetInstanceId() : 0;
		ulong normalId = p.NormalTexture != null ? p.NormalTexture.GetInstanceId() : 0;
		ulong emissionId = p.EmissionTexture != null ? p.EmissionTexture.GetInstanceId() : 0;

		string key = $"{rawAlbedoId}_{normalizeLuminance}_{ormId}_{hasPlayerMask}_{normalId}_{emissionId}_{p.AlbedoColor.ToHtml()}_{p.EmissionColor.ToHtml()}_{p.EmissionEnergy:F2}_{p.Roughness:F2}_{p.Metallic:F2}_{p.Specular:F2}_{p.Uv1Scale.X:F2}_{p.Uv1Scale.Y:F2}_{p.Uv1Offset.X:F2}_{p.Uv1Offset.Y:F2}_{p.UseAlphaBlend}_{p.UseAlphaScissor}_{p.AlphaScissorThreshold:F2}";

		if (_materialCache.TryGetValue(key, out var cached) && GodotObject.IsInstanceValid(cached))
		{
			return cached;
		}

		var material = new ShaderMaterial
		{
			Shader = shader
		};

		ApplyMaterialParametersToShader(material, p, albedoTexture, hasPlayerMask);

		_materialCache[key] = material;
		return material;
	}

	public static bool IsAttachmentNode(Node node)
	{
		if (node == null || !GodotObject.IsInstanceValid(node)) return false;
		if (node.HasMeta("AttachmentId") || node.HasMeta("CleanAttachmentId")) return true;
		string nodeName = node.Name.ToString();
		return nodeName.StartsWith("Att_", StringComparison.OrdinalIgnoreCase)
			|| nodeName.StartsWith("AttVisual_", StringComparison.OrdinalIgnoreCase)
			|| nodeName.StartsWith("BoneAttachment_", StringComparison.OrdinalIgnoreCase)
			|| nodeName.StartsWith("SocketAttachment_", StringComparison.OrdinalIgnoreCase);
	}

	private static bool IsExcludedMesh(GeometryInstance3D geomInst)
	{
		if (geomInst == null) return true;
		string nodeName = geomInst.Name.ToString();
		return nodeName.StartsWith("_selection", StringComparison.OrdinalIgnoreCase)
			|| nodeName.StartsWith("Selection", StringComparison.OrdinalIgnoreCase)
			|| nodeName.StartsWith("_hover", StringComparison.OrdinalIgnoreCase)
			|| nodeName.StartsWith("Hover", StringComparison.OrdinalIgnoreCase)
			|| nodeName.StartsWith("BrushIndicator", StringComparison.OrdinalIgnoreCase)
			|| nodeName.StartsWith("DropShadow", StringComparison.OrdinalIgnoreCase)
			|| nodeName.Contains("SelectionRing", StringComparison.OrdinalIgnoreCase)
			|| nodeName.Contains("HoverRing", StringComparison.OrdinalIgnoreCase);
	}

	public static void ApplyPlayerColorShader(Node rootNode, Color playerColor, bool ignorePlayerColor = false, bool normalizeLuminance = true, bool isUnitOrBuilding = true)
	{
		if (rootNode == null || !GodotObject.IsInstanceValid(rootNode)) return;
		ApplyPlayerColorShaderRecursive(rootNode, playerColor, ignorePlayerColor, normalizeLuminance, isUnitOrBuilding);
	}

	private static void ApplyPlayerColorShaderToMesh(MeshInstance3D meshInst, Color playerColor, bool ignorePlayerColor, bool normalizeLuminance, bool isUnitOrBuilding)
	{
		if (IsExcludedMesh(meshInst)) return;

		ApplyPlayerColorShaderToSurfaces(meshInst, normalizeLuminance);
		ApplyPlayerColorShaderToOverrideMaterial(meshInst, normalizeLuminance);

		meshInst.SetInstanceShaderParameter(_paramPlayerColor, playerColor);
		meshInst.SetInstanceShaderParameter(_paramIgnorePlayerColor, ignorePlayerColor ? 1.0f : 0.0f);
		meshInst.SetInstanceShaderParameter(_paramUnitAmbientBoost, isUnitOrBuilding ? 0.10f : 0.0f);
		meshInst.SetInstanceShaderParameter(_paramUnitRimIntensity, isUnitOrBuilding ? 0.25f : 0.0f);
	}

	private static void ApplyPlayerColorShaderToSurfaces(MeshInstance3D meshInst, bool normalizeLuminance)
	{
		int surfaceCount = meshInst.Mesh != null ? meshInst.Mesh.GetSurfaceCount() : 1;
		for (int i = 0; i < surfaceCount; i++)
		{
			Material srcMat = meshInst.GetSurfaceOverrideMaterial(i);
			if (srcMat == null && meshInst.Mesh != null)
			{
				srcMat = meshInst.Mesh.SurfaceGetMaterial(i);
			}

			if (srcMat is ShaderMaterial sm && sm.Shader == _sharedShader) continue;

			if (srcMat is BaseMaterial3D || srcMat is ShaderMaterial || srcMat == null)
			{
				var shaderMat = GetOrCreateShaderMaterial(srcMat, normalizeLuminance);
				meshInst.SetSurfaceOverrideMaterial(i, shaderMat);
			}
		}
	}

	private static void ApplyPlayerColorShaderToOverrideMaterial(MeshInstance3D meshInst, bool normalizeLuminance)
	{
		if (meshInst.MaterialOverride == null) return;
		if (meshInst.MaterialOverride is ShaderMaterial smOver && smOver.Shader == _sharedShader) return;

		var shaderMat = GetOrCreateShaderMaterial(meshInst.MaterialOverride, normalizeLuminance);
		meshInst.MaterialOverride = shaderMat;
	}

	private static void ApplyPlayerColorShaderRecursive(Node node, Color playerColor, bool ignorePlayerColor = false, bool normalizeLuminance = true, bool isUnitOrBuilding = true)
	{
		if (node is MeshInstance3D meshInst)
		{
			ApplyPlayerColorShaderToMesh(meshInst, playerColor, ignorePlayerColor, normalizeLuminance, isUnitOrBuilding);
		}

		foreach (var child in node.GetChildren())
		{
			if (child is Node childNode && !IsAttachmentNode(childNode))
			{
				ApplyPlayerColorShaderRecursive(childNode, playerColor, ignorePlayerColor, normalizeLuminance, isUnitOrBuilding);
			}
		}
	}

	public static void SetUnitReadability(Node rootNode, bool isUnitOrBuilding)
	{
		if (rootNode == null || !GodotObject.IsInstanceValid(rootNode)) return;
		SetUnitReadabilityRecursive(rootNode, isUnitOrBuilding ? 0.10f : 0.0f, isUnitOrBuilding ? 0.25f : 0.0f);
	}

	private static void SetUnitReadabilityRecursive(Node node, float ambientBoost, float rimIntensity)
	{
		if (node is GeometryInstance3D geomInst)
		{
			if (!IsExcludedMesh(geomInst))
			{
				geomInst.SetInstanceShaderParameter(_paramUnitAmbientBoost, ambientBoost);
				geomInst.SetInstanceShaderParameter(_paramUnitRimIntensity, rimIntensity);
			}
		}

		foreach (var child in node.GetChildren())
		{
			if (child is Node childNode && !IsAttachmentNode(childNode))
			{
				SetUnitReadabilityRecursive(childNode, ambientBoost, rimIntensity);
			}
		}
	}

	public static void RefreshShaderMaterialsForNode(Node rootNode, bool normalizeLuminance = true)
	{
		if (rootNode == null || !GodotObject.IsInstanceValid(rootNode)) return;
		RefreshShaderMaterialsRecursive(rootNode, normalizeLuminance);
	}

	private static void RefreshMeshInstanceMaterials(MeshInstance3D meshInst, bool normalizeLuminance)
	{
		if (IsExcludedMesh(meshInst)) return;

		int surfaceCount = meshInst.Mesh != null ? meshInst.Mesh.GetSurfaceCount() : 1;
		for (int i = 0; i < surfaceCount; i++)
		{
			Material srcMat = meshInst.Mesh != null ? meshInst.Mesh.SurfaceGetMaterial(i) : null;
			if (srcMat == null) srcMat = meshInst.GetSurfaceOverrideMaterial(i);
			if (srcMat != null)
			{
				var shaderMat = GetOrCreateShaderMaterial(srcMat, normalizeLuminance);
				meshInst.SetSurfaceOverrideMaterial(i, shaderMat);
			}
		}

		if (meshInst.MaterialOverride != null)
		{
			var shaderMat = GetOrCreateShaderMaterial(meshInst.MaterialOverride, normalizeLuminance);
			meshInst.MaterialOverride = shaderMat;
		}
	}

	private static void RefreshShaderMaterialsRecursive(Node node, bool normalizeLuminance)
	{
		if (node is MeshInstance3D meshInst)
		{
			RefreshMeshInstanceMaterials(meshInst, normalizeLuminance);
		}

		foreach (var child in node.GetChildren())
		{
			if (child is Node childNode)
			{
				RefreshShaderMaterialsRecursive(childNode, normalizeLuminance);
			}
		}
	}

	public static void SetPlayerColor(Node rootNode, Color playerColor)
	{
		if (rootNode == null || !GodotObject.IsInstanceValid(rootNode)) return;
		SetPlayerColorRecursive(rootNode, playerColor);
	}

	private static void SetPlayerColorRecursive(Node node, Color playerColor)
	{
		if (node is GeometryInstance3D geomInst)
		{
			if (!IsExcludedMesh(geomInst))
			{
				geomInst.SetInstanceShaderParameter(_paramPlayerColor, playerColor);
			}
		}

		foreach (var child in node.GetChildren())
		{
			if (child is Node childNode && !IsAttachmentNode(childNode))
			{
				SetPlayerColorRecursive(childNode, playerColor);
			}
		}
	}

	public static void SetIgnorePlayerColor(Node rootNode, bool ignorePlayerColor)
	{
		if (rootNode == null || !GodotObject.IsInstanceValid(rootNode)) return;
		SetIgnorePlayerColorRecursive(rootNode, ignorePlayerColor);
	}

	private static void SetIgnorePlayerColorRecursive(Node node, bool ignorePlayerColor)
	{
		if (node is GeometryInstance3D geomInst)
		{
			if (!IsExcludedMesh(geomInst))
			{
				geomInst.SetInstanceShaderParameter(_paramIgnorePlayerColor, ignorePlayerColor ? 1.0f : 0.0f);
			}
		}

		foreach (var child in node.GetChildren())
		{
			if (child is Node childNode && !IsAttachmentNode(childNode))
			{
				SetIgnorePlayerColorRecursive(childNode, ignorePlayerColor);
			}
		}
	}

	public static void SetBrightnessAndTint(Node rootNode, float brightness, Color tint)
	{
		if (rootNode == null || !GodotObject.IsInstanceValid(rootNode)) return;
		if (MathF.Abs(brightness - 1.0f) < 0.001f && tint == new Color(1.0f, 1.0f, 1.0f)) return;
		SetBrightnessAndTintRecursive(rootNode, brightness, tint);
	}

	private static void SetBrightnessAndTintRecursive(Node node, float brightness, Color tint)
	{
		if (node is GeometryInstance3D geomInst)
		{
			if (!IsExcludedMesh(geomInst))
			{
				geomInst.SetInstanceShaderParameter(_paramModelBrightness, brightness);
				geomInst.SetInstanceShaderParameter(_paramModelColorTint, tint);
			}
		}

		foreach (var child in node.GetChildren())
		{
			if (child is Node childNode)
			{
				SetBrightnessAndTintRecursive(childNode, brightness, tint);
			}
		}
	}


	public static void ApplyBrightnessAndTintToAlbedoImage(Image img, float brightness, Color tint)
	{
		if (img == null) return;
		if (MathF.Abs(brightness - 1.0f) <= 0.001f && MathF.Abs(tint.R - 1.0f) <= 0.001f && MathF.Abs(tint.G - 1.0f) <= 0.001f && MathF.Abs(tint.B - 1.0f) <= 0.001f)
		{
			return;
		}

		if (img.GetFormat() != Image.Format.Rgba8)
		{
			img.Convert(Image.Format.Rgba8);
		}

		byte[] data = img.GetData();
		float multR = brightness * tint.R;
		float multG = brightness * tint.G;
		float multB = brightness * tint.B;

		for (int i = 0; i < data.Length; i += 4)
		{
			data[i]     = (byte)Math.Clamp((int)MathF.Round(data[i]     * multR), 0, 255);
			data[i + 1] = (byte)Math.Clamp((int)MathF.Round(data[i + 1] * multG), 0, 255);
			data[i + 2] = (byte)Math.Clamp((int)MathF.Round(data[i + 2] * multB), 0, 255);
		}

		img.SetData(img.GetWidth(), img.GetHeight(), false, Image.Format.Rgba8, data);
	}

	public static void SetShroudParameters(Texture2D texture, Vector2 worldMin, Vector2 worldSize, bool enabled)
	{
		_currentShroudTexture = texture;
		_currentShroudWorldMin = worldMin;
		_currentShroudWorldSize = worldSize;
		_currentShroudEnabled = enabled;

		foreach (var material in _materialCache.Values)
		{
			if (GodotObject.IsInstanceValid(material))
			{
				if (texture != null)
				{
					material.SetShaderParameter(_paramShroudTexture, texture);
				}
				material.SetShaderParameter(_paramShroudWorldMin, worldMin);
				material.SetShaderParameter(_paramShroudWorldSize, worldSize);
				material.SetShaderParameter(_paramShroudEnabled, enabled);
			}
		}
	}

	public static void SetHideInShroud(Node node, bool hideInShroud)
	{
		if (node == null || !GodotObject.IsInstanceValid(node)) return;
		float val = hideInShroud ? 1.0f : 0.0f;
		if (node is GeometryInstance3D geom)
		{
			geom.SetInstanceShaderParameter(_paramHideInShroud, val);
		}
		int childCount = node.GetChildCount();
		for (int i = 0; i < childCount; i++)
		{
			SetHideInShroud(node.GetChild(i), hideInShroud);
		}
	}

	public static void SetProceduralAnimation(Node rootNode, ProceduralAnimationConfig? config, Vector3 velocity = default, float impulseStrength = 0f, float impulseTime = 0f, bool showDebugMask = false)
	{
		if (rootNode == null || !GodotObject.IsInstanceValid(rootNode)) return;
		if (config == null)
		{
			DisableProceduralAnimation(rootNode);
			return;
		}

		float signedPower = config.MaskInvert ? -MathF.Abs(config.MaskPower) : MathF.Abs(config.MaskPower);
		Vector4 p1 = new Vector4((float)config.MotionType, (float)config.MaskMode, config.MaskMin, config.MaskMax);
		Vector4 p2 = new Vector4(signedPower, config.SwayFrequency, config.SwayAmplitude, config.FlutterFrequency);
		Vector4 p3 = new Vector4(config.FlutterAmplitude, config.WindInfluence, config.VelocityDragInfluence, config.WaveTurbulence);
		Vector4 pImp = new Vector4(impulseStrength, config.ImpulseFrequency, impulseTime, showDebugMask ? 1.0f : 0.0f);

		SetProceduralAnimationRecursive(rootNode, p1, p2, p3, pImp, velocity);
	}

	private static void SetProceduralAnimationRecursive(Node node, Vector4 p1, Vector4 p2, Vector4 p3, Vector4 pImp, Vector3 velocity)
	{
		if (node is GeometryInstance3D geom && !IsExcludedMesh(geom))
		{
			geom.SetInstanceShaderParameter(_paramProcAnimParams1, p1);
			geom.SetInstanceShaderParameter(_paramProcAnimParams2, p2);
			geom.SetInstanceShaderParameter(_paramProcAnimParams3, p3);
			geom.SetInstanceShaderParameter(_paramProcAnimImpulse, pImp);
			geom.SetInstanceShaderParameter(_paramProcAnimVelocity, velocity);
		}

		int count = node.GetChildCount();
		for (int i = 0; i < count; i++)
		{
			SetProceduralAnimationRecursive(node.GetChild(i), p1, p2, p3, pImp, velocity);
		}
	}

	public static void SetProceduralAnimationImpulse(Node rootNode, float impulseStrength, float impulseFrequency, float impulseTime, bool showDebugMask = false)
	{
		if (rootNode == null || !GodotObject.IsInstanceValid(rootNode)) return;
		Vector4 pImp = new Vector4(impulseStrength, impulseFrequency, impulseTime, showDebugMask ? 1.0f : 0.0f);
		SetProceduralAnimationImpulseRecursive(rootNode, pImp);
	}

	private static void SetProceduralAnimationImpulseRecursive(Node node, Vector4 pImp)
	{
		if (node is GeometryInstance3D geom && !IsExcludedMesh(geom))
		{
			geom.SetInstanceShaderParameter(_paramProcAnimImpulse, pImp);
		}

		int count = node.GetChildCount();
		for (int i = 0; i < count; i++)
		{
			SetProceduralAnimationImpulseRecursive(node.GetChild(i), pImp);
		}
	}

	public static void SetProceduralAnimationVelocity(Node rootNode, Vector3 velocity)
	{
		if (rootNode == null || !GodotObject.IsInstanceValid(rootNode)) return;
		SetProceduralAnimationVelocityRecursive(rootNode, velocity);
	}

	private static void SetProceduralAnimationVelocityRecursive(Node node, Vector3 velocity)
	{
		if (node is GeometryInstance3D geom && !IsExcludedMesh(geom))
		{
			geom.SetInstanceShaderParameter(_paramProcAnimVelocity, velocity);
		}

		int count = node.GetChildCount();
		for (int i = 0; i < count; i++)
		{
			SetProceduralAnimationVelocityRecursive(node.GetChild(i), velocity);
		}
	}

	public static void DisableProceduralAnimation(Node rootNode)
	{
		if (rootNode == null || !GodotObject.IsInstanceValid(rootNode)) return;
		Vector4 pZero = Vector4.Zero;
		Vector3 vZero = Vector3.Zero;
		SetProceduralAnimationRecursive(rootNode, pZero, pZero, pZero, pZero, vZero);
	}

	public static void ClearCache()
	{
		_materialCache.Clear();
		_normalizedAlbedoCache.Clear();
		_playerMaskCheckCache.Clear();
	}
}
