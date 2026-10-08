using Godot;
using System;
using System.Collections.Generic;
using System.Linq;
using Realm.Shared.Distribution;

public partial class MapDetails : Control
{
	private Panel _bgPanel;
	private Panel _leftPillar;
	private Panel _rightPillar;
	
	private Button _backButton;
	

	private PanelContainer _headerPanel;
	private Label _titleLabel;
	private Label _creatorLabel;


	private TextureRect _texLeftPeek;
	private TextureRect _texCenter;
	private TextureRect _texRightPeek;
	private Button _btnLeftArrow;
	private Button _btnRightArrow;
	private HSlider _carouselSlider;


	private PanelContainer _descriptionPanel;
	private Label _descTitle;
	private Label _descText;


	private PanelContainer _featuresPanel;
	private Label _featuresTitle;
	private VBoxContainer _featuresList;


	private PanelContainer _statsPanel;
	private Label _statsTitle;
	
	private Label _ratingTitle;
	private HBoxContainer _starsRow1;
	private Label _votesRow1;
	private HBoxContainer _starsRow2;
	private Label _votesRow2;
	private HBoxContainer _starsRow3;
	private Label _votesRow3;

	private Label _awardsTitle;
	private HBoxContainer _awardsContainer;

	private Label _gameplayTitle;
	private VBoxContainer _gameplayStatsContainer;

	private Label _techTitle;
	private VBoxContainer _techInfoContainer;

	private Button _downloadButton;
	private Label _downloadSubtitle;
	private OptionButton _versionDropdown;
	private HBoxContainer _versionContainer;
	private Label _versionLabel;

	private MapData _mapData;
	private int _carouselIndex = 0;
	private bool _isDownloading = false;
	private float _downloadProgress = 0.0f;

	private ScrollContainer _statsScrollContainer;
	private float _targetScrollVertical = 0.0f;
	private ScrollContainer _descScrollContainer;
	private float _targetDescScrollVertical = 0.0f;
	private ScrollContainer _featuresScrollContainer;
	private float _targetFeaturesScrollVertical = 0.0f;
	private float _pulseTimer = 0.0f;

	public override void _Ready()
	{

		_bgPanel = GetNode<Panel>("Background");
		_leftPillar = GetNode<Panel>("LeftPillar");
		_rightPillar = GetNode<Panel>("RightPillar");
		
		_backButton = GetNode<Button>("BackButton");


		_headerPanel = GetNode<PanelContainer>("HeaderPanel");
		_titleLabel = GetNode<Label>("HeaderPanel/VBoxContainer/TitleLabel");
		_creatorLabel = GetNode<Label>("HeaderPanel/VBoxContainer/CreatorLabel");


		_texLeftPeek = GetNode<TextureRect>("CarouselPanel/LeftPeek");
		_texCenter = GetNode<TextureRect>("CarouselPanel/CenterScreenshot");
		_texRightPeek = GetNode<TextureRect>("CarouselPanel/RightPeek");
		_btnLeftArrow = GetNode<Button>("CarouselPanel/LeftArrow");
		_btnRightArrow = GetNode<Button>("CarouselPanel/RightArrow");
		_carouselSlider = GetNode<HSlider>("CarouselPanel/CarouselSlider");


		_descriptionPanel = GetNode<PanelContainer>("DescriptionPanel");
		_descTitle = GetNode<Label>("DescriptionPanel/VBoxContainer/DescTitle");
		_descText = GetNode<Label>("DescriptionPanel/VBoxContainer/ScrollContainer/DescText");
		_descScrollContainer = GetNodeOrNull<ScrollContainer>("DescriptionPanel/VBoxContainer/ScrollContainer");


		_featuresPanel = GetNode<PanelContainer>("FeaturesPanel");
		_featuresTitle = GetNode<Label>("FeaturesPanel/VBoxContainer/FeaturesTitle");
		_featuresList = GetNode<VBoxContainer>("FeaturesPanel/VBoxContainer/ScrollContainer/FeaturesList");
		_featuresScrollContainer = GetNodeOrNull<ScrollContainer>("FeaturesPanel/VBoxContainer/ScrollContainer");


		_statsPanel = GetNode<PanelContainer>("StatsPanel");
		_statsTitle = GetNode<Label>("StatsPanel/VBoxContainer/StatsTitle");
		_statsScrollContainer = GetNodeOrNull<ScrollContainer>("StatsPanel/VBoxContainer/ScrollContainer");

		_ratingTitle = GetNode<Label>("StatsPanel/VBoxContainer/ScrollContainer/ContentVBox/RatingTitle");
		_starsRow1 = GetNode<HBoxContainer>("StatsPanel/VBoxContainer/ScrollContainer/ContentVBox/RatingGrid/Row1Stars");
		_votesRow1 = GetNode<Label>("StatsPanel/VBoxContainer/ScrollContainer/ContentVBox/RatingGrid/Row1Votes");
		_starsRow2 = GetNode<HBoxContainer>("StatsPanel/VBoxContainer/ScrollContainer/ContentVBox/RatingGrid/Row2Stars");
		_votesRow2 = GetNode<Label>("StatsPanel/VBoxContainer/ScrollContainer/ContentVBox/RatingGrid/Row2Votes");
		_starsRow3 = GetNode<HBoxContainer>("StatsPanel/VBoxContainer/ScrollContainer/ContentVBox/RatingGrid/Row3Stars");
		_votesRow3 = GetNode<Label>("StatsPanel/VBoxContainer/ScrollContainer/ContentVBox/RatingGrid/Row3Votes");

		_awardsTitle = GetNode<Label>("StatsPanel/VBoxContainer/ScrollContainer/ContentVBox/AwardsTitle");
		_awardsContainer = GetNode<HBoxContainer>("StatsPanel/VBoxContainer/ScrollContainer/ContentVBox/AwardsContainer");

		_gameplayTitle = GetNode<Label>("StatsPanel/VBoxContainer/ScrollContainer/ContentVBox/GameplayTitle");
		_gameplayStatsContainer = GetNode<VBoxContainer>("StatsPanel/VBoxContainer/ScrollContainer/ContentVBox/GameplayStatsContainer");

		_techTitle = GetNode<Label>("StatsPanel/VBoxContainer/ScrollContainer/ContentVBox/TechTitle");
		_techInfoContainer = GetNode<VBoxContainer>("StatsPanel/VBoxContainer/ScrollContainer/ContentVBox/TechInfoContainer");

		_downloadButton = GetNode<Button>("StatsPanel/VBoxContainer/DownloadButton");
		_downloadSubtitle = GetNode<Label>("StatsPanel/VBoxContainer/DownloadSubtitle");

		_versionContainer = new HBoxContainer();
		_versionContainer.Name = "VersionDropdownContainer";
		_versionContainer.Alignment = BoxContainer.AlignmentMode.Center;
		_versionContainer.AddThemeConstantOverride("separation", 10);
		_versionContainer.CustomMinimumSize = new Vector2(300, 36);
		_versionContainer.SizeFlagsHorizontal = Control.SizeFlags.ShrinkCenter;

		_versionLabel = new Label();
		_versionLabel.Text = Tr("VERSION:");
		_versionLabel.VerticalAlignment = VerticalAlignment.Center;
		_versionLabel.AddThemeColorOverride("font_color", UIStyle.ColorGold);
		_versionLabel.AddThemeFontSizeOverride("font_size", 14);
		_versionContainer.AddChild(_versionLabel);

		_versionDropdown = new OptionButton();
		_versionDropdown.Name = "VersionDropdown";
		_versionDropdown.CustomMinimumSize = new Vector2(160, 36);
		_versionDropdown.SizeFlagsHorizontal = Control.SizeFlags.ShrinkCenter;
		_versionDropdown.AddThemeStyleboxOverride("normal", UIStyle.CreateButtonNormal());
		_versionDropdown.AddThemeStyleboxOverride("hover", UIStyle.CreateButtonHover());
		_versionDropdown.AddThemeStyleboxOverride("pressed", UIStyle.CreateButtonPressed());
		_versionDropdown.AddThemeStyleboxOverride("focus", new StyleBoxEmpty());
		_versionDropdown.AddThemeColorOverride("font_color", UIStyle.ColorGold);
		_versionDropdown.AddThemeColorOverride("font_hover_color", new Color(1.0f, 0.94f, 0.75f));
		_versionDropdown.AddThemeColorOverride("font_pressed_color", UIStyle.ColorGold);
		_versionDropdown.AddThemeColorOverride("font_focus_color", UIStyle.ColorGold);
		_versionDropdown.AddThemeFontSizeOverride("font_size", 14);
		_versionDropdown.TextureFilter = CanvasItem.TextureFilterEnum.LinearWithMipmaps;
		_versionContainer.AddChild(_versionDropdown);

		var downloadParent = _downloadButton.GetParent();
		downloadParent.AddChild(_versionContainer);
		downloadParent.MoveChild(_versionContainer, _downloadButton.GetIndex());

		ApplyStyles();
		RegisterEvents();
	}

