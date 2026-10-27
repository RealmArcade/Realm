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

[TestSuite]
public class MapImportVerificationTests
{
	private const BindingFlags PrivateInstanceFlags = BindingFlags.Instance | BindingFlags.NonPublic;
	private const string SourceTitle = "Ancient Realm";
	private const string SourceVersion = "1.0.0";
	private const string SourceAttribution = "Ancient Realm v1.0.0";

	[TestCase]
	public void TestTemplateImportCopiesRecursiveDependencies()
	{
		string root = CreateTempRoot("template_recursive");
		try
		{
			string destinationPath = CreateDestinationWorkspace(root);
			MapAssetImportSource source = CreateSourceMap(root, SourceTitle, SourceVersion, BuildTemplateMetadata(), BuildCodeFiles());

			GameContext.EnsureServices();
			MapTemplateImportService service = GameContext.MapTemplateImportService;
			IReadOnlyList<MapTemplateImportCandidate> candidates = service.EnumerateTemplates(source, destinationPath);
			MapTemplateImportCandidate? knight = candidates.SingleOrDefault(candidate => candidate.Identity == "unit/source_knight");

			Assertions.AssertThat(knight != null).OverrideFailureMessage("The source unit template was not enumerated.").IsTrue();
			Assertions.AssertThat(knight!.ObjectType).IsEqual("Unit");
			Assertions.AssertThat(knight.AlreadyPresent).IsFalse();
			Assertions.AssertThat(candidates.Any(candidate => candidate.Identity == "prop/source_rock")).IsTrue();

			MapTemplateImportSummary summary = service.ImportTemplates(source, destinationPath, new[] { knight });

			Assertions.AssertThat(summary.FailedTemplates).OverrideFailureMessage($"Failed templates: {string.Join(", ", summary.FailedTemplates)}").IsEmpty();
			Assertions.AssertThat(summary.ImportedTemplates.Count)
				.OverrideFailureMessage($"Imported templates: {string.Join(", ", summary.ImportedTemplates)}")
				.IsEqual(6);

			JsonObject destinationMetadata = ReadJsonObject(Path.Combine(destinationPath, "metadata.json"));
			JsonObject templates = destinationMetadata["Templates"]!.AsObject();

			AssertSingleImportedTemplate(templates, "Units", "unit/source_knight");
			AssertSingleImportedTemplate(templates, "Weapons", "weapon/source_blade");
			AssertSingleImportedTemplate(templates, "Abilities", "ability/source_cleave");
			AssertSingleImportedTemplate(templates, "Abilities", "ability/source_guard");
			AssertSingleImportedTemplate(templates, "Vfx", "vfx/source_glow");

			JsonObject decals = destinationMetadata["decals"]!.AsObject();
			Assertions.AssertThat(decals.ContainsKey("source_scorch")).IsTrue();
			Assertions.AssertThat(decals["source_scorch"]!.AsObject()["Attribution_ImportedFromMap"]!.GetValue<string>()).IsEqual(SourceAttribution);

			Assertions.AssertThat(templates["Props"] is null || templates["Props"]!.AsArray().Count == 0)
				.OverrideFailureMessage("A template that was not referenced by the selection was imported.")
				.IsTrue();

			JsonArray importedWeapons = templates["Weapons"]!.AsArray();
			Assertions.AssertThat(importedWeapons.Count).IsEqual(1);
			Assertions.AssertThat(importedWeapons[0]!["Abilities"]!.AsArray().Count).IsEqual(1);
			Assertions.AssertThat(importedWeapons[0]!["Abilities"]![0]!.GetValue<string>()).IsEqual("ability/source_cleave");

			JsonArray importedUnits = templates["Units"]!.AsArray();
			Assertions.AssertThat(importedUnits[0]!["VisualEffect"]!.GetValue<string>()).IsEqual("vfx/source_glow");

			MapTemplateImportSummary repeatSummary = service.ImportTemplates(source, destinationPath, new[] { knight });
			Assertions.AssertThat(repeatSummary.ImportedTemplates).IsEmpty();
			Assertions.AssertThat(repeatSummary.SkippedTemplates.Count).IsEqual(6);

			IReadOnlyList<MapTemplateImportCandidate> refreshedCandidates = service.EnumerateTemplates(source, destinationPath);
			Assertions.AssertThat(refreshedCandidates.Single(candidate => candidate.Identity == "unit/source_knight").AlreadyPresent).IsTrue();
		}
		finally
		{
			DeleteTempRoot(root);
		}
	}

