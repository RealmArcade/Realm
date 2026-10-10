using Godot;
using Realm.Client.Services;
using Realm.Client.VFX;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json.Nodes;

namespace Realm.Client.UI.MapEditor;

public partial class VfxStudioDialog : Realm.Client.UI.MapEditor.FloatingPreview3DDialogBase
{
	private ProceduralVfxInstance3D _previewVfxInstance;
	private MeshInstance3D _previewGroundGrid;

	private string _vfxId = "";
	private string _slug = "";
	private Label _lblObjectTypePrefix;
	private LineEdit _txtSlug;
	private LineEdit _txtVfxName;
	private OptionButton _optMode;
	private OptionButton _optPrimitive;
	private Control _rowPrimitive;
	private OptionButton _optBlendMode;
	private OptionButton _optPlacementMode;
	private Control _rowBaseTexture;
	private Control _rowParticleTexture;
	private Control _rowParticleMesh;
	private VBoxContainer _uberShaderContainer;
	private VBoxContainer _particleContainer;
	private static readonly VfxPrimitiveType[] _primitiveTypes = Enum.GetValues<VfxPrimitiveType>().Where(p => p != VfxPrimitiveType.ParticleSystem).ToArray();

	private OptionButton _optParticleShape;
	private OptionButton _optParticleRenderMode;
	private Control _rowParticleRenderMode;
	private LineEdit _txtParticleTexture;
	private LineEdit _txtParticleMesh;
	private Action<string> _setParticleTextureVal;
	private Action<string> _setParticleMeshVal;
	private Action<string> _setBaseTextureVal;
	private Action<string> _setNoiseTextureVal;

	private CheckBox _chkLuminanceToAlpha;
	private HSlider _sliderLuminanceThreshold;
	private Label _lblLuminanceThreshold;
	private HSlider _sliderLuminanceSmoothness;
	private Label _lblLuminanceSmoothness;
	private CheckBox _chkUseGrayscale;
	private CheckBox _chkInvertMask;
	private HSlider _sliderHighPassCutoff;
	private Label _lblHighPassCutoff;
	private LineEdit _txtBaseUvScrollX;
	private LineEdit _txtBaseUvScrollY;
	private LineEdit _txtBaseUvScaleX;
	private LineEdit _txtBaseUvScaleY;
	private HSlider _sliderDistortionStrength;
	private Label _lblDistortionStrength;
	private LineEdit _txtNoiseUvScrollX;
	private LineEdit _txtNoiseUvScrollY;
	private LineEdit _txtNoiseUvScaleX;
	private LineEdit _txtNoiseUvScaleY;
	private ColorPickerButton _pickerBaseColor;
	private HSlider _sliderBaseColorHue;
	private ColorPickerButton _pickerSecondaryColor;
	private HSlider _sliderSecondaryColorHue;
	private ColorPickerButton _pickerCoreColor;
	private HSlider _sliderCoreColorHue;
	private HSlider _sliderColorMixRatio;
	private Label _lblColorMixRatio;
	private HSlider _sliderEmissionBoost;
	private Label _lblEmissionBoost;
	private HSlider _sliderCoreThreshold;
	private Label _lblCoreThreshold;
	private CheckBox _chkRadialFalloff;
	private HSlider _sliderRadialFalloffStart;
	private Label _lblRadialFalloffStart;
	private HSlider _sliderRadialFalloffEnd;
	private Label _lblRadialFalloffEnd;
	private CheckBox _chkLengthFade;
	private HSlider _sliderLengthFadeStart;
	private Label _lblLengthFadeStart;
	private HSlider _sliderLengthFadeEnd;
	private Label _lblLengthFadeEnd;
	private HSlider _sliderErosionProgress;
	private Label _lblErosionProgress;
	private CheckBox _chkFresnel;
	private HSlider _sliderFresnelPower;
	private Label _lblFresnelPower;
	private HSlider _sliderFresnelIntensity;
	private Label _lblFresnelIntensity;
	private CheckBox _chkDepthFade;
	private HSlider _sliderDepthFadeDistance;
	private Label _lblDepthFadeDistance;

	private HSlider _sliderParticleAmount;
	private Label _lblParticleAmount;
	private HSlider _sliderParticleLifetime;
	private Label _lblParticleLifetime;
	private HSlider _sliderParticleExplosiveness;
	private Label _lblParticleExplosiveness;
	private CheckBox _chkParticleLocalCoords;
	private LineEdit _txtParticleDirX;
	private LineEdit _txtParticleDirY;
	private LineEdit _txtParticleDirZ;
	private HSlider _sliderParticleSpread;
	private Label _lblParticleSpread;
	private HSlider _sliderParticleVelMin;
	private Label _lblParticleVelMin;
	private HSlider _sliderParticleVelMax;
	private Label _lblParticleVelMax;
	private LineEdit _txtParticleGravX;
	private LineEdit _txtParticleGravY;
	private LineEdit _txtParticleGravZ;
	private HSlider _sliderParticleDamping;
	private Label _lblParticleDamping;
	private HSlider _sliderParticleRadialAccel;
	private Label _lblParticleRadialAccel;
	private HSlider _sliderParticleTangentialAccel;
	private Label _lblParticleTangentialAccel;
	private HSlider _sliderParticleScaleMin;
	private Label _lblParticleScaleMin;
	private HSlider _sliderParticleScaleMax;
	private Label _lblParticleScaleMax;
	private HSlider _sliderParticleEndScaleRatio;
	private Label _lblParticleEndScaleRatio;
	private ColorPickerButton _pickerParticleColorStart;
	private HSlider _sliderParticleColorStartHue;
	private ColorPickerButton _pickerParticleColorMid;
	private HSlider _sliderParticleColorMidHue;
	private ColorPickerButton _pickerParticleColorEnd;
	private HSlider _sliderParticleColorEndHue;
	private HSlider _sliderParticleEmissionEnergy;
	private Label _lblParticleEmissionEnergy;

	private HSlider _sliderSurfaceNormalOffset;
	private Label _lblSurfaceNormalOffset;
	private LineEdit _txtPosOffsetX;
	private LineEdit _txtPosOffsetY;
	private LineEdit _txtPosOffsetZ;
	private LineEdit _txtRotOffsetX;
	private LineEdit _txtRotOffsetY;
	private LineEdit _txtRotOffsetZ;
	private LineEdit _txtScaleOffsetX;
	private LineEdit _txtScaleOffsetY;
	private LineEdit _txtScaleOffsetZ;

	private string? _tempGeneratedNoiseFileName;
	private JsonObject? _tempGeneratedNoiseConfig;

	private SpritesheetAssetEditDialog _spritesheetEditDialog;

	private VfxAttachmentConfig _currentConfig = new();
	private VfxAttachmentConfig _initialConfig = new();
	private Action<VfxAttachmentConfig> _onAppliedCallback;
	private bool _isUpdatingUI;

	public VfxStudioDialog(MapEditorHUD hud)
		: base(hud, TranslationServer.Translate("Procedural VFX Studio (Uber-Shader & Attachments)"), new Vector2(560, 780))
	{
		_spritesheetEditDialog = new SpritesheetAssetEditDialog(hud);

		DefaultDistance = 4.0f;
		CameraDistance = 4.0f;
		DefaultYaw = Mathf.DegToRad(30.0f);
		DefaultPitch = Mathf.DegToRad(20.0f);
		CameraYaw = Mathf.DegToRad(30.0f);
		CameraPitch = Mathf.DegToRad(20.0f);
		DefaultTargetPosition = new Vector3(0.0f, 0.5f, 0.0f);
		TargetPosition = DefaultTargetPosition;

		BuildControls();
	}

