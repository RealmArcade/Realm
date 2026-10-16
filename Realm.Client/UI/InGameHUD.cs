using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Arch.Core;
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

public partial class InGameHUD : Control
{
	public InGameHUD()
	{
		Instance = this;
	}

	public override void _EnterTree()
	{
		Realm.Client.Services.GameContext.EnsureServices();
	}

	public static InGameHUD Instance { get; set; }

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

	private GridContainer _commandGrid;
	private GridContainer _inventoryGrid;
	public bool IsBuildSubMenuOpen => _viewModel.IsBuildSubMenuOpen;

	public void EnterBuildSubMenu()
	{
		_viewModel.IsBuildSubMenuOpen = true;
		if (GameHost.Instance != null)
		{
			RefreshUI(GameHost.Instance.SelectedUnits);
		}
	}

	public void ExitBuildSubMenu()
	{
		_viewModel.IsBuildSubMenuOpen = false;
		if (GameHost.Instance != null)
		{
			RefreshUI(GameHost.Instance.SelectedUnits);
		}
	}

	private Button _btnCancelBuild;

	private Button _btnSetRally;

	private VBoxContainer _productionBox;
	private Label _productionTitle;
	private ProgressBar _productionProgress;
	private Label _productionQueueLabel;
	private HBoxContainer _queueSlotsContainer;

	private VBoxContainer _minimapControls;
	private Button _btnZoom;
	private Button _btnToggleTerrain;
	private Button _btnPing;
	private Button _btnCenter;
	private Button _btnSettings;
	public bool ShowMinimapTerrain => _viewModel.ShowMinimapTerrain;


	private float _fogUpdateTimer = 0f;

	private static readonly byte[,] _emptyShroudGrid = new byte[32, 32];

	public byte[,] ShroudGrid
	{
		get
		{
			if (GameHost.Instance != null && GameContext.World != null)
			{
				var worldEntity = ECSContext.WorldEntity;
				if (worldEntity != Entity.Null && GameContext.World.IsAlive(worldEntity) && GameContext.World.Has<ShroudState>(worldEntity))
				{
					return GameContext.World.Get<ShroudState>(worldEntity).ShroudGrid;
				}

				Entity fallback = Entity.Null;
				var query = QueryCache.AllShroudStateQuery;
				GameContext.World.Query(in query, ent => fallback = ent);
				if (fallback != Entity.Null && GameContext.World.Has<ShroudState>(fallback))
				{
					return GameContext.World.Get<ShroudState>(fallback).ShroudGrid;
				}
			}

			return _emptyShroudGrid;
		}
		set
		{
			if (GameHost.Instance != null && GameContext.World != null)
			{
				var worldEntity = ECSContext.WorldEntity;
				if (worldEntity != Entity.Null && GameContext.World.IsAlive(worldEntity) && GameContext.World.Has<ShroudState>(worldEntity))
				{
					ref var state = ref GameContext.World.Get<ShroudState>(worldEntity);
					state.ShroudGrid = value;
					return;
				}

				Entity fallback = Entity.Null;
				var query = QueryCache.AllShroudStateQuery;
				GameContext.World.Query(in query, ent => fallback = ent);
				if (fallback != Entity.Null && GameContext.World.Has<ShroudState>(fallback))
				{
					ref var state = ref GameContext.World.Get<ShroudState>(fallback);
					state.ShroudGrid = value;
				}
			}
		}
	}

	public byte[,] FogGrid
	{
		get => ShroudGrid;
		set => ShroudGrid = value;
	}

	private byte[,] _fogGrid
	{
		get => ShroudGrid;
		set => ShroudGrid = value;
	}

	public string ShroudType
	{
		get
		{
			if (GameHost.Instance != null && GameContext.World != null)
			{
				var worldEntity = ECSContext.WorldEntity;
				if (worldEntity != Entity.Null && GameContext.World.IsAlive(worldEntity) && GameContext.World.Has<ShroudState>(worldEntity))
				{
					return GameContext.World.Get<ShroudState>(worldEntity).ShroudType;
				}

				Entity fallback = Entity.Null;
				var query = QueryCache.AllShroudStateQuery;
				GameContext.World.Query(in query, ent => fallback = ent);
				if (fallback != Entity.Null && GameContext.World.Has<ShroudState>(fallback))
				{
					return GameContext.World.Get<ShroudState>(fallback).ShroudType;
				}
			}

			return "VisionShroud";
		}
		set
		{
			if (GameHost.Instance != null && GameContext.World != null)
			{
				var worldEntity = ECSContext.WorldEntity;
				if (worldEntity != Entity.Null && GameContext.World.IsAlive(worldEntity) && GameContext.World.Has<ShroudState>(worldEntity))
				{
					ref var state = ref GameContext.World.Get<ShroudState>(worldEntity);
					state.ShroudType = value;
					return;
				}

				Entity fallback = Entity.Null;
				var query = QueryCache.AllShroudStateQuery;
				GameContext.World.Query(in query, ent => fallback = ent);
				if (fallback != Entity.Null && GameContext.World.Has<ShroudState>(fallback))
				{
					ref var state = ref GameContext.World.Get<ShroudState>(fallback);
					state.ShroudType = value;
				}
			}
		}
	}

	private string _currentWeather
	{
		get
		{
			GameHost tempQualifier = GameHost.Instance;
			return (tempQualifier != null ? GameContext.EnvironmentService : null)?.GetCurrentWeather() ?? "clear";
		}
		set
		{
			GameHost tempQualifier = GameHost.Instance;
			(tempQualifier != null ? GameContext.EnvironmentService : null)?.SetCurrentWeather(value);
		}
	}

