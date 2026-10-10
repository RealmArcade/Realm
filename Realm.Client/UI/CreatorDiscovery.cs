using Godot;
using Realm.Shared.Distribution;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;

namespace Realm.Client.UI;

public partial class CreatorDiscovery : Control
{
	private Button _backButton;
	private TabContainer _tabContainer;
	private GridContainer _forYouGrid;
	private OptionButton _creatorDropdown;
	private GridContainer _portfolioGrid;
	private RichTextLabel _contactInfo;
	private Label _selectedCreatorNameLabel;
	private Button _forYouTabBtn;
	private Button _portfolioTabBtn;

	private struct CreatorInfo
	{
		public string PublicKey { get; set; }
		public string Username { get; set; }
		public string DonationLink { get; set; }
		public string ContactInfo { get; set; }
	}

	private struct PortfolioAsset
	{
		public string Hash { get; set; }
		public string AuthorUsername { get; set; }
	}

	private List<CreatorInfo> _creatorsList = new();

	public override void _EnterTree()
	{
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
		bgPanel.SetAnchorsPreset(LayoutPreset.FullRect);
		bgPanel.AddThemeStyleboxOverride("panel", UIStyle.CreateCreatorDiscoveryBg());
		AddChild(bgPanel);

		var topHeaderPanel = new PanelContainer();
		topHeaderPanel.SetAnchorsPreset(LayoutPreset.TopWide);
		topHeaderPanel.CustomMinimumSize = new Vector2(0, 70);
		topHeaderPanel.AddThemeStyleboxOverride("panel", CreateHeaderPanelStyle());
		AddChild(topHeaderPanel);

		var topHeaderHBox = new HBoxContainer();
		topHeaderHBox.AddThemeConstantOverride("separation", 20);
		topHeaderHBox.Alignment = BoxContainer.AlignmentMode.Begin;
		topHeaderPanel.AddChild(topHeaderHBox);

		_backButton = new Button();
		_backButton.AddThemeConstantOverride("icon_max_width", 24);
		_backButton.CustomMinimumSize = new Vector2(120, 42);
		UIStyle.ApplyButtonText(_backButton, "◀ BACK", 16);
		_backButton.AddThemeStyleboxOverride("normal", UIStyle.CreateButtonNormal());
		_backButton.AddThemeStyleboxOverride("hover", UIStyle.CreateButtonHover());
		_backButton.AddThemeStyleboxOverride("pressed", UIStyle.CreateButtonPressed());
		_backButton.Pressed += () => UIManager.Instance.TransitionTo(GameScreen.MainMenu);
		topHeaderHBox.AddChild(_backButton);

		var titleLabel = new Label();
		UIStyle.ApplyTitle(titleLabel, "CREATOR DISCOVERY", 28);
		titleLabel.SizeFlagsHorizontal = SizeFlags.ExpandFill;
		titleLabel.HorizontalAlignment = HorizontalAlignment.Left;
		topHeaderHBox.AddChild(titleLabel);

		var badgePanel = new PanelContainer();
		badgePanel.AddThemeStyleboxOverride("panel", CreateBadgeStyle());
		var badgeLabel = new Label();
		badgeLabel.Text = TranslationServer.Translate("COMMUNITY HUB");
		badgeLabel.AddThemeColorOverride("font_color", UIStyle.ColorGold);
		badgeLabel.AddThemeFontSizeOverride("font_size", 12);
		badgePanel.AddChild(badgeLabel);
		topHeaderHBox.AddChild(badgePanel);

		var centerVBox = new VBoxContainer();
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
		navHBox.Alignment = BoxContainer.AlignmentMode.Center;
		navHBox.AddThemeConstantOverride("separation", 16);
		centerVBox.AddChild(navHBox);

		_forYouTabBtn = new Button();
		_forYouTabBtn.AddThemeConstantOverride("icon_max_width", 20);
		_forYouTabBtn.CustomMinimumSize = new Vector2(150, 38);
		UIStyle.ApplyButtonText(_forYouTabBtn, "FOR YOU", 14);
		_forYouTabBtn.Pressed += () => SelectTab(0);
		navHBox.AddChild(_forYouTabBtn);

		_portfolioTabBtn = new Button();
		_portfolioTabBtn.AddThemeConstantOverride("icon_max_width", 20);
		_portfolioTabBtn.CustomMinimumSize = new Vector2(180, 38);
		UIStyle.ApplyButtonText(_portfolioTabBtn, "PORTFOLIO BROWSER", 14);
		_portfolioTabBtn.Pressed += () => SelectTab(1);
		navHBox.AddChild(_portfolioTabBtn);

		_tabContainer = new TabContainer();
		_tabContainer.TabsVisible = false;
		_tabContainer.TextureFilter = TextureFilterEnum.LinearWithMipmaps;
		_tabContainer.SizeFlagsVertical = SizeFlags.ExpandFill;
		_tabContainer.SizeFlagsHorizontal = SizeFlags.ExpandFill;
		_tabContainer.AddThemeStyleboxOverride("panel", CreateBackdropContainerStyle());
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
		_forYouTabBtn.AddThemeStyleboxOverride("normal", UIStyle.CreateCreatorDiscoveryTabButtonStyle(index == 0, false, false));
		_forYouTabBtn.AddThemeStyleboxOverride("hover", UIStyle.CreateCreatorDiscoveryTabButtonStyle(index == 0, true, false));
		_forYouTabBtn.AddThemeStyleboxOverride("pressed", UIStyle.CreateCreatorDiscoveryTabButtonStyle(index == 0, false, true));

		_portfolioTabBtn.AddThemeStyleboxOverride("normal", UIStyle.CreateCreatorDiscoveryTabButtonStyle(index == 1, false, false));
		_portfolioTabBtn.AddThemeStyleboxOverride("hover", UIStyle.CreateCreatorDiscoveryTabButtonStyle(index == 1, true, false));
		_portfolioTabBtn.AddThemeStyleboxOverride("pressed", UIStyle.CreateCreatorDiscoveryTabButtonStyle(index == 1, false, true));
	}

