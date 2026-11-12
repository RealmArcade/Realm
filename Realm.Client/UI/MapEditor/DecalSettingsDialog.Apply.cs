using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using Godot;
using Realm.Client.Core;
using Realm.Client.Services;
using Realm.Shared.Services;
using Realm.Shared.Textures;

namespace Realm.Client.UI.MapEditor;

public partial class DecalSettingsDialog
{
	public override void _Process(double delta)
	{
		base._Process(delta);
		if (!Visible || _previewRect == null) return;

		if (_animateOpacity || _animateEmission || _animateScale)
		{
			_previewAnimTime += delta;
			float time = (float)_previewAnimTime;

			float currentOpacity = _opacity;
			if (_animateOpacity)
			{
				float sine = (MathF.Sin(time * _opacityPulseSpeed * MathF.PI * 2.0f) + 1.0f) * 0.5f;
				currentOpacity = Mathf.Lerp(_minOpacity, _maxOpacity, sine);
			}

			float currentEmission = 1.0f;
			if (_animateEmission)
			{
				float sine = (MathF.Sin(time * _emissionPulseSpeed * MathF.PI * 2.0f) + 1.0f) * 0.5f;
				currentEmission = Mathf.Lerp(_minEmission, _maxEmission, sine);
			}

			float currentScale = 1.0f;
			if (_animateScale)
			{
				float sine = (MathF.Sin(time * _scalePulseSpeed * MathF.PI * 2.0f) + 1.0f) * 0.5f;
				currentScale = Mathf.Lerp(_minScaleRatio, _maxScaleRatio, sine);
			}

			Color modColor = CalculateModulateColor(currentEmission, currentOpacity);
			float r = modColor.R, g = modColor.G, b = modColor.B;

			_previewRect.Modulate = new Color(Mathf.Clamp(r, 0f, 4f), Mathf.Clamp(g, 0f, 4f), Mathf.Clamp(b, 0f, 4f), Mathf.Clamp(currentOpacity, 0f, 1f));
			_previewRect.Scale = new Vector2(currentScale, currentScale);
			_previewRect.PivotOffset = _previewRect.Size * 0.5f;
		}
		else
		{
			if (_previewRect.Scale != Vector2.One)
			{
				_previewRect.Scale = Vector2.One;
			}
		}
	}

	private void UpdateLivePreviewAndWorld()
	{
		UpdateLabels();
		UpdatePreviewRectMaterial();
		UpdateGameHostDecals();
	}

	private void UpdateLabels()
	{
		UpdateLabelText(_lblBrightness, _brightness, "F2");
		if (_lblBrightness != null) _lblBrightness.Text += MultiplierSuffix;

		UpdateLabelText(_lblContrast, _contrast, "F2");
		if (_lblContrast != null) _lblContrast.Text += MultiplierSuffix;

		UpdateLabelText(_lblSaturation, _saturation, "F2");
		if (_lblSaturation != null) _lblSaturation.Text += MultiplierSuffix;

		UpdateLabelText(_lblOpacity, _opacity, "F2");
		UpdateLabelText(_lblAlbedoMix, _albedoMix, "F2");

		UpdateLabelText(_lblNormalStrength, _normalStrength, "F2");
		if (_lblNormalStrength != null) _lblNormalStrength.Text += MultiplierSuffix;

		UpdateLabelText(_lblRoughness, _roughness, "F2");
		UpdateLabelText(_lblMetallic, _metallic, "F2");

		UpdateLabelText(_lblOpacitySpeed, _opacityPulseSpeed, "F1");
		if (_lblOpacitySpeed != null) _lblOpacitySpeed.Text += MultiplierSuffix;

		UpdateLabelText(_lblMinOpacity, _minOpacity, "F2");
		UpdateLabelText(_lblMaxOpacity, _maxOpacity, "F2");

		UpdateLabelText(_lblEmissionSpeed, _emissionPulseSpeed, "F1");
		if (_lblEmissionSpeed != null) _lblEmissionSpeed.Text += MultiplierSuffix;

		UpdateLabelText(_lblMinEmission, _minEmission, "F1");
		UpdateLabelText(_lblMaxEmission, _maxEmission, "F1");

		UpdateLabelText(_lblScaleSpeed, _scalePulseSpeed, "F1");
		if (_lblScaleSpeed != null) _lblScaleSpeed.Text += MultiplierSuffix;

		UpdateLabelText(_lblMinScale, _minScaleRatio, "F2");
		if (_lblMinScale != null) _lblMinScale.Text += MultiplierSuffix;

		UpdateLabelText(_lblMaxScale, _maxScaleRatio, "F2");
		if (_lblMaxScale != null) _lblMaxScale.Text += MultiplierSuffix;

		UpdateLabelText(_lblUpperFade, _upperFade, "F2");
		UpdateLabelText(_lblLowerFade, _lowerFade, "F2");
	}