	[TestCase]
	public void TestCodeImportCopiesDependenciesAndCompiles()
	{
		string root = CreateTempRoot("code_recursive");
		try
		{
			string destinationPath = CreateDestinationWorkspace(root);
			MapAssetImportSource source = CreateSourceMap(root, SourceTitle, SourceVersion, BuildEmptyMetadata(), BuildCodeFiles());

			GameContext.EnsureServices();
			MapCodeImportService service = GameContext.MapCodeImportService;
			IReadOnlyList<MapCodeTypeCandidate> types = service.EnumerateTypes(source, destinationPath);
			IReadOnlyList<MapCodeNamespaceCandidate> namespaces = service.EnumerateNamespaces(source, destinationPath);

			Assertions.AssertThat(types.Any(candidate => candidate.FullName == "Ancient.Core.AncientKnight")).IsTrue();
			Assertions.AssertThat(types.Any(candidate => candidate.FullName == "Ancient.Core.Extra.AncientBanner"))
				.OverrideFailureMessage($"Enumerated types: {string.Join(", ", types.Select(candidate => candidate.FullName))}")
				.IsTrue();
			Assertions.AssertThat(types.Any(candidate => candidate.FullName == "Ancient.Other.AncientUnrelated"))
				.OverrideFailureMessage($"Enumerated types: {string.Join(", ", types.Select(candidate => candidate.FullName))}")
				.IsTrue();
			Assertions.AssertThat(types.Any(candidate => candidate.ClassName == "MapScript")).IsFalse();

			MapCodeNamespaceCandidate? coreNamespace = namespaces.SingleOrDefault(candidate => candidate.NamespaceName == "Ancient.Core");
			Assertions.AssertThat(coreNamespace != null).IsTrue();
			Assertions.AssertThat(coreNamespace!.TypeCount).IsEqual(8);

			MapCodeImportSummary summary = service.ImportTypes(source, destinationPath, new[] { "Ancient.Core.AncientKnight" });

			Assertions.AssertThat(summary.FailedTypes).OverrideFailureMessage($"Failed types: {string.Join(", ", summary.FailedTypes)}").IsEmpty();
			Assertions.AssertThat(summary.ImportedTypes.Count).IsEqual(1);
			Assertions.AssertThat(summary.ImportedTypes[0]).IsEqual("Ancient.Core.AncientKnight");
			Assertions.AssertThat(summary.ImportedDependencies.Count)
				.OverrideFailureMessage($"Imported dependencies: {string.Join(", ", summary.ImportedDependencies)}")
				.IsEqual(6);

			string knightPath = Path.Combine(destinationPath, "imports", SourceTitle, "Ancient", "Core", "AncientKnight.cs");
			string weaponPath = Path.Combine(destinationPath, "imports", SourceTitle, "Ancient", "Core", "AncientWeapon.cs");
			string archetypePath = Path.Combine(destinationPath, "imports", SourceTitle, "Ancient", "Core", "AncientArchetype.cs");
			string enumPath = Path.Combine(destinationPath, "imports", SourceTitle, "Ancient", "Core", "AncientDamageType.cs");

			Assertions.AssertThat(File.Exists(knightPath)).OverrideFailureMessage($"Missing {knightPath}").IsTrue();
			Assertions.AssertThat(File.Exists(weaponPath)).OverrideFailureMessage($"Missing {weaponPath}").IsTrue();
			Assertions.AssertThat(File.Exists(archetypePath)).OverrideFailureMessage($"Missing {archetypePath}").IsTrue();

			string knightContent = File.ReadAllText(knightPath);
			Assertions.AssertThat(knightContent.Contains("[Attribution_SourceMap(\"" + SourceTitle + "\")]")).IsTrue();
			Assertions.AssertThat(knightContent.Contains("/// <summary>An imported knight.</summary>"))
				.OverrideFailureMessage($"Generated knight file:{Environment.NewLine}{knightContent}")
				.IsTrue();
			Assertions.AssertThat(knightContent.Contains("namespace Ancient.Core;")).IsTrue();
			Assertions.AssertThat(knightContent.Contains("using Realm.Maps;")).IsTrue();
			Assertions.AssertThat(knightContent.Contains("AncientArchetype")).IsTrue();

			List<string> knightAttributes = GetSourceMapAttributeValues(knightPath);
			Assertions.AssertThat(knightAttributes.Count).IsEqual(2);
			Assertions.AssertThat(knightAttributes[0]).Contains(SourceTitle);
			Assertions.AssertThat(knightAttributes[1]).Contains("Older Map");

			Assertions.AssertThat(GetSourceMapAttributeValues(archetypePath).Count).IsEqual(1);
			Assertions.AssertThat(GetSourceMapAttributeValues(enumPath)).IsEmpty();

			IReadOnlyList<string> compilationErrors = CompileWorkspaceSources(destinationPath);
			Assertions.AssertThat(compilationErrors)
				.OverrideFailureMessage($"Imported code does not compile: {string.Join(Environment.NewLine, compilationErrors)}")
				.IsEmpty();
		}
		finally
		{
			DeleteTempRoot(root);
		}
	}

