using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Godot;
using Realm.Client.Network;
using Realm.Client.Services;
using Realm.Client.Utils;
using Realm.Shared.Distribution;
using static Godot.CanvasItem;
using static Godot.Control;

namespace Realm.Client.UI;

[GlobalClass]
public partial class CreatorDiscovery : Control
{
	private Button _backButton = null!;
	private TabContainer _tabContainer = null!;
	private GridContainer _forYouGrid = null!;
	private LineEdit _creatorSearchInput = null!;
	private OptionButton _creatorDropdown = null!;
	private GridContainer _portfolioGrid = null!;
	private HBoxContainer _creatorLinksBox = null!;
	private Label _creatorStatsLabel = null!;
	private Label _selectedCreatorNameLabel = null!;
	private Label _selectedCreatorInitialsLabel = null!;
	private ColorRect _selectedCreatorAvatarBg = null!;
	private Label _statusFeedbackLabel = null!;
	private Button _forYouTabBtn = null!;
	private Button _portfolioTabBtn = null!;

	private List<CreatorInfo> _allCreators = new();
	private List<CreatorInfo> _filteredCreators = new();
	private List<DiscoveryMapDto> _discoveryMaps = new();
	private Dictionary<string, int> _localPlaytime = new(StringComparer.OrdinalIgnoreCase);
	private bool _catalogReachable;
	private int _portfolioRequestVersion;
	private string _pendingCreatorSelection = string.Empty;

	public override void _EnterTree()
	{
		GameContext.EnsureServices();
		SetFpsCenter(true);
	}

	public override void _ExitTree()
	{
		SetFpsCenter(false);
	}

	private void SetFpsCenter(bool center)
	{
		var fpsNode = GetTree()?.Root?.GetNodeOrNull<Control>("Main/CanvasLayer/FPS");
		if (fpsNode == null)
		{
			return;
		}

		if (center)
		{
			fpsNode.SetAnchorsPreset(LayoutPreset.CenterTop);
			fpsNode.GrowHorizontal = GrowDirection.Both;
			if (fpsNode is Label lbl)
			{
				lbl.HorizontalAlignment = HorizontalAlignment.Center;
			}

			fpsNode.OffsetLeft = -75;
			fpsNode.OffsetRight = 75;
			fpsNode.OffsetTop = 10;
		}
		else
		{
			fpsNode.SetAnchorsPreset(LayoutPreset.TopRight);
			fpsNode.GrowHorizontal = GrowDirection.Begin;
			if (fpsNode is Label lbl)
			{
				lbl.HorizontalAlignment = HorizontalAlignment.Right;
			}

			fpsNode.OffsetLeft = -160;
			fpsNode.OffsetRight = -10;
			fpsNode.OffsetTop = 10;
		}
	}

	public override void _Ready()
	{
		BuildLayout();
		SelectTab(0);
		_ = LoadAllDataAsync();
	}

	private void BuildLayout()
	{
		var bgPanel = new Panel();
		bgPanel.Name = "BgPanel_Panel";
		bgPanel.SetAnchorsPreset(LayoutPreset.FullRect);
		bgPanel.AddThemeStyleboxOverride("panel", UIStyle.CreateCreatorDiscoveryBg());
		AddChild(bgPanel);

		var topHeaderPanel = new PanelContainer();
		topHeaderPanel.Name = "TopHeaderPanel_PanelContainer";
		topHeaderPanel.SetAnchorsPreset(LayoutPreset.TopWide);
		topHeaderPanel.CustomMinimumSize = new Vector2(0, 70);
		topHeaderPanel.AddThemeStyleboxOverride("panel", UIStyle.CreateDiscoveryHeaderPanel());
		AddChild(topHeaderPanel);

		var topHeaderHBox = new HBoxContainer();
		topHeaderHBox.Name = "TopHeaderHBox_HBoxContainer";
		topHeaderHBox.AddThemeConstantOverride("separation", 20);
		topHeaderHBox.Alignment = BoxContainer.AlignmentMode.Begin;
		topHeaderPanel.AddChild(topHeaderHBox);

		_backButton = new Button();
		_backButton.Name = "BackButton_Button";
		_backButton.AddThemeConstantOverride("icon_max_width", 24);
		UIStyle.ApplyBackButton(_backButton, () => GameContext.UserInterface?.TransitionTo(GameScreen.MainMenu));
		topHeaderHBox.AddChild(_backButton);

		var titleLabel = new Label();
		titleLabel.Name = "TitleLabel_Label";
		UIStyle.ApplyTitle(titleLabel, TranslationServer.Translate("CREATOR DISCOVERY"), 28);
		titleLabel.SizeFlagsHorizontal = SizeFlags.ExpandFill;
		titleLabel.HorizontalAlignment = HorizontalAlignment.Left;
		topHeaderHBox.AddChild(titleLabel);

		var badgePanel = new PanelContainer();
		badgePanel.Name = "BadgePanel_PanelContainer";
		badgePanel.AddThemeStyleboxOverride("panel", UIStyle.CreateDiscoveryBadge());
		var badgeLabel = new Label();
		badgeLabel.Name = "BadgeLabel_Label";
		badgeLabel.Text = TranslationServer.Translate("COMMUNITY HUB");
		badgeLabel.AddThemeColorOverride("font_color", UIStyle.ColorGold);
		badgeLabel.AddThemeFontSizeOverride("font_size", 12);
		badgePanel.AddChild(badgeLabel);
		topHeaderHBox.AddChild(badgePanel);

		var centerVBox = new VBoxContainer();
		centerVBox.Name = "CenterVBox_VBoxContainer";
		centerVBox.AnchorLeft = 0.10f;
		centerVBox.AnchorRight = 0.90f;
		centerVBox.AnchorTop = 0.12f;
		centerVBox.AnchorBottom = 0.93f;
		centerVBox.GrowHorizontal = GrowDirection.Both;
		centerVBox.GrowVertical = GrowDirection.Both;
		centerVBox.OffsetLeft = 0;
		centerVBox.OffsetRight = 0;
		centerVBox.OffsetTop = 0;
		centerVBox.OffsetBottom = 0;
		centerVBox.AddThemeConstantOverride("separation", 14);
		AddChild(centerVBox);

		var navHBox = new HBoxContainer();
		navHBox.Name = "NavHBox_HBoxContainer";
		navHBox.Alignment = BoxContainer.AlignmentMode.Center;
		navHBox.AddThemeConstantOverride("separation", 16);
		centerVBox.AddChild(navHBox);

		_forYouTabBtn = new Button();
		_forYouTabBtn.Name = "ForYouTabBtn_Button";
		_forYouTabBtn.AddThemeConstantOverride("icon_max_width", 20);
		_forYouTabBtn.CustomMinimumSize = new Vector2(150, 38);
		UIStyle.ApplyButtonText(_forYouTabBtn, TranslationServer.Translate("FOR YOU"), 14);
		_forYouTabBtn.Pressed += () => SelectTab(0);
		navHBox.AddChild(_forYouTabBtn);

		_portfolioTabBtn = new Button();
		_portfolioTabBtn.Name = "PortfolioTabBtn_Button";
		_portfolioTabBtn.AddThemeConstantOverride("icon_max_width", 20);
		_portfolioTabBtn.CustomMinimumSize = new Vector2(180, 38);
		UIStyle.ApplyButtonText(_portfolioTabBtn, TranslationServer.Translate("PORTFOLIO BROWSER"), 14);
		_portfolioTabBtn.Pressed += () => SelectTab(1);
		navHBox.AddChild(_portfolioTabBtn);

		_tabContainer = new TabContainer();
		_tabContainer.Name = "TabContainer_TabContainer";
		_tabContainer.TabsVisible = false;
		_tabContainer.TextureFilter = TextureFilterEnum.LinearWithMipmaps;
		_tabContainer.SizeFlagsVertical = SizeFlags.ExpandFill;
		_tabContainer.SizeFlagsHorizontal = SizeFlags.ExpandFill;
		_tabContainer.AddThemeStyleboxOverride("panel", UIStyle.CreateCreatorDiscoveryPanelStyle());
		centerVBox.AddChild(_tabContainer);

		BuildForYouTab();
		BuildPortfolioTab();
	}

