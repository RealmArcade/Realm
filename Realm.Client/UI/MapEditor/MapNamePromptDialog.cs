using System;
using Godot;
using static Godot.Control;

namespace Realm.Client.UI.MapEditor;

/// <summary>
/// What the name prompt needs from the editor: the node it parents itself to, the button styling, and the feedback line
/// it complains through when the name is left empty. The member keeps the wrapper's name because the localization
/// generator only extracts keys from call sites that name a registered translating wrapper.
/// </summary>
internal sealed record MapNamePromptHooks(
	Node Owner,
	Action<Button, string, Action, int, string> SetupButton,
	Action<string> Feedback);

/// <summary>
/// The map-name prompt: the overlay, its title and explanation, the name field that takes focus on open, and the
/// name-it/cancel pair. It validates that a name was entered before handing it back; the caller decides what to do with it.
/// </summary>
internal sealed class MapNamePromptDialog
{
	private readonly MapNamePromptHooks _hooks;

	public MapNamePromptDialog(MapNamePromptHooks hooks)
	{
		_hooks = hooks;
	}

	public void Show(Action<string> onNamed)
	{
		var overlay = CreateShell();

		var vbox = BuildPanel(overlay);

		BuildPromptText(vbox);

		var nameInput = BuildInput(vbox);

		Action submit = () =>
		{
			string enteredName = nameInput.Text.Trim();
			if (string.IsNullOrEmpty(enteredName))
			{
				_hooks.Feedback(TranslationServer.Translate("Please enter a name for the map."));
				return;
			}

			overlay.QueueFree();
			onNamed?.Invoke(enteredName);
		};

		nameInput.TextSubmitted += _ => submit();

		BuildButtons(vbox, overlay, submit);

		// Deferred so the field takes focus after the overlay is in the tree, not before it can accept input.
		nameInput.CallDeferred(LineEdit.MethodName.GrabFocus);
	}

	private ColorRect CreateShell()
	{
		var overlay = new ColorRect();
		overlay.Name = "MapNamePromptOverlay";
		overlay.Color = new Color(0, 0, 0, 0.65f);
		overlay.SetAnchorsPreset(LayoutPreset.FullRect);
		overlay.MouseFilter = MouseFilterEnum.Stop;
		overlay.ZIndex = 1100;
		_hooks.Owner.AddChild(overlay);
		return overlay;
	}

	private static VBoxContainer BuildPanel(ColorRect overlay)
	{
		var center = new CenterContainer();
		center.Name = "MapNamePromptCenter_CenterContainer";
		center.SetAnchorsPreset(LayoutPreset.FullRect);
		overlay.AddChild(center);

		var panel = new PanelContainer();
		panel.Name = "MapNamePromptPanel_PanelContainer";
		panel.CustomMinimumSize = new Vector2(520, 260);
		UIStyle.ClearPanelBackground(panel);
		center.AddChild(panel);

		var margin = new MarginContainer();
		margin.Name = "MapNamePromptMargin_MarginContainer";
		margin.AddThemeConstantOverride("margin_top", 24);
		margin.AddThemeConstantOverride("margin_bottom", 22);
		margin.AddThemeConstantOverride("margin_left", 32);
		margin.AddThemeConstantOverride("margin_right", 32);
		panel.AddChild(margin);

		var vbox = new VBoxContainer();
		vbox.Name = "MapNamePromptVBox_VBoxContainer";
		vbox.AddThemeConstantOverride("separation", 14);
		margin.AddChild(vbox);
		return vbox;
	}

	private static void BuildPromptText(VBoxContainer vbox)
	{
		var title = new Label();
		title.Name = "MapNamePromptTitle_Label";
		UIStyle.ApplyTitle(title, TranslationServer.Translate("NAME THIS MAP"), 16);
		title.HorizontalAlignment = HorizontalAlignment.Center;
		title.AddThemeColorOverride("font_color", UIStyle.ColorGold);
		vbox.AddChild(title);

		var message = new Label();
		message.Name = "MapNamePromptMessage_Label";
		message.Text = TranslationServer.Translate("Map needs a valid name, rather than 'Untitled Map' before it can be saved.");
		message.AutowrapMode = TextServer.AutowrapMode.WordSmart;
		message.HorizontalAlignment = HorizontalAlignment.Center;
		message.CustomMinimumSize = new Vector2(440, 0);
		message.SizeFlagsHorizontal = SizeFlags.ExpandFill;
		message.AddThemeFontSizeOverride("font_size", 12);
		vbox.AddChild(message);
	}

	private static LineEdit BuildInput(VBoxContainer vbox)
	{
		var nameInput = new LineEdit();
		nameInput.Name = "MapNameInput_LineEdit";
		nameInput.PlaceholderText = TranslationServer.Translate("Map name");
		nameInput.CustomMinimumSize = new Vector2(0, 34);
		UIStyle.ApplyTextInput(nameInput);
		vbox.AddChild(nameInput);
		return nameInput;
	}

	private void BuildButtons(VBoxContainer vbox, ColorRect overlay, Action submit)
	{
		var buttons = new HBoxContainer();
		buttons.Name = "MapNamePromptButtons_HBoxContainer";
		buttons.AddThemeConstantOverride("separation", 24);
		buttons.SizeFlagsHorizontal = SizeFlags.ShrinkCenter;
		vbox.AddChild(buttons);

		var btnConfirm = new Button();
		btnConfirm.Name = "BtnConfirmMapName_Button";
		btnConfirm.Set("icon_max_width", 0);
		btnConfirm.CustomMinimumSize = new Vector2(110, 34);
		_hooks.SetupButton(btnConfirm, TranslationServer.Translate("NAME IT"), submit, 13, "");
		btnConfirm.AddThemeColorOverride("font_color", UIStyle.ColorCyanGlow);
		buttons.AddChild(btnConfirm);

		var btnCancel = new Button();
		btnCancel.Name = "BtnCancelMapName_Button";
		btnCancel.Set("icon_max_width", 0);
		btnCancel.CustomMinimumSize = new Vector2(110, 34);
		_hooks.SetupButton(btnCancel, TranslationServer.Translate("CANCEL"), () => overlay.QueueFree(), 13, "");
		btnCancel.AddThemeColorOverride("font_color", new Color(0.9f, 0.3f, 0.3f));
		buttons.AddChild(btnCancel);
	}
}
