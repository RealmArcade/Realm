using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json.Nodes;
using Godot;
using Realm.Godot.Services;
using Realm.Godot.Utils;
using Realm.Godot.VFX;

public partial class ProceduralAnimationStudioDialog : FloatingPreview3DDialogBase
{
	public class TemplateItemInfo
	{
		public string Id { get; set; } = "";
		public string DisplayName { get; set; } = "";
		public string Category { get; set; } = "";
		public string ModelPath { get; set; } = "";
		public string VisualMode { get; set; } = "Mesh";
		public string FormattedLabel { get; set; } = "";
	}

	private Node3D _simRoot;
	private Node3D _currentModelRoot;

	private LineEdit _txtAnimId;
	private LineEdit _txtAnimName;
	private OptionButton _optPreset;
	private OptionButton _optTemplatePicker;
	private OptionButton _optMotionType;
	private OptionButton _optMaskMode;

	private HSlider _sldMaskMin;
	private Label _lblMaskMin;
	private HSlider _sldMaskMax;
	private Label _lblMaskMax;
	private HSlider _sldMaskPower;
	private Label _lblMaskPower;
	private CheckBox _chkMaskInvert;
	private CheckBox _chkShowHeatmap;

	private HSlider _sldSwayFrequency;
	private Label _lblSwayFrequency;
	private HSlider _sldSwayAmplitude;
	private Label _lblSwayAmplitude;
	private HSlider _sldFlutterFrequency;
	private Label _lblFlutterFrequency;
	private HSlider _sldFlutterAmplitude;
	private Label _lblFlutterAmplitude;
	private HSlider _sldWindInfluence;
	private Label _lblWindInfluence;
	private HSlider _sldVelocityDragInfluence;
	private Label _lblVelocityDragInfluence;
	private HSlider _sldWaveTurbulence;
	private Label _lblWaveTurbulence;

	private HSlider _sldImpulseAmplitude;
	private Label _lblImpulseAmplitude;
	private HSlider _sldImpulseFrequency;
	private Label _lblImpulseFrequency;
	private HSlider _sldImpulseDecay;
	private Label _lblImpulseDecay;
	private HSlider _sldImpulseDuration;
	private Label _lblImpulseDuration;

	private HSlider _sldSimVelocity;
	private Label _lblSimVelocity;

	private ProceduralAnimationConfig _config = new();
	private ProceduralAnimationConfig _snapshot = new();
	private string _selectedTemplateKey = "";
	private List<TemplateItemInfo> _availableTemplates = new();

	private float _impulseStrength = 0.0f;
	private float _impulseTime = 0.0f;
	private float _simVelocityX = 0.0f;

	private Action<ProceduralAnimationConfig> _onSaved;

	public ProceduralAnimationStudioDialog(MapEditorHUD hud)
		: base(hud, TranslationServer.Translate("Procedural Animation & Sway Studio"), new Vector2(560, 780))
	{
		BuildControls();
	}

