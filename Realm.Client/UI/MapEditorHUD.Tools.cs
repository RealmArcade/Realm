using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using Godot;
using Godot.Collections;
using NSec.Cryptography;
using Realm.Client.Animation;
using Realm.Client.Core;
using Realm.Client.Network;
using Realm.Client.Services;
using Realm.Client.Services.MapHistory;
using Realm.Client.Services.ModelOptimization;
using Realm.Client.Services.Publishing;
using Realm.Client.UI.MapEditor;
using Realm.Client.VFX;
using Realm.Ecs.Components.Terrain;
using Realm.Ecs.Services;
using Realm.EditorAPI;
using Realm.Shared;
using Realm.Shared.Distribution;
using Realm.Shared.ModelOptimization;
using Realm.Shared.Services;
using Realm.Shared.Textures;
using Environment = System.Environment;
using HttpClient = System.Net.Http.HttpClient;
using Key = Godot.Key;
using MirrorMode = Realm.Ecs.Components.Core.MirrorMode;
using TerrainCell = Realm.Ecs.Components.Terrain.TerrainCell;
using WaterType = Realm.Ecs.Components.Terrain.WaterType;

using static Godot.Control;
using Realm.Client.Utils;

namespace Realm.Client.UI
{
	public partial class MapEditorHUD
	{
		private void InitCoreServices()
		{
			GameContext.RegisterMapEditorHud(this);
			GameContext.MetadataService.MetadataSaved += OnMetadataSaved;

			UpdateFPSVisibility();

			_camera3D = (GameContext.Host?.MainCamera);

			HookSliders(this);
			ChildEnteredTree += (node) => HookSliders(node);
		}

		private void InitUIHooksAndStyles()
		{
			_highlightStyle = new StyleBoxFlat();
			_highlightStyle.BgColor = new Color(0.35f, 0.28f, 0.18f, 0.95f);
			_highlightStyle.BorderColor = UIStyle.ColorGold;
			_highlightStyle.SetBorderWidthAll(2);
			_highlightStyle.SetCornerRadiusAll(6);
			_highlightStyle.ContentMarginLeft = 4;
			_highlightStyle.ContentMarginRight = 4;
			_highlightStyle.ContentMarginTop = 4;
			_highlightStyle.ContentMarginBottom = 4;

			var panelTex = TrimmedTextureLoader.LoadTrimmedTexture("res://Assets/UI/map_editor_panel.webp");
			if (panelTex != null)
			{
				var frameRect = new TextureRect();
				frameRect.Name = "MapEditorScreenFrame";
				frameRect.Texture = panelTex;
				frameRect.SetAnchorsPreset(LayoutPreset.FullRect);
				frameRect.ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize;
				frameRect.StretchMode = TextureRect.StretchModeEnum.Scale;
				frameRect.MouseFilter = MouseFilterEnum.Ignore;
				frameRect.Visible = !EditorSettingsDialog.CurrentSettings.HideChromeBorderOverlay;
				AddChild(frameRect);
				MoveChild(frameRect, 0);
				_screenFrameRect = frameRect;
			}
		}

		private void InitBasicPanels()
		{
			_topLeftBox = GetNodeOrNull<HBoxContainer>("TopLeftBox");
			_topBar = GetNodeOrNull<PanelContainer>("BottomCenterBar/TopBar");

			_panelLeft = GetNode<Panel>("LeftSlidePanel");
			_panelRight = GetNode<Panel>("RightSlidePanel");
			if (_panelLeft != null) _panelLeft.MouseFilter = MouseFilterEnum.Ignore;
			if (_panelRight != null) _panelRight.MouseFilter = MouseFilterEnum.Ignore;
		}

		private void InitSidePanels()
		{
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
		}

		public override void _Ready()
		{
			try
			{
				InitCoreServices();
				InitUIHooksAndStyles();
				InitBasicPanels();
				InitSidePanels();

				BuildTopLeftToolbar();

				BuildWindowChrome();

				BuildEditorAccordions();

				WireEditorControllers();

				RestoreEditorSessionAfterReady();
			}
			catch (Exception ex)
			{
				GameLog.LogError($"CRITICAL ERROR IN MAPEDITORHUD _READY: {ex}");
				throw;
			}
		}


		private void BuildInspectorAndPlacementAccordions()
		{
			_accordionInspector = GetNode<VBoxContainer>("RightSlidePanel/RightScroll/AccordionContainer/InspectorAccordion");
			_btnHeaderInspector = GetNode<Button>("RightSlidePanel/RightScroll/AccordionContainer/InspectorAccordion/BtnHeaderInspector");
			_contentInspector = GetNode<VBoxContainer>("RightSlidePanel/RightScroll/AccordionContainer/InspectorAccordion/ContentInspector");
			StyleAccordionHeader(_btnHeaderInspector);
			SetupAccordion(_btnHeaderInspector, _contentInspector, TranslationServer.Translate("Selected Object Inspector"));

			BuildPlacementAccordion();
		}



		private void BuildPlacementAccordion()
		{
			_placementPanel = new PlacementSettingsPanel();
			_placementPanel.Build(new PlacementSettingsHooks(
				this, StyleAccordionHeader, SetupAccordion, SetupOptionButton, SetupMirrorDropdown,
				UpdateGridSnapExternal, () =>
				{
					if (GameContext.Host != null) UpdateSidebarMorph(GameContext.Host.ActiveEditorTool);
				}));

			WirePlacementSliderEvents();
		}


		private void WirePlacementSliderEvents()
		{
			_placementPanel.DensitySlider.ValueChanged += (val) =>
			{
				float fVal = (float)val;
				_placementPanel.DensityValue.Text = fVal.ToString("F0");
				if (GameContext.Host != null) GameContext.Host.EditorClumpCount = fVal;
			};
			_placementPanel.DensitySlider.DragStarted += () => _isDraggingSlider = true;
			_placementPanel.DensitySlider.DragEnded += (valueChanged) => _isDraggingSlider = false;

			_placementPanel.ScaleVarSlider.ValueChanged += (val) =>
			{
				float fVal = (float)val;
				_placementPanel.ScaleVarValue.Text = fVal.ToString("F2");
				if (GameContext.Host != null) GameContext.Host.EditorClumpScale = fVal;
			};
			_placementPanel.ScaleVarSlider.DragStarted += () => _isDraggingSlider = true;
			_placementPanel.ScaleVarSlider.DragEnded += (valueChanged) => _isDraggingSlider = false;

			_placementPanel.RotateSlider.DragStarted += () => _isDraggingSlider = true;
			_placementPanel.RotateSlider.DragEnded += (valueChanged) => _isDraggingSlider = false;
			_placementPanel.ScaleSlider.DragStarted += () => _isDraggingSlider = true;
			_placementPanel.ScaleSlider.DragEnded += (valueChanged) => _isDraggingSlider = false;
		}


