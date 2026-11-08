using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Godot;
using Realm.Client.Core;
using Realm.Client.Network;
using Realm.Client.Services;
using Realm.Client.Services.MapHistory;
using Realm.Client.UI.MapEditor;
using Realm.Ecs.Components.Core;
using Realm.Ecs.Services;
using Realm.Shared.Distribution;
using static Godot.Control;

namespace Realm.Client.UI;

[GlobalClass]
public partial class MapEditorHUD : Control
{
	public MapEditorHUD()
	{
		GameContext.RegisterMapEditorHud(this);
	}

	public override void _EnterTree()
	{
		Realm.Client.Services.GameContext.EnsureServices();

		if (OperatingSystem.IsWindows()) DotnetSdkInstaller.EnsureInstalledInBackground();
	}

	public static string defaultLoadFolder
	{
		get => !string.IsNullOrEmpty(GameSettings.LastOpenedFolder) ? GameSettings.LastOpenedFolder : GetDocumentsDirectory();
		set
		{
			GameSettings.LastOpenedFolder = value ?? string.Empty;
			GameSettings.Save();
		}
	}

	public static string defaultSaveFolder { get; set; } = GetDocumentsDirectory();

	public static string? mapSaveFolder { get; set; } = null;

	public static bool IsDraggingSlider { get; set; } = false;


	private static bool _agreementShownThisSession { get; set; } = false;

	private static string? _lastMapLoadFailure { get; set; } = null;


	public MapEditorHUDViewModel _viewModel { get; set; } = new();

	private Panel _panelLeft { get; set; } = null!;
	private Button _btnLeftTab { get; set; } = null!;
	private Panel _panelRight { get; set; } = null!;
	private Button _btnRightTab { get; set; } = null!;
	private VBoxContainer _accordionContainer { get; set; } = null!;

	private bool _leftPanelExpanded { get; set; } = false;
	private bool _rightPanelExpanded { get; set; } = true;

	private OptionButton _optModule { get; set; } = null!;
	private Button _btnGameSettings { get; set; } = null!;
	private EditorModule _activeModule { get; set; } = EditorModule.Terrain;

	private VBoxContainer _accordionBrush { get; set; } = null!;
	private Button _btnHeaderBrush { get; set; } = null!;
	private VBoxContainer _contentBrush { get; set; } = null!;

	private VBoxContainer _accordionWater { get; set; } = null!;
	private Button _btnHeaderWater { get; set; } = null!;
	private VBoxContainer _contentWater { get; set; } = null!;
	private Button _btnWaterActionAdd { get; set; } = null!;
	private Button _btnWaterActionRemove { get; set; } = null!;


	private VBoxContainer _accordionTool { get; set; } = null!;
	private Button _btnHeaderTool { get; set; } = null!;
	private VBoxContainer _contentTool { get; set; } = null!;

	private VBoxContainer _accordionToolSettings { get; set; } = null!;
	private Button _btnHeaderToolSettings { get; set; } = null!;
	private VBoxContainer _contentToolSettings { get; set; } = null!;

	private VBoxContainer _accordionPlacement { get; set; } = null!;
	private Button _btnHeaderPlacement { get; set; } = null!;
	private VBoxContainer _contentPlacement { get; set; } = null!;

	private VBoxContainer _accordionViewport { get; set; } = null!;
	private Button _btnHeaderViewport { get; set; } = null!;
	private VBoxContainer _contentViewport { get; set; } = null!;


	private Dictionary<Control, CardDragData> _cardDragMap { get; set; } = new();
	private Button _btnResetLayout { get; set; } = null!;

	private VBoxContainer _accordionFile { get; set; } = null!;
	private Button _btnHeaderFile { get; set; } = null!;
	private Control _contentFile { get; set; } = null!;

	private MapSettingsDialog? _mapSettingsDialog { get; set; }

	private VBoxContainer _accordionInspector { get; set; } = null!;
	private Button _btnHeaderInspector { get; set; } = null!;
	private VBoxContainer _contentInspector { get; set; } = null!;

	private VBoxContainer _containerTextureSettings { get; set; } = null!;
	private VBoxContainer _containerPathingSettings { get; set; } = null!;
	private VBoxContainer _containerPlacementSettings { get; set; } = null!;
	private VBoxContainer _containerEyedropperSettings { get; set; } = null!;
	private VBoxContainer _containerPasteSettings { get; set; } = null!;
	private VBoxContainer _containerCategorySelector { get; set; } = null!;

	private VBoxContainer _panelObjects { get; set; } = null!;
	private VBoxContainer _panelClipboard { get; set; } = null!;
	private VBoxContainer _panelTerrainVBox { get; set; } = null!;
	private VBoxContainer _panelDecoVBox { get; set; } = null!;
	private VBoxContainer _panelPathingVBox { get; set; } = null!;
	private VBoxContainer _panelCoordinatesVBox { get; set; } = null!;
	private Button _btnCut { get; set; } = null!;
	private Button _btnEraseArea { get; set; } = null!;
	private Button _btnPasteReflection { get; set; } = null!;
	private Button _btnClipboardBrushShape { get; set; } = null!;
	private OptionButton _optClipboardMirrorMode { get; set; } = null!;
	private HSlider _sldPasteRotation { get; set; } = null!;
	private Label _lblPasteRotation { get; set; } = null!;

	private OptionButton _optPolarRingSpacing { get; set; } = null!;
	private OptionButton _optPolarRadialStep { get; set; } = null!;
	private Button _btnTapeMeasure { get; set; } = null!;
	private Button _btnResetPivotToCenter { get; set; } = null!;
	private HBoxContainer _rowPolarConfig { get; set; } = null!;

	private PanelContainer _panelMeasurementHUD { get; set; } = null!;
	private Label _lblMeasureTelemetry { get; set; } = null!;
	private ThrottledLabelWidth _measureLabelWidth { get; set; } = new();

	private Button _btnPasteAnchor { get; set; } = null!;
	private Label _lblPasteTelemetry { get; set; } = null!;


	private List<Button> _swatchButtons { get; set; } = new List<Button>();


	private ScrollContainer _scrollSwatches { get; set; } = null!;
	private Control _gridSwatches { get; set; } = null!;
	private Button _btnReplaceTexture { get; set; } = null!;

	private PanelContainer _topBar { get; set; } = null!;
	private VBoxContainer _middleRightBox { get; set; } = null!;
	private HBoxContainer _topLeftBox { get; set; } = null!;
	private TextureRect _screenFrameRect { get; set; } = null!;
	private Tween _hudFadeTween { get; set; } = null!;
	private bool _is3DInteractionActive { get; set; } = false;

	private Dictionary<Control, Control.MouseFilterEnum> _savedMouseFilters { get; set; } = new();

	private Button _btnBackToHub { get; set; } = null!;
	private Button _btnPublish { get; set; } = null!;
	private Button _btnSave { get; set; } = null!;
	private Button _btnSaveAs { get; set; } = null!;
	private Button _btnTestMap { get; set; } = null!;
	private OptionButton _optTestSettings { get; set; } = null!;
	private Button _btnExportMap { get; set; } = null!;
	private Button _btnLoad { get; set; } = null!;
	private Button _btnLoadMore { get; set; } = null!;
	private PopupPanel _popupLoadMore { get; set; } = null!;
	private Button _btnImportArchive { get; set; } = null!;
	private Button _btnDeleteObject { get; set; } = null!;
	private Button _btnUndo { get; set; } = null!;
	private Button _btnRedo { get; set; } = null!;

	private Label _statusLabel { get; set; } = null!;
	private Label _feedbackLabel { get; set; } = null!;

	private Button _btnZoomIn { get; set; } = null!;
	private Button _btnZoomOut { get; set; } = null!;
	private Button _btnCenter { get; set; } = null!;
	private Button _btnRotate { get; set; } = null!;
	private Button _btnCameraAngle { get; set; } = null!;

	private Slider _sldBrushSize { get; set; } = null!;
	private Label _lblBrushSizeValue { get; set; } = null!;
	private Control _brushSizeBox { get; set; } = null!;
	private Slider _sldBrushStrength { get; set; } = null!;
	private Label _lblBrushStrengthValue { get; set; } = null!;


	private CheckBox _chkRandomRotation { get; set; } = null!;
	private CheckBox _chkRandomScale { get; set; } = null!;
	private Button _btnAddObject { get; set; } = null!;
	private CheckBox _chkClumpMode { get; set; } = null!;
	private Control _spacingBox { get; set; } = null!;
	private Control _densityBox { get; set; } = null!;
	private Control _scaleVarBox { get; set; } = null!;
	private Control _camBoundsBox { get; set; } = null!;
	private CheckBox _chkBlockMode { get; set; } = null!;
	private Slider _sldBlockStep { get; set; } = null!;
	private Label _lblBlockStepValue { get; set; } = null!;
	private Control _heightBox { get; set; } = null!;
	private Slider _sldHeight { get; set; } = null!;
	private Label _lblHeightValue { get; set; } = null!;