	[TestCase]
	public void TestNamespaceImportCopiesNamespaceTree()
	{
		string root = CreateTempRoot("code_namespaces");
		try
		{
			string destinationPath = CreateDestinationWorkspace(root);
			MapAssetImportSource source = CreateSourceMap(root, SourceTitle, SourceVersion, BuildEmptyMetadata(), BuildCodeFiles());

			GameContext.EnsureServices();
			MapCodeImportService service = GameContext.MapCodeImportService;
			MapCodeImportSummary summary = service.ImportNamespaces(source, destinationPath, new[] { "Ancient.Core" });

			Assertions.AssertThat(summary.FailedTypes).IsEmpty();
			Assertions.AssertThat(summary.ImportedTypes.Count).IsEqual(8);
			Assertions.AssertThat(summary.ImportedDependencies).IsEmpty();

			string bannerPath = Path.Combine(destinationPath, "imports", SourceTitle, "Ancient", "Core", "Extra", "AncientBanner.cs");
			Assertions.AssertThat(File.Exists(bannerPath)).OverrideFailureMessage("The sub-namespace class was not imported.").IsTrue();
			Assertions.AssertThat(File.Exists(Path.Combine(destinationPath, "imports", SourceTitle, "Ancient", "Other", "AncientUnrelated.cs"))).IsFalse();

			IReadOnlyList<string> compilationErrors = CompileWorkspaceSources(destinationPath);
			Assertions.AssertThat(compilationErrors)
				.OverrideFailureMessage($"Imported namespace does not compile: {string.Join(Environment.NewLine, compilationErrors)}")
				.IsEmpty();
		}
		finally
		{
			DeleteTempRoot(root);
		}
	}

