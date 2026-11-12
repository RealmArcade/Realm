using Realm.Shared.Maps;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text.Json.Nodes;
using DotRecast.Detour;
using Godot;
using Godot.Collections;
using Realm.Client.Core;
using Realm.Client.Services;
using Realm.Client.UI;
using Realm.Ecs;
using Realm.Ecs.Components.Terrain;
using Realm.Ecs.Services;
using Realm.Shared.Services;
using Realm.Shared.Textures;
using Array = Godot.Collections.Array;

namespace Realm.Client;
public partial class RuntimeTerrain
{
	protected static Texture2DArray? _cachedAlbedoTextureArray;
	protected static Texture2DArray? _cachedNormalRoughnessTextureArray;
	protected static string? _cachedMapDir;
	protected List<string> _loadedTextureList = new List<string>();
	public IReadOnlyList<string> LoadedTextureList => _loadedTextureList;
	protected Vector4[] _swatchParamsCache = new Vector4[TextureSwatchSlots.MaxSlots];
	protected Vector4[] _swatchHeightParamsCache = new Vector4[TextureSwatchSlots.MaxSlots];
	protected Vector4[] _swatchAlbedoParamsCache = new Vector4[TextureSwatchSlots.MaxSlots];


	protected static readonly System.Collections.Generic.Dictionary<string, SwatchLiveConfig> _liveSwatchOverrides = new(StringComparer.OrdinalIgnoreCase);

	public virtual void UpdateTextureParamDirect(
		string swatchName,
		string tileMode,
		float uvScale,
		float stochasticTileSize,
		float crossFade = 0.0f,
		float? brightness = null,
		string? tintStr = null,
		float heightScale = 1.0f,
		float heightOffset = 0.0f,
		float crevicePower = 1.0f,
		float normalScale = 1.0f,
		float roughnessScale = 1.0f)
	{
		if (_material == null) return;
		string cleanName = Path.GetFileNameWithoutExtension(swatchName);

		UpdateLiveSwatchOverrides(cleanName, tileMode, uvScale, stochasticTileSize, crossFade, brightness, tintStr, heightScale, heightOffset, crevicePower, normalScale, roughnessScale);

		int targetIndex = FindSwatchIndex(cleanName);
		if (targetIndex < 0) return;

		ApplySwatchParams(targetIndex, cleanName, tileMode, uvScale, stochasticTileSize, crossFade, heightScale, heightOffset, crevicePower, normalScale);
		ApplySwatchAlbedo(targetIndex, cleanName, brightness, tintStr, roughnessScale);
		UpdateEcsTerrainSwatchConfig(targetIndex, heightScale, heightOffset, crevicePower, normalScale);
	}

		private (float tm, float uv, float stoch, float cf, float hs, float ho, float cp, float ns) ClampSwatchParams(string tileMode, float uvScale, float stochasticTileSize, float crossFade, float heightScale, float heightOffset, float crevicePower, float normalScale)
	{
		float tm = string.Equals(tileMode, "Grid", StringComparison.OrdinalIgnoreCase) ? 0.0f : 1.0f;
		float uv = Math.Clamp(uvScale, 0.1f, 4.0f);
		float stoch = Math.Clamp(stochasticTileSize, 0.5f, 3.0f);
		float cf = Math.Clamp(crossFade, 0.0f, 10.0f) * 0.01f;

		float hs = Math.Clamp(heightScale, 0.1f, 3.0f);
		float ho = Math.Clamp(heightOffset, -1.0f, 1.0f);
		float cp = Math.Clamp(crevicePower, 0.5f, 4.0f);
		float ns = Math.Clamp(normalScale, 0.0f, 3.0f);
			return (tm, uv, stoch, cf, hs, ho, cp, ns);
		}