	private void UpdateLabelText(Label? lbl, float value, string format)
	{
		if (lbl != null) lbl.Text = value.ToString(format);
	}

	private void UpdatePreviewRectMaterial()
	{
		if (_previewRect == null) return;

		Color modColor = CalculateModulateColor(0f, _opacity);
		float r = modColor.R, g = modColor.G, b = modColor.B;

		_previewRect.Modulate = new Color(Mathf.Clamp(r, 0f, 4f), Mathf.Clamp(g, 0f, 4f), Mathf.Clamp(b, 0f, 4f), Mathf.Clamp(_opacity, 0f, 1f));
		_previewRect.Texture = _baseTexture;

		if (_previewRect.Material is not CanvasItemMaterial mat)
		{
			mat = new CanvasItemMaterial();
			_previewRect.Material = mat;
		}

		mat.BlendMode = _blendMode switch
		{
			"Additive" => CanvasItemMaterial.BlendModeEnum.Add,
			"Multiply" => CanvasItemMaterial.BlendModeEnum.Mul,
			"Screen" => CanvasItemMaterial.BlendModeEnum.Add,
			_ => CanvasItemMaterial.BlendModeEnum.Mix
		};
	}

	private Color CalculateModulateColor(float emission, float opacity)
	{
		float r = _tint.R * _brightness * (1.0f + emission * 0.5f);
		float g = _tint.G * _brightness * (1.0f + emission * 0.5f);
		float b = _tint.B * _brightness * (1.0f + emission * 0.5f);

		float lum = 0.2126f * r + 0.7152f * g + 0.0722f * b;
		r = lum + (r - lum) * _saturation;
		g = lum + (g - lum) * _saturation;
		b = lum + (b - lum) * _saturation;

		r = (r - 0.5f) * _contrast + 0.5f;
		g = (g - 0.5f) * _contrast + 0.5f;
		b = (b - 0.5f) * _contrast + 0.5f;

		return new Color(r, g, b, opacity);
	}

	private void UpdateGameHostDecals()
	{
		GameContext.Host?.RefreshDecalsLive(
			_decalKey,
			_brightness,
			_tint,
			_contrast,
			_saturation,
			_opacity,
			_albedoMix,
			_normalStrength,
			_roughness,
			_metallic,
			_blendMode,
			_animateOpacity,
			_opacityPulseSpeed,
			_minOpacity,
			_maxOpacity,
			_animateEmission,
			_emissionPulseSpeed,
			_minEmission,
			_maxEmission,
			_animateScale,
			_scalePulseSpeed,
			_minScaleRatio,
			_maxScaleRatio,
			_upperFade,
			_lowerFade,
			_placementMode,
			_projectionSize,
			_projectionHeight,
			_groundOffset,
			_normalFade,
			_distanceFadeEnabled,
			_distanceFadeBegin,
			_distanceFadeEnd
		);
	}

