using System;
using System.Collections.Generic;
using Godot;
using Realm.Client.Services;

namespace Realm.Client.UI;

[GlobalClass]
public partial class SteamFriendsDialog : Control
{
	private const float PanelWidth = 640.0f;
	private const float FriendListHeight = 420.0f;
	private const float StatusColumnWidth = 150.0f;

	private readonly HashSet<ulong> _selectedSteamIds = new HashSet<ulong>();

	private ColorRect _dismissOverlay;
	private PanelContainer _panel;
	private Label _setupLabel;
	private Label _steamUnavailableLabel;
	private Label _emptyLabel;
	private ScrollContainer _friendScroll;
	private VBoxContainer _friendList;
	private Label _feedbackLabel;
	private Button _sendButton;
	private Button _closeButton;
	private bool _built;
	private string _gameLobbyId = string.Empty;
	private int _maximumMembers = 2;

	public event Action<int>? InvitesSent;

	public override void _Ready()
	{
		EnsureBuilt();
	}

	public void ShowFor(string gameLobbyId, int maximumMembers)
	{
		EnsureBuilt();

		_gameLobbyId = gameLobbyId;
		_maximumMembers = maximumMembers;
		_selectedSteamIds.Clear();
		_feedbackLabel.Text = string.Empty;
		Visible = true;

		Node? parent = GetParent();
		if (parent != null)
		{
			parent.MoveChild(this, parent.GetChildCount() - 1);
		}

		if (!GameContext.Steam.IsAvailable)
		{
			_steamUnavailableLabel.Text = GameContext.Steam.UnavailableExplanation;
			_steamUnavailableLabel.Visible = true;
			_friendScroll.Visible = false;
			_emptyLabel.Visible = false;
			_sendButton.Disabled = true;
			_setupLabel.Text = string.Empty;
			return;
		}

		_steamUnavailableLabel.Visible = false;
		_friendScroll.Visible = true;

		if (PopulateFriends() == 0)
		{
			RefreshFriendsOnceNamesArriveAsync();
		}

		PrepareInviteLobbyAsync();
	}

	private async void RefreshFriendsOnceNamesArriveAsync()
	{
		await ToSignal(GetTree().CreateTimer(1.5f), SceneTreeTimer.SignalName.Timeout);
		if (!GodotObject.IsInstanceValid(this) || !Visible) return;

		PopulateFriends();
	}

	public void Close()
	{
		Visible = false;
	}

	private void EnsureBuilt()
	{
		if (_built) return;
		_built = true;

		Name = "SteamFriendsDialog";
		Visible = false;
		MouseFilter = MouseFilterEnum.Stop;
		SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);

		_dismissOverlay = new ColorRect();
		_dismissOverlay.Name = "DismissOverlay_ColorRect";
		_dismissOverlay.Color = new Color(0.0f, 0.0f, 0.0f, 0.55f);
		_dismissOverlay.MouseFilter = MouseFilterEnum.Stop;
		_dismissOverlay.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
		_dismissOverlay.GuiInput += OnDismissOverlayInput;
		AddChild(_dismissOverlay);

		var center = new CenterContainer();
		center.Name = "Center_CenterContainer";
		center.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
		AddChild(center);

		_panel = new PanelContainer();
		_panel.Name = "FriendsPanel_PanelContainer";
		_panel.AddThemeStyleboxOverride("panel", UIStyle.CreateStonePanel(true));
		_panel.CustomMinimumSize = new Vector2(PanelWidth, 0);
		center.AddChild(_panel);

		var content = new VBoxContainer();
		content.Name = "Content_VBoxContainer";
		content.AddThemeConstantOverride("separation", 10);
		_panel.AddChild(content);

		var titleLabel = new Label();
		titleLabel.Name = "Title_Label";
		UIStyle.ApplyTitle(titleLabel, TranslationServer.Translate("INVITE STEAM FRIENDS"), 24);
		titleLabel.AddThemeColorOverride("font_color", UIStyle.ColorGold);
		titleLabel.HorizontalAlignment = HorizontalAlignment.Center;
		content.AddChild(titleLabel);

