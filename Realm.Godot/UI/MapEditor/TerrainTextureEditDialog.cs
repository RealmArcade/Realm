using Godot;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using Realm.Godot.Services;
using Realm.Godot.Utils;

public class TerrainTextureSnapshot
{
	public float Brightness { get; set; } = 1.0f;
	public Color Tint { get; set; } = Colors.White;
	public float HeightScale { get; set; } = 1.0f;
	public float HeightOffset { get; set; } = 0.0f;
	public float CrevicePower { get; set; } = 1.0f;
	public float NormalScale { get; set; } = 1.0f;
	public float RoughnessScale { get; set; } = 1.0f;
	public string TileMode { get; set; } = "Stochastic";
	public float UvScale { get; set; } = 1.0f;
	public float StochasticTileSize { get; set; } = 1.0f;
	public float CrossFade { get; set; } = 0.0f;
	public int DefaultPathingCode { get; set; } = EditableTerrain.PATHING_GROUND | EditableTerrain.PATHING_BUILDABLE | EditableTerrain.PATHING_FLYING;
	public List<ProceduralBombingDecalRule> DecalBombingRules { get; set; } = new();
	public List<ProceduralBombingVfxRule> VfxBombingRules { get; set; } = new();

	public TerrainTextureSnapshot Clone()
	{
		var clone = new TerrainTextureSnapshot
		{
			Brightness = this.Brightness,
			Tint = this.Tint,
			HeightScale = this.HeightScale,
			HeightOffset = this.HeightOffset,
			CrevicePower = this.CrevicePower,
			NormalScale = this.NormalScale,
			RoughnessScale = this.RoughnessScale,
			TileMode = this.TileMode,
			UvScale = this.UvScale,
			StochasticTileSize = this.StochasticTileSize,
			CrossFade = this.CrossFade,
			DefaultPathingCode = this.DefaultPathingCode,
			DecalBombingRules = new List<ProceduralBombingDecalRule>(),
			VfxBombingRules = new List<ProceduralBombingVfxRule>()
		};
		if (this.DecalBombingRules != null)
		{
			foreach (var r in this.DecalBombingRules) clone.DecalBombingRules.Add(r.Clone());
		}
		if (this.VfxBombingRules != null)
		{
			foreach (var r in this.VfxBombingRules) clone.VfxBombingRules.Add(r.Clone());
		}
		return clone;
	}
}

public class TerrainTextureUndoAction : IEditorAction
{
	private readonly string _textureFileName;
	private readonly TerrainTextureSnapshot _before;
	private readonly TerrainTextureSnapshot _after;

	public TerrainTextureUndoAction(string textureFileName, TerrainTextureSnapshot before, TerrainTextureSnapshot after)
	{
		_textureFileName = textureFileName;
		_before = before;
		_after = after;
	}

	public void Undo()
	{
		ApplySnapshot(_before);
	}

	public void Redo()
	{
		ApplySnapshot(_after);
	}

	private void ApplySnapshot(TerrainTextureSnapshot snapshot)
	{
		if (string.IsNullOrEmpty(_textureFileName)) return;

		string tintHex = $"#{snapshot.Tint.ToHtml(false)}";

		if (GameHost.Instance != null && GameHost.Instance.GroundTerrain != null)
		{
			GameHost.Instance.GroundTerrain.UpdateTextureParamDirect(
				_textureFileName,
				snapshot.TileMode,
				snapshot.UvScale,
				snapshot.StochasticTileSize,
				snapshot.CrossFade,
				snapshot.Brightness,
				tintHex,
				snapshot.HeightScale,
				snapshot.HeightOffset,
				snapshot.CrevicePower,
				snapshot.NormalScale,
				snapshot.RoughnessScale
			);
		}

		try
		{
			string wsPath = MapWorkspaceService.GetActiveWorkspacePath();
			var assetsObj = Realm.Godot.Utils.MapAssetHelper.LoadAssets(wsPath);

			string metaPath = Path.Combine(wsPath, "metadata.json");
			if (File.Exists(metaPath) && MetadataService.Instance.TryLoadMetadata(wsPath, out var metadataRoot) && metadataRoot != null)
			{
				var prof = new TerrainSwatchProfileData
				{
					SwatchName = _textureFileName,
					DefaultPathingCode = snapshot.DefaultPathingCode,
					DecalBombingRules = snapshot.DecalBombingRules != null ? new List<ProceduralBombingDecalRule>(snapshot.DecalBombingRules) : new(),
					VfxBombingRules = snapshot.VfxBombingRules != null ? new List<ProceduralBombingVfxRule>(snapshot.VfxBombingRules) : new()
				};
				metadataRoot.AddOrUpdateTerrainProfile(prof);
				MetadataService.Instance.SaveMetadata(metaPath, metadataRoot);
				MapEditorHUD.Instance?.UpdateLastMetadataSyncTime(metaPath);
			}
		}
		catch { }
	}
}