	[TestCase]
	public void TestCodeImportReplacesExistingFileWholesale()
	{
		string root = CreateTempRoot("code_replace");
		try
		{
			string destinationPath = CreateDestinationWorkspace(root);
			MapAssetImportSource source = CreateSourceMap(root, SourceTitle, SourceVersion, BuildEmptyMetadata(), BuildCodeFiles());

			GameContext.EnsureServices();
			MapCodeImportService service = GameContext.MapCodeImportService;
			MapCodeImportSummary firstSummary = service.ImportTypes(source, destinationPath, new[] { "Ancient.Core.AncientKnight" });
			Assertions.AssertThat(firstSummary.ReplacedFiles).IsEmpty();

			string knightPath = Path.Combine(destinationPath, "imports", SourceTitle, "Ancient", "Core", "AncientKnight.cs");
			File.WriteAllText(knightPath, "// stale marker\n" + File.ReadAllText(knightPath));

			IReadOnlyList<MapCodeTypeCandidate> candidates = service.EnumerateTypes(source, destinationPath);
			MapCodeTypeCandidate knightCandidate = candidates.Single(candidate => candidate.FullName == "Ancient.Core.AncientKnight");
			Assertions.AssertThat(knightCandidate.WillReplace).IsTrue();
			Assertions.AssertThat(knightCandidate.RelativePath.Replace('\\', '/')).IsEqual($"imports/{SourceTitle}/Ancient/Core/AncientKnight.cs");

			MapCodeImportSummary secondSummary = service.ImportTypes(source, destinationPath, new[] { "Ancient.Core.AncientKnight" });

			Assertions.AssertThat(secondSummary.ReplacedFiles.Count).IsEqual(1);
			Assertions.AssertThat(secondSummary.ReplacedFiles[0].Replace('\\', '/')).IsEqual(knightCandidate.RelativePath);
			Assertions.AssertThat(File.ReadAllText(knightPath).Contains("stale marker")).IsFalse();
			Assertions.AssertThat(Directory.GetFiles(Path.Combine(destinationPath, "imports", SourceTitle, "Ancient", "Core"), "AncientKnight*.cs").Length).IsEqual(1);
		}
		finally
		{
			DeleteTempRoot(root);
		}
	}

	[TestCase]
	public void TestCodeImportKeepsUnrelatedDestinationDependency()
	{
		string root = CreateTempRoot("code_existing_dependency");
		try
		{
			string destinationPath = CreateDestinationWorkspace(root);
			MapAssetImportSource source = CreateSourceMap(root, SourceTitle, SourceVersion, BuildEmptyMetadata(), BuildCodeFiles());

			string existingWeaponPath = Path.Combine(destinationPath, "imports", "Handwritten", "AncientWeapon.cs");
			Directory.CreateDirectory(Path.GetDirectoryName(existingWeaponPath)!);
			File.WriteAllText(existingWeaponPath, "namespace Ancient.Core;\n\npublic class AncientWeapon\n{\n\tpublic string HandwrittenMarker = \"keep-me\";\n}\n");

			GameContext.EnsureServices();
			MapCodeImportSummary summary = GameContext.MapCodeImportService.ImportTypes(source, destinationPath, new[] { "Ancient.Core.AncientKnight" });

			Assertions.AssertThat(summary.SkippedExistingDependencies.Contains("Ancient.Core.AncientWeapon")).IsTrue();
			Assertions.AssertThat(File.ReadAllText(existingWeaponPath).Contains("keep-me")).IsTrue();
			Assertions.AssertThat(File.Exists(Path.Combine(destinationPath, "imports", SourceTitle, "Ancient", "Core", "AncientWeapon.cs"))).IsFalse();
			Assertions.AssertThat(summary.FailedTypes).IsEmpty();

			IReadOnlyList<string> compilationErrors = CompileWorkspaceSources(destinationPath);
			Assertions.AssertThat(compilationErrors)
				.OverrideFailureMessage($"Imported code does not compile with the existing dependency: {string.Join(Environment.NewLine, compilationErrors)}")
				.IsEmpty();
		}
		finally
		{
			DeleteTempRoot(root);
		}
	}

