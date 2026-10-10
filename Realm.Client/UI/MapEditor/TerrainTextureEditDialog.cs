using Godot;
using Realm.Client.Services;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json.Nodes;

namespace Realm.Client.UI.MapEditor;

public partial class TerrainTextureEditDialog : Realm.Client.UI.MapEditor.FloatingDialogBase
{
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
	private int _defaultPathingCode = Realm.Client.EditableTerrain.PATHING_GROUND | Realm.Client.EditableTerrain.PATHING_BUILDABLE | Realm.Client.EditableTerrain.PATHING_FLYING;
	private List<ProceduralBombingDecalRule> _decalRules = new();
	private List<ProceduralBombingVfxRule> _vfxRules = new();

	private TerrainTextureSnapshot _initialSnapshot;
	private Action<JsonObject> _onApplied;

	private Label _lblObjectTypePrefix;
	private LineEdit _txtSlug;
	private LineEdit _txtRtexAsset;
	private Action<string> _setRtexAssetValue;

	private HSlider _sldBrightness;
	private Label _lblBrightness;
	private ColorPickerButton _btnTint;
	private HSlider _sldRoughnessScale;
	private Label _lblRoughnessScale;
	private HSlider _sldNormalScale;
	private Label _lblNormalScale;
	private Label _iconHelpNormalScale;
	private HSlider _sldHeightScale;
	private Label _lblHeightScale;
	private Label _iconHelpHeightScale;
	private HSlider _sldHeightOffset;
	private Label _lblHeightOffset;
	private Label _iconHelpHeightOffset;
	private HSlider _sldCrevicePower;
	private Label _lblCrevicePower;
	private Label _iconHelpCrevicePower;
	private OptionButton _optTileMode;
	private HSlider _sldUvScale;
	private Label _lblUvScale;
	private HSlider _sldStochasticTileSize;
	private Label _lblStochasticTileSize;
	private HSlider _sldCrossFade;
	private Label _lblCrossFade;

	private CheckBox _chkPathGround;
	private CheckBox _chkPathBuildable;
	private CheckBox _chkPathShallow;
	private CheckBox _chkPathDeep;
	private CheckBox _chkPathFlying;

	private ItemList _lstDecals;
	private LineEdit _txtDecalId;
	private Action<string> _setDecalIdValue;
	private HSlider _sldDecalDensity;
	private HSlider _sldDecalMinScale;
	private HSlider _sldDecalMaxScale;

	private ItemList _lstVfx;
	private LineEdit _txtVfxId;
	private Action<string> _setVfxIdValue;
	private HSlider _sldVfxDensity;
	private HSlider _sldVfxMinScale;
	private HSlider _sldVfxMaxScale;

	public TerrainTextureEditDialog(MapEditorHUD hud)
		: base(hud, TranslationServer.Translate("Edit Terrain Texture Swatch"), new Vector2(560, 720))
	{
		BuildControls();
	}

	private void BuildControls()
	{
		var scrollBody = CreateScrollBody(580);
		var contentVBox = new VBoxContainer();
		contentVBox.AddThemeConstantOverride("separation", 10);
		contentVBox.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
		scrollBody.AddChild(contentVBox);

		BuildIdentitySection(contentVBox);
		BuildColorSection(contentVBox);
		BuildHeightmapSection(contentVBox);
		BuildTilingSection(contentVBox);
		BuildPathingSection(contentVBox);
		BuildDecalSection(contentVBox);
		BuildVfxSection(contentVBox);

		UpdateTileModeVisibility();
		UpdateGraphicsQualityState();
	}