	public override void _Process(double delta)
	{
		if (_statsScrollContainer != null && Mathf.Abs(_statsScrollContainer.ScrollVertical - _targetScrollVertical) > 0.5f)
		{
			_statsScrollContainer.ScrollVertical = (int)Mathf.Lerp(_statsScrollContainer.ScrollVertical, _targetScrollVertical, (float)delta * 12.0f);
		}

		if (_descScrollContainer != null && Mathf.Abs(_descScrollContainer.ScrollVertical - _targetDescScrollVertical) > 0.5f)
		{
			_descScrollContainer.ScrollVertical = (int)Mathf.Lerp(_descScrollContainer.ScrollVertical, _targetDescScrollVertical, (float)delta * 12.0f);
		}

		if (_featuresScrollContainer != null && Mathf.Abs(_featuresScrollContainer.ScrollVertical - _targetFeaturesScrollVertical) > 0.5f)
		{
			_featuresScrollContainer.ScrollVertical = (int)Mathf.Lerp(_featuresScrollContainer.ScrollVertical, _targetFeaturesScrollVertical, (float)delta * 12.0f);
		}

		if (_downloadButton != null && !_isDownloading)
		{
			_pulseTimer += (float)delta * 3.0f;
			float pulse = (Mathf.Sin(_pulseTimer) + 1.0f) * 0.5f;
			_downloadButton.Modulate = new Color(1.0f + pulse * 0.15f, 1.0f + pulse * 0.12f, 1.0f + pulse * 0.08f);
		}
	}

	public void SetMapData(MapData mapData)
	{
		_mapData = mapData;
		if (_mapData == null) return;

		if (_mapData.AvailableVersions == null || _mapData.AvailableVersions.Count == 0)
		{
			_mapData.AvailableVersions = new List<MapData> { _mapData };
		}

		_carouselIndex = 0;
		_isDownloading = false;
		_downloadProgress = 0.0f;

		_versionDropdown.Clear();
		int selectedIndex = 0;
		for (int i = 0; i < _mapData.AvailableVersions.Count; i++)
		{
			var vData = _mapData.AvailableVersions[i];
			string vLabel = !string.IsNullOrWhiteSpace(vData.Version) ? $"v{vData.Version}" : "v1.0.0";
			_versionDropdown.AddItem(vLabel, i);
			if (string.Equals(vData.Version, _mapData.Version, StringComparison.OrdinalIgnoreCase) ||
			    string.Equals(vData.MapId, _mapData.MapId, StringComparison.OrdinalIgnoreCase))
			{
				selectedIndex = i;
			}
		}
		_versionDropdown.Select(selectedIndex);

		UpdateVersionDisplay();
	}

	private void OnVersionSelected(int index)
	{
		if (_mapData?.AvailableVersions != null && index >= 0 && index < _mapData.AvailableVersions.Count)
		{
			var selectedVersion = _mapData.AvailableVersions[index];
			selectedVersion.AvailableVersions = _mapData.AvailableVersions;
			_mapData = selectedVersion;
			UpdateVersionDisplay();
		}
	}

