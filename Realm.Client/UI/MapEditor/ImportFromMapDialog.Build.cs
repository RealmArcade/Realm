// Scene construction: the source dropdown, the import-kind selector, the search box, the candidate
// list and the action footer, plus the two helpers that mint a kind item and a button.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Godot;
using Realm.Client.Services;


namespace Realm.Client.UI.MapEditor;

public partial class ImportFromMapDialog
{
	private void BuildControls()
	{
		BodyContainer.AddThemeConstantOverride("separation", 8);

		var kindRow = new HBoxContainer();
		kindRow.Name = "KindRow_HBoxContainer";
		kindRow.AddThemeConstantOverride("separation", 8);

		var lblKind = new Label();
		lblKind.Name = "LblKind_Label";
		lblKind.Text = TranslationServer.Translate("Import Type:");
		lblKind.AddThemeFontSizeOverride("font_size", 11);
		lblKind.AddThemeColorOverride("font_color", UIStyle.ColorGold);
		lblKind.VerticalAlignment = VerticalAlignment.Center;
		kindRow.AddChild(lblKind);

		_optImportKind = new OptionButton();
		_optImportKind.Name = "OptImportKind_OptionButton";
		_optImportKind.CustomMinimumSize = new Vector2(180, 26);
		_optImportKind.AddThemeFontSizeOverride("font_size", 11);
		_optImportKind.FocusMode = FocusModeEnum.None;
		_optImportKind.TooltipText = TranslationServer.Translate("Choose whether to copy assets, configured templates with their dependencies, or map script code");
		AddImportKindItem(TranslationServer.Translate("Assets"), nameof(ImportKind.Assets));
		AddImportKindItem(TranslationServer.Translate("Templates"), nameof(ImportKind.Templates));
		AddImportKindItem(TranslationServer.Translate("Code"), nameof(ImportKind.Code));
		_optImportKind.ItemSelected += _ => OnImportKindChanged();
		kindRow.AddChild(_optImportKind);

		BodyContainer.AddChild(kindRow);

		var sourceRow = new HBoxContainer();
		sourceRow.Name = "SourceRow_HBoxContainer";
		sourceRow.AddThemeConstantOverride("separation", 8);

		var lblSource = new Label();
		lblSource.Name = "LblSource_Label";
		lblSource.Text = TranslationServer.Translate("Source Map:");
		lblSource.AddThemeFontSizeOverride("font_size", 11);
		lblSource.AddThemeColorOverride("font_color", UIStyle.ColorGold);
		lblSource.VerticalAlignment = VerticalAlignment.Center;
		sourceRow.AddChild(lblSource);

		_optMapSource = new OptionButton();
		_optMapSource.Name = "OptMapSource_OptionButton";
		_optMapSource.SizeFlagsHorizontal = SizeFlags.ExpandFill;
		_optMapSource.CustomMinimumSize = new Vector2(0, 26);
		_optMapSource.AddThemeFontSizeOverride("font_size", 11);
		_optMapSource.FocusMode = FocusModeEnum.None;
		_optMapSource.ItemSelected += _ => OnSourceChanged();
		sourceRow.AddChild(_optMapSource);

		_btnAddArchive = CreateActionButton("BtnAddArchive_Button", "📥 " + TranslationServer.Translate("Add .rmap"), () => PromptAddExternalMap(selectFolder: false), TranslationServer.Translate("Copy a map package into storage and use it as the source map"), 92);
		sourceRow.AddChild(_btnAddArchive);

		_btnAddFolder = CreateActionButton("BtnAddFolder_Button", "📂 " + TranslationServer.Translate("Add Folder"), () => PromptAddExternalMap(selectFolder: true), TranslationServer.Translate("Copy a map folder into storage and use it as the source map"), 100);
		sourceRow.AddChild(_btnAddFolder);

		BodyContainer.AddChild(sourceRow);

		var filterRow = new HBoxContainer();
		filterRow.Name = "FilterRow_HBoxContainer";
		filterRow.AddThemeConstantOverride("separation", 8);

		_optCodeScope = new OptionButton();
		_optCodeScope.Name = "OptCodeScope_OptionButton";
		_optCodeScope.CustomMinimumSize = new Vector2(130, 26);
		_optCodeScope.AddThemeFontSizeOverride("font_size", 11);
		_optCodeScope.FocusMode = FocusModeEnum.None;
		_optCodeScope.TooltipText = TranslationServer.Translate("Choose between curated [Export] items, individual classes, or whole namespaces");
		_optCodeScope.AddItem(TranslationServer.Translate("Classes"), (int)CodeScope.Classes);
		_optCodeScope.AddItem(TranslationServer.Translate("Namespaces"), (int)CodeScope.Namespaces);
		_optCodeScope.AddItem(TranslationServer.Translate("Exported"), (int)CodeScope.Exported);
		_optCodeScope.ItemSelected += _ => OnCodeScopeChanged();
		_optCodeScope.Visible = false;
		filterRow.AddChild(_optCodeScope);

		_txtSearch = new LineEdit();
		_txtSearch.Name = "TxtSearch_LineEdit";
		_txtSearch.PlaceholderText = GetSearchPlaceholder();
		_txtSearch.SizeFlagsHorizontal = SizeFlags.ExpandFill;
		_txtSearch.CustomMinimumSize = new Vector2(0, 26);
		_txtSearch.AddThemeFontSizeOverride("font_size", 11);
		_txtSearch.TextChanged += _ => RebuildItemRows();
		filterRow.AddChild(_txtSearch);

		_chkOnlyMissing = new CheckBox();
		_chkOnlyMissing.Name = "ChkOnlyMissing_CheckBox";
		_chkOnlyMissing.Set("icon_max_width", 0);
		_chkOnlyMissing.Text = TranslationServer.Translate("Only missing");
		_chkOnlyMissing.AddThemeFontSizeOverride("font_size", 11);
		_chkOnlyMissing.FocusMode = FocusModeEnum.None;
		_chkOnlyMissing.ButtonPressed = true;
		_chkOnlyMissing.TooltipText = TranslationServer.Translate("Hide items this map already contains");
		_chkOnlyMissing.Toggled += _ => RebuildItemRows();
		filterRow.AddChild(_chkOnlyMissing);

		BodyContainer.AddChild(filterRow);

		var actionRow = new HBoxContainer();
		actionRow.Name = "ActionRow_HBoxContainer";
		actionRow.AddThemeConstantOverride("separation", 8);

		_lblItemCount = new Label();
		_lblItemCount.Name = "LblItemCount_Label";
		_lblItemCount.Text = "0 " + TranslationServer.Translate("items");
		_lblItemCount.SizeFlagsHorizontal = SizeFlags.ExpandFill;
		_lblItemCount.AddThemeFontSizeOverride("font_size", 10);
		_lblItemCount.AddThemeColorOverride("font_color", UIStyle.ColorCyanGlowDim);
		actionRow.AddChild(_lblItemCount);

		_btnSelectAll = CreateActionButton("BtnSelectAll_Button", TranslationServer.Translate("Select All"), OnSelectAllPressed, TranslationServer.Translate("Select every listed item"), 90);
		actionRow.AddChild(_btnSelectAll);

		_btnSelectNone = CreateActionButton("BtnSelectNone_Button", TranslationServer.Translate("Select None"), OnSelectNonePressed, TranslationServer.Translate("Clear the selection"), 95);
		actionRow.AddChild(_btnSelectNone);

		_btnImportSelected = CreateActionButton("BtnImportSelected_Button", TranslationServer.Translate("Import Selected"), OnImportSelectedPressed, TranslationServer.Translate("Copy the selected items into this map"), 130);
		actionRow.AddChild(_btnImportSelected);

		BodyContainer.AddChild(actionRow);

		var listPanel = new PanelContainer();
		listPanel.Name = "ListPanel_PanelContainer";
		listPanel.CustomMinimumSize = new Vector2(0, 360);
		listPanel.SizeFlagsVertical = SizeFlags.ExpandFill;
		listPanel.AddThemeStyleboxOverride("panel", UIStyle.CreateLightInnerPanel());
		BodyContainer.AddChild(listPanel);

		_assetScroll = new ScrollContainer();
		_assetScroll.Name = "AssetScroll_ScrollContainer";
		_assetScroll.SizeFlagsHorizontal = SizeFlags.ExpandFill;
		_assetScroll.SizeFlagsVertical = SizeFlags.ExpandFill;
		_assetScroll.HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled;
		listPanel.AddChild(_assetScroll);

		_assetList = new VBoxContainer();
		_assetList.Name = "AssetList_VBoxContainer";
		_assetList.SizeFlagsHorizontal = SizeFlags.ExpandFill;
		_assetList.AddThemeConstantOverride("separation", 2);
		_assetScroll.AddChild(_assetList);

		_lblSummary = new Label();
		_lblSummary.Name = "LblSummary_Label";
		_lblSummary.AutowrapMode = TextServer.AutowrapMode.Word;
		_lblSummary.CustomMinimumSize = new Vector2(700, 0);
		_lblSummary.SizeFlagsHorizontal = SizeFlags.ExpandFill;
		_lblSummary.AddThemeFontSizeOverride("font_size", 10);
		_lblSummary.AddThemeColorOverride("font_color", UIStyle.ColorGoldDull);
		BodyContainer.AddChild(_lblSummary);
	}

	private void AddImportKindItem(string label, string kindKey)
	{
		int index = _optImportKind.ItemCount;
		_optImportKind.AddItem(label, index);
		_optImportKind.SetItemMetadata(index, kindKey);
	}

	private Button CreateActionButton(string nodeName, string text, Action onPressed, string tooltip, int minimumWidth)
	{
		var button = new Button();
		button.Name = nodeName;
		button.Set("icon_max_width", 0);
		button.Text = text;
		button.CustomMinimumSize = new Vector2(minimumWidth, 26);
		button.FocusMode = FocusModeEnum.None;
		button.TooltipText = tooltip;
		button.AddThemeFontSizeOverride("font_size", 11);
		button.Pressed += onPressed;
		return button;
	}

}