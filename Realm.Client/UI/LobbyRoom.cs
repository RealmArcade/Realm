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
using HttpClient = System.Net.Http.HttpClient;
using static Godot.Control;
using static Godot.CanvasItem;
using Realm.Client.Utils;
using Realm.Client.UI.InGame;
using Realm.Client.Services;

namespace Realm.Client.UI;

[GlobalClass]
public partial class LobbyRoom : Control
{
	public override void _EnterTree()
	{
		Realm.Client.Services.GameContext.EnsureServices();
	}

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
	private Button _attributionButton;
	private AttributionPopover _attributionPopover;
	private List<string> _otherAuthorsList = new List<string>();


	private static readonly HttpClient _sharedHttpClient = new HttpClient();
	public const int NEUTRAL_PLAYER_INDEX = PlayerColorConfig.NEUTRAL_PLAYER_INDEX;
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
		_addBotButton.Visible = GameContext.Lobby.IsHost;


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

		if (!GameContext.Lobby.IsHost)
		{
			UpdateHostStabilityLabel(GameContext.Lobby.HostStability);
			GameContext.Lobby.HostStabilityUpdated += UpdateHostStabilityLabel;
		}
		else if (GameContext.Lobby.IsRestrictedHost)
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


		GameContext.Lobby.PlayerListUpdated += PopulatePlayersList;
		GameContext.Lobby.ChatReceived += OnLobbyChatReceived;
		GameContext.Lobby.ConnectionFailed += OnLobbyConnectionFailed;
		GameContext.Lobby.KickReceived += OnLobbyKickReceived;
		GameContext.Lobby.CountdownStarted += OnCountdownStarted;
		GameContext.Lobby.CountdownTick += OnCountdownTick;
		GameContext.Lobby.CountdownCancelled += OnCountdownCancelled;
		GameContext.Lobby.CountdownFinished += OnCountdownFinished;
		GameContext.Lobby.ActiveMapChanged += OnActiveMapChanged;


		if (!GameContext.Lobby.IsHost)
		{
			CreateDownloadProgressUI();
			GameContext.Lobby.RequestChatHistory();
		}

		PopulatePlayersList();

		if (!GameContext.Lobby.IsHost)
		{
			ShowConnectingPopup();
		}

		CreateSpectatorDelayUI();
		GameContext.Lobby.SpectatorDelayChanged += OnSpectatorDelayChanged;

		_chatLog.Text = $"[color=#ffd700]{Tr("System: Connected to Lobby. Pre-Match Setup is active.")}[/color]\n";
		_chatInput.TextSubmitted += OnChatSubmitted;


