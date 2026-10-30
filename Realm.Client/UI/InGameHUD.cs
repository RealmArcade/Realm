using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Realm.Ecs;
using Godot;
using Realm.Client.Core;
using Realm.Client.Network;
using Realm.Client.ReplaySystem;
using Realm.Client.Services;
using Realm.Client.UI.InGame;
using Realm.Client.Utils;
using Realm.Ecs.Common;
using Realm.Ecs.Components.Terrain;
using Realm.Ecs.Services;
using Vector3 = Godot.Vector3;
using static Godot.Control;
using static Godot.CanvasItem;

namespace Realm.Client.UI;

[GlobalClass]
public partial class InGameHUD : Control
{
	public InGameHUD()
	{
		GameContext.RegisterInGameHud(this);
	}

	public override void _EnterTree()
	{
		Realm.Client.Services.GameContext.EnsureServices();
	}

	private InGameHUDViewModel _viewModel = new();

	public float Gold
	{
		get => _viewModel.Gold;
		set => _viewModel.Gold = value;
	}

	public float Wood
	{
		get => _viewModel.Wood;
		set => _viewModel.Wood = value;
	}

	public float Stone
	{
		get => _viewModel.Stone;
		set => _viewModel.Stone = value;
	}

	public float ResourceGatherMultiplier
	{
		get => _viewModel.ResourceGatherMultiplier;
		set => _viewModel.ResourceGatherMultiplier = value;
	}

	private PanelContainer _resourceContainer;
	private TextureRect _resourceBackground;
	private PanelContainer _bottomConsole;
	private TextureRect _consoleBackground;
	private PanelContainer _minimapFrame;
	private PanelContainer _portraitFrame;
	private PanelContainer _selectionFrame;
	private PanelContainer _commandFrame;
	private PanelContainer _inventoryFrame;
	private readonly Dictionary<HudElement, bool> _hudElementVisibility = new();

	private Shader _hudPanelTopShader;
	private Shader _hudPanelBottomShader;
	private Shader _hudPortraitMaskShader;

	private GridContainer _commandGrid;
	private GridContainer _inventoryGrid;
	public bool IsBuildSubMenuOpen => _viewModel.IsBuildSubMenuOpen;

	public void EnterBuildSubMenu()
	{
		_viewModel.IsBuildSubMenuOpen = true;
		if (GameContext.Host != null)
		{
			RefreshUI(GameContext.Host.SelectedUnits);
		}
	}

	public void ExitBuildSubMenu()
	{
		_viewModel.IsBuildSubMenuOpen = false;
		if (GameContext.Host != null)
		{
			RefreshUI(GameContext.Host.SelectedUnits);
		}
	}

	private VBoxContainer _productionBox;
	private Label _productionTitle;
	private ProgressBar _productionProgress;
	private Label _productionQueueLabel;
	private HBoxContainer _queueSlotsContainer;

	private VBoxContainer _minimapControls;
	private Button _btnZoom;
	private Button _btnToggleTerrain;
	private Button _btnToggleColorScheme;
	private Button _btnPing;
	private Button _btnCenter;
	private Button _btnSettings;
	private Button _btnQuitMatch;
	public bool ShowMinimapTerrain => _viewModel.ShowMinimapTerrain;
	public MinimapColorScheme MinimapColorScheme => _viewModel.MinimapColorScheme;


	private float _shroudMinimapRefreshTimer = 0f;

	private string _currentWeather
	{
		get
		{
			GameHost tempQualifier = GameContext.Host;
			return (tempQualifier != null ? GameContext.EnvironmentService : null)?.GetCurrentWeather() ?? "clear";
		}
		set
		{
			GameHost tempQualifier = GameContext.Host;
			(tempQualifier != null ? GameContext.EnvironmentService : null)?.SetCurrentWeather(value);
		}
	}

	private float _baseFogDensity
	{
		get
		{
			GameHost tempQualifier = GameContext.Host;
			return (tempQualifier != null ? GameContext.EnvironmentService : null)?.GetBaseFogDensity() ?? 0f;
		}
		set
		{
			GameHost tempQualifier = GameContext.Host;
			(tempQualifier != null ? GameContext.EnvironmentService : null)?.SetBaseFogDensity(value);
		}
	}

	private Label _goldLabel;
	private Label _woodLabel;
	private Label _stoneLabel;

	private List<Button> _unitButtons = new List<Button>();
	private HBoxContainer _unitsContainer;

	private HBoxContainer _statsContainer;
	private Label _statsLabel;
	private VBoxContainer _spellsBox;
	private VBoxContainer _itemsBox;

	private Vector2 _dragStart;
	private Vector2 _dragEnd;
	private bool _isDrawingDragBox = false;

	private Button _btnMove;
	private Button _btnStop;
	private Button _btnHold;
	private Button _btnBuild;

	private Label _feedbackLabel;
	private Label _connectionWarningLabel;
	private Control _minimapArea;
	private MinimapCameraIndicator _cameraIndicator;

	private Label _populationLabel;
	private Label _clockLabel;

	private VBoxContainer _customUIPanel;
	private PanelContainer _leaderboardPanel;
	private VBoxContainer _leaderboardSelectors;
	private VBoxContainer _leaderboardContent;
	private Label _leaderboardTitleLabel;
	private PanelContainer _countdownPanel;
	private Button _countdownCancelButton;
	private Label _countdownLabel;

	private Label _unitDescLabel;

	private HBoxContainer _controlGroupsContainer;
	private Button _btnSelectIdle;
	private Button _btnSelectArmy;

	private float _idlePulseTimer = 0f;
	private int _lastIdleCount = 0;

	private PanelContainer _hotkeyPanel;
	private bool _hotkeyPanelVisible = false;

	private Label _armyCompositionLabel;

	private float _incomeUpdateTimer = 0f;

	private Camera3D _camera3D;

	private PanelContainer _chatPanel;
	private LineEdit _chatInput;
	private RichTextLabel _chatLog;
	public bool IsChatActive => _chatPanelController.IsChatActive;

	public int LiveSpectatorPerspective
	{
		get
		{
			GameHost tempQualifier = GameContext.Host;
			return (tempQualifier != null ? GameContext.SpectatorService : null)?.GetSpectatorPerspective() ?? -1;
		}
		set
		{
			GameHost tempQualifier = GameContext.Host;
			(tempQualifier != null ? GameContext.SpectatorService : null)?.SetSpectatorPerspective(value);
		}
	}

	private ResourcePanel _resourcePanelController;
	private MinimapPanel _minimapPanelController;
	private ChatPanel _chatPanelController;
	private LeaderboardPanel _leaderboardPanelController;
	private PanelContainer _summaryPanel;
	private Label _summaryTitleLabel;
	private VBoxContainer _summaryContent;
	private SummaryTablePanel _summaryTablePanelController;
	private PortraitPanel _portraitPanelController;
	private CommandPanel _commandPanelController;
	private InventoryPanel _inventoryPanelController;
	private ControlGroupsUIController _controlGroupsUIController;

	private MinimapOverlay _minimapOverlay;
	private UnitHealthBarOverlay _unitHealthBarOverlay;

	private const float TacticalInterval = 0.04f;
	private const float MacroInterval = 0.1f;
	private const float AmbientInterval = 0.5f;

	private float _tacticalTimer = TacticalInterval;
	private float _macroTimer = MacroInterval;
	private float _ambientTimer = AmbientInterval;

	public void InvalidateCadence(bool tactical = true, bool macro = false, bool ambient = false)
	{
		if (tactical) _tacticalTimer = TacticalInterval;
		if (macro) _macroTimer = MacroInterval;
		if (ambient) _ambientTimer = AmbientInterval;
	}

	public void InvalidateReplicatedOverlays()
	{
		InvalidateCadence(tactical: false, macro: false, ambient: true);
	}

	public override void _Ready()
	{
		GameContext.RegisterInGameHud(this);

		InitializeReferences();
		SetupUnitHealthBarOverlay();
		SetupResourcePanel();
		SetupSelectionFrame();
		SetupCommandAndInventoryGrids();
		SetupWarningLabel();
		SetupMinimapControls();
		string currentWeather = GetCurrentWeather();
		SetupChatPanel();
		SetupCustomUIPanel();

		CreateStatsContainer();
		ApplyThemeStyles();
		SetupCommandCard();
		SetupPortrait();
		ApplyWeatherEffects(currentWeather);

		_feedbackLabel.Modulate = new Color(1, 1, 1, 0);
		Input.MouseMode = Input.MouseModeEnum.Visible;
		MouseFilter = MouseFilterEnum.Ignore;

		ApplyModeSpecificUI();

		Resized += OnHUDResized;
		UpdateBottomConsoleHeight();
		ApplyHUDScale();
		UpdateFPSVisibility();
		BuildHotkeyReferencePanel();

		SetupControllers();
		GenerateDynamicMinimap();

		ApplySpectatorAndReplayOverrides();
		ApplySavedHudVisibilityOverrides();
		SubscribeToLobbyCountdown();
	}

	private void SubscribeToLobbyCountdown()
	{
		LobbyManager? lobby = GameContext.Lobby;
		if (lobby == null) return;

		lobby.CountdownStarted += OnLobbyCountdownStarted;
		lobby.CountdownTick += OnLobbyCountdownTick;
		lobby.CountdownCancelled += StopCountdownTimer;
		lobby.CountdownFinished += StopCountdownTimer;

		if (lobby.IsCountdownActive) StartCountdownTimer(lobby.CountdownRemainingSeconds, TranslationServer.Translate("MATCH STARTS IN"));
	}

	private void UnsubscribeFromLobbyCountdown()
	{
		LobbyManager? lobby = GameContext.Lobby;
		if (lobby == null) return;

		lobby.CountdownStarted -= OnLobbyCountdownStarted;
		lobby.CountdownTick -= OnLobbyCountdownTick;
		lobby.CountdownCancelled -= StopCountdownTimer;
		lobby.CountdownFinished -= StopCountdownTimer;
	}

	private void OnLobbyCountdownStarted(string mapName, int seconds) =>
		StartCountdownTimer(seconds, TranslationServer.Translate("MATCH STARTS IN"));

	private void OnLobbyCountdownTick(int seconds) => _viewModel.CountdownDuration = seconds;

	private void SetupUnitHealthBarOverlay()
	{
		var overlay = new UnitHealthBarOverlay();
		overlay.Name = "UnitHealthBarOverlay";
		overlay.MouseFilter = MouseFilterEnum.Ignore;
		AddChild(overlay);
		overlay.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
		MoveChild(overlay, 0);
		_unitHealthBarOverlay = overlay;
	}

	private void InitializeReferences()
	{
		_resourceContainer = GetNode<PanelContainer>("ResourceContainer");
		_resourceBackground = GetNodeOrNull<TextureRect>("ResourceContainer/ResourceBackground");
		_bottomConsole = GetNode<PanelContainer>("BottomConsole");
		_consoleBackground = GetNodeOrNull<TextureRect>("BottomConsole/ConsoleBackground");
		_minimapFrame = GetNode<PanelContainer>("BottomConsole/HBox/MinimapFrame");
		_portraitFrame = GetNode<PanelContainer>("BottomConsole/HBox/PortraitFrame");
		_selectionFrame = GetNode<PanelContainer>("BottomConsole/HBox/SelectionFrame");
		_commandFrame = GetNode<PanelContainer>("BottomConsole/HBox/CommandFrame");
		_inventoryFrame = GetNodeOrNull<PanelContainer>("BottomConsole/HBox/InventoryFrame");

		_goldLabel = GetNode<Label>("ResourceContainer/HBox/GoldBox/GoldLabel");
		_woodLabel = GetNode<Label>("ResourceContainer/HBox/WoodBox/WoodLabel");
		_stoneLabel = GetNode<Label>("ResourceContainer/HBox/StoneBox/StoneLabel");
		_populationLabel = GetNode<Label>("ResourceContainer/HBox/SupplyBox/SupplyLabel");
		_clockLabel = GetNode<Label>("ResourceContainer/HBox/ClockBox/ClockLabel");
		_btnSettings = GetNode<Button>("ResourceContainer/HBox/BtnSettings");

		_feedbackLabel = GetNode<Label>("FeedbackLabel");

		_minimapArea = GetNode<Control>("BottomConsole/HBox/MinimapFrame/MinimapArea");
		_minimapArea.SizeFlagsHorizontal = SizeFlags.ExpandFill;
		_minimapArea.SizeFlagsVertical = SizeFlags.ExpandFill;
		_minimapArea.CustomMinimumSize = Vector2.Zero;
		_cameraIndicator = GetNode<MinimapCameraIndicator>("BottomConsole/HBox/MinimapFrame/MinimapArea/Indicator");
	}

	private void SetupResourcePanel()
	{
		if (_btnSettings != null)
		{
			_btnSettings.ExpandIcon = true;
			_btnSettings.Icon = UIStyle.LoadCrispIcon("res://Assets/UI/gear_icon.webp", 128);
			_btnSettings.TextureFilter = TextureFilterEnum.LinearWithMipmaps;
			_btnSettings.AddThemeConstantOverride("icon_max_width", 22);
			_btnSettings.TooltipText = TranslationServer.Translate("Settings (F12)");
			_btnSettings.AddThemeStyleboxOverride("normal", UIStyle.CreateHUDButtonStyle(false, false));
			_btnSettings.AddThemeStyleboxOverride("hover", UIStyle.CreateHUDButtonStyle(true, false));
			_btnSettings.AddThemeStyleboxOverride("pressed", UIStyle.CreateHUDButtonStyle(false, true));
			_btnSettings.AddThemeStyleboxOverride("focus", new StyleBoxEmpty());
			_btnSettings.AddThemeColorOverride("icon_normal_color", UIStyle.ColorGoldDull);
			_btnSettings.AddThemeColorOverride("icon_hover_color", UIStyle.ColorGold);
			_btnSettings.AddThemeColorOverride("icon_pressed_color", UIStyle.ColorCyanGlow);
			_btnSettings.AddThemeColorOverride("icon_focus_color", UIStyle.ColorGoldDull);
			_btnSettings.Pressed += () =>
			{
				GameContext.UserInterface?.PlayClickSound();
				GameContext.UserInterface?.OpenSettingsOverlay();
			};
		}

		SetupPauseMatchButton();
		SetupQuitMatchButton();
	}

	private Button _btnPauseMatch;

