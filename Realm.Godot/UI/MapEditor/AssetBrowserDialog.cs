using Godot;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json.Nodes;
using Realm.Ecs.Services;
using Realm.Godot.Services.ModelOptimization;

public partial class AssetBrowserDialog : FloatingDialogBase
{
	private const float CellWidth = 130.0f;
	private const float CellHeight = 150.0f;
	private const float SpacingX = 10.0f;
	private const float SpacingY = 10.0f;
	private const float GridPadding = 10.0f;

	private HBoxContainer _folderChipsContainer;
	private Button _btnAddFolder;
	private Button _btnRescanAll;
	private OptionButton _optDirectoryFilter;
	private LineEdit _txtSearch;
	private Label _lblResultsCount;
	private Label _lblFilterExtensions;

	private ScrollContainer _scrollContainer;
	private Control _virtualGridContent;
	private Label _lblEmptyState;

	private PanelContainer _bottomDetailsPanel;
	private TextureRect _bottomThumbnail;
	private Label _lblSelectedFileName;
	private Label _lblSelectedPath;
	private Label _lblSelectedSize;
	private Button _btnAudioPlayPause;
	private LineEdit _txtTagsEdit;
	private Button _btnEditTags;
	private LineEdit _txtAssetTypeEdit;
	private Button _btnEditAssetType;

	private readonly AudioStreamPlayer _audioPlayer;

	private readonly List<AssetGridCell> _cellPool = new();
	private readonly List<IndexedAsset> _matchingAssets = new();

	private static bool _hasAutoRescannedOnFirstOpen = false;
	private HashSet<string> _allowedExtensions = new(StringComparer.OrdinalIgnoreCase);
	private bool _requireRealmMetadata = false;
	private string? _selectedAssetTypeFilter;
	private OptionButton _optAssetTypeFilter;
	private CheckBox _chkHasPlayerColorMask;
	private static string? _selectedDirectoryFilter;
	private static string? _selectedMapNameFilter;
	private static string? _selectedMapVersionFilter;
	private IndexedAsset? _selectedAsset;
	private Action<string>? _onAssetSelectedCallback;
	private Action<string, string?>? _onAssetSelectedWithPreferredNameCallback;

	public AssetBrowserDialog(MapEditorHUD hud)
		: base(hud, TranslationServer.Translate("Asset Browser"), new Vector2(780, 640))
	{
		SetUncompressedPanelTexture("res://Assets/UI/map_editor_assets_importer.png", 30, 45, 70, 70);

		_audioPlayer = new AudioStreamPlayer();
		_audioPlayer.Finished += OnAudioFinished;
		AddChild(_audioPlayer);

		BuildControls();
		AssetThumbnailProvider.ThumbnailGenerated += OnThumbnailGenerated;
		AssetIndexService.Instance.DirectoryIndexingStateChanged += OnDirectoryIndexingStateChanged;
		AssetIndexService.Instance.DirectoryScanCompleted += OnDirectoryScanCompleted;
	}

	public override void _ExitTree()
	{
		StopAudio();
		AssetThumbnailProvider.ThumbnailGenerated -= OnThumbnailGenerated;
		AssetIndexService.Instance.DirectoryIndexingStateChanged -= OnDirectoryIndexingStateChanged;
		AssetIndexService.Instance.DirectoryScanCompleted -= OnDirectoryScanCompleted;
		base._ExitTree();
	}

	public override void CloseDialog()
	{
		StopAudio();
		base.CloseDialog();
	}

	private void OnDirectoryIndexingStateChanged(string dirPath, bool isIndexing)
	{
		Callable.From(() => RefreshFolderChips()).CallDeferred();
	}

	private void OnDirectoryScanCompleted(string dirPath)
	{
		Callable.From(() =>
		{
			RefreshFolderChips();
			RefreshSearchResults();
		}).CallDeferred();
	}

	private void OnThumbnailGenerated(string filePath, Texture2D texture)
	{
		if (_selectedAsset != null)
		{
			string selectedNorm = AssetThumbnailProvider.NormalizePath(_selectedAsset.FilePath);
			string eventNorm = AssetThumbnailProvider.NormalizePath(filePath);
			if (string.Equals(selectedNorm, eventNorm, StringComparison.OrdinalIgnoreCase))
			{
				_bottomThumbnail.Texture = texture;
			}
		}
	}

