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

/// <summary>importing a template map and its recursive dependencies.</summary>
public partial class MapImportVerificationTests
	{
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
}
