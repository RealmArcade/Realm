using Godot;
using Realm.Client.Network;
using System;
using System.Collections.Generic;

namespace Realm.Client.UI;

public partial class LobbyCreate : Control
{
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
		cardBg.Texture = GD.Load<Texture2D>("res://Assets/UI/custom_match_card.png");
		cardBg.ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize;
		cardBg.StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered;
		cardBg.TextureFilter = CanvasItem.TextureFilterEnum.LinearWithMipmaps;
		
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
		mapSelectHBox.SizeFlagsHorizontal = Control.SizeFlags.ShrinkCenter;

		var contentContainer = _mapSelectButton.GetParent();
		int mapSelectIndex = _mapSelectButton.GetIndex();
		contentContainer.RemoveChild(_mapSelectButton);
		contentContainer.AddChild(mapSelectHBox);
		contentContainer.MoveChild(mapSelectHBox, mapSelectIndex);

		_mapSelectButton.CustomMinimumSize = new Vector2(380, 44);
		_mapSelectButton.SizeFlagsHorizontal = Control.SizeFlags.ShrinkCenter;
		mapSelectHBox.AddChild(_mapSelectButton);

		_importMapButton = new Button();
		_importMapButton.Name = "ImportMapButton";
		_importMapButton.AddThemeConstantOverride("icon_max_width", 24);
		_importMapButton.CustomMinimumSize = new Vector2(210, 44);
		UIStyle.ApplyButtonText(_importMapButton, "📥 " + TranslationServer.Translate("IMPORT MAP (.ZIP / .7Z)"), 13);
		_importMapButton.AddThemeStyleboxOverride("normal", UIStyle.CreateButtonNormal());
		_importMapButton.AddThemeStyleboxOverride("hover", UIStyle.CreateButtonHover());
		_importMapButton.AddThemeStyleboxOverride("pressed", UIStyle.CreateButtonPressed());
		_importMapButton.FocusMode = FocusModeEnum.None;
		_importMapButton.MouseFilter = MouseFilterEnum.Stop;
		_importMapButton.MouseEntered += () => UIManager.Instance?.PlayHoverSound();
		_importMapButton.Pressed += () =>
		{
			UIManager.Instance?.PlayClickSound();
			UIManager.Instance?.PromptAndImportMapArchive((mapTitle, mapVersion) =>
			{
				RefreshAvailableMaps(mapTitle, mapVersion);
			});
		};
		mapSelectHBox.AddChild(_importMapButton);

		_versionContainer = new HBoxContainer();
		_versionContainer.Name = "VersionContainer";
		_versionContainer.Alignment = BoxContainer.AlignmentMode.Center;
		_versionContainer.AddThemeConstantOverride("separation", 10);
		_versionContainer.CustomMinimumSize = new Vector2(400, 44);
		_versionContainer.SizeFlagsHorizontal = Control.SizeFlags.ShrinkCenter;

		_versionLabel = new Label();
		_versionLabel.Text = Tr("VERSION:");
		_versionLabel.VerticalAlignment = VerticalAlignment.Center;
		_versionContainer.AddChild(_versionLabel);

		_versionSelectButton = new OptionButton();
		_versionSelectButton.Name = "VersionSelectButton";
		_versionSelectButton.CustomMinimumSize = new Vector2(180, 44);
		_versionSelectButton.SizeFlagsHorizontal = Control.SizeFlags.ShrinkCenter;
		_versionSelectButton.ItemSelected += (idx) => UIManager.Instance.PlayClickSound();
		_versionContainer.AddChild(_versionSelectButton);

		contentContainer.AddChild(_versionContainer);
		contentContainer.MoveChild(_versionContainer, mapSelectHBox.GetIndex() + 1);

		var textPanelWrapper = GetNode<PanelContainer>("CentralPanel/ContentContainer/BriefingPanel/TextPanelWrapper");
		textPanelWrapper.AddThemeStyleboxOverride("panel", UIStyle.CreateBackdropPanel());