		private void WireEditorControllers()
		{
			TriggerToolSelection(EditorTool.Raise, _toolPanel.RaiseButton);

			_topToolbar.FeedbackLabel.Modulate = new Color(1, 1, 1, 0);
			Input.MouseMode = Input.MouseModeEnum.Visible;


			_entityPaletteController = new MapEditorEntityPaletteController(this, _containerCategorySelector, _toolPanel.AddObjectButton);

			_topBarController = new MapEditorTopBar(_topToolbar.BackToHubButton, _filePanel.PublishButton, _filePanel.SaveButton, _filePanel.LoadButton, _topToolbar.UndoButton, _topToolbar.RedoButton, _topToolbar.EditorsButton, _topToolbar.StatusLabel, _topToolbar.FeedbackLabel);
			_brushSettingsController = new MapEditorBrushSettings(_brushPanel.SizeSlider, _brushPanel.SizeValue, _brushPanel.StrengthSlider, _brushPanel.StrengthValue, _brushPanel.BlockModeCheck, _brushPanel.StepSlider, _brushPanel.StepValue, _brushPanel.HeightSlider, _brushPanel.HeightValue);
			_placementSettingsController = new MapEditorPlacementSettings(_placementPanel.RotateSlider, _placementPanel.RotateValue, _placementPanel.ScaleSlider, _placementPanel.ScaleValue, _placementPanel.RandomRotationCheck, _placementPanel.RandomScaleCheck, _placementPanel.ClumpModeCheck, _placementPanel.DensitySlider, _placementPanel.DensityValue, _placementPanel.ScaleVarSlider, _placementPanel.ScaleVarValue);
			InitializeInspectorPanel();
			_inspectorController = new MapEditorInspector(_inspectorView.Title, _inspectorView.PositionLabel, _inspectorView.RotateLeftButton, _inspectorView.RotateRightButton, _inspectorView.ScaleDownButton, _inspectorView.ScaleUpButton, _inspectorView.ScaleResetButton, _inspectorView.DeleteButton);

			SetupMinimap();

			_minimapController = new MapEditorMinimap(_viewportPanel.MinimapFrame, _viewportPanel.MinimapArea, _viewportPanel.CameraIndicator, this);
			RegenerateMinimap();

			MakeCardDraggable(_accordionFile, _filePanel.Header, _filePanel.Content, "File");
			MakeCardDraggable(_viewportPanel.Accordion, _viewportPanel.Header, _viewportPanel.Content, "Viewport & Navigation");
			MakeCardDraggable(_toolPanel.Accordion, _toolPanel.Header, _toolPanel.Content, "Tool");
			MakeCardDraggable(_brushPanel.Accordion, _brushPanel.Header, _brushPanel.Content, "Global Brush Properties");
			MakeCardDraggable(_accordionToolSettings, _btnHeaderToolSettings, _contentToolSettings, "Tool Settings");
			MakeCardDraggable(_placementPanel.Accordion, _placementPanel.Header, _placementPanel.Content, "Placement Config");
			MakeCardDraggable(_accordionInspector, _btnHeaderInspector, _contentInspector, "Selected Object Inspector");
			if (_waterPanel?.Accordion != null) MakeCardDraggable(_waterPanel.Accordion, _waterPanel.Header, _waterPanel.Content, "Liquid / Water Config");

			RestructurePanelLayouts();

			if (GameContext.Host != null)
			{
				UpdateRotationExternal(GameContext.EditorService.Tools.PlacementRotation);
				UpdateScaleExternal(GameContext.Host.EditorPlacementScale);
				UpdateGridSnapExternal(GameContext.Host.EditorSnapToGrid);
				UpdatePasteRotationExternal(GameContext.EditorService.Tools.PasteRotation);
				UpdatePasteReflectionExternal(GameContext.EditorService.Tools.PasteReflectionMode);
				UpdateMirrorDropdowns();
				UpdateSymmetrySpokesDropdowns();
				UpdatePolarRadialStepButtonText();
				UpdatePolarRingSpacingButtonText();
			}
		}



		private void BuildEditorAccordions()
		{
			_accordionFile = GetNode<VBoxContainer>("LeftSlidePanel/LeftScroll/LeftVBox/FileAccordion");
			BuildFileOperationsPanel();

			BuildViewportNavigationPanel();

			ApplyThemeStyles();
			SetupLightingTuningUI();

			BuildToolAccordion();

			BuildBrushAccordion();

			BuildToolSettingsAccordion();

			BuildInspectorAndPlacementAccordions();
		}


		private void RestoreEditorSessionAfterReady()
		{
			if (!_agreementShownThisSession)
			{
				ShowAgreementModal(() => { CheckPostLaunchPrompts(); });
			}
			else
			{
				CheckPostLaunchPrompts();
			}

			_filePanel.ExpandContentChildren();

			if (GameContext.SessionState.ReturningFromTest)
			{
				ReadMetadataAndRefreshTextures();
				_brushPanel.SizeSlider.Value = GameContext.SessionState.SavedBrushRadius;
				_brushPanel.StrengthSlider.Value = GameContext.SessionState.SavedBrushStrength;

				if (GameContext.SessionState.SavedActiveTool == EditorTool.PlaceUnit ||
				    GameContext.SessionState.SavedActiveTool == EditorTool.PlaceProp ||
				    GameContext.SessionState.SavedActiveTool == EditorTool.PlaceDecal)
				{
					_entityPaletteController?.SelectCategoryItemExternal(GameContext.SessionState.SavedEntityCategory, GameContext.SessionState.SavedActivePlaceId);
				}
				else
				{
					Button? toolBtn = GetButtonForTool(GameContext.SessionState.SavedActiveTool, GameContext.SessionState.SavedActivePlaceId);
					if (toolBtn != null)
					{
						TriggerToolSelection(GameContext.SessionState.SavedActiveTool, toolBtn, GameContext.SessionState.SavedActivePlaceId);
					}
					else
					{
						SwitchModuleView(EditorModule.Terrain);
					}
				}

				GameContext.SessionState.ReturningFromTest = false;
			}
			else
			{
				SwitchModuleView(EditorModule.Terrain);
			}

			StartStartupMapIfPending();
		}


