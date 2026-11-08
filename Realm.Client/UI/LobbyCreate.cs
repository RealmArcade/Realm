using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Godot;
using Realm.Client.Network;
using Realm.Shared;
using static Godot.Control;
using static Godot.CanvasItem;
using Realm.Client.Utils;
using Realm.Client.Services;

namespace Realm.Client.UI;

[GlobalClass]
public partial class LobbyCreate : Control
{
	public override void _EnterTree()
	{
		Realm.Client.Services.GameContext.EnsureServices();
	}

	private Panel _bgPanel = null!;
	private Panel _leftPillar = null!;
	private Panel _rightPillar = null!;
	private PanelContainer _centralPanel = null!;
	private PanelContainer _briefingPanel = null!;

	private Button _backButton = null!;
	private Button _createButton = null!;
	private Button _openMapLibraryButton = null!;
	private OptionButton _versionSelectButton = null!;
	private HBoxContainer _versionContainer = null!;
	private Label _versionLabel = null!;
	private RichTextLabel _briefingText = null!;
	private TextureRect _mapThumbnail = null!;

	private Label _titleLabel = null!;
	private Label _selectedMapNameLabel = null!;

	private Panel? _hostProgressOverlay;
	private ProgressBar? _hostProgressBar;
	private Label? _hostProgressStatusLabel;
	private double _hostProgressCeiling;

	private List<MapBriefingDetails> _availableMaps = new List<MapBriefingDetails>();
	private List<string> _availableVersions = new List<string>();
	private int _selectedMapIndex = -1;

	private Panel? _mapPickerOverlay;
	private LineEdit? _mapPickerSearch;
	private GridContainer? _mapPickerGrid;
	private Label? _mapPickerEmptyLabel;
	private readonly Dictionary<string, CheckBox> _mapPickerTagChecks = new Dictionary<string, CheckBox>();

