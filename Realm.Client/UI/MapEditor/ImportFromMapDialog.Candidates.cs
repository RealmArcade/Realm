// Turning the selected source into the rows the user sees: clear, resolve the default code scope,
// reload the candidate lists, then rebuild the visible rows for whichever kind is selected, filtered
// by the search text. The filter matchers live with the row builders because they only serve them.

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

}