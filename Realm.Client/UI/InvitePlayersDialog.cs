using System;
using System.Collections.Generic;
using Godot;
using Realm.Client.Network;
using Realm.Client.Services;

namespace Realm.Client.UI;

/// <summary>
///     The one window a lobby offers for pulling players in. It holds both ways of doing that in the same place: the Steam
///     friend list, which invites a friend with one click, and the addresses this lobby is played at, which a player joins
///     from whether or not the lobby is listed. A host does not have to know which kind of player they are inviting before
///     opening it, and a screen whose Steam client is not running says so here instead of hiding the half that still works.
/// </summary>
[GlobalClass]
public partial class InvitePlayersDialog : Control
{
	private const float PanelWidth = 760.0f;
	private const float PanelInset = 24.0f;
	private const float ContentWidth = PanelWidth - 2.0f * (PanelInset + 20.0f);
	private const float ScrollHeight = 620.0f;
	private const float StatusColumnWidth = 150.0f;
	private const int SectionFontSize = 15;

	private static readonly Color BodyColor = new(0.78f, 0.8f, 0.86f);
	private static readonly Color WarningColor = new(0.92f, 0.35f, 0.3f);
	private static readonly Color FeedbackColor = new(0.3f, 0.8f, 1.0f);

	private readonly HashSet<ulong> _selectedSteamIds = new HashSet<ulong>();

	private Label _steamWarningLabel = null!;
	private Label _steamExplanationLabel = null!;
	private Label _subtitleLabel = null!;
	private VBoxContainer _friendList = null!;
	private Label _emptyFriendsLabel = null!;
	private Label _setupLabel = null!;
	private Label _feedbackLabel = null!;
	private Button _sendButton = null!;
	private Label _addressDescription = null!;
	private VBoxContainer _addressColumn = null!;
	private bool _built;
	private bool _isHost;
	private string _gameLobbyId = string.Empty;
	private int _maximumMembers = 2;

	public event Action<int>? InvitesSent;

	public override void _Ready()
	{
		EnsureBuilt();
	}

	/// <summary>
	///     Fills the window for the lobby it was opened from. The Steam half is offered whatever the lobby's listing says,
	///     because the friend list is Steam's and a lobby that cannot be listed still has players who can be invited to it by
	///     address; the Steam half reports its own failure inside the window when it cannot send.
	/// </summary>
	/// <param name="isHost">Whether this screen hosts the lobby, which decides which addresses are stated.</param>
	/// <param name="gameLobbyId">The lobby id Steam invitations name, or an empty string when nothing listed the lobby.</param>
	/// <param name="maximumMembers">How many players the lobby holds, which bounds the Steam lobby Steam creates.</param>
	public void ShowFor(bool isHost, string gameLobbyId, int maximumMembers)
	{
		EnsureBuilt();

		_isHost = isHost;
		_gameLobbyId = gameLobbyId;
		_maximumMembers = maximumMembers;
		_selectedSteamIds.Clear();
		_feedbackLabel.Text = string.Empty;
		_setupLabel.Text = string.Empty;
		Visible = true;

		if (GetParent() is { } parent)
		{
			parent.MoveChild(this, parent.GetChildCount() - 1);
		}

		RebuildAddresses();
		ApplySteamAvailability();
	}

	public void Close()
	{
		Visible = false;
	}

	private void ApplySteamAvailability()
	{
		bool steamAvailable = GameContext.Steam.IsAvailable;

		_steamWarningLabel.Visible = !steamAvailable;
		_steamExplanationLabel.Visible = !steamAvailable;
		_steamExplanationLabel.Text = steamAvailable ? string.Empty : GameContext.Steam.UnavailableExplanation;
		_subtitleLabel.Visible = steamAvailable;
		_friendList.Visible = steamAvailable;
		_sendButton.Visible = steamAvailable;

		if (!steamAvailable)
		{
			_emptyFriendsLabel.Visible = false;
			return;
		}

		if (PopulateFriends() == 0)
		{
			RefreshFriendsOnceNamesArriveAsync();
		}

		PrepareInviteLobbyAsync();
	}

