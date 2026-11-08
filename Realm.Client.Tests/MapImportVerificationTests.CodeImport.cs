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

/// <summary>importing map code, its dependencies and its exported types.</summary>
public partial class MapImportVerificationTests
	{
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
	public void TestCodeImportFindsTypesInGeneratedManifest()
	{
		string root = CreateTempRoot("code_generated_manifest");
		try
		{
			string sourcePath = Path.Combine(root, "source_map");
			Directory.CreateDirectory(sourcePath);
			File.WriteAllText(Path.Combine(sourcePath, "metadata.json"), BuildEmptyMetadata().ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
			foreach (KeyValuePair<string, string> sourceFile in BuildCodeFiles())
			{
				WriteTextFile(sourcePath, sourceFile.Key, sourceFile.Value);
			}

			MapManifest generated = MapManifest.CreateFromDirectory(sourcePath, SourceTitle, "Author", SourceVersion);
			string manifestPath = Path.Combine(sourcePath, "manifest.json");
			File.WriteAllText(manifestPath, generated.ToJson());

			MapManifest reloaded = MapManifest.LoadFromFile(manifestPath);
			Assertions.AssertThat(reloaded.Files.Keys.Any(key => key.EndsWith(".cs", StringComparison.OrdinalIgnoreCase)))
				.OverrideFailureMessage($"Generated manifest has no code files. Entries: {string.Join(", ", reloaded.Files.Keys)}")
				.IsTrue();

			GameContext.EnsureServices();
			var source = new MapAssetImportSource(SourceTitle, SourceVersion, manifestPath);
			IReadOnlyList<MapCodeTypeCandidate> types = GameContext.MapCodeImportService.EnumerateTypes(source, CreateDestinationWorkspace(root));

			Assertions.AssertThat(types.Count)
				.OverrideFailureMessage($"No code types were enumerated from a generated manifest. Manifest code entries: {string.Join(", ", reloaded.Files.Keys.Where(key => key.EndsWith(".cs", StringComparison.OrdinalIgnoreCase)))}")
				.IsGreater(0);
		}
		finally
		{
			DeleteTempRoot(root);
		}
	}

	[TestCase]
	public void TestCodeImportFindsTypesAfterArchiveRoundTrip()
	{
		string root = CreateTempRoot("code_archive_roundtrip");
		try
		{
			string sourcePath = Path.Combine(root, "source_map");
			Directory.CreateDirectory(sourcePath);
			File.WriteAllText(Path.Combine(sourcePath, "metadata.json"), BuildEmptyMetadata().ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
			foreach (KeyValuePair<string, string> sourceFile in BuildCodeFiles())
			{
				WriteTextFile(sourcePath, sourceFile.Key, sourceFile.Value);
			}

			MapManifest generated = MapManifest.CreateFromDirectory(sourcePath, SourceTitle, "Author", SourceVersion);
			File.WriteAllText(Path.Combine(sourcePath, "manifest.json"), generated.ToJson());

			string archivePath = MapArchiveHelper.CreateRmapArchive(sourcePath, Path.Combine(root, "export.rmap"), fullExport: true);

			string importedDirectory = Path.Combine(root, "imported_map");
			var cas = new ContentAddressableStorage(Path.Combine(root, "cas"));
			using (var archive = System.IO.Compression.ZipFile.OpenRead(archivePath))
			{
				var (manifestJson, rootPrefix) = MapArchiveHelper.ReadManifestFromArchive(archive);
				Assertions.AssertThat(string.IsNullOrWhiteSpace(manifestJson)).IsFalse();
				MapManifest archiveManifest = MapManifest.LoadFromJson(manifestJson!);
				MapArchiveHelper.ExtractArchiveToCasAndTarget(archive, cas, importedDirectory, archiveManifest, rootPrefix);
				File.WriteAllText(Path.Combine(importedDirectory, "manifest.json"), archiveManifest.ToJson());
			}

			GameContext.EnsureServices();
			var source = new MapAssetImportSource(SourceTitle, SourceVersion, Path.Combine(importedDirectory, "manifest.json"));
			IReadOnlyList<MapCodeTypeCandidate> types = GameContext.MapCodeImportService.EnumerateTypes(source, CreateDestinationWorkspace(root));

			string[] importedCodeFiles = Directory.GetFiles(importedDirectory, "*.cs", SearchOption.AllDirectories);
			Assertions.AssertThat(types.Count)
				.OverrideFailureMessage($"No code types after archive round trip. Code files on disk: {string.Join(", ", importedCodeFiles)}")
				.IsGreater(0);
		}
		finally
		{
			DeleteTempRoot(root);
		}
	}

	[TestCase]
	public void TestCodeImportFindsTypesWhenManifestOmitsCode()
	{
		string root = CreateTempRoot("code_unlisted");
		try
		{
			string sourcePath = Path.Combine(root, "source_map");
			Directory.CreateDirectory(sourcePath);
			File.WriteAllText(Path.Combine(sourcePath, "metadata.json"), BuildEmptyMetadata().ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
			foreach (KeyValuePair<string, string> sourceFile in BuildCodeFiles())
			{
				WriteTextFile(sourcePath, sourceFile.Key, sourceFile.Value);
			}

			var manifest = new MapManifest { MapName = SourceTitle, Version = SourceVersion };
			manifest.Files["metadata.json"] = "verification-hash";
			string manifestPath = Path.Combine(sourcePath, "manifest.json");
			manifest.SaveToFile(manifestPath);

			GameContext.EnsureServices();
			var source = new MapAssetImportSource(SourceTitle, SourceVersion, manifestPath);
			IReadOnlyList<MapCodeTypeCandidate> types = GameContext.MapCodeImportService.EnumerateTypes(source, CreateDestinationWorkspace(root));

			Assertions.AssertThat(types.Count)
				.OverrideFailureMessage("Maps whose manifests do not list their C# files should still expose the code that ships beside the manifest.")
				.IsGreater(0);
			Assertions.AssertThat(types.Any(candidate => candidate.FullName == "Ancient.Other.AncientUnrelated"))
				.OverrideFailureMessage($"Enumerated types: {string.Join(", ", types.Select(candidate => candidate.FullName))}")
				.IsTrue();
		}
		finally
		{
			DeleteTempRoot(root);
		}
	}

	[TestCase]
	public void TestExportedTypesAreListedSeparatelyFromTemplateCode()
	{
		string root = CreateTempRoot("code_exports");
		try
		{
			string destinationPath = CreateDestinationWorkspace(root);
			var sourceFiles = new Dictionary<string, string>(BuildCodeFiles())
			{
				["MeleeRules.cs"] = """
				namespace Realm.Maps;

				using Realm.MapAPI;

				/// <summary>Shared melee rule helpers.</summary>
				[Export("Melee Rules", "Rules")]
				public class MeleeRules
				{
					public float BasePopulation = 20.0f;
				}

				public static class MeleeRulesExtensions
				{
					public static MeleeRules EnableMeleeRules(this IGameAPI api)
					{
						return new MeleeRules();
					}
				}
				"""
			};

			MapAssetImportSource source = CreateSourceMap(root, SourceTitle, SourceVersion, BuildEmptyMetadata(), sourceFiles);

			GameContext.EnsureServices();
			MapCodeImportService service = GameContext.MapCodeImportService;

			IReadOnlyList<MapCodeTypeCandidate> exported = service.EnumerateExports(source, destinationPath);
			Assertions.AssertThat(exported.Count)
				.OverrideFailureMessage($"Exported items: {string.Join(", ", exported.Select(candidate => candidate.FullName))}")
				.IsEqual(1);
			Assertions.AssertThat(exported[0].FullName).IsEqual("Realm.Maps.MeleeRules");
			Assertions.AssertThat(exported[0].ExportLabel).IsEqual("[Rules] Melee Rules");

			IReadOnlyList<MapCodeTypeCandidate> classes = service.EnumerateTypes(source, destinationPath);
			Assertions.AssertThat(classes.Any(candidate => candidate.ClassName == "MapScript"))
				.OverrideFailureMessage("The map entry point should never be offered as an importable class.")
				.IsFalse();

			MapCodeImportSummary summary = service.ImportTypes(source, destinationPath, new[] { "Realm.Maps.MeleeRules" });
			Assertions.AssertThat(summary.FailedTypes).OverrideFailureMessage($"Failed types: {string.Join(", ", summary.FailedTypes)}").IsEmpty();

			string importedPath = Path.Combine(destinationPath, "imports", SourceTitle, "Realm", "Maps", "MeleeRules.cs");
			Assertions.AssertThat(File.Exists(importedPath)).OverrideFailureMessage($"Missing {importedPath}").IsTrue();
			string importedContent = File.ReadAllText(importedPath);
			Assertions.AssertThat(importedContent.Contains("[Export(\"Melee Rules\", \"Rules\")]")).IsTrue();
			Assertions.AssertThat(importedContent.Contains("<summary>Shared melee rule helpers.</summary>")).IsTrue();

			string importedExtensionsPath = Path.Combine(destinationPath, "imports", SourceTitle, "Realm", "Maps", "MeleeRulesExtensions.cs");
			Assertions.AssertThat(File.Exists(importedExtensionsPath))
				.OverrideFailureMessage("An exported item should bring the helper types declared in its own file along with it.")
				.IsTrue();

			IReadOnlyList<string> compilationErrors = CompileWorkspaceSources(destinationPath);
			Assertions.AssertThat(compilationErrors)
				.OverrideFailureMessage($"Imported export does not compile: {string.Join(Environment.NewLine, compilationErrors)}")
				.IsEmpty();
		}
		finally
		{
			DeleteTempRoot(root);
		}
	}
}