	private Control _waterHeightBox { get; set; } = null!;
	private Slider _sldWaterHeight { get; set; } = null!;
	private Label _lblWaterHeightValue { get; set; } = null!;
	private Control _waterModeBox { get; set; } = null!;
	private OptionButton _optWaterMode { get; set; } = null!;
	private Button _btnWaterProfiles { get; set; } = null!;
	private WaterProfileDialog _waterProfileDialog { get; set; } = null!;
	private EnvironmentConfigDialog _environmentConfigDialog { get; set; } = null!;
	private EntityVisualEditDialog _entityVisualEditDialog { get; set; } = null!;
	private AnimationPreviewDialog _animationPreviewDialog { get; set; } = null!;
	private WeaponVfxDialog _weaponVfxDialog { get; set; } = null!;
	private ModelPickerDialog _modelPickerDialog { get; set; } = null!;
	private AbilityVfxDialog _abilityVfxDialog { get; set; } = null!;
	private AssetManagerDialog _assetManagerDialog { get; set; } = null!;
	private ImportFromMapDialog _importFromMapDialog { get; set; } = null!;
	private TemplateManagerDialog _templateManagerDialog { get; set; } = null!;
	private InstanceManagerDialog _instanceManagerDialog { get; set; } = null!;
	private AssetBrowserDialog _assetBrowserDialog { get; set; } = null!;
	private NoiseTextureDialog _noiseTextureDialog { get; set; } = null!;
	private ConvertGlbDialog _convertGlbDialog { get; set; } = null!;
	private EditorSettingsDialog _editorSettingsDialog { get; set; } = null!;
	private ShaderEditorDialog _shaderEditorDialog { get; set; } = null!;
	private VfxStudioDialog _vfxStudioDialog { get; set; } = null!;
	private ProceduralAnimationStudioDialog _proceduralAnimationStudioDialog { get; set; } = null!;
	private AuthorSignatureDialog _authorSignatureDialog { get; set; } = null!;
	private string _publishAuthorshipKeyPath { get; set; } = string.Empty;
	private ReplaceTextureDialog _replaceTextureDialog { get; set; } = null!;
	private UtilitiesDialog _utilitiesDialog { get; set; } = null!;
	private TestSettingsDialog _testSettingsDialog { get; set; } = null!;
	private MapVersionsDialog _mapVersionsDialog { get; set; } = null!;
	private MapHistoryStoresDialog _mapHistoryStoresDialog { get; set; } = null!;
	private Button _btnEditorSettings { get; set; } = null!;
	private Button _btnMapVersions { get; set; } = null!;
	private PanelContainer _mapNameHeaderPanel { get; set; } = null!;
	private Label _lblMapNameHeader { get; set; } = null!;
	private double _mapNameUpdateTimer { get; set; } = 0.0;
	private Button _btnOpenGlobalOverrides { get; set; } = null!;
	private Button BtnEditVfx { get; set; } = null!;
	private Button BtnAssetsManager { get; set; } = null!;
	private Button BtnTemplateManager { get; set; } = null!;
	private Button BtnInstanceManager { get; set; } = null!;
	private Button BtnImportFromMap { get; set; } = null!;
	private Button BtnUtilities { get; set; } = null!;
	private bool IsUpdatingInspectorUI { get; set; }

	private CheckBox? ChkApplyGroundTexture { get; set; }
	private CheckBox? ChkApplyCliffTexture { get; set; }
	private HBoxContainer RowGroundTexture { get; set; } = null!;
	private HBoxContainer RowCliffTexture { get; set; } = null!;

	private Slider _sldPlacementRotate { get; set; } = null!;
	private Label _lblPlacementRotateValue { get; set; } = null!;
	private Slider _sldPlacementScale { get; set; } = null!;
	private Label _lblPlacementScaleValue { get; set; } = null!;
	private VBoxContainer _placementRotateBox { get; set; } = null!;
	private VBoxContainer _placementScaleBox { get; set; } = null!;
	private Button _btnCopy { get; set; } = null!;
	private Button _btnPaste { get; set; } = null!;
	private Control _stepBox { get; set; } = null!;
	private Button _btnToggleSnap { get; set; } = null!;
	private Button _btnToggleGrid { get; set; } = null!;
	private PopupMenu _popupOverlayMode { get; set; } = null!;
	private Button _btnToggleEnvironment { get; set; } = null!;
	private PopupPanel _popupEnvironment { get; set; } = null!;
	private OptionButton _optEnvLighting { get; set; } = null!;
	private OptionButton _optEnvWeather { get; set; } = null!;
	private CheckBox _chkEnvShadows { get; set; } = null!;
	private Button _btnBrushShape { get; set; } = null!;
	private Button _btnResetMap { get; set; } = null!;
	private Button _btnSaveMore { get; set; } = null!;
	private PopupPanel _popupSaveMore { get; set; } = null!;
	private Button _btnEyedropper { get; set; } = null!;
	private OptionButton _optEyedropperMode { get; set; } = null!;
	private Button _btnNoise { get; set; } = null!;
	private Button _btnWater { get; set; } = null!;
	private PanelContainer _minimapFrame { get; set; } = null!;
	private Control _minimapArea { get; set; } = null!;
	private MapEditorCameraIndicator _cameraIndicator { get; set; } = null!;


	private Button _btnToggleCamera { get; set; } = null!;
	private PopupPanel _popupCamera { get; set; } = null!;
	private CheckBox _chkFreeCamera { get; set; } = null!;


	private Button _btnRaise { get; set; } = null!;
	private Button _btnLower { get; set; } = null!;
	private Button _btnHeight { get; set; } = null!;
	private Button _btnSmooth { get; set; } = null!;
	private Button _btnPlateau { get; set; } = null!;
	private Button _btnRamp { get; set; } = null!;
	private OptionButton _optMirrorMode { get; set; } = null!;
	private OptionButton _optPlacementMirrorMode { get; set; } = null!;
	private HSlider _sldClumpDensity { get; set; } = null!;
	private Label _lblClumpDensityValue { get; set; } = null!;
	private HSlider _sldClumpScaleVar { get; set; } = null!;
	private Label _lblClumpScaleVarValue { get; set; } = null!;

	private Button _btnTextureBrush { get; set; } = null!;

	private Button _btnFloodFill { get; set; } = null!;
	private Button _btnSelectArea { get; set; } = null!;
	private Button _btnSelectMove { get; set; } = null!;
	private PanelContainer _inspectorPanel { get; set; } = null!;
	private Label _lblInspectorTitle { get; set; } = null!;
	private Label _lblInspectorPos { get; set; } = null!;
	private Button _btnInspectorRotLeft { get; set; } = null!;
	private Button _btnInspectorRotRight { get; set; } = null!;
	private Button _btnInspectorScaleDown { get; set; } = null!;
	private Button _btnInspectorScaleUp { get; set; } = null!;
	private Button _btnInspectorScaleReset { get; set; } = null!;
	private Button _btnInspectorDelete { get; set; } = null!;
	private Button _btnShowCoverage { get; set; } = null!;
	private HBoxContainer _playerOwnerContainer { get; set; } = null!;
	private OptionButton _optPlayerOwner { get; set; } = null!;
	private HBoxContainer _placementPlayerContainer { get; set; } = null!;
	private OptionButton _optPlacementPlayer { get; set; } = null!;
	private PanelContainer _rigStatusContainer { get; set; } = null!;
	private Label _lblRigStatus { get; set; } = null!;

	private Button _btnPathingBrush { get; set; } = null!;
	private Button _btnFloodFillPathing { get; set; } = null!;
	private CheckBox _chkShallowWater { get; set; } = null!;
	private CheckBox _chkDeepWater { get; set; } = null!;
	private CheckBox _chkFlying { get; set; } = null!;
	private CheckBox _chkGround { get; set; } = null!;
	private CheckBox _chkBuildable { get; set; } = null!;

	private OptionButton _optPathingMode { get; set; } = null!;


	private Button _btnDrawCoordinate { get; set; } = null!;
	private LineEdit? _txtCoordinateName { get; set; }
	private Button _btnCommitCoordinate { get; set; } = null!;
	private Button _btnViewCoordinates { get; set; } = null!;


	private Button? _activeToolButton { get; set; }
	private StyleBoxFlat _highlightStyle { get; set; } = null!;

	private Label _lblInfoText { get; set; } = null!;
	private Label _lblTerrainTexture { get; set; } = null!;
	private Label _lblCliffTexture { get; set; } = null!;

	private PanelContainer? _scaleMapDialog { get; set; }
	private Label? _lblScalePreviewWidth { get; set; }
	private Label? _lblScalePreviewHeight { get; set; }

	private int _scaleDialogTargetDepth { get; set; }

	private Camera3D? _camera3D { get; set; }
	private Button _btnEditors { get; set; } = null!;
	private Button _btnVSCode { get; set; } = null!;
	private PopupPanel _popupEditors { get; set; } = null!;
	private bool _isDraggingSlider { get; set; } = false;
	private Panel _swatchHighlightPanel { get; set; } = null!;
	private Panel _swatchCliffHighlightPanel { get; set; } = null!;