	private void BuildControls()
	{
		var previewContainer = new PanelContainer();
		previewContainer.CustomMinimumSize = new Vector2(0, 220);
		previewContainer.AddThemeStyleboxOverride("panel", UIStyle.CreateLightInnerPanel());
		BodyContainer.AddChild(previewContainer);

		Add3DPreviewViewport(previewContainer, new Vector2(0, 220));

		Setup3DEnvironment();

		AddCameraPresetToolbar(BodyContainer, includeBack: true, includeLightingToggle: true);

		var templateRow = new HBoxContainer();
		templateRow.AddThemeConstantOverride("separation", 6);

		var lblTemplate = new Label();
		lblTemplate.Text = TranslationServer.Translate("Template:");
		lblTemplate.AddThemeFontSizeOverride("font_size", 11);
		lblTemplate.AddThemeColorOverride("font_color", UIStyle.ColorGold);
		templateRow.AddChild(lblTemplate);

		_optTemplatePicker = new OptionButton();
		_optTemplatePicker.AddThemeFontSizeOverride("font_size", 11);
		_optTemplatePicker.CustomMinimumSize = new Vector2(160, 24);
		_optTemplatePicker.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
		_optTemplatePicker.ItemSelected += (idx) =>
		{
			if (idx >= 0 && idx < _availableTemplates.Count)
			{
				_selectedTemplateKey = _availableTemplates[(int)idx].Id;
				LoadPreviewTemplate(_availableTemplates[(int)idx]);
			}
		};
		templateRow.AddChild(_optTemplatePicker);

		BodyContainer.AddChild(templateRow);

		var playRow = new HBoxContainer();
		playRow.AddThemeConstantOverride("separation", 6);

		AddButton(playRow, "💥 " + TranslationServer.Translate("Trigger Shake Impulse"), () => TriggerTestImpulse(), "Trigger Hit / Harvest Shake Impulse", 10, new Vector2(140, 24));

		_chkShowHeatmap = new CheckBox();
		_chkShowHeatmap.Text = TranslationServer.Translate("Show Heatmap Mask");
		_chkShowHeatmap.ButtonPressed = false;
		_chkShowHeatmap.AddThemeFontSizeOverride("font_size", 10);
		_chkShowHeatmap.Toggled += (pressed) => UpdateShaderParameters();
		playRow.AddChild(_chkShowHeatmap);

		var lblVel = new Label();
		lblVel.Text = TranslationServer.Translate("Sim Speed:");
		lblVel.AddThemeFontSizeOverride("font_size", 10);
		lblVel.AddThemeColorOverride("font_color", UIStyle.ColorGoldDull);
		playRow.AddChild(lblVel);

		_sldSimVelocity = new HSlider();
		_sldSimVelocity.MinValue = 0.0;
		_sldSimVelocity.MaxValue = 10.0;
		_sldSimVelocity.Step = 0.2;
		_sldSimVelocity.Value = 0.0;
		_sldSimVelocity.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
		_sldSimVelocity.ValueChanged += (val) =>
		{
			_simVelocityX = (float)val;
			if (_lblSimVelocity != null) _lblSimVelocity.Text = $"{val:F1} m/s";
			UpdateShaderParameters();
		};
		playRow.AddChild(_sldSimVelocity);

		_lblSimVelocity = new Label();
		_lblSimVelocity.Text = "0.0 m/s";
		_lblSimVelocity.CustomMinimumSize = new Vector2(40, 0);
		_lblSimVelocity.AddThemeFontSizeOverride("font_size", 10);
		playRow.AddChild(_lblSimVelocity);

		BodyContainer.AddChild(playRow);

		var scroll = new ScrollContainer();
		scroll.SizeFlagsVertical = Control.SizeFlags.ExpandFill;
		scroll.HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled;
		scroll.VerticalScrollMode = ScrollContainer.ScrollMode.Auto;

		var configVBox = new VBoxContainer();
		configVBox.AddThemeConstantOverride("separation", 6);
		configVBox.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
		scroll.AddChild(configVBox);
		BodyContainer.AddChild(scroll);

		var presetRow = new HBoxContainer();
		presetRow.AddThemeConstantOverride("separation", 6);

		var lblPreset = new Label();
		lblPreset.Text = TranslationServer.Translate("Template Preset:");
		lblPreset.AddThemeFontSizeOverride("font_size", 11);
		lblPreset.AddThemeColorOverride("font_color", UIStyle.ColorGold);
		presetRow.AddChild(lblPreset);

		_optPreset = new OptionButton();
		_optPreset.AddThemeFontSizeOverride("font_size", 11);
		_optPreset.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
		PopulatePresetList();
		_optPreset.ItemSelected += (idx) =>
		{
			if (idx < 0 || idx >= _optPreset.ItemCount) return;
			string key = _optPreset.GetItemMetadata((int)idx).AsString();
			var def = ProceduralAnimationManager.GetConfig(key);
			if (def != null)
			{
				string oldId = _config.Id;
				_config = def.Clone();
				_config.Id = oldId;
				SyncControlsFromConfig();
				UpdateShaderParameters();
			}
		};
		presetRow.AddChild(_optPreset);
		configVBox.AddChild(presetRow);

		_txtAnimId = AddTextInput(configVBox, TranslationServer.Translate("Profile ID:"), _config.Id, (val) =>
		{
			_config.Id = val.Trim().ToLowerInvariant().Replace(" ", "_");
		}, "", 150f);

		_txtAnimName = AddTextInput(configVBox, TranslationServer.Translate("Display Name:"), _config.Name, (val) =>
		{
			_config.Name = val;
		}, "", 150f);

		AddButton(configVBox, "🎲 " + TranslationServer.Translate("Randomize Sway & Flutter"), () => RandomizeParameters(), "Randomize motion parameters", 11, new Vector2(0, 26));

		string[] motionTypes = new[]
		{
			TranslationServer.Translate("Cloth / Cape Sway").ToString(),
			TranslationServer.Translate("Foliage / Tree Wind").ToString(),
			TranslationServer.Translate("Creature Wing Flap").ToString(),
			TranslationServer.Translate("Transient Hit Shake").ToString()
		};

		_optMotionType = AddOptionDropdown(configVBox, TranslationServer.Translate("Motion Dynamics:"), motionTypes, (int)_config.MotionType - 1, (idx) =>
		{
			_config.MotionType = (ProceduralMotionType)(idx + 1);
			UpdateShaderParameters();
		}, 150f);

		AddSectionHeader(configVBox, "📐 " + TranslationServer.Translate("SPATIAL MESH DETECTION MASK"), new Color(0.85f, 0.65f, 0.35f));

		string[] maskModes = new[]
		{
			TranslationServer.Translate("0: Height Gradient (Y-Axis)").ToString(),
			TranslationServer.Translate("1: Radial Distance (Center Axis)").ToString(),
			TranslationServer.Translate("2: Normal Incline (Facing Direction)").ToString()
		};

		_optMaskMode = AddOptionDropdown(configVBox, TranslationServer.Translate("Mask Method:"), maskModes, (int)_config.MaskMode, (idx) =>
		{
			_config.MaskMode = (SpatialMaskMode)idx;
			UpdateShaderParameters();
		}, 150f);

		(_sldMaskMin, _lblMaskMin) = AddSlider(configVBox, TranslationServer.Translate("Mask Lower Bound:"), -5.0f, 10.0f, 0.05f, _config.MaskMin, (val) =>
		{
			_config.MaskMin = val;
			UpdateShaderParameters();
		}, "0.00", 150f);

		(_sldMaskMax, _lblMaskMax) = AddSlider(configVBox, TranslationServer.Translate("Mask Upper Bound:"), -5.0f, 15.0f, 0.05f, _config.MaskMax, (val) =>
		{
			_config.MaskMax = val;
			UpdateShaderParameters();
		}, "0.00", 150f);

		(_sldMaskPower, _lblMaskPower) = AddSlider(configVBox, TranslationServer.Translate("Falloff Curve (Power):"), 0.1f, 5.0f, 0.1f, _config.MaskPower, (val) =>
		{
			_config.MaskPower = val;
			UpdateShaderParameters();
		}, "0.0", 150f);

		_chkMaskInvert = AddCheckBox(configVBox, TranslationServer.Translate("Invert Mask Weight (Bottom / Center Heaviest)"), _config.MaskInvert, (pressed) =>
		{
			_config.MaskInvert = pressed;
			UpdateShaderParameters();
		});

		AddSectionHeader(configVBox, "🌊 " + TranslationServer.Translate("SWAY & WIND MOTION DYNAMICS"), new Color(0.35f, 0.75f, 0.85f));

		(_sldSwayFrequency, _lblSwayFrequency) = AddSlider(configVBox, TranslationServer.Translate("Sway Frequency (Hz):"), 0.0f, 10.0f, 0.1f, _config.SwayFrequency, (val) =>
		{
			_config.SwayFrequency = val;
			UpdateShaderParameters();
		}, "0.0 Hz", 150f);

		(_sldSwayAmplitude, _lblSwayAmplitude) = AddSlider(configVBox, TranslationServer.Translate("Sway Displacement (m):"), 0.0f, 1.0f, 0.02f, _config.SwayAmplitude, (val) =>
		{
			_config.SwayAmplitude = val;
			UpdateShaderParameters();
		}, "0.00 m", 150f);

		(_sldFlutterFrequency, _lblFlutterFrequency) = AddSlider(configVBox, TranslationServer.Translate("Flutter Frequency (Hz):"), 0.0f, 20.0f, 0.2f, _config.FlutterFrequency, (val) =>
		{
			_config.FlutterFrequency = val;
			UpdateShaderParameters();
		}, "0.0 Hz", 150f);

		(_sldFlutterAmplitude, _lblFlutterAmplitude) = AddSlider(configVBox, TranslationServer.Translate("Flutter Displacement (m):"), 0.0f, 0.2f, 0.005f, _config.FlutterAmplitude, (val) =>
		{
			_config.FlutterAmplitude = val;
			UpdateShaderParameters();
		}, "0.000 m", 150f);

		(_sldWindInfluence, _lblWindInfluence) = AddSlider(configVBox, TranslationServer.Translate("Wind Response Multiplier:"), 0.0f, 3.0f, 0.05f, _config.WindInfluence, (val) =>
		{
			_config.WindInfluence = val;
			UpdateShaderParameters();
		}, "0.00", 150f);

		(_sldVelocityDragInfluence, _lblVelocityDragInfluence) = AddSlider(configVBox, TranslationServer.Translate("Velocity Drag Lag:"), 0.0f, 3.0f, 0.05f, _config.VelocityDragInfluence, (val) =>
		{
			_config.VelocityDragInfluence = val;
			UpdateShaderParameters();
		}, "0.00", 150f);

		(_sldWaveTurbulence, _lblWaveTurbulence) = AddSlider(configVBox, TranslationServer.Translate("Wave Turbulence:"), 0.0f, 2.0f, 0.05f, _config.WaveTurbulence, (val) =>
		{
			_config.WaveTurbulence = val;
			UpdateShaderParameters();
		}, "0.00", 150f);

		AddSectionHeader(configVBox, "⚡ " + TranslationServer.Translate("TRANSIENT HIT / HARVEST SHAKE"), new Color(0.95f, 0.45f, 0.25f));

		(_sldImpulseAmplitude, _lblImpulseAmplitude) = AddSlider(configVBox, TranslationServer.Translate("Impact Shake Amplitude (m):"), 0.0f, 1.0f, 0.02f, _config.ImpulseAmplitude, (val) =>
		{
			_config.ImpulseAmplitude = val;
			UpdateShaderParameters();
		}, "0.00 m", 150f);

		(_sldImpulseFrequency, _lblImpulseFrequency) = AddSlider(configVBox, TranslationServer.Translate("Shake Oscillation (Hz):"), 1.0f, 30.0f, 0.5f, _config.ImpulseFrequency, (val) =>
		{
			_config.ImpulseFrequency = val;
			UpdateShaderParameters();
		}, "0.0 Hz", 150f);

		(_sldImpulseDecay, _lblImpulseDecay) = AddSlider(configVBox, TranslationServer.Translate("Damping Decay Rate:"), 0.5f, 10.0f, 0.2f, _config.ImpulseDecay, (val) =>
		{
			_config.ImpulseDecay = val;
			UpdateShaderParameters();
		}, "0.0", 150f);

		(_sldImpulseDuration, _lblImpulseDuration) = AddSlider(configVBox, TranslationServer.Translate("Impulse Duration (s):"), 0.1f, 2.0f, 0.05f, _config.ImpulseDuration, (val) =>
		{
			_config.ImpulseDuration = val;
		}, "0.00 s", 150f);
	}

