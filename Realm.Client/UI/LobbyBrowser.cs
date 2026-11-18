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
	private LineEdit _searchBar = null!;
	private readonly List<(string Tag, CheckBox CheckBox)> _tagFilters = new();
	private CheckBox _trustedOnlyCheck = null!;
	private VBoxContainer _lobbyListContainer = null!;

	private Label _browserTitle = null!;
	private Label _filterTitle = null!;
	private Label _mapCol = null!;
	private Label _sizeCol = null!;
	private Label _modeCol = null!;
	private Label _playersCol = null!;
	private Label _buildCol = null!;
	private Timer _refreshTimer = null!;

	private readonly string[] _runes = { "ᚠ", "ᚢ", "ᚦ", "ᚨ", "ᚱ", "ᚲ", "ᚷ", "ᚹ", "ᚺ", "ᚾ", "ᛁ", "ᛃ", "ᛇ", "ᛈ", "ᛉ", "ᛊ", "ᛏ", "ᛒ", "ᛖ", "ᛗ", "ᛚ", "ᛜ", "ᛞ", "ᛟ" };

	public override void _Ready()
	{
		(_bgPanel, _leftPillar, _rightPillar) = UIStyle.BindStandardChrome(this);
		_filterPanel = GetNode<PanelContainer>("FilterPanel");
		_lobbyPanel = GetNode<PanelContainer>("LobbyPanel");


		_backButton = GetNode<Button>("BackButton");
		_refreshButton = GetNode<Button>("RefreshButton");
		_hostButton = GetNode<Button>("HostButton");

		_searchBar = GetNode<LineEdit>("SearchBar");
		_trustedOnlyCheck = GetNode<CheckBox>("FilterPanel/VBoxContainer/TrustedOnlyCheck");
		_lobbyListContainer = GetNode<VBoxContainer>("LobbyPanel/VBoxContainer/ScrollContainer/LobbyListContainer");


		_browserTitle = GetNode<Label>("BrowserTitle");
		_filterTitle = GetNode<Label>("FilterPanel/VBoxContainer/FilterTitle");
		_mapCol = GetNode<Label>("LobbyPanel/VBoxContainer/TableHeader/MapCol");
		_sizeCol = GetNodeOrNull<Label>("LobbyPanel/VBoxContainer/TableHeader/SizeCol");
		_modeCol = GetNode<Label>("LobbyPanel/VBoxContainer/TableHeader/ModeCol");
		_playersCol = GetNode<Label>("LobbyPanel/VBoxContainer/TableHeader/PlayersCol");
		_buildCol = GetNode<Label>("LobbyPanel/VBoxContainer/TableHeader/BuildCol");


		ApplyStyles();
		CenterFpsCounter();


		LobbyManager? lobby = GameContext.Lobby;
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

		foreach (var lbl in new[] { _mapCol, _sizeCol, _modeCol, _playersCol, _buildCol })
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

		_trustedOnlyCheck.Text = TranslationServer.Translate("Trusted maps only");
		_trustedOnlyCheck.TooltipText = TranslationServer.Translate("Hide lobbies whose map Steam does not vouch for yet. Off by default: every lobby is listed.");
		_trustedOnlyCheck.ButtonPressed = false;
		SetupFilterCheckbox(_trustedOnlyCheck);

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

	/// <summary>
	///     States that a lobby is running a different build of the game and that Steam is what closes the gap. There is no
	///     installed copy of another build to run and no page to download one from, so nothing here offers either.
	/// </summary>
	private void ShowDifferentBuildNotice(string lobbyVersion, string currentVersion)
	{
		GameContext.UserInterface?.ShowPopupMessage(
			Tr("DIFFERENT GAME VERSION"),
			$"{string.Format(Tr("This lobby is running Realm {0}, and this screen is running {1}."), lobbyVersion, currentVersion)}\n\n{Tr("Steam keeps every player on the current build. Restart Realm through Steam to update it.")}");
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
	///     States a join that failed from the lobby list. Every failure the phase machine can name is answered with the one
	///     connectivity notice; a failure no phase names is stated as it arrived.
	/// </summary>
	/// <param name="message">What the lobby said went wrong.</param>
	private void ShowLobbyJoinErrorPopup(string message)
	{
		GameContext.UserInterface?.ShowPopupMessage(TranslationServer.Translate("CONNECTION FAILED"), message);
	}

	protected override void Dispose(bool disposing)
	{
		if (disposing)
		{
			}

		base.Dispose(disposing);
	}
}
