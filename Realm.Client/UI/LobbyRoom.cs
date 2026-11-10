using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using Godot;
using NSec.Cryptography;
using Realm.Client.Network;
using Realm.Shared;
using Realm.Shared.Network;
using HttpClient = System.Net.Http.HttpClient;
using static Godot.Control;
using static Godot.CanvasItem;
using Realm.Client.Utils;
using Realm.Client.UI.InGame;
using Realm.Client.Services;
using Realm.Shared.Accounts;

namespace Realm.Client.UI;

[GlobalClass]
public partial class LobbyRoom : Control
{
	/// <summary>
	/// The lobby this screen is bound to. LobbyRoom is only mounted while a lobby is
	/// registered and it unsubscribes on the way out, so an unguarded read here is a
	/// programming error rather than a state the screen has to survive. Stating that once
	/// also stops the compiler reporting each individual read, which the property getter
	/// cannot prove is non-null no matter how many checks precede it.
	/// </summary>
	private static LobbyManager Lobby => GameContext.Lobby
		?? throw new InvalidOperationException("LobbyRoom read the lobby before one was registered.");

	public override void _EnterTree()
	{
		Realm.Client.Services.GameContext.EnsureServices();
	}

	private Panel _bgPanel = null!;
	private Panel _leftPillar = null!;
	private Panel _rightPillar = null!;
	private PanelContainer _playersPanel = null!;
	private PanelContainer _briefingPanel = null!;
	private PanelContainer _chatPanel = null!;

	private Button _backButton = null!;
	private Button _inviteButton = null!;
	private SteamFriendsDialog? _friendsDialog;
	private Button _addBotButton = null!;
	private Button _startButton = null!;
	private Label _mapNameLabel = null!;
	private LineEdit _chatInput = null!;
	private ColorPickerPopover? _colorPickerPopover;
	private RichTextLabel _chatLog = null!;
	private VBoxContainer _playersContainer = null!;
	private readonly Dictionary<int, PanelContainer> _playerRowsByPeerId = new();
	private Label _inviteFeedback = null!;
	private CheckBox _spectatorDelayCheck = null!;
	private Panel? _countdownPopup;
	private Label? _countdownTextLabel;
	private Panel? _connectingPopup;

	private Label _lobbyTitle = null!;
	private Label _playersTitle = null!;
	private Label _briefingTitle = null!;
	private RichTextLabel _briefingLabel = null!;
	private Panel _mapFrame = null!;
	private bool _versionMismatchDetected;
	private bool _ignoreReadinessOnStart;
	private Label? _unstableWarningLabel;
	private Label? _hostStabilityLabel;

	private Label _authorshipWarningLabel = null!;
	private Label _primaryAuthorLabel = null!;
	private string? _authorshipMapJsonPath;
	private Button _otherAuthorsButton = null!;
	private Button _attributionButton = null!;
	private AttributionPopover _attributionPopover = null!;
	private AttributionPopover _otherAuthorsPopover = null!;
	private List<string> _otherAuthorsList = new List<string>();


	private static readonly HttpClient _sharedHttpClient = new HttpClient();
	public const int NEUTRAL_PLAYER_INDEX = PlayerColorConfig.NEUTRAL_PLAYER_INDEX;
	public static Color GetPlayerColor(int playerIndex) => PlayerColorConfig.GetColor(playerIndex);
	public static string GetPlayerColorName(int playerIndex) => PlayerColorConfig.GetName(playerIndex);

	private List<MapBriefingDetails> _lobbyRoomMaps = new List<MapBriefingDetails>();
	private readonly string[] _runes = { "ᚠ", "ᚢ", "ᚦ", "ᚨ", "ᚱ", "ᚲ", "ᚷ", "ᚹ", "ᚺ", "ᚾ", "ᛁ", "ᛃ", "ᛇ", "ᛈ", "ᛉ", "ᛊ", "ᛏ", "ᛒ", "ᛖ", "ᛗ", "ᛚ", "ᛜ", "ᛞ", "ᛟ" };
	private const int ReadyColumnWidth = 100;
	private const int TeamColumnWidth = 150;
	private const int ColorColumnWidth = 56;
	private const int NameColumnWidth = 140;
	private const int FactionColumnWidth = 100;
	private const int DifficultyColumnWidth = 120;
	private const int RowColumnSeparation = 16;
	private const int RowContentInset = 8;

