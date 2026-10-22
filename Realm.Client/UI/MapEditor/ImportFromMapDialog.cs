using System;
using System.Collections.Generic;
using System.IO;
using Godot;
using Realm.Client.Services;

namespace Realm.Client.UI.MapEditor;

[GlobalClass]
public partial class ImportFromMapDialog : FloatingDialogBase
{
	public ImportFromMapDialog()
	{
	}

	private OptionButton _optMapSource;
	private LineEdit _txtSearch;
	private CheckBox _chkOnlyMissing;
	private Label _lblAssetCount;
	private Label _lblSummary;
	private ScrollContainer _assetScroll;
	private VBoxContainer _assetList;
	private Button _btnSelectAll;
	private Button _btnSelectNone;
	private Button _btnImportSelected;

	private readonly List<MapAssetImportSource> _sources = new();
	private readonly List<MapAssetImportCandidate> _candidates = new();
	private readonly Dictionary<string, CheckBox> _rowCheckBoxes = new(StringComparer.OrdinalIgnoreCase);

	private Action? _onImportCompleted;

	public ImportFromMapDialog(MapEditorHUD hud)
		: base(hud, TranslationServer.Translate("Import Assets From Map"), new Vector2(760, 660))
	{
		SetUncompressedPanelTexture("res://Assets/UI/map_editor_assets_importer.webp", 30, 45, 70, 70);
		BuildControls();
		SetFooterCloseOnly();
	}