	private MapEditorTopBar _topBarController { get; set; } = null!;
	private MapEditorBrushSettings _brushSettingsController { get; set; } = null!;
	private MapEditorPlacementSettings _placementSettingsController { get; set; } = null!;
	private MapEditorInspector _inspectorController { get; set; } = null!;
	private MapEditorPathingPanel _pathingPanelController { get; set; } = null!;
	private MapEditorMinimap _minimapController { get; set; } = null!;
	private MapEditorEntityPaletteController _entityPaletteController { get; set; } = null!;

	private bool _wasmHasErrors { get; set; } = false;
	private string _wasmCompileLogPath { get; set; } = "";


	private string GetAuthorshipKeysDirectory()
	{
		return ProjectSettings.GlobalizePath("user://appdata/keys/");
	}

	private bool EnsureSignedInForPublishing()
	{
		if (GameContext.Account.IsSignedIn) return true;

		ShowFeedback(TranslationServer.Translate("Sign in to publish maps. Offline play and editing still work without an account."));
		return false;
	}

	private bool EnsureAuthorshipKeyForPublishing()
	{
		string keysDirectory = GetAuthorshipKeysDirectory();
		var keys = AuthorshipKeyHelper.EnumerateAuthorshipKeys(keysDirectory);
		string accountUserName = GameContext.Account.DisplayName;

		foreach (AuthorshipKeyInfo keyInfo in keys)
		{
			if (string.Equals(keyInfo.UserName, accountUserName, StringComparison.OrdinalIgnoreCase))
			{
				_publishAuthorshipKeyPath = keyInfo.Path;
				return false;
			}
		}

		if (keys.Count > 0)
		{
			_publishAuthorshipKeyPath = keys[0].Path;
			return false;
		}

		if (string.IsNullOrWhiteSpace(accountUserName))
		{
			ShowFeedback(TranslationServer.Translate("Your account has no user name, so no authorship key can be created."));
			return false;
		}

		var (_, _, keyPath, createdNew) = AuthorshipKeyHelper.GetOrGenerateKeyInfo(keysDirectory, accountUserName);
		_publishAuthorshipKeyPath = keyPath;

		if (createdNew)
		{
			ShowFeedback(string.Format(TranslationServer.Translate("A new authorship key has been generated at {0}. Please back up this file to keep ownership of your maps."), keyPath));
		}

		return createdNew;
	}

	private string GetSelectedAuthorshipKeyPath()
	{
		if (!string.IsNullOrEmpty(_publishAuthorshipKeyPath) && File.Exists(_publishAuthorshipKeyPath)) return _publishAuthorshipKeyPath;

		var keys = AuthorshipKeyHelper.EnumerateAuthorshipKeys(GetAuthorshipKeysDirectory());
		return keys.Count > 0 ? keys[0].Path : string.Empty;
	}

	private bool TryGetSelectedAuthorshipKey(out NSec.Cryptography.Key? key, out Realm.Shared.Metadata.AuthorshipKeyData? data, out string keyPath)
	{
		key = null;
		data = null;
		keyPath = GetSelectedAuthorshipKeyPath();

		return keyPath.Length > 0 && AuthorshipKeyHelper.TryLoadAuthorshipKey(keyPath, out key, out data);
	}

	private string GetPublishAuthorName()
	{
		if (AuthorshipKeyHelper.TryReadKeyFile(GetSelectedAuthorshipKeyPath(), out Realm.Shared.Metadata.AuthorshipKeyData? authorsKeyData) && authorsKeyData != null)
		{
			return authorsKeyData.UserName;
		}

		string accountUserName = GameContext.Account.DisplayName;
		return string.IsNullOrWhiteSpace(accountUserName) ? "MapAuthor" : accountUserName;
	}

	public void PublishMapActionExternal()
	{
		if (!EnsureSignedInForPublishing()) return;

		EnsureMapHasRealName(_ => OpenPublishMapPanel());
	}

	private void OpenPublishMapPanel()
	{
		string wsPath = !string.IsNullOrEmpty(GameContext.EditorService._editingDirectory) ? GameContext.EditorService._editingDirectory : MapWorkspaceService.GetActiveWorkspacePath();

		if (!TryValidateMapPublishRequirements(wsPath)) return;

		bool createdAuthorshipKey = EnsureAuthorshipKeyForPublishing();

		var overlay = new ColorRect { Name = "PublishInstructionsOverlay", Color = new Color(0, 0, 0, 0.7f) };
		overlay.SetAnchorsPreset(LayoutPreset.FullRect);
		AddChild(overlay);

		var center = UIStyle.CreateCenteredOverlay();
		overlay.AddChild(center);

		var panel = CreatePublishMapPanel();
		center.AddChild(panel);

		var vbox = BuildPublishMapUI(panel);

		var title = new Label { Text = TranslationServer.Translate("Publish Map Instructions"), HorizontalAlignment = HorizontalAlignment.Center };
		title.AddThemeFontSizeOverride("font_size", 24);
		vbox.AddChild(title);

		var optType = new OptionButton();
		optType.Name = "OptType_OptionButton";
		optType.AddItem(TranslationServer.Translate("Custom Arcade Map"), 0);
		optType.AddItem(TranslationServer.Translate("Reusable Asset Pack"), 1);
		optType.Selected = _mapSettingsDialog?.SelectedMapTypeIndex ?? 0;
		vbox.AddChild(optType);

		var scroll = new ScrollContainer { SizeFlagsVertical = SizeFlags.ExpandFill };
		var instructionsText = new RichTextLabel { SizeFlagsHorizontal = SizeFlags.ExpandFill, SizeFlagsVertical = SizeFlags.ExpandFill, BbcodeEnabled = true };
		scroll.AddChild(instructionsText);
		vbox.AddChild(scroll);

		BindPublishMapInstructions(optType, instructionsText);

		CreatePublishMapOptimizationTip(wsPath, vbox);

		var chkFullExport = new CheckBox { Text = TranslationServer.Translate("Full Export (Include all physical assets in package, bypassing greenlit deduplication)"), ButtonPressed = false };
		chkFullExport.AddThemeConstantOverride("icon_max_width", 0);
		UIStyle.ApplyCheckboxStyle(chkFullExport);
		vbox.AddChild(chkFullExport);

		CreatePublishMapIdentityPanel(vbox);

		AddPublishMapActionButtons(vbox, overlay, chkFullExport);

		if (createdAuthorshipKey && TryGetSelectedAuthorshipKey(out NSec.Cryptography.Key? createdPublishKey, out _, out _) && createdPublishKey != null)
		{
			CheckCreatorRegistrationAndPrompt(createdPublishKey);
		}
	}

	private bool TryValidateMapPublishRequirements(string wsPath)
	{
		var (assetsValid, missingAssets) = MapAssetHelper.ValidateWorkspaceAssets(wsPath);
		if (!assetsValid)
		{
			ShowFeedback(string.Format(TranslationServer.Translate("Publish failed: Map is missing required asset files:\n{0}"), string.Join(", ", missingAssets.Take(4)) + (missingAssets.Count > 4 ? "..." : "")));
			AppendWasmConsoleLog($"[ERROR] Publish failed. Missing required asset files:\n  {string.Join("\n  ", missingAssets)}");
			return false;
		}

		if (!CheckAssetSizeValidation(wsPath))
		{
			return false;
		}

		return true;
	}

	private bool CheckAssetSizeValidation(string workspace)
	{
		var (sizesValid, oversizedAssets) = MapAssetHelper.ValidateWorkspaceAssetSizes(workspace);
		if (!sizesValid)
		{
			string oversizedSummary = string.Join("\n", oversizedAssets.Take(4).Select(o => $"• {o.RelativePath} ({o.SizeMB:F2} MB > 15 MB)")) + (oversizedAssets.Count > 4 ? "\n..." : "");
			ShowFeedback(string.Format(TranslationServer.Translate("Publish failed: Assets exceed maximum 15 MB size limit:\n{0}"), oversizedSummary));
			AppendWasmConsoleLog($"[ERROR] Publish failed. Assets exceed maximum 15 MB size limit:\n{string.Join("\n", oversizedAssets.Select(o => $"  {o.RelativePath} ({o.SizeMB:F2} MB)"))}");
			return false;
		}
		return true;
	}

	private PanelContainer CreatePublishMapPanel()
	{
		var panel = new PanelContainer { CustomMinimumSize = new Vector2(1200, 800) };
		var style = new StyleBoxFlat
		{
			BgColor = new Color(0.1f, 0.1f, 0.15f, 0.95f),
			BorderWidthTop = 2,
			BorderWidthBottom = 2,
			BorderWidthLeft = 2,
			BorderWidthRight = 2,
			BorderColor = new Color(0.3f, 0.3f, 0.35f, 1f),
			CornerRadiusTopLeft = 4,
			CornerRadiusTopRight = 4,
			CornerRadiusBottomLeft = 4,
			CornerRadiusBottomRight = 4
		};
		panel.AddThemeStyleboxOverride("panel", style);
		return panel;
	}

