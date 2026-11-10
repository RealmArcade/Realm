using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json.Nodes;
using Godot;
using Realm.Client.Core;
using Realm.Client.Services;
using Realm.Client.Utils;
using Realm.Client.VFX;

namespace Realm.Client.UI.MapEditor;

public partial class VfxStudioDialog
{
	private void UpdateUIBasicSettings()
	{
		var (prefix, slug) = TemplateIDHelper.ParseTemplateID(_currentConfig.VfxId);
		_slug = slug;
		_vfxId = TemplateIDHelper.NormalizeTemplateID("vfx", _slug);
		_currentConfig.VfxId = _vfxId;

		UpdateUIBasicTextInputs();
		UpdateUIBasicDropdowns();

		if (_setBaseTextureVal != null) _setBaseTextureVal(_currentConfig.BaseTexture ?? string.Empty);
		if (_setNoiseTextureVal != null) _setNoiseTextureVal(_currentConfig.NoiseTexture ?? string.Empty);
	}

	private void UpdateUIBasicTextInputs()
	{
		if (_lblObjectTypePrefix != null) _lblObjectTypePrefix.Text = "vfx/";
		if (_txtSlug != null) _txtSlug.Text = _slug;
		if (_txtVfxName != null) _txtVfxName.Text = _currentConfig.Name;
	}

	private void UpdateUIBasicDropdowns()
	{
		bool isParticle = _currentConfig.PrimitiveType == VfxPrimitiveType.ParticleSystem;
		if (_optMode != null) _optMode.Selected = isParticle ? 1 : 0;

		if (!isParticle && _optPrimitive != null)
		{
			int primIdx = Array.IndexOf(_primitiveTypes, _currentConfig.PrimitiveType);
			_optPrimitive.Selected = Math.Max(0, primIdx);
		}

		if (_optBlendMode != null) _optBlendMode.Selected = (int)_currentConfig.BlendMode;
		if (_optPlacementMode != null) _optPlacementMode.Selected = GetParticlePlacementModeIndex(_currentConfig.PlacementMode);
		if (_chkTintWithPlayerColor != null) _chkTintWithPlayerColor.ButtonPressed = _currentConfig.TintWithPlayerColor;
	}


	private void UpdateUIMaskSettings()
	{
		UpdateUILuminanceSettings();
		UpdateUIUvSettings();
		UpdateUIProceduralMotionSetting();
	}

	private void UpdateUIProceduralMotionSetting()
	{
		if (_optProceduralMotion == null) return;

		int motionIndex = _proceduralMotionIds.FindIndex(id => string.Equals(id, _currentConfig.ProceduralMotionId, StringComparison.OrdinalIgnoreCase));
		_optProceduralMotion.Selected = Math.Max(0, motionIndex);
	}

	private void UpdateUILuminanceSettings()
	{
		if (_chkLuminanceToAlpha != null) _chkLuminanceToAlpha.ButtonPressed = _currentConfig.LuminanceToAlpha;
		if (_sliderLuminanceThreshold != null) _sliderLuminanceThreshold.SetValueNoSignal(_currentConfig.LuminanceThreshold);
		if (_lblLuminanceThreshold != null) _lblLuminanceThreshold.Text = _currentConfig.LuminanceThreshold.ToString("0.00");
		if (_sliderLuminanceSmoothness != null) _sliderLuminanceSmoothness.SetValueNoSignal(_currentConfig.LuminanceSmoothness);
		if (_lblLuminanceSmoothness != null) _lblLuminanceSmoothness.Text = _currentConfig.LuminanceSmoothness.ToString("0.00");
		if (_chkUseGrayscale != null) _chkUseGrayscale.ButtonPressed = _currentConfig.UseGrayscale;
		if (_chkInvertMask != null) _chkInvertMask.ButtonPressed = _currentConfig.InvertMask;
		if (_sliderHighPassCutoff != null) _sliderHighPassCutoff.SetValueNoSignal(_currentConfig.HighPassCutoff);
		if (_lblHighPassCutoff != null) _lblHighPassCutoff.Text = _currentConfig.HighPassCutoff.ToString("0.00");
	}

	private void UpdateUIUvSettings()
	{
		UpdateBaseUvSettings();
		UpdateNoiseUvSettings();
	}

