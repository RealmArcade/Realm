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

[GlobalClass]
public partial class TerrainTextureEditDialog : FloatingDialogBase
{
	public TerrainTextureEditDialog() { }

	private string _textureFileName = "";
	private string _objectType = "terrain";
	private string _slug = "";
	private string _rtexAsset = "";
	private int _swatchIndex = -1;

	private float _brightness = 1.0f;
	private Color _tint = Colors.White;
	private float _heightScale = 1.0f;
	private float _heightOffset = 0.0f;
	private float _crevicePower = 1.0f;
	private float _normalScale = 1.0f;
	private float _roughnessScale = 1.0f;
	private string _tileMode = "Stochastic";
	private float _uvScale = 1.0f;
	private float _stochasticTileSize = 1.0f;
	private float _crossFade = 0.0f;
	private int _defaultPathingCode = EditableTerrain.PATHING_GROUND | EditableTerrain.PATHING_BUILDABLE | EditableTerrain.PATHING_FLYING;
	private List<ProceduralBombingDecalRule> _decalRules = new();
	private List<ProceduralBombingVfxRule> _vfxRules = new();

	private TerrainTextureSnapshot _initialSnapshot = null!;
	private string? _initialTemplateJson = null;
	private bool _templateExistedInitially = false;
	private Action<JsonObject> _onApplied = null!;

	private Label _lblObjectTypePrefix = null!;
	private LineEdit _txtSlug = null!;
	private LineEdit _txtRtexAsset = null!;
	private Action<string> _setRtexAssetValue = null!;

	private HSlider _sldBrightness = null!;
	private Label _lblBrightness = null!;
	private ColorPickerButton _btnTint = null!;
	private HSlider _sldRoughnessScale = null!;
	private Label _lblRoughnessScale = null!;
	private HSlider _sldNormalScale = null!;
	private Label _lblNormalScale = null!;
	private Label _iconHelpNormalScale = null!;
	private HSlider _sldHeightScale = null!;
	private Label _lblHeightScale = null!;
	private Label _iconHelpHeightScale = null!;
	private HSlider _sldHeightOffset = null!;
	private Label _lblHeightOffset = null!;
	private Label _iconHelpHeightOffset = null!;
	private HSlider _sldCrevicePower = null!;
	private Label _lblCrevicePower = null!;
	private Label _iconHelpCrevicePower = null!;
	private OptionButton _optTileMode = null!;
	private HSlider _sldUvScale = null!;
	private Label _lblUvScale = null!;
	private HSlider _sldStochasticTileSize = null!;
	private Label _lblStochasticTileSize = null!;
	private HSlider _sldCrossFade = null!;
	private Label _lblCrossFade = null!;

	private CheckBox _chkPathGround = null!;
	private CheckBox _chkPathBuildable = null!;
	private CheckBox _chkPathShallow = null!;
	private CheckBox _chkPathDeep = null!;
	private CheckBox _chkPathFlying = null!;

	private ItemList _lstDecals = null!;
	private LineEdit _txtDecalId = null!;
	private Action<string> _setDecalIdValue = null!;
	private HSlider _sldDecalDensity = null!;
	private HSlider _sldDecalMinScale = null!;
	private HSlider _sldDecalMaxScale = null!;

	private ItemList _lstVfx = null!;
	private LineEdit _txtVfxId = null!;
	private Action<string> _setVfxIdValue = null!;
	private HSlider _sldVfxDensity = null!;
	private HSlider _sldVfxMinScale = null!;
	private HSlider _sldVfxMaxScale = null!;

	public TerrainTextureEditDialog(MapEditorHUD hud)
		: base(hud, TranslationServer.Translate("Edit Terrain Texture Swatch"), new Vector2(560, 720))
	{
		BuildControls();
	}