	private void BuildControls()
	{
		var topFoldersSection = new VBoxContainer();
		topFoldersSection.AddThemeConstantOverride("separation", 6);
		BodyContainer.AddChild(topFoldersSection);

		var folderHeaderRow = new HBoxContainer();
		folderHeaderRow.AddThemeConstantOverride("separation", 8);

		var lblFoldersTitle = new Label();
		lblFoldersTitle.Text = "📁 " + TranslationServer.Translate("Indexed Folders:");
		lblFoldersTitle.AddThemeFontSizeOverride("font_size", 11);
		lblFoldersTitle.AddThemeColorOverride("font_color", UIStyle.ColorGold);
		folderHeaderRow.AddChild(lblFoldersTitle);

		_btnAddFolder = new Button();
		_btnAddFolder.Set("icon_max_width", 0);
		_btnAddFolder.Text = "+ " + TranslationServer.Translate("Add Folder");
		_btnAddFolder.AddThemeFontSizeOverride("font_size", 11);
		_btnAddFolder.CustomMinimumSize = new Vector2(100, 24);
		_btnAddFolder.FocusMode = FocusModeEnum.None;
		_btnAddFolder.TooltipText = TranslationServer.Translate("Add a new local folder to the asset index");
		_btnAddFolder.Pressed += OnAddFolderPressed;
		folderHeaderRow.AddChild(_btnAddFolder);

		_btnRescanAll = new Button();
		_btnRescanAll.Set("icon_max_width", 0);
		_btnRescanAll.Text = "🔄 " + TranslationServer.Translate("Rescan");
		_btnRescanAll.AddThemeFontSizeOverride("font_size", 11);
		_btnRescanAll.CustomMinimumSize = new Vector2(80, 24);
		_btnRescanAll.FocusMode = FocusModeEnum.None;
		_btnRescanAll.TooltipText = TranslationServer.Translate("Rescan all indexed folders for new or modified files");
		_btnRescanAll.Pressed += OnRescanAllPressed;
		folderHeaderRow.AddChild(_btnRescanAll);

		topFoldersSection.AddChild(folderHeaderRow);

		var folderScroll = new ScrollContainer();
		folderScroll.CustomMinimumSize = new Vector2(0, 32);
		folderScroll.VerticalScrollMode = ScrollContainer.ScrollMode.Disabled;
		folderScroll.HorizontalScrollMode = ScrollContainer.ScrollMode.Auto;

		_folderChipsContainer = new HBoxContainer();
		_folderChipsContainer.AddThemeConstantOverride("separation", 6);
		folderScroll.AddChild(_folderChipsContainer);
		topFoldersSection.AddChild(folderScroll);

		var directoryRow = new HBoxContainer();
		directoryRow.AddThemeConstantOverride("separation", 8);
		BodyContainer.AddChild(directoryRow);

		_optDirectoryFilter = new OptionButton();
		_optDirectoryFilter.SizeFlagsHorizontal = SizeFlags.ExpandFill;
		_optDirectoryFilter.CustomMinimumSize = new Vector2(0, 24);
		_optDirectoryFilter.AddThemeFontSizeOverride("font_size", 11);
		_optDirectoryFilter.FocusMode = FocusModeEnum.None;
		_optDirectoryFilter.ItemSelected += OnDirectoryFilterChanged;
		directoryRow.AddChild(_optDirectoryFilter);

		var filterRow = new HBoxContainer();
		filterRow.AddThemeConstantOverride("separation", 8);
		BodyContainer.AddChild(filterRow);

		_txtSearch = new LineEdit();
		_txtSearch.PlaceholderText = TranslationServer.Translate("🔍 Search tags (e.g. grass, rock) or filename...");
		_txtSearch.SizeFlagsHorizontal = SizeFlags.ExpandFill;
		_txtSearch.AddThemeFontSizeOverride("font_size", 11);
		_txtSearch.TextChanged += (_) => RefreshSearchResults();
		filterRow.AddChild(_txtSearch);

		_optAssetTypeFilter = new OptionButton();
		_optAssetTypeFilter.CustomMinimumSize = new Vector2(150, 24);
		_optAssetTypeFilter.AddThemeFontSizeOverride("font_size", 11);
		_optAssetTypeFilter.FocusMode = FocusModeEnum.None;
		_optAssetTypeFilter.ItemSelected += OnAssetTypeFilterChanged;
		filterRow.AddChild(_optAssetTypeFilter);

		_chkHasPlayerColorMask = new CheckBox();
		_chkHasPlayerColorMask.Set("icon_max_width", 0);
		_chkHasPlayerColorMask.Text = TranslationServer.Translate("Has Player Color Mask");
		_chkHasPlayerColorMask.AddThemeFontSizeOverride("font_size", 11);
		_chkHasPlayerColorMask.FocusMode = FocusModeEnum.None;
		_chkHasPlayerColorMask.TooltipText = TranslationServer.Translate("Filter assets that have a player color (chroma key) mask in metadata");
		_chkHasPlayerColorMask.Toggled += (_) => RefreshSearchResults();
		filterRow.AddChild(_chkHasPlayerColorMask);

		var infoBar = new HBoxContainer();
		infoBar.AddThemeConstantOverride("separation", 12);
		BodyContainer.AddChild(infoBar);

		_lblResultsCount = new Label();
		_lblResultsCount.Text = "0 " + TranslationServer.Translate("items found");
		_lblResultsCount.AddThemeFontSizeOverride("font_size", 10);
		_lblResultsCount.AddThemeColorOverride("font_color", UIStyle.ColorGoldDull);
		infoBar.AddChild(_lblResultsCount);

		_lblFilterExtensions = new Label();
		_lblFilterExtensions.AddThemeFontSizeOverride("font_size", 10);
		_lblFilterExtensions.AddThemeColorOverride("font_color", UIStyle.ColorCyanGlowDim);
		_lblFilterExtensions.SizeFlagsHorizontal = SizeFlags.ExpandFill;
		infoBar.AddChild(_lblFilterExtensions);

		var gridPanel = new PanelContainer();
		gridPanel.CustomMinimumSize = new Vector2(0, 320);
		gridPanel.SizeFlagsVertical = SizeFlags.ExpandFill;
		gridPanel.AddThemeStyleboxOverride("panel", UIStyle.CreateLightInnerPanel());
		BodyContainer.AddChild(gridPanel);

		_scrollContainer = new ScrollContainer();
		_scrollContainer.SizeFlagsHorizontal = SizeFlags.ExpandFill;
		_scrollContainer.SizeFlagsVertical = SizeFlags.ExpandFill;
		_scrollContainer.HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled;
		_scrollContainer.VerticalScrollMode = ScrollContainer.ScrollMode.Auto;
		gridPanel.AddChild(_scrollContainer);

		_virtualGridContent = new Control();
		_virtualGridContent.SizeFlagsHorizontal = SizeFlags.ExpandFill;
		_virtualGridContent.MouseFilter = MouseFilterEnum.Pass;
		_scrollContainer.AddChild(_virtualGridContent);

		_lblEmptyState = new Label();
		_lblEmptyState.Text = TranslationServer.Translate("No assets found matching the search criteria.\nClick '+ Add Folder' above to index directories.");
		_lblEmptyState.HorizontalAlignment = HorizontalAlignment.Center;
		_lblEmptyState.VerticalAlignment = VerticalAlignment.Center;
		_lblEmptyState.SizeFlagsHorizontal = SizeFlags.ExpandFill;
		_lblEmptyState.SizeFlagsVertical = SizeFlags.ExpandFill;
		_lblEmptyState.AddThemeColorOverride("font_color", UIStyle.ColorGoldDull);
		_lblEmptyState.AddThemeFontSizeOverride("font_size", 12);
		_lblEmptyState.Visible = false;
		gridPanel.AddChild(_lblEmptyState);

		_scrollContainer.GetVScrollBar().ValueChanged += (_) => UpdateVisibleGridCells();
		_scrollContainer.Resized += () =>
		{
			UpdateVirtualGridSize();
			UpdateVisibleGridCells();
		};

		_bottomDetailsPanel = new PanelContainer();
		_bottomDetailsPanel.CustomMinimumSize = new Vector2(0, 60);
		var detailStyle = new StyleBoxFlat();
		detailStyle.BgColor = new Color(0.08f, 0.09f, 0.11f, 0.9f);
		detailStyle.BorderColor = new Color(0.25f, 0.23f, 0.20f, 0.8f);
		detailStyle.SetBorderWidthAll(1);
		detailStyle.CornerRadiusTopLeft = 4;
		detailStyle.CornerRadiusTopRight = 4;
		detailStyle.CornerRadiusBottomLeft = 4;
		detailStyle.CornerRadiusBottomRight = 4;
		detailStyle.ContentMarginLeft = 8;
		detailStyle.ContentMarginRight = 8;
		detailStyle.ContentMarginTop = 6;
		detailStyle.ContentMarginBottom = 6;
		_bottomDetailsPanel.AddThemeStyleboxOverride("panel", detailStyle);
		BodyContainer.AddChild(_bottomDetailsPanel);

		var bottomHBox = new HBoxContainer();
		bottomHBox.AddThemeConstantOverride("separation", 10);
		_bottomDetailsPanel.AddChild(bottomHBox);

		_bottomThumbnail = new TextureRect();
		_bottomThumbnail.CustomMinimumSize = new Vector2(48, 48);
		_bottomThumbnail.ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize;
		_bottomThumbnail.StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered;
		bottomHBox.AddChild(_bottomThumbnail);

		var bottomInfoVBox = new VBoxContainer();
		bottomInfoVBox.SizeFlagsHorizontal = SizeFlags.ExpandFill;
		bottomInfoVBox.AddThemeConstantOverride("separation", 2);
		bottomHBox.AddChild(bottomInfoVBox);

		var nameRow = new HBoxContainer();
		nameRow.AddThemeConstantOverride("separation", 8);

		_lblSelectedFileName = new Label();
		_lblSelectedFileName.Text = TranslationServer.Translate("No asset selected");
		_lblSelectedFileName.AddThemeFontSizeOverride("font_size", 11);
		_lblSelectedFileName.AddThemeColorOverride("font_color", UIStyle.ColorGold);
		nameRow.AddChild(_lblSelectedFileName);

		_lblSelectedSize = new Label();
		_lblSelectedSize.AddThemeFontSizeOverride("font_size", 10);
		_lblSelectedSize.AddThemeColorOverride("font_color", UIStyle.ColorGoldDull);
		nameRow.AddChild(_lblSelectedSize);

		_btnAudioPlayPause = new Button();
		_btnAudioPlayPause.Set("icon_max_width", 0);
		_btnAudioPlayPause.Text = "▶ " + TranslationServer.Translate("Play");
		_btnAudioPlayPause.AddThemeFontSizeOverride("font_size", 10);
		_btnAudioPlayPause.CustomMinimumSize = new Vector2(70, 20);
		_btnAudioPlayPause.FocusMode = FocusModeEnum.None;
		_btnAudioPlayPause.TooltipText = TranslationServer.Translate("Play audio");
		_btnAudioPlayPause.Pressed += OnAudioPlayPausePressed;
		_btnAudioPlayPause.Visible = false;
		nameRow.AddChild(_btnAudioPlayPause);

		bottomInfoVBox.AddChild(nameRow);

		_lblSelectedPath = new Label();
		_lblSelectedPath.TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis;
		_lblSelectedPath.AddThemeFontSizeOverride("font_size", 9);
		_lblSelectedPath.AddThemeColorOverride("font_color", UIStyle.ColorGoldDull);
		bottomInfoVBox.AddChild(_lblSelectedPath);

		var tagsRow = new HBoxContainer();
		tagsRow.AddThemeConstantOverride("separation", 6);

		var lblTagsTitle = new Label();
		lblTagsTitle.Text = TranslationServer.Translate("Tags:");
		lblTagsTitle.AddThemeFontSizeOverride("font_size", 10);
		lblTagsTitle.AddThemeColorOverride("font_color", UIStyle.ColorGoldDull);
		tagsRow.AddChild(lblTagsTitle);

		_txtTagsEdit = new LineEdit();
		_txtTagsEdit.PlaceholderText = TranslationServer.Translate("No tags");
		_txtTagsEdit.SizeFlagsHorizontal = SizeFlags.ExpandFill;
		_txtTagsEdit.AddThemeFontSizeOverride("font_size", 10);
		_txtTagsEdit.Editable = false;
		tagsRow.AddChild(_txtTagsEdit);

		_btnEditTags = new Button();
		_btnEditTags.Set("icon_max_width", 0);
		_btnEditTags.Text = "✏️";
		_btnEditTags.AddThemeFontSizeOverride("font_size", 11);
		_btnEditTags.CustomMinimumSize = new Vector2(28, 22);
		_btnEditTags.FocusMode = FocusModeEnum.None;
		_btnEditTags.TooltipText = TranslationServer.Translate("Edit tags");
		_btnEditTags.Pressed += OnEditTagsPressed;
		tagsRow.AddChild(_btnEditTags);

		bottomInfoVBox.AddChild(tagsRow);

		var assetTypeRow = new HBoxContainer();
		assetTypeRow.AddThemeConstantOverride("separation", 6);

		var lblAssetTypeTitle = new Label();
		lblAssetTypeTitle.Text = TranslationServer.Translate("Asset Type:");
		lblAssetTypeTitle.AddThemeFontSizeOverride("font_size", 10);
		lblAssetTypeTitle.AddThemeColorOverride("font_color", UIStyle.ColorGoldDull);
		assetTypeRow.AddChild(lblAssetTypeTitle);

		_txtAssetTypeEdit = new LineEdit();
		_txtAssetTypeEdit.PlaceholderText = TranslationServer.Translate("None");
		_txtAssetTypeEdit.SizeFlagsHorizontal = SizeFlags.ExpandFill;
		_txtAssetTypeEdit.AddThemeFontSizeOverride("font_size", 10);
		_txtAssetTypeEdit.Editable = false;
		assetTypeRow.AddChild(_txtAssetTypeEdit);

		_btnEditAssetType = new Button();
		_btnEditAssetType.Set("icon_max_width", 0);
		_btnEditAssetType.Text = "✏️";
		_btnEditAssetType.AddThemeFontSizeOverride("font_size", 11);
		_btnEditAssetType.CustomMinimumSize = new Vector2(28, 22);
		_btnEditAssetType.FocusMode = FocusModeEnum.None;
		_btnEditAssetType.TooltipText = TranslationServer.Translate("Edit asset type");
		_btnEditAssetType.Pressed += OnEditAssetTypePressed;
		assetTypeRow.AddChild(_btnEditAssetType);

		bottomInfoVBox.AddChild(assetTypeRow);

		ApplyButton.Text = TranslationServer.Translate("Select");
	}

