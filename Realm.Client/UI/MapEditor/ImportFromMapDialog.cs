using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Godot;
using Realm.Client.Services;

namespace Realm.Client.UI.MapEditor;

[GlobalClass]
public partial class ImportFromMapDialog : FloatingDialogBase
{
	private OptionButton _optImportKind;
	private OptionButton _optMapSource;
	private OptionButton _optCodeScope;
	private LineEdit _txtSearch;
	private CheckBox _chkOnlyMissing;
	private Label _lblItemCount;
	private Label _lblSummary;
	private ScrollContainer _assetScroll;
	private VBoxContainer _assetList;
	private Button _btnSelectAll;
	private Button _btnSelectNone;
	private Button _btnImportSelected;
	private Button _btnAddArchive;
	private Button _btnAddFolder;

	private readonly List<MapAssetImportSource> _sources = new();
	private readonly List<MapAssetImportCandidate> _assetCandidates = new();
	private readonly List<MapTemplateImportCandidate> _templateCandidates = new();
	private readonly List<MapCodeTypeCandidate> _codeTypeCandidates = new();
	private readonly List<MapCodeNamespaceCandidate> _codeNamespaceCandidates = new();
	private readonly Dictionary<string, CheckBox> _rowCheckBoxes = new(StringComparer.OrdinalIgnoreCase);

	private ImportKind _selectedImportKind = ImportKind.Assets;
	private CodeScope _codeScope = CodeScope.Classes;
	private bool _codeScopeChosenExplicitly;
	private Action? _onImportCompleted;

	private enum ImportKind
	{
		Assets,
		Templates,
		Code
	}

	private enum CodeScope
	{
		Classes = 0,
		Namespaces = 1,
		Exported = 2
	}

