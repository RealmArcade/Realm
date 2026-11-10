// Scene construction: the top row and each tab of the container, split further where a tab holds
// two independent groups (normals, emissive, pathing/bombing). The rule lists build their rows here
// and then lean on UpdateDecalRuleSetting / UpdateVfxRuleSetting to write a row back.

using System;
using System.Collections.Generic;
using System.IO;
using Godot;
using Realm.Client.Core;
using Realm.Client.Services;
using Realm.Client.Utils;


namespace Realm.Client.UI.MapEditor;

public partial class WaterProfileDialog
{
	private void BuildUI()
	{
		BuildTopRow();

		var tabContainer = new TabContainer();
		tabContainer.Name = "TabContainer_TabContainer";
		tabContainer.SizeFlagsVertical = SizeFlags.ExpandFill;
		tabContainer.CustomMinimumSize = new Vector2(580, 520);
		BodyContainer.AddChild(tabContainer);

		BuildTabGeneral(tabContainer);
		BuildTabNormals(tabContainer);
		BuildTabEmissive(tabContainer);
		BuildTabPathingAndBombing(tabContainer);
	}

	private void BuildTopRow()
	{
		var topRow = new HBoxContainer();
		topRow.Name = "TopRow_HBoxContainer";
		topRow.AddThemeConstantOverride("separation", 8);

		var lblProf = new Label();
		lblProf.Name = "LblProf_Label";
		lblProf.Text = TranslationServer.Translate("Profile:");
		lblProf.AddThemeFontSizeOverride("font_size", 12);
		topRow.AddChild(lblProf);

		_optProfileSelect = new OptionButton();
		_optProfileSelect.Name = "OptProfileSelect_OptionButton";
		_optProfileSelect.SizeFlagsHorizontal = SizeFlags.ExpandFill;
		_optProfileSelect.ItemSelected += (idx) => SelectProfile((int)idx);
		topRow.AddChild(_optProfileSelect);

		_btnAddProfile = new Button();
		_btnAddProfile.Name = "BtnAddProfile_Button";
		_btnAddProfile.Set("icon_max_width", 0);
		_btnAddProfile.Text = "+ " + TranslationServer.Translate("NEW");
		_btnAddProfile.Pressed += OnAddProfileClicked;
		topRow.AddChild(_btnAddProfile);

		_btnDeleteProfile = new Button();
		_btnDeleteProfile.Name = "BtnDeleteProfile_Button";
		_btnDeleteProfile.Set("icon_max_width", 0);
		_btnDeleteProfile.Text = TranslationServer.Translate("DELETE");
		_btnDeleteProfile.Pressed += OnDeleteProfileClicked;
		topRow.AddChild(_btnDeleteProfile);

		BodyContainer.AddChild(topRow);
	}

