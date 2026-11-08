using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Godot;
using Realm.Client.Network;
using Realm.Shared.Distribution;
using FileAccess = Godot.FileAccess;
using static Godot.Control;
using static Godot.CanvasItem;
using Realm.Client.Utils;
using Realm.Client.Services;

namespace Realm.Client.UI;

[GlobalClass]
public partial class MapDetails : Control
{
	public override void _EnterTree()
	{
		Realm.Client.Services.GameContext.EnsureServices();
	}

	private Panel _bgPanel = null!;
	private Panel _leftPillar = null!;
	private Panel _rightPillar = null!;

	private Button _backButton = null!;


	private PanelContainer _headerPanel = null!;
	private Label _titleLabel = null!;
	private Label _creatorLabel = null!;
	private Button _attributionButton = null!;
	private AttributionPopover _attributionPopover = null!;


	private TextureRect _texLeftPeek = null!;
	private TextureRect _texCenter = null!;
	private TextureRect _texRightPeek = null!;
	private Button _btnLeftArrow = null!;
	private Button _btnRightArrow = null!;
	private HSlider _carouselSlider = null!;


	private PanelContainer _descriptionPanel = null!;
	private Label _descTitle = null!;
	private Label _descText = null!;


	private PanelContainer _featuresPanel = null!;
	private Label _featuresTitle = null!;
	private VBoxContainer _featuresList = null!;


	private PanelContainer _statsPanel = null!;
	private Label _statsTitle = null!;

	private Label _ratingTitle = null!;
	private HBoxContainer _starsRow1 = null!;
	private Label _votesRow1 = null!;
	private HBoxContainer _starsRow2 = null!;
	private Label _votesRow2 = null!;
	private HBoxContainer _starsRow3 = null!;
	private Label _votesRow3 = null!;

	private Label _awardsTitle = null!;
	private HBoxContainer _awardsContainer = null!;

	private Label _gameplayTitle = null!;
	private VBoxContainer _gameplayStatsContainer = null!;

	private Label _techTitle = null!;
	private VBoxContainer _techInfoContainer = null!;

	private Button _downloadButton = null!;
	private Label _downloadSubtitle = null!;
	private OptionButton _versionDropdown = null!;
	private HBoxContainer _versionContainer = null!;
	private Label _versionLabel = null!;

	private MapData _mapData = null!;
	private int _carouselIndex = 0;
	private bool _isDownloading = false;
	private float _downloadProgress = 0.0f;

	private ScrollContainer _statsScrollContainer = null!;
	private float _targetScrollVertical = 0.0f;
	private ScrollContainer _descScrollContainer = null!;
	private float _targetDescScrollVertical = 0.0f;
	private ScrollContainer _featuresScrollContainer = null!;
	private float _targetFeaturesScrollVertical = 0.0f;
	private float _pulseTimer = 0.0f;

