// Scene construction for the studio panel: the sliders, toggles and pickers of the sway, flutter,
// impulse and shader sections. The template picker and the region box are built by their own
// partials, since each owns a sub-dialog worth of widgets.

using System;
using System.Collections.Generic;
using System.IO;
using Godot;
using Realm.Client.Animation;
using Realm.Client.Core;
using Realm.Client.Services;
using Realm.Client.Utils;
using Realm.Shared.Metadata;
using Realm.Shared.ModelOptimization;
using Realm.Shared.Terrain;


namespace Realm.Client.UI.MapEditor;

public partial class ProceduralAnimationStudioDialog
{
	private void BuildControls()
	{
		var previewContainer = new PanelContainer();
		previewContainer.Name = "PreviewContainer_PanelContainer";
		previewContainer.CustomMinimumSize = new Vector2(0, 220);
		previewContainer.AddThemeStyleboxOverride("panel", UIStyle.CreateLightInnerPanel());
		BodyContainer.AddChild(previewContainer);

		Add3DPreviewViewport(previewContainer, new Vector2(0, 220));

		Setup3DEnvironment();

		AddCameraPresetToolbar(BodyContainer, includeBack: true, includeLightingToggle: true);

		var templateRow = new HBoxContainer();
		templateRow.Name = "TemplateRow_HBoxContainer";
		templateRow.AddThemeConstantOverride("separation", 6);

		var lblTemplate = new Label();
		lblTemplate.Name = "LblTemplate_Label";
		lblTemplate.Text = TranslationServer.Translate("Template:");
		lblTemplate.AddThemeFontSizeOverride("font_size", 11);
		lblTemplate.AddThemeColorOverride("font_color", UIStyle.ColorGold);
		templateRow.AddChild(lblTemplate);

		_optTemplatePicker = new OptionButton();
		_optTemplatePicker.Name = "OptTemplatePicker_OptionButton";
		_optTemplatePicker.AddThemeFontSizeOverride("font_size", 11);
		_optTemplatePicker.CustomMinimumSize = new Vector2(160, 24);
		_optTemplatePicker.SizeFlagsHorizontal = SizeFlags.ExpandFill;
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
		playRow.Name = "PlayRow_HBoxContainer";
		playRow.AddThemeConstantOverride("separation", 6);

		AddButton(playRow, "💥 " + TranslationServer.Translate("Trigger Shake Impulse"), () => TriggerTestImpulse(), "Trigger Hit / Harvest Shake Impulse", 10, new Vector2(140, 24));

		_chkShowHeatmap = new CheckBox();
		_chkShowHeatmap.Name = "ChkShowHeatmap_CheckBox";
		_chkShowHeatmap.Text = TranslationServer.Translate("Show Heatmap Mask");
		_chkShowHeatmap.ButtonPressed = false;
		_chkShowHeatmap.AddThemeFontSizeOverride("font_size", 10);
		_chkShowHeatmap.Toggled += (pressed) => UpdateShaderParameters();
		playRow.AddChild(_chkShowHeatmap);

		var lblVel = new Label();
		lblVel.Name = "LblVel_Label";
		lblVel.Text = TranslationServer.Translate("Sim Speed:");
		lblVel.AddThemeFontSizeOverride("font_size", 10);
		lblVel.AddThemeColorOverride("font_color", UIStyle.ColorGoldDull);
		playRow.AddChild(lblVel);

		_sldSimVelocity = new HSlider();
		_sldSimVelocity.Name = "SldSimVelocity_HSlider";
		_sldSimVelocity.MinValue = 0.0;
		_sldSimVelocity.MaxValue = 10.0;
		_sldSimVelocity.Step = 0.2;
		_sldSimVelocity.Value = 0.0;
		_sldSimVelocity.SizeFlagsHorizontal = SizeFlags.ExpandFill;
		_sldSimVelocity.ValueChanged += (val) =>
		{
			_simVelocityX = (float)val;
			if (_lblSimVelocity != null) _lblSimVelocity.Text = $"{val:F1} m/s";
			UpdateShaderParameters();
		};
		playRow.AddChild(_sldSimVelocity);

		_lblSimVelocity = new Label();
		_lblSimVelocity.Name = "LblSimVelocity_Label";
		_lblSimVelocity.Text = "0.0 m/s";
		_lblSimVelocity.CustomMinimumSize = new Vector2(40, 0);
		_lblSimVelocity.AddThemeFontSizeOverride("font_size", 10);
		playRow.AddChild(_lblSimVelocity);

		BodyContainer.AddChild(playRow);

		var scroll = new ScrollContainer();
		scroll.Name = "Scroll_ScrollContainer";
		scroll.SizeFlagsVertical = SizeFlags.ExpandFill;
		scroll.HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled;
		scroll.VerticalScrollMode = ScrollContainer.ScrollMode.Auto;

		var configVBox = new VBoxContainer();
		configVBox.Name = "ConfigVBox_VBoxContainer";
		configVBox.AddThemeConstantOverride("separation", 6);
		configVBox.SizeFlagsHorizontal = SizeFlags.ExpandFill;
		scroll.AddChild(configVBox);
		BodyContainer.AddChild(scroll);

		var presetRow = new HBoxContainer();
		presetRow.Name = "PresetRow_HBoxContainer";
		presetRow.AddThemeConstantOverride("separation", 6);

		var lblPreset = new Label();
		lblPreset.Name = "LblPreset_Label";
		lblPreset.Text = TranslationServer.Translate("Template Preset:");
		lblPreset.AddThemeFontSizeOverride("font_size", 11);
		lblPreset.AddThemeColorOverride("font_color", UIStyle.ColorGold);
		presetRow.AddChild(lblPreset);

		_optPreset = new OptionButton();
		_optPreset.Name = "OptPreset_OptionButton";
		_optPreset.AddThemeFontSizeOverride("font_size", 11);
		_optPreset.SizeFlagsHorizontal = SizeFlags.ExpandFill;
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

		_txtAnimId = AddTextInput(configVBox, TranslationServer.Translate("Profile ID:"), _config.Id, (val) => { _config.Id = val.Trim().ToLowerInvariant().Replace(" ", "_"); }, "", 150f);

		_txtAnimName = AddTextInput(configVBox, TranslationServer.Translate("Display Name:"), _config.Name, (val) => { _config.Name = val; }, "", 150f);

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
			TranslationServer.Translate("2: Normal Incline (Facing Direction)").ToString(),
			TranslationServer.Translate("3: Region Box (Explicit Part Volume)").ToString()
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

		BuildRegionBoxControls(configVBox);

		BuildSwayRegionControls(configVBox);

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

		(_sldImpulseDuration, _lblImpulseDuration) = AddSlider(configVBox, TranslationServer.Translate("Impulse Duration (s):"), 0.1f, 2.0f, 0.05f, _config.ImpulseDuration, (val) => { _config.ImpulseDuration = val; }, "0.00 s", 150f);
	}

}