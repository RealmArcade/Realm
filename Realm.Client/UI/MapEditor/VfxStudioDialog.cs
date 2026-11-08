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

[GlobalClass]
public partial class VfxStudioDialog : FloatingPreview3DDialogBase
{
	public VfxStudioDialog() { }

	private ProceduralVfxInstance3D _previewVfxInstance = null!;
	private MeshInstance3D _previewGroundGrid = null!;

	private string _vfxId = "";
	private string _slug = "";
	private Label _lblObjectTypePrefix = null!;
	private LineEdit _txtSlug = null!;
	private LineEdit _txtVfxName = null!;
	private OptionButton _optMode = null!;
	private OptionButton _optPrimitive = null!;
	private Control? _rowPrimitive;
	private OptionButton _optBlendMode = null!;
	private OptionButton _optPlacementMode = null!;
	private CheckBox _chkTintWithPlayerColor = null!;
	private Control? _rowBaseTexture;
	private Control? _rowParticleTexture;
	private Control? _rowParticleMesh;
	private VBoxContainer _uberShaderContainer = null!;
	private VBoxContainer _particleContainer = null!;
	private VBoxContainer _stagesContainer = null!;
	private static readonly VfxPrimitiveType[] _primitiveTypes = Enum.GetValues<VfxPrimitiveType>().Where(p => p != VfxPrimitiveType.ParticleSystem).ToArray();
	private static readonly VfxPlacementMode[] _particlePlacementModes = { VfxPlacementMode.SurfaceSnap, VfxPlacementMode.Free };

	private OptionButton _optParticleShape = null!;
	private OptionButton _optParticleRenderMode = null!;
	private Control? _rowParticleRenderMode;
	private Action<string> _setParticleTextureVal = null!;
	private Action<string> _setParticleMeshVal = null!;
	private Action<string> _setBaseTextureVal = null!;
	private Action<string> _setNoiseTextureVal = null!;

	private CheckBox _chkLuminanceToAlpha = null!;
	private HSlider _sliderLuminanceThreshold = null!;
	private Label _lblLuminanceThreshold = null!;
	private HSlider _sliderLuminanceSmoothness = null!;
	private Label _lblLuminanceSmoothness = null!;
	private CheckBox _chkUseGrayscale = null!;
	private CheckBox _chkInvertMask = null!;
	private HSlider _sliderHighPassCutoff = null!;
	private Label _lblHighPassCutoff = null!;
	private LineEdit _txtBaseUvScrollX = null!;
	private LineEdit _txtBaseUvScrollY = null!;
	private LineEdit _txtBaseUvScaleX = null!;
	private LineEdit _txtBaseUvScaleY = null!;
	private HSlider _sliderDistortionStrength = null!;
	private Label _lblDistortionStrength = null!;
	private LineEdit _txtNoiseUvScrollX = null!;
	private LineEdit _txtNoiseUvScrollY = null!;
	private LineEdit _txtNoiseUvScaleX = null!;
	private LineEdit _txtNoiseUvScaleY = null!;
	private ColorPickerButton _pickerBaseColor = null!;
	private HSlider _sliderBaseColorHue = null!;
	private ColorPickerButton _pickerSecondaryColor = null!;
	private HSlider _sliderSecondaryColorHue = null!;
	private ColorPickerButton _pickerCoreColor = null!;
	private HSlider _sliderCoreColorHue = null!;
	private HSlider _sliderColorMixRatio = null!;
	private Label _lblColorMixRatio = null!;
	private HSlider _sliderEmissionBoost = null!;
	private Label _lblEmissionBoost = null!;
	private HSlider _sliderCoreThreshold = null!;
	private Label _lblCoreThreshold = null!;
	private CheckBox _chkRadialFalloff = null!;
	private HSlider _sliderRadialFalloffStart = null!;
	private Label _lblRadialFalloffStart = null!;
	private HSlider _sliderRadialFalloffEnd = null!;
	private Label _lblRadialFalloffEnd = null!;
	private CheckBox _chkLengthFade = null!;
	private HSlider _sliderLengthFadeStart = null!;
	private Label _lblLengthFadeStart = null!;
	private HSlider _sliderLengthFadeEnd = null!;
	private Label _lblLengthFadeEnd = null!;
	private HSlider _sliderErosionProgress = null!;
	private Label _lblErosionProgress = null!;
	private CheckBox _chkFresnel = null!;
	private HSlider _sliderFresnelPower = null!;
	private Label _lblFresnelPower = null!;
	private HSlider _sliderFresnelIntensity = null!;
	private Label _lblFresnelIntensity = null!;
	private CheckBox _chkDepthFade = null!;
	private HSlider _sliderDepthFadeDistance = null!;
	private Label _lblDepthFadeDistance = null!;

