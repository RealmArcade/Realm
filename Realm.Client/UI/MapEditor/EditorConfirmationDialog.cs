using System;
using Godot;
using Realm.Client.Utils;
using static Godot.Control;

namespace Realm.Client.UI.MapEditor;

/// <summary>
/// What the confirmation modal needs from the editor: the node it parents itself to and the button styling the rest of
/// the editor uses. The member keeps the wrapper's name because the localization generator only extracts keys from call
/// sites that name a registered translating wrapper.
/// </summary>
public sealed record EditorConfirmationDialogHooks(
	Node Owner,
	Action<Button, string, Action, int, string> SetupButton);

/// <summary>
/// The confirmation / notification modal: a card over the editor with a message and one or two answers. A dismissal
/// callback runs when the overlay leaves the tree, which covers both buttons and an Escape taken elsewhere, and the
/// cancel answer is published as metadata so that path can run the same action.
/// </summary>
public sealed class EditorConfirmationDialog
{
	private readonly EditorConfirmationDialogHooks _hooks;

	public EditorConfirmationDialog(EditorConfirmationDialogHooks hooks)
	{
		_hooks = hooks;
	}

	public void Show(string message, Action? onConfirm, string confirmText = "YES", string cancelText = "NO",
		Action? onCancel = null, Action? onDismissed = null, bool showCancel = true)
	{
		var overlay = CreateShell(onDismissed);

		var vbox = BuildPanel(overlay);

		BuildTitle(vbox, showCancel);

		BuildMessage(vbox, message);

		var hbox = new HBoxContainer();
		hbox.Name = "Hbox_HBoxContainer";
		hbox.AddThemeConstantOverride("separation", 24);
		hbox.SizeFlagsHorizontal = SizeFlags.ShrinkCenter;
		vbox.AddChild(hbox);

		BuildConfirmButton(hbox, overlay, onConfirm, confirmText);

		if (showCancel) BuildCancelButton(hbox, overlay, onCancel, cancelText);
	}

	private ColorRect CreateShell(Action? onDismissed)
	{
		var overlay = new ColorRect();
		overlay.Name = "ConfirmationOverlay";
		overlay.Color = new Color(0, 0, 0, 0.65f);
		overlay.SetAnchorsPreset(LayoutPreset.FullRect);
		overlay.MouseFilter = MouseFilterEnum.Stop;
		overlay.ZIndex = 1100;
		if (onDismissed != null)
		{
			overlay.TreeExited += () => onDismissed();
		}

		_hooks.Owner.AddChild(overlay);
		return overlay;
	}

	private static VBoxContainer BuildPanel(ColorRect overlay)
	{
		var center = UIStyle.CreateCenteredOverlay();
		overlay.AddChild(center);

		var panel = new PanelContainer();
		panel.Name = "Panel_PanelContainer";
		UIStyle.ClearPanelBackground(panel);
		panel.CustomMinimumSize = new Vector2(520, 266);
		center.AddChild(panel);

		var bgTex = new TextureRect();
		bgTex.Name = "BgTex_TextureRect";
		bgTex.Texture = TrimmedTextureLoader.LoadTrimmedTexture("res://Assets/UI/map_editor_test.webp");
		bgTex.ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize;
		bgTex.StretchMode = TextureRect.StretchModeEnum.Scale;
		bgTex.SetAnchorsPreset(LayoutPreset.FullRect);
		panel.AddChild(bgTex);

		var margin = new MarginContainer();
		margin.Name = "Margin_MarginContainer";
		margin.SetAnchorsPreset(LayoutPreset.FullRect);
		margin.AddThemeConstantOverride("margin_top", 30);
		margin.AddThemeConstantOverride("margin_bottom", 25);
		margin.AddThemeConstantOverride("margin_left", 45);
		margin.AddThemeConstantOverride("margin_right", 45);
		panel.AddChild(margin);

		var vbox = new VBoxContainer();
		vbox.Name = "Vbox_VBoxContainer";
		vbox.AddThemeConstantOverride("separation", 14);
		vbox.Alignment = BoxContainer.AlignmentMode.Center;
		margin.AddChild(vbox);
		return vbox;
	}

