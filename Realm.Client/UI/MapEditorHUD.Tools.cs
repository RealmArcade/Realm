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
			_highlightStyle = UIStyle.CreateCardPlate(new Color(0.35f, 0.28f, 0.18f, 0.95f), UIStyle.ColorGold, 2, 6, 4, 4, 4, 4);

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

				ApplyThemeStyles();

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
				    GameContext.SessionState.SavedActiveTool == EditorTool.PlaceDecal ||
				    GameContext.SessionState.SavedActiveTool == EditorTool.PlaceItem)
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
				() => OpenImportFromMapDialog(), OpenCompositeMapDialog, OpenUtilitiesDialog));
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
				ShowFeedback, RefreshCoordinateListExternal, OpenInstanceManagerDialog, OpenSpawnAnchorDialog));
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










		private ScaleMapDialog? _scaleDialog;

		private ScaleMapDialog ScaleDialog => _scaleDialog ??= new ScaleMapDialog(new ScaleDialogHooks(
			this,
			(button, text, onClick, fontSize, tooltip) => SetupButton(button, text, onClick, fontSize, tooltip),
			(button, text, onClick, fontSize, tooltip) => SetupOptionButton(button, text, onClick, fontSize, tooltip)));

		public void OpenScaleMapDialog() => ScaleDialog.Open();

		private void CloseScaleMapDialog() => ScaleDialog.Close();

		private void SetupButton(Button btn, string text, Action? onClick, int fontSize = 13, string tooltip = "") =>
			MapEditorStyle.DialogButton(btn, text, onClick, fontSize, tooltip, GetFontAwesomeFont());

		/// <summary>
		/// Picks the cliff texture. The clicked swatch is passed through when the palette is the source of the choice, so
		/// the cliff ring settles on that button even before the label pass recomputes it from the index.
		/// </summary>
		private void SelectCliffTexture(int index, Button? clickedSwatch = null)
		{
			if (GameContext.Host != null)
			{
				GameContext.EditorService.SelectCliffTexture(index);
				HighlightCliffSwatch(clickedSwatch ?? Swatches.GetButton(index));

				if (!IsSwatchCompatibleTool(GameContext.Host.ActiveEditorTool))
				{
					TriggerToolSelection(EditorTool.PaintTexture, _toolPanel.TextureBrushButton);
				}

				UpdateTextureLabels();

				string name = TranslationServer.Translate(GameContext.EditorService.Swatches.DisplayNames[index]);
				ShowFeedback(string.Format(TranslationServer.Translate("Selected Cliff Face: {0}"), name));
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


		/// <summary>
		/// Opens the item template editor, which is where a selected drop's ground model, icon, name and grants live. The
		/// visual inspector has no item controls, so an item is never handed to it.
		/// </summary>
		/// <param name="templateId">The item template the selected drop is spawned from.</param>
		public void OpenItemTemplateEditor(string templateId)
		{
			_templateManagerDialog?.OpenItemEditor(templateId);
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