using System;
using Godot;
using Realm.Client.Core;
using Realm.Client.Services;
using Realm.Client.Utils;

namespace Realm.Client.UI.MapEditor;

/// <summary>
/// What the top toolbar needs from the editor: its node tree and the shared button styling, the FontAwesome face,
/// and the editor actions each button commits to. The toolbar is pure chrome, so every action arrives as a
/// delegate rather than the panel reaching back into HUD members.
/// </summary>
public sealed record TopToolbarHooks(
	Control Owner,
	HBoxContainer? TopLeftBox,
	Action<Button> StyleTopButton,
	Action<Button, int> StyleCompactTopButton,
	Action<OptionButton> StyleOptionPopup,
	Action<Button, string, Action?, int, string> SetupButton,
	Action<Button, string, Action?, int, string> SetupOptionButton,
	Action<Button, string, Action, string> SetupIconButton,
	Func<Font?> FontAwesomeFont,
	Action UpdateMapNameHeader,
	Action OpenMapSettings,
	Action BackToHub,
	Action ToggleHelpPanel,
	Action Undo,
	Action Redo,
	Action<EditorTool, Button?> SelectTool,
	Action<Realm.Client.UI.EditorModule> SwitchModule,
	Action OpenSettingsOverlay,
	Action TestMap,
	Action OpenTestSettingsDialog,
	Action ResetAllPanelPositions,
	Action ToggleVSCode,
	Action GenerateVSCodeFiles,
	Action OpenAssetManager,
	Action OpenTemplateManager,
	Action OpenInstanceManager,
	Action OpenImportFromMap,
	Action OpenUtilities);

/// <summary>
/// The editor's top-left toolbar: back to hub, the map-name header that opens map settings, help, undo/redo, the
/// eyedropper, the module picker, the game-settings gear, and the test and layout buttons at the end of the row. It
/// also builds the "Windows" popup behind the editors button, which hosts the embedded code editor and the asset,
/// template, instance, import and utilities dialogs. It owns those nodes; the actions they fire stay on the editor.
/// </summary>
public sealed class TopToolbarPanel
{
	private Control? _owner;

	public Button BackToHubButton { get; private set; } = null!;
	public PanelContainer MapNameHeaderPanel { get; private set; } = null!;
	public Label MapNameLabel { get; private set; } = null!;
	public Button UndoButton { get; private set; } = null!;
	public Button RedoButton { get; private set; } = null!;
	public Button EyedropperButton { get; private set; } = null!;
	public OptionButton ModuleOption { get; private set; } = null!;
	public Button? GameSettingsButton { get; private set; }
	public Button? TestMapButton { get; private set; }
	public OptionButton? TestSettingsOption { get; private set; }
	public Button? ResetLayoutButton { get; private set; }
	public Button EditorsButton { get; private set; } = null!;
	public PopupPanel EditorsPopup { get; private set; } = null!;
	public Button? VSCodeButton { get; private set; }
	public Button AssetsManagerButton { get; private set; } = null!;
	public Button TemplateManagerButton { get; private set; } = null!;
	public Button InstanceManagerButton { get; private set; } = null!;
	public Button ImportFromMapButton { get; private set; } = null!;
	public Button UtilitiesButton { get; private set; } = null!;
	public Label StatusLabel { get; private set; } = null!;
	public Label FeedbackLabel { get; private set; } = null!;

	public void Build(TopToolbarHooks hooks)
	{
		_owner = hooks.Owner;
		BuildTopLeftToolbar(hooks);
	}

	/// <summary>
	/// The window-level chrome the toolbar row shares: the status readout in the bottom bar and the fading feedback
	/// label. The side scrollers are configured with the rest of the editor frame's theme, not here.
	/// </summary>
	public void BuildWindowChrome()
	{
		if (_owner == null) return;

		StatusLabel = _owner.GetNode<Label>("BottomCenterBar/TopBar/HBox/StatusLabel");
		FeedbackLabel = _owner.GetNode<Label>("FeedbackLabel");
		FeedbackLabel.Modulate = new Color(1, 1, 1, 0);
		FeedbackLabel.MouseFilter = Control.MouseFilterEnum.Ignore;
	}