	public override void _Ready()
	{
		(_bgPanel, _leftPillar, _rightPillar) = UIStyle.BindStandardChrome(this);
		_centralPanel = GetNode<PanelContainer>("CentralPanel");

		var cardBg = GetNode<TextureRect>("CentralPanel/CardBg");
		cardBg.Texture = TrimmedTextureLoader.LoadTrimmedTexture("res://Assets/UI/custom_match_card.webp");
		cardBg.ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize;
		cardBg.StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered;
		cardBg.TextureFilter = TextureFilterEnum.LinearWithMipmaps;

		_backButton = GetNode<Button>("BackButton");
		_createButton = GetNode<Button>("CentralPanel/ContentContainer/CreateButton");
		_openMapLibraryButton = GetNode<Button>("CentralPanel/ContentContainer/MapLibraryButton");
		_briefingPanel = GetNode<PanelContainer>("CentralPanel/ContentContainer/BriefingPanel");
		_briefingText = GetNode<RichTextLabel>("CentralPanel/ContentContainer/BriefingPanel/TextPanelWrapper/BriefingText");

		var contentContainer = _openMapLibraryButton.GetParent();
		int mapHeaderIndex = _openMapLibraryButton.GetIndex();
		contentContainer.RemoveChild(_openMapLibraryButton);

		var mapLibraryButtonLiftFill = new Control();
		mapLibraryButtonLiftFill.Name = "MapLibraryButtonLiftFill";
		mapLibraryButtonLiftFill.CustomMinimumSize = new Vector2(0, 50);

		var mapLibraryButtonLift = new VBoxContainer();
		mapLibraryButtonLift.Name = "MapLibraryButtonLift";
		mapLibraryButtonLift.AddThemeConstantOverride("separation", 0);
		mapLibraryButtonLift.AddChild(_openMapLibraryButton);
		mapLibraryButtonLift.AddChild(mapLibraryButtonLiftFill);

		var mapLibraryButtonLiftOffset = new MarginContainer();
		mapLibraryButtonLiftOffset.Name = "MapLibraryButtonLiftOffset";
		mapLibraryButtonLiftOffset.AddThemeConstantOverride("margin_top", -50);
		mapLibraryButtonLiftOffset.AddChild(mapLibraryButtonLift);

		var mapHeaderVBox = new VBoxContainer();
		mapHeaderVBox.Name = "MapHeaderVBox";
		mapHeaderVBox.AddThemeConstantOverride("separation", 9);
		mapHeaderVBox.AddChild(mapLibraryButtonLiftOffset);
		contentContainer.AddChild(mapHeaderVBox);
		contentContainer.MoveChild(mapHeaderVBox, mapHeaderIndex);

		var selectedMapRow = new HBoxContainer();
		selectedMapRow.Name = "SelectedMapRow";
		selectedMapRow.Alignment = BoxContainer.AlignmentMode.Center;
		selectedMapRow.CustomMinimumSize = new Vector2(620, 40);
		selectedMapRow.SizeFlagsHorizontal = SizeFlags.ShrinkCenter;

		var selectedMapRowOffset = new MarginContainer();
		selectedMapRowOffset.Name = "SelectedMapRowOffset";
		selectedMapRowOffset.SizeFlagsHorizontal = SizeFlags.ShrinkCenter;
		selectedMapRowOffset.AddThemeConstantOverride("margin_top", -15);
		selectedMapRowOffset.AddChild(selectedMapRow);
		mapHeaderVBox.AddChild(selectedMapRowOffset);

		_selectedMapNameLabel = new Label();
		_selectedMapNameLabel.Name = "SelectedMapNameLabel";
		_selectedMapNameLabel.HorizontalAlignment = HorizontalAlignment.Center;
		_selectedMapNameLabel.VerticalAlignment = VerticalAlignment.Center;
		_selectedMapNameLabel.ClipText = true;
		_selectedMapNameLabel.CustomMinimumSize = new Vector2(600, 0);
		_selectedMapNameLabel.AddThemeFontSizeOverride("font_size", 20);
		_selectedMapNameLabel.AddThemeColorOverride("font_color", UIStyle.ColorGold);
		selectedMapRow.AddChild(_selectedMapNameLabel);

		_versionContainer = new HBoxContainer();
		_versionContainer.Name = "VersionContainer";
		_versionContainer.Alignment = BoxContainer.AlignmentMode.Center;
		_versionContainer.AddThemeConstantOverride("separation", 10);
		_versionContainer.CustomMinimumSize = new Vector2(400, 44);
		_versionContainer.SizeFlagsHorizontal = SizeFlags.ShrinkCenter;

		_versionLabel = new Label();
		_versionLabel.Name = "VersionLabel_Label";
		_versionLabel.Text = Tr("VERSION:");
		_versionLabel.VerticalAlignment = VerticalAlignment.Center;
		_versionContainer.AddChild(_versionLabel);

		_versionSelectButton = new OptionButton();
		_versionSelectButton.Name = "VersionSelectButton";
		_versionSelectButton.CustomMinimumSize = new Vector2(180, 44);
		_versionSelectButton.SizeFlagsHorizontal = SizeFlags.ShrinkCenter;
		_versionSelectButton.ItemSelected += (idx) => GameContext.UserInterface?.PlayClickSound();
		_versionContainer.AddChild(_versionSelectButton);

		contentContainer.AddChild(_versionContainer);
		contentContainer.MoveChild(_versionContainer, mapHeaderVBox.GetIndex() + 1);

		var textPanelWrapper = GetNode<PanelContainer>("CentralPanel/ContentContainer/BriefingPanel/TextPanelWrapper");
		textPanelWrapper.AddThemeStyleboxOverride("panel", UIStyle.CreateBriefingTextPlate());

		var spacer = new Control { CustomMinimumSize = new Vector2(0, 16) };
		_createButton.GetParent().AddChild(spacer);
		_createButton.GetParent().MoveChild(spacer, _createButton.GetIndex());

		_mapThumbnail = GetNode<TextureRect>("CentralPanel/ContentContainer/BriefingPanel/MapThumbnail");

		var briefingVBox = new VBoxContainer();
		briefingVBox.Name = "BriefingVBox";
		briefingVBox.AddThemeConstantOverride("separation", 14);
		briefingVBox.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
		briefingVBox.SizeFlagsVertical = Control.SizeFlags.ExpandFill;

		_briefingPanel.RemoveChild(_mapThumbnail);
		_briefingPanel.RemoveChild(textPanelWrapper);

		var thumbFrame = new Control();
		thumbFrame.Name = "ThumbnailFrame";
		thumbFrame.CustomMinimumSize = new Vector2(250, 250);
		thumbFrame.SizeFlagsHorizontal = Control.SizeFlags.ShrinkCenter;
		thumbFrame.SizeFlagsVertical = Control.SizeFlags.ShrinkCenter;

		var mapThumbnailBg = new Panel();
		mapThumbnailBg.Name = "MapThumbnailBg";
		mapThumbnailBg.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
		mapThumbnailBg.AnchorLeft = 0.13f;
		mapThumbnailBg.AnchorTop = 0.12f;
		mapThumbnailBg.AnchorRight = 0.87f;
		mapThumbnailBg.AnchorBottom = 0.88f;
		mapThumbnailBg.OffsetLeft = 0;
		mapThumbnailBg.OffsetTop = 0;
		mapThumbnailBg.OffsetRight = 0;
		mapThumbnailBg.OffsetBottom = 0;
		mapThumbnailBg.AddThemeStyleboxOverride("panel", UIStyle.CreateBackdropPanel());
		thumbFrame.AddChild(mapThumbnailBg);

		_mapThumbnail.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
		_mapThumbnail.AnchorLeft = 0.13f;
		_mapThumbnail.AnchorTop = 0.12f;
		_mapThumbnail.AnchorRight = 0.87f;
		_mapThumbnail.AnchorBottom = 0.88f;
		_mapThumbnail.OffsetLeft = 0;
		_mapThumbnail.OffsetTop = 0;
		_mapThumbnail.OffsetRight = 0;
		_mapThumbnail.OffsetBottom = 0;
		_mapThumbnail.ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize;
		_mapThumbnail.StretchMode = TextureRect.StretchModeEnum.KeepAspectCovered;
		_mapThumbnail.Modulate = Colors.White;
		_mapThumbnail.Texture = UIStyle.EmptyBlackTexture;
		thumbFrame.AddChild(_mapThumbnail);

		var frameOverlay = new TextureRect();
		frameOverlay.Name = "FrameOverlay";
		frameOverlay.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
		frameOverlay.Texture = TrimmedTextureLoader.LoadTrimmedTexture("res://Assets/UI/custom_match_map.webp");
		frameOverlay.ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize;
		frameOverlay.StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered;
		frameOverlay.TextureFilter = CanvasItem.TextureFilterEnum.LinearWithMipmaps;
		frameOverlay.MouseFilter = Control.MouseFilterEnum.Ignore;
		thumbFrame.AddChild(frameOverlay);

		briefingVBox.AddChild(thumbFrame);

		textPanelWrapper.CustomMinimumSize = new Vector2(620, 110);
		textPanelWrapper.SizeFlagsHorizontal = Control.SizeFlags.ShrinkCenter;
		textPanelWrapper.SizeFlagsVertical = Control.SizeFlags.ExpandFill;

		_briefingText.CustomMinimumSize = new Vector2(570, 70);
		_briefingText.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
		_briefingText.SizeFlagsVertical = Control.SizeFlags.ExpandFill;
		_briefingText.FitContent = false;
		_briefingText.AutowrapMode = TextServer.AutowrapMode.WordSmart;
		_briefingText.ScrollActive = true;
		_briefingText.ScrollFollowing = false;

		briefingVBox.AddChild(textPanelWrapper);

		_briefingPanel.AddChild(briefingVBox);

		_titleLabel = GetNode<Label>("Title");

		ApplyThemeStyles();

		_openMapLibraryButton.Pressed += OnOpenMapLibraryPressed;
		_openMapLibraryButton.MouseEntered += () => GameContext.UserInterface?.PlayHoverSound();
		_openMapLibraryButton.TooltipText = TranslationServer.Translate("Open the map library to choose the map you want to host");
		RefreshAvailableMaps();
		SetProcess(false);
		EnsureFestivalFeaturedMapInstalledAsync();
	}

