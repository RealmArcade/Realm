using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using Godot;
using Realm.Client.Animation;
using Realm.Client.Core;
using Realm.Client.Services;
using Realm.EditorAPI;
using Realm.Shared.Audio;
using Realm.Shared.Textures;
using Realm.Client.Utils;

namespace Realm.Client.UI.MapEditor;

public partial class AssetManagerDialog
{
	private void BuildControls()
	{
		BodyContainer.AddThemeConstantOverride("separation", 8);

		var headerRow = new HBoxContainer();
		headerRow.Name = "HeaderRow_HBoxContainer";
		headerRow.AddThemeConstantOverride("separation", 8);

		var lblType = new Label();
		lblType.Name = "LblType_Label";
		lblType.Text = "🏷 " + TranslationServer.Translate("Asset Type:");
		lblType.AddThemeFontSizeOverride("font_size", 11);
		lblType.AddThemeColorOverride("font_color", UIStyle.ColorGold);
		headerRow.AddChild(lblType);

		_optAssetTypeFilter = new OptionButton();
		_optAssetTypeFilter.Name = "OptAssetTypeFilter_OptionButton";
		_optAssetTypeFilter.CustomMinimumSize = new Vector2(160, 26);
		_optAssetTypeFilter.AddThemeFontSizeOverride("font_size", 11);
		_optAssetTypeFilter.FocusMode = FocusModeEnum.None;

		for (int i = 0; i < ValidAssetTypes.Length; i++)
		{
			string typeName = ValidAssetTypes[i];
			string label = typeName == "All" ? TranslationServer.Translate("All Asset Types") : TranslationServer.Translate(typeName);
			_optAssetTypeFilter.AddItem(label, i);
			_optAssetTypeFilter.SetItemMetadata(i, typeName);
		}

		_optAssetTypeFilter.ItemSelected += (idx) =>
		{
			_selectedAssetType = _optAssetTypeFilter.GetItemMetadata((int)idx).AsString();
			UpdateActionButtonVisibility();
			RefreshAssetList();
		};
		headerRow.AddChild(_optAssetTypeFilter);

		var lblSearch = new Label();
		lblSearch.Name = "LblSearch_Label";
		lblSearch.Text = "🔍 " + TranslationServer.Translate("Search:");
		lblSearch.AddThemeFontSizeOverride("font_size", 11);
		lblSearch.AddThemeColorOverride("font_color", UIStyle.ColorGold);
		headerRow.AddChild(lblSearch);

		_txtSearchFilter = new LineEdit();
		_txtSearchFilter.Name = "TxtSearchFilter_LineEdit";
		_txtSearchFilter.PlaceholderText = TranslationServer.Translate("Search by filename or metadata tags...");
		_txtSearchFilter.SizeFlagsHorizontal = SizeFlags.ExpandFill;
		_txtSearchFilter.AddThemeFontSizeOverride("font_size", 11);
		_txtSearchFilter.TextChanged += (text) =>
		{
			_searchFilterText = text?.Trim() ?? string.Empty;
			RefreshAssetList();
		};
		headerRow.AddChild(_txtSearchFilter);

		_lblAssetCount = new Label();
		_lblAssetCount.Name = "LblAssetCount_Label";
		_lblAssetCount.Text = "0 " + TranslationServer.Translate("assets");
		_lblAssetCount.AddThemeFontSizeOverride("font_size", 11);
		_lblAssetCount.AddThemeColorOverride("font_color", UIStyle.ColorCyanGlowDim);
		headerRow.AddChild(_lblAssetCount);

		BodyContainer.AddChild(headerRow);

		var actionsRow = new HBoxContainer();
		actionsRow.Name = "ActionsRow_HBoxContainer";
		actionsRow.AddThemeConstantOverride("separation", 6);

		_btnImportAsset = AddButton(actionsRow, "📥 " + TranslationServer.Translate("Import Asset"), () => OpenImportFileDialog(), "Import an asset from asset index or disk", 11, new Vector2(110, 26));
		_btnImportAsset.Name = "BtnImportAsset_Button";
		_btnImportShader = AddButton(actionsRow, "✨ " + TranslationServer.Translate("Import Shader"), () => OnImportShaderPressed(), "Import shader configuration file", 11, new Vector2(115, 26));
		_btnConvertFormat = AddButton(actionsRow, $"🔄 {TranslationServer.Translate("Convert External Format")} {UnicodeIcons.CHEVRON_DOWN}", () => ShowConvertFormatMenu(), "Convert external image, audio, 3D model, or Mixamo animation files to Realm formats", 11, new Vector2(215, 26));
		_btnConvertFormat.Name = "BtnConvertFormat_Button";
		_btnGenerateNoise = AddButton(actionsRow, "🎲 " + TranslationServer.Translate("Generate Noise"), () => Hud?.OpenNoiseTextureDialog((_) => RefreshAssetList()), "Generate procedural noise texture", 11, new Vector2(120, 26));
		_btnGenerateAiAsset = AddButton(actionsRow, $"{UnicodeIcons.CLOUD} {TranslationServer.Translate("Generate AI Asset")} {UnicodeIcons.CHEVRON_DOWN}", () => ShowGenerateAiAssetMenu(), "Generate a new asset online in your browser", 11, new Vector2(190, 26));
		_btnGenerateAiAsset.Name = "BtnGenerateAiAsset_Button";

		BodyContainer.AddChild(actionsRow);

		_lblAssetPackStatus = new Label();
		_lblAssetPackStatus.Name = "AssetPackStatus_Label";
		_lblAssetPackStatus.Text = string.Empty;
		_lblAssetPackStatus.Visible = false;
		_lblAssetPackStatus.AutowrapMode = TextServer.AutowrapMode.WordSmart;
		_lblAssetPackStatus.CustomMinimumSize = new Vector2(700, 0);
		_lblAssetPackStatus.SizeFlagsHorizontal = SizeFlags.ExpandFill;
		_lblAssetPackStatus.AddThemeFontSizeOverride("font_size", 11);
		_lblAssetPackStatus.AddThemeColorOverride("font_color", UIStyle.ColorGoldDull);
		BodyContainer.AddChild(_lblAssetPackStatus);

		if (Hud != null)
		{
			_convertFormatMenu = new DropdownSuggestionList(Hud, _btnConvertFormat) { MinWidth = 215.0f, RowHeight = 24.0f };
			_convertFormatMenu.ItemSelected += (index, _) => OnConvertFormatSelected((int)index);
			_generateAiAssetMenu = new DropdownSuggestionList(Hud, _btnGenerateAiAsset) { MinWidth = 190.0f, RowHeight = 24.0f };
			_generateAiAssetMenu.ItemSelected += (index, _) => OnGenerateAiAssetSelected((int)index);
		}

		VisibilityChanged += () =>
		{
			if (Visible) return;
			_convertFormatMenu?.Hide();
			_generateAiAssetMenu?.Hide();
		};

		var gridPanel = new PanelContainer();
		gridPanel.Name = "GridPanel_PanelContainer";
		gridPanel.CustomMinimumSize = new Vector2(0, 360);
		gridPanel.SizeFlagsVertical = SizeFlags.ExpandFill;
		gridPanel.AddThemeStyleboxOverride("panel", UIStyle.CreateLightInnerPanel());
		BodyContainer.AddChild(gridPanel);

		_scrollContainer = new ScrollContainer();
		_scrollContainer.Name = "ScrollContainer_ScrollContainer";
		_scrollContainer.SizeFlagsHorizontal = SizeFlags.ExpandFill;
		_scrollContainer.SizeFlagsVertical = SizeFlags.ExpandFill;
		_scrollContainer.HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled;
		_scrollContainer.VerticalScrollMode = ScrollContainer.ScrollMode.Auto;
		gridPanel.AddChild(_scrollContainer);

		_virtualGridContent = new Control();
		_virtualGridContent.Name = "VirtualGridContent_Control";
		_virtualGridContent.SizeFlagsHorizontal = SizeFlags.ExpandFill;
		_virtualGridContent.MouseFilter = MouseFilterEnum.Pass;
		_scrollContainer.AddChild(_virtualGridContent);

		_lblEmptyState = new Label();
		_lblEmptyState.Name = "LblEmptyState_Label";
		_lblEmptyState.Text = TranslationServer.Translate("No matching assets found in manifest.json.");
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
			UpdateVirtualGridSize(_matchingAssets.Count);
			UpdateVisibleGridCells();
		};

