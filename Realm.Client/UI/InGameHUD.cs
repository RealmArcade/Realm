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
	private readonly List<int> _lastSelectionIds = new();

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
		() => _bottomConsole, () => _minimapFrame, () => _minimapControls, () => _resourceContainer, () => _commandFrame,
		() => _portraitFrame, () => _chatPanel, () => _inventoryFrame, () => _inventoryGrid,
		() => { if (GameContext.Host != null) RefreshUI(GameContext.Host.SelectedUnits); },
		() => _countdownOverlayPanelController?.Update(_viewModel),
		UpdateScoreboardVisibility);



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
	private Label _productionCapacity = null!;
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
	private bool _connectionWarningVisible;
	private bool _connectionWarningWaiting;
	private Control _minimapArea = null!;
	private MinimapCameraIndicator _cameraIndicator = null!;

	private Label _populationLabel = null!;
	private Label _clockLabel = null!;

	private VBoxContainer _customUIPanel = null!;
	private CountdownOverlayPanel _countdownOverlayPanelController = null!;

	private HBoxContainer _controlGroupsContainer = null!;


	private PanelContainer? _hotkeyPanel => (_hotkeyReference?.Root) ?? null;

	private Label _armyCompositionLabel = null!;

	private Camera3D? _camera3D;

	private PanelContainer _chatPanel = null!;
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

	private void SetupUnitHealthBarOverlay() => _unitHealthBarOverlay = UnitHealthBarOverlay.Create(this);

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
		_connectionWarningLabel = ConnectionWarningBanner.Create();
		AddChild(_connectionWarningLabel);
	}

	private MinimapCapture? _minimapCapture;

	/// <summary>The minimap's terrain background capture and the redraw request the shroud service asks for.</summary>
	private MinimapCapture MinimapCapture => _minimapCapture ??= new MinimapCapture(this, () => _minimapArea);

	private CheatCommands? _cheats;

	/// <summary>Chat cheat lines: the service decides what they do, this decides what the player sees.</summary>
	private CheatCommands Cheats => _cheats ??= new CheatCommands(new CheatCommands.Hooks(
		RefreshUI, ShowFeedbackText, RegenerateMinimapBackground, QueueMinimapRedraw));

	private ResourceBarButtons? _resourceBar;

	/// <summary>
	///     The resource bar's buttons: the gear it configures, plus the rules, pause and quit buttons it creates beside
	///     it. Built after <see cref="InitializeReferences" /> resolves the gear, which is what the rest anchor to.
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
		_chatPanelController = ChatPanel.Create(this);
		_chatPanel = _chatPanelController.Panel;

		if (GameContext.Lobby != null)
		{
			GameContext.Lobby.ChatReceived += OnLobbyChatReceived;
		}
	}

	private void SetupCustomUIPanel()
	{
		CustomUiDock.Parts dock = CustomUiDock.Create(this, OnCountdownCancelPressed);
		_customUIPanel = dock.Panel;
		_controlGroupsContainer = dock.ControlGroups;
		_countdownOverlayPanelController = dock.Countdown;
		SetupScoreboardPanel();
	}

	private void SetupScoreboardPanel() => _scoreboardPanelController = ScoreboardPanel.Create(_customUIPanel);

	private void ApplyModeSpecificUI()
	{
		// A test run of a map carries the way back to the editor; every other match does not.
		if (GameContext.SessionState.IsTestMode)
		{
			AddChild(BackToEditorButton.Create());
		}
	}

	private void SetupControllers()
	{
		_resourcePanelController = new ResourcePanel(_resourceContainer);
		_resourcePanelController.InitializeSupplyAndClock(_populationLabel, _clockLabel);

		_camera3D = GameContext.Host?.MainCamera;
		_minimapPanelController = new MinimapPanel(_minimapFrame, _minimapArea, _cameraIndicator, _camera3D);
	
		_portraitPanelController = new PortraitPanel(
			_portraitFrame, _selectionFrame, _unitsContainer, _unitButtons, _statsContainer,
			_unitStatsPanel,
			_productionBox, _productionTitle, _productionCapacity, _productionProgress, _productionQueueLabel, _queueSlotsContainer,
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
		bool isSpectator = GameContext.PerspectiveFilter.IsSpectating;
		Visibility.ApplyModePresentation(GameContext.ReplayPlayback.IsPlayingReplay, isSpectator);

		if (isSpectator)
		{
			CreateSpectatorPerspectiveUI();
		}
	}

	private void OnHUDResized()
	{
		UpdateBottomConsoleHeight();
		ApplyHUDScale();

		InGameHudTheme.ResizeResourcePlate(_resourceBackground, _resourceContainer);

		Vector2 viewportSize = GetViewportRect().Size;
		SpectatorPerspective.PlaceCentered(viewportSize.X);
		HotkeyReference.CenterInPlayfield(viewportSize.X, viewportSize.Y);
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

	public Task RegenerateMinimapBackground() => MinimapCapture.RegenerateAsync();

	private void GenerateDynamicMinimap() => _ = MinimapCapture.RegenerateAsync();

	private void CreateStatsContainer()
	{
		var nodes = SelectionStatsBuilder.Create(_selectionFrame);

		_statsContainer = nodes.Container;
		_unitStatsPanel = nodes.StatsPanel;
		_productionBox = nodes.ProductionBox;
		_productionTitle = nodes.ProductionTitle;
		_productionCapacity = nodes.ProductionCapacity;
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
		_viewModel.SelectedLootItem = GameContext.Host?.SelectedLootItem;
		_viewModel.SelectedResourceNode = GameContext.Host?.SelectedResourceNodeEntity ?? Entity.Null;
		_viewModel.SelectedResourceNodePropId = GameContext.Host?.SelectedResourceNodePropId ?? string.Empty;
		_viewModel.UpdateSelectedUnits(selectedUnits);
		_portraitPanelController?.Update(_viewModel);
		UpdateUnitVitalsBars();
		_commandPanelController?.Update(_viewModel);
		_inventoryPanelController?.Update(_viewModel);

		Visibility.HideDisallowed();
	}

	private InGameHudTheme? _theme;

	private InGameHudTheme HudStyles => _theme ??= new InGameHudTheme();

	private void ApplyThemeStyles()
	{
		HudStyles.Apply(new HudThemeNodes(this, _resourceContainer, _resourceBackground, _consoleBackground, _bottomConsole,
			_minimapFrame, _portraitFrame, _selectionFrame, _commandFrame, _feedbackLabel,
			_goldLabel, _woodLabel, _stoneLabel, _populationLabel, _clockLabel, _minimapArea, _cameraIndicator));

		MinimapCapture.Overlay = HudStyles.MinimapOverlay;
	}

	public override void _Process(double delta)
	{
		float dt = (float)delta;

		_unitHealthBarOverlay?.Refresh(_camera3D ?? GetViewport()?.GetCamera3D());
		_minimapPanelController?.UpdateMinimapIndicator();
		if (_connectionWarningLabel != null)
		{
			bool waiting = _viewModel.IsWaitingForAuthorityDebugger;
			bool visible = waiting || _viewModel.IsConnectionLost;
			if (visible != _connectionWarningVisible || waiting != _connectionWarningWaiting)
			{
				_connectionWarningVisible = visible;
				_connectionWarningWaiting = waiting;
				_connectionWarningLabel.Text = waiting
					? TranslationServer.Translate("Waiting for the authority debugger ... the match is paused")
					: TranslationServer.Translate("Connection to host lost ... Reconnecting");
				_connectionWarningLabel.AddThemeColorOverride("font_color", waiting ? new Color(0.95f, 0.75f, 0.2f) : new Color(0.95f, 0.2f, 0.2f));
				_connectionWarningLabel.Visible = visible;
			}
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
			UpdateIdlePulse(accumulated);
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

		bool unchanged = selectedUnits.Count == _lastSelectionIds.Count;
		if (unchanged)
		{
			for (int index = 0; index < selectedUnits.Count; index++)
			{
				if (selectedUnits[index].Entity.Id == _lastSelectionIds[index]) continue;
				unchanged = false;
				break;
			}
		}

		if (unchanged) return;

		_lastSelectionIds.Clear();
		for (int index = 0; index < selectedUnits.Count; index++)
		{
			_lastSelectionIds.Add(selectedUnits[index].Entity.Id);
		}

		if (_viewModel.IsBuildSubMenuOpen) _viewModel.IsBuildSubMenuOpen = false;
	}


	public bool IsMouseOverUI(Vector2 mousePos)
	{
		var hoveredControl = GetViewport().GuiGetHoveredControl();
		if (hoveredControl != null && hoveredControl != this)
		{
			return true;
		}

		Control?[] controls = { _bottomConsole, _resourceContainer, _hotkeyPanel };
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



	[Rpc(MultiplayerApi.RpcMode.AnyPeer, CallLocal = true)]
	public void SyncWeather(string weather) => WeatherPresenter.Sync(weather, ShowFeedbackText);

	public void ApplyWeatherEffects(string weather) => WeatherPresenter.ApplyEffects(weather, ShowFeedbackText);

	public bool TryTriggerCheat(string text) => Cheats.TryTrigger(text);

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

	private void SetupCommandCard() =>
		CommandCardButtons.Wire(_btnMove, _btnStop, _btnHold, _btnBuild, ShowFeedbackText, EnterBuildSubMenu);

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