	public override void _ExitTree()
	{
		CloseMapPicker();

		if (GameContext.Lobby != null)
		{
			GameContext.Lobby.HostLobbyStageChanged -= OnHostLobbyStageChanged;
		}

		base._ExitTree();
	}

	public override void _Process(double delta)
	{
		if (_hostProgressBar == null || !GodotObject.IsInstanceValid(_hostProgressBar)) return;

		double value = _hostProgressBar.Value;
		if (value >= _hostProgressCeiling) return;

		double creep = (_hostProgressCeiling - value) * Math.Min(1.0, delta * 1.5) + delta * 0.8;
		_hostProgressBar.Value = Math.Min(_hostProgressCeiling, value + creep);
	}

	private void ApplyThemeStyles()
	{
		_bgPanel.AddThemeStyleboxOverride("panel", UIStyle.CreateCustomMatchBg());
		_bgPanel.TextureFilter = TextureFilterEnum.LinearWithMipmaps;
		_leftPillar.AddThemeStyleboxOverride("panel", UIStyle.CreatePillarPanel(true));
		_rightPillar.AddThemeStyleboxOverride("panel", UIStyle.CreatePillarPanel(false));
		_centralPanel.AddThemeStyleboxOverride("panel", UIStyle.CreateCustomMatchCardPanel());
		_centralPanel.TextureFilter = TextureFilterEnum.LinearWithMipmaps;
		_briefingPanel.AddThemeStyleboxOverride("panel", new StyleBoxEmpty());

		UIStyle.ApplyTitle(_titleLabel, GameContext.Lobby?.IsSinglePlayer == true ? TranslationServer.Translate("SINGLE PLAYER") : TranslationServer.Translate("CREATE CUSTOM MATCH"), 36);

		_openMapLibraryButton.Flat = false;
		_openMapLibraryButton.CustomMinimumSize = new Vector2(240, 46);
		_openMapLibraryButton.SizeFlagsHorizontal = SizeFlags.ShrinkCenter;
		_openMapLibraryButton.AddThemeConstantOverride("icon_max_width", 20);
		_openMapLibraryButton.AddThemeConstantOverride("h_separation", 12);
		_openMapLibraryButton.Icon = UIStyle.GetUnicodeIconTexture(UnicodeIcons.FOLDER_OPEN, 20, UIStyle.ColorGold);
		UIStyle.ApplyButtonText(_openMapLibraryButton, TranslationServer.Translate("SELECT MAP"), 20);
		UIStyle.ApplyCustomLobbyDropdown(_openMapLibraryButton);
		_openMapLibraryButton.AddThemeColorOverride("font_pressed_color", UIStyle.ColorCyanGlow);

		UIStyle.ApplyBackButton(_backButton, () => GameContext.UserInterface?.TransitionTo(GameContext.Lobby?.IsSinglePlayer == true ? GameScreen.MainMenu : GameScreen.LobbyBrowser));
		SetupCreateButton();

		_versionLabel.Text = Tr("VERSION:");
		_versionLabel.AddThemeColorOverride("font_color", UIStyle.ColorGold);
		_versionLabel.AddThemeFontSizeOverride("font_size", 14);

		UIStyle.ApplyCustomLobbyDropdown(_versionSelectButton);
		_versionSelectButton.AddThemeColorOverride("font_color", UIStyle.ColorGoldDull);
		_versionSelectButton.AddThemeColorOverride("font_hover_color", UIStyle.ColorGold);
		_versionSelectButton.AddThemeColorOverride("font_pressed_color", UIStyle.ColorCyanGlow);
		_versionSelectButton.AddThemeFontSizeOverride("font_size", 15);

		_briefingText.AddThemeColorOverride("default_color", new Color(0.92f, 0.94f, 0.98f));
		_briefingText.AddThemeFontSizeOverride("normal_font_size", 15);

		PopulateRunicPillar(GetNode<VBoxContainer>("LeftPillar/RuneContainer"));
		PopulateRunicPillar(GetNode<VBoxContainer>("RightPillar/RuneContainer"));
	}

