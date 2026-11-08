// Reading an existing texture back into the dialog. Two sources answer the same question: the
// entity metadata, and the raw JSON a map file carries. Both routes land in the same fields, which
// is why the metadata path fills only what metadata knows and the JSON path then fills the rest.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using Godot;
using Realm.Client.Core;
using Realm.Client.Services;


namespace Realm.Client.UI.MapEditor;

public partial class TerrainTextureEditDialog
{
	private void LoadMetadataProperties()
	{
		string wsPath = MapWorkspaceService.GetActiveWorkspacePath();
		if (string.IsNullOrEmpty(wsPath)) return;
		if (!GameContext.MetadataService.TryLoadMetadata(wsPath, out var metaRoot) || metaRoot == null) return;

		var existingTex = metaRoot.GetTerrainTexture(_textureFileName) ?? metaRoot.GetTerrainTexture(_slug);
		if (existingTex == null) return;

		ApplyMetadataBasicProps(existingTex);
		ApplyMetadataScaleProps(existingTex);
		ApplyMetadataMiscProps(existingTex);

		LoadMetadataBombingRules(existingTex);
	}

	private void ApplyMetadataBasicProps(TextureMetadata existingTex)
	{
		_swatchIndex = existingTex.SwatchIndex;
		if (!string.IsNullOrEmpty(existingTex.TexturePath)) _rtexAsset = Path.GetFileName(existingTex.TexturePath);
		if (existingTex.Brightness > 0.0001f) _brightness = existingTex.Brightness;
		if (!string.IsNullOrEmpty(existingTex.Tint) && Color.HtmlIsValid(existingTex.Tint)) _tint = Color.FromHtml(existingTex.Tint);
	}

	private void ApplyMetadataScaleProps(TextureMetadata existingTex)
	{
		if (existingTex.RoughnessScale > 0.0001f) _roughnessScale = existingTex.RoughnessScale;
		if (existingTex.NormalScale >= 0.0f) _normalScale = existingTex.NormalScale;
		if (existingTex.HeightScale > 0.0001f) _heightScale = existingTex.HeightScale;
		_heightOffset = existingTex.HeightOffset;
		if (existingTex.CrevicePower > 0.0001f) _crevicePower = existingTex.CrevicePower;
	}

	private void ApplyMetadataMiscProps(TextureMetadata existingTex)
	{
		if (!string.IsNullOrEmpty(existingTex.TileMode)) _tileMode = existingTex.TileMode;
		if (existingTex.UvScale > 0.0001f) _uvScale = existingTex.UvScale;
		if (existingTex.StochasticTileSize > 0.0001f) _stochasticTileSize = existingTex.StochasticTileSize;
		if (existingTex.CrossFade >= 0.0f) _crossFade = existingTex.CrossFade;
		_defaultPathingCode = existingTex.DefaultPathingCode;
	}

	private void LoadMetadataBombingRules(TextureMetadata existingTex)
	{
		if (existingTex.DecalBombingRules != null)
		{
			_decalRules = new List<ProceduralBombingDecalRule>(existingTex.DecalBombingRules.Select(r => r.Clone()));
		}

		if (existingTex.VfxBombingRules != null)
		{
			_vfxRules = new List<ProceduralBombingVfxRule>(existingTex.VfxBombingRules.Select(r => r.Clone()));
		}
	}

	private void LoadTextureDataProperties(JsonObject textureData)
	{
		if (textureData == null) return;

		LoadTextureDataBasics(textureData);
		LoadTextureDataScales(textureData);
		LoadTextureDataMisc(textureData);

		LoadTextureDataBombingRules(textureData);
	}

	private void LoadTextureDataBasics(JsonObject textureData)
	{
		if (TryParseInt(textureData, "SwatchIndex", out int sw)) _swatchIndex = sw;
		if (TryParseFloat(textureData, "Brightness", out float b) && b > 0.0001f) _brightness = b;
		if (TryParseColor(textureData, "Tint", out Color c)) _tint = c;
	}