	private void BuildTabGeneral(TabContainer tabContainer)
	{
		var tabGeneral = new ScrollContainer();
		tabGeneral.Name = TranslationServer.Translate("General & Colors");
		var vGeneral = new VBoxContainer();
		vGeneral.Name = "VGeneral_VBoxContainer";
		vGeneral.AddThemeConstantOverride("separation", 6);
		vGeneral.SizeFlagsHorizontal = SizeFlags.ExpandFill;
		tabGeneral.AddChild(vGeneral);
		tabContainer.AddChild(tabGeneral);

		AddSectionHeader(vGeneral, TranslationServer.Translate("Profile Identity"));
		var nameRow = new HBoxContainer();
		nameRow.Name = "NameRow_HBoxContainer";
		nameRow.AddChild(new Label { Text = TranslationServer.Translate("Display Name:"), CustomMinimumSize = new Vector2(110, 0) });
		_txtName = new LineEdit { SizeFlagsHorizontal = SizeFlags.ExpandFill };
		_txtName.TextChanged += (text) =>
		{
			if (_activeProfile != null)
			{
				_activeProfile.Name = text;
				UpdateProfileListUI();
			}
		};
		nameRow.AddChild(_txtName);
		vGeneral.AddChild(nameRow);

		var btnRandomizeAll = CreateRandomizeButton(() => RandomizeAllWaterParameters());
		vGeneral.AddChild(btnRandomizeAll);

		AddSectionHeader(vGeneral, TranslationServer.Translate("Colors & Transparency"));
		var cpRes1 = AddColorPicker(vGeneral, TranslationServer.Translate("Shallow Tint:"), Colors.Teal, (c) =>
		{
			if (_activeProfile != null) _activeProfile.ShallowColorHex = "#" + c.ToHtml(true);
			ApplyLiveMaterialPreview();
		});
		_cpShallow = cpRes1.Picker;
		var cpRes2 = AddColorPicker(vGeneral, TranslationServer.Translate("Deep Tint:"), Colors.NavyBlue, (c) =>
		{
			if (_activeProfile != null) _activeProfile.DeepColorHex = "#" + c.ToHtml(true);
			ApplyLiveMaterialPreview();
		});
		_cpDeep = cpRes2.Picker;
		var cpRes3 = AddColorPicker(vGeneral, TranslationServer.Translate("Foam Tint:"), Colors.White, (c) =>
		{
			if (_activeProfile != null) _activeProfile.FoamColorHex = "#" + c.ToHtml(true);
			ApplyLiveMaterialPreview();
		});
		_cpFoam = cpRes3.Picker;

		var sldMaxDepthRes = AddSlider(vGeneral, TranslationServer.Translate("Max Depth:"), 0.1f, 10.0f, 0.1f, 2.0f, (v) =>
		{
			if (_activeProfile != null) _activeProfile.MaxDepth = v;
			ApplyLiveMaterialPreview();
		});
		_sldMaxDepth = sldMaxDepthRes.Slider;
		var sldFoamDepthRes = AddSlider(vGeneral, TranslationServer.Translate("Foam Distance:"), 0.05f, 3.0f, 0.05f, 0.6f, (v) =>
		{
			if (_activeProfile != null) _activeProfile.FoamDepth = v;
			ApplyLiveMaterialPreview();
		});
		_sldFoamDepth = sldFoamDepthRes.Slider;
		var sldWaveSpdRes = AddSlider(vGeneral, TranslationServer.Translate("Wave Speed:"), 0.0f, 5.0f, 0.1f, 1.2f, (v) =>
		{
			if (_activeProfile != null) _activeProfile.WaveSpeed = v;
			ApplyLiveMaterialPreview();
		});
		_sldWaveSpeed = sldWaveSpdRes.Slider;
		var sldWaveStrRes = AddSlider(vGeneral, TranslationServer.Translate("Wave Amplitude:"), 0.0f, 0.5f, 0.01f, 0.06f, (v) =>
		{
			if (_activeProfile != null) _activeProfile.WaveStrength = v;
			ApplyLiveMaterialPreview();
		});
		_sldWaveStrength = sldWaveStrRes.Slider;
	}

	private void BuildTabNormals(TabContainer tabContainer)
	{
		var tabNormals = new ScrollContainer();
		tabNormals.Name = TranslationServer.Translate("Flow & Optics");
		var vNormals = new VBoxContainer();
		vNormals.Name = "VNormals_VBoxContainer";
		vNormals.AddThemeConstantOverride("separation", 6);
		vNormals.SizeFlagsHorizontal = SizeFlags.ExpandFill;
		tabNormals.AddChild(vNormals);
		tabContainer.AddChild(tabNormals);

		BuildTabNormals_NormalMapping(vNormals);
		BuildTabNormals_Refraction(vNormals);
	}

