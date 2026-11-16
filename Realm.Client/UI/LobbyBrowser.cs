using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Godot;
using Realm.Client.Network;
using Realm.Shared;
using Realm.Shared.Reachability;
using HttpClient = System.Net.Http.HttpClient;
using Timer = Godot.Timer;
using static Godot.Control;
using static Godot.CanvasItem;
using Realm.Client.Utils;
using Realm.Client.Services;

namespace Realm.Client.UI;

[GlobalClass]
public partial class LobbyBrowser : Control
{
	public override void _EnterTree()
	{
		Realm.Client.Services.GameContext.EnsureServices();
	}


	private List<LobbyData> _allLobbies = new List<LobbyData>();
	private List<LobbyData> _filteredLobbies = new List<LobbyData>();

	/// <summary>How many listed lobbies the current filters hide only because they could not be shown to be reachable.</summary>
	private int _hiddenUnreachableCount;
	private bool _connectionFailed;

	private Panel _bgPanel = null!;
	private Panel _leftPillar = null!;
	private Panel _rightPillar = null!;
	private PanelContainer _filterPanel = null!;
	private PanelContainer _lobbyPanel = null!;

	private Button _backButton = null!;
	private Button _refreshButton = null!;
	private Button _hostButton = null!;
	private Button _directConnectButton = null!;
	private LineEdit _searchBar = null!;
	private readonly List<(string Tag, CheckBox CheckBox)> _tagFilters = new();
	private CheckBox _betaTestingMapsCheck = null!;
	private CheckBox _versionMismatchCheck = null!;
	private CheckBox _unreachableLobbiesCheck = null!;
	private VBoxContainer _lobbyListContainer = null!;

	private Label _browserTitle = null!;
	private Label _filterTitle = null!;
	private Label _mapCol = null!;
	private Label _sizeCol = null!;
	private Label _modeCol = null!;
	private Label _playersCol = null!;
	private Label _pingCol = null!;
	private Timer _refreshTimer = null!;

	private readonly string[] _runes = { "ᚠ", "ᚢ", "ᚦ", "ᚨ", "ᚱ", "ᚲ", "ᚷ", "ᚹ", "ᚺ", "ᚾ", "ᛁ", "ᛃ", "ᛇ", "ᛈ", "ᛉ", "ᛊ", "ᛏ", "ᛒ", "ᛖ", "ᛗ", "ᛚ", "ᛜ", "ᛞ", "ᛟ" };
	private readonly HttpClient _httpClient = new HttpClient();

	public override void _Ready()
	{
		(_bgPanel, _leftPillar, _rightPillar) = UIStyle.BindStandardChrome(this);
		_filterPanel = GetNode<PanelContainer>("FilterPanel");
		_lobbyPanel = GetNode<PanelContainer>("LobbyPanel");


		_backButton = GetNode<Button>("BackButton");
		_refreshButton = GetNode<Button>("RefreshButton");
		_hostButton = GetNode<Button>("HostButton");

		_directConnectButton = new Button();
		_directConnectButton.Name = "DirectConnectButton";
		_directConnectButton.FocusMode = FocusModeEnum.None;
		AddChild(_directConnectButton);

		_searchBar = GetNode<LineEdit>("SearchBar");
		_betaTestingMapsCheck = GetNode<CheckBox>("FilterPanel/VBoxContainer/BetaTestingMapsCheck");
		_versionMismatchCheck = GetNode<CheckBox>("FilterPanel/VBoxContainer/VersionMismatchCheck");
		_lobbyListContainer = GetNode<VBoxContainer>("LobbyPanel/VBoxContainer/ScrollContainer/LobbyListContainer");


		_browserTitle = GetNode<Label>("BrowserTitle");
		_filterTitle = GetNode<Label>("FilterPanel/VBoxContainer/FilterTitle");
		_mapCol = GetNode<Label>("LobbyPanel/VBoxContainer/TableHeader/MapCol");
		_sizeCol = GetNodeOrNull<Label>("LobbyPanel/VBoxContainer/TableHeader/SizeCol");
		_modeCol = GetNode<Label>("LobbyPanel/VBoxContainer/TableHeader/ModeCol");
		_playersCol = GetNode<Label>("LobbyPanel/VBoxContainer/TableHeader/PlayersCol");
		_pingCol = GetNode<Label>("LobbyPanel/VBoxContainer/TableHeader/PingCol");


		ApplyStyles();
		ApplyFestivalLayout();
		CenterFpsCounter();


		LobbyManager? lobby = GameContext.Lobby;
		if (lobby != null) lobby.NatTestCompleted += UpdateHostButtonState;
		UpdateHostButtonState();


		FetchLobbiesFromRegistry();

		_refreshTimer = new Timer();
		_refreshTimer.Name = "RefreshTimer_Timer";
		_refreshTimer.WaitTime = 30.0f;
		_refreshTimer.OneShot = false;
		_refreshTimer.Timeout += TriggerRefresh;
		AddChild(_refreshTimer);
		_refreshTimer.Start();

		if (lobby != null && !string.IsNullOrEmpty(lobby.LobbyJoinError))
		{
			ShowLobbyJoinErrorPopup(lobby.LobbyJoinError);
			lobby.LobbyJoinError = null;
		}
	}

