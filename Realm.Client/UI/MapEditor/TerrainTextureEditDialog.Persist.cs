// Writing the dialog back out. The apply path diffs against the snapshot taken on open, records an
// undo action, and saves both the result JSON and the metadata entry under the texture's new id.

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
	public override void OpenDialog()
	{
		base.OpenDialog();
		UpdateGraphicsQualityState();
	}

	protected override void OnApply()
	{
		UpdatePathingMask();

		if (_txtRtexAsset != null)
		{
			_rtexAsset = _txtRtexAsset.Text?.Trim() ?? string.Empty;
		}

		string newTemplateID = string.IsNullOrEmpty(_objectType) ? _slug : $"{_objectType}/{_slug}";

		RecordUndoAction(newTemplateID);

		var result = BuildResultJson(newTemplateID);

		SaveTextureToMetadata(newTemplateID);

		GameContext.MapEditorHud?.SetupTextureSwatches(false);
		if (GameContext.Host?.GroundTerrain != null)
		{
			GameContext.Host.GroundTerrain.ClearLiveSwatchOverrides();
			GameContext.Host.GroundTerrain.ReloadTerrainTextures(true);
		}

		_onApplied?.Invoke(result);
		Hud?.ShowFeedback(string.Format(TranslationServer.Translate("Texture swatch {0} updated successfully."), newTemplateID));
	}

	private void RecordUndoAction(string newTemplateID)
	{
		if (_initialSnapshot == null) return;

		var currentSnapshot = new TerrainTextureSnapshot
		{
			Brightness = _brightness,
			Tint = _tint,
			RoughnessScale = _roughnessScale,
			NormalScale = _normalScale,
			HeightScale = _heightScale,
			HeightOffset = _heightOffset,
			CrevicePower = _crevicePower,
			TileMode = _tileMode,
			UvScale = _uvScale,
			StochasticTileSize = _stochasticTileSize,
			CrossFade = _crossFade,
			DefaultPathingCode = _defaultPathingCode,
			DecalBombingRules = new List<ProceduralBombingDecalRule>(_decalRules.Select(r => r.Clone())),
			VfxBombingRules = new List<ProceduralBombingVfxRule>(_vfxRules.Select(r => r.Clone()))
		};

		var action = new TerrainTextureUndoAction(newTemplateID, _initialSnapshot, currentSnapshot);
		EditorHistoryManager.RecordAction(action);
	}

	private JsonObject BuildResultJson(string newTemplateID)
	{
		return new JsonObject
		{
			["TemplateID"] = newTemplateID,
			["SwatchIndex"] = _swatchIndex,
			["TexturePath"] = _rtexAsset,
			["Brightness"] = _brightness,
			["Tint"] = $"#{_tint.ToHtml(false)}",
			["RoughnessScale"] = _roughnessScale,
			["NormalScale"] = _normalScale,
			["HeightScale"] = _heightScale,
			["HeightOffset"] = _heightOffset,
			["CrevicePower"] = _crevicePower,
			["TileMode"] = _tileMode,
			["UvScale"] = _uvScale,
			["StochasticTileSize"] = _stochasticTileSize,
			["CrossFade"] = _crossFade,
			["DefaultPathingCode"] = _defaultPathingCode,
			["DecalBombingRules"] = JsonSerializer.SerializeToNode(_decalRules),
			["VfxBombingRules"] = JsonSerializer.SerializeToNode(_vfxRules)
		};
	}

	private void SaveTextureToMetadata(string newTemplateID)
	{
		string wsPath = MapWorkspaceService.GetActiveWorkspacePath();
		string metaPath = Path.Combine(wsPath, "metadata.json");

		if (!File.Exists(metaPath)) return;
		if (!GameContext.MetadataService.TryLoadMetadata(wsPath, out var metadataRoot) || metadataRoot == null) return;

		if (!string.Equals(_textureFileName, newTemplateID, StringComparison.OrdinalIgnoreCase) && !string.IsNullOrEmpty(_textureFileName))
		{
			metadataRoot.Textures?.Remove(_textureFileName);
			string alternateOld = _textureFileName.StartsWith("terrain/", StringComparison.OrdinalIgnoreCase)
				? _textureFileName.Substring("terrain/".Length)
				: $"terrain/{_textureFileName}";
			metadataRoot.Textures?.Remove(alternateOld);
		}

		metadataRoot.Textures ??= new(StringComparer.OrdinalIgnoreCase);
		var texMeta = new TextureMetadata
		{
			TexturePath = _rtexAsset,
			SwatchIndex = _swatchIndex,
			Brightness = _brightness,
			Tint = $"#{_tint.ToHtml(false)}",
			RoughnessScale = _roughnessScale,
			NormalScale = _normalScale,
			HeightScale = _heightScale,
			HeightOffset = _heightOffset,
			CrevicePower = _crevicePower,
			TileMode = _tileMode,
			UvScale = _uvScale,
			StochasticTileSize = _stochasticTileSize,
			CrossFade = _crossFade,
			DefaultPathingCode = _defaultPathingCode,
			DecalBombingRules = new List<ProceduralBombingDecalRule>(_decalRules),
			VfxBombingRules = new List<ProceduralBombingVfxRule>(_vfxRules)
		};
		metadataRoot.Textures[newTemplateID] = texMeta;

		GameContext.MetadataService.SaveMetadata(metaPath, metadataRoot);
	}

}