	private void BuildTopToolbar(Control parent)
	{
		var topToolbar = new HBoxContainer();
		topToolbar.AddThemeConstantOverride("separation", 4);

		AddButton(topToolbar, $"{UnicodeIcons.PLAY} " + TranslationServer.Translate("VFX"), () => RestartPreviewVfx(), "Restart procedural effect", 10, new Vector2(0, 22));
		AddButton(topToolbar, $"{UnicodeIcons.WATER} " + TranslationServer.Translate("Wave"), () => TriggerPreviewShockwave(), "Test directional ground planar shockwave", 10, new Vector2(0, 22));
		AddButton(topToolbar, $"{UnicodeIcons.CIRCLE} " + TranslationServer.Translate("Ring"), () => TriggerPreviewRing(), "Test radial expanding ground ring", 10, new Vector2(0, 22));
		AddButton(topToolbar, $"{UnicodeIcons.SUN} " + TranslationServer.Translate("Burst"), () => TriggerPreviewBurst(), "Test expanding 3D burst sphere", 10, new Vector2(0, 22));
		AddButton(topToolbar, $"{UnicodeIcons.EXPAND_ARROWS} " + TranslationServer.Translate("Chain"), () => TriggerPreviewChain(), "Test multi-segment ribbon chain beam", 10, new Vector2(0, 22));
		AddButton(topToolbar, $"{UnicodeIcons.CLOUD_SHOWERS} " + TranslationServer.Translate("Barrage"), () => TriggerPreviewBarrage(), "Test randomized area barrage volley", 10, new Vector2(0, 22));

		var separator = new Control { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
		topToolbar.AddChild(separator);

		AddCameraPresetToolbar(topToolbar, includeBack: false, includeGridToggle: true, onToggleGrid: () =>
		{
			if (_previewGroundGrid != null && GodotObject.IsInstanceValid(_previewGroundGrid))
			{
				_previewGroundGrid.Visible = !_previewGroundGrid.Visible;
			}
		});
		
		parent.AddChild(topToolbar);
	}

	private void BuildSubToolbar(Control parent)
	{
		var rowRandomize = new HBoxContainer();
		var btnRandomize = AddButton(rowRandomize, $"{UnicodeIcons.DICE} " + TranslationServer.Translate("Randomize All"), () =>
		{
			RandomizeAllParameters();
		}, "Randomize all noise masks, shapes, uv scrolling, and color parameters completely");
		btnRandomize.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
		
		AddButton(rowRandomize, $"{UnicodeIcons.COPY} " + TranslationServer.Translate("Copy Code"), () => CopyScriptCode(), "Copy C# MapScript code reference to clipboard", 10, new Vector2(0, 22));
		parent.AddChild(rowRandomize);
	}

	private void BuildBasicSettings(VBoxContainer parent)
	{
		var rowId = new HBoxContainer();
		rowId.AddThemeConstantOverride("separation", 6);
		var lblId = new Label();
		lblId.Text = TranslationServer.Translate("TemplateID:");
		lblId.CustomMinimumSize = new Vector2(140, 0);
		lblId.AddThemeFontSizeOverride("font_size", 11);
		rowId.AddChild(lblId);

		_lblObjectTypePrefix = new Label();
		_lblObjectTypePrefix.Text = "vfx/";
		_lblObjectTypePrefix.AddThemeFontSizeOverride("font_size", 11);
		_lblObjectTypePrefix.AddThemeColorOverride("font_color", UIStyle.ColorGold);
		rowId.AddChild(_lblObjectTypePrefix);

		_txtSlug = new LineEdit();
		_txtSlug.PlaceholderText = TranslationServer.Translate("vfx_slug");
		_txtSlug.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
		_txtSlug.AddThemeFontSizeOverride("font_size", 11);
		_txtSlug.TextChanged += (val) =>
		{
			if (_isUpdatingUI) return;
			_slug = TemplateIDHelper.ToSnakeCase(val);
			_vfxId = TemplateIDHelper.NormalizeTemplateID("vfx", _slug);
			_currentConfig.VfxId = _vfxId;
			if (_currentConfig.ParticleConfig != null)
			{
				_currentConfig.ParticleConfig.ParticleId = _vfxId;
			}
		};
		rowId.AddChild(_txtSlug);
		parent.AddChild(rowId);

		_txtVfxName = AddTextInput(parent, TranslationServer.Translate("Display Name:"), _currentConfig.Name, (val) =>
		{
			if (_isUpdatingUI) return;
			_currentConfig.Name = val;
		}, "Human-readable name", 140f);

		string[] modeNames = new[] { "Primitive", "Particle" };
		int initialModeIdx = _currentConfig.PrimitiveType == VfxPrimitiveType.ParticleSystem ? 1 : 0;
		_optMode = AddOptionDropdown(parent, TranslationServer.Translate("Mode"), modeNames, initialModeIdx, (idx) =>
		{
			if (_isUpdatingUI) return;
			if (idx == 1)
			{
				_currentConfig.PrimitiveType = VfxPrimitiveType.ParticleSystem;
				EnsureParticleConfig();
			}
			else
			{
				if (_currentConfig.PrimitiveType == VfxPrimitiveType.ParticleSystem)
				{
					int primIdx = _optPrimitive != null ? Math.Clamp(_optPrimitive.Selected, 0, _primitiveTypes.Length - 1) : 0;
					_currentConfig.PrimitiveType = _primitiveTypes[primIdx];
				}
			}
			UpdateSectionVisibilities();
			RestartPreviewVfx();
		}, 140f);
	}

	private void BuildSettings(VBoxContainer parent)
	{
		AddSectionHeader(parent, "⚙️ " + TranslationServer.Translate("Settings"));

		string[] blendModes = Enum.GetNames<VfxBlendMode>();
		_optBlendMode = AddOptionDropdown(parent, TranslationServer.Translate("Blend Mode"), blendModes, (int)_currentConfig.BlendMode, (idx) =>
		{
			if (_isUpdatingUI) return;
			_currentConfig.BlendMode = (VfxBlendMode)idx;
			if (_currentConfig.ParticleConfig != null)
			{
				_currentConfig.ParticleConfig.BlendMode = (VfxBlendMode)idx;
			}
			RestartPreviewVfx();
		}, 140f);

		string[] placementModes = Enum.GetNames<VfxPlacementMode>();
		_optPlacementMode = AddOptionDropdown(parent, TranslationServer.Translate("Placement Mode"), placementModes, (int)_currentConfig.PlacementMode, (idx) =>
		{
			if (_isUpdatingUI) return;
			_currentConfig.PlacementMode = (VfxPlacementMode)idx;
		}, 140f);

		string[] renderModes = Enum.GetNames<SpellParticleRenderMode>();
		_optParticleRenderMode = AddOptionDropdown(parent, TranslationServer.Translate("Render Mode"), renderModes, _currentConfig.ParticleConfig != null ? (int)_currentConfig.ParticleConfig.RenderMode : 0, (idx) =>
		{
			if (_isUpdatingUI) return;
			EnsureParticleConfig();
			_currentConfig.ParticleConfig.RenderMode = (SpellParticleRenderMode)idx;
			UpdateSectionVisibilities();
			RestartPreviewVfx();
		}, 140f);
		_rowParticleRenderMode = _optParticleRenderMode.GetParent() as Control;
	}

	private void BuildTextureSettings(VBoxContainer parent)
	{
		var baseTexTuple = AddAssetFilterDropdown(
			parent,
			TranslationServer.Translate("Base Texture (.rtex)"),
			_currentConfig.BaseTexture,
			(all) => ScanTextureAssets(true),
			(val) =>
			{
				if (_isUpdatingUI) return;
				_currentConfig.BaseTexture = val;
				RestartPreviewVfx();
			},
			TranslationServer.Translate("Select ribbon/decal/texture asset..."),
			140f,
			true
		);
		_setBaseTextureVal = baseTexTuple.SetValue;
		_rowBaseTexture = baseTexTuple.Input.GetParent() as Control;

		var particleTexTuple = AddAssetFilterDropdown(
			parent,
			TranslationServer.Translate("Particle Texture"),
			"",
			(all) => ScanTextureAssets(true),
			(val) =>
			{
				if (_isUpdatingUI) return;
				EnsureParticleConfig();
				_currentConfig.ParticleConfig.ParticleTexture = val;
				RestartPreviewVfx();
			},
			TranslationServer.Translate("Select billboard texture..."),
			140f,
			true
		);
		_txtParticleTexture = particleTexTuple.Input;
		_setParticleTextureVal = particleTexTuple.SetValue;
		_rowParticleTexture = particleTexTuple.Input.GetParent() as Control;

		if (_rowParticleTexture is HBoxContainer pTexRow)
		{
			var btnEditSheet = AddButton(
				pTexRow,
				"🎞️",
				() =>
				{
					string tex = _currentConfig.ParticleConfig?.ParticleTexture;
					if (string.IsNullOrEmpty(tex)) return;
					var meta = VfxShaderManager.GetSpritesheetMetadataSafe(tex) ?? (Columns: 4, Rows: 4, Fps: 20.0f, SubframeBlend: true);
					_spritesheetEditDialog?.OpenForSheet(
						tex,
						meta.Columns,
						meta.Rows,
						meta.Fps,
						meta.SubframeBlend,
						(cols, rows, fps, subframeBlend) =>
						{
							SaveSpritesheetGrid(tex, cols, rows, fps, subframeBlend);
							VfxShaderManager.ClearCache();
							RestartPreviewVfx();
						}
					);
				},
				"Configure Spritesheet Animation (Grid & FPS)",
				11,
				new Vector2(24, 22)
			);
		}

		var particleMeshTuple = AddAssetFilterDropdown(
			parent,
			TranslationServer.Translate("Projectile Mesh"),
			"",
			(all) => ScanProjectileMeshAssets(),
			(val) =>
			{
				if (_isUpdatingUI) return;
				EnsureParticleConfig();
				_currentConfig.ParticleConfig.MeshAssetPath = val;
				RestartPreviewVfx();
			},
			TranslationServer.Translate("Select .rmesh asset..."),
			140f,
			true
		);
		_txtParticleMesh = particleMeshTuple.Input;
		_setParticleMeshVal = particleMeshTuple.SetValue;
		_rowParticleMesh = particleMeshTuple.Input.GetParent() as Control;
	}

	private void BuildShaderSettings(VBoxContainer parent)
	{
		_uberShaderContainer = new VBoxContainer();
		parent.AddChild(_uberShaderContainer);

		AddSectionHeader(_uberShaderContainer, "🔳 " + TranslationServer.Translate("Mask & Distortion"));

		SetupLuminanceControls();
		SetupMaskControls();
		SetupUvControls();
	}

	private void SetupLuminanceControls()
	{
		_chkLuminanceToAlpha = AddCheckBox(_uberShaderContainer, TranslationServer.Translate("Luminance To Alpha"), _currentConfig.LuminanceToAlpha, OnLuminanceToAlphaChanged, "Converts grayscale intensity into transparency");
		(_sliderLuminanceThreshold, _lblLuminanceThreshold) = AddSlider(_uberShaderContainer, TranslationServer.Translate("Luma Threshold"), 0.0f, 1.0f, 0.05f, 0.1f, OnLuminanceThresholdChanged, "0.00", 140f);
		(_sliderLuminanceSmoothness, _lblLuminanceSmoothness) = AddSlider(_uberShaderContainer, TranslationServer.Translate("Luma Smoothness"), 0.0f, 1.0f, 0.05f, 0.1f, OnLuminanceSmoothnessChanged, "0.00", 140f);
	}

	private void SetupMaskControls()
	{
		_chkUseGrayscale = AddCheckBox(_uberShaderContainer, TranslationServer.Translate("Use Grayscale Base"), _currentConfig.UseGrayscale, OnUseGrayscaleChanged, "Discard color and use texture purely as a mask");
		_chkInvertMask = AddCheckBox(_uberShaderContainer, TranslationServer.Translate("Invert Base Alpha"), _currentConfig.InvertMask, OnInvertMaskChanged, "Inverts the calculated alpha mask");
		(_sliderHighPassCutoff, _lblHighPassCutoff) = AddSlider(_uberShaderContainer, TranslationServer.Translate("Alpha Cutoff"), 0.0f, 1.0f, 0.05f, 0.0f, OnHighPassCutoffChanged, "0.00", 140f);
	}

	private void SetupUvControls()
	{
		(_txtBaseUvScrollX, _txtBaseUvScrollY) = AddVector2Input(_uberShaderContainer, TranslationServer.Translate("Base UV Scroll"), Vector2.Zero, (System.Action<Vector2>)OnBaseUvScrollChanged, 140f);
		(_txtBaseUvScaleX, _txtBaseUvScaleY) = AddVector2Input(_uberShaderContainer, TranslationServer.Translate("Base UV Scale"), Vector2.One, (System.Action<Vector2>)OnBaseUvScaleChanged, 140f);

		var noiseTexTuple = AddAssetFilterDropdown(
			_uberShaderContainer,
			TranslationServer.Translate("Noise Texture (.rtex)"),
			_currentConfig.NoiseTexture,
			(all) => ScanNoiseAssets(),
			OnNoiseTextureChanged,
			TranslationServer.Translate("Select noise asset..."),
			140f,
			true
		);
		_setNoiseTextureVal = noiseTexTuple.SetValue;

		if (noiseTexTuple.Input.GetParent() is HBoxContainer noiseRow)
		{
			AddButton(noiseRow, "+", OnCreateNoiseTexture, "Create new procedural noise texture", 11, new Vector2(24, 22));
		}

		(_sliderDistortionStrength, _lblDistortionStrength) = AddSlider(_uberShaderContainer, TranslationServer.Translate("Distortion Strength"), 0.0f, 2.0f, 0.02f, _currentConfig.DistortionStrength, OnDistortionStrengthChanged, "0.00", 140f);
		(_txtNoiseUvScrollX, _txtNoiseUvScrollY) = AddVector2Input(_uberShaderContainer, TranslationServer.Translate("Noise UV Scroll"), Vector2.Zero, (System.Action<Vector2>)OnNoiseUvScrollChanged, 140f);
		(_txtNoiseUvScaleX, _txtNoiseUvScaleY) = AddVector2Input(_uberShaderContainer, TranslationServer.Translate("Noise UV Scale"), Vector2.One, (System.Action<Vector2>)OnNoiseUvScaleChanged, 140f);
	}

	private void OnLuminanceToAlphaChanged(bool val) { if (!_isUpdatingUI) { _currentConfig.LuminanceToAlpha = val; RestartPreviewVfx(); } }
	private void OnLuminanceThresholdChanged(float val) { if (!_isUpdatingUI) { _currentConfig.LuminanceThreshold = val; RestartPreviewVfx(); } }
	private void OnLuminanceSmoothnessChanged(float val) { if (!_isUpdatingUI) { _currentConfig.LuminanceSmoothness = val; RestartPreviewVfx(); } }
	private void OnUseGrayscaleChanged(bool val) { if (!_isUpdatingUI) { _currentConfig.UseGrayscale = val; RestartPreviewVfx(); } }
	private void OnInvertMaskChanged(bool val) { if (!_isUpdatingUI) { _currentConfig.InvertMask = val; RestartPreviewVfx(); } }
	private void OnHighPassCutoffChanged(float val) { if (!_isUpdatingUI) { _currentConfig.HighPassCutoff = val; RestartPreviewVfx(); } }
	private void OnBaseUvScrollChanged(Vector2 val) { if (!_isUpdatingUI) { _currentConfig.BaseUvScroll = val.ToVector2Data(); RestartPreviewVfx(); } }
	private void OnBaseUvScaleChanged(Vector2 val) { if (!_isUpdatingUI) { _currentConfig.BaseUvScale = val.ToVector2Data(); RestartPreviewVfx(); } }
	private void OnNoiseTextureChanged(string val) { if (!_isUpdatingUI) { _currentConfig.NoiseTexture = val; RestartPreviewVfx(); } }
	private void OnCreateNoiseTexture() { Hud?.OpenNoiseTextureDialog((createdName) => { if (!string.IsNullOrEmpty(createdName)) { _currentConfig.NoiseTexture = createdName; _setNoiseTextureVal?.Invoke(createdName); RestartPreviewVfx(); } }); }
	private void OnDistortionStrengthChanged(float val) { if (!_isUpdatingUI) { _currentConfig.DistortionStrength = val; RestartPreviewVfx(); } }
	private void OnNoiseUvScrollChanged(Vector2 val) { if (!_isUpdatingUI) { _currentConfig.NoiseUvScroll = val.ToVector2Data(); RestartPreviewVfx(); } }
	private void OnNoiseUvScaleChanged(Vector2 val) { if (!_isUpdatingUI) { _currentConfig.NoiseUvScale = val.ToVector2Data(); RestartPreviewVfx(); } }


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

	private void OnRadialFalloffChanged(bool val) { if (!_isUpdatingUI) { _currentConfig.EnableRadialFalloff = val; RestartPreviewVfx(); } }
	private void OnRadialFalloffStartChanged(float val) { if (!_isUpdatingUI) { _currentConfig.RadialFalloffStart = val; RestartPreviewVfx(); } }
	private void OnRadialFalloffEndChanged(float val) { if (!_isUpdatingUI) { _currentConfig.RadialFalloffEnd = val; RestartPreviewVfx(); } }
	private void OnLengthFadeChanged(bool val) { if (!_isUpdatingUI) { _currentConfig.EnableLengthFade = val; RestartPreviewVfx(); } }
	private void OnLengthFadeStartChanged(float val) { if (!_isUpdatingUI) { _currentConfig.LengthFadeStart = val; RestartPreviewVfx(); } }
	private void OnLengthFadeEndChanged(float val) { if (!_isUpdatingUI) { _currentConfig.LengthFadeEnd = val; RestartPreviewVfx(); } }
	private void OnErosionProgressChanged(float val) { if (!_isUpdatingUI) { _currentConfig.ErosionProgress = val; RestartPreviewVfx(); } }
	private void OnFresnelChanged(bool val) { if (!_isUpdatingUI) { _currentConfig.EnableFresnel = val; RestartPreviewVfx(); } }
	private void OnFresnelPowerChanged(float val) { if (!_isUpdatingUI) { _currentConfig.FresnelPower = val; RestartPreviewVfx(); } }
	private void OnFresnelIntensityChanged(float val) { if (!_isUpdatingUI) { _currentConfig.FresnelIntensity = val; RestartPreviewVfx(); } }
	private void OnDepthFadeChanged(bool val) { if (!_isUpdatingUI) { _currentConfig.EnableDepthFade = val; RestartPreviewVfx(); } }
	private void OnDepthFadeDistanceChanged(float val) { if (!_isUpdatingUI) { _currentConfig.DepthFadeDistance = val; RestartPreviewVfx(); } }


	private void BuildTransformSettings(VBoxContainer parent)
	{
		AddSectionHeader(parent, "📐 " + TranslationServer.Translate("TRANSFORM & SURFACE OFFSET"), new Color(0.75f, 0.65f, 0.95f));

		string[] primitiveNames = _primitiveTypes.Select(p => p.ToString()).ToArray();
		int primitiveInitialIdx = Math.Max(0, Array.IndexOf(_primitiveTypes, _currentConfig.PrimitiveType));
		_optPrimitive = AddOptionDropdown(parent, TranslationServer.Translate("Primitive Shape"), primitiveNames, primitiveInitialIdx, (idx) =>
		{
			if (_isUpdatingUI) return;
			if (idx >= 0 && idx < _primitiveTypes.Length)
			{
				_currentConfig.PrimitiveType = _primitiveTypes[idx];
				RestartPreviewVfx();
			}
		}, 140f);
		_rowPrimitive = _optPrimitive.GetParent() as Control;

		(_sliderSurfaceNormalOffset, _lblSurfaceNormalOffset) = AddSlider(parent, TranslationServer.Translate("Surface Normal Offset"), -0.2f, 0.5f, 0.005f, _currentConfig.SurfaceNormalOffset, (val) =>
		{
			if (_isUpdatingUI) return;
			_currentConfig.SurfaceNormalOffset = val;
			RestartPreviewVfx();
		}, "0.000", 140f);

		(_txtPosOffsetX, _txtPosOffsetY, _txtPosOffsetZ) = AddVector3Input(parent, TranslationServer.Translate("Position Offset"), _currentConfig.PositionOffset, (val) =>
		{
			if (_isUpdatingUI) return;
			_currentConfig.PositionOffset = val;
			RestartPreviewVfx();
		}, 140f);

		(_txtRotOffsetX, _txtRotOffsetY, _txtRotOffsetZ) = AddVector3Input(parent, TranslationServer.Translate("Rotation (Pitch, Yaw, Roll)"), _currentConfig.RotationOffset, (val) =>
		{
			if (_isUpdatingUI) return;
			_currentConfig.RotationOffset = val;
			RestartPreviewVfx();
		}, 140f);

		(_txtScaleOffsetX, _txtScaleOffsetY, _txtScaleOffsetZ) = AddVector3Input(parent, TranslationServer.Translate("Non-Uniform Scale"), _currentConfig.ScaleOffset, (val) =>
		{
			if (_isUpdatingUI) return;
			_currentConfig.ScaleOffset = val;
			RestartPreviewVfx();
		}, 140f);

		CancelButton.Text = TranslationServer.Translate("CANCEL");
		ApplyButton.Text = TranslationServer.Translate("SAVE & APPLY");
	}

	private void BuildControls()
	{
		Add3DPreviewViewport(BodyContainer, new Vector2(530, 220));
		CreatePreviewEnvironment();

		BuildTopToolbar(BodyContainer);
		BuildSubToolbar(BodyContainer);

		var scrollBody = CreateScrollBody(440);

		BuildBasicSettings(scrollBody);
		BuildSettings(scrollBody);
		BuildTextureSettings(scrollBody);

		BuildShaderSettings(scrollBody);
		BuildColorSettings(_uberShaderContainer);
		BuildFalloffSettings(_uberShaderContainer);

		_particleContainer = new VBoxContainer();
		scrollBody.AddChild(_particleContainer);
		BuildParticleControls(_particleContainer);

		BuildTransformSettings(scrollBody);
	}

	private void CreatePreviewEnvironment()
	{
		_previewGroundGrid = new MeshInstance3D();
		_previewGroundGrid.Name = "GroundGrid";
		var planeMesh = new PlaneMesh { Size = new Vector2(10f, 10f), SubdivideWidth = 10, SubdivideDepth = 10 };
		_previewGroundGrid.Mesh = planeMesh;
		var gridMat = new StandardMaterial3D
		{
			AlbedoColor = new Color(0.12f, 0.14f, 0.18f, 0.85f),
			Roughness = 0.8f,
			Transparency = BaseMaterial3D.TransparencyEnum.Alpha
		};
		_previewGroundGrid.MaterialOverride = gridMat;
		_previewGroundGrid.Position = new Vector3(0, -0.01f, 0);
		PreviewSceneRoot.AddChild(_previewGroundGrid);

		_previewVfxInstance = new ProceduralVfxInstance3D();
		_previewVfxInstance.IsPreview = true;
		_previewVfxInstance.Name = "PreviewVfx";
		PreviewSceneRoot.AddChild(_previewVfxInstance);
		_previewVfxInstance.Initialize(_currentConfig);
	}

	public void OpenForConfig(VfxAttachmentConfig config, Action<VfxAttachmentConfig> onApplied = null)
	{
		_initialConfig = config?.Clone() ?? new VfxAttachmentConfig();
		_currentConfig = config?.Clone() ?? new VfxAttachmentConfig();
		_onAppliedCallback = onApplied;

		if (_currentConfig.PrimitiveType == VfxPrimitiveType.ParticleSystem || _currentConfig.ParticleConfig != null)
		{
			EnsureParticleConfig();
		}

		TitleLabel.Text = $"{TranslationServer.Translate("Procedural VFX Studio")} - {(!string.IsNullOrEmpty(_currentConfig.Name) ? _currentConfig.Name : _currentConfig.VfxId)}";

		UpdateUIFromCurrentConfig();
		OpenDialog();
		ResetCameraDefault();
		RestartPreviewVfx();
	}

	private void SetVisibleSafe(Control c, bool visible)
	{
		if (c != null) c.Visible = visible;
	}

	private void UpdateSectionVisibilities()
	{
		bool isParticle = _currentConfig.PrimitiveType == VfxPrimitiveType.ParticleSystem;
		SetVisibleSafe(_uberShaderContainer, !isParticle);
		SetVisibleSafe(_particleContainer, isParticle);

		SetVisibleSafe(_rowPrimitive, !isParticle);
		SetVisibleSafe(_rowBaseTexture, !isParticle);

		bool isMeshMode = _currentConfig.ParticleConfig?.RenderMode == SpellParticleRenderMode.Mesh;
		SetVisibleSafe(_rowParticleRenderMode, isParticle);
		SetVisibleSafe(_rowParticleTexture, isParticle && !isMeshMode);
		SetVisibleSafe(_rowParticleMesh, isParticle && isMeshMode);
	}

	private void BuildParticleEmissionControls(VBoxContainer parent)
	{
		AddSectionHeader(parent, "✨ " + TranslationServer.Translate("PARTICLE EMISSION & DYNAMICS"), new Color(0.95f, 0.75f, 0.35f));

		string[] shapeNames = Enum.GetNames<SpellParticleShape>();
		_optParticleShape = AddOptionDropdown(parent, TranslationServer.Translate("Emitter Shape"), shapeNames, 0, (idx) =>
		{
			if (_isUpdatingUI) return;
			EnsureParticleConfig();
			_currentConfig.ParticleConfig.EmitterShape = (SpellParticleShape)idx;
			RestartPreviewVfx();
		}, 140f);

		(_sliderParticleAmount, _lblParticleAmount) = AddSlider(parent, TranslationServer.Translate("Particle Count"), 1f, 512f, 1f, 32f, (val) =>
		{
			if (_isUpdatingUI) return;
			EnsureParticleConfig();
			_currentConfig.ParticleConfig.Amount = (int)val;
			RestartPreviewVfx();
		}, "0", 140f);

		(_sliderParticleLifetime, _lblParticleLifetime) = AddSlider(parent, TranslationServer.Translate("Lifetime (sec)"), 0.1f, 10.0f, 0.1f, 1.0f, (val) =>
		{
			if (_isUpdatingUI) return;
			EnsureParticleConfig();
			_currentConfig.ParticleConfig.Lifetime = val;
			RestartPreviewVfx();
		}, "0.0", 140f);

		(_sliderParticleExplosiveness, _lblParticleExplosiveness) = AddSlider(parent, TranslationServer.Translate("Explosiveness"), 0.0f, 1.0f, 0.05f, 0.0f, (val) =>
		{
			if (_isUpdatingUI) return;
			EnsureParticleConfig();
			_currentConfig.ParticleConfig.Explosiveness = val;
			RestartPreviewVfx();
		}, "0.00", 140f);

		_chkParticleLocalCoords = AddCheckBox(parent, TranslationServer.Translate("Local Coordinates"), false, (val) =>
		{
			if (_isUpdatingUI) return;
			EnsureParticleConfig();
			_currentConfig.ParticleConfig.LocalCoords = val;
			RestartPreviewVfx();
		}, "Particles move with parent rather than drifting in world space");
	}

	private void BuildParticleVelocityControls(VBoxContainer parent)
	{
		AddSectionHeader(parent, "💨 " + TranslationServer.Translate("VELOCITY, SPREAD & FORCES"), new Color(0.45f, 0.85f, 0.95f));

		(_txtParticleDirX, _txtParticleDirY, _txtParticleDirZ) = AddVector3Input(parent, TranslationServer.Translate("Emitter Direction"), Vector3.Up, (val) =>
		{
			if (_isUpdatingUI) return;
			EnsureParticleConfig();
			_currentConfig.ParticleConfig.Direction = val.ToVector3Data();
			RestartPreviewVfx();
		}, 140f);

		(_sliderParticleSpread, _lblParticleSpread) = AddSlider(parent, TranslationServer.Translate("Spread (Degrees)"), 0.0f, 180.0f, 1.0f, 45.0f, (val) =>
		{
			if (_isUpdatingUI) return;
			EnsureParticleConfig();
			_currentConfig.ParticleConfig.SpreadDegrees = val;
			RestartPreviewVfx();
		}, "0", 140f);

		(_sliderParticleVelMin, _lblParticleVelMin) = AddSlider(parent, TranslationServer.Translate("Velocity Min"), 0.0f, 50.0f, 0.2f, 1.0f, (val) =>
		{
			if (_isUpdatingUI) return;
			EnsureParticleConfig();
			_currentConfig.ParticleConfig.InitialVelocityMin = val;
			RestartPreviewVfx();
		}, "0.0", 140f);

		(_sliderParticleVelMax, _lblParticleVelMax) = AddSlider(parent, TranslationServer.Translate("Velocity Max"), 0.0f, 50.0f, 0.2f, 3.0f, (val) =>
		{
			if (_isUpdatingUI) return;
			EnsureParticleConfig();
			_currentConfig.ParticleConfig.InitialVelocityMax = val;
			RestartPreviewVfx();
		}, "0.0", 140f);

		(_txtParticleGravX, _txtParticleGravY, _txtParticleGravZ) = AddVector3Input(parent, TranslationServer.Translate("Gravity"), new Vector3(0, -4f, 0), (val) =>
		{
			if (_isUpdatingUI) return;
			EnsureParticleConfig();
			_currentConfig.ParticleConfig.Gravity = val.ToVector3Data();
			RestartPreviewVfx();
		}, 140f);

		(_sliderParticleDamping, _lblParticleDamping) = AddSlider(parent, TranslationServer.Translate("Linear Damping"), 0.0f, 20.0f, 0.1f, 0.5f, (val) =>
		{
			if (_isUpdatingUI) return;
			EnsureParticleConfig();
			_currentConfig.ParticleConfig.Damping = val;
			RestartPreviewVfx();
		}, "0.0", 140f);

		(_sliderParticleRadialAccel, _lblParticleRadialAccel) = AddSlider(parent, TranslationServer.Translate("Radial Acceleration"), -50.0f, 50.0f, 0.5f, 0.0f, (val) =>
		{
			if (_isUpdatingUI) return;
			EnsureParticleConfig();
			_currentConfig.ParticleConfig.RadialAccel = val;
			RestartPreviewVfx();
		}, "0.0", 140f);

		(_sliderParticleTangentialAccel, _lblParticleTangentialAccel) = AddSlider(parent, TranslationServer.Translate("Tangential Accel"), -50.0f, 50.0f, 0.5f, 0.0f, (val) =>
		{
			if (_isUpdatingUI) return;
			EnsureParticleConfig();
			_currentConfig.ParticleConfig.TangentialAccel = val;
			RestartPreviewVfx();
		}, "0.0", 140f);
	}

	private void BuildParticleAppearanceControls(VBoxContainer parent)
	{
		AddSectionHeader(parent, "🎨 " + TranslationServer.Translate("PARTICLE APPEARANCE & RAMP"), new Color(0.95f, 0.55f, 0.45f));

		(_sliderParticleScaleMin, _lblParticleScaleMin) = AddSlider(parent, TranslationServer.Translate("Initial Scale Min"), 0.01f, 5.0f, 0.05f, 0.2f, (val) =>
		{
			if (_isUpdatingUI) return;
			EnsureParticleConfig();
			_currentConfig.ParticleConfig.InitialScaleMin = val;
			RestartPreviewVfx();
		}, "0.00", 140f);

		(_sliderParticleScaleMax, _lblParticleScaleMax) = AddSlider(parent, TranslationServer.Translate("Initial Scale Max"), 0.01f, 5.0f, 0.05f, 0.4f, (val) =>
		{
			if (_isUpdatingUI) return;
			EnsureParticleConfig();
			_currentConfig.ParticleConfig.InitialScaleMax = val;
			RestartPreviewVfx();
		}, "0.00", 140f);

		(_sliderParticleEndScaleRatio, _lblParticleEndScaleRatio) = AddSlider(parent, TranslationServer.Translate("End Scale Ratio"), 0.0f, 3.0f, 0.05f, 0.0f, (val) =>
		{
			if (_isUpdatingUI) return;
			EnsureParticleConfig();
			_currentConfig.ParticleConfig.EndScaleRatio = val;
			RestartPreviewVfx();
		}, "0.00", 140f);

		(_pickerParticleColorStart, _sliderParticleColorStartHue) = AddColorPicker(parent, TranslationServer.Translate("Color Start"), Colors.Gold, (c) =>
		{
			if (_isUpdatingUI) return;
			EnsureParticleConfig();
			_currentConfig.ParticleConfig.ColorStart = "#" + c.ToHtml(false);
			RestartPreviewVfx();
		}, 140f);

		(_pickerParticleColorMid, _sliderParticleColorMidHue) = AddColorPicker(parent, TranslationServer.Translate("Color Mid"), Colors.DarkOrange, (c) =>
		{
			if (_isUpdatingUI) return;
			EnsureParticleConfig();
			_currentConfig.ParticleConfig.ColorMid = "#" + c.ToHtml(false);
			RestartPreviewVfx();
		}, 140f);

		(_pickerParticleColorEnd, _sliderParticleColorEndHue) = AddColorPicker(parent, TranslationServer.Translate("Color End"), Colors.Maroon, (c) =>
		{
			if (_isUpdatingUI) return;
			EnsureParticleConfig();
			_currentConfig.ParticleConfig.ColorEnd = "#" + c.ToHtml(false);
			RestartPreviewVfx();
		}, 140f);

		(_sliderParticleEmissionEnergy, _lblParticleEmissionEnergy) = AddSlider(parent, TranslationServer.Translate("Emission Energy"), 0.0f, 20.0f, 0.2f, 3.0f, (val) =>
		{
			if (_isUpdatingUI) return;
			EnsureParticleConfig();
			_currentConfig.ParticleConfig.EmissionEnergy = val;
			RestartPreviewVfx();
		}, "0.0", 140f);
	}

	private void BuildParticleControls(VBoxContainer parent)
	{
		BuildParticleEmissionControls(parent);
		BuildParticleVelocityControls(parent);
		BuildParticleAppearanceControls(parent);
	}

	private void EnsureParticleConfig()
	{
		if (_currentConfig.ParticleConfig == null)
		{
			_currentConfig.ParticleConfig = new SpellParticleConfig
			{
				ParticleId = _currentConfig.VfxId,
				Name = _currentConfig.Name
			};
		}
	}

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
		if (_optPlacementMode != null) _optPlacementMode.Selected = (int)_currentConfig.PlacementMode;
	}


