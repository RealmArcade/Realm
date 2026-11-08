using System;
using System.Collections.Generic;
using Godot;
using Realm.Client.Services;

using static Godot.Control;

namespace Realm.Client.UI.MapEditor;

/// <summary>
/// What the code-editor card needs from the editor: the node it parents the overlay to and takes its poll timer
/// from, the way back into the editor once an install finishes, and the editor's button styling so the dismiss
/// button keeps the icon font the rest of the editor uses.
/// </summary>
internal sealed record CodeEditorHooks(
	Node Owner,
	Action OpenEditor,
	Action<Button, string, Action> StyleButton);

/// <summary>
/// The code editor's availability as the editor reports it: the blocking "editor installing" overlay and the poll
/// that closes it and opens the editor when the background install lands, plus the two notices for an editor that
/// could not be installed or that opened with components missing.
/// </summary>
internal sealed class CodeEditorAvailability
{
	private readonly CodeEditorHooks _hooks;
	private Control? _installingCard;

	public CodeEditorAvailability(CodeEditorHooks hooks)
	{
		_hooks = hooks;
	}

	public void ShowInstallingCard()
	{
		if (_installingCard != null && GodotObject.IsInstanceValid(_installingCard)) return;

		var overlay = new ColorRect();
		overlay.Name = "EditorInstallingOverlay";
		overlay.Color = new Color(0, 0, 0, 0.75f);
		overlay.SetAnchorsPreset(LayoutPreset.FullRect);
		overlay.MouseFilter = MouseFilterEnum.Stop;
		overlay.ZIndex = 1100;
		_hooks.Owner.AddChild(overlay);
		_installingCard = overlay;

		var center = new CenterContainer();
		center.Name = "EditorInstallingCenter";
		center.SetAnchorsPreset(LayoutPreset.FullRect);
		overlay.AddChild(center);

		var panel = new PanelContainer();
		panel.Name = "EditorInstallingPanel";
		panel.AddThemeStyleboxOverride("panel", UIStyle.CreateStonePanel(true));
		panel.CustomMinimumSize = new Vector2(520, 200);
		center.AddChild(panel);

		var vbox = new VBoxContainer();
		vbox.Name = "EditorInstallingVBox";
		vbox.AddThemeConstantOverride("separation", 12);
		panel.AddChild(vbox);

		var titleLabel = new Label();
		titleLabel.Name = "EditorInstallingTitle";
		UIStyle.ApplyTitle(titleLabel, TranslationServer.Translate("⚠️ EDITOR INSTALLING"), 20);
		titleLabel.HorizontalAlignment = HorizontalAlignment.Center;
		vbox.AddChild(titleLabel);

		var messageLabel = new Label();
		messageLabel.Name = "EditorInstallingMessage";
		messageLabel.Text = TranslationServer.Translate("The Map Data Editor is currently installing in the background.\nPlease wait for it to complete.");
		messageLabel.HorizontalAlignment = HorizontalAlignment.Center;
		messageLabel.AutowrapMode = TextServer.AutowrapMode.Word;
		messageLabel.CustomMinimumSize = new Vector2(460, 0);
		messageLabel.SizeFlagsHorizontal = SizeFlags.ExpandFill;
		messageLabel.AddThemeColorOverride("font_color", new Color(0.9f, 0.9f, 0.95f));
		messageLabel.AddThemeFontSizeOverride("font_size", 13);
		vbox.AddChild(messageLabel);

		var dismissButton = new Button();
		dismissButton.Name = "EditorInstallingDismiss";
		dismissButton.AddThemeConstantOverride("icon_max_width", 0);
		_hooks.StyleButton(dismissButton, "OK", CloseInstallingCard);
		dismissButton.SizeFlagsHorizontal = SizeFlags.ShrinkCenter;
		vbox.AddChild(dismissButton);

		StartPolling();
	}

	/// <summary>Frees the overlay if it is still alive; safe to call whether or not the card was shown.</summary>
	public void CloseInstallingCard()
	{
		if (_installingCard != null && GodotObject.IsInstanceValid(_installingCard)) _installingCard.QueueFree();
		_installingCard = null;
	}

	private void StartPolling()
	{
		_hooks.Owner.GetTree().CreateTimer(0.5f).Timeout += OnPollTimeout;
	}

	private void OnPollTimeout()
	{
		// The editor can close while an install is in flight, which frees both the owner and the card; a poll that
		// fires after that must do nothing rather than reach into a dead node.
		if (!OperatingSystem.IsWindows() || !GodotObject.IsInstanceValid(_hooks.Owner)) return;
		if (_installingCard == null || !GodotObject.IsInstanceValid(_installingCard)) return;

		if (GameContext.VSCode.IsInstalled())
		{
			CloseInstallingCard();
			_hooks.OpenEditor();
			return;
		}

		if (GameContext.VSCode.IsInstalling)
		{
			StartPolling();
			return;
		}

		CloseInstallingCard();
		GameContext.UserInterface?.ShowPopupMessage(
			TranslationServer.Translate("EDITOR UNAVAILABLE"),
			TranslationServer.Translate("The code editor could not be installed. Check your internet connection and open the Map Editor again to retry the download.")
		);
	}

	/// <summary>
	/// Reports the editor's dependency state after it opens: nothing when it is complete, otherwise why it is
	/// missing and what still works without it.
	/// </summary>
	public void ReportDependencyState()
	{
		if (!OperatingSystem.IsWindows() || !GodotObject.IsInstanceValid(_hooks.Owner))
		{
			return;
		}

		if (!GameContext.VSCode.IsInstalled())
		{
			GameContext.UserInterface?.ShowPopupMessage(
				TranslationServer.Translate("EDITOR UNAVAILABLE"),
				TranslationServer.Translate("The code editor could not be installed. Check your internet connection and open the Map Editor again to retry the download.")
			);
			return;
		}

		IReadOnlyList<string> missingDependencies = GameContext.VSCode.GetMissingDependencies();
		if (missingDependencies.Count == 0)
		{
			return;
		}

		GameContext.UserInterface?.ShowPopupMessage(
			TranslationServer.Translate("EDITOR DEPENDENCIES INCOMPLETE"),
			string.Format(TranslationServer.Translate("The code editor opened, but these editor components could not be installed: {0}\n\nEditing and saving maps still work. Choose Reinstall Editor in Editor Settings to install them once the download works."), string.Join(", ", missingDependencies))
		);
	}
}
