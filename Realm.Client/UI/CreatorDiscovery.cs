using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Godot;
using Realm.Client.Network;
using Realm.Shared.Distribution;
using Realm.Client.Services;
using static Godot.Control;
using static Godot.CanvasItem;

namespace Realm.Client.UI;

[GlobalClass]
public partial class CreatorDiscovery : Control
{
	private Button _backButton = null!;
	private TabContainer _tabContainer = null!;
	private GridContainer _forYouGrid = null!;
	private OptionButton _creatorDropdown = null!;
	private GridContainer _portfolioGrid = null!;
	private RichTextLabel _contactInfo = null!;
	private Label _selectedCreatorNameLabel = null!;
	private Button _forYouTabBtn = null!;
	private Button _portfolioTabBtn = null!;


	private List<CreatorInfo> _creatorsList = new();

	public override void _EnterTree()
	{
		Realm.Client.Services.GameContext.EnsureServices();
		SetFpsCenter(true);
	}

	public override void _ExitTree()
	{
		SetFpsCenter(false);
	}

	private void SetFpsCenter(bool center)
	{
		var fpsNode = GetTree()?.Root?.GetNodeOrNull<Control>("Main/CanvasLayer/FPS");
		if (fpsNode != null)
		{
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
	}

	public override void _Ready()
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
		centerVBox.AnchorLeft = 0.12f;
		centerVBox.AnchorRight = 0.88f;
		centerVBox.AnchorTop = 0.12f;
		centerVBox.AnchorBottom = 0.92f;
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

		SelectTab(0);

		LoadForYouData();
		LoadPortfolioData();
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
		forYouTab.Name = TranslationServer.Translate("For You");
		forYouTab.AddThemeConstantOverride("margin_top", 36);
		forYouTab.AddThemeConstantOverride("margin_bottom", 36);
		forYouTab.AddThemeConstantOverride("margin_left", 36);
		forYouTab.AddThemeConstantOverride("margin_right", 36);
		_tabContainer.AddChild(forYouTab);

		var forYouVBox = new VBoxContainer();
		forYouVBox.Name = "ForYouVBox_VBoxContainer";
		forYouVBox.AddThemeConstantOverride("separation", 20);
		forYouTab.AddChild(forYouVBox);

		var bannerPanel = new PanelContainer();
		bannerPanel.Name = "BannerPanel_PanelContainer";
		bannerPanel.AddThemeStyleboxOverride("panel", UIStyle.CreateDiscoveryCard(new Color(0.14f, 0.16f, 0.20f, 0.9f), UIStyle.ColorCyanGlowDim));
		forYouVBox.AddChild(bannerPanel);

		var bannerHBox = new HBoxContainer();
		bannerHBox.Name = "BannerHBox_HBoxContainer";
		bannerHBox.AddThemeConstantOverride("separation", 16);
		bannerPanel.AddChild(bannerHBox);

		var bannerIconBox = CreateAvatarPlaceholder(50);
		bannerHBox.AddChild(bannerIconBox);

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
		bannerSub.Text = TranslationServer.Translate("Based on your playtime, here are the top creators who contributed to the maps you love!");
		bannerSub.AutowrapMode = TextServer.AutowrapMode.WordSmart;
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
		portfolioTab.Name = TranslationServer.Translate("Portfolio Browser");
		portfolioTab.AddThemeConstantOverride("margin_top", 36);
		portfolioTab.AddThemeConstantOverride("margin_bottom", 36);
		portfolioTab.AddThemeConstantOverride("margin_left", 36);
		portfolioTab.AddThemeConstantOverride("margin_right", 36);
		_tabContainer.AddChild(portfolioTab);

		var portVBox = new VBoxContainer();
		portVBox.Name = "PortVBox_VBoxContainer";
		portVBox.AddThemeConstantOverride("separation", 16);
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
		topBarHBox.AddThemeConstantOverride("separation", 16);
		profileVBox.AddChild(topBarHBox);

		var selectLbl = new Label();
		selectLbl.Name = "SelectLbl_Label";
		selectLbl.Text = TranslationServer.Translate("Select Creator:");
		selectLbl.AddThemeColorOverride("font_color", UIStyle.ColorGold);
		selectLbl.AddThemeFontSizeOverride("font_size", 14);
		topBarHBox.AddChild(selectLbl);

		_creatorDropdown = new OptionButton();
		_creatorDropdown.Name = "CreatorDropdown_OptionButton";
		_creatorDropdown.CustomMinimumSize = new Vector2(240, 36);
		UIStyle.ApplyDropdownButton(_creatorDropdown);
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

		var avatarBox = CreateAvatarPlaceholder(70);
		bioHBox.AddChild(avatarBox);

		_contactInfo = new RichTextLabel();
		_contactInfo.Name = "ContactInfo_RichTextLabel";
		_contactInfo.BbcodeEnabled = true;
		_contactInfo.SizeFlagsHorizontal = SizeFlags.ExpandFill;
		_contactInfo.CustomMinimumSize = new Vector2(0, 70);
		_contactInfo.AddThemeFontSizeOverride("normal_font_size", 13);
		bioHBox.AddChild(_contactInfo);

		var showcaseHeader = new Label();
		showcaseHeader.Name = "ShowcaseHeader_Label";
		showcaseHeader.Text = TranslationServer.Translate("PUBLISHED ASSET PACKS & GREENLIT MAPS");
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
		_portfolioGrid.AddThemeConstantOverride("h_separation", 20);
		_portfolioGrid.AddThemeConstantOverride("v_separation", 20);
		portfolioScroll.AddChild(_portfolioGrid);
	}