	public void OpenForImport(string titleText, IEnumerable<string> allowedExtensions, Action<string> onAssetSelected, bool requireRealmMetadata = false, string? requiredAssetType = null)
	{
		_onAssetSelectedCallback = onAssetSelected;
		_onAssetSelectedWithPreferredNameCallback = null;
		_selectedAsset = null;
		_txtSearch.Text = string.Empty;
		if (_chkHasPlayerColorMask != null)
		{
			_chkHasPlayerColorMask.ButtonPressed = false;
		}
		_requireRealmMetadata = requireRealmMetadata;
		_selectedAssetTypeFilter = requiredAssetType;

		TitleLabel.Text = string.IsNullOrWhiteSpace(titleText)
			? TranslationServer.Translate("Asset Browser")
			: TranslationServer.Translate(titleText);

		_allowedExtensions = (allowedExtensions ?? Array.Empty<string>())
			.Select(e => e.Trim().ToLowerInvariant())
			.Select(e => e.StartsWith(".") ? e : "." + e)
			.ToHashSet(StringComparer.OrdinalIgnoreCase);

		if (_allowedExtensions.Contains(".rmesh"))
		{
			requireRealmMetadata = true;
		}
		_requireRealmMetadata = requireRealmMetadata;

		if (_allowedExtensions.Count > 0)
		{
			_lblFilterExtensions.Text = $"{TranslationServer.Translate("Extensions")}: {string.Join(", ", _allowedExtensions)}";
			_lblFilterExtensions.Visible = true;
		}
		else
		{
			_lblFilterExtensions.Visible = false;
		}

		if (!_hasAutoRescannedOnFirstOpen || AssetIndexService.Instance.GetAssetCount() == 0)
		{
			_hasAutoRescannedOnFirstOpen = true;
			AssetIndexService.Instance.RescanAllDirectories();
		}

		OpenDialog();

		RefreshFolderChips();
		RefreshAssetTypeFilterOptions();
		RefreshSearchResults();
		UpdateSelectedAssetDisplay();

		Callable.From(() =>
		{
			UpdateVirtualGridSize();
			UpdateVisibleGridCells();
		}).CallDeferred();
	}

