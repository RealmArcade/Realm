using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using Godot;
using Realm.Client.Network;

using static Godot.Control;
using static Godot.CanvasItem;
using Realm.Client.Utils;
using Realm.Client.Services;
using Realm.Client.Services.Steam;

namespace Realm.Client.UI;

[GlobalClass]
public partial class MapDiscovery : Control
{
	public override void _EnterTree()
	{
		Realm.Client.Services.GameContext.EnsureServices();
	}

	private Panel _bgPanel = null!;
	private Panel _leftPillar = null!;
	private Panel _rightPillar = null!;
	private PanelContainer _filterPanel = null!;
	private PanelContainer _mapListPanel = null!;

	private Button _backButton = null!;
	private Label _discoveryTitle = null!;
	private LineEdit _searchBar = null!;
	private CheckBox _trustedOnlyCheck = null!;
	private GridContainer _mapGrid = null!;

	private Button _btnAll = null!;
	private Button _btnFeatured = null!;
	private Button _btnTD = null!;
	private Button _btnMelee = null!;
	private Button _btnCampaign = null!;

	private MapData[] _allMaps = null!;
	private string _selectedCategory = "All";
	private string _searchQuery = "";

	private static Font? _fontNorseBold;

	private static void EnsureFontsLoaded()
	{
		_fontNorseBold ??= LoadFont("res://Assets/UI/Norse-Bold.otf");
	}

	private static Font? LoadFont(string path)
	{
		try
		{
			if (ResourceLoader.Exists(path))
			{
				var font = GD.Load<Font>(path);
				if (font != null) return font;
			}
		}
		catch
		{
		}

		return null;
	}

	public override void _Ready()
	{
		EnsureFontsLoaded();

		(_bgPanel, _leftPillar, _rightPillar) = UIStyle.BindStandardChrome(this);
		_filterPanel = GetNode<PanelContainer>("FilterPanel");
		_mapListPanel = GetNode<PanelContainer>("MapListPanel");

		_backButton = GetNode<Button>("BackButton");
		_discoveryTitle = GetNode<Label>("DiscoveryTitle");

		_searchBar = GetNode<LineEdit>("FilterPanel/VBoxContainer/SearchBar");
		_trustedOnlyCheck = CreateTrustedOnlyToggle();
		_mapGrid = GetNode<GridContainer>("MapListPanel/VBoxContainer/ScrollContainer/MapGrid");

		_btnAll = GetNode<Button>("FilterPanel/VBoxContainer/CatAll");
		_btnFeatured = GetNode<Button>("FilterPanel/VBoxContainer/CatFeatured");
		_btnTD = GetNode<Button>("FilterPanel/VBoxContainer/CatTD");
		_btnMelee = GetNode<Button>("FilterPanel/VBoxContainer/CatMelee");
		_btnCampaign = GetNode<Button>("FilterPanel/VBoxContainer/CatCampaign");

		_allMaps = [];

		ApplyStyles();
		RegisterEvents();
		RenderMapGrid();

		LoadDiscoveryMapsAsync();
	}

	private async void LoadDiscoveryMapsAsync()
	{
		try
		{
			IReadOnlyList<WorkshopMapEntry> items = await SteamWorkshopService.BrowseAsync(null, SteamWorkshopService.BrowsePageCount, CancellationToken.None);
			if (items.Count > 0)
			{
				var liveMaps = items
					.Where(entry => !entry.IsBanned)
					.Select(MapData.FromWorkshop)
					.ToList();

				var groupedMaps = liveMaps
					.GroupBy(map => string.IsNullOrWhiteSpace(map.Title) ? map.MapId : map.Title.Trim(), StringComparer.OrdinalIgnoreCase)
					.Select(group =>
					{
						var versions = group.OrderByDescending(version => MapInfoHelper.ParseVersion(version.Version)).ToList();
						var primary = versions[0];
						primary.AvailableVersions = versions;
						foreach (var version in versions)
						{
							version.AvailableVersions = versions;
						}

						return primary;
					})
					.ToArray();

				_allMaps = groupedMaps;
				RenderMapGrid();
				return;
			}
		}
		catch (Exception exception)
		{
			GameLog.LogError($"[MapDiscovery] Reading the map Workshop failed: {exception.Message}");
		}

		RenderMapGrid();
	}