	public override void _Process(double delta)
	{
		base._Process(delta);

		if (_impulseStrength > 0.001f && Visible)
		{
			_impulseTime += (float)delta;
			float dur = _config.ImpulseDuration > 0.05f ? _config.ImpulseDuration : 0.6f;
			if (_impulseTime >= dur)
			{
				_impulseStrength = 0.0f;
				_impulseTime = 0.0f;
			}
			UpdateShaderParameters();
		}
	}

	private void TriggerTestImpulse()
	{
		_impulseStrength = _config.ImpulseAmplitude > 0f ? _config.ImpulseAmplitude : 0.35f;
		_impulseTime = 0.0f;
		UpdateShaderParameters();
	}

	private void PopulatePresetList()
	{
		if (_optPreset == null) return;
		_optPreset.Clear();
		int pIdx = 0;
		foreach (var kvp in ProceduralAnimationManager.LoadAllConfigs())
		{
			_optPreset.AddItem(kvp.Value.Name, pIdx);
			_optPreset.SetItemMetadata(pIdx, kvp.Key);
			pIdx++;
		}
	}

	public void OpenForConfig(ProceduralAnimationConfig config, Action<ProceduralAnimationConfig> onSaved = null, string previewModelKey = null)
	{
		_onSaved = onSaved;
		if (!string.IsNullOrWhiteSpace(previewModelKey))
			_selectedTemplateKey = previewModelKey;

		PopulatePresetList();
		PopulateTemplateList();
		InitializeConfig(config);

		_snapshot = _config.Clone();
		SyncControlsFromConfig();
		SelectPresetOption();
		SelectInitialTemplate();

		OpenDialog();
	}