	protected override void OnApply()
	{
		_slug = TemplateIDHelper.ResolveSlug(_txtSlug?.Text, _slug);
		_decalKey = TemplateIDHelper.NormalizeTemplateID("decal", _slug);

		string finalTexturePath = !string.IsNullOrWhiteSpace(_txtTexturePath?.Text) ? _txtTexturePath.Text.Trim() : _texturePath;
		_texturePath = finalTexturePath;

		GameContext.Host?.InvalidateDecalCache(_decalKey);

		var result = new JsonObject
		{
			["TemplateID"] = _decalKey,
			["template_id"] = _decalKey,
			["decal_id"] = _decalKey,
			["DecalId"] = _decalKey,
			["texture_path"] = _texturePath,
			["TexturePath"] = _texturePath,
			["brightness"] = Math.Round(_brightness, 3),
			["Brightness"] = Math.Round(_brightness, 3),
			["tint"] = $"#{_tint.ToHtml(false)}",
			["Tint"] = $"#{_tint.ToHtml(false)}",
			["contrast"] = Math.Round(_contrast, 3),
			["Contrast"] = Math.Round(_contrast, 3),
			["saturation"] = Math.Round(_saturation, 3),
			["Saturation"] = Math.Round(_saturation, 3),
			["opacity"] = Math.Round(_opacity, 3),
			["Opacity"] = Math.Round(_opacity, 3),
			["albedo_mix"] = Math.Round(_albedoMix, 3),
			["AlbedoMix"] = Math.Round(_albedoMix, 3),
			["normal_strength"] = Math.Round(_normalStrength, 3),
			["NormalStrength"] = Math.Round(_normalStrength, 3),
			["roughness"] = Math.Round(_roughness, 3),
			["Roughness"] = Math.Round(_roughness, 3),
			["metallic"] = Math.Round(_metallic, 3),
			["Metallic"] = Math.Round(_metallic, 3),
			["blend_mode"] = _blendMode,
			["BlendMode"] = _blendMode,
			["animate_opacity"] = _animateOpacity,
			["AnimateOpacity"] = _animateOpacity,
			["opacity_pulse_speed"] = Math.Round(_opacityPulseSpeed, 2),
			["OpacityPulseSpeed"] = Math.Round(_opacityPulseSpeed, 2),
			["min_opacity"] = Math.Round(_minOpacity, 3),
			["MinOpacity"] = Math.Round(_minOpacity, 3),
			["max_opacity"] = Math.Round(_maxOpacity, 3),
			["MaxOpacity"] = Math.Round(_maxOpacity, 3),
			["animate_emission"] = _animateEmission,
			["AnimateEmission"] = _animateEmission,
			["emission_pulse_speed"] = Math.Round(_emissionPulseSpeed, 2),
			["EmissionPulseSpeed"] = Math.Round(_emissionPulseSpeed, 2),
			["min_emission"] = Math.Round(_minEmission, 2),
			["MinEmission"] = Math.Round(_minEmission, 2),
			["max_emission"] = Math.Round(_maxEmission, 2),
			["MaxEmission"] = Math.Round(_maxEmission, 2),
			["animate_scale"] = _animateScale,
			["AnimateScale"] = _animateScale,
			["scale_pulse_speed"] = Math.Round(_scalePulseSpeed, 2),
			["ScalePulseSpeed"] = Math.Round(_scalePulseSpeed, 2),
			["min_scale_ratio"] = Math.Round(_minScaleRatio, 3),
			["MinScaleRatio"] = Math.Round(_minScaleRatio, 3),
			["max_scale_ratio"] = Math.Round(_maxScaleRatio, 3),
			["MaxScaleRatio"] = Math.Round(_maxScaleRatio, 3),
			["upper_fade"] = Math.Round(_upperFade, 3),
			["UpperFade"] = Math.Round(_upperFade, 3),
			["lower_fade"] = Math.Round(_lowerFade, 3),
			["LowerFade"] = Math.Round(_lowerFade, 3),
			["projection_size"] = Math.Round(_projectionSize, 3),
			["ProjectionSize"] = Math.Round(_projectionSize, 3),
			["projection_height"] = Math.Round(_projectionHeight, 3),
			["ProjectionHeight"] = Math.Round(_projectionHeight, 3),
			["ground_offset"] = Math.Round(_groundOffset, 3),
			["GroundOffset"] = Math.Round(_groundOffset, 3),
			["normal_fade"] = Math.Round(_normalFade, 3),
			["NormalFade"] = Math.Round(_normalFade, 3),
			["distance_fade_enabled"] = _distanceFadeEnabled,
			["DistanceFadeEnabled"] = _distanceFadeEnabled,
			["distance_fade_begin"] = Math.Round(_distanceFadeBegin, 2),
			["DistanceFadeBegin"] = Math.Round(_distanceFadeBegin, 2),
			["distance_fade_end"] = Math.Round(_distanceFadeEnd, 2),
			["DistanceFadeEnd"] = Math.Round(_distanceFadeEnd, 2),
			["PlacementMode"] = (int)_placementMode,
			["asset_type"] = "Decal",
			["AssetType"] = "Decal"
		};

		_onApplied?.Invoke(result);
		Hud?.ShowFeedback(string.Format(TranslationServer.Translate("Saved decal properties for '{0}'"), _decalKey));
		CloseDialog();
	}

	protected override void OnCancel()
	{
		string targetId = !string.IsNullOrEmpty(_decalKey) ? _decalKey : $"decal/{_slug}";
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

		if (!TryRestoreOriginalStates())
		{
			RestoreInitialSnapshot();
		}

		base.OnCancel();
	}
}
