using Realm.Ecs.Services;
using Godot;
using NSec.Cryptography;
using Realm.EditorAPI;
using Realm.Client.Services;
using Realm.Client.UI.MapEditor;
using Realm.Client.VFX;
using Realm.Shared;
using Realm.Shared.Distribution;
using Realm.Shared.ModelOptimization;
using Realm.Shared.Services;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using MirrorMode = Realm.Ecs.Components.Core.MirrorMode;
using PasteReflection = Realm.Ecs.Components.Core.PasteReflection;
using TerrainCell = Realm.Ecs.Components.Terrain.TerrainCell;
using WaterType = Realm.Ecs.Components.Terrain.WaterType;

namespace Realm.Client.UI;

public partial class MapEditorHUD : Control
{
    public static MapEditorHUD Instance { get; set; }
    public static string CurrentDirectoryBlake3 { get => ServiceLocator.Get<WorldAccessor>().CurrentDirectoryBlake3; set => ServiceLocator.Get<WorldAccessor>().CurrentDirectoryBlake3 = value; }
    public static bool IsDraggingSlider { get; set; } = false;
    public static bool IsTestMode { get => ServiceLocator.Get<EditorService>().IsTestMode; set => ServiceLocator.Get<EditorService>().IsTestMode = value; }
    public static bool ReturningFromTest { get => ServiceLocator.Get<EditorService>().ReturningFromTest; set => ServiceLocator.Get<EditorService>().ReturningFromTest = value; }

    public static Vector3 SavedCameraPosition { get => ServiceLocator.Get<EditorService>().SavedCameraPosition; set => ServiceLocator.Get<EditorService>().SavedCameraPosition = value; }
    public static float SavedTargetHeight { get => ServiceLocator.Get<EditorService>().SavedTargetHeight; set => ServiceLocator.Get<EditorService>().SavedTargetHeight = value; }
    public static float SavedCurrentHeight { get => ServiceLocator.Get<EditorService>().SavedCurrentHeight; set => ServiceLocator.Get<EditorService>().SavedCurrentHeight = value; }
    public static float SavedTargetYaw { get => ServiceLocator.Get<EditorService>().SavedTargetYaw; set => ServiceLocator.Get<EditorService>().SavedTargetYaw = value; }
    public static float SavedCurrentYaw { get => ServiceLocator.Get<EditorService>().SavedCurrentYaw; set => ServiceLocator.Get<EditorService>().SavedCurrentYaw = value; }
    public static float SavedTargetPitch { get => ServiceLocator.Get<EditorService>().SavedTargetPitch; set => ServiceLocator.Get<EditorService>().SavedTargetPitch = value; }
    public static float SavedCurrentPitch { get => ServiceLocator.Get<EditorService>().SavedCurrentPitch; set => ServiceLocator.Get<EditorService>().SavedCurrentPitch = value; }
    public static bool SavedIsTopDown { get => ServiceLocator.Get<EditorService>().SavedIsTopDown; set => ServiceLocator.Get<EditorService>().SavedIsTopDown = value; }
    public static float SavedYawSwing { get => ServiceLocator.Get<EditorService>().SavedYawSwing; set => ServiceLocator.Get<EditorService>().SavedYawSwing = value; }
    public static float SavedPitchSwing { get => ServiceLocator.Get<EditorService>().SavedPitchSwing; set => ServiceLocator.Get<EditorService>().SavedPitchSwing = value; }

    public static Realm.Client.Core.GameHost.GridOverlayMode SavedGridMode { get => ServiceLocator.Get<EditorService>().SavedGridMode; set => ServiceLocator.Get<EditorService>().SavedGridMode = value; }
    public static Realm.Client.Core.GameHost.EditorTool SavedActiveTool { get => ServiceLocator.Get<EditorService>().SavedActiveTool; set => ServiceLocator.Get<EditorService>().SavedActiveTool = value; }
    public static string SavedActivePlaceId { get => ServiceLocator.Get<EditorService>().SavedActivePlaceId; set => ServiceLocator.Get<EditorService>().SavedActivePlaceId = value; }
    public static bool SavedCameraBoundsVisible { get => ServiceLocator.Get<EditorService>().SavedCameraBoundsVisible; set => ServiceLocator.Get<EditorService>().SavedCameraBoundsVisible = value; }
    public static bool SavedDisableShadows { get => ServiceLocator.Get<EditorService>().SavedDisableShadows; set => ServiceLocator.Get<EditorService>().SavedDisableShadows = value; }
    public static string SavedEntityCategory { get => ServiceLocator.Get<EditorService>().SavedEntityCategory; set => ServiceLocator.Get<EditorService>().SavedEntityCategory = value; }

    public static float SavedBrushRadius { get => ServiceLocator.Get<EditorService>().SavedBrushRadius; set => ServiceLocator.Get<EditorService>().SavedBrushRadius = value; }
    public static float SavedBrushStrength { get => ServiceLocator.Get<EditorService>().SavedBrushStrength; set => ServiceLocator.Get<EditorService>().SavedBrushStrength = value; }
    public static float SavedTextureIntensity { get => ServiceLocator.Get<EditorService>().SavedTextureIntensity; set => ServiceLocator.Get<EditorService>().SavedTextureIntensity = value; }

    private static string? _lastUsedFolder { get => ServiceLocator.Get<EditorService>().LastUsedFolder; set => ServiceLocator.Get<EditorService>().LastUsedFolder = value; }
    private static string? _currentSourceFolder { get => ServiceLocator.Get<EditorService>().CurrentSourceFolder; set => ServiceLocator.Get<EditorService>().CurrentSourceFolder = value; }

    private static string? _pendingCasSourceDirectory { get => ServiceLocator.Get<EditorService>().PendingCasSourceDirectory; set => ServiceLocator.Get<EditorService>().PendingCasSourceDirectory = value; }
    private static string? _pendingDefaultSaveFolder { get => ServiceLocator.Get<EditorService>().PendingDefaultSaveFolder; set => ServiceLocator.Get<EditorService>().PendingDefaultSaveFolder = value; }

    private static bool _agreementShownThisSession { get; set; } = false;

    public enum EditorModule
    {
        Terrain,
        TextureDeco,
        Pathing,
        Objects,
        Coordinates,
        Clipboard
    }

    private MapEditorHUDViewModel _viewModel { get; set; } = new();
    public MapEditorHUDViewModel ViewModel => _viewModel;

    private Panel _panelLeft { get; set; }
    private Button _btnLeftTab { get; set; }
    private Panel _panelRight { get; set; }
    private Button _btnRightTab { get; set; }
    private VBoxContainer _accordionContainer { get; set; }

    private bool _leftPanelExpanded { get; set; } = false;
    private bool _rightPanelExpanded { get; set; } = true;

    private OptionButton _optModule { get; set; }
    private Button _btnGameSettings { get; set; }
    private EditorModule _activeModule { get; set; } = EditorModule.Terrain;

    private VBoxContainer _accordionBrush;
    private Button _btnHeaderBrush;
    private VBoxContainer _contentBrush;

    private VBoxContainer _accordionWater;
    private Button _btnHeaderWater;
    private VBoxContainer _contentWater { get; set; }
    private Button _btnWaterActionAdd { get; set; }
    private Button _btnWaterActionRemove { get; set; }
    private bool _isWaterRemoveAction { get => ServiceLocator.Get<EditorService>().IsWaterRemoveAction; set => ServiceLocator.Get<EditorService>().IsWaterRemoveAction = value; }