	private List<string> ScanTerrainRtexAssets(bool includeAllFolders)
	{
		var list = ScanAvailableAssets("textures", includeAllFolders);
		var rtexFiles = new HashSet<string>(list.Where(x => x.EndsWith(".rtex", StringComparison.OrdinalIgnoreCase)), StringComparer.OrdinalIgnoreCase);

		string wsPath = MapWorkspaceService.GetActiveWorkspacePath();
		string searchDir = Path.Combine(wsPath, "Assets", "textures");
		ScanDirectoryForRtexFiles(searchDir, rtexFiles);

		string templateDir = Path.Combine(ProjectSettings.GlobalizePath("res://"), "Assets", "textures");
		ScanDirectoryForRtexFiles(templateDir, rtexFiles);

		AddMetadataTextures(wsPath, rtexFiles);

		return rtexFiles.OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToList();
	}

	private void ScanDirectoryForRtexFiles(string dirPath, HashSet<string> rtexFiles)
	{
		if (!Directory.Exists(dirPath)) return;

		foreach (var file in Directory.GetFiles(dirPath, "*.rtex", SearchOption.AllDirectories))
		{
			rtexFiles.Add(Path.GetFileName(file));
		}
	}

	private void AddMetadataTextures(string wsPath, HashSet<string> rtexFiles)
	{
		if (!GameContext.MetadataService.TryLoadMetadata(wsPath, out var meta) || meta?.Textures == null) return;

		foreach (var kvp in meta.Textures)
		{
			if (!string.IsNullOrWhiteSpace(kvp.Value?.TexturePath) && kvp.Value.TexturePath.EndsWith(".rtex", StringComparison.OrdinalIgnoreCase))
			{
				rtexFiles.Add(Path.GetFileName(kvp.Value.TexturePath));
			}

			if (kvp.Key.EndsWith(".rtex", StringComparison.OrdinalIgnoreCase))
			{
				rtexFiles.Add(Path.GetFileName(kvp.Key));
			}
		}
	}

	private void UpdatePathingMask()
	{
		int mask = 0;
		mask = ApplyPathingFlag(mask, _chkPathGround, EditableTerrain.PATHING_GROUND);
		mask = ApplyPathingFlag(mask, _chkPathBuildable, EditableTerrain.PATHING_BUILDABLE);
		mask = ApplyPathingFlag(mask, _chkPathShallow, EditableTerrain.PATHING_SHALLOW_WATER);
		mask = ApplyPathingFlag(mask, _chkPathDeep, EditableTerrain.PATHING_DEEP_WATER);
		mask = ApplyPathingFlag(mask, _chkPathFlying, EditableTerrain.PATHING_FLYING);
		_defaultPathingCode = mask;
	}

	private int ApplyPathingFlag(int currentMask, CheckBox checkBox, int flag)
	{
		if (checkBox != null && checkBox.ButtonPressed)
		{
			return currentMask | flag;
		}

		return currentMask;
	}

	private void OnAddDecalRule()
	{
		if (_txtDecalId == null || string.IsNullOrWhiteSpace(_txtDecalId.Text)) return;
		string id = _txtDecalId.Text.Trim();
		if (string.IsNullOrEmpty(id)) return;

		float density = _sldDecalDensity != null ? (float)_sldDecalDensity.Value : 0.5f;
		float minScale = _sldDecalMinScale != null ? (float)_sldDecalMinScale.Value : 0.2f;
		float maxScale = _sldDecalMaxScale != null ? (float)_sldDecalMaxScale.Value : 0.5f;

		_decalRules.Add(new ProceduralBombingDecalRule
		{
			DecalId = id,
			Density = density,
			MinScale = minScale,
			MaxScale = maxScale
		});
		UpdateDecalList();
	}

	private void OnRemoveDecalRule()
	{
		var selected = _lstDecals.GetSelectedItems();
		if (selected.Length > 0 && selected[0] >= 0 && selected[0] < _decalRules.Count)
		{
			_decalRules.RemoveAt(selected[0]);
			UpdateDecalList();
		}
	}

	private void UpdateDecalList()
	{
		_lstDecals.Clear();
		foreach (var r in _decalRules)
		{
			_lstDecals.AddItem($"{r.DecalId} ({TranslationServer.Translate("Density")}: {r.Density:0.00})");
		}
	}