	private void BuildIdentitySection(VBoxContainer contentVBox)
	{
		AddSectionHeader(contentVBox, "🆔 " + TranslationServer.Translate("IDENTITY & TEXTURE ASSET"), new Color(0.95f, 0.8f, 0.4f));

		var rowId = new HBoxContainer();
		rowId.AddThemeConstantOverride("separation", 6);
		var lblId = new Label();
		lblId.Text = TranslationServer.Translate("TemplateID:");
		lblId.CustomMinimumSize = new Vector2(140, 0);
		lblId.AddThemeFontSizeOverride("font_size", 11);
		rowId.AddChild(lblId);

		_lblObjectTypePrefix = new Label();
		_lblObjectTypePrefix.Text = "terrain/";
		_lblObjectTypePrefix.AddThemeFontSizeOverride("font_size", 11);
		_lblObjectTypePrefix.AddThemeColorOverride("font_color", UIStyle.ColorGold);
		rowId.AddChild(_lblObjectTypePrefix);

		_txtSlug = new LineEdit();
		_txtSlug.PlaceholderText = TranslationServer.Translate("terrain_slug");
		_txtSlug.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
		_txtSlug.AddThemeFontSizeOverride("font_size", 11);
		_txtSlug.TextChanged += (val) =>
		{
			_slug = TemplateIDHelper.ToSnakeCase(val);
		};
		rowId.AddChild(_txtSlug);
		contentVBox.AddChild(rowId);

		(_txtRtexAsset, _setRtexAssetValue) = AddAssetFilterDropdown(
			contentVBox,
			TranslationServer.Translate("Terrain .rtex:"),
			_rtexAsset,
			(all) => ScanTerrainRtexAssets(all),
			(val) => _rtexAsset = val ?? string.Empty,
			TranslationServer.Translate("Select terrain .rtex asset..."),
			140f
		);
	}

	private void BuildColorSection(VBoxContainer contentVBox)
	{
		AddSectionHeader(contentVBox, "🎨 " + TranslationServer.Translate("COLOR & LIGHTING"), new Color(0.95f, 0.8f, 0.4f));

		(_sldBrightness, _lblBrightness) = AddSlider(
			contentVBox,
			TranslationServer.Translate("Brightness:"),
			0.2f,
			2.5f,
			0.05f,
			_brightness,
			(val) =>
			{
				_brightness = val;
				ApplyLiveTerrainUpdate();
			},
			"0.00x",
			140f
		);

		var rowTint = new HBoxContainer();
		rowTint.AddThemeConstantOverride("separation", 8);
		var lblTint = new Label();
		lblTint.Text = TranslationServer.Translate("Tint Color:");
		lblTint.CustomMinimumSize = new Vector2(140, 0);
		lblTint.AddThemeFontSizeOverride("font_size", 11);
		rowTint.AddChild(lblTint);

		_btnTint = new ColorPickerButton();
		_btnTint.CustomMinimumSize = new Vector2(90, 24);
		_btnTint.Color = _tint;
		_btnTint.ColorChanged += (newCol) =>
		{
			_tint = newCol;
			ApplyLiveTerrainUpdate();
		};
		rowTint.AddChild(_btnTint);
		contentVBox.AddChild(rowTint);

		(_sldRoughnessScale, _lblRoughnessScale) = AddSlider(
			contentVBox,
			TranslationServer.Translate("Roughness Scale:"),
			0.1f,
			3.0f,
			0.05f,
			_roughnessScale,
			(val) =>
			{
				_roughnessScale = val;
				ApplyLiveTerrainUpdate();
			},
			"0.00x",
			140f
		);
	}

	private void BuildHeightmapSection(VBoxContainer contentVBox)
	{
		AddSectionHeader(contentVBox, "🏔️ " + TranslationServer.Translate("HEIGHTMAP & CREVICES"), new Color(0.4f, 0.85f, 0.5f));

		(_sldNormalScale, _lblNormalScale) = AddSlider(
			contentVBox,
			TranslationServer.Translate("Normal Strength:"),
			0.0f,
			3.0f,
			0.05f,
			_normalScale,
			(val) =>
			{
				_normalScale = val;
				ApplyLiveTerrainUpdate();
			},
			"0.00x",
			140f
		);
		_iconHelpNormalScale = CreateHelpTooltipIcon(_sldNormalScale);

		(_sldHeightScale, _lblHeightScale) = AddSlider(
			contentVBox,
			TranslationServer.Translate("Height Scale:"),
			0.1f,
			3.0f,
			0.05f,
			_heightScale,
			(val) =>
			{
				_heightScale = val;
				ApplyLiveTerrainUpdate();
			},
			"0.00x",
			140f
		);
		_iconHelpHeightScale = CreateHelpTooltipIcon(_sldHeightScale);

		(_sldHeightOffset, _lblHeightOffset) = AddSlider(
			contentVBox,
			TranslationServer.Translate("Height Offset:"),
			-1.0f,
			1.0f,
			0.05f,
			_heightOffset,
			(val) =>
			{
				_heightOffset = val;
				ApplyLiveTerrainUpdate();
			},
			"0.00",
			140f
		);
		_iconHelpHeightOffset = CreateHelpTooltipIcon(_sldHeightOffset);

		(_sldCrevicePower, _lblCrevicePower) = AddSlider(
			contentVBox,
			TranslationServer.Translate("Crevice Power:"),
			0.5f,
			4.0f,
			0.1f,
			_crevicePower,
			(val) =>
			{
				_crevicePower = val;
				ApplyLiveTerrainUpdate();
			},
			"0.00x",
			140f
		);
		_iconHelpCrevicePower = CreateHelpTooltipIcon(_sldCrevicePower);
	}