    private VBoxContainer _accordionTool { get; set; }
    private Button _btnHeaderTool { get; set; }
    private VBoxContainer _contentTool { get; set; }

    private VBoxContainer _accordionToolSettings { get; set; }
    private Button _btnHeaderToolSettings { get; set; }
    private VBoxContainer _contentToolSettings { get; set; }

    private VBoxContainer _accordionPlacement;
    private Button _btnHeaderPlacement;
    private VBoxContainer _contentPlacement;

    private VBoxContainer _accordionViewport;
    private Button _btnHeaderViewport;
    private VBoxContainer _contentViewport;

    private class CardDragData
    {
        public Control CardNode;
        public Button HeaderButton;
        public Control ContentControl;
        public string TitleText;
        public bool IsDragging;
        public bool HasMovedSincePress;
        public Vector2 DragStartMousePos;
        public Vector2 CardStartPos;
    }

    private readonly Dictionary<Control, CardDragData> _cardDragMap = new();
    private Button _btnResetLayout;

    private VBoxContainer _accordionFile;
    private Button _btnHeaderFile;
    private Control _contentFile { get; set; }

    private Realm.Client.UI.MapEditor.MapSettingsDialog? _mapSettingsDialog { get; set; }

    private VBoxContainer _accordionInspector { get; set; }
    private Button _btnHeaderInspector { get; set; }
    private VBoxContainer _contentInspector { get; set; }

    private VBoxContainer _containerTextureSettings { get; set; }
    private VBoxContainer _containerPathingSettings { get; set; }
    private VBoxContainer _containerPlacementSettings { get; set; }
    private VBoxContainer _containerEyedropperSettings { get; set; }
    private VBoxContainer _containerPasteSettings { get; set; }
    private VBoxContainer _containerCategorySelector;

    private VBoxContainer _panelObjects;
    private VBoxContainer _panelClipboard;
    private VBoxContainer _panelTerrainVBox;
    private VBoxContainer _panelDecoVBox;
    private VBoxContainer _panelPathingVBox;
    private VBoxContainer _panelCoordinatesVBox;
    private Button _btnCut;
    private Button _btnEraseArea;
    private Button _btnPasteReflection { get; set; }
    private Button _btnClipboardBrushShape { get; set; }
    private OptionButton _optClipboardMirrorMode { get; set; }
    private HSlider _sldPasteRotation { get; set; }
    private Label _lblPasteRotation { get; set; }

    private OptionButton _optPolarRingSpacing { get; set; }
    private OptionButton _optPolarRadialStep { get; set; }
    private Button _btnTapeMeasure { get; set; }
    private Button _btnResetPivotToCenter { get; set; }
    private HBoxContainer _rowPolarConfig { get; set; }

    private PanelContainer _panelMeasurementHUD;
    private Label _lblMeasureTelemetry;

    private Button _btnPasteAnchor;
    private Label _lblPasteTelemetry;

    private int _currentPasteAnchorIndex = 0;
    private static readonly string[] _pasteAnchorNames = new string[]
    {
        "CENTER",
        "TOP-LEFT",
        "TOP-RIGHT",
        "BOTTOM-RIGHT",
        "BOTTOM-LEFT"
    };

    private List<Button> _swatchButtons = new List<Button>();
    private List<string> _swatchPaths = new List<string>();
    private List<string> _swatchDisplayNames = new List<string>();
    public IReadOnlyList<string> SwatchDisplayNames => _swatchDisplayNames;
    private List<Color> _swatchColors = new List<Color>();
    private ScrollContainer _scrollSwatches;
    private Control _gridSwatches;
    private Button _btnReplaceTexture;

    private Panel _leftPillar;
    private Panel _rightPillar;
    private PanelContainer _topBar;
    private HBoxContainer _topToolbar;
    private VBoxContainer _middleRightBox;
    private HBoxContainer _topLeftBox;
    private TextureRect _screenFrameRect;
    private Tween _hudFadeTween;
    private bool _is3DInteractionActive = false;
    public bool Is3DInteractionActive => _is3DInteractionActive;
    private readonly Dictionary<Control, Control.MouseFilterEnum> _savedMouseFilters = new();

    private PanelContainer _panelTextures;
    private PanelContainer _panelEntityPalette;
    private PanelContainer _panelTerrain;
    private PanelContainer _panelDeco;
    private PanelContainer _panelEnv;

    private Button _btnBackToHub;
    private Button _btnPublish;
    private Button _btnSave;
    private Button _btnSaveAs { get; set; }
    private Button _btnTestMap { get; set; }
    private Button _btnExportMap { get; set; }
    private Button _btnLoad { get; set; }
    private Button _btnDeleteObject { get; set; }
    private Button _btnUndo { get; set; }
    private Button _btnRedo { get; set; }

    private Label _statusLabel { get; set; }
    private Label _feedbackLabel { get; set; }

    private Button _btnZoomIn { get; set; }
    private Button _btnZoomOut;
    private Button _btnCenter;
    private Button _btnRotate { get; set; }
    private Button _btnCameraAngle { get; set; }

    private Slider _sldBrushSize { get; set; }
    private Label _lblBrushSizeValue { get; set; }
    private Slider _sldBrushStrength { get; set; }
    private Label _lblBrushStrengthValue { get; set; }



    private CheckBox _chkRandomRotation { get; set; }
    private CheckBox _chkRandomScale { get; set; }
    private Button _btnAddObject { get; set; }
    private CheckBox _chkClumpMode { get; set; }
    private Control _spacingBox { get; set; }
    private Control _densityBox { get; set; }
    private Control _scaleVarBox { get; set; }
    private Control _camBoundsBox { get; set; }
    private CheckBox _chkBlockMode { get; set; }
    private Slider _sldBlockStep { get; set; }
    private Label _lblBlockStepValue { get; set; }
    private Control _heightBox { get; set; }
    private Slider _sldHeight { get; set; }
    private Label _lblHeightValue { get; set; }