	private void OnAddVfxRule()
	{
		if (_txtVfxId == null || string.IsNullOrWhiteSpace(_txtVfxId.Text)) return;
		string id = _txtVfxId.Text.Trim();
		if (string.IsNullOrEmpty(id)) return;

		float density = _sldVfxDensity != null ? (float)_sldVfxDensity.Value : 0.5f;
		float minScale = _sldVfxMinScale != null ? (float)_sldVfxMinScale.Value : 0.2f;
		float maxScale = _sldVfxMaxScale != null ? (float)_sldVfxMaxScale.Value : 0.5f;

		_vfxRules.Add(new ProceduralBombingVfxRule
		{
			VfxId = id,
			Density = density,
			MinScale = minScale,
			MaxScale = maxScale
		});
		UpdateVfxList();
	}

	private void OnRemoveVfxRule()
	{
		var selected = _lstVfx.GetSelectedItems();
		if (selected.Length > 0 && selected[0] >= 0 && selected[0] < _vfxRules.Count)
		{
			_vfxRules.RemoveAt(selected[0]);
			UpdateVfxList();
		}
	}

	private void UpdateVfxList()
	{
		_lstVfx.Clear();
		foreach (var r in _vfxRules)
		{
			_lstVfx.AddItem($"{r.VfxId} ({TranslationServer.Translate("Density")}: {r.Density:0.00})");
		}
	}

	private Label CreateHelpTooltipIcon(HSlider slider)
	{
		var helpIcon = new Label();
		helpIcon.Name = "HelpIcon_Label";
		helpIcon.Text = "❓";
		helpIcon.TooltipText = TranslationServer.Translate("NOTE: unavailable on your current graphics preset");
		helpIcon.MouseFilter = MouseFilterEnum.Stop;
		helpIcon.AddThemeFontSizeOverride("font_size", 10);
		helpIcon.Visible = false;

		if (slider.GetParent() is HBoxContainer row)
		{
			row.AddChild(helpIcon);
			row.MoveChild(helpIcon, 0);
		}

		return helpIcon;
	}

	private void UpdateGraphicsQualityState()
	{
		bool isAvailable = GameSettings.QualityIdx >= GraphicsQuality.High;
		string noteTooltip = TranslationServer.Translate("NOTE: unavailable on your current graphics preset");

		void SetSliderQuality(HSlider slider, Label lbl, Label helpIcon)
		{
			if (slider == null) return;
			slider.Editable = isAvailable;
			slider.Modulate = isAvailable ? Colors.White : new Color(0.65f, 0.65f, 0.65f, 0.6f);
			slider.TooltipText = isAvailable ? "" : noteTooltip;
			if (lbl != null)
			{
				lbl.Modulate = isAvailable ? Colors.White : new Color(0.75f, 0.75f, 0.75f, 0.7f);
			}

			if (helpIcon != null)
			{
				helpIcon.Visible = !isAvailable;
			}
		}

		SetSliderQuality(_sldNormalScale, _lblNormalScale, _iconHelpNormalScale);
		SetSliderQuality(_sldHeightScale, _lblHeightScale, _iconHelpHeightScale);
		SetSliderQuality(_sldHeightOffset, _lblHeightOffset, _iconHelpHeightOffset);
		SetSliderQuality(_sldCrevicePower, _lblCrevicePower, _iconHelpCrevicePower);
	}

	private void UpdateTileModeVisibility()
	{
		bool isStochastic = string.Equals(_tileMode, "Stochastic", StringComparison.OrdinalIgnoreCase);
		if (_sldStochasticTileSize?.GetParent() is Control stochRow)
		{
			stochRow.Visible = isStochastic;
		}

		if (_sldCrossFade?.GetParent() is Control crossFadeRow)
		{
			crossFadeRow.Visible = !isStochastic;
		}
	}

	private void ApplyLiveTerrainUpdate()
	{
		if (string.IsNullOrEmpty(_textureFileName)) return;

		string tintHex = $"#{_tint.ToHtml(false)}";
		if (GameContext.Host != null && GameContext.Host.GroundTerrain != null)
		{
			GameContext.Host.GroundTerrain.UpdateTextureParamDirect(
				_textureFileName,
				_tileMode,
				_uvScale,
				_stochasticTileSize,
				_crossFade,
				_brightness,
				tintHex,
				_heightScale,
				_heightOffset,
				_crevicePower,
				_normalScale,
				_roughnessScale
			);
		}
	}

