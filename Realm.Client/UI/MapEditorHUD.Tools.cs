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
using Realm.Client.Services.ModelOptimization;
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
			if (GameContext.BaseMapFolderWatchService != null)
			{
				GameContext.BaseMapFolderWatchService.BaseFolderFilesChanged -= OnBaseMapFolderFilesChanged;
				GameContext.BaseMapFolderWatchService.BaseFolderFilesChanged += OnBaseMapFolderFilesChanged;
			}

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
			_highlightStyle.CornerRadiusTopLeft = 6;
			_highlightStyle.CornerRadiusTopRight = 6;
			_highlightStyle.CornerRadiusBottomLeft = 6;
			_highlightStyle.CornerRadiusBottomRight = 6;
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

				_btnBackToHub = GetNode<Button>("TopLeftBox/BtnBack");
				SetupButton(_btnBackToHub, $"{UnicodeIcons.SIGN_OUT} BACK TO HUB", () => BackToHubAction(), 13, "Exit editor and return to game lobby");
				StyleMapEditorTopButton(_btnBackToHub);
				_mapNameHeaderPanel = new PanelContainer();
				_mapNameHeaderPanel.Name = "MapNameHeaderPanel";
				_mapNameHeaderPanel.AddThemeStyleboxOverride("panel", UIStyle.CreateLightInnerPanel());
				_mapNameHeaderPanel.CustomMinimumSize = new Vector2(160, 32);
				_mapNameHeaderPanel.MouseFilter = MouseFilterEnum.Stop;
				_mapNameHeaderPanel.MouseDefaultCursorShape = CursorShape.PointingHand;
				_mapNameHeaderPanel.TooltipText = TranslationServer.Translate("Click to open Map Settings");
				_mapNameHeaderPanel.GuiInput += (ev) =>
				{
					if (ev is InputEventMouseButton mb && mb.Pressed && mb.ButtonIndex == MouseButton.Left)
					{
						_mapSettingsDialog?.OpenDialog();
					}
				};

				var mapNameHBox = new HBoxContainer();
				mapNameHBox.Name = "MapNameHBox_HBoxContainer";
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
				_lblMapNameHeader.MouseFilter = MouseFilterEnum.Pass;
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
				editorsVBox.Name = "EditorsVBox_VBoxContainer";
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
					GameContext.VSCode.Initialize(this);
					_btnVSCode.GuiInput += (@event) =>
					{
						if (OperatingSystem.IsWindows() && @event is InputEventMouseButton mouseButton && mouseButton.Pressed)
						{
							if (mouseButton.ButtonIndex == MouseButton.Right || mouseButton.ButtonIndex == MouseButton.Middle)
							{
								if (!GameContext.VSCode.IsVisible)
								{
									ToggleVSCodeEditor();
								}

								GetViewport().SetInputAsHandled();
							}
						}
					};
					editorsVBox.AddChild(_btnVSCode);
				}

				BtnAssetsManager = new Button();
				BtnAssetsManager.Name = "BtnAssetsManager";
				SetupOptionButton(BtnAssetsManager, $"{UnicodeIcons.CUBE} ASSETS", () => _assetManagerDialog?.OpenDialog(), 13, "Open Map Assets Manager & Importer");
				BtnAssetsManager.Pressed += () => _popupEditors.Hide();
				editorsVBox.AddChild(BtnAssetsManager);

				BtnTemplateManager = new Button();
				BtnTemplateManager.Name = "BtnTemplateManager";
				SetupOptionButton(BtnTemplateManager, $"{UnicodeIcons.CUBES} TEMPLATES", () => OpenTemplateManagerDialog(), 13, "Open dialog to manage object template types and visual properties");
				BtnTemplateManager.Pressed += () => _popupEditors.Hide();
				editorsVBox.AddChild(BtnTemplateManager);

				BtnInstanceManager = new Button();
				BtnInstanceManager.Name = "BtnInstanceManager";
				SetupOptionButton(BtnInstanceManager, $"{UnicodeIcons.LIST} INSTANCES", () => OpenInstanceManagerDialog(), 13, "Open dialog to list and locate all placed instances");
				BtnInstanceManager.Pressed += () => _popupEditors.Hide();
				editorsVBox.AddChild(BtnInstanceManager);

				BtnImportFromMap = new Button();
				BtnImportFromMap.Name = "BtnImportFromMap";
				SetupOptionButton(BtnImportFromMap, $"🗺 " + TranslationServer.Translate("IMPORT FROM MAP"), () => OpenImportFromMapDialog(), 13, "Copy assets, templates, and code from another map into this map");
				BtnImportFromMap.Pressed += () => _popupEditors.Hide();
				editorsVBox.AddChild(BtnImportFromMap);

				BtnUtilities = new Button();
				BtnUtilities.Name = "BtnUtilities";
				SetupOptionButton(BtnUtilities, $"{UnicodeIcons.WRENCH} UTILITIES", () => OpenUtilitiesDialog(), 13, "Open batch utilities and maintenance tools");
				BtnUtilities.Pressed += () => _popupEditors.Hide();
				editorsVBox.AddChild(BtnUtilities);

				_btnEditors = GetNodeOrNull<Button>("TopLeftBox/BtnEditors") ?? GetNodeOrNull<Button>("TopLeftBox/BtnVSCode");
				if (_btnEditors == null)
				{
					_btnEditors = new Button();
					_btnEditors.Name = "BtnEditors";
				}

				SetupButton(_btnEditors, $"WINDOWS {UnicodeIcons.CHEVRON_DOWN}", () =>
				{
					var popupPosition = _btnEditors.GetScreenPosition() + new Vector2(0, _btnEditors.Size.Y);
					_popupEditors.Popup(new Rect2I((Vector2I)popupPosition, Vector2I.Zero));
				}, 13, "Open Modules");
				StyleMapEditorTopButton(_btnEditors);

				_btnUndo = GetNode<Button>("TopLeftBox/BtnUndo");
				SetupButton(_btnUndo, $"{UnicodeIcons.UNDO} UNDO", () => UndoAction(), 13, "Undo the last action (Ctrl+Z)");
				StyleMapEditorTopButton(_btnUndo);

				_btnRedo = GetNode<Button>("TopLeftBox/BtnRedo");
				SetupButton(_btnRedo, $"{UnicodeIcons.REDO} REDO", () => RedoAction(), 13, "Redo the last undone action (Ctrl+Y)");
				StyleMapEditorTopButton(_btnRedo);

				_btnEyedropper = GetNode<Button>("TopLeftBox/BtnEyedropper");
				SetupButton(_btnEyedropper, $"{UnicodeIcons.EYEDROPPER} EYEDROPPER", () => TriggerToolSelection(EditorTool.Eyedropper, _btnEyedropper), 13, "Pick / sample entities, terrain height (Shift+Click), or vertex color under cursor (I)");
				StyleMapEditorTopButton(_btnEyedropper);

				_optModule = GetNode<OptionButton>("TopLeftBox/OptModule");
				StyleOptionButtonPopup(_optModule);
				_optModule.AddItem($"{UnicodeIcons.MOUNTAIN} " + TranslationServer.Translate("TERRAIN") + " (F1)", (int)EditorModule.Terrain);
				_optModule.AddItem($"{UnicodeIcons.PAINT_BRUSH} " + TranslationServer.Translate("TEXTURE") + " (F2)", (int)EditorModule.TextureDeco);
				_optModule.AddItem($"{UnicodeIcons.VECTOR_SQUARE} " + TranslationServer.Translate("PATHING") + " (F3)", (int)EditorModule.Pathing);
				_optModule.AddItem($"{UnicodeIcons.CUBE} " + TranslationServer.Translate("OBJECTS") + " (F4)", (int)EditorModule.Objects);
				_optModule.AddItem($"{UnicodeIcons.DRAW_POLYGON} " + TranslationServer.Translate("COORDINATES") + " (F5)", (int)EditorModule.Coordinates);
				_optModule.AddItem($"{UnicodeIcons.PASTE} " + TranslationServer.Translate("CLIPBOARD") + " (F6)", (int)EditorModule.Clipboard);
				_optModule.ItemSelected += (index) => SwitchModuleView((EditorModule)index);
				StyleMapEditorTopButton(_optModule);

				_btnGameSettings = GetNodeOrNull<Button>("TopLeftBox/BtnSettings");
				if (_btnGameSettings != null)
				{
					SetupIconButton(_btnGameSettings, "res://Assets/UI/gear_icon.webp", () => { GameContext.UserInterface?.OpenSettingsOverlay(); }, "Game Settings");
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

				_optTestSettings = new OptionButton();
				_optTestSettings.Name = "OptTestSettings";
				_optTestSettings.FitToLongestItem = false;
				_optTestSettings.CustomMinimumSize = new Vector2(20, 0);
				_optTestSettings.SizeFlagsHorizontal = SizeFlags.ShrinkBegin;
				_optTestSettings.Alignment = HorizontalAlignment.Center;
				_optTestSettings.AddThemeIconOverride("arrow", new PlaceholderTexture2D { Size = Vector2.Zero });
				_optTestSettings.AddThemeConstantOverride("arrow_margin", 0);
				_optTestSettings.Text = UnicodeIcons.CHEVRON_DOWN;
				var font = GetFontAwesomeFont();
				if (font != null)
				{
					_optTestSettings.AddThemeFontOverride("font", font);
				}
				_optTestSettings.AddThemeFontSizeOverride("font_size", 11);
				_optTestSettings.TooltipText = "Test Settings";
				_optTestSettings.AddItem("Test Settings", 0);
				_optTestSettings.Selected = -1;
				_optTestSettings.Text = UnicodeIcons.CHEVRON_DOWN;
				StyleOptionButtonPopup(_optTestSettings);
				StyleCompactMapEditorTopButton(_optTestSettings, 4);
				_optTestSettings.ItemSelected += (idx) =>
				{
					Callable.From(() =>
					{
						_optTestSettings.Selected = -1;
						_optTestSettings.Text = UnicodeIcons.CHEVRON_DOWN;
					}).CallDeferred();
					OpenTestSettingsDialog();
				};
				if (_topLeftBox != null)
				{
					_topLeftBox.AddChild(_optTestSettings);
				}

				if (_topLeftBox != null)
				{
					if (_btnTestMap != null && _btnGameSettings != null)
					{
						_topLeftBox.MoveChild(_btnTestMap, _btnGameSettings.GetIndex() + 1);
						_topLeftBox.MoveChild(_optTestSettings, _btnTestMap.GetIndex() + 1);
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

				_statusLabel = GetNode<Label>("BottomCenterBar/TopBar/HBox/StatusLabel");
				_feedbackLabel = GetNode<Label>("FeedbackLabel");
				_feedbackLabel.Modulate = new Color(1, 1, 1, 0);
				_feedbackLabel.MouseFilter = MouseFilterEnum.Ignore;

				var leftScroll = GetNodeOrNull<ScrollContainer>("LeftSlidePanel/LeftScroll");
				if (leftScroll != null)
				{
					leftScroll.MouseFilter = MouseFilterEnum.Ignore;
					leftScroll.HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled;
					leftScroll.VerticalScrollMode = ScrollContainer.ScrollMode.Disabled;
				}

				var rightScroll = GetNodeOrNull<ScrollContainer>("RightSlidePanel/RightScroll");
				if (rightScroll != null)
				{
					rightScroll.MouseFilter = MouseFilterEnum.Ignore;
					rightScroll.HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled;
					rightScroll.VerticalScrollMode = ScrollContainer.ScrollMode.Disabled;
				}

				_accordionFile = GetNode<VBoxContainer>("LeftSlidePanel/LeftScroll/LeftVBox/FileAccordion");
				_btnHeaderFile = GetNode<Button>("LeftSlidePanel/LeftScroll/LeftVBox/FileAccordion/BtnHeaderFile");
				_contentFile = GetNode<VBoxContainer>("LeftSlidePanel/LeftScroll/LeftVBox/FileAccordion/ContentFile");
				StyleAccordionHeader(_btnHeaderFile);
				SetupAccordion(_btnHeaderFile, _contentFile, TranslationServer.Translate("File"));

				_btnLoad = GetNode<Button>("LeftSlidePanel/LeftScroll/LeftVBox/FileAccordion/ContentFile/BoxFileOps/RowFileSaveLoad/RowLoadGroup/BtnLoad");
				SetupOptionButton(_btnLoad, $"{UnicodeIcons.FOLDER_OPEN} LOAD", () => LoadMapAction(), 11, "Load heights, colors, and entities from a saved json file (Ctrl+O)");

				_popupLoadMore = GetNode<PopupPanel>("PopupLoadMore");
				var loadMorePopupStyle = new StyleBoxFlat();
				loadMorePopupStyle.BgColor = new Color(0.14f, 0.13f, 0.11f, 0.98f);
				loadMorePopupStyle.BorderColor = UIStyle.ColorGold;
				loadMorePopupStyle.SetBorderWidthAll(1);
				loadMorePopupStyle.SetCornerRadiusAll(4);
				loadMorePopupStyle.SetContentMarginAll(10);
				_popupLoadMore.AddThemeStyleboxOverride("panel", loadMorePopupStyle);

				_btnImportArchive = GetNode<Button>("PopupLoadMore/LoadMoreVBox_VBoxContainer/BtnImportArchive");
				_btnImportArchive.Set("icon_max_width", 0);
				SetupOptionButton(_btnImportArchive, $"{UnicodeIcons.FILE_IMPORT} IMPORT (.RMAP)", () => ImportMapArchiveAction(), 13, "Import a map package (.rmap) into the editor workspace and open it for editing; saving prompts for a destination folder");
				_btnImportArchive.Pressed += () => _popupLoadMore.Hide();

				_btnLoadMore = GetNode<Button>("LeftSlidePanel/LeftScroll/LeftVBox/FileAccordion/ContentFile/BoxFileOps/RowFileSaveLoad/RowLoadGroup/BtnLoadMore");
				_btnLoadMore.Set("icon_max_width", 0);
				SetupOptionButton(_btnLoadMore, UnicodeIcons.CHEVRON_DOWN, () =>
				{
					var popupPosition = _btnLoadMore.GetScreenPosition() + new Vector2(0, _btnLoadMore.Size.Y);
					_popupLoadMore.Popup(new Rect2I((Vector2I)popupPosition, Vector2I.Zero));
				}, 10, "More load options (Import map package)");

				_btnSave = GetNode<Button>("LeftSlidePanel/LeftScroll/LeftVBox/FileAccordion/ContentFile/BoxFileOps/RowFileSaveLoad/RowSaveGroup/BtnSave");
				SetupOptionButton(_btnSave, $"{UnicodeIcons.SAVE} SAVE", () => SaveMapActionExternal(), 11, "Save current heightmap, textures, and entities (Ctrl+S)");

				_btnSaveAs = GetNode<Button>("PopupSaveMore/SaveMoreVBox_VBoxContainer/BtnSaveAs");
				_btnSaveAs.Set("icon_max_width", 0);
				SetupOptionButton(_btnSaveAs, $"{UnicodeIcons.SAVE} SAVE AS", () => SaveAsMapAction(), 13, "Save map to a new folder location");

				_btnPublish = GetNode<Button>("PopupSaveMore/SaveMoreVBox_VBoxContainer/BtnPublish");
				SetupOptionButton(_btnPublish, $"{UnicodeIcons.UPLOAD} PUBLISH", () => PublishMapActionExternal(), 13, "Publish/export map to custom map registry");

				_btnExportMap = GetNode<Button>("PopupSaveMore/SaveMoreVBox_VBoxContainer/BtnExportMap");
				_btnExportMap.Set("icon_max_width", 0);

				SetupOptionButton(_btnExportMap, $"{UnicodeIcons.FILE_EXPORT} EXPORT (.RMAP)", () => ExportMapAction(), 13, "Export prepared map package (.rmap) with compiled WASM for hosting and CAS storage");

				_popupSaveMore = GetNode<PopupPanel>("PopupSaveMore");
				var saveMorePopupStyle = new StyleBoxFlat();
				saveMorePopupStyle.BgColor = new Color(0.14f, 0.13f, 0.11f, 0.98f);
				saveMorePopupStyle.BorderColor = UIStyle.ColorGold;
				saveMorePopupStyle.SetBorderWidthAll(1);
				saveMorePopupStyle.SetCornerRadiusAll(4);
				saveMorePopupStyle.SetContentMarginAll(10);
				_popupSaveMore.AddThemeStyleboxOverride("panel", saveMorePopupStyle);
				_btnSaveAs.Pressed += () => _popupSaveMore.Hide();
				_btnExportMap.Pressed += () => _popupSaveMore.Hide();
				_btnPublish.Pressed += () => _popupSaveMore.Hide();

				_btnSaveMore = GetNode<Button>("LeftSlidePanel/LeftScroll/LeftVBox/FileAccordion/ContentFile/BoxFileOps/RowFileSaveLoad/RowSaveGroup/BtnSaveMore");
				_btnSaveMore.Set("icon_max_width", 0);
				SetupOptionButton(_btnSaveMore, UnicodeIcons.CHEVRON_DOWN, () =>
				{
					var popupPosition = _btnSaveMore.GetScreenPosition() + new Vector2(0, _btnSaveMore.Size.Y);
					_popupSaveMore.Popup(new Rect2I((Vector2I)popupPosition, Vector2I.Zero));
				}, 10, "More save & export options (Save As, Export, Publish)");

				_btnResetMap = GetNode<Button>("LeftSlidePanel/LeftScroll/LeftVBox/FileAccordion/ContentFile/BoxGenOps/FileGrid2_GridContainer/BtnResetMap");
				SetupOptionButton(_btnResetMap, $"{UnicodeIcons.TRASH} RESET MAP", () =>
				{
					ShowConfirmationDialog(
						"Are you sure you want to clear the entire map? This will delete all placed entities and reset terrain heights.",
						() => ResetToBlankMapView()
					);
				}, 13, "Clear all terrain heights, colors, and placed entities");

				SetupRandomGenerationMenu();

				_btnEditorSettings = new Button();
				_btnEditorSettings.Name = "BtnEditorSettings";
				_btnEditorSettings.Set("icon_max_width", 0);
				SetupOptionButton(_btnEditorSettings, "⚙️ " + TranslationServer.Translate("EDITOR SETTINGS"), () => _editorSettingsDialog?.OpenDialog(), 13, "Configure editor preferences, chrome border, and display overlays");
				_contentFile.AddChild(_btnEditorSettings);
				_contentFile.MoveChild(_btnEditorSettings, 1);

				_accordionViewport = GetNode<VBoxContainer>("LeftSlidePanel/LeftScroll/LeftVBox/ViewportAccordion");
				_btnHeaderViewport = GetNode<Button>("LeftSlidePanel/LeftScroll/LeftVBox/ViewportAccordion/BtnHeaderViewport");
				_contentViewport = GetNode<VBoxContainer>("LeftSlidePanel/LeftScroll/LeftVBox/ViewportAccordion/ContentViewport");
				StyleAccordionHeader(_btnHeaderViewport);
				SetupAccordion(_btnHeaderViewport, _contentViewport, TranslationServer.Translate("Viewport & Navigation"));

				InitializeTempWorkspace();


				_popupOverlayMode = new PopupMenu();
				_popupOverlayMode.Name = "PopupOverlayMode";
				_popupOverlayMode.HideOnCheckableItemSelection = false;
				_popupOverlayMode.AddCheckItem(TranslationServer.Translate("Grid") + " (V)", 0);
				_popupOverlayMode.AddCheckItem(TranslationServer.Translate("Polar") + " (V)", 1);
				_popupOverlayMode.AddCheckItem(TranslationServer.Translate("Camera Bounds"), 2);
				_popupOverlayMode.AddCheckItem(TranslationServer.Translate("Wireframe") + " (F7)", 3);
				StylePopupMenu(_popupOverlayMode);
				_popupOverlayMode.IdPressed += (long id) =>
				{
					_popupOverlayMode.Hide();
					if (GameContext.Host == null) return;
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
							(true, true) => GridOverlayMode.Both,
							(true, false) => GridOverlayMode.Grid,
							(false, true) => GridOverlayMode.Polar,
							(false, false) => GridOverlayMode.Off
						};

						GameContext.EditorService.EditorGridMode = newMode;
						GameContext.Host.UpdateGridOverlayVisibility();
						UpdateGridOverlayExternal(newMode);
						string modeName = newMode switch
						{
							GridOverlayMode.Off => "OFF",
							GridOverlayMode.Grid => "GRID",
							GridOverlayMode.Polar => "POLAR",
							GridOverlayMode.Both => "GRID + POLAR",
							_ => "OFF"
						};
						ShowFeedback($"Overlay Mode: {modeName}");
					}
					else if (id == 2)
					{
						GameContext.EditorService.EditorCameraBoundsVisible = newChecked;
						GameContext.Host.UpdateCameraBoundsOverlayVisibility();
						UpdateCameraBoundsOverlayExternal(newChecked);
						ShowFeedback(newChecked
							? TranslationServer.Translate("Camera Bounds: ON")
							: TranslationServer.Translate("Camera Bounds: OFF"));
					}
					else if (id == 3)
					{
						if (GameContext.Host.GroundTerrain != null)
						{
							GameContext.Host.GroundTerrain.ToggleWireframeMode();
							bool isWireframe = GetViewport()?.DebugDraw == Viewport.DebugDrawEnum.Wireframe;
							UpdateWireframeOverlayExternal(isWireframe);
							ShowFeedback(isWireframe
								? TranslationServer.Translate("Wireframe Mode: ON")
								: TranslationServer.Translate("Wireframe Mode: OFF"));
						}
					}
				};
				AddChild(_popupOverlayMode);

				_btnToggleGrid = GetNode<Button>("LeftSlidePanel/LeftScroll/LeftVBox/ViewportAccordion/ContentViewport/BoxViewportToolbar/ViewportIconRow1/BtnToggleGrid");
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
				_optEnvLighting.SizeFlagsHorizontal = SizeFlags.ExpandFill;
				_optEnvLighting.CustomMinimumSize = new Vector2(0, 24);
				_optEnvLighting.ClipText = true;
				SetupEnvLightingDropdown(_optEnvLighting);
				_optEnvLighting.ItemSelected += (idx) =>
				{
					SetEnvironmentLighting((int)idx);
					_popupEnvironment.Hide();
				};
				envVBox.AddChild(_optEnvLighting);

				_optEnvWeather = new OptionButton();
				_optEnvWeather.Name = "OptEnvWeather";
				_optEnvWeather.SizeFlagsHorizontal = SizeFlags.ExpandFill;
				_optEnvWeather.CustomMinimumSize = new Vector2(0, 24);
				_optEnvWeather.ClipText = true;
				SetupEnvWeatherDropdown(_optEnvWeather);
				_optEnvWeather.ItemSelected += (idx) =>
				{
					SetEnvironmentWeather((int)idx);
					_popupEnvironment.Hide();
				};
				envVBox.AddChild(_optEnvWeather);

				_chkEnvShadows = new CheckBox();
				_chkEnvShadows.Name = "ChkEnvShadows";
				_chkEnvShadows.Text = TranslationServer.Translate("Shadows");
				_chkEnvShadows.TooltipText = TranslationServer.Translate("Toggle shadows in editor (F9)");
				_chkEnvShadows.ButtonPressed = true;
				_chkEnvShadows.FocusMode = FocusModeEnum.None;
				_chkEnvShadows.AddThemeFontSizeOverride("font_size", 11);
				UIStyle.ApplyCheckboxStyle(_chkEnvShadows);
				_chkEnvShadows.Toggled += (pressed) =>
				{
					if (GameContext.Host != null)
					{
						bool disableShadows = !pressed;
						if (GameContext.EditorService.EditorDisableShadows != disableShadows)
						{
							GameContext.EditorService.EditorDisableShadows = disableShadows;
							GameContext.Host.UpdateEditorShadows();
							ShowFeedback(disableShadows
								? TranslationServer.Translate("Shadows: OFF")
								: TranslationServer.Translate("Shadows: ON"));
						}
					}
					_popupEnvironment.Hide();
				};
				envVBox.AddChild(_chkEnvShadows);

				_popupEnvironment.AddChild(envVBox);
				AddChild(_popupEnvironment);

				_btnToggleEnvironment = GetNode<Button>("LeftSlidePanel/LeftScroll/LeftVBox/ViewportAccordion/ContentViewport/BoxViewportToolbar/ViewportIconRow1/BtnToggleEnvironment");
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
					GameContext.UserInterface?.PlayClickSound();
					var camera = GameContext.Host?.MainCamera;
					camera?.Rotate90Degrees();
					_popupCamera.Hide();
				};
				camVBox.AddChild(_btnRotate);

				_btnCameraAngle = new Button();
				_btnCameraAngle.Name = "BtnCameraAngle";
				_btnCameraAngle.Set("icon_max_width", 0);
				StylePopupButton(_btnCameraAngle, $"{UnicodeIcons.CUBE} Top-Down (C)", "Toggle perspective vs top-down angle (C)");
				_btnCameraAngle.Pressed += () =>
				{
					var camera = GameContext.Host?.MainCamera;
					camera?.ToggleTopDown();
					if (camera != null)
					{
						UpdateCameraAngleButtonText(camera.IsTopDown());
					}
					_popupCamera.Hide();
				};
				camVBox.AddChild(_btnCameraAngle);

				_btnZoomIn = new Button();
				_btnZoomIn.Name = "BtnZoomIn";
				_btnZoomIn.Set("icon_max_width", 0);
				StylePopupButton(_btnZoomIn, $"{UnicodeIcons.ZOOM_IN} Zoom In (Mouse_Wheel)", "Zoom camera in (Mouse Wheel)");
				_btnZoomIn.Pressed += () =>
				{
					GameContext.UserInterface?.PlayClickSound();
					GameContext.Host?.MainCamera?.ZoomIn();
					_popupCamera.Hide();
				};
				camVBox.AddChild(_btnZoomIn);

				_btnZoomOut = new Button();
				_btnZoomOut.Name = "BtnZoomOut";
				_btnZoomOut.Set("icon_max_width", 0);
				StylePopupButton(_btnZoomOut, $"{UnicodeIcons.ZOOM_OUT} Zoom Out (Mouse_Wheel)", "Zoom camera out (Mouse Wheel)");
				_btnZoomOut.Pressed += () =>
				{
					GameContext.UserInterface?.PlayClickSound();
					GameContext.Host?.MainCamera?.ZoomOut();
					_popupCamera.Hide();
				};
				camVBox.AddChild(_btnZoomOut);

				_chkFreeCamera = new CheckBox();
				_chkFreeCamera.Name = "ChkFreeCamera";
				_chkFreeCamera.Text = TranslationServer.Translate("Free Camera (F8)");
				_chkFreeCamera.TooltipText = TranslationServer.Translate("Toggle free camera mode (F8)");
				_chkFreeCamera.FocusMode = FocusModeEnum.None;
				_chkFreeCamera.AddThemeFontSizeOverride("font_size", 11);
				UIStyle.ApplyCheckboxStyle(_chkFreeCamera);
				_chkFreeCamera.Toggled += (pressed) =>
				{
					var cam = GameContext.Host?.MainCamera;
					if (cam != null && cam.IsFreeCamera != pressed)
					{
						ToggleFreeCamera();
					}
					_popupCamera.Hide();
				};
				camVBox.AddChild(_chkFreeCamera);

				_popupCamera.AddChild(camVBox);
				AddChild(_popupCamera);

				_btnToggleCamera = GetNode<Button>("LeftSlidePanel/LeftScroll/LeftVBox/ViewportAccordion/ContentViewport/BoxViewportToolbar/ViewportIconRow1/BtnToggleCamera");
				_btnToggleCamera.Set("icon_max_width", 0);
				SetupButton(_btnToggleCamera, UnicodeIcons.CAMERA, () => OpenCameraPopup(), 12, "Configure camera controls (R / C / + / - / F8)");

				var initialCam = GameContext.Host?.MainCamera;
				UpdateFreeCameraExternal(initialCam != null && initialCam.IsFreeCamera);
				GameHost tempQualifier = GameContext.Host;
				UpdateShadowsExternal((tempQualifier != null ? GameContext.EditorService.EditorDisableShadows : (bool?)null) ?? false);

				_minimapFrame = GetNode<PanelContainer>("LeftSlidePanel/LeftScroll/LeftVBox/ViewportAccordion/ContentViewport/MinimapFrame");
				_minimapArea = GetNode<Control>("LeftSlidePanel/LeftScroll/LeftVBox/ViewportAccordion/ContentViewport/MinimapFrame/MinimapArea");
				_cameraIndicator = GetNode<MapEditorCameraIndicator>("LeftSlidePanel/LeftScroll/LeftVBox/ViewportAccordion/ContentViewport/MinimapFrame/MinimapArea/CameraIndicator");

				_mapSettingsDialog = new MapSettingsDialog(this);

				_btnTapeMeasure = GetNode<Button>("LeftSlidePanel/LeftScroll/LeftVBox/ViewportAccordion/ContentViewport/BoxViewportToolbar/ViewportIconRow1/BtnTapeMeasure");
				_btnTapeMeasure.Set("icon_max_width", 0);
				SetupButton(_btnTapeMeasure, UnicodeIcons.RULER, () =>
				{
					if (GameContext.Host != null)
					{
						if (GameContext.Host.ActiveEditorTool == EditorTool.Measure)
						{
							GameContext.Host.ClearMeasureVisuals();
							ClearMeasureTelemetry();
							TriggerToolSelection(GameContext.SessionState.SavedActiveTool != EditorTool.Measure ? GameContext.SessionState.SavedActiveTool : EditorTool.Raise, null);
						}
						else
						{
							TriggerToolSelection(EditorTool.Measure, _btnTapeMeasure);
						}
					}
				}, 12, "Tape Measure Tool: Click Point A, then Point B to measure distance & slope (U)");

				_optPolarRingSpacing = GetNode<OptionButton>("LeftSlidePanel/LeftScroll/LeftVBox/ViewportAccordion/ContentViewport/RowPolarConfig/OptPolarRingSpacing");
				_optPolarRingSpacing.Set("icon_max_width", 0);
				SetupPolarRingSpacingDropdown(_optPolarRingSpacing, "Select interval between concentric polar distance rings (4, 8, 16, 32 tiles)");

				_optPolarRadialStep = GetNode<OptionButton>("LeftSlidePanel/LeftScroll/LeftVBox/ViewportAccordion/ContentViewport/RowPolarConfig/OptPolarRadialStep");
				_optPolarRadialStep.Set("icon_max_width", 0);
				SetupSpokesDropdown(_optPolarRadialStep, (spokes) => SetPolarSpokesView(spokes), "Select radial spoke count (2, 3, 4, 5, 6, 8, 12, 16)");

				_btnResetPivotToCenter = GetNode<Button>("LeftSlidePanel/LeftScroll/LeftVBox/ViewportAccordion/ContentViewport/RowPolarConfig/BtnResetPivotToCenter");
				_btnResetPivotToCenter.Set("icon_max_width", 0);
				SetupOptionButton(_btnResetPivotToCenter, $"{UnicodeIcons.CROSSHAIRS} RESET PIVOT", () => ResetPivotToMapCenterView(), 10, "Reset symmetry and polar overlay center pivot to true map center");

				var bottomBar = GetNode<HBoxContainer>("BottomCenterBar");
				bottomBar.Alignment = BoxContainer.AlignmentMode.Center;
				bottomBar.MouseFilter = MouseFilterEnum.Ignore;

				if (_topBar != null)
				{
					_topBar.CustomMinimumSize = new Vector2(0, 44);
					_topBar.SizeFlagsVertical = SizeFlags.ShrinkCenter;
					_topBar.SizeFlagsHorizontal = SizeFlags.ShrinkCenter;
				}

				_panelMeasurementHUD = new PanelContainer();
				_panelMeasurementHUD.Name = "MeasurementHUD";
				_panelMeasurementHUD.CustomMinimumSize = new Vector2(0, 44);
				_panelMeasurementHUD.SizeFlagsVertical = SizeFlags.ShrinkCenter;
				_panelMeasurementHUD.SizeFlagsHorizontal = SizeFlags.ShrinkCenter;
				_panelMeasurementHUD.MouseFilter = MouseFilterEnum.Ignore;
				_panelMeasurementHUD.Visible = false;

				var measureHBox = new HBoxContainer();
				measureHBox.Name = "MeasureHBox";
				measureHBox.Alignment = BoxContainer.AlignmentMode.Center;
				measureHBox.AddThemeConstantOverride("separation", 8);
				measureHBox.MouseFilter = MouseFilterEnum.Ignore;

				_lblMeasureTelemetry = new Label();
				_lblMeasureTelemetry.Name = "LblMeasureTelemetry";
				_lblMeasureTelemetry.HorizontalAlignment = HorizontalAlignment.Left;
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

				var terrainGrid = GetNode<GridContainer>("RightSlidePanel/RightScroll/AccordionContainer/ToolAccordion/ContentTool/PanelTerrainVBox/TerrainGrid_GridContainer");
				var decoGrid = GetNode<GridContainer>("RightSlidePanel/RightScroll/AccordionContainer/ToolAccordion/ContentTool/PanelDecoVBox/DecoGrid_GridContainer");
				var pathGrid = GetNode<GridContainer>("RightSlidePanel/RightScroll/AccordionContainer/ToolAccordion/ContentTool/PanelPathingVBox/PathGrid_GridContainer");
				var objGrid = GetNode<GridContainer>("RightSlidePanel/RightScroll/AccordionContainer/ToolAccordion/ContentTool/PanelObjectsVBox/ObjGrid_GridContainer");
				var clipGrid = GetNode<GridContainer>("RightSlidePanel/RightScroll/AccordionContainer/ToolAccordion/ContentTool/PanelClipboard/ClipGrid_GridContainer");

				_btnRaise = CreateToolCardView(terrainGrid, "BtnRaise", UnicodeIcons.ARROW_UP, "Raise", () => TriggerToolSelection(EditorTool.Raise, _btnRaise), "Elevate terrain height (1)");

				_btnLower = CreateToolCardView(terrainGrid, "BtnLower", UnicodeIcons.ARROW_DOWN, "Lower", () => TriggerToolSelection(EditorTool.Lower, _btnLower), "Lower terrain height (2)");

				_btnHeight = CreateToolCardView(terrainGrid, "BtnHeight", UnicodeIcons.ARROWS_V, "Height", () => TriggerToolSelection(EditorTool.Height, _btnHeight), "Set terrain to exact height (3)");

				_btnSmooth = CreateToolCardView(terrainGrid, "BtnSmooth", UnicodeIcons.SMOOTH, "Smooth", () => TriggerToolSelection(EditorTool.Smooth, _btnSmooth), "Smooth terrain height (4)");

				_btnPlateau = CreateToolCardView(terrainGrid, "BtnPlateau", UnicodeIcons.SQUARE, "Flatten", () => TriggerToolSelection(EditorTool.Plateau, _btnPlateau), "Flatten terrain to cursor height on click (5)");

				_btnRamp = CreateToolCardView(terrainGrid, "BtnRamp", UnicodeIcons.RAMP, "Ramp", () => TriggerToolSelection(EditorTool.Ramp, _btnRamp), "Create ramp between two points (6)");

				_btnNoise = CreateToolCardView(terrainGrid, "BtnNoise", UnicodeIcons.NOISE, "Noise", () => TriggerToolSelection(EditorTool.Noise, _btnNoise), "Add random height variations/noise to terrain (7)");

				_btnWater = CreateToolCardView(terrainGrid, "BtnWater", UnicodeIcons.WATER, "Water", () => TriggerToolSelection(EditorTool.Water, _btnWater), "Flood fill water mesh bounded by cliff walls (0)");

				_btnTextureBrush = CreateToolCardView(decoGrid, "BtnTextureBrush", UnicodeIcons.PAINT_BRUSH, "Paint", () => TriggerToolSelection(EditorTool.PaintTexture, _btnTextureBrush), "Paint terrain texture (8)");

				_btnFloodFill = CreateToolCardView(decoGrid, "BtnFloodFill", UnicodeIcons.FILL_DRIP, "Flood Fill", () => TriggerToolSelection(EditorTool.FloodFill, _btnFloodFill), "Flood fill terrain texture");

				_btnSelectArea = CreateToolCardView(clipGrid, "BtnSelectArea", UnicodeIcons.EXPAND_ARROWS, "Select Area", () => TriggerToolSelection(EditorTool.SelectArea, _btnSelectArea), "Select rectangular area");

				_btnPathingBrush = CreateToolCardView(pathGrid, "BtnPathingBrush", UnicodeIcons.BRUSH_ALT, "Brush", () => TriggerToolSelection(EditorTool.PaintPathing, _btnPathingBrush), "Paint pathing attributes onto the terrain map");

				_btnFloodFillPathing = CreateToolCardView(pathGrid, "BtnFloodFillPathing", UnicodeIcons.FILL_DRIP, "Flood Fill", () => TriggerToolSelection(EditorTool.FloodFillPathing, _btnFloodFillPathing), "Flood fill pathing attributes onto the terrain map");

				_btnSelectMove = CreateToolCardView(objGrid, "BtnSelectMove", UnicodeIcons.MOVE, "Select/Move", () => TriggerToolSelection(EditorTool.SelectMove, _btnSelectMove), "Select and move units, props, or decals");

				_btnAddObject = CreateToolCardView(objGrid, "BtnAddObject", UnicodeIcons.CUBE, "Add Object", () => _entityPaletteController?.TriggerAddObjectMode(), "Place units, props, or decals");

				_btnDeleteObject = CreateToolCardView(objGrid, "BtnDeleteObject", UnicodeIcons.TRASH, "Erase", () =>
				{
					if (GodotObject.IsInstanceValid(GameContext.Host?.SelectedEditorObject))
						DeleteSelectedObjectAction();
					else
						TriggerToolSelection(EditorTool.DeleteObject, _btnDeleteObject);
				}, "Erase units, props, or decals");

				_btnDrawCoordinate = GetNode<Button>("RightSlidePanel/RightScroll/AccordionContainer/ToolAccordion/ContentTool/PanelCoordinatesVBox/BtnDrawCoordinate");
				SetupButton(_btnDrawCoordinate, $"{UnicodeIcons.DRAW_POLYGON} DRAW COORD", () => TriggerToolSelection(EditorTool.DrawCoordinate, _btnDrawCoordinate), 11, "Drag to define a named coordinate box exposed as C# variables");

				_txtCoordinateName = GetNode<LineEdit>("RightSlidePanel/RightScroll/AccordionContainer/ToolAccordion/ContentTool/PanelCoordinatesVBox/CoordinateNameRow/TxtCoordinateName");
				_btnCommitCoordinate = GetNode<Button>("RightSlidePanel/RightScroll/AccordionContainer/ToolAccordion/ContentTool/PanelCoordinatesVBox/BtnCommitCoordinate");
				SetupButton(_btnCommitCoordinate, "+ ADD", null, 11, "Create named coordinate");
				_btnCommitCoordinate.Pressed += () =>
				{
					if (GameContext.EditorService.PendingCoordinateMinX == GameContext.EditorService.PendingCoordinateMaxX && GameContext.EditorService.PendingCoordinateMinZ == GameContext.EditorService.PendingCoordinateMaxZ)
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

					bool ok = GameContext.IEditorAPI?.CommitCoordinate(name, GameContext.EditorService.PendingCoordinateMinX, GameContext.EditorService.PendingCoordinateMinZ, GameContext.EditorService.PendingCoordinateMaxX, GameContext.EditorService.PendingCoordinateMaxZ) ?? false;
					if (ok)
					{
						RefreshCoordinateListExternal();
						ShowFeedback($"Coordinate '{name}' created.");
						_txtCoordinateName.Text = "";
					}
				};
				_btnViewCoordinates = GetNode<Button>("RightSlidePanel/RightScroll/AccordionContainer/ToolAccordion/ContentTool/PanelCoordinatesVBox/BtnViewCoordinates");
				SetupOptionButton(_btnViewCoordinates, $"{UnicodeIcons.LIST} View Existing Coordinates", () => OpenInstanceManagerDialog("Coordinates"), 11, "Open instance manager to view and locate existing defined coordinates");

				_btnCut = CreateToolCardView(clipGrid, "BtnCut", UnicodeIcons.SCISSORS, "Cut", () => GameContext.EditorService.CutSelectedArea(), "Cut selected area to clipboard (Ctrl+X)");

				_btnCopy = CreateToolCardView(clipGrid, "BtnCopy", UnicodeIcons.COPY, "Copy", () => GameContext.EditorService.CopySelectedArea(), "Copy selected area to clipboard (Ctrl+C)");

				_btnPaste = CreateToolCardView(clipGrid, "BtnPaste", UnicodeIcons.PASTE, "Paste", () => TriggerToolSelection(EditorTool.PasteArea, _btnPaste), "Paste clipboard contents onto terrain (Ctrl+V)");

				_btnEraseArea = CreateToolCardView(clipGrid, "BtnEraseArea", UnicodeIcons.TRASH, "Erase Area", () => GameContext.EditorService.EraseSelectedArea(), "Erase heights, textures and objects within selection (Delete)");

				_accordionBrush = GetNode<VBoxContainer>("RightSlidePanel/RightScroll/AccordionContainer/BrushAccordion");
				_btnHeaderBrush = GetNode<Button>("RightSlidePanel/RightScroll/AccordionContainer/BrushAccordion/BtnHeaderBrush");
				_contentBrush = GetNode<VBoxContainer>("RightSlidePanel/RightScroll/AccordionContainer/BrushAccordion/ContentBrush");
				StyleAccordionHeader(_btnHeaderBrush);
				SetupAccordion(_btnHeaderBrush, _contentBrush, TranslationServer.Translate("Global Brush Properties"));

				_sldBrushSize = GetNode<Slider>("RightSlidePanel/RightScroll/AccordionContainer/BrushAccordion/ContentBrush/BrushSizeBox/SldBrushSize");
				_lblBrushSizeValue = GetNode<Label>("RightSlidePanel/RightScroll/AccordionContainer/BrushAccordion/ContentBrush/BrushSizeBox/Header/LblBrushSizeValue");
				_brushSizeBox = GetNodeOrNull<Control>("RightSlidePanel/RightScroll/AccordionContainer/BrushAccordion/ContentBrush/BrushSizeBox");
				_sldBrushSize.DragStarted += () => _isDraggingSlider = true;
				_sldBrushSize.DragEnded += (valueChanged) => _isDraggingSlider = false;

				_sldBrushStrength = GetNode<Slider>("RightSlidePanel/RightScroll/AccordionContainer/BrushAccordion/ContentBrush/BrushStrengthBox/SldBrushStrength");
				_lblBrushStrengthValue = GetNode<Label>("RightSlidePanel/RightScroll/AccordionContainer/BrushAccordion/ContentBrush/BrushStrengthBox/Header/LblBrushStrengthValue");
				_sldBrushStrength.DragStarted += () => _isDraggingSlider = true;
				_sldBrushStrength.DragEnded += (valueChanged) => _isDraggingSlider = false;

				_btnBrushShape = GetNode<Button>("RightSlidePanel/RightScroll/AccordionContainer/BrushAccordion/ContentBrush/ShapeMirrorGrid/BtnBrushShape");
				SetupOptionButton(_btnBrushShape, $"{UnicodeIcons.SQUARE} BRUSH: SQUARE", () =>
				{
					if (GameContext.Host != null)
					{
						GameContext.Host.EditorBrushIsSquare = !GameContext.Host.EditorBrushIsSquare;
						GameContext.Host.UpdateBrushMesh();
						UpdateBrushShapeExternal(GameContext.Host.EditorBrushIsSquare);
					}
				}, 11, "Toggle brush shape between circular and square (B)");

				_optMirrorMode = GetNode<OptionButton>("RightSlidePanel/RightScroll/AccordionContainer/BrushAccordion/ContentBrush/ShapeMirrorGrid/OptMirrorMode");
				SetupMirrorDropdown(_optMirrorMode, "Select terrain and object mirroring symmetry mode");

				_chkBlockMode = GetNode<CheckBox>("RightSlidePanel/RightScroll/AccordionContainer/BrushAccordion/ContentBrush/ChkBlockMode");
				_chkBlockMode.Toggled += (toggled) =>
				{
					if (GameContext.Host != null)
						GameContext.Host.EditorBlockMode = toggled;

					ShowFeedback(toggled ? "Block Mode: Enabled" : "Block Mode: Disabled");
					UpdateBlockStepVisibility();
					UpdateBrushStrengthVisibility();
					if (GameContext.Host != null)
						UpdateSidebarMorph(GameContext.Host.ActiveEditorTool);
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
				lblWaterHeightTitle.SizeFlagsHorizontal = SizeFlags.ExpandFill;
				lblWaterHeightTitle.AddThemeFontSizeOverride("font_size", 10);
				headerWaterHeight.AddChild(lblWaterHeightTitle);

				_lblWaterHeightValue = new Label();
				_lblWaterHeightValue.Name = "LblWaterHeightValue";
				_lblWaterHeightValue.Text = "0.9m";
				_lblWaterHeightValue.AddThemeFontSizeOverride("font_size", 11);
				headerWaterHeight.AddChild(_lblWaterHeightValue);

				_sldWaterHeight = new HSlider();
				_sldWaterHeight.Name = "SldWaterHeight";
				_sldWaterHeight.FocusMode = FocusModeEnum.None;
				_sldWaterHeight.MinValue = 0.1;
				_sldWaterHeight.MaxValue = 15.0;
				_sldWaterHeight.Step = 0.1;
				_sldWaterHeight.Value = 0.9;
				_sldWaterHeight.ValueChanged += (val) =>
				{
					float fVal = (float)val;
					_lblWaterHeightValue.Text = fVal.ToString("F1") + "m";
					if (GameContext.Host != null) GameContext.EditorService.EditorWaterHeight = fVal;
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
				_btnWaterActionAdd.SizeFlagsHorizontal = SizeFlags.ExpandFill;
				_btnWaterActionAdd.FocusMode = FocusModeEnum.None;
				_btnWaterActionAdd.AddThemeFontSizeOverride("font_size", 11);
				_btnWaterActionAdd.TooltipText = TranslationServer.Translate("Add water to terrain");
				StyleRowButton(_btnWaterActionAdd);
				_btnWaterActionAdd.AddThemeStyleboxOverride("normal", _highlightStyle);
				waterActionButtonRow.AddChild(_btnWaterActionAdd);

				_btnWaterActionRemove = new Button();
				_btnWaterActionRemove.Name = "BtnWaterActionRemove";
				_btnWaterActionRemove.Set("icon_max_width", 0);
				_btnWaterActionRemove.Text = "- " + TranslationServer.Translate("Remove");
				_btnWaterActionRemove.SizeFlagsHorizontal = SizeFlags.ExpandFill;
				_btnWaterActionRemove.FocusMode = FocusModeEnum.None;
				_btnWaterActionRemove.AddThemeFontSizeOverride("font_size", 11);
				_btnWaterActionRemove.TooltipText = TranslationServer.Translate("Remove water from terrain");
				StyleRowButton(_btnWaterActionRemove);
				waterActionButtonRow.AddChild(_btnWaterActionRemove);

				_btnWaterActionAdd.Pressed += () =>
				{
					GameContext.EditorService.IsWaterRemoveAction = false;
					_btnWaterActionAdd.AddThemeStyleboxOverride("normal", _highlightStyle);
					_btnWaterActionRemove.RemoveThemeStyleboxOverride("normal");
					if (_waterHeightBox != null) _waterHeightBox.Visible = true;
					if (_waterModeBox != null) _waterModeBox.Visible = true;
					if (_btnWaterProfiles != null) _btnWaterProfiles.Visible = true;
				};

				_btnWaterActionRemove.Pressed += () =>
				{
					GameContext.EditorService.IsWaterRemoveAction = true;
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
				_optWaterMode.SizeFlagsHorizontal = SizeFlags.ExpandFill;
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

					if (GameContext.Host != null)
					{
						GameContext.Host.EditorWaterMode = WaterType.Shallow;
						GameContext.Host.ActiveWaterProfileIndex = profIdx;
					}
				};
				_contentWater.AddChild(_waterModeBox);
				RefreshWaterSwatches();

				_btnWaterProfiles = new Button();
				_btnWaterProfiles.Name = "BtnWaterProfiles";
				_btnWaterProfiles.Set("icon_max_width", 0);
				_btnWaterProfiles.Text = "⚙ " + TranslationServer.Translate("Water Profiles");
				_btnWaterProfiles.SizeFlagsHorizontal = SizeFlags.ExpandFill;
				_btnWaterProfiles.TooltipText = TranslationServer.Translate("Configure Liquid / Water Uber Profiles");
				_btnWaterProfiles.FocusMode = FocusModeEnum.None;
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

				ChkApplyGroundTexture = new CheckBox();
				ChkApplyGroundTexture.Name = "ChkApplyGroundTexture";
				ChkApplyGroundTexture.Text = TranslationServer.Translate("Ground (Q) [shift]");
				ChkApplyGroundTexture.ButtonPressed = true;
				ChkApplyGroundTexture.FocusMode = FocusModeEnum.None;
				ChkApplyGroundTexture.AddThemeFontSizeOverride("font_size", 10);
				UIStyle.ApplyCheckboxStyle(ChkApplyGroundTexture);

				ChkApplyCliffTexture = new CheckBox();
				ChkApplyCliffTexture.Name = "ChkApplyCliffTexture";
				ChkApplyCliffTexture.Text = TranslationServer.Translate("Cliff (Q) [control]");
				ChkApplyCliffTexture.ButtonPressed = true;
				ChkApplyCliffTexture.FocusMode = FocusModeEnum.None;
				ChkApplyCliffTexture.AddThemeFontSizeOverride("font_size", 10);
				UIStyle.ApplyCheckboxStyle(ChkApplyCliffTexture);

				ChkApplyGroundTexture.Toggled += (toggled) =>
				{
					if (!toggled && (ChkApplyCliffTexture == null || !ChkApplyCliffTexture.ButtonPressed))
					{
						ChkApplyCliffTexture.SetPressedNoSignal(true);
					}

					UpdateTextureLabels();
				};

				ChkApplyCliffTexture.Toggled += (toggled) =>
				{
					if (!toggled && (ChkApplyGroundTexture == null || !ChkApplyGroundTexture.ButtonPressed))
					{
						ChkApplyGroundTexture.SetPressedNoSignal(true);
					}

					UpdateTextureLabels();
				};

				if (_containerTextureSettings != null && _lblTerrainTexture != null && _lblCliffTexture != null)
				{
					RowGroundTexture = new HBoxContainer();
					RowGroundTexture.Name = "RowGroundTexture";
					RowGroundTexture.AddThemeConstantOverride("separation", 6);

					int idxTerrain = _lblTerrainTexture.GetIndex();
					_containerTextureSettings.RemoveChild(_lblTerrainTexture);
					RowGroundTexture.AddChild(_lblTerrainTexture);
					_lblTerrainTexture.SizeFlagsHorizontal = SizeFlags.ExpandFill;
					RowGroundTexture.AddChild(ChkApplyGroundTexture);
					_containerTextureSettings.AddChild(RowGroundTexture);
					_containerTextureSettings.MoveChild(RowGroundTexture, idxTerrain);

					RowCliffTexture = new HBoxContainer();
					RowCliffTexture.Name = "RowCliffTexture";
					RowCliffTexture.AddThemeConstantOverride("separation", 6);

					int idxCliff = _lblCliffTexture.GetIndex();
					_containerTextureSettings.RemoveChild(_lblCliffTexture);
					RowCliffTexture.AddChild(_lblCliffTexture);
					_lblCliffTexture.SizeFlagsHorizontal = SizeFlags.ExpandFill;
					RowCliffTexture.AddChild(ChkApplyCliffTexture);
					_containerTextureSettings.AddChild(RowCliffTexture);
					_containerTextureSettings.MoveChild(RowCliffTexture, idxCliff);
				}

				var btnTextureSwap = GetNode<Button>("RightSlidePanel/RightScroll/AccordionContainer/ToolSettingsAccordion/ContentToolSettings/ContainerTexture/BtnTextureSwap");
				SetupOptionButton(btnTextureSwap, $"{UnicodeIcons.REFRESH} SWAP TEXTURES (GLOBAL)", () =>
				{
					if (GameContext.Host != null)
					{
						GameContext.Host.SwapTexturesExternal(GameContext.EditorService.EditorPaintTextureIndex, GameContext.EditorService.EditorCliffPaintTextureIndex);
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
					_scrollSwatches.SizeFlagsHorizontal = SizeFlags.ExpandFill;
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
					modePanel.SizeFlagsHorizontal = SizeFlags.ExpandFill;
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
				chkPasteEntities.Text = TranslationServer.Translate("Objects");
				chkPasteEntities.ButtonPressed = true;
				var chkPastePathing = GetNode<CheckBox>("RightSlidePanel/RightScroll/AccordionContainer/ToolSettingsAccordion/ContentToolSettings/ContainerPaste/PasteOptionsBox/ChkPastePathing");
				chkPastePathing.Text = TranslationServer.Translate("Pathing");
				chkPastePathing.ButtonPressed = true;

				chkPasteTextures.Toggled += (toggled) =>
				{
					if (GameContext.Host != null) GameContext.EditorService.PasteOptionTextures = toggled;
				};
				chkPasteHeights.Toggled += (toggled) =>
				{
					if (GameContext.Host != null) GameContext.EditorService.PasteOptionHeights = toggled;
				};
				chkPasteEntities.Toggled += (toggled) =>
				{
					if (GameContext.Host != null) GameContext.EditorService.PasteOptionEntities = toggled;
				};
				chkPastePathing.Toggled += (toggled) =>
				{
					if (GameContext.Host != null) GameContext.Host.PasteOptionPathing = toggled;
				};

				_lblPasteRotation = GetNode<Label>("RightSlidePanel/RightScroll/AccordionContainer/ToolSettingsAccordion/ContentToolSettings/ContainerPaste/PasteOptionsBox/PasteRotationBox/Header/LblPasteRotationValue");
				_sldPasteRotation = GetNode<HSlider>("RightSlidePanel/RightScroll/AccordionContainer/ToolSettingsAccordion/ContentToolSettings/ContainerPaste/PasteOptionsBox/PasteRotationBox/SldPasteRotation");
				_sldPasteRotation.ValueChanged += (val) =>
				{
					float fVal = (float)val;
					_lblPasteRotation.Text = fVal.ToString("F0") + "°";
					if (GameContext.Host != null)
					{
						GameContext.EditorService.EditorPasteRotation = fVal;
					}
				};
				_sldPasteRotation.DragStarted += () => _isDraggingSlider = true;
				_sldPasteRotation.DragEnded += (valueChanged) => _isDraggingSlider = false;

				_btnClipboardBrushShape = new Button();
				_btnClipboardBrushShape.Name = "BtnClipboardBrushShape";
				_btnClipboardBrushShape.Set("icon_max_width", 0);
				SetupOptionButton(_btnClipboardBrushShape, GameContext.Host != null && !GameContext.Host.EditorBrushIsSquare ? $"{UnicodeIcons.CIRCLE} BRUSH: CIRCLE" : $"{UnicodeIcons.SQUARE} BRUSH: SQUARE", () =>
				{
					if (GameContext.Host != null)
					{
						GameContext.Host.EditorBrushIsSquare = !GameContext.Host.EditorBrushIsSquare;
						UpdateBrushShapeExternal(GameContext.Host.EditorBrushIsSquare);
						ShowFeedback(GameContext.Host.EditorBrushIsSquare ? TranslationServer.Translate("Brush Shape: SQUARE") : TranslationServer.Translate("Brush Shape: CIRCLE"));
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
				_lblPasteTelemetry.SizeFlagsHorizontal = SizeFlags.ExpandFill;
				_lblPasteTelemetry.Text = "";

				var pasteOptionsBox = _containerPasteSettings.GetNodeOrNull<VBoxContainer>("PasteOptionsBox");
				if (pasteOptionsBox != null)
				{
					var shapeMirrorGrid = new GridContainer();
					shapeMirrorGrid.Name = "ClipboardShapeMirrorGrid";
					shapeMirrorGrid.Columns = 2;
					shapeMirrorGrid.AddThemeConstantOverride("h_separation", 6);
					shapeMirrorGrid.AddThemeConstantOverride("v_separation", 4);
					shapeMirrorGrid.SizeFlagsHorizontal = SizeFlags.ExpandFill;
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
				var lblPlacementRotateTitle = GetNodeOrNull<Label>("RightSlidePanel/RightScroll/AccordionContainer/PlacementAccordion/ContentPlacement/PlacementRotateBox/Header/LblRotateTitle");
				if (lblPlacementRotateTitle != null) lblPlacementRotateTitle.Text = TranslationServer.Translate("Rotation (Shift + Mouse_Wheel)");
				var lblPlacementScaleTitle = GetNodeOrNull<Label>("RightSlidePanel/RightScroll/AccordionContainer/PlacementAccordion/ContentPlacement/PlacementScaleBox/Header/LblScaleTitle");
				if (lblPlacementScaleTitle != null) lblPlacementScaleTitle.Text = TranslationServer.Translate("Scale (Alt + Mouse_Wheel)");

				_btnToggleSnap = GetNode<Button>("RightSlidePanel/RightScroll/AccordionContainer/PlacementAccordion/ContentPlacement/BtnToggleSnap");
				SetupOptionButton(_btnToggleSnap, $"{UnicodeIcons.TABLE} SNAP TO GRID: OFF", () =>
				{
					if (GameContext.Host != null)
					{
						GameContext.Host.EditorSnapToGrid = !GameContext.Host.EditorSnapToGrid;
						UpdateGridSnapExternal(GameContext.Host.EditorSnapToGrid);
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

				_placementPlayerContainer = new HBoxContainer();
				_placementPlayerContainer.Name = "PlacementPlayerOwnerContainer";
				_placementPlayerContainer.AddThemeConstantOverride("separation", 6);
				_placementPlayerContainer.Visible = false;

				var lblPlacementPlayer = new Label();
				lblPlacementPlayer.Name = "LblPlacementPlayer";
				lblPlacementPlayer.Text = TranslationServer.Translate("Player");
				lblPlacementPlayer.CustomMinimumSize = new Vector2(50, 0);
				lblPlacementPlayer.AddThemeFontSizeOverride("font_size", 11);
				_placementPlayerContainer.AddChild(lblPlacementPlayer);

				_optPlacementPlayer = new OptionButton();
				_optPlacementPlayer.Name = "OptPlacementPlayer";
				_optPlacementPlayer.Set("icon_max_width", 0);
				_optPlacementPlayer.SizeFlagsHorizontal = SizeFlags.ExpandFill;
				_optPlacementPlayer.AddThemeFontSizeOverride("font_size", 10);
				_optPlacementPlayer.TooltipText = TranslationServer.Translate("Owner of the next placed unit. Choose 0 NEUTRAL_TAN (gaia) to place neutral objects.");

				for (int i = 0; i < PlayerColorConfig.Palette.Length; i++)
				{
					_optPlacementPlayer.AddItem(PlayerColorConfig.GetPaletteLabel(i), i);
				}

				_optPlacementPlayer.ItemSelected += (long index) =>
				{
					GameContext.EditorService.SetPlaceUnitPlayerIndexDomain((int)index);
				};

				_placementPlayerContainer.AddChild(_optPlacementPlayer);
				if (_contentPlacement != null)
				{
					_contentPlacement.AddChild(_placementPlayerContainer);
					_contentPlacement.MoveChild(_placementPlayerContainer, _optPlacementMirrorMode.GetIndex() + 1);
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
					if (GameContext.Host != null)
					{
						UpdateSidebarMorph(GameContext.Host.ActiveEditorTool);
					}
				};

				_sldClumpDensity.ValueChanged += (val) =>
				{
					float fVal = (float)val;
					_lblClumpDensityValue.Text = fVal.ToString("F0");
					if (GameContext.Host != null) GameContext.Host.EditorClumpCount = fVal;
				};
				_sldClumpDensity.DragStarted += () => _isDraggingSlider = true;
				_sldClumpDensity.DragEnded += (valueChanged) => _isDraggingSlider = false;

				_sldClumpScaleVar.ValueChanged += (val) =>
				{
					float fVal = (float)val;
					_lblClumpScaleVarValue.Text = fVal.ToString("F2");
					if (GameContext.Host != null) GameContext.Host.EditorClumpScale = fVal;
				};
				_sldClumpScaleVar.DragStarted += () => _isDraggingSlider = true;
				_sldClumpScaleVar.DragEnded += (valueChanged) => _isDraggingSlider = false;

				_sldPlacementRotate.DragStarted += () => _isDraggingSlider = true;
				_sldPlacementRotate.DragEnded += (valueChanged) => _isDraggingSlider = false;
				_sldPlacementScale.DragStarted += () => _isDraggingSlider = true;
				_sldPlacementScale.DragEnded += (valueChanged) => _isDraggingSlider = false;

				TriggerToolSelection(EditorTool.Raise, _btnRaise);

				_feedbackLabel.Modulate = new Color(1, 1, 1, 0);
				Input.MouseMode = Input.MouseModeEnum.Visible;


				_entityPaletteController = new MapEditorEntityPaletteController(this, _containerCategorySelector, _btnAddObject);

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

				if (GameContext.Host != null)
				{
					UpdateRotationExternal(GameContext.EditorService.EditorPlacementRotation);
					UpdateScaleExternal(GameContext.Host.EditorPlacementScale);
					UpdateGridSnapExternal(GameContext.Host.EditorSnapToGrid);
					UpdatePasteRotationExternal(GameContext.EditorService.EditorPasteRotation);
					UpdatePasteReflectionExternal(GameContext.EditorService.EditorPasteReflection);
					UpdateMirrorDropdowns();
					UpdateSymmetrySpokesDropdowns();
					UpdatePolarRadialStepButtonText();
					UpdatePolarRingSpacingButtonText();
				}

				if (!_agreementShownThisSession)
				{
					ShowAgreementModal(() => { CheckPostLaunchPrompts(); });
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

				if (GameContext.SessionState.ReturningFromTest)
				{
					_sldBrushSize.Value = GameContext.SessionState.SavedBrushRadius;
					_sldBrushStrength.Value = GameContext.SessionState.SavedBrushStrength;

					if (GameContext.SessionState.SavedActiveTool == EditorTool.PlaceUnit ||
					    GameContext.SessionState.SavedActiveTool == EditorTool.PlaceProp ||
					    GameContext.SessionState.SavedActiveTool == EditorTool.PlaceDecal)
					{
						_entityPaletteController?.SelectCategoryItemExternal(GameContext.SessionState.SavedEntityCategory, GameContext.SessionState.SavedActivePlaceId);
					}
					else
					{
						Button toolBtn = GetButtonForTool(GameContext.SessionState.SavedActiveTool, GameContext.SessionState.SavedActivePlaceId);
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
			return GameContext.EditorService.IsWaterRemoveAction;
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
			if (BtnEditVfx != null) BtnEditVfx.Visible = false;
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

			if (GameContext.Host != null)
			{
				TriggerToolSelection(GameContext.Host.ActiveEditorTool, _activeToolButton, GameContext.Host.ActivePlaceId);
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

			if (selected is Unit3D unit)
			{
				typeStr = unit.IsBuilding ? "BUILDING" : "UNIT";
				idStr = Path.GetFileName(unit.UnitId).ToUpper();
				if (unit.IsResource && GameHost.ResourceRegistry.TryGetValue(unit.UnitId, out var resMeta) && !string.IsNullOrEmpty(resMeta.Name))
					nameStr = resMeta.Name.ToUpper();
				else if (unit.IsBuilding && GameHost.BuildingRegistry.TryGetValue(unit.UnitId, out var bldMeta) && !string.IsNullOrEmpty(bldMeta.Name))
					nameStr = bldMeta.Name.ToUpper();
				else if (GameHost.UnitRegistry.TryGetValue(unit.UnitId, out var unitMeta) && !string.IsNullOrEmpty(unitMeta.Name))
					nameStr = unitMeta.Name.ToUpper();
				else
					nameStr = idStr;
			}
			else if (selected is Prop3D prop)
			{
				typeStr = "PROP";
				idStr = Path.GetFileName(prop.PropId).ToUpper();
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
				nameStr = Path.GetFileName(decal.Name).ToUpper();
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
			if (GameContext.Host == null) return;
			var selected = GameContext.Host.SelectedEditorObject;

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

			if (selected is Unit3D unitOwner && _playerOwnerContainer != null && _optPlayerOwner != null)
			{
				_playerOwnerContainer.Visible = true;
				IsUpdatingInspectorUI = true;
				_optPlayerOwner.Selected = Mathf.Clamp(unitOwner.Player, 0, PlayerColorConfig.Palette.Length - 1);
				IsUpdatingInspectorUI = false;
			}
			else if (_playerOwnerContainer != null) _playerOwnerContainer.Visible = false;

			if (_btnShowCoverage != null)
			{
				bool isUnit = selected is Unit3D;
				_btnShowCoverage.Visible = isUnit;
				if (isUnit && GameContext.Host != null)
				{
					_btnShowCoverage.SetPressedNoSignal(GameContext.Host.EditorCoverageOverlayEnabled);
					_btnShowCoverage.Text = GameContext.Host.EditorCoverageOverlayEnabled ? TranslationServer.Translate("◉ VISION/ATTACK RANGES: ON") : TranslationServer.Translate("◉ VISION/ATTACK RANGES: OFF");
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
				if (selected is Unit3D uObj && uObj.ModelNode != null) modelRoot = uObj.ModelNode;

				var validation = SkeletonValidator.Validate(modelRoot);
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
			}
			else
			{
				if (_rigStatusContainer != null) _rigStatusContainer.Visible = false;
			}

			string assetKey = GameContext.Host.GetSelectedEntityOrAssetKey(selected);
			bool isDecal = selected is Decal || (GameContext.Host != null && GameContext.Host.FindDecalInParentChain(selected) != null);
			if (!string.IsNullOrEmpty(assetKey) && !isDecal && _btnOpenGlobalOverrides != null)
			{
				_btnOpenGlobalOverrides.Visible = true;
				_btnOpenGlobalOverrides.TooltipText = string.Format(TranslationServer.Translate("Edit global model scale, offsets, and shaders for {0}"), assetKey);
			}
			else if (_btnOpenGlobalOverrides != null) _btnOpenGlobalOverrides.Visible = false;

			if (selected is ProceduralVfxInstance3D && BtnEditVfx != null)
			{
				BtnEditVfx.Visible = true;
				BtnEditVfx.TooltipText = TranslationServer.Translate("Open Procedural VFX Studio to edit this effect");
			}
			else if (BtnEditVfx != null) BtnEditVfx.Visible = false;
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
			GameContext.EditorService.SetPendingCoordinatesDomain(minX, minZ, maxX, maxZ);
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
			var envService = GameContext.EnvironmentService;
			if (envService != null)
			{
				var (timeName, icon) = envService.SetEnvironmentLightingDomain(index);
				ShowFeedback(string.Format(TranslationServer.Translate("Lighting: {0} {1}"), icon, TranslationServer.Translate(timeName)));
			}
		}

		private void SetEnvironmentWeather(int index)
		{
			var envService = GameContext.EnvironmentService;
			if (envService != null)
			{
				var (applied, icon) = envService.SetEnvironmentWeatherDomain(index);
				ShowFeedback(string.Format(TranslationServer.Translate("Weather: {0} {1}"), icon, TranslationServer.Translate(applied.Capitalize())));
			}
		}

		public void ToggleVSCodeEditor()
		{
			if (OperatingSystem.IsWindows())
			{
				if (GameContext.VSCode.IsVisible)
				{
					GameContext.VSCode.Focus();
				}
				else
				{
					GenerateVSCodeFilesExternal();
					GameContext.VSCode.SetVisible(true);
				}
			}
		}


		public void UndoAction()
		{
			GameContext.EditorService.UndoDomain();
			ShowFeedback("Undo Action performed");
		}

		public void RedoAction()
		{
			GameContext.EditorService.RedoDomain();
			ShowFeedback("Redo Action performed");
		}

		public void DeleteSelectedObjectAction()
		{
			DeleteSelectedObject();
		}

		private void DeleteSelectedObject()
		{
			EditorOperationResult result = GameContext.IEditorAPI.DeleteSelectedObject();
			if (!string.IsNullOrEmpty(result.Error))
			{
				ShowFeedback(result.Error);
				return;
			}

			if (result.Success)
			{
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

		public void SetSpawnPlayer(int playerIndex)
		{
			if (GameContext.Host == null) return;

			GameContext.EditorService.SetPlaceUnitPlayerIndexDomain(playerIndex);
			SyncPlacementPlayerSelection(GameContext.EditorService.PlaceUnitPlayerIndex);
		}

		private void SyncPlacementPlayerSelection(int playerIndex)
		{
			if (_optPlacementPlayer == null) return;

			_optPlacementPlayer.Selected = Mathf.Clamp(playerIndex, 0, PlayerColorConfig.Palette.Length - 1);
		}

		public void SelectCategoryItemExternal(string category, string filename)
		{
			_entityPaletteController?.SelectCategoryItemExternal(category, filename);
		}

		public void SelectPickedDecalView(string decalId)
		{
			_entityPaletteController?.SelectCategoryItemExternal("Decals", decalId);
		}

		public EditorPaintSurfaceMode GetPaintSurfaceMode()
		{
			bool applyGround = ChkApplyGroundTexture == null || ChkApplyGroundTexture.ButtonPressed;
			bool applyCliff = ChkApplyCliffTexture == null || ChkApplyCliffTexture.ButtonPressed;

			if (applyGround && applyCliff) return EditorPaintSurfaceMode.GroundAndCliff;
			if (applyGround) return EditorPaintSurfaceMode.GroundOnly;
			return EditorPaintSurfaceMode.CliffOnly;
		}

		public void CyclePaintSurfaceMode()
		{
			EditorPaintSurfaceMode nextMode = GetPaintSurfaceMode() switch
			{
				EditorPaintSurfaceMode.GroundOnly => EditorPaintSurfaceMode.GroundAndCliff,
				EditorPaintSurfaceMode.GroundAndCliff => EditorPaintSurfaceMode.CliffOnly,
				_ => EditorPaintSurfaceMode.GroundOnly
			};

			ApplyPaintSurfaceMode(nextMode);
			ShowFeedback(string.Format(TranslationServer.Translate("Painting {0}"), TranslatePaintSurfaceMode(nextMode)));
		}

		private void ApplyPaintSurfaceMode(EditorPaintSurfaceMode mode)
		{
			if (ChkApplyGroundTexture == null || ChkApplyCliffTexture == null) return;

			ChkApplyGroundTexture.SetPressedNoSignal(mode != EditorPaintSurfaceMode.CliffOnly);
			ChkApplyCliffTexture.SetPressedNoSignal(mode != EditorPaintSurfaceMode.GroundOnly);
			UpdateTextureLabels();
		}

		private static string TranslatePaintSurfaceMode(EditorPaintSurfaceMode mode)
		{
			return mode switch
			{
				EditorPaintSurfaceMode.GroundOnly => TranslationServer.Translate("ground only"),
				EditorPaintSurfaceMode.CliffOnly => TranslationServer.Translate("cliff only"),
				_ => TranslationServer.Translate("ground and cliff")
			};
		}

		public void UpdateBrushShapeExternal(bool isSquare)
		{
			if (GameContext.EditorService.TrySetBrushShapeExternal(isSquare))
			{
				if (_btnBrushShape != null)
				{
					_btnBrushShape.Text = isSquare ? TranslationServer.Translate($"{UnicodeIcons.SQUARE} BRUSH: SQUARE") : TranslationServer.Translate($"{UnicodeIcons.CIRCLE} BRUSH: CIRCLE");
				}

				if (_btnClipboardBrushShape != null)
				{
					_btnClipboardBrushShape.Text = isSquare ? TranslationServer.Translate($"{UnicodeIcons.SQUARE} BRUSH: SQUARE") : TranslationServer.Translate($"{UnicodeIcons.CIRCLE} BRUSH: CIRCLE");
				}

				if (GameContext.Host != null && GameContext.Host.GroundTerrain != null && GameContext.EditorService != null && GameContext.EditorService.SelectionStart != null && GameContext.EditorService.SelectionEnd != null)
				{
					var (minX, minZ, maxX, maxZ) = GameContext.EditorService.GetCurrentSelectionBounds();
					GameContext.Host.RebuildSelectionHighlightMeshExternal(minX, minZ, maxX, maxZ);
				}
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

		public void SelectToolFromHotkey(EditorTool tool)
		{
			Button targetBtn = null;
			switch (tool)
			{
				case EditorTool.Raise: targetBtn = _btnRaise; break;
				case EditorTool.Lower: targetBtn = _btnLower; break;
				case EditorTool.Height: targetBtn = _btnHeight; break;
				case EditorTool.Smooth: targetBtn = _btnSmooth; break;
				case EditorTool.Plateau: targetBtn = _btnPlateau; break;
				case EditorTool.Ramp: targetBtn = _btnRamp; break;
				case EditorTool.Noise: targetBtn = _btnNoise; break;
				case EditorTool.Water: targetBtn = _btnWater; break;
				case EditorTool.PaintTexture: targetBtn = _btnTextureBrush; break;
				case EditorTool.FloodFill: targetBtn = _btnFloodFill; break;
				case EditorTool.PaintPathing: targetBtn = _btnPathingBrush; break;
				case EditorTool.FloodFillPathing: targetBtn = _btnFloodFillPathing; break;
				case EditorTool.DrawCoordinate: targetBtn = _btnDrawCoordinate; break;
				case EditorTool.SelectArea: targetBtn = _btnSelectArea; break;
				case EditorTool.PasteArea: targetBtn = _btnPaste; break;
				case EditorTool.PlaceUnit:
				case EditorTool.PlaceProp:
				case EditorTool.PlaceDecal:
					targetBtn = _btnAddObject;
					break;
				case EditorTool.DeleteObject: targetBtn = _btnDeleteObject; break;
				case EditorTool.SelectMove: targetBtn = _btnSelectMove; break;
				case EditorTool.Eyedropper: targetBtn = _btnEyedropper; break;
				case EditorTool.Measure: targetBtn = _btnTapeMeasure; break;
			}

			if (targetBtn != null)
			{
				TriggerToolSelection(tool, targetBtn, GameContext.Host?.ActivePlaceId ?? "");
			}
		}

		private Button GetButtonForTool(EditorTool tool, string placeId)
		{
			return tool switch
			{
				EditorTool.Raise => _btnRaise,
				EditorTool.Lower => _btnLower,
				EditorTool.Height => _btnHeight,
				EditorTool.Smooth => _btnSmooth,
				EditorTool.Plateau => _btnPlateau,
				EditorTool.Ramp => _btnRamp,
				EditorTool.Noise => _btnNoise,
				EditorTool.Water => _btnWater,
				EditorTool.PaintPathing => _btnPathingBrush,
				EditorTool.FloodFillPathing => _btnFloodFillPathing,
				EditorTool.DrawCoordinate => _btnDrawCoordinate,
				EditorTool.SelectArea => _btnSelectArea,
				EditorTool.SelectMove => _btnSelectMove,
				EditorTool.DeleteObject => _btnDeleteObject,
				EditorTool.PaintTexture => GetTextureSwatchButton(placeId),
				EditorTool.FloodFill => _btnFloodFill,
				EditorTool.Eyedropper => _btnEyedropper,
				_ => null
			};
		}

		private Button GetTextureSwatchButton(string placeId)
		{
			for (int i = 0; i < GameContext.EditorService.SwatchPaths.Count && i < _swatchButtons.Count; i++)
			{
				if (GameContext.EditorService.SwatchPaths[i] == placeId)
				{
					return _swatchButtons[i];
				}
			}

			return _btnTextureBrush;
		}

		private async Task<bool> PromptAndUpgradeMapIfNeededAsync(string selectedFolder)
		{
			var upgradeService = GameContext.MapUpgradeService ?? GameContext.MapUpgradeService;
			if (upgradeService == null || !upgradeService.NeedsUpgrade(selectedFolder, out string currentVersion, out string targetVersion))
			{
				return true;
			}

			if (upgradeService.IsMapNewerThanGame(currentVersion, targetVersion))
			{
				ShowMapNewerThanGameErrorDialog(selectedFolder, currentVersion, targetVersion);
				return false;
			}

			var tcs = new TaskCompletionSource<bool>();

			var popup = new Panel();
			popup.Name = "Popup_Panel";
			popup.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
			popup.AddThemeStyleboxOverride("panel", UIStyle.CreateBgGradient());
			AddChild(popup);

			var cardPanel = new Panel();
			cardPanel.Name = "CardPanel_Panel";
			cardPanel.CustomMinimumSize = new Vector2(540, 290);
			cardPanel.SetAnchorsAndOffsetsPreset(LayoutPreset.Center);
			cardPanel.AddThemeStyleboxOverride("panel", UIStyle.CreateStonePanel(true));
			popup.AddChild(cardPanel);

			var vbox = new VBoxContainer();
			vbox.Name = "Vbox_VBoxContainer";
			vbox.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
			vbox.CustomMinimumSize = new Vector2(500, 260);
			vbox.SizeFlagsHorizontal = SizeFlags.ExpandFill;
			vbox.SizeFlagsVertical = SizeFlags.ExpandFill;
			cardPanel.AddChild(vbox);

			vbox.AddChild(new Control { CustomMinimumSize = new Vector2(0, 15) });

			var titleLabel = new Label();
			titleLabel.Name = "TitleLabel_Label";
			UIStyle.ApplyTitle(titleLabel, Tr("MAP BUILD MISMATCH"), 20);
			titleLabel.AddThemeColorOverride("font_color", new Color(0.95f, 0.4f, 0.3f));
			vbox.AddChild(titleLabel);

			vbox.AddChild(new Control { CustomMinimumSize = new Vector2(0, 8) });

			var descLabel = new Label();
			descLabel.Name = "DescLabel_Label";
			descLabel.Text = $"{string.Format(Tr("Map: {0}"), Path.GetFileName(selectedFolder))}\n{string.Format(Tr("Map Build: {0} | Editor Build: {1}"), currentVersion, targetVersion)}\n\n{Tr("This map format needs to be upgraded before it can be opened. Would you like to upgrade now?")}";
			descLabel.HorizontalAlignment = HorizontalAlignment.Center;
			descLabel.AddThemeFontSizeOverride("font_size", 13);
			descLabel.AddThemeColorOverride("font_color", new Color(0.9f, 0.9f, 0.95f));
			vbox.AddChild(descLabel);

			var progressBar = new ProgressBar();
			progressBar.Name = "ProgressBar_ProgressBar";
			progressBar.CustomMinimumSize = new Vector2(460, 20);
			progressBar.MinValue = 0;
			progressBar.MaxValue = 100;
			progressBar.Value = 0;
			progressBar.Visible = false;
			vbox.AddChild(progressBar);

			var statusLabel = new Label();
			statusLabel.Name = "StatusLabel_Label";
			statusLabel.HorizontalAlignment = HorizontalAlignment.Center;
			statusLabel.AddThemeFontSizeOverride("font_size", 12);
			statusLabel.AddThemeColorOverride("font_color", UIStyle.ColorGoldDull);
			statusLabel.Visible = false;
			vbox.AddChild(statusLabel);

			vbox.AddChild(new Control { CustomMinimumSize = new Vector2(0, 12) });

			var buttonRow = new HBoxContainer();
			buttonRow.Name = "ButtonRow_HBoxContainer";
			buttonRow.Alignment = BoxContainer.AlignmentMode.Center;
			buttonRow.AddThemeConstantOverride("separation", 20);
			vbox.AddChild(buttonRow);

			var upgradeBtn = new Button();
			upgradeBtn.Name = "UpgradeBtn_Button";
			upgradeBtn.Flat = false;
			upgradeBtn.AddThemeConstantOverride("icon_max_width", 0);
			UIStyle.ApplyButtonStyleBoxes(upgradeBtn);
			UIStyle.ApplyButtonText(upgradeBtn, Tr("UPGRADE MAP"), 14);
			upgradeBtn.CustomMinimumSize = new Vector2(160, 38);
			buttonRow.AddChild(upgradeBtn);

			var cancelBtn = new Button();
			cancelBtn.Name = "CancelBtn_Button";
			cancelBtn.Flat = false;
			cancelBtn.AddThemeConstantOverride("icon_max_width", 0);
			UIStyle.ApplyButtonStyleBoxes(cancelBtn);
			UIStyle.ApplyButtonText(cancelBtn, Tr("CANCEL"), 14);
			cancelBtn.CustomMinimumSize = new Vector2(120, 38);
			buttonRow.AddChild(cancelBtn);

			cancelBtn.Pressed += () =>
			{
				GameContext.UserInterface?.PlayClickSound();
				popup.QueueFree();
				tcs.TrySetResult(false);
			};

			upgradeBtn.Pressed += async () =>
			{
				GameContext.UserInterface?.PlayClickSound();
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
			popup.Name = "Popup_Panel";
			popup.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
			popup.AddThemeStyleboxOverride("panel", UIStyle.CreateBgGradient());
			AddChild(popup);

			var cardPanel = new Panel();
			cardPanel.Name = "CardPanel_Panel";
			cardPanel.CustomMinimumSize = new Vector2(520, 240);
			cardPanel.SetAnchorsAndOffsetsPreset(LayoutPreset.Center);
			cardPanel.AddThemeStyleboxOverride("panel", UIStyle.CreateStonePanel(true));
			popup.AddChild(cardPanel);

			var vbox = new VBoxContainer();
			vbox.Name = "Vbox_VBoxContainer";
			vbox.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
			vbox.CustomMinimumSize = new Vector2(480, 210);
			vbox.SizeFlagsHorizontal = SizeFlags.ExpandFill;
			vbox.SizeFlagsVertical = SizeFlags.ExpandFill;
			cardPanel.AddChild(vbox);

			vbox.AddChild(new Control { CustomMinimumSize = new Vector2(0, 15) });

			var titleLabel = new Label();
			titleLabel.Name = "TitleLabel_Label";
			UIStyle.ApplyTitle(titleLabel, Tr("INVALID MAP FOLDER"), 20);
			titleLabel.AddThemeColorOverride("font_color", new Color(0.95f, 0.4f, 0.3f));
			vbox.AddChild(titleLabel);

			vbox.AddChild(new Control { CustomMinimumSize = new Vector2(0, 8) });

			var descLabel = new Label();
			descLabel.Name = "DescLabel_Label";
			descLabel.Text = $"{string.Format(Tr("Folder: {0}"), Path.GetFileName(selectedFolder))}\n\n{Tr("The selected folder is not a valid map. Required files metadata.json and manifest.json were not found.")}";
			descLabel.HorizontalAlignment = HorizontalAlignment.Center;
			descLabel.AutowrapMode = TextServer.AutowrapMode.WordSmart;
			descLabel.AddThemeFontSizeOverride("font_size", 13);
			descLabel.AddThemeColorOverride("font_color", new Color(0.9f, 0.9f, 0.95f));
			vbox.AddChild(descLabel);

			vbox.AddChild(new Control { CustomMinimumSize = new Vector2(0, 16) });

			var buttonRow = new HBoxContainer();
			buttonRow.Name = "ButtonRow_HBoxContainer";
			buttonRow.Alignment = BoxContainer.AlignmentMode.Center;
			vbox.AddChild(buttonRow);

				var closeBtn = UIStyle.CreateCloseButton();
				closeBtn.Visible = true;
				closeBtn.CustomMinimumSize = new Vector2(120, 38);
				buttonRow.AddChild(closeBtn);

			closeBtn.Pressed += () =>
			{
				GameContext.UserInterface?.PlayClickSound();
				popup.QueueFree();
			};
		}

		private void ShowCorruptMapFileErrorDialog(string selectedFolder, string fileName, string errorMsg)
		{
			string msg = $"{string.Format(Tr("Folder: {0}"), Path.GetFileName(selectedFolder))}\n\n{string.Format(Tr("The map file '{0}' is corrupt or contains invalid JSON syntax and failed to parse."), fileName)}\n{string.Format(Tr("Error: {0}"), errorMsg)}";
			GameContext.UserInterface?.ShowPopupMessage("CORRUPT MAP FILE", msg);
		}

		private void InitializePublishDialog(out Panel popup, out VBoxContainer vbox, out ProgressBar progressBar, out Label statusLabel, out Button closeBtn, out CheckBox testVersionCheck)
		{
			popup = new Panel();
			popup.Name = "Popup_Panel";
			popup.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
			popup.AddThemeStyleboxOverride("panel", UIStyle.CreateBgGradient());
			popup.ZIndex = 1100;
			AddChild(popup);

			var cardPanel = new Panel();
			cardPanel.Name = "CardPanel_Panel";
			cardPanel.CustomMinimumSize = new Vector2(580, 280);
			cardPanel.SetAnchorsAndOffsetsPreset(LayoutPreset.Center);
			cardPanel.AddThemeStyleboxOverride("panel", UIStyle.CreateStonePanel(true));
			popup.AddChild(cardPanel);

			vbox = new VBoxContainer();
			vbox.Name = "Vbox_VBoxContainer";
			vbox.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
			vbox.CustomMinimumSize = new Vector2(540, 240);
			vbox.SizeFlagsHorizontal = SizeFlags.ExpandFill;
			vbox.SizeFlagsVertical = SizeFlags.ExpandFill;
			vbox.AddThemeConstantOverride("separation", 10);
			cardPanel.AddChild(vbox);

			vbox.AddChild(new Control { CustomMinimumSize = new Vector2(0, 10) });

			var titleLabel = new Label();
			titleLabel.Name = "TitleLabel_Label";
			UIStyle.ApplyTitle(titleLabel, "🚀 " + TranslationServer.Translate("PUBLISHING MAP PACKAGE"), 20);
			titleLabel.HorizontalAlignment = HorizontalAlignment.Center;
			vbox.AddChild(titleLabel);

			string mapTitle = GetMapNameFromMetadata();
			if (string.IsNullOrWhiteSpace(mapTitle) || mapTitle.Equals("Untitled Map", StringComparison.OrdinalIgnoreCase)) mapTitle = "UntitledMap";
			string mapVersion = GetMapVersionFromMetadata();
			if (string.IsNullOrWhiteSpace(mapVersion)) mapVersion = "1.0.0";

			var descLabel = new Label();
			descLabel.Name = "DescLabel_Label";
			descLabel.Text = string.Format(TranslationServer.Translate("Map: {0} (v{1})"), mapTitle, mapVersion);
			descLabel.HorizontalAlignment = HorizontalAlignment.Center;
			descLabel.AddThemeFontSizeOverride("font_size", 13);
			descLabel.AddThemeColorOverride("font_color", UIStyle.ColorGold);
			vbox.AddChild(descLabel);

			var publishOptionsRow = new HBoxContainer();
			publishOptionsRow.Name = "PublishOptions_HBoxContainer";
			publishOptionsRow.AddThemeConstantOverride("separation", 12);
			publishOptionsRow.Alignment = BoxContainer.AlignmentMode.Center;

			testVersionCheck = new CheckBox
			{
				Name = "TestVersionCheck_CheckBox",
				Text = TranslationServer.Translate("Publish as test version"),
				TooltipText = TranslationServer.Translate("Test versions are listed separately and never replace the official version players download.")
			};
			testVersionCheck.AddThemeColorOverride("font_color", UIStyle.ColorCyanGlow);
			publishOptionsRow.AddChild(testVersionCheck);

			vbox.AddChild(publishOptionsRow);

			var maintainerInfoLabel = new Label
			{
				Name = "MaintainerInfo_Label",
				Text = TranslationServer.Translate("Maintainer keys: every listed key can publish this map. Keys are added or revoked by an existing key holder through the registry maintainer endpoints; revocations take effect immediately."),
				AutowrapMode = TextServer.AutowrapMode.Word,
				CustomMinimumSize = new Vector2(520, 0),
				SizeFlagsHorizontal = SizeFlags.ExpandFill
			};
			maintainerInfoLabel.AddThemeFontSizeOverride("font_size", 11);
			maintainerInfoLabel.AddThemeColorOverride("font_color", UIStyle.ColorGoldDull);
			vbox.AddChild(maintainerInfoLabel);

			vbox.AddChild(new Control { CustomMinimumSize = new Vector2(0, 5) });

			progressBar = new ProgressBar();
			progressBar.Name = "ProgressBar_ProgressBar";
			progressBar.CustomMinimumSize = new Vector2(480, 24);
			progressBar.MinValue = 0;
			progressBar.MaxValue = 100;
			progressBar.Value = 0;
			vbox.AddChild(progressBar);

			statusLabel = new Label();
			statusLabel.Name = "StatusLabel_Label";
			statusLabel.Text = TranslationServer.Translate("Checking publish eligibility...");
			statusLabel.HorizontalAlignment = HorizontalAlignment.Center;
			statusLabel.AddThemeFontSizeOverride("font_size", 13);
			statusLabel.AddThemeColorOverride("font_color", new Color(0.9f, 0.9f, 0.95f));
			statusLabel.AutowrapMode = TextServer.AutowrapMode.WordSmart;
			vbox.AddChild(statusLabel);

			vbox.AddChild(new Control { CustomMinimumSize = new Vector2(0, 5) });

			var buttonRow = new HBoxContainer();
			buttonRow.Name = "ButtonRow_HBoxContainer";
			buttonRow.Alignment = BoxContainer.AlignmentMode.Center;
			vbox.AddChild(buttonRow);

			closeBtn = new Button();
			closeBtn.Name = "CloseBtn_Button";
			closeBtn.Flat = false;
			closeBtn.AddThemeConstantOverride("icon_max_width", 0);
			UIStyle.ApplyButtonStyleBoxes(closeBtn);
			UIStyle.ApplyButtonText(closeBtn, TranslationServer.Translate("CLOSE"), 14);
			closeBtn.CustomMinimumSize = new Vector2(130, 36);
			closeBtn.Visible = false;
			buttonRow.AddChild(closeBtn);
		}

		private async void PublishMapActionView(bool fullExport = false)
		{
			if (GameContext.Host == null || GameContext.EditorService.IsSyncing) return;
			GameContext.EditorService.IsSyncing = true;
			if (GameContext.EditorService != null)
			{
				GameContext.EditorService.IsPaused = true;
			}

			string workspace = !string.IsNullOrEmpty(GameContext.EditorService._tempWorkspacePath) ? GameContext.EditorService._tempWorkspacePath : MapWorkspaceService.GetActiveWorkspacePath();

			var (assetsValid, missingAssets) = MapAssetHelper.ValidateWorkspaceAssets(workspace);
			if (!assetsValid)
			{
				ShowFeedback(string.Format(TranslationServer.Translate("Publish failed: Map is missing required asset files:\n{0}"), string.Join(", ", missingAssets.Take(4)) + (missingAssets.Count > 4 ? "..." : "")));
				AppendWasmConsoleLog($"[ERROR] Publish failed. Missing required asset files:\n  {string.Join("\n  ", missingAssets)}");
				GameContext.EditorService.IsSyncing = false;
				if (GameContext.EditorService != null) GameContext.EditorService.IsPaused = false;
				return;
			}

			if (!CheckAssetSizeValidation(workspace))
			{
				GameContext.EditorService.IsSyncing = false;
				if (GameContext.EditorService != null) GameContext.EditorService.IsPaused = false;
				return;
			}

			string mapTitle = GetMapNameFromMetadata();
			if (string.IsNullOrWhiteSpace(mapTitle) || mapTitle.Equals("Untitled Map", StringComparison.OrdinalIgnoreCase)) mapTitle = "UntitledMap";

			string mapVersion = GetMapVersionFromMetadata();
			if (string.IsNullOrWhiteSpace(mapVersion)) mapVersion = "1.0.0";

			string metaJsonPath = Path.Combine(workspace, "metadata.json");
			string activeConfigPath = metaJsonPath;
			string tempTerrainPath = Path.Combine(workspace, "terrain.json");
			string manifestJsonPath = Path.Combine(workspace, "manifest.json");

			InitializePublishDialog(out Panel popup, out VBoxContainer vbox, out ProgressBar progressBar, out Label statusLabel, out Button closeBtn, out CheckBox testVersionCheck);

			closeBtn.Pressed += () =>
			{
				GameContext.UserInterface?.PlayClickSound();
				if (GodotObject.IsInstanceValid(popup))
				{
					popup.QueueFree();
				}
			};

			_wasmHasErrors = false;
			Action<string> logHandler = line => AppendWasmConsoleLog(line);
			GameLog.OnLine += logHandler;

			try
			{
				progressBar.Value = 5;
				statusLabel.Text = TranslationServer.Translate("Checking publish eligibility...");
				await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);

				string seedServerUrl = GameContext.Host != null && GodotObject.IsInstanceValid(GameContext.Lobby) ? GameContext.Lobby.RegistryServerUrl : ServersConfigHelper.GetDefaultServerUrl();
				try
				{
					using var checkClient = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
					string creatorPubKey = "";
					try
					{
						var key = GetOrGenerateAuthorshipKey();
						creatorPubKey = Convert.ToBase64String(key.PublicKey.Export(KeyBlobFormat.RawPublicKey));
					}
					catch
					{
					}

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
				GameContext.Host.SaveMapToFile(tempTerrainPath, performReload: false);
				GameContext.Host.EditorHasUnsavedChanges = false;
				InvalidateMetadataCache();

				if (OperatingSystem.IsWindows())
				{
					await GameContext.VSCode.SaveAllOpenFilesAsync();
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

				if (!CheckAssetSizeValidation(workspace))
				{
					progressBar.Value = 100;
					statusLabel.Text = "❌ " + TranslationServer.Translate("Asset exceeds 15 MB size limit. Publish aborted.");
					statusLabel.AddThemeColorOverride("font_color", new Color(0.95f, 0.3f, 0.3f));
					closeBtn.Visible = true;
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

				var csprojFiles = Directory.GetFiles(workspace, "*.csproj", SearchOption.TopDirectoryOnly);
				if (csprojFiles.Length > 0 || File.Exists(Path.Combine(workspace, "MapScript.cs")))
				{
					string wasmPath = MapStorageService.FindWasmPath(workspace);

					if (string.IsNullOrEmpty(wasmPath) || !File.Exists(wasmPath))
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

				GameContext.MapStorageService.DeduplicateWorkspaceBuildArtifacts(workspace);

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
				string currentUsername = GameContext.Lobby?.AuthenticatedUsername ?? "MapAuthor";
				string pubKeyBase64 = Convert.ToBase64String(authorshipKey.PublicKey.Export(KeyBlobFormat.RawPublicKey));

				MapWorkspaceService.NormalizeMetadataTextureEntries(workspace);

				if (File.Exists(activeConfigPath))
				{
					try
					{
						var metaDoc = JsonNode.Parse(File.ReadAllText(activeConfigPath)) as JsonObject;
						if (metaDoc != null)
						{
							metaDoc["EngineVersion"] = RealmVersion.GameBinaryVersion;
							MapJsonFormatter.SaveFormattedJson(activeConfigPath, metaDoc);
						}
					}
					catch
					{
					}

					GameContext.EditorService.LastMetadataSyncTime = GameContext.EditorService.GetLastWriteTimeSafe(activeConfigPath);
				}

				EditorService.LastInternalSaveTimeUtc = DateTime.UtcNow;

				var manifest = MapManifest.CreateFromDirectory(workspace, mapTitle, currentUsername, mapVersion);
				manifest.Attribution_SourceMap = GameContext.MapAttributionService.CollectSortedSourceMapNames(workspace).ToList();
				manifest.IsTestVersion = testVersionCheck.ButtonPressed;
				manifestJsonPath = Path.Combine(workspace, "manifest.json");
				string manifestJsonContent = manifest.ToJson();
				File.WriteAllText(manifestJsonPath, manifestJsonContent);
				EditorService.LastInternalSaveTimeUtc = DateTime.UtcNow;

				byte[] manifestBytes = Encoding.UTF8.GetBytes(manifestJsonContent);
				string manifestBlake3 = RealmMetadataHelper.ComputeBlake3(manifestBytes, ".json");
				string manifestHash = $"{manifestBlake3}.json";
				byte[] manifestHashBytes = Encoding.UTF8.GetBytes(manifestHash);
				byte[] manifestSigBytes = SignatureAlgorithm.Ed25519.Sign(authorshipKey, manifestHashBytes);
				string manifestSigBase64 = Convert.ToBase64String(manifestSigBytes);

				MapAssetManager.Storage.StoreAsset(manifestBytes, ".json", null, pubKeyBase64, manifestSigBase64, precomputedBlake3: manifestBlake3);

				var hashToRelativePath = new System.Collections.Generic.Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
				foreach (var pair in manifest.Files)
				{
					string norm = ContentAddressableStorage.NormalizeBlake3Hash(pair.Value);
					hashToRelativePath[norm] = pair.Key;
					hashToRelativePath[pair.Value] = pair.Key;

					string localPath = Path.Combine(workspace, pair.Key);
					if (File.Exists(localPath))
					{
						byte[] fileBytes = File.ReadAllBytes(localPath);
						string ext = Path.GetExtension(localPath).ToLowerInvariant();
						MapAssetManager.Storage.StoreAsset(fileBytes, ext, null, pubKeyBase64, null, precomputedBlake3: norm);
					}
				}

				hashToRelativePath[manifestBlake3] = "manifest.json";
				hashToRelativePath[manifestHash] = "manifest.json";
				hashToRelativePath["manifest.json"] = "manifest.json";

				if (File.Exists(activeConfigPath))
				{
					byte[] metaBytes = File.ReadAllBytes(activeConfigPath);
					string metaBlake3 = RealmMetadataHelper.ComputeBlake3(metaBytes, ".json");
					string metaNorm = ContentAddressableStorage.NormalizeBlake3Hash(metaBlake3);
					string metaAssetKey = $"{metaBlake3}.json";

					hashToRelativePath[metaBlake3] = "metadata.json";
					hashToRelativePath[metaNorm] = "metadata.json";
					hashToRelativePath[metaAssetKey] = "metadata.json";
					hashToRelativePath["metadata.json"] = "metadata.json";
					MapAssetManager.Storage.StoreAsset(metaBytes, ".json", null, pubKeyBase64, null, precomputedBlake3: metaNorm);
				}

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
					ReferencedHashes = manifest.Files.Values.ToList(),
					IsTestVersion = testVersionCheck.ButtonPressed
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
							long now = Environment.TickCount64;
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
					Signature = manifestSigBase64,
					IsTestVersion = testVersionCheck.ButtonPressed
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
					GameContext.UserInterface?.ShowConfirmationDialog(
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
				GameLog.OnLine -= logHandler;
				if (GameContext.EditorService != null)
				{
					GameContext.EditorService.IsPaused = false;
				}

				GameContext.EditorService.LastMetadataSyncTime = GameContext.EditorService.GetLastWriteTimeSafe(activeConfigPath);
				GameContext.EditorService.LastTerrainSyncTime = GameContext.EditorService.GetMaxTerrainWriteTime(tempTerrainPath);
				GameContext.EditorService.IsSyncing = false;
			}
		}

		public async Task ShowAssetIndexRepairModalAsync(Action onCompleted = null)
		{
			var overlay = new ColorRect();
			overlay.Name = "AssetIndexRepairOverlay";
			overlay.Color = new Color(0, 0, 0, 0.75f);
			overlay.SetAnchorsPreset(LayoutPreset.FullRect);
			overlay.MouseFilter = MouseFilterEnum.Stop;
			overlay.ZIndex = 1100;
			AddChild(overlay);

			var center = new CenterContainer();
			center.SetAnchorsPreset(LayoutPreset.FullRect);
			overlay.AddChild(center);

			var panel = new PanelContainer();
			panel.Name = "Panel_PanelContainer";
			panel.AddThemeStyleboxOverride("panel", UIStyle.CreateStonePanel(true));
			panel.CustomMinimumSize = new Vector2(520, 220);
			center.AddChild(panel);

			var vbox = new VBoxContainer();
			vbox.Name = "Vbox_VBoxContainer";
			vbox.AddThemeConstantOverride("separation", 12);
			panel.AddChild(vbox);

			var titleLabel = new Label();
			titleLabel.Name = "TitleLabel_Label";
			UIStyle.ApplyTitle(titleLabel, "🗄️ " + TranslationServer.Translate("REPAIRING ASSET INDEX"), 20);
			titleLabel.HorizontalAlignment = HorizontalAlignment.Center;
			vbox.AddChild(titleLabel);

			var descLabel = new Label();
			descLabel.Name = "DescLabel_Label";
			descLabel.Text = TranslationServer.Translate("Rebuilding asset index database from CAS manifests...");
			descLabel.HorizontalAlignment = HorizontalAlignment.Center;
			descLabel.AddThemeColorOverride("font_color", UIStyle.ColorGold);
			descLabel.AddThemeFontSizeOverride("font_size", 13);
			vbox.AddChild(descLabel);

			var progressBar = new ProgressBar();
			progressBar.Name = "ProgressBar_ProgressBar";
			progressBar.CustomMinimumSize = new Vector2(460, 22);
			progressBar.MinValue = 0;
			progressBar.MaxValue = 100;
			progressBar.Value = 0;
			vbox.AddChild(progressBar);

			var statusLabel = new Label();
			statusLabel.Name = "StatusLabel_Label";
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
				await Task.Run(() => GameContext.AssetIndexService.RebuildIndexFromCas(progress));
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
			GameContext.WasmConsole.AppendLog(line);
			GD.Print("[WASM_BUILD] " + line);
		}

		private void SetupMirrorDropdown(OptionButton opt, string tooltip = "")
		{
			if (opt == null) return;
			opt.Clear();
			opt.Set("icon_max_width", 0);
			opt.SizeFlagsHorizontal = SizeFlags.ExpandFill;
			opt.ClipText = true;

			int folds = GameContext.Host != null ? GameContext.Host.EditorSymmetryFolds : 4;
			opt.AddItem(TranslationServer.Translate($"{UnicodeIcons.BAN} SYMMETRY: NONE"), (int)MirrorMode.None);
			opt.AddItem(TranslationServer.Translate($"{UnicodeIcons.ARROWS_V} MIRROR: VERTICAL"), (int)MirrorMode.Vertical);
			opt.AddItem(TranslationServer.Translate($"{UnicodeIcons.ARROWS_H} MIRROR: HORIZONTAL"), (int)MirrorMode.Horizontal);
			opt.AddItem(TranslationServer.Translate($"{UnicodeIcons.GRID_QUAD} MIRROR: QUAD"), (int)MirrorMode.Both);
			opt.AddItem($"{UnicodeIcons.ROTATE} " + string.Format(TranslationServer.Translate("ROTATIONAL ({0}-FOLD)"), folds), (int)MirrorMode.Rotational);

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
			GameContext.EditorService.SetMirrorModeDomain(mode);
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
			if (GameContext.Host == null) return;
			int folds = GameContext.Host.EditorSymmetryFolds;
			var mode = GameContext.Host.EditorMirrorMode;

			UpdateSingleMirrorDropdown(_optMirrorMode, mode, folds);
			UpdateSingleMirrorDropdown(_optPlacementMirrorMode, mode, folds);
			UpdateSingleMirrorDropdown(_optClipboardMirrorMode, mode, folds);

			UpdateSymmetrySubControlsVisibility();
		}

		private void UpdateSingleMirrorDropdown(OptionButton opt, MirrorMode mode, int folds)
		{
			if (opt == null) return;

			string rotText = $"{UnicodeIcons.ROTATE} " + string.Format(TranslationServer.Translate("ROTATIONAL ({0}-FOLD)"), folds);
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
			GameContext.EditorService.SetSymmetrySpokesDomain(spokes);
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
			opt.SizeFlagsHorizontal = SizeFlags.ExpandFill;
			opt.ClipText = true;

			for (int i = 0; i < EditorService.PolarRingSpacingOptions.Length; i++)
			{
				float spacing = EditorService.PolarRingSpacingOptions[i];
				opt.AddItem($"{UnicodeIcons.CIRCLE} " + string.Format(TranslationServer.Translate("RINGS: {0:F0}T"), spacing), (int)spacing);
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
			GameContext.EditorService.SetPolarRingSpacingDomain(spacing);
			UpdatePolarRingSpacingButtonText();
			ShowFeedback(string.Format(TranslationServer.Translate("Polar Ring Spacing: {0:F0} tiles"), spacing));
		}

		private void SetPolarSpokesView(int spokes)
		{
			if (GameContext.Host == null) return;
			if (GameContext.Host.EditorMirrorMode == MirrorMode.Rotational)
			{
				SetSymmetrySpokes(spokes);
				return;
			}

			GameContext.EditorService.SetPolarSpokesDomain(spokes);
			UpdatePolarRadialStepButtonText();
			ShowFeedback(string.Format(TranslationServer.Translate("Polar Spokes: {0}-way"), spokes));
		}

		private void SetPivotToCursorView()
		{
			if (GameContext.Host == null || GameContext.Host.GroundTerrain == null) return;
			var viewport = GetViewport();
			Vector3 hitPos = Vector3.Zero;
			bool hasHit = false;
			if (viewport != null)
			{
				hasHit = GameContext.Host.TryRaycastTerrainFromMousePosition(viewport.GetMousePosition(), out hitPos);
			}

			if (!hasHit)
			{
				hitPos = GameContext.Host.MainCamera?.GlobalPosition ?? Vector3.Zero;
			}

			var (cx, cz) = GameContext.EditorService.SetPivotDomain(new System.Numerics.Vector3(hitPos.X, hitPos.Y, hitPos.Z));
			ShowFeedback(string.Format(TranslationServer.Translate("Symmetry & Polar Pivot set to tile ({0}, {1})"), cx, cz));
		}

		private void ResetPivotToMapCenterView()
		{
			if (GameContext.Host == null || GameContext.Host.GroundTerrain == null) return;
			GameContext.EditorService.ResetPivotToMapCenterDomain();
			ShowFeedback(TranslationServer.Translate("Symmetry & Polar Pivot reset to Map Center"));
		}

		public void SwitchModuleView(EditorModule module)
		{
			_activeModule = module;
			UpdateModuleSwitchButtons();

			if (module != EditorModule.Coordinates)
			{
				GameContext.Host?.HideCoordinateSelectionOutline();
			}

			UpdatePanelVisibilityForModule(module);

			if (GameContext.Host != null)
			{
				switch (module)
				{
					case EditorModule.Terrain:
						TriggerToolSelection(EditorTool.Raise, _btnRaise);
						break;
					case EditorModule.TextureDeco:
						TriggerToolSelection(EditorTool.PaintTexture, _btnTextureBrush);
						break;
					case EditorModule.Pathing:
						TriggerToolSelection(EditorTool.PaintPathing, _btnPathingBrush);
						break;
					case EditorModule.Objects:
						_entityPaletteController?.TriggerAddObjectMode();
						break;
					case EditorModule.Coordinates:
						TriggerToolSelection(EditorTool.DrawCoordinate, _btnDrawCoordinate);
						break;
					case EditorModule.Clipboard:
						TriggerToolSelection(EditorTool.SelectArea, _btnSelectArea);
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

			var headerTex = TrimmedTextureLoader.LoadTrimmedTexture("res://Assets/UI/map_editor_options_header.webp");
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
			chk.SizeFlagsHorizontal = SizeFlags.ExpandFill;
			UIStyle.ApplyCheckboxStyle(chk);
			StyleCheckBoxRow(chk);

			var rowPanel = new PanelContainer();
			rowPanel.Name = "RowPanel" + chk.Name;
			rowPanel.SizeFlagsHorizontal = SizeFlags.ExpandFill;

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
			row.SizeFlagsHorizontal = SizeFlags.ExpandFill;

			int originalIndex = chk.GetIndex();
			parent.RemoveChild(chk);
			row.AddChild(chk);

			var colorBox = new Panel();
			colorBox.Name = "ColorBox" + chk.Name;
			colorBox.CustomMinimumSize = new Vector2(18, 18);
			colorBox.SizeFlagsVertical = SizeFlags.ShrinkCenter;
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

		private void StyleIconButton(Button btn, string iconText, string tooltipText)
		{
			if (btn == null) return;
			btn.Text = iconText;
			btn.TooltipText = tooltipText;
			btn.Flat = false;
			btn.CustomMinimumSize = new Vector2(30, 30);
			btn.SizeFlagsHorizontal = SizeFlags.ExpandFill;

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

		private Button CreateToolCardView(Control cardParent, string buttonName, string iconGlyph, string labelText, Action onClick, string tooltip = "")
		{
			var btn = new Button();
			btn.Name = buttonName;

			btn.Text = iconGlyph;
			btn.TooltipText = tooltip;
			btn.Flat = false;
			btn.CustomMinimumSize = new Vector2(52, 52);
			btn.SizeFlagsHorizontal = SizeFlags.ShrinkCenter;
			btn.SizeFlagsVertical = SizeFlags.ShrinkCenter;

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
			card.SizeFlagsHorizontal = SizeFlags.ExpandFill;
			card.Alignment = BoxContainer.AlignmentMode.Center;

			card.AddChild(btn);
			card.AddChild(lbl);
			cardParent.AddChild(card);

			return btn;
		}

		private void RestructurePanelLayouts()
		{
			var targetFile = GetContentTarget(_contentFile);
			if (targetFile != null)
			{
				_btnLoad.SizeFlagsHorizontal = SizeFlags.ExpandFill;
				_btnLoad.SizeFlagsStretchRatio = 1.0f;

				_btnLoadMore.CustomMinimumSize = new Vector2(24, 30);
				_btnLoadMore.SizeFlagsHorizontal = SizeFlags.ShrinkEnd;

				_btnSave.SizeFlagsHorizontal = SizeFlags.ExpandFill;
				_btnSave.SizeFlagsStretchRatio = 1.0f;

				_btnSaveMore.CustomMinimumSize = new Vector2(24, 30);
				_btnSaveMore.SizeFlagsHorizontal = SizeFlags.ShrinkEnd;

				StyleSubContainer(targetFile.GetNodeOrNull<Control>("BoxFileOps"), "File Operations");
				StyleSubContainer(targetFile.GetNodeOrNull<Control>("BoxGenOps"), "Generation & Imports");
			}

			var targetViewport = GetContentTarget(_contentViewport);
			if (targetViewport != null)
			{
				StyleIconButton(_btnToggleGrid, UnicodeIcons.BORDER_ALL, "Overlay");
				StyleIconButton(_btnToggleEnvironment, UnicodeIcons.SUN, "Environment");
				StyleIconButton(_btnToggleCamera, UnicodeIcons.CAMERA, "Configure camera controls (R / C / + / - / F8)");
				StyleIconButton(_btnTapeMeasure, UnicodeIcons.RULER, "Tape measure distance & slope tool (U)");

				StyleSubContainer(targetViewport.GetNodeOrNull<Control>("BoxViewportToolbar"), "Navigation Bar");

				_rowPolarConfig = targetViewport.GetNodeOrNull<HBoxContainer>("RowPolarConfig");

				if (_optPolarRingSpacing != null)
				{
					_optPolarRingSpacing.CustomMinimumSize = new Vector2(0, 26);
					_optPolarRingSpacing.SizeFlagsHorizontal = SizeFlags.ExpandFill;
					_optPolarRingSpacing.AddThemeFontSizeOverride("font_size", 9);
				}

				if (_optPolarRadialStep != null)
				{
					_optPolarRadialStep.CustomMinimumSize = new Vector2(0, 26);
					_optPolarRadialStep.SizeFlagsHorizontal = SizeFlags.ExpandFill;
					_optPolarRadialStep.AddThemeFontSizeOverride("font_size", 9);
				}

				if (_btnResetPivotToCenter != null)
				{
					_btnResetPivotToCenter.CustomMinimumSize = new Vector2(0, 26);
					_btnResetPivotToCenter.SizeFlagsHorizontal = SizeFlags.ExpandFill;
					_btnResetPivotToCenter.AddThemeFontSizeOverride("font_size", 9);
				}

				bool isSymmetryActive = GameContext.Host != null &&
				                        (GameContext.Host.EditorMirrorMode != MirrorMode.None ||
				                         GameContext.EditorService.EditorGridMode == GridOverlayMode.Polar ||
				                         GameContext.EditorService.EditorGridMode == GridOverlayMode.Both);
				_rowPolarConfig.Visible = isSymmetryActive;
			}

			if (_panelTerrainVBox != null)
			{
				StyleSubContainer(_panelTerrainVBox, "Terrain Elevation");
			}

			if (_panelDecoVBox != null)
			{
				StyleSubContainer(_panelDecoVBox, "Texture Actions");
			}

			if (_panelPathingVBox != null)
			{
				StyleSubContainer(_panelPathingVBox, "Pathing Actions");
			}

			if (_panelObjects != null)
			{
				StyleSubContainer(_panelObjects, "Object Placement");
			}

			if (_panelClipboard != null)
			{
				StyleSubContainer(_panelClipboard, "Clipboard Actions");
			}

			var targetInspector = GetContentTarget(_contentInspector);
			if (targetInspector != null)
			{
				StyleSubContainer(targetInspector.GetNodeOrNull<Control>("InspBox_VBoxContainer"), "Transform Controls");
			}

			if (_contentBrush != null)
			{
				StyleSubContainer(_contentBrush, "Brush Properties");

				StyleValueBadge(_lblBrushSizeValue);
				StyleValueBadge(_lblBrushStrengthValue);
				StyleValueBadge(_lblBlockStepValue);

				if (_btnBrushShape != null)
				{
					_btnBrushShape.CustomMinimumSize = new Vector2(0, 32);
					_btnBrushShape.SizeFlagsHorizontal = SizeFlags.ExpandFill;
					_btnBrushShape.AddThemeFontSizeOverride("font_size", 10);
				}

				if (_optMirrorMode != null)
				{
					_optMirrorMode.CustomMinimumSize = new Vector2(0, 32);
					_optMirrorMode.SizeFlagsHorizontal = SizeFlags.ExpandFill;
					_optMirrorMode.AddThemeFontSizeOverride("font_size", 10);
				}
			}

			if (_containerTextureSettings != null)
			{
				StyleSubContainer(_containerTextureSettings, "Texture Palette & Settings");

				if (RowGroundTexture != null)
				{
					var groundStyle = new StyleBoxFlat();
					groundStyle.BgColor = new Color(0.12f, 0.16f, 0.20f, 0.85f);
					groundStyle.BorderColor = new Color(0.20f, 0.65f, 0.95f, 0.85f);
					groundStyle.SetBorderWidthAll(1);
					groundStyle.CornerRadiusTopLeft = 4;
					groundStyle.CornerRadiusTopRight = 4;
					groundStyle.CornerRadiusBottomLeft = 4;
					groundStyle.CornerRadiusBottomRight = 4;
					groundStyle.ContentMarginLeft = 8;
					groundStyle.ContentMarginRight = 8;
					groundStyle.ContentMarginTop = 4;
					groundStyle.ContentMarginBottom = 4;

					var panelGround = RowGroundTexture.GetNodeOrNull<Panel>("RowBG");
					if (panelGround == null)
					{
						panelGround = new Panel();
						panelGround.Name = "RowBG";
						panelGround.ShowBehindParent = true;
						panelGround.MouseFilter = MouseFilterEnum.Ignore;
						panelGround.SetAnchorsPreset(LayoutPreset.FullRect);
						RowGroundTexture.AddChild(panelGround);
						RowGroundTexture.MoveChild(panelGround, 0);
					}

					panelGround.AddThemeStyleboxOverride("panel", groundStyle);
				}

				if (RowCliffTexture != null)
				{
					var cliffStyle = new StyleBoxFlat();
					cliffStyle.BgColor = new Color(0.18f, 0.14f, 0.10f, 0.85f);
					cliffStyle.BorderColor = new Color(0.95f, 0.55f, 0.15f, 0.85f);
					cliffStyle.SetBorderWidthAll(1);
					cliffStyle.CornerRadiusTopLeft = 4;
					cliffStyle.CornerRadiusTopRight = 4;
					cliffStyle.CornerRadiusBottomLeft = 4;
					cliffStyle.CornerRadiusBottomRight = 4;
					cliffStyle.ContentMarginLeft = 8;
					cliffStyle.ContentMarginRight = 8;
					cliffStyle.ContentMarginTop = 4;
					cliffStyle.ContentMarginBottom = 4;

					var panelCliff = RowCliffTexture.GetNodeOrNull<Panel>("RowBG");
					if (panelCliff == null)
					{
						panelCliff = new Panel();
						panelCliff.Name = "RowBG";
						panelCliff.ShowBehindParent = true;
						panelCliff.MouseFilter = MouseFilterEnum.Ignore;
						panelCliff.SetAnchorsPreset(LayoutPreset.FullRect);
						RowCliffTexture.AddChild(panelCliff);
						RowCliffTexture.MoveChild(panelCliff, 0);
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
					_btnClipboardBrushShape.SizeFlagsHorizontal = SizeFlags.ExpandFill;
					_btnClipboardBrushShape.AddThemeFontSizeOverride("font_size", 10);
				}

				if (_optClipboardMirrorMode != null)
				{
					_optClipboardMirrorMode.CustomMinimumSize = new Vector2(0, 30);
					_optClipboardMirrorMode.SizeFlagsHorizontal = SizeFlags.ExpandFill;
					_optClipboardMirrorMode.AddThemeFontSizeOverride("font_size", 10);
				}

				if (_btnPasteReflection != null)
				{
					_btnPasteReflection.CustomMinimumSize = new Vector2(0, 30);
					_btnPasteReflection.SizeFlagsHorizontal = SizeFlags.ExpandFill;
				}

				if (_btnPasteAnchor != null)
				{
					_btnPasteAnchor.CustomMinimumSize = new Vector2(0, 30);
					_btnPasteAnchor.SizeFlagsHorizontal = SizeFlags.ExpandFill;
				}
			}

			if (_contentPlacement != null)
			{
				if (_optPlacementMirrorMode != null)
				{
					_optPlacementMirrorMode.CustomMinimumSize = new Vector2(0, 30);
					_optPlacementMirrorMode.SizeFlagsHorizontal = SizeFlags.ExpandFill;
					_optPlacementMirrorMode.AddThemeFontSizeOverride("font_size", 10);
				}
			}

			if (_containerEyedropperSettings != null)
			{
				StyleSubContainer(_containerEyedropperSettings, "Eyedropper Settings");
			}

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

		private void UpdateSidebarMorph(EditorTool tool)
		{
			if (_panelRight == null) return;

			bool isClumpActive = _chkClumpMode != null && _chkClumpMode.ButtonPressed;
			bool isBrush = (tool == EditorTool.Raise ||
			                tool == EditorTool.Lower ||
			                tool == EditorTool.Height ||
			                tool == EditorTool.Smooth ||
			                tool == EditorTool.Plateau ||
			                tool == EditorTool.Ramp ||
			                tool == EditorTool.PaintTexture ||
			                tool == EditorTool.Noise ||
			                tool == EditorTool.PaintPathing ||
			                tool == EditorTool.FloodFillPathing ||
			                tool == EditorTool.PlacePropClump ||
			                ((tool == EditorTool.PlaceUnit || tool == EditorTool.PlaceProp || tool == EditorTool.PlaceDecal) && isClumpActive))
			               && tool != EditorTool.Water;

			if (_accordionBrush != null)
			{
				_accordionBrush.Visible = isBrush;
				UpdateBrushSizeVisibility();
				UpdateBrushShapeVisibility();
				UpdateBrushStrengthVisibility();
				bool isTextureMode = tool == EditorTool.PaintTexture;
				if (_chkBlockMode != null)
				{
					_chkBlockMode.Visible = (tool != EditorTool.PaintPathing &&
					                         tool != EditorTool.FloodFillPathing &&
					                         !isTextureMode &&
					                         tool != EditorTool.Smooth &&
					                         tool != EditorTool.Noise &&
					                         tool != EditorTool.Ramp &&
					                         tool != EditorTool.PlacePropClump &&
					                         tool != EditorTool.Height &&
					                         !isClumpActive);
				}

				UpdateBlockStepVisibility();
			}

			if (_accordionWater != null)
			{
				_accordionWater.Visible = (tool == EditorTool.Water);
			}

			bool isBlockModeActive = (_chkBlockMode != null && _chkBlockMode.Visible && _chkBlockMode.ButtonPressed) || (GameContext.Host != null && GameContext.Host.EditorBlockMode);
			bool isPaintTool = tool == EditorTool.PaintTexture ||
			                   tool == EditorTool.FloodFill;
			bool isBlockHeightTool = (isBlockModeActive && (
				                         tool == EditorTool.Raise ||
				                         tool == EditorTool.Lower ||
				                         tool == EditorTool.Plateau)) ||
			                         tool == EditorTool.Height;

			bool isRampTool = tool == EditorTool.Ramp;

			bool texSettingsVisible = _containerTextureSettings != null && (isPaintTool || isBlockHeightTool || isRampTool);
			if (_containerTextureSettings != null) _containerTextureSettings.Visible = texSettingsVisible;

			bool isTexturePaintToolOnly = tool == EditorTool.PaintTexture;

			if (ChkApplyGroundTexture != null) ChkApplyGroundTexture.Visible = texSettingsVisible && isTexturePaintToolOnly;
			if (ChkApplyCliffTexture != null) ChkApplyCliffTexture.Visible = texSettingsVisible && isTexturePaintToolOnly;

			bool pathingSettingsVisible = _containerPathingSettings != null && (tool == EditorTool.PaintPathing || tool == EditorTool.FloodFillPathing);
			if (_containerPathingSettings != null) _containerPathingSettings.Visible = pathingSettingsVisible;

			bool eyedropperSettingsVisible = _containerEyedropperSettings != null && (tool == EditorTool.Eyedropper);
			if (_containerEyedropperSettings != null) _containerEyedropperSettings.Visible = eyedropperSettingsVisible;

			bool pasteSettingsVisible = _containerPasteSettings != null && (tool == EditorTool.SelectArea || tool == EditorTool.PasteArea);
			if (_containerPasteSettings != null) _containerPasteSettings.Visible = pasteSettingsVisible;

			bool isPlacement = (tool == EditorTool.PlaceUnit ||
			                    tool == EditorTool.PlaceProp ||
			                    tool == EditorTool.PlacePropClump ||
			                    tool == EditorTool.PlaceDecal ||
			                    tool == EditorTool.PlaceVfx);

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

			if (_placementPlayerContainer != null)
			{
				bool showPlacementPlayer = tool == EditorTool.PlaceUnit;
				_placementPlayerContainer.Visible = showPlacementPlayer;
				if (showPlacementPlayer)
				{
					SyncPlacementPlayerSelection(GameContext.EditorService.PlaceUnitPlayerIndex);
				}
			}

			if (_chkClumpMode != null)
			{
				_chkClumpMode.Visible = isPlacement;
				if (_spacingBox != null) _spacingBox.Visible = isPlacement && _chkClumpMode.ButtonPressed;
				if (_densityBox != null) _densityBox.Visible = isPlacement && _chkClumpMode.ButtonPressed;
				if (_scaleVarBox != null) _scaleVarBox.Visible = isPlacement && _chkClumpMode.ButtonPressed;
			}

			bool hasSelectedObject = GameContext.Host != null && GodotObject.IsInstanceValid(GameContext.Host.SelectedEditorObject);
			if (_accordionInspector != null)
			{
				_accordionInspector.Visible = hasSelectedObject && (tool == EditorTool.SelectMove);
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
				if (keyEvent.Keycode == Key.F1)
				{
					SwitchModuleView(EditorModule.Terrain);
					GetViewport().SetInputAsHandled();
				}
				else if (keyEvent.Keycode == Key.F2)
				{
					SwitchModuleView(EditorModule.TextureDeco);
					GetViewport().SetInputAsHandled();
				}
				else if (keyEvent.Keycode == Key.F3)
				{
					SwitchModuleView(EditorModule.Pathing);
					GetViewport().SetInputAsHandled();
				}
				else if (keyEvent.Keycode == Key.F4)
				{
					SwitchModuleView(EditorModule.Objects);
					GetViewport().SetInputAsHandled();
				}
				else if (keyEvent.Keycode == Key.F5)
				{
					SwitchModuleView(EditorModule.Coordinates);
					GetViewport().SetInputAsHandled();
				}
				else if (keyEvent.Keycode == Key.F6)
				{
					SwitchModuleView(EditorModule.Clipboard);
					GetViewport().SetInputAsHandled();
				}
				else if (keyEvent.Keycode == Key.F7)
				{
					if (GameContext.Host != null && GameContext.Host.GroundTerrain != null)
					{
						GameContext.Host.GroundTerrain.ToggleWireframeMode();
						bool isWireframe = GetViewport()?.DebugDraw == Viewport.DebugDrawEnum.Wireframe;
						UpdateWireframeOverlayExternal(isWireframe);
					}

					GetViewport().SetInputAsHandled();
				}
				else if (keyEvent.Keycode == Key.F8)
				{
					ToggleFreeCamera();
					GetViewport().SetInputAsHandled();
				}
				else if (keyEvent.Keycode == Key.F9)
				{
					ToggleShadows();
					GetViewport().SetInputAsHandled();
				}
			}
		}

		private void RotateSelectedObject(float angleDelta)
		{
			if (GameContext.Host == null) return;

			var (success, newRotY) = GameContext.Host.RotateSelectedObject(angleDelta);
			if (success)
			{
				UpdateSelectedObjectInfo();
				ShowFeedback(string.Format(TranslationServer.Translate("Rotated Object to {0}°"), newRotY));
			}
		}

		private void ScaleSelectedObject(float scaleDelta)
		{
			if (GameContext.Host == null) return;

			var (success, newScaleVal) = GameContext.Host.ScaleSelectedObject(scaleDelta);
			if (success)
			{
				UpdateSelectedObjectInfo();
				ShowFeedback(string.Format(TranslationServer.Translate("Scaled Object to {0:F1}x"), newScaleVal));
			}
		}

		private void ResetScaleSelectedObject()
		{
			if (GameContext.Host == null) return;

			var success = GameContext.Host.ResetScaleSelectedObject();
			if (success)
			{
				UpdateSelectedObjectInfo();
				ShowFeedback(TranslationServer.Translate("Reset Object scale to 1.0x"));
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

		private void DecreaseScaleDialogWidth()
		{
			if (GameContext.EditorService.DecreaseScaleDialogWidth())
			{
				UpdateScaleDialogLabels();
				GameContext.Host?.ShowScaleMapSilhouette(GameContext.EditorService.ScaleDialogTargetWidth, _scaleDialogTargetDepth);
			}
		}

		private void IncreaseScaleDialogWidth()
		{
			if (GameContext.EditorService.IncreaseScaleDialogWidth())
			{
				UpdateScaleDialogLabels();
				GameContext.Host?.ShowScaleMapSilhouette(GameContext.EditorService.ScaleDialogTargetWidth, _scaleDialogTargetDepth);
			}
		}

		private void DecreaseScaleDialogDepth()
		{
			int newDepth = GameContext.EditorService.DecreaseScaleDialogDepth(_scaleDialogTargetDepth);
			if (newDepth != _scaleDialogTargetDepth)
			{
				_scaleDialogTargetDepth = newDepth;
				UpdateScaleDialogLabels();
				GameContext.Host?.ShowScaleMapSilhouette(GameContext.EditorService.ScaleDialogTargetWidth, _scaleDialogTargetDepth);
			}
		}

		private void IncreaseScaleDialogDepth()
		{
			int newDepth = GameContext.EditorService.IncreaseScaleDialogDepth(_scaleDialogTargetDepth);
			if (newDepth != _scaleDialogTargetDepth)
			{
				_scaleDialogTargetDepth = newDepth;
				UpdateScaleDialogLabels();
				GameContext.Host?.ShowScaleMapSilhouette(GameContext.EditorService.ScaleDialogTargetWidth, _scaleDialogTargetDepth);
			}
		}

		private GridContainer CreateScaleDialogSizeGrid()
		{
			var sizeGrid = new GridContainer();
			sizeGrid.Name = "SizeGrid_GridContainer";
			sizeGrid.Columns = 3;
			sizeGrid.AddThemeConstantOverride("h_separation", 8);
			sizeGrid.AddThemeConstantOverride("v_separation", 6);

			_lblScalePreviewWidth = new Label();
			_lblScalePreviewWidth.Name = "LblScalePreviewWidth_Label";
			_lblScalePreviewWidth.AddThemeFontSizeOverride("font_size", 11);
			_lblScalePreviewWidth.AddThemeColorOverride("font_color", UIStyle.ColorCyanGlow);
			_lblScalePreviewWidth.SizeFlagsHorizontal = SizeFlags.ExpandFill;
			sizeGrid.AddChild(_lblScalePreviewWidth);

			var btnW_Dec = new Button();
			btnW_Dec.Name = "BtnW_Dec_Button";
			btnW_Dec.Set("icon_max_width", 0);
			SetupOptionButton(btnW_Dec, UnicodeIcons.MINUS, DecreaseScaleDialogWidth, 10, "Decrease target width");
			sizeGrid.AddChild(btnW_Dec);

			var btnW_Inc = new Button();
			btnW_Inc.Name = "BtnW_Inc_Button";
			btnW_Inc.Set("icon_max_width", 0);
			SetupOptionButton(btnW_Inc, UnicodeIcons.PLUS, IncreaseScaleDialogWidth, 10, "Increase target width");
			sizeGrid.AddChild(btnW_Inc);

			_lblScalePreviewHeight = new Label();
			_lblScalePreviewHeight.Name = "LblScalePreviewHeight_Label";
			_lblScalePreviewHeight.AddThemeFontSizeOverride("font_size", 11);
			_lblScalePreviewHeight.AddThemeColorOverride("font_color", UIStyle.ColorCyanGlow);
			_lblScalePreviewHeight.SizeFlagsHorizontal = SizeFlags.ExpandFill;
			sizeGrid.AddChild(_lblScalePreviewHeight);

			var btnH_Dec = new Button();
			btnH_Dec.Name = "BtnH_Dec_Button";
			btnH_Dec.Set("icon_max_width", 0);
			SetupOptionButton(btnH_Dec, UnicodeIcons.MINUS, DecreaseScaleDialogDepth, 10, "Decrease target depth");
			sizeGrid.AddChild(btnH_Dec);

			var btnH_Inc = new Button();
			btnH_Inc.Name = "BtnH_Inc_Button";
			btnH_Inc.Set("icon_max_width", 0);
			SetupOptionButton(btnH_Inc, UnicodeIcons.PLUS, IncreaseScaleDialogDepth, 10, "Increase target depth");
			sizeGrid.AddChild(btnH_Inc);
			return sizeGrid;
		}

		private HBoxContainer CreateScaleDialogButtonRow()
		{
			var buttonRow = new HBoxContainer();
			buttonRow.Name = "ButtonRow_HBoxContainer";
			buttonRow.AddThemeConstantOverride("separation", 8);

			var btnCancel = new Button();
			btnCancel.Name = "BtnCancel_Button";
			btnCancel.Set("icon_max_width", 0);
			btnCancel.SizeFlagsHorizontal = SizeFlags.ExpandFill;
			SetupButton(btnCancel, "✖ CANCEL", () =>
			{
				GameContext.Host?.HideScaleMapSilhouette();
				CloseScaleMapDialog();
			}, 11, "Cancel and discard the scale operation");
			buttonRow.AddChild(btnCancel);

			var btnApply = new Button();
			btnApply.Name = "BtnApply_Button";
			btnApply.Set("icon_max_width", 0);
			btnApply.SizeFlagsHorizontal = SizeFlags.ExpandFill;
			SetupButton(btnApply, "✔ APPLY", () =>
			{
				GameContext.Host?.HideScaleMapSilhouette();
				GameContext.Host?.ScaleMapExternal(GameContext.EditorService.ScaleDialogTargetWidth, _scaleDialogTargetDepth);
				CloseScaleMapDialog();
			}, 11, "Apply scale and stretch the entire map");
			buttonRow.AddChild(btnApply);
			return buttonRow;
		}

		public void OpenScaleMapDialog()
		{
			if (_scaleMapDialog != null) return;

			GameContext.Host?.ShowScaleMapSilhouette(GameContext.EditorService.ScaleDialogTargetWidth, _scaleDialogTargetDepth);

			_scaleMapDialog = new PanelContainer();
			_scaleMapDialog.Name = "ScaleMapDialog";
			_scaleMapDialog.AddThemeStyleboxOverride("panel", UIStyle.CreateStonePanel());
			_scaleMapDialog.SetAnchorsPreset(LayoutPreset.Center);
			_scaleMapDialog.CustomMinimumSize = new Vector2(320, 0);
			_scaleMapDialog.GrowHorizontal = GrowDirection.Both;
			_scaleMapDialog.GrowVertical = GrowDirection.Both;
			_scaleMapDialog.ZIndex = 1000;
			AddChild(_scaleMapDialog);
			_scaleMapDialog.MoveToFront();

			var margin = new MarginContainer();
			margin.Name = "Margin_MarginContainer";
			margin.AddThemeConstantOverride("margin_left", 16);
			margin.AddThemeConstantOverride("margin_right", 16);
			margin.AddThemeConstantOverride("margin_top", 14);
			margin.AddThemeConstantOverride("margin_bottom", 14);
			_scaleMapDialog.AddChild(margin);

			var innerVBox = new VBoxContainer();
			innerVBox.Name = "InnerVBox_VBoxContainer";
			innerVBox.AddThemeConstantOverride("separation", 12);
			margin.AddChild(innerVBox);

			var titleLabel = new Label();
			titleLabel.Name = "TitleLabel_Label";
			titleLabel.Text = TranslationServer.Translate("⚖ Scale Map");
			titleLabel.AddThemeColorOverride("font_color", UIStyle.ColorGoldDull);
			titleLabel.AddThemeFontSizeOverride("font_size", 14);
			titleLabel.HorizontalAlignment = HorizontalAlignment.Center;
			innerVBox.AddChild(titleLabel);

			var hintLabel = new Label();
			hintLabel.Name = "HintLabel_Label";
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
			UIStyle.ApplyButtonStyleBoxes(btn);
			btn.AddThemeFontSizeOverride("font_size", fontSize);
			btn.FocusMode = FocusModeEnum.None;
			if (!string.IsNullOrEmpty(tooltip))
			{
				btn.TooltipText = TranslationServer.Translate(tooltip);
			}

			btn.Pressed += () =>
			{
				GameContext.UserInterface?.PlayClickSound();
				onClick?.Invoke();
			};
		}

		private void SelectCliffTexture(int index)
		{
			if (GameContext.Host != null)
			{
				GameContext.EditorService.SelectCliffTexture(index);
				if (!IsSwatchCompatibleTool(GameContext.Host.ActiveEditorTool))
				{
					TriggerToolSelection(EditorTool.PaintTexture, _btnTextureBrush);
				}

				UpdateTextureLabels();

				string name = TranslationServer.Translate(GameContext.EditorService.SwatchDisplayNames[index]);
				ShowFeedback(string.Format(TranslationServer.Translate("Selected Cliff Face: {0}"), name));
			}
		}

		private static bool IsSwatchCompatibleTool(EditorTool tool) =>
			GameContext.EditorService.IsSwatchCompatibleToolDomain(tool);

		public void UpdateTextureLabels()
		{
			if (GameContext.Host == null) return;

			EnsureSwatchDataCurrent();

			var (terrainName, cliffName) = GameContext.EditorService.GetTextureNamesDomain();
			int terrainIdx = GameContext.EditorService.EditorPaintTextureIndex;
			int cliffIdx = GameContext.EditorService.EditorCliffPaintTextureIndex;

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

		private void UpdateBrushSizeVisibility()
		{
			if (_brushSizeBox == null || GameContext.Host == null) return;

			var tool = GameContext.Host.ActiveEditorTool;
			_brushSizeBox.Visible = tool != EditorTool.FloodFill && tool != EditorTool.FloodFillPathing;
		}

		private void UpdateBrushShapeVisibility()
		{
			if (_btnBrushShape == null || GameContext.Host == null) return;

			var tool = GameContext.Host.ActiveEditorTool;
			_btnBrushShape.Visible = tool != EditorTool.FloodFill && tool != EditorTool.FloodFillPathing;
		}

		private void UpdateBrushStrengthVisibility()
		{
			if (_sldBrushStrength != null && _sldBrushStrength.GetParent() is Control strengthParent)
			{
				if (GameContext.Host == null) return;
				var tool = GameContext.Host.ActiveEditorTool;
				bool blockModeEnabled = (_chkBlockMode != null && _chkBlockMode.ButtonPressed);

				bool isClumpPlacement = tool == EditorTool.PlacePropClump ||
				                        ((tool == EditorTool.PlaceUnit || tool == EditorTool.PlaceProp || tool == EditorTool.PlaceDecal) &&
				                         (_chkClumpMode != null && _chkClumpMode.ButtonPressed));

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
			_inspectorPanel = GetNode<PanelContainer>("RightSlidePanel/RightScroll/AccordionContainer/InspectorAccordion/ContentInspector/InspectorPanel");
			_lblInspectorTitle = GetNode<Label>("RightSlidePanel/RightScroll/AccordionContainer/InspectorAccordion/ContentInspector/InspectorPanel/VBox/LblInspectorTitle");
			_lblInspectorPos = GetNode<Label>("RightSlidePanel/RightScroll/AccordionContainer/InspectorAccordion/ContentInspector/InspectorPanel/VBox/LblInspectorPos");				_btnInspectorRotLeft = GetNode<Button>("RightSlidePanel/RightScroll/AccordionContainer/InspectorAccordion/ContentInspector/InspBox_VBoxContainer/InspGrid_GridContainer/BtnInspectorRotLeft");
			SetupButton(_btnInspectorRotLeft, $"{UnicodeIcons.UNDO} ROT -15°", () => RotateSelectedObjectAction(-15f), 11, "Rotate object counter-clockwise (Shift + Mouse_Wheel)");				_btnInspectorRotRight = GetNode<Button>("RightSlidePanel/RightScroll/AccordionContainer/InspectorAccordion/ContentInspector/InspBox_VBoxContainer/InspGrid_GridContainer/BtnInspectorRotRight");
			SetupButton(_btnInspectorRotRight, $"{UnicodeIcons.ROTATE} ROT +15°", () => RotateSelectedObjectAction(15f), 11, "Rotate object clockwise (Shift + Mouse_Wheel)");				_btnInspectorScaleDown = GetNode<Button>("RightSlidePanel/RightScroll/AccordionContainer/InspectorAccordion/ContentInspector/InspBox_VBoxContainer/InspGrid_GridContainer/BtnInspectorScaleDown");
			SetupButton(_btnInspectorScaleDown, $"{UnicodeIcons.MINUS} SCALE DOWN", () => ScaleSelectedObjectAction(0.9f), 11, "Shrink object size by 10% (Alt + Mouse_Wheel)");				_btnInspectorScaleUp = GetNode<Button>("RightSlidePanel/RightScroll/AccordionContainer/InspectorAccordion/ContentInspector/InspBox_VBoxContainer/InspGrid_GridContainer/BtnInspectorScaleUp");
			SetupButton(_btnInspectorScaleUp, $"{UnicodeIcons.PLUS} SCALE UP", () => ScaleSelectedObjectAction(1.1f), 11, "Enlarge object size by 10% (Alt + Mouse_Wheel)");				_btnInspectorScaleReset = GetNode<Button>("RightSlidePanel/RightScroll/AccordionContainer/InspectorAccordion/ContentInspector/InspBox_VBoxContainer/InspGrid_GridContainer/BtnInspectorScaleReset");
			SetupButton(_btnInspectorScaleReset, $"{UnicodeIcons.UNDO} RESET SCALE", () => ScaleSelectedObjectAction(-1f), 12, "Reset object scale size to 1.0x");

			_btnCenter = GetNode<Button>("RightSlidePanel/RightScroll/AccordionContainer/InspectorAccordion/ContentInspector/InspectorPanel/VBox/BtnCenter");
			SetupButton(_btnCenter, $"{UnicodeIcons.CROSSHAIRS_TARGET} LOCATE OBJECT", () => LocateSelectedObjectAction(), 12, "Center camera on selected object");				_btnInspectorDelete = GetNode<Button>("RightSlidePanel/RightScroll/AccordionContainer/InspectorAccordion/ContentInspector/InspBox_VBoxContainer/InspGrid_GridContainer/BtnInspectorDelete");
			SetupButton(_btnInspectorDelete, $"{UnicodeIcons.TRASH_ALT} ERASE", () => DeleteSelectedObjectAction(), 12, "Erase selected unit, prop, or decal");

			_btnShowCoverage = new Button();
			_btnShowCoverage.Name = "BtnShowCoverage_Button";
			_btnShowCoverage.Text = TranslationServer.Translate($"{UnicodeIcons.EYE} RANGES: OFF");
			var fontCoverage = GetFontAwesomeFont();
			if (fontCoverage != null) _btnShowCoverage.AddThemeFontOverride("font", fontCoverage);
			_btnShowCoverage.ToggleMode = true;
			_btnShowCoverage.FocusMode = FocusModeEnum.None;
			_btnShowCoverage.AddThemeFontSizeOverride("font_size", 11);
			_btnShowCoverage.ButtonPressed = false;
			_btnShowCoverage.Toggled += (pressed) =>
			{
				if (GameContext.Host != null)
				{
					GameContext.Host.EditorCoverageOverlayEnabled = pressed;
					_btnShowCoverage.Text = pressed ? TranslationServer.Translate($"{UnicodeIcons.EYE} RANGES: ON") : TranslationServer.Translate($"{UnicodeIcons.EYE} RANGES: OFF");
					GameContext.Host.UpdateEditorCoverageOverlay();
				}
			};
			var inspectorVBox = GetNode<VBoxContainer>("RightSlidePanel/RightScroll/AccordionContainer/InspectorAccordion/ContentInspector/InspectorPanel/VBox");
			inspectorVBox.AddChild(_btnShowCoverage);

			_playerOwnerContainer = new HBoxContainer();
			_playerOwnerContainer.Name = "PlayerOwnerContainer";
			_playerOwnerContainer.Visible = false;

			var lblPlayer = new Label();
			lblPlayer.Name = "LblPlayer_Label";
			lblPlayer.Text = TranslationServer.Translate("Player");
			lblPlayer.CustomMinimumSize = new Vector2(50, 0);
			lblPlayer.AddThemeFontSizeOverride("font_size", 11);
			_playerOwnerContainer.AddChild(lblPlayer);

			_optPlayerOwner = new OptionButton();
			_optPlayerOwner.Name = "OptPlayerOwner";
			_optPlayerOwner.SizeFlagsHorizontal = SizeFlags.ExpandFill;
			_optPlayerOwner.AddThemeFontSizeOverride("font_size", 11);

			for (int i = 0; i < PlayerColorConfig.Palette.Length; i++)
			{
				_optPlayerOwner.AddItem(PlayerColorConfig.GetPaletteLabel(i), i);
			}

			_optPlayerOwner.ItemSelected += (long index) =>
			{
				if (IsUpdatingInspectorUI) return;
				if (GameContext.Host != null && GameContext.Host.SelectedEditorObject is Unit3D unit && GodotObject.IsInstanceValid(unit))
				{
					int playerIndex = (int)index;
					GameContext.Host.SetUnitPlayerExternal(unit, playerIndex);
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
			RefreshWaterSwatches();
			ApplyEditorPreferences(EditorSettingsDialog.CurrentSettings);

			InitInspectorActionButtons(inspectorVBox);
		}

		private void InitInspectorActionButtons(VBoxContainer inspectorVBox)
		{
			_btnOpenGlobalOverrides = new Button();
			_btnOpenGlobalOverrides.Name = "BtnOpenGlobalOverrides";
			_btnOpenGlobalOverrides.Set("icon_max_width", 0);
			_btnOpenGlobalOverrides.Text = "✏️ " + TranslationServer.Translate("Edit Template");
			_btnOpenGlobalOverrides.AddThemeFontSizeOverride("font_size", 11);
			_btnOpenGlobalOverrides.FocusMode = FocusModeEnum.None;
			_btnOpenGlobalOverrides.CustomMinimumSize = new Vector2(0, 28);
			_btnOpenGlobalOverrides.Visible = false;
			_btnOpenGlobalOverrides.Pressed += () =>
			{
				if (GameContext.Host != null && GodotObject.IsInstanceValid(GameContext.Host.SelectedEditorObject))
				{
					_entityVisualEditDialog?.OpenForObject(GameContext.Host.SelectedEditorObject);
				}
			};
			inspectorVBox.AddChild(_btnOpenGlobalOverrides);

			BtnEditVfx = new Button();
			BtnEditVfx.Name = "BtnEditVfx";
			BtnEditVfx.Set("icon_max_width", 0);
			BtnEditVfx.Text = "✨ " + TranslationServer.Translate("Edit VFX");
			BtnEditVfx.AddThemeFontSizeOverride("font_size", 11);
			BtnEditVfx.FocusMode = FocusModeEnum.None;
			BtnEditVfx.CustomMinimumSize = new Vector2(0, 28);
			BtnEditVfx.Visible = false;
			BtnEditVfx.Pressed += () =>
			{
				if (GameContext.Host != null && GodotObject.IsInstanceValid(GameContext.Host.SelectedEditorObject) && GameContext.Host.SelectedEditorObject is ProceduralVfxInstance3D vfx)
				{
					VfxAttachmentConfig editConfig = vfx.Config;
					if (GameContext.RegistryService.TryGetVfxConfig(editConfig?.VfxId, out var templateConfig))
					{
						editConfig = templateConfig.Clone();
					}

					OpenVfxStudioDialog(editConfig, (newCfg) => { vfx.UpdateConfig(newCfg); });
				}
			};
			inspectorVBox.AddChild(BtnEditVfx);
		}

		public void OpenWeaponVfxDialog(string weaponId, WeaponMetadata weapon, Action<WeaponMetadata> onApplied = null)
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

		public void OpenObjectAttachmentDialog(
			string unitId = null,
			string attachmentId = null,
			string hand = "RightHand",
			Node3D sourceModel = null,
			Action<HandAttachmentOrientation> onApplied = null)
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
				GD.PrintErr($"[MapEditorHUD] RemoveUnitObjectAttachment error: {ex.Message}");
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
				GD.PrintErr($"[MapEditorHUD] RestoreUnitObjectAttachments error: {ex.Message}");
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
				GD.PrintErr($"[MapEditorHUD] RestoreUnitPseudoSockets error: {ex.Message}");
			}
		}

		public void OpenShaderEditorDialog(string shaderKey = "", Action<CustomShaderConfig> onSaved = null)
		{
			if (_shaderEditorDialog == null)
			{
				_shaderEditorDialog = new ShaderEditorDialog(this);
			}

			_shaderEditorDialog.OpenForShader(shaderKey, onSaved);
		}

		public void OpenProceduralAnimationStudioDialog(ProceduralAnimationConfig initialConfig = null, Action<ProceduralAnimationConfig> onApplied = null, string previewModelKey = null)
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

		private async void TestMapAction()
		{
			if (GameContext.Host == null) return;

			if (GameContext.Host.AllUnits.Count == 0)
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
				_templateManagerDialog = new TemplateManagerDialog(this);
			}

			_templateManagerDialog.OpenDialog();
		}

		public void OpenEditorSettingsDialog()
		{
			_editorSettingsDialog?.OpenDialog();
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

		private void UpdateMetadataJsonAsset(string category, string fileName, string blake3Hash, string subCategory = null, int columns = 0, int rows = 0, int targetSlot = -1)
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
				GD.PrintErr($"ConvertRawTextureDirect error: {ex.Message}");
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
				string wsPath = string.IsNullOrEmpty(GameContext.EditorService._tempWorkspacePath)
					? ProjectSettings.GlobalizePath(GameContext.EditorService.TempWorkspaceGodotPath)
					: GameContext.EditorService._tempWorkspacePath;

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
				GD.PrintErr($"ImportGlbAssetFromExtension error: {ex.Message}");
				ShowFeedback(string.Format(TranslationServer.Translate("Failed to import GLB asset: {0}"), ex.Message));
			}
		}

		private HSlider CreateSliderRow(VBoxContainer parent, string labelText, float min, float max, float step, float initialVal, Action<float> onChanged, string format = "0.0#", float labelWidth = 70f)
		{
			var row = new HBoxContainer();
			row.Name = "Row_HBoxContainer";

			var lblName = new Label();
			lblName.Name = "LblName_Label";
			lblName.Text = labelText;
			lblName.CustomMinimumSize = new Vector2(labelWidth, 0);
			lblName.AddThemeFontSizeOverride("font_size", 11);
			row.AddChild(lblName);

			var slider = new HSlider();
			slider.Name = "Slider_HSlider";
			slider.MinValue = min;
			slider.MaxValue = max;
			slider.Step = step;
			slider.Value = initialVal;
			slider.SizeFlagsHorizontal = SizeFlags.ExpandFill;
			slider.DragStarted += () => _isDraggingSlider = true;
			slider.DragEnded += (valueChanged) => _isDraggingSlider = false;
			row.AddChild(slider);

			var lblVal = new Label();
			lblVal.Name = "LblVal_Label";
			lblVal.Text = initialVal.ToString(format, CultureInfo.InvariantCulture);
			lblVal.CustomMinimumSize = new Vector2(34, 0);
			lblVal.HorizontalAlignment = HorizontalAlignment.Right;
			lblVal.AddThemeFontSizeOverride("font_size", 11);
			row.AddChild(lblVal);

			slider.SetMeta("val_label", lblVal);
			slider.SetMeta("val_format", format);

			slider.ValueChanged += (double val) =>
			{
				float fVal = (float)val;
				lblVal.Text = fVal.ToString(format, CultureInfo.InvariantCulture);
				onChanged(fVal);
			};

			parent.AddChild(row);
			return slider;
		}

		public void RefreshWaterSwatches()
		{
			if (_optWaterMode == null) return;

			byte currentProf = GameContext.Host != null ? GameContext.Host.ActiveWaterProfileIndex : (byte)0;
			if (_optWaterMode.Selected >= 0 && _optWaterMode.Selected < _optWaterMode.ItemCount)
			{
				var currentMeta = _optWaterMode.GetItemMetadata(_optWaterMode.Selected);
				if (currentMeta.VariantType != Variant.Type.Nil)
				{
					currentProf = (byte)(int)currentMeta;
				}
			}

			_optWaterMode.Clear();

			var profiles = GameContext.Terrain != null ? GameContext.Terrain.GetWaterProfiles() : null;
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

			GameContext.EnvironmentService.SetActiveWaterProfileIndex(currentProf);
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

			List<EnvironmentPresetConfig> presetsList = null;
			string defaultPreset = null;
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
				GameHost tempQualifier = GameContext.Host;
				presetsList = (tempQualifier != null ? GameContext.EnvironmentService : null)?.GetPresets().ToList() ?? EnvironmentPresetConfig.CreateDefaultPresets();
			}

			GameHost tempQualifier1 = GameContext.Host;
			string currentId = (tempQualifier1 != null ? GameContext.EnvironmentService : null)?.GetCurrentPresetId() ?? defaultPreset ?? "day";
			_environmentConfigDialog?.OpenWithPresets(presetsList, currentId, defaultPreset);
		}
	}
}