	private OptionButton _optProceduralMotion = null!;
	private List<string> _proceduralMotionIds = new();

	private HSlider _sliderParticleAmount = null!;
	private Label _lblParticleAmount = null!;
	private HSlider _sliderParticleLifetime = null!;
	private Label _lblParticleLifetime = null!;
	private HSlider _sliderParticleExplosiveness = null!;
	private Label _lblParticleExplosiveness = null!;
	private CheckBox _chkParticleLocalCoords = null!;
	private LineEdit _txtParticleDirX = null!;
	private LineEdit _txtParticleDirY = null!;
	private LineEdit _txtParticleDirZ = null!;
	private HSlider _sliderParticleSpread = null!;
	private Label _lblParticleSpread = null!;
	private HSlider _sliderParticleVelMin = null!;
	private Label _lblParticleVelMin = null!;
	private HSlider _sliderParticleVelMax = null!;
	private Label _lblParticleVelMax = null!;
	private LineEdit _txtParticleGravX = null!;
	private LineEdit _txtParticleGravY = null!;
	private LineEdit _txtParticleGravZ = null!;
	private HSlider _sliderParticleDamping = null!;
	private Label _lblParticleDamping = null!;
	private HSlider _sliderParticleRadialAccel = null!;
	private Label _lblParticleRadialAccel = null!;
	private HSlider _sliderParticleTangentialAccel = null!;
	private Label _lblParticleTangentialAccel = null!;
	private HSlider _sliderParticleScaleMin = null!;
	private Label _lblParticleScaleMin = null!;
	private HSlider _sliderParticleScaleMax = null!;
	private Label _lblParticleScaleMax = null!;
	private HSlider _sliderParticleEndScaleRatio = null!;
	private Label _lblParticleEndScaleRatio = null!;
	private ColorPickerButton _pickerParticleColorStart = null!;
	private HSlider _sliderParticleColorStartHue = null!;
	private ColorPickerButton _pickerParticleColorMid = null!;
	private HSlider _sliderParticleColorMidHue = null!;
	private ColorPickerButton _pickerParticleColorEnd = null!;
	private HSlider _sliderParticleColorEndHue = null!;
	private HSlider _sliderParticleEmissionEnergy = null!;
	private Label _lblParticleEmissionEnergy = null!;

	private HSlider _sliderSurfaceNormalOffset = null!;
	private Label _lblSurfaceNormalOffset = null!;
	private LineEdit _txtPosOffsetX = null!;
	private LineEdit _txtPosOffsetY = null!;
	private LineEdit _txtPosOffsetZ = null!;
	private LineEdit _txtRotOffsetX = null!;
	private LineEdit _txtRotOffsetY = null!;
	private LineEdit _txtRotOffsetZ = null!;
	private LineEdit _txtScaleOffsetX = null!;
	private LineEdit _txtScaleOffsetY = null!;
	private LineEdit _txtScaleOffsetZ = null!;

	private string? _tempGeneratedNoiseFileName;
	private JsonObject? _tempGeneratedNoiseConfig;

	private SpritesheetAssetEditDialog _spritesheetEditDialog = null!;

	private VfxAttachmentConfig _currentConfig = new();
	private VfxAttachmentConfig _initialConfig = new();
	private Action<VfxAttachmentConfig>? _onAppliedCallback;
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
		topToolbar.Name = "TopToolbar_HBoxContainer";
		topToolbar.AddThemeConstantOverride("separation", 4);

		AddButton(topToolbar, $"{UnicodeIcons.PLAY} " + TranslationServer.Translate("VFX"), () =>
		{
			_lastPreviewKind = VfxPreviewKind.VisualEffect;
			RestartPreviewVfx();
		}, "Restart procedural effect", 10, new Vector2(0, 22));
		AddButton(topToolbar, $"{UnicodeIcons.WATER} " + TranslationServer.Translate("Wave"), () => TriggerPreviewShockwave(), "Test directional ground planar shockwave", 10, new Vector2(0, 22));
		AddButton(topToolbar, $"{UnicodeIcons.CIRCLE} " + TranslationServer.Translate("Ring"), () => TriggerPreviewRing(), "Test radial expanding ground ring", 10, new Vector2(0, 22));
		AddButton(topToolbar, $"{UnicodeIcons.SUN} " + TranslationServer.Translate("Burst"), () => TriggerPreviewBurst(), "Test expanding 3D burst sphere", 10, new Vector2(0, 22));
		AddButton(topToolbar, $"{UnicodeIcons.EXPAND_ARROWS} " + TranslationServer.Translate("Chain"), () => TriggerPreviewChain(), "Test multi-segment ribbon chain beam", 10, new Vector2(0, 22));
		AddButton(topToolbar, $"{UnicodeIcons.CLOUD_SHOWERS} " + TranslationServer.Translate("Barrage"), () => TriggerPreviewBarrage(), "Test randomized area barrage volley", 10, new Vector2(0, 22));

