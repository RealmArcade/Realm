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

[GlobalClass]
public partial class DecalSettingsDialog : FloatingDialogBase
{
	public DecalSettingsDialog() { }

	private string _decalKey = "";
	private string _texturePath = "";
	private float _brightness = 1.0f;
	private Color _tint = Colors.White;
	private float _contrast = 1.0f;
	private float _saturation = 1.0f;
	private float _opacity = 1.0f;
	private float _albedoMix = 1.0f;
	private float _normalStrength = 0.0f;
	private float _roughness = 1.0f;
	private float _metallic = 0.0f;
	private string _blendMode = "Mix";

	private bool _animateOpacity = false;
	private float _opacityPulseSpeed = 1.0f;
	private float _minOpacity = 0.2f;
	private float _maxOpacity = 1.0f;

	private bool _animateEmission = false;
	private float _emissionPulseSpeed = 1.0f;
	private float _minEmission = 0.0f;
	private float _maxEmission = 2.0f;

	private bool _animateScale = false;
	private float _scalePulseSpeed = 1.0f;
	private float _minScaleRatio = 0.8f;
	private float _maxScaleRatio = 1.2f;

	private float _upperFade = 0.3f;
	private float _lowerFade = 0.3f;

	private VfxPlacementMode _placementMode = VfxPlacementMode.Free;
	private float _projectionSize = 0.0f;
	private float _projectionHeight = 0.0f;
	private float _groundOffset = 0.05f;
	private float _normalFade = 0.0f;
	private bool _distanceFadeEnabled = false;
	private float _distanceFadeBegin = 10.0f;
	private float _distanceFadeEnd = 30.0f;

	private bool _isSyncingControls = false;
	private string _slug = "";
	private Label _lblObjectTypePrefix;
	private LineEdit _txtSlug;
	private LineEdit _txtTexturePath;
	private Action<string> _setTexturePathValue;
	private CheckBox _chkAnimateOpacity;
	private HSlider _sldOpacitySpeed;
	private Label _lblOpacitySpeed;
	private HSlider _sldMinOpacity;
	private Label _lblMinOpacity;
	private HSlider _sldMaxOpacity;
	private Label _lblMaxOpacity;

	private CheckBox _chkAnimateEmission;
	private HSlider _sldEmissionSpeed;
	private Label _lblEmissionSpeed;
	private HSlider _sldMinEmission;
	private Label _lblMinEmission;
	private HSlider _sldMaxEmission;
	private Label _lblMaxEmission;

	private CheckBox _chkAnimateScale;
	private HSlider _sldScaleSpeed;
	private Label _lblScaleSpeed;
	private HSlider _sldMinScale;
	private Label _lblMinScale;
	private HSlider _sldMaxScale;
	private Label _lblMaxScale;

	private HSlider _sldUpperFade;
	private Label _lblUpperFade;
	private HSlider _sldLowerFade;
	private Label _lblLowerFade;

	private OptionButton _optPlacementMode;
	private HSlider _sldProjectionSize;
	private Label _lblProjectionSize;
	private HSlider _sldProjectionHeight;
	private Label _lblProjectionHeight;
	private HSlider _sldGroundOffset;
	private Label _lblGroundOffset;
	private HSlider _sldNormalFade;
	private Label _lblNormalFade;
	private CheckBox _chkDistanceFade;
	private HSlider _sldDistanceFadeBegin;
	private Label _lblDistanceFadeBegin;
	private HSlider _sldDistanceFadeEnd;
	private Label _lblDistanceFadeEnd;

	private double _previewAnimTime = 0.0;


	private readonly Dictionary<ulong, DecalNodeState> _originalDecalStates = new();

	private DecalSnapshot _initialSnapshot;
	private string? _initialTemplateJson = null;
	private bool _templateExistedInitially = false;
	private Action<JsonObject> _onApplied;

	private Label _lblDecalName;
	private TextureRect _previewRect;
	private Texture2D _baseTexture;

	private HSlider _sldBrightness;
	private Label _lblBrightness;
	private ColorPickerButton _btnTint;
	private HSlider _sldContrast;
	private Label _lblContrast;
	private HSlider _sldSaturation;
	private Label _lblSaturation;
	private HSlider _sldOpacity;
	private Label _lblOpacity;
	private HSlider _sldAlbedoMix;
	private Label _lblAlbedoMix;
	private HSlider _sldNormalStrength;
	private Label _lblNormalStrength;
	private HSlider _sldRoughness;
	private Label _lblRoughness;
	private HSlider _sldMetallic;
	private Label _lblMetallic;
	private OptionButton _optBlendMode;

	public DecalSettingsDialog(MapEditorHUD hud)
		: base(hud, TranslationServer.Translate("Decal Rendering & Blending Properties"), new Vector2(480, 720))
	{
		BuildControls();
	}

	private void BuildControls()
	{
		var scrollBody = CreateScrollBody(600);
		var contentVBox = new VBoxContainer();
		contentVBox.Name = "ContentVBox_VBoxContainer";
		contentVBox.AddThemeConstantOverride("separation", 10);
		contentVBox.SizeFlagsHorizontal = SizeFlags.ExpandFill;
		scrollBody.AddChild(contentVBox);

		_lblDecalName = new Label();
		_lblDecalName.Name = "LblDecalName_Label";
		_lblDecalName.AddThemeFontSizeOverride("font_size", 12);
		_lblDecalName.AddThemeColorOverride("font_color", new Color(0.95f, 0.82f, 0.55f));
		_lblDecalName.HorizontalAlignment = HorizontalAlignment.Center;
		contentVBox.AddChild(_lblDecalName);

		BuildPreviewSection(contentVBox);
		BuildTextureAssetSection(contentVBox);
		BuildColorLightingSection(contentVBox);
		BuildBlendingMaterialSection(contentVBox);
		BuildPlacementSection(contentVBox);
		BuildPropertyAnimationSection(contentVBox);
	}

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