	private static void BuildTitle(VBoxContainer vbox, bool showCancel)
	{
		var titleMargin = new MarginContainer();
		titleMargin.Name = "TitleMargin_MarginContainer";
		titleMargin.AddThemeConstantOverride("margin_top", -12);

		var lblTitle = new Label();
		lblTitle.Name = "LblTitle_Label";
		UIStyle.ApplyTitle(lblTitle, TranslationServer.Translate(showCancel ? "CONFIRMATION REQUIRED" : "NOTIFICATION"), 18);
		lblTitle.HorizontalAlignment = HorizontalAlignment.Center;
		lblTitle.AddThemeColorOverride("font_color", UIStyle.ColorGold);
		titleMargin.AddChild(lblTitle);
		vbox.AddChild(titleMargin);
	}

	private static void BuildMessage(VBoxContainer vbox, string message)
	{
		var msgMargin = new MarginContainer();
		msgMargin.Name = "MsgMargin_MarginContainer";
		msgMargin.AddThemeConstantOverride("margin_top", 12);
		msgMargin.AddThemeConstantOverride("margin_bottom", 0);
		msgMargin.SizeFlagsVertical = SizeFlags.ExpandFill;

		var lblMsg = new Label();
		lblMsg.Name = "LblMsg_Label";
		lblMsg.Text = TranslationServer.Translate(message);
		lblMsg.AutowrapMode = TextServer.AutowrapMode.WordSmart;
		lblMsg.CustomMinimumSize = new Vector2(430, 0);
		lblMsg.SizeFlagsHorizontal = SizeFlags.ExpandFill;
		lblMsg.HorizontalAlignment = HorizontalAlignment.Center;
		lblMsg.AddThemeColorOverride("font_color", new Color(0.9f, 0.9f, 0.95f));
		lblMsg.AddThemeFontSizeOverride("font_size", 13);
		lblMsg.SizeFlagsVertical = SizeFlags.ExpandFill;
		msgMargin.AddChild(lblMsg);
		vbox.AddChild(msgMargin);
	}

	private void BuildConfirmButton(HBoxContainer hbox, ColorRect overlay, Action? onConfirm, string confirmText)
	{
		var btnConfirm = new Button();
		btnConfirm.Name = "BtnConfirm_Button";
		btnConfirm.Set("icon_max_width", 0);
		btnConfirm.CustomMinimumSize = new Vector2(110, 34);
		btnConfirm.MouseFilter = MouseFilterEnum.Stop;
		_hooks.SetupButton(btnConfirm, TranslationServer.Translate(confirmText), () =>
		{
			overlay.QueueFree();
			onConfirm?.Invoke();
		}, 13, "");
		btnConfirm.AddThemeColorOverride("font_color", UIStyle.ColorCyanGlow);
		hbox.AddChild(btnConfirm);
	}

	private void BuildCancelButton(HBoxContainer hbox, ColorRect overlay, Action? onCancel, string cancelText)
	{
		var btnCancel = UIStyle.CreateBareButton("BtnCancel_Button");
		btnCancel.CustomMinimumSize = new Vector2(110, 34);
		btnCancel.MouseFilter = MouseFilterEnum.Stop;
		Action cancelAction = () =>
		{
			overlay.QueueFree();
			onCancel?.Invoke();
		};
		overlay.SetMeta("CancelAction", Callable.From(cancelAction));
		_hooks.SetupButton(btnCancel, TranslationServer.Translate(cancelText), () => { cancelAction(); }, 13, "");
		btnCancel.AddThemeColorOverride("font_color", new Color(0.9f, 0.3f, 0.3f));
		hbox.AddChild(btnCancel);
	}
}