	private VBoxContainer BuildPublishMapUI(PanelContainer panel)
	{
		var vbox = new VBoxContainer { CustomMinimumSize = new Vector2(1180, 780) };
		vbox.AddThemeConstantOverride("separation", 12);
		vbox.SetAnchorsPreset(LayoutPreset.FullRect);

		var margin = new MarginContainer();
		margin.Name = "Margin_MarginContainer";
		margin.AddThemeConstantOverride("margin_top", 10);
		margin.AddThemeConstantOverride("margin_bottom", 10);
		margin.AddThemeConstantOverride("margin_left", 10);
		margin.AddThemeConstantOverride("margin_right", 10);
		margin.AddChild(vbox);
		panel.AddChild(margin);

		return vbox;
	}

	private void BindPublishMapInstructions(OptionButton optType, RichTextLabel instructionsText)
	{
		string textArcade = TranslationServer.Translate("🚀 [b]Publishing Your Custom Map[/b]\nTo maintain a high-quality community arcade, all new maps start in a Beta-Testing Phase.\n\nOnce your map hits our community play-time metrics (gaining enough unique players, ratings, & community playtime), it will become available for official publishing to community maps screens.\n\nUntil then, it is up to you to share it with the community & market it until you hit that threshold.\nYour map is ready to play right now! Click the 'Export' button to share it. Host a lobby with your map & wait for players to join.\nMessage the community via discord channels, etc to explain your map & convince them to try it. If they enjoy it, they will probably re-host it, which will help you hit the graduation threshold more quickly.\n\nWhile in testing, your map name will include a prefix [Beta-Testing] so players know it's an active work-in-progress. However, you should still do as much personal testing as possible before public hosting to avoid a frustrating experience for your testers.\n\nAfter graduation, your map name will be permanently reserved to your creator profile so no one else can use that same name.");
		string textAssetPack = TranslationServer.Translate("📦 [b]Publishing a Reusable Asset Pack[/b]\nWant to share your custom 3D models, audio, or code scripts with other map makers?\n\nIn Realm, Asset Packs are published as playable Showcase/Demo Maps.\n\n[b]Build a Playground:[/b] Turn your asset pack into a map where players can preview the functionality provided by your systems, view your models, etc.\n\n[b]Gather Community Metrics:[/b] Just like a regular map, your asset pack will start in a \"beta\" phase before being promoted on community discovery pages. Read the \"Custom Arcade Map\" section for more information.\n\n[b]Easy Importing:[/b] Once a player has a copy of your map, they can import assets from it into their maps via the map editor.\n\n[b]Automatic Credit:[/b] When creators import from you, the system tracks your files' signatures and automatically adds your info to their map credits.");

		Action updateText = () => { instructionsText.Text = optType.Selected == 0 ? textArcade : textAssetPack; };
		updateText();
		optType.ItemSelected += (_) => updateText();
	}

	private void CreatePublishMapOptimizationTip(string wsPath, VBoxContainer vbox)
	{
		var (unprunedCount, unnormalizedPotentialCount, potentialSavedBytes) = MapNormalizationHelper.CheckOptimizationState(wsPath);
		if (unprunedCount <= 0 && unnormalizedPotentialCount <= 0) return;

		var tipPanel = new PanelContainer();
		tipPanel.Name = "TipPanel_PanelContainer";
		var tipStyle = new StyleBoxFlat
		{
			BgColor = new Color(0.2f, 0.17f, 0.1f, 0.95f),
			BorderColor = UIStyle.ColorGoldDull,
			BorderWidthBottom = 1,
			BorderWidthTop = 1,
			BorderWidthLeft = 1,
			BorderWidthRight = 1,
			CornerRadiusTopLeft = 4,
			CornerRadiusTopRight = 4,
			CornerRadiusBottomLeft = 4,
			CornerRadiusBottomRight = 4
		};
		tipPanel.AddThemeStyleboxOverride("panel", tipStyle);

		var tipMargin = new MarginContainer();
		tipMargin.Name = "TipMargin_MarginContainer";
		tipMargin.AddThemeConstantOverride("margin_top", 6);
		tipMargin.AddThemeConstantOverride("margin_bottom", 6);
		tipMargin.AddThemeConstantOverride("margin_left", 10);
		tipMargin.AddThemeConstantOverride("margin_right", 10);
		tipPanel.AddChild(tipMargin);

		var tipLabel = new Label { Text = "💡 " + string.Format(TranslationServer.Translate("Optimization Advisory: We recommend running 'Prune Unused Assets' and 'Normalize References' in the Map Utilities dialog before publishing to remove unused files and reference shared greenlit assets (potential savings: {0})."), MapStorageService.FormatBytes(potentialSavedBytes)), AutowrapMode = TextServer.AutowrapMode.WordSmart, CustomMinimumSize = new Vector2(1160, 0), SizeFlagsHorizontal = SizeFlags.ExpandFill };
		tipLabel.AddThemeFontSizeOverride("font_size", 12);
		tipLabel.AddThemeColorOverride("font_color", UIStyle.ColorGold);
		tipMargin.AddChild(tipLabel);
		vbox.AddChild(tipPanel);
	}