	private void ApplyStyles()
	{
		ApplyBackgroundStyle();
		ApplyPanelStyles();
		ApplyTitleStyle();
		ApplyBackButtonStyle();
		ApplySearchBarStyle();

		SetupCategoryButton(_btnAll, TranslationServer.Translate("ALL MAPS"), "All");
		SetupCategoryButton(_btnFeatured, TranslationServer.Translate("FEATURED"), "Featured");
		SetupCategoryButton(_btnTD, TranslationServer.Translate("TOWER DEFENSE"), "TD");
		SetupCategoryButton(_btnMelee, TranslationServer.Translate("MELEE"), "Melee");
		SetupCategoryButton(_btnCampaign, TranslationServer.Translate("CAMPAIGN"), "Campaign");

		UpdateButtonHighlight();
	}

	private void ApplyBackgroundStyle()
	{
		UIStyle.ApplyScreenBackground(_bgPanel,
			"res://Assets/UI/procedural_bg.webp",
			"res://Assets/UI/map_discovery_bg_rpg_grey2.webp",
			"res://Assets/UI/map_discovery_bg_fortress2.webp");

		_leftPillar.AddThemeStyleboxOverride("panel", UIStyle.CreatePillarPanel(true));
		_rightPillar.AddThemeStyleboxOverride("panel", UIStyle.CreatePillarPanel(false));
	}

	private void ApplyPanelStyles()
	{
		_filterPanel.AddThemeStyleboxOverride("panel",
			UIStyle.CreateArtworkPlate("res://Assets/UI/procedural_filter_panel.webp", 55, 55, 60, 24));
		_filterPanel.TextureFilter = TextureFilterEnum.LinearWithMipmaps;

		_mapListPanel.AddThemeStyleboxOverride("panel",
			UIStyle.CreateArtworkPlate("res://Assets/UI/procedural_main_panel.webp", 80, 80, 130, 65));
		_mapListPanel.TextureFilter = TextureFilterEnum.LinearWithMipmaps;
	}

	private void ApplyTitleStyle()
	{
		_discoveryTitle.Text = TranslationServer.Translate("MAP DISCOVERY");
		if (_fontNorseBold != null) _discoveryTitle.AddThemeFontOverride("font", _fontNorseBold);
		_discoveryTitle.AddThemeFontSizeOverride("font_size", 24);
		_discoveryTitle.AddThemeColorOverride("font_color", UIStyle.ColorGold);
		_discoveryTitle.AddThemeColorOverride("font_outline_color", new Color(0.06f, 0.06f, 0.08f));
		_discoveryTitle.AddThemeConstantOverride("outline_size", 8);
		_discoveryTitle.HorizontalAlignment = HorizontalAlignment.Center;
		_discoveryTitle.VerticalAlignment = VerticalAlignment.Center;
		_discoveryTitle.AddThemeStyleboxOverride("normal", new StyleBoxEmpty());

		_discoveryTitle.SetAnchorsPreset(LayoutPreset.TopLeft);
		_discoveryTitle.CustomMinimumSize = new Vector2(300, 40);
		_discoveryTitle.Size = new Vector2(300, 40);
		float titleX = _mapListPanel.Position.X + 200.0f;
		_discoveryTitle.Position = new Vector2(titleX, _mapListPanel.Position.Y + 30.0f);
		_discoveryTitle.ZIndex = 1;
		MoveChild(_discoveryTitle, -1);
	}

