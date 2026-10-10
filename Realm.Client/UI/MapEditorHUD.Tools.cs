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

namespace Realm.Client.UI
{
    public partial class MapEditorHUD
    {

        private void InitCoreServices()
        {
            Instance = this;
            MetadataService.Instance.MetadataSaved += OnMetadataSaved;
            _editorService = ServiceLocator.TryGet<EditorService>();
            _mapUpgradeService = ServiceLocator.TryGet<MapUpgradeService>();
            UpdateFPSVisibility();
            _tempWorkspacePath = Realm.Client.Services.MapWorkspaceService.GetDefaultWorkspaceGlobalPath();

            _camera3D = (Realm.Client.Core.GameHost.Instance?.MainCamera);

            HookSliders(this);
            ChildEnteredTree += (node) => HookSliders(node);
        }

        private void InitUIHooksAndStyles()
        {
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
                frameRect.Visible = !Realm.Client.UI.MapEditor.EditorSettingsDialog.CurrentSettings.HideChromeBorderOverlay;
                AddChild(frameRect);
                MoveChild(frameRect, 0);
                _screenFrameRect = frameRect;
            }
        }

        private void InitBasicPanels()
        {
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

                _btnBackToHub = GetNode<Button>("TopLeftBox/BtnBack");
                SetupButton(_btnBackToHub, $"{UnicodeIcons.SIGN_OUT} BACK TO HUB", () => BackToHubAction(), 13, "Exit editor and return to game lobby");
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
                SetupButton(btnHelp, $"{UnicodeIcons.HELP} HELP", () => ToggleHelpPanelExternal(), 13, "Toggle the hotkeys and editor guide overlay (H)");
                StyleMapEditorTopButton(btnHelp);

                _popupEditors = new PopupPanel();
                _popupEditors.Name = "PopupEditors";
                var editorsPopupStyle = new StyleBoxFlat();
                editorsPopupStyle.BgColor = new Color(0.14f, 0.13f, 0.11f, 0.98f);
                editorsPopupStyle.BorderColor = UIStyle.ColorGold;
                editorsPopupStyle.SetBorderWidthAll(1);
                editorsPopupStyle.CornerRadiusTopLeft = 4;
                editorsPopupStyle.CornerRadiusTopRight = 4;
                editorsPopupStyle.CornerRadiusBottomLeft = 4;
                editorsPopupStyle.CornerRadiusBottomRight = 4;
                editorsPopupStyle.ContentMarginLeft = 10;
                editorsPopupStyle.ContentMarginRight = 10;
                editorsPopupStyle.ContentMarginTop = 10;
                editorsPopupStyle.ContentMarginBottom = 10;
                _popupEditors.AddThemeStyleboxOverride("panel", editorsPopupStyle);

                var editorsVBox = new VBoxContainer();
                editorsVBox.AddThemeConstantOverride("separation", 6);
                editorsVBox.CustomMinimumSize = new Vector2(170, 0);
                _popupEditors.AddChild(editorsVBox);
                AddChild(_popupEditors);

                _btnVSCode = new Button();
                _btnVSCode.Name = "BtnVSCode";
                SetupOptionButton(_btnVSCode, $"{UnicodeIcons.CODE} CODE & DATA", () => ToggleVSCodeEditor(), 13, "Toggle the embedded VSCode editor (Right-click or Middle-click: DevTools)");
                _btnVSCode.Pressed += () => _popupEditors.Hide();

                if (OperatingSystem.IsWindows())
                {
                    GenerateVSCodeFilesExternal();
                    Realm.Client.VSCodeManager.Instance.Initialize(this);
                    _btnVSCode.GuiInput += (@event) =>
                    {
                        if (OperatingSystem.IsWindows() && @event is InputEventMouseButton mouseButton && mouseButton.Pressed)
                        {
                            if (mouseButton.ButtonIndex == MouseButton.Right || mouseButton.ButtonIndex == MouseButton.Middle)
                            {
                                if (!Realm.Client.VSCodeManager.Instance.IsVisible)
                                {
                                    ToggleVSCodeEditor();
                                }
                                Realm.Client.VSCodeManager.Instance.OpenDevTools();
                                GetViewport().SetInputAsHandled();
                            }
                        }
                    };
                    editorsVBox.AddChild(_btnVSCode);
                }

                _btnAssetsManager = new Button();
                _btnAssetsManager.Name = "BtnAssetsManager";
                SetupOptionButton(_btnAssetsManager, $"{UnicodeIcons.CUBE} ASSETS", () => _assetManagerDialog?.OpenDialog(), 13, "Open Map Assets Manager & Importer");
                _btnAssetsManager.Pressed += () => _popupEditors.Hide();
                editorsVBox.AddChild(_btnAssetsManager);

                _btnTemplateManager = new Button();
                _btnTemplateManager.Name = "BtnTemplateManager";
                SetupOptionButton(_btnTemplateManager, $"{UnicodeIcons.CUBES} TEMPLATES", () => OpenTemplateManagerDialog(), 13, "Open dialog to manage object template types and visual properties");
                _btnTemplateManager.Pressed += () => _popupEditors.Hide();
                editorsVBox.AddChild(_btnTemplateManager);

                _btnInstanceManager = new Button();
                _btnInstanceManager.Name = "BtnInstanceManager";
                SetupOptionButton(_btnInstanceManager, $"{UnicodeIcons.LIST} INSTANCES", () => OpenInstanceManagerDialog(), 13, "Open dialog to list and locate all placed instances");
                _btnInstanceManager.Pressed += () => _popupEditors.Hide();
                editorsVBox.AddChild(_btnInstanceManager);

                _btnEditors = GetNodeOrNull<Button>("TopLeftBox/BtnEditors") ?? GetNodeOrNull<Button>("TopLeftBox/BtnVSCode");
                if (_btnEditors == null)
                {
                    _btnEditors = new Button();
                    _btnEditors.Name = "BtnEditors";
                }
                SetupButton(_btnEditors, $"EDITORS {UnicodeIcons.CHEVRON_DOWN}", () =>
                {
                    var popupPosition = _btnEditors.GetScreenPosition() + new Vector2(0, _btnEditors.Size.Y);
                    _popupEditors.Popup(new Rect2I((Vector2I)popupPosition, Vector2I.Zero));
                }, 13, "Open Map Editors & Managers menu");
                StyleMapEditorTopButton(_btnEditors);

                _btnUndo = GetNode<Button>("TopLeftBox/BtnUndo");
                SetupButton(_btnUndo, $"{UnicodeIcons.UNDO} UNDO", () => UndoAction(), 13, "Undo the last action (Ctrl+Z)");
                StyleMapEditorTopButton(_btnUndo);

                _btnRedo = GetNode<Button>("TopLeftBox/BtnRedo");
                SetupButton(_btnRedo, $"{UnicodeIcons.REDO} REDO", () => RedoAction(), 13, "Redo the last undone action (Ctrl+Y)");
                StyleMapEditorTopButton(_btnRedo);

                _btnEyedropper = GetNode<Button>("TopLeftBox/BtnEyedropper");
                SetupButton(_btnEyedropper, $"{UnicodeIcons.EYEDROPPER} EYEDROPPER", () => TriggerToolSelection(Realm.Client.Core.GameHost.EditorTool.Eyedropper, _btnEyedropper), 13, "Pick / sample entities, terrain height (Shift+Click), or vertex color under cursor (I)");
                StyleMapEditorTopButton(_btnEyedropper);

                _optModule = GetNode<OptionButton>("TopLeftBox/OptModule");
                StyleOptionButtonPopup(_optModule);
                _optModule.AddItem($"{UnicodeIcons.MOUNTAIN} " + TranslationServer.Translate("TERRAIN"), (int)EditorModule.Terrain);
                _optModule.AddItem($"{UnicodeIcons.PAINT_BRUSH} " + TranslationServer.Translate("TEXTURE"), (int)EditorModule.TextureDeco);
                _optModule.AddItem($"{UnicodeIcons.VECTOR_SQUARE} " + TranslationServer.Translate("PATHING"), (int)EditorModule.Pathing);
                _optModule.AddItem($"{UnicodeIcons.CUBE} " + TranslationServer.Translate("OBJECTS"), (int)EditorModule.Objects);
                _optModule.AddItem($"{UnicodeIcons.DRAW_POLYGON} " + TranslationServer.Translate("COORDINATES"), (int)EditorModule.Coordinates);
                _optModule.AddItem($"{UnicodeIcons.PASTE} " + TranslationServer.Translate("CLIPBOARD"), (int)EditorModule.Clipboard);
                _optModule.ItemSelected += (index) => SwitchModule((EditorModule)index);
                StyleMapEditorTopButton(_optModule);

                _btnGameSettings = GetNodeOrNull<Button>("TopLeftBox/BtnSettings");
                if (_btnGameSettings != null)
                {
                    SetupIconButton(_btnGameSettings, "res://Assets/UI/gear_icon.png", () =>
                    {
                        Realm.Client.UI.UIManager.Instance?.OpenSettingsOverlay();
                    }, "Game Settings");
                    StyleMapEditorTopButton(_btnGameSettings);
                }

                _btnTestMap = GetNodeOrNull<Button>("TopLeftBox/BtnTestMap");
                if (_btnTestMap == null)
                {
                    _btnTestMap = new Button();
                    _btnTestMap.Name = "BtnTestMap";
                    if (_topLeftBox != null)
                    {
                        _topLeftBox.AddChild(_btnTestMap);
                    }
                }
                SetupButton(_btnTestMap, $"{UnicodeIcons.GAMEPAD} TEST", () => TestMapAction(), 13, "Launch single-player mode on the current editor map");
                StyleMapEditorTopButton(_btnTestMap);

                if (_topLeftBox != null)
                {
                    if (_btnTestMap != null && _btnGameSettings != null)
                    {
                        _topLeftBox.MoveChild(_btnTestMap, _btnGameSettings.GetIndex() + 1);
                    }

                    _btnResetLayout = new Button();
                    _btnResetLayout.Name = "BtnResetLayout";
                    SetupButton(_btnResetLayout, $"{UnicodeIcons.PIN} RESET LAYOUT", () => ResetAllPanelPositions(), 12, "Reset all floating panels back to default sidebar positions");
                    StyleMapEditorTopButton(_btnResetLayout);
                    _topLeftBox.AddChild(_btnResetLayout);
                    if (_btnGameSettings != null)
                    {
                        _topLeftBox.MoveChild(_btnResetLayout, _btnGameSettings.GetIndex());
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
                SetupOptionButton(_btnLoad, $"{UnicodeIcons.FOLDER_OPEN} LOAD", () => LoadMapAction(), 11, "Load heights, colors, and entities from a saved json file (Ctrl+O)");

                _btnSave = GetNode<Button>("LeftSlidePanel/LeftScroll/LeftVBox/FileAccordion/ContentFile/BtnSave");
                SetupOptionButton(_btnSave, $"{UnicodeIcons.SAVE} SAVE", () => SaveMapActionExternal(), 11, "Save current heightmap, textures, and entities (Ctrl+S)");

                _btnSaveAs = new Button();
                _btnSaveAs.Name = "BtnSaveAs";
                _btnSaveAs.Set("icon_max_width", 0);
                SetupOptionButton(_btnSaveAs, $"{UnicodeIcons.SAVE} SAVE AS", () => SaveAsMapAction(), 13, "Save map to a new folder location");

                _btnPublish = GetNode<Button>("LeftSlidePanel/LeftScroll/LeftVBox/FileAccordion/ContentFile/BtnPublish");
                SetupOptionButton(_btnPublish, $"{UnicodeIcons.UPLOAD} PUBLISH", () => PublishMapActionExternal(), 13, "Publish/export map to custom map registry");

                _btnExportMap = GetNodeOrNull<Button>("LeftSlidePanel/LeftScroll/LeftVBox/FileAccordion/ContentFile/BtnExportMap");
                if (_btnExportMap == null)
                {
                    _btnExportMap = new Button();
                    _btnExportMap.Name = "BtnExportMap";
                    _btnExportMap.Set("icon_max_width", 0);
                }
                SetupOptionButton(_btnExportMap, $"{UnicodeIcons.FILE_EXPORT} EXPORT (.RMAP)", () => ExportMapAction(), 13, "Export prepared map package (.rmap) with compiled WASM for hosting and CAS storage");

                _popupSaveMore = new PopupPanel();
                _popupSaveMore.Name = "PopupSaveMore";
                var saveMorePopupStyle = new StyleBoxFlat();
                saveMorePopupStyle.BgColor = new Color(0.14f, 0.13f, 0.11f, 0.98f);
                saveMorePopupStyle.BorderColor = UIStyle.ColorGold;
                saveMorePopupStyle.SetBorderWidthAll(1);
                saveMorePopupStyle.SetCornerRadiusAll(4);
                saveMorePopupStyle.SetContentMarginAll(10);
                _popupSaveMore.AddThemeStyleboxOverride("panel", saveMorePopupStyle);
                var saveMoreVBox = new VBoxContainer();
                saveMoreVBox.AddThemeConstantOverride("separation", 6);
                saveMoreVBox.CustomMinimumSize = new Vector2(170, 0);
                _popupSaveMore.AddChild(saveMoreVBox);
                AddChild(_popupSaveMore);
                SafeReparent(_btnSaveAs, saveMoreVBox);
                SafeReparent(_btnExportMap, saveMoreVBox);
                SafeReparent(_btnPublish, saveMoreVBox);
                _btnSaveAs.Pressed += () => _popupSaveMore.Hide();
                _btnExportMap.Pressed += () => _popupSaveMore.Hide();
                _btnPublish.Pressed += () => _popupSaveMore.Hide();

                _btnSaveMore = new Button();
                _btnSaveMore.Name = "BtnSaveMore";
                _btnSaveMore.Set("icon_max_width", 0);
                SetupOptionButton(_btnSaveMore, UnicodeIcons.CHEVRON_DOWN, () =>
                {
                    var popupPosition = _btnSaveMore.GetScreenPosition() + new Vector2(0, _btnSaveMore.Size.Y);
                    _popupSaveMore.Popup(new Rect2I((Vector2I)popupPosition, Vector2I.Zero));
                }, 10, "More save & export options (Save As, Export, Publish)");

                _btnResetMap = GetNode<Button>("LeftSlidePanel/LeftScroll/LeftVBox/FileAccordion/ContentFile/BtnResetMap");
                SetupOptionButton(_btnResetMap, $"{UnicodeIcons.TRASH} RESET MAP", () =>
                {
                    ShowConfirmationDialog(
                        "Are you sure you want to clear the entire map? This will delete all placed entities and reset terrain heights.",
                        () => ResetToBlankMap()
                    );
                }, 13, "Clear all terrain heights, colors, and placed entities");

                _btnGenerateMap = GetNode<Button>("LeftSlidePanel/LeftScroll/LeftVBox/FileAccordion/ContentFile/BtnGenerateMap");
                SetupOptionButton(_btnGenerateMap, $"{UnicodeIcons.DICE} PROCEDURAL", () => _generationDialog.Show(), 13, "Open random terrain generator settings modal");

                _btnImportMinimap = GetNode<Button>("LeftSlidePanel/LeftScroll/LeftVBox/FileAccordion/ContentFile/BtnImportMinimap");
                SetupOptionButton(_btnImportMinimap, $"{UnicodeIcons.MAP_IMAGE} FROM IMAGE", () => ImportTerrainFromMinimapDialog(), 13, "Import terrain elevations, textures, and trees from a minimap image file");

                _popupRandomGen = new PopupPanel();
                _popupRandomGen.Name = "PopupRandomGen";
                var randomGenPopupStyle = new StyleBoxFlat();
                randomGenPopupStyle.BgColor = new Color(0.14f, 0.13f, 0.11f, 0.98f);
                randomGenPopupStyle.BorderColor = UIStyle.ColorGold;
                randomGenPopupStyle.SetBorderWidthAll(1);
                randomGenPopupStyle.SetCornerRadiusAll(4);
                randomGenPopupStyle.SetContentMarginAll(10);
                _popupRandomGen.AddThemeStyleboxOverride("panel", randomGenPopupStyle);
                var randomGenVBox = new VBoxContainer();
                randomGenVBox.AddThemeConstantOverride("separation", 6);
                randomGenVBox.CustomMinimumSize = new Vector2(170, 0);
                _popupRandomGen.AddChild(randomGenVBox);
                AddChild(_popupRandomGen);
                SafeReparent(_btnGenerateMap, randomGenVBox);
                SafeReparent(_btnImportMinimap, randomGenVBox);
                _btnGenerateMap.Pressed += () => _popupRandomGen.Hide();
                _btnImportMinimap.Pressed += () => _popupRandomGen.Hide();

                _btnRandomGen = new Button();
                _btnRandomGen.Name = "BtnRandomGen";
                SetupOptionButton(_btnRandomGen, $"{UnicodeIcons.DICE} RANDOM GEN", () =>
                {
                    var popupPosition = _btnRandomGen.GetScreenPosition() + new Vector2(0, _btnRandomGen.Size.Y);
                    _popupRandomGen.Popup(new Rect2I((Vector2I)popupPosition, Vector2I.Zero));
                }, 13, "Open random map generation options");

                _btnEditorSettings = new Button();
                _btnEditorSettings.Name = "BtnEditorSettings";
                _btnEditorSettings.Set("icon_max_width", 0);
                SetupOptionButton(_btnEditorSettings, "⚙️ " + TranslationServer.Translate("EDITOR SETTINGS"), () => _editorSettingsDialog?.OpenDialog(), 13, "Configure editor preferences, chrome border, and display overlays");
                _contentFile.AddChild(_btnEditorSettings);

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
                    if (Realm.Client.Core.GameHost.Instance == null) return;
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
                            (true, true) => Realm.Client.Core.GameHost.GridOverlayMode.Both,
                            (true, false) => Realm.Client.Core.GameHost.GridOverlayMode.Grid,
                            (false, true) => Realm.Client.Core.GameHost.GridOverlayMode.Polar,
                            (false, false) => Realm.Client.Core.GameHost.GridOverlayMode.Off
                        };

                        Realm.Client.Core.GameHost.Instance.EditorGridMode = newMode;
                        Realm.Client.Core.GameHost.Instance.UpdateGridOverlayVisibility();
                        UpdateGridOverlayExternal(newMode);
                        string modeName = newMode switch
                        {
                            Realm.Client.Core.GameHost.GridOverlayMode.Off => "OFF",
                            Realm.Client.Core.GameHost.GridOverlayMode.Grid => "GRID",
                            Realm.Client.Core.GameHost.GridOverlayMode.Polar => "POLAR",
                            Realm.Client.Core.GameHost.GridOverlayMode.Both => "GRID + POLAR",
                            _ => "OFF"
                        };
                        ShowFeedback($"Overlay Mode: {modeName}");
                    }
                    else if (id == 2)
                    {
                        Realm.Client.Core.GameHost.Instance.EditorCameraBoundsVisible = newChecked;
                        Realm.Client.Core.GameHost.Instance.UpdateCameraBoundsOverlayVisibility();
                        UpdateCameraBoundsOverlayExternal(newChecked);
                        ShowFeedback(newChecked
                            ? TranslationServer.Translate("Camera Bounds: ON")
                            : TranslationServer.Translate("Camera Bounds: OFF"));
                    }
                    else if (id == 3)
                    {
                        if (Realm.Client.Core.GameHost.Instance.GroundTerrain != null)
                        {
                            Realm.Client.Core.GameHost.Instance.GroundTerrain.ToggleWireframeMode();
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
                SetupButton(_btnToggleGrid, UnicodeIcons.BORDER_ALL, () => OpenOverlayModePopup(), 12, "Overlay");
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
                    if (Realm.Client.Core.GameHost.Instance != null)
                    {
                        bool disableShadows = !pressed;
                        if (Realm.Client.Core.GameHost.Instance.EditorDisableShadows != disableShadows)
                        {
                            Realm.Client.Core.GameHost.Instance.EditorDisableShadows = disableShadows;
                            Realm.Client.Core.GameHost.Instance.UpdateEditorShadows();
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
                SetupButton(_btnToggleEnvironment, UnicodeIcons.SUN, () => OpenEnvironmentPopup(), 12, "Environment");

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
                StylePopupButton(_btnRotate, $"{UnicodeIcons.ROTATE} Rotate 90° (R)", "Rotate camera 90 degrees (R)");
                _btnRotate.Pressed += () =>
                {
                    Realm.Client.UI.UIManager.Instance?.PlayClickSound();
                    var camera = (Realm.Client.Core.GameHost.Instance?.MainCamera as Realm.Client.CameraControl);
                    camera?.Rotate90Degrees();
                };
                camVBox.AddChild(_btnRotate);

                _btnCameraAngle = new Button();
                _btnCameraAngle.Name = "BtnCameraAngle";
                _btnCameraAngle.Set("icon_max_width", 0);
                StylePopupButton(_btnCameraAngle, $"{UnicodeIcons.CUBE} Top-Down (C)", "Toggle perspective vs top-down angle (C)");
                _btnCameraAngle.Pressed += () =>
                {
                    var camera = (Realm.Client.Core.GameHost.Instance?.MainCamera as Realm.Client.CameraControl);
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
                StylePopupButton(_btnZoomIn, $"{UnicodeIcons.ZOOM_IN} Zoom In (+)", "Zoom camera in (+)");
                _btnZoomIn.Pressed += () =>
                {
                    Realm.Client.UI.UIManager.Instance?.PlayClickSound();
                    (Realm.Client.Core.GameHost.Instance?.MainCamera as Realm.Client.CameraControl)?.ZoomIn();
                };
                camVBox.AddChild(_btnZoomIn);

                _btnZoomOut = new Button();
                _btnZoomOut.Name = "BtnZoomOut";
                _btnZoomOut.Set("icon_max_width", 0);
                StylePopupButton(_btnZoomOut, $"{UnicodeIcons.ZOOM_OUT} Zoom Out (-)", "Zoom camera out (-)");
                _btnZoomOut.Pressed += () =>
                {
                    Realm.Client.UI.UIManager.Instance?.PlayClickSound();
                    (Realm.Client.Core.GameHost.Instance?.MainCamera as Realm.Client.CameraControl)?.ZoomOut();
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
                    var cam = Realm.Client.Core.GameHost.Instance?.MainCamera as Realm.Client.CameraControl;
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
                SetupButton(_btnToggleCamera, UnicodeIcons.CAMERA, () => OpenCameraPopup(), 12, "Configure camera controls (R / C / + / - / F8)");

                var initialCam = Realm.Client.Core.GameHost.Instance?.MainCamera as Realm.Client.CameraControl;
                UpdateFreeCameraExternal(initialCam != null && initialCam.IsFreeCamera);
                UpdateShadowsExternal(Realm.Client.Core.GameHost.Instance?.EditorDisableShadows ?? false);

                _minimapFrame = GetNode<PanelContainer>("LeftSlidePanel/LeftScroll/LeftVBox/ViewportAccordion/ContentViewport/MinimapFrame");
                _minimapArea = GetNode<Control>("LeftSlidePanel/LeftScroll/LeftVBox/ViewportAccordion/ContentViewport/MinimapFrame/MinimapArea");
                _cameraIndicator = GetNode<Realm.Client.UI.MapEditor.MapEditorCameraIndicator>("LeftSlidePanel/LeftScroll/LeftVBox/ViewportAccordion/ContentViewport/MinimapFrame/MinimapArea/CameraIndicator");

                _mapSettingsDialog = new Realm.Client.UI.MapEditor.MapSettingsDialog(this);
                AddChild(_mapSettingsDialog);

                _btnTapeMeasure = new Button();
                _btnTapeMeasure.Name = "BtnTapeMeasure";
                _btnTapeMeasure.Set("icon_max_width", 0);
                SetupButton(_btnTapeMeasure, UnicodeIcons.RULER, () =>
                {
                    if (Realm.Client.Core.GameHost.Instance != null)
                    {
                        if (Realm.Client.Core.GameHost.Instance.ActiveEditorTool == Realm.Client.Core.GameHost.EditorTool.Measure)
                        {
                            Realm.Client.Core.GameHost.Instance.ClearMeasureVisuals();
                            ClearMeasureTelemetry();
                            TriggerToolSelection(SavedActiveTool != Realm.Client.Core.GameHost.EditorTool.Measure ? SavedActiveTool : Realm.Client.Core.GameHost.EditorTool.Raise, null);
                        }
                        else
                        {
                            TriggerToolSelection(Realm.Client.Core.GameHost.EditorTool.Measure, _btnTapeMeasure);
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
                SetupOptionButton(_btnResetPivotToCenter, $"{UnicodeIcons.CROSSHAIRS} RESET PIVOT", () => ResetPivotToMapCenter(), 10, "Reset symmetry and polar overlay center pivot to true map center");

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
                _cardRaise = CreateToolCard(_btnRaise, UnicodeIcons.ARROW_UP, "Raise", () => TriggerToolSelection(Realm.Client.Core.GameHost.EditorTool.Raise, _btnRaise), "Elevate terrain height (1)");

                _btnLower = GetNode<Button>("RightSlidePanel/RightScroll/AccordionContainer/ToolAccordion/ContentTool/PanelTerrainVBox/BtnLower");
                _cardLower = CreateToolCard(_btnLower, UnicodeIcons.ARROW_DOWN, "Lower", () => TriggerToolSelection(Realm.Client.Core.GameHost.EditorTool.Lower, _btnLower), "Lower terrain height (2)");

                _btnHeight = GetNode<Button>("RightSlidePanel/RightScroll/AccordionContainer/ToolAccordion/ContentTool/PanelTerrainVBox/BtnHeight");
                _cardHeight = CreateToolCard(_btnHeight, UnicodeIcons.ARROWS_V, "Height", () => TriggerToolSelection(Realm.Client.Core.GameHost.EditorTool.Height, _btnHeight), "Set terrain to exact height (3)");

                _btnSmooth = GetNode<Button>("RightSlidePanel/RightScroll/AccordionContainer/ToolAccordion/ContentTool/PanelTerrainVBox/BtnSmooth");
                _cardSmooth = CreateToolCard(_btnSmooth, UnicodeIcons.SMOOTH, "Smooth", () => TriggerToolSelection(Realm.Client.Core.GameHost.EditorTool.Smooth, _btnSmooth), "Smooth terrain height (4)");

                _btnPlateau = GetNode<Button>("RightSlidePanel/RightScroll/AccordionContainer/ToolAccordion/ContentTool/PanelTerrainVBox/BtnPlateau");
                _cardPlateau = CreateToolCard(_btnPlateau, UnicodeIcons.SQUARE, "Flatten", () => TriggerToolSelection(Realm.Client.Core.GameHost.EditorTool.Plateau, _btnPlateau), "Flatten terrain to cursor height on click (5)");

                _btnRamp = GetNode<Button>("RightSlidePanel/RightScroll/AccordionContainer/ToolAccordion/ContentTool/PanelTerrainVBox/BtnRamp");
                _cardRamp = CreateToolCard(_btnRamp, UnicodeIcons.RAMP, "Ramp", () => TriggerToolSelection(Realm.Client.Core.GameHost.EditorTool.Ramp, _btnRamp), "Create ramp between two points (6)");

                _btnNoise = GetNode<Button>("RightSlidePanel/RightScroll/AccordionContainer/ToolAccordion/ContentTool/PanelTerrainVBox/BtnNoise");
                _cardNoise = CreateToolCard(_btnNoise, UnicodeIcons.NOISE, "Noise", () => TriggerToolSelection(Realm.Client.Core.GameHost.EditorTool.Noise, _btnNoise), "Add random height variations/noise to terrain (7)");

                _btnWater = new Button();
                _btnWater.Name = "BtnWater";
                _cardWater = CreateToolCard(_btnWater, UnicodeIcons.WATER, "Water", () => TriggerToolSelection(Realm.Client.Core.GameHost.EditorTool.Water, _btnWater), "Flood fill water mesh bounded by cliff walls");

                _btnTextureBrush = GetNode<Button>("RightSlidePanel/RightScroll/AccordionContainer/ToolAccordion/ContentTool/PanelDecoVBox/BtnTextureBrush");
                _cardTextureBrush = CreateToolCard(_btnTextureBrush, UnicodeIcons.PAINT_BRUSH, "Paint", () => TriggerToolSelection(Realm.Client.Core.GameHost.EditorTool.PaintTexture, _btnTextureBrush), "Paint terrain texture (8)");

                _btnFloodFill = GetNode<Button>("RightSlidePanel/RightScroll/AccordionContainer/ToolAccordion/ContentTool/PanelDecoVBox/BtnFloodFill");
                _cardFloodFill = CreateToolCard(_btnFloodFill, UnicodeIcons.FILL_DRIP, "Flood Fill", () => TriggerToolSelection(Realm.Client.Core.GameHost.EditorTool.FloodFill, _btnFloodFill), "Flood fill terrain texture");

                _btnSelectArea = GetNode<Button>("RightSlidePanel/RightScroll/AccordionContainer/ToolAccordion/ContentTool/PanelClipboard/BtnSelectArea");
                _cardSelectArea = CreateToolCard(_btnSelectArea, UnicodeIcons.EXPAND_ARROWS, "Select Area", () => TriggerToolSelection(Realm.Client.Core.GameHost.EditorTool.SelectArea, _btnSelectArea), "Select rectangular area");

                _btnPathingBrush = GetNode<Button>("RightSlidePanel/RightScroll/AccordionContainer/ToolAccordion/ContentTool/PanelPathingVBox/BtnPathingBrush");
                _cardPathingBrush = CreateToolCard(_btnPathingBrush, UnicodeIcons.BRUSH_ALT, "Brush", () => TriggerToolSelection(Realm.Client.Core.GameHost.EditorTool.PaintPathing, _btnPathingBrush), "Paint pathing attributes onto the terrain map");

                _btnFloodFillPathing = GetNode<Button>("RightSlidePanel/RightScroll/AccordionContainer/ToolAccordion/ContentTool/PanelPathingVBox/BtnFloodFillPathing");
                _cardFloodFillPathing = CreateToolCard(_btnFloodFillPathing, UnicodeIcons.FILL_DRIP, "Flood Fill", () => TriggerToolSelection(Realm.Client.Core.GameHost.EditorTool.FloodFillPathing, _btnFloodFillPathing), "Flood fill pathing attributes onto the terrain map");

                _btnAddObject = GetNode<Button>("RightSlidePanel/RightScroll/AccordionContainer/ToolAccordion/ContentTool/PanelObjectsVBox/BtnAddObject");
                _cardAddObject = CreateToolCard(_btnAddObject, UnicodeIcons.CUBE, "Add Object", () => _entityPaletteController?.TriggerAddObjectMode(), "Place units, props, or decals");

                _btnSelectMove = GetNode<Button>("RightSlidePanel/RightScroll/AccordionContainer/ToolAccordion/ContentTool/PanelObjectsVBox/BtnSelectMove");
                _cardSelectMove = CreateToolCard(_btnSelectMove, UnicodeIcons.MOVE, "Select/Move", () => TriggerToolSelection(Realm.Client.Core.GameHost.EditorTool.SelectMove, _btnSelectMove), "Select and move units, props, or decals");

                _btnDeleteObject = GetNode<Button>("RightSlidePanel/RightScroll/AccordionContainer/ToolAccordion/ContentTool/PanelObjectsVBox/BtnDeleteObject");
                _cardDeleteObject = CreateToolCard(_btnDeleteObject, UnicodeIcons.TRASH, "Erase", () =>
                {
                    if (GodotObject.IsInstanceValid(Realm.Client.Core.GameHost.Instance?.SelectedEditorObject))
                        DeleteSelectedObjectAction();
                    else
                        TriggerToolSelection(Realm.Client.Core.GameHost.EditorTool.DeleteObject, _btnDeleteObject);
                }, "Erase units, props, or decals");

                _btnDrawCoordinate = GetNode<Button>("RightSlidePanel/RightScroll/AccordionContainer/ToolAccordion/ContentTool/PanelCoordinatesVBox/BtnDrawCoordinate");
                SetupButton(_btnDrawCoordinate, $"{UnicodeIcons.DRAW_POLYGON} DRAW COORD", () => TriggerToolSelection(Realm.Client.Core.GameHost.EditorTool.DrawCoordinate, _btnDrawCoordinate), 11, "Drag to define a named coordinate box exposed as C# variables");

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
                    bool ok = EditorApi?.CommitCoordinate(name, _pendingCoordinateMinX, _pendingCoordinateMinZ, _pendingCoordinateMaxX, _pendingCoordinateMaxZ) ?? false;
                    if (ok)
                    {
                        RefreshCoordinateListExternal();
                        ShowFeedback($"Coordinate '{name}' created.");
                        _txtCoordinateName.Text = "";
                    }
                };
                _coordinateListVBox = GetNode<VBoxContainer>("RightSlidePanel/RightScroll/AccordionContainer/ToolAccordion/ContentTool/PanelCoordinatesVBox/CoordinateListVBox");

                _btnCopy = GetNode<Button>("RightSlidePanel/RightScroll/AccordionContainer/ToolAccordion/ContentTool/PanelClipboard/BtnCopy");
                _cardCopy = CreateToolCard(_btnCopy, UnicodeIcons.COPY, "Copy", () => Realm.Client.Core.GameHost.Instance?.PerformCopyAreaExternal(), "Copy selected area to clipboard (Ctrl+C)");

                _btnPaste = GetNode<Button>("RightSlidePanel/RightScroll/AccordionContainer/ToolAccordion/ContentTool/PanelClipboard/BtnPaste");
                _cardPaste = CreateToolCard(_btnPaste, UnicodeIcons.PASTE, "Paste", () => TriggerToolSelection(Realm.Client.Core.GameHost.EditorTool.PasteArea, _btnPaste), "Paste clipboard contents onto terrain (Ctrl+V)");

                _btnCut = GetNode<Button>("RightSlidePanel/RightScroll/AccordionContainer/ToolAccordion/ContentTool/PanelClipboard/BtnCut");
                _cardCut = CreateToolCard(_btnCut, UnicodeIcons.SCISSORS, "Cut", () => Realm.Client.Core.GameHost.Instance?.PerformCutAreaExternal(), "Cut selected area to clipboard (Ctrl+X)");

                _btnEraseArea = GetNode<Button>("RightSlidePanel/RightScroll/AccordionContainer/ToolAccordion/ContentTool/PanelClipboard/BtnEraseArea");
                _cardEraseArea = CreateToolCard(_btnEraseArea, UnicodeIcons.TRASH, "Erase Area", () => Realm.Client.Core.GameHost.Instance?.PerformEraseAreaExternal(), "Erase heights, textures and objects within selection (Delete)");

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
                SetupOptionButton(_btnBrushShape, $"{UnicodeIcons.SQUARE} BRUSH: SQUARE", () =>
                {
                    if (Realm.Client.Core.GameHost.Instance != null)
                    {
                        Realm.Client.Core.GameHost.Instance.EditorBrushIsSquare = !Realm.Client.Core.GameHost.Instance.EditorBrushIsSquare;
                        Realm.Client.Core.GameHost.Instance.UpdateBrushMesh();
                        UpdateBrushShapeExternal(Realm.Client.Core.GameHost.Instance.EditorBrushIsSquare);
                    }
                }, 11, "Toggle brush shape between circular and square (B)");

                _optMirrorMode = GetNode<OptionButton>("RightSlidePanel/RightScroll/AccordionContainer/BrushAccordion/ContentBrush/OptMirrorMode");
                SetupMirrorDropdown(_optMirrorMode, "Select terrain and object mirroring symmetry mode");

                _chkBlockMode = GetNode<CheckBox>("RightSlidePanel/RightScroll/AccordionContainer/BrushAccordion/ContentBrush/ChkBlockMode");
                _chkBlockMode.Toggled += (toggled) =>
                {
                    if (Realm.Client.Core.GameHost.Instance != null)
                        Realm.Client.Core.GameHost.Instance.EditorBlockMode = toggled;

                    ShowFeedback(toggled ? "Block Mode: Enabled" : "Block Mode: Disabled");
                    UpdateBlockStepVisibility();
                    UpdateBrushStrengthVisibility();
                    if (Realm.Client.Core.GameHost.Instance != null)
                        UpdateSidebarMorph(Realm.Client.Core.GameHost.Instance.ActiveEditorTool);
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
                    if (Realm.Client.Core.GameHost.Instance != null) Realm.Client.Core.GameHost.Instance.EditorWaterHeight = fVal;
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
                    if (Realm.Client.Core.GameHost.Instance != null)
                    {
                        Realm.Client.Core.GameHost.Instance.EditorWaterMode = WaterType.Shallow;
                        Realm.Client.Core.GameHost.Instance.ActiveWaterProfileIndex = profIdx;
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
                SetupOptionButton(btnTextureSwap, $"{UnicodeIcons.REFRESH} SWAP TEXTURES (GLOBAL)", () =>
                {
                    if (Realm.Client.Core.GameHost.Instance != null)
                    {
                        Realm.Client.Core.GameHost.Instance.SwapTexturesExternal(Realm.Client.Core.GameHost.Instance.EditorPaintTextureIndex, Realm.Client.Core.GameHost.Instance.EditorCliffPaintTextureIndex);
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
                SetupOptionButton(_btnReplaceTexture, $"{UnicodeIcons.UPLOAD} REPLACE TEXTURE", () => _replaceTextureDialog?.OpenDialog(), 11, "Replace all instances of a texture with another texture across the map");
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

                chkPasteTextures.Toggled += (toggled) => { if (Realm.Client.Core.GameHost.Instance != null) Realm.Client.Core.GameHost.Instance.PasteOptionTextures = toggled; };
                chkPasteHeights.Toggled += (toggled) => { if (Realm.Client.Core.GameHost.Instance != null) Realm.Client.Core.GameHost.Instance.PasteOptionHeights = toggled; };
                chkPasteEntities.Toggled += (toggled) => { if (Realm.Client.Core.GameHost.Instance != null) Realm.Client.Core.GameHost.Instance.PasteOptionEntities = toggled; };
                chkPastePathing.Toggled += (toggled) => { if (Realm.Client.Core.GameHost.Instance != null) Realm.Client.Core.GameHost.Instance.PasteOptionPathing = toggled; };

                _lblPasteRotation = GetNode<Label>("RightSlidePanel/RightScroll/AccordionContainer/ToolSettingsAccordion/ContentToolSettings/ContainerPaste/PasteOptionsBox/PasteRotationBox/Header/LblPasteRotationValue");
                _sldPasteRotation = GetNode<HSlider>("RightSlidePanel/RightScroll/AccordionContainer/ToolSettingsAccordion/ContentToolSettings/ContainerPaste/PasteOptionsBox/PasteRotationBox/SldPasteRotation");
                _sldPasteRotation.ValueChanged += (val) =>
                {
                    float fVal = (float)val;
                    _lblPasteRotation.Text = fVal.ToString("F0") + "°";
                    if (Realm.Client.Core.GameHost.Instance != null)
                    {
                        Realm.Client.Core.GameHost.Instance.EditorPasteRotation = fVal;
                    }
                };
                _sldPasteRotation.DragStarted += () => _isDraggingSlider = true;
                _sldPasteRotation.DragEnded += (valueChanged) => _isDraggingSlider = false;

                _btnClipboardBrushShape = new Button();
                _btnClipboardBrushShape.Name = "BtnClipboardBrushShape";
                _btnClipboardBrushShape.Set("icon_max_width", 0);
                SetupOptionButton(_btnClipboardBrushShape, Realm.Client.Core.GameHost.Instance != null && !Realm.Client.Core.GameHost.Instance.EditorBrushIsSquare ? $"{UnicodeIcons.CIRCLE} BRUSH: CIRCLE" : $"{UnicodeIcons.SQUARE} BRUSH: SQUARE", () =>
                {
                    if (Realm.Client.Core.GameHost.Instance != null)
                    {
                        Realm.Client.Core.GameHost.Instance.EditorBrushIsSquare = !Realm.Client.Core.GameHost.Instance.EditorBrushIsSquare;
                        UpdateBrushShapeExternal(Realm.Client.Core.GameHost.Instance.EditorBrushIsSquare);
                        ShowFeedback(Realm.Client.Core.GameHost.Instance.EditorBrushIsSquare ? TranslationServer.Translate("Brush Shape: SQUARE") : TranslationServer.Translate("Brush Shape: CIRCLE"));
                    }
                }, 10, "Toggle square / circular selection and clipboard brush shape");

                _optClipboardMirrorMode = new OptionButton();
                _optClipboardMirrorMode.Name = "OptClipboardMirrorMode";
                _optClipboardMirrorMode.Set("icon_max_width", 0);
                SetupMirrorDropdown(_optClipboardMirrorMode, "Select terrain, clipboard and object mirroring symmetry mode");

                _btnPasteReflection = new Button();
                _btnPasteReflection.Name = "BtnPasteReflection";
                _btnPasteReflection.Set("icon_max_width", 0);
                SetupOptionButton(_btnPasteReflection, $"{UnicodeIcons.ARROWS_H} REFLECT: NONE", () => CyclePasteReflection(), 11, "Cycle reflection mode for paste operation (None, Horizontal, Vertical)");

                _btnPasteAnchor = new Button();
                _btnPasteAnchor.Name = "BtnPasteAnchor";
                _btnPasteAnchor.Set("icon_max_width", 0);
                SetupOptionButton(_btnPasteAnchor, $"{UnicodeIcons.MOUSE_POINTER} ANCHOR: CENTER", () => CyclePasteAnchor(), 11, "Cycle pivot / anchor tile used to align pasted selection (Center, Corners)");

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
                SetupOptionButton(_btnToggleSnap, $"{UnicodeIcons.TABLE} SNAP TO GRID: OFF", () =>
                {
                    if (Realm.Client.Core.GameHost.Instance != null)
                    {
                        Realm.Client.Core.GameHost.Instance.EditorSnapToGrid = !Realm.Client.Core.GameHost.Instance.EditorSnapToGrid;
                        UpdateGridSnapExternal(Realm.Client.Core.GameHost.Instance.EditorSnapToGrid);
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
                    if (Realm.Client.Core.GameHost.Instance != null)
                    {
                        UpdateSidebarMorph(Realm.Client.Core.GameHost.Instance.ActiveEditorTool);
                    }
                };

                _sldClumpDensity.ValueChanged += (val) =>
                {
                    float fVal = (float)val;
                    _lblClumpDensityValue.Text = fVal.ToString("F0");
                    if (Realm.Client.Core.GameHost.Instance != null) Realm.Client.Core.GameHost.Instance.EditorClumpCount = fVal;
                };
                _sldClumpDensity.DragStarted += () => _isDraggingSlider = true;
                _sldClumpDensity.DragEnded += (valueChanged) => _isDraggingSlider = false;

                _sldClumpScaleVar.ValueChanged += (val) =>
                {
                    float fVal = (float)val;
                    _lblClumpScaleVarValue.Text = fVal.ToString("F2");
                    if (Realm.Client.Core.GameHost.Instance != null) Realm.Client.Core.GameHost.Instance.EditorClumpScale = fVal;
                };
                _sldClumpScaleVar.DragStarted += () => _isDraggingSlider = true;
                _sldClumpScaleVar.DragEnded += (valueChanged) => _isDraggingSlider = false;

                _sldPlacementRotate.DragStarted += () => _isDraggingSlider = true;
                _sldPlacementRotate.DragEnded += (valueChanged) => _isDraggingSlider = false;
                _sldPlacementScale.DragStarted += () => _isDraggingSlider = true;
                _sldPlacementScale.DragEnded += (valueChanged) => _isDraggingSlider = false;

                TriggerToolSelection(Realm.Client.Core.GameHost.EditorTool.Raise, _btnRaise);

                _feedbackLabel.Modulate = new Color(1, 1, 1, 0);
                Input.MouseMode = Input.MouseModeEnum.Visible;



                _entityPaletteController = new MapEditorEntityPaletteController(this, _containerCategorySelector, _btnAddObject);
                _generationDialog = new MapEditorGenerationDialog(this);

                _topBarController = new MapEditorTopBar(_btnBackToHub, _btnPublish, _btnSave, _btnLoad, _btnUndo, _btnRedo, _btnEditors, _statusLabel, _feedbackLabel);
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

                if (Realm.Client.Core.GameHost.Instance != null)
                {
                    UpdateRotationExternal(Realm.Client.Core.GameHost.Instance.EditorPlacementRotation);
                    UpdateScaleExternal(Realm.Client.Core.GameHost.Instance.EditorPlacementScale);
                    UpdateGridSnapExternal(Realm.Client.Core.GameHost.Instance.EditorSnapToGrid);
                    UpdatePasteRotationExternal(Realm.Client.Core.GameHost.Instance.EditorPasteRotation);
                    UpdatePasteReflectionExternal(Realm.Client.Core.GameHost.Instance.EditorPasteReflection);
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

                    if (SavedActiveTool == Realm.Client.Core.GameHost.EditorTool.PlaceUnit ||
                        SavedActiveTool == Realm.Client.Core.GameHost.EditorTool.PlaceProp ||
                        SavedActiveTool == Realm.Client.Core.GameHost.EditorTool.PlaceDecal)
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
                throw;
            }
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

        private void HideInspectorInfo()
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
            if (_accordionInspector != null) _accordionInspector.Visible = false;
            if (_viewModel != null)
            {
                _viewModel.HasInspectorSelection = false;
                _viewModel.InspectorTitle = "No Selection";
                _viewModel.InspectorPos = "Position: (0, 0)";
            }
            if (Realm.Client.Core.GameHost.Instance != null)
            {
                TriggerToolSelection(Realm.Client.Core.GameHost.Instance.ActiveEditorTool, _activeToolButton, Realm.Client.Core.GameHost.Instance.ActivePlaceId);
            }
        }

        private void SetInspectorVisibility()
        {
            if (_lblInfoText != null) _lblInfoText.Visible = false;
            if (_inspectorPanel != null) _inspectorPanel.Visible = true;
            if (_accordionInspector != null)
            {
                _accordionInspector.Visible = true;
                if (_btnHeaderInspector != null) _btnHeaderInspector.Text = "▼ Selected Object Inspector";
                if (_contentInspector != null) _contentInspector.Visible = true;
            }
        }

        private void GetObjectDetails(Node selected, out string typeStr, out string idStr, out string nameStr)
        {
            typeStr = "";
            nameStr = selected.Name;
            idStr = null;

            if (selected is Realm.Client.Unit3D unit)
            {
                typeStr = unit.IsBuilding ? "BUILDING" : "UNIT";
                idStr = System.IO.Path.GetFileName(unit.UnitId).ToUpper();
                if (unit.IsResource && Realm.Client.Core.GameHost.ResourceRegistry.TryGetValue(unit.UnitId, out var resMeta) && !string.IsNullOrEmpty(resMeta.Name))
                    nameStr = resMeta.Name.ToUpper();
                else if (unit.IsBuilding && Realm.Client.Core.GameHost.BuildingRegistry.TryGetValue(unit.UnitId, out var bldMeta) && !string.IsNullOrEmpty(bldMeta.Name))
                    nameStr = bldMeta.Name.ToUpper();
                else if (Realm.Client.Core.GameHost.UnitRegistry.TryGetValue(unit.UnitId, out var unitMeta) && !string.IsNullOrEmpty(unitMeta.Name))
                    nameStr = unitMeta.Name.ToUpper();
                else
                    nameStr = idStr;
            }
            else if (selected is Realm.Client.Prop3D prop)
            {
                typeStr = "PROP";
                idStr = System.IO.Path.GetFileName(prop.PropId).ToUpper();
                if (Realm.Client.Core.GameHost.ResourceRegistry.TryGetValue(prop.PropId, out var propResMeta) && !string.IsNullOrEmpty(propResMeta.Name))
                    nameStr = propResMeta.Name.ToUpper();
                else if (Realm.Client.Core.GameHost.PropRegistry.TryGetValue(prop.PropId, out var propMeta) && !string.IsNullOrEmpty(propMeta.Name))
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

        public void UpdateSelectedObjectInfo()
        {
            if (Realm.Client.Core.GameHost.Instance == null) return;
            var selected = Realm.Client.Core.GameHost.Instance.SelectedEditorObject;
            
            if (!GodotObject.IsInstanceValid(selected))
            {
                HideInspectorInfo();
                return;
            }

            SetInspectorVisibility();

            Vector3 pos = selected is Node3D node3D ? node3D.Position : Vector3.Zero;
            Vector3 rot = selected is Node3D node3DRot ? node3DRot.RotationDegrees : Vector3.Zero;
            Vector3 scale = selected is Node3D node3DScale ? node3DScale.Scale : Vector3.One;

            GetObjectDetails(selected, out string typeStr, out string idStr, out string nameStr);

            if (selected is Realm.Client.Unit3D unitOwner && _playerOwnerContainer != null && _optPlayerOwner != null)
            {
                _playerOwnerContainer.Visible = true;
                _isUpdatingInspectorUI = true;
                _optPlayerOwner.Selected = Mathf.Clamp(unitOwner.Player, 0, PlayerColorConfig.Palette.Length - 1);
                _isUpdatingInspectorUI = false;
            }
            else if (_playerOwnerContainer != null) _playerOwnerContainer.Visible = false;

            if (_btnShowCoverage != null)
            {
                bool isUnit = selected is Realm.Client.Unit3D;
                _btnShowCoverage.Visible = isUnit;
                if (isUnit && Realm.Client.Core.GameHost.Instance != null)
                {
                    _btnShowCoverage.SetPressedNoSignal(Realm.Client.Core.GameHost.Instance.EditorCoverageOverlayEnabled);
                    _btnShowCoverage.Text = Realm.Client.Core.GameHost.Instance.EditorCoverageOverlayEnabled ? TranslationServer.Translate("◉ VISION/ATTACK RANGES: ON") : TranslationServer.Translate("◉ VISION/ATTACK RANGES: OFF");
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

            bool isUnitCharacter = (selected is Realm.Client.Unit3D unitObj && !unitObj.IsBuilding);
            if (isUnitCharacter)
            {
                Node modelRoot = selected;
                if (selected is Realm.Client.Unit3D uObj && uObj.ModelNode != null) modelRoot = uObj.ModelNode;

                var validation = Realm.Client.Animation.SkeletonValidator.Validate(modelRoot);
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
                    _btnOpenAnimationPreview.TooltipText = validation.IsValid ? TranslationServer.Translate("Open animation preview turntable dialog") : TranslationServer.Translate("Animation preview is only available for compatible rigged meshes.");
                }
            }
            else
            {
                if (_rigStatusContainer != null) _rigStatusContainer.Visible = false;
                if (_btnOpenAnimationPreview != null) _btnOpenAnimationPreview.Visible = false;
            }

            string assetKey = Realm.Client.Core.GameHost.Instance.GetSelectedEntityOrAssetKey(selected);
            bool isDecal = selected is Decal || (Realm.Client.Core.GameHost.Instance != null && Realm.Client.Core.GameHost.Instance.FindDecalInParentChain(selected) != null);
            if (!string.IsNullOrEmpty(assetKey) && !isDecal && _btnOpenGlobalOverrides != null)
            {
                _btnOpenGlobalOverrides.Visible = true;
                _btnOpenGlobalOverrides.TooltipText = string.Format(TranslationServer.Translate("Edit global model scale, offsets, and shaders for {0}"), assetKey);
            }
            else if (_btnOpenGlobalOverrides != null) _btnOpenGlobalOverrides.Visible = false;

            if (selected is ProceduralVfxInstance3D && _btnEditVfx != null)
            {
                _btnEditVfx.Visible = true;
                _btnEditVfx.TooltipText = TranslationServer.Translate("Open Procedural VFX Studio to edit this effect");
            }
            else if (_btnEditVfx != null) _btnEditVfx.Visible = false;

            if (_btnEditAttachments != null)
            {
                bool canAttach = selected is Realm.Client.Unit3D;
                _btnEditAttachments.Visible = canAttach;
                if (canAttach) _btnEditAttachments.TooltipText = TranslationServer.Translate("Open Socket & VFX Attachment Studio for this unit or building");
            }
        }

        public void UpdateGridSnapExternal(bool snap)
        {
            if (_btnToggleSnap != null)
            {
                _btnToggleSnap.Text = snap ? TranslationServer.Translate($"{UnicodeIcons.TABLE} SNAP TO GRID: ON") : TranslationServer.Translate($"{UnicodeIcons.TABLE} SNAP TO GRID: OFF");
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

        public void UpdateMeasureToolExternal(bool active)
        {
            if (_btnTapeMeasure != null)
            {
                _btnTapeMeasure.Text = UnicodeIcons.RULER;
                _btnTapeMeasure.TooltipText = TranslationServer.Translate($"Tape Measure Tool: {(active ? "ACTIVE" : "INACTIVE")} (U)");
                _btnTapeMeasure.Modulate = active ? new Color(1.3f, 1.15f, 0.7f) : new Color(1f, 1f, 1f);
            }
        }

        private void SetupEnvLightingDropdown(OptionButton opt)
        {
            opt.Clear();
            opt.AddItem(TranslationServer.Translate($"{UnicodeIcons.SUN} Day"), 0);
            opt.AddItem(TranslationServer.Translate($"{UnicodeIcons.CLOUD_SUN} Dusk"), 1);
            opt.AddItem(TranslationServer.Translate($"{UnicodeIcons.MOON} Night"), 2);
            opt.AddItem(TranslationServer.Translate($"{UnicodeIcons.HORIZON_SUN} Dawn"), 3);
            StyleOptionButtonPopup(opt);
        }

        public void UpdateEnvLightingSelection(int timeOfDayIndex)
        {
            if (_optEnvLighting != null)
            {
                int idx = Mathf.Clamp(timeOfDayIndex, 0, 3);
                _optEnvLighting.Select(idx);
            }
        }

        private void SetEnvironmentLighting(int index)
        {
            if (Realm.Client.Core.GameHost.Instance != null)
            {
                var res = Realm.Client.Core.GameHost.Instance.SetTimeOfDay(index);
                string timeName = Realm.Client.Core.GameHost.Instance.EnvironmentService?.GetTimeOfDayName(res.TimeOfDayIndex) ?? "Day";
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
            if (Realm.Client.Core.GameHost.Instance != null && Realm.Client.Core.GameHost.Instance.EnvironmentService != null)
            {
                string weatherType = index switch
                {
                    1 => "rain",
                    2 => "snow",
                    3 => "fog",
                    _ => "clear"
                };
                string applied = Realm.Client.Core.GameHost.Instance.EnvironmentService.SetWeather(weatherType, Realm.Client.Core.GameHost.Instance);
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

        public void ToggleVSCodeEditor()
        {
            if (OperatingSystem.IsWindows())
            {
                if (Realm.Client.VSCodeManager.Instance.IsVisible)
                {
                    Realm.Client.VSCodeManager.Instance.Focus();
                }
                else
                {
                    GenerateVSCodeFilesExternal();
                    Realm.Client.VSCodeManager.Instance.SetVisible(true);
                }
            }
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
            if (Realm.Client.Core.GameHost.Instance == null)
            {
                ShowFeedback("[Debug] DeleteSelectedObject: Realm.Client.Core.GameHost.Instance is NULL!");
                return;
            }
            var selected = Realm.Client.Core.GameHost.Instance.SelectedEditorObject;
            if (GodotObject.IsInstanceValid(selected))
            {
                Vector3 pos = (selected is Node3D n) ? n.Position : Vector3.Zero;
                Realm.Client.Core.GameHost.Instance.SelectedEditorObject = null;
                var action = Realm.Client.Core.GameHost.Instance.DeleteObjectAtWithUndo(selected, pos);
                if (action != null)
                {
                    EditorHistoryManager.RecordAction(action);
                }
                else
                {
                    Realm.Client.Core.GameHost.Instance.DeleteNodeExternal(selected);
                }
                _instanceManagerDialog?.RefreshIfOpen();
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
            if (Realm.Client.Core.GameHost.Instance != null)
            {
                Realm.Client.Core.GameHost.Instance.PlaceUnitIsEnemy = isEnemy;
            }
        }

        public void SelectCategoryItemExternal(string category, string filename)
        {
            _entityPaletteController?.SelectCategoryItemExternal(category, filename);
        }

        public void SelectPickedDecal(string decalId)
        {
            _entityPaletteController?.SelectCategoryItemExternal("Decals", decalId);
        }

        public bool IsApplyGroundTextureEnabled()
        {
            if (Realm.Client.Core.GameHost.Instance != null)
            {
                var tool = Realm.Client.Core.GameHost.Instance.ActiveEditorTool;
                bool isPaintTool = tool == Realm.Client.Core.GameHost.EditorTool.PaintTexture;
                if (isPaintTool && _chkApplyGroundTexture != null && _chkApplyGroundTexture.Visible)
                {
                    return _chkApplyGroundTexture.ButtonPressed;
                }
            }
            return true;
        }

        public bool IsApplyCliffTextureEnabled()
        {
            if (Realm.Client.Core.GameHost.Instance != null)
            {
                var tool = Realm.Client.Core.GameHost.Instance.ActiveEditorTool;
                bool isPaintTool = tool == Realm.Client.Core.GameHost.EditorTool.PaintTexture;
                if (isPaintTool && _chkApplyCliffTexture != null && _chkApplyCliffTexture.Visible)
                {
                    return _chkApplyCliffTexture.ButtonPressed;
                }
            }
            return true;
        }
        public void UpdateBrushShapeExternal(bool isSquare)
        {
            if (_hasLastBrushShape && _lastBrushShapeIsSquare == isSquare) return;
            _hasLastBrushShape = true;
            _lastBrushShapeIsSquare = isSquare;
            if (_btnBrushShape != null)
            {
                _btnBrushShape.Text = isSquare ? TranslationServer.Translate($"{UnicodeIcons.SQUARE} BRUSH: SQUARE") : TranslationServer.Translate($"{UnicodeIcons.CIRCLE} BRUSH: CIRCLE");
            }
            if (_btnClipboardBrushShape != null)
            {
                _btnClipboardBrushShape.Text = isSquare ? TranslationServer.Translate($"{UnicodeIcons.SQUARE} BRUSH: SQUARE") : TranslationServer.Translate($"{UnicodeIcons.CIRCLE} BRUSH: CIRCLE");
            }
            if (Realm.Client.Core.GameHost.Instance != null && Realm.Client.Core.GameHost.Instance.GroundTerrain != null && _editorService != null && _editorService.SelectionStart != null && _editorService.SelectionEnd != null)
            {
                var (minX, minZ, maxX, maxZ) = _editorService.GetCurrentSelectionBounds();
                Realm.Client.Core.GameHost.Instance.RebuildSelectionHighlightMeshExternal(minX, minZ, maxX, maxZ);
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

        public void SelectToolFromHotkey(Realm.Client.Core.GameHost.EditorTool tool)
        {
            Button targetBtn = null;
            switch (tool)
            {
                case Realm.Client.Core.GameHost.EditorTool.Raise: targetBtn = _btnRaise; break;
                case Realm.Client.Core.GameHost.EditorTool.Lower: targetBtn = _btnLower; break;
                case Realm.Client.Core.GameHost.EditorTool.Height: targetBtn = _btnHeight; break;
                case Realm.Client.Core.GameHost.EditorTool.Smooth: targetBtn = _btnSmooth; break;
                case Realm.Client.Core.GameHost.EditorTool.Plateau: targetBtn = _btnPlateau; break;
                case Realm.Client.Core.GameHost.EditorTool.Ramp: targetBtn = _btnRamp; break;
                case Realm.Client.Core.GameHost.EditorTool.Noise: targetBtn = _btnNoise; break;
                case Realm.Client.Core.GameHost.EditorTool.Water: targetBtn = _btnWater; break;
                case Realm.Client.Core.GameHost.EditorTool.PaintTexture: targetBtn = _btnTextureBrush; break;
                case Realm.Client.Core.GameHost.EditorTool.FloodFill: targetBtn = _btnFloodFill; break;
                case Realm.Client.Core.GameHost.EditorTool.PaintPathing: targetBtn = _btnPathingBrush; break;
                case Realm.Client.Core.GameHost.EditorTool.FloodFillPathing: targetBtn = _btnFloodFillPathing; break;
                case Realm.Client.Core.GameHost.EditorTool.DrawCoordinate: targetBtn = _btnDrawCoordinate; break;
                case Realm.Client.Core.GameHost.EditorTool.SelectArea: targetBtn = _btnSelectArea; break;
                case Realm.Client.Core.GameHost.EditorTool.PasteArea: targetBtn = _btnPaste; break;
                case Realm.Client.Core.GameHost.EditorTool.PlaceUnit:
                case Realm.Client.Core.GameHost.EditorTool.PlaceProp:
                case Realm.Client.Core.GameHost.EditorTool.PlaceDecal:
                    targetBtn = _btnAddObject;
                    break;
                case Realm.Client.Core.GameHost.EditorTool.DeleteObject: targetBtn = _btnDeleteObject; break;
                case Realm.Client.Core.GameHost.EditorTool.SelectMove: targetBtn = _btnSelectMove; break;
                case Realm.Client.Core.GameHost.EditorTool.Eyedropper: targetBtn = _btnEyedropper; break;
                case Realm.Client.Core.GameHost.EditorTool.Measure: targetBtn = _btnTapeMeasure; break;
            }
            if (targetBtn != null)
            {
                TriggerToolSelection(tool, targetBtn, Realm.Client.Core.GameHost.Instance?.ActivePlaceId ?? "");
            }
        }

        private Button GetButtonForTool(Realm.Client.Core.GameHost.EditorTool tool, string placeId)
        {
            return tool switch
            {
                Realm.Client.Core.GameHost.EditorTool.Raise => _btnRaise,
                Realm.Client.Core.GameHost.EditorTool.Lower => _btnLower,
                Realm.Client.Core.GameHost.EditorTool.Height => _btnHeight,
                Realm.Client.Core.GameHost.EditorTool.Smooth => _btnSmooth,
                Realm.Client.Core.GameHost.EditorTool.Plateau => _btnPlateau,
                Realm.Client.Core.GameHost.EditorTool.Ramp => _btnRamp,
                Realm.Client.Core.GameHost.EditorTool.Noise => _btnNoise,
                Realm.Client.Core.GameHost.EditorTool.Water => _btnWater,
                Realm.Client.Core.GameHost.EditorTool.PaintPathing => _btnPathingBrush,
                Realm.Client.Core.GameHost.EditorTool.FloodFillPathing => _btnFloodFillPathing,
                Realm.Client.Core.GameHost.EditorTool.DrawCoordinate => _btnDrawCoordinate,
                Realm.Client.Core.GameHost.EditorTool.SelectArea => _btnSelectArea,
                Realm.Client.Core.GameHost.EditorTool.SelectMove => _btnSelectMove,
                Realm.Client.Core.GameHost.EditorTool.DeleteObject => _btnDeleteObject,
                Realm.Client.Core.GameHost.EditorTool.PaintTexture => GetTextureSwatchButton(placeId),
                Realm.Client.Core.GameHost.EditorTool.FloodFill => _btnFloodFill,
                Realm.Client.Core.GameHost.EditorTool.PlacePropClump => _btnClumpBrush,
                Realm.Client.Core.GameHost.EditorTool.Eyedropper => _btnEyedropper,
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
                Realm.Client.UI.UIManager.Instance?.PlayClickSound();
                popup.QueueFree();
                tcs.TrySetResult(false);
            };

            upgradeBtn.Pressed += async () =>
            {
                Realm.Client.UI.UIManager.Instance?.PlayClickSound();
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
                Realm.Client.UI.UIManager.Instance?.PlayClickSound();
                popup.QueueFree();
            };
        }
        private void InitializePublishDialog(out Panel popup, out VBoxContainer vbox, out ProgressBar progressBar, out Label statusLabel, out Button closeBtn)
        {
            popup = new Panel();
            popup.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
            popup.AddThemeStyleboxOverride("panel", UIStyle.CreateBgGradient());
            popup.ZIndex = 1100;
            AddChild(popup);

            var cardPanel = new Panel();
            cardPanel.CustomMinimumSize = new Vector2(580, 280);
            cardPanel.SetAnchorsAndOffsetsPreset(LayoutPreset.Center);
            cardPanel.AddThemeStyleboxOverride("panel", UIStyle.CreateStonePanel(true));
            popup.AddChild(cardPanel);

            vbox = new VBoxContainer();
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

            string mapTitle = GetMapNameFromMetadata();
            if (string.IsNullOrWhiteSpace(mapTitle) || mapTitle.Equals("Untitled Map", StringComparison.OrdinalIgnoreCase)) mapTitle = "UntitledMap";
            string mapVersion = GetMapVersionFromMetadata();
            if (string.IsNullOrWhiteSpace(mapVersion)) mapVersion = "1.0.0";

            var descLabel = new Label();
            descLabel.Text = string.Format(TranslationServer.Translate("Map: {0} (v{1})"), mapTitle, mapVersion);
            descLabel.HorizontalAlignment = HorizontalAlignment.Center;
            descLabel.AddThemeFontSizeOverride("font_size", 13);
            descLabel.AddThemeColorOverride("font_color", UIStyle.ColorGold);
            vbox.AddChild(descLabel);

            vbox.AddChild(new Control { CustomMinimumSize = new Vector2(0, 5) });

            progressBar = new ProgressBar();
            progressBar.CustomMinimumSize = new Vector2(480, 24);
            progressBar.MinValue = 0;
            progressBar.MaxValue = 100;
            progressBar.Value = 0;
            vbox.AddChild(progressBar);

            statusLabel = new Label();
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

            closeBtn = new Button();
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
        }

        private async void PublishMapAction(bool fullExport = false)
        {
            if (Realm.Client.Core.GameHost.Instance == null || _isSyncing) return;
            _isSyncing = true;
            if (_editorService != null)
            {
                _editorService.IsPaused = true;
            }

            string workspace = !string.IsNullOrEmpty(_tempWorkspacePath) ? _tempWorkspacePath : Realm.Client.Services.MapWorkspaceService.GetActiveWorkspacePath();

            var (assetsValid, missingAssets) = MapAssetHelper.ValidateWorkspaceAssets(workspace);
            if (!assetsValid)
            {
                ShowFeedback(string.Format(TranslationServer.Translate("Publish failed: Map is missing required asset files:\n{0}"), string.Join(", ", missingAssets.Take(4)) + (missingAssets.Count > 4 ? "..." : "")));
                AppendWasmConsoleLog($"[ERROR] Publish failed. Missing required asset files:\n  {string.Join("\n  ", missingAssets)}");
                _isSyncing = false;
                if (_editorService != null) _editorService.IsPaused = false;
                return;
            }

            var (sizesValid, oversizedAssets) = MapAssetHelper.ValidateWorkspaceAssetSizes(workspace);
            if (!sizesValid)
            {
                string oversizedSummary = string.Join("\n", oversizedAssets.Take(4).Select(o => $"• {o.RelativePath} ({o.SizeMB:F2} MB > 15 MB)")) + (oversizedAssets.Count > 4 ? "\n..." : "");
                ShowFeedback(string.Format(TranslationServer.Translate("Publish failed: Assets exceed maximum 15 MB size limit:\n{0}"), oversizedSummary));
                AppendWasmConsoleLog($"[ERROR] Publish failed. Assets exceed maximum 15 MB size limit:\n{string.Join("\n", oversizedAssets.Select(o => $"  {o.RelativePath} ({o.SizeMB:F2} MB)"))}");
                _isSyncing = false;
                if (_editorService != null) _editorService.IsPaused = false;
                return;
            }

            string mapTitle = GetMapNameFromMetadata();
            if (string.IsNullOrWhiteSpace(mapTitle) || mapTitle.Equals("Untitled Map", StringComparison.OrdinalIgnoreCase)) mapTitle = "UntitledMap";

            string mapVersion = GetMapVersionFromMetadata();
            if (string.IsNullOrWhiteSpace(mapVersion)) mapVersion = "1.0.0";

            string metaJsonPath = System.IO.Path.Combine(workspace, "metadata.json");
            string activeConfigPath = metaJsonPath;
            string tempTerrainPath = System.IO.Path.Combine(workspace, "terrain.json");
            string manifestJsonPath = System.IO.Path.Combine(workspace, "manifest.json");

            InitializePublishDialog(out Panel popup, out VBoxContainer vbox, out ProgressBar progressBar, out Label statusLabel, out Button closeBtn);

            closeBtn.Pressed += () =>
            {
                Realm.Client.UI.UIManager.Instance?.PlayClickSound();
                if (GodotObject.IsInstanceValid(popup))
                {
                    popup.QueueFree();
                }
            };

            _wasmHasErrors = false;
            Action<string> logHandler = line => AppendWasmConsoleLog(line);
            Core.WasmRuntime.OnWasmLog += logHandler;

            try
            {
                progressBar.Value = 5;
                statusLabel.Text = TranslationServer.Translate("Checking publish eligibility...");
                await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);

                string seedServerUrl = Realm.Client.Core.GameHost.Instance != null && GodotObject.IsInstanceValid(Realm.Client.Network.LobbyManager.Instance) ? Realm.Client.Network.LobbyManager.Instance.RegistryServerUrl : ServersConfigHelper.GetDefaultServerUrl();
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

                Realm.Client.Services.MapWorkspaceService.EnsureLicenseFile(workspace);
                Realm.Client.Core.GameHost.Instance.SaveMapToFile(tempTerrainPath, performReload: false);
                Realm.Client.Core.GameHost.Instance.EditorHasUnsavedChanges = false;
                InvalidateMetadataCache();

                if (OperatingSystem.IsWindows())
                {
                    await Realm.Client.VSCodeManager.Instance.SaveAllOpenFilesAsync();
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

                await Realm.Client.Services.MapWorkspaceService.EnsureGlbAssetsOptimizedCooperativeAsync(workspace, async (current, total, fileName) =>
                {
                    float fraction = total > 0 ? (float)current / total : 1.0f;
                    progressBar.Value = 35 + fraction * 15;
                    statusLabel.Text = string.Format(TranslationServer.Translate("Optimizing 3D model {0}/{1}: {2}..."), current, total, fileName);
                    await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
                });

                progressBar.Value = 50;
                statusLabel.Text = TranslationServer.Translate("Converting textures...");
                await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);

                await Realm.Client.Services.MapWorkspaceService.EnsurePngAssetsConvertedCooperativeAsync(workspace, async (current, total, fileName) =>
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
                string currentUsername = Realm.Client.Network.LobbyManager.Instance?.AuthenticatedUsername ?? "MapAuthor";
                string pubKeyBase64 = Convert.ToBase64String(authorshipKey.PublicKey.Export(KeyBlobFormat.RawPublicKey));

                Realm.Client.Services.MapWorkspaceService.NormalizeMetadataTextureEntries(workspace);

                if (System.IO.File.Exists(activeConfigPath))
                {
                    try
                    {
                        var metaDoc = JsonNode.Parse(System.IO.File.ReadAllText(activeConfigPath)) as JsonObject;
                        if (metaDoc != null)
                        {
                            metaDoc["EngineVersion"] = RealmVersion.GameBinaryVersion;
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
                    Realm.Client.UI.UIManager.Instance?.ShowConfirmationDialog(
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
                Core.WasmRuntime.OnWasmLog -= logHandler;
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

        public void AppendWasmConsoleLog(string line)
        {
            Realm.Client.UI.WasmConsoleWindow.Instance.AppendLog(line);
            GD.Print("[WASM_BUILD] " + line);
        }

        private void SetupMirrorDropdown(OptionButton opt, string tooltip = "")
        {
            if (opt == null) return;
            opt.Clear();
            opt.Set("icon_max_width", 0);
            opt.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
            opt.ClipText = true;

            int folds = Realm.Client.Core.GameHost.Instance != null ? Realm.Client.Core.GameHost.Instance.EditorSymmetryFolds : 4;
            opt.AddItem(TranslationServer.Translate($"{UnicodeIcons.BAN} SYMMETRY: NONE"), (int)MirrorMode.None);
            opt.AddItem(TranslationServer.Translate($"{UnicodeIcons.ARROWS_V} MIRROR: VERTICAL"), (int)MirrorMode.Vertical);
            opt.AddItem(TranslationServer.Translate($"{UnicodeIcons.ARROWS_H} MIRROR: HORIZONTAL"), (int)MirrorMode.Horizontal);
            opt.AddItem(TranslationServer.Translate($"{UnicodeIcons.GRID_QUAD} MIRROR: QUAD"), (int)MirrorMode.Both);
            opt.AddItem(string.Format(TranslationServer.Translate($"{UnicodeIcons.ROTATE} ROTATIONAL ({0}-FOLD)"), folds), (int)MirrorMode.Rotational);

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
            if (Realm.Client.Core.GameHost.Instance == null) return;
            Realm.Client.Core.GameHost.Instance.EditorMirrorMode = mode;
            Realm.Client.Core.GameHost.Instance.UpdateSymmetryPivotVisuals();
            Realm.Client.Core.GameHost.Instance.InvalidateSelectionHighlightMesh();
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
            if (Realm.Client.Core.GameHost.Instance == null) return;
            int folds = Realm.Client.Core.GameHost.Instance.EditorSymmetryFolds;
            var mode = Realm.Client.Core.GameHost.Instance.EditorMirrorMode;

            UpdateSingleMirrorDropdown(_optMirrorMode, mode, folds);
            UpdateSingleMirrorDropdown(_optPlacementMirrorMode, mode, folds);
            UpdateSingleMirrorDropdown(_optClipboardMirrorMode, mode, folds);

            UpdateSymmetrySubControlsVisibility();
        }

        private void UpdateSingleMirrorDropdown(OptionButton opt, MirrorMode mode, int folds)
        {
            if (opt == null) return;

            string rotText = string.Format(TranslationServer.Translate($"{UnicodeIcons.ROTATE} ROTATIONAL ({0}-FOLD)"), folds);
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

        private void SetSymmetrySpokes(int spokes)
        {
            if (Realm.Client.Core.GameHost.Instance == null) return;
            Realm.Client.Core.GameHost.Instance.EditorSymmetryFolds = spokes;
            Realm.Client.Core.GameHost.Instance.InvalidateSelectionHighlightMesh();
            UpdateSymmetrySpokesDropdowns();
            UpdateMirrorDropdowns();
            UpdatePolarRadialStepButtonText();
            ShowFeedback(string.Format(TranslationServer.Translate("Symmetry Spokes: {0}-way"), spokes));
        }

        public void UpdateSymmetrySpokesDropdowns()
        {
            UpdatePolarRadialStepButtonText();
        }

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
                opt.AddItem(string.Format(TranslationServer.Translate($"{UnicodeIcons.CIRCLE} RINGS: {0:F0}T"), spacing), (int)spacing);
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
            if (Realm.Client.Core.GameHost.Instance == null) return;
            Realm.Client.Core.GameHost.Instance.EditorPolarRingSpacing = spacing;
            Realm.Client.Core.GameHost.Instance.GroundTerrain?.SetPolarRingSpacing(spacing);
            UpdatePolarRingSpacingButtonText();
            ShowFeedback(string.Format(TranslationServer.Translate("Polar Ring Spacing: {0:F0} tiles"), spacing));
        }

        private void SetPolarSpokes(int spokes)
        {
            if (Realm.Client.Core.GameHost.Instance == null) return;
            if (Realm.Client.Core.GameHost.Instance.EditorMirrorMode == MirrorMode.Rotational)
            {
                SetSymmetrySpokes(spokes);
                return;
            }
            Realm.Client.Core.GameHost.Instance.EditorPolarSpokeFolds = spokes;
            Realm.Client.Core.GameHost.Instance.EditorPolarRadialStep = 360.0f / spokes;
            Realm.Client.Core.GameHost.Instance.GroundTerrain?.SetPolarRadialStep(Realm.Client.Core.GameHost.Instance.EditorPolarRadialStep);
            UpdatePolarRadialStepButtonText();
            ShowFeedback(string.Format(TranslationServer.Translate("Polar Spokes: {0}-way"), spokes));
        }

        private void SetPivotToCursor()
        {
            if (Realm.Client.Core.GameHost.Instance == null || Realm.Client.Core.GameHost.Instance.GroundTerrain == null) return;
            var viewport = GetViewport();
            Vector3 hitPos = Vector3.Zero;
            bool hasHit = false;
            if (viewport != null)
            {
                hasHit = Realm.Client.Core.GameHost.Instance.TryRaycastTerrainFromMousePosition(viewport.GetMousePosition(), out hitPos);
            }
            if (!hasHit)
            {
                hitPos = Realm.Client.Core.GameHost.Instance.MainCamera?.GlobalPosition ?? Vector3.Zero;
            }
            var pivot = new Vector2(hitPos.X, hitPos.Z);
            Realm.Client.Core.GameHost.Instance.EditorSymmetryPivot = pivot;
            Realm.Client.Core.GameHost.Instance.GroundTerrain.SetPolarCenter(pivot);
            Realm.Client.Core.GameHost.Instance.UpdateSymmetryPivotVisuals();
            var (cx, cz) = _editorService != null ? _editorService.WorldPosToCellCoords(hitPos) : (0, 0);
            ShowFeedback(string.Format(TranslationServer.Translate("Symmetry & Polar Pivot set to tile ({0}, {1})"), cx, cz));
        }

        private void ResetPivotToMapCenter()
        {
            if (Realm.Client.Core.GameHost.Instance == null || Realm.Client.Core.GameHost.Instance.GroundTerrain == null) return;
            var pivot = Vector2.Zero;
            Realm.Client.Core.GameHost.Instance.EditorSymmetryPivot = pivot;
            Realm.Client.Core.GameHost.Instance.GroundTerrain.SetPolarCenter(pivot);
            Realm.Client.Core.GameHost.Instance.UpdateSymmetryPivotVisuals();
            Realm.Client.Core.GameHost.Instance.InvalidateSelectionHighlightMesh();
            ShowFeedback(TranslationServer.Translate("Symmetry & Polar Pivot reset to Map Center"));
        }

        public void SwitchModule(EditorModule module)
        {
            _activeModule = module;
            UpdateModuleSwitchButtons();

            if (module != EditorModule.Coordinates)
            {
                Realm.Client.Core.GameHost.Instance?.HideCoordinateSelectionOutline();
            }

            UpdatePanelVisibilityForModule(module);

            if (Realm.Client.Core.GameHost.Instance != null)
            {
                switch (module)
                {
                    case EditorModule.Terrain:
                        TriggerToolSelection(Realm.Client.Core.GameHost.EditorTool.Raise, _btnRaise);
                        break;
                    case EditorModule.TextureDeco:
                        TriggerToolSelection(Realm.Client.Core.GameHost.EditorTool.PaintTexture, _btnTextureBrush);
                        break;
                    case EditorModule.Pathing:
                        TriggerToolSelection(Realm.Client.Core.GameHost.EditorTool.PaintPathing, _btnPathingBrush);
                        break;
                    case EditorModule.Objects:
                        _entityPaletteController?.TriggerAddObjectMode();
                        break;
                    case EditorModule.Coordinates:
                        TriggerToolSelection(Realm.Client.Core.GameHost.EditorTool.DrawCoordinate, _btnDrawCoordinate);
                        break;
                    case EditorModule.Clipboard:
                        TriggerToolSelection(Realm.Client.Core.GameHost.EditorTool.SelectArea, _btnSelectArea);
                        break;
                }
            }
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
                        _faFontVariation.Fallbacks = new global::Godot.Collections.Array<Font> { faFont };
                    }
                }
                catch
                {
                    _faFontVariation = null;
                }
            }
            return _faFontVariation;
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

                var saveGroupRow = new HBoxContainer();
                saveGroupRow.Name = "RowSaveGroup";
                saveGroupRow.AddThemeConstantOverride("separation", 0);
                saveGroupRow.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
                saveGroupRow.SizeFlagsStretchRatio = 1.0f;

                _btnSave.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
                _btnSave.SizeFlagsStretchRatio = 1.0f;

                _btnSaveMore.CustomMinimumSize = new Vector2(24, 30);
                _btnSaveMore.SizeFlagsHorizontal = Control.SizeFlags.ShrinkEnd;

                SafeReparent(_btnLoad, saveLoadRow);
                SafeReparent(_btnSave, saveGroupRow);
                SafeReparent(_btnSaveMore, saveGroupRow);
                saveLoadRow.AddChild(saveGroupRow);

                var fileGrid2 = new GridContainer();
                fileGrid2.Columns = 2;
                fileGrid2.AddThemeConstantOverride("h_separation", 6);
                fileGrid2.AddThemeConstantOverride("v_separation", 6);
                fileGrid2.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;

                SafeReparent(_btnRandomGen, fileGrid2);
                SafeReparent(_btnResetMap, fileGrid2);

                var fileBox1 = new VBoxContainer();
                fileBox1.Name = "BoxFileOps";
                fileBox1.AddThemeConstantOverride("separation", 6);
                fileBox1.AddChild(saveLoadRow);
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

                StyleIconButton(_btnToggleGrid, UnicodeIcons.BORDER_ALL, "Overlay");
                StyleIconButton(_btnToggleEnvironment, UnicodeIcons.SUN, "Environment");
                StyleIconButton(_btnToggleCamera, UnicodeIcons.CAMERA, "Configure camera controls (R / C / + / - / F8)");
                StyleIconButton(_btnTapeMeasure, UnicodeIcons.RULER, "Tape measure distance & slope tool (U)");

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

                bool isPolarMode = Realm.Client.Core.GameHost.Instance != null &&
                                   (Realm.Client.Core.GameHost.Instance.EditorMirrorMode == MirrorMode.Rotational || Realm.Client.Core.GameHost.Instance.EditorGridMode == Realm.Client.Core.GameHost.GridOverlayMode.Polar || Realm.Client.Core.GameHost.Instance.EditorGridMode == Realm.Client.Core.GameHost.GridOverlayMode.Both);
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

        private void UpdateSidebarMorph(Realm.Client.Core.GameHost.EditorTool tool)
        {
            if (_panelRight == null) return;

            bool isClumpActive = _chkClumpMode != null && _chkClumpMode.ButtonPressed;
            bool isBrush = (tool == Realm.Client.Core.GameHost.EditorTool.Raise ||
                            tool == Realm.Client.Core.GameHost.EditorTool.Lower ||
                            tool == Realm.Client.Core.GameHost.EditorTool.Height ||
                            tool == Realm.Client.Core.GameHost.EditorTool.Smooth ||
                            tool == Realm.Client.Core.GameHost.EditorTool.Plateau ||
                            tool == Realm.Client.Core.GameHost.EditorTool.Ramp ||
                            tool == Realm.Client.Core.GameHost.EditorTool.PaintTexture ||
                            tool == Realm.Client.Core.GameHost.EditorTool.Noise ||
                            tool == Realm.Client.Core.GameHost.EditorTool.PaintPathing ||
                            tool == Realm.Client.Core.GameHost.EditorTool.FloodFillPathing ||
                            tool == Realm.Client.Core.GameHost.EditorTool.PlacePropClump ||
                            ((tool == Realm.Client.Core.GameHost.EditorTool.PlaceUnit || tool == Realm.Client.Core.GameHost.EditorTool.PlaceProp || tool == Realm.Client.Core.GameHost.EditorTool.PlaceDecal) && isClumpActive))
                           && tool != Realm.Client.Core.GameHost.EditorTool.Water;

            if (_accordionBrush != null)
            {
                _accordionBrush.Visible = isBrush;
                UpdateBrushStrengthVisibility();
                bool isTextureMode = tool == Realm.Client.Core.GameHost.EditorTool.PaintTexture;
                if (_chkBlockMode != null)
                {
                    _chkBlockMode.Visible = (tool != Realm.Client.Core.GameHost.EditorTool.PaintPathing &&
                                             tool != Realm.Client.Core.GameHost.EditorTool.FloodFillPathing &&
                                             !isTextureMode &&
                                             tool != Realm.Client.Core.GameHost.EditorTool.Smooth &&
                                             tool != Realm.Client.Core.GameHost.EditorTool.Noise &&
                                             tool != Realm.Client.Core.GameHost.EditorTool.Ramp &&
                                             tool != Realm.Client.Core.GameHost.EditorTool.PlacePropClump &&
                                             tool != Realm.Client.Core.GameHost.EditorTool.Height &&
                                             !isClumpActive);
                }
                UpdateBlockStepVisibility();
            }

            if (_accordionWater != null)
            {
                _accordionWater.Visible = (tool == Realm.Client.Core.GameHost.EditorTool.Water);
            }

            bool isBlockModeActive = (_chkBlockMode != null && _chkBlockMode.Visible && _chkBlockMode.ButtonPressed) || (Realm.Client.Core.GameHost.Instance != null && Realm.Client.Core.GameHost.Instance.EditorBlockMode);
            bool isPaintTool = tool == Realm.Client.Core.GameHost.EditorTool.PaintTexture ||
                               tool == Realm.Client.Core.GameHost.EditorTool.FloodFill;
            bool isBlockHeightTool = (isBlockModeActive && (
                                         tool == Realm.Client.Core.GameHost.EditorTool.Raise ||
                                         tool == Realm.Client.Core.GameHost.EditorTool.Lower ||
                                         tool == Realm.Client.Core.GameHost.EditorTool.Plateau)) ||
                                     tool == Realm.Client.Core.GameHost.EditorTool.Height;

            bool isRampTool = tool == Realm.Client.Core.GameHost.EditorTool.Ramp;

            bool texSettingsVisible = _containerTextureSettings != null && (isPaintTool || isBlockHeightTool || isRampTool);
            if (_containerTextureSettings != null) _containerTextureSettings.Visible = texSettingsVisible;

            bool isTexturePaintToolOnly = tool == Realm.Client.Core.GameHost.EditorTool.PaintTexture;

            if (_chkApplyGroundTexture != null) _chkApplyGroundTexture.Visible = texSettingsVisible && isTexturePaintToolOnly;
            if (_chkApplyCliffTexture != null) _chkApplyCliffTexture.Visible = texSettingsVisible && isTexturePaintToolOnly;

            bool pathingSettingsVisible = _containerPathingSettings != null && (tool == Realm.Client.Core.GameHost.EditorTool.PaintPathing || tool == Realm.Client.Core.GameHost.EditorTool.FloodFillPathing);
            if (_containerPathingSettings != null) _containerPathingSettings.Visible = pathingSettingsVisible;

            bool eyedropperSettingsVisible = _containerEyedropperSettings != null && (tool == Realm.Client.Core.GameHost.EditorTool.Eyedropper);
            if (_containerEyedropperSettings != null) _containerEyedropperSettings.Visible = eyedropperSettingsVisible;

            bool pasteSettingsVisible = _containerPasteSettings != null && (tool == Realm.Client.Core.GameHost.EditorTool.SelectArea || tool == Realm.Client.Core.GameHost.EditorTool.PasteArea);
            if (_containerPasteSettings != null) _containerPasteSettings.Visible = pasteSettingsVisible;

            bool isPlacement = (tool == Realm.Client.Core.GameHost.EditorTool.PlaceUnit ||
                                tool == Realm.Client.Core.GameHost.EditorTool.PlaceProp ||
                                tool == Realm.Client.Core.GameHost.EditorTool.PlacePropClump ||
                                tool == Realm.Client.Core.GameHost.EditorTool.PlaceDecal ||
                                tool == Realm.Client.Core.GameHost.EditorTool.PlaceVfx);

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

            bool hasSelectedObject = Realm.Client.Core.GameHost.Instance != null && GodotObject.IsInstanceValid(Realm.Client.Core.GameHost.Instance.SelectedEditorObject);
            if (_accordionInspector != null)
            {
                _accordionInspector.Visible = hasSelectedObject && (tool == Realm.Client.Core.GameHost.EditorTool.SelectMove);
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
                if (keyEvent.Keycode == global::Godot.Key.F1)
                {
                    SwitchModule(EditorModule.Terrain);
                    GetViewport().SetInputAsHandled();
                }
                else if (keyEvent.Keycode == global::Godot.Key.F2)
                {
                    SwitchModule(EditorModule.TextureDeco);
                    GetViewport().SetInputAsHandled();
                }
                else if (keyEvent.Keycode == global::Godot.Key.F3)
                {
                    SwitchModule(EditorModule.Pathing);
                    GetViewport().SetInputAsHandled();
                }
                else if (keyEvent.Keycode == global::Godot.Key.F4)
                {
                    SwitchModule(EditorModule.Objects);
                    GetViewport().SetInputAsHandled();
                }
                else if (keyEvent.Keycode == global::Godot.Key.F5)
                {
                    SwitchModule(EditorModule.Coordinates);
                    GetViewport().SetInputAsHandled();
                }
                else if (keyEvent.Keycode == global::Godot.Key.F6)
                {
                    SwitchModule(EditorModule.Clipboard);
                    GetViewport().SetInputAsHandled();
                }
                else if (keyEvent.Keycode == global::Godot.Key.F7)
                {
                    if (Realm.Client.Core.GameHost.Instance != null && Realm.Client.Core.GameHost.Instance.GroundTerrain != null)
                    {
                        Realm.Client.Core.GameHost.Instance.GroundTerrain.ToggleWireframeMode();
                        bool isWireframe = GetViewport()?.DebugDraw == Viewport.DebugDrawEnum.Wireframe;
                        UpdateWireframeOverlayExternal(isWireframe);
                    }
                    GetViewport().SetInputAsHandled();
                }
                else if (keyEvent.Keycode == global::Godot.Key.F8)
                {
                    ToggleFreeCamera();
                    GetViewport().SetInputAsHandled();
                }
                else if (keyEvent.Keycode == global::Godot.Key.F9)
                {
                    ToggleShadows();
                    GetViewport().SetInputAsHandled();
                }
            }
        }

        private void RotateSelectedObject(float angleDelta)
        {
            if (Realm.Client.Core.GameHost.Instance == null) return;
            var selected = Realm.Client.Core.GameHost.Instance.SelectedEditorObject;
            if (GodotObject.IsInstanceValid(selected))
            {
                var node3D = selected as Node3D;
                Vector3 oldRot = node3D.RotationDegrees;
                Vector3 newRot = oldRot;
                newRot.Y = (newRot.Y + angleDelta + 360.0f) % 360.0f;
                bool isUnit = selected is Realm.Client.Unit3D;
                bool isEnemy = isUnit ? (selected as Realm.Client.Unit3D).IsEnemy : false;
                var action = new ObjectTransformAction(
                    node3D,
                    node3D.Position, node3D.Position,
                    oldRot, newRot,
                    node3D.Scale, node3D.Scale,
                    isEnemy, isEnemy
                );
                node3D.RotationDegrees = newRot;
                if (selected is Realm.Client.Prop3D propRot)
                {
                    Realm.Client.PropMultiMeshManager.Instance?.MarkDirty(propRot.PropId);
                }
                EditorHistoryManager.RecordAction(action);
                UpdateSelectedObjectInfo();
                ShowFeedback(string.Format(TranslationServer.Translate("Rotated Object to {0}°"), newRot.Y));
            }
        }

        private void ScaleSelectedObject(float scaleDelta)
        {
            if (Realm.Client.Core.GameHost.Instance == null) return;
            var selected = Realm.Client.Core.GameHost.Instance.SelectedEditorObject;
            if (GodotObject.IsInstanceValid(selected))
            {
                var node3D = selected as Node3D;
                Vector3 oldScale = node3D.Scale;
                float newScaleVal = Mathf.Clamp(oldScale.X + scaleDelta, 0.2f, 4.0f);
                Vector3 newScale = Vector3.One * newScaleVal;
                bool isUnit = selected is Realm.Client.Unit3D;
                bool isEnemy = isUnit ? (selected as Realm.Client.Unit3D).IsEnemy : false;
                var action = new ObjectTransformAction(
                    node3D,
                    node3D.Position, node3D.Position,
                    node3D.RotationDegrees, node3D.RotationDegrees,
                    oldScale, newScale,
                    isEnemy, isEnemy
                );
                node3D.Scale = newScale;
                if (selected is Realm.Client.Prop3D propScale)
                {
                    Realm.Client.PropMultiMeshManager.Instance?.MarkDirty(propScale.PropId);
                }
                EditorHistoryManager.RecordAction(action);
                UpdateSelectedObjectInfo();
                ShowFeedback(string.Format(TranslationServer.Translate("Scaled Object to {0:F1}x"), newScaleVal));
            }
        }

        private void ResetScaleSelectedObject()
        {
            if (Realm.Client.Core.GameHost.Instance == null) return;
            var selected = Realm.Client.Core.GameHost.Instance.SelectedEditorObject;
            if (GodotObject.IsInstanceValid(selected))
            {
                var node3D = selected as Node3D;
                Vector3 oldScale = node3D.Scale;
                Vector3 newScale = Vector3.One;
                bool isUnit = selected is Realm.Client.Unit3D;
                bool isEnemy = isUnit ? (selected as Realm.Client.Unit3D).IsEnemy : false;
                var action = new ObjectTransformAction(
                    node3D,
                    node3D.Position, node3D.Position,
                    node3D.RotationDegrees, node3D.RotationDegrees,
                    oldScale, newScale,
                    isEnemy, isEnemy
                );
                node3D.Scale = newScale;
                if (selected is Realm.Client.Prop3D propReset)
                {
                    Realm.Client.PropMultiMeshManager.Instance?.MarkDirty(propReset.PropId);
                }
                EditorHistoryManager.RecordAction(action);
                UpdateSelectedObjectInfo();
                ShowFeedback(TranslationServer.Translate("Reset Object scale to 1.0x"));
            }
        }

        public void LocateSelectedObjectAction()
        {
            if (Realm.Client.Core.GameHost.Instance == null) return;
            var selected = Realm.Client.Core.GameHost.Instance.SelectedEditorObject;
            if (GodotObject.IsInstanceValid(selected) && selected is Node3D node3D)
            {
                (Realm.Client.Core.GameHost.Instance.MainCamera as Realm.Client.CameraControl)?.FocusOnPosition(node3D.Position);
                ShowFeedback(string.Format(TranslationServer.Translate("Focused camera on {0}"), selected.Name));
            }
            else
            {
                ShowFeedback(TranslationServer.Translate("No object selected to locate"));
            }
        }

        private void DecreaseScaleDialogWidth()
        {
            if (_scaleDialogTargetWidth > 32)
            {
                _scaleDialogTargetWidth = Math.Max(32, (_scaleDialogTargetWidth % 32 == 0 ? _scaleDialogTargetWidth - 32 : (_scaleDialogTargetWidth / 32) * 32));
                UpdateScaleDialogLabels();
                Realm.Client.Core.GameHost.Instance?.ShowScaleMapSilhouette(_scaleDialogTargetWidth, _scaleDialogTargetDepth);
            }
        }

        private void IncreaseScaleDialogWidth()
        {
            if (_scaleDialogTargetWidth < 512)
            {
                _scaleDialogTargetWidth = Math.Min(512, (_scaleDialogTargetWidth / 32 + 1) * 32);
                UpdateScaleDialogLabels();
                Realm.Client.Core.GameHost.Instance?.ShowScaleMapSilhouette(_scaleDialogTargetWidth, _scaleDialogTargetDepth);
            }
        }

        private void DecreaseScaleDialogDepth()
        {
            if (_scaleDialogTargetDepth > 32)
            {
                _scaleDialogTargetDepth = Math.Max(32, (_scaleDialogTargetDepth % 32 == 0 ? _scaleDialogTargetDepth - 32 : (_scaleDialogTargetDepth / 32) * 32));
                UpdateScaleDialogLabels();
                Realm.Client.Core.GameHost.Instance?.ShowScaleMapSilhouette(_scaleDialogTargetWidth, _scaleDialogTargetDepth);
            }
        }

        private void IncreaseScaleDialogDepth()
        {
            if (_scaleDialogTargetDepth < 512)
            {
                _scaleDialogTargetDepth = Math.Min(512, (_scaleDialogTargetDepth / 32 + 1) * 32);
                UpdateScaleDialogLabels();
                Realm.Client.Core.GameHost.Instance?.ShowScaleMapSilhouette(_scaleDialogTargetWidth, _scaleDialogTargetDepth);
            }
        }

        private GridContainer CreateScaleDialogSizeGrid()
        {
            var sizeGrid = new GridContainer();
            sizeGrid.Columns = 3;
            sizeGrid.AddThemeConstantOverride("h_separation", 8);
            sizeGrid.AddThemeConstantOverride("v_separation", 6);

            _lblScalePreviewWidth = new Label();
            _lblScalePreviewWidth.AddThemeFontSizeOverride("font_size", 11);
            _lblScalePreviewWidth.AddThemeColorOverride("font_color", UIStyle.ColorCyanGlow);
            _lblScalePreviewWidth.SizeFlagsHorizontal = SizeFlags.ExpandFill;
            sizeGrid.AddChild(_lblScalePreviewWidth);

            var btnW_Dec = new Button();
            btnW_Dec.Set("icon_max_width", 0);
            SetupOptionButton(btnW_Dec, UnicodeIcons.MINUS, DecreaseScaleDialogWidth, 10, "Decrease target width");
            sizeGrid.AddChild(btnW_Dec);

            var btnW_Inc = new Button();
            btnW_Inc.Set("icon_max_width", 0);
            SetupOptionButton(btnW_Inc, UnicodeIcons.PLUS, IncreaseScaleDialogWidth, 10, "Increase target width");
            sizeGrid.AddChild(btnW_Inc);

            _lblScalePreviewHeight = new Label();
            _lblScalePreviewHeight.AddThemeFontSizeOverride("font_size", 11);
            _lblScalePreviewHeight.AddThemeColorOverride("font_color", UIStyle.ColorCyanGlow);
            _lblScalePreviewHeight.SizeFlagsHorizontal = SizeFlags.ExpandFill;
            sizeGrid.AddChild(_lblScalePreviewHeight);

            var btnH_Dec = new Button();
            btnH_Dec.Set("icon_max_width", 0);
            SetupOptionButton(btnH_Dec, UnicodeIcons.MINUS, DecreaseScaleDialogDepth, 10, "Decrease target depth");
            sizeGrid.AddChild(btnH_Dec);

            var btnH_Inc = new Button();
            btnH_Inc.Set("icon_max_width", 0);
            SetupOptionButton(btnH_Inc, UnicodeIcons.PLUS, IncreaseScaleDialogDepth, 10, "Increase target depth");
            sizeGrid.AddChild(btnH_Inc);
            return sizeGrid;
        }

        private HBoxContainer CreateScaleDialogButtonRow()
        {
            var buttonRow = new HBoxContainer();
            buttonRow.AddThemeConstantOverride("separation", 8);

            var btnCancel = new Button();
            btnCancel.Set("icon_max_width", 0);
            btnCancel.SizeFlagsHorizontal = SizeFlags.ExpandFill;
            SetupButton(btnCancel, "✖ CANCEL", () =>
            {
                Realm.Client.Core.GameHost.Instance?.HideScaleMapSilhouette();
                CloseScaleMapDialog();
            }, 11, "Cancel and discard the scale operation");
            buttonRow.AddChild(btnCancel);

            var btnApply = new Button();
            btnApply.Set("icon_max_width", 0);
            btnApply.SizeFlagsHorizontal = SizeFlags.ExpandFill;
            SetupButton(btnApply, "✔ APPLY", () =>
            {
                Realm.Client.Core.GameHost.Instance?.HideScaleMapSilhouette();
                Realm.Client.Core.GameHost.Instance?.ScaleMapExternal(_scaleDialogTargetWidth, _scaleDialogTargetDepth);
                CloseScaleMapDialog();
            }, 11, "Apply scale and stretch the entire map");
            buttonRow.AddChild(btnApply);
            return buttonRow;
        }

        public void OpenScaleMapDialog()
        {
            if (_scaleMapDialog != null) return;

            Realm.Client.Core.GameHost.Instance?.ShowScaleMapSilhouette(_scaleDialogTargetWidth, _scaleDialogTargetDepth);

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

            innerVBox.AddChild(CreateScaleDialogSizeGrid());
            UpdateScaleDialogLabels();

            var separator = new HSeparator();
            innerVBox.AddChild(separator);

            innerVBox.AddChild(CreateScaleDialogButtonRow());
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
                Realm.Client.UI.UIManager.Instance?.PlayClickSound();
                onClick?.Invoke();
            };
        }

        private void SelectCliffTexture(int index)
        {
            if (Realm.Client.Core.GameHost.Instance != null)
            {
                Realm.Client.Core.GameHost.Instance.EditorCliffPaintTextureIndex = index;
                if (!IsSwatchCompatibleTool(Realm.Client.Core.GameHost.Instance.ActiveEditorTool))
                {
                    TriggerToolSelection(Realm.Client.Core.GameHost.EditorTool.PaintTexture, _btnTextureBrush);
                }
                UpdateTextureLabels();

                string name = TranslationServer.Translate(_swatchDisplayNames[index]);
                ShowFeedback(string.Format(TranslationServer.Translate("Selected Cliff Face: {0}"), name));
            }
        }

        private static bool IsSwatchCompatibleTool(Realm.Client.Core.GameHost.EditorTool tool) => tool switch
        {
            Realm.Client.Core.GameHost.EditorTool.Raise => true,
            Realm.Client.Core.GameHost.EditorTool.Lower => true,
            Realm.Client.Core.GameHost.EditorTool.Height => true,
            Realm.Client.Core.GameHost.EditorTool.Smooth => true,
            Realm.Client.Core.GameHost.EditorTool.Plateau => true,
            Realm.Client.Core.GameHost.EditorTool.Noise => true,
            Realm.Client.Core.GameHost.EditorTool.Ramp => true,
            Realm.Client.Core.GameHost.EditorTool.Water => true,
            Realm.Client.Core.GameHost.EditorTool.PaintTexture => true,
            Realm.Client.Core.GameHost.EditorTool.FloodFill => true,
            Realm.Client.Core.GameHost.EditorTool.Eyedropper => true,
            Realm.Client.Core.GameHost.EditorTool.SelectArea => true,
            _ => false
        };

        public void UpdateTextureLabels()
        {
            if (Realm.Client.Core.GameHost.Instance == null) return;
            int terrainIdx = Realm.Client.Core.GameHost.Instance.EditorPaintTextureIndex;
            int cliffIdx = Realm.Client.Core.GameHost.Instance.EditorCliffPaintTextureIndex;

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
                _btnReplaceTexture.Text = $"{UnicodeIcons.UPLOAD} {TranslationServer.Translate("REPLACE TEXTURE")}";
                _btnReplaceTexture.TooltipText = TranslationServer.Translate("Replace all instances of a texture with another texture across the map");
            }
        }

        private void UpdateBrushStrengthVisibility()
        {
            if (_sldBrushStrength != null && _sldBrushStrength.GetParent() is Control strengthParent)
            {
                if (Realm.Client.Core.GameHost.Instance == null) return;
                var tool = Realm.Client.Core.GameHost.Instance.ActiveEditorTool;
                bool blockModeEnabled = (_chkBlockMode != null && _chkBlockMode.ButtonPressed);

                bool isClumpPlacement = tool == Realm.Client.Core.GameHost.EditorTool.PlacePropClump ||
                                        ((tool == Realm.Client.Core.GameHost.EditorTool.PlaceUnit || tool == Realm.Client.Core.GameHost.EditorTool.PlaceProp || tool == Realm.Client.Core.GameHost.EditorTool.PlaceDecal) &&
                                         (_chkClumpMode != null && _chkClumpMode.ButtonPressed));

                if (tool == Realm.Client.Core.GameHost.EditorTool.Raise || tool == Realm.Client.Core.GameHost.EditorTool.Lower)
                {
                    strengthParent.Visible = !blockModeEnabled;
                }
                else if (tool == Realm.Client.Core.GameHost.EditorTool.Height)
                {
                    strengthParent.Visible = false;
                }
                else
                {
                    strengthParent.Visible = (tool != Realm.Client.Core.GameHost.EditorTool.PaintPathing &&
                                              tool != Realm.Client.Core.GameHost.EditorTool.FloodFillPathing &&
                                              !isClumpPlacement &&
                                              tool != Realm.Client.Core.GameHost.EditorTool.Plateau &&
                                              tool != Realm.Client.Core.GameHost.EditorTool.Ramp);
                }
            }
        }

        private void InitializeInspectorPanel()
        {
            _inspectorPanel = GetNode<PanelContainer>("RightSlidePanel/RightScroll/AccordionContainer/InspectorAccordion/ContentInspector/InspectorPanel");
            _lblInspectorTitle = GetNode<Label>("RightSlidePanel/RightScroll/AccordionContainer/InspectorAccordion/ContentInspector/InspectorPanel/VBox/LblInspectorTitle");
            _lblInspectorPos = GetNode<Label>("RightSlidePanel/RightScroll/AccordionContainer/InspectorAccordion/ContentInspector/InspectorPanel/VBox/LblInspectorPos");

            _btnInspectorRotLeft = GetNode<Button>("RightSlidePanel/RightScroll/AccordionContainer/InspectorAccordion/ContentInspector/InspectorPanel/VBox/Grid/BtnInspectorRotLeft");
            SetupButton(_btnInspectorRotLeft, $"{UnicodeIcons.UNDO} ROT -15°", () => RotateSelectedObjectAction(-15f), 11, "Rotate object counter-clockwise");

            _btnInspectorRotRight = GetNode<Button>("RightSlidePanel/RightScroll/AccordionContainer/InspectorAccordion/ContentInspector/InspectorPanel/VBox/Grid/BtnInspectorRotRight");
            SetupButton(_btnInspectorRotRight, $"{UnicodeIcons.ROTATE} ROT +15°", () => RotateSelectedObjectAction(15f), 11, "Rotate object clockwise");

            _btnInspectorScaleDown = GetNode<Button>("RightSlidePanel/RightScroll/AccordionContainer/InspectorAccordion/ContentInspector/InspectorPanel/VBox/Grid/BtnInspectorScaleDown");
            SetupButton(_btnInspectorScaleDown, $"{UnicodeIcons.MINUS} SCALE DOWN", () => ScaleSelectedObjectAction(0.9f), 11, "Shrink object size by 10%");

            _btnInspectorScaleUp = GetNode<Button>("RightSlidePanel/RightScroll/AccordionContainer/InspectorAccordion/ContentInspector/InspectorPanel/VBox/Grid/BtnInspectorScaleUp");
            SetupButton(_btnInspectorScaleUp, $"{UnicodeIcons.PLUS} SCALE UP", () => ScaleSelectedObjectAction(1.1f), 11, "Enlarge object size by 10%");

            _btnInspectorScaleReset = GetNode<Button>("RightSlidePanel/RightScroll/AccordionContainer/InspectorAccordion/ContentInspector/InspectorPanel/VBox/BtnInspectorScaleReset");
            SetupButton(_btnInspectorScaleReset, $"{UnicodeIcons.UNDO} RESET SCALE", () => ScaleSelectedObjectAction(-1f), 12, "Reset object scale size to 1.0x");

            _btnCenter = GetNode<Button>("RightSlidePanel/RightScroll/AccordionContainer/InspectorAccordion/ContentInspector/InspectorPanel/VBox/BtnCenter");
            SetupButton(_btnCenter, $"{UnicodeIcons.CROSSHAIRS_TARGET} LOCATE OBJECT", () => LocateSelectedObjectAction(), 12, "Center camera on selected object");

            _btnInspectorDelete = GetNode<Button>("RightSlidePanel/RightScroll/AccordionContainer/InspectorAccordion/ContentInspector/InspectorPanel/VBox/BtnInspectorDelete");
            SetupButton(_btnInspectorDelete, $"{UnicodeIcons.TRASH_ALT} ERASE", () => DeleteSelectedObjectAction(), 12, "Erase selected unit, prop, or decal");

            _btnShowCoverage = new Button();
            _btnShowCoverage.Text = TranslationServer.Translate($"{UnicodeIcons.EYE} RANGES: OFF");
            var fontCoverage = GetFontAwesomeFont();
            if (fontCoverage != null) _btnShowCoverage.AddThemeFontOverride("font", fontCoverage);
            _btnShowCoverage.ToggleMode = true;
            _btnShowCoverage.FocusMode = Control.FocusModeEnum.None;
            _btnShowCoverage.AddThemeFontSizeOverride("font_size", 11);
            _btnShowCoverage.ButtonPressed = false;
            _btnShowCoverage.Toggled += (pressed) =>
            {
                if (Realm.Client.Core.GameHost.Instance != null)
                {
                    Realm.Client.Core.GameHost.Instance.EditorCoverageOverlayEnabled = pressed;
                    _btnShowCoverage.Text = pressed ? TranslationServer.Translate($"{UnicodeIcons.EYE} RANGES: ON") : TranslationServer.Translate($"{UnicodeIcons.EYE} RANGES: OFF");
                    Realm.Client.Core.GameHost.Instance.UpdateEditorCoverageOverlay();
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
                if (Realm.Client.Core.GameHost.Instance != null && Realm.Client.Core.GameHost.Instance.SelectedEditorObject is Realm.Client.Unit3D unit && GodotObject.IsInstanceValid(unit))
                {
                    int playerIndex = (int)index;
                    Realm.Client.Core.GameHost.Instance.SetUnitPlayerExternal(unit, playerIndex);
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

            _entityVisualEditDialog = new Realm.Client.UI.MapEditor.EntityVisualEditDialog(this);
            _animationPreviewDialog = new Realm.Client.UI.MapEditor.AnimationPreviewDialog(this);
            _weaponVfxDialog = new Realm.Client.UI.MapEditor.WeaponVfxDialog(this);
            _modelPickerDialog = new Realm.Client.UI.MapEditor.ModelPickerDialog(this);
            _abilityVfxDialog = new Realm.Client.UI.MapEditor.AbilityVfxDialog(this);
            _assetManagerDialog = new Realm.Client.UI.MapEditor.AssetManagerDialog(this);
            _templateManagerDialog = new Realm.Client.UI.MapEditor.TemplateManagerDialog(this);
            _instanceManagerDialog = new Realm.Client.UI.MapEditor.InstanceManagerDialog(this);
            _assetBrowserDialog = new Realm.Client.UI.MapEditor.AssetBrowserDialog(this);
            _noiseTextureDialog = new Realm.Client.UI.MapEditor.NoiseTextureDialog(this);
            _convertGlbDialog = new Realm.Client.UI.MapEditor.ConvertGlbDialog(this);
            _editorSettingsDialog = new Realm.Client.UI.MapEditor.EditorSettingsDialog(this);
            _shaderEditorDialog = new Realm.Client.UI.MapEditor.ShaderEditorDialog(this);
            _vfxStudioDialog = new Realm.Client.UI.MapEditor.VfxStudioDialog(this);
            _proceduralAnimationStudioDialog = new Realm.Client.UI.MapEditor.ProceduralAnimationStudioDialog(this);
            _authorSignatureDialog = new Realm.Client.UI.MapEditor.AuthorSignatureDialog(this);
            _waterProfileDialog = new Realm.Client.UI.MapEditor.WaterProfileDialog(this);
            _environmentConfigDialog = new Realm.Client.UI.MapEditor.EnvironmentConfigDialog(this);
            _replaceTextureDialog = new Realm.Client.UI.MapEditor.ReplaceTextureDialog(this);
            RefreshWaterSwatches();
            ApplyEditorPreferences(Realm.Client.UI.MapEditor.EditorSettingsDialog.CurrentSettings);

            InitInspectorActionButtons(inspectorVBox);
        }

        private void InitInspectorActionButtons(VBoxContainer inspectorVBox)
        {

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
                if (Realm.Client.Core.GameHost.Instance != null && GodotObject.IsInstanceValid(Realm.Client.Core.GameHost.Instance.SelectedEditorObject))
                {
                    _animationPreviewDialog?.OpenForObject(Realm.Client.Core.GameHost.Instance.SelectedEditorObject);
                }
            };
            inspectorVBox.AddChild(_btnOpenAnimationPreview);

            _btnOpenGlobalOverrides = new Button();
            _btnOpenGlobalOverrides.Name = "BtnOpenGlobalOverrides";
            _btnOpenGlobalOverrides.Set("icon_max_width", 0);
            _btnOpenGlobalOverrides.Text = "✏️ " + TranslationServer.Translate("Edit Template");
            _btnOpenGlobalOverrides.AddThemeFontSizeOverride("font_size", 11);
            _btnOpenGlobalOverrides.FocusMode = Control.FocusModeEnum.None;
            _btnOpenGlobalOverrides.CustomMinimumSize = new Vector2(0, 28);
            _btnOpenGlobalOverrides.Visible = false;
            _btnOpenGlobalOverrides.Pressed += () =>
            {
                if (Realm.Client.Core.GameHost.Instance != null && GodotObject.IsInstanceValid(Realm.Client.Core.GameHost.Instance.SelectedEditorObject))
                {
                    _entityVisualEditDialog?.OpenForObject(Realm.Client.Core.GameHost.Instance.SelectedEditorObject);
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
                if (Realm.Client.Core.GameHost.Instance != null && GodotObject.IsInstanceValid(Realm.Client.Core.GameHost.Instance.SelectedEditorObject) && Realm.Client.Core.GameHost.Instance.SelectedEditorObject is ProceduralVfxInstance3D vfx)
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
                if (Realm.Client.Core.GameHost.Instance != null && GodotObject.IsInstanceValid(Realm.Client.Core.GameHost.Instance.SelectedEditorObject))
                {
                    var sel = Realm.Client.Core.GameHost.Instance.SelectedEditorObject;
                    if (sel is Realm.Client.Unit3D u)
                    {
                        bool isBuilding = u.IsBuilding || (Realm.Client.Core.GameHost.BuildingRegistry != null && Realm.Client.Core.GameHost.BuildingRegistry.ContainsKey(u.UnitId));
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

        public void OpenWeaponVfxDialog(string weaponId, WeaponMetadata weapon, Action<WeaponMetadata> onApplied = null)
        {
            if (_weaponVfxDialog == null)
            {
                _weaponVfxDialog = new Realm.Client.UI.MapEditor.WeaponVfxDialog(this);
            }
            _weaponVfxDialog.OpenForWeapon(weaponId, weapon, onApplied);
        }

        public void OpenVfxStudioDialog(VfxAttachmentConfig initialConfig = null, Action<VfxAttachmentConfig> onApplied = null)
        {
            if (_vfxStudioDialog == null)
            {
                _vfxStudioDialog = new Realm.Client.UI.MapEditor.VfxStudioDialog(this);
            }
            _vfxStudioDialog.OpenForConfig(initialConfig, onApplied);
        }

        public void OpenObjectAttachmentDialog(
            string unitId = null,
            string attachmentId = null,
            string hand = "RightHand",
            Node3D sourceModel = null,
            Action<HandAttachmentOrientation> onApplied = null)
        {
            if (_objectAttachmentDialog == null)
            {
                _objectAttachmentDialog = new Realm.Client.UI.MapEditor.ObjectAttachmentDialog(this);
            }
            _objectAttachmentDialog.OpenForUnitAndAttachment(unitId, attachmentId, hand, sourceModel, onApplied);
        }

        public void RemoveUnitObjectAttachment(string unitId, string socket, string attachmentId, string? parentAttachmentId = null)
        {
            try
            {
                if (string.IsNullOrEmpty(unitId) || string.IsNullOrEmpty(attachmentId)) return;

                string regKey = unitId;
                bool isBuildingMeta = false;
                if (Realm.Client.Core.GameHost.UnitRegistry.ContainsKey(unitId))
                {
                    regKey = unitId;
                }
                else if (Realm.Client.Core.GameHost.BuildingRegistry != null && Realm.Client.Core.GameHost.BuildingRegistry.ContainsKey(unitId))
                {
                    regKey = unitId;
                    isBuildingMeta = true;
                }
                else
                {
                    string cleanId = System.IO.Path.GetFileNameWithoutExtension(unitId);
                    if (Realm.Client.Core.GameHost.UnitRegistry.ContainsKey(cleanId))
                    {
                        regKey = cleanId;
                    }
                    else if (Realm.Client.Core.GameHost.BuildingRegistry != null && Realm.Client.Core.GameHost.BuildingRegistry.ContainsKey(cleanId))
                    {
                        regKey = cleanId;
                        isBuildingMeta = true;
                    }
                    else
                    {
                        foreach (var k in Realm.Client.Core.GameHost.UnitRegistry.Keys)
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
                        if (Realm.Client.Core.GameHost.BuildingRegistry != null)
                        {
                            foreach (var k in Realm.Client.Core.GameHost.BuildingRegistry.Keys)
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

                if (isBuildingMeta && Realm.Client.Core.GameHost.BuildingRegistry != null && Realm.Client.Core.GameHost.BuildingRegistry.TryGetValue(regKey, out var bMeta))
                {
                    bMeta.RemoveObjectAttachment(socket, attachmentId, parentAttachmentId);
                    Realm.Client.Core.GameHost.BuildingRegistry[regKey] = bMeta;
                }
                else if (Realm.Client.Core.GameHost.UnitRegistry.TryGetValue(regKey, out var uMeta))
                {
                    uMeta.RemoveObjectAttachment(socket, attachmentId, parentAttachmentId);
                    Realm.Client.Core.GameHost.UnitRegistry[regKey] = uMeta;
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

                if (Realm.Client.Core.GameHost.Instance != null)
                {
                    if (Realm.Client.Core.GameHost.Instance.AllUnits != null)
                    {
                        foreach (var u in Realm.Client.Core.GameHost.Instance.AllUnits)
                        {
                            if (u != null && (u.UnitId == unitId || u.UnitId == regKey || System.IO.Path.GetFileNameWithoutExtension(u.UnitId ?? "").Equals(System.IO.Path.GetFileNameWithoutExtension(unitId), StringComparison.OrdinalIgnoreCase)))
                            {
                                u.ApplyAllConfiguredAttachments();
                            }
                        }
                    }

                    if (Realm.Client.Core.GameHost.Instance.SelectedEditorObject is Realm.Client.Unit3D selUnit)
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

        public void RestoreUnitObjectAttachments(string targetId, UnitObjectAttachments? snapshot)
        {
            try
            {
                if (string.IsNullOrEmpty(targetId)) return;

                string regKey = targetId;
                bool isBuildingMeta = false;
                if (Realm.Client.Core.GameHost.UnitRegistry.ContainsKey(targetId))
                {
                    regKey = targetId;
                }
                else if (Realm.Client.Core.GameHost.BuildingRegistry != null && Realm.Client.Core.GameHost.BuildingRegistry.ContainsKey(targetId))
                {
                    regKey = targetId;
                    isBuildingMeta = true;
                }
                else
                {
                    string cleanId = System.IO.Path.GetFileNameWithoutExtension(targetId);
                    if (Realm.Client.Core.GameHost.UnitRegistry.ContainsKey(cleanId))
                    {
                        regKey = cleanId;
                    }
                    else if (Realm.Client.Core.GameHost.BuildingRegistry != null && Realm.Client.Core.GameHost.BuildingRegistry.ContainsKey(cleanId))
                    {
                        regKey = cleanId;
                        isBuildingMeta = true;
                    }
                    else
                    {
                        foreach (var k in Realm.Client.Core.GameHost.UnitRegistry.Keys)
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
                        if (Realm.Client.Core.GameHost.BuildingRegistry != null)
                        {
                            foreach (var k in Realm.Client.Core.GameHost.BuildingRegistry.Keys)
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

                if (isBuildingMeta && Realm.Client.Core.GameHost.BuildingRegistry != null && Realm.Client.Core.GameHost.BuildingRegistry.TryGetValue(regKey, out var bMeta))
                {
                    bMeta.ObjectAttachments = (snapshot != null && snapshot.HasAny()) ? snapshot?.Clone() : null;
                    Realm.Client.Core.GameHost.BuildingRegistry[regKey] = bMeta;
                }
                else if (Realm.Client.Core.GameHost.UnitRegistry.TryGetValue(regKey, out var uMeta))
                {
                    uMeta.ObjectAttachments = (snapshot != null && snapshot.HasAny()) ? snapshot?.Clone() : null;
                    Realm.Client.Core.GameHost.UnitRegistry[regKey] = uMeta;
                }

                string wsPath = string.IsNullOrEmpty(_tempWorkspacePath)
                    ? ProjectSettings.GlobalizePath(TempWorkspaceGodotPath)
                    : _tempWorkspacePath;
                string metadataPath = MetadataService.ResolveMetadataPath(wsPath);
                if (System.IO.File.Exists(metadataPath))
                {
                    MetadataService.Instance.UpdateMetadata(wsPath, meta =>
                    {
                        var atts = (snapshot != null && snapshot.HasAny()) ? snapshot?.Clone() : null;
                        bool updated = meta.UpdateUnit(targetId, u => { u.ObjectAttachments = atts; return u; });
                        if (!updated) updated = meta.UpdateBuilding(targetId, b => { b.ObjectAttachments = atts; return b; });
                        if (!updated) updated = meta.UpdateUnit(regKey, u => { u.ObjectAttachments = atts; return u; });
                        if (!updated) meta.UpdateBuilding(regKey, b => { b.ObjectAttachments = atts; return b; });
                    });
                    _lastMetadataSyncTime = GetLastWriteTimeSafe(metadataPath);
                }

                if (Realm.Client.Core.GameHost.Instance != null)
                {
                    if (Realm.Client.Core.GameHost.Instance.AllUnits != null)
                    {
                        foreach (var u in Realm.Client.Core.GameHost.Instance.AllUnits)
                        {
                            if (u != null && (u.UnitId == targetId || u.UnitId == regKey || System.IO.Path.GetFileNameWithoutExtension(u.UnitId ?? "").Equals(System.IO.Path.GetFileNameWithoutExtension(targetId), StringComparison.OrdinalIgnoreCase)))
                            {
                                u.ApplyAllConfiguredAttachments();
                            }
                        }
                    }

                    if (Realm.Client.Core.GameHost.Instance.SelectedEditorObject is Realm.Client.Unit3D selUnit)
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

        public void OpenShaderEditorDialog(string shaderKey = "", Action<CustomShaderConfig> onSaved = null)
        {
            if (_shaderEditorDialog == null)
            {
                _shaderEditorDialog = new Realm.Client.UI.MapEditor.ShaderEditorDialog(this);
            }
            _shaderEditorDialog.OpenForShader(shaderKey, onSaved);
        }

        public void OpenProceduralAnimationStudioDialog(ProceduralAnimationConfig initialConfig = null, Action<ProceduralAnimationConfig> onApplied = null, string previewModelKey = null)
        {
            if (_proceduralAnimationStudioDialog == null)
            {
                _proceduralAnimationStudioDialog = new Realm.Client.UI.MapEditor.ProceduralAnimationStudioDialog(this);
            }
            _proceduralAnimationStudioDialog.OpenForConfig(initialConfig, onApplied, previewModelKey);
        }

        public void OpenModelPickerDialog(string entityId, string fieldName, string domain, string currentPath, Action<string> onApplied = null)
        {
            if (_modelPickerDialog == null)
            {
                _modelPickerDialog = new Realm.Client.UI.MapEditor.ModelPickerDialog(this);
            }
            _modelPickerDialog.OpenForEntity(entityId, fieldName, domain, currentPath, onApplied);
        }

        public void OpenAnimationPreviewDialog(string unitId, string modelPath = null)
        {
            if (_animationPreviewDialog == null)
            {
                _animationPreviewDialog = new Realm.Client.UI.MapEditor.AnimationPreviewDialog(this);
            }
            _animationPreviewDialog.OpenForUnitId(unitId, modelPath);
        }

        private async void TestMapAction()
        {
            if (Realm.Client.Core.GameHost.Instance == null) return;

            if (Realm.Client.Core.GameHost.Instance.AllUnits.Count == 0)
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

        public void OpenNoiseTextureDialog(Action<string> onSaved = null)
        {
            _noiseTextureDialog?.OpenWithCallback(onSaved);
        }

        public void OpenTemplateManagerDialog()
        {
            if (_templateManagerDialog == null)
            {
                _templateManagerDialog = new Realm.Client.UI.MapEditor.TemplateManagerDialog(this);
            }
            _templateManagerDialog.OpenDialog();
        }

        public void OpenEditorSettingsDialog()
        {
            _editorSettingsDialog?.OpenDialog();
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

                var metadata = MapFileService.LoadMetadata(wsPath);

                if (category == "glb" && Realm.Client.Core.GameHost.Instance != null)
                {
                    string normKey = Realm.Client.Core.GameHost.Instance.NormalizeModelAssetKey(fileName);
                    if (!Realm.Client.Core.GameHost.Instance.ModelObstacleRadii.ContainsKey(normKey))
                    {
                        string modelPath = System.IO.Path.Combine(wsPath, "Assets", "models", subCategory ?? "", fileName);
                        Node3D modelNode = Realm.Client.Utils.ModelCache.GetModel(modelPath) as Node3D;
                        if (modelNode != null)
                        {
                            float radius = Realm.Client.Core.GameHost.Instance.MeasureModelRadius(modelNode);
                            modelNode.Free();
                            if (radius > 0f)
                            {
                                float rounded = (float)Math.Round(radius, 2);
                                Realm.Client.Core.GameHost.Instance.ModelObstacleRadii[normKey] = rounded;
                                metadata.SetModelObstacleRadius(normKey, rounded);
                            }
                        }
                    }
                }

                if (!string.IsNullOrEmpty(subCategory) && category == "glb")
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

                    var (minY, autoYOffset) = Realm.Client.Utils.ModelCache.CalculateModelBounds(modelFullPath, defaultScale);
                    string subLower = subCategory.ToLowerInvariant();
                    bool isPropOrRes = subLower == "props" || subLower == "resources" || subLower == "attachments" || subLower == "weapons" || subLower == "items" || subLower == "projectiles";

                    metadata.SetModelYOffset(fileName, autoYOffset);
                    metadata.SetModelScale(fileName, defaultScale);

                    Realm.Client.Core.GameHost.Instance?.SetModelYOffset(fileName, autoYOffset);
                    Realm.Client.Core.GameHost.Instance?.SetModelScale(fileName, defaultScale);

                    string unitId = System.IO.Path.GetFileNameWithoutExtension(fileName);
                    if (subLower == "buildings")
                    {
                        var b = metadata.FindBuilding(unitId);
                        if (b == null)
                        {
                            b = new UnitMetadata
                            {
                                TemplateID = unitId,
                                Name = unitId,
                                Scale = defaultScale,
                                YOffset = autoYOffset,
                                PathingType = (int)Realm.Ecs.Components.Terrain.TerrainPathingFlags.Buildable,
                                ModelPath = fileName,
                                NormalizeLuminance = true,
                                IgnorePlayerColor = isPropOrRes
                            };
                            metadata.AddOrUpdateBuilding(b);
                        }
                        else if (autoYOffset != 0f)
                        {
                            b.YOffset = autoYOffset;
                        }
                    }
                    else if (subLower == "props")
                    {
                        var p = metadata.FindProp(unitId);
                        if (p == null)
                        {
                            p = new PropMetadata
                            {
                                TemplateID = unitId,
                                Name = unitId,
                                Scale = defaultScale,
                                YOffset = autoYOffset,
                                PathingType = 0xFF,
                                ModelPath = fileName,
                                NormalizeLuminance = true,
                                IgnorePlayerColor = isPropOrRes
                            };
                            metadata.AddOrUpdateProp(p);
                        }
                        else if (autoYOffset != 0f)
                        {
                            p.YOffset = autoYOffset;
                        }
                    }
                    else if (subLower == "resources")
                    {
                        var r = metadata.FindResource(unitId);
                        if (r == null)
                        {
                            r = new ResourceMetadata
                            {
                                TemplateID = unitId,
                                Name = unitId,
                                Scale = defaultScale,
                                YOffset = autoYOffset,
                                PathingType = 0xFF,
                                ModelPath = fileName,
                                NormalizeLuminance = true,
                                IgnorePlayerColor = isPropOrRes
                            };
                            metadata.AddOrUpdateResource(r);
                        }
                        else if (autoYOffset != 0f)
                        {
                            r.YOffset = autoYOffset;
                        }
                    }
                    else
                    {
                        var u = metadata.FindUnit(unitId);
                        if (u == null)
                        {
                            u = new UnitMetadata
                            {
                                TemplateID = unitId,
                                Name = unitId,
                                Scale = defaultScale,
                                YOffset = autoYOffset,
                                PathingType = (int)(Realm.Ecs.Components.Terrain.TerrainPathingFlags.Ground | Realm.Ecs.Components.Terrain.TerrainPathingFlags.ShallowWater),
                                ModelPath = fileName,
                                NormalizeLuminance = true,
                                IgnorePlayerColor = isPropOrRes
                            };
                            metadata.AddOrUpdateUnit(u);
                        }
                        else if (autoYOffset != 0f)
                        {
                            u.YOffset = autoYOffset;
                        }
                    }
                }

                if (category == "textures" || category == "terrain" || category == "Terrain")
                {
                    string slug = TemplateIDHelper.GenerateSlug(fileName);
                    string terrainTemplateId = TemplateIDHelper.NormalizeTemplateID("terrain", slug);
                    int swatchIdx = targetSlot >= 0 ? targetSlot : 0;
                    metadata.Textures ??= new Dictionary<string, TextureMetadata>(StringComparer.OrdinalIgnoreCase);
                    if (!metadata.Textures.TryGetValue(terrainTemplateId, out var texMeta) || texMeta == null)
                    {
                        if (!metadata.Textures.TryGetValue(fileName, out texMeta) || texMeta == null)
                        {
                            texMeta = new TextureMetadata();
                        }
                        else
                        {
                            metadata.Textures.Remove(fileName);
                        }
                        metadata.Textures[terrainTemplateId] = texMeta;
                    }
                    texMeta.TexturePath = fileName;
                    texMeta.SwatchIndex = swatchIdx;

                    string texPath = System.IO.Path.Combine(wsPath, "Assets", "textures", fileName);
                    if (System.IO.File.Exists(texPath))
                    {
                        texMeta.ScaleFactor = Realm.Shared.Textures.TextureConverter.CalculateLuminanceScaleFactor(texPath);
                    }
                }

                MapFileService.SaveMetadata(wsPath, metadata);
                MapAssetHelper.UpdateManifestAsset(wsPath, category, fileName, blake3Hash);
            }
            catch (Exception ex)
            {
                GD.PrintErr($"Failed to update metadata.json asset: {ex.Message}");
            }
        }

        public void ConvertRawTextureDirect(string rawPngPath, string outputRtexPath, string swatchName)
        {
            try
            {
                if (Realm.Client.Core.GameHost.Instance != null && Realm.Client.Core.GameHost.Instance.GroundTerrain != null)
                {
                    Realm.Client.Core.GameHost.Instance.GroundTerrain.ProcessAndSaveRawTexture(rawPngPath, outputRtexPath);
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

        public Realm.Client.Services.ModelOptimization.ModelOptimizerService.OptimizationResult OptimizeAndImportGlbDirect(
            byte[] glbBytes,
            int maxTextureResolution = 1024,
            float creaseAngleDegrees = GlbMeshSmoother.DefaultCreaseAngleDegrees,
            float allowedPixelError = 1.5f,
            bool forceReDecimate = false)
        {
            var optimizer = ServiceLocator.TryGet<Realm.Client.Services.ModelOptimization.ModelOptimizerService>()
                            ?? new Realm.Client.Services.ModelOptimization.ModelOptimizerService(ServiceLocator.TryGet<Realm.Ecs.Services.WorldAccessor>());

            var options = new Realm.Client.Services.ModelOptimization.ModelOptimizerService.OptimizationOptions
            {
                MaxTextureResolution = maxTextureResolution,
                CreaseAngleDegrees = creaseAngleDegrees,
                AllowedPixelError = allowedPixelError,
                ForceReDecimate = forceReDecimate
            };

            return optimizer.OptimizeGlb(glbBytes, options);
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

                var importResult = Realm.Client.Animation.MixamoAnimationImporter.ImportMixamoGlb(sourceFilePath, wsPath, subCat);
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

                Realm.Client.Core.GameHost.Instance?.LoadUnitMetadata(wsPath);
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

        public void RefreshWaterSwatches()
        {
            if (_optWaterMode == null) return;

            byte currentProf = Realm.Client.Core.GameHost.Instance != null ? Realm.Client.Core.GameHost.Instance.ActiveWaterProfileIndex : (byte)0;
            if (_optWaterMode.Selected >= 0 && _optWaterMode.Selected < _optWaterMode.ItemCount)
            {
                var currentMeta = _optWaterMode.GetItemMetadata(_optWaterMode.Selected);
                if (currentMeta.VariantType != Variant.Type.Nil)
                {
                    currentProf = (byte)(int)currentMeta;
                }
            }

            _optWaterMode.Clear();

            var profiles = Realm.Client.RuntimeTerrain.Instance != null ? Realm.Client.RuntimeTerrain.Instance.GetWaterProfiles() : null;
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

            if (Realm.Client.Core.GameHost.Instance != null)
            {
                Realm.Client.Core.GameHost.Instance.ActiveWaterProfileIndex = currentProf;
            }
        }

        public void OpenWaterProfileDialog()
        {
            var profilesList = new List<WaterProfileSaveData>();
            if (Realm.Client.RuntimeTerrain.Instance != null)
            {
                var profDict = Realm.Client.RuntimeTerrain.Instance.GetWaterProfiles();
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
            if (Realm.Client.Core.GameHost.Instance != null)
            {
                byte currentProf = Realm.Client.Core.GameHost.Instance.ActiveWaterProfileIndex;
                int found = profilesList.FindIndex(p => p.ProfileIndex == currentProf);
                if (found >= 0) targetIdx = found;
            }

            _waterProfileDialog?.OpenWithProfiles(profilesList, targetIdx);
        }

        public void OpenEnvironmentConfigDialog()
        {
            if (_environmentConfigDialog == null)
            {
                _environmentConfigDialog = new Realm.Client.UI.MapEditor.EnvironmentConfigDialog(this);
            }

            List<EnvironmentPresetConfig> presetsList = null;
            string defaultPreset = null;
            string wsPath = Realm.Client.Services.MapWorkspaceService.GetActiveWorkspacePath();
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
                presetsList = Realm.Client.Core.GameHost.Instance?.EnvironmentService?.GetPresets().ToList() ?? EnvironmentPresetConfig.CreateDefaultPresets();
            }

            string currentId = Realm.Client.Core.GameHost.Instance?.EnvironmentService?.GetCurrentPresetId() ?? defaultPreset ?? "day";
            _environmentConfigDialog?.OpenWithPresets(presetsList, currentId, defaultPreset);
        }
    }
}
