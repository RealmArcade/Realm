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

/// <summary>importing a whole C# namespace.</summary>
public partial class MapImportVerificationTests
	{
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
}