	public ImportFromMapDialog(MapEditorHUD hud)
		: base(hud, TranslationServer.Translate("Import From Map"), new Vector2(760, 680))
	{
		SetUncompressedPanelTexture("res://Assets/UI/map_editor_assets_importer.webp", 30, 45, 70, 70);
		BuildControls();
		SetFooterCloseOnly();
	}

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
			ClearCandidates();
			RebuildItemRows();
			return;
		}

		_optMapSource.Select(0);
		ReloadCandidates();
	}

	private MapAssetImportSource? GetSelectedSource()
	{
		int index = _optMapSource.Selected;
		if (index < 0 || index >= _sources.Count) return null;

		return _sources[index];
	}

	private void PromptAddExternalMap(bool selectFolder)
	{
		var dialogMode = selectFolder ? DisplayServer.FileDialogMode.OpenDir : DisplayServer.FileDialogMode.OpenFile;
		string[] filters = selectFolder ? Array.Empty<string>() : new[] { "*.rmap ; Realm Map Package (*.rmap)" };
		string dialogTitle = selectFolder
			? TranslationServer.Translate("Select Map Folder")
			: TranslationServer.Translate("Select Map Package (.rmap)");

		var dialogError = DisplayServer.FileDialogShow(
			dialogTitle,
			OS.GetSystemDir(OS.SystemDir.Documents),
			"",
			false,
			dialogMode,
			filters,
			Callable.From((bool status, string[] selectedPaths, int selectedFilterIndex) =>
			{
				if (status && selectedPaths.Length > 0)
				{
					_ = AddExternalMapAsync(selectedPaths[0]);
				}
			}));

		if (dialogError != Error.Ok)
		{
			_lblSummary.Text = TranslationServer.Translate("The map picker could not be opened.");
		}
	}

	private async Task AddExternalMapAsync(string sourcePath)
	{
		if (string.IsNullOrWhiteSpace(sourcePath)) return;
		if (!File.Exists(sourcePath) && !Directory.Exists(sourcePath))
		{
			_lblSummary.Text = TranslationServer.Translate("The selected map package or folder no longer exists.");
			return;
		}

		SetAddSourceButtonsEnabled(false);
		_lblSummary.Text = string.Format(
			TranslationServer.Translate("Adding {0} to storage..."),
			Path.GetFileName(sourcePath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)));

		try
		{
			var result = await GameContext.MapStorageService.ImportMapAsync(sourcePath);
			if (!result.Success)
			{
				_lblSummary.Text = result.Message;
				Hud?.ShowFeedback(result.Message);
				return;
			}

			LoadSources();

			if (SelectSource(result.MapTitle, result.MapVersion))
			{
				string selectedSourceText = string.Format(
					TranslationServer.Translate("Added {0} v{1} to storage and selected it as the source map."),
					result.MapTitle,
					result.MapVersion);
				_lblSummary.Text = selectedSourceText;
				Hud?.ShowFeedback(selectedSourceText);
			}
			else
			{
				_lblSummary.Text = result.Message;
				Hud?.ShowFeedback(result.Message);
			}
		}
		catch (Exception exception)
		{
			GD.PrintErr($"[ImportFromMapDialog] Failed to add '{sourcePath}': {exception.Message}");
			_lblSummary.Text = string.Format(TranslationServer.Translate("The map could not be added: {0}"), exception.Message);
		}
		finally
		{
			SetAddSourceButtonsEnabled(true);
		}
	}

	private bool SelectSource(string? mapTitle, string? mapVersion)
	{
		if (string.IsNullOrWhiteSpace(mapTitle)) return false;

		for (int sourceIndex = 0; sourceIndex < _sources.Count; sourceIndex++)
		{
			MapAssetImportSource source = _sources[sourceIndex];
			if (!string.Equals(source.MapTitle, mapTitle, StringComparison.OrdinalIgnoreCase)) continue;
			if (!string.IsNullOrWhiteSpace(mapVersion) && !string.Equals(source.MapVersion, mapVersion, StringComparison.OrdinalIgnoreCase)) continue;

			_optMapSource.Select(sourceIndex);
			ReloadCandidates();
			return true;
		}

		return false;
	}

	private void SetAddSourceButtonsEnabled(bool isEnabled)
	{
		if (_btnAddArchive != null) _btnAddArchive.Disabled = !isEnabled;
		if (_btnAddFolder != null) _btnAddFolder.Disabled = !isEnabled;
	}

	private void OnImportKindChanged()
	{
		if (_optImportKind.Selected < 0) return;

		string kindKey = _optImportKind.GetItemMetadata(_optImportKind.Selected).AsString();
		_selectedImportKind = Enum.TryParse(kindKey, out ImportKind parsed) ? parsed : ImportKind.Assets;

		_optCodeScope.Visible = _selectedImportKind == ImportKind.Code;
		_txtSearch.PlaceholderText = GetSearchPlaceholder();
		_chkOnlyMissing.Text = TranslationServer.Translate("Only missing");
		_lblSummary.Text = string.Empty;
		ReloadCandidates();
	}

	private void OnCodeScopeChanged()
	{
		_codeScope = (CodeScope)Math.Max(0, _optCodeScope.Selected);
		_codeScopeChosenExplicitly = true;
		_txtSearch.PlaceholderText = GetSearchPlaceholder();
		ReloadCandidates();
	}

	private string GetSearchPlaceholder()
	{
		if (_selectedImportKind == ImportKind.Code)
		{
			if (_codeScope == CodeScope.Namespaces)
			{
				return TranslationServer.Translate("Filter by namespace...");
			}

			return _codeScope == CodeScope.Exported
				? TranslationServer.Translate("Filter exported items...")
				: TranslationServer.Translate("Filter by class name or namespace...");
		}

		return TranslationServer.Translate("Filter by name, type, or category...");
	}

	private void OnSourceChanged()
	{
		_lblSummary.Text = string.Empty;
		ReloadCandidates();
	}

	private string GetWorkspacePath()
	{
		if (!string.IsNullOrEmpty(GameContext.EditorService._tempWorkspacePath))
		{
			return GameContext.EditorService._tempWorkspacePath;
		}

		return MapWorkspaceService.GetActiveWorkspacePath();
	}

	private void ClearCandidates()
	{
		_assetCandidates.Clear();
		_templateCandidates.Clear();
		_codeTypeCandidates.Clear();
		_codeNamespaceCandidates.Clear();
	}

	private void ResolveDefaultCodeScope(MapAssetImportSource source, string workspacePath)
	{
		if (_codeScopeChosenExplicitly) return;

		_codeScope = GameContext.MapCodeImportService.EnumerateExports(source, workspacePath).Count > 0
			? CodeScope.Exported
			: CodeScope.Classes;

		if (_optCodeScope != null)
		{
			_optCodeScope.Selected = (int)_codeScope;
		}
	}

	private void ReloadCandidates()
	{
		ClearCandidates();

		var source = GetSelectedSource();
		if (source.HasValue)
		{
			var selectedSource = source.Value;
			string workspacePath = GetWorkspacePath();

			switch (_selectedImportKind)
			{
				case ImportKind.Assets:
					_assetCandidates.AddRange(GameContext.MapAssetImportService.EnumerateAssets(selectedSource.ManifestFilePath, workspacePath));
					break;
				case ImportKind.Templates:
					_templateCandidates.AddRange(GameContext.MapTemplateImportService.EnumerateTemplates(selectedSource, workspacePath));
					break;
				case ImportKind.Code:
					ResolveDefaultCodeScope(selectedSource, workspacePath);
					switch (_codeScope)
					{
						case CodeScope.Namespaces:
							_codeNamespaceCandidates.AddRange(GameContext.MapCodeImportService.EnumerateNamespaces(selectedSource, workspacePath));
							break;
						case CodeScope.Exported:
							_codeTypeCandidates.AddRange(GameContext.MapCodeImportService.EnumerateExports(selectedSource, workspacePath));
							break;
						default:
							_codeTypeCandidates.AddRange(GameContext.MapCodeImportService.EnumerateTypes(selectedSource, workspacePath));
							break;
					}
					break;
			}
		}

		RebuildItemRows();
	}

	private void RebuildItemRows()
	{
		foreach (var child in _assetList.GetChildren())
		{
			_assetList.RemoveChild(child);
			child.QueueFree();
		}

		_rowCheckBoxes.Clear();

		string filter = _txtSearch.Text?.Trim() ?? string.Empty;

		switch (_selectedImportKind)
		{
			case ImportKind.Assets:
				RebuildAssetRows(filter);
				break;
			case ImportKind.Templates:
				RebuildTemplateRows(filter);
				break;
			case ImportKind.Code:
				RebuildCodeRows(filter);
				break;
		}

		UpdateSelectionState();
	}

	private void RebuildAssetRows(string filter)
	{
		bool onlyMissing = _chkOnlyMissing.ButtonPressed;
		int listedCount = 0;

		foreach (var candidate in _assetCandidates)
		{
			if (onlyMissing && candidate.AlreadyPresent) continue;
			if (!MatchesAssetFilter(candidate, filter)) continue;

			string label = candidate.AlreadyPresent
				? $"{candidate.FileName} ({TranslationServer.Translate("already present")})"
				: candidate.FileName;
			string tooltip = $"{candidate.RelativePath}\n{TranslationServer.Translate("Asset Type:")} {candidate.Category}";

			AddRow(candidate.RelativePath, label, tooltip, candidate.AlreadyPresent);
			listedCount++;
		}

		UpdateItemCount(listedCount);
	}

	private void RebuildTemplateRows(string filter)
	{
		bool onlyMissing = _chkOnlyMissing.ButtonPressed;
		int listedCount = 0;

		foreach (var candidate in _templateCandidates)
		{
			if (onlyMissing && candidate.AlreadyPresent) continue;
			if (!MatchesTemplateFilter(candidate, filter)) continue;

			string label = $"{candidate.ObjectType}: {candidate.DisplayName}";
			if (candidate.AlreadyPresent)
			{
				label += $" ({TranslationServer.Translate("already present")})";
			}

			string tooltip = $"{candidate.Identity}\n{TranslationServer.Translate("Template Type:")} {candidate.ObjectType}\n{TranslationServer.Translate("Imports referenced templates and assets automatically")}";

			AddRow(candidate.SelectionKey, label, tooltip, candidate.AlreadyPresent);
			listedCount++;
		}

		UpdateItemCount(listedCount);
	}

	private void RebuildCodeRows(string filter)
	{
		if (_codeScope == CodeScope.Namespaces)
		{
			RebuildCodeNamespaceRows(filter);
			return;
		}

		bool useExportLabels = _codeScope == CodeScope.Exported;
		bool onlyMissing = _chkOnlyMissing.ButtonPressed;
		int listedCount = 0;

		foreach (var candidate in _codeTypeCandidates)
		{
			if (onlyMissing && candidate.WillReplace) continue;

			string primaryText = useExportLabels ? candidate.ExportLabel : candidate.DisplayName;
			string secondaryText = useExportLabels ? $"{candidate.DisplayName} {candidate.NamespaceName}" : candidate.NamespaceName;
			if (!MatchesFilterText(primaryText, secondaryText, filter)) continue;

			string label = candidate.WillReplace
				? $"{primaryText} ({TranslationServer.Translate("will replace")})"
				: primaryText;
			string tooltip = useExportLabels
				? $"{candidate.ExportLabel}\n{candidate.FullName}\n{TranslationServer.Translate("Class type:")} {candidate.Kind}\n{TranslationServer.Translate("Target file:")} {candidate.RelativePath}"
				: $"{candidate.FullName}\n{TranslationServer.Translate("Class type:")} {candidate.Kind}\n{TranslationServer.Translate("Target file:")} {candidate.RelativePath}";

			AddRow(candidate.SelectionKey, label, tooltip, alreadyPresent: false);
			listedCount++;
		}

		UpdateItemCount(listedCount);
	}

	private void RebuildCodeNamespaceRows(string filter)
	{
		bool onlyMissing = _chkOnlyMissing.ButtonPressed;
		int listedCount = 0;

		foreach (var candidate in _codeNamespaceCandidates)
		{
			if (onlyMissing && candidate.WillReplace) continue;
			if (!MatchesFilterText(candidate.DisplayName, candidate.NamespaceName, filter)) continue;

			string label = $"{candidate.DisplayName} ({string.Format(TranslationServer.Translate("{0} classes"), candidate.TypeCount)})";
			if (candidate.WillReplace)
			{
				label += $" ({TranslationServer.Translate("will replace")})";
			}

			string tooltip = $"{candidate.NamespaceName}\n{TranslationServer.Translate("Imports every class in this namespace and its dependencies automatically")}";

			AddRow(candidate.SelectionKey, label, tooltip, alreadyPresent: false);
			listedCount++;
		}

		UpdateItemCount(listedCount);
	}

	private void UpdateItemCount(int count)
	{
		_lblItemCount.Text = $"{count} " + TranslationServer.Translate("items");
	}

	private void AddRow(string key, string label, string tooltip, bool alreadyPresent)
	{
		var checkBox = new CheckBox();
		checkBox.Name = $"ImportRow_{SanitizeNodeName(key)}";
		checkBox.Set("icon_max_width", 0);
		checkBox.Text = label;
		checkBox.TooltipText = tooltip;
		checkBox.AddThemeFontSizeOverride("font_size", 11);
		checkBox.FocusMode = FocusModeEnum.None;
		checkBox.Disabled = alreadyPresent;
		checkBox.ButtonPressed = !alreadyPresent;
		checkBox.Toggled += _ => UpdateSelectionState();

		_rowCheckBoxes[key] = checkBox;
		_assetList.AddChild(checkBox);
	}

	private static bool MatchesAssetFilter(MapAssetImportCandidate candidate, string filter)
	{
		return MatchesFilterText(candidate.FileName, candidate.Category, filter);
	}

	private static bool MatchesTemplateFilter(MapTemplateImportCandidate candidate, string filter)
	{
		return MatchesFilterText(candidate.DisplayName, candidate.ObjectType, filter)
		       || MatchesFilterText(candidate.Identity, candidate.ObjectType, filter);
	}

	private static bool MatchesFilterText(string primary, string secondary, string filter)
	{
		if (string.IsNullOrEmpty(filter)) return true;

		return primary.IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0
		       || secondary.IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0;
	}

	private static string SanitizeNodeName(string fileName)
	{
		return fileName.Replace('.', '_').Replace('/', '_').Replace('\\', '_').Replace(' ', '_').Replace('|', '_');
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
		int selectedCount = CountSelectedRows();

		_btnImportSelected.Text = selectedCount > 0
			? string.Format(TranslationServer.Translate("Import Selected ({0})"), selectedCount)
			: TranslationServer.Translate("Import Selected");
		_btnImportSelected.Disabled = selectedCount == 0;
	}

	private int CountSelectedRows()
	{
		int selectedCount = 0;
		foreach (var checkBox in _rowCheckBoxes.Values)
		{
			if (checkBox.ButtonPressed)
			{
				selectedCount++;
			}
		}

		return selectedCount;
	}

	private void OnImportSelectedPressed()
	{
		var selectedKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
		foreach (var entry in _rowCheckBoxes)
		{
			if (entry.Value.ButtonPressed)
			{
				selectedKeys.Add(entry.Key);
			}
		}

		if (selectedKeys.Count == 0)
		{
			_lblSummary.Text = TranslationServer.Translate("Select at least one item to import.");
			return;
		}

		string workspacePath = GetWorkspacePath();

		switch (_selectedImportKind)
		{
			case ImportKind.Assets:
				FinishImport(ImportSelectedAssets(selectedKeys, workspacePath));
				break;
			case ImportKind.Templates:
				FinishImport(ImportSelectedTemplates(selectedKeys, workspacePath));
				break;
			case ImportKind.Code:
				ImportSelectedCode(selectedKeys, workspacePath);
				break;
		}
	}

	private void FinishImport(string summaryText)
	{
		_lblSummary.Text = summaryText;
		Hud?.ShowFeedback(summaryText);
		_onImportCompleted?.Invoke();
		ReloadCandidates();
	}

	private string ImportSelectedAssets(HashSet<string> selectedKeys, string workspacePath)
	{
		var selectedCandidates = new List<MapAssetImportCandidate>();
		foreach (var candidate in _assetCandidates)
		{
			if (selectedKeys.Contains(candidate.RelativePath))
			{
				selectedCandidates.Add(candidate);
			}
		}

		var summary = GameContext.MapAssetImportService.ImportInto(workspacePath, selectedCandidates);

		if (summary.Missing.Count > 0)
		{
			GD.Print($"[ImportFromMapDialog] Assets not found in storage: {string.Join(", ", summary.Missing)}");
		}

		if (summary.Conflicts.Count > 0)
		{
			GD.Print($"[ImportFromMapDialog] Assets skipped due to name conflicts: {string.Join(", ", summary.Conflicts)}");
		}

		return BuildAssetSummaryText(summary);
	}

	private string ImportSelectedTemplates(HashSet<string> selectedKeys, string workspacePath)
	{
		var selectedCandidates = new List<MapTemplateImportCandidate>();
		foreach (var candidate in _templateCandidates)
		{
			if (selectedKeys.Contains(candidate.SelectionKey))
			{
				selectedCandidates.Add(candidate);
			}
		}

		var source = GetSelectedSource();
		if (!source.HasValue)
		{
			return TranslationServer.Translate("No source map is selected.");
		}

		var summary = GameContext.MapTemplateImportService.ImportTemplates(source.Value, workspacePath, selectedCandidates);

		if (summary.ImportedTemplates.Count > 0)
		{
			string metadataPath = MetadataService.ResolveMetadataPath(workspacePath);
			Hud?.UpdateLastMetadataSyncTime(metadataPath);
			Hud?.ReadMetadataAndRefreshTextures();
		}

		if (summary.MissingAssets.Count > 0)
		{
			GD.Print($"[ImportFromMapDialog] Template assets not found in storage: {string.Join(", ", summary.MissingAssets)}");
		}

		if (summary.ConflictAssets.Count > 0)
		{
			GD.Print($"[ImportFromMapDialog] Template assets skipped due to name conflicts: {string.Join(", ", summary.ConflictAssets)}");
		}

		return BuildTemplateSummaryText(summary);
	}

	private void ImportSelectedCode(HashSet<string> selectedKeys, string workspacePath)
	{
		var source = GetSelectedSource();
		if (!source.HasValue)
		{
			FinishImport(TranslationServer.Translate("No source map is selected."));
			return;
		}

		var selectedTypeNames = new List<string>();
		var selectedNamespaces = new List<string>();
		var replacePaths = new List<string>();

		if (_codeScope == CodeScope.Namespaces)
		{
			foreach (var candidate in _codeNamespaceCandidates)
			{
				if (!selectedKeys.Contains(candidate.SelectionKey)) continue;

				selectedNamespaces.Add(candidate.NamespaceName);
				if (candidate.WillReplace)
				{
					replacePaths.AddRange(candidate.TargetRelativePaths);
				}
			}
		}
		else
		{
			foreach (var candidate in _codeTypeCandidates)
			{
				if (!selectedKeys.Contains(candidate.SelectionKey)) continue;

				selectedTypeNames.Add(candidate.FullName);
				if (candidate.WillReplace)
				{
					replacePaths.Add(candidate.RelativePath);
				}
			}
		}

		var distinctReplacePaths = replacePaths
			.Distinct(StringComparer.OrdinalIgnoreCase)
			.OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
			.ToList();

		if (distinctReplacePaths.Count > 0)
		{
			string fileList = string.Join("\n • ", distinctReplacePaths);

			string message = string.Format(
				TranslationServer.Translate("The following code file(s) already exist in this map with different content and will be replaced:\n\n • {0}\n\nReplace the selected files?"),
				fileList);

			if (Hud == null)
			{
				ExecuteCodeImport(source.Value, selectedTypeNames, selectedNamespaces, workspacePath);
				return;
			}

			Hud.ShowConfirmationDialog(
				message,
				() => ExecuteCodeImport(source.Value, selectedTypeNames, selectedNamespaces, workspacePath),
				confirmText: "REPLACE",
				cancelText: "CANCEL");
			return;
		}

		ExecuteCodeImport(source.Value, selectedTypeNames, selectedNamespaces, workspacePath);
	}

	private void ExecuteCodeImport(MapAssetImportSource source, IReadOnlyList<string> selectedTypeNames, IReadOnlyList<string> selectedNamespaces, string workspacePath)
	{
		MapWorkspaceService.EnsureSourceMapAttributionAttribute(workspacePath);

		var summary = _codeScope == CodeScope.Namespaces
			? GameContext.MapCodeImportService.ImportNamespaces(source, workspacePath, selectedNamespaces)
			: GameContext.MapCodeImportService.ImportTypes(source, workspacePath, selectedTypeNames);

		if (summary.FailedTypes.Count > 0)
		{
			GD.Print($"[ImportFromMapDialog] Code classes not imported: {string.Join(", ", summary.FailedTypes)}");
		}

		FinishImport(BuildCodeSummaryText(summary));
	}

	private static string BuildCodeSummaryText(MapCodeImportSummary summary)
	{
		return string.Format(
			TranslationServer.Translate("Imported {0} classes with {1} dependencies, replaced {2} existing files, {3} failed."),
			summary.ImportedTypes.Count,
			summary.ImportedDependencies.Count,
			summary.ReplacedFiles.Count,
			summary.FailedTypes.Count);
	}

	private static string BuildAssetSummaryText(MapAssetImportSummary summary)
	{
		return string.Format(
			TranslationServer.Translate("Imported {0}, skipped {1} already present, {2} missing, {3} name conflicts, {4} failed."),
			summary.Imported.Count,
			summary.Skipped.Count,
			summary.Missing.Count,
			summary.Conflicts.Count,
			summary.Failed.Count);
	}

	private static string BuildTemplateSummaryText(MapTemplateImportSummary summary)
	{
		return string.Format(
			TranslationServer.Translate("Imported {0} templates with {1} assets, skipped {2} already present, {3} assets missing, {4} failed."),
			summary.ImportedTemplates.Count,
			summary.ImportedAssets.Count,
			summary.SkippedTemplates.Count,
			summary.MissingAssets.Count,
			summary.FailedTemplates.Count + summary.FailedAssets.Count);
	}

	public override void CloseDialog()
	{
		base.CloseDialog();
	}
}
