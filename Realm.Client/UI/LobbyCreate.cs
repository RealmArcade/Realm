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

	private Panel _bgPanel;
	private Panel _leftPillar;
	private Panel _rightPillar;
	private PanelContainer _centralPanel;
	private PanelContainer _briefingPanel;

	private Button _backButton;
	private Button _createButton;
	private Button _openMapLibraryButton;
	private OptionButton _versionSelectButton;
	private HBoxContainer _versionContainer;
	private Label _versionLabel;
	private RichTextLabel _briefingText;
	private TextureRect _mapThumbnail;

	private Label _titleLabel;
	private Label _selectedMapNameLabel;

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
		_bgPanel = GetNode<Panel>("Background");
		_leftPillar = GetNode<Panel>("LeftPillar");
		_rightPillar = GetNode<Panel>("RightPillar");
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
		_versionSelectButton.ItemSelected += (idx) => GameContext.UserInterface.PlayClickSound();
		_versionContainer.AddChild(_versionSelectButton);

		contentContainer.AddChild(_versionContainer);
		contentContainer.MoveChild(_versionContainer, mapHeaderVBox.GetIndex() + 1);

		var textPanelWrapper = GetNode<PanelContainer>("CentralPanel/ContentContainer/BriefingPanel/TextPanelWrapper");
		var textPanelStyle = UIStyle.CreateBackdropPanel();
		textPanelStyle.ContentMarginLeft = 24;
		textPanelStyle.ContentMarginRight = 24;
		textPanelStyle.ContentMarginTop = 20;
		textPanelStyle.ContentMarginBottom = 20;
		textPanelWrapper.AddThemeStyleboxOverride("panel", textPanelStyle);

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

		UIStyle.ApplyTitle(_titleLabel, GameContext.Lobby.IsSinglePlayer ? TranslationServer.Translate("SINGLE PLAYER") : TranslationServer.Translate("CREATE CUSTOM MATCH"), 36);

		_openMapLibraryButton.Flat = false;
		_openMapLibraryButton.CustomMinimumSize = new Vector2(240, 46);
		_openMapLibraryButton.SizeFlagsHorizontal = SizeFlags.ShrinkCenter;
		_openMapLibraryButton.AddThemeConstantOverride("icon_max_width", 20);
		_openMapLibraryButton.AddThemeConstantOverride("h_separation", 12);
		_openMapLibraryButton.Icon = UIStyle.GetUnicodeIconTexture(UnicodeIcons.FOLDER_OPEN, 20, UIStyle.ColorGold);
		UIStyle.ApplyButtonText(_openMapLibraryButton, TranslationServer.Translate("SELECT MAP"), 20);
		_openMapLibraryButton.AddThemeStyleboxOverride("normal", UIStyle.CreateDropdownStyle(false, false));
		_openMapLibraryButton.AddThemeStyleboxOverride("hover", UIStyle.CreateDropdownStyle(true, false));
		_openMapLibraryButton.AddThemeStyleboxOverride("pressed", UIStyle.CreateDropdownStyle(false, true));
		_openMapLibraryButton.AddThemeStyleboxOverride("focus", new StyleBoxEmpty());
		_openMapLibraryButton.AddThemeColorOverride("font_pressed_color", UIStyle.ColorCyanGlow);

		UIStyle.ApplyBackButton(_backButton, () => GameContext.UserInterface.TransitionTo(GameContext.Lobby.IsSinglePlayer ? GameScreen.MainMenu : GameScreen.LobbyBrowser));
		SetupCreateButton();

		_versionLabel.Text = Tr("VERSION:");
		_versionLabel.AddThemeColorOverride("font_color", UIStyle.ColorGold);
		_versionLabel.AddThemeFontSizeOverride("font_size", 14);

		_versionSelectButton.AddThemeStyleboxOverride("normal", UIStyle.CreateDropdownStyle(false, false));
		_versionSelectButton.AddThemeStyleboxOverride("hover", UIStyle.CreateDropdownStyle(true, false));
		_versionSelectButton.AddThemeStyleboxOverride("pressed", UIStyle.CreateDropdownStyle(false, true));
		_versionSelectButton.AddThemeStyleboxOverride("focus", new StyleBoxEmpty());
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

		_createButton.AddThemeStyleboxOverride("normal", UIStyle.CreateCustomLobbyStartGameButton(false, false));
		_createButton.AddThemeStyleboxOverride("hover", UIStyle.CreateCustomLobbyStartGameButton(true, false));
		_createButton.AddThemeStyleboxOverride("pressed", UIStyle.CreateCustomLobbyStartGameButton(false, true));
		_createButton.AddThemeStyleboxOverride("focus", new StyleBoxEmpty());

		_createButton.Pressed += OnCreatePressed;
		_createButton.MouseEntered += () => GameContext.UserInterface.PlayHoverSound();
	}


	private async void OnCreatePressed()
	{
		GameContext.UserInterface.PlayClickSound();

		string selectedVersion = GetSelectedVersion();
		var mapInfo = GetSelectedMapInfo();

		if (IsMapBuildMismatch(mapInfo.BuildNumber))
		{
			GameContext.UserInterface.PlayWarningSound();
			ShowMapBuildMismatchModal(mapInfo.PathName, mapInfo.DisplayName, mapInfo.BuildNumber, RealmVersion.GameBuildNumber);
			return;
		}

		await ContinueHosting(mapInfo.PathName, mapInfo.DisplayName, selectedVersion);
	}

	private async Task ContinueHosting(string pathName, string displayName, string selectedVersion)
	{
		GameSettings.LastHostedMapName = pathName;
		GameSettings.LastHostedMapVersion = selectedVersion;
		GameSettings.Save();

		if (GameContext.Lobby.IsSinglePlayer)
		{
			_createButton.Disabled = true;
			bool hosted = await GameContext.Lobby.HostSinglePlayerLobbyAsync(pathName, selectedVersion);
			_createButton.Disabled = false;

			if (hosted)
			{
				GameContext.UserInterface.TransitionTo(GameScreen.LobbyRoom);
				return;
			}

			GameContext.UserInterface.PlayWarningSound();
			ShowHostErrorModal(GameContext.Lobby.LastHostError ?? Tr("Failed to start the match."));
			return;
		}

		if (GameContext.Lobby.LocalNatType == NatType.Symmetric)
		{
			GameContext.UserInterface.PlayWarningSound();
			ShowNatErrorModal();
			return;
		}

		_createButton.Disabled = true;
		ShowHostProgressOverlay();
		GameContext.Lobby.HostLobbyStageChanged += OnHostLobbyStageChanged;
		bool success = await GameContext.Lobby.HostLobbyAsync(pathName, displayName, selectedVersion);
		GameContext.Lobby.HostLobbyStageChanged -= OnHostLobbyStageChanged;
		HideHostProgressOverlay();
		_createButton.Disabled = false;

		if (success)
		{
			GameContext.UserInterface.TransitionTo(GameScreen.LobbyRoom);
		}
		else
		{
			GameContext.UserInterface.PlayWarningSound();
			GD.PrintErr("[LobbyCreate] Failed to host lobby.");

			if (GameContext.Lobby.LocalNatType == NatType.Symmetric)
			{
				ShowNatErrorModal();
				return;
			}

			string errorMsg = GameContext.Lobby.LastHostError ?? Tr("Failed to host lobby on the registry server.");
			ShowHostErrorModal(errorMsg);
		}
	}

	private async void EnsureFestivalFeaturedMapInstalledAsync()
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

	private bool IsMapBuildMismatch(string mapBuildNumber)
	{
		return !string.IsNullOrEmpty(mapBuildNumber) &&
		       !string.Equals(mapBuildNumber, RealmVersion.GameBuildNumber, StringComparison.OrdinalIgnoreCase);
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
			cancelText: "CANCEL", titleOverride: "MAP BUILD MISMATCH");
	}

	private void ShowNatErrorModal()
	{
		GameContext.UserInterface?.ShowPopupMessage(
			TranslationServer.Translate("CANNOT HOST A GAME"),
			TranslationServer.Translate("Your internet provider's network prevents other players from connecting to a game you host.\n\nYou can join another player's lobby, or play single player against AI bots."),
			() => GameContext.UserInterface.TransitionTo(GameScreen.LobbyBrowser));
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

		SelectMap(ResolvePreferredMapIndex(selectMapName), selectMapVersion);
		RefreshMapPickerGrid();
	}

	private int ResolvePreferredMapIndex(string? requestedMapName)
	{
		int requestedIndex = IndexOfMap(requestedMapName);
		if (requestedIndex >= 0) return requestedIndex;

		if (BuildFlags.IS_AUTOBATTLE_FESTIVAL_ACTIVE)
		{
			int festivalIndex = IndexOfMap(BuildFlags.FESTIVAL_FEATURED_MAP_NAME);
			if (festivalIndex >= 0) return festivalIndex;
		}

		int lastHostedIndex = IndexOfMap(GameSettings.LastHostedMapName);
		if (lastHostedIndex >= 0) return lastHostedIndex;

		for (int i = 0; i < _availableMaps.Count; i++)
		{
			if (!IsAssetPackMap(_availableMaps[i])) return i;
		}

		return 0;
	}

	private int IndexOfMap(string? mapName)
	{
		if (string.IsNullOrWhiteSpace(mapName)) return -1;

		string normalizedMapName = NormalizeMapName(mapName);

		for (int i = 0; i < _availableMaps.Count; i++)
		{
			var map = _availableMaps[i];

			if (string.Equals(map.PathName, mapName, StringComparison.OrdinalIgnoreCase)) return i;
			if (string.Equals(map.DisplayName, mapName, StringComparison.OrdinalIgnoreCase)) return i;
			if (string.Equals(NormalizeMapName(map.PathName), normalizedMapName, StringComparison.Ordinal)) return i;
			if (string.Equals(NormalizeMapName(map.DisplayName), normalizedMapName, StringComparison.Ordinal)) return i;
		}

		return -1;
	}

	private static string NormalizeMapName(string mapName)
	{
		return mapName.Replace(" ", string.Empty).Replace("_", string.Empty).Replace("-", string.Empty).Trim().ToLowerInvariant();
	}

	private static bool IsAssetPackMap(MapBriefingDetails map)
	{
		if (string.Equals((map.MapType ?? string.Empty).Trim(), MapTagCatalog.AssetPackTag, StringComparison.OrdinalIgnoreCase)) return true;
		if (map.Tags == null) return false;

		foreach (string mapTag in map.Tags)
		{
			if (string.IsNullOrWhiteSpace(mapTag)) continue;
			if (string.Equals(mapTag.Trim(), MapTagCatalog.AssetPackTag, StringComparison.OrdinalIgnoreCase)) return true;
		}

		return false;
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

	private void ShowMapPicker()
	{
		var overlay = new Panel();
		overlay.Name = "MapPickerOverlay";
		overlay.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
		overlay.AddThemeStyleboxOverride("panel", UIStyle.CreateBgGradient());
		overlay.ZIndex = 1150;
		AddChild(overlay);
		_mapPickerOverlay = overlay;

		var center = new CenterContainer();
		center.Name = "MapPickerCenter";
		center.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
		overlay.AddChild(center);

		var panel = new PanelContainer();
		panel.Name = "MapPickerPanel";
		panel.AddThemeStyleboxOverride("panel", UIStyle.CreateStonePanel(true));
		panel.CustomMinimumSize = new Vector2(1060, 640);
		center.AddChild(panel);

		var vbox = new VBoxContainer();
		vbox.Name = "MapPickerVBox";
		vbox.AddThemeConstantOverride("separation", 12);
		panel.AddChild(vbox);

		var titleLabel = new Label();
		titleLabel.Name = "MapPickerTitle_Label";
		UIStyle.ApplyTitle(titleLabel, TranslationServer.Translate("MAP LIBRARY"), 22);
		titleLabel.HorizontalAlignment = HorizontalAlignment.Center;
		vbox.AddChild(titleLabel);

		var searchBar = new LineEdit();
		searchBar.Name = "MapPickerSearch_LineEdit";
		searchBar.PlaceholderText = TranslationServer.Translate("Search maps by name...");
		searchBar.ClearButtonEnabled = true;
		searchBar.CustomMinimumSize = new Vector2(1000, 44);
		searchBar.SizeFlagsHorizontal = SizeFlags.ExpandFill;
		searchBar.AddThemeStyleboxOverride("normal", UIStyle.CreateCustomLobbySearchInput(false));
		searchBar.AddThemeStyleboxOverride("focus", UIStyle.CreateCustomLobbySearchInput(true));
		searchBar.TextChanged += _ => RefreshMapPickerGrid();
		vbox.AddChild(searchBar);
		_mapPickerSearch = searchBar;

		var filterRow = new HFlowContainer();
		filterRow.Name = "MapPickerFilters_HFlowContainer";
		filterRow.AddThemeConstantOverride("h_separation", 18);
		filterRow.AddThemeConstantOverride("v_separation", 6);
		vbox.AddChild(filterRow);

		_mapPickerTagChecks.Clear();
		foreach (string tag in MapTagCatalog.MapFilters)
		{
			var tagCheckBox = new CheckBox();
			tagCheckBox.Name = "MapPickerTag_" + tag.Replace(" ", string.Empty);
			tagCheckBox.Text = MapTagCatalog.GetDisplayName(tag);
			tagCheckBox.FocusMode = FocusModeEnum.None;
			tagCheckBox.Pressed += () =>
			{
				GameContext.UserInterface?.PlayClickSound();
				RefreshMapPickerGrid();
			};
			tagCheckBox.MouseEntered += () => GameContext.UserInterface?.PlayHoverSound();

			if (string.Equals(tag, MapTagCatalog.AssetPackTag, StringComparison.Ordinal))
			{
				tagCheckBox.TooltipText = TranslationServer.Translate("Also list asset packs, which carry shared assets and cannot be hosted on their own");
			}

			UIStyle.ApplyCheckboxStyle(tagCheckBox);
			filterRow.AddChild(tagCheckBox);
			_mapPickerTagChecks[tag] = tagCheckBox;
		}

		var scroll = new ScrollContainer();
		scroll.Name = "MapPickerScroll_ScrollContainer";
		scroll.CustomMinimumSize = new Vector2(1000, 460);
		scroll.SizeFlagsHorizontal = SizeFlags.ExpandFill;
		scroll.SizeFlagsVertical = SizeFlags.ExpandFill;
		scroll.HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled;
		vbox.AddChild(scroll);

		var grid = new GridContainer();
		grid.Name = "MapPickerGrid_GridContainer";
		grid.Columns = 3;
		grid.AddThemeConstantOverride("h_separation", 12);
		grid.AddThemeConstantOverride("v_separation", 10);
		grid.SizeFlagsHorizontal = SizeFlags.ExpandFill;
		scroll.AddChild(grid);
		_mapPickerGrid = grid;

		var emptyLabel = new Label();
		emptyLabel.Name = "MapPickerEmpty_Label";
		emptyLabel.Text = TranslationServer.Translate("No map matches the search and filters.");
		emptyLabel.Visible = false;
		emptyLabel.HorizontalAlignment = HorizontalAlignment.Center;
		emptyLabel.AutowrapMode = TextServer.AutowrapMode.WordSmart;
		emptyLabel.CustomMinimumSize = new Vector2(1000, 0);
		emptyLabel.SizeFlagsHorizontal = SizeFlags.ExpandFill;
		emptyLabel.AddThemeFontSizeOverride("font_size", 14);
		emptyLabel.AddThemeColorOverride("font_color", new Color(0.88f, 0.9f, 0.95f));
		vbox.AddChild(emptyLabel);
		_mapPickerEmptyLabel = emptyLabel;

		var footer = new HBoxContainer();
		footer.Name = "MapPickerFooter_HBoxContainer";
		footer.Alignment = BoxContainer.AlignmentMode.Center;
		footer.AddThemeConstantOverride("separation", 16);
		vbox.AddChild(footer);

		var importButton = new Button();
		importButton.Name = "MapPickerImportButton";
		importButton.CustomMinimumSize = new Vector2(260, 44);
		importButton.FocusMode = FocusModeEnum.None;
		importButton.MouseFilter = MouseFilterEnum.Stop;
		importButton.AddThemeConstantOverride("icon_max_width", 24);
		UIStyle.ApplyButtonText(importButton, "📥 " + TranslationServer.Translate("Import Map (.rmap)"), 13);
		importButton.AddThemeStyleboxOverride("normal", UIStyle.CreateButtonNormal());
		importButton.AddThemeStyleboxOverride("hover", UIStyle.CreateButtonHover());
		importButton.AddThemeStyleboxOverride("pressed", UIStyle.CreateButtonPressed());
		importButton.AddThemeStyleboxOverride("focus", new StyleBoxEmpty());
		importButton.MouseEntered += () => GameContext.UserInterface?.PlayHoverSound();
		importButton.Pressed += () =>
		{
			GameContext.UserInterface?.PlayClickSound();
			GameContext.UserInterface?.PromptAndImportMapArchive((mapTitle, mapVersion) =>
			{
				RefreshAvailableMaps(mapTitle, mapVersion);
				CloseMapPicker();
			});
		};
		footer.AddChild(importButton);

		var closeButton = new Button();
		closeButton.Name = "MapPickerCloseButton";
		closeButton.CustomMinimumSize = new Vector2(200, 44);
		closeButton.FocusMode = FocusModeEnum.None;
		closeButton.AddThemeConstantOverride("icon_max_width", 0);
		UIStyle.ApplyButtonText(closeButton, TranslationServer.Translate("CLOSE"), 15);
		closeButton.AddThemeStyleboxOverride("normal", UIStyle.CreateButtonNormal());
		closeButton.AddThemeStyleboxOverride("hover", UIStyle.CreateButtonHover());
		closeButton.AddThemeStyleboxOverride("pressed", UIStyle.CreateButtonPressed());
		closeButton.AddThemeStyleboxOverride("focus", new StyleBoxEmpty());
		closeButton.MouseEntered += () => GameContext.UserInterface?.PlayHoverSound();
		closeButton.Pressed += () =>
		{
			GameContext.UserInterface?.PlayClickSound();
			CloseMapPicker();
		};
		footer.AddChild(closeButton);

		RefreshMapPickerGrid();
	}

	private void CloseMapPicker()
	{
		_mapPickerSearch = null;
		_mapPickerGrid = null;
		_mapPickerEmptyLabel = null;
		_mapPickerTagChecks.Clear();

		if (_mapPickerOverlay != null && GodotObject.IsInstanceValid(_mapPickerOverlay))
		{
			_mapPickerOverlay.QueueFree();
		}

		_mapPickerOverlay = null;
	}

	private void RefreshMapPickerGrid()
	{
		if (_mapPickerGrid == null || !GodotObject.IsInstanceValid(_mapPickerGrid)) return;

		foreach (Node child in _mapPickerGrid.GetChildren())
		{
			_mapPickerGrid.RemoveChild(child);
			child.QueueFree();
		}

		string searchText = _mapPickerSearch != null && GodotObject.IsInstanceValid(_mapPickerSearch) ? _mapPickerSearch.Text.Trim() : string.Empty;
		bool includeAssetPacks = IsMapPickerTagChecked(MapTagCatalog.AssetPackTag);

		var requiredTags = new List<string>();
		foreach (string tag in MapTagCatalog.MapFilters)
		{
			if (string.Equals(tag, MapTagCatalog.AssetPackTag, StringComparison.Ordinal)) continue;
			if (IsMapPickerTagChecked(tag)) requiredTags.Add(tag);
		}

		int shownMaps = 0;
		for (int i = 0; i < _availableMaps.Count; i++)
		{
			var map = _availableMaps[i];

			if (!includeAssetPacks && IsAssetPackMap(map)) continue;
			if (searchText.Length > 0 && !MatchesSearchText(map, searchText)) continue;
			if (requiredTags.Count > 0 && !HasAnyTag(map, requiredTags)) continue;

			_mapPickerGrid.AddChild(CreateMapPickerTile(map, i));
			shownMaps++;
		}

		if (_mapPickerEmptyLabel != null && GodotObject.IsInstanceValid(_mapPickerEmptyLabel))
		{
			_mapPickerEmptyLabel.Visible = shownMaps == 0;
		}
	}

	private bool IsMapPickerTagChecked(string tag)
	{
		return _mapPickerTagChecks.TryGetValue(tag, out var tagCheckBox) &&
		       GodotObject.IsInstanceValid(tagCheckBox) &&
		       tagCheckBox.ButtonPressed;
	}

	private static bool MatchesSearchText(MapBriefingDetails map, string searchText)
	{
		if (map.DisplayName.Contains(searchText, StringComparison.OrdinalIgnoreCase)) return true;
		if (map.PathName.Contains(searchText, StringComparison.OrdinalIgnoreCase)) return true;
		if (map.Tags == null) return false;

		foreach (string mapTag in map.Tags)
		{
			if (!string.IsNullOrWhiteSpace(mapTag) && mapTag.Contains(searchText, StringComparison.OrdinalIgnoreCase)) return true;
		}

		return false;
	}

	private static bool HasAnyTag(MapBriefingDetails map, List<string> requiredTags)
	{
		if (map.Tags == null) return false;

		foreach (string mapTag in map.Tags)
		{
			if (string.IsNullOrWhiteSpace(mapTag)) continue;

			foreach (string requiredTag in requiredTags)
			{
				if (string.Equals(mapTag.Trim(), requiredTag, StringComparison.OrdinalIgnoreCase)) return true;
			}
		}

		return false;
	}

	private Button CreateMapPickerTile(MapBriefingDetails map, int mapIndex)
	{
		var tile = new Button();
		tile.Name = "MapPickerTile_" + map.PathName;
		tile.Text = map.DisplayName;
		tile.TooltipText = DescribeMapForPicker(map);
		tile.CustomMinimumSize = new Vector2(320, 44);
		tile.SizeFlagsHorizontal = SizeFlags.ExpandFill;
		tile.ClipText = true;
		tile.FocusMode = FocusModeEnum.None;
		tile.AddThemeConstantOverride("icon_max_width", 0);
		tile.AddThemeStyleboxOverride("normal", UIStyle.CreateDropdownStyle(false, false));
		tile.AddThemeStyleboxOverride("hover", UIStyle.CreateDropdownStyle(true, false));
		tile.AddThemeStyleboxOverride("pressed", UIStyle.CreateDropdownStyle(false, true));
		tile.AddThemeStyleboxOverride("focus", new StyleBoxEmpty());
		tile.AddThemeColorOverride("font_color", mapIndex == _selectedMapIndex ? UIStyle.ColorCyanGlow : UIStyle.ColorGoldDull);
		tile.AddThemeColorOverride("font_hover_color", UIStyle.ColorGold);
		tile.AddThemeColorOverride("font_pressed_color", UIStyle.ColorCyanGlow);
		tile.AddThemeFontSizeOverride("font_size", 15);
		tile.MouseEntered += () => GameContext.UserInterface?.PlayHoverSound();
		tile.Pressed += () =>
		{
			GameContext.UserInterface?.PlayClickSound();
			SelectMap(mapIndex);
			CloseMapPicker();
		};

		return tile;
	}

	private static string DescribeMapForPicker(MapBriefingDetails map)
	{
		string version = string.IsNullOrWhiteSpace(map.Version) ? "1.0.0" : map.Version.TrimStart('v', 'V');
		string description = $"{map.DisplayName}\n{string.Format(TranslationServer.Translate("Version: {0}"), version)}";

		if (map.Tags != null)
		{
			var shownTags = new List<string>();
			foreach (string mapTag in map.Tags)
			{
				if (!string.IsNullOrWhiteSpace(mapTag)) shownTags.Add(mapTag.Trim());
			}

			if (shownTags.Count > 0) description += $"\n{string.Join(", ", shownTags)}";
		}

		return description;
	}

	private string GetMapThumbnailPath(MapBriefingDetails selectedMap)
	{
		if (!string.IsNullOrEmpty(selectedMap.ThumbnailPath) && File.Exists(selectedMap.ThumbnailPath))
		{
			return selectedMap.ThumbnailPath;
		}

		return MapInfoHelper.FindThumbnailForMap(selectedMap.PathName, selectedMap.Version);
	}

	private void LoadThumbnailTexture(string thumbPath)
	{
		if (string.IsNullOrEmpty(thumbPath) || !File.Exists(thumbPath))
		{
			_mapThumbnail.Texture = UIStyle.EmptyBlackTexture;
			return;
		}

		try
		{
			var img = Image.LoadFromFile(thumbPath);
			if (img != null && !img.IsEmpty())
			{
				_mapThumbnail.Texture = ImageTexture.CreateFromImage(img);
				_mapThumbnail.Modulate = Colors.White;
			}
			else
			{
				_mapThumbnail.Texture = UIStyle.EmptyBlackTexture;
			}
		}
		catch
		{
			_mapThumbnail.Texture = UIStyle.EmptyBlackTexture;
		}
	}

	private void RefreshVersionsForSelectedMap(MapBriefingDetails selectedMap, string? targetVersion = null)
	{
		_availableVersions = MapInfoHelper.GetDownloadedVersionsForMap(selectedMap.PathName, selectedMap.DisplayName);
		_versionSelectButton.Clear();
		int selectedVersionIdx = 0;

		for (int i = 0; i < _availableVersions.Count; i++)
		{
			string ver = _availableVersions[i];
			string label = !string.IsNullOrWhiteSpace(ver) ? $"v{ver.TrimStart('v', 'V')}" : "v1.0.0";
			_versionSelectButton.AddItem(label, i);

			if (!string.IsNullOrEmpty(targetVersion))
			{
				string cleanTarget = targetVersion.TrimStart('v', 'V');
				if (ver.Contains(cleanTarget, StringComparison.OrdinalIgnoreCase) || label.Contains(cleanTarget, StringComparison.OrdinalIgnoreCase))
				{
					selectedVersionIdx = i;
				}
			}
		}

		if (_versionSelectButton.ItemCount > 0)
		{
			_versionSelectButton.Selected = selectedVersionIdx;
		}
	}

	private void PopulateRunicPillar(VBoxContainer container)
	{
		container.Visible = false;
	}
}