		var separator = new Control { SizeFlagsHorizontal = SizeFlags.ExpandFill };
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
		rowRandomize.Name = "RowRandomize_HBoxContainer";
		var btnRandomize = AddButton(rowRandomize, $"{UnicodeIcons.DICE} " + TranslationServer.Translate("Randomize All"), () => { RandomizeAllParameters(); }, "Randomize all noise masks, shapes, uv scrolling, and color parameters completely");
		btnRandomize.SizeFlagsHorizontal = SizeFlags.ExpandFill;

		AddButton(rowRandomize, $"{UnicodeIcons.COPY} " + TranslationServer.Translate("Copy Code"), () => CopyScriptCode(), "Copy C# MapScript code reference to clipboard", 10, new Vector2(0, 22));
		parent.AddChild(rowRandomize);
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
		_particleContainer.Name = "ParticleContainer_VBoxContainer";
		scrollBody.AddChild(_particleContainer);
		BuildParticleControls(_particleContainer);

		BuildTransformSettings(scrollBody);
		BuildStagesSection(scrollBody);
	}

	public void OpenForConfig(VfxAttachmentConfig? config, Action<VfxAttachmentConfig>? onApplied = null)
	{
		_initialConfig = config?.Clone() ?? new VfxAttachmentConfig();
		_currentConfig = config?.Clone() ?? new VfxAttachmentConfig();
		_onAppliedCallback = onApplied;

		if (_currentConfig.PrimitiveType == VfxPrimitiveType.ParticleSystem || _currentConfig.ParticleConfig != null)
		{
			EnsureParticleConfig();
		}

		TitleLabel.Text = $"{TranslationServer.Translate("Procedural VFX Studio")} - {(!string.IsNullOrEmpty(_currentConfig.Name) ? _currentConfig.Name : _currentConfig.VfxId)}";

		RefreshProceduralMotionOptions();
		UpdateUIFromCurrentConfig();
		OpenDialog();
		ResetCameraDefault();
		RestartPreviewVfx();
	}

	// Rows are built lazily per primitive type, so an absent row is normal rather than a bug.
	private static void SetVisibleSafe(Control? c, bool visible)
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

	// The particle controls cannot render an absent section, so touching one creates it rather than
	// reporting a null the caller would have to re-check at every widget.
	private SpellParticleConfig Particle => _currentConfig.ParticleConfig ??= new SpellParticleConfig
	{
		ParticleId = _currentConfig.VfxId,
		Name = _currentConfig.Name
	};

	protected override void OnApply()
	{
		SyncConfigFromControls();

		if (!string.IsNullOrEmpty(_tempGeneratedNoiseFileName) &&
		    _tempGeneratedNoiseConfig != null &&
		    string.Equals(_currentConfig.NoiseTexture, _tempGeneratedNoiseFileName, StringComparison.OrdinalIgnoreCase))
		{
			try
			{
				string wsPath = GameContext.EditorService._editingDirectory;
				string outputRtex = Path.Combine(wsPath, "Assets", "noise", _tempGeneratedNoiseFileName);
				string blake3Hash = NoiseTextureGenerator.GenerateAndSaveRtex(_tempGeneratedNoiseConfig, outputRtex);

				MapAssetHelper.UpdateManifestAsset(wsPath, "Noise", _tempGeneratedNoiseFileName, blake3Hash);

				GameContext.MetadataService.UpdateMetadata(wsPath, m =>
				{
					m.NoiseTextures ??= new(StringComparer.OrdinalIgnoreCase);
					m.NoiseTextures[_tempGeneratedNoiseFileName] = new TextureMetadata
					{
						NoiseConfig = _tempGeneratedNoiseConfig.ToJsonString()
					};
				});
				Hud?.ReadMetadataAndRefreshTextures();
			}
			catch (Exception ex)
			{
				GameLog.LogError($"[VfxStudioDialog] Failed to save generated noise asset to manifest: {ex.Message}");
			}

			_tempGeneratedNoiseFileName = null;
			_tempGeneratedNoiseConfig = null;
		}
		else
		{
			CleanupTransientNoiseFile();
		}

		Hud?.SaveCustomVfxToMetadata(_currentConfig.VfxId, _currentConfig);
		GameContext.Host?.RefreshVfxTemplateInstances(_initialConfig.VfxId, _currentConfig);
		_onAppliedCallback?.Invoke(_currentConfig);
		Hud?.ShowFeedback(string.Format(TranslationServer.Translate("Saved VFX '{0}' to metadata.json"), _currentConfig.Name));
	}

	protected override void OnCancel()
	{
		CleanupTransientNoiseFile();
		_currentConfig = _initialConfig.Clone();
	}

}