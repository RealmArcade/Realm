using System.IO.Compression;
using System.Text;
using System.Text.Json.Nodes;
using NUnit.Framework;
using Realm.Shared.Distribution;
using Realm.Shared.Metadata;
using Realm.Shared.Services;

namespace Realm.Distribution.Tests;

/// <summary>
///     Covers a reference being re-imported into the referencing map on every save: the metadata merge, the single map
///     each other file is kept from, and the read-only mark that keeps a copied file from being edited.
/// </summary>
[TestFixture]
[NonParallelizable]
public class MapReferenceResolutionTests
{
	private string _root = string.Empty;
	private string _parent = string.Empty;

	[SetUp]
	public void Setup()
	{
		_root = Path.Combine(Path.GetTempPath(), "RealmReferenceTest_" + Guid.NewGuid().ToString("N"));
		Directory.CreateDirectory(_root);

		_parent = Path.Combine(_root, "Parent Map");
		Directory.CreateDirectory(_parent);
	}

	[TearDown]
	public void TearDown()
	{
		try
		{
			if (Directory.Exists(_root))
			{
				MapReferenceResolver.ClearReadOnlyRecursive(_root);
				Directory.Delete(_root, true);
			}
		}
		catch
		{
		}
	}

	[Test]
	public void MetadataMergesReferencedMapsAlphabeticallyAndTheParentLast()
	{
		WriteMetadata(_parent, """{"MapProperties":{"MapName":"Parent","ShroudType":"None"},"Shared":"parent"}""");
		string alpha = CreateReferencedMap("Alpha", """{"MapProperties":{"ShroudType":"ExplorationShroud"},"Shared":"alpha"}""");
		string beta = CreateReferencedMap("Beta", """{"MapProperties":{"WeatherType":"Rain"},"Shared":"beta"}""");
		WriteReferences(("Beta", beta), ("Alpha", alpha));

		MapReferenceResolutionResult result = MapReferenceResolver.Apply(_parent);
		Assert.That(result.Success, Is.True, result.Message);

		JsonNode merged = ReadJson(Path.Combine(_parent, "metadata.json"));
		Assert.That(merged["Shared"]!.GetValue<string>(), Is.EqualTo("parent"), "The parent is merged last, so it takes precedence.");
		Assert.That(merged["MapProperties"]!["ShroudType"]!.GetValue<string>(), Is.EqualTo("None"), "The parent's own property stands over the referenced map's.");
		Assert.That(merged["MapProperties"]!["WeatherType"]!.GetValue<string>(), Is.EqualTo("Rain"), "A property only a referenced map states arrives.");
		Assert.That(merged["MapProperties"]!["MapName"]!.GetValue<string>(), Is.EqualTo("Parent"), "The map identity stays the parent's.");
	}

	[Test]
	public void MetadataArraysCombineAndARepeatedSlugMovesToTheEnd()
	{
		WriteMetadata(_parent, """
		                       {"Templates":{"Buildings":[
		                         {"TemplateID":"building/nexus","Health":900},
		                         {"TemplateID":"building/tower","Health":500}
		                       ]}}
		                       """);
		string child = CreateReferencedMap("Terrain", """
		                                             {"Templates":{"Buildings":[
		                                               {"TemplateID":"building/nexus","Health":100},
		                                               {"TemplateID":"building/wall","Health":200}
		                                             ]}}
		                                             """);
		WriteReferences(("Terrain", child));

		MapReferenceResolutionResult result = MapReferenceResolver.Apply(_parent);
		Assert.That(result.Success, Is.True, result.Message);

		JsonArray buildings = ReadJson(Path.Combine(_parent, "metadata.json"))["Templates"]!["Buildings"]!.AsArray();
		var identifiers = buildings.Select(entry => entry!["TemplateID"]!.GetValue<string>()).ToList();
		Assert.That(identifiers, Is.EqualTo(new[] { "building/wall", "building/nexus", "building/tower" }), "Each slug is kept once, and the parent's entry for it is laid down last.");
		Assert.That(buildings.First(entry => entry!["TemplateID"]!.GetValue<string>() == "building/nexus")!["Health"]!.GetValue<int>(), Is.EqualTo(900), "The parent's entry is the one that survives a repeated slug.");
	}

