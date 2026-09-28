using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Godot;
using Realm.Godot.Services;
using Realm.Shared.Distribution;
using Realm.Shared.Metadata;

public partial class StorageMenu : Control
{
	private MapStorageService _mapStorageService;

	private Button _backButton;
	private Label _titleLabel;
	private Button _importMapButton;

	private LineEdit _searchBar;
	private Label _mapCountLabel;
	private VBoxContainer _mapListContainer;

	private PanelContainer _rightNavPanel;
	private Label _emptySelectionLabel;
	private VBoxContainer _selectedMapContainer;

	private TextureRect _mapThumbnail;
	private Label _selectedTitleLabel;
	private Label _selectedAuthorLabel;
	private Label _selectedGenreLabel;
	private Label _selectedDescriptionLabel;
	private Label _selectedTotalSizeLabel;
	private Button _downloadUpdateButton;

	private VBoxContainer _versionsListContainer;

	private List<DownloadedMapInfo> _allDownloadedMaps = new();
	private DownloadedMapInfo? _selectedMap;
	private string _searchFilter = string.Empty;

	public override void _Ready()
	{
		MouseFilter = MouseFilterEnum.Pass;
		TextureFilter = CanvasItem.TextureFilterEnum.LinearWithMipmaps;
		_mapStorageService = ServiceLocator.Get<MapStorageService>();

		BuildLayout();
		RefreshDownloadedMaps();
	}

	private void BuildLayout()
	{
		var bgPanel = new Panel();
		bgPanel.SetAnchorsPreset(LayoutPreset.FullRect);
		bgPanel.AddThemeStyleboxOverride("panel", UIStyle.CreateEntranceBgTexture());
		bgPanel.MouseFilter = MouseFilterEnum.Ignore;
		AddChild(bgPanel);

		var rootVBox = new VBoxContainer();
		rootVBox.SetAnchorsPreset(LayoutPreset.FullRect);
		rootVBox.AddThemeConstantOverride("separation", 0);
		rootVBox.MouseFilter = MouseFilterEnum.Pass;
		AddChild(rootVBox);

		var topHeaderPanel = new PanelContainer();
		topHeaderPanel.CustomMinimumSize = new Vector2(0, 70);
		topHeaderPanel.AddThemeStyleboxOverride("panel", CreateHeaderPanelStyle());
		topHeaderPanel.MouseFilter = MouseFilterEnum.Pass;
		rootVBox.AddChild(topHeaderPanel);

		var topHeaderHBox = new HBoxContainer();
		topHeaderHBox.AddThemeConstantOverride("separation", 20);
		topHeaderHBox.Alignment = BoxContainer.AlignmentMode.Begin;
		topHeaderHBox.MouseFilter = MouseFilterEnum.Pass;
		topHeaderPanel.AddChild(topHeaderHBox);

		_backButton = new Button();
		_backButton.AddThemeConstantOverride("icon_max_width", 24);
		_backButton.CustomMinimumSize = new Vector2(130, 42);
		UIStyle.ApplyButtonText(_backButton, "◀ " + TranslationServer.Translate("BACK"), 16);
		_backButton.AddThemeStyleboxOverride("normal", UIStyle.CreateButtonNormal());
		_backButton.AddThemeStyleboxOverride("hover", UIStyle.CreateButtonHover());
		_backButton.AddThemeStyleboxOverride("pressed", UIStyle.CreateButtonPressed());
		_backButton.FocusMode = FocusModeEnum.None;
		_backButton.MouseFilter = MouseFilterEnum.Stop;
		_backButton.MouseEntered += () => UIManager.Instance?.PlayHoverSound();
		_backButton.Pressed += () =>
		{
			UIManager.Instance?.PlayClickSound();
			UIManager.Instance.TransitionTo(GameScreen.MainMenu);
		};
		topHeaderHBox.AddChild(_backButton);

		_titleLabel = new Label();
		UIStyle.ApplyTitle(_titleLabel, TranslationServer.Translate("MAP STORAGE"), 28);
		_titleLabel.SizeFlagsHorizontal = SizeFlags.ExpandFill;
		_titleLabel.HorizontalAlignment = HorizontalAlignment.Center;
		_titleLabel.MouseFilter = MouseFilterEnum.Ignore;
		topHeaderHBox.AddChild(_titleLabel);

		_importMapButton = new Button();
		_importMapButton.AddThemeConstantOverride("icon_max_width", 24);
		_importMapButton.CustomMinimumSize = new Vector2(210, 42);
		UIStyle.ApplyButtonText(_importMapButton, "📥 " + TranslationServer.Translate("IMPORT MAP (.RMAP)"), 14);
		_importMapButton.AddThemeStyleboxOverride("normal", UIStyle.CreateButtonNormal());
		_importMapButton.AddThemeStyleboxOverride("hover", UIStyle.CreateButtonHover());
		_importMapButton.AddThemeStyleboxOverride("pressed", UIStyle.CreateButtonPressed());
		_importMapButton.FocusMode = FocusModeEnum.None;
		_importMapButton.MouseFilter = MouseFilterEnum.Stop;
		_importMapButton.MouseEntered += () => UIManager.Instance?.PlayHoverSound();
		_importMapButton.Pressed += () =>
		{
			UIManager.Instance?.PlayClickSound();
			OnImportMapPressed();
		};
		topHeaderHBox.AddChild(_importMapButton);

		var mainMargin = new MarginContainer();
		mainMargin.SizeFlagsHorizontal = SizeFlags.ExpandFill;
		mainMargin.SizeFlagsVertical = SizeFlags.ExpandFill;
		mainMargin.AddThemeConstantOverride("margin_left", 35);
		mainMargin.AddThemeConstantOverride("margin_right", 35);
		mainMargin.AddThemeConstantOverride("margin_top", 25);
		mainMargin.AddThemeConstantOverride("margin_bottom", 60);
		mainMargin.MouseFilter = MouseFilterEnum.Pass;
		rootVBox.AddChild(mainMargin);

		var mainHBox = new HBoxContainer();
		mainHBox.AddThemeConstantOverride("separation", 20);
		mainHBox.SizeFlagsHorizontal = SizeFlags.ExpandFill;
		mainHBox.SizeFlagsVertical = SizeFlags.ExpandFill;
		mainHBox.MouseFilter = MouseFilterEnum.Pass;
		mainMargin.AddChild(mainHBox);

		BuildLeftNav(mainHBox);
		BuildRightNav(mainHBox);
	}