		private void UpdateLiveSwatchOverrides(string cleanName, string tileMode, float uvScale, float stochasticTileSize, float crossFade, float? brightness, string? tintStr, float heightScale, float heightOffset, float crevicePower, float normalScale, float roughnessScale)
		{
			var clamped = ClampSwatchParams(tileMode, uvScale, stochasticTileSize, crossFade, heightScale, heightOffset, crevicePower, normalScale);
		float rs = Math.Clamp(roughnessScale, 0.1f, 3.0f);

		Color? parsedTint = (!string.IsNullOrEmpty(tintStr) && Color.HtmlIsValid(tintStr)) ? Color.FromHtml(tintStr) : null;

		_liveSwatchOverrides[cleanName] = new SwatchLiveConfig
		{
				TileMode = clamped.tm,
				UvScale = clamped.uv,
				StochasticTileSize = clamped.stoch,
				CrossFade = clamped.cf,
			Brightness = brightness,
			Tint = parsedTint,
				HeightScale = clamped.hs,
				HeightOffset = clamped.ho,
				CrevicePower = clamped.cp,
				NormalScale = clamped.ns,
			RoughnessScale = rs
		};
	}

	private int FindSwatchIndex(string cleanName)
	{
		for (int i = 0; i < _loadedTextureList.Count && i < TextureSwatchSlots.MaxSlots; i++)
		{
			if (string.Equals(_loadedTextureList[i], cleanName, StringComparison.OrdinalIgnoreCase))
			{
				return i;
			}
		}

		return -1;
	}

	private void ApplySwatchParams(int targetIndex, string cleanName, string tileMode, float uvScale, float stochasticTileSize, float crossFade, float heightScale, float heightOffset, float crevicePower, float normalScale)
	{
		var clamped = ClampSwatchParams(tileMode, uvScale, stochasticTileSize, crossFade, heightScale, heightOffset, crevicePower, normalScale);

		_swatchParamsCache[targetIndex] = new Vector4(clamped.tm, clamped.uv, clamped.stoch, clamped.cf);
		_swatchHeightParamsCache[targetIndex] = new Vector4(clamped.hs, clamped.ho, clamped.cp, clamped.ns);

		_material.SetShaderParameter("swatch_params", _swatchParamsCache);
		_material.SetShaderParameter("swatch_height_params", _swatchHeightParamsCache);
	}

	private void ApplySwatchAlbedo(int targetIndex, string cleanName, float? brightness, string? tintStr, float roughnessScale)
	{
		float rs = Math.Clamp(roughnessScale, 0.1f, 3.0f);
		Color? parsedTint = (!string.IsNullOrEmpty(tintStr) && Color.HtmlIsValid(tintStr)) ? Color.FromHtml(tintStr) : null;

		float currentScaleFactor = GetScaleFactor(cleanName);
		float currentBrightness = GetResolvedBrightness(cleanName, brightness);
		Color currentTint = GetResolvedTint(cleanName, parsedTint);

		if (currentScaleFactor <= 0.0001f) currentScaleFactor = 1.0f;
		if (currentBrightness <= 0.0001f) currentBrightness = 1.0f;

		float effectiveMultiplier = currentScaleFactor * currentBrightness;
		if (effectiveMultiplier <= 0.0001f) effectiveMultiplier = 1.0f;

		_swatchAlbedoParamsCache[targetIndex] = new Vector4(currentTint.R * effectiveMultiplier, currentTint.G * effectiveMultiplier, currentTint.B * effectiveMultiplier, rs);
		_material.SetShaderParameter("swatch_albedo_params", _swatchAlbedoParamsCache);
	}

	private float GetResolvedBrightness(string cleanName, float? brightness)
	{
		if (brightness.HasValue) return brightness.Value;
		if (_liveSwatchOverrides.TryGetValue(cleanName, out var over) && over.Brightness.HasValue) return over.Brightness.Value;
		return 1.0f;
	}

	private Color GetResolvedTint(string cleanName, Color? parsedTint)
	{
		if (parsedTint.HasValue) return parsedTint.Value;
		if (_liveSwatchOverrides.TryGetValue(cleanName, out var over) && over.Tint.HasValue) return over.Tint.Value;
		return Colors.White;
	}

	private float GetScaleFactor(string cleanName)
	{
		string mapDir = MapWorkspaceService.GetActiveWorkspacePath();
		try
		{
			var metadata = MapFileService.LoadMetadata(mapDir);
			if (metadata?.Textures != null && metadata.Textures.TryGetValue(cleanName + ".rtex", out var texMeta) && texMeta != null && texMeta.ScaleFactor > 0.0001f)
			{
				return Math.Clamp(texMeta.ScaleFactor, 0.10f, 4.0f);
			}
		}
		catch
		{
		}

		return ResolveRtexScaleFactor(cleanName, mapDir);
	}