	private void ApplyStyles()
	{
		ApplyBackgroundStyle();
		_leftPillar.AddThemeStyleboxOverride("panel", UIStyle.CreatePillarPanel(true));
		_rightPillar.AddThemeStyleboxOverride("panel", UIStyle.CreatePillarPanel(false));
		ApplyFilterPanelStyle();
		ApplyMainPanelStyle();

		UIStyle.ApplyCustomLobbySearchInput(_searchBar);

		UIStyle.ApplyTitle(_browserTitle, TranslationServer.Translate("CUSTOM LOBBY BROWSER"), 36);
		UIStyle.ApplyTitle(_filterTitle, TranslationServer.Translate("FILTER"), 20);

		ApplyTableHeaderStyle();

		SetupBackButton();
		SetupRefreshButton();
		SetupHostButton();
		SetupDirectConnectButton();

		SetupCheckboxes();
		SetupSearchBar();

		PopulateRunicPillar(GetNode<VBoxContainer>("LeftPillar/RuneContainer"));
		PopulateRunicPillar(GetNode<VBoxContainer>("RightPillar/RuneContainer"));
	}

	private void ApplyBackgroundStyle()
	{
		Texture2D? bgTexture = TrimmedTextureLoader.LoadTrimmedTexture("res://Assets/UI/custom_lobby_bg.webp");

		if (bgTexture != null)
		{
			var style = new StyleBoxTexture { Texture = bgTexture };
			_bgPanel.AddThemeStyleboxOverride("panel", style);
			_bgPanel.TextureFilter = TextureFilterEnum.LinearWithMipmaps;
		}
		else
		{
			_bgPanel.AddThemeStyleboxOverride("panel", UIStyle.CreateBgGradient());
		}
	}

	private void ApplyFestivalLayout()
	{
		_filterPanel.Visible = !BuildFlags.IS_AUTOBATTLE_FESTIVAL_ACTIVE;
		if (_filterPanel.Visible) return;

		_lobbyPanel.OffsetLeft = 130;
	}

	private void ApplyFilterPanelStyle()
	{
		Texture2D? filterPanelTex = TrimmedTextureLoader.LoadTrimmedTexture("res://Assets/UI/custom_lobby_panel_filter.webp");

		if (filterPanelTex != null)
		{
			var filterPanelStyle = new StyleBoxTexture
			{
				Texture = filterPanelTex,
				TextureMarginLeft = 40,
				TextureMarginRight = 40,
				TextureMarginTop = 40,
				TextureMarginBottom = 40,
				ContentMarginLeft = 46,
				ContentMarginRight = 46,
				ContentMarginTop = 44,
				ContentMarginBottom = 30
			};
			_filterPanel.AddThemeStyleboxOverride("panel", filterPanelStyle);
			_filterPanel.TextureFilter = TextureFilterEnum.LinearWithMipmaps;
		}
		else
		{
			_filterPanel.AddThemeStyleboxOverride("panel", UIStyle.CreateBackdropPanel());
		}
	}

	private void ApplyMainPanelStyle()
	{
		Texture2D? mainPanelTex = TrimmedTextureLoader.LoadTrimmedTexture("res://Assets/UI/custom_lobby_panel_main.webp");

		if (mainPanelTex != null)
		{
			var mainPanelStyle = new StyleBoxTexture
			{
				Texture = mainPanelTex,
				TextureMarginLeft = 60,
				TextureMarginRight = 60,
				TextureMarginTop = 60,
				TextureMarginBottom = 60,
				ContentMarginLeft = 50,
				ContentMarginRight = 50,
				ContentMarginTop = 45,
				ContentMarginBottom = 45
			};
			_lobbyPanel.AddThemeStyleboxOverride("panel", mainPanelStyle);
			_lobbyPanel.TextureFilter = TextureFilterEnum.LinearWithMipmaps;
		}
		else
		{
			_lobbyPanel.AddThemeStyleboxOverride("panel", UIStyle.CreateBackdropPanel());
		}
	}