	private void CreatePublishMapIdentityPanel(VBoxContainer vbox)
	{
		var identityPanel = new PanelContainer();
		identityPanel.Name = "IdentityPanel_PanelContainer";
		var identityStyle = new StyleBoxFlat
		{
			BgColor = new Color(0.12f, 0.15f, 0.22f, 0.95f),
			BorderColor = new Color(0.3f, 0.5f, 0.8f, 0.9f),
			BorderWidthBottom = 1,
			BorderWidthTop = 1,
			BorderWidthLeft = 1,
			BorderWidthRight = 1,
			CornerRadiusTopLeft = 4,
			CornerRadiusTopRight = 4,
			CornerRadiusBottomLeft = 4,
			CornerRadiusBottomRight = 4
		};
		identityPanel.AddThemeStyleboxOverride("panel", identityStyle);
		var idMargin = new MarginContainer();
		idMargin.Name = "IdMargin_MarginContainer";
		idMargin.AddThemeConstantOverride("margin_top", 8);
		idMargin.AddThemeConstantOverride("margin_bottom", 8);
		idMargin.AddThemeConstantOverride("margin_left", 12);
		idMargin.AddThemeConstantOverride("margin_right", 12);
		identityPanel.AddChild(idMargin);

		var idVBox = new VBoxContainer();
		idVBox.Name = "IdVBox_VBoxContainer";
		idVBox.AddThemeConstantOverride("separation", 6);
		idMargin.AddChild(idVBox);

		var idHeaderHBox = new HBoxContainer();
		idHeaderHBox.Name = "IdHeaderHBox_HBoxContainer";
		idHeaderHBox.AddThemeConstantOverride("separation", 8);
		var idHeaderLabel = new Label { Text = "🔑 " + TranslationServer.Translate("AUTHOR IDENTITY & KEY VERIFICATION"), SizeFlagsHorizontal = SizeFlags.ExpandFill };
		idHeaderLabel.AddThemeFontSizeOverride("font_size", 13);
		idHeaderLabel.AddThemeColorOverride("font_color", UIStyle.ColorGold);
		idHeaderHBox.AddChild(idHeaderLabel);
		idVBox.AddChild(idHeaderHBox);

		var keyRow = new HBoxContainer();
		keyRow.Name = "KeyRow_HBoxContainer";
		keyRow.AddThemeConstantOverride("separation", 8);

		var lblKeyCaption = new Label { Text = TranslationServer.Translate("Publishing Key:") };
		lblKeyCaption.AddThemeFontSizeOverride("font_size", 11);
		lblKeyCaption.AddThemeColorOverride("font_color", UIStyle.ColorGold);
		keyRow.AddChild(lblKeyCaption);

		var optKeys = new OptionButton { Name = "PublishKeyOption_OptionButton", CustomMinimumSize = new Vector2(320, 26), SizeFlagsHorizontal = SizeFlags.ExpandFill };
		optKeys.AddThemeFontSizeOverride("font_size", 11);
		optKeys.AddThemeConstantOverride("icon_max_width", 0);
		keyRow.AddChild(optKeys);

		Font? faFont = GetFontAwesomeFont();

		var btnViewKey = new Button { Name = "ViewKey_Button", Text = Realm.Client.Utils.UnicodeIcons.EYE, TooltipText = TranslationServer.Translate("Open the selected key"), CustomMinimumSize = new Vector2(34, 26) };
		btnViewKey.AddThemeConstantOverride("icon_max_width", 0);
		if (faFont != null) btnViewKey.AddThemeFontOverride("font", faFont);
		keyRow.AddChild(btnViewKey);

		var btnAddKey = new Button { Name = "AddKey_Button", Text = Realm.Client.Utils.UnicodeIcons.PLUS, TooltipText = TranslationServer.Translate("Create a new authorship key"), CustomMinimumSize = new Vector2(34, 26) };
		btnAddKey.AddThemeConstantOverride("icon_max_width", 0);
		if (faFont != null) btnAddKey.AddThemeFontOverride("font", faFont);
		keyRow.AddChild(btnAddKey);

		idVBox.AddChild(keyRow);

		var idDetailsLabel = new Label { Name = "IdDetailsLabel_Label", Text = string.Empty, AutowrapMode = TextServer.AutowrapMode.WordSmart, CustomMinimumSize = new Vector2(1156, 0), SizeFlagsHorizontal = SizeFlags.ExpandFill };
		idDetailsLabel.AddThemeFontSizeOverride("font_size", 11);
		idDetailsLabel.AddThemeColorOverride("font_color", new Color(0.85f, 0.9f, 1.0f));
		idVBox.AddChild(idDetailsLabel);

		string keysDirectory = GetAuthorshipKeysDirectory();

		void RefreshSelectedKeyDetails()
		{
			if (AuthorshipKeyHelper.TryReadKeyFile(_publishAuthorshipKeyPath, out Realm.Shared.Metadata.AuthorshipKeyData? selectedKeyData) && selectedKeyData != null)
			{
				string shortPubKey = selectedKeyData.PublicKey.Length > 16 ? $"{selectedKeyData.PublicKey[..8]}...{selectedKeyData.PublicKey[^8..]}" : selectedKeyData.PublicKey;
				idDetailsLabel.Text = string.Format(TranslationServer.Translate("Author: {0} | Public Key: {1}\nKey File: {2}"), selectedKeyData.UserName, shortPubKey, _publishAuthorshipKeyPath);
				return;
			}

			idDetailsLabel.Text = TranslationServer.Translate("No authorship key is selected.");
		}

		void RefreshKeyOptions()
		{
			var keys = AuthorshipKeyHelper.EnumerateAuthorshipKeys(keysDirectory);
			optKeys.Clear();

			int selectedIndex = 0;
			for (int index = 0; index < keys.Count; index++)
			{
				AuthorshipKeyInfo keyInfo = keys[index];
				optKeys.AddItem($"{keyInfo.UserName} — {Path.GetFileName(keyInfo.Path)}", index);
				if (string.Equals(keyInfo.Path, _publishAuthorshipKeyPath, StringComparison.OrdinalIgnoreCase)) selectedIndex = index;
			}

			if (keys.Count == 0) return;

			_publishAuthorshipKeyPath = keys[selectedIndex].Path;
			optKeys.Selected = selectedIndex;
		}

		optKeys.ItemSelected += selectedIndex =>
		{
			var keys = AuthorshipKeyHelper.EnumerateAuthorshipKeys(keysDirectory);
			if (selectedIndex < 0 || selectedIndex >= keys.Count) return;

			_publishAuthorshipKeyPath = keys[(int)selectedIndex].Path;
			RefreshSelectedKeyDetails();
		};

		btnViewKey.Pressed += () =>
		{
			GameContext.UserInterface?.PlayClickSound();
			_authorSignatureDialog ??= new AuthorSignatureDialog(this);
			_authorSignatureDialog.OpenForKey(_publishAuthorshipKeyPath);
		};

		btnAddKey.Pressed += () =>
		{
			GameContext.UserInterface?.PlayClickSound();

			if (!GameContext.Account.IsSignedIn)
			{
				ShowFeedback(TranslationServer.Translate("Sign in to create an authorship key."));
				return;
			}

			string accountUserName = GameContext.Account.DisplayName;
			if (string.IsNullOrWhiteSpace(accountUserName))
			{
				ShowFeedback(TranslationServer.Translate("Your account has no user name, so no authorship key can be created."));
				return;
			}

			var (createdKey, _, createdKeyPath, createdNew) = AuthorshipKeyHelper.GetOrGenerateKeyInfo(keysDirectory, accountUserName);
			_publishAuthorshipKeyPath = createdKeyPath;
			RefreshKeyOptions();
			RefreshSelectedKeyDetails();

			ShowFeedback(createdNew
				? string.Format(TranslationServer.Translate("A new authorship key has been generated for {0} at {1}. Please back up this file to keep ownership of your maps."), accountUserName, createdKeyPath)
				: string.Format(TranslationServer.Translate("Using the existing authorship key for {0} at {1}."), accountUserName, createdKeyPath));

			if (createdNew && createdKey != null)
			{
				CheckCreatorRegistrationAndPrompt(createdKey);
			}
		};

		RefreshKeyOptions();
		RefreshSelectedKeyDetails();

		var idBackupWarn = new Label { Text = "⚠️ " + TranslationServer.Translate("Important: Back up your key file! Your authorship key proves ownership of your map name and allows future updates. If you lose your key file, you will permanently lose ownership and the ability to update this map."), AutowrapMode = TextServer.AutowrapMode.WordSmart, CustomMinimumSize = new Vector2(1156, 0), SizeFlagsHorizontal = SizeFlags.ExpandFill };
		idBackupWarn.AddThemeFontSizeOverride("font_size", 11);
		idBackupWarn.AddThemeColorOverride("font_color", new Color(1.0f, 0.75f, 0.35f));
		idVBox.AddChild(idBackupWarn);

		vbox.AddChild(identityPanel);
	}

	private void AddPublishMapActionButtons(VBoxContainer vbox, ColorRect overlay, CheckBox chkFullExport)
	{
		var hbox = new HBoxContainer { Alignment = BoxContainer.AlignmentMode.Center };
		hbox.AddThemeConstantOverride("separation", 20);
		vbox.AddChild(hbox);

		var btnPublish = new Button { Text = TranslationServer.Translate("Publish Map"), CustomMinimumSize = new Vector2(140, 40) };
		btnPublish.AddThemeConstantOverride("icon_max_width", 0);
		btnPublish.Pressed += () =>
		{
			bool isFullExport = chkFullExport.ButtonPressed;
			overlay.QueueFree();
			PublishMapActionView(isFullExport);
		};
		hbox.AddChild(btnPublish);

		var btnClose = new Button { Text = TranslationServer.Translate("Close"), CustomMinimumSize = new Vector2(120, 40) };
		btnClose.AddThemeConstantOverride("icon_max_width", 0);
		btnClose.Pressed += () => overlay.QueueFree();
		hbox.AddChild(btnClose);
	}


	public void TriggerToolSelection(EditorTool tool, Button? btn, string placeId = "")
	{
		if (GameContext.Host == null) return;

		HandleEditorToolStateClear(tool);
		UpdateActiveToolButton(btn);

		GameContext.Host.ActiveEditorTool = tool;
		GameContext.Host.ActivePlaceId = placeId;

		UpdateActiveEditorModuleForTool(tool);
		UpdatePanelVisibilityForToolPathing(tool);
		UpdateSidebarMorph(tool);
		ApplyBrushStrengthForTool(tool);
		UpdateToolStatusLabel(tool, placeId);
		UpdateTextureLabels();
	}

	private void HandleEditorToolStateClear(EditorTool tool)
	{
		var host = GameContext.Host;
		if (host == null) return;

		if (tool != EditorTool.SelectMove)
		{
			host.SelectedEditorObject = null;
		}

		if (tool != EditorTool.Ramp)
		{
			host.ClearRampStartPosExternal();
		}

		if (tool != EditorTool.Measure)
		{
			host.ClearMeasureVisuals();
			ClearMeasureTelemetry();
		}
	}

	private void UpdateActiveToolButton(Button? btn)
	{
		if (_activeToolButton != null)
		{
			_activeToolButton.RemoveThemeStyleboxOverride("normal");
			if (_activeToolButton.GetParent() is Control oldParent)
			{
				Label cardLbl = oldParent.GetNodeOrNull<Label>("ToolCardLabel");
				if (cardLbl != null) cardLbl.RemoveThemeColorOverride("font_color");
			}
		}

		_activeToolButton = btn;
		if (_activeToolButton != null)
		{
			if (_activeToolButton.Name.ToString().StartsWith("Swatch"))
			{
				HighlightSwatch(_activeToolButton);
			}
			else
			{
				_activeToolButton.AddThemeStyleboxOverride("normal", _highlightStyle);
				if (_activeToolButton.GetParent() is Control newParent)
				{
					Label cardLbl = newParent.GetNodeOrNull<Label>("ToolCardLabel");
					if (cardLbl != null) cardLbl.AddThemeColorOverride("font_color", UIStyle.ColorGold);
				}

				HighlightSwatch(null);
			}
		}
		else
		{
			HighlightSwatch(null);
		}
	}

	private void UpdateActiveEditorModuleForTool(EditorTool tool)
	{
		EditorModule targetModule = DetermineModuleForTool(tool);

		if (targetModule != _activeModule)
		{
			_activeModule = targetModule;
			UpdateModuleSwitchButtons();
			UpdatePanelVisibilityForModule(targetModule);
		}
	}