		_txtSlug = new LineEdit();
		_txtSlug.Name = "TxtSlug_LineEdit";
		_txtSlug.PlaceholderText = TranslationServer.Translate("decal_slug");
		_txtSlug.SizeFlagsHorizontal = SizeFlags.ExpandFill;
		_txtSlug.AddThemeFontSizeOverride("font_size", 11);
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

	private void ReloadBaseTexture()
	{
		string texKey = !string.IsNullOrWhiteSpace(_texturePath)
			? _texturePath
			: (!string.IsNullOrWhiteSpace(_decalKey) && _decalKey.EndsWith(".rtex", StringComparison.OrdinalIgnoreCase) ? _decalKey : string.Empty);

		if (string.IsNullOrWhiteSpace(texKey))
		{
			_baseTexture = null;
			return;
		}

		_baseTexture = GameHost.Instance?.LoadDecalTexture(texKey);
		if (_baseTexture != null) return;

		string wsPath = MapWorkspaceService.GetActiveWorkspacePath();
		string filename = Path.GetFileName(texKey);
		string baseKey = Path.GetFileNameWithoutExtension(texKey);

		string[] candidates = new[]
		{
			Path.Combine(wsPath, "Assets", "decals", texKey),
			Path.Combine(wsPath, "Assets", "decals", filename),
			Path.Combine(wsPath, "Assets", "decals", $"{baseKey}.rtex"),
			Path.Combine(wsPath, "Assets", $"{baseKey}.rtex"),
			Path.Combine(wsPath, texKey)
		};

		foreach (var p in candidates)
		{
			if (TryLoadRtexTexture(p)) break;
		}
	}

	private bool TryLoadRtexTexture(string path)
	{
		if (!File.Exists(path) || !path.EndsWith(".rtex", StringComparison.OrdinalIgnoreCase)) return false;

		byte[] rtexBytes = File.ReadAllBytes(path);
		byte[]? webpBytes = RtexFile.GetLayer(rtexBytes, 0);
		if (webpBytes == null || webpBytes.Length == 0) return false;

		var img = Image.CreateEmpty(1, 1, false, Image.Format.Rgba8);
		if (img.LoadWebpFromBuffer(webpBytes) == Error.Ok || img.LoadPngFromBuffer(webpBytes) == Error.Ok)
		{
			_baseTexture = ImageTexture.CreateFromImage(img);
			return true;
		}

		return false;
	}

	public static JsonObject ResolveDecalMetadata(string decalKey, JsonObject? providedData = null)
	{
		var result = new JsonObject();
		if (providedData != null)
		{
			foreach (var kvp in providedData)
			{
				result[kvp.Key] = kvp.Value?.DeepClone();
			}
		}

		ResolveFromMetadataJson(decalKey, result);
		ResolveFromRtexFile(decalKey, result);
		ResolveFromInWorldDecal(decalKey, result);

		return result;
	}

	private static void ResolveFromMetadataJson(string decalKey, JsonObject result)
	{
		try
		{
			string wsPath = MapWorkspaceService.GetActiveWorkspacePath();
			var metadata = MapFileService.LoadMetadata(wsPath);
			if (metadata?.Decals == null) return;

			string key = Path.GetFileName(decalKey);
			string baseKey = Path.GetFileNameWithoutExtension(decalKey);

			DecalMetadata? foundMeta = null;
			string[] candidates = new[] { decalKey, key, baseKey, $"{baseKey}.rtex", $"{baseKey}.png", $"{baseKey}.webp" };

			foreach (var candidate in candidates)
			{
				if (metadata.Decals.TryGetValue(candidate, out var meta))
				{
					foundMeta = meta;
					break;
				}
			}

			if (foundMeta == null) return;

			MergeMetadataJson(foundMeta, result);
		}
		catch
		{
		}
	}

	private static void MergeMetadataJson(DecalMetadata foundMeta, JsonObject result)
	{
		var jsonMeta = JsonNode.Parse(JsonSerializer.Serialize(foundMeta)) as JsonObject;
		if (jsonMeta == null) return;

		foreach (var kvp in jsonMeta)
		{
			if (!result.ContainsKey(kvp.Key))
			{
				result[kvp.Key] = kvp.Value?.DeepClone();
			}
		}
	}

	private static void ResolveFromRtexFile(string decalKey, JsonObject result)
	{
		if (result.ContainsKey("brightness") || result.ContainsKey("tint")) return;

		try
		{
			string targetTexture = GetTargetTexturePath(decalKey, result);
			string wsPath = MapWorkspaceService.GetActiveWorkspacePath();

			ResolveRtexCandidates(wsPath, targetTexture, result);
		}
		catch
		{
		}
	}

