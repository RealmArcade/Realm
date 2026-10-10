using Godot;
using Realm.Shared.Distribution;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Realm.Client.UI;

public partial class MapDiscovery : Control
{
	private Panel _bgPanel;
	private Panel _leftPillar;
	private Panel _rightPillar;
	private PanelContainer _filterPanel;
	private PanelContainer _mapListPanel;

	private Button _backButton;
	private Label _discoveryTitle;
	private LineEdit _searchBar;
	private GridContainer _mapGrid;

	// Category buttons
	private Button _btnAll;
	private Button _btnFeatured;
	private Button _btnTD;
	private Button _btnMelee;
	private Button _btnCampaign;

	private MapData[] _allMaps;
	private string _selectedCategory = "All";
	private string _searchQuery = "";

	private static Font _fontNorseBold;
	private static Font _fontCinzel;
	private static Font _fontOutfit;
	private static Font _fontOutfitBold;

	private static void EnsureFontsLoaded()
	{
		_fontNorseBold ??= LoadFont("res://Assets/UI/Norse-Bold.otf");
		_fontCinzel ??= LoadFont("res://Assets/UI/Cinzel-Bold.ttf") ?? _fontNorseBold;
		_fontOutfit ??= LoadFont("res://Assets/UI/Outfit-Medium.ttf") ?? _fontNorseBold;
		_fontOutfitBold ??= LoadFont("res://Assets/UI/Outfit-Bold.ttf") ?? _fontNorseBold;
	}

	private static Font LoadFont(string path)
	{
		try
		{
			if (ResourceLoader.Exists(path))
			{
				var font = GD.Load<Font>(path);
				if (font != null) return font;
			}
		}
		catch { }
		return null;
	}