	private void UpdateBaseUvSettings()
	{
		if (_txtBaseUvScrollX != null) _txtBaseUvScrollX.Text = _currentConfig.BaseUvScroll.X.ToString("0.##");
		if (_txtBaseUvScrollY != null) _txtBaseUvScrollY.Text = _currentConfig.BaseUvScroll.Y.ToString("0.##");
		if (_txtBaseUvScaleX != null) _txtBaseUvScaleX.Text = _currentConfig.BaseUvScale.X.ToString("0.##");
		if (_txtBaseUvScaleY != null) _txtBaseUvScaleY.Text = _currentConfig.BaseUvScale.Y.ToString("0.##");
	}

	private void UpdateNoiseUvSettings()
	{
		if (_sliderDistortionStrength != null) _sliderDistortionStrength.SetValueNoSignal(_currentConfig.DistortionStrength);
		if (_lblDistortionStrength != null) _lblDistortionStrength.Text = _currentConfig.DistortionStrength.ToString("0.00");
		if (_txtNoiseUvScrollX != null) _txtNoiseUvScrollX.Text = _currentConfig.NoiseUvScroll.X.ToString("0.##");
		if (_txtNoiseUvScrollY != null) _txtNoiseUvScrollY.Text = _currentConfig.NoiseUvScroll.Y.ToString("0.##");
		if (_txtNoiseUvScaleX != null) _txtNoiseUvScaleX.Text = _currentConfig.NoiseUvScale.X.ToString("0.##");
		if (_txtNoiseUvScaleY != null) _txtNoiseUvScaleY.Text = _currentConfig.NoiseUvScale.Y.ToString("0.##");
	}


	private void UpdateUIColorSettings()
	{
		UpdateUIColorPickers();
		UpdateUIColorSliders();
	}

	private void UpdateUIColorPickers()
	{
		Color baseCol = VfxShaderManager.ParseColorSafe(_currentConfig.BaseColor, Colors.Orange);
		if (_sliderBaseColorHue != null) _sliderBaseColorHue.SetValueNoSignal(baseCol.H);
		if (_pickerBaseColor != null) _pickerBaseColor.Color = baseCol;

		Color secCol = VfxShaderManager.ParseColorSafe(_currentConfig.SecondaryColor, Colors.DarkRed);
		if (_sliderSecondaryColorHue != null) _sliderSecondaryColorHue.SetValueNoSignal(secCol.H);
		if (_pickerSecondaryColor != null) _pickerSecondaryColor.Color = secCol;

		Color coreCol = VfxShaderManager.ParseColorSafe(_currentConfig.CoreColor, Colors.White);
		if (_sliderCoreColorHue != null) _sliderCoreColorHue.SetValueNoSignal(coreCol.H);
		if (_pickerCoreColor != null) _pickerCoreColor.Color = coreCol;
	}

	private void UpdateUIColorSliders()
	{
		if (_sliderColorMixRatio != null) _sliderColorMixRatio.SetValueNoSignal(_currentConfig.ColorMixRatio);
		if (_lblColorMixRatio != null) _lblColorMixRatio.Text = _currentConfig.ColorMixRatio.ToString("0.00");

		if (_sliderEmissionBoost != null) _sliderEmissionBoost.SetValueNoSignal(_currentConfig.EmissionBoost);
		if (_lblEmissionBoost != null) _lblEmissionBoost.Text = _currentConfig.EmissionBoost.ToString("0.0");
		if (_sliderCoreThreshold != null) _sliderCoreThreshold.SetValueNoSignal(_currentConfig.CoreThreshold);
		if (_lblCoreThreshold != null) _lblCoreThreshold.Text = _currentConfig.CoreThreshold.ToString("0.00");
	}


	private void UpdateUIFalloffSettings()
	{
		UpdateUIRadialSettings();
		UpdateUIFadeSettings();
		UpdateUIFresnelSettings();
		UpdateUIDepthFadeSettings();
	}