	private void ApplyTableHeaderStyle()
	{
		var tableHeader = _lobbyPanel.GetNode<HBoxContainer>("VBoxContainer/TableHeader");
		var headerWrapper = new PanelContainer();
		headerWrapper.Name = "HeaderWrapper_PanelContainer";
		headerWrapper.AddThemeStyleboxOverride("panel", UIStyle.CreateBackdropPanel());
		tableHeader.GetParent().AddChild(headerWrapper);
		tableHeader.GetParent().MoveChild(headerWrapper, tableHeader.GetIndex());
		tableHeader.GetParent().RemoveChild(tableHeader);
		headerWrapper.AddChild(tableHeader);

		foreach (var lbl in new[] { _mapCol, _sizeCol, _modeCol, _playersCol, _pingCol })
		{
			if (lbl == null) continue;
			lbl.Text = Tr(lbl.Text);
			lbl.AddThemeColorOverride("font_color", UIStyle.ColorGold);
			lbl.AddThemeFontSizeOverride("font_size", 16);
		}
	}

	private void SetupCheckboxes()
	{
		var tagFilterColumn = GetNode<VBoxContainer>("FilterPanel/VBoxContainer/TagFilters");
		foreach (string tag in MapTagCatalog.MapFilters)
		{
			var checkBox = new CheckBox
			{
				Name = "TagCheck_" + tag.Replace(" ", string.Empty),
				Text = MapTagCatalog.GetDisplayName(tag),
				FocusMode = FocusModeEnum.None
			};
			SetupFilterCheckbox(checkBox);
			tagFilterColumn.AddChild(checkBox);
			_tagFilters.Add((tag, checkBox));
		}

		_betaTestingMapsCheck.Text = Tr("Show beta-testing maps");
		_betaTestingMapsCheck.TooltipText = Tr("Also list lobbies for maps that are still in Beta-Testing and have not been greenlit yet");
		_betaTestingMapsCheck.ButtonPressed = true;
		SetupFilterCheckbox(_betaTestingMapsCheck);

		_versionMismatchCheck.Text = TranslationServer.Translate("Show mismatched game version");
		_versionMismatchCheck.TooltipText = TranslationServer.Translate("Also list lobbies that run a different game version and cannot be joined from this build");
		_versionMismatchCheck.ButtonPressed = false;
		SetupFilterCheckbox(_versionMismatchCheck);

		_unreachableLobbiesCheck = new CheckBox
		{
			Name = "UnreachableLobbiesCheck",
			Text = TranslationServer.Translate("Show unreachable lobbies"),
			TooltipText = TranslationServer.Translate("Also list lobbies whose host could not be shown to be reachable from outside, which most players cannot join"),
			ButtonPressed = false,
			FocusMode = FocusModeEnum.None
		};
		SetupFilterCheckbox(_unreachableLobbiesCheck);
		var filterRows = GetNode<VBoxContainer>("FilterPanel/VBoxContainer");
		filterRows.AddChild(_unreachableLobbiesCheck);
		filterRows.MoveChild(_unreachableLobbiesCheck, _versionMismatchCheck.GetIndex() + 1);
	}

	private void SetupFilterCheckbox(CheckBox checkBox)
	{
		checkBox.Pressed += () =>
		{
			GameContext.UserInterface?.PlayClickSound();
			ApplyFilters();
		};
		checkBox.MouseEntered += () => GameContext.UserInterface?.PlayHoverSound();
		UIStyle.ApplyCheckboxStyle(checkBox);
	}

	private void SetupSearchBar()
	{
		_searchBar.TextChanged += (text) => ApplyFilters();
		UIStyle.ApplyCustomLobbySearchInput(_searchBar);
		_searchBar.AddThemeFontSizeOverride("font_size", 18);
		_searchBar.AddThemeColorOverride("font_color", new Color(0.95f, 0.9f, 0.8f));
		_searchBar.AddThemeColorOverride("font_placeholder_color", new Color(0.65f, 0.6f, 0.5f));
		_searchBar.AddThemeColorOverride("caret_color", new Color(0.95f, 0.9f, 0.8f));
		_searchBar.CustomMinimumSize = new Vector2(660, 100);
		_searchBar.Alignment = HorizontalAlignment.Left;
		_searchBar.PlaceholderText = "Search Lobbies...";
		_searchBar.RightIcon = null;
	}