	private static string GetTargetTexturePath(string decalKey, JsonObject result)
	{
		if (result.TryGetPropertyValue("texture_path", out var tpNode) && !string.IsNullOrWhiteSpace(tpNode?.ToString()))
		{
			return tpNode.ToString()!;
		}

		if (result.TryGetPropertyValue("TexturePath", out var tpNode2) && !string.IsNullOrWhiteSpace(tpNode2?.ToString()))
		{
			return tpNode2.ToString()!;
		}

		return decalKey;
	}

	private static void ResolveRtexCandidates(string wsPath, string targetTexture, JsonObject result)
	{
		string filename = Path.GetFileName(targetTexture);
		string baseKey = Path.GetFileNameWithoutExtension(targetTexture);
		string[] candidates = new[]
		{
			Path.Combine(wsPath, "Assets", "decals", targetTexture),
			Path.Combine(wsPath, "Assets", "decals", filename),
			Path.Combine(wsPath, "Assets", "decals", $"{baseKey}.rtex"),
			Path.Combine(wsPath, "Assets", $"{baseKey}.rtex")
		};

		foreach (var path in candidates)
		{
			if (TryResolveRtexFile(path, result)) break;
		}
	}

	private static bool TryResolveRtexFile(string path, JsonObject result)
	{
		if (!File.Exists(path) || !path.EndsWith(".rtex", StringComparison.OrdinalIgnoreCase)) return false;

		byte[] bytes = File.ReadAllBytes(path);
		if (!RtexFile.IsRtexBytes(bytes)) return false;

		var (customJson, _, _) = RtexFile.Parse(bytes);
		if (string.IsNullOrEmpty(customJson)) return false;

		var rtexMeta = JsonNode.Parse(customJson)?.AsObject();
		if (rtexMeta == null) return false;

		foreach (var kvp in rtexMeta)
		{
			if (!result.ContainsKey(kvp.Key))
			{
				result[kvp.Key] = kvp.Value?.DeepClone();
			}
		}

		return true;
	}

	private static void ResolveFromInWorldDecal(string decalKey, JsonObject result)
	{
		if (result.ContainsKey("brightness")) return;
		if (result.ContainsKey("tint")) return;
		if (GameHost.Instance == null) return;
		if (GameHost.Instance.AllDecals == null) return;

		string baseKey = Path.GetFileNameWithoutExtension(decalKey);
		foreach (var d in GameHost.Instance.AllDecals)
		{
			if (TryMatchAndApplyDecal(d, decalKey, baseKey, result))
			{
				break;
			}
		}
	}

	private static bool TryMatchAndApplyDecal(Decal d, string decalKey, string baseKey, JsonObject result)
	{
		if (d == null) return false;
		if (!GodotObject.IsInstanceValid(d)) return false;
		if (!(d is Decal3D d3d)) return false;

		string dId = d3d.DecalId;
		string dBase = Path.GetFileNameWithoutExtension(dId);

		if (dId.Equals(decalKey, StringComparison.OrdinalIgnoreCase))
		{
			ApplyDecalStateToResult(d, result);
			return true;
		}

		if (dBase.Equals(baseKey, StringComparison.OrdinalIgnoreCase))
		{
			ApplyDecalStateToResult(d, result);
			return true;
		}

		return false;
	}

	private static void ApplyDecalStateToResult(Decal d, JsonObject result)
	{
		result["albedo_mix"] = d.AlbedoMix;
		result["tint"] = $"#{d.Modulate.ToHtml(false)}";
		result["opacity"] = d.Modulate.A;
		result["normal_strength"] = (d.TextureNormal != null) ? 1.0f : 0.0f;
		result["roughness"] = 1.0f;
		result["metallic"] = 0.0f;
		result["upper_fade"] = d.UpperFade;
		result["lower_fade"] = d.LowerFade;
		result["normal_fade"] = d.NormalFade;
		result["distance_fade_enabled"] = d.DistanceFadeEnabled;
		result["distance_fade_begin"] = d.DistanceFadeBegin;
		result["distance_fade_end"] = d.DistanceFadeBegin + d.DistanceFadeLength;
		result["projection_size"] = d.Size.X;
		result["projection_height"] = d.Size.Z;

		if (d is Decal3D placementDecal)
		{
			result["PlacementMode"] = (int)(placementDecal.GroundProjected ? VfxPlacementMode.GroundProjected : VfxPlacementMode.Free);
			result["ground_offset"] = placementDecal.GroundOffset;
		}

		if (d.TextureEmission != null)
		{
			result["blend_mode"] = d.AlbedoMix <= 0.01f ? "Additive" : "Screen";
		}
		else
		{
			result["blend_mode"] = "Mix";
		}

		if (d is Decal3D d3dAnim)
		{
			result["animate_opacity"] = d3dAnim.Animation.AnimateOpacity;
			result["opacity_pulse_speed"] = d3dAnim.Animation.OpacityPulseSpeed;
			result["min_opacity"] = d3dAnim.Animation.MinOpacity;
			result["max_opacity"] = d3dAnim.Animation.MaxOpacity;
			result["animate_emission"] = d3dAnim.Animation.AnimateEmission;
			result["emission_pulse_speed"] = d3dAnim.Animation.EmissionPulseSpeed;
			result["min_emission"] = d3dAnim.Animation.MinEmission;
			result["max_emission"] = d3dAnim.Animation.MaxEmission;
			result["animate_scale"] = d3dAnim.Animation.AnimateScale;
			result["scale_pulse_speed"] = d3dAnim.Animation.ScalePulseSpeed;
			result["min_scale_ratio"] = d3dAnim.Animation.MinScaleRatio;
			result["max_scale_ratio"] = d3dAnim.Animation.MaxScaleRatio;
		}
	}