	private async void RefreshFriendsOnceNamesArriveAsync()
	{
		try
		{
			await ToSignal(GetTree().CreateTimer(1.5f), SceneTreeTimer.SignalName.Timeout);
			if (!GodotObject.IsInstanceValid(this) || !Visible) return;

			PopulateFriends();
		}
		catch (Exception exception)
		{
			GameLog.LogError($"[InvitePlayersDialog] Refreshing the friend list failed: {exception}");
		}
	}

	private void EnsureBuilt()
	{
		if (_built) return;
		_built = true;

		UIStyle.ConfigurePopoverRoot(this, "InvitePlayersDialog");

		var dismissOverlay = UIStyle.CreateDismissOverlay(0.55f);
		dismissOverlay.GuiInput += OnDismissOverlayInput;
		AddChild(dismissOverlay);

		var center = new CenterContainer { Name = "InvitePlayersCenter" };
		center.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
		AddChild(center);

		var panel = new PanelContainer { Name = "InvitePlayersPanel" };
		panel.AddThemeStyleboxOverride("panel", UIStyle.CreateStonePanel(true));
		panel.CustomMinimumSize = new Vector2(PanelWidth, 0);
		center.AddChild(panel);

		var margin = new MarginContainer { Name = "InvitePlayersMargin" };
		margin.AddThemeConstantOverride("margin_left", (int)PanelInset);
		margin.AddThemeConstantOverride("margin_right", (int)PanelInset);
		margin.AddThemeConstantOverride("margin_top", (int)PanelInset);
		margin.AddThemeConstantOverride("margin_bottom", (int)PanelInset);
		panel.AddChild(margin);

		var column = new VBoxContainer { Name = "InvitePlayersColumn" };
		column.AddThemeConstantOverride("separation", 12);
		margin.AddChild(column);

		var titleLabel = new Label { Name = "InvitePlayersTitle" };
		UIStyle.ApplyTitle(titleLabel, TranslationServer.Translate("INVITE PLAYERS"), 24);
		titleLabel.AddThemeColorOverride("font_color", UIStyle.ColorGold);
		titleLabel.HorizontalAlignment = HorizontalAlignment.Center;
		column.AddChild(titleLabel);

		var scroll = new ScrollContainer { Name = "InvitePlayersScroll" };
		scroll.CustomMinimumSize = new Vector2(ContentWidth, ScrollHeight);
		scroll.SizeFlagsHorizontal = SizeFlags.ExpandFill;
		scroll.SizeFlagsVertical = SizeFlags.ExpandFill;
		scroll.HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled;
		column.AddChild(scroll);

		var content = new VBoxContainer { Name = "InvitePlayersContent" };
		content.SizeFlagsHorizontal = SizeFlags.ExpandFill;
		content.AddThemeConstantOverride("separation", 10);
		scroll.AddChild(content);

		content.AddChild(CreateSectionHeader("InvitePlayersSteamHeader", "STEAM FRIENDS"));

		_steamWarningLabel = CreateBodyLabel("InvitePlayersSteamWarning", WarningColor);
		_steamWarningLabel.Text = TranslationServer.Translate("Steam is not running. Open the Steam client, sign in, and restart Realm to invite your Steam friends.");
		_steamWarningLabel.Visible = false;
		content.AddChild(_steamWarningLabel);

		_steamExplanationLabel = CreateBodyLabel("InvitePlayersSteamExplanation", UIStyle.ColorGoldDull);
		_steamExplanationLabel.AddThemeFontSizeOverride("font_size", 12);
		_steamExplanationLabel.Visible = false;
		content.AddChild(_steamExplanationLabel);

		_subtitleLabel = CreateBodyLabel("InvitePlayersSteamSubtitle", BodyColor);
		_subtitleLabel.Text = TranslationServer.Translate("Choose the friends to invite. They receive the invitation from Steam and can join this lobby with one click.");
		_subtitleLabel.AddThemeFontSizeOverride("font_size", 13);
		content.AddChild(_subtitleLabel);

		_setupLabel = CreateBodyLabel("InvitePlayersSteamSetup", UIStyle.ColorGoldDull);
		_setupLabel.AddThemeFontSizeOverride("font_size", 12);
		content.AddChild(_setupLabel);

		_friendList = new VBoxContainer { Name = "InvitePlayersFriendList" };
		_friendList.SizeFlagsHorizontal = SizeFlags.ExpandFill;
		_friendList.AddThemeConstantOverride("separation", 6);
		content.AddChild(_friendList);

		_emptyFriendsLabel = CreateBodyLabel("InvitePlayersEmptyFriends", new Color(0.7f, 0.72f, 0.78f));
		_emptyFriendsLabel.Text = TranslationServer.Translate("No Steam friends were found on this account.");
		_emptyFriendsLabel.AddThemeFontSizeOverride("font_size", 13);
		_emptyFriendsLabel.Visible = false;
		content.AddChild(_emptyFriendsLabel);

		_sendButton = UIStyle.CreateActionButton("InvitePlayersSendButton", TranslationServer.Translate("SEND INVITES"), 14);
		_sendButton.CustomMinimumSize = new Vector2(220, 44);
		_sendButton.SizeFlagsHorizontal = SizeFlags.ShrinkCenter;
		_sendButton.FocusMode = FocusModeEnum.None;
		_sendButton.Pressed += OnSendInvitesPressed;
		_sendButton.MouseEntered += () => GameContext.UserInterface?.PlayHoverSound();
		content.AddChild(_sendButton);

		var divider = new HSeparator { Name = "InvitePlayersDivider" };
		divider.AddThemeStyleboxOverride("separator", UIStyle.CreateHairlineSeparator());
		content.AddChild(divider);

		content.AddChild(CreateSectionHeader("InvitePlayersAddressHeader", "DIRECT CONNECT"));

		_addressDescription = CreateBodyLabel("InvitePlayersAddressDescription", BodyColor);
		_addressDescription.AddThemeFontSizeOverride("font_size", 13);
		content.AddChild(_addressDescription);

		_addressColumn = new VBoxContainer { Name = "InvitePlayersAddressColumn" };
		_addressColumn.SizeFlagsHorizontal = SizeFlags.ExpandFill;
		_addressColumn.AddThemeConstantOverride("separation", 8);
		content.AddChild(_addressColumn);

		_feedbackLabel = CreateBodyLabel("InvitePlayersFeedback", FeedbackColor);
		_feedbackLabel.AddThemeFontSizeOverride("font_size", 13);
		_feedbackLabel.CustomMinimumSize = new Vector2(ContentWidth, 22);
		column.AddChild(_feedbackLabel);

		var closeButton = UIStyle.CreateActionButton("InvitePlayersCloseButton", TranslationServer.Translate("CLOSE"), 14);
		closeButton.CustomMinimumSize = new Vector2(170, 40);
		closeButton.SizeFlagsHorizontal = SizeFlags.ShrinkCenter;
		closeButton.FocusMode = FocusModeEnum.None;
		closeButton.Pressed += Close;
		closeButton.MouseEntered += () => GameContext.UserInterface?.PlayHoverSound();
		column.AddChild(closeButton);
	}