	private void UpdateUIRadialSettings()
	{
		if (_chkRadialFalloff != null) _chkRadialFalloff.ButtonPressed = _currentConfig.EnableRadialFalloff;
		if (_sliderRadialFalloffStart != null) _sliderRadialFalloffStart.SetValueNoSignal(_currentConfig.RadialFalloffStart);
		if (_lblRadialFalloffStart != null) _lblRadialFalloffStart.Text = _currentConfig.RadialFalloffStart.ToString("0.00");
		if (_sliderRadialFalloffEnd != null) _sliderRadialFalloffEnd.SetValueNoSignal(_currentConfig.RadialFalloffEnd);
		if (_lblRadialFalloffEnd != null) _lblRadialFalloffEnd.Text = _currentConfig.RadialFalloffEnd.ToString("0.00");
	}

	private void UpdateUIFadeSettings()
	{
		if (_chkLengthFade != null) _chkLengthFade.ButtonPressed = _currentConfig.EnableLengthFade;
		if (_sliderLengthFadeStart != null) _sliderLengthFadeStart.SetValueNoSignal(_currentConfig.LengthFadeStart);
		if (_lblLengthFadeStart != null) _lblLengthFadeStart.Text = _currentConfig.LengthFadeStart.ToString("0.00");
		if (_sliderLengthFadeEnd != null) _sliderLengthFadeEnd.SetValueNoSignal(_currentConfig.LengthFadeEnd);
		if (_lblLengthFadeEnd != null) _lblLengthFadeEnd.Text = _currentConfig.LengthFadeEnd.ToString("0.00");
		if (_sliderErosionProgress != null) _sliderErosionProgress.SetValueNoSignal(_currentConfig.ErosionProgress);
		if (_lblErosionProgress != null) _lblErosionProgress.Text = _currentConfig.ErosionProgress.ToString("0.00");
	}

	private void UpdateUIFresnelSettings()
	{
		if (_chkFresnel != null) _chkFresnel.ButtonPressed = _currentConfig.EnableFresnel;
		if (_sliderFresnelPower != null) _sliderFresnelPower.SetValueNoSignal(_currentConfig.FresnelPower);
		if (_lblFresnelPower != null) _lblFresnelPower.Text = _currentConfig.FresnelPower.ToString("0.0");
		if (_sliderFresnelIntensity != null) _sliderFresnelIntensity.SetValueNoSignal(_currentConfig.FresnelIntensity);
		if (_lblFresnelIntensity != null) _lblFresnelIntensity.Text = _currentConfig.FresnelIntensity.ToString("0.0");
	}

	private void UpdateUIDepthFadeSettings()
	{
		if (_chkDepthFade != null) _chkDepthFade.ButtonPressed = _currentConfig.EnableDepthFade;
		if (_sliderDepthFadeDistance != null) _sliderDepthFadeDistance.SetValueNoSignal(_currentConfig.DepthFadeDistance);
		if (_lblDepthFadeDistance != null) _lblDepthFadeDistance.Text = _currentConfig.DepthFadeDistance.ToString("0.00");

		if (_sliderSurfaceNormalOffset != null) _sliderSurfaceNormalOffset.SetValueNoSignal(_currentConfig.SurfaceNormalOffset);
		if (_lblSurfaceNormalOffset != null) _lblSurfaceNormalOffset.Text = _currentConfig.SurfaceNormalOffset.ToString("0.000");
	}


	private void UpdateUIParticleSettings()
	{
		if (_currentConfig.ParticleConfig == null) return;

		UpdateUIParticleBasicSettings();
		UpdateUIParticleMotionSettings();
		UpdateUIParticleVisualSettings();
	}

	private void UpdateUIParticleBasicSettings()
	{
		var pc = Particle;
		UpdateUIParticleBasicShapeSettings(pc);
		UpdateUIParticleBasicAmountSettings(pc);
	}

	private void UpdateUIParticleBasicShapeSettings(SpellParticleConfig pc)
	{
		if (_optParticleShape != null) _optParticleShape.Selected = (int)pc.EmitterShape;
		if (_optParticleRenderMode != null) _optParticleRenderMode.Selected = (int)pc.RenderMode;
		if (_setParticleTextureVal != null) _setParticleTextureVal(pc.ParticleTexture ?? string.Empty);
		if (_setParticleMeshVal != null) _setParticleMeshVal(pc.MeshAssetPath ?? string.Empty);
	}