	public void OpenForDecal(string decalKey, JsonObject decalData, Action<JsonObject> onApplied)
	{
		var (_, parsedSlug) = TemplateIDHelper.ParseTemplateID(decalKey);
		_slug = !string.IsNullOrWhiteSpace(parsedSlug) ? TemplateIDHelper.ToSnakeCase(parsedSlug) : TemplateIDHelper.ToSnakeCase(decalKey);
		_decalKey = TemplateIDHelper.NormalizeTemplateID("decal", _slug);
		_onApplied = onApplied;
		_lblDecalName.Text = TranslationServer.Translate("TemplateID:") + " " + _decalKey;

		var resolvedData = ResolveDecalMetadata(decalKey, decalData);

		_texturePath = ResolveTexturePath(decalKey, resolvedData);

		_brightness = GetFloat(resolvedData, "brightness", 1.0f);
		_contrast = GetFloat(resolvedData, "contrast", 1.0f);
		_saturation = GetFloat(resolvedData, "saturation", 1.0f);
		_opacity = GetFloat(resolvedData, "opacity", 1.0f);
		_albedoMix = GetFloat(resolvedData, "albedo_mix", 1.0f);
		_normalStrength = GetFloat(resolvedData, "normal_strength", 0.0f);
		_roughness = GetFloat(resolvedData, "roughness", 1.0f);
		_metallic = GetFloat(resolvedData, "metallic", 0.0f);
		_blendMode = GetString(resolvedData, "blend_mode", "Mix");

		_animateOpacity = GetBool(resolvedData, "animate_opacity", false);
		_opacityPulseSpeed = GetFloat(resolvedData, "opacity_pulse_speed", 1.0f);
		_minOpacity = GetFloat(resolvedData, "min_opacity", 0.2f);
		_maxOpacity = GetFloat(resolvedData, "max_opacity", 1.0f);

		_animateEmission = GetBool(resolvedData, "animate_emission", false);
		_emissionPulseSpeed = GetFloat(resolvedData, "emission_pulse_speed", 1.0f);
		_minEmission = GetFloat(resolvedData, "min_emission", 0.0f);
		_maxEmission = GetFloat(resolvedData, "max_emission", 2.0f);

		_animateScale = GetBool(resolvedData, "animate_scale", false);
		_scalePulseSpeed = GetFloat(resolvedData, "scale_pulse_speed", 1.0f);
		_minScaleRatio = GetFloat(resolvedData, "min_scale_ratio", 0.8f);
		_maxScaleRatio = GetFloat(resolvedData, "max_scale_ratio", 1.2f);

		_upperFade = GetFloat(resolvedData, "upper_fade", 0.3f);
		_lowerFade = GetFloat(resolvedData, "lower_fade", 0.3f);

		_placementMode = GetPlacementMode(resolvedData, "PlacementMode", "placement_mode", VfxPlacementMode.Free);
		_projectionSize = GetFloat(resolvedData, "projection_size", 0.0f);
		_projectionHeight = GetFloat(resolvedData, "projection_height", 0.0f);
		_groundOffset = GetFloat(resolvedData, "ground_offset", 0.05f);
		_normalFade = GetFloat(resolvedData, "normal_fade", 0.0f);
		_distanceFadeEnabled = GetBool(resolvedData, "distance_fade_enabled", false);
		_distanceFadeBegin = GetFloat(resolvedData, "distance_fade_begin", 10.0f);
		_distanceFadeEnd = GetFloat(resolvedData, "distance_fade_end", 30.0f);

		_tint = Colors.White;
		if (resolvedData.TryGetPropertyValue("tint", out var tNode) && tNode != null)
		{
			string tStr = tNode.ToString();
			if (tStr.StartsWith("#")) _tint = Color.FromHtml(tStr);
		}

		TrackOriginalDecalStates(decalKey);
		TakeInitialSnapshot();

		SyncControlsWithValues();
		ReloadBaseTexture();
		UpdateLivePreviewAndWorld();
		OpenDialog();
	}

	protected override string GetRandomizeTemplateSlug()
	{
		return !string.IsNullOrEmpty(_decalKey) ? _decalKey : $"decal/{_slug}";
	}

	protected override void OnAssetsRandomized()
	{
		string targetId = GetRandomizeTemplateSlug();
		string wsPath = MapWorkspaceService.GetActiveWorkspacePath();

		var metadata = MapFileService.LoadMetadata(wsPath);
		if (metadata?.Decals != null)
		{
			var (_, curSlug) = TemplateIDHelper.ParseTemplateID(targetId);
			Realm.Shared.Metadata.DecalMetadata? foundMeta = null;
			if (metadata.Decals.TryGetValue(targetId, out foundMeta) ||
			    (!string.IsNullOrEmpty(curSlug) && metadata.Decals.TryGetValue(curSlug, out foundMeta)) ||
			    (!string.IsNullOrEmpty(_texturePath) && metadata.Decals.TryGetValue(_texturePath, out foundMeta)))
			{
				if (foundMeta != null && !string.IsNullOrEmpty(foundMeta.TexturePath))
				{
					_texturePath = foundMeta.TexturePath;
				}
			}
		}

		SyncControlsWithValues();
		ReloadBaseTexture();
		UpdateLivePreviewAndWorld();
	}