	private static Label CreateSectionHeader(string name, string key)
	{
		var label = new Label { Name = name, Text = TranslationServer.Translate(key) };
		label.AutowrapMode = TextServer.AutowrapMode.WordSmart;
		label.CustomMinimumSize = new Vector2(ContentWidth, 0);
		label.SizeFlagsHorizontal = SizeFlags.ExpandFill;
		label.HorizontalAlignment = HorizontalAlignment.Center;
		label.AddThemeColorOverride("font_color", UIStyle.ColorGold);
		label.AddThemeFontSizeOverride("font_size", SectionFontSize);
		return label;
	}

	private static Label CreateBodyLabel(string name, Color color)
	{
		var label = new Label { Name = name };
		label.AutowrapMode = TextServer.AutowrapMode.WordSmart;
		label.CustomMinimumSize = new Vector2(ContentWidth, 0);
		label.SizeFlagsHorizontal = SizeFlags.ExpandFill;
		label.HorizontalAlignment = HorizontalAlignment.Center;
		label.AddThemeColorOverride("font_color", color);
		label.AddThemeFontSizeOverride("font_size", 14);
		return label;
	}

	private int PopulateFriends()
	{
		foreach (Node child in _friendList.GetChildren())
		{
			_friendList.RemoveChild(child);
			child.QueueFree();
		}

		IReadOnlyList<SteamFriend> friends = GameContext.Steam.GetFriends();
		_emptyFriendsLabel.Visible = friends.Count == 0;

		foreach (SteamFriend friend in friends)
		{
			_friendList.AddChild(CreateFriendRow(friend));
		}

		return friends.Count;
	}