	[Test]
	public void ThePartialMetadataHoldsOnlyTheParentsOverrides()
	{
		WriteMetadata(_parent, """{"GameBuildNumber":"v9","MapProperties":{"MapName":"Parent","ShroudType":"None"}}""");
		string child = CreateReferencedMap("Terrain", """{"GameBuildNumber":"v1","MapProperties":{"MapName":"Terrain","ShroudType":"ExplorationShroud","WeatherType":"Rain"}}""");
		WriteReferences(("Terrain", child));

		MapReferenceResolutionResult result = MapReferenceResolver.Apply(_parent);
		Assert.That(result.Success, Is.True, result.Message);

		JsonNode partial = ReadJson(Path.Combine(_parent, "metadata.partial.json"));
		Assert.That(partial["GameBuildNumber"]!.GetValue<string>(), Is.EqualTo("v9"), "An overridden scalar is recorded.");
		Assert.That(partial["MapProperties"]!["ShroudType"]!.GetValue<string>(), Is.EqualTo("None"), "An overridden nested key is recorded.");
		Assert.That(partial["MapProperties"]!.AsObject().ContainsKey("WeatherType"), Is.False, "A value the referenced map already states is not the parent's override.");

		JsonNode merged = ReadJson(Path.Combine(_parent, "metadata.json"));
		Assert.That(merged["MapProperties"]!["WeatherType"]!.GetValue<string>(), Is.EqualTo("Rain"), "The referenced map's own value still arrives in the merge.");
	}

	[Test]
	public void AParentsNullDoesNotEraseWhatAReferencedMapStates()
	{
		WriteMetadata(_parent, """{"MapProperties":{"MapName":"Terrain","ShroudType":null,"WeatherType":null}}""");
		string child = CreateReferencedMap("Rules", """{"MapProperties":{"ShroudType":"None","WeatherType":"Rain"}}""");
		WriteReferences(("Rules", child));

		MapReferenceResolutionResult result = MapReferenceResolver.Apply(_parent);
		Assert.That(result.Success, Is.True, result.Message);

		JsonNode merged = ReadJson(Path.Combine(_parent, "metadata.json"));
		Assert.That(merged["MapProperties"]!["MapName"]!.GetValue<string>(), Is.EqualTo("Terrain"), "A property only the parent states still arrives.");
		Assert.That(merged["MapProperties"]!["ShroudType"]!.GetValue<string>(), Is.EqualTo("None"), "A parent that states null states nothing, so the referenced map's value stands.");
		Assert.That(merged["MapProperties"]!["WeatherType"]!.GetValue<string>(), Is.EqualTo("Rain"), "Every property the parent leaves null keeps the referenced map's value.");

		JsonNode partial = ReadJson(Path.Combine(_parent, "metadata.partial.json"));
		Assert.That(partial["MapProperties"]!.AsObject().ContainsKey("ShroudType"), Is.False, "A null is not recorded as an override either, so a later save cannot freeze it.");

		MapReferenceResolutionResult second = MapReferenceResolver.Apply(_parent);
		Assert.That(second.Success, Is.True, second.Message);
		Assert.That(JsonNode.DeepEquals(ReadJson(Path.Combine(_parent, "metadata.json")), merged), Is.True, "Merging a parent that leaves properties null is stable.");
	}

	[Test]
	public void GeneratedCodeIsNeverTakenFromAReferencedMap()
	{
		WriteMetadata(_parent, "{}");
		WriteFile(_parent, "WasmEntryPoint.g.cs", "parent entry point");
		string child = CreateReferencedMap("Rules", "{}");
		WriteFile(child, "WasmEntryPoint.g.cs", "referenced entry point");
		WriteFile(child, "MapTemplates.g.cs", "referenced template constants");
		WriteReferences(("Rules", child));

		MapReferenceResolutionResult result = MapReferenceResolver.Apply(_parent);
		Assert.That(result.Success, Is.True, result.Message);

		Assert.That(
			File.ReadAllText(Path.Combine(_parent, "WasmEntryPoint.g.cs")),
			Is.EqualTo("parent entry point"),
			"The entry point is generated for this workspace from the engine's template, so another map's copy would be another build's.");
		Assert.That(
			File.Exists(Path.Combine(_parent, "MapTemplates.g.cs")),
			Is.False,
			"The template constants are generated from this map's merged metadata, so a referenced map's copy is not content the parent needs.");
		Assert.That(result.CopiedFiles, Does.Not.Contain("MapTemplates.g.cs"));
		Assert.That(MapReferenceResolver.EnumeratePackageFiles(_parent), Does.Not.Contain("WasmEntryPoint.g.cs"), "A generated file is never offered as a file a save decides.");
	}