	private void InitializeConfig(ProceduralAnimationConfig config)
	{
		if (config != null && !string.IsNullOrWhiteSpace(config.Id))
		{
			var existing = ProceduralAnimationManager.GetConfig(config.Id);
			_config = existing != null ? existing.Clone() : config.Clone();
			return;
		}
		_config = new ProceduralAnimationConfig
		{
			Id = "new_procedural_anim",
			Name = "New Procedural Animation"
		};
	}

	private void SelectPresetOption()
	{
		if (_optPreset == null || string.IsNullOrEmpty(_config.Id)) return;
		for (int i = 0; i < _optPreset.ItemCount; i++)
		{
			string metaKey = _optPreset.GetItemMetadata(i).AsString();
			if (string.Equals(metaKey, _config.Id, StringComparison.OrdinalIgnoreCase))
			{
				_optPreset.Selected = i;
				return;
			}
		}
	}

	private void SelectInitialTemplate()
	{
		if (_optTemplatePicker != null && _optTemplatePicker.Selected >= 0 && _optTemplatePicker.Selected < _availableTemplates.Count)
		{
			LoadPreviewTemplate(_availableTemplates[_optTemplatePicker.Selected]);
			return;
		}
		if (_availableTemplates.Count > 0)
		{
			LoadPreviewTemplate(_availableTemplates[0]);
		}
	}

	private void PopulateTemplateList()
	{
		_availableTemplates.Clear();
		if (_optTemplatePicker == null) return;
		_optTemplatePicker.Clear();

		var existingIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
		string wsPath = MapWorkspaceService.GetActiveWorkspacePath();

		LoadMetadataTemplates(wsPath, existingIds);
		LoadGameHostRegistryTemplates(existingIds);
		LoadMapAssetsTemplates(wsPath, existingIds);
		AddSampleTemplates();

		PopulateTemplatePickerAndSelect();
	}

	private void AddTemplateIfNotExists(string id, string name, string category, string modelPath, string visualMode, HashSet<string> existingIds)
	{
		if (string.IsNullOrEmpty(id) || !existingIds.Add(id)) return;
		
		string actualVisualMode = !string.IsNullOrEmpty(visualMode) ? visualMode : (modelPath?.EndsWith(".rtex", StringComparison.OrdinalIgnoreCase) == true ? "GroundPlane" : "Mesh");
		_availableTemplates.Add(new TemplateItemInfo
		{
			Id = id,
			DisplayName = !string.IsNullOrEmpty(name) ? name : id,
			Category = category,
			ModelPath = modelPath ?? "",
			VisualMode = actualVisualMode
		});
	}