	private void BuildTabNormals_NormalMapping(VBoxContainer vNormals)
	{
		AddSectionHeader(vNormals, TranslationServer.Translate("Normal Mapping & Flow"));
		_chkUseNormal = AddCheckBox(vNormals, TranslationServer.Translate("Enable Custom Normal Texture"), false, (val) =>
		{
			if (_activeProfile != null) _activeProfile.UseNormalTexture = val;
			ApplyLiveMaterialPreview();
		});
		var (txtNorm, setNorm) = AddAssetFilterDropdown(
			vNormals,
			TranslationServer.Translate("Normal Map File:"),
			"",
			(all) => ScanRtexAssets(all),
			(val) =>
			{
				if (_activeProfile != null)
				{
					_activeProfile.NormalTexturePath = val;
					ApplyLiveMaterialPreview();
				}
			},
			TranslationServer.Translate("Select .rtex normal map..."),
			110f
		);
		_txtNormalPath = txtNorm;
		_setNormalPath = setNorm;

		var btnRandomizeFlow = CreateRandomizeButton(() => RandomizeNormalAndFlowParameters());
		vNormals.AddChild(btnRandomizeFlow);

		var sldNormScaleRes = AddSlider(vNormals, TranslationServer.Translate("Normal Strength:"), 0.0f, 3.0f, 0.1f, 1.0f, (v) =>
		{
			if (_activeProfile != null) _activeProfile.NormalScale = v;
			ApplyLiveMaterialPreview();
		});
		_sldNormalScale = sldNormScaleRes.Slider;
		var sldFlowXRes = AddSlider(vNormals, TranslationServer.Translate("Flow Direction X:"), -1.0f, 1.0f, 0.05f, 1.0f, (v) =>
		{
			if (_activeProfile != null) _activeProfile.FlowDirectionX = v;
			ApplyLiveMaterialPreview();
		});
		_sldFlowDirX = sldFlowXRes.Slider;
		var sldFlowYRes = AddSlider(vNormals, TranslationServer.Translate("Flow Direction Y:"), -1.0f, 1.0f, 0.05f, 0.0f, (v) =>
		{
			if (_activeProfile != null) _activeProfile.FlowDirectionY = v;
			ApplyLiveMaterialPreview();
		});
		_sldFlowDirY = sldFlowYRes.Slider;
		var sldFlowSpdRes = AddSlider(vNormals, TranslationServer.Translate("Flow Velocity:"), 0.0f, 5.0f, 0.1f, 0.5f, (v) =>
		{
			if (_activeProfile != null) _activeProfile.FlowSpeed = v;
			ApplyLiveMaterialPreview();
		});
		_sldFlowSpeed = sldFlowSpdRes.Slider;

		_chkUseFlowMap = AddCheckBox(vNormals, TranslationServer.Translate("Enable Directional Flow Map"), false, (val) =>
		{
			if (_activeProfile != null) _activeProfile.UseFlowMap = val;
			ApplyLiveMaterialPreview();
		});
		var (txtFlow, setFlow) = AddAssetFilterDropdown(
			vNormals,
			TranslationServer.Translate("Flow Map File:"),
			"",
			(all) => ScanRtexAssets(all),
			(val) =>
			{
				if (_activeProfile != null)
				{
					_activeProfile.FlowMapPath = val;
					ApplyLiveMaterialPreview();
				}
			},
			TranslationServer.Translate("Select .rtex flow map..."),
			110f
		);
		_txtFlowMapPath = txtFlow;
		_setFlowMapPath = setFlow;
	}

	private void BuildTabNormals_Refraction(VBoxContainer vNormals)
	{
		AddSectionHeader(vNormals, TranslationServer.Translate("Refraction & Caustics"));
		var btnRandomizeOptics = CreateRandomizeButton(() => RandomizeRefractionAndCausticsParameters());
		vNormals.AddChild(btnRandomizeOptics);

		var sldRefrRes = AddSlider(vNormals, TranslationServer.Translate("Refraction Index:"), 0.0f, 1.0f, 0.02f, 0.0f, (v) =>
		{
			if (_activeProfile != null) _activeProfile.RefractionStrength = v;
			ApplyLiveMaterialPreview();
		});
		_sldRefraction = sldRefrRes.Slider;
		var sldCaustStrRes = AddSlider(vNormals, TranslationServer.Translate("Caustics Intensity:"), 0.0f, 2.0f, 0.05f, 0.0f, (v) =>
		{
			if (_activeProfile != null) _activeProfile.CausticStrength = v;
			ApplyLiveMaterialPreview();
		});
		_sldCausticStrength = sldCaustStrRes.Slider;
		var sldCaustScaleRes = AddSlider(vNormals, TranslationServer.Translate("Caustics Scale:"), 0.1f, 10.0f, 0.1f, 1.0f, (v) =>
		{
			if (_activeProfile != null) _activeProfile.CausticScale = v;
			ApplyLiveMaterialPreview();
		});
		_sldCausticScale = sldCaustScaleRes.Slider;
		var sldCaustSpdRes = AddSlider(vNormals, TranslationServer.Translate("Caustics Speed:"), 0.0f, 5.0f, 0.1f, 1.0f, (v) =>
		{
			if (_activeProfile != null) _activeProfile.CausticSpeed = v;
			ApplyLiveMaterialPreview();
		});
		_sldCausticSpeed = sldCaustSpdRes.Slider;
	}

