using Godot;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json.Nodes;
using Realm.Godot.Utils;
using Realm.Shared.Metadata;
using Realm.Shared.Services;

namespace Realm.Godot.VFX;

public class VfxShaderManager
{
	private static Shader _shaderAdd;
	private static Shader _shaderMix;
	private static Shader _shaderProjectorAdd;
	private static Shader _shaderProjectorMix;
	private static readonly Dictionary<string, Texture2D> TextureCache = new(StringComparer.OrdinalIgnoreCase);
	private static readonly Dictionary<string, (int Columns, int Rows, float Fps, bool SubframeBlend)> SpritesheetMetaCache = new(StringComparer.OrdinalIgnoreCase);
	private static readonly object SyncLock = new();

	public static void ClearCache()
	{
		lock (SyncLock)
		{
			_shaderAdd = null;
			_shaderMix = null;
			_shaderProjectorAdd = null;
			_shaderProjectorMix = null;
			TextureCache.Clear();
			SpritesheetMetaCache.Clear();
		}
	}

	public static Shader GetShader(VfxBlendMode blendMode, VfxPrimitiveType primitiveType = VfxPrimitiveType.VortexDisc)
	{
		lock (SyncLock)
		{
			if (primitiveType == VfxPrimitiveType.ProjectedVolumeCube)
			{
				if (blendMode == VfxBlendMode.Additive)
				{
					if (_shaderProjectorAdd == null)
					{
						_shaderProjectorAdd = LoadShaderFromFile("res://Assets/shaders/vfx_projector_add.gdshader", "Assets/shaders/vfx_projector_add.gdshader");
					}
					return _shaderProjectorAdd;
				}
				else
				{
					if (_shaderProjectorMix == null)
					{
						_shaderProjectorMix = LoadShaderFromFile("res://Assets/shaders/vfx_projector_mix.gdshader", "Assets/shaders/vfx_projector_mix.gdshader");
					}
					return _shaderProjectorMix;
				}
			}

			if (blendMode == VfxBlendMode.Additive)
			{
				if (_shaderAdd == null)
				{
					_shaderAdd = LoadShaderFromFile("res://Assets/shaders/vfx_uber_add.gdshader", "Assets/shaders/vfx_uber_add.gdshader");
				}
				return _shaderAdd;
			}
			else
			{
				if (_shaderMix == null)
				{
					_shaderMix = LoadShaderFromFile("res://Assets/shaders/vfx_uber_mix.gdshader", "Assets/shaders/vfx_uber_mix.gdshader");
				}
				return _shaderMix;
			}
		}
	}

	private static Shader LoadShaderFromFile(string resPath, string fallbackRelPath)
	{
		string shaderCode = "";
		if (global::Godot.FileAccess.FileExists(resPath))
		{
			using var file = global::Godot.FileAccess.Open(resPath, global::Godot.FileAccess.ModeFlags.Read);
			shaderCode = file?.GetAsText() ?? "";
		}

		if (string.IsNullOrEmpty(shaderCode))
		{
			string[] candidatePaths = new[]
			{
				fallbackRelPath,
				Path.Combine("Realm.Godot", fallbackRelPath),
				Path.Combine(AppDomain.CurrentDomain.BaseDirectory, fallbackRelPath)
			};

			foreach (var path in candidatePaths)
			{
				if (File.Exists(path))
				{
					shaderCode = File.ReadAllText(path);
					break;
				}
			}
		}

		if (!string.IsNullOrEmpty(shaderCode))
		{
			return new Shader { Code = shaderCode };
		}

		return GD.Load<Shader>(resPath);
	}

	public static ShaderMaterial CreateMaterial(VfxAttachmentConfig config)
	{
		var material = new ShaderMaterial();
		material.Shader = GetShader(config.BlendMode, config.PrimitiveType);
		ApplyConfigToMaterial(material, config);
		return material;
	}

