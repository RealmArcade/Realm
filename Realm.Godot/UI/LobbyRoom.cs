using Godot;
using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Nodes;
using NSec.Cryptography;
using System.Threading.Tasks;
using Realm.Godot;
using Realm.Shared;
using Realm.Shared.Metadata;


public partial class LobbyRoom : Control
{
	private Panel _bgPanel;
	private Panel _leftPillar;
	private Panel _rightPillar;
	private PanelContainer _playersPanel;
	private PanelContainer _briefingPanel;
	private PanelContainer _chatPanel;

	private Button _backButton;
	private Button _inviteButton;
	private Button _addBotButton;
	private Button _startButton;
	private Label _mapNameLabel;
	private LineEdit _chatInput;
	private RichTextLabel _chatLog;
	private VBoxContainer _playersContainer;
	private Label _inviteFeedback;
	private CheckBox _spectatorDelayCheck;
	private Panel? _countdownPopup;
	private Label? _countdownTextLabel;
	private Panel _connectingPopup;

	private Label _lobbyTitle;
	private Label _playersTitle;
	private Label _briefingTitle;
	private RichTextLabel _briefingLabel;
	private Panel _mapFrame;
	private bool _versionMismatchDetected;
	private Label? _unstableWarningLabel;
	private Label? _hostStabilityLabel;

	private Label _authorshipWarningLabel;
	private Label _primaryAuthorLabel;
	private Button _otherAuthorsButton;
	private List<string> _otherAuthorsList = new List<string>();


	private static readonly System.Net.Http.HttpClient _sharedHttpClient = new System.Net.Http.HttpClient();
	public const int NEUTRAL_PLAYER_INDEX = PlayerColorConfig.NEUTRAL_PLAYER_INDEX;
	public static List<Color> AvailableColors => PlayerColorConfig.AvailableColors;
	public static Color GetPlayerColor(int playerIndex) => PlayerColorConfig.GetColor(playerIndex);
	public static string GetPlayerColorName(int playerIndex) => PlayerColorConfig.GetName(playerIndex);

	private List<MapBriefingDetails> _lobbyRoomMaps = new List<MapBriefingDetails>();
	private readonly string[] _runes = { "ᚠ", "ᚢ", "ᚦ", "ᚨ", "ᚱ", "ᚲ", "ᚷ", "ᚹ", "ᚺ", "ᚾ", "ᛁ", "ᛃ", "ᛇ", "ᛈ", "ᛉ", "ᛊ", "ᛏ", "ᛒ", "ᛖ", "ᛗ", "ᛚ", "ᛜ", "ᛞ", "ᛟ" };
	private readonly string[] _factions = { "HUMAN", "ORC", "UNDEAD", "ELF" };

	public override void _Ready()
	{

		_bgPanel = GetNode<Panel>("Background");
		_leftPillar = GetNode<Panel>("LeftPillar");
		_rightPillar = GetNode<Panel>("RightPillar");
		_playersPanel = GetNode<PanelContainer>("PlayersPanel");
		_briefingPanel = GetNode<PanelContainer>("BriefingPanel");
		_chatPanel = GetNode<PanelContainer>("ChatPanel");


		_backButton = GetNode<Button>("BackButton");
		_inviteButton = GetNode<Button>("InviteButton");
		_addBotButton = GetNode<Button>("AddBotButton");
		_startButton = GetNode<Button>("StartButton");
		_addBotButton.Visible = LobbyManager.Instance.IsHost;


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
		_hostStabilityLabel.MouseFilter = Control.MouseFilterEnum.Pass;
		_hostStabilityLabel.TooltipText = Tr("Average over last 10 games");
		AddChild(_hostStabilityLabel);
		_hostStabilityLabel.SetAnchorsPreset(LayoutPreset.CenterTop);
		_hostStabilityLabel.GrowHorizontal = GrowDirection.Both;
		_hostStabilityLabel.OffsetLeft = -300;
		_hostStabilityLabel.OffsetRight = 300;
		_hostStabilityLabel.OffsetTop = 85;
		_hostStabilityLabel.OffsetBottom = 110;

		if (!LobbyManager.Instance.IsHost)
		{
			UpdateHostStabilityLabel(LobbyManager.Instance.HostStability);
			LobbyManager.Instance.HostStabilityUpdated += UpdateHostStabilityLabel;
		}


		ApplyThemeStyles();


		LobbyManager.Instance.PlayerListUpdated += PopulatePlayersList;
		LobbyManager.Instance.ChatReceived += OnLobbyChatReceived;
		LobbyManager.Instance.ConnectionFailed += OnLobbyConnectionFailed;
		LobbyManager.Instance.KickReceived += OnLobbyKickReceived;
		LobbyManager.Instance.CountdownStarted += OnCountdownStarted;
		LobbyManager.Instance.CountdownTick += OnCountdownTick;
		LobbyManager.Instance.CountdownCancelled += OnCountdownCancelled;
		LobbyManager.Instance.CountdownFinished += OnCountdownFinished;
		LobbyManager.Instance.ActiveMapChanged += OnActiveMapChanged;


		if (!LobbyManager.Instance.IsHost)
		{
			CreateDownloadProgressUI();
			LobbyManager.Instance.RequestChatHistory();
		}

		PopulatePlayersList();

		if (!LobbyManager.Instance.IsHost)
		{
			ShowConnectingPopup();
		}

		CreateSpectatorDelayUI();
		LobbyManager.Instance.SpectatorDelayChanged += OnSpectatorDelayChanged;

		_chatLog.Text = $"[color=#ffd700]{Tr("System: Connected to Lobby. Pre-Match Setup is active.")}[/color]\n";
		_chatInput.TextSubmitted += OnChatSubmitted;


		var mapImage = new TextureRect();
		mapImage.Texture = GD.Load<Texture2D>("res://Assets/UI/snowy_forest_path.png");
		mapImage.ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize;
		mapImage.StretchMode = TextureRect.StretchModeEnum.KeepAspectCovered;
		_mapFrame.AddChild(mapImage);
		mapImage.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);


