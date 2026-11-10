// Controls -> widgets. One refresher per section mirrors of the Build* methods above: the panel is
// built once and then driven by these, so a field added to a section needs its line here too.

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
		SafeInvoke(_setAttackSoundValue, _currentWeapon.AttackSound);
		SafeInvoke(_setImpactSoundValue, _currentWeapon.ImpactSound);
		SafeInvoke(_setImpactVisualEffectValue, _currentWeapon.ImpactVisualEffect);
		SafeInvoke(_setProjectileModelValue, _currentWeapon.ProjectileModelPath);
		SafeInvoke(_setRibbonTextureValue, _currentWeapon.RibbonTexture);
		SafeInvoke(_setNoiseTextureValue, _currentWeapon.NoiseTexture);
	}

	private void SyncProjectileMovement()
	{
		UpdateDropdown(_optTrajectoryType, Trajectories, _currentWeapon.TrajectoryType, "Parabolic");
		UpdateSliderAndLabel(_sldBoomerangReturnDelay, _lblBoomerangReturnDelay, _currentWeapon.BoomerangReturnDelay, "0.00");
		UpdateSliderAndLabel(_sldOrbitRadius, _lblOrbitRadius, _currentWeapon.OrbitRadius, "0.0");
		UpdateSliderAndLabel(_sldOrbitSpeed, _lblOrbitSpeed, _currentWeapon.OrbitSpeed, "0.0");

		float speedVal = _currentWeapon.ProjectileSpeed;
		if (speedVal <= 0) speedVal = 25f;
		UpdateSliderAndLabel(_sldProjectileSpeed, _lblProjectileSpeed, speedVal, "0");

		UpdateSliderAndLabel(_sldAcceleration, _lblAcceleration, _currentWeapon.Acceleration, "0.0");
		UpdateDropdownLower(_optSpeedCurve, SpeedCurves, _currentWeapon.SpeedCurve, "constant");
		UpdateDropdownLower(_optEaseCurve, EaseCurves, _currentWeapon.EaseCurve, "linear");
		UpdateSliderAndLabel(_sldArcHeight, _lblArcHeight, _currentWeapon.ArcHeight, "0.0");
		UpdateSliderAndLabel(_sldHomingWeight, _lblHomingWeight, _currentWeapon.HomingWeight, "0.00");
		UpdateSliderAndLabel(_sldTurnRateLimit, _lblTurnRateLimit, _currentWeapon.TurnRateLimit, "0");
		UpdateSliderAndLabel(_sldMaxLifetime, _lblMaxLifetime, _currentWeapon.MaxLifetime, "0.0");
		UpdateSliderAndLabel(_sldFailsafeRange, _lblFailsafeRange, _currentWeapon.FailsafeRange, "0");
		UpdateDropdownLower(_optScaleCurve, ScaleCurves, _currentWeapon.ScaleCurve, "constant");
		UpdateCheckBox(_chkOrientToTrajectory, _currentWeapon.OrientToTrajectory);
		UpdateSliderAndLabel(_sldMaxBounces, _lblMaxBounces, _currentWeapon.MaxBounces, "0");
		UpdateSliderAndLabel(_sldPierceCount, _lblPierceCount, _currentWeapon.PierceCount, "0");
	}

	private void SyncAdvancedVectors()
	{
		UpdateVector3UI(_txtTumbleAngularVelocity, _currentWeapon.TumbleAngularVelocity.ToGodotVector3());
		UpdateVector2UI(_txtSpiral, new Vector2(_currentWeapon.SpiralRadius, _currentWeapon.SpiralFrequency));
		UpdateVector2UI(_txtZigzag, new Vector2(_currentWeapon.ZigzagAmplitude, _currentWeapon.ZigzagFrequency));
		int forwardAxisIdx = Math.Max(0, Array.IndexOf(ForwardAxes, _currentWeapon.ForwardAxisPreset?.Trim().ToUpperInvariant() ?? "-Z"));
		if (_optForwardAxisPreset != null) _optForwardAxisPreset.Selected = forwardAxisIdx;
		UpdateVector3UI(_txtMeshTranslationOffset, _currentWeapon.MeshTranslationOffset.ToGodotVector3());
		UpdateVector3UI(_txtMeshRotationOffset, _currentWeapon.MeshRotationOffset.ToGodotVector3());
		Vector3 scale = _currentWeapon.MeshScaleOffset.ToGodotVector3();
		if (scale == Vector3.Zero) scale = Vector3.One;
		UpdateVector3UI(_txtMeshScaleOffset, scale);
	}

	private void SyncVisualMaterials()
	{
		int maskIdx = Math.Max(0, Array.IndexOf(EmissionMasks, _currentWeapon.EmissionMaskSource?.ToLowerInvariant() ?? "noise"));
		if (_optEmissionMaskSource != null) _optEmissionMaskSource.Selected = maskIdx;
		UpdateColorPicker(_cpBaseColor, ParseColorSafe(_currentWeapon.BaseColor, new Color(0.15f, 0.12f, 0.1f)));
		UpdateColorPicker(_cpEmissionColor, ParseColorSafe(_currentWeapon.EmissionColor, new Color(1.0f, 0.4f, 0.0f)));
		UpdateSliderAndLabel(_sldEmissionEnergy, _lblEmissionEnergy, _currentWeapon.EmissionEnergy, "0.0");
		UpdateColorPicker(_cpFresnelColor, ParseColorSafe(_currentWeapon.FresnelColor, new Color(1.0f, 0.6f, 0.2f)));
		float fresnelPow = _currentWeapon.FresnelPower > 0 ? _currentWeapon.FresnelPower : 3.0f;
		UpdateSliderAndLabel(_sldFresnelPower, _lblFresnelPower, fresnelPow, "0.0");
		UpdateSliderAndLabel(_sldFresnelFactor, _lblFresnelFactor, _currentWeapon.FresnelFactor, "0.0");
		float noiseSc = _currentWeapon.NoiseScale > 0 ? _currentWeapon.NoiseScale : 3.0f;
		UpdateSliderAndLabel(_sldNoiseScale, _lblNoiseScale, noiseSc, "0.0");
		UpdateVector2UI(_txtUvScrollSpeed1, _currentWeapon.UvScrollSpeed1.ToGodotVector2());
		UpdateVector2UI(_txtUvScrollSpeed2, _currentWeapon.UvScrollSpeed2.ToGodotVector2());
		UpdateSliderAndLabel(_sldThresholdCutoff, _lblThresholdCutoff, _currentWeapon.ThresholdCutoff, "0.00");
		float threshSmooth = _currentWeapon.ThresholdSmoothness > 0 ? _currentWeapon.ThresholdSmoothness : 0.1f;
		UpdateSliderAndLabel(_sldThresholdSmoothness, _lblThresholdSmoothness, threshSmooth, "0.00");
		if (_chkPointLightEnabled != null) _chkPointLightEnabled.ButtonPressed = _currentWeapon.PointLightEnabled;
		UpdateColorPicker(_cpPointLightColor, ParseColorSafe(_currentWeapon.PointLightColor, new Color(1.0f, 0.65f, 0.2f)));
		UpdateSliderAndLabel(_sldPointLightIntensity, _lblPointLightIntensity, _currentWeapon.PointLightIntensity, "0.0");
		float lightRng = _currentWeapon.PointLightRange > 0 ? _currentWeapon.PointLightRange : 6.0f;
		UpdateSliderAndLabel(_sldPointLightRange, _lblPointLightRange, lightRng, "0.0");
	}

	private void SyncRibbonTrail()
	{
		Color ribbonCol = ParseColorSafe(_currentWeapon.RibbonColor, new Color(1.0f, 0.65f, 0.2f));
		if (_cpRibbonColor.Picker != null)
		{
			_cpRibbonColor.Picker.Color = ribbonCol;
			_cpRibbonColor.HueSlider.SetValueNoSignal(ribbonCol.H);
		}

		float ribWidth = _currentWeapon.RibbonWidth > 0 ? _currentWeapon.RibbonWidth : 0.4f;
		if (_sldRibbonWidth != null)
		{
			_sldRibbonWidth.Value = ribWidth;
			_lblRibbonWidth.Text = ribWidth.ToString("0.00");
		}

		float ribLife = _currentWeapon.RibbonLifetime > 0 ? _currentWeapon.RibbonLifetime : 0.5f;
		if (_sldRibbonLifetime != null)
		{
			_sldRibbonLifetime.Value = ribLife;
			_lblRibbonLifetime.Text = ribLife.ToString("0.00");
		}

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

}