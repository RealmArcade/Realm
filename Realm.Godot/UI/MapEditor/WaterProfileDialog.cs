using System;
using System.Collections.Generic;
using System.IO;
using Godot;
using Realm.Ecs.Components.Terrain;
using Realm.Godot.Services;

public partial class WaterProfileDialog : FloatingDialogBase
{
	private List<WaterProfileSaveData> _profiles = new();
	private int _selectedProfileIdx = 0;
	private WaterProfileSaveData _activeProfile;
	private List<WaterProfileSaveData> _initialSnapshots = new();

	private OptionButton _optProfileSelect;
	private Button _btnAddProfile;
	private Button _btnDeleteProfile;

	private LineEdit _txtName;

	private ColorPickerButton _cpShallow;
	private ColorPickerButton _cpDeep;
	private ColorPickerButton _cpFoam;
	private HSlider _sldMaxDepth;
	private HSlider _sldFoamDepth;
	private HSlider _sldWaveSpeed;
	private HSlider _sldWaveStrength;

	private CheckBox _chkUseNormal;
	private LineEdit _txtNormalPath;
	private Action<string> _setNormalPath;
	private HSlider _sldNormalScale;
	private HSlider _sldFlowDirX;
	private HSlider _sldFlowDirY;
	private HSlider _sldFlowSpeed;
	private CheckBox _chkUseFlowMap;
	private LineEdit _txtFlowMapPath;
	private Action<string> _setFlowMapPath;

	private HSlider _sldRefraction;
	private HSlider _sldCausticStrength;
	private HSlider _sldCausticScale;
	private HSlider _sldCausticSpeed;

	private ColorPickerButton _cpEmission;
	private HSlider _sldEmissionBoost;
	private ColorPickerButton _cpCore;
	private HSlider _sldCoreThreshold;
	private ColorPickerButton _cpSubsurface;
	private HSlider _sldSubsurfaceStrength;

	private CheckBox _chkUseDetail;
	private LineEdit _txtDetailPath;
	private Action<string> _setDetailPath;
	private OptionButton _optDetailTileMode;
	private HSlider _sldDetailUvScaleX;
	private HSlider _sldDetailUvScaleY;
	private HSlider _sldDetailUvScrollX;
	private HSlider _sldDetailUvScrollY;
	private HSlider _sldDetailStochasticTileSize;
	private HSlider _sldDetailCrossFade;
	private HSlider _sldDetailAlpha;
	private OptionButton _optDetailBlend;

	private CheckBox _chkPathShallow;
	private CheckBox _chkPathDeep;
	private CheckBox _chkPathGround;
	private CheckBox _chkPathBuildable;
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

	public WaterProfileDialog(MapEditorHUD hud) : base(hud, TranslationServer.Translate("LIQUID / WATER PROFILE UBER CONFIG"), new Vector2(620, 680))
	{
		BuildUI();
	}