public partial class TerrainTextureEditDialog : FloatingDialogBase
{
	private string _textureFileName = "";
	private string _objectType = "terrain";
	private string _slug = "";
	private string _rtexAsset = "";

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

		// SECTION 0: IDENTITY & TEXTURE ASSET
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

		// SECTION 1: COLOR & LIGHTING
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

		// SECTION 2: HEIGHTMAP & CREVICE BLENDING
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

		// SECTION 3: TILING & PROJECTION
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

		// SECTION 4: DEFAULT PATHING
		AddSectionHeader(contentVBox, "🚶 " + TranslationServer.Translate("DEFAULT PATHING CAPABILITIES"), new Color(0.85f, 0.65f, 0.35f));
		AddDescription(contentVBox, TranslationServer.Translate("Automatically assigned to cells when painted with this terrain swatch:"));
		_chkPathGround = AddCheckBox(contentVBox, TranslationServer.Translate("Ground"), (_defaultPathingCode & EditableTerrain.PATHING_GROUND) != 0, (v) => UpdatePathingMask());
		_chkPathBuildable = AddCheckBox(contentVBox, TranslationServer.Translate("Buildable"), (_defaultPathingCode & EditableTerrain.PATHING_BUILDABLE) != 0, (v) => UpdatePathingMask());
		_chkPathShallow = AddCheckBox(contentVBox, TranslationServer.Translate("Shallow Water"), (_defaultPathingCode & EditableTerrain.PATHING_SHALLOW_WATER) != 0, (v) => UpdatePathingMask());
		_chkPathDeep = AddCheckBox(contentVBox, TranslationServer.Translate("Deep Water"), (_defaultPathingCode & EditableTerrain.PATHING_DEEP_WATER) != 0, (v) => UpdatePathingMask());
		_chkPathFlying = AddCheckBox(contentVBox, TranslationServer.Translate("Flying"), (_defaultPathingCode & EditableTerrain.PATHING_FLYING) != 0, (v) => UpdatePathingMask());