	public void OpenForTexture(string fileName, JsonObject textureData, Action<JsonObject> onApplied)
	{
		_textureFileName = fileName ?? string.Empty;
		var (parsedType, parsedSlug) = TemplateIDHelper.ParseTemplateID(_textureFileName);
		_objectType = !string.IsNullOrEmpty(parsedType) ? parsedType : "terrain";
		_slug = !string.IsNullOrEmpty(parsedSlug) ? parsedSlug : TemplateIDHelper.ToSnakeCase(_textureFileName);
		_onApplied = onApplied;

		DetermineRtexAsset(textureData);
		ResetDefaultValues();
		LoadMetadataProperties();
		LoadTextureDataProperties(textureData);
		DetermineSwatchIndex();
		TakeInitialSnapshot();
		ApplyValuesToUIControls();

		OpenDialog();
	}

	protected override string GetRandomizeTemplateSlug()
	{
		return !string.IsNullOrEmpty(_textureFileName) ? _textureFileName : $"{_objectType}/{_slug}";
	}

	protected override void OnAssetsRandomized()
	{
		string targetId = GetRandomizeTemplateSlug();
		string wsPath = MapWorkspaceService.GetActiveWorkspacePath();

		if (GameContext.MetadataService.TryLoadMetadata(wsPath, out var meta) && meta?.Textures != null)
		{
			var (_, curSlug) = TemplateIDHelper.ParseTemplateID(targetId);
			TextureMetadata? texMeta = null;
			if (meta.Textures.TryGetValue(targetId, out texMeta) ||
			    (!string.IsNullOrEmpty(curSlug) && meta.Textures.TryGetValue(curSlug, out texMeta)) ||
			    (!string.IsNullOrEmpty(_rtexAsset) && meta.Textures.TryGetValue(_rtexAsset, out texMeta)))
			{
				if (texMeta != null && !string.IsNullOrEmpty(texMeta.TexturePath))
				{
					_rtexAsset = EnsureRtexExtension(Path.GetFileName(texMeta.TexturePath));
				}
			}
		}

		ApplyValuesToUIControls();
		ApplyLiveTerrainUpdate();
	}

	private void DetermineRtexAsset(JsonObject textureData)
	{
		_rtexAsset = textureData?.TryGetPropertyValue("TexturePath", out var tpNode) == true && tpNode != null ? tpNode.ToString() : string.Empty;

		if (!string.IsNullOrEmpty(_rtexAsset))
		{
			_rtexAsset = EnsureRtexExtension(Path.GetFileName(_rtexAsset));
			return;
		}

		if (TryFindRtexAssetFromMetadata()) return;

		FindRtexAssetFallback();
	}

	private bool TryFindRtexAssetFromMetadata()
	{
		string wsPathTex = MapWorkspaceService.GetActiveWorkspacePath();
		if (string.IsNullOrEmpty(wsPathTex)) return false;
		if (!GameContext.MetadataService.TryLoadMetadata(wsPathTex, out var metaTex)) return false;
		if (metaTex?.Textures == null) return false;

		if (TryGetTexturePathFromMetadata(metaTex, _textureFileName)) return true;
		if (TryGetTexturePathFromMetadata(metaTex, _slug)) return true;

		return false;
	}

	private bool TryGetTexturePathFromMetadata(MapMetadata metaTex, string key)
	{
		if (metaTex.Textures.TryGetValue(key, out var tMeta) && !string.IsNullOrEmpty(tMeta?.TexturePath))
		{
			_rtexAsset = EnsureRtexExtension(Path.GetFileName(tMeta.TexturePath));
			return true;
		}

		return false;
	}