	private void BuildTopLeftToolbar(TopToolbarHooks hooks)
	{
		BackToHubButton = hooks.Owner.GetNode<Button>("TopLeftBox/BtnBack");
		hooks.SetupButton(BackToHubButton, $"{UnicodeIcons.SIGN_OUT} " + TranslationServer.Translate("BACK TO HUB"), () => hooks.BackToHub(), 13, "Exit editor and return to game lobby");
		hooks.StyleTopButton(BackToHubButton);

		MapNameHeaderPanel = new PanelContainer();
		MapNameHeaderPanel.Name = "MapNameHeaderPanel";
		MapNameHeaderPanel.AddThemeStyleboxOverride("panel", UIStyle.CreateLightInnerPanel());
		MapNameHeaderPanel.CustomMinimumSize = new Vector2(160, 32);
		MapNameHeaderPanel.MouseFilter = Control.MouseFilterEnum.Stop;
		MapNameHeaderPanel.MouseDefaultCursorShape = Control.CursorShape.PointingHand;
		MapNameHeaderPanel.TooltipText = TranslationServer.Translate("Click to open Map Settings");
		MapNameHeaderPanel.GuiInput += (ev) =>
		{
			if (ev is InputEventMouseButton mb && mb.Pressed && mb.ButtonIndex == MouseButton.Left)
			{
				hooks.OpenMapSettings();
			}
		};

		var mapNameHBox = new HBoxContainer();
		mapNameHBox.Name = "MapNameHBox_HBoxContainer";
		mapNameHBox.Alignment = BoxContainer.AlignmentMode.Center;
		mapNameHBox.AddThemeConstantOverride("separation", 6);
		MapNameHeaderPanel.AddChild(mapNameHBox);

		MapNameLabel = new Label();
		MapNameLabel.Name = "LblMapNameHeader";
		MapNameLabel.Text = "";
		MapNameLabel.AddThemeFontSizeOverride("font_size", 12);
		MapNameLabel.AddThemeColorOverride("font_color", UIStyle.ColorGold);
		MapNameLabel.HorizontalAlignment = HorizontalAlignment.Center;
		MapNameLabel.VerticalAlignment = VerticalAlignment.Center;
		MapNameLabel.MouseFilter = Control.MouseFilterEnum.Pass;
		mapNameHBox.AddChild(MapNameLabel);

		if (hooks.TopLeftBox != null)
		{
			hooks.TopLeftBox.AddChild(MapNameHeaderPanel);
			int backIdx = BackToHubButton.GetIndex();
			hooks.TopLeftBox.MoveChild(MapNameHeaderPanel, backIdx + 1);
		}

		hooks.UpdateMapNameHeader();

		var btnHelp = hooks.Owner.GetNode<Button>("TopLeftBox/BtnHelp");
		hooks.SetupButton(btnHelp, $"{UnicodeIcons.HELP} " + TranslationServer.Translate("HELP"), () => hooks.ToggleHelpPanel(), 13, "Toggle the hotkeys and editor guide overlay (H)");
		hooks.StyleTopButton(btnHelp);

		BuildEditorsPopup(hooks);

		UndoButton = hooks.Owner.GetNode<Button>("TopLeftBox/BtnUndo");
		hooks.SetupButton(UndoButton, $"{UnicodeIcons.UNDO} " + TranslationServer.Translate("UNDO"), () => hooks.Undo(), 13, "Undo the last action (Ctrl+Z)");
		hooks.StyleTopButton(UndoButton);

		RedoButton = hooks.Owner.GetNode<Button>("TopLeftBox/BtnRedo");
		hooks.SetupButton(RedoButton, $"{UnicodeIcons.REDO} " + TranslationServer.Translate("REDO"), () => hooks.Redo(), 13, "Redo the last undone action (Ctrl+Y)");
		hooks.StyleTopButton(RedoButton);

		EyedropperButton = hooks.Owner.GetNode<Button>("TopLeftBox/BtnEyedropper");
		hooks.SetupButton(EyedropperButton, $"{UnicodeIcons.EYEDROPPER} " + TranslationServer.Translate("EYEDROPPER"), () => hooks.SelectTool(EditorTool.Eyedropper, EyedropperButton), 13, "Pick / sample entities, terrain height (Shift+Click), or vertex color under cursor (I)");
		hooks.StyleTopButton(EyedropperButton);

		ModuleOption = hooks.Owner.GetNode<OptionButton>("TopLeftBox/OptModule");
		hooks.StyleOptionPopup(ModuleOption);
		ModuleOption.AddItem($"{UnicodeIcons.MOUNTAIN} " + TranslationServer.Translate("TERRAIN") + " (F1)", (int)Realm.Client.UI.EditorModule.Terrain);
		ModuleOption.AddItem($"{UnicodeIcons.PAINT_BRUSH} " + TranslationServer.Translate("TEXTURE") + " (F2)", (int)Realm.Client.UI.EditorModule.TextureDeco);
		ModuleOption.AddItem($"{UnicodeIcons.VECTOR_SQUARE} " + TranslationServer.Translate("PATHING") + " (F3)", (int)Realm.Client.UI.EditorModule.Pathing);
		ModuleOption.AddItem($"{UnicodeIcons.CUBE} " + TranslationServer.Translate("OBJECTS") + " (F4)", (int)Realm.Client.UI.EditorModule.Objects);
		ModuleOption.AddItem($"{UnicodeIcons.DRAW_POLYGON} " + TranslationServer.Translate("COORDINATES") + " (F5)", (int)Realm.Client.UI.EditorModule.Coordinates);
		ModuleOption.AddItem($"{UnicodeIcons.PASTE} " + TranslationServer.Translate("CLIPBOARD") + " (F6)", (int)Realm.Client.UI.EditorModule.Clipboard);
		ModuleOption.ItemSelected += (index) => hooks.SwitchModule((Realm.Client.UI.EditorModule)index);
		hooks.StyleTopButton(ModuleOption);

		GameSettingsButton = hooks.Owner.GetNodeOrNull<Button>("TopLeftBox/BtnSettings");
		if (GameSettingsButton != null)
		{
			hooks.SetupIconButton(GameSettingsButton, "res://Assets/UI/gear_icon.webp", () => hooks.OpenSettingsOverlay(), "Game Settings");
			hooks.StyleTopButton(GameSettingsButton);
		}

		BuildTestAndLayoutButtons(hooks);
	}