	public void OpenForImport(string titleText, IEnumerable<string> allowedExtensions, Action<string, string?> onAssetSelected, bool requireRealmMetadata = false, string? requiredAssetType = null)
	{
		_onAssetSelectedCallback = null;
		_onAssetSelectedWithPreferredNameCallback = onAssetSelected;
		_selectedAsset = null;
		_txtSearch.Text = string.Empty;
		if (_chkHasPlayerColorMask != null)
		{
			_chkHasPlayerColorMask.ButtonPressed = false;
		}
		_requireRealmMetadata = requireRealmMetadata;
		_selectedAssetTypeFilter = requiredAssetType;

		TitleLabel.Text = string.IsNullOrWhiteSpace(titleText)
			? TranslationServer.Translate("Asset Browser")
			: TranslationServer.Translate(titleText);

		_allowedExtensions = (allowedExtensions ?? Array.Empty<string>())
			.Select(e => e.Trim().ToLowerInvariant())
			.Select(e => e.StartsWith(".") ? e : "." + e)
			.ToHashSet(StringComparer.OrdinalIgnoreCase);

		if (_allowedExtensions.Contains(".rmesh"))
		{
			requireRealmMetadata = true;
		}
		_requireRealmMetadata = requireRealmMetadata;

		if (_allowedExtensions.Count > 0)
		{
			_lblFilterExtensions.Text = $"{TranslationServer.Translate("Extensions")}: {string.Join(", ", _allowedExtensions)}";
			_lblFilterExtensions.Visible = true;
		}
		else
		{
			_lblFilterExtensions.Visible = false;
		}

		if (!_hasAutoRescannedOnFirstOpen || AssetIndexService.Instance.GetAssetCount() == 0)
		{
			_hasAutoRescannedOnFirstOpen = true;
			AssetIndexService.Instance.RescanAllDirectories();
		}

		OpenDialog();

		RefreshFolderChips();
		RefreshAssetTypeFilterOptions();
		RefreshSearchResults();
		UpdateSelectedAssetDisplay();

		Callable.From(() =>
		{
			UpdateVirtualGridSize();
			UpdateVisibleGridCells();
		}).CallDeferred();
	}

	private void RefreshFolderChips()
	{
		foreach (var child in _folderChipsContainer.GetChildren())
		{
			child.QueueFree();
		}

		_optDirectoryFilter.Clear();
		_optDirectoryFilter.AddItem(TranslationServer.Translate("All Indexed Folders & Maps"), 0);
		_optDirectoryFilter.SetItemMetadata(0, "all");

		int currentItemIndex = 1;
		int selectedIndex = 0;

		selectedIndex = PopulateMapGroupsFilter(ref currentItemIndex, selectedIndex);
		selectedIndex = PopulateIndexedDirsFilter(ref currentItemIndex, selectedIndex);

		if (selectedIndex == 0 && (_selectedDirectoryFilter != null || _selectedMapNameFilter != null))
		{
			_selectedDirectoryFilter = null;
			_selectedMapNameFilter = null;
			_selectedMapVersionFilter = null;
		}

		_optDirectoryFilter.Selected = selectedIndex;
	}