	private void LoadForYouData()
	{
		string statsPath = ProjectSettings.GlobalizePath("user://appdata/playtime_stats.json");
		Dictionary<string, int> localCreators = CreatorDiscoveryService.LoadPlaytimeCreators(statsPath);

		if (localCreators.Count == 0)
		{
			localCreators = new Dictionary<string, int>
			{
				{ "Realm Builder", 120 },
				{ "Ice Mage", 45 },
				{ "Pathfinder", 30 }
			};
		}

		foreach (Node child in _forYouGrid.GetChildren())
		{
			child.QueueFree();
		}

		foreach (var kvp in localCreators)
		{
			var cardPanel = new PanelContainer();
			cardPanel.Name = "CardPanel_PanelContainer";
			cardPanel.TextureFilter = TextureFilterEnum.LinearWithMipmaps;
			cardPanel.SizeFlagsHorizontal = SizeFlags.ExpandFill;
			cardPanel.AddThemeStyleboxOverride("panel", UIStyle.CreateDiscoveryCard(new Color(0.12f, 0.14f, 0.17f, 0.95f), UIStyle.ColorBronze));

			var cardHBox = new HBoxContainer();
			cardHBox.Name = "CardHBox_HBoxContainer";
			cardHBox.AddThemeConstantOverride("separation", 14);
			cardPanel.AddChild(cardHBox);

			var avatarBox = CreateAvatarPlaceholder(64);
			cardHBox.AddChild(avatarBox);

			var infoVBox = UIStyle.CreateInfoColumn();
			infoVBox.Alignment = BoxContainer.AlignmentMode.Center;
			cardHBox.AddChild(infoVBox);

			var nameLbl = new Label();
			nameLbl.Name = "NameLbl_Label";
			nameLbl.Text = kvp.Key;
			nameLbl.AddThemeColorOverride("font_color", UIStyle.ColorGold);
			nameLbl.AddThemeFontSizeOverride("font_size", 16);
			infoVBox.AddChild(nameLbl);

			var chipPanel = new PanelContainer();
			chipPanel.Name = "ChipPanel_PanelContainer";
			chipPanel.AddThemeStyleboxOverride("panel", UIStyle.CreateDiscoveryChip());
			var ptLbl = new Label();
			ptLbl.Name = "PtLbl_Label";
			ptLbl.Text = $"{kvp.Value} {TranslationServer.Translate("mins played on maps/assets")}";
			ptLbl.AddThemeColorOverride("font_color", UIStyle.ColorCyanGlow);
			ptLbl.AddThemeFontSizeOverride("font_size", 12);
			chipPanel.AddChild(ptLbl);
			infoVBox.AddChild(chipPanel);

			var actionBtn = new Button();
			actionBtn.Name = "ActionBtn_Button";
			actionBtn.AddThemeConstantOverride("icon_max_width", 20);
			actionBtn.CustomMinimumSize = new Vector2(170, 36);
			actionBtn.SizeFlagsVertical = SizeFlags.ShrinkCenter;
			actionBtn.SizeFlagsHorizontal = SizeFlags.ShrinkEnd;
			UIStyle.ApplyButtonText(actionBtn, TranslationServer.Translate("VIEW PORTFOLIO"), 12);
			UIStyle.ApplyFlatButton(actionBtn);

			string targetCreator = kvp.Key;
			actionBtn.Pressed += () => SwitchToPortfolioForCreator(targetCreator);
			cardHBox.AddChild(actionBtn);

			_forYouGrid.AddChild(cardPanel);
		}
	}

