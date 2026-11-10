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

	private EditorModule _activeModule { get; set; } = EditorModule.Terrain;





	private VBoxContainer _accordionToolSettings { get; set; } = null!;
	private Button _btnHeaderToolSettings { get; set; } = null!;
	private VBoxContainer _contentToolSettings { get; set; } = null!;





	private VBoxContainer _accordionFile { get; set; } = null!;

	private MapSettingsDialog? _mapSettingsDialog { get; set; }

	private VBoxContainer _accordionInspector { get; set; } = null!;
	private Button _btnHeaderInspector { get; set; } = null!;
	private VBoxContainer _contentInspector { get; set; } = null!;

	private VBoxContainer _containerTextureSettings { get; set; } = null!;
	private VBoxContainer _containerPathingSettings { get; set; } = null!;
	private VBoxContainer _containerPlacementSettings { get; set; } = null!;
	private VBoxContainer _containerEyedropperSettings { get; set; } = null!;
	private VBoxContainer _containerCategorySelector { get; set; } = null!;


	private HBoxContainer _rowPolarConfig { get; set; } = null!;

	private ThrottledLabelWidth _measureLabelWidth { get; set; } = new();



	private Button _btnReplaceTexture { get; set; } = null!;

	private PanelContainer _topBar { get; set; } = null!;
	private VBoxContainer _middleRightBox { get; set; } = null!;
	private HBoxContainer _topLeftBox { get; set; } = null!;
	private TextureRect _screenFrameRect { get; set; } = null!;
	private MapEditorHudFade? _hudFade;






	private Control _spacingBox { get; set; } = null!;
	private Control _camBoundsBox { get; set; } = null!;

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
	private double _mapNameUpdateTimer { get; set; } = 0.0;
	private bool IsUpdatingInspectorUI { get; set; }

	private CheckBox? ChkApplyGroundTexture { get; set; }
	private CheckBox? ChkApplyCliffTexture { get; set; }
	private HBoxContainer RowGroundTexture { get; set; } = null!;
	private HBoxContainer RowCliffTexture { get; set; } = null!;

	private VBoxContainer _placementRotateBox { get; set; } = null!;
	private VBoxContainer _placementScaleBox { get; set; } = null!;
	private OptionButton _optEyedropperMode { get; set; } = null!;







	private CheckBox _chkShallowWater { get; set; } = null!;
	private CheckBox _chkDeepWater { get; set; } = null!;
	private CheckBox _chkFlying { get; set; } = null!;
	private CheckBox _chkGround { get; set; } = null!;
	private CheckBox _chkBuildable { get; set; } = null!;

	private OptionButton _optPathingMode { get; set; } = null!;




	private Button? _activeToolButton { get; set; }
	private StyleBoxFlat _highlightStyle { get; set; } = null!;

	private Label _lblInfoText { get; set; } = null!;
	private Label _lblTerrainTexture { get; set; } = null!;
	private Label _lblCliffTexture { get; set; } = null!;

	private PanelContainer? _scaleMapDialog => _scaleDialog?.Panel;

	private Camera3D? _camera3D { get; set; }
	private bool _isDraggingSlider { get; set; } = false;

	private MapEditorTopBar _topBarController { get; set; } = null!;
	private MapEditorBrushSettings _brushSettingsController { get; set; } = null!;
	private MapEditorPlacementSettings _placementSettingsController { get; set; } = null!;
	private MapEditorInspector _inspectorController { get; set; } = null!;
	private MapEditorPathingPanel _pathingPanelController { get; set; } = null!;
	private PlacementSettingsPanel _placementPanel { get; set; } = null!;
	private ToolAccordionPanel _toolPanel { get; set; } = null!;
	private TopToolbarPanel _topToolbar { get; set; } = null!;
	private FileOperationsPanel _filePanel { get; set; } = null!;
	private CameraControlsPanel _cameraPanel { get; set; } = null!;
	private ViewportPanel _viewportPanel { get; set; } = null!;
	private InspectorPanel _inspectorView { get; set; } = null!;
	private BrushSettingsPanel _brushPanel { get; set; } = null!;
	private WaterSettingsPanel _waterPanel { get; set; } = null!;
	private PasteSettingsPanel _pastePanel { get; set; } = null!;
	private MapEditorMinimap _minimapController { get; set; } = null!;
	private MapEditorEntityPaletteController _entityPaletteController { get; set; } = null!;

	private bool _wasmHasErrors { get; set; } = false;
	private string _wasmCompileLogPath { get; set; } = "";




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
		if (tool == EditorTool.DrawCoordinate && _toolPanel.CommitCoordinateButton != null)
		{
			_toolPanel.CommitCoordinateButton.Visible = false;
		}

		if (NeedsPathingOverlay(tool))
		{
			GameContext.Host?.UpdatePathingOverlay();
		}
	}

	private void ApplyBrushStrengthForTool(EditorTool tool)
	{
		if (_brushPanel?.StrengthSlider == null || GameContext.Host == null) return;

		if (tool == EditorTool.PaintTexture)
		{
			_brushPanel.StrengthSlider.MinValue = 0.0;
			_brushPanel.StrengthSlider.MaxValue = 10.0;
			_brushPanel.StrengthSlider.Step = 1.0;
			_brushPanel.StrengthSlider.Value = GameContext.SessionState.SavedTextureIntensity;
			if (_brushPanel?.StrengthValue != null) _brushPanel.StrengthValue.Text = GameContext.SessionState.SavedTextureIntensity.ToString("F0");
			GameContext.Host.EditorBrushStrength = GameContext.SessionState.SavedTextureIntensity;
		}
		else
		{
			_brushPanel.StrengthSlider.MinValue = 0.5;
			_brushPanel.StrengthSlider.MaxValue = 10.0;
			_brushPanel.StrengthSlider.Step = 0.5;
			float restoredStrength = Math.Max(0.5f, GameContext.SessionState.SavedBrushStrength);
			_brushPanel.StrengthSlider.Value = restoredStrength;
			if (_brushPanel?.StrengthValue != null) _brushPanel.StrengthValue.Text = restoredStrength.ToString("F1");
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
		if (_topToolbar.StatusLabel == null) return;

		string toolName = Realm.Client.Services.EditorToolLabels.GetLocalizedToolName(tool);
		if (!string.IsNullOrEmpty(shortPlaceName)) toolName += $" ({shortPlaceName})";

		_topToolbar.StatusLabel.Text = string.Format(TranslationServer.Translate("ACTIVE TOOL: {0}"), toolName);
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


	private ColorRect? _helpOverlayPanel => _helpPanel?.Panel;


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

		if (_accordionFile != null) _accordionFile.Visible = true;
		if (_viewportPanel.Accordion != null) _viewportPanel.Accordion.Visible = true;
		if (_toolPanel.Accordion != null) _toolPanel.Accordion.Visible = true;

		if (_brushPanel?.Accordion != null) _brushPanel.Accordion.Visible = IsBrushAccordionVisible(module);
		if (_accordionToolSettings != null) _accordionToolSettings.Visible = IsToolSettingsAccordionVisible(module);
		if (_placementPanel?.Accordion != null) _placementPanel.Accordion.Visible = IsPlacementAccordionVisible(module);
		if (_accordionInspector != null) _accordionInspector.Visible = IsInspectorAccordionVisible(module);
	}

	private void SetModuleVBoxVisibilities(EditorModule module)
	{
		if (_toolPanel.TerrainPanel != null) _toolPanel.TerrainPanel.Visible = (module == EditorModule.Terrain);
		if (_toolPanel.DecoPanel != null) _toolPanel.DecoPanel.Visible = (module == EditorModule.TextureDeco);
		if (_toolPanel.PathingPanel != null) _toolPanel.PathingPanel.Visible = (module == EditorModule.Pathing);
		if (_toolPanel.CoordinatesPanel != null) _toolPanel.CoordinatesPanel.Visible = (module == EditorModule.Coordinates);
		if (_toolPanel.ObjectsPanel != null) _toolPanel.ObjectsPanel.Visible = (module == EditorModule.Objects);
		if (_toolPanel.ClipboardPanel != null) _toolPanel.ClipboardPanel.Visible = (module == EditorModule.Clipboard);
	}

	private void SetSettingsVisibilities(EditorModule module)
	{
		if (_containerTextureSettings != null) _containerTextureSettings.Visible = (module == EditorModule.Terrain || module == EditorModule.TextureDeco);
		if (_containerPathingSettings != null) _containerPathingSettings.Visible = (module == EditorModule.Pathing);
		if (_pastePanel?.Container != null) _pastePanel.Container.Visible = (module == EditorModule.Clipboard);
		if (_containerCategorySelector != null) _containerCategorySelector.Visible = (module == EditorModule.Objects);
	}

	private void SetContentHeaders(EditorModule module)
	{
		SetHeaderContent(_toolPanel.Content, _toolPanel.Header, true, "Tool");

		bool showBrush = module is EditorModule.Terrain or EditorModule.TextureDeco or EditorModule.Pathing;
		SetHeaderContent(_brushPanel.Content, _brushPanel.Header, showBrush, "Global Brush Properties");

		bool showToolSettings = module is EditorModule.Terrain or EditorModule.TextureDeco or EditorModule.Pathing or EditorModule.Objects or EditorModule.Clipboard;
		SetHeaderContent(_contentToolSettings, _btnHeaderToolSettings, showToolSettings, "Tool Settings");

		bool showPlacement = module is EditorModule.Objects or EditorModule.Clipboard;
		SetHeaderContent(_placementPanel.Content, _placementPanel.Header, showPlacement, "Placement Config");
	}

	private void SetHeaderContent(Control content, Button btn, bool isVisible, string translationKey)
	{
		if (content == null || !isVisible) return;
		content.Visible = true;
		if (btn != null) btn.Text = TranslationServer.Translate(translationKey).ToString().ToUpperInvariant() + "  ▼";
	}


	private void RefreshCardScrollStates()
	{
		foreach (var card in Cards()) MapEditorCardTheme.RefreshScroll(card.Content, card.ScrollMaxHeight, card.AllowExpand, card.Scrollable);
	}

	private FontVariation? _faFontVariation { get; set; }

	private HashSet<ulong> _hookedSliderInstanceIds { get; set; } = new();





	private ObjectAttachmentDialog _objectAttachmentDialog { get; set; } = null!;




	public void InvalidateMetadataCache()
	{
		GameContext.EditorService.CachedMapName = null;
		GameContext.EditorService.CachedMapVersion = null;
		GameContext.EditorService.LastMapNameCacheTicks = 0;
	}

	private VBoxContainer _contentLightingTuning => LightingTuning.Content;

}