	private int PopulateMapGroupsFilter(ref int currentItemIndex, int selectedIndex)
	{
		var downloadedPackages = AssetIndexService.Instance.GetDownloadedMapPackages();
		var mapGroups = downloadedPackages
			.GroupBy(p => p.MapName, StringComparer.OrdinalIgnoreCase)
			.OrderBy(g => g.Key, StringComparer.OrdinalIgnoreCase);

		foreach (var group in mapGroups)
		{
			string mapName = group.Key;
			var sortedVersions = group.OrderBy(p => p.MapVersion, StringComparer.OrdinalIgnoreCase).ToList();
			sortedVersions.Sort((a, b) => AssetIndexService.CompareVersions(b.MapVersion, a.MapVersion));

			bool isFirst = true;
			foreach (var pkg in sortedVersions)
			{
				string ver = pkg.MapVersion;
				string label = isFirst
					? $"📦 [Map] {mapName} ({ver} - {TranslationServer.Translate("Latest")})"
					: $"📦 [Map] {mapName} ({ver})";
				string metaKey = $"map:{mapName}|{ver}";

				_optDirectoryFilter.AddItem(label, currentItemIndex);
				_optDirectoryFilter.SetItemMetadata(currentItemIndex, metaKey);

				if (IsMapFilterSelected(mapName, ver, isFirst))
				{
					selectedIndex = currentItemIndex;
				}

				currentItemIndex++;
				isFirst = false;
			}
		}

		return selectedIndex;
	}

	private bool IsMapFilterSelected(string mapName, string ver, bool isFirst)
	{
		if (string.IsNullOrEmpty(_selectedMapNameFilter)) return false;
		if (!string.Equals(mapName, _selectedMapNameFilter, StringComparison.OrdinalIgnoreCase)) return false;

		if (string.IsNullOrEmpty(_selectedMapVersionFilter)) return true;
		if (string.Equals(_selectedMapVersionFilter, "latest", StringComparison.OrdinalIgnoreCase) && isFirst) return true;
		if (string.Equals(_selectedMapVersionFilter, ver, StringComparison.OrdinalIgnoreCase)) return true;
		
		return false;
	}

	private int PopulateIndexedDirsFilter(ref int currentItemIndex, int selectedIndex)
	{
		var indexedDirs = AssetIndexService.Instance.GetIndexedDirectories();

		for (int i = 0; i < indexedDirs.Count; i++)
		{
			string dirPath = indexedDirs[i];
			if (string.Equals(dirPath, AssetIndexService.GlobalCasAssetsDirectory, StringComparison.OrdinalIgnoreCase))
			{
				continue;
			}

			int itemIdx = currentItemIndex++;
			selectedIndex = AddIndexedDirItem(dirPath, itemIdx, selectedIndex);
			AddIndexedDirChip(dirPath);
		}

		return selectedIndex;
	}

	private int AddIndexedDirItem(string dirPath, int itemIdx, int selectedIndex)
	{
		string folderName = Path.GetFileName(dirPath.TrimEnd('/', '\\'));
		if (string.IsNullOrEmpty(folderName))
		{
			folderName = dirPath;
		}

		bool isIndexing = AssetIndexService.Instance.IsDirectoryIndexing(dirPath);
		string metaKey = $"dir:{dirPath}";
		_optDirectoryFilter.AddItem(isIndexing ? $"⏳ {folderName} ({TranslationServer.Translate("Indexing...")})" : folderName, itemIdx);
		_optDirectoryFilter.SetItemMetadata(itemIdx, metaKey);

		if (string.IsNullOrEmpty(_selectedMapNameFilter) && !string.IsNullOrEmpty(_selectedDirectoryFilter) && string.Equals(dirPath, _selectedDirectoryFilter, StringComparison.OrdinalIgnoreCase))
		{
			return itemIdx;
		}

		return selectedIndex;
	}

	private void AddIndexedDirChip(string dirPath)
	{
		string folderName = Path.GetFileName(dirPath.TrimEnd('/', '\\'));
		if (string.IsNullOrEmpty(folderName))
		{
			folderName = dirPath;
		}

		bool isIndexing = AssetIndexService.Instance.IsDirectoryIndexing(dirPath);
		var chip = new PanelContainer();
		var chipStyle = new StyleBoxFlat();
		chipStyle.BgColor = isIndexing ? new Color(0.18f, 0.16f, 0.12f, 0.9f) : new Color(0.15f, 0.16f, 0.19f, 0.9f);
		chipStyle.BorderColor = isIndexing ? UIStyle.ColorGold : new Color(0.35f, 0.32f, 0.28f, 0.7f);
		chipStyle.SetBorderWidthAll(1);
		chipStyle.CornerRadiusTopLeft = 3;
		chipStyle.CornerRadiusTopRight = 3;
		chipStyle.CornerRadiusBottomLeft = 3;
		chipStyle.CornerRadiusBottomRight = 3;
		chipStyle.ContentMarginLeft = 6;
		chipStyle.ContentMarginRight = 4;
		chipStyle.ContentMarginTop = 2;
		chipStyle.ContentMarginBottom = 2;
		chip.AddThemeStyleboxOverride("panel", chipStyle);
		chip.TooltipText = isIndexing
			? $"{dirPath}\n({TranslationServer.Translate("Indexing in progress...")})"
			: dirPath;

		var chipHBox = new HBoxContainer();
		chipHBox.AddThemeConstantOverride("separation", 4);
		chip.AddChild(chipHBox);

		var lblName = new Label();
		lblName.Text = isIndexing ? $"⏳ {folderName}" : $"📁 {folderName}";
		lblName.AddThemeFontSizeOverride("font_size", 10);
		lblName.AddThemeColorOverride("font_color", isIndexing ? UIStyle.ColorCyanGlow : UIStyle.ColorGold);
		chipHBox.AddChild(lblName);

		var btnRemove = new Button();
		btnRemove.Set("icon_max_width", 0);
		btnRemove.Text = "✕";
		btnRemove.AddThemeFontSizeOverride("font_size", 9);
		btnRemove.CustomMinimumSize = new Vector2(16, 16);
		btnRemove.FocusMode = FocusModeEnum.None;
		btnRemove.TooltipText = $"{TranslationServer.Translate("Remove folder from index")}: {dirPath}";
		btnRemove.Pressed += () =>
		{
			AssetIndexService.Instance.RemoveDirectory(dirPath);
			RefreshFolderChips();
			RefreshSearchResults();
		};
		chipHBox.AddChild(btnRemove);

		_folderChipsContainer.AddChild(chip);
	}