	private void SetupCreateButton()
	{
		_createButton.Flat = false;
		_createButton.AddThemeConstantOverride("icon_max_width", 0);
		UIStyle.ApplyButtonText(_createButton, TranslationServer.Translate("CREATE LOBBY"), 18);

		UIStyle.ApplyCustomLobbyStartGameButton(_createButton);

		_createButton.Pressed += OnCreatePressed;
		_createButton.MouseEntered += () => GameContext.UserInterface?.PlayHoverSound();
	}


	// Pressed is a Godot signal, so the handler keeps its void signature; the awaited work is in a Task-returning
	// method and this wrapper logs what it throws. Without the catch a fault in ContinueHosting left the button
	// silently unresponsive with nothing in the log.
	private async void OnCreatePressed()
	{
		try
		{
			await CreateLobbyAsync();
		}
		catch (Exception exception)
		{
			GameLog.LogError($"[LobbyCreate] Creating the lobby failed: {exception}");
		}
	}

	private async Task CreateLobbyAsync()
	{
		GameContext.UserInterface?.PlayClickSound();

		string selectedVersion = GetSelectedVersion();
		var mapInfo = GetSelectedMapInfo();

		if (MapSelectionService.IsBuildMismatch(mapInfo.BuildNumber))
		{
			GameContext.UserInterface?.PlayWarningSound();
			ShowMapBuildMismatchModal(mapInfo.PathName, mapInfo.DisplayName, mapInfo.BuildNumber, RealmVersion.GameBuildNumber);
			return;
		}

		await ContinueHosting(mapInfo.PathName, mapInfo.DisplayName, selectedVersion);
	}