	private void BuildLeftNav(HBoxContainer parent)
	{
		var leftPanelContainer = new PanelContainer();
		leftPanelContainer.CustomMinimumSize = new Vector2(380, 0);
		leftPanelContainer.SizeFlagsVertical = SizeFlags.ExpandFill;
		leftPanelContainer.TextureFilter = CanvasItem.TextureFilterEnum.LinearWithMipmaps;
		leftPanelContainer.AddThemeStyleboxOverride("panel", UIStyle.CreateMapStorageFilterPanelStyle());
		leftPanelContainer.MouseFilter = MouseFilterEnum.Pass;
		parent.AddChild(leftPanelContainer);

		var leftVBox = new VBoxContainer();
		leftVBox.AddThemeConstantOverride("separation", 12);
		leftVBox.SizeFlagsHorizontal = SizeFlags.ExpandFill;
		leftVBox.SizeFlagsVertical = SizeFlags.ExpandFill;
		leftVBox.MouseFilter = MouseFilterEnum.Pass;
		leftPanelContainer.AddChild(leftVBox);

		_searchBar = new LineEdit();
		_searchBar.PlaceholderText = TranslationServer.Translate("Search Maps...");
		_searchBar.CustomMinimumSize = new Vector2(0, 40);
		_searchBar.AddThemeStyleboxOverride("normal", UIStyle.CreateTextInput(false));
		_searchBar.AddThemeStyleboxOverride("focus", UIStyle.CreateTextInput(true));
		_searchBar.AddThemeColorOverride("font_color", new Color(0.95f, 0.95f, 0.95f));
		_searchBar.AddThemeColorOverride("font_placeholder_color", new Color(0.65f, 0.60f, 0.50f));
		_searchBar.MouseFilter = MouseFilterEnum.Stop;
		_searchBar.TextChanged += (newText) =>
		{
			_searchFilter = newText?.Trim() ?? string.Empty;
			RenderMapList();
		};
		leftVBox.AddChild(_searchBar);

		var mapCountMargin = new MarginContainer();
		mapCountMargin.AddThemeConstantOverride("margin_top", 24);
		mapCountMargin.AddThemeConstantOverride("margin_bottom", 4);
		mapCountMargin.MouseFilter = MouseFilterEnum.Ignore;
		leftVBox.AddChild(mapCountMargin);

		_mapCountLabel = new Label();
		_mapCountLabel.Text = string.Format(TranslationServer.Translate("Downloaded Maps ({0})"), 0);
		_mapCountLabel.AddThemeColorOverride("font_color", UIStyle.ColorGoldDull);
		_mapCountLabel.AddThemeFontSizeOverride("font_size", 13);
		_mapCountLabel.MouseFilter = MouseFilterEnum.Ignore;
		mapCountMargin.AddChild(_mapCountLabel);

		var scrollContainer = new ScrollContainer();
		scrollContainer.SizeFlagsHorizontal = SizeFlags.ExpandFill;
		scrollContainer.SizeFlagsVertical = SizeFlags.ExpandFill;
		scrollContainer.HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled;
		scrollContainer.MouseFilter = MouseFilterEnum.Pass;
		leftVBox.AddChild(scrollContainer);

		_mapListContainer = new VBoxContainer();
		_mapListContainer.SizeFlagsHorizontal = SizeFlags.ExpandFill;
		_mapListContainer.SizeFlagsVertical = SizeFlags.ExpandFill;
		_mapListContainer.AddThemeConstantOverride("separation", 8);
		_mapListContainer.MouseFilter = MouseFilterEnum.Pass;
		scrollContainer.AddChild(_mapListContainer);
	}

