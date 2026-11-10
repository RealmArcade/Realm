using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using GdUnit4;
using Godot;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Realm.Client.Services;
using Realm.Client.UI;
using Realm.Client.UI.MapEditor;
using Realm.Shared.Distribution;
using Environment = System.Environment;

namespace Realm.Client.Tests;

/// <summary>the import dialog driving a template import.</summary>
public partial class MapImportVerificationTests
	{
	[TestCase]
	[RequireGodotRuntime]
	public void TestImportDialogFiltersTemplatesAndCodeAndImportsTemplates()
	{
		string root = CreateTempRoot("import_dialog");
		try
		{
			string destinationPath = CreateDestinationWorkspace(root);
			MapAssetImportSource source = CreateSourceMap(root, SourceTitle, SourceVersion, BuildTemplateMetadata(), BuildCodeFiles());

			MapWorkspaceService.SetActiveWorkspacePathForTesting(destinationPath);
			GameContext.EnsureServices();

			var dialog = new ImportFromMapDialog(null);
			try
			{
				var sources = GetPrivateField<List<MapAssetImportSource>>(dialog, "_sources");
				sources.Add(source);

				var sourceOption = dialog.FindChild("OptMapSource_OptionButton", true, false) as OptionButton;
				Assertions.AssertThat(sourceOption != null).IsTrue();
				sourceOption!.AddItem($"{source.MapTitle} v{source.MapVersion}", 0);
				sourceOption.Select(0);

				InvokePrivate(dialog, "ReloadCandidates");

				var kindOption = dialog.FindChild("OptImportKind_OptionButton", true, false) as OptionButton;
				Assertions.AssertThat(kindOption != null).IsTrue();
				Assertions.AssertThat(kindOption!.ItemCount).IsEqual(3);
				Assertions.AssertThat(kindOption.GetItemText(0)).IsEqual("Assets");
				Assertions.AssertThat(kindOption.GetItemText(1)).IsEqual("Templates");
				Assertions.AssertThat(kindOption.GetItemText(2)).IsEqual("Code");

				SelectOption(kindOption, 1);
				VBoxContainer rowList = GetRowList(dialog);

				Assertions.AssertThat(GetRowLabels(rowList).Any(label => label.Contains("Unit: Source Knight"))).IsTrue();
				Assertions.AssertThat(GetRowLabels(rowList).Any(label => label.Contains("Weapon: Source Blade"))).IsTrue();
				Assertions.AssertThat(GetRowLabels(rowList).Any(label => label.Contains("Prop: Source Rock"))).IsTrue();

				LineEdit search = dialog.FindChild("TxtSearch_LineEdit", true, false) as LineEdit ?? throw new InvalidOperationException("Search field is missing.");
				SetSearchText(search, "Source Blade");

				Assertions.AssertThat(GetRowLabels(rowList).Count).IsEqual(1);
				Assertions.AssertThat(GetRowLabels(rowList)[0]).Contains("Weapon: Source Blade");

				SetSearchText(search, string.Empty);

				SelectOption(kindOption, 2);
				Assertions.AssertThat(GetRowLabels(rowList).Any(label => label.Contains("Ancient.Core.AncientKnight"))).IsTrue();
				Assertions.AssertThat(GetRowLabels(rowList).Any(label => label.Contains("Ancient.Other.AncientUnrelated"))).IsTrue();

				SetSearchText(search, "Ancient.Other");
				Assertions.AssertThat(GetRowLabels(rowList).Count).IsEqual(1);
				Assertions.AssertThat(GetRowLabels(rowList)[0]).Contains("Ancient.Other.AncientUnrelated");

				SetSearchText(search, string.Empty);

				var codeScope = dialog.FindChild("OptCodeScope_OptionButton", true, false) as OptionButton;
				Assertions.AssertThat(codeScope != null).IsTrue();
				Assertions.AssertThat(codeScope!.Visible).IsTrue();
				SelectOption(codeScope, 1);

				Assertions.AssertThat(GetRowLabels(rowList).Any(label => label.Contains("Ancient.Core (8 classes)")))
					.OverrideFailureMessage($"Namespace rows: {string.Join(" | ", GetRowLabels(rowList))}")
					.IsTrue();
				Assertions.AssertThat(GetRowLabels(rowList).Any(label => label.Contains("Ancient.Other (1 classes)")))
					.OverrideFailureMessage($"Namespace rows: {string.Join(" | ", GetRowLabels(rowList))}")
					.IsTrue();

				SetSearchText(search, "Ancient.Other");
				Assertions.AssertThat(GetRowLabels(rowList).Count).IsEqual(1);
				SetSearchText(search, string.Empty);

				SelectOption(kindOption, 1);
				InvokePrivate(dialog, "OnImportSelectedPressed");

				var summaryLabel = dialog.FindChild("LblSummary_Label", true, false) as Label;
				Assertions.AssertThat(summaryLabel != null).IsTrue();
				Assertions.AssertThat(summaryLabel!.Text.Contains("Imported 7 templates")).OverrideFailureMessage($"Unexpected import summary: {summaryLabel.Text}").IsTrue();

				JsonObject destinationMetadata = ReadJsonObject(Path.Combine(destinationPath, "metadata.json"));
				JsonArray importedUnits = destinationMetadata["Templates"]!["Units"]!.AsArray();
				Assertions.AssertThat(importedUnits.Count).IsEqual(1);
				Assertions.AssertThat(importedUnits[0]!["Attribution_ImportedFromMap"]!.GetValue<string>()).IsEqual(SourceAttribution);
				Assertions.AssertThat(destinationMetadata["Templates"]!["Weapons"]!.AsArray().Count).IsEqual(1);

				SelectOption(kindOption, 2);
				SelectOption(codeScope!, 0);
				InvokePrivate(dialog, "OnSelectNonePressed");

				var knightRow = rowList.FindChild("ImportRow_Ancient_Core_AncientKnight", true, false) as CheckBox;
				Assertions.AssertThat(knightRow != null).OverrideFailureMessage("The knight class row is missing.").IsTrue();
				knightRow!.ButtonPressed = true;

				InvokePrivate(dialog, "OnImportSelectedPressed");
				Assertions.AssertThat(summaryLabel.Text.Contains("Imported 1 classes with 6 dependencies"))
					.OverrideFailureMessage($"Unexpected code import summary: {summaryLabel.Text}")
					.IsTrue();

				string importedKnightPath = Path.Combine(destinationPath, "imports", SourceTitle, "Ancient", "Core", "AncientKnight.cs");
				Assertions.AssertThat(File.Exists(importedKnightPath)).OverrideFailureMessage($"Missing {importedKnightPath}").IsTrue();
				Assertions.AssertThat(GetSourceMapAttributeValues(importedKnightPath).Count).IsEqual(2);

				VerifyAttributionPopover(dialog);
			}
			finally
			{
				dialog.Free();
			}
		}
		finally
		{
			MapWorkspaceService.SetActiveWorkspacePathForTesting(null);
			DeleteTempRoot(root);
		}
	}
}
