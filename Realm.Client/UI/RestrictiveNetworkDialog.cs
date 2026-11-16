using System;
using Godot;
using Realm.Client.Services;
using Realm.Client.Utils;

namespace Realm.Client.UI;

/// <summary>
///     The one thing the game says when a network refused a lobby. It is a dialog rather than a line on whichever screen
///     noticed, because the player is asked to do the same three things wherever it appears: read the sentence, copy the
///     block of measurements a helper on the community Discord asks for, and leave the screen they were on when they are
///     ready. Every surface that reports a restriction opens this same dialog, so the sentence, the Discord door and the
///     copied block cannot drift apart.
/// </summary>
[GlobalClass]
public partial class RestrictiveNetworkDialog : Control
{
	public const string Message = "Your ISP has a restrictive firewall that cannot be bypassed. You may have trouble hosting or joining lobbies. Contact our discord channel for troubleshooting advice.";

	private const string CopyDiagnosticsText = "COPY NETWORK DIAGNOSTICS";
	private const string CopiedText = "Network diagnostics copied to clipboard";
	private const int CardWidth = 640;
	private const int CardInsetX = 28;
	private const int CardInsetY = 24;
	private const int StonePanelPadding = 20;
	private const int ContentWidth = CardWidth - 2 * (CardInsetX + StonePanelPadding);
	private const int ButtonHeight = 42;
	private const int DiscordIconWidth = 20;

	private static readonly Color TitleColor = new(0.9f, 0.3f, 0.3f);
	private static readonly Color BodyTextColor = new(0.88f, 0.85f, 0.78f);

	private Button _discordButton = null!;
	private Label _copiedFeedback = null!;
	private Action? _onClosed;

	public static string Text() => TranslationServer.Translate(Message);

	/// <summary>
	///     Adds the dialog to the screen that met the restriction and opens it. The callback runs once, when the player
	///     leaves the dialog, which is where a screen gives up the lobby it was in or returns to the list it came from.
	/// </summary>
	/// <param name="parent">The screen the dialog belongs to.</param>
	/// <param name="onClosed">What the screen does when the player closes the dialog, or null to stay on it.</param>
	/// <returns>The open dialog.</returns>
	public static RestrictiveNetworkDialog Show(Node parent, Action? onClosed = null)
	{
		if (parent.GetNodeOrNull<RestrictiveNetworkDialog>("RestrictiveNetworkDialog") is { } open)
		{
			open.Open(onClosed);
			return open;
		}

		var dialog = new RestrictiveNetworkDialog { Name = "RestrictiveNetworkDialog" };
		parent.AddChild(dialog);
		dialog.Open(onClosed);
		return dialog;
	}

	public override void _Ready()
	{
		Name = "RestrictiveNetworkDialog";
		SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
		MouseFilter = MouseFilterEnum.Stop;

		BuildLayout();
	}

	public void Open(Action? onClosed = null)
	{
		_onClosed = onClosed;
		Visible = true;
		_discordButton.GrabFocus();
	}