	private Control CreateFriendRow(SteamFriend friend)
	{
		var row = new HBoxContainer { Name = "InvitePlayersFriendRow" };
		row.SizeFlagsHorizontal = SizeFlags.ExpandFill;
		row.AddThemeConstantOverride("separation", 12);

		var checkBox = new CheckBox { Name = "InvitePlayersFriendCheck" };
		checkBox.Text = friend.PersonaName;
		checkBox.CustomMinimumSize = new Vector2(ContentWidth - StatusColumnWidth - 40, 34);
		checkBox.SizeFlagsHorizontal = SizeFlags.ExpandFill;
		checkBox.FocusMode = FocusModeEnum.None;
		checkBox.ClipText = true;
		checkBox.AddThemeFontSizeOverride("font_size", 14);
		checkBox.ButtonPressed = _selectedSteamIds.Contains(friend.SteamId);
		UIStyle.ApplyCheckboxStyle(checkBox);
		checkBox.Toggled += pressed => OnFriendToggled(friend.SteamId, pressed);
		checkBox.MouseEntered += () => GameContext.UserInterface?.PlayHoverSound();
		row.AddChild(checkBox);

		var statusLabel = new Label { Name = "InvitePlayersFriendStatus" };
		statusLabel.Text = DescribeFriend(friend);
		statusLabel.CustomMinimumSize = new Vector2(StatusColumnWidth, 0);
		statusLabel.SizeFlagsHorizontal = SizeFlags.ShrinkEnd;
		statusLabel.HorizontalAlignment = HorizontalAlignment.Right;
		statusLabel.VerticalAlignment = VerticalAlignment.Center;
		statusLabel.AddThemeFontSizeOverride("font_size", 12);
		statusLabel.AddThemeColorOverride("font_color", DescribeFriendColor(friend));
		row.AddChild(statusLabel);

		return row;
	}

	private static string DescribeFriend(SteamFriend friend)
	{
		if (friend.IsPlayingRealm) return TranslationServer.Translate("In Realm");
		return friend.IsOnline ? TranslationServer.Translate("Online") : TranslationServer.Translate("Offline");
	}

	private static Color DescribeFriendColor(SteamFriend friend)
	{
		if (friend.IsPlayingRealm) return new Color(0.4f, 0.85f, 0.45f);
		return friend.IsOnline ? new Color(0.62f, 0.78f, 0.92f) : new Color(0.55f, 0.55f, 0.6f);
	}

	private void OnFriendToggled(ulong steamId, bool pressed)
	{
		if (pressed)
		{
			_selectedSteamIds.Add(steamId);
		}
		else
		{
			_selectedSteamIds.Remove(steamId);
		}

		_feedbackLabel.Text = string.Empty;
	}

	private async void PrepareInviteLobbyAsync()
	{
		try
		{
			_setupLabel.Text = TranslationServer.Translate("Preparing the invite...");
			_sendButton.Disabled = true;

			bool prepared = await GameContext.Steam.OpenInviteLobbyAsync(_gameLobbyId, _maximumMembers);
			if (!GodotObject.IsInstanceValid(this)) return;

			_setupLabel.Text = prepared ? string.Empty : TranslationServer.Translate("Failed to connect to Steam. Restart Steam client and Game and try again. Otherwise, send IP address directly to friends so they can join.");
			_sendButton.Disabled = !prepared;
		}
		catch (Exception exception)
		{
			GameLog.LogError($"[InvitePlayersDialog] Preparing the invite lobby failed: {exception}");
		}
	}

	private void OnSendInvitesPressed()
	{
		GameContext.UserInterface?.PlayClickSound();

		if (_selectedSteamIds.Count == 0)
		{
			_feedbackLabel.Text = TranslationServer.Translate("Select at least one friend to invite.");
			return;
		}

		var friendSteamIds = new List<ulong>(_selectedSteamIds);
		int sent = GameContext.Steam.InviteFriends(friendSteamIds);

		if (sent == 0)
		{
			_feedbackLabel.Text = TranslationServer.Translate("Steam did not send the invitations. Close this window and try again.");
			return;
		}

		InvitesSent?.Invoke(sent);
		Close();
	}