	private string ResolveTexturePath(string decalKey, JsonObject resolvedData)
	{
		string rawTexturePath = GetRawTexturePath(resolvedData);

		if (string.IsNullOrWhiteSpace(rawTexturePath) && decalKey.EndsWith(".rtex", StringComparison.OrdinalIgnoreCase))
		{
			rawTexturePath = decalKey;
		}

		if (string.IsNullOrWhiteSpace(rawTexturePath))
		{
			rawTexturePath = ResolveFallbackTexturePath();
		}

		return rawTexturePath ?? string.Empty;
	}

	private string GetRawTexturePath(JsonObject resolvedData)
	{
		if (resolvedData.TryGetPropertyValue("texture_path", out var tpNode) && !string.IsNullOrWhiteSpace(tpNode?.ToString()))
		{
			return tpNode.ToString()!;
		}

		if (resolvedData.TryGetPropertyValue("TexturePath", out var tpNode2) && !string.IsNullOrWhiteSpace(tpNode2?.ToString()))
		{
			return tpNode2.ToString()!;
		}

		return string.Empty;
	}

	private string ResolveFallbackTexturePath()
	{
		string wsPath = MapWorkspaceService.GetActiveWorkspacePath();
		string candidate = $"{_slug}.rtex";

		if (File.Exists(Path.Combine(wsPath, "Assets", "decals", candidate)))
		{
			return candidate;
		}

		var candidates = ScanAvailableAssets("decals", true);
		string candidateMatch = candidates.FirstOrDefault(c => string.Equals(c, candidate, StringComparison.OrdinalIgnoreCase))
		                        ?? candidates.FirstOrDefault(c => string.Equals(Path.GetFileNameWithoutExtension(c), _slug, StringComparison.OrdinalIgnoreCase));

		return !string.IsNullOrEmpty(candidateMatch) ? candidateMatch : string.Empty;
	}

	private float GetFloat(JsonObject data, string key, float defaultVal)
	{
		return data.TryGetPropertyValue(key, out var node) && float.TryParse(node?.ToString(), out float val) ? val : defaultVal;
	}

	private bool GetBool(JsonObject data, string key, bool defaultVal)
	{
		return data.TryGetPropertyValue(key, out var node) && bool.TryParse(node?.ToString(), out bool val) ? val : defaultVal;
	}

	private string GetString(JsonObject data, string key, string defaultVal)
	{
		return data.TryGetPropertyValue(key, out var node) ? node?.ToString() ?? defaultVal : defaultVal;
	}

	private VfxPlacementMode GetPlacementMode(JsonObject data, string key, string alternateKey, VfxPlacementMode defaultVal)
	{
		foreach (string candidateKey in new[] { key, alternateKey })
		{
			if (!data.TryGetPropertyValue(candidateKey, out var node) || node == null) continue;

			string raw = node.ToString();
			if (Enum.TryParse<VfxPlacementMode>(raw, true, out var parsedMode))
			{
				return parsedMode;
			}

			if (int.TryParse(raw, out int numericMode) && Enum.IsDefined(typeof(VfxPlacementMode), numericMode))
			{
				return (VfxPlacementMode)numericMode;
			}
		}

		return defaultVal;
	}

	private void TrackOriginalDecalStates(string decalKey)
	{
		_originalDecalStates.Clear();
		if (GameHost.Instance?.AllDecals == null) return;

		string baseKey = Path.GetFileNameWithoutExtension(decalKey);
		foreach (var d in GameHost.Instance.AllDecals)
		{
			if (d == null || !GodotObject.IsInstanceValid(d)) continue;

			string dId = d is Decal3D d3d ? d3d.DecalId : "";
			string dBase = Path.GetFileNameWithoutExtension(dId);
			if (dId.Equals(decalKey, StringComparison.OrdinalIgnoreCase) || dBase.Equals(baseKey, StringComparison.OrdinalIgnoreCase))
			{
				_originalDecalStates[d.GetInstanceId()] = new DecalNodeState
				{
					Modulate = d.Modulate,
					AlbedoMix = d.AlbedoMix,
					TextureNormal = d.TextureNormal,
					TextureOrm = d.TextureOrm,
					TextureEmission = d.TextureEmission,
					EmissionEnergy = d.EmissionEnergy,
					Size = d.Size,
					UpperFade = d.UpperFade,
					LowerFade = d.LowerFade,
					GroundProjected = d is Decal3D groundDecal && groundDecal.GroundProjected,
					GroundOffset = d is Decal3D offsetDecal ? offsetDecal.GroundOffset : 0.05f,
					NormalFade = d.NormalFade,
					DistanceFadeEnabled = d.DistanceFadeEnabled,
					DistanceFadeBegin = d.DistanceFadeBegin,
					DistanceFadeEnd = d.DistanceFadeBegin + d.DistanceFadeLength
				};
			}
		}
	}