	private void UpdateVersionDisplay()
	{
		if (_mapData == null) return;

		UIStyle.ApplyTitle(_titleLabel, $"MAP DETAILS: {_mapData.Title}", 28);
		_creatorLabel.Text = $"By: {_mapData.Creator}";

		_descText.Text = _mapData.Description;

		PopulateFeatures();

		if (_mapData.Screenshots != null && _mapData.Screenshots.Length > 0)
		{
			_carouselSlider.MaxValue = _mapData.Screenshots.Length - 1;
			_carouselSlider.Value = 0;
			UpdateCarousel();
		}

		PopulateRatings();
		PopulateAwards();
		PopulateGameplayStats();
		PopulateTechInfo();

		bool isDownloaded = CheckIfMapAlreadyDownloaded(_mapData);

		_downloadButton.Disabled = false;
		if (isDownloaded)
		{
			UIStyle.ApplyButtonText(_downloadButton, "PLAY MAP", 18);
			_downloadSubtitle.Text = Tr("Ready to Play");
			_downloadSubtitle.AddThemeColorOverride("font_color", UIStyle.ColorCyanGlow);
		}
		else
		{
			UIStyle.ApplyButtonText(_downloadButton, "DOWNLOAD MAP", 18);
			_downloadSubtitle.Text = $"File Size: {_mapData.FileSize}";
			_downloadSubtitle.AddThemeColorOverride("font_color", UIStyle.ColorGoldDull);
		}
	}

	private bool CheckIfMapAlreadyDownloaded(MapData map)
	{
		if (map == null) return false;

		string version = !string.IsNullOrWhiteSpace(map.Version) ? map.Version.Trim() : "1.0.0";

		if (FileAccess.FileExists($"res://Maps/{map.Title}/{version}/manifest.json") ||
		    FileAccess.FileExists($"res://Maps/{map.MapId}/manifest.json"))
		{
			return true;
		}

		if (FileAccess.FileExists($"res://Maps/{map.Title}/manifest.json") && CheckManifestByTitle(map.Title, version))
		{
			return true;
		}

		if (!string.IsNullOrWhiteSpace(map.MapId) && MapAssetManager.IsMapDownloaded(map.MapId))
		{
			return true;
		}

		if (!string.IsNullOrWhiteSpace(map.Title) && MapAssetManager.IsMapDownloaded(map.Title, version))
		{
			return true;
		}

		return false;
	}

	private bool CheckManifestByTitle(string title, string version)
	{
		try
		{
			var mf = MapManifest.LoadFromJson(FileAccess.GetFileAsString($"res://Maps/{title}/manifest.json"));
			return mf != null && (string.Equals(mf.Version, version, StringComparison.OrdinalIgnoreCase) || string.IsNullOrEmpty(mf.Version));
		}
		catch 
		{
			return false;
		}
	}

	private void ApplyStyles()
	{
		ApplyBackgroundStyles();
		ApplyPanelStyles();
		ApplyWrapperStyles();
		ApplyTextStyles();

		SetupPillarButton(_backButton, "◀ BACK", () => UIManager.Instance.TransitionTo(GameScreen.MapDiscovery));

		_downloadButton.AddThemeStyleboxOverride("normal", UIStyle.CreateButtonNormal());
		_downloadButton.AddThemeStyleboxOverride("hover", UIStyle.CreateButtonHover());
		_downloadButton.AddThemeStyleboxOverride("pressed", UIStyle.CreateButtonPressed());
		_downloadButton.AddThemeStyleboxOverride("disabled", UIStyle.CreateButtonPressed());
		_downloadSubtitle.AddThemeColorOverride("font_color", UIStyle.ColorGoldDull);
		_downloadSubtitle.AddThemeFontSizeOverride("font_size", 12);

		ApplyMapFrame();
		ApplyCarouselButtons();

		_carouselSlider.AddThemeStyleboxOverride("slider", UIStyle.CreateSliderTrack());
		_carouselSlider.AddThemeStyleboxOverride("grabber_area", UIStyle.CreateSliderFill());
		_carouselSlider.AddThemeStyleboxOverride("grabber_area_highlight", UIStyle.CreateSliderFill());
	}

	private void ApplyBackgroundStyles()
	{
		Texture2D bgTexture = null;
		string[] bgPaths = { "res://Assets/UI/map_details_bg.png", "res://Assets/UI/map_details_bg.jpg", "res://Assets/UI/map_discovery_background.png" };

		foreach (var path in bgPaths)
		{
			if (ResourceLoader.Exists(path))
			{
				bgTexture = GD.Load<Texture2D>(path);
				if (bgTexture != null) break;
			}
		}

		if (bgTexture != null)
		{
			var style = new StyleBoxTexture { Texture = bgTexture };
			_bgPanel.AddThemeStyleboxOverride("panel", style);
			_bgPanel.TextureFilter = CanvasItem.TextureFilterEnum.LinearWithMipmaps;
		}
		else
		{
			_bgPanel.AddThemeStyleboxOverride("panel", UIStyle.CreateBgGradient());
		}

		_leftPillar.AddThemeStyleboxOverride("panel", UIStyle.CreatePillarPanel(true));
		_rightPillar.AddThemeStyleboxOverride("panel", UIStyle.CreatePillarPanel(false));
	}

	private void ApplyPanelStyles()
	{
		ApplyStyleBox(_headerPanel, "res://Assets/UI/map_details_header.png", 0, 0, 0, 0);
		ApplyStyleBox(_descriptionPanel, "res://Assets/UI/map_details_panel_description.png", 52, 52, 38, 42);
		ApplyStyleBox(_featuresPanel, "res://Assets/UI/map_details_panel_features.png", 40, 40, 38, 35);
		ApplyStyleBox(_statsPanel, "res://Assets/UI/map_details_info_panel.png", 45, 50, 48, 38);
	}

	private void ApplyStyleBox(Control panel, string path, int left, int right, int top, int bottom)
	{
		if (ResourceLoader.Exists(path))
		{
			var style = new StyleBoxTexture();
			style.Texture = GD.Load<Texture2D>(path);
			style.ContentMarginLeft = left;
			style.ContentMarginRight = right;
			style.ContentMarginTop = top;
			style.ContentMarginBottom = bottom;
			panel.AddThemeStyleboxOverride("panel", style);
			panel.TextureFilter = CanvasItem.TextureFilterEnum.LinearWithMipmaps;
		}
		else
		{
			panel.AddThemeStyleboxOverride("panel", UIStyle.CreateBackdropPanel());
		}
	}

