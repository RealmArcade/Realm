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
	private Button _importMapButton;
	private OptionButton _mapSelectButton;
	private OptionButton _versionSelectButton;
	private HBoxContainer _versionContainer;
	private Label _versionLabel;
	private RichTextLabel _briefingText;
	private TextureRect _mapThumbnail;

	private Label _titleLabel;
	private Label _mapSelectLabel;

	private List<MapBriefingDetails> _availableMaps = new List<MapBriefingDetails>();
	private List<string> _availableVersions = new List<string>();

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
		_mapSelectButton = GetNode<OptionButton>("CentralPanel/ContentContainer/MapSelectButton");
		_briefingPanel = GetNode<PanelContainer>("CentralPanel/ContentContainer/BriefingPanel");
		_briefingText = GetNode<RichTextLabel>("CentralPanel/ContentContainer/BriefingPanel/TextPanelWrapper/BriefingText");

		var mapSelectHBox = new HBoxContainer();
		mapSelectHBox.Name = "MapSelectHBox";
		mapSelectHBox.Alignment = BoxContainer.AlignmentMode.Center;
		mapSelectHBox.AddThemeConstantOverride("separation", 10);
		mapSelectHBox.CustomMinimumSize = new Vector2(620, 44);
		mapSelectHBox.SizeFlagsHorizontal = SizeFlags.ShrinkCenter;

		var contentContainer = _mapSelectButton.GetParent();
		int mapSelectIndex = _mapSelectButton.GetIndex();
		contentContainer.RemoveChild(_mapSelectButton);
		contentContainer.AddChild(mapSelectHBox);
		contentContainer.MoveChild(mapSelectHBox, mapSelectIndex);

		_mapSelectButton.CustomMinimumSize = new Vector2(380, 44);
		_mapSelectButton.SizeFlagsHorizontal = SizeFlags.ShrinkCenter;
		mapSelectHBox.AddChild(_mapSelectButton);

		_importMapButton = new Button();
		_importMapButton.Name = "ImportMapButton";
		_importMapButton.AddThemeConstantOverride("icon_max_width", 24);
		_importMapButton.CustomMinimumSize = new Vector2(210, 44);
		UIStyle.ApplyButtonText(_importMapButton, "📥 " + TranslationServer.Translate("Import Map (.rmap)"), 13);
		_importMapButton.AddThemeStyleboxOverride("normal", UIStyle.CreateButtonNormal());
		_importMapButton.AddThemeStyleboxOverride("hover", UIStyle.CreateButtonHover());
		_importMapButton.AddThemeStyleboxOverride("pressed", UIStyle.CreateButtonPressed());
		_importMapButton.FocusMode = FocusModeEnum.None;
		_importMapButton.MouseFilter = MouseFilterEnum.Stop;
		_importMapButton.MouseEntered += () => GameContext.UserInterface?.PlayHoverSound();
		_importMapButton.Pressed += () =>
		{
			GameContext.UserInterface?.PlayClickSound();
			GameContext.UserInterface?.PromptAndImportMapArchive((mapTitle, mapVersion) => { RefreshAvailableMaps(mapTitle, mapVersion); });
		};
		mapSelectHBox.AddChild(_importMapButton);

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
		contentContainer.MoveChild(_versionContainer, mapSelectHBox.GetIndex() + 1);

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

		briefingVBox.AddChild(textPanelWrapper);

		_briefingPanel.AddChild(briefingVBox);

		_titleLabel = GetNode<Label>("Title");
		_mapSelectLabel = GetNode<Label>("CentralPanel/ContentContainer/MapSelectLabel");

		ApplyThemeStyles();

		_mapSelectButton.ItemSelected += (idx) => OnMapSelected(idx);
		RefreshAvailableMaps();
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

		UIStyle.ApplyTitle(_titleLabel, GameContext.Lobby.IsSinglePlayer ? "SINGLE PLAYER" : "CREATE CUSTOM MATCH", 36);
		UIStyle.ApplyTitle(_mapSelectLabel, "SELECT MAP", 20);

		Realm.Client.Utils.UIHelper.SetupPillarButton(_backButton, "◀", () => GameContext.UserInterface.TransitionTo(GameContext.Lobby.IsSinglePlayer ? GameScreen.MainMenu : GameScreen.LobbyBrowser));
		SetupCreateButton();

		_mapSelectButton.AddThemeStyleboxOverride("normal", UIStyle.CreateDropdownStyle(false, false));
		_mapSelectButton.AddThemeStyleboxOverride("hover", UIStyle.CreateDropdownStyle(true, false));
		_mapSelectButton.AddThemeStyleboxOverride("pressed", UIStyle.CreateDropdownStyle(false, true));
		_mapSelectButton.AddThemeStyleboxOverride("focus", new StyleBoxEmpty());
		_mapSelectButton.AddThemeColorOverride("font_color", UIStyle.ColorGoldDull);
		_mapSelectButton.AddThemeColorOverride("font_hover_color", UIStyle.ColorGold);
		_mapSelectButton.AddThemeColorOverride("font_pressed_color", UIStyle.ColorCyanGlow);
		_mapSelectButton.AddThemeFontSizeOverride("font_size", 16);

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
		UIStyle.ApplyButtonText(_createButton, GameContext.Lobby.IsSinglePlayer ? "START GAME" : "CREATE LOBBY", 18);

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
		if (GameContext.Lobby.IsSinglePlayer)
		{
			_createButton.Disabled = true;
			bool started = await GameContext.Lobby.HostSinglePlayerGame(pathName, displayName, selectedVersion);
			_createButton.Disabled = false;

			if (started) return;

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
		bool success = await GameContext.Lobby.HostLobbyAsync(pathName, displayName, selectedVersion);
		_createButton.Disabled = false;

		if (success)
		{
			GameContext.UserInterface.TransitionTo(GameScreen.LobbyRoom);
		}
		else
		{
			GameContext.UserInterface.PlayWarningSound();
			GD.PrintErr("[LobbyCreate] Failed to host lobby.");
			string errorMsg = GameContext.Lobby.LastHostError ?? Tr("Failed to host lobby on the registry server.");
			ShowHostErrorModal(errorMsg);
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
		int selectedIndex = _mapSelectButton.Selected;
		if (selectedIndex >= 0 && selectedIndex < _availableMaps.Count)
		{
			var map = _availableMaps[selectedIndex];
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
		GameContext.UserInterface?.ShowPopupMessage("HOSTING ERROR", "STUN test determines you cannot host a game due to a Symmetric NAT router configuration. Please configure port forwarding or UPnP.", () =>
		{
			GameContext.UserInterface.TransitionTo(GameScreen.LobbyBrowser);
		});
	}

	private void ShowHostErrorModal(string errorMessage)
	{
		GameContext.UserInterface?.ShowPopupMessage("LOBBY CREATION ERROR", errorMessage);
	}

	private void RefreshAvailableMaps(string? selectMapName = null, string? selectMapVersion = null)
	{
		_availableMaps = MapInfoHelper.GetAvailableMaps();
		_mapSelectButton.Clear();
		int targetIndex = 0;

		for (int i = 0; i < _availableMaps.Count; i++)
		{
			var map = _availableMaps[i];
			_mapSelectButton.AddItem(map.DisplayName);

			if (!string.IsNullOrEmpty(selectMapName) &&
			    (string.Equals(map.DisplayName, selectMapName, StringComparison.OrdinalIgnoreCase) ||
			     string.Equals(map.PathName, selectMapName, StringComparison.OrdinalIgnoreCase)))
			{
				targetIndex = i;
			}
		}

		if (_mapSelectButton.ItemCount > 0)
		{
			_mapSelectButton.Selected = targetIndex;
			OnMapSelected(targetIndex, selectMapVersion);
		}
	}

	private void OnMapSelected(long index)
	{
		OnMapSelected(index, null);
	}

	private void OnMapSelected(long index, string? targetVersion)
	{
		GameContext.UserInterface?.PlayClickSound();

		if (index < 0 || index >= _availableMaps.Count)
		{
			return;
		}

		var selectedMap = _availableMaps[(int)index];
		_briefingText.Text = selectedMap.Description;
		RefreshVersionsForSelectedMap(selectedMap, targetVersion);

		string thumbPath = GetMapThumbnailPath(selectedMap);
		LoadThumbnailTexture(thumbPath);
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