	[Test]
	public void ReferencedFilesAreCopiedOnceAndThenLeftAlone()
	{
		WriteMetadata(_parent, "{}");
		string child = CreateReferencedMap("Terrain", "{}");
		WriteFile(child, "terrain.json", "child terrain");
		WriteFile(child, "Assets/rock.rmesh", "rock");
		WriteReferences(("Terrain", child));

		MapReferenceResolutionResult first = MapReferenceResolver.Apply(_parent);
		Assert.That(first.Success, Is.True, first.Message);
		Assert.That(first.CopiedFiles, Is.EquivalentTo(new[] { "Assets/rock.rmesh", "terrain.json" }));
		Assert.That(first.ReplacedFiles, Is.Empty, "Nothing the parent authored is overwritten.");

		string terrainPath = Path.Combine(_parent, "terrain.json");
		Assert.That(File.GetAttributes(terrainPath).HasFlag(FileAttributes.ReadOnly), Is.True, "A copied file is read-only, because an edit to it would be reverted by the next save.");
		Assert.That(File.ReadAllText(terrainPath), Is.EqualTo("child terrain"));
		Assert.That(File.GetAttributes(Path.Combine(_parent, "Assets", "rock.rmesh")).HasFlag(FileAttributes.ReadOnly), Is.True);

		MapReferenceResolutionResult second = MapReferenceResolver.Apply(_parent);
		Assert.That(second.Success, Is.True, second.Message);
		Assert.That(second.CopiedFiles, Is.Empty, "A save straight after the first has nothing left to write.");
		Assert.That(File.GetAttributes(terrainPath).HasFlag(FileAttributes.ReadOnly), Is.True, "The copied file stays read-only rather than becoming the parent's own.");
	}

	[Test]
	public void TheParentKeepsAFileBothMapsCarryByDefault()
	{
		WriteMetadata(_parent, "{}");
		WriteFile(_parent, "MapScript.cs", "parent script");
		string child = CreateReferencedMap("Terrain", "{}");
		WriteFile(child, "MapScript.cs", "child script");
		WriteReferences(("Terrain", child));

		MapReferenceResolutionResult result = MapReferenceResolver.Apply(_parent);
		Assert.That(result.Success, Is.True, result.Message);

		string scriptPath = Path.Combine(_parent, "MapScript.cs");
		Assert.That(File.ReadAllText(scriptPath), Is.EqualTo("parent script"), "No priority is stated, so the parent's own copy is the one that stays.");
		Assert.That(File.GetAttributes(scriptPath).HasFlag(FileAttributes.ReadOnly), Is.False, "The parent's own file stays editable.");
	}

	[Test]
	public void APriorityKeepsTheNamedMapsCopyOfACollidingFile()
	{
		WriteMetadata(_parent, "{}");
		WriteFile(_parent, "terrain.json", "parent terrain");
		string child = CreateReferencedMap("Terrain", "{}");
		WriteFile(child, "terrain.json", "child terrain");
		WriteReferences(("Terrain", child));
		WritePriority("terrain.json", "Terrain");

		MapReferenceResolutionResult result = MapReferenceResolver.Apply(_parent);
		Assert.That(result.Success, Is.True, result.Message);
		Assert.That(result.ReplacedFiles, Is.EquivalentTo(new[] { "terrain.json" }), "The parent's own copy was replaced, which a save has to report.");

		string terrainPath = Path.Combine(_parent, "terrain.json");
		Assert.That(File.ReadAllText(terrainPath), Is.EqualTo("child terrain"));
		Assert.That(File.GetAttributes(terrainPath).HasFlag(FileAttributes.ReadOnly), Is.True);
	}

	[Test]
	public void APriorityNamingAMapThatDoesNotCarryTheFileFails()
	{
		WriteMetadata(_parent, "{}");
		string alpha = CreateReferencedMap("Alpha", "{}");
		WriteFile(alpha, "Assets/rock.rmesh", "alpha rock");
		string beta = CreateReferencedMap("Beta", "{}");
		WriteFile(beta, "Assets/rock.rmesh", "beta rock");
		string gamma = CreateReferencedMap("Gamma", "{}");
		WriteFile(gamma, "Assets/tree.rmesh", "tree");
		WriteReferences(("Alpha", alpha), ("Beta", beta), ("Gamma", gamma));
		WritePriority("Assets/rock.rmesh", "Gamma");

		MapReferenceResolutionResult result = MapReferenceResolver.Apply(_parent);
		Assert.That(result.Success, Is.False, "A priority for a map that does not carry the file cannot be honoured silently.");
		Assert.That(result.Message, Does.Contain("Assets/rock.rmesh"));
	}