	private void ApplyWrapperStyles()
	{
		var descVBox = _descriptionPanel.GetNode<VBoxContainer>("VBoxContainer");
		descVBox.AddThemeConstantOverride("separation", 16);
		WrapInPanelContainer(descVBox, "res://Assets/UI/map_details_panel_description.png");

		var featuresVBox = _featuresPanel.GetNode<VBoxContainer>("VBoxContainer");
		if (ResourceLoader.Exists("res://Assets/UI/map_details_panel_features.png"))
		{
			featuresVBox.AddThemeConstantOverride("separation", 16);
		}
		WrapInPanelContainer(featuresVBox, "res://Assets/UI/map_details_panel_features.png");

		var statsVBox = _statsPanel.GetNode<VBoxContainer>("VBoxContainer");
		statsVBox.AddThemeConstantOverride("separation", 18);
		WrapInPanelContainer(statsVBox, "res://Assets/UI/map_details_info_panel.png");
	}

	private void WrapInPanelContainer(Control content, string stylePath)
	{
		var wrapper = new PanelContainer();
		if (ResourceLoader.Exists(stylePath))
		{
			wrapper.AddThemeStyleboxOverride("panel", new StyleBoxEmpty());
		}
		else
		{
			wrapper.AddThemeStyleboxOverride("panel", UIStyle.CreateBackdropPanel());
		}
		content.GetParent().AddChild(wrapper);
		content.GetParent().MoveChild(wrapper, content.GetIndex());
		content.GetParent().RemoveChild(content);
		wrapper.AddChild(content);
	}

	private void ApplyTextStyles()
	{
		ApplyTitleStyle(_descTitle, "MAP DESCRIPTION", 15);
		_descText.AddThemeColorOverride("font_color", new Color(0.92f, 0.94f, 0.98f));
		_descText.AddThemeFontSizeOverride("font_size", 13);
		_descText.AddThemeConstantOverride("line_spacing", 4);

		ApplyTitleStyle(_featuresTitle, "MAP FEATURES", 16);
		ApplyTitleStyle(_statsTitle, "MAP INFO & STATS", 20);
		ApplyTitleStyle(_ratingTitle, "RATINGS & COMMUNITY", 13);
		ApplyTitleStyle(_awardsTitle, "AWARDS", 13);
		ApplyTitleStyle(_gameplayTitle, "GAMEPLAY STATS", 13);
		ApplyTitleStyle(_techTitle, "TECHNICAL INFO", 13);

		var contentVBox = _statsPanel.GetNodeOrNull<VBoxContainer>("VBoxContainer/ScrollContainer/ContentVBox");
		if (contentVBox != null)
		{
			HideSeparators(contentVBox);
			WrapSectionInCard(contentVBox, _ratingTitle, contentVBox.GetNodeOrNull<GridContainer>("RatingGrid"), isAccentCard: true);
			WrapSectionInCard(contentVBox, _awardsTitle, _awardsContainer);
			WrapSectionInCard(contentVBox, _gameplayTitle, _gameplayStatsContainer);
			WrapSectionInCard(contentVBox, _techTitle, _techInfoContainer);
			contentVBox.AddChild(new Control { CustomMinimumSize = new Vector2(0, 24) });
		}
	}

	private void ApplyTitleStyle(Label label, string text, int fontSize)
	{
		label.Text = Tr(text);
		label.AddThemeColorOverride("font_color", UIStyle.ColorGold);
		label.AddThemeFontSizeOverride("font_size", fontSize);
	}

	private void HideSeparators(Node container)
	{
		for (int i = 1; i <= 3; i++)
		{
			var sep = container.GetNodeOrNull<HSeparator>($"HSeparator{i}");
			if (sep != null) sep.Visible = false;
		}
	}

	private void ApplyMapFrame()
	{
		Texture2D mapFrameTex = LoadTextureSafe("res://Assets/UI/map_details_map.png") ?? LoadTextureSafe("res://Assets/UI/map_details_map.jpg");

		if (mapFrameTex == null)
		{
			_texLeftPeek.Modulate = new Color(0.3f, 0.3f, 0.3f, 0.7f);
			_texRightPeek.Modulate = new Color(0.3f, 0.3f, 0.3f, 0.7f);
			return;
		}

		_texLeftPeek.Visible = false;
		_texRightPeek.Visible = false;
		_texLeftPeek.Hide();
		_texRightPeek.Hide();

		var carouselPanel = GetNode<Control>("CarouselPanel");
		carouselPanel.GetNodeOrNull<TextureRect>("MapFrameOverlay")?.QueueFree();

		var mapFrame = carouselPanel.GetNodeOrNull<PanelContainer>("MapFramePanel");
		if (mapFrame == null)
		{
			mapFrame = new PanelContainer { Name = "MapFramePanel" };
			carouselPanel.AddChild(mapFrame);
		}

		var frameStyle = new StyleBoxTexture { Texture = mapFrameTex, ContentMarginLeft = 36, ContentMarginRight = 36, ContentMarginTop = 32, ContentMarginBottom = 32 };
		mapFrame.AddThemeStyleboxOverride("panel", frameStyle);
		mapFrame.TextureFilter = CanvasItem.TextureFilterEnum.LinearWithMipmaps;
		mapFrame.Position = new Vector2(170, 0);
		mapFrame.Size = new Vector2(650, 380);

		if (_texCenter.GetParent() != mapFrame)
		{
			_texCenter.GetParent()?.RemoveChild(_texCenter);
			mapFrame.AddChild(_texCenter);
		}

		_texCenter.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
		_texCenter.SizeFlagsVertical = Control.SizeFlags.ExpandFill;
		_texCenter.ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize;
		_texCenter.StretchMode = TextureRect.StretchModeEnum.KeepAspectCovered;

		carouselPanel.MoveChild(mapFrame, 0);
		carouselPanel.MoveChild(_btnLeftArrow, 1);
		carouselPanel.MoveChild(_btnRightArrow, 2);
		if (_carouselSlider != null) carouselPanel.MoveChild(_carouselSlider, 3);

		_btnLeftArrow.Position = new Vector2(130, 160);
		_btnRightArrow.Position = new Vector2(790, 160);
	}

