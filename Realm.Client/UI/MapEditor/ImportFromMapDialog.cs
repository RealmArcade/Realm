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
	private OptionButton _optImportKind = null!;
	private OptionButton _optMapSource = null!;
	private OptionButton _optCodeScope = null!;
	private LineEdit _txtSearch = null!;
	private CheckBox _chkOnlyMissing = null!;
	private Label _lblItemCount = null!;
	private Label _lblSummary = null!;
	private ScrollContainer _assetScroll = null!;
	private VBoxContainer _assetList = null!;
	private Button _btnSelectAll = null!;
	private Button _btnSelectNone = null!;
	private Button _btnImportSelected = null!;
	private Button _btnAddArchive = null!;
	private Button _btnAddFolder = null!;

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

	public ImportFromMapDialog(MapEditorHUD? hud)
		: base(hud, TranslationServer.Translate("Import From Map"), new Vector2(760, 680))
	{
		SetUncompressedPanelTexture("res://Assets/UI/map_editor_assets_importer.webp", 30, 45, 70, 70);
		BuildControls();
		SetFooterCloseOnly();
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
			GameLog.LogError($"[ImportFromMapDialog] Failed to add '{sourcePath}': {exception.Message}");
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
		if (!string.IsNullOrEmpty(GameContext.EditorService._editingDirectory))
		{
			return GameContext.EditorService._editingDirectory;
		}

		return MapWorkspaceService.GetActiveWorkspacePath();
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
			GameLog.LogToConsole($"[ImportFromMapDialog] Assets not found in storage: {string.Join(", ", summary.Missing)}");
		}

		if (summary.Conflicts.Count > 0)
		{
			GameLog.LogToConsole($"[ImportFromMapDialog] Assets skipped due to name conflicts: {string.Join(", ", summary.Conflicts)}");
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
			GameLog.LogToConsole($"[ImportFromMapDialog] Template assets not found in storage: {string.Join(", ", summary.MissingAssets)}");
		}

		if (summary.ConflictAssets.Count > 0)
		{
			GameLog.LogToConsole($"[ImportFromMapDialog] Template assets skipped due to name conflicts: {string.Join(", ", summary.ConflictAssets)}");
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

			RequireHud.ShowConfirmationDialog(
				message,
				() => ExecuteCodeImport(source.Value, selectedTypeNames, selectedNamespaces, workspacePath),
				confirmText: TranslationServer.Translate("REPLACE"),
				cancelText: TranslationServer.Translate("CANCEL"));
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
			GameLog.LogToConsole($"[ImportFromMapDialog] Code classes not imported: {string.Join(", ", summary.FailedTypes)}");
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