	private static IReadOnlyList<string> FactionList => GameContext.Lobby?.LobbyFactions ?? Array.Empty<string>();

	private static bool HasFactions => FactionList.Count > 0;

	private static int TeamCount => Math.Max(1, GameContext.Lobby?.LobbyTeamCount ?? LobbyLimits.DefaultTeamCount);

	private int _renderedTeamCount = -1;

	public override void _Ready()
	{
		(_bgPanel, _leftPillar, _rightPillar) = UIStyle.BindStandardChrome(this);
		_playersPanel = GetNode<PanelContainer>("PlayersPanel");
		_briefingPanel = GetNode<PanelContainer>("BriefingPanel");
		_chatPanel = GetNode<PanelContainer>("ChatPanel");


		_backButton = GetNode<Button>("BackButton");
		_inviteButton = GetNode<Button>("InviteButton");
		_addBotButton = GetNode<Button>("AddBotButton");
		_startButton = GetNode<Button>("StartButton");
		_addBotButton.Visible = Lobby.IsHost;
		_inviteButton.Visible = !Lobby.IsSinglePlayer;


		_mapNameLabel = GetNode<Label>("MapNameLabel");
		_lobbyRoomMaps = MapInfoHelper.GetAvailableMaps();

		_chatInput = GetNode<LineEdit>("ChatPanel/ChatContainer/ChatInput");
		_chatLog = GetNode<RichTextLabel>("ChatPanel/ChatContainer/ChatLog");
		_playersContainer = GetNode<VBoxContainer>("PlayersPanel/VBoxContainer/ScrollContainer/PlayersContainer");
		_inviteFeedback = GetNode<Label>("InviteFeedback");


		_lobbyTitle = GetNode<Label>("LobbyTitle");
		_playersTitle = GetNode<Label>("PlayersPanel/VBoxContainer/PanelTitle");
		_briefingTitle = GetNode<Label>("BriefingPanel/VBoxContainer/PanelTitle");
		_briefingLabel = GetNode<RichTextLabel>("BriefingPanel/VBoxContainer/BriefingLabel");
		_mapFrame = GetNode<Panel>("BriefingPanel/VBoxContainer/MapFrame");

		_unstableWarningLabel = new Label();
		_unstableWarningLabel.Name = "UnstableWarningLabel";
		_unstableWarningLabel.HorizontalAlignment = HorizontalAlignment.Center;
		_unstableWarningLabel.VerticalAlignment = VerticalAlignment.Center;
		_unstableWarningLabel.Text = Tr("⚠️ Connection between players is unstable. Gameplay will not be reliable.");
		_unstableWarningLabel.AddThemeColorOverride("font_color", new Color(0.9f, 0.25f, 0.25f));
		_unstableWarningLabel.AddThemeFontSizeOverride("font_size", 14);
		_unstableWarningLabel.Visible = false;
		AddChild(_unstableWarningLabel);
		_unstableWarningLabel.SetAnchorsPreset(LayoutPreset.CenterTop);
		_unstableWarningLabel.GrowHorizontal = GrowDirection.Both;
		_unstableWarningLabel.OffsetLeft = -500;
		_unstableWarningLabel.OffsetRight = 500;
		_unstableWarningLabel.OffsetTop = 90;
		_unstableWarningLabel.OffsetBottom = 115;

		_hostStabilityLabel = new Label();
		_hostStabilityLabel.Name = "HostStabilityLabel";
		_hostStabilityLabel.HorizontalAlignment = HorizontalAlignment.Center;
		_hostStabilityLabel.VerticalAlignment = VerticalAlignment.Center;
		_hostStabilityLabel.AddThemeFontSizeOverride("font_size", 16);
		_hostStabilityLabel.Visible = false;
		_hostStabilityLabel.MouseFilter = MouseFilterEnum.Pass;
		_hostStabilityLabel.TooltipText = Tr("Average over last 10 games");
		AddChild(_hostStabilityLabel);
		_hostStabilityLabel.SetAnchorsPreset(LayoutPreset.CenterTop);
		_hostStabilityLabel.GrowHorizontal = GrowDirection.Both;
		_hostStabilityLabel.OffsetLeft = -300;
		_hostStabilityLabel.OffsetRight = 300;
		_hostStabilityLabel.OffsetTop = 85;
		_hostStabilityLabel.OffsetBottom = 110;

		if (!Lobby.IsHost)
		{
			UpdateHostStabilityLabel(Lobby.HostStability);
			Lobby.HostStabilityUpdated += UpdateHostStabilityLabel;
		}
		else if (Lobby.IsRestrictedHost)
		{
			var restrictedWarning = new Label();
			restrictedWarning.Name = "RestrictedWarningLabel";
			restrictedWarning.HorizontalAlignment = HorizontalAlignment.Center;
			restrictedWarning.VerticalAlignment = VerticalAlignment.Center;
			restrictedWarning.Text = Tr("⚠️ UPnP inactive. External players may need Direct Connect (IP:Port) or port forwarding.");
			restrictedWarning.AddThemeColorOverride("font_color", new Color(0.95f, 0.7f, 0.2f));
			restrictedWarning.AddThemeFontSizeOverride("font_size", 13);
			AddChild(restrictedWarning);
			restrictedWarning.SetAnchorsPreset(LayoutPreset.CenterTop);
			restrictedWarning.GrowHorizontal = GrowDirection.Both;
			restrictedWarning.OffsetLeft = -500;
			restrictedWarning.OffsetRight = 500;
			restrictedWarning.OffsetTop = 85;
			restrictedWarning.OffsetBottom = 110;
		}


		ApplyThemeStyles();


		Lobby.PlayerListUpdated += PopulatePlayersList;
		Lobby.ChatReceived += OnLobbyChatReceived;
		Lobby.ConnectionFailed += OnLobbyConnectionFailed;
		Lobby.KickReceived += OnLobbyKickReceived;
		Lobby.CountdownStarted += OnCountdownStarted;
		Lobby.CountdownTick += OnCountdownTick;
		Lobby.CountdownCancelled += OnCountdownCancelled;
		Lobby.CountdownFinished += OnCountdownFinished;
		Lobby.ActiveMapChanged += OnActiveMapChanged;
		Lobby.StartWarningReceived += OnStartWarningReceived;


		if (!Lobby.IsHost)
		{
			CreateDownloadProgressUI();
			Lobby.RequestChatHistory();
		}

		PopulatePlayersList();

		// The lobby is drawn first and covers the screen only while this screen has no seat in it yet. Once the authority
		// has described the lobby there is nothing left to connect to: the map package and the world are prepared behind
		// a lobby the player can already read and chat in, so the cover is not raised over them.
		if (!Lobby.IsHost && !Lobby.HasLobbyState)
		{
			ShowConnectingPopup();
		}

		CreateSpectatorDelayUI();
		Lobby.SpectatorDelayChanged += OnSpectatorDelayChanged;

		_chatLog.Text = $"[color=#ffd700]{Tr("System: Connected to Lobby.")}[/color]\n";
		_chatInput.TextSubmitted += OnChatSubmitted;


		var mapImage = new TextureRect();
		mapImage.Name = "MapImage_TextureRect";
		mapImage.Texture = TrimmedTextureLoader.LoadTrimmedTexture("res://Assets/UI/snowy_forest_path.webp");
		mapImage.ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize;
		mapImage.StretchMode = TextureRect.StretchModeEnum.KeepAspectCovered;
		_mapFrame.AddChild(mapImage);
		mapImage.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);


