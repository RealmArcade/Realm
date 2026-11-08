using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Godot;
using Realm.Client.Services;
using Realm.Shared.Audio;

namespace Realm.Client.UI.MapEditor;

public partial class AssetBrowserDialog
{
	private void BuildControls()
	{
		BodyContainer.AddThemeConstantOverride("separation", 6);

		var topFoldersSection = new VBoxContainer();
		topFoldersSection.Name = "TopFoldersSection_VBoxContainer";
		topFoldersSection.AddThemeConstantOverride("separation", 4);
		BodyContainer.AddChild(topFoldersSection);

		var folderHeaderRow = new HBoxContainer();
		folderHeaderRow.Name = "FolderHeaderRow_HBoxContainer";
		folderHeaderRow.AddThemeConstantOverride("separation", 8);

		var faFont = Hud?.GetFontAwesomeFont();

		var lblFoldersTitle = new Label();
		lblFoldersTitle.Name = "LblFoldersTitle_Label";
		lblFoldersTitle.Text = UnicodeIcons.FOLDER + " " + TranslationServer.Translate("Indexed Folders:");
		lblFoldersTitle.AddThemeFontSizeOverride("font_size", 11);
		lblFoldersTitle.AddThemeColorOverride("font_color", UIStyle.ColorGold);
		if (faFont != null)
		{
			lblFoldersTitle.AddThemeFontOverride("font", faFont);
		}
		folderHeaderRow.AddChild(lblFoldersTitle);

		_btnAddFolder = new Button();
		_btnAddFolder.Name = "BtnAddFolder_Button";
		_btnAddFolder.Set("icon_max_width", 0);
		_btnAddFolder.Text = UnicodeIcons.FOLDER_PLUS + " " + TranslationServer.Translate("Add Folder");
		_btnAddFolder.AddThemeFontSizeOverride("font_size", 11);
		_btnAddFolder.CustomMinimumSize = new Vector2(100, 24);
		_btnAddFolder.FocusMode = FocusModeEnum.None;
		_btnAddFolder.TooltipText = TranslationServer.Translate("Add a new local folder to the asset index");
		if (faFont != null)
		{
			_btnAddFolder.AddThemeFontOverride("font", faFont);
		}
		_btnAddFolder.Pressed += OnAddFolderPressed;
		folderHeaderRow.AddChild(_btnAddFolder);

		_btnRescanAll = new Button();
		_btnRescanAll.Name = "BtnRescanAll_Button";
		_btnRescanAll.Set("icon_max_width", 0);
		_btnRescanAll.Text = UnicodeIcons.REFRESH + " " + TranslationServer.Translate("Rescan");
		_btnRescanAll.AddThemeFontSizeOverride("font_size", 11);
		_btnRescanAll.CustomMinimumSize = new Vector2(80, 24);
		_btnRescanAll.FocusMode = FocusModeEnum.None;
		_btnRescanAll.TooltipText = TranslationServer.Translate("Rescan all indexed folders for new or modified files");
		if (faFont != null)
		{
			_btnRescanAll.AddThemeFontOverride("font", faFont);
		}
		_btnRescanAll.Pressed += OnRescanAllPressed;
		folderHeaderRow.AddChild(_btnRescanAll);

		topFoldersSection.AddChild(folderHeaderRow);

		_folderScroll = new ScrollContainer();
		_folderScroll.Name = "FolderScroll_ScrollContainer";
		_folderScroll.CustomMinimumSize = new Vector2(0, 24);
		_folderScroll.VerticalScrollMode = ScrollContainer.ScrollMode.Disabled;
		_folderScroll.HorizontalScrollMode = ScrollContainer.ScrollMode.Auto;
		_folderScroll.Visible = false;

		_folderChipsContainer = new HBoxContainer();
		_folderChipsContainer.Name = "FolderChipsContainer_HBoxContainer";
		_folderChipsContainer.AddThemeConstantOverride("separation", 6);
		_folderScroll.AddChild(_folderChipsContainer);
		topFoldersSection.AddChild(_folderScroll);

		var directoryRow = new HBoxContainer();
		directoryRow.Name = "DirectoryRow_HBoxContainer";
		directoryRow.AddThemeConstantOverride("separation", 8);
		BodyContainer.AddChild(directoryRow);

		_optDirectoryFilter = new OptionButton();
		_optDirectoryFilter.Name = "OptDirectoryFilter_OptionButton";
		_optDirectoryFilter.SizeFlagsHorizontal = SizeFlags.ExpandFill;
		_optDirectoryFilter.CustomMinimumSize = new Vector2(0, 24);
		_optDirectoryFilter.AddThemeFontSizeOverride("font_size", 11);
		_optDirectoryFilter.FocusMode = FocusModeEnum.None;
		_optDirectoryFilter.ItemSelected += OnDirectoryFilterChanged;
		directoryRow.AddChild(_optDirectoryFilter);

		var filterRow = new HBoxContainer();
		filterRow.Name = "FilterRow_HBoxContainer";
		filterRow.AddThemeConstantOverride("separation", 8);
		BodyContainer.AddChild(filterRow);

		_txtSearch = new LineEdit();
		_txtSearch.Name = "TxtSearch_LineEdit";
		_txtSearch.PlaceholderText = UnicodeIcons.SEARCH + " " + TranslationServer.Translate("Search tags (e.g. grass, rock) or filename...");
		_txtSearch.SizeFlagsHorizontal = SizeFlags.ExpandFill;
		_txtSearch.AddThemeFontSizeOverride("font_size", 11);
		if (faFont != null)
		{
			_txtSearch.AddThemeFontOverride("font", faFont);
		}
		_txtSearch.TextChanged += (_) => RefreshSearchResults();
		filterRow.AddChild(_txtSearch);

		_optAssetTypeFilter = new OptionButton();
		_optAssetTypeFilter.Name = "OptAssetTypeFilter_OptionButton";
		_optAssetTypeFilter.CustomMinimumSize = new Vector2(150, 24);
		_optAssetTypeFilter.AddThemeFontSizeOverride("font_size", 11);
		_optAssetTypeFilter.FocusMode = FocusModeEnum.None;
		_optAssetTypeFilter.ItemSelected += OnAssetTypeFilterChanged;
		filterRow.AddChild(_optAssetTypeFilter);

		_chkHasPlayerColorMask = new CheckBox();
		_chkHasPlayerColorMask.Name = "ChkHasPlayerColorMask_CheckBox";
		_chkHasPlayerColorMask.Set("icon_max_width", 0);
		_chkHasPlayerColorMask.Text = TranslationServer.Translate("Has Player Color Mask");
		_chkHasPlayerColorMask.AddThemeFontSizeOverride("font_size", 11);
		_chkHasPlayerColorMask.FocusMode = FocusModeEnum.None;
		_chkHasPlayerColorMask.TooltipText = TranslationServer.Translate("Filter assets that have a player color (chroma key) mask in metadata");
		_chkHasPlayerColorMask.Toggled += (_) => RefreshSearchResults();
		filterRow.AddChild(_chkHasPlayerColorMask);

		var infoBar = new HBoxContainer();
		infoBar.Name = "InfoBar_HBoxContainer";
		infoBar.AddThemeConstantOverride("separation", 12);
		BodyContainer.AddChild(infoBar);

		_lblResultsCount = new Label();
		_lblResultsCount.Name = "LblResultsCount_Label";
		_lblResultsCount.Text = "0 " + TranslationServer.Translate("items found");
		_lblResultsCount.AddThemeFontSizeOverride("font_size", 10);
		_lblResultsCount.AddThemeColorOverride("font_color", UIStyle.ColorGoldDull);
		infoBar.AddChild(_lblResultsCount);

		_lblFilterExtensions = new Label();
		_lblFilterExtensions.Name = "LblFilterExtensions_Label";
		_lblFilterExtensions.AddThemeFontSizeOverride("font_size", 10);
		_lblFilterExtensions.AddThemeColorOverride("font_color", UIStyle.ColorCyanGlowDim);
		_lblFilterExtensions.SizeFlagsHorizontal = SizeFlags.ExpandFill;
		infoBar.AddChild(_lblFilterExtensions);

		_lblSelectionCount = new Label();
		_lblSelectionCount.Name = "LblSelectionCount_Label";
		_lblSelectionCount.AddThemeFontSizeOverride("font_size", 10);
		_lblSelectionCount.AddThemeColorOverride("font_color", UIStyle.ColorGold);
		_lblSelectionCount.Visible = false;
		infoBar.AddChild(_lblSelectionCount);

		_btnSelectAll = new Button();
		_btnSelectAll.Name = "BtnSelectAll_Button";
		_btnSelectAll.Set("icon_max_width", 0);
		_btnSelectAll.Text = TranslationServer.Translate("Select All");
		_btnSelectAll.AddThemeFontSizeOverride("font_size", 10);
		_btnSelectAll.CustomMinimumSize = new Vector2(90, 22);
		_btnSelectAll.FocusMode = FocusModeEnum.None;
		_btnSelectAll.TooltipText = TranslationServer.Translate("Select every asset matching the current filters");
		_btnSelectAll.Pressed += OnSelectAllPressed;
		_btnSelectAll.Visible = false;
		infoBar.AddChild(_btnSelectAll);

		_btnClearSelection = new Button();
		_btnClearSelection.Name = "BtnClearSelection_Button";
		_btnClearSelection.Set("icon_max_width", 0);
		_btnClearSelection.Text = TranslationServer.Translate("Clear Selection");
		_btnClearSelection.AddThemeFontSizeOverride("font_size", 10);
		_btnClearSelection.CustomMinimumSize = new Vector2(100, 22);
		_btnClearSelection.FocusMode = FocusModeEnum.None;
		_btnClearSelection.TooltipText = TranslationServer.Translate("Deselect all assets");
		_btnClearSelection.Pressed += OnClearSelectionPressed;
		_btnClearSelection.Visible = false;
		infoBar.AddChild(_btnClearSelection);

		_gridPanel = new PanelContainer();
		_gridPanel.Name = "GridPanel_PanelContainer";
		_gridPanel.CustomMinimumSize = new Vector2(0, 320);
		_gridPanel.SizeFlagsVertical = SizeFlags.ExpandFill;
		_gridPanel.AddThemeStyleboxOverride("panel", UIStyle.CreateLightInnerPanel());
		BodyContainer.AddChild(_gridPanel);

		_scrollContainer = new ScrollContainer();
		_scrollContainer.Name = "ScrollContainer_ScrollContainer";
		_scrollContainer.SizeFlagsHorizontal = SizeFlags.ExpandFill;
		_scrollContainer.SizeFlagsVertical = SizeFlags.ExpandFill;
		_scrollContainer.HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled;
		_scrollContainer.VerticalScrollMode = ScrollContainer.ScrollMode.Auto;
		_gridPanel.AddChild(_scrollContainer);

		_virtualGridContent = new Control();
		_virtualGridContent.Name = "VirtualGridContent_Control";
		_virtualGridContent.SizeFlagsHorizontal = SizeFlags.ExpandFill;
		_virtualGridContent.MouseFilter = MouseFilterEnum.Pass;
		_scrollContainer.AddChild(_virtualGridContent);

		_lblEmptyState = new Label();
		_lblEmptyState.Name = "LblEmptyState_Label";
		_lblEmptyState.Text = TranslationServer.Translate("No assets found matching the search criteria.\nClick '+ Add Folder' above to index directories.");
		_lblEmptyState.HorizontalAlignment = HorizontalAlignment.Center;
		_lblEmptyState.VerticalAlignment = VerticalAlignment.Center;
		_lblEmptyState.SizeFlagsHorizontal = SizeFlags.ExpandFill;
		_lblEmptyState.SizeFlagsVertical = SizeFlags.ExpandFill;
		_lblEmptyState.AddThemeColorOverride("font_color", UIStyle.ColorGoldDull);
		_lblEmptyState.AddThemeFontSizeOverride("font_size", 12);
		_lblEmptyState.Visible = false;
		_gridPanel.AddChild(_lblEmptyState);

		_scrollContainer.GetVScrollBar().ValueChanged += (_) => UpdateVisibleGridCells();
		_scrollContainer.Resized += () =>
		{
			UpdateVirtualGridSize(_matchingAssets.Count);
			UpdateVisibleGridCells();
		};

		_bottomDetailsPanel = new PanelContainer();
		_bottomDetailsPanel.Name = "BottomDetailsPanel_PanelContainer";
		_bottomDetailsPanel.CustomMinimumSize = new Vector2(0, 60);
		var detailStyle = new StyleBoxFlat();
		detailStyle.BgColor = new Color(0.08f, 0.09f, 0.11f, 0.9f);
		detailStyle.BorderColor = new Color(0.25f, 0.23f, 0.20f, 0.8f);
		detailStyle.SetBorderWidthAll(1);
		detailStyle.SetCornerRadiusAll(4);
		detailStyle.ContentMarginLeft = 8;
		detailStyle.ContentMarginRight = 8;
		detailStyle.ContentMarginTop = 6;
		detailStyle.ContentMarginBottom = 6;
		_bottomDetailsPanel.AddThemeStyleboxOverride("panel", detailStyle);
		BodyContainer.AddChild(_bottomDetailsPanel);

		var bottomHBox = new HBoxContainer();
		bottomHBox.Name = "BottomHBox_HBoxContainer";
		bottomHBox.AddThemeConstantOverride("separation", 10);
		_bottomDetailsPanel.AddChild(bottomHBox);

		_bottomThumbnail = new TextureRect();
		_bottomThumbnail.Name = "BottomThumbnail_TextureRect";
		_bottomThumbnail.CustomMinimumSize = new Vector2(48, 48);
		_bottomThumbnail.ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize;
		_bottomThumbnail.StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered;
		bottomHBox.AddChild(_bottomThumbnail);

		var bottomInfoVBox = new VBoxContainer();
		bottomInfoVBox.Name = "BottomInfoVBox_VBoxContainer";
		bottomInfoVBox.SizeFlagsHorizontal = SizeFlags.ExpandFill;
		bottomInfoVBox.AddThemeConstantOverride("separation", 2);
		bottomHBox.AddChild(bottomInfoVBox);

		var nameRow = new HBoxContainer();
		nameRow.Name = "NameRow_HBoxContainer";
		nameRow.AddThemeConstantOverride("separation", 8);

		_lblSelectedFileName = new Label();
		_lblSelectedFileName.Name = "LblSelectedFileName_Label";
		_lblSelectedFileName.Text = TranslationServer.Translate("No asset selected");
		_lblSelectedFileName.AddThemeFontSizeOverride("font_size", 11);
		_lblSelectedFileName.AddThemeColorOverride("font_color", UIStyle.ColorGold);
		nameRow.AddChild(_lblSelectedFileName);

		_lblSelectedSize = new Label();
		_lblSelectedSize.Name = "LblSelectedSize_Label";
		_lblSelectedSize.AddThemeFontSizeOverride("font_size", 10);
		_lblSelectedSize.AddThemeColorOverride("font_color", UIStyle.ColorGoldDull);
		nameRow.AddChild(_lblSelectedSize);

		_btnAudioPlayPause = new Button();
		_btnAudioPlayPause.Name = "BtnAudioPlayPause_Button";
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
		_lblSelectedPath.Name = "LblSelectedPath_Label";
		_lblSelectedPath.TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis;
		_lblSelectedPath.AddThemeFontSizeOverride("font_size", 9);
		_lblSelectedPath.AddThemeColorOverride("font_color", UIStyle.ColorGoldDull);
		bottomInfoVBox.AddChild(_lblSelectedPath);

		var tagsRow = new HBoxContainer();
		tagsRow.Name = "TagsRow_HBoxContainer";
		tagsRow.AddThemeConstantOverride("separation", 6);

		var lblTagsTitle = new Label();
		lblTagsTitle.Name = "LblTagsTitle_Label";
		lblTagsTitle.Text = TranslationServer.Translate("Tags:");
		lblTagsTitle.AddThemeFontSizeOverride("font_size", 10);
		lblTagsTitle.AddThemeColorOverride("font_color", UIStyle.ColorGoldDull);
		tagsRow.AddChild(lblTagsTitle);

		_txtTagsEdit = new LineEdit();
		_txtTagsEdit.Name = "TxtTagsEdit_LineEdit";
		_txtTagsEdit.PlaceholderText = TranslationServer.Translate("No tags");
		_txtTagsEdit.SizeFlagsHorizontal = SizeFlags.ExpandFill;
		_txtTagsEdit.AddThemeFontSizeOverride("font_size", 10);
		_txtTagsEdit.Editable = false;
		tagsRow.AddChild(_txtTagsEdit);

		_btnEditTags = new Button();
		_btnEditTags.Name = "BtnEditTags_Button";
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
		assetTypeRow.Name = "AssetTypeRow_HBoxContainer";
		assetTypeRow.AddThemeConstantOverride("separation", 6);

		var lblAssetTypeTitle = new Label();
		lblAssetTypeTitle.Name = "LblAssetTypeTitle_Label";
		lblAssetTypeTitle.Text = TranslationServer.Translate("Asset Type:");
		lblAssetTypeTitle.AddThemeFontSizeOverride("font_size", 10);
		lblAssetTypeTitle.AddThemeColorOverride("font_color", UIStyle.ColorGoldDull);
		assetTypeRow.AddChild(lblAssetTypeTitle);

		_txtAssetTypeEdit = new LineEdit();
		_txtAssetTypeEdit.Name = "TxtAssetTypeEdit_LineEdit";
		_txtAssetTypeEdit.PlaceholderText = TranslationServer.Translate("None");
		_txtAssetTypeEdit.SizeFlagsHorizontal = SizeFlags.ExpandFill;
		_txtAssetTypeEdit.AddThemeFontSizeOverride("font_size", 10);
		_txtAssetTypeEdit.Editable = false;
		assetTypeRow.AddChild(_txtAssetTypeEdit);

		_btnEditAssetType = new Button();
		_btnEditAssetType.Name = "BtnEditAssetType_Button";
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
}