	private void ApplyCarouselButtons()
	{
		Texture2D arrowTex = LoadTextureSafe("res://Assets/UI/map_details_prev_next.png") ?? LoadTextureSafe("res://Assets/UI/map_details_prev_next.jpg");

		if (arrowTex != null)
		{
			ApplyImageCarouselButton(_btnLeftArrow, arrowTex, true);
			ApplyImageCarouselButton(_btnRightArrow, arrowTex, false);

			Texture2D mapFrameTex = LoadTextureSafe("res://Assets/UI/map_details_map.png") ?? LoadTextureSafe("res://Assets/UI/map_details_map.jpg");
			if (mapFrameTex != null)
			{
				_btnLeftArrow.Position = new Vector2(132, 155);
				_btnRightArrow.Position = new Vector2(818, 155);
			}
		}
		else
		{
			ApplyFlatCarouselButton(_btnLeftArrow, "◀");
			ApplyFlatCarouselButton(_btnRightArrow, "▶");
		}
	}

	private void ApplyImageCarouselButton(Button button, Texture2D texture, bool flipH)
	{
		button.Text = "";
		button.AddThemeStyleboxOverride("normal", new StyleBoxEmpty());
		button.AddThemeStyleboxOverride("hover", new StyleBoxEmpty());
		button.AddThemeStyleboxOverride("pressed", new StyleBoxEmpty());
		button.AddThemeStyleboxOverride("focus", new StyleBoxEmpty());
		button.Size = new Vector2(40, 70);

		var icon = button.GetNodeOrNull<TextureRect>("ArrowIcon");
		if (icon == null)
		{
			icon = new TextureRect { Name = "ArrowIcon", MouseFilter = Control.MouseFilterEnum.Ignore };
			button.AddChild(icon);
		}
		icon.Texture = texture;
		icon.ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize;
		icon.StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered;
		icon.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
		icon.FlipH = flipH;
		icon.TextureFilter = CanvasItem.TextureFilterEnum.LinearWithMipmaps;
	}

	private void ApplyFlatCarouselButton(Button button, string text)
	{
		button.AddThemeStyleboxOverride("normal", UIStyle.CreateFlatButtonStyle(false, false));
		button.AddThemeStyleboxOverride("hover", UIStyle.CreateFlatButtonStyle(true, false));
		button.AddThemeStyleboxOverride("pressed", UIStyle.CreateFlatButtonStyle(false, true));
		button.AddThemeStyleboxOverride("focus", new StyleBoxEmpty());
		UIStyle.ApplyButtonText(button, text, 24);
	}

	private void SetupPillarButton(Button button, string text, Action onClick)
	{
		UIStyle.ApplyButtonText(button, text, 18);
		button.AddThemeStyleboxOverride("normal", UIStyle.CreateButtonNormal());
		button.AddThemeStyleboxOverride("hover", UIStyle.CreateButtonHover());
		button.AddThemeStyleboxOverride("pressed", UIStyle.CreateButtonPressed());
		button.Pressed += () =>
		{
			UIManager.Instance.PlayClickSound();
			onClick?.Invoke();
		};
		button.MouseEntered += () => UIManager.Instance.PlayHoverSound();
	}

	private void RegisterEvents()
	{
		RegisterCarouselEvents();
		RegisterDownloadEvents();
		RegisterAllScrollEvents();
		RegisterHoverEvents();
	}

	private void RegisterCarouselEvents()
	{
		_btnLeftArrow.Pressed += () =>
		{
			if (_mapData?.Screenshots == null || _mapData.Screenshots.Length == 0) return;
			_carouselIndex = (_carouselIndex - 1 + _mapData.Screenshots.Length) % _mapData.Screenshots.Length;
			_carouselSlider.Value = _carouselIndex;
			UpdateCarousel();
			UIManager.Instance.PlayClickSound();
		};

		_btnRightArrow.Pressed += () =>
		{
			if (_mapData?.Screenshots == null || _mapData.Screenshots.Length == 0) return;
			_carouselIndex = (_carouselIndex + 1) % _mapData.Screenshots.Length;
			_carouselSlider.Value = _carouselIndex;
			UpdateCarousel();
			UIManager.Instance.PlayClickSound();
		};

		_carouselSlider.ValueChanged += (value) =>
		{
			_carouselIndex = (int)value;
			UpdateCarousel();
		};
	}

	private void RegisterDownloadEvents()
	{
		_versionDropdown.ItemSelected += (index) =>
		{
			UIManager.Instance.PlayClickSound();
			OnVersionSelected((int)index);
		};

		_downloadButton.Pressed += () =>
		{
			UIManager.Instance.PlayClickSound();
			if (CheckIfMapAlreadyDownloaded(_mapData))
			{
				GD.Print("Playing map: " + _mapData?.Title);
				UIManager.Instance.TransitionTo(GameScreen.MainMenu);
			}
			else
			{
				StartMapDownloadAsync();
			}
		};
	}

	private void RegisterAllScrollEvents()
	{
		if (_statsScrollContainer != null)
		{
			_targetScrollVertical = _statsScrollContainer.ScrollVertical;
			_statsScrollContainer.GuiInput += (@event) => HandleScrollInput(@event, _statsScrollContainer, ref _targetScrollVertical, 70f);
		}

		if (_descScrollContainer != null)
		{
			_targetDescScrollVertical = _descScrollContainer.ScrollVertical;
			_descScrollContainer.GuiInput += (@event) => HandleScrollInput(@event, _descScrollContainer, ref _targetDescScrollVertical, 50f);
		}

		if (_featuresScrollContainer != null)
		{
			_targetFeaturesScrollVertical = _featuresScrollContainer.ScrollVertical;
			_featuresScrollContainer.GuiInput += (@event) => HandleScrollInput(@event, _featuresScrollContainer, ref _targetFeaturesScrollVertical, 50f);
		}
	}

	private void HandleScrollInput(InputEvent @event, ScrollContainer container, ref float targetScroll, float step)
	{
		if (@event is InputEventMouseButton mb && mb.Pressed)
		{
			var vScroll = container.GetVScrollBar();
			float maxScroll = vScroll != null ? (float)Mathf.Max(0, vScroll.MaxValue - vScroll.Page) : 1000f;

			if (mb.ButtonIndex == MouseButton.WheelUp)
			{
				targetScroll = Mathf.Max(0, targetScroll - step);
				GetViewport().SetInputAsHandled();
			}
			else if (mb.ButtonIndex == MouseButton.WheelDown)
			{
				targetScroll = Mathf.Min(maxScroll, targetScroll + step);
				GetViewport().SetInputAsHandled();
			}
		}
	}