		var mapDrawer = new TacticalMap();
		mapDrawer.Name = "MapDrawer_TacticalMap";
		_mapFrame.AddChild(mapDrawer);
		mapDrawer.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);


		_authorshipWarningLabel = new Label();
		_authorshipWarningLabel.Name = "AuthorshipWarningLabel_Label";
		_authorshipWarningLabel.Text = "⚠️ " + Tr("Unable to verify map authorship");
		_authorshipWarningLabel.AddThemeColorOverride("font_color", new Color(1, 0.4f, 0.4f));
		_authorshipWarningLabel.AddThemeFontSizeOverride("font_size", 12);
		_authorshipWarningLabel.HorizontalAlignment = HorizontalAlignment.Center;
		_authorshipWarningLabel.Visible = false;
		GetNode<VBoxContainer>("BriefingPanel/VBoxContainer").AddChild(_authorshipWarningLabel);

		_primaryAuthorLabel = new Label();
		_primaryAuthorLabel.Name = "PrimaryAuthorLabel_Label";
		_primaryAuthorLabel.Text = Tr("Author: Unknown");
		_primaryAuthorLabel.AddThemeColorOverride("font_color", UIStyle.ColorGoldDull);
		_primaryAuthorLabel.AddThemeFontSizeOverride("font_size", 12);
		_primaryAuthorLabel.HorizontalAlignment = HorizontalAlignment.Center;
		_primaryAuthorLabel.Visible = false;
		GetNode<VBoxContainer>("BriefingPanel/VBoxContainer").AddChild(_primaryAuthorLabel);

		var authorsSpacer = new Control { CustomMinimumSize = new Vector2(0, 24) };
		GetNode<VBoxContainer>("BriefingPanel/VBoxContainer").AddChild(authorsSpacer);

		_otherAuthorsButton = new Button();
		_otherAuthorsButton.Name = "OtherAuthorsButton_Button";
		_otherAuthorsButton.Text = Tr("Other Authors");
		_otherAuthorsButton.CustomMinimumSize = new Vector2(160, 32);
		_otherAuthorsButton.SizeFlagsHorizontal = Control.SizeFlags.ShrinkCenter;
		_otherAuthorsButton.Visible = false;
		UIStyle.ApplyButtonStyleBoxes(_otherAuthorsButton);
		_otherAuthorsButton.Pressed += OnOtherAuthorsPressed;
		GetNode<VBoxContainer>("BriefingPanel/VBoxContainer").AddChild(_otherAuthorsButton);

		SetupAttributionButton();

		UpdateSelectedMapUI();
	}


	private void SetupLobbyButton(Button btn, string text, Action onClick)
	{
		btn.Flat = false;
		btn.AddThemeConstantOverride("icon_max_width", 0);
		UIStyle.ApplyButtonText(btn, text, 15);

		UIStyle.ApplyButtonStyleBoxesClearingFocus(btn);

		btn.Pressed += () =>
		{
			GameContext.UserInterface?.PlayClickSound();
			onClick?.Invoke();
		};
		btn.MouseEntered += () => GameContext.UserInterface?.PlayHoverSound();
	}


	private void OnActiveMapChanged(string mapName)
	{
		UpdateSelectedMapUI();
		if (!Lobby.IsHost && _downloadProgress != null && _downloadLabel != null)
		{
			if (MapAssetManager.IsMapDownloaded(mapName))
			{
				_downloadProgress.Value = 100.0f;
				_downloadLabel.Text = Tr("Map package ready.");
				_downloadLabel.AddThemeColorOverride("font_color", UIStyle.ColorCyanGlow);
			}
			else
			{
				_downloadProgress.Value = 0.0f;
				_downloadLabel.Text = Tr("Checking map package...");
				_downloadLabel.AddThemeColorOverride("font_color", UIStyle.ColorGoldDull);
			}
		}

		SetupStartButton();
	}



	private void OpenInviteFriendsDialog()
	{
		if (Lobby.ActiveLobbyId is not { Length: > 0 } lobbyId || lobbyId.StartsWith("direct-", StringComparison.Ordinal))
		{
			GameContext.UserInterface?.ShowPopupMessage(
				Tr("LOBBY INVITES"),
				Tr("Invites need a lobby the server lists. A direct connection cannot be invited to."));
			return;
		}

		if (_friendsDialog == null || !GodotObject.IsInstanceValid(_friendsDialog))
		{
			_friendsDialog = new SteamFriendsDialog();
			AddChild(_friendsDialog);
			_friendsDialog.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
			_friendsDialog.InvitesSent += OnFriendInvitesSent;
		}

		_friendsDialog.ShowFor(lobbyId, Lobby.MaxPlayers);
	}

	private void OnFriendInvitesSent(int sentCount)
	{
		ShowInviteFeedback(sentCount == 1
			? Tr("Invited 1 friend.")
			: string.Format(Tr("Invited {0} friends."), sentCount));
	}

	private void ShowInviteFeedback(string text)
	{
		_inviteFeedback.Text = text;
		_inviteFeedback.Modulate = new Color(0.3f, 0.8f, 1.0f, 1.0f);

		var tween = CreateTween();
		tween.TweenProperty(_inviteFeedback, "modulate:a", 0.0f, 2.0f).SetDelay(2.0f);
	}

	private void PopulateRunicPillar(VBoxContainer container)
	{
		container.Visible = false;
	}

	public override void _ExitTree()
	{
		GameContext.Steam.CloseInviteLobby();

		if (GameContext.Lobby != null)
		{
			Lobby.PlayerListUpdated -= PopulatePlayersList;
			Lobby.ChatReceived -= OnLobbyChatReceived;
			Lobby.ConnectionFailed -= OnLobbyConnectionFailed;
			Lobby.KickReceived -= OnLobbyKickReceived;

			Lobby.SpectatorDelayChanged -= OnSpectatorDelayChanged;

			Lobby.MapDownloadProgressChanged -= OnMapDownloadProgress;
			Lobby.MapDownloadCompleted -= OnMapDownloadCompleted;
			Lobby.MapDownloadFailed -= OnMapDownloadFailed;

			Lobby.CountdownStarted -= OnCountdownStarted;
			Lobby.CountdownTick -= OnCountdownTick;
			Lobby.CountdownCancelled -= OnCountdownCancelled;
			Lobby.CountdownFinished -= OnCountdownFinished;
			Lobby.HostStabilityUpdated -= UpdateHostStabilityLabel;
			Lobby.ActiveMapChanged -= OnActiveMapChanged;
			Lobby.StartWarningReceived -= OnStartWarningReceived;
		}

		base._ExitTree();
	}

	private void OnCountdownStarted(string mapName, int seconds)
	{
		if (_countdownPopup != null && GodotObject.IsInstanceValid(_countdownPopup))
		{
			_countdownPopup.QueueFree();
		}

		_countdownPopup = new Panel();
		_countdownPopup.Name = "CountdownPopup_Panel";
		_countdownPopup.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
		_countdownPopup.AddThemeStyleboxOverride("panel", UIStyle.CreateLobbyRoomBg());
		AddChild(_countdownPopup);

		var cardPanel = new Panel();
		cardPanel.Name = "CardPanel_Panel";
		cardPanel.CustomMinimumSize = new Vector2(420, 560);
		cardPanel.Size = new Vector2(420, 560);
		cardPanel.GrowHorizontal = Control.GrowDirection.Both;
		cardPanel.GrowVertical = Control.GrowDirection.Both;
		cardPanel.SetAnchorsAndOffsetsPreset(LayoutPreset.Center);
		cardPanel.AddThemeStyleboxOverride("panel", UIStyle.CreateGameStartingPanel());
		_countdownPopup.AddChild(cardPanel);

		var vbox = new VBoxContainer();
		vbox.Name = "Vbox_VBoxContainer";
		vbox.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
		vbox.CustomMinimumSize = new Vector2(420, 560);
		vbox.SizeFlagsHorizontal = SizeFlags.ExpandFill;
		vbox.SizeFlagsVertical = SizeFlags.ExpandFill;
		cardPanel.AddChild(vbox);

		vbox.AddChild(new Control { CustomMinimumSize = new Vector2(0, 22) });

		var titleLabel = new Label();
		titleLabel.Name = "TitleLabel_Label";
		UIStyle.ApplyTitle(titleLabel, TranslationServer.Translate("GAME STARTING"), 26);
		titleLabel.AddThemeColorOverride("font_color", UIStyle.ColorGold);
		vbox.AddChild(titleLabel);

		var circleContainer = new CenterContainer();
		circleContainer.Name = "CircleContainer_CenterContainer";
		circleContainer.SizeFlagsVertical = SizeFlags.ExpandFill;
		circleContainer.SizeFlagsHorizontal = SizeFlags.ExpandFill;

		_countdownTextLabel = new Label();
		_countdownTextLabel.Name = "CountdownTextLabel_Label";
		_countdownTextLabel.Text = seconds.ToString();
		_countdownTextLabel.HorizontalAlignment = HorizontalAlignment.Center;
		_countdownTextLabel.VerticalAlignment = VerticalAlignment.Center;
		_countdownTextLabel.AddThemeColorOverride("font_color", UIStyle.ColorCyanGlow);
		_countdownTextLabel.AddThemeColorOverride("font_outline_color", Colors.Black);
		_countdownTextLabel.AddThemeConstantOverride("outline_size", 10);
		_countdownTextLabel.AddThemeFontSizeOverride("font_size", 72);
		circleContainer.AddChild(_countdownTextLabel);
		vbox.AddChild(circleContainer);

		var cancelBtn = UIStyle.CreateActionButton("CancelBtn_Button", TranslationServer.Translate("CANCEL"), 16);

		cancelBtn.CustomMinimumSize = new Vector2(180, 48);
		cancelBtn.SizeFlagsHorizontal = SizeFlags.ShrinkCenter;
		cancelBtn.Pressed += () =>
		{
			GameContext.UserInterface?.PlayClickSound();
			Lobby.RequestCancelCountdown();
		};
		vbox.AddChild(cancelBtn);

		vbox.AddChild(new Control { CustomMinimumSize = new Vector2(0, 18) });
	}

	private void OnCountdownTick(int seconds)
	{
		if (_countdownTextLabel != null && GodotObject.IsInstanceValid(_countdownTextLabel))
		{
			_countdownTextLabel.Text = seconds.ToString();
		}
	}

	private void OnCountdownCancelled()
	{
		if (_countdownPopup != null && GodotObject.IsInstanceValid(_countdownPopup))
		{
			_countdownPopup.QueueFree();
			_countdownPopup = null;
			_countdownTextLabel = null;
		}
	}

	private void OnCountdownFinished()
	{
		if (_countdownPopup != null && GodotObject.IsInstanceValid(_countdownPopup))
		{
			_countdownPopup.QueueFree();
			_countdownPopup = null;
			_countdownTextLabel = null;
		}

		if (GameContext.Lobby != null && Lobby.IsHost)
		{
			Lobby.UnregisterActiveLobbyFromRegistry();
		}
	}

	private void PopulatePlayersList()
	{
		if (!GodotObject.IsInstanceValid(this) || !IsInsideTree()) return;

		HideConnectingPopup();

		if (CheckHostVersionMismatch()) return;

		UpdateUnstableWarningLabel();
		UpdateFactionColumnVisibility();

		var existingRows = GetExistingPlayerRows();
		if (_renderedTeamCount != TeamCount)
		{
			foreach (var row in existingRows.Values) ReleasePlayerRow(row);

			existingRows.Clear();
			_playerRowsByPeerId.Clear();
			_renderedTeamCount = TeamCount;
		}

		var activePeerIds = SyncPlayerRows(existingRows);

		CleanupStalePlayerRows(existingRows, activePeerIds);

		UpdateAddBotAvailability();
		SetupStartButton();
	}

	private void UpdateAddBotAvailability()
	{
		_playersTitle.Text = $"{TranslationServer.Translate("TEAMS & PLAYERS")} ({Lobby.PlayingPlayerCount}/{Lobby.MaxPlayers})";

		if (_addBotButton == null || !GodotObject.IsInstanceValid(_addBotButton)) return;
		if (!Lobby.IsHost) return;

		_addBotButton.Disabled = Lobby.PlayingPlayerCount >= Lobby.MaxPlayers;
	}

	private bool CheckHostVersionMismatch()
	{
		if (Lobby.IsHost || _versionMismatchDetected) return false;

		var host = Lobby.PlayerList.Find(p => p.IsHost);
		if (host == null) return false;

		string hostVersion = host.BinaryVersion;
		string clientVersion = RealmVersion.GameBinaryVersion;
		if (hostVersion == clientVersion) return false;

		_versionMismatchDetected = true;
		ShowVersionMismatchPopup(hostVersion, clientVersion);
		return true;
	}

	private void UpdateUnstableWarningLabel()
	{
		if (_unstableWarningLabel != null && GodotObject.IsInstanceValid(_unstableWarningLabel))
		{
			_unstableWarningLabel.Visible = IsAnyMetricRed();
		}
	}

	private Dictionary<int, PanelContainer> GetExistingPlayerRows()
	{
		var existingRows = new Dictionary<int, PanelContainer>(_playerRowsByPeerId.Count);
		foreach (var pair in _playerRowsByPeerId)
		{
			if (!GodotObject.IsInstanceValid(pair.Value)) continue;
			existingRows[pair.Key] = pair.Value;
		}

		_playerRowsByPeerId.Clear();
		foreach (var pair in existingRows) _playerRowsByPeerId[pair.Key] = pair.Value;

		return existingRows;
	}

	private void ReleasePlayerRow(PanelContainer row)
	{
		if (!GodotObject.IsInstanceValid(row)) return;

		if (row.GetParent() == _playersContainer) _playersContainer.RemoveChild(row);
		row.QueueFree();
	}

	private HashSet<int> SyncPlayerRows(Dictionary<int, PanelContainer> existingRows)
	{
		var activePeerIds = new HashSet<int>();

		for (int i = 0; i < Lobby.PlayerList.Count; i++)
		{
			var player = Lobby.PlayerList[i];
			activePeerIds.Add(player.PeerId);

			if (existingRows.TryGetValue(player.PeerId, out var row))
			{
				UpdatePlayerRow(row, player);
				_playersContainer.MoveChild(row, i);
			}
			else
			{
				var newRow = CreatePlayerRow(player);
				newRow.Name = $"PlayerRow_{player.PeerId}";
				_playersContainer.AddChild(newRow);
				_playersContainer.MoveChild(newRow, i);
				existingRows[player.PeerId] = newRow;
				_playerRowsByPeerId[player.PeerId] = newRow;
			}
		}

		return activePeerIds;
	}

	private void CleanupStalePlayerRows(Dictionary<int, PanelContainer> existingRows, HashSet<int> activePeerIds)
	{
		foreach (var kvp in existingRows)
		{
			if (!activePeerIds.Contains(kvp.Key))
			{
				_playerRowsByPeerId.Remove(kvp.Key);
				ReleasePlayerRow(kvp.Value);
			}
		}
	}


	private void OnChatSubmitted(string text)
	{
		if (string.IsNullOrEmpty(text.Trim())) return;
		_chatInput.Clear();
		string myName = GetLocalPlayer()?.Name ?? Lobby.PlayerDisplayName;
		Lobby.SendChatMessage(myName, text);
	}

	private void OnLobbyChatReceived(string senderName, string message, bool alliesOnly, long timestampUtcTicks)
	{
		string myName = GetLocalPlayer()?.Name ?? Lobby.PlayerDisplayName;
		string color = senderName == "System" ? "#ffd700" : (senderName == myName ? "#5cd6ff" : "#d4a0a0");
		string timestamp = ChatPanel.FormatTimestamp(timestampUtcTicks);
		_chatLog.Text += $"[color=#a0a0a0][{timestamp}][/color] [color={color}]{senderName}[/color]: {message}\n";
	}

	private void OnLobbyConnectionFailed(string reason)
	{
		HideConnectingPopup();
		GameLog.LogError($"[LobbyRoom] Connection failed from lobby: {reason}");
		GameContext.UserInterface?.PlayWarningSound();
		ShowConnectionFailedPopup(reason);
	}

	private void ShowConnectionFailedPopup(string reason)
	{
		string displayReason = reason;
		if (reason == "Direct connection handshake failed.")
		{
			displayReason = $"{Tr("Direct connection handshake failed.")}\n\n{Tr("The host's router may be blocking incoming UDP traffic on port 8999. Ensure UPnP is enabled on the host's router, configure UDP port 8999 forwarding, or connect via a Virtual LAN (Tailscale / Radmin).")}";
		}

		GameContext.UserInterface?.ShowPopupMessage(Tr("CONNECTION FAILED"), $"{Tr("Failed to connect to host.")}\n\n{string.Format(Tr("Reason: {0}"), displayReason)}", () =>
		{
			ReturnFromLobby();
		});
	}

	private void OnLobbyKickReceived(string reason)
	{
		GameLog.LogToConsole($"[LobbyRoom] Disconnected / Kicked: {reason}");
		GameContext.UserInterface?.PlayWarningSound();
		ReturnFromLobby();
	}



	private void ShowConnectingPopup()
	{
		if (_connectingPopup != null && GodotObject.IsInstanceValid(_connectingPopup))
		{
			_connectingPopup.QueueFree();
		}

		if (_spectatorDelayCheck != null && GodotObject.IsInstanceValid(_spectatorDelayCheck))
		{
			_spectatorDelayCheck.Visible = false;
		}

		_connectingPopup = new Panel();
		_connectingPopup.Name = "ConnectingPopup_Panel";
		_connectingPopup.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
		_connectingPopup.AddThemeStyleboxOverride("panel", UIStyle.CreateBgGradient());
		AddChild(_connectingPopup);

		var centerContainer = new CenterContainer();
		centerContainer.Name = "CenterContainer_CenterContainer";
		centerContainer.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
		_connectingPopup.AddChild(centerContainer);

		var cardPanel = new Panel();
		cardPanel.Name = "CardPanel_Panel";
		cardPanel.CustomMinimumSize = new Vector2(440, 160);
		cardPanel.AddThemeStyleboxOverride("panel", UIStyle.CreateBackdropPanel());
		centerContainer.AddChild(cardPanel);

		var vbox = new VBoxContainer();
		vbox.Name = "Vbox_VBoxContainer";
		vbox.CustomMinimumSize = new Vector2(400, 120);
		vbox.Alignment = BoxContainer.AlignmentMode.Center;
		cardPanel.AddChild(vbox);
		vbox.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);

		var label = new Label();
		label.Name = "Label_Label";
		UIStyle.ApplyTitle(label, Tr("CONNECTING TO HOST ..."), 22);
		label.AddThemeColorOverride("font_color", UIStyle.ColorGold);
		label.AddThemeConstantOverride("outline_size", 4);
		label.AddThemeColorOverride("font_outline_color", new Color(0, 0, 0, 1));
		vbox.AddChild(label);

		var spinner = new Label();
		spinner.Name = "Spinner_Label";
		spinner.Text = Tr("Connecting ...");
		spinner.HorizontalAlignment = HorizontalAlignment.Center;
		spinner.AddThemeFontSizeOverride("font_size", 14);
		spinner.AddThemeColorOverride("font_color", new Color(0.7f, 0.7f, 0.75f));
		vbox.AddChild(spinner);
	}

	private void HideConnectingPopup()
	{
		if (_connectingPopup != null && GodotObject.IsInstanceValid(_connectingPopup))
		{
			_connectingPopup.QueueFree();
			_connectingPopup = null;
		}

		if (_spectatorDelayCheck != null && GodotObject.IsInstanceValid(_spectatorDelayCheck))
		{
			_spectatorDelayCheck.Visible = true;
		}
	}
}
