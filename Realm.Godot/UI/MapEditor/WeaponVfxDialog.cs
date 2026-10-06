using Godot;
using System;
using System.Collections.Generic;
using Realm.Godot.Utils;

public partial class WeaponVfxDialog : FloatingPreview3DDialogBase
{
	private AudioStreamPlayer _sfxPlayer;
	private VisualProjectile3D _previewProjectile;

	public VisualProjectile3D PreviewProjectile => _previewProjectile;

	private WeaponMetadata _initialWeapon = new();
	private WeaponMetadata _currentWeapon = new();
	private string _weaponId = "";
	private string _slug = "";
	private Label _lblObjectTypePrefix;
	private LineEdit _txtSlug;
	private Action<WeaponMetadata> _onAppliedCallback;
	private bool _isUpdatingUI;

	private bool _isPlaybackPaused;
	private float _previewSpeed = 1.0f;

	public WeaponVfxDialog(MapEditorHUD hud)
		: base(hud, TranslationServer.Translate("Weapon Visual & Sound Effects"), new Vector2(480, 710))
	{
		DefaultDistance = 5.5f;
		CameraDistance = 5.5f;
		DefaultYaw = Mathf.DegToRad(30.0f);
		DefaultPitch = Mathf.DegToRad(15.0f);
		CameraYaw = Mathf.DegToRad(30.0f);
		CameraPitch = Mathf.DegToRad(15.0f);
		DefaultTargetPosition = new Vector3(0, 0.5f, 0);
		TargetPosition = DefaultTargetPosition;

		BuildControls();
	}