	private void SelectTab(int index)
	{
		_tabContainer.CurrentTab = index;
		UIStyle.ApplyCreatorDiscoveryTabButton(_forYouTabBtn, index == 0);
		UIStyle.ApplyCreatorDiscoveryTabButton(_portfolioTabBtn, index == 1);
	}

	private void BuildForYouTab()
	{
		var forYouTab = new MarginContainer();
		forYouTab.Name = "ForYouTab_MarginContainer";
		forYouTab.AddThemeConstantOverride("margin_top", 28);
		forYouTab.AddThemeConstantOverride("margin_bottom", 28);
		forYouTab.AddThemeConstantOverride("margin_left", 28);
		forYouTab.AddThemeConstantOverride("margin_right", 28);
		_tabContainer.AddChild(forYouTab);

		var forYouVBox = new VBoxContainer();
		forYouVBox.Name = "ForYouVBox_VBoxContainer";
		forYouVBox.AddThemeConstantOverride("separation", 18);
		forYouTab.AddChild(forYouVBox);

		var bannerPanel = new PanelContainer();
		bannerPanel.Name = "BannerPanel_PanelContainer";
		bannerPanel.AddThemeStyleboxOverride("panel", UIStyle.CreateDiscoveryCard(new Color(0.14f, 0.16f, 0.20f, 0.9f), UIStyle.ColorCyanGlowDim));
		forYouVBox.AddChild(bannerPanel);

		var bannerHBox = new HBoxContainer();
		bannerHBox.Name = "BannerHBox_HBoxContainer";
		bannerHBox.AddThemeConstantOverride("separation", 16);
		bannerPanel.AddChild(bannerHBox);

		var (bannerAvatar, _, _) = CreateCreatorCrestAvatar(50, "R");
		bannerHBox.AddChild(bannerAvatar);

		var bannerVBox = new VBoxContainer();
		bannerVBox.Name = "BannerVBox_VBoxContainer";
		bannerVBox.SizeFlagsHorizontal = SizeFlags.ExpandFill;
		bannerHBox.AddChild(bannerVBox);

		var bannerTitle = new Label();
		bannerTitle.Name = "BannerTitle_Label";
		bannerTitle.Text = TranslationServer.Translate("RECOMMENDED COMMUNITY CREATORS");
		bannerTitle.AddThemeColorOverride("font_color", UIStyle.ColorGold);
		bannerTitle.AddThemeFontSizeOverride("font_size", 16);
		bannerVBox.AddChild(bannerTitle);

		var bannerSub = new Label();
		bannerSub.Name = "BannerSub_Label";
		bannerSub.Text = TranslationServer.Translate("Creators from the maps you play and active community builders.");
		bannerSub.AutowrapMode = TextServer.AutowrapMode.WordSmart;
		bannerSub.CustomMinimumSize = new Vector2(300, 0);
		bannerSub.SizeFlagsHorizontal = SizeFlags.ExpandFill;
		bannerSub.AddThemeColorOverride("font_color", new Color(0.8f, 0.82f, 0.88f));
		bannerSub.AddThemeFontSizeOverride("font_size", 13);
		bannerVBox.AddChild(bannerSub);

		var scrollContainer = new ScrollContainer();
		scrollContainer.Name = "ScrollContainer_ScrollContainer";
		scrollContainer.SizeFlagsVertical = SizeFlags.ExpandFill;
		scrollContainer.HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled;
		forYouVBox.AddChild(scrollContainer);

		_forYouGrid = new GridContainer();
		_forYouGrid.Name = "ForYouGrid_GridContainer";
		_forYouGrid.Columns = 2;
		_forYouGrid.SizeFlagsHorizontal = SizeFlags.ExpandFill;
		_forYouGrid.AddThemeConstantOverride("h_separation", 20);
		_forYouGrid.AddThemeConstantOverride("v_separation", 20);
		scrollContainer.AddChild(_forYouGrid);
	}

