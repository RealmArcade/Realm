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
	private string _lastSelectionSignature = string.Empty;

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

	private PanelContainer _resourceContainer = null!;
	private TextureRect _resourceBackground = null!;
	private PanelContainer _bottomConsole = null!;
	private TextureRect _consoleBackground = null!;
	private PanelContainer _minimapFrame = null!;
	private PanelContainer _portraitFrame = null!;
	private PanelContainer _selectionFrame = null!;
	private PanelContainer _commandFrame = null!;
	private PanelContainer _inventoryFrame = null!;
	private HudVisibility? _visibility;

	/// <summary>
	///     The player's HUD element switches and the panels each one fences.
	/// </summary>
	private HudVisibility Visibility => _visibility ??= new HudVisibility(
		() => _minimapFrame, () => _minimapControls, () => _resourceContainer, () => _commandFrame,
		() => _portraitFrame, () => _chatPanel, () => _inventoryFrame, () => _inventoryGrid,
		() => { if (GameContext.Host != null) RefreshUI(GameContext.Host.SelectedUnits); },
		() => _countdownOverlayPanelController?.Update(_viewModel),
		UpdateScoreboardVisibility);


	private Shader _hudPortraitMaskShader = null!;

	private GridContainer _commandGrid = null!;
	private GridContainer _inventoryGrid = null!;
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

	private VBoxContainer _productionBox = null!;
	private Label _productionTitle = null!;
	private ProgressBar _productionProgress = null!;
	private Label _productionQueueLabel = null!;
	private HBoxContainer _queueSlotsContainer = null!;

	private VBoxContainer _minimapControls = null!;
	private Button _btnSettings = null!;
	public bool ShowMinimapTerrain => _viewModel.ShowMinimapTerrain;
	public MinimapColorScheme MinimapColorScheme => _viewModel.MinimapColorScheme;


	private float _shroudMinimapRefreshTimer = 0f;

	private Label _goldLabel = null!;
	private Label _woodLabel = null!;
	private Label _stoneLabel = null!;

	private List<Button> _unitButtons = new List<Button>();
	private HBoxContainer _unitsContainer = null!;

	private HBoxContainer _statsContainer = null!;
	private UnitStatsPanel _unitStatsPanel = null!;
	private UnitVitalsBars _unitVitalsBars = null!;
	private Vector2 _dragStart;
	private Vector2 _dragEnd;
	private bool _isDrawingDragBox = false;

	private Button _btnMove = null!;
	private Button _btnStop = null!;
	private Button _btnHold = null!;
	private Button _btnBuild = null!;

	private Label _feedbackLabel = null!;
	private Label _connectionWarningLabel = null!;
	private Control _minimapArea = null!;
	private MinimapCameraIndicator _cameraIndicator = null!;

	private Label _populationLabel = null!;
	private Label _clockLabel = null!;

	private VBoxContainer _customUIPanel = null!;
	private PanelContainer _scoreboardPanel = null!;
	private VBoxContainer _scoreboardContent = null!;
	private VBoxContainer _scoreboardSelectors = null!;
	private VBoxContainer _scoreboardLeaderboardContent = null!;
	private VBoxContainer _scoreboardSummaryContent = null!;
	private Label _scoreboardTitleLabel = null!;
	private Button _scoreboardCollapseButton = null!;
	private CountdownOverlayPanel _countdownOverlayPanelController = null!;

	private HBoxContainer _controlGroupsContainer = null!;

	private float _idlePulseTimer = 0f;
	private int _lastIdleCount = 0;

	private PanelContainer? _hotkeyPanel => (_hotkeyReference?.Root) ?? null;

	private Label _armyCompositionLabel = null!;

	private Camera3D? _camera3D;

	private PanelContainer _chatPanel = null!;
	private LineEdit _chatInput = null!;
	private RichTextLabel _chatLog = null!;
	public bool IsChatActive => _chatPanelController.IsChatActive;

	public int LiveSpectatorPerspective
	{
		get => GameContext.Host != null ? GameContext.SpectatorService.GetSpectatorPerspective() : -1;
		set
		{
			if (GameContext.Host != null) GameContext.SpectatorService.SetSpectatorPerspective(value);
		}
	}

	private ResourcePanel _resourcePanelController = null!;
	private MinimapPanel _minimapPanelController = null!;
	private ChatPanel _chatPanelController = null!;
	private ScoreboardPanel _scoreboardPanelController = null!;
	private PortraitPanel _portraitPanelController = null!;
	private CommandPanel _commandPanelController = null!;
	private InventoryPanel _inventoryPanelController = null!;
	private ControlGroupsUIController _controlGroupsUIController = null!;

	private MinimapOverlay _minimapOverlay = null!;
	private UnitHealthBarOverlay _unitHealthBarOverlay = null!;

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
		ResourceBar.Build();
		SetupSelectionFrame();
		SetupCommandAndInventoryGrids();
		SetupWarningLabel();
		SetupMinimapControls();
		string currentWeather = WeatherPresenter.ResolveCurrent();
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

		// The match may have been running before this screen was built - a map tested from the editor starts its
		// authority first - so what it declared about its rules and its standings is replayed now that there is a HUD.
		GameContext.Host?.OnInGameHudReady();
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

	/// <summary>
	///     Takes one page the map published for the Instructions menu. Pages keep the order they arrive in, and a page
	///     whose title is already listed is restated in place, so a rule that changes during a match does not become a
	///     second entry.
	/// </summary>
	public void AddInstructionsSection(string title, string body)
	{
		if (string.IsNullOrWhiteSpace(title)) return;

		List<InstructionsSection> sections = _viewModel.InstructionSections;
		sections.RemoveAll(section => string.Equals(section.Title, title, StringComparison.Ordinal));
		sections.Add(new InstructionsSection(title, body ?? string.Empty));

		ResourceBar.SetInstructionsAvailable(sections.Count > 0);
		ResourceBar.RefreshOpenInstructions();
	}

	/// <summary>
	///     The pause control every player has: it asks the match to hold where it is, and while the match is held it asks
	///     for the resume countdown that belongs to everyone. What a player is still allowed to ask for is the match's
	///     answer, so the button reports the match's state rather than deciding anything itself.
	/// </summary>
	private void SetupSelectionFrame()
	{
		SelectionUnitStrip.Parts strip = SelectionUnitStrip.Rebuild(this);
		_unitsContainer = strip.Container;
		_unitButtons = strip.Buttons;
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

	private ResourceBarButtons? _resourceBar;

	/// <summary>
	///     The rules, pause and quit buttons beside the settings gear. They are built after
	///     <see cref="InitializeReferences" /> resolves the gear, which is what they anchor themselves to.
	/// </summary>
	private ResourceBarButtons ResourceBar => _resourceBar ??= new ResourceBarButtons(_btnSettings, this, () => _viewModel.InstructionSections);

	private MinimapControlsPanel? _minimapControlsPanel;

	private MinimapControlsPanel MinimapControls => _minimapControlsPanel ??= new MinimapControlsPanel();

	private void SetupMinimapControls()
	{
		MinimapControls.Build(new MinimapControlsHooks(this, ToggleMinimapTerrain, ToggleMinimapColorScheme,
			ShowFeedbackText, ToggleHotkeyPanel));
		_minimapControls = MinimapControls.Root!;
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
		chatPanelStyle.SetCornerRadiusAll(4);
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

		SetupCountdownOverlay();
		SetupScoreboardPanel();
	}

	private void SetupCountdownOverlay()
	{
		_countdownOverlayPanelController = new CountdownOverlayPanel(this, _customUIPanel, OnCountdownCancelPressed);
	}

	private void SetupScoreboardPanel()
	{
		var nodes = ScoreboardBuilder.Create(_customUIPanel);

		_scoreboardPanel = nodes.Panel;
		_scoreboardTitleLabel = nodes.TitleLabel;
		_scoreboardCollapseButton = nodes.CollapseButton;
		_scoreboardContent = nodes.Content;
		_scoreboardSelectors = nodes.Selectors;
		_scoreboardLeaderboardContent = nodes.LeaderboardContent;
		_scoreboardSummaryContent = nodes.SummaryContent;
	}

	private void ApplyModeSpecificUI()
	{
		if (GameContext.SessionState.IsTestMode)
		{
			var btnBackToEditor = new Button();
			btnBackToEditor.Name = "BtnBackToEditor";
			btnBackToEditor.Text = TranslationServer.Translate("Back to Editor");
			btnBackToEditor.Set("icon_max_width", 0);
			UIStyle.ApplyButtonStyleBoxes(btnBackToEditor);
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
		_scoreboardPanelController = new ScoreboardPanel(_scoreboardPanel, _scoreboardTitleLabel, _scoreboardCollapseButton, _scoreboardContent, _scoreboardSelectors, _scoreboardLeaderboardContent, _scoreboardSummaryContent);

		_portraitPanelController = new PortraitPanel(
			_portraitFrame, _selectionFrame, _unitsContainer, _unitButtons, _statsContainer,
			_unitStatsPanel,
			_productionBox, _productionTitle, _productionProgress, _productionQueueLabel, _queueSlotsContainer,
			_armyCompositionLabel, GetNode<Label>("BottomConsole/HBox/PortraitFrame/VBox/UnitName"),
			GetNodeOrNull<TextureRect>("BottomConsole/HBox/PortraitFrame/VBox/PortraitTexture") != null ? GetNode<TextureRect>("BottomConsole/HBox/PortraitFrame/VBox/PortraitTexture") : null
		);
		_portraitPanelController.UnitSelectionButtonClicked += OnUnitSelectionButtonClicked;

		_unitVitalsBars = new UnitVitalsBars();
		_bottomConsole.AddChild(_unitVitalsBars.Root);
		UpdateBottomConsoleHeight();

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

		if (_hotkeyReference != null)
		{
			float vpW = GetViewportRect().Size.X;
			float vpH = GetViewportRect().Size.Y;
			if (vpW <= 0f) vpW = 1920f;
			if (vpH <= 0f) vpH = 1080f;
			float consoleH = vpW * (BottomConsoleLayout.BaseConsoleHeight / 1920f);
			float playfieldCenterY = (80f + (vpH - consoleH)) / 2f;
			_hotkeyReference.PlaceOverPlayfield(playfieldCenterY, vpH);
		}
	}

	private BottomConsoleLayout? _consoleLayout;

	private BottomConsoleLayout ConsoleLayout => _consoleLayout ??= new BottomConsoleLayout();

	private ConsoleLayoutNodes ConsoleNodes => new(this,
		() => _bottomConsole,
		() => _consoleBackground,
		() => _unitVitalsBars,
		() => _minimapFrame,
		() => _minimapControls,
		() => _portraitFrame,
		() => _selectionFrame,
		() => _unitStatsPanel,
		() => _commandFrame,
		() => _chatPanel,
		() => _resourceContainer,
		() => _controlGroupsContainer,
		() => _customUIPanel);

	private void UpdateBottomConsoleHeight() => ConsoleLayout.Apply(ConsoleNodes);

	public void ApplyHUDScale() => ConsoleLayout.ApplyHudScale(ConsoleNodes, GameSettings.HudScale / 100f);

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
			GameLog.LogError($"Failed to dynamically capture terrain minimap: {ex.Message}");
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
		var nodes = SelectionStatsBuilder.Create(_selectionFrame);

		_statsContainer = nodes.Container;
		_unitStatsPanel = nodes.StatsPanel;
		_productionBox = nodes.ProductionBox;
		_productionTitle = nodes.ProductionTitle;
		_productionProgress = nodes.ProductionProgress;
		_productionQueueLabel = nodes.ProductionQueueLabel;
		_queueSlotsContainer = nodes.QueueSlots;
	}

	private void UpdateUnitVitalsBars()
	{
		if (_unitVitalsBars == null) return;

		List<SelectedUnitInfo> selectedUnits = _viewModel.SelectedUnits;
		if (selectedUnits == null || selectedUnits.Count != 1)
		{
			_unitVitalsBars.Hide();
			return;
		}

		int index = _viewModel.CycleSelectionIndex < selectedUnits.Count ? _viewModel.CycleSelectionIndex : 0;
		_unitVitalsBars.UpdateSingleSelection(selectedUnits[index]);
	}

	public void RefreshUI(List<Unit3D>? selectedUnits)
	{
		InvalidateCadence(tactical: true, macro: false);

		if (selectedUnits != null)
		{
			bool hasPlayerSelection = Enumerable.Any(selectedUnits, u => GameContext.PerspectiveFilter.CanControl(u.Player));
			bool isSpectator = GameContext.PerspectiveFilter.IsSpectating;
			bool commandCardAllowed = IsHudElementVisible(HudElement.CommandCard, true);
			_commandFrame.Visible = !isSpectator && hasPlayerSelection && commandCardAllowed;
		}

		ResetBuildSubMenuOnSelectionChange(selectedUnits);

		_viewModel.SelectedProp = GameContext.Host?.SelectedProp;
		_viewModel.UpdateSelectedUnits(selectedUnits);
		_portraitPanelController?.Update(_viewModel);
		UpdateUnitVitalsBars();
		_commandPanelController?.Update(_viewModel);
		_inventoryPanelController?.Update(_viewModel);

		Visibility.HideDisallowed();
	}

	private InGameHudTheme? _theme;

	private InGameHudTheme Theme => _theme ??= new InGameHudTheme();

	private void ApplyThemeStyles()
	{
		Theme.Apply(new HudThemeNodes(this, _resourceContainer, _resourceBackground, _consoleBackground, _bottomConsole,
			_minimapFrame, _portraitFrame, _selectionFrame, _commandFrame, _feedbackLabel,
			_goldLabel, _woodLabel, _stoneLabel, _populationLabel, _clockLabel, _minimapArea, _cameraIndicator));

		_minimapOverlay = Theme.MinimapOverlay!;
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
			UpdateUnitVitalsBars();
			_commandPanelController?.Update(_viewModel);
			_inventoryPanelController?.Update(_viewModel);
			_controlGroupsUIController?.Update();
			_countdownOverlayPanelController?.Update(_viewModel);
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
			_scoreboardPanelController?.Update(_viewModel);
		}
	}

	private void UpdateSelectedUnitVisuals()
	{
		if (GameContext.Host?.SelectedUnits == null) return;

		var selected = GameContext.Host.SelectedUnits;
		ResetBuildSubMenuOnSelectionChange(selected);
		_viewModel.UpdateSelectedUnits(selected);
	}

	/// <summary>
	///     The Build submenu belongs to the unit that opened it, so a new selection closes it instead of leaving a
	///     builder's build options on another unit's command card.
	/// </summary>
	private void ResetBuildSubMenuOnSelectionChange(List<Unit3D>? selectedUnits)
	{
		if (selectedUnits == null) return;

		string signature = string.Join(";", selectedUnits.Select(unit => unit.Entity.Id));
		if (string.Equals(signature, _lastSelectionSignature, StringComparison.Ordinal)) return;

		_lastSelectionSignature = signature;
		if (_viewModel.IsBuildSubMenuOpen) _viewModel.IsBuildSubMenuOpen = false;
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
		if (MinimapControls.IdleButton is not { } idleButton) return;

		if (_lastIdleCount != _viewModel.IdleCount)
		{
			idleButton.TooltipText = $"{TranslationServer.Translate("Select All Idle Units [F1]")} ({_viewModel.IdleCount} {TranslationServer.Translate("Idle")})";
		}

		if (_viewModel.IdleCount > 0)
		{
			_idlePulseTimer += (float)delta;
			if (_lastIdleCount == 0)
			{
				ShowFeedbackText(string.Format(TranslationServer.Translate("{0} unit(s) are idle! [F1] to select"), _viewModel.IdleCount), new Color(0.9f, 0.7f, 0.2f));
			}

			float pulse = Mathf.Sin(_idlePulseTimer * 4f) * 0.5f + 0.5f;
			idleButton.Modulate = new Color(1f, 0.8f + pulse * 0.2f, 0.2f + pulse * 0.8f, 1f);
		}
		else
		{
			_idlePulseTimer = 0f;
			idleButton.Modulate = Colors.White;
		}

		_lastIdleCount = _viewModel.IdleCount;
	}

	public void QueueMinimapRedraw()
	{
		_minimapOverlay?.QueueRedraw();
	}

	public void ShowFeedbackText(string text, Color color)
	{
		_feedbackLabel.Text = UIHelper.StripBbCode(TranslationServer.Translate(text));
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

		UIStyle.ApplyHudButtonStyleboxes(btn);
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
		if (!SelectionClickPolicy.Apply(index, CenterCameraOnUnit)) return;
		RefreshUI(GameContext.Host!.SelectedUnits);
	}

	public override void _UnhandledInput(InputEvent @event)
	{
		if (HandleFocusNext(@event)) return;

		if (@event is InputEventKey keyEvent && keyEvent.Pressed)
		{
			if (keyEvent.Keycode == Key.Escape && HotkeyReference.Visible)
			{
				HotkeyReference.Close();
				GetViewport().SetInputAsHandled();
				return;
			}

			if (HandleSpaceKey(keyEvent)) return;
			if (HandleEnterKey(keyEvent)) return;
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

	private HotkeyReferencePanel? _hotkeyReference;

	private HotkeyReferencePanel HotkeyReference => _hotkeyReference ??= new HotkeyReferencePanel();

	private void BuildHotkeyReferencePanel() => HotkeyReference.Build(this);

	public void ToggleHotkeyPanel() => HotkeyReference.Toggle();

	public void StartCountdownTimer(float duration, string labelText)
	{
		_viewModel.CountdownDuration = duration;
		_viewModel.CountdownText = labelText;
		_viewModel.CountdownActive = true;
		_countdownOverlayPanelController?.Update(_viewModel);
	}

	private void OnCountdownCancelPressed()
	{
		GameContext.UserInterface?.PlayClickSound();
		GameContext.Lobby?.RequestCancelCountdown();
	}

	public void StopCountdownTimer()
	{
		_viewModel.CountdownActive = false;
		_countdownOverlayPanelController?.Update(_viewModel);
	}

	public void UpdateCountdownLabel(string labelText)
	{
		_viewModel.CountdownText = labelText;
	}

	public bool IsHudElementVisible(HudElement element, bool defaultValue = true) => Visibility.IsVisible(element, defaultValue);

	public void SetHudElementVisible(HudElement element, bool visible)
	{
		Visibility.Set(element, visible);
		if (GameContext.Host != null)
		{
			GameContext.Host.LocalHudVisibilityOverrides[element] = visible;
		}
		ApplyHudElementVisibility(element);
	}

	public void ApplyHudElementVisibility(HudElement element) => Visibility.Apply(element);

	public void ApplySavedHudVisibilityOverrides()
	{
		if (GameContext.Host != null)
		{
			foreach (var kvp in GameContext.Host.LocalHudVisibilityOverrides)
			{
				Visibility.Set(kvp.Key, kvp.Value);
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

	private void UpdateScoreboardVisibility()
	{
		_scoreboardPanelController?.Update(_viewModel);
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
	public void SyncWeather(string weather) => WeatherPresenter.Sync(weather, ShowFeedbackText);

	public void ApplyWeatherEffects(string weather) => WeatherPresenter.ApplyEffects(weather, ShowFeedbackText);

	public bool TryTriggerCheat(string text)
	{
		if (GameContext.CheatService == null) return false;

		var selectedEntities = GameContext.Host?.SelectedUnits is { } selectedUnits
			? Enumerable.ToList(Enumerable.Select(selectedUnits, u => u.Entity))
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
				RefreshUI(GameContext.Host?.SelectedUnits);
				break;
			case CheatResult.AbsoluteUnit:
			{
				if (GameContext.Host is not { } host) break;

				foreach (var unit in host.SelectedUnits)
				{
					if (GodotObject.IsInstanceValid(unit)) unit.Scale = new Vector3(3f, 3f, 3f);
				}

				RefreshUI(host.SelectedUnits);
				break;
			}
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

	private SpectatorPerspectiveOverlay? _spectatorPerspective;

	private SpectatorPerspectiveOverlay SpectatorPerspective => _spectatorPerspective ??= new SpectatorPerspectiveOverlay();

	private void CreateSpectatorPerspectiveUI() =>
		SpectatorPerspective.Build(this, perspective => LiveSpectatorPerspective = perspective);

	private PauseOverlayPanel? _pauseOverlay;

	private PauseOverlayPanel PauseOverlay => _pauseOverlay ??= new PauseOverlayPanel();

	public void UpdatePauseUI()
	{
		if (GameContext.Host == null) return;

		ResourceBar.RefreshPauseMatch();
		PauseOverlay.Update(this);
	}

}