	private void BuildTilingSection(VBoxContainer contentVBox)
	{
		AddSectionHeader(contentVBox, "📐 " + TranslationServer.Translate("TILING & PROJECTION"), new Color(0.35f, 0.75f, 0.9f));

		_optTileMode = AddOptionDropdown(
			contentVBox,
			TranslationServer.Translate("Tile Mode:"),
			new[] { "Grid", "Stochastic" },
			_tileMode == "Stochastic" ? 1 : 0,
			(idx) =>
			{
				_tileMode = idx == 1 ? "Stochastic" : "Grid";
				UpdateTileModeVisibility();
				ApplyLiveTerrainUpdate();
			},
			140f
		);

		(_sldUvScale, _lblUvScale) = AddSlider(
			contentVBox,
			TranslationServer.Translate("UV Scale:"),
			0.1f,
			4.0f,
			0.05f,
			_uvScale,
			(val) =>
			{
				_uvScale = val;
				ApplyLiveTerrainUpdate();
			},
			"0.00x",
			140f
		);

		(_sldStochasticTileSize, _lblStochasticTileSize) = AddSlider(
			contentVBox,
			TranslationServer.Translate("Stochastic Size:"),
			0.5f,
			3.0f,
			0.05f,
			_stochasticTileSize,
			(val) =>
			{
				_stochasticTileSize = val;
				ApplyLiveTerrainUpdate();
			},
			"0.00x",
			140f
		);

		(_sldCrossFade, _lblCrossFade) = AddSlider(
			contentVBox,
			TranslationServer.Translate("Cross-Fade:"),
			0.0f,
			10.0f,
			0.25f,
			_crossFade,
			(val) =>
			{
				_crossFade = val;
				ApplyLiveTerrainUpdate();
			},
			"0.0'%'",
			140f
		);
	}

	private void BuildPathingSection(VBoxContainer contentVBox)
	{
		AddSectionHeader(contentVBox, "🚶 " + TranslationServer.Translate("DEFAULT PATHING CAPABILITIES"), new Color(0.85f, 0.65f, 0.35f));
		AddDescription(contentVBox, TranslationServer.Translate("Automatically assigned to cells when painted with this terrain swatch:"));
		_chkPathGround = AddCheckBox(contentVBox, TranslationServer.Translate("Ground"), (_defaultPathingCode & Realm.Client.EditableTerrain.PATHING_GROUND) != 0, (v) => UpdatePathingMask());
		_chkPathBuildable = AddCheckBox(contentVBox, TranslationServer.Translate("Buildable"), (_defaultPathingCode & Realm.Client.EditableTerrain.PATHING_BUILDABLE) != 0, (v) => UpdatePathingMask());
		_chkPathShallow = AddCheckBox(contentVBox, TranslationServer.Translate("Shallow Water"), (_defaultPathingCode & Realm.Client.EditableTerrain.PATHING_SHALLOW_WATER) != 0, (v) => UpdatePathingMask());
		_chkPathDeep = AddCheckBox(contentVBox, TranslationServer.Translate("Deep Water"), (_defaultPathingCode & Realm.Client.EditableTerrain.PATHING_DEEP_WATER) != 0, (v) => UpdatePathingMask());
		_chkPathFlying = AddCheckBox(contentVBox, TranslationServer.Translate("Flying"), (_defaultPathingCode & Realm.Client.EditableTerrain.PATHING_FLYING) != 0, (v) => UpdatePathingMask());
	}

