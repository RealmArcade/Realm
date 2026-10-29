using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Godot;
using Realm.Client.Core;
using Realm.Client.Network;
using Realm.Client.Services;
using Realm.Client.UI.MapEditor;
using Realm.Ecs.Components.Core;
using Realm.Ecs.Services;
using Realm.Shared.Distribution;
using static Godot.Control;

namespace Realm.Client.UI;

public partial class MapEditorHUD : Control
{
	public MapEditorHUD()
	{
		GameContext.RegisterMapEditorHud(this);
	}

	public override void _EnterTree()
	{
		Realm.Client.Services.GameContext.EnsureServices();
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


	public MapEditorHUDViewModel _viewModel { get; set; } = new();

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

	private VBoxContainer _accordionBrush { get; set; }
	private Button _btnHeaderBrush { get; set; }
	private VBoxContainer _contentBrush { get; set; }

	private VBoxContainer _accordionWater { get; set; }
	private Button _btnHeaderWater { get; set; }
	private VBoxContainer _contentWater { get; set; }
	private Button _btnWaterActionAdd { get; set; }
	private Button _btnWaterActionRemove { get; set; }


	private VBoxContainer _accordionTool { get; set; }
	private Button _btnHeaderTool { get; set; }
	private VBoxContainer _contentTool { get; set; }

	private VBoxContainer _accordionToolSettings { get; set; }
	private Button _btnHeaderToolSettings { get; set; }
	private VBoxContainer _contentToolSettings { get; set; }

	private VBoxContainer _accordionPlacement { get; set; }
	private Button _btnHeaderPlacement { get; set; }
	private VBoxContainer _contentPlacement { get; set; }

	private VBoxContainer _accordionViewport { get; set; }
	private Button _btnHeaderViewport { get; set; }
	private VBoxContainer _contentViewport { get; set; }


	private Dictionary<Control, CardDragData> _cardDragMap { get; set; } = new();
	private Button _btnResetLayout { get; set; }

	private VBoxContainer _accordionFile { get; set; }
	private Button _btnHeaderFile { get; set; }
	private Control _contentFile { get; set; }

	private MapSettingsDialog? _mapSettingsDialog { get; set; }

	private VBoxContainer _accordionInspector { get; set; }
	private Button _btnHeaderInspector { get; set; }
	private VBoxContainer _contentInspector { get; set; }

	private VBoxContainer _containerTextureSettings { get; set; }
	private VBoxContainer _containerPathingSettings { get; set; }
	private VBoxContainer _containerPlacementSettings { get; set; }
	private VBoxContainer _containerEyedropperSettings { get; set; }
	private VBoxContainer _containerPasteSettings { get; set; }
	private VBoxContainer _containerCategorySelector { get; set; }

	private VBoxContainer _panelObjects { get; set; }
	private VBoxContainer _panelClipboard { get; set; }
	private VBoxContainer _panelTerrainVBox { get; set; }
	private VBoxContainer _panelDecoVBox { get; set; }
	private VBoxContainer _panelPathingVBox { get; set; }
	private VBoxContainer _panelCoordinatesVBox { get; set; }
	private Button _btnCut { get; set; }
	private Button _btnEraseArea { get; set; }
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

	private PanelContainer _panelMeasurementHUD { get; set; }
	private Label _lblMeasureTelemetry { get; set; }
	private ThrottledLabelWidth _measureLabelWidth { get; set; } = new();

	private Button _btnPasteAnchor { get; set; }
	private Label _lblPasteTelemetry { get; set; }


	private List<Button> _swatchButtons { get; set; } = new List<Button>();


	private ScrollContainer _scrollSwatches { get; set; }
	private Control _gridSwatches { get; set; }
	private Button _btnReplaceTexture { get; set; }

	private PanelContainer _topBar { get; set; }
	private VBoxContainer _middleRightBox { get; set; }
	private HBoxContainer _topLeftBox { get; set; }
	private TextureRect _screenFrameRect { get; set; }
	private Tween _hudFadeTween { get; set; }
	private bool _is3DInteractionActive { get; set; } = false;

	private Dictionary<Control, Control.MouseFilterEnum> _savedMouseFilters { get; set; } = new();

	private Button _btnBackToHub { get; set; }
	private Button _btnPublish { get; set; }
	private Button _btnSave { get; set; }
	private Button _btnSaveAs { get; set; }
	private Button _btnTestMap { get; set; }
	private OptionButton _optTestSettings { get; set; }
	private Button _btnExportMap { get; set; }
	private Button _btnLoad { get; set; }
	private Button _btnLoadMore { get; set; }
	private PopupPanel _popupLoadMore { get; set; }
	private Button _btnImportArchive { get; set; }
	private Button _btnDeleteObject { get; set; }
	private Button _btnUndo { get; set; }
	private Button _btnRedo { get; set; }

	private Label _statusLabel { get; set; }
	private Label _feedbackLabel { get; set; }

	private Button _btnZoomIn { get; set; }
	private Button _btnZoomOut { get; set; }
	private Button _btnCenter { get; set; }
	private Button _btnRotate { get; set; }
	private Button _btnCameraAngle { get; set; }

	private Slider _sldBrushSize { get; set; }
	private Label _lblBrushSizeValue { get; set; }
	private Control _brushSizeBox { get; set; }
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

	private Control _waterHeightBox { get; set; }
	private Slider _sldWaterHeight { get; set; }
	private Label _lblWaterHeightValue { get; set; }
	private Control _waterModeBox { get; set; }
	private OptionButton _optWaterMode { get; set; }
	private Button _btnWaterProfiles { get; set; }
	private WaterProfileDialog _waterProfileDialog { get; set; }
	private EnvironmentConfigDialog _environmentConfigDialog { get; set; }
	private EntityVisualEditDialog _entityVisualEditDialog { get; set; }
	private AnimationPreviewDialog _animationPreviewDialog { get; set; }
	private WeaponVfxDialog _weaponVfxDialog { get; set; }
	private ModelPickerDialog _modelPickerDialog { get; set; }
	private AbilityVfxDialog _abilityVfxDialog { get; set; }
	private AssetManagerDialog _assetManagerDialog { get; set; }
	private ImportFromMapDialog _importFromMapDialog { get; set; }
	private TemplateManagerDialog _templateManagerDialog { get; set; }
	private InstanceManagerDialog _instanceManagerDialog { get; set; }
	private AssetBrowserDialog _assetBrowserDialog { get; set; }
	private NoiseTextureDialog _noiseTextureDialog { get; set; }
	private ConvertGlbDialog _convertGlbDialog { get; set; }
	private EditorSettingsDialog _editorSettingsDialog { get; set; }
	private ShaderEditorDialog _shaderEditorDialog { get; set; }
	private VfxStudioDialog _vfxStudioDialog { get; set; }
	private ProceduralAnimationStudioDialog _proceduralAnimationStudioDialog { get; set; }
	private AuthorSignatureDialog _authorSignatureDialog { get; set; }
	private ReplaceTextureDialog _replaceTextureDialog { get; set; }
	private UtilitiesDialog _utilitiesDialog { get; set; }
	private TestSettingsDialog _testSettingsDialog { get; set; }
	private Button _btnEditorSettings { get; set; }
	private PanelContainer _mapNameHeaderPanel { get; set; }
	private Label _lblMapNameHeader { get; set; }
	private double _mapNameUpdateTimer { get; set; } = 0.0;
	private Button _btnOpenGlobalOverrides { get; set; }
	private Button BtnEditVfx { get; set; }
	private Button BtnAssetsManager { get; set; }
	private Button BtnTemplateManager { get; set; }
	private Button BtnInstanceManager { get; set; }
	private Button BtnImportFromMap { get; set; }
	private Button BtnUtilities { get; set; }
	private bool IsUpdatingInspectorUI { get; set; }

	private CheckBox ChkApplyGroundTexture { get; set; }
	private CheckBox ChkApplyCliffTexture { get; set; }
	private HBoxContainer RowGroundTexture { get; set; }
	private HBoxContainer RowCliffTexture { get; set; }

	private Slider _sldPlacementRotate { get; set; }
	private Label _lblPlacementRotateValue { get; set; }
	private Slider _sldPlacementScale { get; set; }
	private Label _lblPlacementScaleValue { get; set; }
	private VBoxContainer _placementRotateBox { get; set; }
	private VBoxContainer _placementScaleBox { get; set; }
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
	private Button _btnSaveMore { get; set; }
	private PopupPanel _popupSaveMore { get; set; }
	private Button _btnEyedropper { get; set; }
	private OptionButton _optEyedropperMode { get; set; }
	private Button _btnNoise { get; set; }
	private Button _btnWater { get; set; }
	private PanelContainer _minimapFrame { get; set; }
	private Control _minimapArea { get; set; }
	private MapEditorCameraIndicator _cameraIndicator { get; set; }


	private Button _btnToggleCamera { get; set; }
	private PopupPanel _popupCamera { get; set; }
	private CheckBox _chkFreeCamera { get; set; }


	private Button _btnRaise { get; set; }
	private Button _btnLower { get; set; }
	private Button _btnHeight { get; set; }
	private Button _btnSmooth { get; set; }
	private Button _btnPlateau { get; set; }
	private Button _btnRamp { get; set; }
	private OptionButton _optMirrorMode { get; set; }
	private OptionButton _optPlacementMirrorMode { get; set; }
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
	private Label _lblInspectorPos { get; set; }
	private Button _btnInspectorRotLeft { get; set; }
	private Button _btnInspectorRotRight { get; set; }
	private Button _btnInspectorScaleDown { get; set; }
	private Button _btnInspectorScaleUp { get; set; }
	private Button _btnInspectorScaleReset { get; set; }
	private Button _btnInspectorDelete { get; set; }
	private Button _btnShowCoverage { get; set; }
	private HBoxContainer _playerOwnerContainer { get; set; }
	private OptionButton _optPlayerOwner { get; set; }
	private HBoxContainer _placementPlayerContainer { get; set; }
	private OptionButton _optPlacementPlayer { get; set; }
	private PanelContainer _rigStatusContainer { get; set; }
	private Label _lblRigStatus { get; set; }

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
	private Button _btnViewCoordinates { get; set; }


	private Button _activeToolButton { get; set; } = null;
	private StyleBoxFlat _highlightStyle { get; set; }

	private Label _lblInfoText { get; set; }
	private Label _lblTerrainTexture { get; set; }
	private Label _lblCliffTexture { get; set; }

	private PanelContainer _scaleMapDialog { get; set; }
	private Label _lblScalePreviewWidth { get; set; }
	private Label _lblScalePreviewHeight { get; set; }

	private int _scaleDialogTargetDepth { get; set; }

	private Camera3D _camera3D { get; set; }
	private Button _btnEditors { get; set; }
	private Button _btnVSCode { get; set; }
	private PopupPanel _popupEditors { get; set; }
	private bool _isDraggingSlider { get; set; } = false;
	private Panel _swatchHighlightPanel { get; set; }
	private Panel _swatchCliffHighlightPanel { get; set; }

	private MapEditorTopBar _topBarController { get; set; }
	private MapEditorBrushSettings _brushSettingsController { get; set; }
	private MapEditorPlacementSettings _placementSettingsController { get; set; }
	private MapEditorInspector _inspectorController { get; set; }
	private MapEditorPathingPanel _pathingPanelController { get; set; }
	private MapEditorMinimap _minimapController { get; set; }
	private MapEditorEntityPaletteController _entityPaletteController { get; set; }

	private bool _wasmHasErrors { get; set; } = false;
	private string _wasmCompileLogPath { get; set; } = "";


	public void PublishMapActionExternal()
	{
		string wsPath = !string.IsNullOrEmpty(GameContext.EditorService._tempWorkspacePath) ? GameContext.EditorService._tempWorkspacePath : MapWorkspaceService.GetActiveWorkspacePath();

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
		optType.Name = "OptType_OptionButton";
		optType.AddItem("Custom Arcade Map", 0);
		optType.AddItem("Reusable Asset Pack", 1);
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

		var tipLabel = new Label { Text = "💡 " + string.Format(TranslationServer.Translate("Optimization Advisory: We recommend running 'Prune Unused Assets' and 'Normalize References' in the Map Utilities dialog before publishing to remove unused files and reference shared greenlit assets (potential savings: {0})."), MapStorageService.FormatBytes(potentialSavedBytes)), AutowrapMode = TextServer.AutowrapMode.WordSmart };
		tipLabel.AddThemeFontSizeOverride("font_size", 12);
		tipLabel.AddThemeColorOverride("font_color", UIStyle.ColorGold);
		tipMargin.AddChild(tipLabel);
		vbox.AddChild(tipPanel);
	}

	private void CreatePublishMapIdentityPanel(VBoxContainer vbox)
	{
		string keyDir = ProjectSettings.GlobalizePath("user://appdata/keys/");
		string defaultUsername = GameContext.Lobby?.AuthenticatedUsername ?? System.Environment.UserName;
		var (_, keyData, keyPath, _) = AuthorshipKeyHelper.GetOrGenerateKeyInfo(keyDir, defaultUsername);
		string currentAuthorName = !string.IsNullOrWhiteSpace(keyData?.UserName) ? keyData.UserName : defaultUsername;
		string authorPubKey = !string.IsNullOrWhiteSpace(keyData?.PublicKey) ? keyData.PublicKey : "";
		string shortPubKey = authorPubKey.Length > 16 ? $"{authorPubKey[..8]}...{authorPubKey[^8..]}" : authorPubKey;

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

		var btnEditKey = new Button { Text = TranslationServer.Translate("Manage Key / Identity"), CustomMinimumSize = new Vector2(160, 26) };
		btnEditKey.AddThemeConstantOverride("icon_max_width", 0);
		btnEditKey.AddThemeFontSizeOverride("font_size", 11);
		btnEditKey.Pressed += () =>
		{
			_authorSignatureDialog ??= new AuthorSignatureDialog(this);
			_authorSignatureDialog.OpenDialog();
		};
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


	public void TriggerToolSelection(EditorTool tool, Button btn, string placeId = "")
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
		if (tool != EditorTool.SelectMove)
		{
			GameContext.Host.SelectedEditorObject = null;
		}

		if (tool != EditorTool.Ramp)
		{
			GameContext.Host.ClearRampStartPosExternal();
		}

		if (tool != EditorTool.Measure)
		{
			GameContext.Host.ClearMeasureVisuals();
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

		string toolName = tool.ToString().ToUpper();
		if (!string.IsNullOrEmpty(shortPlaceName)) toolName += $" ({shortPlaceName})";

		_statusLabel.Text = $"ACTIVE TOOL: {toolName}";
	}

	private void UpdateToolInfoText(EditorTool tool, string shortPlaceName)
	{
		if (tool == EditorTool.PlaceUnit)
		{
			string alignment = PlayerColorConfig.GetPaletteLabel(GameContext.EditorService.PlaceUnitPlayerIndex);
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

		if (EditorService.ToolInfoTexts.TryGetValue(tool, out string rawText))
		{
			_lblInfoText.Text = TranslationServer.Translate(rawText);
		}
	}


	private ColorRect _helpOverlayPanel { get; set; } = null;


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

	private FontVariation _faFontVariation { get; set; }

	private void StyleGridButton(Button btn)
	{
		if (btn == null) return;
		btn.Flat = false;
		btn.CustomMinimumSize = new Vector2(105, 30);
		btn.SizeFlagsHorizontal = SizeFlags.ExpandFill;

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

	private bool AnyOptionButtonPopupVisible(params OptionButton[] buttons)
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
		Control[] checkControls =
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


	private ObjectAttachmentDialog _objectAttachmentDialog { get; set; }


	public async Task ProceedToTestMap()
	{
		if (GameContext.Host == null) return;

		_wasmHasErrors = false;
		ShowWasmConsoleModal();
		Action<string> logHandler = line => AppendWasmConsoleLog(line);
		GameLog.OnLine += logHandler;

		try
		{
			SaveEditorStateBeforeTest();
			await PrepareMapAndWorkspaceForTest();

			if (_wasmHasErrors) return;

			if (!TryLocateCompiledWasm(out string wasmPath)) return;

			AppendWasmConsoleLog($"[WASM] Located compiled WASM binary: {Path.GetFileName(wasmPath)}");

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
			GameLog.OnLine -= logHandler;
		}
	}

	private void SaveEditorStateBeforeTest()
	{
		SaveCameraState();
		SaveGameHostStates();
		SaveEditorWorldBeforeTest();
	}

	/// <summary>
	///     Keeps the editor's world so the world the playtest builds cannot be built on it. What is played has to be the
	///     map as it was saved, standing in a world of its own, and the editor is put back from here once the playtest
	///     has ended.
	/// </summary>
	private void SaveEditorWorldBeforeTest()
	{
		GameContext.SessionState.EditorWorldState = MapStateSnapshot.CreateSnapshot();
	}

	private void SaveCameraState()
	{
		var camera = GameContext.Host.MainCamera;
		if (camera == null) return;

		GameContext.SessionState.SavedCameraPosition = camera.Position;

		var host = GameContext.Host;
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
		GameContext.SessionState.SavedGridMode = GameContext.EditorService.EditorGridMode;
		GameContext.SessionState.SavedActiveTool = host.ActiveEditorTool;
		GameContext.SessionState.SavedActivePlaceId = host.ActivePlaceId;
		GameContext.SessionState.SavedCameraBoundsVisible = GameContext.EditorService.EditorCameraBoundsVisible;
		GameContext.SessionState.SavedDisableShadows = GameContext.EditorService.EditorDisableShadows;
		GameContext.SessionState.SavedEditorHasUnsavedChanges = host.EditorHasUnsavedChanges;
		GameContext.SessionState.SavedEntityCategory = _entityPaletteController?.CurrentCategory ?? "";
		GameContext.SessionState.SavedBrushRadius = (float)(_sldBrushSize?.Value ?? 1f);
		float val = (float)(_sldBrushStrength?.Value ?? 1f);
		GameContext.SessionState.SavedBrushStrength = val;
	}

	private async Task PrepareMapAndWorkspaceForTest()
	{
		MapWorkspaceService.EnsureWasmEntryPoint(GameContext.EditorService._tempWorkspacePath);
		string tempTerrainPath = Path.Combine(GameContext.EditorService._tempWorkspacePath, "terrain.json");
		GameContext.Host.SaveMapToFile(tempTerrainPath);
		GameContext.Host.EditorHasUnsavedChanges = false;
		InvalidateMetadataCache();

		if (OperatingSystem.IsWindows())
		{
			SetWasmConsoleStatus("Auto-saving modified files in VSCode...", UIStyle.ColorCyanGlow);
			AppendWasmConsoleLog("[VSCode] Requesting auto-save of all open workspace files...");
			await GameContext.VSCode.SaveAllOpenFilesAsync();
		}

		SetWasmConsoleStatus("Compiling WASM map script...", UIStyle.ColorCyanGlow);
		AppendWasmConsoleLog("=== WASM COMPILATION PIPELINE STARTED ===");
		AppendWasmConsoleLog("[DEBUG] The map script runs in the authority process, not in this editor. Attach with the workspace's 'Attach to Realm Authority (Map Script WASM)' configuration; the authority reports its PID and the JIT ELF it exported once it is running.");

		await CompileAndSignMapAsync(GameContext.EditorService._tempWorkspacePath, skipAttribution: true, isDebug: true);

		if (_wasmHasErrors)
		{
			SetWasmConsoleStatus("❌ WASM Compilation Failed", new Color(1.0f, 0.3f, 0.3f));
			AppendWasmConsoleLog("[ERROR] Map script compilation failed. Test mode aborted.");
		}
	}

	private bool TryLocateCompiledWasm(out string wasmPath)
	{
		string binDir = Path.Combine(GameContext.EditorService._tempWorkspacePath, "bin");
		wasmPath = null;
		if (Directory.Exists(binDir))
		{
			var wasmFiles = Directory.GetFiles(
				binDir,
				"*.wasm",
				SearchOption.AllDirectories
			).Where(f => !f.Contains("native") && !f.Contains("obj")).ToList();

			wasmPath = wasmFiles.FirstOrDefault(f => f.Contains("publish"))
			           ?? wasmFiles.OrderByDescending(f => File.GetLastWriteTimeUtc(f)).FirstOrDefault();
		}

		if (!File.Exists(wasmPath))
		{
			_wasmHasErrors = true;
			SetWasmConsoleStatus("❌ WASM Compilation Failed: Output binary missing", new Color(1.0f, 0.3f, 0.3f));
			AppendWasmConsoleLog($"[ERROR] Could not find compiled WASM in {binDir}. Test mode aborted.");
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

		GameContext.Host.ExitMapEditorMode();
		GameContext.SessionState.IsTestMode = true;
		ShadowClientVerifier.IsEnabled = true;

		if (GameContext.UserInterface != null)
		{
			GameContext.UserInterface.TransitionTo(GameScreen.InGameHUD);
		}

		// The authority loads the map, runs its script and bakes the mesh this screen will path through, which takes as
		// long as a map takes to load. That happens in another process and is waited for here rather than blocking the
		// frame thread, so the editor keeps drawing and this console stays readable while it happens.
		SetWasmConsoleStatus("Starting the authority that runs this map...", UIStyle.ColorCyanGlow);
		AppendWasmConsoleLog("[AUTHORITY] Starting the process the map script runs in; the editor stays responsive while it loads.");

		bool started = false;
		if (GameContext.Lobby != null)
		{
			started = await GameContext.Lobby.HostSinglePlayerGame(GameContext.EditorService.TempWorkspaceGodotPath, "Test Map", debugMapScript: true);
		}

		// Leaving editor mode takes the ground with it, and the match is what builds the one this screen plays on. A match
		// that could not be started therefore leaves a map with no terrain and nothing in it, so the console that was
		// reporting the launch stays open and says why instead of closing on a playtest that never began.
		if (!started)
		{
			_wasmHasErrors = true;
			SetWasmConsoleStatus("❌ Test mode could not start", new Color(1f, 0.3f, 0.3f));
			AppendWasmConsoleLog($"[ERROR] {GameContext.Lobby?.LastHostError ?? "this build has no lobby to start a match with"}");
			AppendAuthorityDiagnostics();
			return;
		}

		// The authority is the process the script runs in, so its identifier is what a debugger has to be given. It is
		// printed for the console that closes in a moment and written to the game log the match itself keeps.
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

	/// <summary>How many of the authority's last lines are shown when the match it was starting did not come up.</summary>
	private const int AuthorityDiagnosticsLineCount = 20;

	/// <summary>
	///     Shows the end of what the authority printed when a playtest could not be started. The process that runs the map
	///     script is the only thing that knows what went wrong with it, and its console is the file it says so in, so the
	///     dialog that reported the failure says it too rather than leaving the reason in a log nobody opens.
	/// </summary>
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

	private Button _btnHeaderLightingTuning { get; set; }
	private VBoxContainer _contentLightingTuning { get; set; }

	private HSlider _sldHeightBlendSoftness { get; set; }
	private HSlider _sldBlendNoiseStrength { get; set; }
	private HSlider _sldBlendNoiseScale { get; set; }
}