	private void BuildLayout()
	{
		var backdrop = new ColorRect { Name = "RestrictiveNetworkDialogBackdrop", Color = new Color(0.02f, 0.02f, 0.04f, 0.82f) };
		UIStyle.ConfigureModalBackdrop(backdrop);
		backdrop.GuiInput += OnBackdropInput;
		AddChild(backdrop);

		var center = new CenterContainer { Name = "RestrictiveNetworkDialogCenter" };
		UIStyle.ConfigureModalContentLayer(center);
		AddChild(center);

		var card = new PanelContainer { Name = "RestrictiveNetworkDialogCard" };
		card.CustomMinimumSize = new Vector2(CardWidth, 0);
		card.AddThemeStyleboxOverride("panel", UIStyle.CreateStonePanel(true));
		center.AddChild(card);

		var margin = new MarginContainer { Name = "RestrictiveNetworkDialogMargin" };
		margin.AddThemeConstantOverride("margin_left", CardInsetX);
		margin.AddThemeConstantOverride("margin_right", CardInsetX);
		margin.AddThemeConstantOverride("margin_top", CardInsetY);
		margin.AddThemeConstantOverride("margin_bottom", CardInsetY);
		card.AddChild(margin);

		var column = new VBoxContainer { Name = "RestrictiveNetworkDialogColumn" };
		column.AddThemeConstantOverride("separation", 14);
		margin.AddChild(column);

		var titleLabel = new Label { Name = "RestrictiveNetworkDialogTitle" };
		UIStyle.ApplyTitle(titleLabel, TranslationServer.Translate("CONNECTION FAILED"), 24);
		titleLabel.AddThemeColorOverride("font_color", TitleColor);
		titleLabel.HorizontalAlignment = HorizontalAlignment.Center;
		column.AddChild(titleLabel);

		var bodyLabel = new Label { Name = "RestrictiveNetworkDialogBody", Text = Text() };
		bodyLabel.AutowrapMode = TextServer.AutowrapMode.WordSmart;
		bodyLabel.CustomMinimumSize = new Vector2(ContentWidth, 0);
		bodyLabel.SizeFlagsHorizontal = SizeFlags.ExpandFill;
		bodyLabel.HorizontalAlignment = HorizontalAlignment.Center;
		bodyLabel.AddThemeColorOverride("font_color", BodyTextColor);
		bodyLabel.AddThemeFontSizeOverride("font_size", 14);
		column.AddChild(bodyLabel);

		_copiedFeedback = new Label { Name = "RestrictiveNetworkDialogCopiedFeedback", Text = TranslationServer.Translate(CopiedText), Visible = false };
		_copiedFeedback.AutowrapMode = TextServer.AutowrapMode.WordSmart;
		_copiedFeedback.CustomMinimumSize = new Vector2(ContentWidth, 0);
		_copiedFeedback.SizeFlagsHorizontal = SizeFlags.ExpandFill;
		_copiedFeedback.HorizontalAlignment = HorizontalAlignment.Center;
		_copiedFeedback.AddThemeColorOverride("font_color", UIStyle.ColorGoldDull);
		_copiedFeedback.AddThemeFontSizeOverride("font_size", 12);
		column.AddChild(_copiedFeedback);

		column.AddChild(BuildActions());

		var footer = new HBoxContainer { Name = "RestrictiveNetworkDialogFooter" };
		footer.Alignment = BoxContainer.AlignmentMode.End;
		column.AddChild(footer);

		var closeButton = UIStyle.CreateActionButton("RestrictiveNetworkDialogCloseButton", TranslationServer.Translate("CLOSE"), 14);
		closeButton.CustomMinimumSize = new Vector2(140, 34);
		closeButton.FocusMode = FocusModeEnum.None;
		closeButton.Pressed += Close;
		closeButton.MouseEntered += () => GameContext.UserInterface?.PlayHoverSound();
		footer.AddChild(closeButton);
	}

	private HBoxContainer BuildActions()
	{
		var actions = new HBoxContainer { Name = "RestrictiveNetworkDialogActions" };
		actions.AddThemeConstantOverride("separation", 12);

		_discordButton = UIStyle.CreateActionButton("RestrictiveNetworkDialogDiscordButton", TranslationServer.Translate("DISCORD"), 14);
		_discordButton.CustomMinimumSize = new Vector2(0, ButtonHeight);
		_discordButton.SizeFlagsHorizontal = SizeFlags.ExpandFill;
		_discordButton.AddThemeConstantOverride("icon_max_width", DiscordIconWidth);
		_discordButton.Icon = TrimmedTextureLoader.LoadTrimmedTexture("res://Assets/UI/discord_icon.webp");
		_discordButton.Pressed += OpenDiscordDialog;
		_discordButton.MouseEntered += () => GameContext.UserInterface?.PlayHoverSound();
		actions.AddChild(_discordButton);

		var copyButton = UIStyle.CreateActionButton("RestrictiveNetworkDialogCopyButton", TranslationServer.Translate(CopyDiagnosticsText), 14);
		copyButton.CustomMinimumSize = new Vector2(0, ButtonHeight);
		copyButton.SizeFlagsHorizontal = SizeFlags.ExpandFill;
		copyButton.FocusMode = FocusModeEnum.None;
		copyButton.Disabled = GameContext.Lobby == null;
		copyButton.Pressed += CopyNetworkDiagnostics;
		copyButton.MouseEntered += () => GameContext.UserInterface?.PlayHoverSound();
		actions.AddChild(copyButton);

		return actions;
	}

	private void OpenDiscordDialog()
	{
		GameContext.UserInterface?.PlayClickSound();

		if (GetParent() is not Node parent) return;

		var communityDialog = new DiscordCommunityDialog { Name = "DiscordCommunityDialog" };
		parent.AddChild(communityDialog);
		communityDialog.Open();
	}

	private void CopyNetworkDiagnostics()
	{
		GameContext.UserInterface?.PlayClickSound();

		DisplayServer.ClipboardSet(GameContext.Lobby?.DescribeNetworkDiagnostics() ?? string.Empty);
		_copiedFeedback.Visible = true;
	}

	private void OnBackdropInput(InputEvent inputEvent)
	{
		if (inputEvent is InputEventMouseButton mouseButton && mouseButton.Pressed && mouseButton.ButtonIndex == MouseButton.Left)
		{
			Close();
		}
	}

	private void Close()
	{
		GameContext.UserInterface?.PlayClickSound();

		Action? onClosed = _onClosed;
		_onClosed = null;
		QueueFree();
		onClosed?.Invoke();
	}
}