	private void RegisterHoverEvents()
	{
		_texCenter.MouseDefaultCursorShape = Control.CursorShape.PointingHand;
		_texLeftPeek.MouseDefaultCursorShape = Control.CursorShape.PointingHand;
		_texRightPeek.MouseDefaultCursorShape = Control.CursorShape.PointingHand;

		AttachHoverAnimation(_texCenter, 1.03f);
		AttachHoverAnimation(_texLeftPeek, 1.04f);
		AttachHoverAnimation(_texRightPeek, 1.04f);

		_texLeftPeek.GuiInput += (@event) =>
		{
			if (@event is InputEventMouseButton mb && mb.Pressed && mb.ButtonIndex == MouseButton.Left)
			{
				_btnLeftArrow.EmitSignal("pressed");
			}
		};

		_texRightPeek.GuiInput += (@event) =>
		{
			if (@event is InputEventMouseButton mb && mb.Pressed && mb.ButtonIndex == MouseButton.Left)
			{
				_btnRightArrow.EmitSignal("pressed");
			}
		};
	}

	private void UpdateCarousel()
	{
		if (_mapData?.Screenshots == null || _mapData.Screenshots.Length == 0) return;

		int len = _mapData.Screenshots.Length;
		int leftIndex = (_carouselIndex - 1 + len) % len;
		int rightIndex = (_carouselIndex + 1) % len;

		Texture2D centerTex = LoadTextureSafe(_mapData.Screenshots[_carouselIndex]);
		if (centerTex != null)
		{
			_texCenter.Texture = centerTex;
		}

		bool hasMapFrame = LoadTextureSafe("res://Assets/UI/map_details_map.png") != null || LoadTextureSafe("res://Assets/UI/map_details_map.jpg") != null;
		if (_texLeftPeek != null) _texLeftPeek.Visible = !hasMapFrame;
		if (_texRightPeek != null) _texRightPeek.Visible = !hasMapFrame;

		if (hasMapFrame) return;

		if (_texLeftPeek != null) _texLeftPeek.Texture = LoadTextureSafe(_mapData.Screenshots[leftIndex]);
		if (_texRightPeek != null) _texRightPeek.Texture = LoadTextureSafe(_mapData.Screenshots[rightIndex]);
	}

	private Texture2D LoadTextureSafe(string resPath)
	{
		if (string.IsNullOrEmpty(resPath)) return null;

		try
		{
			if (ResourceLoader.Exists(resPath))
			{
				var tex = GD.Load<Texture2D>(resPath);
				if (tex != null) return tex;
			}
		}
		catch { }

		try
		{
			string globalPath = ProjectSettings.GlobalizePath(resPath);
			if (System.IO.File.Exists(globalPath))
			{
				var image = Image.LoadFromFile(globalPath);
				if (image != null)
				{
					return ImageTexture.CreateFromImage(image);
				}
			}
		}
		catch { }

		return null;
	}

	private void PopulateFeatures()
	{
		foreach (Node child in _featuresList.GetChildren())
		{
			child.QueueFree();
		}

		if (_mapData == null) return;

		var featureSet = new HashSet<string>(_mapData.Features ?? Array.Empty<string>(), StringComparer.OrdinalIgnoreCase);
		var displayList = new List<string>(_mapData.Features ?? Array.Empty<string>());
		string[] standardFeatures = { "Custom Units", "Custom Buildings", "Custom Abilities", "Custom Weapons", "Custom Upgrades", "Boss Waves", "Achievements", "Hardcore Mode" };

		foreach (var sf in standardFeatures)
		{
			if (!featureSet.Contains(sf) && displayList.Count < 8)
			{
				displayList.Add(sf);
			}
		}

		foreach (var feat in displayList)
		{
			CreateFeatureRow(feat, featureSet.Contains(feat));
		}
	}

	private void CreateFeatureRow(string feat, bool isActive)
	{
		var rowPanel = new PanelContainer();
		var rowStyle = new StyleBoxFlat();
		rowStyle.BgColor = isActive ? new Color(0.03f, 0.05f, 0.08f, 0.65f) : new Color(0.02f, 0.03f, 0.04f, 0.35f);
		rowStyle.BorderColor = isActive ? new Color(0.15f, 0.65f, 1.0f, 0.5f) : new Color(0.35f, 0.3f, 0.22f, 0.25f);
		rowStyle.SetBorderWidthAll(1);
		rowStyle.SetCornerRadiusAll(6);
		rowStyle.ContentMarginLeft = 12;
		rowStyle.ContentMarginRight = 12;
		rowStyle.ContentMarginTop = 8;
		rowStyle.ContentMarginBottom = 8;
		rowPanel.AddThemeStyleboxOverride("panel", rowStyle);

		var hBox = new HBoxContainer();
		hBox.AddThemeConstantOverride("separation", 10);
		rowPanel.AddChild(hBox);

		var iconRect = new TextureRect();
		iconRect.CustomMinimumSize = new Vector2(22, 22);
		iconRect.ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize;
		iconRect.StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered;
		iconRect.Texture = LoadTextureSafe(isActive ? "res://Assets/UI/checked_box.jpg" : "res://Assets/UI/unchecked_box.jpg");
		hBox.AddChild(iconRect);

		var label = new Label();
		label.Text = feat;
		label.VerticalAlignment = VerticalAlignment.Center;
		label.AddThemeColorOverride("font_color", isActive ? new Color(0.92f, 0.95f, 1.0f) : new Color(0.55f, 0.58f, 0.65f));
		label.AddThemeFontSizeOverride("font_size", 13);
		label.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
		hBox.AddChild(label);

		AttachHoverAnimation(rowPanel, 1.03f,
			isActive ? UIStyle.ColorCyanGlow : UIStyle.ColorGold,
			isActive ? new Color(0.15f, 0.65f, 1.0f, 0.5f) : new Color(0.35f, 0.3f, 0.22f, 0.25f));

		_featuresList.AddChild(rowPanel);
	}

