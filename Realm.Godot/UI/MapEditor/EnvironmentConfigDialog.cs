using System;
using System.Collections.Generic;
using System.IO;
using Godot;
using Realm.Godot.Services;

public partial class EnvironmentConfigDialog : FloatingDialogBase
{
	private List<EnvironmentPresetConfig> _presets = new();
	private int _selectedPresetIdx = 0;
	private EnvironmentPresetConfig _activePreset;
	private List<EnvironmentPresetConfig> _initialSnapshots = new();
	private string _defaultPresetId = "day";

	private OptionButton _optPresetSelect;
	private Button _btnAddPreset;
	private Button _btnDuplicatePreset;
	private Button _btnDeletePreset;
	private Button _btnSetDefault;
	private Button _btnResetDefaults;
	private CheckBox _chkLivePreview;

	private LineEdit _txtName;
	private LineEdit _txtId;
	private Label _lblDefaultBadge;

	private HSlider _sldSunPitch;
	private HSlider _sldSunYaw;
	private HSlider _sldSunEnergy;
	private ColorPickerButton _cpSunColor;
	private HSlider _sldShadowBias;
	private HSlider _sldShadowNormalBias;

	private HSlider _sldAmbientEnergy;
	private ColorPickerButton _cpAmbientColor;
	private HSlider _sldCharacterFill;

	private CheckBox _chkFogEnabled;
	private HSlider _sldFogDensity;
	private ColorPickerButton _cpFogColor;
	private HSlider _sldBaseFogDensity;

	private CheckBox _chkSsaoEnabled;
	private HSlider _sldSsaoRadius;
	private HSlider _sldSsaoIntensity;
	private HSlider _sldSsaoDetail;

	private HSlider _sldExposure;
	private HSlider _sldContrast;
	private HSlider _sldSaturation;
	private HSlider _sldGlowIntensity;
	private HSlider _sldGlowBloom;
	private HSlider _sldGlowStrength;

	private OptionButton _optWeatherType;
	private HSlider _sldRainDensity;
	private LineEdit _txtAnnouncement;

	public EnvironmentConfigDialog(MapEditorHUD hud) : base(hud, TranslationServer.Translate("WEATHER & LIGHTING ENVIRONMENT PRESETS"), new Vector2(640, 700))
	{
		BuildUI();
	}