		// SECTION 5: PROCEDURAL DECAL BOMBING
		AddSectionHeader(contentVBox, "🎯 " + TranslationServer.Translate("PROCEDURAL DECAL BOMBING"), new Color(0.9f, 0.5f, 0.7f));
		AddDescription(contentVBox, TranslationServer.Translate("Randomly scattered decals placed at terrain elevation during texture painting:"));
		var decalListRow = new HBoxContainer();
		_lstDecals = new ItemList { CustomMinimumSize = new Vector2(240, 75), SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
		_lstDecals.ItemSelected += (idx) =>
		{
			if (idx >= 0 && idx < _decalRules.Count)
			{
				var r = _decalRules[(int)idx];
				_setDecalIdValue?.Invoke(r.DecalId);
				if (_sldDecalDensity != null) _sldDecalDensity.Value = r.Density;
				if (_sldDecalMinScale != null) _sldDecalMinScale.Value = r.MinScale;
				if (_sldDecalMaxScale != null) _sldDecalMaxScale.Value = r.MaxScale;
			}
		};
		decalListRow.AddChild(_lstDecals);

		var decalBtnBox = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
		(_txtDecalId, _setDecalIdValue) = AddAssetFilterDropdown(
			decalBtnBox,
			string.Empty,
			string.Empty,
			(all) => ScanAvailableAssets("decals", all),
			(val) => { },
			TranslationServer.Translate("decal_texture_id"),
			0f
		);

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

		var sldDecalDensRes = AddSlider(contentVBox, TranslationServer.Translate("Decal Density:"), 0.0f, 1.0f, 0.01f, 0.5f, (v) =>
		{
			var sel = _lstDecals.GetSelectedItems();
			int idx = sel.Length > 0 ? sel[0] : 0;
			if (idx >= 0 && idx < _decalRules.Count) _decalRules[idx].Density = v;
		});
		_sldDecalDensity = sldDecalDensRes.Slider;
		var sldDecalMinScRes = AddSlider(contentVBox, TranslationServer.Translate("Min Decal Scale:"), 0.05f, 1.0f, 0.01f, 0.2f, (v) =>
		{
			var sel = _lstDecals.GetSelectedItems();
			int idx = sel.Length > 0 ? sel[0] : 0;
			if (idx >= 0 && idx < _decalRules.Count) _decalRules[idx].MinScale = v;
		});
		_sldDecalMinScale = sldDecalMinScRes.Slider;
		var sldDecalMaxScRes = AddSlider(contentVBox, TranslationServer.Translate("Max Decal Scale:"), 0.05f, 1.0f, 0.01f, 0.5f, (v) =>
		{
			var sel = _lstDecals.GetSelectedItems();
			int idx = sel.Length > 0 ? sel[0] : 0;
			if (idx >= 0 && idx < _decalRules.Count) _decalRules[idx].MaxScale = v;
		});
		_sldDecalMaxScale = sldDecalMaxScRes.Slider;

		// SECTION 6: PROCEDURAL VFX BOMBING
		AddSectionHeader(contentVBox, "✨ " + TranslationServer.Translate("PROCEDURAL VFX BOMBING"), new Color(0.4f, 0.7f, 1.0f));
		AddDescription(contentVBox, TranslationServer.Translate("Randomly scattered particle systems placed at terrain elevation during painting:"));
		var vfxListRow = new HBoxContainer();
		_lstVfx = new ItemList { CustomMinimumSize = new Vector2(240, 75), SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
		_lstVfx.ItemSelected += (idx) =>
		{
			if (idx >= 0 && idx < _vfxRules.Count)
			{
				var r = _vfxRules[(int)idx];
				_setVfxIdValue?.Invoke(r.VfxId);
				if (_sldVfxDensity != null) _sldVfxDensity.Value = r.Density;
				if (_sldVfxMinScale != null) _sldVfxMinScale.Value = r.MinScale;
				if (_sldVfxMaxScale != null) _sldVfxMaxScale.Value = r.MaxScale;
			}
		};
		vfxListRow.AddChild(_lstVfx);

		var vfxBtnBox = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
		(_txtVfxId, _setVfxIdValue) = AddAssetFilterDropdown(
			vfxBtnBox,
			string.Empty,
			string.Empty,
			(all) => ScanAvailableAssets("vfx", all),
			(val) => { },
			TranslationServer.Translate("vfx_template_id"),
			0f
		);

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

		var sldVfxDensRes = AddSlider(contentVBox, TranslationServer.Translate("VFX Density:"), 0.0f, 1.0f, 0.01f, 0.5f, (v) =>
		{
			var sel = _lstVfx.GetSelectedItems();
			int idx = sel.Length > 0 ? sel[0] : 0;
			if (idx >= 0 && idx < _vfxRules.Count) _vfxRules[idx].Density = v;
		});
		_sldVfxDensity = sldVfxDensRes.Slider;
		var sldVfxMinScRes = AddSlider(contentVBox, TranslationServer.Translate("Min VFX Scale:"), 0.05f, 1.0f, 0.01f, 0.2f, (v) =>
		{
			var sel = _lstVfx.GetSelectedItems();
			int idx = sel.Length > 0 ? sel[0] : 0;
			if (idx >= 0 && idx < _vfxRules.Count) _vfxRules[idx].MinScale = v;
		});
		_sldVfxMinScale = sldVfxMinScRes.Slider;
		var sldVfxMaxScRes = AddSlider(contentVBox, TranslationServer.Translate("Max VFX Scale:"), 0.05f, 1.0f, 0.01f, 0.5f, (v) =>
		{
			var sel = _lstVfx.GetSelectedItems();
			int idx = sel.Length > 0 ? sel[0] : 0;
			if (idx >= 0 && idx < _vfxRules.Count) _vfxRules[idx].MaxScale = v;
		});
		_sldVfxMaxScale = sldVfxMaxScRes.Slider;

		UpdateTileModeVisibility();
		UpdateGraphicsQualityState();
	}

	private List<string> ScanTerrainRtexAssets(bool includeAllFolders)
	{
		var list = ScanAvailableAssets("textures", includeAllFolders);
		string wsPath = MapWorkspaceService.GetActiveWorkspacePath();
		var rtexFiles = new HashSet<string>(list, StringComparer.OrdinalIgnoreCase);

		string searchDir = Path.Combine(wsPath, "Assets", "textures");
		if (Directory.Exists(searchDir))
		{
			foreach (var file in Directory.GetFiles(searchDir, "*.rtex", SearchOption.AllDirectories))
			{
				rtexFiles.Add(Path.GetFileName(file));
			}
		}

		string templateDir = Path.Combine(ProjectSettings.GlobalizePath("res://"), "Assets", "textures");
		if (Directory.Exists(templateDir))
		{
			foreach (var file in Directory.GetFiles(templateDir, "*.rtex", SearchOption.AllDirectories))
			{
				rtexFiles.Add(Path.GetFileName(file));
			}
		}

		if (MetadataService.Instance.TryLoadMetadata(wsPath, out var meta) && meta?.Textures != null)
		{
			foreach (var kvp in meta.Textures)
			{
				if (!string.IsNullOrWhiteSpace(kvp.Value?.AssetType))
				{
					rtexFiles.Add(kvp.Value.AssetType);
				}
				if (kvp.Key.EndsWith(".rtex", StringComparison.OrdinalIgnoreCase))
				{
					rtexFiles.Add(kvp.Key);
				}
			}
		}

		return rtexFiles.OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToList();
	}

	private void UpdatePathingMask()
	{
		int mask = 0;
		if (_chkPathGround != null && _chkPathGround.ButtonPressed) mask |= EditableTerrain.PATHING_GROUND;
		if (_chkPathBuildable != null && _chkPathBuildable.ButtonPressed) mask |= EditableTerrain.PATHING_BUILDABLE;
		if (_chkPathShallow != null && _chkPathShallow.ButtonPressed) mask |= EditableTerrain.PATHING_SHALLOW_WATER;
		if (_chkPathDeep != null && _chkPathDeep.ButtonPressed) mask |= EditableTerrain.PATHING_DEEP_WATER;
		if (_chkPathFlying != null && _chkPathFlying.ButtonPressed) mask |= EditableTerrain.PATHING_FLYING;
		_defaultPathingCode = mask;
	}

	private void OnAddDecalRule()
	{
		string id = _txtDecalId?.Text?.Trim() ?? string.Empty;
		if (string.IsNullOrEmpty(id)) return;
		_decalRules.Add(new ProceduralBombingDecalRule
		{
			DecalId = id,
			Density = (float)(_sldDecalDensity?.Value ?? 0.5f),
			MinScale = (float)(_sldDecalMinScale?.Value ?? 0.2f),
			MaxScale = (float)(_sldDecalMaxScale?.Value ?? 0.5f)
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
		string id = _txtVfxId?.Text?.Trim() ?? string.Empty;
		if (string.IsNullOrEmpty(id)) return;
		_vfxRules.Add(new ProceduralBombingVfxRule
		{
			VfxId = id,
			Density = (float)(_sldVfxDensity?.Value ?? 0.5f),
			MinScale = (float)(_sldVfxMinScale?.Value ?? 0.2f),
			MaxScale = (float)(_sldVfxMaxScale?.Value ?? 0.5f)
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
		if (GameHost.Instance != null && GameHost.Instance.GroundTerrain != null)
		{
			GameHost.Instance.GroundTerrain.UpdateTextureParamDirect(
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

		_rtexAsset = textureData?["AssetType"]?.ToString()
			?? textureData?["assetType"]?.ToString()
			?? textureData?["rtex"]?.ToString()
			?? textureData?["TexturePath"]?.ToString()
			?? textureData?["asset_type"]?.ToString()
			?? string.Empty;

		if (string.IsNullOrEmpty(_rtexAsset) && textureData != null)
		{
			string hash = textureData["Hash"]?.ToString() ?? textureData["hash"]?.ToString() ?? string.Empty;
			if (hash.EndsWith(".rtex", StringComparison.OrdinalIgnoreCase))
			{
				_rtexAsset = hash;
			}
		}

		if (string.IsNullOrEmpty(_rtexAsset))
		{
			_rtexAsset = _slug.EndsWith(".rtex", StringComparison.OrdinalIgnoreCase) ? _slug : $"{_slug}.rtex";
		}

		_onApplied = onApplied;

		TitleLabel.Text = $"{TranslationServer.Translate("Edit Texture Swatch")} - {_textureFileName}";

		if (_lblObjectTypePrefix != null) _lblObjectTypePrefix.Text = $"{_objectType}/";
		if (_txtSlug != null) _txtSlug.Text = _slug;
		_setRtexAssetValue?.Invoke(_rtexAsset);

		RuntimeTerrain.ActiveSwatchConfig activeConfig = default;
		if (GameHost.Instance != null && GameHost.Instance.GroundTerrain != null)
		{
			activeConfig = GameHost.Instance.GroundTerrain.GetActiveSwatchConfig(_textureFileName);
		}
		else
		{
			activeConfig = new RuntimeTerrain.ActiveSwatchConfig
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

		_brightness = activeConfig.Brightness;
		_tint = activeConfig.Tint;
		_roughnessScale = activeConfig.RoughnessScale;
		_normalScale = activeConfig.NormalScale;
		_heightScale = activeConfig.HeightScale;
		_heightOffset = activeConfig.HeightOffset;
		_crevicePower = activeConfig.CrevicePower;
		_tileMode = activeConfig.TileMode;
		_uvScale = activeConfig.UvScale;
		_stochasticTileSize = activeConfig.StochasticTileSize;
		_crossFade = activeConfig.CrossFade;
		_defaultPathingCode = EditableTerrain.PATHING_GROUND | EditableTerrain.PATHING_BUILDABLE | EditableTerrain.PATHING_FLYING;
		_decalRules.Clear();
		_vfxRules.Clear();

		string wsPath = MapWorkspaceService.GetActiveWorkspacePath();
		if (!string.IsNullOrEmpty(wsPath) && MetadataService.Instance.TryLoadMetadata(wsPath, out var metaRoot) && metaRoot != null)
		{
			var existingProf = metaRoot.GetTerrainProfile(_textureFileName);
			if (existingProf != null)
			{
				_defaultPathingCode = existingProf.DefaultPathingCode;
				if (existingProf.DecalBombingRules != null)
				{
					foreach (var r in existingProf.DecalBombingRules) _decalRules.Add(r.Clone());
				}
				if (existingProf.VfxBombingRules != null)
				{
					foreach (var r in existingProf.VfxBombingRules) _vfxRules.Add(r.Clone());
				}
			}
		}

		if (textureData != null)
		{
			string brightStr = textureData["Brightness"]?.ToString() ?? textureData["brightness"]?.ToString();
			if (!string.IsNullOrEmpty(brightStr) && float.TryParse(brightStr, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float parsedB))
			{
				_brightness = parsedB;
			}

			string tintStr = textureData["Tint"]?.ToString() ?? textureData["tint"]?.ToString();
			if (!string.IsNullOrEmpty(tintStr) && Color.HtmlIsValid(tintStr))
			{
				_tint = Color.FromHtml(tintStr);
			}

			string roughStr = textureData["Roughness_Scale"]?.ToString() ?? textureData["roughness_scale"]?.ToString() ?? textureData["roughnessScale"]?.ToString();
			if (!string.IsNullOrEmpty(roughStr) && float.TryParse(roughStr, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float parsedR))
			{
				_roughnessScale = parsedR;
			}

			string normStr = textureData["Normal_Scale"]?.ToString() ?? textureData["normal_scale"]?.ToString() ?? textureData["normalScale"]?.ToString();
			if (!string.IsNullOrEmpty(normStr) && float.TryParse(normStr, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float parsedN))
			{
				_normalScale = parsedN;
			}

			string hsStr = textureData["Height_Scale"]?.ToString() ?? textureData["height_scale"]?.ToString() ?? textureData["heightScale"]?.ToString();
			if (!string.IsNullOrEmpty(hsStr) && float.TryParse(hsStr, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float parsedHs))
			{
				_heightScale = parsedHs;
			}

			string hoStr = textureData["Height_Offset"]?.ToString() ?? textureData["height_offset"]?.ToString() ?? textureData["heightOffset"]?.ToString();
			if (!string.IsNullOrEmpty(hoStr) && float.TryParse(hoStr, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float parsedHo))
			{
				_heightOffset = parsedHo;
			}

			string cpStr = textureData["Crevice_Power"]?.ToString() ?? textureData["crevice_power"]?.ToString() ?? textureData["crevicePower"]?.ToString();
			if (!string.IsNullOrEmpty(cpStr) && float.TryParse(cpStr, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float parsedCp))
			{
				_crevicePower = parsedCp;
			}

			string tmStr = textureData["Tile_Mode"]?.ToString() ?? textureData["tile_mode"]?.ToString() ?? textureData["tileMode"]?.ToString();
			if (!string.IsNullOrEmpty(tmStr))
			{
				_tileMode = string.Equals(tmStr, "Grid", StringComparison.OrdinalIgnoreCase) ? "Grid" : "Stochastic";
			}

			string uvStr = textureData["UV_Scale"]?.ToString() ?? textureData["uv_scale"]?.ToString() ?? textureData["uvScale"]?.ToString();
			if (!string.IsNullOrEmpty(uvStr) && float.TryParse(uvStr, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float parsedUv))
			{
				_uvScale = parsedUv;
			}

			string stochStr = textureData["Stochastic_Tile_Size"]?.ToString() ?? textureData["stochastic_tile_size"]?.ToString() ?? textureData["stochasticTileSize"]?.ToString();
			if (!string.IsNullOrEmpty(stochStr) && float.TryParse(stochStr, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float parsedStoch))
			{
				_stochasticTileSize = parsedStoch;
			}

			string cfStr = textureData["Cross_Fade"]?.ToString() ?? textureData["cross_fade"]?.ToString() ?? textureData["Grid_Cross_Fade"]?.ToString() ?? textureData["grid_cross_fade"]?.ToString() ?? textureData["crossFade"]?.ToString();
			if (!string.IsNullOrEmpty(cfStr) && float.TryParse(cfStr, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float parsedCf))
			{
				_crossFade = parsedCf <= 0.10f && parsedCf > 0.0f ? parsedCf * 100.0f : parsedCf;
			}
		}

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
			DecalBombingRules = new List<ProceduralBombingDecalRule>(_decalRules),
			VfxBombingRules = new List<ProceduralBombingVfxRule>(_vfxRules)
		};

		if (_sldBrightness != null) _sldBrightness.Value = _brightness;
		if (_btnTint != null) _btnTint.Color = _tint;
		if (_sldRoughnessScale != null) _sldRoughnessScale.Value = _roughnessScale;
		if (_sldNormalScale != null) _sldNormalScale.Value = _normalScale;
		if (_sldHeightScale != null) _sldHeightScale.Value = _heightScale;
		if (_sldHeightOffset != null) _sldHeightOffset.Value = _heightOffset;
		if (_sldCrevicePower != null) _sldCrevicePower.Value = _crevicePower;
		if (_optTileMode != null) _optTileMode.Selected = _tileMode == "Stochastic" ? 1 : 0;
		if (_sldUvScale != null) _sldUvScale.Value = _uvScale;
		if (_sldStochasticTileSize != null) _sldStochasticTileSize.Value = _stochasticTileSize;
		if (_sldCrossFade != null) _sldCrossFade.Value = _crossFade;

		if (_chkPathGround != null) _chkPathGround.ButtonPressed = (_defaultPathingCode & EditableTerrain.PATHING_GROUND) != 0;
		if (_chkPathBuildable != null) _chkPathBuildable.ButtonPressed = (_defaultPathingCode & EditableTerrain.PATHING_BUILDABLE) != 0;
		if (_chkPathShallow != null) _chkPathShallow.ButtonPressed = (_defaultPathingCode & EditableTerrain.PATHING_SHALLOW_WATER) != 0;
		if (_chkPathDeep != null) _chkPathDeep.ButtonPressed = (_defaultPathingCode & EditableTerrain.PATHING_DEEP_WATER) != 0;
		if (_chkPathFlying != null) _chkPathFlying.ButtonPressed = (_defaultPathingCode & EditableTerrain.PATHING_FLYING) != 0;

		_setDecalIdValue?.Invoke(string.Empty);
		_setVfxIdValue?.Invoke(string.Empty);
		UpdateDecalList();
		UpdateVfxList();

		UpdateTileModeVisibility();
		UpdateGraphicsQualityState();
		OpenDialog();
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
			DecalBombingRules = new List<ProceduralBombingDecalRule>(_decalRules),
			VfxBombingRules = new List<ProceduralBombingVfxRule>(_vfxRules)
		};

		if (_initialSnapshot != null)
		{
			var action = new TerrainTextureUndoAction(newTemplateID, _initialSnapshot, currentSnapshot);
			EditorHistoryManager.RecordAction(action);
		}

		var result = new JsonObject
		{
			["TemplateID"] = newTemplateID,
			["AssetType"] = _rtexAsset,
			["rtex"] = _rtexAsset,
			["TexturePath"] = _rtexAsset,
			["Brightness"] = _brightness,
			["Tint"] = $"#{_tint.ToHtml(false)}",
			["Roughness_Scale"] = _roughnessScale,
			["Normal_Scale"] = _normalScale,
			["Height_Scale"] = _heightScale,
			["Height_Offset"] = _heightOffset,
			["Crevice_Power"] = _crevicePower,
			["Tile_Mode"] = _tileMode,
			["UV_Scale"] = _uvScale,
			["Stochastic_Tile_Size"] = _stochasticTileSize,
			["Cross_Fade"] = _crossFade
		};

		string wsPath = MapWorkspaceService.GetActiveWorkspacePath();
		string metaPath = Path.Combine(wsPath, "metadata.json");
		if (File.Exists(metaPath) && MetadataService.Instance.TryLoadMetadata(wsPath, out var metadataRoot) && metadataRoot != null)
		{
			var prof = new TerrainSwatchProfileData
			{
				SwatchName = newTemplateID,
				DefaultPathingCode = _defaultPathingCode,
				DecalBombingRules = new List<ProceduralBombingDecalRule>(_decalRules),
				VfxBombingRules = new List<ProceduralBombingVfxRule>(_vfxRules)
			};
			metadataRoot.AddOrUpdateTerrainProfile(prof);

			if (!string.Equals(_textureFileName, newTemplateID, StringComparison.OrdinalIgnoreCase) && !string.IsNullOrEmpty(_textureFileName))
			{
				metadataRoot.Textures?.Remove(_textureFileName);
				metadataRoot.RemoveTerrainProfile(_textureFileName);
				string alternateOld = _textureFileName.StartsWith("terrain/", StringComparison.OrdinalIgnoreCase)
					? _textureFileName.Substring("terrain/".Length)
					: $"terrain/{_textureFileName}";
				metadataRoot.Textures?.Remove(alternateOld);
				metadataRoot.RemoveTerrainProfile(alternateOld);
			}

			metadataRoot.Textures ??= new(StringComparer.OrdinalIgnoreCase);
			var texMeta = JsonSerializer.Deserialize<Realm.Shared.Metadata.TextureMetadata>(result.ToJsonString()) ?? new Realm.Shared.Metadata.TextureMetadata();
			texMeta.AssetType = _rtexAsset;
			metadataRoot.Textures[newTemplateID] = texMeta;

			MetadataService.Instance.SaveMetadata(metaPath, metadataRoot);
		}

		_onApplied?.Invoke(result);
		Hud?.ShowFeedback(string.Format(TranslationServer.Translate("Texture swatch {0} updated successfully."), newTemplateID));
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
		}
	}
}