	private void RefreshAssetTypeFilterOptions()
	{
		if (_optAssetTypeFilter == null) return;
		_optAssetTypeFilter.Clear();

		var validTypes = GetValidAssetTypes();

		if (validTypes.Count == 0)
		{
			_optAssetTypeFilter.Visible = false;
			_selectedAssetTypeFilter = null;
			return;
		}

		_optAssetTypeFilter.Visible = true;
		_optAssetTypeFilter.AddItem(TranslationServer.Translate("All Types"), 0);
		_optAssetTypeFilter.SetItemMetadata(0, "");

		int selectedIndex = PopulateAssetTypeFilterDropdown(validTypes);

		if (selectedIndex == 0 && !string.IsNullOrEmpty(_selectedAssetTypeFilter))
		{
			_selectedAssetTypeFilter = null;
		}

		_optAssetTypeFilter.Selected = selectedIndex;
	}

	private List<string> GetValidAssetTypes()
	{
		var validTypes = new List<string>();
		foreach (var ext in _allowedExtensions)
		{
			var types = Realm.Shared.Metadata.RealmMetadataHelper.GetValidAssetTypesForExtension(ext);
			foreach (var t in types)
			{
				if (!validTypes.Contains(t, StringComparer.OrdinalIgnoreCase))
				{
					validTypes.Add(t);
				}
			}
		}
		return validTypes;
	}

	private int PopulateAssetTypeFilterDropdown(List<string> validTypes)
	{
		int selectedIndex = 0;
		for (int i = 0; i < validTypes.Count; i++)
		{
			string typeName = validTypes[i];
			int itemIdx = i + 1;
			_optAssetTypeFilter.AddItem(TranslationServer.Translate(typeName), itemIdx);
			_optAssetTypeFilter.SetItemMetadata(itemIdx, typeName);

			if (IsAssetTypeSelected(typeName))
			{
				selectedIndex = itemIdx;
				_selectedAssetTypeFilter = typeName;
			}
		}
		return selectedIndex;
	}

	private bool IsAssetTypeSelected(string typeName)
	{
		if (string.IsNullOrEmpty(_selectedAssetTypeFilter)) return false;
		
		if (typeName.Equals(_selectedAssetTypeFilter, StringComparison.OrdinalIgnoreCase)) return true;
		
		string normType = Realm.Shared.Metadata.RealmMetadataHelper.NormalizeAssetType(typeName);
		string normSelected = Realm.Shared.Metadata.RealmMetadataHelper.NormalizeAssetType(_selectedAssetTypeFilter);
		return normType.Equals(normSelected, StringComparison.OrdinalIgnoreCase);
	}

	private void OnAssetTypeFilterChanged(long index)
	{
		int idx = (int)index;
		if (idx <= 0 || _optAssetTypeFilter == null)
		{
			_selectedAssetTypeFilter = null;
		}
		else
		{
			_selectedAssetTypeFilter = _optAssetTypeFilter.GetItemMetadata(idx).AsString();
		}
		RefreshSearchResults();
	}

	private void OnDirectoryFilterChanged(long index)
	{
		int idx = (int)index;
		if (idx <= 0 || _optDirectoryFilter == null)
		{
			_selectedDirectoryFilter = null;
			_selectedMapNameFilter = null;
			_selectedMapVersionFilter = null;
		}
		else
		{
			string meta = _optDirectoryFilter.GetItemMetadata(idx).AsString();
			if (meta.StartsWith("map:"))
			{
				string payload = meta.Substring(4);
				string[] parts = payload.Split('|');
				_selectedDirectoryFilter = null;
				_selectedMapNameFilter = parts[0];
				_selectedMapVersionFilter = parts.Length > 1 ? parts[1] : "latest";
			}
			else if (meta.StartsWith("dir:"))
			{
				_selectedDirectoryFilter = meta.Substring(4);
				_selectedMapNameFilter = null;
				_selectedMapVersionFilter = null;
			}
			else
			{
				_selectedDirectoryFilter = null;
				_selectedMapNameFilter = null;
				_selectedMapVersionFilter = null;
			}
		}

		RefreshSearchResults();
	}

	private void OnAddFolderPressed()
	{
		var err = DisplayServer.FileDialogShow(
			TranslationServer.Translate("Select Folder to Index"),
			PathUtils.GetProjectRoot(),
			"",
			false,
			DisplayServer.FileDialogMode.OpenDir,
			System.Array.Empty<string>(),
			Callable.From((bool status, string[] selectedPaths, int selectedFilterIndex) =>
			{
				if (status && selectedPaths.Length > 0)
				{
					string folderPath = selectedPaths[0];
					AssetIndexService.Instance.AddDirectory(folderPath);
					RefreshFolderChips();
					RefreshSearchResults();
				}
			})
		);

		if (err != Error.Ok)
		{
			Hud?.ShowFeedback(TranslationServer.Translate("Failed to show folder dialog"));
		}
	}

	private void OnRescanAllPressed()
	{
		AssetIndexService.Instance.RescanAllDirectories();
		Hud?.ShowFeedback(TranslationServer.Translate("Rescanned all indexed directories."));
		RefreshSearchResults();
	}

	private void RefreshSearchResults()
	{
		try
		{
			string searchTerm = _txtSearch.Text?.Trim() ?? string.Empty;
			bool requirePlayerColorMask = _chkHasPlayerColorMask?.ButtonPressed ?? false;
			_matchingAssets.Clear();
			var searchResults = AssetIndexService.Instance.SearchAssets(
				searchTerm,
				_allowedExtensions,
				_selectedDirectoryFilter,
				_requireRealmMetadata,
				_selectedAssetTypeFilter,
				_selectedMapNameFilter,
				_selectedMapVersionFilter,
				requirePlayerColorMask);

			_matchingAssets.AddRange(searchResults);

			_lblResultsCount.Text = $"{_matchingAssets.Count} {TranslationServer.Translate("items found")}";
			_lblEmptyState.Visible = _matchingAssets.Count == 0;

			if (_selectedAsset != null && !_matchingAssets.Any(a => string.Equals(a.FilePath, _selectedAsset.FilePath, StringComparison.OrdinalIgnoreCase)))
			{
				_selectedAsset = null;
				UpdateSelectedAssetDisplay();
			}

			UpdateVirtualGridSize();
			UpdateVisibleGridCells();
		}
		catch (Exception ex)
		{
			GD.PrintErr($"[AssetBrowserDialog] RefreshSearchResults error: {ex.Message}");
		}
	}