	private string EnsureRtexExtension(string assetPath)
	{
		if (string.IsNullOrEmpty(assetPath)) return string.Empty;
		if (assetPath.EndsWith(".rtex", StringComparison.OrdinalIgnoreCase)) return assetPath;
		return $"{assetPath}.rtex";
	}

	private void FindRtexAssetFallback()
	{
		if (_textureFileName.EndsWith(".rtex", StringComparison.OrdinalIgnoreCase))
		{
			_rtexAsset = Path.GetFileName(_textureFileName);
			return;
		}

		var candidates = ScanTerrainRtexAssets(true);
		string? candidateMatch = candidates.FirstOrDefault(c => string.Equals(c, $"{_slug}.rtex", StringComparison.OrdinalIgnoreCase))
		                        ?? candidates.FirstOrDefault(c => string.Equals(Path.GetFileNameWithoutExtension(c), _slug, StringComparison.OrdinalIgnoreCase));

		if (!string.IsNullOrEmpty(candidateMatch))
		{
			_rtexAsset = candidateMatch;
		}
	}

	private void ResetDefaultValues()
	{
		_brightness = 1.0f;
		_tint = Colors.White;
		_roughnessScale = 1.0f;
		_normalScale = 1.0f;
		_heightScale = 1.0f;
		_heightOffset = 0.0f;
		_crevicePower = 1.0f;
		_tileMode = "Stochastic";
		_uvScale = 1.0f;
		_stochasticTileSize = 1.0f;
		_crossFade = 0.0f;
		_defaultPathingCode = EditableTerrain.PATHING_GROUND | EditableTerrain.PATHING_BUILDABLE | EditableTerrain.PATHING_FLYING;
		_decalRules.Clear();
		_vfxRules.Clear();
		_swatchIndex = -1;
	}

	private void DetermineSwatchIndex()
	{
		if (_swatchIndex >= 0) return;

		var occupied = new bool[TextureSwatchSlots.MaxSlots];
		PopulateOccupiedSwatches(occupied);

		_swatchIndex = TextureSwatchSlots.FirstFreeSlot(occupied);
		if (_swatchIndex < 0) _swatchIndex = 0;
	}

	private void PopulateOccupiedSwatches(bool[] occupied)
	{
		string wsPath = MapWorkspaceService.GetActiveWorkspacePath();
		if (string.IsNullOrEmpty(wsPath) || !GameContext.MetadataService.TryLoadMetadata(wsPath, out var metaRoot) || metaRoot?.Textures == null) return;

		foreach (var t in metaRoot.Textures.Values)
		{
			if (t != null && t.SwatchIndex >= 0 && t.SwatchIndex < TextureSwatchSlots.MaxSlots)
			{
				occupied[t.SwatchIndex] = true;
			}
		}
	}

	private void TakeInitialSnapshot()
	{
		_initialSnapshot = new TerrainTextureSnapshot
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

		string targetId = !string.IsNullOrEmpty(_textureFileName) ? _textureFileName : $"{_objectType}/{_slug}";
		_initialTemplateJson = GameContext.EditorService.SnapshotTemplateJson(targetId);
		_templateExistedInitially = _initialTemplateJson != null;
	}

	private void ApplyValuesToUIControls()
	{
		TitleLabel.Text = $"{TranslationServer.Translate("Edit Texture Swatch")} - {_textureFileName}";

		if (_lblObjectTypePrefix != null) _lblObjectTypePrefix.Text = $"{_objectType}/";
		if (_txtSlug != null) _txtSlug.Text = _slug;
		_setRtexAssetValue?.Invoke(_rtexAsset);

		ApplySliderAndColorValues();
		ApplyPathingCheckboxes();

		_setDecalIdValue?.Invoke(string.Empty);
		_setVfxIdValue?.Invoke(string.Empty);
		UpdateDecalList();
		UpdateVfxList();

		UpdateTileModeVisibility();
		UpdateGraphicsQualityState();
	}

	private void ApplySliderAndColorValues()
	{
		ApplySliderBasics();
		ApplySliderScales();
		ApplySliderMisc();
	}

	private void ApplySliderBasics()
	{
		if (_sldBrightness != null) _sldBrightness.Value = _brightness;
		if (_btnTint != null) _btnTint.Color = _tint;
	}