	private void LoadTextureDataScales(JsonObject textureData)
	{
		if (TryParseFloat(textureData, "RoughnessScale", out float rs) && rs > 0.0001f) _roughnessScale = rs;
		if (TryParseFloat(textureData, "NormalScale", out float ns) && ns >= 0.0f) _normalScale = ns;
		if (TryParseFloat(textureData, "HeightScale", out float hs) && hs > 0.0001f) _heightScale = hs;
		if (TryParseFloat(textureData, "HeightOffset", out float ho)) _heightOffset = ho;
		if (TryParseFloat(textureData, "CrevicePower", out float cp) && cp > 0.0001f) _crevicePower = cp;
	}

	private void LoadTextureDataMisc(JsonObject textureData)
	{
		LoadTileMode(textureData);
		LoadUvScale(textureData);
		LoadStochasticTileSize(textureData);
		LoadCrossFade(textureData);
		LoadDefaultPathingCode(textureData);
	}

	private void LoadTileMode(JsonObject textureData)
	{
		if (!TryParseString(textureData, "TileMode", out string tm) || string.IsNullOrEmpty(tm)) return;

		if (string.Equals(tm, "Grid", StringComparison.OrdinalIgnoreCase))
		{
			_tileMode = "Grid";
		}
		else
		{
			_tileMode = "Stochastic";
		}
	}

	private void LoadUvScale(JsonObject textureData)
	{
		if (TryParseFloat(textureData, "UvScale", out float uv) && uv > 0.0001f)
		{
			_uvScale = uv;
		}
	}

	private void LoadStochasticTileSize(JsonObject textureData)
	{
		if (TryParseFloat(textureData, "StochasticTileSize", out float st) && st > 0.0001f)
		{
			_stochasticTileSize = st;
		}
	}

	private void LoadCrossFade(JsonObject textureData)
	{
		if (!TryParseFloat(textureData, "CrossFade", out float cf) || cf < 0.0f) return;

		if (cf <= 0.10f && cf > 0.0f)
		{
			_crossFade = cf * 100.0f;
		}
		else
		{
			_crossFade = cf;
		}
	}

	private void LoadDefaultPathingCode(JsonObject textureData)
	{
		if (TryParseInt(textureData, "DefaultPathingCode", out int dpc))
		{
			_defaultPathingCode = dpc;
		}
	}

	private void LoadTextureDataBombingRules(JsonObject textureData)
	{
		if (textureData.TryGetPropertyValue("DecalBombingRules", out var dbrNode) && dbrNode != null)
		{
			var list = JsonSerializer.Deserialize<List<ProceduralBombingDecalRule>>(dbrNode.ToJsonString());
			if (list != null && list.Count > 0) _decalRules = list;
		}

		if (textureData.TryGetPropertyValue("VfxBombingRules", out var vbrNode) && vbrNode != null)
		{
			var list = JsonSerializer.Deserialize<List<ProceduralBombingVfxRule>>(vbrNode.ToJsonString());
			if (list != null && list.Count > 0) _vfxRules = list;
		}
	}

	private bool TryParseInt(JsonObject data, string key, out int result)
	{
		result = 0;
		return data.TryGetPropertyValue(key, out var node) && node != null && int.TryParse(node.ToString(), out result);
	}

	private bool TryParseFloat(JsonObject data, string key, out float result)
	{
		result = 0f;
		return data.TryGetPropertyValue(key, out var node) && node != null && float.TryParse(node.ToString(), NumberStyles.Float, CultureInfo.InvariantCulture, out result);
	}

	private bool TryParseString(JsonObject data, string key, out string result)
	{
		result = string.Empty;
		if (data.TryGetPropertyValue(key, out var node) && node != null)
		{
			result = node.ToString();
			return true;
		}

		return false;
	}

	private bool TryParseColor(JsonObject data, string key, out Color result)
	{
		result = Colors.White;
		if (data.TryGetPropertyValue(key, out var node) && node != null && Color.HtmlIsValid(node.ToString()))
		{
			result = Color.FromHtml(node.ToString());
			return true;
		}

		return false;
	}

}