	private void UpdateVirtualGridSize()
	{
		float availableWidth = Mathf.Max(100.0f, _scrollContainer.Size.X - GridPadding * 2.0f);
		int columns = Math.Max(1, (int)((availableWidth + SpacingX) / (CellWidth + SpacingX)));
		int totalRows = (_matchingAssets.Count + columns - 1) / columns;
		float totalHeight = _matchingAssets.Count > 0
			? (GridPadding * 2.0f + totalRows * CellHeight + Math.Max(0, totalRows - 1) * SpacingY)
			: 0;

		_virtualGridContent.CustomMinimumSize = new Vector2(0, totalHeight);
	}

	private void UpdateVisibleGridCells()
	{
		float availableWidth = Mathf.Max(100.0f, _scrollContainer.Size.X - GridPadding * 2.0f);
		int columns = Math.Max(1, (int)((availableWidth + SpacingX) / (CellWidth + SpacingX)));
		int totalRows = (_matchingAssets.Count + columns - 1) / columns;
		float totalHeight = _matchingAssets.Count > 0
			? (GridPadding * 2.0f + totalRows * CellHeight + Math.Max(0, totalRows - 1) * SpacingY)
			: 0;

		if (Math.Abs(_virtualGridContent.CustomMinimumSize.Y - totalHeight) > 0.5f)
		{
			_virtualGridContent.CustomMinimumSize = new Vector2(0, totalHeight);
		}

		if (_matchingAssets.Count == 0)
		{
			HideAllGridCells();
			return;
		}

		UpdateGridCellPositions(columns, totalRows);
	}

	private void HideAllGridCells()
	{
		foreach (var cell in _cellPool)
		{
			cell.Visible = false;
		}
	}

	private void UpdateGridCellPositions(int columns, int totalRows)
	{
		float scrollY = (float)_scrollContainer.GetVScrollBar().Value;
		float viewHeight = _scrollContainer.Size.Y;
		if (viewHeight <= 0)
		{
			viewHeight = 320.0f;
		}

		int startRow = Math.Max(0, (int)((scrollY - GridPadding) / (CellHeight + SpacingY)) - 1);
		int endRow = Math.Min(totalRows - 1, (int)((scrollY + viewHeight - GridPadding) / (CellHeight + SpacingY)) + 1);

		int startIndex = Math.Max(0, startRow * columns);
		int endIndex = Math.Min(_matchingAssets.Count - 1, (endRow + 1) * columns - 1);
		int visibleCount = endIndex >= startIndex ? (endIndex - startIndex + 1) : 0;

		EnsureCellPoolSize(visibleCount);
		BindVisibleGridCells(startIndex, visibleCount, columns);

		for (int i = visibleCount; i < _cellPool.Count; i++)
		{
			_cellPool[i].Visible = false;
		}
	}

	private void EnsureCellPoolSize(int visibleCount)
	{
		while (_cellPool.Count < visibleCount)
		{
			var newCell = new AssetGridCell();
			_cellPool.Add(newCell);
			_virtualGridContent.AddChild(newCell);
		}
	}

	private void BindVisibleGridCells(int startIndex, int visibleCount, int columns)
	{
		for (int i = 0; i < visibleCount; i++)
		{
			int assetIndex = startIndex + i;
			var asset = _matchingAssets[assetIndex];
			int row = assetIndex / columns;
			int col = assetIndex % columns;

			float posX = GridPadding + col * (CellWidth + SpacingX);
			float posY = GridPadding + row * (CellHeight + SpacingY);

			var cell = _cellPool[i];
			cell.Position = new Vector2(posX, posY);
			cell.Size = new Vector2(CellWidth, CellHeight);
			cell.Visible = true;

			bool isSelected = _selectedAsset != null && string.Equals(_selectedAsset.FilePath, asset.FilePath, StringComparison.OrdinalIgnoreCase);
			cell.Bind(asset, isSelected, OnAssetCellClicked);
		}
	}

	private void OnAssetCellClicked(IndexedAsset asset, bool isDoubleClick)
	{
		_selectedAsset = asset;
		UpdateSelectedAssetDisplay();
		UpdateVisibleGridCells();

		if (isDoubleClick)
		{
			ApplyAndClose();
		}
	}

	private void UpdateSelectedAssetDisplay()
	{
		StopAudio();

		if (_selectedAsset == null)
		{
			ResetAssetDisplay();
			return;
		}

		_lblSelectedFileName.Text = _selectedAsset.FileName;
		_lblSelectedPath.Text = _selectedAsset.FilePath;
		_lblSelectedSize.Text = FormatFileSize(_selectedAsset.FileSizeBytes);
		_bottomThumbnail.Texture = AssetThumbnailProvider.GetThumbnail(_selectedAsset);
		_txtTagsEdit.Text = _selectedAsset.Tags != null ? string.Join(", ", _selectedAsset.Tags) : string.Empty;
		_txtTagsEdit.Editable = false;
		_btnEditTags.Disabled = false;

		UpdateAssetTypeDisplay();
		UpdateAudioDisplay();
	}

	private void ResetAssetDisplay()
	{
		_lblSelectedFileName.Text = TranslationServer.Translate("No asset selected");
		_lblSelectedPath.Text = string.Empty;
		_lblSelectedSize.Text = string.Empty;
		_bottomThumbnail.Texture = null;
		_txtTagsEdit.Text = string.Empty;
		_txtTagsEdit.Editable = false;
		_btnEditTags.Disabled = true;
		_txtAssetTypeEdit.Text = string.Empty;
		_txtAssetTypeEdit.Editable = false;
		_btnEditAssetType.Disabled = true;
		_btnAudioPlayPause.Visible = false;
	}

