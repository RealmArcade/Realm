using Godot;
using System;
using System.IO;
using System.Text.Json.Nodes;

public class DecalSnapshot
{
	public string DecalId { get; set; } = "";
	public string TexturePath { get; set; } = "";
	public float Brightness { get; set; } = 1.0f;
	public Color Tint { get; set; } = Colors.White;
	public float Contrast { get; set; } = 1.0f;
	public float Saturation { get; set; } = 1.0f;
	public float Opacity { get; set; } = 1.0f;
	public float AlbedoMix { get; set; } = 1.0f;
	public float NormalStrength { get; set; } = 0.0f;
	public float Roughness { get; set; } = 1.0f;
	public float Metallic { get; set; } = 0.0f;
	public string BlendMode { get; set; } = "Mix";

	public bool AnimateOpacity { get; set; } = false;
	public float OpacityPulseSpeed { get; set; } = 1.0f;
	public float MinOpacity { get; set; } = 0.2f;
	public float MaxOpacity { get; set; } = 1.0f;

	public bool AnimateEmission { get; set; } = false;
	public float EmissionPulseSpeed { get; set; } = 1.0f;
	public float MinEmission { get; set; } = 0.0f;
	public float MaxEmission { get; set; } = 2.0f;

	public bool AnimateScale { get; set; } = false;
	public float ScalePulseSpeed { get; set; } = 1.0f;
	public float MinScaleRatio { get; set; } = 0.8f;
	public float MaxScaleRatio { get; set; } = 1.2f;

	public float UpperFade { get; set; } = 0.3f;
	public float LowerFade { get; set; } = 0.3f;

	public DecalSnapshot Clone()
	{
		return new DecalSnapshot
		{
			DecalId = this.DecalId,
			TexturePath = this.TexturePath,
			Brightness = this.Brightness,
			Tint = this.Tint,
			Contrast = this.Contrast,
			Saturation = this.Saturation,
			Opacity = this.Opacity,
			AlbedoMix = this.AlbedoMix,
			NormalStrength = this.NormalStrength,
			Roughness = this.Roughness,
			Metallic = this.Metallic,
			BlendMode = this.BlendMode,
			AnimateOpacity = this.AnimateOpacity,
			OpacityPulseSpeed = this.OpacityPulseSpeed,
			MinOpacity = this.MinOpacity,
			MaxOpacity = this.MaxOpacity,
			AnimateEmission = this.AnimateEmission,
			EmissionPulseSpeed = this.EmissionPulseSpeed,
			MinEmission = this.MinEmission,
			MaxEmission = this.MaxEmission,
			AnimateScale = this.AnimateScale,
			ScalePulseSpeed = this.ScalePulseSpeed,
			MinScaleRatio = this.MinScaleRatio,
			MaxScaleRatio = this.MaxScaleRatio,
			UpperFade = this.UpperFade,
			LowerFade = this.LowerFade
		};
	}
}

public partial class DecalSettingsDialog : FloatingDialogBase
{
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

	private bool _isSyncingControls = false;
	private LineEdit _txtDecalId;
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

	private double _previewAnimTime = 0.0;

	private struct DecalNodeState
	{
		public Color Modulate;
		public float AlbedoMix;
		public Texture2D TextureNormal;
		public Texture2D TextureOrm;
		public Texture2D TextureEmission;
		public float EmissionEnergy;
		public Vector3 Size;
		public float UpperFade;
		public float LowerFade;
	}

	private readonly System.Collections.Generic.Dictionary<ulong, DecalNodeState> _originalDecalStates = new();

	private DecalSnapshot _initialSnapshot;
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
		contentVBox.AddThemeConstantOverride("separation", 10);
		contentVBox.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
		scrollBody.AddChild(contentVBox);

		_lblDecalName = new Label();
		_lblDecalName.AddThemeFontSizeOverride("font_size", 12);
		_lblDecalName.AddThemeColorOverride("font_color", new Color(0.95f, 0.82f, 0.55f));
		_lblDecalName.HorizontalAlignment = HorizontalAlignment.Center;
		contentVBox.AddChild(_lblDecalName);