	private void ApplyBackButtonStyle()
	{
		UIStyle.ApplyBackButton(_backButton, () => GameContext.UserInterface?.TransitionTo(GameScreen.MainMenu));
		_backButton.SetAnchorsPreset(LayoutPreset.TopLeft);
		_backButton.Position = new Vector2(130, 40);
		_backButton.Size = new Vector2(130, 42);
		_backButton.ZIndex = 1;
	}

	private void ApplySearchBarStyle()
	{
		var searchStyle = UIStyle.CreateArtworkPlate("res://Assets/UI/procedural_search_bar.webp", 24, 24, 0, 0);
		_searchBar.AddThemeStyleboxOverride("normal", searchStyle);
		_searchBar.AddThemeStyleboxOverride("focus", searchStyle);
		if (_fontNorseBold != null) _searchBar.AddThemeFontOverride("font", _fontNorseBold);
		_searchBar.AddThemeFontSizeOverride("font_size", 17);
		_searchBar.AddThemeColorOverride("font_color", new Color(0.95f, 0.9f, 0.8f));
		_searchBar.AddThemeColorOverride("font_placeholder_color", new Color(0.6f, 0.55f, 0.45f));
		_searchBar.AddThemeColorOverride("caret_color", new Color(0.95f, 0.9f, 0.8f));
		_searchBar.PlaceholderText = TranslationServer.Translate("Search maps... ");
		_searchBar.Alignment = HorizontalAlignment.Left;
		_searchBar.RightIcon = null;
		_searchBar.CustomMinimumSize = new Vector2(0, 56);

		var container = GetNode<VBoxContainer>("FilterPanel/VBoxContainer");
		if (container != null && !container.HasNode("SearchLabel"))
		{
			var searchLabel = new Label();
			searchLabel.Name = "SearchLabel";
			searchLabel.Text = TranslationServer.Translate("Search");
			if (_fontNorseBold != null) searchLabel.AddThemeFontOverride("font", _fontNorseBold);
			searchLabel.AddThemeFontSizeOverride("font_size", 20);
			searchLabel.AddThemeColorOverride("font_color", UIStyle.ColorGold);
			searchLabel.HorizontalAlignment = HorizontalAlignment.Center;
			container.AddChild(searchLabel);
			container.MoveChild(searchLabel, 0);
		}
	}

	private void SetupCategoryButton(Button button, string text, string categoryValue)
	{
		button.Text = TranslationServer.Translate(text);
		if (_fontNorseBold != null) button.AddThemeFontOverride("font", _fontNorseBold);
		button.AddThemeFontSizeOverride("font_size", 19);
		button.AddThemeConstantOverride("outline_size", 4);
		button.AddThemeColorOverride("font_outline_color", new Color(0.06f, 0.06f, 0.08f));
		button.CustomMinimumSize = new Vector2(0, 76);
		button.TextureFilter = TextureFilterEnum.LinearWithMipmaps;

		button.Pressed += () =>
		{
			GameContext.UserInterface?.PlayClickSound();
			_selectedCategory = categoryValue;
			UpdateButtonHighlight();
			RenderMapGrid();
		};
		button.MouseEntered += () => GameContext.UserInterface?.PlayHoverSound();
	}

	private void UpdateButtonHighlight()
	{
		HighlightButton(_btnAll, _selectedCategory == "All");
		HighlightButton(_btnFeatured, _selectedCategory == "Featured");
		HighlightButton(_btnTD, _selectedCategory == "TD");
		HighlightButton(_btnMelee, _selectedCategory == "Melee");
		HighlightButton(_btnCampaign, _selectedCategory == "Campaign");
	}