	/// <summary>
	///     The pause control every player has: it asks the match to hold where it is, and while the match is held it asks
	///     for the resume countdown that belongs to everyone. What a player is still allowed to ask for is the match's
	///     answer, so the button reports the match's state rather than deciding anything itself.
	/// </summary>
	private void SetupPauseMatchButton()
	{
		if (_btnSettings == null) return;

		_btnPauseMatch = new Button();
		_btnPauseMatch.Name = "BtnPauseMatch";
		_btnPauseMatch.Set("icon_max_width", 0);
		_btnPauseMatch.Text = UnicodeIcons.PAUSE;
		_btnPauseMatch.CustomMinimumSize = new Vector2(32, 32);
		_btnPauseMatch.SizeFlagsVertical = SizeFlags.ShrinkCenter;
		_btnPauseMatch.FocusMode = FocusModeEnum.None;
		Font pauseIconFont = UIStyle.FontAwesome;
		if (pauseIconFont != null)
		{
			_btnPauseMatch.AddThemeFontOverride("font", pauseIconFont);
		}

		_btnPauseMatch.AddThemeFontSizeOverride("font_size", 16);
		_btnPauseMatch.AddThemeColorOverride("font_color", UIStyle.ColorGoldDull);
		_btnPauseMatch.AddThemeColorOverride("font_hover_color", UIStyle.ColorGold);
		_btnPauseMatch.AddThemeColorOverride("font_pressed_color", UIStyle.ColorCyanGlow);
		_btnPauseMatch.AddThemeColorOverride("font_focus_color", UIStyle.ColorGoldDull);
		_btnPauseMatch.AddThemeStyleboxOverride("normal", UIStyle.CreateHUDButtonStyle(false, false));
		_btnPauseMatch.AddThemeStyleboxOverride("hover", UIStyle.CreateHUDButtonStyle(true, false));
		_btnPauseMatch.AddThemeStyleboxOverride("pressed", UIStyle.CreateHUDButtonStyle(false, true));
		_btnPauseMatch.AddThemeStyleboxOverride("focus", new StyleBoxEmpty());
		_btnPauseMatch.TooltipText = TranslationServer.Translate("Pause Match [P]");
		_btnPauseMatch.Pressed += OnPauseMatchButtonPressed;
		_btnPauseMatch.MouseEntered += () => GameContext.UserInterface?.PlayHoverSound();

		var resourceRow = _btnSettings.GetParent();
		resourceRow.AddChild(_btnPauseMatch);
		resourceRow.MoveChild(_btnPauseMatch, _btnSettings.GetIndex());
	}

	private void OnPauseMatchButtonPressed()
	{
		GameContext.UserInterface?.PlayClickSound();
		GameContext.Host?.TogglePauseRequest();
	}

	private void RefreshPauseMatchButton()
	{
		if (_btnPauseMatch == null || !GodotObject.IsInstanceValid(_btnPauseMatch)) return;

		var network = GameContext.NetworkService;
		bool paused = network != null && network.IsPaused;

		_btnPauseMatch.Text = paused ? UnicodeIcons.PLAY : UnicodeIcons.PAUSE;
		_btnPauseMatch.TooltipText = paused
			? TranslationServer.Translate("Resume Match [P]")
			: string.Format(TranslationServer.Translate("Pause Match [P] - {0} pause(s) left this match"), network?.RemainingPauses ?? 0);

		// The cooldown is deliberately left to the authority: the button that could tell a player it is still recharging is
		// the one it has to press to be told, so only a player with nothing left to spend is stopped here.
		_btnPauseMatch.Disabled = !paused && network != null && network.RemainingPauses <= 0;
	}

	private void SetupQuitMatchButton()
	{
		if (_btnSettings == null) return;

		_btnQuitMatch = new Button();
		_btnQuitMatch.Name = "BtnQuitMatch";
		_btnQuitMatch.Set("icon_max_width", 0);
		_btnQuitMatch.Text = UnicodeIcons.SIGN_OUT;
		_btnQuitMatch.TooltipText = TranslationServer.Translate("Quit Match");
		_btnQuitMatch.CustomMinimumSize = new Vector2(32, 32);
		_btnQuitMatch.SizeFlagsVertical = SizeFlags.ShrinkCenter;
		_btnQuitMatch.FocusMode = FocusModeEnum.None;
		Font quitIconFont = UIStyle.FontAwesome;
		if (quitIconFont != null)
		{
			_btnQuitMatch.AddThemeFontOverride("font", quitIconFont);
		}

		_btnQuitMatch.AddThemeFontSizeOverride("font_size", 16);
		_btnQuitMatch.AddThemeColorOverride("font_color", UIStyle.ColorGoldDull);
		_btnQuitMatch.AddThemeColorOverride("font_hover_color", UIStyle.ColorGold);
		_btnQuitMatch.AddThemeColorOverride("font_pressed_color", UIStyle.ColorCyanGlow);
		_btnQuitMatch.AddThemeColorOverride("font_focus_color", UIStyle.ColorGoldDull);
		_btnQuitMatch.AddThemeStyleboxOverride("normal", UIStyle.CreateHUDButtonStyle(false, false));
		_btnQuitMatch.AddThemeStyleboxOverride("hover", UIStyle.CreateHUDButtonStyle(true, false));
		_btnQuitMatch.AddThemeStyleboxOverride("pressed", UIStyle.CreateHUDButtonStyle(false, true));
		_btnQuitMatch.AddThemeStyleboxOverride("focus", new StyleBoxEmpty());
		_btnQuitMatch.Pressed += OnQuitMatchPressed;
		_btnQuitMatch.MouseEntered += () => GameContext.UserInterface?.PlayHoverSound();

		var resourceRow = _btnSettings.GetParent();
		resourceRow.AddChild(_btnQuitMatch);
		resourceRow.MoveChild(_btnQuitMatch, _btnSettings.GetIndex() + 1);
	}

	private void OnQuitMatchPressed()
	{
		GameContext.UserInterface?.PlayClickSound();
		GameContext.UserInterface?.ShowConfirmationDialog(
			TranslationServer.Translate("Are you sure you want to quit the current match and return to the main menu?"),
			QuitMatchToMainMenu,
			confirmText: "QUIT",
			cancelText: "CANCEL");
	}

	private void QuitMatchToMainMenu()
	{
		GameContext.SessionState.IsTestMode = false;
		GameContext.Lobby?.Disconnect();
		GameContext.UserInterface?.TransitionTo(GameScreen.MainMenu);
	}

	private void SetupSelectionFrame()
	{
		_unitsContainer = GetNode<HBoxContainer>("BottomConsole/HBox/SelectionFrame/UnitsContainer");

		foreach (Node child in _unitsContainer.GetChildren())
		{
			_unitsContainer.RemoveChild(child);
			child.QueueFree();
		}
		_unitButtons.Clear();

		for (int i = 0; i < 12; i++)
		{
			var btn = new Button();
			btn.Name = $"Unit{i + 1}";
			btn.CustomMinimumSize = new Vector2(36, 36);
			btn.SizeFlagsHorizontal = SizeFlags.ExpandFill;
			btn.ExpandIcon = true;
			btn.FocusMode = FocusModeEnum.None;
			btn.AddThemeConstantOverride("icon_max_width", 30);
			btn.AddThemeStyleboxOverride("normal", UIStyle.CreateButtonNormal());
			btn.AddThemeStyleboxOverride("hover", UIStyle.CreateButtonHover());
			btn.AddThemeStyleboxOverride("pressed", UIStyle.CreateButtonPressed());
			btn.AddThemeStyleboxOverride("focus", new StyleBoxEmpty());

			var hpBar = new ProgressBar();
			hpBar.Name = "HealthBar";
			hpBar.CustomMinimumSize = new Vector2(36, 4);
			hpBar.ShowPercentage = false;
			hpBar.SetAnchorsAndOffsetsPreset(LayoutPreset.BottomWide);
			hpBar.OffsetTop = -6;
			hpBar.AddThemeStyleboxOverride("background", UIStyle.CreateSliderTrack());
			hpBar.AddThemeStyleboxOverride("fill", UIStyle.CreateSliderFill());
			hpBar.Visible = false;
			btn.AddChild(hpBar);

			var statusLbl = new Label();
			statusLbl.Name = "StatusIcon";
			statusLbl.Text = "";
			statusLbl.AddThemeFontSizeOverride("font_size", 10);
			statusLbl.AddThemeColorOverride("font_color", new Color(1f, 0.9f, 0.2f));
			statusLbl.SetAnchorsAndOffsetsPreset(LayoutPreset.TopLeft);
			statusLbl.Visible = false;
			btn.AddChild(statusLbl);

			_unitsContainer.AddChild(btn);
			_unitButtons.Add(btn);
		}
	}

	private void SetupCommandAndInventoryGrids()
	{
		_commandGrid = GetNode<GridContainer>("BottomConsole/HBox/CommandFrame/GridContainer");
		_commandGrid.Columns = 4;

		_inventoryGrid = GetNode<GridContainer>("BottomConsole/HBox/InventoryFrame/InventoryGrid");
		_inventoryGrid.Columns = 2;

		_btnMove = GetNode<Button>("BottomConsole/HBox/CommandFrame/GridContainer/BtnMove");
		_btnStop = GetNode<Button>("BottomConsole/HBox/CommandFrame/GridContainer/BtnStop");
		_btnHold = GetNode<Button>("BottomConsole/HBox/CommandFrame/GridContainer/BtnHold");
		_btnBuild = GetNode<Button>("BottomConsole/HBox/CommandFrame/GridContainer/BtnBuild");
	}

	private void SetupWarningLabel()
	{
		_connectionWarningLabel = new Label();
		_connectionWarningLabel.Name = "ConnectionWarningLabel";
		_connectionWarningLabel.Text = TranslationServer.Translate("Connection to host lost ... Reconnecting");
		_connectionWarningLabel.HorizontalAlignment = HorizontalAlignment.Center;
		_connectionWarningLabel.VerticalAlignment = VerticalAlignment.Center;
		_connectionWarningLabel.AddThemeColorOverride("font_color", new Color(0.95f, 0.2f, 0.2f));
		_connectionWarningLabel.AddThemeFontSizeOverride("font_size", 22);

		var warningStyle = new StyleBoxFlat();
		warningStyle.BgColor = new Color(0.12f, 0.05f, 0.05f, 0.7f);
		warningStyle.BorderColor = new Color(0.8f, 0.2f, 0.2f, 0.5f);
		warningStyle.SetBorderWidthAll(2);
		warningStyle.ContentMarginLeft = 20;
		warningStyle.ContentMarginRight = 20;
		warningStyle.ContentMarginTop = 8;
		warningStyle.ContentMarginBottom = 8;
		_connectionWarningLabel.AddThemeStyleboxOverride("normal", warningStyle);

		AddChild(_connectionWarningLabel);
		_connectionWarningLabel.SetAnchorsPreset(LayoutPreset.CenterTop);
		_connectionWarningLabel.GrowHorizontal = GrowDirection.Both;
		_connectionWarningLabel.OffsetTop = 20;
		_connectionWarningLabel.OffsetLeft = -250;
		_connectionWarningLabel.OffsetRight = 250;
		_connectionWarningLabel.Visible = false;
	}

	private void SetupMinimapControls()
	{
		_minimapControls = new VBoxContainer();
		_minimapControls.Name = "MinimapControls_VBoxContainer";
		_minimapControls.Alignment = BoxContainer.AlignmentMode.Center;
		_minimapControls.AddThemeConstantOverride("separation", 3);
		_minimapControls.AnchorLeft = 0.1773f;
		_minimapControls.AnchorRight = 0.2660f;
		_minimapControls.AnchorTop = 0.18f;
		_minimapControls.AnchorBottom = 0.94f;
		_minimapControls.OffsetLeft = 0;
		_minimapControls.OffsetRight = 0;
		_minimapControls.OffsetTop = 0;
		_minimapControls.OffsetBottom = 0;
		_minimapControls.GrowHorizontal = GrowDirection.Both;
		_minimapControls.GrowVertical = GrowDirection.Both;
		var slotsContainer = GetNode<Control>("BottomConsole/HBox");
		slotsContainer.AddChild(_minimapControls);

		_btnZoom = new Button();
		_btnZoom.Name = "BtnZoom_Button";
		SetupMinimapButton(_btnZoom, UnicodeIcons.ZOOM_IN, "Toggle Camera Zoom (Near / Mid / Far) [Z]", () => GameContext.Host?.CycleCameraZoom());
		_minimapControls.AddChild(_btnZoom);

		_btnToggleTerrain = new Button();
		_btnToggleTerrain.Name = "BtnToggleTerrain_Button";
		SetupMinimapButton(_btnToggleTerrain, UnicodeIcons.MAP_IMAGE, "Toggle Terrain Overlay (Radar Mode) [F4]", () => ToggleMinimapTerrain());
		_minimapControls.AddChild(_btnToggleTerrain);

		_btnToggleColorScheme = new Button();
		_btnToggleColorScheme.Name = "BtnToggleColorScheme_Button";
		SetupMinimapButton(_btnToggleColorScheme, UnicodeIcons.PALETTE, "Toggle Minimap Color Scheme (Team / Player Colors)", () => ToggleMinimapColorScheme());
		_minimapControls.AddChild(_btnToggleColorScheme);

		_btnPing = new Button();
		_btnPing.Name = "BtnPing_Button";
		SetupMinimapButton(_btnPing, UnicodeIcons.CROSSHAIRS_TARGET, "Send Alert Ping on Map [Alt+G]", () =>
		{
			if (GameContext.Host != null)
			{
				GameContext.Host.ActivePingMode = true;
				ShowFeedbackText("Ping Mode: Click Minimap or Ground to ping", new Color(1.0f, 0.1f, 0.2f));
			}
		});
		_minimapControls.AddChild(_btnPing);

		_btnCenter = new Button();
		_btnCenter.Name = "BtnCenter_Button";
		SetupMinimapButton(_btnCenter, UnicodeIcons.CASTLE, "Center Camera on Base [Space]", () => GameContext.Host?.CenterCameraOnPrimaryBuilding());
		_minimapControls.AddChild(_btnCenter);

		_btnSelectIdle = new Button();
		_btnSelectIdle.Name = "BtnSelectIdle_Button";
		SetupMinimapButton(_btnSelectIdle, UnicodeIcons.USER_CLOCK, "Select All Idle Units [F1]", () => GameContext.Host?.SelectAllIdleUnits());
		_minimapControls.AddChild(_btnSelectIdle);

		_btnSelectArmy = new Button();
		_btnSelectArmy.Name = "BtnSelectArmy_Button";
		SetupMinimapButton(_btnSelectArmy, UnicodeIcons.SHIELD, "Select All Army Units [F2]", () => GameContext.Host?.SelectAllMilitaryUnits());
		_minimapControls.AddChild(_btnSelectArmy);

		var btnHotkeys = new Button();
		btnHotkeys.Name = "BtnHotkeys_Button";
		SetupMinimapButton(btnHotkeys, UnicodeIcons.KEYBOARD, "Hotkey Reference", () => ToggleHotkeyPanel());
		_minimapControls.AddChild(btnHotkeys);
	}

	private string GetCurrentWeather()
	{
		if (GameContext.Host == null) return "clear";

		string mapDir = !string.IsNullOrEmpty(GameContext.MapStorageService.CurrentMapDirectory)
			? GameContext.MapStorageService.CurrentMapDirectory
			: MapWorkspaceService.GetActiveWorkspacePath();
		string metaPath = Path.Combine(mapDir, "metadata.json").Replace("\\", "/");
		GameContext.Host.LoadMapProperties(metaPath);
		GameHost tempQualifier = GameContext.Host;
		return (tempQualifier != null ? GameContext.EnvironmentService : null)?.GetCurrentWeather() ?? "clear";
	}