	private void BuildRightNav(HBoxContainer parent)
	{
		_rightNavPanel = new PanelContainer();
		_rightNavPanel.SizeFlagsHorizontal = SizeFlags.ExpandFill;
		_rightNavPanel.SizeFlagsVertical = SizeFlags.ExpandFill;
		_rightNavPanel.TextureFilter = CanvasItem.TextureFilterEnum.LinearWithMipmaps;
		_rightNavPanel.AddThemeStyleboxOverride("panel", UIStyle.CreateMapStorageMainPanelStyle());
		_rightNavPanel.MouseFilter = MouseFilterEnum.Pass;
		parent.AddChild(_rightNavPanel);

		_emptySelectionLabel = new Label();
		_emptySelectionLabel.Text = TranslationServer.Translate("Select a map to view details.");
		_emptySelectionLabel.HorizontalAlignment = HorizontalAlignment.Center;
		_emptySelectionLabel.VerticalAlignment = VerticalAlignment.Center;
		_emptySelectionLabel.SizeFlagsHorizontal = SizeFlags.ExpandFill;
		_emptySelectionLabel.SizeFlagsVertical = SizeFlags.ExpandFill;
		_emptySelectionLabel.AddThemeColorOverride("font_color", UIStyle.ColorGoldDull);
		_emptySelectionLabel.AddThemeFontSizeOverride("font_size", 16);
		_emptySelectionLabel.MouseFilter = MouseFilterEnum.Ignore;
		_rightNavPanel.AddChild(_emptySelectionLabel);

		_selectedMapContainer = new VBoxContainer();
		_selectedMapContainer.SizeFlagsHorizontal = SizeFlags.ExpandFill;
		_selectedMapContainer.SizeFlagsVertical = SizeFlags.ExpandFill;
		_selectedMapContainer.AddThemeConstantOverride("separation", 16);
		_selectedMapContainer.MouseFilter = MouseFilterEnum.Pass;
		_selectedMapContainer.Visible = false;
		_rightNavPanel.AddChild(_selectedMapContainer);

		var topDetailsHBox = new HBoxContainer();
		topDetailsHBox.AddThemeConstantOverride("separation", 16);
		topDetailsHBox.MouseFilter = MouseFilterEnum.Pass;
		_selectedMapContainer.AddChild(topDetailsHBox);

		_mapThumbnail = new TextureRect();
		_mapThumbnail.CustomMinimumSize = new Vector2(140, 100);
		_mapThumbnail.ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize;
		_mapThumbnail.StretchMode = TextureRect.StretchModeEnum.KeepAspectCovered;
		_mapThumbnail.TextureFilter = CanvasItem.TextureFilterEnum.LinearWithMipmaps;
		_mapThumbnail.Texture = UIStyle.EmptyBlackTexture;
		_mapThumbnail.MouseFilter = MouseFilterEnum.Ignore;
		topDetailsHBox.AddChild(_mapThumbnail);

		var metaVBox = new VBoxContainer();
		metaVBox.SizeFlagsHorizontal = SizeFlags.ExpandFill;
		metaVBox.AddThemeConstantOverride("separation", 4);
		metaVBox.MouseFilter = MouseFilterEnum.Pass;
		topDetailsHBox.AddChild(metaVBox);

		_selectedTitleLabel = new Label();
		UIStyle.ApplyTitle(_selectedTitleLabel, "Map Title", 22);
		_selectedTitleLabel.MouseFilter = MouseFilterEnum.Ignore;
		metaVBox.AddChild(_selectedTitleLabel);

		_selectedAuthorLabel = new Label();
		_selectedAuthorLabel.Text = "Author: Unknown";
		_selectedAuthorLabel.AddThemeColorOverride("font_color", UIStyle.ColorGoldDull);
		_selectedAuthorLabel.AddThemeFontSizeOverride("font_size", 13);
		_selectedAuthorLabel.MouseFilter = MouseFilterEnum.Ignore;
		metaVBox.AddChild(_selectedAuthorLabel);

		_selectedGenreLabel = new Label();
		_selectedGenreLabel.Text = "Genre: Custom Map";
		_selectedGenreLabel.AddThemeColorOverride("font_color", UIStyle.ColorCyanGlow);
		_selectedGenreLabel.AddThemeFontSizeOverride("font_size", 12);
		metaVBox.AddChild(_selectedGenreLabel);

		_selectedTotalSizeLabel = new Label();
		_selectedTotalSizeLabel.Text = "Total Storage: 0 MB";
		_selectedTotalSizeLabel.AddThemeColorOverride("font_color", new Color(0.8f, 0.8f, 0.8f));
		_selectedTotalSizeLabel.AddThemeFontSizeOverride("font_size", 12);
		metaVBox.AddChild(_selectedTotalSizeLabel);

		_selectedDescriptionLabel = new Label();
		_selectedDescriptionLabel.Text = "";
		_selectedDescriptionLabel.AutowrapMode = TextServer.AutowrapMode.WordSmart;
		_selectedDescriptionLabel.AddThemeColorOverride("font_color", new Color(0.85f, 0.85f, 0.85f));
		_selectedDescriptionLabel.AddThemeFontSizeOverride("font_size", 13);
		_selectedDescriptionLabel.MouseFilter = MouseFilterEnum.Ignore;
		_selectedMapContainer.AddChild(_selectedDescriptionLabel);

		var actionsHBox = new HBoxContainer();
		actionsHBox.AddThemeConstantOverride("separation", 14);
		actionsHBox.MouseFilter = MouseFilterEnum.Pass;
		_selectedMapContainer.AddChild(actionsHBox);

		_downloadUpdateButton = new Button();
		_downloadUpdateButton.AddThemeConstantOverride("icon_max_width", 20);
		_downloadUpdateButton.CustomMinimumSize = new Vector2(240, 38);
		UIStyle.ApplyButtonText(_downloadUpdateButton, "⚡ " + TranslationServer.Translate("DOWNLOAD UPDATED VERSION"), 14);
		_downloadUpdateButton.AddThemeStyleboxOverride("normal", UIStyle.CreateButtonNormal());
		_downloadUpdateButton.AddThemeStyleboxOverride("hover", UIStyle.CreateButtonHover());
		_downloadUpdateButton.AddThemeStyleboxOverride("pressed", UIStyle.CreateButtonPressed());
		_downloadUpdateButton.FocusMode = FocusModeEnum.None;
		_downloadUpdateButton.MouseFilter = MouseFilterEnum.Stop;
		_downloadUpdateButton.Visible = false;
		_downloadUpdateButton.MouseEntered += () => UIManager.Instance?.PlayHoverSound();
		_downloadUpdateButton.Pressed += () =>
		{
			UIManager.Instance?.PlayClickSound();
			OnDownloadUpdatePressed();
		};
		actionsHBox.AddChild(_downloadUpdateButton);

		var separator = new HSeparator();
		separator.MouseFilter = MouseFilterEnum.Ignore;
		_selectedMapContainer.AddChild(separator);

		var versionsHeaderLabel = new Label();
		UIStyle.ApplyTitle(versionsHeaderLabel, TranslationServer.Translate("INSTALLED VERSIONS"), 16);
		versionsHeaderLabel.MouseFilter = MouseFilterEnum.Ignore;
		_selectedMapContainer.AddChild(versionsHeaderLabel);

		var versionsScroll = new ScrollContainer();
		versionsScroll.SizeFlagsHorizontal = SizeFlags.ExpandFill;
		versionsScroll.SizeFlagsVertical = SizeFlags.ExpandFill;
		versionsScroll.HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled;
		versionsScroll.MouseFilter = MouseFilterEnum.Pass;
		_selectedMapContainer.AddChild(versionsScroll);

		_versionsListContainer = new VBoxContainer();
		_versionsListContainer.SizeFlagsHorizontal = SizeFlags.ExpandFill;
		_versionsListContainer.SizeFlagsVertical = SizeFlags.ExpandFill;
		_versionsListContainer.AddThemeConstantOverride("separation", 10);
		_versionsListContainer.MouseFilter = MouseFilterEnum.Pass;
		versionsScroll.AddChild(_versionsListContainer);
	}