    private Control _waterHeightBox;
    private Slider _sldWaterHeight;
    private Label _lblWaterHeightValue;
    private Control _waterModeBox;
    private OptionButton _optWaterMode;
    private Button _btnWaterProfiles;
    private Realm.Client.UI.MapEditor.WaterProfileDialog _waterProfileDialog;
    private Realm.Client.UI.MapEditor.EnvironmentConfigDialog _environmentConfigDialog;
    private Realm.Client.UI.MapEditor.EntityVisualEditDialog _entityVisualEditDialog;
    private Realm.Client.UI.MapEditor.AnimationPreviewDialog _animationPreviewDialog;
    private Realm.Client.UI.MapEditor.WeaponVfxDialog _weaponVfxDialog;
    private Realm.Client.UI.MapEditor.ModelPickerDialog _modelPickerDialog;
    private Realm.Client.UI.MapEditor.AbilityVfxDialog _abilityVfxDialog;
    private Realm.Client.UI.MapEditor.AssetManagerDialog _assetManagerDialog;
    private Realm.Client.UI.MapEditor.TemplateManagerDialog _templateManagerDialog;
    private Realm.Client.UI.MapEditor.InstanceManagerDialog _instanceManagerDialog;
    private Realm.Client.UI.MapEditor.AssetBrowserDialog _assetBrowserDialog;
    private Realm.Client.UI.MapEditor.NoiseTextureDialog _noiseTextureDialog;
    private Realm.Client.UI.MapEditor.ConvertGlbDialog _convertGlbDialog;
    private Realm.Client.UI.MapEditor.EditorSettingsDialog _editorSettingsDialog;
    private Realm.Client.UI.MapEditor.ShaderEditorDialog _shaderEditorDialog { get; set; }
    private Realm.Client.UI.MapEditor.VfxStudioDialog _vfxStudioDialog { get; set; }
    private Realm.Client.UI.MapEditor.ProceduralAnimationStudioDialog _proceduralAnimationStudioDialog { get; set; }
    private Realm.Client.UI.MapEditor.AuthorSignatureDialog _authorSignatureDialog { get; set; }
    private Realm.Client.UI.MapEditor.ReplaceTextureDialog _replaceTextureDialog { get; set; }
    private Button _btnEditorSettings { get; set; }
    private PanelContainer _mapNameHeaderPanel { get; set; }
    private Label _lblMapNameHeader { get; set; }
    private double _mapNameUpdateTimer { get; set; } = 0.0;
    private Button _btnOpenGlobalOverrides { get; set; }
    private Button _btnOpenAnimationPreview;
    private Button BtnEditVfx { get; set; }
    private Button BtnEditAttachments { get; set; }
    private Button BtnAssetsManager { get; set; }
    private Button BtnTemplateManager { get; set; }
    private Button BtnInstanceManager { get; set; }
    private bool IsUpdatingInspectorUI { get; set; }

    private CheckBox ChkApplyGroundTexture { get; set; }
    private CheckBox ChkApplyCliffTexture { get; set; }
    private HBoxContainer RowGroundTexture { get; set; }
    private HBoxContainer RowCliffTexture { get; set; }

    private Slider _sldPlacementRotate;
    private Label _lblPlacementRotateValue;
    private Slider _sldPlacementScale;
    private Label _lblPlacementScaleValue;
    private VBoxContainer _placementRotateBox;
    private VBoxContainer _placementScaleBox;
    private Button _btnCopy { get; set; }
    private Button _btnPaste { get; set; }
    private Control _stepBox { get; set; }
    private Button _btnToggleSnap { get; set; }
    private Button _btnToggleGrid { get; set; }
    private PopupMenu _popupOverlayMode { get; set; }
    private Button _btnToggleEnvironment { get; set; }
    private PopupPanel _popupEnvironment { get; set; }
    private OptionButton _optEnvLighting { get; set; }
    private OptionButton _optEnvWeather { get; set; }
    private CheckBox _chkEnvShadows { get; set; }
    private Button _btnBrushShape { get; set; }
    private Button _btnResetMap { get; set; }
    private Button _btnGenerateMap { get; set; }
    private Button _btnRandomGen { get; set; }
    private PopupPanel _popupRandomGen { get; set; }
    private Button _btnSaveMore { get; set; }
    private PopupPanel _popupSaveMore { get; set; }
    private Button _btnImportMinimap { get; set; }
    private Button _btnEyedropper { get; set; }
    private OptionButton _optEyedropperMode;
    private Button _btnNoise;
    private Button _btnWater;
    private PanelContainer _minimapFrame;
    private Control _minimapArea;
    private Realm.Client.UI.MapEditor.MapEditorCameraIndicator _cameraIndicator;
    private Vector3 _lastRaycastPos = new Vector3(float.MinValue, float.MinValue, float.MinValue);

    private Button _btnToggleCamera;
    private PopupPanel _popupCamera;
    private CheckBox _chkFreeCamera;



    private Button _btnRaise;
    private Button _btnLower;
    private Button _btnHeight;
    private Button _btnSmooth;
    private Button _btnPlateau;
    private Button _btnRamp;
    private OptionButton _optMirrorMode;
    private OptionButton _optPlacementMirrorMode;
    private Button _btnClumpBrush;
    private HSlider _sldClumpDensity { get; set; }
    private Label _lblClumpDensityValue { get; set; }
    private HSlider _sldClumpScaleVar { get; set; }
    private Label _lblClumpScaleVarValue { get; set; }

    private Button _btnTextureBrush { get; set; }

    private Button _btnFloodFill { get; set; }
    private Button _btnSelectArea { get; set; }
    private Button _btnSelectMove { get; set; }
    private PanelContainer _inspectorPanel { get; set; }
    private Label _lblInspectorTitle { get; set; }
    private Label _lblInspectorPos;
    private Button _btnInspectorRotLeft;
    private Button _btnInspectorRotRight;
    private Button _btnInspectorScaleDown;
    private Button _btnInspectorScaleUp;
    private Button _btnInspectorScaleReset;
    private Button _btnInspectorDelete;
    private Button _btnShowCoverage;
    private HBoxContainer _playerOwnerContainer;
    private OptionButton _optPlayerOwner;
    private PanelContainer _rigStatusContainer;
    private Label _lblRigStatus;

    private Button _btnPathingBrush { get; set; }
    private Button _btnFloodFillPathing { get; set; }
    private CheckBox _chkShallowWater { get; set; }
    private CheckBox _chkDeepWater { get; set; }
    private CheckBox _chkFlying { get; set; }
    private CheckBox _chkGround { get; set; }
    private CheckBox _chkBuildable { get; set; }

    private OptionButton _optPathingMode { get; set; }


    private Button _btnDrawCoordinate { get; set; }
    private LineEdit _txtCoordinateName { get; set; }
    private Button _btnCommitCoordinate { get; set; }
    private VBoxContainer _coordinateListVBox { get; set; }
    private int _pendingCoordinateMinX { get => ServiceLocator.Get<EditorService>().PendingCoordinateMinX; set => ServiceLocator.Get<EditorService>().PendingCoordinateMinX = value; }
    private int _pendingCoordinateMinZ { get => ServiceLocator.Get<EditorService>().PendingCoordinateMinZ; set => ServiceLocator.Get<EditorService>().PendingCoordinateMinZ = value; }
    private int _pendingCoordinateMaxX { get => ServiceLocator.Get<EditorService>().PendingCoordinateMaxX; set => ServiceLocator.Get<EditorService>().PendingCoordinateMaxX = value; }
    private int _pendingCoordinateMaxZ { get => ServiceLocator.Get<EditorService>().PendingCoordinateMaxZ; set => ServiceLocator.Get<EditorService>().PendingCoordinateMaxZ = value; }

    private Button _activeToolButton { get; set; } = null;
    private StyleBoxFlat _highlightStyle { get; set; }