	private void BuildPortfolioTab()
	{
		var portfolioTab = new MarginContainer();
		portfolioTab.Name = "PortfolioTab_MarginContainer";
		portfolioTab.AddThemeConstantOverride("margin_top", 28);
		portfolioTab.AddThemeConstantOverride("margin_bottom", 28);
		portfolioTab.AddThemeConstantOverride("margin_left", 28);
		portfolioTab.AddThemeConstantOverride("margin_right", 28);
		_tabContainer.AddChild(portfolioTab);

		var portVBox = new VBoxContainer();
		portVBox.Name = "PortVBox_VBoxContainer";
		portVBox.AddThemeConstantOverride("separation", 14);
		portfolioTab.AddChild(portVBox);

		var profileHeaderPanel = new PanelContainer();
		profileHeaderPanel.Name = "ProfileHeaderPanel_PanelContainer";
		profileHeaderPanel.AddThemeStyleboxOverride("panel", UIStyle.CreateDiscoveryCard(new Color(0.12f, 0.13f, 0.17f, 0.95f), UIStyle.ColorBronze));
		portVBox.AddChild(profileHeaderPanel);

		var profileVBox = new VBoxContainer();
		profileVBox.Name = "ProfileVBox_VBoxContainer";
		profileVBox.AddThemeConstantOverride("separation", 12);
		profileHeaderPanel.AddChild(profileVBox);

		var topBarHBox = new HBoxContainer();
		topBarHBox.Name = "TopBarHBox_HBoxContainer";
		topBarHBox.AddThemeConstantOverride("separation", 12);
		profileVBox.AddChild(topBarHBox);

		var selectLbl = new Label();
		selectLbl.Name = "SelectLbl_Label";
		selectLbl.Text = TranslationServer.Translate("Creator:");
		selectLbl.AddThemeColorOverride("font_color", UIStyle.ColorGold);
		selectLbl.AddThemeFontSizeOverride("font_size", 14);
		topBarHBox.AddChild(selectLbl);

		_creatorSearchInput = new LineEdit();
		_creatorSearchInput.Name = "CreatorSearchInput_LineEdit";
		_creatorSearchInput.PlaceholderText = TranslationServer.Translate("Filter creators...");
		_creatorSearchInput.CustomMinimumSize = new Vector2(180, 36);
		_creatorSearchInput.ClearButtonEnabled = true;
		UIStyle.ApplyTextInput(_creatorSearchInput, 14);
		_creatorSearchInput.TextChanged += OnCreatorSearchChanged;
		topBarHBox.AddChild(_creatorSearchInput);

		_creatorDropdown = new OptionButton();
		_creatorDropdown.Name = "CreatorDropdown_OptionButton";
		_creatorDropdown.CustomMinimumSize = new Vector2(260, 36);
		_creatorDropdown.AddThemeConstantOverride("icon_max_width", 20);
		UIStyle.ApplyDropdownButton(_creatorDropdown);
		_creatorDropdown.ItemSelected += OnCreatorDropdownSelected;
		topBarHBox.AddChild(_creatorDropdown);

		_selectedCreatorNameLabel = new Label();
		_selectedCreatorNameLabel.Name = "SelectedCreatorNameLabel_Label";
		_selectedCreatorNameLabel.AddThemeColorOverride("font_color", UIStyle.ColorCyanGlow);
		_selectedCreatorNameLabel.AddThemeFontSizeOverride("font_size", 18);
		_selectedCreatorNameLabel.SizeFlagsHorizontal = SizeFlags.ExpandFill;
		_selectedCreatorNameLabel.HorizontalAlignment = HorizontalAlignment.Right;
		topBarHBox.AddChild(_selectedCreatorNameLabel);

		var bioHBox = new HBoxContainer();
		bioHBox.Name = "BioHBox_HBoxContainer";
		bioHBox.AddThemeConstantOverride("separation", 16);
		profileVBox.AddChild(bioHBox);

		var (avatarBox, avatarBg, avatarInitials) = CreateCreatorCrestAvatar(66, "?");
		_selectedCreatorAvatarBg = avatarBg;
		_selectedCreatorInitialsLabel = avatarInitials;
		bioHBox.AddChild(avatarBox);

		var detailsVBox = new VBoxContainer();
		detailsVBox.Name = "DetailsVBox_VBoxContainer";
		detailsVBox.SizeFlagsHorizontal = SizeFlags.ExpandFill;
		detailsVBox.AddThemeConstantOverride("separation", 6);
		bioHBox.AddChild(detailsVBox);

		_creatorStatsLabel = new Label();
		_creatorStatsLabel.Name = "CreatorStatsLabel_Label";
		_creatorStatsLabel.AutowrapMode = TextServer.AutowrapMode.WordSmart;
		_creatorStatsLabel.CustomMinimumSize = new Vector2(250, 0);
		_creatorStatsLabel.SizeFlagsHorizontal = SizeFlags.ExpandFill;
		_creatorStatsLabel.AddThemeColorOverride("font_color", new Color(0.84f, 0.86f, 0.92f));
		_creatorStatsLabel.AddThemeFontSizeOverride("font_size", 13);
		detailsVBox.AddChild(_creatorStatsLabel);

		_creatorLinksBox = new HBoxContainer();
		_creatorLinksBox.Name = "CreatorLinksBox_HBoxContainer";
		_creatorLinksBox.AddThemeConstantOverride("separation", 12);
		detailsVBox.AddChild(_creatorLinksBox);

		_statusFeedbackLabel = new Label();
		_statusFeedbackLabel.Name = "StatusFeedbackLabel_Label";
		_statusFeedbackLabel.AddThemeColorOverride("font_color", UIStyle.ColorCyanGlow);
		_statusFeedbackLabel.AddThemeFontSizeOverride("font_size", 12);
		_statusFeedbackLabel.Visible = false;
		detailsVBox.AddChild(_statusFeedbackLabel);

		var showcaseHeader = new Label();
		showcaseHeader.Name = "ShowcaseHeader_Label";
		showcaseHeader.Text = TranslationServer.Translate("PUBLISHED MAPS & ASSET PACK");
		showcaseHeader.AddThemeColorOverride("font_color", UIStyle.ColorGoldDull);
		showcaseHeader.AddThemeFontSizeOverride("font_size", 15);
		portVBox.AddChild(showcaseHeader);

		var portfolioScroll = new ScrollContainer();
		portfolioScroll.Name = "PortfolioScroll_ScrollContainer";
		portfolioScroll.SizeFlagsVertical = SizeFlags.ExpandFill;
		portfolioScroll.HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled;
		portVBox.AddChild(portfolioScroll);

		_portfolioGrid = new GridContainer();
		_portfolioGrid.Name = "PortfolioGrid_GridContainer";
		_portfolioGrid.Columns = 3;
		_portfolioGrid.SizeFlagsHorizontal = SizeFlags.ExpandFill;
		_portfolioGrid.AddThemeConstantOverride("h_separation", 18);
		_portfolioGrid.AddThemeConstantOverride("v_separation", 18);
		portfolioScroll.AddChild(_portfolioGrid);
	}