	private void SetupHostButton()
	{
		_hostButton.Flat = false;
		_hostButton.CustomMinimumSize = new Vector2(300, 86);
		UIStyle.ApplyButtonText(_hostButton, TranslationServer.Translate("HOST A GAME"), 24);
		_hostButton.AddThemeFontOverride("font", UIStyle.FontNorseBold);

		UIStyle.ApplyCustomLobbyStartGameButton(_hostButton);

		_hostButton.Pressed += () =>
		{
			GameContext.UserInterface?.PlayClickSound();
			GameContext.UserInterface?.TransitionTo(GameScreen.LobbyCreate);
		};
		_hostButton.MouseEntered += () => GameContext.UserInterface?.PlayHoverSound();
	}

	private void SetupDirectConnectButton()
	{
		_directConnectButton.Flat = false;
		_directConnectButton.CustomMinimumSize = new Vector2(440, 86);
		_directConnectButton.Size = new Vector2(440, 86);
		_directConnectButton.Position = new Vector2(_hostButton.Position.X - 460, _hostButton.Position.Y);
		_directConnectButton.AddThemeConstantOverride("icon_max_width", 0);
		UIStyle.ApplyButtonText(_directConnectButton, Tr("Join Private Lobby via IP Address"), 20);
		_directConnectButton.AddThemeFontOverride("font", UIStyle.FontNorseBold);

		UIStyle.ApplyCustomLobbyStartGameButton(_directConnectButton);

		_directConnectButton.Pressed += () =>
		{
			GameContext.UserInterface?.PlayClickSound();
			ShowDirectConnectDialog();
		};
		_directConnectButton.MouseEntered += () => GameContext.UserInterface?.PlayHoverSound();
	}

	private const int MaximumRecentAddressesShown = 4;

	private enum DirectConnectAddressError
	{
		None,
		MissingAddress,
		MissingBracket,
		InvalidPort
	}

	/// <summary>
	///     Reads the address a host sent. Players paste the whole `address:port` the host's lobby room shows, and a player
	///     who only knows the address still reaches the default port, so both forms are accepted here rather than asking the
	///     player to split them apart.
	/// </summary>
	/// <param name="text">What the player typed or pasted.</param>
	/// <param name="host">The address to reach.</param>
	/// <param name="port">The port to reach it on.</param>
	/// <param name="error">What is wrong with the text, when anything is.</param>
	/// <returns>Whether the text states an address.</returns>
	private static bool TryParseDirectConnectAddress(string text, out string host, out int port, out DirectConnectAddressError error)
	{
		host = string.Empty;
		port = LobbyManager.DefaultGamePort;
		error = DirectConnectAddressError.None;

		string trimmed = text.Trim();
		if (trimmed.Length == 0)
		{
			error = DirectConnectAddressError.MissingAddress;
			return false;
		}

		string portText = string.Empty;
		if (trimmed.StartsWith('['))
		{
			int closingBracket = trimmed.IndexOf(']');
			if (closingBracket < 0)
			{
				error = DirectConnectAddressError.MissingBracket;
				return false;
			}

			host = trimmed[1..closingBracket];
			if (closingBracket + 1 < trimmed.Length && trimmed[closingBracket + 1] == ':') portText = trimmed[(closingBracket + 2)..];
		}
		else
		{
			int colon = trimmed.LastIndexOf(':');
			bool hasOneColon = colon == trimmed.IndexOf(':');
			if (hasOneColon && colon > 0)
			{
				host = trimmed[..colon];
				portText = trimmed[(colon + 1)..];
			}
			else
			{
				host = trimmed;
			}
		}

		host = host.Trim();
		if (host.Length == 0)
		{
			error = DirectConnectAddressError.MissingAddress;
			return false;
		}

		if (portText.Length > 0 && (!int.TryParse(portText, out port) || port < 1 || port > 65535))
		{
			error = DirectConnectAddressError.InvalidPort;
			return false;
		}

		return true;
	}