    private Control _cardRaise { get; set; }
    private Control _cardLower { get; set; }
    private Control _cardHeight { get; set; }
    private Control _cardSmooth { get; set; }
    private Control _cardRamp { get; set; }
    private Control _cardNoise { get; set; }
    private Control _cardWater { get; set; }
    private Control _cardPlateau { get; set; }
    private Control _cardTextureBrush { get; set; }
    private Control _cardFloodFill { get; set; }
    private Control _cardPathingBrush { get; set; }
    private Control _cardFloodFillPathing { get; set; }
    private Control _cardAddObject { get; set; }
    private Control _cardSelectMove { get; set; }
    private Control _cardDeleteObject { get; set; }
    private Control _cardSelectArea { get; set; }
    private Control _cardCut { get; set; }
    private Control _cardCopy { get; set; }
    private Control _cardPaste { get; set; }
    private Control _cardEraseArea { get; set; }
    private Label _lblInfoText { get; set; }
    private Label _lblTerrainTexture { get; set; }
    private Label _lblCliffTexture { get; set; }

    private PanelContainer _scaleMapDialog { get; set; }
    private Label _lblScalePreviewWidth;
    private Label _lblScalePreviewHeight;
    private int _scaleDialogTargetWidth;
    private int _scaleDialogTargetDepth;

    private Camera3D _camera3D;
    private Button _btnEditors;
    private Button _btnVSCode;
    private PopupPanel _popupEditors;
    private bool _isDraggingSlider = false;
    private Panel _swatchHighlightPanel;
    private Panel _swatchCliffHighlightPanel;

    private MapEditorTopBar _topBarController;
    private MapEditorBrushSettings _brushSettingsController;
    private MapEditorPlacementSettings _placementSettingsController;
    private MapEditorInspector _inspectorController;
    private MapEditorPathingPanel _pathingPanelController;
    private MapEditorMinimap _minimapController;
    private MapEditorEntityPaletteController _entityPaletteController { get; set; }
    private MapEditorGenerationDialog _generationDialog { get; set; }

    private bool _wasmHasErrors { get; set; } = false;
    private string _wasmCompileLogPath { get; set; } = "";

    public static string TempWorkspaceGodotPath => Realm.Client.Services.ServiceLocator.Get<Realm.Client.Services.EditorService>().TempWorkspaceGodotPath;

    private string _tempWorkspacePath { get => Realm.Client.Services.ServiceLocator.Get<Realm.Client.Services.EditorService>()._tempWorkspacePath; set => Realm.Client.Services.ServiceLocator.Get<Realm.Client.Services.EditorService>()._tempWorkspacePath = value; }
    public string TempWorkspacePath => _tempWorkspacePath;
    private EditorService _editorService => Realm.Client.Services.ServiceLocator.Get<Realm.Client.Services.EditorService>();
    private IEditorAPI _editorApi => Realm.Client.Services.ServiceLocator.Get<Realm.EditorAPI.IEditorAPI>();
    public IEditorAPI EditorApi => _editorApi;
    private MapUpgradeService _mapUpgradeService => Realm.Client.Services.ServiceLocator.Get<Realm.Client.Services.MapUpgradeService>();
    private long _lastTerrainSyncTime { get => Realm.Client.Services.ServiceLocator.Get<Realm.Ecs.Services.WorldAccessor>()._lastTerrainSyncTime; set => Realm.Client.Services.ServiceLocator.Get<Realm.Ecs.Services.WorldAccessor>()._lastTerrainSyncTime = value; }
    private long _lastMetadataSyncTime { get => Realm.Client.Services.ServiceLocator.Get<Realm.Ecs.Services.WorldAccessor>()._lastMetadataSyncTime; set => Realm.Client.Services.ServiceLocator.Get<Realm.Ecs.Services.WorldAccessor>()._lastMetadataSyncTime = value; }
    private bool _isSyncing { get => Realm.Client.Services.ServiceLocator.Get<Realm.Client.Services.EditorService>().IsSyncing; set => Realm.Client.Services.ServiceLocator.Get<Realm.Client.Services.EditorService>().IsSyncing = value; }
    public bool IsSyncing => _isSyncing;

    private double _autoBackupElapsedSeconds { get => Realm.Client.Services.ServiceLocator.Get<Realm.Client.Services.EditorService>().AutoBackupElapsedSeconds; set => Realm.Client.Services.ServiceLocator.Get<Realm.Client.Services.EditorService>().AutoBackupElapsedSeconds = value; }



    public void PublishMapActionExternal()
    {
        string wsPath = !string.IsNullOrEmpty(_tempWorkspacePath) ? _tempWorkspacePath : Realm.Client.Services.MapWorkspaceService.GetActiveWorkspacePath();
        
        if (!TryValidateMapPublishRequirements(wsPath)) return;

        var overlay = new ColorRect { Name = "PublishInstructionsOverlay", Color = new Color(0, 0, 0, 0.7f) };
        overlay.SetAnchorsPreset(LayoutPreset.FullRect);
        AddChild(overlay);

        var center = new CenterContainer();
        center.SetAnchorsPreset(LayoutPreset.FullRect);
        overlay.AddChild(center);

        var panel = CreatePublishMapPanel();
        center.AddChild(panel);

        var vbox = BuildPublishMapUI(panel);

        var title = new Label { Text = "Publish Map Instructions", HorizontalAlignment = HorizontalAlignment.Center };
        title.AddThemeFontSizeOverride("font_size", 24);
        vbox.AddChild(title);

        var optType = new OptionButton();
        optType.AddItem("Custom Arcade Map", 0);
        optType.AddItem("Reusable Asset Pack", 1);
        optType.Selected = _mapSettingsDialog?.SelectedMapTypeIndex ?? 0;
        vbox.AddChild(optType);

        var scroll = new ScrollContainer { SizeFlagsVertical = Control.SizeFlags.ExpandFill };
        var instructionsText = new RichTextLabel { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, SizeFlagsVertical = Control.SizeFlags.ExpandFill, BbcodeEnabled = true };
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

        var (sizesValid, oversizedAssets) = MapAssetHelper.ValidateWorkspaceAssetSizes(wsPath);
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
            BorderWidthTop = 2, BorderWidthBottom = 2, BorderWidthLeft = 2, BorderWidthRight = 2,
            BorderColor = new Color(0.3f, 0.3f, 0.35f, 1f),
            CornerRadiusTopLeft = 4, CornerRadiusTopRight = 4, CornerRadiusBottomLeft = 4, CornerRadiusBottomRight = 4
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
        var tipStyle = new StyleBoxFlat
        {
            BgColor = new Color(0.2f, 0.17f, 0.1f, 0.95f),
            BorderColor = UIStyle.ColorGoldDull,
            BorderWidthBottom = 1, BorderWidthTop = 1, BorderWidthLeft = 1, BorderWidthRight = 1,
            CornerRadiusTopLeft = 4, CornerRadiusTopRight = 4, CornerRadiusBottomLeft = 4, CornerRadiusBottomRight = 4
        };
        tipPanel.AddThemeStyleboxOverride("panel", tipStyle);
        
        var tipMargin = new MarginContainer();
        tipMargin.AddThemeConstantOverride("margin_top", 6);
        tipMargin.AddThemeConstantOverride("margin_bottom", 6);
        tipMargin.AddThemeConstantOverride("margin_left", 10);
        tipMargin.AddThemeConstantOverride("margin_right", 10);
        tipPanel.AddChild(tipMargin);

        var tipLabel = new Label { Text = "💡 " + string.Format(TranslationServer.Translate("Optimization Advisory: We recommend clicking 'Prune Unused' and 'Normalize References' in the Asset Manager before publishing to remove unused files and reference shared greenlit assets (potential savings: {0})."), MapStorageService.FormatBytes(potentialSavedBytes)), AutowrapMode = TextServer.AutowrapMode.WordSmart };
        tipLabel.AddThemeFontSizeOverride("font_size", 12);
        tipLabel.AddThemeColorOverride("font_color", UIStyle.ColorGold);
        tipMargin.AddChild(tipLabel);
        vbox.AddChild(tipPanel);
    }