	private async Task LoadAllDataAsync()
	{
		string statsPath = ProjectSettings.GlobalizePath("user://appdata/playtime_stats.json");
		_localPlaytime = CreatorDiscoveryService.LoadPlaytimeCreators(statsPath);

		DisplayForYouMessage(TranslationServer.Translate("Loading creators..."), showRetry: false);
		SetEmptyPortfolioHeader(TranslationServer.Translate("Loading creator catalog..."));
		DisplayPortfolioMessage(TranslationServer.Translate("Loading creators..."));

		string seedServerUrl = GetRegistryServerUrl();
		CreatorCatalogLoadResult catalog = await CreatorDiscoveryService.LoadCatalogAsync(seedServerUrl);
		if (!GodotObject.IsInstanceValid(this))
		{
			return;
		}

		_catalogReachable = catalog.Reachable;
		_allCreators = catalog.Creators;
		_discoveryMaps = catalog.DiscoveryMaps;

		PopulateForYouGrid();
		ApplyCreatorFilter(string.Empty);

		if (!string.IsNullOrEmpty(_pendingCreatorSelection))
		{
			string target = _pendingCreatorSelection;
			_pendingCreatorSelection = string.Empty;
			SelectOrCreateCreatorInPortfolio(target);
		}
	}

	private void PopulateForYouGrid()
	{
		ClearContainer(_forYouGrid);

		var recommendations = BuildForYouRecommendations();
		if (recommendations.Count == 0)
		{
			string message = _catalogReachable
				? TranslationServer.Translate("Play maps or browse the registry to discover creators here.")
				: TranslationServer.Translate("Could not reach the creator registry and no local playtime was recorded yet.");
			DisplayForYouMessage(message, showRetry: !_catalogReachable);
			return;
		}

		foreach (var item in recommendations)
		{
			_forYouGrid.AddChild(CreateForYouCard(item.Creator, item.LocalMinutesPlayed));
		}
	}

	private List<(CreatorInfo Creator, int LocalMinutesPlayed)> BuildForYouRecommendations()
	{
		var creatorLookup = _allCreators
			.GroupBy(c => c.Username, StringComparer.OrdinalIgnoreCase)
			.ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

		var recommendations = new List<(CreatorInfo Creator, int LocalMinutesPlayed)>();
		var addedNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

		foreach (var kvp in _localPlaytime.OrderByDescending(x => x.Value))
		{
			string name = kvp.Key.Trim();
			if (string.IsNullOrEmpty(name) || !addedNames.Add(name))
			{
				continue;
			}

			if (!creatorLookup.TryGetValue(name, out CreatorInfo creator))
			{
				if (string.Equals(name, "Realm Builder", StringComparison.OrdinalIgnoreCase))
				{
					continue;
				}

				creator = new CreatorInfo
				{
					PublicKey = string.Empty,
					Username = name,
					IsRegistered = false
				};
			}

			recommendations.Add((creator, kvp.Value));
		}

		foreach (var creator in _allCreators
			         .OrderByDescending(c => c.PublishedMapCount)
			         .ThenByDescending(c => c.TotalGamesPlayed)
			         .ThenByDescending(c => c.AssetCount)
			         .ThenBy(c => c.Username, StringComparer.OrdinalIgnoreCase))
		{
			if (recommendations.Count >= 12)
			{
				break;
			}

			if (!addedNames.Add(creator.Username))
			{
				continue;
			}

			if (creator.PublishedMapCount == 0 && creator.AssetCount == 0)
			{
				continue;
			}

			recommendations.Add((creator, 0));
		}

		return recommendations;
	}

	private Control CreateForYouCard(CreatorInfo creator, int localMinutesPlayed)
	{
		var cardPanel = new PanelContainer();
		cardPanel.Name = "ForYouCard_PanelContainer";
		cardPanel.TextureFilter = TextureFilterEnum.LinearWithMipmaps;
		cardPanel.SizeFlagsHorizontal = SizeFlags.ExpandFill;
		cardPanel.AddThemeStyleboxOverride("panel", UIStyle.CreateDiscoveryCard(new Color(0.12f, 0.14f, 0.17f, 0.95f), UIStyle.ColorBronze));

		var cardHBox = new HBoxContainer();
		cardHBox.Name = "CardHBox_HBoxContainer";
		cardHBox.AddThemeConstantOverride("separation", 16);
		cardPanel.AddChild(cardHBox);

		var (avatar, _, _) = CreateCreatorCrestAvatar(56, creator.Username);
		cardHBox.AddChild(avatar);

		var infoVBox = new VBoxContainer();
		infoVBox.Name = "InfoVBox_VBoxContainer";
		infoVBox.SizeFlagsHorizontal = SizeFlags.ExpandFill;
		infoVBox.Alignment = BoxContainer.AlignmentMode.Center;
		infoVBox.AddThemeConstantOverride("separation", 4);
		cardHBox.AddChild(infoVBox);

		var nameLbl = new Label();
		nameLbl.Name = "CreatorName_Label";
		nameLbl.Text = creator.Username;
		nameLbl.AddThemeColorOverride("font_color", UIStyle.ColorGold);
		nameLbl.AddThemeFontSizeOverride("font_size", 17);
		infoVBox.AddChild(nameLbl);

		var statsLbl = new Label();
		statsLbl.Name = "StatsLbl_Label";
		statsLbl.Text = FormatCreatorSummary(creator, localMinutesPlayed);
		statsLbl.AutowrapMode = TextServer.AutowrapMode.WordSmart;
		statsLbl.CustomMinimumSize = new Vector2(180, 0);
		statsLbl.SizeFlagsHorizontal = SizeFlags.ExpandFill;
		statsLbl.AddThemeColorOverride("font_color", new Color(0.75f, 0.78f, 0.85f));
		statsLbl.AddThemeFontSizeOverride("font_size", 12);
		infoVBox.AddChild(statsLbl);

		var viewBtn = new Button();
		viewBtn.Name = "ViewProfileBtn_Button";
		viewBtn.AddThemeConstantOverride("icon_max_width", 20);
		viewBtn.CustomMinimumSize = new Vector2(130, 32);
		viewBtn.SizeFlagsHorizontal = SizeFlags.ShrinkEnd;
		viewBtn.SizeFlagsVertical = SizeFlags.ShrinkCenter;
		UIStyle.ApplyButtonText(viewBtn, TranslationServer.Translate("VIEW PROFILE"), 12);
		UIStyle.ApplyButtonStyleBoxesClearingFocus(viewBtn);
		viewBtn.Pressed += () => SwitchToPortfolioForCreator(creator.Username);
		cardHBox.AddChild(viewBtn);

		return cardPanel;
	}

