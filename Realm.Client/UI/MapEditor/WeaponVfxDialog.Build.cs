// Scene construction: the two toolbars and every section of the scroll body, in the order they
// appear in the panel. Each Build* builds one section and returns nothing, because the section
// seats its widgets into fields the sync methods then refresh.

using System;
using System.IO;
using System.Linq;
using Godot;
using Realm.Client.Core;
using Realm.Client.Services;
using Realm.Shared.Audio;


namespace Realm.Client.UI.MapEditor;

public partial class WeaponVfxDialog
{
	private void BuildControls()
	{
		Add3DPreviewViewport(BodyContainer, new Vector2(460, 220));

		_sfxPlayer = new AudioStreamPlayer { Name = "WeaponPreviewSfx", Bus = AudioService.SfxBusName };
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
		topToolbar.Name = "TopToolbar_HBoxContainer";
		topToolbar.AddThemeConstantOverride("separation", 4);

		AddButton(topToolbar, $"{UnicodeIcons.PLAY} " + TranslationServer.Translate("Fire Test"), () => RestartPreviewProjectile(), "Restart preview projectile", 10, new Vector2(0, 22));
		AddButton(topToolbar, $"{UnicodeIcons.COPY} " + TranslationServer.Translate("Copy Code"), () =>
		{
			string code = $"api.SpawnProjectile(\"{_weaponId}\", startPos, targetPos, {(_currentWeapon.ProjectileSpeed > 0 ? _currentWeapon.ProjectileSpeed : 25f):0.0}f);";
			DisplayServer.ClipboardSet(code);
			Hud?.ShowFeedback(TranslationServer.Translate("Copied script code to clipboard"));
		}, "Copy C# MapScript code reference to clipboard", 10, new Vector2(0, 22));

		var separator = new Control { SizeFlagsHorizontal = SizeFlags.ExpandFill };
		topToolbar.AddChild(separator);

		AddCameraPresetToolbar(topToolbar, includeBack: false);

		BodyContainer.AddChild(topToolbar);

		var playbackToolbar = new HBoxContainer();
		playbackToolbar.Name = "PlaybackToolbar_HBoxContainer";
		playbackToolbar.AddThemeConstantOverride("separation", 4);

		AddButton(playbackToolbar, "▶ " + TranslationServer.Translate("Play"), () => SetPlaybackPaused(false), "Resume projectile animation", 10, new Vector2(0, 22));
		AddButton(playbackToolbar, "⏸ " + TranslationServer.Translate("Pause"), () => SetPlaybackPaused(true), "Pause projectile animation", 10, new Vector2(0, 22));
		AddButton(playbackToolbar, "⏮ " + TranslationServer.Translate("-1F"), () => StepFrame(-1f / 30f), "Step 1 frame backward (1/30s)", 10, new Vector2(0, 22));
		AddButton(playbackToolbar, "⏭ " + TranslationServer.Translate("+1F"), () => StepFrame(1f / 30f), "Step 1 frame forward (1/30s)", 10, new Vector2(0, 22));

		var speedLbl = new Label();
		speedLbl.Name = "SpeedLbl_Label";
		speedLbl.Text = TranslationServer.Translate("Speed:");
		speedLbl.AddThemeFontSizeOverride("font_size", 10);
		speedLbl.AddThemeColorOverride("font_color", UIStyle.ColorGoldDull);
		playbackToolbar.AddChild(speedLbl);

		var speedSlider = new HSlider();
		speedSlider.Name = "SpeedSlider_HSlider";
		speedSlider.MinValue = 0.1f;
		speedSlider.MaxValue = 3.0f;
		speedSlider.Step = 0.05f;
		speedSlider.Value = _previewSpeed;
		speedSlider.CustomMinimumSize = new Vector2(80, 0);
		speedSlider.SizeFlagsHorizontal = SizeFlags.ExpandFill;
		playbackToolbar.AddChild(speedSlider);

		var speedValLbl = new Label();
		speedValLbl.Name = "SpeedValLbl_Label";
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
		AddSectionHeader(scrollBody, "🆔 " + TranslationServer.Translate("IDENTITY"), new Color(0.95f, 0.8f, 0.4f));

		var rowId = BuildIdentityRow(scrollBody, "weapon/", out _lblObjectTypePrefix);

		_txtSlug = CreateSlugField("weapon_slug");
		_txtSlug.TextChanged += (val) =>
		{
			if (_isUpdatingUI) return;
			_slug = TemplateIDHelper.ToSnakeCase(val);
			_weaponId = TemplateIDHelper.NormalizeTemplateID("weapon", _slug);
			_currentWeapon.TemplateID = _weaponId;
		};
		rowId.AddChild(_txtSlug);
		

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
		BuildProjectileMovementSectionPart1(scrollBody);
		BuildProjectileMovementSectionPart2(scrollBody);
		BuildProjectileMovementSectionPart3(scrollBody);
		BuildProjectileMovementSectionPart4(scrollBody);
		BuildProjectileMovementSectionPart5(scrollBody);
		BuildProjectileMovementSectionPart6(scrollBody);
	}

	private void BuildProjectileMovementSectionPart1(VBoxContainer scrollBody)
	{
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
		rowRandomize.Name = "RowRandomize_HBoxContainer";
		rowRandomize.AddThemeConstantOverride("separation", 6);
		var btnRandomize = AddButton(
			rowRandomize,
			"🎲 " + TranslationServer.Translate("Randomize All"),
			() => RandomizeAllParameters(),
			"Pick random values for all visual configuration parameters below",
			11,
			new Vector2(0, 26)
		);
		btnRandomize.SizeFlagsHorizontal = SizeFlags.ExpandFill;
		scrollBody.AddChild(rowRandomize);
	}

	private void BuildProjectileMovementSectionPart2(VBoxContainer scrollBody)
	{
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
	}

	private void BuildProjectileMovementSectionPart3(VBoxContainer scrollBody)
	{
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
	}

	private void BuildProjectileMovementSectionPart4(VBoxContainer scrollBody)
	{
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
	}

	private void BuildProjectileMovementSectionPart5(VBoxContainer scrollBody)
	{
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
	}

	private void BuildProjectileMovementSectionPart6(VBoxContainer scrollBody)
	{
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
		BuildProceduralShaderSectionPart1(scrollBody);
		BuildProceduralShaderSectionPart2(scrollBody);
		BuildProceduralShaderSectionPart3(scrollBody);
		BuildProceduralShaderSectionPart4(scrollBody);
	}

	private void BuildProceduralShaderSectionPart1(VBoxContainer scrollBody)
	{
		AddSectionHeader(scrollBody, "🎨 " + TranslationServer.Translate("PROCEDURAL SURFACE UBER-SHADER"), new Color(0.8f, 0.55f, 0.45f));
		var presetFxRow = new HBoxContainer();
		presetFxRow.Name = "PresetFxRow_HBoxContainer";
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
	}

	private void BuildProceduralShaderSectionPart2(VBoxContainer scrollBody)
	{
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
	}

	private void BuildProceduralShaderSectionPart3(VBoxContainer scrollBody)
	{
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
	}

	private void BuildProceduralShaderSectionPart4(VBoxContainer scrollBody)
	{
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

}