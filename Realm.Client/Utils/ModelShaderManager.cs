using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using Godot;

namespace Realm.Client.Utils;

public static partial class ModelShaderManager
{
	private static Shader? _sharedShader;
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
	private static readonly StringName _paramSilhouette = new("silhouette");
	private static readonly StringName _paramProcAnimParams1 = new("proc_anim_params1");
	private static readonly StringName _paramProcAnimParams2 = new("proc_anim_params2");
	private static readonly StringName _paramProcAnimParams3 = new("proc_anim_params3");
	private static readonly StringName _paramProcAnimImpulse = new("proc_anim_impulse");
	private static readonly StringName _paramProcAnimVelocity = new("proc_anim_velocity");
	private static readonly StringName _paramProcAnimRegionMin = new("proc_anim_region_min");
	private static readonly StringName _paramProcAnimRegionMax = new("proc_anim_region_max");
	private static readonly StringName _paramTextureAlbedo = new("texture_albedo");
	private static readonly StringName _paramTextureOrm = new("texture_orm");
	private static readonly StringName _paramHasOrmTexture = new("has_orm_texture");
	private static readonly StringName _paramHasPlayerMask = new("has_player_mask");
	private static readonly StringName _paramTextureNormal = new("texture_normal");
	private static readonly StringName _paramHasNormalTexture = new("has_normal_texture");
	private static readonly StringName _paramTextureEmission = new("texture_emission");
	private static readonly StringName _paramHasEmissionTexture = new("has_emission_texture");
	private static readonly StringName _paramUseAlphaBlend = new("use_alpha_blend");
	private static readonly StringName _paramUseAlphaScissor = new("use_alpha_scissor");
	private static readonly StringName _paramAlphaScissorThreshold = new("alpha_scissor_threshold");
	private static readonly StringName _paramAlbedoColor = new("albedo_color");
	private static readonly StringName _paramEmissionColor = new("emission_color");
	private static readonly StringName _paramEmissionEnergy = new("emission_energy");
	private static readonly StringName _paramRoughnessValue = new("roughness_value");
	private static readonly StringName _paramMetallicValue = new("metallic_value");
	private static readonly StringName _paramSpecularValue = new("specular_value");
	private static readonly StringName _paramUv1Scale = new("uv1_scale");
	private static readonly StringName _paramUv1Offset = new("uv1_offset");

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

	public static Texture2D? GetOrCreateNormalizedAlbedoTexture(Texture2D? sourceTexture, float targetLinearLuminance = 0.22f, float minScaleFactor = 0.2f, float maxScaleFactor = 8.0f)
	{
		if (sourceTexture == null) return null;
		ulong id = sourceTexture.GetInstanceId();
		if (_normalizedAlbedoCache.TryGetValue(id, out var cached) && GodotObject.IsInstanceValid(cached))
		{
			return cached;
		}

		var normalized = NormalizeAlbedoTexture(sourceTexture, targetLinearLuminance, minScaleFactor, maxScaleFactor);
		if (normalized != null)
		{
			_normalizedAlbedoCache[id] = normalized;
		}

		return normalized;
	}

	public static Texture2D? NormalizeAlbedoTexture(Texture2D? sourceTexture, float targetLinearLuminance = 0.22f, float minScaleFactor = 0.2f, float maxScaleFactor = 8.0f)
	{
		if (sourceTexture == null) return null;
		Image img = sourceTexture.GetImage();
		if (img == null) return sourceTexture;

		Image? normalizedImg = NormalizeAlbedoImage(img, targetLinearLuminance, minScaleFactor, maxScaleFactor);
		if (normalizedImg == null || normalizedImg == img)
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

}