	public override void _Ready()
	{
		EnsureFontsLoaded();

		_bgPanel = GetNode<Panel>("Background");
		_leftPillar = GetNode<Panel>("LeftPillar");
		_rightPillar = GetNode<Panel>("RightPillar");
		_filterPanel = GetNode<PanelContainer>("FilterPanel");
		_mapListPanel = GetNode<PanelContainer>("MapListPanel");
		
		_backButton = GetNode<Button>("BackButton");
		_discoveryTitle = GetNode<Label>("DiscoveryTitle");
		
		_searchBar = GetNode<LineEdit>("FilterPanel/VBoxContainer/SearchBar");
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
		string seedServerUrl = GodotObject.IsInstanceValid(Network.LobbyManager.Instance)
			? Network.LobbyManager.Instance.RegistryServerUrl
			: ServersConfigHelper.GetDefaultServerUrl();

		try
		{
			var distClient = new Realm.Shared.Distribution.DistributionClient(seedServerUrl);
			var discoveryDtos = await distClient.GetDiscoveryMapsAsync();
			if (discoveryDtos != null && discoveryDtos.Count > 0)
			{
				var liveMaps = discoveryDtos
					.Select(dto => MapData.FromDto(dto, seedServerUrl))
					.ToList();

				var groupedMaps = liveMaps
					.GroupBy(m => string.IsNullOrWhiteSpace(m.Title) ? m.MapId : m.Title.Trim(), StringComparer.OrdinalIgnoreCase)
					.Select(group =>
					{
						var versions = group.OrderByDescending(v => ParseVersion(v.Version)).ToList();
						var primary = versions[0];
						primary.AvailableVersions = versions;
						foreach (var v in versions)
						{
							v.AvailableVersions = versions;
						}
						return primary;
					})
					.ToArray();

				_allMaps = groupedMaps;
				RenderMapGrid();
				return;
			}
		}
		catch (Exception ex)
		{
			GD.PrintErr($"[MapDiscovery] Failed to load maps from server: {ex.Message}");
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

		SetupCategoryButton(_btnAll, "ALL MAPS", "All");
		SetupCategoryButton(_btnFeatured, "FEATURED", "Featured");
		SetupCategoryButton(_btnTD, "TOWER DEFENSE", "TD");
		SetupCategoryButton(_btnMelee, "MELEE", "Melee");
		SetupCategoryButton(_btnCampaign, "CAMPAIGN", "Campaign");

		UpdateButtonHighlight();
	}

	private void ApplyBackgroundStyle()
	{
		Texture2D bgTexture = null;
		string[] bgPaths = new string[]
		{
			"res://Assets/UI/procedural_bg.png",
			"res://Assets/UI/map_discovery_bg_rpg_grey2.jpg",
			"res://Assets/UI/map_discovery_bg_fortress2.jpg"
		};

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
			var style = new StyleBoxTexture();
			style.Texture = bgTexture;
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
		var filterPanelStyle = new StyleBoxTexture();
		filterPanelStyle.Texture = GD.Load<Texture2D>("res://Assets/UI/procedural_filter_panel.png");
		filterPanelStyle.TextureMarginLeft = 0;
		filterPanelStyle.TextureMarginRight = 0;
		filterPanelStyle.TextureMarginTop = 0;
		filterPanelStyle.TextureMarginBottom = 0;
		filterPanelStyle.ContentMarginLeft = 55;
		filterPanelStyle.ContentMarginRight = 55;
		filterPanelStyle.ContentMarginTop = 60;
		filterPanelStyle.ContentMarginBottom = 24;
		_filterPanel.AddThemeStyleboxOverride("panel", filterPanelStyle);
		_filterPanel.TextureFilter = CanvasItem.TextureFilterEnum.LinearWithMipmaps;

		var lobbyPanelStyle = new StyleBoxTexture();
		lobbyPanelStyle.Texture = GD.Load<Texture2D>("res://Assets/UI/procedural_main_panel.png");
		lobbyPanelStyle.TextureMarginLeft = 0;
		lobbyPanelStyle.TextureMarginRight = 0;
		lobbyPanelStyle.TextureMarginTop = 0;
		lobbyPanelStyle.TextureMarginBottom = 0;
		lobbyPanelStyle.ContentMarginLeft = 80;
		lobbyPanelStyle.ContentMarginRight = 80;
		lobbyPanelStyle.ContentMarginTop = 130;
		lobbyPanelStyle.ContentMarginBottom = 65;
		_mapListPanel.AddThemeStyleboxOverride("panel", lobbyPanelStyle);
		_mapListPanel.TextureFilter = CanvasItem.TextureFilterEnum.LinearWithMipmaps;
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

		_discoveryTitle.SetAnchorsPreset(Control.LayoutPreset.TopLeft);
		_discoveryTitle.CustomMinimumSize = new Vector2(300, 40);
		_discoveryTitle.Size = new Vector2(300, 40);
		float titleX = _mapListPanel.Position.X + 200.0f;
		_discoveryTitle.Position = new Vector2(titleX, _mapListPanel.Position.Y + 30.0f);
		_discoveryTitle.ZIndex = 1;
		MoveChild(_discoveryTitle, -1);
	}

	private void ApplyBackButtonStyle()
	{
		_backButton.Text = TranslationServer.Translate("MAIN MENU");
		if (_fontNorseBold != null) _backButton.AddThemeFontOverride("font", _fontNorseBold);
		_backButton.AddThemeFontSizeOverride("font_size", 19);
		_backButton.AddThemeConstantOverride("outline_size", 4);
		_backButton.AddThemeColorOverride("font_outline_color", new Color(0.06f, 0.06f, 0.08f));
		_backButton.AddThemeColorOverride("font_color", UIStyle.ColorGoldDull);
		_backButton.AddThemeColorOverride("font_hover_color", UIStyle.ColorGold);
		_backButton.AddThemeColorOverride("font_pressed_color", UIStyle.ColorGold);
		_backButton.AddThemeColorOverride("font_focus_color", UIStyle.ColorGold);
		_backButton.Icon = null;

		_backButton.AddThemeStyleboxOverride("normal", UIStyle.CreateButtonNormal());
		_backButton.AddThemeStyleboxOverride("hover", UIStyle.CreateButtonHover());
		_backButton.AddThemeStyleboxOverride("pressed", UIStyle.CreateButtonPressed());
		_backButton.AddThemeStyleboxOverride("focus", new StyleBoxEmpty());
		_backButton.TextureFilter = CanvasItem.TextureFilterEnum.Linear;

		_backButton.SetAnchorsPreset(Control.LayoutPreset.TopLeft);
		_backButton.CustomMinimumSize = new Vector2(190, 52);
		_backButton.Size = new Vector2(190, 52);
		_backButton.Position = new Vector2(1590, 50);
		_backButton.ZIndex = 1;

		_backButton.Pressed += () =>
		{
			UIManager.Instance.PlayClickSound();
			UIManager.Instance.TransitionTo(GameScreen.MainMenu);
		};
		_backButton.MouseEntered += () => UIManager.Instance.PlayHoverSound();
	}

	private void ApplySearchBarStyle()
	{
		var searchStyle = new StyleBoxTexture();
		searchStyle.Texture = GD.Load<Texture2D>("res://Assets/UI/procedural_search_bar.png");
		searchStyle.TextureMarginLeft = 0;
		searchStyle.TextureMarginRight = 0;
		searchStyle.TextureMarginTop = 0;
		searchStyle.TextureMarginBottom = 0;
		searchStyle.ContentMarginLeft = 24;
		searchStyle.ContentMarginRight = 24;
		searchStyle.ContentMarginTop = 0;
		searchStyle.ContentMarginBottom = 0;

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
		button.TextureFilter = CanvasItem.TextureFilterEnum.LinearWithMipmaps;
		
		button.Pressed += () =>
		{
			UIManager.Instance.PlayClickSound();
			_selectedCategory = categoryValue;
			UpdateButtonHighlight();
			RenderMapGrid();
		};
		button.MouseEntered += () => UIManager.Instance.PlayHoverSound();
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
		button.TextureFilter = CanvasItem.TextureFilterEnum.LinearWithMipmaps;
		
		if (highlight)
		{
			button.AddThemeColorOverride("font_color", UIStyle.ColorGold);
			button.AddThemeColorOverride("font_hover_color", new Color(1.0f, 0.94f, 0.75f));
			button.AddThemeColorOverride("font_pressed_color", UIStyle.ColorGold);
			button.AddThemeColorOverride("font_focus_color", UIStyle.ColorGold);
			button.AddThemeConstantOverride("outline_size", 5);
			button.AddThemeColorOverride("font_outline_color", new Color(0.22f, 0.16f, 0.04f));
			
			var activeStyle = new StyleBoxTexture();
			activeStyle.Texture = GD.Load<Texture2D>("res://Assets/UI/procedural_btn_normal.png");
			activeStyle.TextureMarginLeft = 0;
			activeStyle.TextureMarginRight = 0;
			activeStyle.TextureMarginTop = 0;
			activeStyle.TextureMarginBottom = 0;
			activeStyle.ContentMarginLeft = 16;
			activeStyle.ContentMarginRight = 16;
			activeStyle.ContentMarginTop = 18;
			activeStyle.ContentMarginBottom = 14;
			activeStyle.ModulateColor = new Color(1.30f, 1.20f, 0.88f);
			
			var activeHover = (StyleBoxTexture)activeStyle.Duplicate();
			activeHover.ModulateColor = new Color(1.42f, 1.30f, 0.95f);
			
			var activePressed = (StyleBoxTexture)activeStyle.Duplicate();
			activePressed.ModulateColor = new Color(1.08f, 1.00f, 0.75f);
			
			button.AddThemeStyleboxOverride("normal", activeStyle);
			button.AddThemeStyleboxOverride("hover", activeHover);
			button.AddThemeStyleboxOverride("pressed", activePressed);
		}
		else
		{
			button.AddThemeColorOverride("font_color", UIStyle.ColorGoldDull);
			button.AddThemeColorOverride("font_hover_color", UIStyle.ColorGold);
			button.AddThemeColorOverride("font_pressed_color", UIStyle.ColorGold);
			button.AddThemeColorOverride("font_focus_color", UIStyle.ColorGold);
			button.AddThemeConstantOverride("outline_size", 4);
			button.AddThemeColorOverride("font_outline_color", new Color(0.06f, 0.06f, 0.08f));
			
			var normalStyle = new StyleBoxTexture();
			normalStyle.Texture = GD.Load<Texture2D>("res://Assets/UI/procedural_btn_normal.png");
			normalStyle.TextureMarginLeft = 0;
			normalStyle.TextureMarginRight = 0;
			normalStyle.TextureMarginTop = 0;
			normalStyle.TextureMarginBottom = 0;
			normalStyle.ContentMarginLeft = 16;
			normalStyle.ContentMarginRight = 16;
			normalStyle.ContentMarginTop = 18;
			normalStyle.ContentMarginBottom = 14;
			normalStyle.ModulateColor = new Color(0.85f, 0.85f, 0.88f);
			
			var hoverStyle = (StyleBoxTexture)normalStyle.Duplicate();
			hoverStyle.ModulateColor = new Color(1.12f, 1.08f, 0.92f);
			
			var pressedStyle = (StyleBoxTexture)normalStyle.Duplicate();
			pressedStyle.ModulateColor = new Color(0.75f, 0.75f, 0.78f);
			
			button.AddThemeStyleboxOverride("normal", normalStyle);
			button.AddThemeStyleboxOverride("hover", hoverStyle);
			button.AddThemeStyleboxOverride("pressed", pressedStyle);
		}
	}

	private void RegisterEvents()
	{
		_searchBar.TextChanged += (text) =>
		{
			_searchQuery = text.Trim();
			RenderMapGrid();
		};
	}

	private void RenderMapGrid()
	{
		foreach (Node child in _mapGrid.GetChildren())
		{
			child.QueueFree();
		}

		var filtered = _allMaps.AsEnumerable();
		filtered = FilterByCategory(filtered);
		filtered = FilterBySearchQuery(filtered);

		var list = filtered.ToList();

		if (list.Count == 0)
		{
			var noMapsLabel = new Label();
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

	private IEnumerable<MapData> FilterByCategory(IEnumerable<MapData> maps)
	{
		if (_selectedCategory == "All") return maps;

		if (_selectedCategory == "Featured")
		{
			return maps.Where(m => m.RatingStars >= 4.5f);
		}

		return maps.Where(m => m.Genre.Contains(_selectedCategory) || m.Features.Contains(_selectedCategory) || m.MapId.Contains(_selectedCategory));
	}

	private IEnumerable<MapData> FilterBySearchQuery(IEnumerable<MapData> maps)
	{
		if (string.IsNullOrEmpty(_searchQuery)) return maps;

		return maps.Where(m => m.Title.Contains(_searchQuery, StringComparison.OrdinalIgnoreCase) || 
		                       m.Creator.Contains(_searchQuery, StringComparison.OrdinalIgnoreCase) ||
		                       m.Description.Contains(_searchQuery, StringComparison.OrdinalIgnoreCase));
	}

	private Control CreateMapCard(MapData map)
	{
		EnsureFontsLoaded();

		var card = new PanelContainer();
		card.CustomMinimumSize = new Vector2(560, 220);
		card.TextureFilter = CanvasItem.TextureFilterEnum.LinearWithMipmaps;
		
		var cardStyle = new StyleBoxTexture();
		cardStyle.Texture = GD.Load<Texture2D>("res://Assets/UI/procedural_card_bg.png");
		cardStyle.TextureMarginLeft = 0;
		cardStyle.TextureMarginRight = 0;
		cardStyle.TextureMarginTop = 0;
		cardStyle.TextureMarginBottom = 0;
		cardStyle.ContentMarginLeft = 32;
		cardStyle.ContentMarginRight = 32;
		cardStyle.ContentMarginTop = 38;
		cardStyle.ContentMarginBottom = 26;
		card.AddThemeStyleboxOverride("panel", cardStyle);

		var hBox = new HBoxContainer();
		hBox.AddThemeConstantOverride("separation", 16);
		card.AddChild(hBox);

		var imgContainer = new Control();
		imgContainer.CustomMinimumSize = new Vector2(180, 144);
		imgContainer.SizeFlagsVertical = Control.SizeFlags.ShrinkCenter;
		imgContainer.TextureFilter = CanvasItem.TextureFilterEnum.LinearWithMipmaps;

		var thumbnail = new TextureRect();
		thumbnail.SetAnchorsPreset(Control.LayoutPreset.FullRect);
		thumbnail.OffsetLeft = 12;
		thumbnail.OffsetTop = 12;
		thumbnail.OffsetRight = -12;
		thumbnail.OffsetBottom = -12;
		thumbnail.ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize;
		thumbnail.StretchMode = TextureRect.StretchModeEnum.KeepAspectCovered;
		thumbnail.TextureFilter = CanvasItem.TextureFilterEnum.LinearWithMipmaps;
		var loadedTex = LoadTextureSafe(map.ThumbnailPath);
		thumbnail.Texture = loadedTex ?? UIStyle.EmptyBlackTexture;
		imgContainer.AddChild(thumbnail);

		var frameOverlay = new TextureRect();
		frameOverlay.SetAnchorsPreset(Control.LayoutPreset.FullRect);
		frameOverlay.ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize;
		frameOverlay.StretchMode = TextureRect.StretchModeEnum.Scale;
		frameOverlay.TextureFilter = CanvasItem.TextureFilterEnum.LinearWithMipmaps;
		frameOverlay.Texture = GD.Load<Texture2D>("res://Assets/UI/procedural_card_img.png");
		imgContainer.AddChild(frameOverlay);

		hBox.AddChild(imgContainer);

		var vBox = new VBoxContainer();
		vBox.SizeFlagsHorizontal = SizeFlags.ExpandFill;
		vBox.SizeFlagsVertical = SizeFlags.ExpandFill;
		vBox.AddThemeConstantOverride("separation", 2);
		hBox.AddChild(vBox);

		var headerBox = new HBoxContainer();
		headerBox.SizeFlagsHorizontal = SizeFlags.ExpandFill;
		vBox.AddChild(headerBox);

		var title = new Label();
		title.Text = map.Title;
		if (_fontNorseBold != null) title.AddThemeFontOverride("font", _fontNorseBold);
		title.AddThemeColorOverride("font_color", UIStyle.ColorGold);
		title.AddThemeFontSizeOverride("font_size", 22);
		title.AddThemeConstantOverride("outline_size", 4);
		title.AddThemeColorOverride("font_outline_color", new Color(0.06f, 0.06f, 0.08f));
		title.SizeFlagsHorizontal = SizeFlags.ExpandFill;
		headerBox.AddChild(title);

		var ratingLabel = new Label();
		ratingLabel.Text = $"★ {map.RatingStars:F1}";
		if (_fontNorseBold != null) ratingLabel.AddThemeFontOverride("font", _fontNorseBold);
		ratingLabel.AddThemeColorOverride("font_color", UIStyle.ColorGold);
		ratingLabel.AddThemeFontSizeOverride("font_size", 17);
		ratingLabel.AddThemeConstantOverride("outline_size", 4);
		ratingLabel.AddThemeColorOverride("font_outline_color", new Color(0.06f, 0.06f, 0.08f));
		headerBox.AddChild(ratingLabel);

		var creator = new Label();
		creator.Text = $"By: {map.Creator}  •  {map.Genre}";
		if (_fontNorseBold != null) creator.AddThemeFontOverride("font", _fontNorseBold);
		creator.AddThemeColorOverride("font_color", UIStyle.ColorGoldDull);
		creator.AddThemeFontSizeOverride("font_size", 14);
		vBox.AddChild(creator);

		var desc = new Label();
		desc.Text = map.Description;
		desc.AutowrapMode = TextServer.AutowrapMode.WordSmart;
		desc.SizeFlagsVertical = SizeFlags.ExpandFill;
		if (_fontNorseBold != null) desc.AddThemeFontOverride("font", _fontNorseBold);
		desc.AddThemeColorOverride("font_color", new Color(0.94f, 0.94f, 0.96f));
		desc.AddThemeFontSizeOverride("font_size", 14);
		vBox.AddChild(desc);

		var footer = new HBoxContainer();
		footer.Alignment = BoxContainer.AlignmentMode.End;
		vBox.AddChild(footer);

		var btnDetails = new Button();
		btnDetails.CustomMinimumSize = new Vector2(110, 32);
		btnDetails.Text = TranslationServer.Translate("DETAILS");
		if (_fontNorseBold != null) btnDetails.AddThemeFontOverride("font", _fontNorseBold);
		btnDetails.AddThemeFontSizeOverride("font_size", 16);
		btnDetails.AddThemeConstantOverride("outline_size", 4);
		btnDetails.AddThemeColorOverride("font_outline_color", new Color(0.06f, 0.06f, 0.08f));
		btnDetails.AddThemeColorOverride("font_color", UIStyle.ColorGoldDull);
		btnDetails.AddThemeColorOverride("font_hover_color", UIStyle.ColorGold);
		btnDetails.AddThemeColorOverride("font_pressed_color", UIStyle.ColorGold);
		btnDetails.AddThemeColorOverride("font_focus_color", UIStyle.ColorGold);
		btnDetails.AddThemeStyleboxOverride("normal", UIStyle.CreateButtonNormal());
		btnDetails.AddThemeStyleboxOverride("hover", UIStyle.CreateButtonHover());
		btnDetails.AddThemeStyleboxOverride("pressed", UIStyle.CreateButtonPressed());
		btnDetails.AddThemeStyleboxOverride("focus", new StyleBoxEmpty());
		btnDetails.TextureFilter = CanvasItem.TextureFilterEnum.LinearWithMipmaps;

		btnDetails.Pressed += () =>
		{
			UIManager.Instance.PlayClickSound();
			UIManager.Instance.TransitionToMapDetails(map);
		};
		btnDetails.MouseEntered += () => UIManager.Instance.PlayHoverSound();
		footer.AddChild(btnDetails);

		return card;
	}

	private static Texture2D LoadTextureSafe(string resPath)
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

	private static Version ParseVersion(string versionStr)
	{
		if (string.IsNullOrWhiteSpace(versionStr)) return new Version(1, 0, 0);
		string cleaned = versionStr.TrimStart('v', 'V').Trim();
		if (Version.TryParse(cleaned, out var v))
		{
			return v;
		}
		var parts = cleaned.Split('.');
		if (parts.Length == 1 && int.TryParse(parts[0], out int major))
		{
			return new Version(major, 0, 0);
		}
		if (parts.Length == 2 && int.TryParse(parts[0], out int maj) && int.TryParse(parts[1], out int min))
		{
			return new Version(maj, min, 0);
		}
		return new Version(0, 0, 0);
	}
}