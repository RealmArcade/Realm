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
	private LineEdit _txtName;
	private Action<WeaponMetadata> _onAppliedCallback;
	private bool _isUpdatingUI;

	private Action<string> _setAttackSoundValue;
	private Action<string> _setImpactSoundValue;
	private Action<string> _setImpactVisualEffectValue;
	private Action<string> _setProjectileModelValue;
	private Action<string> _setRibbonTextureValue;
	private Action<string> _setNoiseTextureValue;

	private OptionButton _optTrajectoryType;
	private HSlider _sldBoomerangReturnDelay;
	private Label _lblBoomerangReturnDelay;
	private HSlider _sldOrbitRadius;
	private Label _lblOrbitRadius;
	private HSlider _sldOrbitSpeed;
	private Label _lblOrbitSpeed;
	private HSlider _sldProjectileSpeed;
	private Label _lblProjectileSpeed;
	private HSlider _sldAcceleration;
	private Label _lblAcceleration;
	private OptionButton _optSpeedCurve;
	private OptionButton _optEaseCurve;
	private HSlider _sldArcHeight;
	private Label _lblArcHeight;
	private HSlider _sldHomingWeight;
	private Label _lblHomingWeight;
	private HSlider _sldTurnRateLimit;
	private Label _lblTurnRateLimit;
	private HSlider _sldMaxLifetime;
	private Label _lblMaxLifetime;
	private HSlider _sldFailsafeRange;
	private Label _lblFailsafeRange;
	private OptionButton _optScaleCurve;
	private CheckBox _chkOrientToTrajectory;
	private HSlider _sldMaxBounces;
	private Label _lblMaxBounces;
	private HSlider _sldPierceCount;
	private Label _lblPierceCount;
	private (LineEdit X, LineEdit Y, LineEdit Z) _txtTumbleAngularVelocity;
	private (LineEdit X, LineEdit Y) _txtSpiral;
	private (LineEdit X, LineEdit Y) _txtZigzag;
	private OptionButton _optForwardAxisPreset;
	private (LineEdit X, LineEdit Y, LineEdit Z) _txtMeshTranslationOffset;
	private (LineEdit X, LineEdit Y, LineEdit Z) _txtMeshRotationOffset;
	private (LineEdit X, LineEdit Y, LineEdit Z) _txtMeshScaleOffset;

	private OptionButton _optEmissionMaskSource;
	private (ColorPickerButton Picker, HSlider HueSlider) _cpBaseColor;
	private (ColorPickerButton Picker, HSlider HueSlider) _cpEmissionColor;
	private HSlider _sldEmissionEnergy;
	private Label _lblEmissionEnergy;
	private (ColorPickerButton Picker, HSlider HueSlider) _cpFresnelColor;
	private HSlider _sldFresnelPower;
	private Label _lblFresnelPower;
	private HSlider _sldFresnelFactor;
	private Label _lblFresnelFactor;
	private HSlider _sldNoiseScale;
	private Label _lblNoiseScale;
	private (LineEdit X, LineEdit Y) _txtUvScrollSpeed1;
	private (LineEdit X, LineEdit Y) _txtUvScrollSpeed2;
	private HSlider _sldThresholdCutoff;
	private Label _lblThresholdCutoff;
	private HSlider _sldThresholdSmoothness;
	private Label _lblThresholdSmoothness;
	private CheckBox _chkPointLightEnabled;
	private (ColorPickerButton Picker, HSlider HueSlider) _cpPointLightColor;
	private HSlider _sldPointLightIntensity;
	private Label _lblPointLightIntensity;
	private HSlider _sldPointLightRange;
	private Label _lblPointLightRange;

	private (ColorPickerButton Picker, HSlider HueSlider) _cpRibbonColor;
	private HSlider _sldRibbonWidth;
	private Label _lblRibbonWidth;
	private HSlider _sldRibbonLifetime;
	private Label _lblRibbonLifetime;
	private CheckBox _chkRibbonTaper;
	private CheckBox _chkRibbonAdditive;
	private (LineEdit X, LineEdit Y, LineEdit Z) _txtTrailOffset;

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

		BuildTopToolbars();

		var scrollBody = (VBoxContainer)CreateScrollBody(360);

		BuildIdentitySection(scrollBody);
		BuildAudioAndImpactSection(scrollBody);
		BuildProjectileMovementSection(scrollBody);
		BuildProceduralShaderSection(scrollBody);
		BuildRibbonTrailSection(scrollBody);
	}

	private void BuildTopToolbars()
	{
		var topToolbar = new HBoxContainer();
		topToolbar.AddThemeConstantOverride("separation", 4);

		AddButton(topToolbar, $"{UnicodeIcons.PLAY} " + TranslationServer.Translate("Fire Test"), () => RestartPreviewProjectile(), "Restart preview projectile", 10, new Vector2(0, 22));
		AddButton(topToolbar, $"{UnicodeIcons.COPY} " + TranslationServer.Translate("Copy Code"), () =>
		{
			string code = $"api.SpawnProjectile(\"{_weaponId}\", startPos, targetPos, {(_currentWeapon.ProjectileSpeed > 0 ? _currentWeapon.ProjectileSpeed : 25f):0.0}f);";
			DisplayServer.ClipboardSet(code);
			Hud?.ShowFeedback(TranslationServer.Translate("Copied script code to clipboard"));
		}, "Copy C# MapScript code reference to clipboard", 10, new Vector2(0, 22));

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
	}

	private void BuildIdentitySection(VBoxContainer scrollBody)
	{
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

		_txtName = AddTextInput(
			scrollBody,
			TranslationServer.Translate("Display Name:"),
			_currentWeapon.Name ?? "",
			(val) =>
			{
				if (_isUpdatingUI) return;
				_currentWeapon.Name = val;
			},
			TranslationServer.Translate("Weapon display name..."),
			140f
		);
	}

	private void BuildAudioAndImpactSection(VBoxContainer scrollBody)
	{
// SECTION 1: AUDIO & IMPACT EFFECTS
		AddSectionHeader(scrollBody, "🔊 " + TranslationServer.Translate("AUDIO & IMPACT EFFECTS"), new Color(0.3f, 0.8f, 0.7f));
		
		(_, _setAttackSoundValue) = AddAssetFilterDropdown(
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

		(_, _setImpactSoundValue) = AddAssetFilterDropdown(
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

		(_, _setImpactVisualEffectValue) = AddAssetFilterDropdown(
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
	}

	private void BuildProjectileMovementSection(VBoxContainer scrollBody)
	{
// SECTION 2: PROGRAMMATIC PROJECTILE MOVEMENT
		AddSectionHeader(scrollBody, "🚀 " + TranslationServer.Translate("PROJECTILE MOVEMENT"), new Color(0.35f, 0.6f, 0.85f));

		(_, _setProjectileModelValue) = AddAssetFilterDropdown(
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

		(_, _setRibbonTextureValue) = AddAssetFilterDropdown(
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

		(_, _setNoiseTextureValue) = AddAssetFilterDropdown(
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

		var rowRandomize = new HBoxContainer();
		rowRandomize.AddThemeConstantOverride("separation", 6);
		var btnRandomize = AddButton(
			rowRandomize,
			"🎲 " + TranslationServer.Translate("Randomize All"),
			() => RandomizeAllParameters(),
			"Pick random values for all visual configuration parameters below",
			11,
			new Vector2(0, 26)
		);
		btnRandomize.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
		scrollBody.AddChild(rowRandomize);

		string[] trajectories = new[] { "Parabolic", "Homing", "LinearVector", "Boomerang", "SwarmOrbit" };
		int trajIdx = Math.Max(0, Array.IndexOf(trajectories, _currentWeapon.TrajectoryType ?? "Parabolic"));
		_optTrajectoryType = AddOptionDropdown(scrollBody, TranslationServer.Translate("Trajectory Type"), trajectories, trajIdx, (idx) =>
		{
			if (_isUpdatingUI) return;
			_currentWeapon.TrajectoryType = trajectories[idx];
			RestartPreviewProjectile();
		}, 140f);

		(_sldBoomerangReturnDelay, _lblBoomerangReturnDelay) = AddSlider(scrollBody, TranslationServer.Translate("Boomerang Return Delay"), 0f, 2f, 0.05f, _currentWeapon.BoomerangReturnDelay, (val) =>
		{
			if (_isUpdatingUI) return;
			_currentWeapon.BoomerangReturnDelay = val;
		}, "0.00", 140f);

		(_sldOrbitRadius, _lblOrbitRadius) = AddSlider(scrollBody, TranslationServer.Translate("Orbit Radius"), 0.1f, 5f, 0.1f, _currentWeapon.OrbitRadius, (val) =>
		{
			if (_isUpdatingUI) return;
			_currentWeapon.OrbitRadius = val;
		}, "0.0", 140f);

		(_sldOrbitSpeed, _lblOrbitSpeed) = AddSlider(scrollBody, TranslationServer.Translate("Orbit Speed"), 1f, 30f, 0.5f, _currentWeapon.OrbitSpeed, (val) =>
		{
			if (_isUpdatingUI) return;
			_currentWeapon.OrbitSpeed = val;
		}, "0.0", 140f);

		(_sldProjectileSpeed, _lblProjectileSpeed) = AddSlider(scrollBody, TranslationServer.Translate("Speed (Units/s)"), 0f, 100f, 1f, _currentWeapon.ProjectileSpeed > 0 ? _currentWeapon.ProjectileSpeed : 25f, (val) =>
		{
			if (_isUpdatingUI) return;
			_currentWeapon.ProjectileSpeed = val;
			RestartPreviewProjectile();
		}, "0", 140f);

		(_sldAcceleration, _lblAcceleration) = AddSlider(scrollBody, TranslationServer.Translate("Acceleration"), -50f, 100f, 1f, _currentWeapon.Acceleration, (val) =>
		{
			if (_isUpdatingUI) return;
			_currentWeapon.Acceleration = val;
		}, "0.0", 140f);

		string[] speedCurves = new[] { "constant", "ease_in", "ease_out", "ease_in_out", "rocket_boost", "burst" };
		int speedCurveIdx = Math.Max(0, Array.IndexOf(speedCurves, _currentWeapon.SpeedCurve?.ToLowerInvariant() ?? "constant"));
		_optSpeedCurve = AddOptionDropdown(scrollBody, TranslationServer.Translate("Speed Curve"), speedCurves, speedCurveIdx, (idx) =>
		{
			if (_isUpdatingUI) return;
			_currentWeapon.SpeedCurve = speedCurves[idx];
		}, 140f);

		string[] easeCurves = new[] { "linear", "ease_in", "ease_out", "ease_in_out" };
		int easeCurveIdx = Math.Max(0, Array.IndexOf(easeCurves, _currentWeapon.EaseCurve?.ToLowerInvariant() ?? "linear"));
		_optEaseCurve = AddOptionDropdown(scrollBody, TranslationServer.Translate("Ease Curve"), easeCurves, easeCurveIdx, (idx) =>
		{
			if (_isUpdatingUI) return;
			_currentWeapon.EaseCurve = easeCurves[idx];
		}, 140f);

		(_sldArcHeight, _lblArcHeight) = AddSlider(scrollBody, TranslationServer.Translate("Arc Height"), 0f, 20f, 0.5f, _currentWeapon.ArcHeight, (val) =>
		{
			if (_isUpdatingUI) return;
			_currentWeapon.ArcHeight = val;
			RestartPreviewProjectile();
		}, "0.0", 140f);

		(_sldHomingWeight, _lblHomingWeight) = AddSlider(scrollBody, TranslationServer.Translate("Homing Weight"), 0f, 1f, 0.05f, _currentWeapon.HomingWeight, (val) =>
		{
			if (_isUpdatingUI) return;
			_currentWeapon.HomingWeight = val;
		}, "0.00", 140f);

		(_sldTurnRateLimit, _lblTurnRateLimit) = AddSlider(scrollBody, TranslationServer.Translate("Turn Rate (°/s)"), 0f, 720f, 15f, _currentWeapon.TurnRateLimit, (val) =>
		{
			if (_isUpdatingUI) return;
			_currentWeapon.TurnRateLimit = val;
		}, "0", 140f);

		(_sldMaxLifetime, _lblMaxLifetime) = AddSlider(scrollBody, TranslationServer.Translate("Max Lifetime (s)"), 0f, 15f, 0.5f, _currentWeapon.MaxLifetime, (val) =>
		{
			if (_isUpdatingUI) return;
			_currentWeapon.MaxLifetime = val;
		}, "0.0", 140f);

		(_sldFailsafeRange, _lblFailsafeRange) = AddSlider(scrollBody, TranslationServer.Translate("Failsafe Range"), 0f, 150f, 5f, _currentWeapon.FailsafeRange, (val) =>
		{
			if (_isUpdatingUI) return;
			_currentWeapon.FailsafeRange = val;
		}, "0", 140f);

		string[] scaleCurves = new[] { "constant", "grow", "shrink", "grow_shrink", "squash_stretch", "impact_shrink" };
		int scaleCurveIdx = Math.Max(0, Array.IndexOf(scaleCurves, _currentWeapon.ScaleCurve?.ToLowerInvariant() ?? "constant"));
		_optScaleCurve = AddOptionDropdown(scrollBody, TranslationServer.Translate("Scale over Lifetime"), scaleCurves, scaleCurveIdx, (idx) =>
		{
			if (_isUpdatingUI) return;
			_currentWeapon.ScaleCurve = scaleCurves[idx];
		}, 140f);

		_chkOrientToTrajectory = AddCheckBox(scrollBody, TranslationServer.Translate("Orient to Trajectory"), _currentWeapon.OrientToTrajectory, (pressed) =>
		{
			if (_isUpdatingUI) return;
			_currentWeapon.OrientToTrajectory = pressed;
			RestartPreviewProjectile();
		});

		(_sldMaxBounces, _lblMaxBounces) = AddSlider(scrollBody, TranslationServer.Translate("Max Bounces"), 0f, 10f, 1f, _currentWeapon.MaxBounces, (val) =>
		{
			if (_isUpdatingUI) return;
			_currentWeapon.MaxBounces = (int)val;
		}, "0", 140f);

		(_sldPierceCount, _lblPierceCount) = AddSlider(scrollBody, TranslationServer.Translate("Pierce Count"), 0f, 10f, 1f, _currentWeapon.PierceCount, (val) =>
		{
			if (_isUpdatingUI) return;
			_currentWeapon.PierceCount = (int)val;
		}, "0", 140f);

		_txtTumbleAngularVelocity = AddVector3Input(scrollBody, TranslationServer.Translate("Tumble Angular Vel"), _currentWeapon.TumbleAngularVelocity.ToGodotVector3(), (val) =>
		{
			if (_isUpdatingUI) return;
			_currentWeapon.TumbleAngularVelocity = val.ToVector3Data();
		}, 140f);

		_txtSpiral = AddVector2Input(scrollBody, TranslationServer.Translate("Spiral (Rad / Freq)"), new Vector2(_currentWeapon.SpiralRadius, _currentWeapon.SpiralFrequency), (val) =>
		{
			if (_isUpdatingUI) return;
			_currentWeapon.SpiralRadius = val.X;
			_currentWeapon.SpiralFrequency = val.Y;
		}, 140f);

		_txtZigzag = AddVector2Input(scrollBody, TranslationServer.Translate("Zigzag (Amp / Freq)"), new Vector2(_currentWeapon.ZigzagAmplitude, _currentWeapon.ZigzagFrequency), (val) =>
		{
			if (_isUpdatingUI) return;
			_currentWeapon.ZigzagAmplitude = val.X;
			_currentWeapon.ZigzagFrequency = val.Y;
		}, 140f);

		string[] forwardAxes = new[] { "-Z", "+Z", "+X", "-X", "+Y", "-Y" };
		int forwardAxisIdx = Math.Max(0, Array.IndexOf(forwardAxes, _currentWeapon.ForwardAxisPreset?.Trim().ToUpperInvariant() ?? "-Z"));
		_optForwardAxisPreset = AddOptionDropdown(scrollBody, TranslationServer.Translate("Forward Axis"), forwardAxes, forwardAxisIdx, (idx) =>
		{
			if (_isUpdatingUI) return;
			_currentWeapon.ForwardAxisPreset = forwardAxes[idx];
			RestartPreviewProjectile();
		}, 140f);

		_txtMeshTranslationOffset = AddVector3Input(scrollBody, TranslationServer.Translate("Mesh Translation Offset"), _currentWeapon.MeshTranslationOffset.ToGodotVector3(), (val) =>
		{
			if (_isUpdatingUI) return;
			_currentWeapon.MeshTranslationOffset = val.ToVector3Data();
			RestartPreviewProjectile();
		}, 140f);

		_txtMeshRotationOffset = AddVector3Input(scrollBody, TranslationServer.Translate("Mesh Rotation Offset"), _currentWeapon.MeshRotationOffset.ToGodotVector3(), (val) =>
		{
			if (_isUpdatingUI) return;
			_currentWeapon.MeshRotationOffset = val.ToVector3Data();
			RestartPreviewProjectile();
		}, 140f);

		Vector3 meshScale = _currentWeapon.MeshScaleOffset.ToGodotVector3();
		_txtMeshScaleOffset = AddVector3Input(scrollBody, TranslationServer.Translate("Mesh Scale Offset"), meshScale == Vector3.Zero ? Vector3.One : meshScale, (val) =>
		{
			if (_isUpdatingUI) return;
			_currentWeapon.MeshScaleOffset = val.ToVector3Data();
			RestartPreviewProjectile();
		}, 140f);
	}

	private void BuildProceduralShaderSection(VBoxContainer scrollBody)
	{
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
		_optEmissionMaskSource = AddOptionDropdown(scrollBody, TranslationServer.Translate("Emission Mask"), emissionMasks, maskIdx, (idx) =>
		{
			if (_isUpdatingUI) return;
			_currentWeapon.EmissionMaskSource = emissionMasks[idx];
			RestartPreviewProjectile();
		}, 140f);

		_cpBaseColor = AddColorPicker(scrollBody, TranslationServer.Translate("Base Color"), ParseColorSafe(_currentWeapon.BaseColor, new Color(0.15f, 0.12f, 0.1f)), (c) =>
		{
			if (_isUpdatingUI) return;
			_currentWeapon.BaseColor = "#" + c.ToHtml(false);
			RestartPreviewProjectile();
		}, 140f);

		_cpEmissionColor = AddColorPicker(scrollBody, TranslationServer.Translate("Emission Color"), ParseColorSafe(_currentWeapon.EmissionColor, new Color(1.0f, 0.4f, 0.0f)), (c) =>
		{
			if (_isUpdatingUI) return;
			_currentWeapon.EmissionColor = "#" + c.ToHtml(false);
			RestartPreviewProjectile();
		}, 140f);

		(_sldEmissionEnergy, _lblEmissionEnergy) = AddSlider(scrollBody, TranslationServer.Translate("Emission Energy"), 0f, 20f, 0.5f, _currentWeapon.EmissionEnergy, (val) =>
		{
			if (_isUpdatingUI) return;
			_currentWeapon.EmissionEnergy = val;
			RestartPreviewProjectile();
		}, "0.0", 140f);

		_cpFresnelColor = AddColorPicker(scrollBody, TranslationServer.Translate("Fresnel Color"), ParseColorSafe(_currentWeapon.FresnelColor, new Color(1.0f, 0.6f, 0.2f)), (c) =>
		{
			if (_isUpdatingUI) return;
			_currentWeapon.FresnelColor = "#" + c.ToHtml(false);
			RestartPreviewProjectile();
		}, 140f);

		(_sldFresnelPower, _lblFresnelPower) = AddSlider(scrollBody, TranslationServer.Translate("Fresnel Power"), 0.1f, 10f, 0.2f, _currentWeapon.FresnelPower > 0 ? _currentWeapon.FresnelPower : 3.0f, (val) =>
		{
			if (_isUpdatingUI) return;
			_currentWeapon.FresnelPower = val;
			RestartPreviewProjectile();
		}, "0.0", 140f);

		(_sldFresnelFactor, _lblFresnelFactor) = AddSlider(scrollBody, TranslationServer.Translate("Fresnel Factor"), 0.0f, 5.0f, 0.1f, _currentWeapon.FresnelFactor, (val) =>
		{
			if (_isUpdatingUI) return;
			_currentWeapon.FresnelFactor = val;
			RestartPreviewProjectile();
		}, "0.0", 140f);

		(_sldNoiseScale, _lblNoiseScale) = AddSlider(scrollBody, TranslationServer.Translate("Noise Scale"), 0.1f, 20f, 0.5f, _currentWeapon.NoiseScale > 0 ? _currentWeapon.NoiseScale : 3.0f, (val) =>
		{
			if (_isUpdatingUI) return;
			_currentWeapon.NoiseScale = val;
			RestartPreviewProjectile();
		}, "0.0", 140f);

		_txtUvScrollSpeed1 = AddVector2Input(scrollBody, TranslationServer.Translate("UV Scroll 1 (X, Y)"), _currentWeapon.UvScrollSpeed1.ToGodotVector2(), (val) =>
		{
			if (_isUpdatingUI) return;
			_currentWeapon.UvScrollSpeed1 = val.ToVector2Data();
			RestartPreviewProjectile();
		}, 140f);

		_txtUvScrollSpeed2 = AddVector2Input(scrollBody, TranslationServer.Translate("UV Scroll 2 (X, Y)"), _currentWeapon.UvScrollSpeed2.ToGodotVector2(), (val) =>
		{
			if (_isUpdatingUI) return;
			_currentWeapon.UvScrollSpeed2 = val.ToVector2Data();
			RestartPreviewProjectile();
		}, 140f);

		(_sldThresholdCutoff, _lblThresholdCutoff) = AddSlider(scrollBody, TranslationServer.Translate("Threshold Cutoff"), 0f, 1f, 0.05f, _currentWeapon.ThresholdCutoff, (val) =>
		{
			if (_isUpdatingUI) return;
			_currentWeapon.ThresholdCutoff = val;
			RestartPreviewProjectile();
		}, "0.00", 140f);

		(_sldThresholdSmoothness, _lblThresholdSmoothness) = AddSlider(scrollBody, TranslationServer.Translate("Threshold Smoothness"), 0.01f, 0.5f, 0.01f, _currentWeapon.ThresholdSmoothness > 0 ? _currentWeapon.ThresholdSmoothness : 0.1f, (val) =>
		{
			if (_isUpdatingUI) return;
			_currentWeapon.ThresholdSmoothness = val;
			RestartPreviewProjectile();
		}, "0.00", 140f);

		_chkPointLightEnabled = AddCheckBox(scrollBody, TranslationServer.Translate("Dynamic Point Light"), _currentWeapon.PointLightEnabled, (pressed) =>
		{
			if (_isUpdatingUI) return;
			_currentWeapon.PointLightEnabled = pressed;
			RestartPreviewProjectile();
		});

		_cpPointLightColor = AddColorPicker(scrollBody, TranslationServer.Translate("Light Color"), ParseColorSafe(_currentWeapon.PointLightColor, new Color(1.0f, 0.65f, 0.2f)), (c) =>
		{
			if (_isUpdatingUI) return;
			_currentWeapon.PointLightColor = "#" + c.ToHtml(false);
			RestartPreviewProjectile();
		}, 140f);

		(_sldPointLightIntensity, _lblPointLightIntensity) = AddSlider(scrollBody, TranslationServer.Translate("Light Intensity"), 0f, 10f, 0.5f, _currentWeapon.PointLightIntensity, (val) =>
		{
			if (_isUpdatingUI) return;
			_currentWeapon.PointLightIntensity = val;
			RestartPreviewProjectile();
		}, "0.0", 140f);

		(_sldPointLightRange, _lblPointLightRange) = AddSlider(scrollBody, TranslationServer.Translate("Light Range"), 0.5f, 30f, 0.5f, _currentWeapon.PointLightRange > 0 ? _currentWeapon.PointLightRange : 6.0f, (val) =>
		{
			if (_isUpdatingUI) return;
			_currentWeapon.PointLightRange = val;
			RestartPreviewProjectile();
		}, "0.0", 140f);
	}

	private void BuildRibbonTrailSection(VBoxContainer scrollBody)
	{
// SECTION 4: RIBBON TRAIL EMITTER
		AddSectionHeader(scrollBody, "🎗️ " + TranslationServer.Translate("RIBBON TRAIL EMITTER"), new Color(0.85f, 0.85f, 0.6f));

		_cpRibbonColor = AddColorPicker(scrollBody, TranslationServer.Translate("Ribbon Color"), ParseColorSafe(_currentWeapon.RibbonColor, new Color(1.0f, 0.65f, 0.2f)), (c) =>
		{
			if (_isUpdatingUI) return;
			_currentWeapon.RibbonColor = "#" + c.ToHtml(false);
			RestartPreviewProjectile();
		}, 140f);

		(_sldRibbonWidth, _lblRibbonWidth) = AddSlider(scrollBody, TranslationServer.Translate("Ribbon Width"), 0.05f, 2.0f, 0.05f, _currentWeapon.RibbonWidth > 0 ? _currentWeapon.RibbonWidth : 0.4f, (val) =>
		{
			if (_isUpdatingUI) return;
			_currentWeapon.RibbonWidth = val;
			RestartPreviewProjectile();
		}, "0.00", 140f);

		(_sldRibbonLifetime, _lblRibbonLifetime) = AddSlider(scrollBody, TranslationServer.Translate("Ribbon Lifetime (s)"), 0.05f, 3.0f, 0.05f, _currentWeapon.RibbonLifetime > 0 ? _currentWeapon.RibbonLifetime : 0.5f, (val) =>
		{
			if (_isUpdatingUI) return;
			_currentWeapon.RibbonLifetime = val;
			RestartPreviewProjectile();
		}, "0.00", 140f);

		_chkRibbonTaper = AddCheckBox(scrollBody, TranslationServer.Translate("Taper Tail"), _currentWeapon.RibbonTaper, (pressed) =>
		{
			if (_isUpdatingUI) return;
			_currentWeapon.RibbonTaper = pressed;
			RestartPreviewProjectile();
		});

		_chkRibbonAdditive = AddCheckBox(scrollBody, TranslationServer.Translate("Additive Blend"), _currentWeapon.RibbonAdditive, (pressed) =>
		{
			if (_isUpdatingUI) return;
			_currentWeapon.RibbonAdditive = pressed;
			RestartPreviewProjectile();
		});

		_txtTrailOffset = AddVector3Input(scrollBody, TranslationServer.Translate("Trail Offset"), _currentWeapon.TrailOffset.ToGodotVector3(), (val) =>
		{
			if (_isUpdatingUI) return;
			_currentWeapon.TrailOffset = val.ToVector3Data();
			RestartPreviewProjectile();
		}, 140f);
	}

	private void SyncControlsFromCurrentWeapon()
	{
		_isUpdatingUI = true;
		try
		{
			SyncIdentityAndEffects();
			SyncProjectileMovement();
			SyncAdvancedVectors();
			SyncVisualMaterials();
			SyncRibbonTrail();
		}
		finally
		{
			_isUpdatingUI = false;
		}
	}

	private void SyncIdentityAndEffects()
	{
		if (_txtSlug != null) _txtSlug.Text = _slug;
		if (_txtName != null) _txtName.Text = _currentWeapon.Name ?? "";

		_setAttackSoundValue?.Invoke(_currentWeapon.AttackSound ?? "");
		_setImpactSoundValue?.Invoke(_currentWeapon.ImpactSound ?? "");
		_setImpactVisualEffectValue?.Invoke(_currentWeapon.ImpactVisualEffect ?? "");
		_setProjectileModelValue?.Invoke(_currentWeapon.ProjectileModelPath ?? "");
		_setRibbonTextureValue?.Invoke(_currentWeapon.RibbonTexture ?? "");
		_setNoiseTextureValue?.Invoke(_currentWeapon.NoiseTexture ?? "");
	}

	private void SyncProjectileMovement()
	{
		string[] trajectories = new[] { "Parabolic", "Homing", "LinearVector", "Boomerang", "SwarmOrbit" };
		int trajIdx = Math.Max(0, Array.IndexOf(trajectories, _currentWeapon.TrajectoryType ?? "Parabolic"));
		if (_optTrajectoryType != null) _optTrajectoryType.Selected = trajIdx;

		if (_sldBoomerangReturnDelay != null) { _sldBoomerangReturnDelay.Value = _currentWeapon.BoomerangReturnDelay; _lblBoomerangReturnDelay.Text = _currentWeapon.BoomerangReturnDelay.ToString("0.00"); }
		if (_sldOrbitRadius != null) { _sldOrbitRadius.Value = _currentWeapon.OrbitRadius; _lblOrbitRadius.Text = _currentWeapon.OrbitRadius.ToString("0.0"); }
		if (_sldOrbitSpeed != null) { _sldOrbitSpeed.Value = _currentWeapon.OrbitSpeed; _lblOrbitSpeed.Text = _currentWeapon.OrbitSpeed.ToString("0.0"); }
		float speedVal = _currentWeapon.ProjectileSpeed > 0 ? _currentWeapon.ProjectileSpeed : 25f;
		if (_sldProjectileSpeed != null) { _sldProjectileSpeed.Value = speedVal; _lblProjectileSpeed.Text = speedVal.ToString("0"); }
		if (_sldAcceleration != null) { _sldAcceleration.Value = _currentWeapon.Acceleration; _lblAcceleration.Text = _currentWeapon.Acceleration.ToString("0.0"); }

		string[] speedCurves = new[] { "constant", "ease_in", "ease_out", "ease_in_out", "rocket_boost", "burst" };
		int speedCurveIdx = Math.Max(0, Array.IndexOf(speedCurves, _currentWeapon.SpeedCurve?.ToLowerInvariant() ?? "constant"));
		if (_optSpeedCurve != null) _optSpeedCurve.Selected = speedCurveIdx;

		string[] easeCurves = new[] { "linear", "ease_in", "ease_out", "ease_in_out" };
		int easeCurveIdx = Math.Max(0, Array.IndexOf(easeCurves, _currentWeapon.EaseCurve?.ToLowerInvariant() ?? "linear"));
		if (_optEaseCurve != null) _optEaseCurve.Selected = easeCurveIdx;

		if (_sldArcHeight != null) { _sldArcHeight.Value = _currentWeapon.ArcHeight; _lblArcHeight.Text = _currentWeapon.ArcHeight.ToString("0.0"); }
		if (_sldHomingWeight != null) { _sldHomingWeight.Value = _currentWeapon.HomingWeight; _lblHomingWeight.Text = _currentWeapon.HomingWeight.ToString("0.00"); }
		if (_sldTurnRateLimit != null) { _sldTurnRateLimit.Value = _currentWeapon.TurnRateLimit; _lblTurnRateLimit.Text = _currentWeapon.TurnRateLimit.ToString("0"); }
		if (_sldMaxLifetime != null) { _sldMaxLifetime.Value = _currentWeapon.MaxLifetime; _lblMaxLifetime.Text = _currentWeapon.MaxLifetime.ToString("0.0"); }
		if (_sldFailsafeRange != null) { _sldFailsafeRange.Value = _currentWeapon.FailsafeRange; _lblFailsafeRange.Text = _currentWeapon.FailsafeRange.ToString("0"); }

		string[] scaleCurves = new[] { "constant", "grow", "shrink", "grow_shrink", "squash_stretch", "impact_shrink" };
		int scaleCurveIdx = Math.Max(0, Array.IndexOf(scaleCurves, _currentWeapon.ScaleCurve?.ToLowerInvariant() ?? "constant"));
		if (_optScaleCurve != null) _optScaleCurve.Selected = scaleCurveIdx;

		if (_chkOrientToTrajectory != null) _chkOrientToTrajectory.ButtonPressed = _currentWeapon.OrientToTrajectory;
		if (_sldMaxBounces != null) { _sldMaxBounces.Value = _currentWeapon.MaxBounces; _lblMaxBounces.Text = _currentWeapon.MaxBounces.ToString("0"); }
		if (_sldPierceCount != null) { _sldPierceCount.Value = _currentWeapon.PierceCount; _lblPierceCount.Text = _currentWeapon.PierceCount.ToString("0"); }
	}

	private void SyncAdvancedVectors()
	{
		if (_txtTumbleAngularVelocity.X != null)
		{
			var tumble = _currentWeapon.TumbleAngularVelocity.ToGodotVector3();
			_txtTumbleAngularVelocity.X.Text = tumble.X.ToString("0.##");
			_txtTumbleAngularVelocity.Y.Text = tumble.Y.ToString("0.##");
			_txtTumbleAngularVelocity.Z.Text = tumble.Z.ToString("0.##");
		}

		if (_txtSpiral.X != null)
		{
			_txtSpiral.X.Text = _currentWeapon.SpiralRadius.ToString("0.##");
			_txtSpiral.Y.Text = _currentWeapon.SpiralFrequency.ToString("0.##");
		}

		if (_txtZigzag.X != null)
		{
			_txtZigzag.X.Text = _currentWeapon.ZigzagAmplitude.ToString("0.##");
			_txtZigzag.Y.Text = _currentWeapon.ZigzagFrequency.ToString("0.##");
		}

		string[] forwardAxes = new[] { "-Z", "+Z", "+X", "-X", "+Y", "-Y" };
		int forwardAxisIdx = Math.Max(0, Array.IndexOf(forwardAxes, _currentWeapon.ForwardAxisPreset?.Trim().ToUpperInvariant() ?? "-Z"));
		if (_optForwardAxisPreset != null) _optForwardAxisPreset.Selected = forwardAxisIdx;

		if (_txtMeshTranslationOffset.X != null)
		{
			var trans = _currentWeapon.MeshTranslationOffset.ToGodotVector3();
			_txtMeshTranslationOffset.X.Text = trans.X.ToString("0.##");
			_txtMeshTranslationOffset.Y.Text = trans.Y.ToString("0.##");
			_txtMeshTranslationOffset.Z.Text = trans.Z.ToString("0.##");
		}

		if (_txtMeshRotationOffset.X != null)
		{
			var rot = _currentWeapon.MeshRotationOffset.ToGodotVector3();
			_txtMeshRotationOffset.X.Text = rot.X.ToString("0.##");
			_txtMeshRotationOffset.Y.Text = rot.Y.ToString("0.##");
			_txtMeshRotationOffset.Z.Text = rot.Z.ToString("0.##");
		}

		if (_txtMeshScaleOffset.X != null)
		{
			var scale = _currentWeapon.MeshScaleOffset.ToGodotVector3();
			if (scale == Vector3.Zero) scale = Vector3.One;
			_txtMeshScaleOffset.X.Text = scale.X.ToString("0.##");
			_txtMeshScaleOffset.Y.Text = scale.Y.ToString("0.##");
			_txtMeshScaleOffset.Z.Text = scale.Z.ToString("0.##");
		}
	}

	private void SyncVisualMaterials()
	{
		string[] emissionMasks = new[] { "noise", "vertex_color", "fresnel", "texture_alpha" };
		int maskIdx = Math.Max(0, Array.IndexOf(emissionMasks, _currentWeapon.EmissionMaskSource?.ToLowerInvariant() ?? "noise"));
		if (_optEmissionMaskSource != null) _optEmissionMaskSource.Selected = maskIdx;

		Color baseCol = ParseColorSafe(_currentWeapon.BaseColor, new Color(0.15f, 0.12f, 0.1f));
		if (_cpBaseColor.Picker != null) { _cpBaseColor.Picker.Color = baseCol; _cpBaseColor.HueSlider.SetValueNoSignal(baseCol.H); }

		Color emissionCol = ParseColorSafe(_currentWeapon.EmissionColor, new Color(1.0f, 0.4f, 0.0f));
		if (_cpEmissionColor.Picker != null) { _cpEmissionColor.Picker.Color = emissionCol; _cpEmissionColor.HueSlider.SetValueNoSignal(emissionCol.H); }

		if (_sldEmissionEnergy != null) { _sldEmissionEnergy.Value = _currentWeapon.EmissionEnergy; _lblEmissionEnergy.Text = _currentWeapon.EmissionEnergy.ToString("0.0"); }

		Color fresnelCol = ParseColorSafe(_currentWeapon.FresnelColor, new Color(1.0f, 0.6f, 0.2f));
		if (_cpFresnelColor.Picker != null) { _cpFresnelColor.Picker.Color = fresnelCol; _cpFresnelColor.HueSlider.SetValueNoSignal(fresnelCol.H); }

		float fresnelPow = _currentWeapon.FresnelPower > 0 ? _currentWeapon.FresnelPower : 3.0f;
		if (_sldFresnelPower != null) { _sldFresnelPower.Value = fresnelPow; _lblFresnelPower.Text = fresnelPow.ToString("0.0"); }

		if (_sldFresnelFactor != null) { _sldFresnelFactor.Value = _currentWeapon.FresnelFactor; _lblFresnelFactor.Text = _currentWeapon.FresnelFactor.ToString("0.0"); }

		float noiseSc = _currentWeapon.NoiseScale > 0 ? _currentWeapon.NoiseScale : 3.0f;
		if (_sldNoiseScale != null) { _sldNoiseScale.Value = noiseSc; _lblNoiseScale.Text = noiseSc.ToString("0.0"); }

		if (_txtUvScrollSpeed1.X != null)
		{
			var uv1 = _currentWeapon.UvScrollSpeed1.ToGodotVector2();
			_txtUvScrollSpeed1.X.Text = uv1.X.ToString("0.##");
			_txtUvScrollSpeed1.Y.Text = uv1.Y.ToString("0.##");
		}

		if (_txtUvScrollSpeed2.X != null)
		{
			var uv2 = _currentWeapon.UvScrollSpeed2.ToGodotVector2();
			_txtUvScrollSpeed2.X.Text = uv2.X.ToString("0.##");
			_txtUvScrollSpeed2.Y.Text = uv2.Y.ToString("0.##");
		}

		if (_sldThresholdCutoff != null) { _sldThresholdCutoff.Value = _currentWeapon.ThresholdCutoff; _lblThresholdCutoff.Text = _currentWeapon.ThresholdCutoff.ToString("0.00"); }
		float threshSmooth = _currentWeapon.ThresholdSmoothness > 0 ? _currentWeapon.ThresholdSmoothness : 0.1f;
		if (_sldThresholdSmoothness != null) { _sldThresholdSmoothness.Value = threshSmooth; _lblThresholdSmoothness.Text = threshSmooth.ToString("0.00"); }

		if (_chkPointLightEnabled != null) _chkPointLightEnabled.ButtonPressed = _currentWeapon.PointLightEnabled;

		Color lightCol = ParseColorSafe(_currentWeapon.PointLightColor, new Color(1.0f, 0.65f, 0.2f));
		if (_cpPointLightColor.Picker != null) { _cpPointLightColor.Picker.Color = lightCol; _cpPointLightColor.HueSlider.SetValueNoSignal(lightCol.H); }

		if (_sldPointLightIntensity != null) { _sldPointLightIntensity.Value = _currentWeapon.PointLightIntensity; _lblPointLightIntensity.Text = _currentWeapon.PointLightIntensity.ToString("0.0"); }
		float lightRng = _currentWeapon.PointLightRange > 0 ? _currentWeapon.PointLightRange : 6.0f;
		if (_sldPointLightRange != null) { _sldPointLightRange.Value = lightRng; _lblPointLightRange.Text = lightRng.ToString("0.0"); }
	}

	private void SyncRibbonTrail()
	{
		Color ribbonCol = ParseColorSafe(_currentWeapon.RibbonColor, new Color(1.0f, 0.65f, 0.2f));
		if (_cpRibbonColor.Picker != null) { _cpRibbonColor.Picker.Color = ribbonCol; _cpRibbonColor.HueSlider.SetValueNoSignal(ribbonCol.H); }

		float ribWidth = _currentWeapon.RibbonWidth > 0 ? _currentWeapon.RibbonWidth : 0.4f;
		if (_sldRibbonWidth != null) { _sldRibbonWidth.Value = ribWidth; _lblRibbonWidth.Text = ribWidth.ToString("0.00"); }

		float ribLife = _currentWeapon.RibbonLifetime > 0 ? _currentWeapon.RibbonLifetime : 0.5f;
		if (_sldRibbonLifetime != null) { _sldRibbonLifetime.Value = ribLife; _lblRibbonLifetime.Text = ribLife.ToString("0.00"); }

		if (_chkRibbonTaper != null) _chkRibbonTaper.ButtonPressed = _currentWeapon.RibbonTaper;
		if (_chkRibbonAdditive != null) _chkRibbonAdditive.ButtonPressed = _currentWeapon.RibbonAdditive;

		if (_txtTrailOffset.X != null)
		{
			var trail = _currentWeapon.TrailOffset.ToGodotVector3();
			_txtTrailOffset.X.Text = trail.X.ToString("0.##");
			_txtTrailOffset.Y.Text = trail.Y.ToString("0.##");
			_txtTrailOffset.Z.Text = trail.Z.ToString("0.##");
		}
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

		CleanWeaponMetadataPaths(_currentWeapon);

		SyncControlsFromCurrentWeapon();
		_onAppliedCallback = onApplied;
		_isPlaybackPaused = false;

		TitleLabel.Text = $"{TranslationServer.Translate("Weapon VFX & Audio")} - {(!string.IsNullOrEmpty(weapon?.Name) ? weapon.Name : _weaponId)}";

		OpenDialog();
		ResetCameraDefault();
		RestartPreviewProjectile();
	}

	private void CleanWeaponMetadataPaths(WeaponMetadata weapon)
	{
		if (!string.IsNullOrEmpty(weapon.ProjectileModelPath) &&
			!weapon.ProjectileModelPath.EndsWith(".rmesh", StringComparison.OrdinalIgnoreCase) &&
			!weapon.ProjectileModelPath.StartsWith("vfx:", StringComparison.OrdinalIgnoreCase))
		{
			weapon.ProjectileModelPath = "";
		}
		if (!string.IsNullOrEmpty(weapon.RibbonTexture) &&
			!weapon.RibbonTexture.EndsWith(".rtex", StringComparison.OrdinalIgnoreCase))
		{
			weapon.RibbonTexture = "";
		}
		if (!string.IsNullOrEmpty(weapon.NoiseTexture) &&
			!weapon.NoiseTexture.EndsWith(".rtex", StringComparison.OrdinalIgnoreCase))
		{
			weapon.NoiseTexture = "";
		}
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

	private void RandomizeAllParameters()
	{
		string[] trajectories = new[] { "Parabolic", "Homing", "LinearVector", "Boomerang", "SwarmOrbit" };
		_currentWeapon.TrajectoryType = trajectories[Random.Shared.Next(0, trajectories.Length)];

		_currentWeapon.BoomerangReturnDelay = (float)Math.Round(Random.Shared.NextDouble() * 2.0, 2);
		_currentWeapon.OrbitRadius = (float)Math.Round(Random.Shared.NextDouble() * (5.0 - 0.1) + 0.1, 1);
		_currentWeapon.OrbitSpeed = (float)Math.Round(Random.Shared.NextDouble() * (30.0 - 1.0) + 1.0, 1);
		_currentWeapon.ProjectileSpeed = (float)Math.Round(Random.Shared.NextDouble() * (60.0 - 10.0) + 10.0, 0);
		_currentWeapon.Acceleration = (float)Math.Round(Random.Shared.NextDouble() * 60.0 - 20.0, 1);

		string[] speedCurves = new[] { "constant", "ease_in", "ease_out", "ease_in_out", "rocket_boost", "burst" };
		_currentWeapon.SpeedCurve = speedCurves[Random.Shared.Next(0, speedCurves.Length)];

		string[] easeCurves = new[] { "linear", "ease_in", "ease_out", "ease_in_out" };
		_currentWeapon.EaseCurve = easeCurves[Random.Shared.Next(0, easeCurves.Length)];

		_currentWeapon.ArcHeight = (float)Math.Round(Random.Shared.NextDouble() * 10.0, 1);
		_currentWeapon.HomingWeight = (float)Math.Round(Random.Shared.NextDouble(), 2);
		_currentWeapon.TurnRateLimit = (float)Math.Round(Random.Shared.NextDouble() * (540.0 - 90.0) + 90.0, 0);
		_currentWeapon.MaxLifetime = (float)Math.Round(Random.Shared.NextDouble() * (8.0 - 1.0) + 1.0, 1);
		_currentWeapon.FailsafeRange = (float)Math.Round(Random.Shared.NextDouble() * (100.0 - 20.0) + 20.0, 0);

		string[] scaleCurves = new[] { "constant", "grow", "shrink", "grow_shrink", "squash_stretch", "impact_shrink" };
		_currentWeapon.ScaleCurve = scaleCurves[Random.Shared.Next(0, scaleCurves.Length)];
		_currentWeapon.OrientToTrajectory = Random.Shared.NextDouble() > 0.3;
		_currentWeapon.MaxBounces = Random.Shared.Next(0, 4);
		_currentWeapon.PierceCount = Random.Shared.Next(0, 4);

		_currentWeapon.TumbleAngularVelocity = new Vector3(
			(float)Math.Round((Random.Shared.NextDouble() * 2.0 - 1.0) * 10.0, 1),
			(float)Math.Round((Random.Shared.NextDouble() * 2.0 - 1.0) * 10.0, 1),
			(float)Math.Round((Random.Shared.NextDouble() * 2.0 - 1.0) * 10.0, 1)
		).ToVector3Data();

		_currentWeapon.SpiralRadius = (float)Math.Round(Random.Shared.NextDouble() * 2.0, 2);
		_currentWeapon.SpiralFrequency = (float)Math.Round(Random.Shared.NextDouble() * 8.0, 2);
		_currentWeapon.ZigzagAmplitude = (float)Math.Round(Random.Shared.NextDouble() * 2.0, 2);
		_currentWeapon.ZigzagFrequency = (float)Math.Round(Random.Shared.NextDouble() * 8.0, 2);

		string[] forwardAxes = new[] { "-Z", "+Z", "+X", "-X", "+Y", "-Y" };
		_currentWeapon.ForwardAxisPreset = forwardAxes[Random.Shared.Next(0, forwardAxes.Length)];

		_currentWeapon.MeshTranslationOffset = Vector3.Zero.ToVector3Data();
		_currentWeapon.MeshRotationOffset = Vector3.Zero.ToVector3Data();
		float scaleVal = (float)Math.Round(Random.Shared.NextDouble() * (1.8 - 0.6) + 0.6, 2);
		_currentWeapon.MeshScaleOffset = new Vector3(scaleVal, scaleVal, scaleVal).ToVector3Data();

		string[] emissionMasks = new[] { "noise", "vertex_color", "fresnel", "texture_alpha" };
		_currentWeapon.EmissionMaskSource = emissionMasks[Random.Shared.Next(0, emissionMasks.Length)];

		float baseHue = (float)Random.Shared.NextDouble();
		Color baseCol = Color.FromHsv((baseHue + 0.5f) % 1.0f, (float)(Random.Shared.NextDouble() * 0.4 + 0.2), (float)(Random.Shared.NextDouble() * 0.2 + 0.1));
		Color emissiveCol = Color.FromHsv(baseHue, (float)(Random.Shared.NextDouble() * 0.3 + 0.7), 1.0f);
		Color fresnelCol = Color.FromHsv((baseHue + 0.08f) % 1.0f, (float)(Random.Shared.NextDouble() * 0.4 + 0.5), 1.0f);
		Color lightCol = Color.FromHsv(baseHue, (float)(Random.Shared.NextDouble() * 0.3 + 0.7), 1.0f);
		Color ribbonCol = Color.FromHsv(baseHue, (float)(Random.Shared.NextDouble() * 0.3 + 0.7), 1.0f);

		_currentWeapon.BaseColor = "#" + baseCol.ToHtml(false);
		_currentWeapon.EmissionColor = "#" + emissiveCol.ToHtml(false);
		_currentWeapon.EmissionEnergy = (float)Math.Round(Random.Shared.NextDouble() * (8.0 - 1.0) + 1.0, 1);
		_currentWeapon.FresnelColor = "#" + fresnelCol.ToHtml(false);
		_currentWeapon.FresnelPower = (float)Math.Round(Random.Shared.NextDouble() * (6.0 - 1.0) + 1.0, 1);
		_currentWeapon.FresnelFactor = (float)Math.Round(Random.Shared.NextDouble() * (3.0 - 0.5) + 0.5, 1);
		_currentWeapon.NoiseScale = (float)Math.Round(Random.Shared.NextDouble() * (12.0 - 1.0) + 1.0, 1);

		_currentWeapon.UvScrollSpeed1 = new Vector2(
			(float)Math.Round((Random.Shared.NextDouble() * 2.0 - 1.0) * 3.0, 2),
			(float)Math.Round((Random.Shared.NextDouble() * 2.0 - 1.0) * 3.0, 2)
		).ToVector2Data();
		_currentWeapon.UvScrollSpeed2 = new Vector2(
			(float)Math.Round((Random.Shared.NextDouble() * 2.0 - 1.0) * 3.0, 2),
			(float)Math.Round((Random.Shared.NextDouble() * 2.0 - 1.0) * 3.0, 2)
		).ToVector2Data();

		_currentWeapon.ThresholdCutoff = (float)Math.Round(Random.Shared.NextDouble() * 0.6, 2);
		_currentWeapon.ThresholdSmoothness = (float)Math.Round(Random.Shared.NextDouble() * (0.3 - 0.02) + 0.02, 2);

		_currentWeapon.PointLightEnabled = Random.Shared.NextDouble() > 0.2;
		_currentWeapon.PointLightColor = "#" + lightCol.ToHtml(false);
		_currentWeapon.PointLightIntensity = (float)Math.Round(Random.Shared.NextDouble() * (6.0 - 1.0) + 1.0, 1);
		_currentWeapon.PointLightRange = (float)Math.Round(Random.Shared.NextDouble() * (12.0 - 3.0) + 3.0, 1);

		_currentWeapon.RibbonColor = "#" + ribbonCol.ToHtml(false);
		_currentWeapon.RibbonWidth = (float)Math.Round(Random.Shared.NextDouble() * (0.8 - 0.15) + 0.15, 2);
		_currentWeapon.RibbonLifetime = (float)Math.Round(Random.Shared.NextDouble() * (1.2 - 0.2) + 0.2, 2);
		_currentWeapon.RibbonTaper = Random.Shared.NextDouble() > 0.3;
		_currentWeapon.RibbonAdditive = Random.Shared.NextDouble() > 0.3;
		_currentWeapon.TrailOffset = Vector3.Zero.ToVector3Data();

		SyncControlsFromCurrentWeapon();
		RestartPreviewProjectile();
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
		SyncControlsFromCurrentWeapon();
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
			AudioStream stream = GetAudioStream(soundPath);

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

	private AudioStream GetAudioStream(string soundPath)
	{
		if (soundPath.StartsWith("res://") || soundPath.StartsWith("user://"))
		{
			return ResourceLoader.Exists(soundPath) ? GD.Load<AudioStream>(soundPath) : null;
		}

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

		if (!System.IO.File.Exists(fullPath)) return null;

		return LoadStreamFromFile(fullPath);
	}

	private AudioStream LoadStreamFromFile(string fullPath)
	{
		if (fullPath.EndsWith(".raud", StringComparison.OrdinalIgnoreCase))
		{
			byte[] raudBytes = System.IO.File.ReadAllBytes(fullPath);
			byte[]? oggBytes = Realm.Shared.Audio.RaudFile.GetTrack(raudBytes, 0);
			return (oggBytes != null && oggBytes.Length > 0) ? AudioStreamOggVorbis.LoadFromBuffer(oggBytes) : null;
		}
		
		if (fullPath.EndsWith(".ogg", StringComparison.OrdinalIgnoreCase))
		{
			return AudioStreamOggVorbis.LoadFromFile(fullPath);
		}
		
		if (fullPath.EndsWith(".wav", StringComparison.OrdinalIgnoreCase))
		{
			return GD.Load<AudioStream>(fullPath);
		}

		return null;
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
		if (_txtName != null)
		{
			_currentWeapon.Name = _txtName.Text;
		}

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