	private void BuildDecalSection(VBoxContainer contentVBox)
	{
		AddSectionHeader(contentVBox, "🎯 " + TranslationServer.Translate("PROCEDURAL DECAL BOMBING"), new Color(0.9f, 0.5f, 0.7f));
		AddDescription(contentVBox, TranslationServer.Translate("Randomly scattered decals placed at terrain elevation during texture painting:"));
		var decalListRow = new HBoxContainer();
		_lstDecals = new ItemList { CustomMinimumSize = new Vector2(240, 75), SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
		_lstDecals.ItemSelected += OnDecalItemSelected;
		decalListRow.AddChild(_lstDecals);

		var decalBtnBox = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
		(_txtDecalId, _setDecalIdValue) = AddAssetFilterDropdown(
			decalBtnBox, string.Empty, string.Empty, (all) => ScanAvailableAssets("decals", all), (val) => { }, TranslationServer.Translate("decal_texture_id"), 0f);

		var btnAddDecal = new Button { Text = "+ " + TranslationServer.Translate("Add Decal") };
		btnAddDecal.Set("icon_max_width", 0);
		btnAddDecal.Pressed += OnAddDecalRule;
		decalBtnBox.AddChild(btnAddDecal);

		var btnRemoveDecal = new Button { Text = "✕ " + TranslationServer.Translate("Remove") };
		btnRemoveDecal.Set("icon_max_width", 0);
		btnRemoveDecal.Pressed += OnRemoveDecalRule;
		decalBtnBox.AddChild(btnRemoveDecal);

		decalListRow.AddChild(decalBtnBox);
		contentVBox.AddChild(decalListRow);

		_sldDecalDensity = AddSlider(contentVBox, TranslationServer.Translate("Decal Density:"), 0.0f, 1.0f, 0.01f, 0.5f, OnDecalDensityChanged).Slider;
		_sldDecalMinScale = AddSlider(contentVBox, TranslationServer.Translate("Min Decal Scale:"), 0.05f, 1.0f, 0.01f, 0.2f, OnDecalMinScaleChanged).Slider;
		_sldDecalMaxScale = AddSlider(contentVBox, TranslationServer.Translate("Max Decal Scale:"), 0.05f, 1.0f, 0.01f, 0.5f, OnDecalMaxScaleChanged).Slider;
	}

	private void OnDecalItemSelected(long idx)
	{
		if (idx < 0 || idx >= _decalRules.Count) return;
		var r = _decalRules[(int)idx];
		_setDecalIdValue?.Invoke(r.DecalId);
		if (_sldDecalDensity != null) _sldDecalDensity.Value = r.Density;
		if (_sldDecalMinScale != null) _sldDecalMinScale.Value = r.MinScale;
		if (_sldDecalMaxScale != null) _sldDecalMaxScale.Value = r.MaxScale;
	}

	private void OnDecalDensityChanged(float v)
	{
		var sel = _lstDecals.GetSelectedItems();
		int idx = sel.Length > 0 ? sel[0] : 0;
		if (idx >= 0 && idx < _decalRules.Count) _decalRules[idx].Density = v;
	}

	private void OnDecalMinScaleChanged(float v)
	{
		var sel = _lstDecals.GetSelectedItems();
		int idx = sel.Length > 0 ? sel[0] : 0;
		if (idx >= 0 && idx < _decalRules.Count) _decalRules[idx].MinScale = v;
	}

	private void OnDecalMaxScaleChanged(float v)
	{
		var sel = _lstDecals.GetSelectedItems();
		int idx = sel.Length > 0 ? sel[0] : 0;
		if (idx >= 0 && idx < _decalRules.Count) _decalRules[idx].MaxScale = v;
	}

	private void BuildVfxSection(VBoxContainer contentVBox)
	{
		AddSectionHeader(contentVBox, "✨ " + TranslationServer.Translate("PROCEDURAL VFX BOMBING"), new Color(0.4f, 0.7f, 1.0f));
		AddDescription(contentVBox, TranslationServer.Translate("Randomly scattered particle systems placed at terrain elevation during painting:"));
		var vfxListRow = new HBoxContainer();
		_lstVfx = new ItemList { CustomMinimumSize = new Vector2(240, 75), SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
		_lstVfx.ItemSelected += OnVfxItemSelected;
		vfxListRow.AddChild(_lstVfx);

		var vfxBtnBox = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
		(_txtVfxId, _setVfxIdValue) = AddAssetFilterDropdown(
			vfxBtnBox, string.Empty, string.Empty, (all) => ScanAvailableAssets("vfx", all), (val) => { }, TranslationServer.Translate("vfx_template_id"), 0f);

		var btnAddVfx = new Button { Text = "+ " + TranslationServer.Translate("Add VFX") };
		btnAddVfx.Set("icon_max_width", 0);
		btnAddVfx.Pressed += OnAddVfxRule;
		vfxBtnBox.AddChild(btnAddVfx);

		var btnRemoveVfx = new Button { Text = "✕ " + TranslationServer.Translate("Remove") };
		btnRemoveVfx.Set("icon_max_width", 0);
		btnRemoveVfx.Pressed += OnRemoveVfxRule;
		vfxBtnBox.AddChild(btnRemoveVfx);

		vfxListRow.AddChild(vfxBtnBox);
		contentVBox.AddChild(vfxListRow);

		_sldVfxDensity = AddSlider(contentVBox, TranslationServer.Translate("VFX Density:"), 0.0f, 1.0f, 0.01f, 0.5f, OnVfxDensityChanged).Slider;
		_sldVfxMinScale = AddSlider(contentVBox, TranslationServer.Translate("Min VFX Scale:"), 0.05f, 1.0f, 0.01f, 0.2f, OnVfxMinScaleChanged).Slider;
		_sldVfxMaxScale = AddSlider(contentVBox, TranslationServer.Translate("Max VFX Scale:"), 0.05f, 1.0f, 0.01f, 0.5f, OnVfxMaxScaleChanged).Slider;
	}

	private void OnVfxItemSelected(long idx)
	{
		if (idx < 0 || idx >= _vfxRules.Count) return;
		var r = _vfxRules[(int)idx];
		_setVfxIdValue?.Invoke(r.VfxId);
		if (_sldVfxDensity != null) _sldVfxDensity.Value = r.Density;
		if (_sldVfxMinScale != null) _sldVfxMinScale.Value = r.MinScale;
		if (_sldVfxMaxScale != null) _sldVfxMaxScale.Value = r.MaxScale;
	}

	private void OnVfxDensityChanged(float v)
	{
		var sel = _lstVfx.GetSelectedItems();
		int idx = sel.Length > 0 ? sel[0] : 0;
		if (idx >= 0 && idx < _vfxRules.Count) _vfxRules[idx].Density = v;
	}

	private void OnVfxMinScaleChanged(float v)
	{
		var sel = _lstVfx.GetSelectedItems();
		int idx = sel.Length > 0 ? sel[0] : 0;
		if (idx >= 0 && idx < _vfxRules.Count) _vfxRules[idx].MinScale = v;
	}

	private void OnVfxMaxScaleChanged(float v)
	{
		var sel = _lstVfx.GetSelectedItems();
		int idx = sel.Length > 0 ? sel[0] : 0;
		if (idx >= 0 && idx < _vfxRules.Count) _vfxRules[idx].MaxScale = v;
	}

	private List<string> ScanTerrainRtexAssets(bool includeAllFolders)
	{
		var list = ScanAvailableAssets("textures", includeAllFolders);
		var rtexFiles = new HashSet<string>(list.Where(x => x.EndsWith(".rtex", StringComparison.OrdinalIgnoreCase)), StringComparer.OrdinalIgnoreCase);

		string wsPath = Services.MapWorkspaceService.GetActiveWorkspacePath();
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
		if (!MetadataService.Instance.TryLoadMetadata(wsPath, out var meta) || meta?.Textures == null) return;

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
		mask = ApplyPathingFlag(mask, _chkPathGround, Realm.Client.EditableTerrain.PATHING_GROUND);
		mask = ApplyPathingFlag(mask, _chkPathBuildable, Realm.Client.EditableTerrain.PATHING_BUILDABLE);
		mask = ApplyPathingFlag(mask, _chkPathShallow, Realm.Client.EditableTerrain.PATHING_SHALLOW_WATER);
		mask = ApplyPathingFlag(mask, _chkPathDeep, Realm.Client.EditableTerrain.PATHING_DEEP_WATER);
		mask = ApplyPathingFlag(mask, _chkPathFlying, Realm.Client.EditableTerrain.PATHING_FLYING);
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
		helpIcon.Text = "❓";
		helpIcon.TooltipText = TranslationServer.Translate("NOTE: unavailable on your current graphics preset");
		helpIcon.MouseFilter = Control.MouseFilterEnum.Stop;
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
		if (Realm.Client.Core.GameHost.Instance != null && Realm.Client.Core.GameHost.Instance.GroundTerrain != null)
		{
			Realm.Client.Core.GameHost.Instance.GroundTerrain.UpdateTextureParamDirect(
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
		string wsPathTex = Services.MapWorkspaceService.GetActiveWorkspacePath();
		if (string.IsNullOrEmpty(wsPathTex)) return false;
		if (!MetadataService.Instance.TryLoadMetadata(wsPathTex, out var metaTex)) return false;
		if (metaTex?.Textures == null) return false;

		if (TryGetTexturePathFromMetadata(metaTex, _textureFileName)) return true;
		if (TryGetTexturePathFromMetadata(metaTex, _slug)) return true;

		return false;
	}

	private bool TryGetTexturePathFromMetadata(Realm.Shared.Metadata.MapMetadata metaTex, string key)
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
		string candidateMatch = candidates.FirstOrDefault(c => string.Equals(c, $"{_slug}.rtex", StringComparison.OrdinalIgnoreCase))
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
		_defaultPathingCode = Realm.Client.EditableTerrain.PATHING_GROUND | Realm.Client.EditableTerrain.PATHING_BUILDABLE | Realm.Client.EditableTerrain.PATHING_FLYING;
		_decalRules.Clear();
		_vfxRules.Clear();
		_swatchIndex = -1;
	}

	private void LoadMetadataProperties()
	{
		string wsPath = Services.MapWorkspaceService.GetActiveWorkspacePath();
		if (string.IsNullOrEmpty(wsPath)) return;
		if (!MetadataService.Instance.TryLoadMetadata(wsPath, out var metaRoot) || metaRoot == null) return;

		var existingTex = metaRoot.GetTerrainTexture(_textureFileName) ?? metaRoot.GetTerrainTexture(_slug);
		if (existingTex == null) return;

		ApplyMetadataBasicProps(existingTex);
		ApplyMetadataScaleProps(existingTex);
		ApplyMetadataMiscProps(existingTex);
		
		LoadMetadataBombingRules(existingTex);
	}

	private void ApplyMetadataBasicProps(Realm.Shared.Metadata.TextureMetadata existingTex)
	{
		_swatchIndex = existingTex.SwatchIndex;
		if (!string.IsNullOrEmpty(existingTex.TexturePath)) _rtexAsset = Path.GetFileName(existingTex.TexturePath);
		if (existingTex.Brightness > 0.0001f) _brightness = existingTex.Brightness;
		if (!string.IsNullOrEmpty(existingTex.Tint) && Color.HtmlIsValid(existingTex.Tint)) _tint = Color.FromHtml(existingTex.Tint);
	}

	private void ApplyMetadataScaleProps(Realm.Shared.Metadata.TextureMetadata existingTex)
	{
		if (existingTex.RoughnessScale > 0.0001f) _roughnessScale = existingTex.RoughnessScale;
		if (existingTex.NormalScale >= 0.0f) _normalScale = existingTex.NormalScale;
		if (existingTex.HeightScale > 0.0001f) _heightScale = existingTex.HeightScale;
		_heightOffset = existingTex.HeightOffset;
		if (existingTex.CrevicePower > 0.0001f) _crevicePower = existingTex.CrevicePower;
	}

	private void ApplyMetadataMiscProps(Realm.Shared.Metadata.TextureMetadata existingTex)
	{
		if (!string.IsNullOrEmpty(existingTex.TileMode)) _tileMode = existingTex.TileMode;
		if (existingTex.UvScale > 0.0001f) _uvScale = existingTex.UvScale;
		if (existingTex.StochasticTileSize > 0.0001f) _stochasticTileSize = existingTex.StochasticTileSize;
		if (existingTex.CrossFade >= 0.0f) _crossFade = existingTex.CrossFade;
		_defaultPathingCode = existingTex.DefaultPathingCode;
	}

	private void LoadMetadataBombingRules(Realm.Shared.Metadata.TextureMetadata existingTex)
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
			var list = System.Text.Json.JsonSerializer.Deserialize<List<ProceduralBombingDecalRule>>(dbrNode.ToJsonString());
			if (list != null && list.Count > 0) _decalRules = list;
		}
		if (textureData.TryGetPropertyValue("VfxBombingRules", out var vbrNode) && vbrNode != null)
		{
			var list = System.Text.Json.JsonSerializer.Deserialize<List<ProceduralBombingVfxRule>>(vbrNode.ToJsonString());
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
		return data.TryGetPropertyValue(key, out var node) && node != null && float.TryParse(node.ToString(), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out result);
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
		string wsPath = Services.MapWorkspaceService.GetActiveWorkspacePath();
		if (string.IsNullOrEmpty(wsPath) || !MetadataService.Instance.TryLoadMetadata(wsPath, out var metaRoot) || metaRoot?.Textures == null) return;

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
		if (_chkPathGround != null) _chkPathGround.ButtonPressed = (_defaultPathingCode & Realm.Client.EditableTerrain.PATHING_GROUND) != 0;
		if (_chkPathBuildable != null) _chkPathBuildable.ButtonPressed = (_defaultPathingCode & Realm.Client.EditableTerrain.PATHING_BUILDABLE) != 0;
		if (_chkPathShallow != null) _chkPathShallow.ButtonPressed = (_defaultPathingCode & Realm.Client.EditableTerrain.PATHING_SHALLOW_WATER) != 0;
		if (_chkPathDeep != null) _chkPathDeep.ButtonPressed = (_defaultPathingCode & Realm.Client.EditableTerrain.PATHING_DEEP_WATER) != 0;
		if (_chkPathFlying != null) _chkPathFlying.ButtonPressed = (_defaultPathingCode & Realm.Client.EditableTerrain.PATHING_FLYING) != 0;
	}

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

		MapEditorHUD.Instance?.SetupTextureSwatches(false);
		if (Realm.Client.Core.GameHost.Instance?.GroundTerrain != null)
		{
			Realm.Client.Core.GameHost.Instance.GroundTerrain.ClearLiveSwatchOverrides();
			Realm.Client.Core.GameHost.Instance.GroundTerrain.ReloadTerrainTextures(true);
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
			["DecalBombingRules"] = System.Text.Json.JsonSerializer.SerializeToNode(_decalRules),
			["VfxBombingRules"] = System.Text.Json.JsonSerializer.SerializeToNode(_vfxRules)
		};
	}

	private void SaveTextureToMetadata(string newTemplateID)
	{
		string wsPath = Services.MapWorkspaceService.GetActiveWorkspacePath();
		string metaPath = Path.Combine(wsPath, "metadata.json");
		
		if (!File.Exists(metaPath)) return;
		if (!MetadataService.Instance.TryLoadMetadata(wsPath, out var metadataRoot) || metadataRoot == null) return;
		
		if (!string.Equals(_textureFileName, newTemplateID, StringComparison.OrdinalIgnoreCase) && !string.IsNullOrEmpty(_textureFileName))
		{
			metadataRoot.Textures?.Remove(_textureFileName);
			string alternateOld = _textureFileName.StartsWith("terrain/", StringComparison.OrdinalIgnoreCase)
				? _textureFileName.Substring("terrain/".Length)
				: $"terrain/{_textureFileName}";
			metadataRoot.Textures?.Remove(alternateOld);
		}

		metadataRoot.Textures ??= new(StringComparer.OrdinalIgnoreCase);
		var texMeta = new Realm.Shared.Metadata.TextureMetadata
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

		MetadataService.Instance.SaveMetadata(metaPath, metadataRoot);
	}

	protected override void OnCancel()
	{
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
			if (Realm.Client.Core.GameHost.Instance?.GroundTerrain != null)
			{
				Realm.Client.Core.GameHost.Instance.GroundTerrain.ClearLiveSwatchOverrides(_textureFileName);
				Realm.Client.Core.GameHost.Instance.GroundTerrain.ReloadTerrainTextures(true);
			}
		}
	}
}