	private async void LoadPortfolioData()
	{
		string seedServerUrl = GodotObject.IsInstanceValid(GameContext.Lobby) ? GameContext.Lobby.RegistryServerUrl : ServersConfigHelper.GetDefaultServerUrl();
		List<CreatorInfo> creators = await CreatorDiscoveryService.LoadCreatorsAsync(seedServerUrl);
		if (creators.Count > 0)
		{
			_creatorsList = creators;
			_creatorDropdown.Clear();
			for (int i = 0; i < _creatorsList.Count; i++)
			{
				_creatorDropdown.AddItem(_creatorsList[i].Username, i);
			}

			_creatorDropdown.ItemSelected += (idx) => UpdatePortfolioView(idx);
			UpdatePortfolioView(0);
			return;
		}

		_creatorDropdown.Clear();
		_creatorDropdown.AddItem("Realm Builder", 0);
		_creatorDropdown.AddItem("Ice Mage", 1);
		_creatorDropdown.ItemSelected += (idx) => UpdatePortfolioViewFallback(idx);
		UpdatePortfolioViewFallback(0);
	}

	// The dropdown's ItemSelected signal and the grid's own loops call this, so the signature has to stay void.
	// The awaited work lives in a Task-returning method and the wrapper logs whatever it throws: an async void
	// body that throws with no catch has nobody to hand the exception to.
	private async void UpdatePortfolioView(long index)
	{
		try
		{
			await UpdatePortfolioViewAsync(index);
		}
		catch (Exception exception)
		{
			GameLog.LogError($"[CreatorDiscovery] Failed to load portfolio view: {exception}");
		}
	}

	private async Task UpdatePortfolioViewAsync(long index)
	{
		int idx = (int)index;
		if (idx < 0 || idx >= _creatorsList.Count) return;
		var creator = _creatorsList[idx];

		UpdateCreatorLabels(creator);
		ClearPortfolioGrid();

		string seedServerUrl = GodotObject.IsInstanceValid(GameContext.Lobby) ? GameContext.Lobby.RegistryServerUrl : ServersConfigHelper.GetDefaultServerUrl();
		bool loaded = await TryLoadAndDisplayPortfolioAssets(seedServerUrl, creator);
		if (!loaded)
		{
			DisplayEmptyPortfolioMessage();
		}
	}