	private void PopulateRatings()
	{

		FillStarsRow(_starsRow1, 5);
		_votesRow1.Text = $"{_mapData.Votes5Star}   {_mapData.AvgRating} Stars";
		_votesRow1.AddThemeColorOverride("font_color", new Color(0.85f, 0.85f, 0.9f));
		_votesRow1.AddThemeFontSizeOverride("font_size", 12);

		FillStarsRow(_starsRow2, 3);
		_votesRow2.Text = $"{_mapData.Votes3Star}   3.0/5 Stars";
		_votesRow2.AddThemeColorOverride("font_color", new Color(0.85f, 0.85f, 0.9f));
		_votesRow2.AddThemeFontSizeOverride("font_size", 12);

		FillStarsRow(_starsRow3, 1);
		_votesRow3.Text = $"{_mapData.Votes1Star}   1.0/5 Stars";
		_votesRow3.AddThemeColorOverride("font_color", new Color(0.85f, 0.85f, 0.9f));
		_votesRow3.AddThemeFontSizeOverride("font_size", 12);
	}

	private void FillStarsRow(HBoxContainer container, int filledStars)
	{
		foreach (Node child in container.GetChildren())
		{
			child.QueueFree();
		}

		for (int i = 0; i < 5; i++)
		{
			var star = new Label();
			star.Text = i < filledStars ? "★" : "☆";
			star.AddThemeColorOverride("font_color", UIStyle.ColorGold);
			star.AddThemeFontSizeOverride("font_size", 16);
			container.AddChild(star);
		}
	}

	private void PopulateAwards()
	{
		foreach (Node child in _awardsContainer.GetChildren())
		{
			child.QueueFree();
		}

		if (_mapData?.Awards == null) return;

		foreach (var awardPath in _mapData.Awards)
		{
			var badgePanel = new PanelContainer();
			var badgeStyle = new StyleBoxFlat();
			badgeStyle.BgColor = new Color(0.08f, 0.09f, 0.12f, 0.85f);
			badgeStyle.BorderColor = UIStyle.ColorGoldDull;
			badgeStyle.SetBorderWidthAll(1);
			badgeStyle.SetCornerRadiusAll(6);
			badgeStyle.ContentMarginLeft = 8;
			badgeStyle.ContentMarginRight = 8;
			badgeStyle.ContentMarginTop = 8;
			badgeStyle.ContentMarginBottom = 8;
			badgePanel.AddThemeStyleboxOverride("panel", badgeStyle);

			var rect = new TextureRect();
			rect.CustomMinimumSize = new Vector2(36, 36);
			rect.ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize;
			rect.StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered;
			rect.Texture = LoadTextureSafe(awardPath) ?? LoadTextureSafe("res://Assets/UI/gold_coin.png");
			
			badgePanel.AddChild(rect);
			AttachHoverAnimation(badgePanel, 1.10f, UIStyle.ColorGold, UIStyle.ColorGoldDull);
			_awardsContainer.AddChild(badgePanel);
		}
	}

	private void PopulateGameplayStats()
	{
		foreach (Node child in _gameplayStatsContainer.GetChildren())
		{
			child.QueueFree();
		}

		AddStatRow(_gameplayStatsContainer, "res://Assets/UI/clock.png", $"AVG PLAYTIME: {_mapData.AvgPlaytime}");
		AddStatRow(_gameplayStatsContainer, "res://Assets/UI/alliance_flag.png", $"PLAYER COUNT: {_mapData.PlayerCount}");
		AddStatRow(_gameplayStatsContainer, "res://Assets/UI/victory_flag.png", $"COMPLETION RATE: {_mapData.CompletionRate}");
	}

	private void PopulateTechInfo()
	{
		foreach (Node child in _techInfoContainer.GetChildren())
		{
			child.QueueFree();
		}

		AddStatRow(_techInfoContainer, "res://Assets/UI/wood_logs.png", $"FILE SIZE: {_mapData.FileSize}");
		AddStatRow(_techInfoContainer, "res://Assets/UI/gold_g.png", $"ENGINE VERSION: {_mapData.EngineVersion}");
		AddStatRow(_techInfoContainer, "res://Assets/UI/battle_shield.png", $"MAX PLAYERS: {_mapData.MaxPlayers}");
		AddStatRow(_techInfoContainer, "res://Assets/UI/battle_axe.png", $"GENRE: {_mapData.Genre}");
	}

	private void AddStatRow(VBoxContainer container, string iconPath, string text)
	{
		var rowPanel = new PanelContainer();
		var rowStyle = new StyleBoxFlat();
		rowStyle.BgColor = new Color(0.02f, 0.03f, 0.04f, 0.5f);
		rowStyle.BorderColor = new Color(0.45f, 0.38f, 0.25f, 0.35f);
		rowStyle.SetBorderWidthAll(1);
		rowStyle.SetCornerRadiusAll(4);
		rowStyle.ContentMarginLeft = 10;
		rowStyle.ContentMarginRight = 10;
		rowStyle.ContentMarginTop = 6;
		rowStyle.ContentMarginBottom = 6;
		rowPanel.AddThemeStyleboxOverride("panel", rowStyle);

		var hBox = new HBoxContainer();
		hBox.AddThemeConstantOverride("separation", 10);
		rowPanel.AddChild(hBox);

		var icon = new TextureRect();
		icon.CustomMinimumSize = new Vector2(24, 24);
		icon.ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize;
		icon.StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered;
		icon.Texture = GD.Load<Texture2D>(iconPath);
		hBox.AddChild(icon);

		string[] parts = text.Split(new char[] { ':' }, 2);
		if (parts.Length == 2)
		{
			var keyLabel = new Label();
			keyLabel.Text = parts[0].Trim() + ":";
			keyLabel.VerticalAlignment = VerticalAlignment.Center;
			keyLabel.AddThemeColorOverride("font_color", UIStyle.ColorGoldDull);
			keyLabel.AddThemeFontSizeOverride("font_size", 12);
			hBox.AddChild(keyLabel);

			var valueLabel = new Label();
			valueLabel.Text = parts[1].Trim();
			valueLabel.VerticalAlignment = VerticalAlignment.Center;
			valueLabel.AddThemeColorOverride("font_color", new Color(0.92f, 0.94f, 0.98f));
			valueLabel.AddThemeFontSizeOverride("font_size", 13);
			valueLabel.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
			hBox.AddChild(valueLabel);
		}
		else
		{
			var label = new Label();
			label.Text = text;
			label.VerticalAlignment = VerticalAlignment.Center;
			label.AddThemeColorOverride("font_color", new Color(0.9f, 0.9f, 0.95f));
			label.AddThemeFontSizeOverride("font_size", 13);
			hBox.AddChild(label);
		}

		container.AddChild(rowPanel);
		AttachHoverAnimation(rowPanel, 1.02f, UIStyle.ColorGoldDull, new Color(0.45f, 0.38f, 0.25f, 0.35f));
	}