	private void TakeInitialSnapshot()
	{
		_initialSnapshot = new DecalSnapshot
		{
			DecalId = _decalKey,
			TexturePath = _texturePath,
			Brightness = _brightness,
			Tint = _tint,
			Contrast = _contrast,
			Saturation = _saturation,
			Opacity = _opacity,
			AlbedoMix = _albedoMix,
			NormalStrength = _normalStrength,
			Roughness = _roughness,
			Metallic = _metallic,
			BlendMode = _blendMode,
			Animation = new DecalAnimationConfig
			{
				AnimateOpacity = _animateOpacity,
				OpacityPulseSpeed = _opacityPulseSpeed,
				MinOpacity = _minOpacity,
				MaxOpacity = _maxOpacity,
				AnimateEmission = _animateEmission,
				EmissionPulseSpeed = _emissionPulseSpeed,
				MinEmission = _minEmission,
				MaxEmission = _maxEmission,
				AnimateScale = _animateScale,
				ScalePulseSpeed = _scalePulseSpeed,
				MinScaleRatio = _minScaleRatio,
				MaxScaleRatio = _maxScaleRatio
			},
			UpperFade = _upperFade,
			LowerFade = _lowerFade,
			PlacementMode = _placementMode,
			ProjectionSize = _projectionSize,
			ProjectionHeight = _projectionHeight,
			GroundOffset = _groundOffset,
			NormalFade = _normalFade,
			DistanceFadeEnabled = _distanceFadeEnabled,
			DistanceFadeBegin = _distanceFadeBegin,
			DistanceFadeEnd = _distanceFadeEnd
		};

		string targetId = !string.IsNullOrEmpty(_decalKey) ? _decalKey : $"decal/{_slug}";
		_initialTemplateJson = GameContext.EditorService.SnapshotTemplateJson(targetId);
		_templateExistedInitially = _initialTemplateJson != null;
	}

	private void SyncControlsWithValues()
	{
		_isSyncingControls = true;
		try
		{
			SyncBaseControls();
			SyncMaterialControls();
			SyncAnimationControls();
			SyncFadeControls();
			SyncPlacementControls();
		}
		finally
		{
			_isSyncingControls = false;
		}
	}

	private void SyncBaseControls()
	{
		if (_txtSlug != null) _txtSlug.Text = _slug;
		_setTexturePathValue?.Invoke(_texturePath);

		SyncSlider(_sldBrightness, _lblBrightness, _brightness, "F2");
		if (_lblBrightness != null) _lblBrightness.Text += "x"; // Append x for brightness

		if (_btnTint != null) _btnTint.Color = _tint;

		SyncSlider(_sldContrast, _lblContrast, _contrast, "F2");
		if (_lblContrast != null) _lblContrast.Text += "x"; // Append x for contrast

		SyncSlider(_sldSaturation, _lblSaturation, _saturation, "F2");
		if (_lblSaturation != null) _lblSaturation.Text += "x"; // Append x for saturation

		SyncSlider(_sldOpacity, _lblOpacity, _opacity, "F2");
	}

	private void SyncSlider(HSlider? slider, Label? label, float value, string format)
	{
		if (slider != null) slider.Value = value;
		if (label != null) label.Text = value.ToString(format);
	}

	private void SyncMaterialControls()
	{
		SyncSlider(_sldAlbedoMix, _lblAlbedoMix, _albedoMix, "F2");

		SyncSlider(_sldNormalStrength, _lblNormalStrength, _normalStrength, "F2");
		if (_lblNormalStrength != null) _lblNormalStrength.Text += "x"; // Append x for normal strength

		SyncSlider(_sldRoughness, _lblRoughness, _roughness, "F2");
		SyncSlider(_sldMetallic, _lblMetallic, _metallic, "F2");

		SyncBlendModeControl();
	}

	private void SyncBlendModeControl()
	{
		if (_optBlendMode == null) return;

		_optBlendMode.Selected = _blendMode switch
		{
			"Additive" => 1,
			"Multiply" => 2,
			"Screen" => 3,
			_ => 0
		};
	}

	private void SyncAnimationControls()
	{
		SyncOpacityAnimationControls();
		SyncEmissionAnimationControls();
		SyncScaleAnimationControls();
	}

	private void SyncOpacityAnimationControls()
	{
		if (_chkAnimateOpacity != null) _chkAnimateOpacity.ButtonPressed = _animateOpacity;

		SyncSlider(_sldOpacitySpeed, _lblOpacitySpeed, _opacityPulseSpeed, "F1");
		if (_lblOpacitySpeed != null) _lblOpacitySpeed.Text += "x"; // Append x for speed

		SyncSlider(_sldMinOpacity, _lblMinOpacity, _minOpacity, "F2");
		SyncSlider(_sldMaxOpacity, _lblMaxOpacity, _maxOpacity, "F2");
	}

	private void SyncEmissionAnimationControls()
	{
		if (_chkAnimateEmission != null) _chkAnimateEmission.ButtonPressed = _animateEmission;

		SyncSlider(_sldEmissionSpeed, _lblEmissionSpeed, _emissionPulseSpeed, "F1");
		if (_lblEmissionSpeed != null) _lblEmissionSpeed.Text += "x"; // Append x for speed

		SyncSlider(_sldMinEmission, _lblMinEmission, _minEmission, "F1");
		SyncSlider(_sldMaxEmission, _lblMaxEmission, _maxEmission, "F1");
	}

	private void SyncScaleAnimationControls()
	{
		if (_chkAnimateScale != null) _chkAnimateScale.ButtonPressed = _animateScale;

		SyncSlider(_sldScaleSpeed, _lblScaleSpeed, _scalePulseSpeed, "F1");
		if (_lblScaleSpeed != null) _lblScaleSpeed.Text += "x"; // Append x for speed

		SyncSlider(_sldMinScale, _lblMinScale, _minScaleRatio, "F2");
		if (_lblMinScale != null) _lblMinScale.Text += "x"; // Append x for scale ratio

		SyncSlider(_sldMaxScale, _lblMaxScale, _maxScaleRatio, "F2");
		if (_lblMaxScale != null) _lblMaxScale.Text += "x"; // Append x for scale ratio
	}