		_footerPanel = new PanelContainer();
		_footerPanel.Name = "FooterPanel_PanelContainer";
		_footerPanel.CustomMinimumSize = new Vector2(0, 95);
		_footerPanel.SizeFlagsHorizontal = SizeFlags.ExpandFill;
		var detailStyle = new StyleBoxFlat
		{
			BgColor = new Color(0.08f, 0.09f, 0.11f, 0.95f),
			BorderColor = new Color(0.25f, 0.23f, 0.20f, 0.8f)
		};
		detailStyle.SetBorderWidthAll(1);
		detailStyle.SetCornerRadiusAll(4);
		detailStyle.ContentMarginLeft = 10;
		detailStyle.ContentMarginRight = 10;
		detailStyle.ContentMarginTop = 8;
		detailStyle.ContentMarginBottom = 8;
		_footerPanel.AddThemeStyleboxOverride("panel", detailStyle);
		BodyContainer.AddChild(_footerPanel);

		var footerHBox = new HBoxContainer();
		footerHBox.Name = "FooterHBox_HBoxContainer";
		footerHBox.AddThemeConstantOverride("separation", 12);
		_footerPanel.AddChild(footerHBox);

		_footerThumbnail = new TextureRect();
		_footerThumbnail.Name = "FooterThumbnail_TextureRect";
		_footerThumbnail.CustomMinimumSize = new Vector2(74, 74);
		_footerThumbnail.ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize;
		_footerThumbnail.StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered;
		footerHBox.AddChild(_footerThumbnail);

		var footerInfoVBox = new VBoxContainer();
		footerInfoVBox.Name = "FooterInfoVBox_VBoxContainer";
		footerInfoVBox.SizeFlagsHorizontal = SizeFlags.ExpandFill;
		footerInfoVBox.AddThemeConstantOverride("separation", 3);
		footerHBox.AddChild(footerInfoVBox);

