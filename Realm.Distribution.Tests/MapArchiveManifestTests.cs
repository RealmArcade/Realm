using System.IO.Compression;
using NUnit.Framework;
using Realm.Shared.Distribution;
using Realm.Shared.Metadata;
using Realm.Shared.Services;

namespace Realm.Distribution.Tests;

/// <summary>
///     Checks the index a map package is downloaded by: a download, a lobby hand-off and a peer mirror rebuild a map
///     folder from <c>manifest.json</c> alone, so every file the archive carries has to be named in it.
/// </summary>
[TestFixture]
public class MapArchiveManifestTests
{
	private string _root = string.Empty;
	private string _workspace = string.Empty;
	private string _destination = string.Empty;

	[SetUp]
	public void SetUp()
	{
		_root = Path.Combine(Path.GetTempPath(), "realm-archive-manifest", Guid.NewGuid().ToString("N"));
		_workspace = Path.Combine(_root, "workspace");
		string outputDirectory = Path.Combine(_root, "out");
		_destination = Path.Combine(outputDirectory, "package.rmap");

		Directory.CreateDirectory(Path.Combine(_workspace, "Assets", "models", "units"));
		Directory.CreateDirectory(outputDirectory);

		File.WriteAllText(Path.Combine(_workspace, "Assets", "models", "units", "hero.rmesh"), "mesh-bytes");
		File.WriteAllText(Path.Combine(_workspace, "terrain.json"), """{ "HeightData": null }""");
		File.WriteAllText(Path.Combine(_workspace, "metadata.json"), """{ "MapProperties": { "MapName": "Stale Manifest" } }""");
		File.WriteAllText(Path.Combine(_workspace, "MapScript.cs"), "public class MapScript { }");

		var manifest = new MapManifest
		{
			MapName = "Stale Manifest",
			Author = "Fixture",
			Version = "1.0.0"
		};
		manifest.Files["Assets/models/units/hero.rmesh"] = AssetKeyOf("Assets/models/units/hero.rmesh");
		MapFileService.WriteAllTextAtomically(Path.Combine(_workspace, "manifest.json"), manifest.ToJson());
	}

	[TearDown]
	public void TearDown()
	{
		if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
	}

	[Test]
	public void TheArchiveManifestNamesEveryFileTheArchiveCarries()
	{
		string archivePath = MapArchiveHelper.CreateRmapArchive(_workspace, _destination);
		var (manifestJson, _) = MapArchiveHelper.ReadManifestFromArchive(archivePath);
		Assert.That(manifestJson, Is.Not.Null.And.Not.Empty, "The package has to carry the manifest it is downloaded by.");

		MapManifest archivedManifest = MapManifest.LoadFromJson(manifestJson!);
		using ZipArchive archive = ZipFile.OpenRead(archivePath);
		string[] entries = archive.Entries.Select(entry => entry.FullName.Replace('\\', '/')).ToArray();

		var listedHashes = new HashSet<string>(archivedManifest.Files.Values.Select(ContentAddressableStorage.NormalizeBlake3Hash));
		foreach (ZipArchiveEntry entry in archive.Entries)
		{
			string entryPath = entry.FullName.Replace('\\', '/');
			if (string.Equals(entryPath, "manifest.json", StringComparison.OrdinalIgnoreCase)) continue;

			using var entryStream = new MemoryStream();
			using (Stream content = entry.Open()) content.CopyTo(entryStream);
			string hash = ContentAddressableStorage.NormalizeBlake3Hash(RealmMetadataHelper.ComputeBlake3(entryStream.ToArray(), Path.GetExtension(entryPath).ToLowerInvariant()));
			Assert.That(listedHashes, Does.Contain(hash), $"The package carries '{entryPath}' without naming it in manifest.json, so a download would leave it behind.");
		}

		Assert.That(entries, Does.Contain("terrain.json"), "The terrain the package carries has to travel in the archive.");
		Assert.That(archivedManifest.Files.Keys, Does.Contain("terrain.json"), "The terrain has to be downloadable, which means the manifest names it.");
		Assert.That(archivedManifest.Files.Keys, Does.Contain("metadata.json"), "A package whose metadata is unnamed downloads as a map with no rules.");
		Assert.That(archivedManifest.Files.Keys, Does.Contain("MapScript.cs"), "The script source has to be named for anyone opening the package later.");
	}

	[Test]
	public void TheArchiveKeepsTheEntriesTheManifestAlreadyNamed()
	{
		string archivePath = MapArchiveHelper.CreateRmapArchive(_workspace, _destination);
		var (manifestJson, _) = MapArchiveHelper.ReadManifestFromArchive(archivePath);
		MapManifest archivedManifest = MapManifest.LoadFromJson(manifestJson!);

		Assert.That(archivedManifest.Files["Assets/models/units/hero.rmesh"], Is.EqualTo(AssetKeyOf("Assets/models/units/hero.rmesh")), "An asset the manifest already named keeps the key it had.");
		Assert.That(ContentAddressableStorage.NormalizeBlake3Hash(archivedManifest.Files["terrain.json"]), Is.EqualTo(ContentAddressableStorage.NormalizeBlake3Hash(AssetKeyOf("terrain.json"))), "A file the manifest gains is keyed by the bytes the archive carries.");
		Assert.That(archivedManifest.MapName, Is.EqualTo("Stale Manifest"), "Indexing the archive leaves the package's identity untouched.");
	}

	private string AssetKeyOf(string relativePath)
	{
		string filePath = Path.Combine(_workspace, relativePath.Replace('/', Path.DirectorySeparatorChar));
		string extension = Path.GetExtension(filePath).ToLowerInvariant();
		string hash = RealmMetadataHelper.ComputeBlake3(File.ReadAllBytes(filePath), extension);
		return string.IsNullOrEmpty(extension) ? hash : $"{hash}{extension}";
	}
}
