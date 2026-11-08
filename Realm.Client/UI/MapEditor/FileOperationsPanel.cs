using System;
using Godot;
using Realm.Client.Utils;

namespace Realm.Client.UI.MapEditor;

/// <summary>
/// What the file panel needs from the editor: its node tree, the shared accordion and button styling, and the map
/// actions each button fires. The popup buttons close their own popup, so nothing else is needed for them.
/// </summary>
public sealed record FileOperationsHooks(
	Node Owner,
	Action<Button> StyleAccordionHeader,
	Action<Button, Control, string> SetupAccordion,
	Action<Button, string, Action?, int, string> SetupOptionButton,
	Action LoadMap,
	Action ImportMapArchive,
	Action SaveMap,
	Action SaveAsMap,
	Action PublishMap,
	Action ExportMap,
	Action ResetMap,
	Action SetupRandomGenerationMenu,
	Action OpenEditorSettings,
	Action OpenMapVersions);

/// <summary>
/// The File accordion in the editor's left panel: load and save with their "more" popups (import archive on the
/// load side; save as, publish and export on the save side), the reset-map button, and the editor settings and map
/// versions entries. It owns those nodes; the map actions themselves stay on the editor.
/// </summary>
public sealed class FileOperationsPanel
{
	private const string AccordionPath = "LeftSlidePanel/LeftScroll/LeftVBox/FileAccordion";
	private const string ContentPath = AccordionPath + "/ContentFile";

	public Button Header { get; private set; } = null!;
	public VBoxContainer Content { get; private set; } = null!;
	public Button LoadButton { get; private set; } = null!;
	public Button LoadMoreButton { get; private set; } = null!;
	public PopupPanel LoadMorePopup { get; private set; } = null!;
	public Button ImportArchiveButton { get; private set; } = null!;
	public Button SaveButton { get; private set; } = null!;
	public Button SaveMoreButton { get; private set; } = null!;
	public PopupPanel SaveMorePopup { get; private set; } = null!;
	public Button SaveAsButton { get; private set; } = null!;
	public Button PublishButton { get; private set; } = null!;
	public Button ExportMapButton { get; private set; } = null!;
	public Button ResetMapButton { get; private set; } = null!;
	public Button EditorSettingsButton { get; private set; } = null!;
	public Button MapVersionsButton { get; private set; } = null!;

	public void Build(FileOperationsHooks hooks)
	{
		BuildAccordion(hooks);
		BuildLoadControls(hooks);
		BuildSaveControls(hooks);
		BuildResetMapButton(hooks);

		hooks.SetupRandomGenerationMenu();

		BuildSettingsAndVersionButtons(hooks);
	}

	private void BuildAccordion(FileOperationsHooks hooks)
	{
		Header = hooks.Owner.GetNode<Button>(AccordionPath + "/BtnHeaderFile");
		Content = hooks.Owner.GetNode<VBoxContainer>(ContentPath);
		hooks.StyleAccordionHeader(Header);
		hooks.SetupAccordion(Header, Content, TranslationServer.Translate("File"));
	}

	private void BuildLoadControls(FileOperationsHooks hooks)
	{
		LoadButton = hooks.Owner.GetNode<Button>(ContentPath + "/BoxFileOps/RowFileSaveLoad/RowLoadGroup/BtnLoad");
		hooks.SetupOptionButton(LoadButton, $"{UnicodeIcons.FOLDER_OPEN} " + TranslationServer.Translate("LOAD"), () => hooks.LoadMap(), 11, "Load heights, colors, and entities from a saved json file (Ctrl+O)");

		BuildLoadMorePopup(hooks);

		LoadMoreButton = hooks.Owner.GetNode<Button>(ContentPath + "/BoxFileOps/RowFileSaveLoad/RowLoadGroup/BtnLoadMore");
		LoadMoreButton.Set("icon_max_width", 0);
		hooks.SetupOptionButton(LoadMoreButton, UnicodeIcons.CHEVRON_DOWN, () =>
		{
			var popupPosition = LoadMoreButton.GetScreenPosition() + new Vector2(0, LoadMoreButton.Size.Y);
			LoadMorePopup.Popup(new Rect2I((Vector2I)popupPosition, Vector2I.Zero));
		}, 10, "More load options (Import map package)");
	}