		var subtitleLabel = new Label();
		subtitleLabel.Name = "Subtitle_Label";
		subtitleLabel.Text = TranslationServer.Translate("Choose the friends to invite. They receive the invitation from Steam and can join this lobby with one click.");
		subtitleLabel.AutowrapMode = TextServer.AutowrapMode.WordSmart;
		subtitleLabel.CustomMinimumSize = new Vector2(PanelWidth - 80, 0);
		subtitleLabel.SizeFlagsHorizontal = SizeFlags.ExpandFill;
		subtitleLabel.HorizontalAlignment = HorizontalAlignment.Center;
		subtitleLabel.AddThemeColorOverride("font_color", new Color(0.78f, 0.8f, 0.86f));
		subtitleLabel.AddThemeFontSizeOverride("font_size", 13);
		content.AddChild(subtitleLabel);

		_setupLabel = new Label();
		_setupLabel.Name = "Setup_Label";
		_setupLabel.AutowrapMode = TextServer.AutowrapMode.WordSmart;
		_setupLabel.CustomMinimumSize = new Vector2(PanelWidth - 80, 0);
		_setupLabel.SizeFlagsHorizontal = SizeFlags.ExpandFill;
		_setupLabel.HorizontalAlignment = HorizontalAlignment.Center;
		_setupLabel.AddThemeColorOverride("font_color", UIStyle.ColorGoldDull);
		_setupLabel.AddThemeFontSizeOverride("font_size", 12);
		content.AddChild(_setupLabel);

		_steamUnavailableLabel = new Label();
		_steamUnavailableLabel.Name = "SteamUnavailable_Label";
		_steamUnavailableLabel.AutowrapMode = TextServer.AutowrapMode.WordSmart;
		_steamUnavailableLabel.CustomMinimumSize = new Vector2(PanelWidth - 80, 0);
		_steamUnavailableLabel.SizeFlagsHorizontal = SizeFlags.ExpandFill;
		_steamUnavailableLabel.HorizontalAlignment = HorizontalAlignment.Center;
		_steamUnavailableLabel.AddThemeColorOverride("font_color", new Color(0.92f, 0.55f, 0.35f));
		_steamUnavailableLabel.AddThemeFontSizeOverride("font_size", 13);
		_steamUnavailableLabel.Visible = false;
		content.AddChild(_steamUnavailableLabel);

		_friendScroll = new ScrollContainer();
		_friendScroll.Name = "FriendScroll_ScrollContainer";
		_friendScroll.CustomMinimumSize = new Vector2(PanelWidth - 60, FriendListHeight);
		_friendScroll.SizeFlagsVertical = SizeFlags.ExpandFill;
		_friendScroll.HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled;
		content.AddChild(_friendScroll);

		_friendList = new VBoxContainer();
		_friendList.Name = "FriendList_VBoxContainer";
		_friendList.SizeFlagsHorizontal = SizeFlags.ExpandFill;
		_friendList.AddThemeConstantOverride("separation", 6);
		_friendScroll.AddChild(_friendList);

		_emptyLabel = new Label();
		_emptyLabel.Name = "EmptyFriends_Label";
		_emptyLabel.Text = TranslationServer.Translate("No Steam friends were found on this account.");
		_emptyLabel.AutowrapMode = TextServer.AutowrapMode.WordSmart;
		_emptyLabel.CustomMinimumSize = new Vector2(PanelWidth - 80, 0);
		_emptyLabel.SizeFlagsHorizontal = SizeFlags.ExpandFill;
		_emptyLabel.HorizontalAlignment = HorizontalAlignment.Center;
		_emptyLabel.AddThemeColorOverride("font_color", new Color(0.7f, 0.72f, 0.78f));
		_emptyLabel.AddThemeFontSizeOverride("font_size", 13);
		_emptyLabel.Visible = false;
		content.AddChild(_emptyLabel);

		_feedbackLabel = new Label();
		_feedbackLabel.Name = "Feedback_Label";
		_feedbackLabel.AutowrapMode = TextServer.AutowrapMode.WordSmart;
		_feedbackLabel.CustomMinimumSize = new Vector2(PanelWidth - 80, 24);
		_feedbackLabel.SizeFlagsHorizontal = SizeFlags.ExpandFill;
		_feedbackLabel.HorizontalAlignment = HorizontalAlignment.Center;
		_feedbackLabel.AddThemeColorOverride("font_color", new Color(0.3f, 0.8f, 1.0f));
		_feedbackLabel.AddThemeFontSizeOverride("font_size", 13);
		content.AddChild(_feedbackLabel);