		var mapDrawer = new TacticalMap();
		_mapFrame.AddChild(mapDrawer);
		mapDrawer.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);

		
		_authorshipWarningLabel = new Label();
		_authorshipWarningLabel.Text = "⚠️ " + Tr("Unable to verify map authorship");
		_authorshipWarningLabel.AddThemeColorOverride("font_color", new Color(1, 0.4f, 0.4f));
		_authorshipWarningLabel.AddThemeFontSizeOverride("font_size", 12);
		_authorshipWarningLabel.HorizontalAlignment = HorizontalAlignment.Center;
		_authorshipWarningLabel.Visible = false;
		GetNode<VBoxContainer>("BriefingPanel/VBoxContainer").AddChild(_authorshipWarningLabel);
		
		_primaryAuthorLabel = new Label();
		_primaryAuthorLabel.Text = Tr("Author: Unknown");
		_primaryAuthorLabel.AddThemeColorOverride("font_color", UIStyle.ColorGoldDull);
		_primaryAuthorLabel.AddThemeFontSizeOverride("font_size", 12);
		_primaryAuthorLabel.HorizontalAlignment = HorizontalAlignment.Center;
		GetNode<VBoxContainer>("BriefingPanel/VBoxContainer").AddChild(_primaryAuthorLabel);
		
		var authorsSpacer = new Control { CustomMinimumSize = new Vector2(0, 24) };
		GetNode<VBoxContainer>("BriefingPanel/VBoxContainer").AddChild(authorsSpacer);

		_otherAuthorsButton = new Button();
		_otherAuthorsButton.Text = Tr("Other Authors");
		_otherAuthorsButton.CustomMinimumSize = new Vector2(160, 32);
		_otherAuthorsButton.SizeFlagsHorizontal = Control.SizeFlags.ShrinkCenter;
		_otherAuthorsButton.AddThemeStyleboxOverride("normal", UIStyle.CreateButtonNormal());
		_otherAuthorsButton.AddThemeStyleboxOverride("hover", UIStyle.CreateButtonHover());
		_otherAuthorsButton.AddThemeStyleboxOverride("pressed", UIStyle.CreateButtonPressed());
		_otherAuthorsButton.Pressed += () => {
			if (_otherAuthorsList.Count > 0) {
				ShowConfirmationDialog(Tr("Contributors:") + "\n" + string.Join("\n", _otherAuthorsList), null);
			} else {
				ShowConfirmationDialog(Tr("No other contributors listed."), null);
			}
		};
		GetNode<VBoxContainer>("BriefingPanel/VBoxContainer").AddChild(_otherAuthorsButton);

		UpdateSelectedMapUI();
	}

	private void ApplyThemeStyles()
	{
		_bgPanel.TextureFilter = CanvasItem.TextureFilterEnum.LinearWithMipmaps;
		_bgPanel.AddThemeStyleboxOverride("panel", UIStyle.CreateLobbyRoomBg());
		_leftPillar.AddThemeStyleboxOverride("panel", UIStyle.CreatePillarPanel(true));
		_rightPillar.AddThemeStyleboxOverride("panel", UIStyle.CreatePillarPanel(false));
		_playersPanel.TextureFilter = CanvasItem.TextureFilterEnum.LinearWithMipmaps;
		_playersPanel.AddThemeStyleboxOverride("panel", UIStyle.CreateLobbyRoomMainPanel());
		_briefingPanel.TextureFilter = CanvasItem.TextureFilterEnum.LinearWithMipmaps;
		_briefingPanel.AddThemeStyleboxOverride("panel", UIStyle.CreateLobbyRoomSecondPanel());
		_chatPanel.TextureFilter = CanvasItem.TextureFilterEnum.LinearWithMipmaps;
		_chatPanel.AddThemeStyleboxOverride("panel", UIStyle.CreateLobbyRoomChatPanel());


		UIStyle.ApplyTitle(_lobbyTitle, "PRE-MATCH SETUP", 36);
		UIStyle.ApplyTitle(_playersTitle, "TEAMS & PLAYERS", 20);
		UIStyle.ApplyTitle(_briefingTitle, "MATCH INFO & BRIEFING", 20);

		var playersVBox = GetNodeOrNull<VBoxContainer>("PlayersPanel/VBoxContainer");
		if (playersVBox != null)
		{
			playersVBox.AddThemeConstantOverride("separation", 16);
		}

		_briefingLabel.AddThemeColorOverride("default_color", new Color(0.85f, 0.85f, 0.9f));


		string[] headers = { "PlayersPanel/VBoxContainer/TableHeader/ReadyCol",
							 "PlayersPanel/VBoxContainer/TableHeader/TeamCol",
							 "PlayersPanel/VBoxContainer/TableHeader/ColorCol", 
							 "PlayersPanel/VBoxContainer/TableHeader/NameCol",
							 "PlayersPanel/VBoxContainer/TableHeader/FactionCol" };
		foreach (var path in headers)
		{
			var lbl = GetNodeOrNull<Label>(path);
			if (lbl != null)
			{
				lbl.Text = Tr(lbl.Text);
				lbl.AddThemeColorOverride("font_color", UIStyle.ColorGold);
				lbl.AddThemeFontSizeOverride("font_size", 15);
			}
		}


		_inviteFeedback.Modulate = new Color(1, 1, 1, 0);


		SetupLobbyButton(_backButton, " LEAVE LOBBY", () => 
		{
			LobbyManager.Instance.Disconnect();
			UIManager.Instance.TransitionTo(GameScreen.LobbyBrowser);
		});
		SetupLobbyButton(_inviteButton, "INVITE FRIEND", TriggerInviteFeedback);
		SetupLobbyButton(_addBotButton, "ADD AI BOT", () => LobbyManager.Instance.AddAIBot());
		SetupStartButton();

		_mapNameLabel.AddThemeColorOverride("font_color", UIStyle.ColorGold);
		_mapNameLabel.AddThemeFontSizeOverride("font_size", 18);
		_mapNameLabel.HorizontalAlignment = HorizontalAlignment.Center;
		_mapNameLabel.VerticalAlignment = VerticalAlignment.Center;
		
		var labelStyle = new StyleBoxFlat();
		labelStyle.BgColor = new Color(0.12f, 0.13f, 0.16f, 0.5f);
		labelStyle.BorderColor = new Color(0.25f, 0.25f, 0.3f, 0.4f);
		labelStyle.SetBorderWidthAll(1);
		_mapNameLabel.AddThemeStyleboxOverride("normal", labelStyle);


		var logStyle = new StyleBoxFlat();
		logStyle.BgColor = new Color(0.08f, 0.08f, 0.1f, 0.7f);
		logStyle.BorderColor = new Color(0.2f, 0.2f, 0.25f, 0.3f);
		logStyle.SetBorderWidthAll(1);
		logStyle.ContentMarginLeft = 50;
		logStyle.ContentMarginRight = 50;
		logStyle.ContentMarginTop = 8;
		logStyle.ContentMarginBottom = 8;
		_chatLog.AddThemeStyleboxOverride("normal", logStyle);

		var inputNormal = UIStyle.CreateTextInput(false);
		inputNormal.ContentMarginLeft = 14;
		inputNormal.ContentMarginRight = 14;
		var inputFocus = UIStyle.CreateTextInput(true);
		inputFocus.ContentMarginLeft = 14;
		inputFocus.ContentMarginRight = 14;
		_chatInput.AddThemeStyleboxOverride("normal", inputNormal);
		_chatInput.AddThemeStyleboxOverride("focus", inputFocus);
		_chatInput.AddThemeColorOverride("font_color", new Color(0.9f, 0.85f, 0.7f));


		PopulateRunicPillar(GetNode<VBoxContainer>("LeftPillar/RuneContainer"));
		PopulateRunicPillar(GetNode<VBoxContainer>("RightPillar/RuneContainer"));
	}

	private void SetupLobbyButton(Button btn, string text, Action onClick)
	{
		btn.Flat = false;
		btn.AddThemeConstantOverride("icon_max_width", 0);
		UIStyle.ApplyButtonText(btn, text, 15);

		btn.AddThemeStyleboxOverride("normal", UIStyle.CreateButtonNormal());
		btn.AddThemeStyleboxOverride("hover", UIStyle.CreateButtonHover());
		btn.AddThemeStyleboxOverride("pressed", UIStyle.CreateButtonPressed());
		btn.AddThemeStyleboxOverride("focus", new StyleBoxEmpty());

		btn.Pressed += () => 
		{
			UIManager.Instance.PlayClickSound();
			onClick?.Invoke();
		};
		btn.MouseEntered += () => UIManager.Instance.PlayHoverSound();
	}

	private void SetupStartButton()
	{
		_startButton.Flat = false;

		var normStyle = UIStyle.CreateButtonNormal();
		if (normStyle is StyleBoxFlat flatNorm)
		{
			flatNorm.BorderColor = UIStyle.ColorCyanGlow;
		}

		var hoverStyle = UIStyle.CreateButtonHover();
		if (hoverStyle is StyleBoxFlat flatHover)
		{
			flatHover.BgColor = new Color(0.1f, 0.5f, 0.9f, 0.15f);
			flatHover.BorderColor = UIStyle.ColorCyanGlow;
			flatHover.ShadowColor = UIStyle.ColorCyanGlow;
			flatHover.ShadowSize = 10;
		}

		var pressedStyle = UIStyle.CreateButtonPressed();
		if (pressedStyle is StyleBoxFlat flatPressed)
		{
			flatPressed.BgColor = new Color(0.1f, 0.5f, 0.9f, 0.3f);
		}

		_startButton.CustomMinimumSize = new Vector2(300, 86);
		_startButton.AddThemeStyleboxOverride("normal", UIStyle.CreateCustomLobbyStartGameButton(false, false));
		_startButton.AddThemeStyleboxOverride("hover", UIStyle.CreateCustomLobbyStartGameButton(true, false));
		_startButton.AddThemeStyleboxOverride("pressed", UIStyle.CreateCustomLobbyStartGameButton(false, true));
		_startButton.AddThemeStyleboxOverride("focus", new StyleBoxEmpty());

		var pressedCallable = Callable.From(OnStartPressed);
		if (_startButton.IsConnected(Button.SignalName.Pressed, pressedCallable))
		{
			_startButton.Disconnect(Button.SignalName.Pressed, pressedCallable);
		}

		var readyToggleCallable = Callable.From(OnClientReadyTogglePressed);
		if (_startButton.IsConnected(Button.SignalName.Pressed, readyToggleCallable))
		{
			_startButton.Disconnect(Button.SignalName.Pressed, readyToggleCallable);
		}
		
		if (LobbyManager.Instance.IsHost)
		{
			UIStyle.ApplyButtonText(_startButton, "START GAME", 22);
			_startButton.Disabled = false;
			_startButton.Pressed += OnStartPressed;
		}
		else
		{
			string activeMap = LobbyManager.Instance.ActiveMapName ?? "";
			bool isMapReady = !string.IsNullOrEmpty(activeMap) && MapAssetManager.IsMapDownloaded(activeMap);
			if (!isMapReady)
			{
				UIStyle.ApplyButtonText(_startButton, "DOWNLOADING MAP...", 18);
				_startButton.Disabled = true;
			}
			else
			{
				var localPlayer = LobbyManager.Instance.LocalPlayer;
				bool isReady = localPlayer != null && localPlayer.IsReady;
				UIStyle.ApplyButtonText(_startButton, isReady ? "NOT READY" : "READY", 22);
				_startButton.Disabled = false;
				_startButton.Pressed += OnClientReadyTogglePressed;
			}
		}
		
		var mouseEnteredCallable = Callable.From(OnStartButtonMouseEntered);
		if (_startButton.IsConnected(Control.SignalName.MouseEntered, mouseEnteredCallable))
		{
			_startButton.Disconnect(Control.SignalName.MouseEntered, mouseEnteredCallable);
		}
		_startButton.MouseEntered += OnStartButtonMouseEntered;
	}

	private void OnClientReadyTogglePressed()
	{
		UIManager.Instance.PlayClickSound();
		var localPlayer = LobbyManager.Instance.LocalPlayer;
		if (localPlayer != null)
		{
			bool newReady = !localPlayer.IsReady;
			localPlayer.IsReady = newReady;
			LobbyManager.Instance.UpdateReadyState(localPlayer.PeerId, newReady);
			SetupStartButton();
		}
	}

	
	private void ShowConfirmationDialog(string message, Action onConfirm)
	{
		var overlay = new ColorRect();
		overlay.Color = new Color(0, 0, 0, 0.5f);
		overlay.SetAnchorsPreset(LayoutPreset.FullRect);
		AddChild(overlay);

		var panel = new PanelContainer();
		panel.CustomMinimumSize = new Vector2(400, 200);
		panel.SizeFlagsHorizontal = SizeFlags.ShrinkCenter;
		panel.SizeFlagsVertical = SizeFlags.ShrinkCenter;
		
		var center = new CenterContainer();
		center.SetAnchorsPreset(LayoutPreset.FullRect);
		overlay.AddChild(center);
		center.AddChild(panel);

		var vbox = new VBoxContainer();
		panel.AddChild(vbox);

		var lblMsg = new Label();
		lblMsg.Text = message;
		lblMsg.AutowrapMode = TextServer.AutowrapMode.WordSmart;
		vbox.AddChild(lblMsg);

		var btnConfirm = new Button();
		btnConfirm.Text = "OK";
		btnConfirm.Pressed += () => {
			overlay.QueueFree();
			onConfirm?.Invoke();
		};
		vbox.AddChild(btnConfirm);
	}

	private string FormatMapDisplayName(string rawName)
	{
		if (string.IsNullOrEmpty(rawName))
		{
			return "";
		}
		string formatted = rawName.Replace('_', ' ');
		string[] words = formatted.Split(' ', StringSplitOptions.RemoveEmptyEntries);
		for (int i = 0; i < words.Length; i++)
		{
			if (words[i].Equals("td", StringComparison.OrdinalIgnoreCase))
			{
				words[i] = "TD";
			}
			else if (words[i].Length > 0)
			{
				words[i] = char.ToUpper(words[i][0]) + words[i].Substring(1).ToLower();
			}
		}
		return string.Join(" ", words);
	}

	private void OnStartPressed()
	{
		UIManager.Instance.PlayClickSound();
		string mapName = LobbyManager.Instance.ActiveMapName ?? "melee";

		bool anyDownloading = false;
		foreach (var player in LobbyManager.Instance.PlayerList)
		{
			if (player.PeerId > 1 && !player.IsMapReady)
			{
				anyDownloading = true;
				break;
			}
		}

		if (anyDownloading)
		{
			ShowDownloadingClientsPopup();
			return;
		}

		bool anyNotReady = false;
		foreach (var player in LobbyManager.Instance.PlayerList)
		{
			if (player.PeerId > 1 && !player.IsReady)
			{
				anyNotReady = true;
				break;
			}
		}

		if (anyNotReady)
		{
			ShowStartWarningPopup(mapName);
		}
		else
		{
			LobbyManager.Instance.StartGame(mapName);
		}
	}

	private void ShowVersionMismatchPopup(string hostVersion, string clientVersion)
	{
		var warningPopup = new Panel();
		warningPopup.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
		warningPopup.AddThemeStyleboxOverride("panel", UIStyle.CreateBgGradient());
		AddChild(warningPopup);

		var cardPanel = new Panel();
		cardPanel.CustomMinimumSize = new Vector2(450, 220);
		cardPanel.SetAnchorsAndOffsetsPreset(LayoutPreset.Center);
		cardPanel.AddThemeStyleboxOverride("panel", UIStyle.CreateStonePanel(true));
		warningPopup.AddChild(cardPanel);

		var vbox = new VBoxContainer();
		vbox.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
		vbox.CustomMinimumSize = new Vector2(400, 180);
		vbox.SizeFlagsHorizontal = SizeFlags.ExpandFill;
		vbox.SizeFlagsVertical = SizeFlags.ExpandFill;
		cardPanel.AddChild(vbox);

		vbox.AddChild(new Control { CustomMinimumSize = new Vector2(0, 20) });

		var titleLabel = new Label();
		UIStyle.ApplyTitle(titleLabel, Tr("VERSION MISMATCH"), 20);
		titleLabel.AddThemeColorOverride("font_color", new Color(0.9f, 0.3f, 0.3f));
		vbox.AddChild(titleLabel);

		vbox.AddChild(new Control { CustomMinimumSize = new Vector2(0, 10) });

		var descLabel = new Label();
		descLabel.Text = $"{string.Format(Tr("Host Version: {0}"), hostVersion)}\n{string.Format(Tr("Client Version: {0}"), clientVersion)}\n\n{Tr("Connection aborted.")}";
		descLabel.HorizontalAlignment = HorizontalAlignment.Center;
		descLabel.AddThemeFontSizeOverride("font_size", 14);
		descLabel.AddThemeColorOverride("font_color", new Color(0.9f, 0.9f, 0.95f));
		vbox.AddChild(descLabel);

		vbox.AddChild(new Control { CustomMinimumSize = new Vector2(0, 15) });

		var okBtn = new Button();
		okBtn.Flat = false;
		okBtn.AddThemeConstantOverride("icon_max_width", 0);
		okBtn.AddThemeStyleboxOverride("normal", UIStyle.CreateButtonNormal());
		okBtn.AddThemeStyleboxOverride("hover", UIStyle.CreateButtonHover());
		okBtn.AddThemeStyleboxOverride("pressed", UIStyle.CreateButtonPressed());
		okBtn.AddThemeStyleboxOverride("focus", new StyleBoxEmpty());
		UIStyle.ApplyButtonText(okBtn, Tr("OK"), 14);
		okBtn.CustomMinimumSize = new Vector2(160, 40);
		okBtn.SizeFlagsHorizontal = SizeFlags.ShrinkCenter;
		okBtn.Pressed += () =>
		{
			UIManager.Instance.PlayClickSound();
			warningPopup.QueueFree();
			LobbyManager.Instance.Disconnect();
			UIManager.Instance.TransitionTo(GameScreen.LobbyBrowser);
		};
		vbox.AddChild(okBtn);
	}

	private void ShowStartWarningPopup(string mapName)
	{
		var warningPopup = new Panel();
		warningPopup.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
		warningPopup.AddThemeStyleboxOverride("panel", UIStyle.CreateBgGradient());
		AddChild(warningPopup);

		var cardPanel = new Panel();
		cardPanel.CustomMinimumSize = new Vector2(450, 220);
		cardPanel.SetAnchorsAndOffsetsPreset(LayoutPreset.Center);
		cardPanel.AddThemeStyleboxOverride("panel", UIStyle.CreateStonePanel(true));
		warningPopup.AddChild(cardPanel);

		var vbox = new VBoxContainer();
		vbox.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
		vbox.CustomMinimumSize = new Vector2(400, 180);
		vbox.SizeFlagsHorizontal = SizeFlags.ExpandFill;
		vbox.SizeFlagsVertical = SizeFlags.ExpandFill;
		cardPanel.AddChild(vbox);

		vbox.AddChild(new Control { CustomMinimumSize = new Vector2(0, 20) });

		var titleLabel = new Label();
		UIStyle.ApplyTitle(titleLabel, Tr("NOT ALL PLAYERS READY"), 20);
		titleLabel.AddThemeColorOverride("font_color", new Color(0.9f, 0.3f, 0.3f));
		vbox.AddChild(titleLabel);

		vbox.AddChild(new Control { CustomMinimumSize = new Vector2(0, 10) });

		var descLabel = new Label();
		descLabel.Text = Tr("Not all players are ready, start anyway?");
		descLabel.HorizontalAlignment = HorizontalAlignment.Center;
		descLabel.AddThemeFontSizeOverride("font_size", 15);
		descLabel.AddThemeColorOverride("font_color", new Color(0.9f, 0.9f, 0.95f));
		vbox.AddChild(descLabel);

		vbox.AddChild(new Control { CustomMinimumSize = new Vector2(0, 20) });

		var hbox = new HBoxContainer();
		hbox.Alignment = BoxContainer.AlignmentMode.Center;
		vbox.AddChild(hbox);

		var startBtn = new Button();
		startBtn.Flat = false;
		startBtn.AddThemeConstantOverride("icon_max_width", 0);
		startBtn.AddThemeStyleboxOverride("normal", UIStyle.CreateButtonNormal());
		startBtn.AddThemeStyleboxOverride("hover", UIStyle.CreateButtonHover());
		startBtn.AddThemeStyleboxOverride("pressed", UIStyle.CreateButtonPressed());
		startBtn.AddThemeStyleboxOverride("focus", new StyleBoxEmpty());
		UIStyle.ApplyButtonText(startBtn, "START GAME", 14);
		startBtn.CustomMinimumSize = new Vector2(160, 40);
		startBtn.Pressed += () =>
		{
			UIManager.Instance.PlayClickSound();
			warningPopup.QueueFree();
			LobbyManager.Instance.StartGame(mapName);
		};
		hbox.AddChild(startBtn);

		var spacer = new Control { CustomMinimumSize = new Vector2(20, 0) };
		hbox.AddChild(spacer);

		var cancelBtn = new Button();
		cancelBtn.Flat = false;
		cancelBtn.AddThemeConstantOverride("icon_max_width", 0);
		cancelBtn.AddThemeStyleboxOverride("normal", UIStyle.CreateButtonNormal());
		cancelBtn.AddThemeStyleboxOverride("hover", UIStyle.CreateButtonHover());
		cancelBtn.AddThemeStyleboxOverride("pressed", UIStyle.CreateButtonPressed());
		cancelBtn.AddThemeStyleboxOverride("focus", new StyleBoxEmpty());
		UIStyle.ApplyButtonText(cancelBtn, "CANCEL", 14);
		cancelBtn.CustomMinimumSize = new Vector2(160, 40);
		cancelBtn.Pressed += () =>
		{
			UIManager.Instance.PlayClickSound();
			warningPopup.QueueFree();
		};
		hbox.AddChild(cancelBtn);
	}

	private void ShowDownloadingClientsPopup()
	{
		var warningPopup = new Panel();
		warningPopup.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
		warningPopup.AddThemeStyleboxOverride("panel", UIStyle.CreateBgGradient());
		AddChild(warningPopup);

		var cardPanel = new Panel();
		cardPanel.CustomMinimumSize = new Vector2(480, 220);
		cardPanel.SetAnchorsAndOffsetsPreset(LayoutPreset.Center);
		cardPanel.AddThemeStyleboxOverride("panel", UIStyle.CreateStonePanel(true));
		warningPopup.AddChild(cardPanel);

		var vbox = new VBoxContainer();
		vbox.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
		vbox.CustomMinimumSize = new Vector2(440, 180);
		vbox.SizeFlagsHorizontal = SizeFlags.ExpandFill;
		vbox.SizeFlagsVertical = SizeFlags.ExpandFill;
		cardPanel.AddChild(vbox);

		vbox.AddChild(new Control { CustomMinimumSize = new Vector2(0, 20) });

		var titleLabel = new Label();
		UIStyle.ApplyTitle(titleLabel, Tr("CLIENTS DOWNLOADING MAP"), 20);
		titleLabel.AddThemeColorOverride("font_color", new Color(0.9f, 0.6f, 0.1f));
		vbox.AddChild(titleLabel);

		vbox.AddChild(new Control { CustomMinimumSize = new Vector2(0, 10) });

		var descLabel = new Label();
		descLabel.Text = Tr("Some players are still downloading the map.\nWait for them or Kick them before starting the game.");
		descLabel.HorizontalAlignment = HorizontalAlignment.Center;
		descLabel.AddThemeFontSizeOverride("font_size", 14);
		descLabel.AddThemeColorOverride("font_color", new Color(0.9f, 0.9f, 0.95f));
		vbox.AddChild(descLabel);

		vbox.AddChild(new Control { CustomMinimumSize = new Vector2(0, 20) });

		var okBtn = new Button();
		okBtn.Flat = false;
		okBtn.AddThemeConstantOverride("icon_max_width", 0);
		okBtn.AddThemeStyleboxOverride("normal", UIStyle.CreateButtonNormal());
		okBtn.AddThemeStyleboxOverride("hover", UIStyle.CreateButtonHover());
		okBtn.AddThemeStyleboxOverride("pressed", UIStyle.CreateButtonPressed());
		okBtn.AddThemeStyleboxOverride("focus", new StyleBoxEmpty());
		UIStyle.ApplyButtonText(okBtn, Tr("OK"), 14);
		okBtn.CustomMinimumSize = new Vector2(160, 40);
		okBtn.SizeFlagsHorizontal = SizeFlags.ShrinkCenter;
		okBtn.Pressed += () =>
		{
			UIManager.Instance.PlayClickSound();
			warningPopup.QueueFree();
		};
		vbox.AddChild(okBtn);
	}

	private void OnActiveMapChanged(string mapName)
	{
		UpdateSelectedMapUI();
		if (!LobbyManager.Instance.IsHost && _downloadProgress != null && _downloadLabel != null)
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


	private async void VerifyMapAuthorship(string mapName)
	{
		_authorshipWarningLabel.Visible = false;
		_primaryAuthorLabel.Text = Tr("Author: Unknown");
		_otherAuthorsList.Clear();
		
		string? manifestPath = MapAssetManager.FindManifestPath(mapName);
		string mapDir = manifestPath != null ? System.IO.Path.GetDirectoryName(manifestPath)! : ProjectSettings.GlobalizePath("user://maps/" + mapName);
		string mapJsonPath = System.IO.Path.Combine(mapDir, "metadata.json");
		if (!System.IO.File.Exists(mapJsonPath))
		{
			mapJsonPath = System.IO.Path.Combine(mapDir, "manifest.json");
		}
		if (!System.IO.File.Exists(mapJsonPath))
		{
			return; 
		}
		
		try
		{
			string jsonContent = System.IO.File.ReadAllText(mapJsonPath);
			var mapDoc = JsonNode.Parse(jsonContent);
			if (mapDoc != null)
			{
				if (mapDoc["Contributors"] is JsonArray contArr)
				{
					foreach (var node in contArr)
					{
						if (node != null)
						{
							_otherAuthorsList.Add(node.GetValue<string>());
						}
					}
				}
				else if (mapDoc["Author"] != null)
				{
					string auth = mapDoc["Author"]!.ToString();
					if (!string.IsNullOrEmpty(auth))
					{
						_primaryAuthorLabel.Text = Tr("Author:") + " " + auth;
					}
				}
			}
			
			byte[] fileBytes = System.IO.File.ReadAllBytes(mapJsonPath);
			string mapBlake3 = RealmMetadataHelper.ComputeBlake3(fileBytes, ".json");
			string hash = $"{mapBlake3}.json";
			
			string adminServerUrl = LobbyManager.Instance.RegistryServerUrl;
			var assetAuthorRes = await _sharedHttpClient.GetAsync(adminServerUrl + "/api/publish_map/asset_author/" + hash);
			if (assetAuthorRes.IsSuccessStatusCode)
			{
				string assetAuthorJson = await assetAuthorRes.Content.ReadAsStringAsync();
				var assetMeta = JsonNode.Parse(assetAuthorJson);
				if (assetMeta != null)
				{
					string author = assetMeta["AuthorUsername"]?.GetValue<string>() ?? "Unknown";
					string signatureB64 = assetMeta["Signature"]?.GetValue<string>();
					string pubKeyB64 = assetMeta["PublicKey"]?.GetValue<string>();
					
					if (string.IsNullOrEmpty(signatureB64) || string.IsNullOrEmpty(pubKeyB64))
					{
						_authorshipWarningLabel.Visible = true;
						return;
					}
					
					byte[] signatureBytes = Convert.FromBase64String(signatureB64);
					byte[] pubKeyBytes = Convert.FromBase64String(pubKeyB64);
					byte[] hashBytes = System.Text.Encoding.UTF8.GetBytes(hash);
					
					var publicKey = PublicKey.Import(SignatureAlgorithm.Ed25519, pubKeyBytes, KeyBlobFormat.RawPublicKey);
					bool isValid = SignatureAlgorithm.Ed25519.Verify(publicKey, hashBytes, signatureBytes);
					
					if (isValid)
					{
						_primaryAuthorLabel.Text = Tr("Author:") + " " + author;
					}
					else
					{
						_authorshipWarningLabel.Visible = true;
					}
				}
			}
			else
			{
				_authorshipWarningLabel.Visible = true;
			}
		}
		catch
		{
			_authorshipWarningLabel.Visible = true;
		}
	}

private void UpdateSelectedMapUI()
	{
		string currentMap = LobbyManager.Instance.ActiveMapName ?? "melee";
		int selectedIndex = _lobbyRoomMaps.FindIndex(m => m.PathName == currentMap);
		if (selectedIndex >= 0)
		{
			_mapNameLabel.Text = _lobbyRoomMaps[selectedIndex].DisplayName.ToUpper();
			_briefingLabel.Text = _lobbyRoomMaps[selectedIndex].Description;
		}
		else
		{
			selectedIndex = _lobbyRoomMaps.FindIndex(m => m.PathName.ToLower().Contains(currentMap.ToLower()));
			if (selectedIndex >= 0)
			{
				_mapNameLabel.Text = _lobbyRoomMaps[selectedIndex].DisplayName.ToUpper();
				_briefingLabel.Text = _lobbyRoomMaps[selectedIndex].Description;
			}
			else if (_lobbyRoomMaps.Count > 0)
			{
				_mapNameLabel.Text = _lobbyRoomMaps[0].DisplayName.ToUpper();
				_briefingLabel.Text = _lobbyRoomMaps[0].Description;
			}
		}

		if (_lobbyRoomMaps.Count > 0 && selectedIndex >= 0)
		{
			VerifyMapAuthorship(_lobbyRoomMaps[selectedIndex].PathName);
		}
	}

	private void TriggerInviteFeedback()
	{
		_inviteFeedback.Text = Tr("Friend Invite Sent!");
		_inviteFeedback.Modulate = new Color(0.3f, 0.8f, 1.0f, 1.0f);
		
		var tween = CreateTween();
		tween.TweenProperty(_inviteFeedback, "modulate:a", 0.0f, 2.0f).SetDelay(1.0f);
	}

	private void PopulateRunicPillar(VBoxContainer container)
	{
		container.Visible = false;
	}

	public override void _ExitTree()
	{
		if (LobbyManager.Instance != null)
		{
			LobbyManager.Instance.PlayerListUpdated -= PopulatePlayersList;
			LobbyManager.Instance.ChatReceived -= OnLobbyChatReceived;
			LobbyManager.Instance.ConnectionFailed -= OnLobbyConnectionFailed;
			LobbyManager.Instance.KickReceived -= OnLobbyKickReceived;

			LobbyManager.Instance.SpectatorDelayChanged -= OnSpectatorDelayChanged;

			LobbyManager.Instance.MapDownloadProgressChanged -= OnMapDownloadProgress;
			LobbyManager.Instance.MapDownloadCompleted -= OnMapDownloadCompleted;
			LobbyManager.Instance.MapDownloadFailed -= OnMapDownloadFailed;

			LobbyManager.Instance.CountdownStarted -= OnCountdownStarted;
			LobbyManager.Instance.CountdownTick -= OnCountdownTick;
			LobbyManager.Instance.CountdownCancelled -= OnCountdownCancelled;
			LobbyManager.Instance.CountdownFinished -= OnCountdownFinished;
			LobbyManager.Instance.HostStabilityUpdated -= UpdateHostStabilityLabel;
			LobbyManager.Instance.ActiveMapChanged -= OnActiveMapChanged;
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
		_countdownPopup.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
		_countdownPopup.AddThemeStyleboxOverride("panel", UIStyle.CreateLobbyRoomBg());
		AddChild(_countdownPopup);

		var cardPanel = new Panel();
		cardPanel.CustomMinimumSize = new Vector2(420, 560);
		cardPanel.Size = new Vector2(420, 560);
		cardPanel.GrowHorizontal = Control.GrowDirection.Both;
		cardPanel.GrowVertical = Control.GrowDirection.Both;
		cardPanel.SetAnchorsAndOffsetsPreset(LayoutPreset.Center);
		cardPanel.AddThemeStyleboxOverride("panel", UIStyle.CreateGameStartingPanel());
		_countdownPopup.AddChild(cardPanel);

		var vbox = new VBoxContainer();
		vbox.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
		vbox.CustomMinimumSize = new Vector2(420, 560);
		vbox.SizeFlagsHorizontal = SizeFlags.ExpandFill;
		vbox.SizeFlagsVertical = SizeFlags.ExpandFill;
		cardPanel.AddChild(vbox);

		vbox.AddChild(new Control { CustomMinimumSize = new Vector2(0, 22) });

		var titleLabel = new Label();
		UIStyle.ApplyTitle(titleLabel, "GAME STARTING", 26);
		titleLabel.AddThemeColorOverride("font_color", UIStyle.ColorGold);
		vbox.AddChild(titleLabel);

		var circleContainer = new CenterContainer();
		circleContainer.SizeFlagsVertical = SizeFlags.ExpandFill;
		circleContainer.SizeFlagsHorizontal = SizeFlags.ExpandFill;

		_countdownTextLabel = new Label();
		_countdownTextLabel.Text = seconds.ToString();
		_countdownTextLabel.HorizontalAlignment = HorizontalAlignment.Center;
		_countdownTextLabel.VerticalAlignment = VerticalAlignment.Center;
		_countdownTextLabel.AddThemeColorOverride("font_color", UIStyle.ColorCyanGlow);
		_countdownTextLabel.AddThemeColorOverride("font_outline_color", Colors.Black);
		_countdownTextLabel.AddThemeConstantOverride("outline_size", 10);
		_countdownTextLabel.AddThemeFontSizeOverride("font_size", 72);
		circleContainer.AddChild(_countdownTextLabel);
		vbox.AddChild(circleContainer);

		var cancelBtn = new Button();
		cancelBtn.Flat = false;
		cancelBtn.AddThemeConstantOverride("icon_max_width", 0);
		cancelBtn.AddThemeStyleboxOverride("normal", UIStyle.CreateButtonNormal());
		cancelBtn.AddThemeStyleboxOverride("hover", UIStyle.CreateButtonHover());
		cancelBtn.AddThemeStyleboxOverride("pressed", UIStyle.CreateButtonPressed());
		cancelBtn.AddThemeStyleboxOverride("focus", new StyleBoxEmpty());
		UIStyle.ApplyButtonText(cancelBtn, "CANCEL", 16);

		cancelBtn.CustomMinimumSize = new Vector2(180, 48);
		cancelBtn.SizeFlagsHorizontal = SizeFlags.ShrinkCenter;
		cancelBtn.Pressed += () =>
		{
			UIManager.Instance.PlayClickSound();
			LobbyManager.Instance.RequestCancelCountdown();
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

		if (LobbyManager.Instance != null && LobbyManager.Instance.IsHost)
		{
			LobbyManager.Instance.UnregisterActiveLobbyFromRegistry();
		}
	}

	private void PopulatePlayersList()
	{
		HideConnectingPopup();
		if (!LobbyManager.Instance.IsHost && !_versionMismatchDetected)
		{
			var host = LobbyManager.Instance.PlayerList.Find(p => p.IsHost);
			if (host != null)
			{
				string hostVersion = host.BinaryVersion;
				string clientVersion = RealmVersion.GameBinaryVersion;
				if (hostVersion != clientVersion)
				{
					_versionMismatchDetected = true;
					ShowVersionMismatchPopup(hostVersion, clientVersion);
					return;
				}
			}
		}

		if (_unstableWarningLabel != null)
		{
			_unstableWarningLabel.Visible = IsAnyMetricRed();
		}

		var existingRows = new Dictionary<int, PanelContainer>();
		foreach (Node child in _playersContainer.GetChildren())
		{
			if (child is PanelContainer pc && pc.Name.ToString().StartsWith("PlayerRow_"))
			{
				string idStr = pc.Name.ToString().Substring("PlayerRow_".Length);
				if (int.TryParse(idStr, out int peerId))
				{
					existingRows[peerId] = pc;
				}
			}
		}

		var activePeerIds = new HashSet<int>();

		for (int i = 0; i < LobbyManager.Instance.PlayerList.Count; i++)
		{
			var player = LobbyManager.Instance.PlayerList[i];
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
			}
		}

		foreach (var kvp in existingRows)
		{
			if (!activePeerIds.Contains(kvp.Key))
			{
				kvp.Value.QueueFree();
			}
		}
		
		SetupStartButton();
	}

	private PanelContainer CreatePlayerRow(LobbyManager.PlayerInfo p)
	{
		bool isLocalPlayer = (LobbyManager.Instance.LocalPlayer != null && p.PeerId == LobbyManager.Instance.LocalPlayer.PeerId) ||
		                     (LobbyManager.Instance.IsHost && p.PeerId == 1) ||
		                     (p.PeerId == Multiplayer.GetUniqueId());

		var panel = new PanelContainer();
		panel.CustomMinimumSize = new Vector2(0, 46);

		var style = new StyleBoxFlat();
		style.BgColor = new Color(0.12f, 0.13f, 0.16f, 0.3f);
		style.BorderColor = new Color(0.2f, 0.2f, 0.25f, 0.2f);
		style.SetBorderWidthAll(1);
		style.ContentMarginLeft = 8;
		style.ContentMarginRight = 8;
		style.ContentMarginTop = 0;
		style.ContentMarginBottom = 0;
		panel.AddThemeStyleboxOverride("panel", style);

		var hBox = new HBoxContainer();
		hBox.AddThemeConstantOverride("separation", 16);
		panel.AddChild(hBox);

		if (p.PeerId >= 1 || isLocalPlayer)
		{
			var readyCheck = new CheckBox();
			readyCheck.Name = "ReadyCheck";
			readyCheck.Text = Tr("READY");
			readyCheck.ButtonPressed = p.IsReady;
			readyCheck.SizeFlagsVertical = SizeFlags.ShrinkCenter;
			readyCheck.AddThemeConstantOverride("icon_max_width", 20);
			UIStyle.ApplyCheckboxStyle(readyCheck);

			if (isLocalPlayer)
			{
				readyCheck.Disabled = false;
				readyCheck.Toggled += (toggled) =>
				{
					UIManager.Instance.PlayClickSound();
					p.IsReady = toggled;
					if (LobbyManager.Instance.LocalPlayer != null && LobbyManager.Instance.LocalPlayer.PeerId == p.PeerId)
					{
						LobbyManager.Instance.LocalPlayer.IsReady = toggled;
					}
					LobbyManager.Instance.UpdateReadyState(p.PeerId, toggled);
					SetupStartButton();
				};
			}
			else
			{
				readyCheck.Disabled = true;
				readyCheck.AddThemeColorOverride("font_disabled_color", new Color(0.6f, 0.6f, 0.6f));
			}
			hBox.AddChild(readyCheck);
		}

		var optTeam = new OptionButton();
		optTeam.Name = "OptTeam";
		optTeam.CustomMinimumSize = new Vector2(95, 30);
		optTeam.SizeFlagsVertical = SizeFlags.ShrinkCenter;
		optTeam.Flat = false;

		optTeam.AddThemeStyleboxOverride("normal", UIStyle.CreateButtonNormal());
		optTeam.AddThemeStyleboxOverride("hover", UIStyle.CreateButtonHover());
		optTeam.AddThemeStyleboxOverride("pressed", UIStyle.CreateButtonPressed());
		optTeam.AddThemeStyleboxOverride("focus", new StyleBoxEmpty());
		optTeam.AddThemeColorOverride("font_color", UIStyle.ColorGoldDull);
		optTeam.AddThemeColorOverride("font_hover_color", UIStyle.ColorGold);
		optTeam.AddThemeColorOverride("font_pressed_color", UIStyle.ColorCyanGlow);
		optTeam.AddThemeFontSizeOverride("font_size", 13);

		optTeam.AddItem("Team 1");
		optTeam.AddItem("Team 2");
		optTeam.AddItem("Spectator");

		int teamIdx = p.Team == "Team 1" ? 0 : (p.Team == "Team 2" ? 1 : 2);
		optTeam.Select(teamIdx);

		if (isLocalPlayer)
		{
			optTeam.ItemSelected += (idx) =>
			{
				UIManager.Instance.PlayClickSound();
				p.Team = idx == 0 ? "Team 1" : (idx == 1 ? "Team 2" : "Spectator");
				if (p.Team == "Spectator")
				{
					p.Faction = "SPECTATOR";
					p.Color = new Color(0.5f, 0.5f, 0.5f);
				}
				else if (p.Faction == "SPECTATOR")
				{
					p.Faction = "HUMAN";
				}
				LobbyManager.Instance.UpdatePlayerSlot(p.PeerId, p.Faction, p.Team, p.Color, p.Name);
			};
			optTeam.MouseEntered += () => UIManager.Instance.PlayHoverSound();
		}
		else
		{
			optTeam.Disabled = true;
			optTeam.AddThemeColorOverride("font_disabled_color", new Color(0.5f, 0.5f, 0.5f));
		}
		hBox.AddChild(optTeam);

		var colorBtn = new Button();
		colorBtn.Name = "ColorBtn";
		colorBtn.CustomMinimumSize = new Vector2(24, 24);
		colorBtn.SizeFlagsVertical = SizeFlags.ShrinkCenter;
		
		var colorStyle = new StyleBoxFlat();
		colorStyle.BgColor = p.Color;
		colorStyle.BorderColor = new Color(0.4f, 0.4f, 0.45f);
		colorStyle.SetBorderWidthAll(2);
		colorStyle.CornerRadiusTopLeft = 4;
		colorStyle.CornerRadiusTopRight = 4;
		colorStyle.CornerRadiusBottomLeft = 4;
		colorStyle.CornerRadiusBottomRight = 4;
		colorBtn.AddThemeStyleboxOverride("normal", colorStyle);
		colorBtn.AddThemeStyleboxOverride("hover", colorStyle);
		colorBtn.AddThemeStyleboxOverride("pressed", colorStyle);

		if (isLocalPlayer && p.Team != "Spectator")
		{
			colorBtn.Pressed += () =>
			{
				UIManager.Instance.PlayClickSound();
				int nextIdx = (PlayerColorConfig.GetColorIndex(p.Color) + 1) % AvailableColors.Count;
				p.Color = AvailableColors[nextIdx];
				colorStyle.BgColor = p.Color;
				colorBtn.AddThemeStyleboxOverride("normal", colorStyle);
				
				LobbyManager.Instance.UpdatePlayerSlot(p.PeerId, p.Faction, p.Team, p.Color, p.Name);
			};
			colorBtn.MouseEntered += () => UIManager.Instance.PlayHoverSound();
		}
		else
		{
			colorBtn.Disabled = true;
		}
		hBox.AddChild(colorBtn);

		if (isLocalPlayer)
		{
			var nameEdit = new LineEdit();
			nameEdit.Name = "NameEdit";
			nameEdit.Text = p.Name;
			nameEdit.SizeFlagsHorizontal = SizeFlags.ShrinkCenter;
			nameEdit.CustomMinimumSize = new Vector2(110, 30);
			nameEdit.SizeFlagsVertical = SizeFlags.ShrinkCenter;
			
			nameEdit.AddThemeStyleboxOverride("normal", UIStyle.CreateTextInput(false));
			nameEdit.AddThemeStyleboxOverride("focus", UIStyle.CreateTextInput(true));
			nameEdit.AddThemeColorOverride("font_color", new Color(1, 1, 1));
			nameEdit.AddThemeFontSizeOverride("font_size", 14);
			
			nameEdit.TextSubmitted += (text) =>
			{
				p.Name = text;
				LobbyManager.Instance.UpdatePlayerSlot(p.PeerId, p.Faction, p.Team, p.Color, p.Name);
			};
			hBox.AddChild(nameEdit);
		}
		else
		{
			var lblName = new Label();
			lblName.Name = "LblName";
			lblName.Text = p.Name;
			lblName.SizeFlagsHorizontal = SizeFlags.ShrinkCenter;
			lblName.CustomMinimumSize = new Vector2(110, 0);
			lblName.AddThemeColorOverride("font_color", new Color(0.9f, 0.9f, 0.9f));
			lblName.AddThemeFontSizeOverride("font_size", 14);
			lblName.VerticalAlignment = VerticalAlignment.Center;
			hBox.AddChild(lblName);
		}

		var optFaction = new OptionButton();
		optFaction.Name = "OptFaction";
		optFaction.CustomMinimumSize = new Vector2(110, 30);
		optFaction.SizeFlagsVertical = SizeFlags.ShrinkCenter;
		optFaction.Flat = false;
		
		optFaction.AddThemeStyleboxOverride("normal", UIStyle.CreateButtonNormal());
		optFaction.AddThemeStyleboxOverride("hover", UIStyle.CreateButtonHover());
		optFaction.AddThemeStyleboxOverride("pressed", UIStyle.CreateButtonPressed());
		optFaction.AddThemeStyleboxOverride("focus", new StyleBoxEmpty());
		optFaction.AddThemeColorOverride("font_color", UIStyle.ColorGoldDull);
		optFaction.AddThemeColorOverride("font_hover_color", UIStyle.ColorGold);
		optFaction.AddThemeColorOverride("font_pressed_color", UIStyle.ColorCyanGlow);
		optFaction.AddThemeFontSizeOverride("font_size", 13);
		
		if (p.Team == "Spectator")
		{
			optFaction.AddItem("SPECTATOR");
			optFaction.Select(0);
			optFaction.Disabled = true;
			optFaction.AddThemeColorOverride("font_disabled_color", new Color(0.5f, 0.5f, 0.5f));
		}
		else
		{
			foreach (var fact in _factions)
			{
				optFaction.AddItem(fact);
			}
			
			int selIdx = Array.IndexOf(_factions, p.Faction);
			if (selIdx >= 0) optFaction.Select(selIdx);

			if (isLocalPlayer)
			{
				optFaction.ItemSelected += (idx) => 
				{
					UIManager.Instance.PlayClickSound();
					p.Faction = _factions[idx];
					LobbyManager.Instance.UpdatePlayerSlot(p.PeerId, p.Faction, p.Team, p.Color, p.Name);
				};
				optFaction.MouseEntered += () => UIManager.Instance.PlayHoverSound();
			}
			else
			{
				optFaction.Disabled = true;
				optFaction.AddThemeColorOverride("font_disabled_color", new Color(0.5f, 0.5f, 0.5f));
			}
		}
		hBox.AddChild(optFaction);


		var diagLabel = new RichTextLabel();
		diagLabel.Name = "DiagLabel";
		diagLabel.BbcodeEnabled = true;
		diagLabel.ScrollActive = false;
		diagLabel.AutowrapMode = TextServer.AutowrapMode.Off;
		diagLabel.CustomMinimumSize = new Vector2(220, 24);
		diagLabel.SizeFlagsVertical = SizeFlags.ShrinkCenter;
		diagLabel.AddThemeFontSizeOverride("normal_font_size", 13);
		if (p.IsHost)
		{
			diagLabel.MouseFilter = Control.MouseFilterEnum.Pass;
			diagLabel.TooltipText = Tr("Host: Average connection with all players in lobby");
		}
		
		string latencyText = p.Latency == "--" ? "measuring..." : p.Latency;
		string jitterText = p.Jitter == "--" ? "n/a" : p.Jitter;
		string lossText = p.PacketLoss == "--" ? "n/a" : p.PacketLoss;

		string pingColor = GetPingColorCode(p.Latency);
		string jitterColor = GetJitterColorCode(p.Jitter);
		string lossColor = GetLossColorCode(p.PacketLoss);

		if (!isLocalPlayer && !p.IsHost && !p.IsMapReady)
		{
			diagLabel.Text = $"  [color=#ffa040]{Tr("downloading...")}[/color]";
		}
		else
		{
			diagLabel.Text = $"  {Tr("Ping")}: [color={pingColor}]{latencyText}[/color] | {Tr("Jitter")}: [color={jitterColor}]{jitterText}[/color] | {Tr("Loss")}: [color={lossColor}]{lossText}[/color]";
		}
		hBox.AddChild(diagLabel);


		if (LobbyManager.Instance.IsHost && p.PeerId != 1)
		{
			var bootBtn = new Button();
			bootBtn.Name = "BootBtn";
			bootBtn.Text = Tr("KICK");
			bootBtn.CustomMinimumSize = new Vector2(60, 26);
			bootBtn.SizeFlagsVertical = SizeFlags.ShrinkCenter;
			
			var btnNorm = UIStyle.CreateButtonNormal();
			if (btnNorm is StyleBoxFlat flat)
			{
				flat.BgColor = new Color(0.4f, 0.1f, 0.1f, 0.5f);
				flat.BorderColor = new Color(0.8f, 0.2f, 0.2f, 0.6f);
			}
			
			var btnHover = UIStyle.CreateButtonHover();
			if (btnHover is StyleBoxFlat flatH)
			{
				flatH.BgColor = new Color(0.6f, 0.1f, 0.1f, 0.8f);
				flatH.BorderColor = new Color(1.0f, 0.2f, 0.2f, 0.9f);
			}

			bootBtn.AddThemeStyleboxOverride("normal", btnNorm);
			bootBtn.AddThemeStyleboxOverride("hover", btnHover);
			bootBtn.AddThemeColorOverride("font_color", new Color(1.0f, 0.8f, 0.8f));
			bootBtn.AddThemeFontSizeOverride("font_size", 12);
			
			bootBtn.Pressed += () =>
			{
				UIManager.Instance.PlayWarningSound();
				LobbyManager.Instance.BootPlayer(p.PeerId);
			};
			
			hBox.AddChild(bootBtn);
		}

		var spaceEnd = new Control();
		spaceEnd.CustomMinimumSize = new Vector2(10, 0);
		hBox.AddChild(spaceEnd);

		return panel;
	}

	private void UpdatePlayerRow(PanelContainer row, LobbyManager.PlayerInfo p)
	{
		var hBox = row.GetChildCount() > 0 ? row.GetChild(0) as HBoxContainer : null;
		if (hBox == null) return;

		bool isLocalPlayer = (LobbyManager.Instance.LocalPlayer != null && p.PeerId == LobbyManager.Instance.LocalPlayer.PeerId) ||
		                     (LobbyManager.Instance.IsHost && p.PeerId == 1) ||
		                     (p.PeerId == Multiplayer.GetUniqueId());

		var readyCheck = hBox.GetNodeOrNull<CheckBox>("ReadyCheck");
		if (readyCheck != null)
		{
			if (readyCheck.ButtonPressed != p.IsReady)
			{
				readyCheck.SetPressedNoSignal(p.IsReady);
			}
			readyCheck.Disabled = !isLocalPlayer;
		}

		var optTeam = hBox.GetNodeOrNull<OptionButton>("OptTeam");
		if (optTeam != null)
		{
			int teamIdx = p.Team == "Team 1" ? 0 : (p.Team == "Team 2" ? 1 : 2);
			if (optTeam.Selected != teamIdx)
			{
				optTeam.Selected = teamIdx;
			}
		}

		var colorBtn = hBox.GetNodeOrNull<Button>("ColorBtn");
		if (colorBtn != null && colorBtn.GetThemeStylebox("normal") is StyleBoxFlat colorStyle)
		{
			if (colorStyle.BgColor != p.Color)
			{
				colorStyle.BgColor = p.Color;
				colorBtn.AddThemeStyleboxOverride("normal", colorStyle);
			}
		}

		var nameEdit = hBox.GetNodeOrNull<LineEdit>("NameEdit");
		if (nameEdit != null && nameEdit.Text != p.Name)
		{
			nameEdit.Text = p.Name;
		}

		var lblName = hBox.GetNodeOrNull<Label>("LblName");
		if (lblName != null && lblName.Text != p.Name)
		{
			lblName.Text = p.Name;
		}

		var optFaction = hBox.GetNodeOrNull<OptionButton>("OptFaction");
		if (optFaction != null)
		{
			if (p.Team == "Spectator")
			{
				if (optFaction.Selected != 0) optFaction.Selected = 0;
			}
			else
			{
				int selIdx = Array.IndexOf(_factions, p.Faction);
				if (selIdx >= 0 && optFaction.Selected != selIdx)
				{
					optFaction.Selected = selIdx;
				}
			}
		}

		var diagLabel = hBox.GetNodeOrNull<RichTextLabel>("DiagLabel");
		if (diagLabel != null)
		{
			string latencyText = p.Latency == "--" ? "measuring..." : p.Latency;
			string jitterText = p.Jitter == "--" ? "n/a" : p.Jitter;
			string lossText = p.PacketLoss == "--" ? "n/a" : p.PacketLoss;

			string pingColor = GetPingColorCode(p.Latency);
			string jitterColor = GetJitterColorCode(p.Jitter);
			string lossColor = GetLossColorCode(p.PacketLoss);

			string newText;
			if (!isLocalPlayer && !p.IsHost && !p.IsMapReady)
			{
				newText = $"  [color=#ffa040]{Tr("downloading...")}[/color]";
			}
			else
			{
				newText = $"  {Tr("Ping")}: [color={pingColor}]{latencyText}[/color] | {Tr("Jitter")}: [color={jitterColor}]{jitterText}[/color] | {Tr("Loss")}: [color={lossColor}]{lossText}[/color]";
			}
			if (diagLabel.Text != newText)
			{
				diagLabel.Text = newText;
			}
		}
	}

	private void OnChatSubmitted(string text)
	{
		if (string.IsNullOrEmpty(text.Trim())) return;
		_chatInput.Clear();
		LobbyManager.Instance.SendChatMessage(LobbyManager.Instance.LocalPlayer.Name, text);
	}

	private void OnLobbyChatReceived(string senderName, string message, bool alliesOnly)
	{
		string color = senderName == "System" ? "#ffd700" : (senderName == LobbyManager.Instance.LocalPlayer.Name ? "#5cd6ff" : "#d4a0a0");
		_chatLog.Text += $"[color={color}]{senderName}[/color]: {message}\n";
	}

	private void OnLobbyConnectionFailed(string reason)
	{
		HideConnectingPopup();
		GD.PrintErr($"[LobbyRoom] Connection failed from lobby: {reason}");
		UIManager.Instance.PlayWarningSound();
		ShowConnectionFailedPopup(reason);
	}

	private void ShowConnectionFailedPopup(string reason)
	{
		var warningPopup = new Panel();
		warningPopup.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
		warningPopup.AddThemeStyleboxOverride("panel", UIStyle.CreateBgGradient());
		AddChild(warningPopup);

		var cardPanel = new Panel();
		cardPanel.CustomMinimumSize = new Vector2(450, 220);
		cardPanel.SetAnchorsAndOffsetsPreset(LayoutPreset.Center);
		cardPanel.AddThemeStyleboxOverride("panel", UIStyle.CreateStonePanel(true));
		warningPopup.AddChild(cardPanel);

		var vbox = new VBoxContainer();
		vbox.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
		vbox.CustomMinimumSize = new Vector2(400, 180);
		vbox.SizeFlagsHorizontal = SizeFlags.ExpandFill;
		vbox.SizeFlagsVertical = SizeFlags.ExpandFill;
		cardPanel.AddChild(vbox);

		vbox.AddChild(new Control { CustomMinimumSize = new Vector2(0, 20) });

		var titleLabel = new Label();
		UIStyle.ApplyTitle(titleLabel, Tr("CONNECTION FAILED"), 20);
		titleLabel.AddThemeColorOverride("font_color", new Color(0.9f, 0.3f, 0.3f));
		vbox.AddChild(titleLabel);

		vbox.AddChild(new Control { CustomMinimumSize = new Vector2(0, 10) });

		var descLabel = new Label();
		descLabel.Text = $"{Tr("Failed to connect to host.")}\n\n{string.Format(Tr("Reason: {0}"), reason)}";
		descLabel.HorizontalAlignment = HorizontalAlignment.Center;
		descLabel.AddThemeFontSizeOverride("font_size", 14);
		descLabel.AddThemeColorOverride("font_color", new Color(0.9f, 0.9f, 0.95f));
		vbox.AddChild(descLabel);

		vbox.AddChild(new Control { CustomMinimumSize = new Vector2(0, 15) });

		var okBtn = new Button();
		okBtn.Flat = false;
		okBtn.AddThemeConstantOverride("icon_max_width", 0);
		okBtn.AddThemeStyleboxOverride("normal", UIStyle.CreateButtonNormal());
		okBtn.AddThemeStyleboxOverride("hover", UIStyle.CreateButtonHover());
		okBtn.AddThemeStyleboxOverride("pressed", UIStyle.CreateButtonPressed());
		okBtn.AddThemeStyleboxOverride("focus", new StyleBoxEmpty());
		UIStyle.ApplyButtonText(okBtn, Tr("OK"), 14);
		okBtn.CustomMinimumSize = new Vector2(160, 40);
		okBtn.SizeFlagsHorizontal = SizeFlags.ShrinkCenter;
		okBtn.Pressed += () =>
		{
			UIManager.Instance.PlayClickSound();
			warningPopup.QueueFree();
			LobbyManager.Instance.Disconnect();
			UIManager.Instance.TransitionTo(GameScreen.LobbyBrowser);
		};
		vbox.AddChild(okBtn);
	}

	private void OnLobbyKickReceived(string reason)
	{
		GD.Print($"[LobbyRoom] Disconnected / Kicked: {reason}");
		UIManager.Instance.PlayWarningSound();
		UIManager.Instance.TransitionTo(GameScreen.LobbyBrowser);
	}

	private ProgressBar _downloadProgress;
	private Label _downloadLabel;
	private void CreateDownloadProgressUI()
	{

		_downloadLabel = new Label();
		_downloadLabel.Text = Tr("Checking map package...");
		_downloadLabel.Position = new Vector2(1450, 805);
		_downloadLabel.Size = new Vector2(340, 25);
		_downloadLabel.AddThemeColorOverride("font_color", UIStyle.ColorGoldDull);
		_downloadLabel.AddThemeFontSizeOverride("font_size", 14);
		AddChild(_downloadLabel);


		_downloadProgress = new ProgressBar();
		_downloadProgress.Position = new Vector2(1450, 835);
		_downloadProgress.Size = new Vector2(340, 24);
		_downloadProgress.ShowPercentage = true;
		_downloadProgress.Value = 0;
		

		_downloadProgress.AddThemeStyleboxOverride("background", UIStyle.CreateSliderTrack());
		_downloadProgress.AddThemeStyleboxOverride("fill", UIStyle.CreateSliderFill());
		_downloadProgress.AddThemeColorOverride("font_color", new Color(1, 1, 1));
		_downloadProgress.AddThemeFontSizeOverride("font_size", 12);
		AddChild(_downloadProgress);

		bool isReady = !string.IsNullOrEmpty(LobbyManager.Instance.ActiveMapName) && MapAssetManager.IsMapDownloaded(LobbyManager.Instance.ActiveMapName);
		if (isReady)
		{
			_downloadProgress.Value = 100.0f;
			_downloadLabel.Text = Tr("Map package ready.");
			_downloadLabel.AddThemeColorOverride("font_color", UIStyle.ColorCyanGlow);
		}

		LobbyManager.Instance.MapDownloadProgressChanged += OnMapDownloadProgress;
		LobbyManager.Instance.MapDownloadCompleted += OnMapDownloadCompleted;
		LobbyManager.Instance.MapDownloadFailed += OnMapDownloadFailed;

		SetupStartButton();
	}

	private void OnMapDownloadProgress(float progress)
	{
		if (_downloadProgress != null)
		{
			_downloadProgress.Value = progress * 100.0f;
		}
		if (_downloadLabel != null)
		{
			_downloadLabel.Text = $"{Tr("Downloading map package:")} {Math.Round(progress * 100.0f)}%";
		}
	}

	private void OnMapDownloadCompleted()
	{
		if (_downloadProgress != null)
		{
			_downloadProgress.Value = 100.0f;
		}
		if (_downloadLabel != null)
		{
			_downloadLabel.Text = Tr("Map package ready.");
			_downloadLabel.AddThemeColorOverride("font_color", UIStyle.ColorCyanGlow);
		}
		LobbyManager.Instance?.ReportLocalMapReadyState(true);
		SetupStartButton();
	}

	private void OnMapDownloadFailed()
	{
		if (_downloadLabel != null)
		{
			_downloadLabel.Text = Tr("Map download failed!");
			_downloadLabel.AddThemeColorOverride("font_color", new Color(1.0f, 0.3f, 0.3f));
		}
	}

	private void OnStartButtonMouseEntered() => UIManager.Instance.PlayHoverSound();

	private void CreateSpectatorDelayUI()
	{
		_spectatorDelayCheck = new CheckBox();
		_spectatorDelayCheck.Text = Tr("Spectator Delay (5m)");
		_spectatorDelayCheck.Position = new Vector2(1450, 770);
		_spectatorDelayCheck.Size = new Vector2(340, 40);
		_spectatorDelayCheck.ButtonPressed = LobbyManager.Instance.SpectatorDelay;
		_spectatorDelayCheck.Disabled = !LobbyManager.Instance.IsHost;
		
		if (_connectingPopup != null && GodotObject.IsInstanceValid(_connectingPopup))
		{
			_spectatorDelayCheck.Visible = false;
		}

		UIStyle.ApplyCheckboxStyle(_spectatorDelayCheck);
		
		_spectatorDelayCheck.Pressed += () =>
		{
			UIManager.Instance.PlayClickSound();
			LobbyManager.Instance.UpdateSpectatorDelay(_spectatorDelayCheck.ButtonPressed);
		};
		
		_spectatorDelayCheck.MouseEntered += () => UIManager.Instance.PlayHoverSound();
		AddChild(_spectatorDelayCheck);
	}

	private void OnSpectatorDelayChanged(bool enabled)
	{
		if (_spectatorDelayCheck != null)
		{
			_spectatorDelayCheck.ButtonPressed = enabled;
		}
	}

	private int ParsePingValue(string latency)
	{
		if (string.IsNullOrEmpty(latency) || latency == "--" || latency == "measuring...")
		{
			return -1;
		}
		string clean = latency.Replace("ms", "").Trim();
		if (int.TryParse(clean, out int val))
		{
			return val;
		}
		return -1;
	}

	private int ParseJitterValue(string jitter)
	{
		if (string.IsNullOrEmpty(jitter) || jitter == "--" || jitter == "")
		{
			return -1;
		}
		string clean = jitter.Replace("ms", "").Trim();
		if (int.TryParse(clean, out int val))
		{
			return val;
		}
		return -1;
	}

	private float ParseLossValue(string loss)
	{
		if (string.IsNullOrEmpty(loss) || loss == "--" || loss == "")
		{
			return -1f;
		}
		int pctIdx = loss.IndexOf('%');
		if (pctIdx >= 0)
		{
			string clean = loss.Substring(0, pctIdx).Trim();
			if (float.TryParse(clean, out float val))
			{
				return val;
			}
		}
		return -1f;
	}

	private string GetPingColorCode(string latency)
	{
		int val = ParsePingValue(latency);
		if (val < 0) return "#a0a0a0";
		if (val < 100) return "#4ce66a";
		if (val < 200) return "#ffd700";
		return "#ff5555";
	}

	private string GetJitterColorCode(string jitter)
	{
		int val = ParseJitterValue(jitter);
		if (val < 0) return "#a0a0a0";
		if (val < 20) return "#4ce66a";
		if (val < 50) return "#ffd700";
		return "#ff5555";
	}

	private string GetLossColorCode(string loss)
	{
		float val = ParseLossValue(loss);
		if (val < 0f) return "#a0a0a0";
		if (val < 1.0f) return "#4ce66a";
		if (val < 5.0f) return "#ffd700";
		return "#ff5555";
	}

	private bool IsAnyMetricRed()
	{
		foreach (var player in LobbyManager.Instance.PlayerList)
		{
			if (player.PeerId < 0)
			{
				continue;
			}
			int ping = ParsePingValue(player.Latency);
			if (ping >= 200)
			{
				return true;
			}
			int jitter = ParseJitterValue(player.Jitter);
			if (jitter >= 50)
			{
				return true;
			}
			float loss = ParseLossValue(player.PacketLoss);
			if (loss >= 5.0f)
			{
				return true;
			}
		}
		return false;
	}

	private void UpdateHostStabilityLabel(string stability)
	{
		if (LobbyManager.Instance == null || LobbyManager.Instance.IsHost || _hostStabilityLabel == null)
		{
			if (_hostStabilityLabel != null)
			{
				_hostStabilityLabel.Visible = false;
			}
			return;
		}

		_hostStabilityLabel.Visible = true;
		if (stability == "Excellent")
		{
			_hostStabilityLabel.Text = Tr("Host Stability: Good");
			_hostStabilityLabel.AddThemeColorOverride("font_color", new Color(0.3f, 0.9f, 0.4f));
		}
		else if (stability == "Average")
		{
			_hostStabilityLabel.Text = Tr("Host Stability: Average");
			_hostStabilityLabel.AddThemeColorOverride("font_color", new Color(0.9f, 0.8f, 0.1f));
		}
		else if (stability == "Poor")
		{
			_hostStabilityLabel.Text = Tr("Host Stability: Poor");
			_hostStabilityLabel.AddThemeColorOverride("font_color", new Color(0.9f, 0.25f, 0.25f));
		}
		else
		{
			_hostStabilityLabel.Visible = false;
		}
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
		_connectingPopup.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
		_connectingPopup.AddThemeStyleboxOverride("panel", UIStyle.CreateBgGradient());
		AddChild(_connectingPopup);

		var centerContainer = new CenterContainer();
		centerContainer.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
		_connectingPopup.AddChild(centerContainer);

		var cardPanel = new Panel();
		cardPanel.CustomMinimumSize = new Vector2(440, 160);
		cardPanel.AddThemeStyleboxOverride("panel", UIStyle.CreateBackdropPanel());
		centerContainer.AddChild(cardPanel);

		var vbox = new VBoxContainer();
		vbox.CustomMinimumSize = new Vector2(400, 120);
		vbox.Alignment = BoxContainer.AlignmentMode.Center;
		cardPanel.AddChild(vbox);
		vbox.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);

		var label = new Label();
		UIStyle.ApplyTitle(label, Tr("CONNECTING TO HOST ..."), 22);
		label.AddThemeColorOverride("font_color", UIStyle.ColorGold);
		label.AddThemeConstantOverride("outline_size", 4);
		label.AddThemeColorOverride("font_outline_color", new Color(0, 0, 0, 1));
		vbox.AddChild(label);

		var spinner = new Label();
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