	private void BuildTabEmissive(TabContainer tabContainer)
	{
		var tabEmissive = new ScrollContainer();
		tabEmissive.Name = TranslationServer.Translate("Emission & Surface");
		var vEmissive = new VBoxContainer();
		vEmissive.Name = "VEmissive_VBoxContainer";
		vEmissive.AddThemeConstantOverride("separation", 6);
		vEmissive.SizeFlagsHorizontal = SizeFlags.ExpandFill;
		tabEmissive.AddChild(vEmissive);
		tabContainer.AddChild(tabEmissive);

		BuildTabEmissive_Core(vEmissive);
		BuildTabEmissive_SurfaceDetail(vEmissive);
	}

	private void BuildTabEmissive_Core(VBoxContainer vEmissive)
	{
		AddSectionHeader(vEmissive, TranslationServer.Translate("Emissive Heat & Core (Lava / Acid)"));
		var btnRandomizeEmissive = CreateRandomizeButton(() => RandomizeEmissiveParameters());
		vEmissive.AddChild(btnRandomizeEmissive);

		var cpEmRes = AddColorPicker(vEmissive, TranslationServer.Translate("Emission Color:"), Colors.Black, (c) =>
		{
			if (_activeProfile != null) _activeProfile.EmissionColorHex = "#" + c.ToHtml(true);
			ApplyLiveMaterialPreview();
		});
		_cpEmission = cpEmRes.Picker;
		var sldEmBoostRes = AddSlider(vEmissive, TranslationServer.Translate("Emission Boost:"), 0.0f, 2.0f, 0.01f, 0.0f, (v) =>
		{
			if (_activeProfile != null) _activeProfile.EmissionBoost = v;
			ApplyLiveMaterialPreview();
		});
		_sldEmissionBoost = sldEmBoostRes.Slider;
		var cpCoreRes = AddColorPicker(vEmissive, TranslationServer.Translate("Core Hotspot Color:"), Colors.White, (c) =>
		{
			if (_activeProfile != null) _activeProfile.CoreColorHex = "#" + c.ToHtml(true);
			ApplyLiveMaterialPreview();
		});
		_cpCore = cpCoreRes.Picker;
		var sldCoreThreshRes = AddSlider(vEmissive, TranslationServer.Translate("Core Threshold:"), 0.0f, 1.0f, 0.05f, 0.8f, (v) =>
		{
			if (_activeProfile != null) _activeProfile.CoreThreshold = v;
			ApplyLiveMaterialPreview();
		});
		_sldCoreThreshold = sldCoreThreshRes.Slider;
		var cpSssRes = AddColorPicker(vEmissive, TranslationServer.Translate("Subsurface Color:"), Colors.Black, (c) =>
		{
			if (_activeProfile != null) _activeProfile.SubsurfaceColorHex = "#" + c.ToHtml(true);
			ApplyLiveMaterialPreview();
		});
		_cpSubsurface = cpSssRes.Picker;
		var sldSssRes = AddSlider(vEmissive, TranslationServer.Translate("Subsurface Power:"), 0.0f, 5.0f, 0.1f, 0.0f, (v) =>
		{
			if (_activeProfile != null) _activeProfile.SubsurfaceStrength = v;
			ApplyLiveMaterialPreview();
		});
		_sldSubsurfaceStrength = sldSssRes.Slider;
	}