	private void UpdateUIParticleBasicAmountSettings(SpellParticleConfig pc)
	{
		if (_sliderParticleAmount != null) _sliderParticleAmount.SetValueNoSignal(pc.Amount);
		if (_lblParticleAmount != null) _lblParticleAmount.Text = pc.Amount.ToString();
		if (_sliderParticleLifetime != null) _sliderParticleLifetime.SetValueNoSignal(pc.Lifetime);
		if (_lblParticleLifetime != null) _lblParticleLifetime.Text = pc.Lifetime.ToString("0.0");
		if (_sliderParticleExplosiveness != null) _sliderParticleExplosiveness.SetValueNoSignal(pc.Explosiveness);
		if (_lblParticleExplosiveness != null) _lblParticleExplosiveness.Text = pc.Explosiveness.ToString("0.00");
		if (_chkParticleLocalCoords != null) _chkParticleLocalCoords.ButtonPressed = pc.LocalCoords;
	}

	private void UpdateUIParticleMotionSettings()
	{
		var pc = Particle;
		UpdateUIParticleMotionDirectionSettings(pc);
		UpdateUIParticleMotionGravitySettings(pc);
	}

	private void UpdateUIParticleMotionDirectionSettings(SpellParticleConfig pc)
	{
		if (_txtParticleDirX != null) _txtParticleDirX.Text = pc.Direction.X.ToString("0.##");
		if (_txtParticleDirY != null) _txtParticleDirY.Text = pc.Direction.Y.ToString("0.##");
		if (_txtParticleDirZ != null) _txtParticleDirZ.Text = pc.Direction.Z.ToString("0.##");

		if (_sliderParticleSpread != null) _sliderParticleSpread.SetValueNoSignal(pc.SpreadDegrees);
		if (_lblParticleSpread != null) _lblParticleSpread.Text = pc.SpreadDegrees.ToString("0");
		if (_sliderParticleVelMin != null) _sliderParticleVelMin.SetValueNoSignal(pc.InitialVelocityMin);
		if (_lblParticleVelMin != null) _lblParticleVelMin.Text = pc.InitialVelocityMin.ToString("0.0");
		if (_sliderParticleVelMax != null) _sliderParticleVelMax.SetValueNoSignal(pc.InitialVelocityMax);
		if (_lblParticleVelMax != null) _lblParticleVelMax.Text = pc.InitialVelocityMax.ToString("0.0");
	}

	private void UpdateUIParticleMotionGravitySettings(SpellParticleConfig pc)
	{
		if (_txtParticleGravX != null) _txtParticleGravX.Text = pc.Gravity.X.ToString("0.##");
		if (_txtParticleGravY != null) _txtParticleGravY.Text = pc.Gravity.Y.ToString("0.##");
		if (_txtParticleGravZ != null) _txtParticleGravZ.Text = pc.Gravity.Z.ToString("0.##");

		if (_sliderParticleDamping != null) _sliderParticleDamping.SetValueNoSignal(pc.Damping);
		if (_lblParticleDamping != null) _lblParticleDamping.Text = pc.Damping.ToString("0.0");
		if (_sliderParticleRadialAccel != null) _sliderParticleRadialAccel.SetValueNoSignal(pc.RadialAccel);
		if (_lblParticleRadialAccel != null) _lblParticleRadialAccel.Text = pc.RadialAccel.ToString("0.0");
		if (_sliderParticleTangentialAccel != null) _sliderParticleTangentialAccel.SetValueNoSignal(pc.TangentialAccel);
		if (_lblParticleTangentialAccel != null) _lblParticleTangentialAccel.Text = pc.TangentialAccel.ToString("0.0");
	}

	private void UpdateUIParticleVisualSettings()
	{
		var pc = Particle;
		UpdateUIParticleVisualScaleSettings(pc);
		UpdateUIParticleVisualColorSettings(pc);
	}

	private void UpdateUIParticleVisualScaleSettings(SpellParticleConfig pc)
	{
		if (_sliderParticleScaleMin != null) _sliderParticleScaleMin.SetValueNoSignal(pc.InitialScaleMin);
		if (_lblParticleScaleMin != null) _lblParticleScaleMin.Text = pc.InitialScaleMin.ToString("0.00");
		if (_sliderParticleScaleMax != null) _sliderParticleScaleMax.SetValueNoSignal(pc.InitialScaleMax);
		if (_lblParticleScaleMax != null) _lblParticleScaleMax.Text = pc.InitialScaleMax.ToString("0.00");
		if (_sliderParticleEndScaleRatio != null) _sliderParticleEndScaleRatio.SetValueNoSignal(pc.EndScaleRatio);
		if (_lblParticleEndScaleRatio != null) _lblParticleEndScaleRatio.Text = pc.EndScaleRatio.ToString("0.00");
	}