	private void ShowDirectConnectDialog()
	{
		var overlay = new Panel();
		overlay.Name = "Overlay_Panel";
		overlay.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
		overlay.AddThemeStyleboxOverride("panel", UIStyle.CreateBgGradient());
		AddChild(overlay);

		var center = UIStyle.CreateCenteredOverlay();
		overlay.AddChild(center);

		var cardPanel = new PanelContainer();
		cardPanel.Name = "CardPanel_PanelContainer";
		cardPanel.CustomMinimumSize = new Vector2(560, 0);
		cardPanel.AddThemeStyleboxOverride("panel", UIStyle.CreateStonePanel(true));
		center.AddChild(cardPanel);

		var vbox = new VBoxContainer();
		vbox.Name = "Vbox_VBoxContainer";
		vbox.CustomMinimumSize = new Vector2(520, 0);
		vbox.SizeFlagsHorizontal = SizeFlags.ExpandFill;
		vbox.SizeFlagsVertical = SizeFlags.ExpandFill;
		vbox.AddThemeConstantOverride("separation", 10);
		cardPanel.AddChild(vbox);

		var titleLabel = new Label();
		titleLabel.Name = "TitleLabel_Label";
		UIStyle.ApplyTitle(titleLabel, Tr("DIRECT CONNECTION"), 22);
		titleLabel.AddThemeColorOverride("font_color", UIStyle.ColorGold);
		titleLabel.HorizontalAlignment = HorizontalAlignment.Center;
		vbox.AddChild(titleLabel);

		var descLabel = new Label();
		descLabel.Name = "DescLabel_Label";
		descLabel.AutowrapMode = TextServer.AutowrapMode.WordSmart;
		descLabel.CustomMinimumSize = new Vector2(500, 0);
		descLabel.SizeFlagsHorizontal = SizeFlags.ExpandFill;
		descLabel.Text = Tr("Enter the address the host sent you. A host whose lobby is not listed reads it from the INVITE PLAYERS button in their lobby room; LAN, Tailscale, Radmin and port-forwarded hosts all work.");
		descLabel.HorizontalAlignment = HorizontalAlignment.Center;
		descLabel.AddThemeFontSizeOverride("font_size", 13);
		descLabel.AddThemeColorOverride("font_color", new Color(0.85f, 0.85f, 0.9f));
		vbox.AddChild(descLabel);

		var addressLabel = new Label();
		addressLabel.Name = "AddressLabel_Label";
		addressLabel.Text = Tr("Host address:");
		addressLabel.AddThemeColorOverride("font_color", UIStyle.ColorGoldDull);
		addressLabel.AddThemeFontSizeOverride("font_size", 13);
		vbox.AddChild(addressLabel);

		var addressInput = new LineEdit();
		addressInput.Name = "AddressInput_LineEdit";
		addressInput.PlaceholderText = "192.168.1.42:8999";
		addressInput.AddThemeStyleboxOverride("normal", UIStyle.CreateTextInput(false));
		addressInput.AddThemeStyleboxOverride("focus", UIStyle.CreateTextInput(true));
		addressInput.AddThemeColorOverride("font_color", new Color(0.95f, 0.9f, 0.8f));
		addressInput.CustomMinimumSize = new Vector2(500, 36);
		vbox.AddChild(addressInput);

		var hintLabel = new Label();
		hintLabel.Name = "AddressHint_Label";
		hintLabel.AutowrapMode = TextServer.AutowrapMode.WordSmart;
		hintLabel.CustomMinimumSize = new Vector2(500, 0);
		hintLabel.SizeFlagsHorizontal = SizeFlags.ExpandFill;
		hintLabel.Text = string.Format(Tr("The port is {0} when the address does not name one."), LobbyManager.DefaultGamePort);
		hintLabel.AddThemeFontSizeOverride("font_size", 11);
		hintLabel.AddThemeColorOverride("font_color", new Color(0.7f, 0.72f, 0.78f));
		vbox.AddChild(hintLabel);

		// A lobby that is not listed depends on an address the player was handed once, so the addresses this screen has
		// already joined are offered back instead of asking them to find the invitation again. The field starts at the most
		// recent of them, which makes rejoining the usual case a single press of CONNECT.
		List<DirectConnectHistoryEntry> recentAddresses = DirectConnectHistory.LoadRecent();
		if (recentAddresses.Count > 0)
		{
			var recentTitle = new Label();
			recentTitle.Name = "RecentAddressesTitle_Label";
			recentTitle.Text = Tr("RECENT");
			recentTitle.AddThemeColorOverride("font_color", UIStyle.ColorGoldDull);
			recentTitle.AddThemeFontSizeOverride("font_size", 12);
			vbox.AddChild(recentTitle);

			int shownAddresses = Math.Min(recentAddresses.Count, MaximumRecentAddressesShown);
			for (int index = 0; index < shownAddresses; index++)
			{
				DirectConnectHistoryEntry entry = recentAddresses[index];
				var recentButton = UIStyle.CreateActionButton($"RecentAddress{index}_Button", entry.Address, 13);
				recentButton.CustomMinimumSize = new Vector2(500, 32);
				recentButton.Alignment = HorizontalAlignment.Left;
				recentButton.AddThemeConstantOverride("icon_max_width", 0);
				LocalizedText.SetTooltip(recentButton, "Last joined {0}.", entry.LastJoinedUtc.ToLocalTime().ToString("g", CultureInfo.CurrentCulture));
				recentButton.Pressed += () =>
				{
					GameContext.UserInterface?.PlayClickSound();
					addressInput.Text = entry.Address;
					addressInput.CaretColumn = addressInput.Text.Length;
					addressInput.GrabFocus();
				};
				recentButton.MouseEntered += () => GameContext.UserInterface?.PlayHoverSound();
				vbox.AddChild(recentButton);
			}

			addressInput.Text = recentAddresses[0].Address;
			addressInput.CaretColumn = addressInput.Text.Length;
		}

		var errorLabel = new Label();
		errorLabel.Name = "ConnectError_Label";
		errorLabel.AutowrapMode = TextServer.AutowrapMode.WordSmart;
		errorLabel.CustomMinimumSize = new Vector2(500, 0);
		errorLabel.SizeFlagsHorizontal = SizeFlags.ExpandFill;
		errorLabel.AddThemeFontSizeOverride("font_size", 12);
		errorLabel.AddThemeColorOverride("font_color", new Color(0.95f, 0.4f, 0.4f));
		errorLabel.Visible = false;
		vbox.AddChild(errorLabel);

		var hbox = new HBoxContainer();
		hbox.Name = "Hbox_HBoxContainer";
		hbox.Alignment = BoxContainer.AlignmentMode.Center;
		hbox.AddThemeConstantOverride("separation", 20);
		vbox.AddChild(hbox);

		var cancelBtn = UIStyle.CreateActionButton("CancelBtn_Button", Tr("CANCEL"), 14);
		cancelBtn.CustomMinimumSize = new Vector2(140, 38);
		cancelBtn.Pressed += () =>
		{
			GameContext.UserInterface?.PlayClickSound();
			overlay.QueueFree();
		};
		hbox.AddChild(cancelBtn);

		var connectBtn = UIStyle.CreateActionButton("ConnectBtn_Button", Tr("CONNECT"), 14);
		connectBtn.CustomMinimumSize = new Vector2(140, 38);
		connectBtn.Pressed += connectButtonPressed;
		hbox.AddChild(connectBtn);
		addressInput.TextSubmitted += _ => connectButtonPressed();

		// What a refused connect names. The sentence is built from the address and from whatever the lobby said went wrong,
		// so the label draws it through a renderer that reads these: a language change rebuilds the sentence around the
		// values instead of leaving the one the attempt happened to be made in on screen.
		string? refusedEndpoint = null;
		string refusalReason = string.Empty;

		addressInput.GrabFocus();
		return;

		async void connectButtonPressed()
		{
			if (connectBtn.Disabled) return;

			if (!TryParseDirectConnectAddress(addressInput.Text, out string host, out int port, out DirectConnectAddressError parseError))
			{
				GameContext.UserInterface?.PlayWarningSound();
				refusedEndpoint = null;
				refusalReason = string.Empty;
				switch (parseError)
				{
					case DirectConnectAddressError.MissingBracket:
						LocalizedText.Set(errorLabel, "The address is missing its closing bracket.");
						break;
					case DirectConnectAddressError.InvalidPort:
						LocalizedText.Set(errorLabel, "The port must be a whole number between 1 and 65535.");
						break;
					default:
						LocalizedText.Set(errorLabel, "Enter the address the host sent you.");
						break;
				}

				errorLabel.Visible = true;
				return;
			}

			GameContext.UserInterface?.PlayClickSound();
			errorLabel.Visible = false;
			connectBtn.Disabled = true;
			addressInput.Editable = false;
			UIStyle.ApplyButtonText(connectBtn, Tr("CONNECTING..."), 14);

			if (GameContext.Lobby is not { } lobby) return;

			bool success = await lobby.JoinByIpAsync(host, port);
			if (!GodotObject.IsInstanceValid(this) || !GodotObject.IsInstanceValid(overlay)) return;

			if (success)
			{
				overlay.QueueFree();
				GameContext.UserInterface?.TransitionTo(GameScreen.LobbyRoom);
				return;
			}

			GameContext.UserInterface?.PlayWarningSound();
			connectBtn.Disabled = false;
			addressInput.Editable = true;
			UIStyle.ApplyButtonText(connectBtn, Tr("CONNECT"), 14);

			refusedEndpoint = DirectConnectHistory.DescribeAddress(host, port);
			refusalReason = lobby.LastHostError ?? lobby.LobbyJoinError ?? Tr("The host did not answer.");
			LocalizedText.Bind(errorLabel, RenderRefusedConnect);
			errorLabel.Visible = true;
		}

		string? RenderRefusedConnect()
		{
			if (refusedEndpoint == null) return null;

			return $"{string.Format(Tr("Could not reach {0}."), refusedEndpoint)}\n{refusalReason}\n\n{RestrictiveNetworkNotice.Text()}";
		}
	}