	private EditorModule DetermineModuleForTool(EditorTool tool)
	{
		return tool switch
		{
			EditorTool.Raise or EditorTool.Lower or EditorTool.Smooth or EditorTool.Plateau or EditorTool.Ramp or EditorTool.Noise or EditorTool.Water => EditorModule.Terrain,
			EditorTool.PaintTexture or EditorTool.FloodFill => EditorModule.TextureDeco,
			EditorTool.DrawCoordinate => EditorModule.Coordinates,
			EditorTool.PaintPathing or EditorTool.FloodFillPathing => EditorModule.Pathing,
			EditorTool.PlaceUnit or EditorTool.PlaceProp or EditorTool.PlacePropClump or EditorTool.PlaceDecal or EditorTool.DeleteObject or EditorTool.SelectMove => EditorModule.Objects,
			EditorTool.SelectArea or EditorTool.PasteArea => EditorModule.Clipboard,
			_ => _activeModule
		};
	}

	private bool NeedsPathingOverlay(EditorTool tool)
	{
		return tool is EditorTool.PaintPathing or EditorTool.FloodFillPathing or EditorTool.DrawCoordinate or EditorTool.SelectArea or EditorTool.PasteArea;
	}

	private void UpdatePanelVisibilityForToolPathing(EditorTool tool)
	{
		if (tool == EditorTool.DrawCoordinate && _btnCommitCoordinate != null)
		{
			_btnCommitCoordinate.Visible = false;
		}

		if (NeedsPathingOverlay(tool))
		{
			GameContext.Host?.UpdatePathingOverlay();
		}
	}

	private void ApplyBrushStrengthForTool(EditorTool tool)
	{
		if (_sldBrushStrength == null || GameContext.Host == null) return;

		if (tool == EditorTool.PaintTexture)
		{
			_sldBrushStrength.MinValue = 0.0;
			_sldBrushStrength.MaxValue = 10.0;
			_sldBrushStrength.Step = 1.0;
			_sldBrushStrength.Value = GameContext.SessionState.SavedTextureIntensity;
			if (_lblBrushStrengthValue != null) _lblBrushStrengthValue.Text = GameContext.SessionState.SavedTextureIntensity.ToString("F0");
			GameContext.Host.EditorBrushStrength = GameContext.SessionState.SavedTextureIntensity;
		}
		else
		{
			_sldBrushStrength.MinValue = 0.5;
			_sldBrushStrength.MaxValue = 10.0;
			_sldBrushStrength.Step = 0.5;
			float restoredStrength = Math.Max(0.5f, GameContext.SessionState.SavedBrushStrength);
			_sldBrushStrength.Value = restoredStrength;
			if (_lblBrushStrengthValue != null) _lblBrushStrengthValue.Text = restoredStrength.ToString("F1");
			GameContext.Host.EditorBrushStrength = restoredStrength;
		}
	}

	private void UpdateToolStatusLabel(EditorTool tool, string placeId)
	{
		string shortPlaceName = !string.IsNullOrEmpty(placeId) ? Path.GetFileName(placeId).ToUpper() : "";
		UpdateActiveToolLabel(tool, shortPlaceName);

		if (_lblInfoText == null) return;
		UpdateToolInfoText(tool, shortPlaceName);
	}

	private void UpdateActiveToolLabel(EditorTool tool, string shortPlaceName)
	{
		if (_statusLabel == null) return;

		string toolName = Realm.Client.Services.EditorToolLabels.GetLocalizedToolName(tool);
		if (!string.IsNullOrEmpty(shortPlaceName)) toolName += $" ({shortPlaceName})";

		_statusLabel.Text = string.Format(TranslationServer.Translate("ACTIVE TOOL: {0}"), toolName);
	}

	private void UpdateToolInfoText(EditorTool tool, string shortPlaceName)
	{
		if (tool == EditorTool.PlaceUnit)
		{
			string alignment = PlayerColorConfig.GetPaletteLabel(GameContext.EditorService.Tools.PlaceUnitPlayerIndex);
			_lblInfoText.Text = string.Format(TranslationServer.Translate("TOOL: Place Unit\n\nLeft-click on the ground to spawn a {0} aligned with {1}."), shortPlaceName, alignment);
			return;
		}

		if (tool == EditorTool.PlaceProp)
		{
			_lblInfoText.Text = string.Format(TranslationServer.Translate("TOOL: Place Prop\n\nLeft-click on the ground to spawn static decorative object: {0}."), shortPlaceName);
			return;
		}

		if (tool == EditorTool.PlacePropClump)
		{
			_lblInfoText.Text = string.Format(TranslationServer.Translate("TOOL: Clump Brush\n\nDrag left click on the ground to paint clumps of static props: {0} based on Density and Scale Variation settings. Uses texture brush shape (Circle/Square)."), shortPlaceName);
			return;
		}

		if (EditorToolCatalog.ToolInfoTexts.TryGetValue(tool, out var rawText))
		{
			_lblInfoText.Text = TranslationServer.Translate(rawText);
		}
	}


	private ColorRect? _helpOverlayPanel { get; set; }


	private void UpdatePanelVisibilityForModule(EditorModule module)
	{
		SetAccordionVisibilities(module);
		SetModuleVBoxVisibilities(module);
		SetSettingsVisibilities(module);
		SetContentHeaders(module);

		_accordionContainer?.QueueSort();
		RefreshCardScrollStates();
	}

	private bool IsBrushAccordionVisible(EditorModule module) => module is EditorModule.Terrain or EditorModule.TextureDeco or EditorModule.Pathing;
	private bool IsToolSettingsAccordionVisible(EditorModule module) => module is EditorModule.Terrain or EditorModule.TextureDeco or EditorModule.Pathing or EditorModule.Objects or EditorModule.Clipboard;
	private bool IsPlacementAccordionVisible(EditorModule module) => module is EditorModule.Objects or EditorModule.Clipboard;

	private bool IsInspectorAccordionVisible(EditorModule module)
	{
		if (module == EditorModule.Objects) return true;
		var host = GameContext.Host;
		bool hasSelectedObject = host != null && GodotObject.IsInstanceValid(host.SelectedEditorObject);
		return hasSelectedObject && host?.ActiveEditorTool == EditorTool.SelectMove;
	}

	private void SetAccordionVisibilities(EditorModule module)
	{
		_accordionFile ??= GetNodeOrNull<VBoxContainer>("LeftSlidePanel/LeftScroll/LeftVBox/FileAccordion");
		_accordionViewport ??= GetNodeOrNull<VBoxContainer>("LeftSlidePanel/LeftScroll/LeftVBox/ViewportAccordion");

		if (_accordionFile != null) _accordionFile.Visible = true;
		if (_accordionViewport != null) _accordionViewport.Visible = true;
		if (_accordionTool != null) _accordionTool.Visible = true;

		if (_accordionBrush != null) _accordionBrush.Visible = IsBrushAccordionVisible(module);
		if (_accordionToolSettings != null) _accordionToolSettings.Visible = IsToolSettingsAccordionVisible(module);
		if (_accordionPlacement != null) _accordionPlacement.Visible = IsPlacementAccordionVisible(module);
		if (_accordionInspector != null) _accordionInspector.Visible = IsInspectorAccordionVisible(module);
	}

	private void SetModuleVBoxVisibilities(EditorModule module)
	{
		if (_panelTerrainVBox != null) _panelTerrainVBox.Visible = (module == EditorModule.Terrain);
		if (_panelDecoVBox != null) _panelDecoVBox.Visible = (module == EditorModule.TextureDeco);
		if (_panelPathingVBox != null) _panelPathingVBox.Visible = (module == EditorModule.Pathing);
		if (_panelCoordinatesVBox != null) _panelCoordinatesVBox.Visible = (module == EditorModule.Coordinates);
		if (_panelObjects != null) _panelObjects.Visible = (module == EditorModule.Objects);
		if (_panelClipboard != null) _panelClipboard.Visible = (module == EditorModule.Clipboard);
	}

	private void SetSettingsVisibilities(EditorModule module)
	{
		if (_containerTextureSettings != null) _containerTextureSettings.Visible = (module == EditorModule.Terrain || module == EditorModule.TextureDeco);
		if (_containerPathingSettings != null) _containerPathingSettings.Visible = (module == EditorModule.Pathing);
		if (_containerPasteSettings != null) _containerPasteSettings.Visible = (module == EditorModule.Clipboard);
		if (_containerCategorySelector != null) _containerCategorySelector.Visible = (module == EditorModule.Objects);
	}

	private void SetContentHeaders(EditorModule module)
	{
		SetHeaderContent(_contentTool, _btnHeaderTool, true, "Tool");

		bool showBrush = module is EditorModule.Terrain or EditorModule.TextureDeco or EditorModule.Pathing;
		SetHeaderContent(_contentBrush, _btnHeaderBrush, showBrush, "Global Brush Properties");

		bool showToolSettings = module is EditorModule.Terrain or EditorModule.TextureDeco or EditorModule.Pathing or EditorModule.Objects or EditorModule.Clipboard;
		SetHeaderContent(_contentToolSettings, _btnHeaderToolSettings, showToolSettings, "Tool Settings");

		bool showPlacement = module is EditorModule.Objects or EditorModule.Clipboard;
		SetHeaderContent(_contentPlacement, _btnHeaderPlacement, showPlacement, "Placement Config");
	}