		var buttons = new HBoxContainer();
		buttons.Name = "Buttons_HBoxContainer";
		buttons.AddThemeConstantOverride("separation", 20);
		buttons.SizeFlagsHorizontal = SizeFlags.ShrinkCenter;
		content.AddChild(buttons);

		_sendButton = new Button();
		_sendButton.Name = "SendInvites_Button";
		_sendButton.Text = TranslationServer.Translate("SEND INVITES");
		_sendButton.Set("icon_max_width", 0);
		_sendButton.CustomMinimumSize = new Vector2(220, 44);
		_sendButton.FocusMode = FocusModeEnum.None;
		_sendButton.AddThemeFontSizeOverride("font_size", 14);
		UIStyle.ApplyButtonStyleboxes(_sendButton);
		_sendButton.AddThemeStyleboxOverride("disabled", UIStyle.CreateButtonNormal());
		_sendButton.AddThemeStyleboxOverride("focus", new StyleBoxEmpty());
		_sendButton.Pressed += OnSendInvitesPressed;
		_sendButton.MouseEntered += () => GameContext.UserInterface?.PlayHoverSound();
		buttons.AddChild(_sendButton);

		_closeButton = new Button();
		_closeButton.Name = "Close_Button";
		_closeButton.Text = TranslationServer.Translate("CLOSE");
		_closeButton.Set("icon_max_width", 0);
		_closeButton.CustomMinimumSize = new Vector2(160, 44);
		_closeButton.FocusMode = FocusModeEnum.None;
		_closeButton.AddThemeFontSizeOverride("font_size", 14);
		UIStyle.ApplyButtonStyleboxesClearingFocus(_closeButton);
		_closeButton.Pressed += Close;
		_closeButton.MouseEntered += () => GameContext.UserInterface?.PlayHoverSound();
		buttons.AddChild(_closeButton);
	}

	private int PopulateFriends()
	{
		foreach (Node child in _friendList.GetChildren())
		{
			_friendList.RemoveChild(child);
			child.QueueFree();
		}

		IReadOnlyList<SteamFriend> friends = GameContext.Steam.GetFriends();
		_emptyLabel.Visible = friends.Count == 0;

		foreach (SteamFriend friend in friends)
		{
			_friendList.AddChild(CreateFriendRow(friend));
		}

		return friends.Count;
	}

	private Control CreateFriendRow(SteamFriend friend)
	{
		var row = new HBoxContainer();
		row.Name = "FriendRow_HBoxContainer";
		row.SizeFlagsHorizontal = SizeFlags.ExpandFill;
		row.AddThemeConstantOverride("separation", 12);

		var checkBox = new CheckBox();
		checkBox.Name = "FriendCheck_CheckBox";
		checkBox.Text = friend.PersonaName;
		checkBox.CustomMinimumSize = new Vector2(PanelWidth - 60 - StatusColumnWidth - 40, 34);
		checkBox.SizeFlagsHorizontal = SizeFlags.ExpandFill;
		checkBox.FocusMode = FocusModeEnum.None;
		checkBox.ClipText = true;
		checkBox.AddThemeFontSizeOverride("font_size", 14);
		checkBox.ButtonPressed = _selectedSteamIds.Contains(friend.SteamId);
		UIStyle.ApplyCheckboxStyle(checkBox);
		checkBox.Toggled += pressed => OnFriendToggled(friend.SteamId, pressed);
		checkBox.MouseEntered += () => GameContext.UserInterface?.PlayHoverSound();
		row.AddChild(checkBox);


		var statusLabel = new Label();
		statusLabel.Name = "FriendStatus_Label";
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
		_setupLabel.Text = TranslationServer.Translate("Preparing the invite...");
		_sendButton.Disabled = true;
		_feedbackLabel.Text = string.Empty;

		bool prepared = await GameContext.Steam.OpenInviteLobbyAsync(_gameLobbyId, _maximumMembers);
		if (!GodotObject.IsInstanceValid(this)) return;

		_setupLabel.Text = prepared ? string.Empty : TranslationServer.Translate("Steam could not prepare an invite. Close this window and try again.");
		_sendButton.Disabled = !prepared;
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
