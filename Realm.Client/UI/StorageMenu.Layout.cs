// Scene construction: the three-column layout, the left navigation (import, refresh, storage totals)
// and the right navigation (the selected map's versions and actions).

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Godot;
using Realm.Client.Services;
using Realm.Shared.Distribution;
using Environment = System.Environment;
using static Godot.Control;
using static Godot.CanvasItem;


namespace Realm.Client.UI;

public partial class StorageMenu
{
	private void BuildLayout()
	{
		var bgPanel = new Panel();
		bgPanel.Name = "BgPanel_Panel";
		bgPanel.SetAnchorsPreset(LayoutPreset.FullRect);
		bgPanel.AddThemeStyleboxOverride("panel", UIStyle.CreateEntranceBgTexture());
		bgPanel.MouseFilter = MouseFilterEnum.Ignore;
		AddChild(bgPanel);

		var rootVBox = new VBoxContainer();
		rootVBox.Name = "RootVBox_VBoxContainer";
		rootVBox.SetAnchorsPreset(LayoutPreset.FullRect);
		rootVBox.AddThemeConstantOverride("separation", 0);
		rootVBox.MouseFilter = MouseFilterEnum.Pass;
		AddChild(rootVBox);

		var topHeaderPanel = new PanelContainer();
		topHeaderPanel.Name = "TopHeaderPanel_PanelContainer";
		topHeaderPanel.CustomMinimumSize = new Vector2(0, 70);
		topHeaderPanel.AddThemeStyleboxOverride("panel", CreateHeaderPanelStyle());
		topHeaderPanel.MouseFilter = MouseFilterEnum.Pass;
		rootVBox.AddChild(topHeaderPanel);

		var topHeaderHBox = new HBoxContainer();
		topHeaderHBox.Name = "TopHeaderHBox_HBoxContainer";
		topHeaderHBox.AddThemeConstantOverride("separation", 20);
		topHeaderHBox.Alignment = BoxContainer.AlignmentMode.Begin;
		topHeaderHBox.MouseFilter = MouseFilterEnum.Pass;
		topHeaderPanel.AddChild(topHeaderHBox);

		_backButton = new Button();
		_backButton.Name = "BackButton_Button";
		UIStyle.ApplyBackButton(_backButton, () => GameContext.UserInterface?.TransitionTo(GameScreen.MainMenu));
		topHeaderHBox.AddChild(_backButton);

		_titleLabel = new Label();
		_titleLabel.Name = "TitleLabel_Label";
		UIStyle.ApplyTitle(_titleLabel, TranslationServer.Translate("MAP STORAGE"), 28);
		_titleLabel.SizeFlagsHorizontal = SizeFlags.ExpandFill;
		_titleLabel.HorizontalAlignment = HorizontalAlignment.Center;
		_titleLabel.MouseFilter = MouseFilterEnum.Ignore;
		topHeaderHBox.AddChild(_titleLabel);

		_importMapButton = new Button();
		_importMapButton.Name = "ImportMapButton_Button";
		_importMapButton.AddThemeConstantOverride("icon_max_width", 24);
		_importMapButton.CustomMinimumSize = new Vector2(210, 42);
		UIStyle.ApplyButtonText(_importMapButton, "📥 " + TranslationServer.Translate("IMPORT MAP (.RMAP)"), 14);
		UIStyle.ApplyButtonStyleBoxes(_importMapButton);
		_importMapButton.FocusMode = FocusModeEnum.None;
		_importMapButton.MouseFilter = MouseFilterEnum.Stop;
		_importMapButton.MouseEntered += () => GameContext.UserInterface?.PlayHoverSound();
		_importMapButton.Pressed += () =>
		{
			GameContext.UserInterface?.PlayClickSound();
			OnImportMapPressed();
		};
		topHeaderHBox.AddChild(_importMapButton);

		var mainMargin = new MarginContainer();
		mainMargin.Name = "MainMargin_MarginContainer";
		mainMargin.SizeFlagsHorizontal = SizeFlags.ExpandFill;
		mainMargin.SizeFlagsVertical = SizeFlags.ExpandFill;
		mainMargin.AddThemeConstantOverride("margin_left", 35);
		mainMargin.AddThemeConstantOverride("margin_right", 35);
		mainMargin.AddThemeConstantOverride("margin_top", 25);
		mainMargin.AddThemeConstantOverride("margin_bottom", 60);
		mainMargin.MouseFilter = MouseFilterEnum.Pass;
		rootVBox.AddChild(mainMargin);

		var mainHBox = new HBoxContainer();
		mainHBox.Name = "MainHBox_HBoxContainer";
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
		leftPanelContainer.Name = "LeftPanelContainer_PanelContainer";
		leftPanelContainer.CustomMinimumSize = new Vector2(380, 0);
		leftPanelContainer.SizeFlagsVertical = SizeFlags.ExpandFill;
		leftPanelContainer.TextureFilter = CanvasItem.TextureFilterEnum.LinearWithMipmaps;
		leftPanelContainer.AddThemeStyleboxOverride("panel", UIStyle.CreateMapStorageFilterPanelStyle());
		leftPanelContainer.MouseFilter = MouseFilterEnum.Pass;
		parent.AddChild(leftPanelContainer);

		var leftVBox = new VBoxContainer();
		leftVBox.Name = "LeftVBox_VBoxContainer";
		leftVBox.AddThemeConstantOverride("separation", 12);
		leftVBox.SizeFlagsHorizontal = SizeFlags.ExpandFill;
		leftVBox.SizeFlagsVertical = SizeFlags.ExpandFill;
		leftVBox.MouseFilter = MouseFilterEnum.Pass;
		leftPanelContainer.AddChild(leftVBox);

		_searchBar = new LineEdit();
		_searchBar.Name = "SearchBar_LineEdit";
		_searchBar.PlaceholderText = TranslationServer.Translate("Search Maps...");
		_searchBar.CustomMinimumSize = new Vector2(0, 40);
		UIStyle.ApplyTextInput(_searchBar);
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
		mapCountMargin.Name = "MapCountMargin_MarginContainer";
		mapCountMargin.AddThemeConstantOverride("margin_top", 24);
		mapCountMargin.AddThemeConstantOverride("margin_bottom", 4);
		mapCountMargin.MouseFilter = MouseFilterEnum.Ignore;
		leftVBox.AddChild(mapCountMargin);

		_mapCountLabel = new Label();
		_mapCountLabel.Name = "MapCountLabel_Label";
		_mapCountLabel.Text = string.Format(TranslationServer.Translate("Downloaded Maps ({0})"), 0);
		_mapCountLabel.AddThemeColorOverride("font_color", UIStyle.ColorGoldDull);
		_mapCountLabel.AddThemeFontSizeOverride("font_size", 13);
		_mapCountLabel.MouseFilter = MouseFilterEnum.Ignore;
		mapCountMargin.AddChild(_mapCountLabel);

		var scrollContainer = new ScrollContainer();
		scrollContainer.Name = "ScrollContainer_ScrollContainer";
		scrollContainer.SizeFlagsHorizontal = SizeFlags.ExpandFill;
		scrollContainer.SizeFlagsVertical = SizeFlags.ExpandFill;
		scrollContainer.HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled;
		scrollContainer.MouseFilter = MouseFilterEnum.Pass;
		leftVBox.AddChild(scrollContainer);

		_mapListContainer = new VBoxContainer();
		_mapListContainer.Name = "MapListContainer_VBoxContainer";
		_mapListContainer.SizeFlagsHorizontal = SizeFlags.ExpandFill;
		_mapListContainer.SizeFlagsVertical = SizeFlags.ExpandFill;
		_mapListContainer.AddThemeConstantOverride("separation", 8);
		_mapListContainer.MouseFilter = MouseFilterEnum.Pass;
		scrollContainer.AddChild(_mapListContainer);
	}

