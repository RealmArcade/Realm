using System;
using Godot;
using Realm.Client.Services;
using Realm.Client.Utils;

namespace Realm.Client.UI;

/// <summary>
///     The one thing the game says when a network refused a lobby. It is a dialog rather than a line on whichever screen
///     noticed, because the player is asked to do the same three things wherever it appears: read the sentence, copy the
///     block of measurements a helper on the community Discord asks for, and leave the screen they were on when they are
///     ready. Every surface that reports a restriction opens this same dialog, so the sentence the player reads, the Discord
///     door and the copied block cannot drift apart.
/// </summary>
/// <remarks>
///     It belongs to the interface root rather than to the screen that reported the failure: a screen adds its own panels
///     while it is being built, and a dialog added in the middle of that can be drawn over by the panels that follow it,
///     which reads as a barrier the player cannot click through. The interface root is where every other popup in the game
///     lives, and the dialog is added there through a deferred call so a surface reporting a failure from its own
///     <c>_Ready</c> is never asking a parent that is still setting up its children to take another one.
/// </remarks>
[GlobalClass]
public partial class RestrictiveNetworkDialog : Control
{
	public const string Message = "Your ISP has a restrictive firewall that cannot be bypassed. You may have trouble hosting or joining lobbies. Contact our discord channel for troubleshooting advice.";

	private const string CopyDiagnosticsText = "COPY NETWORK DIAGNOSTICS";
	private const string CopiedText = "Network diagnostics copied to clipboard";
	private const int CardWidth = 620;
	private const int CardInsetX = 26;
	private const int CardInsetY = 22;
	private const int StonePanelPadding = 20;
	private const int ContentWidth = CardWidth - 2 * (CardInsetX + StonePanelPadding);
	private const int ButtonHeight = 40;
	private const int DiscordIconWidth = 20;

	private static readonly Color TitleColor = new(0.9f, 0.3f, 0.3f);
	private static readonly Color BodyTextColor = new(0.88f, 0.85f, 0.78f);

	private static bool _openPending;

	private Button _discordButton = null!;
	private Label _copiedFeedback = null!;
	private Action? _onClosed;

	public static string Text() => TranslationServer.Translate(Message);

	/// <summary>
	///     Opens the dialog above whatever the player is looking at. A dialog that is already open is reused rather than
	///     stacked on top of itself, so a second failure arriving while the first is being read does not bury it.
	/// </summary>
	/// <param name="onClosed">What the surface that reported the failure does once the player closes the dialog.</param>
	public static void Show(Action? onClosed = null)
	{
		if (GameContext.UserInterface is not { } userInterface) return;

		if (userInterface.GetNodeOrNull<RestrictiveNetworkDialog>("RestrictiveNetworkDialog") is { } open)
		{
			open.Open(onClosed);
			return;
		}

		if (_openPending) return;
		_openPending = true;

		var dialog = new RestrictiveNetworkDialog { Name = "RestrictiveNetworkDialog" };
		dialog._onClosed = onClosed;
		userInterface.CallDeferred(Node.MethodName.AddChild, dialog);
	}

	public override void _Ready()
	{
		_openPending = false;
		Name = "RestrictiveNetworkDialog";
		SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
		MouseFilter = MouseFilterEnum.Stop;

		BuildLayout();
		Visible = true;
	}

	public override void _ExitTree()
	{
		_openPending = false;
		base._ExitTree();
	}

	public void Open(Action? onClosed = null)
	{
		_onClosed = onClosed;
		Visible = true;
	}

	private void BuildLayout()
	{
		var backdrop = new ColorRect { Name = "RestrictiveNetworkDialogBackdrop", Color = new Color(0.02f, 0.02f, 0.04f, 0.82f) };
		backdrop.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
		backdrop.MouseFilter = MouseFilterEnum.Stop;
		AddChild(backdrop);

		var center = UIStyle.CreateCenteredOverlay();
		center.Name = "RestrictiveNetworkDialogCenter";
		AddChild(center);

		var card = new PanelContainer { Name = "RestrictiveNetworkDialogCard" };
		card.CustomMinimumSize = new Vector2(CardWidth, 0);
		card.SizeFlagsHorizontal = SizeFlags.ShrinkCenter;
		card.SizeFlagsVertical = SizeFlags.ShrinkCenter;
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

		column.AddChild(BuildActions());

		_copiedFeedback = new Label { Name = "RestrictiveNetworkDialogCopiedFeedback", Text = TranslationServer.Translate(CopiedText), Visible = false };
		_copiedFeedback.AutowrapMode = TextServer.AutowrapMode.WordSmart;
		_copiedFeedback.CustomMinimumSize = new Vector2(ContentWidth, 0);
		_copiedFeedback.SizeFlagsHorizontal = SizeFlags.ExpandFill;
		_copiedFeedback.HorizontalAlignment = HorizontalAlignment.Center;
		_copiedFeedback.AddThemeColorOverride("font_color", UIStyle.ColorGoldDull);
		_copiedFeedback.AddThemeFontSizeOverride("font_size", 12);
		column.AddChild(_copiedFeedback);

		var footer = new HBoxContainer { Name = "RestrictiveNetworkDialogFooter" };
		footer.Alignment = BoxContainer.AlignmentMode.Center;
		column.AddChild(footer);

		var closeButton = UIStyle.CreateActionButton("RestrictiveNetworkDialogCloseButton", TranslationServer.Translate("CLOSE"), 14);
		closeButton.CustomMinimumSize = new Vector2(150, ButtonHeight);
		closeButton.FocusMode = FocusModeEnum.None;
		closeButton.Pressed += Close;
		closeButton.MouseEntered += () => GameContext.UserInterface?.PlayHoverSound();
		footer.AddChild(closeButton);
	}

	private HFlowContainer BuildActions()
	{
		var actions = new HFlowContainer { Name = "RestrictiveNetworkDialogActions" };
		actions.Alignment = FlowContainer.AlignmentMode.Center;
		actions.AddThemeConstantOverride("h_separation", 12);
		actions.AddThemeConstantOverride("v_separation", 8);

		_discordButton = UIStyle.CreateActionButton("RestrictiveNetworkDialogDiscordButton", TranslationServer.Translate("DISCORD"), 14);
		_discordButton.CustomMinimumSize = new Vector2(200, ButtonHeight);
		_discordButton.AddThemeConstantOverride("icon_max_width", DiscordIconWidth);
		_discordButton.Icon = TrimmedTextureLoader.LoadTrimmedTexture("res://Assets/UI/discord_icon.webp");
		_discordButton.Pressed += OpenDiscordDialog;
		_discordButton.MouseEntered += () => GameContext.UserInterface?.PlayHoverSound();
		actions.AddChild(_discordButton);

		var copyButton = UIStyle.CreateActionButton("RestrictiveNetworkDialogCopyButton", TranslationServer.Translate(CopyDiagnosticsText), 14);
		copyButton.CustomMinimumSize = new Vector2(250, ButtonHeight);
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

		if (GameContext.UserInterface is not { } userInterface) return;

		var communityDialog = new DiscordCommunityDialog { Name = "DiscordCommunityDialog" };
		userInterface.AddChild(communityDialog);
		userInterface.MoveChild(communityDialog, userInterface.GetChildCount() - 1);
		communityDialog.Open();
	}

	private void CopyNetworkDiagnostics()
	{
		GameContext.UserInterface?.PlayClickSound();

		DisplayServer.ClipboardSet(GameContext.Lobby?.DescribeNetworkDiagnostics() ?? string.Empty);
		_copiedFeedback.Visible = true;
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