	private void OnCreatorSearchChanged(string searchText)
	{
		ApplyCreatorFilter(searchText);
	}

	private void ApplyCreatorFilter(string searchText)
	{
		string query = searchText.Trim();
		_filteredCreators = string.IsNullOrEmpty(query)
			? _allCreators.ToList()
			: _allCreators
				.Where(c => c.Username.Contains(query, StringComparison.OrdinalIgnoreCase))
				.ToList();

		_creatorDropdown.Clear();
		if (_filteredCreators.Count == 0)
		{
			_creatorDropdown.Disabled = true;
			string emptyText = _allCreators.Count == 0
				? TranslationServer.Translate("No creators found")
				: TranslationServer.Translate("No matching creators");
			_creatorDropdown.AddItem(emptyText);
			SetEmptyPortfolioHeader(emptyText);
			DisplayPortfolioMessage(_catalogReachable
				? emptyText
				: TranslationServer.Translate("Unable to load creators from the registry."));
			return;
		}

		_creatorDropdown.Disabled = false;
		foreach (var creator in _filteredCreators)
		{
			string label = creator.PublishedMapCount > 0
				? string.Format(TranslationServer.Translate("{0} ({1} maps)"), creator.Username, creator.PublishedMapCount)
				: creator.Username;
			_creatorDropdown.AddItem(label);
		}

		_creatorDropdown.Select(0);
		_ = LoadPortfolioForIndexAsync(0);
	}

	private void OnCreatorDropdownSelected(long index)
	{
		_ = LoadPortfolioForIndexAsync((int)index);
	}

	private async Task LoadPortfolioForIndexAsync(int index)
	{
		if (index < 0 || index >= _filteredCreators.Count)
		{
			return;
		}

		int requestVersion = ++_portfolioRequestVersion;
		CreatorInfo creator = _filteredCreators[index];
		UpdateCreatorHeader(creator, assetPackOverrideCount: creator.AssetCount);
		DisplayPortfolioMessage(TranslationServer.Translate("Loading creator portfolio..."));

		string seedServerUrl = GetRegistryServerUrl();
		CreatorPortfolioResult result = await CreatorDiscoveryService.LoadPortfolioAsync(
			seedServerUrl,
			creator,
			_discoveryMaps);

		if (!GodotObject.IsInstanceValid(this) || requestVersion != _portfolioRequestVersion)
		{
			return;
		}

		ClearContainer(_portfolioGrid);

		int updatedMapCount = Math.Max(creator.PublishedMapCount, result.Maps.Count);
		int updatedAssetCount = result.AssetPack?.AssetCount ?? creator.AssetCount;
		CreatorInfo updatedCreator = creator;
		updatedCreator.PublishedMapCount = updatedMapCount;
		updatedCreator.AssetCount = updatedAssetCount;
		UpdateCreatorHeader(updatedCreator, updatedAssetCount);

		foreach (var map in result.Maps)
		{
			_portfolioGrid.AddChild(CreateMapCard(map, seedServerUrl));
		}

		if (result.AssetPack != null && result.AssetPack.AssetCount > 0)
		{
			_portfolioGrid.AddChild(CreateAssetPackSummaryCard(result.AssetPack));
		}

		if (_portfolioGrid.GetChildCount() == 0)
		{
			string msg = result.RequestSucceeded
				? TranslationServer.Translate("No published maps or asset packs yet.")
				: TranslationServer.Translate("Could not load this creator's portfolio right now.");
			DisplayPortfolioMessage(msg);
		}
	}

	private void SwitchToPortfolioForCreator(string creatorName)
	{
		SelectTab(1);
		if (_allCreators.Count == 0)
		{
			_pendingCreatorSelection = creatorName;
			return;
		}

		SelectOrCreateCreatorInPortfolio(creatorName);
	}

	private void SelectOrCreateCreatorInPortfolio(string creatorName)
	{
		if (!string.IsNullOrEmpty(_creatorSearchInput.Text))
		{
			_creatorSearchInput.Text = string.Empty;
			ApplyCreatorFilter(string.Empty);
		}

		int idx = _filteredCreators.FindIndex(c => string.Equals(c.Username, creatorName, StringComparison.OrdinalIgnoreCase));
		if (idx < 0)
		{
			var unregistered = new CreatorInfo
			{
				PublicKey = string.Empty,
				Username = creatorName,
				IsRegistered = false
			};
			_allCreators.Add(unregistered);
			ApplyCreatorFilter(string.Empty);
			idx = _filteredCreators.FindIndex(c => string.Equals(c.Username, creatorName, StringComparison.OrdinalIgnoreCase));
		}

		if (idx >= 0)
		{
			_creatorDropdown.Select(idx);
			_ = LoadPortfolioForIndexAsync(idx);
		}
	}