	private void RebuildAddresses()
	{
		foreach (Node child in _addressColumn.GetChildren())
		{
			_addressColumn.RemoveChild(child);
			child.QueueFree();
		}

		if (GameContext.Lobby is not { } lobby)
		{
			_addressDescription.Visible = false;
			return;
		}

		_addressDescription.Visible = true;
		_addressDescription.Text = _isHost
			? TranslationServer.Translate("Send one of these addresses to the players you want to play with. A player who has it can join this lobby whether or not it is listed.")
			: TranslationServer.Translate("This is the address this lobby is played at, and you can pass it on to another player.");

		if (_isHost)
		{
			AddAddresses(_addressColumn, "InviteLan", (int)ContentWidth, 17);
			AddCopyInviteMessageButton(_addressColumn);
			return;
		}

		if (lobby.JoinedLobbyAddress is { Length: > 0 } joinedAddress)
		{
			_addressColumn.AddChild(CreateAddressRow("InviteHostAddress", joinedAddress, (int)ContentWidth, 17));
		}
	}

	private void AddAddresses(VBoxContainer container, string prefix, int width, int addressFontSize)
	{
		LobbyManager lobby = GameContext.Lobby!;

		container.AddChild(CreateAddressRow(prefix, lobby.DirectConnectAddress, width, addressFontSize));

		if (lobby.Ipv6ConnectAddress is { Length: > 0 } ipv6Address && lobby.OffersIpv4)
		{
			container.AddChild(CreateNote(
				$"{prefix}Ipv6Hint_Label",
				"Players whose network gives them IPv6 reach this lobby at this address, which needs no port forward and no hole punch:",
				width,
				11,
				UIStyle.ColorGoldDull));
			container.AddChild(CreateAddressRow($"{prefix}Ipv6", ipv6Address, width, addressFontSize));
		}

		if (!lobby.OffersIpv4)
		{
			container.AddChild(CreateNote(
				$"{prefix}Ipv6OnlyHint_Label",
				"Send this address to the players you want to play with. It is this machine's own IPv6 address, so it needs no port forward, no hole punch and nothing from your router - and only players whose network gives them an IPv6 address can use it.",
				width,
				11,
				UIStyle.ColorGoldDull));
			return;
		}

		if (lobby.PublicConnectAddress is { Length: > 0 } publicAddress)
		{
			container.AddChild(CreateNote(
				$"{prefix}PublicHint_Label",
				"Players outside your network join at this address:",
				width,
				11,
				UIStyle.ColorGoldDull));
			container.AddChild(CreateAddressRow($"{prefix}Public", publicAddress, width, addressFontSize));
			container.AddChild(CreateNote(
				$"{prefix}ForwardConfirmation_Label",
				"Your router forwards UDP port {0} to this machine, so this address works without anything else changing.",
				width,
				11,
				UIStyle.ColorGoldDull,
				lobby.BoundPort));
			return;
		}

		if (lobby.RouterReachability == RouterReachability.BehindAnotherNetwork)
		{
			container.AddChild(CreateNote(
				$"{prefix}BehindNetworkHint_Label",
				"Your router already forwards UDP port {0}, but the network players outside can see is the one in front of it, so that forward does not reach them. Make the forward on that network instead, or have your friends join through a virtual LAN such as Tailscale.",
				width,
				11,
				UIStyle.ColorGoldDull,
				lobby.BoundPort));
			return;
		}

		container.AddChild(CreateNote(
			$"{prefix}NoForwardHint_Label",
			lobby.RouterReachability == RouterReachability.PublicAddressUnmeasured
				? "This machine could not measure its own address outside your network, so nothing confirmed a route in to UDP port {0}. If your friends cannot join at the address above, forward that port on the router that faces the internet, or have them join through a virtual LAN such as Tailscale."
				: "Nothing outside your network has reached UDP port {0}, so the address above only works on your own network. Forward that port on your router, or have your friends join through a virtual LAN such as Tailscale, to play with them anywhere.",
			width,
			11,
			UIStyle.ColorGoldDull,
			lobby.BoundPort));

		if (lobby.MeasuredPublicConnectAddress is not { Length: > 0 } measuredAddress) return;

		container.AddChild(CreateNote(
			$"{prefix}MeasuredPublicHint_Label",
			"With UDP port {0} forwarded on your router, players outside your network use this address instead:",
			width,
			11,
			UIStyle.ColorGoldDull,
			lobby.BoundPort));
		container.AddChild(CreateAddressRow($"{prefix}MeasuredPublic", measuredAddress, width, addressFontSize));
	}