	private float ResolveRtexScaleFactor(string? cleanName, string mapDir)
	{
		string rtexPath = Path.Combine(mapDir, "Assets", "textures", cleanName + ".rtex");
		if (!File.Exists(rtexPath)) rtexPath = Path.Combine(mapDir, cleanName + ".rtex");
		if (!File.Exists(rtexPath)) rtexPath = PathUtils.FindPath($"Assets/textures/{cleanName}.rtex");
		if (!File.Exists(rtexPath)) rtexPath = PathUtils.FindPath($"MapTemplate/Assets/textures/{cleanName}.rtex");

		float rtexSf = ExtractRtexScaleFactor(rtexPath);
		if (rtexSf > 0.0001f && MathF.Abs(rtexSf - 1.0f) > 0.001f) return rtexSf;

		if (File.Exists(rtexPath))
		{
			float calc = TextureConverter.CalculateLuminanceScaleFactor(rtexPath);
			return calc > 0.0001f ? Math.Clamp(calc, 0.10f, 4.0f) : 1.0f;
		}

		return 1.0f;
	}

	private void UpdateEcsTerrainSwatchConfig(int targetIndex, float heightScale, float heightOffset, float crevicePower, float normalScale)
	{
		if (GameContext.Host != null && GameContext.World != null && World.IsAlive(GameContext.Simulation.WorldEntity) && World.Has<TerrainState>(GameContext.Simulation.WorldEntity))
		{
			ref var ts = ref World.Get<TerrainState>(GameContext.Simulation.WorldEntity);
			if (ts.SwatchConfigs == null || ts.SwatchConfigs.Length != TextureSwatchSlots.MaxSlots)
			{
				ts.SwatchConfigs = new TerrainSwatchConfig[TextureSwatchSlots.MaxSlots];
			}

			ts.SwatchConfigs[targetIndex] = new TerrainSwatchConfig(
				Math.Clamp(heightScale, 0.1f, 3.0f),
				Math.Clamp(heightOffset, -1.0f, 1.0f),
				Math.Clamp(crevicePower, 0.5f, 4.0f),
				Math.Clamp(normalScale, 0.0f, 3.0f)
			);
		}
	}


	public virtual ActiveSwatchConfig GetActiveSwatchConfig(string swatchName)
	{
		string cleanName = Path.GetFileNameWithoutExtension(swatchName);
		int targetIndex = FindSwatchIndex(cleanName);

		var config = CreateDefaultSwatchConfig();
		ApplyCachedParamsToConfig(config, targetIndex);
		ApplyLiveOverridesToConfig(config, cleanName);

		return config;
	}

	private ActiveSwatchConfig CreateDefaultSwatchConfig()
	{
		return new ActiveSwatchConfig
		{
			TileMode = "Stochastic",
			UvScale = 1.0f,
			StochasticTileSize = 1.0f,
			CrossFade = 0.0f,
			HeightScale = 1.0f,
			HeightOffset = 0.0f,
			CrevicePower = 1.0f,
			NormalScale = 1.0f,
			RoughnessScale = 1.0f,
			Brightness = 1.0f,
			Tint = Colors.White
		};
	}

	private void ApplyCachedParamsToConfig(ActiveSwatchConfig config, int targetIndex)
	{
		if (targetIndex < 0 || _swatchParamsCache == null || targetIndex >= _swatchParamsCache.Length) return;

		ApplyCachedBaseParamsToConfig(config, targetIndex);
		ApplyCachedHeightParamsToConfig(config, targetIndex);
		ApplyCachedAlbedoParamsToConfig(config, targetIndex);
	}

	private void ApplyCachedBaseParamsToConfig(ActiveSwatchConfig config, int targetIndex)
	{
		var p = _swatchParamsCache[targetIndex];
		config.TileMode = p.X < 0.5f ? "Grid" : "Stochastic";
		config.UvScale = p.Y > 0.001f ? p.Y : 1.0f;
		config.StochasticTileSize = p.Z > 0.001f ? p.Z : 1.0f;
		config.CrossFade = p.W * 100.0f;
	}