	private void WrapSectionInCard(VBoxContainer parent, Label titleLabel, Control contentControl, bool isAccentCard = false)
	{
		if (titleLabel == null || contentControl == null) return;
		if (titleLabel.GetParent() is PanelContainer) return;

		var card = new PanelContainer();
		var cardStyle = new StyleBoxFlat();
		cardStyle.BgColor = isAccentCard 
			? new Color(0.06f, 0.07f, 0.10f, 0.8f) 
			: new Color(0.04f, 0.05f, 0.07f, 0.55f);
		cardStyle.BorderColor = isAccentCard 
			? UIStyle.ColorGold 
			: new Color(0.55f, 0.45f, 0.32f, 0.4f);
		cardStyle.SetBorderWidthAll(isAccentCard ? 2 : 1);
		cardStyle.SetCornerRadiusAll(6);
		cardStyle.ContentMarginLeft = 14;
		cardStyle.ContentMarginRight = 14;
		cardStyle.ContentMarginTop = 12;
		cardStyle.ContentMarginBottom = 12;
		card.AddThemeStyleboxOverride("panel", cardStyle);

		var innerVBox = new VBoxContainer();
		innerVBox.AddThemeConstantOverride("separation", 8);

		int insertIndex = titleLabel.GetIndex();

		parent.RemoveChild(titleLabel);
		parent.RemoveChild(contentControl);

		innerVBox.AddChild(titleLabel);
		innerVBox.AddChild(contentControl);
		card.AddChild(innerVBox);

		parent.AddChild(card);
		parent.MoveChild(card, insertIndex);

		AttachHoverAnimation(card, 1.01f, isAccentCard ? UIStyle.ColorGold : UIStyle.ColorBronze, isAccentCard ? UIStyle.ColorGold : new Color(0.55f, 0.45f, 0.32f, 0.4f));
	}

	private void AttachHoverAnimation(Control node, float targetScale = 1.03f, Color? hoverBorderColor = null, Color? defaultBorderColor = null)
	{
		if (node == null) return;

		node.MouseEntered += () =>
		{
			node.PivotOffset = node.Size / 2.0f;
			var tween = node.CreateTween().SetParallel(true);
			tween.TweenProperty(node, "scale", new Vector2(targetScale, targetScale), 0.12);

			if (hoverBorderColor.HasValue && node.HasThemeStyleboxOverride("panel"))
			{
				var sb = node.GetThemeStylebox("panel") as StyleBoxFlat;
				if (sb != null)
				{
					tween.TweenProperty(sb, "border_color", hoverBorderColor.Value, 0.12);
				}
			}
			UIManager.Instance.PlayHoverSound();
		};

		node.MouseExited += () =>
		{
			var tween = node.CreateTween().SetParallel(true);
			tween.TweenProperty(node, "scale", Vector2.One, 0.12);

			if (defaultBorderColor.HasValue && node.HasThemeStyleboxOverride("panel"))
			{
				var sb = node.GetThemeStylebox("panel") as StyleBoxFlat;
				if (sb != null)
				{
					tween.TweenProperty(sb, "border_color", defaultBorderColor.Value, 0.12);
				}
			}
		};
	}

	private async void StartMapDownloadAsync()
	{
		if (_mapData == null || _isDownloading) return;

		if (!MapAssetManager.Storage.CheckFreeDiskSpaceAcceptingDownloads())
		{
			UIStyle.ApplyButtonText(_downloadButton, "RETRY DOWNLOAD", 18);
			_downloadSubtitle.Text = Tr("Insufficient disk space (at least 1% free required).");
			_downloadSubtitle.AddThemeColorOverride("font_color", new Color(0.9f, 0.3f, 0.3f));
			return;
		}

		_isDownloading = true;
		_downloadButton.Disabled = true;
		_downloadProgress = 0.0f;
		UIStyle.ApplyButtonText(_downloadButton, "Downloading... 0%", 16);
		_downloadSubtitle.Text = $"0% ({_mapData.FileSize})";
		_downloadSubtitle.AddThemeColorOverride("font_color", UIStyle.ColorGoldDull);

		string seedServerUrl = GodotObject.IsInstanceValid(LobbyManager.Instance)
			? LobbyManager.Instance.RegistryServerUrl
			: ServersConfigHelper.GetDefaultServerUrl();

		var distClient = new MapDistributionClient();
		int lastReportedPercent = 0;
		bool success = await distClient.DownloadMapPackageFromRegistryAsync(
			_mapData.MapId,
			seedServerUrl,
			progress =>
			{
				int currentPercent = (int)MathF.Round(progress * 100.0f);
				if (currentPercent != lastReportedPercent)
				{
					lastReportedPercent = currentPercent;
					_downloadProgress = currentPercent;
					_downloadButton.Text = $"Downloading... {currentPercent}%";
					_downloadSubtitle.Text = $"{currentPercent}% ({_mapData.FileSize})";
				}
			}
		);

		_isDownloading = false;
		_downloadButton.Disabled = false;

		if (success)
		{
			UIStyle.ApplyButtonText(_downloadButton, "PLAY MAP", 18);
			_downloadSubtitle.Text = Tr("Ready to Play");
			_downloadSubtitle.AddThemeColorOverride("font_color", UIStyle.ColorCyanGlow);
		}
		else
		{
			UIStyle.ApplyButtonText(_downloadButton, "RETRY DOWNLOAD", 18);
			if (!MapAssetManager.Storage.CheckFreeDiskSpaceAcceptingDownloads())
			{
				_downloadSubtitle.Text = Tr("Insufficient disk space (at least 1% free required).");
			}
			else
			{
				_downloadSubtitle.Text = Tr("Download failed. Click to retry.");
			}
			_downloadSubtitle.AddThemeColorOverride("font_color", new Color(0.9f, 0.3f, 0.3f));
		}
	}
}
