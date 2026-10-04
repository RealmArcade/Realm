using Godot;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json.Nodes;
using Realm.Godot.Utils;
using Realm.Shared.Metadata;

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

		material.SetShaderParameter("base_uv_scroll", config.BaseUvScroll);
		material.SetShaderParameter("base_uv_scale", config.BaseUvScale);
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

		material.SetShaderParameter("noise_uv_scroll", config.NoiseUvScroll);
		material.SetShaderParameter("noise_uv_scale", config.NoiseUvScale);
		material.SetShaderParameter("noise_distortion_strength", Mathf.Clamp(config.DistortionStrength, 0.0f, 2.0f));

		material.SetShaderParameter("base_color", ParseColorSafe(config.BaseColor, new Color(1.0f, 0.45f, 0.1f, 1.0f)));
		material.SetShaderParameter("secondary_color", ParseColorSafe(config.SecondaryColor, new Color(0.8f, 0.1f, 0.0f, 1.0f)));
		material.SetShaderParameter("core_color", ParseColorSafe(config.CoreColor, new Color(1.0f, 0.95f, 0.8f, 1.0f)));
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
			if (TextureCache.TryGetValue(path, out var cached) && cached != null && GodotObject.IsInstanceValid(cached))
			{
				return cached;
			}

			if (path.StartsWith("res://") || path.StartsWith("user://"))
			{
				if (ResourceLoader.Exists(path))
				{
					var tex = GD.Load<Texture2D>(path);
					if (tex != null)
					{
						TextureCache[path] = tex;
						return tex;
					}
				}
			}

			string wsPath = ProjectSettings.GlobalizePath(MapEditorHUD.TempWorkspaceGodotPath);
			string[] candidates = new[]
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

			foreach (var candidate in candidates)
			{
				string clean = candidate;
				if (!File.Exists(clean))
				{
					if (!clean.EndsWith(".rtex", StringComparison.OrdinalIgnoreCase) && File.Exists(clean + ".rtex"))
					{
						clean += ".rtex";
					}
					else if (!clean.EndsWith(".png", StringComparison.OrdinalIgnoreCase) && File.Exists(clean + ".png"))
					{
						clean += ".png";
					}
				}

				if (File.Exists(clean))
				{
					var loaded = LoadTextureFromFile(clean);
					if (loaded != null)
					{
						TextureCache[path] = loaded;
						return loaded;
					}
				}
			}

			return null;
		}
	}

	private static Texture2D LoadTextureFromFile(string fullPath)
	{
		try
		{
			if (fullPath.EndsWith(".rtex", StringComparison.OrdinalIgnoreCase))
			{
				byte[] bytes = File.ReadAllBytes(fullPath);
				byte[] layerData = Realm.Shared.Textures.RtexFile.GetLayer(bytes, 0);
				if (layerData != null && layerData.Length > 0)
				{
					var img = Image.CreateEmpty(1, 1, false, Image.Format.Rgba8);
					if (img.LoadWebpFromBuffer(layerData) == Error.Ok || img.LoadPngFromBuffer(layerData) == Error.Ok)
					{
						img.GenerateMipmaps();
						return ImageTexture.CreateFromImage(img);
					}
				}
			}
			else
			{
				var img = Image.LoadFromFile(fullPath);
				if (img != null)
				{
					img.GenerateMipmaps();
					return ImageTexture.CreateFromImage(img);
				}
			}
		}
		catch (Exception ex)
		{
			GD.PrintErr($"[VfxShaderManager] Error loading texture {fullPath}: {ex.Message}");
		}
		return null;
	}

	public static (int Columns, int Rows, float Fps, bool SubframeBlend)? GetSpritesheetMetadataSafe(string path)
	{
		if (string.IsNullOrEmpty(path)) return null;

		lock (SyncLock)
		{
			if (SpritesheetMetaCache.TryGetValue(path, out var cachedMeta))
			{
				return cachedMeta;
			}

			string wsPath = ProjectSettings.GlobalizePath(MapEditorHUD.TempWorkspaceGodotPath);
			string fileName = Path.GetFileName(path);
			string cleanBase = Path.GetFileNameWithoutExtension(path);

			// 1. Check workspace metadata.json / manifest.json / unioned assets
			try
			{
				var assetsObj = MapAssetHelper.LoadAssets(wsPath);
				if (assetsObj != null)
				{
					foreach (var cat in new[] { "vfx_spritesheets", "vfx", "textures", "decals", "ribbons", "noise_textures" })
					{
						if (assetsObj.TryGetPropertyValue(cat, out var catNode))
						{
							if (catNode is JsonObject catObj)
							{
								JsonNode? entry = null;
								if (catObj.TryGetPropertyValue(path, out var e1)) entry = e1;
								else if (catObj.TryGetPropertyValue(fileName, out var e2)) entry = e2;
								else if (catObj.TryGetPropertyValue($"{cleanBase}.rtex", out var e3)) entry = e3;
								else if (catObj.TryGetPropertyValue($"{cleanBase}.png", out var e4)) entry = e4;

							if (entry is JsonObject eObj)
							{
								int cols = 1;
								int rows = 1;
								float fps = 20.0f;
								bool subframeBlend = true;

								if (eObj.TryGetPropertyValue("columns", out var cNode) && int.TryParse(cNode?.ToString(), out int parsedCols) && parsedCols > 0)
									cols = parsedCols;
								if (eObj.TryGetPropertyValue("rows", out var rNode) && int.TryParse(rNode?.ToString(), out int parsedRows) && parsedRows > 0)
									rows = parsedRows;
								if (eObj.TryGetPropertyValue("fps", out var fNode) && float.TryParse(fNode?.ToString(), out float parsedFps) && parsedFps > 0.001f)
									fps = parsedFps;
								if (eObj.TryGetPropertyValue("subframe_blend", out var sbNode) && bool.TryParse(sbNode?.ToString(), out bool parsedSb))
									subframeBlend = parsedSb;

								string? assetType = eObj["asset_type"]?.ToString() ?? eObj["type"]?.ToString();
								bool isSpritesheet = string.Equals(assetType, "Spritesheet", StringComparison.OrdinalIgnoreCase) ||
								                     string.Equals(assetType, "SpellSpritesheet", StringComparison.OrdinalIgnoreCase) ||
								                     cols > 1 || rows > 1;

								if (isSpritesheet)
								{
									var result = (cols, rows, fps, subframeBlend);
									SpritesheetMetaCache[path] = result;
									return result;
								}
							}
						}
					}
				}
			}
			}
			catch { }

			// 2. Check embedded metadata inside .rtex or candidate files
			string[] candidates = new[]
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

			foreach (var candidate in candidates)
			{
				string clean = candidate;
				if (!File.Exists(clean))
				{
					if (!clean.EndsWith(".rtex", StringComparison.OrdinalIgnoreCase) && File.Exists(clean + ".rtex"))
					{
						clean += ".rtex";
					}
					else if (!clean.EndsWith(".png", StringComparison.OrdinalIgnoreCase) && File.Exists(clean + ".png"))
					{
						clean += ".png";
					}
				}

				if (File.Exists(clean))
				{
					string? metaJson = RealmMetadataHelper.ExtractMetadata(clean);
					if (!string.IsNullOrEmpty(metaJson))
					{
						try
						{
							var node = JsonNode.Parse(metaJson);
							if (node is JsonObject obj)
							{
								int cols = 1;
								int rows = 1;
								float fps = 20.0f;
								bool subframeBlend = true;

								if (obj.TryGetPropertyValue("columns", out var cNode) && int.TryParse(cNode?.ToString(), out int parsedCols) && parsedCols > 0)
									cols = parsedCols;
								if (obj.TryGetPropertyValue("rows", out var rNode) && int.TryParse(rNode?.ToString(), out int parsedRows) && parsedRows > 0)
									rows = parsedRows;
								if (obj.TryGetPropertyValue("fps", out var fNode) && float.TryParse(fNode?.ToString(), out float parsedFps) && parsedFps > 0.001f)
									fps = parsedFps;
								if (obj.TryGetPropertyValue("subframe_blend", out var sbNode) && bool.TryParse(sbNode?.ToString(), out bool parsedSb))
									subframeBlend = parsedSb;

								string? assetType = obj["asset_type"]?.ToString() ?? obj["type"]?.ToString();
								bool isSpritesheet = string.Equals(assetType, "Spritesheet", StringComparison.OrdinalIgnoreCase) ||
								                     string.Equals(assetType, "SpellSpritesheet", StringComparison.OrdinalIgnoreCase) ||
								                     cols > 1 || rows > 1;

								if (isSpritesheet)
								{
									var result = (cols, rows, fps, subframeBlend);
									SpritesheetMetaCache[path] = result;
									return result;
								}
							}
						}
						catch { }
					}
					break;
				}
			}

			return null;
		}
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