	public static void ApplyConfigToMaterial(ShaderMaterial material, VfxAttachmentConfig config)
	{
		if (material == null || config == null) return;

		var targetShader = GetShader(config.BlendMode, config.PrimitiveType);
		if (material.Shader != targetShader)
		{
			material.Shader = targetShader;
		}

		Texture2D baseTex = !string.IsNullOrEmpty(config.BaseTexture) ? LoadTextureSafe(config.BaseTexture) : null;
		material.SetShaderParameter("use_base_texture", baseTex != null);
		if (baseTex != null)
		{
			material.SetShaderParameter("base_texture", baseTex);
		}

		material.SetShaderParameter("base_uv_scroll", config.BaseUvScroll.ToGodotVector2());
		material.SetShaderParameter("base_uv_scale", config.BaseUvScale.ToGodotVector2());
		material.SetShaderParameter("use_flipbook", config.UseFlipbook);
		material.SetShaderParameter("flipbook_columns", Math.Max(1, config.FlipbookColumns));
		material.SetShaderParameter("flipbook_rows", Math.Max(1, config.FlipbookRows));
		material.SetShaderParameter("flipbook_fps", config.FlipbookFps > 0.001f ? config.FlipbookFps : 12.0f);
		material.SetShaderParameter("flipbook_subframe_blend", config.FlipbookSubframeBlend);

		material.SetShaderParameter("luminance_to_alpha", config.LuminanceToAlpha);
		material.SetShaderParameter("luminance_threshold", Mathf.Clamp(config.LuminanceThreshold, 0.0f, 1.0f));
		material.SetShaderParameter("luminance_smoothness", Mathf.Clamp(config.LuminanceSmoothness, 0.001f, 0.5f));
		material.SetShaderParameter("use_grayscale", config.UseGrayscale);
		material.SetShaderParameter("invert_mask", config.InvertMask);
		material.SetShaderParameter("high_pass_cutoff", Mathf.Clamp(config.HighPassCutoff, 0.0f, 1.0f));

		Texture2D noiseTex = !string.IsNullOrEmpty(config.NoiseTexture) ? LoadTextureSafe(config.NoiseTexture) : null;
		material.SetShaderParameter("use_noise_texture", noiseTex != null);
		if (noiseTex != null)
		{
			material.SetShaderParameter("noise_texture", noiseTex);
		}

		material.SetShaderParameter("noise_uv_scroll", config.NoiseUvScroll.ToGodotVector2());
		material.SetShaderParameter("noise_uv_scale", config.NoiseUvScale.ToGodotVector2());
		material.SetShaderParameter("noise_distortion_strength", Mathf.Clamp(config.DistortionStrength, 0.0f, 2.0f));

		material.SetShaderParameter("base_color", ParseColorSafe(config.BaseColor, new Color(1.0f, 0.45f, 0.1f, 1.0f)));
		material.SetShaderParameter("secondary_color", ParseColorSafe(config.SecondaryColor, new Color(0.8f, 0.1f, 0.0f, 1.0f)));
		material.SetShaderParameter("core_color", ParseColorSafe(config.CoreColor, new Color(1.0f, 0.95f, 0.8f, 1.0f)));
		material.SetShaderParameter("color_mix_ratio", Mathf.Clamp(config.ColorMixRatio, 0.0f, 1.0f));
		material.SetShaderParameter("emission_boost", Mathf.Clamp(config.EmissionBoost, 0.0f, 20.0f));
		material.SetShaderParameter("core_threshold", Mathf.Clamp(config.CoreThreshold, 0.0f, 1.0f));

		material.SetShaderParameter("enable_radial_falloff", config.EnableRadialFalloff);
		material.SetShaderParameter("radial_falloff_start", Mathf.Clamp(config.RadialFalloffStart, 0.0f, 1.0f));
		material.SetShaderParameter("radial_falloff_end", Mathf.Clamp(config.RadialFalloffEnd, 0.0f, 1.0f));

		material.SetShaderParameter("enable_length_fade", config.EnableLengthFade);
		material.SetShaderParameter("length_fade_start", Mathf.Clamp(config.LengthFadeStart, 0.0f, 1.0f));
		material.SetShaderParameter("length_fade_end", Mathf.Clamp(config.LengthFadeEnd, 0.0f, 1.0f));
		material.SetShaderParameter("erosion_progress", Mathf.Clamp(config.ErosionProgress, 0.0f, 1.0f));

		material.SetShaderParameter("enable_fresnel", config.EnableFresnel);
		material.SetShaderParameter("fresnel_power", Mathf.Clamp(config.FresnelPower, 0.1f, 10.0f));
		material.SetShaderParameter("fresnel_intensity", Mathf.Clamp(config.FresnelIntensity, 0.0f, 10.0f));

		material.SetShaderParameter("enable_depth_fade", config.EnableDepthFade);
		material.SetShaderParameter("depth_fade_distance", Mathf.Clamp(config.DepthFadeDistance, 0.0f, 5.0f));

		material.SetShaderParameter("surface_normal_offset", Mathf.Clamp(config.SurfaceNormalOffset, -0.5f, 0.5f));
	}