	private Label CreateNote(string name, string key, int width, int fontSize, Color color, params object?[] arguments)
	{
		Label note = CreateNoteFromText(name, string.Empty, width, fontSize, color);
		LocalizedText.Set(note, key, arguments);
		return note;
	}

	private Label CreateNoteFromText(string name, string text, int width, int fontSize, Color color)
	{
		var note = new Label { Name = name, Text = text };
		note.AutowrapMode = TextServer.AutowrapMode.WordSmart;
		note.CustomMinimumSize = new Vector2(width, 0);
		note.SizeFlagsHorizontal = SizeFlags.ExpandFill;
		note.HorizontalAlignment = HorizontalAlignment.Center;
		note.AddThemeFontSizeOverride("font_size", fontSize);
		note.AddThemeColorOverride("font_color", color);
		return note;
	}

	private HBoxContainer CreateAddressRow(string namePrefix, string address, int width, int fontSize)
	{
		var row = new HBoxContainer { Name = $"{namePrefix}Row" };
		row.Alignment = BoxContainer.AlignmentMode.Center;
		row.AddThemeConstantOverride("separation", 8);

		var value = new Label { Name = $"{namePrefix}Label", Text = address };
		value.VerticalAlignment = VerticalAlignment.Center;
		value.CustomMinimumSize = new Vector2(width - 160, 0);
		value.AddThemeFontSizeOverride("font_size", fontSize);
		value.AddThemeColorOverride("font_color", UIStyle.ColorCyanGlow);
		row.AddChild(value);

		var copyButton = UIStyle.CreateActionButton($"{namePrefix}Copy_Button", TranslationServer.Translate("COPY"), 13);
		copyButton.CustomMinimumSize = new Vector2(110, 32);
		copyButton.AddThemeConstantOverride("icon_max_width", 0);
		copyButton.TooltipText = TranslationServer.Translate("Copy this address so you can send it to your friends.");
		copyButton.Pressed += () => CopyAddress(address);
		row.AddChild(copyButton);

		return row;
	}

	private void AddCopyInviteMessageButton(VBoxContainer container)
	{
		var copyInviteButton = UIStyle.CreateActionButton("InvitePlayersCopyInviteMessage", TranslationServer.Translate("COPY INVITE MESSAGE"), 13);
		copyInviteButton.CustomMinimumSize = new Vector2(240, 34);
		copyInviteButton.SizeFlagsHorizontal = SizeFlags.ShrinkCenter;
		copyInviteButton.AddThemeConstantOverride("icon_max_width", 0);
		copyInviteButton.TooltipText = TranslationServer.Translate("Copies the map name and the address to join at as one message you can paste into a chat. It uses the address players outside your network reach when that was confirmed, and your own network's address otherwise.");
		copyInviteButton.Pressed += CopyInviteMessage;
		container.AddChild(copyInviteButton);
	}

	private void CopyAddress(string address)
	{
		DisplayServer.ClipboardSet(address);
		GameContext.UserInterface?.PlayClickSound();
		ShowCopyFeedback(TranslationServer.Translate("Address copied."));
	}

	private void CopyInviteMessage()
	{
		if (GameContext.Lobby is not { } lobby) return;

		string message = lobby.DirectConnectInviteMessage;
		if (message.Length == 0) return;

		DisplayServer.ClipboardSet(message);
		GameContext.UserInterface?.PlayClickSound();
		ShowCopyFeedback(TranslationServer.Translate("Invite message copied."));
	}

	private void ShowCopyFeedback(string text)
	{
		_feedbackLabel.Text = text;
	}

	private void OnDismissOverlayInput(InputEvent @event)
	{
		if (@event is not InputEventMouseButton mouseEvent) return;
		if (!mouseEvent.Pressed || mouseEvent.ButtonIndex != MouseButton.Left) return;

		Close();
		GetViewport().SetInputAsHandled();
	}

	public override void _UnhandledKeyInput(InputEvent @event)
	{
		if (!Visible) return;
		if (!@event.IsActionPressed("ui_cancel")) return;

		Close();
		GetViewport().SetInputAsHandled();
	}
}