	private void BuildTestAndLayoutButtons(TopToolbarHooks hooks)
	{
		TestMapButton = hooks.Owner.GetNodeOrNull<Button>("TopLeftBox/BtnTestMap");
		if (TestMapButton == null)
		{
			TestMapButton = new Button();
			TestMapButton.Name = "BtnTestMap";
			if (hooks.TopLeftBox != null)
			{
				hooks.TopLeftBox.AddChild(TestMapButton);
			}
		}

		hooks.SetupButton(TestMapButton, $"{UnicodeIcons.GAMEPAD} " + TranslationServer.Translate("TEST"), () => hooks.TestMap(), 13, "Launch single-player mode on the current editor map");
		hooks.StyleTopButton(TestMapButton);

		TestSettingsOption = new OptionButton();
		TestSettingsOption.Name = "OptTestSettings";
		TestSettingsOption.FitToLongestItem = false;
		TestSettingsOption.CustomMinimumSize = new Vector2(20, 0);
		TestSettingsOption.SizeFlagsHorizontal = Control.SizeFlags.ShrinkBegin;
		TestSettingsOption.Alignment = HorizontalAlignment.Center;
		TestSettingsOption.AddThemeIconOverride("arrow", new PlaceholderTexture2D { Size = Vector2.Zero });
		TestSettingsOption.AddThemeConstantOverride("arrow_margin", 0);
		TestSettingsOption.Text = UnicodeIcons.CHEVRON_DOWN;
		var font = hooks.FontAwesomeFont();
		if (font != null)
		{
			TestSettingsOption.AddThemeFontOverride("font", font);
		}
		TestSettingsOption.AddThemeFontSizeOverride("font_size", 11);
		TestSettingsOption.TooltipText = "Test Settings";
		TestSettingsOption.AddItem("Test Settings", 0);
		TestSettingsOption.Selected = -1;
		TestSettingsOption.Text = UnicodeIcons.CHEVRON_DOWN;
		hooks.StyleOptionPopup(TestSettingsOption);
		hooks.StyleCompactTopButton(TestSettingsOption, 4);
		TestSettingsOption.ItemSelected += (idx) =>
		{
			Callable.From(() =>
			{
				TestSettingsOption.Selected = -1;
				TestSettingsOption.Text = UnicodeIcons.CHEVRON_DOWN;
			}).CallDeferred();
			hooks.OpenTestSettingsDialog();
		};
		if (hooks.TopLeftBox != null)
		{
			hooks.TopLeftBox.AddChild(TestSettingsOption);
		}

		if (hooks.TopLeftBox != null)
		{
			if (TestMapButton != null && GameSettingsButton != null)
			{
				hooks.TopLeftBox.MoveChild(TestMapButton, GameSettingsButton.GetIndex() + 1);
				hooks.TopLeftBox.MoveChild(TestSettingsOption, TestMapButton.GetIndex() + 1);
			}

			ResetLayoutButton = new Button();
			ResetLayoutButton.Name = "BtnResetLayout";
			hooks.SetupButton(ResetLayoutButton, $"{UnicodeIcons.PIN} " + TranslationServer.Translate("RESET LAYOUT"), () => hooks.ResetAllPanelPositions(), 12, "Reset all floating panels back to default sidebar positions");
			hooks.StyleTopButton(ResetLayoutButton);
			hooks.TopLeftBox.AddChild(ResetLayoutButton);
			if (GameSettingsButton != null)
			{
				hooks.TopLeftBox.MoveChild(ResetLayoutButton, GameSettingsButton.GetIndex());
			}
		}
	}