	private void UpdateCreatorHeader(CreatorInfo creator, int assetPackOverrideCount)
	{
		_selectedCreatorNameLabel.Text = creator.Username;
		ApplyAvatarVisual(_selectedCreatorAvatarBg, _selectedCreatorInitialsLabel, creator.Username);

		var parts = new List<string>();
		if (creator.PublishedMapCount > 0)
		{
			parts.Add(string.Format(TranslationServer.Translate("{0} published maps"), creator.PublishedMapCount));
		}
		else
		{
			parts.Add(TranslationServer.Translate("No published maps yet"));
		}

		if (assetPackOverrideCount > 0)
		{
			parts.Add(string.Format(TranslationServer.Translate("{0} verified assets"), assetPackOverrideCount));
		}

		if (!creator.IsRegistered)
		{
			parts.Add(TranslationServer.Translate("Profile not registered"));
		}

		_creatorStatsLabel.Text = string.Join("  •  ", parts);
		_statusFeedbackLabel.Visible = false;

		ClearContainer(_creatorLinksBox);

		if (!string.IsNullOrWhiteSpace(creator.DonationLink))
		{
			var supportBtn = CreateLinkActionButton(
				TranslationServer.Translate("Support Creator"),
				creator.DonationLink,
				openIfUrl: true);
			_creatorLinksBox.AddChild(supportBtn);
		}

		if (!string.IsNullOrWhiteSpace(creator.ContactInfo))
		{
			var contactBtn = CreateLinkActionButton(
				string.Format(TranslationServer.Translate("Contact: {0}"), creator.ContactInfo),
				creator.ContactInfo,
				openIfUrl: false);
			_creatorLinksBox.AddChild(contactBtn);
		}

		if (_creatorLinksBox.GetChildCount() == 0)
		{
			var noInfoLbl = new Label();
			noInfoLbl.Name = "NoContactLbl_Label";
			noInfoLbl.Text = TranslationServer.Translate("No donation or contact link provided.");
			noInfoLbl.AddThemeColorOverride("font_color", new Color(0.60f, 0.63f, 0.70f));
			noInfoLbl.AddThemeFontSizeOverride("font_size", 12);
			_creatorLinksBox.AddChild(noInfoLbl);
		}
	}

	private void SetEmptyPortfolioHeader(string titleText)
	{
		_selectedCreatorNameLabel.Text = titleText;
		ApplyAvatarVisual(_selectedCreatorAvatarBg, _selectedCreatorInitialsLabel, "?");
		_creatorStatsLabel.Text = string.Empty;
		_statusFeedbackLabel.Visible = false;
		ClearContainer(_creatorLinksBox);
	}

	private Button CreateLinkActionButton(string label, string value, bool openIfUrl)
	{
		var btn = new Button();
		btn.Name = "CreatorLinkBtn_Button";
		btn.Text = label;
		btn.AddThemeConstantOverride("icon_max_width", 18);
		btn.AddThemeFontSizeOverride("font_size", 12);
		btn.CustomMinimumSize = new Vector2(0, 28);
		UIStyle.ApplyFlatButton(btn);
		btn.Pressed += () =>
		{
			if (openIfUrl && IsSafeHttpUrl(value))
			{
				OS.ShellOpen(value);
				return;
			}

			DisplayServer.ClipboardSet(value);
			_statusFeedbackLabel.Text = string.Format(TranslationServer.Translate("Copied to clipboard: {0}"), value);
			_statusFeedbackLabel.Visible = true;
		};
		return btn;
	}

	private Control CreateMapCard(MapData map, string seedServerUrl)
	{
		var cardPanel = new PanelContainer();
		cardPanel.Name = "PortfolioMapCard_PanelContainer";
		cardPanel.TextureFilter = TextureFilterEnum.LinearWithMipmaps;
		cardPanel.CustomMinimumSize = new Vector2(220, 240);
		cardPanel.SizeFlagsHorizontal = SizeFlags.ExpandFill;
		cardPanel.AddThemeStyleboxOverride("panel", UIStyle.CreateCreatorDiscoveryMapFrameStyle());

		var cardVBox = new VBoxContainer();
		cardVBox.Name = "CardVBox_VBoxContainer";
		cardVBox.AddThemeConstantOverride("separation", 8);
		cardPanel.AddChild(cardVBox);

		var (thumbContainer, textureRect) = CreateCardThumbnailFrame(210, 112, map.IsGreenlit ? TranslationServer.Translate("GREENLIT") : string.Empty);
		cardVBox.AddChild(thumbContainer);
		_ = CasAssetImageLoader.ApplyTextureAsync(textureRect, map.ThumbnailHash, seedServerUrl, map.ThumbnailPath);

		var titleLbl = new Label();
		titleLbl.Name = "TitleLbl_Label";
		titleLbl.Text = map.Title;
		titleLbl.HorizontalAlignment = HorizontalAlignment.Center;
		titleLbl.TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis;
		titleLbl.AddThemeColorOverride("font_color", UIStyle.ColorGold);
		titleLbl.AddThemeFontSizeOverride("font_size", 15);
		cardVBox.AddChild(titleLbl);

		string metaText = !string.IsNullOrWhiteSpace(map.Genre)
			? $"{map.Genre}  •  v{map.Version}"
			: $"v{map.Version}";
		var metaLbl = new Label();
		metaLbl.Name = "MetaLbl_Label";
		metaLbl.Text = metaText;
		metaLbl.HorizontalAlignment = HorizontalAlignment.Center;
		metaLbl.AddThemeColorOverride("font_color", new Color(0.75f, 0.78f, 0.85f));
		metaLbl.AddThemeFontSizeOverride("font_size", 12);
		cardVBox.AddChild(metaLbl);

		var statsLbl = new Label();
		statsLbl.Name = "CardStatsLbl_Label";
		statsLbl.HorizontalAlignment = HorizontalAlignment.Center;
		statsLbl.AddThemeColorOverride("font_color", UIStyle.ColorGoldDull);
		statsLbl.AddThemeFontSizeOverride("font_size", 11);
		if (map.TotalReviews > 0)
		{
			statsLbl.Text = string.Format(TranslationServer.Translate("Rating {0:F1} ({1})  •  {2} plays"), map.RatingStars, map.TotalReviews, map.GamesPlayed);
		}
		else if (map.GamesPlayed > 0)
		{
			statsLbl.Text = string.Format(TranslationServer.Translate("{0} plays"), map.GamesPlayed);
		}
		else
		{
			statsLbl.Text = map.FileSize;
		}

		cardVBox.AddChild(statsLbl);

		var detailsBtn = new Button();
		detailsBtn.Name = "DetailsBtn_Button";
		detailsBtn.AddThemeConstantOverride("icon_max_width", 18);
		detailsBtn.CustomMinimumSize = new Vector2(120, 30);
		detailsBtn.SizeFlagsHorizontal = SizeFlags.ShrinkCenter;
		UIStyle.ApplyButtonText(detailsBtn, TranslationServer.Translate("DETAILS"), 12);
		UIStyle.ApplyButtonStyleBoxesClearingFocus(detailsBtn);
		detailsBtn.Pressed += () =>
		{
			GameContext.UserInterface?.PlayClickSound();
			GameContext.UserInterface?.TransitionToMapDetails(map, GameScreen.CreatorDiscovery);
		};
		cardVBox.AddChild(detailsBtn);

		return cardPanel;
	}