	private void SetupChatPanel()
	{
		_chatPanel = GetNode<PanelContainer>("ChatPanel");
		_chatInput = GetNode<LineEdit>("ChatPanel/ChatContainer/ChatInput");
		_chatLog = GetNode<RichTextLabel>("ChatPanel/ChatContainer/ChatLog");

		var chatPanelStyle = new StyleBoxFlat();
		chatPanelStyle.BgColor = new Color(0.15f, 0.15f, 0.15f, 0.9f);
		chatPanelStyle.SetBorderWidthAll(1);
		chatPanelStyle.BorderColor = new Color(0.25f, 0.25f, 0.25f, 0.9f);
		chatPanelStyle.CornerRadiusTopLeft = 4;
		chatPanelStyle.CornerRadiusTopRight = 4;
		chatPanelStyle.CornerRadiusBottomLeft = 4;
		chatPanelStyle.CornerRadiusBottomRight = 4;
		chatPanelStyle.ContentMarginLeft = 8;
		chatPanelStyle.ContentMarginRight = 8;
		chatPanelStyle.ContentMarginTop = 8;
		chatPanelStyle.ContentMarginBottom = 8;
		_chatPanel.AddThemeStyleboxOverride("panel", chatPanelStyle);
		_chatInput.AddThemeStyleboxOverride("normal", UIStyle.CreateTextInput(false));
		_chatInput.AddThemeStyleboxOverride("focus", UIStyle.CreateTextInput(true));
		_chatInput.AddThemeColorOverride("font_color", new Color(0.9f, 0.85f, 0.7f));
		_chatLog.Text = $"[color=#ffd700]{TranslationServer.Translate("Chat log initialized. Press Enter to type a message.")}[/color]\n";

		if (!string.IsNullOrEmpty(GameContext.Lobby?.LastLobbyWarning))
		{
			_chatLog.AppendText($"[color=#ffb347]{GameContext.Lobby.LastLobbyWarning}[/color]\n");
		}

		if (GameContext.Lobby != null)
		{
			GameContext.Lobby.ChatReceived += OnLobbyChatReceived;
		}
	}

	private void SetupCustomUIPanel()
	{
		_controlGroupsContainer = new HBoxContainer();
		_controlGroupsContainer.Name = "ControlGroupsContainer";
		_controlGroupsContainer.SetAnchorsAndOffsetsPreset(LayoutPreset.TopLeft);
		_controlGroupsContainer.OffsetLeft = 20;
		_controlGroupsContainer.OffsetTop = 80;
		_controlGroupsContainer.AddThemeConstantOverride("separation", 8);
		AddChild(_controlGroupsContainer);

		_customUIPanel = new VBoxContainer();
		_customUIPanel.Name = "CustomUIPanel";
		_customUIPanel.SetAnchorsAndOffsetsPreset(LayoutPreset.TopRight);
		_customUIPanel.GrowHorizontal = GrowDirection.Begin;
		_customUIPanel.OffsetLeft = -440;
		_customUIPanel.OffsetRight = -20;
		_customUIPanel.OffsetTop = 20;
		_customUIPanel.CustomMinimumSize = new Vector2(420, 0);
		_customUIPanel.AddThemeConstantOverride("separation", 10);
		AddChild(_customUIPanel);

		SetupCountdownPanel();
		SetupLeaderboardPanel();
		SetupSummaryPanel();
	}

	private void SetupCountdownPanel()
	{
		_countdownPanel = new PanelContainer();
		_countdownPanel.Name = "CountdownPanel";
		_countdownPanel.Visible = false;
		_countdownPanel.SizeFlagsHorizontal = SizeFlags.ShrinkEnd;
		var countdownStyle = new StyleBoxFlat();
		countdownStyle.BgColor = new Color(0.12f, 0.12f, 0.12f, 0.85f);
		countdownStyle.SetBorderWidthAll(1);
		countdownStyle.BorderColor = UIStyle.ColorBronze;
		countdownStyle.CornerRadiusTopLeft = 4;
		countdownStyle.CornerRadiusTopRight = 4;
		countdownStyle.CornerRadiusBottomLeft = 4;
		countdownStyle.CornerRadiusBottomRight = 4;
		countdownStyle.ContentMarginLeft = 8;
		countdownStyle.ContentMarginRight = 8;
		countdownStyle.ContentMarginTop = 6;
		countdownStyle.ContentMarginBottom = 6;
		_countdownPanel.AddThemeStyleboxOverride("panel", countdownStyle);

		var countdownRow = new HBoxContainer();
		countdownRow.Name = "CountdownRow";
		countdownRow.AddThemeConstantOverride("separation", 8);

		_countdownLabel = new Label();
		_countdownLabel.Name = "CountdownLabel";
		_countdownLabel.Text = "";
		_countdownLabel.HorizontalAlignment = HorizontalAlignment.Center;
		_countdownLabel.VerticalAlignment = VerticalAlignment.Center;
		_countdownLabel.AddThemeFontSizeOverride("font_size", 14);
		_countdownLabel.AddThemeColorOverride("font_color", UIStyle.ColorGold);
		countdownRow.AddChild(_countdownLabel);

		_countdownCancelButton = new Button();
		_countdownCancelButton.Name = "CountdownCancelButton";
		_countdownCancelButton.CustomMinimumSize = new Vector2(0, 24);
		_countdownCancelButton.AddThemeConstantOverride("icon_max_width", 0);
		_countdownCancelButton.FocusMode = FocusModeEnum.None;
		_countdownCancelButton.AddThemeStyleboxOverride("normal", UIStyle.CreateButtonNormal());
		_countdownCancelButton.AddThemeStyleboxOverride("hover", UIStyle.CreateButtonHover());
		_countdownCancelButton.AddThemeStyleboxOverride("pressed", UIStyle.CreateButtonPressed());
		_countdownCancelButton.AddThemeStyleboxOverride("focus", new StyleBoxEmpty());
		UIStyle.ApplyButtonText(_countdownCancelButton, TranslationServer.Translate("CANCEL"), 12);
		_countdownCancelButton.Pressed += OnCountdownCancelPressed;
		countdownRow.AddChild(_countdownCancelButton);

		_countdownPanel.AddChild(countdownRow);
		_customUIPanel.AddChild(_countdownPanel);
	}

	private void SetupLeaderboardPanel()
	{
		_leaderboardPanel = new PanelContainer();
		_leaderboardPanel.Name = "LeaderboardPanel";
		_leaderboardPanel.Visible = false;
		_leaderboardPanel.SizeFlagsHorizontal = SizeFlags.ShrinkEnd;
		var leaderboardStyle = new StyleBoxFlat();
		leaderboardStyle.BgColor = new Color(0.12f, 0.12f, 0.12f, 0.85f);
		leaderboardStyle.SetBorderWidthAll(1);
		leaderboardStyle.BorderColor = UIStyle.ColorBronze;
		leaderboardStyle.CornerRadiusTopLeft = 4;
		leaderboardStyle.CornerRadiusTopRight = 4;
		leaderboardStyle.CornerRadiusBottomLeft = 4;
		leaderboardStyle.CornerRadiusBottomRight = 4;
		leaderboardStyle.ContentMarginLeft = 10;
		leaderboardStyle.ContentMarginRight = 10;
		leaderboardStyle.ContentMarginTop = 8;
		leaderboardStyle.ContentMarginBottom = 8;
		_leaderboardPanel.AddThemeStyleboxOverride("panel", leaderboardStyle);

		var lbVBox = new VBoxContainer();
		lbVBox.Name = "LbVBox_VBoxContainer";
		_leaderboardPanel.AddChild(lbVBox);

		_leaderboardTitleLabel = new Label();
		_leaderboardTitleLabel.Name = "LeaderboardTitle";
		_leaderboardTitleLabel.Text = TranslationServer.Translate("LEADERBOARD");
		UIStyle.ApplyTitle(_leaderboardTitleLabel, TranslationServer.Translate("LEADERBOARD"), 12);
		_leaderboardTitleLabel.HorizontalAlignment = HorizontalAlignment.Center;
		lbVBox.AddChild(_leaderboardTitleLabel);

		_leaderboardSelectors = new VBoxContainer();
		_leaderboardSelectors.Name = "LeaderboardSelectors";
		_leaderboardSelectors.AddThemeConstantOverride("separation", 4);
		lbVBox.AddChild(_leaderboardSelectors);

		_leaderboardContent = new VBoxContainer();
		_leaderboardContent.Name = "LeaderboardContent";
		lbVBox.AddChild(_leaderboardContent);
		_customUIPanel.AddChild(_leaderboardPanel);
	}

	private void SetupSummaryPanel()
	{
		_summaryPanel = new PanelContainer();
		_summaryPanel.Name = "SummaryTablePanel";
		_summaryPanel.Visible = false;
		_summaryPanel.SizeFlagsHorizontal = SizeFlags.ShrinkEnd;
		var summaryStyle = new StyleBoxFlat();
		summaryStyle.BgColor = new Color(0.1f, 0.1f, 0.14f, 0.92f);
		summaryStyle.SetBorderWidthAll(2);
		summaryStyle.BorderColor = UIStyle.ColorGold;
		summaryStyle.CornerRadiusTopLeft = 4;
		summaryStyle.CornerRadiusTopRight = 4;
		summaryStyle.CornerRadiusBottomLeft = 4;
		summaryStyle.CornerRadiusBottomRight = 4;
		summaryStyle.ContentMarginLeft = 14;
		summaryStyle.ContentMarginRight = 14;
		summaryStyle.ContentMarginTop = 10;
		summaryStyle.ContentMarginBottom = 10;
		_summaryPanel.AddThemeStyleboxOverride("panel", summaryStyle);

		var summaryVBox = new VBoxContainer();
		summaryVBox.Name = "SummaryVBox_VBoxContainer";
		_summaryPanel.AddChild(summaryVBox);

		_summaryTitleLabel = new Label();
		_summaryTitleLabel.Name = "SummaryTitle";
		_summaryTitleLabel.Text = TranslationServer.Translate("SUMMARY");
		UIStyle.ApplyTitle(_summaryTitleLabel, TranslationServer.Translate("SUMMARY"), 14);
		_summaryTitleLabel.HorizontalAlignment = HorizontalAlignment.Center;
		summaryVBox.AddChild(_summaryTitleLabel);

		_summaryContent = new VBoxContainer();
		_summaryContent.Name = "SummaryContent";
		summaryVBox.AddChild(_summaryContent);
		_customUIPanel.AddChild(_summaryPanel);
	}

	private void ApplyModeSpecificUI()
	{
		if (GameContext.SessionState.IsTestMode)
		{
			var btnBackToEditor = new Button();
			btnBackToEditor.Name = "BtnBackToEditor";
			btnBackToEditor.Text = TranslationServer.Translate("Back to Editor");
			btnBackToEditor.Set("icon_max_width", 0);
			btnBackToEditor.AddThemeStyleboxOverride("normal", UIStyle.CreateButtonNormal());
			btnBackToEditor.AddThemeStyleboxOverride("hover", UIStyle.CreateButtonHover());
			btnBackToEditor.AddThemeStyleboxOverride("pressed", UIStyle.CreateButtonPressed());
			btnBackToEditor.AddThemeColorOverride("font_color", UIStyle.ColorCyanGlow);
			btnBackToEditor.AddThemeFontSizeOverride("font_size", 13);
			btnBackToEditor.CustomMinimumSize = new Vector2(160, 40);
			btnBackToEditor.SetAnchorsPreset(LayoutPreset.TopLeft);
			btnBackToEditor.OffsetLeft = 20;
			btnBackToEditor.OffsetTop = 20;
			btnBackToEditor.Pressed += () =>
			{
				GameContext.UserInterface?.PlayClickSound();
				GameContext.SessionState.IsTestMode = false;
				GameContext.SessionState.ReturningFromTest = true;
				if (GameContext.Lobby != null)
				{
					GameContext.Lobby.Disconnect();
				}

				GameContext.UserInterface?.TransitionTo(GameScreen.MapEditorHUD);
			};
			AddChild(btnBackToEditor);
		}
	}

	private void SetupControllers()
	{
		_resourcePanelController = new ResourcePanel(_resourceContainer);
		_resourcePanelController.InitializeSupplyAndClock(_populationLabel, _clockLabel);

		_camera3D = GameContext.Host?.MainCamera;
		_minimapPanelController = new MinimapPanel(_minimapFrame, _minimapArea, _cameraIndicator, _camera3D);
		_chatPanelController = new ChatPanel(_chatPanel, _chatInput, _chatLog);
		_leaderboardPanelController = new LeaderboardPanel(_customUIPanel, _countdownPanel, _countdownLabel, _leaderboardPanel, _leaderboardTitleLabel, _leaderboardSelectors, _leaderboardContent);
		_summaryTablePanelController = new SummaryTablePanel(_summaryPanel, _summaryTitleLabel, _summaryContent);

		_portraitPanelController = new PortraitPanel(
			_portraitFrame, _selectionFrame, _unitsContainer, _unitButtons, _statsContainer,
			_statsLabel, _itemsBox,
			_productionBox, _productionTitle, _productionProgress, _productionQueueLabel, _queueSlotsContainer,
			_armyCompositionLabel, GetNode<Label>("BottomConsole/HBox/PortraitFrame/VBox/UnitName"),
			GetNodeOrNull<TextureRect>("BottomConsole/HBox/PortraitFrame/VBox/PortraitTexture") != null ? GetNode<TextureRect>("BottomConsole/HBox/PortraitFrame/VBox/PortraitTexture") : null
		);
		_portraitPanelController.UnitSelectionButtonClicked += OnUnitSelectionButtonClicked;

		_inventoryPanelController = new InventoryPanel(_inventoryGrid);

		_commandPanelController = new CommandPanel(_commandGrid);

		_controlGroupsUIController = new ControlGroupsUIController(_controlGroupsContainer);
	}

	private void ApplySpectatorAndReplayOverrides()
	{
		if (GameContext.ReplayPlayback.IsPlayingReplay)
		{
			_bottomConsole.Visible = false;
			_resourceContainer.Visible = false;
			if (_chatPanel != null) _chatPanel.Visible = false;
		}

		bool isSpectator = GameContext.PerspectiveFilter.IsSpectating;
		if (isSpectator)
		{
			_resourceContainer.Visible = false;
			CreateSpectatorPerspectiveUI();
		}
	}

	private void OnHUDResized()
	{
		UpdateBottomConsoleHeight();
		ApplyHUDScale();

		if (_resourceBackground != null && _resourceBackground.Material is ShaderMaterial topMat && _resourceContainer != null)
		{
			float resW = _resourceContainer.Size.X > 0 ? _resourceContainer.Size.X : 840f;
			float resH = _resourceContainer.Size.Y > 0 ? _resourceContainer.Size.Y : 76f;
			topMat.SetShaderParameter("u_panel_size", new Vector2(resW, resH));
		}

		var specPanel = GetNodeOrNull<PanelContainer>("SpectatorPanel");
		if (specPanel != null)
		{
			specPanel.Position = new Vector2((GetViewportRect().Size.X - 300) / 2.0f, 10);
		}

		if (_hotkeyPanel != null)
		{
			float vpW = GetViewportRect().Size.X;
			float vpH = GetViewportRect().Size.Y;
			if (vpW <= 0f) vpW = 1920f;
			if (vpH <= 0f) vpH = 1080f;
			float consoleH = vpW * (BaseConsoleHeight / 1920f);
			float playfieldCenterY = (80f + (vpH - consoleH)) / 2f;
			_hotkeyPanel.OffsetLeft = -240f;
			_hotkeyPanel.OffsetRight = 240f;
			_hotkeyPanel.OffsetTop = playfieldCenterY - (vpH / 2f) - 336f;
			_hotkeyPanel.OffsetBottom = _hotkeyPanel.OffsetTop + 672f;
		}
	}