	private void BuildTabEmissive_SurfaceDetail(VBoxContainer vEmissive)
	{
		AddSectionHeader(vEmissive, TranslationServer.Translate("Surface Detail Overlay (Algae / Foam / Crust)"));
		_chkUseDetail = AddCheckBox(vEmissive, TranslationServer.Translate("Enable Surface Detail Overlay"), false, (val) =>
		{
			if (_activeProfile != null) _activeProfile.UseDetailTexture = val;
			ApplyLiveMaterialPreview();
		});
		var (txtDetail, setDetail) = AddAssetFilterDropdown(
			vEmissive,
			TranslationServer.Translate("Detail Texture File:"),
			"",
			(all) => ScanRtexAssets(all),
			(val) =>
			{
				if (_activeProfile != null)
				{
					_activeProfile.DetailTexturePath = val;
					ApplyLiveMaterialPreview();
				}
			},
			TranslationServer.Translate("Select .rtex detail texture..."),
			110f
		);
		_txtDetailPath = txtDetail;
		_setDetailPath = setDetail;

		var btnRandomizeDetail = CreateRandomizeButton(() => RandomizeDetailOverlayParameters());
		vEmissive.AddChild(btnRandomizeDetail);

		BuildTabEmissive_TileMode(vEmissive);
		BuildTabEmissive_Sliders(vEmissive);
		BuildTabEmissive_BlendMode(vEmissive);
	}

	private void BuildTabEmissive_TileMode(VBoxContainer vEmissive)
	{
		var tileModeRow = new HBoxContainer();
		tileModeRow.Name = "TileModeRow_HBoxContainer";
		tileModeRow.AddChild(new Label { Text = TranslationServer.Translate("Tile Mode:"), CustomMinimumSize = new Vector2(110, 0) });
		_optDetailTileMode = new OptionButton();
		_optDetailTileMode.Name = "OptDetailTileMode_OptionButton";
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
	}

	private void BuildTabEmissive_Sliders(VBoxContainer vEmissive)
	{
		var sldDetUvXRes = AddSlider(vEmissive, TranslationServer.Translate("Detail UV Scale X:"), 0.1f, 10.0f, 0.1f, 1.0f, (v) =>
		{
			if (_activeProfile != null) _activeProfile.DetailUvScaleX = v;
			ApplyLiveMaterialPreview();
		});
		_sldDetailUvScaleX = sldDetUvXRes.Slider;
		var sldDetUvYRes = AddSlider(vEmissive, TranslationServer.Translate("Detail UV Scale Y:"), 0.1f, 10.0f, 0.1f, 1.0f, (v) =>
		{
			if (_activeProfile != null) _activeProfile.DetailUvScaleY = v;
			ApplyLiveMaterialPreview();
		});
		_sldDetailUvScaleY = sldDetUvYRes.Slider;
		var sldDetScrXRes = AddSlider(vEmissive, TranslationServer.Translate("Detail Scroll X:"), -2.0f, 2.0f, 0.05f, 0.0f, (v) =>
		{
			if (_activeProfile != null) _activeProfile.DetailUvScrollX = v;
			ApplyLiveMaterialPreview();
		});
		_sldDetailUvScrollX = sldDetScrXRes.Slider;
		var sldDetScrYRes = AddSlider(vEmissive, TranslationServer.Translate("Detail Scroll Y:"), -2.0f, 2.0f, 0.05f, 0.0f, (v) =>
		{
			if (_activeProfile != null) _activeProfile.DetailUvScrollY = v;
			ApplyLiveMaterialPreview();
		});
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

		var sldDetAlphaRes = AddSlider(vEmissive, TranslationServer.Translate("Detail Opacity:"), 0.0f, 1.0f, 0.05f, 0.5f, (v) =>
		{
			if (_activeProfile != null) _activeProfile.DetailAlpha = v;
			ApplyLiveMaterialPreview();
		});
		_sldDetailAlpha = sldDetAlphaRes.Slider;
	}