	private void BuildUI()
	{
		var topRow = new HBoxContainer();
		topRow.AddThemeConstantOverride("separation", 8);

		var lblProf = new Label();
		lblProf.Text = TranslationServer.Translate("Profile:");
		lblProf.AddThemeFontSizeOverride("font_size", 12);
		topRow.AddChild(lblProf);

		_optProfileSelect = new OptionButton();
		_optProfileSelect.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
		_optProfileSelect.ItemSelected += (idx) => SelectProfile((int)idx);
		topRow.AddChild(_optProfileSelect);

		_btnAddProfile = new Button();
		_btnAddProfile.Set("icon_max_width", 0);
		_btnAddProfile.Text = "+ " + TranslationServer.Translate("NEW");
		_btnAddProfile.Pressed += OnAddProfileClicked;
		topRow.AddChild(_btnAddProfile);

		_btnDeleteProfile = new Button();
		_btnDeleteProfile.Set("icon_max_width", 0);
		_btnDeleteProfile.Text = TranslationServer.Translate("DELETE");
		_btnDeleteProfile.Pressed += OnDeleteProfileClicked;
		topRow.AddChild(_btnDeleteProfile);

		BodyContainer.AddChild(topRow);

		var tabContainer = new TabContainer();
		tabContainer.SizeFlagsVertical = SizeFlags.ExpandFill;
		tabContainer.CustomMinimumSize = new Vector2(580, 520);
		BodyContainer.AddChild(tabContainer);

		var tabGeneral = new ScrollContainer();
		tabGeneral.Name = TranslationServer.Translate("General & Colors");
		var vGeneral = new VBoxContainer();
		vGeneral.AddThemeConstantOverride("separation", 6);
		vGeneral.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
		tabGeneral.AddChild(vGeneral);
		tabContainer.AddChild(tabGeneral);

		AddSectionHeader(vGeneral, TranslationServer.Translate("Profile Identity"));
		var nameRow = new HBoxContainer();
		nameRow.AddChild(new Label { Text = TranslationServer.Translate("Display Name:"), CustomMinimumSize = new Vector2(110, 0) });
		_txtName = new LineEdit { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
		_txtName.TextChanged += (text) => { if (_activeProfile != null) { _activeProfile.Name = text; UpdateProfileListUI(); } };
		nameRow.AddChild(_txtName);
		vGeneral.AddChild(nameRow);

		var btnRandomizeAll = CreateRandomizeButton(() => RandomizeAllWaterParameters());
		vGeneral.AddChild(btnRandomizeAll);

		AddSectionHeader(vGeneral, TranslationServer.Translate("Colors & Transparency"));
		var cpRes1 = AddColorPicker(vGeneral, TranslationServer.Translate("Shallow Tint:"), Colors.Teal, (c) => { if (_activeProfile != null) _activeProfile.ShallowColorHex = "#" + c.ToHtml(true); ApplyLiveMaterialPreview(); });
		_cpShallow = cpRes1.Picker;
		var cpRes2 = AddColorPicker(vGeneral, TranslationServer.Translate("Deep Tint:"), Colors.NavyBlue, (c) => { if (_activeProfile != null) _activeProfile.DeepColorHex = "#" + c.ToHtml(true); ApplyLiveMaterialPreview(); });
		_cpDeep = cpRes2.Picker;
		var cpRes3 = AddColorPicker(vGeneral, TranslationServer.Translate("Foam Tint:"), Colors.White, (c) => { if (_activeProfile != null) _activeProfile.FoamColorHex = "#" + c.ToHtml(true); ApplyLiveMaterialPreview(); });
		_cpFoam = cpRes3.Picker;

		var sldMaxDepthRes = AddSlider(vGeneral, TranslationServer.Translate("Max Depth:"), 0.1f, 10.0f, 0.1f, 2.0f, (v) => { if (_activeProfile != null) _activeProfile.MaxDepth = v; ApplyLiveMaterialPreview(); });
		_sldMaxDepth = sldMaxDepthRes.Slider;
		var sldFoamDepthRes = AddSlider(vGeneral, TranslationServer.Translate("Foam Distance:"), 0.05f, 3.0f, 0.05f, 0.6f, (v) => { if (_activeProfile != null) _activeProfile.FoamDepth = v; ApplyLiveMaterialPreview(); });
		_sldFoamDepth = sldFoamDepthRes.Slider;
		var sldWaveSpdRes = AddSlider(vGeneral, TranslationServer.Translate("Wave Speed:"), 0.0f, 5.0f, 0.1f, 1.2f, (v) => { if (_activeProfile != null) _activeProfile.WaveSpeed = v; ApplyLiveMaterialPreview(); });
		_sldWaveSpeed = sldWaveSpdRes.Slider;
		var sldWaveStrRes = AddSlider(vGeneral, TranslationServer.Translate("Wave Amplitude:"), 0.0f, 0.5f, 0.01f, 0.06f, (v) => { if (_activeProfile != null) _activeProfile.WaveStrength = v; ApplyLiveMaterialPreview(); });
		_sldWaveStrength = sldWaveStrRes.Slider;

		var tabNormals = new ScrollContainer();
		tabNormals.Name = TranslationServer.Translate("Flow & Optics");
		var vNormals = new VBoxContainer();
		vNormals.AddThemeConstantOverride("separation", 6);
		vNormals.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
		tabNormals.AddChild(vNormals);
		tabContainer.AddChild(tabNormals);

		AddSectionHeader(vNormals, TranslationServer.Translate("Normal Mapping & Flow"));
		_chkUseNormal = AddCheckBox(vNormals, TranslationServer.Translate("Enable Custom Normal Texture"), false, (val) => { if (_activeProfile != null) _activeProfile.UseNormalTexture = val; ApplyLiveMaterialPreview(); });
		var (txtNorm, setNorm) = AddAssetFilterDropdown(
			vNormals,
			TranslationServer.Translate("Normal Map File:"),
			"",
			(all) => ScanRtexAssets(all),
			(val) => { if (_activeProfile != null) { _activeProfile.NormalTexturePath = val; ApplyLiveMaterialPreview(); } },
			TranslationServer.Translate("Select .rtex normal map..."),
			110f
		);
		_txtNormalPath = txtNorm;
		_setNormalPath = setNorm;

		var sldNormScaleRes = AddSlider(vNormals, TranslationServer.Translate("Normal Strength:"), 0.0f, 3.0f, 0.1f, 1.0f, (v) => { if (_activeProfile != null) _activeProfile.NormalScale = v; ApplyLiveMaterialPreview(); });
		_sldNormalScale = sldNormScaleRes.Slider;
		var sldFlowXRes = AddSlider(vNormals, TranslationServer.Translate("Flow Direction X:"), -1.0f, 1.0f, 0.05f, 1.0f, (v) => { if (_activeProfile != null) _activeProfile.FlowDirectionX = v; ApplyLiveMaterialPreview(); });
		_sldFlowDirX = sldFlowXRes.Slider;
		var sldFlowYRes = AddSlider(vNormals, TranslationServer.Translate("Flow Direction Y:"), -1.0f, 1.0f, 0.05f, 0.0f, (v) => { if (_activeProfile != null) _activeProfile.FlowDirectionY = v; ApplyLiveMaterialPreview(); });
		_sldFlowDirY = sldFlowYRes.Slider;
		var sldFlowSpdRes = AddSlider(vNormals, TranslationServer.Translate("Flow Velocity:"), 0.0f, 5.0f, 0.1f, 0.5f, (v) => { if (_activeProfile != null) _activeProfile.FlowSpeed = v; ApplyLiveMaterialPreview(); });
		_sldFlowSpeed = sldFlowSpdRes.Slider;

		_chkUseFlowMap = AddCheckBox(vNormals, TranslationServer.Translate("Enable Directional Flow Map"), false, (val) => { if (_activeProfile != null) _activeProfile.UseFlowMap = val; ApplyLiveMaterialPreview(); });
		var (txtFlow, setFlow) = AddAssetFilterDropdown(
			vNormals,
			TranslationServer.Translate("Flow Map File:"),
			"",
			(all) => ScanRtexAssets(all),
			(val) => { if (_activeProfile != null) { _activeProfile.FlowMapPath = val; ApplyLiveMaterialPreview(); } },
			TranslationServer.Translate("Select .rtex flow map..."),
			110f
		);
		_txtFlowMapPath = txtFlow;
		_setFlowMapPath = setFlow;

		var btnRandomizeFlow = CreateRandomizeButton(() => RandomizeNormalAndFlowParameters());
		vNormals.AddChild(btnRandomizeFlow);

		AddSectionHeader(vNormals, TranslationServer.Translate("Refraction & Caustics"));
		var sldRefrRes = AddSlider(vNormals, TranslationServer.Translate("Refraction Index:"), 0.0f, 1.0f, 0.02f, 0.0f, (v) => { if (_activeProfile != null) _activeProfile.RefractionStrength = v; ApplyLiveMaterialPreview(); });
		_sldRefraction = sldRefrRes.Slider;
		var sldCaustStrRes = AddSlider(vNormals, TranslationServer.Translate("Caustics Intensity:"), 0.0f, 2.0f, 0.05f, 0.0f, (v) => { if (_activeProfile != null) _activeProfile.CausticStrength = v; ApplyLiveMaterialPreview(); });
		_sldCausticStrength = sldCaustStrRes.Slider;
		var sldCaustScaleRes = AddSlider(vNormals, TranslationServer.Translate("Caustics Scale:"), 0.1f, 10.0f, 0.1f, 1.0f, (v) => { if (_activeProfile != null) _activeProfile.CausticScale = v; ApplyLiveMaterialPreview(); });
		_sldCausticScale = sldCaustScaleRes.Slider;
		var sldCaustSpdRes = AddSlider(vNormals, TranslationServer.Translate("Caustics Speed:"), 0.0f, 5.0f, 0.1f, 1.0f, (v) => { if (_activeProfile != null) _activeProfile.CausticSpeed = v; ApplyLiveMaterialPreview(); });
		_sldCausticSpeed = sldCaustSpdRes.Slider;

		var tabEmissive = new ScrollContainer();
		tabEmissive.Name = TranslationServer.Translate("Emission & Surface");
		var vEmissive = new VBoxContainer();
		vEmissive.AddThemeConstantOverride("separation", 6);
		vEmissive.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
		tabEmissive.AddChild(vEmissive);
		tabContainer.AddChild(tabEmissive);

		AddSectionHeader(vEmissive, TranslationServer.Translate("Emissive Heat & Core (Lava / Acid)"));
		var cpEmRes = AddColorPicker(vEmissive, TranslationServer.Translate("Emission Color:"), Colors.Black, (c) => { if (_activeProfile != null) _activeProfile.EmissionColorHex = "#" + c.ToHtml(true); ApplyLiveMaterialPreview(); });
		_cpEmission = cpEmRes.Picker;
		var sldEmBoostRes = AddSlider(vEmissive, TranslationServer.Translate("Emission Boost:"), 0.0f, 2.0f, 0.01f, 0.0f, (v) => { if (_activeProfile != null) _activeProfile.EmissionBoost = v; ApplyLiveMaterialPreview(); });
		_sldEmissionBoost = sldEmBoostRes.Slider;
		var cpCoreRes = AddColorPicker(vEmissive, TranslationServer.Translate("Core Hotspot Color:"), Colors.White, (c) => { if (_activeProfile != null) _activeProfile.CoreColorHex = "#" + c.ToHtml(true); ApplyLiveMaterialPreview(); });
		_cpCore = cpCoreRes.Picker;
		var sldCoreThreshRes = AddSlider(vEmissive, TranslationServer.Translate("Core Threshold:"), 0.0f, 1.0f, 0.05f, 0.8f, (v) => { if (_activeProfile != null) _activeProfile.CoreThreshold = v; ApplyLiveMaterialPreview(); });
		_sldCoreThreshold = sldCoreThreshRes.Slider;
		var cpSssRes = AddColorPicker(vEmissive, TranslationServer.Translate("Subsurface Color:"), Colors.Black, (c) => { if (_activeProfile != null) _activeProfile.SubsurfaceColorHex = "#" + c.ToHtml(true); ApplyLiveMaterialPreview(); });
		_cpSubsurface = cpSssRes.Picker;
		var sldSssRes = AddSlider(vEmissive, TranslationServer.Translate("Subsurface Power:"), 0.0f, 5.0f, 0.1f, 0.0f, (v) => { if (_activeProfile != null) _activeProfile.SubsurfaceStrength = v; ApplyLiveMaterialPreview(); });
		_sldSubsurfaceStrength = sldSssRes.Slider;

		var btnRandomizeEmissive = CreateRandomizeButton(() => RandomizeEmissiveParameters());
		vEmissive.AddChild(btnRandomizeEmissive);

		AddSectionHeader(vEmissive, TranslationServer.Translate("Surface Detail Overlay (Algae / Foam / Crust)"));
		_chkUseDetail = AddCheckBox(vEmissive, TranslationServer.Translate("Enable Surface Detail Overlay"), false, (val) => { if (_activeProfile != null) _activeProfile.UseDetailTexture = val; ApplyLiveMaterialPreview(); });
		var (txtDetail, setDetail) = AddAssetFilterDropdown(
			vEmissive,
			TranslationServer.Translate("Detail Texture File:"),
			"",
			(all) => ScanRtexAssets(all),
			(val) => { if (_activeProfile != null) { _activeProfile.DetailTexturePath = val; ApplyLiveMaterialPreview(); } },
			TranslationServer.Translate("Select .rtex detail texture..."),
			110f
		);
		_txtDetailPath = txtDetail;
		_setDetailPath = setDetail;

		var tileModeRow = new HBoxContainer();
		tileModeRow.AddChild(new Label { Text = TranslationServer.Translate("Tile Mode:"), CustomMinimumSize = new Vector2(110, 0) });
		_optDetailTileMode = new OptionButton();
		_optDetailTileMode.AddItem(TranslationServer.Translate("Grid"), 0);
		_optDetailTileMode.AddItem(TranslationServer.Translate("Stochastic"), 1);
		_optDetailTileMode.Selected = 1;
		_optDetailTileMode.ItemSelected += (idx) =>
		{
			if (_activeProfile != null)
			{
				_activeProfile.DetailTileMode = idx == 0 ? "Grid" : "Stochastic";
				UpdateDetailTileModeVisibility();
				ApplyLiveMaterialPreview();
			}
		};
		tileModeRow.AddChild(_optDetailTileMode);
		vEmissive.AddChild(tileModeRow);

		var sldDetUvXRes = AddSlider(vEmissive, TranslationServer.Translate("Detail UV Scale X:"), 0.1f, 10.0f, 0.1f, 1.0f, (v) => { if (_activeProfile != null) _activeProfile.DetailUvScaleX = v; ApplyLiveMaterialPreview(); });
		_sldDetailUvScaleX = sldDetUvXRes.Slider;
		var sldDetUvYRes = AddSlider(vEmissive, TranslationServer.Translate("Detail UV Scale Y:"), 0.1f, 10.0f, 0.1f, 1.0f, (v) => { if (_activeProfile != null) _activeProfile.DetailUvScaleY = v; ApplyLiveMaterialPreview(); });
		_sldDetailUvScaleY = sldDetUvYRes.Slider;
		var sldDetScrXRes = AddSlider(vEmissive, TranslationServer.Translate("Detail Scroll X:"), -2.0f, 2.0f, 0.05f, 0.0f, (v) => { if (_activeProfile != null) _activeProfile.DetailUvScrollX = v; ApplyLiveMaterialPreview(); });
		_sldDetailUvScrollX = sldDetScrXRes.Slider;
		var sldDetScrYRes = AddSlider(vEmissive, TranslationServer.Translate("Detail Scroll Y:"), -2.0f, 2.0f, 0.05f, 0.0f, (v) => { if (_activeProfile != null) _activeProfile.DetailUvScrollY = v; ApplyLiveMaterialPreview(); });
		_sldDetailUvScrollY = sldDetScrYRes.Slider;

		var sldStochRes = AddSlider(vEmissive, TranslationServer.Translate("Stochastic Size:"), 0.5f, 3.0f, 0.05f, 1.0f, (v) =>
		{
			if (_activeProfile != null)
			{
				_activeProfile.DetailStochasticTileSize = v;
				ApplyLiveMaterialPreview();
			}
		});
		_sldDetailStochasticTileSize = sldStochRes.Slider;

		var sldCrossRes = AddSlider(vEmissive, TranslationServer.Translate("Cross-Fade:"), 0.0f, 10.0f, 0.25f, 0.0f, (v) =>
		{
			if (_activeProfile != null)
			{
				_activeProfile.DetailCrossFade = v;
				ApplyLiveMaterialPreview();
			}
		});
		_sldDetailCrossFade = sldCrossRes.Slider;

		var sldDetAlphaRes = AddSlider(vEmissive, TranslationServer.Translate("Detail Opacity:"), 0.0f, 1.0f, 0.05f, 0.5f, (v) => { if (_activeProfile != null) _activeProfile.DetailAlpha = v; ApplyLiveMaterialPreview(); });
		_sldDetailAlpha = sldDetAlphaRes.Slider;

		var blendRow = new HBoxContainer();
		blendRow.AddChild(new Label { Text = TranslationServer.Translate("Blend Mode:"), CustomMinimumSize = new Vector2(110, 0) });
		_optDetailBlend = new OptionButton();
		_optDetailBlend.AddItem(TranslationServer.Translate("Alpha Mix"), 0);
		_optDetailBlend.AddItem(TranslationServer.Translate("Additive"), 1);
		_optDetailBlend.AddItem(TranslationServer.Translate("Multiply"), 2);
		_optDetailBlend.ItemSelected += (idx) => { if (_activeProfile != null) _activeProfile.DetailBlendMode = (int)idx; ApplyLiveMaterialPreview(); };
		blendRow.AddChild(_optDetailBlend);
		vEmissive.AddChild(blendRow);

		var btnRandomizeDetail = CreateRandomizeButton(() => RandomizeDetailOverlayParameters());
		vEmissive.AddChild(btnRandomizeDetail);

		var tabPathingAndBombing = new ScrollContainer();
		tabPathingAndBombing.Name = TranslationServer.Translate("Pathing & Bombing");
		var vBombing = new VBoxContainer();
		vBombing.AddThemeConstantOverride("separation", 6);
		vBombing.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
		tabPathingAndBombing.AddChild(vBombing);
		tabContainer.AddChild(tabPathingAndBombing);

		AddSectionHeader(vBombing, TranslationServer.Translate("Default Pathing Capabilities"));
		AddDescription(vBombing, TranslationServer.Translate("Automatically assigned to cells painted with this liquid profile:"));
		_chkPathShallow = AddCheckBox(vBombing, TranslationServer.Translate("Shallow Water"), true, (val) => UpdatePathingMask());
		_chkPathDeep = AddCheckBox(vBombing, TranslationServer.Translate("Deep Water"), false, (val) => UpdatePathingMask());
		_chkPathGround = AddCheckBox(vBombing, TranslationServer.Translate("Ground"), false, (val) => UpdatePathingMask());
		_chkPathBuildable = AddCheckBox(vBombing, TranslationServer.Translate("Buildable"), false, (val) => UpdatePathingMask());
		_chkPathFlying = AddCheckBox(vBombing, TranslationServer.Translate("Flying"), true, (val) => UpdatePathingMask());

		AddSectionHeader(vBombing, TranslationServer.Translate("Procedural Decal Texture Bombing"));
		AddDescription(vBombing, TranslationServer.Translate("Randomly scattered decals placed at liquid elevation during painting:"));
		var decalListRow = new HBoxContainer();
		_lstDecals = new ItemList { CustomMinimumSize = new Vector2(240, 75), SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
		_lstDecals.ItemSelected += (idx) =>
		{
			if (_activeProfile != null && idx >= 0 && idx < _activeProfile.DecalBombingRules.Count)
			{
				var r = _activeProfile.DecalBombingRules[(int)idx];
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
		vBombing.AddChild(decalListRow);

		var sldDecalDensRes = AddSlider(vBombing, TranslationServer.Translate("Decal Density:"), 0.0f, 1.0f, 0.01f, 0.5f, (v) =>
		{
			if (_activeProfile != null && _activeProfile.DecalBombingRules.Count > 0)
			{
				var sel = _lstDecals.GetSelectedItems();
				int idx = sel.Length > 0 ? sel[0] : 0;
				if (idx >= 0 && idx < _activeProfile.DecalBombingRules.Count) _activeProfile.DecalBombingRules[idx].Density = v;
			}
		});
		_sldDecalDensity = sldDecalDensRes.Slider;
		var sldDecalMinScRes = AddSlider(vBombing, TranslationServer.Translate("Min Decal Scale:"), 0.05f, 1.0f, 0.01f, 0.2f, (v) =>
		{
			if (_activeProfile != null && _activeProfile.DecalBombingRules.Count > 0)
			{
				var sel = _lstDecals.GetSelectedItems();
				int idx = sel.Length > 0 ? sel[0] : 0;
				if (idx >= 0 && idx < _activeProfile.DecalBombingRules.Count) _activeProfile.DecalBombingRules[idx].MinScale = v;
			}
		});
		_sldDecalMinScale = sldDecalMinScRes.Slider;
		var sldDecalMaxScRes = AddSlider(vBombing, TranslationServer.Translate("Max Decal Scale:"), 0.05f, 1.0f, 0.01f, 0.5f, (v) =>
		{
			if (_activeProfile != null && _activeProfile.DecalBombingRules.Count > 0)
			{
				var sel = _lstDecals.GetSelectedItems();
				int idx = sel.Length > 0 ? sel[0] : 0;
				if (idx >= 0 && idx < _activeProfile.DecalBombingRules.Count) _activeProfile.DecalBombingRules[idx].MaxScale = v;
			}
		});
		_sldDecalMaxScale = sldDecalMaxScRes.Slider;

		AddSectionHeader(vBombing, TranslationServer.Translate("Procedural VFX / Particle Bombing"));
		AddDescription(vBombing, TranslationServer.Translate("Randomly scattered particle systems & ribbons (bubbles, steam, foam, smoke) placed at liquid elevation:"));
		var vfxListRow = new HBoxContainer();
		_lstVfx = new ItemList { CustomMinimumSize = new Vector2(240, 75), SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
		_lstVfx.ItemSelected += (idx) =>
		{
			if (_activeProfile != null && idx >= 0 && idx < _activeProfile.VfxBombingRules.Count)
			{
				var r = _activeProfile.VfxBombingRules[(int)idx];
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
		vBombing.AddChild(vfxListRow);

		var sldVfxDensRes = AddSlider(vBombing, TranslationServer.Translate("VFX Density:"), 0.0f, 1.0f, 0.01f, 0.5f, (v) =>
		{
			if (_activeProfile != null && _activeProfile.VfxBombingRules.Count > 0)
			{
				var sel = _lstVfx.GetSelectedItems();
				int idx = sel.Length > 0 ? sel[0] : 0;
				if (idx >= 0 && idx < _activeProfile.VfxBombingRules.Count) _activeProfile.VfxBombingRules[idx].Density = v;
			}
		});
		_sldVfxDensity = sldVfxDensRes.Slider;
		var sldVfxMinScRes = AddSlider(vBombing, TranslationServer.Translate("Min VFX Scale:"), 0.05f, 1.0f, 0.01f, 0.2f, (v) =>
		{
			if (_activeProfile != null && _activeProfile.VfxBombingRules.Count > 0)
			{
				var sel = _lstVfx.GetSelectedItems();
				int idx = sel.Length > 0 ? sel[0] : 0;
				if (idx >= 0 && idx < _activeProfile.VfxBombingRules.Count) _activeProfile.VfxBombingRules[idx].MinScale = v;
			}
		});
		_sldVfxMinScale = sldVfxMinScRes.Slider;
		var sldVfxMaxScRes = AddSlider(vBombing, TranslationServer.Translate("Max VFX Scale:"), 0.05f, 1.0f, 0.01f, 0.5f, (v) =>
		{
			if (_activeProfile != null && _activeProfile.VfxBombingRules.Count > 0)
			{
				var sel = _lstVfx.GetSelectedItems();
				int idx = sel.Length > 0 ? sel[0] : 0;
				if (idx >= 0 && idx < _activeProfile.VfxBombingRules.Count) _activeProfile.VfxBombingRules[idx].MaxScale = v;
			}
		});
		_sldVfxMaxScale = sldVfxMaxScRes.Slider;
	}

	private Button CreateRandomizeButton(Action onPressed)
	{
		var btn = new Button();
		btn.Set("icon_max_width", 0);
		btn.Text = "🎲 " + TranslationServer.Translate("Randomize All");
		btn.CustomMinimumSize = new Vector2(0, 28);
		btn.SizeFlagsHorizontal = Control.SizeFlags.ShrinkBegin;
		btn.AddThemeFontSizeOverride("font_size", 11);
		var mapEdBtnTex = GD.Load<Texture2D>("res://Assets/UI/map_editor_button.png");
		if (mapEdBtnTex != null)
		{
			var normalSb = new StyleBoxTexture { Texture = mapEdBtnTex, ContentMarginLeft = 12, ContentMarginRight = 12, ContentMarginTop = 4, ContentMarginBottom = 4 };
			var hoverSb = new StyleBoxTexture { Texture = mapEdBtnTex, ModulateColor = new Color(1.25f, 1.2f, 1.0f, 1.0f), ContentMarginLeft = 12, ContentMarginRight = 12, ContentMarginTop = 4, ContentMarginBottom = 4 };
			var pressedSb = new StyleBoxTexture { Texture = mapEdBtnTex, ModulateColor = new Color(0.85f, 0.8f, 0.7f, 1.0f), ContentMarginLeft = 12, ContentMarginRight = 12, ContentMarginTop = 4, ContentMarginBottom = 4 };
			btn.AddThemeStyleboxOverride("normal", normalSb);
			btn.AddThemeStyleboxOverride("hover", hoverSb);
			btn.AddThemeStyleboxOverride("pressed", pressedSb);
			btn.AddThemeStyleboxOverride("focus", new StyleBoxEmpty());
		}
		else
		{
			btn.AddThemeStyleboxOverride("normal", UIStyle.CreateButtonNormal());
			btn.AddThemeStyleboxOverride("hover", UIStyle.CreateButtonHover());
			btn.AddThemeStyleboxOverride("pressed", UIStyle.CreateButtonPressed());
		}
		btn.Pressed += onPressed;
		return btn;
	}

	public void OpenWithProfiles(List<WaterProfileSaveData> profiles, int selectedIdx = 0)
	{
		_profiles.Clear();
		_initialSnapshots.Clear();
		foreach (var p in profiles)
		{
			_profiles.Add(p.Clone());
			_initialSnapshots.Add(p.Clone());
		}
		if (_profiles.Count == 0)
		{
			_profiles = WaterProfileSaveData.CreateDefaultProfiles();
			foreach (var p in _profiles) _initialSnapshots.Add(p.Clone());
		}

		_selectedProfileIdx = Math.Clamp(selectedIdx, 0, _profiles.Count - 1);
		UpdateProfileListUI();
		SelectProfile(_selectedProfileIdx);
		OpenDialog();
	}

	private void UpdateProfileListUI()
	{
		if (_optProfileSelect == null) return;
		_optProfileSelect.Clear();
		for (int i = 0; i < _profiles.Count; i++)
		{
			_optProfileSelect.AddItem($"[{_profiles[i].ProfileIndex}] {_profiles[i].Name} ({_profiles[i].Id})", i);
		}
		if (_selectedProfileIdx >= 0 && _selectedProfileIdx < _profiles.Count)
		{
			_optProfileSelect.Selected = _selectedProfileIdx;
		}
	}

	private void SelectProfile(int idx)
	{
		if (idx < 0 || idx >= _profiles.Count) return;
		_selectedProfileIdx = idx;
		_activeProfile = _profiles[idx];
		if (_optProfileSelect != null && _optProfileSelect.Selected != idx)
		{
			_optProfileSelect.Selected = idx;
		}
		PopulateFormFromActiveProfile();
		ApplyLiveMaterialPreview();
	}

	private void PopulateFormFromActiveProfile()
	{
		if (_activeProfile == null) return;
		_txtName.Text = _activeProfile.Name;

		_cpShallow.Color = Color.HtmlIsValid(_activeProfile.ShallowColorHex) ? Color.FromHtml(_activeProfile.ShallowColorHex) : Colors.Teal;
		_cpDeep.Color = Color.HtmlIsValid(_activeProfile.DeepColorHex) ? Color.FromHtml(_activeProfile.DeepColorHex) : Colors.NavyBlue;
		_cpFoam.Color = Color.HtmlIsValid(_activeProfile.FoamColorHex) ? Color.FromHtml(_activeProfile.FoamColorHex) : Colors.White;

		_sldMaxDepth.Value = _activeProfile.MaxDepth;
		_sldFoamDepth.Value = _activeProfile.FoamDepth;
		_sldWaveSpeed.Value = _activeProfile.WaveSpeed;
		_sldWaveStrength.Value = _activeProfile.WaveStrength;

		_chkUseNormal.ButtonPressed = _activeProfile.UseNormalTexture;
		_setNormalPath?.Invoke(_activeProfile.NormalTexturePath ?? "");
		_sldNormalScale.Value = _activeProfile.NormalScale;
		_sldFlowDirX.Value = _activeProfile.FlowDirectionX;
		_sldFlowDirY.Value = _activeProfile.FlowDirectionY;
		_sldFlowSpeed.Value = _activeProfile.FlowSpeed;
		_chkUseFlowMap.ButtonPressed = _activeProfile.UseFlowMap;
		_setFlowMapPath?.Invoke(_activeProfile.FlowMapPath ?? "");

		_sldRefraction.Value = _activeProfile.RefractionStrength;
		_sldCausticStrength.Value = _activeProfile.CausticStrength;
		_sldCausticScale.Value = _activeProfile.CausticScale;
		_sldCausticSpeed.Value = _activeProfile.CausticSpeed;

		_cpEmission.Color = Color.HtmlIsValid(_activeProfile.EmissionColorHex) ? Color.FromHtml(_activeProfile.EmissionColorHex) : Colors.Black;
		_sldEmissionBoost.Value = _activeProfile.EmissionBoost;
		_cpCore.Color = Color.HtmlIsValid(_activeProfile.CoreColorHex) ? Color.FromHtml(_activeProfile.CoreColorHex) : Colors.White;
		_sldCoreThreshold.Value = _activeProfile.CoreThreshold;
		_cpSubsurface.Color = Color.HtmlIsValid(_activeProfile.SubsurfaceColorHex) ? Color.FromHtml(_activeProfile.SubsurfaceColorHex) : Colors.Black;
		_sldSubsurfaceStrength.Value = _activeProfile.SubsurfaceStrength;

		_chkUseDetail.ButtonPressed = _activeProfile.UseDetailTexture;
		_setDetailPath?.Invoke(_activeProfile.DetailTexturePath ?? "");
		if (_optDetailTileMode != null)
		{
			_optDetailTileMode.Selected = string.Equals(_activeProfile.DetailTileMode, "Grid", StringComparison.OrdinalIgnoreCase) ? 0 : 1;
		}
		_sldDetailUvScaleX.Value = _activeProfile.DetailUvScaleX;
		_sldDetailUvScaleY.Value = _activeProfile.DetailUvScaleY;
		_sldDetailUvScrollX.Value = _activeProfile.DetailUvScrollX;
		_sldDetailUvScrollY.Value = _activeProfile.DetailUvScrollY;
		if (_sldDetailStochasticTileSize != null) _sldDetailStochasticTileSize.Value = _activeProfile.DetailStochasticTileSize > 0.001f ? _activeProfile.DetailStochasticTileSize : 1.0f;
		if (_sldDetailCrossFade != null) _sldDetailCrossFade.Value = _activeProfile.DetailCrossFade;
		UpdateDetailTileModeVisibility();
		_sldDetailAlpha.Value = _activeProfile.DetailAlpha;
		_optDetailBlend.Selected = Math.Clamp(_activeProfile.DetailBlendMode, 0, 2);

		int code = _activeProfile.DefaultPathingCode;
		_chkPathShallow.ButtonPressed = (code & EditableTerrain.PATHING_SHALLOW_WATER) != 0;
		_chkPathDeep.ButtonPressed = (code & EditableTerrain.PATHING_DEEP_WATER) != 0;
		_chkPathGround.ButtonPressed = (code & EditableTerrain.PATHING_GROUND) != 0;
		_chkPathBuildable.ButtonPressed = (code & EditableTerrain.PATHING_BUILDABLE) != 0;
		_chkPathFlying.ButtonPressed = (code & EditableTerrain.PATHING_FLYING) != 0;

		_setDecalIdValue?.Invoke(string.Empty);
		_setVfxIdValue?.Invoke(string.Empty);
		UpdateDecalListUI();
		UpdateVfxListUI();
	}

	private void UpdateDetailTileModeVisibility()
	{
		bool isStochastic = _activeProfile == null || !string.Equals(_activeProfile.DetailTileMode, "Grid", StringComparison.OrdinalIgnoreCase);
		if (_sldDetailStochasticTileSize?.GetParent() is Control stochRow)
		{
			stochRow.Visible = isStochastic;
		}
		if (_sldDetailCrossFade?.GetParent() is Control crossFadeRow)
		{
			crossFadeRow.Visible = !isStochastic;
		}
	}

	private void RandomizeAllWaterParameters()
	{
		if (_activeProfile == null) return;
		var rng = new Random();

		float baseHue = (float)rng.NextDouble();
		float shallowSat = 0.4f + (float)rng.NextDouble() * 0.5f;
		float shallowVal = 0.4f + (float)rng.NextDouble() * 0.5f;
		float shallowAlpha = 0.45f + (float)rng.NextDouble() * 0.35f;
		Color shallowColor = Color.FromHsv(baseHue, shallowSat, shallowVal, shallowAlpha);

		float deepSat = Math.Clamp(shallowSat + 0.15f, 0.0f, 1.0f);
		float deepVal = 0.05f + (float)rng.NextDouble() * 0.25f;
		float deepAlpha = 0.85f + (float)rng.NextDouble() * 0.15f;
		Color deepColor = Color.FromHsv((baseHue + 0.02f) % 1.0f, deepSat, deepVal, deepAlpha);

		float foamSat = 0.05f + (float)rng.NextDouble() * 0.2f;
		float foamVal = 0.85f + (float)rng.NextDouble() * 0.15f;
		float foamAlpha = 0.75f + (float)rng.NextDouble() * 0.2f;
		Color foamColor = Color.FromHsv(baseHue, foamSat, foamVal, foamAlpha);

		_activeProfile.ShallowColorHex = "#" + shallowColor.ToHtml(true);
		_activeProfile.DeepColorHex = "#" + deepColor.ToHtml(true);
		_activeProfile.FoamColorHex = "#" + foamColor.ToHtml(true);

		_activeProfile.MaxDepth = (float)Math.Round(0.5f + rng.NextDouble() * 3.0f, 2);
		_activeProfile.FoamDepth = (float)Math.Round(0.2f + rng.NextDouble() * 1.0f, 2);
		_activeProfile.WaveSpeed = (float)Math.Round(0.4f + rng.NextDouble() * 2.0f, 2);
		_activeProfile.WaveStrength = (float)Math.Round(0.02f + rng.NextDouble() * 0.10f, 3);

		_activeProfile.NormalScale = (float)Math.Round(0.3f + rng.NextDouble() * 1.7f, 2);
		double dirAngle = rng.NextDouble() * Math.PI * 2.0;
		_activeProfile.FlowDirectionX = (float)Math.Round(Math.Cos(dirAngle), 2);
		_activeProfile.FlowDirectionY = (float)Math.Round(Math.Sin(dirAngle), 2);
		_activeProfile.FlowSpeed = (float)Math.Round(0.2f + rng.NextDouble() * 1.5f, 2);

		bool hasRefraction = rng.NextDouble() > 0.35;
		_activeProfile.RefractionStrength = hasRefraction ? (float)Math.Round(0.1f + rng.NextDouble() * 0.5f, 2) : 0.0f;

		bool hasCaustics = rng.NextDouble() > 0.35;
		_activeProfile.CausticStrength = hasCaustics ? (float)Math.Round(0.2f + rng.NextDouble() * 1.0f, 2) : 0.0f;
		_activeProfile.CausticScale = (float)Math.Round(0.5f + rng.NextDouble() * 2.0f, 2);
		_activeProfile.CausticSpeed = (float)Math.Round(0.5f + rng.NextDouble() * 2.0f, 2);

		bool isGlowing = rng.NextDouble() > 0.65;
		if (isGlowing)
		{
			float emHue = (baseHue + (float)(rng.NextDouble() * 0.2 - 0.1) + 1.0f) % 1.0f;
			Color emColor = Color.FromHsv(emHue, 0.85f + (float)rng.NextDouble() * 0.15f, 0.9f + (float)rng.NextDouble() * 0.1f);
			_activeProfile.EmissionColorHex = "#" + emColor.ToHtml(true);
			_activeProfile.EmissionBoost = (float)Math.Round(0.3f + rng.NextDouble() * 1.7f, 2);

			Color coreColor = Color.FromHsv((emHue + 0.08f) % 1.0f, 0.2f + (float)rng.NextDouble() * 0.3f, 1.0f);
			_activeProfile.CoreColorHex = "#" + coreColor.ToHtml(true);
			_activeProfile.CoreThreshold = (float)Math.Round(0.55f + rng.NextDouble() * 0.35f, 2);

			Color sssColor = Color.FromHsv(emHue, 0.7f + (float)rng.NextDouble() * 0.3f, 0.8f + (float)rng.NextDouble() * 0.2f);
			_activeProfile.SubsurfaceColorHex = "#" + sssColor.ToHtml(true);
			_activeProfile.SubsurfaceStrength = (float)Math.Round(0.5f + rng.NextDouble() * 2.0f, 2);
		}
		else
		{
			_activeProfile.EmissionColorHex = "#000000FF";
			_activeProfile.EmissionBoost = 0.0f;
			_activeProfile.CoreColorHex = "#FFFFFFFF";
			_activeProfile.CoreThreshold = 0.8f;
			_activeProfile.SubsurfaceColorHex = "#000000FF";
			_activeProfile.SubsurfaceStrength = 0.0f;
		}

		_activeProfile.DetailUvScaleX = (float)Math.Round(0.5f + rng.NextDouble() * 2.5f, 2);
		_activeProfile.DetailUvScaleY = (float)Math.Round(0.5f + rng.NextDouble() * 2.5f, 2);
		_activeProfile.DetailStochasticTileSize = (float)Math.Round(0.8f + rng.NextDouble() * 1.5f, 2);
		_activeProfile.DetailCrossFade = (float)Math.Round(rng.NextDouble() * 4.0f, 1);

		SelectProfile(_selectedProfileIdx);
		ApplyLiveMaterialPreview();
	}

	private void RandomizeEmissiveParameters()
	{
		if (_activeProfile == null) return;
		var rng = new Random();

		float emHue = (float)rng.NextDouble();
		Color emColor = Color.FromHsv(emHue, 0.80f + (float)rng.NextDouble() * 0.20f, 0.85f + (float)rng.NextDouble() * 0.15f);
		_activeProfile.EmissionColorHex = "#" + emColor.ToHtml(true);
		_activeProfile.EmissionBoost = (float)Math.Round(0.2f + rng.NextDouble() * 1.8f, 2);

		Color coreColor = Color.FromHsv((emHue + 0.06f + (float)rng.NextDouble() * 0.06f) % 1.0f, 0.15f + (float)rng.NextDouble() * 0.35f, 1.0f);
		_activeProfile.CoreColorHex = "#" + coreColor.ToHtml(true);
		_activeProfile.CoreThreshold = (float)Math.Round(0.45f + rng.NextDouble() * 0.45f, 2);

		Color sssColor = Color.FromHsv((emHue + (float)(rng.NextDouble() * 0.1 - 0.05) + 1.0f) % 1.0f, 0.70f + (float)rng.NextDouble() * 0.30f, 0.75f + (float)rng.NextDouble() * 0.25f);
		_activeProfile.SubsurfaceColorHex = "#" + sssColor.ToHtml(true);
		_activeProfile.SubsurfaceStrength = (float)Math.Round(0.5f + rng.NextDouble() * 3.5f, 2);

		if (_cpEmission != null) _cpEmission.Color = emColor;
		if (_sldEmissionBoost != null) _sldEmissionBoost.Value = _activeProfile.EmissionBoost;
		if (_cpCore != null) _cpCore.Color = coreColor;
		if (_sldCoreThreshold != null) _sldCoreThreshold.Value = _activeProfile.CoreThreshold;
		if (_cpSubsurface != null) _cpSubsurface.Color = sssColor;
		if (_sldSubsurfaceStrength != null) _sldSubsurfaceStrength.Value = _activeProfile.SubsurfaceStrength;

		ApplyLiveMaterialPreview();
	}

	private void RandomizeNormalAndFlowParameters()
	{
		if (_activeProfile == null) return;
		var rng = new Random();

		_activeProfile.NormalScale = (float)Math.Round(0.3f + rng.NextDouble() * 2.2f, 2);
		double dirAngle = rng.NextDouble() * Math.PI * 2.0;
		_activeProfile.FlowDirectionX = (float)Math.Round(Math.Cos(dirAngle), 2);
		_activeProfile.FlowDirectionY = (float)Math.Round(Math.Sin(dirAngle), 2);
		_activeProfile.FlowSpeed = (float)Math.Round(0.2f + rng.NextDouble() * 2.0f, 2);

		if (_sldNormalScale != null) _sldNormalScale.Value = _activeProfile.NormalScale;
		if (_sldFlowDirX != null) _sldFlowDirX.Value = _activeProfile.FlowDirectionX;
		if (_sldFlowDirY != null) _sldFlowDirY.Value = _activeProfile.FlowDirectionY;
		if (_sldFlowSpeed != null) _sldFlowSpeed.Value = _activeProfile.FlowSpeed;

		ApplyLiveMaterialPreview();
	}

	private void RandomizeDetailOverlayParameters()
	{
		if (_activeProfile == null) return;
		var rng = new Random();

		_activeProfile.DetailTileMode = rng.NextDouble() > 0.35 ? "Stochastic" : "Grid";
		_activeProfile.DetailUvScaleX = (float)Math.Round(0.5f + rng.NextDouble() * 3.5f, 2);
		_activeProfile.DetailUvScaleY = (float)Math.Round(0.5f + rng.NextDouble() * 3.5f, 2);
		_activeProfile.DetailUvScrollX = rng.NextDouble() > 0.4 ? (float)Math.Round((rng.NextDouble() * 2.0 - 1.0) * 0.75f, 2) : 0.0f;
		_activeProfile.DetailUvScrollY = rng.NextDouble() > 0.4 ? (float)Math.Round((rng.NextDouble() * 2.0 - 1.0) * 0.75f, 2) : 0.0f;
		_activeProfile.DetailStochasticTileSize = (float)Math.Round(0.6f + rng.NextDouble() * 1.8f, 2);
		_activeProfile.DetailCrossFade = (float)Math.Round(rng.NextDouble() * 4.0f, 1);
		_activeProfile.DetailAlpha = (float)Math.Round(0.25f + rng.NextDouble() * 0.65f, 2);
		_activeProfile.DetailBlendMode = rng.Next(0, 3);

		if (_optDetailTileMode != null)
		{
			_optDetailTileMode.Selected = string.Equals(_activeProfile.DetailTileMode, "Grid", StringComparison.OrdinalIgnoreCase) ? 0 : 1;
		}
		UpdateDetailTileModeVisibility();
		if (_sldDetailUvScaleX != null) _sldDetailUvScaleX.Value = _activeProfile.DetailUvScaleX;
		if (_sldDetailUvScaleY != null) _sldDetailUvScaleY.Value = _activeProfile.DetailUvScaleY;
		if (_sldDetailUvScrollX != null) _sldDetailUvScrollX.Value = _activeProfile.DetailUvScrollX;
		if (_sldDetailUvScrollY != null) _sldDetailUvScrollY.Value = _activeProfile.DetailUvScrollY;
		if (_sldDetailStochasticTileSize != null) _sldDetailStochasticTileSize.Value = _activeProfile.DetailStochasticTileSize;
		if (_sldDetailCrossFade != null) _sldDetailCrossFade.Value = _activeProfile.DetailCrossFade;
		if (_sldDetailAlpha != null) _sldDetailAlpha.Value = _activeProfile.DetailAlpha;
		if (_optDetailBlend != null) _optDetailBlend.Selected = _activeProfile.DetailBlendMode;

		ApplyLiveMaterialPreview();
	}

	private void UpdatePathingMask()
	{
		if (_activeProfile == null) return;
		int mask = 0;
		if (_chkPathShallow.ButtonPressed) mask |= EditableTerrain.PATHING_SHALLOW_WATER;
		if (_chkPathDeep.ButtonPressed) mask |= EditableTerrain.PATHING_DEEP_WATER;
		if (_chkPathGround.ButtonPressed) mask |= EditableTerrain.PATHING_GROUND;
		if (_chkPathBuildable.ButtonPressed) mask |= EditableTerrain.PATHING_BUILDABLE;
		if (_chkPathFlying.ButtonPressed) mask |= EditableTerrain.PATHING_FLYING;
		_activeProfile.DefaultPathingCode = mask;
	}

	private void UpdateDecalListUI()
	{
		_lstDecals.Clear();
		if (_activeProfile?.DecalBombingRules == null) return;
		foreach (var r in _activeProfile.DecalBombingRules)
		{
			_lstDecals.AddItem($"{r.DecalId} ({TranslationServer.Translate("Density")}: {r.Density:0.00})");
		}
	}

	private void UpdateVfxListUI()
	{
		_lstVfx.Clear();
		if (_activeProfile?.VfxBombingRules == null) return;
		foreach (var r in _activeProfile.VfxBombingRules)
		{
			_lstVfx.AddItem($"{r.VfxId} ({TranslationServer.Translate("Density")}: {r.Density:0.00})");
		}
	}

	private void OnAddDecalRule()
	{
		string id = _txtDecalId?.Text?.Trim() ?? string.Empty;
		if (_activeProfile == null || string.IsNullOrEmpty(id)) return;
		_activeProfile.DecalBombingRules.Add(new ProceduralBombingDecalRule
		{
			DecalId = id,
			Density = (float)(_sldDecalDensity?.Value ?? 0.5f),
			MinScale = (float)(_sldDecalMinScale?.Value ?? 0.2f),
			MaxScale = (float)(_sldDecalMaxScale?.Value ?? 0.5f)
		});
		UpdateDecalListUI();
	}

	private void OnRemoveDecalRule()
	{
		if (_activeProfile == null) return;
		var selected = _lstDecals.GetSelectedItems();
		if (selected.Length > 0 && selected[0] >= 0 && selected[0] < _activeProfile.DecalBombingRules.Count)
		{
			_activeProfile.DecalBombingRules.RemoveAt(selected[0]);
			UpdateDecalListUI();
		}
	}

	private void OnAddVfxRule()
	{
		string id = _txtVfxId?.Text?.Trim() ?? string.Empty;
		if (_activeProfile == null || string.IsNullOrEmpty(id)) return;
		_activeProfile.VfxBombingRules.Add(new ProceduralBombingVfxRule
		{
			VfxId = id,
			Density = (float)(_sldVfxDensity?.Value ?? 0.5f),
			MinScale = (float)(_sldVfxMinScale?.Value ?? 0.2f),
			MaxScale = (float)(_sldVfxMaxScale?.Value ?? 0.5f)
		});
		UpdateVfxListUI();
	}

	private void OnRemoveVfxRule()
	{
		if (_activeProfile == null) return;
		var selected = _lstVfx.GetSelectedItems();
		if (selected.Length > 0 && selected[0] >= 0 && selected[0] < _activeProfile.VfxBombingRules.Count)
		{
			_activeProfile.VfxBombingRules.RemoveAt(selected[0]);
			UpdateVfxListUI();
		}
	}

	private void OnAddProfileClicked()
	{
		byte nextIndex = 0;
		foreach (var p in _profiles)
		{
			if (p.ProfileIndex >= nextIndex) nextIndex = (byte)(p.ProfileIndex + 1);
		}
		var newProf = new WaterProfileSaveData
		{
			Id = $"liquid_custom_{nextIndex}",
			Name = $"Custom Liquid {nextIndex}",
			ProfileIndex = nextIndex
		};
		_profiles.Add(newProf);
		UpdateProfileListUI();
		SelectProfile(_profiles.Count - 1);
	}

	private void OnDeleteProfileClicked()
	{
		if (_profiles.Count <= 1) return;
		_profiles.RemoveAt(_selectedProfileIdx);
		_selectedProfileIdx = Math.Clamp(_selectedProfileIdx, 0, _profiles.Count - 1);
		UpdateProfileListUI();
		SelectProfile(_selectedProfileIdx);
	}

	private void ApplyLiveMaterialPreview()
	{
		if (_activeProfile == null || RuntimeTerrain.Instance == null) return;
		var mat = RuntimeTerrain.Instance.GetWaterMaterial(_activeProfile.ProfileIndex);
		if (mat != null)
		{
			RuntimeTerrain.Instance.ApplyWaterProfileToMaterial(mat, _activeProfile);
		}
	}

	protected override void OnApply()
	{
		if (RuntimeTerrain.Instance != null)
		{
			RuntimeTerrain.Instance.SetWaterProfiles(_profiles);
		}

		string wsPath = MapWorkspaceService.GetActiveWorkspacePath();
		string metaPath = Path.Combine(wsPath, "metadata.json");
		if (File.Exists(metaPath))
		{
			if (MetadataService.Instance.TryLoadMetadata(wsPath, out var metadataRoot) && metadataRoot != null)
			{
				metadataRoot.CustomWaterProfiles = _profiles;
				MetadataService.Instance.SaveMetadata(metaPath, metadataRoot);
			}
		}

		Hud?.RefreshWaterSwatches();
	}

	protected override void OnCancel()
	{
		if (RuntimeTerrain.Instance != null)
		{
			RuntimeTerrain.Instance.SetWaterProfiles(_initialSnapshots);
		}
		Hud?.RefreshWaterSwatches();
	}

	private List<string> ScanRtexAssets(bool includeAllFolders = true)
	{
		var results = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
		string wsPath = ProjectSettings.GlobalizePath(MapEditorHUD.TempWorkspaceGodotPath);

		if (!string.IsNullOrEmpty(wsPath) && Directory.Exists(wsPath))
		{
			string assetsDir = Path.Combine(wsPath, "Assets");
			if (Directory.Exists(assetsDir))
			{
				try
				{
					foreach (var file in Directory.GetFiles(assetsDir, "*.rtex", SearchOption.AllDirectories))
					{
						string rel = Path.GetRelativePath(wsPath, file).Replace('\\', '/');
						results.Add(rel);
						results.Add(Path.GetFileName(file));
					}
				}
				catch { }
			}
		}

		try
		{
			var assetsObj = Realm.Godot.Utils.MapAssetHelper.LoadUnionedAssets(wsPath);
			if (assetsObj != null)
			{
				foreach (var catName in new[] { "textures", "noise_textures", "noise", "decals", "ribbons", "vfx", "vfx_spritesheets" })
				{
					if (assetsObj[catName] is System.Text.Json.Nodes.JsonObject catObj)
					{
						foreach (var kvp in catObj)
						{
							if (!string.IsNullOrEmpty(kvp.Key) && kvp.Key.EndsWith(".rtex", StringComparison.OrdinalIgnoreCase))
							{
								results.Add(kvp.Key);
								results.Add(Path.GetFileName(kvp.Key));
							}
						}
					}
				}
			}
		}
		catch { }

		var list = new List<string>(results);
		list.Sort(StringComparer.OrdinalIgnoreCase);
		return list;
	}
}