		var spacer = new Control { CustomMinimumSize = new Vector2(0, 16) };
		_createButton.GetParent().AddChild(spacer);
		_createButton.GetParent().MoveChild(spacer, _createButton.GetIndex());

		_mapThumbnail = GetNode<TextureRect>("CentralPanel/ContentContainer/BriefingPanel/MapThumbnail");

		var briefingHBox = new HBoxContainer();
		briefingHBox.Name = "BriefingHBox";
		briefingHBox.AddThemeConstantOverride("separation", 16);
		briefingHBox.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
		briefingHBox.SizeFlagsVertical = Control.SizeFlags.ExpandFill;

		_briefingPanel.RemoveChild(_mapThumbnail);
		_briefingPanel.RemoveChild(textPanelWrapper);

		var thumbFrame = new PanelContainer();
		thumbFrame.Name = "ThumbnailFrame";
		thumbFrame.CustomMinimumSize = new Vector2(256, 256);
		thumbFrame.SizeFlagsHorizontal = Control.SizeFlags.ShrinkCenter;
		thumbFrame.SizeFlagsVertical = Control.SizeFlags.ShrinkCenter;
		thumbFrame.AddThemeStyleboxOverride("panel", UIStyle.CreateBackdropPanel());

		_mapThumbnail.CustomMinimumSize = new Vector2(240, 240);
		_mapThumbnail.ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize;
		_mapThumbnail.StretchMode = TextureRect.StretchModeEnum.KeepAspectCovered;
		_mapThumbnail.Modulate = Colors.White;
		_mapThumbnail.Texture = UIStyle.EmptyBlackTexture;

		thumbFrame.AddChild(_mapThumbnail);
		briefingHBox.AddChild(thumbFrame);

		textPanelWrapper.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
		textPanelWrapper.SizeFlagsVertical = Control.SizeFlags.ExpandFill;
		briefingHBox.AddChild(textPanelWrapper);

		_briefingPanel.AddChild(briefingHBox);

		_titleLabel = GetNode<Label>("Title");
		_mapSelectLabel = GetNode<Label>("CentralPanel/ContentContainer/MapSelectLabel");

		ApplyThemeStyles();