	private void ApplyCachedHeightParamsToConfig(ActiveSwatchConfig config, int targetIndex)
	{
		var hp = _swatchHeightParamsCache[targetIndex];
		config.HeightScale = hp.X > 0.001f ? hp.X : 1.0f;
		config.HeightOffset = hp.Y;
		config.CrevicePower = hp.Z > 0.001f ? hp.Z : 1.0f;
		config.NormalScale = hp.W > 0.0001f ? hp.W : 1.0f;
	}

	private void ApplyCachedAlbedoParamsToConfig(ActiveSwatchConfig config, int targetIndex)
	{
		if (_swatchAlbedoParamsCache == null || targetIndex >= _swatchAlbedoParamsCache.Length) return;
		config.RoughnessScale = _swatchAlbedoParamsCache[targetIndex].W > 0.0001f ? _swatchAlbedoParamsCache[targetIndex].W : 1.0f;
	}

	private void ApplyLiveOverridesToConfig(ActiveSwatchConfig config, string cleanName)
	{
		if (!_liveSwatchOverrides.TryGetValue(cleanName, out var liveOver)) return;
		ApplyLiveOverridesCore(config, liveOver);
	}

	private static void ApplyLiveOverridesCore(ActiveSwatchConfig config, SwatchLiveConfig liveOver)
	{
		ApplyLiveOverridesTexture(config, liveOver);
		ApplyLiveOverridesMaterial(config, liveOver);
	}

	private static void ApplyLiveOverridesTexture(ActiveSwatchConfig config, SwatchLiveConfig liveOver)
	{
		if (liveOver.TileMode.HasValue)
		{
			if (liveOver.TileMode.Value < 0.5f) config.TileMode = "Grid";
			else config.TileMode = "Stochastic";
		}

		if (liveOver.UvScale.HasValue) config.UvScale = liveOver.UvScale.Value;
		if (liveOver.StochasticTileSize.HasValue) config.StochasticTileSize = liveOver.StochasticTileSize.Value;
		if (liveOver.CrossFade.HasValue) config.CrossFade = liveOver.CrossFade.Value * 100.0f;
		if (liveOver.HeightScale.HasValue) config.HeightScale = liveOver.HeightScale.Value;
		if (liveOver.HeightOffset.HasValue) config.HeightOffset = liveOver.HeightOffset.Value;
	}

	private static void ApplyLiveOverridesMaterial(ActiveSwatchConfig config, SwatchLiveConfig liveOver)
	{
		if (liveOver.CrevicePower.HasValue) config.CrevicePower = liveOver.CrevicePower.Value;
		if (liveOver.NormalScale.HasValue) config.NormalScale = liveOver.NormalScale.Value;
		if (liveOver.RoughnessScale.HasValue) config.RoughnessScale = liveOver.RoughnessScale.Value;
		if (liveOver.Brightness.HasValue) config.Brightness = liveOver.Brightness.Value;
		if (liveOver.Tint.HasValue) config.Tint = liveOver.Tint.Value;
	}

	public virtual void ClearLiveSwatchOverrides(string? swatchName = null)
	{
		if (string.IsNullOrEmpty(swatchName))
		{
			_liveSwatchOverrides.Clear();
		}
		else
		{
			string cleanName = Path.GetFileNameWithoutExtension(swatchName);
			_liveSwatchOverrides.Remove(cleanName);
		}
	}

	private static float ExtractRtexScaleFactor(string rtexPath)
	{
		if (string.IsNullOrEmpty(rtexPath) || !File.Exists(rtexPath)) return 1.0f;

		try
		{
			string? rtexMeta = RealmMetadataHelper.ExtractMetadata(rtexPath);
			if (string.IsNullOrEmpty(rtexMeta)) return 1.0f;

			var rNode = JsonNode.Parse(rtexMeta);
			if (rNode is JsonObject rObj)
			{
				return ParseScaleFactorFromJsonObject(rObj);
			}
		}
		catch
		{
		}

		return 1.0f;
	}

	private static float ParseScaleFactorFromJsonObject(JsonObject rObj)
	{
		if (rObj.TryGetPropertyValue("scale_factor", out var sfVal) ||
		    rObj.TryGetPropertyValue("Scale_Factor", out sfVal) ||
		    rObj.TryGetPropertyValue("scaleFactor", out sfVal) ||
		    rObj.TryGetPropertyValue("ScaleFactor", out sfVal))
		{
			if (float.TryParse(sfVal?.ToString(), NumberStyles.Float, CultureInfo.InvariantCulture, out float parsedScale))
			{
				return Math.Clamp(parsedScale, 0.10f, 4.0f);
			}
		}

		return 1.0f;
	}