	private void BuildTabEmissive_BlendMode(VBoxContainer vEmissive)
	{
		var blendRow = new HBoxContainer();
		blendRow.Name = "BlendRow_HBoxContainer";
		blendRow.AddChild(new Label { Text = TranslationServer.Translate("Blend Mode:"), CustomMinimumSize = new Vector2(110, 0) });
		_optDetailBlend = new OptionButton();
		_optDetailBlend.Name = "OptDetailBlend_OptionButton";
		_optDetailBlend.AddItem(TranslationServer.Translate("Alpha Mix"), 0);
		_optDetailBlend.AddItem(TranslationServer.Translate("Additive"), 1);
		_optDetailBlend.AddItem(TranslationServer.Translate("Multiply"), 2);
		_optDetailBlend.ItemSelected += (idx) =>
		{
			if (_activeProfile != null) _activeProfile.DetailBlendMode = (int)idx;
			ApplyLiveMaterialPreview();
		};
		blendRow.AddChild(_optDetailBlend);
		vEmissive.AddChild(blendRow);
	}

	private void BuildTabPathingAndBombing(TabContainer tabContainer)
	{
		var tabPathingAndBombing = new ScrollContainer();
		tabPathingAndBombing.Name = TranslationServer.Translate("Pathing & Bombing");
		var vBombing = new VBoxContainer();
		vBombing.Name = "VBombing_VBoxContainer";
		vBombing.AddThemeConstantOverride("separation", 6);
		vBombing.SizeFlagsHorizontal = SizeFlags.ExpandFill;
		tabPathingAndBombing.AddChild(vBombing);
		tabContainer.AddChild(tabPathingAndBombing);

		BuildTabPathingAndBombing_Pathing(vBombing);
		BuildTabPathingAndBombing_Decals(vBombing);
		BuildTabPathingAndBombing_VFX(vBombing);
	}

	private void BuildTabPathingAndBombing_Pathing(VBoxContainer vBombing)
	{
		AddSectionHeader(vBombing, TranslationServer.Translate("Default Pathing Capabilities"));
		AddDescription(vBombing, TranslationServer.Translate("Automatically assigned to cells painted with this liquid profile:"));
		_chkPathShallow = AddCheckBox(vBombing, TranslationServer.Translate("Shallow Water"), true, (val) => UpdatePathingMask());
		_chkPathDeep = AddCheckBox(vBombing, TranslationServer.Translate("Deep Water"), false, (val) => UpdatePathingMask());
		_chkPathGround = AddCheckBox(vBombing, TranslationServer.Translate("Ground"), false, (val) => UpdatePathingMask());
		_chkPathBuildable = AddCheckBox(vBombing, TranslationServer.Translate("Buildable"), false, (val) => UpdatePathingMask());
		_chkPathFlying = AddCheckBox(vBombing, TranslationServer.Translate("Flying"), true, (val) => UpdatePathingMask());
	}

	private void BuildTabPathingAndBombing_Decals(VBoxContainer vBombing)
	{
		AddSectionHeader(vBombing, TranslationServer.Translate("Procedural Decal Texture Bombing"));
		AddDescription(vBombing, TranslationServer.Translate("Randomly scattered decals placed at liquid elevation during painting:"));

		var result = AddAssetRuleList(vBombing, "Decal", "decals", "decal_texture_id", OnDecalItemSelected, OnAddDecalRule, OnRemoveDecalRule);
		_lstDecals = result.Item1;
		_txtDecalId = result.Item2;
		_setDecalIdValue = result.Item3;

		var sldDecalDensRes = AddSlider(vBombing, TranslationServer.Translate("Decal Density:"), 0.0f, 1.0f, 0.01f, 0.5f, (v) => UpdateDecalRuleSetting(rule => rule.Density = v));
		_sldDecalDensity = sldDecalDensRes.Slider;
		var sldDecalMinScRes = AddSlider(vBombing, TranslationServer.Translate("Min Decal Scale:"), 0.05f, 1.0f, 0.01f, 0.2f, (v) => UpdateDecalRuleSetting(rule => rule.MinScale = v));
		_sldDecalMinScale = sldDecalMinScRes.Slider;
		var sldDecalMaxScRes = AddSlider(vBombing, TranslationServer.Translate("Max Decal Scale:"), 0.05f, 1.0f, 0.01f, 0.5f, (v) => UpdateDecalRuleSetting(rule => rule.MaxScale = v));
		_sldDecalMaxScale = sldDecalMaxScRes.Slider;
	}