	private void BuildEditorsPopup(TopToolbarHooks hooks)
	{
		var editorsVBox = BuildEditorsPopupShell(hooks);

		BuildVSCodeButton(hooks, editorsVBox);

		BuildEditorModuleButtons(hooks, editorsVBox);

		EditorsButton = hooks.Owner.GetNodeOrNull<Button>("TopLeftBox/BtnEditors") ?? hooks.Owner.GetNodeOrNull<Button>("TopLeftBox/BtnVSCode");
		if (EditorsButton == null)
		{
			EditorsButton = new Button();
			EditorsButton.Name = "BtnEditors";
		}

		hooks.SetupButton(EditorsButton, TranslationServer.Translate("WINDOWS") + $" {UnicodeIcons.CHEVRON_DOWN}", () =>
		{
			var popupPosition = EditorsButton.GetScreenPosition() + new Vector2(0, EditorsButton.Size.Y);
			EditorsPopup.Popup(new Rect2I((Vector2I)popupPosition, Vector2I.Zero));
		}, 13, "Open Modules");
		hooks.StyleTopButton(EditorsButton);
	}

	private VBoxContainer BuildEditorsPopupShell(TopToolbarHooks hooks)
	{
		EditorsPopup = new PopupPanel();
		EditorsPopup.Name = "PopupEditors";
		EditorsPopup.AddThemeStyleboxOverride("panel", UIStyle.CreateEditorPopupPanel(10));

		var editorsVBox = new VBoxContainer();
		editorsVBox.Name = "EditorsVBox_VBoxContainer";
		editorsVBox.AddThemeConstantOverride("separation", 6);
		editorsVBox.CustomMinimumSize = new Vector2(170, 0);
		EditorsPopup.AddChild(editorsVBox);
		hooks.Owner.AddChild(EditorsPopup);
		return editorsVBox;
	}