	private const float BaseConsoleHeight = 240f;

	private void UpdateBottomConsoleHeight()
	{
		if (_bottomConsole != null)
		{
			float viewportWidth = GetViewportRect().Size.X;
			if (viewportWidth <= 0f) viewportWidth = 1920f;
			float proportionalHeight = viewportWidth * (BaseConsoleHeight / 1920f);

			_bottomConsole.OffsetBottom = 0;
			_bottomConsole.OffsetTop = -Mathf.Round(proportionalHeight);

			if (_consoleBackground != null && _consoleBackground.Material is ShaderMaterial mat)
			{
				mat.SetShaderParameter("u_console_size", new Vector2(viewportWidth, proportionalHeight));
			}

			float s = proportionalHeight / 433f;
			float wLeft = 935f * s;
			float wRight = 912f * s;

			if (_minimapFrame != null)
			{
				_minimapFrame.AnchorLeft = 0;
				_minimapFrame.AnchorRight = 0;
				_minimapFrame.AnchorTop = 0;
				_minimapFrame.AnchorBottom = 0;
				_minimapFrame.OffsetLeft = Mathf.Round(38f * s);
				_minimapFrame.OffsetRight = Mathf.Round(426f * s);
				_minimapFrame.OffsetTop = Mathf.Round(61f * s);
				_minimapFrame.OffsetBottom = Mathf.Round(426f * s);
			}

			if (_minimapControls != null)
			{
				_minimapControls.AnchorLeft = 0;
				_minimapControls.AnchorRight = 0;
				_minimapControls.AnchorTop = 0;
				_minimapControls.AnchorBottom = 0;
				_minimapControls.OffsetLeft = Mathf.Round(438f * s);
				_minimapControls.OffsetRight = Mathf.Round(534f * s);
				_minimapControls.OffsetTop = Mathf.Round(64f * s);
				_minimapControls.OffsetBottom = Mathf.Round(424f * s);
			}

			if (_portraitFrame != null)
			{
				_portraitFrame.AnchorLeft = 0;
				_portraitFrame.AnchorRight = 0;
				_portraitFrame.AnchorTop = 0;
				_portraitFrame.AnchorBottom = 0;
				_portraitFrame.OffsetLeft = Mathf.Round(660f * s);
				_portraitFrame.OffsetRight = Mathf.Round(935f * s);
				_portraitFrame.OffsetTop = Mathf.Round(110f * s);
				_portraitFrame.OffsetBottom = Mathf.Round(428f * s);

				var pTexture = _portraitFrame.GetNodeOrNull<TextureRect>("VBox/PortraitTexture");
				if (pTexture != null)
				{
					float portraitSize = Mathf.Round(180f * s);
					pTexture.CustomMinimumSize = new Vector2(portraitSize, portraitSize);
				}
			}

			if (_selectionFrame != null)
			{
				_selectionFrame.AnchorLeft = 0;
				_selectionFrame.AnchorRight = 0;
				_selectionFrame.AnchorTop = 0;
				_selectionFrame.AnchorBottom = 0;
				_selectionFrame.OffsetLeft = Mathf.Round(wLeft + 140f * s);
				_selectionFrame.OffsetRight = Mathf.Round(viewportWidth - wRight - 40f * s);
				_selectionFrame.OffsetTop = Mathf.Round(165f * s);
				_selectionFrame.OffsetBottom = Mathf.Round(415f * s);

				if (_statsLabel != null)
				{
					float labelW = Mathf.Max(200f, _selectionFrame.OffsetRight - _selectionFrame.OffsetLeft - 20f);
					_statsLabel.CustomMinimumSize = new Vector2(labelW, 0);
				}
			}

			var inventoryFrame = GetNodeOrNull<PanelContainer>("BottomConsole/HBox/InventoryFrame");
			if (inventoryFrame != null)
			{
				inventoryFrame.AnchorLeft = 0;
				inventoryFrame.AnchorRight = 0;
				inventoryFrame.AnchorTop = 0;
				inventoryFrame.AnchorBottom = 0;
				inventoryFrame.OffsetLeft = Mathf.Round(viewportWidth - (2752f - 1850f) * s);
				inventoryFrame.OffsetRight = Mathf.Round(viewportWidth - (2752f - 2150f) * s);
				inventoryFrame.OffsetTop = Mathf.Round(170f * s);
				inventoryFrame.OffsetBottom = Mathf.Round(425f * s);
			}

			if (_commandFrame != null)
			{
				_commandFrame.AnchorLeft = 0;
				_commandFrame.AnchorRight = 0;
				_commandFrame.AnchorTop = 0;
				_commandFrame.AnchorBottom = 0;
				_commandFrame.OffsetLeft = Mathf.Round(viewportWidth - (2752f - 2250f) * s);
				_commandFrame.OffsetRight = Mathf.Round(viewportWidth - (2752f - 2725f) * s);
				_commandFrame.OffsetTop = Mathf.Round(60f * s);
				_commandFrame.OffsetBottom = Mathf.Round(415f * s);
			}

			if (_chatPanel != null)
			{
				float chatHeight = 240f;
				_chatPanel.OffsetBottom = _bottomConsole.OffsetTop - 15f;
				_chatPanel.OffsetTop = _chatPanel.OffsetBottom - chatHeight;
			}
		}
	}

	public void ApplyHUDScale()
	{
		float s = GameSettings.HudScale / 100f;

		if (_resourceContainer != null)
		{
			_resourceContainer.PivotOffset = new Vector2(_resourceContainer.Size.X / 2f, 0f);
			_resourceContainer.Scale = new Vector2(s, s);
		}

		if (_bottomConsole != null)
		{
			float width = _bottomConsole.Size.X > 0 ? _bottomConsole.Size.X : GetViewportRect().Size.X;
			float height = _bottomConsole.Size.Y > 0 ? _bottomConsole.Size.Y : BaseConsoleHeight;
			_bottomConsole.PivotOffset = new Vector2(width / 2f, height);
			_bottomConsole.Scale = new Vector2(s, s);
		}

		if (_chatPanel != null)
		{
			_chatPanel.PivotOffset = new Vector2(0f, _chatPanel.Size.Y);
			_chatPanel.Scale = new Vector2(s, s);
		}

		if (_controlGroupsContainer != null)
		{
			_controlGroupsContainer.PivotOffset = Vector2.Zero;
			_controlGroupsContainer.Scale = new Vector2(s, s);
		}

		if (_customUIPanel != null)
		{
			_customUIPanel.PivotOffset = new Vector2(_customUIPanel.Size.X, 0f);
			_customUIPanel.Scale = new Vector2(s, s);
		}

		if (_hotkeyPanel != null)
		{
			_hotkeyPanel.PivotOffset = new Vector2(240f, 336f);
			_hotkeyPanel.Scale = new Vector2(s, s);
		}
	}

	public void UpdateFPSVisibility()
	{
		var fpsLabel = (GameContext.Host != null ? GameContext.Host.MainNode?.GetNodeOrNull<Label>("CanvasLayer/FPS") : null);
		if (fpsLabel != null)
		{
			fpsLabel.Visible = GameSettings.DisplayFps;
			fpsLabel.ZIndex = 100;
		}
	}

	public override void _ExitTree()
	{
		UnsubscribeFromLobbyCountdown();
		GameContext.UnregisterInGameHud(this);
		if (GameContext.Lobby != null)
		{
			GameContext.Lobby.ChatReceived -= OnLobbyChatReceived;
		}
	}

	private bool _isGeneratingMinimap = false;
	private bool _minimapNeedsRegen = false;