	private void OnDecalItemSelected(long idx)
	{
		if (_activeProfile == null || idx < 0 || idx >= _activeProfile.DecalBombingRules.Count) return;

		var r = _activeProfile.DecalBombingRules[(int)idx];
		_setDecalIdValue?.Invoke(r.DecalId);
		SetSliderValue(_sldDecalDensity, r.Density);
		SetSliderValue(_sldDecalMinScale, r.MinScale);
		SetSliderValue(_sldDecalMaxScale, r.MaxScale);
	}

	private void BuildTabPathingAndBombing_VFX(VBoxContainer vBombing)
	{
		AddSectionHeader(vBombing, TranslationServer.Translate("Procedural VFX / Particle Bombing"));
		AddDescription(vBombing, TranslationServer.Translate("Randomly scattered particle systems & ribbons (bubbles, steam, foam, smoke) placed at liquid elevation:"));

		var result = AddAssetRuleList(vBombing, "VFX", "vfx", "vfx_template_id", OnVfxItemSelected, OnAddVfxRule, OnRemoveVfxRule);
		_lstVfx = result.Item1;
		_txtVfxId = result.Item2;
		_setVfxIdValue = result.Item3;

		var sldVfxDensRes = AddSlider(vBombing, TranslationServer.Translate("VFX Density:"), 0.0f, 1.0f, 0.01f, 0.5f, (v) => UpdateVfxRuleSetting(rule => rule.Density = v));
		_sldVfxDensity = sldVfxDensRes.Slider;
		var sldVfxMinScRes = AddSlider(vBombing, TranslationServer.Translate("Min VFX Scale:"), 0.05f, 1.0f, 0.01f, 0.2f, (v) => UpdateVfxRuleSetting(rule => rule.MinScale = v));
		_sldVfxMinScale = sldVfxMinScRes.Slider;
		var sldVfxMaxScRes = AddSlider(vBombing, TranslationServer.Translate("Max VFX Scale:"), 0.05f, 1.0f, 0.01f, 0.5f, (v) => UpdateVfxRuleSetting(rule => rule.MaxScale = v));
		_sldVfxMaxScale = sldVfxMaxScRes.Slider;
	}

	private void OnVfxItemSelected(long idx)
	{
		if (_activeProfile == null || idx < 0 || idx >= _activeProfile.VfxBombingRules.Count) return;

		var r = _activeProfile.VfxBombingRules[(int)idx];
		_setVfxIdValue?.Invoke(r.VfxId);
		SetSliderValue(_sldVfxDensity, r.Density);
		SetSliderValue(_sldVfxMinScale, r.MinScale);
		SetSliderValue(_sldVfxMaxScale, r.MaxScale);
	}

	private void UpdateDecalRuleSetting(Action<ProceduralBombingDecalRule> updateAction)
	{
		if (_activeProfile == null || _activeProfile.DecalBombingRules.Count == 0) return;
		var sel = _lstDecals.GetSelectedItems();
		int idx = sel.Length > 0 ? sel[0] : 0;
		if (idx >= 0 && idx < _activeProfile.DecalBombingRules.Count)
		{
			updateAction(_activeProfile.DecalBombingRules[idx]);
		}
	}

	private void UpdateVfxRuleSetting(Action<ProceduralBombingVfxRule> updateAction)
	{
		if (_activeProfile == null || _activeProfile.VfxBombingRules.Count == 0) return;
		var sel = _lstVfx.GetSelectedItems();
		int idx = sel.Length > 0 ? sel[0] : 0;
		if (idx >= 0 && idx < _activeProfile.VfxBombingRules.Count)
		{
			updateAction(_activeProfile.VfxBombingRules[idx]);
		}
	}

	private Button CreateRandomizeButton(Action onPressed)
	{
		var btn = CreateDialogButton();
		btn.Text = "🎲 " + TranslationServer.Translate("Randomize All");
		btn.CustomMinimumSize = new Vector2(0, 28);
		btn.SizeFlagsHorizontal = SizeFlags.ShrinkBegin;
		btn.AddThemeFontSizeOverride("font_size", 11);
		UIStyle.ApplyMapEditorRandomizeButtonStyle(btn);

		btn.Pressed += onPressed;
		return btn;
	}

}