	private void UpdateUIParticleVisualColorSettings(SpellParticleConfig pc)
	{
		Color pStart = VfxShaderManager.ParseColorSafe(pc.ColorStart, Colors.Gold);
		if (_sliderParticleColorStartHue != null) _sliderParticleColorStartHue.SetValueNoSignal(pStart.H);
		if (_pickerParticleColorStart != null) _pickerParticleColorStart.Color = pStart;

		Color pMid = VfxShaderManager.ParseColorSafe(pc.ColorMid, Colors.DarkOrange);
		if (_sliderParticleColorMidHue != null) _sliderParticleColorMidHue.SetValueNoSignal(pMid.H);
		if (_pickerParticleColorMid != null) _pickerParticleColorMid.Color = pMid;

		Color pEnd = VfxShaderManager.ParseColorSafe(pc.ColorEnd, Colors.Maroon);
		if (_sliderParticleColorEndHue != null) _sliderParticleColorEndHue.SetValueNoSignal(pEnd.H);
		if (_pickerParticleColorEnd != null) _pickerParticleColorEnd.Color = pEnd;

		if (_sliderParticleEmissionEnergy != null) _sliderParticleEmissionEnergy.SetValueNoSignal(pc.EmissionEnergy);
		if (_lblParticleEmissionEnergy != null) _lblParticleEmissionEnergy.Text = pc.EmissionEnergy.ToString("0.0");
	}


	private void UpdateUIOffsetSettings()
	{
		if (_txtPosOffsetX != null) _txtPosOffsetX.Text = _currentConfig.PositionOffset.X.ToString("0.##");
		if (_txtPosOffsetY != null) _txtPosOffsetY.Text = _currentConfig.PositionOffset.Y.ToString("0.##");
		if (_txtPosOffsetZ != null) _txtPosOffsetZ.Text = _currentConfig.PositionOffset.Z.ToString("0.##");

		if (_txtRotOffsetX != null) _txtRotOffsetX.Text = _currentConfig.RotationOffset.X.ToString("0.##");
		if (_txtRotOffsetY != null) _txtRotOffsetY.Text = _currentConfig.RotationOffset.Y.ToString("0.##");
		if (_txtRotOffsetZ != null) _txtRotOffsetZ.Text = _currentConfig.RotationOffset.Z.ToString("0.##");

		if (_txtScaleOffsetX != null) _txtScaleOffsetX.Text = _currentConfig.ScaleOffset.X.ToString("0.##");
		if (_txtScaleOffsetY != null) _txtScaleOffsetY.Text = _currentConfig.ScaleOffset.Y.ToString("0.##");
		if (_txtScaleOffsetZ != null) _txtScaleOffsetZ.Text = _currentConfig.ScaleOffset.Z.ToString("0.##");
	}

	private void UpdateUIFromCurrentConfig()
	{
		_isUpdatingUI = true;
		try
		{
			UpdateUIBasicSettings();
			UpdateUIMaskSettings();
			UpdateUIColorSettings();
			UpdateUIFalloffSettings();
			UpdateUIParticleSettings();
			UpdateUIOffsetSettings();
			RebuildStageRows();

			UpdateSectionVisibilities();
		}
		finally
		{
			_isUpdatingUI = false;
		}
	}

	private static int GetParticlePlacementModeIndex(VfxPlacementMode placementMode)
	{
		for (int placementIndex = 0; placementIndex < _particlePlacementModes.Length; placementIndex++)
		{
			if (_particlePlacementModes[placementIndex] == placementMode) return placementIndex;
		}

		return _particlePlacementModes.Length - 1;
	}