	private void UpdateCreatorLabels(CreatorInfo creator)
	{
		if (_selectedCreatorNameLabel != null)
		{
			_selectedCreatorNameLabel.Text = creator.Username;
		}

		string contactText = "";
		if (!string.IsNullOrEmpty(creator.DonationLink)) contactText += $"{TranslationServer.Translate("Donation Link")}: [color=cyan]{creator.DonationLink}[/color]\n";
		if (!string.IsNullOrEmpty(creator.ContactInfo)) contactText += $"{TranslationServer.Translate("Contact Info")}: [color=cyan]{creator.ContactInfo}[/color]";
		if (string.IsNullOrEmpty(contactText)) contactText = TranslationServer.Translate("No contact/donation links provided.");
		_contactInfo.Text = contactText;
	}

	private void ClearPortfolioGrid()
	{
		foreach (Node n in _portfolioGrid.GetChildren())
		{
			n.QueueFree();
		}
	}

	private async Task<bool> TryLoadAndDisplayPortfolioAssets(string seedServerUrl, CreatorInfo creator)
	{
		List<PortfolioAsset> assets = await CreatorDiscoveryService.LoadPortfolioAsync(seedServerUrl, creator);
		if (assets.Count == 0) return false;

		foreach (var asset in assets)
		{
			var card = CreateAssetCard(TranslationServer.Translate("Greenlit Asset Pack"), asset.Hash);
			_portfolioGrid.AddChild(card);
		}

		return true;
	}

	private void DisplayEmptyPortfolioMessage()
	{
		var emptyPanel = UIStyle.CreateDiscoveryCard(new Color(0.1f, 0.11f, 0.14f, 0.7f), new Color(0.3f, 0.3f, 0.35f));
		var emptyBox = new PanelContainer();
		emptyBox.Name = "EmptyBox_PanelContainer";
		emptyBox.AddThemeStyleboxOverride("panel", emptyPanel);
		var emptyLbl = new Label();
		emptyLbl.Name = "EmptyLbl_Label";
		emptyLbl.Text = TranslationServer.Translate("No greenlit assets found in portfolio.");
		emptyLbl.HorizontalAlignment = HorizontalAlignment.Center;
		emptyBox.AddChild(emptyLbl);
		_portfolioGrid.AddChild(emptyBox);
	}

	private void UpdatePortfolioViewFallback(long idx)
	{
		string name = idx == 0 ? "Realm Builder" : "Ice Mage";
		if (_selectedCreatorNameLabel != null)
		{
			_selectedCreatorNameLabel.Text = name;
		}

		_contactInfo.Text = idx == 0 ? $"{TranslationServer.Translate("Support me on Patreon")}: [color=cyan]patreon.com/realmbuilder[/color]" : $"{TranslationServer.Translate("Donate to my Ko-fi")}: [color=cyan]ko-fi.com/icemage[/color]";

		foreach (Node n in _portfolioGrid.GetChildren())
		{
			n.QueueFree();
		}

		var numAssets = idx == 0 ? 3 : 1;
		for (int i = 0; i < numAssets; i++)
		{
			var card = CreateAssetCard($"{TranslationServer.Translate("Greenlit Asset Pack")} #{i + 1}", $"hash_{i + 1000}_fallback");
			_portfolioGrid.AddChild(card);
		}
	}

	private void SwitchToPortfolioForCreator(string creatorName)
	{
		SelectTab(1);
		for (int i = 0; i < _creatorDropdown.ItemCount; i++)
		{
			if (_creatorDropdown.GetItemText(i).Equals(creatorName, StringComparison.OrdinalIgnoreCase))
			{
				_creatorDropdown.Select(i);
				if (_creatorsList.Count > i)
				{
					UpdatePortfolioView(i);
				}
				else
				{
					UpdatePortfolioViewFallback(i);
				}

				break;
			}
		}
	}