		// PREVIEW BOX
		var previewPanel = new PanelContainer();
		previewPanel.AddThemeStyleboxOverride("panel", UIStyle.CreateLightInnerPanel());
		previewPanel.CustomMinimumSize = new Vector2(0, 130);

		var previewVBox = new VBoxContainer();
		previewVBox.Alignment = BoxContainer.AlignmentMode.Center;

		_previewRect = new TextureRect();
		_previewRect.CustomMinimumSize = new Vector2(100, 100);
		_previewRect.ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize;
		_previewRect.StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered;
		_previewRect.SizeFlagsHorizontal = Control.SizeFlags.ShrinkCenter;
		_previewRect.SizeFlagsVertical = Control.SizeFlags.ShrinkCenter;

		previewVBox.AddChild(_previewRect);
		previewPanel.AddChild(previewVBox);
		contentVBox.AddChild(previewPanel);

		// SECTION 1: TEXTURE & ASSET
		AddSectionHeader(contentVBox, "🖼 " + TranslationServer.Translate("TEXTURE & ASSET"), new Color(0.95f, 0.8f, 0.4f));

		_txtDecalId = AddTextInput(
			contentVBox,
			TranslationServer.Translate("Decal ID:"),
			_decalKey,
			(val) =>
			{
				if (_isSyncingControls) return;
				_decalKey = val?.Trim() ?? string.Empty;
				if (_lblDecalName != null) _lblDecalName.Text = TranslationServer.Translate("Decal ID:") + " " + _decalKey;
			},
			TranslationServer.Translate("Enter arbitrary Decal ID..."),
			140f
		);

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

		// SECTION 2: COLOR & LIGHTING
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

		var rowTint = new HBoxContainer();
		rowTint.AddThemeConstantOverride("separation", 8);
		var lblTint = new Label();
		lblTint.Text = TranslationServer.Translate("Tint Color:");
		lblTint.CustomMinimumSize = new Vector2(140, 0);
		lblTint.AddThemeFontSizeOverride("font_size", 11);
		rowTint.AddChild(lblTint);

		_btnTint = new ColorPickerButton();
		_btnTint.CustomMinimumSize = new Vector2(90, 24);
		_btnTint.Color = _tint;
		_btnTint.ColorChanged += (newCol) =>
		{
			if (_isSyncingControls) return;
			_tint = newCol;
			UpdateLivePreviewAndWorld();
		};
		rowTint.AddChild(_btnTint);
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

		// SECTION 2: BLENDING & TRANSPARENCY
		AddSectionHeader(contentVBox, "✨ " + TranslationServer.Translate("BLENDING & MATERIAL"), new Color(0.5f, 0.85f, 1.0f));

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

