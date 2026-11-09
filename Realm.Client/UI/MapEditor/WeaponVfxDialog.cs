using System;
using System.IO;
using System.Linq;
using Godot;
using Realm.Client.Core;
using Realm.Client.Services;
using Realm.Shared.Audio;

namespace Realm.Client.UI.MapEditor;

[GlobalClass]
public partial class WeaponVfxDialog : FloatingPreview3DDialogBase
{
	public WeaponVfxDialog() { }

	private static readonly string[] Trajectories = { "Parabolic", "Homing", "LinearVector", "Boomerang", "SwarmOrbit" };
	private static readonly string[] SpeedCurves = { "constant", "ease_in", "ease_out", "ease_in_out", "rocket_boost", "burst" };
	private static readonly string[] EaseCurves = { "linear", "ease_in", "ease_out", "ease_in_out" };
	private static readonly string[] ScaleCurves = { "constant", "grow", "shrink", "grow_shrink", "squash_stretch", "impact_shrink" };
	private static readonly string[] ForwardAxes = { "-Z", "+Z", "+X", "-X", "+Y", "-Y" };
	private static readonly string[] EmissionMasks = { "noise", "vertex_color", "fresnel", "texture_alpha" };

	private void SafeInvoke(Action<string>? action, string? val)
	{
		action?.Invoke(val ?? "");
	}

	private void UpdateSliderAndLabel(HSlider slider, Label label, float val, string fmt)
	{
		if (slider != null)
		{
			slider.Value = val;
			label.Text = val.ToString(fmt);
		}
	}

	private void UpdateColorPicker((ColorPickerButton Picker, HSlider HueSlider) cp, Color c)
	{
		if (cp.Picker != null)
		{
			cp.Picker.Color = c;
			cp.HueSlider.SetValueNoSignal(c.H);
		}
	}

	private void UpdateVector3UI((LineEdit X, LineEdit Y, LineEdit Z) ui, Vector3 val)
	{
		if (ui.X != null)
		{
			ui.X.Text = val.X.ToString("0.##");
			ui.Y.Text = val.Y.ToString("0.##");
			ui.Z.Text = val.Z.ToString("0.##");
		}
	}

	private void UpdateVector2UI((LineEdit X, LineEdit Y) ui, Vector2 val)
	{
		if (ui.X != null)
		{
			ui.X.Text = val.X.ToString("0.##");
			ui.Y.Text = val.Y.ToString("0.##");
		}
	}

	private void UpdateDropdown(OptionButton dropdown, string[] options, string? value, string fallback)
	{
		if (dropdown != null)
		{
			string? v = value;
			if (v == null) v = fallback;
			dropdown.Selected = Math.Max(0, Array.IndexOf(options, v));
		}
	}

	private void UpdateDropdownLower(OptionButton dropdown, string[] options, string? value, string fallback)
	{
		if (dropdown != null)
		{
			string? v = value;
			if (v != null) v = v.ToLowerInvariant();
			if (v == null) v = fallback;
			dropdown.Selected = Math.Max(0, Array.IndexOf(options, v));
		}
	}

	private void UpdateCheckBox(CheckBox chk, bool val)
	{
		if (chk != null) chk.ButtonPressed = val;
	}

	private AudioStreamPlayer _sfxPlayer = null!;
	private VisualProjectile3D? _previewProjectile;

	public VisualProjectile3D? PreviewProjectile => _previewProjectile;

	private WeaponMetadata _initialWeapon = new();
	private WeaponMetadata _currentWeapon = new();
	private string? _initialTemplateJson = null;
	private bool _templateExistedInitially = false;
	private string _weaponId = "";
	private string _slug = "";
	private Label _lblObjectTypePrefix = null!;
	private LineEdit _txtSlug = null!;
	private LineEdit _txtName = null!;
	private Action<WeaponMetadata>? _onAppliedCallback;
	private bool _isUpdatingUI;