		var titleRow = new HBoxContainer();
		titleRow.Name = "TitleRow_HBoxContainer";
		titleRow.AddThemeConstantOverride("separation", 8);

		_lblSelectedFileName = new Label();
		_lblSelectedFileName.Name = "LblSelectedFileName_Label";
		_lblSelectedFileName.Text = TranslationServer.Translate("No asset selected");
		_lblSelectedFileName.AddThemeFontSizeOverride("font_size", 12);
		_lblSelectedFileName.AddThemeColorOverride("font_color", UIStyle.ColorGold);
		titleRow.AddChild(_lblSelectedFileName);

		_lblSelectedSize = new Label();
		_lblSelectedSize.Name = "LblSelectedSize_Label";
		_lblSelectedSize.AddThemeFontSizeOverride("font_size", 10);
		_lblSelectedSize.AddThemeColorOverride("font_color", UIStyle.ColorGoldDull);
		titleRow.AddChild(_lblSelectedSize);

		_btnAudioPlay = new Button();
		_btnAudioPlay.Name = "BtnAudioPlay_Button";
		_btnAudioPlay.Set("icon_max_width", 0);
		_btnAudioPlay.Text = "▶ " + TranslationServer.Translate("Play");
		_btnAudioPlay.AddThemeFontSizeOverride("font_size", 10);
		_btnAudioPlay.CustomMinimumSize = new Vector2(70, 22);
		_btnAudioPlay.FocusMode = FocusModeEnum.None;
		_btnAudioPlay.TooltipText = TranslationServer.Translate("Play loaded audio");
		_btnAudioPlay.Pressed += OnAudioPlayPausePressed;
		_btnAudioPlay.Visible = false;
		titleRow.AddChild(_btnAudioPlay);

		_optRaudEntries = new OptionButton();
		_optRaudEntries.Name = "OptRaudEntries_OptionButton";
		_optRaudEntries.Set("icon_max_width", 0);
		_optRaudEntries.AddThemeFontSizeOverride("font_size", 10);
		_optRaudEntries.CustomMinimumSize = new Vector2(140, 22);
		_optRaudEntries.FocusMode = FocusModeEnum.None;
		_optRaudEntries.TooltipText = TranslationServer.Translate("Select the voice line inside the audio container");
		_optRaudEntries.ItemSelected += OnRaudEntrySelected;
		_optRaudEntries.Visible = false;
		titleRow.AddChild(_optRaudEntries);

		footerInfoVBox.AddChild(titleRow);

		_lblSelectedPath = new Label();
		_lblSelectedPath.Name = "LblSelectedPath_Label";
		_lblSelectedPath.AutowrapMode = TextServer.AutowrapMode.WordSmart;
		_lblSelectedPath.SizeFlagsHorizontal = SizeFlags.ExpandFill;
		_lblSelectedPath.AddThemeFontSizeOverride("font_size", 10);
		_lblSelectedPath.AddThemeColorOverride("font_color", UIStyle.ColorGoldDull);
		footerInfoVBox.AddChild(_lblSelectedPath);

		_lblSelectedDetails = new Label();
		_lblSelectedDetails.Name = "LblSelectedDetails_Label";
		_lblSelectedDetails.AutowrapMode = TextServer.AutowrapMode.WordSmart;
		_lblSelectedDetails.SizeFlagsHorizontal = SizeFlags.ExpandFill;
		_lblSelectedDetails.AddThemeFontSizeOverride("font_size", 10);
		_lblSelectedDetails.AddThemeColorOverride("font_color", UIStyle.ColorCyanGlowDim);
		footerInfoVBox.AddChild(_lblSelectedDetails);

		var footerBtnVBox = new VBoxContainer();
		footerBtnVBox.Name = "FooterBtnVBox_VBoxContainer";
		footerBtnVBox.Alignment = BoxContainer.AlignmentMode.Center;
		footerBtnVBox.AddThemeConstantOverride("separation", 6);
		footerHBox.AddChild(footerBtnVBox);

		_btnDeleteAsset = new Button();
		_btnDeleteAsset.Name = "BtnDeleteAsset_Button";
		_btnDeleteAsset.Set("icon_max_width", 0);
		_btnDeleteAsset.Text = "🗑 " + TranslationServer.Translate("Delete");
		_btnDeleteAsset.AddThemeFontSizeOverride("font_size", 11);
		_btnDeleteAsset.AddThemeColorOverride("font_color", new Color(0.95f, 0.4f, 0.4f));
		_btnDeleteAsset.CustomMinimumSize = new Vector2(90, 28);
		_btnDeleteAsset.FocusMode = FocusModeEnum.None;
		_btnDeleteAsset.TooltipText = TranslationServer.Translate("Remove asset from manifest.json with orphan protection check");
		_btnDeleteAsset.Pressed += OnDeleteAssetClicked;
		_btnDeleteAsset.Disabled = true;
		footerBtnVBox.AddChild(_btnDeleteAsset);

		UpdateActionButtonVisibility();
	}
}