		_mapSelectButton.ItemSelected += (idx) => OnMapSelected(idx);
		RefreshAvailableMaps();
	}

	private void ApplyThemeStyles()
	{
		_bgPanel.AddThemeStyleboxOverride("panel", UIStyle.CreateCustomMatchBg());
		_bgPanel.TextureFilter = CanvasItem.TextureFilterEnum.LinearWithMipmaps;
		_leftPillar.AddThemeStyleboxOverride("panel", UIStyle.CreatePillarPanel(true));
		_rightPillar.AddThemeStyleboxOverride("panel", UIStyle.CreatePillarPanel(false));
		_centralPanel.AddThemeStyleboxOverride("panel", UIStyle.CreateCustomMatchCardPanel());
		_centralPanel.TextureFilter = CanvasItem.TextureFilterEnum.LinearWithMipmaps;
		_briefingPanel.AddThemeStyleboxOverride("panel", UIStyle.CreateStonePanel(false));

		UIStyle.ApplyTitle(_titleLabel, Network.LobbyManager.Instance.IsSinglePlayer ? "SINGLE PLAYER" : "CREATE CUSTOM MATCH", 36);
		UIStyle.ApplyTitle(_mapSelectLabel, "SELECT MAP", 20);

		SetupPillarButton(_backButton, "◀", () => UIManager.Instance.TransitionTo(Network.LobbyManager.Instance.IsSinglePlayer ? GameScreen.MainMenu : GameScreen.LobbyBrowser));
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

		_briefingText.AddThemeColorOverride("default_color", new Color(0.85f, 0.85f, 0.9f));
		_briefingText.AddThemeFontSizeOverride("normal_font_size", 14);

		PopulateRunicPillar(GetNode<VBoxContainer>("LeftPillar/RuneContainer"));
		PopulateRunicPillar(GetNode<VBoxContainer>("RightPillar/RuneContainer"));
	}

	private void SetupPillarButton(Button btn, string text, Action onClick)
	{
		btn.Flat = false;
		btn.Text = text;
		btn.AddThemeConstantOverride("icon_max_width", 0);
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

	private void SetupCreateButton()
	{
		_createButton.Flat = false;
		_createButton.AddThemeConstantOverride("icon_max_width", 0);
		UIStyle.ApplyButtonText(_createButton, Network.LobbyManager.Instance.IsSinglePlayer ? "START GAME" : "CREATE LOBBY", 18);
		
		_createButton.AddThemeStyleboxOverride("normal", UIStyle.CreateCustomLobbyStartGameButton(false, false));
		_createButton.AddThemeStyleboxOverride("hover", UIStyle.CreateCustomLobbyStartGameButton(true, false));
		_createButton.AddThemeStyleboxOverride("pressed", UIStyle.CreateCustomLobbyStartGameButton(false, true));
		_createButton.AddThemeStyleboxOverride("focus", new StyleBoxEmpty());

		_createButton.Pressed += OnCreatePressed;
		_createButton.MouseEntered += () => UIManager.Instance.PlayHoverSound();
	}

	private async void OnCreatePressed()
	{
		UIManager.Instance.PlayClickSound();

		string selectedVersion = GetSelectedVersion();
		var mapInfo = GetSelectedMapInfo();

		if (IsMapBuildMismatch(mapInfo.BuildNumber))
		{
			UIManager.Instance.PlayWarningSound();
			ShowMapBuildMismatchModal(mapInfo.PathName, mapInfo.DisplayName, mapInfo.BuildNumber, Realm.Shared.RealmVersion.GameBuildNumber);
			return;
		}

		if (Network.LobbyManager.Instance.IsSinglePlayer)
		{
			_createButton.Disabled = true;
			Network.LobbyManager.Instance.HostSinglePlayerGame(mapInfo.PathName, mapInfo.DisplayName, selectedVersion);
			_createButton.Disabled = false;
			return;
		}

		if (Network.LobbyManager.Instance.LocalNatType == NatType.Symmetric)
		{
			UIManager.Instance.PlayWarningSound();
			ShowSTUNErrorModal();
			return;
		}
			
		_createButton.Disabled = true;
		bool success = await Network.LobbyManager.Instance.HostLobbyAsync(mapInfo.PathName, mapInfo.DisplayName, selectedVersion);
		_createButton.Disabled = false;

		if (success)
		{
			UIManager.Instance.TransitionTo(GameScreen.LobbyRoom);
		}
		else
		{
			UIManager.Instance.PlayWarningSound();
			GD.PrintErr("[LobbyCreate] Failed to host lobby.");
			string errorMsg = Network.LobbyManager.Instance.LastHostError ?? Tr("Failed to host lobby on the registry server.");
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
		return ("melee", "Melee Battlefield", Realm.Shared.RealmVersion.GameBuildNumber);
	}

	private bool IsMapBuildMismatch(string mapBuildNumber)
	{
		return !string.IsNullOrEmpty(mapBuildNumber) && 
		       !string.Equals(mapBuildNumber, Realm.Shared.RealmVersion.GameBuildNumber, StringComparison.OrdinalIgnoreCase);
	}

	private void ShowMapBuildMismatchModal(string mapPath, string mapDisplayName, string mapBuildNumber, string currentBuildNumber)
	{
		var warningPopup = new Panel();
		warningPopup.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
		warningPopup.AddThemeStyleboxOverride("panel", UIStyle.CreateBgGradient());
		AddChild(warningPopup);

		var cardPanel = new Panel();
		cardPanel.CustomMinimumSize = new Vector2(520, 290);
		cardPanel.SetAnchorsAndOffsetsPreset(LayoutPreset.Center);
		cardPanel.AddThemeStyleboxOverride("panel", UIStyle.CreateStonePanel(true));
		warningPopup.AddChild(cardPanel);

		var vbox = new VBoxContainer();
		vbox.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
		vbox.CustomMinimumSize = new Vector2(480, 260);
		vbox.SizeFlagsHorizontal = SizeFlags.ExpandFill;
		vbox.SizeFlagsVertical = SizeFlags.ExpandFill;
		cardPanel.AddChild(vbox);

		vbox.AddChild(new Control { CustomMinimumSize = new Vector2(0, 15) });

		var titleLabel = new Label();
		UIStyle.ApplyTitle(titleLabel, Tr("MAP BUILD MISMATCH"), 20);
		titleLabel.AddThemeColorOverride("font_color", new Color(0.95f, 0.4f, 0.3f));
		vbox.AddChild(titleLabel);

		vbox.AddChild(new Control { CustomMinimumSize = new Vector2(0, 8) });

		bool versionExistsLocally = System.IO.File.Exists(Network.LobbyManager.GetVersionExecutablePath(mapBuildNumber));

		var descLabel = new Label();
		string promptText = Tr("This map is out of date. To host this match, download the matching game build or open the map in the Map Editor to upgrade it.");
		descLabel.Text = $"{string.Format(Tr("Map: {0}"), mapDisplayName)}\n{string.Format(Tr("Map Build: {0}"), mapBuildNumber)} | {string.Format(Tr("Current Build: {0}"), currentBuildNumber)}\n\n{promptText}";
		descLabel.HorizontalAlignment = HorizontalAlignment.Center;
		descLabel.AddThemeFontSizeOverride("font_size", 13);
		descLabel.AddThemeColorOverride("font_color", new Color(0.9f, 0.9f, 0.95f));
		vbox.AddChild(descLabel);

		vbox.AddChild(new Control { CustomMinimumSize = new Vector2(0, 15) });

		var hBox = new HBoxContainer();
		hBox.Alignment = BoxContainer.AlignmentMode.Center;
		hBox.AddThemeConstantOverride("separation", 12);
		vbox.AddChild(hBox);

		var editorBtn = new Button();
		editorBtn.Flat = false;
		editorBtn.AddThemeConstantOverride("icon_max_width", 0);
		editorBtn.AddThemeStyleboxOverride("normal", UIStyle.CreateButtonNormal());
		editorBtn.AddThemeStyleboxOverride("hover", UIStyle.CreateButtonHover());
		editorBtn.AddThemeStyleboxOverride("pressed", UIStyle.CreateButtonPressed());
		editorBtn.AddThemeStyleboxOverride("focus", new StyleBoxEmpty());
		UIStyle.ApplyButtonText(editorBtn, Tr("OPEN MAP EDITOR"), 13);
		editorBtn.CustomMinimumSize = new Vector2(140, 38);
		editorBtn.Pressed += () =>
		{
			UIManager.Instance.PlayClickSound();
			warningPopup.QueueFree();
			UIManager.Instance.TransitionTo(GameScreen.MapEditorHUD);
		};
		hBox.AddChild(editorBtn);

		var versionBtn = new Button();
		versionBtn.Flat = false;
		versionBtn.AddThemeConstantOverride("icon_max_width", 0);
		versionBtn.AddThemeStyleboxOverride("normal", UIStyle.CreateButtonNormal());
		versionBtn.AddThemeStyleboxOverride("hover", UIStyle.CreateButtonHover());
		versionBtn.AddThemeStyleboxOverride("pressed", UIStyle.CreateButtonPressed());
		versionBtn.AddThemeStyleboxOverride("focus", new StyleBoxEmpty());
		string btnText = versionExistsLocally ? string.Format(Tr("LAUNCH {0}"), mapBuildNumber) : string.Format(Tr("DOWNLOAD {0}"), mapBuildNumber);
		UIStyle.ApplyButtonText(versionBtn, btnText, 13);
		versionBtn.CustomMinimumSize = new Vector2(140, 38);
		versionBtn.Pressed += () =>
		{
			UIManager.Instance.PlayClickSound();
			warningPopup.QueueFree();
			if (versionExistsLocally)
			{
				string exePath = Network.LobbyManager.GetVersionExecutablePath(mapBuildNumber);
				OS.CreateProcess(exePath, Array.Empty<string>());
				GetTree().Quit();
			}
		};
		hBox.AddChild(versionBtn);

		var cancelBtn = new Button();
		cancelBtn.Flat = false;
		cancelBtn.AddThemeConstantOverride("icon_max_width", 0);
		cancelBtn.AddThemeStyleboxOverride("normal", UIStyle.CreateButtonNormal());
		cancelBtn.AddThemeStyleboxOverride("hover", UIStyle.CreateButtonHover());
		cancelBtn.AddThemeStyleboxOverride("pressed", UIStyle.CreateButtonPressed());
		cancelBtn.AddThemeStyleboxOverride("focus", new StyleBoxEmpty());
		UIStyle.ApplyButtonText(cancelBtn, Tr("CANCEL"), 13);
		cancelBtn.CustomMinimumSize = new Vector2(100, 38);
		cancelBtn.Pressed += () =>
		{
			UIManager.Instance.PlayClickSound();
			warningPopup.QueueFree();
		};
		hBox.AddChild(cancelBtn);
	}

	private void ShowSTUNErrorModal()
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
		descLabel.Text = Tr("STUN test determines you cannot host a game due to a Symmetric NAT router configuration. Please configure port forwarding or UPnP.");
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
			UIManager.Instance.TransitionTo(GameScreen.LobbyBrowser);
		};
		vbox.AddChild(okBtn);
	}

	private void ShowHostErrorModal(string errorMessage)
	{
		var warningPopup = new Panel();
		warningPopup.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
		warningPopup.AddThemeStyleboxOverride("panel", UIStyle.CreateBgGradient());
		AddChild(warningPopup);

		var cardPanel = new Panel();
		cardPanel.CustomMinimumSize = new Vector2(480, 240);
		cardPanel.SetAnchorsAndOffsetsPreset(LayoutPreset.Center);
		cardPanel.AddThemeStyleboxOverride("panel", UIStyle.CreateStonePanel(true));
		warningPopup.AddChild(cardPanel);

		var vbox = new VBoxContainer();
		vbox.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
		vbox.CustomMinimumSize = new Vector2(440, 200);
		vbox.SizeFlagsHorizontal = SizeFlags.ExpandFill;
		vbox.SizeFlagsVertical = SizeFlags.ExpandFill;
		cardPanel.AddChild(vbox);

		vbox.AddChild(new Control { CustomMinimumSize = new Vector2(0, 20) });

		var titleLabel = new Label();
		UIStyle.ApplyTitle(titleLabel, Tr("LOBBY CREATION ERROR"), 20);
		titleLabel.AddThemeColorOverride("font_color", new Color(0.95f, 0.35f, 0.35f));
		vbox.AddChild(titleLabel);

		vbox.AddChild(new Control { CustomMinimumSize = new Vector2(0, 10) });

		var descLabel = new Label();
		descLabel.Text = errorMessage;
		descLabel.HorizontalAlignment = HorizontalAlignment.Center;
		descLabel.AutowrapMode = TextServer.AutowrapMode.WordSmart;
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
		UIManager.Instance?.PlayClickSound();
			
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
		if (!string.IsNullOrEmpty(selectedMap.ThumbnailPath) && System.IO.File.Exists(selectedMap.ThumbnailPath))
		{
			return selectedMap.ThumbnailPath;
		}
		return MapInfoHelper.FindThumbnailForMap(selectedMap.PathName, selectedMap.Version);
	}

	private void LoadThumbnailTexture(string thumbPath)
	{
		if (string.IsNullOrEmpty(thumbPath) || !System.IO.File.Exists(thumbPath))
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