	private void SyncBasicSettings()
	{
		SyncVfxId();
		if (_txtVfxName != null) _currentConfig.Name = _txtVfxName.Text.Trim();
		SyncPrimitiveMode();

		if (_optBlendMode != null) _currentConfig.BlendMode = (VfxBlendMode)_optBlendMode.Selected;
		if (_optPlacementMode != null) _currentConfig.PlacementMode = _particlePlacementModes[Math.Clamp(_optPlacementMode.Selected, 0, _particlePlacementModes.Length - 1)];
		if (_chkTintWithPlayerColor != null) _currentConfig.TintWithPlayerColor = _chkTintWithPlayerColor.ButtonPressed;
	}

	private void SyncVfxId()
	{
		if (_txtSlug == null) return;

		_slug = TemplateIDHelper.ToSnakeCase(_txtSlug.Text);
		_vfxId = TemplateIDHelper.NormalizeTemplateID("vfx", _slug);
		_currentConfig.VfxId = _vfxId;

		if (_currentConfig.ParticleConfig != null)
		{
			Particle.ParticleId = _vfxId;
		}
	}

	private void SyncPrimitiveMode()
	{
		if (_optMode == null) return;

		if (_optMode.Selected == 1)
		{
			_currentConfig.PrimitiveType = VfxPrimitiveType.ParticleSystem;
		}
		else if (_optPrimitive != null && _optPrimitive.Selected >= 0 && _optPrimitive.Selected < _primitiveTypes.Length)
		{
			_currentConfig.PrimitiveType = _primitiveTypes[_optPrimitive.Selected];
		}
	}


	private void SyncParticleSettings()
	{
		if (_currentConfig.PrimitiveType != VfxPrimitiveType.ParticleSystem && _currentConfig.ParticleConfig == null) return;

		EnsureParticleConfig();
		SyncParticleVisuals();
		SyncParticleMotion();
	}

	private void SyncParticleVisuals()
	{
		var pc = Particle;
		if (_optParticleShape != null) pc.EmitterShape = (SpellParticleShape)_optParticleShape.Selected;
		if (_optParticleRenderMode != null) pc.RenderMode = (SpellParticleRenderMode)_optParticleRenderMode.Selected;
		if (_pickerParticleColorStart != null) pc.ColorStart = "#" + _pickerParticleColorStart.Color.ToHtml(false);
		if (_pickerParticleColorMid != null) pc.ColorMid = "#" + _pickerParticleColorMid.Color.ToHtml(false);
		if (_pickerParticleColorEnd != null) pc.ColorEnd = "#" + _pickerParticleColorEnd.Color.ToHtml(false);
		if (_sliderParticleEmissionEnergy != null) pc.EmissionEnergy = (float)_sliderParticleEmissionEnergy.Value;
		if (_sliderParticleScaleMin != null) pc.InitialScaleMin = (float)_sliderParticleScaleMin.Value;
		if (_sliderParticleScaleMax != null) pc.InitialScaleMax = (float)_sliderParticleScaleMax.Value;
		if (_sliderParticleEndScaleRatio != null) pc.EndScaleRatio = (float)_sliderParticleEndScaleRatio.Value;
	}

	private void SyncParticleMotion()
	{
		var pc = Particle;
		SyncParticleMotionAmount(pc);
		SyncParticleMotionVelocity(pc);
	}

	private void SyncParticleMotionAmount(SpellParticleConfig pc)
	{
		if (_sliderParticleAmount != null) pc.Amount = (int)_sliderParticleAmount.Value;
		if (_sliderParticleLifetime != null) pc.Lifetime = (float)_sliderParticleLifetime.Value;
		if (_sliderParticleExplosiveness != null) pc.Explosiveness = (float)_sliderParticleExplosiveness.Value;
		if (_chkParticleLocalCoords != null) pc.LocalCoords = _chkParticleLocalCoords.ButtonPressed;
	}

	private void SyncParticleMotionVelocity(SpellParticleConfig pc)
	{
		if (_sliderParticleSpread != null) pc.SpreadDegrees = (float)_sliderParticleSpread.Value;
		if (_sliderParticleVelMin != null) pc.InitialVelocityMin = (float)_sliderParticleVelMin.Value;
		if (_sliderParticleVelMax != null) pc.InitialVelocityMax = (float)_sliderParticleVelMax.Value;
		if (_sliderParticleDamping != null) pc.Damping = (float)_sliderParticleDamping.Value;
		if (_sliderParticleRadialAccel != null) pc.RadialAccel = (float)_sliderParticleRadialAccel.Value;
		if (_sliderParticleTangentialAccel != null) pc.TangentialAccel = (float)_sliderParticleTangentialAccel.Value;
	}