	private void SyncFadeControls()
	{
		if (_sldUpperFade != null)
		{
			_sldUpperFade.Value = _upperFade;
			_lblUpperFade.Text = $"{_upperFade:F2}";
		}

		if (_sldLowerFade != null)
		{
			_sldLowerFade.Value = _lowerFade;
			_lblLowerFade.Text = $"{_lowerFade:F2}";
		}
	}

	private void SyncPlacementControls()
	{
		if (_optPlacementMode != null)
		{
			_optPlacementMode.Selected = _placementMode == VfxPlacementMode.GroundProjected ? 1 : 0;
		}

		SyncSlider(_sldProjectionSize, _lblProjectionSize, _projectionSize, "F1");
		SyncSlider(_sldProjectionHeight, _lblProjectionHeight, _projectionHeight, "F1");
		SyncSlider(_sldGroundOffset, _lblGroundOffset, _groundOffset, "F2");
		SyncSlider(_sldNormalFade, _lblNormalFade, _normalFade, "F2");

		if (_chkDistanceFade != null) _chkDistanceFade.ButtonPressed = _distanceFadeEnabled;
		SyncSlider(_sldDistanceFadeBegin, _lblDistanceFadeBegin, _distanceFadeBegin, "F0");
		SyncSlider(_sldDistanceFadeEnd, _lblDistanceFadeEnd, _distanceFadeEnd, "F0");
	}

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
		if (_lblBrightness != null) _lblBrightness.Text += "x"; // Append x

		UpdateLabelText(_lblContrast, _contrast, "F2");
		if (_lblContrast != null) _lblContrast.Text += "x"; // Append x

		UpdateLabelText(_lblSaturation, _saturation, "F2");
		if (_lblSaturation != null) _lblSaturation.Text += "x"; // Append x

		UpdateLabelText(_lblOpacity, _opacity, "F2");
		UpdateLabelText(_lblAlbedoMix, _albedoMix, "F2");

		UpdateLabelText(_lblNormalStrength, _normalStrength, "F2");
		if (_lblNormalStrength != null) _lblNormalStrength.Text += "x"; // Append x

		UpdateLabelText(_lblRoughness, _roughness, "F2");
		UpdateLabelText(_lblMetallic, _metallic, "F2");

		UpdateLabelText(_lblOpacitySpeed, _opacityPulseSpeed, "F1");
		if (_lblOpacitySpeed != null) _lblOpacitySpeed.Text += "x"; // Append x

		UpdateLabelText(_lblMinOpacity, _minOpacity, "F2");
		UpdateLabelText(_lblMaxOpacity, _maxOpacity, "F2");

		UpdateLabelText(_lblEmissionSpeed, _emissionPulseSpeed, "F1");
		if (_lblEmissionSpeed != null) _lblEmissionSpeed.Text += "x"; // Append x

		UpdateLabelText(_lblMinEmission, _minEmission, "F1");
		UpdateLabelText(_lblMaxEmission, _maxEmission, "F1");

		UpdateLabelText(_lblScaleSpeed, _scalePulseSpeed, "F1");
		if (_lblScaleSpeed != null) _lblScaleSpeed.Text += "x"; // Append x

		UpdateLabelText(_lblMinScale, _minScaleRatio, "F2");
		if (_lblMinScale != null) _lblMinScale.Text += "x"; // Append x