	private (Image? AlbedoHeight, Image? NormalRoughness) LoadRtexLayers(string rtexPath)
	{
		string globalRtexPath = ProjectSettings.GlobalizePath(rtexPath);
		if (!File.Exists(globalRtexPath)) return (null, null);

		try
		{
			byte[] bytes = File.ReadAllBytes(globalRtexPath);
			var (_, layers, _) = RtexFile.Parse(bytes);
			if (layers.Count == 0) return (null, null);

			Image? img0 = null;
			if (layers.Count > 0 && layers[0].Length > 0)
			{
				img0 = Image.CreateEmpty(1, 1, false, Image.Format.Rgba8);
				if (img0.LoadWebpFromBuffer(layers[0]) != Error.Ok)
				{
					img0.LoadPngFromBuffer(layers[0]);
				}
			}

			Image? img1 = null;
			if (layers.Count > 1 && layers[1].Length > 0)
			{
				img1 = Image.CreateEmpty(1, 1, false, Image.Format.Rgba8);
				if (img1.LoadWebpFromBuffer(layers[1]) != Error.Ok)
				{
					img1.LoadPngFromBuffer(layers[1]);
				}
			}

			return (img0, img1);
		}
		catch (Exception ex)
		{
			GameLog.LogError($"Failed to load RTEX layers from '{globalRtexPath}': {ex.Message}");
			return (null, null);
		}
	}

	public void ReloadTerrainTextures(bool forceReload = false)
	{
		if (_material == null) return;
		if (forceReload)
		{
			_liveSwatchOverrides.Clear();
			_cachedAlbedoTextureArray = null;
			_cachedNormalRoughnessTextureArray = null;
			_cachedMapDir = null;
		}

		string mapDir = MapWorkspaceService.GetActiveWorkspacePath();
		var swatchSlots = TextureSwatchSlots.ResolveSlots(null, mapDir);

		var swatchParams = new Vector4[TextureSwatchSlots.MaxSlots];
		var swatchHeightParams = new Vector4[TextureSwatchSlots.MaxSlots];
		var swatchAlbedoParams = new Vector4[TextureSwatchSlots.MaxSlots];
		var textureList = new List<string>();

		for (int i = 0; i < TextureSwatchSlots.MaxSlots; i++)
		{
			ProcessSwatchSlot(i, swatchSlots[i], mapDir, textureList, swatchParams, swatchHeightParams, swatchAlbedoParams);
		}

		UpdateMaterialAndEcs(textureList, swatchParams, swatchHeightParams, swatchAlbedoParams);

		if (!forceReload && _cachedAlbedoTextureArray != null && _cachedNormalRoughnessTextureArray != null && _cachedMapDir == mapDir)
		{
			RestoreCachedTextureArrays();
			return;
		}

		RegenerateTextureArrays(swatchSlots, mapDir);
	}

	private void ProcessSwatchSlot(int index, SwatchSlotInfo slot, string mapDir, List<string> textureList, Vector4[] swatchParams, Vector4[] swatchHeightParams, Vector4[] swatchAlbedoParams)
	{
		textureList.Add(slot.BaseName ?? "");

		if (slot.IsFiller || string.IsNullOrEmpty(slot.BaseName))
		{
			SetDefaultSwatchParams(index, swatchParams, swatchHeightParams, swatchAlbedoParams);
			return;
		}

		var config = ExtractSwatchConfigFromSlot(slot, mapDir);
		ApplyLiveOverridesToSwatchConfig(slot.BaseName, config);

		float effectiveMultiplier = Math.Max(config.ScaleFactor * config.Brightness, 0.0001f);

		swatchParams[index] = new Vector4(config.TileMode, config.UvScale, config.StochasticTileSize, config.CrossFade);
		swatchHeightParams[index] = new Vector4(config.HeightScale, config.HeightOffset, config.CrevicePower, config.NormalScale);
		swatchAlbedoParams[index] = new Vector4(config.Tint.R * effectiveMultiplier, config.Tint.G * effectiveMultiplier, config.Tint.B * effectiveMultiplier, config.RoughnessScale);
	}