	private float _baseFogDensity
	{
		get
		{
			GameHost tempQualifier = GameHost.Instance;
			return (tempQualifier != null ? GameContext.EnvironmentService : null)?.GetBaseFogDensity() ?? 0f;
		}
		set
		{
			GameHost tempQualifier = GameHost.Instance;
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
	private Button _btnAttack;
	private Button _btnBuild;
	private Button _btnPatrol;

	private Label _feedbackLabel;
	private Label _connectionWarningLabel;
	private Control _minimapArea;
	private MinimapCameraIndicator _cameraIndicator;

	private Label _populationLabel;
	private Label _clockLabel;

	private VBoxContainer _customUIPanel;
	private PanelContainer _leaderboardPanel;
	private VBoxContainer _leaderboardContent;
	private Label _leaderboardTitleLabel;
	private PanelContainer _countdownPanel;
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
			GameHost tempQualifier = GameHost.Instance;
			return (tempQualifier != null ? GameContext.SpectatorService : null)?.GetSpectatorPerspective() ?? -1;
		}
		set
		{
			GameHost tempQualifier = GameHost.Instance;
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

	public override void _Ready()
	{
		Instance = this;

		InitializeReferences();
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
			_btnSettings.Icon = UIStyle.LoadCrispIcon("res://Assets/UI/gear_icon.png", 128);
			_btnSettings.TextureFilter = TextureFilterEnum.LinearWithMipmaps;
			_btnSettings.AddThemeConstantOverride("icon_max_width", 22);
			_btnSettings.TooltipText = TranslationServer.Translate("Settings (Esc)");
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
				UIManager.Instance?.PlayClickSound();
				UIManager.Instance?.OpenSettingsOverlay();
			};
		}
	}

	private void SetupSelectionFrame()
	{
		_unitsContainer = GetNode<HBoxContainer>("BottomConsole/HBox/SelectionFrame/UnitsContainer");

		foreach (Node child in _unitsContainer.GetChildren())
			_unitsContainer.RemoveChild(child);
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

		_btnAttack = new Button();
		_btnAttack.Name = "BtnAttack";
		_btnAttack.CustomMinimumSize = new Vector2(48, 48);
		_btnAttack.SizeFlagsHorizontal = SizeFlags.ExpandFill;
		_btnAttack.SizeFlagsVertical = SizeFlags.ExpandFill;
		_btnAttack.FocusMode = FocusModeEnum.None;
		_btnAttack.AddThemeConstantOverride("icon_max_width", 42);

		_btnPatrol = new Button();
		_btnPatrol.Name = "BtnPatrol";
		_btnPatrol.CustomMinimumSize = new Vector2(48, 48);
		_btnPatrol.SizeFlagsHorizontal = SizeFlags.ExpandFill;
		_btnPatrol.SizeFlagsVertical = SizeFlags.ExpandFill;
		_btnPatrol.FocusMode = FocusModeEnum.None;
		_btnPatrol.AddThemeConstantOverride("icon_max_width", 42);
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
		SetupMinimapButton(_btnZoom, UnicodeIcons.ZOOM_IN, "Toggle Camera Zoom (Near / Mid / Far) [Z]", () => GameHost.Instance?.CycleCameraZoom());
		_minimapControls.AddChild(_btnZoom);

		_btnToggleTerrain = new Button();
		SetupMinimapButton(_btnToggleTerrain, UnicodeIcons.MAP_IMAGE, "Toggle Terrain Overlay (Radar Mode) [F4]", () => ToggleMinimapTerrain());
		_minimapControls.AddChild(_btnToggleTerrain);

		_btnPing = new Button();
		SetupMinimapButton(_btnPing, UnicodeIcons.CROSSHAIRS_TARGET, "Send Alert Ping on Map [Alt+G]", () =>
		{
			if (GameHost.Instance != null)
			{
				GameHost.Instance.ActivePingMode = true;
				ShowFeedbackText("Ping Mode: Click Minimap or Ground to ping", new Color(1.0f, 0.1f, 0.2f));
			}
		});
		_minimapControls.AddChild(_btnPing);

		_btnCenter = new Button();
		SetupMinimapButton(_btnCenter, UnicodeIcons.CASTLE, "Center Camera on Castle [Space]", () => GameHost.Instance?.CenterCameraOnCastle());
		_minimapControls.AddChild(_btnCenter);

		_btnSelectIdle = new Button();
		SetupMinimapButton(_btnSelectIdle, UnicodeIcons.USER_CLOCK, "Select All Idle Units [F1]", () => GameHost.Instance?.SelectAllIdleUnits());
		_minimapControls.AddChild(_btnSelectIdle);

		_btnSelectArmy = new Button();
		SetupMinimapButton(_btnSelectArmy, UnicodeIcons.SHIELD, "Select All Army Units [F2]", () => GameHost.Instance?.SelectAllMilitaryUnits());
		_minimapControls.AddChild(_btnSelectArmy);

		var btnHotkeys = new Button();
		SetupMinimapButton(btnHotkeys, UnicodeIcons.KEYBOARD, "Hotkey Reference", () => ToggleHotkeyPanel());
		_minimapControls.AddChild(btnHotkeys);
	}