	private void SetupBackButton()
	{
		_backButton.Flat = false;
		_backButton.Text = "";
		_backButton.CustomMinimumSize = new Vector2(110, 110);
		_backButton.PivotOffset = new Vector2(55, 55);
		_backButton.AddThemeConstantOverride("icon_max_width", 110);

		UIStyle.ApplyCustomLobbyBackButton(_backButton);

		_backButton.Pressed += () =>
		{
			GameContext.UserInterface?.PlayClickSound();
			GameContext.UserInterface?.TransitionTo(GameScreen.MainMenu);
		};
		_backButton.MouseEntered += () => GameContext.UserInterface?.PlayHoverSound();
	}

	private void SetupRefreshButton()
	{
		_refreshButton.Flat = false;
		_refreshButton.Text = "";
		_refreshButton.CustomMinimumSize = new Vector2(110, 110);
		_refreshButton.PivotOffset = new Vector2(55, 55);
		_refreshButton.AddThemeConstantOverride("icon_max_width", 110);

		UIStyle.ApplyCustomLobbyRechargeButton(_refreshButton);

		_refreshButton.Pressed += () =>
		{
			GameContext.UserInterface?.PlayClickSound();
			TriggerRefresh();
		};
		_refreshButton.MouseEntered += () => GameContext.UserInterface?.PlayHoverSound();
	}