	[TestCase]
	public void TestAttributionCollectionWritesSortedManifestField()
	{
		string root = CreateTempRoot("attribution");
		try
		{
			string workspacePath = CreateDestinationWorkspace(root);
			string metadataPath = Path.Combine(workspacePath, "metadata.json");
			File.WriteAllText(metadataPath, """
			{
			  "GameBuildNumber": "v0.0.4",
			  "Templates": {
			    "Units": [
			      { "TemplateID": "unit/imported", "Name": "Imported", "Attribution_ImportedFromMap": "Zeta Map" },
			      { "TemplateID": "unit/also_imported", "Name": "Also Imported", "Attribution_ImportedFromMap": "Alpha Map" }
			    ]
			  }
			}
			""");

			WriteTextFile(workspacePath, "MapScript.cs", """
			namespace Realm.Maps;

			[Attribution_SourceMap("alpha map")]
			public class MapScript
			{
			}
			""");

			WriteTextFile(workspacePath, "imports/Source Map/Ancient/Core/AncientKnight.cs", """
			using Realm.Maps;

			namespace Ancient.Core;

			[Attribution_SourceMap("alpha map")]
			[Attribution_SourceMap("Beta Map")]
			public class AncientKnight
			{
			}
			""");

			WriteTextFile(workspacePath, "bin/Ignored.cs", """
			[Attribution_SourceMap("Ignored Map")]
			public class Ignored
			{
			}
			""");

			WriteTextFile(workspacePath, "obj/IgnoredToo.cs", """
			[Attribution_SourceMap("Also Ignored")]
			public class IgnoredToo
			{
			}
			""");

			GameContext.EnsureServices();
			MapAttributionService service = GameContext.MapAttributionService;
			IReadOnlyList<string> collected = service.CollectSortedSourceMapNames(workspacePath);

			Assertions.AssertThat(string.Join("|", collected)).IsEqual("Alpha Map|Beta Map|Zeta Map");

			var manifest = new MapManifest { MapName = "Destination" };
			manifest.SaveToFile(Path.Combine(workspacePath, "manifest.json"));

			service.UpdateManifestAttribution(workspacePath);
			MapManifest reloaded = MapManifest.LoadFromFile(Path.Combine(workspacePath, "manifest.json"));
			Assertions.AssertThat(string.Join("|", reloaded.Attribution_SourceMap)).IsEqual("Alpha Map|Beta Map|Zeta Map");

			var appliedManifest = new MapManifest();
			service.ApplyAttribution(appliedManifest, workspacePath);
			Assertions.AssertThat(string.Join("|", appliedManifest.Attribution_SourceMap)).IsEqual("Alpha Map|Beta Map|Zeta Map");

			string schema = MapManifest.GenerateJsonSchema();
			Assertions.AssertThat(schema.Contains("Attribution_SourceMap")).IsTrue();
		}
		finally
		{
			DeleteTempRoot(root);
		}
	}

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

	private static void VerifyAttributionPopover(Control anchor)
	{
		var host = new Control { Name = "AttributionPopoverVerificationHost" };
		var sceneTree = Engine.GetMainLoop() as SceneTree;
		sceneTree?.Root.AddChild(host);

		var popover = new AttributionPopover();
		host.AddChild(popover);

		try
		{
			popover.ShowFor(anchor, new[] { "Zeta Map", "Alpha Map", "alpha map" });
			Assertions.AssertThat(popover.Visible).IsTrue();

			var sourceList = popover.FindChild("SourceList_VBoxContainer", true, false) as VBoxContainer;
			Assertions.AssertThat(sourceList != null).IsTrue();

			var labels = new List<string>();
			foreach (Node child in sourceList!.GetChildren())
			{
				if (child is PanelContainer row && row.GetChildCount() > 0 && row.GetChild(0) is Label label)
				{
					labels.Add(label.Text);
				}
			}

			Assertions.AssertThat(string.Join("|", labels)).OverrideFailureMessage($"Popover labels: {string.Join("|", labels)}").IsEqual("Alpha Map|Zeta Map");

			var emptyLabel = popover.FindChild("EmptyLabel_Label", true, false) as Label;
			Assertions.AssertThat(emptyLabel != null).IsTrue();
			Assertions.AssertThat(emptyLabel!.Visible).IsFalse();

			popover.ShowFor(anchor, Array.Empty<string>());
			Assertions.AssertThat(emptyLabel.Visible).IsTrue();

			popover.Close();
			Assertions.AssertThat(popover.Visible).IsFalse();
		}
		finally
		{
			host.Free();
		}
	}