	private StyleBoxFlat CreateTabButtonStyle(bool isActive, bool isHover)
	{
		var style = new StyleBoxFlat();
		style.BgColor = isActive
			? new Color(0.14f, 0.16f, 0.22f, 0.95f)
			: (isHover ? new Color(0.10f, 0.11f, 0.15f, 0.85f) : new Color(0.07f, 0.08f, 0.11f, 0.75f));
		style.BorderColor = isActive ? UIStyle.ColorGold : (isHover ? UIStyle.ColorCyanGlowDim : new Color(0.35f, 0.35f, 0.40f, 0.6f));
		style.SetBorderWidthAll(1);
		if (isActive)
		{
			style.BorderWidthBottom = 3;
		}
		style.CornerRadiusTopLeft = 6;
		style.CornerRadiusTopRight = 6;
		style.CornerRadiusBottomLeft = 6;
		style.CornerRadiusBottomRight = 6;
		style.ContentMarginLeft = 16;
		style.ContentMarginRight = 16;
		style.ContentMarginTop = 8;
		style.ContentMarginBottom = 8;
		return style;
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
		forYouVBox.AddThemeConstantOverride("separation", 20);
		forYouTab.AddChild(forYouVBox);

		var bannerPanel = new PanelContainer();
		bannerPanel.AddThemeStyleboxOverride("panel", CreateCardStyle(new Color(0.14f, 0.16f, 0.20f, 0.9f), UIStyle.ColorCyanGlowDim));
		forYouVBox.AddChild(bannerPanel);

		var bannerHBox = new HBoxContainer();
		bannerHBox.AddThemeConstantOverride("separation", 16);
		bannerPanel.AddChild(bannerHBox);

		var bannerIconBox = CreateAvatarPlaceholder(50);
		bannerHBox.AddChild(bannerIconBox);

		var bannerVBox = new VBoxContainer();
		bannerVBox.SizeFlagsHorizontal = SizeFlags.ExpandFill;
		bannerHBox.AddChild(bannerVBox);

		var bannerTitle = new Label();
		bannerTitle.Text = TranslationServer.Translate("RECOMMENDED COMMUNITY CREATORS");
		bannerTitle.AddThemeColorOverride("font_color", UIStyle.ColorGold);
		bannerTitle.AddThemeFontSizeOverride("font_size", 16);
		bannerVBox.AddChild(bannerTitle);

		var bannerSub = new Label();
		bannerSub.Text = TranslationServer.Translate("Based on your playtime, here are the top creators who contributed to the maps you love!");
		bannerSub.AutowrapMode = TextServer.AutowrapMode.WordSmart;
		bannerSub.AddThemeColorOverride("font_color", new Color(0.8f, 0.82f, 0.88f));
		bannerSub.AddThemeFontSizeOverride("font_size", 13);
		bannerVBox.AddChild(bannerSub);

		var scrollContainer = new ScrollContainer();
		scrollContainer.SizeFlagsVertical = SizeFlags.ExpandFill;
		scrollContainer.HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled;
		forYouVBox.AddChild(scrollContainer);

		_forYouGrid = new GridContainer();
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
		portVBox.AddThemeConstantOverride("separation", 16);
		portfolioTab.AddChild(portVBox);

		var profileHeaderPanel = new PanelContainer();
		profileHeaderPanel.AddThemeStyleboxOverride("panel", CreateCardStyle(new Color(0.12f, 0.13f, 0.17f, 0.95f), UIStyle.ColorBronze));
		portVBox.AddChild(profileHeaderPanel);

		var profileVBox = new VBoxContainer();
		profileVBox.AddThemeConstantOverride("separation", 12);
		profileHeaderPanel.AddChild(profileVBox);

		var topBarHBox = new HBoxContainer();
		topBarHBox.AddThemeConstantOverride("separation", 16);
		profileVBox.AddChild(topBarHBox);

		var selectLbl = new Label();
		selectLbl.Text = TranslationServer.Translate("Select Creator:");
		selectLbl.AddThemeColorOverride("font_color", UIStyle.ColorGold);
		selectLbl.AddThemeFontSizeOverride("font_size", 14);
		topBarHBox.AddChild(selectLbl);

		_creatorDropdown = new OptionButton();
		_creatorDropdown.CustomMinimumSize = new Vector2(240, 36);
		_creatorDropdown.AddThemeStyleboxOverride("normal", UIStyle.CreateDropdownStyle(false, false));
		_creatorDropdown.AddThemeStyleboxOverride("hover", UIStyle.CreateDropdownStyle(true, false));
		_creatorDropdown.AddThemeStyleboxOverride("pressed", UIStyle.CreateDropdownStyle(false, true));
		topBarHBox.AddChild(_creatorDropdown);

		_selectedCreatorNameLabel = new Label();
		_selectedCreatorNameLabel.AddThemeColorOverride("font_color", UIStyle.ColorCyanGlow);
		_selectedCreatorNameLabel.AddThemeFontSizeOverride("font_size", 18);
		_selectedCreatorNameLabel.SizeFlagsHorizontal = SizeFlags.ExpandFill;
		_selectedCreatorNameLabel.HorizontalAlignment = HorizontalAlignment.Right;
		topBarHBox.AddChild(_selectedCreatorNameLabel);

		var bioHBox = new HBoxContainer();
		bioHBox.AddThemeConstantOverride("separation", 16);
		profileVBox.AddChild(bioHBox);

		var avatarBox = CreateAvatarPlaceholder(70);
		bioHBox.AddChild(avatarBox);

		_contactInfo = new RichTextLabel();
		_contactInfo.BbcodeEnabled = true;
		_contactInfo.SizeFlagsHorizontal = SizeFlags.ExpandFill;
		_contactInfo.CustomMinimumSize = new Vector2(0, 70);
		_contactInfo.AddThemeFontSizeOverride("normal_font_size", 13);
		bioHBox.AddChild(_contactInfo);

		var showcaseHeader = new Label();
		showcaseHeader.Text = TranslationServer.Translate("PUBLISHED ASSET PACKS & GREENLIT MAPS");
		showcaseHeader.AddThemeColorOverride("font_color", UIStyle.ColorGoldDull);
		showcaseHeader.AddThemeFontSizeOverride("font_size", 15);
		portVBox.AddChild(showcaseHeader);

		var portfolioScroll = new ScrollContainer();
		portfolioScroll.SizeFlagsVertical = SizeFlags.ExpandFill;
		portfolioScroll.HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled;
		portVBox.AddChild(portfolioScroll);

		_portfolioGrid = new GridContainer();
		_portfolioGrid.Columns = 3;
		_portfolioGrid.SizeFlagsHorizontal = SizeFlags.ExpandFill;
		_portfolioGrid.AddThemeConstantOverride("h_separation", 20);
		_portfolioGrid.AddThemeConstantOverride("v_separation", 20);
		portfolioScroll.AddChild(_portfolioGrid);
	}

