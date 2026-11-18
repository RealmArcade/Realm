using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Godot;
using Realm.Client.Core;
using Realm.Client.Network;
using FileAccess = Godot.FileAccess;
using static Godot.Control;
using static Godot.CanvasItem;
using Realm.Client.Utils;
using Realm.Client.Services;
using Realm.Client.Services.Steam;

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
	private Button _steamProfileButton = null!;
	private Button _workshopButton = null!;
	private Button _discordButton = null!;
	private Button _otherMapsButton = null!;
	private TextureRect _creatorAvatar = null!;
	private HBoxContainer _otherMapsStrip = null!;
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
	private Label _votesRow2 = null!;
	private Label _votesRow3 = null!;

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

	/// <summary>The invite the map itself states, read from its metadata when the details are shown.</summary>
	private string _statedDiscordInvite = string.Empty;
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
		SetupSteamLinks();


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
		_votesRow1 = GetNode<Label>("StatsPanel/VBoxContainer/ScrollContainer/ContentVBox/RatingGrid/Row1Votes");
		_votesRow2 = GetNode<Label>("StatsPanel/VBoxContainer/ScrollContainer/ContentVBox/RatingGrid/Row2Votes");
		_votesRow3 = GetNode<Label>("StatsPanel/VBoxContainer/ScrollContainer/ContentVBox/RatingGrid/Row3Votes");

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

	/// <summary>
	///     Adds the two links Steam owns: the author's own profile and the Workshop page of the map, which is where its
	///     description, screenshots, comments and report button live. Nothing about either is stored by the game.
	/// </summary>
	private void SetupSteamLinks()
	{
		// The creator is a Steam account, so the row is the account's own face and name rather than a name the map typed.
		// Nothing here is stored: an avatar or a persona is read from Steam each session, and a private profile simply
		// draws no picture.
		_creatorAvatar = new TextureRect();
		_creatorAvatar.Name = "CreatorAvatar_TextureRect";
		_creatorAvatar.CustomMinimumSize = new Vector2(48, 48);
		_creatorAvatar.ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize;
		_creatorAvatar.StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered;
		_creatorAvatar.Visible = false;
		_headerPanel.GetNode<VBoxContainer>("VBoxContainer").AddChild(_creatorAvatar);
		_headerPanel.GetNode<VBoxContainer>("VBoxContainer").MoveChild(_creatorAvatar, _creatorLabel.GetIndex());

		_otherMapsStrip = new HBoxContainer();
		_otherMapsStrip.Name = "OtherMapsStrip_HBoxContainer";
		_otherMapsStrip.Alignment = BoxContainer.AlignmentMode.Center;
		_otherMapsStrip.AddThemeConstantOverride("separation", 8);
		_otherMapsStrip.Visible = false;
		_headerPanel.GetNode<VBoxContainer>("VBoxContainer").AddChild(_otherMapsStrip);

		_steamProfileButton = CreateHeaderLink("SteamProfileButton_Button", TranslationServer.Translate("VIEW STEAM PROFILE"));
		_steamProfileButton.Pressed += () =>
		{
			if (_mapData?.CreatorSteamId is not > 0) return;

			GameContext.UserInterface?.PlayClickSound();
			OS.ShellOpen($"https://steamcommunity.com/profiles/{_mapData.CreatorSteamId}");
		};

		_workshopButton = CreateHeaderLink("WorkshopButton_Button", TranslationServer.Translate("OPEN ON STEAM WORKSHOP"));
		_workshopButton.Pressed += () =>
		{
			if (_mapData?.WorkshopId is not > 0) return;

			GameContext.UserInterface?.PlayClickSound();
			OS.ShellOpen($"https://steamcommunity.com/sharedfiles/filedetails/?id={_mapData.WorkshopId}");
		};

		_otherMapsButton = CreateHeaderLink("OtherMapsButton_Button", TranslationServer.Translate("OTHER MAPS BY THIS CREATOR"));
		_otherMapsButton.Pressed += () =>
		{
			if (_mapData?.CreatorSteamId is not > 0) return;

			GameContext.UserInterface?.PlayClickSound();
			OS.ShellOpen($"https://steamcommunity.com/profiles/{_mapData.CreatorSteamId}/myworkshopfiles/?appid={GameContext.Steam.AppId.ToString(System.Globalization.CultureInfo.InvariantCulture)}");
		};

		_discordButton = CreateHeaderLink("DiscordButton_Button", TranslationServer.Translate("JOIN THE CREATOR'S DISCORD"));
		_discordButton.Pressed += () =>
		{
			if (!DiscordInvite.TryNormalize(_statedDiscordInvite, out string inviteLink)) return;

			GameContext.UserInterface?.PlayClickSound();
			OS.ShellOpen(inviteLink);
		};
	}

	/// <summary>
	///     The creator's other items for this game, drawn as a strip of their titles. The query names the running AppID, so
	///     an item the creator published for another game can never appear here, and an empty answer is a normal state: the
	///     author simply has nothing else published.
	/// </summary>
	/// <param name="creatorSteamId">The SteamID64 the item states as its owner.</param>
	private async void LoadOtherMapsByCreator(ulong creatorSteamId)
	{
		foreach (Node child in _otherMapsStrip.GetChildren()) child.QueueFree();
		_otherMapsStrip.Visible = false;

		if (creatorSteamId == 0 || !SteamWorkshopService.IsAvailable) return;
		if (_mapData?.CreatorSteamId != creatorSteamId) return;

		try
		{
			IReadOnlyList<WorkshopMapEntry> others = await SteamWorkshopService.BrowseByAuthorAsync(creatorSteamId, 5, CancellationToken.None);
			if (!GodotObject.IsInstanceValid(this) || _mapData?.CreatorSteamId != creatorSteamId) return;

			foreach (WorkshopMapEntry entry in others)
			{
				if (entry.PublishedFileId == _mapData?.WorkshopId) continue;

				var itemButton = new Button();
				itemButton.Name = $"OtherMap_{entry.PublishedFileId}_Button";
				itemButton.Text = entry.Title;
				itemButton.TooltipText = string.Format(TranslationServer.Translate("{0} at {1}% positive"), entry.Title, entry.VotesUp + entry.VotesDown > 0 ? (int)(entry.VotesUp * 100 / (entry.VotesUp + entry.VotesDown)) : 0);
				itemButton.Set("icon_max_width", 0);
				itemButton.FocusMode = FocusModeEnum.None;
				itemButton.CustomMinimumSize = new Vector2(150, 24);
				itemButton.AddThemeFontSizeOverride("font_size", 11);
				UIStyle.ApplyButtonStyleBoxesClearingFocus(itemButton);
				ulong publishedFileId = entry.PublishedFileId;
				itemButton.Pressed += () =>
				{
					GameContext.UserInterface?.PlayClickSound();
					OS.ShellOpen($"https://steamcommunity.com/sharedfiles/filedetails/?id={publishedFileId}");
				};
				_otherMapsStrip.AddChild(itemButton);
			}

			_otherMapsStrip.Visible = _otherMapsStrip.GetChildCount() > 0;
		}
		catch (Exception exception)
		{
			GameLog.LogError($"[MapDetails] the creator's other maps could not be read: {exception.Message}");
		}
	}

	private Button CreateHeaderLink(string name, string text)
	{
		var button = new Button { Name = name };
		button.Text = text;
		button.Set("icon_max_width", 0);
		button.CustomMinimumSize = new Vector2(220, 24);
		button.SizeFlagsHorizontal = SizeFlags.ShrinkCenter;
		button.FocusMode = FocusModeEnum.None;
		button.AddThemeFontSizeOverride("font_size", 11);
		UIStyle.ApplyButtonStyleBoxesClearingFocus(button);
		_headerPanel.GetNode<VBoxContainer>("VBoxContainer").AddChild(button);
		button.Visible = false;
		return button;
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

		// The credits name the creators whose work the map carries, which is the reason the panel exists: a source map with a
		// known author is shown with that author, and composition counts the same as an imported file.
		string? mapDirectory = GameHost.ResolveMapDirectory(_mapData?.Title, _mapData?.Version);
		IReadOnlyList<MapCreditEntry> credits = mapDirectory != null
			? GameContext.MapAttributionService.CollectCredits(mapDirectory)
			: Array.Empty<MapCreditEntry>();

		IReadOnlyList<string> sourceMapNames = credits.Count > 0
			? credits.Select(DescribeCredit).ToList()
			: MapAttributionLookup.Resolve(_mapData?.Attribution_SourceMap, _mapData?.Title, _mapData?.Version);
		_attributionPopover.ShowFor(_attributionButton, sourceMapNames);
	}

	/// <summary>
	///     States one credit as the creator the package names, so the attribution panel answers who made the work rather than
	///     only which map it came from. A source that names nobody is shown as an unknown source, which is a normal state and
	///     not a damaged record.
	/// </summary>
	/// <param name="credit">The source and what the package says about it.</param>
	/// <returns>The line the panel shows for that source.</returns>
	private static string DescribeCredit(MapCreditEntry credit)
	{
		string authorName = credit.AuthorSteamId != 0
			? GameContext.Steam.GetPersonaName(credit.AuthorSteamId)
			: string.Empty;

		if (authorName.Length == 0) return credit.SourceMap;
		return string.Format(TranslationServer.Translate("{0} by {1}"), credit.SourceMap, authorName);
	}

	private static string ReadStatedDiscordInvite(string? mapTitle, string? mapVersion)
	{
		if (string.IsNullOrWhiteSpace(mapTitle)) return string.Empty;

		try
		{
			string? mapDirectory = GameHost.ResolveMapDirectory(mapTitle, mapVersion);
			if (string.IsNullOrEmpty(mapDirectory)) return string.Empty;

			string metadataPath = Path.Combine(mapDirectory, "metadata.json");
			if (!File.Exists(metadataPath)) return string.Empty;

			return GameContext.MetadataService.LoadMetadata(metadataPath).MapProperties?.DiscordServerInviteLink ?? string.Empty;
		}
		catch (Exception exception)
		{
			GameLog.LogError($"[MapDetails] the map's stated Discord invite could not be read: {exception.Message}");
			return string.Empty;
		}
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

		UIStyle.ApplyTitle(_titleLabel, TranslationServer.Translate("MAP DETAILS: {0}"), 28);
		LocalizedText.Set(_titleLabel, "MAP DETAILS: {0}", _mapData.Title);
		_creatorLabel.Text = _mapData.Creator.Length > 0
			? string.Format(TranslationServer.Translate("By: {0}"), _mapData.Creator)
			: TranslationServer.Translate("By: an author Steam has not named");

		_steamProfileButton.Visible = _mapData.CreatorSteamId != 0;
		_workshopButton.Visible = _mapData.WorkshopId != 0;
		_statedDiscordInvite = ReadStatedDiscordInvite(_mapData.Title, _mapData.Version);
		_otherMapsButton.Visible = _mapData.CreatorSteamId != 0 && SteamWorkshopService.IsAvailable;
		_discordButton.Visible = DiscordInvite.IsValid(_statedDiscordInvite);

		// A creator's face and their other work are Steam's, and Steam can be absent: without it the row keeps the name the
		// map states and hides the actions that would need an account.
		Texture2D? creatorAvatar = _mapData.CreatorSteamId != 0 ? GameContext.Steam.GetAvatar(_mapData.CreatorSteamId) : null;
		_creatorAvatar.Texture = creatorAvatar;
		_creatorAvatar.Visible = creatorAvatar != null;

		LoadOtherMapsByCreator(_mapData.CreatorSteamId);

		_descText.Text = _mapData.Description;

		PopulateFeatures();

		if (_mapData.PreviewImages != null && _mapData.PreviewImages.Length > 0)
		{
			_carouselSlider.MaxValue = _mapData.PreviewImages.Length - 1;
			_carouselSlider.Value = 0;
			UpdateCarousel();
		}

		PopulateRatings();
		PopulateGameplayStats();
		PopulateTechInfo();

		bool isDownloaded = MapProvisioningService.IsLocalCopyPresent(_mapData);

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
			_downloadSubtitle.Text = string.Format(TranslationServer.Translate("FILE SIZE: {0}"), _mapData.FileSize);
			_downloadSubtitle.AddThemeColorOverride("font_color", UIStyle.ColorGoldDull);
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
		return _mapData?.PreviewImages != null && _mapData.PreviewImages.Length > 0;
	}

	private void UpdateCenterTexture()
	{
		Texture2D? centerTex = LoadTextureSafe(_mapData.PreviewImages[_carouselIndex]);
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
		int len = _mapData.PreviewImages.Length;
		int leftIndex = (_carouselIndex - 1 + len) % len;
		int rightIndex = (_carouselIndex + 1) % len;

		if (_texLeftPeek != null) _texLeftPeek.Texture = LoadTextureSafe(_mapData.PreviewImages[leftIndex]);
		if (_texRightPeek != null) _texRightPeek.Texture = LoadTextureSafe(_mapData.PreviewImages[rightIndex]);
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

		int lastReportedPercent = 0;
		bool success = await SteamWorkshopService.InstallAsync(
			PublishedFileIdOf(_mapData),
			progress =>
			{
				int currentPercent = (int)MathF.Round(progress * 100.0f);
				if (currentPercent != lastReportedPercent)
				{
					lastReportedPercent = currentPercent;
					_downloadProgress = currentPercent;
					_downloadButton.Text = string.Format(TranslationServer.Translate("Downloading... {0}%"), currentPercent);
					_downloadSubtitle.Text = $"{currentPercent}% ({_mapData.FileSize})";
				}
			},
			CancellationToken.None
		);

		_isDownloading = false;
		_downloadButton.Disabled = false;

		if (success || MapProvisioningService.IsLocalCopyPresent(_mapData))
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

	/// <summary>
	///     The Workshop item a library entry came from. The library's id is the item id as text, because every screen that
	///     hands a map around does so as a string.
	/// </summary>
	/// <param name="map">The map the details screen is showing.</param>
	/// <returns>The item id, or zero when the entry did not come from the Workshop.</returns>
	private static ulong PublishedFileIdOf(MapData map)
	{
		return ulong.TryParse(map.MapId, out ulong publishedFileId) ? publishedFileId : 0;
	}
}