	private void RefreshDownloadedMaps()
	{
		_allDownloadedMaps = _mapStorageService.GetDownloadedMaps().ToList();
		RenderMapList();

		if (_selectedMap != null)
		{
			var currentSelected = _allDownloadedMaps.FirstOrDefault(m => string.Equals(m.Title, _selectedMap.Title, StringComparison.OrdinalIgnoreCase));
			if (currentSelected != null)
			{
				SelectMap(currentSelected);
			}
			else if (_allDownloadedMaps.Count > 0)
			{
				SelectMap(_allDownloadedMaps[0]);
			}
			else
			{
				ClearSelection();
			}
		}
		else if (_allDownloadedMaps.Count > 0)
		{
			SelectMap(_allDownloadedMaps[0]);
		}
		else
		{
			ClearSelection();
		}
	}

	private void RenderMapList()
	{
		foreach (var child in _mapListContainer.GetChildren())
		{
			child.QueueFree();
		}

		var filteredMaps = string.IsNullOrWhiteSpace(_searchFilter)
			? _allDownloadedMaps
			: _allDownloadedMaps.Where(m =>
				m.Title.Contains(_searchFilter, StringComparison.OrdinalIgnoreCase) ||
				m.Author.Contains(_searchFilter, StringComparison.OrdinalIgnoreCase) ||
				m.Tags.Any(t => t.Contains(_searchFilter, StringComparison.OrdinalIgnoreCase))).ToList();

		_mapCountLabel.Text = string.Format(TranslationServer.Translate("Downloaded Maps ({0})"), filteredMaps.Count);

		if (filteredMaps.Count == 0)
		{
			var emptyLabel = new Label();
			emptyLabel.Text = TranslationServer.Translate("No maps currently downloaded to storage.");
			emptyLabel.AddThemeColorOverride("font_color", UIStyle.ColorGoldDull);
			emptyLabel.AddThemeFontSizeOverride("font_size", 13);
			emptyLabel.AutowrapMode = TextServer.AutowrapMode.WordSmart;
			emptyLabel.MouseFilter = MouseFilterEnum.Ignore;
			_mapListContainer.AddChild(emptyLabel);
			return;
		}

		foreach (var map in filteredMaps)
		{
			var mapCard = CreateMapCard(map);
			_mapListContainer.AddChild(mapCard);
		}
	}

	private Button CreateMapCard(DownloadedMapInfo map)
	{
		var btn = new Button();
		btn.AddThemeConstantOverride("icon_max_width", 0);
		btn.CustomMinimumSize = new Vector2(0, 60);
		btn.SizeFlagsHorizontal = SizeFlags.ExpandFill;
		btn.FocusMode = FocusModeEnum.None;
		btn.MouseFilter = MouseFilterEnum.Stop;

		bool isSelected = _selectedMap != null && string.Equals(_selectedMap.Title, map.Title, StringComparison.OrdinalIgnoreCase);

		btn.AddThemeStyleboxOverride("normal", CreateCardStyle(isSelected, false));
		btn.AddThemeStyleboxOverride("hover", CreateCardStyle(isSelected, true));
		btn.AddThemeStyleboxOverride("pressed", CreateCardStyle(true, false));

		var hbox = new HBoxContainer();
		hbox.SetAnchorsPreset(LayoutPreset.FullRect);
		hbox.AddThemeConstantOverride("margin_left", 8);
		hbox.AddThemeConstantOverride("margin_right", 8);
		hbox.AddThemeConstantOverride("separation", 10);
		hbox.MouseFilter = MouseFilterEnum.Ignore;
		btn.AddChild(hbox);

		var cardVBox = new VBoxContainer();
		cardVBox.SizeFlagsHorizontal = SizeFlags.ExpandFill;
		cardVBox.Alignment = BoxContainer.AlignmentMode.Center;
		cardVBox.AddThemeConstantOverride("separation", 2);
		cardVBox.MouseFilter = MouseFilterEnum.Ignore;
		hbox.AddChild(cardVBox);

		var titleLbl = new Label();
		titleLbl.Text = map.Title;
		titleLbl.AddThemeColorOverride("font_color", isSelected ? UIStyle.ColorGold : new Color(0.95f, 0.95f, 0.95f));
		titleLbl.AddThemeFontSizeOverride("font_size", 14);
		titleLbl.TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis;
		titleLbl.MouseFilter = MouseFilterEnum.Ignore;
		cardVBox.AddChild(titleLbl);

		var subLbl = new Label();
		string versionsText = map.Versions.Count == 1 ? "1 Version" : $"{map.Versions.Count} Versions";
		subLbl.Text = $"{map.Author} • {versionsText} • {map.FormattedTotalSize}";
		subLbl.AddThemeColorOverride("font_color", UIStyle.ColorGoldDull);
		subLbl.AddThemeFontSizeOverride("font_size", 11);
		subLbl.TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis;
		subLbl.MouseFilter = MouseFilterEnum.Ignore;
		cardVBox.AddChild(subLbl);

		btn.MouseEntered += () => UIManager.Instance?.PlayHoverSound();
		btn.Pressed += () =>
		{
			UIManager.Instance?.PlayClickSound();
			SelectMap(map);
			RenderMapList();
		};

		return btn;
	}