	private ParsedSwatchConfig ExtractSwatchConfigFromSlot(SwatchSlotInfo slot, string mapDir)
	{
		var c = new ParsedSwatchConfig();
		if (slot.MetadataNode != null)
		{
			ApplyMetadataToSwatchConfig(c, slot.MetadataNode);
		}

		if (c.ScaleFactor <= 0.0001f || MathF.Abs(c.ScaleFactor - 1.0f) < 0.0001f)
		{
			c.ScaleFactor = ResolveRtexScaleFactor(slot.BaseName, mapDir);
		}

		return c;
	}

	private void ApplyMetadataToSwatchConfig(ParsedSwatchConfig c, TextureMetadata sObj)
	{
		ApplyMetadataTileMode(c, sObj);
		ApplyMetadataScaleAndFade(c, sObj);
		ApplyMetadataVisuals(c, sObj);
		ApplyMetadataHeight(c, sObj);
	}

	private void ApplyMetadataTileMode(ParsedSwatchConfig c, TextureMetadata sObj)
	{
		if (!string.IsNullOrEmpty(sObj.TileMode)) c.TileMode = string.Equals(sObj.TileMode, "Grid", StringComparison.OrdinalIgnoreCase) ? 0.0f : 1.0f;
		if (sObj.UvScale > 0.0001f) c.UvScale = Math.Clamp(sObj.UvScale, 0.1f, 4.0f);
		if (sObj.StochasticTileSize > 0.0001f) c.StochasticTileSize = Math.Clamp(sObj.StochasticTileSize, 0.5f, 3.0f);
	}

	private void ApplyMetadataScaleAndFade(ParsedSwatchConfig c, TextureMetadata sObj)
	{
		if (sObj.CrossFade >= 0.0f) c.CrossFade = Math.Clamp(sObj.CrossFade, 0.0f, 10.0f) * 0.01f;
		if (sObj.ScaleFactor > 0.0001f) c.ScaleFactor = Math.Clamp(sObj.ScaleFactor, 0.10f, 4.0f);
	}

	private void ApplyMetadataVisuals(ParsedSwatchConfig c, TextureMetadata sObj)
	{
		if (sObj.Brightness > 0f) c.Brightness = Math.Clamp(sObj.Brightness, 0.1f, 5.0f);
		if (sObj.NormalScale >= 0f) c.NormalScale = Math.Clamp(sObj.NormalScale, 0.0f, 3.0f);
		if (sObj.RoughnessScale > 0f) c.RoughnessScale = Math.Clamp(sObj.RoughnessScale, 0.10f, 3.0f);
		if (!string.IsNullOrEmpty(sObj.Tint) && sObj.Tint.StartsWith("#")) c.Tint = Color.FromHtml(sObj.Tint);
	}

	private void ApplyMetadataHeight(ParsedSwatchConfig c, TextureMetadata sObj)
	{
		if (sObj.HeightScale > 0f) c.HeightScale = Math.Clamp(sObj.HeightScale, 0.1f, 3.0f);
		if (sObj.HeightOffset != 0f) c.HeightOffset = Math.Clamp(sObj.HeightOffset, -1.0f, 1.0f);
		if (sObj.CrevicePower > 0f) c.CrevicePower = Math.Clamp(sObj.CrevicePower, 0.5f, 4.0f);
	}

	private void ApplyLiveOverridesToSwatchConfig(string name, ParsedSwatchConfig c)
	{
		if (_liveSwatchOverrides.TryGetValue(name, out var liveOver))
		{
			ApplyLiveOverridesTileMode(liveOver, c);
			ApplyLiveOverridesVisuals(liveOver, c);
			ApplyLiveOverridesHeight(liveOver, c);
		}

		if (c.ScaleFactor <= 0.0001f) c.ScaleFactor = 1.0f;
		if (c.Brightness <= 0.0001f) c.Brightness = 1.0f;
	}

	private void ApplyLiveOverridesTileMode(SwatchLiveConfig liveOver, ParsedSwatchConfig c)
	{
		if (liveOver.TileMode.HasValue) c.TileMode = liveOver.TileMode.Value;
		if (liveOver.UvScale.HasValue) c.UvScale = liveOver.UvScale.Value;
		if (liveOver.StochasticTileSize.HasValue) c.StochasticTileSize = liveOver.StochasticTileSize.Value;
	}