	private Action<string> _setAttackSoundValue = null!;
	private Action<string> _setImpactSoundValue = null!;
	private Action<string> _setImpactVisualEffectValue = null!;
	private Action<string> _setProjectileModelValue = null!;
	private Action<string> _setRibbonTextureValue = null!;
	private Action<string> _setNoiseTextureValue = null!;

	private OptionButton _optTrajectoryType = null!;
	private HSlider _sldBoomerangReturnDelay = null!;
	private Label _lblBoomerangReturnDelay = null!;
	private HSlider _sldOrbitRadius = null!;
	private Label _lblOrbitRadius = null!;
	private HSlider _sldOrbitSpeed = null!;
	private Label _lblOrbitSpeed = null!;
	private HSlider _sldProjectileSpeed = null!;
	private Label _lblProjectileSpeed = null!;
	private HSlider _sldAcceleration = null!;
	private Label _lblAcceleration = null!;
	private OptionButton _optSpeedCurve = null!;
	private OptionButton _optEaseCurve = null!;
	private HSlider _sldArcHeight = null!;
	private Label _lblArcHeight = null!;
	private HSlider _sldHomingWeight = null!;
	private Label _lblHomingWeight = null!;
	private HSlider _sldTurnRateLimit = null!;
	private Label _lblTurnRateLimit = null!;
	private HSlider _sldMaxLifetime = null!;
	private Label _lblMaxLifetime = null!;
	private HSlider _sldFailsafeRange = null!;
	private Label _lblFailsafeRange = null!;
	private OptionButton _optScaleCurve = null!;
	private CheckBox _chkOrientToTrajectory = null!;
	private HSlider _sldMaxBounces = null!;
	private Label _lblMaxBounces = null!;
	private HSlider _sldPierceCount = null!;
	private Label _lblPierceCount = null!;
	private (LineEdit X, LineEdit Y, LineEdit Z) _txtTumbleAngularVelocity;
	private (LineEdit X, LineEdit Y) _txtSpiral;
	private (LineEdit X, LineEdit Y) _txtZigzag;
	private OptionButton _optForwardAxisPreset = null!;
	private (LineEdit X, LineEdit Y, LineEdit Z) _txtMeshTranslationOffset;
	private (LineEdit X, LineEdit Y, LineEdit Z) _txtMeshRotationOffset;
	private (LineEdit X, LineEdit Y, LineEdit Z) _txtMeshScaleOffset;

	private OptionButton _optEmissionMaskSource = null!;
	private (ColorPickerButton Picker, HSlider HueSlider) _cpBaseColor;
	private (ColorPickerButton Picker, HSlider HueSlider) _cpEmissionColor;
	private HSlider _sldEmissionEnergy = null!;
	private Label _lblEmissionEnergy = null!;
	private (ColorPickerButton Picker, HSlider HueSlider) _cpFresnelColor;
	private HSlider _sldFresnelPower = null!;
	private Label _lblFresnelPower = null!;
	private HSlider _sldFresnelFactor = null!;
	private Label _lblFresnelFactor = null!;
	private HSlider _sldNoiseScale = null!;
	private Label _lblNoiseScale = null!;
	private (LineEdit X, LineEdit Y) _txtUvScrollSpeed1;
	private (LineEdit X, LineEdit Y) _txtUvScrollSpeed2;
	private HSlider _sldThresholdCutoff = null!;
	private Label _lblThresholdCutoff = null!;
	private HSlider _sldThresholdSmoothness = null!;
	private Label _lblThresholdSmoothness = null!;
	private CheckBox _chkPointLightEnabled = null!;
	private (ColorPickerButton Picker, HSlider HueSlider) _cpPointLightColor;
	private HSlider _sldPointLightIntensity = null!;
	private Label _lblPointLightIntensity = null!;
	private HSlider _sldPointLightRange = null!;
	private Label _lblPointLightRange = null!;