	private void HighlightButton(Button button, bool highlight)
	{
		button.AddThemeStyleboxOverride("focus", new StyleBoxEmpty());
		button.TextureFilter = TextureFilterEnum.LinearWithMipmaps;

		if (highlight)
		{
			button.AddThemeColorOverride("font_color", UIStyle.ColorGold);
			button.AddThemeColorOverride("font_hover_color", new Color(1.0f, 0.94f, 0.75f));
			button.AddThemeColorOverride("font_pressed_color", UIStyle.ColorGold);
			button.AddThemeColorOverride("font_focus_color", UIStyle.ColorGold);
			button.AddThemeConstantOverride("outline_size", 5);
			button.AddThemeColorOverride("font_outline_color", new Color(0.22f, 0.16f, 0.04f));

			UIStyle.ApplyTintedArtworkButton(button, "res://Assets/UI/procedural_btn_normal.webp",
				new Color(1.30f, 1.20f, 0.88f), new Color(1.42f, 1.30f, 0.95f), new Color(1.08f, 1.00f, 0.75f));
		}
		else
		{
			button.AddThemeColorOverride("font_color", UIStyle.ColorGoldDull);
			UIStyle.ApplyGoldHoverAndFocusColors(button);
			button.AddThemeConstantOverride("outline_size", 4);
			button.AddThemeColorOverride("font_outline_color", new Color(0.06f, 0.06f, 0.08f));

			UIStyle.ApplyTintedArtworkButton(button, "res://Assets/UI/procedural_btn_normal.webp",
				new Color(0.85f, 0.85f, 0.88f), new Color(1.12f, 1.08f, 0.92f), new Color(0.75f, 0.75f, 0.78f));
		}
	}

	private void RegisterEvents()
	{
		_searchBar.TextChanged += (text) =>
		{
			_searchQuery = text.Trim();
			RenderMapGrid();
		};

		_trustedOnlyCheck.Toggled += _ => RenderMapGrid();
	}

	private void RenderMapGrid()
	{
		foreach (Node child in _mapGrid.GetChildren())
		{
			child.QueueFree();
		}

		var filtered = _allMaps.AsEnumerable();
		filtered = MapCatalogFilter.ByCategory(filtered, _selectedCategory);
		filtered = MapCatalogFilter.BySearchQuery(filtered, _searchQuery);

		// Off unless the player asks for it. A new map has no votes yet, and hiding it by default would keep it from
		// ever collecting any.
		if (_trustedOnlyCheck.ButtonPressed)
		{
			filtered = filtered.Where(map => map.Trust.IsTrusted);
		}

		var list = filtered.ToList();

		if (list.Count == 0)
		{
			var noMapsLabel = new Label();
			noMapsLabel.Name = "NoMapsLabel_Label";
			noMapsLabel.Text = TranslationServer.Translate("No maps found matching criteria.");
			noMapsLabel.AddThemeColorOverride("font_color", new Color(0.7f, 0.7f, 0.7f));
			noMapsLabel.AddThemeFontSizeOverride("font_size", 16);
			_mapGrid.AddChild(noMapsLabel);
			return;
		}

		foreach (var map in list)
		{
			var card = CreateMapCard(map);
			_mapGrid.AddChild(card);
		}
	}

	/// <summary>
	///     Adds the trusted-only toggle under the search box. It is off on first run, because every map is visible by
	///     default and a creator's first upload has not collected the votes a mark asks for.
	/// </summary>
	/// <returns>The toggle the grid reads.</returns>
	private CheckBox CreateTrustedOnlyToggle()
	{
		var toggle = new CheckBox { Name = "TrustedOnlyCheck" };
		toggle.Text = TranslationServer.Translate("Trusted maps only");
		toggle.TooltipText = TranslationServer.Translate("Hide maps Steam does not vouch for yet. Off by default: every map is listed with its trust mark.");
		toggle.ButtonPressed = false;
		toggle.FocusMode = FocusModeEnum.None;
		toggle.AddThemeColorOverride("font_color", UIStyle.ColorGoldDull);
		toggle.AddThemeFontSizeOverride("font_size", 12);

		_filterPanel.GetNode<VBoxContainer>("VBoxContainer").AddChild(toggle);
		return toggle;
	}