	private async Task ContinueHosting(string pathName, string displayName, string selectedVersion)
	{
		var lobby = GameContext.Lobby;
		if (lobby == null) return;

		GameSettings.LastHostedMapName = pathName;
		GameSettings.LastHostedMapVersion = selectedVersion;
		GameSettings.Save();

		if (lobby.IsSinglePlayer)
		{
			_createButton.Disabled = true;
			bool hosted = await lobby.HostSinglePlayerLobbyAsync(pathName, selectedVersion);
			_createButton.Disabled = false;

			if (hosted)
			{
				GameContext.UserInterface?.TransitionTo(GameScreen.LobbyRoom);
				return;
			}

			GameContext.UserInterface?.PlayWarningSound();
			ShowHostErrorModal(lobby.LastHostError ?? Tr("Failed to start the match."));
			return;
		}

		if (lobby.LocalNatType == NatType.Symmetric)
		{
			GameContext.UserInterface?.PlayWarningSound();
			ShowNatErrorModal();
			return;
		}

		_createButton.Disabled = true;
		ShowHostProgressOverlay();
		lobby.HostLobbyStageChanged += OnHostLobbyStageChanged;
		bool success = await lobby.HostLobbyAsync(pathName, displayName, selectedVersion);
		lobby.HostLobbyStageChanged -= OnHostLobbyStageChanged;
		HideHostProgressOverlay();
		_createButton.Disabled = false;

		if (success)
		{
			GameContext.UserInterface?.TransitionTo(GameScreen.LobbyRoom);
		}
		else
		{
			GameContext.UserInterface?.PlayWarningSound();
			GameLog.LogError("[LobbyCreate] Failed to host lobby.");

			if (lobby.LocalNatType == NatType.Symmetric)
			{
				ShowNatErrorModal();
				return;
			}

			string errorMsg = lobby.LastHostError ?? Tr("Failed to host lobby on the registry server.");
			ShowHostErrorModal(errorMsg);
		}
	}