	[Test]
	public void APriorityForAFileOnlyOneMapCarriesIsAccepted()
	{
		WriteMetadata(_parent, "{}");
		string alpha = CreateReferencedMap("Alpha", "{}");
		WriteFile(alpha, "Assets/rock.rmesh", "alpha rock");
		WriteReferences(("Alpha", alpha));
		WritePriority("Assets/rock.rmesh", "Alpha");

		MapReferenceResolutionResult result = MapReferenceResolver.Apply(_parent);
		Assert.That(result.Success, Is.True, result.Message);
		Assert.That(File.ReadAllText(Path.Combine(_parent, "Assets", "rock.rmesh")), Is.EqualTo("alpha rock"));
	}

	[Test]
	public void TwoReferencedMapsCollidingWithoutAPriorityFails()
	{
		WriteMetadata(_parent, "{}");
		string alpha = CreateReferencedMap("Alpha", "{}");
		WriteFile(alpha, "Assets/rock.rmesh", "alpha rock");
		string beta = CreateReferencedMap("Beta", "{}");
		WriteFile(beta, "Assets/rock.rmesh", "beta rock");
		WriteReferences(("Alpha", alpha), ("Beta", beta));

		MapReferenceResolutionResult result = MapReferenceResolver.Apply(_parent);
		Assert.That(result.Success, Is.False, "Either choice would silently drop the other map's file.");
		Assert.That(result.Message, Does.Contain("Assets/rock.rmesh"));
		Assert.That(File.Exists(Path.Combine(_parent, "Assets", "rock.rmesh")), Is.False, "A failed save writes no file it could not decide.");
	}

	[Test]
	public void TheReferencesTravelWithThePackageButTheOverrideFileDoesNot()
	{
		WriteMetadata(_parent, "{}");
		string child = CreateReferencedMap("Terrain", "{}");
		WriteFile(child, "terrain.json", "child terrain");
		WriteFile(child, "manifest.json", "{\"Files\":{}}");
		WriteReferences(("Terrain", child));

		MapReferenceResolutionResult result = MapReferenceResolver.Apply(_parent);
		Assert.That(result.Success, Is.True, result.Message);

		List<string> packageFiles = MapReferenceResolver.EnumeratePackageFiles(_parent);
		Assert.That(packageFiles, Does.Contain("terrain.json"));

		Assert.That(packageFiles, Does.Not.Contain(MapMetadataMerger.MetadataPartialFileName), "The override file is the author's, not content a player needs.");
		Assert.That(packageFiles, Does.Not.Contain("manifest.json"), "A manifest is generated over the folder, so it is not content itself.");

		MapManifest manifest = MapManifest.CreateFromDirectory(_parent, "Parent", "Author", "1.0.0");
		Assert.That(manifest.Files.Keys, Does.Contain("terrain.json"));
		Assert.That(manifest.Files.Keys, Does.Contain(MapReferences.FileName), "A download rebuilds the folder from the manifest alone, so the references have to be listed in it.");
		Assert.That(manifest.Files.Keys, Does.Not.Contain(MapMetadataMerger.MetadataPartialFileName));
		MapFileService.SaveManifest(_parent, manifest);

		string archivePath = MapArchiveHelper.CreateRmapArchive(_parent, Path.Combine(_root, "parent.rmap"));
		using var archive = ZipFile.OpenRead(archivePath);
		var packagedNames = archive.Entries.Select(entry => entry.FullName).ToList();
		Assert.That(packagedNames, Does.Contain(MapReferences.FileName), "The references travel inside the package, so another author can open the map and keep extending the composition it was built from.");
		Assert.That(packagedNames, Does.Not.Contain(MapMetadataMerger.MetadataPartialFileName), "The override file states the parent's own intent, which the merged metadata already carries, so it does not travel.");

		byte[] packagedReferencesBytes = ReadEntryBytes(archive, MapReferences.FileName);
		MapReferences? packagedReferences = MapReferences.LoadFromJson(Encoding.UTF8.GetString(packagedReferencesBytes));
		Assert.That(packagedReferences, Is.Not.Null, "The packaged references have to be readable by the editor that opens the package.");
		Assert.That(packagedReferences!.References, Has.Count.EqualTo(1), "Every reference travels, not only the first.");
		Assert.That(packagedReferences.References[0].FolderPath, Is.Empty, "The helper folder names a place on the author's machine, so the package ships without it.");
		Assert.That(packagedReferences.References[0].MapName, Is.EqualTo("Terrain"), "Everything but the helper folder travels unchanged.");
		Assert.That(packagedReferences.References[0].ManifestBlake3, Is.EqualTo("00"), "The pin travels with the reference, so the package resolves the exact map it was composed from.");

		(string? packagedManifestJson, _) = MapArchiveHelper.ReadManifestFromArchive(archive);
		MapManifest packagedManifest = MapManifest.LoadFromJson(packagedManifestJson ?? string.Empty)!;
		string packagedHash = ContentAddressableStorage.NormalizeBlake3Hash(RealmMetadataHelper.ComputeBlake3(packagedReferencesBytes, ".json"));
		Assert.That(
			ContentAddressableStorage.NormalizeBlake3Hash(packagedManifest.Files[MapReferences.FileName]),
			Is.EqualTo(packagedHash),
			"The manifest the package carries names the exact bytes it carries, or a download stores the references under a hash they do not have.");
	}

