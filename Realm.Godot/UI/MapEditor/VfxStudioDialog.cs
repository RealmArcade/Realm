using Godot;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json.Nodes;
using Realm.Godot.Services;
using Realm.Godot.Utils;
using Realm.Godot.VFX;

public partial class VfxStudioDialog : FloatingPreview3DDialogBase
{
	private ProceduralVfxInstance3D _previewVfxInstance;
	private MeshInstance3D _previewGroundGrid;

	private LineEdit _txtVfxId;
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

	private void BuildControls()
	{
		Add3DPreviewViewport(BodyContainer, new Vector2(530, 220));

		CreatePreviewEnvironment();

		AddCameraPresetToolbar(BodyContainer, includeBack: false, includeGridToggle: true, onToggleGrid: () =>
		{
			if (_previewGroundGrid != null && GodotObject.IsInstanceValid(_previewGroundGrid))
			{
				_previewGroundGrid.Visible = !_previewGroundGrid.Visible;
			}
		});

		var scrollBody = CreateScrollBody(440);

		_txtVfxId = AddTextInput(scrollBody, TranslationServer.Translate("VFX ID:"), _currentConfig.VfxId, (val) =>
		{
			if (_isUpdatingUI) return;
			_currentConfig.VfxId = val;
		}, "Unique identifier e.g. vfx_fire_blade", 140f);

		_txtVfxName = AddTextInput(scrollBody, TranslationServer.Translate("Display Name:"), _currentConfig.Name, (val) =>
		{
			if (_isUpdatingUI) return;
			_currentConfig.Name = val;
		}, "Human-readable name", 140f);

		string[] modeNames = new[] { "Primitive", "Particle" };
		int initialModeIdx = _currentConfig.PrimitiveType == VfxPrimitiveType.ParticleSystem ? 1 : 0;
		_optMode = AddOptionDropdown(scrollBody, TranslationServer.Translate("Mode"), modeNames, initialModeIdx, (idx) =>
		{
			if (_isUpdatingUI) return;
			if (idx == 1)
			{
				_currentConfig.PrimitiveType = VfxPrimitiveType.ParticleSystem;
			}
			else
			{
				if (_currentConfig.PrimitiveType == VfxPrimitiveType.ParticleSystem)
				{
					_currentConfig.PrimitiveType = _optPrimitive != null ? (VfxPrimitiveType)_optPrimitive.Selected : VfxPrimitiveType.VortexDisc;
					if (_currentConfig.PrimitiveType == VfxPrimitiveType.ParticleSystem)
					{
						_currentConfig.PrimitiveType = VfxPrimitiveType.VortexDisc;
					}
				}
			}
			UpdateSectionVisibilities();
			RestartPreviewVfx();
		}, 140f);

		AddSectionHeader(scrollBody, "⚙️ " + TranslationServer.Translate("Settings"));

		string[] blendModes = Enum.GetNames<VfxBlendMode>();
		_optBlendMode = AddOptionDropdown(scrollBody, TranslationServer.Translate("Blend Mode"), blendModes, (int)_currentConfig.BlendMode, (idx) =>
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
		_optPlacementMode = AddOptionDropdown(scrollBody, TranslationServer.Translate("Placement Mode"), placementModes, (int)_currentConfig.PlacementMode, (idx) =>
		{
			if (_isUpdatingUI) return;
			_currentConfig.PlacementMode = (VfxPlacementMode)idx;
		}, 140f);

		string[] renderModes = Enum.GetNames<SpellParticleRenderMode>();
		_optParticleRenderMode = AddOptionDropdown(scrollBody, TranslationServer.Translate("Render Mode"), renderModes, _currentConfig.ParticleConfig != null ? (int)_currentConfig.ParticleConfig.RenderMode : 0, (idx) =>
		{
			if (_isUpdatingUI) return;
			EnsureParticleConfig();
			_currentConfig.ParticleConfig.RenderMode = (SpellParticleRenderMode)idx;
			UpdateSectionVisibilities();
			RestartPreviewVfx();
		}, 140f);
		_rowParticleRenderMode = _optParticleRenderMode.GetParent() as Control;

		var baseTexTuple = AddAssetFilterDropdown(
			scrollBody,
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
			scrollBody,
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
			scrollBody,
			TranslationServer.Translate("Projectile Mesh"),
			"",
			(all) => ScanProjectileMeshAssets(),
			(val) =>
			{
				if (_isUpdatingUI) return;
				EnsureParticleConfig();
				_currentConfig.ParticleConfig.MeshAssetPath = val;
				if (!string.IsNullOrEmpty(val))
				{
					_currentConfig.ParticleConfig.RenderMode = SpellParticleRenderMode.Mesh;
					if (_optParticleRenderMode != null)
					{
						_optParticleRenderMode.Selected = (int)SpellParticleRenderMode.Mesh;
					}
					UpdateSectionVisibilities();
				}
				RestartPreviewVfx();
			},
			TranslationServer.Translate("Select projectile/prop mesh (.rmesh)..."),
			140f,
			true
		);
		_txtParticleMesh = particleMeshTuple.Input;
		_setParticleMeshVal = particleMeshTuple.SetValue;
		_rowParticleMesh = particleMeshTuple.Input.GetParent() as Control;

		string[] primitiveNames = Enum.GetNames<VfxPrimitiveType>().Where(p => p != nameof(VfxPrimitiveType.ParticleSystem)).ToArray();
		int primitiveInitialIdx = Math.Clamp((int)_currentConfig.PrimitiveType, 0, primitiveNames.Length - 1);
		_optPrimitive = AddOptionDropdown(scrollBody, TranslationServer.Translate("Primitive Shape"), primitiveNames, primitiveInitialIdx, (idx) =>
		{
			if (_isUpdatingUI) return;
			_currentConfig.PrimitiveType = (VfxPrimitiveType)idx;
			UpdateSectionVisibilities();
			RestartPreviewVfx();
		}, 140f);
		_rowPrimitive = _optPrimitive.GetParent() as Control;

		var rowRandomize = new HBoxContainer();
		rowRandomize.AddThemeConstantOverride("separation", 6);
		var btnRandomize = AddButton(
			rowRandomize,
			"🎲 " + TranslationServer.Translate("Randomize All"),
			() => RandomizeAllParameters(),
			"Pick random values for all visual configuration parameters below",
			11,
			new Vector2(0, 26)
		);
		btnRandomize.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
		scrollBody.AddChild(rowRandomize);

		_uberShaderContainer = new VBoxContainer();
		_uberShaderContainer.AddThemeConstantOverride("separation", 8);
		scrollBody.AddChild(_uberShaderContainer);

		_particleContainer = new VBoxContainer();
		_particleContainer.AddThemeConstantOverride("separation", 8);
		scrollBody.AddChild(_particleContainer);

		BuildParticleControls(_particleContainer);

		AddSectionHeader(_uberShaderContainer, "🖼️ " + TranslationServer.Translate("SLOT 1: BASE SHAPE MASK (SILHOUETTE)"), new Color(0.85f, 0.6f, 0.35f));

		_chkLuminanceToAlpha = AddCheckBox(_uberShaderContainer, TranslationServer.Translate("Luminance to Alpha"), _currentConfig.LuminanceToAlpha, (pressed) =>
		{
			if (_isUpdatingUI) return;
			_currentConfig.LuminanceToAlpha = pressed;
			RestartPreviewVfx();
		}, "Derive transparency from texture brightness to strip black/dark backgrounds automatically");

		(_sliderLuminanceThreshold, _lblLuminanceThreshold) = AddSlider(_uberShaderContainer, TranslationServer.Translate("Luminance Threshold"), 0.0f, 1.0f, 0.01f, _currentConfig.LuminanceThreshold, (val) =>
		{
			if (_isUpdatingUI) return;
			_currentConfig.LuminanceThreshold = val;
			RestartPreviewVfx();
		}, "0.00", 140f);

		(_sliderLuminanceSmoothness, _lblLuminanceSmoothness) = AddSlider(_uberShaderContainer, TranslationServer.Translate("Threshold Smoothness"), 0.001f, 0.5f, 0.01f, _currentConfig.LuminanceSmoothness, (val) =>
		{
			if (_isUpdatingUI) return;
			_currentConfig.LuminanceSmoothness = val;
			RestartPreviewVfx();
		}, "0.00", 140f);

		_chkUseGrayscale = AddCheckBox(_uberShaderContainer, TranslationServer.Translate("Convert to Grayscale"), _currentConfig.UseGrayscale, (pressed) =>
		{
			if (_isUpdatingUI) return;
			_currentConfig.UseGrayscale = pressed;
			RestartPreviewVfx();
		});

		_chkInvertMask = AddCheckBox(_uberShaderContainer, TranslationServer.Translate("Invert Mask Colors"), _currentConfig.InvertMask, (pressed) =>
		{
			if (_isUpdatingUI) return;
			_currentConfig.InvertMask = pressed;
			RestartPreviewVfx();
		});

		(_sliderHighPassCutoff, _lblHighPassCutoff) = AddSlider(_uberShaderContainer, TranslationServer.Translate("High-Pass Cutoff"), 0.0f, 1.0f, 0.02f, _currentConfig.HighPassCutoff, (val) =>
		{
			if (_isUpdatingUI) return;
			_currentConfig.HighPassCutoff = val;
			RestartPreviewVfx();
		}, "0.00", 140f);

		(_txtBaseUvScrollX, _txtBaseUvScrollY) = AddVector2Input(_uberShaderContainer, TranslationServer.Translate("Base UV Scroll (X, Y)"), _currentConfig.BaseUvScroll, (val) =>
		{
			if (_isUpdatingUI) return;
			_currentConfig.BaseUvScroll = val;
			RestartPreviewVfx();
		}, 140f);

		(_txtBaseUvScaleX, _txtBaseUvScaleY) = AddVector2Input(_uberShaderContainer, TranslationServer.Translate("Base UV Scale (X, Y)"), _currentConfig.BaseUvScale, (val) =>
		{
			if (_isUpdatingUI) return;
			_currentConfig.BaseUvScale = val;
			RestartPreviewVfx();
		}, 140f);

		AddSectionHeader(_uberShaderContainer, "🌪️ " + TranslationServer.Translate("SLOT 2: DISTORTION & NOISE MASK"), new Color(0.35f, 0.75f, 0.85f));

		var noiseTexTuple = AddAssetFilterDropdown(
			_uberShaderContainer,
			TranslationServer.Translate("Noise Texture (.rtex)"),
			_currentConfig.NoiseTexture,
			(all) => ScanNoiseAssets(),
			(val) =>
			{
				if (_isUpdatingUI) return;
				_currentConfig.NoiseTexture = val;
				RestartPreviewVfx();
			},
			TranslationServer.Translate("Select noise texture..."),
			140f
		);
		_setNoiseTextureVal = noiseTexTuple.SetValue;

		if (noiseTexTuple.Input.GetParent() is HBoxContainer noiseRow)
		{
			var btnAddNoise = AddButton(
				noiseRow,
				"+",
				() =>
				{
					Hud?.OpenNoiseTextureDialog((createdName) =>
					{
						if (!string.IsNullOrEmpty(createdName))
						{
							_currentConfig.NoiseTexture = createdName;
							_setNoiseTextureVal?.Invoke(createdName);
							RestartPreviewVfx();
						}
					});
				},
				"Create new procedural noise texture",
				11,
				new Vector2(24, 22)
			);
		}

		(_sliderDistortionStrength, _lblDistortionStrength) = AddSlider(_uberShaderContainer, TranslationServer.Translate("Distortion Strength"), 0.0f, 2.0f, 0.02f, _currentConfig.DistortionStrength, (val) =>
		{
			if (_isUpdatingUI) return;
			_currentConfig.DistortionStrength = val;
			RestartPreviewVfx();
		}, "0.00", 140f);

		(_txtNoiseUvScrollX, _txtNoiseUvScrollY) = AddVector2Input(_uberShaderContainer, TranslationServer.Translate("Noise UV Scroll (X, Y)"), _currentConfig.NoiseUvScroll, (val) =>
		{
			if (_isUpdatingUI) return;
			_currentConfig.NoiseUvScroll = val;
			RestartPreviewVfx();
		}, 140f);

		(_txtNoiseUvScaleX, _txtNoiseUvScaleY) = AddVector2Input(_uberShaderContainer, TranslationServer.Translate("Noise UV Scale (X, Y)"), _currentConfig.NoiseUvScale, (val) =>
		{
			if (_isUpdatingUI) return;
			_currentConfig.NoiseUvScale = val;
			RestartPreviewVfx();
		}, 140f);

		AddSectionHeader(_uberShaderContainer, "🔥 " + TranslationServer.Translate("COLOR & HEAT HIERARCHY"), new Color(0.95f, 0.45f, 0.25f));

		(_pickerBaseColor, _sliderBaseColorHue) = AddColorPicker(_uberShaderContainer, TranslationServer.Translate("Base Color"), VfxShaderManager.ParseColorSafe(_currentConfig.BaseColor, Colors.Orange), (c) =>
		{
			if (_isUpdatingUI) return;
			_currentConfig.BaseColor = "#" + c.ToHtml(false);
			RestartPreviewVfx();
		}, 140f);

		(_pickerSecondaryColor, _sliderSecondaryColorHue) = AddColorPicker(_uberShaderContainer, TranslationServer.Translate("Secondary / Rim Color"), VfxShaderManager.ParseColorSafe(_currentConfig.SecondaryColor, Colors.DarkRed), (c) =>
		{
			if (_isUpdatingUI) return;
			_currentConfig.SecondaryColor = "#" + c.ToHtml(false);
			RestartPreviewVfx();
		}, 140f);

		(_pickerCoreColor, _sliderCoreColorHue) = AddColorPicker(_uberShaderContainer, TranslationServer.Translate("Inner Core Color"), VfxShaderManager.ParseColorSafe(_currentConfig.CoreColor, Colors.White), (c) =>
		{
			if (_isUpdatingUI) return;
			_currentConfig.CoreColor = "#" + c.ToHtml(false);
			RestartPreviewVfx();
		}, 140f);

		(_sliderEmissionBoost, _lblEmissionBoost) = AddSlider(_uberShaderContainer, TranslationServer.Translate("Emission Boost"), 0.0f, 15.0f, 0.2f, _currentConfig.EmissionBoost, (val) =>
		{
			if (_isUpdatingUI) return;
			_currentConfig.EmissionBoost = val;
			RestartPreviewVfx();
		}, "0.0", 140f);

		(_sliderCoreThreshold, _lblCoreThreshold) = AddSlider(_uberShaderContainer, TranslationServer.Translate("Core Threshold"), 0.0f, 1.0f, 0.02f, _currentConfig.CoreThreshold, (val) =>
		{
			if (_isUpdatingUI) return;
			_currentConfig.CoreThreshold = val;
			RestartPreviewVfx();
		}, "0.00", 140f);

		AddSectionHeader(_uberShaderContainer, "🌊 " + TranslationServer.Translate("FADING & DISSOLVE SUITE"), new Color(0.5f, 0.85f, 0.65f));

		_chkRadialFalloff = AddCheckBox(_uberShaderContainer, TranslationServer.Translate("Radial Falloff (Edge Fade)"), _currentConfig.EnableRadialFalloff, (pressed) =>
		{
			if (_isUpdatingUI) return;
			_currentConfig.EnableRadialFalloff = pressed;
			RestartPreviewVfx();
		}, "Softens perimeter edges of discs and quads to prevent hard clipping");

		(_sliderRadialFalloffStart, _lblRadialFalloffStart) = AddSlider(_uberShaderContainer, TranslationServer.Translate("Radial Falloff Start"), 0.0f, 1.0f, 0.02f, _currentConfig.RadialFalloffStart, (val) =>
		{
			if (_isUpdatingUI) return;
			_currentConfig.RadialFalloffStart = val;
			RestartPreviewVfx();
		}, "0.00", 140f);

		(_sliderRadialFalloffEnd, _lblRadialFalloffEnd) = AddSlider(_uberShaderContainer, TranslationServer.Translate("Radial Falloff End"), 0.0f, 1.0f, 0.02f, _currentConfig.RadialFalloffEnd, (val) =>
		{
			if (_isUpdatingUI) return;
			_currentConfig.RadialFalloffEnd = val;
			RestartPreviewVfx();
		}, "0.00", 140f);

		_chkLengthFade = AddCheckBox(_uberShaderContainer, TranslationServer.Translate("Length Fade (Erosion)"), _currentConfig.EnableLengthFade, (pressed) =>
		{
			if (_isUpdatingUI) return;
			_currentConfig.EnableLengthFade = pressed;
			RestartPreviewVfx();
		}, "Gradient falloff along UV length to pinch off flame tongues and dissolve trails");

		(_sliderLengthFadeStart, _lblLengthFadeStart) = AddSlider(_uberShaderContainer, TranslationServer.Translate("Length Fade Start"), 0.0f, 1.0f, 0.02f, _currentConfig.LengthFadeStart, (val) =>
		{
			if (_isUpdatingUI) return;
			_currentConfig.LengthFadeStart = val;
			RestartPreviewVfx();
		}, "0.00", 140f);

		(_sliderLengthFadeEnd, _lblLengthFadeEnd) = AddSlider(_uberShaderContainer, TranslationServer.Translate("Length Fade End"), 0.0f, 1.0f, 0.02f, _currentConfig.LengthFadeEnd, (val) =>
		{
			if (_isUpdatingUI) return;
			_currentConfig.LengthFadeEnd = val;
			RestartPreviewVfx();
		}, "0.00", 140f);

		(_sliderErosionProgress, _lblErosionProgress) = AddSlider(_uberShaderContainer, TranslationServer.Translate("Erosion Progress"), 0.0f, 1.0f, 0.02f, _currentConfig.ErosionProgress, (val) =>
		{
			if (_isUpdatingUI) return;
			_currentConfig.ErosionProgress = val;
			RestartPreviewVfx();
		}, "0.00", 140f);

		_chkFresnel = AddCheckBox(_uberShaderContainer, TranslationServer.Translate("Fresnel / Rim Glow"), _currentConfig.EnableFresnel, (pressed) =>
		{
			if (_isUpdatingUI) return;
			_currentConfig.EnableFresnel = pressed;
			RestartPreviewVfx();
		}, "View-angle falloff for luminous shields, force fields, and domes");

		(_sliderFresnelPower, _lblFresnelPower) = AddSlider(_uberShaderContainer, TranslationServer.Translate("Fresnel Power"), 0.1f, 10.0f, 0.1f, _currentConfig.FresnelPower, (val) =>
		{
			if (_isUpdatingUI) return;
			_currentConfig.FresnelPower = val;
			RestartPreviewVfx();
		}, "0.0", 140f);

		(_sliderFresnelIntensity, _lblFresnelIntensity) = AddSlider(_uberShaderContainer, TranslationServer.Translate("Fresnel Intensity"), 0.0f, 10.0f, 0.2f, _currentConfig.FresnelIntensity, (val) =>
		{
			if (_isUpdatingUI) return;
			_currentConfig.FresnelIntensity = val;
			RestartPreviewVfx();
		}, "0.0", 140f);

		_chkDepthFade = AddCheckBox(_uberShaderContainer, TranslationServer.Translate("Depth Fade (Soft Intersect)"), _currentConfig.EnableDepthFade, (pressed) =>
		{
			if (_isUpdatingUI) return;
			_currentConfig.EnableDepthFade = pressed;
			RestartPreviewVfx();
		}, "Eliminates hard seams where VFX intersects terrain or geometry");

		(_sliderDepthFadeDistance, _lblDepthFadeDistance) = AddSlider(_uberShaderContainer, TranslationServer.Translate("Depth Fade Distance"), 0.0f, 5.0f, 0.05f, _currentConfig.DepthFadeDistance, (val) =>
		{
			if (_isUpdatingUI) return;
			_currentConfig.DepthFadeDistance = val;
			RestartPreviewVfx();
		}, "0.00", 140f);

		AddSectionHeader(scrollBody, "📐 " + TranslationServer.Translate("TRANSFORM & SURFACE OFFSET"), new Color(0.75f, 0.65f, 0.95f));

		(_sliderSurfaceNormalOffset, _lblSurfaceNormalOffset) = AddSlider(scrollBody, TranslationServer.Translate("Surface Normal Offset"), -0.2f, 0.5f, 0.005f, _currentConfig.SurfaceNormalOffset, (val) =>
		{
			if (_isUpdatingUI) return;
			_currentConfig.SurfaceNormalOffset = val;
			RestartPreviewVfx();
		}, "0.000", 140f);

		(_txtPosOffsetX, _txtPosOffsetY, _txtPosOffsetZ) = AddVector3Input(scrollBody, TranslationServer.Translate("Position Offset"), _currentConfig.PositionOffset, (val) =>
		{
			if (_isUpdatingUI) return;
			_currentConfig.PositionOffset = val;
			RestartPreviewVfx();
		}, 140f);

		(_txtRotOffsetX, _txtRotOffsetY, _txtRotOffsetZ) = AddVector3Input(scrollBody, TranslationServer.Translate("Rotation (Pitch, Yaw, Roll)"), _currentConfig.RotationOffset, (val) =>
		{
			if (_isUpdatingUI) return;
			_currentConfig.RotationOffset = val;
			RestartPreviewVfx();
		}, 140f);

		(_txtScaleOffsetX, _txtScaleOffsetY, _txtScaleOffsetZ) = AddVector3Input(scrollBody, TranslationServer.Translate("Non-Uniform Scale"), _currentConfig.ScaleOffset, (val) =>
		{
			if (_isUpdatingUI) return;
			_currentConfig.ScaleOffset = val;
			RestartPreviewVfx();
		}, 140f);

		CancelButton.Text = TranslationServer.Translate("CANCEL");
		ApplyButton.Text = TranslationServer.Translate("SAVE & APPLY");
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
		_previewVfxInstance.Name = "PreviewVfx";
		PreviewSceneRoot.AddChild(_previewVfxInstance);
		_previewVfxInstance.Initialize(_currentConfig);
	}

	public void OpenForConfig(VfxAttachmentConfig config, Action<VfxAttachmentConfig> onApplied = null)
	{
		_initialConfig = config?.Clone() ?? new VfxAttachmentConfig();
		_currentConfig = config?.Clone() ?? new VfxAttachmentConfig();
		_onAppliedCallback = onApplied;

		TitleLabel.Text = $"{TranslationServer.Translate("Procedural VFX Studio")} - {(!string.IsNullOrEmpty(_currentConfig.Name) ? _currentConfig.Name : _currentConfig.VfxId)}";

		UpdateUIFromCurrentConfig();
		OpenDialog();
		ResetCameraDefault();
		RestartPreviewVfx();
	}

	private void UpdateSectionVisibilities()
	{
		bool isParticle = _currentConfig.PrimitiveType == VfxPrimitiveType.ParticleSystem;
		if (_uberShaderContainer != null) _uberShaderContainer.Visible = !isParticle;
		if (_particleContainer != null) _particleContainer.Visible = isParticle;

		if (_rowPrimitive != null) _rowPrimitive.Visible = !isParticle;
		if (_rowBaseTexture != null) _rowBaseTexture.Visible = !isParticle;

		bool isMeshMode = _currentConfig.ParticleConfig?.RenderMode == SpellParticleRenderMode.Mesh;
		if (_rowParticleRenderMode != null) _rowParticleRenderMode.Visible = isParticle;
		if (_rowParticleTexture != null) _rowParticleTexture.Visible = isParticle && !isMeshMode;
		if (_rowParticleMesh != null) _rowParticleMesh.Visible = isParticle && isMeshMode;
	}

	private void BuildParticleControls(VBoxContainer parent)
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

	private void UpdateUIFromCurrentConfig()
	{
		_isUpdatingUI = true;
		try
		{
			if (_txtVfxId != null) _txtVfxId.Text = _currentConfig.VfxId;
			if (_txtVfxName != null) _txtVfxName.Text = _currentConfig.Name;
			bool isParticle = _currentConfig.PrimitiveType == VfxPrimitiveType.ParticleSystem;
			if (_optMode != null) _optMode.Selected = isParticle ? 1 : 0;
			if (!isParticle && _optPrimitive != null) _optPrimitive.Selected = Math.Clamp((int)_currentConfig.PrimitiveType, 0, _optPrimitive.ItemCount - 1);
			if (_optBlendMode != null) _optBlendMode.Selected = (int)_currentConfig.BlendMode;
			if (_optPlacementMode != null) _optPlacementMode.Selected = (int)_currentConfig.PlacementMode;

			if (_setBaseTextureVal != null) _setBaseTextureVal(_currentConfig.BaseTexture ?? string.Empty);
			if (_setNoiseTextureVal != null) _setNoiseTextureVal(_currentConfig.NoiseTexture ?? string.Empty);

			if (_chkLuminanceToAlpha != null) _chkLuminanceToAlpha.ButtonPressed = _currentConfig.LuminanceToAlpha;
			if (_sliderLuminanceThreshold != null) _sliderLuminanceThreshold.Value = _currentConfig.LuminanceThreshold;
			if (_lblLuminanceThreshold != null) _lblLuminanceThreshold.Text = _currentConfig.LuminanceThreshold.ToString("0.00");
			if (_sliderLuminanceSmoothness != null) _sliderLuminanceSmoothness.Value = _currentConfig.LuminanceSmoothness;
			if (_lblLuminanceSmoothness != null) _lblLuminanceSmoothness.Text = _currentConfig.LuminanceSmoothness.ToString("0.00");
			if (_chkUseGrayscale != null) _chkUseGrayscale.ButtonPressed = _currentConfig.UseGrayscale;
			if (_chkInvertMask != null) _chkInvertMask.ButtonPressed = _currentConfig.InvertMask;
			if (_sliderHighPassCutoff != null) _sliderHighPassCutoff.Value = _currentConfig.HighPassCutoff;
			if (_lblHighPassCutoff != null) _lblHighPassCutoff.Text = _currentConfig.HighPassCutoff.ToString("0.00");

			if (_txtBaseUvScrollX != null) _txtBaseUvScrollX.Text = _currentConfig.BaseUvScroll.X.ToString("0.##");
			if (_txtBaseUvScrollY != null) _txtBaseUvScrollY.Text = _currentConfig.BaseUvScroll.Y.ToString("0.##");
			if (_txtBaseUvScaleX != null) _txtBaseUvScaleX.Text = _currentConfig.BaseUvScale.X.ToString("0.##");
			if (_txtBaseUvScaleY != null) _txtBaseUvScaleY.Text = _currentConfig.BaseUvScale.Y.ToString("0.##");

			if (_sliderDistortionStrength != null) _sliderDistortionStrength.Value = _currentConfig.DistortionStrength;
			if (_lblDistortionStrength != null) _lblDistortionStrength.Text = _currentConfig.DistortionStrength.ToString("0.00");
			if (_txtNoiseUvScrollX != null) _txtNoiseUvScrollX.Text = _currentConfig.NoiseUvScroll.X.ToString("0.##");
			if (_txtNoiseUvScrollY != null) _txtNoiseUvScrollY.Text = _currentConfig.NoiseUvScroll.Y.ToString("0.##");
			if (_txtNoiseUvScaleX != null) _txtNoiseUvScaleX.Text = _currentConfig.NoiseUvScale.X.ToString("0.##");
			if (_txtNoiseUvScaleY != null) _txtNoiseUvScaleY.Text = _currentConfig.NoiseUvScale.Y.ToString("0.##");

			Color baseCol = VfxShaderManager.ParseColorSafe(_currentConfig.BaseColor, Colors.Orange);
			if (_pickerBaseColor != null) _pickerBaseColor.Color = baseCol;
			if (_sliderBaseColorHue != null) _sliderBaseColorHue.Value = baseCol.H;

			Color secCol = VfxShaderManager.ParseColorSafe(_currentConfig.SecondaryColor, Colors.DarkRed);
			if (_pickerSecondaryColor != null) _pickerSecondaryColor.Color = secCol;
			if (_sliderSecondaryColorHue != null) _sliderSecondaryColorHue.Value = secCol.H;

			Color coreCol = VfxShaderManager.ParseColorSafe(_currentConfig.CoreColor, Colors.White);
			if (_pickerCoreColor != null) _pickerCoreColor.Color = coreCol;
			if (_sliderCoreColorHue != null) _sliderCoreColorHue.Value = coreCol.H;

			if (_sliderEmissionBoost != null) _sliderEmissionBoost.Value = _currentConfig.EmissionBoost;
			if (_lblEmissionBoost != null) _lblEmissionBoost.Text = _currentConfig.EmissionBoost.ToString("0.0");
			if (_sliderCoreThreshold != null) _sliderCoreThreshold.Value = _currentConfig.CoreThreshold;
			if (_lblCoreThreshold != null) _lblCoreThreshold.Text = _currentConfig.CoreThreshold.ToString("0.00");

			if (_chkRadialFalloff != null) _chkRadialFalloff.ButtonPressed = _currentConfig.EnableRadialFalloff;
			if (_sliderRadialFalloffStart != null) _sliderRadialFalloffStart.Value = _currentConfig.RadialFalloffStart;
			if (_lblRadialFalloffStart != null) _lblRadialFalloffStart.Text = _currentConfig.RadialFalloffStart.ToString("0.00");
			if (_sliderRadialFalloffEnd != null) _sliderRadialFalloffEnd.Value = _currentConfig.RadialFalloffEnd;
			if (_lblRadialFalloffEnd != null) _lblRadialFalloffEnd.Text = _currentConfig.RadialFalloffEnd.ToString("0.00");

			if (_chkLengthFade != null) _chkLengthFade.ButtonPressed = _currentConfig.EnableLengthFade;
			if (_sliderLengthFadeStart != null) _sliderLengthFadeStart.Value = _currentConfig.LengthFadeStart;
			if (_lblLengthFadeStart != null) _lblLengthFadeStart.Text = _currentConfig.LengthFadeStart.ToString("0.00");
			if (_sliderLengthFadeEnd != null) _sliderLengthFadeEnd.Value = _currentConfig.LengthFadeEnd;
			if (_lblLengthFadeEnd != null) _lblLengthFadeEnd.Text = _currentConfig.LengthFadeEnd.ToString("0.00");
			if (_sliderErosionProgress != null) _sliderErosionProgress.Value = _currentConfig.ErosionProgress;
			if (_lblErosionProgress != null) _lblErosionProgress.Text = _currentConfig.ErosionProgress.ToString("0.00");

			if (_chkFresnel != null) _chkFresnel.ButtonPressed = _currentConfig.EnableFresnel;
			if (_sliderFresnelPower != null) _sliderFresnelPower.Value = _currentConfig.FresnelPower;
			if (_lblFresnelPower != null) _lblFresnelPower.Text = _currentConfig.FresnelPower.ToString("0.0");
			if (_sliderFresnelIntensity != null) _sliderFresnelIntensity.Value = _currentConfig.FresnelIntensity;
			if (_lblFresnelIntensity != null) _lblFresnelIntensity.Text = _currentConfig.FresnelIntensity.ToString("0.0");

			if (_chkDepthFade != null) _chkDepthFade.ButtonPressed = _currentConfig.EnableDepthFade;
			if (_sliderDepthFadeDistance != null) _sliderDepthFadeDistance.Value = _currentConfig.DepthFadeDistance;
			if (_lblDepthFadeDistance != null) _lblDepthFadeDistance.Text = _currentConfig.DepthFadeDistance.ToString("0.00");

			if (_currentConfig.ParticleConfig != null)
			{
				if (_optParticleShape != null) _optParticleShape.Selected = (int)_currentConfig.ParticleConfig.EmitterShape;
				if (_optParticleRenderMode != null) _optParticleRenderMode.Selected = (int)_currentConfig.ParticleConfig.RenderMode;
				if (_setParticleTextureVal != null) _setParticleTextureVal(_currentConfig.ParticleConfig.ParticleTexture ?? string.Empty);
				if (_setParticleMeshVal != null) _setParticleMeshVal(_currentConfig.ParticleConfig.MeshAssetPath ?? string.Empty);

				if (_sliderParticleAmount != null) _sliderParticleAmount.Value = _currentConfig.ParticleConfig.Amount;
				if (_lblParticleAmount != null) _lblParticleAmount.Text = _currentConfig.ParticleConfig.Amount.ToString();
				if (_sliderParticleLifetime != null) _sliderParticleLifetime.Value = _currentConfig.ParticleConfig.Lifetime;
				if (_lblParticleLifetime != null) _lblParticleLifetime.Text = _currentConfig.ParticleConfig.Lifetime.ToString("0.0");
				if (_sliderParticleExplosiveness != null) _sliderParticleExplosiveness.Value = _currentConfig.ParticleConfig.Explosiveness;
				if (_lblParticleExplosiveness != null) _lblParticleExplosiveness.Text = _currentConfig.ParticleConfig.Explosiveness.ToString("0.00");
				if (_chkParticleLocalCoords != null) _chkParticleLocalCoords.ButtonPressed = _currentConfig.ParticleConfig.LocalCoords;

				if (_txtParticleDirX != null) _txtParticleDirX.Text = _currentConfig.ParticleConfig.Direction.X.ToString("0.##");
				if (_txtParticleDirY != null) _txtParticleDirY.Text = _currentConfig.ParticleConfig.Direction.Y.ToString("0.##");
				if (_txtParticleDirZ != null) _txtParticleDirZ.Text = _currentConfig.ParticleConfig.Direction.Z.ToString("0.##");

				if (_sliderParticleSpread != null) _sliderParticleSpread.Value = _currentConfig.ParticleConfig.SpreadDegrees;
				if (_lblParticleSpread != null) _lblParticleSpread.Text = _currentConfig.ParticleConfig.SpreadDegrees.ToString("0");
				if (_sliderParticleVelMin != null) _sliderParticleVelMin.Value = _currentConfig.ParticleConfig.InitialVelocityMin;
				if (_lblParticleVelMin != null) _lblParticleVelMin.Text = _currentConfig.ParticleConfig.InitialVelocityMin.ToString("0.0");
				if (_sliderParticleVelMax != null) _sliderParticleVelMax.Value = _currentConfig.ParticleConfig.InitialVelocityMax;
				if (_lblParticleVelMax != null) _lblParticleVelMax.Text = _currentConfig.ParticleConfig.InitialVelocityMax.ToString("0.0");

				if (_txtParticleGravX != null) _txtParticleGravX.Text = _currentConfig.ParticleConfig.Gravity.X.ToString("0.##");
				if (_txtParticleGravY != null) _txtParticleGravY.Text = _currentConfig.ParticleConfig.Gravity.Y.ToString("0.##");
				if (_txtParticleGravZ != null) _txtParticleGravZ.Text = _currentConfig.ParticleConfig.Gravity.Z.ToString("0.##");

				if (_sliderParticleDamping != null) _sliderParticleDamping.Value = _currentConfig.ParticleConfig.Damping;
				if (_lblParticleDamping != null) _lblParticleDamping.Text = _currentConfig.ParticleConfig.Damping.ToString("0.0");
				if (_sliderParticleRadialAccel != null) _sliderParticleRadialAccel.Value = _currentConfig.ParticleConfig.RadialAccel;
				if (_lblParticleRadialAccel != null) _lblParticleRadialAccel.Text = _currentConfig.ParticleConfig.RadialAccel.ToString("0.0");
				if (_sliderParticleTangentialAccel != null) _sliderParticleTangentialAccel.Value = _currentConfig.ParticleConfig.TangentialAccel;
				if (_lblParticleTangentialAccel != null) _lblParticleTangentialAccel.Text = _currentConfig.ParticleConfig.TangentialAccel.ToString("0.0");

				if (_sliderParticleScaleMin != null) _sliderParticleScaleMin.Value = _currentConfig.ParticleConfig.InitialScaleMin;
				if (_lblParticleScaleMin != null) _lblParticleScaleMin.Text = _currentConfig.ParticleConfig.InitialScaleMin.ToString("0.00");
				if (_sliderParticleScaleMax != null) _sliderParticleScaleMax.Value = _currentConfig.ParticleConfig.InitialScaleMax;
				if (_lblParticleScaleMax != null) _lblParticleScaleMax.Text = _currentConfig.ParticleConfig.InitialScaleMax.ToString("0.00");
				if (_sliderParticleEndScaleRatio != null) _sliderParticleEndScaleRatio.Value = _currentConfig.ParticleConfig.EndScaleRatio;
				if (_lblParticleEndScaleRatio != null) _lblParticleEndScaleRatio.Text = _currentConfig.ParticleConfig.EndScaleRatio.ToString("0.00");

				Color pStart = VfxShaderManager.ParseColorSafe(_currentConfig.ParticleConfig.ColorStart, Colors.Gold);
				if (_pickerParticleColorStart != null) _pickerParticleColorStart.Color = pStart;
				if (_sliderParticleColorStartHue != null) _sliderParticleColorStartHue.Value = pStart.H;

				Color pMid = VfxShaderManager.ParseColorSafe(_currentConfig.ParticleConfig.ColorMid, Colors.DarkOrange);
				if (_pickerParticleColorMid != null) _pickerParticleColorMid.Color = pMid;
				if (_sliderParticleColorMidHue != null) _sliderParticleColorMidHue.Value = pMid.H;

				Color pEnd = VfxShaderManager.ParseColorSafe(_currentConfig.ParticleConfig.ColorEnd, Colors.Maroon);
				if (_pickerParticleColorEnd != null) _pickerParticleColorEnd.Color = pEnd;
				if (_sliderParticleColorEndHue != null) _sliderParticleColorEndHue.Value = pEnd.H;

				if (_sliderParticleEmissionEnergy != null) _sliderParticleEmissionEnergy.Value = _currentConfig.ParticleConfig.EmissionEnergy;
				if (_lblParticleEmissionEnergy != null) _lblParticleEmissionEnergy.Text = _currentConfig.ParticleConfig.EmissionEnergy.ToString("0.0");
			}

			if (_sliderSurfaceNormalOffset != null) _sliderSurfaceNormalOffset.Value = _currentConfig.SurfaceNormalOffset;
			if (_lblSurfaceNormalOffset != null) _lblSurfaceNormalOffset.Text = _currentConfig.SurfaceNormalOffset.ToString("0.000");

			if (_txtPosOffsetX != null) _txtPosOffsetX.Text = _currentConfig.PositionOffset.X.ToString("0.##");
			if (_txtPosOffsetY != null) _txtPosOffsetY.Text = _currentConfig.PositionOffset.Y.ToString("0.##");
			if (_txtPosOffsetZ != null) _txtPosOffsetZ.Text = _currentConfig.PositionOffset.Z.ToString("0.##");

			if (_txtRotOffsetX != null) _txtRotOffsetX.Text = _currentConfig.RotationOffset.X.ToString("0.##");
			if (_txtRotOffsetY != null) _txtRotOffsetY.Text = _currentConfig.RotationOffset.Y.ToString("0.##");
			if (_txtRotOffsetZ != null) _txtRotOffsetZ.Text = _currentConfig.RotationOffset.Z.ToString("0.##");

			if (_txtScaleOffsetX != null) _txtScaleOffsetX.Text = _currentConfig.ScaleOffset.X.ToString("0.##");
			if (_txtScaleOffsetY != null) _txtScaleOffsetY.Text = _currentConfig.ScaleOffset.Y.ToString("0.##");
			if (_txtScaleOffsetZ != null) _txtScaleOffsetZ.Text = _currentConfig.ScaleOffset.Z.ToString("0.##");

			UpdateSectionVisibilities();
		}
		finally
		{
			_isUpdatingUI = false;
		}
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

		void CollectFromDir(string subFolder)
		{
			string dir = Path.Combine(wsPath, "Assets", subFolder);
			if (Directory.Exists(dir))
			{
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

		CollectFromDir("ribbons");
		CollectFromDir("decals");
		CollectFromDir("textures");
		CollectFromDir("vfx");
		CollectFromDir("noise");

		try
		{
			var assetsObj = MapAssetHelper.LoadAssets(wsPath);
			if (assetsObj != null)
			{
				foreach (var catName in new[] { "Ribbon", "Decal", "Terrain", "Spritesheet", "Noise" })
				{
					var catDict = assetsObj.GetCategory(catName);
					if (catDict != null)
					{
						foreach (var kvp in catDict)
						{
							if (!string.IsNullOrEmpty(kvp.Key))
							{
								results.Add(Path.GetFileName(kvp.Key));
							}
						}
					}
				}
			}
		}
		catch { }

		return results.OrderBy(s => s, StringComparer.OrdinalIgnoreCase).ToList();
	}

	private List<string> ScanNoiseAssets()
	{
		var results = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
		string wsPath = ProjectSettings.GlobalizePath(MapEditorHUD.TempWorkspaceGodotPath);

		string dir = Path.Combine(wsPath, "Assets", "noise");
		if (Directory.Exists(dir))
		{
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

		try
		{
			var assetsObj = MapAssetHelper.LoadAssets(wsPath);
			if (assetsObj != null)
			{
				var noiseDict = assetsObj.GetCategory("Noise");
				if (noiseDict != null)
				{
					foreach (var kvp in noiseDict)
					{
						if (!string.IsNullOrEmpty(kvp.Key))
						{
							results.Add(Path.GetFileName(kvp.Key));
						}
					}
				}
			}
		}
		catch { }

		return results.OrderBy(s => s, StringComparer.OrdinalIgnoreCase).ToList();
	}

	private List<string> ScanProjectileMeshAssets()
	{
		var results = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
		string wsPath = ProjectSettings.GlobalizePath(MapEditorHUD.TempWorkspaceGodotPath);

		try
		{
			var assetsObj = MapAssetHelper.LoadAssets(wsPath);
			if (assetsObj != null)
			{
				foreach (var catName in new[] { "Character", "Building", "Prop", "Item" })
				{
					var catDict = assetsObj.GetCategory(catName);
					if (catDict != null)
					{
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
			}
		}
		catch { }

		void ScanFolder(string folderPath)
		{
			if (Directory.Exists(folderPath))
			{
				foreach (var file in Directory.GetFiles(folderPath, "*.rmesh"))
				{
					results.Add(Path.GetFileName(file));
				}
			}
		}

		ScanFolder(Path.Combine(wsPath, "Assets", "models", "projectiles"));
		ScanFolder(Path.Combine(wsPath, "Assets", "models", "props"));
		string templateDir = PathUtils.FindPath("MapTemplate/Assets/models/projectiles");
		if (!string.IsNullOrEmpty(templateDir)) ScanFolder(templateDir);

		return results.OrderBy(s => s, StringComparer.OrdinalIgnoreCase).ToList();
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
						Hash = blake3Hash,
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
			MetadataService.Instance.UpdateMetadata(wsPath, m =>
			{
				m.VfxSpritesheets ??= new(StringComparer.OrdinalIgnoreCase);
				m.VfxSpritesheets[fileName] = new Realm.Shared.Metadata.VfxMetadata
				{
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
}