	private static void AssertSingleImportedTemplate(JsonObject templates, string sectionKey, string templateIdentity)
	{
		JsonArray entries = templates[sectionKey]!.AsArray();
		JsonObject? entry = entries
			.Select(node => node!.AsObject())
			.FirstOrDefault(candidate => candidate["TemplateID"]?.GetValue<string>() == templateIdentity);
		if (entry == null && sectionKey == "Vfx")
		{
			entry = entries
				.Select(node => node!.AsObject())
				.FirstOrDefault(candidate => candidate["VfxId"]?.GetValue<string>() == templateIdentity);
		}

		Assertions.AssertThat(entry != null).OverrideFailureMessage($"{sectionKey} did not contain {templateIdentity}.").IsTrue();
		Assertions.AssertThat(entry!["Attribution_ImportedFromMap"]?.GetValue<string>()).IsEqual(SourceAttribution);
	}

	private static JsonObject BuildTemplateMetadata()
	{
		var metadata = new JsonObject { ["GameBuildNumber"] = "v0.0.4" };
		var templates = new JsonObject
		{
			["Units"] = new JsonArray
			{
				new JsonObject
				{
					["TemplateID"] = "unit/source_knight",
					["Name"] = "Source Knight",
					["Weapons"] = new JsonArray { "weapon/source_blade" },
					["Abilities"] = new JsonArray { "ability/source_guard" },
					["VisualEffect"] = "vfx/source_glow"
				}
			},
			["Weapons"] = new JsonArray
			{
				new JsonObject
				{
					["TemplateID"] = "weapon/source_blade",
					["Name"] = "Source Blade",
					["Abilities"] = new JsonArray { "ability/source_cleave" }
				}
			},
			["Abilities"] = new JsonArray
			{
				new JsonObject
				{
					["TemplateID"] = "ability/source_cleave",
					["Name"] = "Source Cleave"
				},
				new JsonObject
				{
					["TemplateID"] = "ability/source_guard",
					["Name"] = "Source Guard"
				}
			},
			["Vfx"] = new JsonArray
			{
				new JsonObject
				{
					["VfxId"] = "vfx/source_glow",
					["Name"] = "Source Glow",
					["DecalId"] = "source_scorch"
				}
			},
			["Props"] = new JsonArray
			{
				new JsonObject
				{
					["TemplateID"] = "prop/source_rock",
					["Name"] = "Source Rock"
				}
			}
		};

		metadata["Templates"] = templates;
		metadata["decals"] = new JsonObject
		{
			["source_scorch"] = new JsonObject { ["Name"] = "Source Scorch" }
		};

		return metadata;
	}

	private static JsonObject BuildEmptyMetadata()
	{
		return new JsonObject
		{
			["GameBuildNumber"] = "v0.0.4",
			["Templates"] = new JsonObject()
		};
	}