	private Control CreateAssetPackSummaryCard(CreatorAssetPackDto assetPack)
	{
		var cardPanel = new PanelContainer();
		cardPanel.Name = "AssetPackCard_PanelContainer";
		cardPanel.TextureFilter = TextureFilterEnum.LinearWithMipmaps;
		cardPanel.CustomMinimumSize = new Vector2(220, 240);
		cardPanel.SizeFlagsHorizontal = SizeFlags.ExpandFill;
		cardPanel.AddThemeStyleboxOverride("panel", UIStyle.CreateCreatorDiscoveryMapFrameStyle());

		var cardVBox = new VBoxContainer();
		cardVBox.Name = "CardVBox_VBoxContainer";
		cardVBox.AddThemeConstantOverride("separation", 8);
		cardPanel.AddChild(cardVBox);

		var (thumbContainer, textureRect) = CreateCardThumbnailFrame(210, 112, TranslationServer.Translate("ASSET PACK"));
		textureRect.Texture = TrimmedTextureLoader.LoadTrimmedTexture("res://Assets/UI/menu_creator_button.webp");
		textureRect.StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered;
		cardVBox.AddChild(thumbContainer);

		var titleLbl = new Label();
		titleLbl.Name = "TitleLbl_Label";
		titleLbl.Text = assetPack.Title;
		titleLbl.HorizontalAlignment = HorizontalAlignment.Center;
		titleLbl.TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis;
		titleLbl.AddThemeColorOverride("font_color", UIStyle.ColorGold);
		titleLbl.AddThemeFontSizeOverride("font_size", 15);
		cardVBox.AddChild(titleLbl);

		var countLbl = new Label();
		countLbl.Name = "AssetCountLbl_Label";
		string sizeText = assetPack.TotalSizeBytes > 0 ? FormatBytes(assetPack.TotalSizeBytes) : string.Empty;
		countLbl.Text = string.IsNullOrEmpty(sizeText)
			? string.Format(TranslationServer.Translate("{0} verified assets"), assetPack.AssetCount)
			: string.Format(TranslationServer.Translate("{0} verified assets  •  {1}"), assetPack.AssetCount, sizeText);
		countLbl.HorizontalAlignment = HorizontalAlignment.Center;
		countLbl.AddThemeColorOverride("font_color", UIStyle.ColorCyanGlow);
		countLbl.AddThemeFontSizeOverride("font_size", 12);
		cardVBox.AddChild(countLbl);

		var descLbl = new Label();
		descLbl.Name = "AssetDescLbl_Label";
		descLbl.Text = TranslationServer.Translate("Shared verified assets signed by this creator for use in Realm maps.");
		descLbl.AutowrapMode = TextServer.AutowrapMode.WordSmart;
		descLbl.CustomMinimumSize = new Vector2(180, 0);
		descLbl.SizeFlagsHorizontal = SizeFlags.ExpandFill;
		descLbl.HorizontalAlignment = HorizontalAlignment.Center;
		descLbl.AddThemeColorOverride("font_color", new Color(0.70f, 0.74f, 0.80f));
		descLbl.AddThemeFontSizeOverride("font_size", 11);
		cardVBox.AddChild(descLbl);

		return cardPanel;
	}

	private void DisplayForYouMessage(string text, bool showRetry)
	{
		ClearContainer(_forYouGrid);

		var box = new VBoxContainer();
		box.Name = "ForYouMessageBox_VBoxContainer";
		box.SizeFlagsHorizontal = SizeFlags.ExpandFill;
		box.AddThemeConstantOverride("separation", 12);

		var lbl = new Label();
		lbl.Name = "ForYouMessage_Label";
		lbl.Text = text;
		lbl.AutowrapMode = TextServer.AutowrapMode.WordSmart;
		lbl.CustomMinimumSize = new Vector2(300, 0);
		lbl.SizeFlagsHorizontal = SizeFlags.ExpandFill;
		lbl.AddThemeColorOverride("font_color", new Color(0.75f, 0.78f, 0.84f));
		lbl.AddThemeFontSizeOverride("font_size", 14);
		box.AddChild(lbl);

		if (showRetry)
		{
			var retryBtn = new Button();
			retryBtn.Name = "RetryCreatorsBtn_Button";
			retryBtn.AddThemeConstantOverride("icon_max_width", 20);
			retryBtn.CustomMinimumSize = new Vector2(130, 34);
			retryBtn.SizeFlagsHorizontal = SizeFlags.ShrinkBegin;
			UIStyle.ApplyButtonText(retryBtn, TranslationServer.Translate("RETRY"), 13);
			UIStyle.ApplyButtonStyleBoxesClearingFocus(retryBtn);
			retryBtn.Pressed += () => _ = LoadAllDataAsync();
			box.AddChild(retryBtn);
		}

		_forYouGrid.AddChild(box);
	}

	private void DisplayPortfolioMessage(string text)
	{
		ClearContainer(_portfolioGrid);
		var msgLbl = new Label();
		msgLbl.Name = "EmptyMsgLbl_Label";
		msgLbl.Text = text;
		msgLbl.AutowrapMode = TextServer.AutowrapMode.WordSmart;
		msgLbl.CustomMinimumSize = new Vector2(260, 0);
		msgLbl.SizeFlagsHorizontal = SizeFlags.ExpandFill;
		msgLbl.AddThemeColorOverride("font_color", new Color(0.7f, 0.72f, 0.78f));
		msgLbl.AddThemeFontSizeOverride("font_size", 14);
		_portfolioGrid.AddChild(msgLbl);
	}