	private void LoadForYouData()
	{
		string statsPath = ProjectSettings.GlobalizePath("user://appdata/playtime_stats.json");
		var localCreators = new Dictionary<string, int>();
		if (System.IO.File.Exists(statsPath))
		{
			try
			{
				string statsJson = System.IO.File.ReadAllText(statsPath);
				localCreators = JsonSerializer.Deserialize<Dictionary<string, int>>(statsJson) ?? localCreators;
			}
			catch {}
		}

		if (localCreators.Count == 0)
		{
			localCreators = new Dictionary<string, int> {
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
			cardPanel.TextureFilter = TextureFilterEnum.LinearWithMipmaps;
			cardPanel.SizeFlagsHorizontal = SizeFlags.ExpandFill;
			cardPanel.AddThemeStyleboxOverride("panel", CreateCardStyle(new Color(0.12f, 0.14f, 0.17f, 0.95f), UIStyle.ColorBronze));

			var cardHBox = new HBoxContainer();
			cardHBox.AddThemeConstantOverride("separation", 14);
			cardPanel.AddChild(cardHBox);

			var avatarBox = CreateAvatarPlaceholder(64);
			cardHBox.AddChild(avatarBox);

			var infoVBox = new VBoxContainer();
			infoVBox.SizeFlagsHorizontal = SizeFlags.ExpandFill;
			infoVBox.Alignment = BoxContainer.AlignmentMode.Center;
			cardHBox.AddChild(infoVBox);

			var nameLbl = new Label();
			nameLbl.Text = kvp.Key;
			nameLbl.AddThemeColorOverride("font_color", UIStyle.ColorGold);
			nameLbl.AddThemeFontSizeOverride("font_size", 16);
			infoVBox.AddChild(nameLbl);

			var chipPanel = new PanelContainer();
			chipPanel.AddThemeStyleboxOverride("panel", CreateChipStyle());
			var ptLbl = new Label();
			ptLbl.Text = $"{kvp.Value} {TranslationServer.Translate("mins played on maps/assets")}";
			ptLbl.AddThemeColorOverride("font_color", UIStyle.ColorCyanGlow);
			ptLbl.AddThemeFontSizeOverride("font_size", 12);
			chipPanel.AddChild(ptLbl);
			infoVBox.AddChild(chipPanel);

			var actionBtn = new Button();
			actionBtn.AddThemeConstantOverride("icon_max_width", 20);
			actionBtn.CustomMinimumSize = new Vector2(110, 36);
			actionBtn.SizeFlagsVertical = SizeFlags.ShrinkCenter;
			UIStyle.ApplyButtonText(actionBtn, "VIEW PORTFOLIO", 12);
			actionBtn.AddThemeStyleboxOverride("normal", UIStyle.CreateFlatButtonStyle(false, false));
			actionBtn.AddThemeStyleboxOverride("hover", UIStyle.CreateFlatButtonStyle(true, false));
			actionBtn.AddThemeStyleboxOverride("pressed", UIStyle.CreateFlatButtonStyle(false, true));

			string targetCreator = kvp.Key;
			actionBtn.Pressed += () => SwitchToPortfolioForCreator(targetCreator);
			cardHBox.AddChild(actionBtn);

			_forYouGrid.AddChild(cardPanel);
		}
	}

	private async void LoadPortfolioData()
	{
		string seedServerUrl = GodotObject.IsInstanceValid(Network.LobbyManager.Instance) ? Network.LobbyManager.Instance.RegistryServerUrl : ServersConfigHelper.GetDefaultServerUrl();
		try
		{
			using (var httpClient = new System.Net.Http.HttpClient())
			{
				var res = await httpClient.GetAsync(seedServerUrl + "/api/creators");
				if (res.IsSuccessStatusCode)
				{
					string json = await res.Content.ReadAsStringAsync();
					var creators = JsonSerializer.Deserialize<CreatorInfo[]>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
					if (creators != null && creators.Length > 0)
					{
						_creatorsList = creators.ToList();
						_creatorDropdown.Clear();
						for (int i = 0; i < _creatorsList.Count; i++)
						{
							_creatorDropdown.AddItem(_creatorsList[i].Username, i);
						}
						_creatorDropdown.ItemSelected += (idx) => UpdatePortfolioView(idx);
						UpdatePortfolioView(0);
						return;
					}
				}
			}
		}
		catch (Exception ex)
		{
			GD.PrintErr($"[CreatorDiscovery] Failed to load creators: {ex.Message}");
		}

		_creatorDropdown.Clear();
		_creatorDropdown.AddItem("Realm Builder", 0);
		_creatorDropdown.AddItem("Ice Mage", 1);
		_creatorDropdown.ItemSelected += (idx) => UpdatePortfolioViewFallback(idx);
		UpdatePortfolioViewFallback(0);
	}

	private async void UpdatePortfolioView(long index)
	{
		int idx = (int)index;
		if (idx < 0 || idx >= _creatorsList.Count) return;
		var creator = _creatorsList[idx];

		UpdateCreatorLabels(creator);
		ClearPortfolioGrid();

		string seedServerUrl = GodotObject.IsInstanceValid(Network.LobbyManager.Instance) ? Network.LobbyManager.Instance.RegistryServerUrl : ServersConfigHelper.GetDefaultServerUrl();
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

	private async System.Threading.Tasks.Task<bool> TryLoadAndDisplayPortfolioAssets(string seedServerUrl, CreatorInfo creator)
	{
		try
		{
			using var httpClient = new System.Net.Http.HttpClient();
			var res = await httpClient.GetAsync(seedServerUrl + $"/api/creators/{Uri.EscapeDataString(creator.PublicKey)}/portfolio");
			if (!res.IsSuccessStatusCode) return false;

			string json = await res.Content.ReadAsStringAsync();
			var assets = JsonSerializer.Deserialize<PortfolioAsset[]>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
			if (assets == null || assets.Length == 0) return false;

			foreach (var asset in assets)
			{
				var card = CreateAssetCard(TranslationServer.Translate("Greenlit Asset Pack"), asset.Hash);
				_portfolioGrid.AddChild(card);
			}
			return true;
		}
		catch (Exception ex)
		{
			GD.PrintErr($"[CreatorDiscovery] Failed to load portfolio: {ex.Message}");
			return false;
		}
	}

	private void DisplayEmptyPortfolioMessage()
	{
		var emptyPanel = CreateCardStyle(new Color(0.1f, 0.11f, 0.14f, 0.7f), new Color(0.3f, 0.3f, 0.35f));
		var emptyBox = new PanelContainer();
		emptyBox.AddThemeStyleboxOverride("panel", emptyPanel);
		var emptyLbl = new Label();
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
			var card = CreateAssetCard($"{TranslationServer.Translate("Greenlit Asset Pack")} #{i+1}", $"hash_{i+1000}_fallback");
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
		cardPanel.TextureFilter = TextureFilterEnum.LinearWithMipmaps;
		cardPanel.CustomMinimumSize = new Vector2(210, 190);
		cardPanel.SizeFlagsHorizontal = SizeFlags.ExpandFill;
		cardPanel.AddThemeStyleboxOverride("panel", UIStyle.CreateCreatorDiscoveryMapFrameStyle());

		var cardVBox = new VBoxContainer();
		cardVBox.AddThemeConstantOverride("separation", 8);
		cardPanel.AddChild(cardVBox);

		var thumbContainer = CreateThumbnailPlaceholder(210, 110);
		cardVBox.AddChild(thumbContainer);

		var titleLbl = new Label();
		titleLbl.Text = title;
		titleLbl.HorizontalAlignment = HorizontalAlignment.Center;
		titleLbl.AddThemeColorOverride("font_color", UIStyle.ColorGold);
		titleLbl.AddThemeFontSizeOverride("font_size", 13);
		cardVBox.AddChild(titleLbl);

		var hashLbl = new Label();
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
		container.CustomMinimumSize = new Vector2(size, size);

		var style = new StyleBoxFlat();
		style.BgColor = new Color(0.08f, 0.09f, 0.12f, 0.9f);
		style.BorderColor = UIStyle.ColorCyanGlowDim;
		style.SetBorderWidthAll(2);
		style.CornerRadiusTopLeft = size / 2;
		style.CornerRadiusTopRight = size / 2;
		style.CornerRadiusBottomLeft = size / 2;
		style.CornerRadiusBottomRight = size / 2;
		container.AddThemeStyleboxOverride("panel", style);

		var textureRect = new TextureRect();
		textureRect.SetAnchorsPreset(LayoutPreset.FullRect);
		textureRect.ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize;
		textureRect.StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered;
		container.AddChild(textureRect);

		return container;
	}

	private PanelContainer CreateThumbnailPlaceholder(int width, int height)
	{
		var container = new PanelContainer();
		container.CustomMinimumSize = new Vector2(width, height);

		var style = new StyleBoxFlat();
		style.BgColor = new Color(0.06f, 0.07f, 0.09f, 0.95f);
		style.BorderColor = new Color(0.25f, 0.27f, 0.32f);
		style.SetBorderWidthAll(1);
		style.CornerRadiusTopLeft = 6;
		style.CornerRadiusTopRight = 6;
		style.CornerRadiusBottomLeft = 6;
		style.CornerRadiusBottomRight = 6;
		container.AddThemeStyleboxOverride("panel", style);

		var textureRect = new TextureRect();
		textureRect.SetAnchorsPreset(LayoutPreset.FullRect);
		textureRect.ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize;
		textureRect.StretchMode = TextureRect.StretchModeEnum.KeepAspectCovered;
		container.AddChild(textureRect);

		var badgePanel = new PanelContainer();
		badgePanel.SetAnchorsPreset(LayoutPreset.TopRight);
		badgePanel.OffsetLeft = -65;
		badgePanel.OffsetTop = 6;
		badgePanel.OffsetRight = -6;
		badgePanel.OffsetBottom = 24;
		badgePanel.AddThemeStyleboxOverride("panel", CreateBadgeStyle());

		var badgeLabel = new Label();
		badgeLabel.Text = TranslationServer.Translate("GREENLIT");
		badgeLabel.HorizontalAlignment = HorizontalAlignment.Center;
		badgeLabel.AddThemeColorOverride("font_color", UIStyle.ColorGold);
		badgeLabel.AddThemeFontSizeOverride("font_size", 9);
		badgePanel.AddChild(badgeLabel);
		container.AddChild(badgePanel);

		return container;
	}

	private StyleBoxFlat CreateCardStyle(Color bgColor, Color borderColor)
	{
		var style = new StyleBoxFlat();
		style.BgColor = bgColor;
		style.BorderColor = borderColor;
		style.SetBorderWidthAll(1);
		style.CornerRadiusTopLeft = 8;
		style.CornerRadiusTopRight = 8;
		style.CornerRadiusBottomLeft = 8;
		style.CornerRadiusBottomRight = 8;
		style.ContentMarginLeft = 18;
		style.ContentMarginRight = 18;
		style.ContentMarginTop = 14;
		style.ContentMarginBottom = 14;
		return style;
	}

	private StyleBoxFlat CreateHeaderPanelStyle()
	{
		var style = new StyleBoxFlat();
		style.BgColor = new Color(0.08f, 0.09f, 0.11f, 0.95f);
		style.BorderColor = UIStyle.ColorBronze;
		style.BorderWidthBottom = 2;
		style.ContentMarginLeft = 20;
		style.ContentMarginRight = 20;
		style.ContentMarginTop = 12;
		style.ContentMarginBottom = 12;
		return style;
	}

	private StyleBox CreateBackdropContainerStyle()
	{
		return UIStyle.CreateCreatorDiscoveryPanelStyle();
	}

	private StyleBoxFlat CreateBadgeStyle()
	{
		var style = new StyleBoxFlat();
		style.BgColor = new Color(0.18f, 0.15f, 0.10f, 0.9f);
		style.BorderColor = UIStyle.ColorGoldDull;
		style.SetBorderWidthAll(1);
		style.CornerRadiusTopLeft = 4;
		style.CornerRadiusTopRight = 4;
		style.CornerRadiusBottomLeft = 4;
		style.CornerRadiusBottomRight = 4;
		style.ContentMarginLeft = 8;
		style.ContentMarginRight = 8;
		style.ContentMarginTop = 4;
		style.ContentMarginBottom = 4;
		return style;
	}

	private StyleBoxFlat CreateChipStyle()
	{
		var style = new StyleBoxFlat();
		style.BgColor = new Color(0.10f, 0.18f, 0.24f, 0.7f);
		style.BorderColor = UIStyle.ColorCyanGlowDim;
		style.SetBorderWidthAll(1);
		style.CornerRadiusTopLeft = 10;
		style.CornerRadiusTopRight = 10;
		style.CornerRadiusBottomLeft = 10;
		style.CornerRadiusBottomRight = 10;
		style.ContentMarginLeft = 10;
		style.ContentMarginRight = 10;
		style.ContentMarginTop = 4;
		style.ContentMarginBottom = 4;
		return style;
	}
}