	private static Dictionary<string, string> BuildCodeFiles()
	{
		return new Dictionary<string, string>
		{
			["MapScript.cs"] = """
			namespace Realm.Maps;

			using Realm.MapAPI;

			public class MapScript : IWasmModule
			{
				public void Initialize(IGameAPI api)
				{
				}

				public void Update(IGameAPI api, float delta)
				{
				}
			}
			""",
			["AncientUnitBase.cs"] = """
			namespace Ancient.Core;

			public abstract class AncientUnitBase
			{
				protected AncientStats Stats = new AncientStats();
			}
			""",
			["AncientStats.cs"] = """
			namespace Ancient.Core;

			public class AncientStats
			{
				public int Strength;
			}
			""",
			["AncientDamageType.cs"] = """
			namespace Ancient.Core;

			public enum AncientDamageType
			{
				Physical,
				Fire
			}
			""",
			["AncientAbility.cs"] = """
			namespace Ancient.Core;

			public class AncientAbility
			{
				public string Name = string.Empty;
			}
			""",
			["AncientWeapon.cs"] = """
			namespace Ancient.Core;

			using System.Collections.Generic;

			public class AncientWeapon
			{
				public AncientDamageType DamageType;
				public List<AncientAbility> Abilities = new List<AncientAbility>();
			}
			""",
			["AncientArchetype.cs"] = """
			namespace Ancient.Core;

			public record AncientArchetype(string Name);
			""",
			["AncientKnight.cs"] = """
			namespace Ancient.Core;

			using System.Collections.Generic;
			using Realm.Maps;

			/// <summary>An imported knight.</summary>
			[Attribution_SourceMap("Older Map")]
			public class AncientKnight : AncientUnitBase
			{
				public AncientWeapon PrimaryWeapon = new AncientWeapon();
				public List<AncientWeapon> SpareWeapons = new List<AncientWeapon>();
				public AncientArchetype Archetype = new AncientArchetype("Knight");
			}
			""",
			["AncientBanner.cs"] = """
			namespace Ancient.Core.Extra;

			public class AncientBanner : Ancient.Core.AncientUnitBase
			{
			}
			""",
			["AncientUnrelated.cs"] = """
			namespace Ancient.Other;

			public class AncientUnrelated
			{
			}
			"""
		};
	}

	private static MapAssetImportSource CreateSourceMap(string root, string title, string version, JsonObject metadata, IReadOnlyDictionary<string, string> sourceFiles)
	{
		string mapPath = Path.Combine(root, "source_map");
		Directory.CreateDirectory(mapPath);
		File.WriteAllText(Path.Combine(mapPath, "metadata.json"), metadata.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));

		var manifest = new MapManifest { MapName = title, Version = version };
		manifest.Files["metadata.json"] = "verification-hash";

		foreach (KeyValuePair<string, string> sourceFile in sourceFiles)
		{
			WriteTextFile(mapPath, sourceFile.Key, sourceFile.Value);
			manifest.Files[sourceFile.Key] = "verification-hash";
		}