	private (PanelContainer Container, ColorRect Background, Label Initials) CreateCreatorCrestAvatar(int size, string creatorName)
	{
		var container = new PanelContainer();
		container.Name = "AvatarContainer_PanelContainer";
		container.CustomMinimumSize = new Vector2(size, size);
		container.AddThemeStyleboxOverride("panel", UIStyle.CreateDiscoveryAvatarPlate(size));

		var bg = new ColorRect();
		bg.Name = "AvatarTint_ColorRect";
		bg.SetAnchorsPreset(LayoutPreset.FullRect);
		container.AddChild(bg);

		var initialsLbl = new Label();
		initialsLbl.Name = "AvatarInitials_Label";
		initialsLbl.SetAnchorsPreset(LayoutPreset.FullRect);
		initialsLbl.HorizontalAlignment = HorizontalAlignment.Center;
		initialsLbl.VerticalAlignment = VerticalAlignment.Center;
		initialsLbl.AddThemeFontSizeOverride("font_size", Math.Max(14, size / 2));
		initialsLbl.AddThemeColorOverride("font_color", UIStyle.ColorGold);
		container.AddChild(initialsLbl);

		ApplyAvatarVisual(bg, initialsLbl, creatorName);
		return (container, bg, initialsLbl);
	}

	private static void ApplyAvatarVisual(ColorRect bg, Label initialsLbl, string creatorName)
	{
		string trimmed = creatorName?.Trim() ?? string.Empty;
		if (string.IsNullOrEmpty(trimmed) || trimmed == "?")
		{
			initialsLbl.Text = "?";
			bg.Color = new Color(0.16f, 0.18f, 0.24f, 0.9f);
			return;
		}

		string[] words = trimmed.Split(' ', StringSplitOptions.RemoveEmptyEntries);
		initialsLbl.Text = words.Length >= 2
			? $"{char.ToUpperInvariant(words[0][0])}{char.ToUpperInvariant(words[1][0])}"
			: char.ToUpperInvariant(trimmed[0]).ToString();

		int hash = StringComparer.OrdinalIgnoreCase.GetHashCode(trimmed);
		float hue = Math.Abs(hash % 360) / 360f;
		bg.Color = Color.FromHsv(hue, 0.35f, 0.26f, 0.95f);
	}

	private (PanelContainer Container, TextureRect Image) CreateCardThumbnailFrame(int width, int height, string badgeText)
	{
		var container = new PanelContainer();
		container.Name = "ThumbContainer_PanelContainer";
		container.CustomMinimumSize = new Vector2(width, height);
		container.AddThemeStyleboxOverride("panel", UIStyle.CreateDiscoveryThumbnailPlate());

		var textureRect = new TextureRect();
		textureRect.Name = "ThumbTexture_TextureRect";
		textureRect.SetAnchorsPreset(LayoutPreset.FullRect);
		textureRect.ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize;
		textureRect.StretchMode = TextureRect.StretchModeEnum.KeepAspectCovered;
		container.AddChild(textureRect);

		if (!string.IsNullOrWhiteSpace(badgeText))
		{
			var badgePanel = new PanelContainer();
			badgePanel.Name = "BadgePanel_PanelContainer";
			badgePanel.SetAnchorsPreset(LayoutPreset.TopRight);
			badgePanel.GrowHorizontal = GrowDirection.Begin;
			badgePanel.OffsetLeft = -82;
			badgePanel.OffsetTop = 6;
			badgePanel.OffsetRight = -6;
			badgePanel.OffsetBottom = 24;
			badgePanel.AddThemeStyleboxOverride("panel", UIStyle.CreateDiscoveryBadge());

			var badgeLabel = new Label();
			badgeLabel.Name = "BadgeLabel_Label";
			badgeLabel.Text = badgeText;
			badgeLabel.HorizontalAlignment = HorizontalAlignment.Center;
			badgeLabel.AddThemeColorOverride("font_color", UIStyle.ColorGold);
			badgeLabel.AddThemeFontSizeOverride("font_size", 9);
			badgePanel.AddChild(badgeLabel);
			container.AddChild(badgePanel);
		}

		return (container, textureRect);
	}

	private static string FormatCreatorSummary(CreatorInfo creator, int localMinutesPlayed)
	{
		var parts = new List<string>();
		if (localMinutesPlayed > 0)
		{
			parts.Add(string.Format(TranslationServer.Translate("{0} min played"), localMinutesPlayed));
		}

		if (creator.PublishedMapCount > 0)
		{
			parts.Add(string.Format(TranslationServer.Translate("{0} maps"), creator.PublishedMapCount));
		}

		if (creator.TotalGamesPlayed > 0)
		{
			parts.Add(string.Format(TranslationServer.Translate("{0} plays"), creator.TotalGamesPlayed));
		}

		if (creator.AssetCount > 0 && creator.PublishedMapCount == 0)
		{
			parts.Add(string.Format(TranslationServer.Translate("{0} assets"), creator.AssetCount));
		}

		return parts.Count > 0
			? string.Join("  •  ", parts)
			: TranslationServer.Translate("Community creator");
	}

	private static string FormatBytes(long totalBytes)
	{
		return totalBytes > 1048576
			? $"{(double)totalBytes / 1048576.0:F1} MB"
			: $"{Math.Max(1.0, (double)totalBytes / 1024.0):F0} KB";
	}

	private static bool IsSafeHttpUrl(string value)
	{
		return Uri.TryCreate(value.Trim(), UriKind.Absolute, out Uri? uri) &&
		       (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps);
	}

	private static string GetRegistryServerUrl()
	{
		return GodotObject.IsInstanceValid(GameContext.Lobby)
			? GameContext.Lobby.RegistryServerUrl
			: ServersConfigHelper.GetDefaultServerUrl();
	}

	private static void ClearContainer(Node container)
	{
		foreach (Node child in container.GetChildren())
		{
			container.RemoveChild(child);
			child.QueueFree();
		}
	}
}