	private void SetHeaderContent(Control content, Button btn, bool isVisible, string translationKey)
	{
		if (content == null || !isVisible) return;
		content.Visible = true;
		if (btn != null) btn.Text = TranslationServer.Translate(translationKey).ToString().ToUpperInvariant() + "  ▼";
	}


	private void RefreshCardScrollStates()
	{
		UpdateCardScrollState(_contentFile, 300f);
		UpdateCardScrollState(_contentViewport, 0f, false);
		UpdateCardScrollState(_contentTool, 320f);
		UpdateCardScrollState(_contentBrush, 300f);
		UpdateCardScrollState(_contentToolSettings, 320f);
		UpdateCardScrollState(_contentPlacement, 320f);
		UpdateCardScrollState(_contentInspector, 300f);
	}

	private FontVariation? _faFontVariation { get; set; }

	private HashSet<ulong> _hookedSliderInstanceIds { get; set; } = new();


	public bool IsMouseOverUI(Vector2 mousePos)
	{
		if (IsMenuOrOverlayOpen(mousePos)) return true;
		if (IsDraggingOrHoveringCoreUI()) return true;
		if (IsPopupVisible()) return true;
		if (IsMouseOverOptionCards(mousePos)) return true;
		if (IsMouseOverOptionContents(mousePos)) return true;
		if (IsMouseOverToolbarOrOverlays(mousePos)) return true;

		return false;
	}

	private bool IsMenuOrOverlayOpen(Vector2 mousePos)
	{
		if (GameContext.IsSettingsMenuOpen) return true;
		if (_helpOverlayPanel != null && _helpOverlayPanel.IsVisibleInTree()) return true;
		if (GetNodeOrNull<Control>("ConfirmationOverlay") != null) return true;
		if (GetNodeOrNull<Control>("GenerationOverlay") != null) return true;
		if (GetNodeOrNull<Control>("AgreementOverlay") != null) return true;
		if (_scaleMapDialog != null && _scaleMapDialog.IsVisibleInTree()) return true;
		if (FloatingDialogBase.IsMouseOverAnyDialogOpen(mousePos)) return true;
		return false;
	}

	private bool IsDraggingOrHoveringCoreUI()
	{
		if (_is3DInteractionActive) return false;
		if (_minimapController?.IsDragging == true || _isDraggingSlider) return true;

		var hoveredControl = GetViewport().GuiGetHoveredControl();
		if (hoveredControl == null || hoveredControl == this) return false;

		if (hoveredControl == _panelLeft || hoveredControl == _panelRight) return false;

		string[] ignoredNames = { "LeftScroll", "RightScroll", "LeftVBox", "AccordionContainer", "FeedbackLabel", "MapEditorScreenFrame" };
		if (Array.IndexOf(ignoredNames, (string)hoveredControl.Name) >= 0) return false;

		return true;
	}


	private bool AnyWindowVisible(params Window[] windows)
	{
		foreach (var w in windows)
		{
			if (w?.Visible == true) return true;
		}

		return false;
	}

	private bool AnyOptionButtonPopupVisible(params OptionButton?[] buttons)
	{
		foreach (var b in buttons)
		{
			if (b?.GetPopup()?.Visible == true) return true;
		}

		return false;
	}

	private bool IsPanelPopupVisible()
	{
		return AnyWindowVisible(
			_popupOverlayMode,
			_popupEnvironment,
			_popupCamera,
			_popupEditors,
			_popupSaveMore,
			_popupLoadMore
		);
	}

	private bool IsPrimaryOptionPopupVisible()
	{
		return AnyOptionButtonPopupVisible(
			_optModule,
			_optTestSettings,
			_entityPaletteController?.OptCategoryItems,
			_optEyedropperMode,
			_optPathingMode,
			_optMirrorMode,
			_optClipboardMirrorMode
		);
	}

	public void OpenTestSettingsDialog()
	{
		_testSettingsDialog ??= new TestSettingsDialog(this);
		_testSettingsDialog.OpenDialog();
	}

	private bool IsSecondaryOptionPopupVisible()
	{
		return AnyOptionButtonPopupVisible(
			_optPlacementMirrorMode,
			_optPolarRingSpacing,
			_optPolarRadialStep,
			_optEnvLighting,
			_optEnvWeather
		);
	}

	private bool IsPopupVisible()
	{
		if (_popupRandomGen?.Visible == true) return true;
		if (IsPanelPopupVisible()) return true;
		if (IsPrimaryOptionPopupVisible()) return true;
		if (IsSecondaryOptionPopupVisible()) return true;

		return false;
	}

	private bool IsMouseOverOptionCards(Vector2 mousePos)
	{
		foreach (var kvp in _cardDragMap)
		{
			var card = kvp.Key;
			if (GodotObject.IsInstanceValid(card) && card.IsVisibleInTree())
			{
				if (card.GetGlobalRect().HasPoint(mousePos)) return true;
			}
		}

		return false;
	}

	private bool IsMouseOverOptionContents(Vector2 mousePos)
	{
		Control[] optionContents = new Control[]
		{
			_contentFile, _contentViewport, _contentTool,
			_contentBrush, _contentToolSettings, _contentPlacement, _contentInspector, _contentLightingTuning
		};
		foreach (var content in optionContents)
		{
			if (GodotObject.IsInstanceValid(content) && content.IsVisibleInTree())
			{
				if (content.GetGlobalRect().HasPoint(mousePos)) return true;
			}
		}

		return false;
	}

	private bool IsMouseOverToolbarOrOverlays(Vector2 mousePos)
	{
		Control?[] checkControls =
		{
			_btnLeftTab, _btnRightTab,
			GetNodeOrNull<Control>("TopLeftBox"),
			GetNodeOrNull<Control>("BottomCenterBar/TopBar"),
			_middleRightBox, _scaleMapDialog,
			GetNodeOrNull<Control>("GenerationOverlay")
		};

		foreach (var control in checkControls)
		{
			if (control?.Visible == true && control.GetGlobalRect().HasPoint(mousePos)) return true;
		}

		return false;
	}


	private void ApplyMouseFilterIgnoreRecursive(Node node)
	{
		if (node == null) return;
		if (node is Control ctrl)
		{
			if (ctrl.MouseFilter != MouseFilterEnum.Ignore)
			{
				_savedMouseFilters[ctrl] = ctrl.MouseFilter;
				ctrl.MouseFilter = MouseFilterEnum.Ignore;
			}
		}

		foreach (Node child in node.GetChildren())
		{
			ApplyMouseFilterIgnoreRecursive(child);
		}
	}


	private ObjectAttachmentDialog _objectAttachmentDialog { get; set; } = null!;


	public async Task ProceedToTestMap()
	{
		if (GameContext.Host == null) return;

		_wasmHasErrors = false;
		ShowWasmConsoleModal();
		MatchLoadTrace.Begin("test_map");
		GameHost? tracingHost = GameContext.Host;
		Action<string> logHandler = line => AppendWasmConsoleLog(line);
		GameLog.OnLine += logHandler;

		try
		{
			SaveEditorStateBeforeTest();
			MatchLoadTrace.Mark("editor_state_saved");
			await PrepareMapAndWorkspaceForTest();
			MatchLoadTrace.Mark("map_saved_and_compiled");

			if (_wasmHasErrors) return;

			if (!TryLocateCompiledWasm(out string wasmPath)) return;

			AppendWasmConsoleLog($"[WASM] Running {Path.GetFileName(wasmPath)} from {Path.GetDirectoryName(wasmPath)} ({MapWorkspaceService.ReadBuildFlavorDomain(GameContext.EditorService._editingDirectory) ?? "unrecorded"} build)");

			await LaunchTestModeEngine();
		}
		catch (Exception ex)
		{
			_wasmHasErrors = true;
			SetWasmConsoleStatus("❌ Error launching test mode", new Color(1.0f, 0.3f, 0.3f));
			AppendWasmConsoleLog($"[RUNTIME EXCEPTION] {ex}");
		}
		finally
		{
			if (ReferenceEquals(GameContext.Host, tracingHost) && MatchLoadTrace.IsTracing) MatchLoadTrace.WriteSummary("test_map_abandoned");
			GameLog.OnLine -= logHandler;
		}
	}

	private void SaveEditorStateBeforeTest()
	{
		SaveCameraState();
		SaveGameHostStates();
		SaveEditorWorldBeforeTest();
	}

	private void SaveEditorWorldBeforeTest()
	{
		GameContext.SessionState.EditorWorldState = MapStateSnapshot.CreateSnapshot();
		GameContext.SessionState.SavedEditorDirectory = GameContext.EditorService._editingDirectory;
		GameContext.SessionState.SavedEditorSourceFolder = GameContext.EditorService.CurrentSourceFolder;
	}