	private (ColorPickerButton Picker, HSlider HueSlider) _cpRibbonColor;
	private HSlider _sldRibbonWidth = null!;
	private Label _lblRibbonWidth = null!;
	private HSlider _sldRibbonLifetime = null!;
	private Label _lblRibbonLifetime = null!;
	private CheckBox _chkRibbonTaper = null!;
	private CheckBox _chkRibbonAdditive = null!;
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

	public void OpenForWeapon(string weaponId, WeaponMetadata? weapon, Action<WeaponMetadata>? onApplied = null)
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

		string targetId = !string.IsNullOrEmpty(_weaponId) ? _weaponId : _slug;
		_initialTemplateJson = GameContext.EditorService.SnapshotTemplateJson(targetId);
		_templateExistedInitially = _initialTemplateJson != null;

		TitleLabel.Text = $"{TranslationServer.Translate("Weapon VFX & Audio")} - {(!string.IsNullOrEmpty(weapon?.Name) ? weapon.Name : _weaponId)}";

		OpenDialog();
		ResetCameraDefault();
		RestartPreviewProjectile();
	}

	protected override string GetRandomizeTemplateSlug()
	{
		return !string.IsNullOrEmpty(_weaponId) ? _weaponId : _slug;
	}

	protected override void OnAssetsRandomized()
	{
		string targetId = GetRandomizeTemplateSlug();
		string wsPath = MapWorkspaceService.GetActiveWorkspacePath();

		if (GameContext.MetadataService.TryLoadMetadata(wsPath, out var reloadedMeta) && reloadedMeta?.Templates?.Weapons != null)
		{
			var reloaded = reloadedMeta.Templates.Weapons.FirstOrDefault(w => string.Equals(w.TemplateID, targetId, StringComparison.OrdinalIgnoreCase) || string.Equals(w.TemplateID, _weaponId, StringComparison.OrdinalIgnoreCase));
			if (reloaded != null)
			{
				_currentWeapon = reloaded;
				CleanWeaponMetadataPaths(_currentWeapon);
			}
		}

		SyncControlsFromCurrentWeapon();
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

	/// <summary>The shader presets this dialog offers. Editor state only: never read from a file.</summary>
	private enum ShaderPreset
	{
		Fire,
		Frost,
		Poison,
		Arcane,
		Holy
	}

	private void ApplyShaderPreset(ShaderPreset preset)
	{
		switch (preset)
		{
			case ShaderPreset.Fire:
				_currentWeapon.BaseColor = "#261e19";
				_currentWeapon.EmissionColor = "#ff6600";
				_currentWeapon.EmissionEnergy = 4.0f;
				_currentWeapon.FresnelColor = "#ff9933";
				_currentWeapon.FresnelFactor = 1.5f;
				_currentWeapon.PointLightColor = "#ffaa33";
				_currentWeapon.RibbonColor = "#ff6600";
				break;
			case ShaderPreset.Frost:
				_currentWeapon.BaseColor = "#142838";
				_currentWeapon.EmissionColor = "#00b4ff";
				_currentWeapon.EmissionEnergy = 3.5f;
				_currentWeapon.FresnelColor = "#8ee5ff";
				_currentWeapon.FresnelFactor = 1.6f;
				_currentWeapon.PointLightColor = "#00b4ff";
				_currentWeapon.RibbonColor = "#00b4ff";
				break;
			case ShaderPreset.Poison:
				_currentWeapon.BaseColor = "#142814";
				_currentWeapon.EmissionColor = "#00ff3c";
				_currentWeapon.EmissionEnergy = 3.5f;
				_currentWeapon.FresnelColor = "#8effaa";
				_currentWeapon.FresnelFactor = 1.4f;
				_currentWeapon.PointLightColor = "#00ff3c";
				_currentWeapon.RibbonColor = "#00ff3c";
				break;
			case ShaderPreset.Arcane:
				_currentWeapon.BaseColor = "#261438";
				_currentWeapon.EmissionColor = "#b400ff";
				_currentWeapon.EmissionEnergy = 4.5f;
				_currentWeapon.FresnelColor = "#e599ff";
				_currentWeapon.FresnelFactor = 1.8f;
				_currentWeapon.PointLightColor = "#b400ff";
				_currentWeapon.RibbonColor = "#b400ff";
				break;
			case ShaderPreset.Holy:
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
		_previewProjectile.Name = "PreviewProjectile_VisualProjectile3D";
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
			AudioStream? stream = GetAudioStream(soundPath);

			if (stream != null && _sfxPlayer != null)
			{
				_sfxPlayer.Stream = stream;
				_sfxPlayer.Play();
			}
		}
		catch (Exception ex)
		{
			GameLog.LogError($"[WeaponVfxDialog] PlaySound error: {ex.Message}");
		}
	}

	private AudioStream? GetAudioStream(string soundPath)
	{
		if (soundPath.StartsWith("res://") || soundPath.StartsWith("user://"))
		{
			return ResourceLoader.Exists(soundPath) ? GD.Load<AudioStream>(soundPath) : null;
		}

		string wsPath = GameContext.EditorService._editingDirectory;
		string? fullPath = ResolveAudioFilePath(Path.Combine(wsPath, "Assets", "audio"), soundPath)
			?? ResolveAudioFilePath(wsPath, soundPath)
			?? ResolveAudioFilePath(ProjectSettings.GlobalizePath("res://Assets/audio"), soundPath)
			?? ResolveAudioFilePath(ProjectSettings.GlobalizePath("res://Assets/Audio/UI"), soundPath);

		if (string.IsNullOrEmpty(fullPath)) return null;

		return LoadStreamFromFile(fullPath);
	}

	private static string? ResolveAudioFilePath(string rootPath, string soundPath)
	{
		if (string.IsNullOrEmpty(rootPath) || !Directory.Exists(rootPath)) return null;

		string directPath = Path.Combine(rootPath, soundPath);
		if (File.Exists(directPath)) return directPath;

		string fileName = Path.GetFileName(soundPath);
		if (string.IsNullOrEmpty(fileName)) return null;

		foreach (string candidatePath in Directory.EnumerateFiles(rootPath, fileName, SearchOption.AllDirectories))
		{
			return candidatePath;
		}

		return null;
	}

	private AudioStream? LoadStreamFromFile(string fullPath)
	{
		if (fullPath.EndsWith(".raud", StringComparison.OrdinalIgnoreCase))
		{
			byte[] raudBytes = File.ReadAllBytes(fullPath);
			byte[]? oggBytes = RaudFile.GetTrack(raudBytes, 0);
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

	private Color ParseColorSafe(string? hex, Color fallback)
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
		_slug = TemplateIDHelper.ResolveSlug(_txtSlug?.Text, _slug);
		_weaponId = TemplateIDHelper.NormalizeTemplateID("weapon", _slug);
		_currentWeapon.TemplateID = _weaponId;
		if (_txtName != null)
		{
			_currentWeapon.Name = _txtName.Text;
		}

		if (GameContext.Host != null && !string.IsNullOrEmpty(_weaponId))
		{
			GameHost.WeaponRegistry[_weaponId] = _currentWeapon;
		}

		_onAppliedCallback?.Invoke(_currentWeapon);
		Hud?.ShowFeedback(TranslationServer.Translate("Weapon VFX applied successfully"));
		ClearPreviewProjectile();
	}

	protected override void OnCancel()
	{
		string targetId = !string.IsNullOrEmpty(_weaponId) ? _weaponId : _slug;
		if (!string.IsNullOrEmpty(targetId))
		{
			if (_templateExistedInitially)
			{
				GameContext.EditorService.RevertTemplateJson(targetId, _initialTemplateJson);
			}
			else
			{
				GameContext.EditorService.RevertTemplateJson(targetId, null);
			}
		}

		if (GameContext.Host != null && !string.IsNullOrEmpty(targetId))
		{
			if (_initialWeapon != null)
			{
				GameHost.WeaponRegistry[targetId] = _initialWeapon;
			}
			else
			{
				GameHost.WeaponRegistry.Remove(targetId);
			}
		}

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