	private void SelectMap(DownloadedMapInfo map)
	{
		_selectedMap = map;
		_emptySelectionLabel.Visible = false;
		_selectedMapContainer.Visible = true;

		_selectedTitleLabel.Text = map.Title;
		_selectedAuthorLabel.Text = $"Author: {map.Author}";
		_selectedGenreLabel.Text = $"Genre: {map.Genre}" + (map.Tags.Count > 0 ? $" • Tags: {string.Join(", ", map.Tags)}" : "");
		_selectedTotalSizeLabel.Text = $"Total Storage: {map.FormattedTotalSize}";
		_selectedDescriptionLabel.Text = !string.IsNullOrWhiteSpace(map.Description) ? map.Description : "No description provided.";

		if (!string.IsNullOrEmpty(map.ThumbnailPath) && File.Exists(map.ThumbnailPath))
		{
			var img = Image.LoadFromFile(map.ThumbnailPath);
			if (img != null && !img.IsEmpty())
			{
				_mapThumbnail.Texture = ImageTexture.CreateFromImage(img);
			}
			else
			{
				_mapThumbnail.Texture = UIStyle.EmptyBlackTexture;
			}
		}
		else
		{
			_mapThumbnail.Texture = UIStyle.EmptyBlackTexture;
		}

		_downloadUpdateButton.Visible = false;
		RenderVersionsList(map);
		CheckForServerUpdatesAsync(map);
	}

	private void ClearSelection()
	{
		_selectedMap = null;
		_emptySelectionLabel.Visible = true;
		_selectedMapContainer.Visible = false;
	}

	private void RenderVersionsList(DownloadedMapInfo map)
	{
		foreach (var child in _versionsListContainer.GetChildren())
		{
			child.QueueFree();
		}

		foreach (var version in map.Versions)
		{
			var versionPanel = CreateVersionRow(map, version);
			_versionsListContainer.AddChild(versionPanel);
		}
	}