	private void BuildUI()
	{
		var topRow = new HBoxContainer();
		topRow.AddThemeConstantOverride("separation", 6);

		var lblPreset = new Label();
		lblPreset.Text = TranslationServer.Translate("Preset:");
		lblPreset.AddThemeFontSizeOverride("font_size", 12);
		topRow.AddChild(lblPreset);

		_optPresetSelect = new OptionButton();
		_optPresetSelect.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
		_optPresetSelect.ItemSelected += (idx) => SelectPreset((int)idx);
		topRow.AddChild(_optPresetSelect);

		_btnAddPreset = new Button();
		_btnAddPreset.Set("icon_max_width", 0);
		_btnAddPreset.Text = "+ " + TranslationServer.Translate("NEW");
		_btnAddPreset.Pressed += OnAddPresetClicked;
		topRow.AddChild(_btnAddPreset);

		_btnDuplicatePreset = new Button();
		_btnDuplicatePreset.Set("icon_max_width", 0);
		_btnDuplicatePreset.Text = TranslationServer.Translate("DUPLICATE");
		_btnDuplicatePreset.Pressed += OnDuplicatePresetClicked;
		topRow.AddChild(_btnDuplicatePreset);

		_btnDeletePreset = new Button();
		_btnDeletePreset.Set("icon_max_width", 0);
		_btnDeletePreset.Text = TranslationServer.Translate("DELETE");
		_btnDeletePreset.Pressed += OnDeletePresetClicked;
		topRow.AddChild(_btnDeletePreset);

		_btnSetDefault = new Button();
		_btnSetDefault.Set("icon_max_width", 0);
		_btnSetDefault.Text = "★ " + TranslationServer.Translate("SET DEFAULT");
		_btnSetDefault.Pressed += OnSetDefaultClicked;
		topRow.AddChild(_btnSetDefault);

		_btnResetDefaults = new Button();
		_btnResetDefaults.Set("icon_max_width", 0);
		_btnResetDefaults.Text = "↺ " + TranslationServer.Translate("RESET DEFAULTS");
		_btnResetDefaults.Pressed += OnResetDefaultsClicked;
		topRow.AddChild(_btnResetDefaults);

		BodyContainer.AddChild(topRow);

		var previewRow = new HBoxContainer();
		previewRow.AddThemeConstantOverride("separation", 8);
		_chkLivePreview = new CheckBox();
		_chkLivePreview.Text = TranslationServer.Translate("Live Viewport Preview");
		_chkLivePreview.ButtonPressed = true;
		_chkLivePreview.Toggled += (val) => { if (val) ApplyLivePreview(); };
		previewRow.AddChild(_chkLivePreview);

		_lblDefaultBadge = new Label();
		_lblDefaultBadge.Text = "";
		_lblDefaultBadge.AddThemeColorOverride("font_color", UIStyle.ColorGold);
		_lblDefaultBadge.AddThemeFontSizeOverride("font_size", 11);
		previewRow.AddChild(_lblDefaultBadge);

		BodyContainer.AddChild(previewRow);

		var tabContainer = new TabContainer();
		tabContainer.SizeFlagsVertical = SizeFlags.ExpandFill;
		tabContainer.CustomMinimumSize = new Vector2(600, 520);
		BodyContainer.AddChild(tabContainer);

		// Tab 1: Sun & Directional Light
		var tabSun = new ScrollContainer();
		tabSun.Name = TranslationServer.Translate("Sun & Directional Light");
		var vSun = new VBoxContainer();
		vSun.AddThemeConstantOverride("separation", 6);
		vSun.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
		tabSun.AddChild(vSun);
		tabContainer.AddChild(tabSun);

		AddSectionHeader(vSun, TranslationServer.Translate("Preset Identity"));
		var nameRow = new HBoxContainer();
		nameRow.AddChild(new Label { Text = TranslationServer.Translate("Display Name:"), CustomMinimumSize = new Vector2(130, 0) });
		_txtName = new LineEdit { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
		_txtName.TextChanged += (text) => { if (_activePreset != null) { _activePreset.Name = text; UpdatePresetListUI(); } };
		nameRow.AddChild(_txtName);
		vSun.AddChild(nameRow);

		var idRow = new HBoxContainer();
		idRow.AddChild(new Label { Text = TranslationServer.Translate("Preset ID:"), CustomMinimumSize = new Vector2(130, 0) });
		_txtId = new LineEdit { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
		_txtId.TextChanged += (text) => { if (_activePreset != null) { _activePreset.Id = text.Trim(); UpdatePresetListUI(); } };
		idRow.AddChild(_txtId);
		vSun.AddChild(idRow);

		AddSectionHeader(vSun, TranslationServer.Translate("Directional Light (Sun / Moon)"));
		var sldPitchRes = AddSlider(vSun, TranslationServer.Translate("Sun Pitch (Deg):"), -90.0f, 90.0f, 1.0f, -58.0f, (v) => { if (_activePreset != null) _activePreset.SunPitch = v; ApplyLivePreview(); }, "0.0");
		_sldSunPitch = sldPitchRes.Slider;

		var sldYawRes = AddSlider(vSun, TranslationServer.Translate("Sun Yaw (Deg):"), -180.0f, 180.0f, 1.0f, 29.0f, (v) => { if (_activePreset != null) _activePreset.SunYaw = v; ApplyLivePreview(); }, "0.0");
		_sldSunYaw = sldYawRes.Slider;

		var sldSunEnergyRes = AddSlider(vSun, TranslationServer.Translate("Sun Energy:"), 0.0f, 10.0f, 0.05f, 2.50f, (v) => { if (_activePreset != null) _activePreset.SunEnergy = v; ApplyLivePreview(); });
		_sldSunEnergy = sldSunEnergyRes.Slider;

		var cpSunRes = AddColorPicker(vSun, TranslationServer.Translate("Sun Light Color:"), Colors.White, (c) => { if (_activePreset != null) _activePreset.SunColorHex = "#" + c.ToHtml(false); ApplyLivePreview(); });
		_cpSunColor = cpSunRes.Picker;

		var sldBiasRes = AddSlider(vSun, TranslationServer.Translate("Shadow Bias:"), 0.0f, 0.2f, 0.005f, 0.03f, (v) => { if (_activePreset != null) _activePreset.ShadowBias = v; ApplyLivePreview(); }, "0.000");
		_sldShadowBias = sldBiasRes.Slider;

		var sldNormBiasRes = AddSlider(vSun, TranslationServer.Translate("Shadow Normal Bias:"), 0.0f, 5.0f, 0.05f, 1.2f, (v) => { if (_activePreset != null) _activePreset.ShadowNormalBias = v; ApplyLivePreview(); });
		_sldShadowNormalBias = sldNormBiasRes.Slider;

		// Tab 2: Ambient & Character Fill
		var tabAmbient = new ScrollContainer();
		tabAmbient.Name = TranslationServer.Translate("Ambient & Fill");
		var vAmbient = new VBoxContainer();
		vAmbient.AddThemeConstantOverride("separation", 6);
		vAmbient.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
		tabAmbient.AddChild(vAmbient);
		tabContainer.AddChild(tabAmbient);

		AddSectionHeader(vAmbient, TranslationServer.Translate("Ambient Sky Lighting"));
		var sldAmbEnergyRes = AddSlider(vAmbient, TranslationServer.Translate("Ambient Energy:"), 0.0f, 5.0f, 0.05f, 0.80f, (v) => { if (_activePreset != null) _activePreset.AmbientEnergy = v; ApplyLivePreview(); });
		_sldAmbientEnergy = sldAmbEnergyRes.Slider;

		var cpAmbRes = AddColorPicker(vAmbient, TranslationServer.Translate("Ambient Color:"), Colors.Gray, (c) => { if (_activePreset != null) _activePreset.AmbientColorHex = "#" + c.ToHtml(false); ApplyLivePreview(); });
		_cpAmbientColor = cpAmbRes.Picker;

		AddSectionHeader(vAmbient, TranslationServer.Translate("Character Readability Fill Light"));
		var sldCharFillRes = AddSlider(vAmbient, TranslationServer.Translate("Fill Light Energy:"), 0.0f, 2.0f, 0.02f, 0.15f, (v) => { if (_activePreset != null) _activePreset.CharacterFillEnergy = v; ApplyLivePreview(); });
		_sldCharacterFill = sldCharFillRes.Slider;

		// Tab 3: Atmospheric Fog
		var tabFog = new ScrollContainer();
		tabFog.Name = TranslationServer.Translate("Atmospheric Fog");
		var vFog = new VBoxContainer();
		vFog.AddThemeConstantOverride("separation", 6);
		vFog.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
		tabFog.AddChild(vFog);
		tabContainer.AddChild(tabFog);

		AddSectionHeader(vFog, TranslationServer.Translate("Volumetric Fog & Distance"));
		_chkFogEnabled = AddCheckBox(vFog, TranslationServer.Translate("Enable Atmospheric Fog"), true, (val) => { if (_activePreset != null) _activePreset.FogEnabled = val; ApplyLivePreview(); });

		var sldFogDensRes = AddSlider(vFog, TranslationServer.Translate("Fog Density:"), 0.0f, 0.1f, 0.0005f, 0.0080f, (v) => { if (_activePreset != null) _activePreset.FogDensity = v; ApplyLivePreview(); }, "0.0000");
		_sldFogDensity = sldFogDensRes.Slider;

		var cpFogRes = AddColorPicker(vFog, TranslationServer.Translate("Fog Light Color:"), Colors.LightGray, (c) => { if (_activePreset != null) _activePreset.FogColorHex = "#" + c.ToHtml(false); ApplyLivePreview(); });
		_cpFogColor = cpFogRes.Picker;

		var sldBaseFogRes = AddSlider(vFog, TranslationServer.Translate("Base Fog Density:"), 0.0f, 0.1f, 0.001f, 0.0f, (v) => { if (_activePreset != null) _activePreset.BaseFogDensity = v; ApplyLivePreview(); }, "0.000");
		_sldBaseFogDensity = sldBaseFogRes.Slider;

		// Tab 4: Post-Processing & SSAO
		var tabPost = new ScrollContainer();
		tabPost.Name = TranslationServer.Translate("Post-Processing & SSAO");
		var vPost = new VBoxContainer();
		vPost.AddThemeConstantOverride("separation", 6);
		vPost.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
		tabPost.AddChild(vPost);
		tabContainer.AddChild(tabPost);

		AddSectionHeader(vPost, TranslationServer.Translate("Screen Space Ambient Occlusion (SSAO)"));
		_chkSsaoEnabled = AddCheckBox(vPost, TranslationServer.Translate("Enable SSAO"), true, (val) => { if (_activePreset != null) _activePreset.SsaoEnabled = val; ApplyLivePreview(); });

		var sldSsaoRadRes = AddSlider(vPost, TranslationServer.Translate("SSAO Radius:"), 0.1f, 5.0f, 0.05f, 1.20f, (v) => { if (_activePreset != null) _activePreset.SsaoRadius = v; ApplyLivePreview(); });
		_sldSsaoRadius = sldSsaoRadRes.Slider;

		var sldSsaoIntRes = AddSlider(vPost, TranslationServer.Translate("SSAO Intensity:"), 0.0f, 5.0f, 0.05f, 0.40f, (v) => { if (_activePreset != null) _activePreset.SsaoIntensity = v; ApplyLivePreview(); });
		_sldSsaoIntensity = sldSsaoIntRes.Slider;

		var sldSsaoDetRes = AddSlider(vPost, TranslationServer.Translate("SSAO Detail:"), 0.0f, 1.0f, 0.05f, 0.50f, (v) => { if (_activePreset != null) _activePreset.SsaoDetail = v; ApplyLivePreview(); });
		_sldSsaoDetail = sldSsaoDetRes.Slider;

		AddSectionHeader(vPost, TranslationServer.Translate("Color Grading & Bloom"));
		var sldExpRes = AddSlider(vPost, TranslationServer.Translate("Tonemap Exposure:"), 0.1f, 3.0f, 0.02f, 1.18f, (v) => { if (_activePreset != null) _activePreset.TonemapExposure = v; ApplyLivePreview(); });
		_sldExposure = sldExpRes.Slider;

		var sldContRes = AddSlider(vPost, TranslationServer.Translate("Contrast:"), 0.5f, 2.0f, 0.02f, 1.02f, (v) => { if (_activePreset != null) _activePreset.AdjustmentContrast = v; ApplyLivePreview(); });
		_sldContrast = sldContRes.Slider;

		var sldSatRes = AddSlider(vPost, TranslationServer.Translate("Saturation:"), 0.0f, 2.0f, 0.02f, 1.06f, (v) => { if (_activePreset != null) _activePreset.AdjustmentSaturation = v; ApplyLivePreview(); });
		_sldSaturation = sldSatRes.Slider;

		var sldGlowIntRes = AddSlider(vPost, TranslationServer.Translate("Glow Intensity:"), 0.0f, 2.0f, 0.05f, 0.15f, (v) => { if (_activePreset != null) _activePreset.GlowIntensity = v; ApplyLivePreview(); });
		_sldGlowIntensity = sldGlowIntRes.Slider;

		var sldGlowBloomRes = AddSlider(vPost, TranslationServer.Translate("Glow Bloom:"), 0.0f, 1.0f, 0.02f, 0.14f, (v) => { if (_activePreset != null) _activePreset.GlowBloom = v; ApplyLivePreview(); });
		_sldGlowBloom = sldGlowBloomRes.Slider;

		var sldGlowStrRes = AddSlider(vPost, TranslationServer.Translate("Glow Strength:"), 0.0f, 2.0f, 0.05f, 0.90f, (v) => { if (_activePreset != null) _activePreset.GlowStrength = v; ApplyLivePreview(); });
		_sldGlowStrength = sldGlowStrRes.Slider;

		// Tab 5: Weather Effects
		var tabWeather = new ScrollContainer();
		tabWeather.Name = TranslationServer.Translate("Weather & Precipitation");
		var vWeather = new VBoxContainer();
		vWeather.AddThemeConstantOverride("separation", 6);
		vWeather.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
		tabWeather.AddChild(vWeather);
		tabContainer.AddChild(tabWeather);

		AddSectionHeader(vWeather, TranslationServer.Translate("Precipitation & VFX"));
		var weatherRow = new HBoxContainer();
		weatherRow.AddChild(new Label { Text = TranslationServer.Translate("Weather Condition:"), CustomMinimumSize = new Vector2(130, 0) });
		_optWeatherType = new OptionButton();
		_optWeatherType.AddItem(TranslationServer.Translate("Clear"), 0);
		_optWeatherType.AddItem(TranslationServer.Translate("Rain Shower"), 1);
		_optWeatherType.AddItem(TranslationServer.Translate("Dense Fog"), 2);
		_optWeatherType.AddItem(TranslationServer.Translate("Snow"), 3);
		_optWeatherType.ItemSelected += (idx) =>
		{
			if (_activePreset != null)
			{
				_activePreset.WeatherType = idx switch
				{
					1 => "rain",
					2 => "fog",
					3 => "snow",
					_ => "clear"
				};
				ApplyLivePreview();
			}
		};
		weatherRow.AddChild(_optWeatherType);
		vWeather.AddChild(weatherRow);

		var sldRainRes = AddSlider(vWeather, TranslationServer.Translate("Particle Density:"), 0.0f, 3000.0f, 50.0f, 0.0f, (v) => { if (_activePreset != null) _activePreset.RainParticleDensity = (int)v; ApplyLivePreview(); }, "0");
		_sldRainDensity = sldRainRes.Slider;

		AddSectionHeader(vWeather, TranslationServer.Translate("In-Game Notification Banner"));
		var bannerRow = new HBoxContainer();
		bannerRow.AddChild(new Label { Text = TranslationServer.Translate("Forecast Text:"), CustomMinimumSize = new Vector2(130, 0) });
		_txtAnnouncement = new LineEdit { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
		_txtAnnouncement.TextChanged += (t) => { if (_activePreset != null) _activePreset.WeatherAnnouncement = t; };
		bannerRow.AddChild(_txtAnnouncement);
		vWeather.AddChild(bannerRow);
	}

	public void OpenWithPresets(List<EnvironmentPresetConfig> presets, string? initialPresetId = null, string? defaultPresetId = null)
	{
		_presets.Clear();
		_initialSnapshots.Clear();
		foreach (var p in presets)
		{
			_presets.Add(p.Clone());
			_initialSnapshots.Add(p.Clone());
		}

		if (_presets.Count == 0)
		{
			_presets = EnvironmentPresetConfig.CreateDefaultPresets();
			foreach (var p in _presets) _initialSnapshots.Add(p.Clone());
		}

		_defaultPresetId = !string.IsNullOrWhiteSpace(defaultPresetId) ? defaultPresetId : (_presets.Count > 0 ? _presets[0].Id : "day");

		UpdatePresetListUI();

		int selIdx = 0;
		if (!string.IsNullOrWhiteSpace(initialPresetId))
		{
			selIdx = _presets.FindIndex(p => string.Equals(p.Id, initialPresetId, StringComparison.OrdinalIgnoreCase));
			if (selIdx < 0) selIdx = 0;
		}

		SelectPreset(selIdx);
		OpenDialog();
	}

	private void UpdatePresetListUI()
	{
		_optPresetSelect.Clear();
		for (int i = 0; i < _presets.Count; i++)
		{
			string prefix = string.Equals(_presets[i].Id, _defaultPresetId, StringComparison.OrdinalIgnoreCase) ? "★ " : "";
			_optPresetSelect.AddItem($"{prefix}{_presets[i].Name} ({_presets[i].Id})", i);
		}
		if (_selectedPresetIdx >= 0 && _selectedPresetIdx < _presets.Count)
		{
			_optPresetSelect.Selected = _selectedPresetIdx;
		}
	}

	private void SelectPreset(int idx)
	{
		if (idx < 0 || idx >= _presets.Count) return;
		_selectedPresetIdx = idx;
		_activePreset = _presets[idx];
		PopulateFormFromActivePreset();
		ApplyLivePreview();
	}

	private void PopulateFormFromActivePreset()
	{
		if (_activePreset == null) return;
		_txtName.Text = _activePreset.Name;
		_txtId.Text = _activePreset.Id;

		bool isDefault = string.Equals(_activePreset.Id, _defaultPresetId, StringComparison.OrdinalIgnoreCase);
		_lblDefaultBadge.Text = isDefault ? "★ " + TranslationServer.Translate("Default Map Preset") : "";

		_sldSunPitch.Value = _activePreset.SunPitch;
		_sldSunYaw.Value = _activePreset.SunYaw;
		_sldSunEnergy.Value = _activePreset.SunEnergy;
		_cpSunColor.Color = _activePreset.GetSunColor();
		_sldShadowBias.Value = _activePreset.ShadowBias;
		_sldShadowNormalBias.Value = _activePreset.ShadowNormalBias;

		_sldAmbientEnergy.Value = _activePreset.AmbientEnergy;
		_cpAmbientColor.Color = _activePreset.GetAmbientColor();
		_sldCharacterFill.Value = _activePreset.CharacterFillEnergy;

		_chkFogEnabled.ButtonPressed = _activePreset.FogEnabled;
		_sldFogDensity.Value = _activePreset.FogDensity;
		_cpFogColor.Color = _activePreset.GetFogColor();
		_sldBaseFogDensity.Value = _activePreset.BaseFogDensity;

		_chkSsaoEnabled.ButtonPressed = _activePreset.SsaoEnabled;
		_sldSsaoRadius.Value = _activePreset.SsaoRadius;
		_sldSsaoIntensity.Value = _activePreset.SsaoIntensity;
		_sldSsaoDetail.Value = _activePreset.SsaoDetail;

		_sldExposure.Value = _activePreset.TonemapExposure;
		_sldContrast.Value = _activePreset.AdjustmentContrast;
		_sldSaturation.Value = _activePreset.AdjustmentSaturation;
		_sldGlowIntensity.Value = _activePreset.GlowIntensity;
		_sldGlowBloom.Value = _activePreset.GlowBloom;
		_sldGlowStrength.Value = _activePreset.GlowStrength;

		_optWeatherType.Selected = _activePreset.WeatherType?.ToLowerInvariant() switch
		{
			"rain" => 1,
			"fog" => 2,
			"snow" => 3,
			_ => 0
		};
		_sldRainDensity.Value = _activePreset.RainParticleDensity;
		_txtAnnouncement.Text = _activePreset.WeatherAnnouncement ?? "";
	}

	private void OnAddPresetClicked()
	{
		int nextIndex = _presets.Count + 1;
		var newPreset = new EnvironmentPresetConfig
		{
			Id = $"preset_{nextIndex}",
			Name = $"Custom Preset {nextIndex}",
			SunPitch = -55.0f,
			SunYaw = 30.0f,
			SunEnergy = 2.0f,
			AmbientEnergy = 0.8f,
			WeatherType = "clear"
		};
		_presets.Add(newPreset);
		UpdatePresetListUI();
		SelectPreset(_presets.Count - 1);
	}

	private void OnDuplicatePresetClicked()
	{
		if (_activePreset == null) return;
		var dup = _activePreset.Clone();
		dup.Id = $"{_activePreset.Id}_copy";
		dup.Name = $"{_activePreset.Name} (Copy)";
		_presets.Add(dup);
		UpdatePresetListUI();
		SelectPreset(_presets.Count - 1);
	}

	private void OnDeletePresetClicked()
	{
		if (_presets.Count <= 1) return;
		_presets.RemoveAt(_selectedPresetIdx);
		_selectedPresetIdx = Math.Clamp(_selectedPresetIdx, 0, _presets.Count - 1);
		UpdatePresetListUI();
		SelectPreset(_selectedPresetIdx);
	}

	private void OnSetDefaultClicked()
	{
		if (_activePreset == null) return;
		_defaultPresetId = _activePreset.Id;
		UpdatePresetListUI();
		_lblDefaultBadge.Text = "★ " + TranslationServer.Translate("Default Map Preset");
	}

	private void OnResetDefaultsClicked()
	{
		_presets = EnvironmentPresetConfig.CreateDefaultPresets();
		_defaultPresetId = "day";
		UpdatePresetListUI();
		SelectPreset(0);
	}

	private void ApplyLivePreview()
	{
		if (_activePreset == null || _chkLivePreview == null || !_chkLivePreview.ButtonPressed) return;
		if (GameHost.Instance != null && GameHost.Instance.EnvironmentService != null)
		{
			GameHost.Instance.EnvironmentService.ApplyPreset(GameHost.Instance, _activePreset);
		}
	}

	protected override void OnApply()
	{
		if (GameHost.Instance != null && GameHost.Instance.EnvironmentService != null)
		{
			GameHost.Instance.EnvironmentService.LoadPresets(_presets);
			if (!string.IsNullOrWhiteSpace(_defaultPresetId))
			{
				GameHost.Instance.EnvironmentService.ApplyPresetById(GameHost.Instance, _defaultPresetId);
			}
			else if (_activePreset != null)
			{
				GameHost.Instance.EnvironmentService.ApplyPreset(GameHost.Instance, _activePreset);
			}
			GameHost.Instance.EditorHasUnsavedChanges = true;
		}

		string wsPath = MapWorkspaceService.GetActiveWorkspacePath();
		string metaPath = Path.Combine(wsPath, "metadata.json");
		if (File.Exists(metaPath))
		{
			if (MetadataService.Instance.TryLoadMetadata(wsPath, out var metadataRoot) && metadataRoot != null)
			{
				metadataRoot.CustomEnvironmentPresets = _presets;
				metadataRoot.DefaultEnvironmentPreset = _defaultPresetId;
				MetadataService.Instance.SaveMetadata(metaPath, metadataRoot);
			}
		}
	}

	protected override void OnCancel()
	{
		if (GameHost.Instance != null && GameHost.Instance.EnvironmentService != null)
		{
			GameHost.Instance.EnvironmentService.LoadPresets(_initialSnapshots);
			var initial = _initialSnapshots.Count > 0 ? _initialSnapshots[0] : null;
			if (initial != null)
			{
				GameHost.Instance.EnvironmentService.ApplyPreset(GameHost.Instance, initial);
			}
		}
	}
}
