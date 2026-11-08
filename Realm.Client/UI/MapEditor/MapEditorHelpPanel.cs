using System;
using Godot;
using Realm.Client.Utils;
using static Godot.Control;

namespace Realm.Client.UI.MapEditor;

/// <summary>
/// What the manual needs from the editor: the node it parents itself to, and the button styling. The member keeps the
/// wrapper's name because the localization generator only extracts keys from call sites naming a registered wrapper.
/// </summary>
internal sealed record HelpPanelHooks(
	Node Owner,
	Action<Button, string, Action, int> SetupButton);

/// <summary>
/// The editor's reference manual: the overlay, the manual artwork behind it, the hotkey grid grouped by section, and the
/// close button. It owns its overlay so a second toggle closes it, and reports whether it was open so the Escape path can
/// use the same close.
/// </summary>
internal sealed class MapEditorHelpPanel
{
	private readonly HelpPanelHooks _hooks;
	private ColorRect? _panel;

	public MapEditorHelpPanel(HelpPanelHooks hooks)
	{
		_hooks = hooks;
	}

	public ColorRect? Panel => _panel;

	/// <summary>Closes the manual if it is open, and reports whether it was.</summary>
	public bool Close()
	{
		if (_panel == null) return false;
		if (GodotObject.IsInstanceValid(_panel)) _panel.QueueFree();
		_panel = null;
		return true;
	}

	public void Toggle()
	{
		if (Close()) return;

		_panel = new ColorRect();
		_panel.Name = "HelpOverlayPanel";
		_panel.Color = new Color(0, 0, 0, 0.65f);
		_panel.SetAnchorsPreset(LayoutPreset.FullRect);
		_panel.MouseFilter = MouseFilterEnum.Stop;
		_panel.ZIndex = 1000;
		_hooks.Owner.AddChild(_panel);

		var center = UIStyle.CreateCenteredOverlay();
		_panel.AddChild(center);

		var panel = new PanelContainer();
		panel.Name = "Panel_PanelContainer";
		panel.AddThemeStyleboxOverride("panel", new StyleBoxEmpty());
		panel.CustomMinimumSize = new Vector2(1024, 770);
		center.AddChild(panel);

		var bgTex = new TextureRect();
		bgTex.Name = "BgTex_TextureRect";
		bgTex.Texture = TrimmedTextureLoader.LoadTrimmedTexture("res://Assets/UI/map_editor_manual.webp");
		bgTex.ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize;
		bgTex.StretchMode = TextureRect.StretchModeEnum.Scale;
		bgTex.SetAnchorsPreset(LayoutPreset.FullRect);
		panel.AddChild(bgTex);

		var margin = new MarginContainer();
		margin.Name = "Margin_MarginContainer";
		margin.SetAnchorsPreset(LayoutPreset.FullRect);
		margin.AddThemeConstantOverride("margin_top", 45);
		margin.AddThemeConstantOverride("margin_bottom", 75);
		margin.AddThemeConstantOverride("margin_left", 65);
		margin.AddThemeConstantOverride("margin_right", 65);
		panel.AddChild(margin);

		var vbox = new VBoxContainer();
		vbox.Name = "Vbox_VBoxContainer";
		vbox.AddThemeConstantOverride("separation", 15);
		margin.AddChild(vbox);

		var titleMargin = new MarginContainer();
		titleMargin.Name = "TitleMargin_MarginContainer";
		titleMargin.AddThemeConstantOverride("margin_top", -20);
		titleMargin.AddThemeConstantOverride("margin_bottom", 0);

		var lblTitle = new Label();
		lblTitle.Name = "LblTitle_Label";
		UIStyle.ApplyTitle(lblTitle, TranslationServer.Translate("RTS MAP EDITOR REFERENCE MANUAL"), 18);
		lblTitle.HorizontalAlignment = HorizontalAlignment.Center;
		lblTitle.AddThemeColorOverride("font_color", UIStyle.ColorGold);
		titleMargin.AddChild(lblTitle);
		vbox.AddChild(titleMargin);

		BuildHelpHotkeyGrid(vbox);

		var bottomMargin = new MarginContainer();
		bottomMargin.Name = "BottomMargin_MarginContainer";
		bottomMargin.SetAnchorsPreset(LayoutPreset.FullRect);
		bottomMargin.AddThemeConstantOverride("margin_bottom", 20);
		bottomMargin.AddThemeConstantOverride("margin_right", 16);
		panel.AddChild(bottomMargin);

		var btnClose = new Button();
		btnClose.Name = "BtnClose_Button";
		btnClose.Set("icon_max_width", 0);
		btnClose.SizeFlagsHorizontal = SizeFlags.ShrinkCenter;
		btnClose.SizeFlagsVertical = SizeFlags.ShrinkEnd;
		btnClose.CustomMinimumSize = new Vector2(165, 38);
		_hooks.SetupButton(btnClose, TranslationServer.Translate("CLOSE MANUAL"), () =>
		{
			Close();
		}, 13);
		bottomMargin.AddChild(btnClose);
	}


	private void BuildHelpHotkeyGrid(VBoxContainer vbox)
	{
		var scroll = UIStyle.CreateScrollingBody();
		vbox.AddChild(scroll);

		var grid = UIStyle.CreateGrid(2);
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
		AddHelpShortcutRow(grid, "0", TranslationServer.Translate("Water Tool"));
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
	}


	private void AddHelpSectionHeader(GridContainer grid, string title)
	{
		var lbl = new Label();
		lbl.Name = "Lbl_Label";
		lbl.Text = title;
		lbl.AddThemeColorOverride("font_color", UIStyle.ColorBronze);
		lbl.AddThemeFontSizeOverride("font_size", 12);
		grid.AddChild(lbl);

		var empty = new Control();
		empty.Name = "Empty_Control";
		grid.AddChild(empty);
	}

	private void AddHelpShortcutRow(GridContainer grid, string keys, string action)
	{
		var lblKeys = new Label();
		lblKeys.Name = "LblKeys_Label";
		lblKeys.Text = keys;
		lblKeys.AddThemeColorOverride("font_color", UIStyle.ColorCyanGlow);
		lblKeys.AddThemeFontSizeOverride("font_size", 11);
		grid.AddChild(lblKeys);

		var lblAction = new Label();
		lblAction.Name = "LblAction_Label";
		lblAction.Text = action;
		lblAction.AddThemeColorOverride("font_color", new Color(0.85f, 0.85f, 0.9f));
		lblAction.AddThemeFontSizeOverride("font_size", 11);
		grid.AddChild(lblAction);
	}

}