	public static Texture2D LoadTextureSafe(string path)
	{
		if (string.IsNullOrEmpty(path)) return null;

		lock (SyncLock)
		{
			if (TryGetCachedTexture(path, out var cached))
				return cached;

			var godotTex = LoadGodotTexture(path);
			if (godotTex != null)
				return godotTex;

			return LoadTextureFromCandidates(path);
		}
	}

	private static bool TryGetCachedTexture(string path, out Texture2D texture)
	{
		texture = null;
		if (!TextureCache.TryGetValue(path, out var cached)) return false;
		if (cached == null || !GodotObject.IsInstanceValid(cached)) return false;
		
		texture = cached;
		return true;
	}

	private static Texture2D LoadGodotTexture(string path)
	{
		if (!path.StartsWith("res://") && !path.StartsWith("user://")) return null;
		if (!ResourceLoader.Exists(path)) return null;
		
		var tex = GD.Load<Texture2D>(path);
		if (tex == null) return null;

		TextureCache[path] = tex;
		return tex;
	}

	private static Texture2D LoadTextureFromCandidates(string path)
	{
		string wsPath = ProjectSettings.GlobalizePath(MapEditorHUD.TempWorkspaceGodotPath);
		string[] candidates = GetTextureCandidates(path, wsPath);

		foreach (var candidate in candidates)
		{
			var loaded = TryLoadCandidate(path, candidate);
			if (loaded != null) return loaded;
		}

		return null;
	}

	private static string[] GetTextureCandidates(string path, string wsPath)
	{
		return new[]
		{
			path,
			Path.Combine(wsPath, path),
			Path.Combine(wsPath, "Assets", "textures", path),
			Path.Combine(wsPath, "Assets", "decals", path),
			Path.Combine(wsPath, "Assets", "ribbons", path),
			Path.Combine(wsPath, "Assets", "vfx", path),
			Path.Combine(wsPath, "Assets", "noise", path),
			Path.Combine("MapTemplate", path),
			Path.Combine("MapTemplate", "Assets", "textures", path),
			Path.Combine("MapTemplate", "Assets", "decals", path),
			Path.Combine("MapTemplate", "Assets", "ribbons", path),
			Path.Combine("MapTemplate", "Assets", "vfx", path),
			Path.Combine("MapTemplate", "Assets", "noise", path)
		};
	}

	private static Texture2D TryLoadCandidate(string path, string candidate)
	{
		string clean = GetExistingFilePath(candidate);
		if (string.IsNullOrEmpty(clean)) return null;

		var loaded = LoadTextureFromFile(clean);
		if (loaded == null) return null;

		TextureCache[path] = loaded;
		return loaded;
	}

	private static string GetExistingFilePath(string candidate)
	{
		if (File.Exists(candidate)) return candidate;

		string rtexPath = candidate + ".rtex";
		if (!candidate.EndsWith(".rtex", StringComparison.OrdinalIgnoreCase) && File.Exists(rtexPath))
			return rtexPath;

		string pngPath = candidate + ".png";
		if (!candidate.EndsWith(".png", StringComparison.OrdinalIgnoreCase) && File.Exists(pngPath))
			return pngPath;

		return null;
	}