	private void UpdateHostButtonState()
	{
		if (!GodotObject.IsInstanceValid(this) || !IsInsideTree() || _hostButton == null || !GodotObject.IsInstanceValid(_hostButton))
		{
			return;
		}

		_hostButton.Disabled = false;
		_hostButton.TooltipText = "";
		_hostButton.RemoveThemeColorOverride("font_disabled_color");
	}

	private void PopulateRunicPillar(VBoxContainer container)
	{
		container.Visible = false;
	}

	private void ShowVersionMismatchPopup(string lobbyVersion, string currentVersion)
	{
		bool versionExistsLocally = File.Exists(LobbyManager.GetVersionExecutablePath(lobbyVersion));

		string promptText = versionExistsLocally
			? Tr("The running game version doesn't match the lobby. Re-launch game with correct version?")
			: Tr("The required game version is not installed. Download it?");
		string message = $"{string.Format(Tr("Host Version: {0}"), lobbyVersion)}\n{string.Format(Tr("Current Version: {0}"), currentVersion)}\n\n{promptText}";

		GameContext.UserInterface?.ShowConfirmationDialog(message, () =>
		{
			if (versionExistsLocally)
			{
				string targetExe = LobbyManager.GetVersionExecutablePath(lobbyVersion);
				OS.CreateProcess(targetExe, new string[] { });
				GetTree().Quit();
			}
			else
			{
				OS.ShellOpen("https://github.com/RealmArcade/Realm/releases");
			}
		}, confirmText: versionExistsLocally ? "RELAUNCH" : "DOWNLOAD", titleOverride: "VERSION MISMATCH");
	}

	private void TriggerRefresh()
	{
		if (_refreshButton != null && GodotObject.IsInstanceValid(_refreshButton))
		{
			var tween = CreateTween();
			tween.TweenProperty(_refreshButton, "rotation", _refreshButton.Rotation + Mathf.Pi * 2, 0.4f);
		}

		FetchLobbiesFromRegistry();
	}