		string manifestPath = Path.Combine(mapPath, "manifest.json");
		manifest.SaveToFile(manifestPath);
		return new MapAssetImportSource(title, version, manifestPath);
	}

	private static string CreateDestinationWorkspace(string root)
	{
		string destinationPath = Path.Combine(root, "destination_map");
		Directory.CreateDirectory(destinationPath);
		File.WriteAllText(Path.Combine(destinationPath, "metadata.json"), BuildEmptyMetadata().ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
		return destinationPath;
	}

	private static string CreateTempRoot(string prefix)
	{
		string root = Path.Combine(Path.GetTempPath(), "realm_import_verification", $"{prefix}_{Guid.NewGuid():N}");
		Directory.CreateDirectory(root);
		return root;
	}

	private static void DeleteTempRoot(string root)
	{
		if (!Directory.Exists(root)) return;

		try
		{
			Directory.Delete(root, true);
		}
		catch (Exception exception)
		{
			GD.PrintErr($"[MapImportVerificationTests] Failed to remove '{root}': {exception.Message}");
		}
	}

	private static void WriteTextFile(string root, string relativePath, string content)
	{
		string path = Path.Combine(root, relativePath.Replace('/', Path.DirectorySeparatorChar));
		string? directory = Path.GetDirectoryName(path);
		if (!string.IsNullOrEmpty(directory))
		{
			Directory.CreateDirectory(directory);
		}

		File.WriteAllText(path, content);
	}

	private static JsonObject ReadJsonObject(string path)
	{
		return JsonNode.Parse(File.ReadAllText(path))!.AsObject();
	}

	private static List<string> GetSourceMapAttributeValues(string path)
	{
		SyntaxNode root = CSharpSyntaxTree.ParseText(File.ReadAllText(path)).GetRoot();
		return root.DescendantNodes()
			.OfType<Microsoft.CodeAnalysis.CSharp.Syntax.AttributeSyntax>()
			.Where(attribute => attribute.Name.ToString().EndsWith("Attribution_SourceMap", StringComparison.Ordinal)
			                    || attribute.Name.ToString().EndsWith("Attribution_SourceMapAttribute", StringComparison.Ordinal))
			.Select(attribute => attribute.ArgumentList?.Arguments.FirstOrDefault()?.Expression.ToString() ?? string.Empty)
			.ToList();
	}

	private static IReadOnlyList<string> CompileWorkspaceSources(string workspacePath)
	{
		var sourcePaths = new List<string>();
		string importsPath = Path.Combine(workspacePath, "imports");
		if (Directory.Exists(importsPath))
		{
			sourcePaths.AddRange(Directory.GetFiles(importsPath, "*.cs", SearchOption.AllDirectories));
		}

		string attributePath = Path.Combine(workspacePath, "Attribution_SourceMapAttribute.cs");
		if (File.Exists(attributePath))
		{
			sourcePaths.Add(attributePath);
		}

		var parseOptions = new CSharpParseOptions(LanguageVersion.Latest);
		IEnumerable<SyntaxTree> syntaxTrees = sourcePaths.Select(path => CSharpSyntaxTree.ParseText(File.ReadAllText(path), parseOptions, path));

		var referencePaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
		if (AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES") is string trustedPlatformAssemblies)
		{
			foreach (string referencePath in trustedPlatformAssemblies.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
			{
				if (File.Exists(referencePath))
				{
					referencePaths.Add(referencePath);
				}
			}
		}

		referencePaths.Add(typeof(object).Assembly.Location);
		referencePaths.Add(typeof(List<>).Assembly.Location);
		referencePaths.Add(typeof(Enumerable).Assembly.Location);
		string mapApiAssemblyPath = Path.Combine(AppContext.BaseDirectory, "MapTemplate", "lib", "Realm.MapAPI.dll");
		if (File.Exists(mapApiAssemblyPath)) referencePaths.Add(mapApiAssemblyPath);

		IEnumerable<MetadataReference> references = referencePaths.Select(referencePath => MetadataReference.CreateFromFile(referencePath));
		CSharpCompilation compilation = CSharpCompilation.Create(
			"MapImportVerification",
			syntaxTrees,
			references,
			new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, nullableContextOptions: NullableContextOptions.Enable));

		return compilation.GetDiagnostics()
			.Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error)
			.Select(diagnostic => diagnostic.ToString())
			.ToList();
	}

	private static T GetPrivateField<T>(object instance, string fieldName)
	{
		FieldInfo? field = instance.GetType().GetField(fieldName, PrivateInstanceFlags);
		if (field == null) throw new InvalidOperationException($"Private field '{fieldName}' was not found on {instance.GetType().Name}.");

		return (T)field.GetValue(instance)!;
	}

	private static void InvokePrivate(object instance, string methodName)
	{
		MethodInfo? method = instance.GetType().GetMethod(methodName, PrivateInstanceFlags);
		if (method == null) throw new InvalidOperationException($"Private method '{methodName}' was not found on {instance.GetType().Name}.");

		method.Invoke(instance, null);
	}

	private static void SetSearchText(LineEdit search, string text)
	{
		search.Set("text", text);
		search.EmitSignal(LineEdit.SignalName.TextChanged, Variant.From(text));
	}

	private static void SelectOption(OptionButton optionButton, int index)
	{
		optionButton.Select(index);
		optionButton.EmitSignal(OptionButton.SignalName.ItemSelected, Variant.From(index));
	}

	private static VBoxContainer GetRowList(ImportFromMapDialog dialog)
	{
		return dialog.FindChild("AssetList_VBoxContainer", true, false) as VBoxContainer
		       ?? throw new InvalidOperationException("Import dialog row list is missing.");
	}

	private static List<string> GetRowLabels(VBoxContainer rowList)
	{
		var labels = new List<string>();
		foreach (Node child in rowList.GetChildren())
		{
			if (child is CheckBox checkBox)
			{
				labels.Add(checkBox.Text);
			}
		}

		return labels;
	}
}