	private string GetCurrentWeather()
	{
		if (GameHost.Instance == null) return "clear";

		string mapDir = !string.IsNullOrEmpty(GameContext.MapStorageService.CurrentMapDirectory)
			? GameContext.MapStorageService.CurrentMapDirectory
			: MapWorkspaceService.GetActiveWorkspacePath();
		string metaPath = Path.Combine(mapDir, "metadata.json").Replace("\\", "/");
		GameHost.Instance.LoadMapProperties(metaPath);
		GameHost tempQualifier = GameHost.Instance;
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

		if (LobbyManager.Instance != null)
		{
			LobbyManager.Instance.ChatReceived += OnLobbyChatReceived;
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

		_countdownLabel = new Label();
		_countdownLabel.Name = "CountdownLabel";
		_countdownLabel.Text = "";
		_countdownLabel.HorizontalAlignment = HorizontalAlignment.Center;
		_countdownLabel.AddThemeFontSizeOverride("font_size", 14);
		_countdownLabel.AddThemeColorOverride("font_color", UIStyle.ColorGold);
		_countdownPanel.AddChild(_countdownLabel);
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
		_leaderboardPanel.AddChild(lbVBox);

		_leaderboardTitleLabel = new Label();
		_leaderboardTitleLabel.Name = "LeaderboardTitle";
		_leaderboardTitleLabel.Text = TranslationServer.Translate("LEADERBOARD");
		UIStyle.ApplyTitle(_leaderboardTitleLabel, TranslationServer.Translate("LEADERBOARD"), 12);
		_leaderboardTitleLabel.HorizontalAlignment = HorizontalAlignment.Center;
		lbVBox.AddChild(_leaderboardTitleLabel);

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
				UIManager.Instance?.PlayClickSound();
				GameContext.SessionState.IsTestMode = false;
				GameContext.SessionState.ReturningFromTest = true;
				if (LobbyManager.Instance != null)
				{
					LobbyManager.Instance.Disconnect();
				}

				UIManager.Instance?.TransitionTo(GameScreen.MapEditorHUD);
			};
			AddChild(btnBackToEditor);
		}
	}

	private void SetupControllers()
	{
		_resourcePanelController = new ResourcePanel(_resourceContainer);
		_resourcePanelController.InitializeSupplyAndClock(_populationLabel, _clockLabel);

		_camera3D = GameHost.Instance?.MainCamera;
		_minimapPanelController = new MinimapPanel(_minimapFrame, _minimapArea, _cameraIndicator, _camera3D);
		_chatPanelController = new ChatPanel(_chatPanel, _chatInput, _chatLog);
		_leaderboardPanelController = new LeaderboardPanel(_customUIPanel, _countdownPanel, _countdownLabel, _leaderboardPanel, _leaderboardTitleLabel, _leaderboardContent);
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
		if (ReplayPlaybackManager.Instance.IsPlayingReplay)
		{
			_bottomConsole.Visible = false;
			_resourceContainer.Visible = false;
			if (_chatPanel != null) _chatPanel.Visible = false;
		}

		bool isSpectator = LobbyManager.Instance != null && LobbyManager.Instance.LocalPlayer != null && LobbyManager.Instance.LocalPlayer.Team == "Spectator";
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
		var fpsLabel = (GameHost.Instance != null ? GameHost.Instance.MainNode?.GetNodeOrNull<Label>("CanvasLayer/FPS") : null);
		if (fpsLabel != null)
		{
			fpsLabel.Visible = GameSettings.DisplayFps;
			fpsLabel.ZIndex = 100;
		}
	}

	public override void _ExitTree()
	{
		if (Instance == this) Instance = null;
		if (LobbyManager.Instance != null)
		{
			LobbyManager.Instance.ChatReceived -= OnLobbyChatReceived;
		}
	}

	private bool _isGeneratingMinimap = false;
	private bool _minimapNeedsRegen = false;

	public async void RegenerateMinimapBackground()
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
		RegenerateMinimapBackground();
	}

	private void CreateStatsContainer()
	{
		_statsContainer = new HBoxContainer();
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
		statsVBox.SizeFlagsHorizontal = SizeFlags.ExpandFill;
		statsVBox.SizeFlagsVertical = SizeFlags.ShrinkBegin;
		statsScroll.AddChild(statsVBox);

		_statsLabel = new Label();
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
		_itemsBox.SizeFlagsHorizontal = SizeFlags.ExpandFill;
		_itemsBox.Alignment = BoxContainer.AlignmentMode.Center;
		_itemsBox.Visible = false;
		_statsContainer.AddChild(_itemsBox);

		var itemsTitle = new Label();
		itemsTitle.Text = TranslationServer.Translate("ITEMS");
		UIStyle.ApplyTitle(itemsTitle, TranslationServer.Translate("ITEMS"), 12);
		itemsTitle.AddThemeColorOverride("font_color", UIStyle.ColorBronze);
		_itemsBox.AddChild(itemsTitle);

		var itemsHBox = new HBoxContainer();
		itemsHBox.AddThemeConstantOverride("separation", 8);
		_itemsBox.AddChild(itemsHBox);

		var itemAxe = new TextureRect();
		SetupItemIcon(itemAxe, "res://Assets/UI/battle_axe.png", "Battle Axe\n+5 Attack Damage (Equipped)");
		itemsHBox.AddChild(itemAxe);

		var itemShield = new TextureRect();
		SetupItemIcon(itemShield, "res://Assets/UI/battle_shield.png", "Battle Shield\n+3 Armor Block (Equipped)");
		itemsHBox.AddChild(itemShield);


		var potHotkeyLabel = new Label();
		potHotkeyLabel.Name = "HotkeyLabel";
		potHotkeyLabel.Text = TranslationServer.Translate("I");
		potHotkeyLabel.AddThemeFontSizeOverride("font_size", 10);
		potHotkeyLabel.AddThemeColorOverride("font_color", UIStyle.ColorGold);
		potHotkeyLabel.AddThemeColorOverride("font_outline_color", new Color(0f, 0f, 0f, 0.9f));
		potHotkeyLabel.AddThemeConstantOverride("outline_size", 4);
		potHotkeyLabel.SetAnchorsAndOffsetsPreset(LayoutPreset.TopLeft);
		potHotkeyLabel.OffsetLeft = 4;
		potHotkeyLabel.OffsetTop = 3;
		potHotkeyLabel.MouseFilter = MouseFilterEnum.Ignore;

		_productionBox = new VBoxContainer();
		_productionBox.SizeFlagsHorizontal = SizeFlags.ExpandFill;
		_productionBox.Alignment = BoxContainer.AlignmentMode.Center;
		_statsContainer.AddChild(_productionBox);
		_productionBox.Visible = false;

		_productionTitle = new Label();
		_productionTitle.Text = TranslationServer.Translate("PRODUCTION");
		UIStyle.ApplyTitle(_productionTitle, TranslationServer.Translate("PRODUCTION"), 12);
		_productionTitle.AddThemeColorOverride("font_color", UIStyle.ColorBronze);
		_productionBox.AddChild(_productionTitle);

		_productionProgress = new ProgressBar();
		_productionProgress.CustomMinimumSize = new Vector2(120, 16);
		_productionProgress.ShowPercentage = true;
		_productionProgress.AddThemeStyleboxOverride("background", UIStyle.CreateSliderTrack());
		_productionProgress.AddThemeStyleboxOverride("fill", UIStyle.CreateSliderFill());
		_productionBox.AddChild(_productionProgress);

		_productionQueueLabel = new Label();
		_productionQueueLabel.Text = string.Format(TranslationServer.Translate("Queue: {0}"), 0);
		_productionQueueLabel.HorizontalAlignment = HorizontalAlignment.Center;
		_productionQueueLabel.AddThemeFontSizeOverride("font_size", 11);
		_productionQueueLabel.AddThemeColorOverride("font_color", UIStyle.ColorGoldDull);
		_productionBox.AddChild(_productionQueueLabel);

		_queueSlotsContainer = new HBoxContainer();
		_queueSlotsContainer.Alignment = BoxContainer.AlignmentMode.Center;
		_queueSlotsContainer.AddThemeConstantOverride("separation", 6);
		_productionBox.AddChild(_queueSlotsContainer);
	}

	private void SetupSpellButton(Button btn, string iconPath, string spellId, string tooltip)
	{
		btn.Flat = false;
		btn.Text = "";
		btn.ExpandIcon = true;
		btn.Icon = GD.Load<Texture2D>(iconPath);
		btn.TooltipText = tooltip;
		btn.CustomMinimumSize = new Vector2(48, 48);
		btn.ClipContents = true;
		btn.AddThemeConstantOverride("icon_max_width", 42);

		if (tooltip.StartsWith('[') && tooltip.Contains(']'))
		{
			int end = tooltip.IndexOf(']');
			string hotkeyText = tooltip.Substring(1, end - 1);
			var hotkeyLabel = new Label();
			hotkeyLabel.Name = "HotkeyLabel";
			hotkeyLabel.Text = hotkeyText;
			hotkeyLabel.AddThemeFontSizeOverride("font_size", 12);
			hotkeyLabel.AddThemeColorOverride("font_color", UIStyle.ColorGold);
			hotkeyLabel.AddThemeColorOverride("font_outline_color", new Color(0f, 0f, 0f, 0.9f));
			hotkeyLabel.AddThemeConstantOverride("outline_size", 4);
			hotkeyLabel.SetAnchorsAndOffsetsPreset(LayoutPreset.TopLeft);
			hotkeyLabel.OffsetLeft = 4;
			hotkeyLabel.OffsetTop = 3;
			hotkeyLabel.MouseFilter = MouseFilterEnum.Ignore;
			btn.AddChild(hotkeyLabel);
		}

		btn.AddThemeStyleboxOverride("normal", UIStyle.CreateButtonNormal());
		btn.AddThemeStyleboxOverride("hover", UIStyle.CreateButtonHover());
		btn.AddThemeStyleboxOverride("pressed", UIStyle.CreateButtonPressed());
		btn.AddThemeStyleboxOverride("focus", new StyleBoxEmpty());

		btn.Pressed += () =>
		{
			if (GameHost.Instance != null)
			{
				GameHost.Instance.EnterSpellTargeting(spellId);
			}
		};
	}

	private void SetupItemIcon(TextureRect rect, string iconPath, string tooltip)
	{
		rect.Texture = GD.Load<Texture2D>(iconPath);
		rect.ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize;
		rect.StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered;
		rect.CustomMinimumSize = new Vector2(60, 60);
		rect.TooltipText = tooltip;
	}

	public void RefreshUI(List<Unit3D> selectedUnits)
	{
		if (selectedUnits != null)
		{
			bool hasPlayerSelection = Enumerable.Any(selectedUnits, u => !u.IsEnemy);
			bool isSpectator = LobbyManager.Instance?.LocalPlayer?.Team == "Spectator";
			_commandFrame.Visible = !isSpectator && hasPlayerSelection;
		}

		_viewModel.SelectedProp = GameHost.Instance?.SelectedProp;
		_viewModel.UpdateSelectedUnits(selectedUnits);
		_portraitPanelController?.Update(_viewModel);
		_commandPanelController?.Update(_viewModel);
		_inventoryPanelController?.Update(_viewModel);
	}

	private void ApplyThemeStyles()
	{
		_resourceContainer.AddThemeStyleboxOverride("panel", new StyleBoxEmpty());

		if (_resourceBackground != null)
		{
			var topTexture = UIStyle.LoadTextureWithFallback("res://Assets/UI/In-Game_MatchView_PanelTop.png");
			_resourceBackground.Texture = topTexture;

			var topShader = new Shader();
			topShader.Code = @"
shader_type canvas_item;

uniform vec2 u_panel_size = vec2(840.0, 76.0);

void fragment() {
	float x = UV.x * u_panel_size.x;
	float h = u_panel_size.y;
	float s = h / 434.0;
	float w_left = 280.0 * s;
	float w_right = 280.0 * s;

	float tex_x = 0.0;
	if (x < w_left) {
		tex_x = x / s;
	} else if (x > u_panel_size.x - w_right) {
		float rx = x - (u_panel_size.x - w_right);
		tex_x = (2624.0 - 280.0) + (rx / s);
	} else {
		float center_w = max(u_panel_size.x - w_left - w_right, 1.0);
		float t = (x - w_left) / center_w;
		tex_x = 280.0 + t * (2344.0 - 280.0);
	}

	vec2 mapped_uv = vec2(clamp(tex_x / 2624.0, 0.0, 1.0), UV.y);
	COLOR = texture(TEXTURE, mapped_uv);
}
";
			var topMaterial = new ShaderMaterial();
			topMaterial.Shader = topShader;
			topMaterial.SetShaderParameter("u_panel_size", new Vector2(840.0f, 76.0f));
			_resourceBackground.Material = topMaterial;
		}

		if (_consoleBackground != null)
		{
			var texture = UIStyle.LoadTextureWithFallback("res://Assets/UI/In-Game_MatchView_PanelBottom.png");
			if (texture == null)
			{
				texture = UIStyle.LoadTextureWithFallback("res://Assets/UI/In-Game_MatchView_PanelBottom.jpg");
			}
			_consoleBackground.Texture = texture;

			var consoleShader = new Shader();
			consoleShader.Code = @"
shader_type canvas_item;

uniform vec2 u_console_size = vec2(1920.0, 240.0);

void fragment() {
	float x = UV.x * u_console_size.x;
	float h = u_console_size.y;
	float s = h / 433.0;
	float w_left = 935.0 * s;
	float w_right = 912.0 * s;

	float tex_x = 0.0;
	if (x < w_left) {
		tex_x = x / s;
	} else if (x > u_console_size.x - w_right) {
		float rx = x - (u_console_size.x - w_right);
		tex_x = (2752.0 - 912.0) + (rx / s);
	} else {
		float center_w = max(u_console_size.x - w_left - w_right, 1.0);
		float t = (x - w_left) / center_w;
		tex_x = 935.0 + t * (1840.0 - 935.0);
	}

	vec2 mapped_uv = vec2(clamp(tex_x / 2752.0, 0.0, 1.0), UV.y);
	COLOR = texture(TEXTURE, mapped_uv);
}
";
			var consoleMaterial = new ShaderMaterial();
			consoleMaterial.Shader = consoleShader;
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
			("ResourceContainer/HBox/GoldBox/GoldIcon", "res://Assets/UI/gold_coin.png"),
			("ResourceContainer/HBox/WoodBox/WoodIcon", "res://Assets/UI/wood_logs.png"),
			("ResourceContainer/HBox/StoneBox/StoneIcon", "res://Assets/UI/stone.png"),
			("ResourceContainer/HBox/SupplyBox/SupplyIcon", "res://Assets/UI/Supplies.png"),
			("ResourceContainer/HBox/ClockBox/ClockIcon", "res://Assets/UI/clock.png"),
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
		_viewModel.Update(delta);

		UpdateSelectedUnitVisuals();
		UpdateFogTimer(delta);

		_portraitPanelController?.Update(_viewModel);
		_commandPanelController?.Update(_viewModel);
		_resourcePanelController?.Update(_viewModel);
		_leaderboardPanelController?.Update(_viewModel);
		_summaryTablePanelController?.Update(_viewModel);

		if (_connectionWarningLabel != null)
		{
			_connectionWarningLabel.Visible = _viewModel.IsConnectionLost;
		}

		UpdateIdleUnitButton(delta);

		_minimapPanelController?.UpdateMinimapIndicator();

		_controlGroupsUIController?.Update();
	}

	private void UpdateSelectedUnitVisuals()
	{
		if (GameHost.Instance?.SelectedUnits == null) return;

		var selected = GameHost.Instance.SelectedUnits;
		_viewModel.UpdateSelectedUnits(selected);

		// Handle specific visual updates that are not data-bound yet.
		foreach (var u in selected)
		{
			if (u.IsBuilding && !u.IsEnemy && u.UnitId == "castle")
			{
				u.UpdateRallyVisuals();
			}
		}
	}

	private void UpdateFogTimer(double delta)
	{
		_fogUpdateTimer += (float)delta;
		if (_fogUpdateTimer >= 0.1f)
		{
			_fogUpdateTimer = 0f;
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
			UIManager.Instance?.PlayClickSound();
			onClick?.Invoke();
		};
		btn.MouseEntered += () => UIManager.Instance?.PlayHoverSound();
	}

	private void SetupHUDButton(Button btn, string iconPath, string tooltip, Action onClick)
	{
		btn.Flat = false;
		btn.Text = "";
		btn.ExpandIcon = true;
		btn.Icon = GD.Load<Texture2D>(iconPath);
		string trans = TranslationServer.Translate(tooltip);
		btn.TooltipText = string.IsNullOrEmpty(trans) ? tooltip : trans;
		btn.CustomMinimumSize = new Vector2(48, 48);
		btn.SizeFlagsHorizontal = SizeFlags.ExpandFill;
		btn.SizeFlagsVertical = SizeFlags.ExpandFill;
		btn.FocusMode = FocusModeEnum.None;
		btn.ClipContents = true;
		btn.AddThemeConstantOverride("icon_max_width", 42);

		if (tooltip.StartsWith('[') && tooltip.Contains(']'))
		{
			int end = tooltip.IndexOf(']');
			string hotkeyText = tooltip.Substring(1, end - 1);
			var hotkeyLabel = new Label();
			hotkeyLabel.Name = "HotkeyLabel";
			hotkeyLabel.Text = hotkeyText;
			hotkeyLabel.AddThemeFontSizeOverride("font_size", 12);
			hotkeyLabel.AddThemeColorOverride("font_color", UIStyle.ColorGold);
			hotkeyLabel.AddThemeColorOverride("font_outline_color", new Color(0f, 0f, 0f, 0.9f));
			hotkeyLabel.AddThemeConstantOverride("outline_size", 4);
			hotkeyLabel.SetAnchorsAndOffsetsPreset(LayoutPreset.TopLeft);
			hotkeyLabel.OffsetLeft = 4;
			hotkeyLabel.OffsetTop = 3;
			hotkeyLabel.MouseFilter = MouseFilterEnum.Ignore;
			btn.AddChild(hotkeyLabel);
		}

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
			pTexture.Texture = GD.Load<Texture2D>("res://Assets/UI/alliance_flag.png");
			var portraitMaskShader = new Shader();
			portraitMaskShader.Code = @"
shader_type canvas_item;

void fragment() {
	vec2 center = UV - vec2(0.5);
	if (dot(center, center) > 0.25) {
		discard;
	}
	COLOR = texture(TEXTURE, UV);
}
";
			var portraitMaskMaterial = new ShaderMaterial();
			portraitMaskMaterial.Shader = portraitMaskShader;
			pTexture.Material = portraitMaskMaterial;
		}

		GetNode<Label>("BottomConsole/HBox/PortraitFrame/VBox/UnitName").Text = TranslationServer.Translate("No Selection");
	}

	private void CenterCameraOnSelectedUnit()
	{
		var selected = GameHost.Instance?.SelectedUnits;
		if (selected != null && selected.Count > 0)
		{
			int idx = GameHost.Instance != null ? GameHost.Instance.CycleSelectionIndex : 0;
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
		if (GameHost.Instance == null) return;
		var selectedUnits = GameHost.Instance.SelectedUnits;
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
				GameHost.Instance.DeselectUnit(u);
			}
		}
		else if (shiftPressed)
		{
			GameHost.Instance.DeselectUnit(clickedUnit);
		}
		else
		{
			GameHost.Instance.SelectOnlyUnit(clickedUnit);
			CenterCameraOnUnit(clickedUnit);
		}

		RefreshUI(GameHost.Instance.SelectedUnits);
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
		if (GameHost.Instance == null || GameHost.Instance.SelectedUnits.Count <= 1) return false;

		bool reverse = Input.IsKeyPressed(Key.Shift);
		GameHost.Instance.CycleSelectionFocus(reverse);
		RefreshUI(GameHost.Instance.SelectedUnits);
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

		_chatPanelController?.ShowChatInput(keyEvent.ShiftPressed);
		GetViewport().SetInputAsHandled();
		return true;
	}

	private bool HandlePauseKey(InputEventKey keyEvent)
	{
		if (keyEvent.Keycode != Key.P && keyEvent.Keycode != Key.Pause) return false;
		if (IsChatActive || GameHost.Instance == null) return false;

		GameHost.Instance.TogglePauseRequest();
		GetViewport().SetInputAsHandled();
		return true;
	}

	private bool HandleTildeKey(InputEventKey keyEvent)
	{
		if (keyEvent.Keycode != Key.Quoteleft || keyEvent.Echo) return false;
		if (IsChatActive || !WasmConsoleWindow.IsSinglePlayerOrTestMode()) return false;

		WasmConsoleWindow.Instance.ToggleVisibility();
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

	private void OnLobbyChatReceived(string senderName, string message, bool alliesOnly)
	{
		_chatPanelController?.OnLobbyChatReceived(senderName, message, alliesOnly);
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
		bgTexture.Texture = UIStyle.LoadTextureWithFallback("res://Assets/UI/In-Game_MatchView_HotkeyReference.png");
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
					("Space", "Center Camera on Castle"),
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
					("Esc", "Clear Selection / Menu"),
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
				groupSpacer.CustomMinimumSize = new Vector2(0, 6);
				vbox.AddChild(groupSpacer);
			}

			var catHeader = new PanelContainer();
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
				row.AddThemeConstantOverride("separation", 6);
				row.SizeFlagsHorizontal = SizeFlags.ExpandFill;

				var keyBadge = new PanelContainer();
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
	}

	public void StopCountdownTimer()
	{
		_viewModel.CountdownActive = false;
	}

	public void UpdateCountdownLabel(string labelText)
	{
		_viewModel.CountdownText = labelText;
	}

	public void SetLeaderboardVisible(string title, bool visible)
	{
		_viewModel.LeaderboardTitle = title;
		_viewModel.LeaderboardVisible = visible;
	}

	public void ClearLeaderboard()
	{
		_viewModel.LeaderboardValues.Clear();
	}

	public void SetLeaderboardValue(string label, string value)
	{
		_viewModel.LeaderboardValues[label] = value;
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
		GameHost tempQualifier = GameHost.Instance;
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
			GameContext.EnvironmentService.ApplyWeatherVisuals(GameHost.Instance, weather);
		}

		ApplyWeatherEffects(weather);
	}

	public void ApplyWeatherEffects(string weather)
	{
		GameHost tempQualifier = GameHost.Instance;
		if ((tempQualifier != null ? GameContext.EnvironmentService : null) != null)
		{
			GameContext.EnvironmentService.ApplyWeatherVisuals(GameHost.Instance, weather);
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

		var selectedEntities = GameHost.Instance.SelectedUnits != null
			? Enumerable.ToList(Enumerable.Select(GameHost.Instance.SelectedUnits, u => u.Entity))
			: new List<Entity>();

		bool isMultiplayer = LobbyManager.Instance != null && !LobbyManager.Instance.IsSinglePlayer
		                                                   && Multiplayer.MultiplayerPeer != null && !(Multiplayer.MultiplayerPeer is OfflineMultiplayerPeer);

		Entity localPlayerEntity = GameHost.Instance.GetPlayerEntityForPlayerIndex(LobbyManager.Instance?.LocalPlayer?.Slot ?? 1);
		var (result, _) = GameContext.CheatService.TryTriggerCheat(
			text, isMultiplayer, localPlayerEntity, ECSContext.DefinitionManager, selectedEntities);

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
				RefreshUI(GameHost.Instance.SelectedUnits);
				break;
			case CheatResult.AbsoluteUnit:
				foreach (var unit in GameHost.Instance.SelectedUnits)
				{
					if (GodotObject.IsInstanceValid(unit)) unit.Scale = new Vector3(3f, 3f, 3f);
				}

				RefreshUI(GameHost.Instance.SelectedUnits);
				break;
			case CheatResult.EzClap:
				UIManager.Instance?.PlayClickSound();
				UIManager.Instance?.TransitionTo(GameScreen.GameOver, true);
				break;
			case CheatResult.NoCap:
				RegenerateMinimapBackground();
				QueueMinimapRedraw();
				break;
		}
	}

	public bool HandleCommandCardHotkey(Key keycode)
	{
		if (_commandPanelController == null) return false;
		return _commandPanelController.HandleHotkey(keycode);
	}

	private void SetupCommandCard()
	{
		SetupHUDButton(_btnMove, "res://Assets/UI/move_speed.png", TranslationServer.Translate("[M] Move / Right-Click Ground"), () => GameHost.Instance?.EnterCommandTargeting("move"));
		SetupHUDButton(_btnStop, "res://Assets/UI/cancel_button_2.png", TranslationServer.Translate("[S] Stop Selected Units"), () =>
		{
			ShowFeedbackText(TranslationServer.Translate("Command: Stop Current Action"), new Color(0.9f, 0.2f, 0.2f));
			GameHost.Instance?.StopSelectedUnits();
		});
		SetupHUDButton(_btnHold, "res://Assets/UI/magic_upgrade_arrow.png", TranslationServer.Translate("[H] Hold Position — Unit stays put and attacks in place"), () =>
		{
			ShowFeedbackText(TranslationServer.Translate("Command: Hold Position"), new Color(0.9f, 0.8f, 0.1f));
			GameHost.Instance?.HoldSelectedUnits();
		});
		SetupHUDButton(_btnAttack, "res://Assets/UI/battle_axe.png", TranslationServer.Translate("[A] Attack / Attack-Move — Click enemy to attack, click ground to attack-move"), () => GameHost.Instance?.EnterCommandTargeting("attack"));
		SetupHUDButton(_btnPatrol, "res://Assets/UI/patrol.jpg", TranslationServer.Translate("[P] Patrol — Unit patrols between current position and target, engaging enemies"), () => GameHost.Instance?.EnterCommandTargeting("patrol"));
		SetupHUDButton(_btnBuild, "res://Assets/UI/golden_hammers.png", TranslationServer.Translate("[B] Build Structure"), () => EnterBuildSubMenu());

		_btnCancelBuild = new Button();
		SetupHUDButton(_btnCancelBuild, "res://Assets/UI/cancel_button_2.png", TranslationServer.Translate("[Esc] Cancel"), () => ExitBuildSubMenu());

		_btnSetRally = new Button();
		SetupHUDButton(_btnSetRally, "res://Assets/UI/alliance_flag.png", TranslationServer.Translate("[Y] Set Rally Point — Set location where new units will walk"), () => GameHost.Instance?.EnterCommandTargeting("rally"));
	}

	private void CreateSpectatorPerspectiveUI()
	{
		var specPanel = new PanelContainer();
		specPanel.Name = "SpectatorPanel";
		specPanel.AddThemeStyleboxOverride("panel", UIStyle.CreateStonePanel());

		var specVBox = new VBoxContainer();
		specPanel.AddChild(specVBox);

		var title = new Label();
		title.Text = TranslationServer.Translate("SPECTATOR MODE");
		UIStyle.ApplyTitle(title, TranslationServer.Translate("SPECTATOR MODE"), 12);
		title.HorizontalAlignment = HorizontalAlignment.Center;
		specVBox.AddChild(title);

		var specHBox = new HBoxContainer();
		specHBox.Alignment = BoxContainer.AlignmentMode.Center;
		specHBox.AddThemeConstantOverride("separation", 10);
		specVBox.AddChild(specHBox);

		var btnAll = new Button();
		btnAll.Text = TranslationServer.Translate("ALL");
		btnAll.Pressed += () => { LiveSpectatorPerspective = -1; };
		specHBox.AddChild(btnAll);

		var btnBlue = new Button();
		btnBlue.Text = TranslationServer.Translate("BLUE");
		btnBlue.Pressed += () => { LiveSpectatorPerspective = 0; };
		specHBox.AddChild(btnBlue);

		var btnRed = new Button();
		btnRed.Text = TranslationServer.Translate("RED");
		btnRed.Pressed += () => { LiveSpectatorPerspective = 1; };
		specHBox.AddChild(btnRed);

		var btnGreen = new Button();
		btnGreen.Text = TranslationServer.Translate("GREEN");
		btnGreen.Pressed += () => { LiveSpectatorPerspective = 2; };
		specHBox.AddChild(btnGreen);

		var btnYellow = new Button();
		btnYellow.Text = TranslationServer.Translate("YELLOW");
		btnYellow.Pressed += () => { LiveSpectatorPerspective = 3; };
		specHBox.AddChild(btnYellow);

		AddChild(specPanel);
	}

	private PanelContainer _pausePanel;
	private VBoxContainer _pausePlayerListContainer;
	private Label _pauseTitleLabel;
	private CheckButton _pauseReadyCheck;
	private Button _pauseForceResumeBtn;

	public void UpdatePauseUI()
	{
		if (GameHost.Instance == null) return;
		if (_pausePanel == null) BuildPausePanel();

		_pausePanel.Visible = GameContext.NetworkService.IsPaused;
		if (!GameContext.NetworkService.IsPaused) return;

		UpdatePauseTitle();

		ClearPausePlayerList();

		GetLocalPlayerInfo(out bool isLocalHost, out int localPeerId);

		UpdatePausePlayerList(isLocalHost);
		UpdatePauseControls(localPeerId, isLocalHost);
	}

	private void ClearPausePlayerList()
	{
		foreach (var child in _pausePlayerListContainer.GetChildren()) child.QueueFree();
	}

	private void GetLocalPlayerInfo(out bool isLocalHost, out int localPeerId)
	{
		isLocalHost = false;
		localPeerId = 1;

		var lobby = LobbyManager.Instance;
		if (lobby == null) return;

		var localPlayer = lobby.LocalPlayer;
		if (localPlayer == null) return;

		isLocalHost = localPlayer.IsHost;
		localPeerId = localPlayer.PeerId;
	}

	private void UpdatePauseTitle()
	{
		_pauseTitleLabel.Text = GameContext.NetworkService.ResumeCountdownSeconds >= 0
			? $"Resuming in {GameContext.NetworkService.ResumeCountdownSeconds}s..."
			: "Game Paused";
	}

	private void UpdatePausePlayerList(bool isLocalHost)
	{
		if (LobbyManager.Instance == null) return;

		foreach (var player in LobbyManager.Instance.PlayerList)
		{
			if (player.Team == "Spectator") continue;

			var row = new HBoxContainer();
			row.AddThemeConstantOverride("separation", 15);

			var nameLabel = new Label { Text = player.Name, CustomMinimumSize = new Vector2(150, 0) };
			row.AddChild(nameLabel);

			GameHost.Instance.GetPlayerReadyState(player.PeerId, out bool ready);
			var readyLabel = new Label
			{
				Text = ready ? "READY" : "NOT READY",
				CustomMinimumSize = new Vector2(100, 0)
			};
			readyLabel.AddThemeColorOverride("font_color", ready ? new Color(0.2f, 0.9f, 0.3f) : new Color(0.9f, 0.3f, 0.2f));
			row.AddChild(readyLabel);

			var disallowCheck = new CheckBox { Text = "Disallow Pause", Disabled = !isLocalHost || player.PeerId == 1 };
			GameHost.Instance.GetPlayerDisallowPause(player.PeerId, out bool disallowed);
			disallowCheck.ButtonPressed = disallowed;
			disallowCheck.Toggled += (buttonPressed) => GameHost.Instance.RequestSetDisallowPause(player.PeerId, buttonPressed);
			row.AddChild(disallowCheck);

			_pausePlayerListContainer.AddChild(row);
		}
	}

	private void UpdatePauseControls(int localPeerId, bool isLocalHost)
	{
		GameHost.Instance.GetPlayerReadyState(localPeerId, out bool localReady);
		_pauseReadyCheck.ButtonPressed = localReady;
		_pauseReadyCheck.Disabled = GameContext.NetworkService.ResumeCountdownSeconds >= 0;

		_pauseForceResumeBtn.Visible = isLocalHost;
		_pauseForceResumeBtn.Disabled = GameContext.NetworkService.ResumeCountdownSeconds >= 0;
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
		mainVBox.AddThemeConstantOverride("separation", 15);
		_pausePanel.AddChild(mainVBox);

		_pauseTitleLabel = new Label();
		_pauseTitleLabel.Text = "Game Paused";
		_pauseTitleLabel.HorizontalAlignment = HorizontalAlignment.Center;
		_pauseTitleLabel.AddThemeFontSizeOverride("font_size", 24);
		mainVBox.AddChild(_pauseTitleLabel);

		var scroll = new ScrollContainer();
		scroll.CustomMinimumSize = new Vector2(450, 150);
		mainVBox.AddChild(scroll);

		_pausePlayerListContainer = new VBoxContainer();
		_pausePlayerListContainer.AddThemeConstantOverride("separation", 10);
		scroll.AddChild(_pausePlayerListContainer);

		var actionHBox = new HBoxContainer();
		actionHBox.AddThemeConstantOverride("separation", 20);
		actionHBox.Alignment = BoxContainer.AlignmentMode.Center;
		mainVBox.AddChild(actionHBox);

		_pauseReadyCheck = new CheckButton();
		_pauseReadyCheck.Text = "Ready to Resume";
		_pauseReadyCheck.Toggled += (buttonPressed) =>
		{
			int localPeerId = LobbyManager.Instance != null && LobbyManager.Instance.LocalPlayer != null ? LobbyManager.Instance.LocalPlayer.PeerId : 1;
			GameHost.Instance?.RequestToggleReady(localPeerId, buttonPressed);
		};
		actionHBox.AddChild(_pauseReadyCheck);

		_pauseForceResumeBtn = new Button();
		_pauseForceResumeBtn.Text = "Force Resume";
		_pauseForceResumeBtn.Pressed += OnForceResumePressed;
		actionHBox.AddChild(_pauseForceResumeBtn);

		AddChild(_pausePanel);
	}

	private void OnForceResumePressed()
	{
		GameHost.Instance?.RequestForceResume();
	}

	private string GetBuildOptionTooltip(string unitId, string hotkey)
	{
		string name = unitId.ToUpper();
		float gold = 0, wood = 0, stone = 0;
		if (GameHost.TryGetUnitOrBuildingMetadata(unitId, out var meta))
		{
			name = meta.Name;
			gold = meta.CostGold;
			wood = meta.CostWood;
			stone = meta.CostStone;
		}

		string costStr = $"Cost: {gold} Gold";
		if (wood > 0) costStr += $", {wood} Wood";
		if (stone > 0) costStr += $", {stone} Stone";

		string tooltipFormat = !string.IsNullOrEmpty(hotkey)
			? "[{0}] Build {1} ({2})"
			: "Build {0} ({1})";

		return string.Format(TranslationServer.Translate(tooltipFormat), hotkey, name, costStr);
	}

	private string GetTrainOptionTooltip(string unitId, string hotkey)
	{
		string name = unitId.ToUpper();
		float gold = 0, wood = 0, stone = 0;
		int pop = 0;
		string desc = "";
		if (GameHost.TryGetUnitOrBuildingMetadata(unitId, out var meta))
		{
			name = meta.Name;
			gold = meta.CostGold;
			wood = meta.CostWood;
			stone = meta.CostStone;
			pop = meta.PopCost;
			desc = meta.Description;
		}

		string costStr = $"Cost: {gold} Gold";
		if (wood > 0) costStr += $", {wood} Wood";
		if (stone > 0) costStr += $", {stone} Stone";
		if (pop > 0) costStr += $", {pop} Pop";

		string tooltipFormat = !string.IsNullOrEmpty(hotkey)
			? (string.IsNullOrEmpty(desc) ? "[{0}] Train {1} ({2})" : "[{0}] Train {1} ({2}) — {3}")
			: (string.IsNullOrEmpty(desc) ? "Train {0} ({1})" : "Train {0} ({1}) — {2}");

		return string.IsNullOrEmpty(desc)
			? string.Format(TranslationServer.Translate(tooltipFormat), hotkey, name, costStr)
			: string.Format(TranslationServer.Translate(tooltipFormat), hotkey, name, costStr, desc);
	}
}