		// SECTION 3: PROPERTY ANIMATION
		AddSectionHeader(contentVBox, "⚡ " + TranslationServer.Translate("DYNAMIC PROPERTY ANIMATION"), new Color(0.4f, 0.8f, 0.95f));

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
		if (_baseTexture == null)
		{
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
				if (File.Exists(p) && p.EndsWith(".rtex", StringComparison.OrdinalIgnoreCase))
				{
					byte[] rtexBytes = File.ReadAllBytes(p);
					byte[]? webpBytes = Realm.Shared.Textures.RtexFile.GetLayer(rtexBytes, 0);
					if (webpBytes != null && webpBytes.Length > 0)
					{
						var img = Image.CreateEmpty(1, 1, false, Image.Format.Rgba8);
						if (img.LoadWebpFromBuffer(webpBytes) == Error.Ok || img.LoadPngFromBuffer(webpBytes) == Error.Ok)
						{
							_baseTexture = ImageTexture.CreateFromImage(img);
							break;
						}
					}
				}
			}
		}
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

		// 1. Check metadata.json / unioned assets if properties are missing
		try
		{
			string wsPath = MapWorkspaceService.GetActiveWorkspacePath();
			var metadata = Realm.Shared.Services.MapFileService.LoadMetadata(wsPath);
			if (metadata?.Decals != null)
			{
				string key = Path.GetFileName(decalKey);
				string baseKey = Path.GetFileNameWithoutExtension(decalKey);

				Realm.Shared.Metadata.DecalMetadata? foundMeta = null;
				if (metadata.Decals.TryGetValue(decalKey, out var d0)) foundMeta = d0;
				else if (metadata.Decals.TryGetValue(key, out var d1)) foundMeta = d1;
				else if (metadata.Decals.TryGetValue(baseKey, out var d2)) foundMeta = d2;
				else if (metadata.Decals.TryGetValue($"{baseKey}.rtex", out var d3)) foundMeta = d3;
				else if (metadata.Decals.TryGetValue($"{baseKey}.png", out var d4)) foundMeta = d4;
				else if (metadata.Decals.TryGetValue($"{baseKey}.webp", out var d5)) foundMeta = d5;

				if (foundMeta != null)
				{
					var jsonMeta = System.Text.Json.Nodes.JsonNode.Parse(System.Text.Json.JsonSerializer.Serialize(foundMeta)) as JsonObject;
					if (jsonMeta != null)
					{
						foreach (var kvp in jsonMeta)
						{
							if (!result.ContainsKey(kvp.Key))
							{
								result[kvp.Key] = kvp.Value?.DeepClone();
							}
						}
					}
				}
			}
		}
		catch { }

		// 2. Check .rtex file metadata if still missing
		if (!result.ContainsKey("brightness") && !result.ContainsKey("tint"))
		{
			try
			{
				string wsPath = MapWorkspaceService.GetActiveWorkspacePath();
				string targetTexture = result.TryGetPropertyValue("texture_path", out var tpNode) && !string.IsNullOrWhiteSpace(tpNode?.ToString())
					? tpNode.ToString()
					: (result.TryGetPropertyValue("TexturePath", out var tpNode2) && !string.IsNullOrWhiteSpace(tpNode2?.ToString())
						? tpNode2.ToString()
						: decalKey);

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
					if (File.Exists(path) && path.EndsWith(".rtex", StringComparison.OrdinalIgnoreCase))
					{
						byte[] bytes = File.ReadAllBytes(path);
						if (Realm.Shared.Textures.RtexFile.IsRtexBytes(bytes))
						{
							var (customJson, _, _) = Realm.Shared.Textures.RtexFile.Parse(bytes);
							if (!string.IsNullOrEmpty(customJson))
							{
								var rtexMeta = JsonNode.Parse(customJson)?.AsObject();
								if (rtexMeta != null)
								{
									foreach (var kvp in rtexMeta)
									{
										if (!result.ContainsKey(kvp.Key))
										{
											result[kvp.Key] = kvp.Value?.DeepClone();
										}
									}
									break;
								}
							}
						}
					}
				}
			}
			catch { }
		}

		// 3. Inspect existing in-world Decal node if still missing
		if (!result.ContainsKey("brightness") && !result.ContainsKey("tint") && GameHost.Instance?.AllDecals != null)
		{
			string baseKey = Path.GetFileNameWithoutExtension(decalKey);
			foreach (var d in GameHost.Instance.AllDecals)
			{
				if (d != null && GodotObject.IsInstanceValid(d))
				{
					string dId = d is Decal3D d3d ? d3d.DecalId : "";
					string dBase = Path.GetFileNameWithoutExtension(dId);
					if (dId.Equals(decalKey, StringComparison.OrdinalIgnoreCase) || dBase.Equals(baseKey, StringComparison.OrdinalIgnoreCase))
					{
						result["albedo_mix"] = d.AlbedoMix;
						result["tint"] = $"#{d.Modulate.ToHtml(false)}";
						result["opacity"] = d.Modulate.A;
						result["normal_strength"] = (d.TextureNormal != null) ? 1.0f : 0.0f;
						result["roughness"] = 1.0f;
						result["metallic"] = 0.0f;
						result["upper_fade"] = d.UpperFade;
						result["lower_fade"] = d.LowerFade;
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
							result["animate_opacity"] = d3dAnim.AnimateOpacity;
							result["opacity_pulse_speed"] = d3dAnim.OpacityPulseSpeed;
							result["min_opacity"] = d3dAnim.MinOpacity;
							result["max_opacity"] = d3dAnim.MaxOpacity;
							result["animate_emission"] = d3dAnim.AnimateEmission;
							result["emission_pulse_speed"] = d3dAnim.EmissionPulseSpeed;
							result["min_emission"] = d3dAnim.MinEmission;
							result["max_emission"] = d3dAnim.MaxEmission;
							result["animate_scale"] = d3dAnim.AnimateScale;
							result["scale_pulse_speed"] = d3dAnim.ScalePulseSpeed;
							result["min_scale_ratio"] = d3dAnim.MinScaleRatio;
							result["max_scale_ratio"] = d3dAnim.MaxScaleRatio;
						}
						break;
					}
				}
			}
		}

		return result;
	}

	public void OpenForDecal(string decalKey, JsonObject decalData, Action<JsonObject> onApplied)
	{
		_decalKey = decalKey;
		_onApplied = onApplied;
		_lblDecalName.Text = TranslationServer.Translate("Decal ID:") + " " + decalKey;

		var resolvedData = ResolveDecalMetadata(decalKey, decalData);

		string rawTexturePath = resolvedData.TryGetPropertyValue("texture_path", out var tpNode) && !string.IsNullOrWhiteSpace(tpNode?.ToString())
			? tpNode.ToString()
			: (resolvedData.TryGetPropertyValue("TexturePath", out var tpNode2) && !string.IsNullOrWhiteSpace(tpNode2?.ToString())
				? tpNode2.ToString()
				: string.Empty);

		if (string.IsNullOrWhiteSpace(rawTexturePath) && decalKey.EndsWith(".rtex", StringComparison.OrdinalIgnoreCase))
		{
			rawTexturePath = decalKey;
		}

		if (string.IsNullOrWhiteSpace(rawTexturePath))
		{
			string baseKey = Path.GetFileNameWithoutExtension(decalKey);
			string candidate = $"{baseKey}.rtex";
			string wsPath = MapWorkspaceService.GetActiveWorkspacePath();
			if (File.Exists(Path.Combine(wsPath, "Assets", "decals", candidate)))
			{
				rawTexturePath = candidate;
			}
		}

		_texturePath = rawTexturePath ?? string.Empty;

		_brightness = resolvedData.TryGetPropertyValue("brightness", out var bNode) && float.TryParse(bNode?.ToString(), out float b) ? b : 1.0f;
		_contrast = resolvedData.TryGetPropertyValue("contrast", out var cNode) && float.TryParse(cNode?.ToString(), out float c) ? c : 1.0f;
		_saturation = resolvedData.TryGetPropertyValue("saturation", out var sNode) && float.TryParse(sNode?.ToString(), out float s) ? s : 1.0f;
		_opacity = resolvedData.TryGetPropertyValue("opacity", out var oNode) && float.TryParse(oNode?.ToString(), out float o) ? o : 1.0f;
		_albedoMix = resolvedData.TryGetPropertyValue("albedo_mix", out var mNode) && float.TryParse(mNode?.ToString(), out float m) ? m : 1.0f;
		_normalStrength = resolvedData.TryGetPropertyValue("normal_strength", out var nNode) && float.TryParse(nNode?.ToString(), out float n) ? n : 0.0f;
		_roughness = resolvedData.TryGetPropertyValue("roughness", out var rNode) && float.TryParse(rNode?.ToString(), out float r) ? r : 1.0f;
		_metallic = resolvedData.TryGetPropertyValue("metallic", out var metNode) && float.TryParse(metNode?.ToString(), out float met) ? met : 0.0f;
		_blendMode = resolvedData.TryGetPropertyValue("blend_mode", out var bmNode) ? bmNode?.ToString() ?? "Mix" : "Mix";

		_animateOpacity = resolvedData.TryGetPropertyValue("animate_opacity", out var aoNode) && bool.TryParse(aoNode?.ToString(), out bool ao) && ao;
		_opacityPulseSpeed = resolvedData.TryGetPropertyValue("opacity_pulse_speed", out var opsNode) && float.TryParse(opsNode?.ToString(), out float ops) ? ops : 1.0f;
		_minOpacity = resolvedData.TryGetPropertyValue("min_opacity", out var minONode) && float.TryParse(minONode?.ToString(), out float minO) ? minO : 0.2f;
		_maxOpacity = resolvedData.TryGetPropertyValue("max_opacity", out var maxONode) && float.TryParse(maxONode?.ToString(), out float maxO) ? maxO : 1.0f;

		_animateEmission = resolvedData.TryGetPropertyValue("animate_emission", out var aeNode) && bool.TryParse(aeNode?.ToString(), out bool ae) && ae;
		_emissionPulseSpeed = resolvedData.TryGetPropertyValue("emission_pulse_speed", out var epsNode) && float.TryParse(epsNode?.ToString(), out float eps) ? eps : 1.0f;
		_minEmission = resolvedData.TryGetPropertyValue("min_emission", out var minENode) && float.TryParse(minENode?.ToString(), out float minE) ? minE : 0.0f;
		_maxEmission = resolvedData.TryGetPropertyValue("max_emission", out var maxENode) && float.TryParse(maxENode?.ToString(), out float maxE) ? maxE : 2.0f;

		_animateScale = resolvedData.TryGetPropertyValue("animate_scale", out var asNode) && bool.TryParse(asNode?.ToString(), out bool aSc) && aSc;
		_scalePulseSpeed = resolvedData.TryGetPropertyValue("scale_pulse_speed", out var scpsNode) && float.TryParse(scpsNode?.ToString(), out float scps) ? scps : 1.0f;
		_minScaleRatio = resolvedData.TryGetPropertyValue("min_scale_ratio", out var minScNode) && float.TryParse(minScNode?.ToString(), out float minSc) ? minSc : 0.8f;
		_maxScaleRatio = resolvedData.TryGetPropertyValue("max_scale_ratio", out var maxScNode) && float.TryParse(maxScNode?.ToString(), out float maxSc) ? maxSc : 1.2f;

		_upperFade = resolvedData.TryGetPropertyValue("upper_fade", out var ufNode) && float.TryParse(ufNode?.ToString(), out float uf) ? uf : 0.3f;
		_lowerFade = resolvedData.TryGetPropertyValue("lower_fade", out var lfNode) && float.TryParse(lfNode?.ToString(), out float lf) ? lf : 0.3f;

		_tint = Colors.White;
		if (resolvedData.TryGetPropertyValue("tint", out var tNode) && tNode != null)
		{
			string tStr = tNode.ToString();
			if (tStr.StartsWith("#")) _tint = Color.FromHtml(tStr);
		}

		_originalDecalStates.Clear();
		if (GameHost.Instance?.AllDecals != null)
		{
			string baseKey = Path.GetFileNameWithoutExtension(decalKey);
			foreach (var d in GameHost.Instance.AllDecals)
			{
				if (d != null && GodotObject.IsInstanceValid(d))
				{
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
							LowerFade = d.LowerFade
						};
					}
				}
			}
		}

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
			LowerFade = _lowerFade
		};

		SyncControlsWithValues();
		ReloadBaseTexture();
		UpdateLivePreviewAndWorld();
		OpenDialog();
	}

	private void SyncControlsWithValues()
	{
		_isSyncingControls = true;
		try
		{
			if (_txtDecalId != null) _txtDecalId.Text = _decalKey;
			_setTexturePathValue?.Invoke(_texturePath);
			_sldBrightness.Value = _brightness;
			_lblBrightness.Text = $"{_brightness:F2}x";
			_btnTint.Color = _tint;
			_sldContrast.Value = _contrast;
			_lblContrast.Text = $"{_contrast:F2}x";
			_sldSaturation.Value = _saturation;
			_lblSaturation.Text = $"{_saturation:F2}x";
			_sldOpacity.Value = _opacity;
			_lblOpacity.Text = $"{_opacity:F2}";
			_sldAlbedoMix.Value = _albedoMix;
			_lblAlbedoMix.Text = $"{_albedoMix:F2}";
			_sldNormalStrength.Value = _normalStrength;
			_lblNormalStrength.Text = $"{_normalStrength:F2}x";
			_sldRoughness.Value = _roughness;
			_lblRoughness.Text = $"{_roughness:F2}";
			_sldMetallic.Value = _metallic;
			_lblMetallic.Text = $"{_metallic:F2}";

			_optBlendMode.Selected = _blendMode switch
			{
				"Additive" => 1,
				"Multiply" => 2,
				"Screen" => 3,
				_ => 0
			};

			if (_chkAnimateOpacity != null) _chkAnimateOpacity.ButtonPressed = _animateOpacity;
			if (_sldOpacitySpeed != null)
			{
				_sldOpacitySpeed.Value = _opacityPulseSpeed;
				_lblOpacitySpeed.Text = $"{_opacityPulseSpeed:F1}x";
			}
			if (_sldMinOpacity != null)
			{
				_sldMinOpacity.Value = _minOpacity;
				_lblMinOpacity.Text = $"{_minOpacity:F2}";
			}
			if (_sldMaxOpacity != null)
			{
				_sldMaxOpacity.Value = _maxOpacity;
				_lblMaxOpacity.Text = $"{_maxOpacity:F2}";
			}

			if (_chkAnimateEmission != null) _chkAnimateEmission.ButtonPressed = _animateEmission;
			if (_sldEmissionSpeed != null)
			{
				_sldEmissionSpeed.Value = _emissionPulseSpeed;
				_lblEmissionSpeed.Text = $"{_emissionPulseSpeed:F1}x";
			}
			if (_sldMinEmission != null)
			{
				_sldMinEmission.Value = _minEmission;
				_lblMinEmission.Text = $"{_minEmission:F1}";
			}
			if (_sldMaxEmission != null)
			{
				_sldMaxEmission.Value = _maxEmission;
				_lblMaxEmission.Text = $"{_maxEmission:F1}";
			}

			if (_chkAnimateScale != null) _chkAnimateScale.ButtonPressed = _animateScale;
			if (_sldScaleSpeed != null)
			{
				_sldScaleSpeed.Value = _scalePulseSpeed;
				_lblScaleSpeed.Text = $"{_scalePulseSpeed:F1}x";
			}
			if (_sldMinScale != null)
			{
				_sldMinScale.Value = _minScaleRatio;
				_lblMinScale.Text = $"{_minScaleRatio:F2}x";
			}
			if (_sldMaxScale != null)
			{
				_sldMaxScale.Value = _maxScaleRatio;
				_lblMaxScale.Text = $"{_maxScaleRatio:F2}x";
			}

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
		finally
		{
			_isSyncingControls = false;
		}
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

			float r = _tint.R * _brightness * (1.0f + currentEmission * 0.5f);
			float g = _tint.G * _brightness * (1.0f + currentEmission * 0.5f);
			float b = _tint.B * _brightness * (1.0f + currentEmission * 0.5f);

			float lum = 0.2126f * r + 0.7152f * g + 0.0722f * b;
			r = lum + (r - lum) * _saturation;
			g = lum + (g - lum) * _saturation;
			b = lum + (b - lum) * _saturation;

			r = (r - 0.5f) * _contrast + 0.5f;
			g = (g - 0.5f) * _contrast + 0.5f;
			b = (b - 0.5f) * _contrast + 0.5f;

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
		_lblBrightness.Text = $"{_brightness:F2}x";
		_lblContrast.Text = $"{_contrast:F2}x";
		_lblSaturation.Text = $"{_saturation:F2}x";
		_lblOpacity.Text = $"{_opacity:F2}";
		_lblAlbedoMix.Text = $"{_albedoMix:F2}";
		_lblNormalStrength.Text = $"{_normalStrength:F2}x";
		_lblRoughness.Text = $"{_roughness:F2}";
		_lblMetallic.Text = $"{_metallic:F2}";

		if (_lblOpacitySpeed != null) _lblOpacitySpeed.Text = $"{_opacityPulseSpeed:F1}x";
		if (_lblMinOpacity != null) _lblMinOpacity.Text = $"{_minOpacity:F2}";
		if (_lblMaxOpacity != null) _lblMaxOpacity.Text = $"{_maxOpacity:F2}";
		if (_lblEmissionSpeed != null) _lblEmissionSpeed.Text = $"{_emissionPulseSpeed:F1}x";
		if (_lblMinEmission != null) _lblMinEmission.Text = $"{_minEmission:F1}";
		if (_lblMaxEmission != null) _lblMaxEmission.Text = $"{_maxEmission:F1}";
		if (_lblScaleSpeed != null) _lblScaleSpeed.Text = $"{_scalePulseSpeed:F1}x";
		if (_lblMinScale != null) _lblMinScale.Text = $"{_minScaleRatio:F2}x";
		if (_lblMaxScale != null) _lblMaxScale.Text = $"{_maxScaleRatio:F2}x";
		if (_lblUpperFade != null) _lblUpperFade.Text = $"{_upperFade:F2}";
		if (_lblLowerFade != null) _lblLowerFade.Text = $"{_lowerFade:F2}";

		if (_previewRect != null)
		{
			float r = _tint.R * _brightness;
			float g = _tint.G * _brightness;
			float b = _tint.B * _brightness;

			float lum = 0.2126f * r + 0.7152f * g + 0.0722f * b;
			r = lum + (r - lum) * _saturation;
			g = lum + (g - lum) * _saturation;
			b = lum + (b - lum) * _saturation;

			r = (r - 0.5f) * _contrast + 0.5f;
			g = (g - 0.5f) * _contrast + 0.5f;
			b = (b - 0.5f) * _contrast + 0.5f;

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
			_lowerFade
		);
	}

	protected override void OnApply()
	{
		string finalDecalId = !string.IsNullOrWhiteSpace(_txtDecalId?.Text) ? _txtDecalId.Text.Trim() : _decalKey;
		if (string.IsNullOrWhiteSpace(finalDecalId)) finalDecalId = _decalKey;
		_decalKey = finalDecalId;

		string finalTexturePath = !string.IsNullOrWhiteSpace(_txtTexturePath?.Text) ? _txtTexturePath.Text.Trim() : _texturePath;
		_texturePath = finalTexturePath;

		GameHost.Instance?.InvalidateDecalCache(_decalKey);

		var result = new JsonObject
		{
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
			["asset_type"] = "Decal",
			["AssetType"] = "Decal"
		};

		_onApplied?.Invoke(result);
		Hud?.ShowFeedback(string.Format(TranslationServer.Translate("Saved decal properties for '{0}'"), _decalKey));
		CloseDialog();
	}

	protected override void OnCancel()
	{
		if (_originalDecalStates.Count > 0 && GameHost.Instance?.AllDecals != null)
		{
			foreach (var d in GameHost.Instance.AllDecals)
			{
				if (d != null && GodotObject.IsInstanceValid(d) && _originalDecalStates.TryGetValue(d.GetInstanceId(), out var orig))
				{
					d.Modulate = orig.Modulate;
					d.AlbedoMix = orig.AlbedoMix;
					d.TextureNormal = orig.TextureNormal;
					d.TextureOrm = orig.TextureOrm;
					d.TextureEmission = orig.TextureEmission;
					d.EmissionEnergy = orig.EmissionEnergy;
					d.Size = orig.Size;
					d.UpperFade = orig.UpperFade;
					d.LowerFade = orig.LowerFade;
					if (d is Decal3D d3d)
					{
						d3d.SetBaseProperties(orig.Modulate, orig.EmissionEnergy, orig.Size);
						d3d.UpdateProcessState();
					}
				}
			}
		}
		else if (_initialSnapshot != null)
		{
			_decalKey = _initialSnapshot.DecalId;
			if (_txtDecalId != null) _txtDecalId.Text = _decalKey;
			if (_lblDecalName != null) _lblDecalName.Text = TranslationServer.Translate("Decal ID:") + " " + _decalKey;
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
			_animateOpacity = _initialSnapshot.AnimateOpacity;
			_opacityPulseSpeed = _initialSnapshot.OpacityPulseSpeed;
			_minOpacity = _initialSnapshot.MinOpacity;
			_maxOpacity = _initialSnapshot.MaxOpacity;
			_animateEmission = _initialSnapshot.AnimateEmission;
			_emissionPulseSpeed = _initialSnapshot.EmissionPulseSpeed;
			_minEmission = _initialSnapshot.MinEmission;
			_maxEmission = _initialSnapshot.MaxEmission;
			_animateScale = _initialSnapshot.AnimateScale;
			_scalePulseSpeed = _initialSnapshot.ScalePulseSpeed;
			_minScaleRatio = _initialSnapshot.MinScaleRatio;
			_maxScaleRatio = _initialSnapshot.MaxScaleRatio;
			_upperFade = _initialSnapshot.UpperFade;
			_lowerFade = _initialSnapshot.LowerFade;

			ReloadBaseTexture();
			UpdateLivePreviewAndWorld();
		}
		base.OnCancel();
	}
}