	private void CenterFpsCounter()
	{
		var fpsLabel = GetTree().Root.FindChild("FPS", recursive: true, owned: false) as Label;
		if (fpsLabel != null)
		{
			fpsLabel.SetAnchorsPreset(LayoutPreset.CenterTop);
			fpsLabel.OffsetLeft = -60;
			fpsLabel.OffsetRight = 60;
			fpsLabel.OffsetTop = 10;
			fpsLabel.OffsetBottom = 40;
			fpsLabel.HorizontalAlignment = HorizontalAlignment.Center;
		}
	}

	private void RestoreFpsCounter()
	{
		var fpsLabel = GetTree().Root.FindChild("FPS", recursive: true, owned: false) as Label;
		if (fpsLabel != null)
		{
			fpsLabel.SetAnchorsPreset(LayoutPreset.TopRight);
			fpsLabel.OffsetLeft = -160;
			fpsLabel.OffsetRight = -10;
			fpsLabel.OffsetTop = 10;
			fpsLabel.OffsetBottom = 40;
			fpsLabel.HorizontalAlignment = HorizontalAlignment.Right;
		}
	}

	public override void _ExitTree()
	{
		RestoreFpsCounter();
		if (_refreshTimer != null)
		{
			_refreshTimer.Timeout -= TriggerRefresh;
		}

		if (GameContext.Lobby != null)
		{
			GameContext.Lobby.NatTestCompleted -= UpdateHostButtonState;
		}

		_httpClient?.Dispose();
		base._ExitTree();
	}

	private Panel ShowConnectingPopup()
	{
		var overlay = new Panel();
		overlay.Name = "ConnectingOverlay_Panel";
		overlay.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
		overlay.AddThemeStyleboxOverride("panel", UIStyle.CreateBgGradient());
		AddChild(overlay);

		var cardPanel = new Panel();
		cardPanel.Name = "CardPanel_Panel";
		cardPanel.CustomMinimumSize = new Vector2(440, 160);
		cardPanel.SetAnchorsAndOffsetsPreset(LayoutPreset.Center);
		cardPanel.AddThemeStyleboxOverride("panel", UIStyle.CreateStonePanel(true));
		overlay.AddChild(cardPanel);

		var vbox = new VBoxContainer();
		vbox.Name = "Vbox_VBoxContainer";
		vbox.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
		vbox.Alignment = BoxContainer.AlignmentMode.Center;
		cardPanel.AddChild(vbox);

		var titleLabel = new Label();
		titleLabel.Name = "TitleLabel_Label";
		UIStyle.ApplyTitle(titleLabel, Tr("CONNECTING TO LOBBY ..."), 22);
		titleLabel.AddThemeColorOverride("font_color", UIStyle.ColorGold);
		titleLabel.HorizontalAlignment = HorizontalAlignment.Center;
		vbox.AddChild(titleLabel);

		var statusLabel = new Label();
		statusLabel.Name = "StatusLabel_Label";
		statusLabel.Text = Tr("Traversing NAT and establishing connection...");
		statusLabel.HorizontalAlignment = HorizontalAlignment.Center;
		statusLabel.AddThemeFontSizeOverride("font_size", 13);
		statusLabel.AddThemeColorOverride("font_color", new Color(0.85f, 0.85f, 0.9f));
		vbox.AddChild(statusLabel);

		return overlay;
	}

	/// <summary>
	///     States a join that failed from the lobby list. A failure at a network step is answered with the one connectivity
	///     notice; a lobby that refused the seat is stated as it arrived, because it describes a problem the player does not
	///     have.
	/// </summary>
	/// <param name="message">What the lobby said went wrong.</param>
	private void ShowLobbyJoinErrorPopup(string message)
	{
		LobbyManager? lobby = GameContext.Lobby;
		string phase = lobby?.LastJoinPhase ?? string.Empty;
		bool networkFailure = lobby != null && JoinPhases.IsKnown(phase) && phase != JoinPhases.Admission && phase != JoinPhases.Session;

		if (networkFailure)
		{
			GameContext.UserInterface?.ShowPopupMessage(TranslationServer.Translate("CONNECTION FAILED"), RestrictiveNetworkNotice.Text());
			return;
		}

		GameContext.UserInterface?.ShowPopupMessage(TranslationServer.Translate("CONNECTION FAILED"), message);
	}

	protected override void Dispose(bool disposing)
	{
		if (disposing)
		{
			if (GameContext.Lobby != null)
			{
				GameContext.Lobby.NatTestCompleted -= UpdateHostButtonState;
			}

			_httpClient?.Dispose();
		}

		base.Dispose(disposing);
	}
}
