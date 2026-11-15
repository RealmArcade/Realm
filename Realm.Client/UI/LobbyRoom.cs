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
using Realm.Shared.Reachability;
using Realm.Shared.Accounts;
using Realm.Shared.Simulation;

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
	private Button _steamInviteButton = null!;
	private Button _shareAddressButton = null!;
	private Label _privateLobbyHint = null!;

	private Label _lobbyFamiliesHint = null!;
	private SteamFriendsDialog? _friendsDialog;
	private Button _startButton = null!;
	private Label _mapNameLabel = null!;
	private LineEdit _chatInput = null!;
	private ColorPickerPopover? _colorPickerPopover;
	private AiSettingsPopover? _aiSettingsPopover;
	private RichTextLabel _chatLog = null!;
	private VBoxContainer _playersContainer = null!;
	private readonly Dictionary<int, PanelContainer> _playerRowsBySlot = new();
	private Label _inviteFeedback = null!;
	private Panel? _countdownPopup;
	private Label? _countdownTextLabel;
	private Panel? _connectingPopup;
	private Button? _matchSettingsButton;
	private MatchSettingsPanel? _matchSettingsPanel;

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
	private Button _attributionButton = null!;
	private AttributionPopover _attributionPopover = null!;
	private List<string> _otherAuthorsList = new List<string>();


	private static readonly HttpClient _sharedHttpClient = new HttpClient();
	public const int NEUTRAL_PLAYER_INDEX = PlayerColorConfig.NEUTRAL_PLAYER_INDEX;
	public static Color GetPlayerColor(int playerIndex) => PlayerColorConfig.GetColor(playerIndex);
	public static string GetPlayerColorName(int playerIndex) => PlayerColorConfig.GetName(playerIndex);

	private List<MapBriefingDetails> _lobbyRoomMaps = new List<MapBriefingDetails>();
	private readonly string[] _runes = { "ᚠ", "ᚢ", "ᚦ", "ᚨ", "ᚱ", "ᚲ", "ᚷ", "ᚹ", "ᚺ", "ᚾ", "ᛁ", "ᛃ", "ᛇ", "ᛈ", "ᛉ", "ᛊ", "ᛏ", "ᛒ", "ᛖ", "ᛗ", "ᛚ", "ᛜ", "ᛞ", "ᛟ" };
	private const int ReadyColumnWidth = 82;
	private const int TeamColumnWidth = 126;
	private const int ColorColumnWidth = 70;
	private const int SlotColumnWidth = 104;
	private const int NameColumnWidth = 226;
	private const int FactionColumnWidth = 98;
	private const int BootColumnWidth = 100;
	private const int RowColumnSeparation = 10;
	private const int RowContentInset = 8;

	/// <summary>
	///     The width the player column takes: what the row has left once the columns that are always there have taken
	///     theirs. A map that offers factions spends that width on them, and a map that offers none leaves the player's
	///     name the room instead.
	/// </summary>
	private static int NameCellWidth => HasFactions ? NameColumnWidth : NameColumnWidth + FactionColumnWidth + RowColumnSeparation;

	private static IReadOnlyList<string> FactionList => GameContext.Lobby?.LobbyFactions ?? Array.Empty<string>();

	private static bool HasFactions => FactionList.Count > 0;

	private static int TeamCount => Math.Max(1, GameContext.Lobby?.LobbyTeamCount ?? LobbyLimits.DefaultTeamCount);


	public override void _Ready()
	{
		(_bgPanel, _leftPillar, _rightPillar) = UIStyle.BindStandardChrome(this);
		_playersPanel = GetNode<PanelContainer>("PlayersPanel");
		_briefingPanel = GetNode<PanelContainer>("BriefingPanel");
		_chatPanel = GetNode<PanelContainer>("ChatPanel");


		_backButton = GetNode<Button>("BackButton");
		_steamInviteButton = GetNode<Button>("SteamInviteButton");
		_shareAddressButton = GetNode<Button>("ShareAddressButton");
		_privateLobbyHint = GetNode<Label>("PrivateLobbyHint");
		_startButton = GetNode<Button>("StartButton");
		_privateLobbyHint.Text = TranslationServer.Translate("Private Lobby: Send your IP address to players so they can join.");
		_lobbyFamiliesHint = GetNode<Label>("LobbyFamiliesHint");
		UpdateLobbyFamiliesHint();
		ApplyInviteVisibility();
		if (Lobby.IsHost)
		{
			CreateHostNetworkButtons();

			// A lobby that could not be shown reachable stays open to the players it is given the address to, so the host is
			// told what to change at the moment the listing was refused rather than after somebody has failed to get in.
			Lobby.LobbyPublicationRefused += ShowConnectivityHelp;
		}


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

		Lobby.MatchSettingsChanged += OnMatchSettingsChanged;
		Lobby.TeamsTogetherChanged += OnTeamsTogetherChanged;
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

		SetupAttributionButton();
		CreateMatchSettingsUI();

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



	/// <summary>
	///     Whether this lobby is played at an address its host sends around instead of being listed for the lobby browser
	///     to find. The staging id the authority opens a direct lobby under states the same thing as the flag, and a screen
	///     that joined by address reads it from there.
	/// </summary>
	private static bool IsDirectConnectLobby =>
		Lobby.IsDirectLobby ||
		(Lobby.ActiveLobbyId is { Length: > 0 } activeLobbyId && activeLobbyId.StartsWith("direct-", StringComparison.Ordinal));

	/// <summary>
	///     Shows the one invite button this lobby can use. A Steam invite needs a lobby the lobby browser lists, because that
	///     list is what a friend's client finds and joins; a lobby that is not listed hands out the address its host reads
	///     from the dialog instead. The note beside that button is the host's, since an address a joining player passes on is
	///     the host's and not their own.
	/// </summary>
	/// <summary>
	///     Says which address families this lobby can be joined over, which is the only way a host can tell that its lobby
	///     is reachable over IPv6 and that the players it matters to can reach it. The sentence is drawn through a renderer
	///     because it is built from live state, so a language change redraws it rather than leaving the language it was
	///     first drawn in on screen.
	/// </summary>
	private void UpdateLobbyFamiliesHint()
	{
		if (!GodotObject.IsInstanceValid(_lobbyFamiliesHint)) return;

		LocalizedText.Bind(_lobbyFamiliesHint, RenderLobbyFamiliesHint);
		_lobbyFamiliesHint.Visible = RenderLobbyFamiliesHint() != null;
	}

	private string? RenderLobbyFamiliesHint()
	{
		(bool offersIpv4, bool offersIpv6) = DescribeOfferedFamilies();
		if (offersIpv4 && offersIpv6)
		{
			return TranslationServer.Translate("This lobby is reachable over IPv6 and IPv4, so players on either can join it.");
		}

		if (offersIpv6)
		{
			return TranslationServer.Translate("This lobby is reachable over IPv6 only. Players without a global IPv6 address cannot join it.");
		}

		return null;
	}

	/// <summary>
	///     Which families this screen's lobby is offered over. A listed lobby states the endpoints a registry node reached;
	///     one that is not listed is judged by the addresses this machine holds, which is what its invite tells players to
	///     dial.
	/// </summary>
	/// <returns>Whether IPv4 and whether IPv6 are offered.</returns>
	private (bool OffersIpv4, bool OffersIpv6) DescribeOfferedFamilies()
	{
		bool offersIpv4 = false;
		bool offersIpv6 = false;

		IReadOnlyList<LobbyEndpointInfo> published = Lobby.PublishedEndpoints;
		for (int index = 0; index < published.Count; index++)
		{
			LobbyAddressFamily family = published[index].FamilyValue;
			if (family == LobbyAddressFamily.IPv4) offersIpv4 = true;
			if (family == LobbyAddressFamily.IPv6) offersIpv6 = true;
		}

		if (offersIpv4 || offersIpv6) return (offersIpv4, offersIpv6);
		if (!Lobby.IsHost) return (Lobby.JoinedLobbyFamily != LobbyAddressFamily.IPv6, Lobby.JoinedLobbyFamily == LobbyAddressFamily.IPv6);

		// Read once: two readings of the machine's addresses can disagree when an interface changes between them, and a hint
		// that states a family the row beneath it does not offer is worse than the sentence it was meant to explain.
		List<LobbyEndpointCandidate> held = LocalEndpointDiscovery.Discover();
		return (held.Exists(candidate => candidate.Family == LobbyAddressFamily.IPv4), held.Exists(candidate => candidate.Family == LobbyAddressFamily.IPv6));
	}

	private void ApplyInviteVisibility()
	{
		bool invitesPlayers = !Lobby.IsSinglePlayer;
		bool directLobby = IsDirectConnectLobby;

		_steamInviteButton.Visible = invitesPlayers && !directLobby;
		_shareAddressButton.Visible = invitesPlayers && directLobby;
		_privateLobbyHint.Visible = invitesPlayers && directLobby && Lobby.IsHost;
	}

	private void OpenInviteFriendsDialog()
	{
		if (Lobby.ActiveLobbyId is not { Length: > 0 } lobbyId)
		{
			GameContext.UserInterface?.ShowPopupMessage(
				Tr("LOBBY INVITES"),
				Tr("Invites need a lobby the server lists. Share the lobby's direct connect address instead."));
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
			Lobby.TeamsTogetherChanged -= OnTeamsTogetherChanged;
			Lobby.MatchSettingsChanged -= OnMatchSettingsChanged;

			Lobby.MapDownloadProgressChanged -= OnMapDownloadProgress;
			Lobby.MapDownloadCompleted -= OnMapDownloadCompleted;
			Lobby.MapDownloadFailed -= OnMapDownloadFailed;
			CloseMatchSettingsPanel();

			Lobby.CountdownStarted -= OnCountdownStarted;
			Lobby.CountdownTick -= OnCountdownTick;
			Lobby.CountdownCancelled -= OnCountdownCancelled;
			Lobby.CountdownFinished -= OnCountdownFinished;
			Lobby.HostStabilityUpdated -= UpdateHostStabilityLabel;
			Lobby.LobbyPublicationRefused -= ShowConnectivityHelp;
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
		int slotCount = Lobby.SlotCount;

		for (int slot = 1; slot <= slotCount; slot++)
		{
			PlayerInfo? player = FindPlayerInSlot(slot);
			if (existingRows.TryGetValue(slot, out var row))
			{
				UpdatePlayerRow(row, slot, player);
				_playersContainer.MoveChild(row, slot - 1);
			}
			else
			{
				var newRow = CreatePlayerRow(slot, player);
				newRow.Name = $"PlayerRow_Slot{slot}";
				_playersContainer.AddChild(newRow);
				_playersContainer.MoveChild(newRow, slot - 1);
				existingRows[slot] = newRow;
				_playerRowsBySlot[slot] = newRow;
			}
		}

		ReleaseStalePlayerRows(existingRows, slotCount);

		UpdatePlayersHeading();
		SetupStartButton();
	}

	private void UpdatePlayersHeading()
	{
		_playersTitle.Text = $"{TranslationServer.Translate("TEAMS & PLAYERS")} ({Lobby.PlayingPlayerCount}/{Lobby.OpenSlotCount})";
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
		var existingRows = new Dictionary<int, PanelContainer>(_playerRowsBySlot.Count);
		foreach (var pair in _playerRowsBySlot)
		{
			if (!GodotObject.IsInstanceValid(pair.Value)) continue;
			existingRows[pair.Key] = pair.Value;
		}

		_playerRowsBySlot.Clear();
		foreach (var pair in existingRows) _playerRowsBySlot[pair.Key] = pair.Value;

		return existingRows;
	}

	private void ReleasePlayerRow(PanelContainer row)
	{
		if (!GodotObject.IsInstanceValid(row)) return;

		if (row.GetParent() == _playersContainer) _playersContainer.RemoveChild(row);
		row.QueueFree();
	}

	/// <summary>Releases every row above the number of slots the lobby now carries.</summary>
	/// <param name="existingRows">The rows that were on screen, keyed by slot.</param>
	/// <param name="slotCount">The number of slots the lobby carries.</param>
	private void ReleaseStalePlayerRows(Dictionary<int, PanelContainer> existingRows, int slotCount)
	{
		var stale = new List<int>();
		foreach (var pair in existingRows)
		{
			if (pair.Key <= slotCount) continue;
			stale.Add(pair.Key);
		}

		for (int index = 0; index < stale.Count; index++)
		{
			_playerRowsBySlot.Remove(stale[index]);
			ReleasePlayerRow(existingRows[stale[index]]);
		}
	}

	/// <summary>Answers the seat one player slot holds, or nothing while nobody sits in it.</summary>
	/// <param name="slot">The player slot.</param>
	/// <returns>The seat in that slot, when one is seated.</returns>
	private static PlayerInfo? FindPlayerInSlot(int slot)
	{
		return Lobby.PlayerList.Find(candidate => candidate.Slot == slot);
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

	/// <summary>
	///     Explains a join that failed with the same connectivity help a host's refusal is explained with, on the side of the
	///     connection that failed. A joining screen is told what the game measured, which of the causes could be its own and
	///     which are usually the host's, and where to read about each - and closing it returns to the lobby list, which is what
	///     a player who cannot get in wants next.
	/// </summary>
	/// <param name="reason">What the lobby said went wrong, kept for the log and for the popup this falls back to.</param>
	private void ShowConnectionFailedPopup(string reason)
	{
		// A failure that is not about the network - an authority that is gone, a lobby that refused this seat, a seam in the
		// game - is stated as it arrived: sending that player to read about routers would describe a problem they do not have.
		string phase = Lobby.LastJoinPhase;
		bool networkFailure = JoinPhases.IsKnown(phase) && phase != JoinPhases.Admission && phase != JoinPhases.Session;

		if (Lobby.IsHost || !networkFailure)
		{
			GameContext.UserInterface?.ShowPopupMessage(Tr("CONNECTION FAILED"), $"{Tr("Failed to connect to host.")}\n\n{string.Format(Tr("Reason: {0}"), reason)}", ReturnFromLobby);
			return;
		}

		var dialog = new ConnectivityHelpDialog { Name = "JoinFailureHelpDialog" };
		AddChild(dialog);
		dialog.Closed += ReturnFromLobby;
		dialog.Open(new ConnectivityHelpRequest
		{
			Audience = ConnectivityAudience.Joining,
			TitleKey = ConnectivitySentences.JoiningTitle,
			DiagnosticsBlock = Lobby.DescribeNetworkDiagnostics
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


	}

	/// <summary>
	///     Offers the lobby's host the one button the match settings are reached through. The settings themselves are the
	///     panel's, and it is the same panel the lobby was confirmed with before it was published, so the two places a host
	///     meets them cannot drift apart.
	/// </summary>
	private void CreateMatchSettingsUI()
	{
		if (!GameContext.Lobby.IsHost) return;

		_matchSettingsButton = new Button();
		_matchSettingsButton.Name = "MatchSettingsButton_Button";
		_matchSettingsButton.CustomMinimumSize = new Vector2(280, 36);
		_matchSettingsButton.SizeFlagsHorizontal = Control.SizeFlags.ShrinkCenter;
		_matchSettingsButton.AddThemeConstantOverride("icon_max_width", 20);
		_matchSettingsButton.AddThemeStyleboxOverride("normal", UIStyle.CreateButtonNormal());
		_matchSettingsButton.AddThemeStyleboxOverride("hover", UIStyle.CreateButtonHover());
		_matchSettingsButton.AddThemeStyleboxOverride("pressed", UIStyle.CreateButtonPressed());
		_matchSettingsButton.AddThemeStyleboxOverride("focus", new StyleBoxEmpty());
		UIStyle.ApplyButtonText(_matchSettingsButton, TranslationServer.Translate("Match Settings"), 16);
		_matchSettingsButton.TooltipText = TranslationServer.Translate("Set the shroud, the teams and the spectator delay this match plays with, and the rules this map adds to them.");
		_matchSettingsButton.Pressed += () =>
		{
			GameContext.UserInterface.PlayClickSound();
			ToggleMatchSettingsPopup();
		};
		_matchSettingsButton.MouseEntered += () => GameContext.UserInterface.PlayHoverSound();
		GetNode<VBoxContainer>("BriefingPanel/VBoxContainer").AddChild(_matchSettingsButton);
	}

	private void ToggleMatchSettingsPopup()
	{
		if (_matchSettingsPanel != null && GodotObject.IsInstanceValid(_matchSettingsPanel))
		{
			CloseMatchSettingsPanel();
			return;
		}

		_matchSettingsPanel = new MatchSettingsPanel();
		_matchSettingsPanel.Closed += CloseMatchSettingsPanel;
		AddChild(_matchSettingsPanel);
		_matchSettingsPanel.Open();
	}

	private void CloseMatchSettingsPanel()
	{
		if (_matchSettingsPanel == null) return;

		if (GodotObject.IsInstanceValid(_matchSettingsPanel)) _matchSettingsPanel.QueueFree();
		_matchSettingsPanel = null;
	}

	/// <summary>
	///     A setting the lobby changed is redrawn in the panel, which is the only surface those settings are edited on: the
	///     host's own change and another seat's arrive the same way, so the panel always reads what the match will play.
	/// </summary>
	private void OnMatchSettingsChanged()
	{
		if (_matchSettingsPanel != null && GodotObject.IsInstanceValid(_matchSettingsPanel)) _matchSettingsPanel.RefreshFromLobby();
	}

	private void OnTeamsTogetherChanged(bool enabled)
	{
		if (_matchSettingsPanel != null && GodotObject.IsInstanceValid(_matchSettingsPanel)) _matchSettingsPanel.RefreshFromLobby();
	}

	private void OnSpectatorDelayChanged(bool enabled)
	{
		if (_matchSettingsPanel != null && GodotObject.IsInstanceValid(_matchSettingsPanel)) _matchSettingsPanel.RefreshFromLobby();
	}
}
