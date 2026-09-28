using Arch.Core;
using DotRecast.Core.Numerics;
using DotRecast.Detour;
using Godot;
using Realm.Ecs.Common;
using Realm.Ecs.Components.Combat;
using Realm.Ecs.Components.Core;
using Realm.Ecs.Components.Meta;
using Realm.Ecs.Components.Movement;
using Realm.Ecs.Components.Resources;
using Realm.Ecs.Components.Terrain;
using Realm.Ecs.Services;
using Realm.Godot.ReplaySystem;
using Realm.Godot.Services;
using System;
using System.Collections.Generic;
using Vector3 = Godot.Vector3;

public partial class InGameHUD : Control
{
	public static InGameHUD Instance { get; private set; }

	private InGameHUDViewModel _viewModel = new();

	public float Gold { get => _viewModel.Gold; set => _viewModel.Gold = value; }
	public float Wood { get => _viewModel.Wood; set => _viewModel.Wood = value; }
	public float Stone { get => _viewModel.Stone; set => _viewModel.Stone = value; }
	public float ResourceGatherMultiplier { get => _viewModel.ResourceGatherMultiplier; set => _viewModel.ResourceGatherMultiplier = value; }

	private PanelContainer _resourceContainer;
	private PanelContainer _bottomConsole;
	private PanelContainer _minimapFrame;
	private PanelContainer _portraitFrame;
	private PanelContainer _selectionFrame;
	private PanelContainer _commandFrame;
	private Panel _devPanel;

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
			if (GameHost.Instance != null && GameHost.Instance.EcsWorld != null)
			{
				var worldEntity = GameHost.Instance.WorldEntity;
				if (worldEntity != Entity.Null && GameHost.Instance.EcsWorld.IsAlive(worldEntity) && GameHost.Instance.EcsWorld.Has<ShroudState>(worldEntity))
				{
					return GameHost.Instance.EcsWorld.Get<ShroudState>(worldEntity).ShroudGrid;
				}

				Entity fallback = Entity.Null;
				var query = Realm.Ecs.Common.QueryCache.AllShroudStateQuery;
				GameHost.Instance.EcsWorld.Query(in query, ent => fallback = ent);
				if (fallback != Entity.Null && GameHost.Instance.EcsWorld.Has<ShroudState>(fallback))
				{
					return GameHost.Instance.EcsWorld.Get<ShroudState>(fallback).ShroudGrid;
				}
			}
			return _emptyShroudGrid;
		}
		set
		{
			if (GameHost.Instance != null && GameHost.Instance.EcsWorld != null)
			{
				var worldEntity = GameHost.Instance.WorldEntity;
				if (worldEntity != Entity.Null && GameHost.Instance.EcsWorld.IsAlive(worldEntity) && GameHost.Instance.EcsWorld.Has<ShroudState>(worldEntity))
				{
					ref var state = ref GameHost.Instance.EcsWorld.Get<ShroudState>(worldEntity);
					state.ShroudGrid = value;
					return;
				}

				Entity fallback = Entity.Null;
				var query = Realm.Ecs.Common.QueryCache.AllShroudStateQuery;
				GameHost.Instance.EcsWorld.Query(in query, ent => fallback = ent);
				if (fallback != Entity.Null && GameHost.Instance.EcsWorld.Has<ShroudState>(fallback))
				{
					ref var state = ref GameHost.Instance.EcsWorld.Get<ShroudState>(fallback);
					state.ShroudGrid = value;
				}
			}
		}
	}
	public byte[,] FogGrid { get => ShroudGrid; set => ShroudGrid = value; }
	private byte[,] _fogGrid { get => ShroudGrid; set => ShroudGrid = value; }

	public string ShroudType
	{
		get
		{
			if (GameHost.Instance != null && GameHost.Instance.EcsWorld != null)
			{
				var worldEntity = GameHost.Instance.WorldEntity;
				if (worldEntity != Entity.Null && GameHost.Instance.EcsWorld.IsAlive(worldEntity) && GameHost.Instance.EcsWorld.Has<ShroudState>(worldEntity))
				{
					return GameHost.Instance.EcsWorld.Get<ShroudState>(worldEntity).ShroudType;
				}

				Entity fallback = Entity.Null;
				var query = Realm.Ecs.Common.QueryCache.AllShroudStateQuery;
				GameHost.Instance.EcsWorld.Query(in query, ent => fallback = ent);
				if (fallback != Entity.Null && GameHost.Instance.EcsWorld.Has<ShroudState>(fallback))
				{
					return GameHost.Instance.EcsWorld.Get<ShroudState>(fallback).ShroudType;
				}
			}
			return "VisionShroud";
		}
		set
		{
			if (GameHost.Instance != null && GameHost.Instance.EcsWorld != null)
			{
				var worldEntity = GameHost.Instance.WorldEntity;
				if (worldEntity != Entity.Null && GameHost.Instance.EcsWorld.IsAlive(worldEntity) && GameHost.Instance.EcsWorld.Has<ShroudState>(worldEntity))
				{
					ref var state = ref GameHost.Instance.EcsWorld.Get<ShroudState>(worldEntity);
					state.ShroudType = value;
					return;
				}

				Entity fallback = Entity.Null;
				var query = Realm.Ecs.Common.QueryCache.AllShroudStateQuery;
				GameHost.Instance.EcsWorld.Query(in query, ent => fallback = ent);
				if (fallback != Entity.Null && GameHost.Instance.EcsWorld.Has<ShroudState>(fallback))
				{
					ref var state = ref GameHost.Instance.EcsWorld.Get<ShroudState>(fallback);
					state.ShroudType = value;
				}
			}
		}
	}

	private string _currentWeather
	{
		get => GameHost.Instance?.EnvironmentService?.GetCurrentWeather() ?? "clear";
		set => GameHost.Instance?.EnvironmentService?.SetCurrentWeather(value);
	}

	private float _baseFogDensity
	{
		get => GameHost.Instance?.EnvironmentService?.GetBaseFogDensity() ?? 0f;
		set => GameHost.Instance?.EnvironmentService?.SetBaseFogDensity(value);
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

	private Button _btnVictory;
	private Button _btnDefeat;

	private Label _feedbackLabel;
	private Label _connectionWarningLabel;
	private Control _minimapArea;
	private Control _cameraIndicator;

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
		get => GameHost.Instance?.SpectatorService?.GetSpectatorPerspective() ?? -1;
		set => GameHost.Instance?.SpectatorService?.SetSpectatorPerspective(value);
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

		_resourceContainer = GetNode<PanelContainer>("ResourceContainer");
		_bottomConsole = GetNode<PanelContainer>("BottomConsole");
		_minimapFrame = GetNode<PanelContainer>("BottomConsole/HBox/MinimapFrame");
		_portraitFrame = GetNode<PanelContainer>("BottomConsole/HBox/PortraitFrame");
		_selectionFrame = GetNode<PanelContainer>("BottomConsole/HBox/SelectionFrame");
		_commandFrame = GetNode<PanelContainer>("BottomConsole/HBox/CommandFrame");
		_devPanel = GetNode<Panel>("DevPanel");

		_goldLabel = GetNode<Label>("ResourceContainer/HBox/GoldBox/GoldLabel");
		_woodLabel = GetNode<Label>("ResourceContainer/HBox/WoodBox/WoodLabel");
		_stoneLabel = GetNode<Label>("ResourceContainer/HBox/StoneBox/StoneLabel");

		var resHBox = GetNode<HBoxContainer>("ResourceContainer/HBox");
		var popBox = new VBoxContainer();
		popBox.AddThemeConstantOverride("separation", 2);
		resHBox.AddChild(popBox);
		var popTitleLbl = new Label();
		popTitleLbl.Text = TranslationServer.Translate("SUPPLY");
		popTitleLbl.AddThemeFontSizeOverride("font_size", 11);
		popTitleLbl.AddThemeColorOverride("font_color", UIStyle.ColorBronze);
		popBox.AddChild(popTitleLbl);
		_populationLabel = new Label();
		_populationLabel.Text = "0 / 20";
		_populationLabel.AddThemeFontSizeOverride("font_size", 18);
		_populationLabel.AddThemeColorOverride("font_color", UIStyle.ColorGoldDull);
		popBox.AddChild(_populationLabel);

		var clockBox = new VBoxContainer();
		clockBox.AddThemeConstantOverride("separation", 2);
		resHBox.AddChild(clockBox);
		var clockTitleLbl = new Label();
		clockTitleLbl.Text = TranslationServer.Translate("TIME");
		clockTitleLbl.AddThemeFontSizeOverride("font_size", 11);
		clockTitleLbl.AddThemeColorOverride("font_color", UIStyle.ColorBronze);
		clockBox.AddChild(clockTitleLbl);
		_clockLabel = new Label();
		_clockLabel.Text = "0:00";
		_clockLabel.AddThemeFontSizeOverride("font_size", 18);
		_clockLabel.AddThemeColorOverride("font_color", UIStyle.ColorGoldDull);
		clockBox.AddChild(_clockLabel);

		_btnSettings = new Button();
		_btnSettings.Name = "BtnSettings";
		_btnSettings.CustomMinimumSize = new Vector2(32, 32);
		_btnSettings.SizeFlagsVertical = SizeFlags.ShrinkCenter;
		_btnSettings.FocusMode = FocusModeEnum.None;
		_btnSettings.ExpandIcon = true;
		_btnSettings.Icon = GD.Load<Texture2D>("res://Assets/UI/gear_icon.png");
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
		resHBox.AddChild(_btnSettings);

		_unitsContainer = GetNode<HBoxContainer>("BottomConsole/HBox/SelectionFrame/UnitsContainer");

		foreach (Node child in _unitsContainer.GetChildren())
			_unitsContainer.RemoveChild(child);
		_unitButtons.Clear();

		for (int i = 0; i < 12; i++)
		{
			var btn = new Button();
			btn.Name = $"Unit{i + 1}";
			btn.CustomMinimumSize = new Vector2(42, 42);
			btn.SizeFlagsHorizontal = SizeFlags.ExpandFill;
			btn.ExpandIcon = true;
			btn.FocusMode = FocusModeEnum.None;
			btn.AddThemeStyleboxOverride("normal", UIStyle.CreateButtonNormal());
			btn.AddThemeStyleboxOverride("hover",   UIStyle.CreateButtonHover());
			btn.AddThemeStyleboxOverride("pressed", UIStyle.CreateButtonPressed());
			btn.AddThemeStyleboxOverride("focus",   new StyleBoxEmpty());

			var hpBar = new ProgressBar();
			hpBar.Name = "HealthBar";
			hpBar.CustomMinimumSize = new Vector2(50, 5);
			hpBar.ShowPercentage = false;
			hpBar.SetAnchorsAndOffsetsPreset(LayoutPreset.BottomWide);
			hpBar.OffsetTop = -6;
			hpBar.AddThemeStyleboxOverride("background", UIStyle.CreateSliderTrack());
			hpBar.AddThemeStyleboxOverride("fill",       UIStyle.CreateSliderFill());
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
		_btnAttack.CustomMinimumSize = new Vector2(44, 44);
		_btnAttack.SizeFlagsHorizontal = SizeFlags.ExpandFill;
		_btnAttack.SizeFlagsVertical = SizeFlags.ExpandFill;
		_btnAttack.FocusMode = FocusModeEnum.None;

		_btnPatrol = new Button();
		_btnPatrol.Name = "BtnPatrol";
		_btnPatrol.CustomMinimumSize = new Vector2(44, 44);
		_btnPatrol.SizeFlagsHorizontal = SizeFlags.ExpandFill;
		_btnPatrol.SizeFlagsVertical = SizeFlags.ExpandFill;
		_btnPatrol.FocusMode = FocusModeEnum.None;

		_btnVictory = GetNode<Button>("DevPanel/BtnVictory");
		_btnDefeat = GetNode<Button>("DevPanel/BtnDefeat");
		_feedbackLabel = GetNode<Label>("FeedbackLabel");

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

		_minimapArea = GetNode<Control>("BottomConsole/HBox/MinimapFrame/MinimapArea");
		_cameraIndicator = GetNode<Control>("BottomConsole/HBox/MinimapFrame/MinimapArea/Indicator");

		_minimapControls = new VBoxContainer();
		_minimapControls.SizeFlagsHorizontal = SizeFlags.ShrinkEnd;
		_minimapControls.SizeFlagsVertical = SizeFlags.ShrinkCenter;
		_minimapControls.AddThemeConstantOverride("separation", 3);
		_minimapControls.CustomMinimumSize = new Vector2(40, 0);
		var minimapHBox = GetNode<HBoxContainer>("BottomConsole/HBox");
		minimapHBox.AddChild(_minimapControls);
		minimapHBox.MoveChild(_minimapControls, 1);

		_btnZoom = new Button();
		SetupMinimapButton(_btnZoom, "res://Assets/UI/search_icon_clean.png", "Toggle Camera Zoom (Near / Mid / Far) [Z]", () => GameHost.Instance?.CycleCameraZoom());
		_minimapControls.AddChild(_btnZoom);

		_btnToggleTerrain = new Button();
		SetupMinimapButton(_btnToggleTerrain, "res://Assets/UI/game_menu.png", "Toggle Terrain Overlay (Radar Mode) [F4]", () => ToggleMinimapTerrain());
		_minimapControls.AddChild(_btnToggleTerrain);

		_btnPing = new Button();
		SetupMinimapButton(_btnPing, "res://Assets/UI/magic_upgrade_arrow.png", "Send Alert Ping on Map [Alt+G]", () => 
		{
			if (GameHost.Instance != null)
			{
				GameHost.Instance.ActivePingMode = true;
				ShowFeedbackText("Ping Mode: Click Minimap or Ground to ping", new Color(1.0f, 0.1f, 0.2f));
			}
		});
		_minimapControls.AddChild(_btnPing);

		_btnCenter = new Button();
		SetupMinimapButton(_btnCenter, "res://Assets/UI/alliance_flag.png", "Center Camera on Castle [Space]", () => GameHost.Instance?.CenterCameraOnCastle());
		_minimapControls.AddChild(_btnCenter);

		_btnSelectIdle = new Button();
		SetupMinimapButton(_btnSelectIdle, "res://Assets/UI/unit_placeholder.png", "Select All Idle Units [F1]", () => GameHost.Instance?.SelectAllIdleUnits());
		_minimapControls.AddChild(_btnSelectIdle);

		_btnSelectArmy = new Button();
		SetupMinimapButton(_btnSelectArmy, "res://Assets/UI/heavy_knight.png", "Select All Army Units [F2]", () => GameHost.Instance?.SelectAllMilitaryUnits());
		_minimapControls.AddChild(_btnSelectArmy);

		var btnHotkeys = new Button();
		SetupMinimapButton(btnHotkeys, "res://Assets/UI/game_menu.png", "Hotkey Reference", () => ToggleHotkeyPanel());
		_minimapControls.AddChild(btnHotkeys);

		string currentWeather = "clear";
		if (GameHost.Instance != null)
		{
			string mapDir = !string.IsNullOrEmpty(GameHost.Instance.CurrentMapDirectory)
				? GameHost.Instance.CurrentMapDirectory
				: MapWorkspaceService.GetActiveWorkspacePath();
			string metaPath = System.IO.Path.Combine(mapDir, "metadata.json").Replace("\\", "/");
			GameHost.Instance.LoadMapProperties(metaPath);
			currentWeather = GameHost.Instance?.EnvironmentService?.GetCurrentWeather() ?? "clear";
		}

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
		_chatLog.Text = $"[color=#ffd700]{TranslationServer.Translate("Chat log initialized. Press Enter to type a message/cheat.")}[/color]\n";

		if (LobbyManager.Instance != null)
		{
			LobbyManager.Instance.ChatReceived += OnLobbyChatReceived;
		}

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
		_customUIPanel.GrowHorizontal = Control.GrowDirection.Begin;
		_customUIPanel.OffsetLeft = -440;
		_customUIPanel.OffsetRight = -20;
		_customUIPanel.OffsetTop = 20;
		_customUIPanel.CustomMinimumSize = new Vector2(420, 0);
		_customUIPanel.AddThemeConstantOverride("separation", 10);
		AddChild(_customUIPanel);

		_countdownPanel = new PanelContainer();
		_countdownPanel.Name = "CountdownPanel";
		_countdownPanel.Visible = false;
		_countdownPanel.SizeFlagsHorizontal = Control.SizeFlags.ShrinkEnd;
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

		_leaderboardPanel = new PanelContainer();
		_leaderboardPanel.Name = "LeaderboardPanel";
		_leaderboardPanel.Visible = false;
		_leaderboardPanel.SizeFlagsHorizontal = Control.SizeFlags.ShrinkEnd;
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

		_summaryPanel = new PanelContainer();
		_summaryPanel.Name = "SummaryTablePanel";
		_summaryPanel.Visible = false;
		_summaryPanel.SizeFlagsHorizontal = Control.SizeFlags.ShrinkEnd;
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

		CreateStatsContainer();
		ApplyThemeStyles();
		SetupCommandCard();
		SetupDevPanel();
		SetupPortrait();
		ApplyWeatherEffects(currentWeather);

		_feedbackLabel.Modulate = new Color(1, 1, 1, 0);
		Input.MouseMode = Input.MouseModeEnum.Visible;
		MouseFilter = MouseFilterEnum.Ignore;

		if (MapEditorHUD.IsTestMode)
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
				MapEditorHUD.IsTestMode = false;
				MapEditorHUD.ReturningFromTest = true;
				if (LobbyManager.Instance != null)
				{
					LobbyManager.Instance.Disconnect();
				}
				UIManager.Instance?.TransitionTo(GameScreen.MapEditorHUD);
			};
			AddChild(btnBackToEditor);
		}

		Resized += OnHUDResized;
		ApplyHUDScale();
		UpdateFPSVisibility();
		BuildHotkeyReferencePanel();

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
		GenerateDynamicMinimap();

		if (ReplayPlaybackManager.Instance.IsPlayingReplay)
		{
			_bottomConsole.Visible = false;
			_resourceContainer.Visible = false;
			if (_devPanel != null) _devPanel.Visible = false;
			if (_chatPanel != null) _chatPanel.Visible = false;
		}

		bool isSpectator = LobbyManager.Instance != null && LobbyManager.Instance.LocalPlayer != null && LobbyManager.Instance.LocalPlayer.Team == "Spectator";
		if (isSpectator)
		{
			_resourceContainer.Visible = false;
			if (_devPanel != null) _devPanel.Visible = false;
			CreateSpectatorPerspectiveUI();
		}
	}

	private void OnHUDResized()
	{
		ApplyHUDScale();
		var specPanel = GetNodeOrNull<PanelContainer>("SpectatorPanel");
		if (specPanel != null)
		{
			specPanel.Position = new Vector2((GetViewportRect().Size.X - 300) / 2.0f, 10);
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
			_bottomConsole.PivotOffset = new Vector2(_bottomConsole.Size.X / 2f, _bottomConsole.Size.Y);
			_bottomConsole.Scale = new Vector2(s, s);
		}

		if (_chatPanel != null)
		{
			_chatPanel.PivotOffset = new Vector2(0f, _chatPanel.Size.Y);
			_chatPanel.Scale = new Vector2(s, s);
		}

		if (_devPanel != null)
		{
			_devPanel.PivotOffset = Vector2.Zero;
			_devPanel.Scale = new Vector2(s, s);
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

				var shroudMesh = GameHost.Instance?.MainNode?.GetNodeOrNull<MeshInstance3D>("3DShroudMesh") ?? GameHost.Instance?.MainNode?.GetNodeOrNull<MeshInstance3D>("3DFogMesh");
				bool wasVisible = false;
				if (shroudMesh != null)
				{
					wasVisible = shroudMesh.Visible;
					shroudMesh.Visible = false;
				}

				bool wasPathingVisible = false;
				if (GameHost.Instance?.PathingOverlayMesh != null)
				{
					wasPathingVisible = GameHost.Instance.PathingOverlayMesh.Visible;
					GameHost.Instance.PathingOverlayMesh.Visible = false;
				}

				var unitsList = GameHost.Instance?.AllUnits;
				var unitVisibility = new System.Collections.Generic.List<(Unit3D unit, bool visible)>();
				if (unitsList != null)
				{
					foreach (var u in unitsList)
					{
						if (u != null && GodotObject.IsInstanceValid(u))
						{
							unitVisibility.Add((u, u.Visible));
							u.Visible = false;
						}
					}
				}

				SubViewport viewport = null;
				try
				{
					viewport = new SubViewport();
					viewport.Size = new Vector2I(256, 256);
					viewport.RenderTargetUpdateMode = SubViewport.UpdateMode.Once;
					AddChild(viewport);

					var camera = new Camera3D();
					camera.Projection = Camera3D.ProjectionType.Orthogonal;
					camera.Size = 250f;
					camera.Far = 200f;
					camera.Position = new Vector3(0, 100, 0);
					camera.RotationDegrees = new Vector3(-90, 0, 0);
					viewport.AddChild(camera);

					RuntimeTerrain.IsMinimapRendering = true;
					RuntimeTerrain.Instance?.BeginMinimapCapture();
					PropMultiMeshManager.Instance?.SetAllNodesVisible(true);
					try
					{
						await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);

						var texture = viewport.GetTexture();
						if (texture != null)
						{
							var img = texture.GetImage();
							if (img != null)
							{
								var imgTexture = ImageTexture.CreateFromImage(img);
								minimapBg.Texture = imgTexture;
							}
						}
					}
					finally
					{
						RuntimeTerrain.Instance?.EndMinimapCapture();
						RuntimeTerrain.IsMinimapRendering = false;
					}
				}
				catch (Exception ex)
				{
					GD.PrintErr($"Failed to dynamically capture terrain minimap: {ex.Message}");
				}
				finally
				{
					if (viewport != null && GodotObject.IsInstanceValid(viewport))
					{
						viewport.QueueFree();
					}
					if (shroudMesh != null && GodotObject.IsInstanceValid(shroudMesh))
					{
						shroudMesh.Visible = wasVisible;
					}
					if (GameHost.Instance?.PathingOverlayMesh != null)
					{
						GameHost.Instance.PathingOverlayMesh.Visible = wasPathingVisible;
					}
					foreach (var (u, vis) in unitVisibility)
					{
						if (u != null && GodotObject.IsInstanceValid(u))
						{
							u.Visible = vis;
						}
					}
				}
			} while (_minimapNeedsRegen);
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

		var statsVBox = new VBoxContainer();
		statsVBox.SizeFlagsHorizontal = SizeFlags.ExpandFill;
		statsVBox.CustomMinimumSize = new Vector2(0, 0);
		statsVBox.Alignment = BoxContainer.AlignmentMode.Center;
		_statsContainer.AddChild(statsVBox);

		_statsLabel = new Label();
		_statsLabel.AutowrapMode = TextServer.AutowrapMode.WordSmart;
		_statsLabel.SizeFlagsHorizontal = SizeFlags.ExpandFill;
		_statsLabel.CustomMinimumSize = new Vector2(0, 0);
		_statsLabel.Text = TranslationServer.Translate("HP: 100/100\nDamage: 10\nArmor: 2\nSpeed: 5");
		_statsLabel.AddThemeFontSizeOverride("font_size", 14);
		_statsLabel.AddThemeColorOverride("font_color", UIStyle.ColorGoldDull);
		statsVBox.AddChild(_statsLabel);

		_itemsBox = new VBoxContainer();
		_itemsBox.SizeFlagsHorizontal = SizeFlags.ExpandFill;
		_itemsBox.Alignment = BoxContainer.AlignmentMode.Center;
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
		btn.CustomMinimumSize = new Vector2(44, 44);
		btn.ClipContents = true;
		btn.AddThemeConstantOverride("icon_max_width", 38);

		if (tooltip.StartsWith('[') && tooltip.Contains(']'))
		{
			int end = tooltip.IndexOf(']');
			string hotkeyText = tooltip.Substring(1, end - 1);
			var hotkeyLabel = new Label();
			hotkeyLabel.Name = "HotkeyLabel";
			hotkeyLabel.Text = hotkeyText;
			hotkeyLabel.AddThemeFontSizeOverride("font_size", 10);
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
			bool hasPlayerSelection = false;
			foreach (var u in selectedUnits)
			{
				if (!u.IsEnemy)
				{
					hasPlayerSelection = true;
					break;
				}
			}
			bool isSpectator = LobbyManager.Instance != null && LobbyManager.Instance.LocalPlayer != null && LobbyManager.Instance.LocalPlayer.Team == "Spectator";
			_commandFrame.Visible = isSpectator ? false : hasPlayerSelection;
		}

		_viewModel.SelectedProp = GameHost.Instance?.SelectedProp;
		_viewModel.UpdateSelectedUnits(selectedUnits);
		_portraitPanelController?.Update(_viewModel);
		_commandPanelController?.Update(_viewModel);
		_inventoryPanelController?.Update(_viewModel);
	}

	private void ApplyThemeStyles()
	{
		var resourceBg = new StyleBoxTexture();
		resourceBg.Texture = GD.Load<Texture2D>("res://Assets/UI/stone_button_premium.png");
		resourceBg.TextureMarginLeft = 30;
		resourceBg.TextureMarginRight = 30;
		resourceBg.TextureMarginTop = 15;
		resourceBg.TextureMarginBottom = 15;
		resourceBg.ContentMarginLeft = 20;
		resourceBg.ContentMarginRight = 20;
		resourceBg.ContentMarginTop = 4;
		resourceBg.ContentMarginBottom = 4;
		_resourceContainer.AddThemeStyleboxOverride("panel", resourceBg);

		var bottomConsoleStyle = UIStyle.CreateStonePanel();
		bottomConsoleStyle.ContentMarginLeft = 40; 
		bottomConsoleStyle.ContentMarginRight = 40; 
		_bottomConsole.AddThemeStyleboxOverride("panel", bottomConsoleStyle);
		_minimapFrame.AddThemeStyleboxOverride("panel", UIStyle.CreateStonePanel(true));
		_portraitFrame.AddThemeStyleboxOverride("panel", UIStyle.CreateStonePanel(true));
		_selectionFrame.AddThemeStyleboxOverride("panel", UIStyle.CreateStonePanel(true));
		_selectionFrame.ClipContents = true;
		
		var commandFrameStyle = (StyleBoxTexture)UIStyle.CreateStonePanel(true).Duplicate();
		commandFrameStyle.ContentMarginLeft = 2;
		commandFrameStyle.ContentMarginRight = 2;
		commandFrameStyle.ContentMarginTop = 2;
		commandFrameStyle.ContentMarginBottom = 2;
		_commandFrame.AddThemeStyleboxOverride("panel", commandFrameStyle);

		var devStyle = new StyleBoxFlat();
		devStyle.BgColor = new Color(0.12f, 0.13f, 0.15f, 0.9f);
		devStyle.BorderColor = UIStyle.ColorBronze;
		devStyle.SetBorderWidthAll(2);
		devStyle.CornerRadiusBottomRight = 8;
		_devPanel.AddThemeStyleboxOverride("panel", devStyle);

		UIStyle.ApplyTitle(_feedbackLabel, "", 32);
		_feedbackLabel.AddThemeColorOverride("font_color", UIStyle.ColorGold);

		GetNode<TextureRect>("ResourceContainer/HBox/GoldBox/GoldIcon").Texture = GD.Load<Texture2D>("res://Assets/UI/gold_coin.png");
		GetNode<TextureRect>("ResourceContainer/HBox/WoodBox/WoodIcon").Texture = GD.Load<Texture2D>("res://Assets/UI/wood_logs.png");
		GetNode<TextureRect>("ResourceContainer/HBox/StoneBox/StoneIcon").Texture = GD.Load<Texture2D>("res://Assets/UI/wooden_planks.png");

		var topLabels = new[] { _goldLabel, _woodLabel, _stoneLabel };
		foreach (var lbl in topLabels)
		{
			lbl.AddThemeFontSizeOverride("font_size", 20);
			lbl.AddThemeColorOverride("font_color", UIStyle.ColorGoldDull);
		}
		_goldLabel.AddThemeColorOverride("font_color", UIStyle.ColorGold);

		GetNode<Label>("BottomConsole/HBox/PortraitFrame/VBox/UnitName").AddThemeColorOverride("font_color", UIStyle.ColorGoldDull);

		var minimapBg = new TextureRect();
		minimapBg.ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize;
		minimapBg.StretchMode = TextureRect.StretchModeEnum.KeepAspectCovered;
		minimapBg.MouseFilter = MouseFilterEnum.Ignore;
		_minimapArea.AddChild(minimapBg);
		minimapBg.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
		_minimapArea.MoveChild(minimapBg, 0); 

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

		if (GameHost.Instance != null)
		{
			var selected = GameHost.Instance.SelectedUnits;
			if (selected != null)
			{
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
		}

		_fogUpdateTimer += (float)delta;
		if (_fogUpdateTimer >= 0.1f)
		{
			_fogUpdateTimer = 0f;
			QueueMinimapRedraw();
		}

		_portraitPanelController?.Update(_viewModel);
		_commandPanelController?.Update(_viewModel);
		_resourcePanelController?.Update(_viewModel);
		_leaderboardPanelController?.Update(_viewModel);
		_summaryTablePanelController?.Update(_viewModel);

		if (_connectionWarningLabel != null)
		{
			_connectionWarningLabel.Visible = _viewModel.IsConnectionLost;
		}

		if (_btnSelectIdle != null)
		{
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

		_minimapPanelController?.UpdateMinimapIndicator();

		_controlGroupsUIController?.Update();
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

	private void SetupMinimapButton(Button btn, string iconPath, string tooltip, Action onClick)
	{
		btn.Flat = false;
		btn.Text = "";
		btn.ExpandIcon = true;
		btn.Icon = GD.Load<Texture2D>(iconPath);
		string trans = TranslationServer.Translate(tooltip);
		btn.TooltipText = string.IsNullOrEmpty(trans) ? tooltip : trans;
		btn.CustomMinimumSize = new Vector2(24, 24);
		btn.FocusMode = FocusModeEnum.None;
		btn.AddThemeConstantOverride("icon_max_width", 20);
		btn.AddThemeStyleboxOverride("normal", UIStyle.CreateHUDButtonStyle(false, false));
		btn.AddThemeStyleboxOverride("hover", UIStyle.CreateHUDButtonStyle(true, false));
		btn.AddThemeStyleboxOverride("pressed", UIStyle.CreateHUDButtonStyle(false, true));
		btn.Pressed += () => onClick?.Invoke();
	}

	private void SetupHUDButton(Button btn, string iconPath, string tooltip, Action onClick)
	{
		btn.Flat = false;
		btn.Text = "";
		btn.ExpandIcon = true;
		btn.Icon = GD.Load<Texture2D>(iconPath);
		string trans = TranslationServer.Translate(tooltip);
		btn.TooltipText = string.IsNullOrEmpty(trans) ? tooltip : trans;
		btn.CustomMinimumSize = new Vector2(44, 44);
		btn.SizeFlagsHorizontal = SizeFlags.ExpandFill;
		btn.SizeFlagsVertical = SizeFlags.ExpandFill;
		btn.FocusMode = FocusModeEnum.None;
		btn.ClipContents = true;
		btn.AddThemeConstantOverride("icon_max_width", 38);

		if (tooltip.StartsWith('[') && tooltip.Contains(']'))
		{
			int end = tooltip.IndexOf(']');
			string hotkeyText = tooltip.Substring(1, end - 1);
			var hotkeyLabel = new Label();
			hotkeyLabel.Name = "HotkeyLabel";
			hotkeyLabel.Text = hotkeyText;
			hotkeyLabel.AddThemeFontSizeOverride("font_size", 10);
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

	private void SetupDevPanel()
	{
		if (_btnVictory != null)
		{
			_btnVictory.Text = TranslationServer.Translate("Trigger Victory");
			_btnVictory.Pressed += () =>
			{
				UIManager.Instance?.PlayClickSound();
				UIManager.Instance?.TransitionTo(GameScreen.GameOver, true);
			};
		}
		if (_btnDefeat != null)
		{
			_btnDefeat.Text = TranslationServer.Translate("Trigger Defeat");
			_btnDefeat.Pressed += () =>
			{
				UIManager.Instance?.PlayClickSound();
				UIManager.Instance?.TransitionTo(GameScreen.GameOver, false);
			};
		}

		// Cycle Weather button — dynamically created since it is not in the .tscn
		if (_devPanel != null)
		{
			var btnWeather = new Button();
			btnWeather.Name = "BtnCycleWeather";
			btnWeather.Text = TranslationServer.Translate("Cycle Weather");
			btnWeather.AddThemeFontSizeOverride("font_size", 12);
			btnWeather.FocusMode = FocusModeEnum.None;
			// Position it to the right of BtnDefeat (offset_right = 252)
			btnWeather.SetAnchorsAndOffsetsPreset(LayoutPreset.TopLeft);
			btnWeather.OffsetLeft = 258;
			btnWeather.OffsetTop = 6;
			btnWeather.OffsetRight = 378;
			btnWeather.OffsetBottom = 36;
			btnWeather.Pressed += () => CycleWeather();
			_devPanel.AddChild(btnWeather);
		}
	}

	private void SetupPortrait()
	{
		var pTexture = GetNodeOrNull<TextureRect>("BottomConsole/HBox/PortraitFrame/VBox/PortraitTexture");
		if (pTexture != null)
		{
			pTexture.Texture = GD.Load<Texture2D>("res://Assets/UI/alliance_flag.png");
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

		if (_bottomConsole != null && _bottomConsole.Visible && _bottomConsole.GetGlobalRect().HasPoint(mousePos))
		{
			return true;
		}

		if (_resourceContainer != null && _resourceContainer.Visible && _resourceContainer.GetGlobalRect().HasPoint(mousePos))
		{
			return true;
		}

		if (_devPanel != null && _devPanel.Visible && _devPanel.GetGlobalRect().HasPoint(mousePos))
		{
			return true;
		}

		return false;
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
		if (@event.IsActionPressed("ui_focus_next"))
		{
			if (GameHost.Instance != null && GameHost.Instance.SelectedUnits.Count > 1)
			{
				bool reverse = Input.IsKeyPressed(Key.Shift);
				GameHost.Instance.CycleSelectionFocus(reverse);
				RefreshUI(GameHost.Instance.SelectedUnits);
				GetViewport().SetInputAsHandled();
			}
		}
		else if (@event is InputEventKey keyEvent && keyEvent.Pressed && keyEvent.Keycode == Key.Space)
		{
			if (!IsChatActive)
			{
				CenterCameraOnSelectedUnit();
				GetViewport().SetInputAsHandled();
			}
		}
		else if (@event is InputEventKey keyEntEvent && keyEntEvent.Pressed && keyEntEvent.Keycode == Key.Enter)
		{
			if (!IsChatActive)
			{
				_chatPanelController?.ShowChatInput(keyEntEvent.ShiftPressed);
				GetViewport().SetInputAsHandled();
			}
		}
		else if (@event is InputEventKey pauseKeyEvent && pauseKeyEvent.Pressed && (pauseKeyEvent.Keycode == Key.P || pauseKeyEvent.Keycode == Key.Pause))
		{
			if (!IsChatActive)
			{
				if (GameHost.Instance != null)
				{
					GameHost.Instance.TogglePauseRequest();
					GetViewport().SetInputAsHandled();
				}
			}
		}
		else if (@event is InputEventKey tildeEvent && tildeEvent.Pressed && !tildeEvent.Echo && tildeEvent.Keycode == Key.Quoteleft)
		{
			if (!IsChatActive && Realm.Godot.UI.WasmConsoleWindow.IsSinglePlayerOrTestMode())
			{
				Realm.Godot.UI.WasmConsoleWindow.Instance.ToggleVisibility();
				GetViewport().SetInputAsHandled();
			}
		}
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

		_hotkeyPanel.SetAnchorsAndOffsetsPreset(LayoutPreset.TopRight);
		_hotkeyPanel.OffsetRight = -10;
		_hotkeyPanel.OffsetTop = 80;
		_hotkeyPanel.OffsetLeft = -380;
		_hotkeyPanel.OffsetBottom = 600;
		_hotkeyPanel.Visible = false;
		_hotkeyPanel.AddThemeStyleboxOverride("panel", UIStyle.CreateStonePanel(true));
		AddChild(_hotkeyPanel);

		var scroll = new ScrollContainer();
		scroll.SizeFlagsVertical = SizeFlags.ExpandFill;
		scroll.SizeFlagsHorizontal = SizeFlags.ExpandFill;
		_hotkeyPanel.AddChild(scroll);

		var vbox = new VBoxContainer();
		vbox.AddThemeConstantOverride("separation", 4);
		scroll.AddChild(vbox);

		var titleLbl = new Label();
		titleLbl.Text = TranslationServer.Translate("HOTKEY REFERENCE");
		UIStyle.ApplyTitle(titleLbl, TranslationServer.Translate("HOTKEY REFERENCE"), 13);
		titleLbl.AddThemeColorOverride("font_color", UIStyle.ColorGold);
		vbox.AddChild(titleLbl);

		var sep = new HSeparator();
		vbox.AddChild(sep);

		var hotkeys = new (string key, string desc)[] {
			("Right-click",      "Smart command (move / attack / follow / rally)"),
			("Shift+Right-click","Queue additional move waypoint"),
			("Left-click",       "Select unit (drag to box-select)"),
			("Double-click",     "Select all same-type on screen"),
			("Shift+click",      "Add/remove unit from selection"),
			("", ""),
			("M",   "Move command (then left-click ground)"),
			("A",   "Attack / Attack-Move command"),
			("P",   "Patrol command"),
			("S",   "Stop all selected units"),
			("H",   "Hold Position"),
			("B",   "Open Build submenu"),
			("", ""),
			("Building selected:", ""),
			("Y",   "Set Rally Point"),
			("", ""),
			("", ""),
			("Camera:", ""),
			("WASD / Edge","Pan camera"),
			("Shift+WASD", "Fast pan"),
			("Space",      "Center on your Castle"),
			("Z",          "Cycle camera zoom"),
			("Ctrl+F5..F8","Save Camera Location 1..4"),
			("F5..F8",     "Jump to Saved Camera Location 1..4"),
			("", ""),
			("Selection:", ""),
			("F1",         "Select all Idle units"),
			("F2",         "Select all Army units"),
			("F3",         "Select all Buildings"),
			("Tab",        "Cycle focus through selected"),
			("`",          "Cycle through your buildings"),
			("Ctrl+0..9",  "Assign Control Group"),
			("0..9",       "Recall Control Group (double: jump)"),
			("", ""),
			("Map:", ""),
			("F4",         "Toggle Minimap terrain / radar"),
			("Alt+G",      "Ping map alert"),
			("", ""),
			("Chat / Cheats:", ""),
			("Enter",      "Open chat"),
			("", ""),
			("[Esc]",      "Cancel / clear selection / open settings"),
			("[Del]",      "Remove selected units (dev)"),
		};

		foreach (var (key, desc) in hotkeys)
		{
			if (string.IsNullOrEmpty(key) && string.IsNullOrEmpty(desc))
			{
				var spacer = new Label();
				spacer.Text = "";
				spacer.CustomMinimumSize = new Vector2(0, 4);
				vbox.AddChild(spacer);
				continue;
			}
			var row = new HBoxContainer();
			row.AddThemeConstantOverride("separation", 8);
			var keyLbl = new Label();
			keyLbl.Text = TranslationServer.Translate(key);
			keyLbl.CustomMinimumSize = new Vector2(130, 0);
			keyLbl.AddThemeFontSizeOverride("font_size", 11);
			keyLbl.AddThemeColorOverride("font_color", UIStyle.ColorGold);
			var descLbl = new Label();
			descLbl.Text = TranslationServer.Translate(desc);
			descLbl.AddThemeFontSizeOverride("font_size", 11);
			descLbl.AddThemeColorOverride("font_color", UIStyle.ColorGoldDull);
			descLbl.AutowrapMode = TextServer.AutowrapMode.WordSmart;
			descLbl.SizeFlagsHorizontal = SizeFlags.ExpandFill;
			row.AddChild(keyLbl);
			row.AddChild(descLbl);
			vbox.AddChild(row);
		}
	}

	public void ToggleHotkeyPanel()
	{
		_hotkeyPanelVisible = !_hotkeyPanelVisible;
		_hotkeyPanel.Visible = _hotkeyPanelVisible;
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




	private void CycleWeather()
	{
		if (Multiplayer.MultiplayerPeer != null && !Multiplayer.IsServer()) return;

		if (GameHost.Instance?.EnvironmentService != null)
		{
			string next = GameHost.Instance.EnvironmentService.CycleWeather();
			if (Multiplayer.MultiplayerPeer != null && Multiplayer.IsServer())
			{
				Rpc(nameof(SyncWeather), next);
			}
			else
			{
				ApplyWeatherEffects(next);
			}
		}
	}

	[Rpc(MultiplayerApi.RpcMode.AnyPeer, CallLocal = true)]
	private void SyncWeather(string weather)
	{
		if (GameHost.Instance?.EnvironmentService != null)
		{
			GameHost.Instance.EnvironmentService.SetCurrentWeather(weather);
			float density = weather switch
			{
				"clear" => 0f,
				"rain" => 0.0075f,
				"snow" => 0.005f,
				"fog" => 0.045f,
				_ => 0f
			};
			GameHost.Instance.EnvironmentService.SetBaseFogDensity(density);
			GameHost.Instance.EnvironmentService.ApplyWeatherVisuals(GameHost.Instance, weather);
		}
		ApplyWeatherEffects(weather);
	}

	public void ApplyWeatherEffects(string weather)
	{
		if (GameHost.Instance?.EnvironmentService != null)
		{
			GameHost.Instance.EnvironmentService.ApplyWeatherVisuals(GameHost.Instance, weather);
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
		if (GameHost.Instance == null || GameHost.Instance.CheatService == null)
		{
			return false;
		}

		var selectedEntities = new List<Entity>();
		if (GameHost.Instance.SelectedUnits != null)
		{
			foreach (var u in GameHost.Instance.SelectedUnits)
			{
				selectedEntities.Add(u.Entity);
			}
		}

		bool isMultiplayer = LobbyManager.Instance != null && !LobbyManager.Instance.IsSinglePlayer;
		if (Multiplayer.MultiplayerPeer == null || Multiplayer.MultiplayerPeer is OfflineMultiplayerPeer)
		{
			isMultiplayer = false;
		}

		var (result, affectedCount) = GameHost.Instance.CheatService.TryTriggerCheat(
			text,
			isMultiplayer,
			GameHost.Instance.PlayerEntity,
			GameHost.Instance.DefinitionManager,
			selectedEntities
		);

		if (result == CheatService.CheatResult.None)
		{
			return false;
		}

		ShowFeedbackText("Cheat Activated!", new Color(0.1f, 0.9f, 0.2f));

		switch (result)
		{
			case CheatService.CheatResult.Stonks:
				break;
			case CheatService.CheatResult.Gigachad:
				RefreshUI(GameHost.Instance.SelectedUnits);
				break;
			case CheatService.CheatResult.AbsoluteUnit:
				foreach (var unit in GameHost.Instance.SelectedUnits)
				{
					if (GodotObject.IsInstanceValid(unit))
					{
						unit.Scale = new Vector3(3f, 3f, 3f);
					}
				}
				RefreshUI(GameHost.Instance.SelectedUnits);
				break;
			case CheatService.CheatResult.ThanosSnap:
				break;
			case CheatService.CheatResult.EzClap:
				UIManager.Instance?.PlayClickSound();
				UIManager.Instance?.TransitionTo(GameScreen.GameOver, true);
				break;
			case CheatService.CheatResult.NoCap:
				RegenerateMinimapBackground();
				QueueMinimapRedraw();
				break;
			case CheatService.CheatResult.UnlimitedPower:
				{
				}
				break;
			case CheatService.CheatResult.WarpSpeed:
			{
				break;
			}
		}
		return true;
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

		if (_pausePanel == null)
		{
			BuildPausePanel();
		}

		bool isPaused = GameHost.Instance.IsPaused;
		_pausePanel.Visible = isPaused;

		if (!isPaused) return;

		if (GameHost.Instance.ResumeCountdownSeconds >= 0)
		{
			_pauseTitleLabel.Text = $"Resuming in {GameHost.Instance.ResumeCountdownSeconds}s...";
		}
		else
		{
			_pauseTitleLabel.Text = "Game Paused";
		}

		foreach (var child in _pausePlayerListContainer.GetChildren())
		{
			child.QueueFree();
		}

		bool isLocalHost = LobbyManager.Instance != null && LobbyManager.Instance.LocalPlayer != null && LobbyManager.Instance.LocalPlayer.IsHost;
		int localPeerId = LobbyManager.Instance != null && LobbyManager.Instance.LocalPlayer != null ? LobbyManager.Instance.LocalPlayer.PeerId : 1;

		if (LobbyManager.Instance != null)
		{
			foreach (var player in LobbyManager.Instance.PlayerList)
			{
				if (player.Team == "Spectator") continue;

				var row = new HBoxContainer();
				row.AddThemeConstantOverride("separation", 15);

				var nameLabel = new Label();
				nameLabel.Text = player.Name;
				nameLabel.CustomMinimumSize = new Vector2(150, 0);
				row.AddChild(nameLabel);

				GameHost.Instance.GetPlayerReadyState(player.PeerId, out bool ready);
				var readyLabel = new Label();
				readyLabel.Text = ready ? "READY" : "NOT READY";
				readyLabel.AddThemeColorOverride("font_color", ready ? new Color(0.2f, 0.9f, 0.3f) : new Color(0.9f, 0.3f, 0.2f));
				readyLabel.CustomMinimumSize = new Vector2(100, 0);
				row.AddChild(readyLabel);

				var disallowCheck = new CheckBox();
				disallowCheck.Text = "Disallow Pause";
				GameHost.Instance.GetPlayerDisallowPause(player.PeerId, out bool disallowed);
				disallowCheck.ButtonPressed = disallowed;
				disallowCheck.Disabled = !isLocalHost || player.PeerId == 1;
				disallowCheck.Toggled += (buttonPressed) =>
				{
					GameHost.Instance.RequestSetDisallowPause(player.PeerId, buttonPressed);
				};
				row.AddChild(disallowCheck);

				_pausePlayerListContainer.AddChild(row);
			}
		}

		GameHost.Instance.GetPlayerReadyState(localPeerId, out bool localReady);
		_pauseReadyCheck.ButtonPressed = localReady;
		_pauseReadyCheck.Disabled = GameHost.Instance.ResumeCountdownSeconds >= 0;

		_pauseForceResumeBtn.Visible = isLocalHost;
		_pauseForceResumeBtn.Disabled = GameHost.Instance.ResumeCountdownSeconds >= 0;
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