	private PanelContainer CreateAssetCard(string title, string hash)
	{
		var cardPanel = new PanelContainer();
		cardPanel.Name = "CardPanel_PanelContainer";
		cardPanel.TextureFilter = TextureFilterEnum.LinearWithMipmaps;
		cardPanel.CustomMinimumSize = new Vector2(210, 190);
		cardPanel.SizeFlagsHorizontal = SizeFlags.ExpandFill;
		cardPanel.AddThemeStyleboxOverride("panel", UIStyle.CreateCreatorDiscoveryMapFrameStyle());

		var cardVBox = new VBoxContainer();
		cardVBox.Name = "CardVBox_VBoxContainer";
		cardVBox.AddThemeConstantOverride("separation", 8);
		cardPanel.AddChild(cardVBox);

		var thumbContainer = CreateThumbnailPlaceholder(210, 110);
		cardVBox.AddChild(thumbContainer);

		var titleLbl = new Label();
		titleLbl.Name = "TitleLbl_Label";
		titleLbl.Text = title;
		titleLbl.HorizontalAlignment = HorizontalAlignment.Center;
		titleLbl.AddThemeColorOverride("font_color", UIStyle.ColorGold);
		titleLbl.AddThemeFontSizeOverride("font_size", 13);
		cardVBox.AddChild(titleLbl);

		var hashLbl = new Label();
		hashLbl.Name = "HashLbl_Label";
		string displayHash = hash.Length > 12 ? hash.Substring(0, 12) + "..." : hash;
		hashLbl.Text = displayHash;
		hashLbl.HorizontalAlignment = HorizontalAlignment.Center;
		hashLbl.AddThemeFontSizeOverride("font_size", 10);
		hashLbl.AddThemeColorOverride("font_color", new Color(0.6f, 0.62f, 0.68f));
		cardVBox.AddChild(hashLbl);

		return cardPanel;
	}

	private PanelContainer CreateAvatarPlaceholder(int size)
	{
		var container = new PanelContainer();
		container.Name = "Container_PanelContainer";
		container.CustomMinimumSize = new Vector2(size, size);

		container.AddThemeStyleboxOverride("panel", UIStyle.CreateDiscoveryAvatarPlate(size));

		var textureRect = new TextureRect();
		textureRect.Name = "TextureRect_TextureRect";
		textureRect.SetAnchorsPreset(LayoutPreset.FullRect);
		textureRect.ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize;
		textureRect.StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered;
		container.AddChild(textureRect);

		return container;
	}

	private PanelContainer CreateThumbnailPlaceholder(int width, int height)
	{
		var container = new PanelContainer();
		container.Name = "Container_PanelContainer";
		container.CustomMinimumSize = new Vector2(width, height);

		container.AddThemeStyleboxOverride("panel", UIStyle.CreateDiscoveryThumbnailPlate());

		var textureRect = new TextureRect();
		textureRect.Name = "TextureRect_TextureRect";
		textureRect.SetAnchorsPreset(LayoutPreset.FullRect);
		textureRect.ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize;
		textureRect.StretchMode = TextureRect.StretchModeEnum.KeepAspectCovered;
		container.AddChild(textureRect);

		var badgePanel = new PanelContainer();
		badgePanel.Name = "BadgePanel_PanelContainer";
		badgePanel.SetAnchorsPreset(LayoutPreset.TopRight);
		badgePanel.OffsetLeft = -65;
		badgePanel.OffsetTop = 6;
		badgePanel.OffsetRight = -6;
		badgePanel.OffsetBottom = 24;
		badgePanel.AddThemeStyleboxOverride("panel", UIStyle.CreateDiscoveryBadge());

		var badgeLabel = new Label();
		badgeLabel.Name = "BadgeLabel_Label";
		badgeLabel.Text = TranslationServer.Translate("GREENLIT");
		badgeLabel.HorizontalAlignment = HorizontalAlignment.Center;
		badgeLabel.AddThemeColorOverride("font_color", UIStyle.ColorGold);
		badgeLabel.AddThemeFontSizeOverride("font_size", 9);
		badgePanel.AddChild(badgeLabel);
		container.AddChild(badgePanel);

		return container;
	}

}
