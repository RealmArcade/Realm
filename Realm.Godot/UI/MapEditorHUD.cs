using Godot;
using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using HttpClient = System.Net.Http.HttpClient;
using NSec.Cryptography;
using System.Linq;

using MirrorMode = Realm.Ecs.Components.Core.MirrorMode;
using PasteReflection = Realm.Ecs.Components.Core.PasteReflection;
using WaterType = Realm.Ecs.Components.Terrain.WaterType;
using TerrainCell = Realm.Ecs.Components.Terrain.TerrainCell;
using Realm.Shared;
using Realm.Shared.Distribution;
using Realm.Shared.Metadata;
using Realm.Shared.ModelOptimization;
using Realm.Godot.Utils;
using Realm.Godot.VFX;
using Realm.Godot.Services;

public partial class MapEditorHUD : Control
{
	public static MapEditorHUD Instance { get; private set; }
	public static string CurrentDirectoryBlake3 { get; set; } = string.Empty;
	public static bool IsDraggingSlider { get; set; } = false;
	public static bool IsTestMode { get; set; } = false;
	public static bool ReturningFromTest { get; set; } = false;

	public static Vector3 SavedCameraPosition;
	public static float SavedTargetHeight;
	public static float SavedCurrentHeight;
	public static float SavedTargetYaw;
	public static float SavedCurrentYaw;
	public static float SavedTargetPitch;
	public static float SavedCurrentPitch;
	public static bool SavedIsTopDown;
	public static float SavedYawSwing;
	public static float SavedPitchSwing;

	public static GameHost.GridOverlayMode SavedGridMode = GameHost.GridOverlayMode.Off;
	public static GameHost.EditorTool SavedActiveTool = GameHost.EditorTool.Raise;
	public static string SavedActivePlaceId = "";
	public static bool SavedCameraBoundsVisible = false;
	public static bool SavedDisableShadows = false;
	public static string SavedEntityCategory = "";

	public static float SavedBrushRadius = 2f;
	public static float SavedBrushStrength = 0.5f;
	public static float SavedTextureIntensity = 10f;

	private static string? _lastUsedFolder = null;
	private static string? _currentSourceFolder = null;

	private static string? _pendingCasSourceDirectory = null;
	private static string? _pendingDefaultSaveFolder = null;

	public static void RequestOpenFromCas(string casSourceDirectory, string? defaultSaveFolder)
	{
		_pendingCasSourceDirectory = casSourceDirectory;
		_pendingDefaultSaveFolder = defaultSaveFolder;
	}

	private static bool _agreementShownThisSession = false;

	public enum EditorModule
	{
		Terrain,
		TextureDeco,
		Pathing,
		Objects,
		Coordinates,
		Clipboard
	}

	private MapEditorHUDViewModel _viewModel = new();
	public MapEditorHUDViewModel ViewModel => _viewModel;

	private Panel _panelLeft;
	private Button _btnLeftTab;
	private Panel _panelRight;
	private Button _btnRightTab;
	private VBoxContainer _accordionContainer;
	
	private bool _leftPanelExpanded = false;
	private bool _rightPanelExpanded = true;

	private OptionButton _optModule;
	private Button _btnSettings;
	private EditorModule _activeModule = EditorModule.Terrain;

	private VBoxContainer _accordionBrush;
	private Button _btnHeaderBrush;
	private VBoxContainer _contentBrush;

	private VBoxContainer _accordionWater;
	private Button _btnHeaderWater;
	private VBoxContainer _contentWater;
	private Button _btnWaterActionAdd;
	private Button _btnWaterActionRemove;
	private bool _isWaterRemoveAction = false;
	
	private VBoxContainer _accordionTool;
	private Button _btnHeaderTool;
	private VBoxContainer _contentTool;
	
	private VBoxContainer _accordionToolSettings;
	private Button _btnHeaderToolSettings;
	private VBoxContainer _contentToolSettings;
	
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
	private Control _contentFile;

	private MapSettingsDialog? _mapSettingsDialog;
	
	private VBoxContainer _accordionInspector;
	private Button _btnHeaderInspector;
	private VBoxContainer _contentInspector;

	private VBoxContainer _containerTextureSettings;
	private VBoxContainer _containerPathingSettings;
	private VBoxContainer _containerPlacementSettings;
	private VBoxContainer _containerEyedropperSettings;
	private VBoxContainer _containerPasteSettings;
	private VBoxContainer _containerCategorySelector;

	private VBoxContainer _panelObjects;
	private VBoxContainer _panelClipboard;
	private VBoxContainer _panelTerrainVBox;
	private VBoxContainer _panelDecoVBox;
	private VBoxContainer _panelPathingVBox;
	private VBoxContainer _panelCoordinatesVBox;
	private Button _btnCut;
	private Button _btnEraseArea;
	private Button _btnPasteReflection;
	private Button _btnClipboardBrushShape;
	private OptionButton _optClipboardMirrorMode;
	private HSlider _sldPasteRotation;
	private Label _lblPasteRotation;

	private OptionButton _optPolarRingSpacing;
	private OptionButton _optPolarRadialStep;
	private Button _btnTapeMeasure;
	private Button _btnResetPivotToCenter;
	private HBoxContainer _rowPolarConfig;

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
	private Button _btnSaveAs;
	private Button _btnTestMap;
	private Button _btnExportMap;
	private Button _btnLoad;
	private Button _btnDeleteObject;
	private Button _btnUndo;
	private Button _btnRedo;

	private Label _statusLabel;
	private Label _feedbackLabel;

	private Button _btnZoomIn;
	private Button _btnZoomOut;
	private Button _btnCenter;
	private Button _btnRotate;
	private Button _btnCameraAngle;

	private Slider _sldBrushSize;
	private Label _lblBrushSizeValue;
	private Slider _sldBrushStrength;
	private Label _lblBrushStrengthValue;



	private CheckBox _chkRandomRotation;
	private CheckBox _chkRandomScale;
	private Button _btnAddObject;
	private CheckBox _chkClumpMode;
	private Control _spacingBox;
	private Control _densityBox;
	private Control _scaleVarBox;
	private Control _camBoundsBox;
	private CheckBox _chkBlockMode;
	private Slider _sldBlockStep;
	private Label _lblBlockStepValue;
	private Control _heightBox;
	private Slider _sldHeight;
	private Label _lblHeightValue;

	private Control _waterHeightBox;
	private Slider _sldWaterHeight;
	private Label _lblWaterHeightValue;
	private Control _waterModeBox;
	private OptionButton _optWaterMode;
	private Button _btnWaterProfiles;
	private WaterProfileDialog _waterProfileDialog;
	private EnvironmentConfigDialog _environmentConfigDialog;
	private GlobalObjectOverridesDialog _globalOverridesDialog;
	private AnimationPreviewDialog _animationPreviewDialog;
	private WeaponVfxDialog _weaponVfxDialog;
	private ModelPickerDialog _modelPickerDialog;
	private AbilityVfxDialog _abilityVfxDialog;
	private AssetManagerDialog _assetManagerDialog;
	private ObjectManagerDialog _objectManagerDialog;
	private PlacedObjectsDialog _placedObjectsDialog;
	private AssetBrowserDialog _assetBrowserDialog;
	private NoiseTextureDialog _noiseTextureDialog;
	private ConvertGlbDialog _convertGlbDialog;
	private EditorSettingsDialog _editorSettingsDialog;
	private ShaderEditorDialog _shaderEditorDialog;
	private VfxStudioDialog _vfxStudioDialog;
	private ProceduralAnimationStudioDialog _proceduralAnimationStudioDialog;
	private AuthorSignatureDialog _authorSignatureDialog;
	private ReplaceTextureDialog _replaceTextureDialog;
	private Button _btnEditorSettings;
	private Button _btnAuthorSignature;
	private PanelContainer _mapNameHeaderPanel;
	private Label _lblMapNameHeader;
	private double _mapNameUpdateTimer = 0.0;
	private Button _btnOpenGlobalOverrides;
	private Button _btnOpenAnimationPreview;
	private Button _btnEditVfx;
	private Button _btnEditAttachments;
	private Button _btnAssetsManager;
	private Button _btnPlacedObjects;
	private bool _isUpdatingInspectorUI;

	private CheckBox _chkApplyGroundTexture;
	private CheckBox _chkApplyCliffTexture;
	private HBoxContainer _rowGroundTexture;
	private HBoxContainer _rowCliffTexture;

	private Slider _sldPlacementRotate;
	private Label _lblPlacementRotateValue;
	private Slider _sldPlacementScale;
	private Label _lblPlacementScaleValue;
	private VBoxContainer _placementRotateBox;
	private VBoxContainer _placementScaleBox;
	private Button _btnCopy;
	private Button _btnPaste;
	private Control _stepBox;
	private Button _btnToggleSnap;
	private Button _btnToggleGrid;
	private PopupMenu _popupOverlayMode;
	private Button _btnToggleEnvironment;
	private PopupPanel _popupEnvironment;
	private OptionButton _optEnvLighting;
	private OptionButton _optEnvWeather;
	private CheckBox _chkEnvShadows;
	private Button _btnBrushShape;
	private Button _btnResetMap;
	private Button _btnGenerateMap;
	private Button _btnImportMinimap;
	private Button _btnEyedropper;
	private OptionButton _optEyedropperMode;
	private Button _btnNoise;
	private Button _btnWater;
	private PanelContainer _minimapFrame;
	private Control _minimapArea;
	private MapEditorCameraIndicator _cameraIndicator;
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
	private HSlider _sldClumpDensity;
	private Label _lblClumpDensityValue;
	private HSlider _sldClumpScaleVar;
	private Label _lblClumpScaleVarValue;

	private Button _btnTextureBrush;

	private Button _btnFloodFill;
	private Button _btnSelectArea;
	private Button _btnSelectMove;
	private PanelContainer _inspectorPanel;
	private Label _lblInspectorTitle;
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

	private Button _btnPathingBrush;
	private Button _btnFloodFillPathing;
	private CheckBox _chkShallowWater;
	private CheckBox _chkDeepWater;
	private CheckBox _chkFlying;
	private CheckBox _chkGround;
	private CheckBox _chkBuildable;

	private OptionButton _optPathingMode;


	private Button _btnDrawCoordinate;
	private LineEdit _txtCoordinateName;
	private Button _btnCommitCoordinate;
	private VBoxContainer _coordinateListVBox;
	private int _pendingCoordinateMinX;
	private int _pendingCoordinateMinZ;
	private int _pendingCoordinateMaxX;
	private int _pendingCoordinateMaxZ;

	private Button _activeToolButton = null;
	private StyleBoxFlat _highlightStyle;

	private Control _cardRaise, _cardLower, _cardHeight, _cardSmooth, _cardPlateau, _cardRamp, _cardNoise, _cardWater;
	private Control _cardTextureBrush, _cardFloodFill;
	private Control _cardPathingBrush, _cardFloodFillPathing;
	private Control _cardAddObject, _cardSelectMove, _cardDeleteObject;
	private Control _cardSelectArea, _cardCut, _cardCopy, _cardPaste, _cardEraseArea;
	private Label _lblInfoText;
	private Label _lblTerrainTexture;
	private Label _lblCliffTexture;

	private PanelContainer _scaleMapDialog;
	private Label _lblScalePreviewWidth;
	private Label _lblScalePreviewHeight;
	private int _scaleDialogTargetWidth;
	private int _scaleDialogTargetDepth;

	private Camera3D _camera3D;
	private Button _btnVSCode;
	private bool _isDraggingSlider = false;
	private Panel _swatchHighlightPanel;
	private Panel _swatchCliffHighlightPanel;

	private MapEditorTopBar _topBarController;
	private MapEditorBrushSettings _brushSettingsController;
	private MapEditorPlacementSettings _placementSettingsController;
	private MapEditorInspector _inspectorController;
	private MapEditorPathingPanel _pathingPanelController;
	private MapEditorMinimap _minimapController;
	private MapEditorEntityPaletteController _entityPaletteController;
	private MapEditorGenerationDialog _generationDialog;

	private bool _wasmHasErrors = false;
	private string _wasmCompileLogPath = "";

	public const string TempWorkspaceGodotPath = MapWorkspaceService.DefaultWorkspaceGodotPath;

	private string _tempWorkspacePath = MapWorkspaceService.GetDefaultWorkspaceGlobalPath();
	public string TempWorkspacePath => _tempWorkspacePath;
	private EditorService _editorService;
	private MapUpgradeService _mapUpgradeService;
	private long _lastTerrainSyncTime = 0;
	private long _lastMetadataSyncTime = 0;
	private bool _isSyncing = false;
	public bool IsSyncing => _isSyncing;

	public void UpdateLastMetadataSyncTime(string? path = null)
	{
		string wsPath = string.IsNullOrEmpty(_tempWorkspacePath) 
			? ProjectSettings.GlobalizePath(TempWorkspaceGodotPath) 
			: _tempWorkspacePath;
		string metadataPath = string.IsNullOrEmpty(path) ? System.IO.Path.Combine(wsPath, "metadata.json") : path;
		_lastMetadataSyncTime = Math.Max(GetLastWriteTimeSafe(metadataPath), DateTime.UtcNow.Ticks);
	}

	private void OnMetadataSaved(string targetPath)
	{
		UpdateLastMetadataSyncTime(targetPath);
	}

	public override void _ExitTree()
	{
		MetadataService.Instance.MetadataSaved -= OnMetadataSaved;
		_editorService?.StopWorkspaceWatcher();
		CloseWasmConsoleModal();
		if (Instance == this)
		{
			Instance = null;
		}
		if (GodotObject.IsInstanceValid(_swatchHighlightPanel) && _swatchHighlightPanel.GetParent() == null)
		{
			_swatchHighlightPanel.QueueFree();
		}
		if (GodotObject.IsInstanceValid(_swatchCliffHighlightPanel) && _swatchCliffHighlightPanel.GetParent() == null)
		{
			_swatchCliffHighlightPanel.QueueFree();
		}
		if (_hudFadeTween != null && _hudFadeTween.IsValid())
		{
			_hudFadeTween.Kill();
		}
		foreach (var (ctrl, filter) in _savedMouseFilters)
		{
			if (GodotObject.IsInstanceValid(ctrl))
			{
				ctrl.MouseFilter = filter;
			}
		}
		_savedMouseFilters.Clear();
	}

	private double _autoBackupElapsedSeconds = 0;

	public void PerformAutoBackup()
	{
		try
		{
			if (GameHost.Instance == null || GameHost.Instance.GroundTerrain == null) return;
			string wsPath = MapWorkspaceService.GetActiveWorkspacePath();
			if (string.IsNullOrEmpty(wsPath) || !System.IO.Directory.Exists(wsPath)) return;

			string tempTerrainPath = System.IO.Path.Combine(wsPath, "terrain.json");
			GameHost.Instance.SaveMapToFile(tempTerrainPath, performReload: false);
			_lastTerrainSyncTime = GetMaxTerrainWriteTime(tempTerrainPath);
			_lastMetadataSyncTime = GetLastWriteTimeSafe(System.IO.Path.Combine(wsPath, "metadata.json"));
			ShowFeedback(TranslationServer.Translate("Auto-backup snapshot saved."));
			GD.Print("[MapEditorHUD] Auto-backup snapshot saved.");
		}
		catch (Exception ex)
		{
			GD.PrintErr($"[MapEditorHUD] Auto-backup failed: {ex.Message}");
		}
	}

	public void UpdateFPSVisibility()
	{
		var fpsLabel = (GameHost.Instance != null ? GameHost.Instance.MainNode?.GetNodeOrNull<Label>("CanvasLayer/FPS") : null);
		if (fpsLabel != null)
		{
			fpsLabel.Visible = GameSettings.DisplayFps;
			fpsLabel.ZIndex = 100;
		}
	}

	public override void _Ready()
	{
		try
		{
			Instance = this;
			MetadataService.Instance.MetadataSaved += OnMetadataSaved;
			_editorService = ServiceLocator.TryGet<EditorService>();
			_mapUpgradeService = ServiceLocator.TryGet<MapUpgradeService>();
			UpdateFPSVisibility();
			_tempWorkspacePath = MapWorkspaceService.GetDefaultWorkspaceGlobalPath();

			_camera3D = (GameHost.Instance?.MainCamera);

			HookSliders(this);
			ChildEnteredTree += (node) => HookSliders(node);

		_highlightStyle = new StyleBoxFlat();
		_highlightStyle.BgColor = new Color(0.35f, 0.28f, 0.18f, 0.95f);
		_highlightStyle.BorderColor = UIStyle.ColorGold;
		_highlightStyle.SetBorderWidthAll(2);
		_highlightStyle.CornerRadiusTopLeft = 6;
		_highlightStyle.CornerRadiusTopRight = 6;
		_highlightStyle.CornerRadiusBottomLeft = 6;
		_highlightStyle.CornerRadiusBottomRight = 6;
		_highlightStyle.ContentMarginLeft = 4;
		_highlightStyle.ContentMarginRight = 4;
		_highlightStyle.ContentMarginTop = 4;
		_highlightStyle.ContentMarginBottom = 4;

		var panelTex = GD.Load<Texture2D>("res://Assets/UI/map_editor_panel.png");
		if (panelTex != null)
		{
			var frameRect = new TextureRect();
			frameRect.Name = "MapEditorScreenFrame";
			frameRect.Texture = panelTex;
			frameRect.SetAnchorsPreset(Control.LayoutPreset.FullRect);
			frameRect.ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize;
			frameRect.StretchMode = TextureRect.StretchModeEnum.Scale;
			frameRect.MouseFilter = Control.MouseFilterEnum.Ignore;
			frameRect.Visible = !EditorSettingsDialog.CurrentSettings.HideChromeBorderOverlay;
			AddChild(frameRect);
			MoveChild(frameRect, 0);
			_screenFrameRect = frameRect;
		}

		_topLeftBox = GetNodeOrNull<HBoxContainer>("TopLeftBox");
		_topBar = GetNodeOrNull<PanelContainer>("TopBar");

		_leftPillar = new Panel();
		_rightPillar = new Panel();
		_topToolbar = new HBoxContainer();

		_panelTextures = new PanelContainer();
		_panelEntityPalette = new PanelContainer();
		_panelTerrain = new PanelContainer();
		_panelDeco = new PanelContainer();
		_panelEnv = new PanelContainer();
		_btnClumpBrush = new Button();

		_panelLeft = GetNode<Panel>("LeftSlidePanel");
		_panelRight = GetNode<Panel>("RightSlidePanel");
		if (_panelLeft != null) _panelLeft.MouseFilter = Control.MouseFilterEnum.Ignore;
		if (_panelRight != null) _panelRight.MouseFilter = Control.MouseFilterEnum.Ignore;

		_btnLeftTab = GetNodeOrNull<Button>("LeftSlidePanel/LeftTabButton");
		if (_btnLeftTab != null)
		{
			_btnLeftTab.Visible = false;
			_btnLeftTab.Pressed += ToggleLeftPanel;
		}

		_btnRightTab = GetNodeOrNull<Button>("RightSlidePanel/RightTabButton");
		if (_btnRightTab != null)
		{
			_btnRightTab.Visible = false;
			_btnRightTab.Pressed += ToggleRightPanel;
		}

		_btnBackToHub = GetNode<Button>("TopLeftBox/BtnBack");
		SetupButton(_btnBackToHub, "\uf2f5 BACK TO HUB", () => BackToHubAction(), 13, "Exit editor and return to game lobby");
		StyleMapEditorTopButton(_btnBackToHub);
		_mapNameHeaderPanel = new PanelContainer();
		_mapNameHeaderPanel.Name = "MapNameHeaderPanel";
		_mapNameHeaderPanel.AddThemeStyleboxOverride("panel", UIStyle.CreateLightInnerPanel());
		_mapNameHeaderPanel.CustomMinimumSize = new Vector2(160, 32);
		_mapNameHeaderPanel.MouseFilter = Control.MouseFilterEnum.Stop;
		_mapNameHeaderPanel.MouseDefaultCursorShape = Control.CursorShape.PointingHand;
		_mapNameHeaderPanel.TooltipText = TranslationServer.Translate("Click to open Map Settings");
		_mapNameHeaderPanel.GuiInput += (ev) =>
		{
			if (ev is InputEventMouseButton mb && mb.Pressed && mb.ButtonIndex == MouseButton.Left)
			{
				_mapSettingsDialog?.OpenDialog();
			}
		};

		var mapNameHBox = new HBoxContainer();
		mapNameHBox.Alignment = BoxContainer.AlignmentMode.Center;
		mapNameHBox.AddThemeConstantOverride("separation", 6);
		_mapNameHeaderPanel.AddChild(mapNameHBox);

		_lblMapNameHeader = new Label();
		_lblMapNameHeader.Name = "LblMapNameHeader";
		_lblMapNameHeader.Text = "";
		_lblMapNameHeader.AddThemeFontSizeOverride("font_size", 12);
		_lblMapNameHeader.AddThemeColorOverride("font_color", UIStyle.ColorGold);
		_lblMapNameHeader.HorizontalAlignment = HorizontalAlignment.Center;
		_lblMapNameHeader.VerticalAlignment = VerticalAlignment.Center;
		_lblMapNameHeader.MouseFilter = Control.MouseFilterEnum.Pass;
		mapNameHBox.AddChild(_lblMapNameHeader);

		var topLeftBoxNode = GetNodeOrNull<HBoxContainer>("TopLeftBox");
		if (topLeftBoxNode != null)
		{
			topLeftBoxNode.AddChild(_mapNameHeaderPanel);
			int backIdx = _btnBackToHub.GetIndex();
			topLeftBoxNode.MoveChild(_mapNameHeaderPanel, backIdx + 1);
		}

		UpdateMapNameHeader();

		var btnHelp = GetNode<Button>("TopLeftBox/BtnHelp");
		SetupButton(btnHelp, "\uf059 HELP / HOTKEYS", () => ToggleHelpPanelExternal(), 13, "Toggle the hotkeys and editor guide overlay (H)");
		StyleMapEditorTopButton(btnHelp);

		if (OperatingSystem.IsWindows())
		{
			GenerateVSCodeFilesExternal();
			VSCodeManager.Instance.Initialize(this);
			_btnVSCode = GetNode<Button>("TopLeftBox/BtnVSCode");
			SetupButton(_btnVSCode, "\uf121 CODE & DATA", () => ToggleVSCodeEditor(), 13, "Toggle the embedded VSCode editor (Right-click or Middle-click: DevTools)");
			_btnVSCode.GuiInput += (@event) =>
			{
				if (OperatingSystem.IsWindows() && @event is InputEventMouseButton mouseButton && mouseButton.Pressed)
				{
					if (mouseButton.ButtonIndex == MouseButton.Right || mouseButton.ButtonIndex == MouseButton.Middle)
					{
						if (!VSCodeManager.Instance.IsVisible)
						{
							ToggleVSCodeEditor();
						}
						VSCodeManager.Instance.OpenDevTools();
						GetViewport().SetInputAsHandled();
					}
				}
			};
			StyleMapEditorTopButton(_btnVSCode);
		}
		else
		{
			_btnVSCode = new Button();
		}

		_btnUndo = GetNode<Button>("TopLeftBox/BtnUndo");
		SetupButton(_btnUndo, "\uf0e2 UNDO", () => UndoAction(), 13, "Undo the last action (Ctrl+Z)");
		StyleMapEditorTopButton(_btnUndo);

		_btnRedo = GetNode<Button>("TopLeftBox/BtnRedo");
		SetupButton(_btnRedo, "\uf01e REDO", () => RedoAction(), 13, "Redo the last undone action (Ctrl+Y)");
		StyleMapEditorTopButton(_btnRedo);

		_btnEyedropper = GetNode<Button>("TopLeftBox/BtnEyedropper");
		SetupButton(_btnEyedropper, "\uf1fb EYEDROPPER", () => TriggerToolSelection(GameHost.EditorTool.Eyedropper, _btnEyedropper), 13, "Pick / sample entities, terrain height (Shift+Click), or vertex color under cursor (I)");
		StyleMapEditorTopButton(_btnEyedropper);

		_optModule = GetNode<OptionButton>("TopLeftBox/OptModule");
		StyleOptionButtonPopup(_optModule);
		_optModule.AddItem("\uf6e8 " + TranslationServer.Translate("TERRAIN"), (int)EditorModule.Terrain);
		_optModule.AddItem("\uf1fc " + TranslationServer.Translate("TEXTURE"), (int)EditorModule.TextureDeco);
		_optModule.AddItem("\uf4d7 " + TranslationServer.Translate("PATHING"), (int)EditorModule.Pathing);
		_optModule.AddItem("\uf1b2 " + TranslationServer.Translate("OBJECTS"), (int)EditorModule.Objects);
		_optModule.AddItem("\uf303 " + TranslationServer.Translate("COORDINATES"), (int)EditorModule.Coordinates);
		_optModule.AddItem("\uf0ea " + TranslationServer.Translate("CLIPBOARD"), (int)EditorModule.Clipboard);
		_optModule.ItemSelected += (index) => SwitchModule((EditorModule)index);
		StyleMapEditorTopButton(_optModule);

		_btnSettings = GetNodeOrNull<Button>("TopLeftBox/BtnSettings");
		if (_btnSettings != null)
		{
			SetupIconButton(_btnSettings, "res://Assets/UI/gear_icon.png", () =>
			{
				UIManager.Instance?.OpenSettingsOverlay();
			}, "Editor Settings");
			StyleMapEditorTopButton(_btnSettings);
		}

		var topLeftBox = GetNodeOrNull<HBoxContainer>("TopLeftBox");
		if (topLeftBox != null)
		{
			_btnResetLayout = new Button();
			_btnResetLayout.Name = "BtnResetLayout";
			SetupButton(_btnResetLayout, "\uf08d RESET LAYOUT", () => ResetAllPanelPositions(), 12, "Reset all floating panels back to default sidebar positions");
			StyleMapEditorTopButton(_btnResetLayout);
			topLeftBox.AddChild(_btnResetLayout);
			if (_btnSettings != null)
			{
				topLeftBox.MoveChild(_btnResetLayout, _btnSettings.GetIndex());
			}
		}

		_statusLabel = GetNode<Label>("TopBar/HBox/StatusLabel");
		_feedbackLabel = GetNode<Label>("FeedbackLabel");
		_feedbackLabel.Modulate = new Color(1, 1, 1, 0);
		_feedbackLabel.MouseFilter = Control.MouseFilterEnum.Ignore;

		var leftScroll = GetNodeOrNull<ScrollContainer>("LeftSlidePanel/LeftScroll");
		if (leftScroll != null)
		{
			leftScroll.MouseFilter = Control.MouseFilterEnum.Ignore;
			leftScroll.HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled;
			leftScroll.VerticalScrollMode = ScrollContainer.ScrollMode.Disabled;
		}

		var rightScroll = GetNodeOrNull<ScrollContainer>("RightSlidePanel/RightScroll");
		if (rightScroll != null)
		{
			rightScroll.MouseFilter = Control.MouseFilterEnum.Ignore;
			rightScroll.HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled;
			rightScroll.VerticalScrollMode = ScrollContainer.ScrollMode.Disabled;
		}

		// Left Accordions
		_accordionFile = GetNode<VBoxContainer>("LeftSlidePanel/LeftScroll/LeftVBox/FileAccordion");
		_btnHeaderFile = GetNode<Button>("LeftSlidePanel/LeftScroll/LeftVBox/FileAccordion/BtnHeaderFile");
		_contentFile = GetNode<VBoxContainer>("LeftSlidePanel/LeftScroll/LeftVBox/FileAccordion/ContentFile");
		StyleAccordionHeader(_btnHeaderFile);
		SetupAccordion(_btnHeaderFile, _contentFile, TranslationServer.Translate("File"));

		_btnLoad = GetNode<Button>("LeftSlidePanel/LeftScroll/LeftVBox/FileAccordion/ContentFile/BtnLoad");
		SetupOptionButton(_btnLoad, "\uf07c LOAD", () => LoadMapAction(), 11, "Load heights, colors, and entities from a saved json file (Ctrl+O)");

		_btnSave = GetNode<Button>("LeftSlidePanel/LeftScroll/LeftVBox/FileAccordion/ContentFile/BtnSave");
		SetupOptionButton(_btnSave, "\uf0c7 SAVE", () => SaveMapActionExternal(), 11, "Save current heightmap, textures, and entities (Ctrl+S)");

		_btnSaveAs = new Button();
		_btnSaveAs.Name = "BtnSaveAs";
		_btnSaveAs.Set("icon_max_width", 0);
		SetupOptionButton(_btnSaveAs, "\uf0c7 SAVE AS", () => SaveAsMapAction(), 11, "Save map to a new folder location");
		_contentFile.AddChild(_btnSaveAs);
		_contentFile.MoveChild(_btnSaveAs, _btnSave.GetIndex() + 1);

		_btnTestMap = GetNode<Button>("LeftSlidePanel/LeftScroll/LeftVBox/FileAccordion/ContentFile/BtnTestMap");
		SetupOptionButton(_btnTestMap, "\uf11b TEST", () => TestMapAction(), 13, "Launch single-player mode on the current editor map");

		_btnPublish = GetNode<Button>("LeftSlidePanel/LeftScroll/LeftVBox/FileAccordion/ContentFile/BtnPublish");
		SetupOptionButton(_btnPublish, "\uf093 PUBLISH", () => PublishMapActionExternal(), 13, "Publish/export map to custom map registry");

		_btnExportMap = GetNodeOrNull<Button>("LeftSlidePanel/LeftScroll/LeftVBox/FileAccordion/ContentFile/BtnExportMap");
		if (_btnExportMap == null)
		{
			_btnExportMap = new Button();
			_btnExportMap.Name = "BtnExportMap";
			_btnExportMap.Set("icon_max_width", 0);
			int insertIndex = _contentFile.GetChildren().IndexOf(_btnTestMap);
			if (insertIndex >= 0)
			{
				_contentFile.AddChild(_btnExportMap);
				_contentFile.MoveChild(_btnExportMap, insertIndex + 1);
			}
			else
			{
				_contentFile.AddChild(_btnExportMap);
			}
		}
		SetupOptionButton(_btnExportMap, "\uf56e EXPORT (.RMAP)", () => ExportMapAction(), 13, "Export prepared map package (.rmap) with compiled WASM for hosting and CAS storage");

		_btnResetMap = GetNode<Button>("LeftSlidePanel/LeftScroll/LeftVBox/FileAccordion/ContentFile/BtnResetMap");
		SetupOptionButton(_btnResetMap, "\uf12d RESET MAP", () =>
		{
			ShowConfirmationDialog(
				"Are you sure you want to clear the entire map? This will delete all placed entities and reset terrain heights.",
				() => ResetToBlankMap()
			);
		}, 13, "Clear all terrain heights, colors, and placed entities");

		_btnGenerateMap = GetNode<Button>("LeftSlidePanel/LeftScroll/LeftVBox/FileAccordion/ContentFile/BtnGenerateMap");
		SetupOptionButton(_btnGenerateMap, "\uf522 RANDOM GEN", () => _generationDialog.Show(), 13, "Open random terrain generator settings modal");

		_btnImportMinimap = GetNode<Button>("LeftSlidePanel/LeftScroll/LeftVBox/FileAccordion/ContentFile/BtnImportMinimap");
		SetupOptionButton(_btnImportMinimap, "\uf279 FROM IMAGE", () => ImportTerrainFromMinimapDialog(), 13, "Import terrain elevations, textures, and trees from a minimap image file");

		_btnAssetsManager = new Button();
		_btnAssetsManager.Name = "BtnAssetsManager";
		SetupOptionButton(_btnAssetsManager, "\uf1b2 ASSETS", () => _assetManagerDialog?.OpenDialog(), 13, "Open Map Assets Manager & Importer");
		_contentFile.AddChild(_btnAssetsManager);

		_btnPlacedObjects = new Button();
		_btnPlacedObjects.Name = "BtnPlacedObjects";
		SetupOptionButton(_btnPlacedObjects, "\uf0cb OBJECTS", () => OpenPlacedObjectsDialog(), 13, "Open dialog to list and locate all placed objects");
		_contentFile.AddChild(_btnPlacedObjects);

		_btnEditorSettings = new Button();
		_btnEditorSettings.Name = "BtnEditorSettings";
		_btnEditorSettings.Set("icon_max_width", 0);
		SetupOptionButton(_btnEditorSettings, "⚙️ " + TranslationServer.Translate("EDITOR SETTINGS"), () => _editorSettingsDialog?.OpenDialog(), 13, "Configure editor preferences, chrome border, and display overlays");
		_contentFile.AddChild(_btnEditorSettings);

		_btnAuthorSignature = new Button();
		_btnAuthorSignature.Name = "BtnAuthorSignature";
		_btnAuthorSignature.Set("icon_max_width", 0);
		SetupOptionButton(_btnAuthorSignature, "✍️ " + TranslationServer.Translate("AUTHOR SIGNATURE"), () => _authorSignatureDialog?.OpenDialog(), 13, "View author identity key, signature details, and backup location");
		int pubIdx = _contentFile.GetChildren().IndexOf(_btnPublish);
		if (pubIdx >= 0)
		{
			_contentFile.AddChild(_btnAuthorSignature);
			_contentFile.MoveChild(_btnAuthorSignature, pubIdx + 1);
		}
		else
		{
			_contentFile.AddChild(_btnAuthorSignature);
		}

		_accordionViewport = GetNode<VBoxContainer>("LeftSlidePanel/LeftScroll/LeftVBox/ViewportAccordion");
		_btnHeaderViewport = GetNode<Button>("LeftSlidePanel/LeftScroll/LeftVBox/ViewportAccordion/BtnHeaderViewport");
		_contentViewport = GetNode<VBoxContainer>("LeftSlidePanel/LeftScroll/LeftVBox/ViewportAccordion/ContentViewport");
		StyleAccordionHeader(_btnHeaderViewport);
		SetupAccordion(_btnHeaderViewport, _contentViewport, TranslationServer.Translate("Viewport & Navigation"));

		InitializeTempWorkspace();

	


		_popupOverlayMode = new PopupMenu();
		_popupOverlayMode.Name = "PopupOverlayMode";
		_popupOverlayMode.HideOnCheckableItemSelection = false;
		_popupOverlayMode.AddCheckItem(TranslationServer.Translate("Grid"), 0);
		_popupOverlayMode.AddCheckItem(TranslationServer.Translate("Polar"), 1);
		_popupOverlayMode.AddCheckItem(TranslationServer.Translate("Camera Bounds"), 2);
		_popupOverlayMode.AddCheckItem(TranslationServer.Translate("Wireframe"), 3);
		StylePopupMenu(_popupOverlayMode);
		_popupOverlayMode.IdPressed += (long id) =>
		{
			if (GameHost.Instance == null) return;
			int idx = _popupOverlayMode.GetItemIndex((int)id);
			if (idx < 0) return;
			bool newChecked = !_popupOverlayMode.IsItemChecked(idx);
			_popupOverlayMode.SetItemChecked(idx, newChecked);

			if (id == 0 || id == 1)
			{
				int gridIdx = _popupOverlayMode.GetItemIndex(0);
				int polarIdx = _popupOverlayMode.GetItemIndex(1);
				bool gridOn = gridIdx >= 0 && _popupOverlayMode.IsItemChecked(gridIdx);
				bool polarOn = polarIdx >= 0 && _popupOverlayMode.IsItemChecked(polarIdx);

				var newMode = (gridOn, polarOn) switch
				{
					(true, true) => GameHost.GridOverlayMode.Both,
					(true, false) => GameHost.GridOverlayMode.Grid,
					(false, true) => GameHost.GridOverlayMode.Polar,
					(false, false) => GameHost.GridOverlayMode.Off
				};

				GameHost.Instance.EditorGridMode = newMode;
				GameHost.Instance.UpdateGridOverlayVisibility();
				UpdateGridOverlayExternal(newMode);
				string modeName = newMode switch
				{
					GameHost.GridOverlayMode.Off => "OFF",
					GameHost.GridOverlayMode.Grid => "GRID",
					GameHost.GridOverlayMode.Polar => "POLAR",
					GameHost.GridOverlayMode.Both => "GRID + POLAR",
					_ => "OFF"
				};
				ShowFeedback($"Overlay Mode: {modeName}");
			}
			else if (id == 2)
			{
				GameHost.Instance.EditorCameraBoundsVisible = newChecked;
				GameHost.Instance.UpdateCameraBoundsOverlayVisibility();
				UpdateCameraBoundsOverlayExternal(newChecked);
				ShowFeedback(newChecked
					? TranslationServer.Translate("Camera Bounds: ON")
					: TranslationServer.Translate("Camera Bounds: OFF"));
			}
			else if (id == 3)
			{
				if (GameHost.Instance.GroundTerrain != null)
				{
					GameHost.Instance.GroundTerrain.ToggleWireframeMode();
					bool isWireframe = GetViewport()?.DebugDraw == Viewport.DebugDrawEnum.Wireframe;
					UpdateWireframeOverlayExternal(isWireframe);
					ShowFeedback(isWireframe
						? TranslationServer.Translate("Wireframe Mode: ON")
						: TranslationServer.Translate("Wireframe Mode: OFF"));
				}
			}
		};
		AddChild(_popupOverlayMode);

		_btnToggleGrid = GetNode<Button>("LeftSlidePanel/LeftScroll/LeftVBox/ViewportAccordion/ContentViewport/BtnToggleGrid");
		SetupButton(_btnToggleGrid, "\uf84c", () => OpenOverlayModePopup(), 12, "Overlay");
		_popupEnvironment = new PopupPanel();
		_popupEnvironment.Name = "PopupEnvironment";
		
		var envPopupStyle = new StyleBoxFlat();
		envPopupStyle.BgColor = new Color(0.14f, 0.13f, 0.11f, 0.98f);
		envPopupStyle.BorderColor = UIStyle.ColorGold;
		envPopupStyle.SetBorderWidthAll(1);
		envPopupStyle.CornerRadiusTopLeft = 4;
		envPopupStyle.CornerRadiusTopRight = 4;
		envPopupStyle.CornerRadiusBottomLeft = 4;
		envPopupStyle.CornerRadiusBottomRight = 4;
		envPopupStyle.ContentMarginLeft = 10;
		envPopupStyle.ContentMarginRight = 10;
		envPopupStyle.ContentMarginTop = 10;
		envPopupStyle.ContentMarginBottom = 10;
		_popupEnvironment.AddThemeStyleboxOverride("panel", envPopupStyle);

		var envVBox = new VBoxContainer();
		envVBox.Name = "EnvVBox";
		envVBox.AddThemeConstantOverride("separation", 6);
		envVBox.CustomMinimumSize = new Vector2(170, 0);

		_optEnvLighting = new OptionButton();
		_optEnvLighting.Name = "OptEnvLighting";
		_optEnvLighting.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
		_optEnvLighting.CustomMinimumSize = new Vector2(0, 24);
		_optEnvLighting.ClipText = true;
		SetupEnvLightingDropdown(_optEnvLighting);
		_optEnvLighting.ItemSelected += (idx) => SetEnvironmentLighting((int)idx);
		envVBox.AddChild(_optEnvLighting);

		_optEnvWeather = new OptionButton();
		_optEnvWeather.Name = "OptEnvWeather";
		_optEnvWeather.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
		_optEnvWeather.CustomMinimumSize = new Vector2(0, 24);
		_optEnvWeather.ClipText = true;
		SetupEnvWeatherDropdown(_optEnvWeather);
		_optEnvWeather.ItemSelected += (idx) => SetEnvironmentWeather((int)idx);
		envVBox.AddChild(_optEnvWeather);

		_chkEnvShadows = new CheckBox();
		_chkEnvShadows.Name = "ChkEnvShadows";
		_chkEnvShadows.Text = TranslationServer.Translate("Shadows");
		_chkEnvShadows.TooltipText = TranslationServer.Translate("Toggle shadows in editor (F9)");
		_chkEnvShadows.ButtonPressed = true;
		_chkEnvShadows.FocusMode = Control.FocusModeEnum.None;
		_chkEnvShadows.AddThemeFontSizeOverride("font_size", 11);
		UIStyle.ApplyCheckboxStyle(_chkEnvShadows);
		_chkEnvShadows.Toggled += (pressed) =>
		{
			if (GameHost.Instance != null)
			{
				bool disableShadows = !pressed;
				if (GameHost.Instance.EditorDisableShadows != disableShadows)
				{
					GameHost.Instance.EditorDisableShadows = disableShadows;
					GameHost.Instance.UpdateEditorShadows();
					ShowFeedback(disableShadows
						? TranslationServer.Translate("Shadows: OFF")
						: TranslationServer.Translate("Shadows: ON"));
				}
			}
		};
		envVBox.AddChild(_chkEnvShadows);

		_popupEnvironment.AddChild(envVBox);
		AddChild(_popupEnvironment);

		_btnToggleEnvironment = GetNodeOrNull<Button>("LeftSlidePanel/LeftScroll/LeftVBox/ViewportAccordion/ContentViewport/BtnToggleEnvironment") ?? new Button();
		_btnToggleEnvironment.Name = "BtnToggleEnvironment";
		_btnToggleEnvironment.Set("icon_max_width", 0);
		SetupButton(_btnToggleEnvironment, "\uf185", () => OpenEnvironmentPopup(), 12, "Environment");

		_popupCamera = new PopupPanel();
		_popupCamera.Name = "PopupCamera";

		var camPopupStyle = new StyleBoxFlat();
		camPopupStyle.BgColor = new Color(0.14f, 0.13f, 0.11f, 0.98f);
		camPopupStyle.BorderColor = UIStyle.ColorGold;
		camPopupStyle.SetBorderWidthAll(1);
		camPopupStyle.CornerRadiusTopLeft = 4;
		camPopupStyle.CornerRadiusTopRight = 4;
		camPopupStyle.CornerRadiusBottomLeft = 4;
		camPopupStyle.CornerRadiusBottomRight = 4;
		camPopupStyle.ContentMarginLeft = 8;
		camPopupStyle.ContentMarginRight = 8;
		camPopupStyle.ContentMarginTop = 8;
		camPopupStyle.ContentMarginBottom = 8;
		_popupCamera.AddThemeStyleboxOverride("panel", camPopupStyle);

		var camVBox = new VBoxContainer();
		camVBox.Name = "CamVBox";
		camVBox.AddThemeConstantOverride("separation", 6);
		camVBox.CustomMinimumSize = new Vector2(170, 0);

		_btnRotate = new Button();
		_btnRotate.Name = "BtnRotate";
		_btnRotate.Set("icon_max_width", 0);
		StylePopupButton(_btnRotate, "\uf01e Rotate 90° (R)", "Rotate camera 90 degrees (R)");
		_btnRotate.Pressed += () =>
		{
			UIManager.Instance?.PlayClickSound();
			var camera = (GameHost.Instance?.MainCamera as CameraControl);
			camera?.Rotate90Degrees();
		};
		camVBox.AddChild(_btnRotate);

		_btnCameraAngle = new Button();
		_btnCameraAngle.Name = "BtnCameraAngle";
		_btnCameraAngle.Set("icon_max_width", 0);
		StylePopupButton(_btnCameraAngle, "\uf1b2 Top-Down (C)", "Toggle perspective vs top-down angle (C)");
		_btnCameraAngle.Pressed += () =>
		{
			var camera = (GameHost.Instance?.MainCamera as CameraControl);
			camera?.ToggleTopDown();
			if (camera != null)
			{
				UpdateCameraAngleButtonText(camera.IsTopDown());
			}
		};
		camVBox.AddChild(_btnCameraAngle);

		_btnZoomIn = new Button();
		_btnZoomIn.Name = "BtnZoomIn";
		_btnZoomIn.Set("icon_max_width", 0);
		StylePopupButton(_btnZoomIn, "\uf00e Zoom In (+)", "Zoom camera in (+)");
		_btnZoomIn.Pressed += () =>
		{
			UIManager.Instance?.PlayClickSound();
			(GameHost.Instance?.MainCamera as CameraControl)?.ZoomIn();
		};
		camVBox.AddChild(_btnZoomIn);

		_btnZoomOut = new Button();
		_btnZoomOut.Name = "BtnZoomOut";
		_btnZoomOut.Set("icon_max_width", 0);
		StylePopupButton(_btnZoomOut, "\uf010 Zoom Out (-)", "Zoom camera out (-)");
		_btnZoomOut.Pressed += () =>
		{
			UIManager.Instance?.PlayClickSound();
			(GameHost.Instance?.MainCamera as CameraControl)?.ZoomOut();
		};
		camVBox.AddChild(_btnZoomOut);

		_chkFreeCamera = new CheckBox();
		_chkFreeCamera.Name = "ChkFreeCamera";
		_chkFreeCamera.Text = TranslationServer.Translate("Free Camera (F8)");
		_chkFreeCamera.TooltipText = TranslationServer.Translate("Toggle free camera mode (F8)");
		_chkFreeCamera.FocusMode = Control.FocusModeEnum.None;
		_chkFreeCamera.AddThemeFontSizeOverride("font_size", 11);
		UIStyle.ApplyCheckboxStyle(_chkFreeCamera);
		_chkFreeCamera.Toggled += (pressed) =>
		{
			var cam = GameHost.Instance?.MainCamera as CameraControl;
			if (cam != null && cam.IsFreeCamera != pressed)
			{
				ToggleFreeCamera();
			}
		};
		camVBox.AddChild(_chkFreeCamera);

		_popupCamera.AddChild(camVBox);
		AddChild(_popupCamera);

		_btnToggleCamera = GetNodeOrNull<Button>("LeftSlidePanel/LeftScroll/LeftVBox/ViewportAccordion/ContentViewport/BtnToggleCamera") ?? new Button();
		_btnToggleCamera.Name = "BtnToggleCamera";
		_btnToggleCamera.Set("icon_max_width", 0);
		SetupButton(_btnToggleCamera, "\uf030", () => OpenCameraPopup(), 12, "Configure camera controls (R / C / + / - / F8)");

		var initialCam = GameHost.Instance?.MainCamera as CameraControl;
		UpdateFreeCameraExternal(initialCam != null && initialCam.IsFreeCamera);
		UpdateShadowsExternal(GameHost.Instance?.EditorDisableShadows ?? false);

		_minimapFrame = GetNode<PanelContainer>("LeftSlidePanel/LeftScroll/LeftVBox/ViewportAccordion/ContentViewport/MinimapFrame");
		_minimapArea = GetNode<Control>("LeftSlidePanel/LeftScroll/LeftVBox/ViewportAccordion/ContentViewport/MinimapFrame/MinimapArea");
		_cameraIndicator = GetNode<MapEditorCameraIndicator>("LeftSlidePanel/LeftScroll/LeftVBox/ViewportAccordion/ContentViewport/MinimapFrame/MinimapArea/CameraIndicator");

		_mapSettingsDialog = new MapSettingsDialog(this);
		AddChild(_mapSettingsDialog);

		_btnTapeMeasure = new Button();
		_btnTapeMeasure.Name = "BtnTapeMeasure";
		_btnTapeMeasure.Set("icon_max_width", 0);
		SetupButton(_btnTapeMeasure, "\uf545", () =>
		{
			if (GameHost.Instance != null)
			{
				if (GameHost.Instance.ActiveEditorTool == GameHost.EditorTool.Measure)
				{
					GameHost.Instance.ClearMeasureVisuals();
					ClearMeasureTelemetry();
					TriggerToolSelection(SavedActiveTool != GameHost.EditorTool.Measure ? SavedActiveTool : GameHost.EditorTool.Raise, null);
				}
				else
				{
					TriggerToolSelection(GameHost.EditorTool.Measure, _btnTapeMeasure);
				}
			}
		}, 12, "Tape Measure Tool: Click Point A, then Point B to measure distance & slope (U)");

		_optPolarRingSpacing = new OptionButton();
		_optPolarRingSpacing.Name = "OptPolarRingSpacing";
		_optPolarRingSpacing.Set("icon_max_width", 0);
		SetupPolarRingSpacingDropdown(_optPolarRingSpacing, "Select interval between concentric polar distance rings (4, 8, 16, 32 tiles)");

		_optPolarRadialStep = new OptionButton();
		_optPolarRadialStep.Name = "OptPolarRadialStep";
		_optPolarRadialStep.Set("icon_max_width", 0);
		SetupSpokesDropdown(_optPolarRadialStep, (spokes) => SetPolarSpokes(spokes), "Select radial spoke count (2, 3, 4, 5, 6, 8, 12, 16)");

		_btnResetPivotToCenter = new Button();
		_btnResetPivotToCenter.Name = "BtnResetPivotToCenter";
		_btnResetPivotToCenter.Set("icon_max_width", 0);
		SetupOptionButton(_btnResetPivotToCenter, "\uf05b RESET PIVOT", () => ResetPivotToMapCenter(), 10, "Reset symmetry and polar overlay center pivot to true map center");

		var bottomBar = new HBoxContainer();
		bottomBar.Name = "BottomCenterBar";
		bottomBar.SetAnchorsPreset(LayoutPreset.CenterBottom);
		bottomBar.GrowHorizontal = GrowDirection.Both;
		bottomBar.GrowVertical = GrowDirection.Begin;
		bottomBar.OffsetTop = -65;
		bottomBar.OffsetBottom = -10;
		bottomBar.Alignment = BoxContainer.AlignmentMode.Center;
		bottomBar.AddThemeConstantOverride("separation", 10);
		bottomBar.MouseFilter = Control.MouseFilterEnum.Ignore;
		AddChild(bottomBar);

		if (_topBar != null)
		{
			_topBar.CustomMinimumSize = new Vector2(0, 44);
			_topBar.SizeFlagsVertical = SizeFlags.ShrinkCenter;
			_topBar.SizeFlagsHorizontal = SizeFlags.ShrinkCenter;
			SafeReparent(_topBar, bottomBar);
		}

		_panelMeasurementHUD = new PanelContainer();
		_panelMeasurementHUD.Name = "MeasurementHUD";
		_panelMeasurementHUD.CustomMinimumSize = new Vector2(0, 44);
		_panelMeasurementHUD.SizeFlagsVertical = SizeFlags.ShrinkCenter;
		_panelMeasurementHUD.SizeFlagsHorizontal = SizeFlags.ShrinkCenter;
		_panelMeasurementHUD.MouseFilter = Control.MouseFilterEnum.Ignore;
		_panelMeasurementHUD.Visible = false;

		var measureHBox = new HBoxContainer();
		measureHBox.Name = "MeasureHBox";
		measureHBox.Alignment = BoxContainer.AlignmentMode.Center;
		measureHBox.AddThemeConstantOverride("separation", 8);
		measureHBox.MouseFilter = Control.MouseFilterEnum.Ignore;

		_lblMeasureTelemetry = new Label();
		_lblMeasureTelemetry.Name = "LblMeasureTelemetry";
		_lblMeasureTelemetry.HorizontalAlignment = HorizontalAlignment.Center;
		_lblMeasureTelemetry.VerticalAlignment = VerticalAlignment.Center;
		_lblMeasureTelemetry.AddThemeFontSizeOverride("font_size", 13);
		_lblMeasureTelemetry.AddThemeColorOverride("font_color", UIStyle.ColorGold);
		var fontStatus = GetFontAwesomeFont();
		if (fontStatus != null)
		{
			_lblMeasureTelemetry.AddThemeFontOverride("font", fontStatus);
		}
		measureHBox.AddChild(_lblMeasureTelemetry);
		_panelMeasurementHUD.AddChild(measureHBox);

		bottomBar.AddChild(_panelMeasurementHUD);

		ApplyThemeStyles();
		SetupLightingTuningUI();

		// Right Accordions
		_accordionTool = GetNode<VBoxContainer>("RightSlidePanel/RightScroll/AccordionContainer/ToolAccordion");
		_btnHeaderTool = GetNode<Button>("RightSlidePanel/RightScroll/AccordionContainer/ToolAccordion/BtnHeaderTool");
		_contentTool = GetNode<VBoxContainer>("RightSlidePanel/RightScroll/AccordionContainer/ToolAccordion/ContentTool");
		StyleAccordionHeader(_btnHeaderTool);
		SetupAccordion(_btnHeaderTool, _contentTool, TranslationServer.Translate("Tool"));

		_panelTerrainVBox = GetNode<VBoxContainer>("RightSlidePanel/RightScroll/AccordionContainer/ToolAccordion/ContentTool/PanelTerrainVBox");
		_panelDecoVBox = GetNode<VBoxContainer>("RightSlidePanel/RightScroll/AccordionContainer/ToolAccordion/ContentTool/PanelDecoVBox");
		_panelPathingVBox = GetNode<VBoxContainer>("RightSlidePanel/RightScroll/AccordionContainer/ToolAccordion/ContentTool/PanelPathingVBox");
		_panelCoordinatesVBox = GetNode<VBoxContainer>("RightSlidePanel/RightScroll/AccordionContainer/ToolAccordion/ContentTool/PanelCoordinatesVBox");
		_panelObjects = GetNode<VBoxContainer>("RightSlidePanel/RightScroll/AccordionContainer/ToolAccordion/ContentTool/PanelObjectsVBox");
		_panelClipboard = GetNode<VBoxContainer>("RightSlidePanel/RightScroll/AccordionContainer/ToolAccordion/ContentTool/PanelClipboard");

		_btnRaise = GetNode<Button>("RightSlidePanel/RightScroll/AccordionContainer/ToolAccordion/ContentTool/PanelTerrainVBox/BtnRaise");
		_cardRaise = CreateToolCard(_btnRaise, "\uf062", "Raise", () => TriggerToolSelection(GameHost.EditorTool.Raise, _btnRaise), "Elevate terrain height (1)");

		_btnLower = GetNode<Button>("RightSlidePanel/RightScroll/AccordionContainer/ToolAccordion/ContentTool/PanelTerrainVBox/BtnLower");
		_cardLower = CreateToolCard(_btnLower, "\uf063", "Lower", () => TriggerToolSelection(GameHost.EditorTool.Lower, _btnLower), "Lower terrain height (2)");

		_btnHeight = GetNode<Button>("RightSlidePanel/RightScroll/AccordionContainer/ToolAccordion/ContentTool/PanelTerrainVBox/BtnHeight");
		_cardHeight = CreateToolCard(_btnHeight, "\uf07d", "Height", () => TriggerToolSelection(GameHost.EditorTool.Height, _btnHeight), "Set terrain to exact height (3)");

		_btnSmooth = GetNode<Button>("RightSlidePanel/RightScroll/AccordionContainer/ToolAccordion/ContentTool/PanelTerrainVBox/BtnSmooth");
		_cardSmooth = CreateToolCard(_btnSmooth, "\uf043", "Smooth", () => TriggerToolSelection(GameHost.EditorTool.Smooth, _btnSmooth), "Smooth terrain height (4)");

		_btnPlateau = GetNode<Button>("RightSlidePanel/RightScroll/AccordionContainer/ToolAccordion/ContentTool/PanelTerrainVBox/BtnPlateau");
		_cardPlateau = CreateToolCard(_btnPlateau, "\uf0c8", "Flatten", () => TriggerToolSelection(GameHost.EditorTool.Plateau, _btnPlateau), "Flatten terrain to cursor height on click (5)");

		_btnRamp = GetNode<Button>("RightSlidePanel/RightScroll/AccordionContainer/ToolAccordion/ContentTool/PanelTerrainVBox/BtnRamp");
		_cardRamp = CreateToolCard(_btnRamp, "\uf542", "Ramp", () => TriggerToolSelection(GameHost.EditorTool.Ramp, _btnRamp), "Create ramp between two points (6)");

		_btnNoise = GetNode<Button>("RightSlidePanel/RightScroll/AccordionContainer/ToolAccordion/ContentTool/PanelTerrainVBox/BtnNoise");
		_cardNoise = CreateToolCard(_btnNoise, "\uf6d9", "Noise", () => TriggerToolSelection(GameHost.EditorTool.Noise, _btnNoise), "Add random height variations/noise to terrain (7)");

		_btnWater = new Button();
		_btnWater.Name = "BtnWater";
		_cardWater = CreateToolCard(_btnWater, "\uf773", "Water", () => TriggerToolSelection(GameHost.EditorTool.Water, _btnWater), "Flood fill water mesh bounded by cliff walls");

		_btnTextureBrush = GetNode<Button>("RightSlidePanel/RightScroll/AccordionContainer/ToolAccordion/ContentTool/PanelDecoVBox/BtnTextureBrush");
		_cardTextureBrush = CreateToolCard(_btnTextureBrush, "\uf1fc", "Paint", () => TriggerToolSelection(GameHost.EditorTool.PaintTexture, _btnTextureBrush), "Paint terrain texture (8)");

		_btnFloodFill = GetNode<Button>("RightSlidePanel/RightScroll/AccordionContainer/ToolAccordion/ContentTool/PanelDecoVBox/BtnFloodFill");
		_cardFloodFill = CreateToolCard(_btnFloodFill, "\uf576", "Flood Fill", () => TriggerToolSelection(GameHost.EditorTool.FloodFill, _btnFloodFill), "Flood fill terrain texture");

		_btnSelectArea = GetNode<Button>("RightSlidePanel/RightScroll/AccordionContainer/ToolAccordion/ContentTool/PanelClipboard/BtnSelectArea");
		_cardSelectArea = CreateToolCard(_btnSelectArea, "\uf065", "Select Area", () => TriggerToolSelection(GameHost.EditorTool.SelectArea, _btnSelectArea), "Select rectangular area");

		_btnPathingBrush = GetNode<Button>("RightSlidePanel/RightScroll/AccordionContainer/ToolAccordion/ContentTool/PanelPathingVBox/BtnPathingBrush");
		_cardPathingBrush = CreateToolCard(_btnPathingBrush, "\uf54b", "Brush", () => TriggerToolSelection(GameHost.EditorTool.PaintPathing, _btnPathingBrush), "Paint pathing attributes onto the terrain map");

		_btnFloodFillPathing = GetNode<Button>("RightSlidePanel/RightScroll/AccordionContainer/ToolAccordion/ContentTool/PanelPathingVBox/BtnFloodFillPathing");
		_cardFloodFillPathing = CreateToolCard(_btnFloodFillPathing, "\uf576", "Flood Fill", () => TriggerToolSelection(GameHost.EditorTool.FloodFillPathing, _btnFloodFillPathing), "Flood fill pathing attributes onto the terrain map");

		_btnAddObject = GetNode<Button>("RightSlidePanel/RightScroll/AccordionContainer/ToolAccordion/ContentTool/PanelObjectsVBox/BtnAddObject");
		_cardAddObject = CreateToolCard(_btnAddObject, "\uf1b2", "Add Object", () => _entityPaletteController?.TriggerAddObjectMode(), "Place units, props, or decals");

		_btnSelectMove = GetNode<Button>("RightSlidePanel/RightScroll/AccordionContainer/ToolAccordion/ContentTool/PanelObjectsVBox/BtnSelectMove");
		_cardSelectMove = CreateToolCard(_btnSelectMove, "\uf0b2", "Select/Move", () => TriggerToolSelection(GameHost.EditorTool.SelectMove, _btnSelectMove), "Select and move units, props, or decals");

		_btnDeleteObject = GetNode<Button>("RightSlidePanel/RightScroll/AccordionContainer/ToolAccordion/ContentTool/PanelObjectsVBox/BtnDeleteObject");
		_cardDeleteObject = CreateToolCard(_btnDeleteObject, "\uf12d", "Erase", () =>
		{
			if (GodotObject.IsInstanceValid(GameHost.Instance?.SelectedEditorObject))
				DeleteSelectedObjectAction();
			else
				TriggerToolSelection(GameHost.EditorTool.DeleteObject, _btnDeleteObject);
		}, "Erase units, props, or decals");

		_btnDrawCoordinate = GetNode<Button>("RightSlidePanel/RightScroll/AccordionContainer/ToolAccordion/ContentTool/PanelCoordinatesVBox/BtnDrawCoordinate");
		SetupButton(_btnDrawCoordinate, "\uf303 DRAW COORD", () => TriggerToolSelection(GameHost.EditorTool.DrawCoordinate, _btnDrawCoordinate), 11, "Drag to define a named coordinate box exposed as C# variables");

		_txtCoordinateName = GetNode<LineEdit>("RightSlidePanel/RightScroll/AccordionContainer/ToolAccordion/ContentTool/PanelCoordinatesVBox/CoordinateNameRow/TxtCoordinateName");
		_btnCommitCoordinate = GetNode<Button>("RightSlidePanel/RightScroll/AccordionContainer/ToolAccordion/ContentTool/PanelCoordinatesVBox/BtnCommitCoordinate");
		SetupButton(_btnCommitCoordinate, "+ ADD", null, 11, "Create named coordinate");
		_btnCommitCoordinate.Pressed += () =>
		{
			if (_pendingCoordinateMinX == _pendingCoordinateMaxX && _pendingCoordinateMinZ == _pendingCoordinateMaxZ)
			{
				ShowFeedback("Select a valid area first by dragging.");
				return;
			}
			string name = _txtCoordinateName?.Text ?? "";
			if (string.IsNullOrWhiteSpace(name))
			{
				ShowFeedback("Enter a coordinate name before creating.");
				return;
			}
			bool ok = GameHost.Instance?.CommitCoordinateExternal(name, _pendingCoordinateMinX, _pendingCoordinateMinZ, _pendingCoordinateMaxX, _pendingCoordinateMaxZ) ?? false;
			if (ok)
			{
				RefreshCoordinateListExternal();
				ShowFeedback($"Coordinate '{name}' created.");
				_txtCoordinateName.Text = "";
			}
		};
		_coordinateListVBox = GetNode<VBoxContainer>("RightSlidePanel/RightScroll/AccordionContainer/ToolAccordion/ContentTool/PanelCoordinatesVBox/CoordinateListVBox");

		_btnCopy = GetNode<Button>("RightSlidePanel/RightScroll/AccordionContainer/ToolAccordion/ContentTool/PanelClipboard/BtnCopy");
		_cardCopy = CreateToolCard(_btnCopy, "\uf0c5", "Copy", () => GameHost.Instance?.PerformCopyAreaExternal(), "Copy selected area to clipboard (Ctrl+C)");

		_btnPaste = GetNode<Button>("RightSlidePanel/RightScroll/AccordionContainer/ToolAccordion/ContentTool/PanelClipboard/BtnPaste");
		_cardPaste = CreateToolCard(_btnPaste, "\uf0ea", "Paste", () => TriggerToolSelection(GameHost.EditorTool.PasteArea, _btnPaste), "Paste clipboard contents onto terrain (Ctrl+V)");

		_btnCut = GetNode<Button>("RightSlidePanel/RightScroll/AccordionContainer/ToolAccordion/ContentTool/PanelClipboard/BtnCut");
		_cardCut = CreateToolCard(_btnCut, "\uf0c4", "Cut", () => GameHost.Instance?.PerformCutAreaExternal(), "Cut selected area to clipboard (Ctrl+X)");

		_btnEraseArea = GetNode<Button>("RightSlidePanel/RightScroll/AccordionContainer/ToolAccordion/ContentTool/PanelClipboard/BtnEraseArea");
		_cardEraseArea = CreateToolCard(_btnEraseArea, "\uf12d", "Erase Area", () => GameHost.Instance?.PerformEraseAreaExternal(), "Erase heights, textures and objects within selection (Delete)");

		_accordionBrush = GetNode<VBoxContainer>("RightSlidePanel/RightScroll/AccordionContainer/BrushAccordion");
		_btnHeaderBrush = GetNode<Button>("RightSlidePanel/RightScroll/AccordionContainer/BrushAccordion/BtnHeaderBrush");
		_contentBrush = GetNode<VBoxContainer>("RightSlidePanel/RightScroll/AccordionContainer/BrushAccordion/ContentBrush");
		StyleAccordionHeader(_btnHeaderBrush);
		SetupAccordion(_btnHeaderBrush, _contentBrush, TranslationServer.Translate("Global Brush Properties"));

		_sldBrushSize = GetNode<Slider>("RightSlidePanel/RightScroll/AccordionContainer/BrushAccordion/ContentBrush/BrushSizeBox/SldBrushSize");
		_lblBrushSizeValue = GetNode<Label>("RightSlidePanel/RightScroll/AccordionContainer/BrushAccordion/ContentBrush/BrushSizeBox/Header/LblBrushSizeValue");
		_sldBrushSize.DragStarted += () => _isDraggingSlider = true;
		_sldBrushSize.DragEnded += (valueChanged) => _isDraggingSlider = false;

		_sldBrushStrength = GetNode<Slider>("RightSlidePanel/RightScroll/AccordionContainer/BrushAccordion/ContentBrush/BrushStrengthBox/SldBrushStrength");
		_lblBrushStrengthValue = GetNode<Label>("RightSlidePanel/RightScroll/AccordionContainer/BrushAccordion/ContentBrush/BrushStrengthBox/Header/LblBrushStrengthValue");
		_sldBrushStrength.DragStarted += () => _isDraggingSlider = true;
		_sldBrushStrength.DragEnded += (valueChanged) => _isDraggingSlider = false;

		_btnBrushShape = GetNode<Button>("RightSlidePanel/RightScroll/AccordionContainer/BrushAccordion/ContentBrush/BtnBrushShape");
		SetupOptionButton(_btnBrushShape, "\uf0c8 BRUSH: SQUARE", () =>
		{
			if (GameHost.Instance != null)
			{
				GameHost.Instance.EditorBrushIsSquare = !GameHost.Instance.EditorBrushIsSquare;
				GameHost.Instance.UpdateBrushMesh();
				UpdateBrushShapeExternal(GameHost.Instance.EditorBrushIsSquare);
			}
		}, 11, "Toggle brush shape between circular and square (B)");

		_optMirrorMode = GetNode<OptionButton>("RightSlidePanel/RightScroll/AccordionContainer/BrushAccordion/ContentBrush/OptMirrorMode");
		SetupMirrorDropdown(_optMirrorMode, "Select terrain and object mirroring symmetry mode");

		_chkBlockMode = GetNode<CheckBox>("RightSlidePanel/RightScroll/AccordionContainer/BrushAccordion/ContentBrush/ChkBlockMode");
		_chkBlockMode.Toggled += (toggled) =>
		{
			if (GameHost.Instance != null)
				GameHost.Instance.EditorBlockMode = toggled;

			ShowFeedback(toggled ? "Block Mode: Enabled" : "Block Mode: Disabled");
			UpdateBlockStepVisibility();
			UpdateBrushStrengthVisibility();
			if (GameHost.Instance != null)
				UpdateSidebarMorph(GameHost.Instance.ActiveEditorTool);
		};
		_chkBlockMode.ButtonPressed = true;

		_stepBox = GetNode<Control>("RightSlidePanel/RightScroll/AccordionContainer/BrushAccordion/ContentBrush/StepBox");
		_sldBlockStep = GetNode<Slider>("RightSlidePanel/RightScroll/AccordionContainer/BrushAccordion/ContentBrush/StepBox/SldBlockStep");
		_sldBlockStep.DragStarted += () => _isDraggingSlider = true;
		_sldBlockStep.DragEnded += (valueChanged) => _isDraggingSlider = false;
		_lblBlockStepValue = GetNode<Label>("RightSlidePanel/RightScroll/AccordionContainer/BrushAccordion/ContentBrush/StepBox/Header/LblBlockStepValue");
		var lblStepTitle = GetNodeOrNull<Label>("RightSlidePanel/RightScroll/AccordionContainer/BrushAccordion/ContentBrush/StepBox/Header/LblStepTitle");
		if (lblStepTitle != null) lblStepTitle.Text = TranslationServer.Translate("Step Height");

		_heightBox = GetNode<Control>("RightSlidePanel/RightScroll/AccordionContainer/BrushAccordion/ContentBrush/HeightBox");
		_sldHeight = GetNode<Slider>("RightSlidePanel/RightScroll/AccordionContainer/BrushAccordion/ContentBrush/HeightBox/SldHeight");
		_sldHeight.MinValue = TerrainCell.MIN_Y;
		_sldHeight.MaxValue = TerrainCell.MAX_Y;
		_sldHeight.DragStarted += () => _isDraggingSlider = true;
		_sldHeight.DragEnded += (valueChanged) => _isDraggingSlider = false;
		_lblHeightValue = GetNode<Label>("RightSlidePanel/RightScroll/AccordionContainer/BrushAccordion/ContentBrush/HeightBox/Header/LblHeightValue");
		var lblHeightTitle = GetNodeOrNull<Label>("RightSlidePanel/RightScroll/AccordionContainer/BrushAccordion/ContentBrush/HeightBox/Header/LblHeightTitle");
		if (lblHeightTitle != null) lblHeightTitle.Text = TranslationServer.Translate("Height");

		_accordionWater = new VBoxContainer();
		_accordionWater.Name = "WaterAccordion";
		_btnHeaderWater = new Button();
		_btnHeaderWater.Name = "BtnHeaderWater";
		_contentWater = new VBoxContainer();
		_contentWater.Name = "ContentWater";
		_accordionWater.AddChild(_btnHeaderWater);
		_accordionWater.AddChild(_contentWater);

		var mainAccordionContainer = GetNodeOrNull<VBoxContainer>("RightSlidePanel/RightScroll/AccordionContainer");
		if (mainAccordionContainer != null)
		{
			mainAccordionContainer.AddChild(_accordionWater);
			if (_accordionBrush != null)
			{
				mainAccordionContainer.MoveChild(_accordionWater, _accordionBrush.GetIndex() + 1);
			}
		}

		StyleAccordionHeader(_btnHeaderWater);
		SetupAccordion(_btnHeaderWater, _contentWater, TranslationServer.Translate("Liquid / Water Config"));

		_waterHeightBox = new VBoxContainer();
		_waterHeightBox.Name = "WaterHeightBox";
		_waterHeightBox.AddThemeConstantOverride("separation", 2);

		var headerWaterHeight = new HBoxContainer();
		headerWaterHeight.Name = "HeaderWaterHeight";
		_waterHeightBox.AddChild(headerWaterHeight);

		var lblWaterHeightTitle = new Label();
		lblWaterHeightTitle.Name = "LblWaterHeightTitle";
		lblWaterHeightTitle.Text = TranslationServer.Translate("Water Height");
		lblWaterHeightTitle.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
		lblWaterHeightTitle.AddThemeFontSizeOverride("font_size", 10);
		headerWaterHeight.AddChild(lblWaterHeightTitle);

		_lblWaterHeightValue = new Label();
		_lblWaterHeightValue.Name = "LblWaterHeightValue";
		_lblWaterHeightValue.Text = "0.9m";
		_lblWaterHeightValue.AddThemeFontSizeOverride("font_size", 11);
		headerWaterHeight.AddChild(_lblWaterHeightValue);

		_sldWaterHeight = new HSlider();
		_sldWaterHeight.Name = "SldWaterHeight";
		_sldWaterHeight.FocusMode = Control.FocusModeEnum.None;
		_sldWaterHeight.MinValue = 0.1;
		_sldWaterHeight.MaxValue = 15.0;
		_sldWaterHeight.Step = 0.1;
		_sldWaterHeight.Value = 0.9;
		_sldWaterHeight.ValueChanged += (val) =>
		{
			float fVal = (float)val;
			_lblWaterHeightValue.Text = fVal.ToString("F1") + "m";
			if (GameHost.Instance != null) GameHost.Instance.EditorWaterHeight = fVal;
		};
		_sldWaterHeight.DragStarted += () => _isDraggingSlider = true;
		_sldWaterHeight.DragEnded += (valueChanged) => _isDraggingSlider = false;
		_waterHeightBox.AddChild(_sldWaterHeight);

		_contentWater.AddChild(_waterHeightBox);

		var waterActionBox = new VBoxContainer();
		waterActionBox.Name = "WaterActionBox";
		waterActionBox.AddThemeConstantOverride("separation", 2);

		var lblWaterAction = new Label();
		lblWaterAction.Name = "LblWaterActionTitle";
		lblWaterAction.Text = TranslationServer.Translate("Action");
		lblWaterAction.AddThemeFontSizeOverride("font_size", 10);
		waterActionBox.AddChild(lblWaterAction);

		var waterActionButtonRow = new HBoxContainer();
		waterActionButtonRow.Name = "WaterActionButtonRow";
		waterActionButtonRow.AddThemeConstantOverride("separation", 4);
		waterActionBox.AddChild(waterActionButtonRow);

		_btnWaterActionAdd = new Button();
		_btnWaterActionAdd.Name = "BtnWaterActionAdd";
		_btnWaterActionAdd.Set("icon_max_width", 0);
		_btnWaterActionAdd.Text = "+ " + TranslationServer.Translate("Add");
		_btnWaterActionAdd.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
		_btnWaterActionAdd.FocusMode = Control.FocusModeEnum.None;
		_btnWaterActionAdd.AddThemeFontSizeOverride("font_size", 11);
		_btnWaterActionAdd.TooltipText = TranslationServer.Translate("Add water to terrain");
		StyleRowButton(_btnWaterActionAdd);
		_btnWaterActionAdd.AddThemeStyleboxOverride("normal", _highlightStyle);
		waterActionButtonRow.AddChild(_btnWaterActionAdd);

		_btnWaterActionRemove = new Button();
		_btnWaterActionRemove.Name = "BtnWaterActionRemove";
		_btnWaterActionRemove.Set("icon_max_width", 0);
		_btnWaterActionRemove.Text = "- " + TranslationServer.Translate("Remove");
		_btnWaterActionRemove.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
		_btnWaterActionRemove.FocusMode = Control.FocusModeEnum.None;
		_btnWaterActionRemove.AddThemeFontSizeOverride("font_size", 11);
		_btnWaterActionRemove.TooltipText = TranslationServer.Translate("Remove water from terrain");
		StyleRowButton(_btnWaterActionRemove);
		waterActionButtonRow.AddChild(_btnWaterActionRemove);

		_btnWaterActionAdd.Pressed += () =>
		{
			_isWaterRemoveAction = false;
			_btnWaterActionAdd.AddThemeStyleboxOverride("normal", _highlightStyle);
			_btnWaterActionRemove.RemoveThemeStyleboxOverride("normal");
			if (_waterHeightBox != null) _waterHeightBox.Visible = true;
			if (_waterModeBox != null) _waterModeBox.Visible = true;
			if (_btnWaterProfiles != null) _btnWaterProfiles.Visible = true;
		};

		_btnWaterActionRemove.Pressed += () =>
		{
			_isWaterRemoveAction = true;
			_btnWaterActionRemove.AddThemeStyleboxOverride("normal", _highlightStyle);
			_btnWaterActionAdd.RemoveThemeStyleboxOverride("normal");
			if (_waterHeightBox != null) _waterHeightBox.Visible = false;
			if (_waterModeBox != null) _waterModeBox.Visible = false;
			if (_btnWaterProfiles != null) _btnWaterProfiles.Visible = false;
		};

		_contentWater.AddChild(waterActionBox);

		_waterModeBox = new VBoxContainer();
		_waterModeBox.Name = "WaterModeBox";
		_waterModeBox.AddThemeConstantOverride("separation", 2);

		_optWaterMode = new OptionButton();
		_optWaterMode.Name = "OptWaterMode";
		_optWaterMode.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
		_optWaterMode.ClipText = true;
		_optWaterMode.CustomMinimumSize = new Vector2(0, 24);
		_waterModeBox.AddChild(_optWaterMode);

		_optWaterMode.ItemSelected += (idx) =>
		{
			byte profIdx = 0;
			var meta = _optWaterMode.GetItemMetadata((int)idx);
			if (meta.VariantType != Variant.Type.Nil)
			{
				profIdx = (byte)(int)meta;
			}
			if (GameHost.Instance != null)
			{
				GameHost.Instance.EditorWaterMode = WaterType.Shallow;
				GameHost.Instance.ActiveWaterProfileIndex = profIdx;
			}
		};
		_contentWater.AddChild(_waterModeBox);
		RefreshWaterSwatches();

		_btnWaterProfiles = new Button();
		_btnWaterProfiles.Name = "BtnWaterProfiles";
		_btnWaterProfiles.Set("icon_max_width", 0);
		_btnWaterProfiles.Text = "⚙ " + TranslationServer.Translate("Water Profiles");
		_btnWaterProfiles.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
		_btnWaterProfiles.TooltipText = TranslationServer.Translate("Configure Liquid / Water Uber Profiles");
		_btnWaterProfiles.FocusMode = Control.FocusModeEnum.None;
		_btnWaterProfiles.AddThemeFontSizeOverride("font_size", 11);
		_btnWaterProfiles.Pressed += () => OpenWaterProfileDialog();
		_contentWater.AddChild(_btnWaterProfiles);

		_accordionToolSettings = GetNode<VBoxContainer>("RightSlidePanel/RightScroll/AccordionContainer/ToolSettingsAccordion");
		_btnHeaderToolSettings = GetNode<Button>("RightSlidePanel/RightScroll/AccordionContainer/ToolSettingsAccordion/BtnHeaderToolSettings");
		_contentToolSettings = GetNode<VBoxContainer>("RightSlidePanel/RightScroll/AccordionContainer/ToolSettingsAccordion/ContentToolSettings");
		StyleAccordionHeader(_btnHeaderToolSettings);
		SetupAccordion(_btnHeaderToolSettings, _contentToolSettings, TranslationServer.Translate("Tool Settings"));

		_containerTextureSettings = GetNode<VBoxContainer>("RightSlidePanel/RightScroll/AccordionContainer/ToolSettingsAccordion/ContentToolSettings/ContainerTexture");
		_lblTerrainTexture = GetNode<Label>("RightSlidePanel/RightScroll/AccordionContainer/ToolSettingsAccordion/ContentToolSettings/ContainerTexture/LblTerrainTexture");
		_lblCliffTexture = GetNode<Label>("RightSlidePanel/RightScroll/AccordionContainer/ToolSettingsAccordion/ContentToolSettings/ContainerTexture/LblCliffTexture");
		
		_chkApplyGroundTexture = new CheckBox();
		_chkApplyGroundTexture.Name = "ChkApplyGroundTexture";
		_chkApplyGroundTexture.Text = TranslationServer.Translate("Ground");
		_chkApplyGroundTexture.ButtonPressed = true;
		_chkApplyGroundTexture.FocusMode = Control.FocusModeEnum.None;
		_chkApplyGroundTexture.AddThemeFontSizeOverride("font_size", 10);
		UIStyle.ApplyCheckboxStyle(_chkApplyGroundTexture);

		_chkApplyCliffTexture = new CheckBox();
		_chkApplyCliffTexture.Name = "ChkApplyCliffTexture";
		_chkApplyCliffTexture.Text = TranslationServer.Translate("Cliff");
		_chkApplyCliffTexture.ButtonPressed = true;
		_chkApplyCliffTexture.FocusMode = Control.FocusModeEnum.None;
		_chkApplyCliffTexture.AddThemeFontSizeOverride("font_size", 10);
		UIStyle.ApplyCheckboxStyle(_chkApplyCliffTexture);

		_chkApplyGroundTexture.Toggled += (toggled) =>
		{
			if (!toggled && (_chkApplyCliffTexture == null || !_chkApplyCliffTexture.ButtonPressed))
			{
				_chkApplyCliffTexture.SetPressedNoSignal(true);
			}
			UpdateTextureLabels();
		};

		_chkApplyCliffTexture.Toggled += (toggled) =>
		{
			if (!toggled && (_chkApplyGroundTexture == null || !_chkApplyGroundTexture.ButtonPressed))
			{
				_chkApplyGroundTexture.SetPressedNoSignal(true);
			}
			UpdateTextureLabels();
		};

		if (_containerTextureSettings != null && _lblTerrainTexture != null && _lblCliffTexture != null)
		{
			_rowGroundTexture = new HBoxContainer();
			_rowGroundTexture.Name = "RowGroundTexture";
			_rowGroundTexture.AddThemeConstantOverride("separation", 6);

			int idxTerrain = _lblTerrainTexture.GetIndex();
			_containerTextureSettings.RemoveChild(_lblTerrainTexture);
			_rowGroundTexture.AddChild(_lblTerrainTexture);
			_lblTerrainTexture.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
			_rowGroundTexture.AddChild(_chkApplyGroundTexture);
			_containerTextureSettings.AddChild(_rowGroundTexture);
			_containerTextureSettings.MoveChild(_rowGroundTexture, idxTerrain);

			_rowCliffTexture = new HBoxContainer();
			_rowCliffTexture.Name = "RowCliffTexture";
			_rowCliffTexture.AddThemeConstantOverride("separation", 6);

			int idxCliff = _lblCliffTexture.GetIndex();
			_containerTextureSettings.RemoveChild(_lblCliffTexture);
			_rowCliffTexture.AddChild(_lblCliffTexture);
			_lblCliffTexture.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
			_rowCliffTexture.AddChild(_chkApplyCliffTexture);
			_containerTextureSettings.AddChild(_rowCliffTexture);
			_containerTextureSettings.MoveChild(_rowCliffTexture, idxCliff);
		}
		
		var btnTextureSwap = GetNode<Button>("RightSlidePanel/RightScroll/AccordionContainer/ToolSettingsAccordion/ContentToolSettings/ContainerTexture/BtnTextureSwap");
		SetupOptionButton(btnTextureSwap, "\uf021 SWAP TEXTURES (GLOBAL)", () =>
		{
			if (GameHost.Instance != null)
			{
				GameHost.Instance.SwapTexturesExternal(GameHost.Instance.EditorPaintTextureIndex, GameHost.Instance.EditorCliffPaintTextureIndex);
			}
		}, 11, "Globally swap grass/dirt texture assignment indices (X)");

		_scrollSwatches = GetNodeOrNull<ScrollContainer>("RightSlidePanel/RightScroll/AccordionContainer/ToolSettingsAccordion/ContentToolSettings/ContainerTexture/ScrollSwatches");
		_gridSwatches = GetNodeOrNull<Control>("RightSlidePanel/RightScroll/AccordionContainer/ToolSettingsAccordion/ContentToolSettings/ContainerTexture/ScrollSwatches/GridSwatches")
			?? GetNodeOrNull<Control>("RightSlidePanel/RightScroll/AccordionContainer/ToolSettingsAccordion/ContentToolSettings/ContainerTexture/GridSwatches");

		if (_gridSwatches != null && _scrollSwatches == null && _gridSwatches.GetParent() is VBoxContainer parentVBox)
		{
			int gridIndex = _gridSwatches.GetIndex();
			parentVBox.RemoveChild(_gridSwatches);
			_scrollSwatches = new ScrollContainer();
			_scrollSwatches.Name = "ScrollSwatches";
			_scrollSwatches.HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled;
			_scrollSwatches.VerticalScrollMode = ScrollContainer.ScrollMode.Auto;
			_scrollSwatches.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
			_scrollSwatches.AddChild(_gridSwatches);
			parentVBox.AddChild(_scrollSwatches);
			parentVBox.MoveChild(_scrollSwatches, gridIndex);
		}

		SetupTextureSwatches(true);

		_btnReplaceTexture = new Button();
		_btnReplaceTexture.Name = "BtnReplaceTexture";
		_btnReplaceTexture.Set("icon_max_width", 0);
		SetupOptionButton(_btnReplaceTexture, "\uf093 REPLACE TEXTURE", () => _replaceTextureDialog?.OpenDialog(), 11, "Replace all instances of a texture with another texture across the map");
		_containerTextureSettings?.AddChild(_btnReplaceTexture);

		_containerPathingSettings = GetNode<VBoxContainer>("RightSlidePanel/RightScroll/AccordionContainer/ToolSettingsAccordion/ContentToolSettings/ContainerPathing");
		var pathingContent = GetNode<VBoxContainer>("RightSlidePanel/RightScroll/AccordionContainer/ToolSettingsAccordion/ContentToolSettings/ContainerPathing/PathingContent");
		_chkShallowWater = pathingContent.GetNode<CheckBox>("ChkShallowWater");
		_chkDeepWater = pathingContent.GetNode<CheckBox>("ChkDeepWater");
		_chkFlying = pathingContent.GetNode<CheckBox>("ChkFlying");
		_chkGround = pathingContent.GetNode<CheckBox>("ChkGround");
		_chkBuildable = pathingContent.GetNode<CheckBox>("ChkBuildable");

		SetupPathingCheckBoxRow(pathingContent, _chkShallowWater, new Color(0.2f, 0.6f, 1.0f), "Shallow Water");
		SetupPathingCheckBoxRow(pathingContent, _chkDeepWater, new Color(0.0f, 0.15f, 0.7f), "Deep Water");
		SetupPathingCheckBoxRow(pathingContent, _chkFlying, new Color(0.85f, 0.85f, 0.0f), "Flying");
		SetupPathingCheckBoxRow(pathingContent, _chkGround, new Color(0.2f, 0.85f, 0.2f), "Ground");
		SetupPathingCheckBoxRow(pathingContent, _chkBuildable, new Color(0.6f, 0.2f, 0.8f), "Buildable");
		_chkGround.ButtonPressed = true;

		var pathingModeRow = pathingContent.GetNodeOrNull<HBoxContainer>("PathingModeRow");
		if (pathingModeRow != null)
		{
			var modePanel = new PanelContainer();
			modePanel.Name = "ModeRowPanel";
			modePanel.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
			var modeStyle = new StyleBoxFlat();
			modeStyle.BgColor = new Color(0.12f, 0.13f, 0.16f, 0.82f);
			modeStyle.BorderColor = new Color(0.35f, 0.33f, 0.28f, 0.75f);
			modeStyle.SetBorderWidthAll(1);
			modeStyle.CornerRadiusTopLeft = 4;
			modeStyle.CornerRadiusTopRight = 4;
			modeStyle.CornerRadiusBottomLeft = 4;
			modeStyle.CornerRadiusBottomRight = 4;
			modeStyle.ContentMarginLeft = 8;
			modeStyle.ContentMarginRight = 8;
			modeStyle.ContentMarginTop = 4;
			modeStyle.ContentMarginBottom = 4;
			modePanel.AddThemeStyleboxOverride("panel", modeStyle);

			int modeIdx = pathingModeRow.GetIndex();
			pathingContent.RemoveChild(pathingModeRow);
			modePanel.AddChild(pathingModeRow);
			pathingContent.AddChild(modePanel);
			pathingContent.MoveChild(modePanel, modeIdx);
		}

		_optPathingMode = pathingContent.GetNode<OptionButton>("ModeRowPanel/PathingModeRow/OptPathingMode");
		_optPathingMode.AddItem(TranslationServer.Translate("Add Pathing Attribute"), 0);
		_optPathingMode.AddItem(TranslationServer.Translate("Clear Pathing Attribute"), 1);
		_optPathingMode.Selected = 0;

		_containerCategorySelector = GetNode<VBoxContainer>("RightSlidePanel/RightScroll/AccordionContainer/ToolSettingsAccordion/ContentToolSettings/ContainerCategorySelector");


		_containerEyedropperSettings = GetNode<VBoxContainer>("RightSlidePanel/RightScroll/AccordionContainer/ToolSettingsAccordion/ContentToolSettings/ContainerEyedropper");
		_optEyedropperMode = GetNode<OptionButton>("RightSlidePanel/RightScroll/AccordionContainer/ToolSettingsAccordion/ContentToolSettings/ContainerEyedropper/OptEyedropperMode");
		_optEyedropperMode.AddItem(TranslationServer.Translate("Auto-Detect Mode"), 0);
		_optEyedropperMode.AddItem(TranslationServer.Translate("Pick 3D Asset"), 1);
		_optEyedropperMode.AddItem(TranslationServer.Translate("Pick Decal"), 2);
		_optEyedropperMode.AddItem(TranslationServer.Translate("Pick Terrain Texture"), 3);
		_optEyedropperMode.AddItem(TranslationServer.Translate("Pick Height"), 4);
		_optEyedropperMode.Selected = 0;

		_containerPasteSettings = GetNode<VBoxContainer>("RightSlidePanel/RightScroll/AccordionContainer/ToolSettingsAccordion/ContentToolSettings/ContainerPaste");
		var chkPasteHeights = GetNode<CheckBox>("RightSlidePanel/RightScroll/AccordionContainer/ToolSettingsAccordion/ContentToolSettings/ContainerPaste/PasteOptionsBox/ChkPasteHeights");
		chkPasteHeights.Text = TranslationServer.Translate("HeightMap");
		chkPasteHeights.ButtonPressed = true;
		var chkPasteTextures = GetNode<CheckBox>("RightSlidePanel/RightScroll/AccordionContainer/ToolSettingsAccordion/ContentToolSettings/ContainerPaste/PasteOptionsBox/ChkPasteTextures");
		chkPasteTextures.Text = TranslationServer.Translate("Textures");
		chkPasteTextures.ButtonPressed = true;
		var chkPasteEntities = GetNode<CheckBox>("RightSlidePanel/RightScroll/AccordionContainer/ToolSettingsAccordion/ContentToolSettings/ContainerPaste/PasteOptionsBox/ChkPasteEntities");
		chkPasteEntities.Text = TranslationServer.Translate("Units / Props / Decals");
		chkPasteEntities.ButtonPressed = true;
		var chkPastePathing = GetNode<CheckBox>("RightSlidePanel/RightScroll/AccordionContainer/ToolSettingsAccordion/ContentToolSettings/ContainerPaste/PasteOptionsBox/ChkPastePathing");
		chkPastePathing.Text = TranslationServer.Translate("Pathing");
		chkPastePathing.ButtonPressed = true;

		chkPasteTextures.Toggled += (toggled) => { if (GameHost.Instance != null) GameHost.Instance.PasteOptionTextures = toggled; };
		chkPasteHeights.Toggled += (toggled) => { if (GameHost.Instance != null) GameHost.Instance.PasteOptionHeights = toggled; };
		chkPasteEntities.Toggled += (toggled) => { if (GameHost.Instance != null) GameHost.Instance.PasteOptionEntities = toggled; };
		chkPastePathing.Toggled += (toggled) => { if (GameHost.Instance != null) GameHost.Instance.PasteOptionPathing = toggled; };

		_lblPasteRotation = GetNode<Label>("RightSlidePanel/RightScroll/AccordionContainer/ToolSettingsAccordion/ContentToolSettings/ContainerPaste/PasteOptionsBox/PasteRotationBox/Header/LblPasteRotationValue");
		_sldPasteRotation = GetNode<HSlider>("RightSlidePanel/RightScroll/AccordionContainer/ToolSettingsAccordion/ContentToolSettings/ContainerPaste/PasteOptionsBox/PasteRotationBox/SldPasteRotation");
		_sldPasteRotation.ValueChanged += (val) =>
		{
			float fVal = (float)val;
			_lblPasteRotation.Text = fVal.ToString("F0") + "°";
			if (GameHost.Instance != null)
			{
				GameHost.Instance.EditorPasteRotation = fVal;
			}
		};
		_sldPasteRotation.DragStarted += () => _isDraggingSlider = true;
		_sldPasteRotation.DragEnded += (valueChanged) => _isDraggingSlider = false;

		_btnClipboardBrushShape = new Button();
		_btnClipboardBrushShape.Name = "BtnClipboardBrushShape";
		_btnClipboardBrushShape.Set("icon_max_width", 0);
		SetupOptionButton(_btnClipboardBrushShape, GameHost.Instance != null && !GameHost.Instance.EditorBrushIsSquare ? "\uf111 BRUSH: CIRCLE" : "\uf0c8 BRUSH: SQUARE", () =>
		{
			if (GameHost.Instance != null)
			{
				GameHost.Instance.EditorBrushIsSquare = !GameHost.Instance.EditorBrushIsSquare;
				UpdateBrushShapeExternal(GameHost.Instance.EditorBrushIsSquare);
				ShowFeedback(GameHost.Instance.EditorBrushIsSquare ? TranslationServer.Translate("Brush Shape: SQUARE") : TranslationServer.Translate("Brush Shape: CIRCLE"));
			}
		}, 10, "Toggle square / circular selection and clipboard brush shape");

		_optClipboardMirrorMode = new OptionButton();
		_optClipboardMirrorMode.Name = "OptClipboardMirrorMode";
		_optClipboardMirrorMode.Set("icon_max_width", 0);
		SetupMirrorDropdown(_optClipboardMirrorMode, "Select terrain, clipboard and object mirroring symmetry mode");

		_btnPasteReflection = new Button();
		_btnPasteReflection.Name = "BtnPasteReflection";
		_btnPasteReflection.Set("icon_max_width", 0);
		SetupOptionButton(_btnPasteReflection, "\uf07e REFLECT: NONE", () => CyclePasteReflection(), 11, "Cycle reflection mode for paste operation (None, Horizontal, Vertical)");

		_btnPasteAnchor = new Button();
		_btnPasteAnchor.Name = "BtnPasteAnchor";
		_btnPasteAnchor.Set("icon_max_width", 0);
		SetupOptionButton(_btnPasteAnchor, "\uf245 ANCHOR: CENTER", () => CyclePasteAnchor(), 11, "Cycle pivot / anchor tile used to align pasted selection (Center, Corners)");

		_lblPasteTelemetry = new Label();
		_lblPasteTelemetry.Name = "LblPasteTelemetry";
		_lblPasteTelemetry.AddThemeFontSizeOverride("font_size", 10);
		_lblPasteTelemetry.AddThemeColorOverride("font_color", new Color(0.3f, 0.85f, 1.0f));
		_lblPasteTelemetry.AutowrapMode = TextServer.AutowrapMode.WordSmart;
		_lblPasteTelemetry.CustomMinimumSize = new Vector2(200, 0);
		_lblPasteTelemetry.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
		_lblPasteTelemetry.Text = "";

		var pasteOptionsBox = _containerPasteSettings.GetNodeOrNull<VBoxContainer>("PasteOptionsBox");
		if (pasteOptionsBox != null)
		{
			var shapeMirrorGrid = new GridContainer();
			shapeMirrorGrid.Name = "ClipboardShapeMirrorGrid";
			shapeMirrorGrid.Columns = 2;
			shapeMirrorGrid.AddThemeConstantOverride("h_separation", 6);
			shapeMirrorGrid.AddThemeConstantOverride("v_separation", 4);
			shapeMirrorGrid.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
			shapeMirrorGrid.AddChild(_btnClipboardBrushShape);
			shapeMirrorGrid.AddChild(_optClipboardMirrorMode);
			pasteOptionsBox.AddChild(shapeMirrorGrid);

			pasteOptionsBox.AddChild(_btnPasteReflection);
			pasteOptionsBox.AddChild(_btnPasteAnchor);
			pasteOptionsBox.AddChild(_lblPasteTelemetry);
		}

		_accordionInspector = GetNode<VBoxContainer>("RightSlidePanel/RightScroll/AccordionContainer/InspectorAccordion");
		_btnHeaderInspector = GetNode<Button>("RightSlidePanel/RightScroll/AccordionContainer/InspectorAccordion/BtnHeaderInspector");
		_contentInspector = GetNode<VBoxContainer>("RightSlidePanel/RightScroll/AccordionContainer/InspectorAccordion/ContentInspector");
		StyleAccordionHeader(_btnHeaderInspector);
		SetupAccordion(_btnHeaderInspector, _contentInspector, TranslationServer.Translate("Selected Object Inspector"));

		_accordionPlacement = GetNode<VBoxContainer>("RightSlidePanel/RightScroll/AccordionContainer/PlacementAccordion");
		_btnHeaderPlacement = GetNode<Button>("RightSlidePanel/RightScroll/AccordionContainer/PlacementAccordion/BtnHeaderPlacement");
		_contentPlacement = GetNode<VBoxContainer>("RightSlidePanel/RightScroll/AccordionContainer/PlacementAccordion/ContentPlacement");
		StyleAccordionHeader(_btnHeaderPlacement);
		SetupAccordion(_btnHeaderPlacement, _contentPlacement, TranslationServer.Translate("Placement Config"));

		_sldPlacementRotate = GetNode<Slider>("RightSlidePanel/RightScroll/AccordionContainer/PlacementAccordion/ContentPlacement/PlacementRotateBox/SldPlacementRotate");
		_lblPlacementRotateValue = GetNode<Label>("RightSlidePanel/RightScroll/AccordionContainer/PlacementAccordion/ContentPlacement/PlacementRotateBox/Header/LblPlacementRotateValue");
		_sldPlacementScale = GetNode<Slider>("RightSlidePanel/RightScroll/AccordionContainer/PlacementAccordion/ContentPlacement/PlacementScaleBox/SldPlacementScale");
		_lblPlacementScaleValue = GetNode<Label>("RightSlidePanel/RightScroll/AccordionContainer/PlacementAccordion/ContentPlacement/PlacementScaleBox/Header/LblPlacementScaleValue");
		
		_btnToggleSnap = GetNode<Button>("RightSlidePanel/RightScroll/AccordionContainer/PlacementAccordion/ContentPlacement/BtnToggleSnap");
		SetupOptionButton(_btnToggleSnap, "\uf0ce SNAP TO GRID: OFF", () =>
		{
			if (GameHost.Instance != null)
			{
				GameHost.Instance.EditorSnapToGrid = !GameHost.Instance.EditorSnapToGrid;
				UpdateGridSnapExternal(GameHost.Instance.EditorSnapToGrid);
			}
		}, 11, "Toggle snapping objects and placements to the grid");

		_optPlacementMirrorMode = new OptionButton();
		_optPlacementMirrorMode.Name = "OptPlacementMirrorMode";
		_optPlacementMirrorMode.Set("icon_max_width", 0);
		SetupMirrorDropdown(_optPlacementMirrorMode, "Select terrain and object mirroring symmetry mode");
		if (_contentPlacement != null)
		{
			_contentPlacement.AddChild(_optPlacementMirrorMode);
			_contentPlacement.MoveChild(_optPlacementMirrorMode, _btnToggleSnap.GetIndex() + 1);
		}

		_chkRandomRotation = GetNode<CheckBox>("RightSlidePanel/RightScroll/AccordionContainer/PlacementAccordion/ContentPlacement/ChkRandomRotation");
		_chkRandomScale = GetNode<CheckBox>("RightSlidePanel/RightScroll/AccordionContainer/PlacementAccordion/ContentPlacement/ChkRandomScale");

		_chkClumpMode = GetNode<CheckBox>("RightSlidePanel/RightScroll/AccordionContainer/PlacementAccordion/ContentPlacement/ChkClumpMode");
		_densityBox = GetNode<Control>("RightSlidePanel/RightScroll/AccordionContainer/PlacementAccordion/ContentPlacement/DensityBox");
		_sldClumpDensity = GetNode<HSlider>("RightSlidePanel/RightScroll/AccordionContainer/PlacementAccordion/ContentPlacement/DensityBox/SldClumpDensity");
		_lblClumpDensityValue = GetNode<Label>("RightSlidePanel/RightScroll/AccordionContainer/PlacementAccordion/ContentPlacement/DensityBox/Header/LblClumpDensityValue");
		_scaleVarBox = GetNode<Control>("RightSlidePanel/RightScroll/AccordionContainer/PlacementAccordion/ContentPlacement/ScaleVarBox");
		_sldClumpScaleVar = GetNode<HSlider>("RightSlidePanel/RightScroll/AccordionContainer/PlacementAccordion/ContentPlacement/ScaleVarBox/SldClumpScaleVar");
		_lblClumpScaleVarValue = GetNode<Label>("RightSlidePanel/RightScroll/AccordionContainer/PlacementAccordion/ContentPlacement/ScaleVarBox/Header/LblClumpScaleVarValue");
		var lblScaleVarTitle = GetNodeOrNull<Label>("RightSlidePanel/RightScroll/AccordionContainer/PlacementAccordion/ContentPlacement/ScaleVarBox/Header/LblScaleVarTitle");
		if (lblScaleVarTitle != null) lblScaleVarTitle.Text = TranslationServer.Translate("Clump Scale Variance");

		_chkClumpMode.ButtonPressed = false;
		_chkClumpMode.Toggled += (toggled) =>
		{
			if (_densityBox != null) _densityBox.Visible = toggled;
			if (_scaleVarBox != null) _scaleVarBox.Visible = toggled;
			if (GameHost.Instance != null)
			{
				UpdateSidebarMorph(GameHost.Instance.ActiveEditorTool);
			}
		};

		_sldClumpDensity.ValueChanged += (val) =>
		{
			float fVal = (float)val;
			_lblClumpDensityValue.Text = fVal.ToString("F0");
			if (GameHost.Instance != null) GameHost.Instance.EditorClumpCount = fVal;
		};
		_sldClumpDensity.DragStarted += () => _isDraggingSlider = true;
		_sldClumpDensity.DragEnded += (valueChanged) => _isDraggingSlider = false;

		_sldClumpScaleVar.ValueChanged += (val) =>
		{
			float fVal = (float)val;
			_lblClumpScaleVarValue.Text = fVal.ToString("F2");
			if (GameHost.Instance != null) GameHost.Instance.EditorClumpScale = fVal;
		};
		_sldClumpScaleVar.DragStarted += () => _isDraggingSlider = true;
		_sldClumpScaleVar.DragEnded += (valueChanged) => _isDraggingSlider = false;

		_sldPlacementRotate.DragStarted += () => _isDraggingSlider = true;
		_sldPlacementRotate.DragEnded += (valueChanged) => _isDraggingSlider = false;
		_sldPlacementScale.DragStarted += () => _isDraggingSlider = true;
		_sldPlacementScale.DragEnded += (valueChanged) => _isDraggingSlider = false;

		TriggerToolSelection(GameHost.EditorTool.Raise, _btnRaise);

		_feedbackLabel.Modulate = new Color(1, 1, 1, 0);
		Input.MouseMode = Input.MouseModeEnum.Visible;



		_entityPaletteController = new MapEditorEntityPaletteController(this, _containerCategorySelector, _btnAddObject);
		_generationDialog = new MapEditorGenerationDialog(this);

		_topBarController = new MapEditorTopBar(_btnBackToHub, _btnPublish, _btnSave, _btnLoad, _btnUndo, _btnRedo, _btnVSCode, _statusLabel, _feedbackLabel);
		_brushSettingsController = new MapEditorBrushSettings(_sldBrushSize, _lblBrushSizeValue, _sldBrushStrength, _lblBrushStrengthValue, _chkBlockMode, _sldBlockStep, _lblBlockStepValue, _sldHeight, _lblHeightValue);
		_placementSettingsController = new MapEditorPlacementSettings(_sldPlacementRotate, _lblPlacementRotateValue, _sldPlacementScale, _lblPlacementScaleValue, _chkRandomRotation, _chkRandomScale, _chkClumpMode, _sldClumpDensity, _lblClumpDensityValue, _sldClumpScaleVar, _lblClumpScaleVarValue);
		InitializeInspectorPanel();
		_inspectorController = new MapEditorInspector(_lblInspectorTitle, _lblInspectorPos, _btnInspectorRotLeft, _btnInspectorRotRight, _btnInspectorScaleDown, _btnInspectorScaleUp, _btnInspectorScaleReset, _btnInspectorDelete);
		_pathingPanelController = new MapEditorPathingPanel(_chkShallowWater, _chkDeepWater, _chkFlying, _chkGround, _chkBuildable, _optPathingMode);

		SetupMinimap();

		_minimapController = new MapEditorMinimap(_minimapFrame, _minimapArea, _cameraIndicator, this);
		RegenerateMinimap();

		MakeCardDraggable(_accordionFile, _btnHeaderFile, _contentFile, "File");
		MakeCardDraggable(_accordionViewport, _btnHeaderViewport, _contentViewport, "Viewport & Navigation");
		MakeCardDraggable(_accordionTool, _btnHeaderTool, _contentTool, "Tool");
		MakeCardDraggable(_accordionBrush, _btnHeaderBrush, _contentBrush, "Global Brush Properties");
		MakeCardDraggable(_accordionToolSettings, _btnHeaderToolSettings, _contentToolSettings, "Tool Settings");
		MakeCardDraggable(_accordionPlacement, _btnHeaderPlacement, _contentPlacement, "Placement Config");
		MakeCardDraggable(_accordionInspector, _btnHeaderInspector, _contentInspector, "Selected Object Inspector");
		if (_accordionWater != null) MakeCardDraggable(_accordionWater, _btnHeaderWater, _contentWater, "Liquid / Water Config");

		RestructurePanelLayouts();

		if (GameHost.Instance != null)
		{
			UpdateRotationExternal(GameHost.Instance.EditorPlacementRotation);
			UpdateScaleExternal(GameHost.Instance.EditorPlacementScale);
			UpdateGridSnapExternal(GameHost.Instance.EditorSnapToGrid);
			UpdatePasteRotationExternal(GameHost.Instance.EditorPasteRotation);
			UpdatePasteReflectionExternal(GameHost.Instance.EditorPasteReflection);
			UpdateMirrorDropdowns();
			UpdateSymmetrySpokesDropdowns();
			UpdatePolarRadialStepButtonText();
			UpdatePolarRingSpacingButtonText();
		}

		if (!_agreementShownThisSession)
		{
			ShowAgreementModal(() =>
			{
				CheckPostLaunchPrompts();
			});
		}
		else
		{
			CheckPostLaunchPrompts();
		}

		var targetFileBox = GetContentTarget(_contentFile);
		if (targetFileBox != null)
		{
			foreach (Node child in targetFileBox.GetChildren())
			{
				if (child is Control ctrl)
				{
					ctrl.SizeFlagsHorizontal = SizeFlags.ExpandFill;
				}
			}
		}

		if (ReturningFromTest)
		{
			_sldBrushSize.Value = SavedBrushRadius;
			_sldBrushStrength.Value = SavedBrushStrength;

			if (SavedActiveTool == GameHost.EditorTool.PlaceUnit ||
				SavedActiveTool == GameHost.EditorTool.PlaceProp ||
				SavedActiveTool == GameHost.EditorTool.PlaceDecal)
			{
				_entityPaletteController?.SelectCategoryItemExternal(SavedEntityCategory, SavedActivePlaceId);
			}
			else
			{
				Button toolBtn = GetButtonForTool(SavedActiveTool, SavedActivePlaceId);
				if (toolBtn != null)
				{
					TriggerToolSelection(SavedActiveTool, toolBtn, SavedActivePlaceId);
				}
				else
				{
					SwitchModule(EditorModule.Terrain);
				}
			}

			ReturningFromTest = false;
		}
		else
		{
			SwitchModule(EditorModule.Terrain);
		}
		}
		catch (Exception ex)
		{
			GD.PrintErr($"CRITICAL ERROR IN MAPEDITORHUD _READY: {ex}");
			System.IO.File.WriteAllText(@"C:\temp\Realm\ready_exception.txt", ex.ToString());
			throw;
		}
	}

	public override void _Process(double delta)
	{
		if (GameHost.Instance != null)
		{
			_viewModel.UpdateFromHost();
			var mousePos = GetViewport().GetMousePosition();
			if (GameHost.Instance.TryRaycastTerrainFromMousePosition(mousePos, out Vector3 pos))
			{
				if ((pos - _lastRaycastPos).LengthSquared() > 0.01f)
				{
					_lastRaycastPos = pos;
					_viewModel.StatusText = GameHost.Instance.GetTerrainStatusString(pos);
				}
			}
		}

		_topBarController?.Update(_viewModel);
		if (!_isDraggingSlider)
		{
			_brushSettingsController?.Update(_viewModel);
			_placementSettingsController?.Update(_viewModel);
			UpdatePasteRotationExternal(_viewModel.PasteRotation);
			UpdatePasteReflectionExternal(_viewModel.PasteReflection);
		}
		_inspectorController?.Update(_viewModel);
		_pathingPanelController?.Update(_viewModel);
		_minimapController?.Update(_viewModel);

		if (GameHost.Instance != null)
		{
			bool hasSelectedObject = GodotObject.IsInstanceValid(GameHost.Instance.SelectedEditorObject);
			bool shouldShowInspector = hasSelectedObject && (GameHost.Instance.ActiveEditorTool == GameHost.EditorTool.SelectMove);
			if (_accordionInspector != null && _accordionInspector.Visible != shouldShowInspector)
			{
				_accordionInspector.Visible = shouldShowInspector;
				if (shouldShowInspector)
				{
					if (_contentInspector != null) _contentInspector.Visible = true;
					if (_btnHeaderInspector != null) _btnHeaderInspector.Text = "▼ " + TranslationServer.Translate("Selected Object Inspector");
				}
				_accordionContainer?.QueueSort();
			}
		}

		_mapNameUpdateTimer += delta;
		if (_mapNameUpdateTimer >= 0.2)
		{
			_mapNameUpdateTimer = 0.0;
			UpdateMapNameHeader();
		}

		int intervalMins = EditorSettingsDialog.CurrentSettings?.AutoBackupIntervalMinutes ?? 30;
		if (intervalMins > 0)
		{
			_autoBackupElapsedSeconds += delta;
			double targetSeconds = intervalMins * 60.0;
			if (_autoBackupElapsedSeconds >= targetSeconds)
			{
				_autoBackupElapsedSeconds = 0;
				PerformAutoBackup();
			}
		}
	}

	public void ShowFeedbackExternal(string text)
	{
		ShowFeedback(text);
	}

	public void ShowFeedback(string text)
	{
		_topBarController?.ShowFeedback(text);
	}

	public int GetSelectedPathingMask()
	{
		int mask = 0;
		if (_chkGround != null && _chkGround.ButtonPressed) mask |= EditableTerrain.PATHING_GROUND;
		if (_chkFlying != null && _chkFlying.ButtonPressed) mask |= EditableTerrain.PATHING_FLYING;
		if (_chkShallowWater != null && _chkShallowWater.ButtonPressed) mask |= EditableTerrain.PATHING_SHALLOW_WATER;
		if (_chkDeepWater != null && _chkDeepWater.ButtonPressed) mask |= EditableTerrain.PATHING_DEEP_WATER;
		if (_chkBuildable != null && _chkBuildable.ButtonPressed) mask |= EditableTerrain.PATHING_BUILDABLE;
		return mask;
	}

	public bool IsPathingAddMode()
	{
		if (_optPathingMode == null) return true;
		return _optPathingMode.Selected == 0;
	}

	public bool IsWaterRemoveAction()
	{
		return _isWaterRemoveAction;
	}

	public byte GetSelectedWaterProfileIndex()
	{
		if (_optWaterMode == null || _optWaterMode.Selected < 0) return 0;
		var meta = _optWaterMode.GetItemMetadata(_optWaterMode.Selected);
		if (meta.VariantType == Variant.Type.Nil) return 0;
		return (byte)(int)meta;
	}

	public WaterType GetSelectedWaterMode()
	{
		if (_optWaterMode == null || _optWaterMode.Selected < 0) return WaterType.None;
		return WaterType.Shallow;
	}

	public float GetSelectedWaterHeight()
	{
		if (_sldWaterHeight == null) return 0.9f;
		return (float)_sldWaterHeight.Value;
	}

	public string GetEyedropperMode()
	{
		if (_optEyedropperMode == null) return "all";
		return _optEyedropperMode.Selected switch
		{
			1 => "3d",
			2 => "decal",
			3 => "terrain",
			4 => "height",
			_ => "all"
		};
	}

	private void ApplyThemeStyles()
	{
		if (_panelLeft != null)
		{
			_panelLeft.MouseFilter = Control.MouseFilterEnum.Ignore;
			_panelLeft.AddThemeStyleboxOverride("panel", new StyleBoxEmpty());
		}
		if (_panelRight != null)
		{
			_panelRight.MouseFilter = Control.MouseFilterEnum.Ignore;
			_panelRight.AddThemeStyleboxOverride("panel", new StyleBoxEmpty());
		}

		var leftScroll = GetNodeOrNull<ScrollContainer>("LeftSlidePanel/LeftScroll");
		if (leftScroll != null) leftScroll.MouseFilter = Control.MouseFilterEnum.Ignore;

		var rightScroll = GetNodeOrNull<ScrollContainer>("RightSlidePanel/RightScroll");
		if (rightScroll != null) rightScroll.MouseFilter = Control.MouseFilterEnum.Ignore;

		var leftVBox = GetNodeOrNull<VBoxContainer>("LeftSlidePanel/LeftScroll/LeftVBox");
		if (leftVBox != null)
		{
			leftVBox.MouseFilter = Control.MouseFilterEnum.Ignore;
			leftVBox.AddThemeConstantOverride("separation", 14);
		}

		var rightVBox = GetNodeOrNull<VBoxContainer>("RightSlidePanel/RightScroll/AccordionContainer");
		if (rightVBox != null)
		{
			rightVBox.MouseFilter = Control.MouseFilterEnum.Ignore;
			rightVBox.AddThemeConstantOverride("separation", 14);
		}

		if (_accordionFile != null) _accordionFile.CustomMinimumSize = new Vector2(260, 0);
		if (_accordionViewport != null) _accordionViewport.CustomMinimumSize = new Vector2(260, 0);
		if (_accordionTool != null) _accordionTool.CustomMinimumSize = new Vector2(260, 0);
		if (_accordionBrush != null) _accordionBrush.CustomMinimumSize = new Vector2(260, 0);
		if (_accordionToolSettings != null) _accordionToolSettings.CustomMinimumSize = new Vector2(260, 0);
		if (_accordionPlacement != null) _accordionPlacement.CustomMinimumSize = new Vector2(260, 0);
		if (_accordionInspector != null) _accordionInspector.CustomMinimumSize = new Vector2(260, 0);
		if (_accordionWater != null) _accordionWater.CustomMinimumSize = new Vector2(260, 0);

		ApplyCardPanelStyle(_accordionFile);
		ApplyCardPanelStyle(_accordionViewport);
		ApplyCardPanelStyle(_accordionTool);
		ApplyCardPanelStyle(_accordionBrush);
		if (_accordionWater != null) ApplyCardPanelStyle(_accordionWater);
		ApplyCardPanelStyle(_accordionToolSettings);
		ApplyCardPanelStyle(_accordionPlacement);
		ApplyCardPanelStyle(_accordionInspector);

		StyleContentBox(_contentFile);
		StyleContentBox(_contentViewport);
		StyleContentBox(_contentTool);
		StyleContentBox(_contentBrush);
		if (_contentWater != null) StyleContentBox(_contentWater);
		StyleContentBox(_contentToolSettings);
		StyleContentBox(_contentPlacement);
		StyleContentBox(_contentInspector);

		SetupCardScrollContainer(_contentFile, 300f);
		SetupCardScrollContainer(_contentViewport, 0f, false);
		SetupCardScrollContainer(_contentTool, 320f);
		SetupCardScrollContainer(_contentBrush, 300f);
		if (_contentWater != null) SetupCardScrollContainer(_contentWater, 200f);
		SetupCardScrollContainer(_contentToolSettings, 320f);
		SetupCardScrollContainer(_contentPlacement, 320f);
		SetupCardScrollContainer(_contentInspector, 300f);

		foreach (var btn in new[] { _btnLoad, _btnSave, _btnSaveAs })
		{
			StyleRowButton(btn);
			btn.AddThemeFontSizeOverride("font_size", 11);
			btn.Alignment = HorizontalAlignment.Center;
			foreach (string styleName in new[] { "normal", "hover", "pressed" })
			{
				if (btn.GetThemeStylebox(styleName) is StyleBoxFlat styleBox)
				{
					var compactBox = (StyleBoxFlat)styleBox.Duplicate();
					compactBox.ContentMarginLeft = 4;
					compactBox.ContentMarginRight = 4;
					btn.AddThemeStyleboxOverride(styleName, compactBox);
				}
			}
		}
		StyleRowButton(_btnTestMap);
		StyleRowButton(_btnPublish);
		StyleRowButton(_btnExportMap);
		StyleRowButton(_btnResetMap);
		StyleRowButton(_btnGenerateMap);
		StyleRowButton(_btnImportMinimap);
		StyleRowButton(_btnEditorSettings);
		StyleRowButton(_btnAuthorSignature);

		StyleRowButton(_btnRaise);
		StyleRowButton(_btnLower);
		StyleRowButton(_btnHeight);
		StyleRowButton(_btnSmooth);
		StyleRowButton(_btnPlateau);
		StyleRowButton(_btnRamp);
		StyleRowButton(_btnNoise);
		StyleRowButton(_btnWater);
		StyleRowButton(_btnTextureBrush);
		StyleRowButton(_btnFloodFill);
		StyleRowButton(_btnPathingBrush);
		StyleRowButton(_btnFloodFillPathing);
		StyleRowButton(_btnAddObject);
		StyleRowButton(_btnSelectMove);
		StyleRowButton(_btnDeleteObject);
		StyleRowButton(_btnDrawCoordinate);
		StyleRowButton(_btnCommitCoordinate);
		StyleRowButton(_btnSelectArea);
		StyleRowButton(_btnCut);
		StyleRowButton(_btnCopy);
		StyleRowButton(_btnPaste);
		StyleRowButton(_btnEraseArea);

		StyleRowButton(_btnInspectorRotLeft);
		StyleRowButton(_btnInspectorRotRight);
		StyleRowButton(_btnInspectorScaleDown);
		StyleRowButton(_btnInspectorScaleUp);
		StyleRowButton(_btnInspectorScaleReset);
		StyleRowButton(_btnInspectorDelete);

		StyleValueBadge(_lblBrushSizeValue);
		StyleValueBadge(_lblBrushStrengthValue);
		StyleValueBadge(_lblBlockStepValue);
		StyleValueBadge(_lblHeightValue);
		StyleValueBadge(_lblPlacementRotateValue);
		StyleValueBadge(_lblPlacementScaleValue);
		StyleValueBadge(_lblClumpDensityValue);
		StyleValueBadge(_lblClumpScaleVarValue);
		StyleValueBadge(_lblPasteRotation);

		StyleCheckBoxRow(_chkBlockMode);
		StyleCheckBoxRow(_chkShallowWater);
		StyleCheckBoxRow(_chkDeepWater);
		StyleCheckBoxRow(_chkFlying);
		StyleCheckBoxRow(_chkGround);
		StyleCheckBoxRow(_chkBuildable);
		StyleCheckBoxRow(_chkRandomRotation);
		StyleCheckBoxRow(_chkRandomScale);
		StyleCheckBoxRow(_chkClumpMode);
		StyleSubContainer(_containerTextureSettings, "Texture Paint Palette");
		StyleSubContainer(_containerPathingSettings, "Pathing Masks");
		StyleSubContainer(_containerPlacementSettings, "Placement Controls");
		StyleSubContainer(_densityBox, "Clump Density");
		StyleSubContainer(_scaleVarBox, "Scale Variance");
		StyleSubContainer(_containerEyedropperSettings, "Eyedropper Sample Filter");
		StyleSubContainer(_containerPasteSettings, "Paste Options");
		StyleSubContainer(_containerCategorySelector, "Entity Categories");

		var titleLbl = _topBar?.GetNodeOrNull<Label>("HBox/TitleLabel") ?? GetNodeOrNull<Label>("TopBar/HBox/TitleLabel");
		if (titleLbl != null) titleLbl.Visible = false;

		StyleBox barStyle;
		var posTexture = GD.Load<Texture2D>("res://Assets/UI/map_editor_pos.png");
		if (posTexture != null)
		{
			var posStyle = new StyleBoxTexture();
			posStyle.Texture = posTexture;
			posStyle.TextureMarginLeft = 0;
			posStyle.TextureMarginRight = 0;
			posStyle.TextureMarginTop = 0;
			posStyle.TextureMarginBottom = 0;
			posStyle.ContentMarginLeft = 16;
			posStyle.ContentMarginRight = 16;
			posStyle.ContentMarginTop = 6;
			posStyle.ContentMarginBottom = 6;
			barStyle = posStyle;
		}
		else
		{
			var alphaStyle = new StyleBoxFlat();
			alphaStyle.BgColor = new Color(0.12f, 0.14f, 0.18f, 0.75f);
			alphaStyle.BorderColor = UIStyle.ColorGold;
			alphaStyle.SetBorderWidthAll(1);
			alphaStyle.CornerRadiusTopLeft = 6;
			alphaStyle.CornerRadiusTopRight = 6;
			alphaStyle.CornerRadiusBottomLeft = 6;
			alphaStyle.CornerRadiusBottomRight = 6;
			alphaStyle.ContentMarginLeft = 16;
			alphaStyle.ContentMarginRight = 16;
			alphaStyle.ContentMarginTop = 6;
			alphaStyle.ContentMarginBottom = 6;
			barStyle = alphaStyle;
		}

		if (_topBar != null)
		{
			_topBar.AddThemeStyleboxOverride("panel", barStyle);
		}

		if (_panelMeasurementHUD != null)
		{
			_panelMeasurementHUD.AddThemeStyleboxOverride("panel", barStyle);
		}

		var hBox = _topBar?.GetNodeOrNull<HBoxContainer>("HBox") ?? GetNodeOrNull<HBoxContainer>("TopBar/HBox");
		if (hBox != null)
		{
			hBox.Alignment = BoxContainer.AlignmentMode.Center;
		}

		if (_statusLabel != null)
		{
			_statusLabel.HorizontalAlignment = HorizontalAlignment.Center;
			_statusLabel.VerticalAlignment = VerticalAlignment.Center;
			_statusLabel.AddThemeFontSizeOverride("font_size", 13);
			_statusLabel.AddThemeColorOverride("font_color", UIStyle.ColorGold);
			var fontStatus = GetFontAwesomeFont();
			if (fontStatus != null)
			{
				_statusLabel.AddThemeFontOverride("font", fontStatus);
			}
		}

		if (_lblMeasureTelemetry != null)
		{
			_lblMeasureTelemetry.HorizontalAlignment = HorizontalAlignment.Center;
			_lblMeasureTelemetry.VerticalAlignment = VerticalAlignment.Center;
			_lblMeasureTelemetry.AddThemeFontSizeOverride("font_size", 13);
			_lblMeasureTelemetry.AddThemeColorOverride("font_color", UIStyle.ColorGold);
			var fontStatus = GetFontAwesomeFont();
			if (fontStatus != null)
			{
				_lblMeasureTelemetry.AddThemeFontOverride("font", fontStatus);
			}
		}

		UIStyle.ApplyTitle(_feedbackLabel, "", 24);
	}

	public void UpdateSelectedObjectInfo()
	{
		if (GameHost.Instance == null) return;
		var selected = GameHost.Instance.SelectedEditorObject;
		if (GodotObject.IsInstanceValid(selected))
		{
			if (_lblInfoText != null) _lblInfoText.Visible = false;
			if (_inspectorPanel != null) _inspectorPanel.Visible = true;
			if (_accordionInspector != null)
			{
				_accordionInspector.Visible = true;
				if (_btnHeaderInspector != null) _btnHeaderInspector.Text = "▼ Selected Object Inspector";
				if (_contentInspector != null) _contentInspector.Visible = true;
			}
			string nameStr = selected.Name;
			string idStr = null;
			Vector3 pos = Vector3.Zero;
			Vector3 rot = Vector3.Zero;
			Vector3 scale = Vector3.One;
			if (selected is Node3D node3D)
			{
				pos = node3D.Position;
				rot = node3D.RotationDegrees;
				scale = node3D.Scale;
			}
			string typeStr = "";
			if (selected is Unit3D unit)
			{
				typeStr = unit.IsBuilding ? "BUILDING" : "UNIT";
				idStr = System.IO.Path.GetFileName(unit.UnitId).ToUpper();
				if (unit.IsResource && GameHost.ResourceRegistry.TryGetValue(unit.UnitId, out var resMeta) && !string.IsNullOrEmpty(resMeta.Name))
					nameStr = resMeta.Name.ToUpper();
				else if (unit.IsBuilding && GameHost.BuildingRegistry.TryGetValue(unit.UnitId, out var bldMeta) && !string.IsNullOrEmpty(bldMeta.Name))
					nameStr = bldMeta.Name.ToUpper();
				else if (GameHost.UnitRegistry.TryGetValue(unit.UnitId, out var unitMeta) && !string.IsNullOrEmpty(unitMeta.Name))
					nameStr = unitMeta.Name.ToUpper();
				else
					nameStr = idStr;
				if (_playerOwnerContainer != null && _optPlayerOwner != null)
				{
					_playerOwnerContainer.Visible = true;
					_isUpdatingInspectorUI = true;
					int pIdx = Mathf.Clamp(unit.Player, 0, PlayerColorConfig.Palette.Length - 1);
					_optPlayerOwner.Selected = pIdx;
					_isUpdatingInspectorUI = false;
				}
			}
			else
			{
				if (_playerOwnerContainer != null)
				{
					_playerOwnerContainer.Visible = false;
				}
				if (selected is Prop3D prop)
				{
					typeStr = "PROP";
					idStr = System.IO.Path.GetFileName(prop.PropId).ToUpper();
					if (GameHost.ResourceRegistry.TryGetValue(prop.PropId, out var propResMeta) && !string.IsNullOrEmpty(propResMeta.Name))
						nameStr = propResMeta.Name.ToUpper();
					else if (GameHost.PropRegistry.TryGetValue(prop.PropId, out var propMeta) && !string.IsNullOrEmpty(propMeta.Name))
						nameStr = propMeta.Name.ToUpper();
					else
						nameStr = idStr;
				}
				else if (selected is Decal decal)
				{
					typeStr = "DECAL";
					nameStr = System.IO.Path.GetFileName(decal.Name).ToUpper();
				}
				else if (selected is ProceduralVfxInstance3D vfx)
				{
					typeStr = "VFX";
					nameStr = (!string.IsNullOrEmpty(vfx.Config?.Name) ? vfx.Config.Name : vfx.Config?.PrimitiveType.ToString() ?? "VFX").ToUpper();
					idStr = vfx.Config?.VfxId?.ToUpper() ?? "";
				}
			}

			if (_btnShowCoverage != null)
			{
				bool isUnit = selected is Unit3D;
				_btnShowCoverage.Visible = isUnit;
				if (isUnit && GameHost.Instance != null)
				{
					_btnShowCoverage.SetPressedNoSignal(GameHost.Instance.EditorCoverageOverlayEnabled);
					_btnShowCoverage.Text = GameHost.Instance.EditorCoverageOverlayEnabled ? TranslationServer.Translate("◉ VISION/ATTACK RANGES: ON") : TranslationServer.Translate("◉ VISION/ATTACK RANGES: OFF");
				}
			}
			
			if (_viewModel != null)
			{
				_viewModel.HasInspectorSelection = true;
				_viewModel.InspectorTitle = idStr != null
					? $"SELECTED: {nameStr}\n({idStr})\n[{typeStr}]"
					: $"SELECTED: {nameStr}\n[{typeStr}]";
				_viewModel.InspectorPos = $"Pos: {pos.X:F2}, {pos.Y:F2}, {pos.Z:F2}\nRot: {rot.Y:F1}° | Scale: {scale.X:F2}x";
			}

			bool isUnitCharacter = (selected is Unit3D unitObj && !unitObj.IsBuilding);
			if (isUnitCharacter)
			{
				Node modelRoot = selected;
				if (selected is Unit3D uObj && uObj.ModelNode != null)
				{
					modelRoot = uObj.ModelNode;
				}

				var validation = Realm.Godot.Animation.SkeletonValidator.Validate(modelRoot);
				if (_rigStatusContainer != null && _lblRigStatus != null)
				{
					_rigStatusContainer.Visible = true;
					if (validation.IsValid)
					{
						_lblRigStatus.Text = TranslationServer.Translate("Rig: ✔ Compatible Humanoid");
						_lblRigStatus.AddThemeColorOverride("font_color", new Color(0.3f, 0.9f, 0.3f));
					}
					else if (validation.Skeleton == null)
					{
						_lblRigStatus.Text = TranslationServer.Translate("Rig: ✖ Unrigged Mesh");
						_lblRigStatus.AddThemeColorOverride("font_color", new Color(0.9f, 0.4f, 0.4f));
					}
					else
					{
						_lblRigStatus.Text = string.Format(TranslationServer.Translate("Rig: ✖ Incompatible ({0})"), string.Join(", ", validation.MissingRequiredBones));
						_lblRigStatus.AddThemeColorOverride("font_color", new Color(0.9f, 0.4f, 0.4f));
					}
				}

				if (_btnOpenAnimationPreview != null)
				{
					_btnOpenAnimationPreview.Visible = true;
					_btnOpenAnimationPreview.Disabled = !validation.IsValid;
					_btnOpenAnimationPreview.TooltipText = validation.IsValid
						? TranslationServer.Translate("Open animation preview turntable dialog")
						: TranslationServer.Translate("Animation preview is only available for compatible rigged meshes.");
				}
			}
			else
			{
				if (_rigStatusContainer != null) _rigStatusContainer.Visible = false;
				if (_btnOpenAnimationPreview != null) _btnOpenAnimationPreview.Visible = false;
			}

			string assetKey = GameHost.Instance.GetSelectedEntityOrAssetKey(selected);
			bool isDecal = selected is Decal || (GameHost.Instance != null && GameHost.Instance.FindDecalInParentChain(selected) != null);
			if (!string.IsNullOrEmpty(assetKey) && !isDecal)
			{
				if (_btnOpenGlobalOverrides != null)
				{
					_btnOpenGlobalOverrides.Visible = true;
					_btnOpenGlobalOverrides.TooltipText = string.Format(TranslationServer.Translate("Edit global model scale, offsets, and shaders for {0}"), assetKey);
				}
			}
			if (selected is ProceduralVfxInstance3D)
			{
				if (_btnEditVfx != null)
				{
					_btnEditVfx.Visible = true;
					_btnEditVfx.TooltipText = TranslationServer.Translate("Open Procedural VFX Studio to edit this effect");
				}
			}
			else
			{
				if (_btnEditVfx != null) _btnEditVfx.Visible = false;
			}

			if (_btnEditAttachments != null)
			{
				bool canAttach = selected is Unit3D;
				_btnEditAttachments.Visible = canAttach;
				if (canAttach)
				{
					_btnEditAttachments.TooltipText = TranslationServer.Translate("Open Socket & VFX Attachment Studio for this unit or building");
				}
			}
		}
		else
		{
			if (_playerOwnerContainer != null) _playerOwnerContainer.Visible = false;
			if (_btnOpenGlobalOverrides != null) _btnOpenGlobalOverrides.Visible = false;
			if (_btnOpenAnimationPreview != null) _btnOpenAnimationPreview.Visible = false;
			if (_btnEditVfx != null) _btnEditVfx.Visible = false;
			if (_btnEditAttachments != null) _btnEditAttachments.Visible = false;
			if (_rigStatusContainer != null) _rigStatusContainer.Visible = false;
			if (_btnShowCoverage != null) _btnShowCoverage.Visible = false;
			if (_lblInfoText != null) _lblInfoText.Visible = true;
			if (_inspectorPanel != null) _inspectorPanel.Visible = false;
			if (_accordionInspector != null)
			{
				_accordionInspector.Visible = false;
			}
			if (_viewModel != null)
			{
				_viewModel.HasInspectorSelection = false;
				_viewModel.InspectorTitle = "No Selection";
				_viewModel.InspectorPos = "Position: (0, 0)";
			}
			TriggerToolSelection(GameHost.Instance.ActiveEditorTool, _activeToolButton, GameHost.Instance.ActivePlaceId);
		}
	}

	public void SaveMapActionExternal()
	{
		SaveMapAction();
	}

	public void UpdateGridSnapExternal(bool snap)
	{
		if (_btnToggleSnap != null)
		{
			_btnToggleSnap.Text = snap ? TranslationServer.Translate("\uf0ce SNAP TO GRID: ON") : TranslationServer.Translate("\uf0ce SNAP TO GRID: OFF");
		}
	}

	public void OpenCoordinateNamingPanel(int minX, int minZ, int maxX, int maxZ)
	{
		_pendingCoordinateMinX = minX;
		_pendingCoordinateMinZ = minZ;
		_pendingCoordinateMaxX = maxX;
		_pendingCoordinateMaxZ = maxZ;
		if (_btnCommitCoordinate != null) _btnCommitCoordinate.Visible = true;
		if (_txtCoordinateName != null) _txtCoordinateName.GrabFocus();
	}

	public void RefreshCoordinateListExternal()
	{
		if (_coordinateListVBox == null) return;
		foreach (var child in _coordinateListVBox.GetChildren())
		{
			child.QueueFree();
		}
		var coordinates = GameHost.Instance?.EditorCoordinates;
		if (coordinates == null) return;
		foreach (var coord in coordinates)
		{
			var row = new HBoxContainer();
			row.AddThemeConstantOverride("separation", 6);
			_coordinateListVBox.AddChild(row);

			var btnSelect = new Button();
			btnSelect.Text = $"{coord.Name}  ({coord.MinX:F0},{coord.MinZ:F0}) → ({coord.MaxX:F0},{coord.MaxZ:F0})";
			btnSelect.SizeFlagsHorizontal = SizeFlags.ExpandFill;
			btnSelect.Flat = true;
			btnSelect.Alignment = HorizontalAlignment.Left;
			btnSelect.AddThemeFontSizeOverride("font_size", 11);
			btnSelect.AddThemeColorOverride("font_color", UIStyle.ColorCyanGlow);
			string coordinateName = coord.Name;
			btnSelect.Pressed += () =>
			{
				GameHost.Instance?.SelectCoordinateExternal(coordinateName);
			};
			row.AddChild(btnSelect);

			var btnDel = new Button();
			btnDel.Set("icon_max_width", 0);
			SetupButton(btnDel, "✕", () =>
			{
				GameHost.Instance?.DeleteCoordinateExternal(coordinateName);
				RefreshCoordinateListExternal();
			}, 10, $"Delete coordinate '{coordinateName}'");
			btnDel.CustomMinimumSize = new Vector2(28, 24);
			row.AddChild(btnDel);
		}
	}

	private void OpenOverlayModePopup()
	{
		if (_popupOverlayMode == null || GameHost.Instance == null || _btnToggleGrid == null) return;

		var mode = GameHost.Instance.EditorGridMode;
		bool isGrid = mode == GameHost.GridOverlayMode.Grid || mode == GameHost.GridOverlayMode.Both;
		bool isPolar = mode == GameHost.GridOverlayMode.Polar || mode == GameHost.GridOverlayMode.Both;

		int gridIdx = _popupOverlayMode.GetItemIndex(0);
		int polarIdx = _popupOverlayMode.GetItemIndex(1);
		int camBoundsIdx = _popupOverlayMode.GetItemIndex(2);
		int wireframeIdx = _popupOverlayMode.GetItemIndex(3);

		if (gridIdx >= 0) _popupOverlayMode.SetItemChecked(gridIdx, isGrid);
		if (polarIdx >= 0) _popupOverlayMode.SetItemChecked(polarIdx, isPolar);
		if (camBoundsIdx >= 0) _popupOverlayMode.SetItemChecked(camBoundsIdx, GameHost.Instance.EditorCameraBoundsVisible);
		if (wireframeIdx >= 0)
		{
			bool isWireframe = GetViewport()?.DebugDraw == Viewport.DebugDrawEnum.Wireframe;
			_popupOverlayMode.SetItemChecked(wireframeIdx, isWireframe);
		}

		var globalRect = _btnToggleGrid.GetGlobalRect();
		var popupPos = new Vector2I((int)globalRect.Position.X, (int)(globalRect.Position.Y + globalRect.Size.Y + 2));
		_popupOverlayMode.Position = popupPos;
		_popupOverlayMode.Popup();
	}

	public void UpdateGridOverlayExternal(GameHost.GridOverlayMode mode)
	{
		if (_btnToggleGrid != null)
		{
			_btnToggleGrid.Text = "\uf84c";
			_btnToggleGrid.TooltipText = TranslationServer.Translate("Overlay");
		}
		if (_popupOverlayMode != null)
		{
			bool isGrid = mode == GameHost.GridOverlayMode.Grid || mode == GameHost.GridOverlayMode.Both;
			bool isPolar = mode == GameHost.GridOverlayMode.Polar || mode == GameHost.GridOverlayMode.Both;
			int gridIdx = _popupOverlayMode.GetItemIndex(0);
			int polarIdx = _popupOverlayMode.GetItemIndex(1);
			if (gridIdx >= 0) _popupOverlayMode.SetItemChecked(gridIdx, isGrid);
			if (polarIdx >= 0) _popupOverlayMode.SetItemChecked(polarIdx, isPolar);
		}
		UpdatePolarSubControlsVisibility(mode);
	}

	public void UpdatePolarSubControlsVisibility(GameHost.GridOverlayMode mode)
	{
		bool isPolar = (GameHost.Instance != null && GameHost.Instance.EditorMirrorMode == MirrorMode.Rotational) || mode == GameHost.GridOverlayMode.Polar || mode == GameHost.GridOverlayMode.Both;
		if (_rowPolarConfig != null)
		{
			_rowPolarConfig.Visible = isPolar;
		}
	}

	public void UpdateMeasureToolExternal(bool active)
	{
		if (_btnTapeMeasure != null)
		{
			_btnTapeMeasure.Text = "\uf545";
			_btnTapeMeasure.TooltipText = TranslationServer.Translate($"Tape Measure Tool: {(active ? "ACTIVE" : "INACTIVE")} (U)");
			_btnTapeMeasure.Modulate = active ? new Color(1.3f, 1.15f, 0.7f) : new Color(1f, 1f, 1f);
		}
	}

	public void UpdateMeasureTelemetry(float eucTiles, float eucWorld, float manhattanTiles, float dx, float dz, float dy, float angleDeg, float slopePct)
	{
		if (_panelMeasurementHUD == null || _lblMeasureTelemetry == null) return;
		_panelMeasurementHUD.Visible = true;

		string slopeStr;
		float absDx = Mathf.Abs(dx);
		float absDz = Mathf.Abs(dz);
		if (absDx < 0.01f && absDz < 0.01f)
		{
			slopeStr = "0:0";
		}
		else if (absDx < 0.01f)
		{
			slopeStr = TranslationServer.Translate("Vertical");
		}
		else if (absDz < 0.01f)
		{
			slopeStr = TranslationServer.Translate("Horizontal (1:0)");
		}
		else
		{
			float ratio = absDz / absDx;
			if (Mathf.Abs(ratio - Mathf.Round(ratio)) < 0.05f)
			{
				slopeStr = $"{Mathf.Round(ratio):0}:1";
			}
			else if (Mathf.Abs(1.0f / ratio - Mathf.Round(1.0f / ratio)) < 0.05f)
			{
				slopeStr = $"1:{Mathf.Round(1.0f / ratio):0}";
			}
			else
			{
				slopeStr = $"{ratio:F1}:1";
			}
		}

		string text = string.Format(
			TranslationServer.Translate("\uf545 MEASURE: Manhattan: {0:F1}T | Euclidean: {1:F1}T ({2:F1}m) | Angle: {3:F1}° | Slope: {4}"),
			manhattanTiles, eucTiles, eucWorld, angleDeg, slopeStr);

		if (Mathf.Abs(dy) > 0.01f)
		{
			text += string.Format(TranslationServer.Translate(" | Elev: {0:+0.0;-0.0;0.0}m ({1:F1}%)"), dy, slopePct);
		}

		_lblMeasureTelemetry.Text = text;
	}

	public void ClearMeasureTelemetry()
	{
		if (_panelMeasurementHUD != null)
		{
			_panelMeasurementHUD.Visible = false;
		}
	}

	public void UpdatePasteTelemetry(int deltaX, int deltaZ, float distTiles, float distWorld, float angleDeg)
	{
		if (_lblPasteTelemetry != null)
		{
			_lblPasteTelemetry.Visible = true;
			_lblPasteTelemetry.Text = string.Format("ΔX: {0:+0;-0;0}, ΔZ: {1:+0;-0;0} | Pivot: {2:F1}T ({3:F1}m) @ {4:F1}°",
				deltaX, deltaZ, distTiles, distWorld, angleDeg);
		}
	}

	public void ClearPasteTelemetry()
	{
		if (_lblPasteTelemetry != null)
		{
			_lblPasteTelemetry.Text = "";
		}
	}

	public void UpdateCameraBoundsOverlayExternal(bool visible)
	{
		if (_popupOverlayMode != null)
		{
			int camBoundsIdx = _popupOverlayMode.GetItemIndex(2);
			if (camBoundsIdx >= 0) _popupOverlayMode.SetItemChecked(camBoundsIdx, visible);
		}
	}

	public void UpdateWireframeOverlayExternal(bool enabled)
	{
		if (_popupOverlayMode != null)
		{
			int wireframeIdx = _popupOverlayMode.GetItemIndex(3);
			if (wireframeIdx >= 0) _popupOverlayMode.SetItemChecked(wireframeIdx, enabled);
		}
	}

	public void ToggleFreeCamera()
	{
		var cam = GameHost.Instance?.MainCamera as CameraControl;
		if (cam != null)
		{
			cam.ToggleFreeCamera();
			UpdateFreeCameraExternal(cam.IsFreeCamera);
			ShowFeedback(cam.IsFreeCamera
				? TranslationServer.Translate("Free Camera: ON (RMB drag to look, WASD/QE to fly, Wheel to zoom)")
				: TranslationServer.Translate("Free Camera: OFF (Clamped to game bounds)"));
		}
	}

	public void UpdateFreeCameraExternal(bool isFreeCam)
	{
		if (_chkFreeCamera != null)
		{
			_chkFreeCamera.SetPressedNoSignal(isFreeCam);
		}
	}

	private void StylePopupButton(Button btn, string text, string tooltip)
	{
		btn.Text = TranslationServer.Translate(text);
		btn.TooltipText = TranslationServer.Translate(tooltip);
		btn.FocusMode = Control.FocusModeEnum.None;
		btn.CustomMinimumSize = new Vector2(0, 24);
		btn.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
		btn.AddThemeFontSizeOverride("font_size", 11);
		var font = GetFontAwesomeFont();
		if (font != null)
		{
			btn.AddThemeFontOverride("font", font);
		}
		btn.AddThemeStyleboxOverride("normal", UIStyle.CreateOptionButtonNormal());
		btn.AddThemeStyleboxOverride("hover", UIStyle.CreateOptionButtonHover());
		btn.AddThemeStyleboxOverride("pressed", UIStyle.CreateOptionButtonPressed());
		btn.AddThemeStyleboxOverride("focus", new StyleBoxEmpty());
		btn.AddThemeColorOverride("font_color", new Color(0.95f, 0.90f, 0.82f));
		btn.AddThemeColorOverride("font_hover_color", UIStyle.ColorGold);
		btn.Alignment = HorizontalAlignment.Left;
	}

	private void OpenCameraPopup()
	{
		if (_popupCamera == null || GameHost.Instance == null || _btnToggleCamera == null) return;

		UpdateCameraPopupControls();

		var globalRect = _btnToggleCamera.GetGlobalRect();
		var popupPos = new Vector2I((int)globalRect.Position.X, (int)(globalRect.Position.Y + globalRect.Size.Y + 2));
		_popupCamera.Position = popupPos;
		_popupCamera.Popup();
	}

	public void UpdateCameraPopupControls()
	{
		if (GameHost.Instance == null) return;
		var camera = GameHost.Instance.MainCamera as CameraControl;
		if (camera != null)
		{
			UpdateCameraAngleButtonText(camera.IsTopDown());
			UpdateFreeCameraExternal(camera.IsFreeCamera);
		}
	}

	public void ToggleShadows()
	{
		if (GameHost.Instance != null)
		{
			GameHost.Instance.EditorDisableShadows = !GameHost.Instance.EditorDisableShadows;
			GameHost.Instance.UpdateEditorShadows();
			UpdateShadowsExternal(GameHost.Instance.EditorDisableShadows);
			ShowFeedback(GameHost.Instance.EditorDisableShadows
				? TranslationServer.Translate("Shadows: OFF")
				: TranslationServer.Translate("Shadows: ON"));
		}
	}

	public void UpdateShadowsExternal(bool disabled)
	{
		if (_chkEnvShadows != null)
		{
			_chkEnvShadows.SetPressedNoSignal(!disabled);
		}
	}

	private void SetupEnvLightingDropdown(OptionButton opt)
	{
		opt.Clear();
		opt.AddItem(TranslationServer.Translate("\uf185 Day"), 0);
		opt.AddItem(TranslationServer.Translate("\uf6c4 Dusk"), 1);
		opt.AddItem(TranslationServer.Translate("\uf186 Night"), 2);
		opt.AddItem(TranslationServer.Translate("\uf6c3 Dawn"), 3);
		StyleOptionButtonPopup(opt);
	}

	private void SetupEnvWeatherDropdown(OptionButton opt)
	{
		opt.Clear();
		opt.AddItem(TranslationServer.Translate("\uf0c2 Clear"), 0);
		opt.AddItem(TranslationServer.Translate("\uf73d Rain"), 1);
		opt.AddItem(TranslationServer.Translate("\uf2dc Snow"), 2);
		opt.AddItem(TranslationServer.Translate("\uf75f Fog"), 3);
		StyleOptionButtonPopup(opt);
	}

	private void OpenEnvironmentPopup()
	{
		if (_popupEnvironment == null || GameHost.Instance == null || _btnToggleEnvironment == null) return;

		UpdateEnvironmentPopupControls();

		var globalRect = _btnToggleEnvironment.GetGlobalRect();
		var popupPos = new Vector2I((int)globalRect.Position.X, (int)(globalRect.Position.Y + globalRect.Size.Y + 2));
		_popupEnvironment.Position = popupPos;
		_popupEnvironment.Popup();
	}

	public void UpdateEnvironmentPopupControls()
	{
		if (GameHost.Instance == null) return;

		if (_optEnvLighting != null)
		{
			int timeIdx = GameHost.Instance.TimeOfDayIndex;
			_optEnvLighting.Select(Mathf.Clamp(timeIdx, 0, 3));
		}

		if (_optEnvWeather != null && GameHost.Instance.EnvironmentService != null)
		{
			string curWeather = GameHost.Instance.EnvironmentService.GetCurrentWeather();
			int weatherIdx = (curWeather?.ToLowerInvariant()) switch
			{
				"rain" => 1,
				"snow" => 2,
				"fog" => 3,
				_ => 0
			};
			_optEnvWeather.Select(weatherIdx);
		}

		if (_chkEnvShadows != null)
		{
			_chkEnvShadows.SetPressedNoSignal(!GameHost.Instance.EditorDisableShadows);
		}
	}

	public void UpdateEnvLightingSelection(int timeOfDayIndex)
	{
		if (_optEnvLighting != null)
		{
			int idx = Mathf.Clamp(timeOfDayIndex, 0, 3);
			_optEnvLighting.Select(idx);
		}
	}

	public void UpdateEnvWeatherSelection(string weatherType)
	{
		if (_optEnvWeather != null)
		{
			int idx = (weatherType?.ToLowerInvariant()) switch
			{
				"rain" => 1,
				"snow" => 2,
				"fog" => 3,
				_ => 0
			};
			_optEnvWeather.Select(idx);
		}
	}

	private void SetEnvironmentLighting(int index)
	{
		if (GameHost.Instance != null)
		{
			var res = GameHost.Instance.SetTimeOfDay(index);
			string timeName = GameHost.Instance.EnvironmentService?.GetTimeOfDayName(res.TimeOfDayIndex) ?? "Day";
			string icon = res.TimeOfDayIndex switch
			{
				0 => "☀️",
				1 => "🌅",
				2 => "🌙",
				3 => "🌄",
				_ => "☀️"
			};
			ShowFeedback(string.Format(TranslationServer.Translate("Lighting: {0} {1}"), icon, TranslationServer.Translate(timeName)));
		}
	}

	private void SetEnvironmentWeather(int index)
	{
		if (GameHost.Instance != null && GameHost.Instance.EnvironmentService != null)
		{
			string weatherType = index switch
			{
				1 => "rain",
				2 => "snow",
				3 => "fog",
				_ => "clear"
			};
			string applied = GameHost.Instance.EnvironmentService.SetWeather(weatherType, GameHost.Instance);
			string icon = applied switch
			{
				"rain" => "🌧️",
				"snow" => "❄️",
				"fog" => "🌫️",
				_ => "☀️"
			};
			ShowFeedback(string.Format(TranslationServer.Translate("Weather: {0} {1}"), icon, TranslationServer.Translate(applied.Capitalize())));
		}
	}

	public void EnsureCameraBoundsVisible()
	{
		if (GameHost.Instance != null && !GameHost.Instance.EditorCameraBoundsVisible)
		{
			GameHost.Instance.EditorCameraBoundsVisible = true;
			GameHost.Instance.UpdateCameraBoundsOverlayVisibility();
			UpdateCameraBoundsOverlayExternal(true);
		}
	}

	public void UpdateCameraBoundsUI()
	{
		_mapSettingsDialog?.UpdateCameraBoundsUI();
	}

	public void UpdateSelectedSkyboxExternal(string path)
	{
		_mapSettingsDialog?.SelectSkybox(path);
	}

	public void UpdatePathingOverlayExternal(bool visible)
	{
	}

	public void ToggleVSCodeEditor()
	{
		if (OperatingSystem.IsWindows())
		{
			if (VSCodeManager.Instance.IsVisible)
			{
				VSCodeManager.Instance.Focus();
			}
			else
			{
				GenerateVSCodeFilesExternal();
				VSCodeManager.Instance.SetVisible(true);
			}
		}
	}

	public void BackToHubAction()
	{
		if (HasUnsavedChanges())
		{
			ShowConfirmationDialog(
				"You haven't saved yet",
				onConfirm: () =>
				{
					WriteCleanExitMarker();
					UIManager.Instance.TransitionTo(GameScreen.MainMenu);
				},
				confirmText: "Quit",
				cancelText: "Stay"
			);
		}
		else
		{
			WriteCleanExitMarker();
			UIManager.Instance.TransitionTo(GameScreen.MainMenu);
		}
	}

	public void HandleQuitRequest()
	{
		if (HasUnsavedChanges())
		{
			ShowConfirmationDialog(
				"You haven't saved yet",
				onConfirm: () =>
				{
					WriteCleanExitMarker();
					GetTree().Quit();
				},
				confirmText: "Quit",
				cancelText: "Stay"
			);
		}
		else
		{
			WriteCleanExitMarker();
			GetTree().Quit();
		}
	}


	public void PublishMapActionExternal()
	{
		string wsPath = !string.IsNullOrEmpty(_tempWorkspacePath) ? _tempWorkspacePath : MapWorkspaceService.GetActiveWorkspacePath();
		var (assetsValid, missingAssets) = MapAssetHelper.ValidateWorkspaceAssets(wsPath);
		if (!assetsValid)
		{
			ShowFeedback(string.Format(TranslationServer.Translate("Publish failed: Map is missing required asset files:\n{0}"), string.Join(", ", missingAssets.Take(4)) + (missingAssets.Count > 4 ? "..." : "")));
			AppendWasmConsoleLog($"[ERROR] Publish failed. Missing required asset files:\n  {string.Join("\n  ", missingAssets)}");
			return;
		}

		var (sizesValid, oversizedAssets) = MapAssetHelper.ValidateWorkspaceAssetSizes(wsPath);
		if (!sizesValid)
		{
			string oversizedSummary = string.Join("\n", oversizedAssets.Take(4).Select(o => $"• {o.RelativePath} ({o.SizeMB:F2} MB > 15 MB)")) + (oversizedAssets.Count > 4 ? "\n..." : "");
			ShowFeedback(string.Format(TranslationServer.Translate("Publish failed: Assets exceed maximum 15 MB size limit:\n{0}"), oversizedSummary));
			AppendWasmConsoleLog($"[ERROR] Publish failed. Assets exceed maximum 15 MB size limit:\n{string.Join("\n", oversizedAssets.Select(o => $"  {o.RelativePath} ({o.SizeMB:F2} MB)"))}");
			return;
		}

		var overlay = new ColorRect();
		overlay.Name = "PublishInstructionsOverlay";
		overlay.Color = new Color(0, 0, 0, 0.7f);
		overlay.SetAnchorsPreset(LayoutPreset.FullRect);
		AddChild(overlay);

		var center = new CenterContainer();
		center.SetAnchorsPreset(LayoutPreset.FullRect);
		overlay.AddChild(center);

		var panel = new PanelContainer();
		panel.CustomMinimumSize = new Vector2(1200, 800);
		var style = new StyleBoxFlat();
		style.BgColor = new Color(0.1f, 0.1f, 0.15f, 0.95f);
		style.BorderWidthTop = 2; style.BorderWidthBottom = 2; style.BorderWidthLeft = 2; style.BorderWidthRight = 2;
		style.BorderColor = new Color(0.3f, 0.3f, 0.35f, 1f);
		style.CornerRadiusTopLeft = 4; style.CornerRadiusTopRight = 4; style.CornerRadiusBottomLeft = 4; style.CornerRadiusBottomRight = 4;
		panel.AddThemeStyleboxOverride("panel", style);
		center.AddChild(panel);

		var vbox = new VBoxContainer();
		vbox.AddThemeConstantOverride("separation", 12);
		vbox.SetAnchorsPreset(LayoutPreset.FullRect);
		vbox.CustomMinimumSize = new Vector2(1180, 780);
		var margin = new MarginContainer();
		margin.AddThemeConstantOverride("margin_top", 10);
		margin.AddThemeConstantOverride("margin_bottom", 10);
		margin.AddThemeConstantOverride("margin_left", 10);
		margin.AddThemeConstantOverride("margin_right", 10);
		margin.AddChild(vbox);
		panel.AddChild(margin);

		var title = new Label();
		title.Text = "Publish Map Instructions";
		title.HorizontalAlignment = HorizontalAlignment.Center;
		title.AddThemeFontSizeOverride("font_size", 24);
		vbox.AddChild(title);

		var optType = new OptionButton();
		optType.AddItem("Custom Arcade Map", 0);
		optType.AddItem("Reusable Asset Pack", 1);
		optType.Selected = _mapSettingsDialog?.SelectedMapTypeIndex ?? 0;
		vbox.AddChild(optType);

		var scroll = new ScrollContainer();
		scroll.SizeFlagsVertical = Control.SizeFlags.ExpandFill;
		var instructionsText = new RichTextLabel();
		instructionsText.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
		instructionsText.SizeFlagsVertical = Control.SizeFlags.ExpandFill;
		instructionsText.BbcodeEnabled = true;
		scroll.AddChild(instructionsText);
		vbox.AddChild(scroll);

		string textArcade = TranslationServer.Translate("🚀 [b]Publishing Your Custom Map[/b]\nTo maintain a high-quality community arcade, all new maps start in a Beta-Testing Phase.\n\nOnce your map hits our community play-time metrics (gaining enough unique players, ratings, & community playtime), it will become available for official publishing to community maps screens.\n\nUntil then, it is up to you to share it with the community & market it until you hit that threshold.\nYour map is ready to play right now! Click the 'Export' button to share it. Host a lobby with your map & wait for players to join.\nMessage the community via discord channels, etc to explain your map & convince them to try it. If they enjoy it, they will probably re-host it, which will help you hit the graduation threshold more quickly.\n\nWhile in testing, your map name will include a prefix [Beta-Testing] so players know it's an active work-in-progress. However, you should still do as much personal testing as possible before public hosting to avoid a frustrating experience for your testers.\n\nAfter graduation, your map name will be permanently reserved to your creator profile so no one else can use that same name.");
		string textAssetPack = TranslationServer.Translate("📦 [b]Publishing a Reusable Asset Pack[/b]\nWant to share your custom 3D models, audio, or code scripts with other map makers?\n\nIn Realm, Asset Packs are published as playable Showcase/Demo Maps.\n\n[b]Build a Playground:[/b] Turn your asset pack into a map where players can preview the functionality provided by your systems, view your models, etc.\n\n[b]Gather Community Metrics:[/b] Just like a regular map, your asset pack will start in a \"beta\" phase before being promoted on community discovery pages. Read the \"Custom Arcade Map\" section for more information.\n\n[b]Easy Importing:[/b] Once a player has a copy of your map, they can import assets from it into their maps via the map editor.\n\n[b]Automatic Credit:[/b] When creators import from you, the system tracks your files' signatures and automatically adds your info to their map credits.");

		Action updateText = () => {
			instructionsText.Text = optType.Selected == 0 ? textArcade : textAssetPack;
		};
		updateText();
		optType.ItemSelected += (_) => updateText();

		var (unprunedCount, unnormalizedPotentialCount, potentialSavedBytes) = MapNormalizationHelper.CheckOptimizationState(wsPath);
		if (unprunedCount > 0 || unnormalizedPotentialCount > 0)
		{
			var tipPanel = new PanelContainer();
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
			tipMargin.AddThemeConstantOverride("margin_top", 6);
			tipMargin.AddThemeConstantOverride("margin_bottom", 6);
			tipMargin.AddThemeConstantOverride("margin_left", 10);
			tipMargin.AddThemeConstantOverride("margin_right", 10);
			tipPanel.AddChild(tipMargin);

			var tipLabel = new Label();
			tipLabel.Text = "💡 " + string.Format(TranslationServer.Translate("Optimization Advisory: We recommend clicking 'Prune Unused' and 'Normalize References' in the Asset Manager before publishing to remove unused files and reference shared greenlit assets (potential savings: {0})."), MapStorageService.FormatBytes(potentialSavedBytes));
			tipLabel.AutowrapMode = TextServer.AutowrapMode.WordSmart;
			tipLabel.AddThemeFontSizeOverride("font_size", 12);
			tipLabel.AddThemeColorOverride("font_color", UIStyle.ColorGold);
			tipMargin.AddChild(tipLabel);
			vbox.AddChild(tipPanel);
		}

		var chkFullExport = new CheckBox();
		chkFullExport.Text = TranslationServer.Translate("Full Export (Include all physical assets in package, bypassing greenlit deduplication)");
		chkFullExport.ButtonPressed = false;
		chkFullExport.AddThemeConstantOverride("icon_max_width", 0);
		UIStyle.ApplyCheckboxStyle(chkFullExport);
		vbox.AddChild(chkFullExport);

		string keyDir = ProjectSettings.GlobalizePath("user://appdata/keys/");
		string defaultUsername = LobbyManager.Instance?.AuthenticatedUsername ?? System.Environment.UserName;
		var (_, keyData, keyPath, _) = AuthorshipKeyHelper.GetOrGenerateKeyInfo(keyDir, defaultUsername);
		string currentAuthorName = !string.IsNullOrWhiteSpace(keyData?.UserName) ? keyData.UserName : defaultUsername;
		string authorPubKey = !string.IsNullOrWhiteSpace(keyData?.PublicKey) ? keyData.PublicKey : "";
		string shortPubKey = authorPubKey.Length > 16 ? $"{authorPubKey[..8]}...{authorPubKey[^8..]}" : authorPubKey;

		var identityPanel = new PanelContainer();
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
		var idHeaderLabel = new Label();
		idHeaderLabel.Text = "🔑 " + TranslationServer.Translate("AUTHOR IDENTITY & KEY VERIFICATION");
		idHeaderLabel.AddThemeFontSizeOverride("font_size", 13);
		idHeaderLabel.AddThemeColorOverride("font_color", UIStyle.ColorGold);
		idHeaderLabel.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
		idHeaderHBox.AddChild(idHeaderLabel);

		var btnEditKey = new Button();
		btnEditKey.AddThemeConstantOverride("icon_max_width", 0);
		btnEditKey.Text = TranslationServer.Translate("Manage Key / Identity");
		btnEditKey.AddThemeFontSizeOverride("font_size", 11);
		btnEditKey.CustomMinimumSize = new Vector2(160, 26);
		btnEditKey.Pressed += () =>
		{
			if (_authorSignatureDialog == null)
			{
				_authorSignatureDialog = new AuthorSignatureDialog(this);
			}
			_authorSignatureDialog.OpenDialog();
		};
		idHeaderHBox.AddChild(btnEditKey);
		idVBox.AddChild(idHeaderHBox);

		var idDetailsLabel = new Label();
		idDetailsLabel.Text = string.Format(TranslationServer.Translate("Author: {0} | Public Key: {1}\nKey File: {2}"), currentAuthorName, shortPubKey, keyPath);
		idDetailsLabel.AddThemeFontSizeOverride("font_size", 11);
		idDetailsLabel.AddThemeColorOverride("font_color", new Color(0.85f, 0.9f, 1.0f));
		idDetailsLabel.AutowrapMode = TextServer.AutowrapMode.WordSmart;
		idVBox.AddChild(idDetailsLabel);

		var idBackupWarn = new Label();
		idBackupWarn.Text = "⚠️ " + TranslationServer.Translate("Important: Back up your key file! Your authorship key proves ownership of your map name and allows future updates. If you lose your key file, you will permanently lose ownership and the ability to update this map.");
		idBackupWarn.AddThemeFontSizeOverride("font_size", 11);
		idBackupWarn.AddThemeColorOverride("font_color", new Color(1.0f, 0.75f, 0.35f));
		idBackupWarn.AutowrapMode = TextServer.AutowrapMode.WordSmart;
		idVBox.AddChild(idBackupWarn);

		vbox.AddChild(identityPanel);

		var hbox = new HBoxContainer();
		hbox.Alignment = BoxContainer.AlignmentMode.Center;
		hbox.AddThemeConstantOverride("separation", 20);
		vbox.AddChild(hbox);

		var btnPublish = new Button();
		btnPublish.AddThemeConstantOverride("icon_max_width", 0);
		btnPublish.Text = TranslationServer.Translate("Publish Map");
		btnPublish.CustomMinimumSize = new Vector2(140, 40);
		btnPublish.Pressed += () =>
		{
			bool isFullExport = chkFullExport.ButtonPressed;
			overlay.QueueFree();
			PublishMapAction(isFullExport);
		};
		hbox.AddChild(btnPublish);

		var btnClose = new Button();
		btnClose.AddThemeConstantOverride("icon_max_width", 0);
		btnClose.Text = TranslationServer.Translate("Close");
		btnClose.CustomMinimumSize = new Vector2(120, 40);
		btnClose.Pressed += () => overlay.QueueFree();
		hbox.AddChild(btnClose);
	}


	public void UndoAction()
	{
		EditorHistoryManager.Undo();
		ShowFeedback("Undo Action performed");
	}

	public void RedoAction()
	{
		EditorHistoryManager.Redo();
		ShowFeedback("Redo Action performed");
	}

	public void DeleteSelectedObjectAction()
	{
		DeleteSelectedObject();
	}

	private void DeleteSelectedObject()
	{
		if (GameHost.Instance == null)
		{
			ShowFeedback("[Debug] DeleteSelectedObject: GameHost.Instance is NULL!");
			return;
		}
		var selected = GameHost.Instance.SelectedEditorObject;
		if (GodotObject.IsInstanceValid(selected))
		{
			Vector3 pos = (selected is Node3D n) ? n.Position : Vector3.Zero;
			GameHost.Instance.SelectedEditorObject = null;
			var action = GameHost.Instance.DeleteObjectAtWithUndo(selected, pos);
			if (action != null)
			{
				EditorHistoryManager.RecordAction(action);
			}
			else
			{
				GameHost.Instance.DeleteNodeExternal(selected);
			}
			_placedObjectsDialog?.RefreshIfOpen();
		}
	}

	public void RotateSelectedObjectAction(float angleDelta)
	{
		RotateSelectedObject(angleDelta);
	}

	public void ScaleSelectedObjectAction(float scaleMultiplier)
	{
		if (scaleMultiplier < 0f)
		{
			ResetScaleSelectedObject();
		}
		else
		{
			ScaleSelectedObject(scaleMultiplier - 1.0f);
		}
	}

	public void SetSpawnAsEnemy(bool isEnemy)
	{
		if (GameHost.Instance != null)
		{
			GameHost.Instance.PlaceUnitIsEnemy = isEnemy;
		}
	}

	public void SelectCategoryItemExternal(string category, string filename)
	{
		_entityPaletteController?.SelectCategoryItemExternal(category, filename);
	}

	public void SelectPickedUnitOrProp(string id, bool isBuilding)
	{
		if (isBuilding)
		{
			_entityPaletteController?.SelectCategoryItemExternal("Buildings", id);
		}
		else
		{
			_entityPaletteController?.SelectCategoryItemExternal("Units", id);
		}
	}

	public void SelectPickedDecal(string decalId)
	{
		_entityPaletteController?.SelectCategoryItemExternal("Decals", decalId);
	}

	public void RefreshEntityPalette()
	{
		_entityPaletteController?.SelectCategory(_entityPaletteController.CurrentCategory, triggerAddObject: false);
	}

	public bool IsApplyGroundTextureEnabled()
	{
		if (GameHost.Instance != null)
		{
			var tool = GameHost.Instance.ActiveEditorTool;
			bool isPaintTool = tool == GameHost.EditorTool.PaintTexture;
			if (isPaintTool && _chkApplyGroundTexture != null && _chkApplyGroundTexture.Visible)
			{
				return _chkApplyGroundTexture.ButtonPressed;
			}
		}
		return true;
	}

	public bool IsApplyCliffTextureEnabled()
	{
		if (GameHost.Instance != null)
		{
			var tool = GameHost.Instance.ActiveEditorTool;
			bool isPaintTool = tool == GameHost.EditorTool.PaintTexture;
			if (isPaintTool && _chkApplyCliffTexture != null && _chkApplyCliffTexture.Visible)
			{
				return _chkApplyCliffTexture.ButtonPressed;
			}
		}
		return true;
	}

	public void SelectPaintSwatchByIndex(int index)
	{
		if (index >= 0 && index < _swatchButtons.Count)
		{
			if (_chkApplyCliffTexture != null && _chkApplyCliffTexture.ButtonPressed && (_chkApplyGroundTexture == null || !_chkApplyGroundTexture.ButtonPressed))
			{
				SelectCliffTexture(index);
			}
			else
			{
				HighlightSwatch(_swatchButtons[index]);
				TriggerToolSelection(GameHost.EditorTool.PaintTexture, _swatchButtons[index]);
			}
		}
	}

	private bool _lastBrushShapeIsSquare;
	private bool _hasLastBrushShape;
	public void UpdateBrushShapeExternal(bool isSquare)
	{
		if (_hasLastBrushShape && _lastBrushShapeIsSquare == isSquare) return;
		_hasLastBrushShape = true;
		_lastBrushShapeIsSquare = isSquare;
		if (_btnBrushShape != null)
		{
			_btnBrushShape.Text = isSquare ? TranslationServer.Translate("\uf0c8 BRUSH: SQUARE") : TranslationServer.Translate("\uf111 BRUSH: CIRCLE");
		}
		if (_btnClipboardBrushShape != null)
		{
			_btnClipboardBrushShape.Text = isSquare ? TranslationServer.Translate("\uf0c8 BRUSH: SQUARE") : TranslationServer.Translate("\uf111 BRUSH: CIRCLE");
		}
		if (GameHost.Instance != null && GameHost.Instance.GroundTerrain != null && _editorService != null && _editorService.SelectionStart != null && _editorService.SelectionEnd != null)
		{
			var (minX, minZ, maxX, maxZ) = _editorService.GetCurrentSelectionBounds();
			GameHost.Instance.RebuildSelectionHighlightMeshExternal(minX, minZ, maxX, maxZ);
		}
	}

	private float _lastRotationExternal = float.NaN;
	public void UpdateRotationExternal(float angle)
	{
		if (Mathf.IsEqualApprox(_lastRotationExternal, angle)) return;
		_lastRotationExternal = angle;
		if (_lblPlacementRotateValue != null) _lblPlacementRotateValue.Text = angle.ToString("F0") + "°";
		if (_sldPlacementRotate != null && !Mathf.IsEqualApprox((float)_sldPlacementRotate.Value, angle)) _sldPlacementRotate.Value = angle;
	}

	private float _lastPasteRotationExternal = float.NaN;
	public void UpdatePasteRotationExternal(float angle)
	{
		if (Mathf.IsEqualApprox(_lastPasteRotationExternal, angle)) return;
		_lastPasteRotationExternal = angle;
		if (_lblPasteRotation != null) _lblPasteRotation.Text = angle.ToString("F0") + "°";
		if (_sldPasteRotation != null && !Mathf.IsEqualApprox((float)_sldPasteRotation.Value, angle)) _sldPasteRotation.Value = angle;
	}

	private PasteReflection _lastPasteReflectionExternal = (PasteReflection)(-1);
	public void UpdatePasteReflectionExternal(PasteReflection reflection)
	{
		if (_lastPasteReflectionExternal == reflection) return;
		_lastPasteReflectionExternal = reflection;
		UpdatePasteReflectionButtonText();
	}

	private float _lastScaleExternal = float.NaN;
	public void UpdateScaleExternal(float scale)
	{
		if (Mathf.IsEqualApprox(_lastScaleExternal, scale)) return;
		_lastScaleExternal = scale;
		if (_lblPlacementScaleValue != null) _lblPlacementScaleValue.Text = scale.ToString("F1") + "x";
		if (_sldPlacementScale != null && !Mathf.IsEqualApprox((float)_sldPlacementScale.Value, scale)) _sldPlacementScale.Value = scale;
	}

	private float _lastBrushSizeExternal = float.NaN;
	public void UpdateBrushSizeExternal(float size)
	{
		if (Mathf.IsEqualApprox(_lastBrushSizeExternal, size)) return;
		_lastBrushSizeExternal = size;
		if (_sldBrushSize != null && !Mathf.IsEqualApprox((float)_sldBrushSize.Value, size))
		{
			_sldBrushSize.Value = Mathf.Round(size);
		}
		if (_lblBrushSizeValue != null)
		{
			_lblBrushSizeValue.Text = Mathf.Round(size).ToString("F0");
		}
	}

	public void UpdateCameraAngleButtonText(bool isTopDown)
	{
		if (_btnCameraAngle != null)
		{
			_btnCameraAngle.Text = isTopDown
				? TranslationServer.Translate("\uf1b2 Tilt (C)")
				: TranslationServer.Translate("\uf1b2 Top-Down (C)");
		}
	}

	public void UpdateBrushStrengthExternal(float strength)
	{
		if (_sldBrushStrength != null)
		{
			_sldBrushStrength.Value = strength;
		}
		if (_lblBrushStrengthValue != null)
		{
			_lblBrushStrengthValue.Text = strength.ToString("F1");
		}
	}

	public void UpdateBlockModeExternal(bool enabled)
	{
		if (_chkBlockMode != null)
		{
			_chkBlockMode.ButtonPressed = enabled;
		}
	}

	public void UpdateBlockLevelHeightExternal(float step)
	{
		if (_sldBlockStep != null)
		{
			_sldBlockStep.Value = step;
		}
		if (_lblBlockStepValue != null)
		{
			_lblBlockStepValue.Text = step.ToString("F1") + " m";
		}
	}

	public void UpdateExactHeightExternal(float height)
	{
		if (_sldHeight != null)
		{
			_sldHeight.Value = height;
		}
		if (_lblHeightValue != null)
		{
			_lblHeightValue.Text = height.ToString("F1") + "m";
		}
	}

	public void SelectToolFromHotkey(GameHost.EditorTool tool)
	{
		Button targetBtn = null;
		switch (tool)
		{
			case GameHost.EditorTool.Raise: targetBtn = _btnRaise; break;
			case GameHost.EditorTool.Lower: targetBtn = _btnLower; break;
			case GameHost.EditorTool.Height: targetBtn = _btnHeight; break;
			case GameHost.EditorTool.Smooth: targetBtn = _btnSmooth; break;
			case GameHost.EditorTool.Plateau: targetBtn = _btnPlateau; break;
			case GameHost.EditorTool.Ramp: targetBtn = _btnRamp; break;
			case GameHost.EditorTool.Noise: targetBtn = _btnNoise; break;
			case GameHost.EditorTool.Water: targetBtn = _btnWater; break;
			case GameHost.EditorTool.PaintTexture: targetBtn = _btnTextureBrush; break;
			case GameHost.EditorTool.FloodFill: targetBtn = _btnFloodFill; break;
			case GameHost.EditorTool.PaintPathing: targetBtn = _btnPathingBrush; break;
			case GameHost.EditorTool.FloodFillPathing: targetBtn = _btnFloodFillPathing; break;
			case GameHost.EditorTool.DrawCoordinate: targetBtn = _btnDrawCoordinate; break;
			case GameHost.EditorTool.SelectArea: targetBtn = _btnSelectArea; break;
			case GameHost.EditorTool.PasteArea: targetBtn = _btnPaste; break;
			case GameHost.EditorTool.PlaceUnit:
			case GameHost.EditorTool.PlaceProp:
			case GameHost.EditorTool.PlaceDecal:
				targetBtn = _btnAddObject;
				break;
			case GameHost.EditorTool.DeleteObject: targetBtn = _btnDeleteObject; break;
			case GameHost.EditorTool.SelectMove: targetBtn = _btnSelectMove; break;
			case GameHost.EditorTool.Eyedropper: targetBtn = _btnEyedropper; break;
			case GameHost.EditorTool.Measure: targetBtn = _btnTapeMeasure; break;
		}
		if (targetBtn != null)
		{
			TriggerToolSelection(tool, targetBtn, GameHost.Instance?.ActivePlaceId ?? "");
		}
	}

	private void HighlightSwatch(Button selectedSwatch)
	{
		if (!GodotObject.IsInstanceValid(_swatchHighlightPanel))
		{
			_swatchHighlightPanel = new Panel();
			_swatchHighlightPanel.Name = "SwatchHighlightPanel";
			_swatchHighlightPanel.MouseFilter = Control.MouseFilterEnum.Ignore;
			_swatchHighlightPanel.SetAnchorsPreset(LayoutPreset.FullRect);
			_swatchHighlightPanel.GrowHorizontal = GrowDirection.Both;
			_swatchHighlightPanel.GrowVertical = GrowDirection.Both;

			var style = new StyleBoxFlat();
			style.BgColor = new Color(0, 0, 0, 0);
			style.BorderColor = UIStyle.ColorCyanGlow;
			style.SetBorderWidthAll(3);
			style.CornerRadiusTopLeft = 4;
			style.CornerRadiusTopRight = 4;
			style.CornerRadiusBottomLeft = 4;
			style.CornerRadiusBottomRight = 4;
			_swatchHighlightPanel.AddThemeStyleboxOverride("panel", style);
		}

		if (_swatchHighlightPanel.GetParent() != null)
		{
			_swatchHighlightPanel.GetParent().RemoveChild(_swatchHighlightPanel);
		}

		if (selectedSwatch != null)
		{
			selectedSwatch.AddChild(_swatchHighlightPanel);
		}
	}

	private void HighlightCliffSwatch(Button selectedSwatch)
	{
		if (!GodotObject.IsInstanceValid(_swatchCliffHighlightPanel))
		{
			_swatchCliffHighlightPanel = new Panel();
			_swatchCliffHighlightPanel.Name = "SwatchCliffHighlightPanel";
			_swatchCliffHighlightPanel.MouseFilter = Control.MouseFilterEnum.Ignore;
			_swatchCliffHighlightPanel.SetAnchorsPreset(LayoutPreset.FullRect);
			_swatchCliffHighlightPanel.GrowHorizontal = GrowDirection.Both;
			_swatchCliffHighlightPanel.GrowVertical = GrowDirection.Both;

			var style = new StyleBoxFlat();
			style.BgColor = new Color(0, 0, 0, 0);
			style.BorderColor = new Color(0.9f, 0.45f, 0.1f); // Vibrant orange/gold for cliff
			style.SetBorderWidthAll(3);
			style.CornerRadiusTopLeft = 4;
			style.CornerRadiusTopRight = 4;
			style.CornerRadiusBottomLeft = 4;
			style.CornerRadiusBottomRight = 4;
			_swatchCliffHighlightPanel.AddThemeStyleboxOverride("panel", style);
		}

		if (_swatchCliffHighlightPanel.GetParent() != null)
		{
			_swatchCliffHighlightPanel.GetParent().RemoveChild(_swatchCliffHighlightPanel);
		}

		if (selectedSwatch != null)
		{
			selectedSwatch.AddChild(_swatchCliffHighlightPanel);
		}
	}

	private Button GetButtonForTool(GameHost.EditorTool tool, string placeId)
	{
		return tool switch
		{
			GameHost.EditorTool.Raise => _btnRaise,
			GameHost.EditorTool.Lower => _btnLower,
			GameHost.EditorTool.Height => _btnHeight,
			GameHost.EditorTool.Smooth => _btnSmooth,
			GameHost.EditorTool.Plateau => _btnPlateau,
			GameHost.EditorTool.Ramp => _btnRamp,
			GameHost.EditorTool.Noise => _btnNoise,
			GameHost.EditorTool.Water => _btnWater,
			GameHost.EditorTool.PaintPathing => _btnPathingBrush,
			GameHost.EditorTool.FloodFillPathing => _btnFloodFillPathing,
			GameHost.EditorTool.DrawCoordinate => _btnDrawCoordinate,
			GameHost.EditorTool.SelectArea => _btnSelectArea,
			GameHost.EditorTool.SelectMove => _btnSelectMove,
			GameHost.EditorTool.DeleteObject => _btnDeleteObject,
			GameHost.EditorTool.PaintTexture => GetTextureSwatchButton(placeId),
			GameHost.EditorTool.FloodFill => _btnFloodFill,
			GameHost.EditorTool.PlacePropClump => _btnClumpBrush,
			GameHost.EditorTool.Eyedropper => _btnEyedropper,
			_ => null
		};
	}

	private Button GetTextureSwatchButton(string placeId)
	{
		for (int i = 0; i < _swatchPaths.Count && i < _swatchButtons.Count; i++)
		{
			if (_swatchPaths[i] == placeId)
			{
				return _swatchButtons[i];
			}
		}
		return _btnTextureBrush;
	}

	public void TriggerToolSelection(GameHost.EditorTool tool, Button btn, string placeId = "")
	{
		if (GameHost.Instance == null) return;

		if (tool != GameHost.EditorTool.SelectMove)
		{
			GameHost.Instance.SelectedEditorObject = null;
		}

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
				HighlightSwatch(_activeToolButton as Button);
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

		GameHost.Instance.ActiveEditorTool = tool;
		if (tool != GameHost.EditorTool.Ramp)
		{
			GameHost.Instance.ClearRampStartPosExternal();
		}
		if (tool != GameHost.EditorTool.Measure)
		{
			GameHost.Instance.ClearMeasureVisuals();
			ClearMeasureTelemetry();
		}
		GameHost.Instance.ActivePlaceId = placeId;


		EditorModule targetModule = _activeModule;
		if (tool == GameHost.EditorTool.Raise ||
			tool == GameHost.EditorTool.Lower ||
			tool == GameHost.EditorTool.Smooth ||
			tool == GameHost.EditorTool.Plateau ||
			tool == GameHost.EditorTool.Ramp ||
			tool == GameHost.EditorTool.Noise ||
			tool == GameHost.EditorTool.Water)
		{
			targetModule = EditorModule.Terrain;
		}
		else if (tool == GameHost.EditorTool.PaintTexture ||
				 tool == GameHost.EditorTool.FloodFill)
		{
			targetModule = EditorModule.TextureDeco;
		}
		else if (tool == GameHost.EditorTool.DrawCoordinate)
		{
			targetModule = EditorModule.Coordinates;
		}
		else if (tool == GameHost.EditorTool.PaintPathing ||
				 tool == GameHost.EditorTool.FloodFillPathing)
		{
			targetModule = EditorModule.Pathing;
		}
		else if (tool == GameHost.EditorTool.PlaceUnit ||
				 tool == GameHost.EditorTool.PlaceProp ||
				 tool == GameHost.EditorTool.PlacePropClump ||
				 tool == GameHost.EditorTool.PlaceDecal ||
				 tool == GameHost.EditorTool.DeleteObject ||
				 tool == GameHost.EditorTool.SelectMove)
		{
			targetModule = EditorModule.Objects;
		}
		else if (tool == GameHost.EditorTool.SelectArea ||
				 tool == GameHost.EditorTool.PasteArea)
		{
			targetModule = EditorModule.Clipboard;
		}

		if (targetModule != _activeModule)
		{
			_activeModule = targetModule;
			UpdateModuleSwitchButtons();
			UpdatePanelVisibilityForModule(targetModule);
		}

		if (tool == GameHost.EditorTool.PaintPathing || tool == GameHost.EditorTool.FloodFillPathing)
		{
			if (_panelTextures != null) _panelTextures.Visible = false;
			if (_panelEntityPalette != null) _panelEntityPalette.Visible = false;
			GameHost.Instance?.UpdatePathingOverlay();
		}
		else if (tool == GameHost.EditorTool.DrawCoordinate)
		{
			if (_panelTextures != null) _panelTextures.Visible = false;
			if (_panelEntityPalette != null) _panelEntityPalette.Visible = false;
			if (_btnCommitCoordinate != null) _btnCommitCoordinate.Visible = false;
			GameHost.Instance?.UpdatePathingOverlay();
		}
		else
		{
			if (_panelTextures != null) _panelTextures.Visible = false;
			if (_panelEntityPalette != null) _panelEntityPalette.Visible = false;
			if (tool == GameHost.EditorTool.SelectArea || tool == GameHost.EditorTool.PasteArea)
			{
				GameHost.Instance?.UpdatePathingOverlay();
			}
		}

		UpdateSidebarMorph(tool);

		bool isPaintTool = tool == GameHost.EditorTool.PaintTexture;

		if (_sldBrushStrength != null)
		{
			if (isPaintTool)
			{
				_sldBrushStrength.MinValue = 0.0;
				_sldBrushStrength.MaxValue = 10.0;
				_sldBrushStrength.Step = 1.0;
				_sldBrushStrength.Value = SavedTextureIntensity;
				if (_lblBrushStrengthValue != null) _lblBrushStrengthValue.Text = SavedTextureIntensity.ToString("F0");
				if (GameHost.Instance != null) GameHost.Instance.EditorBrushStrength = SavedTextureIntensity;
			}
			else
			{
				_sldBrushStrength.MinValue = 0.5;
				_sldBrushStrength.MaxValue = 10.0;
				_sldBrushStrength.Step = 0.5;
				float restoredStrength = Math.Max(0.5f, SavedBrushStrength);
				_sldBrushStrength.Value = restoredStrength;
				if (_lblBrushStrengthValue != null) _lblBrushStrengthValue.Text = restoredStrength.ToString("F1");
				if (GameHost.Instance != null) GameHost.Instance.EditorBrushStrength = restoredStrength;
			}
		}

		string shortPlaceName = !string.IsNullOrEmpty(placeId) ? System.IO.Path.GetFileName(placeId).ToUpper() : "";
		string toolName = tool.ToString().ToUpper();
		if (!string.IsNullOrEmpty(shortPlaceName)) toolName += $" ({shortPlaceName})";
		
		if (_statusLabel != null)
		{
			_statusLabel.Text = $"ACTIVE TOOL: {toolName}";
		}

		if (_lblInfoText != null)
		{
			switch (tool)
			{
				case GameHost.EditorTool.Raise:
					_lblInfoText.Text = TranslationServer.Translate("TOOL: Raise Heights\n\nDrag left click on the map ground to elevate terrain. Adjust size and strength in settings.");
					break;
				case GameHost.EditorTool.Ramp:
					_lblInfoText.Text = TranslationServer.Translate("TOOL: Ramping\n\nLeft-click once on the terrain to set the Ramp Start Point. Left-click again to set the Ramp End Point. The tool will smoothly interpolate heights between the two points. Press Right-click or Escape to cancel.");
					break;
				case GameHost.EditorTool.Lower:
					_lblInfoText.Text = TranslationServer.Translate("TOOL: Lower Heights\n\nDrag left click on the map ground to depress terrain. Adjust size and strength in settings.");
					break;
				case GameHost.EditorTool.Height:
					_lblInfoText.Text = TranslationServer.Translate("TOOL: Exact Height\n\nDrag left click on the map ground to set terrain to exact block height. Adjust height in settings.");
					break;
				case GameHost.EditorTool.Plateau:
					_lblInfoText.Text = TranslationServer.Translate("TOOL: Plateau\n\nDrag left click to flatten terrain to the elevation of your initial click point.");
					break;
				case GameHost.EditorTool.Smooth:
					_lblInfoText.Text = TranslationServer.Translate("TOOL: Smooth Terrain\n\nDrag left click to average neighbor vertex heights and smooth out rugged elevations.");
					break;
				case GameHost.EditorTool.PaintTexture:
					_lblInfoText.Text = TranslationServer.Translate("TOOL: Texture Painting\n\nDrag left click to paint texture layers onto the vertices of the terrain mesh.");
					break;
				case GameHost.EditorTool.FloodFill:
					_lblInfoText.Text = TranslationServer.Translate("TOOL: Flood Fill\n\nClick once on the terrain map to flood-fill an area sharing the same texture color until hitting a boundary (cliff or different texture). Uses selected texture swatch.");
					break;
				case GameHost.EditorTool.SelectArea:
					_lblInfoText.Text = TranslationServer.Translate("TOOL: Area Select\n\nDrag left click to select a rectangular area of the map. Press Ctrl+C to copy the area.");
					break;
				case GameHost.EditorTool.PasteArea:
					_lblInfoText.Text = TranslationServer.Translate("TOOL: Area Paste\n\nClick on the terrain to paste the copied area. Use the Affected Layers checkboxes to filter what is pasted (Textures, HeightMap, Units / Props, Pathing).");
					break;
				case GameHost.EditorTool.PlaceUnit:
					string alignment = (GameHost.Instance != null && GameHost.Instance.PlaceUnitIsEnemy) ? TranslationServer.Translate("Enemy (Orc)") : TranslationServer.Translate("Player (Alliance)");
					_lblInfoText.Text = string.Format(TranslationServer.Translate("TOOL: Place Unit\n\nLeft-click on the ground to spawn a {0} aligned with {1}."), shortPlaceName, alignment);
					break;
				case GameHost.EditorTool.PlaceProp:
					_lblInfoText.Text = string.Format(TranslationServer.Translate("TOOL: Place Prop\n\nLeft-click on the ground to spawn static decorative object: {0}."), shortPlaceName);
					break;
				case GameHost.EditorTool.PlacePropClump:
					_lblInfoText.Text = string.Format(TranslationServer.Translate("TOOL: Clump Brush\n\nDrag left click on the ground to paint clumps of static props: {0} based on Density and Scale Variation settings. Uses texture brush shape (Circle/Square)."), shortPlaceName);
					break;
				case GameHost.EditorTool.PlaceDecal:
					_lblInfoText.Text = TranslationServer.Translate("TOOL: Place Decal\n\nLeft-click on the ground to project a decorative decal. Snapping, scaling, and rotation apply.");
					break;
				case GameHost.EditorTool.DeleteObject:
					_lblInfoText.Text = TranslationServer.Translate("TOOL: Object Eraser\n\nLeft-click directly on any unit or prop in 3D scene to erase and remove it from the map.");
					break;
				case GameHost.EditorTool.SelectMove:
					_lblInfoText.Text = TranslationServer.Translate("TOOL: Select / Move\n\nLeft-click directly on any unit, prop, or decal to select it. Hold and drag left click to move it. Use Alt + MouseWheel to scale, or Delete to delete.");
					break;
				case GameHost.EditorTool.Eyedropper:
					_lblInfoText.Text = TranslationServer.Translate("TOOL: Eyedropper / Picker\n\nLeft-click directly on any unit, prop, or decal to copy and select it as the active placement tool. Click on terrain to copy its texture color, or hold Shift to copy its height.");
					break;
				case GameHost.EditorTool.Noise:
					_lblInfoText.Text = TranslationServer.Translate("TOOL: Roughen Terrain\n\nDrag left-click to apply random height variations/noise to ruggedize the terrain surface. Adjust size and strength in settings.");
					break;
				case GameHost.EditorTool.Water:
					_lblInfoText.Text = TranslationServer.Translate("TOOL: Water Flood Fill\n\nClick on terrain to flood-fill water bounded by cliff walls. Uses selected Water Profile or toggles water.");
					break;
				case GameHost.EditorTool.PaintPathing:
					_lblInfoText.Text = TranslationServer.Translate("TOOL: Pathing Layer Painting\n\nDrag left click to paint pathing properties (ground, flying, water, etc.) onto the map. Use checkboxes to select layers, and Mode to Add/Remove.");
					break;
				case GameHost.EditorTool.FloodFillPathing:
					_lblInfoText.Text = TranslationServer.Translate("TOOL: Flood Fill Pathing\n\nClick once on the terrain map to flood-fill pathing properties (ground, flying, water, etc.) across an area sharing the same texture color until hitting a boundary. Use checkboxes to select layers, and Mode to Add/Remove.");
					break;
				case GameHost.EditorTool.Measure:
					_lblInfoText.Text = TranslationServer.Translate("TOOL: Tape Measure\n\nClick on the terrain to set measurement origin. Move the cursor or click again to lock the endpoint. Displays live Euclidean distance, Manhattan distance, grid delta, and slope.");
					break;
				case GameHost.EditorTool.None:
					_lblInfoText.Text = TranslationServer.Translate("Select a tool from the panels to begin terrain modification.");
					break;
			}
		}
		UpdateTextureLabels();
	}

	private void InitializeTempWorkspace()
	{
		_tempWorkspacePath = ProjectSettings.GlobalizePath(TempWorkspaceGodotPath);
		if (!ReturningFromTest)
		{
			try
			{
				System.IO.Directory.CreateDirectory(_tempWorkspacePath);
				MapWorkspaceService.SetupWorkspace(_tempWorkspacePath, "MapScript");
			}
			catch (Exception ex)
			{
				GD.PrintErr($"Failed initializing temp workspace: {ex.Message}");
			}
		}

		try
		{
			if (System.IO.Directory.Exists(_tempWorkspacePath))
			{
				foreach (var rmapFile in System.IO.Directory.GetFiles(_tempWorkspacePath, "*.rmap", System.IO.SearchOption.TopDirectoryOnly))
				{
					try { System.IO.File.Delete(rmapFile); } catch { }
				}
			}
		}
		catch { }

		string initTerrainPath = System.IO.Path.Combine(_tempWorkspacePath, "terrain.json");
		string initMetadataPath = System.IO.Path.Combine(_tempWorkspacePath, "metadata.json");
		_lastTerrainSyncTime = GetMaxTerrainWriteTime(initTerrainPath);
		_lastMetadataSyncTime = GetLastWriteTimeSafe(initMetadataPath);
		_editorService?.StartWorkspaceWatcher(_tempWorkspacePath);

		try
		{
			Realm.Godot.Animation.RealmDefaultAnimations.EnsureDefaultTemplateAnimations(_tempWorkspacePath);
		}
		catch (Exception ex)
		{
			GD.PrintErr($"[MapEditorHUD] Pre-warming default animations error: {ex.Message}");
		}

		var syncTimer = new Godot.Timer();
		syncTimer.WaitTime = 1.0f;
		syncTimer.Autostart = true;
		syncTimer.Timeout += OnSyncTimerTimeout;
		AddChild(syncTimer);

		try
		{
			LoadMapProperties();
		}
		catch (Exception ex)
		{
			GD.PrintErr($"LoadMapProperties error: {ex.Message}");
		}

		if (GameHost.Instance != null && GameHost.Instance.GroundTerrain != null)
		{
			try
			{
				GameHost.Instance.GroundTerrain.ReloadTerrainTextures(false);
			}
			catch (Exception ex)
			{
				GD.PrintErr($"ReloadTerrainTextures error during workspace init: {ex.Message}. Resetting to blank map.");
				GameHost.Instance.ClearMapEntirely();
			}
		}

		if (!string.IsNullOrEmpty(_pendingCasSourceDirectory) && System.IO.Directory.Exists(_pendingCasSourceDirectory))
		{
			string casDir = _pendingCasSourceDirectory;
			string? defaultSave = _pendingDefaultSaveFolder;
			_pendingCasSourceDirectory = null;
			_pendingDefaultSaveFolder = null;

			_ = OpenFromCasAsync(casDir, defaultSave);
		}

	}

	private async System.Threading.Tasks.Task OpenFromCasAsync(string casSourceDirectory, string? defaultSaveFolder)
	{
		bool loaded = await LoadMapFolderAsync(casSourceDirectory);
		if (loaded && !string.IsNullOrEmpty(defaultSaveFolder))
		{
			_lastUsedFolder = defaultSaveFolder;
			_currentSourceFolder = defaultSaveFolder;
			if (!IsRestrictedSaveDirectory(defaultSaveFolder))
			{
				GameSettings.LastOpenedFolder = defaultSaveFolder;
				GameSettings.Save();
			}
		}
	}

	private void CheckPostLaunchPrompts()
	{
		if (AssetIndexService.Instance.IsIndexVersionMismatch())
		{
			_ = ShowAssetIndexRepairModalAsync(() =>
			{
				CheckUnsavedSessionOnLaunch(() =>
				{
					CheckCreatorRegistrationAndPrompt();
				});
			});
		}
		else
		{
			CheckUnsavedSessionOnLaunch(() =>
			{
				CheckCreatorRegistrationAndPrompt();
			});
		}
	}

	public static string ComputeDirectoryBlake3(string directoryPath)
	{
		if (string.IsNullOrEmpty(directoryPath) || !System.IO.Directory.Exists(directoryPath)) return string.Empty;

		try
		{
			var files = System.IO.Directory.GetFiles(directoryPath, "*", System.IO.SearchOption.AllDirectories)
				.Where(f => !IsIgnoredPath(f.Substring(directoryPath.Length).TrimStart('/', '\\')))
				.OrderBy(f => System.IO.Path.GetRelativePath(directoryPath, f).Replace('\\', '/'), StringComparer.OrdinalIgnoreCase)
				.ToList();

			using var hasher = Blake3.Hasher.New();
			byte[] buffer = new byte[65536];

			foreach (var file in files)
			{
				try
				{
					string relPath = System.IO.Path.GetRelativePath(directoryPath, file).Replace('\\', '/');
					byte[] pathBytes = System.Text.Encoding.UTF8.GetBytes(relPath);
					hasher.Update(pathBytes);

					using var fs = System.IO.File.OpenRead(file);
					int read;
					while ((read = fs.Read(buffer, 0, buffer.Length)) > 0)
					{
						hasher.Update(new ReadOnlySpan<byte>(buffer, 0, read));
					}
				}
				catch (Exception ex)
				{
					GD.PrintErr($"[ComputeDirectoryBlake3] Error hashing {file}: {ex.Message}");
				}
			}

			return hasher.Finalize().ToString();
		}
		catch (Exception ex)
		{
			GD.PrintErr($"[ComputeDirectoryBlake3] Error scanning directory {directoryPath}: {ex.Message}");
			return string.Empty;
		}
	}

	public static bool HasUnsavedChangesStatic()
	{
		string tempPath = ProjectSettings.GlobalizePath(TempWorkspaceGodotPath);
		if (string.IsNullOrEmpty(tempPath) || !System.IO.Directory.Exists(tempPath))
		{
			return false;
		}
		if (string.IsNullOrEmpty(CurrentDirectoryBlake3))
		{
			return false;
		}
		string currentHash = ComputeDirectoryBlake3(tempPath);
		return !string.Equals(currentHash, CurrentDirectoryBlake3, StringComparison.OrdinalIgnoreCase);
	}

	public bool HasUnsavedChanges() => HasUnsavedChangesStatic();

	public void SaveCurrentDirectoryBlake3()
	{
		if (string.IsNullOrEmpty(_tempWorkspacePath) || !System.IO.Directory.Exists(_tempWorkspacePath)) return;
		try
		{
			string hash = ComputeDirectoryBlake3(_tempWorkspacePath);
			CurrentDirectoryBlake3 = hash;
			string saveFile = ProjectSettings.GlobalizePath("user://editor_last_save.txt");
			System.IO.File.WriteAllText(saveFile, hash);
		}
		catch (Exception ex)
		{
			GD.PrintErr($"[SaveCurrentDirectoryBlake3] Error writing editor_last_save.txt: {ex.Message}");
		}
	}

	private void WriteCleanExitMarker()
	{
		try
		{
			SaveCurrentDirectoryBlake3();
			string cleanExitFile = ProjectSettings.GlobalizePath("user://editor_clean_exit.txt");
			System.IO.File.WriteAllText(cleanExitFile, CurrentDirectoryBlake3 ?? string.Empty);
		}
		catch (Exception ex)
		{
			GD.PrintErr($"[MapEditorHUD] Error writing editor_clean_exit.txt: {ex.Message}");
		}
	}

	private void CheckUnsavedSessionOnLaunch(Action onCompleted = null)
	{
		if (ReturningFromTest)
		{
			onCompleted?.Invoke();
			return;
		}

		if (!string.IsNullOrEmpty(_pendingCasSourceDirectory))
		{
			onCompleted?.Invoke();
			return;
		}

		string cleanExitFile = ProjectSettings.GlobalizePath("user://editor_clean_exit.txt");
		if (System.IO.File.Exists(cleanExitFile))
		{
			try
			{
				System.IO.File.Delete(cleanExitFile);
			}
			catch { }
			SaveCurrentDirectoryBlake3();
			onCompleted?.Invoke();
			return;
		}

		string editorLastSaveFile = ProjectSettings.GlobalizePath("user://editor_last_save.txt");
		if (!System.IO.File.Exists(editorLastSaveFile))
		{
			SaveCurrentDirectoryBlake3();
			onCompleted?.Invoke();
			return;
		}

		string savedHash = System.IO.File.ReadAllText(editorLastSaveFile).Trim();
		string currentHash = ComputeDirectoryBlake3(_tempWorkspacePath);
		CurrentDirectoryBlake3 = savedHash;

		if (!string.IsNullOrEmpty(savedHash) && !currentHash.Equals(savedHash, StringComparison.OrdinalIgnoreCase))
		{
			ShowUnsavedSessionModal(onCompleted);
		}
		else
		{
			onCompleted?.Invoke();
		}
	}

	private void ShowUnsavedSessionModal(Action onCompleted = null)
	{
		ShowConfirmationDialog(
			"There were unsaved changes in last editor session.",
			onConfirm: () =>
			{
				LoadTempWorkspaceMap();
				SaveCurrentDirectoryBlake3();
			},
			confirmText: "Restore",
			cancelText: "Discard",
			onCancel: () =>
			{
				_isSyncing = true;
				try
				{
					ResetFolderLocations();
					ClearTempWorkspaceExternal();
					MapWorkspaceService.SetupWorkspace(_tempWorkspacePath, "MapScript");
					GameHost.Instance?.ClearMapEntirely();
					string terrainPath = System.IO.Path.Combine(_tempWorkspacePath, "terrain.json");
					string metadataPath = System.IO.Path.Combine(_tempWorkspacePath, "metadata.json");
					_lastTerrainSyncTime = GetMaxTerrainWriteTime(terrainPath);
					_lastMetadataSyncTime = GetLastWriteTimeSafe(metadataPath);
					SaveCurrentDirectoryBlake3();
					ReadMetadataAndRefreshTextures();
					LoadMapProperties();
					UpdateMapNameHeader();
				}
				catch (Exception ex)
				{
					GD.PrintErr($"[ShowUnsavedSessionModal] Error discarding session: {ex.Message}");
				}
				finally
				{
					_isSyncing = false;
				}
			},
			onDismissed: () =>
			{
				onCompleted?.Invoke();
			}
		);
	}

	private void LoadTempWorkspaceMap()
	{
		string terrainPath = System.IO.Path.Combine(_tempWorkspacePath, "terrain.json");
		MapWorkspaceService.EnsureGlbAssetsOptimized(_tempWorkspacePath);
		MapWorkspaceService.EnsurePngAssetsConverted(_tempWorkspacePath);
		LoadMapProperties();
		ReadMetadataAndRefreshTextures();
		if (GameHost.Instance != null && System.IO.File.Exists(terrainPath))
		{
			GameHost.Instance.LoadMapFromFile(terrainPath);
		}
		_lastTerrainSyncTime = GetMaxTerrainWriteTime(terrainPath);
		_lastMetadataSyncTime = GetLastWriteTimeSafe(System.IO.Path.Combine(_tempWorkspacePath, "metadata.json"));
		ShowFeedback(TranslationServer.Translate("Restored map workspace from last session!"));
	}

	public void ClearTempWorkspaceExternal()
	{
		if (string.IsNullOrEmpty(_tempWorkspacePath) || !System.IO.Directory.Exists(_tempWorkspacePath)) return;

		try
		{
			ClearDirectoryReadOnly(_tempWorkspacePath);
			foreach (var file in System.IO.Directory.GetFiles(_tempWorkspacePath, "*", System.IO.SearchOption.AllDirectories))
			{
				var fileAttributes = System.IO.File.GetAttributes(file);
				if ((fileAttributes & System.IO.FileAttributes.ReadOnly) == System.IO.FileAttributes.ReadOnly)
				{
					System.IO.File.SetAttributes(file, fileAttributes & ~System.IO.FileAttributes.ReadOnly);
				}
				System.IO.File.Delete(file);
			}

			foreach (var directory in System.IO.Directory.GetDirectories(_tempWorkspacePath))
			{
				System.IO.Directory.Delete(directory, true);
			}
		}
		catch (Exception ex)
		{
			GD.PrintErr($"[ClearTempWorkspaceExternal] Error: {ex.Message}");
		}

		_wasmHasErrors = false;
		_wasmCompileLogPath = "";
		_lastTerrainSyncTime = 0;
		_lastMetadataSyncTime = 0;
	}

	private static void ClearDirectoryReadOnly(string targetDir)
	{
		foreach (var file in System.IO.Directory.GetFiles(targetDir, "*", System.IO.SearchOption.AllDirectories))
		{
			var attrs = System.IO.File.GetAttributes(file);
			if ((attrs & System.IO.FileAttributes.ReadOnly) != 0)
			{
				System.IO.File.SetAttributes(file, attrs & ~System.IO.FileAttributes.ReadOnly);
			}
		}
	}

	public void GenerateVSCodeFilesExternal()
	{
		if (string.IsNullOrEmpty(_tempWorkspacePath)) return;
		string scriptPath = System.IO.Path.Combine(_tempWorkspacePath, "MapScript.cs");
		string unitsPath = System.IO.Path.Combine(_tempWorkspacePath, "metadata.json");
		System.IO.Directory.CreateDirectory(_tempWorkspacePath);
		MapWorkspaceService.SetupWorkspace(_tempWorkspacePath, "MapScript");
		string terrainPath = System.IO.Path.Combine(_tempWorkspacePath, "terrain.json");
		_lastTerrainSyncTime = GetMaxTerrainWriteTime(terrainPath);
		_lastMetadataSyncTime = GetLastWriteTimeSafe(unitsPath);
	}

	private long GetLastWriteTimeSafe(string path)
	{
		if (!System.IO.File.Exists(path)) return 0;
		return System.IO.File.GetLastWriteTimeUtc(path).Ticks;
	}

	private long GetMaxTerrainWriteTime(string baseTerrainJsonPath)
	{
		long maxTime = GetLastWriteTimeSafe(baseTerrainJsonPath);
		string dir = System.IO.Path.GetDirectoryName(baseTerrainJsonPath);
		if (!string.IsNullOrEmpty(dir))
		{
			maxTime = Math.Max(maxTime, GetLastWriteTimeSafe(System.IO.Path.Combine(dir, "terrain_heights.exr")));
			maxTime = Math.Max(maxTime, GetLastWriteTimeSafe(System.IO.Path.Combine(dir, "terrain_water.exr")));
			maxTime = Math.Max(maxTime, GetLastWriteTimeSafe(System.IO.Path.Combine(dir, "terrain_splat_indices.exr")));
			maxTime = Math.Max(maxTime, GetLastWriteTimeSafe(System.IO.Path.Combine(dir, "terrain_splat_weights.exr")));
			maxTime = Math.Max(maxTime, GetLastWriteTimeSafe(System.IO.Path.Combine(dir, "terrain_splat_indices.png")));
			maxTime = Math.Max(maxTime, GetLastWriteTimeSafe(System.IO.Path.Combine(dir, "terrain_splat_weights.png")));
			maxTime = Math.Max(maxTime, GetLastWriteTimeSafe(System.IO.Path.Combine(dir, "terrain_pathing.png")));
		}
		return maxTime;
	}

	private void OnSyncTimerTimeout()
	{
		if (GameHost.Instance == null || !GameHost.Instance.IsMapEditorMode || IsTestMode || _isSyncing) return;
		_isSyncing = true;
		
		string terrainPath = System.IO.Path.Combine(_tempWorkspacePath, "terrain.json");
		string metadataPath = System.IO.Path.Combine(_tempWorkspacePath, "metadata.json");

		long currentTerrainWrite = GetMaxTerrainWriteTime(terrainPath);
		long currentMetadataWrite = GetLastWriteTimeSafe(metadataPath);

		bool terrainModifiedOnDisk = currentTerrainWrite > _lastTerrainSyncTime;
		bool metadataModifiedOnDisk = currentMetadataWrite > _lastMetadataSyncTime;

		bool isRecentInternalSave = (DateTime.UtcNow - EditorService.LastInternalSaveTimeUtc).TotalMilliseconds < 2000;

		if (terrainModifiedOnDisk || metadataModifiedOnDisk)
		{
			if (metadataModifiedOnDisk)
			{
				_lastMetadataSyncTime = Math.Max(currentMetadataWrite, DateTime.UtcNow.Ticks);
				if (!isRecentInternalSave)
				{
					ReadMetadataAndRefreshTextures();
				}
			}
			if (terrainModifiedOnDisk)
			{
				_lastTerrainSyncTime = Math.Max(currentTerrainWrite, DateTime.UtcNow.Ticks);
				if (!isRecentInternalSave)
				{
					GameHost.Instance.LoadMapFromFile(terrainPath);
				}
			}
			
			GameHost.Instance.EditorHasUnsavedChanges = false;
		}
		
		_isSyncing = false;
	}

	internal static bool IsIgnoredPath(string relativePath)
	{
		if (string.IsNullOrEmpty(relativePath)) return false;
		string normalized = relativePath.Replace('\\', '/');
		if (normalized.StartsWith(".git/", StringComparison.OrdinalIgnoreCase) || normalized.Contains("/.git/", StringComparison.OrdinalIgnoreCase) || normalized.Equals(".git", StringComparison.OrdinalIgnoreCase) ||
			normalized.StartsWith(".vs/", StringComparison.OrdinalIgnoreCase) || normalized.Contains("/.vs/", StringComparison.OrdinalIgnoreCase) || normalized.Equals(".vs", StringComparison.OrdinalIgnoreCase) ||
			normalized.StartsWith(".godot/", StringComparison.OrdinalIgnoreCase) || normalized.Contains("/.godot/", StringComparison.OrdinalIgnoreCase) || normalized.Equals(".godot", StringComparison.OrdinalIgnoreCase) ||
			normalized.StartsWith(".idea/", StringComparison.OrdinalIgnoreCase) || normalized.Contains("/.idea/", StringComparison.OrdinalIgnoreCase) || normalized.Equals(".idea", StringComparison.OrdinalIgnoreCase) ||
			normalized.StartsWith("bin/", StringComparison.OrdinalIgnoreCase) || normalized.Contains("/bin/", StringComparison.OrdinalIgnoreCase) || normalized.Equals("bin", StringComparison.OrdinalIgnoreCase) ||
			normalized.StartsWith("obj/", StringComparison.OrdinalIgnoreCase) || normalized.Contains("/obj/", StringComparison.OrdinalIgnoreCase) || normalized.Equals("obj", StringComparison.OrdinalIgnoreCase) ||
			normalized.StartsWith("map_backups/", StringComparison.OrdinalIgnoreCase) || normalized.Contains("/map_backups/", StringComparison.OrdinalIgnoreCase) || normalized.Equals("map_backups", StringComparison.OrdinalIgnoreCase) ||
			normalized.StartsWith("map_upgrades/", StringComparison.OrdinalIgnoreCase) || normalized.Contains("/map_upgrades/", StringComparison.OrdinalIgnoreCase) || normalized.Equals("map_upgrades", StringComparison.OrdinalIgnoreCase) ||
			normalized.StartsWith("backups/", StringComparison.OrdinalIgnoreCase) || normalized.Contains("/backups/", StringComparison.OrdinalIgnoreCase) || normalized.Equals("backups", StringComparison.OrdinalIgnoreCase) ||
			normalized.StartsWith(".backups/", StringComparison.OrdinalIgnoreCase) || normalized.Contains("/.backups/", StringComparison.OrdinalIgnoreCase) || normalized.Equals(".backups", StringComparison.OrdinalIgnoreCase) ||
			normalized.StartsWith(".dotnet/", StringComparison.OrdinalIgnoreCase) || normalized.Contains("/.dotnet/", StringComparison.OrdinalIgnoreCase) || normalized.Equals(".dotnet", StringComparison.OrdinalIgnoreCase) ||
			normalized.StartsWith(".wasi/", StringComparison.OrdinalIgnoreCase) || normalized.Contains("/.wasi/", StringComparison.OrdinalIgnoreCase) || normalized.Equals(".wasi", StringComparison.OrdinalIgnoreCase) ||
			normalized.StartsWith(".sidecarcache/", StringComparison.OrdinalIgnoreCase) || normalized.Contains("/.sidecarcache/", StringComparison.OrdinalIgnoreCase) || normalized.Equals(".sidecarcache", StringComparison.OrdinalIgnoreCase) ||
			normalized.StartsWith(".cache/", StringComparison.OrdinalIgnoreCase) || normalized.Contains("/.cache/", StringComparison.OrdinalIgnoreCase) || normalized.Equals(".cache", StringComparison.OrdinalIgnoreCase) ||
			normalized.EndsWith(".rmap", StringComparison.OrdinalIgnoreCase) ||
			normalized.EndsWith(".tmp", StringComparison.OrdinalIgnoreCase) ||
			normalized.EndsWith(".log", StringComparison.OrdinalIgnoreCase) ||
			normalized.EndsWith(".bak", StringComparison.OrdinalIgnoreCase) ||
			System.IO.Path.GetFileName(normalized).StartsWith("~", StringComparison.OrdinalIgnoreCase))
		{
			return true;
		}
		return false;
	}

	private static void CopyFileClearingReadOnly(string sourceFile, string targetFile)
	{
		PathUtils.CopyFileClearingReadOnly(sourceFile, targetFile);
	}

	private void CopyFolderToTempWorkspace(string sourceFolder)
	{
		ClearTempWorkspaceExternal();
		if (!System.IO.Directory.Exists(_tempWorkspacePath))
		{
			System.IO.Directory.CreateDirectory(_tempWorkspacePath);
		}
		
		var allFiles = System.IO.Directory.GetFiles(sourceFolder, "*", System.IO.SearchOption.AllDirectories);
		var filesToProcess = new List<(string Source, string Target, bool IsMutable)>(allFiles.Length);
		var createdDirs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

		foreach (var file in allFiles)
		{
			string relativePath = file.Substring(sourceFolder.Length + 1);
			if (IsIgnoredPath(relativePath)) continue;

			string targetFile = System.IO.Path.Combine(_tempWorkspacePath, relativePath);
			string targetDir = System.IO.Path.GetDirectoryName(targetFile);
			if (!string.IsNullOrEmpty(targetDir) && createdDirs.Add(targetDir))
			{
				System.IO.Directory.CreateDirectory(targetDir);
			}
			filesToProcess.Add((file, targetFile, PathUtils.IsMutableMapFileType(relativePath)));
		}

		System.Threading.Tasks.Parallel.ForEach(filesToProcess, item =>
		{
			if (item.IsMutable)
			{
				PathUtils.CopyFileClearingReadOnly(item.Source, item.Target);
			}
			else
			{
				PathUtils.LinkOrCopyFile(item.Source, item.Target, preferHardLink: true);
			}
		});

		MapWorkspaceService.EnsureWitFile(_tempWorkspacePath);
		MapWorkspaceService.EnsureWasmEntryPoint(_tempWorkspacePath);
		MapWorkspaceService.EnsureCsproj(_tempWorkspacePath, System.IO.Path.GetFileName(sourceFolder));
		CopyVsCodeFolderFromMapTemplate(_tempWorkspacePath);
	}

	private static void CopyVsCodeFolderFromMapTemplate(string targetWorkspacePath)
	{
		if (string.IsNullOrEmpty(targetWorkspacePath)) return;

		string templateVsCodeDir = MapWorkspaceService.GetTemplatePath(".vscode");
		if (string.IsNullOrEmpty(templateVsCodeDir) || !System.IO.Directory.Exists(templateVsCodeDir))
		{
			templateVsCodeDir = PathUtils.FindPath("MapTemplate/.vscode");
		}

		if (string.IsNullOrEmpty(templateVsCodeDir) || !System.IO.Directory.Exists(templateVsCodeDir))
		{
			return;
		}

		string targetVsCodeDir = System.IO.Path.Combine(targetWorkspacePath, ".vscode");
		if (!System.IO.Directory.Exists(targetVsCodeDir))
		{
			System.IO.Directory.CreateDirectory(targetVsCodeDir);
		}

		foreach (string file in System.IO.Directory.GetFiles(templateVsCodeDir, "*", System.IO.SearchOption.AllDirectories))
		{
			string relativePath = file.Substring(templateVsCodeDir.Length + 1);
			string destFile = System.IO.Path.Combine(targetVsCodeDir, relativePath);
			PathUtils.CopyFileClearingReadOnly(file, destFile);
		}
	}

	private void CopyTempWorkspaceToFolder(string targetFolder)
	{
		if (!System.IO.Directory.Exists(targetFolder))
		{
			System.IO.Directory.CreateDirectory(targetFolder);
		}
		
		string tempTerrainPath = System.IO.Path.Combine(_tempWorkspacePath, "terrain.json");
		_lastTerrainSyncTime = GetMaxTerrainWriteTime(tempTerrainPath);

		var allFiles = System.IO.Directory.GetFiles(_tempWorkspacePath, "*", System.IO.SearchOption.AllDirectories);
		var filesToCopy = new List<(string Source, string Target)>(allFiles.Length);
		var createdDirs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

		foreach (var file in allFiles)
		{
			string relativePath = file.Substring(_tempWorkspacePath.Length + 1);
			if (IsIgnoredPath(relativePath)) continue;
			string targetFile = System.IO.Path.Combine(targetFolder, relativePath);
			string targetDir = System.IO.Path.GetDirectoryName(targetFile);
			if (!string.IsNullOrEmpty(targetDir) && createdDirs.Add(targetDir))
			{
				System.IO.Directory.CreateDirectory(targetDir);
			}
			filesToCopy.Add((file, targetFile));
		}

		System.Threading.Tasks.Parallel.ForEach(filesToCopy, pair =>
		{
			PathUtils.CopyFileClearingReadOnly(pair.Source, pair.Target);
		});
		
		if (OperatingSystem.IsWindows())
		{
			VSCodeManager.Instance.SaveRecentMapDir(targetFolder);
		}
	}

	private void SetPanelExpanded(string panelPath, string buttonPath, string contentPath, bool expand)
	{
		var btn = GetNodeOrNull<Button>($"{panelPath}/{buttonPath}");
		var content = GetNodeOrNull<Control>($"{panelPath}/{contentPath}");

		if (btn != null && content != null)
		{
			content.Visible = expand;
			btn.Text = expand ? "▲" : "▼";
		}
	}

	private async System.Threading.Tasks.Task SaveMapToFolderAsync(string targetFolder)
	{
		MapWorkspaceService.EnsureLicenseFile(_tempWorkspacePath);
		string tempTerrainPath = System.IO.Path.Combine(_tempWorkspacePath, "terrain.json");
		if (GameHost.Instance != null)
		{
			GameHost.Instance.SaveMapToFile(tempTerrainPath);
			GameHost.Instance.EditorHasUnsavedChanges = false;
			InvalidateMetadataCache();
		}

		ShowFeedback(TranslationServer.Translate("Saving map folder..."));

		try
		{
			await System.Threading.Tasks.Task.Run(() => CopyTempWorkspaceToFolder(targetFolder));

			MapWorkspaceService.EnsureLicenseFile(targetFolder);

			SaveCurrentDirectoryBlake3();
			_lastTerrainSyncTime = GetMaxTerrainWriteTime(tempTerrainPath);
			_lastMetadataSyncTime = GetLastWriteTimeSafe(System.IO.Path.Combine(_tempWorkspacePath, "metadata.json"));
			EditorService.LastInternalSaveTimeUtc = DateTime.UtcNow;

			ShowFeedback(string.Format(TranslationServer.Translate("Map saved successfully to folder {0}!"), System.IO.Path.GetFileName(targetFolder)));
		}
		catch (Exception ex)
		{
			GD.PrintErr($"[MapEditorHUD] SaveMapToFolderAsync failed: {ex}");
			ShowFeedback(string.Format(TranslationServer.Translate("Failed to save map: {0}"), ex.Message));
		}
	}

	private void SaveMapAction()
	{
		if (GameHost.Instance == null) return;

		if (!string.IsNullOrEmpty(_currentSourceFolder) && !IsRestrictedSaveDirectory(_currentSourceFolder) && System.IO.Directory.Exists(_currentSourceFolder))
		{
			_ = SaveMapToFolderAsync(_currentSourceFolder);
			return;
		}

		PromptSaveMapFolder();
	}

	private void SaveAsMapAction()
	{
		if (GameHost.Instance == null) return;
		PromptSaveMapFolder();
	}

	public void PromptSaveMapFolder()
	{
		if (GameHost.Instance == null) return;

		string initialDir = GetInitialDirectory();
		try
		{
			if (!System.IO.Directory.Exists(initialDir))
			{
				System.IO.Directory.CreateDirectory(initialDir);
			}
		}
		catch { }

		var err = DisplayServer.FileDialogShow(
			TranslationServer.Translate("Save Map Folder"),
			initialDir,
			"",
			false,
			DisplayServer.FileDialogMode.OpenDir,
			System.Array.Empty<string>(),
			Callable.From((bool status, string[] selectedPaths, int selectedFilterIndex) => {
				if (status && selectedPaths.Length > 0)
				{
					HandleSaveToFolder(selectedPaths[0]);
				}
				else
				{
					ShowFeedback(TranslationServer.Translate("Save cancelled"));
				}
			})
		);

		if (err != Error.Ok)
		{
			string defaultFolder = GetDefaultDevelopmentMapDirectory();
			try { System.IO.Directory.CreateDirectory(defaultFolder); } catch { }
			HandleSaveToFolder(defaultFolder);
		}
	}

	private void HandleSaveToFolder(string selectedFolder)
	{
		if (string.IsNullOrWhiteSpace(selectedFolder)) return;

		string fullSelectedPath = System.IO.Path.GetFullPath(selectedFolder);

		if (IsRestrictedSaveDirectory(fullSelectedPath))
		{
			ShowFeedback(TranslationServer.Translate("Cannot save to restricted application directory. Please choose a folder in Documents or your workspace."));
			return;
		}

		var parentDir = System.IO.Directory.GetParent(fullSelectedPath);
		while (parentDir != null)
		{
			if (System.IO.File.Exists(System.IO.Path.Combine(parentDir.FullName, "manifest.json")))
			{
				ShowFeedback(TranslationServer.Translate("Cannot save map inside an existing map folder. Please select a separate root directory."));
				return;
			}
			parentDir = parentDir.Parent;
		}

		if (System.IO.Directory.Exists(fullSelectedPath))
		{
			try
			{
				foreach (string subDir in System.IO.Directory.EnumerateDirectories(fullSelectedPath))
				{
					if (System.IO.File.Exists(System.IO.Path.Combine(subDir, "manifest.json")))
					{
						ShowFeedback(TranslationServer.Translate("Found nested map inside selected folder. Cannot save to this folder location."));
						return;
					}
				}
			}
			catch { }
		}

		string rootManifest = System.IO.Path.Combine(fullSelectedPath, "manifest.json");
		bool isSameAsCurrent = !string.IsNullOrEmpty(_currentSourceFolder) &&
			string.Equals(fullSelectedPath, System.IO.Path.GetFullPath(_currentSourceFolder), StringComparison.OrdinalIgnoreCase);

		if (System.IO.File.Exists(rootManifest) && !isSameAsCurrent)
		{
			ShowConfirmationDialog(
				TranslationServer.Translate("An existing map was found in this folder. Overwrite existing map?"),
				() =>
				{
					_lastUsedFolder = fullSelectedPath;
					_currentSourceFolder = fullSelectedPath;
					if (!IsRestrictedSaveDirectory(fullSelectedPath))
					{
						GameSettings.LastOpenedFolder = fullSelectedPath;
						GameSettings.Save();
					}
					_ = SaveMapToFolderAsync(fullSelectedPath);
				},
				confirmText: TranslationServer.Translate("OVERWRITE"),
				cancelText: TranslationServer.Translate("CANCEL")
			);
			return;
		}

		_lastUsedFolder = fullSelectedPath;
		_currentSourceFolder = fullSelectedPath;
		if (!IsRestrictedSaveDirectory(fullSelectedPath))
		{
			GameSettings.LastOpenedFolder = fullSelectedPath;
			GameSettings.Save();
		}
		_ = SaveMapToFolderAsync(fullSelectedPath);
	}

	public void LoadMapAction()
	{
		if (GameHost.Instance == null) return;

		var err = DisplayServer.FileDialogShow(
			TranslationServer.Translate("Load Map Folder"),
			GetInitialDirectory(),
			"",
			false,
			DisplayServer.FileDialogMode.OpenDir,
			System.Array.Empty<string>(),
			Callable.From((bool status, string[] selectedPaths, int selectedFilterIndex) => {
				if (status && selectedPaths.Length > 0)
				{
					string selectedFolder = selectedPaths[0];
					_lastUsedFolder = selectedFolder;
					if (!IsRestrictedSaveDirectory(selectedFolder))
					{
						GameSettings.LastOpenedFolder = selectedFolder;
						GameSettings.Save();
					}
					_ = LoadMapFolderAsync(selectedFolder);
				}
			})
		);

		if (err != Error.Ok)
		{
			ShowFeedback(TranslationServer.Translate("Map could not be loaded."));
		}
	}

	private async System.Threading.Tasks.Task<bool> PromptAndUpgradeMapIfNeededAsync(string selectedFolder)
	{
		var upgradeService = _mapUpgradeService ?? MapUpgradeService.Instance;
		if (upgradeService == null || !upgradeService.NeedsUpgrade(selectedFolder, out string currentVersion, out string targetVersion))
		{
			return true;
		}

		if (upgradeService.IsMapNewerThanGame(currentVersion, targetVersion))
		{
			ShowMapNewerThanGameErrorDialog(selectedFolder, currentVersion, targetVersion);
			return false;
		}

		var tcs = new System.Threading.Tasks.TaskCompletionSource<bool>();

		var popup = new Panel();
		popup.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
		popup.AddThemeStyleboxOverride("panel", UIStyle.CreateBgGradient());
		AddChild(popup);

		var cardPanel = new Panel();
		cardPanel.CustomMinimumSize = new Vector2(540, 290);
		cardPanel.SetAnchorsAndOffsetsPreset(LayoutPreset.Center);
		cardPanel.AddThemeStyleboxOverride("panel", UIStyle.CreateStonePanel(true));
		popup.AddChild(cardPanel);

		var vbox = new VBoxContainer();
		vbox.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
		vbox.CustomMinimumSize = new Vector2(500, 260);
		vbox.SizeFlagsHorizontal = SizeFlags.ExpandFill;
		vbox.SizeFlagsVertical = SizeFlags.ExpandFill;
		cardPanel.AddChild(vbox);

		vbox.AddChild(new Control { CustomMinimumSize = new Vector2(0, 15) });

		var titleLabel = new Label();
		UIStyle.ApplyTitle(titleLabel, Tr("MAP BUILD MISMATCH"), 20);
		titleLabel.AddThemeColorOverride("font_color", new Color(0.95f, 0.4f, 0.3f));
		vbox.AddChild(titleLabel);

		vbox.AddChild(new Control { CustomMinimumSize = new Vector2(0, 8) });

		var descLabel = new Label();
		descLabel.Text = $"{string.Format(Tr("Map: {0}"), System.IO.Path.GetFileName(selectedFolder))}\n{string.Format(Tr("Map Build: {0} | Editor Build: {1}"), currentVersion, targetVersion)}\n\n{Tr("This map format needs to be upgraded before it can be opened. Would you like to upgrade now?")}";
		descLabel.HorizontalAlignment = HorizontalAlignment.Center;
		descLabel.AddThemeFontSizeOverride("font_size", 13);
		descLabel.AddThemeColorOverride("font_color", new Color(0.9f, 0.9f, 0.95f));
		vbox.AddChild(descLabel);

		var progressBar = new ProgressBar();
		progressBar.CustomMinimumSize = new Vector2(460, 20);
		progressBar.MinValue = 0;
		progressBar.MaxValue = 100;
		progressBar.Value = 0;
		progressBar.Visible = false;
		vbox.AddChild(progressBar);

		var statusLabel = new Label();
		statusLabel.HorizontalAlignment = HorizontalAlignment.Center;
		statusLabel.AddThemeFontSizeOverride("font_size", 12);
		statusLabel.AddThemeColorOverride("font_color", UIStyle.ColorGoldDull);
		statusLabel.Visible = false;
		vbox.AddChild(statusLabel);

		vbox.AddChild(new Control { CustomMinimumSize = new Vector2(0, 12) });

		var buttonRow = new HBoxContainer();
		buttonRow.Alignment = BoxContainer.AlignmentMode.Center;
		buttonRow.AddThemeConstantOverride("separation", 20);
		vbox.AddChild(buttonRow);

		var upgradeBtn = new Button();
		upgradeBtn.Flat = false;
		upgradeBtn.AddThemeConstantOverride("icon_max_width", 0);
		upgradeBtn.AddThemeStyleboxOverride("normal", UIStyle.CreateButtonNormal());
		upgradeBtn.AddThemeStyleboxOverride("hover", UIStyle.CreateButtonHover());
		upgradeBtn.AddThemeStyleboxOverride("pressed", UIStyle.CreateButtonPressed());
		upgradeBtn.AddThemeStyleboxOverride("focus", new StyleBoxEmpty());
		UIStyle.ApplyButtonText(upgradeBtn, Tr("UPGRADE MAP"), 14);
		upgradeBtn.CustomMinimumSize = new Vector2(160, 38);
		buttonRow.AddChild(upgradeBtn);

		var cancelBtn = new Button();
		cancelBtn.Flat = false;
		cancelBtn.AddThemeConstantOverride("icon_max_width", 0);
		cancelBtn.AddThemeStyleboxOverride("normal", UIStyle.CreateButtonNormal());
		cancelBtn.AddThemeStyleboxOverride("hover", UIStyle.CreateButtonHover());
		cancelBtn.AddThemeStyleboxOverride("pressed", UIStyle.CreateButtonPressed());
		cancelBtn.AddThemeStyleboxOverride("focus", new StyleBoxEmpty());
		UIStyle.ApplyButtonText(cancelBtn, Tr("CANCEL"), 14);
		cancelBtn.CustomMinimumSize = new Vector2(120, 38);
		buttonRow.AddChild(cancelBtn);

		cancelBtn.Pressed += () =>
		{
			UIManager.Instance?.PlayClickSound();
			popup.QueueFree();
			tcs.TrySetResult(false);
		};

		upgradeBtn.Pressed += async () =>
		{
			UIManager.Instance?.PlayClickSound();
			upgradeBtn.Disabled = true;
			cancelBtn.Disabled = true;
			progressBar.Visible = true;
			statusLabel.Visible = true;

			var progress = new Progress<MigrationProgressUpdate>(update =>
			{
				progressBar.Value = update.TotalSteps > 0 ? (double)update.StepIndex / update.TotalSteps * 100.0 : 0;
				statusLabel.Text = $"[{update.StepIndex}/{update.TotalSteps}] {update.CurrentMigration}: {update.Message}";
			});

			var upgradeResult = await upgradeService.UpgradeMapAsync(selectedFolder, progress);

			if (upgradeResult.Success)
			{
				statusLabel.Text = Tr("Upgrade completed successfully!");
				statusLabel.AddThemeColorOverride("font_color", new Color(0.3f, 0.9f, 0.3f));
				await ToSignal(GetTree().CreateTimer(0.6f), SceneTreeTimer.SignalName.Timeout);
				popup.QueueFree();
				tcs.TrySetResult(true);
			}
			else
			{
				statusLabel.Text = $"{Tr("Upgrade failed:")} {upgradeResult.ErrorMessage}";
				statusLabel.AddThemeColorOverride("font_color", new Color(0.95f, 0.3f, 0.3f));
				cancelBtn.Disabled = false;
				UIStyle.ApplyButtonText(cancelBtn, Tr("CLOSE"), 14);
			}
		};

		return await tcs.Task;
	}

	private void ShowMapNewerThanGameErrorDialog(string selectedFolder, string mapVersion, string gameVersion)
	{
		var popup = new Panel();
		popup.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
		popup.AddThemeStyleboxOverride("panel", UIStyle.CreateBgGradient());
		AddChild(popup);

		var cardPanel = new Panel();
		cardPanel.CustomMinimumSize = new Vector2(540, 260);
		cardPanel.SetAnchorsAndOffsetsPreset(LayoutPreset.Center);
		cardPanel.AddThemeStyleboxOverride("panel", UIStyle.CreateStonePanel(true));
		popup.AddChild(cardPanel);

		var vbox = new VBoxContainer();
		vbox.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
		vbox.CustomMinimumSize = new Vector2(500, 230);
		vbox.SizeFlagsHorizontal = SizeFlags.ExpandFill;
		vbox.SizeFlagsVertical = SizeFlags.ExpandFill;
		cardPanel.AddChild(vbox);

		vbox.AddChild(new Control { CustomMinimumSize = new Vector2(0, 15) });

		var titleLabel = new Label();
		UIStyle.ApplyTitle(titleLabel, Tr("MAP REQUIRES NEWER GAME VERSION"), 18);
		titleLabel.AddThemeColorOverride("font_color", new Color(0.95f, 0.4f, 0.3f));
		vbox.AddChild(titleLabel);

		vbox.AddChild(new Control { CustomMinimumSize = new Vector2(0, 8) });

		var descLabel = new Label();
		descLabel.Text = $"{string.Format(Tr("Map: {0}"), System.IO.Path.GetFileName(selectedFolder))}\n{string.Format(Tr("Map Build: {0} | Editor Build: {1}"), mapVersion, gameVersion)}\n\n{Tr("This map was created with a newer version of the game. Downgrading maps is not supported.\nPlease update the game to open this map.")}";
		descLabel.HorizontalAlignment = HorizontalAlignment.Center;
		descLabel.AutowrapMode = TextServer.AutowrapMode.WordSmart;
		descLabel.AddThemeFontSizeOverride("font_size", 13);
		descLabel.AddThemeColorOverride("font_color", new Color(0.9f, 0.9f, 0.95f));
		vbox.AddChild(descLabel);

		vbox.AddChild(new Control { CustomMinimumSize = new Vector2(0, 16) });

		var buttonRow = new HBoxContainer();
		buttonRow.Alignment = BoxContainer.AlignmentMode.Center;
		vbox.AddChild(buttonRow);

		var closeBtn = new Button();
		closeBtn.Flat = false;
		closeBtn.AddThemeConstantOverride("icon_max_width", 0);
		closeBtn.AddThemeStyleboxOverride("normal", UIStyle.CreateButtonNormal());
		closeBtn.AddThemeStyleboxOverride("hover", UIStyle.CreateButtonHover());
		closeBtn.AddThemeStyleboxOverride("pressed", UIStyle.CreateButtonPressed());
		closeBtn.AddThemeStyleboxOverride("focus", new StyleBoxEmpty());
		UIStyle.ApplyButtonText(closeBtn, Tr("CLOSE"), 14);
		closeBtn.CustomMinimumSize = new Vector2(120, 38);
		buttonRow.AddChild(closeBtn);

		closeBtn.Pressed += () =>
		{
			UIManager.Instance?.PlayClickSound();
			popup.QueueFree();
		};
	}

	private static bool IsValidMapFolder(string folder)
	{
		return System.IO.File.Exists(System.IO.Path.Combine(folder, "metadata.json"))
			&& System.IO.File.Exists(System.IO.Path.Combine(folder, "manifest.json"));
	}

	private void ShowInvalidMapFolderErrorDialog(string selectedFolder)
	{
		var popup = new Panel();
		popup.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
		popup.AddThemeStyleboxOverride("panel", UIStyle.CreateBgGradient());
		AddChild(popup);

		var cardPanel = new Panel();
		cardPanel.CustomMinimumSize = new Vector2(520, 240);
		cardPanel.SetAnchorsAndOffsetsPreset(LayoutPreset.Center);
		cardPanel.AddThemeStyleboxOverride("panel", UIStyle.CreateStonePanel(true));
		popup.AddChild(cardPanel);

		var vbox = new VBoxContainer();
		vbox.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
		vbox.CustomMinimumSize = new Vector2(480, 210);
		vbox.SizeFlagsHorizontal = SizeFlags.ExpandFill;
		vbox.SizeFlagsVertical = SizeFlags.ExpandFill;
		cardPanel.AddChild(vbox);

		vbox.AddChild(new Control { CustomMinimumSize = new Vector2(0, 15) });

		var titleLabel = new Label();
		UIStyle.ApplyTitle(titleLabel, Tr("INVALID MAP FOLDER"), 20);
		titleLabel.AddThemeColorOverride("font_color", new Color(0.95f, 0.4f, 0.3f));
		vbox.AddChild(titleLabel);

		vbox.AddChild(new Control { CustomMinimumSize = new Vector2(0, 8) });

		var descLabel = new Label();
		descLabel.Text = $"{string.Format(Tr("Folder: {0}"), System.IO.Path.GetFileName(selectedFolder))}\n\n{Tr("The selected folder is not a valid map. Required files metadata.json and manifest.json were not found.")}";
		descLabel.HorizontalAlignment = HorizontalAlignment.Center;
		descLabel.AutowrapMode = TextServer.AutowrapMode.WordSmart;
		descLabel.AddThemeFontSizeOverride("font_size", 13);
		descLabel.AddThemeColorOverride("font_color", new Color(0.9f, 0.9f, 0.95f));
		vbox.AddChild(descLabel);

		vbox.AddChild(new Control { CustomMinimumSize = new Vector2(0, 16) });

		var buttonRow = new HBoxContainer();
		buttonRow.Alignment = BoxContainer.AlignmentMode.Center;
		vbox.AddChild(buttonRow);

		var closeBtn = new Button();
		closeBtn.Flat = false;
		closeBtn.AddThemeConstantOverride("icon_max_width", 0);
		closeBtn.AddThemeStyleboxOverride("normal", UIStyle.CreateButtonNormal());
		closeBtn.AddThemeStyleboxOverride("hover", UIStyle.CreateButtonHover());
		closeBtn.AddThemeStyleboxOverride("pressed", UIStyle.CreateButtonPressed());
		closeBtn.AddThemeStyleboxOverride("focus", new StyleBoxEmpty());
		UIStyle.ApplyButtonText(closeBtn, Tr("CLOSE"), 14);
		closeBtn.CustomMinimumSize = new Vector2(120, 38);
		buttonRow.AddChild(closeBtn);

		closeBtn.Pressed += () =>
		{
			UIManager.Instance?.PlayClickSound();
			popup.QueueFree();
		};
	}

	public bool LoadMapFolder(string selectedFolder)
	{
		if (!System.IO.Directory.Exists(selectedFolder)) return false;

		if (!IsValidMapFolder(selectedFolder))
		{
			ShowInvalidMapFolderErrorDialog(selectedFolder);
			return false;
		}

		var upgradeService = _mapUpgradeService ?? MapUpgradeService.Instance;
		if (upgradeService != null && upgradeService.NeedsUpgrade(selectedFolder, out _, out _))
		{
			_ = LoadMapFolderAsync(selectedFolder);
			return true;
		}

		GameHost.Instance?.ClearMapEntirely();
		_lastUsedFolder = selectedFolder;
		_currentSourceFolder = selectedFolder;
		if (!IsRestrictedSaveDirectory(selectedFolder))
		{
			GameSettings.LastOpenedFolder = selectedFolder;
			GameSettings.Save();
		}

		ShowFeedback(TranslationServer.Translate("Loading map..."));
		CopyFolderToTempWorkspace(selectedFolder);

		try
		{
			MapWorkspaceService.EnsureGlbAssetsOptimized(_tempWorkspacePath);
			MapWorkspaceService.EnsurePngAssetsConverted(_tempWorkspacePath);
			LoadMapProperties();
			ReadMetadataAndRefreshTextures();
			string terrainPath = System.IO.Path.Combine(_tempWorkspacePath, "terrain.json");
			bool success = GameHost.Instance?.LoadMapFromFile(terrainPath, ensureGlbOptimized: false) ?? false;

			if (success)
			{
				if (OperatingSystem.IsWindows())
				{
					VSCodeManager.Instance.SaveRecentMapDir(selectedFolder);
				}
				_lastTerrainSyncTime = GetMaxTerrainWriteTime(terrainPath);
				_lastMetadataSyncTime = GetLastWriteTimeSafe(System.IO.Path.Combine(_tempWorkspacePath, "metadata.json"));
				_editorService?.StartWorkspaceWatcher(_tempWorkspacePath);
				ShowFeedback(string.Format(TranslationServer.Translate("Map loaded successfully from folder {0}!"), System.IO.Path.GetFileName(selectedFolder)));
				SaveCurrentDirectoryBlake3();
			}
			else
			{
				ShowFeedback(TranslationServer.Translate("Failed to load map files from folder!"));
			}

			return success;
		}
		catch (Exception ex)
		{
			GD.PrintErr($"[MapEditorHUD] Failed to load map folder: {ex.Message}");
			return false;
		}
	}

	public async System.Threading.Tasks.Task<bool> LoadMapFolderAsync(string selectedFolder)
	{
		if (!System.IO.Directory.Exists(selectedFolder)) return false;

		if (!IsValidMapFolder(selectedFolder))
		{
			ShowInvalidMapFolderErrorDialog(selectedFolder);
			return false;
		}

		bool canProceed = await PromptAndUpgradeMapIfNeededAsync(selectedFolder);
		if (!canProceed)
		{
			ShowFeedback(TranslationServer.Translate("Map loading cancelled."));
			return false;
		}

		GameHost.Instance?.ClearMapEntirely();
		_isSyncing = true;
		try
		{
			ModelCache.Clear();
			PathUtils.ClearCache();

			_lastUsedFolder = selectedFolder;
			_currentSourceFolder = selectedFolder;
			if (!IsRestrictedSaveDirectory(selectedFolder))
			{
				GameSettings.LastOpenedFolder = selectedFolder;
				GameSettings.Save();
			}

			ShowFeedback(TranslationServer.Translate("Loading map..."));
			await System.Threading.Tasks.Task.Run(() => CopyFolderToTempWorkspace(selectedFolder));

			string terrainPath = System.IO.Path.Combine(_tempWorkspacePath, "terrain.json");
			string metadataPath = System.IO.Path.Combine(_tempWorkspacePath, "metadata.json");
			_lastTerrainSyncTime = GetMaxTerrainWriteTime(terrainPath);
			_lastMetadataSyncTime = GetLastWriteTimeSafe(metadataPath);

			await MapWorkspaceService.EnsureGlbAssetsOptimizedCooperativeAsync(_tempWorkspacePath, async (current, total, fileName) =>
			{
				ShowFeedback(string.Format(TranslationServer.Translate("Optimizing 3D asset {0}/{1}: {2}..."), current, total, fileName));
				await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
			});

			await MapWorkspaceService.EnsurePngAssetsConvertedCooperativeAsync(_tempWorkspacePath, async (current, total, fileName) =>
			{
				ShowFeedback(string.Format(TranslationServer.Translate("Converting texture {0}/{1}: {2}..."), current, total, fileName));
				await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
			});

			LoadMapProperties();
			ReadMetadataAndRefreshTextures();
			bool success = GameHost.Instance?.LoadMapFromFile(terrainPath, ensureGlbOptimized: false) ?? false;

			if (success)
			{
				if (OperatingSystem.IsWindows())
				{
					VSCodeManager.Instance.SaveRecentMapDir(selectedFolder);
				}
				_lastTerrainSyncTime = GetMaxTerrainWriteTime(terrainPath);
				_lastMetadataSyncTime = GetLastWriteTimeSafe(metadataPath);
				_editorService?.StartWorkspaceWatcher(_tempWorkspacePath);
				ShowFeedback(string.Format(TranslationServer.Translate("Map loaded successfully from folder {0}!"), System.IO.Path.GetFileName(selectedFolder)));
				SaveCurrentDirectoryBlake3();
			}
			else
			{
				ShowFeedback(TranslationServer.Translate("Failed to load map files from folder!"));
			}

			return success;
		}
		catch (Exception ex)
		{
			GD.PrintErr($"[MapEditorHUD] Failed to load map folder: {ex.Message}");
			return false;
		}
		finally
		{
			_isSyncing = false;
		}
	}

	public static void ResetFolderLocations()
	{
		_lastUsedFolder = null;
		_currentSourceFolder = null;
		_pendingCasSourceDirectory = null;
		_pendingDefaultSaveFolder = null;
	}

	public void ResetToBlankMap()
	{
		ResetFolderLocations();
		GameHost.Instance?.ClearMapEntirely();
		LoadMapProperties();
		UpdateMapNameHeader();
		SaveCurrentDirectoryBlake3();
	}

	public static string GetDocumentsDirectory()
	{
		string docs = System.Environment.GetFolderPath(System.Environment.SpecialFolder.MyDocuments);
		if (string.IsNullOrEmpty(docs))
		{
			string userProfile = System.Environment.GetFolderPath(System.Environment.SpecialFolder.UserProfile);
			docs = !string.IsNullOrEmpty(userProfile) ? System.IO.Path.Combine(userProfile, "Documents") : ProjectSettings.GlobalizePath("user://");
		}
		try
		{
			if (!System.IO.Directory.Exists(docs))
			{
				System.IO.Directory.CreateDirectory(docs);
			}
		}
		catch { }
		return docs;
	}

	private string GetInitialDirectory()
	{
		if (!string.IsNullOrEmpty(_lastUsedFolder) && !IsRestrictedSaveDirectory(_lastUsedFolder) && System.IO.Directory.Exists(_lastUsedFolder))
		{
			return _lastUsedFolder;
		}
		if (!string.IsNullOrEmpty(_currentSourceFolder) && !IsRestrictedSaveDirectory(_currentSourceFolder) && System.IO.Directory.Exists(_currentSourceFolder))
		{
			return _currentSourceFolder;
		}
		if (!string.IsNullOrEmpty(GameSettings.LastOpenedFolder) && !IsRestrictedSaveDirectory(GameSettings.LastOpenedFolder) && System.IO.Directory.Exists(GameSettings.LastOpenedFolder))
		{
			return GameSettings.LastOpenedFolder;
		}
		return GetDocumentsDirectory();
	}

	public string GetDefaultDevelopmentMapDirectory(string? mapName = null)
	{
		string name = !string.IsNullOrWhiteSpace(mapName) ? mapName : GetMapNameFromMetadata();
		string cleanName = string.Join("_", name.Split(System.IO.Path.GetInvalidFileNameChars(), StringSplitOptions.RemoveEmptyEntries)).Trim();
		if (string.IsNullOrEmpty(cleanName) || cleanName.Equals("Untitled Map", StringComparison.OrdinalIgnoreCase)) cleanName = "new_map";

		string docs = GetDocumentsDirectory();
		return System.IO.Path.Combine(docs, cleanName);
	}

	public static bool IsRestrictedSaveDirectory(string path)
	{
		if (string.IsNullOrWhiteSpace(path)) return true;
		try
		{
			string fullPath = System.IO.Path.GetFullPath(path).Replace("\\", "/").TrimEnd('/');

			string tempWorkspace = System.IO.Path.GetFullPath(ProjectSettings.GlobalizePath("user://temp_map_workspace")).Replace("\\", "/").TrimEnd('/');
			string mapBackups = System.IO.Path.GetFullPath(ProjectSettings.GlobalizePath("user://map_backups")).Replace("\\", "/").TrimEnd('/');

			if (fullPath.Equals(tempWorkspace, StringComparison.OrdinalIgnoreCase) || fullPath.StartsWith(tempWorkspace + "/", StringComparison.OrdinalIgnoreCase))
			{
				return false;
			}
			if (fullPath.Equals(mapBackups, StringComparison.OrdinalIgnoreCase) || fullPath.StartsWith(mapBackups + "/", StringComparison.OrdinalIgnoreCase))
			{
				return false;
			}

			string userDir = System.IO.Path.GetFullPath(ProjectSettings.GlobalizePath("user://")).Replace("\\", "/").TrimEnd('/');
			if (fullPath.Equals(userDir, StringComparison.OrdinalIgnoreCase) || fullPath.StartsWith(userDir + "/", StringComparison.OrdinalIgnoreCase))
			{
				return true;
			}

			string resDir = System.IO.Path.GetFullPath(ProjectSettings.GlobalizePath("res://")).Replace("\\", "/").TrimEnd('/');
			if (fullPath.Equals(resDir, StringComparison.OrdinalIgnoreCase) || fullPath.StartsWith(resDir + "/", StringComparison.OrdinalIgnoreCase))
			{
				return true;
			}
		}
		catch { }

		return false;
	}


	private NSec.Cryptography.Key GetOrGenerateAuthorshipKey()
	{
		string keyDir = ProjectSettings.GlobalizePath("user://appdata/keys/");
		string defaultUsername = LobbyManager.Instance?.AuthenticatedUsername ?? string.Empty;
		var (key, data, keyPath, createdNew) = AuthorshipKeyHelper.GetOrGenerateKeyInfo(keyDir, defaultUsername);
		if (createdNew)
		{
			ShowFeedback(TranslationServer.Translate("A new authorship key has been generated at ") + keyPath + TranslationServer.Translate(". Please backup this file to retain your authorship identity."));
		}
		return key;
	}

	private void ShowGreenlightStatusDialog(string mapTitle, string mapVersion, int verifiedGoodReviews, int totalReviews, double averageRating, bool isGreenlit)
	{
		var overlay = new ColorRect();
		overlay.Name = "GreenlightStatusOverlay";
		overlay.Color = new Color(0, 0, 0, 0.75f);
		overlay.SetAnchorsPreset(LayoutPreset.FullRect);
		overlay.MouseFilter = Control.MouseFilterEnum.Stop;
		overlay.ZIndex = 1000;
		AddChild(overlay);

		var center = new CenterContainer();
		center.SetAnchorsPreset(LayoutPreset.FullRect);
		overlay.AddChild(center);

		var panel = new PanelContainer();
		panel.CustomMinimumSize = new Vector2(640, 440);
		var style = new StyleBoxFlat();
		style.BgColor = new Color(0.12f, 0.14f, 0.20f, 0.98f);
		style.BorderWidthTop = 2; style.BorderWidthBottom = 2; style.BorderWidthLeft = 2; style.BorderWidthRight = 2;
		style.BorderColor = isGreenlit ? new Color(0.3f, 0.8f, 0.4f, 1f) : new Color(0.8f, 0.4f, 0.3f, 1f);
		style.CornerRadiusTopLeft = 8; style.CornerRadiusTopRight = 8; style.CornerRadiusBottomLeft = 8; style.CornerRadiusBottomRight = 8;
		panel.AddThemeStyleboxOverride("panel", style);
		center.AddChild(panel);

		var margin = new MarginContainer();
		margin.AddThemeConstantOverride("margin_top", 22);
		margin.AddThemeConstantOverride("margin_bottom", 22);
		margin.AddThemeConstantOverride("margin_left", 26);
		margin.AddThemeConstantOverride("margin_right", 26);
		panel.AddChild(margin);

		var vbox = new VBoxContainer();
		vbox.AddThemeConstantOverride("separation", 14);
		margin.AddChild(vbox);

		var lblTitle = new Label();
		lblTitle.Text = isGreenlit ? TranslationServer.Translate("MAP GREENLIT FOR PUBLISHING") : TranslationServer.Translate("MAP NOT YET GREENLIT");
		lblTitle.HorizontalAlignment = HorizontalAlignment.Center;
		lblTitle.AddThemeFontSizeOverride("font_size", 20);
		lblTitle.AddThemeColorOverride("font_color", isGreenlit ? new Color(0.4f, 0.9f, 0.5f) : new Color(1f, 0.6f, 0.4f));
		vbox.AddChild(lblTitle);

		var lblDesc = new Label();
		lblDesc.Text = isGreenlit 
			? string.Format(TranslationServer.Translate("'{0}' v{1} has met the community greenlight requirement and can now be published to the public registry."), mapTitle, mapVersion)
			: string.Format(TranslationServer.Translate("'{0}' v{1} is currently in Beta-Testing. Maps must achieve 100 Verified Good Reviews before graduating to the public registry."), mapTitle, mapVersion);
		lblDesc.AutowrapMode = TextServer.AutowrapMode.WordSmart;
		lblDesc.HorizontalAlignment = HorizontalAlignment.Center;
		lblDesc.AddThemeFontSizeOverride("font_size", 13);
		lblDesc.AddThemeColorOverride("font_color", new Color(0.85f, 0.85f, 0.9f));
		vbox.AddChild(lblDesc);

		var metricsBox = new VBoxContainer();
		metricsBox.AddThemeConstantOverride("separation", 8);
		vbox.AddChild(metricsBox);

		bool reviewsMet = verifiedGoodReviews >= 100;
		var lblReviews = new Label();
		lblReviews.Text = string.Format(TranslationServer.Translate("• Total Verified Good Reviews: {0} / 100 {1}"), verifiedGoodReviews, reviewsMet ? "✓" : "");
		lblReviews.AddThemeColorOverride("font_color", reviewsMet ? new Color(0.4f, 0.9f, 0.5f) : new Color(0.9f, 0.8f, 0.6f));
		lblReviews.AddThemeFontSizeOverride("font_size", 15);
		metricsBox.AddChild(lblReviews);

		if (totalReviews > 0)
		{
			var lblAggregate = new Label();
			lblAggregate.Text = string.Format(TranslationServer.Translate("• Public Aggregate Reviews: {0} (Average Rating: {1:F1} ★)"), totalReviews, averageRating);
			lblAggregate.AddThemeColorOverride("font_color", new Color(0.7f, 0.75f, 0.85f));
			lblAggregate.AddThemeFontSizeOverride("font_size", 12);
			metricsBox.AddChild(lblAggregate);
		}

		var tip = new Label();
		tip.Text = TranslationServer.Translate("How Verified Good Reviews are determined:\nA review qualifies as Verified & Good when submitted by a player who:\n  1. Is logged in with a verified account (Steam, Discord, etc.)\n  2. Has played your map for at least 30 minutes across 3 or more completed games\n  3. Gives your map a rating of 3 stars or higher\n\nHost multiplayer lobbies or share your map peer-to-peer to build verified reviews from your community.");
		tip.AutowrapMode = TextServer.AutowrapMode.WordSmart;
		tip.AddThemeFontSizeOverride("font_size", 11);
		tip.AddThemeColorOverride("font_color", new Color(0.65f, 0.7f, 0.8f));
		vbox.AddChild(tip);

		var hbox = new HBoxContainer();
		hbox.Alignment = BoxContainer.AlignmentMode.Center;
		hbox.AddThemeConstantOverride("separation", 20);
		vbox.AddChild(hbox);

		var btnClose = new Button();
		btnClose.Text = TranslationServer.Translate("OK");
		btnClose.CustomMinimumSize = new Vector2(100, 36);
		btnClose.Pressed += () => overlay.QueueFree();
		hbox.AddChild(btnClose);
	}
	private async void PublishMapAction(bool fullExport = false)
	{
		if (GameHost.Instance == null || _isSyncing) return;
		_isSyncing = true;
		if (_editorService != null)
		{
			_editorService.IsPaused = true;
		}

		string workspace = !string.IsNullOrEmpty(_tempWorkspacePath) ? _tempWorkspacePath : MapWorkspaceService.GetActiveWorkspacePath();

		var (assetsValid, missingAssets) = MapAssetHelper.ValidateWorkspaceAssets(workspace);
		if (!assetsValid)
		{
			ShowFeedback(string.Format(TranslationServer.Translate("Publish failed: Map is missing required asset files:\n{0}"), string.Join(", ", missingAssets.Take(4)) + (missingAssets.Count > 4 ? "..." : "")));
			AppendWasmConsoleLog($"[ERROR] Publish failed. Missing required asset files:\n  {string.Join("\n  ", missingAssets)}");
			return;
		}

		var (sizesValid, oversizedAssets) = MapAssetHelper.ValidateWorkspaceAssetSizes(workspace);
		if (!sizesValid)
		{
			string oversizedSummary = string.Join("\n", oversizedAssets.Take(4).Select(o => $"• {o.RelativePath} ({o.SizeMB:F2} MB > 15 MB)")) + (oversizedAssets.Count > 4 ? "\n..." : "");
			ShowFeedback(string.Format(TranslationServer.Translate("Publish failed: Assets exceed maximum 15 MB size limit:\n{0}"), oversizedSummary));
			AppendWasmConsoleLog($"[ERROR] Publish failed. Assets exceed maximum 15 MB size limit:\n{string.Join("\n", oversizedAssets.Select(o => $"  {o.RelativePath} ({o.SizeMB:F2} MB)"))}");
			return;
		}

		string mapTitle = GetMapNameFromMetadata();
		if (string.IsNullOrWhiteSpace(mapTitle) || mapTitle.Equals("Untitled Map", StringComparison.OrdinalIgnoreCase))
		{
			mapTitle = "UntitledMap";
		}
		string mapVersion = GetMapVersionFromMetadata();
		if (string.IsNullOrWhiteSpace(mapVersion))
		{
			mapVersion = "1.0.0";
		}
		string metaJsonPath = System.IO.Path.Combine(workspace, "metadata.json");
		string activeConfigPath = metaJsonPath;
		string tempTerrainPath = System.IO.Path.Combine(workspace, "terrain.json");
		string manifestJsonPath = System.IO.Path.Combine(workspace, "manifest.json");

		var popup = new Panel();
		popup.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
		popup.AddThemeStyleboxOverride("panel", UIStyle.CreateBgGradient());
		popup.ZIndex = 1100;
		AddChild(popup);

		var cardPanel = new Panel();
		cardPanel.CustomMinimumSize = new Vector2(580, 280);
		cardPanel.SetAnchorsAndOffsetsPreset(LayoutPreset.Center);
		cardPanel.AddThemeStyleboxOverride("panel", UIStyle.CreateStonePanel(true));
		popup.AddChild(cardPanel);

		var vbox = new VBoxContainer();
		vbox.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
		vbox.CustomMinimumSize = new Vector2(540, 240);
		vbox.SizeFlagsHorizontal = SizeFlags.ExpandFill;
		vbox.SizeFlagsVertical = SizeFlags.ExpandFill;
		vbox.AddThemeConstantOverride("separation", 10);
		cardPanel.AddChild(vbox);

		vbox.AddChild(new Control { CustomMinimumSize = new Vector2(0, 10) });

		var titleLabel = new Label();
		UIStyle.ApplyTitle(titleLabel, "🚀 " + TranslationServer.Translate("PUBLISHING MAP PACKAGE"), 20);
		titleLabel.HorizontalAlignment = HorizontalAlignment.Center;
		vbox.AddChild(titleLabel);

		var descLabel = new Label();
		descLabel.Text = string.Format(TranslationServer.Translate("Map: {0} (v{1})"), mapTitle, mapVersion);
		descLabel.HorizontalAlignment = HorizontalAlignment.Center;
		descLabel.AddThemeFontSizeOverride("font_size", 13);
		descLabel.AddThemeColorOverride("font_color", UIStyle.ColorGold);
		vbox.AddChild(descLabel);

		vbox.AddChild(new Control { CustomMinimumSize = new Vector2(0, 5) });

		var progressBar = new ProgressBar();
		progressBar.CustomMinimumSize = new Vector2(480, 24);
		progressBar.MinValue = 0;
		progressBar.MaxValue = 100;
		progressBar.Value = 0;
		vbox.AddChild(progressBar);

		var statusLabel = new Label();
		statusLabel.Text = TranslationServer.Translate("Checking publish eligibility...");
		statusLabel.HorizontalAlignment = HorizontalAlignment.Center;
		statusLabel.AddThemeFontSizeOverride("font_size", 13);
		statusLabel.AddThemeColorOverride("font_color", new Color(0.9f, 0.9f, 0.95f));
		statusLabel.AutowrapMode = TextServer.AutowrapMode.WordSmart;
		vbox.AddChild(statusLabel);

		vbox.AddChild(new Control { CustomMinimumSize = new Vector2(0, 5) });

		var buttonRow = new HBoxContainer();
		buttonRow.Alignment = BoxContainer.AlignmentMode.Center;
		vbox.AddChild(buttonRow);

		var closeBtn = new Button();
		closeBtn.Flat = false;
		closeBtn.AddThemeConstantOverride("icon_max_width", 0);
		closeBtn.AddThemeStyleboxOverride("normal", UIStyle.CreateButtonNormal());
		closeBtn.AddThemeStyleboxOverride("hover", UIStyle.CreateButtonHover());
		closeBtn.AddThemeStyleboxOverride("pressed", UIStyle.CreateButtonPressed());
		closeBtn.AddThemeStyleboxOverride("focus", new StyleBoxEmpty());
		UIStyle.ApplyButtonText(closeBtn, TranslationServer.Translate("CLOSE"), 14);
		closeBtn.CustomMinimumSize = new Vector2(130, 36);
		closeBtn.Visible = false;
		buttonRow.AddChild(closeBtn);

		closeBtn.Pressed += () =>
		{
			UIManager.Instance?.PlayClickSound();
			if (GodotObject.IsInstanceValid(popup))
			{
				popup.QueueFree();
			}
		};

		_wasmHasErrors = false;
		Action<string> logHandler = line => AppendWasmConsoleLog(line);
		Realm.Godot.WasmRuntime.OnWasmLog += logHandler;

		try
		{
			progressBar.Value = 5;
			statusLabel.Text = TranslationServer.Translate("Checking publish eligibility...");
			await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);

			string seedServerUrl = GameHost.Instance != null && GodotObject.IsInstanceValid(LobbyManager.Instance) ? LobbyManager.Instance.RegistryServerUrl : ServersConfigHelper.GetDefaultServerUrl();

			try
			{
				using var checkClient = new System.Net.Http.HttpClient { Timeout = TimeSpan.FromSeconds(10) };
				string creatorPubKey = "";
				try
				{
					var key = GetOrGenerateAuthorshipKey();
					creatorPubKey = Convert.ToBase64String(key.PublicKey.Export(KeyBlobFormat.RawPublicKey));
				}
				catch { }

				string statusKey = !string.IsNullOrEmpty(creatorPubKey)
					? $"{Uri.EscapeDataString(mapTitle)}_{Uri.EscapeDataString(mapVersion)}_{Uri.EscapeDataString(creatorPubKey)}"
					: $"{Uri.EscapeDataString(mapTitle)}_{Uri.EscapeDataString(mapVersion)}";
				string statusUrl = $"{seedServerUrl.TrimEnd('/')}/api/maps/greenlight_status/{statusKey}";
				var statusRes = await checkClient.GetAsync(statusUrl);
				if (statusRes.IsSuccessStatusCode)
				{
					string statusJson = await statusRes.Content.ReadAsStringAsync();
					var statusNode = JsonNode.Parse(statusJson);
					bool isGreenlit = statusNode?["isGreenlit"]?.GetValue<bool>() ?? false;
					int verifiedGoodReviews = statusNode?["verifiedGoodReviewsCount"]?.GetValue<int>() ?? 0;
					int totalReviews = statusNode?["totalReviewsCount"]?.GetValue<int>() ?? 0;
					double averageRating = statusNode?["averageRating"]?.GetValue<double>() ?? 0.0;

					if (!isGreenlit)
					{
						if (GodotObject.IsInstanceValid(popup))
						{
							popup.QueueFree();
						}
						ShowGreenlightStatusDialog(mapTitle, mapVersion, verifiedGoodReviews, totalReviews, averageRating, isGreenlit: false);
						return;
					}
				}
			}
			catch (Exception ex)
			{
				AppendWasmConsoleLog($"[WARN] Could not verify greenlight status: {ex.Message}");
			}

			progressBar.Value = 10;
			statusLabel.Text = TranslationServer.Translate("Saving map state & workspace...");
			await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);

			MapWorkspaceService.EnsureLicenseFile(workspace);
			GameHost.Instance.SaveMapToFile(tempTerrainPath, performReload: false);
			GameHost.Instance.EditorHasUnsavedChanges = false;
			InvalidateMetadataCache();

			if (OperatingSystem.IsWindows())
			{
				await VSCodeManager.Instance.SaveAllOpenFilesAsync();
			}

			progressBar.Value = 15;
			statusLabel.Text = TranslationServer.Translate("Verifying map assets...");
			await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);

			(assetsValid, missingAssets) = MapAssetHelper.ValidateWorkspaceAssets(workspace);
			if (!assetsValid)
			{
				progressBar.Value = 100;
				statusLabel.Text = "❌ " + TranslationServer.Translate("Missing required asset files. Publish aborted.");
				statusLabel.AddThemeColorOverride("font_color", new Color(0.95f, 0.3f, 0.3f));
				closeBtn.Visible = true;
				ShowFeedback(string.Format(TranslationServer.Translate("Publish failed: Map is missing required asset files:\n{0}"), string.Join(", ", missingAssets.Take(4)) + (missingAssets.Count > 4 ? "..." : "")));
				AppendWasmConsoleLog($"[ERROR] Publish aborted. Missing required asset files:\n  {string.Join("\n  ", missingAssets)}");
				return;
			}

			(sizesValid, oversizedAssets) = MapAssetHelper.ValidateWorkspaceAssetSizes(workspace);
			if (!sizesValid)
			{
				progressBar.Value = 100;
				statusLabel.Text = "❌ " + TranslationServer.Translate("Asset exceeds 15 MB size limit. Publish aborted.");
				statusLabel.AddThemeColorOverride("font_color", new Color(0.95f, 0.3f, 0.3f));
				closeBtn.Visible = true;
				string oversizedSummary = string.Join("\n", oversizedAssets.Take(4).Select(o => $"• {o.RelativePath} ({o.SizeMB:F2} MB > 15 MB)")) + (oversizedAssets.Count > 4 ? "\n..." : "");
				ShowFeedback(string.Format(TranslationServer.Translate("Publish failed: Assets exceed maximum 15 MB size limit:\n{0}"), oversizedSummary));
				AppendWasmConsoleLog($"[ERROR] Publish aborted. Assets exceed maximum 15 MB size limit:\n{string.Join("\n", oversizedAssets.Select(o => $"  {o.RelativePath} ({o.SizeMB:F2} MB)"))}");
				return;
			}

			progressBar.Value = 20;
			statusLabel.Text = TranslationServer.Translate("Compiling WASM map script...");
			await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);

			await CompileAndSignMapAsync(workspace, skipAttribution: true);

			if (_wasmHasErrors)
			{
				progressBar.Value = 100;
				statusLabel.Text = "❌ " + TranslationServer.Translate("WASM compilation failed. Publish aborted.");
				statusLabel.AddThemeColorOverride("font_color", new Color(0.95f, 0.3f, 0.3f));
				closeBtn.Visible = true;
				ShowFeedback(TranslationServer.Translate("WASM compilation failed. Publish aborted."));
				return;
			}

			var csprojFiles = System.IO.Directory.GetFiles(workspace, "*.csproj", System.IO.SearchOption.TopDirectoryOnly);
			if (csprojFiles.Length > 0 || System.IO.File.Exists(System.IO.Path.Combine(workspace, "MapScript.cs")))
			{
				string binDir = System.IO.Path.Combine(workspace, "bin");
				string wasmPath = null;
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

				if (string.IsNullOrEmpty(wasmPath) || !System.IO.File.Exists(wasmPath))
				{
					_wasmHasErrors = true;
					progressBar.Value = 100;
					statusLabel.Text = "❌ " + TranslationServer.Translate("Compiled WASM binary missing. Publish aborted.");
					statusLabel.AddThemeColorOverride("font_color", new Color(0.95f, 0.3f, 0.3f));
					closeBtn.Visible = true;
					ShowFeedback(TranslationServer.Translate("Compiled WASM binary missing. Publish aborted."));
					return;
				}
			}

			progressBar.Value = 35;
			statusLabel.Text = TranslationServer.Translate("Optimizing 3D assets...");
			await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);

			await MapWorkspaceService.EnsureGlbAssetsOptimizedCooperativeAsync(workspace, async (current, total, fileName) =>
			{
				float fraction = total > 0 ? (float)current / total : 1.0f;
				progressBar.Value = 35 + fraction * 15;
				statusLabel.Text = string.Format(TranslationServer.Translate("Optimizing 3D model {0}/{1}: {2}..."), current, total, fileName);
				await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
			});

			progressBar.Value = 50;
			statusLabel.Text = TranslationServer.Translate("Converting textures...");
			await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);

			await MapWorkspaceService.EnsurePngAssetsConvertedCooperativeAsync(workspace, async (current, total, fileName) =>
			{
				float fraction = total > 0 ? (float)current / total : 1.0f;
				progressBar.Value = 50 + fraction * 15;
				statusLabel.Text = string.Format(TranslationServer.Translate("Converting texture {0}/{1}: {2}..."), current, total, fileName);
				await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
			});

			progressBar.Value = 60;
			statusLabel.Text = TranslationServer.Translate("Generating map thumbnail...");
			await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);

			if (_minimapController != null)
			{
				await _minimapController.GenerateAndSaveMinimapThumbnailAsync(workspace);
			}

			progressBar.Value = 65;
			statusLabel.Text = TranslationServer.Translate("Generating manifest & indexing asset hashes...");
			await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);

			var authorshipKey = GetOrGenerateAuthorshipKey();
			string currentUsername = LobbyManager.Instance?.AuthenticatedUsername ?? "MapAuthor";
			string pubKeyBase64 = Convert.ToBase64String(authorshipKey.PublicKey.Export(KeyBlobFormat.RawPublicKey));

			MapWorkspaceService.NormalizeMetadataTextureEntries(workspace);

			if (System.IO.File.Exists(activeConfigPath))
			{
				try
				{
					var metaDoc = JsonNode.Parse(System.IO.File.ReadAllText(activeConfigPath)) as JsonObject;
					if (metaDoc != null)
					{
						metaDoc["EngineVersion"] = RealmVersion.GameBinaryVersion;
						SaveLoadService.CleanMetadataJsonSchema(metaDoc);
						MapJsonFormatter.SaveFormattedJson(activeConfigPath, metaDoc);
					}
				}
				catch { }
				_lastMetadataSyncTime = GetLastWriteTimeSafe(activeConfigPath);
			}
			EditorService.LastInternalSaveTimeUtc = DateTime.UtcNow;

			var manifest = MapManifest.CreateFromDirectory(workspace, mapTitle, currentUsername, mapVersion);
			manifestJsonPath = System.IO.Path.Combine(workspace, "manifest.json");
			string manifestJsonContent = manifest.ToJson();
			System.IO.File.WriteAllText(manifestJsonPath, manifestJsonContent);
			EditorService.LastInternalSaveTimeUtc = DateTime.UtcNow;

			var hashToRelativePath = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
			foreach (var pair in manifest.Files)
			{
				string norm = ContentAddressableStorage.NormalizeBlake3Hash(pair.Value);
				hashToRelativePath[norm] = pair.Key;
				hashToRelativePath[pair.Value] = pair.Key;
			}

			byte[] manifestBytes = System.Text.Encoding.UTF8.GetBytes(manifestJsonContent);
			string manifestBlake3 = RealmMetadataHelper.ComputeBlake3(manifestBytes, ".json");
			string manifestHash = $"{manifestBlake3}.json";
			byte[] manifestHashBytes = System.Text.Encoding.UTF8.GetBytes(manifestHash);
			byte[] manifestSigBytes = SignatureAlgorithm.Ed25519.Sign(authorshipKey, manifestHashBytes);
			string manifestSigBase64 = Convert.ToBase64String(manifestSigBytes);

			var distClient = new DistributionClient(seedServerUrl);

			progressBar.Value = 70;
			statusLabel.Text = TranslationServer.Translate("Initiating publish session with registry...");
			await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);

			var initReq = new PublishMapInitiateRequest
			{
				ManifestJson = manifestJsonContent,
				MapTitle = mapTitle,
				MapVersion = mapVersion,
				PublicKey = pubKeyBase64,
				Signature = manifestSigBase64,
				ReferencedHashes = manifest.Files.Values.ToList()
			};

			var initRes = await distClient.InitiatePublishAsync(initReq);
			if (!initRes.Success)
			{
				progressBar.Value = 100;
				string baseMsg = !string.IsNullOrEmpty(initRes.Message) ? initRes.Message : TranslationServer.Translate("Failed to initiate publish.");
				string errorText = "❌ " + string.Format(TranslationServer.Translate("Failed to initiate publish: {0}"), baseMsg);
				if (initRes.MissingHashes != null && initRes.MissingHashes.Count > 0)
				{
					string firstMissingHash = initRes.MissingHashes[0];
					string firstMissingName = hashToRelativePath.TryGetValue(firstMissingHash, out var rel) || hashToRelativePath.TryGetValue(ContentAddressableStorage.NormalizeBlake3Hash(firstMissingHash), out rel)
						? rel
						: firstMissingHash;
					errorText += "\n" + string.Format(TranslationServer.Translate("Missing asset: {0}"), firstMissingName);
				}
				statusLabel.Text = errorText;
				statusLabel.AddThemeColorOverride("font_color", new Color(0.95f, 0.3f, 0.3f));
				closeBtn.Visible = true;
				ShowFeedback(errorText.Replace("❌ ", ""));
				return;
			}

			if (initRes.MissingHashes.Count > 0)
			{
				long lastProgressTicks = 0;
				var (uploadSuccess, failedAsset, errorMsg) = await distClient.UploadMissingAssetsMultiThreadedAsync(
					workspace,
					initRes.MissingHashes,
					hashToRelativePath,
					currentUsername,
					pubKeyBase64,
					authorshipKey,
					mapTitle,
					mapVersion,
					initRes.SessionId,
					progressCallback: (done, total, fileName) =>
					{
						long now = System.Environment.TickCount64;
						if (now - lastProgressTicks < 50 && done < total)
						{
							return;
						}
						lastProgressTicks = now;
						Callable.From(() =>
						{
							if (GodotObject.IsInstanceValid(progressBar) && GodotObject.IsInstanceValid(statusLabel))
							{
								float fraction = total > 0 ? (float)done / total : 1.0f;
								progressBar.Value = 70 + fraction * 25;
								statusLabel.Text = string.Format(TranslationServer.Translate("Uploading asset {0}/{1}: {2}..."), done, total, fileName);
							}
						}).CallDeferred();
					},
					maximumConcurrency: 4
				);

				if (!uploadSuccess)
				{
					progressBar.Value = 100;
					statusLabel.Text = "❌ " + string.Format(TranslationServer.Translate("Failed to upload asset: {0}"), failedAsset);
					statusLabel.AddThemeColorOverride("font_color", new Color(0.95f, 0.3f, 0.3f));
					closeBtn.Visible = true;
					ShowFeedback($"Failed to upload asset: {failedAsset} ({errorMsg})");
					return;
				}
			}

			progressBar.Value = 95;
			statusLabel.Text = TranslationServer.Translate("Finalizing map publish...");
			await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);

			var finalizeReq = new PublishMapFinalizeRequest
			{
				SessionId = initRes.SessionId,
				MapTitle = mapTitle,
				MapVersion = mapVersion,
				PublicKey = pubKeyBase64,
				Signature = manifestSigBase64
			};

			var finalRes = await distClient.FinalizePublishAsync(finalizeReq);
			if (finalRes.Success)
			{
				progressBar.Value = 100;
				statusLabel.Text = "✓ " + TranslationServer.Translate("Map compiled & published successfully!");
				statusLabel.AddThemeColorOverride("font_color", new Color(0.3f, 0.9f, 0.3f));
				await ToSignal(GetTree().CreateTimer(0.6f), SceneTreeTimer.SignalName.Timeout);

				if (GodotObject.IsInstanceValid(popup))
				{
					popup.QueueFree();
				}

				ShowFeedback(TranslationServer.Translate("Map compiled & published successfully!"));
				UIManager.Instance?.ShowConfirmationDialog(
					TranslationServer.Translate("Map compiled & published successfully!"),
					() => { },
					confirmText: "OK",
					showCancel: false
				);
			}
			else
			{
				progressBar.Value = 100;
				string baseMsg = !string.IsNullOrEmpty(finalRes.Message) ? finalRes.Message : TranslationServer.Translate("Failed to finalize publish.");
				string errorText = "❌ " + string.Format(TranslationServer.Translate("Failed to finalize publish: {0}"), baseMsg);
				if (finalRes.MissingHashes != null && finalRes.MissingHashes.Count > 0)
				{
					string firstMissingHash = finalRes.MissingHashes[0];
					string firstMissingName = hashToRelativePath.TryGetValue(firstMissingHash, out var rel) || hashToRelativePath.TryGetValue(ContentAddressableStorage.NormalizeBlake3Hash(firstMissingHash), out rel)
						? rel
						: firstMissingHash;
					errorText += "\n" + string.Format(TranslationServer.Translate("Missing asset: {0}"), firstMissingName);
				}
				statusLabel.Text = errorText;
				statusLabel.AddThemeColorOverride("font_color", new Color(0.95f, 0.3f, 0.3f));
				closeBtn.Visible = true;
				ShowFeedback(errorText.Replace("❌ ", ""));
			}
		}
		catch (Exception ex)
		{
			SetWasmConsoleStatus($"❌ Publish Failed: {ex.Message}", new Color(1.0f, 0.3f, 0.3f));
			if (GodotObject.IsInstanceValid(progressBar) && GodotObject.IsInstanceValid(statusLabel) && GodotObject.IsInstanceValid(closeBtn))
			{
				progressBar.Value = 100;
				statusLabel.Text = "❌ " + string.Format(TranslationServer.Translate("Publish error: {0}"), ex.Message);
				statusLabel.AddThemeColorOverride("font_color", new Color(0.95f, 0.3f, 0.3f));
				closeBtn.Visible = true;
			}
			ShowFeedback(string.Format(TranslationServer.Translate("Publish error: {0}"), ex.Message));
		}
		finally
		{
			Realm.Godot.WasmRuntime.OnWasmLog -= logHandler;
			if (_editorService != null)
			{
				_editorService.IsPaused = false;
			}
			_lastMetadataSyncTime = GetLastWriteTimeSafe(activeConfigPath);
			_lastTerrainSyncTime = GetMaxTerrainWriteTime(tempTerrainPath);
			_isSyncing = false;
		}
	}

	public async Task ShowAssetIndexRepairModalAsync(Action onCompleted = null)
	{
		var overlay = new ColorRect();
		overlay.Name = "AssetIndexRepairOverlay";
		overlay.Color = new Color(0, 0, 0, 0.75f);
		overlay.SetAnchorsPreset(LayoutPreset.FullRect);
		overlay.MouseFilter = Control.MouseFilterEnum.Stop;
		overlay.ZIndex = 1100;
		AddChild(overlay);

		var center = new CenterContainer();
		center.SetAnchorsPreset(LayoutPreset.FullRect);
		overlay.AddChild(center);

		var panel = new PanelContainer();
		panel.AddThemeStyleboxOverride("panel", UIStyle.CreateStonePanel(true));
		panel.CustomMinimumSize = new Vector2(520, 220);
		center.AddChild(panel);

		var vbox = new VBoxContainer();
		vbox.AddThemeConstantOverride("separation", 12);
		panel.AddChild(vbox);

		var titleLabel = new Label();
		UIStyle.ApplyTitle(titleLabel, "🗄️ " + TranslationServer.Translate("REPAIRING ASSET INDEX"), 20);
		titleLabel.HorizontalAlignment = HorizontalAlignment.Center;
		vbox.AddChild(titleLabel);

		var descLabel = new Label();
		descLabel.Text = TranslationServer.Translate("Rebuilding asset index database from CAS manifests...");
		descLabel.HorizontalAlignment = HorizontalAlignment.Center;
		descLabel.AddThemeColorOverride("font_color", UIStyle.ColorGold);
		descLabel.AddThemeFontSizeOverride("font_size", 13);
		vbox.AddChild(descLabel);

		var progressBar = new ProgressBar();
		progressBar.CustomMinimumSize = new Vector2(460, 22);
		progressBar.MinValue = 0;
		progressBar.MaxValue = 100;
		progressBar.Value = 0;
		vbox.AddChild(progressBar);

		var statusLabel = new Label();
		statusLabel.Text = TranslationServer.Translate("Initializing asset database rebuild...");
		statusLabel.HorizontalAlignment = HorizontalAlignment.Center;
		statusLabel.AddThemeFontSizeOverride("font_size", 13);
		statusLabel.AddThemeColorOverride("font_color", new Color(0.9f, 0.9f, 0.95f));
		vbox.AddChild(statusLabel);

		await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);

		var progress = new Progress<AssetIndexProgressUpdate>(update =>
		{
			Callable.From(() =>
			{
				if (GodotObject.IsInstanceValid(progressBar))
				{
					progressBar.Value = Mathf.Clamp(update.ProgressPercentage * 100.0, 0, 100);
				}
				if (GodotObject.IsInstanceValid(statusLabel))
				{
					statusLabel.Text = update.Message;
				}
			}).CallDeferred();
		});

		try
		{
			await Task.Run(() => AssetIndexService.Instance.RebuildIndexFromCas(progress));
		}
		catch (Exception ex)
		{
			GD.PrintErr($"[MapEditorHUD] AssetIndex rebuild error: {ex.Message}");
		}

		if (GodotObject.IsInstanceValid(progressBar))
		{
			progressBar.Value = 100;
		}
		if (GodotObject.IsInstanceValid(statusLabel))
		{
			statusLabel.Text = TranslationServer.Translate("Asset index repair complete!");
			statusLabel.AddThemeColorOverride("font_color", new Color(0.3f, 0.9f, 0.3f));
		}

		await ToSignal(GetTree().CreateTimer(0.6f), SceneTreeTimer.SignalName.Timeout);

		if (GodotObject.IsInstanceValid(overlay))
		{
			overlay.QueueFree();
		}

		ShowFeedback(TranslationServer.Translate("Asset index database successfully rebuilt from CAS."));
		onCompleted?.Invoke();
	}

	public void ShowConfirmationDialog(string message, Action onConfirm, string confirmText = "YES", string cancelText = "NO", Action onCancel = null, Action onDismissed = null, bool showCancel = true)
	{
		var overlay = new ColorRect();
		overlay.Name = "ConfirmationOverlay";
		overlay.Color = new Color(0, 0, 0, 0.65f);
		overlay.SetAnchorsPreset(LayoutPreset.FullRect);
		overlay.MouseFilter = Control.MouseFilterEnum.Stop;
		overlay.ZIndex = 1100;
		if (onDismissed != null)
		{
			overlay.TreeExited += () => onDismissed();
		}
		AddChild(overlay);

		var center = new CenterContainer();
		center.SetAnchorsPreset(LayoutPreset.FullRect);
		overlay.AddChild(center);

		var panel = new PanelContainer();
		panel.AddThemeStyleboxOverride("panel", new StyleBoxEmpty());
		panel.CustomMinimumSize = new Vector2(520, 266);
		center.AddChild(panel);

		var bgTex = new TextureRect();
		bgTex.Texture = GD.Load<Texture2D>("res://Assets/UI/map_editor_test.png");
		bgTex.ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize;
		bgTex.StretchMode = TextureRect.StretchModeEnum.Scale;
		bgTex.SetAnchorsPreset(LayoutPreset.FullRect);
		panel.AddChild(bgTex);

		var margin = new MarginContainer();
		margin.SetAnchorsPreset(LayoutPreset.FullRect);
		margin.AddThemeConstantOverride("margin_top", 30);
		margin.AddThemeConstantOverride("margin_bottom", 25);
		margin.AddThemeConstantOverride("margin_left", 45);
		margin.AddThemeConstantOverride("margin_right", 45);
		panel.AddChild(margin);

		var vbox = new VBoxContainer();
		vbox.AddThemeConstantOverride("separation", 14);
		vbox.Alignment = BoxContainer.AlignmentMode.Center;
		margin.AddChild(vbox);

		var titleMargin = new MarginContainer();
		titleMargin.AddThemeConstantOverride("margin_top", -12);

		var lblTitle = new Label();
		UIStyle.ApplyTitle(lblTitle, TranslationServer.Translate(showCancel ? "CONFIRMATION REQUIRED" : "NOTIFICATION"), 18);
		lblTitle.HorizontalAlignment = HorizontalAlignment.Center;
		lblTitle.AddThemeColorOverride("font_color", UIStyle.ColorGold);
		titleMargin.AddChild(lblTitle);
		vbox.AddChild(titleMargin);

		var msgMargin = new MarginContainer();
		msgMargin.AddThemeConstantOverride("margin_top", 12);
		msgMargin.AddThemeConstantOverride("margin_bottom", 0);
		msgMargin.SizeFlagsVertical = SizeFlags.ExpandFill;

		var lblMsg = new Label();
		lblMsg.Text = TranslationServer.Translate(message);
		lblMsg.AutowrapMode = TextServer.AutowrapMode.WordSmart;
		lblMsg.HorizontalAlignment = HorizontalAlignment.Center;
		lblMsg.AddThemeColorOverride("font_color", new Color(0.9f, 0.9f, 0.95f));
		lblMsg.AddThemeFontSizeOverride("font_size", 13);
		lblMsg.SizeFlagsVertical = SizeFlags.ExpandFill;
		msgMargin.AddChild(lblMsg);
		vbox.AddChild(msgMargin);

		var hbox = new HBoxContainer();
		hbox.AddThemeConstantOverride("separation", 24);
		hbox.SizeFlagsHorizontal = SizeFlags.ShrinkCenter;
		vbox.AddChild(hbox);

		var btnConfirm = new Button();
		btnConfirm.Set("icon_max_width", 0);
		btnConfirm.CustomMinimumSize = new Vector2(110, 34);
		btnConfirm.MouseFilter = Control.MouseFilterEnum.Stop;
		SetupButton(btnConfirm, TranslationServer.Translate(confirmText), () =>
		{
			overlay.QueueFree();
			onConfirm?.Invoke();
		}, 13);
		btnConfirm.AddThemeColorOverride("font_color", UIStyle.ColorCyanGlow);
		hbox.AddChild(btnConfirm);

		if (showCancel)
		{
			var btnCancel = new Button();
			btnCancel.Set("icon_max_width", 0);
			btnCancel.CustomMinimumSize = new Vector2(110, 34);
			btnCancel.MouseFilter = Control.MouseFilterEnum.Stop;
			Action cancelAction = () =>
			{
				overlay.QueueFree();
				onCancel?.Invoke();
			};
			overlay.SetMeta("CancelAction", Callable.From(cancelAction));
			SetupButton(btnCancel, TranslationServer.Translate(cancelText), () =>
			{
				cancelAction();
			}, 13);
			btnCancel.AddThemeColorOverride("font_color", new Color(0.9f, 0.3f, 0.3f));
			hbox.AddChild(btnCancel);
		}
	}

	private void ShowWasmConsoleModal()
	{
		Realm.Godot.UI.WasmConsoleWindow.Instance.ClearLogs();
		Realm.Godot.UI.WasmConsoleWindow.Instance.ShowConsole();
	}

	public void AppendWasmConsoleLog(string line)
	{
		Realm.Godot.UI.WasmConsoleWindow.Instance.AppendLog(line);
		GD.Print("[WASM_BUILD] " + line);
	}

	public void SetWasmConsoleStatus(string statusText, Color color)
	{
		Realm.Godot.UI.WasmConsoleWindow.Instance.SetStatus(statusText, color);
	}

	public void CloseWasmConsoleModal()
	{
		// WasmConsoleWindow remains open as a persistent window across scene transitions.
	}

	private ColorRect _helpOverlayPanel = null;

	public void ToggleHelpPanelExternal()
	{
		if (GodotObject.IsInstanceValid(_helpOverlayPanel))
		{
			_helpOverlayPanel.QueueFree();
			_helpOverlayPanel = null;
			return;
		}

		_helpOverlayPanel = new ColorRect();
		_helpOverlayPanel.Name = "HelpOverlayPanel";
		_helpOverlayPanel.Color = new Color(0, 0, 0, 0.65f);
		_helpOverlayPanel.SetAnchorsPreset(LayoutPreset.FullRect);
		_helpOverlayPanel.MouseFilter = Control.MouseFilterEnum.Stop;
		_helpOverlayPanel.ZIndex = 1000;
		AddChild(_helpOverlayPanel);

		var center = new CenterContainer();
		center.SetAnchorsPreset(LayoutPreset.FullRect);
		_helpOverlayPanel.AddChild(center);

		var panel = new PanelContainer();
		panel.AddThemeStyleboxOverride("panel", new StyleBoxEmpty());
		panel.CustomMinimumSize = new Vector2(1024, 770);
		center.AddChild(panel);

		var bgTex = new TextureRect();
		bgTex.Texture = GD.Load<Texture2D>("res://Assets/UI/map_editor_manual.png");
		bgTex.ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize;
		bgTex.StretchMode = TextureRect.StretchModeEnum.Scale;
		bgTex.SetAnchorsPreset(LayoutPreset.FullRect);
		panel.AddChild(bgTex);

		var margin = new MarginContainer();
		margin.SetAnchorsPreset(LayoutPreset.FullRect);
		margin.AddThemeConstantOverride("margin_top", 45);
		margin.AddThemeConstantOverride("margin_bottom", 75);
		margin.AddThemeConstantOverride("margin_left", 65);
		margin.AddThemeConstantOverride("margin_right", 65);
		panel.AddChild(margin);

		var vbox = new VBoxContainer();
		vbox.AddThemeConstantOverride("separation", 15);
		margin.AddChild(vbox);

		var titleMargin = new MarginContainer();
		titleMargin.AddThemeConstantOverride("margin_top", -20);
		titleMargin.AddThemeConstantOverride("margin_bottom", 0);

		var lblTitle = new Label();
		UIStyle.ApplyTitle(lblTitle, TranslationServer.Translate("RTS MAP EDITOR REFERENCE MANUAL"), 18);
		lblTitle.HorizontalAlignment = HorizontalAlignment.Center;
		lblTitle.AddThemeColorOverride("font_color", UIStyle.ColorGold);
		titleMargin.AddChild(lblTitle);
		vbox.AddChild(titleMargin);

		var scroll = new ScrollContainer();
		scroll.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
		scroll.SizeFlagsVertical = Control.SizeFlags.ExpandFill;
		scroll.HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled;
		scroll.VerticalScrollMode = ScrollContainer.ScrollMode.Auto;
		vbox.AddChild(scroll);

		var grid = new GridContainer();
		grid.Columns = 2;
		grid.AddThemeConstantOverride("h_separation", 30);
		grid.AddThemeConstantOverride("v_separation", 10);
		grid.SizeFlagsHorizontal = SizeFlags.ExpandFill;
		scroll.AddChild(grid);

		AddHelpSectionHeader(grid, TranslationServer.Translate("PRIMARY MODULE SWITCHING"));
		AddHelpShortcutRow(grid, "F1", TranslationServer.Translate("Terrain Module"));
		AddHelpShortcutRow(grid, "F2", TranslationServer.Translate("Texture Module"));
		AddHelpShortcutRow(grid, "F3", TranslationServer.Translate("Pathing Module"));
		AddHelpShortcutRow(grid, "F4", TranslationServer.Translate("Objects Module"));
		AddHelpShortcutRow(grid, "F5", TranslationServer.Translate("Coordinates Module"));
		AddHelpShortcutRow(grid, "F6", TranslationServer.Translate("Clipboard Module"));

		AddHelpSectionHeader(grid, TranslationServer.Translate("CAMERA CONTROLS"));
		AddHelpShortcutRow(grid, "Arrows", TranslationServer.Translate("Pan map camera"));
		AddHelpShortcutRow(grid, "Mouse Scroll", TranslationServer.Translate("Zoom camera in / out"));
		AddHelpShortcutRow(grid, "Middle Mouse Drag", TranslationServer.Translate("Pan camera by dragging"));
		AddHelpShortcutRow(grid, "Shift + Middle Drag", TranslationServer.Translate("Rotate map camera view"));
		AddHelpShortcutRow(grid, "Comma (,) / Period (.)", TranslationServer.Translate("Rotate camera 90 degrees"));
		AddHelpShortcutRow(grid, "F8", TranslationServer.Translate("Toggle Free Camera"));
		AddHelpShortcutRow(grid, "WASD / QE", TranslationServer.Translate("Fly camera in Free Camera mode (RMB drag to look)"));

		AddHelpSectionHeader(grid, TranslationServer.Translate("EDITOR TOOLS"));
		AddHelpShortcutRow(grid, "1", TranslationServer.Translate("Raise Terrain Tool"));
		AddHelpShortcutRow(grid, "2", TranslationServer.Translate("Lower Terrain Tool"));
		AddHelpShortcutRow(grid, "3", TranslationServer.Translate("Smooth Terrain Tool"));
		AddHelpShortcutRow(grid, "4", TranslationServer.Translate("Flatten Terrain Tool"));
		AddHelpShortcutRow(grid, "5", TranslationServer.Translate("Plateau Terrain Tool"));
		AddHelpShortcutRow(grid, "6", TranslationServer.Translate("Ramp Tool"));
		AddHelpShortcutRow(grid, "7", TranslationServer.Translate("Roughen (Noise) Tool"));
		AddHelpShortcutRow(grid, "8", TranslationServer.Translate("Texture Painter Brush"));
		AddHelpShortcutRow(grid, "9", TranslationServer.Translate("Add Objects"));
		AddHelpShortcutRow(grid, "Q", TranslationServer.Translate("Select / Move Tool"));
		AddHelpShortcutRow(grid, "I", TranslationServer.Translate("Eyedropper Picker"));

		AddHelpSectionHeader(grid, TranslationServer.Translate("SCULPTING / PLACEMENT SETTINGS"));
		AddHelpShortcutRow(grid, "[ / ]", TranslationServer.Translate("Increase / decrease brush size"));
		AddHelpShortcutRow(grid, "- / =", TranslationServer.Translate("Increase / decrease brush strength"));
		AddHelpShortcutRow(grid, "Shift + MouseWheel Scroll", TranslationServer.Translate("Quickly change brush size"));
		AddHelpShortcutRow(grid, "Ctrl + MouseWheel Scroll", TranslationServer.Translate("Quickly change brush strength"));
		AddHelpShortcutRow(grid, "B Key", TranslationServer.Translate("Toggle brush shape (Circle / Square)"));
		AddHelpShortcutRow(grid, "V Key", TranslationServer.Translate("Toggle terrain alignment grid lines"));
		AddHelpShortcutRow(grid, "M Key", TranslationServer.Translate("Toggle blocky sculpt mode"));
		AddHelpShortcutRow(grid, "Shift + MouseWheel Scroll", TranslationServer.Translate("Rotate placement/selected object"));
		AddHelpShortcutRow(grid, "Alt + MouseWheel Scroll", TranslationServer.Translate("Fine-tune object scale size"));
		AddHelpShortcutRow(grid, "Ctrl + G", TranslationServer.Translate("Toggle alignment grid snap placement"));
		AddHelpShortcutRow(grid, "Ctrl + D", TranslationServer.Translate("Duplicate / clone selected object"));
		AddHelpShortcutRow(grid, "Delete", TranslationServer.Translate("Delete / erase selected object"));

		AddHelpSectionHeader(grid, TranslationServer.Translate("GENERAL OPERATIONS"));
		AddHelpShortcutRow(grid, "Ctrl + Z / Ctrl + Y", TranslationServer.Translate("Undo / Redo editor actions"));
		AddHelpShortcutRow(grid, "Ctrl + S / Ctrl + O", TranslationServer.Translate("Save Map File / Load Map File"));
		AddHelpShortcutRow(grid, "Escape Key", TranslationServer.Translate("Close open dialog"));

		var bottomMargin = new MarginContainer();
		bottomMargin.SetAnchorsPreset(LayoutPreset.FullRect);
		bottomMargin.AddThemeConstantOverride("margin_bottom", 20);
		bottomMargin.AddThemeConstantOverride("margin_right", 16);
		panel.AddChild(bottomMargin);

		var btnClose = new Button();
		btnClose.Set("icon_max_width", 0);
		btnClose.SizeFlagsHorizontal = Control.SizeFlags.ShrinkCenter;
		btnClose.SizeFlagsVertical = Control.SizeFlags.ShrinkEnd;
		btnClose.CustomMinimumSize = new Vector2(165, 38);
		SetupButton(btnClose, TranslationServer.Translate("CLOSE MANUAL"), () =>
		{
			_helpOverlayPanel.QueueFree();
			_helpOverlayPanel = null;
		}, 13);
		bottomMargin.AddChild(btnClose);
	}

	private void AddHelpSectionHeader(GridContainer grid, string title)
	{
		var lbl = new Label();
		lbl.Text = title;
		lbl.AddThemeColorOverride("font_color", UIStyle.ColorBronze);
		lbl.AddThemeFontSizeOverride("font_size", 12);
		grid.AddChild(lbl);

		var empty = new Control();
		grid.AddChild(empty);
	}

	private void AddHelpShortcutRow(GridContainer grid, string keys, string action)
	{
		var lblKeys = new Label();
		lblKeys.Text = keys;
		lblKeys.AddThemeColorOverride("font_color", UIStyle.ColorCyanGlow);
		lblKeys.AddThemeFontSizeOverride("font_size", 11);
		grid.AddChild(lblKeys);

		var lblAction = new Label();
		lblAction.Text = action;
		lblAction.AddThemeColorOverride("font_color", new Color(0.85f, 0.85f, 0.9f));
		lblAction.AddThemeFontSizeOverride("font_size", 11);
		grid.AddChild(lblAction);
	}



	
	private void SaveMapProperties()
	{
		if (GameHost.Instance == null) return;
		
		_mapSettingsDialog?.SaveMapProperties();
	}

	public void LoadMapProperties()
	{
		RuntimeTerrain.Instance?.ReloadWaterProfiles();
		RefreshWaterSwatches();
		_mapSettingsDialog?.LoadMapProperties();
	}

	private async System.Threading.Tasks.Task CompileAndSignMapAsync(string workspace, bool skipAttribution = true)
	{
		_wasmHasErrors = false;
		// 1. Compile triggers
		try
		{
			if (System.IO.Directory.Exists(workspace))
			{
				MapWorkspaceService.EnsureWitFile(workspace);
				MapWorkspaceService.EnsureWasmEntryPoint(workspace);
				MapWorkspaceService.EnsureCsproj(workspace, System.IO.Path.GetFileName(workspace));

				var csprojFiles = System.IO.Directory.GetFiles(workspace, "*.csproj", System.IO.SearchOption.TopDirectoryOnly);
				if (csprojFiles.Length == 0)
				{
					_wasmHasErrors = true;
					var errorMessage = "[MapEditorHUD] ERROR: No .csproj found in workspace, cannot compile map script";
					SetWasmConsoleStatus("❌ " + errorMessage, new Color(1.0f, 0.3f, 0.3f));
					AppendWasmConsoleLog(errorMessage);
					GD.PrintErr(errorMessage);
					return;					
				}

				string csproj = csprojFiles.FirstOrDefault(f => System.IO.Path.GetFileName(f).Equals("MapScript.csproj", System.StringComparison.OrdinalIgnoreCase)) ?? csprojFiles[0];

				// Check if WASM binary already exists and no .cs files have been modified since it was built
				string binDir = System.IO.Path.Combine(workspace, "bin");
				string existingWasm = null;
				if (System.IO.Directory.Exists(binDir))
				{
					var wasmFiles = System.IO.Directory.GetFiles(
						binDir,
						"*.wasm",
						System.IO.SearchOption.AllDirectories
					).Where(f => !f.Contains("native") && !f.Contains("obj")).ToList();

					existingWasm = wasmFiles.FirstOrDefault(f => f.Contains("publish"))
						?? wasmFiles.OrderByDescending(f => System.IO.File.GetLastWriteTimeUtc(f)).FirstOrDefault();
				}

				if (string.IsNullOrEmpty(existingWasm) && !string.IsNullOrEmpty(_currentSourceFolder) && System.IO.Directory.Exists(_currentSourceFolder))
				{
					string sourceBinDir = System.IO.Path.Combine(_currentSourceFolder, "bin");
					if (System.IO.Directory.Exists(sourceBinDir))
					{
						var wasmFiles = System.IO.Directory.GetFiles(
							sourceBinDir,
							"*.wasm",
							System.IO.SearchOption.AllDirectories
						).Where(f => !f.Contains("native") && !f.Contains("obj")).ToList();

						existingWasm = wasmFiles.FirstOrDefault(f => f.Contains("publish"))
							?? wasmFiles.OrderByDescending(f => System.IO.File.GetLastWriteTimeUtc(f)).FirstOrDefault();
					}
				}

				if (!string.IsNullOrEmpty(existingWasm) && System.IO.File.Exists(existingWasm))
				{
					DateTime wasmTime = System.IO.File.GetLastWriteTimeUtc(existingWasm);
					var sourceDirs = new System.Collections.Generic.List<string> { workspace };
					if (!string.IsNullOrEmpty(_currentSourceFolder) && System.IO.Directory.Exists(_currentSourceFolder) && !_currentSourceFolder.Equals(workspace, System.StringComparison.OrdinalIgnoreCase))
					{
						sourceDirs.Add(_currentSourceFolder);
					}

					bool hasNewerCsFile = false;
					foreach (var dir in sourceDirs)
					{
						var dependencyFiles = System.IO.Directory.GetFiles(dir, "*.cs", System.IO.SearchOption.AllDirectories)
							.Where(f => {
								string rel = f.Substring(dir.Length).TrimStart(System.IO.Path.DirectorySeparatorChar, System.IO.Path.AltDirectorySeparatorChar);
								return !rel.StartsWith("bin", System.StringComparison.OrdinalIgnoreCase) && !rel.StartsWith("obj", System.StringComparison.OrdinalIgnoreCase);
							})
							.Concat(System.IO.Directory.GetFiles(dir, "*.csproj", System.IO.SearchOption.TopDirectoryOnly))
							.Concat(System.IO.Directory.GetFiles(dir, "metadata.json", System.IO.SearchOption.TopDirectoryOnly))
							.Concat(System.IO.Directory.Exists(System.IO.Path.Combine(dir, "lib"))
								? System.IO.Directory.GetFiles(System.IO.Path.Combine(dir, "lib"), "*.dll", System.IO.SearchOption.TopDirectoryOnly)
								: System.Array.Empty<string>())
							.Concat(System.IO.Directory.Exists(System.IO.Path.Combine(dir, "wit"))
								? System.IO.Directory.GetFiles(System.IO.Path.Combine(dir, "wit"), "*.wit", System.IO.SearchOption.TopDirectoryOnly)
								: System.Array.Empty<string>());

						if (dependencyFiles.Any(f => System.IO.File.GetLastWriteTimeUtc(f) > wasmTime))
						{
							hasNewerCsFile = true;
							break;
						}
					}

					if (!hasNewerCsFile)
					{
						string targetWasmInTemp = System.IO.Path.Combine(workspace, "bin", System.IO.Path.GetFileName(existingWasm));
						if (!System.IO.File.Exists(targetWasmInTemp))
						{
							System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(targetWasmInTemp));
							System.IO.File.Copy(existingWasm, targetWasmInTemp, true);
						}

						_wasmHasErrors = false;
						SetWasmConsoleStatus("✓ WASM Compilation Bypassed (Unchanged)", UIStyle.ColorCyanGlow);
						AppendWasmConsoleLog("[INFO] .cs files unchanged since last build. Bypassing compilation using existing WASM binary.");
						if (skipAttribution) return;
					}
				}

				string mapApiDll = System.IO.Path.Combine(workspace, "lib", "Realm.MapAPI.dll");
				if (!System.IO.File.Exists(mapApiDll))
				{
					_wasmHasErrors = true;
					SetWasmConsoleStatus("❌ WASM Compilation Failed: Realm.MapAPI.dll missing", new Color(1.0f, 0.3f, 0.3f));
					AppendWasmConsoleLog("[ERROR] Realm.MapAPI.dll is missing from the workspace (expected at lib/Realm.MapAPI.dll).");
					AppendWasmConsoleLog("[ERROR] The map script cannot compile without the MapAPI assembly. Reopen the map in the editor to restore template files, then retry Test.");
					return;
				}

				await System.Threading.Tasks.Task.Run(() =>
				{
					bool streamHadErrors = false;
					string resolvedWasiSdk = WasiSdkResolver.ResolveWasiSdkPath();
					string csprojName = System.IO.Path.GetFileName(csproj);
					var compileProcess = new System.Diagnostics.Process();
					compileProcess.StartInfo.FileName = "dotnet";
					compileProcess.StartInfo.Arguments = $"publish \"{csprojName}\" -c Release -r wasi-wasm -p:WASI_SDK_PATH=\"{resolvedWasiSdk}\"";
					compileProcess.StartInfo.EnvironmentVariables["WASI_SDK_PATH"] = resolvedWasiSdk;
					compileProcess.StartInfo.WorkingDirectory = workspace;
					compileProcess.StartInfo.CreateNoWindow = true;
					compileProcess.StartInfo.UseShellExecute = false;
					compileProcess.StartInfo.RedirectStandardOutput = true;
					compileProcess.StartInfo.RedirectStandardError = true;

					compileProcess.OutputDataReceived += (s, e) =>
					{
						if (!string.IsNullOrEmpty(e.Data))
						{
							if (e.Data.Contains(": error ") || e.Data.Contains("Build FAILED"))
							{
								streamHadErrors = true;
							}
							AppendWasmConsoleLog(e.Data);
						}
					};

					compileProcess.ErrorDataReceived += (s, e) =>
					{
						if (!string.IsNullOrEmpty(e.Data))
						{
							if (e.Data.Contains(": error ") || e.Data.Contains("Build FAILED"))
							{
								streamHadErrors = true;
								AppendWasmConsoleLog("[COMPILER ERROR] " + e.Data);
							}
							else if (e.Data.Contains(": warning "))
							{
								AppendWasmConsoleLog("[COMPILER WARNING] " + e.Data);
							}
							else
							{
								AppendWasmConsoleLog(e.Data);
							}
						}
					};

					compileProcess.Start();
					compileProcess.BeginOutputReadLine();
					compileProcess.BeginErrorReadLine();
					compileProcess.WaitForExit();

					if (compileProcess.ExitCode != 0 || streamHadErrors)
					{
						_wasmHasErrors = true;
						SetWasmConsoleStatus($"❌ WASM Compilation Failed (exit code {compileProcess.ExitCode})", new Color(1.0f, 0.3f, 0.3f));
						AppendWasmConsoleLog($"[ERROR] dotnet publish failed with exit code {compileProcess.ExitCode}");
						GD.PrintErr($"[MapEditorHUD] Map script compilation failed (exit code {compileProcess.ExitCode})");
					}
					else
					{
						_wasmHasErrors = false;
						SetWasmConsoleStatus("✓ WASM Compilation Succeeded", UIStyle.ColorCyanGlow);
						AppendWasmConsoleLog("[SUCCESS] WASM compilation complete (exit code 0).");
					}
				});
			}
		}
		catch (Exception ex)
		{
			_wasmHasErrors = true;
			SetWasmConsoleStatus($"❌ WASM Compilation Failed: {ex.Message}", new Color(1.0f, 0.3f, 0.3f));
			AppendWasmConsoleLog($"[COMPILER EXCEPTION] {ex}");
			GD.PrintErr($"[MapEditorHUD] Trigger compilation failed: {ex.Message}");
		}

		if (skipAttribution) return;

		// 2. Resolve contributors, attributions, and sign
		try
		{
			var authorshipKey = GetOrGenerateAuthorshipKey();
			string currentUsername = "MapAuthor";
			string pubKeyStr = Convert.ToBase64String(authorshipKey.PublicKey.Export(KeyBlobFormat.RawPublicKey));
			
			string seedServerUrl = GameHost.Instance != null && GodotObject.IsInstanceValid(LobbyManager.Instance) ? LobbyManager.Instance.RegistryServerUrl : ServersConfigHelper.GetDefaultServerUrl();
			
			using (var httpClient = new System.Net.Http.HttpClient())
			{
				try
				{
					var resTask = httpClient.GetAsync(seedServerUrl + "/api/creators/check/" + Uri.EscapeDataString(pubKeyStr));
					resTask.Wait();
					var res = resTask.Result;
					if (res.IsSuccessStatusCode)
					{
						var jsonTask = res.Content.ReadAsStringAsync();
						jsonTask.Wait();
						using var creatorDoc = JsonDocument.Parse(jsonTask.Result);
						if (creatorDoc.RootElement.TryGetProperty("username", out var uProp))
						{
							currentUsername = uProp.GetString() ?? currentUsername;
						}
					}
				}
				catch {}

				var referencedHashes = new List<string>();
				var allFiles = System.IO.Directory.GetFiles(workspace, "*", System.IO.SearchOption.AllDirectories);
				
				foreach (var file in allFiles)
				{
					if (file.EndsWith("metadata.json") || file.EndsWith("manifest.json") || file.EndsWith("authorship_key.pem") || file.EndsWith("authorship_key_DO-NOT-SHARE.rkey") || file.EndsWith(".rkey")) continue;
					
					byte[] fileBytes = System.IO.File.ReadAllBytes(file);
					string ext = System.IO.Path.GetExtension(file).ToLowerInvariant();
					string blake3 = RealmMetadataHelper.ComputeBlake3(fileBytes, ext);
					string hash = string.IsNullOrEmpty(ext) ? blake3 : $"{blake3}{ext}";
					
					byte[] hashBytes = System.Text.Encoding.UTF8.GetBytes(hash);
					byte[] signatureBytes = SignatureAlgorithm.Ed25519.Sign(authorshipKey, hashBytes);
					string signatureStr = Convert.ToBase64String(signatureBytes);
					
					referencedHashes.Add(hash);
					
					try
					{
						var existsResTask = httpClient.GetAsync(seedServerUrl + "/api/publish_map/asset_author/" + hash);
						existsResTask.Wait();
						var existsRes = existsResTask.Result;
						if (!existsRes.IsSuccessStatusCode)
						{
							using var form = new System.Net.Http.MultipartFormDataContent();
							form.Add(new System.Net.Http.StringContent(hash), "Hash");
							form.Add(new System.Net.Http.StringContent(signatureStr), "Signature");
							form.Add(new System.Net.Http.StringContent(currentUsername), "AuthorUsername");
							form.Add(new System.Net.Http.StringContent(pubKeyStr), "PublicKey");
							
							var fileContent = new System.Net.Http.ByteArrayContent(fileBytes);
							form.Add(fileContent, "File", System.IO.Path.GetFileName(file));
							
							var uploadTask = httpClient.PostAsync(seedServerUrl + "/api/publish_map/upload_asset", form);
							uploadTask.Wait();
						}
					}
					catch {}
				}
				
				string metadataJsonPath = System.IO.Path.Combine(workspace, "metadata.json");
				if (System.IO.File.Exists(metadataJsonPath))
				{
					string metadataJsonContent = System.IO.File.ReadAllText(metadataJsonPath);
					var options = new JsonSerializerOptions { WriteIndented = true };
					var mapDoc = JsonNode.Parse(metadataJsonContent) as JsonObject;
					
					if (mapDoc != null)
					{
						var contributorsList = new HashSet<string>();
						if (mapDoc.TryGetPropertyValue("Contributors", out var contNode) && contNode is JsonArray arr)
						{
							foreach (var node in arr)
							{
								if (node != null) contributorsList.Add(node.GetValue<string>());
							}
						}
						
						var authorCounts = new System.Collections.Generic.Dictionary<string, int>();
						foreach (var hash in referencedHashes)
						{
							try
							{
								var assetAuthorResTask = httpClient.GetAsync(seedServerUrl + "/api/publish_map/asset_author/" + hash);
								assetAuthorResTask.Wait();
								var assetAuthorRes = assetAuthorResTask.Result;
								if (assetAuthorRes.IsSuccessStatusCode)
								{
									var assetAuthorJsonTask = assetAuthorRes.Content.ReadAsStringAsync();
									assetAuthorJsonTask.Wait();
									var assetMeta = JsonNode.Parse(assetAuthorJsonTask.Result);
									if (assetMeta != null && assetMeta["AuthorUsername"] != null)
									{
										string author = assetMeta["AuthorUsername"].GetValue<string>();
										if (!string.IsNullOrEmpty(author))
										{
											contributorsList.Add(author);
											if (!authorCounts.ContainsKey(author)) authorCounts[author] = 0;
											authorCounts[author]++;
										}
									}
								}
							}
							catch {}
						}
						
						contributorsList.Add(currentUsername);
						
						var newContributorsArr = new JsonArray();
						foreach (var cont in contributorsList)
						{
							newContributorsArr.Add(cont);
						}
						mapDoc["Contributors"] = newContributorsArr;

						var attributionsArr = new JsonArray();
						var sortedAuthors = authorCounts.Keys.ToList();
						sortedAuthors.Sort((a, b) => authorCounts[b].CompareTo(authorCounts[a]));
						foreach (var a in sortedAuthors)
						{
							attributionsArr.Add(a);
						}
						mapDoc["Attributions"] = attributionsArr;
						mapDoc["EngineVersion"] = RealmVersion.GameBinaryVersion;
						
						mapDoc["author_key"] = pubKeyStr;
						if (mapDoc.ContainsKey("signature"))
						{
							mapDoc.Remove("signature");
						}
						
						string updatedMetadataJson = mapDoc.ToJsonString(options);
						System.IO.File.WriteAllText(metadataJsonPath, updatedMetadataJson);
						
						byte[] mapBytes = System.IO.File.ReadAllBytes(metadataJsonPath);
						string mapBlake3 = RealmMetadataHelper.ComputeBlake3(mapBytes, ".json");
						string mapHash = $"{mapBlake3}.json";
						byte[] mapHashBytes = System.Text.Encoding.UTF8.GetBytes(mapHash);
						byte[] mapSigBytes = SignatureAlgorithm.Ed25519.Sign(authorshipKey, mapHashBytes);
						
						mapDoc["signature"] = Convert.ToBase64String(mapSigBytes);
						updatedMetadataJson = mapDoc.ToJsonString(options);
						System.IO.File.WriteAllText(metadataJsonPath, updatedMetadataJson);
						
						try
						{
							using (var form = new System.Net.Http.MultipartFormDataContent())
							{
								form.Add(new System.Net.Http.StringContent(mapHash), "Hash");
								form.Add(new System.Net.Http.StringContent(Convert.ToBase64String(mapSigBytes)), "Signature");
								form.Add(new System.Net.Http.StringContent(currentUsername), "AuthorUsername");
								form.Add(new System.Net.Http.StringContent(pubKeyStr), "PublicKey");
								
								var fileContent = new System.Net.Http.ByteArrayContent(mapBytes);
								form.Add(fileContent, "File", "metadata.json");
								
								var uploadMapTask = httpClient.PostAsync(seedServerUrl + "/api/publish_map/upload_asset", form);
								uploadMapTask.Wait();
							}
							
							var publishReq = new 
							{
								MapJson = updatedMetadataJson,
								ReferencedHashes = referencedHashes,
								Signature = Convert.ToBase64String(mapSigBytes),
								PublicKey = pubKeyStr
							};
							
							var pubContent = new StringContent(JsonSerializer.Serialize(publishReq), System.Text.Encoding.UTF8, "application/json");
							var pubTask = httpClient.PostAsync(seedServerUrl + "/api/publish_map", pubContent);
							pubTask.Wait();
						}
						catch {}
					}
				}
			}
		}
		catch (Exception ex)
		{
			GD.PrintErr($"[MapEditorHUD] Error signing/compiling map: {ex.Message}");
		}
	}

	private async void CheckCreatorRegistrationAndPrompt()
	{
		if (GetNodeOrNull("AgreementOverlay") != null || GetNodeOrNull("CreatorRegistrationOverlay") != null)
		{
			return;
		}

		var key = GetOrGenerateAuthorshipKey();
		string pubKeyStr = Convert.ToBase64String(key.PublicKey.Export(KeyBlobFormat.RawPublicKey));
		
		string seedServerUrl = GameHost.Instance != null && GodotObject.IsInstanceValid(LobbyManager.Instance) ? LobbyManager.Instance.RegistryServerUrl : ServersConfigHelper.GetDefaultServerUrl();
		
		try
		{
			using (var httpClient = new System.Net.Http.HttpClient())
			{
				httpClient.Timeout = TimeSpan.FromSeconds(3);
				var res = await httpClient.GetAsync(seedServerUrl + "/api/creators/check/" + Uri.EscapeDataString(pubKeyStr));
				if (res.StatusCode == System.Net.HttpStatusCode.NotFound)
				{
					if (GodotObject.IsInstanceValid(this) && IsInsideTree() && GetNodeOrNull("CreatorRegistrationOverlay") == null && GetNodeOrNull("AgreementOverlay") == null)
					{
						ShowCreatorRegistrationDialog(pubKeyStr, key);
					}
				}
			}
		}
		catch (Exception ex)
		{
			GD.Print($"[MapEditorHUD] Offline or registry server unreachable, skipping creator registration prompt: {ex.Message}");
		}
	}

	private void ShowCreatorRegistrationDialog(string pubKeyStr, NSec.Cryptography.Key key)
	{
		if (GetNodeOrNull("CreatorRegistrationOverlay") != null) return;

		var overlay = new ColorRect();
		overlay.Name = "CreatorRegistrationOverlay";
		overlay.Color = new Color(0, 0, 0, 0.75f);
		overlay.SetAnchorsPreset(LayoutPreset.FullRect);
		overlay.MouseFilter = Control.MouseFilterEnum.Stop;
		overlay.ZIndex = 1000;
		AddChild(overlay);

		var center = new CenterContainer();
		center.SetAnchorsPreset(LayoutPreset.FullRect);
		overlay.AddChild(center);

		var panel = new PanelContainer();
		panel.CustomMinimumSize = new Vector2(480, 260);
		var style = new StyleBoxFlat();
		style.BgColor = new Color(0.12f, 0.12f, 0.18f, 0.98f);
		style.BorderWidthTop = 2; style.BorderWidthBottom = 2; style.BorderWidthLeft = 2; style.BorderWidthRight = 2;
		style.BorderColor = UIStyle.ColorCyanGlow;
		style.CornerRadiusTopLeft = 6; style.CornerRadiusTopRight = 6; style.CornerRadiusBottomLeft = 6; style.CornerRadiusBottomRight = 6;
		panel.AddThemeStyleboxOverride("panel", style);
		center.AddChild(panel);

		var vbox = new VBoxContainer();
		vbox.AddThemeConstantOverride("separation", 12);
		var margin = new MarginContainer();
		margin.AddThemeConstantOverride("margin_top", 18);
		margin.AddThemeConstantOverride("margin_bottom", 18);
		margin.AddThemeConstantOverride("margin_left", 20);
		margin.AddThemeConstantOverride("margin_right", 20);
		margin.AddChild(vbox);
		panel.AddChild(margin);

		var title = new Label();
		UIStyle.ApplyTitle(title, TranslationServer.Translate("REGISTER CREATOR PROFILE"), 18);
		title.HorizontalAlignment = HorizontalAlignment.Center;
		title.AddThemeColorOverride("font_color", UIStyle.ColorGold);
		vbox.AddChild(title);

		var desc = new Label();
		desc.Text = TranslationServer.Translate("A new cryptographic key pair has been generated for your machine. Please choose a unique display name to lock to this key.");
		desc.HorizontalAlignment = HorizontalAlignment.Center;
		desc.AutowrapMode = TextServer.AutowrapMode.WordSmart;
		desc.AddThemeFontSizeOverride("font_size", 12);
		desc.AddThemeColorOverride("font_color", new Color(0.9f, 0.9f, 0.95f));
		vbox.AddChild(desc);

		var lineEdit = new LineEdit();
		lineEdit.PlaceholderText = TranslationServer.Translate("Enter Username");
		lineEdit.Alignment = HorizontalAlignment.Center;
		vbox.AddChild(lineEdit);

		var errLabel = new Label();
		errLabel.HorizontalAlignment = HorizontalAlignment.Center;
		errLabel.AddThemeColorOverride("font_color", new Color(1, 0.3f, 0.3f));
		errLabel.AddThemeFontSizeOverride("font_size", 11);
		vbox.AddChild(errLabel);

		var btnRow = new HBoxContainer();
		btnRow.AddThemeConstantOverride("separation", 12);
		btnRow.Alignment = BoxContainer.AlignmentMode.Center;

		var btnCancel = new Button();
		btnCancel.Set("icon_max_width", 0);
		SetupOptionButton(btnCancel, TranslationServer.Translate("Cancel / Skip"), () => overlay.QueueFree(), 13);
		btnCancel.CustomMinimumSize = new Vector2(130, 36);
		btnRow.AddChild(btnCancel);

		var btnRegister = new Button();
		btnRegister.Set("icon_max_width", 0);
		SetupOptionButton(btnRegister, TranslationServer.Translate("Register"), null, 13);
		btnRegister.CustomMinimumSize = new Vector2(150, 36);
		btnRegister.Pressed += async () => {
			string username = lineEdit.Text.Trim();
			if (!NameNormalizationHelper.ValidateUsername(username, out var validationError))
			{
				errLabel.Text = TranslationServer.Translate(validationError ?? "Invalid username.");
				return;
			}

			btnRegister.Disabled = true;
			errLabel.Text = TranslationServer.Translate("Registering...");

			try
			{
				byte[] payloadBytes = System.Text.Encoding.UTF8.GetBytes(username + ":" + pubKeyStr);
				byte[] sigBytes = SignatureAlgorithm.Ed25519.Sign(key, payloadBytes);
				string signatureStr = Convert.ToBase64String(sigBytes);

				var regPayload = new {
					Username = username,
					PublicKey = pubKeyStr,
					Signature = signatureStr
				};

				string seedServerUrl = GameHost.Instance != null && GodotObject.IsInstanceValid(LobbyManager.Instance) ? LobbyManager.Instance.RegistryServerUrl : ServersConfigHelper.GetDefaultServerUrl();
				using (var httpClient = new System.Net.Http.HttpClient())
				{
					httpClient.Timeout = TimeSpan.FromSeconds(5);
					var content = new StringContent(JsonSerializer.Serialize(regPayload), System.Text.Encoding.UTF8, "application/json");
					var res = await httpClient.PostAsync(seedServerUrl + "/api/creators/register", content);
					if (res.IsSuccessStatusCode)
					{
						string keyDir = ProjectSettings.GlobalizePath("user://appdata/keys/");
						AuthorshipKeyHelper.UpdateUserName(keyDir, username);
						overlay.QueueFree();
						ShowFeedback(TranslationServer.Translate("Creator registered successfully!"));
					}
					else
					{
						string errText = await res.Content.ReadAsStringAsync();
						try {
							var errDoc = JsonDocument.Parse(errText);
							if (errDoc.RootElement.TryGetProperty("Message", out var msgProp))
							{
								errLabel.Text = msgProp.GetString();
							}
							else
							{
								errLabel.Text = TranslationServer.Translate("Registration failed.");
							}
						}
						catch {
							errLabel.Text = TranslationServer.Translate("Registration failed: ") + errText;
						}
						btnRegister.Disabled = false;
					}
				}
			}
			catch (Exception ex)
			{
				errLabel.Text = TranslationServer.Translate("Error: ") + ex.Message;
				btnRegister.Disabled = false;
			}
		};
		btnRow.AddChild(btnRegister);
		vbox.AddChild(btnRow);
	}

	public void OpenAssetBrowser(string title, IEnumerable<string> allowedExtensions, Action<string> onAssetSelected, bool requireRealmMetadata = false, string? requiredAssetType = null)
	{
		if (_assetBrowserDialog == null)
		{
			_assetBrowserDialog = new AssetBrowserDialog(this);
		}
		_assetBrowserDialog.OpenForImport(title, allowedExtensions, onAssetSelected, requireRealmMetadata, requiredAssetType);
	}

	public void ImportTerrainFromMinimapDialog()
	{
		string initialDir = GetInitialDirectory();
		var err = DisplayServer.FileDialogShow(
			TranslationServer.Translate("Select Minimap Image to Import Terrain"),
			initialDir,
			"",
			false,
			DisplayServer.FileDialogMode.OpenFile,
			new[] { "*.png,*.jpg,*.jpeg,*.webp,*.gif ; Image Files (*.png, *.jpg, *.jpeg, *.webp, *.gif)" },
			Callable.From((bool status, string[] selectedPaths, int selectedFilterIndex) =>
			{
				if (status && selectedPaths.Length > 0)
				{
					ImportTerrainFromMinimapPath(selectedPaths[0]);
				}
			})
		);

		if (err != Error.Ok)
		{
			ShowFeedback(TranslationServer.Translate("Failed to show file dialog"));
		}
	}

	private void ImportTerrainFromMinimapPath(string selectedPath)
	{
		if (GameHost.Instance == null || GameHost.Instance.GroundTerrain == null) return;

		ResetFolderLocations();
		GameHost.Instance.ClearMapEntirely();
		bool success = GameHost.Instance.ImportTerrainFromMinimap(selectedPath, out var smoothedHeights, out var splatMap, out var treePositions);
		if (!success) return;

		int width = GameHost.Instance.GroundTerrain.Width;
		int depth = GameHost.Instance.GroundTerrain.Depth;

		if (GameHost.Instance.GroundTerrain.CliffSplatMap == null || GameHost.Instance.GroundTerrain.CliffSplatMap.GetLength(0) != width + 1 || GameHost.Instance.GroundTerrain.CliffSplatMap.GetLength(1) != depth + 1)
		{
			GameHost.Instance.GroundTerrain.CliffSplatMap = new TerrainSplatWeights[width + 1, depth + 1];
		}

		var pathingCodes = GameHost.Instance.GroundTerrain.PathingCodes;
		if (pathingCodes == null || pathingCodes.GetLength(0) != width || pathingCodes.GetLength(1) != depth)
		{
			pathingCodes = new int[width, depth];
		}

		GameHost.Instance.GroundTerrain.SetHeights(smoothedHeights);
		var cells = GameHost.Instance.GroundTerrain.Cells;

		for (int gz = 0; gz <= depth; gz++)
		{
			for (int gx = 0; gx <= width; gx++)
			{
				if (gx < splatMap.GetLength(0) && gz < splatMap.GetLength(1))
				{
					GameHost.Instance.GroundTerrain.SplatMap[gx, gz] = splatMap[gx, gz];
				}
				GameHost.Instance.GroundTerrain.CliffSplatMap[gx, gz] = TerrainSplatWeights.CreateSolid(GameHost.Instance.EditorCliffPaintTextureIndex);

				if (gx < width && gz < depth)
				{
					pathingCodes[gx, gz] = cells != null ? EditableTerrain.GetDefaultPathingCode(cells[gx, gz]) : EditableTerrain.GetDefaultPathingCode(WaterType.None);
				}
			}
		}

		GameHost.Instance.AlignTerrainSplatMapExternal();
		GameHost.Instance.GroundTerrain.UpdateMeshAndPhysics();

		List<string> treeModels = new();
		string wsPath = !string.IsNullOrEmpty(_tempWorkspacePath) 
			? _tempWorkspacePath 
			: ProjectSettings.GlobalizePath(TempWorkspaceGodotPath);

		if (MetadataService.Instance.TryLoadMetadata(wsPath, out var metadata))
		{
			try
			{
				if (metadata.CustomResources != null && metadata.CustomResources.Count > 0)
				{
					foreach (var rObj in metadata.CustomResources)
					{
						string uId = rObj.ObjectID ?? "";
						string name = rObj.Name ?? "";
						string mPath = rObj.ModelPath ?? "";
						if (!string.IsNullOrEmpty(uId))
						{
							if (uId.Contains("tree", StringComparison.OrdinalIgnoreCase) ||
							    name.Contains("tree", StringComparison.OrdinalIgnoreCase) ||
							    mPath.Contains("tree", StringComparison.OrdinalIgnoreCase))
							{
								treeModels.Add(uId);
							}
						}
					}

					if (treeModels.Count == 0)
					{
						foreach (var rObj in metadata.CustomResources)
						{
							string uId = rObj.ObjectID ?? "";
							if (!string.IsNullOrEmpty(uId))
							{
								treeModels.Add(uId);
							}
						}
					}
				}

				var assetsObj = Realm.Godot.Utils.MapAssetHelper.LoadUnionedAssets(wsPath);
				if (treeModels.Count == 0 && assetsObj?["glb"]?["resources"] is System.Text.Json.Nodes.JsonObject glbRes)
				{
					foreach (var kvp in glbRes)
					{
						string key = kvp.Key;
						if (key.Contains("tree", StringComparison.OrdinalIgnoreCase))
						{
							treeModels.Add(System.IO.Path.GetFileNameWithoutExtension(key));
						}
					}

					if (treeModels.Count == 0)
					{
						foreach (var kvp in glbRes)
						{
							treeModels.Add(System.IO.Path.GetFileNameWithoutExtension(kvp.Key));
						}
					}
				}
			}
			catch (Exception ex)
			{
				GD.PrintErr($"Failed to read tree models from metadata.json: {ex.Message}");
			}
		}

		if (treeModels.Count == 0 && GameHost.ResourceRegistry != null && GameHost.ResourceRegistry.Count > 0)
		{
			foreach (var kvp in GameHost.ResourceRegistry)
			{
				if (kvp.Key.ToString().Contains("tree", StringComparison.OrdinalIgnoreCase) ||
				    (!string.IsNullOrEmpty(kvp.Value.Name) && kvp.Value.Name.Contains("tree", StringComparison.OrdinalIgnoreCase)) ||
				    (!string.IsNullOrEmpty(kvp.Value.ModelPath) && kvp.Value.ModelPath.Contains("tree", StringComparison.OrdinalIgnoreCase)))
				{
					treeModels.Add(kvp.Key);
				}
			}

			if (treeModels.Count == 0)
			{
				foreach (var kvp in GameHost.ResourceRegistry)
				{
					treeModels.Add(kvp.Key);
				}
			}
		}

		if (treeModels.Count > 0)
		{
			var random = new Random();
			foreach (var (x, y, z, rot, scale) in treePositions)
			{
				string treePropId = treeModels[random.Next(treeModels.Count)];
				GameHost.Instance.SpawnPropExternalWithParams(treePropId, new Vector3(x, y, z), rot, scale);
			}
		}

		ShowFeedback(TranslationServer.Translate("Terrain imported from minimap image successfully!"));
	}

	private void SetupMirrorDropdown(OptionButton opt, string tooltip = "")
	{
		if (opt == null) return;
		opt.Clear();
		opt.Set("icon_max_width", 0);
		opt.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
		opt.ClipText = true;

		int folds = GameHost.Instance != null ? GameHost.Instance.EditorSymmetryFolds : 4;
		opt.AddItem(TranslationServer.Translate("\uf05e SYMMETRY: NONE"), (int)MirrorMode.None);
		opt.AddItem(TranslationServer.Translate("\uf07d MIRROR: VERTICAL"), (int)MirrorMode.Vertical);
		opt.AddItem(TranslationServer.Translate("\uf07e MIRROR: HORIZONTAL"), (int)MirrorMode.Horizontal);
		opt.AddItem(TranslationServer.Translate("\uf00a MIRROR: QUAD"), (int)MirrorMode.Both);
		opt.AddItem(string.Format(TranslationServer.Translate("\uf01e ROTATIONAL ({0}-FOLD)"), folds), (int)MirrorMode.Rotational);

		if (!string.IsNullOrEmpty(tooltip))
		{
			opt.TooltipText = tooltip;
		}

		StyleOptionButtonPopup(opt);

		opt.ItemSelected += (long idx) =>
		{
			int modeId = opt.GetItemId((int)idx);
			SetMirrorMode((MirrorMode)modeId);
		};
	}

	private void SetMirrorMode(MirrorMode mode)
	{
		if (GameHost.Instance == null) return;
		GameHost.Instance.EditorMirrorMode = mode;
		GameHost.Instance.UpdateSymmetryPivotVisuals();
		GameHost.Instance.InvalidateSelectionHighlightMesh();
		UpdateMirrorDropdowns();
		UpdateSymmetrySubControlsVisibility();
		UpdatePolarRadialStepButtonText();
		if (mode == MirrorMode.Rotational)
		{
			ExpandAccordion(_btnHeaderViewport, _contentViewport, "Viewport & Navigation");
		}
		string modeName = mode == MirrorMode.Both ? "QUAD" : mode.ToString().ToUpperInvariant();
		ShowFeedback(string.Format(TranslationServer.Translate("Mirroring: {0}"), modeName));
	}

	public void UpdateMirrorDropdowns()
	{
		if (GameHost.Instance == null) return;
		int folds = GameHost.Instance.EditorSymmetryFolds;
		var mode = GameHost.Instance.EditorMirrorMode;

		UpdateSingleMirrorDropdown(_optMirrorMode, mode, folds);
		UpdateSingleMirrorDropdown(_optPlacementMirrorMode, mode, folds);
		UpdateSingleMirrorDropdown(_optClipboardMirrorMode, mode, folds);

		UpdateSymmetrySubControlsVisibility();
	}

	public void UpdateMirrorButtonText() => UpdateMirrorDropdowns();

	private void UpdateSingleMirrorDropdown(OptionButton opt, MirrorMode mode, int folds)
	{
		if (opt == null) return;

		string rotText = string.Format(TranslationServer.Translate("\uf01e ROTATIONAL ({0}-FOLD)"), folds);
		for (int i = 0; i < opt.ItemCount; i++)
		{
			if (opt.GetItemId(i) == (int)MirrorMode.Rotational)
			{
				opt.SetItemText(i, rotText);
				break;
			}
		}

		for (int i = 0; i < opt.ItemCount; i++)
		{
			if (opt.GetItemId(i) == (int)mode)
			{
				if (opt.Selected != i)
				{
					opt.Selected = i;
				}
				break;
			}
		}
	}

	private static readonly int[] SpokeCountOptions = new[] { 2, 3, 4, 5, 6, 8, 12, 16 };

	private void SetupSpokesDropdown(OptionButton opt, Action<int> onSpokesSelected, string tooltip = "")
	{
		if (opt == null) return;
		opt.Clear();
		opt.Set("icon_max_width", 0);
		opt.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
		opt.ClipText = true;

		for (int i = 0; i < SpokeCountOptions.Length; i++)
		{
			int count = SpokeCountOptions[i];
			opt.AddItem(string.Format(TranslationServer.Translate("\uf14e SPOKES: {0}"), count), count);
		}

		if (!string.IsNullOrEmpty(tooltip))
		{
			opt.TooltipText = tooltip;
		}

		StyleOptionButtonPopup(opt);

		opt.ItemSelected += (long idx) =>
		{
			int spokeCount = opt.GetItemId((int)idx);
			onSpokesSelected?.Invoke(spokeCount);
		};
	}

	private void UpdateSpokesDropdownSelection(OptionButton opt, int currentSpokes)
	{
		if (opt == null) return;
		for (int i = 0; i < opt.ItemCount; i++)
		{
			if (opt.GetItemId(i) == currentSpokes)
			{
				if (opt.Selected != i)
				{
					opt.Selected = i;
				}
				break;
			}
		}
	}

	private void SetSymmetrySpokes(int spokes)
	{
		if (GameHost.Instance == null) return;
		GameHost.Instance.EditorSymmetryFolds = spokes;
		GameHost.Instance.InvalidateSelectionHighlightMesh();
		UpdateSymmetrySpokesDropdowns();
		UpdateMirrorDropdowns();
		UpdatePolarRadialStepButtonText();
		ShowFeedback(string.Format(TranslationServer.Translate("Symmetry Spokes: {0}-way"), spokes));
	}

	public void UpdateSymmetrySpokesDropdowns()
	{
		UpdatePolarRadialStepButtonText();
	}

	public void UpdateSymmetryFoldsButtonText() => UpdateSymmetrySpokesDropdowns();

	private void UpdateSymmetrySubControlsVisibility()
	{
		if (GameHost.Instance == null) return;
		var mode = GameHost.Instance.EditorMirrorMode;
		bool isRotational = mode == MirrorMode.Rotational;
		UpdatePolarSubControlsVisibility(GameHost.Instance.EditorGridMode);
		if (isRotational)
		{
			ExpandAccordion(_btnHeaderViewport, _contentViewport, "Viewport & Navigation");
		}
	}

	private static readonly float[] PolarRingSpacingOptions = new[] { 4.0f, 8.0f, 16.0f, 32.0f };

	private void SetupPolarRingSpacingDropdown(OptionButton opt, string tooltip = "")
	{
		if (opt == null) return;
		opt.Clear();
		opt.Set("icon_max_width", 0);
		opt.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
		opt.ClipText = true;

		for (int i = 0; i < PolarRingSpacingOptions.Length; i++)
		{
			float spacing = PolarRingSpacingOptions[i];
			opt.AddItem(string.Format(TranslationServer.Translate("\uf111 RINGS: {0:F0}T"), spacing), (int)spacing);
		}

		if (!string.IsNullOrEmpty(tooltip))
		{
			opt.TooltipText = tooltip;
		}

		StyleOptionButtonPopup(opt);

		opt.ItemSelected += (long idx) =>
		{
			float spacing = (float)opt.GetItemId((int)idx);
			SetPolarRingSpacing(spacing);
		};
	}

	private void SetPolarRingSpacing(float spacing)
	{
		if (GameHost.Instance == null) return;
		GameHost.Instance.EditorPolarRingSpacing = spacing;
		GameHost.Instance.GroundTerrain?.SetPolarRingSpacing(spacing);
		UpdatePolarRingSpacingButtonText();
		ShowFeedback(string.Format(TranslationServer.Translate("Polar Ring Spacing: {0:F0} tiles"), spacing));
	}

	public void UpdatePolarRingSpacingButtonText()
	{
		if (_optPolarRingSpacing != null && GameHost.Instance != null)
		{
			float current = GameHost.Instance.EditorPolarRingSpacing;
			for (int i = 0; i < _optPolarRingSpacing.ItemCount; i++)
			{
				if (Mathf.IsEqualApprox((float)_optPolarRingSpacing.GetItemId(i), current))
				{
					if (_optPolarRingSpacing.Selected != i)
					{
						_optPolarRingSpacing.Selected = i;
					}
					break;
				}
			}
		}
	}

	private void SetPolarSpokes(int spokes)
	{
		if (GameHost.Instance == null) return;
		if (GameHost.Instance.EditorMirrorMode == MirrorMode.Rotational)
		{
			SetSymmetrySpokes(spokes);
			return;
		}
		GameHost.Instance.EditorPolarSpokeFolds = spokes;
		GameHost.Instance.EditorPolarRadialStep = 360.0f / spokes;
		GameHost.Instance.GroundTerrain?.SetPolarRadialStep(GameHost.Instance.EditorPolarRadialStep);
		UpdatePolarRadialStepButtonText();
		ShowFeedback(string.Format(TranslationServer.Translate("Polar Spokes: {0}-way"), spokes));
	}

	public void UpdatePolarRadialStepButtonText()
	{
		if (_optPolarRadialStep != null && GameHost.Instance != null)
		{
			int spokes = GameHost.Instance.EditorMirrorMode == MirrorMode.Rotational
				? GameHost.Instance.EditorSymmetryFolds
				: GameHost.Instance.EditorPolarSpokeFolds;

			UpdateSpokesDropdownSelection(_optPolarRadialStep, spokes);

			if (GameHost.Instance.EditorMirrorMode == MirrorMode.Rotational)
			{
				_optPolarRadialStep.TooltipText = TranslationServer.Translate("Spokes are automatically locked to Rotational N-Fold symmetry");
			}
			else
			{
				_optPolarRadialStep.TooltipText = TranslationServer.Translate("Select radial spoke count (2, 3, 4, 5, 6, 8, 12, 16)");
			}
		}
	}

	private void SetPivotToCursor()
	{
		if (GameHost.Instance == null || GameHost.Instance.GroundTerrain == null) return;
		var viewport = GetViewport();
		Vector3 hitPos = Vector3.Zero;
		bool hasHit = false;
		if (viewport != null)
		{
			hasHit = GameHost.Instance.TryRaycastTerrainFromMousePosition(viewport.GetMousePosition(), out hitPos);
		}
		if (!hasHit)
		{
			hitPos = GameHost.Instance.MainCamera?.GlobalPosition ?? Vector3.Zero;
		}
		var pivot = new Vector2(hitPos.X, hitPos.Z);
		GameHost.Instance.EditorSymmetryPivot = pivot;
		GameHost.Instance.GroundTerrain.SetPolarCenter(pivot);
		GameHost.Instance.UpdateSymmetryPivotVisuals();
		var (cx, cz) = _editorService != null ? _editorService.WorldPosToCellCoords(hitPos) : (0, 0);
		ShowFeedback(string.Format(TranslationServer.Translate("Symmetry & Polar Pivot set to tile ({0}, {1})"), cx, cz));
	}

	private void ResetPivotToMapCenter()
	{
		if (GameHost.Instance == null || GameHost.Instance.GroundTerrain == null) return;
		var pivot = Vector2.Zero;
		GameHost.Instance.EditorSymmetryPivot = pivot;
		GameHost.Instance.GroundTerrain.SetPolarCenter(pivot);
		GameHost.Instance.UpdateSymmetryPivotVisuals();
		GameHost.Instance.InvalidateSelectionHighlightMesh();
		ShowFeedback(TranslationServer.Translate("Symmetry & Polar Pivot reset to Map Center"));
	}

	private void CyclePasteAnchor()
	{
		if (_editorService == null || !_editorService.HasCopiedArea)
		{
			ShowFeedback(TranslationServer.Translate("No area currently in clipboard to set anchor for."));
			return;
		}
		_currentPasteAnchorIndex = (_currentPasteAnchorIndex + 1) % _pasteAnchorNames.Length;
		int w = _editorService.CopiedAreaWidth;
		int d = _editorService.CopiedAreaDepth;
		int ax = _currentPasteAnchorIndex switch
		{
			0 => w / 2,
			1 => 0,
			2 => Math.Max(0, w - 1),
			3 => Math.Max(0, w - 1),
			4 => 0,
			_ => w / 2
		};
		int az = _currentPasteAnchorIndex switch
		{
			0 => d / 2,
			1 => 0,
			2 => 0,
			3 => Math.Max(0, d - 1),
			4 => Math.Max(0, d - 1),
			_ => d / 2
		};
		_editorService.SetCopiedAreaAnchor(ax, az);
		UpdatePasteAnchorButtonText();
		ShowFeedback(string.Format(TranslationServer.Translate("Paste Anchor: {0}"), TranslationServer.Translate(_pasteAnchorNames[_currentPasteAnchorIndex])));
	}

	public void UpdatePasteAnchorButtonText()
	{
		if (_btnPasteAnchor != null)
		{
			_btnPasteAnchor.Text = string.Format(TranslationServer.Translate("\uf245 ANCHOR: {0}"), TranslationServer.Translate(_pasteAnchorNames[_currentPasteAnchorIndex]));
		}
	}

	private void CyclePasteReflection()
	{
		if (GameHost.Instance == null) return;
		var current = GameHost.Instance.EditorPasteReflection;
		var next = current switch
		{
			PasteReflection.None => PasteReflection.Horizontal,
			PasteReflection.Horizontal => PasteReflection.Vertical,
			PasteReflection.Vertical => PasteReflection.None,
			_ => PasteReflection.None
		};
		GameHost.Instance.EditorPasteReflection = next;
		UpdatePasteReflectionButtonText();
		ShowFeedback(string.Format(TranslationServer.Translate("Paste Reflection: {0}"), TranslationServer.Translate(next.ToString().ToUpperInvariant())));
	}

	public void UpdatePasteReflectionButtonText()
	{
		if (_btnPasteReflection != null && GameHost.Instance != null)
		{
			_btnPasteReflection.Text = string.Format(TranslationServer.Translate("\uf07e REFLECT: {0}"), TranslationServer.Translate(GameHost.Instance.EditorPasteReflection.ToString().ToUpperInvariant()));
		}
	}

	private void RebuildHUDLayout()
	{
	}

	private void UpdatePanelVisibilityForModule(EditorModule module)
	{
		if (_accordionFile == null) _accordionFile = GetNodeOrNull<VBoxContainer>("LeftSlidePanel/LeftScroll/LeftVBox/FileAccordion");
		if (_accordionViewport == null) _accordionViewport = GetNodeOrNull<VBoxContainer>("LeftSlidePanel/LeftScroll/LeftVBox/ViewportAccordion");

		if (_accordionFile != null) _accordionFile.Visible = true;
		if (_accordionViewport != null) _accordionViewport.Visible = true;

		bool showTool = true;
		bool showBrush = (module == EditorModule.Terrain || module == EditorModule.TextureDeco || module == EditorModule.Pathing);
		bool showToolSettings = (module == EditorModule.Terrain || module == EditorModule.TextureDeco || module == EditorModule.Pathing || module == EditorModule.Objects || module == EditorModule.Clipboard);
		bool showPlacement = (module == EditorModule.Objects || module == EditorModule.Clipboard);

		bool hasSelectedObject = (GameHost.Instance != null && GodotObject.IsInstanceValid(GameHost.Instance.SelectedEditorObject));
		bool showInspector = (module == EditorModule.Objects) || (hasSelectedObject && GameHost.Instance?.ActiveEditorTool == GameHost.EditorTool.SelectMove);

		if (_accordionTool != null) _accordionTool.Visible = showTool;
		if (_accordionBrush != null) _accordionBrush.Visible = showBrush;
		if (_accordionToolSettings != null) _accordionToolSettings.Visible = showToolSettings;
		if (_accordionPlacement != null) _accordionPlacement.Visible = showPlacement;
		if (_accordionInspector != null) _accordionInspector.Visible = showInspector;

		if (_panelTerrainVBox != null) _panelTerrainVBox.Visible = (module == EditorModule.Terrain);
		if (_panelDecoVBox != null) _panelDecoVBox.Visible = (module == EditorModule.TextureDeco);
		if (_panelPathingVBox != null) _panelPathingVBox.Visible = (module == EditorModule.Pathing);
		if (_panelCoordinatesVBox != null) _panelCoordinatesVBox.Visible = (module == EditorModule.Coordinates);
		if (_panelObjects != null) _panelObjects.Visible = (module == EditorModule.Objects);
		if (_panelClipboard != null) _panelClipboard.Visible = (module == EditorModule.Clipboard);

		if (_containerTextureSettings != null) _containerTextureSettings.Visible = (module == EditorModule.Terrain || module == EditorModule.TextureDeco);
		if (_panelEnv != null) _panelEnv.Visible = (module == EditorModule.TextureDeco);
		if (_containerPathingSettings != null) _containerPathingSettings.Visible = (module == EditorModule.Pathing);
		if (_containerPasteSettings != null) _containerPasteSettings.Visible = (module == EditorModule.Clipboard);
		if (_containerCategorySelector != null) _containerCategorySelector.Visible = (module == EditorModule.Objects);

		if (showTool && _contentTool != null)
		{
			_contentTool.Visible = true;
			if (_btnHeaderTool != null) _btnHeaderTool.Text = TranslationServer.Translate("Tool").ToString().ToUpperInvariant() + "  ▼";
		}
		if (showBrush && _contentBrush != null)
		{
			_contentBrush.Visible = true;
			if (_btnHeaderBrush != null) _btnHeaderBrush.Text = TranslationServer.Translate("Global Brush Properties").ToString().ToUpperInvariant() + "  ▼";
		}
		if (showToolSettings && _contentToolSettings != null)
		{
			_contentToolSettings.Visible = true;
			if (_btnHeaderToolSettings != null) _btnHeaderToolSettings.Text = TranslationServer.Translate("Tool Settings").ToString().ToUpperInvariant() + "  ▼";
		}
		if (showPlacement && _contentPlacement != null)
		{
			_contentPlacement.Visible = true;
			if (_btnHeaderPlacement != null) _btnHeaderPlacement.Text = TranslationServer.Translate("Placement Config").ToString().ToUpperInvariant() + "  ▼";
		}

		_accordionContainer?.QueueSort();
		RefreshCardScrollStates();
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

	private void UpdateCardScrollState(Control contentControl, float maxHeight = 300f, bool allowExpandBtn = true)
	{
		if (contentControl == null) return;
		ScrollContainer scroll = contentControl.GetNodeOrNull<ScrollContainer>("CardScroll");
		if (scroll == null) return;

		var targetInner = scroll.GetNodeOrNull<VBoxContainer>("InnerVBox");
		if (targetInner != null)
		{
			targetInner.ForceUpdateTransform();
			float minH = targetInner.GetCombinedMinimumSize().Y;
			Button expandBtn = contentControl.GetNodeOrNull<Button>("BtnExpandHeight");

			if (!allowExpandBtn)
			{
				if (expandBtn != null) expandBtn.Visible = false;
				scroll.VerticalScrollMode = ScrollContainer.ScrollMode.Disabled;
				scroll.CustomMinimumSize = new Vector2(245, 0);
				return;
			}

			if (minH > maxHeight)
			{
				if (expandBtn != null) expandBtn.Visible = true;
			}
			else
			{
				if (expandBtn != null) expandBtn.Visible = false;
				scroll.CustomMinimumSize = new Vector2(245, minH);
			}
		}
	}

	public void SwitchModule(EditorModule module)
	{
		_activeModule = module;
		UpdateModuleSwitchButtons();

		if (module != EditorModule.Coordinates)
		{
			GameHost.Instance?.HideCoordinateSelectionOutline();
		}

		UpdatePanelVisibilityForModule(module);

		if (GameHost.Instance != null)
		{
			switch (module)
			{
				case EditorModule.Terrain:
					TriggerToolSelection(GameHost.EditorTool.Raise, _btnRaise);
					break;
				case EditorModule.TextureDeco:
					TriggerToolSelection(GameHost.EditorTool.PaintTexture, _btnTextureBrush);
					break;
				case EditorModule.Pathing:
					TriggerToolSelection(GameHost.EditorTool.PaintPathing, _btnPathingBrush);
					break;
				case EditorModule.Objects:
					_entityPaletteController?.TriggerAddObjectMode();
					break;
				case EditorModule.Coordinates:
					TriggerToolSelection(GameHost.EditorTool.DrawCoordinate, _btnDrawCoordinate);
					break;
				case EditorModule.Clipboard:
					TriggerToolSelection(GameHost.EditorTool.SelectArea, _btnSelectArea);
					break;
			}
		}
	}

	private void UpdateModuleSwitchButtons()
	{
		if (_optModule != null)
		{
			_optModule.Selected = (int)_activeModule;
		}
	}



	private void SetupAccordion(Button headerBtn, Control contentControl, string titleText)
	{
		string upperTitle = TranslationServer.Translate(titleText).ToString().ToUpperInvariant();
		headerBtn.Text = upperTitle + (contentControl.Visible ? "  \uf0d7" : "  \uf0da");
		var font = GetFontAwesomeFont();
		if (font != null)
		{
			headerBtn.AddThemeFontOverride("font", font);
		}
		StyleSubContainer(contentControl);
	}

	private void StyleAccordionHeader(Button btn)
	{
		if (btn == null) return;
		btn.Flat = false;
		btn.CustomMinimumSize = new Vector2(0, 36);

		var font = GetFontAwesomeFont();
		if (font != null)
		{
			btn.AddThemeFontOverride("font", font);
		}

		var headerTex = GD.Load<Texture2D>("res://Assets/UI/map_editor_options_header.png");
		if (headerTex != null)
		{
			var headerNormal = new StyleBoxTexture();
			headerNormal.Texture = headerTex;
			headerNormal.TextureMarginLeft = 0;
			headerNormal.TextureMarginRight = 0;
			headerNormal.TextureMarginTop = 0;
			headerNormal.TextureMarginBottom = 0;
			headerNormal.ContentMarginLeft = 12;
			headerNormal.ContentMarginRight = 12;
			headerNormal.ContentMarginTop = 6;
			headerNormal.ContentMarginBottom = 6;

			var headerHover = new StyleBoxTexture();
			headerHover.Texture = headerTex;
			headerHover.ModulateColor = new Color(1.2f, 1.15f, 0.9f, 1.0f);
			headerHover.TextureMarginLeft = 0;
			headerHover.TextureMarginRight = 0;
			headerHover.TextureMarginTop = 0;
			headerHover.TextureMarginBottom = 0;
			headerHover.ContentMarginLeft = 12;
			headerHover.ContentMarginRight = 12;
			headerHover.ContentMarginTop = 6;
			headerHover.ContentMarginBottom = 6;

			btn.AddThemeStyleboxOverride("normal", headerNormal);
			btn.AddThemeStyleboxOverride("hover", headerHover);
			btn.AddThemeStyleboxOverride("pressed", headerHover);
		}
		else
		{
			var headerNormal = new StyleBoxFlat();
			headerNormal.BgColor = new Color(0.18f, 0.16f, 0.14f, 0.95f);
			headerNormal.BorderColor = new Color(0.40f, 0.34f, 0.24f, 0.9f);
			headerNormal.SetBorderWidthAll(1);
			headerNormal.CornerRadiusTopLeft = 4;
			headerNormal.CornerRadiusTopRight = 4;
			headerNormal.CornerRadiusBottomLeft = 2;
			headerNormal.CornerRadiusBottomRight = 2;
			headerNormal.ContentMarginLeft = 10;
			headerNormal.ContentMarginRight = 10;

			btn.AddThemeStyleboxOverride("normal", headerNormal);
			btn.AddThemeStyleboxOverride("hover", headerNormal);
			btn.AddThemeStyleboxOverride("pressed", headerNormal);
		}

		btn.AddThemeStyleboxOverride("focus", new StyleBoxEmpty());
		btn.AddThemeFontSizeOverride("font_size", 12);
		btn.AddThemeColorOverride("font_color", UIStyle.ColorGold);
		btn.AddThemeColorOverride("font_hover_color", new Color(1f, 0.95f, 0.8f));
		btn.Alignment = HorizontalAlignment.Left;
	}

	private void ApplyCardPanelStyle(Control accordionNode)
	{
		if (accordionNode == null) return;

		var cardStyle = new StyleBoxFlat();
		cardStyle.BgColor = new Color(0.12f, 0.11f, 0.10f, 0.94f);
		cardStyle.BorderColor = new Color(0.38f, 0.32f, 0.22f, 0.85f);
		cardStyle.SetBorderWidthAll(1);
		cardStyle.CornerRadiusTopLeft = 5;
		cardStyle.CornerRadiusTopRight = 5;
		cardStyle.CornerRadiusBottomLeft = 5;
		cardStyle.CornerRadiusBottomRight = 5;
		cardStyle.ContentMarginLeft = 8;
		cardStyle.ContentMarginRight = 8;
		cardStyle.ContentMarginTop = 6;
		cardStyle.ContentMarginBottom = 8;

		Panel bgPanel = accordionNode.GetNodeOrNull<Panel>("CardBG");
		if (bgPanel == null)
		{
			bgPanel = new Panel();
			bgPanel.Name = "CardBG";
			bgPanel.ShowBehindParent = true;
			bgPanel.MouseFilter = Control.MouseFilterEnum.Ignore;
			bgPanel.SetAnchorsPreset(Control.LayoutPreset.FullRect);
			accordionNode.AddChild(bgPanel);
			accordionNode.MoveChild(bgPanel, 0);
		}
		bgPanel.AddThemeStyleboxOverride("panel", cardStyle);
	}

	private void StyleContentBox(Control contentControl)
	{
		if (contentControl == null) return;

		var contentStyle = new StyleBoxFlat();
		contentStyle.BgColor = new Color(0.10f, 0.09f, 0.08f, 0.95f);
		contentStyle.BorderColor = new Color(0.38f, 0.32f, 0.22f, 0.85f);
		contentStyle.SetBorderWidthAll(1);
		contentStyle.BorderWidthTop = 0;
		contentStyle.CornerRadiusBottomLeft = 4;
		contentStyle.CornerRadiusBottomRight = 4;
		contentStyle.ContentMarginLeft = 6;
		contentStyle.ContentMarginRight = 6;
		contentStyle.ContentMarginTop = 6;
		contentStyle.ContentMarginBottom = 8;

		if (contentControl is PanelContainer pc)
		{
			pc.AddThemeStyleboxOverride("panel", contentStyle);
		}
		else if (contentControl is VBoxContainer vbox)
		{
			vbox.AddThemeConstantOverride("separation", 2);

			Panel bgPanel = vbox.GetNodeOrNull<Panel>("ContentBG");
			if (bgPanel == null)
			{
				bgPanel = new Panel();
				bgPanel.Name = "ContentBG";
				bgPanel.ShowBehindParent = true;
				bgPanel.MouseFilter = Control.MouseFilterEnum.Ignore;
				bgPanel.SetAnchorsPreset(Control.LayoutPreset.FullRect);
				vbox.AddChild(bgPanel);
				vbox.MoveChild(bgPanel, 0);
			}
			bgPanel.AddThemeStyleboxOverride("panel", contentStyle);
		}
	}

	private void StyleRowButton(Button btn)
	{
		if (btn == null) return;
		btn.Flat = false;
		btn.CustomMinimumSize = new Vector2(0, 32);

		var rowNormal = new StyleBoxFlat();
		rowNormal.BgColor = new Color(0.14f, 0.13f, 0.11f, 0.4f);
		rowNormal.BorderColor = new Color(0.24f, 0.21f, 0.17f, 0.5f);
		rowNormal.BorderWidthBottom = 1;
		rowNormal.ContentMarginLeft = 10;
		rowNormal.ContentMarginRight = 10;

		var rowHover = new StyleBoxFlat();
		rowHover.BgColor = new Color(0.25f, 0.22f, 0.18f, 0.9f);
		rowHover.BorderColor = UIStyle.ColorGold;
		rowHover.BorderWidthBottom = 1;
		rowHover.ContentMarginLeft = 10;
		rowHover.ContentMarginRight = 10;

		var rowPressed = new StyleBoxFlat();
		rowPressed.BgColor = new Color(0.30f, 0.26f, 0.20f, 0.95f);
		rowPressed.BorderColor = UIStyle.ColorGold;
		rowPressed.SetBorderWidthAll(1);
		rowPressed.ContentMarginLeft = 10;
		rowPressed.ContentMarginRight = 10;

		btn.AddThemeStyleboxOverride("normal", rowNormal);
		btn.AddThemeStyleboxOverride("hover", rowHover);
		btn.AddThemeStyleboxOverride("pressed", rowPressed);
		btn.AddThemeStyleboxOverride("focus", new StyleBoxEmpty());
		btn.AddThemeFontSizeOverride("font_size", 12);
		btn.AddThemeColorOverride("font_color", new Color(0.92f, 0.88f, 0.82f));
		btn.AddThemeColorOverride("font_hover_color", UIStyle.ColorGold);
		btn.Alignment = HorizontalAlignment.Left;
	}

	private Control GetContentTarget(Control contentControl)
	{
		if (contentControl == null) return null;
		var inner = contentControl.GetNodeOrNull<Control>("CardScroll/InnerVBox");
		return inner ?? contentControl;
	}

	private void SetupCardScrollContainer(Control contentControl, float maxHeight = 300f, bool allowExpandBtn = true)
	{
		if (contentControl == null) return;

		contentControl.CustomMinimumSize = new Vector2(245, 0);

		ScrollContainer scroll = contentControl.GetNodeOrNull<ScrollContainer>("CardScroll");
		if (scroll == null)
		{
			scroll = new ScrollContainer();
			scroll.Name = "CardScroll";
			scroll.HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled;
			scroll.VerticalScrollMode = allowExpandBtn ? ScrollContainer.ScrollMode.Auto : ScrollContainer.ScrollMode.Disabled;
			scroll.CustomMinimumSize = new Vector2(245, 0);

			var innerVBox = new VBoxContainer();
			innerVBox.Name = "InnerVBox";
			innerVBox.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
			innerVBox.AddThemeConstantOverride("separation", 2);

			var children = new Godot.Collections.Array<Node>(contentControl.GetChildren());
			foreach (Node child in children)
			{
				if (child is Panel && child.Name == "ContentBG") continue;
				contentControl.RemoveChild(child);
				innerVBox.AddChild(child);
			}

			scroll.AddChild(innerVBox);
			contentControl.AddChild(scroll);
		}

		var targetInner = scroll.GetNodeOrNull<VBoxContainer>("InnerVBox");
		if (targetInner != null)
		{
			targetInner.ForceUpdateTransform();
			float minH = targetInner.GetCombinedMinimumSize().Y;
			Button expandBtn = contentControl.GetNodeOrNull<Button>("BtnExpandHeight");

			if (!allowExpandBtn)
			{
				if (expandBtn != null) expandBtn.Visible = false;
				scroll.VerticalScrollMode = ScrollContainer.ScrollMode.Disabled;
				scroll.CustomMinimumSize = new Vector2(245, 0);
				return;
			}
			if (minH > maxHeight)
			{
				if (expandBtn == null)
				{
					expandBtn = new Button();
					expandBtn.Name = "BtnExpandHeight";
					expandBtn.Flat = false;
					expandBtn.CustomMinimumSize = new Vector2(0, 24);

					var btnStyle = new StyleBoxFlat();
					btnStyle.BgColor = new Color(0.16f, 0.14f, 0.12f, 0.85f);
					btnStyle.BorderColor = new Color(0.38f, 0.32f, 0.22f, 0.6f);
					btnStyle.SetBorderWidthAll(1);
					btnStyle.CornerRadiusBottomLeft = 4;
					btnStyle.CornerRadiusBottomRight = 4;

					expandBtn.AddThemeStyleboxOverride("normal", btnStyle);
					expandBtn.AddThemeStyleboxOverride("hover", btnStyle);
					expandBtn.AddThemeStyleboxOverride("pressed", btnStyle);
					expandBtn.AddThemeStyleboxOverride("focus", new StyleBoxEmpty());
					expandBtn.AddThemeFontSizeOverride("font_size", 11);
					expandBtn.AddThemeColorOverride("font_color", UIStyle.ColorGold);
					expandBtn.Alignment = HorizontalAlignment.Center;
					expandBtn.TooltipText = "Expand panel height to view all options without scrolling";

					bool isFull = false;
					expandBtn.Text = "\uf078 EXPAND FULL";
					var fontExp = GetFontAwesomeFont();
					if (fontExp != null) expandBtn.AddThemeFontOverride("font", fontExp);
					expandBtn.Pressed += () =>
					{
						isFull = !isFull;
						if (isFull)
						{
							targetInner.ForceUpdateTransform();
							float fullH = targetInner.GetCombinedMinimumSize().Y + 12f;
							scroll.CustomMinimumSize = new Vector2(245, fullH);
							expandBtn.Text = "\uf077 COMPACT VIEW";
						}
						else
						{
							scroll.CustomMinimumSize = new Vector2(245, maxHeight);
							expandBtn.Text = "\uf078 EXPAND FULL";
						}
						(contentControl as Container)?.QueueSort();
						(contentControl.GetParent() as Container)?.QueueSort();
						(contentControl.GetParent()?.GetParent() as Container)?.QueueSort();

						var leftVBox = GetNodeOrNull<VBoxContainer>("LeftSlidePanel/LeftScroll/LeftVBox");
						leftVBox?.ForceUpdateTransform();
						leftVBox?.QueueSort();

						var rightVBox = GetNodeOrNull<VBoxContainer>("RightSlidePanel/RightScroll/AccordionContainer");
						rightVBox?.ForceUpdateTransform();
						rightVBox?.QueueSort();

						UIManager.Instance?.PlayClickSound();
					};

					contentControl.AddChild(expandBtn);
				}
				if (expandBtn.Text != "🔼 COMPACT VIEW")
				{
					scroll.CustomMinimumSize = new Vector2(245, maxHeight);
				}
				expandBtn.Visible = true;
			}
			else
			{
				scroll.CustomMinimumSize = new Vector2(245, minH);
				if (expandBtn != null) expandBtn.Visible = false;
			}
		}
	}

	private void StyleSubContainer(Control boxNode, string headerText = null)
	{
		if (boxNode == null) return;

		var boxStyle = new StyleBoxFlat();
		boxStyle.BgColor = new Color(0.13f, 0.12f, 0.10f, 0.7f);
		boxStyle.BorderColor = new Color(0.32f, 0.27f, 0.20f, 0.6f);
		boxStyle.SetBorderWidthAll(1);
		boxStyle.CornerRadiusTopLeft = 4;
		boxStyle.CornerRadiusTopRight = 4;
		boxStyle.CornerRadiusBottomLeft = 4;
		boxStyle.CornerRadiusBottomRight = 4;
		boxStyle.ContentMarginLeft = 10;
		boxStyle.ContentMarginRight = 14;
		boxStyle.ContentMarginTop = 6;
		boxStyle.ContentMarginBottom = 6;

		if (boxNode is PanelContainer pc)
		{
			pc.AddThemeStyleboxOverride("panel", boxStyle);
		}
		else if (boxNode is VBoxContainer vbox)
		{
			vbox.AddThemeConstantOverride("separation", 4);
			Panel bgPanel = vbox.GetNodeOrNull<Panel>("SubBoxBG");
			if (bgPanel == null)
			{
				bgPanel = new Panel();
				bgPanel.Name = "SubBoxBG";
				bgPanel.ShowBehindParent = true;
				bgPanel.MouseFilter = Control.MouseFilterEnum.Ignore;
				bgPanel.SetAnchorsPreset(Control.LayoutPreset.FullRect);
				vbox.AddChild(bgPanel);
				vbox.MoveChild(bgPanel, 0);
			}
			bgPanel.AddThemeStyleboxOverride("panel", boxStyle);
		}

		if (!string.IsNullOrEmpty(headerText))
		{
			Label lblHeader = boxNode.GetNodeOrNull<Label>("Header/LblSubTitle");
			if (lblHeader == null)
			{
				var headerBox = boxNode.GetNodeOrNull<Control>("Header");
				if (headerBox != null)
				{
					lblHeader = headerBox.GetNodeOrNull<Label>("LblSubTitle");
				}
			}
			if (lblHeader != null)
			{
				lblHeader.Text = headerText.ToUpperInvariant();
				lblHeader.AddThemeFontSizeOverride("font_size", 10);
				lblHeader.AddThemeColorOverride("font_color", UIStyle.ColorGold);
			}
		}
	}

	private void StyleValueBadge(Label lbl)
	{
		if (lbl == null) return;
		var badgeStyle = new StyleBoxFlat();
		badgeStyle.BgColor = new Color(0.18f, 0.16f, 0.13f, 0.95f);
		badgeStyle.BorderColor = UIStyle.ColorGold;
		badgeStyle.SetBorderWidthAll(1);
		badgeStyle.CornerRadiusTopLeft = 3;
		badgeStyle.CornerRadiusTopRight = 3;
		badgeStyle.CornerRadiusBottomLeft = 3;
		badgeStyle.CornerRadiusBottomRight = 3;
		badgeStyle.ContentMarginLeft = 6;
		badgeStyle.ContentMarginRight = 6;
		badgeStyle.ContentMarginTop = 2;
		badgeStyle.ContentMarginBottom = 2;

		lbl.AddThemeStyleboxOverride("normal", badgeStyle);
		lbl.AddThemeFontSizeOverride("font_size", 11);
		lbl.AddThemeColorOverride("font_color", UIStyle.ColorGold);
		lbl.HorizontalAlignment = HorizontalAlignment.Center;
		lbl.VerticalAlignment = VerticalAlignment.Center;
	}

	private void StyleCheckBoxRow(CheckBox chk)
	{
		if (chk == null) return;
		chk.CustomMinimumSize = new Vector2(0, 28);
		chk.AddThemeFontSizeOverride("font_size", 11);
		chk.AddThemeColorOverride("font_color", new Color(0.88f, 0.84f, 0.78f));
		chk.AddThemeColorOverride("font_hover_color", UIStyle.ColorGold);
		chk.AddThemeColorOverride("font_pressed_color", UIStyle.ColorGold);
	}

	private void SetupPathingCheckBoxRow(VBoxContainer parent, CheckBox chk, Color color, string labelText)
	{
		if (chk == null || parent == null) return;

		chk.Text = TranslationServer.Translate(labelText);
		chk.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
		UIStyle.ApplyCheckboxStyle(chk);
		StyleCheckBoxRow(chk);

		var rowPanel = new PanelContainer();
		rowPanel.Name = "RowPanel" + chk.Name;
		rowPanel.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;

		var rowStyle = new StyleBoxFlat();
		rowStyle.BgColor = new Color(0.12f, 0.13f, 0.16f, 0.82f);
		rowStyle.BorderColor = new Color(0.35f, 0.33f, 0.28f, 0.75f);
		rowStyle.SetBorderWidthAll(1);
		rowStyle.CornerRadiusTopLeft = 4;
		rowStyle.CornerRadiusTopRight = 4;
		rowStyle.CornerRadiusBottomLeft = 4;
		rowStyle.CornerRadiusBottomRight = 4;
		rowStyle.ContentMarginLeft = 8;
		rowStyle.ContentMarginRight = 8;
		rowStyle.ContentMarginTop = 3;
		rowStyle.ContentMarginBottom = 3;
		rowPanel.AddThemeStyleboxOverride("panel", rowStyle);

		var row = new HBoxContainer();
		row.Name = "Row" + chk.Name;
		row.AddThemeConstantOverride("separation", 8);
		row.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;

		int originalIndex = chk.GetIndex();
		parent.RemoveChild(chk);
		row.AddChild(chk);

		var colorBox = new Panel();
		colorBox.Name = "ColorBox" + chk.Name;
		colorBox.CustomMinimumSize = new Vector2(18, 18);
		colorBox.SizeFlagsVertical = Control.SizeFlags.ShrinkCenter;
		colorBox.TooltipText = TranslationServer.Translate(labelText);

		var boxStyle = new StyleBoxFlat();
		boxStyle.BgColor = color;
		boxStyle.BorderColor = new Color(0.95f, 0.95f, 1.0f, 0.85f);
		boxStyle.SetBorderWidthAll(1);
		boxStyle.CornerRadiusTopLeft = 3;
		boxStyle.CornerRadiusTopRight = 3;
		boxStyle.CornerRadiusBottomLeft = 3;
		boxStyle.CornerRadiusBottomRight = 3;
		colorBox.AddThemeStyleboxOverride("panel", boxStyle);

		colorBox.GuiInput += (@event) =>
		{
			if (@event is InputEventMouseButton mb && mb.Pressed && mb.ButtonIndex == MouseButton.Left)
			{
				chk.ButtonPressed = !chk.ButtonPressed;
			}
		};

		row.AddChild(colorBox);
		rowPanel.AddChild(row);
		parent.AddChild(rowPanel);
		parent.MoveChild(rowPanel, originalIndex);
	}

	private FontVariation _faFontVariation;
	public FontVariation GetFontAwesomeFont()
	{
		if (_faFontVariation == null)
		{
			try
			{
				var faFont = GD.Load<FontFile>("res://Assets/UI/fa-solid-900.ttf");
				if (faFont != null)
				{
					_faFontVariation = new FontVariation();
					_faFontVariation.Fallbacks = new Godot.Collections.Array<Font> { faFont };
				}
			}
			catch
			{
				_faFontVariation = null;
			}
		}
		return _faFontVariation;
	}

	private void StyleMapEditorTopButton(Button btn)
	{
		if (btn == null) return;
		var tex = GD.Load<Texture2D>("res://Assets/UI/map_editor_button.png");
		if (tex != null)
		{
			var normalStyle = new StyleBoxTexture();
			normalStyle.Texture = tex;
			normalStyle.TextureMarginLeft = 0;
			normalStyle.TextureMarginRight = 0;
			normalStyle.TextureMarginTop = 0;
			normalStyle.TextureMarginBottom = 0;
			normalStyle.ContentMarginLeft = 12;
			normalStyle.ContentMarginRight = 12;
			normalStyle.ContentMarginTop = 6;
			normalStyle.ContentMarginBottom = 6;

			var hoverStyle = new StyleBoxTexture();
			hoverStyle.Texture = tex;
			hoverStyle.ModulateColor = new Color(1.25f, 1.2f, 1.0f, 1.0f);
			hoverStyle.TextureMarginLeft = 0;
			hoverStyle.TextureMarginRight = 0;
			hoverStyle.TextureMarginTop = 0;
			hoverStyle.TextureMarginBottom = 0;
			hoverStyle.ContentMarginLeft = 12;
			hoverStyle.ContentMarginRight = 12;
			hoverStyle.ContentMarginTop = 6;
			hoverStyle.ContentMarginBottom = 6;

			var pressedStyle = new StyleBoxTexture();
			pressedStyle.Texture = tex;
			pressedStyle.ModulateColor = new Color(0.85f, 0.8f, 0.7f, 1.0f);
			pressedStyle.TextureMarginLeft = 0;
			pressedStyle.TextureMarginRight = 0;
			pressedStyle.TextureMarginTop = 0;
			pressedStyle.TextureMarginBottom = 0;
			pressedStyle.ContentMarginLeft = 12;
			pressedStyle.ContentMarginRight = 12;
			pressedStyle.ContentMarginTop = 6;
			pressedStyle.ContentMarginBottom = 6;

			btn.AddThemeStyleboxOverride("normal", normalStyle);
			btn.AddThemeStyleboxOverride("hover", hoverStyle);
			btn.AddThemeStyleboxOverride("pressed", pressedStyle);
		}
		btn.AddThemeStyleboxOverride("focus", new StyleBoxEmpty());
	}

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

	private void StyleIconButton(Button btn, string iconText, string tooltipText)
	{
		if (btn == null) return;
		btn.Text = iconText;
		btn.TooltipText = tooltipText;
		btn.Flat = false;
		btn.CustomMinimumSize = new Vector2(30, 30);
		btn.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;

		var font = GetFontAwesomeFont();
		if (font != null)
		{
			btn.AddThemeFontOverride("font", font);
		}

		var btnNormal = new StyleBoxFlat();
		btnNormal.BgColor = new Color(0.16f, 0.15f, 0.13f, 0.85f);
		btnNormal.BorderColor = new Color(0.38f, 0.32f, 0.24f, 0.7f);
		btnNormal.SetBorderWidthAll(1);
		btnNormal.CornerRadiusTopLeft = 4;
		btnNormal.CornerRadiusTopRight = 4;
		btnNormal.CornerRadiusBottomLeft = 4;
		btnNormal.CornerRadiusBottomRight = 4;

		var btnHover = new StyleBoxFlat();
		btnHover.BgColor = new Color(0.28f, 0.25f, 0.19f, 0.95f);
		btnHover.BorderColor = UIStyle.ColorGold;
		btnHover.SetBorderWidthAll(1);
		btnHover.CornerRadiusTopLeft = 4;
		btnHover.CornerRadiusTopRight = 4;
		btnHover.CornerRadiusBottomLeft = 4;
		btnHover.CornerRadiusBottomRight = 4;

		var btnPressed = new StyleBoxFlat();
		btnPressed.BgColor = new Color(0.35f, 0.30f, 0.22f, 0.98f);
		btnPressed.BorderColor = UIStyle.ColorGold;
		btnPressed.SetBorderWidthAll(1);

		btn.AddThemeStyleboxOverride("normal", btnNormal);
		btn.AddThemeStyleboxOverride("hover", btnHover);
		btn.AddThemeStyleboxOverride("pressed", btnPressed);
		btn.AddThemeStyleboxOverride("focus", new StyleBoxEmpty());
		btn.AddThemeFontSizeOverride("font_size", 14);
		btn.AddThemeColorOverride("font_color", new Color(0.95f, 0.90f, 0.82f));
		btn.AddThemeColorOverride("font_hover_color", UIStyle.ColorGold);
		btn.Alignment = HorizontalAlignment.Center;
	}

	private Control CreateToolCard(Button btn, string iconGlyph, string labelText, Action onClick, string tooltip = "")
	{
		if (btn == null) return new Control();

		btn.Text = iconGlyph;
		btn.TooltipText = tooltip;
		btn.Flat = false;
		btn.CustomMinimumSize = new Vector2(52, 52);
		btn.SizeFlagsHorizontal = Control.SizeFlags.ShrinkCenter;
		btn.SizeFlagsVertical = Control.SizeFlags.ShrinkCenter;

		var font = GetFontAwesomeFont();
		if (font != null)
		{
			btn.AddThemeFontOverride("font", font);
		}

		var btnNormal = new StyleBoxFlat();
		btnNormal.BgColor = new Color(0.16f, 0.15f, 0.13f, 0.90f);
		btnNormal.BorderColor = new Color(0.42f, 0.36f, 0.26f, 0.85f);
		btnNormal.SetBorderWidthAll(2);
		btnNormal.CornerRadiusTopLeft = 6;
		btnNormal.CornerRadiusTopRight = 6;
		btnNormal.CornerRadiusBottomLeft = 6;
		btnNormal.CornerRadiusBottomRight = 6;
		btnNormal.ContentMarginLeft = 4;
		btnNormal.ContentMarginRight = 4;
		btnNormal.ContentMarginTop = 4;
		btnNormal.ContentMarginBottom = 4;

		var btnHover = new StyleBoxFlat();
		btnHover.BgColor = new Color(0.28f, 0.24f, 0.18f, 0.96f);
		btnHover.BorderColor = UIStyle.ColorGold;
		btnHover.SetBorderWidthAll(2);
		btnHover.CornerRadiusTopLeft = 6;
		btnHover.CornerRadiusTopRight = 6;
		btnHover.CornerRadiusBottomLeft = 6;
		btnHover.CornerRadiusBottomRight = 6;
		btnHover.ContentMarginLeft = 4;
		btnHover.ContentMarginRight = 4;
		btnHover.ContentMarginTop = 4;
		btnHover.ContentMarginBottom = 4;

		var btnPressed = new StyleBoxFlat();
		btnPressed.BgColor = new Color(0.36f, 0.30f, 0.20f, 0.98f);
		btnPressed.BorderColor = UIStyle.ColorGold;
		btnPressed.SetBorderWidthAll(2);
		btnPressed.CornerRadiusTopLeft = 6;
		btnPressed.CornerRadiusTopRight = 6;
		btnPressed.CornerRadiusBottomLeft = 6;
		btnPressed.CornerRadiusBottomRight = 6;

		btn.AddThemeStyleboxOverride("normal", btnNormal);
		btn.AddThemeStyleboxOverride("hover", btnHover);
		btn.AddThemeStyleboxOverride("pressed", btnPressed);
		btn.AddThemeStyleboxOverride("focus", new StyleBoxEmpty());
		btn.AddThemeFontSizeOverride("font_size", 20);
		btn.AddThemeColorOverride("font_color", new Color(0.95f, 0.90f, 0.82f));
		btn.AddThemeColorOverride("font_hover_color", UIStyle.ColorGold);

		if (onClick != null)
		{
			btn.Pressed += onClick;
		}

		var lbl = new Label();
		lbl.Name = "ToolCardLabel";
		lbl.Text = labelText;
		lbl.HorizontalAlignment = HorizontalAlignment.Center;
		lbl.AddThemeFontSizeOverride("font_size", 10);
		lbl.AddThemeColorOverride("font_color", new Color(0.88f, 0.84f, 0.78f));

		var card = new VBoxContainer();
		card.Name = "ToolCard_" + btn.Name;
		card.AddThemeConstantOverride("separation", 3);
		card.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
		card.Alignment = BoxContainer.AlignmentMode.Center;

		if (btn.GetParent() != null)
		{
			btn.GetParent().RemoveChild(btn);
		}
		card.AddChild(btn);
		card.AddChild(lbl);

		return card;
	}

	private void RestructurePanelLayouts()
	{
		// 1. File Panel
		var targetFile = GetContentTarget(_contentFile);
		if (targetFile != null)
		{
			var saveLoadRow = new HBoxContainer();
			saveLoadRow.Name = "RowFileSaveLoad";
			saveLoadRow.AddThemeConstantOverride("separation", 6);
			saveLoadRow.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;

			_btnLoad.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
			_btnLoad.SizeFlagsStretchRatio = 1.0f;

			_btnSave.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
			_btnSave.SizeFlagsStretchRatio = 1.0f;

			_btnSaveAs.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
			_btnSaveAs.SizeFlagsStretchRatio = 1.0f;

			SafeReparent(_btnLoad, saveLoadRow);
			SafeReparent(_btnSave, saveLoadRow);
			SafeReparent(_btnSaveAs, saveLoadRow);

			var fileGrid1 = new GridContainer();
			fileGrid1.Columns = 2;
			fileGrid1.AddThemeConstantOverride("h_separation", 6);
			fileGrid1.AddThemeConstantOverride("v_separation", 6);
			fileGrid1.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;

			SafeReparent(_btnTestMap, fileGrid1);
			SafeReparent(_btnExportMap, fileGrid1);
			SafeReparent(_btnPublish, fileGrid1);
			SafeReparent(_btnAuthorSignature, fileGrid1);

			var fileGrid2 = new GridContainer();
			fileGrid2.Columns = 2;
			fileGrid2.AddThemeConstantOverride("h_separation", 6);
			fileGrid2.AddThemeConstantOverride("v_separation", 6);
			fileGrid2.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;

			SafeReparent(_btnGenerateMap, fileGrid2);
			SafeReparent(_btnImportMinimap, fileGrid2);
			SafeReparent(_btnResetMap, fileGrid2);

			var fileBox1 = new VBoxContainer();
			fileBox1.Name = "BoxFileOps";
			fileBox1.AddThemeConstantOverride("separation", 6);
			fileBox1.AddChild(saveLoadRow);
			fileBox1.AddChild(fileGrid1);
			StyleSubContainer(fileBox1, "File Operations");

			var fileBox2 = new VBoxContainer();
			fileBox2.Name = "BoxGenOps";
			fileBox2.AddChild(fileGrid2);
			StyleSubContainer(fileBox2, "Generation & Imports");

			targetFile.AddChild(fileBox1);
			targetFile.AddChild(fileBox2);
		}

		// 2. Viewport Panel
		var targetViewport = GetContentTarget(_contentViewport);
		if (targetViewport != null)
		{
			if (_minimapFrame != null)
			{
				SafeReparent(_minimapFrame, targetViewport);
			}

			var vpRow1 = new HBoxContainer();
			vpRow1.Name = "ViewportIconRow1";
			vpRow1.AddThemeConstantOverride("separation", 4);
			vpRow1.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;

			StyleIconButton(_btnToggleGrid, "\uf84c", "Overlay");
			StyleIconButton(_btnToggleEnvironment, "\uf185", "Environment");
			StyleIconButton(_btnToggleCamera, "\uf030", "Configure camera controls (R / C / + / - / F8)");
			StyleIconButton(_btnTapeMeasure, "\uf545", "Tape measure distance & slope tool (U)");

			SafeReparent(_btnToggleGrid, vpRow1);
			SafeReparent(_btnToggleEnvironment, vpRow1);
			SafeReparent(_btnToggleCamera, vpRow1);
			SafeReparent(_btnTapeMeasure, vpRow1);

			var vpBox = new VBoxContainer();
			vpBox.Name = "BoxViewportToolbar";
			vpBox.AddThemeConstantOverride("separation", 4);
			vpBox.AddChild(vpRow1);
			StyleSubContainer(vpBox, "Navigation Bar");

			_rowPolarConfig = new HBoxContainer();
			_rowPolarConfig.Name = "RowPolarConfig";
			_rowPolarConfig.AddThemeConstantOverride("separation", 4);
			_rowPolarConfig.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;

			SafeReparent(_optPolarRingSpacing, _rowPolarConfig);
			SafeReparent(_optPolarRadialStep, _rowPolarConfig);
			SafeReparent(_btnResetPivotToCenter, _rowPolarConfig);

			if (_optPolarRingSpacing != null)
			{
				_optPolarRingSpacing.CustomMinimumSize = new Vector2(0, 26);
				_optPolarRingSpacing.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
				_optPolarRingSpacing.AddThemeFontSizeOverride("font_size", 9);
			}
			if (_optPolarRadialStep != null)
			{
				_optPolarRadialStep.CustomMinimumSize = new Vector2(0, 26);
				_optPolarRadialStep.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
				_optPolarRadialStep.AddThemeFontSizeOverride("font_size", 9);
			}
			if (_btnResetPivotToCenter != null)
			{
				_btnResetPivotToCenter.CustomMinimumSize = new Vector2(0, 26);
				_btnResetPivotToCenter.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
				_btnResetPivotToCenter.AddThemeFontSizeOverride("font_size", 9);
			}

			bool isPolarMode = GameHost.Instance != null &&
				(GameHost.Instance.EditorMirrorMode == MirrorMode.Rotational || GameHost.Instance.EditorGridMode == GameHost.GridOverlayMode.Polar || GameHost.Instance.EditorGridMode == GameHost.GridOverlayMode.Both);
			_rowPolarConfig.Visible = isPolarMode;

			targetViewport.AddChild(vpBox);
			targetViewport.AddChild(_rowPolarConfig);
		}

		// 3. Terrain Tools
		if (_panelTerrainVBox != null)
		{
			var terrainGrid = new GridContainer();
			terrainGrid.Columns = 3;
			terrainGrid.AddThemeConstantOverride("h_separation", 6);
			terrainGrid.AddThemeConstantOverride("v_separation", 8);
			terrainGrid.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;

			SafeReparent(_cardRaise ?? (Control)_btnRaise, terrainGrid);
			SafeReparent(_cardLower ?? (Control)_btnLower, terrainGrid);
			SafeReparent(_cardHeight ?? (Control)_btnHeight, terrainGrid);
			SafeReparent(_cardSmooth ?? (Control)_btnSmooth, terrainGrid);
			SafeReparent(_cardPlateau ?? (Control)_btnPlateau, terrainGrid);
			SafeReparent(_cardRamp ?? (Control)_btnRamp, terrainGrid);
			SafeReparent(_cardNoise ?? (Control)_btnNoise, terrainGrid);
			SafeReparent(_cardWater ?? (Control)_btnWater, terrainGrid);

			_panelTerrainVBox.AddChild(terrainGrid);
			StyleSubContainer(_panelTerrainVBox, "Terrain Elevation");
		}

		// 4. Deco Tools
		if (_panelDecoVBox != null)
		{
			var decoGrid = new GridContainer();
			decoGrid.Columns = 3;
			decoGrid.AddThemeConstantOverride("h_separation", 6);
			decoGrid.AddThemeConstantOverride("v_separation", 8);
			decoGrid.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;

			SafeReparent(_cardTextureBrush ?? (Control)_btnTextureBrush, decoGrid);
			SafeReparent(_cardFloodFill ?? (Control)_btnFloodFill, decoGrid);

			_panelDecoVBox.AddChild(decoGrid);
			StyleSubContainer(_panelDecoVBox, "Texture Actions");
		}

		// 5. Pathing Tools
		if (_panelPathingVBox != null)
		{
			var pathGrid = new GridContainer();
			pathGrid.Columns = 3;
			pathGrid.AddThemeConstantOverride("h_separation", 6);
			pathGrid.AddThemeConstantOverride("v_separation", 8);
			pathGrid.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;

			SafeReparent(_cardPathingBrush ?? (Control)_btnPathingBrush, pathGrid);
			SafeReparent(_cardFloodFillPathing ?? (Control)_btnFloodFillPathing, pathGrid);

			_panelPathingVBox.AddChild(pathGrid);
			StyleSubContainer(_panelPathingVBox, "Pathing Actions");
		}

		// 6. Object Tools
		if (_panelObjects != null)
		{
			var objGrid = new GridContainer();
			objGrid.Columns = 3;
			objGrid.AddThemeConstantOverride("h_separation", 6);
			objGrid.AddThemeConstantOverride("v_separation", 8);
			objGrid.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;

			SafeReparent(_cardSelectMove ?? (Control)_btnSelectMove, objGrid);
			SafeReparent(_cardAddObject ?? (Control)_btnAddObject, objGrid);
			SafeReparent(_cardDeleteObject ?? (Control)_btnDeleteObject, objGrid);

			_panelObjects.AddChild(objGrid);
			StyleSubContainer(_panelObjects, "Object Placement");
		}

		// 7. Clipboard Tools
		if (_panelClipboard != null)
		{
			var clipGrid = new GridContainer();
			clipGrid.Columns = 3;
			clipGrid.AddThemeConstantOverride("h_separation", 6);
			clipGrid.AddThemeConstantOverride("v_separation", 8);
			clipGrid.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;

			SafeReparent(_cardSelectArea ?? (Control)_btnSelectArea, clipGrid);
			SafeReparent(_cardCut ?? (Control)_btnCut, clipGrid);
			SafeReparent(_cardCopy ?? (Control)_btnCopy, clipGrid);
			SafeReparent(_cardPaste ?? (Control)_btnPaste, clipGrid);
			SafeReparent(_cardEraseArea ?? (Control)_btnEraseArea, clipGrid);

			_panelClipboard.AddChild(clipGrid);
			StyleSubContainer(_panelClipboard, "Clipboard Actions");
		}

		// 8. Inspector Transform
		var targetInspector = GetContentTarget(_contentInspector);
		if (targetInspector != null)
		{
			var inspGrid = new GridContainer();
			inspGrid.Columns = 2;
			inspGrid.AddThemeConstantOverride("h_separation", 6);
			inspGrid.AddThemeConstantOverride("v_separation", 6);
			inspGrid.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;

			SafeReparent(_btnInspectorRotLeft, inspGrid);
			SafeReparent(_btnInspectorRotRight, inspGrid);
			SafeReparent(_btnInspectorScaleDown, inspGrid);
			SafeReparent(_btnInspectorScaleUp, inspGrid);
			SafeReparent(_btnInspectorScaleReset, inspGrid);
			SafeReparent(_btnInspectorDelete, inspGrid);

			var inspBox = new VBoxContainer();
			inspBox.AddChild(inspGrid);
			StyleSubContainer(inspBox, "Transform Controls");
			targetInspector.AddChild(inspBox);
		}

		// 9. Global Brush Properties Panel
		if (_contentBrush != null)
		{
			StyleSubContainer(_contentBrush, "Brush Properties");

			StyleValueBadge(_lblBrushSizeValue);
			StyleValueBadge(_lblBrushStrengthValue);
			StyleValueBadge(_lblBlockStepValue);

			var shapeMirrorGrid = _contentBrush.GetNodeOrNull<GridContainer>("ShapeMirrorGrid");
			if (shapeMirrorGrid == null && _btnBrushShape != null && _optMirrorMode != null)
			{
				shapeMirrorGrid = new GridContainer();
				shapeMirrorGrid.Name = "ShapeMirrorGrid";
				shapeMirrorGrid.Columns = 2;
				shapeMirrorGrid.AddThemeConstantOverride("h_separation", 6);
				shapeMirrorGrid.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;

				SafeReparent(_btnBrushShape, shapeMirrorGrid);
				SafeReparent(_optMirrorMode, shapeMirrorGrid);

				var strengthBox = _contentBrush.GetNodeOrNull<Control>("BrushStrengthBox");
				int insertIdx = strengthBox != null ? strengthBox.GetIndex() + 1 : 2;
				_contentBrush.AddChild(shapeMirrorGrid);
				_contentBrush.MoveChild(shapeMirrorGrid, insertIdx);
			}

			if (_btnBrushShape != null)
			{
				_btnBrushShape.CustomMinimumSize = new Vector2(0, 32);
				_btnBrushShape.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
				_btnBrushShape.AddThemeFontSizeOverride("font_size", 10);
			}
			if (_optMirrorMode != null)
			{
				_optMirrorMode.CustomMinimumSize = new Vector2(0, 32);
				_optMirrorMode.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
				_optMirrorMode.AddThemeFontSizeOverride("font_size", 10);
			}
		}

		// 10. Tool Settings Panel
		if (_containerTextureSettings != null)
		{
			StyleSubContainer(_containerTextureSettings, "Texture Palette & Settings");

			if (_rowGroundTexture != null)
			{
				var groundStyle = new StyleBoxFlat();
				groundStyle.BgColor = new Color(0.12f, 0.16f, 0.20f, 0.85f);
				groundStyle.BorderColor = new Color(0.20f, 0.65f, 0.95f, 0.85f); // Blue Ground accent
				groundStyle.SetBorderWidthAll(1);
				groundStyle.CornerRadiusTopLeft = 4;
				groundStyle.CornerRadiusTopRight = 4;
				groundStyle.CornerRadiusBottomLeft = 4;
				groundStyle.CornerRadiusBottomRight = 4;
				groundStyle.ContentMarginLeft = 8;
				groundStyle.ContentMarginRight = 8;
				groundStyle.ContentMarginTop = 4;
				groundStyle.ContentMarginBottom = 4;

				var panelGround = _rowGroundTexture.GetNodeOrNull<Panel>("RowBG");
				if (panelGround == null)
				{
					panelGround = new Panel();
					panelGround.Name = "RowBG";
					panelGround.ShowBehindParent = true;
					panelGround.MouseFilter = Control.MouseFilterEnum.Ignore;
					panelGround.SetAnchorsPreset(Control.LayoutPreset.FullRect);
					_rowGroundTexture.AddChild(panelGround);
					_rowGroundTexture.MoveChild(panelGround, 0);
				}
				panelGround.AddThemeStyleboxOverride("panel", groundStyle);
			}

			if (_rowCliffTexture != null)
			{
				var cliffStyle = new StyleBoxFlat();
				cliffStyle.BgColor = new Color(0.18f, 0.14f, 0.10f, 0.85f);
				cliffStyle.BorderColor = new Color(0.95f, 0.55f, 0.15f, 0.85f); // Orange Cliff accent
				cliffStyle.SetBorderWidthAll(1);
				cliffStyle.CornerRadiusTopLeft = 4;
				cliffStyle.CornerRadiusTopRight = 4;
				cliffStyle.CornerRadiusBottomLeft = 4;
				cliffStyle.CornerRadiusBottomRight = 4;
				cliffStyle.ContentMarginLeft = 8;
				cliffStyle.ContentMarginRight = 8;
				cliffStyle.ContentMarginTop = 4;
				cliffStyle.ContentMarginBottom = 4;

				var panelCliff = _rowCliffTexture.GetNodeOrNull<Panel>("RowBG");
				if (panelCliff == null)
				{
					panelCliff = new Panel();
					panelCliff.Name = "RowBG";
					panelCliff.ShowBehindParent = true;
					panelCliff.MouseFilter = Control.MouseFilterEnum.Ignore;
					panelCliff.SetAnchorsPreset(Control.LayoutPreset.FullRect);
					_rowCliffTexture.AddChild(panelCliff);
					_rowCliffTexture.MoveChild(panelCliff, 0);
				}
				panelCliff.AddThemeStyleboxOverride("panel", cliffStyle);
			}

			if (_lblTerrainTexture != null)
			{
				_lblTerrainTexture.AddThemeFontSizeOverride("font_size", 11);
				_lblTerrainTexture.AddThemeColorOverride("font_color", new Color(0.40f, 0.80f, 1.0f));
			}

			if (_lblCliffTexture != null)
			{
				_lblCliffTexture.AddThemeFontSizeOverride("font_size", 11);
				_lblCliffTexture.AddThemeColorOverride("font_color", new Color(1.0f, 0.70f, 0.30f));
			}
		}

		if (_containerPathingSettings != null)
		{
			StyleSubContainer(_containerPathingSettings, "Pathing Properties");
		}

		if (_containerPasteSettings != null)
		{
			StyleSubContainer(_containerPasteSettings, "Paste Options");
			StyleValueBadge(_lblPasteRotation);
			if (_btnClipboardBrushShape != null)
			{
				_btnClipboardBrushShape.CustomMinimumSize = new Vector2(0, 30);
				_btnClipboardBrushShape.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
				_btnClipboardBrushShape.AddThemeFontSizeOverride("font_size", 10);
			}
			if (_optClipboardMirrorMode != null)
			{
				_optClipboardMirrorMode.CustomMinimumSize = new Vector2(0, 30);
				_optClipboardMirrorMode.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
				_optClipboardMirrorMode.AddThemeFontSizeOverride("font_size", 10);
			}
			if (_btnPasteReflection != null)
			{
				_btnPasteReflection.CustomMinimumSize = new Vector2(0, 30);
				_btnPasteReflection.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
			}
			if (_btnPasteAnchor != null)
			{
				_btnPasteAnchor.CustomMinimumSize = new Vector2(0, 30);
				_btnPasteAnchor.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
			}
		}

		if (_contentPlacement != null)
		{
			if (_optPlacementMirrorMode != null)
			{
				_optPlacementMirrorMode.CustomMinimumSize = new Vector2(0, 30);
				_optPlacementMirrorMode.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
				_optPlacementMirrorMode.AddThemeFontSizeOverride("font_size", 10);
			}
		}

		if (_containerEyedropperSettings != null)
		{
			StyleSubContainer(_containerEyedropperSettings, "Eyedropper Settings");
		}

		// 11. Apply Procedural RTS Button Style to all Option Panel Buttons
		Control[] allOptionContents = new Control[]
		{
			_contentFile, _contentViewport, _contentTool,
			_contentBrush, _contentToolSettings, _contentPlacement, _contentInspector, _contentLightingTuning
		};
		foreach (var content in allOptionContents)
		{
			StyleOptionButtonsInContainer(content);
		}
	}

	private void MakeCardDraggable(Control cardNode, Button headerBtn, Control contentControl = null, string titleText = null)
	{
		if (cardNode == null || headerBtn == null) return;

		var data = new CardDragData
		{
			CardNode = cardNode,
			HeaderButton = headerBtn,
			ContentControl = contentControl,
			TitleText = titleText
		};
		_cardDragMap[cardNode] = data;

		headerBtn.GuiInput += (@event) =>
		{
			if (@event is InputEventMouseButton mb && mb.ButtonIndex == MouseButton.Left)
			{
				if (mb.Pressed)
				{
					if (mb.DoubleClick)
					{
						data.IsDragging = false;
						data.HasMovedSincePress = false;
						ResetSingleCardPosition(cardNode);
						ShowFeedback(TranslationServer.Translate("Card position reset to panel layout."));
						return;
					}
					data.IsDragging = true;
					data.HasMovedSincePress = false;
					data.DragStartMousePos = mb.GlobalPosition;
					data.CardStartPos = cardNode.GlobalPosition;
				}
				else
				{
					if (data.IsDragging)
					{
						bool moved = data.HasMovedSincePress;
						data.IsDragging = false;
						data.HasMovedSincePress = false;

						if (!moved && contentControl != null)
						{
							ToggleAccordionState(headerBtn, contentControl, titleText);
						}
					}
				}
			}
			else if (@event is InputEventMouseMotion mm && data.IsDragging)
			{
				Vector2 delta = mm.GlobalPosition - data.DragStartMousePos;
				if (!data.HasMovedSincePress && delta.LengthSquared() > 16.0f)
				{
					data.HasMovedSincePress = true;
					if (!cardNode.TopLevel)
					{
						cardNode.TopLevel = true;
						cardNode.GlobalPosition = data.CardStartPos;
					}
					cardNode.MoveToFront();
				}

				if (data.HasMovedSincePress)
				{
					Vector2 targetPos = data.CardStartPos + delta;
					Vector2 viewportSize = GetViewportRect().Size;
					float maxX = Mathf.Max(0, viewportSize.X - cardNode.Size.X);
					float maxY = Mathf.Max(0, viewportSize.Y - cardNode.Size.Y);

					cardNode.GlobalPosition = new Vector2(
						Mathf.Clamp(targetPos.X, 0, maxX),
						Mathf.Clamp(targetPos.Y, 0, maxY)
					);
				}
			}
		};
	}

	private void ExpandAccordion(Button headerBtn, Control contentControl, string titleText)
	{
		if (headerBtn == null || contentControl == null) return;
		if (contentControl.Visible) return;
		ToggleAccordionState(headerBtn, contentControl, titleText);
	}

	private void ToggleAccordionState(Button headerBtn, Control contentControl, string titleText)
	{
		if (headerBtn == null || contentControl == null) return;
		contentControl.Visible = !contentControl.Visible;
		if (!string.IsNullOrEmpty(titleText))
		{
			string upperTitle = TranslationServer.Translate(titleText).ToString().ToUpperInvariant();
			headerBtn.Text = upperTitle + (contentControl.Visible ? "  \uf0d7" : "  \uf0da");
		}

		var cardParent = headerBtn.GetParent() as Control;
		if (cardParent != null)
		{
			cardParent.ForceUpdateTransform();
			(cardParent as Container)?.QueueSort();

			var sidebarContainer = cardParent.GetParent() as Container;
			if (sidebarContainer != null)
			{
				sidebarContainer.ForceUpdateTransform();
				sidebarContainer.QueueSort();
			}
		}

		var leftVBox = GetNodeOrNull<VBoxContainer>("LeftSlidePanel/LeftScroll/LeftVBox");
		leftVBox?.ForceUpdateTransform();
		leftVBox?.QueueSort();

		var rightVBox = GetNodeOrNull<VBoxContainer>("RightSlidePanel/RightScroll/AccordionContainer");
		rightVBox?.ForceUpdateTransform();
		rightVBox?.QueueSort();

		UIManager.Instance?.PlayClickSound();
	}

	private void ResetSingleCardPosition(Control cardNode)
	{
		if (cardNode == null) return;
		if (_cardDragMap.TryGetValue(cardNode, out var data))
		{
			data.IsDragging = false;
			data.HasMovedSincePress = false;
		}
		cardNode.TopLevel = false;
		cardNode.Position = Vector2.Zero;

		var parentContainer = cardNode.GetParent() as Container;
		if (parentContainer != null)
		{
			parentContainer.QueueSort();
		}
		var leftVBox = GetNodeOrNull<VBoxContainer>("LeftSlidePanel/LeftScroll/LeftVBox");
		leftVBox?.QueueSort();

		var rightVBox = GetNodeOrNull<VBoxContainer>("RightSlidePanel/RightScroll/AccordionContainer");
		rightVBox?.QueueSort();
	}

	public void ResetAllPanelPositions()
	{
		ResetSingleCardPosition(_accordionFile);
		ResetSingleCardPosition(_accordionViewport);
		ResetSingleCardPosition(_accordionTool);
		ResetSingleCardPosition(_accordionBrush);
		ResetSingleCardPosition(_accordionToolSettings);
		ResetSingleCardPosition(_accordionPlacement);
		ResetSingleCardPosition(_accordionInspector);

		var leftVBox = GetNodeOrNull<VBoxContainer>("LeftSlidePanel/LeftScroll/LeftVBox");
		leftVBox?.QueueSort();

		var rightVBox = GetNodeOrNull<VBoxContainer>("RightSlidePanel/RightScroll/AccordionContainer");
		rightVBox?.QueueSort();

		ShowFeedback(TranslationServer.Translate("All panel positions reset to default layout."));
	}

	private void ToggleLeftPanel()
	{
		SetLeftPanelExpanded(!_leftPanelExpanded);
	}

	private void ToggleRightPanel()
	{
		SetRightPanelExpanded(!_rightPanelExpanded);
	}

	private void SetLeftPanelExpanded(bool expand)
	{
		_leftPanelExpanded = expand;
		var tween = CreateTween();
		float targetLeft = expand ? 0.0f : -260.0f;
		float targetRight = expand ? 260.0f : 0.0f;
		tween.TweenProperty(_panelLeft, "offset_left", targetLeft, 0.2f).SetTrans(Tween.TransitionType.Quad).SetEase(Tween.EaseType.Out);
		tween.TweenProperty(_panelLeft, "offset_right", targetRight, 0.2f).SetTrans(Tween.TransitionType.Quad).SetEase(Tween.EaseType.Out);
		_btnLeftTab.Text = expand ? "◀" : "▶";
	}

	private void SetRightPanelExpanded(bool expand)
	{
		_rightPanelExpanded = expand;
		var tween = CreateTween();
		float targetLeft = expand ? -300.0f : 0.0f;
		float targetRight = expand ? 0.0f : 300.0f;
		tween.TweenProperty(_panelRight, "offset_left", targetLeft, 0.2f).SetTrans(Tween.TransitionType.Quad).SetEase(Tween.EaseType.Out);
		tween.TweenProperty(_panelRight, "offset_right", targetRight, 0.2f).SetTrans(Tween.TransitionType.Quad).SetEase(Tween.EaseType.Out);
		_btnRightTab.Text = expand ? "▶" : "◀";
	}

	private void SafeReparent(Node node, Node newParent)
	{
		if (node == null || newParent == null) return;
		var oldParent = node.GetParent();
		if (oldParent == newParent) return;
		oldParent?.RemoveChild(node);
		newParent.AddChild(node);
	}

	private void UpdateSidebarMorph(GameHost.EditorTool tool)
	{
		if (_panelRight == null) return;

		bool isClumpActive = _chkClumpMode != null && _chkClumpMode.ButtonPressed;
		bool isBrush = (tool == GameHost.EditorTool.Raise ||
					   tool == GameHost.EditorTool.Lower ||
					   tool == GameHost.EditorTool.Height ||
					   tool == GameHost.EditorTool.Smooth ||
					   tool == GameHost.EditorTool.Plateau ||
					   tool == GameHost.EditorTool.Ramp ||
					   tool == GameHost.EditorTool.PaintTexture ||
					   tool == GameHost.EditorTool.Noise ||
					   tool == GameHost.EditorTool.PaintPathing ||
					   tool == GameHost.EditorTool.FloodFillPathing ||
					   tool == GameHost.EditorTool.PlacePropClump ||
					   ((tool == GameHost.EditorTool.PlaceUnit || tool == GameHost.EditorTool.PlaceProp || tool == GameHost.EditorTool.PlaceDecal) && isClumpActive))
					   && tool != GameHost.EditorTool.Water;

		if (_accordionBrush != null)
		{
			_accordionBrush.Visible = isBrush;
			UpdateBrushStrengthVisibility();
			bool isTextureMode = tool == GameHost.EditorTool.PaintTexture;
			if (_chkBlockMode != null)
			{
				_chkBlockMode.Visible = (tool != GameHost.EditorTool.PaintPathing && 
										 tool != GameHost.EditorTool.FloodFillPathing &&
										 !isTextureMode &&
										 tool != GameHost.EditorTool.Smooth &&
										 tool != GameHost.EditorTool.Noise &&
										 tool != GameHost.EditorTool.Ramp &&
										 tool != GameHost.EditorTool.PlacePropClump &&
										 tool != GameHost.EditorTool.Height &&
										 !isClumpActive);
			}
			UpdateBlockStepVisibility();
		}

		if (_accordionWater != null)
		{
			_accordionWater.Visible = (tool == GameHost.EditorTool.Water);
		}

		bool isBlockModeActive = (_chkBlockMode != null && _chkBlockMode.Visible && _chkBlockMode.ButtonPressed) || (GameHost.Instance != null && GameHost.Instance.EditorBlockMode);
		bool isPaintTool = tool == GameHost.EditorTool.PaintTexture ||
						   tool == GameHost.EditorTool.FloodFill;
		bool isBlockHeightTool = (isBlockModeActive && (
						   tool == GameHost.EditorTool.Raise ||
						   tool == GameHost.EditorTool.Lower ||
						   tool == GameHost.EditorTool.Plateau)) ||
						   tool == GameHost.EditorTool.Height;

		bool isRampTool = tool == GameHost.EditorTool.Ramp;

		bool texSettingsVisible = _containerTextureSettings != null && (isPaintTool || isBlockHeightTool || isRampTool);
		if (_containerTextureSettings != null) _containerTextureSettings.Visible = texSettingsVisible;

		bool isTexturePaintToolOnly = tool == GameHost.EditorTool.PaintTexture;

		if (_chkApplyGroundTexture != null) _chkApplyGroundTexture.Visible = texSettingsVisible && isTexturePaintToolOnly;
		if (_chkApplyCliffTexture != null) _chkApplyCliffTexture.Visible = texSettingsVisible && isTexturePaintToolOnly;

		bool pathingSettingsVisible = _containerPathingSettings != null && (tool == GameHost.EditorTool.PaintPathing || tool == GameHost.EditorTool.FloodFillPathing);
		if (_containerPathingSettings != null) _containerPathingSettings.Visible = pathingSettingsVisible;

		bool eyedropperSettingsVisible = _containerEyedropperSettings != null && (tool == GameHost.EditorTool.Eyedropper);
		if (_containerEyedropperSettings != null) _containerEyedropperSettings.Visible = eyedropperSettingsVisible;

		bool pasteSettingsVisible = _containerPasteSettings != null && (tool == GameHost.EditorTool.SelectArea || tool == GameHost.EditorTool.PasteArea);
		if (_containerPasteSettings != null) _containerPasteSettings.Visible = pasteSettingsVisible;

		bool isPlacement = (tool == GameHost.EditorTool.PlaceUnit ||
							tool == GameHost.EditorTool.PlaceProp ||
							tool == GameHost.EditorTool.PlacePropClump ||
							tool == GameHost.EditorTool.PlaceDecal ||
							tool == GameHost.EditorTool.PlaceVfx);
		
		bool categorySelectorVisible = _containerCategorySelector != null && isPlacement;
		if (_containerCategorySelector != null) _containerCategorySelector.Visible = categorySelectorVisible;

		bool anyToolSettingVisible = texSettingsVisible ||
									 pathingSettingsVisible ||
									 eyedropperSettingsVisible ||
									 pasteSettingsVisible ||
									 categorySelectorVisible;

		if (_accordionToolSettings != null)
		{
			_accordionToolSettings.Visible = anyToolSettingVisible;
		}

		bool hasPlacementConfig = isPlacement;
		if (_accordionPlacement != null)
		{
			_accordionPlacement.Visible = hasPlacementConfig;
		}
		if (_chkClumpMode != null)
		{
			_chkClumpMode.Visible = isPlacement;
			if (_spacingBox != null) _spacingBox.Visible = isPlacement && _chkClumpMode.ButtonPressed;
			if (_densityBox != null) _densityBox.Visible = isPlacement && _chkClumpMode.ButtonPressed;
			if (_scaleVarBox != null) _scaleVarBox.Visible = isPlacement && _chkClumpMode.ButtonPressed;
		}

		bool hasSelectedObject = GameHost.Instance != null && GodotObject.IsInstanceValid(GameHost.Instance.SelectedEditorObject);
		if (_accordionInspector != null)
		{
			_accordionInspector.Visible = hasSelectedObject && (tool == GameHost.EditorTool.SelectMove);
		}

		if (_accordionContainer != null)
		{
			_accordionContainer.QueueSort();
		}
	}

	public override void _UnhandledInput(InputEvent @event)
	{
		if (@event is InputEventKey keyEvent && keyEvent.Pressed && !keyEvent.Echo)
		{
			if (keyEvent.Keycode == Godot.Key.F1)
			{
				SwitchModule(EditorModule.Terrain);
				GetViewport().SetInputAsHandled();
			}
			else if (keyEvent.Keycode == Godot.Key.F2)
			{
				SwitchModule(EditorModule.TextureDeco);
				GetViewport().SetInputAsHandled();
			}
			else if (keyEvent.Keycode == Godot.Key.F3)
			{
				SwitchModule(EditorModule.Pathing);
				GetViewport().SetInputAsHandled();
			}
			else if (keyEvent.Keycode == Godot.Key.F4)
			{
				SwitchModule(EditorModule.Objects);
				GetViewport().SetInputAsHandled();
			}
			else if (keyEvent.Keycode == Godot.Key.F5)
			{
				SwitchModule(EditorModule.Coordinates);
				GetViewport().SetInputAsHandled();
			}
			else if (keyEvent.Keycode == Godot.Key.F6)
			{
				SwitchModule(EditorModule.Clipboard);
				GetViewport().SetInputAsHandled();
			}
			else if (keyEvent.Keycode == Godot.Key.F7)
			{
				if (GameHost.Instance != null && GameHost.Instance.GroundTerrain != null)
				{
					GameHost.Instance.GroundTerrain.ToggleWireframeMode();
					bool isWireframe = GetViewport()?.DebugDraw == Viewport.DebugDrawEnum.Wireframe;
					UpdateWireframeOverlayExternal(isWireframe);
				}
				GetViewport().SetInputAsHandled();
			}
			else if (keyEvent.Keycode == Godot.Key.F8)
			{
				ToggleFreeCamera();
				GetViewport().SetInputAsHandled();
			}
			else if (keyEvent.Keycode == Godot.Key.F9)
			{
				ToggleShadows();
				GetViewport().SetInputAsHandled();
			}
		}
	}

	private void RotateSelectedObject(float angleDelta)
	{
		if (GameHost.Instance == null) return;
		var selected = GameHost.Instance.SelectedEditorObject;
		if (GodotObject.IsInstanceValid(selected))
		{
			var node3D = selected as Node3D;
			Vector3 oldRot = node3D.RotationDegrees;
			Vector3 newRot = oldRot;
			newRot.Y = (newRot.Y + angleDelta + 360.0f) % 360.0f;
			bool isUnit = selected is Unit3D;
			bool isEnemy = isUnit ? (selected as Unit3D).IsEnemy : false;
			var action = new ObjectTransformAction(
				node3D,
				node3D.Position, node3D.Position,
				oldRot, newRot,
				node3D.Scale, node3D.Scale,
				isEnemy, isEnemy
			);
			node3D.RotationDegrees = newRot;
			if (selected is Prop3D propRot)
			{
				PropMultiMeshManager.Instance?.MarkDirty(propRot.PropId);
			}
			EditorHistoryManager.RecordAction(action);
			UpdateSelectedObjectInfo();
			ShowFeedback(string.Format(TranslationServer.Translate("Rotated Object to {0}°"), newRot.Y));
		}
	}

	private void ScaleSelectedObject(float scaleDelta)
	{
		if (GameHost.Instance == null) return;
		var selected = GameHost.Instance.SelectedEditorObject;
		if (GodotObject.IsInstanceValid(selected))
		{
			var node3D = selected as Node3D;
			Vector3 oldScale = node3D.Scale;
			float newScaleVal = Mathf.Clamp(oldScale.X + scaleDelta, 0.2f, 4.0f);
			Vector3 newScale = Vector3.One * newScaleVal;
			bool isUnit = selected is Unit3D;
			bool isEnemy = isUnit ? (selected as Unit3D).IsEnemy : false;
			var action = new ObjectTransformAction(
				node3D,
				node3D.Position, node3D.Position,
				node3D.RotationDegrees, node3D.RotationDegrees,
				oldScale, newScale,
				isEnemy, isEnemy
			);
			node3D.Scale = newScale;
			if (selected is Prop3D propScale)
			{
				PropMultiMeshManager.Instance?.MarkDirty(propScale.PropId);
			}
			EditorHistoryManager.RecordAction(action);
			UpdateSelectedObjectInfo();
			ShowFeedback(string.Format(TranslationServer.Translate("Scaled Object to {0:F1}x"), newScaleVal));
		}
	}

	private void ResetScaleSelectedObject()
	{
		if (GameHost.Instance == null) return;
		var selected = GameHost.Instance.SelectedEditorObject;
		if (GodotObject.IsInstanceValid(selected))
		{
			var node3D = selected as Node3D;
			Vector3 oldScale = node3D.Scale;
			Vector3 newScale = Vector3.One;
			bool isUnit = selected is Unit3D;
			bool isEnemy = isUnit ? (selected as Unit3D).IsEnemy : false;
			var action = new ObjectTransformAction(
				node3D,
				node3D.Position, node3D.Position,
				node3D.RotationDegrees, node3D.RotationDegrees,
				oldScale, newScale,
				isEnemy, isEnemy
			);
			node3D.Scale = newScale;
			if (selected is Prop3D propReset)
			{
				PropMultiMeshManager.Instance?.MarkDirty(propReset.PropId);
			}
			EditorHistoryManager.RecordAction(action);
			UpdateSelectedObjectInfo();
			ShowFeedback(TranslationServer.Translate("Reset Object scale to 1.0x"));
		}
	}

	public void LocateSelectedObjectAction()
	{
		if (GameHost.Instance == null) return;
		var selected = GameHost.Instance.SelectedEditorObject;
		if (GodotObject.IsInstanceValid(selected) && selected is Node3D node3D)
		{
			(GameHost.Instance.MainCamera as CameraControl)?.FocusOnPosition(node3D.Position);
			ShowFeedback(string.Format(TranslationServer.Translate("Focused camera on {0}"), selected.Name));
		}
		else
		{
			ShowFeedback(TranslationServer.Translate("No object selected to locate"));
		}
	}



	private void SetupMinimap()
	{
		MinimapHelper.SetupMinimapBackground(_minimapArea);
	}

	public void RegenerateMinimap()
	{
		_minimapController?.RegenerateMinimap();
	}

	public void SetScaleDialogTargets(int width, int depth)
	{
		_scaleDialogTargetWidth = width;
		_scaleDialogTargetDepth = depth;
	}

	public void OpenScaleMapDialog()
	{
		if (_scaleMapDialog != null) return;

		GameHost.Instance?.ShowScaleMapSilhouette(_scaleDialogTargetWidth, _scaleDialogTargetDepth);

		_scaleMapDialog = new PanelContainer();
		_scaleMapDialog.Name = "ScaleMapDialog";
		_scaleMapDialog.AddThemeStyleboxOverride("panel", UIStyle.CreateStonePanel());
		_scaleMapDialog.SetAnchorsPreset(Control.LayoutPreset.Center);
		_scaleMapDialog.CustomMinimumSize = new Vector2(320, 0);
		_scaleMapDialog.GrowHorizontal = Control.GrowDirection.Both;
		_scaleMapDialog.GrowVertical = Control.GrowDirection.Both;
		_scaleMapDialog.ZIndex = 1000;
		AddChild(_scaleMapDialog);
		_scaleMapDialog.MoveToFront();

		var margin = new MarginContainer();
		margin.AddThemeConstantOverride("margin_left", 16);
		margin.AddThemeConstantOverride("margin_right", 16);
		margin.AddThemeConstantOverride("margin_top", 14);
		margin.AddThemeConstantOverride("margin_bottom", 14);
		_scaleMapDialog.AddChild(margin);

		var innerVBox = new VBoxContainer();
		innerVBox.AddThemeConstantOverride("separation", 12);
		margin.AddChild(innerVBox);

		var titleLabel = new Label();
		titleLabel.Text = TranslationServer.Translate("⚖ Scale Map");
		titleLabel.AddThemeColorOverride("font_color", UIStyle.ColorGoldDull);
		titleLabel.AddThemeFontSizeOverride("font_size", 14);
		titleLabel.HorizontalAlignment = HorizontalAlignment.Center;
		innerVBox.AddChild(titleLabel);

		var hintLabel = new Label();
		hintLabel.Text = TranslationServer.Translate("Sets the new size. All terrain and entity\npositions will be scaled proportionally.");
		hintLabel.AddThemeColorOverride("font_color", new Color(0.75f, 0.75f, 0.75f));
		hintLabel.AddThemeFontSizeOverride("font_size", 10);
		hintLabel.HorizontalAlignment = HorizontalAlignment.Center;
		innerVBox.AddChild(hintLabel);

		var sizeGrid = new GridContainer();
		sizeGrid.Columns = 3;
		sizeGrid.AddThemeConstantOverride("h_separation", 8);
		sizeGrid.AddThemeConstantOverride("v_separation", 6);
		innerVBox.AddChild(sizeGrid);

		_lblScalePreviewWidth = new Label();
		_lblScalePreviewWidth.AddThemeFontSizeOverride("font_size", 11);
		_lblScalePreviewWidth.AddThemeColorOverride("font_color", UIStyle.ColorCyanGlow);
		_lblScalePreviewWidth.SizeFlagsHorizontal = SizeFlags.ExpandFill;
		sizeGrid.AddChild(_lblScalePreviewWidth);

		var btnW_Dec = new Button();
		btnW_Dec.Set("icon_max_width", 0);
		SetupOptionButton(btnW_Dec, "\uf068", () =>
		{
			if (_scaleDialogTargetWidth > 32)
			{
				_scaleDialogTargetWidth = Math.Max(32, (_scaleDialogTargetWidth % 32 == 0 ? _scaleDialogTargetWidth - 32 : (_scaleDialogTargetWidth / 32) * 32));
				UpdateScaleDialogLabels();
				GameHost.Instance?.ShowScaleMapSilhouette(_scaleDialogTargetWidth, _scaleDialogTargetDepth);
			}
		}, 10, "Decrease target width");
		sizeGrid.AddChild(btnW_Dec);

		var btnW_Inc = new Button();
		btnW_Inc.Set("icon_max_width", 0);
		SetupOptionButton(btnW_Inc, "\uf067", () =>
		{
			if (_scaleDialogTargetWidth < 512)
			{
				_scaleDialogTargetWidth = Math.Min(512, (_scaleDialogTargetWidth / 32 + 1) * 32);
				UpdateScaleDialogLabels();
				GameHost.Instance?.ShowScaleMapSilhouette(_scaleDialogTargetWidth, _scaleDialogTargetDepth);
			}
		}, 10, "Increase target width");
		sizeGrid.AddChild(btnW_Inc);

		_lblScalePreviewHeight = new Label();
		_lblScalePreviewHeight.AddThemeFontSizeOverride("font_size", 11);
		_lblScalePreviewHeight.AddThemeColorOverride("font_color", UIStyle.ColorCyanGlow);
		_lblScalePreviewHeight.SizeFlagsHorizontal = SizeFlags.ExpandFill;
		sizeGrid.AddChild(_lblScalePreviewHeight);

		UpdateScaleDialogLabels();

		var btnH_Dec = new Button();
		btnH_Dec.Set("icon_max_width", 0);
		SetupOptionButton(btnH_Dec, "\uf068", () =>
		{
			if (_scaleDialogTargetDepth > 32)
			{
				_scaleDialogTargetDepth = Math.Max(32, (_scaleDialogTargetDepth % 32 == 0 ? _scaleDialogTargetDepth - 32 : (_scaleDialogTargetDepth / 32) * 32));
				UpdateScaleDialogLabels();
				GameHost.Instance?.ShowScaleMapSilhouette(_scaleDialogTargetWidth, _scaleDialogTargetDepth);
			}
		}, 10, "Decrease target depth");
		sizeGrid.AddChild(btnH_Dec);

		var btnH_Inc = new Button();
		btnH_Inc.Set("icon_max_width", 0);
		SetupOptionButton(btnH_Inc, "\uf067", () =>
		{
			if (_scaleDialogTargetDepth < 512)
			{
				_scaleDialogTargetDepth = Math.Min(512, (_scaleDialogTargetDepth / 32 + 1) * 32);
				UpdateScaleDialogLabels();
				GameHost.Instance?.ShowScaleMapSilhouette(_scaleDialogTargetWidth, _scaleDialogTargetDepth);
			}
		}, 10, "Increase target depth");
		sizeGrid.AddChild(btnH_Inc);

		var separator = new HSeparator();
		innerVBox.AddChild(separator);

		var buttonRow = new HBoxContainer();
		buttonRow.AddThemeConstantOverride("separation", 8);
		innerVBox.AddChild(buttonRow);

		var btnCancel = new Button();
		btnCancel.Set("icon_max_width", 0);
		btnCancel.SizeFlagsHorizontal = SizeFlags.ExpandFill;
		SetupButton(btnCancel, "✖ CANCEL", () =>
		{
			GameHost.Instance?.HideScaleMapSilhouette();
			CloseScaleMapDialog();
		}, 11, "Cancel and discard the scale operation");
		buttonRow.AddChild(btnCancel);

		var btnApply = new Button();
		btnApply.Set("icon_max_width", 0);
		btnApply.SizeFlagsHorizontal = SizeFlags.ExpandFill;
		SetupButton(btnApply, "✔ APPLY", () =>
		{
			GameHost.Instance?.HideScaleMapSilhouette();
			GameHost.Instance?.ScaleMapExternal(_scaleDialogTargetWidth, _scaleDialogTargetDepth);
			CloseScaleMapDialog();
		}, 11, "Apply scale and stretch the entire map");
		buttonRow.AddChild(btnApply);
	}

	private void UpdateScaleDialogLabels()
	{
		if (_lblScalePreviewWidth != null)
			_lblScalePreviewWidth.Text = $"W: {_scaleDialogTargetWidth}";
		if (_lblScalePreviewHeight != null)
			_lblScalePreviewHeight.Text = $"H: {_scaleDialogTargetDepth}";
	}

	private void CloseScaleMapDialog()
	{
		if (_scaleMapDialog != null && GodotObject.IsInstanceValid(_scaleMapDialog))
		{
			_scaleMapDialog.QueueFree();
			_scaleMapDialog = null;
		}
		_lblScalePreviewWidth = null;
		_lblScalePreviewHeight = null;
	}

	private readonly HashSet<ulong> _hookedSliderInstanceIds = new();

	private void HookSliders(Node parent)
	{
		if (parent == null) return;
		if (parent is Slider slider)
		{
			if (_hookedSliderInstanceIds.Add(slider.GetInstanceId()))
			{
				slider.DragStarted += () => IsDraggingSlider = true;
				slider.DragEnded += (_) => IsDraggingSlider = false;
			}
		}
		int childCount = parent.GetChildCount();
		for (int i = 0; i < childCount; i++)
		{
			HookSliders(parent.GetChild(i));
		}
	}

	private void SetupButton(Button btn, string text, Action onClick, int fontSize = 13, string tooltip = "")
	{
		if (btn == null) return;
		btn.Text = text;
		var font = GetFontAwesomeFont();
		if (font != null)
		{
			btn.AddThemeFontOverride("font", font);
		}
		btn.CustomMinimumSize = new Vector2(0, 32);
		btn.AddThemeStyleboxOverride("normal", UIStyle.CreateButtonNormal());
		btn.AddThemeStyleboxOverride("hover", UIStyle.CreateButtonHover());
		btn.AddThemeStyleboxOverride("pressed", UIStyle.CreateButtonPressed());
		btn.AddThemeStyleboxOverride("focus", new StyleBoxEmpty());
		btn.AddThemeFontSizeOverride("font_size", fontSize);
		btn.FocusMode = FocusModeEnum.None;
		if (!string.IsNullOrEmpty(tooltip))
		{
			btn.TooltipText = TranslationServer.Translate(tooltip);
		}
		btn.Pressed += () =>
		{
			UIManager.Instance?.PlayClickSound();
			onClick?.Invoke();
		};
	}

	private void SetupIconButton(Button btn, string iconPath, Action onClick, string tooltip = "")
	{
		btn.Flat = false;
		btn.Text = "";
		btn.Icon = null;
		btn.CustomMinimumSize = new Vector2(36, 32);
		btn.FocusMode = FocusModeEnum.None;
		btn.AddThemeConstantOverride("icon_max_width", 0);

		btn.AddThemeStyleboxOverride("normal", UIStyle.CreateButtonNormal());
		btn.AddThemeStyleboxOverride("hover", UIStyle.CreateButtonHover());
		btn.AddThemeStyleboxOverride("pressed", UIStyle.CreateButtonPressed());
		btn.AddThemeStyleboxOverride("focus", new StyleBoxEmpty());

		foreach (Node child in btn.GetChildren())
		{
			if (child is TextureRect) child.QueueFree();
		}

		var iconRect = new TextureRect();
		iconRect.Name = "IconRect";
		iconRect.Texture = GD.Load<Texture2D>(iconPath);
		iconRect.ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize;
		iconRect.StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered;
		iconRect.MouseFilter = MouseFilterEnum.Ignore;
		iconRect.AnchorLeft = 0;
		iconRect.AnchorTop = 0;
		iconRect.AnchorRight = 1;
		iconRect.AnchorBottom = 1;
		iconRect.OffsetLeft = 7;
		iconRect.OffsetTop = 5;
		iconRect.OffsetRight = -7;
		iconRect.OffsetBottom = -5;
		iconRect.Modulate = UIStyle.ColorGoldDull;
		btn.AddChild(iconRect);

		btn.MouseEntered += () => iconRect.Modulate = UIStyle.ColorGold;
		btn.MouseExited += () => iconRect.Modulate = UIStyle.ColorGoldDull;
		btn.ButtonDown += () => iconRect.Modulate = UIStyle.ColorCyanGlow;
		btn.ButtonUp += () => iconRect.Modulate = btn.IsHovered() ? UIStyle.ColorGold : UIStyle.ColorGoldDull;

		if (!string.IsNullOrEmpty(tooltip))
		{
			btn.TooltipText = TranslationServer.Translate(tooltip);
		}

		btn.Pressed += () =>
		{
			UIManager.Instance?.PlayClickSound();
			onClick?.Invoke();
		};
	}

	private void SetupOptionButton(Button btn, string text, Action onClick, int fontSize = 11, string tooltip = "")
	{
		if (btn == null) return;
		btn.Text = text;
		var font = GetFontAwesomeFont();
		if (font != null)
		{
			btn.AddThemeFontOverride("font", font);
		}
		btn.CustomMinimumSize = new Vector2(0, 30);
		btn.AddThemeStyleboxOverride("normal", UIStyle.CreateOptionButtonNormal());
		btn.AddThemeStyleboxOverride("hover", UIStyle.CreateOptionButtonHover());
		btn.AddThemeStyleboxOverride("pressed", UIStyle.CreateOptionButtonPressed());
		btn.AddThemeStyleboxOverride("focus", new StyleBoxEmpty());
		btn.AddThemeFontSizeOverride("font_size", fontSize);
		btn.AddThemeColorOverride("font_color", new Color(0.95f, 0.90f, 0.82f));
		btn.AddThemeColorOverride("font_hover_color", UIStyle.ColorGold);
		btn.FocusMode = FocusModeEnum.None;
		if (!string.IsNullOrEmpty(tooltip))
		{
			btn.TooltipText = TranslationServer.Translate(tooltip);
		}
		if (onClick != null)
		{
			btn.Pressed += () =>
			{
				UIManager.Instance?.PlayClickSound();
				onClick?.Invoke();
			};
		}
	}

	private void StyleOptionButtonPopup(OptionButton optBtn)
	{
		if (optBtn == null) return;
		var font = GetFontAwesomeFont();
		if (font != null)
		{
			optBtn.AddThemeFontOverride("font", font);
		}
		StylePopupMenu(optBtn.GetPopup());
	}

	private void StylePopupMenu(PopupMenu popup)
	{
		if (popup == null) return;
		var font = GetFontAwesomeFont();
		if (font != null)
		{
			popup.AddThemeFontOverride("font", font);
		}
		popup.AddThemeFontSizeOverride("font_size", 12);

		var popupStyle = new StyleBoxFlat();
		popupStyle.BgColor = new Color(0.14f, 0.13f, 0.11f, 0.98f);
		popupStyle.BorderColor = UIStyle.ColorGold;
		popupStyle.SetBorderWidthAll(1);
		popupStyle.CornerRadiusTopLeft = 4;
		popupStyle.CornerRadiusTopRight = 4;
		popupStyle.CornerRadiusBottomLeft = 4;
		popupStyle.CornerRadiusBottomRight = 4;
		popupStyle.ContentMarginLeft = 8;
		popupStyle.ContentMarginRight = 8;
		popupStyle.ContentMarginTop = 6;
		popupStyle.ContentMarginBottom = 6;

		popup.AddThemeStyleboxOverride("panel", popupStyle);
		popup.AddThemeColorOverride("font_color", new Color(0.92f, 0.88f, 0.82f));
		popup.AddThemeColorOverride("font_hover_color", UIStyle.ColorGold);
	}

	private void StyleOptionButtonsInContainer(Control container)
	{
		if (container == null) return;
		foreach (Node child in container.GetChildren())
		{
			if (child is OptionButton optBtn)
			{
				StyleOptionButtonPopup(optBtn);
			}
			else if (child is Button btn && !btn.Name.ToString().StartsWith("BtnHeader"))
			{
				if (btn.CustomMinimumSize.X != 52 || btn.CustomMinimumSize.Y != 52)
				{
					btn.AddThemeStyleboxOverride("normal", UIStyle.CreateOptionButtonNormal());
					btn.AddThemeStyleboxOverride("hover", UIStyle.CreateOptionButtonHover());
					btn.AddThemeStyleboxOverride("pressed", UIStyle.CreateOptionButtonPressed());
					btn.AddThemeStyleboxOverride("focus", new StyleBoxEmpty());
					btn.AddThemeColorOverride("font_color", new Color(0.95f, 0.90f, 0.82f));
					btn.AddThemeColorOverride("font_hover_color", UIStyle.ColorGold);
				}
			}
			else if (child is Control subContainer)
			{
				StyleOptionButtonsInContainer(subContainer);
			}
		}
	}

	public void SetupTextureSwatches(bool connectEvents = false)
	{
		_swatchTextureCache.Clear();
		_swatchPaths.Clear();
		_swatchDisplayNames.Clear();
		_swatchColors.Clear();

		try
		{
			string wsPath = string.IsNullOrEmpty(_tempWorkspacePath) 
				? ProjectSettings.GlobalizePath(TempWorkspaceGodotPath) 
				: _tempWorkspacePath;
			var unionedAssets = Realm.Godot.Utils.MapAssetHelper.LoadUnionedAssets(wsPath);
			JsonObject? texturesObj = unionedAssets?["textures"] as JsonObject;

			var slots = Realm.Godot.Utils.TextureSwatchSlots.ResolveSlots(texturesObj, wsPath);
			for (int i = 0; i < Realm.Godot.Utils.TextureSwatchSlots.MaxSlots; i++)
			{
				var slot = slots[i];
				if (!slot.IsFiller && !string.IsNullOrEmpty(slot.BaseName))
				{
					string cleanDisplayName = System.Globalization.CultureInfo.CurrentCulture.TextInfo.ToTitleCase(slot.BaseName.Replace("_", " "));
					_swatchDisplayNames.Add(cleanDisplayName);
					string resolvedPath = System.IO.Path.Combine(wsPath, "Assets", "textures", slot.FileName ?? (slot.BaseName + ".rtex"));
					if (!System.IO.File.Exists(resolvedPath))
					{
						resolvedPath = System.IO.Path.Combine(wsPath, slot.FileName ?? (slot.BaseName + ".rtex"));
					}
					_swatchPaths.Add(resolvedPath);
					_swatchColors.Add(new Color(0.6f, 0.6f, 0.6f));
				}
				else
				{
					_swatchDisplayNames.Add($"Slot {i} (Empty)");
					_swatchPaths.Add("");
					_swatchColors.Add(new Color(0.2f, 0.2f, 0.2f, 0.5f));
				}
			}

			if (_gridSwatches != null)
			{
				if (_scrollSwatches == null && _gridSwatches.GetParent() is VBoxContainer parentVBox)
				{
					int gridIndex = _gridSwatches.GetIndex();
					parentVBox.RemoveChild(_gridSwatches);
					_scrollSwatches = new ScrollContainer();
					_scrollSwatches.Name = "ScrollSwatches";
					_scrollSwatches.HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled;
					_scrollSwatches.VerticalScrollMode = ScrollContainer.ScrollMode.Auto;
					_scrollSwatches.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
					_scrollSwatches.AddChild(_gridSwatches);
					parentVBox.AddChild(_scrollSwatches);
					parentVBox.MoveChild(_scrollSwatches, gridIndex);
				}

				int totalColumns = 6;
				if (_gridSwatches is GridContainer gridSwatchesContainer)
				{
					gridSwatchesContainer.Columns = totalColumns;
				}
				foreach (Node child in _gridSwatches.GetChildren())
				{
					_gridSwatches.RemoveChild(child);
					child.QueueFree();
				}
				_swatchButtons.Clear();

				int visibleCount = 0;
				for (int i = 0; i < Realm.Godot.Utils.TextureSwatchSlots.MaxSlots; i++)
				{
					var slot = slots[i];
					int slotIndex = slot.SlotIndex;
					var btn = new Button();
					btn.Name = $"Swatch{slotIndex + 1}";
					btn.Flat = false;
					btn.ExpandIcon = true;
					btn.FocusMode = FocusModeEnum.None;
					btn.CustomMinimumSize = new Vector2(40, 40);
					btn.Set("icon_max_width", 0);
					btn.AddThemeStyleboxOverride("normal", UIStyle.CreateButtonNormal());
					btn.AddThemeStyleboxOverride("hover", UIStyle.CreateButtonHover());
					btn.AddThemeStyleboxOverride("pressed", UIStyle.CreateButtonPressed());

					if (!slot.IsFiller && !string.IsNullOrEmpty(slot.BaseName))
					{
						btn.Visible = true;
						visibleCount++;
						Texture2D tex = GetSwatchTexture(slotIndex);
						if (tex != null)
						{
							var texRect = new TextureRect();
							texRect.Texture = tex;
							texRect.ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize;
							texRect.StretchMode = TextureRect.StretchModeEnum.KeepAspectCovered;
							texRect.MouseFilter = MouseFilterEnum.Ignore;
							texRect.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
							texRect.GrowHorizontal = GrowDirection.Both;
							texRect.GrowVertical = GrowDirection.Both;
							btn.AddChild(texRect);
						}
						else
						{
							var colorBox = new ColorRect();
							colorBox.Color = _swatchColors[slotIndex];
							colorBox.MouseFilter = MouseFilterEnum.Ignore;
							colorBox.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
							colorBox.GrowHorizontal = GrowDirection.Both;
							colorBox.GrowVertical = GrowDirection.Both;
							btn.AddChild(colorBox);
						}
						btn.TooltipText = $"{slotIndex}: {_swatchDisplayNames[slotIndex]}";
					}
					else
					{
						btn.Visible = false;
					}

					btn.GuiInput += (@event) =>
					{
						if (@event is InputEventMouseButton mouseEvent && mouseEvent.Pressed)
						{
							if (mouseEvent.ButtonIndex == MouseButton.Left)
							{
								if (Input.IsKeyPressed(Godot.Key.Shift) || (_chkApplyCliffTexture != null && _chkApplyCliffTexture.ButtonPressed && (_chkApplyGroundTexture == null || !_chkApplyGroundTexture.ButtonPressed)))
								{
									SelectCliffTexture(slotIndex);
								}
								else
								{
									SelectTerrainTexture(slotIndex, btn);
								}
							}
							else if (mouseEvent.ButtonIndex == MouseButton.Right)
							{
								SelectCliffTexture(slotIndex);
							}
						}
					};

					_gridSwatches.AddChild(btn);
					_swatchButtons.Add(btn);
				}

				const int maxVisibleSwatchesBeforeScroll = 36;
				int numRows = (int)MathF.Ceiling((float)visibleCount / totalColumns);
				if (numRows < 1) numRows = 1;
				int maxRows = maxVisibleSwatchesBeforeScroll / totalColumns;
				int displayedRows = Math.Min(numRows, maxRows);

				float rowHeight = 40f;
				float vSeparation = 6f;
				if (_gridSwatches is GridContainer gc && gc.HasThemeConstantOverride("v_separation"))
				{
					vSeparation = gc.GetThemeConstant("v_separation");
				}
				float targetHeight = displayedRows * rowHeight + Math.Max(0, displayedRows - 1) * vSeparation;

				if (_scrollSwatches != null)
				{
					_scrollSwatches.CustomMinimumSize = new Vector2(0, targetHeight);
					_scrollSwatches.HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled;
					_scrollSwatches.VerticalScrollMode = visibleCount > maxVisibleSwatchesBeforeScroll 
						? ScrollContainer.ScrollMode.Auto 
						: ScrollContainer.ScrollMode.Disabled;
				}
			}
		}
		catch { }

		UpdateTextureLabels();
	}

	private void SelectTerrainTexture(int index, Button swatch)
	{
		if (GameHost.Instance != null)
		{
			if (_chkApplyCliffTexture != null && _chkApplyCliffTexture.ButtonPressed && (_chkApplyGroundTexture == null || !_chkApplyGroundTexture.ButtonPressed))
			{
				SelectCliffTexture(index);
				return;
			}

			GameHost.Instance.EditorPaintTextureIndex = index;
			HighlightSwatch(swatch);

			if (!IsSwatchCompatibleTool(GameHost.Instance.ActiveEditorTool))
			{
				TriggerToolSelection(GameHost.EditorTool.PaintTexture, _btnTextureBrush);
			}
			
			UpdateTextureLabels();
			
			string name = TranslationServer.Translate(_swatchDisplayNames[index]);
			ShowFeedback(string.Format(TranslationServer.Translate("Selected Terrain: {0}"), name));
		}
	}

	private void SelectCliffTexture(int index)
	{
		if (GameHost.Instance != null)
		{
			GameHost.Instance.EditorCliffPaintTextureIndex = index;
			if (!IsSwatchCompatibleTool(GameHost.Instance.ActiveEditorTool))
			{
				TriggerToolSelection(GameHost.EditorTool.PaintTexture, _btnTextureBrush);
			}
			UpdateTextureLabels();
			
			string name = TranslationServer.Translate(_swatchDisplayNames[index]);
			ShowFeedback(string.Format(TranslationServer.Translate("Selected Cliff Face: {0}"), name));
		}
	}

	private static bool IsSwatchCompatibleTool(GameHost.EditorTool tool) => tool switch
	{
		GameHost.EditorTool.Raise       => true,
		GameHost.EditorTool.Lower       => true,
		GameHost.EditorTool.Height      => true,
		GameHost.EditorTool.Smooth      => true,
		GameHost.EditorTool.Plateau     => true,
		GameHost.EditorTool.Noise       => true,
		GameHost.EditorTool.Ramp        => true,
		GameHost.EditorTool.Water       => true,
		GameHost.EditorTool.PaintTexture => true,
		GameHost.EditorTool.FloodFill   => true,
		GameHost.EditorTool.Eyedropper  => true,
		GameHost.EditorTool.SelectArea  => true,
		_ => false
	};

	private void UpdateTextureLabels()
	{
		if (GameHost.Instance == null) return;
		int terrainIdx = GameHost.Instance.EditorPaintTextureIndex;
		int cliffIdx = GameHost.Instance.EditorCliffPaintTextureIndex;

		string terrainName = (terrainIdx >= 0 && terrainIdx < _swatchDisplayNames.Count) ? _swatchDisplayNames[terrainIdx] : "Unknown";
		string cliffName = (cliffIdx >= 0 && cliffIdx < _swatchDisplayNames.Count) ? _swatchDisplayNames[cliffIdx] : "Unknown";

		if (_lblTerrainTexture != null) _lblTerrainTexture.Text = TranslationServer.Translate(terrainName);
		if (_lblCliffTexture != null) _lblCliffTexture.Text = TranslationServer.Translate(cliffName);

		Button terrainSwatch = (terrainIdx >= 0 && terrainIdx < _swatchButtons.Count) ? _swatchButtons[terrainIdx] : null;
		Button cliffSwatch = (cliffIdx >= 0 && cliffIdx < _swatchButtons.Count) ? _swatchButtons[cliffIdx] : null;

		HighlightSwatch(terrainSwatch);
		HighlightCliffSwatch(cliffSwatch);

		if (_btnReplaceTexture != null)
		{
			_btnReplaceTexture.Text = $"\uf093 {TranslationServer.Translate("REPLACE TEXTURE")}";
			_btnReplaceTexture.TooltipText = TranslationServer.Translate("Replace all instances of a texture with another texture across the map");
		}
	}

	private string GetSwatchName(Color color)
	{
		float epsilon = 0.01f;
		for (int i = 1; i <= _swatchColors.Count; i++)
		{
			Color c = GetSwatchColor(i);
			if (Mathf.Abs(color.R - c.R) < epsilon &&
				Mathf.Abs(color.G - c.G) < epsilon &&
				Mathf.Abs(color.B - c.B) < epsilon)
			{
				string texName = (i >= 1 && i <= _swatchDisplayNames.Count) ? _swatchDisplayNames[i - 1] : "Unknown";
				return texName;
			}
		}
		return "Custom";
	}

	private Color GetSwatchColor(int index)
	{
		if (index >= 1 && index <= _swatchColors.Count)
		{
			return _swatchColors[index - 1];
		}
		return new Color(1, 1, 1);
	}

	public bool IsMouseOverUI(Vector2 mousePos)
	{
		if (SettingsMenu.IsOpen)
		{
			return true;
		}

		if (_helpOverlayPanel != null && _helpOverlayPanel.IsVisibleInTree())
		{
			return true;
		}
		if (GetNodeOrNull<Control>("ConfirmationOverlay") != null)
		{
			return true;
		}
		if (GetNodeOrNull<Control>("GenerationOverlay") != null)
		{
			return true;
		}
		if (_scaleMapDialog != null && _scaleMapDialog.IsVisibleInTree())
		{
			return true;
		}
		if (FloatingDialogBase.HasAnyDialogOpen)
		{
			return true;
		}
		if (_is3DInteractionActive)
		{
			return false;
		}

		if (_minimapController != null && _minimapController.IsDragging)
		{
			return true;
		}
		if (_isDraggingSlider)
		{
			return true;
		}
		var hoveredControl = GetViewport().GuiGetHoveredControl();
		if (hoveredControl != null && hoveredControl != this)
		{
			if (hoveredControl != _panelLeft &&
				hoveredControl != _panelRight &&
				hoveredControl.Name != "LeftScroll" &&
				hoveredControl.Name != "RightScroll" &&
				hoveredControl.Name != "LeftVBox" &&
				hoveredControl.Name != "AccordionContainer" &&
				hoveredControl.Name != "FeedbackLabel" &&
				hoveredControl.Name != "MapEditorScreenFrame")
			{
				return true;
			}
		}
		if (_optModule != null && _optModule.GetPopup() != null && _optModule.GetPopup().Visible)
		{
			return true;
		}
		if (_entityPaletteController?.OptCategoryItems != null && _entityPaletteController.OptCategoryItems.GetPopup() != null && _entityPaletteController.OptCategoryItems.GetPopup().Visible)
		{
			return true;
		}
		if (_optEyedropperMode != null && _optEyedropperMode.GetPopup() != null && _optEyedropperMode.GetPopup().Visible)
		{
			return true;
		}
		if (_optPathingMode != null && _optPathingMode.GetPopup() != null && _optPathingMode.GetPopup().Visible)
		{
			return true;
		}
		if (_optMirrorMode != null && _optMirrorMode.GetPopup() != null && _optMirrorMode.GetPopup().Visible)
		{
			return true;
		}
		if (_optClipboardMirrorMode != null && _optClipboardMirrorMode.GetPopup() != null && _optClipboardMirrorMode.GetPopup().Visible)
		{
			return true;
		}
		if (_optPlacementMirrorMode != null && _optPlacementMirrorMode.GetPopup() != null && _optPlacementMirrorMode.GetPopup().Visible)
		{
			return true;
		}
		if (_optPolarRingSpacing != null && _optPolarRingSpacing.GetPopup() != null && _optPolarRingSpacing.GetPopup().Visible)
		{
			return true;
		}
		if (_optPolarRadialStep != null && _optPolarRadialStep.GetPopup() != null && _optPolarRadialStep.GetPopup().Visible)
		{
			return true;
		}
		if (_popupOverlayMode != null && _popupOverlayMode.Visible)
		{
			return true;
		}
		if (_popupEnvironment != null && _popupEnvironment.Visible)
		{
			return true;
		}
		if (_popupCamera != null && _popupCamera.Visible)
		{
			return true;
		}
		if (_optEnvLighting != null && _optEnvLighting.GetPopup() != null && _optEnvLighting.GetPopup().Visible)
		{
			return true;
		}
		if (_optEnvWeather != null && _optEnvWeather.GetPopup() != null && _optEnvWeather.GetPopup().Visible)
		{
			return true;
		}

		// Check all option cards in _cardDragMap
		foreach (var kvp in _cardDragMap)
		{
			var card = kvp.Key;
			if (GodotObject.IsInstanceValid(card) && card.IsVisibleInTree())
			{
				if (card.GetGlobalRect().HasPoint(mousePos))
				{
					return true;
				}
			}
		}

		// Check all option content containers
		Control[] optionContents = new Control[]
		{
			_contentFile, _contentViewport, _contentTool,
			_contentBrush, _contentToolSettings, _contentPlacement, _contentInspector, _contentLightingTuning
		};
		foreach (var content in optionContents)
		{
			if (GodotObject.IsInstanceValid(content) && content.IsVisibleInTree())
			{
				if (content.GetGlobalRect().HasPoint(mousePos))
				{
					return true;
				}
			}
		}

		if (_btnLeftTab != null && _btnLeftTab.Visible && _btnLeftTab.GetGlobalRect().HasPoint(mousePos))
		{
			return true;
		}
		if (_btnRightTab != null && _btnRightTab.Visible && _btnRightTab.GetGlobalRect().HasPoint(mousePos))
		{
			return true;
		}
		var topLeftBox = GetNodeOrNull<Control>("TopLeftBox");
		if (topLeftBox != null && topLeftBox.Visible && topLeftBox.GetGlobalRect().HasPoint(mousePos))
		{
			return true;
		}
		var topBar = GetNodeOrNull<Control>("TopBar");
		if (topBar != null && topBar.Visible && topBar.GetGlobalRect().HasPoint(mousePos))
		{
			return true;
		}
		if (_topToolbar != null && _topToolbar.Visible && _topToolbar.GetGlobalRect().HasPoint(mousePos))
		{
			return true;
		}
		if (_middleRightBox != null && _middleRightBox.Visible && _middleRightBox.GetGlobalRect().HasPoint(mousePos))
		{
			return true;
		}
		if (_scaleMapDialog != null && _scaleMapDialog.Visible && _scaleMapDialog.GetGlobalRect().HasPoint(mousePos))
		{
			return true;
		}
		var genOverlay = GetNodeOrNull<Control>("GenerationOverlay");
		if (genOverlay != null && genOverlay.Visible && genOverlay.GetGlobalRect().HasPoint(mousePos))
		{
			return true;
		}

		return false;
	}

	public void Set3DInteractionActive(bool active)
	{
		if (_is3DInteractionActive == active) return;
		_is3DInteractionActive = active;

		if (!EditorSettingsDialog.CurrentSettings.HideHudDuringToolUsage && active)
		{
			return;
		}

		if (active)
		{
			_savedMouseFilters.Clear();
			ApplyMouseFilterIgnoreRecursive(_panelLeft);
			ApplyMouseFilterIgnoreRecursive(_panelRight);
			ApplyMouseFilterIgnoreRecursive(_topLeftBox);
			if (GodotObject.IsInstanceValid(_topBar))
			{
				ApplyMouseFilterIgnoreRecursive(_topBar);
			}
			foreach (var card in _cardDragMap.Keys)
			{
				if (GodotObject.IsInstanceValid(card))
				{
					ApplyMouseFilterIgnoreRecursive(card);
				}
			}
		}
		else
		{
			foreach (var (ctrl, filter) in _savedMouseFilters)
			{
				if (GodotObject.IsInstanceValid(ctrl))
				{
					ctrl.MouseFilter = filter;
				}
			}
			_savedMouseFilters.Clear();
		}

		if (_hudFadeTween != null && _hudFadeTween.IsValid())
		{
			_hudFadeTween.Kill();
		}

		_hudFadeTween = CreateTween();
		_hudFadeTween.SetParallel(true);
		float targetAlpha = (active && EditorSettingsDialog.CurrentSettings.HideHudDuringToolUsage) ? 0.0f : 1.0f;
		float duration = 0.35f;

		if (GodotObject.IsInstanceValid(_topLeftBox))
		{
			_hudFadeTween.TweenProperty(_topLeftBox, "modulate:a", targetAlpha, duration)
				.SetTrans(Tween.TransitionType.Cubic)
				.SetEase(Tween.EaseType.Out);
		}
		if (GodotObject.IsInstanceValid(_panelLeft))
		{
			_hudFadeTween.TweenProperty(_panelLeft, "modulate:a", targetAlpha, duration)
				.SetTrans(Tween.TransitionType.Cubic)
				.SetEase(Tween.EaseType.Out);
		}
		if (GodotObject.IsInstanceValid(_panelRight))
		{
			_hudFadeTween.TweenProperty(_panelRight, "modulate:a", targetAlpha, duration)
				.SetTrans(Tween.TransitionType.Cubic)
				.SetEase(Tween.EaseType.Out);
		}
		if (GodotObject.IsInstanceValid(_screenFrameRect) && !EditorSettingsDialog.CurrentSettings.HideChromeBorderOverlay)
		{
			_hudFadeTween.TweenProperty(_screenFrameRect, "modulate:a", targetAlpha, duration)
				.SetTrans(Tween.TransitionType.Cubic)
				.SetEase(Tween.EaseType.Out);
		}
		foreach (var card in _cardDragMap.Keys)
		{
			if (GodotObject.IsInstanceValid(card) && (card.TopLevel || card.GetParent() == this))
			{
				_hudFadeTween.TweenProperty(card, "modulate:a", targetAlpha, duration)
					.SetTrans(Tween.TransitionType.Cubic)
					.SetEase(Tween.EaseType.Out);
			}
		}
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

	private void UpdateBlockStepVisibility()
	{
		bool blockModeEnabled = (_chkBlockMode != null && _chkBlockMode.Visible && _chkBlockMode.ButtonPressed);
		var tool = GameHost.Instance != null ? GameHost.Instance.ActiveEditorTool : GameHost.EditorTool.Lower;

		if (_stepBox != null)
		{
			_stepBox.Visible = blockModeEnabled && (tool == GameHost.EditorTool.Raise || tool == GameHost.EditorTool.Lower);
		}

		if (_heightBox != null)
		{
			_heightBox.Visible = (tool == GameHost.EditorTool.Height);
		}
	}

	private void UpdateBrushStrengthVisibility()
	{
		if (_sldBrushStrength != null && _sldBrushStrength.GetParent() is Control strengthParent)
		{
			if (GameHost.Instance == null) return;
			var tool = GameHost.Instance.ActiveEditorTool;
			bool blockModeEnabled = (_chkBlockMode != null && _chkBlockMode.ButtonPressed);

			bool isClumpPlacement = tool == GameHost.EditorTool.PlacePropClump ||
									((tool == GameHost.EditorTool.PlaceUnit || tool == GameHost.EditorTool.PlaceProp || tool == GameHost.EditorTool.PlaceDecal) &&
									 (_chkClumpMode != null && _chkClumpMode.ButtonPressed));

			if (tool == GameHost.EditorTool.Raise || tool == GameHost.EditorTool.Lower)
			{
				strengthParent.Visible = !blockModeEnabled;
			}
			else if (tool == GameHost.EditorTool.Height)
			{
				strengthParent.Visible = false;
			}
			else
			{
				strengthParent.Visible = (tool != GameHost.EditorTool.PaintPathing && 
										  tool != GameHost.EditorTool.FloodFillPathing &&
										  !isClumpPlacement &&
										  tool != GameHost.EditorTool.Plateau &&
										  tool != GameHost.EditorTool.Ramp);
			}
		}
	}

	private void ShowAgreementModal(Action onAccepted = null)
	{
		var overlay = new ColorRect();
		overlay.Name = "AgreementOverlay";
		overlay.Color = new Color(0, 0, 0, 0.75f);
		overlay.SetAnchorsPreset(LayoutPreset.FullRect);
		overlay.MouseFilter = Control.MouseFilterEnum.Stop;
		overlay.ZIndex = 1000;
		AddChild(overlay);

		var center = new CenterContainer();
		center.SetAnchorsPreset(LayoutPreset.FullRect);
		overlay.AddChild(center);

		var panel = new PanelContainer();
		panel.AddThemeStyleboxOverride("panel", new StyleBoxEmpty());
		panel.CustomMinimumSize = new Vector2(1024, 762);
		center.AddChild(panel);

		var bgTexRect = new TextureRect();
		bgTexRect.Texture = GD.Load<Texture2D>("res://Assets/UI/map_editor_agreement.png");
		bgTexRect.ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize;
		bgTexRect.StretchMode = TextureRect.StretchModeEnum.Scale;
		bgTexRect.SetAnchorsPreset(LayoutPreset.FullRect);
		panel.AddChild(bgTexRect);

		var contentOverlay = new Control();
		contentOverlay.SetAnchorsPreset(LayoutPreset.FullRect);
		panel.AddChild(contentOverlay);

		// Top Title Header
		var lblTitle = new Label();
		UIStyle.ApplyTitle(lblTitle, "Realm Creator Agreement", 20);
		lblTitle.Text = TranslationServer.Translate("Realm Creator Agreement").ToString().ToUpperInvariant();
		lblTitle.HorizontalAlignment = HorizontalAlignment.Center;
		lblTitle.Position = new Vector2(140, 48);
		lblTitle.Size = new Vector2(744, 30);
		contentOverlay.AddChild(lblTitle);

		// Intro Line 1 & Line 2
		var lblIntro1 = new Label();
		lblIntro1.Text = TranslationServer.Translate("By publishing content on Realm, you grant us permission to host, distribute, and display your map so people can play it.");
		lblIntro1.HorizontalAlignment = HorizontalAlignment.Left;
		lblIntro1.AddThemeFontSizeOverride("font_size", 12);
		lblIntro1.AddThemeColorOverride("font_color", new Color(0.18f, 0.15f, 0.12f));
		lblIntro1.Position = new Vector2(120, 112);
		lblIntro1.Size = new Vector2(784, 20);
		contentOverlay.AddChild(lblIntro1);

		var lblIntro2 = new Label();
		lblIntro2.Text = TranslationServer.Translate("We want Realm to be a thriving, collaborative arcade. Please respect these rules:");
		lblIntro2.HorizontalAlignment = HorizontalAlignment.Left;
		lblIntro2.AddThemeFontSizeOverride("font_size", 12);
		lblIntro2.AddThemeColorOverride("font_color", new Color(0.18f, 0.15f, 0.12f));
		lblIntro2.Position = new Vector2(120, 132);
		lblIntro2.Size = new Vector2(784, 20);
		contentOverlay.AddChild(lblIntro2);

		// Helper to place text rule card at exact X, Y coordinates
		void AddRuleTextBox(string title, string desc, float posX, float posY, float width, float height)
		{
			var textVBox = new VBoxContainer();
			textVBox.Position = new Vector2(posX, posY);
			textVBox.Size = new Vector2(width, height);
			textVBox.AddThemeConstantOverride("separation", 2);

			var lblTitle = new Label();
			lblTitle.Text = TranslationServer.Translate(title);
			lblTitle.AddThemeFontSizeOverride("font_size", 15);
			lblTitle.AddThemeColorOverride("font_color", new Color(0.72f, 0.35f, 0.12f));
			lblTitle.SizeFlagsHorizontal = SizeFlags.ExpandFill;
			textVBox.AddChild(lblTitle);

			var lblDesc = new Label();
			lblDesc.Text = TranslationServer.Translate(desc);
			lblDesc.AddThemeFontSizeOverride("font_size", 11);
			lblDesc.AddThemeColorOverride("font_color", new Color(0.22f, 0.18f, 0.15f));
			lblDesc.AutowrapMode = TextServer.AutowrapMode.WordSmart;
			lblDesc.SizeFlagsHorizontal = SizeFlags.ExpandFill;
			textVBox.AddChild(lblDesc);

			contentOverlay.AddChild(textVBox);
		}

		// Row 1
		AddRuleTextBox("Your Work is Yours", 
			"You retain full ownership of your original creations. You aren't signing away your copyright to anyone.",
			252, 185, 235, 125);

		AddRuleTextBox("Monetization", 
			"You may ask for donations from your player base, but you may not offer any differences in gameplay compared to non-paying users, other than cosmetic rewards. Pay-to-win is not allowed.",
			655, 185, 240, 125);

		// Row 2
		AddRuleTextBox("Collaboration", 
			"By publishing your content on Realm, you allow other creators to open and learn from your work. You also grant them permission to adapt, build upon, and incorporate it into their own creations, provided those new works remain exclusively within the Realm platform.",
			252, 338, 235, 135);

		AddRuleTextBox("Going Solo", 
			"Want to turn your map into a standalone game? Go for it! However, you can only take your original work with you. You must replace any content created by other Realm users, unless you obtain their explicit written permission.",
			655, 338, 240, 135);

		// Row 3
		AddRuleTextBox("Give Credit", 
			"If you import another creator's work, they still own the original. Never claim their work as your own.",
			252, 518, 235, 125);

		AddRuleTextBox("No Plagiarism or Piracy", 
			"Do not upload content you didn’t make or don't have the rights to use. This includes trademarked content from other video games, movies, music, and media.",
			655, 518, 240, 125);

		// Bottom Buttons (Accept & Quit)
		var btnAccept = new Button();
		btnAccept.Set("icon_max_width", 0);
		SetupOptionButton(btnAccept, "Accept", () =>
		{
			_agreementShownThisSession = true;
			if (onAccepted != null)
			{
				overlay.TreeExited += () => onAccepted();
			}
			overlay.QueueFree();
			CheckCreatorRegistrationAndPrompt();
		}, 13);
		btnAccept.Position = new Vector2(300, 692);
		btnAccept.Size = new Vector2(165, 42);
		contentOverlay.AddChild(btnAccept);

		var btnQuit = new Button();
		btnQuit.Set("icon_max_width", 0);
		SetupOptionButton(btnQuit, "Quit", () =>
		{
			_agreementShownThisSession = false;
			overlay.QueueFree();
			UIManager.Instance?.TransitionTo(GameScreen.MainMenu);
		}, 13);
		btnQuit.Position = new Vector2(558, 692);
		btnQuit.Size = new Vector2(165, 42);
		contentOverlay.AddChild(btnQuit);
	}

	private void InitializeInspectorPanel()
	{
		_inspectorPanel = GetNode<PanelContainer>("RightSlidePanel/RightScroll/AccordionContainer/InspectorAccordion/ContentInspector/InspectorPanel");
		_lblInspectorTitle = GetNode<Label>("RightSlidePanel/RightScroll/AccordionContainer/InspectorAccordion/ContentInspector/InspectorPanel/VBox/LblInspectorTitle");
		_lblInspectorPos = GetNode<Label>("RightSlidePanel/RightScroll/AccordionContainer/InspectorAccordion/ContentInspector/InspectorPanel/VBox/LblInspectorPos");
		
		_btnInspectorRotLeft = GetNode<Button>("RightSlidePanel/RightScroll/AccordionContainer/InspectorAccordion/ContentInspector/InspectorPanel/VBox/Grid/BtnInspectorRotLeft");
		SetupButton(_btnInspectorRotLeft, "\uf0e2 ROT -15°", () => RotateSelectedObjectAction(-15f), 11, "Rotate object counter-clockwise");

		_btnInspectorRotRight = GetNode<Button>("RightSlidePanel/RightScroll/AccordionContainer/InspectorAccordion/ContentInspector/InspectorPanel/VBox/Grid/BtnInspectorRotRight");
		SetupButton(_btnInspectorRotRight, "\uf01e ROT +15°", () => RotateSelectedObjectAction(15f), 11, "Rotate object clockwise");

		_btnInspectorScaleDown = GetNode<Button>("RightSlidePanel/RightScroll/AccordionContainer/InspectorAccordion/ContentInspector/InspectorPanel/VBox/Grid/BtnInspectorScaleDown");
		SetupButton(_btnInspectorScaleDown, "\uf068 SCALE DOWN", () => ScaleSelectedObjectAction(0.9f), 11, "Shrink object size by 10%");

		_btnInspectorScaleUp = GetNode<Button>("RightSlidePanel/RightScroll/AccordionContainer/InspectorAccordion/ContentInspector/InspectorPanel/VBox/Grid/BtnInspectorScaleUp");
		SetupButton(_btnInspectorScaleUp, "\uf067 SCALE UP", () => ScaleSelectedObjectAction(1.1f), 11, "Enlarge object size by 10%");

		_btnInspectorScaleReset = GetNode<Button>("RightSlidePanel/RightScroll/AccordionContainer/InspectorAccordion/ContentInspector/InspectorPanel/VBox/BtnInspectorScaleReset");
		SetupButton(_btnInspectorScaleReset, "\uf0e2 RESET SCALE", () => ScaleSelectedObjectAction(-1f), 12, "Reset object scale size to 1.0x");

		_btnCenter = GetNode<Button>("RightSlidePanel/RightScroll/AccordionContainer/InspectorAccordion/ContentInspector/InspectorPanel/VBox/BtnCenter");
		SetupButton(_btnCenter, "\uf140 LOCATE OBJECT", () => LocateSelectedObjectAction(), 12, "Center camera on selected object");

		_btnInspectorDelete = GetNode<Button>("RightSlidePanel/RightScroll/AccordionContainer/InspectorAccordion/ContentInspector/InspectorPanel/VBox/BtnInspectorDelete");
		SetupButton(_btnInspectorDelete, "\uf2ed ERASE", () => DeleteSelectedObjectAction(), 12, "Erase selected unit, prop, or decal");

		_btnShowCoverage = new Button();
		_btnShowCoverage.Text = TranslationServer.Translate("\uf06e RANGES: OFF");
		var fontCoverage = GetFontAwesomeFont();
		if (fontCoverage != null) _btnShowCoverage.AddThemeFontOverride("font", fontCoverage);
		_btnShowCoverage.ToggleMode = true;
		_btnShowCoverage.FocusMode = Control.FocusModeEnum.None;
		_btnShowCoverage.AddThemeFontSizeOverride("font_size", 11);
		_btnShowCoverage.ButtonPressed = false;
		_btnShowCoverage.Toggled += (pressed) =>
		{
			if (GameHost.Instance != null)
			{
				GameHost.Instance.EditorCoverageOverlayEnabled = pressed;
				_btnShowCoverage.Text = pressed ? TranslationServer.Translate("\uf06e RANGES: ON") : TranslationServer.Translate("\uf06e RANGES: OFF");
				GameHost.Instance.UpdateEditorCoverageOverlay();
			}
		};
		var inspectorVBox = GetNode<VBoxContainer>("RightSlidePanel/RightScroll/AccordionContainer/InspectorAccordion/ContentInspector/InspectorPanel/VBox");
		inspectorVBox.AddChild(_btnShowCoverage);

		_playerOwnerContainer = new HBoxContainer();
		_playerOwnerContainer.Name = "PlayerOwnerContainer";
		_playerOwnerContainer.Visible = false;

		var lblPlayer = new Label();
		lblPlayer.Text = TranslationServer.Translate("Player");
		lblPlayer.CustomMinimumSize = new Vector2(50, 0);
		lblPlayer.AddThemeFontSizeOverride("font_size", 11);
		_playerOwnerContainer.AddChild(lblPlayer);

		_optPlayerOwner = new OptionButton();
		_optPlayerOwner.Name = "OptPlayerOwner";
		_optPlayerOwner.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
		_optPlayerOwner.AddThemeFontSizeOverride("font_size", 11);

		for (int i = 0; i < PlayerColorConfig.Palette.Length; i++)
		{
			var entry = PlayerColorConfig.Palette[i];
			_optPlayerOwner.AddItem($"{entry.Index} {entry.Name}", i);
		}

		_optPlayerOwner.ItemSelected += (long index) =>
		{
			if (_isUpdatingInspectorUI) return;
			if (GameHost.Instance != null && GameHost.Instance.SelectedEditorObject is Unit3D unit && GodotObject.IsInstanceValid(unit))
			{
				int playerIndex = (int)index;
				GameHost.Instance.SetUnitPlayerExternal(unit, playerIndex);
			}
		};

		_playerOwnerContainer.AddChild(_optPlayerOwner);
		inspectorVBox.AddChild(_playerOwnerContainer);
		inspectorVBox.MoveChild(_playerOwnerContainer, 2);

		_rigStatusContainer = new PanelContainer();
		_rigStatusContainer.Name = "RigStatusContainer";
		_rigStatusContainer.Visible = false;
		_lblRigStatus = new Label();
		_lblRigStatus.Name = "LblRigStatus";
		_lblRigStatus.AddThemeFontSizeOverride("font_size", 11);
		_lblRigStatus.AutowrapMode = TextServer.AutowrapMode.Word;
		_rigStatusContainer.AddChild(_lblRigStatus);
		inspectorVBox.AddChild(_rigStatusContainer);

		_globalOverridesDialog = new GlobalObjectOverridesDialog(this);
		_animationPreviewDialog = new AnimationPreviewDialog(this);
		_weaponVfxDialog = new WeaponVfxDialog(this);
		_modelPickerDialog = new ModelPickerDialog(this);
		_abilityVfxDialog = new AbilityVfxDialog(this);
		_assetManagerDialog = new AssetManagerDialog(this);
		_objectManagerDialog = new ObjectManagerDialog(this);
		_placedObjectsDialog = new PlacedObjectsDialog(this);
		_assetBrowserDialog = new AssetBrowserDialog(this);
		_noiseTextureDialog = new NoiseTextureDialog(this);
		_convertGlbDialog = new ConvertGlbDialog(this);
		_editorSettingsDialog = new EditorSettingsDialog(this);
		_shaderEditorDialog = new ShaderEditorDialog(this);
		_vfxStudioDialog = new VfxStudioDialog(this);
		_proceduralAnimationStudioDialog = new ProceduralAnimationStudioDialog(this);
		_authorSignatureDialog = new AuthorSignatureDialog(this);
		_waterProfileDialog = new WaterProfileDialog(this);
		_environmentConfigDialog = new EnvironmentConfigDialog(this);
		_replaceTextureDialog = new ReplaceTextureDialog(this);
		RefreshWaterSwatches();
		ApplyEditorPreferences(EditorSettingsDialog.CurrentSettings);

		_btnOpenAnimationPreview = new Button();
		_btnOpenAnimationPreview.Name = "BtnOpenAnimationPreview";
		_btnOpenAnimationPreview.Set("icon_max_width", 0);
		_btnOpenAnimationPreview.Text = "✏️ " + TranslationServer.Translate("Edit Animations");
		_btnOpenAnimationPreview.AddThemeFontSizeOverride("font_size", 11);
		_btnOpenAnimationPreview.FocusMode = Control.FocusModeEnum.None;
		_btnOpenAnimationPreview.CustomMinimumSize = new Vector2(0, 28);
		_btnOpenAnimationPreview.Visible = false;
		_btnOpenAnimationPreview.Pressed += () =>
		{
			if (GameHost.Instance != null && GodotObject.IsInstanceValid(GameHost.Instance.SelectedEditorObject))
			{
				_animationPreviewDialog?.OpenForObject(GameHost.Instance.SelectedEditorObject);
			}
		};
		inspectorVBox.AddChild(_btnOpenAnimationPreview);

		_btnOpenGlobalOverrides = new Button();
		_btnOpenGlobalOverrides.Name = "BtnOpenGlobalOverrides";
		_btnOpenGlobalOverrides.Set("icon_max_width", 0);
		_btnOpenGlobalOverrides.Text = "✏️ " + TranslationServer.Translate("Global Overrides");
		_btnOpenGlobalOverrides.AddThemeFontSizeOverride("font_size", 11);
		_btnOpenGlobalOverrides.FocusMode = Control.FocusModeEnum.None;
		_btnOpenGlobalOverrides.CustomMinimumSize = new Vector2(0, 28);
		_btnOpenGlobalOverrides.Visible = false;
		_btnOpenGlobalOverrides.Pressed += () =>
		{
			if (GameHost.Instance != null && GodotObject.IsInstanceValid(GameHost.Instance.SelectedEditorObject))
			{
				_globalOverridesDialog?.OpenForObject(GameHost.Instance.SelectedEditorObject);
			}
		};
		inspectorVBox.AddChild(_btnOpenGlobalOverrides);

		_btnEditVfx = new Button();
		_btnEditVfx.Name = "BtnEditVfx";
		_btnEditVfx.Set("icon_max_width", 0);
		_btnEditVfx.Text = "✨ " + TranslationServer.Translate("Edit VFX");
		_btnEditVfx.AddThemeFontSizeOverride("font_size", 11);
		_btnEditVfx.FocusMode = Control.FocusModeEnum.None;
		_btnEditVfx.CustomMinimumSize = new Vector2(0, 28);
		_btnEditVfx.Visible = false;
		_btnEditVfx.Pressed += () =>
		{
			if (GameHost.Instance != null && GodotObject.IsInstanceValid(GameHost.Instance.SelectedEditorObject) && GameHost.Instance.SelectedEditorObject is ProceduralVfxInstance3D vfx)
			{
				OpenVfxStudioDialog(vfx.Config, (newCfg) =>
				{
					vfx.UpdateConfig(newCfg);
				});
			}
		};
		inspectorVBox.AddChild(_btnEditVfx);

		_btnEditAttachments = new Button();
		_btnEditAttachments.Name = "BtnEditAttachments";
		_btnEditAttachments.Set("icon_max_width", 0);
		_btnEditAttachments.Text = "📎 " + TranslationServer.Translate("Sockets & VFX");
		_btnEditAttachments.AddThemeFontSizeOverride("font_size", 11);
		_btnEditAttachments.FocusMode = Control.FocusModeEnum.None;
		_btnEditAttachments.CustomMinimumSize = new Vector2(0, 28);
		_btnEditAttachments.Visible = false;
		_btnEditAttachments.Pressed += () =>
		{
			if (GameHost.Instance != null && GodotObject.IsInstanceValid(GameHost.Instance.SelectedEditorObject))
			{
				var sel = GameHost.Instance.SelectedEditorObject;
				if (sel is Unit3D u)
				{
					bool isBuilding = u.IsBuilding || (GameHost.BuildingRegistry != null && GameHost.BuildingRegistry.ContainsKey(u.UnitId));
					string defaultSocket = isBuilding ? "Center" : "RightHand";
					OpenObjectAttachmentDialog(u.UnitId, null, defaultSocket, u, (orient) =>
					{
						u.ApplyAllConfiguredAttachments();
					});
				}
			}
		};
		inspectorVBox.AddChild(_btnEditAttachments);
	}

	public WeaponVfxDialog WeaponVfxDialog => _weaponVfxDialog;

	public void OpenWeaponVfxDialog(string weaponId, GameHost.WeaponMetadata weapon, Action<GameHost.WeaponMetadata> onApplied = null)
	{
		if (_weaponVfxDialog == null)
		{
			_weaponVfxDialog = new WeaponVfxDialog(this);
		}
		_weaponVfxDialog.OpenForWeapon(weaponId, weapon, onApplied);
	}

	public void OpenVfxStudioDialog(VfxAttachmentConfig initialConfig = null, Action<VfxAttachmentConfig> onApplied = null)
	{
		if (_vfxStudioDialog == null)
		{
			_vfxStudioDialog = new VfxStudioDialog(this);
		}
		_vfxStudioDialog.OpenForConfig(initialConfig, onApplied);
	}

	private VfxManagerDialog _vfxManagerDialog;

	public void OpenVfxManagerDialog(Action<VfxAttachmentConfig> onSelected = null, string initialVfxId = null)
	{
		if (_vfxManagerDialog == null)
		{
			_vfxManagerDialog = new VfxManagerDialog(this);
		}
		_vfxManagerDialog.Open(onSelected, initialVfxId);
	}

	private ObjectAttachmentDialog _objectAttachmentDialog;

	public void OpenObjectAttachmentDialog(
		string unitId = null, 
		string attachmentId = null, 
		string hand = "RightHand", 
		Node3D sourceModel = null, 
		Action<GameHost.HandAttachmentOrientation> onApplied = null)
	{
		if (_objectAttachmentDialog == null)
		{
			_objectAttachmentDialog = new ObjectAttachmentDialog(this);
		}
		_objectAttachmentDialog.OpenForUnitAndAttachment(unitId, attachmentId, hand, sourceModel, onApplied);
	}

	public void SaveUnitObjectAttachment(string unitId, HumanoidBone hand, string attachmentId, GameHost.HandAttachmentOrientation orientation)
	{
		string handKey = hand switch
		{
			HumanoidBone.LeftHand => "left_hand",
			HumanoidBone.RightHand => "right_hand",
			HumanoidBone.Chest => "chest",
			HumanoidBone.Hips => "root",
			HumanoidBone.Head => "head",
			HumanoidBone.LeftFoot => "left_foot",
			HumanoidBone.RightFoot => "right_foot",
			_ => "right_hand"
		};
		SaveUnitObjectAttachment(unitId, handKey, attachmentId, orientation);
	}

	public void SaveUnitObjectAttachment(string unitId, string socket, string attachmentId, GameHost.HandAttachmentOrientation orientation)
	{
		try
		{
			if (string.IsNullOrEmpty(unitId) || string.IsNullOrEmpty(attachmentId)) return;

			string regKey = unitId;
			bool isBuildingMeta = false;
			if (GameHost.UnitRegistry.ContainsKey(unitId))
			{
				regKey = unitId;
			}
			else if (GameHost.BuildingRegistry != null && GameHost.BuildingRegistry.ContainsKey(unitId))
			{
				regKey = unitId;
				isBuildingMeta = true;
			}
			else
			{
				string cleanId = System.IO.Path.GetFileNameWithoutExtension(unitId);
				if (GameHost.UnitRegistry.ContainsKey(cleanId))
				{
					regKey = cleanId;
				}
				else if (GameHost.BuildingRegistry != null && GameHost.BuildingRegistry.ContainsKey(cleanId))
				{
					regKey = cleanId;
					isBuildingMeta = true;
				}
				else
				{
					foreach (var k in GameHost.UnitRegistry.Keys)
					{
						string kStr = k.ToString();
						if (kStr.Equals(unitId, StringComparison.OrdinalIgnoreCase) ||
							kStr.Equals(cleanId, StringComparison.OrdinalIgnoreCase) ||
							System.IO.Path.GetFileNameWithoutExtension(kStr).Equals(cleanId, StringComparison.OrdinalIgnoreCase))
						{
							regKey = k;
							break;
						}
					}
					if (GameHost.BuildingRegistry != null)
					{
						foreach (var k in GameHost.BuildingRegistry.Keys)
						{
							string kStr = k.ToString();
							if (kStr.Equals(unitId, StringComparison.OrdinalIgnoreCase) ||
								kStr.Equals(cleanId, StringComparison.OrdinalIgnoreCase) ||
								System.IO.Path.GetFileNameWithoutExtension(kStr).Equals(cleanId, StringComparison.OrdinalIgnoreCase))
							{
								regKey = k;
								isBuildingMeta = true;
								break;
							}
						}
					}
				}
			}

			if (isBuildingMeta && GameHost.BuildingRegistry != null && GameHost.BuildingRegistry.TryGetValue(regKey, out var bMeta))
			{
				bMeta.SetObjectAttachment(socket, attachmentId, orientation);
				GameHost.BuildingRegistry[regKey] = bMeta;
			}
			else if (GameHost.UnitRegistry.TryGetValue(regKey, out var uMeta))
			{
				uMeta.SetObjectAttachment(socket, attachmentId, orientation);
				GameHost.UnitRegistry[regKey] = uMeta;
			}

			string wsPath = string.IsNullOrEmpty(_tempWorkspacePath) 
				? ProjectSettings.GlobalizePath(TempWorkspaceGodotPath) 
				: _tempWorkspacePath;
			string metadataPath = MetadataService.ResolveMetadataPath(wsPath);
			if (!System.IO.File.Exists(metadataPath)) return;

			MetadataService.Instance.UpdateMetadata(wsPath, meta =>
			{
				bool updated = meta.UpdateUnit(unitId, u => { u.SetObjectAttachment(socket, attachmentId, orientation); return u; });
				if (!updated) updated = meta.UpdateBuilding(unitId, b => { b.SetObjectAttachment(socket, attachmentId, orientation); return b; });
				if (!updated) updated = meta.UpdateUnit(regKey, u => { u.SetObjectAttachment(socket, attachmentId, orientation); return u; });
				if (!updated) meta.UpdateBuilding(regKey, b => { b.SetObjectAttachment(socket, attachmentId, orientation); return b; });
			});
			_lastMetadataSyncTime = GetLastWriteTimeSafe(metadataPath);

			if (GameHost.Instance != null)
			{
				if (GameHost.Instance.AllUnits != null)
				{
					foreach (var u in GameHost.Instance.AllUnits)
					{
						if (u != null && (u.UnitId == unitId || u.UnitId == regKey || System.IO.Path.GetFileNameWithoutExtension(u.UnitId ?? "").Equals(System.IO.Path.GetFileNameWithoutExtension(unitId), StringComparison.OrdinalIgnoreCase)))
						{
							u.ApplyAllConfiguredAttachments();
						}
					}
				}

				if (GameHost.Instance.SelectedEditorObject is Unit3D selUnit)
				{
					if (selUnit.UnitId == unitId || selUnit.UnitId == regKey || System.IO.Path.GetFileNameWithoutExtension(selUnit.UnitId ?? "").Equals(System.IO.Path.GetFileNameWithoutExtension(unitId), StringComparison.OrdinalIgnoreCase))
					{
						selUnit.ApplyAllConfiguredAttachments();
					}
				}
			}
		}
		catch (Exception ex)
		{
			GD.PrintErr($"[MapEditorHUD] SaveUnitObjectAttachment error: {ex.Message}");
		}
	}

	public void RemoveUnitObjectAttachment(string unitId, string socket, string attachmentId, string? parentAttachmentId = null)
	{
		try
		{
			if (string.IsNullOrEmpty(unitId) || string.IsNullOrEmpty(attachmentId)) return;

			string regKey = unitId;
			bool isBuildingMeta = false;
			if (GameHost.UnitRegistry.ContainsKey(unitId))
			{
				regKey = unitId;
			}
			else if (GameHost.BuildingRegistry != null && GameHost.BuildingRegistry.ContainsKey(unitId))
			{
				regKey = unitId;
				isBuildingMeta = true;
			}
			else
			{
				string cleanId = System.IO.Path.GetFileNameWithoutExtension(unitId);
				if (GameHost.UnitRegistry.ContainsKey(cleanId))
				{
					regKey = cleanId;
				}
				else if (GameHost.BuildingRegistry != null && GameHost.BuildingRegistry.ContainsKey(cleanId))
				{
					regKey = cleanId;
					isBuildingMeta = true;
				}
				else
				{
					foreach (var k in GameHost.UnitRegistry.Keys)
					{
						string kStr = k.ToString();
						if (kStr.Equals(unitId, StringComparison.OrdinalIgnoreCase) ||
							kStr.Equals(cleanId, StringComparison.OrdinalIgnoreCase) ||
							System.IO.Path.GetFileNameWithoutExtension(kStr).Equals(cleanId, StringComparison.OrdinalIgnoreCase))
						{
							regKey = k;
							break;
						}
					}
					if (GameHost.BuildingRegistry != null)
					{
						foreach (var k in GameHost.BuildingRegistry.Keys)
						{
							string kStr = k.ToString();
							if (kStr.Equals(unitId, StringComparison.OrdinalIgnoreCase) ||
								kStr.Equals(cleanId, StringComparison.OrdinalIgnoreCase) ||
								System.IO.Path.GetFileNameWithoutExtension(kStr).Equals(cleanId, StringComparison.OrdinalIgnoreCase))
							{
								regKey = k;
								isBuildingMeta = true;
								break;
							}
						}
					}
				}
			}

			if (isBuildingMeta && GameHost.BuildingRegistry != null && GameHost.BuildingRegistry.TryGetValue(regKey, out var bMeta))
			{
				bMeta.RemoveObjectAttachment(socket, attachmentId, parentAttachmentId);
				GameHost.BuildingRegistry[regKey] = bMeta;
			}
			else if (GameHost.UnitRegistry.TryGetValue(regKey, out var uMeta))
			{
				uMeta.RemoveObjectAttachment(socket, attachmentId, parentAttachmentId);
				GameHost.UnitRegistry[regKey] = uMeta;
			}

			string wsPath = string.IsNullOrEmpty(_tempWorkspacePath) 
				? ProjectSettings.GlobalizePath(TempWorkspaceGodotPath) 
				: _tempWorkspacePath;
			string metadataPath = MetadataService.ResolveMetadataPath(wsPath);
			if (!System.IO.File.Exists(metadataPath)) return;

			MetadataService.Instance.UpdateMetadata(wsPath, meta =>
			{
				bool updated = meta.UpdateUnit(unitId, u => { u.RemoveObjectAttachment(socket, attachmentId, parentAttachmentId); return u; });
				if (!updated) updated = meta.UpdateBuilding(unitId, b => { b.RemoveObjectAttachment(socket, attachmentId, parentAttachmentId); return b; });
				if (!updated) updated = meta.UpdateUnit(regKey, u => { u.RemoveObjectAttachment(socket, attachmentId, parentAttachmentId); return u; });
				if (!updated) meta.UpdateBuilding(regKey, b => { b.RemoveObjectAttachment(socket, attachmentId, parentAttachmentId); return b; });
			});
			_lastMetadataSyncTime = GetLastWriteTimeSafe(metadataPath);

			if (GameHost.Instance != null)
			{
				if (GameHost.Instance.AllUnits != null)
				{
					foreach (var u in GameHost.Instance.AllUnits)
					{
						if (u != null && (u.UnitId == unitId || u.UnitId == regKey || System.IO.Path.GetFileNameWithoutExtension(u.UnitId ?? "").Equals(System.IO.Path.GetFileNameWithoutExtension(unitId), StringComparison.OrdinalIgnoreCase)))
						{
							u.ApplyAllConfiguredAttachments();
						}
					}
				}

				if (GameHost.Instance.SelectedEditorObject is Unit3D selUnit)
				{
					if (selUnit.UnitId == unitId || selUnit.UnitId == regKey || System.IO.Path.GetFileNameWithoutExtension(selUnit.UnitId ?? "").Equals(System.IO.Path.GetFileNameWithoutExtension(unitId), StringComparison.OrdinalIgnoreCase))
					{
						selUnit.ApplyAllConfiguredAttachments();
					}
				}
			}
		}
		catch (Exception ex)
		{
			GD.PrintErr($"[MapEditorHUD] RemoveUnitObjectAttachment error: {ex.Message}");
		}
	}

	public void RestoreUnitObjectAttachments(string targetId, GameHost.UnitObjectAttachments? snapshot)
	{
		try
		{
			if (string.IsNullOrEmpty(targetId)) return;

			string regKey = targetId;
			bool isBuildingMeta = false;
			if (GameHost.UnitRegistry.ContainsKey(targetId))
			{
				regKey = targetId;
			}
			else if (GameHost.BuildingRegistry != null && GameHost.BuildingRegistry.ContainsKey(targetId))
			{
				regKey = targetId;
				isBuildingMeta = true;
			}
			else
			{
				string cleanId = System.IO.Path.GetFileNameWithoutExtension(targetId);
				if (GameHost.UnitRegistry.ContainsKey(cleanId))
				{
					regKey = cleanId;
				}
				else if (GameHost.BuildingRegistry != null && GameHost.BuildingRegistry.ContainsKey(cleanId))
				{
					regKey = cleanId;
					isBuildingMeta = true;
				}
				else
				{
					foreach (var k in GameHost.UnitRegistry.Keys)
					{
						string kStr = k.ToString();
						if (kStr.Equals(targetId, StringComparison.OrdinalIgnoreCase) ||
							kStr.Equals(cleanId, StringComparison.OrdinalIgnoreCase) ||
							System.IO.Path.GetFileNameWithoutExtension(kStr).Equals(cleanId, StringComparison.OrdinalIgnoreCase))
						{
							regKey = k;
							break;
						}
					}
					if (GameHost.BuildingRegistry != null)
					{
						foreach (var k in GameHost.BuildingRegistry.Keys)
						{
							string kStr = k.ToString();
							if (kStr.Equals(targetId, StringComparison.OrdinalIgnoreCase) ||
								kStr.Equals(cleanId, StringComparison.OrdinalIgnoreCase) ||
								System.IO.Path.GetFileNameWithoutExtension(kStr).Equals(cleanId, StringComparison.OrdinalIgnoreCase))
							{
								regKey = k;
								isBuildingMeta = true;
								break;
							}
						}
					}
				}
			}

			if (isBuildingMeta && GameHost.BuildingRegistry != null && GameHost.BuildingRegistry.TryGetValue(regKey, out var bMeta))
			{
				bMeta.ObjectAttachments = (snapshot.HasValue && snapshot.Value.HasAny()) ? snapshot?.Clone() : null;
				GameHost.BuildingRegistry[regKey] = bMeta;
			}
			else if (GameHost.UnitRegistry.TryGetValue(regKey, out var uMeta))
			{
				uMeta.ObjectAttachments = (snapshot.HasValue && snapshot.Value.HasAny()) ? snapshot?.Clone() : null;
				GameHost.UnitRegistry[regKey] = uMeta;
			}

			string wsPath = string.IsNullOrEmpty(_tempWorkspacePath) 
				? ProjectSettings.GlobalizePath(TempWorkspaceGodotPath) 
				: _tempWorkspacePath;
			string metadataPath = MetadataService.ResolveMetadataPath(wsPath);
			if (System.IO.File.Exists(metadataPath))
			{
				MetadataService.Instance.UpdateMetadata(wsPath, meta =>
				{
					var atts = (snapshot.HasValue && snapshot.Value.HasAny()) ? snapshot?.Clone() : null;
					bool updated = meta.UpdateUnit(targetId, u => { u.ObjectAttachments = atts; return u; });
					if (!updated) updated = meta.UpdateBuilding(targetId, b => { b.ObjectAttachments = atts; return b; });
					if (!updated) updated = meta.UpdateUnit(regKey, u => { u.ObjectAttachments = atts; return u; });
					if (!updated) meta.UpdateBuilding(regKey, b => { b.ObjectAttachments = atts; return b; });
				});
				_lastMetadataSyncTime = GetLastWriteTimeSafe(metadataPath);
			}

			if (GameHost.Instance != null)
			{
				if (GameHost.Instance.AllUnits != null)
				{
					foreach (var u in GameHost.Instance.AllUnits)
					{
						if (u != null && (u.UnitId == targetId || u.UnitId == regKey || System.IO.Path.GetFileNameWithoutExtension(u.UnitId ?? "").Equals(System.IO.Path.GetFileNameWithoutExtension(targetId), StringComparison.OrdinalIgnoreCase)))
						{
							u.ApplyAllConfiguredAttachments();
						}
					}
				}

				if (GameHost.Instance.SelectedEditorObject is Unit3D selUnit)
				{
					if (selUnit.UnitId == targetId || selUnit.UnitId == regKey || System.IO.Path.GetFileNameWithoutExtension(selUnit.UnitId ?? "").Equals(System.IO.Path.GetFileNameWithoutExtension(targetId), StringComparison.OrdinalIgnoreCase))
					{
						selUnit.ApplyAllConfiguredAttachments();
					}
				}
			}
		}
		catch (Exception ex)
		{
			GD.PrintErr($"[MapEditorHUD] RestoreUnitObjectAttachments error: {ex.Message}");
		}
	}

	public void SaveAllUnitObjectAttachments(string targetId, GameHost.UnitObjectAttachments? attachments)
	{
		RestoreUnitObjectAttachments(targetId, attachments);
	}

	public void SaveCustomWeaponToMetadata(string weaponId, GameHost.WeaponMetadata weapon)
	{
		try
		{
			string wsPath = string.IsNullOrEmpty(_tempWorkspacePath) 
				? ProjectSettings.GlobalizePath(TempWorkspaceGodotPath) 
				: _tempWorkspacePath;
			string metadataPath = MetadataService.ResolveMetadataPath(wsPath);
			if (!System.IO.File.Exists(metadataPath)) return;

			MetadataService.Instance.UpdateMetadata(wsPath, meta =>
			{
				meta.AddOrUpdateWeapon(weapon);
			});
			_lastMetadataSyncTime = GetLastWriteTimeSafe(metadataPath);
		}
		catch (Exception ex)
		{
			GD.PrintErr($"[MapEditorHUD] SaveCustomWeaponToMetadata error: {ex.Message}");
		}
	}

	public void SaveCustomVfxToMetadata(string vfxId, VfxAttachmentConfig config)
	{
		try
		{
			if (string.IsNullOrEmpty(vfxId) || config == null) return;

			GameHost.VfxRegistry[vfxId] = config.Clone();

			string wsPath = string.IsNullOrEmpty(_tempWorkspacePath) 
				? ProjectSettings.GlobalizePath(TempWorkspaceGodotPath) 
				: _tempWorkspacePath;
			string metadataPath = MetadataService.ResolveMetadataPath(wsPath);
			if (!System.IO.File.Exists(metadataPath)) return;

			MetadataService.Instance.UpdateMetadata(wsPath, meta =>
			{
				meta.AddOrUpdateVfx(config);
			});
			_lastMetadataSyncTime = GetLastWriteTimeSafe(metadataPath);
		}
		catch (Exception ex)
		{
			GD.PrintErr($"[MapEditorHUD] SaveCustomVfxToMetadata error: {ex.Message}");
		}
	}

	public void RemoveCustomVfxFromMetadata(string vfxId)
	{
		try
		{
			if (string.IsNullOrEmpty(vfxId)) return;

			if (GameHost.VfxRegistry != null)
			{
				GameHost.VfxRegistry.Remove(vfxId);
			}

			string wsPath = string.IsNullOrEmpty(_tempWorkspacePath) 
				? ProjectSettings.GlobalizePath(TempWorkspaceGodotPath) 
				: _tempWorkspacePath;
			string metadataPath = MetadataService.ResolveMetadataPath(wsPath);
			if (!System.IO.File.Exists(metadataPath)) return;

			MetadataService.Instance.UpdateMetadata(wsPath, meta =>
			{
				meta.RemoveVfx(vfxId);
			});
			_lastMetadataSyncTime = GetLastWriteTimeSafe(metadataPath);
		}
		catch (Exception ex)
		{
			GD.PrintErr($"[MapEditorHUD] RemoveCustomVfxFromMetadata error: {ex.Message}");
		}
	}

	public void SaveCustomUnitAnimations(string unitId, Dictionary<string, List<GameHost.UnitAnimationEntry>> animations)
	{
		try
		{
			if (string.IsNullOrEmpty(unitId)) return;

			if (GameHost.UnitRegistry.TryGetValue(unitId, out var uMeta))
			{
				uMeta.Animations = animations;
				GameHost.UnitRegistry[unitId] = uMeta;
			}

			string wsPath = string.IsNullOrEmpty(_tempWorkspacePath) 
				? ProjectSettings.GlobalizePath(TempWorkspaceGodotPath) 
				: _tempWorkspacePath;
			string metadataPath = MetadataService.ResolveMetadataPath(wsPath);
			if (!System.IO.File.Exists(metadataPath)) return;

			MetadataService.Instance.UpdateMetadata(wsPath, meta =>
			{
				meta.UpdateUnit(unitId, u => { u.Animations = animations; return u; });
			});
			_lastMetadataSyncTime = GetLastWriteTimeSafe(metadataPath);
		}
		catch (Exception ex)
		{
			GD.PrintErr($"[MapEditorHUD] SaveCustomUnitAnimations error: {ex.Message}");
		}
	}

	public void SaveCustomUnitAnimations(string unitId, Dictionary<string, string[]> animations)
	{
		var converted = new Dictionary<string, List<GameHost.UnitAnimationEntry>>(StringComparer.OrdinalIgnoreCase);
		if (animations != null)
		{
			foreach (var kvp in animations)
			{
				converted[kvp.Key] = (kvp.Value ?? Array.Empty<string>())
					.Select(s => new GameHost.UnitAnimationEntry { Animation = s })
					.ToList();
			}
		}
		SaveCustomUnitAnimations(unitId, converted);
	}

	public void OpenShaderEditorDialog(string shaderKey = "", Action<CustomShaderConfig> onSaved = null)
	{
		if (_shaderEditorDialog == null)
		{
			_shaderEditorDialog = new ShaderEditorDialog(this);
		}
		_shaderEditorDialog.OpenForShader(shaderKey, onSaved);
	}

	public void OpenProceduralAnimationStudioDialog(Realm.Godot.VFX.ProceduralAnimationConfig initialConfig = null, Action<Realm.Godot.VFX.ProceduralAnimationConfig> onApplied = null, string previewModelKey = null)
	{
		if (_proceduralAnimationStudioDialog == null)
		{
			_proceduralAnimationStudioDialog = new ProceduralAnimationStudioDialog(this);
		}
		_proceduralAnimationStudioDialog.OpenForConfig(initialConfig, onApplied, previewModelKey);
	}

	public void OpenModelPickerDialog(string entityId, string fieldName, string domain, string currentPath, Action<string> onApplied = null)
	{
		if (_modelPickerDialog == null)
		{
			_modelPickerDialog = new ModelPickerDialog(this);
		}
		_modelPickerDialog.OpenForEntity(entityId, fieldName, domain, currentPath, onApplied);
	}

	public void OpenAnimationPreviewDialog(string unitId, string modelPath = null)
	{
		if (_animationPreviewDialog == null)
		{
			_animationPreviewDialog = new AnimationPreviewDialog(this);
		}
		_animationPreviewDialog.OpenForUnitId(unitId, modelPath);
	}

	public void SaveEntityModelPathToMetadata(string entityId, string fieldName, string domain, string newModelPath)
	{
		try
		{
			if (string.IsNullOrEmpty(entityId)) return;

			fieldName = string.IsNullOrEmpty(fieldName) ? "ModelPath" : fieldName;
			domain = string.IsNullOrEmpty(domain) ? "units" : domain;

			if (domain.Equals("units", StringComparison.OrdinalIgnoreCase) && GameHost.UnitRegistry.TryGetValue(entityId, out var uMeta))
			{
				if (fieldName == "PortraitModelPath")
				{
					uMeta.PortraitModelPath = newModelPath;
				}
				else
				{
					uMeta.ModelPath = newModelPath;
				}
				GameHost.UnitRegistry[entityId] = uMeta;
			}
			else if (domain.Equals("buildings", StringComparison.OrdinalIgnoreCase) && GameHost.BuildingRegistry.TryGetValue(entityId, out var bMeta))
			{
				if (fieldName == "PortraitModelPath")
				{
					bMeta.PortraitModelPath = newModelPath;
				}
				else
				{
					bMeta.ModelPath = newModelPath;
				}
				GameHost.BuildingRegistry[entityId] = bMeta;
			}
			else if (domain.Equals("resources", StringComparison.OrdinalIgnoreCase) && GameHost.ResourceRegistry.TryGetValue(entityId, out var rMeta))
			{
				if (fieldName == "PortraitModelPath")
				{
					rMeta.PortraitModelPath = newModelPath;
				}
				else
				{
					rMeta.ModelPath = newModelPath;
				}
				GameHost.ResourceRegistry[entityId] = rMeta;
			}
			else if (domain.Equals("props", StringComparison.OrdinalIgnoreCase) && GameHost.PropRegistry.TryGetValue(entityId, out var pMeta))
			{
				pMeta.ModelPath = newModelPath;
				GameHost.PropRegistry[entityId] = pMeta;
			}

			string wsPath = string.IsNullOrEmpty(_tempWorkspacePath) 
				? ProjectSettings.GlobalizePath(TempWorkspaceGodotPath) 
				: _tempWorkspacePath;
			string metadataPath = MetadataService.ResolveMetadataPath(wsPath);
			if (!System.IO.File.Exists(metadataPath)) return;

			MetadataService.Instance.UpdateMetadata(wsPath, meta =>
			{
				switch (domain.ToLowerInvariant())
				{
					case "units":
						meta.UpdateUnit(entityId, u =>
						{
							if (fieldName == "PortraitModelPath") u.PortraitModelPath = newModelPath;
							else u.ModelPath = newModelPath;
							return u;
						});
						break;
					case "buildings":
						meta.UpdateBuilding(entityId, b =>
						{
							if (fieldName == "PortraitModelPath") b.PortraitModelPath = newModelPath;
							else b.ModelPath = newModelPath;
							return b;
						});
						break;
					case "resources":
						meta.UpdateResource(entityId, r =>
						{
							if (fieldName == "PortraitModelPath") r.PortraitModelPath = newModelPath;
							else r.ModelPath = newModelPath;
							return r;
						});
						break;
					case "props":
						meta.UpdateProp(entityId, p =>
						{
							p.ModelPath = newModelPath;
							return p;
						});
						break;
				}
			});
			_lastMetadataSyncTime = GetLastWriteTimeSafe(metadataPath);

			GameHost.Instance?.LoadUnitMetadata(wsPath);
			if (fieldName != "PortraitModelPath")
			{
				GameHost.Instance?.RefreshAllPlacedObjectModels(entityId);
			}
			_entityPaletteController?.SelectCategory(_entityPaletteController.CurrentCategory, triggerAddObject: false);
		}
		catch (Exception ex)
		{
			GD.PrintErr($"[MapEditorHUD] SaveEntityModelPathToMetadata error: {ex.Message}");
		}
	}

	public void OpenAbilityVfxDialog(string abilityId, System.Text.Json.Nodes.JsonObject abilityData, Action<System.Text.Json.Nodes.JsonObject> onApplied = null)
	{
		if (_abilityVfxDialog == null)
		{
			_abilityVfxDialog = new AbilityVfxDialog(this);
		}
		_abilityVfxDialog.OpenForAbility(abilityId, abilityData, onApplied);
	}

	public void SaveCustomAbilityVfxToMetadata(string abilityId, string visualEffect, string castSound, string iconPath, float aoeRadius)
	{
		try
		{
			if (string.IsNullOrEmpty(abilityId)) return;

			string wsPath = string.IsNullOrEmpty(_tempWorkspacePath) 
				? ProjectSettings.GlobalizePath(TempWorkspaceGodotPath) 
				: _tempWorkspacePath;
			string metadataPath = MetadataService.ResolveMetadataPath(wsPath);
			if (!System.IO.File.Exists(metadataPath)) return;

			MetadataService.Instance.UpdateMetadata(wsPath, meta =>
			{
				meta.UpdateAbility(abilityId, ab =>
				{
					ab.VisualEffect = visualEffect;
					ab.CastSound = castSound;
					ab.IconPath = iconPath;
					ab.AreaOfEffectRadius = aoeRadius;
					return ab;
				});
			});
			_lastMetadataSyncTime = GetLastWriteTimeSafe(metadataPath);
		}
		catch (Exception ex)
		{
			GD.PrintErr($"[MapEditorHUD] SaveCustomAbilityVfxToMetadata error: {ex.Message}");
		}
	}

	public bool CloseCurrentlyOpenDialog()
	{
		var confirmOverlay = GetNodeOrNull<Control>("ConfirmationOverlay") ?? UIManager.Instance?.GetNodeOrNull<Control>("ConfirmationOverlay");
		if (confirmOverlay != null && GodotObject.IsInstanceValid(confirmOverlay) && confirmOverlay.IsInsideTree() && !confirmOverlay.IsQueuedForDeletion())
		{
			if (confirmOverlay.HasMeta("CancelAction"))
			{
				var action = confirmOverlay.GetMeta("CancelAction").AsCallable();
				action.Call();
			}
			else
			{
				confirmOverlay.QueueFree();
			}
			return true;
		}

		var genOverlay = GetNodeOrNull<Control>("GenerationOverlay");
		if (genOverlay != null && GodotObject.IsInstanceValid(genOverlay) && genOverlay.IsInsideTree() && !genOverlay.IsQueuedForDeletion())
		{
			genOverlay.QueueFree();
			return true;
		}

		if (_scaleMapDialog != null && GodotObject.IsInstanceValid(_scaleMapDialog) && _scaleMapDialog.IsInsideTree() && !_scaleMapDialog.IsQueuedForDeletion())
		{
			CloseScaleMapDialog();
			return true;
		}

		var pubOverlay = GetNodeOrNull<Control>("PublishInstructionsOverlay");
		if (pubOverlay != null && GodotObject.IsInstanceValid(pubOverlay) && pubOverlay.IsInsideTree() && !pubOverlay.IsQueuedForDeletion())
		{
			pubOverlay.QueueFree();
			return true;
		}

		var creatorOverlay = GetNodeOrNull<Control>("CreatorRegistrationOverlay");
		if (creatorOverlay != null && GodotObject.IsInstanceValid(creatorOverlay) && creatorOverlay.IsInsideTree() && !creatorOverlay.IsQueuedForDeletion())
		{
			creatorOverlay.QueueFree();
			return true;
		}

		var agreementOverlay = GetNodeOrNull<Control>("AgreementOverlay");
		if (agreementOverlay != null && GodotObject.IsInstanceValid(agreementOverlay) && agreementOverlay.IsInsideTree() && !agreementOverlay.IsQueuedForDeletion())
		{
			agreementOverlay.QueueFree();
			UIManager.Instance?.TransitionTo(GameScreen.MainMenu);
			return true;
		}

		if (_helpOverlayPanel != null && GodotObject.IsInstanceValid(_helpOverlayPanel) && _helpOverlayPanel.IsInsideTree() && !_helpOverlayPanel.IsQueuedForDeletion())
		{
			_helpOverlayPanel.QueueFree();
			_helpOverlayPanel = null;
			return true;
		}

		if (FloatingDialogBase.HasAnyDialogOpen)
		{
			return FloatingDialogBase.CloseTopmostDialog();
		}

		return false;
	}

	public override void _Input(InputEvent @event)
	{
		if (@event is InputEventKey keyEvent)
		{
			if (keyEvent.Pressed && !keyEvent.Echo && keyEvent.Keycode == Godot.Key.Escape)
			{
				if (SettingsMenu.IsOpen)
				{
					return;
				}

				if (CloseCurrentlyOpenDialog())
				{
					GetViewport().SetInputAsHandled();
					return;
				}
			}
			if (keyEvent.Keycode == Godot.Key.Tab)
			{
				if (FloatingDialogBase.HasAnyDialogOpen)
				{
					return;
				}
				GetViewport().SetInputAsHandled();
				return;
			}
			if (keyEvent.Pressed && !keyEvent.Echo && keyEvent.Keycode == Godot.Key.Quoteleft)
			{
				var focusOwner = GetViewport().GuiGetFocusOwner();
				if (focusOwner != null && (focusOwner is LineEdit || focusOwner is TextEdit))
				{
					return;
				}
				if (Realm.Godot.UI.WasmConsoleWindow.IsSinglePlayerOrTestMode())
				{
					Realm.Godot.UI.WasmConsoleWindow.Instance.ToggleVisibility();
					GetViewport().SetInputAsHandled();
					return;
				}
			}
			if (keyEvent.Pressed && (keyEvent.Keycode == Godot.Key.Up || keyEvent.Keycode == Godot.Key.Down || 
				keyEvent.Keycode == Godot.Key.Left || keyEvent.Keycode == Godot.Key.Right))
			{
				var focusOwner = GetViewport().GuiGetFocusOwner();
				if (focusOwner != null && (focusOwner is LineEdit || focusOwner is TextEdit))
				{
					return;
				}
				GetViewport().SetInputAsHandled();
			}
		}
	}

	private void ExportMapAction()
	{
		if (GameHost.Instance == null) return;

		string wsPath = !string.IsNullOrEmpty(_tempWorkspacePath) ? _tempWorkspacePath : MapWorkspaceService.GetActiveWorkspacePath();
		var (assetsValid, missingAssets) = MapAssetHelper.ValidateWorkspaceAssets(wsPath);
		if (!assetsValid)
		{
			ShowFeedback(string.Format(TranslationServer.Translate("Export failed: Map is missing required asset files:\n{0}"), string.Join(", ", missingAssets.Take(4)) + (missingAssets.Count > 4 ? "..." : "")));
			AppendWasmConsoleLog($"[ERROR] Export failed. Missing required asset files:\n  {string.Join("\n  ", missingAssets)}");
			return;
		}

		var (sizesValid, oversizedAssets) = MapAssetHelper.ValidateWorkspaceAssetSizes(wsPath);
		if (!sizesValid)
		{
			string oversizedSummary = string.Join("\n", oversizedAssets.Take(4).Select(o => $"• {o.RelativePath} ({o.SizeMB:F2} MB > 15 MB)")) + (oversizedAssets.Count > 4 ? "\n..." : "");
			ShowFeedback(string.Format(TranslationServer.Translate("Export failed: Assets exceed maximum 15 MB size limit:\n{0}"), oversizedSummary));
			AppendWasmConsoleLog($"[ERROR] Export failed. Assets exceed maximum 15 MB size limit:\n{string.Join("\n", oversizedAssets.Select(o => $"  {o.RelativePath} ({o.SizeMB:F2} MB)"))}");
			return;
		}

		string mapTitle = GetMapNameFromMetadata();
		string cleanMapName = string.Join("_", mapTitle.Split(System.IO.Path.GetInvalidFileNameChars(), StringSplitOptions.RemoveEmptyEntries)).Trim();
		if (string.IsNullOrEmpty(cleanMapName) || cleanMapName.Equals("Untitled Map", StringComparison.OrdinalIgnoreCase)) cleanMapName = "MapExport";

		string mapVersion = GetMapVersionFromMetadata();
		string cleanMapVersion = string.Join("_", mapVersion.Split(System.IO.Path.GetInvalidFileNameChars(), StringSplitOptions.RemoveEmptyEntries)).Trim();
		if (string.IsNullOrEmpty(cleanMapVersion)) cleanMapVersion = "1.0.0";

		string manifestBlake3 = GetManifestBlake3();
		string normHash = !string.IsNullOrEmpty(manifestBlake3) ? ContentAddressableStorage.NormalizeBlake3Hash(manifestBlake3) : string.Empty;
		string shortHash = normHash.Length >= 4 ? normHash.Substring(0, 4) : (normHash.Length > 0 ? normHash : "0000");

		string defaultFileName = $"{cleanMapName}_{cleanMapVersion}_{shortHash}.rmap";
		string initialDir = GetInitialDirectory();

		var err = DisplayServer.FileDialogShow(
			TranslationServer.Translate("Export Map Package (.rmap)"),
			initialDir,
			defaultFileName,
			false,
			DisplayServer.FileDialogMode.SaveFile,
			new[] { "*.rmap ; Realm Map Package (*.rmap)" },
			Callable.From((bool status, string[] selectedPaths, int selectedFilterIndex) => {
				if (status && selectedPaths.Length > 0)
				{
					string destinationPath = selectedPaths[0];
					if (!destinationPath.EndsWith(".rmap", StringComparison.OrdinalIgnoreCase))
					{
						destinationPath += ".rmap";
					}
					_ = ExportMapPackageAsync(destinationPath);
				}
			})
		);

		if (err != Error.Ok)
		{
			string fallbackPath = System.IO.Path.Combine(initialDir, defaultFileName);
			_ = ExportMapPackageAsync(fallbackPath);
		}
	}

	public async System.Threading.Tasks.Task ExportMapPackageAsync(string destinationPath, bool fullExport = false)
	{
		if (GameHost.Instance == null) return;

		if (System.IO.Directory.Exists(destinationPath))
		{
			string mapTitle = GetMapNameFromMetadata();
			string cleanMapName = string.Join("_", mapTitle.Split(System.IO.Path.GetInvalidFileNameChars(), StringSplitOptions.RemoveEmptyEntries)).Trim();
			if (string.IsNullOrEmpty(cleanMapName) || cleanMapName.Equals("Untitled Map", StringComparison.OrdinalIgnoreCase)) cleanMapName = "MapExport";

			string mapVersion = GetMapVersionFromMetadata();
			string cleanMapVersion = string.Join("_", mapVersion.Split(System.IO.Path.GetInvalidFileNameChars(), StringSplitOptions.RemoveEmptyEntries)).Trim();
			if (string.IsNullOrEmpty(cleanMapVersion)) cleanMapVersion = "1.0.0";

			string manifestBlake3 = GetManifestBlake3();
			string normHash = !string.IsNullOrEmpty(manifestBlake3) ? ContentAddressableStorage.NormalizeBlake3Hash(manifestBlake3) : string.Empty;
			string shortHash = normHash.Length >= 4 ? normHash.Substring(0, 4) : (normHash.Length > 0 ? normHash : "0000");

			destinationPath = System.IO.Path.Combine(destinationPath, $"{cleanMapName}_{cleanMapVersion}_{shortHash}.rmap");
		}

		var popup = new Panel();
		popup.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
		popup.AddThemeStyleboxOverride("panel", UIStyle.CreateBgGradient());
		popup.ZIndex = 1100;
		AddChild(popup);

		var cardPanel = new Panel();
		cardPanel.CustomMinimumSize = new Vector2(560, 260);
		cardPanel.SetAnchorsAndOffsetsPreset(LayoutPreset.Center);
		cardPanel.AddThemeStyleboxOverride("panel", UIStyle.CreateStonePanel(true));
		popup.AddChild(cardPanel);

		var vbox = new VBoxContainer();
		vbox.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
		vbox.CustomMinimumSize = new Vector2(520, 220);
		vbox.SizeFlagsHorizontal = SizeFlags.ExpandFill;
		vbox.SizeFlagsVertical = SizeFlags.ExpandFill;
		vbox.AddThemeConstantOverride("separation", 10);
		cardPanel.AddChild(vbox);

		vbox.AddChild(new Control { CustomMinimumSize = new Vector2(0, 10) });

		var titleLabel = new Label();
		UIStyle.ApplyTitle(titleLabel, "📦 " + TranslationServer.Translate("EXPORTING MAP PACKAGE (.RMAP)"), 20);
		titleLabel.HorizontalAlignment = HorizontalAlignment.Center;
		vbox.AddChild(titleLabel);

		var descLabel = new Label();
		descLabel.Text = string.Format(TranslationServer.Translate("Destination: {0}"), System.IO.Path.GetFileName(destinationPath));
		descLabel.HorizontalAlignment = HorizontalAlignment.Center;
		descLabel.AddThemeFontSizeOverride("font_size", 13);
		descLabel.AddThemeColorOverride("font_color", UIStyle.ColorGold);
		vbox.AddChild(descLabel);

		vbox.AddChild(new Control { CustomMinimumSize = new Vector2(0, 5) });

		var progressBar = new ProgressBar();
		progressBar.CustomMinimumSize = new Vector2(480, 24);
		progressBar.MinValue = 0;
		progressBar.MaxValue = 100;
		progressBar.Value = 0;
		vbox.AddChild(progressBar);

		var statusLabel = new Label();
		statusLabel.Text = TranslationServer.Translate("Preparing export...");
		statusLabel.HorizontalAlignment = HorizontalAlignment.Center;
		statusLabel.AddThemeFontSizeOverride("font_size", 13);
		statusLabel.AddThemeColorOverride("font_color", new Color(0.9f, 0.9f, 0.95f));
		vbox.AddChild(statusLabel);

		vbox.AddChild(new Control { CustomMinimumSize = new Vector2(0, 5) });

		var buttonRow = new HBoxContainer();
		buttonRow.Alignment = BoxContainer.AlignmentMode.Center;
		vbox.AddChild(buttonRow);

		var closeBtn = new Button();
		closeBtn.Flat = false;
		closeBtn.AddThemeConstantOverride("icon_max_width", 0);
		closeBtn.AddThemeStyleboxOverride("normal", UIStyle.CreateButtonNormal());
		closeBtn.AddThemeStyleboxOverride("hover", UIStyle.CreateButtonHover());
		closeBtn.AddThemeStyleboxOverride("pressed", UIStyle.CreateButtonPressed());
		closeBtn.AddThemeStyleboxOverride("focus", new StyleBoxEmpty());
		UIStyle.ApplyButtonText(closeBtn, TranslationServer.Translate("CLOSE"), 14);
		closeBtn.CustomMinimumSize = new Vector2(130, 36);
		closeBtn.Visible = false;
		buttonRow.AddChild(closeBtn);

		closeBtn.Pressed += () =>
		{
			UIManager.Instance?.PlayClickSound();
			if (GodotObject.IsInstanceValid(popup))
			{
				popup.QueueFree();
			}
		};

		Realm.Godot.UI.WasmConsoleWindow.Instance.Hide();
		_wasmHasErrors = false;
		Action<string> logHandler = line => AppendWasmConsoleLog(line);
		Realm.Godot.WasmRuntime.OnWasmLog += logHandler;

		try
		{
			progressBar.Value = 10;
			statusLabel.Text = TranslationServer.Translate("Saving map state & workspace...");
			await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);

			string tempTerrainPath = System.IO.Path.Combine(_tempWorkspacePath, "terrain.json");
			MapWorkspaceService.EnsureLicenseFile(_tempWorkspacePath);
			GameHost.Instance.SaveMapToFile(tempTerrainPath, performReload: false);
			GameHost.Instance.EditorHasUnsavedChanges = false;

			if (OperatingSystem.IsWindows())
			{
				await VSCodeManager.Instance.SaveAllOpenFilesAsync();
			}

			progressBar.Value = 15;
			statusLabel.Text = TranslationServer.Translate("Verifying map assets...");
			await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);

			var (assetsValid, missingAssets) = MapAssetHelper.ValidateWorkspaceAssets(_tempWorkspacePath);
			if (!assetsValid)
			{
				progressBar.Value = 100;
				statusLabel.Text = "❌ " + TranslationServer.Translate("Missing required asset files. Export aborted.");
				statusLabel.AddThemeColorOverride("font_color", new Color(0.95f, 0.3f, 0.3f));
				closeBtn.Visible = true;
				ShowFeedback(string.Format(TranslationServer.Translate("Export failed: Map is missing required asset files:\n{0}"), string.Join(", ", missingAssets.Take(4)) + (missingAssets.Count > 4 ? "..." : "")));
				AppendWasmConsoleLog($"[ERROR] Export aborted. Missing required asset files:\n  {string.Join("\n  ", missingAssets)}");
				return;
			}

			var (sizesValid, oversizedAssets) = MapAssetHelper.ValidateWorkspaceAssetSizes(_tempWorkspacePath);
			if (!sizesValid)
			{
				progressBar.Value = 100;
				statusLabel.Text = "❌ " + TranslationServer.Translate("Asset exceeds 15 MB size limit. Export aborted.");
				statusLabel.AddThemeColorOverride("font_color", new Color(0.95f, 0.3f, 0.3f));
				closeBtn.Visible = true;
				string oversizedSummary = string.Join("\n", oversizedAssets.Take(4).Select(o => $"• {o.RelativePath} ({o.SizeMB:F2} MB > 15 MB)")) + (oversizedAssets.Count > 4 ? "\n..." : "");
				ShowFeedback(string.Format(TranslationServer.Translate("Export failed: Assets exceed maximum 15 MB size limit:\n{0}"), oversizedSummary));
				AppendWasmConsoleLog($"[ERROR] Export aborted. Assets exceed maximum 15 MB size limit:\n{string.Join("\n", oversizedAssets.Select(o => $"  {o.RelativePath} ({o.SizeMB:F2} MB)"))}");
				return;
			}

			progressBar.Value = 20;
			statusLabel.Text = TranslationServer.Translate("Compiling WASM map script...");
			await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);

			await CompileAndSignMapAsync(_tempWorkspacePath, skipAttribution: true);

			if (_wasmHasErrors)
			{
				progressBar.Value = 100;
				statusLabel.Text = "❌ " + TranslationServer.Translate("WASM compilation failed. Export aborted.");
				statusLabel.AddThemeColorOverride("font_color", new Color(0.95f, 0.3f, 0.3f));
				closeBtn.Visible = true;
				ShowFeedback(TranslationServer.Translate("WASM compilation failed. Export aborted."));
				return;
			}

			var csprojFiles = System.IO.Directory.GetFiles(_tempWorkspacePath, "*.csproj", System.IO.SearchOption.TopDirectoryOnly);
			if (csprojFiles.Length > 0 || System.IO.File.Exists(System.IO.Path.Combine(_tempWorkspacePath, "MapScript.cs")))
			{
				string binDir = System.IO.Path.Combine(_tempWorkspacePath, "bin");
				string wasmPath = null;
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

				if (string.IsNullOrEmpty(wasmPath) || !System.IO.File.Exists(wasmPath))
				{
					_wasmHasErrors = true;
					progressBar.Value = 100;
					statusLabel.Text = "❌ " + TranslationServer.Translate("Compiled WASM binary missing. Export aborted.");
					statusLabel.AddThemeColorOverride("font_color", new Color(0.95f, 0.3f, 0.3f));
					closeBtn.Visible = true;
					ShowFeedback(TranslationServer.Translate("Compiled WASM binary missing. Export aborted."));
					return;
				}
			}

			progressBar.Value = 35;
			statusLabel.Text = TranslationServer.Translate("Optimizing 3D assets...");
			await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);

			await MapWorkspaceService.EnsureGlbAssetsOptimizedCooperativeAsync(_tempWorkspacePath, async (current, total, fileName) =>
			{
				float fraction = total > 0 ? (float)current / total : 1.0f;
				progressBar.Value = 35 + fraction * 20;
				statusLabel.Text = string.Format(TranslationServer.Translate("Optimizing 3D model {0}/{1}: {2}..."), current, total, fileName);
				await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
			});

			progressBar.Value = 55;
			statusLabel.Text = TranslationServer.Translate("Converting textures...");
			await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);

			await MapWorkspaceService.EnsurePngAssetsConvertedCooperativeAsync(_tempWorkspacePath, async (current, total, fileName) =>
			{
				float fraction = total > 0 ? (float)current / total : 1.0f;
				progressBar.Value = 55 + fraction * 15;
				statusLabel.Text = string.Format(TranslationServer.Translate("Converting texture {0}/{1}: {2}..."), current, total, fileName);
				await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
			});

			progressBar.Value = 70;
			statusLabel.Text = TranslationServer.Translate("Generating map thumbnail...");
			await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);

			if (_minimapController != null)
			{
				await _minimapController.GenerateAndSaveMinimapThumbnailAsync(_tempWorkspacePath);
			}

			progressBar.Value = 75;
			statusLabel.Text = TranslationServer.Translate("Generating manifest & indexing asset hashes...");
			await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);

			string mapTitle = GetMapNameFromMetadata();
			string mapVersion = GetMapVersionFromMetadata();
			string metaJsonPath = System.IO.Path.Combine(_tempWorkspacePath, "metadata.json");
			if (System.IO.File.Exists(metaJsonPath))
			{
				try
				{
					var doc = JsonNode.Parse(System.IO.File.ReadAllText(metaJsonPath));
					if (doc != null)
					{
						if (doc["MapProperties"] is JsonObject props)
						{
							mapTitle = props["MapTitle"]?.ToString() ?? props["MapName"]?.ToString() ?? mapTitle;
							mapVersion = props["MapVersion"]?.ToString() ?? props["Version"]?.ToString() ?? mapVersion;
						}
						else
						{
							mapTitle = doc["MapName"]?.ToString() ?? doc["MapTitle"]?.ToString() ?? mapTitle;
							mapVersion = doc["Version"]?.ToString() ?? doc["MapVersion"]?.ToString() ?? mapVersion;
						}
					}
				}
				catch { }
			}

			string author = LobbyManager.Instance?.AuthenticatedUsername ?? "MapAuthor";
			var manifest = MapManifest.CreateFromDirectory(_tempWorkspacePath, mapTitle, author, mapVersion);
			string manifestJsonPath = System.IO.Path.Combine(_tempWorkspacePath, "manifest.json");
			System.IO.File.WriteAllText(manifestJsonPath, manifest.ToJson());

			progressBar.Value = 80;
			statusLabel.Text = TranslationServer.Translate("Compressing package into .rmap archive...");
			await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);

			var excludedPaths = !fullExport ? MapNormalizationHelper.GetExcludedRelativePaths(_tempWorkspacePath) : null;
			long lastProgressUpdateTicks = 0;
			await System.Threading.Tasks.Task.Run(() =>
			{
				MapArchiveHelper.CreateRmapArchive(_tempWorkspacePath, destinationPath, (pct, file) =>
				{
					long now = System.Environment.TickCount64;
					if (now - lastProgressUpdateTicks < 50 && pct < 1.0f)
					{
						return;
					}
					lastProgressUpdateTicks = now;
					string fileName = System.IO.Path.GetFileName(file);
					Callable.From(() =>
					{
						if (GodotObject.IsInstanceValid(progressBar) && GodotObject.IsInstanceValid(statusLabel))
						{
							progressBar.Value = 80 + pct * 18;
							statusLabel.Text = string.Format(TranslationServer.Translate("Compressing {0} ({1}%)..."), fileName, (int)(pct * 100));
						}
					}).CallDeferred();
				}, compressionLevel: 1, fullExport: fullExport, excludedRelativePaths: excludedPaths);
			});

			progressBar.Value = 100;
			statusLabel.Text = "✓ " + TranslationServer.Translate("Export completed successfully!");
			statusLabel.AddThemeColorOverride("font_color", new Color(0.3f, 0.9f, 0.3f));
			await ToSignal(GetTree().CreateTimer(0.6f), SceneTreeTimer.SignalName.Timeout);

			if (GodotObject.IsInstanceValid(popup))
			{
				popup.QueueFree();
			}

			ShowFeedback(string.Format(TranslationServer.Translate("Map exported successfully to {0}!"), System.IO.Path.GetFileName(destinationPath)));
			UIManager.Instance?.ShowConfirmationDialog(
				string.Format(TranslationServer.Translate("Map exported successfully to {0}."), destinationPath),
				() => { },
				confirmText: "OK",
				showCancel: false
			);
		}
		catch (Exception ex)
		{
			SetWasmConsoleStatus($"❌ Export Failed: {ex.Message}", new Color(1.0f, 0.3f, 0.3f));
			AppendWasmConsoleLog($"[ERROR] Export failed: {ex}");
			ShowFeedback(string.Format(TranslationServer.Translate("Export failed: {0}"), ex.Message));
			if (GodotObject.IsInstanceValid(statusLabel))
			{
				statusLabel.Text = $"❌ {string.Format(TranslationServer.Translate("Export failed: {0}"), ex.Message)}";
				statusLabel.AddThemeColorOverride("font_color", new Color(0.95f, 0.3f, 0.3f));
			}
			if (GodotObject.IsInstanceValid(closeBtn))
			{
				closeBtn.Visible = true;
			}
		}
		finally
		{
			Realm.Godot.WasmRuntime.OnWasmLog -= logHandler;
			if (_wasmHasErrors)
			{
				Realm.Godot.UI.WasmConsoleWindow.Instance.ShowConsole();
			}
			else
			{
				Realm.Godot.UI.WasmConsoleWindow.Instance.Hide();
			}
		}
	}

	private async void TestMapAction()
	{
		if (GameHost.Instance == null) return;

		if (GameHost.Instance.AllUnits.Count == 0)
		{
			ShowConfirmationDialog(
				"Warning: You have not placed any units, you won't see anything due to Shroud.",
				async () => await ProceedToTestMap(),
				"Okay",
				"Cancel"
			);
		}
		else
		{
			await ProceedToTestMap();
		}
	}

	public async System.Threading.Tasks.Task ProceedToTestMap()
	{
		if (GameHost.Instance == null) return;

		_wasmHasErrors = false;
		ShowWasmConsoleModal();
		Action<string> logHandler = line => AppendWasmConsoleLog(line);
		Realm.Godot.WasmRuntime.OnWasmLog += logHandler;

		try
		{
			var camera = GameHost.Instance.MainCamera as CameraControl;
			if (camera != null)
			{
				SavedCameraPosition = camera.Position;
				if (GameHost.Instance.EcsWorld != null && GameHost.Instance.EcsWorld.IsAlive(GameHost.Instance.WorldEntity) && GameHost.Instance.EcsWorld.Has<Realm.Ecs.Components.Core.CameraState>(GameHost.Instance.WorldEntity))
				{
					var state = GameHost.Instance.EcsWorld.Get<Realm.Ecs.Components.Core.CameraState>(GameHost.Instance.WorldEntity);
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
			SavedGridMode = GameHost.Instance.EditorGridMode;
			SavedActiveTool = GameHost.Instance.ActiveEditorTool;
			SavedActivePlaceId = GameHost.Instance.ActivePlaceId;
			SavedCameraBoundsVisible = GameHost.Instance.EditorCameraBoundsVisible;
			SavedDisableShadows = GameHost.Instance.EditorDisableShadows;
			SavedEntityCategory = _entityPaletteController?.CurrentCategory ?? "";
			SavedBrushRadius = (float)_sldBrushSize.Value;
			SavedBrushStrength = (float)_sldBrushStrength.Value;

			string tempTerrainPath = System.IO.Path.Combine(_tempWorkspacePath, "terrain.json");

			GameHost.Instance.SaveMapToFile(tempTerrainPath);
			GameHost.Instance.EditorHasUnsavedChanges = false;
			InvalidateMetadataCache();

			if (OperatingSystem.IsWindows())
			{
				SetWasmConsoleStatus("Auto-saving modified files in VSCode...", UIStyle.ColorCyanGlow);
				AppendWasmConsoleLog("[VSCode] Requesting auto-save of all open workspace files...");
				await VSCodeManager.Instance.SaveAllOpenFilesAsync();
			}

			SetWasmConsoleStatus("Compiling WASM map script...", UIStyle.ColorCyanGlow);
			AppendWasmConsoleLog("=== WASM COMPILATION PIPELINE STARTED ===");

			// Compile map script DLL (skip attribution/signing during test mode)
			await CompileAndSignMapAsync(_tempWorkspacePath, skipAttribution: true);

			if (_wasmHasErrors)
			{
				SetWasmConsoleStatus("❌ WASM Compilation Failed", new Color(1.0f, 0.3f, 0.3f));
				AppendWasmConsoleLog("[ERROR] Map script compilation failed. Test mode aborted.");
				return;
			}

			// Find compiled map script WASM
			string binDir = System.IO.Path.Combine(_tempWorkspacePath, "bin");
			string wasmPath = null;
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
			if (System.IO.File.Exists(wasmPath))
			{
				GameHost.PendingMapScriptPath = wasmPath;
				AppendWasmConsoleLog($"[WASM] Located compiled WASM binary: {System.IO.Path.GetFileName(wasmPath)}");
			}
			else
			{
				_wasmHasErrors = true;
				SetWasmConsoleStatus("❌ WASM Compilation Failed: Output binary missing", new Color(1.0f, 0.3f, 0.3f));
				AppendWasmConsoleLog($"[ERROR] Could not find compiled WASM in {binDir}. Test mode aborted.");
				return;
			}

			SetWasmConsoleStatus("Launching test mode...", UIStyle.ColorCyanGlow);
			AppendWasmConsoleLog("=== LAUNCHING GAME ENGINE ===");

			if (UIManager.Instance != null)
			{
				await UIManager.Instance.ApplyWindowSettings(GameSettings.WindowModeIdx, GameSettings.ResolutionIdx);
			}
			GameHost.Instance.ExitMapEditorMode();
			IsTestMode = true;

			if (UIManager.Instance != null)
			{
				UIManager.Instance.TransitionTo(GameScreen.InGameHUD);
			}

			if (LobbyManager.Instance != null)
			{
				LobbyManager.Instance.HostSinglePlayerGame(TempWorkspaceGodotPath, "Test Map");
			}

			// Close console modal once InGameHUD has started
			await System.Threading.Tasks.Task.Delay(500);
			CloseWasmConsoleModal();
		}
		catch (Exception ex)
		{
			_wasmHasErrors = true;
			SetWasmConsoleStatus("❌ Error launching test mode", new Color(1.0f, 0.3f, 0.3f));
			AppendWasmConsoleLog($"[RUNTIME EXCEPTION] {ex}");
		}
		finally
		{
			Realm.Godot.WasmRuntime.OnWasmLog -= logHandler;
		}
	}

	private readonly Dictionary<int, Texture2D> _swatchTextureCache = new();

	private Texture2D GetSwatchTexture(int i)
	{
		if (_swatchTextureCache.TryGetValue(i, out var cached) && cached != null && GodotObject.IsInstanceValid(cached))
		{
			return cached;
		}

		Texture2D result = LoadSwatchTextureInternal(i);
		if (result != null)
		{
			_swatchTextureCache[i] = result;
		}
		return result;
	}

	private Texture2D LoadSwatchTextureInternal(int i)
	{
		string wsPath = string.IsNullOrEmpty(_tempWorkspacePath) 
			? ProjectSettings.GlobalizePath(TempWorkspaceGodotPath) 
			: _tempWorkspacePath;
		if (i >= 0 && i < _swatchDisplayNames.Count && _swatchDisplayNames[i].EndsWith("(Empty)"))
		{
			return null;
		}
		string localRtex = "";
		if (i >= 0 && i < _swatchPaths.Count && !string.IsNullOrEmpty(_swatchPaths[i]) && System.IO.File.Exists(_swatchPaths[i]))
		{
			localRtex = _swatchPaths[i];
		}
		else
		{
			string texName = (i >= 0 && i < _swatchDisplayNames.Count) ? _swatchDisplayNames[i] : $"swatch_{i}";
			if (string.IsNullOrEmpty(texName) || texName.EndsWith("(Empty)")) return null;
			string cleanName = texName.ToLowerInvariant().Replace(" ", "_") + ".rtex";
			localRtex = System.IO.Path.Combine(wsPath, "Assets", "textures", cleanName);
			if (!System.IO.File.Exists(localRtex))
			{
				localRtex = System.IO.Path.Combine(wsPath, cleanName);
			}
		}
		if (System.IO.File.Exists(localRtex))
		{
			try
			{
				byte[] bytes = System.IO.File.ReadAllBytes(localRtex);
				byte[]? webpBytes = Realm.Shared.Textures.RtexFile.GetLayer(bytes, 0);
				if (webpBytes != null && webpBytes.Length > 0)
				{
					var img = Image.CreateEmpty(1, 1, false, Image.Format.Rgba8);
					if (img.LoadWebpFromBuffer(webpBytes) != Error.Ok)
					{
						img.LoadPngFromBuffer(webpBytes);
					}
					return ImageTexture.CreateFromImage(img);
				}
			}
			catch (Exception ex)
			{
				GD.PrintErr($"Failed to load swatch preview: {ex.Message}");
			}
		}
		if (i >= 0 && i < _swatchPaths.Count && ResourceLoader.Exists(_swatchPaths[i]))
		{
			return GD.Load<Texture2D>(_swatchPaths[i]);
		}
		return null;
	}

	private void ImportTextureAction()
	{
		if (GameHost.Instance == null) return;
		int selectedIdx = GameHost.Instance.EditorPaintTextureIndex;
		if (selectedIdx < 0 || selectedIdx >= _swatchDisplayNames.Count)
		{
			ShowFeedback(TranslationServer.Translate("Please select a texture slot first"));
			return;
		}

		OpenAssetBrowser("Import Texture Image", new[] { ".rtex", ".png", ".webp" }, imagePath =>
		{
			if (selectedIdx >= 0 && selectedIdx < _swatchPaths.Count && !string.IsNullOrEmpty(_swatchPaths[selectedIdx]))
			{
				string cleanPrevName = _swatchDisplayNames[selectedIdx];
				string msg = string.Format(TranslationServer.Translate("Replacing texture in Slot {0} ({1}) will update all areas of the terrain painted with this slot. Do you want to proceed?"), selectedIdx, cleanPrevName);
				ShowConfirmationDialog(msg, () =>
				{
					ImportTextureFile(imagePath, selectedIdx);
				}, confirmText: "REPLACE", cancelText: "CANCEL");
			}
			else
			{
				ImportTextureFile(imagePath, selectedIdx);
			}
		}, requireRealmMetadata: false);
	}

	private void ImportTextureFile(string imagePath, int index)
	{
		string cleanBaseName = System.IO.Path.GetFileNameWithoutExtension(imagePath).ToLowerInvariant().Replace(" ", "_") + ".rtex";
		string wsPath = string.IsNullOrEmpty(_tempWorkspacePath) 
			? ProjectSettings.GlobalizePath(TempWorkspaceGodotPath) 
			: _tempWorkspacePath;
		string texDir = System.IO.Path.Combine(wsPath, "Assets", "textures");
		System.IO.Directory.CreateDirectory(texDir);
		string outputRtex = System.IO.Path.Combine(texDir, cleanBaseName);
		ShowFeedback(TranslationServer.Translate("Importing texture..."));
		try
		{
			string ext = System.IO.Path.GetExtension(imagePath);
			if (ext.Equals(".rtex", StringComparison.OrdinalIgnoreCase))
			{
				System.IO.File.Copy(imagePath, outputRtex, true);
			}
			else if (GameHost.Instance != null && GameHost.Instance.GroundTerrain != null)
			{
				GameHost.Instance.GroundTerrain.ProcessAndSaveRawTexture(imagePath, outputRtex);
			}

			if (System.IO.File.Exists(outputRtex))
			{
				byte[] rtexBytes = System.IO.File.ReadAllBytes(outputRtex);
				string blake3 = RealmMetadataHelper.ComputeBlake3(rtexBytes, ".rtex");
				UpdateMetadataJsonAsset("textures", cleanBaseName, blake3, targetSlot: index);
			}
			if (GameHost.Instance != null && GameHost.Instance.GroundTerrain != null)
			{
				GameHost.Instance.GroundTerrain.ReloadTerrainTextures(true);
			}
			SetupTextureSwatches(false);
			ShowFeedback(string.Format(TranslationServer.Translate("Successfully imported custom texture for {0}!"), cleanBaseName));
		}
		catch (Exception ex)
		{
			ShowFeedback(string.Format(TranslationServer.Translate("Failed to import texture: {0}"), ex.Message));
			GD.PrintErr($"Failed to import texture: {ex.Message}");
		}
	}

	public void OpenNoiseTextureDialog(Action<string> onSaved = null)
	{
		_noiseTextureDialog?.OpenWithCallback(onSaved);
	}

	public void OpenConvertGlbDialog(string? initialPath = null, string? initialSubCat = null, Action<string>? onConverted = null)
	{
		Action<string> chainedCallback = (resultPath) =>
		{
			onConverted?.Invoke(resultPath);
			_assetManagerDialog?.RefreshAssetListAndSelect(resultPath);
			_objectManagerDialog?.RefreshObjectList();
		};
		_convertGlbDialog?.OpenWithPreset(initialPath, initialSubCat, chainedCallback);
	}

	public void OpenPlacedObjectsDialog()
	{
		if (_placedObjectsDialog == null)
		{
			_placedObjectsDialog = new PlacedObjectsDialog(this);
		}
		_placedObjectsDialog.OpenDialog();
	}

	public void OpenEditorSettingsDialog()
	{
		_editorSettingsDialog?.OpenDialog();
	}

	public void ApplyEditorPreferences(EditorPreferencesData prefs)
	{
		if (prefs == null) return;

		var screenFrame = GetNodeOrNull<TextureRect>("MapEditorScreenFrame");
		if (screenFrame != null)
		{
			screenFrame.Visible = !prefs.HideChromeBorderOverlay;
		}

		var leftPanel = GetNodeOrNull<Panel>("LeftSlidePanel");
		var rightPanel = GetNodeOrNull<Panel>("RightSlidePanel");
		var topBar = GetNodeOrNull<PanelContainer>("TopBar");
		var minimap = GetNodeOrNull<PanelContainer>("MinimapFrame");

		if (prefs.HideChromeBorderOverlay)
		{
			if (leftPanel != null) leftPanel.SelfModulate = new Color(1, 1, 1, 0.45f);
			if (rightPanel != null) rightPanel.SelfModulate = new Color(1, 1, 1, 0.45f);
			if (topBar != null) topBar.SelfModulate = new Color(1, 1, 1, 0.0f);
			if (minimap != null) minimap.SelfModulate = new Color(1, 1, 1, 0.5f);
		}
		else
		{
			if (leftPanel != null) leftPanel.SelfModulate = Colors.White;
			if (rightPanel != null) rightPanel.SelfModulate = Colors.White;
			if (topBar != null) topBar.SelfModulate = Colors.White;
			if (minimap != null) minimap.SelfModulate = Colors.White;
		}

		UpdateFPSVisibility();

		if (leftPanel != null && !prefs.HideChromeBorderOverlay)
		{
			leftPanel.Modulate = new Color(1, 1, 1, prefs.PanelOpacity);
		}
		if (rightPanel != null && !prefs.HideChromeBorderOverlay)
		{
			rightPanel.Modulate = new Color(1, 1, 1, prefs.PanelOpacity);
		}

		if (!prefs.HideHudDuringToolUsage && _is3DInteractionActive)
		{
			Set3DInteractionActive(false);
		}
	}



	public static string SanitizeMapName(string? candidateName)
	{
		if (string.IsNullOrWhiteSpace(candidateName))
		{
			return "Untitled Map";
		}

		string sanitized = candidateName.Replace(MapWorkspaceService.DefaultWorkspaceFolder, string.Empty, StringComparison.OrdinalIgnoreCase).Trim();
		return string.IsNullOrEmpty(sanitized) ? "Untitled Map" : sanitized;
	}

	private static bool TrySanitizeCandidate(string? candidateName, out string sanitizedMapName)
	{
		sanitizedMapName = string.Empty;
		if (string.IsNullOrWhiteSpace(candidateName))
		{
			return false;
		}

		string cleaned = candidateName.Replace(MapWorkspaceService.DefaultWorkspaceFolder, string.Empty, StringComparison.OrdinalIgnoreCase).Trim();
		if (string.IsNullOrEmpty(cleaned))
		{
			return false;
		}

		sanitizedMapName = cleaned;
		return true;
	}

	private string? _cachedMapName;
	private string? _cachedMapVersion;
	private long _lastMapNameCacheTicks;

	public void InvalidateMetadataCache()
	{
		_cachedMapName = null;
		_cachedMapVersion = null;
		_lastMapNameCacheTicks = 0;
	}

	public string GetMapNameFromMetadata(bool forceReload = false)
	{
		long now = System.Environment.TickCount64;
		if (!forceReload && !string.IsNullOrEmpty(_cachedMapName) && (now - _lastMapNameCacheTicks < 2000))
		{
			return _cachedMapName;
		}

		try
		{
			string workspacePath = !string.IsNullOrEmpty(_tempWorkspacePath) ? _tempWorkspacePath : MapWorkspaceService.GetActiveWorkspacePath();
			string manifestPath = System.IO.Path.Combine(workspacePath, "manifest.json");
			if (System.IO.File.Exists(manifestPath))
			{
				string json = System.IO.File.ReadAllText(manifestPath);
				var root = System.Text.Json.Nodes.JsonNode.Parse(json) as System.Text.Json.Nodes.JsonObject;
				if (root != null)
				{
					if (root.TryGetPropertyValue("MapName", out var n) && TrySanitizeCandidate(n?.ToString(), out var manifestMapName))
					{
						_cachedMapName = manifestMapName;
						_lastMapNameCacheTicks = now;
						return manifestMapName;
					}
				}
			}

			string metaJsonPath = System.IO.Path.Combine(workspacePath, "metadata.json");
			if (System.IO.File.Exists(metaJsonPath))
			{
				try
				{
					var metaObj = System.Text.Json.Nodes.JsonNode.Parse(System.IO.File.ReadAllText(metaJsonPath)) as System.Text.Json.Nodes.JsonObject;
					if (metaObj != null)
					{
						if (metaObj.TryGetPropertyValue("MapProperties", out var mpNode) && mpNode is System.Text.Json.Nodes.JsonObject mpObj &&
							mpObj.TryGetPropertyValue("MapName", out var mpName) && TrySanitizeCandidate(mpName?.ToString(), out var parsedMpName))
						{
							_cachedMapName = parsedMpName;
							_lastMapNameCacheTicks = now;
							return parsedMpName;
						}
						if (metaObj.TryGetPropertyValue("MapName", out var rootNameNode) && TrySanitizeCandidate(rootNameNode?.ToString(), out var parsedRootName))
						{
							_cachedMapName = parsedRootName;
							_lastMapNameCacheTicks = now;
							return parsedRootName;
						}
					}
				}
				catch { }
			}

			if (MetadataService.Instance.TryLoadMetadata(workspacePath, out var metadata))
			{
				if (TrySanitizeCandidate(metadata.MapProperties?.MapName, out var name1))
				{
					_cachedMapName = name1;
					_lastMapNameCacheTicks = now;
					return name1;
				}
			}

			if (!string.IsNullOrEmpty(GameHost.Instance?.ActiveMapName))
			{
				string candidate = System.IO.Path.GetFileNameWithoutExtension(GameHost.Instance.ActiveMapName);
				if (TrySanitizeCandidate(candidate, out var activeMapName))
				{
					_cachedMapName = activeMapName;
					_lastMapNameCacheTicks = now;
					return activeMapName;
				}
			}

			if (!string.IsNullOrEmpty(workspacePath))
			{
				string candidate = System.IO.Path.GetFileName(workspacePath);
				if (TrySanitizeCandidate(candidate, out var workspaceName))
				{
					_cachedMapName = workspaceName;
					_lastMapNameCacheTicks = now;
					return workspaceName;
				}
			}

			_cachedMapName = "Untitled Map";
			_lastMapNameCacheTicks = now;
			return "Untitled Map";
		}
		catch
		{
			_cachedMapName = "Untitled Map";
			_lastMapNameCacheTicks = now;
			return "Untitled Map";
		}
	}

	public string GetMapVersionFromMetadata(bool forceReload = false)
	{
		long now = System.Environment.TickCount64;
		if (!forceReload && !string.IsNullOrEmpty(_cachedMapVersion) && (now - _lastMapNameCacheTicks < 2000))
		{
			return _cachedMapVersion;
		}

		try
		{
			string workspacePath = !string.IsNullOrEmpty(_tempWorkspacePath) ? _tempWorkspacePath : MapWorkspaceService.GetActiveWorkspacePath();
			string manifestPath = System.IO.Path.Combine(workspacePath, "manifest.json");
			if (System.IO.File.Exists(manifestPath))
			{
				string json = System.IO.File.ReadAllText(manifestPath);
				var root = System.Text.Json.Nodes.JsonNode.Parse(json) as System.Text.Json.Nodes.JsonObject;
				if (root != null && root.TryGetPropertyValue("Version", out var verNode) && verNode != null)
				{
					string v = verNode.ToString().Trim();
					if (!string.IsNullOrEmpty(v))
					{
						_cachedMapVersion = v;
						return v;
					}
				}
			}

			if (MetadataService.Instance.TryLoadMetadata(workspacePath, out var metadata))
			{
				string? ver = metadata.MapProperties?.Version;
				if (!string.IsNullOrWhiteSpace(ver))
				{
					_cachedMapVersion = ver.Trim();
					return _cachedMapVersion;
				}
			}

			string metaJsonPath = System.IO.Path.Combine(workspacePath, "metadata.json");
			if (System.IO.File.Exists(metaJsonPath))
			{
				var doc = JsonNode.Parse(System.IO.File.ReadAllText(metaJsonPath));
				if (doc != null)
				{
					if (doc["MapProperties"] is JsonObject props)
					{
						string? v = props["MapVersion"]?.ToString() ?? props["Version"]?.ToString();
						if (!string.IsNullOrWhiteSpace(v))
						{
							_cachedMapVersion = v.Trim();
							return _cachedMapVersion;
						}
					}
					else
					{
						string? v = doc["Version"]?.ToString() ?? doc["MapVersion"]?.ToString();
						if (!string.IsNullOrWhiteSpace(v))
						{
							_cachedMapVersion = v.Trim();
							return _cachedMapVersion;
						}
					}
				}
			}

			_cachedMapVersion = "1.0.0";
			return "1.0.0";
		}
		catch
		{
			_cachedMapVersion = "1.0.0";
			return "1.0.0";
		}
	}

	public string GetManifestBlake3()
	{
		try
		{
			string workspacePath = !string.IsNullOrEmpty(_tempWorkspacePath) ? _tempWorkspacePath : MapWorkspaceService.GetActiveWorkspacePath();
			string manifestJsonPath = System.IO.Path.Combine(workspacePath, "manifest.json");
			if (System.IO.File.Exists(manifestJsonPath))
			{
				string manifestBlake3 = RealmMetadataHelper.ComputeBlake3(manifestJsonPath);
				if (!string.IsNullOrEmpty(manifestBlake3))
				{
					return manifestBlake3;
				}
			}

			if (System.IO.Directory.Exists(workspacePath))
			{
				string mapTitle = GetMapNameFromMetadata();
				string mapVersion = GetMapVersionFromMetadata();
				string author = LobbyManager.Instance?.AuthenticatedUsername ?? "MapAuthor";
				var manifest = MapManifest.CreateFromDirectory(workspacePath, mapTitle, author, mapVersion);
				return manifest.ComputeManifestBlake3();
			}
		}
		catch { }

		return string.Empty;
	}

	public void UpdateMapNameHeader()
	{
		if (_lblMapNameHeader == null) return;
		string mapName = GetMapNameFromMetadata();
		string displayMapName = mapName == "Untitled Map" ? TranslationServer.Translate("Untitled Map") : mapName;
		bool hasUnsaved = GameHost.Instance?.EditorHasUnsavedChanges ?? false;

		string displayText = hasUnsaved ? $"🗺️ {displayMapName} *" : $"🗺️ {displayMapName}";
		string tooltipText = hasUnsaved
			? $"{displayMapName} * ({TranslationServer.Translate("Unsaved changes — Press Ctrl+S to save")})"
			: $"{displayMapName} ({TranslationServer.Translate("All changes saved")})";

		if (_lblMapNameHeader.Text != displayText)
		{
			_lblMapNameHeader.Text = displayText;
		}

		if (_lblMapNameHeader.TooltipText != tooltipText)
		{
			_lblMapNameHeader.TooltipText = tooltipText;
		}
	}

	private void RefreshSkyboxList()
	{
		_mapSettingsDialog?.RefreshSkyboxList();
	}

	private void UpdateMetadataJsonAsset(string category, string fileName, string blake3Hash, string subCategory = null, int columns = 0, int rows = 0, int targetSlot = -1)
	{
		try
		{
			string wsPath = string.IsNullOrEmpty(_tempWorkspacePath) 
				? ProjectSettings.GlobalizePath(TempWorkspaceGodotPath) 
				: _tempWorkspacePath;
			string metadataPath = System.IO.Path.Combine(wsPath, "metadata.json");
			JsonObject root = new JsonObject();
			if (System.IO.File.Exists(metadataPath))
			{
				string text = System.IO.File.ReadAllText(metadataPath);
				if (!string.IsNullOrWhiteSpace(text))
				{
					root = System.Text.Json.Nodes.JsonNode.Parse(text) as JsonObject ?? new JsonObject();
				}
			}

			if (category == "glb" && GameHost.Instance != null)
			{
				string normKey = GameHost.Instance.NormalizeModelAssetKey(fileName);
				if (!GameHost.Instance.ModelObstacleRadii.ContainsKey(normKey))
				{
					string modelPath = System.IO.Path.Combine(wsPath, "Assets", "models", subCategory ?? "", fileName);
					Node3D modelNode = Realm.Godot.Utils.ModelCache.GetModel(modelPath) as Node3D;
					if (modelNode != null)
					{
						float radius = GameHost.Instance.MeasureModelRadius(modelNode);
						modelNode.Free();
						if (radius > 0f)
						{
							float rounded = (float)Math.Round(radius, 2);
							GameHost.Instance.ModelObstacleRadii[normKey] = rounded;
							if (!root.ContainsKey("Models") || root["Models"] is not JsonObject) root["Models"] = new JsonObject();
							var modelsObj = (JsonObject)root["Models"]!;
							if (!modelsObj.ContainsKey(normKey) || modelsObj[normKey] is not JsonObject) modelsObj[normKey] = new JsonObject();
							((JsonObject)modelsObj[normKey]!)["ObstacleRadii"] = rounded;
						}
					}
				}
			}

			JsonObject assetsObj = Realm.Godot.Utils.MapAssetHelper.LoadUnionedAssets(wsPath) ?? new JsonObject();

			if (!string.IsNullOrEmpty(subCategory))
			{
				if (!assetsObj.ContainsKey(category)) assetsObj[category] = new JsonObject();
				JsonObject catObj = assetsObj[category] as JsonObject ?? new JsonObject();
				if (!catObj.ContainsKey(subCategory)) catObj[subCategory] = new JsonObject();
				JsonObject subObj = catObj[subCategory] as JsonObject ?? new JsonObject();
				if (category == "glb")
				{
					float defaultScale = subCategory.ToLowerInvariant() switch
					{
						"resources" => 2.75f,
						"buildings" => 1.5f,
						"props" => 1.25f,
						"units" => 1.0f,
						_ => 1.0f
					};

					string modelFullPath = System.IO.Path.Combine(wsPath, "Assets", "models", subCategory, fileName);
					if (!System.IO.File.Exists(modelFullPath))
					{
						modelFullPath = System.IO.Path.Combine(wsPath, "Assets", "glb", subCategory, fileName);
					}

					var (minY, autoYOffset) = Realm.Godot.Utils.ModelCache.CalculateModelBounds(modelFullPath, defaultScale);
					string subLower = subCategory.ToLowerInvariant();
					bool isPropOrRes = subLower == "props" || subLower == "resources" || subLower == "attachments" || subLower == "weapons" || subLower == "items" || subLower == "projectiles";

					var glbMetaObj = new JsonObject
					{
						["hash"] = blake3Hash,
						["min_y"] = minY,
						["scale"] = defaultScale,
						["y_offset"] = autoYOffset,
						["default_asset_type"] = subCategory.ToLowerInvariant(),
						["despill_player_color"] = false,
						["normalize_luminance"] = true,
						["ignore_player_color"] = isPropOrRes
					};
					subObj[fileName] = glbMetaObj;
					catObj[subCategory] = subObj;
					assetsObj[category] = catObj;

					if (!root.ContainsKey("Models") || root["Models"] is not JsonObject) root["Models"] = new JsonObject();
					var modelsMap = (JsonObject)root["Models"]!;
					if (!modelsMap.ContainsKey(fileName) || modelsMap[fileName] is not JsonObject) modelsMap[fileName] = new JsonObject();
					var modelEntry = (JsonObject)modelsMap[fileName]!;
					modelEntry["Offsets"] = autoYOffset;
					modelEntry["Scales"] = defaultScale;

					GameHost.Instance?.SetModelYOffset(fileName, autoYOffset);
					GameHost.Instance?.SetModelScale(fileName, defaultScale);

					string unitId = System.IO.Path.GetFileNameWithoutExtension(fileName);
					string targetArrayKey = subCategory.ToLowerInvariant() switch
					{
						"units" => "CustomUnits",
						"buildings" => "CustomBuildings",
						"resources" => "CustomResources",
						"props" => "CustomProps",
						_ => "CustomUnits"
					};

					if (!root.ContainsKey(targetArrayKey) || root[targetArrayKey] is not JsonArray)
					{
						root[targetArrayKey] = new JsonArray();
					}
					JsonArray targetArr = (JsonArray)root[targetArrayKey];
					bool exists = false;
					foreach (var item in targetArr)
					{
						if (item is JsonObject uObj && (uObj["UnitId"]?.ToString() == unitId || uObj["ModelPath"]?.ToString() == fileName))
						{
							exists = true;
							if (autoYOffset != 0f) uObj["YOffset"] = autoYOffset;
							break;
						}
					}

					if (!exists)
					{
						int defaultPathing = subCategory.ToLowerInvariant() switch
						{
							"units" => (int)(Realm.Ecs.Components.Terrain.TerrainPathingFlags.Ground | Realm.Ecs.Components.Terrain.TerrainPathingFlags.ShallowWater),
							"buildings" => (int)Realm.Ecs.Components.Terrain.TerrainPathingFlags.Buildable,
							"resources" => 0xFF,
							"props" => 0xFF,
							_ => (int)Realm.Ecs.Components.Terrain.TerrainPathingFlags.Ground
						};

						var newUnitObj = new JsonObject
						{
							["UnitId"] = unitId,
							["Name"] = unitId,
							["Description"] = "",
							["Scale"] = defaultScale,
							["YOffset"] = autoYOffset,
							["PathingType"] = defaultPathing,
							["ModelPath"] = fileName,
							["DespillPlayerColor"] = false,
							["NormalizeLuminance"] = true,
							["IgnorePlayerColor"] = isPropOrRes
						};
						targetArr.Add(newUnitObj);
					}
				}
				else
				{
					subObj[fileName] = blake3Hash;
					catObj[subCategory] = subObj;
					assetsObj[category] = catObj;
				}
			}
			else
			{
				if (!assetsObj.ContainsKey(category)) assetsObj[category] = new JsonObject();
				JsonObject catObj = assetsObj[category] as JsonObject ?? new JsonObject();
				if (category == "textures")
				{
					var knownRibbons = Realm.Godot.Utils.TextureSwatchSlots.BuildKnownRibbonsCache(assetsObj, wsPath);
					if (!Realm.Godot.Utils.TextureSwatchSlots.ValidateCategory(fileName, knownRibbons: knownRibbons))
					{
						GD.PrintErr($"[MapEditorHUD] Asset '{fileName}' is not a valid terrain texture.");
						return;
					}

					if (catObj.Count == 0 && root.ContainsKey("textures") && root["textures"] is JsonObject rootTexExisting)
					{
						foreach (var kvp in rootTexExisting)
						{
							if (Realm.Godot.Utils.TextureSwatchSlots.ValidateCategory(kvp.Key, kvp.Value, knownRibbons))
							{
								catObj[kvp.Key] = kvp.Value?.DeepClone();
							}
						}
					}

					var occupiedSlots = new bool[Realm.Godot.Utils.TextureSwatchSlots.MaxSlots];
					int existingItemIndex = -1;

					foreach (var kvp in catObj)
					{
						if (!Realm.Godot.Utils.TextureSwatchSlots.ValidateCategory(kvp.Key, kvp.Value, knownRibbons))
						{
							continue;
						}

						int sIdx = -1;
						if (kvp.Value is JsonObject sObj)
						{
							if (sObj.TryGetPropertyValue("swatchIndex", out var idxNode) && idxNode != null && int.TryParse(idxNode.ToString(), out int parsed))
							{
								sIdx = parsed;
							}
						}

						if (kvp.Key.Equals(fileName, StringComparison.OrdinalIgnoreCase))
						{
							existingItemIndex = sIdx;
						}
						else if (sIdx >= 0 && sIdx < Realm.Godot.Utils.TextureSwatchSlots.MaxSlots)
						{
							occupiedSlots[sIdx] = true;
						}
					}

					int swatchIdx = -1;
					if (targetSlot >= 0 && targetSlot < Realm.Godot.Utils.TextureSwatchSlots.MaxSlots)
					{
						swatchIdx = targetSlot;
					}
					else if (existingItemIndex >= 0 && existingItemIndex < Realm.Godot.Utils.TextureSwatchSlots.MaxSlots)
					{
						swatchIdx = existingItemIndex;
					}
					else
					{
						swatchIdx = Realm.Godot.Utils.TextureSwatchSlots.FirstFreeSlot(occupiedSlots);
					}

					if (swatchIdx < 0 && GameHost.Instance != null && GameHost.Instance.EditorPaintTextureIndex >= 0 && GameHost.Instance.EditorPaintTextureIndex < Realm.Godot.Utils.TextureSwatchSlots.MaxSlots)
					{
						swatchIdx = GameHost.Instance.EditorPaintTextureIndex;
					}

					if (swatchIdx < 0)
					{
						GD.PrintErr($"[MapEditorHUD] All 32 texture slots are occupied. Cannot assign slot to '{fileName}'.");
						return;
					}

					string prevOccupantKey = null;
					foreach (var kvp in catObj)
					{
						if (kvp.Key.Equals(fileName, StringComparison.OrdinalIgnoreCase)) continue;
						if (kvp.Value is JsonObject sObj)
						{
							int s = -1;
							if (sObj.TryGetPropertyValue("swatchIndex", out var n1) && n1 != null && int.TryParse(n1.ToString(), out int p1)) s = p1;
							else if (sObj.TryGetPropertyValue("swatch_index", out var n2) && n2 != null && int.TryParse(n2.ToString(), out int p2)) s = p2;
							else if (sObj.TryGetPropertyValue("SwatchIndex", out var n3) && n3 != null && int.TryParse(n3.ToString(), out int p3)) s = p3;
							if (s == swatchIdx)
							{
								prevOccupantKey = kvp.Key;
								break;
							}
						}
					}

					if (!string.IsNullOrEmpty(prevOccupantKey))
					{
						catObj.Remove(prevOccupantKey);
						if (root.ContainsKey("textures") && root["textures"] is JsonObject rootTex)
						{
							rootTex.Remove(prevOccupantKey);
						}
					}

					JsonObject texEntry;
					if (catObj.ContainsKey(fileName) && catObj[fileName] is JsonObject existingEntry)
					{
						texEntry = existingEntry;
						texEntry["hash"] = blake3Hash;
						texEntry["swatchIndex"] = swatchIdx;
					}
					else
					{
						texEntry = new JsonObject
						{
							["hash"] = blake3Hash,
							["swatchIndex"] = swatchIdx
						};
					}

					if (!texEntry.ContainsKey("Scale_Factor"))
					{
						string texPath = System.IO.Path.Combine(wsPath, "Assets", "textures", fileName);
						float scaleFactor = Realm.Shared.Textures.TextureConverter.CalculateLuminanceScaleFactor(texPath);
						texEntry["Scale_Factor"] = scaleFactor;
					}

					catObj[fileName] = texEntry;
				}
				else if (columns > 0 && rows > 0)
				{
					var metaObj = new JsonObject
					{
						["hash"] = blake3Hash,
						["columns"] = columns,
						["rows"] = rows
					};
					catObj[fileName] = metaObj;
				}
				else
				{
					catObj[fileName] = blake3Hash;
				}
				assetsObj[category] = catObj;
			}

			Realm.Godot.Utils.MapAssetHelper.SaveAssetsToManifest(wsPath, assetsObj, removeFromMetadata: true);
			root.Remove("Assets");
			SaveLoadService.CleanMetadataJsonSchema(root);
			MapJsonFormatter.SaveFormattedJson(metadataPath, root);
		}
		catch (Exception ex)
		{
			GD.PrintErr($"Failed to update metadata.json asset: {ex.Message}");
		}
	}

	public void ImportTextureAssetFromExtension(string sourceFilePath, int slotIndex)
	{
		try
		{
			string cleanBaseName = System.IO.Path.GetFileNameWithoutExtension(sourceFilePath).ToLowerInvariant().Replace(" ", "_") + ".rtex";
			string wsPath = string.IsNullOrEmpty(_tempWorkspacePath) 
				? ProjectSettings.GlobalizePath(TempWorkspaceGodotPath) 
				: _tempWorkspacePath;
			string texDir = System.IO.Path.Combine(wsPath, "Assets", "textures");
			System.IO.Directory.CreateDirectory(texDir);
			string outputRtex = System.IO.Path.Combine(texDir, cleanBaseName);

			string ext = System.IO.Path.GetExtension(sourceFilePath);
			if (ext.Equals(".rtex", StringComparison.OrdinalIgnoreCase))
			{
				System.IO.File.Copy(sourceFilePath, outputRtex, true);
			}
			else if (GameHost.Instance != null && GameHost.Instance.GroundTerrain != null)
			{
				GameHost.Instance.GroundTerrain.ProcessAndSaveRawTexture(sourceFilePath, outputRtex);
			}

			if (System.IO.File.Exists(outputRtex))
			{
				byte[] rtexBytes = System.IO.File.ReadAllBytes(outputRtex);
				string blake3 = RealmMetadataHelper.ComputeBlake3(rtexBytes, ".rtex");
				UpdateMetadataJsonAsset("textures", cleanBaseName, blake3, targetSlot: slotIndex);
				ReadMetadataAndRefreshTextures();
				ShowFeedback($"Successfully processed & imported RTEX texture for {cleanBaseName}!");
			}
			else
			{
				ShowFeedback($"Failed to generate RTEX texture at {outputRtex}");
			}
		}
		catch (Exception ex)
		{
			GD.PrintErr($"ImportTextureAssetFromExtension error: {ex.Message}");
			ShowFeedback($"Failed to import texture: {ex.Message}");
		}
	}

	public void ConvertRawTextureDirect(string rawPngPath, string outputRtexPath, string swatchName)
	{
		try
		{
			if (GameHost.Instance != null && GameHost.Instance.GroundTerrain != null)
			{
				GameHost.Instance.GroundTerrain.ProcessAndSaveRawTexture(rawPngPath, outputRtexPath);
			}

			if (System.IO.File.Exists(outputRtexPath))
			{
				ReadMetadataAndRefreshTextures();
				ShowFeedback($"Successfully processed & imported RTEX texture for {swatchName}!");
			}
			else
			{
				ShowFeedback($"Failed to generate RTEX texture at {outputRtexPath}");
			}
		}
		catch (Exception ex)
		{
			GD.PrintErr($"ConvertRawTextureDirect error: {ex.Message}");
			ShowFeedback($"Failed to import texture: {ex.Message}");
		}
	}

	public Realm.Godot.Services.ModelOptimization.ModelOptimizerService.OptimizationResult OptimizeAndImportGlbDirect(
		byte[] glbBytes,
		int maxTextureResolution = 1024,
		float creaseAngleDegrees = GlbMeshSmoother.DefaultCreaseAngleDegrees,
		float allowedPixelError = 1.5f,
		bool forceReDecimate = false)
	{
		var optimizer = ServiceLocator.TryGet<Realm.Godot.Services.ModelOptimization.ModelOptimizerService>()
			?? new Realm.Godot.Services.ModelOptimization.ModelOptimizerService(ServiceLocator.TryGet<Realm.Ecs.Services.WorldAccessor>());

		var options = new Realm.Godot.Services.ModelOptimization.ModelOptimizerService.OptimizationOptions
		{
			MaxTextureResolution = maxTextureResolution,
			CreaseAngleDegrees = creaseAngleDegrees,
			AllowedPixelError = allowedPixelError,
			ForceReDecimate = forceReDecimate
		};

		return optimizer.OptimizeGlb(glbBytes, options);
	}

	public void ReadMetadataAndRefreshTextures()
	{
		try
		{
			string wsPath = string.IsNullOrEmpty(_tempWorkspacePath) 
				? ProjectSettings.GlobalizePath(TempWorkspaceGodotPath) 
				: _tempWorkspacePath;
			string metadataPath = System.IO.Path.Combine(wsPath, "metadata.json");

			string texDir = System.IO.Path.Combine(wsPath, "Assets", "textures");
			System.IO.Directory.CreateDirectory(texDir);

			if (System.IO.File.Exists(metadataPath))
			{
				MapWorkspaceService.NormalizeMetadataTextureEntries(wsPath);
			}
			InvalidateMetadataCache();

			_swatchTextureCache.Clear();
			if (GameHost.Instance != null && GameHost.Instance.GroundTerrain != null)
			{
				GameHost.Instance.GroundTerrain.ReloadTerrainTextures(true);
			}
			SetupTextureSwatches(false);
			RefreshSkyboxList();
			if (System.IO.File.Exists(metadataPath))
			{
				GameHost.Instance?.LoadModelYOffsetsFromMetadataJson(wsPath);
				GameHost.Instance?.LoadUnitMetadata(wsPath);
			}
			_entityPaletteController?.SelectCategory(_entityPaletteController.CurrentCategory, triggerAddObject: false);
		}
		catch (Exception ex)
		{
			GD.PrintErr($"ReadMetadataAndRefreshTextures error: {ex.Message}");
		}
	}

	public void ImportMixamoOrAnimationDialog()
	{
		OpenAssetBrowser("Select Animation (.ranim)", new[] { ".ranim" }, path =>
		{
			ImportAnimationAssetFromExtension(path);
		});
	}

	public void ImportAnimationAssetFromExtension(string sourceFilePath)
	{
		try
		{
			string wsPath = string.IsNullOrEmpty(_tempWorkspacePath) 
				? ProjectSettings.GlobalizePath(TempWorkspaceGodotPath) 
				: _tempWorkspacePath;
			string ext = System.IO.Path.GetExtension(sourceFilePath).ToLowerInvariant();
			string animsDir = System.IO.Path.Combine(wsPath, "Assets", "animations");
			System.IO.Directory.CreateDirectory(animsDir);

			if (ext == ".glb" || ext == ".gltf" || ext == ".fbx")
			{
				string originalFileName = System.IO.Path.GetFileNameWithoutExtension(sourceFilePath);
				var extracted = Realm.Godot.Animation.MixamoAnimationImporter.ExtractAnimationsFromFile(sourceFilePath, originalFileName);
				if (extracted.Count == 0)
				{
					ShowFeedback(TranslationServer.Translate("No animations found in file."));
					return;
				}

				int importedCount = 0;
				int skippedCount = 0;
				foreach (var (animName, animData) in extracted)
				{
					var (savedFileName, blake3, alreadyExisted) = Realm.Godot.Animation.MixamoAnimationImporter.SaveAnimationWithDeduplication(animsDir, animName, animData);
					UpdateMetadataJsonAsset("animations", savedFileName, blake3);
					if (alreadyExisted) skippedCount++;
					else importedCount++;
				}

				PopulateAnimationPreviewDropdown();
				if (importedCount > 0)
				{
					ShowFeedback(string.Format(TranslationServer.Translate("Successfully imported {0} animation(s) (.ranim)!"), importedCount));
				}
				else
				{
					ShowFeedback(TranslationServer.Translate("Animation already imported (identical BLAKE3 hash)."));
				}
			}
			else
			{
				string fileName = System.IO.Path.GetFileName(sourceFilePath);
				byte[] sourceBytes = System.IO.File.ReadAllBytes(sourceFilePath);
				string newHash = RealmMetadataHelper.ComputeBlake3(sourceBytes, ".ranim");

				string baseName = System.IO.Path.GetFileNameWithoutExtension(fileName).ToLowerInvariant();
				string finalFileName = $"{baseName}.ranim";
				string targetPath = System.IO.Path.Combine(animsDir, finalFileName);

				if (System.IO.File.Exists(targetPath))
				{
					string existingHash = RealmMetadataHelper.ComputeBlake3(System.IO.File.ReadAllBytes(targetPath), ".ranim");
					if (existingHash.Equals(newHash, StringComparison.OrdinalIgnoreCase))
					{
						UpdateMetadataJsonAsset("animations", finalFileName, newHash);
						PopulateAnimationPreviewDropdown();
						ShowFeedback(TranslationServer.Translate("Animation already imported (identical BLAKE3 hash)."));
						return;
					}

					for (int i = 1; i <= 9999; i++)
					{
						string varName = $"{baseName}_{i}.ranim";
						string varPath = System.IO.Path.Combine(animsDir, varName);
						if (!System.IO.File.Exists(varPath))
						{
							finalFileName = varName;
							targetPath = varPath;
							System.IO.File.WriteAllBytes(targetPath, sourceBytes);
							break;
						}
						else
						{
							string varHash = RealmMetadataHelper.ComputeBlake3(System.IO.File.ReadAllBytes(varPath), ".ranim");
							if (varHash.Equals(newHash, StringComparison.OrdinalIgnoreCase))
							{
								finalFileName = varName;
								break;
							}
						}
					}
				}
				else
				{
					System.IO.File.WriteAllBytes(targetPath, sourceBytes);
				}

				UpdateMetadataJsonAsset("animations", finalFileName, newHash);
				PopulateAnimationPreviewDropdown();
				ShowFeedback(string.Format(TranslationServer.Translate("Imported animation {0}"), finalFileName));
			}
		}
		catch (Exception ex)
		{
			GD.PrintErr($"ImportAnimationAssetFromExtension error: {ex.Message}");
			ShowFeedback(string.Format(TranslationServer.Translate("Failed to import animation: {0}"), ex.Message));
		}
	}

	public void ImportGlbAssetFromExtension(string sourceFilePath, string category)
	{
		try
		{
			string wsPath = string.IsNullOrEmpty(_tempWorkspacePath) 
				? ProjectSettings.GlobalizePath(TempWorkspaceGodotPath) 
				: _tempWorkspacePath;
			string fileName = System.IO.Path.GetFileName(sourceFilePath);
			string subCat = category.ToLowerInvariant();

			var importResult = Realm.Godot.Animation.MixamoAnimationImporter.ImportMixamoGlb(sourceFilePath, wsPath, subCat);
			if (!importResult.Success)
			{
				ShowFeedback(string.Format(TranslationServer.Translate("Failed to import GLB asset: {0}"), importResult.ErrorMessage));
				return;
			}

			byte[] glbBytes = System.IO.File.ReadAllBytes(importResult.StrippedGlbPath);
			string glbBlake3 = RealmMetadataHelper.ComputeBlake3(glbBytes, ".glb");
			UpdateMetadataJsonAsset("glb", fileName, glbBlake3, subCategory: subCat);

			foreach (var animFile in importResult.ExtractedAnimationFiles)
			{
				string animPath = System.IO.Path.Combine(wsPath, "Assets", "animations", animFile);
				if (System.IO.File.Exists(animPath))
				{
					byte[] animBytes = System.IO.File.ReadAllBytes(animPath);
					string animBlake3 = RealmMetadataHelper.ComputeBlake3(animBytes, ".ranim");
					UpdateMetadataJsonAsset("animations", animFile, animBlake3);
				}
			}

			GameHost.Instance?.LoadUnitMetadata(wsPath);
			_entityPaletteController?.SelectCategory(_entityPaletteController.CurrentCategory, triggerAddObject: false);
			PopulateAnimationPreviewDropdown();
			if (importResult.ExtractedAnimationFiles.Count > 0)
			{
				ShowFeedback(string.Format(TranslationServer.Translate("Imported GLB {0} and extracted {1} .ranim animation(s)"), fileName, importResult.ExtractedAnimationFiles.Count));
			}
			else
			{
				ShowFeedback(string.Format(TranslationServer.Translate("Imported GLB asset {0} ({1})"), fileName, category));
			}
		}
		catch (Exception ex)
		{
			GD.PrintErr($"ImportGlbAssetFromExtension error: {ex.Message}");
			ShowFeedback(string.Format(TranslationServer.Translate("Failed to import GLB asset: {0}"), ex.Message));
		}
	}

	public void PopulateAnimationPreviewDropdown()
	{
	}

	public void ImportDecalAssetFromExtension(string sourceFilePath)
	{
		try
		{
			string wsPath = string.IsNullOrEmpty(_tempWorkspacePath) 
				? ProjectSettings.GlobalizePath(TempWorkspaceGodotPath) 
				: _tempWorkspacePath;
			string baseName = System.IO.Path.GetFileNameWithoutExtension(sourceFilePath);
			string fileName = baseName + ".png";
			string decalsDir = System.IO.Path.Combine(wsPath, "Assets", "decals");
			System.IO.Directory.CreateDirectory(decalsDir);
			string targetPath = System.IO.Path.Combine(decalsDir, fileName);

			var img = Image.LoadFromFile(sourceFilePath);
			if (img != null)
			{
				img.SavePng(targetPath);
			}
			else
			{
				System.IO.File.Copy(sourceFilePath, targetPath, true);
			}

			byte[] bytes = System.IO.File.ReadAllBytes(targetPath);
			string blake3 = RealmMetadataHelper.ComputeBlake3(bytes, ".png");

			UpdateMetadataJsonAsset("decals", fileName, blake3);
			ShowFeedback($"Imported decal {fileName}");
		}
		catch (Exception ex)
		{
			GD.PrintErr($"ImportDecalAssetFromExtension error: {ex.Message}");
			ShowFeedback($"Failed to import decal: {ex.Message}");
		}
	}

	public void ImportSkyboxAssetFromExtension(string sourceFilePath)
	{
		try
		{
			string wsPath = string.IsNullOrEmpty(_tempWorkspacePath) 
				? ProjectSettings.GlobalizePath(TempWorkspaceGodotPath) 
				: _tempWorkspacePath;
			string fileName = System.IO.Path.GetFileName(sourceFilePath);
			string skyboxesDir = System.IO.Path.Combine(wsPath, "Assets", "skyboxes");
			System.IO.Directory.CreateDirectory(skyboxesDir);
			string targetPath = System.IO.Path.Combine(skyboxesDir, fileName);

			System.IO.File.Copy(sourceFilePath, targetPath, true);

			byte[] bytes = System.IO.File.ReadAllBytes(targetPath);
			string blake3 = RealmMetadataHelper.ComputeBlake3(bytes, fileName);

			UpdateMetadataJsonAsset("skyboxes", fileName, blake3);
			RefreshSkyboxList();
			ShowFeedback($"Imported skybox {fileName}");
		}
		catch (Exception ex)
		{
			GD.PrintErr($"ImportSkyboxAssetFromExtension error: {ex.Message}");
			ShowFeedback($"Failed to import skybox: {ex.Message}");
		}
	}

	public void ImportSpritesheetAssetFromExtension(string sourceFilePath, int columns = 4, int rows = 4)
	{
		try
		{
			string wsPath = string.IsNullOrEmpty(_tempWorkspacePath) 
				? ProjectSettings.GlobalizePath(TempWorkspaceGodotPath) 
				: _tempWorkspacePath;
			string baseName = System.IO.Path.GetFileNameWithoutExtension(sourceFilePath);
			string fileName = baseName + ".png";
			string vfxDir = System.IO.Path.Combine(wsPath, "Assets", "vfx");
			System.IO.Directory.CreateDirectory(vfxDir);
			string targetPath = System.IO.Path.Combine(vfxDir, fileName);

			var img = Image.LoadFromFile(sourceFilePath);
			if (img != null)
			{
				img.SavePng(targetPath);
			}
			else
			{
				System.IO.File.Copy(sourceFilePath, targetPath, true);
			}

			byte[] bytes = System.IO.File.ReadAllBytes(targetPath);
			string blake3 = RealmMetadataHelper.ComputeBlake3(bytes, ".png");

			UpdateMetadataJsonAsset("vfx_spritesheets", fileName, blake3, columns: columns, rows: rows);
			ShowFeedback($"Imported VFX spritesheet {fileName}");
		}
		catch (Exception ex)
		{
			GD.PrintErr($"ImportSpritesheetAssetFromExtension error: {ex.Message}");
			ShowFeedback($"Failed to import VFX spritesheet: {ex.Message}");
		}
	}

	public void ImportIconAssetFromExtension(string sourceFilePath)
	{
		try
		{
			string wsPath = string.IsNullOrEmpty(_tempWorkspacePath) 
				? ProjectSettings.GlobalizePath(TempWorkspaceGodotPath) 
				: _tempWorkspacePath;
			string baseName = System.IO.Path.GetFileNameWithoutExtension(sourceFilePath);
			string fileName = baseName + ".png";
			string iconsDir = System.IO.Path.Combine(wsPath, "Assets", "icons");
			System.IO.Directory.CreateDirectory(iconsDir);
			string targetPath = System.IO.Path.Combine(iconsDir, fileName);

			var img = Image.LoadFromFile(sourceFilePath);
			if (img != null)
			{
				img.SavePng(targetPath);
			}
			else
			{
				System.IO.File.Copy(sourceFilePath, targetPath, true);
			}

			byte[] bytes = System.IO.File.ReadAllBytes(targetPath);
			string blake3 = RealmMetadataHelper.ComputeBlake3(bytes, ".png");

			UpdateMetadataJsonAsset("icons", fileName, blake3);
			ShowFeedback($"Imported 2D Icon {fileName}");
		}
		catch (Exception ex)
		{
			GD.PrintErr($"ImportIconAssetFromExtension error: {ex.Message}");
			ShowFeedback($"Failed to import 2D Icon: {ex.Message}");
		}
	}

	public void ImportAudioAssetFromExtension(string sourceFilePath, string audioType)
	{
		try
		{
			string wsPath = string.IsNullOrEmpty(_tempWorkspacePath) 
				? ProjectSettings.GlobalizePath(TempWorkspaceGodotPath) 
				: _tempWorkspacePath;
			string baseName = System.IO.Path.GetFileNameWithoutExtension(sourceFilePath);
			string fileName = baseName + ".ogg";
			string targetPath = System.IO.Path.Combine(wsPath, fileName);

			if (sourceFilePath.EndsWith(".ogg", StringComparison.OrdinalIgnoreCase))
			{
				System.IO.File.Copy(sourceFilePath, targetPath, true);
			}
			else
			{
				Realm.Shared.Audio.AudioConverter.ConvertToOgg(sourceFilePath, targetPath);
				if (!System.IO.File.Exists(targetPath) || new System.IO.FileInfo(targetPath).Length == 0)
				{
					System.IO.File.Copy(sourceFilePath, targetPath, true);
				}
			}

			byte[] bytes = System.IO.File.ReadAllBytes(targetPath);
			string blake3 = RealmMetadataHelper.ComputeBlake3(bytes, ".ogg");

			UpdateMetadataJsonAsset(audioType.ToLowerInvariant() == "music" ? "music" : "sfx", fileName, blake3);
			ShowFeedback($"Imported audio {fileName} ({audioType})");
		}
		catch (Exception ex)
		{
			GD.PrintErr($"ImportAudioAssetFromExtension error: {ex.Message}");
			ShowFeedback($"Failed to import audio asset: {ex.Message}");
		}
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

	private void SetupLightingTuningUI()
	{
		var leftSlidePanel = GetNodeOrNull<Control>("LeftSlidePanel");
		if (leftSlidePanel != null)
		{
			leftSlidePanel.CustomMinimumSize = new Vector2(325, 0);
			leftSlidePanel.OffsetRight = 325.0f;
		}

		var leftVBox = GetNodeOrNull<VBoxContainer>("LeftSlidePanel/LeftScroll/LeftVBox");
		if (leftVBox == null) return;

		var lightingAccordion = new VBoxContainer();
		lightingAccordion.Name = "LightingTuningAccordion";

		_btnHeaderLightingTuning = new Button();
		_btnHeaderLightingTuning.Name = "BtnHeaderLightingTuning";
		lightingAccordion.AddChild(_btnHeaderLightingTuning);

		_contentLightingTuning = new VBoxContainer();
		_contentLightingTuning.Name = "ContentLightingTuning";
		lightingAccordion.AddChild(_contentLightingTuning);

		leftVBox.AddChild(lightingAccordion);

		StyleAccordionHeader(_btnHeaderLightingTuning);
		SetupAccordion(_btnHeaderLightingTuning, _contentLightingTuning, "💡 Lighting Tuning (Live Override)");

		CreateSectionHeader(_contentLightingTuning, "--- TERRAIN CLIFFS & SILHOUETTES ---");
		_sldCliffJitterStrength = CreateSliderRow(_contentLightingTuning, "Cliff Jitter Str", 0f, 2f, 0.02f, _tuneCliffJitterStrength, val => { _tuneCliffJitterStrength = val; ApplyLiveLightingTuning(); });
		_sldCliffJitterScale = CreateSliderRow(_contentLightingTuning, "Cliff Jitter Scl", 0.01f, 0.5f, 0.005f, _tuneCliffJitterScale, val => { _tuneCliffJitterScale = val; ApplyLiveLightingTuning(); }, "0.00#");
		_sldCliffRimNoiseStrength = CreateSliderRow(_contentLightingTuning, "Cliff Rim Str", 0f, 1f, 0.02f, _tuneCliffRimNoiseStrength, val => { _tuneCliffRimNoiseStrength = val; ApplyLiveLightingTuning(); });

		CreateSectionHeader(_contentLightingTuning, "--- TERRAIN TEXTURE BLENDING ---");
		_sldHeightBlendSoftness = CreateSliderRow(_contentLightingTuning, "Blend Softness", 0.001f, 0.20f, 0.002f, _tuneHeightBlendSoftness, val => { _tuneHeightBlendSoftness = val; ApplyLiveLightingTuning(); }, "0.00#");
		_sldBlendNoiseStrength = CreateSliderRow(_contentLightingTuning, "Blend Noise Str", 0f, 1f, 0.02f, _tuneBlendNoiseStrength, val => { _tuneBlendNoiseStrength = val; ApplyLiveLightingTuning(); });
		_sldBlendNoiseScale = CreateSliderRow(_contentLightingTuning, "Blend Noise Scl", 0.01f, 0.5f, 0.005f, _tuneBlendNoiseScale, val => { _tuneBlendNoiseScale = val; ApplyLiveLightingTuning(); }, "0.00#");

		lightingAccordion.Visible = false;
	}

	private void ApplyLiveLightingTuning()
	{
		if (EditableTerrain.Instance?.Material != null)
		{
			EditableTerrain.Instance.CliffJitterStrength = _tuneCliffJitterStrength;
			EditableTerrain.Instance.CliffJitterScale = _tuneCliffJitterScale;
			EditableTerrain.Instance.CliffRimNoiseStrength = _tuneCliffRimNoiseStrength;
			EditableTerrain.Instance.BlendSoftness = _tuneHeightBlendSoftness;
			EditableTerrain.Instance.BlendNoiseStrength = _tuneBlendNoiseStrength;
			EditableTerrain.Instance.BlendNoiseScale = _tuneBlendNoiseScale;

			EditableTerrain.Instance.Material.SetShaderParameter("cliff_jitter_strength", _tuneCliffJitterStrength);
			EditableTerrain.Instance.Material.SetShaderParameter("cliff_jitter_scale", _tuneCliffJitterScale);
			EditableTerrain.Instance.Material.SetShaderParameter("blend_softness", _tuneHeightBlendSoftness);
			EditableTerrain.Instance.Material.SetShaderParameter("blend_noise_strength", _tuneBlendNoiseStrength);
			EditableTerrain.Instance.Material.SetShaderParameter("blend_noise_scale", _tuneBlendNoiseScale);
		}
	}

	private HSlider CreateSliderRow(VBoxContainer parent, string labelText, float min, float max, float step, float initialVal, Action<float> onChanged, string format = "0.0#", float labelWidth = 70f)
	{
		var row = new HBoxContainer();
		
		var lblName = new Label();
		lblName.Text = labelText;
		lblName.CustomMinimumSize = new Vector2(labelWidth, 0);
		lblName.AddThemeFontSizeOverride("font_size", 11);
		row.AddChild(lblName);

		var slider = new HSlider();
		slider.MinValue = min;
		slider.MaxValue = max;
		slider.Step = step;
		slider.Value = initialVal;
		slider.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
		slider.DragStarted += () => _isDraggingSlider = true;
		slider.DragEnded += (valueChanged) => _isDraggingSlider = false;
		row.AddChild(slider);

		var lblVal = new Label();
		lblVal.Text = initialVal.ToString(format, System.Globalization.CultureInfo.InvariantCulture);
		lblVal.CustomMinimumSize = new Vector2(34, 0);
		lblVal.HorizontalAlignment = HorizontalAlignment.Right;
		lblVal.AddThemeFontSizeOverride("font_size", 11);
		row.AddChild(lblVal);

		slider.SetMeta("val_label", lblVal);
		slider.SetMeta("val_format", format);

		slider.ValueChanged += (double val) =>
		{
			float fVal = (float)val;
			lblVal.Text = fVal.ToString(format, System.Globalization.CultureInfo.InvariantCulture);
			onChanged(fVal);
		};

		parent.AddChild(row);
		return slider;
	}

	private void UpdateSliderLabel(Slider slider, float val)
	{
		if (slider != null && slider.HasMeta("val_label"))
		{
			var lbl = slider.GetMeta("val_label").As<Label>();
			if (lbl != null && GodotObject.IsInstanceValid(lbl))
			{
				string format = slider.HasMeta("val_format") ? slider.GetMeta("val_format").AsString() : "0.0#";
				lbl.Text = val.ToString(format, System.Globalization.CultureInfo.InvariantCulture);
			}
		}
	}

	private OptionButton CreateDropdownRow(VBoxContainer parent, string labelText, string[] options, int initialIdx, Action<int> onChanged)
	{
		var row = new HBoxContainer();

		var lblName = new Label();
		lblName.Text = labelText;
		lblName.CustomMinimumSize = new Vector2(110, 0);
		lblName.AddThemeFontSizeOverride("font_size", 11);
		row.AddChild(lblName);

		var opt = new OptionButton();
		opt.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
		opt.CustomMinimumSize = new Vector2(90, 0);
		opt.AddThemeFontSizeOverride("font_size", 11);
		for (int i = 0; i < options.Length; i++)
		{
			opt.AddItem(options[i], i);
		}
		if (initialIdx >= 0 && initialIdx < options.Length)
		{
			opt.Select(initialIdx);
		}
		opt.ItemSelected += (long index) =>
		{
			onChanged((int)index);
		};
		row.AddChild(opt);

		parent.AddChild(row);
		return opt;
	}

	private HBoxContainer CreateToggleRow(VBoxContainer parent, string labelText, bool initialVal, Action<bool> onChanged)
	{
		var row = new HBoxContainer();
		var chk = new CheckBox();
		chk.Text = labelText;
		chk.ButtonPressed = initialVal;
		chk.AddThemeFontSizeOverride("font_size", 11);
		chk.Toggled += (bool pressed) => onChanged(pressed);
		row.AddChild(chk);
		parent.AddChild(row);
		return row;
	}

	private CheckBox CreateCheckBoxRow(VBoxContainer parent, string labelText, bool initialVal, Action<bool> onChanged)
	{
		var row = new HBoxContainer();
		var chk = new CheckBox();
		chk.Text = labelText;
		chk.ButtonPressed = initialVal;
		chk.FocusMode = Control.FocusModeEnum.None;
		chk.AddThemeFontSizeOverride("font_size", 11);
		chk.Toggled += (bool pressed) => onChanged(pressed);
		row.AddChild(chk);
		parent.AddChild(row);
		return chk;
	}

	private Label CreateSectionHeader(VBoxContainer parent, string titleText)
	{
		var lbl = new Label();
		lbl.Text = titleText;
		lbl.AddThemeFontSizeOverride("font_size", 11);
		lbl.Modulate = new Color(0.95f, 0.85f, 0.35f);
		parent.AddChild(lbl);
		return lbl;
	}

	public void RefreshWaterSwatches()
	{
		if (_optWaterMode == null) return;

		byte currentProf = GameHost.Instance != null ? GameHost.Instance.ActiveWaterProfileIndex : (byte)0;
		if (_optWaterMode.Selected >= 0 && _optWaterMode.Selected < _optWaterMode.ItemCount)
		{
			var currentMeta = _optWaterMode.GetItemMetadata(_optWaterMode.Selected);
			if (currentMeta.VariantType != Variant.Type.Nil)
			{
				currentProf = (byte)(int)currentMeta;
			}
		}

		_optWaterMode.Clear();

		var profiles = RuntimeTerrain.Instance != null ? RuntimeTerrain.Instance.GetWaterProfiles() : null;
		if (profiles != null && profiles.Count > 0)
		{
			int itemIdx = 0;
			foreach (var kvp in profiles)
			{
				byte pIdx = kvp.Key;
				var prof = kvp.Value;
				string label = TranslationServer.Translate(prof.Name);
				_optWaterMode.AddItem(label, itemIdx);
				_optWaterMode.SetItemMetadata(itemIdx, pIdx);
				itemIdx++;
			}
		}
		else
		{
			var defaults = WaterProfileSaveData.CreateDefaultProfiles();
			int itemIdx = 0;
			foreach (var def in defaults)
			{
				_optWaterMode.AddItem(TranslationServer.Translate(def.Name), itemIdx);
				_optWaterMode.SetItemMetadata(itemIdx, def.ProfileIndex);
				itemIdx++;
			}
		}

		int targetSelected = 0;
		for (int i = 0; i < _optWaterMode.ItemCount; i++)
		{
			var meta = _optWaterMode.GetItemMetadata(i);
			if (meta.VariantType != Variant.Type.Nil && (byte)(int)meta == currentProf)
			{
				targetSelected = i;
				break;
			}
		}
		if (_optWaterMode.ItemCount > 0)
		{
			_optWaterMode.Selected = targetSelected;
		}

		if (GameHost.Instance != null)
		{
			GameHost.Instance.ActiveWaterProfileIndex = currentProf;
		}
	}

	public void OpenWaterProfileDialog()
	{
		var profilesList = new List<WaterProfileSaveData>();
		if (RuntimeTerrain.Instance != null)
		{
			var profDict = RuntimeTerrain.Instance.GetWaterProfiles();
			if (profDict != null && profDict.Count > 0)
			{
				foreach (var p in profDict.Values) profilesList.Add(p);
			}
		}
		if (profilesList.Count == 0)
		{
			profilesList.AddRange(WaterProfileSaveData.CreateDefaultProfiles());
		}

		int targetIdx = 0;
		if (GameHost.Instance != null)
		{
			byte currentProf = GameHost.Instance.ActiveWaterProfileIndex;
			int found = profilesList.FindIndex(p => p.ProfileIndex == currentProf);
			if (found >= 0) targetIdx = found;
		}

		_waterProfileDialog?.OpenWithProfiles(profilesList, targetIdx);
	}

	public void OpenEnvironmentConfigDialog()
	{
		if (_environmentConfigDialog == null)
		{
			_environmentConfigDialog = new EnvironmentConfigDialog(this);
		}

		List<EnvironmentPresetConfig> presetsList = null;
		string defaultPreset = null;
		string wsPath = MapWorkspaceService.GetActiveWorkspacePath();
		if (MetadataService.Instance.TryLoadMetadata(wsPath, out var meta) && meta != null)
		{
			if (meta.CustomEnvironmentPresets != null && meta.CustomEnvironmentPresets.Count > 0)
			{
				presetsList = meta.CustomEnvironmentPresets;
			}
			defaultPreset = meta.DefaultEnvironmentPreset;
		}

		if (presetsList == null || presetsList.Count == 0)
		{
			presetsList = GameHost.Instance?.EnvironmentService?.GetPresets().ToList() ?? EnvironmentPresetConfig.CreateDefaultPresets();
		}

		string currentId = GameHost.Instance?.EnvironmentService?.GetCurrentPresetId() ?? defaultPreset ?? "day";
		_environmentConfigDialog?.OpenWithPresets(presetsList, currentId, defaultPreset);
	}
}
