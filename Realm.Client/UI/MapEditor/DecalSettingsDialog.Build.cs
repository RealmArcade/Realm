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
	private void BuildPreviewSection(VBoxContainer contentVBox)
	{
		var previewPanel = new PanelContainer();
		previewPanel.Name = "PreviewPanel_PanelContainer";
		previewPanel.AddThemeStyleboxOverride("panel", UIStyle.CreateLightInnerPanel());
		previewPanel.CustomMinimumSize = new Vector2(0, 130);

		var previewVBox = new VBoxContainer();
		previewVBox.Name = "PreviewVBox_VBoxContainer";
		previewVBox.Alignment = BoxContainer.AlignmentMode.Center;

		_previewRect = new TextureRect();
		_previewRect.Name = "PreviewRect_TextureRect";
		_previewRect.CustomMinimumSize = new Vector2(100, 100);
		_previewRect.ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize;
		_previewRect.StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered;
		_previewRect.SizeFlagsHorizontal = SizeFlags.ShrinkCenter;
		_previewRect.SizeFlagsVertical = SizeFlags.ShrinkCenter;

		previewVBox.AddChild(_previewRect);
		previewPanel.AddChild(previewVBox);
		contentVBox.AddChild(previewPanel);
	}

	private void BuildTextureAssetSection(VBoxContainer contentVBox)
	{
		AddSectionHeader(contentVBox, "🖼 " + TranslationServer.Translate("TEXTURE & ASSET"), new Color(0.95f, 0.8f, 0.4f));

		var rowId = BuildIdentityRow(contentVBox, "decal/", out _lblObjectTypePrefix);

		_txtSlug = CreateSlugField("decal_slug");
		_txtSlug.TextChanged += (val) =>
		{
			if (_isSyncingControls) return;
			_slug = TemplateIDHelper.ToSnakeCase(val);
			_decalKey = TemplateIDHelper.NormalizeTemplateID("decal", _slug);
			if (_lblDecalName != null) _lblDecalName.Text = TranslationServer.Translate("TemplateID:") + " " + _decalKey;
		};
		rowId.AddChild(_txtSlug);
		

		(_txtTexturePath, _setTexturePathValue) = AddAssetFilterDropdown(
			contentVBox,
			TranslationServer.Translate("Texture Path:"),
			_texturePath,
			(all) => ScanAvailableAssets("decals", all),
			(val) =>
			{
				if (_isSyncingControls) return;
				_texturePath = val ?? string.Empty;
				ReloadBaseTexture();
				UpdateLivePreviewAndWorld();
			},
			TranslationServer.Translate("Select or enter decal texture path..."),
			140f
		);
	}

	private void BuildColorLightingSection(VBoxContainer contentVBox)
	{
		AddSectionHeader(contentVBox, "🎨 " + TranslationServer.Translate("COLOR & LIGHTING"), new Color(0.95f, 0.8f, 0.4f));

		(_sldBrightness, _lblBrightness) = AddSlider(
			contentVBox,
			TranslationServer.Translate("Brightness:"),
			0.1f,
			3.0f,
			0.05f,
			_brightness,
			(val) =>
			{
				if (_isSyncingControls) return;
				_brightness = val;
				UpdateLivePreviewAndWorld();
			},
			"0.00x",
			140f
		);

			HBoxContainer rowTint;
			(rowTint, _btnTint) = UIStyle.CreateColorPickerRow("Tint Color:", _tint, (newCol) =>
		{
			if (_isSyncingControls) return;
			_tint = newCol;
			UpdateLivePreviewAndWorld();
			});
		contentVBox.AddChild(rowTint);

		(_sldContrast, _lblContrast) = AddSlider(
			contentVBox,
			TranslationServer.Translate("Contrast:"),
			0.2f,
			2.5f,
			0.05f,
			_contrast,
			(val) =>
			{
				if (_isSyncingControls) return;
				_contrast = val;
				UpdateLivePreviewAndWorld();
			},
			"0.00x",
			140f
		);

		(_sldSaturation, _lblSaturation) = AddSlider(
			contentVBox,
			TranslationServer.Translate("Saturation:"),
			0.0f,
			2.5f,
			0.05f,
			_saturation,
			(val) =>
			{
				if (_isSyncingControls) return;
				_saturation = val;
				UpdateLivePreviewAndWorld();
			},
			"0.00x",
			140f
		);
	}

	private void BuildBlendingMaterialSection(VBoxContainer contentVBox)
	{
		AddSectionHeader(contentVBox, "✨ " + TranslationServer.Translate("BLENDING & MATERIAL"), new Color(0.5f, 0.85f, 1.0f));

		BuildOpacityAndMixControls(contentVBox);
		BuildPBRControls(contentVBox);
		BuildFadeControls(contentVBox);
	}

	private void BuildOpacityAndMixControls(VBoxContainer contentVBox)
	{
		(_sldOpacity, _lblOpacity) = AddSlider(
			contentVBox,
			TranslationServer.Translate("Opacity / Alpha:"),
			0.0f,
			1.0f,
			0.02f,
			_opacity,
			(val) =>
			{
				if (_isSyncingControls) return;
				_opacity = val;
				UpdateLivePreviewAndWorld();
			},
			"0.00",
			140f
		);

		(_sldAlbedoMix, _lblAlbedoMix) = AddSlider(
			contentVBox,
			TranslationServer.Translate("Albedo Mix:"),
			0.0f,
			1.0f,
			0.02f,
			_albedoMix,
			(val) =>
			{
				if (_isSyncingControls) return;
				_albedoMix = val;
				UpdateLivePreviewAndWorld();
			},
			"0.00",
			140f
		);
	}

	private void BuildPBRControls(VBoxContainer contentVBox)
	{
		(_sldNormalStrength, _lblNormalStrength) = AddSlider(
			contentVBox,
			TranslationServer.Translate("Normal Depth:"),
			0.0f,
			2.0f,
			0.05f,
			_normalStrength,
			(val) =>
			{
				if (_isSyncingControls) return;
				_normalStrength = val;
				UpdateLivePreviewAndWorld();
			},
			"0.00x",
			140f
		);

		(_sldRoughness, _lblRoughness) = AddSlider(
			contentVBox,
			TranslationServer.Translate("Roughness:"),
			0.0f,
			1.0f,
			0.05f,
			_roughness,
			(val) =>
			{
				if (_isSyncingControls) return;
				_roughness = val;
				UpdateLivePreviewAndWorld();
			},
			"0.00",
			140f
		);

		(_sldMetallic, _lblMetallic) = AddSlider(
			contentVBox,
			TranslationServer.Translate("Metallic:"),
			0.0f,
			1.0f,
			0.05f,
			_metallic,
			(val) =>
			{
				if (_isSyncingControls) return;
				_metallic = val;
				UpdateLivePreviewAndWorld();
			},
			"0.00",
			140f
		);

		string[] blendModes = new[]
		{
			TranslationServer.Translate("Mix (Normal)").ToString(),
			TranslationServer.Translate("Additive").ToString(),
			TranslationServer.Translate("Multiply").ToString(),
			TranslationServer.Translate("Screen").ToString()
		};

		_optBlendMode = AddOptionDropdown(
			contentVBox,
			TranslationServer.Translate("Blend Mode:"),
			blendModes,
			0,
			(idx) =>
			{
				if (_isSyncingControls) return;
				_blendMode = idx switch
				{
					1 => "Additive",
					2 => "Multiply",
					3 => "Screen",
					_ => "Mix"
				};
				UpdateLivePreviewAndWorld();
			},
			140f
		);
	}

	private void BuildFadeControls(VBoxContainer contentVBox)
	{
		(_sldUpperFade, _lblUpperFade) = AddSlider(
			contentVBox,
			TranslationServer.Translate("Upper Fade:"),
			0.0f,
			1.0f,
			0.05f,
			_upperFade,
			(val) =>
			{
				if (_isSyncingControls) return;
				_upperFade = val;
				UpdateLivePreviewAndWorld();
			},
			"0.00",
			140f
		);

		(_sldLowerFade, _lblLowerFade) = AddSlider(
			contentVBox,
			TranslationServer.Translate("Lower Fade:"),
			0.0f,
			1.0f,
			0.05f,
			_lowerFade,
			(val) =>
			{
				if (_isSyncingControls) return;
				_lowerFade = val;
				UpdateLivePreviewAndWorld();
			},
			"0.00",
			140f
		);
	}

	private void BuildPlacementSection(VBoxContainer contentVBox)
	{
		AddSectionHeader(contentVBox, "⭕ " + TranslationServer.Translate("GROUND PROJECTION"), new Color(0.95f, 0.65f, 0.35f));

		var placementRow = new HBoxContainer();
		placementRow.Name = "PlacementRow_HBoxContainer";
		placementRow.AddThemeConstantOverride("separation", 6);

		var placementLabel = new Label();
		placementLabel.Name = "LblPlacementMode_Label";
		placementLabel.Text = TranslationServer.Translate("Orientation:");
		placementLabel.CustomMinimumSize = new Vector2(140, 0);
		placementLabel.AddThemeFontSizeOverride("font_size", 11);
		placementRow.AddChild(placementLabel);

		_optPlacementMode = new OptionButton();
		_optPlacementMode.Name = "OptPlacementMode_OptionButton";
		_optPlacementMode.AddItem(TranslationServer.Translate("Authored"), 0);
		_optPlacementMode.AddItem(TranslationServer.Translate("Flat On Ground"), 1);
		_optPlacementMode.SizeFlagsHorizontal = SizeFlags.ExpandFill;
		_optPlacementMode.ItemSelected += index =>
		{
			if (_isSyncingControls) return;
			_placementMode = index == 1 ? VfxPlacementMode.GroundProjected : VfxPlacementMode.Free;
			UpdateLivePreviewAndWorld();
		};
		placementRow.AddChild(_optPlacementMode);
		contentVBox.AddChild(placementRow);

		(_sldProjectionSize, _lblProjectionSize) = AddSlider(contentVBox, TranslationServer.Translate("Projection Size:"), 1.0f, 30.0f, 0.5f, _projectionSize, (val) =>
		{
			if (_isSyncingControls) return;
			_projectionSize = val;
			UpdateLivePreviewAndWorld();
		}, "0.0", 140f);

		(_sldProjectionHeight, _lblProjectionHeight) = AddSlider(contentVBox, TranslationServer.Translate("Projection Height:"), 1.0f, 30.0f, 0.5f, _projectionHeight, (val) =>
		{
			if (_isSyncingControls) return;
			_projectionHeight = val;
			UpdateLivePreviewAndWorld();
		}, "0.0", 140f);

		(_sldGroundOffset, _lblGroundOffset) = AddSlider(contentVBox, TranslationServer.Translate("Ground Offset:"), 0.0f, 1.0f, 0.01f, _groundOffset, (val) =>
		{
			if (_isSyncingControls) return;
			_groundOffset = val;
			UpdateLivePreviewAndWorld();
		}, "0.00", 140f);

		(_sldNormalFade, _lblNormalFade) = AddSlider(contentVBox, TranslationServer.Translate("Normal Fade:"), 0.0f, 1.0f, 0.05f, _normalFade, (val) =>
		{
			if (_isSyncingControls) return;
			_normalFade = val;
			UpdateLivePreviewAndWorld();
		}, "0.00", 140f);

		_chkDistanceFade = AddCheckBox(contentVBox, TranslationServer.Translate("Enable Distance Fade:"), _distanceFadeEnabled, (val) =>
		{
			if (_isSyncingControls) return;
			_distanceFadeEnabled = val;
			UpdateLivePreviewAndWorld();
		});

		(_sldDistanceFadeBegin, _lblDistanceFadeBegin) = AddSlider(contentVBox, TranslationServer.Translate("Distance Fade Begin:"), 1.0f, 200.0f, 1.0f, _distanceFadeBegin, (val) =>
		{
			if (_isSyncingControls) return;
			_distanceFadeBegin = val;
			UpdateLivePreviewAndWorld();
		}, "0", 140f);

		(_sldDistanceFadeEnd, _lblDistanceFadeEnd) = AddSlider(contentVBox, TranslationServer.Translate("Distance Fade End:"), 1.0f, 400.0f, 1.0f, _distanceFadeEnd, (val) =>
		{
			if (_isSyncingControls) return;
			_distanceFadeEnd = val;
			UpdateLivePreviewAndWorld();
		}, "0", 140f);
	}

	private void BuildPropertyAnimationSection(VBoxContainer contentVBox)
	{
		AddSectionHeader(contentVBox, "⚡ " + TranslationServer.Translate("DYNAMIC PROPERTY ANIMATION"), new Color(0.4f, 0.8f, 0.95f));

		BuildOpacityAnimationControls(contentVBox);
		BuildEmissionAnimationControls(contentVBox);
		BuildScaleAnimationControls(contentVBox);
	}

	private void BuildOpacityAnimationControls(VBoxContainer contentVBox)
	{
		_chkAnimateOpacity = AddCheckBox(contentVBox, TranslationServer.Translate("Enable Opacity Pulse:"), _animateOpacity, (val) =>
		{
			if (_isSyncingControls) return;
			_animateOpacity = val;
			UpdateLivePreviewAndWorld();
		});

		(_sldOpacitySpeed, _lblOpacitySpeed) = AddSlider(contentVBox, TranslationServer.Translate("Opacity Speed:"), 0.1f, 10.0f, 0.1f, _opacityPulseSpeed, (val) =>
		{
			if (_isSyncingControls) return;
			_opacityPulseSpeed = val;
			UpdateLivePreviewAndWorld();
		}, "0.0x", 140f);

		(_sldMinOpacity, _lblMinOpacity) = AddSlider(contentVBox, TranslationServer.Translate("Min Opacity:"), 0.0f, 1.0f, 0.05f, _minOpacity, (val) =>
		{
			if (_isSyncingControls) return;
			_minOpacity = val;
			UpdateLivePreviewAndWorld();
		}, "0.00", 140f);

		(_sldMaxOpacity, _lblMaxOpacity) = AddSlider(contentVBox, TranslationServer.Translate("Max Opacity:"), 0.0f, 1.0f, 0.05f, _maxOpacity, (val) =>
		{
			if (_isSyncingControls) return;
			_maxOpacity = val;
			UpdateLivePreviewAndWorld();
		}, "0.00", 140f);
	}

	private void BuildEmissionAnimationControls(VBoxContainer contentVBox)
	{
		_chkAnimateEmission = AddCheckBox(contentVBox, TranslationServer.Translate("Enable Emission Pulse:"), _animateEmission, (val) =>
		{
			if (_isSyncingControls) return;
			_animateEmission = val;
			UpdateLivePreviewAndWorld();
		});

		(_sldEmissionSpeed, _lblEmissionSpeed) = AddSlider(contentVBox, TranslationServer.Translate("Emission Speed:"), 0.1f, 10.0f, 0.1f, _emissionPulseSpeed, (val) =>
		{
			if (_isSyncingControls) return;
			_emissionPulseSpeed = val;
			UpdateLivePreviewAndWorld();
		}, "0.0x", 140f);

		(_sldMinEmission, _lblMinEmission) = AddSlider(contentVBox, TranslationServer.Translate("Min Emission:"), 0.0f, 10.0f, 0.1f, _minEmission, (val) =>
		{
			if (_isSyncingControls) return;
			_minEmission = val;
			UpdateLivePreviewAndWorld();
		}, "0.0", 140f);

		(_sldMaxEmission, _lblMaxEmission) = AddSlider(contentVBox, TranslationServer.Translate("Max Emission:"), 0.0f, 10.0f, 0.1f, _maxEmission, (val) =>
		{
			if (_isSyncingControls) return;
			_maxEmission = val;
			UpdateLivePreviewAndWorld();
		}, "0.0", 140f);
	}

	private void BuildScaleAnimationControls(VBoxContainer contentVBox)
	{
		_chkAnimateScale = AddCheckBox(contentVBox, TranslationServer.Translate("Enable Scale Pulse:"), _animateScale, (val) =>
		{
			if (_isSyncingControls) return;
			_animateScale = val;
			UpdateLivePreviewAndWorld();
		});

		(_sldScaleSpeed, _lblScaleSpeed) = AddSlider(contentVBox, TranslationServer.Translate("Scale Speed:"), 0.1f, 10.0f, 0.1f, _scalePulseSpeed, (val) =>
		{
			if (_isSyncingControls) return;
			_scalePulseSpeed = val;
			UpdateLivePreviewAndWorld();
		}, "0.0x", 140f);

		(_sldMinScale, _lblMinScale) = AddSlider(contentVBox, TranslationServer.Translate("Min Scale Ratio:"), 0.1f, 2.0f, 0.05f, _minScaleRatio, (val) =>
		{
			if (_isSyncingControls) return;
			_minScaleRatio = val;
			UpdateLivePreviewAndWorld();
		}, "0.00x", 140f);

		(_sldMaxScale, _lblMaxScale) = AddSlider(contentVBox, TranslationServer.Translate("Max Scale Ratio:"), 0.1f, 3.0f, 0.05f, _maxScaleRatio, (val) =>
		{
			if (_isSyncingControls) return;
			_maxScaleRatio = val;
			UpdateLivePreviewAndWorld();
		}, "0.00x", 140f);
	}
}
