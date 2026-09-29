using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json.Nodes;
using Godot;
using Realm.Godot.Utils;
using Realm.Godot.VFX;

public partial class ProceduralAnimationStudioDialog : FloatingPreview3DDialogBase
{
	private Node3D _simRoot;
	private Node3D _currentModelRoot;

	private LineEdit _txtAnimId;
	private LineEdit _txtAnimName;
	private OptionButton _optPreset;
	private OptionButton _optModelPicker;
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
	private string _selectedModelKey = "";
	private List<string> _availableModels = new();

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
		// 1. TOP LIVE PREVIEW (3D VIEWPORT)
		var previewContainer = new PanelContainer();
		previewContainer.CustomMinimumSize = new Vector2(0, 220);
		previewContainer.AddThemeStyleboxOverride("panel", UIStyle.CreateLightInnerPanel());
		BodyContainer.AddChild(previewContainer);

		Add3DPreviewViewport(previewContainer, new Vector2(0, 220));

		Setup3DEnvironment();

		// CAMERA TOOLBAR WITH EMBEDDED CONTROLS
		AddCameraPresetToolbar(BodyContainer, includeBack: true, includeLightingToggle: true);

		// ROW 1: MODEL PICKER
		var modelRow = new HBoxContainer();
		modelRow.AddThemeConstantOverride("separation", 6);

		var lblModel = new Label();
		lblModel.Text = TranslationServer.Translate("Preview Model:");
		lblModel.AddThemeFontSizeOverride("font_size", 11);
		lblModel.AddThemeColorOverride("font_color", UIStyle.ColorGold);
		modelRow.AddChild(lblModel);

		_optModelPicker = new OptionButton();
		_optModelPicker.AddThemeFontSizeOverride("font_size", 11);
		_optModelPicker.CustomMinimumSize = new Vector2(160, 24);
		_optModelPicker.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
		_optModelPicker.ItemSelected += (idx) =>
		{
			if (idx >= 0 && idx < _availableModels.Count)
			{
				_selectedModelKey = _availableModels[(int)idx];
				LoadPreviewModel(_selectedModelKey);
			}
		};
		modelRow.AddChild(_optModelPicker);

		BodyContainer.AddChild(modelRow);

		// ROW 2: PLAYBACK & INTERACTIVE TESTING
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

		// 2. CONFIGURATION CONTROLS
		var scroll = new ScrollContainer();
		scroll.SizeFlagsVertical = Control.SizeFlags.ExpandFill;
		scroll.HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled;
		scroll.VerticalScrollMode = ScrollContainer.ScrollMode.Auto;

		var configVBox = new VBoxContainer();
		configVBox.AddThemeConstantOverride("separation", 6);
		configVBox.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
		scroll.AddChild(configVBox);
		BodyContainer.AddChild(scroll);

		// PRESET LOADER ROW
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

		// IDENTIFIERS
		_txtAnimId = AddTextInput(configVBox, TranslationServer.Translate("Profile ID:"), _config.Id, (val) =>
		{
			_config.Id = val.Trim().ToLowerInvariant().Replace(" ", "_");
		}, "", 150f);

		_txtAnimName = AddTextInput(configVBox, TranslationServer.Translate("Display Name:"), _config.Name, (val) =>
		{
			_config.Name = val;
		}, "", 150f);

		var btnRandomize = AddButton(configVBox, "🎲 " + TranslationServer.Translate("Randomize Sway & Flutter"), () => RandomizeParameters(), "Randomize motion parameters", 11, new Vector2(0, 26));

		// MOTION TYPE
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

		// SECTION 1: SPATIAL MASK
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

		// SECTION 2: SWAY & FLUTTER DYNAMICS
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

		// SECTION 3: TRANSIENT IMPACT / SHAKE
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
		{
			_selectedModelKey = previewModelKey;
		}

		PopulatePresetList();
		PopulateModelList();

		if (config != null && !string.IsNullOrWhiteSpace(config.Id))
		{
			var existing = ProceduralAnimationManager.GetConfig(config.Id);
			_config = existing != null ? existing.Clone() : config.Clone();
		}
		else
		{
			_config = new ProceduralAnimationConfig
			{
				Id = "new_procedural_anim",
				Name = "New Procedural Animation"
			};
		}

		_snapshot = _config.Clone();
		SyncControlsFromConfig();

		int selectedPresetIdx = -1;
		if (_optPreset != null && !string.IsNullOrEmpty(_config.Id))
		{
			for (int i = 0; i < _optPreset.ItemCount; i++)
			{
				string metaKey = _optPreset.GetItemMetadata(i).AsString();
				if (string.Equals(metaKey, _config.Id, StringComparison.OrdinalIgnoreCase))
				{
					selectedPresetIdx = i;
					break;
				}
			}
			_optPreset.Selected = selectedPresetIdx;
		}