	private void BuildLoadMorePopup(FileOperationsHooks hooks)
	{
		LoadMorePopup = hooks.Owner.GetNode<PopupPanel>("PopupLoadMore");
		var loadMorePopupStyle = new StyleBoxFlat();
		loadMorePopupStyle.BgColor = new Color(0.14f, 0.13f, 0.11f, 0.98f);
		loadMorePopupStyle.BorderColor = UIStyle.ColorGold;
		loadMorePopupStyle.SetBorderWidthAll(1);
		loadMorePopupStyle.SetCornerRadiusAll(4);
		loadMorePopupStyle.SetContentMarginAll(10);
		LoadMorePopup.AddThemeStyleboxOverride("panel", loadMorePopupStyle);
		ImportArchiveButton = hooks.Owner.GetNode<Button>("PopupLoadMore/LoadMoreVBox_VBoxContainer/BtnImportArchive");
		ImportArchiveButton.Set("icon_max_width", 0);
		hooks.SetupOptionButton(ImportArchiveButton, $"{UnicodeIcons.FILE_IMPORT} IMPORT (.RMAP)", () => hooks.ImportMapArchive(), 13, "Import a map package (.rmap) into the editor workspace and open it for editing; saving prompts for a destination folder");
		ImportArchiveButton.Pressed += () => LoadMorePopup.Hide();
	}

	private void BuildSaveControls(FileOperationsHooks hooks)
	{
		SaveButton = hooks.Owner.GetNode<Button>(ContentPath + "/BoxFileOps/RowFileSaveLoad/RowSaveGroup/BtnSave");
		hooks.SetupOptionButton(SaveButton, $"{UnicodeIcons.SAVE} " + TranslationServer.Translate("SAVE"), () => hooks.SaveMap(), 11, "Save current heightmap, textures, and entities (Ctrl+S)");

		BuildSaveMorePopup(hooks);

		SaveMoreButton = hooks.Owner.GetNode<Button>(ContentPath + "/BoxFileOps/RowFileSaveLoad/RowSaveGroup/BtnSaveMore");
		SaveMoreButton.Set("icon_max_width", 0);
		hooks.SetupOptionButton(SaveMoreButton, UnicodeIcons.CHEVRON_DOWN, () =>
		{
			var popupPosition = SaveMoreButton.GetScreenPosition() + new Vector2(0, SaveMoreButton.Size.Y);
			SaveMorePopup.Popup(new Rect2I((Vector2I)popupPosition, Vector2I.Zero));
		}, 10, "More save & export options (Save As, Export, Publish)");
	}

	private void BuildSaveMorePopup(FileOperationsHooks hooks)
	{
		SaveAsButton = hooks.Owner.GetNode<Button>("PopupSaveMore/SaveMoreVBox_VBoxContainer/BtnSaveAs");
		SaveAsButton.Set("icon_max_width", 0);
		hooks.SetupOptionButton(SaveAsButton, $"{UnicodeIcons.SAVE} " + TranslationServer.Translate("SAVE AS"), () => hooks.SaveAsMap(), 13, "Save map to a new folder location");

		PublishButton = hooks.Owner.GetNode<Button>("PopupSaveMore/SaveMoreVBox_VBoxContainer/BtnPublish");
		hooks.SetupOptionButton(PublishButton, $"{UnicodeIcons.UPLOAD} " + TranslationServer.Translate("PUBLISH"), () => hooks.PublishMap(), 13, "Publish/export map to custom map registry");

		ExportMapButton = hooks.Owner.GetNode<Button>("PopupSaveMore/SaveMoreVBox_VBoxContainer/BtnExportMap");
		ExportMapButton.Set("icon_max_width", 0);
		hooks.SetupOptionButton(ExportMapButton, $"{UnicodeIcons.FILE_EXPORT} " + TranslationServer.Translate("EXPORT (.RMAP)"), () => hooks.ExportMap(), 13, "Export prepared map package (.rmap) with compiled WASM for hosting and CAS storage");

		SaveMorePopup = hooks.Owner.GetNode<PopupPanel>("PopupSaveMore");
		var saveMorePopupStyle = new StyleBoxFlat();
		saveMorePopupStyle.BgColor = new Color(0.14f, 0.13f, 0.11f, 0.98f);
		saveMorePopupStyle.BorderColor = UIStyle.ColorGold;
		saveMorePopupStyle.SetBorderWidthAll(1);
		saveMorePopupStyle.SetCornerRadiusAll(4);
		saveMorePopupStyle.SetContentMarginAll(10);
		SaveMorePopup.AddThemeStyleboxOverride("panel", saveMorePopupStyle);
		SaveAsButton.Pressed += () => SaveMorePopup.Hide();
		ExportMapButton.Pressed += () => SaveMorePopup.Hide();
		PublishButton.Pressed += () => SaveMorePopup.Hide();
	}

