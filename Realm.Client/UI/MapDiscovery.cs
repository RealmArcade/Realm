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
	private Button _btnCreators = null!;
	private CheckBox _followedOnlyCheck = null!;
	private Label _creatorStatusLabel = null!;

	/// <summary>The category value that switches the grid from maps to the creators who published them.</summary>
	private const string CreatorsCategory = "Creators";

	/// <summary>The maps the creators this player follows published, loaded once when the followed filter is first used.</summary>
	private MapData[] _followedMaps = [];

	private bool _followedMapsLoaded;
	private bool _followedMapsLoading;

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

		CreateCreatorControls();
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
				_allMaps = GroupByMap(items.Where(entry => !entry.IsBanned).Select(MapData.FromWorkshop));
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

	/// <summary>
	///     Folds every version of one map into its newest entry, so a grid shows a map once and the details screen offers its
	///     versions. The catalogue list and a creator's list both group this way.
	/// </summary>
	/// <param name="maps">The maps to fold.</param>
	/// <returns>One entry per map, its newest version first.</returns>
	private static MapData[] GroupByMap(IEnumerable<MapData> maps)
	{
		return maps
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
	}

	/// <summary>
	///     Reads the maps published by the creators this player follows, which Steam answers from the player's own follow
	///     list. They are kept beside the browse list rather than merged into it: the catalogue is read page by page and a
	///     creator the player follows may have published nothing inside those pages.
	/// </summary>
	private async void LoadFollowedCreatorMapsAsync()
	{
		if (_followedMapsLoading) return;
		_followedMapsLoading = true;

		LocalizedText.Set(_creatorStatusLabel, "Reading the creators you follow...");
		_creatorStatusLabel.Visible = true;

		try
		{
			if (!SteamWorkshopService.IsAvailable)
			{
				LocalizedText.Set(_creatorStatusLabel, "Following creators needs Steam.");
				_followedOnlyCheck.SetPressedNoSignal(false);
				return;
			}

			IReadOnlyList<WorkshopMapEntry> items = await SteamWorkshopService.BrowseFollowedCreatorsAsync(SteamWorkshopService.BrowsePageCount, CancellationToken.None);
			if (!GodotObject.IsInstanceValid(this)) return;

			_followedMaps = GroupByMap(items.Where(entry => !entry.IsBanned).Select(MapData.FromWorkshop));
			_followedMapsLoaded = true;

			if (_followedMaps.Length == 0) LocalizedText.Set(_creatorStatusLabel, "None of the creators you follow have a map here yet.");
			else _creatorStatusLabel.Visible = false;
		}
		catch (Exception exception)
		{
			GameLog.LogError($"[MapDiscovery] Reading the creators this player follows failed: {exception.Message}");
			_followedOnlyCheck.SetPressedNoSignal(false);
		}
		finally
		{
			_followedMapsLoading = false;
			if (GodotObject.IsInstanceValid(this)) RenderMapGrid();
		}
	}

	/// <summary>
	///     Adds the creators view beside the category filters: the view itself, the followed-creators filter that belongs to
	///     it, and the line that states what that filter is doing.
	/// </summary>
	private void CreateCreatorControls()
	{
		var container = _filterPanel.GetNode<VBoxContainer>("VBoxContainer");

		_btnCreators = new Button
		{
			Name = "CatCreators",
			CustomMinimumSize = new Vector2(0, 45),
			FocusMode = FocusModeEnum.None
		};
		container.AddChild(_btnCreators);
		container.MoveChild(_btnCreators, _btnCampaign.GetIndex() + 1);

		_followedOnlyCheck = new CheckBox
		{
			Name = "FollowedCreatorsCheck",
			FocusMode = FocusModeEnum.None,
			Visible = false
		};
		LocalizedText.Set(_followedOnlyCheck, "Creators I follow only");
		LocalizedText.SetTooltip(_followedOnlyCheck, "Show only maps from creators you follow on Steam. Following a creator is done on their Steam profile page.");
		_followedOnlyCheck.AddThemeColorOverride("font_color", UIStyle.ColorGoldDull);
		_followedOnlyCheck.AddThemeFontSizeOverride("font_size", 12);
		container.AddChild(_followedOnlyCheck);
		container.MoveChild(_followedOnlyCheck, _btnCreators.GetIndex() + 1);

		_creatorStatusLabel = new Label
		{
			Name = "CreatorStatus_Label",
			Visible = false,
			AutowrapMode = TextServer.AutowrapMode.WordSmart,
			CustomMinimumSize = new Vector2(340, 0),
			SizeFlagsHorizontal = SizeFlags.ExpandFill
		};
		_creatorStatusLabel.AddThemeColorOverride("font_color", UIStyle.ColorGoldDull);
		_creatorStatusLabel.AddThemeFontSizeOverride("font_size", 12);
		container.AddChild(_creatorStatusLabel);
		container.MoveChild(_creatorStatusLabel, _followedOnlyCheck.GetIndex() + 1);
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
		SetupCategoryButton(_btnCreators, TranslationServer.Translate("CREATORS"), CreatorsCategory);

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
		HighlightButton(_btnCreators, _selectedCategory == CreatorsCategory);
		_followedOnlyCheck.Visible = _selectedCategory == CreatorsCategory;
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

		_followedOnlyCheck.Toggled += pressed =>
		{
			GameContext.UserInterface?.PlayClickSound();
			if (pressed && !_followedMapsLoaded)
			{
				LoadFollowedCreatorMapsAsync();
				return;
			}

			RenderMapGrid();
		};
	}

	private void RenderMapGrid()
	{
		foreach (Node child in _mapGrid.GetChildren())
		{
			child.QueueFree();
		}

		if (_selectedCategory == CreatorsCategory)
		{
			RenderCreatorCards();
			return;
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
			trustLabel.TooltipText = MapTrustPolicy.DescribeTooltip(map.Trust);

			// The developer's mark is the one thing on a new map that is neither its votes nor its age, so it is drawn in
			// the colour reserved for what the game itself vouches for rather than the muted gold of a missing vote.
			trustLabel.AddThemeColorOverride("font_color", map.Trust.Level == MapTrustLevel.Verified ? UIStyle.ColorCyanGlow : UIStyle.ColorGoldDull);
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
		LocalizedText.Bind(creator, () => $"{string.Format(TranslationServer.Translate("By: {0}"), map.Creator)}  •  {map.Genre}");
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

	/// <summary>
	///     Draws one card per creator, grouping the maps this grid already holds by the Steam account that owns them. The maps
	///     inside a card are sorted by their votes and the cards follow that same order, so the creators whose maps the
	///     catalogue actually plays come first. Nothing here is stored: a persona and an avatar are read from Steam each
	///     session, and a private profile simply draws no picture.
	/// </summary>
	private void RenderCreatorCards()
	{
		IEnumerable<MapData> source = _followedOnlyCheck.ButtonPressed ? _followedMaps : _allMaps;
		IEnumerable<MapData> filtered = MapCatalogFilter.BySearchQuery(source, _searchQuery);

		// The trusted-only toggle means the same thing in this view: it hides a map whose creator Steam does not vouch for
		// yet, and a creator left with no maps is not drawn.
		if (_trustedOnlyCheck.ButtonPressed)
		{
			filtered = filtered.Where(map => map.Trust.IsTrusted);
		}

		List<CreatorGroup> creators = filtered
			.GroupBy(map => map.CreatorSteamId)
			.Select(group => new CreatorGroup(
				group.Key,
				DescribeCreator(group.Key, group),
				group.OrderByDescending(map => map.VotesUp + map.VotesDown)
					.ThenByDescending(map => map.VotesUp)
					.ThenBy(map => map.Title, StringComparer.OrdinalIgnoreCase)
					.ToList()))
			.OrderByDescending(creator => creator.Maps[0].VotesUp + creator.Maps[0].VotesDown)
			.ThenByDescending(creator => creator.Maps.Count)
			.ThenBy(creator => creator.Name, StringComparer.OrdinalIgnoreCase)
			.ToList();

		if (creators.Count == 0)
		{
			var noCreatorsLabel = new Label { Name = "NoCreatorsLabel_Label" };
			if (_followedOnlyCheck.ButtonPressed) LocalizedText.Set(noCreatorsLabel, "None of the creators you follow have a map here yet.");
			else LocalizedText.Set(noCreatorsLabel, "No maps found matching criteria.");
			noCreatorsLabel.AddThemeColorOverride("font_color", new Color(0.7f, 0.7f, 0.7f));
			noCreatorsLabel.AddThemeFontSizeOverride("font_size", 16);
			_mapGrid.AddChild(noCreatorsLabel);
			return;
		}

		foreach (CreatorGroup creator in creators)
		{
			_mapGrid.AddChild(CreateCreatorCard(creator));
		}
	}

	/// <summary>One creator as the creators view draws them: the account, the name to show and their maps, most voted first.</summary>
	/// <param name="SteamId">The creator's SteamID64, or zero for a map that credits nobody.</param>
	/// <param name="Name">The persona Steam states, or the name the map itself credits.</param>
	/// <param name="Maps">Their maps for this game, most voted first.</param>
	private record CreatorGroup(ulong SteamId, string Name, List<MapData> Maps);

	/// <summary>
	///     The name to put over a creator's maps: Steam's persona for the account, which is what the Workshop page shows,
	///     falling back to the name the map's own metadata credits when Steam has nothing to say.
	/// </summary>
	/// <param name="creatorSteamId">The owner Steam states for the group.</param>
	/// <param name="maps">The maps in the group, used only for the credited name.</param>
	/// <returns>The creator's name, never empty.</returns>
	private static string DescribeCreator(ulong creatorSteamId, IEnumerable<MapData> maps)
	{
		if (creatorSteamId != 0)
		{
			string personaName = GameContext.Steam.GetPersonaName(creatorSteamId);
			if (personaName.Length > 0) return personaName;
		}

		foreach (MapData map in maps)
		{
			if (!string.IsNullOrWhiteSpace(map.Creator)) return map.Creator;
		}

		return TranslationServer.Translate("Unknown creator");
	}

	private Control CreateCreatorCard(CreatorGroup creator)
	{
		EnsureFontsLoaded();

		var card = new PanelContainer();
		card.Name = $"CreatorCard_{creator.SteamId}_PanelContainer";
		card.CustomMinimumSize = new Vector2(560, 0);
		card.TextureFilter = TextureFilterEnum.LinearWithMipmaps;
		card.AddThemeStyleboxOverride("panel",
			UIStyle.CreateArtworkPlate("res://Assets/UI/procedural_card_bg.webp", 32, 32, 38, 26));

		var vBox = new VBoxContainer { Name = "CardVBox_VBoxContainer" };
		vBox.AddThemeConstantOverride("separation", 8);
		card.AddChild(vBox);

		var header = new HBoxContainer { Name = "CreatorHeader_HBoxContainer" };
		header.AddThemeConstantOverride("separation", 12);
		vBox.AddChild(header);

		var avatar = new TextureRect
		{
			Name = "CreatorAvatar_TextureRect",
			CustomMinimumSize = new Vector2(64, 64),
			ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
			StretchMode = TextureRect.StretchModeEnum.KeepAspectCovered
		};
		avatar.Texture = creator.SteamId != 0 ? GameContext.Steam.GetAvatar(creator.SteamId) : null;
		avatar.Visible = avatar.Texture != null;
		header.AddChild(avatar);

		var nameBox = new VBoxContainer { Name = "CreatorName_VBoxContainer", SizeFlagsHorizontal = SizeFlags.ExpandFill };
		nameBox.AddThemeConstantOverride("separation", 2);
		header.AddChild(nameBox);

		var nameLabel = new Label { Name = "CreatorName_Label" };
		if (_fontNorseBold != null) nameLabel.AddThemeFontOverride("font", _fontNorseBold);
		nameLabel.AddThemeColorOverride("font_color", UIStyle.ColorGold);
		nameLabel.AddThemeFontSizeOverride("font_size", 20);
		nameLabel.AddThemeConstantOverride("outline_size", 4);
		nameLabel.AddThemeColorOverride("font_outline_color", new Color(0.06f, 0.06f, 0.08f));
		LocalizedText.Bind(nameLabel, () => creator.Name);
		nameBox.AddChild(nameLabel);

		var countLabel = new Label { Name = "CreatorMapCount_Label" };
		countLabel.AddThemeColorOverride("font_color", UIStyle.ColorGoldDull);
		countLabel.AddThemeFontSizeOverride("font_size", 12);
		LocalizedText.Bind(countLabel, () => creator.Maps.Count == 1
			? TranslationServer.Translate("1 map")
			: string.Format(TranslationServer.Translate("{0} maps"), creator.Maps.Count));
		nameBox.AddChild(countLabel);

		if (creator.SteamId != 0)
		{
			var links = new HBoxContainer { Name = "CreatorLinks_HBoxContainer" };
			links.Alignment = BoxContainer.AlignmentMode.End;
			links.AddThemeConstantOverride("separation", 8);
			vBox.AddChild(links);

			links.AddChild(CreateCreatorLink(creator.SteamId, "Profile", "STEAM PROFILE",
				"Open this creator's Steam profile", SteamCommunityLinks.Profile(creator.SteamId)));
			links.AddChild(CreateCreatorLink(creator.SteamId, "Workshop", "WORKSHOP",
				"Open everything this creator published for this game", SteamCommunityLinks.CreatorWorkshop(creator.SteamId, GameContext.Steam.AppId)));
		}

		foreach (MapData map in creator.Maps)
		{
			vBox.AddChild(CreateCreatorMapRow(map));
		}

		return card;
	}

	/// <summary>
	///     One link on a creator card, opened in the Steam overlay when the player has it on and in the system browser
	///     otherwise. The page is Steam's either way: the game keeps no copy of a profile or a Workshop listing.
	/// </summary>
	/// <param name="steamId">The creator the link belongs to, which keeps the node name unique on a busy grid.</param>
	/// <param name="role">Which page of the creator this is, used only to name the button.</param>
	/// <param name="textKey">The translation key the button shows.</param>
	/// <param name="tooltipKey">The translation key the tooltip shows.</param>
	/// <param name="url">The page to open.</param>
	/// <returns>The button, ready to add to a card.</returns>
	private static Button CreateCreatorLink(ulong steamId, string role, string textKey, string tooltipKey, string url)
	{
		var button = new Button
		{
			Name = $"Creator{role}_{steamId}_Button",
			CustomMinimumSize = new Vector2(0, 28),
			FocusMode = FocusModeEnum.None
		};
		button.Set("icon_max_width", 0);
		LocalizedText.Set(button, textKey);
		LocalizedText.SetTooltip(button, tooltipKey);
		button.AddThemeFontSizeOverride("font_size", 12);
		button.AddThemeColorOverride("font_color", UIStyle.ColorGold);
		UIStyle.ApplyGoldHoverAndFocusColors(button);
		UIStyle.ApplyButtonStyleBoxesClearingFocus(button);

		button.Pressed += () =>
		{
			GameContext.UserInterface?.PlayClickSound();
			GameContext.Steam.OpenSteamPage(url);
		};
		button.MouseEntered += () => GameContext.UserInterface?.PlayHoverSound();
		return button;
	}

	/// <summary>
	///     One of a creator's maps, drawn as a row: the title opens the map's own details screen and the Steam button opens
	///     its Workshop page, which is where the item's description, screenshots, comments and report button live.
	/// </summary>
	/// <param name="map">The map to draw.</param>
	/// <returns>The row, ready to add to a creator card.</returns>
	private static Control CreateCreatorMapRow(MapData map)
	{
		var row = new PanelContainer { Name = $"CreatorMap_{map.WorkshopId}_PanelContainer" };
		row.AddThemeStyleboxOverride("panel", UIStyle.CreateCardPlate(
			new Color(0.02f, 0.03f, 0.04f, 0.5f), new Color(0.45f, 0.38f, 0.25f, 0.35f), 1, 4, 10, 10, 6, 6));

		var hBox = new HBoxContainer { Name = "MapRow_HBoxContainer" };
		hBox.AddThemeConstantOverride("separation", 8);
		row.AddChild(hBox);

		var titleButton = new Button
		{
			Name = $"MapTitle_{map.WorkshopId}_Button",
			SizeFlagsHorizontal = SizeFlags.ExpandFill,
			Alignment = HorizontalAlignment.Left,
			FocusMode = FocusModeEnum.None
		};
		titleButton.Set("icon_max_width", 0);
		titleButton.AddThemeFontSizeOverride("font_size", 13);
		titleButton.AddThemeColorOverride("font_color", new Color(0.92f, 0.94f, 0.98f));
		UIStyle.ApplyGoldHoverAndFocusColors(titleButton);
		UIStyle.ApplyButtonStyleBoxesClearingFocus(titleButton);
		LocalizedText.Bind(titleButton, () => map.Title);
		titleButton.Pressed += () =>
		{
			GameContext.UserInterface?.PlayClickSound();
			GameContext.UserInterface?.TransitionToMapDetails(map);
		};
		titleButton.MouseEntered += () => GameContext.UserInterface?.PlayHoverSound();
		hBox.AddChild(titleButton);

		var votesLabel = new Label { Name = "MapVotes_Label", VerticalAlignment = VerticalAlignment.Center };
		votesLabel.AddThemeColorOverride("font_color", UIStyle.ColorGoldDull);
		votesLabel.AddThemeFontSizeOverride("font_size", 12);
		uint totalVotes = map.VotesUp + map.VotesDown;
		if (totalVotes > 0) LocalizedText.Set(votesLabel, "{0} votes", totalVotes);
		else LocalizedText.Set(votesLabel, "No votes yet");
		hBox.AddChild(votesLabel);

		if (map.WorkshopId != 0)
		{
			var steamButton = new Button
			{
				Name = $"MapSteam_{map.WorkshopId}_Button",
				CustomMinimumSize = new Vector2(80, 26),
				FocusMode = FocusModeEnum.None
			};
			steamButton.Set("icon_max_width", 0);
			LocalizedText.Set(steamButton, "STEAM");
			LocalizedText.SetTooltip(steamButton, "Open this map's Steam Workshop page");
			steamButton.AddThemeFontSizeOverride("font_size", 12);
			steamButton.AddThemeColorOverride("font_color", UIStyle.ColorGold);
			UIStyle.ApplyGoldHoverAndFocusColors(steamButton);
			UIStyle.ApplyButtonStyleBoxesClearingFocus(steamButton);

			ulong publishedFileId = map.WorkshopId;
			steamButton.Pressed += () =>
			{
				GameContext.UserInterface?.PlayClickSound();
				GameContext.Steam.OpenSteamPage(SteamCommunityLinks.MapPage(publishedFileId));
			};
			steamButton.MouseEntered += () => GameContext.UserInterface?.PlayHoverSound();
			hBox.AddChild(steamButton);
		}

		return row;
	}

	private static Texture2D? LoadTextureSafe(string resPath)
	{
		return TrimmedTextureLoader.LoadTrimmedTexture(resPath);
	}
}