	private void LoadMetadataTemplates(string wsPath, HashSet<string> existingIds)
	{
		if (!MetadataService.Instance.TryLoadMetadata(wsPath, out var meta) || meta?.Templates == null) return;
		
		AddMetadataTemplates(meta.Templates.Units, "Unit", existingIds, u => u.TemplateID, u => u.Name, u => u.ModelPath, u => u.VisualMode);
		AddMetadataTemplates(meta.Templates.Buildings, "Building", existingIds, b => b.TemplateID, b => b.Name, b => b.ModelPath, b => b.VisualMode);
		AddMetadataTemplates(meta.Templates.Resources, "Resource", existingIds, r => r.TemplateID, r => r.Name, r => r.ModelPath, r => r.VisualMode);
		AddMetadataTemplates(meta.Templates.Props, "Prop", existingIds, p => p.TemplateID, p => p.Name, p => p.ModelPath, p => p.VisualMode);
	}

	private void AddMetadataTemplates<T>(IEnumerable<T> items, string category, HashSet<string> existingIds, Func<T, string> getId, Func<T, string> getName, Func<T, string> getModelPath, Func<T, string> getVisualMode)
	{
		if (items == null) return;
		
		foreach (var item in items)
			AddTemplateIfNotExists(getId(item), getName(item), category, getModelPath(item), getVisualMode(item), existingIds);
	}

	private void LoadGameHostRegistryTemplates(HashSet<string> existingIds)
	{
		if (GameHost.UnitRegistry != null)
			foreach (var kvp in GameHost.UnitRegistry)
				AddTemplateIfNotExists(kvp.Key.ToString(), kvp.Value.Name, "Unit", kvp.Value.ModelPath, kvp.Value.VisualMode, existingIds);

		if (GameHost.BuildingRegistry != null)
			foreach (var kvp in GameHost.BuildingRegistry)
				AddTemplateIfNotExists(kvp.Key.ToString(), kvp.Value.Name, "Building", kvp.Value.ModelPath, kvp.Value.VisualMode, existingIds);

		if (GameHost.ResourceRegistry != null)
			foreach (var kvp in GameHost.ResourceRegistry)
				AddTemplateIfNotExists(kvp.Key.ToString(), kvp.Value.Name, "Resource", kvp.Value.ModelPath, kvp.Value.VisualMode, existingIds);

		if (GameHost.PropRegistry != null)
			foreach (var kvp in GameHost.PropRegistry)
				AddTemplateIfNotExists(kvp.Key.ToString(), kvp.Value.Name, "Prop", kvp.Value.ModelPath, kvp.Value.VisualMode, existingIds);
	}

	private void LoadMapAssetsTemplates(string wsPath, HashSet<string> existingIds)
	{
		try
		{
			var assets = MapAssetHelper.LoadAssets(wsPath);
			if (assets == null) return;
			
			foreach (var groupKvp in assets.GetAllCategories())
			{
				ProcessAssetGroup(groupKvp.Key, groupKvp.Value, existingIds);
			}
		}
		catch { }
	}

	private void ProcessAssetGroup(string groupName, Dictionary<string, string> subDict, HashSet<string> existingIds)
	{
		if (subDict == null) return;
		string category = groupName switch
		{
			"Character" or "units" or "characters" => "Unit",
			"Building" or "buildings" => "Building",
			"Resource" or "resources" => "Resource",
			_ => "Prop"
		};

		foreach (var itemKvp in subDict)
		{
			string fn = itemKvp.Key;
			if (string.IsNullOrEmpty(fn) || (!fn.EndsWith(".rmesh", StringComparison.OrdinalIgnoreCase) && !fn.EndsWith(".rtex", StringComparison.OrdinalIgnoreCase))) continue;
			
			string id = Path.GetFileName(fn);
			AddTemplateIfNotExists(id, id, category, fn, null, existingIds);
		}
	}

	private void AddSampleTemplates()
	{
		_availableTemplates.Add(new TemplateItemInfo
		{
			Id = "(Sample Tree Cylinder)",
			DisplayName = "(Sample Tree Cylinder)",
			Category = "Sample",
			ModelPath = "",
			VisualMode = "Mesh"
		});
		_availableTemplates.Add(new TemplateItemInfo
		{
			Id = "(Sample Unit Capsule)",
			DisplayName = "(Sample Unit Capsule)",
			Category = "Sample",
			ModelPath = "",
			VisualMode = "Mesh"
		});
	}