	private PanelContainer CreateVersionRow(DownloadedMapInfo map, DownloadedMapVersionInfo version)
	{
		var panel = new PanelContainer();
		panel.AddThemeStyleboxOverride("panel", CreateVersionRowStyle());
		panel.CustomMinimumSize = new Vector2(0, 52);
		panel.MouseFilter = MouseFilterEnum.Pass;

		var hbox = new HBoxContainer();
		hbox.AddThemeConstantOverride("separation", 12);
		hbox.MouseFilter = MouseFilterEnum.Pass;
		panel.AddChild(hbox);

		var infoVBox = new VBoxContainer();
		infoVBox.SizeFlagsHorizontal = SizeFlags.ExpandFill;
		infoVBox.Alignment = BoxContainer.AlignmentMode.Center;
		infoVBox.AddThemeConstantOverride("separation", 2);
		infoVBox.MouseFilter = MouseFilterEnum.Ignore;
		hbox.AddChild(infoVBox);

		var verLabel = new Label();
		verLabel.Text = $"Version {version.Version}";
		verLabel.AddThemeColorOverride("font_color", UIStyle.ColorGold);
		verLabel.AddThemeFontSizeOverride("font_size", 14);
		verLabel.MouseFilter = MouseFilterEnum.Ignore;
		infoVBox.AddChild(verLabel);

		var detailsLabel = new Label();
		string shortHash = version.ManifestHash.Length >= 12 ? version.ManifestHash.Substring(0, 12) + "..." : version.ManifestHash;
		detailsLabel.Text = $"BLAKE3: {shortHash}  •  Size: {version.FormattedSize}  •  Modified: {version.LastModified:yyyy-MM-dd HH:mm}";
		detailsLabel.AddThemeColorOverride("font_color", new Color(0.75f, 0.75f, 0.8f));
		detailsLabel.AddThemeFontSizeOverride("font_size", 11);
		detailsLabel.MouseFilter = MouseFilterEnum.Ignore;
		infoVBox.AddChild(detailsLabel);

		var openInEditorBtn = new Button();
		openInEditorBtn.AddThemeConstantOverride("icon_max_width", 20);
		openInEditorBtn.CustomMinimumSize = new Vector2(140, 36);
		openInEditorBtn.SizeFlagsVertical = SizeFlags.ShrinkCenter;
		UIStyle.ApplyButtonText(openInEditorBtn, "✏️ " + TranslationServer.Translate("OPEN IN EDITOR"), 12);
		openInEditorBtn.AddThemeStyleboxOverride("normal", UIStyle.CreateButtonNormal());
		openInEditorBtn.AddThemeStyleboxOverride("hover", UIStyle.CreateButtonHover());
		openInEditorBtn.AddThemeStyleboxOverride("pressed", UIStyle.CreateButtonPressed());
		openInEditorBtn.AddThemeStyleboxOverride("focus", new StyleBoxEmpty());
		openInEditorBtn.TooltipText = TranslationServer.Translate("Copy map to editor workspace. Saves will go to Documents/MapName.");
		openInEditorBtn.FocusMode = FocusModeEnum.None;
		openInEditorBtn.MouseFilter = MouseFilterEnum.Stop;
		openInEditorBtn.MouseEntered += () => UIManager.Instance?.PlayHoverSound();
		openInEditorBtn.Pressed += () =>
		{
			UIManager.Instance?.PlayClickSound();
			OnOpenInEditorPressed(map.Title, version.DirectoryPath);
		};
		hbox.AddChild(openInEditorBtn);

		var exportBtn = new Button();
		exportBtn.AddThemeConstantOverride("icon_max_width", 20);
		exportBtn.CustomMinimumSize = new Vector2(140, 36);
		exportBtn.SizeFlagsVertical = SizeFlags.ShrinkCenter;
		UIStyle.ApplyButtonText(exportBtn, "📦 " + TranslationServer.Translate("EXPORT MAP"), 12);
		exportBtn.AddThemeStyleboxOverride("normal", UIStyle.CreateButtonNormal());
		exportBtn.AddThemeStyleboxOverride("hover", UIStyle.CreateButtonHover());
		exportBtn.AddThemeStyleboxOverride("pressed", UIStyle.CreateButtonPressed());
		exportBtn.AddThemeStyleboxOverride("focus", new StyleBoxEmpty());
		exportBtn.TooltipText = TranslationServer.Translate("Export this version as a standalone .rmap package");
		exportBtn.FocusMode = FocusModeEnum.None;
		exportBtn.MouseFilter = MouseFilterEnum.Stop;
		exportBtn.MouseEntered += () => UIManager.Instance?.PlayHoverSound();
		exportBtn.Pressed += () =>
		{
			UIManager.Instance?.PlayClickSound();
			OnExportMapVersionPressed(map, version, exportBtn);
		};
		hbox.AddChild(exportBtn);

		var deleteBtn = new Button();
		deleteBtn.AddThemeConstantOverride("icon_max_width", 24);
		deleteBtn.CustomMinimumSize = new Vector2(36, 36);
		deleteBtn.SizeFlagsVertical = SizeFlags.ShrinkCenter;
		deleteBtn.Text = "✕";
		deleteBtn.AddThemeColorOverride("font_color", new Color(1.0f, 0.4f, 0.4f));
		deleteBtn.AddThemeStyleboxOverride("normal", CreateDeleteButtonStyle(false));
		deleteBtn.AddThemeStyleboxOverride("hover", CreateDeleteButtonStyle(true));
		deleteBtn.AddThemeStyleboxOverride("pressed", CreateDeleteButtonStyle(false));
		deleteBtn.TooltipText = TranslationServer.Translate("Delete version from CAS");
		deleteBtn.FocusMode = FocusModeEnum.None;
		deleteBtn.MouseFilter = MouseFilterEnum.Stop;
		deleteBtn.MouseEntered += () => UIManager.Instance?.PlayHoverSound();

		deleteBtn.Pressed += () =>
		{
			UIManager.Instance?.PlayClickSound();
			string confirmMsg = string.Format(
				TranslationServer.Translate("Are you sure you want to delete version {0} of {1} from storage?"),
				version.Version,
				map.Title
			);

			UIManager.Instance.ShowConfirmationDialog(
				confirmMsg,
				onConfirm: () =>
				{
					bool deleted = _mapStorageService.DeleteMapVersion(map.Title, version.Version, version.ManifestHash);
					if (deleted)
					{
						RefreshDownloadedMaps();
					}
				},
				confirmText: "DELETE",
				cancelText: "CANCEL"
			);
		};

		hbox.AddChild(deleteBtn);
		return panel;
	}

	private void OnOpenInEditorPressed(string mapTitle, string casVersionDirectory)
	{
		if (string.IsNullOrEmpty(casVersionDirectory) || !Directory.Exists(casVersionDirectory))
		{
			return;
		}

		string sanitizedTitle = string.IsNullOrWhiteSpace(mapTitle) ? "MyMap" : mapTitle.Trim();
		string defaultSaveFolder = Path.Combine(OS.GetSystemDir(OS.SystemDir.Documents), sanitizedTitle);

		MapEditorHUD.RequestOpenFromCas(casVersionDirectory, defaultSaveFolder);
		UIManager.Instance?.TransitionTo(GameScreen.MapEditorHUD);
	}

	private async void CheckForServerUpdatesAsync(DownloadedMapInfo map)
	{
		var localVersions = map.Versions.Select(v => v.Version).ToList();
		var (hasNewer, newerVer, mapId) = await _mapStorageService.CheckForNewerVersionAsync(map.Title, localVersions);

		if (hasNewer && !string.IsNullOrEmpty(newerVer) && _selectedMap != null && string.Equals(_selectedMap.Title, map.Title, StringComparison.OrdinalIgnoreCase))
		{
			map.HasServerUpdate = true;
			map.ServerNewerVersion = newerVer;
			map.ServerMapId = mapId ?? map.Title;

			_downloadUpdateButton.Visible = true;
			UIStyle.ApplyButtonText(_downloadUpdateButton, $"⚡ {TranslationServer.Translate("DOWNLOAD UPDATED VERSION")} (v{newerVer})", 14);
		}
	}