	private async void EnsureFestivalFeaturedMapInstalledAsync()
	{
		try
		{
			await InstallFestivalFeaturedMapAsync();
		}
		catch (Exception exception)
		{
			GameLog.LogError($"[LobbyCreate] Installing the festival featured map failed: {exception}");
		}
	}

	private async Task InstallFestivalFeaturedMapAsync()
	{
		if (!BuildFlags.IS_AUTOBATTLE_FESTIVAL_ACTIVE) return;
		if (MapProvisioningService.IsInstalled(BuildFlags.FESTIVAL_FEATURED_MAP_NAME)) return;

		_createButton.Disabled = true;
		ShowHostProgressOverlay(
			TranslationServer.Translate("PREPARING MAP"),
			TranslationServer.Translate("Looking for the newest version of `Chaos Arena` map..."));

		bool installed = await MapProvisioningService.DownloadLatestAsync(BuildFlags.FESTIVAL_FEATURED_MAP_NAME, ReportFestivalMapDownloadProgress);
		if (!GodotObject.IsInstanceValid(this) || !IsInsideTree()) return;

		HideHostProgressOverlay();
		_createButton.Disabled = false;

		if (installed)
		{
			RefreshAvailableMaps();
			return;
		}

		ShowFestivalMapUnavailableModal();
	}

	private void ReportFestivalMapDownloadProgress(float progress)
	{
		Callable.From(() => UpdateFestivalMapDownloadProgress(progress)).CallDeferred();
	}

	private void UpdateFestivalMapDownloadProgress(float progress)
	{
		if (_hostProgressBar == null || _hostProgressStatusLabel == null) return;
		if (!GodotObject.IsInstanceValid(_hostProgressBar) || !GodotObject.IsInstanceValid(_hostProgressStatusLabel)) return;

		double percent = Math.Clamp(progress * 100.0, 0, 100);
		_hostProgressStatusLabel.Text = string.Format(TranslationServer.Translate("Downloading map... {0}%"), (int)percent);
		_hostProgressBar.Value = Math.Max(_hostProgressBar.Value, percent);
		_hostProgressCeiling = _hostProgressBar.Value;
	}

	private void ShowFestivalMapUnavailableModal()
	{
		GameContext.UserInterface?.ShowPopupMessage(
			TranslationServer.Translate("MAP UNAVAILABLE"),
			TranslationServer.Translate("The map (Chaos Arena) could not be downloaded. Check your internet connection, then open this screen again."));
	}

	private void ShowHostProgressOverlay(string? titleText = null, string? initialStatus = null)
	{
		var overlay = new Panel();
		overlay.Name = "HostProgressOverlay";
		overlay.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
		overlay.AddThemeStyleboxOverride("panel", UIStyle.CreateBgGradient());
		overlay.ZIndex = 1100;
		AddChild(overlay);

		var center = new CenterContainer();
		center.Name = "HostProgressCenter";
		center.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
		overlay.AddChild(center);

		var panel = new PanelContainer();
		panel.Name = "HostProgressPanel";
		panel.AddThemeStyleboxOverride("panel", UIStyle.CreateStonePanel(true));
		panel.CustomMinimumSize = new Vector2(560, 0);
		center.AddChild(panel);

		var vbox = new VBoxContainer();
		vbox.Name = "HostProgressVBox";
		vbox.AddThemeConstantOverride("separation", 14);
		panel.AddChild(vbox);

		var titleLabel = new Label();
		titleLabel.Name = "HostProgressTitle_Label";
		UIStyle.ApplyTitle(titleLabel, titleText ?? TranslationServer.Translate("CREATING LOBBY"), 22);
		titleLabel.AddThemeColorOverride("font_color", UIStyle.ColorGold);
		titleLabel.HorizontalAlignment = HorizontalAlignment.Center;
		vbox.AddChild(titleLabel);

		var progressBar = new ProgressBar();
		progressBar.Name = "HostProgressBar_ProgressBar";
		progressBar.CustomMinimumSize = new Vector2(500, 20);
		progressBar.MinValue = 0;
		progressBar.MaxValue = 100;
		progressBar.Value = 4;
		progressBar.ShowPercentage = false;
		vbox.AddChild(progressBar);

		var statusLabel = new Label();
		statusLabel.Name = "HostProgressStatus_Label";
		statusLabel.Text = initialStatus ?? TranslationServer.Translate("Preparing the lobby...");
		statusLabel.AutowrapMode = TextServer.AutowrapMode.WordSmart;
		statusLabel.CustomMinimumSize = new Vector2(500, 0);
		statusLabel.SizeFlagsHorizontal = SizeFlags.ExpandFill;
		statusLabel.HorizontalAlignment = HorizontalAlignment.Center;
		statusLabel.AddThemeFontSizeOverride("font_size", 13);
		statusLabel.AddThemeColorOverride("font_color", new Color(0.88f, 0.9f, 0.95f));
		vbox.AddChild(statusLabel);

		_hostProgressOverlay = overlay;
		_hostProgressBar = progressBar;
		_hostProgressStatusLabel = statusLabel;
		_hostProgressCeiling = 6;
		SetProcess(true);
	}

