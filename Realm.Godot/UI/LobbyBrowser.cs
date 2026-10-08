using Godot;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Timer = Godot.Timer;
using Realm.Shared;

public partial class LobbyBrowser : Control
{
	public struct LobbyData
	{
		public string LobbyId;
		public string Map;
		public string Mode;
		public string Players;
		public int Ping;
		public string GameVersion;
		public long MapSizeBytes;
		public string MapSizeFormatted;
	}

	private List<LobbyData> _allLobbies = new List<LobbyData>();
	private List<LobbyData> _filteredLobbies = new List<LobbyData>();
	private bool _connectionFailed;

	private Panel _bgPanel;
	private Panel _leftPillar;
	private Panel _rightPillar;
	private PanelContainer _filterPanel;
	private PanelContainer _lobbyPanel;

	private Button _backButton;
	private Button _refreshButton;
	private Button _hostButton;
	private LineEdit _searchBar;
	private CheckBox _campaignCheck;
	private CheckBox _meleeCheck;
	private CheckBox _tutorialCheck;
	private CheckBox _arcadeCheck;
	private VBoxContainer _lobbyListContainer;

	private Label _browserTitle;
	private Label _filterTitle;
	private Label _mapCol;
	private Label _sizeCol;
	private Label _modeCol;
	private Label _playersCol;
	private Label _pingCol;
	private Label _refreshIcon;
	private Timer _refreshTimer;

	private readonly string[] _runes = { "ᚠ", "ᚢ", "ᚦ", "ᚨ", "ᚱ", "ᚲ", "ᚷ", "ᚹ", "ᚺ", "ᚾ", "ᛁ", "ᛃ", "ᛇ", "ᛈ", "ᛉ", "ᛊ", "ᛏ", "ᛒ", "ᛖ", "ᛗ", "ᛚ", "ᛜ", "ᛞ", "ᛟ" };
	private readonly System.Net.Http.HttpClient _httpClient = new System.Net.Http.HttpClient();

	public override void _Ready()
	{

		_bgPanel = GetNode<Panel>("Background");
		_leftPillar = GetNode<Panel>("LeftPillar");
		_rightPillar = GetNode<Panel>("RightPillar");
		_filterPanel = GetNode<PanelContainer>("FilterPanel");
		_lobbyPanel = GetNode<PanelContainer>("LobbyPanel");


		_backButton = GetNode<Button>("BackButton");
		_refreshButton = GetNode<Button>("RefreshButton");
		_hostButton = GetNode<Button>("HostButton");
		_searchBar = GetNode<LineEdit>("SearchBar");
		_campaignCheck = GetNode<CheckBox>("FilterPanel/VBoxContainer/CampaignCheck");
		_meleeCheck = GetNode<CheckBox>("FilterPanel/VBoxContainer/MeleeCheck");
		_tutorialCheck = GetNode<CheckBox>("FilterPanel/VBoxContainer/TutorialCheck");
		_arcadeCheck = GetNode<CheckBox>("FilterPanel/VBoxContainer/ArcadeCheck");
		_lobbyListContainer = GetNode<VBoxContainer>("LobbyPanel/VBoxContainer/ScrollContainer/LobbyListContainer");


		_browserTitle = GetNode<Label>("BrowserTitle");
		_filterTitle = GetNode<Label>("FilterPanel/VBoxContainer/FilterTitle");
		_mapCol = GetNode<Label>("LobbyPanel/VBoxContainer/TableHeader/MapCol");
		_sizeCol = GetNodeOrNull<Label>("LobbyPanel/VBoxContainer/TableHeader/SizeCol");
		_modeCol = GetNode<Label>("LobbyPanel/VBoxContainer/TableHeader/ModeCol");
		_playersCol = GetNode<Label>("LobbyPanel/VBoxContainer/TableHeader/PlayersCol");
		_pingCol = GetNode<Label>("LobbyPanel/VBoxContainer/TableHeader/PingCol");


		ApplyStyles();
		CenterFpsCounter();


		LobbyManager.Instance.NatTestCompleted += UpdateHostButtonState;
		UpdateHostButtonState();


		FetchLobbiesFromRegistry();

		_refreshTimer = new Timer();
		_refreshTimer.WaitTime = 30.0f;
		_refreshTimer.OneShot = false;
		_refreshTimer.Timeout += TriggerRefresh;
		AddChild(_refreshTimer);
		_refreshTimer.Start();

		if (!string.IsNullOrEmpty(LobbyManager.Instance.LobbyJoinError))
		{
			ShowLobbyJoinErrorPopup(LobbyManager.Instance.LobbyJoinError);
			LobbyManager.Instance.LobbyJoinError = null;
		}
	}

