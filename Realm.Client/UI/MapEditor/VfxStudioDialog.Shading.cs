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
	private void BuildColorSettings(VBoxContainer parent)
	{
		AddSectionHeader(parent, "🎨 " + TranslationServer.Translate("Color Ramp"));

		(_pickerBaseColor, _sliderBaseColorHue) = AddColorPicker(parent, TranslationServer.Translate("Base Tint"), Colors.Orange, (c) =>
		{
			if (_isUpdatingUI) return;
			_currentConfig.BaseColor = "#" + c.ToHtml(false);
			RestartPreviewVfx();
		}, 140f);

		(_pickerSecondaryColor, _sliderSecondaryColorHue) = AddColorPicker(parent, TranslationServer.Translate("Secondary Color"), Colors.DarkRed, (c) =>
		{
			if (_isUpdatingUI) return;
			_currentConfig.SecondaryColor = "#" + c.ToHtml(false);
			RestartPreviewVfx();
		}, 140f);

		(_pickerCoreColor, _sliderCoreColorHue) = AddColorPicker(parent, TranslationServer.Translate("Core Color"), Colors.White, (c) =>
		{
			if (_isUpdatingUI) return;
			_currentConfig.CoreColor = "#" + c.ToHtml(false);
			RestartPreviewVfx();
		}, 140f);

		(_sliderColorMixRatio, _lblColorMixRatio) = AddSlider(parent, TranslationServer.Translate("Color Mix Ratio"), 0.0f, 1.0f, 0.05f, 0.5f, (val) =>
		{
			if (_isUpdatingUI) return;
			_currentConfig.ColorMixRatio = val;
			RestartPreviewVfx();
		}, "0.00", 140f);

		(_sliderEmissionBoost, _lblEmissionBoost) = AddSlider(parent, TranslationServer.Translate("Emission Energy"), 0.0f, 20.0f, 0.2f, 2.0f, (val) =>
		{
			if (_isUpdatingUI) return;
			_currentConfig.EmissionBoost = val;
			RestartPreviewVfx();
		}, "0.0", 140f);

		(_sliderCoreThreshold, _lblCoreThreshold) = AddSlider(parent, TranslationServer.Translate("Core Threshold"), 0.0f, 1.0f, 0.05f, 0.8f, (val) =>
		{
			if (_isUpdatingUI) return;
			_currentConfig.CoreThreshold = val;
			RestartPreviewVfx();
		}, "0.00", 140f);
	}

	private void BuildFalloffSettings(VBoxContainer parent)
	{
		AddSectionHeader(parent, "📉 " + TranslationServer.Translate("Fade & Falloff"));

		SetupRadialFalloffControls(parent);
		SetupFadeControls(parent);
		SetupFresnelControls(parent);
		SetupDepthFadeControls(parent);
	}

	private void SetupRadialFalloffControls(VBoxContainer parent)
	{
		_chkRadialFalloff = AddCheckBox(parent, TranslationServer.Translate("Radial Edge Falloff"), _currentConfig.EnableRadialFalloff, OnRadialFalloffChanged, "Softens the outer edges of the texture radially");
		(_sliderRadialFalloffStart, _lblRadialFalloffStart) = AddSlider(parent, TranslationServer.Translate("Falloff Start"), 0.0f, 1.0f, 0.05f, 0.3f, OnRadialFalloffStartChanged, "0.00", 140f);
		(_sliderRadialFalloffEnd, _lblRadialFalloffEnd) = AddSlider(parent, TranslationServer.Translate("Falloff End"), 0.0f, 1.0f, 0.05f, 0.5f, OnRadialFalloffEndChanged, "0.00", 140f);
	}

	private void SetupFadeControls(VBoxContainer parent)
	{
		_chkLengthFade = AddCheckBox(parent, TranslationServer.Translate("Vertical/Length Fade"), _currentConfig.EnableLengthFade, OnLengthFadeChanged, "Fades out along the V axis (useful for beams/shockwaves)");
		(_sliderLengthFadeStart, _lblLengthFadeStart) = AddSlider(parent, TranslationServer.Translate("Fade Start (V)"), 0.0f, 1.0f, 0.05f, 0.0f, OnLengthFadeStartChanged, "0.00", 140f);
		(_sliderLengthFadeEnd, _lblLengthFadeEnd) = AddSlider(parent, TranslationServer.Translate("Fade End (V)"), 0.0f, 1.0f, 0.05f, 1.0f, OnLengthFadeEndChanged, "0.00", 140f);
		(_sliderErosionProgress, _lblErosionProgress) = AddSlider(parent, TranslationServer.Translate("Erosion Progress"), 0.0f, 1.0f, 0.05f, 0.0f, OnErosionProgressChanged, "0.00", 140f);
	}

	private void SetupFresnelControls(VBoxContainer parent)
	{
		_chkFresnel = AddCheckBox(parent, TranslationServer.Translate("Fresnel Glow"), _currentConfig.EnableFresnel, OnFresnelChanged, "Highlights grazing angles (only visible on 3D meshes)");
		(_sliderFresnelPower, _lblFresnelPower) = AddSlider(parent, TranslationServer.Translate("Fresnel Power"), 0.0f, 10.0f, 0.2f, 3.0f, OnFresnelPowerChanged, "0.0", 140f);
		(_sliderFresnelIntensity, _lblFresnelIntensity) = AddSlider(parent, TranslationServer.Translate("Fresnel Intensity"), 0.0f, 10.0f, 0.2f, 1.0f, OnFresnelIntensityChanged, "0.0", 140f);
	}

	private void SetupDepthFadeControls(VBoxContainer parent)
	{
		_chkDepthFade = AddCheckBox(parent, TranslationServer.Translate("Depth Fade (Soft Intersect)"), _currentConfig.EnableDepthFade, OnDepthFadeChanged, "Eliminates hard seams where VFX intersects terrain or geometry");
		(_sliderDepthFadeDistance, _lblDepthFadeDistance) = AddSlider(parent, TranslationServer.Translate("Depth Fade Distance"), 0.0f, 5.0f, 0.05f, _currentConfig.DepthFadeDistance, OnDepthFadeDistanceChanged, "0.00", 140f);
	}

	private void OnRadialFalloffChanged(bool val)
	{
		if (!_isUpdatingUI)
		{
			_currentConfig.EnableRadialFalloff = val;
			RestartPreviewVfx();
		}
	}

	private void OnRadialFalloffStartChanged(float val)
	{
		if (!_isUpdatingUI)
		{
			_currentConfig.RadialFalloffStart = val;
			RestartPreviewVfx();
		}
	}

	private void OnRadialFalloffEndChanged(float val)
	{
		if (!_isUpdatingUI)
		{
			_currentConfig.RadialFalloffEnd = val;
			RestartPreviewVfx();
		}
	}

	private void OnLengthFadeChanged(bool val)
	{
		if (!_isUpdatingUI)
		{
			_currentConfig.EnableLengthFade = val;
			RestartPreviewVfx();
		}
	}

	private void OnLengthFadeStartChanged(float val)
	{
		if (!_isUpdatingUI)
		{
			_currentConfig.LengthFadeStart = val;
			RestartPreviewVfx();
		}
	}

	private void OnLengthFadeEndChanged(float val)
	{
		if (!_isUpdatingUI)
		{
			_currentConfig.LengthFadeEnd = val;
			RestartPreviewVfx();
		}
	}

	private void OnErosionProgressChanged(float val)
	{
		if (!_isUpdatingUI)
		{
			_currentConfig.ErosionProgress = val;
			RestartPreviewVfx();
		}
	}

	private void OnFresnelChanged(bool val)
	{
		if (!_isUpdatingUI)
		{
			_currentConfig.EnableFresnel = val;
			RestartPreviewVfx();
		}
	}

	private void OnFresnelPowerChanged(float val)
	{
		if (!_isUpdatingUI)
		{
			_currentConfig.FresnelPower = val;
			RestartPreviewVfx();
		}
	}

	private void OnFresnelIntensityChanged(float val)
	{
		if (!_isUpdatingUI)
		{
			_currentConfig.FresnelIntensity = val;
			RestartPreviewVfx();
		}
	}

	private void OnDepthFadeChanged(bool val)
	{
		if (!_isUpdatingUI)
		{
			_currentConfig.EnableDepthFade = val;
			RestartPreviewVfx();
		}
	}

	private void OnDepthFadeDistanceChanged(float val)
	{
		if (!_isUpdatingUI)
		{
			_currentConfig.DepthFadeDistance = val;
			RestartPreviewVfx();
		}
	}


}