	private void PopulateTemplatePickerAndSelect()
	{
		int selectedIdx = FindSelectedTemplateIndex();

		for (int i = 0; i < _availableTemplates.Count; i++)
		{
			var t = _availableTemplates[i];
			string label = !string.IsNullOrEmpty(t.DisplayName) && !string.Equals(t.DisplayName, t.Id, StringComparison.OrdinalIgnoreCase)
				? $"[{t.Category}] {t.DisplayName} ({t.Id})"
				: $"[{t.Category}] {t.Id}";
			t.FormattedLabel = label;
			_optTemplatePicker.AddItem(label, i);
		}

		if (selectedIdx >= 0 && selectedIdx < _availableTemplates.Count)
		{
			_selectedTemplateKey = _availableTemplates[selectedIdx].Id;
			_optTemplatePicker.Selected = selectedIdx;
		}
		else if (_availableTemplates.Count > 0)
		{
			_selectedTemplateKey = _availableTemplates[0].Id;
			_optTemplatePicker.Selected = 0;
		}
	}

	private int FindSelectedTemplateIndex()
	{
		if (string.IsNullOrWhiteSpace(_selectedTemplateKey)) return -1;

		string targetName = Path.GetFileName(_selectedTemplateKey);
		string targetWithoutExt = Path.GetFileNameWithoutExtension(_selectedTemplateKey);

		int idx = _availableTemplates.FindIndex(t => string.Equals(t.Id, _selectedTemplateKey, StringComparison.OrdinalIgnoreCase));
		if (idx >= 0) return idx;

		idx = _availableTemplates.FindIndex(t => string.Equals(t.ModelPath, _selectedTemplateKey, StringComparison.OrdinalIgnoreCase));
		if (idx >= 0) return idx;

		idx = _availableTemplates.FindIndex(t => string.Equals(Path.GetFileName(t.ModelPath), targetName, StringComparison.OrdinalIgnoreCase));
		if (idx >= 0) return idx;

		idx = _availableTemplates.FindIndex(t => string.Equals(Path.GetFileNameWithoutExtension(t.Id), targetWithoutExt, StringComparison.OrdinalIgnoreCase));
		if (idx >= 0) return idx;

		return _availableTemplates.FindIndex(t => string.Equals(Path.GetFileNameWithoutExtension(t.ModelPath), targetWithoutExt, StringComparison.OrdinalIgnoreCase));
	}

	private void SyncControlsFromConfig()
	{
		UpdateTextControls();
		UpdateMaskControls();
		UpdateSwayAndFlutterControls();
		UpdateImpulseControls();
	}

	private void UpdateTextControls()
	{
		if (_txtAnimId != null) _txtAnimId.Text = _config.Id;
		if (_txtAnimName != null) _txtAnimName.Text = _config.Name;
		if (_optMotionType != null) _optMotionType.Selected = (int)_config.MotionType - 1;
		if (_optMaskMode != null) _optMaskMode.Selected = (int)_config.MaskMode;
	}

	private void UpdateMaskControls()
	{
		SetSlider(_sldMaskMin, _lblMaskMin, _config.MaskMin, "F2");
		SetSlider(_sldMaskMax, _lblMaskMax, _config.MaskMax, "F2");
		SetSlider(_sldMaskPower, _lblMaskPower, _config.MaskPower, "F1");
		if (_chkMaskInvert != null) _chkMaskInvert.ButtonPressed = _config.MaskInvert;
	}

	private void UpdateSwayAndFlutterControls()
	{
		SetSlider(_sldSwayFrequency, _lblSwayFrequency, _config.SwayFrequency, "F1", " Hz");
		SetSlider(_sldSwayAmplitude, _lblSwayAmplitude, _config.SwayAmplitude, "F2", " m");
		SetSlider(_sldFlutterFrequency, _lblFlutterFrequency, _config.FlutterFrequency, "F1", " Hz");
		SetSlider(_sldFlutterAmplitude, _lblFlutterAmplitude, _config.FlutterAmplitude, "F3", " m");
		SetSlider(_sldWindInfluence, _lblWindInfluence, _config.WindInfluence, "F2");
		SetSlider(_sldVelocityDragInfluence, _lblVelocityDragInfluence, _config.VelocityDragInfluence, "F2");
		SetSlider(_sldWaveTurbulence, _lblWaveTurbulence, _config.WaveTurbulence, "F2");
	}

	private void UpdateImpulseControls()
	{
		SetSlider(_sldImpulseAmplitude, _lblImpulseAmplitude, _config.ImpulseAmplitude, "F2", " m");
		SetSlider(_sldImpulseFrequency, _lblImpulseFrequency, _config.ImpulseFrequency, "F1", " Hz");
		SetSlider(_sldImpulseDecay, _lblImpulseDecay, _config.ImpulseDecay, "F1");
		SetSlider(_sldImpulseDuration, _lblImpulseDuration, _config.ImpulseDuration, "F2", " s");
	}

	private void SetSlider(Slider sld, Label lbl, float val, string format, string suffix = "")
	{
		if (sld != null) sld.Value = val;
		if (lbl != null) lbl.Text = $"{val.ToString(format)}{suffix}";
	}