	public override void _Ready()
	{
		(_bgPanel, _leftPillar, _rightPillar) = UIStyle.BindStandardChrome(this);

		_backButton = GetNode<Button>("BackButton");


		_headerPanel = GetNode<PanelContainer>("HeaderPanel");
		_titleLabel = GetNode<Label>("HeaderPanel/VBoxContainer/TitleLabel");
		_creatorLabel = GetNode<Label>("HeaderPanel/VBoxContainer/CreatorLabel");
		SetupAttributionButton();


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
		_versionContainer.SizeFlagsHorizontal = SizeFlags.ShrinkCenter;

		_versionLabel = new Label();
		_versionLabel.Name = "VersionLabel_Label";
		_versionLabel.Text = Tr("VERSION:");
		_versionLabel.VerticalAlignment = VerticalAlignment.Center;
		_versionLabel.AddThemeColorOverride("font_color", UIStyle.ColorGold);
		_versionLabel.AddThemeFontSizeOverride("font_size", 14);
		_versionContainer.AddChild(_versionLabel);

		_versionDropdown = new OptionButton();
		_versionDropdown.Name = "VersionDropdown";
		_versionDropdown.CustomMinimumSize = new Vector2(160, 36);
		_versionDropdown.SizeFlagsHorizontal = SizeFlags.ShrinkCenter;
		UIStyle.ApplyButtonStyleBoxesClearingFocus(_versionDropdown);
		_versionDropdown.AddThemeColorOverride("font_color", UIStyle.ColorGold);
		_versionDropdown.AddThemeColorOverride("font_hover_color", new Color(1.0f, 0.94f, 0.75f));
		_versionDropdown.AddThemeColorOverride("font_pressed_color", UIStyle.ColorGold);
		_versionDropdown.AddThemeColorOverride("font_focus_color", UIStyle.ColorGold);
		_versionDropdown.AddThemeFontSizeOverride("font_size", 14);
		_versionDropdown.TextureFilter = TextureFilterEnum.LinearWithMipmaps;
		_versionContainer.AddChild(_versionDropdown);

		var downloadParent = _downloadButton.GetParent();
		downloadParent.AddChild(_versionContainer);
		downloadParent.MoveChild(_versionContainer, _downloadButton.GetIndex());

		ApplyStyles();
		RegisterEvents();
		SetProcess(false);
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
			if (vData.IsTestVersion)
			{
				vLabel += " " + TranslationServer.Translate("(Test)");
			}

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

	private void SetupAttributionButton()
	{
		_attributionButton = new Button();
		_attributionButton.Name = "AttributionButton_Button";
		_attributionButton.Text = LocalizationManager.Translate("ATTRIBUTION");
		_attributionButton.Set("icon_max_width", 0);
		_attributionButton.CustomMinimumSize = new Vector2(180, 24);
		_attributionButton.SizeFlagsHorizontal = SizeFlags.ShrinkCenter;
		_attributionButton.FocusMode = FocusModeEnum.None;
		_attributionButton.TooltipText = LocalizationManager.Translate("Show maps whose content was imported into this map");
		_attributionButton.AddThemeFontSizeOverride("font_size", 11);
		UIStyle.ApplyButtonStyleBoxesClearingFocus(_attributionButton);
		_attributionButton.Pressed += OnAttributionPressed;
		_headerPanel.GetNode<VBoxContainer>("VBoxContainer").AddChild(_attributionButton);
	}

	private void OnAttributionPressed()
	{
		GameContext.UserInterface?.PlayClickSound();

		if (_attributionPopover == null || !GodotObject.IsInstanceValid(_attributionPopover))
		{
			_attributionPopover = new AttributionPopover();
			AddChild(_attributionPopover);
			_attributionPopover.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
		}

		IReadOnlyList<string> sourceMapNames = MapAttributionLookup.Resolve(_mapData?.Attribution_SourceMap, _mapData?.Title, _mapData?.Version);
		_attributionPopover.ShowFor(_attributionButton, sourceMapNames);
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
			UIStyle.ApplyButtonText(_downloadButton, TranslationServer.Translate("PLAY MAP"), 18);
			_downloadSubtitle.Text = Tr("Ready to Play");
			_downloadSubtitle.AddThemeColorOverride("font_color", UIStyle.ColorCyanGlow);
		}
		else
		{
			UIStyle.ApplyButtonText(_downloadButton, TranslationServer.Translate("DOWNLOAD MAP"), 18);
			_downloadSubtitle.Text = $"File Size: {_mapData.FileSize}";
			_downloadSubtitle.AddThemeColorOverride("font_color", UIStyle.ColorGoldDull);
		}
	}

	private bool CheckIfMapAlreadyDownloaded(MapData map)
	{
		if (map == null) return false;

		string version = !string.IsNullOrWhiteSpace(map.Version) ? map.Version.Trim() : "1.0.0";

		if (HasLocalManifestFile(map, version)) return true;
		if (IsDownloadedViaAssetManager(map, version)) return true;

		return false;
	}

	private bool HasLocalManifestFile(MapData map, string version)
	{
		if (FileAccess.FileExists($"res://Maps/{map.Title}/{version}/manifest.json")) return true;
		if (FileAccess.FileExists($"res://Maps/{map.MapId}/manifest.json")) return true;

		return FileAccess.FileExists($"res://Maps/{map.Title}/manifest.json") && CheckManifestByTitle(map.Title, version);
	}