		private void BuildTopLeftToolbar()
		{
			_topToolbar = new TopToolbarPanel();
			// The toolbar is chrome: the panel builds and owns its nodes and receives every action as a hook, so
			// nothing in it reaches back into the HUD.
			_topToolbar.Build(new TopToolbarHooks(
				this, _topLeftBox, StyleMapEditorTopButton, StyleCompactMapEditorTopButton, StyleOptionButtonPopup,
				SetupButton, SetupOptionButton, SetupIconButton, GetFontAwesomeFont, UpdateMapNameHeader,
				() => _mapSettingsDialog?.OpenDialog(), BackToHubAction, ToggleHelpPanelExternal, UndoAction, RedoAction,
				(tool, btn) => TriggerToolSelection(tool, btn), SwitchModuleView,
				() => GameContext.UserInterface?.OpenSettingsOverlay(), TestMapAction,
				OpenTestSettingsDialog, ResetAllPanelPositions, ToggleVSCodeEditor, GenerateVSCodeFilesExternal,
				() => _assetManagerDialog?.OpenDialog(), OpenTemplateManagerDialog, () => OpenInstanceManagerDialog(),
				() => OpenImportFromMapDialog(), OpenUtilitiesDialog));
		}


		private void BuildWindowChrome()
		{
			// Window-level readouts belong with the toolbar row, which already feeds the same top-bar controller.
			_topToolbar.BuildWindowChrome();
		}


		private void BuildFileOperationsPanel()
		{
			_filePanel = new FileOperationsPanel();
			// The random-generation menu is built between the reset button and the settings entries, so the panel
			// calls back for it at that point to keep the content order unchanged.
			_filePanel.Build(new FileOperationsHooks(
				this, StyleAccordionHeader, SetupAccordion, SetupOptionButton,
				LoadMapAction, ImportMapArchiveAction, SaveMapActionExternal, SaveAsMapAction, PublishMapActionExternal,
				ExportMapAction, ResetMapWithConfirmation, SetupRandomGenerationMenu,
				() => _editorSettingsDialog?.OpenDialog(), OpenMapVersionsDialog));
		}


		/// <summary>
		/// Asks before replacing the open map with a blank one. The wording differs for an untitled map because its
		/// contents are what gets replaced, while a named map stays in its folder.
		/// </summary>
		private void ResetMapWithConfirmation()
		{
			string confirmMessage = MapNaming.IsUntitledMapName(GetMapNameFromMetadata())
				? TranslationServer.Translate("Start a new blank map? Everything in the untitled map is replaced, and its last state is kept in MAP VERSIONS.")
				: TranslationServer.Translate("Start a new blank map? The map you have open stays in its folder and can be opened again later.");

			ShowConfirmationDialog(confirmMessage, () => ResetToBlankMapView());
		}


		private void BuildViewportNavigationPanel()
		{
			_viewportPanel = new ViewportPanel();
			// The panel builds the accordion, both toolbar popups, the minimap references, the measurement readout and
			// the polar controls; the camera popup, its initial state and the map-settings dialog follow here because
			// they are editor state rather than viewport nodes.
			_viewportPanel.Build(new ViewportPanelHooks(
				this, StyleAccordionHeader, SetupAccordion, SetupButton, SetupOptionButton, StylePopupMenu,
				OpenOverlayModePopup, OpenEnvironmentPopup, SetupEnvLightingDropdown, SetupEnvWeatherDropdown,
				UpdateGridOverlayExternal,
				UpdateCameraBoundsOverlayExternal, UpdateWireframeOverlayExternal, ShowFeedback, ToggleMeasureTool,
				InitializeEditingDirectory,
				opt => SetupPolarRingSpacingDropdown(opt, "Select interval between concentric polar distance rings (4, 8, 16, 32 tiles)"),
				opt => SetupSpokesDropdown(opt, SetPolarSpokesView, "Select radial spoke count (2, 3, 4, 5, 6, 8, 12, 16)"),
				ResetPivotToMapCenterView));

			BuildCameraPopup();

			var initialCam = GameContext.Host?.MainCamera;
			UpdateFreeCameraExternal(initialCam != null && initialCam.IsFreeCamera);
			UpdateShadowsExternal(GameContext.Host != null && GameContext.EditorService.Tools.DisableShadows);

			_mapSettingsDialog = new MapSettingsDialog(this);

			if (_topBar != null)
			{
				_topBar.CustomMinimumSize = new Vector2(0, 44);
				_topBar.SizeFlagsVertical = SizeFlags.ShrinkCenter;
				_topBar.SizeFlagsHorizontal = SizeFlags.ShrinkCenter;
			}
		}


		/// <summary>
		/// The tape measure button toggles: pressing it while the tool is active clears the measurement and returns to
		/// the tool the user had before, otherwise it activates the tape measure.
		/// </summary>
		private void ToggleMeasureTool(Button measureButton)
		{
			if (GameContext.Host == null) return;

			if (GameContext.Host.ActiveEditorTool == EditorTool.Measure)
			{
				GameContext.Host.ClearMeasureVisuals();
				ClearMeasureTelemetry();
				TriggerToolSelection(GameContext.SessionState.SavedActiveTool != EditorTool.Measure ? GameContext.SessionState.SavedActiveTool : EditorTool.Raise, null);
			}
			else
			{
				TriggerToolSelection(EditorTool.Measure, measureButton);
			}
		}


		private void BuildCameraPopup()
		{
			_cameraPanel = new CameraControlsPanel();
			_cameraPanel.Build(new CameraControlsHooks(
				this, StylePopupButton, SetupButton, OpenCameraPopup, UpdateCameraAngleButtonText, ToggleFreeCamera));
		}


		private void BuildToolAccordion()
		{
			_toolPanel = new ToolAccordionPanel();
			// The per-tool buttons the panel creates come back through its properties, so tool highlighting and the
			// sidebar morph keep reading one set of cards; only selection and the object/coordinate actions are handed in.
			_toolPanel.Build(new ToolAccordionHooks(
				this, StyleAccordionHeader, SetupAccordion, SetupButton, SetupOptionButton, (tool, btn) => TriggerToolSelection(tool, btn),
				GetFontAwesomeFont, () => _entityPaletteController?.TriggerAddObjectMode(), DeleteSelectedObjectAction,
				ShowFeedback, RefreshCoordinateListExternal, OpenInstanceManagerDialog));
		}


		private void BuildBrushAccordion()
		{
			_brushPanel = new BrushSettingsPanel();
			_brushPanel.Build(new BrushSettingsHooks(
				this, StyleAccordionHeader, SetupAccordion, SetupOptionButton, SetupMirrorDropdown, ShowFeedback,
				UpdateBrushShapeExternal, () =>
				{
					UpdateBlockStepVisibility();
					UpdateBrushStrengthVisibility();
					if (GameContext.Host != null) UpdateSidebarMorph(GameContext.Host.ActiveEditorTool);
				},
				dragging => _isDraggingSlider = dragging));

			BuildWaterAccordion();
		}