	private void RandomizeParameters()
	{
		_config.SwayFrequency = (float)Math.Round(Random.Shared.NextDouble() * 3.0 + 0.5, 1);
		_config.SwayAmplitude = (float)Math.Round(Random.Shared.NextDouble() * 0.35 + 0.05, 2);
		_config.FlutterFrequency = (float)Math.Round(Random.Shared.NextDouble() * 8.0 + 2.0, 1);
		_config.FlutterAmplitude = (float)Math.Round(Random.Shared.NextDouble() * 0.08 + 0.01, 3);
		_config.WaveTurbulence = (float)Math.Round(Random.Shared.NextDouble() * 0.8 + 0.2, 2);

		SyncControlsFromConfig();
		UpdateShaderParameters();
	}

	private void UpdateShaderParameters()
	{
		if (_currentModelRoot != null && GodotObject.IsInstanceValid(_currentModelRoot))
		{
			bool showHeatmap = _chkShowHeatmap != null && _chkShowHeatmap.ButtonPressed;
			Vector3 vel = new Vector3(_simVelocityX, 0, 0);
			ProceduralAnimationManager.ApplyPreviewToNode(_currentModelRoot, _config, vel, _impulseStrength, _impulseTime, showHeatmap);
		}
	}

	private void Setup3DEnvironment()
	{
		_simRoot = new Node3D();
		_simRoot.Name = "SimRoot";
		PreviewSubViewport.AddChild(_simRoot);

		_currentModelRoot = new Node3D();
		_currentModelRoot.Name = "ModelRoot";
		_simRoot.AddChild(_currentModelRoot);

		UpdateCameraTransform();
	}

	private void LoadPreviewTemplate(TemplateItemInfo template)
	{
		if (_currentModelRoot == null) return;
		ClearCurrentModelRoot();

		if (template == null) return;

		if (template.Id.StartsWith("(") || template.Category.Equals("Sample", StringComparison.OrdinalIgnoreCase))
		{
			LoadSampleTemplate(template);
			return;
		}

		Node3D loadedNode3D = LoadTemplateModel(template);

		if (loadedNode3D != null)
		{
			SetupLoadedModel(loadedNode3D, template);
		}

		UpdateShaderParameters();
	}

	private void ClearCurrentModelRoot()
	{
		foreach (Node child in _currentModelRoot.GetChildren())
		{
			child.QueueFree();
		}
	}

	private void LoadSampleTemplate(TemplateItemInfo template)
	{
		var meshInst = new MeshInstance3D();
		if (template.Id.Contains("Capsule"))
		{
			meshInst.Mesh = new CapsuleMesh { Radius = 0.5f, Height = 1.8f };
			meshInst.Position = new Vector3(0, 0.9f, 0);
		}
		else
		{
			meshInst.Mesh = new CylinderMesh { TopRadius = 0.4f, BottomRadius = 0.6f, Height = 3.0f };
			meshInst.Position = new Vector3(0, 1.5f, 0);
		}
		var mat = new StandardMaterial3D { AlbedoColor = new Color(0.35f, 0.65f, 0.45f) };
		meshInst.MaterialOverride = mat;
		_currentModelRoot.AddChild(meshInst);
		ModelShaderManager.RefreshShaderMaterialsForNode(_currentModelRoot, false);
		CenterAndFrameNode(_currentModelRoot);
		UpdateShaderParameters();
	}

	private Node3D LoadTemplateModel(TemplateItemInfo template)
	{
		string assetPath = !string.IsNullOrEmpty(template.ModelPath) ? template.ModelPath : template.Id;
		string visualMode = !string.IsNullOrEmpty(template.VisualMode) ? template.VisualMode : (assetPath.EndsWith(".rtex", StringComparison.OrdinalIgnoreCase) ? "GroundPlane" : "Mesh");

		bool is2DTexture = string.Equals(visualMode, "GroundPlane", StringComparison.OrdinalIgnoreCase) ||
			string.Equals(visualMode, "SlopeAlignedQuad", StringComparison.OrdinalIgnoreCase) ||
			assetPath.EndsWith(".rtex", StringComparison.OrdinalIgnoreCase) ||
			assetPath.EndsWith(".png", StringComparison.OrdinalIgnoreCase) ||
			assetPath.EndsWith(".webp", StringComparison.OrdinalIgnoreCase);

		if (is2DTexture)
		{
			return Load2DTextureModel(assetPath, visualMode);
		}
		return Load3DModel(assetPath);
	}

	private Node3D Load2DTextureModel(string assetPath, string visualMode)
	{
		Node3D loadedNode3D = null;
		var loaded = ModelCache.GetModel(assetPath);
		if (loaded is Node3D n)
		{
			loadedNode3D = n;
		}
		else
		{
			string resolved = ModelCache.ResolveModelPath(assetPath);
			if (!string.IsNullOrEmpty(resolved))
			{
				loaded = ModelCache.GetModel(resolved);
				if (loaded is Node3D nResolved) loadedNode3D = nResolved;
			}
		}

		if (loadedNode3D != null && string.Equals(visualMode, "SlopeAlignedQuad", StringComparison.OrdinalIgnoreCase))
		{
			loadedNode3D.RotationDegrees = new Vector3(45f, 0f, 0f);
		}
		return loadedNode3D;
	}