	private static byte[] ReadEntryBytes(ZipArchive archive, string entryName)
	{
		ZipArchiveEntry? entry = archive.GetEntry(entryName);
		Assert.That(entry, Is.Not.Null);
		using Stream entryStream = entry!.Open();
		using var memory = new MemoryStream();
		entryStream.CopyTo(memory);
		return memory.ToArray();
	}

	[Test]
	public void AFileLeftBehindByARemovedReferenceBecomesTheParentsOwn()
	{
		WriteMetadata(_parent, "{}");
		string terrain = CreateReferencedMap("Terrain", "{}");
		WriteFile(terrain, "Assets/rock.rmesh", "rock");
		string other = CreateReferencedMap("Other", "{}");
		WriteFile(other, "Assets/tree.rmesh", "tree");
		WriteReferences(("Terrain", terrain), ("Other", other));
		Assert.That(MapReferenceResolver.Apply(_parent).Success, Is.True);

		string rockPath = Path.Combine(_parent, "Assets", "rock.rmesh");
		Assert.That(File.GetAttributes(rockPath).HasFlag(FileAttributes.ReadOnly), Is.True);

		WriteReferences(("Other", other));
		MapReferenceResolutionResult result = MapReferenceResolver.Apply(_parent);
		Assert.That(result.Success, Is.True, result.Message);
		Assert.That(File.GetAttributes(rockPath).HasFlag(FileAttributes.ReadOnly), Is.False, "No remaining reference carries the file, so it becomes the parent's own and is editable.");
		Assert.That(File.ReadAllText(rockPath), Is.EqualTo("rock"));
	}

	[Test]
	public void AReferenceWithNoReachableFolderFails()
	{
		WriteMetadata(_parent, "{}");
		var references = new MapReferences();
		references.References.Add(new MapReference { MapName = "Missing Map", Version = "1.0.0", ManifestBlake3 = "00", FolderPath = Path.Combine(_root, "Nowhere") });
		references.Save(_parent);

		MapReferenceResolutionResult result = MapReferenceResolver.Apply(_parent);
		Assert.That(result.Success, Is.False);
		Assert.That(result.Message, Does.Contain("Missing Map"));
	}

	private string CreateReferencedMap(string mapName, string metadataJson)
	{
		string folder = Path.Combine(_root, mapName);
		Directory.CreateDirectory(folder);
		WriteMetadata(folder, metadataJson);
		return folder;
	}

	private void WriteReferences(params (string MapName, string Folder)[] references)
	{
		var document = new MapReferences();
		foreach ((string mapName, string folder) in references)
		{
			document.References.Add(new MapReference
			{
				MapName = mapName,
				Version = "1.0.0",
				ManifestBlake3 = "00",
				FolderPath = folder
			});
		}

		document.Save(_parent);
	}

	private void WritePriority(string relativePath, string mapName)
	{
		MapReferences? document = MapReferences.Load(_parent);
		Assert.That(document, Is.Not.Null);
		document!.SetConflictPriority(relativePath, mapName);
		document.Save(_parent);
	}

	private static void WriteMetadata(string folder, string json)
	{
		File.WriteAllText(Path.Combine(folder, "metadata.json"), json, new UTF8Encoding(false));
	}

	private static void WriteFile(string folder, string relativePath, string content)
	{
		string path = Path.Combine(folder, relativePath.Replace('/', Path.DirectorySeparatorChar));
		string? directory = Path.GetDirectoryName(path);
		if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);
		File.WriteAllText(path, content, new UTF8Encoding(false));
	}

	private static JsonNode ReadJson(string path)
	{
		return JsonNode.Parse(File.ReadAllText(path))!;
	}
}