	private void ApplyLiveOverridesVisuals(SwatchLiveConfig liveOver, ParsedSwatchConfig c)
	{
		if (liveOver.CrossFade.HasValue) c.CrossFade = liveOver.CrossFade.Value;
		if (liveOver.NormalScale.HasValue) c.NormalScale = liveOver.NormalScale.Value;
		if (liveOver.RoughnessScale.HasValue) c.RoughnessScale = liveOver.RoughnessScale.Value;
		if (liveOver.Brightness.HasValue) c.Brightness = liveOver.Brightness.Value;
		if (liveOver.Tint.HasValue) c.Tint = liveOver.Tint.Value;
	}

	private void ApplyLiveOverridesHeight(SwatchLiveConfig liveOver, ParsedSwatchConfig c)
	{
		if (liveOver.HeightScale.HasValue) c.HeightScale = liveOver.HeightScale.Value;
		if (liveOver.HeightOffset.HasValue) c.HeightOffset = liveOver.HeightOffset.Value;
		if (liveOver.CrevicePower.HasValue) c.CrevicePower = liveOver.CrevicePower.Value;
	}

	private void SetDefaultSwatchParams(int index, Vector4[] swatchParams, Vector4[] swatchHeightParams, Vector4[] swatchAlbedoParams)
	{
		swatchParams[index] = new Vector4(1.0f, 1.0f, 1.0f, 0.05f);
		swatchHeightParams[index] = new Vector4(1.0f, 0.0f, 1.0f, 0.0f);
		swatchAlbedoParams[index] = new Vector4(1.0f, 1.0f, 1.0f, 1.0f);
	}

	private void UpdateMaterialAndEcs(List<string> textureList, Vector4[] swatchParams, Vector4[] swatchHeightParams, Vector4[] swatchAlbedoParams)
	{
		_loadedTextureList = textureList;
		_swatchParamsCache = swatchParams;
		_swatchHeightParamsCache = swatchHeightParams;
		_swatchAlbedoParamsCache = swatchAlbedoParams;

		_material.SetShaderParameter("swatch_params", swatchParams);
		_material.SetShaderParameter("swatch_height_params", swatchHeightParams);
		_material.SetShaderParameter("swatch_albedo_params", swatchAlbedoParams);

		if (GameContext.Host != null && GameContext.World != null && World.IsAlive(GameContext.Simulation.WorldEntity) && World.Has<TerrainState>(GameContext.Simulation.WorldEntity))
		{
			ref var ts = ref World.Get<TerrainState>(GameContext.Simulation.WorldEntity);
			ts.SwatchConfigs = new TerrainSwatchConfig[TextureSwatchSlots.MaxSlots];
			for (int i = 0; i < TextureSwatchSlots.MaxSlots; i++)
			{
				ts.SwatchConfigs[i] = new TerrainSwatchConfig(swatchHeightParams[i].X, swatchHeightParams[i].Y, swatchHeightParams[i].Z, swatchHeightParams[i].W);
			}
		}
	}

	private void RestoreCachedTextureArrays()
	{
		_material.SetShaderParameter("terrain_textures", ToVariantOrNil(_cachedAlbedoTextureArray));
		_material.SetShaderParameter("terrain_normals_pbr", ToVariantOrNil(_cachedNormalRoughnessTextureArray));
		_material.SetShaderParameter("cliff_textures", ToVariantOrNil(_cachedAlbedoTextureArray));
		_material.SetShaderParameter("cliff_normals_pbr", ToVariantOrNil(_cachedNormalRoughnessTextureArray));
	}

	private static Variant ToVariantOrNil(GodotObject? obj) => obj != null ? obj : default(Variant);

	private void RegenerateTextureArrays(SwatchSlotInfo[] swatchSlots, string mapDir)
	{
		var albedoHeightImages = new Array<Image>();
		var normalRoughnessImages = new Array<Image>();

		for (int i = 0; i < TextureSwatchSlots.MaxSlots; i++)
		{
			ProcessAndAddSwatchImages(swatchSlots[i], mapDir, albedoHeightImages, normalRoughnessImages);
		}

		var albedoTextureArray = new Texture2DArray();
		albedoTextureArray.CreateFromImages(albedoHeightImages);
		var normalTextureArray = new Texture2DArray();
		normalTextureArray.CreateFromImages(normalRoughnessImages);

		_cachedAlbedoTextureArray = albedoTextureArray;
		_cachedNormalRoughnessTextureArray = normalTextureArray;
		_cachedMapDir = mapDir;

		_material.SetShaderParameter("terrain_textures", albedoTextureArray);
		_material.SetShaderParameter("terrain_normals_pbr", normalTextureArray);
		_material.SetShaderParameter("cliff_textures", albedoTextureArray);
		_material.SetShaderParameter("cliff_normals_pbr", normalTextureArray);
	}