	private async void OnDownloadUpdatePressed()
	{
		if (_selectedMap == null || string.IsNullOrEmpty(_selectedMap.ServerMapId))
		{
			return;
		}

		_downloadUpdateButton.Disabled = true;
		UIStyle.ApplyButtonText(_downloadUpdateButton, TranslationServer.Translate("DOWNLOADING UPDATE..."), 14);

		string mapId = _selectedMap.ServerMapId;
		bool success = await _mapStorageService.DownloadUpdatedVersionAsync(
			mapId,
			progressCallback: p =>
			{
				UIStyle.ApplyButtonText(_downloadUpdateButton, $"{(int)(p * 100)}% {TranslationServer.Translate("DOWNLOADING UPDATE...")}", 14);
			}
		);

		_downloadUpdateButton.Disabled = false;

		if (success)
		{
			RefreshDownloadedMaps();
		}
		else
		{
			UIStyle.ApplyButtonText(_downloadUpdateButton, "❌ " + TranslationServer.Translate("Update Failed"), 14);
		}
	}

	private void OnExportMapVersionPressed(DownloadedMapInfo map, DownloadedMapVersionInfo version, Button exportBtn)
	{
		if (map == null || version == null)
		{
			return;
		}

		string mapTitle = map.Title;
		string cleanMapName = string.Join("_", mapTitle.Split(Path.GetInvalidFileNameChars(), StringSplitOptions.RemoveEmptyEntries)).Trim();
		if (string.IsNullOrEmpty(cleanMapName) || cleanMapName.Equals("Untitled Map", StringComparison.OrdinalIgnoreCase))
		{
			cleanMapName = "MapExport";
		}

		string mapVersion = version.Version;
		string cleanMapVersion = string.Join("_", mapVersion.Split(Path.GetInvalidFileNameChars(), StringSplitOptions.RemoveEmptyEntries)).Trim();
		if (string.IsNullOrEmpty(cleanMapVersion))
		{
			cleanMapVersion = "1.0.0";
		}

		string manifestBlake3 = version.ManifestHash;
		if (string.IsNullOrEmpty(manifestBlake3) && File.Exists(version.ManifestFilePath))
		{
			manifestBlake3 = RealmMetadataHelper.ComputeBlake3(version.ManifestFilePath);
		}
		if (string.IsNullOrEmpty(manifestBlake3) && !string.IsNullOrEmpty(version.DirectoryPath))
		{
			string manifestPath = Path.Combine(version.DirectoryPath, "manifest.json");
			if (File.Exists(manifestPath))
			{
				manifestBlake3 = RealmMetadataHelper.ComputeBlake3(manifestPath);
			}
		}

		string normHash = !string.IsNullOrEmpty(manifestBlake3) ? ContentAddressableStorage.NormalizeBlake3Hash(manifestBlake3) : string.Empty;
		string shortHash = normHash.Length >= 4 ? normHash.Substring(0, 4) : (normHash.Length > 0 ? normHash : "0000");

		string defaultFileName = $"{cleanMapName}_{cleanMapVersion}_{shortHash}.rmap";

		var err = DisplayServer.FileDialogShow(
			TranslationServer.Translate("Export Map as .rmap"),
			OS.GetSystemDir(OS.SystemDir.Documents),
			defaultFileName,
			false,
			DisplayServer.FileDialogMode.SaveFile,
			new[] { "*.rmap ; Realm Map Package (*.rmap)" },
			Callable.From((bool status, string[] selectedPaths, int selectedFilterIndex) =>
			{
				if (status && selectedPaths.Length > 0)
				{
					string destinationPath = selectedPaths[0];
					if (!destinationPath.EndsWith(".rmap", StringComparison.OrdinalIgnoreCase))
					{
						destinationPath += ".rmap";
					}

					_ = ExportMapToFileAsync(map, version, destinationPath, exportBtn);
				}
			})
		);

		if (err != Error.Ok)
		{
			string fallbackPath = Path.Combine(OS.GetSystemDir(OS.SystemDir.Documents), defaultFileName);
			_ = ExportMapToFileAsync(map, version, fallbackPath, exportBtn);
		}
	}