		var mapImage = new TextureRect();
		mapImage.Name = "MapImage_TextureRect";
		mapImage.Texture = TrimmedTextureLoader.LoadTrimmedTexture("res://Assets/UI/snowy_forest_path.webp");
		mapImage.ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize;
		mapImage.StretchMode = TextureRect.StretchModeEnum.KeepAspectCovered;
		_mapFrame.AddChild(mapImage);
		mapImage.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);


		var mapDrawer = new TacticalMap();
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
		GetNode<VBoxContainer>("BriefingPanel/VBoxContainer").AddChild(_primaryAuthorLabel);

		var authorsSpacer = new Control { CustomMinimumSize = new Vector2(0, 24) };
		GetNode<VBoxContainer>("BriefingPanel/VBoxContainer").AddChild(authorsSpacer);

		_otherAuthorsButton = new Button();
		_otherAuthorsButton.Name = "OtherAuthorsButton_Button";
		_otherAuthorsButton.Text = Tr("Other Authors");
		_otherAuthorsButton.CustomMinimumSize = new Vector2(160, 32);
		_otherAuthorsButton.SizeFlagsHorizontal = Control.SizeFlags.ShrinkCenter;
		_otherAuthorsButton.AddThemeStyleboxOverride("normal", UIStyle.CreateButtonNormal());
		_otherAuthorsButton.AddThemeStyleboxOverride("hover", UIStyle.CreateButtonHover());
		_otherAuthorsButton.AddThemeStyleboxOverride("pressed", UIStyle.CreateButtonPressed());
		_otherAuthorsButton.Pressed += () =>
		{
			if (_otherAuthorsList.Count > 0)
			{
				ShowConfirmationDialog(Tr("Contributors:") + "\n" + string.Join("\n", _otherAuthorsList), null);
			}
			else
			{
				ShowConfirmationDialog(Tr("No other contributors listed."), null);
			}
		};
		GetNode<VBoxContainer>("BriefingPanel/VBoxContainer").AddChild(_otherAuthorsButton);

		SetupAttributionButton();

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


		string[] headers =
		{
			"PlayersPanel/VBoxContainer/TableHeader/ReadyCol",
			"PlayersPanel/VBoxContainer/TableHeader/TeamCol",
			"PlayersPanel/VBoxContainer/TableHeader/ColorCol",
			"PlayersPanel/VBoxContainer/TableHeader/NameCol",
			"PlayersPanel/VBoxContainer/TableHeader/FactionCol"
		};
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
			GameContext.Lobby.Disconnect();
			GameContext.UserInterface.TransitionTo(GameScreen.LobbyBrowser);
		});
		SetupLobbyButton(_inviteButton, "INVITE FRIEND", TriggerInviteFeedback);
		SetupLobbyButton(_addBotButton, "ADD AI BOT", () => GameContext.Lobby.AddAIBot());
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
			GameContext.UserInterface.PlayClickSound();
			onClick?.Invoke();
		};
		btn.MouseEntered += () => GameContext.UserInterface.PlayHoverSound();
	}

	private void SetupStartButton()
	{
		_startButton.Flat = false;
		ApplyStartButtonStyle();
		ApplyStartButtonState();
	}

	private void ApplyStartButtonStyle()
	{
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
	}

	private void ApplyStartButtonState()
	{
		DisconnectStartButtonSignals();

		if (GameContext.Lobby.IsHost)
		{
			ApplyHostStartButtonState();
		}
		else
		{
			ApplyClientStartButtonState();
		}

		var mouseEnteredCallable = Callable.From(OnStartButtonMouseEntered);
		if (_startButton.IsConnected(Control.SignalName.MouseEntered, mouseEnteredCallable))
		{
			_startButton.Disconnect(Control.SignalName.MouseEntered, mouseEnteredCallable);
		}

		_startButton.MouseEntered += OnStartButtonMouseEntered;
	}

	private void DisconnectStartButtonSignals()
	{
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
	}

	private void ApplyHostStartButtonState()
	{
		UIStyle.ApplyButtonText(_startButton, "START GAME", 22);
		_startButton.Disabled = false;
		_startButton.Pressed += OnStartPressed;
	}

	private void ApplyClientStartButtonState()
	{
		string activeMap = GameContext.Lobby.ActiveMapName ?? "";
		bool isMapReady = !string.IsNullOrEmpty(activeMap) && MapAssetManager.IsMapDownloaded(activeMap);
		if (!isMapReady)
		{
			UIStyle.ApplyButtonText(_startButton, "DOWNLOADING MAP...", 18);
			_startButton.Disabled = true;
			return;
		}

		var localPlayer = GetLocalPlayer();
		bool isReady = localPlayer != null && localPlayer.IsReady;
		UIStyle.ApplyButtonText(_startButton, isReady ? "NOT READY" : "READY", 22);
		_startButton.Disabled = false;
		_startButton.Pressed += OnClientReadyTogglePressed;
	}

	private void OnClientReadyTogglePressed()
	{
		GameContext.UserInterface.PlayClickSound();
		var localPlayer = GetLocalPlayer();
		if (localPlayer != null)
		{
			bool newReady = !localPlayer.IsReady;
			localPlayer.IsReady = newReady;
			GameContext.Lobby.UpdateReadyState(localPlayer.PeerId, newReady);
			SetupStartButton();
		}
	}


	private void ShowConfirmationDialog(string message, Action onConfirm)
	{
		var overlay = new ColorRect();
		overlay.Name = "Overlay_ColorRect";
		overlay.Color = new Color(0, 0, 0, 0.5f);
		overlay.SetAnchorsPreset(LayoutPreset.FullRect);
		AddChild(overlay);

		var panel = new PanelContainer();
		panel.Name = "Panel_PanelContainer";
		panel.CustomMinimumSize = new Vector2(400, 200);
		panel.SizeFlagsHorizontal = SizeFlags.ShrinkCenter;
		panel.SizeFlagsVertical = SizeFlags.ShrinkCenter;

		var center = new CenterContainer();
		center.SetAnchorsPreset(LayoutPreset.FullRect);
		overlay.AddChild(center);
		center.AddChild(panel);

		var vbox = new VBoxContainer();
		vbox.Name = "Vbox_VBoxContainer";
		panel.AddChild(vbox);

		var lblMsg = new Label();
		lblMsg.Name = "LblMsg_Label";
		lblMsg.Text = message;
		lblMsg.AutowrapMode = TextServer.AutowrapMode.WordSmart;
		vbox.AddChild(lblMsg);

		var btnConfirm = new Button();
		btnConfirm.Name = "BtnConfirm_Button";
		btnConfirm.Text = Tr("OK");
		btnConfirm.Pressed += () =>
		{
			overlay.QueueFree();
			onConfirm?.Invoke();
		};
		vbox.AddChild(btnConfirm);
	}

	private void OnStartPressed()
	{
		GameContext.UserInterface.PlayClickSound();
		string mapName = GameContext.Lobby.ActiveMapName ?? "melee";

		bool anyDownloading = false;
		foreach (var player in GameContext.Lobby.PlayerList)
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

		bool anyLoading = false;
		foreach (var player in GameContext.Lobby.PlayerList)
		{
			if (player.PeerId > 1 && !player.IsLoadReady)
			{
				anyLoading = true;
				break;
			}
		}

		if (anyLoading)
		{
			ShowLoadingClientsPopup();
			return;
		}

		bool anyNotReady = false;
		foreach (var player in GameContext.Lobby.PlayerList)
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
			GameContext.Lobby.StartGame(mapName);
		}
	}

	private void ShowVersionMismatchPopup(string hostVersion, string clientVersion)
	{
		string msg = $"{string.Format(Tr("Host Version: {0}"), hostVersion)}\n{string.Format(Tr("Client Version: {0}"), clientVersion)}\n\n{Tr("Connection aborted.")}";
		GameContext.UserInterface?.ShowPopupMessage(Tr("VERSION MISMATCH"), msg, () =>
		{
			GameContext.Lobby.Disconnect();
			GameContext.UserInterface.TransitionTo(GameScreen.LobbyBrowser);
		});
	}

	private void ShowStartWarningPopup(string mapName)
	{
		GameContext.UserInterface?.ShowConfirmationDialog(Tr("Not all players are ready, start anyway?"), () =>
		{
			GameContext.Lobby.StartGame(mapName, ignoreReadiness: true);
		}, confirmText: "START GAME", cancelText: "CANCEL", titleOverride: "NOT ALL PLAYERS READY");
	}

	private void ShowDownloadingClientsPopup()
	{
		GameContext.UserInterface?.ShowPopupMessage(Tr("CLIENTS DOWNLOADING MAP"), Tr("Some players are still downloading the map.\nWait for them or Kick them before starting the game."));
	}

	private void ShowLoadingClientsPopup()
	{
		GameContext.UserInterface?.ShowPopupMessage(Tr("CLIENTS LOADING MATCH"), Tr("Some players are still loading the match.\nWait for them or Kick them before starting the game."));
	}

	private void OnActiveMapChanged(string mapName)
	{
		UpdateSelectedMapUI();
		if (!GameContext.Lobby.IsHost && _downloadProgress != null && _downloadLabel != null)
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

		string? mapJsonPath = TryGetMapJsonPath(mapName);
		if (mapJsonPath == null) return;

		try
		{
			ExtractAuthorsFromJson(mapJsonPath);
			await VerifyAuthorSignatureAsync(mapJsonPath);
		}
		catch
		{
			_authorshipWarningLabel.Visible = true;
		}
	}

	private string? TryGetMapJsonPath(string mapName)
	{
		string? manifestPath = MapAssetManager.FindManifestPath(mapName);
		string mapDir = manifestPath != null ? Path.GetDirectoryName(manifestPath)! : ProjectSettings.GlobalizePath("user://maps/" + mapName);
		string mapJsonPath = Path.Combine(mapDir, "metadata.json");
		if (File.Exists(mapJsonPath)) return mapJsonPath;

		mapJsonPath = Path.Combine(mapDir, "manifest.json");
		return File.Exists(mapJsonPath) ? mapJsonPath : null;
	}

	private void ExtractAuthorsFromJson(string mapJsonPath)
	{
		string jsonContent = File.ReadAllText(mapJsonPath);
		var mapDoc = JsonNode.Parse(jsonContent);
		if (mapDoc == null) return;

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

	private async Task VerifyAuthorSignatureAsync(string mapJsonPath)
	{
		byte[] fileBytes = File.ReadAllBytes(mapJsonPath);
		string mapBlake3 = RealmMetadataHelper.ComputeBlake3(fileBytes, ".json");
		string hash = $"{mapBlake3}.json";

		string adminServerUrl = GameContext.Lobby.RegistryServerUrl;
		var assetAuthorRes = await _sharedHttpClient.GetAsync(adminServerUrl + "/api/publish_map/asset_author/" + hash);
		if (!assetAuthorRes.IsSuccessStatusCode)
		{
			_authorshipWarningLabel.Visible = true;
			return;
		}

		string assetAuthorJson = await assetAuthorRes.Content.ReadAsStringAsync();
		var assetMeta = JsonNode.Parse(assetAuthorJson);
		if (assetMeta == null)
		{
			_authorshipWarningLabel.Visible = true;
			return;
		}

		string author = assetMeta["AuthorUsername"]?.GetValue<string>() ?? "Unknown";
		string? signatureB64 = assetMeta["Signature"]?.GetValue<string>();
		string? pubKeyB64 = assetMeta["PublicKey"]?.GetValue<string>();

		if (string.IsNullOrEmpty(signatureB64) || string.IsNullOrEmpty(pubKeyB64))
		{
			_authorshipWarningLabel.Visible = true;
			return;
		}

		byte[] signatureBytes = Convert.FromBase64String(signatureB64);
		byte[] pubKeyBytes = Convert.FromBase64String(pubKeyB64);
		byte[] hashBytes = Encoding.UTF8.GetBytes(hash);

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

	private void SetupAttributionButton()
	{
		_attributionButton = new Button();
		_attributionButton.Name = "AttributionButton_Button";
		_attributionButton.Text = LocalizationManager.Translate("ATTRIBUTION");
		_attributionButton.Set("icon_max_width", 0);
		_attributionButton.CustomMinimumSize = new Vector2(160, 28);
		_attributionButton.SizeFlagsHorizontal = Control.SizeFlags.ShrinkCenter;
		_attributionButton.FocusMode = FocusModeEnum.None;
		_attributionButton.TooltipText = LocalizationManager.Translate("Show maps whose content was imported into this map");
		_attributionButton.AddThemeFontSizeOverride("font_size", 11);
		_attributionButton.AddThemeStyleboxOverride("normal", UIStyle.CreateButtonNormal());
		_attributionButton.AddThemeStyleboxOverride("hover", UIStyle.CreateButtonHover());
		_attributionButton.AddThemeStyleboxOverride("pressed", UIStyle.CreateButtonPressed());
		_attributionButton.AddThemeStyleboxOverride("focus", new StyleBoxEmpty());
		_attributionButton.Pressed += OnAttributionPressed;
		GetNode<VBoxContainer>("BriefingPanel/VBoxContainer").AddChild(_attributionButton);
	}

	private void OnAttributionPressed()
	{
		GameContext.UserInterface.PlayClickSound();

		if (_attributionPopover == null || !GodotObject.IsInstanceValid(_attributionPopover))
		{
			_attributionPopover = new AttributionPopover();
			AddChild(_attributionPopover);
			_attributionPopover.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
		}

		int selectedIndex = GetSelectedMapIndex();
		IReadOnlyList<string> sourceMapNames = selectedIndex >= 0
			? MapAttributionLookup.Resolve(_lobbyRoomMaps[selectedIndex].Attribution_SourceMap, _lobbyRoomMaps[selectedIndex].PathName, _lobbyRoomMaps[selectedIndex].Version)
			: Array.Empty<string>();

		_attributionPopover.ShowFor(_attributionButton, sourceMapNames);
	}

	private int GetSelectedMapIndex()
	{
		string currentMap = GameContext.Lobby.ActiveMapName ?? "melee";
		int selectedIndex = _lobbyRoomMaps.FindIndex(m => m.PathName == currentMap);
		if (selectedIndex >= 0) return selectedIndex;

		selectedIndex = _lobbyRoomMaps.FindIndex(m => m.PathName.ToLower().Contains(currentMap.ToLower()));
		if (selectedIndex >= 0) return selectedIndex;

		return _lobbyRoomMaps.Count > 0 ? 0 : -1;
	}

	private void UpdateSelectedMapUI()
	{
		int selectedIndex = GetSelectedMapIndex();
		if (selectedIndex >= 0)
		{
			_mapNameLabel.Text = _lobbyRoomMaps[selectedIndex].DisplayName.ToUpper();
			_briefingLabel.Text = _lobbyRoomMaps[selectedIndex].Description;
			VerifyMapAuthorship(_lobbyRoomMaps[selectedIndex].PathName);
		}

		_attributionPopover?.Close();
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
		if (GameContext.Lobby != null)
		{
			GameContext.Lobby.PlayerListUpdated -= PopulatePlayersList;
			GameContext.Lobby.ChatReceived -= OnLobbyChatReceived;
			GameContext.Lobby.ConnectionFailed -= OnLobbyConnectionFailed;
			GameContext.Lobby.KickReceived -= OnLobbyKickReceived;

			GameContext.Lobby.SpectatorDelayChanged -= OnSpectatorDelayChanged;

			GameContext.Lobby.MapDownloadProgressChanged -= OnMapDownloadProgress;
			GameContext.Lobby.MapDownloadCompleted -= OnMapDownloadCompleted;
			GameContext.Lobby.MapDownloadFailed -= OnMapDownloadFailed;

			GameContext.Lobby.CountdownStarted -= OnCountdownStarted;
			GameContext.Lobby.CountdownTick -= OnCountdownTick;
			GameContext.Lobby.CountdownCancelled -= OnCountdownCancelled;
			GameContext.Lobby.CountdownFinished -= OnCountdownFinished;
			GameContext.Lobby.HostStabilityUpdated -= UpdateHostStabilityLabel;
			GameContext.Lobby.ActiveMapChanged -= OnActiveMapChanged;
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
		UIStyle.ApplyTitle(titleLabel, "GAME STARTING", 26);
		titleLabel.AddThemeColorOverride("font_color", UIStyle.ColorGold);
		vbox.AddChild(titleLabel);

		var circleContainer = new CenterContainer();
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

		var cancelBtn = new Button();
		cancelBtn.Name = "CancelBtn_Button";
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
			GameContext.UserInterface.PlayClickSound();
			GameContext.Lobby.RequestCancelCountdown();
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

		if (GameContext.Lobby != null && GameContext.Lobby.IsHost)
		{
			GameContext.Lobby.UnregisterActiveLobbyFromRegistry();
		}
	}

	private void PopulatePlayersList()
	{
		if (!GodotObject.IsInstanceValid(this) || !IsInsideTree()) return;

		HideConnectingPopup();

		if (CheckHostVersionMismatch()) return;

		UpdateUnstableWarningLabel();

		var existingRows = GetExistingPlayerRows();
		var activePeerIds = SyncPlayerRows(existingRows);

		CleanupStalePlayerRows(existingRows, activePeerIds);

		UpdateAddBotAvailability();
		SetupStartButton();
	}

	private void UpdateAddBotAvailability()
	{
		if (_addBotButton == null || !GodotObject.IsInstanceValid(_addBotButton)) return;
		if (!GameContext.Lobby.IsHost) return;

		bool lobbyHasFreeSlot = GameContext.Lobby.PlayerList.Count < GameContext.Lobby.MaxPlayers;
		_addBotButton.Disabled = !lobbyHasFreeSlot;
		_playersTitle.Text = $"{TranslationServer.Translate("TEAMS & PLAYERS")} ({GameContext.Lobby.PlayerList.Count}/{GameContext.Lobby.MaxPlayers})";
	}

	private bool CheckHostVersionMismatch()
	{
		if (GameContext.Lobby.IsHost || _versionMismatchDetected) return false;

		var host = GameContext.Lobby.PlayerList.Find(p => p.IsHost);
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

		return existingRows;
	}

	private HashSet<int> SyncPlayerRows(Dictionary<int, PanelContainer> existingRows)
	{
		var activePeerIds = new HashSet<int>();

		for (int i = 0; i < GameContext.Lobby.PlayerList.Count; i++)
		{
			var player = GameContext.Lobby.PlayerList[i];
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

		return activePeerIds;
	}

	private void CleanupStalePlayerRows(Dictionary<int, PanelContainer> existingRows, HashSet<int> activePeerIds)
	{
		foreach (var kvp in existingRows)
		{
			if (!activePeerIds.Contains(kvp.Key))
			{
				kvp.Value.QueueFree();
			}
		}
	}

	private PanelContainer CreatePlayerRow(PlayerInfo p)
	{
		bool isLocalPlayer = CheckIfLocalPlayer(p);
		bool canEditSeat = isLocalPlayer || GameContext.Lobby.IsHost;

		var panel = CreatePlayerRowPanel();
		var hBox = new HBoxContainer();
		hBox.Name = "HBox_HBoxContainer";
		hBox.AddThemeConstantOverride("separation", 16);
		panel.AddChild(hBox);

		AddReadyCheckToRow(hBox, p, isLocalPlayer);
		AddTeamOptionToRow(hBox, p, canEditSeat);
		AddColorButtonToRow(hBox, p, canEditSeat);
		AddSeparatorToRow(hBox, 15);
		AddNameElementToRow(hBox, p, isLocalPlayer);
		AddFactionOptionToRow(hBox, p, canEditSeat);
		AddDiagnosticLabelToRow(hBox, p, isLocalPlayer);
		AddBootButtonToRow(hBox, p);
		AddSeparatorToRow(hBox, 10);

		return panel;
	}

	private bool CheckIfLocalPlayer(PlayerInfo p)
	{
		return GameContext.Lobby.IsLocalPlayer(p);
	}

	private static PlayerInfo? ResolveLiveSeat(PlayerInfo rowSeat)
	{
		return GameContext.Lobby?.PlayerList.Find(candidate => candidate.PeerId == rowSeat.PeerId);
	}

	private PlayerInfo? GetLocalPlayer()
	{
		if (GameContext.Lobby?.PlayerList == null) return null;
		return GameContext.Lobby.PlayerList.Find(CheckIfLocalPlayer);
	}

	private PanelContainer CreatePlayerRowPanel()
	{
		var panel = new PanelContainer();
		panel.Name = "Panel_PanelContainer";
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
		return panel;
	}

	private void AddReadyCheckToRow(HBoxContainer hBox, PlayerInfo p, bool isLocalPlayer)
	{
		if (p.PeerId < 1 && !isLocalPlayer) return;

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
			readyCheck.Toggled += (toggled) => OnReadyCheckToggled(toggled, p);
		}
		else
		{
			readyCheck.Disabled = true;
			readyCheck.AddThemeColorOverride("font_disabled_color", new Color(0.6f, 0.6f, 0.6f));
		}

		hBox.AddChild(readyCheck);

		AddSeparatorToRow(hBox, 10);
	}

	private void OnReadyCheckToggled(bool toggled, PlayerInfo p)
	{
		GameContext.UserInterface.PlayClickSound();
		p.IsReady = toggled;
		GameContext.Lobby.UpdateReadyState(p.PeerId, toggled);
		SetupStartButton();
	}

	private void AddSeparatorToRow(HBoxContainer hBox, int width)
	{
		var sep = new Control();
		sep.Name = "Sep_Control";
		sep.CustomMinimumSize = new Vector2(width, 0);
		hBox.AddChild(sep);
	}

	private void AddTeamOptionToRow(HBoxContainer hBox, PlayerInfo p, bool canEditSeat)
	{
		var optTeam = new OptionButton();
		optTeam.Name = "OptTeam";
		optTeam.CustomMinimumSize = new Vector2(95, 30);
		optTeam.SizeFlagsVertical = SizeFlags.ShrinkCenter;
		optTeam.Flat = false;

		ApplyTeamOptionStyle(optTeam);

		optTeam.AddItem(Tr("Team 1"));
		optTeam.AddItem(Tr("Team 2"));
		optTeam.AddItem(Tr("Spectator"));

		int teamIdx = p.Team == "Team 1" ? 0 : (p.Team == "Team 2" ? 1 : 2);
		optTeam.Select(teamIdx);

		optTeam.ItemSelected += (idx) => OnTeamOptionSelected(idx, p);
		optTeam.MouseEntered += () => GameContext.UserInterface.PlayHoverSound();

		if (!canEditSeat)
		{
			optTeam.Disabled = true;
			optTeam.AddThemeColorOverride("font_disabled_color", new Color(0.5f, 0.5f, 0.5f));
		}

		hBox.AddChild(optTeam);
	}

	private void ApplyTeamOptionStyle(OptionButton optTeam)
	{
		optTeam.AddThemeStyleboxOverride("normal", UIStyle.CreateButtonNormal());
		optTeam.AddThemeStyleboxOverride("hover", UIStyle.CreateButtonHover());
		optTeam.AddThemeStyleboxOverride("pressed", UIStyle.CreateButtonPressed());
		optTeam.AddThemeStyleboxOverride("focus", new StyleBoxEmpty());
		optTeam.AddThemeColorOverride("font_color", UIStyle.ColorGoldDull);
		optTeam.AddThemeColorOverride("font_hover_color", UIStyle.ColorGold);
		optTeam.AddThemeColorOverride("font_pressed_color", UIStyle.ColorCyanGlow);
		optTeam.AddThemeFontSizeOverride("font_size", 13);
	}

	private void OnTeamOptionSelected(long idx, PlayerInfo p)
	{
		GameContext.UserInterface.PlayClickSound();

		PlayerInfo? seat = ResolveLiveSeat(p);
		if (seat == null) return;

		seat.Team = idx == 0 ? "Team 1" : (idx == 1 ? "Team 2" : "Spectator");
		if (seat.Team == "Spectator")
		{
			seat.Faction = "SPECTATOR";
			seat.Color = new Color(0.5f, 0.5f, 0.5f);
		}
		else if (seat.Faction == "SPECTATOR")
		{
			seat.Faction = "HUMAN";
		}

		GameContext.Lobby.UpdatePlayerSlot(seat.PeerId, seat.Faction, seat.Team, seat.Color, seat.Name);
	}

	private void AddColorButtonToRow(HBoxContainer hBox, PlayerInfo p, bool canEditSeat)
	{
		var colorBtn = new Button();
		colorBtn.Name = "ColorBtn";
		colorBtn.CustomMinimumSize = new Vector2(24, 24);
		colorBtn.SizeFlagsVertical = SizeFlags.ShrinkCenter;

		var colorStyle = CreateColorStyle(p.Color);
		colorBtn.AddThemeStyleboxOverride("normal", colorStyle);
		colorBtn.AddThemeStyleboxOverride("hover", colorStyle);
		colorBtn.AddThemeStyleboxOverride("pressed", colorStyle);

		colorBtn.Pressed += () => OnColorButtonPressed(p, colorStyle, colorBtn);
		colorBtn.MouseEntered += () => GameContext.UserInterface.PlayHoverSound();

		if (!canEditSeat || p.Team == "Spectator")
		{
			colorBtn.Disabled = true;
		}

		hBox.AddChild(colorBtn);
	}

	private StyleBoxFlat CreateColorStyle(Color color)
	{
		var colorStyle = new StyleBoxFlat();
		colorStyle.BgColor = color;
		colorStyle.BorderColor = new Color(0.4f, 0.4f, 0.45f);
		colorStyle.SetBorderWidthAll(2);
		colorStyle.CornerRadiusTopLeft = 4;
		colorStyle.CornerRadiusTopRight = 4;
		colorStyle.CornerRadiusBottomLeft = 4;
		colorStyle.CornerRadiusBottomRight = 4;
		return colorStyle;
	}

	private void OnColorButtonPressed(PlayerInfo p, StyleBoxFlat colorStyle, Button colorBtn)
	{
		GameContext.UserInterface.PlayClickSound();

		PlayerInfo? seat = ResolveLiveSeat(p);
		if (seat == null) return;

		int nextIdx = (PlayerColorConfig.GetColorIndex(seat.Color) + 1) % PlayerColorConfig.AvailableColors.Count;
		seat.Color = PlayerColorConfig.AvailableColors[nextIdx];
		colorStyle.BgColor = seat.Color;
		colorBtn.AddThemeStyleboxOverride("normal", colorStyle);

		GameContext.Lobby.UpdatePlayerSlot(seat.PeerId, seat.Faction, seat.Team, seat.Color, seat.Name);
	}

	private void AddNameElementToRow(HBoxContainer hBox, PlayerInfo p, bool isLocalPlayer)
	{
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
				PlayerInfo? seat = ResolveLiveSeat(p);
				if (seat == null) return;

				seat.Name = text;
				GameContext.Lobby.UpdatePlayerSlot(seat.PeerId, seat.Faction, seat.Team, seat.Color, seat.Name);
			};
			hBox.AddChild(nameEdit);
		}
		else
		{
			var lblName = new Label();
			lblName.Name = "LblName";
			lblName.Text = GetPlayerRowDisplayName(p);
			lblName.SizeFlagsHorizontal = SizeFlags.ShrinkCenter;
			lblName.CustomMinimumSize = new Vector2(110, 0);
			lblName.AddThemeColorOverride("font_color", new Color(0.9f, 0.9f, 0.9f));
			lblName.AddThemeFontSizeOverride("font_size", 14);
			lblName.VerticalAlignment = VerticalAlignment.Center;
			hBox.AddChild(lblName);
		}
	}

	private void AddFactionOptionToRow(HBoxContainer hBox, PlayerInfo p, bool canEditSeat)
	{
		var optFaction = new OptionButton();
		optFaction.Name = "OptFaction";
		optFaction.CustomMinimumSize = new Vector2(110, 30);
		optFaction.SizeFlagsVertical = SizeFlags.ShrinkCenter;
		optFaction.Flat = false;

		ApplyTeamOptionStyle(optFaction);

		if (p.Team == "Spectator")
		{
			SetupSpectatorFactionOption(optFaction);
		}
		else
		{
			SetupPlayerFactionOption(optFaction, p, canEditSeat);
		}

		hBox.AddChild(optFaction);
	}

	private void SetupSpectatorFactionOption(OptionButton optFaction)
	{
		optFaction.AddItem(Tr("SPECTATOR"));
		optFaction.Select(0);
		optFaction.Disabled = true;
		optFaction.AddThemeColorOverride("font_disabled_color", new Color(0.5f, 0.5f, 0.5f));
	}

	private void SetupPlayerFactionOption(OptionButton optFaction, PlayerInfo p, bool canEditSeat)
	{
		optFaction.AddItem(Tr("HUMAN"));
		optFaction.AddItem(Tr("ORC"));
		optFaction.AddItem(Tr("UNDEAD"));
		optFaction.AddItem(Tr("ELF"));

		int selIdx = Array.IndexOf(_factions, p.Faction);
		if (selIdx >= 0) optFaction.Select(selIdx);

		optFaction.ItemSelected += (idx) =>
		{
			GameContext.UserInterface.PlayClickSound();

			PlayerInfo? seat = ResolveLiveSeat(p);
			if (seat == null) return;

			seat.Faction = _factions[idx];
			GameContext.Lobby.UpdatePlayerSlot(seat.PeerId, seat.Faction, seat.Team, seat.Color, seat.Name);
		};
		optFaction.MouseEntered += () => GameContext.UserInterface.PlayHoverSound();

		if (!canEditSeat)
		{
			optFaction.Disabled = true;
			optFaction.AddThemeColorOverride("font_disabled_color", new Color(0.5f, 0.5f, 0.5f));
		}
	}

	private void AddDiagnosticLabelToRow(HBoxContainer hBox, PlayerInfo p, bool isLocalPlayer)
	{
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
			diagLabel.MouseFilter = MouseFilterEnum.Pass;
			diagLabel.TooltipText = Tr("Host: Average connection with all players in lobby");
		}

		diagLabel.Text = BuildDiagnosticText(p, isLocalPlayer);
		hBox.AddChild(diagLabel);
	}

	private string BuildDiagnosticText(PlayerInfo p, bool isLocalPlayer)
	{
		if (!isLocalPlayer && !p.IsHost && !p.IsMapReady)
		{
			return $"  [color=#ffa040]{Tr("downloading...")}[/color]";
		}

		if (!isLocalPlayer && !p.IsHost && !p.IsLoadReady)
		{
			return $"  [color=#ffa040]{Tr("loading...")}[/color]";
		}

		string latencyText = p.Latency == "--" ? Tr("measuring...") : p.Latency;
		string jitterText = p.Jitter == "--" ? Tr("n/a") : p.Jitter;
		string lossText = p.PacketLoss == "--" ? Tr("n/a") : p.PacketLoss;

		string pingColor = GetPingColorCode(p.Latency);
		string jitterColor = GetJitterColorCode(p.Jitter);
		string lossColor = GetLossColorCode(p.PacketLoss);

		return $"  {Tr("Ping")}: [color={pingColor}]{latencyText}[/color] | {Tr("Jitter")}: [color={jitterColor}]{jitterText}[/color] | {Tr("Loss")}: [color={lossColor}]{lossText}[/color]";
	}

	private void AddBootButtonToRow(HBoxContainer hBox, PlayerInfo p)
	{
		if (!GameContext.Lobby.IsHost || GameContext.Lobby.IsLocalPlayer(p)) return;

		var bootBtn = new Button();
		bootBtn.Name = "BootBtn";
		bootBtn.Text = Tr("KICK");
		bootBtn.CustomMinimumSize = new Vector2(60, 26);
		bootBtn.SizeFlagsVertical = SizeFlags.ShrinkCenter;

		ApplyBootButtonStyle(bootBtn);

		bootBtn.Pressed += () =>
		{
			GameContext.UserInterface.PlayWarningSound();
			GameContext.Lobby.BootPlayer(p.PeerId);
		};

		hBox.AddChild(bootBtn);
	}

	private void ApplyBootButtonStyle(Button bootBtn)
	{
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
	}

	private void UpdatePlayerRow(PanelContainer row, PlayerInfo p)
	{
		var hBox = row.GetChildCount() > 0 ? row.GetChild(0) as HBoxContainer : null;
		if (hBox == null) return;

		bool isLocalPlayer = CheckIfLocalPlayer(p);
		bool canEditSeat = isLocalPlayer || GameContext.Lobby.IsHost;

		UpdateRowReadyCheck(hBox, p, isLocalPlayer);
		UpdateRowTeamOption(hBox, p, canEditSeat);
		UpdateRowColorButton(hBox, p, canEditSeat);
		UpdateRowNameElements(hBox, p);
		UpdateRowFactionOption(hBox, p, canEditSeat);
		UpdateRowDiagnosticLabel(hBox, p, isLocalPlayer);
	}

	private void UpdateRowReadyCheck(HBoxContainer hBox, PlayerInfo p, bool isLocalPlayer)
	{
		var readyCheck = hBox.GetNodeOrNull<CheckBox>("ReadyCheck");
		if (readyCheck == null) return;

		if (readyCheck.ButtonPressed != p.IsReady)
		{
			readyCheck.SetPressedNoSignal(p.IsReady);
		}

		readyCheck.Disabled = !isLocalPlayer;
	}

	private void UpdateRowTeamOption(HBoxContainer hBox, PlayerInfo p, bool canEditSeat)
	{
		var optTeam = hBox.GetNodeOrNull<OptionButton>("OptTeam");
		if (optTeam == null) return;

		int teamIdx = p.Team == "Team 1" ? 0 : (p.Team == "Team 2" ? 1 : 2);
		if (optTeam.Selected != teamIdx)
		{
			optTeam.Selected = teamIdx;
		}

		optTeam.Disabled = !canEditSeat;
	}

	private void UpdateRowColorButton(HBoxContainer hBox, PlayerInfo p, bool canEditSeat)
	{
		var colorBtn = hBox.GetNodeOrNull<Button>("ColorBtn");
		if (colorBtn == null) return;

		colorBtn.Disabled = !canEditSeat || p.Team == "Spectator";

		if (colorBtn.GetThemeStylebox("normal") is not StyleBoxFlat colorStyle) return;

		if (colorStyle.BgColor != p.Color)
		{
			colorStyle.BgColor = p.Color;
			colorBtn.AddThemeStyleboxOverride("normal", colorStyle);
		}
	}

	private void UpdateRowNameElements(HBoxContainer hBox, PlayerInfo p)
	{
		var nameEdit = hBox.GetNodeOrNull<LineEdit>("NameEdit");
		if (nameEdit != null && nameEdit.Text != p.Name)
		{
			nameEdit.Text = p.Name;
		}

		var lblName = hBox.GetNodeOrNull<Label>("LblName");
		if (lblName == null) return;

		string displayName = GetPlayerRowDisplayName(p);
		if (lblName.Text != displayName)
		{
			lblName.Text = displayName;
		}

		Color nameColor = p.IsDisconnected ? new Color(0.95f, 0.65f, 0.3f) : new Color(0.9f, 0.9f, 0.9f);
		if (lblName.GetThemeColor("font_color") != nameColor)
		{
			lblName.AddThemeColorOverride("font_color", nameColor);
		}
	}

	private static string GetPlayerRowDisplayName(PlayerInfo player)
	{
		return player.IsDisconnected
			? string.Format(TranslationServer.Translate("{0} (reconnecting)"), player.Name)
			: player.Name;
	}

	private void UpdateRowFactionOption(HBoxContainer hBox, PlayerInfo p, bool canEditSeat)
	{
		var optFaction = hBox.GetNodeOrNull<OptionButton>("OptFaction");
		if (optFaction == null) return;

		if (p.Team == "Spectator")
		{
			if (optFaction.ItemCount != 1)
			{
				optFaction.Clear();
				optFaction.AddItem(Tr("SPECTATOR"));
			}

			if (optFaction.Selected != 0) optFaction.Selected = 0;
			optFaction.Disabled = true;
			return;
		}

		if (optFaction.ItemCount != _factions.Length)
		{
			optFaction.Clear();
			foreach (string faction in _factions) optFaction.AddItem(Tr(faction));
		}

		optFaction.Disabled = !canEditSeat;

		int selIdx = Array.IndexOf(_factions, p.Faction);
		if (selIdx >= 0 && optFaction.Selected != selIdx)
		{
			optFaction.Selected = selIdx;
		}
	}

	private void UpdateRowDiagnosticLabel(HBoxContainer hBox, PlayerInfo p, bool isLocalPlayer)
	{
		var diagLabel = hBox.GetNodeOrNull<RichTextLabel>("DiagLabel");
		if (diagLabel == null) return;

		string newText = BuildDiagnosticText(p, isLocalPlayer);
		if (diagLabel.Text != newText)
		{
			diagLabel.Text = newText;
		}
	}

	private void OnChatSubmitted(string text)
	{
		if (string.IsNullOrEmpty(text.Trim())) return;
		_chatInput.Clear();
		string myName = GetLocalPlayer()?.Name ?? GameContext.Lobby.AuthenticatedUsername;
		GameContext.Lobby.SendChatMessage(myName, text);
	}

	private void OnLobbyChatReceived(string senderName, string message, bool alliesOnly, long timestampUtcTicks)
	{
		string myName = GetLocalPlayer()?.Name ?? GameContext.Lobby.AuthenticatedUsername;
		string color = senderName == "System" ? "#ffd700" : (senderName == myName ? "#5cd6ff" : "#d4a0a0");
		string timestamp = ChatPanel.FormatTimestamp(timestampUtcTicks);
		_chatLog.Text += $"[color=#a0a0a0][{timestamp}][/color] [color={color}]{senderName}[/color]: {message}\n";
	}

	private void OnLobbyConnectionFailed(string reason)
	{
		HideConnectingPopup();
		GD.PrintErr($"[LobbyRoom] Connection failed from lobby: {reason}");
		GameContext.UserInterface.PlayWarningSound();
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
			GameContext.Lobby.Disconnect();
			GameContext.UserInterface.TransitionTo(GameScreen.LobbyBrowser);
		});
	}

	private void OnLobbyKickReceived(string reason)
	{
		GD.Print($"[LobbyRoom] Disconnected / Kicked: {reason}");
		GameContext.UserInterface.PlayWarningSound();
		GameContext.UserInterface.TransitionTo(GameScreen.LobbyBrowser);
	}

	private ProgressBar _downloadProgress;
	private Label _downloadLabel;

	private void CreateDownloadProgressUI()
	{
		_downloadLabel = new Label();
		_downloadLabel.Name = "DownloadLabel_Label";
		_downloadLabel.Text = Tr("Checking map package...");
		_downloadLabel.Position = new Vector2(1450, 805);
		_downloadLabel.Size = new Vector2(340, 25);
		_downloadLabel.AddThemeColorOverride("font_color", UIStyle.ColorGoldDull);
		_downloadLabel.AddThemeFontSizeOverride("font_size", 14);
		AddChild(_downloadLabel);


		_downloadProgress = new ProgressBar();
		_downloadProgress.Name = "DownloadProgress_ProgressBar";
		_downloadProgress.Position = new Vector2(1450, 835);
		_downloadProgress.Size = new Vector2(340, 24);
		_downloadProgress.ShowPercentage = true;
		_downloadProgress.Value = 0;


		_downloadProgress.AddThemeStyleboxOverride("background", UIStyle.CreateSliderTrack());
		_downloadProgress.AddThemeStyleboxOverride("fill", UIStyle.CreateSliderFill());
		_downloadProgress.AddThemeColorOverride("font_color", new Color(1, 1, 1));
		_downloadProgress.AddThemeFontSizeOverride("font_size", 12);
		AddChild(_downloadProgress);

		bool isReady = !string.IsNullOrEmpty(GameContext.Lobby.ActiveMapName) && MapAssetManager.IsMapDownloaded(GameContext.Lobby.ActiveMapName);
		if (isReady)
		{
			_downloadProgress.Value = 100.0f;
			_downloadLabel.Text = Tr("Map package ready.");
			_downloadLabel.AddThemeColorOverride("font_color", UIStyle.ColorCyanGlow);
		}

		GameContext.Lobby.MapDownloadProgressChanged += OnMapDownloadProgress;
		GameContext.Lobby.MapDownloadCompleted += OnMapDownloadCompleted;
		GameContext.Lobby.MapDownloadFailed += OnMapDownloadFailed;

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

		GameContext.Lobby?.ReportLocalMapReadyState(true);
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

	private void OnStartButtonMouseEntered() => GameContext.UserInterface.PlayHoverSound();

	private void CreateSpectatorDelayUI()
	{
		_spectatorDelayCheck = new CheckBox();
		_spectatorDelayCheck.Name = "SpectatorDelayCheck_CheckBox";
		_spectatorDelayCheck.Text = Tr("Spectator Delay (5m)");
		_spectatorDelayCheck.Position = new Vector2(1450, 770);
		_spectatorDelayCheck.Size = new Vector2(340, 40);
		_spectatorDelayCheck.ButtonPressed = GameContext.Lobby.SpectatorDelay;
		_spectatorDelayCheck.Disabled = !GameContext.Lobby.IsHost;

		if (_connectingPopup != null && GodotObject.IsInstanceValid(_connectingPopup))
		{
			_spectatorDelayCheck.Visible = false;
		}

		UIStyle.ApplyCheckboxStyle(_spectatorDelayCheck);

		_spectatorDelayCheck.Pressed += () =>
		{
			GameContext.UserInterface.PlayClickSound();
			GameContext.Lobby.UpdateSpectatorDelay(_spectatorDelayCheck.ButtonPressed);
		};

		_spectatorDelayCheck.MouseEntered += () => GameContext.UserInterface.PlayHoverSound();
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
		foreach (var player in GameContext.Lobby.PlayerList)
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
		if (GameContext.Lobby == null || GameContext.Lobby.IsHost || _hostStabilityLabel == null)
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
		_connectingPopup.Name = "ConnectingPopup_Panel";
		_connectingPopup.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
		_connectingPopup.AddThemeStyleboxOverride("panel", UIStyle.CreateBgGradient());
		AddChild(_connectingPopup);

		var centerContainer = new CenterContainer();
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