	private bool IsDownloadedViaAssetManager(MapData map, string version)
	{
		if (!string.IsNullOrWhiteSpace(map.MapId) && MapAssetManager.IsMapDownloaded(map.MapId)) return true;
		return !string.IsNullOrWhiteSpace(map.Title) && MapAssetManager.IsMapDownloaded(map.Title, version);
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

	private void UpdateCarousel()
	{
		if (!HasValidScreenshots()) return;

		UpdateCenterTexture();

		bool hasMapFrame = CheckForMapFrame();
		UpdatePeekVisibilities(!hasMapFrame);

		if (hasMapFrame) return;

		UpdatePeekTextures();
	}

	private bool HasValidScreenshots()
	{
		return _mapData?.Screenshots != null && _mapData.Screenshots.Length > 0;
	}

	private void UpdateCenterTexture()
	{
		Texture2D? centerTex = LoadTextureSafe(_mapData.Screenshots[_carouselIndex]);
		if (centerTex != null)
		{
			_texCenter.Texture = centerTex;
		}
	}

	private bool CheckForMapFrame()
	{
		return LoadTextureSafe("res://Assets/UI/map_details_map.webp") != null;
	}

	private void UpdatePeekVisibilities(bool isVisible)
	{
		if (_texLeftPeek != null) _texLeftPeek.Visible = isVisible;
		if (_texRightPeek != null) _texRightPeek.Visible = isVisible;
	}

	private void UpdatePeekTextures()
	{
		int len = _mapData.Screenshots.Length;
		int leftIndex = (_carouselIndex - 1 + len) % len;
		int rightIndex = (_carouselIndex + 1) % len;

		if (_texLeftPeek != null) _texLeftPeek.Texture = LoadTextureSafe(_mapData.Screenshots[leftIndex]);
		if (_texRightPeek != null) _texRightPeek.Texture = LoadTextureSafe(_mapData.Screenshots[rightIndex]);
	}

	private Texture2D? LoadTextureSafe(string resPath)
	{
		return TrimmedTextureLoader.LoadTrimmedTexture(resPath);
	}

	private async void StartMapDownloadAsync()
	{
		try
		{
			await DownloadMapAsync();
		}
		catch (Exception exception)
		{
			GameLog.LogError($"[MapDetails] Downloading the map failed: {exception}");
		}
	}

	private async Task DownloadMapAsync()
	{
		if (_mapData == null || _isDownloading) return;

		if (!MapAssetManager.Storage.CheckFreeDiskSpaceAcceptingDownloads())
		{
			UIStyle.ApplyButtonText(_downloadButton, TranslationServer.Translate("RETRY DOWNLOAD"), 18);
			_downloadSubtitle.Text = Tr("Insufficient disk space (at least 1% free required).");
			_downloadSubtitle.AddThemeColorOverride("font_color", new Color(0.9f, 0.3f, 0.3f));
			return;
		}

		_isDownloading = true;
		_downloadButton.Disabled = true;
		_downloadProgress = 0.0f;
		UIStyle.ApplyButtonText(_downloadButton, TranslationServer.Translate("Downloading... 0%"), 16);
		_downloadSubtitle.Text = $"0% ({_mapData.FileSize})";
		_downloadSubtitle.AddThemeColorOverride("font_color", UIStyle.ColorGoldDull);

		string seedServerUrl = GodotObject.IsInstanceValid(GameContext.Lobby)
			? GameContext.Lobby.RegistryServerUrl
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

		if (success || CheckIfMapAlreadyDownloaded(_mapData))
		{
			UIStyle.ApplyButtonText(_downloadButton, TranslationServer.Translate("PLAY MAP"), 18);
			_downloadSubtitle.Text = Tr("Ready to Play");
			_downloadSubtitle.AddThemeColorOverride("font_color", UIStyle.ColorCyanGlow);
		}
		else
		{
			UIStyle.ApplyButtonText(_downloadButton, TranslationServer.Translate("RETRY DOWNLOAD"), 18);
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