	private void BuildResetMapButton(FileOperationsHooks hooks)
	{
		ResetMapButton = hooks.Owner.GetNode<Button>(ContentPath + "/BoxGenOps/FileGrid2_GridContainer/BtnResetMap");
		hooks.SetupOptionButton(ResetMapButton, $"{UnicodeIcons.TRASH} " + TranslationServer.Translate("RESET MAP"), () => hooks.ResetMap(), 13, TranslationServer.Translate("Start a new untitled map from the template"));
	}

	private void BuildSettingsAndVersionButtons(FileOperationsHooks hooks)
	{
		EditorSettingsButton = new Button();
		EditorSettingsButton.Name = "BtnEditorSettings";
		EditorSettingsButton.Set("icon_max_width", 0);
		hooks.SetupOptionButton(EditorSettingsButton, "⚙️ " + TranslationServer.Translate("EDITOR SETTINGS"), () => hooks.OpenEditorSettings(), 13, "Configure editor preferences, chrome border, and display overlays");
		Content.AddChild(EditorSettingsButton);
		Content.MoveChild(EditorSettingsButton, 1);

		MapVersionsButton = new Button();
		MapVersionsButton.Name = "BtnMapVersions";
		MapVersionsButton.Set("icon_max_width", 0);
		hooks.SetupOptionButton(MapVersionsButton, $"{UnicodeIcons.USER_CLOCK} " + TranslationServer.Translate("MAP VERSIONS"), () => hooks.OpenMapVersions(), 13, "Go back to earlier versions of this map");
		Content.AddChild(MapVersionsButton);
		Content.MoveChild(MapVersionsButton, 2);
	}

	/// <summary>
	/// Stretches every control in the file card to the card's full width. The card's children are built by several
	/// helpers, so the editor calls this once the whole accordion exists rather than each helper opting in.
	/// </summary>
	public void ExpandContentChildren()
	{
		var target = MapEditorStyle.ContentTarget(Content);
		if (target == null) return;

		foreach (Node child in target.GetChildren())
		{
			if (child is Control ctrl)
			{
				ctrl.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
			}
		}
	}

	public void StyleOperations()
	{
		var targetFile = MapEditorStyle.ContentTarget(Content);
		if (targetFile != null)
		{
			LoadButton.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
			LoadButton.SizeFlagsStretchRatio = 1.0f;

			LoadMoreButton.CustomMinimumSize = new Vector2(24, 30);
			LoadMoreButton.SizeFlagsHorizontal = Control.SizeFlags.ShrinkEnd;

			SaveButton.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
			SaveButton.SizeFlagsStretchRatio = 1.0f;

			SaveMoreButton.CustomMinimumSize = new Vector2(24, 30);
			SaveMoreButton.SizeFlagsHorizontal = Control.SizeFlags.ShrinkEnd;

			MapEditorStyle.SubContainer(targetFile.GetNodeOrNull<Control>("BoxFileOps"), "File Operations");
			MapEditorStyle.SubContainer(targetFile.GetNodeOrNull<Control>("BoxGenOps"), "Generation & Imports");
		}

		// Load and save are the wide pair with a narrow "more" button beside each, so they take the row look with
		// tightened side padding to leave room for the label inside half the column.
		StyleCompactRow(LoadButton);
		StyleCompactRow(SaveButton);
		StyleCompactRow(LoadMoreButton);
		StyleCompactRow(SaveMoreButton);

		MapEditorStyle.RowButton(SaveAsButton);
		MapEditorStyle.RowButton(PublishButton);
		MapEditorStyle.RowButton(ExportMapButton);
		MapEditorStyle.RowButton(ResetMapButton);
		MapEditorStyle.RowButton(EditorSettingsButton);
		if (EditorSettingsButton != null) EditorSettingsButton.Alignment = HorizontalAlignment.Center;
	}

	private static void StyleCompactRow(Button btn)
	{
		if (btn == null) return;

		MapEditorStyle.RowButton(btn);
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
}