	private void ProcessAndAddSwatchImages(SwatchSlotInfo slot, string mapDir, Array<Image> albedoHeightImages, Array<Image> normalRoughnessImages)
	{
		Image? imgLayer0 = null;
		Image? imgLayer1 = null;

		if (!slot.IsFiller && !string.IsNullOrEmpty(slot.BaseName))
		{
			LoadImagesForSwatch(slot, mapDir, ref imgLayer0, ref imgLayer1);
		}

		if (imgLayer0 == null || imgLayer1 == null)
		{
			imgLayer0 = Image.CreateEmpty(TargetTextureResolution, TargetTextureResolution, false, Image.Format.Rgba8);
			imgLayer0.Fill(new Color(0.5f, 0.5f, 0.5f, 1.0f));
			imgLayer1 = Image.CreateEmpty(TargetTextureResolution, TargetTextureResolution, false, Image.Format.Rgba8);
			imgLayer1.Fill(new Color(0.5f, 0.5f, 1.0f, 0.8f));
		}

		FormatAndAddImage(imgLayer0, albedoHeightImages);
		FormatAndAddImage(imgLayer1, normalRoughnessImages);
	}

	private void LoadImagesForSwatch(SwatchSlotInfo slot, string mapDir, ref Image? imgLayer0, ref Image? imgLayer1)
	{
		string? name = slot.BaseName;
		string rtexFileName = slot.MetadataNode?.TexturePath ?? slot.FileName ?? (name + ".rtex");
		if (!rtexFileName.EndsWith(".rtex", StringComparison.OrdinalIgnoreCase)) rtexFileName += ".rtex";
		rtexFileName = Path.GetFileName(rtexFileName);

		string rtexPath = ResolveRtexPath(rtexFileName, name, mapDir);

		if (!File.Exists(rtexPath)) return;

		try
		{
			var layers = LoadRtexLayers(rtexPath);
			imgLayer0 = layers.AlbedoHeight;
			imgLayer1 = layers.NormalRoughness;
		}
		catch (Exception ex)
		{
			GameLog.LogError($"Failed to load dynamic RTEX layers for {name}: {ex.Message}");
		}
	}

	private string ResolveRtexPath(string rtexFileName, string? baseName, string mapDir)
	{
		string[] pathsToTry = new string[]
		{
			Path.Combine(mapDir, "Assets", "textures", rtexFileName),
			Path.Combine(mapDir, rtexFileName),
			PathUtils.FindPath($"Assets/textures/{rtexFileName}"),
			PathUtils.FindPath($"MapTemplate/Assets/textures/{rtexFileName}"),
			ProjectSettings.GlobalizePath($"res://Assets/2d/TileSheets/{rtexFileName}")
		};

		foreach (var path in pathsToTry)
		{
			if (File.Exists(path)) return path;
		}

		string fallbackRtexPath = ProjectSettings.GlobalizePath($"res://Assets/2d/TileSheets/{rtexFileName}");
		string pngPath = ProjectSettings.GlobalizePath($"res://Assets/2d/TileSheets/{baseName}.png");
		if (File.Exists(pngPath))
		{
			ProcessAndSaveRawTexture(pngPath, fallbackRtexPath);
		}

		return fallbackRtexPath;
	}

	private void FormatAndAddImage(Image img, Array<Image> targetArray)
	{
		if (img.GetWidth() != TargetTextureResolution || img.GetHeight() != TargetTextureResolution)
		{
			img.Resize(TargetTextureResolution, TargetTextureResolution, Image.Interpolation.Bilinear);
		}

		if (img.GetFormat() != Image.Format.Rgba8)
		{
			img.Convert(Image.Format.Rgba8);
		}

		img.GenerateMipmaps();
		targetArray.Add(img);
	}

}
