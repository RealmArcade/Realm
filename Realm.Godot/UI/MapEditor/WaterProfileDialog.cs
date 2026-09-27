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
	private OptionButton _optWaterType;

	private ColorPickerButton _cpShallow;
	private ColorPickerButton _cpDeep;
	private ColorPickerButton _cpFoam;
	private HSlider _sldMaxDepth;
	private HSlider _sldFoamDepth;
	private HSlider _sldWaveSpeed;
	private HSlider _sldWaveStrength;

	private CheckBox _chkUseNormal;
	private LineEdit _txtNormalPath;
	private HSlider _sldNormalScale;
	private HSlider _sldFlowDirX;
	private HSlider _sldFlowDirY;
	private HSlider _sldFlowSpeed;
	private CheckBox _chkUseFlowMap;
	private LineEdit _txtFlowMapPath;

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
	private HSlider _sldDetailUvScaleX;
	private HSlider _sldDetailUvScaleY;
	private HSlider _sldDetailUvScrollX;
	private HSlider _sldDetailUvScrollY;
	private HSlider _sldDetailAlpha;
	private OptionButton _optDetailBlend;

	private CheckBox _chkPathShallow;
	private CheckBox _chkPathDeep;
	private CheckBox _chkPathGround;
	private CheckBox _chkPathBuildable;
	private CheckBox _chkPathFlying;

	private ItemList _lstDecals;
	private LineEdit _txtDecalId;
	private HSlider _sldDecalDensity;
	private HSlider _sldDecalMinScale;
	private HSlider _sldDecalMaxScale;

	private ItemList _lstVfx;
	private LineEdit _txtVfxId;
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

		var typeRow = new HBoxContainer();
		typeRow.AddChild(new Label { Text = TranslationServer.Translate("Water Depth Type:"), CustomMinimumSize = new Vector2(110, 0) });
		_optWaterType = new OptionButton();
		_optWaterType.AddItem(TranslationServer.Translate("Shallow"), 1);
		_optWaterType.AddItem(TranslationServer.Translate("Deep"), 2);
		_optWaterType.ItemSelected += (idx) => { if (_activeProfile != null) _activeProfile.WaterType = (WaterType)_optWaterType.GetSelectedId(); ApplyLiveMaterialPreview(); };
		typeRow.AddChild(_optWaterType);
		vGeneral.AddChild(typeRow);

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
		var normPathRow = new HBoxContainer();
		normPathRow.AddChild(new Label { Text = TranslationServer.Translate("Normal Map File:"), CustomMinimumSize = new Vector2(110, 0) });
		_txtNormalPath = new LineEdit { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
		_txtNormalPath.TextChanged += (t) => { if (_activeProfile != null) _activeProfile.NormalTexturePath = t; ApplyLiveMaterialPreview(); };
		normPathRow.AddChild(_txtNormalPath);
		vNormals.AddChild(normPathRow);

		var sldNormScaleRes = AddSlider(vNormals, TranslationServer.Translate("Normal Strength:"), 0.0f, 3.0f, 0.1f, 1.0f, (v) => { if (_activeProfile != null) _activeProfile.NormalScale = v; ApplyLiveMaterialPreview(); });
		_sldNormalScale = sldNormScaleRes.Slider;
		var sldFlowXRes = AddSlider(vNormals, TranslationServer.Translate("Flow Direction X:"), -1.0f, 1.0f, 0.05f, 1.0f, (v) => { if (_activeProfile != null) _activeProfile.FlowDirectionX = v; ApplyLiveMaterialPreview(); });
		_sldFlowDirX = sldFlowXRes.Slider;
		var sldFlowYRes = AddSlider(vNormals, TranslationServer.Translate("Flow Direction Y:"), -1.0f, 1.0f, 0.05f, 0.0f, (v) => { if (_activeProfile != null) _activeProfile.FlowDirectionY = v; ApplyLiveMaterialPreview(); });
		_sldFlowDirY = sldFlowYRes.Slider;
		var sldFlowSpdRes = AddSlider(vNormals, TranslationServer.Translate("Flow Velocity:"), 0.0f, 5.0f, 0.1f, 0.5f, (v) => { if (_activeProfile != null) _activeProfile.FlowSpeed = v; ApplyLiveMaterialPreview(); });
		_sldFlowSpeed = sldFlowSpdRes.Slider;

		_chkUseFlowMap = AddCheckBox(vNormals, TranslationServer.Translate("Enable Directional Flow Map"), false, (val) => { if (_activeProfile != null) _activeProfile.UseFlowMap = val; ApplyLiveMaterialPreview(); });
		var flowMapRow = new HBoxContainer();
		flowMapRow.AddChild(new Label { Text = TranslationServer.Translate("Flow Map File:"), CustomMinimumSize = new Vector2(110, 0) });
		_txtFlowMapPath = new LineEdit { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
		_txtFlowMapPath.TextChanged += (t) => { if (_activeProfile != null) _activeProfile.FlowMapPath = t; ApplyLiveMaterialPreview(); };
		flowMapRow.AddChild(_txtFlowMapPath);
		vNormals.AddChild(flowMapRow);

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
		var sldEmBoostRes = AddSlider(vEmissive, TranslationServer.Translate("Emission Boost:"), 0.0f, 20.0f, 0.2f, 0.0f, (v) => { if (_activeProfile != null) _activeProfile.EmissionBoost = v; ApplyLiveMaterialPreview(); });
		_sldEmissionBoost = sldEmBoostRes.Slider;
		var cpCoreRes = AddColorPicker(vEmissive, TranslationServer.Translate("Core Hotspot Color:"), Colors.White, (c) => { if (_activeProfile != null) _activeProfile.CoreColorHex = "#" + c.ToHtml(true); ApplyLiveMaterialPreview(); });
		_cpCore = cpCoreRes.Picker;
		var sldCoreThreshRes = AddSlider(vEmissive, TranslationServer.Translate("Core Threshold:"), 0.0f, 1.0f, 0.05f, 0.8f, (v) => { if (_activeProfile != null) _activeProfile.CoreThreshold = v; ApplyLiveMaterialPreview(); });
		_sldCoreThreshold = sldCoreThreshRes.Slider;
		var cpSssRes = AddColorPicker(vEmissive, TranslationServer.Translate("Subsurface Color:"), Colors.Black, (c) => { if (_activeProfile != null) _activeProfile.SubsurfaceColorHex = "#" + c.ToHtml(true); ApplyLiveMaterialPreview(); });
		_cpSubsurface = cpSssRes.Picker;
		var sldSssRes = AddSlider(vEmissive, TranslationServer.Translate("Subsurface Power:"), 0.0f, 5.0f, 0.1f, 0.0f, (v) => { if (_activeProfile != null) _activeProfile.SubsurfaceStrength = v; ApplyLiveMaterialPreview(); });
		_sldSubsurfaceStrength = sldSssRes.Slider;

		AddSectionHeader(vEmissive, TranslationServer.Translate("Surface Detail Overlay (Algae / Foam / Crust)"));
		_chkUseDetail = AddCheckBox(vEmissive, TranslationServer.Translate("Enable Surface Detail Overlay"), false, (val) => { if (_activeProfile != null) _activeProfile.UseDetailTexture = val; ApplyLiveMaterialPreview(); });
		var detPathRow = new HBoxContainer();
		detPathRow.AddChild(new Label { Text = TranslationServer.Translate("Detail Texture File:"), CustomMinimumSize = new Vector2(110, 0) });
		_txtDetailPath = new LineEdit { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
		_txtDetailPath.TextChanged += (t) => { if (_activeProfile != null) _activeProfile.DetailTexturePath = t; ApplyLiveMaterialPreview(); };
		detPathRow.AddChild(_txtDetailPath);
		vEmissive.AddChild(detPathRow);

		var sldDetUvXRes = AddSlider(vEmissive, TranslationServer.Translate("Detail UV Scale X:"), 0.1f, 10.0f, 0.1f, 1.0f, (v) => { if (_activeProfile != null) _activeProfile.DetailUvScaleX = v; ApplyLiveMaterialPreview(); });
		_sldDetailUvScaleX = sldDetUvXRes.Slider;
		var sldDetUvYRes = AddSlider(vEmissive, TranslationServer.Translate("Detail UV Scale Y:"), 0.1f, 10.0f, 0.1f, 1.0f, (v) => { if (_activeProfile != null) _activeProfile.DetailUvScaleY = v; ApplyLiveMaterialPreview(); });
		_sldDetailUvScaleY = sldDetUvYRes.Slider;
		var sldDetScrXRes = AddSlider(vEmissive, TranslationServer.Translate("Detail Scroll X:"), -2.0f, 2.0f, 0.05f, 0.0f, (v) => { if (_activeProfile != null) _activeProfile.DetailUvScrollX = v; ApplyLiveMaterialPreview(); });
		_sldDetailUvScrollX = sldDetScrXRes.Slider;
		var sldDetScrYRes = AddSlider(vEmissive, TranslationServer.Translate("Detail Scroll Y:"), -2.0f, 2.0f, 0.05f, 0.0f, (v) => { if (_activeProfile != null) _activeProfile.DetailUvScrollY = v; ApplyLiveMaterialPreview(); });
		_sldDetailUvScrollY = sldDetScrYRes.Slider;
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

		var tabPathingAndBombing = new ScrollContainer();
		tabPathingAndBombing.Name = TranslationServer.Translate("Pathing & Bombing");
		var vBombing = new VBoxContainer();
		vBombing.AddThemeConstantOverride("separation", 6);
		vBombing.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
		tabPathingAndBombing.AddChild(vBombing);
		tabContainer.AddChild(tabPathingAndBombing);

		AddSectionHeader(vBombing, TranslationServer.Translate("Default Pathing Capabilities"));
		AddDescription(vBombing, TranslationServer.Translate("Automatically assigned to cells painted with this liquid profile:"));
		_chkPathShallow = AddCheckBox(vBombing, TranslationServer.Translate("Shallow Water Passable"), true, (val) => UpdatePathingMask());
		_chkPathDeep = AddCheckBox(vBombing, TranslationServer.Translate("Deep Water Passable"), false, (val) => UpdatePathingMask());
		_chkPathGround = AddCheckBox(vBombing, TranslationServer.Translate("Ground Passable"), false, (val) => UpdatePathingMask());
		_chkPathBuildable = AddCheckBox(vBombing, TranslationServer.Translate("Buildable Ground"), false, (val) => UpdatePathingMask());
		_chkPathFlying = AddCheckBox(vBombing, TranslationServer.Translate("Flying Passable"), true, (val) => UpdatePathingMask());

		AddSectionHeader(vBombing, TranslationServer.Translate("Procedural Decal Texture Bombing"));
		AddDescription(vBombing, TranslationServer.Translate("Randomly scattered decals placed at liquid elevation during painting:"));
		var decalListRow = new HBoxContainer();
		_lstDecals = new ItemList { CustomMinimumSize = new Vector2(250, 80), SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
		decalListRow.AddChild(_lstDecals);

		var decalBtnBox = new VBoxContainer();
		_txtDecalId = new LineEdit { PlaceholderText = "decal_texture_id" };
		decalBtnBox.AddChild(_txtDecalId);

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

		var sldDecalDensRes = AddSlider(vBombing, TranslationServer.Translate("Decal Density:"), 0.0f, 1.0f, 0.05f, 0.5f, (v) => { if (_activeProfile != null && _activeProfile.DecalBombingRules.Count > 0) _activeProfile.DecalBombingRules[0].Density = v; });
		_sldDecalDensity = sldDecalDensRes.Slider;
		var sldDecalMinScRes = AddSlider(vBombing, TranslationServer.Translate("Min Decal Scale:"), 0.1f, 5.0f, 0.1f, 0.8f, (v) => { if (_activeProfile != null && _activeProfile.DecalBombingRules.Count > 0) _activeProfile.DecalBombingRules[0].MinScale = v; });
		_sldDecalMinScale = sldDecalMinScRes.Slider;
		var sldDecalMaxScRes = AddSlider(vBombing, TranslationServer.Translate("Max Decal Scale:"), 0.1f, 5.0f, 0.1f, 1.2f, (v) => { if (_activeProfile != null && _activeProfile.DecalBombingRules.Count > 0) _activeProfile.DecalBombingRules[0].MaxScale = v; });
		_sldDecalMaxScale = sldDecalMaxScRes.Slider;

		AddSectionHeader(vBombing, TranslationServer.Translate("Procedural VFX / Particle Bombing"));
		AddDescription(vBombing, TranslationServer.Translate("Randomly scattered particle systems & ribbons (bubbles, steam, foam, smoke) placed at liquid elevation:"));
		var vfxListRow = new HBoxContainer();
		_lstVfx = new ItemList { CustomMinimumSize = new Vector2(250, 80), SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
		vfxListRow.AddChild(_lstVfx);

		var vfxBtnBox = new VBoxContainer();
		_txtVfxId = new LineEdit { PlaceholderText = "vfx_template_id" };
		vfxBtnBox.AddChild(_txtVfxId);

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

		var sldVfxDensRes = AddSlider(vBombing, TranslationServer.Translate("VFX Density:"), 0.0f, 1.0f, 0.05f, 0.3f, (v) => { if (_activeProfile != null && _activeProfile.VfxBombingRules.Count > 0) _activeProfile.VfxBombingRules[0].Density = v; });
		_sldVfxDensity = sldVfxDensRes.Slider;
		var sldVfxMinScRes = AddSlider(vBombing, TranslationServer.Translate("Min VFX Scale:"), 0.1f, 5.0f, 0.1f, 0.8f, (v) => { if (_activeProfile != null && _activeProfile.VfxBombingRules.Count > 0) _activeProfile.VfxBombingRules[0].MinScale = v; });
		_sldVfxMinScale = sldVfxMinScRes.Slider;
		var sldVfxMaxScRes = AddSlider(vBombing, TranslationServer.Translate("Max VFX Scale:"), 0.1f, 5.0f, 0.1f, 1.2f, (v) => { if (_activeProfile != null && _activeProfile.VfxBombingRules.Count > 0) _activeProfile.VfxBombingRules[0].MaxScale = v; });
		_sldVfxMaxScale = sldVfxMaxScRes.Slider;
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

		UpdateProfileListUI();
		SelectProfile(Math.Clamp(selectedIdx, 0, _profiles.Count - 1));
		OpenDialog();
	}

	private void UpdateProfileListUI()
	{
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
		PopulateFormFromActiveProfile();
		ApplyLiveMaterialPreview();
	}

	private void PopulateFormFromActiveProfile()
	{
		if (_activeProfile == null) return;
		_txtName.Text = _activeProfile.Name;
		_optWaterType.Selected = _activeProfile.WaterType == WaterType.Deep ? 1 : 0;

		_cpShallow.Color = Color.HtmlIsValid(_activeProfile.ShallowColorHex) ? Color.FromHtml(_activeProfile.ShallowColorHex) : Colors.Teal;
		_cpDeep.Color = Color.HtmlIsValid(_activeProfile.DeepColorHex) ? Color.FromHtml(_activeProfile.DeepColorHex) : Colors.NavyBlue;
		_cpFoam.Color = Color.HtmlIsValid(_activeProfile.FoamColorHex) ? Color.FromHtml(_activeProfile.FoamColorHex) : Colors.White;

		_sldMaxDepth.Value = _activeProfile.MaxDepth;
		_sldFoamDepth.Value = _activeProfile.FoamDepth;
		_sldWaveSpeed.Value = _activeProfile.WaveSpeed;
		_sldWaveStrength.Value = _activeProfile.WaveStrength;

		_chkUseNormal.ButtonPressed = _activeProfile.UseNormalTexture;
		_txtNormalPath.Text = _activeProfile.NormalTexturePath ?? "";
		_sldNormalScale.Value = _activeProfile.NormalScale;
		_sldFlowDirX.Value = _activeProfile.FlowDirectionX;
		_sldFlowDirY.Value = _activeProfile.FlowDirectionY;
		_sldFlowSpeed.Value = _activeProfile.FlowSpeed;
		_chkUseFlowMap.ButtonPressed = _activeProfile.UseFlowMap;
		_txtFlowMapPath.Text = _activeProfile.FlowMapPath ?? "";

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
		_txtDetailPath.Text = _activeProfile.DetailTexturePath ?? "";
		_sldDetailUvScaleX.Value = _activeProfile.DetailUvScaleX;
		_sldDetailUvScaleY.Value = _activeProfile.DetailUvScaleY;
		_sldDetailUvScrollX.Value = _activeProfile.DetailUvScrollX;
		_sldDetailUvScrollY.Value = _activeProfile.DetailUvScrollY;
		_sldDetailAlpha.Value = _activeProfile.DetailAlpha;
		_optDetailBlend.Selected = Math.Clamp(_activeProfile.DetailBlendMode, 0, 2);

		int code = _activeProfile.DefaultPathingCode;
		_chkPathShallow.ButtonPressed = (code & EditableTerrain.PATHING_SHALLOW_WATER) != 0;
		_chkPathDeep.ButtonPressed = (code & EditableTerrain.PATHING_DEEP_WATER) != 0;
		_chkPathGround.ButtonPressed = (code & EditableTerrain.PATHING_GROUND) != 0;
		_chkPathBuildable.ButtonPressed = (code & EditableTerrain.PATHING_BUILDABLE) != 0;
		_chkPathFlying.ButtonPressed = (code & EditableTerrain.PATHING_FLYING) != 0;

		UpdateDecalListUI();
		UpdateVfxListUI();
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
			_lstDecals.AddItem($"{r.DecalId} (Dens: {r.Density:0.00}, Scale: {r.MinScale:0.0}-{r.MaxScale:0.0})");
		}
		if (_activeProfile.DecalBombingRules.Count > 0)
		{
			var first = _activeProfile.DecalBombingRules[0];
			_sldDecalDensity.Value = first.Density;
			_sldDecalMinScale.Value = first.MinScale;
			_sldDecalMaxScale.Value = first.MaxScale;
		}
	}

	private void UpdateVfxListUI()
	{
		_lstVfx.Clear();
		if (_activeProfile?.VfxBombingRules == null) return;
		foreach (var r in _activeProfile.VfxBombingRules)
		{
			_lstVfx.AddItem($"{r.VfxId} (Dens: {r.Density:0.00}, Scale: {r.MinScale:0.0}-{r.MaxScale:0.0})");
		}
		if (_activeProfile.VfxBombingRules.Count > 0)
		{
			var first = _activeProfile.VfxBombingRules[0];
			_sldVfxDensity.Value = first.Density;
			_sldVfxMinScale.Value = first.MinScale;
			_sldVfxMaxScale.Value = first.MaxScale;
		}
	}

	private void OnAddDecalRule()
	{
		if (_activeProfile == null || string.IsNullOrWhiteSpace(_txtDecalId.Text)) return;
		_activeProfile.DecalBombingRules.Add(new ProceduralBombingDecalRule
		{
			DecalId = _txtDecalId.Text.Trim(),
			Density = (float)_sldDecalDensity.Value,
			MinScale = (float)_sldDecalMinScale.Value,
			MaxScale = (float)_sldDecalMaxScale.Value
		});
		_txtDecalId.Text = "";
		UpdateDecalListUI();
	}

	private void OnRemoveDecalRule()
	{
		if (_activeProfile == null || _lstDecals.GetSelectedItems().Length == 0) return;
		int sel = _lstDecals.GetSelectedItems()[0];
		if (sel >= 0 && sel < _activeProfile.DecalBombingRules.Count)
		{
			_activeProfile.DecalBombingRules.RemoveAt(sel);
			UpdateDecalListUI();
		}
	}

	private void OnAddVfxRule()
	{
		if (_activeProfile == null || string.IsNullOrWhiteSpace(_txtVfxId.Text)) return;
		_activeProfile.VfxBombingRules.Add(new ProceduralBombingVfxRule
		{
			VfxId = _txtVfxId.Text.Trim(),
			Density = (float)_sldVfxDensity.Value,
			MinScale = (float)_sldVfxMinScale.Value,
			MaxScale = (float)_sldVfxMaxScale.Value
		});
		_txtVfxId.Text = "";
		UpdateVfxListUI();
	}

	private void OnRemoveVfxRule()
	{
		if (_activeProfile == null || _lstVfx.GetSelectedItems().Length == 0) return;
		int sel = _lstVfx.GetSelectedItems()[0];
		if (sel >= 0 && sel < _activeProfile.VfxBombingRules.Count)
		{
			_activeProfile.VfxBombingRules.RemoveAt(sel);
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
			ProfileIndex = nextIndex,
			WaterType = WaterType.Shallow
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
}