	public async Task RegenerateMinimapBackground()
	{
		if (_isGeneratingMinimap)
		{
			_minimapNeedsRegen = true;
			return;
		}

		_isGeneratingMinimap = true;

		try
		{
			do
			{
				_minimapNeedsRegen = false;
				await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
				await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);

				var minimapBg = _minimapArea.GetChildCount() > 0 ? _minimapArea.GetChild<TextureRect>(0) : null;
				if (minimapBg == null) return;

				var imgTexture = await MinimapHelper.CaptureTerrainMinimapTextureAsync(this, 256);
				if (imgTexture != null)
				{
					minimapBg.Texture = imgTexture;
				}
			} while (_minimapNeedsRegen);
		}
		catch (Exception ex)
		{
			GD.PrintErr($"Failed to dynamically capture terrain minimap: {ex.Message}");
		}
		finally
		{
			_isGeneratingMinimap = false;
		}
	}

	private void GenerateDynamicMinimap()
	{
		_ = RegenerateMinimapBackground();
	}

	private void CreateStatsContainer()
	{
		_statsContainer = new HBoxContainer();
		_statsContainer.Name = "StatsContainer_HBoxContainer";
		_statsContainer.LayoutMode = 2;
		_statsContainer.SizeFlagsHorizontal = SizeFlags.ExpandFill;
		_statsContainer.SizeFlagsVertical = SizeFlags.ExpandFill;
		_statsContainer.CustomMinimumSize = new Vector2(0, 0);
		_statsContainer.ClipContents = true;
		_statsContainer.AddThemeConstantOverride("separation", 20);
		_selectionFrame.AddChild(_statsContainer);
		_statsContainer.Visible = false;

		var statsScroll = new ScrollContainer();
		statsScroll.Name = "StatsScroll";
		statsScroll.SizeFlagsHorizontal = SizeFlags.ExpandFill;
		statsScroll.SizeFlagsVertical = SizeFlags.ExpandFill;
		statsScroll.HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled;
		statsScroll.VerticalScrollMode = ScrollContainer.ScrollMode.Auto;
		_statsContainer.AddChild(statsScroll);

		var statsVBox = new VBoxContainer();
		statsVBox.Name = "StatsVBox_VBoxContainer";
		statsVBox.SizeFlagsHorizontal = SizeFlags.ExpandFill;
		statsVBox.SizeFlagsVertical = SizeFlags.ShrinkBegin;
		statsScroll.AddChild(statsVBox);

		_statsLabel = new Label();
		_statsLabel.Name = "StatsLabel_Label";
		_statsLabel.AutowrapMode = TextServer.AutowrapMode.WordSmart;
		_statsLabel.SizeFlagsHorizontal = SizeFlags.ExpandFill;
		_statsLabel.CustomMinimumSize = new Vector2(300, 0);
		_statsLabel.Text = TranslationServer.Translate("HP: 100/100\nDamage: 10\nArmor: 2\nSpeed: 5");
		_statsLabel.AddThemeFontSizeOverride("font_size", 13);
		_statsLabel.AddThemeColorOverride("font_color", UIStyle.ColorGoldDull);
		_statsLabel.AddThemeConstantOverride("line_spacing", 2);
		_statsLabel.AddThemeConstantOverride("outline_size", 2);
		_statsLabel.AddThemeColorOverride("font_outline_color", new Color(0f, 0f, 0f, 0.9f));
		statsVBox.AddChild(_statsLabel);

		var vScroll = statsScroll.GetVScrollBar();
		if (vScroll != null)
		{
			vScroll.CustomMinimumSize = new Vector2(6, 0);
			var grabberNormal = new StyleBoxFlat();
			grabberNormal.BgColor = UIStyle.ColorBronze;
			grabberNormal.CornerRadiusTopLeft = 3;
			grabberNormal.CornerRadiusTopRight = 3;
			grabberNormal.CornerRadiusBottomLeft = 3;
			grabberNormal.CornerRadiusBottomRight = 3;
			vScroll.AddThemeStyleboxOverride("grabber", grabberNormal);

			var grabberHighlight = new StyleBoxFlat();
			grabberHighlight.BgColor = UIStyle.ColorGold;
			grabberHighlight.CornerRadiusTopLeft = 3;
			grabberHighlight.CornerRadiusTopRight = 3;
			grabberHighlight.CornerRadiusBottomLeft = 3;
			grabberHighlight.CornerRadiusBottomRight = 3;
			vScroll.AddThemeStyleboxOverride("grabber_highlight", grabberHighlight);

			var grabberPressed = new StyleBoxFlat();
			grabberPressed.BgColor = UIStyle.ColorCyanGlow;
			grabberPressed.CornerRadiusTopLeft = 3;
			grabberPressed.CornerRadiusTopRight = 3;
			grabberPressed.CornerRadiusBottomLeft = 3;
			grabberPressed.CornerRadiusBottomRight = 3;
			vScroll.AddThemeStyleboxOverride("grabber_pressed", grabberPressed);

			var scrollTrack = new StyleBoxFlat();
			scrollTrack.BgColor = new Color(0.04f, 0.04f, 0.06f, 0.5f);
			scrollTrack.CornerRadiusTopLeft = 3;
			scrollTrack.CornerRadiusTopRight = 3;
			scrollTrack.CornerRadiusBottomLeft = 3;
			scrollTrack.CornerRadiusBottomRight = 3;
			vScroll.AddThemeStyleboxOverride("scroll", scrollTrack);
		}

		_itemsBox = new VBoxContainer();
		_itemsBox.Name = "ItemsBox_VBoxContainer";
		_itemsBox.SizeFlagsHorizontal = SizeFlags.ExpandFill;
		_itemsBox.Alignment = BoxContainer.AlignmentMode.Center;
		_itemsBox.Visible = false;
		_statsContainer.AddChild(_itemsBox);

		var itemsTitle = new Label();
		itemsTitle.Name = "ItemsTitle_Label";
		itemsTitle.Text = TranslationServer.Translate("ITEMS");
		UIStyle.ApplyTitle(itemsTitle, TranslationServer.Translate("ITEMS"), 12);
		itemsTitle.AddThemeColorOverride("font_color", UIStyle.ColorBronze);
		_itemsBox.AddChild(itemsTitle);

		var itemsHBox = new HBoxContainer();
		itemsHBox.Name = "ItemsHBox_HBoxContainer";
		itemsHBox.AddThemeConstantOverride("separation", 8);
		_itemsBox.AddChild(itemsHBox);

		var itemAxe = new TextureRect();
		itemAxe.Name = "ItemAxe_TextureRect";
		SetupItemIcon(itemAxe, "res://Assets/UI/battle_axe.webp", "Battle Axe\n+5 Attack Damage (Equipped)");
		itemsHBox.AddChild(itemAxe);

		var itemShield = new TextureRect();
		itemShield.Name = "ItemShield_TextureRect";
		SetupItemIcon(itemShield, "res://Assets/UI/battle_shield.webp", "Battle Shield\n+3 Armor Block (Equipped)");
		itemsHBox.AddChild(itemShield);

		_productionBox = new VBoxContainer();
		_productionBox.Name = "ProductionBox_VBoxContainer";
		_productionBox.SizeFlagsHorizontal = SizeFlags.ExpandFill;
		_productionBox.Alignment = BoxContainer.AlignmentMode.Center;
		_statsContainer.AddChild(_productionBox);
		_productionBox.Visible = false;

		_productionTitle = new Label();
		_productionTitle.Name = "ProductionTitle_Label";
		_productionTitle.Text = TranslationServer.Translate("PRODUCTION");
		UIStyle.ApplyTitle(_productionTitle, TranslationServer.Translate("PRODUCTION"), 12);
		_productionTitle.AddThemeColorOverride("font_color", UIStyle.ColorBronze);
		_productionBox.AddChild(_productionTitle);

		_productionProgress = new ProgressBar();
		_productionProgress.Name = "ProductionProgress_ProgressBar";
		_productionProgress.CustomMinimumSize = new Vector2(120, 16);
		_productionProgress.ShowPercentage = true;
		_productionProgress.AddThemeStyleboxOverride("background", UIStyle.CreateSliderTrack());
		_productionProgress.AddThemeStyleboxOverride("fill", UIStyle.CreateSliderFill());
		_productionBox.AddChild(_productionProgress);

		_productionQueueLabel = new Label();
		_productionQueueLabel.Name = "ProductionQueueLabel_Label";
		_productionQueueLabel.Text = string.Format(TranslationServer.Translate("Queue: {0}"), 0);
		_productionQueueLabel.HorizontalAlignment = HorizontalAlignment.Center;
		_productionQueueLabel.AddThemeFontSizeOverride("font_size", 11);
		_productionQueueLabel.AddThemeColorOverride("font_color", UIStyle.ColorGoldDull);
		_productionBox.AddChild(_productionQueueLabel);

		_queueSlotsContainer = new HBoxContainer();
		_queueSlotsContainer.Name = "QueueSlotsContainer_HBoxContainer";
		_queueSlotsContainer.Alignment = BoxContainer.AlignmentMode.Center;
		_queueSlotsContainer.AddThemeConstantOverride("separation", 6);
		_productionBox.AddChild(_queueSlotsContainer);
	}

	private void SetupSpellButton(Button btn, string iconPath, string spellId, string tooltip)
	{
		btn.Flat = false;
		btn.Text = "";
		btn.ExpandIcon = true;
		btn.Icon = TrimmedTextureLoader.LoadTrimmedTexture(iconPath);
		btn.TooltipText = tooltip;
		UIStyle.SetupHUDButton(btn, tooltip);

		btn.AddThemeStyleboxOverride("normal", UIStyle.CreateButtonNormal());
		btn.AddThemeStyleboxOverride("hover", UIStyle.CreateButtonHover());
		btn.AddThemeStyleboxOverride("pressed", UIStyle.CreateButtonPressed());
		btn.AddThemeStyleboxOverride("focus", new StyleBoxEmpty());

		btn.Pressed += () =>
		{
			if (GameContext.Host != null)
			{
				GameContext.Host.EnterSpellTargeting(spellId);
			}
		};
	}

	private void SetupItemIcon(TextureRect rect, string iconPath, string tooltip)
	{
		rect.Texture = TrimmedTextureLoader.LoadTrimmedTexture(iconPath);
		rect.ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize;
		rect.StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered;
		rect.CustomMinimumSize = new Vector2(60, 60);
		rect.TooltipText = tooltip;
	}

	public void RefreshUI(List<Unit3D> selectedUnits)
	{
		InvalidateCadence(tactical: true, macro: false);

		if (selectedUnits != null)
		{
			bool hasPlayerSelection = Enumerable.Any(selectedUnits, u => GameContext.PerspectiveFilter.CanControl(u.Player));
			bool isSpectator = GameContext.PerspectiveFilter.IsSpectating;
			bool commandCardAllowed = IsHudElementVisible(HudElement.CommandCard, true);
			_commandFrame.Visible = !isSpectator && hasPlayerSelection && commandCardAllowed;
		}

		_viewModel.SelectedProp = GameContext.Host?.SelectedProp;
		_viewModel.UpdateSelectedUnits(selectedUnits);
		_portraitPanelController?.Update(_viewModel);
		_commandPanelController?.Update(_viewModel);
		_inventoryPanelController?.Update(_viewModel);

		if (!IsHudElementVisible(HudElement.UnitPortrait, true) && _portraitFrame != null)
		{
			_portraitFrame.Visible = false;
		}
		if (!IsHudElementVisible(HudElement.CommandCard, true) && _commandFrame != null)
		{
			_commandFrame.Visible = false;
		}
		if (!IsHudElementVisible(HudElement.Minimap, true) && _minimapFrame != null)
		{
			_minimapFrame.Visible = false;
		}
		if (!IsHudElementVisible(HudElement.ResourceBar, true) && _resourceContainer != null)
		{
			_resourceContainer.Visible = false;
		}
		if (!IsHudElementVisible(HudElement.Inventory, true))
		{
			if (_inventoryFrame != null) _inventoryFrame.Visible = false;
			if (_inventoryGrid != null) _inventoryGrid.Visible = false;
		}
	}

	private void ApplyThemeStyles()
	{
		_resourceContainer.AddThemeStyleboxOverride("panel", new StyleBoxEmpty());

		if (_resourceBackground != null)
		{
			var topTexture = UIStyle.LoadTextureWithFallback("res://Assets/UI/In-Game_MatchView_PanelTop.webp");
			_resourceBackground.Texture = topTexture;

			_hudPanelTopShader ??= GD.Load<Shader>("res://Assets/shaders/hud_panel_top.gdshader");
			var topMaterial = new ShaderMaterial();
			topMaterial.Shader = _hudPanelTopShader;
			topMaterial.SetShaderParameter("u_panel_size", new Vector2(840.0f, 76.0f));
			_resourceBackground.Material = topMaterial;
		}

		if (_consoleBackground != null)
		{
			var texture = UIStyle.LoadTextureWithFallback("res://Assets/UI/In-Game_MatchView_PanelBottom.webp");
			if (texture == null)
			{
				texture = UIStyle.LoadTextureWithFallback("res://Assets/UI/In-Game_MatchView_PanelBottom.webp");
			}
			_consoleBackground.Texture = texture;

			_hudPanelBottomShader ??= GD.Load<Shader>("res://Assets/shaders/hud_panel_bottom.gdshader");
			var consoleMaterial = new ShaderMaterial();
			consoleMaterial.Shader = _hudPanelBottomShader;
			_consoleBackground.Material = consoleMaterial;
		}

		_bottomConsole.AddThemeStyleboxOverride("panel", new StyleBoxEmpty());
		_minimapFrame.AddThemeStyleboxOverride("panel", new StyleBoxEmpty());
		_portraitFrame.AddThemeStyleboxOverride("panel", new StyleBoxEmpty());
		_selectionFrame.AddThemeStyleboxOverride("panel", new StyleBoxEmpty());
		_selectionFrame.ClipContents = true;
		
		var inventoryFrame = GetNodeOrNull<PanelContainer>("BottomConsole/HBox/InventoryFrame");
		if (inventoryFrame != null)
		{
			inventoryFrame.AddThemeStyleboxOverride("panel", new StyleBoxEmpty());
		}

		_commandFrame.AddThemeStyleboxOverride("panel", new StyleBoxEmpty());

		UIStyle.ApplyTitle(_feedbackLabel, "", 32);
		_feedbackLabel.AddThemeColorOverride("font_color", UIStyle.ColorGold);

		var resourceIcons = new (string nodePath, string assetPath)[]
		{
			("ResourceContainer/HBox/GoldBox/GoldIcon", "res://Assets/UI/gold_coin.webp"),
			("ResourceContainer/HBox/WoodBox/WoodIcon", "res://Assets/UI/wood_logs.webp"),
			("ResourceContainer/HBox/StoneBox/StoneIcon", "res://Assets/UI/stone.webp"),
			("ResourceContainer/HBox/SupplyBox/SupplyIcon", "res://Assets/UI/Supplies.webp"),
			("ResourceContainer/HBox/ClockBox/ClockIcon", "res://Assets/UI/clock.webp"),
		};

		foreach (var (nodePath, assetPath) in resourceIcons)
		{
			var iconRect = GetNodeOrNull<TextureRect>(nodePath);
			if (iconRect != null)
			{
				iconRect.Texture = UIStyle.LoadCrispIcon(assetPath, 256);
				iconRect.TextureFilter = TextureFilterEnum.LinearWithMipmaps;
				iconRect.CustomMinimumSize = new Vector2(36, 36);
				iconRect.ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize;
				iconRect.StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered;
			}
		}

		var resourceHBox = GetNodeOrNull<HBoxContainer>("ResourceContainer/HBox");
		if (resourceHBox != null)
		{
			resourceHBox.AddThemeConstantOverride("separation", 32);
		}

		GetNode<Control>("ResourceContainer/HBox/GoldBox").TooltipText = TranslationServer.Translate("Gold");
		GetNode<Control>("ResourceContainer/HBox/WoodBox").TooltipText = TranslationServer.Translate("Wood");
		GetNode<Control>("ResourceContainer/HBox/StoneBox").TooltipText = TranslationServer.Translate("Stone");
		GetNode<Control>("ResourceContainer/HBox/SupplyBox").TooltipText = TranslationServer.Translate("Population / Supply");
		GetNode<Control>("ResourceContainer/HBox/ClockBox").TooltipText = TranslationServer.Translate("Game Time & Day/Night Cycle");

		var topLabels = new[] { _goldLabel, _woodLabel, _stoneLabel, _populationLabel, _clockLabel };
		foreach (var lbl in topLabels)
		{
			if (lbl == null) continue;
			lbl.AddThemeFontSizeOverride("font_size", 18);
			lbl.AddThemeColorOverride("font_color", UIStyle.ColorGoldDull);
			lbl.AddThemeConstantOverride("outline_size", 2);
			lbl.AddThemeColorOverride("font_outline_color", new Color(0, 0, 0, 0.85f));
			lbl.VerticalAlignment = VerticalAlignment.Center;
		}

		_goldLabel?.AddThemeColorOverride("font_color", UIStyle.ColorGold);

		GetNode<Label>("BottomConsole/HBox/PortraitFrame/VBox/UnitName").AddThemeColorOverride("font_color", UIStyle.ColorGoldDull);

		var minimapBg = MinimapHelper.SetupMinimapBackground(_minimapArea);
		if (minimapBg != null)
		{
			minimapBg.Material = null;
		}

		var overlay = new MinimapOverlay();
		overlay.Name = "MinimapOverlay";
		overlay.MouseFilter = MouseFilterEnum.Ignore;
		_minimapArea.AddChild(overlay);
		overlay.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
		_minimapOverlay = overlay;

		_cameraIndicator.MouseFilter = MouseFilterEnum.Ignore;
		_minimapArea.MoveChild(_cameraIndicator, _minimapArea.GetChildCount() - 1);
	}

	public override void _Process(double delta)
	{
		float dt = (float)delta;

		_unitHealthBarOverlay?.Refresh(_camera3D ?? GetViewport()?.GetCamera3D());
		_minimapPanelController?.UpdateMinimapIndicator();
		if (_connectionWarningLabel != null)
		{
			_connectionWarningLabel.Visible = _viewModel.IsConnectionLost;
		}

		_tacticalTimer += dt;
		if (_tacticalTimer >= TacticalInterval)
		{
			float accumulated = _tacticalTimer;
			_tacticalTimer -= TacticalInterval;
			_viewModel.UpdateTactical(accumulated);
			UpdateSelectedUnitVisuals();
			_portraitPanelController?.Update(_viewModel);
			_commandPanelController?.Update(_viewModel);
			_inventoryPanelController?.Update(_viewModel);
			_controlGroupsUIController?.Update();
		}

		_macroTimer += dt;
		if (_macroTimer >= MacroInterval)
		{
			float accumulated = _macroTimer;
			_macroTimer -= MacroInterval;
			_viewModel.UpdateMacro(accumulated);
			_resourcePanelController?.Update(_viewModel);
			UpdateShroudMinimapTimer(accumulated);
			UpdateIdleUnitButton(accumulated);
		}

		_ambientTimer += dt;
		if (_ambientTimer >= AmbientInterval)
		{
			_ambientTimer -= AmbientInterval;
			_leaderboardPanelController?.Update(_viewModel);
			_summaryTablePanelController?.Update(_viewModel);
		}
	}

	private void UpdateSelectedUnitVisuals()
	{
		if (GameContext.Host?.SelectedUnits == null) return;

		var selected = GameContext.Host.SelectedUnits;
		_viewModel.UpdateSelectedUnits(selected);
	}

	private void UpdateShroudMinimapTimer(double delta)
	{
		_shroudMinimapRefreshTimer += (float)delta;
		if (_shroudMinimapRefreshTimer >= 0.1f)
		{
			_shroudMinimapRefreshTimer = 0f;
			QueueMinimapRedraw();
		}
	}

	private void UpdateIdleUnitButton(double delta)
	{
		if (_btnSelectIdle == null) return;

		if (_lastIdleCount != _viewModel.IdleCount)
		{
			_btnSelectIdle.TooltipText = $"{TranslationServer.Translate("Select All Idle Units [F1]")} ({_viewModel.IdleCount} {TranslationServer.Translate("Idle")})";
		}

		if (_viewModel.IdleCount > 0)
		{
			_idlePulseTimer += (float)delta;
			if (_lastIdleCount == 0)
			{
				ShowFeedbackText(string.Format(TranslationServer.Translate("{0} unit(s) are idle! [F1] to select"), _viewModel.IdleCount), new Color(0.9f, 0.7f, 0.2f));
			}

			float pulse = Mathf.Sin(_idlePulseTimer * 4f) * 0.5f + 0.5f;
			_btnSelectIdle.Modulate = new Color(1f, 0.8f + pulse * 0.2f, 0.2f + pulse * 0.8f, 1f);
		}
		else
		{
			_idlePulseTimer = 0f;
			_btnSelectIdle.Modulate = Colors.White;
		}

		_lastIdleCount = _viewModel.IdleCount;
	}

	public void QueueMinimapRedraw()
	{
		_minimapOverlay?.QueueRedraw();
	}

	public void ShowFeedbackText(string text, Color color)
	{
		_feedbackLabel.Text = TranslationServer.Translate(text);
		_feedbackLabel.AddThemeColorOverride("font_color", color);
		_feedbackLabel.Modulate = new Color(color.R, color.G, color.B, 1.0f);

		var tween = CreateTween();
		tween.TweenProperty(_feedbackLabel, "modulate:a", 0.0f, 1.5f).SetDelay(0.5f);
	}

	public void ToggleMinimapTerrain()
	{
		_viewModel.ShowMinimapTerrain = !_viewModel.ShowMinimapTerrain;
	}

	public void ToggleMinimapColorScheme()
	{
		_viewModel.MinimapColorScheme = _viewModel.MinimapColorScheme == MinimapColorScheme.TeamColors
			? MinimapColorScheme.PlayerColors
			: MinimapColorScheme.TeamColors;

		string schemeName = _viewModel.MinimapColorScheme == MinimapColorScheme.TeamColors
			? TranslationServer.Translate("Team Colors")
			: TranslationServer.Translate("Player Colors");

		ShowFeedbackText(string.Format(TranslationServer.Translate("Minimap Colors: {0}"), schemeName), UIStyle.ColorGold);
		QueueMinimapRedraw();
	}

	private void SetupMinimapButton(Button btn, string iconGlyph, string tooltip, Action onClick)
	{
		btn.Flat = false;
		btn.Text = iconGlyph;
		btn.Icon = null;
		string trans = TranslationServer.Translate(tooltip);
		btn.TooltipText = string.IsNullOrEmpty(trans) ? tooltip : trans;
		btn.FocusMode = FocusModeEnum.None;
		btn.SizeFlagsHorizontal = SizeFlags.ExpandFill;
		btn.SizeFlagsVertical = SizeFlags.ExpandFill;
		btn.Alignment = HorizontalAlignment.Center;
		btn.AddThemeConstantOverride("icon_max_width", 0);

		var font = UIStyle.FontAwesome;
		if (font != null)
		{
			btn.AddThemeFontOverride("font", font);
		}

		btn.AddThemeFontSizeOverride("font_size", 14);
		btn.AddThemeColorOverride("font_color", UIStyle.ColorGoldDull);
		btn.AddThemeColorOverride("font_hover_color", UIStyle.ColorGold);
		btn.AddThemeColorOverride("font_pressed_color", UIStyle.ColorCyanGlow);
		btn.AddThemeColorOverride("font_focus_color", UIStyle.ColorGoldDull);
		btn.AddThemeConstantOverride("outline_size", 2);
		btn.AddThemeColorOverride("font_outline_color", new Color(0f, 0f, 0f, 0.9f));

		btn.AddThemeStyleboxOverride("normal", UIStyle.CreateMinimapButtonStyle(false, false));
		btn.AddThemeStyleboxOverride("hover", UIStyle.CreateMinimapButtonStyle(true, false));
		btn.AddThemeStyleboxOverride("pressed", UIStyle.CreateMinimapButtonStyle(false, true));
		btn.AddThemeStyleboxOverride("focus", new StyleBoxEmpty());

		btn.Pressed += () =>
		{
			GameContext.UserInterface?.PlayClickSound();
			onClick?.Invoke();
		};
		btn.MouseEntered += () => GameContext.UserInterface?.PlayHoverSound();
	}

	private void SetupHUDButton(Button btn, string iconPath, string tooltip, Action onClick)
	{
		btn.Flat = false;
		btn.Text = "";
		btn.ExpandIcon = true;
		btn.Icon = TrimmedTextureLoader.LoadTrimmedTexture(iconPath);
		string trans = TranslationServer.Translate(tooltip);
		btn.TooltipText = string.IsNullOrEmpty(trans) ? tooltip : trans;
		btn.CustomMinimumSize = new Vector2(48, 48);
		btn.SizeFlagsHorizontal = SizeFlags.ExpandFill;
		btn.SizeFlagsVertical = SizeFlags.ExpandFill;
		btn.FocusMode = FocusModeEnum.None;
		UIStyle.SetupHUDButton(btn, tooltip);

		btn.AddThemeStyleboxOverride("normal", UIStyle.CreateHUDButtonStyle(false, false));
		btn.AddThemeStyleboxOverride("hover", UIStyle.CreateHUDButtonStyle(true, false));
		btn.AddThemeStyleboxOverride("pressed", UIStyle.CreateHUDButtonStyle(false, true));
		btn.AddThemeStyleboxOverride("focus", new StyleBoxEmpty());

		btn.Pressed += () => onClick?.Invoke();
	}

	private void SetupPortrait()
	{
		var pTexture = GetNodeOrNull<TextureRect>("BottomConsole/HBox/PortraitFrame/VBox/PortraitTexture");
		if (pTexture != null)
		{
			pTexture.Texture = TrimmedTextureLoader.LoadTrimmedTexture("res://Assets/UI/alliance_flag.webp");
			_hudPortraitMaskShader ??= GD.Load<Shader>("res://Assets/shaders/hud_portrait_mask.gdshader");
			var portraitMaskMaterial = new ShaderMaterial();
			portraitMaskMaterial.Shader = _hudPortraitMaskShader;
			pTexture.Material = portraitMaskMaterial;
		}

		GetNode<Label>("BottomConsole/HBox/PortraitFrame/VBox/UnitName").Text = TranslationServer.Translate("No Selection");
	}

	private void CenterCameraOnSelectedUnit()
	{
		var selected = GameContext.Host?.SelectedUnits;
		if (selected != null && selected.Count > 0)
		{
			int idx = GameContext.Host != null ? GameContext.Host.CycleSelectionIndex : 0;
			if (idx >= 0 && idx < selected.Count)
				CenterCameraOnUnit(selected[idx]);
		}
	}

	private void CenterCameraOnUnit(Unit3D unit)
	{
		if (unit == null || !GodotObject.IsInstanceValid(unit)) return;
		if (_camera3D != null && GodotObject.IsInstanceValid(_camera3D))
		{
			_camera3D.GlobalPosition = new Vector3(unit.GlobalPosition.X, _camera3D.GlobalPosition.Y, unit.GlobalPosition.Z + 25f);
			ShowFeedbackText($"Camera → {unit.UnitId.ToUpper()}", new Color(0.9f, 0.8f, 0.5f));
		}
	}

	public bool IsMouseOverUI(Vector2 mousePos)
	{
		var hoveredControl = GetViewport().GuiGetHoveredControl();
		if (hoveredControl != null && hoveredControl != this)
		{
			return true;
		}

		Control[] controls = { _bottomConsole, _resourceContainer, _hotkeyPanel };
		return Enumerable.Any(controls, c => c != null && c.Visible && c.GetGlobalRect().HasPoint(mousePos));
	}

	public void UpdateDragBox(Vector2 start, Vector2 end, bool isVisible)
	{
		_dragStart = start;
		_dragEnd = end;
		_isDrawingDragBox = isVisible;
		QueueRedraw();
	}

	public override void _Draw()
	{
		if (_isDrawingDragBox)
		{
			DrawRect(new Rect2(_dragStart, _dragEnd - _dragStart), new Color(0.22f, 0.54f, 0.26f, 0.15f), true);
			DrawRect(new Rect2(_dragStart, _dragEnd - _dragStart), new Color(0.22f, 0.54f, 0.26f, 0.6f), false, 2f);
		}
	}


	private void OnUnitSelectionButtonClicked(int index)
	{
		if (GameContext.Host == null) return;
		var selectedUnits = GameContext.Host.SelectedUnits;
		if (selectedUnits == null || index < 0 || index >= selectedUnits.Count) return;

		var clickedUnit = selectedUnits[index];
		bool shiftPressed = Input.IsKeyPressed(Key.Shift);
		bool ctrlPressed = Input.IsKeyPressed(Key.Ctrl);

		if (ctrlPressed)
		{
			string targetId = clickedUnit.UnitId;
			var toDeselect = new List<Unit3D>();
			foreach (var u in selectedUnits)
			{
				if (u.UnitId != targetId)
				{
					toDeselect.Add(u);
				}
			}

			foreach (var u in toDeselect)
			{
				GameContext.Host.DeselectUnit(u);
			}
		}
		else if (shiftPressed)
		{
			GameContext.Host.DeselectUnit(clickedUnit);
		}
		else
		{
			GameContext.Host.SelectOnlyUnit(clickedUnit);
			CenterCameraOnUnit(clickedUnit);
		}

		RefreshUI(GameContext.Host.SelectedUnits);
	}

	public override void _UnhandledInput(InputEvent @event)
	{
		if (HandleFocusNext(@event)) return;

		if (@event is InputEventKey keyEvent && keyEvent.Pressed)
		{
			if (keyEvent.Keycode == Key.Escape && _hotkeyPanelVisible)
			{
				ToggleHotkeyPanel();
				GetViewport().SetInputAsHandled();
				return;
			}

			if (HandleSpaceKey(keyEvent)) return;
			if (HandleEnterKey(keyEvent)) return;
			if (HandlePauseKey(keyEvent)) return;
			if (HandleTildeKey(keyEvent)) return;
		}
	}

	private bool HandleFocusNext(InputEvent @event)
	{
		if (!@event.IsActionPressed("ui_focus_next")) return false;
		if (GameContext.Host == null || GameContext.Host.SelectedUnits.Count <= 1) return false;

		bool reverse = Input.IsKeyPressed(Key.Shift);
		GameContext.Host.CycleSelectionFocus(reverse);
		RefreshUI(GameContext.Host.SelectedUnits);
		GetViewport().SetInputAsHandled();
		return true;
	}

	private bool HandleSpaceKey(InputEventKey keyEvent)
	{
		if (keyEvent.Keycode != Key.Space || IsChatActive) return false;

		CenterCameraOnSelectedUnit();
		GetViewport().SetInputAsHandled();
		return true;
	}

	private bool HandleEnterKey(InputEventKey keyEvent)
	{
		if (keyEvent.Keycode != Key.Enter || IsChatActive) return false;
		if (!IsHudElementVisible(HudElement.Chat, true)) return false;

		_chatPanelController?.ShowChatInput(keyEvent.ShiftPressed);
		GetViewport().SetInputAsHandled();
		return true;
	}

	private bool HandlePauseKey(InputEventKey keyEvent)
	{
		if (keyEvent.Keycode != Key.P && keyEvent.Keycode != Key.Pause) return false;
		if (IsChatActive || GameContext.Host == null) return false;

		GameContext.Host.TogglePauseRequest();
		GetViewport().SetInputAsHandled();
		return true;
	}

	private bool HandleTildeKey(InputEventKey keyEvent)
	{
		if (keyEvent.Keycode != Key.Quoteleft || keyEvent.Echo) return false;
		if (IsChatActive || !WasmConsoleWindow.IsSinglePlayerOrTestMode()) return false;

		GameContext.WasmConsole.ToggleVisibility();
		GetViewport().SetInputAsHandled();
		return true;
	}

	private void OnChatInputSubmitted(string text)
	{
		if (string.IsNullOrWhiteSpace(text)) return;
		if (TryTriggerCheat(text))
		{
			return;
		}
	}

	private void OnLobbyChatReceived(string senderName, string message, bool alliesOnly, long timestampUtcTicks)
	{
		_chatPanelController?.OnLobbyChatReceived(senderName, message, alliesOnly, timestampUtcTicks);
	}

	private void BuildHotkeyReferencePanel()
	{
		_hotkeyPanel = new PanelContainer();
		_hotkeyPanel.Name = "HotkeyPanel";
		_hotkeyPanel.Visible = false;
		_hotkeyPanel.MouseFilter = MouseFilterEnum.Stop;
		_hotkeyPanel.AddThemeStyleboxOverride("panel", new StyleBoxEmpty());

		_hotkeyPanel.SetAnchorsPreset(LayoutPreset.Center);
		_hotkeyPanel.GrowHorizontal = GrowDirection.Both;
		_hotkeyPanel.GrowVertical = GrowDirection.Both;
		_hotkeyPanel.OffsetLeft = -240;
		_hotkeyPanel.OffsetRight = 240;
		_hotkeyPanel.OffsetTop = -372;
		_hotkeyPanel.OffsetBottom = 300;
		_hotkeyPanel.PivotOffset = new Vector2(240f, 336f);

		var bgTexture = new TextureRect();
		bgTexture.Name = "Background";
		bgTexture.Texture = UIStyle.LoadTextureWithFallback("res://Assets/UI/In-Game_MatchView_HotkeyReference.webp");
		bgTexture.ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize;
		bgTexture.StretchMode = TextureRect.StretchModeEnum.Scale;
		bgTexture.SetAnchorsPreset(LayoutPreset.FullRect);
		bgTexture.MouseFilter = MouseFilterEnum.Ignore;
		_hotkeyPanel.AddChild(bgTexture);

		var contentRoot = new Control();
		contentRoot.Name = "ContentRoot";
		contentRoot.SetAnchorsPreset(LayoutPreset.FullRect);
		contentRoot.MouseFilter = MouseFilterEnum.Pass;
		_hotkeyPanel.AddChild(contentRoot);

		var titleLbl = new Label();
		titleLbl.Name = "TitleLabel";
		titleLbl.Text = TranslationServer.Translate("HOTKEY REFERENCE");
		titleLbl.SetAnchorsPreset(LayoutPreset.TopWide);
		titleLbl.OffsetLeft = 65;
		titleLbl.OffsetRight = -105;
		titleLbl.OffsetTop = 22;
		titleLbl.OffsetBottom = 58;
		titleLbl.HorizontalAlignment = HorizontalAlignment.Center;
		titleLbl.VerticalAlignment = VerticalAlignment.Center;
		titleLbl.AddThemeFontOverride("font", UIStyle.FontNorseBold);
		titleLbl.AddThemeFontSizeOverride("font_size", 16);
		titleLbl.AddThemeColorOverride("font_color", UIStyle.ColorGold);
		titleLbl.AddThemeConstantOverride("outline_size", 3);
		titleLbl.AddThemeColorOverride("font_outline_color", new Color(0f, 0f, 0f, 0.95f));
		contentRoot.AddChild(titleLbl);

		var btnClose = new Button();
		btnClose.Name = "BtnClose";
		btnClose.Text = "✕";
		btnClose.SetAnchorsPreset(LayoutPreset.TopRight);
		btnClose.OffsetLeft = -98;
		btnClose.OffsetRight = -72;
		btnClose.OffsetTop = 27;
		btnClose.OffsetBottom = 53;
		btnClose.FocusMode = FocusModeEnum.None;
		btnClose.TooltipText = TranslationServer.Translate("Close [Esc]");
		btnClose.AddThemeFontSizeOverride("font_size", 14);
		btnClose.AddThemeStyleboxOverride("normal", new StyleBoxEmpty());
		var closeHoverStyle = new StyleBoxFlat();
		closeHoverStyle.BgColor = new Color(0.15f, 0.65f, 1.0f, 0.25f);
		closeHoverStyle.CornerRadiusTopLeft = 4;
		closeHoverStyle.CornerRadiusTopRight = 4;
		closeHoverStyle.CornerRadiusBottomLeft = 4;
		closeHoverStyle.CornerRadiusBottomRight = 4;
		btnClose.AddThemeStyleboxOverride("hover", closeHoverStyle);
		var closePressedStyle = new StyleBoxFlat();
		closePressedStyle.BgColor = new Color(0.15f, 0.65f, 1.0f, 0.45f);
		closePressedStyle.CornerRadiusTopLeft = 4;
		closePressedStyle.CornerRadiusTopRight = 4;
		closePressedStyle.CornerRadiusBottomLeft = 4;
		closePressedStyle.CornerRadiusBottomRight = 4;
		btnClose.AddThemeStyleboxOverride("pressed", closePressedStyle);
		btnClose.AddThemeStyleboxOverride("focus", new StyleBoxEmpty());
		btnClose.AddThemeColorOverride("font_color", Colors.White);
		btnClose.AddThemeColorOverride("font_hover_color", UIStyle.ColorGold);
		btnClose.AddThemeColorOverride("font_pressed_color", UIStyle.ColorCyanGlow);
		btnClose.AddThemeConstantOverride("outline_size", 2);
		btnClose.AddThemeColorOverride("font_outline_color", new Color(0f, 0f, 0f, 0.9f));
		btnClose.Pressed += () => ToggleHotkeyPanel();
		contentRoot.AddChild(btnClose);

		var scroll = new ScrollContainer();
		scroll.Name = "HotkeyScroll";
		scroll.SetAnchorsPreset(LayoutPreset.FullRect);
		scroll.OffsetLeft = 90;
		scroll.OffsetRight = -90;
		scroll.OffsetTop = 88;
		scroll.OffsetBottom = -82;
		scroll.HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled;
		scroll.VerticalScrollMode = ScrollContainer.ScrollMode.Auto;
		contentRoot.AddChild(scroll);

		var vScroll = scroll.GetVScrollBar();
		if (vScroll != null)
		{
			vScroll.CustomMinimumSize = new Vector2(4, 0);
			var grabberNormal = new StyleBoxFlat();
			grabberNormal.BgColor = new Color(0.6f, 0.5f, 0.35f, 0.7f);
			grabberNormal.CornerRadiusTopLeft = 2;
			grabberNormal.CornerRadiusTopRight = 2;
			grabberNormal.CornerRadiusBottomLeft = 2;
			grabberNormal.CornerRadiusBottomRight = 2;
			vScroll.AddThemeStyleboxOverride("grabber", grabberNormal);

			var grabberHighlight = new StyleBoxFlat();
			grabberHighlight.BgColor = UIStyle.ColorGold;
			grabberHighlight.CornerRadiusTopLeft = 2;
			grabberHighlight.CornerRadiusTopRight = 2;
			grabberHighlight.CornerRadiusBottomLeft = 2;
			grabberHighlight.CornerRadiusBottomRight = 2;
			vScroll.AddThemeStyleboxOverride("grabber_highlight", grabberHighlight);

			var grabberPressed = new StyleBoxFlat();
			grabberPressed.BgColor = UIStyle.ColorCyanGlow;
			grabberPressed.CornerRadiusTopLeft = 2;
			grabberPressed.CornerRadiusTopRight = 2;
			grabberPressed.CornerRadiusBottomLeft = 2;
			grabberPressed.CornerRadiusBottomRight = 2;
			vScroll.AddThemeStyleboxOverride("grabber_pressed", grabberPressed);

			var scrollTrack = new StyleBoxFlat();
			scrollTrack.BgColor = new Color(0.04f, 0.04f, 0.06f, 0.3f);
			scrollTrack.CornerRadiusTopLeft = 2;
			scrollTrack.CornerRadiusTopRight = 2;
			scrollTrack.CornerRadiusBottomLeft = 2;
			scrollTrack.CornerRadiusBottomRight = 2;
			vScroll.AddThemeStyleboxOverride("scroll", scrollTrack);
		}

		var vbox = new VBoxContainer();
		vbox.Name = "Vbox_VBoxContainer";
		vbox.SizeFlagsHorizontal = SizeFlags.ExpandFill;
		vbox.AddThemeConstantOverride("separation", 3);
		scroll.AddChild(vbox);

		var hotkeyGroups = new (string groupTitle, (string key, string desc)[] items)[]
		{
			(
				"COMMANDS",
				new (string key, string desc)[]
				{
					("Right-Click", "Smart Command (Move/Attack/Rally)"),
					("Shift+R-Click", "Queue Action / Waypoint"),
					("A", "Attack / Attack-Move"),
					("M", "Move to Target"),
					("P", "Patrol Route"),
					("S", "Stop Current Action"),
					("H", "Hold Position"),
					("B", "Open Build Menu"),
				}
			),
			(
				"SELECTION & GROUPS",
				new (string key, string desc)[]
				{
					("Left-Click", "Select Unit (Drag to Box-Select)"),
					("Shift+Click", "Add / Remove from Selection"),
					("Double-Click", "Select All Same Type on Screen"),
					("Tab", "Cycle Selection Focus"),
					("F1", "Select All Idle Units"),
					("F2", "Select All Military Units"),
					("F3", "Select All Buildings"),
					("`", "Cycle Through Buildings"),
					("Ctrl+0..9", "Assign Control Group"),
					("0..9", "Recall Group (Double: Jump)"),
				}
			),
			(
				"CAMERA & MINIMAP",
				new (string key, string desc)[]
				{
					("WASD / Edge", "Pan Camera"),
					("Shift+WASD", "Fast Camera Pan"),
					("Space", "Center Camera on Base"),
					("Z", "Cycle Zoom (Near / Mid / Far)"),
					("F4", "Toggle Terrain Radar Mode"),
					("Alt+G", "Ping Map Alert"),
					("Ctrl+F5..F8", "Save Camera Location 1..4"),
					("F5..F8", "Jump to Saved Camera 1..4"),
				}
			),
			(
				"BUILDINGS & SYSTEM",
				new (string key, string desc)[]
				{
					("Y", "Set Building Rally Point"),
					("Enter", "Open Chat (Shift: All Chat)"),
					("Esc", "Clear Selection / Cancel Targeting"),
					("F12", "Settings"),
					("P / Pause", "Pause / Resume Match"),
					("~ (Tilde)", "Developer Console"),
				}
			),
		};

		int groupIndex = 0;
		foreach (var (groupTitle, items) in hotkeyGroups)
		{
			if (groupIndex > 0)
			{
				var groupSpacer = new Control();
				groupSpacer.Name = "GroupSpacer_Control";
				groupSpacer.CustomMinimumSize = new Vector2(0, 6);
				vbox.AddChild(groupSpacer);
			}

			var catHeader = new PanelContainer();
			catHeader.Name = "CatHeader_PanelContainer";
			var catHeaderStyle = new StyleBoxFlat();
			catHeaderStyle.BgColor = new Color(0.18f, 0.16f, 0.14f, 0.85f);
			catHeaderStyle.BorderColor = new Color(0.6f, 0.5f, 0.35f, 0.5f);
			catHeaderStyle.SetBorderWidthAll(1);
			catHeaderStyle.CornerRadiusTopLeft = 3;
			catHeaderStyle.CornerRadiusTopRight = 3;
			catHeaderStyle.CornerRadiusBottomLeft = 3;
			catHeaderStyle.CornerRadiusBottomRight = 3;
			catHeaderStyle.ContentMarginLeft = 6;
			catHeaderStyle.ContentMarginRight = 6;
			catHeaderStyle.ContentMarginTop = 3;
			catHeaderStyle.ContentMarginBottom = 3;
			catHeader.AddThemeStyleboxOverride("panel", catHeaderStyle);

			var catTitleLbl = new Label();
			catTitleLbl.Name = "CatTitleLbl_Label";
			catTitleLbl.Text = TranslationServer.Translate(groupTitle);
			catTitleLbl.AddThemeFontOverride("font", UIStyle.FontNorseBold);
			catTitleLbl.AddThemeFontSizeOverride("font_size", 13);
			catTitleLbl.AddThemeColorOverride("font_color", UIStyle.ColorGold);
			catTitleLbl.AddThemeConstantOverride("outline_size", 2);
			catTitleLbl.AddThemeColorOverride("font_outline_color", new Color(0, 0, 0, 0.95f));
			catTitleLbl.HorizontalAlignment = HorizontalAlignment.Center;
			catTitleLbl.VerticalAlignment = VerticalAlignment.Center;
			catHeader.AddChild(catTitleLbl);
			vbox.AddChild(catHeader);

			for (int i = 0; i < items.Length; i++)
			{
				var (key, desc) = items[i];

				var rowPanel = new PanelContainer();
				rowPanel.Name = "RowPanel_PanelContainer";
				var rowStyle = new StyleBoxFlat();
				rowStyle.BgColor = (i % 2 == 0)
					? new Color(0.12f, 0.13f, 0.15f, 0.35f)
					: new Color(0.06f, 0.07f, 0.08f, 0.15f);
				rowStyle.CornerRadiusTopLeft = 2;
				rowStyle.CornerRadiusTopRight = 2;
				rowStyle.CornerRadiusBottomLeft = 2;
				rowStyle.CornerRadiusBottomRight = 2;
				rowStyle.ContentMarginLeft = 4;
				rowStyle.ContentMarginRight = 4;
				rowStyle.ContentMarginTop = 2;
				rowStyle.ContentMarginBottom = 2;
				rowPanel.AddThemeStyleboxOverride("panel", rowStyle);

				var row = new HBoxContainer();
				row.Name = "Row_HBoxContainer";
				row.AddThemeConstantOverride("separation", 6);
				row.SizeFlagsHorizontal = SizeFlags.ExpandFill;

				var keyBadge = new PanelContainer();
				keyBadge.Name = "KeyBadge_PanelContainer";
				var badgeStyle = new StyleBoxFlat();
				badgeStyle.BgColor = new Color(0.08f, 0.10f, 0.14f, 0.85f);
				badgeStyle.BorderColor = new Color(0.35f, 0.45f, 0.55f, 0.5f);
				badgeStyle.SetBorderWidthAll(1);
				badgeStyle.CornerRadiusTopLeft = 3;
				badgeStyle.CornerRadiusTopRight = 3;
				badgeStyle.CornerRadiusBottomLeft = 3;
				badgeStyle.CornerRadiusBottomRight = 3;
				badgeStyle.ContentMarginLeft = 4;
				badgeStyle.ContentMarginRight = 4;
				badgeStyle.ContentMarginTop = 1;
				badgeStyle.ContentMarginBottom = 1;
				keyBadge.AddThemeStyleboxOverride("panel", badgeStyle);
				keyBadge.CustomMinimumSize = new Vector2(86, 0);

				var keyLbl = new Label();
				keyLbl.Name = "KeyLbl_Label";
				keyLbl.Text = TranslationServer.Translate(key);
				keyLbl.AddThemeFontSizeOverride("font_size", 10);
				keyLbl.AddThemeColorOverride("font_color", UIStyle.ColorCyanGlow);
				keyLbl.AddThemeConstantOverride("outline_size", 1);
				keyLbl.AddThemeColorOverride("font_outline_color", new Color(0, 0, 0, 0.9f));
				keyLbl.HorizontalAlignment = HorizontalAlignment.Center;
				keyLbl.VerticalAlignment = VerticalAlignment.Center;
				keyBadge.AddChild(keyLbl);
				row.AddChild(keyBadge);

				var descLbl = new Label();
				descLbl.Name = "DescLbl_Label";
				descLbl.Text = TranslationServer.Translate(desc);
				descLbl.AddThemeFontSizeOverride("font_size", 11);
				descLbl.AddThemeColorOverride("font_color", UIStyle.ColorGoldDull);
				descLbl.AutowrapMode = TextServer.AutowrapMode.WordSmart;
				descLbl.CustomMinimumSize = new Vector2(170, 0);
				descLbl.SizeFlagsHorizontal = SizeFlags.ExpandFill;
				descLbl.VerticalAlignment = VerticalAlignment.Center;
				row.AddChild(descLbl);

				rowPanel.AddChild(row);
				vbox.AddChild(rowPanel);
			}

			groupIndex++;
		}

		AddChild(_hotkeyPanel);
	}

	public void ToggleHotkeyPanel()
	{
		_hotkeyPanelVisible = !_hotkeyPanelVisible;
		_hotkeyPanel.Visible = _hotkeyPanelVisible;
		if (_hotkeyPanelVisible && _hotkeyPanel != null)
		{
			_hotkeyPanel.MoveToFront();
		}
	}

	public void StartCountdownTimer(float duration, string labelText)
	{
		_viewModel.CountdownDuration = duration;
		_viewModel.CountdownText = labelText;
		_viewModel.CountdownActive = true;

		if (_countdownCancelButton != null && GodotObject.IsInstanceValid(_countdownCancelButton))
		{
			_countdownCancelButton.Visible = GameContext.Lobby?.IsHost == true;
		}
	}

	private void OnCountdownCancelPressed()
	{
		GameContext.UserInterface?.PlayClickSound();
		GameContext.Lobby?.RequestCancelCountdown();
	}

	public void StopCountdownTimer()
	{
		_viewModel.CountdownActive = false;
	}

	public void UpdateCountdownLabel(string labelText)
	{
		_viewModel.CountdownText = labelText;
	}

	public bool IsHudElementVisible(HudElement element, bool defaultValue = true)
	{
		return _hudElementVisibility.TryGetValue(element, out bool v) ? v : defaultValue;
	}

	public void SetHudElementVisible(HudElement element, bool visible)
	{
		_hudElementVisibility[element] = visible;
		if (GameContext.Host != null)
		{
			GameContext.Host.LocalHudVisibilityOverrides[element] = visible;
		}
		ApplyHudElementVisibility(element);
	}

	public void ApplyHudElementVisibility(HudElement element)
	{
		bool visible = IsHudElementVisible(element, true);
		switch (element)
		{
			case HudElement.Minimap:
				if (_minimapFrame != null) _minimapFrame.Visible = visible;
				if (_minimapControls != null) _minimapControls.Visible = visible;
				break;
			case HudElement.ResourceBar:
				if (_resourceContainer != null) _resourceContainer.Visible = visible;
				break;
			case HudElement.CommandCard:
				if (!visible && _commandFrame != null)
				{
					_commandFrame.Visible = false;
				}
				else if (visible && GameContext.Host != null)
				{
					RefreshUI(GameContext.Host.SelectedUnits);
				}
				break;
			case HudElement.UnitPortrait:
				if (!visible && _portraitFrame != null)
				{
					_portraitFrame.Visible = false;
				}
				else if (visible && _portraitFrame != null)
				{
					_portraitFrame.Visible = true;
				}
				break;
			case HudElement.Chat:
				if (_chatPanel != null) _chatPanel.Visible = visible;
				break;
			case HudElement.Leaderboard:
				if (_leaderboardPanel != null)
				{
					_leaderboardPanel.Visible = visible && _viewModel.HasVisibleLeaderboard;
				}
				break;
			case HudElement.SummaryTable:
				if (_summaryPanel != null)
				{
					_summaryPanel.Visible = visible && _viewModel.SummaryTableVisible;
				}
				break;
			case HudElement.Inventory:
				if (_inventoryFrame != null) _inventoryFrame.Visible = visible;
				if (_inventoryGrid != null) _inventoryGrid.Visible = visible;
				break;
			case HudElement.CountdownTimer:
				if (_countdownPanel != null)
				{
					_countdownPanel.Visible = visible && _viewModel.CountdownActive;
				}
				break;
		}
	}

	public void ApplySavedHudVisibilityOverrides()
	{
		if (GameContext.Host != null)
		{
			foreach (var kvp in GameContext.Host.LocalHudVisibilityOverrides)
			{
				_hudElementVisibility[kvp.Key] = kvp.Value;
				ApplyHudElementVisibility(kvp.Key);
			}
		}
	}

	public void SetLeaderboardBoard(string boardId, string title, bool visible)
	{
		_viewModel.Leaderboards.SetBoard(boardId, title, visible);
	}

	public void ClearLeaderboardBoards()
	{
		_viewModel.Leaderboards.ClearAll();
	}

	public void ClearLeaderboardBoard(string boardId)
	{
		_viewModel.Leaderboards.ClearBoard(boardId);
	}

	public void SetLeaderboardBoardGroup(string boardId, int groupPlayerIndex, string groupTitle)
	{
		_viewModel.Leaderboards.SetGroup(boardId, groupPlayerIndex, groupTitle);
	}

	public void AddLeaderboardBoardRow(string boardId, int groupPlayerIndex, string groupTitle, string label, string value)
	{
		_viewModel.Leaderboards.AddRow(boardId, groupPlayerIndex, groupTitle, label, value);
	}

	public void ShowSummaryTable(string title, bool visible)
	{
		_viewModel.SummaryTableTitle = title;
		_viewModel.SummaryTableVisible = visible;
	}

	public void ClearSummaryTable()
	{
		_viewModel.SummaryTableRows.Clear();
	}

	public void SetSummaryTableHeaders(string[] headers)
	{
		_viewModel.SummaryTableHeaders = headers;
	}

	public void SetSummaryTableRow(string rowKey, string[] cellValues)
	{
		_viewModel.SummaryTableRows[rowKey] = cellValues;
	}


	[Rpc(MultiplayerApi.RpcMode.AnyPeer, CallLocal = true)]
	public void SyncWeather(string weather)
	{
		GameHost tempQualifier = GameContext.Host;
		if ((tempQualifier != null ? GameContext.EnvironmentService : null) != null)
		{
			GameContext.EnvironmentService.SetCurrentWeather(weather);
			float density = weather switch
			{
				"clear" => 0f,
				"rain" => 0.0075f,
				"snow" => 0.005f,
				"fog" => 0.045f,
				_ => 0f
			};
			GameContext.EnvironmentService.SetBaseFogDensity(density);
			GameContext.EnvironmentService.ApplyWeatherVisuals(weather);
		}

		ApplyWeatherEffects(weather);
	}

	public void ApplyWeatherEffects(string weather)
	{
		GameHost tempQualifier = GameContext.Host;
		if ((tempQualifier != null ? GameContext.EnvironmentService : null) != null)
		{
			GameContext.EnvironmentService.ApplyWeatherVisuals(weather);
		}

		if (weather == "clear")
		{
			ShowFeedbackText(TranslationServer.Translate("Weather Forecast: Clear Skies"), new Color(0.3f, 0.9f, 1.0f));
		}
		else if (weather == "rain")
		{
			ShowFeedbackText(TranslationServer.Translate("Weather Forecast: Light Rain Shower"), new Color(0.2f, 0.5f, 0.9f));
		}
		else if (weather == "snow")
		{
			ShowFeedbackText(TranslationServer.Translate("Weather Forecast: Snowfall"), new Color(0.8f, 0.9f, 1.0f));
		}
		else if (weather == "fog")
		{
			ShowFeedbackText(TranslationServer.Translate("Weather Forecast: Dense Fog Warning"), new Color(0.7f, 0.7f, 0.8f));
		}
	}


	public bool TryTriggerCheat(string text)
	{
		if (GameContext.CheatService == null) return false;

		var selectedEntities = GameContext.Host.SelectedUnits != null
			? Enumerable.ToList(Enumerable.Select(GameContext.Host.SelectedUnits, u => u.Entity))
			: new List<Entity>();

		bool isMultiplayer = GameContext.Lobby != null && !GameContext.Lobby.IsSinglePlayer
		                                                   && Multiplayer.MultiplayerPeer != null && !(Multiplayer.MultiplayerPeer is OfflineMultiplayerPeer);

		if (GameContext.PerspectiveFilter.IsSpectating) return false;
		var (result, _) = GameContext.CheatService.TryTriggerCheat(text, isMultiplayer, selectedEntities);

		if (result == CheatResult.None) return false;

		ShowFeedbackText("Cheat Activated!", new Color(0.1f, 0.9f, 0.2f));
		HandleCheatResult(result);

		return true;
	}

	private void HandleCheatResult(CheatResult result)
	{
		switch (result)
		{
			case CheatResult.Gigachad:
				RefreshUI(GameContext.Host.SelectedUnits);
				break;
			case CheatResult.AbsoluteUnit:
				foreach (var unit in GameContext.Host.SelectedUnits)
				{
					if (GodotObject.IsInstanceValid(unit)) unit.Scale = new Vector3(3f, 3f, 3f);
				}

				RefreshUI(GameContext.Host.SelectedUnits);
				break;
			case CheatResult.EzClap:
				GameContext.UserInterface?.PlayClickSound();
				GameContext.UserInterface?.TransitionTo(GameScreen.GameOver, true);
				break;
			case CheatResult.NoCap:
				_ = RegenerateMinimapBackground();
				QueueMinimapRedraw();
				break;
		}
	}

	public bool HandleCommandCardHotkey(Key keycode, KeyModifierMask modifiers)
	{
		if (_commandPanelController == null) return false;
		bool handled = _commandPanelController.HandleHotkey(keycode, modifiers);
		if (handled)
		{
			InvalidateCadence(tactical: true, macro: false);
		}
		return handled;
	}

	private void SetupCommandCard()
	{
		SetupHUDButton(_btnMove, "res://Assets/UI/move_speed.webp", TranslationServer.Translate("[M] Move / Right-Click Ground"), () => GameContext.Host?.EnterCommandTargeting("move"));
		SetupHUDButton(_btnStop, "res://Assets/UI/cancel_button_2.webp", TranslationServer.Translate("[S] Stop Selected Units"), () =>
		{
			ShowFeedbackText(TranslationServer.Translate("Command: Stop Current Action"), new Color(0.9f, 0.2f, 0.2f));
			GameContext.Host?.StopSelectedUnits();
		});
		SetupHUDButton(_btnHold, "res://Assets/UI/magic_upgrade_arrow.webp", TranslationServer.Translate("[H] Hold Position — Unit stays put and attacks in place"), () =>
		{
			ShowFeedbackText(TranslationServer.Translate("Command: Hold Position"), new Color(0.9f, 0.8f, 0.1f));
			GameContext.Host?.HoldSelectedUnits();
		});
		SetupHUDButton(_btnBuild, "res://Assets/UI/golden_hammers.webp", TranslationServer.Translate("[B] Build Structure"), () => EnterBuildSubMenu());
	}

	private void CreateSpectatorPerspectiveUI()
	{
		var specPanel = new PanelContainer();
		specPanel.Name = "SpectatorPanel";
		specPanel.AddThemeStyleboxOverride("panel", UIStyle.CreateStonePanel());

		var specVBox = new VBoxContainer();
		specVBox.Name = "SpecVBox_VBoxContainer";
		specPanel.AddChild(specVBox);

		var title = new Label();
		title.Name = "Title_Label";
		title.Text = TranslationServer.Translate("SPECTATOR MODE");
		UIStyle.ApplyTitle(title, TranslationServer.Translate("SPECTATOR MODE"), 12);
		title.HorizontalAlignment = HorizontalAlignment.Center;
		specVBox.AddChild(title);

		var specHBox = new HBoxContainer();
		specHBox.Name = "SpecHBox_HBoxContainer";
		specHBox.Alignment = BoxContainer.AlignmentMode.Center;
		specHBox.AddThemeConstantOverride("separation", 10);
		specVBox.AddChild(specHBox);

		var btnAll = new Button();
		btnAll.Name = "BtnAll_Button";
		btnAll.Text = TranslationServer.Translate("ALL");
		btnAll.Pressed += () => { LiveSpectatorPerspective = -1; };
		specHBox.AddChild(btnAll);

		var btnBlue = new Button();
		btnBlue.Name = "BtnBlue_Button";
		btnBlue.Text = TranslationServer.Translate("BLUE");
		btnBlue.Pressed += () => { LiveSpectatorPerspective = 1; };
		specHBox.AddChild(btnBlue);

		var btnRed = new Button();
		btnRed.Name = "BtnRed_Button";
		btnRed.Text = TranslationServer.Translate("RED");
		btnRed.Pressed += () => { LiveSpectatorPerspective = 2; };
		specHBox.AddChild(btnRed);

		var btnGreen = new Button();
		btnGreen.Name = "BtnGreen_Button";
		btnGreen.Text = TranslationServer.Translate("GREEN");
		btnGreen.Pressed += () => { LiveSpectatorPerspective = 3; };
		specHBox.AddChild(btnGreen);

		var btnYellow = new Button();
		btnYellow.Name = "BtnYellow_Button";
		btnYellow.Text = TranslationServer.Translate("YELLOW");
		btnYellow.Pressed += () => { LiveSpectatorPerspective = 4; };
		specHBox.AddChild(btnYellow);

		AddChild(specPanel);
	}

	private PanelContainer _pausePanel;
	private VBoxContainer _pausePlayerListContainer;
	private Label _pauseTitleLabel;
	private Button _pauseResumeButton;

	public void UpdatePauseUI()
	{
		if (GameContext.Host == null) return;
		if (_pausePanel == null) BuildPausePanel();

		RefreshPauseMatchButton();

		_pausePanel.Visible = GameContext.NetworkService.IsPaused;
		if (!GameContext.NetworkService.IsPaused) return;

		UpdatePauseTitle();
		UpdatePauseResumeButton();

		ClearPausePlayerList();
		UpdatePausePlayerList();
	}

	private void UpdatePauseResumeButton()
	{
		if (_pauseResumeButton == null) return;

		int countdown = GameContext.NetworkService.PauseResumeCountdownSeconds;
		_pauseResumeButton.Disabled = countdown > 0;
		_pauseResumeButton.Text = countdown > 0
			? string.Format(TranslationServer.Translate("RESUMING IN {0}"), countdown)
			: TranslationServer.Translate("RESUME FOR EVERYONE");
	}

	private void ClearPausePlayerList()
	{
		foreach (var child in _pausePlayerListContainer.GetChildren()) child.QueueFree();
	}

	private void UpdatePauseTitle()
	{
		int countdown = GameContext.NetworkService.PauseResumeCountdownSeconds;
		if (countdown > 0)
		{
			_pauseTitleLabel.Text = string.Format(TranslationServer.Translate("Resuming in {0}"), countdown);
			return;
		}

		int pausedByPlayer = GameContext.NetworkService.PausedByPlayerIndex;
		string pausedByName = ResolvePlayerName(pausedByPlayer);

		_pauseTitleLabel.Text = string.IsNullOrEmpty(pausedByName)
			? TranslationServer.Translate("Game Paused")
			: string.Format(TranslationServer.Translate("Paused by {0}"), pausedByName);
	}

	private static string ResolvePlayerName(int playerSlot)
	{
		if (playerSlot < 0 || GameContext.Lobby == null) return string.Empty;

		foreach (PlayerInfo player in GameContext.Lobby.PlayerList)
		{
			if (player.Slot == playerSlot) return player.Name;
		}

		return string.Empty;
	}

	private void UpdatePausePlayerList()
	{
		if (GameContext.Lobby == null) return;

		foreach (var player in GameContext.Lobby.PlayerList)
		{
			if (player.Team == "Spectator") continue;

			var row = new HBoxContainer();
			row.Name = "Row_HBoxContainer";
			row.AddThemeConstantOverride("separation", 15);

			var nameLabel = new Label { Text = player.Name, CustomMinimumSize = new Vector2(150, 0) };
			row.AddChild(nameLabel);

			_pausePlayerListContainer.AddChild(row);
		}
	}

	private void BuildPausePanel()
	{
		_pausePanel = new PanelContainer();
		_pausePanel.Name = "PausePanel";

		var style = new StyleBoxFlat();
		style.BgColor = new Color(0.1f, 0.1f, 0.12f, 0.95f);
		style.SetBorderWidthAll(2);
		style.BorderColor = UIStyle.ColorBronze;
		style.CornerRadiusTopLeft = 8;
		style.CornerRadiusTopRight = 8;
		style.CornerRadiusBottomLeft = 8;
		style.CornerRadiusBottomRight = 8;
		style.ContentMarginLeft = 20;
		style.ContentMarginRight = 20;
		style.ContentMarginTop = 20;
		style.ContentMarginBottom = 20;
		_pausePanel.AddThemeStyleboxOverride("panel", style);

		_pausePanel.SetAnchorsAndOffsetsPreset(LayoutPreset.Center);
		_pausePanel.GrowHorizontal = GrowDirection.Both;
		_pausePanel.GrowVertical = GrowDirection.Both;

		var mainVBox = new VBoxContainer();
		mainVBox.Name = "MainVBox_VBoxContainer";
		mainVBox.AddThemeConstantOverride("separation", 15);
		_pausePanel.AddChild(mainVBox);

		_pauseTitleLabel = new Label();
		_pauseTitleLabel.Name = "PauseTitleLabel_Label";
		_pauseTitleLabel.Text = "Game Paused";
		_pauseTitleLabel.HorizontalAlignment = HorizontalAlignment.Center;
		_pauseTitleLabel.AddThemeFontSizeOverride("font_size", 24);
		mainVBox.AddChild(_pauseTitleLabel);

		var scroll = new ScrollContainer();
		scroll.Name = "Scroll_ScrollContainer";
		scroll.CustomMinimumSize = new Vector2(450, 150);
		mainVBox.AddChild(scroll);

		_pausePlayerListContainer = new VBoxContainer();
		_pausePlayerListContainer.Name = "PausePlayerListContainer_VBoxContainer";
		_pausePlayerListContainer.AddThemeConstantOverride("separation", 10);
		scroll.AddChild(_pausePlayerListContainer);

		_pauseResumeButton = new Button();
		_pauseResumeButton.Name = "PauseResumeButton";
		_pauseResumeButton.Text = TranslationServer.Translate("RESUME FOR EVERYONE");
		_pauseResumeButton.CustomMinimumSize = new Vector2(0, 34);
		_pauseResumeButton.AddThemeStyleboxOverride("normal", UIStyle.CreateHUDButtonStyle(false, false));
		_pauseResumeButton.AddThemeStyleboxOverride("hover", UIStyle.CreateHUDButtonStyle(true, false));
		_pauseResumeButton.AddThemeStyleboxOverride("pressed", UIStyle.CreateHUDButtonStyle(false, true));
		_pauseResumeButton.AddThemeStyleboxOverride("focus", new StyleBoxEmpty());
		_pauseResumeButton.AddThemeColorOverride("font_color", UIStyle.ColorGoldDull);
		_pauseResumeButton.AddThemeColorOverride("font_hover_color", UIStyle.ColorGold);
		_pauseResumeButton.Pressed += () =>
		{
			GameContext.UserInterface?.PlayClickSound();
			GameContext.Host?.TogglePauseRequest();
		};
		mainVBox.AddChild(_pauseResumeButton);

		AddChild(_pausePanel);
	}

	private string GetBuildOptionTooltip(string unitId, string hotkeyStr)
		{
			HotkeySpec hotkey = HotkeySpec.ParseOrDefault(hotkeyStr);
			return UIHelper.GetUnitCostTooltip(unitId, hotkey, "Build");
		}

		private string GetTrainOptionTooltip(string unitId, string hotkeyStr)
		{
			HotkeySpec hotkey = HotkeySpec.ParseOrDefault(hotkeyStr);
			return UIHelper.GetUnitCostTooltip(unitId, hotkey, "Train");
		}
}