	private void UpdateUIMaskSettings()
	{
		UpdateUILuminanceSettings();
		UpdateUIUvSettings();
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
		var pc = _currentConfig.ParticleConfig;
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
		var pc = _currentConfig.ParticleConfig;
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
		var pc = _currentConfig.ParticleConfig;
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

			UpdateSectionVisibilities();
		}
		finally
		{
			_isUpdatingUI = false;
		}
	}

	private void SyncBasicSettings()
	{
		SyncVfxId();
		if (_txtVfxName != null) _currentConfig.Name = _txtVfxName.Text.Trim();
		SyncPrimitiveMode();
    
		if (_optBlendMode != null) _currentConfig.BlendMode = (VfxBlendMode)_optBlendMode.Selected;
		if (_optPlacementMode != null) _currentConfig.PlacementMode = (VfxPlacementMode)_optPlacementMode.Selected;
	}

	private void SyncVfxId()
	{
		if (_txtSlug == null) return;
    
		_slug = TemplateIDHelper.ToSnakeCase(_txtSlug.Text);
		_vfxId = TemplateIDHelper.NormalizeTemplateID("vfx", _slug);
		_currentConfig.VfxId = _vfxId;
    
		if (_currentConfig.ParticleConfig != null)
		{
			_currentConfig.ParticleConfig.ParticleId = _vfxId;
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
		var pc = _currentConfig.ParticleConfig;
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
		var pc = _currentConfig.ParticleConfig;
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


	private List<string> ScanTextureAssets(bool includeAll)
	{
		var results = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
		string wsPath = ProjectSettings.GlobalizePath(MapEditorHUD.TempWorkspaceGodotPath);

		ScanTextureFolders(wsPath, results);
		ScanTextureAssetsObj(wsPath, results);

		return results.OrderBy(s => s, StringComparer.OrdinalIgnoreCase).ToList();
	}

	private void ScanTextureFolders(string wsPath, HashSet<string> results)
	{
		string[] subFolders = { "ribbons", "decals", "textures", "vfx", "noise" };
		foreach (var subFolder in subFolders)
		{
			string dir = Path.Combine(wsPath, "Assets", subFolder);
			if (!Directory.Exists(dir)) continue;

			foreach (var file in Directory.GetFiles(dir, "*.*"))
			{
				if (file.EndsWith(".rtex", StringComparison.OrdinalIgnoreCase) ||
				    file.EndsWith(".png", StringComparison.OrdinalIgnoreCase) ||
				    file.EndsWith(".webp", StringComparison.OrdinalIgnoreCase))
				{
					results.Add(Path.GetFileName(file));
				}
			}
		}
	}

	private void ScanTextureAssetsObj(string wsPath, HashSet<string> results)
	{
		try
		{
			var assetsObj = MapAssetHelper.LoadAssets(wsPath);
			if (assetsObj == null) return;

			string[] categories = { "Ribbon", "Decal", "Terrain", "Spritesheet", "Noise" };
			foreach (var catName in categories)
			{
				var catDict = assetsObj.GetCategory(catName);
				if (catDict == null) continue;

				foreach (var kvp in catDict)
				{
					if (!string.IsNullOrEmpty(kvp.Key))
					{
						results.Add(Path.GetFileName(kvp.Key));
					}
				}
			}
		}
		catch { }
	}


	private List<string> ScanNoiseAssets()
	{
		var results = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
		string wsPath = ProjectSettings.GlobalizePath(MapEditorHUD.TempWorkspaceGodotPath);

		ScanNoiseFolder(wsPath, results);
		ScanNoiseAssetsObj(wsPath, results);

		return results.OrderBy(s => s, StringComparer.OrdinalIgnoreCase).ToList();
	}

	private void ScanNoiseFolder(string wsPath, HashSet<string> results)
	{
		string dir = Path.Combine(wsPath, "Assets", "noise");
		if (!Directory.Exists(dir)) return;

		foreach (var file in Directory.GetFiles(dir, "*.*"))
		{
			if (file.EndsWith(".rtex", StringComparison.OrdinalIgnoreCase) ||
			    file.EndsWith(".png", StringComparison.OrdinalIgnoreCase) ||
			    file.EndsWith(".webp", StringComparison.OrdinalIgnoreCase))
			{
				results.Add(Path.GetFileName(file));
			}
		}
	}

	private void ScanNoiseAssetsObj(string wsPath, HashSet<string> results)
	{
		try
		{
			var assetsObj = MapAssetHelper.LoadAssets(wsPath);
			var noiseDict = assetsObj?.GetCategory("Noise");
        
			if (noiseDict == null) return;

			foreach (var kvp in noiseDict)
			{
				if (!string.IsNullOrEmpty(kvp.Key))
				{
					results.Add(Path.GetFileName(kvp.Key));
				}
			}
		}
		catch { }
	}


	private List<string> ScanProjectileMeshAssets()
	{
		var results = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
		string wsPath = ProjectSettings.GlobalizePath(MapEditorHUD.TempWorkspaceGodotPath);

		ScanProjectileAssetsObj(wsPath, results);

		void ScanFolder(string folderPath)
		{
			if (!Directory.Exists(folderPath)) return;
        
			foreach (var file in Directory.GetFiles(folderPath, "*.rmesh"))
			{
				results.Add(Path.GetFileName(file));
			}
		}

		ScanFolder(Path.Combine(wsPath, "Assets", "models", "projectiles"));
		ScanFolder(Path.Combine(wsPath, "Assets", "models", "props"));
    
		string templateDir = PathUtils.FindPath("MapTemplate/Assets/models/projectiles");
		if (!string.IsNullOrEmpty(templateDir)) ScanFolder(templateDir);

		return results.OrderBy(s => s, StringComparer.OrdinalIgnoreCase).ToList();
	}

	private void ScanProjectileAssetsObj(string wsPath, HashSet<string> results)
	{
		try
		{
			var assetsObj = MapAssetHelper.LoadAssets(wsPath);
			if (assetsObj == null) return;

			string[] categories = { "Character", "Building", "Prop", "Item" };
			foreach (var catName in categories)
			{
				var catDict = assetsObj.GetCategory(catName);
				if (catDict == null) continue;

				foreach (var modelProp in catDict)
				{
					string fn = modelProp.Key;
					if (!string.IsNullOrEmpty(fn) && fn.EndsWith(".rmesh", StringComparison.OrdinalIgnoreCase))
					{
						results.Add(Path.GetFileName(fn));
					}
				}
			}
		}
		catch { }
	}


	private void RandomizeAllParameters()
	{
		bool isParticle = _currentConfig.PrimitiveType == VfxPrimitiveType.ParticleSystem;

		if (isParticle)
		{
			EnsureParticleConfig();

			var shapes = Enum.GetValues<SpellParticleShape>();
			_currentConfig.ParticleConfig.EmitterShape = shapes.GetValue(Random.Shared.Next(0, shapes.Length)) is SpellParticleShape s ? s : SpellParticleShape.Sphere;

			_currentConfig.ParticleConfig.Amount = Random.Shared.Next(12, 180);
			_currentConfig.ParticleConfig.Lifetime = (float)Math.Round(Random.Shared.NextDouble() * (3.0 - 0.4) + 0.4, 2);
			_currentConfig.ParticleConfig.Explosiveness = (float)Math.Round(Random.Shared.NextDouble() * 0.9, 2);
			_currentConfig.ParticleConfig.LocalCoords = Random.Shared.NextDouble() > 0.6;

			float dirX = (float)Math.Round((Random.Shared.NextDouble() * 2.0 - 1.0), 2);
			float dirY = (float)Math.Round(Random.Shared.NextDouble() * 1.5, 2);
			float dirZ = (float)Math.Round((Random.Shared.NextDouble() * 2.0 - 1.0), 2);
			_currentConfig.ParticleConfig.Direction = new Vector3(dirX, dirY, dirZ).Normalized().ToVector3Data();

			_currentConfig.ParticleConfig.SpreadDegrees = (float)Math.Round(Random.Shared.NextDouble() * 80.0 + 10.0, 1);

			float vMin = (float)Math.Round(Random.Shared.NextDouble() * 4.0 + 0.5, 2);
			float vMax = (float)Math.Round(vMin + Random.Shared.NextDouble() * 8.0 + 0.5, 2);
			_currentConfig.ParticleConfig.InitialVelocityMin = vMin;
			_currentConfig.ParticleConfig.InitialVelocityMax = vMax;

			float gravY = (float)Math.Round(Random.Shared.NextDouble() * 12.0 - 6.0, 2);
			_currentConfig.ParticleConfig.Gravity = new Vector3(0.0f, gravY, 0.0f).ToVector3Data();

			_currentConfig.ParticleConfig.Damping = (float)Math.Round(Random.Shared.NextDouble() * 4.0, 2);
			_currentConfig.ParticleConfig.RadialAccel = (float)Math.Round((Random.Shared.NextDouble() * 2.0 - 1.0) * 15.0, 2);
			_currentConfig.ParticleConfig.TangentialAccel = (float)Math.Round((Random.Shared.NextDouble() * 2.0 - 1.0) * 15.0, 2);

			float sMin = (float)Math.Round(Random.Shared.NextDouble() * 0.4 + 0.05, 2);
			float sMax = (float)Math.Round(sMin + Random.Shared.NextDouble() * 0.6 + 0.1, 2);
			_currentConfig.ParticleConfig.InitialScaleMin = sMin;
			_currentConfig.ParticleConfig.InitialScaleMax = sMax;
			_currentConfig.ParticleConfig.EndScaleRatio = (float)Math.Round(Random.Shared.NextDouble() * 2.0, 2);

			float baseHue = (float)Random.Shared.NextDouble();
			Color colStart = Color.FromHsv(baseHue, (float)(Random.Shared.NextDouble() * 0.4 + 0.6), 1.0f);
			Color colMid = Color.FromHsv((baseHue + 0.08f) % 1.0f, (float)(Random.Shared.NextDouble() * 0.4 + 0.6), (float)(Random.Shared.NextDouble() * 0.4 + 0.6));
			Color colEnd = Color.FromHsv((baseHue + 0.16f) % 1.0f, 1.0f, (float)(Random.Shared.NextDouble() * 0.3 + 0.1));

			_currentConfig.ParticleConfig.ColorStart = "#" + colStart.ToHtml(false);
			_currentConfig.ParticleConfig.ColorMid = "#" + colMid.ToHtml(false);
			_currentConfig.ParticleConfig.ColorEnd = "#" + colEnd.ToHtml(false);
			_currentConfig.ParticleConfig.EmissionEnergy = (float)Math.Round(Random.Shared.NextDouble() * 9.0 + 1.0, 1);
		}
		else
		{
			string chosenNoise = GenerateHeadlessRandomNoise();
			if (!string.IsNullOrEmpty(chosenNoise))
			{
				_currentConfig.NoiseTexture = chosenNoise;
			}

			_currentConfig.LuminanceToAlpha = Random.Shared.NextDouble() > 0.25;
			_currentConfig.LuminanceThreshold = (float)Math.Round(Random.Shared.NextDouble() * 0.35 + 0.01, 2);
			_currentConfig.LuminanceSmoothness = (float)Math.Round(Random.Shared.NextDouble() * 0.2 + 0.01, 2);
			_currentConfig.UseGrayscale = Random.Shared.NextDouble() > 0.4;
			_currentConfig.InvertMask = Random.Shared.NextDouble() > 0.75;
			_currentConfig.HighPassCutoff = (float)Math.Round(Random.Shared.NextDouble() * 0.3, 2);

			_currentConfig.BaseUvScroll = new Vector2(
				(float)Math.Round((Random.Shared.NextDouble() * 2.0 - 1.0) * 1.5, 2),
				(float)Math.Round((Random.Shared.NextDouble() * 2.0 - 1.0) * 1.5, 2)
			).ToVector2Data();
			_currentConfig.BaseUvScale = new Vector2(
				(float)Math.Round(Random.Shared.NextDouble() * 2.5 + 0.5, 2),
				(float)Math.Round(Random.Shared.NextDouble() * 2.5 + 0.5, 2)
			).ToVector2Data();

			_currentConfig.DistortionStrength = (float)Math.Round(Random.Shared.NextDouble() * 0.85 + 0.05, 2);
			_currentConfig.NoiseUvScroll = new Vector2(
				(float)Math.Round((Random.Shared.NextDouble() * 2.0 - 1.0) * 1.5, 2),
				(float)Math.Round((Random.Shared.NextDouble() * 2.0 - 1.0) * 1.5, 2)
			).ToVector2Data();
			_currentConfig.NoiseUvScale = new Vector2(
				(float)Math.Round(Random.Shared.NextDouble() * 2.5 + 0.5, 2),
				(float)Math.Round(Random.Shared.NextDouble() * 2.5 + 0.5, 2)
			).ToVector2Data();

			float baseHue = (float)Random.Shared.NextDouble();
			Color colBase = Color.FromHsv(baseHue, (float)(Random.Shared.NextDouble() * 0.3 + 0.7), 1.0f);
			Color colSec = Color.FromHsv((baseHue + 0.12f) % 1.0f, (float)(Random.Shared.NextDouble() * 0.3 + 0.7), (float)(Random.Shared.NextDouble() * 0.5 + 0.3));
			Color colCore = Color.FromHsv((baseHue + 0.95f) % 1.0f, (float)(Random.Shared.NextDouble() * 0.2), 1.0f);

			_currentConfig.BaseColor = "#" + colBase.ToHtml(false);
			_currentConfig.SecondaryColor = "#" + colSec.ToHtml(false);
			_currentConfig.CoreColor = "#" + colCore.ToHtml(false);
			_currentConfig.ColorMixRatio = (float)Math.Round(Random.Shared.NextDouble() * 0.8 + 0.2, 2);
			_currentConfig.EmissionBoost = (float)Math.Round(Random.Shared.NextDouble() * 7.0 + 1.5, 1);
			_currentConfig.CoreThreshold = (float)Math.Round(Random.Shared.NextDouble() * 0.6 + 0.3, 2);

			_currentConfig.EnableRadialFalloff = Random.Shared.NextDouble() > 0.3;
			float radStart = (float)Math.Round(Random.Shared.NextDouble() * 0.5 + 0.3, 2);
			_currentConfig.RadialFalloffStart = radStart;
			_currentConfig.RadialFalloffEnd = (float)Math.Round(radStart + Random.Shared.NextDouble() * 0.4 + 0.1, 2);

			_currentConfig.EnableLengthFade = Random.Shared.NextDouble() > 0.5;
			float lenStart = (float)Math.Round(Random.Shared.NextDouble() * 0.4 + 0.1, 2);
			_currentConfig.LengthFadeStart = lenStart;
			_currentConfig.LengthFadeEnd = (float)Math.Round(lenStart + Random.Shared.NextDouble() * 0.5 + 0.2, 2);
			_currentConfig.ErosionProgress = (float)Math.Round(Random.Shared.NextDouble() * 0.2, 2);

			_currentConfig.EnableFresnel = Random.Shared.NextDouble() > 0.6;
			_currentConfig.FresnelPower = (float)Math.Round(Random.Shared.NextDouble() * 4.0 + 1.0, 1);
			_currentConfig.FresnelIntensity = (float)Math.Round(Random.Shared.NextDouble() * 4.0 + 0.5, 1);

			_currentConfig.EnableDepthFade = Random.Shared.NextDouble() > 0.2;
			_currentConfig.DepthFadeDistance = (float)Math.Round(Random.Shared.NextDouble() * 1.2 + 0.1, 2);
		}

		UpdateUIFromCurrentConfig();
		RestartPreviewVfx();
	}

	private string GenerateHeadlessRandomNoise()
	{
		string wsPath = ProjectSettings.GlobalizePath(MapEditorHUD.TempWorkspaceGodotPath);
		string noiseDir = Path.Combine(wsPath, "Assets", "noise");
		Directory.CreateDirectory(noiseDir);

		if (!string.IsNullOrEmpty(_tempGeneratedNoiseFileName))
		{
			string oldPath = Path.Combine(noiseDir, _tempGeneratedNoiseFileName);
			if (File.Exists(oldPath))
			{
				try { File.Delete(oldPath); } catch { }
			}
			_tempGeneratedNoiseFileName = null;
			_tempGeneratedNoiseConfig = null;
		}

		int dedupeIndex = 1;
		string fileName;
		while (true)
		{
			fileName = $"random_noise_{dedupeIndex}.rtex";
			string fullPath = Path.Combine(noiseDir, fileName);
			if (!File.Exists(fullPath))
			{
				break;
			}
			dedupeIndex++;
		}

		string[] noiseTypes = new[] { "Perlin", "Simplex", "SimplexSmooth", "Cellular", "ValueCubic" };
		string[] fractalTypes = new[] { "Fbm", "Ridged", "PingPong" };

		var config = new JsonObject
		{
			["generator"] = "FastNoiseLite",
			["noise_type"] = noiseTypes[Random.Shared.Next(0, noiseTypes.Length)],
			["seed"] = Random.Shared.Next(1, 999999),
			["frequency"] = (float)Math.Round(Random.Shared.NextDouble() * (0.05 - 0.005) + 0.005, 4),
			["fractal_type"] = fractalTypes[Random.Shared.Next(0, fractalTypes.Length)],
			["fractal_octaves"] = Random.Shared.Next(2, 6),
			["fractal_lacunarity"] = (float)Math.Round(Random.Shared.NextDouble() * (3.0 - 1.5) + 1.5, 2),
			["fractal_gain"] = (float)Math.Round(Random.Shared.NextDouble() * (0.8 - 0.2) + 0.2, 2),
			["fractal_weighted_strength"] = (float)Math.Round(Random.Shared.NextDouble() * 0.6, 2),
			["invert"] = false,
			["normalize"] = true,
			["width"] = 512,
			["height"] = 512,
			["color_mode"] = "Grayscale"
		};

		string outputRtex = Path.Combine(noiseDir, fileName);
		try
		{
			string blake3Hash = NoiseTextureGenerator.GenerateAndSaveRtex(config, outputRtex);
			config["hash"] = blake3Hash;

			_tempGeneratedNoiseFileName = fileName;
			_tempGeneratedNoiseConfig = config;
			return fileName;
		}
		catch (Exception ex)
		{
			GD.PrintErr($"[VfxStudioDialog] Failed to generate random noise: {ex.Message}");
			return string.Empty;
		}
	}

	private void CleanupTransientNoiseFile()
	{
		if (!string.IsNullOrEmpty(_tempGeneratedNoiseFileName))
		{
			try
			{
				string wsPath = ProjectSettings.GlobalizePath(MapEditorHUD.TempWorkspaceGodotPath);
				string filePath = Path.Combine(wsPath, "Assets", "noise", _tempGeneratedNoiseFileName);
				if (File.Exists(filePath))
				{
					File.Delete(filePath);
				}
			}
			catch { }
			_tempGeneratedNoiseFileName = null;
			_tempGeneratedNoiseConfig = null;
		}
	}

	protected override void OnApply()
	{
		SyncConfigFromControls();

		if (!string.IsNullOrEmpty(_tempGeneratedNoiseFileName) &&
		    _tempGeneratedNoiseConfig != null &&
		    string.Equals(_currentConfig.NoiseTexture, _tempGeneratedNoiseFileName, StringComparison.OrdinalIgnoreCase))
		{
			try
			{
				string wsPath = ProjectSettings.GlobalizePath(MapEditorHUD.TempWorkspaceGodotPath);
				string outputRtex = Path.Combine(wsPath, "Assets", "noise", _tempGeneratedNoiseFileName);
				string blake3Hash = NoiseTextureGenerator.GenerateAndSaveRtex(_tempGeneratedNoiseConfig, outputRtex);

				MapAssetHelper.UpdateManifestAsset(wsPath, "Noise", _tempGeneratedNoiseFileName, blake3Hash);

				MetadataService.Instance.UpdateMetadata(wsPath, m =>
				{
					m.NoiseTextures ??= new(StringComparer.OrdinalIgnoreCase);
					m.NoiseTextures[_tempGeneratedNoiseFileName] = new Realm.Shared.Metadata.TextureMetadata
					{
						NoiseConfig = _tempGeneratedNoiseConfig.ToJsonString()
					};
				});
				Hud?.ReadMetadataAndRefreshTextures();
			}
			catch (Exception ex)
			{
				GD.PrintErr($"[VfxStudioDialog] Failed to save generated noise asset to manifest: {ex.Message}");
			}
			_tempGeneratedNoiseFileName = null;
			_tempGeneratedNoiseConfig = null;
		}
		else
		{
			CleanupTransientNoiseFile();
		}

		Hud?.SaveCustomVfxToMetadata(_currentConfig.VfxId, _currentConfig);
		_onAppliedCallback?.Invoke(_currentConfig);
		Hud?.ShowFeedback(string.Format(TranslationServer.Translate("Saved VFX '{0}' to metadata.json"), _currentConfig.Name));
	}

	protected override void OnCancel()
	{
		CleanupTransientNoiseFile();
		_currentConfig = _initialConfig.Clone();
	}

	private void SaveSpritesheetGrid(string key, int columns, int rows, float fps = 20.0f, bool subframeBlend = true)
	{
		string wsPath = ProjectSettings.GlobalizePath(MapEditorHUD.TempWorkspaceGodotPath);

		try
		{
			string fileName = Path.GetFileName(key);
			string slug = Realm.Client.Utils.TemplateIDHelper.GenerateSlug(fileName);
			string spritesheetTemplateId = Realm.Client.Utils.TemplateIDHelper.NormalizeTemplateID("spritesheet", slug);
			MetadataService.Instance.UpdateMetadata(wsPath, m =>
			{
				m.VfxSpritesheets ??= new(StringComparer.OrdinalIgnoreCase);
				m.VfxSpritesheets[spritesheetTemplateId] = new Realm.Shared.Metadata.VfxMetadata
				{
					TexturePath = fileName,
					Columns = columns,
					Rows = rows,
					Fps = fps,
					SubframeBlend = subframeBlend
				};
			});
		}
		catch (Exception ex)
		{
			GD.PrintErr($"[VfxStudioDialog] SaveSpritesheetGrid error: {ex.Message}");
		}
	}

	private void CopyScriptCode()
	{
		string code;
		if (_currentConfig.PrimitiveType == VfxPrimitiveType.VortexDisc)
		{
			code = $"api.SpawnExpandingGroundRing(targetPos, 8.0f, 12.0f, 1.0f);";
		}
		else if (_currentConfig.PrimitiveType == VfxPrimitiveType.AuraSphere)
		{
			code = $"api.SpawnExpandingBurstSphere(targetPos, 6.0f, 15.0f, 0.8f);";
		}
		else if (_currentConfig.PrimitiveType == VfxPrimitiveType.AuraCapsule || _currentConfig.PrimitiveType == VfxPrimitiveType.HemisphereDome)
		{
			code = $"api.AttachPersistentAura(unit, \"{_vfxId}\", 1.0f);";
		}
		else
		{
			code = $"api.SpawnVisualEffect(\"{_vfxId}\", targetPos, 1.0f);";
		}
		DisplayServer.ClipboardSet(code);
		Hud?.ShowFeedback(TranslationServer.Translate("Copied script code to clipboard"));
	}

	private void TriggerPreviewShockwave()
	{
		if (PreviewSceneRoot == null || !GodotObject.IsInstanceValid(PreviewSceneRoot)) return;
		Color baseCol = VfxShaderManager.ParseColorSafe(_currentConfig.BaseColor, Colors.Orange);
		Color col = new Color(baseCol.R, baseCol.G, baseCol.B, 0.9f);
		var shock = GroundShockwave3D.Create(PreviewSceneRoot, new Vector3(0, 0, -2.5f), ShockwaveType.PlanarWave, maxRadius: 6.0f, speed: 10.0f, duration: 0.9f, col);
		shock.ConformToTerrain = false;
		shock.Direction = Vector3.Back;
	}

	private void TriggerPreviewRing()
	{
		if (PreviewSceneRoot == null || !GodotObject.IsInstanceValid(PreviewSceneRoot)) return;
		Color baseCol = VfxShaderManager.ParseColorSafe(_currentConfig.BaseColor, Colors.Orange);
		Color col = new Color(baseCol.R, baseCol.G, baseCol.B, 0.9f);
		var ring = GroundShockwave3D.Create(PreviewSceneRoot, Vector3.Zero, ShockwaveType.ExpandingGroundRing, maxRadius: 5.0f, speed: 8.0f, duration: 0.9f, col);
		ring.ConformToTerrain = false;
	}

	private void TriggerPreviewBurst()
	{
		if (PreviewSceneRoot == null || !GodotObject.IsInstanceValid(PreviewSceneRoot)) return;
		Color baseCol = VfxShaderManager.ParseColorSafe(_currentConfig.BaseColor, Colors.Orange);
		Color col = new Color(baseCol.R, baseCol.G, baseCol.B, 0.8f);
		var burst = GroundShockwave3D.Create(PreviewSceneRoot, new Vector3(0, 0.8f, 0), ShockwaveType.ExpandingBurstSphere, maxRadius: 4.0f, speed: 10.0f, duration: 0.7f, col);
		burst.ConformToTerrain = false;
	}

	private void TriggerPreviewChain()
	{
		if (PreviewSceneRoot == null || !GodotObject.IsInstanceValid(PreviewSceneRoot)) return;
		Color baseCol = VfxShaderManager.ParseColorSafe(_currentConfig.BaseColor, Colors.Orange);
		Color col = new Color(baseCol.R, baseCol.G, baseCol.B, 1.0f);
		var pts = new Vector3[]
		{
			new Vector3(-2.5f, 0.5f, -1.5f),
			new Vector3(-0.5f, 1.2f, 0.5f),
			new Vector3(1.5f, 0.6f, -0.5f),
			new Vector3(2.5f, 1.0f, 1.5f)
		};
		ChainBeam3D.Create(PreviewSceneRoot, pts, jumpDelay: 0.08f, forkCount: 2, fadeLifetime: 0.45f, width: 0.35f, col);
	}

	private void TriggerPreviewBarrage()
	{
		if (PreviewSceneRoot == null || !GodotObject.IsInstanceValid(PreviewSceneRoot)) return;
		var rng = new Random();
		int count = 8;
		float interval = 0.12f;
		for (int i = 0; i < count; i++)
		{
			float delay = i * interval;
			var timer = GetTree().CreateTimer(delay);
			timer.Timeout += () =>
			{
				if (!GodotObject.IsInstanceValid(PreviewSceneRoot)) return;
				float angle = (float)(rng.NextDouble() * Math.PI * 2.0);
				float dist = (float)(rng.NextDouble() * 3.5f);
				var impactPos = new Vector3(Mathf.Cos(angle) * dist, 0.05f, Mathf.Sin(angle) * dist);
				var subVfx = new ProceduralVfxInstance3D();
				subVfx.IsPreview = true;
				PreviewSceneRoot.AddChild(subVfx);
				subVfx.Position = impactPos;
				subVfx.Initialize(_currentConfig);
				var cleanupTimer = GetTree().CreateTimer(1.2f);
				cleanupTimer.Timeout += () =>
				{
					if (GodotObject.IsInstanceValid(subVfx)) subVfx.QueueFree();
				};
			};
		}
	}
}