		if (_availableModels.Count > 0 && string.IsNullOrEmpty(_selectedModelKey))
		{
			_selectedModelKey = _availableModels[0];
		}
		LoadPreviewModel(_selectedModelKey);

		OpenDialog();
	}

	private void PopulateModelList()
	{
		_availableModels.Clear();
		if (_optModelPicker == null) return;
		_optModelPicker.Clear();

		string wsPath = MapWorkspaceService.GetActiveWorkspacePath();
		string modelsDir = Path.Combine(wsPath, "Assets", "models");
		if (Directory.Exists(modelsDir))
		{
			var files = Directory.GetFiles(modelsDir, "*.rmesh", SearchOption.AllDirectories);
			foreach (var f in files)
			{
				string name = Path.GetFileName(f);
				if (!_availableModels.Contains(name))
				{
					_availableModels.Add(name);
				}
			}
		}

		try
		{
			var assets = MapAssetHelper.LoadUnionedAssets(wsPath);
			if (assets["rmesh"] is JsonObject rmeshObj)
			{
				foreach (var sub in rmeshObj)
				{
					if (sub.Value is JsonObject subObj)
					{
						foreach (var model in subObj)
						{
							if (!string.IsNullOrEmpty(model.Key) && model.Key.EndsWith(".rmesh", StringComparison.OrdinalIgnoreCase))
							{
								string name = Path.GetFileName(model.Key);
								if (!_availableModels.Contains(name))
								{
									_availableModels.Add(name);
								}
							}
						}
					}
				}
			}
		}
		catch { }

		if (_availableModels.Count == 0)
		{
			_availableModels.Add("(Sample Tree Cylinder)");
			_availableModels.Add("(Sample Unit Capsule)");
		}

		int selectedIdx = -1;
		if (!string.IsNullOrWhiteSpace(_selectedModelKey))
		{
			string targetName = Path.GetFileName(_selectedModelKey);
			string targetWithoutExt = Path.GetFileNameWithoutExtension(_selectedModelKey);

			selectedIdx = _availableModels.FindIndex(m => string.Equals(m, _selectedModelKey, StringComparison.OrdinalIgnoreCase));
			if (selectedIdx < 0)
			{
				selectedIdx = _availableModels.FindIndex(m => string.Equals(Path.GetFileName(m), targetName, StringComparison.OrdinalIgnoreCase));
			}
			if (selectedIdx < 0)
			{
				selectedIdx = _availableModels.FindIndex(m => string.Equals(Path.GetFileNameWithoutExtension(m), targetWithoutExt, StringComparison.OrdinalIgnoreCase));
			}

			if (selectedIdx < 0 && !string.IsNullOrWhiteSpace(targetName))
			{
				string modelToAdd = targetName.EndsWith(".rmesh", StringComparison.OrdinalIgnoreCase) ? targetName : targetName + ".rmesh";
				_availableModels.Insert(0, modelToAdd);
				selectedIdx = 0;
			}
		}

		int idx = 0;
		foreach (var m in _availableModels)
		{
			_optModelPicker.AddItem(m, idx++);
		}

		if (selectedIdx >= 0 && selectedIdx < _availableModels.Count)
		{
			_selectedModelKey = _availableModels[selectedIdx];
			_optModelPicker.Selected = selectedIdx;
		}
		else if (_availableModels.Count > 0)
		{
			_selectedModelKey = _availableModels[0];
			_optModelPicker.Selected = 0;
		}
	}

	private void SyncControlsFromConfig()
	{
		if (_txtAnimId != null) _txtAnimId.Text = _config.Id;
		if (_txtAnimName != null) _txtAnimName.Text = _config.Name;
		if (_optMotionType != null) _optMotionType.Selected = (int)_config.MotionType - 1;
		if (_optMaskMode != null) _optMaskMode.Selected = (int)_config.MaskMode;

		if (_sldMaskMin != null) { _sldMaskMin.Value = _config.MaskMin; _lblMaskMin.Text = $"{_config.MaskMin:F2}"; }
		if (_sldMaskMax != null) { _sldMaskMax.Value = _config.MaskMax; _lblMaskMax.Text = $"{_config.MaskMax:F2}"; }
		if (_sldMaskPower != null) { _sldMaskPower.Value = _config.MaskPower; _lblMaskPower.Text = $"{_config.MaskPower:F1}"; }
		if (_chkMaskInvert != null) _chkMaskInvert.ButtonPressed = _config.MaskInvert;

		if (_sldSwayFrequency != null) { _sldSwayFrequency.Value = _config.SwayFrequency; _lblSwayFrequency.Text = $"{_config.SwayFrequency:F1} Hz"; }
		if (_sldSwayAmplitude != null) { _sldSwayAmplitude.Value = _config.SwayAmplitude; _lblSwayAmplitude.Text = $"{_config.SwayAmplitude:F2} m"; }
		if (_sldFlutterFrequency != null) { _sldFlutterFrequency.Value = _config.FlutterFrequency; _lblFlutterFrequency.Text = $"{_config.FlutterFrequency:F1} Hz"; }
		if (_sldFlutterAmplitude != null) { _sldFlutterAmplitude.Value = _config.FlutterAmplitude; _lblFlutterAmplitude.Text = $"{_config.FlutterAmplitude:F3} m"; }
		if (_sldWindInfluence != null) { _sldWindInfluence.Value = _config.WindInfluence; _lblWindInfluence.Text = $"{_config.WindInfluence:F2}"; }
		if (_sldVelocityDragInfluence != null) { _sldVelocityDragInfluence.Value = _config.VelocityDragInfluence; _lblVelocityDragInfluence.Text = $"{_config.VelocityDragInfluence:F2}"; }
		if (_sldWaveTurbulence != null) { _sldWaveTurbulence.Value = _config.WaveTurbulence; _lblWaveTurbulence.Text = $"{_config.WaveTurbulence:F2}"; }

		if (_sldImpulseAmplitude != null) { _sldImpulseAmplitude.Value = _config.ImpulseAmplitude; _lblImpulseAmplitude.Text = $"{_config.ImpulseAmplitude:F2} m"; }
		if (_sldImpulseFrequency != null) { _sldImpulseFrequency.Value = _config.ImpulseFrequency; _lblImpulseFrequency.Text = $"{_config.ImpulseFrequency:F1} Hz"; }
		if (_sldImpulseDecay != null) { _sldImpulseDecay.Value = _config.ImpulseDecay; _lblImpulseDecay.Text = $"{_config.ImpulseDecay:F1}"; }
		if (_sldImpulseDuration != null) { _sldImpulseDuration.Value = _config.ImpulseDuration; _lblImpulseDuration.Text = $"{_config.ImpulseDuration:F2} s"; }
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

	private void LoadPreviewModel(string key)
	{
		if (_currentModelRoot == null) return;
		foreach (Node child in _currentModelRoot.GetChildren())
		{
			child.QueueFree();
		}

		if (string.IsNullOrEmpty(key)) return;

		if (key.StartsWith("("))
		{
			var meshInst = new MeshInstance3D();
			if (key.Contains("Capsule"))
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
			return;
		}

		string wsPath = MapWorkspaceService.GetActiveWorkspacePath();
		string modelPath = null;
		foreach (var sub in new[] { "props", "units", "buildings", "resources", "characters", "items" })
		{
			string p = Path.Combine(wsPath, "Assets", "models", sub, key);
			if (File.Exists(p)) { modelPath = p; break; }
		}

		if (!File.Exists(modelPath))
		{
			string modelsDir = Path.Combine(wsPath, "Assets", "models");
			if (Directory.Exists(modelsDir))
			{
				var files = Directory.GetFiles(modelsDir, key, SearchOption.AllDirectories);
				if (files.Length > 0 && files[0].EndsWith(".rmesh", StringComparison.OrdinalIgnoreCase))
				{
					modelPath = files[0];
				}
			}
		}

		Node3D loadedNode3D = null;
		if (modelPath != null && modelPath.EndsWith(".rmesh", StringComparison.OrdinalIgnoreCase) && File.Exists(modelPath))
		{
			var loaded = ModelCache.GetModel(modelPath) ?? ModelCache.GetModel(key);
			if (loaded is Node3D n)
			{
				loadedNode3D = n;
			}
			else
			{
				var gltfDoc = new GltfDocument();
				var gltfState = new GltfState();
				byte[] rmeshBytes = File.ReadAllBytes(modelPath);
				byte[] glbBytes = Realm.Shared.ModelOptimization.RmeshFile.GetGlbBytes(rmeshBytes) ?? rmeshBytes;
				if (gltfDoc.AppendFromBuffer(glbBytes, "", gltfState) == Error.Ok)
				{
					var node = gltfDoc.GenerateScene(gltfState);
					if (node is Node3D n3d) loadedNode3D = n3d;
				}
			}
		}
		else if (!string.IsNullOrEmpty(key))
		{
			var loaded = ModelCache.GetModel(key);
			if (loaded is Node3D n)
			{
				loadedNode3D = n;
			}
		}

		if (loadedNode3D != null)
		{
			_currentModelRoot.AddChild(loadedNode3D);
			ModelShaderManager.RefreshShaderMaterialsForNode(loadedNode3D, false);
			Realm.Godot.Animation.AnimationRetargetingService.TryApplyRiggedIdlePose(loadedNode3D, modelPath ?? key);
			CenterAndFrameNode(loadedNode3D);
		}

		UpdateShaderParameters();
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