	private void HideHostProgressOverlay()
	{
		SetProcess(false);
		_hostProgressBar = null;
		_hostProgressStatusLabel = null;
		_hostProgressCeiling = 0;

		if (_hostProgressOverlay != null && GodotObject.IsInstanceValid(_hostProgressOverlay))
		{
			_hostProgressOverlay.QueueFree();
		}

		_hostProgressOverlay = null;
	}

	private void OnHostLobbyStageChanged(HostLobbyStage stage)
	{
		if (_hostProgressOverlay == null || !GodotObject.IsInstanceValid(_hostProgressOverlay)) return;
		if (_hostProgressBar == null || _hostProgressStatusLabel == null) return;

		(string statusText, double floor, double ceiling) = DescribeHostLobbyStage(stage);
		if (string.IsNullOrEmpty(statusText)) return;

		_hostProgressStatusLabel.Text = statusText;
		_hostProgressBar.Value = Math.Max(_hostProgressBar.Value, floor);
		_hostProgressCeiling = ceiling;
	}

	private static (string StatusText, double Floor, double Ceiling) DescribeHostLobbyStage(HostLobbyStage stage)
	{
		switch (stage)
		{
			case HostLobbyStage.TestingNetwork:
				return (TranslationServer.Translate("Testing your connection and NAT type..."), 6, 25);
			case HostLobbyStage.LoadingMap:
				return (TranslationServer.Translate("Loading the map into the match server..."), 25, 60);
			case HostLobbyStage.AdmittingHost:
				return (TranslationServer.Translate("Entering the lobby as host..."), 60, 74);
			case HostLobbyStage.OpeningPort:
				return (TranslationServer.Translate("Opening your port for other players..."), 74, 88);
			case HostLobbyStage.RegisteringLobby:
				return (TranslationServer.Translate("Registering the lobby on the server..."), 88, 99);
			default:
				return (string.Empty, 0, 0);
		}
	}

	private string GetSelectedVersion()
	{
		if (_availableVersions.Count > 0 && _versionSelectButton.Selected >= 0 && _versionSelectButton.Selected < _availableVersions.Count)
		{
			return _availableVersions[_versionSelectButton.Selected];
		}

		return "1.0.0";
	}

	private (string PathName, string DisplayName, string BuildNumber) GetSelectedMapInfo()
	{
		if (_selectedMapIndex >= 0 && _selectedMapIndex < _availableMaps.Count)
		{
			var map = _availableMaps[_selectedMapIndex];
			return (map.PathName, map.DisplayName, map.GameBuildNumber);
		}

		return ("melee", "Melee Battlefield", RealmVersion.GameBuildNumber);
	}