	private Node3D Load3DModel(string assetPath)
	{
		string wsPath = MapWorkspaceService.GetActiveWorkspacePath();
		string resolvedDiskPath = ResolveDiskPath(wsPath, assetPath);

		Node3D loadedNode3D = null;
		if (resolvedDiskPath != null && File.Exists(resolvedDiskPath))
		{
			loadedNode3D = LoadFromDiskPath(resolvedDiskPath, assetPath);
		}
		else if (!string.IsNullOrEmpty(assetPath))
		{
			var loaded = ModelCache.GetModel(assetPath);
			if (loaded is Node3D n)
			{
				loadedNode3D = n;
			}
		}

		if (loadedNode3D != null)
		{
			Realm.Godot.Animation.AnimationRetargetingService.TryApplyRiggedIdlePose(loadedNode3D, resolvedDiskPath ?? assetPath);
		}
		return loadedNode3D;
	}

	private string ResolveDiskPath(string wsPath, string assetPath)
	{
		foreach (var sub in new[] { "props", "units", "buildings", "resources", "characters", "items", "projectiles", "attachments" })
		{
			string p = Path.Combine(wsPath, "Assets", "models", sub, assetPath);
			if (File.Exists(p)) return p;
			p = Path.Combine("MapTemplate", "Assets", "models", sub, assetPath);
			if (File.Exists(p)) return p;
		}

		string modelsDir = Path.Combine(wsPath, "Assets", "models");
		if (Directory.Exists(modelsDir))
		{
			var files = Directory.GetFiles(modelsDir, assetPath, SearchOption.AllDirectories);
			if (files.Length > 0 && files[0].EndsWith(".rmesh", StringComparison.OrdinalIgnoreCase))
			{
				return files[0];
			}
		}
		return null;
	}

	private Node3D LoadFromDiskPath(string resolvedDiskPath, string assetPath)
	{
		var loaded = ModelCache.GetModel(resolvedDiskPath) ?? ModelCache.GetModel(assetPath);
		if (loaded is Node3D n) return n;
		
		var gltfDoc = new GltfDocument();
		var gltfState = new GltfState();
		byte[] rmeshBytes = File.ReadAllBytes(resolvedDiskPath);
		byte[] glbBytes = Realm.Shared.ModelOptimization.RmeshFile.GetGlbBytes(rmeshBytes) ?? rmeshBytes;
		if (gltfDoc.AppendFromBuffer(glbBytes, "", gltfState) == Error.Ok)
		{
			var node = gltfDoc.GenerateScene(gltfState);
			if (node is Node3D n3d) return n3d;
		}
		return null;
	}

	private void SetupLoadedModel(Node3D loadedNode3D, TemplateItemInfo template)
	{
		_currentModelRoot.AddChild(loadedNode3D);
		ModelShaderManager.RefreshShaderMaterialsForNode(loadedNode3D, false);

		if (GameHost.Instance != null && !string.IsNullOrEmpty(template.Id))
		{
			float scale = GameHost.Instance.GetModelScale(template.Id);
			if (scale > 0.01f)
			{
				loadedNode3D.Scale = new Vector3(scale, scale, scale);
			}
			float yOffset = GameHost.Instance.GetModelYOffset(template.Id);
			loadedNode3D.Position = new Vector3(0, yOffset, 0);
		}

		CenterAndFrameNode(loadedNode3D);
	}

	private void CenterAndFrameNode(Node3D targetNode)
	{
		var aabb = SpawnDeathShaderManager.CalculateNodeAabb(targetNode);
		TargetPosition = aabb.Position + aabb.Size * 0.5f;
		float maxDim = Mathf.Max(aabb.Size.X, Mathf.Max(aabb.Size.Y, aabb.Size.Z));
		CameraDistance = Mathf.Clamp(maxDim * 2.2f, 2.0f, 30.0f);
		DefaultDistance = CameraDistance;
		UpdateCameraTransform();
	}

	protected override void OnApply()
	{
		if (string.IsNullOrWhiteSpace(_config.Id))
		{
			_config.Id = "custom_procedural_anim";
		}
		if (string.IsNullOrWhiteSpace(_config.Name))
		{
			_config.Name = _config.Id;
		}

		ProceduralAnimationManager.SaveConfig(_config);
		_onSaved?.Invoke(_config);

		Hud?.ShowFeedback(string.Format(TranslationServer.Translate("Saved procedural animation {0} to metadata.json"), _config.Name));
		CloseDialog();
	}

	protected override void OnCancel()
	{
		_config = _snapshot.Clone();
		CloseDialog();
	}
}