	private void SyncMaskSettings()
	{
		if (_chkLuminanceToAlpha != null) _currentConfig.LuminanceToAlpha = _chkLuminanceToAlpha.ButtonPressed;
		if (_sliderLuminanceThreshold != null) _currentConfig.LuminanceThreshold = (float)_sliderLuminanceThreshold.Value;
		if (_sliderLuminanceSmoothness != null) _currentConfig.LuminanceSmoothness = (float)_sliderLuminanceSmoothness.Value;
		if (_chkUseGrayscale != null) _currentConfig.UseGrayscale = _chkUseGrayscale.ButtonPressed;
		if (_chkInvertMask != null) _currentConfig.InvertMask = _chkInvertMask.ButtonPressed;
		if (_sliderHighPassCutoff != null) _currentConfig.HighPassCutoff = (float)_sliderHighPassCutoff.Value;
	}

	private void SyncColorSettings()
	{
		if (_pickerBaseColor != null) _currentConfig.BaseColor = "#" + _pickerBaseColor.Color.ToHtml(false);
		if (_pickerSecondaryColor != null) _currentConfig.SecondaryColor = "#" + _pickerSecondaryColor.Color.ToHtml(false);
		if (_pickerCoreColor != null) _currentConfig.CoreColor = "#" + _pickerCoreColor.Color.ToHtml(false);
		if (_sliderColorMixRatio != null) _currentConfig.ColorMixRatio = (float)_sliderColorMixRatio.Value;
		if (_sliderEmissionBoost != null) _currentConfig.EmissionBoost = (float)_sliderEmissionBoost.Value;
		if (_sliderCoreThreshold != null) _currentConfig.CoreThreshold = (float)_sliderCoreThreshold.Value;
	}

	private void SyncFalloffSettings()
	{
		SyncRadialFalloff();
		SyncFades();

		if (_sliderSurfaceNormalOffset != null) _currentConfig.SurfaceNormalOffset = (float)_sliderSurfaceNormalOffset.Value;
	}

	private void SyncRadialFalloff()
	{
		if (_chkRadialFalloff != null) _currentConfig.EnableRadialFalloff = _chkRadialFalloff.ButtonPressed;
		if (_sliderRadialFalloffStart != null) _currentConfig.RadialFalloffStart = (float)_sliderRadialFalloffStart.Value;
		if (_sliderRadialFalloffEnd != null) _currentConfig.RadialFalloffEnd = (float)_sliderRadialFalloffEnd.Value;
	}

	private void SyncFades()
	{
		if (_chkLengthFade != null) _currentConfig.EnableLengthFade = _chkLengthFade.ButtonPressed;
		if (_sliderLengthFadeStart != null) _currentConfig.LengthFadeStart = (float)_sliderLengthFadeStart.Value;
		if (_sliderLengthFadeEnd != null) _currentConfig.LengthFadeEnd = (float)_sliderLengthFadeEnd.Value;
		if (_sliderErosionProgress != null) _currentConfig.ErosionProgress = (float)_sliderErosionProgress.Value;

		if (_chkFresnel != null) _currentConfig.EnableFresnel = _chkFresnel.ButtonPressed;
		if (_sliderFresnelPower != null) _currentConfig.FresnelPower = (float)_sliderFresnelPower.Value;
		if (_sliderFresnelIntensity != null) _currentConfig.FresnelIntensity = (float)_sliderFresnelIntensity.Value;

		if (_chkDepthFade != null) _currentConfig.EnableDepthFade = _chkDepthFade.ButtonPressed;
		if (_sliderDepthFadeDistance != null) _currentConfig.DepthFadeDistance = (float)_sliderDepthFadeDistance.Value;
	}


	private void SyncConfigFromControls()
	{
		SyncBasicSettings();
		SyncParticleSettings();
		SyncMaskSettings();
		SyncColorSettings();
		SyncFalloffSettings();
	}

	public void RestartPreviewVfx()
	{
		if (_previewVfxInstance != null && GodotObject.IsInstanceValid(_previewVfxInstance))
		{
			_previewVfxInstance.UpdateConfig(_currentConfig);
		}
	}


}