	private void BuildControls()
	{
		BodyContainer.AddThemeConstantOverride("separation", 8);

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

		BodyContainer.AddChild(sourceRow);

		var filterRow = new HBoxContainer();
		filterRow.Name = "FilterRow_HBoxContainer";
		filterRow.AddThemeConstantOverride("separation", 8);

		_txtSearch = new LineEdit();
		_txtSearch.Name = "TxtSearch_LineEdit";
		_txtSearch.PlaceholderText = TranslationServer.Translate("Filter by asset name or category...");
		_txtSearch.SizeFlagsHorizontal = SizeFlags.ExpandFill;
		_txtSearch.CustomMinimumSize = new Vector2(0, 26);
		_txtSearch.AddThemeFontSizeOverride("font_size", 11);
		_txtSearch.TextChanged += _ => RebuildAssetRows();
		filterRow.AddChild(_txtSearch);

		_chkOnlyMissing = new CheckBox();
		_chkOnlyMissing.Name = "ChkOnlyMissing_CheckBox";
		_chkOnlyMissing.Set("icon_max_width", 0);
		_chkOnlyMissing.Text = TranslationServer.Translate("Only missing assets");
		_chkOnlyMissing.AddThemeFontSizeOverride("font_size", 11);
		_chkOnlyMissing.FocusMode = FocusModeEnum.None;
		_chkOnlyMissing.ButtonPressed = true;
		_chkOnlyMissing.TooltipText = TranslationServer.Translate("Hide assets this map already contains");
		_chkOnlyMissing.Toggled += _ => RebuildAssetRows();
		filterRow.AddChild(_chkOnlyMissing);

		BodyContainer.AddChild(filterRow);

		var actionRow = new HBoxContainer();
		actionRow.Name = "ActionRow_HBoxContainer";
		actionRow.AddThemeConstantOverride("separation", 8);

		_lblAssetCount = new Label();
		_lblAssetCount.Name = "LblAssetCount_Label";
		_lblAssetCount.Text = "0 " + TranslationServer.Translate("assets");
		_lblAssetCount.SizeFlagsHorizontal = SizeFlags.ExpandFill;
		_lblAssetCount.AddThemeFontSizeOverride("font_size", 10);
		_lblAssetCount.AddThemeColorOverride("font_color", UIStyle.ColorCyanGlowDim);
		actionRow.AddChild(_lblAssetCount);

		_btnSelectAll = CreateActionButton("BtnSelectAll_Button", TranslationServer.Translate("Select All"), OnSelectAllPressed, TranslationServer.Translate("Select every listed asset"), 90);
		actionRow.AddChild(_btnSelectAll);

		_btnSelectNone = CreateActionButton("BtnSelectNone_Button", TranslationServer.Translate("Select None"), OnSelectNonePressed, TranslationServer.Translate("Clear the asset selection"), 95);
		actionRow.AddChild(_btnSelectNone);

		_btnImportSelected = CreateActionButton("BtnImportSelected_Button", TranslationServer.Translate("Import Selected"), OnImportSelectedPressed, TranslationServer.Translate("Copy the selected assets into this map"), 130);
		actionRow.AddChild(_btnImportSelected);

		BodyContainer.AddChild(actionRow);

		var listPanel = new PanelContainer();
		listPanel.Name = "ListPanel_PanelContainer";
		listPanel.CustomMinimumSize = new Vector2(0, 380);
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

	public void OpenForWorkspace(Action? onImportCompleted = null)
	{
		_onImportCompleted = onImportCompleted;
		_lblSummary.Text = string.Empty;
		_txtSearch.Text = string.Empty;
		LoadSources();
		OpenDialog();
	}

	private void LoadSources()
	{
		_sources.Clear();
		_sources.AddRange(GameContext.MapAssetImportService.GetImportSources());

		_optMapSource.Clear();
		foreach (var source in _sources)
		{
			int index = _optMapSource.ItemCount;
			_optMapSource.AddItem($"{source.MapTitle} v{source.MapVersion}", index);
		}

		bool hasSources = _sources.Count > 0;
		_optMapSource.Disabled = !hasSources;
		_btnSelectAll.Disabled = !hasSources;
		_btnSelectNone.Disabled = !hasSources;
		_btnImportSelected.Disabled = !hasSources;

		if (!hasSources)
		{
			_lblSummary.Text = TranslationServer.Translate("No downloaded or local maps are available to import from.");
			_candidates.Clear();
			RebuildAssetRows();
			return;
		}

		_optMapSource.Select(0);
		OnSourceChanged();
	}

	private void OnSourceChanged()
	{
		_candidates.Clear();

		int index = _optMapSource.Selected;
		if (index < 0 || index >= _sources.Count) return;

		var source = _sources[index];
		_candidates.AddRange(GameContext.MapAssetImportService.EnumerateAssets(source.ManifestFilePath, GetWorkspacePath()));
		RebuildAssetRows();
	}

	private string GetWorkspacePath()
	{
		if (!string.IsNullOrEmpty(GameContext.EditorService._tempWorkspacePath))
		{
			return GameContext.EditorService._tempWorkspacePath;
		}

		return MapWorkspaceService.GetActiveWorkspacePath();
	}

	private void RebuildAssetRows()
	{
		foreach (var child in _assetList.GetChildren())
		{
			_assetList.RemoveChild(child);
			child.QueueFree();
		}

		_rowCheckBoxes.Clear();

		string filter = _txtSearch.Text?.Trim() ?? string.Empty;
		bool onlyMissing = _chkOnlyMissing.ButtonPressed;
		int listedCount = 0;

		foreach (var candidate in _candidates)
		{
			if (onlyMissing && candidate.AlreadyPresent) continue;
			if (!MatchesFilter(candidate, filter)) continue;

			var row = CreateAssetRow(candidate);
			_assetList.AddChild(row);
			listedCount++;
		}

		_lblAssetCount.Text = $"{listedCount} " + TranslationServer.Translate("assets");
		UpdateSelectionState();
	}

	private static bool MatchesFilter(MapAssetImportCandidate candidate, string filter)
	{
		if (string.IsNullOrEmpty(filter)) return true;

		return candidate.FileName.IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0
		       || candidate.Category.IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0;
	}

	private CheckBox CreateAssetRow(MapAssetImportCandidate candidate)
	{
		var checkBox = new CheckBox();
		checkBox.Name = $"AssetRow_{SanitizeNodeName(candidate.FileName)}";
		checkBox.Set("icon_max_width", 0);
		checkBox.Text = candidate.AlreadyPresent
			? $"{candidate.FileName} ({TranslationServer.Translate("already present")})"
			: candidate.FileName;
		checkBox.TooltipText = $"{candidate.RelativePath}\n{TranslationServer.Translate("Asset Type:")} {candidate.Category}";
		checkBox.AddThemeFontSizeOverride("font_size", 11);
		checkBox.FocusMode = FocusModeEnum.None;
		checkBox.Disabled = candidate.AlreadyPresent;
		checkBox.ButtonPressed = !candidate.AlreadyPresent;
		checkBox.Toggled += _ => UpdateSelectionState();
		_rowCheckBoxes[candidate.RelativePath] = checkBox;
		return checkBox;
	}

	private static string SanitizeNodeName(string fileName)
	{
		return fileName.Replace('.', '_').Replace('/', '_').Replace('\\', '_').Replace(' ', '_');
	}

	private void OnSelectAllPressed()
	{
		foreach (var checkBox in _rowCheckBoxes.Values)
		{
			if (!checkBox.Disabled)
			{
				checkBox.ButtonPressed = true;
			}
		}

		UpdateSelectionState();
	}

	private void OnSelectNonePressed()
	{
		foreach (var checkBox in _rowCheckBoxes.Values)
		{
			if (!checkBox.Disabled)
			{
				checkBox.ButtonPressed = false;
			}
		}

		UpdateSelectionState();
	}

	private void UpdateSelectionState()
	{
		int selectedCount = 0;
		foreach (var checkBox in _rowCheckBoxes.Values)
		{
			if (checkBox.ButtonPressed)
			{
				selectedCount++;
			}
		}

		_btnImportSelected.Text = selectedCount > 0
			? string.Format(TranslationServer.Translate("Import Selected ({0})"), selectedCount)
			: TranslationServer.Translate("Import Selected");
		_btnImportSelected.Disabled = selectedCount == 0;
	}

	private void OnImportSelectedPressed()
	{
		var selectedCandidates = new List<MapAssetImportCandidate>();
		foreach (var candidate in _candidates)
		{
			if (!_rowCheckBoxes.TryGetValue(candidate.RelativePath, out var checkBox)) continue;
			if (!checkBox.ButtonPressed) continue;

			selectedCandidates.Add(candidate);
		}

		if (selectedCandidates.Count == 0)
		{
			_lblSummary.Text = TranslationServer.Translate("Select at least one asset to import.");
			return;
		}

		var summary = GameContext.MapAssetImportService.ImportInto(GetWorkspacePath(), selectedCandidates);
		string summaryText = BuildSummaryText(summary);
		_lblSummary.Text = summaryText;
		Hud?.ShowFeedback(summaryText);

		if (summary.Missing.Count > 0)
		{
			GD.Print($"[ImportFromMapDialog] Assets not found in storage: {string.Join(", ", summary.Missing)}");
		}

		if (summary.Conflicts.Count > 0)
		{
			GD.Print($"[ImportFromMapDialog] Assets skipped due to name conflicts: {string.Join(", ", summary.Conflicts)}");
		}

		_onImportCompleted?.Invoke();

		int index = _optMapSource.Selected;
		if (index >= 0 && index < _sources.Count)
		{
			_candidates.Clear();
			_candidates.AddRange(GameContext.MapAssetImportService.EnumerateAssets(_sources[index].ManifestFilePath, GetWorkspacePath()));
		}

		RebuildAssetRows();
	}

	private static string BuildSummaryText(MapAssetImportSummary summary)
	{
		return string.Format(
			TranslationServer.Translate("Imported {0}, skipped {1} already present, {2} missing, {3} name conflicts, {4} failed."),
			summary.Imported.Count,
			summary.Skipped.Count,
			summary.Missing.Count,
			summary.Conflicts.Count,
			summary.Failed.Count);
	}

	public override void CloseDialog()
	{
		base.CloseDialog();
	}
}