	private void ApplySliderScales()
	{
		if (_sldRoughnessScale != null) _sldRoughnessScale.Value = _roughnessScale;
		if (_sldNormalScale != null) _sldNormalScale.Value = _normalScale;
		if (_sldHeightScale != null) _sldHeightScale.Value = _heightScale;
		if (_sldHeightOffset != null) _sldHeightOffset.Value = _heightOffset;
		if (_sldCrevicePower != null) _sldCrevicePower.Value = _crevicePower;
	}

	private void ApplySliderMisc()
	{
		if (_optTileMode != null) _optTileMode.Selected = _tileMode == "Stochastic" ? 1 : 0;
		if (_sldUvScale != null) _sldUvScale.Value = _uvScale;
		if (_sldStochasticTileSize != null) _sldStochasticTileSize.Value = _stochasticTileSize;
		if (_sldCrossFade != null) _sldCrossFade.Value = _crossFade;
	}

	private void ApplyPathingCheckboxes()
	{
		if (_chkPathGround != null) _chkPathGround.ButtonPressed = (_defaultPathingCode & EditableTerrain.PATHING_GROUND) != 0;
		if (_chkPathBuildable != null) _chkPathBuildable.ButtonPressed = (_defaultPathingCode & EditableTerrain.PATHING_BUILDABLE) != 0;
		if (_chkPathShallow != null) _chkPathShallow.ButtonPressed = (_defaultPathingCode & EditableTerrain.PATHING_SHALLOW_WATER) != 0;
		if (_chkPathDeep != null) _chkPathDeep.ButtonPressed = (_defaultPathingCode & EditableTerrain.PATHING_DEEP_WATER) != 0;
		if (_chkPathFlying != null) _chkPathFlying.ButtonPressed = (_defaultPathingCode & EditableTerrain.PATHING_FLYING) != 0;
	}

	protected override void OnCancel()
	{
		string targetId = !string.IsNullOrEmpty(_textureFileName) ? _textureFileName : $"{_objectType}/{_slug}";
		if (!string.IsNullOrEmpty(targetId))
		{
			if (_templateExistedInitially)
			{
				GameContext.EditorService.RevertTemplateJson(targetId, _initialTemplateJson);
			}
			else
			{
				GameContext.EditorService.RevertTemplateJson(targetId, null);
			}
		}

		if (_initialSnapshot != null)
		{
			_brightness = _initialSnapshot.Brightness;
			_tint = _initialSnapshot.Tint;
			_roughnessScale = _initialSnapshot.RoughnessScale;
			_normalScale = _initialSnapshot.NormalScale;
			_heightScale = _initialSnapshot.HeightScale;
			_heightOffset = _initialSnapshot.HeightOffset;
			_crevicePower = _initialSnapshot.CrevicePower;
			_tileMode = _initialSnapshot.TileMode;
			_uvScale = _initialSnapshot.UvScale;
			_stochasticTileSize = _initialSnapshot.StochasticTileSize;
			_crossFade = _initialSnapshot.CrossFade;
			_defaultPathingCode = _initialSnapshot.DefaultPathingCode;
			_decalRules.Clear();
			_vfxRules.Clear();
			if (_initialSnapshot.DecalBombingRules != null)
			{
				foreach (var r in _initialSnapshot.DecalBombingRules) _decalRules.Add(r.Clone());
			}

			if (_initialSnapshot.VfxBombingRules != null)
			{
				foreach (var r in _initialSnapshot.VfxBombingRules) _vfxRules.Add(r.Clone());
			}

			if (_optTileMode != null) _optTileMode.Selected = _tileMode == "Stochastic" ? 1 : 0;
			UpdateTileModeVisibility();
			ApplyLiveTerrainUpdate();
			if (GameContext.Host?.GroundTerrain != null)
			{
				GameContext.Host.GroundTerrain.ClearLiveSwatchOverrides(_textureFileName);
				GameContext.Host.GroundTerrain.ReloadTerrainTextures(true);
			}
		}
	}
}