	private void SaveCameraState()
	{
		var host = GameContext.Host;
		if (host == null) return;

		var camera = host.MainCamera;
		if (camera == null) return;

		GameContext.SessionState.SavedCameraPosition = camera.Position;

		if (GameContext.World != null && GameContext.World.IsAlive(GameContext.Simulation.WorldEntity) && GameContext.World.Has<CameraState>(GameContext.Simulation.WorldEntity))
		{
			var state = GameContext.World.Get<CameraState>(GameContext.Simulation.WorldEntity);
			GameContext.SessionState.SavedTargetHeight = state.TargetHeight;
			GameContext.SessionState.SavedCurrentHeight = state.CurrentHeight;
			GameContext.SessionState.SavedTargetYaw = state.TargetYaw;
			GameContext.SessionState.SavedCurrentYaw = state.CurrentYaw;
			GameContext.SessionState.SavedTargetPitch = state.TargetPitch;
			GameContext.SessionState.SavedCurrentPitch = state.CurrentPitch;
			GameContext.SessionState.SavedIsTopDown = state.IsTopDown;
			GameContext.SessionState.SavedYawSwing = state.YawSwing;
			GameContext.SessionState.SavedPitchSwing = state.PitchSwing;
		}
	}

	private void SaveGameHostStates()
	{
		var host = GameContext.Host;
		if (host == null) return;

		GameContext.SessionState.SavedGridMode = GameContext.EditorService.Tools.GridMode;
		GameContext.SessionState.SavedActiveTool = host.ActiveEditorTool;
		GameContext.SessionState.SavedActivePlaceId = host.ActivePlaceId;
		GameContext.SessionState.SavedCameraBoundsVisible = GameContext.EditorService.Tools.CameraBoundsVisible;
		GameContext.SessionState.SavedDisableShadows = GameContext.EditorService.Tools.DisableShadows;
		GameContext.SessionState.SavedEditorHasUnsavedChanges = host.EditorHasUnsavedChanges;
		GameContext.SessionState.SavedEntityCategory = _entityPaletteController?.CurrentCategory ?? "";
		GameContext.SessionState.SavedBrushRadius = (float)(_sldBrushSize?.Value ?? 1f);
		float val = (float)(_sldBrushStrength?.Value ?? 1f);
		GameContext.SessionState.SavedBrushStrength = val;
	}

	private async Task PrepareMapAndWorkspaceForTest()
	{
		var host = GameContext.Host;
		if (host == null) return;

		MapWorkspaceService.EnsureWasmEntryPoint(GameContext.EditorService._editingDirectory);
		string tempTerrainPath = Path.Combine(GameContext.EditorService._editingDirectory, "terrain.json");
		host.SaveMapToFile(tempTerrainPath, performReload: false);
		host.EditorHasUnsavedChanges = false;
		InvalidateMetadataCache();

		await GameContext.MapHistoryService.CreateVersionAsync(
			GameContext.EditorService._editingDirectory,
			MapVersionKind.BeforePlaytest,
			null,
			GameContext.EditorService.CurrentSourceFolder);

		if (OperatingSystem.IsWindows())
		{
			SetWasmConsoleStatus("Auto-saving modified files in VSCode...", UIStyle.ColorCyanGlow);
			AppendWasmConsoleLog("[VSCode] Requesting auto-save of all open workspace files...");
			await GameContext.VSCode.SaveAllOpenFilesAsync();
		}

		SetWasmConsoleStatus("Compiling WASM map script...", UIStyle.ColorCyanGlow);
		AppendWasmConsoleLog("=== WASM COMPILATION PIPELINE STARTED ===");
		AppendWasmConsoleLog("[DEBUG] The map script runs in the authority process, not in this editor. Attach with the workspace's 'Attach to Realm Authority (Map Script WASM)' configuration; the authority reports its PID and the JIT ELF it exported once it is running.");

		await CompileWorkspaceScriptAsync(GameContext.EditorService._editingDirectory, isDebug: true);

		if (_wasmHasErrors)
		{
			SetWasmConsoleStatus("❌ WASM Compilation Failed", new Color(1.0f, 0.3f, 0.3f));
			AppendWasmConsoleLog("[ERROR] Map script compilation failed. Test mode aborted.");
		}
	}

	private bool TryLocateCompiledWasm(out string wasmPath)
	{
		string editingDirectory = GameContext.EditorService._editingDirectory;
		wasmPath = MapWorkspaceService.SearchForWasmInDirectoryDomain(Path.Combine(editingDirectory, "bin")) ?? string.Empty;

		if (string.IsNullOrEmpty(wasmPath) || !File.Exists(wasmPath))
		{
			_wasmHasErrors = true;
			SetWasmConsoleStatus("❌ WASM Compilation Failed: Output binary missing", new Color(1.0f, 0.3f, 0.3f));
			AppendWasmConsoleLog($"[ERROR] Could not find the compiled WASM in {MapWorkspaceService.GetScriptOutputDirectory(editingDirectory)}. Test mode aborted.");
			return false;
		}

		return true;
	}

	private async Task LaunchTestModeEngine()
	{
		SetWasmConsoleStatus("Launching test mode...", UIStyle.ColorCyanGlow);
		AppendWasmConsoleLog("=== LAUNCHING GAME ENGINE ===");

		if (GameContext.UserInterface != null)
		{
			await GameContext.UserInterface.ApplyWindowSettings(GameSettings.WindowModeIdx, GameSettings.ResolutionIdx);
		}

		GameContext.SessionState.IsTestMode = true;
		ShadowClientVerifier.IsEnabled = true;

		SetWasmConsoleStatus("Starting the authority that runs this map...", UIStyle.ColorCyanGlow);
		AppendWasmConsoleLog("[AUTHORITY] Starting the process the map script runs in; the editor stays responsive while it loads.");

		bool started = false;
		if (GameContext.Lobby != null)
		{
			started = await GameContext.Lobby.HostSinglePlayerGame(GameContext.EditorService._editingDirectory, "Test Map", debugMapScript: true);
		}

		if (!started)
		{
			_wasmHasErrors = true;
			SetWasmConsoleStatus("❌ Test mode could not start", new Color(1f, 0.3f, 0.3f));
			AppendWasmConsoleLog($"[ERROR] {GameContext.Lobby?.LastHostError ?? "this build has no lobby to start a match with"}");
			AppendAuthorityDiagnostics();
			return;
		}

		MatchLoadTrace.Mark("authority_started");
		MatchLoadTrace.WriteSummary("authority_started");

		GameContext.Host?.ExitMapEditorMode();

		if (GameContext.UserInterface != null)
		{
			GameContext.UserInterface.TransitionTo(GameScreen.InGameHUD);
		}

		int authorityProcessId = GameContext.ServerSession?.AuthorityProcessId ?? 0;
		if (authorityProcessId > 0)
		{
			string attachHint = $"[DEBUG] Authority process ID: {authorityProcessId}. Attach to it in CodeLLDB with the workspace's map script configuration.";
			AppendWasmConsoleLog(attachHint);
			GameLog.LogToConsole(attachHint);
		}

		await Task.Delay(500);
		CloseWasmConsoleModal();
	}

	private const int AuthorityDiagnosticsLineCount = 20;

	private void AppendAuthorityDiagnostics()
	{
		string consolePath = GameContext.Lobby?.LastAuthorityConsoleLogPath ?? string.Empty;
		if (consolePath.Length == 0) return;

		try
		{
			if (!File.Exists(consolePath))
			{
				AppendWasmConsoleLog($"[AUTHORITY] It wrote no console output; its log is '{GameContext.Lobby?.LastAuthorityLogPath}'.");
				return;
			}

			string[] lines = File.ReadAllLines(consolePath);
			int first = Math.Max(0, lines.Length - AuthorityDiagnosticsLineCount);
			AppendWasmConsoleLog($"=== AUTHORITY CONSOLE ({consolePath}, last {lines.Length - first} of {lines.Length} line(s)) ===");
			for (int index = first; index < lines.Length; index++) AppendWasmConsoleLog(lines[index]);
		}
		catch (IOException exception)
		{
			AppendWasmConsoleLog($"[AUTHORITY] Its console output could not be read: {exception.Message}");
		}
		catch (UnauthorizedAccessException exception)
		{
			AppendWasmConsoleLog($"[AUTHORITY] Its console output could not be read: {exception.Message}");
		}
	}


	private Dictionary<int, Texture2D> _swatchTextureCache { get; set; } = new();


	public void InvalidateMetadataCache()
	{
		GameContext.EditorService.CachedMapName = null;
		GameContext.EditorService.CachedMapVersion = null;
		GameContext.EditorService.LastMapNameCacheTicks = 0;
	}

	private Button _btnHeaderLightingTuning { get; set; } = null!;
	private VBoxContainer _contentLightingTuning { get; set; } = null!;

	private HSlider _sldHeightBlendSoftness { get; set; } = null!;
	private HSlider _sldBlendNoiseStrength { get; set; } = null!;
	private HSlider _sldBlendNoiseScale { get; set; } = null!;
}