	private static Texture2D LoadTextureFromFile(string fullPath)
	{
		try
		{
			return fullPath.EndsWith(".rtex", StringComparison.OrdinalIgnoreCase) 
				? LoadRtexImage(fullPath) 
				: LoadStandardImage(fullPath);
		}
		catch (Exception ex)
		{
			GD.PrintErr($"[VfxShaderManager] Error loading texture {fullPath}: {ex.Message}");
			return null;
		}
	}

	private static Texture2D LoadRtexImage(string fullPath)
	{
		byte[] bytes = File.ReadAllBytes(fullPath);
		byte[] layerData = Realm.Shared.Textures.RtexFile.GetLayer(bytes, 0);
		if (layerData == null || layerData.Length == 0) return null;

		var img = Image.CreateEmpty(1, 1, false, Image.Format.Rgba8);
		if (img.LoadWebpFromBuffer(layerData) != Error.Ok && img.LoadPngFromBuffer(layerData) != Error.Ok) return null;

		img.GenerateMipmaps();
		return ImageTexture.CreateFromImage(img);
	}

	private static Texture2D LoadStandardImage(string fullPath)
	{
		var img = Image.LoadFromFile(fullPath);
		if (img == null) return null;

		img.GenerateMipmaps();
		return ImageTexture.CreateFromImage(img);
	}

	public static (int Columns, int Rows, float Fps, bool SubframeBlend)? GetSpritesheetMetadataSafe(string path)
	{
		if (string.IsNullOrEmpty(path)) return null;

		lock (SyncLock)
		{
			if (SpritesheetMetaCache.TryGetValue(path, out var cachedMeta))
				return cachedMeta;

			string wsPath = ProjectSettings.GlobalizePath(MapEditorHUD.TempWorkspaceGodotPath);
			
			var wsMeta = TryLoadWorkspaceMetadata(path, wsPath);
			if (wsMeta != null) return wsMeta;

			return TryLoadEmbeddedMetadata(path, wsPath);
		}
	}

	private static (int Columns, int Rows, float Fps, bool SubframeBlend)? TryLoadWorkspaceMetadata(string path, string wsPath)
	{
		try
		{
			var metadata = MapFileService.LoadMetadata(wsPath);
			if (metadata?.VfxSpritesheets == null) return null;

			string fileName = Path.GetFileName(path);
			string cleanBase = Path.GetFileNameWithoutExtension(path);
			VfxMetadata vmeta = GetVfxMetadata(metadata, path, fileName, cleanBase);

			if (vmeta == null) return null;

			int cols = vmeta.Columns > 0 ? vmeta.Columns : 1;
			int rows = vmeta.Rows > 0 ? vmeta.Rows : 1;
			float fps = vmeta.Fps > 0.001f ? vmeta.Fps : 20.0f;
			bool subframeBlend = vmeta.SubframeBlend;

			bool isSpritesheet = cols > 1 || rows > 1 || !string.IsNullOrEmpty(vmeta.TexturePath);
			if (!isSpritesheet) return null;

			var result = (cols, rows, fps, subframeBlend);
			SpritesheetMetaCache[path] = result;
			return result;
		}
		catch 
		{ 
			return null; 
		}
	}

	private static VfxMetadata GetVfxMetadata(MapMetadata metadata, string path, string fileName, string cleanBase)
	{
		if (metadata.VfxSpritesheets.TryGetValue(path, out var v1)) return v1;
		if (metadata.VfxSpritesheets.TryGetValue(fileName, out var v2)) return v2;
		if (metadata.VfxSpritesheets.TryGetValue($"{cleanBase}.rtex", out var v3)) return v3;
		if (metadata.VfxSpritesheets.TryGetValue($"{cleanBase}.png", out var v4)) return v4;
		return null;
	}

	private static (int Columns, int Rows, float Fps, bool SubframeBlend)? TryLoadEmbeddedMetadata(string path, string wsPath)
	{
		string[] candidates = GetSpritesheetCandidates(path, wsPath);

		foreach (var candidate in candidates)
		{
			string clean = GetExistingFilePath(candidate);
			if (string.IsNullOrEmpty(clean)) continue;

			return ParseEmbeddedMetadata(path, clean);
		}

		return null;
	}