	private void ApplyStyles()
	{
		ApplyBackgroundStyle();
		_leftPillar.AddThemeStyleboxOverride("panel", UIStyle.CreatePillarPanel(true));
		_rightPillar.AddThemeStyleboxOverride("panel", UIStyle.CreatePillarPanel(false));
		ApplyFilterPanelStyle();
		ApplyMainPanelStyle();
		
		_searchBar.AddThemeStyleboxOverride("normal", UIStyle.CreateCustomLobbySearchInput(false));
		_searchBar.AddThemeStyleboxOverride("focus", UIStyle.CreateCustomLobbySearchInput(true));

		UIStyle.ApplyTitle(_browserTitle, "CUSTOM LOBBY BROWSER", 36);
		UIStyle.ApplyTitle(_filterTitle, "FILTER", 20);

		ApplyTableHeaderStyle();

		SetupBackButton();
		SetupRefreshButton();
		SetupHostButton();

		SetupCheckboxes();
		SetupSearchBar();

		PopulateRunicPillar(GetNode<VBoxContainer>("LeftPillar/RuneContainer"));
		PopulateRunicPillar(GetNode<VBoxContainer>("RightPillar/RuneContainer"));
	}

	private static Texture2D LoadFirstAvailableTexture(params string[] paths)
	{
		foreach (var path in paths)
		{
			if (!ResourceLoader.Exists(path)) continue;
			var tex = GD.Load<Texture2D>(path);
			if (tex != null) return tex;
		}
		return null;
	}

	private void ApplyBackgroundStyle()
	{
		Texture2D bgTexture = LoadFirstAvailableTexture(
			"res://Assets/UI/custom_lobby_bg.png",
			"res://Assets/UI/custom_lobby_bg.jpg",
			"res://Assets/UI/procedural_bg.png",
			"res://Assets/UI/lobby_bg_forest.png"
		);

		if (bgTexture != null)
		{
			var style = new StyleBoxTexture { Texture = bgTexture };
			_bgPanel.AddThemeStyleboxOverride("panel", style);
			_bgPanel.TextureFilter = CanvasItem.TextureFilterEnum.LinearWithMipmaps;
		}
		else
		{
			_bgPanel.AddThemeStyleboxOverride("panel", UIStyle.CreateBgGradient());
		}
	}