	private void BuildControls()
	{
		Add3DPreviewViewport(BodyContainer, new Vector2(460, 220));

		_sfxPlayer = new AudioStreamPlayer();
		PreviewSubViewport.AddChild(_sfxPlayer);

		// ROW 1: FIRE TEST & CAMERA PRESETS
		var topToolbar = new HBoxContainer();
		topToolbar.AddThemeConstantOverride("separation", 4);

		AddButton(topToolbar, "▶ " + TranslationServer.Translate("Fire Test"), () => RestartPreviewProjectile(), "Restart preview projectile", 10, new Vector2(0, 22));

		var separator = new Control { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
		topToolbar.AddChild(separator);

		AddCameraPresetToolbar(topToolbar, includeBack: false);

		BodyContainer.AddChild(topToolbar);

		// ROW 2: PLAYBACK CONTROLS (PLAY, PAUSE, FRAME-FORWARD, FRAME-BACKWARD, SPEED SLIDER)
		var playbackToolbar = new HBoxContainer();
		playbackToolbar.AddThemeConstantOverride("separation", 4);

		AddButton(playbackToolbar, "▶ " + TranslationServer.Translate("Play"), () => SetPlaybackPaused(false), "Resume projectile animation", 10, new Vector2(0, 22));
		AddButton(playbackToolbar, "⏸ " + TranslationServer.Translate("Pause"), () => SetPlaybackPaused(true), "Pause projectile animation", 10, new Vector2(0, 22));
		AddButton(playbackToolbar, "⏮ " + TranslationServer.Translate("-1F"), () => StepFrame(-1f / 30f), "Step 1 frame backward (1/30s)", 10, new Vector2(0, 22));
		AddButton(playbackToolbar, "⏭ " + TranslationServer.Translate("+1F"), () => StepFrame(1f / 30f), "Step 1 frame forward (1/30s)", 10, new Vector2(0, 22));

		var speedLbl = new Label();
		speedLbl.Text = TranslationServer.Translate("Speed:");
		speedLbl.AddThemeFontSizeOverride("font_size", 10);
		speedLbl.AddThemeColorOverride("font_color", UIStyle.ColorGoldDull);
		playbackToolbar.AddChild(speedLbl);

		var speedSlider = new HSlider();
		speedSlider.MinValue = 0.1f;
		speedSlider.MaxValue = 3.0f;
		speedSlider.Step = 0.05f;
		speedSlider.Value = _previewSpeed;
		speedSlider.CustomMinimumSize = new Vector2(80, 0);
		speedSlider.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
		playbackToolbar.AddChild(speedSlider);

		var speedValLbl = new Label();
		speedValLbl.Text = $"{_previewSpeed:0.00}x";
		speedValLbl.CustomMinimumSize = new Vector2(38, 0);
		speedValLbl.AddThemeFontSizeOverride("font_size", 10);
		playbackToolbar.AddChild(speedValLbl);

		speedSlider.ValueChanged += (double val) =>
		{
			_previewSpeed = (float)val;
			speedValLbl.Text = $"{_previewSpeed:0.00}x";
			if (_previewProjectile != null && GodotObject.IsInstanceValid(_previewProjectile))
			{
				_previewProjectile.TimeScale = _previewSpeed;
			}
		};

		BodyContainer.AddChild(playbackToolbar);

		var scrollBody = CreateScrollBody(360);

		// SECTION 0: IDENTITY
		AddSectionHeader(scrollBody, "🆔 " + TranslationServer.Translate("IDENTITY"), new Color(0.95f, 0.8f, 0.4f));

		var rowId = new HBoxContainer();
		rowId.AddThemeConstantOverride("separation", 6);
		var lblId = new Label();
		lblId.Text = TranslationServer.Translate("TemplateID:");
		lblId.CustomMinimumSize = new Vector2(140, 0);
		lblId.AddThemeFontSizeOverride("font_size", 11);
		rowId.AddChild(lblId);

		_lblObjectTypePrefix = new Label();
		_lblObjectTypePrefix.Text = "weapon/";
		_lblObjectTypePrefix.AddThemeFontSizeOverride("font_size", 11);
		_lblObjectTypePrefix.AddThemeColorOverride("font_color", UIStyle.ColorGold);
		rowId.AddChild(_lblObjectTypePrefix);

		_txtSlug = new LineEdit();
		_txtSlug.PlaceholderText = TranslationServer.Translate("weapon_slug");
		_txtSlug.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
		_txtSlug.AddThemeFontSizeOverride("font_size", 11);
		_txtSlug.TextChanged += (val) =>
		{
			if (_isUpdatingUI) return;
			_slug = TemplateIDHelper.ToSnakeCase(val);
			_weaponId = TemplateIDHelper.NormalizeTemplateID("weapon", _slug);
			_currentWeapon.TemplateID = _weaponId;
		};
		rowId.AddChild(_txtSlug);
		scrollBody.AddChild(rowId);

		// SECTION 1: AUDIO & IMPACT EFFECTS
		AddSectionHeader(scrollBody, "🔊 " + TranslationServer.Translate("AUDIO & IMPACT EFFECTS"), new Color(0.3f, 0.8f, 0.7f));
		
		AddAssetFilterDropdown(
			scrollBody,
			TranslationServer.Translate("Attack Sound"),
			_currentWeapon.AttackSound ?? "",
			(all) => ScanAvailableAssets("audio", all),
			(val) =>
			{
				if (_isUpdatingUI) return;
				_currentWeapon.AttackSound = val;
			},
			TranslationServer.Translate("Select imported sound..."),
			140f,
			false,
			(snd) => PlaySound(snd)
		);

		AddAssetFilterDropdown(
			scrollBody,
			TranslationServer.Translate("Impact Sound"),
			_currentWeapon.ImpactSound ?? "",
			(all) => ScanAvailableAssets("audio", all),
			(val) =>
			{
				if (_isUpdatingUI) return;
				_currentWeapon.ImpactSound = val;
			},
			TranslationServer.Translate("Select imported sound..."),
			140f,
			false,
			(snd) => PlaySound(snd)
		);

		var (impactInput, setImpactValue) = AddAssetFilterDropdown(
			scrollBody,
			TranslationServer.Translate("Impact Visual VFX"),
			_currentWeapon.ImpactVisualEffect ?? "",
			(all) => ScanAvailableAssets("vfx", all),
			(val) =>
			{
				if (_isUpdatingUI) return;
				_currentWeapon.ImpactVisualEffect = val;
			},
			TranslationServer.Translate("Select imported VFX..."),
			140f
		);

		var impactBtnRow = new HBoxContainer();
		impactBtnRow.AddThemeConstantOverride("separation", 6);
		var impactSpacer = new Control { CustomMinimumSize = new Vector2(140f, 0) };
		impactBtnRow.AddChild(impactSpacer);
		AddButton(impactBtnRow, "✨ " + TranslationServer.Translate("VFX Studio (Impact)"), () =>
		{
			Hud?.OpenVfxStudioDialog(null, (cfg) =>
			{
				string key = $"vfx:{cfg.VfxId}";
				_currentWeapon.ImpactVisualEffect = key;
				setImpactValue?.Invoke(key);
			});
		}, "Open Procedural VFX Studio to create or edit impact VFX", 10, new Vector2(180, 24));
		scrollBody.AddChild(impactBtnRow);

		// SECTION 2: PROGRAMMATIC PROJECTILE MOVEMENT
		AddSectionHeader(scrollBody, "🚀 " + TranslationServer.Translate("PROJECTILE MOVEMENT"), new Color(0.35f, 0.6f, 0.85f));

		var (modelInput, setModelValue) = AddAssetFilterDropdown(
			scrollBody,
			TranslationServer.Translate("3D Model / VFX"),
			_currentWeapon.ProjectileModelPath ?? "",
			(all) => ScanAvailableAssets("models", all),
			(val) =>
			{
				if (_isUpdatingUI) return;
				_currentWeapon.ProjectileModelPath = val;
				RestartPreviewProjectile();
			},
			TranslationServer.Translate("Select imported 3D model or procedural VFX..."),
			140f,
			true
		);

		var modelBtnRow = new HBoxContainer();
		modelBtnRow.AddThemeConstantOverride("separation", 6);
		var modelSpacer = new Control { CustomMinimumSize = new Vector2(140f, 0) };
		modelBtnRow.AddChild(modelSpacer);
		AddButton(modelBtnRow, "✨ " + TranslationServer.Translate("VFX Studio (Projectile)"), () =>
		{
			Hud?.OpenVfxStudioDialog(null, (cfg) =>
			{
				string key = $"vfx:{cfg.VfxId}";
				_currentWeapon.ProjectileModelPath = key;
				setModelValue?.Invoke(key);
				RestartPreviewProjectile();
			});
		}, "Open Procedural VFX Studio to create or edit projectile VFX", 10, new Vector2(180, 24));
		scrollBody.AddChild(modelBtnRow);

		AddSlider(scrollBody, TranslationServer.Translate("Speed (Units/s)"), 0f, 100f, 1f, _currentWeapon.ProjectileSpeed > 0 ? _currentWeapon.ProjectileSpeed : 25f, (val) =>
		{
			if (_isUpdatingUI) return;
			_currentWeapon.ProjectileSpeed = val;
			RestartPreviewProjectile();
		}, "0", 140f);

		AddSlider(scrollBody, TranslationServer.Translate("Acceleration"), -50f, 100f, 1f, _currentWeapon.Acceleration, (val) =>
		{
			if (_isUpdatingUI) return;
			_currentWeapon.Acceleration = val;
		}, "0.0", 140f);

		string[] speedCurves = new[] { "constant", "ease_in", "ease_out", "ease_in_out", "rocket_boost", "burst" };
		int speedCurveIdx = Math.Max(0, Array.IndexOf(speedCurves, _currentWeapon.SpeedCurve?.ToLowerInvariant() ?? "constant"));
		AddOptionDropdown(scrollBody, TranslationServer.Translate("Speed Curve"), speedCurves, speedCurveIdx, (idx) =>
		{
			if (_isUpdatingUI) return;
			_currentWeapon.SpeedCurve = speedCurves[idx];
		}, 140f);

		string[] easeCurves = new[] { "linear", "ease_in", "ease_out", "ease_in_out" };
		int easeCurveIdx = Math.Max(0, Array.IndexOf(easeCurves, _currentWeapon.EaseCurve?.ToLowerInvariant() ?? "linear"));
		AddOptionDropdown(scrollBody, TranslationServer.Translate("Ease Curve"), easeCurves, easeCurveIdx, (idx) =>
		{
			if (_isUpdatingUI) return;
			_currentWeapon.EaseCurve = easeCurves[idx];
		}, 140f);

		AddSlider(scrollBody, TranslationServer.Translate("Arc Height"), 0f, 20f, 0.5f, _currentWeapon.ArcHeight, (val) =>
		{
			if (_isUpdatingUI) return;
			_currentWeapon.ArcHeight = val;
			RestartPreviewProjectile();
		}, "0.0", 140f);

		AddSlider(scrollBody, TranslationServer.Translate("Homing Weight"), 0f, 1f, 0.05f, _currentWeapon.HomingWeight, (val) =>
		{
			if (_isUpdatingUI) return;
			_currentWeapon.HomingWeight = val;
		}, "0.00", 140f);

		AddSlider(scrollBody, TranslationServer.Translate("Turn Rate (°/s)"), 0f, 720f, 15f, _currentWeapon.TurnRateLimit, (val) =>
		{
			if (_isUpdatingUI) return;
			_currentWeapon.TurnRateLimit = val;
		}, "0", 140f);

		AddSlider(scrollBody, TranslationServer.Translate("Max Lifetime (s)"), 0f, 15f, 0.5f, _currentWeapon.MaxLifetime, (val) =>
		{
			if (_isUpdatingUI) return;
			_currentWeapon.MaxLifetime = val;
		}, "0.0", 140f);

		AddSlider(scrollBody, TranslationServer.Translate("Failsafe Range"), 0f, 150f, 5f, _currentWeapon.FailsafeRange, (val) =>
		{
			if (_isUpdatingUI) return;
			_currentWeapon.FailsafeRange = val;
		}, "0", 140f);

		string[] scaleCurves = new[] { "constant", "grow", "shrink", "grow_shrink", "squash_stretch", "impact_shrink" };
		int scaleCurveIdx = Math.Max(0, Array.IndexOf(scaleCurves, _currentWeapon.ScaleCurve?.ToLowerInvariant() ?? "constant"));
		AddOptionDropdown(scrollBody, TranslationServer.Translate("Scale over Lifetime"), scaleCurves, scaleCurveIdx, (idx) =>
		{
			if (_isUpdatingUI) return;
			_currentWeapon.ScaleCurve = scaleCurves[idx];
		}, 140f);

		AddCheckBox(scrollBody, TranslationServer.Translate("Orient to Trajectory"), _currentWeapon.OrientToTrajectory, (pressed) =>
		{
			if (_isUpdatingUI) return;
			_currentWeapon.OrientToTrajectory = pressed;
			RestartPreviewProjectile();
		});

		AddSlider(scrollBody, TranslationServer.Translate("Max Bounces"), 0f, 10f, 1f, _currentWeapon.MaxBounces, (val) =>
		{
			if (_isUpdatingUI) return;
			_currentWeapon.MaxBounces = (int)val;
		}, "0", 140f);

		AddSlider(scrollBody, TranslationServer.Translate("Pierce Count"), 0f, 10f, 1f, _currentWeapon.PierceCount, (val) =>
		{
			if (_isUpdatingUI) return;
			_currentWeapon.PierceCount = (int)val;
		}, "0", 140f);

		AddVector3Input(scrollBody, TranslationServer.Translate("Tumble Angular Vel"), _currentWeapon.TumbleAngularVelocity.ToGodotVector3(), (val) =>
		{
			if (_isUpdatingUI) return;
			_currentWeapon.TumbleAngularVelocity = val.ToVector3Data();
		}, 140f);

		AddVector2Input(scrollBody, TranslationServer.Translate("Spiral (Rad / Freq)"), new Vector2(_currentWeapon.SpiralRadius, _currentWeapon.SpiralFrequency), (val) =>
		{
			if (_isUpdatingUI) return;
			_currentWeapon.SpiralRadius = val.X;
			_currentWeapon.SpiralFrequency = val.Y;
		}, 140f);

		AddVector2Input(scrollBody, TranslationServer.Translate("Zigzag (Amp / Freq)"), new Vector2(_currentWeapon.ZigzagAmplitude, _currentWeapon.ZigzagFrequency), (val) =>
		{
			if (_isUpdatingUI) return;
			_currentWeapon.ZigzagAmplitude = val.X;
			_currentWeapon.ZigzagFrequency = val.Y;
		}, 140f);

		string[] forwardAxes = new[] { "-Z", "+Z", "+X", "-X", "+Y", "-Y" };
		int forwardAxisIdx = Math.Max(0, Array.IndexOf(forwardAxes, _currentWeapon.ForwardAxisPreset?.Trim().ToUpperInvariant() ?? "-Z"));
		AddOptionDropdown(scrollBody, TranslationServer.Translate("Forward Axis"), forwardAxes, forwardAxisIdx, (idx) =>
		{
			if (_isUpdatingUI) return;
			_currentWeapon.ForwardAxisPreset = forwardAxes[idx];
			RestartPreviewProjectile();
		}, 140f);

		AddVector3Input(scrollBody, TranslationServer.Translate("Mesh Translation Offset"), _currentWeapon.MeshTranslationOffset.ToGodotVector3(), (val) =>
		{
			if (_isUpdatingUI) return;
			_currentWeapon.MeshTranslationOffset = val.ToVector3Data();
			RestartPreviewProjectile();
		}, 140f);

		AddVector3Input(scrollBody, TranslationServer.Translate("Mesh Rotation Offset"), _currentWeapon.MeshRotationOffset.ToGodotVector3(), (val) =>
		{
			if (_isUpdatingUI) return;
			_currentWeapon.MeshRotationOffset = val.ToVector3Data();
			RestartPreviewProjectile();
		}, 140f);

		Vector3 meshScale = _currentWeapon.MeshScaleOffset.ToGodotVector3();
		AddVector3Input(scrollBody, TranslationServer.Translate("Mesh Scale Offset"), meshScale == Vector3.Zero ? Vector3.One : meshScale, (val) =>
		{
			if (_isUpdatingUI) return;
			_currentWeapon.MeshScaleOffset = val.ToVector3Data();
			RestartPreviewProjectile();
		}, 140f);

		// SECTION 3: PROCEDURAL SURFACE UBER-SHADER
		AddSectionHeader(scrollBody, "🎨 " + TranslationServer.Translate("PROCEDURAL SURFACE UBER-SHADER"), new Color(0.8f, 0.55f, 0.45f));

		var presetFxRow = new HBoxContainer();
		presetFxRow.AddThemeConstantOverride("separation", 4);
		AddLabel(presetFxRow, TranslationServer.Translate("Presets:"), 11, UIStyle.ColorGoldDull);
		AddButton(presetFxRow, "🔥 Fire", () => ApplyShaderPreset("fire"), "Fire/Lava preset", 10);
		AddButton(presetFxRow, "❄️ Frost", () => ApplyShaderPreset("frost"), "Frost/Ice preset", 10);
		AddButton(presetFxRow, "🧪 Poison", () => ApplyShaderPreset("poison"), "Poison preset", 10);
		AddButton(presetFxRow, "✨ Arcane", () => ApplyShaderPreset("arcane"), "Arcane preset", 10);
		AddButton(presetFxRow, "☀️ Holy", () => ApplyShaderPreset("holy"), "Holy preset", 10);
		scrollBody.AddChild(presetFxRow);

		string[] emissionMasks = new[] { "noise", "vertex_color", "fresnel", "texture_alpha" };
		int maskIdx = Math.Max(0, Array.IndexOf(emissionMasks, _currentWeapon.EmissionMaskSource?.ToLowerInvariant() ?? "noise"));
		AddOptionDropdown(scrollBody, TranslationServer.Translate("Emission Mask"), emissionMasks, maskIdx, (idx) =>
		{
			if (_isUpdatingUI) return;
			_currentWeapon.EmissionMaskSource = emissionMasks[idx];
			RestartPreviewProjectile();
		}, 140f);

		AddColorPicker(scrollBody, TranslationServer.Translate("Base Color"), ParseColorSafe(_currentWeapon.BaseColor, new Color(0.15f, 0.12f, 0.1f)), (c) =>
		{
			if (_isUpdatingUI) return;
			_currentWeapon.BaseColor = "#" + c.ToHtml(false);
			RestartPreviewProjectile();
		}, 140f);

		AddColorPicker(scrollBody, TranslationServer.Translate("Emission Color"), ParseColorSafe(_currentWeapon.EmissionColor, new Color(1.0f, 0.4f, 0.0f)), (c) =>
		{
			if (_isUpdatingUI) return;
			_currentWeapon.EmissionColor = "#" + c.ToHtml(false);
			RestartPreviewProjectile();
		}, 140f);

		AddSlider(scrollBody, TranslationServer.Translate("Emission Energy"), 0f, 20f, 0.5f, _currentWeapon.EmissionEnergy, (val) =>
		{
			if (_isUpdatingUI) return;
			_currentWeapon.EmissionEnergy = val;
			RestartPreviewProjectile();
		}, "0.0", 140f);

		AddColorPicker(scrollBody, TranslationServer.Translate("Fresnel Color"), ParseColorSafe(_currentWeapon.FresnelColor, new Color(1.0f, 0.6f, 0.2f)), (c) =>
		{
			if (_isUpdatingUI) return;
			_currentWeapon.FresnelColor = "#" + c.ToHtml(false);
			RestartPreviewProjectile();
		}, 140f);

		AddSlider(scrollBody, TranslationServer.Translate("Fresnel Power"), 0.1f, 10f, 0.2f, _currentWeapon.FresnelPower > 0 ? _currentWeapon.FresnelPower : 3.0f, (val) =>
		{
			if (_isUpdatingUI) return;
			_currentWeapon.FresnelPower = val;
			RestartPreviewProjectile();
		}, "0.0", 140f);

		AddSlider(scrollBody, TranslationServer.Translate("Fresnel Factor"), 0.0f, 5.0f, 0.1f, _currentWeapon.FresnelFactor, (val) =>
		{
			if (_isUpdatingUI) return;
			_currentWeapon.FresnelFactor = val;
			RestartPreviewProjectile();
		}, "0.0", 140f);

		AddSlider(scrollBody, TranslationServer.Translate("Noise Scale"), 0.1f, 20f, 0.5f, _currentWeapon.NoiseScale > 0 ? _currentWeapon.NoiseScale : 3.0f, (val) =>
		{
			if (_isUpdatingUI) return;
			_currentWeapon.NoiseScale = val;
			RestartPreviewProjectile();
		}, "0.0", 140f);

		AddAssetFilterDropdown(
			scrollBody,
			TranslationServer.Translate("Noise Texture"),
			_currentWeapon.NoiseTexture ?? "",
			(all) => ScanAvailableAssets("noise", all),
			(val) =>
			{
				if (_isUpdatingUI) return;
				_currentWeapon.NoiseTexture = val;
				RestartPreviewProjectile();
			},
			TranslationServer.Translate("Select imported noise texture..."),
			140f
		);

		AddVector2Input(scrollBody, TranslationServer.Translate("UV Scroll 1 (X, Y)"), _currentWeapon.UvScrollSpeed1.ToGodotVector2(), (val) =>
		{
			if (_isUpdatingUI) return;
			_currentWeapon.UvScrollSpeed1 = val.ToVector2Data();
			RestartPreviewProjectile();
		}, 140f);

		AddVector2Input(scrollBody, TranslationServer.Translate("UV Scroll 2 (X, Y)"), _currentWeapon.UvScrollSpeed2.ToGodotVector2(), (val) =>
		{
			if (_isUpdatingUI) return;
			_currentWeapon.UvScrollSpeed2 = val.ToVector2Data();
			RestartPreviewProjectile();
		}, 140f);

		AddSlider(scrollBody, TranslationServer.Translate("Threshold Cutoff"), 0f, 1f, 0.05f, _currentWeapon.ThresholdCutoff, (val) =>
		{
			if (_isUpdatingUI) return;
			_currentWeapon.ThresholdCutoff = val;
			RestartPreviewProjectile();
		}, "0.00", 140f);

		AddSlider(scrollBody, TranslationServer.Translate("Threshold Smoothness"), 0.01f, 0.5f, 0.01f, _currentWeapon.ThresholdSmoothness > 0 ? _currentWeapon.ThresholdSmoothness : 0.1f, (val) =>
		{
			if (_isUpdatingUI) return;
			_currentWeapon.ThresholdSmoothness = val;
			RestartPreviewProjectile();
		}, "0.00", 140f);

		AddCheckBox(scrollBody, TranslationServer.Translate("Dynamic Point Light"), _currentWeapon.PointLightEnabled, (pressed) =>
		{
			if (_isUpdatingUI) return;
			_currentWeapon.PointLightEnabled = pressed;
			RestartPreviewProjectile();
		});

		AddColorPicker(scrollBody, TranslationServer.Translate("Light Color"), ParseColorSafe(_currentWeapon.PointLightColor, new Color(1.0f, 0.65f, 0.2f)), (c) =>
		{
			if (_isUpdatingUI) return;
			_currentWeapon.PointLightColor = "#" + c.ToHtml(false);
			RestartPreviewProjectile();
		}, 140f);

		AddSlider(scrollBody, TranslationServer.Translate("Light Intensity"), 0f, 10f, 0.5f, _currentWeapon.PointLightIntensity, (val) =>
		{
			if (_isUpdatingUI) return;
			_currentWeapon.PointLightIntensity = val;
			RestartPreviewProjectile();
		}, "0.0", 140f);

		AddSlider(scrollBody, TranslationServer.Translate("Light Range"), 0.5f, 30f, 0.5f, _currentWeapon.PointLightRange > 0 ? _currentWeapon.PointLightRange : 6.0f, (val) =>
		{
			if (_isUpdatingUI) return;
			_currentWeapon.PointLightRange = val;
			RestartPreviewProjectile();
		}, "0.0", 140f);

		// SECTION 4: RIBBON TRAIL EMITTER
		AddSectionHeader(scrollBody, "🎗️ " + TranslationServer.Translate("RIBBON TRAIL EMITTER"), new Color(0.85f, 0.85f, 0.6f));

		AddAssetFilterDropdown(
			scrollBody,
			TranslationServer.Translate("Ribbon Texture"),
			_currentWeapon.RibbonTexture ?? "",
			(all) => ScanAvailableAssets("ribbons", all),
			(val) =>
			{
				if (_isUpdatingUI) return;
				_currentWeapon.RibbonTexture = val;
				RestartPreviewProjectile();
			},
			TranslationServer.Translate("Select imported ribbon texture..."),
			140f
		);

		AddColorPicker(scrollBody, TranslationServer.Translate("Ribbon Color"), ParseColorSafe(_currentWeapon.RibbonColor, new Color(1.0f, 0.65f, 0.2f)), (c) =>
		{
			if (_isUpdatingUI) return;
			_currentWeapon.RibbonColor = "#" + c.ToHtml(false);
			RestartPreviewProjectile();
		}, 140f);

		AddSlider(scrollBody, TranslationServer.Translate("Ribbon Width"), 0.05f, 2.0f, 0.05f, _currentWeapon.RibbonWidth > 0 ? _currentWeapon.RibbonWidth : 0.4f, (val) =>
		{
			if (_isUpdatingUI) return;
			_currentWeapon.RibbonWidth = val;
			RestartPreviewProjectile();
		}, "0.00", 140f);

		AddSlider(scrollBody, TranslationServer.Translate("Ribbon Lifetime (s)"), 0.05f, 3.0f, 0.05f, _currentWeapon.RibbonLifetime > 0 ? _currentWeapon.RibbonLifetime : 0.5f, (val) =>
		{
			if (_isUpdatingUI) return;
			_currentWeapon.RibbonLifetime = val;
			RestartPreviewProjectile();
		}, "0.00", 140f);

		AddCheckBox(scrollBody, TranslationServer.Translate("Taper Tail"), _currentWeapon.RibbonTaper, (pressed) =>
		{
			if (_isUpdatingUI) return;
			_currentWeapon.RibbonTaper = pressed;
			RestartPreviewProjectile();
		});

		AddCheckBox(scrollBody, TranslationServer.Translate("Additive Blend"), _currentWeapon.RibbonAdditive, (pressed) =>
		{
			if (_isUpdatingUI) return;
			_currentWeapon.RibbonAdditive = pressed;
			RestartPreviewProjectile();
		});

		AddVector3Input(scrollBody, TranslationServer.Translate("Trail Offset"), _currentWeapon.TrailOffset.ToGodotVector3(), (val) =>
		{
			if (_isUpdatingUI) return;
			_currentWeapon.TrailOffset = val.ToVector3Data();
			RestartPreviewProjectile();
		}, 140f);
	}

	public void OpenForWeapon(string weaponId, WeaponMetadata weapon, Action<WeaponMetadata> onApplied = null)
	{
		string effectiveId = !string.IsNullOrWhiteSpace(weapon?.TemplateID) ? weapon.TemplateID : weaponId;
		var (_, parsedSlug) = TemplateIDHelper.ParseTemplateID(effectiveId);
		_slug = !string.IsNullOrWhiteSpace(parsedSlug) ? TemplateIDHelper.ToSnakeCase(parsedSlug) : TemplateIDHelper.ToSnakeCase(effectiveId);
		_weaponId = TemplateIDHelper.NormalizeTemplateID("weapon", _slug);
		_initialWeapon = weapon ?? new WeaponMetadata();
		_currentWeapon = weapon ?? new WeaponMetadata();
		_currentWeapon.TemplateID = _weaponId;
		if (_txtSlug != null)
		{
			_isUpdatingUI = true;
			_txtSlug.Text = _slug;
			_isUpdatingUI = false;
		}
		if (!string.IsNullOrEmpty(_currentWeapon.ProjectileModelPath) &&
			!_currentWeapon.ProjectileModelPath.EndsWith(".rmesh", StringComparison.OrdinalIgnoreCase) &&
			!_currentWeapon.ProjectileModelPath.StartsWith("vfx:", StringComparison.OrdinalIgnoreCase))
		{
			_currentWeapon.ProjectileModelPath = "";
		}
		if (!string.IsNullOrEmpty(_currentWeapon.RibbonTexture) &&
			!_currentWeapon.RibbonTexture.EndsWith(".rtex", StringComparison.OrdinalIgnoreCase))
		{
			_currentWeapon.RibbonTexture = "";
		}
		if (!string.IsNullOrEmpty(_currentWeapon.NoiseTexture) &&
			!_currentWeapon.NoiseTexture.EndsWith(".rtex", StringComparison.OrdinalIgnoreCase))
		{
			_currentWeapon.NoiseTexture = "";
		}
		_onAppliedCallback = onApplied;
		_isPlaybackPaused = false;

		TitleLabel.Text = $"{TranslationServer.Translate("Weapon VFX & Audio")} - {(!string.IsNullOrEmpty(weapon?.Name) ? weapon.Name : _weaponId)}";

		OpenDialog();
		ResetCameraDefault();
		RestartPreviewProjectile();
	}

	public void SetPlaybackPaused(bool paused)
	{
		_isPlaybackPaused = paused;
		if (_previewProjectile != null && GodotObject.IsInstanceValid(_previewProjectile))
		{
			_previewProjectile.IsPaused = paused;
		}
		if (!paused && (_previewProjectile == null || !GodotObject.IsInstanceValid(_previewProjectile)))
		{
			RestartPreviewProjectile();
		}
	}

	public void StepFrame(float deltaSeconds)
	{
		SetPlaybackPaused(true);
		if (_previewProjectile == null || !GodotObject.IsInstanceValid(_previewProjectile))
		{
			RestartPreviewProjectile();
		}
		_previewProjectile?.StepSimulation(deltaSeconds);
	}

	private void ApplyShaderPreset(string preset)
	{
		switch (preset)
		{
			case "fire":
				_currentWeapon.BaseColor = "#261e19";
				_currentWeapon.EmissionColor = "#ff6600";
				_currentWeapon.EmissionEnergy = 4.0f;
				_currentWeapon.FresnelColor = "#ff9933";
				_currentWeapon.FresnelFactor = 1.5f;
				_currentWeapon.PointLightColor = "#ffaa33";
				_currentWeapon.RibbonColor = "#ff6600";
				break;
			case "frost":
				_currentWeapon.BaseColor = "#142838";
				_currentWeapon.EmissionColor = "#00b4ff";
				_currentWeapon.EmissionEnergy = 3.5f;
				_currentWeapon.FresnelColor = "#8ee5ff";
				_currentWeapon.FresnelFactor = 1.6f;
				_currentWeapon.PointLightColor = "#00b4ff";
				_currentWeapon.RibbonColor = "#00b4ff";
				break;
			case "poison":
				_currentWeapon.BaseColor = "#142814";
				_currentWeapon.EmissionColor = "#00ff3c";
				_currentWeapon.EmissionEnergy = 3.5f;
				_currentWeapon.FresnelColor = "#8effaa";
				_currentWeapon.FresnelFactor = 1.4f;
				_currentWeapon.PointLightColor = "#00ff3c";
				_currentWeapon.RibbonColor = "#00ff3c";
				break;
			case "arcane":
				_currentWeapon.BaseColor = "#261438";
				_currentWeapon.EmissionColor = "#b400ff";
				_currentWeapon.EmissionEnergy = 4.5f;
				_currentWeapon.FresnelColor = "#e599ff";
				_currentWeapon.FresnelFactor = 1.8f;
				_currentWeapon.PointLightColor = "#b400ff";
				_currentWeapon.RibbonColor = "#b400ff";
				break;
			case "holy":
				_currentWeapon.BaseColor = "#383214";
				_currentWeapon.EmissionColor = "#ffdc00";
				_currentWeapon.EmissionEnergy = 5.0f;
				_currentWeapon.FresnelColor = "#fff28e";
				_currentWeapon.FresnelFactor = 2.0f;
				_currentWeapon.PointLightColor = "#ffdc00";
				_currentWeapon.RibbonColor = "#ffdc00";
				break;
		}
		RestartPreviewProjectile();
	}

	public void RestartPreviewProjectile()
	{
		if (_previewProjectile != null && GodotObject.IsInstanceValid(_previewProjectile))
		{
			_previewProjectile.QueueFree();
			_previewProjectile = null;
		}

		if (PreviewSubViewport == null) return;

		_previewProjectile = new VisualProjectile3D();
		PreviewSubViewport.AddChild(_previewProjectile);

		Vector3 startPos = new Vector3(-2.8f, 0.5f, 0f);
		Vector3 targetPos = new Vector3(2.8f, 0.5f, 0f);

		_previewProjectile.Initialize(_currentWeapon, startPos, targetPos, default, (proj) =>
		{
			if (Visible && PreviewSubViewport != null)
			{
				Callable.From(() => RestartPreviewProjectile()).CallDeferred();
			}
		});

		_previewProjectile.TimeScale = _previewSpeed;
		_previewProjectile.IsPaused = _isPlaybackPaused;
	}

	private void PlaySound(string soundPath)
	{
		if (string.IsNullOrEmpty(soundPath)) return;
		try
		{
			AudioStream stream = null;
			if (soundPath.StartsWith("res://") || soundPath.StartsWith("user://"))
			{
				if (ResourceLoader.Exists(soundPath))
				{
					stream = GD.Load<AudioStream>(soundPath);
				}
			}
			else
			{
				string wsPath = ProjectSettings.GlobalizePath(MapEditorHUD.TempWorkspaceGodotPath);
				string fullPath = System.IO.Path.Combine(wsPath, "Assets", "audio", soundPath);
				if (!System.IO.File.Exists(fullPath))
				{
					fullPath = System.IO.Path.Combine(wsPath, soundPath);
				}
				if (!System.IO.File.Exists(fullPath))
				{
					fullPath = ProjectSettings.GlobalizePath($"res://Assets/Audio/UI/{soundPath}");
				}

				if (System.IO.File.Exists(fullPath))
				{
					if (fullPath.EndsWith(".raud", StringComparison.OrdinalIgnoreCase))
					{
						byte[] raudBytes = System.IO.File.ReadAllBytes(fullPath);
						byte[]? oggBytes = Realm.Shared.Audio.RaudFile.GetTrack(raudBytes, 0);
						if (oggBytes != null && oggBytes.Length > 0)
						{
							stream = AudioStreamOggVorbis.LoadFromBuffer(oggBytes);
						}
					}
					else if (fullPath.EndsWith(".ogg", StringComparison.OrdinalIgnoreCase))
					{
						stream = AudioStreamOggVorbis.LoadFromFile(fullPath);
					}
					else if (fullPath.EndsWith(".wav", StringComparison.OrdinalIgnoreCase))
					{
						stream = GD.Load<AudioStream>(fullPath);
					}
				}
			}

			if (stream != null && _sfxPlayer != null)
			{
				_sfxPlayer.Stream = stream;
				_sfxPlayer.Play();
			}
		}
		catch (Exception ex)
		{
			GD.PrintErr($"[WeaponVfxDialog] PlaySound error: {ex.Message}");
		}
	}

	private Color ParseColorSafe(string hex, Color fallback)
	{
		if (string.IsNullOrEmpty(hex)) return fallback;
		try
		{
			return Color.FromHtml(hex);
		}
		catch
		{
			return fallback;
		}
	}

	protected override void OnApply()
	{
		string finalSlug = !string.IsNullOrWhiteSpace(_txtSlug?.Text) ? TemplateIDHelper.ToSnakeCase(_txtSlug.Text) : _slug;
		if (string.IsNullOrWhiteSpace(finalSlug)) finalSlug = _slug;
		_slug = finalSlug;
		_weaponId = TemplateIDHelper.NormalizeTemplateID("weapon", _slug);
		_currentWeapon.TemplateID = _weaponId;

		if (GameHost.Instance != null && !string.IsNullOrEmpty(_weaponId))
		{
			GameHost.WeaponRegistry[_weaponId] = _currentWeapon;
		}

		_onAppliedCallback?.Invoke(_currentWeapon);
		Hud?.ShowFeedback(TranslationServer.Translate("Weapon VFX applied successfully"));
		ClearPreviewProjectile();
	}

	protected override void OnCancel()
	{
		ClearPreviewProjectile();
	}

	public override void CloseDialog()
	{
		ClearPreviewProjectile();
		base.CloseDialog();
	}

	private void ClearPreviewProjectile()
	{
		if (_previewProjectile != null && GodotObject.IsInstanceValid(_previewProjectile))
		{
			_previewProjectile.QueueFree();
			_previewProjectile = null;
		}
	}
}