	private async Task ExportMapToFileAsync(DownloadedMapInfo map, DownloadedMapVersionInfo version, string destinationPath, Button exportBtn)
	{
		if (Directory.Exists(destinationPath) && map != null && version != null)
		{
			string mapTitle = map.Title;
			string cleanMapName = string.Join("_", mapTitle.Split(Path.GetInvalidFileNameChars(), StringSplitOptions.RemoveEmptyEntries)).Trim();
			if (string.IsNullOrEmpty(cleanMapName) || cleanMapName.Equals("Untitled Map", StringComparison.OrdinalIgnoreCase))
			{
				cleanMapName = "MapExport";
			}

			string mapVersion = version.Version;
			string cleanMapVersion = string.Join("_", mapVersion.Split(Path.GetInvalidFileNameChars(), StringSplitOptions.RemoveEmptyEntries)).Trim();
			if (string.IsNullOrEmpty(cleanMapVersion))
			{
				cleanMapVersion = "1.0.0";
			}

			string manifestBlake3 = version.ManifestHash;
			if (string.IsNullOrEmpty(manifestBlake3) && File.Exists(version.ManifestFilePath))
			{
				manifestBlake3 = RealmMetadataHelper.ComputeBlake3(version.ManifestFilePath);
			}
			if (string.IsNullOrEmpty(manifestBlake3) && !string.IsNullOrEmpty(version.DirectoryPath))
			{
				string manifestPath = Path.Combine(version.DirectoryPath, "manifest.json");
				if (File.Exists(manifestPath))
				{
					manifestBlake3 = RealmMetadataHelper.ComputeBlake3(manifestPath);
				}
			}

			string normHash = !string.IsNullOrEmpty(manifestBlake3) ? ContentAddressableStorage.NormalizeBlake3Hash(manifestBlake3) : string.Empty;
			string shortHash = normHash.Length >= 4 ? normHash.Substring(0, 4) : (normHash.Length > 0 ? normHash : "0000");

			destinationPath = Path.Combine(destinationPath, $"{cleanMapName}_{cleanMapVersion}_{shortHash}.rmap");
		}

		if (GodotObject.IsInstanceValid(exportBtn))
		{
			exportBtn.Disabled = true;
			UIStyle.ApplyButtonText(exportBtn, TranslationServer.Translate("Exporting..."), 12);
		}

		MapWorkspaceService.EnsureLicenseFile(version.DirectoryPath);
		bool success = await _mapStorageService.ExportMapAsync(version.DirectoryPath, destinationPath, compressionLevel: 1);

		if (GodotObject.IsInstanceValid(exportBtn))
		{
			exportBtn.Disabled = false;
			UIStyle.ApplyButtonText(exportBtn, "📦 " + TranslationServer.Translate("EXPORT MAP"), 12);
		}

		if (success)
		{
			UIManager.Instance.ShowConfirmationDialog(
				string.Format(TranslationServer.Translate("Map exported successfully to {0}."), destinationPath),
				() => { },
				confirmText: "OK",
				showCancel: false
			);
		}
	}

	private void OnImportMapPressed()
	{
		UIManager.Instance?.PromptAndImportMapArchive((mapTitle, mapVersion) =>
		{
			RefreshDownloadedMaps();
			if (!string.IsNullOrEmpty(mapTitle))
			{
				var imported = _allDownloadedMaps.FirstOrDefault(m => string.Equals(m.Title, mapTitle, StringComparison.OrdinalIgnoreCase));
				if (imported != null)
				{
					SelectMap(imported);
					RenderMapList();
				}
			}
		});
	}

	private static StyleBoxFlat CreateHeaderPanelStyle()
	{
		var style = new StyleBoxFlat();
		style.BgColor = new Color(0.06f, 0.07f, 0.09f, 0.95f);
		style.BorderColor = UIStyle.ColorBronze;
		style.BorderWidthBottom = 2;
		style.ContentMarginLeft = 25;
		style.ContentMarginRight = 25;
		style.ContentMarginTop = 12;
		style.ContentMarginBottom = 12;
		return style;
	}

	private static StyleBoxFlat CreateCardStyle(bool isSelected, bool isHover)
	{
		var style = new StyleBoxFlat();
		style.BgColor = isSelected
			? new Color(0.16f, 0.18f, 0.24f, 0.95f)
			: (isHover ? new Color(0.12f, 0.13f, 0.17f, 0.90f) : new Color(0.09f, 0.10f, 0.13f, 0.85f));
		style.BorderColor = isSelected ? UIStyle.ColorGold : (isHover ? UIStyle.ColorCyanGlowDim : new Color(0.25f, 0.25f, 0.30f, 0.7f));
		style.SetBorderWidthAll(1);
		if (isSelected)
		{
			style.BorderWidthLeft = 4;
		}
		style.CornerRadiusTopLeft = 4;
		style.CornerRadiusTopRight = 4;
		style.CornerRadiusBottomLeft = 4;
		style.CornerRadiusBottomRight = 4;
		style.ContentMarginLeft = 10;
		style.ContentMarginRight = 10;
		style.ContentMarginTop = 6;
		style.ContentMarginBottom = 6;
		return style;
	}

	private static StyleBoxFlat CreateVersionRowStyle()
	{
		var style = new StyleBoxFlat();
		style.BgColor = new Color(0.10f, 0.11f, 0.15f, 0.90f);
		style.BorderColor = new Color(0.28f, 0.28f, 0.35f, 0.7f);
		style.SetBorderWidthAll(1);
		style.CornerRadiusTopLeft = 4;
		style.CornerRadiusTopRight = 4;
		style.CornerRadiusBottomLeft = 4;
		style.CornerRadiusBottomRight = 4;
		style.ContentMarginLeft = 14;
		style.ContentMarginRight = 14;
		style.ContentMarginTop = 8;
		style.ContentMarginBottom = 8;
		return style;
	}

	private static StyleBoxFlat CreateDeleteButtonStyle(bool isHover)
	{
		var style = new StyleBoxFlat();
		style.BgColor = isHover ? new Color(0.4f, 0.12f, 0.12f, 0.9f) : new Color(0.25f, 0.08f, 0.08f, 0.8f);
		style.BorderColor = isHover ? new Color(0.9f, 0.3f, 0.3f, 1.0f) : new Color(0.5f, 0.2f, 0.2f, 0.8f);
		style.SetBorderWidthAll(1);
		style.CornerRadiusTopLeft = 4;
		style.CornerRadiusTopRight = 4;
		style.CornerRadiusBottomLeft = 4;
		style.CornerRadiusBottomRight = 4;
		return style;
	}
}