	private void UpdateAssetTypeDisplay()
	{
		string embeddedAssetType = Realm.Shared.Metadata.RealmMetadataHelper.ExtractAssetType(_selectedAsset!.FilePath) ?? string.Empty;
		_txtAssetTypeEdit.Text = !string.IsNullOrEmpty(embeddedAssetType) ? embeddedAssetType : TranslationServer.Translate("None");
		_txtAssetTypeEdit.Editable = false;
		var validTypes = Realm.Shared.Metadata.RealmMetadataHelper.GetValidAssetTypesForExtension(_selectedAsset.FilePath);
		_btnEditAssetType.Disabled = (validTypes.Count == 0);
	}

	private void UpdateAudioDisplay()
	{
		string ext = _selectedAsset!.Extension?.ToLowerInvariant() ?? "";
		bool isAudio = ext is ".raud" or ".ogg" or ".wav" or ".mp3" or ".flac" or ".aac";
		if (isAudio && File.Exists(_selectedAsset.FilePath))
		{
			LoadSelectedAudioFile(ext);
			_btnAudioPlayPause.Visible = (_audioPlayer.Stream != null);
			_btnAudioPlayPause.Text = "▶ " + TranslationServer.Translate("Play");
			_btnAudioPlayPause.TooltipText = TranslationServer.Translate("Play audio");
		}
		else
		{
			_btnAudioPlayPause.Visible = false;
		}
	}

	private void LoadSelectedAudioFile(string ext)
	{
		try
		{
			if (ext == ".raud")
			{
				byte[] raudBytes = File.ReadAllBytes(_selectedAsset!.FilePath);
				byte[]? oggBytes = Realm.Shared.Audio.RaudFile.GetTrack(raudBytes, 0);
				if (oggBytes != null && oggBytes.Length > 0)
				{
					var oggStream = AudioStreamOggVorbis.LoadFromBuffer(oggBytes);
					if (oggStream != null)
					{
						oggStream.Loop = false;
						_audioPlayer.Stream = oggStream;
					}
				}
			}
			else if (ext == ".ogg")
			{
				var oggStream = AudioStreamOggVorbis.LoadFromFile(_selectedAsset!.FilePath);
				if (oggStream != null)
				{
					oggStream.Loop = false;
					_audioPlayer.Stream = oggStream;
				}
			}
			else
			{
				_audioPlayer.Stream = GD.Load<AudioStream>(_selectedAsset!.FilePath);
			}
		}
		catch (Exception ex)
		{
			GD.PrintErr($"[AssetBrowserDialog] Failed to load audio stream: {ex.Message}");
		}
	}

	private void OnAudioPlayPausePressed()
	{
		if (_audioPlayer == null || _audioPlayer.Stream == null) return;

		if (_audioPlayer.Playing)
		{
			_audioPlayer.Stop();
			_btnAudioPlayPause.Text = "▶ " + TranslationServer.Translate("Play");
			_btnAudioPlayPause.TooltipText = TranslationServer.Translate("Play audio");
		}
		else
		{
			_audioPlayer.Play();
			_btnAudioPlayPause.Text = "⏹ " + TranslationServer.Translate("Stop");
			_btnAudioPlayPause.TooltipText = TranslationServer.Translate("Stop audio");
		}
	}

	private void OnAudioFinished()
	{
		if (_btnAudioPlayPause != null)
		{
			_btnAudioPlayPause.Text = "▶ " + TranslationServer.Translate("Play");
			_btnAudioPlayPause.TooltipText = TranslationServer.Translate("Play audio");
		}
	}

	private void StopAudio()
	{
		if (_audioPlayer != null)
		{
			if (_audioPlayer.Playing)
			{
				_audioPlayer.Stop();
			}
			_audioPlayer.Stream = null;
		}
		if (_btnAudioPlayPause != null)
		{
			_btnAudioPlayPause.Text = "▶ " + TranslationServer.Translate("Play");
			_btnAudioPlayPause.TooltipText = TranslationServer.Translate("Play audio");
		}
	}

	private void OnEditTagsPressed()
	{
		if (_selectedAsset == null)
		{
			return;
		}

		var dialog = new TagEditorDialog(Hud, _selectedAsset, OnTagsSaved);
		Hud?.AddChild(dialog);
		dialog.OpenDialog();
	}

	private void OnTagsSaved(List<string> updatedTags)
	{
		if (_selectedAsset == null)
		{
			return;
		}

		AssetIndexService.Instance.UpdateAssetTags(_selectedAsset.FilePath, updatedTags);
		_selectedAsset.Tags = updatedTags;
		_txtTagsEdit.Text = string.Join(", ", updatedTags);

		Hud?.ShowFeedback(string.Format(TranslationServer.Translate("Updated tags for {0}"), _selectedAsset.FileName));
		RefreshSearchResults();
	}

	private void OnEditAssetTypePressed()
	{
		if (_selectedAsset == null) return;
		var dialog = new AssetTypeEditorDialog(Hud, _selectedAsset, OnAssetTypeSaved);
		Hud?.AddChild(dialog);
		dialog.OpenDialog();
	}

	private void OnAssetTypeSaved(string updatedAssetType)
	{
		if (_selectedAsset == null) return;
		AssetIndexService.Instance.UpdateAssetType(_selectedAsset.FilePath, updatedAssetType);
		_selectedAsset.AssetType = updatedAssetType;
		_txtAssetTypeEdit.Text = updatedAssetType;
		Hud?.ShowFeedback(string.Format(TranslationServer.Translate("Updated asset type for {0} to {1}"), _selectedAsset.FileName, updatedAssetType));
		RefreshSearchResults();
	}

	protected override void OnApply()
	{
		StopAudio();
		if (_selectedAsset != null && File.Exists(_selectedAsset.FilePath))
		{
			Realm.Shared.Metadata.RealmMetadataHelper.EnsureMetadata(_selectedAsset.FilePath);
			_onAssetSelectedWithPreferredNameCallback?.Invoke(_selectedAsset.FilePath, _selectedAsset.FileName);
			_onAssetSelectedCallback?.Invoke(_selectedAsset.FilePath);
		}
	}

	private static string FormatFileSize(long bytes)
	{
		if (bytes < 1024)
		{
			return $"{bytes} B";
		}
		if (bytes < 1024 * 1024)
		{
			return $"{bytes / 1024.0:F1} KB";
		}
		return $"{bytes / (1024.0 * 1024.0):F2} MB";
	}
}