	private void ShowMapBuildMismatchModal(string mapPath, string mapDisplayName, string mapBuildNumber, string currentBuildNumber)
	{
		bool versionExistsLocally = File.Exists(LobbyManager.GetVersionExecutablePath(mapBuildNumber));

		string promptText = Tr("This map is out of date. To host this match, download the matching game build or open the map in the Map Editor to upgrade it.");
		string message = $"{string.Format(Tr("Map: {0}"), mapDisplayName)}\n{string.Format(Tr("Map Build: {0}"), mapBuildNumber)} | {string.Format(Tr("Current Build: {0}"), currentBuildNumber)}\n\n{promptText}";

		GameContext.UserInterface?.ShowConfirmationDialog(message, () =>
		{
			if (versionExistsLocally)
			{
				string exePath = LobbyManager.GetVersionExecutablePath(mapBuildNumber);
				OS.CreateProcess(exePath, Array.Empty<string>());
				GetTree().Quit();
			}
			}, confirmText: versionExistsLocally ? string.Format(Tr("LAUNCH {0}"), mapBuildNumber) : string.Format(Tr("DOWNLOAD {0}"), mapBuildNumber),
			cancelText: TranslationServer.Translate("CANCEL"), titleOverride: "MAP BUILD MISMATCH");
	}

	private void ShowNatErrorModal()
	{
		GameContext.UserInterface?.ShowPopupMessage(
			TranslationServer.Translate("CANNOT HOST A GAME"),
			TranslationServer.Translate("Your internet provider's network prevents other players from connecting to a game you host.\n\nYou can join another player's lobby, or play single player against AI bots."),
			() => GameContext.UserInterface?.TransitionTo(GameScreen.LobbyBrowser));
	}

	private void ShowHostErrorModal(string errorMessage)
	{
		GameContext.UserInterface?.ShowPopupMessage("LOBBY CREATION ERROR", errorMessage);
	}

	private void RefreshAvailableMaps(string? selectMapName = null, string? selectMapVersion = null)
	{
		_availableMaps = MapInfoHelper.GetAvailableMaps();

		if (_availableMaps.Count == 0)
		{
			_selectedMapIndex = -1;
			_selectedMapNameLabel.Text = TranslationServer.Translate("No maps installed");
			_selectedMapNameLabel.TooltipText = string.Empty;
			_briefingText.Text = string.Empty;
			_mapThumbnail.Texture = UIStyle.EmptyBlackTexture;
			_availableVersions.Clear();
			_versionSelectButton.Clear();
			CloseMapPicker();
			return;
		}

		SelectMap(MapSelectionService.ResolvePreferredMapIndex(_availableMaps, selectMapName), selectMapVersion);
		RefreshMapPickerGrid();
	}

	private void SelectMap(int index, string? targetVersion = null)
	{
		if (index < 0 || index >= _availableMaps.Count) return;

		_selectedMapIndex = index;
		var selectedMap = _availableMaps[index];

		_selectedMapNameLabel.Text = selectedMap.DisplayName;
		_selectedMapNameLabel.TooltipText = DescribeMapForPicker(selectedMap);

		string? resolvedVersion = targetVersion;
		if (string.IsNullOrEmpty(resolvedVersion) &&
		    string.Equals(selectedMap.PathName, GameSettings.LastHostedMapName, StringComparison.OrdinalIgnoreCase))
		{
			resolvedVersion = GameSettings.LastHostedMapVersion;
		}

		_briefingText.Text = selectedMap.Description;
		_briefingText.ScrollToLine(0);
		RefreshVersionsForSelectedMap(selectedMap, resolvedVersion);

		string thumbPath = GetMapThumbnailPath(selectedMap);
		LoadThumbnailTexture(thumbPath);
	}

	private void OnOpenMapLibraryPressed()
	{
		GameContext.UserInterface?.PlayClickSound();

		if (_mapPickerOverlay != null && GodotObject.IsInstanceValid(_mapPickerOverlay))
		{
			CloseMapPicker();
			return;
		}

		ShowMapPicker();
	}

	private void PopulateRunicPillar(VBoxContainer container)
	{
		container.Visible = false;
	}
}