		private void BuildWaterAccordion()
		{
			_waterPanel = new WaterSettingsPanel();
			_waterPanel.Build(new WaterSettingsHooks(
				this, _brushPanel.Accordion, StyleAccordionHeader, SetupAccordion, _highlightStyle,
				OpenWaterProfileDialog, dragging => _isDraggingSlider = dragging));
		}


		private void BuildToolSettingsAccordion()
		{
			_accordionToolSettings = GetNode<VBoxContainer>("RightSlidePanel/RightScroll/AccordionContainer/ToolSettingsAccordion");
			_btnHeaderToolSettings = GetNode<Button>("RightSlidePanel/RightScroll/AccordionContainer/ToolSettingsAccordion/BtnHeaderToolSettings");
			_contentToolSettings = GetNode<VBoxContainer>("RightSlidePanel/RightScroll/AccordionContainer/ToolSettingsAccordion/ContentToolSettings");
			StyleAccordionHeader(_btnHeaderToolSettings);
			SetupAccordion(_btnHeaderToolSettings, _contentToolSettings, TranslationServer.Translate("Tool Settings"));

			BuildTextureSettingsPanel();

			BuildPathingSettingsPanel();

			BuildEyedropperSettings();

			BuildPasteSettingsPanel();
		}


		private void BuildEyedropperSettings()
		{
			_containerCategorySelector = GetNode<VBoxContainer>("RightSlidePanel/RightScroll/AccordionContainer/ToolSettingsAccordion/ContentToolSettings/ContainerCategorySelector");


			_containerEyedropperSettings = GetNode<VBoxContainer>("RightSlidePanel/RightScroll/AccordionContainer/ToolSettingsAccordion/ContentToolSettings/ContainerEyedropper");
			_optEyedropperMode = GetNode<OptionButton>("RightSlidePanel/RightScroll/AccordionContainer/ToolSettingsAccordion/ContentToolSettings/ContainerEyedropper/OptEyedropperMode");
			_optEyedropperMode.AddItem(TranslationServer.Translate("Auto-Detect Mode"), 0);
			_optEyedropperMode.AddItem(TranslationServer.Translate("Pick 3D Asset"), 1);
			_optEyedropperMode.AddItem(TranslationServer.Translate("Pick Decal"), 2);
			_optEyedropperMode.AddItem(TranslationServer.Translate("Pick Terrain Texture"), 3);
			_optEyedropperMode.AddItem(TranslationServer.Translate("Pick Height"), 4);
			_optEyedropperMode.Selected = 0;
		}


		private void BuildPasteSettingsPanel()
		{
			_pastePanel = new PasteSettingsPanel();
			_pastePanel.Build(new PasteSettingsHooks(
				this, SetupOptionButton, SetupMirrorDropdown, CyclePasteReflection, CyclePasteAnchor,
				UpdateBrushShapeExternal, ShowFeedback, dragging => _isDraggingSlider = dragging));
		}


		private void BuildTextureSettingsPanel()
		{
			_containerTextureSettings = GetNode<VBoxContainer>("RightSlidePanel/RightScroll/AccordionContainer/ToolSettingsAccordion/ContentToolSettings/ContainerTexture");

			var texturePanel = new TextureSettingsPanel(new TextureSettingsHooks(
				(button, text, onClick, fontSize, tooltip) => SetupOptionButton(button, text, onClick, fontSize, tooltip),
				UpdateTextureLabels,
				() => _replaceTextureDialog?.OpenDialog()));
			texturePanel.Build(_containerTextureSettings);

			_lblTerrainTexture = texturePanel.TerrainLabel;
			_lblCliffTexture = texturePanel.CliffLabel;
			ChkApplyGroundTexture = texturePanel.GroundApply;
			ChkApplyCliffTexture = texturePanel.CliffApply;
			RowGroundTexture = texturePanel.GroundRow;
			RowCliffTexture = texturePanel.CliffRow;
			Swatches.Attach(texturePanel.SwatchesGrid!, texturePanel.SwatchesScroll);
			_btnReplaceTexture = texturePanel.ReplaceButton;

			SetupTextureSwatches(true);
		}






		private void BuildPathingSettingsPanel()
		{
			_containerPathingSettings = GetNode<VBoxContainer>("RightSlidePanel/RightScroll/AccordionContainer/ToolSettingsAccordion/ContentToolSettings/ContainerPathing");

			_pathingPanelController = new MapEditorPathingPanel();
			_pathingPanelController.Build(_containerPathingSettings, StyleCheckBoxRow);

			_chkShallowWater = _pathingPanelController.ShallowWater;
			_chkDeepWater = _pathingPanelController.DeepWater;
			_chkFlying = _pathingPanelController.Flying;
			_chkGround = _pathingPanelController.Ground;
			_chkBuildable = _pathingPanelController.Buildable;
			_optPathingMode = _pathingPanelController.Mode;
		}






		private void StyleAccordionHeader(Button btn) => MapEditorStyle.AccordionHeader(btn, GetFontAwesomeFont());

		public FontVariation? GetFontAwesomeFont()
		{
			if (_faFontVariation == null)
			{
				try
				{
					var faFont = GD.Load<FontFile>("res://Assets/UI/fa-solid-900.ttf");
					if (faFont != null)
					{
						_faFontVariation = new FontVariation();
						_faFontVariation.Fallbacks = new Array<Font> { faFont };
					}
				}
				catch
				{
					_faFontVariation = null;
				}
			}

			return _faFontVariation;
		}

		private void StyleIconButton(Button btn, string iconText, string tooltipText) => MapEditorStyle.IconButton(btn, iconText, tooltipText, GetFontAwesomeFont());


		private void RestructurePanelLayouts()
		{
			StyleFileAccordionOperations();

			StyleViewportNavigationBar();

			StyleToolPanelContainers();

			StyleBrushAccordionControls();

			StyleTexturePaletteRows();

			if (_containerPathingSettings != null)
			{
				StyleSubContainer(_containerPathingSettings, "Pathing Properties");
			}

			StylePasteAndPlacementSettings();

			RestyleOptionButtonContents();
		}








		private void StylePasteAndPlacementSettings()
		{
			_pastePanel.StyleOptions();

			_placementPanel.StyleControls();

			if (_containerEyedropperSettings != null)
			{
				MapEditorStyle.SubContainer(_containerEyedropperSettings, "Eyedropper Settings");
			}
		}

		private void RestyleOptionButtonContents()
		{
			Control[] allOptionContents = new Control[]
			{
				_filePanel.Content, _viewportPanel.Content, _toolPanel.Content,
				_brushPanel.Content, _contentToolSettings, _placementPanel.Content, _contentInspector, _contentLightingTuning
			};
			foreach (var content in allOptionContents)
			{
				MapEditorStyle.OptionButtonsInContainer(content, GetFontAwesomeFont());
			}
		}