	private void BuildVSCodeButton(TopToolbarHooks hooks, VBoxContainer editorsVBox)
	{
		VSCodeButton = new Button();
		VSCodeButton.Name = "BtnVSCode";
		hooks.SetupOptionButton(VSCodeButton, $"{UnicodeIcons.CODE} " + TranslationServer.Translate("CODE & DATA"), () => hooks.ToggleVSCode(), 13, "Toggle the embedded VSCode editor (Right-click or Middle-click: DevTools)");
		VSCodeButton.Pressed += () => EditorsPopup.Hide();

		if (OperatingSystem.IsWindows())
		{
			hooks.GenerateVSCodeFiles();
			GameContext.VSCode.Initialize(hooks.Owner);
			VSCodeButton.GuiInput += (@event) =>
			{
				if (OperatingSystem.IsWindows() && @event is InputEventMouseButton mouseButton && mouseButton.Pressed)
				{
					if (mouseButton.ButtonIndex == MouseButton.Right || mouseButton.ButtonIndex == MouseButton.Middle)
					{
						if (!GameContext.VSCode.IsVisible)
						{
							hooks.ToggleVSCode();
						}

						hooks.Owner.GetViewport().SetInputAsHandled();
					}
				}
			};
			editorsVBox.AddChild(VSCodeButton);
		}
	}

	private void BuildEditorModuleButtons(TopToolbarHooks hooks, VBoxContainer editorsVBox)
	{
		AssetsManagerButton = new Button();
		AssetsManagerButton.Name = "BtnAssetsManager";
		hooks.SetupOptionButton(AssetsManagerButton, $"{UnicodeIcons.CUBE} " + TranslationServer.Translate("ASSETS"), () => hooks.OpenAssetManager(), 13, "Open Map Assets Manager & Importer");
		AssetsManagerButton.Pressed += () => EditorsPopup.Hide();
		editorsVBox.AddChild(AssetsManagerButton);

		TemplateManagerButton = new Button();
		TemplateManagerButton.Name = "BtnTemplateManager";
		hooks.SetupOptionButton(TemplateManagerButton, $"{UnicodeIcons.CUBES} " + TranslationServer.Translate("TEMPLATES"), () => hooks.OpenTemplateManager(), 13, "Open dialog to manage object template types and visual properties");
		TemplateManagerButton.Pressed += () => EditorsPopup.Hide();
		editorsVBox.AddChild(TemplateManagerButton);

		InstanceManagerButton = new Button();
		InstanceManagerButton.Name = "BtnInstanceManager";
		hooks.SetupOptionButton(InstanceManagerButton, $"{UnicodeIcons.LIST} " + TranslationServer.Translate("INSTANCES"), () => hooks.OpenInstanceManager(), 13, "Open dialog to list and locate all placed instances");
		InstanceManagerButton.Pressed += () => EditorsPopup.Hide();
		editorsVBox.AddChild(InstanceManagerButton);

		ImportFromMapButton = new Button();
		ImportFromMapButton.Name = "BtnImportFromMap";
		hooks.SetupOptionButton(ImportFromMapButton, $"🗺 " + TranslationServer.Translate("IMPORT FROM MAP"), () => hooks.OpenImportFromMap(), 13, "Copy assets, templates, and code from another map into this map");
		ImportFromMapButton.Pressed += () => EditorsPopup.Hide();
		editorsVBox.AddChild(ImportFromMapButton);

		UtilitiesButton = new Button();
		UtilitiesButton.Name = "BtnUtilities";
		hooks.SetupOptionButton(UtilitiesButton, $"{UnicodeIcons.WRENCH} " + TranslationServer.Translate("UTILITIES"), () => hooks.OpenUtilities(), 13, "Open batch utilities and maintenance tools");
		UtilitiesButton.Pressed += () => EditorsPopup.Hide();
		editorsVBox.AddChild(UtilitiesButton);
	}
}