	private void ApplyFilterPanelStyle()
	{
		Texture2D filterPanelTex = LoadFirstAvailableTexture(
			"res://Assets/UI/custom_lobby_panel_filter.png",
			"res://Assets/UI/custom_panel_filter.png",
			"res://Assets/UI/procedural_filter_panel.png",
			"res://Assets/UI/lobby_filter_panel.png"
		);

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
			_filterPanel.TextureFilter = CanvasItem.TextureFilterEnum.LinearWithMipmaps;
		}
		else
		{
			_filterPanel.AddThemeStyleboxOverride("panel", UIStyle.CreateBackdropPanel());
		}
	}

	private void ApplyMainPanelStyle()
	{
		Texture2D mainPanelTex = LoadFirstAvailableTexture(
			"res://Assets/UI/custom_lobby_panel_main.png",
			"res://Assets/UI/procedural_main_panel.png",
			"res://Assets/UI/lobby_central_panel.png"
		);

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
			_lobbyPanel.TextureFilter = CanvasItem.TextureFilterEnum.LinearWithMipmaps;
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
		var checkBoxes = new[] { _campaignCheck, _meleeCheck, _tutorialCheck, _arcadeCheck };
		foreach (var cb in checkBoxes)
		{
			cb.Pressed += () => 
			{
				UIManager.Instance.PlayClickSound();
				ApplyFilters();
			};
			cb.MouseEntered += () => UIManager.Instance.PlayHoverSound();
			UIStyle.ApplyCheckboxStyle(cb);
		}
	}

	private void SetupSearchBar()
	{
		_searchBar.TextChanged += (text) => ApplyFilters();
		_searchBar.AddThemeStyleboxOverride("normal", UIStyle.CreateCustomLobbySearchInput(false));
		_searchBar.AddThemeStyleboxOverride("focus", UIStyle.CreateCustomLobbySearchInput(true));
		_searchBar.AddThemeFontSizeOverride("font_size", 18);
		_searchBar.AddThemeColorOverride("font_color", new Color(0.95f, 0.9f, 0.8f));
		_searchBar.AddThemeColorOverride("font_placeholder_color", new Color(0.65f, 0.6f, 0.5f));
		_searchBar.AddThemeColorOverride("caret_color", new Color(0.95f, 0.9f, 0.8f));
		_searchBar.CustomMinimumSize = new Vector2(660, 100);
		_searchBar.Alignment = HorizontalAlignment.Left;
		_searchBar.PlaceholderText = "Search Lobbies...";
		_searchBar.RightIcon = null;
	}

	private void SetupPillarButton(Button btn, string text, Action onClick)
	{
		btn.Flat = false;
		btn.Text = text;
		btn.AddThemeFontSizeOverride("font_size", 24);
		btn.AddThemeColorOverride("font_color", UIStyle.ColorGoldDull);
		btn.AddThemeColorOverride("font_hover_color", UIStyle.ColorGold);
		btn.AddThemeColorOverride("font_pressed_color", UIStyle.ColorCyanGlow);

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

	private void SetupHostButton()
	{
		_hostButton.Flat = false;
		_hostButton.CustomMinimumSize = new Vector2(300, 86);
		UIStyle.ApplyButtonText(_hostButton, "HOST A GAME", 24);
		_hostButton.AddThemeFontOverride("font", UIStyle.FontNorseBold);
		
		_hostButton.AddThemeStyleboxOverride("normal", UIStyle.CreateCustomLobbyStartGameButton(false, false));
		_hostButton.AddThemeStyleboxOverride("hover", UIStyle.CreateCustomLobbyStartGameButton(true, false));
		_hostButton.AddThemeStyleboxOverride("pressed", UIStyle.CreateCustomLobbyStartGameButton(false, true));
		_hostButton.AddThemeStyleboxOverride("focus", new StyleBoxEmpty());

		_hostButton.Pressed += () => 
		{
			UIManager.Instance.PlayClickSound();
			UIManager.Instance.TransitionTo(GameScreen.LobbyCreate);
		};
		_hostButton.MouseEntered += () => UIManager.Instance.PlayHoverSound();
	}

	private void SetupBackButton()
	{
		_backButton.Flat = false;
		_backButton.Text = "";
		_backButton.CustomMinimumSize = new Vector2(110, 110);
		_backButton.PivotOffset = new Vector2(55, 55);
		_backButton.AddThemeConstantOverride("icon_max_width", 110);

		_backButton.AddThemeStyleboxOverride("normal", UIStyle.CreateCustomLobbyBackButton(false, false));
		_backButton.AddThemeStyleboxOverride("hover", UIStyle.CreateCustomLobbyBackButton(true, false));
		_backButton.AddThemeStyleboxOverride("pressed", UIStyle.CreateCustomLobbyBackButton(false, true));
		_backButton.AddThemeStyleboxOverride("focus", new StyleBoxEmpty());

		_backButton.Pressed += () =>
		{
			UIManager.Instance.PlayClickSound();
			UIManager.Instance.TransitionTo(GameScreen.MainMenu);
		};
		_backButton.MouseEntered += () => UIManager.Instance.PlayHoverSound();
	}

	private void SetupRefreshButton()
	{
		_refreshButton.Flat = false;
		_refreshButton.Text = "";
		_refreshButton.CustomMinimumSize = new Vector2(110, 110);
		_refreshButton.PivotOffset = new Vector2(55, 55);
		_refreshButton.AddThemeConstantOverride("icon_max_width", 110);

		_refreshButton.AddThemeStyleboxOverride("normal", UIStyle.CreateCustomLobbyRechargeButton(false, false));
		_refreshButton.AddThemeStyleboxOverride("hover", UIStyle.CreateCustomLobbyRechargeButton(true, false));
		_refreshButton.AddThemeStyleboxOverride("pressed", UIStyle.CreateCustomLobbyRechargeButton(false, true));
		_refreshButton.AddThemeStyleboxOverride("focus", new StyleBoxEmpty());

		_refreshButton.Pressed += () =>
		{
			UIManager.Instance.PlayClickSound();
			TriggerRefresh();
		};
		_refreshButton.MouseEntered += () => UIManager.Instance.PlayHoverSound();
	}

	private void ShowHostingErrorPopup()
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
		UIStyle.ApplyTitle(titleLabel, Tr("HOSTING ERROR"), 20);
		titleLabel.AddThemeColorOverride("font_color", new Color(0.9f, 0.3f, 0.3f));
		vbox.AddChild(titleLabel);

		vbox.AddChild(new Control { CustomMinimumSize = new Vector2(0, 10) });

		var descLabel = new Label();
		descLabel.Text = Tr("Your network configuration is not compatible with hosting games.");
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
		};
		vbox.AddChild(okBtn);
	}

	private void UpdateHostButtonState()
	{
		_hostButton.Disabled = false;
		_hostButton.TooltipText = "";
		_hostButton.RemoveThemeColorOverride("font_disabled_color");
	}

	private void PopulateRunicPillar(VBoxContainer container)
	{
		container.Visible = false;
	}

	private void FetchLobbiesFromRegistry()
	{
		LobbyManager.Instance.RandomizeServerIndex();
		Task.Run(async () =>
		{
			try
			{
				var json = await LobbyManager.Instance.FetchLobbiesRawAsync();
				if (json == null)
				{
					Callable.From(() => OnLobbiesFetchedFallback()).CallDeferred();
					return;
				}

				int clientBaseline = await LobbyManager.Instance.MeasurePingToRegistryAsync();
				using var doc = JsonDocument.Parse(json);
				var lobbyList = new List<LobbyData>();
				
				foreach (var item in doc.RootElement.EnumerateArray())
				{
					lobbyList.Add(ParseLobbyData(item, clientBaseline));
				}

				Callable.From(() => OnLobbiesFetched(lobbyList)).CallDeferred();
			}
			catch (Exception ex)
			{
				GD.PrintErr($"[LobbyBrowser] Error fetching lobbies: {ex.Message}");
				Callable.From(() => OnLobbiesFetchedFallback()).CallDeferred();
			}
		});
	}

	private static LobbyData ParseLobbyData(JsonElement item, int clientBaseline)
	{
		string hostIp = "";
		if (item.TryGetProperty("hostIP", out var hostIpProp))
		{
			hostIp = hostIpProp.GetString() ?? "";
		}
		else if (item.TryGetProperty("hostIp", out hostIpProp))
		{
			hostIp = hostIpProp.GetString() ?? "";
		}

		int calculatedPing = CalculatePing(item, hostIp, clientBaseline);
		long mapSizeBytes = GetMapSizeBytes(item);
		
		string mapName = item.TryGetProperty("map", out var mapProp) ? mapProp.GetString() ?? "" : "";
		string mapVersion = item.TryGetProperty("mapVersion", out var mvProp) ? mvProp.GetString() ?? "" : "";

		if (mapSizeBytes <= 0)
		{
			mapSizeBytes = MapAssetManager.GetMapTotalSizeBytes(mapName, mapVersion);
		}

		return new LobbyData
		{
			LobbyId = item.TryGetProperty("lobbyId", out var idProp) ? idProp.GetString() ?? "" : "",
			Map = mapName,
			Mode = "Melee", // Default mode
			Players = $"{(item.TryGetProperty("slotsUsed", out var slotsProp) ? slotsProp.GetInt32() : 0)}/{(item.TryGetProperty("maxPlayers", out var maxProp) ? maxProp.GetInt32() : 8)}",
			Ping = calculatedPing,
			GameVersion = item.TryGetProperty("gameVersion", out var gvProp) ? gvProp.GetString() ?? "" : "",
			MapSizeBytes = mapSizeBytes,
			MapSizeFormatted = MapAssetManager.FormatSizeInMB(mapSizeBytes)
		};
	}

	private static int CalculatePing(JsonElement item, string hostIp, int clientBaseline)
	{
		if (hostIp == "127.0.0.1" || hostIp == "localhost" || string.IsNullOrEmpty(hostIp))
		{
			return 5;
		}

		double distanceKm = 0;
		if (item.TryGetProperty("distanceKm", out var distProp))
		{
			distanceKm = distProp.GetDouble();
		}
		else if (item.TryGetProperty("distance", out distProp))
		{
			distanceKm = distProp.GetDouble();
		}

		int hostPingBaseline = 20;
		if (item.TryGetProperty("hostPingBaseline", out var baselineProp))
		{
			hostPingBaseline = baselineProp.GetInt32();
		}

		double geoPing = 10.0 + (distanceKm / 100.0);
		double clientOverhead = Math.Max(0, clientBaseline - 20);
		double hostOverhead = Math.Max(0, hostPingBaseline - 20);
		return (int)Math.Round(geoPing + clientOverhead + hostOverhead);
	}

	private static long GetMapSizeBytes(JsonElement item)
	{
		if (item.TryGetProperty("mapSizeBytes", out var sizeProp) && sizeProp.ValueKind == JsonValueKind.Number)
		{
			return sizeProp.GetInt64();
		}
		if (item.TryGetProperty("MapSizeBytes", out sizeProp) && sizeProp.ValueKind == JsonValueKind.Number)
		{
			return sizeProp.GetInt64();
		}
		if (item.TryGetProperty("mapSize", out sizeProp) && sizeProp.ValueKind == JsonValueKind.Number)
		{
			return sizeProp.GetInt64();
		}
		return 0;
	}

	private void OnLobbiesFetched(List<LobbyData> list)
	{
		_connectionFailed = false;
		_allLobbies.Clear();
		if (list != null)
		{
			_allLobbies.AddRange(list);
		}
		ApplyFilters();
	}

	private void OnLobbiesFetchedFallback()
	{
		_connectionFailed = true;
		_allLobbies.Clear();
		ApplyFilters();
	}

	private void ApplyFilters()
	{
		_filteredLobbies = _allLobbies.ToList();

		string query = _searchBar.Text.Trim().ToLower();
		if (!string.IsNullOrEmpty(query))
		{
			_filteredLobbies = _filteredLobbies.Where(x => x.Map.ToLower().Contains(query)).ToList();
		}

		var activeModes = GetActiveModes();
		if (activeModes.Count > 0)
		{
			_filteredLobbies = _filteredLobbies.Where(x => activeModes.Contains(x.Mode.ToLower())).ToList();
		}

		RefreshLobbyDisplay();
	}

	private HashSet<string> GetActiveModes()
	{
		var modes = new HashSet<string>();
		if (_campaignCheck.ButtonPressed) modes.Add("campaign");
		if (_meleeCheck.ButtonPressed) modes.Add("melee");
		if (_tutorialCheck.ButtonPressed) modes.Add("tutorial");
		if (_arcadeCheck.ButtonPressed) modes.Add("arcade");
		return modes;
	}

	private void RefreshLobbyDisplay()
	{
		foreach (Node child in _lobbyListContainer.GetChildren())
		{
			child.QueueFree();
		}

		if (_connectionFailed)
		{
			var lbl = new Label();
			lbl.Text = Tr("⚠️ Failed to connect to the lobby registry server. Please check your internet connection or try again later.");
			lbl.HorizontalAlignment = HorizontalAlignment.Center;
			lbl.VerticalAlignment = VerticalAlignment.Center;
			lbl.AddThemeColorOverride("font_color", new Color(0.9f, 0.3f, 0.3f));
			lbl.AddThemeFontSizeOverride("font_size", 15);
			_lobbyListContainer.AddChild(lbl);
		}
		else if (_filteredLobbies.Count == 0)
		{
			var lbl = new Label();
			lbl.Text = Tr("Lobby server connected. No active games online. Host a game to start playing!");
			lbl.HorizontalAlignment = HorizontalAlignment.Center;
			lbl.VerticalAlignment = VerticalAlignment.Center;
			lbl.AddThemeColorOverride("font_color", new Color(0.75f, 0.75f, 0.8f));
			lbl.AddThemeFontSizeOverride("font_size", 15);
			_lobbyListContainer.AddChild(lbl);
		}
		else
		{
			foreach (var lobby in _filteredLobbies)
			{
				var row = CreateLobbyRow(lobby);
				_lobbyListContainer.AddChild(row);
			}
		}
	}

	private PanelContainer CreateLobbyRow(LobbyData data)
	{
		var panel = new PanelContainer();
		panel.CustomMinimumSize = new Vector2(0, 48);

		var rowStyle = new StyleBoxFlat();
		rowStyle.BgColor = new Color(0.12f, 0.13f, 0.16f, 0.3f);
		rowStyle.BorderColor = new Color(0.25f, 0.25f, 0.3f, 0.2f);
		rowStyle.SetBorderWidthAll(1);
		rowStyle.ContentMarginLeft = 12;
		rowStyle.ContentMarginRight = 24;
		rowStyle.ContentMarginTop = 0;
		rowStyle.ContentMarginBottom = 0;
		
		var hoverStyle = new StyleBoxFlat();
		hoverStyle.BgColor = new Color(0.95f, 0.82f, 0.55f, 0.12f); // Gold hover fill
		hoverStyle.BorderColor = UIStyle.ColorGold;
		hoverStyle.SetBorderWidthAll(1);
		hoverStyle.ContentMarginLeft = 12;
		hoverStyle.ContentMarginRight = 24;
		hoverStyle.ContentMarginTop = 0;
		hoverStyle.ContentMarginBottom = 0;

		panel.AddThemeStyleboxOverride("panel", rowStyle);

		var hBox = new HBoxContainer();
		panel.AddChild(hBox);

		var lblMap = new Label();
		lblMap.Text = $"  {data.Map}";
		lblMap.SizeFlagsHorizontal = SizeFlags.ExpandFill;
		lblMap.AddThemeColorOverride("font_color", new Color(0.9f, 0.85f, 0.7f));
		lblMap.AddThemeFontSizeOverride("font_size", 16);
		lblMap.VerticalAlignment = VerticalAlignment.Center;
		hBox.AddChild(lblMap);

		var lblSize = new Label();
		lblSize.Text = data.MapSizeFormatted;
		lblSize.SizeFlagsHorizontal = SizeFlags.ExpandFill;
		lblSize.AddThemeColorOverride("font_color", new Color(0.75f, 0.75f, 0.8f));
		lblSize.AddThemeFontSizeOverride("font_size", 15);
		lblSize.VerticalAlignment = VerticalAlignment.Center;
		hBox.AddChild(lblSize);

		var lblMode = new Label();
		lblMode.Text = data.Mode;
		lblMode.SizeFlagsHorizontal = SizeFlags.ExpandFill;
		lblMode.AddThemeColorOverride("font_color", new Color(0.75f, 0.75f, 0.8f));
		lblMode.AddThemeFontSizeOverride("font_size", 15);
		lblMode.VerticalAlignment = VerticalAlignment.Center;
		hBox.AddChild(lblMode);

		var lblPlayers = new Label();
		lblPlayers.Text = data.Players;
		lblPlayers.SizeFlagsHorizontal = SizeFlags.ExpandFill;
		lblPlayers.AddThemeColorOverride("font_color", new Color(0.75f, 0.75f, 0.8f));
		lblPlayers.AddThemeFontSizeOverride("font_size", 15);
		lblPlayers.VerticalAlignment = VerticalAlignment.Center;
		hBox.AddChild(lblPlayers);

		var lblPing = new Label();
		lblPing.Text = $"{data.Ping} ms  ";
		lblPing.SizeFlagsHorizontal = SizeFlags.ExpandFill;
		lblPing.HorizontalAlignment = HorizontalAlignment.Right;
		lblPing.AddThemeColorOverride("font_color", data.Ping < 80 ? new Color(0.3f, 0.8f, 0.4f) : new Color(0.85f, 0.65f, 0.3f));
		lblPing.AddThemeFontSizeOverride("font_size", 15);
		lblPing.VerticalAlignment = VerticalAlignment.Center;
		hBox.AddChild(lblPing);

		var lblVersion = new Label();
		lblVersion.Text = $"v{data.GameVersion}  ";
		lblVersion.SizeFlagsHorizontal = SizeFlags.ExpandFill;
		lblVersion.HorizontalAlignment = HorizontalAlignment.Right;
		lblVersion.AddThemeColorOverride("font_color", data.GameVersion == RealmVersion.GameBinaryVersion ? new Color(0.3f, 0.8f, 0.4f) : new Color(0.85f, 0.3f, 0.3f));
		lblVersion.AddThemeFontSizeOverride("font_size", 15);
		lblVersion.VerticalAlignment = VerticalAlignment.Center;
		hBox.AddChild(lblVersion);

		panel.MouseEntered += () => 
		{
			UIManager.Instance.PlayHoverSound();
			panel.AddThemeStyleboxOverride("panel", hoverStyle);
		};
		panel.MouseExited += () => panel.AddThemeStyleboxOverride("panel", rowStyle);

		panel.GuiInput += async (@event) =>
		{
			if (@event is InputEventMouseButton mouseBtn && mouseBtn.Pressed && mouseBtn.ButtonIndex == MouseButton.Left)
			{
				UIManager.Instance.PlayClickSound();
				
				if (!string.IsNullOrEmpty(data.GameVersion) && data.GameVersion != RealmVersion.GameBinaryVersion)
				{
					ShowVersionMismatchPopup(data.GameVersion, RealmVersion.GameBinaryVersion);
					return;
				}

				panel.MouseFilter = MouseFilterEnum.Ignore;
				LobbyManager.Instance.ActiveMapName = data.Map;
				bool success = await LobbyManager.Instance.JoinLobbyAsync(data.LobbyId);
				if (success)
				{
					UIManager.Instance.TransitionTo(GameScreen.LobbyRoom);
				}
				else
				{
					UIManager.Instance.PlayWarningSound();
					panel.MouseFilter = MouseFilterEnum.Stop;
				}
			}
		};

		return panel;
	}

	private void ShowVersionMismatchPopup(string lobbyVersion, string currentVersion)
	{
		var warningPopup = new Panel();
		warningPopup.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
		warningPopup.AddThemeStyleboxOverride("panel", UIStyle.CreateBgGradient());
		AddChild(warningPopup);

		var cardPanel = new Panel();
		cardPanel.CustomMinimumSize = new Vector2(500, 260);
		cardPanel.SetAnchorsAndOffsetsPreset(LayoutPreset.Center);
		cardPanel.AddThemeStyleboxOverride("panel", UIStyle.CreateStonePanel(true));
		warningPopup.AddChild(cardPanel);

		var vbox = new VBoxContainer();
		vbox.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
		vbox.CustomMinimumSize = new Vector2(450, 220);
		vbox.SizeFlagsHorizontal = SizeFlags.ExpandFill;
		vbox.SizeFlagsVertical = SizeFlags.ExpandFill;
		cardPanel.AddChild(vbox);

		vbox.AddChild(new Control { CustomMinimumSize = new Vector2(0, 20) });

		var titleLabel = new Label();
		UIStyle.ApplyTitle(titleLabel, Tr("VERSION MISMATCH"), 20);
		titleLabel.AddThemeColorOverride("font_color", new Color(0.9f, 0.3f, 0.3f));
		vbox.AddChild(titleLabel);

		vbox.AddChild(new Control { CustomMinimumSize = new Vector2(0, 10) });

		bool versionExistsLocally = System.IO.File.Exists(LobbyManager.GetVersionExecutablePath(lobbyVersion));

		var descLabel = new Label();
		string promptText = versionExistsLocally 
			? Tr("The running game version doesn't match the lobby. Re-launch game with correct version?") 
			: Tr("The required game version is not installed. Download it?");
		descLabel.Text = $"{string.Format(Tr("Host Version: {0}"), lobbyVersion)}\n{string.Format(Tr("Current Version: {0}"), currentVersion)}\n\n{promptText}";
		descLabel.HorizontalAlignment = HorizontalAlignment.Center;
		descLabel.AddThemeFontSizeOverride("font_size", 14);
		descLabel.AddThemeColorOverride("font_color", new Color(0.9f, 0.9f, 0.95f));
		vbox.AddChild(descLabel);

		vbox.AddChild(new Control { CustomMinimumSize = new Vector2(0, 15) });
		
		var hBox = new HBoxContainer();
		hBox.Alignment = BoxContainer.AlignmentMode.Center;
		hBox.AddThemeConstantOverride("separation", 20);
		vbox.AddChild(hBox);

		var cancelBtn = new Button();
		cancelBtn.Flat = false;
		cancelBtn.AddThemeConstantOverride("icon_max_width", 0);
		cancelBtn.AddThemeStyleboxOverride("normal", UIStyle.CreateButtonNormal());
		cancelBtn.AddThemeStyleboxOverride("hover", UIStyle.CreateButtonHover());
		cancelBtn.AddThemeStyleboxOverride("pressed", UIStyle.CreateButtonPressed());
		cancelBtn.AddThemeStyleboxOverride("focus", new StyleBoxEmpty());
		UIStyle.ApplyButtonText(cancelBtn, Tr("CANCEL"), 14);
		cancelBtn.CustomMinimumSize = new Vector2(160, 40);
		cancelBtn.Pressed += () =>
		{
			UIManager.Instance?.PlayClickSound();
			warningPopup.QueueFree();
		};
		hBox.AddChild(cancelBtn);

		var okBtn = new Button();
		okBtn.Flat = false;
		okBtn.AddThemeConstantOverride("icon_max_width", 0);
		okBtn.AddThemeStyleboxOverride("normal", UIStyle.CreateButtonNormal());
		okBtn.AddThemeStyleboxOverride("hover", UIStyle.CreateButtonHover());
		okBtn.AddThemeStyleboxOverride("pressed", UIStyle.CreateButtonPressed());
		okBtn.AddThemeStyleboxOverride("focus", new StyleBoxEmpty());
		UIStyle.ApplyButtonText(okBtn, versionExistsLocally ? Tr("RELAUNCH") : Tr("DOWNLOAD"), 14);
		okBtn.CustomMinimumSize = new Vector2(160, 40);
		okBtn.Pressed += () =>
		{
			UIManager.Instance?.PlayClickSound();
			warningPopup.QueueFree();
			
			if (versionExistsLocally)
			{
				string targetExe = LobbyManager.GetVersionExecutablePath(lobbyVersion);
				OS.CreateProcess(targetExe, new string[] {});
				GetTree().Quit();
			}
			else
			{
				OS.ShellOpen("https://github.com/RealmArcade/Realm/releases");
			}
		};
		hBox.AddChild(okBtn);
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
			fpsLabel.SetAnchorsPreset(Control.LayoutPreset.CenterTop);
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
			fpsLabel.SetAnchorsPreset(Control.LayoutPreset.TopRight);
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
		base._ExitTree();
	}

	private void ShowLobbyJoinErrorPopup(string message)
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
		UIStyle.ApplyTitle(titleLabel, Tr("CONNECTION ERROR"), 20);
		titleLabel.AddThemeColorOverride("font_color", new Color(0.9f, 0.3f, 0.3f));
		vbox.AddChild(titleLabel);

		vbox.AddChild(new Control { CustomMinimumSize = new Vector2(0, 10) });

		var descLabel = new Label();
		descLabel.Text = Tr(message);
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
		};
		vbox.AddChild(okBtn);
	}

	protected override void Dispose(bool disposing)
	{
		if (disposing)
		{
			if (LobbyManager.Instance != null)
			{
				LobbyManager.Instance.NatTestCompleted -= UpdateHostButtonState;
			}
			_httpClient.Dispose();
		}
		base.Dispose(disposing);
	}
}