    private void CreatePublishMapIdentityPanel(VBoxContainer vbox)
    {
        string keyDir = ProjectSettings.GlobalizePath("user://appdata/keys/");
        string defaultUsername = Realm.Client.Network.LobbyManager.Instance?.AuthenticatedUsername ?? System.Environment.UserName;
        var (_, keyData, keyPath, _) = AuthorshipKeyHelper.GetOrGenerateKeyInfo(keyDir, defaultUsername);
        string currentAuthorName = !string.IsNullOrWhiteSpace(keyData?.UserName) ? keyData.UserName : defaultUsername;
        string authorPubKey = !string.IsNullOrWhiteSpace(keyData?.PublicKey) ? keyData.PublicKey : "";
        string shortPubKey = authorPubKey.Length > 16 ? $"{authorPubKey[..8]}...{authorPubKey[^8..]}" : authorPubKey;

        var identityPanel = new PanelContainer();
        var identityStyle = new StyleBoxFlat
        {
            BgColor = new Color(0.12f, 0.15f, 0.22f, 0.95f),
            BorderColor = new Color(0.3f, 0.5f, 0.8f, 0.9f),
            BorderWidthBottom = 1, BorderWidthTop = 1, BorderWidthLeft = 1, BorderWidthRight = 1,
            CornerRadiusTopLeft = 4, CornerRadiusTopRight = 4, CornerRadiusBottomLeft = 4, CornerRadiusBottomRight = 4
        };
        identityPanel.AddThemeStyleboxOverride("panel", identityStyle);
        var idMargin = new MarginContainer();
        idMargin.AddThemeConstantOverride("margin_top", 8);
        idMargin.AddThemeConstantOverride("margin_bottom", 8);
        idMargin.AddThemeConstantOverride("margin_left", 12);
        idMargin.AddThemeConstantOverride("margin_right", 12);
        identityPanel.AddChild(idMargin);

        var idVBox = new VBoxContainer();
        idVBox.AddThemeConstantOverride("separation", 6);
        idMargin.AddChild(idVBox);

        var idHeaderHBox = new HBoxContainer();
        idHeaderHBox.AddThemeConstantOverride("separation", 8);
        var idHeaderLabel = new Label { Text = "🔑 " + TranslationServer.Translate("AUTHOR IDENTITY & KEY VERIFICATION"), SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        idHeaderLabel.AddThemeFontSizeOverride("font_size", 13);
        idHeaderLabel.AddThemeColorOverride("font_color", UIStyle.ColorGold);
        idHeaderHBox.AddChild(idHeaderLabel);

        var btnEditKey = new Button { Text = TranslationServer.Translate("Manage Key / Identity"), CustomMinimumSize = new Vector2(160, 26) };
        btnEditKey.AddThemeConstantOverride("icon_max_width", 0);
        btnEditKey.AddThemeFontSizeOverride("font_size", 11);
        btnEditKey.Pressed += () => { _authorSignatureDialog ??= new Realm.Client.UI.MapEditor.AuthorSignatureDialog(this); _authorSignatureDialog.OpenDialog(); };
        idHeaderHBox.AddChild(btnEditKey);
        idVBox.AddChild(idHeaderHBox);

        var idDetailsLabel = new Label { Text = string.Format(TranslationServer.Translate("Author: {0} | Public Key: {1}\nKey File: {2}"), currentAuthorName, shortPubKey, keyPath), AutowrapMode = TextServer.AutowrapMode.WordSmart };
        idDetailsLabel.AddThemeFontSizeOverride("font_size", 11);
        idDetailsLabel.AddThemeColorOverride("font_color", new Color(0.85f, 0.9f, 1.0f));
        idVBox.AddChild(idDetailsLabel);

        var idBackupWarn = new Label { Text = "⚠️ " + TranslationServer.Translate("Important: Back up your key file! Your authorship key proves ownership of your map name and allows future updates. If you lose your key file, you will permanently lose ownership and the ability to update this map."), AutowrapMode = TextServer.AutowrapMode.WordSmart };
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
        btnPublish.Pressed += () => { bool isFullExport = chkFullExport.ButtonPressed; overlay.QueueFree(); PublishMapAction(isFullExport); };
        hbox.AddChild(btnPublish);

        var btnClose = new Button { Text = TranslationServer.Translate("Close"), CustomMinimumSize = new Vector2(120, 40) };
        btnClose.AddThemeConstantOverride("icon_max_width", 0);
        btnClose.Pressed += () => overlay.QueueFree();
        hbox.AddChild(btnClose);
    }


    private bool _lastBrushShapeIsSquare { get => ServiceLocator.Get<EditorService>()._lastBrushShapeIsSquare; set => ServiceLocator.Get<EditorService>()._lastBrushShapeIsSquare = value; }
    private bool _hasLastBrushShape { get => ServiceLocator.Get<EditorService>()._hasLastBrushShape; set => ServiceLocator.Get<EditorService>()._hasLastBrushShape = value; }

    private float _lastRotationExternal { get => ServiceLocator.Get<EditorService>()._lastRotationExternal; set => ServiceLocator.Get<EditorService>()._lastRotationExternal = value; }

    private float _lastPasteRotationExternal { get => ServiceLocator.Get<EditorService>()._lastPasteRotationExternal; set => ServiceLocator.Get<EditorService>()._lastPasteRotationExternal = value; }

    private PasteReflection _lastPasteReflectionExternal { get => ServiceLocator.Get<EditorService>()._lastPasteReflectionExternal; set => ServiceLocator.Get<EditorService>()._lastPasteReflectionExternal = value; }

    private float _lastScaleExternal { get => ServiceLocator.Get<EditorService>()._lastScaleExternal; set => ServiceLocator.Get<EditorService>()._lastScaleExternal = value; }

    private float _lastBrushSizeExternal { get => ServiceLocator.Get<EditorService>()._lastBrushSizeExternal; set => ServiceLocator.Get<EditorService>()._lastBrushSizeExternal = value; }


    private static System.Collections.Generic.Dictionary<Realm.Client.Core.GameHost.EditorTool, string> ToolInfoTexts => EditorService.ToolInfoTexts;

    public void TriggerToolSelection(Realm.Client.Core.GameHost.EditorTool tool, Button btn, string placeId = "")
    {
        if (Realm.Client.Core.GameHost.Instance == null) return;

        HandleEditorToolStateClear(tool);
        UpdateActiveToolButton(btn);

        Realm.Client.Core.GameHost.Instance.ActiveEditorTool = tool;
        Realm.Client.Core.GameHost.Instance.ActivePlaceId = placeId;

        UpdateActiveEditorModuleForTool(tool);
        UpdatePanelVisibilityForToolPathing(tool);
        UpdateSidebarMorph(tool);
        ApplyBrushStrengthForTool(tool);
        UpdateToolStatusLabel(tool, placeId);
        UpdateTextureLabels();
    }

    private void HandleEditorToolStateClear(Realm.Client.Core.GameHost.EditorTool tool)
    {
        if (tool != Realm.Client.Core.GameHost.EditorTool.SelectMove)
        {
            Realm.Client.Core.GameHost.Instance.SelectedEditorObject = null;
        }
        if (tool != Realm.Client.Core.GameHost.EditorTool.Ramp)
        {
            Realm.Client.Core.GameHost.Instance.ClearRampStartPosExternal();
        }
        if (tool != Realm.Client.Core.GameHost.EditorTool.Measure)
        {
            Realm.Client.Core.GameHost.Instance.ClearMeasureVisuals();
            ClearMeasureTelemetry();
        }
    }

    private void UpdateActiveToolButton(Button btn)
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

    private void UpdateActiveEditorModuleForTool(Realm.Client.Core.GameHost.EditorTool tool)
    {
        EditorModule targetModule = DetermineModuleForTool(tool);

        if (targetModule != _activeModule)
        {
            _activeModule = targetModule;
            UpdateModuleSwitchButtons();
            UpdatePanelVisibilityForModule(targetModule);
        }
    }

    private EditorModule DetermineModuleForTool(Realm.Client.Core.GameHost.EditorTool tool)
    {
        return tool switch
        {
            Realm.Client.Core.GameHost.EditorTool.Raise or Realm.Client.Core.GameHost.EditorTool.Lower or Realm.Client.Core.GameHost.EditorTool.Smooth or Realm.Client.Core.GameHost.EditorTool.Plateau or Realm.Client.Core.GameHost.EditorTool.Ramp or Realm.Client.Core.GameHost.EditorTool.Noise or Realm.Client.Core.GameHost.EditorTool.Water => EditorModule.Terrain,
            Realm.Client.Core.GameHost.EditorTool.PaintTexture or Realm.Client.Core.GameHost.EditorTool.FloodFill => EditorModule.TextureDeco,
            Realm.Client.Core.GameHost.EditorTool.DrawCoordinate => EditorModule.Coordinates,
            Realm.Client.Core.GameHost.EditorTool.PaintPathing or Realm.Client.Core.GameHost.EditorTool.FloodFillPathing => EditorModule.Pathing,
            Realm.Client.Core.GameHost.EditorTool.PlaceUnit or Realm.Client.Core.GameHost.EditorTool.PlaceProp or Realm.Client.Core.GameHost.EditorTool.PlacePropClump or Realm.Client.Core.GameHost.EditorTool.PlaceDecal or Realm.Client.Core.GameHost.EditorTool.DeleteObject or Realm.Client.Core.GameHost.EditorTool.SelectMove => EditorModule.Objects,
            Realm.Client.Core.GameHost.EditorTool.SelectArea or Realm.Client.Core.GameHost.EditorTool.PasteArea => EditorModule.Clipboard,
            _ => _activeModule
        };
    }

    private bool NeedsPathingOverlay(Realm.Client.Core.GameHost.EditorTool tool)
    {
        return tool is Realm.Client.Core.GameHost.EditorTool.PaintPathing or Realm.Client.Core.GameHost.EditorTool.FloodFillPathing or Realm.Client.Core.GameHost.EditorTool.DrawCoordinate or Realm.Client.Core.GameHost.EditorTool.SelectArea or Realm.Client.Core.GameHost.EditorTool.PasteArea;
    }

    private void UpdatePanelVisibilityForToolPathing(Realm.Client.Core.GameHost.EditorTool tool)
    {
        if (_panelTextures != null) _panelTextures.Visible = false;
        if (_panelEntityPalette != null) _panelEntityPalette.Visible = false;
        
        if (tool == Realm.Client.Core.GameHost.EditorTool.DrawCoordinate && _btnCommitCoordinate != null)
        {
            _btnCommitCoordinate.Visible = false;
        }

        if (NeedsPathingOverlay(tool))
        {
            Realm.Client.Core.GameHost.Instance?.UpdatePathingOverlay();
        }
    }

    private void ApplyBrushStrengthForTool(Realm.Client.Core.GameHost.EditorTool tool)
    {
        if (_sldBrushStrength == null || Realm.Client.Core.GameHost.Instance == null) return;

        if (tool == Realm.Client.Core.GameHost.EditorTool.PaintTexture)
        {
            _sldBrushStrength.MinValue = 0.0;
            _sldBrushStrength.MaxValue = 10.0;
            _sldBrushStrength.Step = 1.0;
            _sldBrushStrength.Value = SavedTextureIntensity;
            if (_lblBrushStrengthValue != null) _lblBrushStrengthValue.Text = SavedTextureIntensity.ToString("F0");
            Realm.Client.Core.GameHost.Instance.EditorBrushStrength = SavedTextureIntensity;
        }
        else
        {
            _sldBrushStrength.MinValue = 0.5;
            _sldBrushStrength.MaxValue = 10.0;
            _sldBrushStrength.Step = 0.5;
            float restoredStrength = Math.Max(0.5f, SavedBrushStrength);
            _sldBrushStrength.Value = restoredStrength;
            if (_lblBrushStrengthValue != null) _lblBrushStrengthValue.Text = restoredStrength.ToString("F1");
            Realm.Client.Core.GameHost.Instance.EditorBrushStrength = restoredStrength;
        }
    }

    private void UpdateToolStatusLabel(Realm.Client.Core.GameHost.EditorTool tool, string placeId)
    {
        string shortPlaceName = !string.IsNullOrEmpty(placeId) ? System.IO.Path.GetFileName(placeId).ToUpper() : "";
        UpdateActiveToolLabel(tool, shortPlaceName);

        if (_lblInfoText == null) return;
        UpdateToolInfoText(tool, shortPlaceName);
    }

    private void UpdateActiveToolLabel(Realm.Client.Core.GameHost.EditorTool tool, string shortPlaceName)
    {
        if (_statusLabel == null) return;
        
        string toolName = tool.ToString().ToUpper();
        if (!string.IsNullOrEmpty(shortPlaceName)) toolName += $" ({shortPlaceName})";

        _statusLabel.Text = $"ACTIVE TOOL: {toolName}";
    }

    private void UpdateToolInfoText(Realm.Client.Core.GameHost.EditorTool tool, string shortPlaceName)
    {
        if (tool == Realm.Client.Core.GameHost.EditorTool.PlaceUnit)
        {
            string alignment = (Realm.Client.Core.GameHost.Instance != null && Realm.Client.Core.GameHost.Instance.PlaceUnitIsEnemy) ? TranslationServer.Translate("Enemy (Orc)") : TranslationServer.Translate("Player (Alliance)");
            _lblInfoText.Text = string.Format(TranslationServer.Translate("TOOL: Place Unit\n\nLeft-click on the ground to spawn a {0} aligned with {1}."), shortPlaceName, alignment);
            return;
        }
        
        if (tool == Realm.Client.Core.GameHost.EditorTool.PlaceProp)
        {
            _lblInfoText.Text = string.Format(TranslationServer.Translate("TOOL: Place Prop\n\nLeft-click on the ground to spawn static decorative object: {0}."), shortPlaceName);
            return;
        }
        
        if (tool == Realm.Client.Core.GameHost.EditorTool.PlacePropClump)
        {
            _lblInfoText.Text = string.Format(TranslationServer.Translate("TOOL: Clump Brush\n\nDrag left click on the ground to paint clumps of static props: {0} based on Density and Scale Variation settings. Uses texture brush shape (Circle/Square)."), shortPlaceName);
            return;
        }
        
        if (ToolInfoTexts.TryGetValue(tool, out string rawText))
        {
            _lblInfoText.Text = TranslationServer.Translate(rawText);
        }
    }


    private ColorRect _helpOverlayPanel { get; set; } = null;

    private static int[] SpokeCountOptions => EditorService.SpokeCountOptions;

    private static float[] PolarRingSpacingOptions => Realm.Client.Services.EditorService.PolarRingSpacingOptions;


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
        var host = Realm.Client.Core.GameHost.Instance;
        bool hasSelectedObject = host != null && GodotObject.IsInstanceValid(host.SelectedEditorObject);
        return hasSelectedObject && host?.ActiveEditorTool == Realm.Client.Core.GameHost.EditorTool.SelectMove;
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
        if (_panelEnv != null) _panelEnv.Visible = (module == EditorModule.TextureDeco);
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

    private FontVariation _faFontVariation { get; set; }

    private void StyleGridButton(Button btn)
    {
        if (btn == null) return;
        btn.Flat = false;
        btn.CustomMinimumSize = new Vector2(105, 30);
        btn.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;

        var font = GetFontAwesomeFont();
        if (font != null)
        {
            btn.AddThemeFontOverride("font", font);
        }

        var btnNormal = new StyleBoxFlat();
        btnNormal.BgColor = new Color(0.15f, 0.14f, 0.12f, 0.75f);
        btnNormal.BorderColor = new Color(0.32f, 0.27f, 0.20f, 0.6f);
        btnNormal.SetBorderWidthAll(1);
        btnNormal.CornerRadiusTopLeft = 3;
        btnNormal.CornerRadiusTopRight = 3;
        btnNormal.CornerRadiusBottomLeft = 3;
        btnNormal.CornerRadiusBottomRight = 3;
        btnNormal.ContentMarginLeft = 4;
        btnNormal.ContentMarginRight = 4;

        var btnHover = new StyleBoxFlat();
        btnHover.BgColor = new Color(0.26f, 0.23f, 0.18f, 0.95f);
        btnHover.BorderColor = UIStyle.ColorGold;
        btnHover.SetBorderWidthAll(1);
        btnHover.CornerRadiusTopLeft = 3;
        btnHover.CornerRadiusTopRight = 3;
        btnHover.CornerRadiusBottomLeft = 3;
        btnHover.CornerRadiusBottomRight = 3;
        btnHover.ContentMarginLeft = 4;
        btnHover.ContentMarginRight = 4;

        var btnPressed = new StyleBoxFlat();
        btnPressed.BgColor = new Color(0.32f, 0.28f, 0.21f, 0.98f);
        btnPressed.BorderColor = UIStyle.ColorGold;
        btnPressed.SetBorderWidthAll(1);

        btn.AddThemeStyleboxOverride("normal", btnNormal);
        btn.AddThemeStyleboxOverride("hover", btnHover);
        btn.AddThemeStyleboxOverride("pressed", btnPressed);
        btn.AddThemeStyleboxOverride("focus", new StyleBoxEmpty());
        btn.AddThemeFontSizeOverride("font_size", 11);
        btn.AddThemeColorOverride("font_color", new Color(0.92f, 0.88f, 0.82f));
        btn.AddThemeColorOverride("font_hover_color", UIStyle.ColorGold);
        btn.Alignment = HorizontalAlignment.Center;
    }

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
        if (Realm.Client.UI.SettingsMenu.IsOpen) return true;
        if (_helpOverlayPanel != null && _helpOverlayPanel.IsVisibleInTree()) return true;
        if (GetNodeOrNull<Control>("ConfirmationOverlay") != null) return true;
        if (GetNodeOrNull<Control>("GenerationOverlay") != null) return true;
        if (GetNodeOrNull<Control>("AgreementOverlay") != null) return true;
        if (_scaleMapDialog != null && _scaleMapDialog.IsVisibleInTree()) return true;
        if (Realm.Client.UI.MapEditor.FloatingDialogBase.IsMouseOverAnyDialogOpen(mousePos)) return true;
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

    private bool IsPanelPopupVisible()
    {
        return (_popupOverlayMode?.Visible == true) ||
               (_popupEnvironment?.Visible == true) ||
               (_popupCamera?.Visible == true) ||
               (_popupEditors?.Visible == true) ||
               (_popupSaveMore?.Visible == true) ||
               (_popupRandomGen?.Visible == true);
    }

    private bool IsPrimaryOptionPopupVisible()
    {
        return (_optModule?.GetPopup()?.Visible == true) ||
               (_entityPaletteController?.OptCategoryItems?.GetPopup()?.Visible == true) ||
               (_optEyedropperMode?.GetPopup()?.Visible == true) ||
               (_optPathingMode?.GetPopup()?.Visible == true) ||
               (_optMirrorMode?.GetPopup()?.Visible == true) ||
               (_optClipboardMirrorMode?.GetPopup()?.Visible == true);
    }

    private bool IsSecondaryOptionPopupVisible()
    {
        return (_optPlacementMirrorMode?.GetPopup()?.Visible == true) ||
               (_optPolarRingSpacing?.GetPopup()?.Visible == true) ||
               (_optPolarRadialStep?.GetPopup()?.Visible == true) ||
               (_optEnvLighting?.GetPopup()?.Visible == true) ||
               (_optEnvWeather?.GetPopup()?.Visible == true);
    }

    private bool IsPopupVisible()
    {
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
        Control[] checkControls = {
            _btnLeftTab, _btnRightTab,
            GetNodeOrNull<Control>("TopLeftBox"),
            GetNodeOrNull<Control>("TopBar"),
            _topToolbar, _middleRightBox, _scaleMapDialog,
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
            if (ctrl.MouseFilter != Control.MouseFilterEnum.Ignore)
            {
                _savedMouseFilters[ctrl] = ctrl.MouseFilter;
                ctrl.MouseFilter = Control.MouseFilterEnum.Ignore;
            }
        }
        foreach (Node child in node.GetChildren())
        {
            ApplyMouseFilterIgnoreRecursive(child);
        }
    }

    public Realm.Client.UI.MapEditor.WeaponVfxDialog WeaponVfxDialog => _weaponVfxDialog;

    private Realm.Client.UI.MapEditor.ObjectAttachmentDialog _objectAttachmentDialog { get; set; }


    public async System.Threading.Tasks.Task ProceedToTestMap()
    {
        if (Realm.Client.Core.GameHost.Instance == null) return;

        _wasmHasErrors = false;
        ShowWasmConsoleModal();
        Action<string> logHandler = line => AppendWasmConsoleLog(line);
        Core.WasmRuntime.OnWasmLog += logHandler;

        try
        {
            SaveEditorStateBeforeTest();
            await PrepareMapAndWorkspaceForTest();
            
            if (_wasmHasErrors) return;

            if (!TryLocateCompiledWasm(out string wasmPath)) return;

            Realm.Client.Core.GameHost.PendingMapScriptPath = wasmPath;
            AppendWasmConsoleLog($"[WASM] Located compiled WASM binary: {System.IO.Path.GetFileName(wasmPath)}");

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
            Core.WasmRuntime.OnWasmLog -= logHandler;
        }
    }

    private void SaveEditorStateBeforeTest()
    {
        SaveCameraState();
        SaveGameHostStates();
    }

    private void SaveCameraState()
    {
        var camera = Realm.Client.Core.GameHost.Instance.MainCamera as Realm.Client.CameraControl;
        if (camera == null) return;
        
        SavedCameraPosition = camera.Position;
        
        var host = Realm.Client.Core.GameHost.Instance;
        if (host.EcsWorld != null && host.EcsWorld.IsAlive(host.WorldEntity) && host.EcsWorld.Has<Realm.Ecs.Components.Core.CameraState>(host.WorldEntity))
        {
            var state = host.EcsWorld.Get<Realm.Ecs.Components.Core.CameraState>(host.WorldEntity);
            SavedTargetHeight = state.TargetHeight;
            SavedCurrentHeight = state.CurrentHeight;
            SavedTargetYaw = state.TargetYaw;
            SavedCurrentYaw = state.CurrentYaw;
            SavedTargetPitch = state.TargetPitch;
            SavedCurrentPitch = state.CurrentPitch;
            SavedIsTopDown = state.IsTopDown;
            SavedYawSwing = state.YawSwing;
            SavedPitchSwing = state.PitchSwing;
        }
    }

    private void SaveGameHostStates()
    {
        var host = Realm.Client.Core.GameHost.Instance;
        SavedGridMode = host.EditorGridMode;
        SavedActiveTool = host.ActiveEditorTool;
        SavedActivePlaceId = host.ActivePlaceId;
        SavedCameraBoundsVisible = host.EditorCameraBoundsVisible;
        SavedDisableShadows = host.EditorDisableShadows;
        SavedEntityCategory = _entityPaletteController?.CurrentCategory ?? "";
        SavedBrushRadius = (float)(_sldBrushSize?.Value ?? 1f);
        SavedBrushStrength = (float)(_sldBrushStrength?.Value ?? 1f);
    }

    private async System.Threading.Tasks.Task PrepareMapAndWorkspaceForTest()
    {
        string tempTerrainPath = System.IO.Path.Combine(_tempWorkspacePath, "terrain.json");
        Realm.Client.Core.GameHost.Instance.SaveMapToFile(tempTerrainPath);
        Realm.Client.Core.GameHost.Instance.EditorHasUnsavedChanges = false;
        InvalidateMetadataCache();

        if (OperatingSystem.IsWindows())
        {
            SetWasmConsoleStatus("Auto-saving modified files in VSCode...", UIStyle.ColorCyanGlow);
            AppendWasmConsoleLog("[VSCode] Requesting auto-save of all open workspace files...");
            await Realm.Client.VSCodeManager.Instance.SaveAllOpenFilesAsync();
        }

        SetWasmConsoleStatus("Compiling WASM map script...", UIStyle.ColorCyanGlow);
        AppendWasmConsoleLog("=== WASM COMPILATION PIPELINE STARTED ===");

        await CompileAndSignMapAsync(_tempWorkspacePath, skipAttribution: true);

        if (_wasmHasErrors)
        {
            SetWasmConsoleStatus("❌ WASM Compilation Failed", new Color(1.0f, 0.3f, 0.3f));
            AppendWasmConsoleLog("[ERROR] Map script compilation failed. Test mode aborted.");
        }
    }

    private bool TryLocateCompiledWasm(out string wasmPath)
    {
        string binDir = System.IO.Path.Combine(_tempWorkspacePath, "bin");
        wasmPath = null;
        if (System.IO.Directory.Exists(binDir))
        {
            var wasmFiles = System.IO.Directory.GetFiles(
                binDir,
                "*.wasm",
                System.IO.SearchOption.AllDirectories
            ).Where(f => !f.Contains("native") && !f.Contains("obj")).ToList();

            wasmPath = wasmFiles.FirstOrDefault(f => f.Contains("publish"))
                       ?? wasmFiles.OrderByDescending(f => System.IO.File.GetLastWriteTimeUtc(f)).FirstOrDefault();
        }

        if (!System.IO.File.Exists(wasmPath))
        {
            _wasmHasErrors = true;
            SetWasmConsoleStatus("❌ WASM Compilation Failed: Output binary missing", new Color(1.0f, 0.3f, 0.3f));
            AppendWasmConsoleLog($"[ERROR] Could not find compiled WASM in {binDir}. Test mode aborted.");
            return false;
        }

        return true;
    }

    private async System.Threading.Tasks.Task LaunchTestModeEngine()
    {
        SetWasmConsoleStatus("Launching test mode...", UIStyle.ColorCyanGlow);
        AppendWasmConsoleLog("=== LAUNCHING GAME ENGINE ===");

        if (Realm.Client.UI.UIManager.Instance != null)
        {
            await Realm.Client.UI.UIManager.Instance.ApplyWindowSettings(GameSettings.WindowModeIdx, GameSettings.ResolutionIdx);
        }
        
        Realm.Client.Core.GameHost.Instance.ExitMapEditorMode();
        IsTestMode = true;

        if (Realm.Client.UI.UIManager.Instance != null)
        {
            Realm.Client.UI.UIManager.Instance.TransitionTo(GameScreen.InGameHUD);
        }

        if (Realm.Client.Network.LobbyManager.Instance != null)
        {
            Realm.Client.Network.LobbyManager.Instance.HostSinglePlayerGame(TempWorkspaceGodotPath, "Test Map");
        }

        await System.Threading.Tasks.Task.Delay(500);
        CloseWasmConsoleModal();
    }


    private Dictionary<int, Texture2D> _swatchTextureCache { get; set; } = new();

    private string? _cachedMapName { get => Realm.Client.Services.ServiceLocator.Get<Realm.Ecs.Services.WorldAccessor>()._cachedMapName; set => Realm.Client.Services.ServiceLocator.Get<Realm.Ecs.Services.WorldAccessor>()._cachedMapName = value; }
    private string? _cachedMapVersion { get => Realm.Client.Services.ServiceLocator.Get<Realm.Ecs.Services.WorldAccessor>()._cachedMapVersion; set => Realm.Client.Services.ServiceLocator.Get<Realm.Ecs.Services.WorldAccessor>()._cachedMapVersion = value; }
    private long _lastMapNameCacheTicks;

    public void InvalidateMetadataCache()
    {
        _cachedMapName = null;
        _cachedMapVersion = null;
        _lastMapNameCacheTicks = 0;
    }

    private Button _btnHeaderLightingTuning;
    private VBoxContainer _contentLightingTuning;

    private HSlider _sldCliffJitterStrength, _sldCliffJitterScale, _sldCliffRimNoiseStrength;
    private HSlider _sldHeightBlendSoftness, _sldBlendNoiseStrength, _sldBlendNoiseScale;
    private float _tuneCliffJitterStrength = 1.0f;
    private float _tuneCliffJitterScale = 0.20f;
    private float _tuneCliffRimNoiseStrength = 0.30f;
    private float _tuneHeightBlendSoftness = 0.04f;
    private float _tuneBlendNoiseStrength = 0.22f;
    private float _tuneBlendNoiseScale = 0.22f;
}