	private Control CreateMapCard(MapData map)
	{
		EnsureFontsLoaded();

		var card = new PanelContainer();
		card.Name = "Card_PanelContainer";
		card.CustomMinimumSize = new Vector2(560, 220);
		card.TextureFilter = TextureFilterEnum.LinearWithMipmaps;

		card.AddThemeStyleboxOverride("panel",
			UIStyle.CreateArtworkPlate("res://Assets/UI/procedural_card_bg.webp", 32, 32, 38, 26));

		var hBox = new HBoxContainer();
		hBox.Name = "HBox_HBoxContainer";
		hBox.AddThemeConstantOverride("separation", 16);
		card.AddChild(hBox);

		var imgContainer = new Control();
		imgContainer.Name = "ImgContainer_Control";
		imgContainer.CustomMinimumSize = new Vector2(180, 144);
		imgContainer.SizeFlagsVertical = SizeFlags.ShrinkCenter;
		imgContainer.TextureFilter = TextureFilterEnum.LinearWithMipmaps;

		var thumbnail = new TextureRect();
		thumbnail.Name = "Thumbnail_TextureRect";
		thumbnail.SetAnchorsPreset(LayoutPreset.FullRect);
		thumbnail.OffsetLeft = 12;
		thumbnail.OffsetTop = 12;
		thumbnail.OffsetRight = -12;
		thumbnail.OffsetBottom = -12;
		thumbnail.ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize;
		thumbnail.StretchMode = TextureRect.StretchModeEnum.KeepAspectCovered;
		thumbnail.TextureFilter = TextureFilterEnum.LinearWithMipmaps;
		var loadedTex = LoadTextureSafe(map.ThumbnailPath);
		thumbnail.Texture = loadedTex ?? UIStyle.EmptyBlackTexture;
		imgContainer.AddChild(thumbnail);

		var frameOverlay = new TextureRect();
		frameOverlay.Name = "FrameOverlay_TextureRect";
		frameOverlay.SetAnchorsPreset(LayoutPreset.FullRect);
		frameOverlay.ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize;
		frameOverlay.StretchMode = TextureRect.StretchModeEnum.Scale;
		frameOverlay.TextureFilter = TextureFilterEnum.LinearWithMipmaps;
		frameOverlay.Texture = TrimmedTextureLoader.LoadTrimmedTexture("res://Assets/UI/procedural_card_img.webp");
		imgContainer.AddChild(frameOverlay);

		hBox.AddChild(imgContainer);

		var vBox = new VBoxContainer();
		vBox.Name = "VBox_VBoxContainer";
		vBox.SizeFlagsHorizontal = SizeFlags.ExpandFill;
		vBox.SizeFlagsVertical = SizeFlags.ExpandFill;
		vBox.AddThemeConstantOverride("separation", 2);
		hBox.AddChild(vBox);

		var headerBox = new HBoxContainer();
		headerBox.Name = "HeaderBox_HBoxContainer";
		headerBox.SizeFlagsHorizontal = SizeFlags.ExpandFill;
		vBox.AddChild(headerBox);

		var title = new Label();
		title.Name = "Title_Label";
		title.Text = map.Title;
		if (_fontNorseBold != null) title.AddThemeFontOverride("font", _fontNorseBold);
		title.AddThemeColorOverride("font_color", UIStyle.ColorGold);
		title.AddThemeFontSizeOverride("font_size", 22);
		title.AddThemeConstantOverride("outline_size", 4);
		title.AddThemeColorOverride("font_outline_color", new Color(0.06f, 0.06f, 0.08f));
		title.SizeFlagsHorizontal = SizeFlags.ExpandFill;
		headerBox.AddChild(title);

		string trustBadge = MapTrustPolicy.DescribeBadge(map.Trust);
		if (trustBadge.Length > 0)
		{
			var trustLabel = new Label { Name = "TrustLabel_Label" };
			trustLabel.Text = trustBadge;
			trustLabel.TooltipText = MapTrustPolicy.DescribeReason(map.Trust.Reason);
			trustLabel.AddThemeColorOverride("font_color", UIStyle.ColorGoldDull);
			trustLabel.AddThemeFontSizeOverride("font_size", 12);
			headerBox.AddChild(trustLabel);
		}

		var ratingLabel = new Label();
		ratingLabel.Name = "RatingLabel_Label";
		ratingLabel.Text = map.VotesUp + map.VotesDown > 0
			? string.Format(TranslationServer.Translate("{0}% positive"), map.PositivePercent)
			: TranslationServer.Translate("No votes yet");
		if (_fontNorseBold != null) ratingLabel.AddThemeFontOverride("font", _fontNorseBold);
		ratingLabel.AddThemeColorOverride("font_color", UIStyle.ColorGold);
		ratingLabel.AddThemeFontSizeOverride("font_size", 17);
		ratingLabel.AddThemeConstantOverride("outline_size", 4);
		ratingLabel.AddThemeColorOverride("font_outline_color", new Color(0.06f, 0.06f, 0.08f));
		headerBox.AddChild(ratingLabel);

		var creator = new Label();
		creator.Name = "Creator_Label";
		creator.Text = $"By: {map.Creator}  •  {map.Genre}";
		if (_fontNorseBold != null) creator.AddThemeFontOverride("font", _fontNorseBold);
		creator.AddThemeColorOverride("font_color", UIStyle.ColorGoldDull);
		creator.AddThemeFontSizeOverride("font_size", 14);
		vBox.AddChild(creator);

		var desc = new Label();
		desc.Name = "Desc_Label";
		desc.Text = map.Description;
		desc.AutowrapMode = TextServer.AutowrapMode.WordSmart;
		desc.CustomMinimumSize = new Vector2(300, 0);
		desc.SizeFlagsHorizontal = SizeFlags.ExpandFill;
		desc.SizeFlagsVertical = SizeFlags.ExpandFill;
		if (_fontNorseBold != null) desc.AddThemeFontOverride("font", _fontNorseBold);
		desc.AddThemeColorOverride("font_color", new Color(0.94f, 0.94f, 0.96f));
		desc.AddThemeFontSizeOverride("font_size", 14);
		vBox.AddChild(desc);

		var footer = new HBoxContainer();
		footer.Name = "Footer_HBoxContainer";
		footer.Alignment = BoxContainer.AlignmentMode.End;
		vBox.AddChild(footer);

		var btnDetails = new Button();
		btnDetails.Name = "BtnDetails_Button";
		btnDetails.CustomMinimumSize = new Vector2(110, 32);
		btnDetails.Text = TranslationServer.Translate("DETAILS");
		if (_fontNorseBold != null) btnDetails.AddThemeFontOverride("font", _fontNorseBold);
		btnDetails.AddThemeFontSizeOverride("font_size", 16);
		btnDetails.AddThemeConstantOverride("outline_size", 4);
		btnDetails.AddThemeColorOverride("font_outline_color", new Color(0.06f, 0.06f, 0.08f));
		btnDetails.AddThemeColorOverride("font_color", UIStyle.ColorGoldDull);
		UIStyle.ApplyGoldHoverAndFocusColors(btnDetails);
		UIStyle.ApplyButtonStyleBoxesClearingFocus(btnDetails);
		btnDetails.TextureFilter = TextureFilterEnum.LinearWithMipmaps;

		btnDetails.Pressed += () =>
		{
			GameContext.UserInterface?.PlayClickSound();
			GameContext.UserInterface?.TransitionToMapDetails(map);
		};
		btnDetails.MouseEntered += () => GameContext.UserInterface?.PlayHoverSound();
		footer.AddChild(btnDetails);

		return card;
	}

	private static Texture2D? LoadTextureSafe(string resPath)
	{
		return TrimmedTextureLoader.LoadTrimmedTexture(resPath);
	}
}