	private void BuildRightNav(HBoxContainer parent)
	{
		_rightNavPanel = new PanelContainer();
		_rightNavPanel.Name = "RightNavPanel_PanelContainer";
		_rightNavPanel.SizeFlagsHorizontal = SizeFlags.ExpandFill;
		_rightNavPanel.SizeFlagsVertical = SizeFlags.ExpandFill;
		_rightNavPanel.TextureFilter = CanvasItem.TextureFilterEnum.LinearWithMipmaps;
		_rightNavPanel.AddThemeStyleboxOverride("panel", UIStyle.CreateMapStorageMainPanelStyle());
		_rightNavPanel.MouseFilter = MouseFilterEnum.Pass;
		parent.AddChild(_rightNavPanel);

		_emptySelectionLabel = new Label();
		_emptySelectionLabel.Name = "EmptySelectionLabel_Label";
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
		_selectedMapContainer.Name = "SelectedMapContainer_VBoxContainer";
		_selectedMapContainer.SizeFlagsHorizontal = SizeFlags.ExpandFill;
		_selectedMapContainer.SizeFlagsVertical = SizeFlags.ExpandFill;
		_selectedMapContainer.AddThemeConstantOverride("separation", 16);
		_selectedMapContainer.MouseFilter = MouseFilterEnum.Pass;
		_selectedMapContainer.Visible = false;
		_rightNavPanel.AddChild(_selectedMapContainer);

		var topDetailsHBox = new HBoxContainer();
		topDetailsHBox.Name = "TopDetailsHBox_HBoxContainer";
		topDetailsHBox.AddThemeConstantOverride("separation", 16);
		topDetailsHBox.MouseFilter = MouseFilterEnum.Pass;
		_selectedMapContainer.AddChild(topDetailsHBox);

		_mapThumbnail = new TextureRect();
		_mapThumbnail.Name = "MapThumbnail_TextureRect";
		_mapThumbnail.CustomMinimumSize = new Vector2(140, 100);
		_mapThumbnail.ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize;
		_mapThumbnail.StretchMode = TextureRect.StretchModeEnum.KeepAspectCovered;
		_mapThumbnail.TextureFilter = CanvasItem.TextureFilterEnum.LinearWithMipmaps;
		_mapThumbnail.Texture = UIStyle.EmptyBlackTexture;
		_mapThumbnail.MouseFilter = MouseFilterEnum.Ignore;
		topDetailsHBox.AddChild(_mapThumbnail);

		var metaVBox = new VBoxContainer();
		metaVBox.Name = "MetaVBox_VBoxContainer";
		metaVBox.SizeFlagsHorizontal = SizeFlags.ExpandFill;
		metaVBox.AddThemeConstantOverride("separation", 4);
		metaVBox.MouseFilter = MouseFilterEnum.Pass;
		topDetailsHBox.AddChild(metaVBox);

		_selectedTitleLabel = new Label();
		_selectedTitleLabel.Name = "SelectedTitleLabel_Label";
		UIStyle.ApplyTitle(_selectedTitleLabel, TranslationServer.Translate("Map Title"), 22);
		_selectedTitleLabel.MouseFilter = MouseFilterEnum.Ignore;
		metaVBox.AddChild(_selectedTitleLabel);

		_selectedAuthorLabel = new Label();
		_selectedAuthorLabel.Name = "SelectedAuthorLabel_Label";
		_selectedAuthorLabel.Text = "Author: Unknown";
		_selectedAuthorLabel.AddThemeColorOverride("font_color", UIStyle.ColorGoldDull);
		_selectedAuthorLabel.AddThemeFontSizeOverride("font_size", 13);
		_selectedAuthorLabel.MouseFilter = MouseFilterEnum.Ignore;
		metaVBox.AddChild(_selectedAuthorLabel);

		_selectedGenreLabel = new Label();
		_selectedGenreLabel.Name = "SelectedGenreLabel_Label";
		_selectedGenreLabel.Text = "Genre: Custom Map";
		_selectedGenreLabel.AddThemeColorOverride("font_color", UIStyle.ColorCyanGlow);
		_selectedGenreLabel.AddThemeFontSizeOverride("font_size", 12);
		metaVBox.AddChild(_selectedGenreLabel);

		_selectedTotalSizeLabel = new Label();
		_selectedTotalSizeLabel.Name = "SelectedTotalSizeLabel_Label";
		_selectedTotalSizeLabel.Text = "Total Storage: 0 MB";
		_selectedTotalSizeLabel.AddThemeColorOverride("font_color", new Color(0.8f, 0.8f, 0.8f));
		_selectedTotalSizeLabel.AddThemeFontSizeOverride("font_size", 12);
		metaVBox.AddChild(_selectedTotalSizeLabel);

		_selectedDescriptionLabel = new Label();
		_selectedDescriptionLabel.Name = "SelectedDescriptionLabel_Label";
		_selectedDescriptionLabel.Text = "";
		_selectedDescriptionLabel.AutowrapMode = TextServer.AutowrapMode.WordSmart;
		_selectedDescriptionLabel.AddThemeColorOverride("font_color", new Color(0.85f, 0.85f, 0.85f));
		_selectedDescriptionLabel.AddThemeFontSizeOverride("font_size", 13);
		_selectedDescriptionLabel.MouseFilter = MouseFilterEnum.Ignore;
		_selectedMapContainer.AddChild(_selectedDescriptionLabel);

		var actionsHBox = new HBoxContainer();
		actionsHBox.Name = "ActionsHBox_HBoxContainer";
		actionsHBox.AddThemeConstantOverride("separation", 14);
		actionsHBox.MouseFilter = MouseFilterEnum.Pass;
		_selectedMapContainer.AddChild(actionsHBox);

		_downloadUpdateButton = new Button();
		_downloadUpdateButton.Name = "DownloadUpdateButton_Button";
		_downloadUpdateButton.AddThemeConstantOverride("icon_max_width", 20);
		_downloadUpdateButton.CustomMinimumSize = new Vector2(240, 38);
		UIStyle.ApplyButtonText(_downloadUpdateButton, "⚡ " + TranslationServer.Translate("DOWNLOAD UPDATED VERSION"), 14);
		UIStyle.ApplyButtonStyleBoxes(_downloadUpdateButton);
		_downloadUpdateButton.FocusMode = FocusModeEnum.None;
		_downloadUpdateButton.MouseFilter = MouseFilterEnum.Stop;
		_downloadUpdateButton.Visible = false;
		_downloadUpdateButton.MouseEntered += () => GameContext.UserInterface?.PlayHoverSound();
		_downloadUpdateButton.Pressed += () =>
		{
			GameContext.UserInterface?.PlayClickSound();
			OnDownloadUpdatePressed();
		};
		actionsHBox.AddChild(_downloadUpdateButton);

		var separator = new HSeparator();
		separator.Name = "Separator_HSeparator";
		separator.MouseFilter = MouseFilterEnum.Ignore;
		_selectedMapContainer.AddChild(separator);

		var versionsHeaderLabel = new Label();
		versionsHeaderLabel.Name = "VersionsHeaderLabel_Label";
		UIStyle.ApplyTitle(versionsHeaderLabel, TranslationServer.Translate("INSTALLED VERSIONS"), 16);
		versionsHeaderLabel.MouseFilter = MouseFilterEnum.Ignore;
		_selectedMapContainer.AddChild(versionsHeaderLabel);

		var versionsScroll = new ScrollContainer();
		versionsScroll.Name = "VersionsScroll_ScrollContainer";
		versionsScroll.SizeFlagsHorizontal = SizeFlags.ExpandFill;
		versionsScroll.SizeFlagsVertical = SizeFlags.ExpandFill;
		versionsScroll.HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled;
		versionsScroll.MouseFilter = MouseFilterEnum.Pass;
		_selectedMapContainer.AddChild(versionsScroll);

		_versionsListContainer = new VBoxContainer();
		_versionsListContainer.Name = "VersionsListContainer_VBoxContainer";
		_versionsListContainer.SizeFlagsHorizontal = SizeFlags.ExpandFill;
		_versionsListContainer.SizeFlagsVertical = SizeFlags.ExpandFill;
		_versionsListContainer.AddThemeConstantOverride("separation", 10);
		_versionsListContainer.MouseFilter = MouseFilterEnum.Pass;
		versionsScroll.AddChild(_versionsListContainer);
	}

}