		private void StyleBrushAccordionControls() => _brushPanel.StyleControls();


		private void StyleToolPanelContainers() => _toolPanel.StyleContainers(_contentInspector);


		private void StyleFileAccordionOperations() => _filePanel.StyleOperations();


		private void StyleViewportNavigationBar()
		{
			var polarRow = _viewportPanel.StyleNavigationBar(GetFontAwesomeFont(), _cameraPanel.ToggleButton);
			if (polarRow != null) _rowPolarConfig = polarRow;
			if (_rowPolarConfig == null) return;

			bool isSymmetryActive = GameContext.Host != null &&
			                        (GameContext.Host.EditorMirrorMode != MirrorMode.None ||
			                         GameContext.EditorService.Tools.GridMode == GridOverlayMode.Polar ||
			                         GameContext.EditorService.Tools.GridMode == GridOverlayMode.Both);
			_rowPolarConfig.Visible = isSymmetryActive;
		}


		private void StyleTexturePaletteRows()
		{
			if (_containerTextureSettings == null) return;

			MapEditorStyle.SubContainer(_containerTextureSettings, "Texture Palette & Settings");

			if (RowGroundTexture != null)
			{
				MapEditorStyle.RowBackground(RowGroundTexture,
					new Color(0.12f, 0.16f, 0.20f, 0.85f), new Color(0.20f, 0.65f, 0.95f, 0.85f));
			}

			if (RowCliffTexture != null)
			{
				MapEditorStyle.RowBackground(RowCliffTexture,
					new Color(0.18f, 0.14f, 0.10f, 0.85f), new Color(0.95f, 0.55f, 0.15f, 0.85f));
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



		private void RotateSelectedObject(float angleDelta)
		{
			if (GameContext.Host == null) return;

			var (success, newRotY) = GameContext.Host.RotateSelectedObject(angleDelta);
			if (success)
			{
				int selectedCount = GameContext.Host.SelectedEditorObjectCount;
				UpdateSelectedObjectInfo();
				ShowFeedback(selectedCount > 1
					? string.Format(TranslationServer.Translate("{0} Objects Rotated to {1}°"), selectedCount, newRotY)
					: string.Format(TranslationServer.Translate("Rotated Object to {0}°"), newRotY));
			}
		}

		private void ScaleSelectedObject(float scaleDelta)
		{
			if (GameContext.Host == null) return;

			var (success, newScaleVal) = GameContext.Host.ScaleSelectedObject(scaleDelta);
			if (success)
			{
				int selectedCount = GameContext.Host.SelectedEditorObjectCount;
				UpdateSelectedObjectInfo();
				ShowFeedback(selectedCount > 1
					? string.Format(TranslationServer.Translate("{0} Objects Scaled to {1:F1}x"), selectedCount, newScaleVal)
					: string.Format(TranslationServer.Translate("Scaled Object to {0:F1}x"), newScaleVal));
			}
		}

		private void ResetScaleSelectedObject()
		{
			if (GameContext.Host == null) return;

			var success = GameContext.Host.ResetScaleSelectedObject();
			if (success)
			{
				int selectedCount = GameContext.Host.SelectedEditorObjectCount;
				UpdateSelectedObjectInfo();
				ShowFeedback(selectedCount > 1
					? string.Format(TranslationServer.Translate("{0} Objects Reset to 1.0x"), selectedCount)
					: TranslationServer.Translate("Reset Object scale to 1.0x"));
			}
		}

		public void LocateSelectedObjectAction()
		{
			if (GameContext.Host == null) return;
			var selected = GameContext.Host.SelectedEditorObject;
			if (GodotObject.IsInstanceValid(selected) && selected is Node3D node3D)
			{
				GameContext.Host.MainCamera?.FocusOnPosition(node3D.Position);
				ShowFeedback(string.Format(TranslationServer.Translate("Focused camera on {0}"), selected.Name));
			}
			else
			{
				ShowFeedback(TranslationServer.Translate("No object selected to locate"));
			}
		}

		private ScaleMapDialog? _scaleDialog;

		private ScaleMapDialog ScaleDialog => _scaleDialog ??= new ScaleMapDialog(new ScaleDialogHooks(
			this,
			(button, text, onClick, fontSize, tooltip) => SetupButton(button, text, onClick, fontSize, tooltip),
			(button, text, onClick, fontSize, tooltip) => SetupOptionButton(button, text, onClick, fontSize, tooltip)));

		public void OpenScaleMapDialog() => ScaleDialog.Open();

		private void CloseScaleMapDialog() => ScaleDialog.Close();

		private void SetupButton(Button btn, string text, Action? onClick, int fontSize = 13, string tooltip = "") =>
			MapEditorStyle.DialogButton(btn, text, onClick, fontSize, tooltip, GetFontAwesomeFont());

		private void SelectCliffTexture(int index)
		{
			if (GameContext.Host != null)
			{
				GameContext.EditorService.SelectCliffTexture(index);
				if (!IsSwatchCompatibleTool(GameContext.Host.ActiveEditorTool))
				{
					TriggerToolSelection(EditorTool.PaintTexture, _toolPanel.TextureBrushButton);
				}

				UpdateTextureLabels();

				string name = TranslationServer.Translate(GameContext.EditorService.Swatches.DisplayNames[index]);
				ShowFeedback(string.Format(TranslationServer.Translate("Selected Cliff Face: {0}"), name));
			}
		}

		private static bool IsSwatchCompatibleTool(EditorTool tool) =>
			EditorToolCatalog.IsSwatchCompatibleTool(tool);

		public void UpdateTextureLabels()
		{
			if (GameContext.Host == null) return;

			EnsureSwatchDataCurrent();

			var (terrainName, cliffName) = GameContext.EditorService.GetTextureNamesDomain();
			int terrainIdx = GameContext.EditorService.Tools.PaintTextureIndex;
			int cliffIdx = GameContext.EditorService.Tools.CliffPaintTextureIndex;

			if (_lblTerrainTexture != null) _lblTerrainTexture.Text = TranslationServer.Translate(terrainName);
			if (_lblCliffTexture != null) _lblCliffTexture.Text = TranslationServer.Translate(cliffName);

			Button? terrainSwatch = Swatches.GetButton(terrainIdx);
			Button? cliffSwatch = Swatches.GetButton(cliffIdx);

			HighlightSwatch(terrainSwatch);
			HighlightCliffSwatch(cliffSwatch);

			if (_btnReplaceTexture != null)
			{
				_btnReplaceTexture.Text = $"{UnicodeIcons.UPLOAD} {TranslationServer.Translate("REPLACE TEXTURE")}";
				_btnReplaceTexture.TooltipText = TranslationServer.Translate("Replace all instances of a texture with another texture across the map");
			}
		}

		private void UpdateBrushSizeVisibility()
		{
			if (_brushPanel?.SizeBox == null || GameContext.Host == null) return;

			var tool = GameContext.Host.ActiveEditorTool;
			_brushPanel.SizeBox.Visible = tool != EditorTool.FloodFill && tool != EditorTool.FloodFillPathing;
		}

		private void UpdateBrushShapeVisibility()
		{
			if (_brushPanel?.ShapeButton == null || GameContext.Host == null) return;

			var tool = GameContext.Host.ActiveEditorTool;
			_brushPanel.ShapeButton.Visible = tool != EditorTool.FloodFill && tool != EditorTool.FloodFillPathing;
		}

		private void UpdateBrushStrengthVisibility()
		{
			if (_brushPanel?.StrengthSlider != null && _brushPanel.StrengthSlider.GetParent() is Control strengthParent)
			{
				if (GameContext.Host == null) return;
				var tool = GameContext.Host.ActiveEditorTool;
				bool blockModeEnabled = (_brushPanel?.BlockModeCheck != null && _brushPanel.BlockModeCheck.ButtonPressed);

				bool isClumpPlacement = tool == EditorTool.PlacePropClump ||
				                        ((tool == EditorTool.PlaceUnit || tool == EditorTool.PlaceProp || tool == EditorTool.PlaceDecal) &&
				                         (_placementPanel?.ClumpModeCheck != null && _placementPanel.ClumpModeCheck.ButtonPressed));

				if (tool == EditorTool.Raise || tool == EditorTool.Lower)
				{
					strengthParent.Visible = !blockModeEnabled;
				}
				else if (tool == EditorTool.Height)
				{
					strengthParent.Visible = false;
				}
				else
				{
					strengthParent.Visible = (tool != EditorTool.PaintPathing &&
					                          tool != EditorTool.FloodFillPathing &&
					                          !isClumpPlacement &&
					                          tool != EditorTool.Plateau &&
					                          tool != EditorTool.Ramp);
				}
			}
		}

		private void InitializeInspectorPanel()
		{
			_inspectorView = new InspectorPanel();
			// Dialog creation, the water swatch refresh and the preference pass sit between the inspector's rows and
			// its action buttons, so they arrive as hooks rather than moving: dialogs belong to the whole editor.
			_inspectorView.Build(new InspectorPanelHooks(
				this, SetupButton, GetFontAwesomeFont,
				RotateSelectedObjectAction, ScaleSelectedObjectAction, LocateSelectedObjectAction, DeleteSelectedObjectAction,
				CreateInspectorDialogs, RefreshWaterSwatches,
				() => ApplyEditorPreferences(EditorSettingsDialog.CurrentSettings),
				EditSelectedTemplate, EditSelectedVfx, () => IsUpdatingInspectorUI));
		}


		/// <summary>Opens the visual template editor for the object selected in the inspector.</summary>
		private void EditSelectedTemplate()
		{
			if (GameContext.Host != null && GodotObject.IsInstanceValid(GameContext.Host.SelectedEditorObject))
			{
				_entityVisualEditDialog?.OpenForObject(GameContext.Host.SelectedEditorObject);
			}
		}


		/// <summary>
		/// Opens the VFX studio for the selected procedural VFX instance. When its VFX id has a template the studio
		/// starts from a clone, so edits produce a new configuration instead of mutating the shared template.
		/// </summary>
		private void EditSelectedVfx()
		{
			if (GameContext.Host == null || !GodotObject.IsInstanceValid(GameContext.Host.SelectedEditorObject) || GameContext.Host.SelectedEditorObject is not ProceduralVfxInstance3D vfx) return;

			VfxAttachmentConfig editConfig = vfx.Config;
			if (GameContext.RegistryService.TryGetVfxConfig(editConfig?.VfxId, out var templateConfig))
			{
				editConfig = templateConfig.Clone();
			}

			OpenVfxStudioDialog(editConfig, (newCfg) => { vfx.UpdateConfig(newCfg); });
		}


		private void CreateInspectorDialogs()
		{
			_entityVisualEditDialog = new EntityVisualEditDialog(this);
			_animationPreviewDialog = new AnimationPreviewDialog(this);
			_weaponVfxDialog = new WeaponVfxDialog(this);
			_modelPickerDialog = new ModelPickerDialog(this);
			_abilityVfxDialog = new AbilityVfxDialog(this);
			_assetManagerDialog = new AssetManagerDialog(this);
			_importFromMapDialog = new ImportFromMapDialog(this);
			_templateManagerDialog = new TemplateManagerDialog(this);
			_instanceManagerDialog = new InstanceManagerDialog(this);
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
			_utilitiesDialog = new UtilitiesDialog(this);
			_testSettingsDialog = new TestSettingsDialog(this);
		}



		public void OpenWeaponVfxDialog(string weaponId, WeaponMetadata? weapon, Action<WeaponMetadata>? onApplied = null)
		{
			if (_weaponVfxDialog == null)
			{
				_weaponVfxDialog = new WeaponVfxDialog(this);
			}

			_weaponVfxDialog.OpenForWeapon(weaponId, weapon, onApplied);
		}

		public void OpenVfxStudioDialog(VfxAttachmentConfig? initialConfig = null, Action<VfxAttachmentConfig>? onApplied = null)
		{
			if (_vfxStudioDialog == null)
			{
				_vfxStudioDialog = new VfxStudioDialog(this);
			}

			_vfxStudioDialog.OpenForConfig(initialConfig, onApplied);
		}

		public void OpenObjectAttachmentDialog(
			string? unitId = null,
			string? attachmentId = null,
			string hand = "RightHand",
			Node3D? sourceModel = null,
			Action<HandAttachmentOrientation?>? onApplied = null)
		{
			if (_objectAttachmentDialog == null)
			{
				_objectAttachmentDialog = new ObjectAttachmentDialog(this);
			}

			_objectAttachmentDialog.OpenForUnitAndAttachment(unitId, attachmentId, hand, sourceModel, onApplied);
		}

		public void RemoveUnitObjectAttachment(string unitId, string socket, string attachmentId, string? parentAttachmentId = null)
		{
			try
			{
				if (string.IsNullOrEmpty(unitId) || string.IsNullOrEmpty(attachmentId)) return;

				string regKey = GameContext.EditorService.RemoveUnitObjectAttachmentDomain(unitId, socket, attachmentId, parentAttachmentId);

				if (GameContext.Host != null)
				{
					if (GameContext.Host.AllUnits != null)
					{
						foreach (var u in GameContext.Host.AllUnits)
						{
							if (u != null && (u.UnitId == unitId || u.UnitId == regKey || Path.GetFileNameWithoutExtension(u.UnitId ?? "").Equals(Path.GetFileNameWithoutExtension(unitId), StringComparison.OrdinalIgnoreCase)))
							{
								u.ApplyAllConfiguredAttachments();
							}
						}
					}

					if (GameContext.Host.SelectedEditorObject is Unit3D selUnit)
					{
						if (selUnit.UnitId == unitId || selUnit.UnitId == regKey || Path.GetFileNameWithoutExtension(selUnit.UnitId ?? "").Equals(Path.GetFileNameWithoutExtension(unitId), StringComparison.OrdinalIgnoreCase))
						{
							selUnit.ApplyAllConfiguredAttachments();
						}
					}
				}
			}
			catch (Exception ex)
			{
				GameLog.LogError($"[MapEditorHUD] RemoveUnitObjectAttachment error: {ex.Message}");
			}
		}

		public void RestoreUnitObjectAttachments(string targetId, UnitObjectAttachments? snapshot)
		{
			try
			{
				if (string.IsNullOrEmpty(targetId)) return;

				string regKey = GameContext.EditorService.RestoreUnitObjectAttachmentsDomain(targetId, snapshot);

				if (GameContext.Host != null)
				{
					if (GameContext.Host.AllUnits != null)
					{
						foreach (var u in GameContext.Host.AllUnits)
						{
							if (u != null && (u.UnitId == targetId || u.UnitId == regKey || Path.GetFileNameWithoutExtension(u.UnitId ?? "").Equals(Path.GetFileNameWithoutExtension(targetId), StringComparison.OrdinalIgnoreCase)))
							{
								u.ApplyAllConfiguredAttachments();
							}
						}
					}

					if (GameContext.Host.SelectedEditorObject is Unit3D selUnit)
					{
						if (selUnit.UnitId == targetId || selUnit.UnitId == regKey || Path.GetFileNameWithoutExtension(selUnit.UnitId ?? "").Equals(Path.GetFileNameWithoutExtension(targetId), StringComparison.OrdinalIgnoreCase))
						{
							selUnit.ApplyAllConfiguredAttachments();
						}
					}
				}
			}
			catch (Exception ex)
			{
				GameLog.LogError($"[MapEditorHUD] RestoreUnitObjectAttachments error: {ex.Message}");
			}
		}

		public void RestoreUnitPseudoSockets(string targetId, List<PseudoSocketAnchor>? snapshot)
		{
			try
			{
				if (string.IsNullOrEmpty(targetId)) return;

				string regKey = GameContext.EditorService.RestoreUnitPseudoSocketsDomain(targetId, snapshot);

				if (GameContext.Host != null)
				{
					if (GameContext.Host.AllUnits != null)
					{
						foreach (var u in GameContext.Host.AllUnits)
						{
							if (u != null && (u.UnitId == targetId || u.UnitId == regKey || Path.GetFileNameWithoutExtension(u.UnitId ?? "").Equals(Path.GetFileNameWithoutExtension(targetId), StringComparison.OrdinalIgnoreCase)))
							{
								u.ApplyAllConfiguredAttachments();
							}
						}
					}

					if (GameContext.Host.SelectedEditorObject is Unit3D selUnit)
					{
						if (selUnit.UnitId == targetId || selUnit.UnitId == regKey || Path.GetFileNameWithoutExtension(selUnit.UnitId ?? "").Equals(Path.GetFileNameWithoutExtension(targetId), StringComparison.OrdinalIgnoreCase))
						{
							selUnit.ApplyAllConfiguredAttachments();
						}
					}
				}
			}
			catch (Exception ex)
			{
				GameLog.LogError($"[MapEditorHUD] RestoreUnitPseudoSockets error: {ex.Message}");
			}
		}

		public void OpenShaderEditorDialog(string shaderKey = "", Action<CustomShaderConfig>? onSaved = null)
		{
			if (_shaderEditorDialog == null)
			{
				_shaderEditorDialog = new ShaderEditorDialog(this);
			}

			_shaderEditorDialog.OpenForShader(shaderKey, onSaved);
		}

		public void OpenProceduralAnimationStudioDialog(ProceduralAnimationConfig? initialConfig = null, Action<ProceduralAnimationConfig>? onApplied = null, string? previewModelKey = null)
		{
			if (_proceduralAnimationStudioDialog == null)
			{
				_proceduralAnimationStudioDialog = new ProceduralAnimationStudioDialog(this);
			}

			_proceduralAnimationStudioDialog.OpenForConfig(initialConfig, onApplied, previewModelKey);
		}

		public void OpenModelPickerDialog(string entityId, string fieldName, string domain, string currentPath, Action<string>? onApplied = null)
		{
			if (_modelPickerDialog == null)
			{
				_modelPickerDialog = new ModelPickerDialog(this);
			}

			_modelPickerDialog.OpenForEntity(entityId, fieldName, domain, currentPath, onApplied);
		}

		public void OpenAnimationPreviewDialog(string unitId, string? modelPath = null)
		{
			if (_animationPreviewDialog == null)
			{
				_animationPreviewDialog = new AnimationPreviewDialog(this);
			}

			_animationPreviewDialog.OpenForUnitId(unitId, modelPath);
		}

		// The test button's handler, and the confirmation dialog's callback, both have to be void, and both awaited
		// ProceedToTestMap with no catch: a fault launching the test map was unobserved, so the editor just stayed put.
		private async void TestMapAction()
		{
			try
			{
				await TestMapCoreAsync();
			}
			catch (Exception exception)
			{
				GameLog.LogError($"[MapEditorHUD] Starting the map test failed: {exception}");
			}
		}

		private async Task TestMapCoreAsync()
		{
			if (GameContext.Host == null) return;

			if (GameContext.Host.AllUnits.Count == 0)
			{
				ShowConfirmationDialog(
					"Warning: You have not placed any units, you won't see anything due to Shroud.",
					async () =>
					{
						try
						{
							await ProceedToTestMap();
						}
						catch (Exception exception)
						{
							GameLog.LogError($"[MapEditorHUD] Starting the map test failed: {exception}");
						}
					},
					"Okay",
					"Cancel"
				);
			}
			else
			{
				await ProceedToTestMap();
			}
		}

		public void OpenNoiseTextureDialog(Action<string>? onSaved = null)
		{
			_noiseTextureDialog?.OpenWithCallback(onSaved);
		}

		public void OpenTemplateManagerDialog()
		{
			if (_templateManagerDialog == null)
			{
				_templateManagerDialog = new TemplateManagerDialog(this);
			}

			_templateManagerDialog.OpenDialog();
		}

		public void OpenEditorSettingsDialog()
		{
			_editorSettingsDialog?.OpenDialog();
		}

		public void OpenMapVersionsDialog()
		{
			if (_mapVersionsDialog == null || !GodotObject.IsInstanceValid(_mapVersionsDialog))
			{
				_mapVersionsDialog = new MapVersionsDialog(this);
			}

			_mapVersionsDialog.OpenDialog();
		}

		public void OpenMapHistoryStoresDialog()
		{
			if (_mapHistoryStoresDialog == null || !GodotObject.IsInstanceValid(_mapHistoryStoresDialog))
			{
				_mapHistoryStoresDialog = new MapHistoryStoresDialog(this);
			}

			_mapHistoryStoresDialog.OpenDialog();
		}

		public void NotifyHistoryRestoreApplied(bool versionRecorded)
		{
			_hasChangesSinceLastVersion = !versionRecorded;
			EditorWorkspaceWatcher.UpdateWatchedFileTimestamps();
		}

		public void MarkScriptSourcesChangedByRestore()
		{
			SetWasmConsoleStatus(TranslationServer.Translate("Script sources changed — publish or test the map to compile them again."), UIStyle.ColorCyanGlow);
			ShowFeedback(TranslationServer.Translate("The restored version changed the map script. Publish or test the map to compile it again."));
		}

		public void OpenUtilitiesDialog()
		{
			if (_utilitiesDialog == null)
			{
				_utilitiesDialog = new UtilitiesDialog(this);
			}

			_utilitiesDialog.OpenDialog();
		}

		public void RefreshAllInspectors()
		{
			_templateManagerDialog?.RefreshObjectList();
			_templateManagerDialog?.ReloadCurrentPreview();
			_assetManagerDialog?.RefreshAssetListAndSelect("");
		}

		private void RefreshSkyboxList()
		{
			_mapSettingsDialog?.RefreshSkyboxList();
		}

		private void UpdateMetadataJsonAsset(string category, string fileName, string blake3Hash, string? subCategory = null, int columns = 0, int rows = 0, int targetSlot = -1)
		{
			GameContext.EditorService.UpdateMetadataJsonAssetDomain(category, fileName, blake3Hash, subCategory, columns, rows, targetSlot);
		}

		public void ConvertRawTextureDirect(string rawPngPath, string outputRtexPath, string swatchName)
		{
			try
			{
				if (GameContext.Host != null && GameContext.Host.GroundTerrain != null)
				{
					GameContext.Host.GroundTerrain.ProcessAndSaveRawTexture(rawPngPath, outputRtexPath);
				}

				if (File.Exists(outputRtexPath))
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
				GameLog.LogError($"ConvertRawTextureDirect error: {ex.Message}");
				ShowFeedback($"Failed to import texture: {ex.Message}");
			}
		}

		public ModelOptimizerService.OptimizationResult OptimizeAndImportGlbDirect(
			byte[] glbBytes,
			int maxTextureResolution = 1024,
			float creaseAngleDegrees = GlbMeshSmoother.DefaultCreaseAngleDegrees,
			float allowedPixelError = 1.5f,
			bool forceReDecimate = false)
		{
			return GameContext.ModelOptimizerService.OptimizeAndImportGlbDirect(glbBytes, maxTextureResolution, creaseAngleDegrees, allowedPixelError, forceReDecimate);
		}

		public void ImportGlbAssetFromExtension(string sourceFilePath, string category)
		{
			try
			{
				string wsPath = GameContext.EditorService._editingDirectory;

				var result = GameContext.EditorService.ImportGlbAssetFromExtensionDomain(sourceFilePath, category, wsPath, (cat, fName, hash, sub) => UpdateMetadataJsonAsset(cat, fName, hash, subCategory: sub));

				if (!result.Success)
				{
					ShowFeedback(string.Format(TranslationServer.Translate("Failed to import GLB asset: {0}"), result.ErrorMessage));
					return;
				}

				_entityPaletteController?.SelectCategory(_entityPaletteController.CurrentCategory, triggerAddObject: false);

				if (result.ExtractedAnimationFiles != null && result.ExtractedAnimationFiles.Count > 0)
				{
					ShowFeedback(string.Format(TranslationServer.Translate("Imported GLB {0} and extracted {1} .ranim animation(s)"), result.FileName, result.ExtractedAnimationFiles.Count));
				}
				else
				{
					ShowFeedback(string.Format(TranslationServer.Translate("Imported GLB asset {0} ({1})"), result.FileName, category));
				}
			}
			catch (Exception ex)
			{
				GameLog.LogError($"ImportGlbAssetFromExtension error: {ex.Message}");
				ShowFeedback(string.Format(TranslationServer.Translate("Failed to import GLB asset: {0}"), ex.Message));
			}
		}

		private HSlider CreateSliderRow(VBoxContainer parent, string labelText, float min, float max, float step, float initialVal, Action<float> onChanged, string format = "0.0#", float labelWidth = 70f)
			=> MapEditorStyle.SliderRow(parent, labelText, min, max, step, initialVal, onChanged, drag => _isDraggingSlider = drag, format, labelWidth);

		public void RefreshWaterSwatches()
		{
			_waterPanel?.RefreshSwatches();
		}

		public void OpenWaterProfileDialog()
		{
			var profilesList = new List<WaterProfileSaveData>();
			if (GameContext.Terrain != null)
			{
				var profDict = GameContext.Terrain.GetWaterProfiles();
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
			if (GameContext.Host != null)
			{
				byte currentProf = GameContext.Host.ActiveWaterProfileIndex;
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

			List<EnvironmentPresetConfig>? presetsList = null;
			string? defaultPreset = null;
			string wsPath = MapWorkspaceService.GetActiveWorkspacePath();
			if (GameContext.MetadataService.TryLoadMetadata(wsPath, out var meta) && meta != null)
			{
				if (meta.CustomEnvironmentPresets != null && meta.CustomEnvironmentPresets.Count > 0)
				{
					presetsList = meta.CustomEnvironmentPresets;
				}

				defaultPreset = meta.DefaultEnvironmentPreset;
			}

			if (presetsList == null || presetsList.Count == 0)
			{
				presetsList = GameContext.Host != null
					? GameContext.EnvironmentService.GetPresets().ToList()
					: EnvironmentPresetConfig.CreateDefaultPresets();
			}

			string currentId = (GameContext.Host != null ? GameContext.EnvironmentService.GetCurrentPresetId() : null) ?? defaultPreset ?? "day";
			_environmentConfigDialog?.OpenWithPresets(presetsList, currentId, defaultPreset);
		}
	}
}