		UpdateLabelText(_lblMaxScale, _maxScaleRatio, "F2");
		if (_lblMaxScale != null) _lblMaxScale.Text += "x"; // Append x

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
		GameHost.Instance?.RefreshDecalsLive(
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
		string finalSlug = !string.IsNullOrWhiteSpace(_txtSlug?.Text) ? TemplateIDHelper.ToSnakeCase(_txtSlug.Text) : _slug;
		if (string.IsNullOrWhiteSpace(finalSlug)) finalSlug = _slug;
		_slug = finalSlug;
		_decalKey = TemplateIDHelper.NormalizeTemplateID("decal", _slug);

		string finalTexturePath = !string.IsNullOrWhiteSpace(_txtTexturePath?.Text) ? _txtTexturePath.Text.Trim() : _texturePath;
		_texturePath = finalTexturePath;

		GameHost.Instance?.InvalidateDecalCache(_decalKey);

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

	private bool TryRestoreOriginalStates()
	{
		if (_originalDecalStates.Count == 0 || GameHost.Instance?.AllDecals == null) return false;

		foreach (var d in GameHost.Instance.AllDecals)
		{
			if (d == null || !GodotObject.IsInstanceValid(d) || !_originalDecalStates.TryGetValue(d.GetInstanceId(), out var orig)) continue;

			d.Modulate = orig.Modulate;
			d.AlbedoMix = orig.AlbedoMix;
			d.TextureNormal = orig.TextureNormal;
			d.TextureOrm = orig.TextureOrm;
			d.TextureEmission = orig.TextureEmission;
			d.EmissionEnergy = orig.EmissionEnergy;
			d.Size = orig.Size;
			d.UpperFade = orig.UpperFade;
			d.LowerFade = orig.LowerFade;
			d.NormalFade = orig.NormalFade;
			d.DistanceFadeEnabled = orig.DistanceFadeEnabled;
			d.DistanceFadeBegin = orig.DistanceFadeBegin;
			d.DistanceFadeLength = Mathf.Max(0.1f, orig.DistanceFadeEnd - orig.DistanceFadeBegin);

			if (d is Decal3D d3d)
			{
				d3d.SetBaseProperties(orig.Modulate, orig.EmissionEnergy, orig.Size);
				d3d.SetGroundProjection(orig.GroundProjected, orig.GroundOffset);
				d3d.UpdateProcessState();
			}
		}

		return true;
	}

	private void RestoreInitialSnapshot()
	{
		if (_initialSnapshot == null) return;

		_decalKey = _initialSnapshot.DecalId;
		var (_, initSlug) = TemplateIDHelper.ParseTemplateID(_decalKey);
		_slug = !string.IsNullOrWhiteSpace(initSlug) ? TemplateIDHelper.ToSnakeCase(initSlug) : TemplateIDHelper.ToSnakeCase(_decalKey);

		if (_txtSlug != null) _txtSlug.Text = _slug;
		if (_lblDecalName != null) _lblDecalName.Text = TranslationServer.Translate("TemplateID:") + " " + _decalKey;

		_texturePath = _initialSnapshot.TexturePath;
		_brightness = _initialSnapshot.Brightness;
		_tint = _initialSnapshot.Tint;
		_contrast = _initialSnapshot.Contrast;
		_saturation = _initialSnapshot.Saturation;
		_opacity = _initialSnapshot.Opacity;
		_albedoMix = _initialSnapshot.AlbedoMix;
		_normalStrength = _initialSnapshot.NormalStrength;
		_roughness = _initialSnapshot.Roughness;
		_metallic = _initialSnapshot.Metallic;
		_blendMode = _initialSnapshot.BlendMode;
		_animateOpacity = _initialSnapshot.Animation.AnimateOpacity;
		_opacityPulseSpeed = _initialSnapshot.Animation.OpacityPulseSpeed;
		_minOpacity = _initialSnapshot.Animation.MinOpacity;
		_maxOpacity = _initialSnapshot.Animation.MaxOpacity;
		_animateEmission = _initialSnapshot.Animation.AnimateEmission;
		_emissionPulseSpeed = _initialSnapshot.Animation.EmissionPulseSpeed;
		_minEmission = _initialSnapshot.Animation.MinEmission;
		_maxEmission = _initialSnapshot.Animation.MaxEmission;
		_animateScale = _initialSnapshot.Animation.AnimateScale;
		_scalePulseSpeed = _initialSnapshot.Animation.ScalePulseSpeed;
		_minScaleRatio = _initialSnapshot.Animation.MinScaleRatio;
		_maxScaleRatio = _initialSnapshot.Animation.MaxScaleRatio;
		_upperFade = _initialSnapshot.UpperFade;
		_lowerFade = _initialSnapshot.LowerFade;
		_placementMode = _initialSnapshot.PlacementMode;
		_projectionSize = _initialSnapshot.ProjectionSize;
		_projectionHeight = _initialSnapshot.ProjectionHeight;
		_groundOffset = _initialSnapshot.GroundOffset;
		_normalFade = _initialSnapshot.NormalFade;
		_distanceFadeEnabled = _initialSnapshot.DistanceFadeEnabled;
		_distanceFadeBegin = _initialSnapshot.DistanceFadeBegin;
		_distanceFadeEnd = _initialSnapshot.DistanceFadeEnd;

		string wsPath = MapWorkspaceService.GetActiveWorkspacePath();
		GameContext.MetadataService.UpdateMetadata(wsPath, m =>
		{
			m.Decals ??= new(StringComparer.OrdinalIgnoreCase);
			m.Decals[_decalKey] = new Realm.Shared.Metadata.DecalMetadata
			{
				TexturePath = _texturePath,
				Brightness = _brightness,
				Tint = $"#{_tint.ToHtml(false)}",
				Contrast = _contrast,
				Saturation = _saturation,
				Opacity = _opacity,
				AlbedoMix = _albedoMix,
				NormalStrength = _normalStrength,
				Roughness = _roughness,
				Metallic = _metallic,
				BlendMode = _blendMode,
				AnimateOpacity = _animateOpacity,
				OpacityPulseSpeed = _opacityPulseSpeed,
				MinOpacity = _minOpacity,
				MaxOpacity = _maxOpacity,
				AnimateEmission = _animateEmission,
				EmissionPulseSpeed = _emissionPulseSpeed,
				MinEmission = _minEmission,
				MaxEmission = _maxEmission,
				AnimateScale = _animateScale,
				ScalePulseSpeed = _scalePulseSpeed,
				MinScaleRatio = _minScaleRatio,
				MaxScaleRatio = _maxScaleRatio,
				UpperFade = _upperFade,
				LowerFade = _lowerFade,
				PlacementMode = _placementMode,
				ProjectionSize = _projectionSize,
				ProjectionHeight = _projectionHeight,
				GroundOffset = _groundOffset,
				NormalFade = _normalFade,
				DistanceFadeEnabled = _distanceFadeEnabled,
				DistanceFadeBegin = _distanceFadeBegin,
				DistanceFadeEnd = _distanceFadeEnd
			};
		});

		ReloadBaseTexture();
		UpdateLivePreviewAndWorld();
	}
}