	private static string[] GetSpritesheetCandidates(string path, string wsPath)
	{
		return new[]
		{
			path,
			Path.Combine(wsPath, path),
			Path.Combine(wsPath, "Assets", "vfx", path),
			Path.Combine(wsPath, "Assets", "textures", path),
			Path.Combine(wsPath, "Assets", "decals", path),
			Path.Combine(wsPath, "Assets", "ribbons", path),
			Path.Combine(wsPath, "Assets", "noise", path),
			Path.Combine("MapTemplate", path),
			Path.Combine("MapTemplate", "Assets", "vfx", path),
			Path.Combine("MapTemplate", "Assets", "textures", path),
			Path.Combine("MapTemplate", "Assets", "decals", path),
			Path.Combine("MapTemplate", "Assets", "ribbons", path),
			Path.Combine("MapTemplate", "Assets", "noise", path)
		};
	}

	private static (int Columns, int Rows, float Fps, bool SubframeBlend)? ParseEmbeddedMetadata(string path, string clean)
	{
		string metaJson = RealmMetadataHelper.ExtractMetadata(clean);
		if (string.IsNullOrEmpty(metaJson)) return null;

		try
		{
			var node = JsonNode.Parse(metaJson);
			if (node is not JsonObject obj) return null;

			return ParseJsonMetadata(path, obj);
		}
		catch 
		{ 
			return null;
		}
	}

	private static (int Columns, int Rows, float Fps, bool SubframeBlend)? ParseJsonMetadata(string path, JsonObject obj)
	{
		int cols = 1;
		int rows = 1;
		float fps = 20.0f;
		bool subframeBlend = true;

		TryParseColumns(obj, ref cols);
		TryParseRows(obj, ref rows);
		TryParseFps(obj, ref fps);
		TryParseSubframeBlend(obj, ref subframeBlend);

		if (!IsSpritesheetAssetType(obj, cols, rows)) return null;

		var result = (cols, rows, fps, subframeBlend);
		SpritesheetMetaCache[path] = result;
		return result;
	}

	private static void TryParseColumns(JsonObject obj, ref int cols)
	{
		if (obj.TryGetPropertyValue("columns", out var cNode) && int.TryParse(cNode?.ToString(), out int parsedCols) && parsedCols > 0)
			cols = parsedCols;
	}

	private static void TryParseRows(JsonObject obj, ref int rows)
	{
		if (obj.TryGetPropertyValue("rows", out var rNode) && int.TryParse(rNode?.ToString(), out int parsedRows) && parsedRows > 0)
			rows = parsedRows;
	}

	private static void TryParseFps(JsonObject obj, ref float fps)
	{
		if (obj.TryGetPropertyValue("fps", out var fNode) && float.TryParse(fNode?.ToString(), out float parsedFps) && parsedFps > 0.001f)
			fps = parsedFps;
	}

	private static void TryParseSubframeBlend(JsonObject obj, ref bool subframeBlend)
	{
		if (obj.TryGetPropertyValue("subframe_blend", out var sbNode) && bool.TryParse(sbNode?.ToString(), out bool parsedSb))
			subframeBlend = parsedSb;
	}

	private static bool IsSpritesheetAssetType(JsonObject obj, int cols, int rows)
	{
		string assetType = null;
		if (obj.TryGetPropertyValue("asset_type", out var atNode))
			assetType = atNode?.ToString();
		else if (obj.TryGetPropertyValue("type", out var tNode))
			assetType = tNode?.ToString();
		
		return string.Equals(assetType, "Spritesheet", StringComparison.OrdinalIgnoreCase) ||
		       string.Equals(assetType, "SpellSpritesheet", StringComparison.OrdinalIgnoreCase) ||
		       cols > 1 || rows > 1;
	}


	public static Color ParseColorSafe(string hex, Color fallback)
	{
		if (string.IsNullOrEmpty(hex)) return fallback;
		try
		{
			return Color.FromHtml(hex);
		}
		catch
		{
			return fallback;
		}
	}
}
