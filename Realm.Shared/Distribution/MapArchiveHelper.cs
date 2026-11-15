using System.IO.Compression;
using System.Text;
using Blake3;
using Realm.Shared.Metadata;
using Realm.Shared.Services;

namespace Realm.Shared.Distribution;

public static class MapArchiveHelper
{
	public static readonly byte[] RmapMagic = "RMAP"u8.ToArray();
	public const uint CurrentVersion = 1;

	public static string CreateRmapArchive(
		string sourceDirectory,
		string destinationRmapPath,
		Action<float, string>? progressCallback = null,
		int compressionLevel = 1,
		bool fullExport = false,
		IReadOnlyCollection<string>? excludedRelativePaths = null)
	{
		PrepareArchiveDestination(sourceDirectory, destinationRmapPath);

		DistributionReferences? references = BuildDistributionReferences(sourceDirectory);
		var filesToArchive = GetFilesToArchive(sourceDirectory, fullExport, excludedRelativePaths);
		IndexArchivedFilesInManifest(sourceDirectory, filesToArchive, references);
		SortFilesForArchive(filesToArchive, sourceDirectory);

		WriteFilesToZipArchive(filesToArchive, sourceDirectory, destinationRmapPath, progressCallback, references);

		return AppendContentHashToRmapFileName(destinationRmapPath);
	}

	private static string AppendContentHashToRmapFileName(string rmapPath)
	{
		string blake3 = ComputeFileBlake3(rmapPath);
		if (string.IsNullOrEmpty(blake3)) return rmapPath;

		string shortHash = blake3.Length >= 4 ? blake3.Substring(0, 4) : blake3;
		string directory = Path.GetDirectoryName(rmapPath) ?? string.Empty;
		string baseName = Path.GetFileNameWithoutExtension(rmapPath);
		string renamedPath = Path.Combine(directory, $"{baseName}_{shortHash}.rmap");

		File.Move(rmapPath, renamedPath, overwrite: true);
		return renamedPath;
	}

	private static string ComputeFileBlake3(string filePath)
	{
		if (!File.Exists(filePath)) return string.Empty;

		using var hasher = Hasher.New();
		byte[] buffer = new byte[81920];

		using var fileStream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, FileOptions.SequentialScan);
		int read;
		while ((read = fileStream.Read(buffer, 0, buffer.Length)) > 0)
		{
			hasher.Update(new ReadOnlySpan<byte>(buffer, 0, read));
		}

		return hasher.Finalize().ToString();
	}

	private static void PrepareArchiveDestination(string sourceDirectory, string destinationRmapPath)
	{
		if (string.IsNullOrWhiteSpace(sourceDirectory) || !Directory.Exists(sourceDirectory))
		{
			throw new DirectoryNotFoundException($"Source directory '{sourceDirectory}' does not exist.");
		}

		string? destinationDir = Path.GetDirectoryName(destinationRmapPath);
		if (!string.IsNullOrEmpty(destinationDir) && !Directory.Exists(destinationDir))
		{
			Directory.CreateDirectory(destinationDir);
		}

		if (File.Exists(destinationRmapPath))
		{
			File.Delete(destinationRmapPath);
		}
	}

	/// <summary>
	///     Makes the manifest the archive carries name every file the archive contains. A download, a lobby hand-off and a
	///     peer mirror all rebuild a map folder from <c>manifest.json</c> alone, so a file that travels in the package
	///     without being listed in it never reaches the player: the map arrives without its terrain, its metadata or the
	///     script it runs, and cannot be loaded.
	/// </summary>
	/// <remarks>
///     Entries the manifest already holds are left exactly as they are, including the hashes of assets a greenlit
///     reference lends the package and which therefore never travel inside it. Only files the manifest does not name are
///     added, with the canonical asset key their bytes produce. The one exception is the references file: the package
///     ships a copy without the author's helper folders, so its entry is restated with the hash of the bytes that travel.
/// </remarks>
/// <param name="sourceDirectory">The package folder being archived.</param>
/// <param name="filesToArchive">The files the archive will contain.</param>
/// <param name="references">The portable references the package carries instead of the folder's own copy, when it has one.</param>
private static void IndexArchivedFilesInManifest(string sourceDirectory, IReadOnlyList<string> filesToArchive, DistributionReferences? references)
	{
		string manifestPath = Path.Combine(sourceDirectory, "manifest.json");
		if (!File.Exists(manifestPath)) return;

		MapManifest manifest;
		try
		{
			manifest = MapManifest.LoadFromJson(File.ReadAllText(manifestPath));
		}
		catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or System.Text.Json.JsonException)
		{
			return;
		}

		if (manifest == null) return;

		Dictionary<string, string> listedFiles = manifest.Files;
		bool added = false;

		foreach (string filePath in filesToArchive)
		{
			string relativePath = Path.GetRelativePath(sourceDirectory, filePath).Replace('\\', '/');
			if (string.Equals(relativePath, "manifest.json", StringComparison.OrdinalIgnoreCase)) continue;
			if (MapManifest.IsGeneratedWorkspaceFile(relativePath)) continue;
			if (listedFiles.ContainsKey(relativePath)) continue;

			var archivedFile = new FileInfo(filePath);
			if (!archivedFile.Exists || archivedFile.Length == 0) continue;

			string extension = Path.GetExtension(filePath).ToLowerInvariant();
			string hash = RealmMetadataHelper.ComputeBlake3(File.ReadAllBytes(filePath), extension);
			if (string.IsNullOrEmpty(hash)) continue;

			listedFiles[relativePath] = string.IsNullOrEmpty(extension) ? hash : $"{hash}{extension}";
			added = true;
		}

		if (references != null)
		{
			listedFiles[references.RelativePath] = references.AssetKey;
			manifest.FileSizes![references.RelativePath] = references.Bytes.Length;
			added = true;
		}

		if (!added) return;

		MapFileService.WriteAllTextAtomically(manifestPath, manifest.ToJson());
	}

	private static bool IsIgnoredPath(string relPath)
	{
		string[] prefixes = [".git/", ".backups/", "obj/", ".godot/", ".sidecarcache/", ".vscode/", ".vs/", $"bin/{MapFileService.DebugBuildFlavor}/", $"bin/{MapFileService.ReleaseBuildFlavor}/", $"bin/{MapFileService.ScriptStagingDirectoryNamePrefix}"];
		string[] suffixes = [".tmp", ".rmap", ".7z", ".zip", ".rar", ".tar", ".gz", ".bak", ".backup", ".rkey", ".elf", ".jit.elf"];

		if (prefixes.Any(p => relPath.StartsWith(p, StringComparison.OrdinalIgnoreCase))) return true;
		if (suffixes.Any(s => relPath.EndsWith(s, StringComparison.OrdinalIgnoreCase))) return true;
		if (relPath.Contains("/obj/", StringComparison.OrdinalIgnoreCase)) return true;
		if (string.Equals(Path.GetFileName(relPath), MapFileService.RetiredBuildFlavorFileName, StringComparison.OrdinalIgnoreCase)) return true;
		if (MapFileService.IsDebugScriptFileName(relPath)) return true;

		string fileName = Path.GetFileName(relPath);
		if (string.Equals(fileName, MapMetadataMerger.MetadataPartialFileName, StringComparison.OrdinalIgnoreCase)) return true;

		return string.Equals(fileName, "authorship_key.pem", StringComparison.OrdinalIgnoreCase);
	}

	private static bool IsAllowedExcludedFile(string relativePath)
	{
		string fn = Path.GetFileName(relativePath);
		string[] allowedExact = ["manifest.json", "metadata.json", "terrain.json"];
		string[] allowedSuffixes = [".cs", ".wasm", ".csproj"];

		if (allowedExact.Any(a => fn.Equals(a, StringComparison.OrdinalIgnoreCase))) return true;
		if (allowedSuffixes.Any(s => fn.EndsWith(s, StringComparison.OrdinalIgnoreCase))) return true;

		// A map's locale/*.json is read by name (the file name is the language), so it is text the package carries rather
		// than an asset anything references. Trimming it away would strip the map's translations from a published copy.
		if (relativePath.StartsWith("locale/", StringComparison.OrdinalIgnoreCase)) return true;

		return false;
	}

	private static List<string> GetFilesToArchive(string sourceDirectory, bool fullExport, IReadOnlyCollection<string>? excludedRelativePaths)
	{
		var allFiles = Directory.GetFiles(sourceDirectory, "*.*", SearchOption.AllDirectories);
		var filesToArchive = new List<string>(allFiles.Length);

		foreach (var file in allFiles)
		{
			if (ShouldIncludeFile(file, sourceDirectory, fullExport, excludedRelativePaths))
			{
				filesToArchive.Add(file);
			}
		}

		return filesToArchive;
	}

	private static bool ShouldIncludeFile(string file, string sourceDirectory, bool fullExport, IReadOnlyCollection<string>? excludedRelativePaths)
	{
		string relativePath = Path.GetRelativePath(sourceDirectory, file).Replace('\\', '/');

		if (IsIgnoredPath(relativePath)) return false;

		if (!fullExport && excludedRelativePaths != null && excludedRelativePaths.Contains(relativePath))
		{
			if (!IsAllowedExcludedFile(relativePath)) return false;
		}

		var fileInfo = new FileInfo(file);
		if (!fileInfo.Exists) return false;
		if (fileInfo.Length == 0) return false;

		return true;
	}

	private static void SortFilesForArchive(List<string> filesToArchive, string sourceDirectory)
	{
		filesToArchive.Sort((a, b) =>
		{
			string relA = Path.GetRelativePath(sourceDirectory, a).Replace('\\', '/');
			string relB = Path.GetRelativePath(sourceDirectory, b).Replace('\\', '/');
			int priorityA = GetFilePriority(relA);
			int priorityB = GetFilePriority(relB);
			if (priorityA != priorityB) return priorityA.CompareTo(priorityB);
			return string.Compare(relA, relB, StringComparison.OrdinalIgnoreCase);
		});
	}

	private static void WriteFilesToZipArchive(List<string> filesToArchive, string sourceDirectory, string destinationRmapPath, Action<float, string>? progressCallback, DistributionReferences? references)
	{
		using var fileStream = new FileStream(destinationRmapPath, FileMode.Create, FileAccess.Write, FileShare.None, 81920);
		using var zipArchive = new ZipArchive(fileStream, ZipArchiveMode.Create, leaveOpen: false);

		int total = filesToArchive.Count;
		for (int i = 0; i < total; i++)
		{
			string file = filesToArchive[i];
			string relativePath = Path.GetRelativePath(sourceDirectory, file).Replace('\\', '/');
			progressCallback?.Invoke((float)(i + 1) / Math.Max(1, total), relativePath);

			if (references != null && string.Equals(relativePath, references.RelativePath, StringComparison.OrdinalIgnoreCase))
			{
				var referencesEntry = zipArchive.CreateEntry(relativePath, CompressionLevel.NoCompression);
				using var referencesStream = referencesEntry.Open();
				referencesStream.Write(references.Bytes, 0, references.Bytes.Length);
				continue;
			}

			var entry = zipArchive.CreateEntry(relativePath, CompressionLevel.NoCompression);
			using var entryStream = entry.Open();
			using var inputFs = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, FileOptions.SequentialScan);
			inputFs.CopyTo(entryStream, 81920);
		}
	}

	/// <summary>
	///     Builds the copy of the references the package ships: the same maps, versions and conflict priorities, with the
	///     helper folders cleared, because those name a place on the machine the map is being authored on.
	/// </summary>
	/// <param name="sourceDirectory">The package folder being archived.</param>
	/// <returns>The bytes and asset key the package states, or null when the folder references no map.</returns>
	private static DistributionReferences? BuildDistributionReferences(string sourceDirectory)
	{
		string path = Path.Combine(sourceDirectory, MapReferences.FileName);
		if (!File.Exists(path)) return null;

		MapReferences? references = MapReferences.Load(sourceDirectory);
		if (references == null)
		{
			throw new InvalidDataException($"The map keeps '{MapReferences.FileName}' and it cannot be read, so the package cannot state the maps it is composed from.");
		}

		const string extension = ".json";
		byte[] bytes = Encoding.UTF8.GetBytes(references.ToDistributionJson());
		return new DistributionReferences
		{
			RelativePath = MapReferences.FileName,
			Bytes = bytes,
			AssetKey = $"{RealmMetadataHelper.ComputeBlake3(bytes, extension)}{extension}"
		};
	}

	private class DistributionReferences
	{
		public string RelativePath { get; init; } = string.Empty;

		public byte[] Bytes { get; init; } = Array.Empty<byte>();

		public string AssetKey { get; init; } = string.Empty;
	}

	public static RmapHeaderInfo? ReadHeaderFromRmap(string rmapFilePath)
	{
		if (string.IsNullOrWhiteSpace(rmapFilePath) || !File.Exists(rmapFilePath)) return null;

		var (manifestJson, _) = ReadManifestFromArchive(rmapFilePath);
		if (string.IsNullOrWhiteSpace(manifestJson)) return null;

		try
		{
			var manifest = MapManifest.LoadFromJson(manifestJson);
			if (manifest == null) return null;
			return ReadHeaderFromManifest(manifest);
		}
		catch
		{
			return null;
		}
	}

	public static RmapHeaderInfo? ReadHeaderFromManifest(MapManifest manifest)
	{
		if (manifest == null) return null;
		return new RmapHeaderInfo
		{
			MapName = manifest.MapName ?? string.Empty,
			Version = manifest.Version ?? "1.0.0",
			Author = manifest.Author ?? "Unknown",
			Description = manifest.Description ?? string.Empty,
			Tags = manifest.Tags ?? new List<string>()
		};
	}

	public static (string? ManifestJson, string RootPrefix) ReadManifestFromArchive(ZipArchive zipArchive)
	{
		if (zipArchive == null) return (null, string.Empty);
		foreach (var entry in zipArchive.Entries)
		{
			string norm = entry.FullName.Replace('\\', '/');
			if (norm.EndsWith("manifest.json", StringComparison.OrdinalIgnoreCase) &&
			    (norm.Equals("manifest.json", StringComparison.OrdinalIgnoreCase) || norm.EndsWith("/manifest.json", StringComparison.OrdinalIgnoreCase)))
			{
				string rootPrefix = norm.Length > "manifest.json".Length
					? norm.Substring(0, norm.Length - "manifest.json".Length)
					: string.Empty;

				using var entryStream = entry.Open();
				using var textReader = new StreamReader(entryStream, Encoding.UTF8);
				string manifestJson = textReader.ReadToEnd();
				return (manifestJson, rootPrefix);
			}
		}

		return (null, string.Empty);
	}

	public static (string? ManifestJson, string RootPrefix) ReadManifestFromArchive(string archiveFilePath)
	{
		if (string.IsNullOrWhiteSpace(archiveFilePath) || !File.Exists(archiveFilePath))
		{
			return (null, string.Empty);
		}

		using var zipArchive = ZipFile.OpenRead(archiveFilePath);
		return ReadManifestFromArchive(zipArchive);
	}

	public static void ExtractArchiveToCasAndTarget(
		ZipArchive zipArchive,
		ContentAddressableStorage cas,
		string targetDirectory,
		MapManifest manifest,
		string rootPrefix,
		Action<float>? progressCallback = null)
	{
		if (zipArchive == null) throw new ArgumentNullException(nameof(zipArchive));
		if (cas == null) throw new ArgumentNullException(nameof(cas));
		if (string.IsNullOrWhiteSpace(targetDirectory)) throw new ArgumentNullException(nameof(targetDirectory));

		if (!Directory.Exists(targetDirectory))
		{
			Directory.CreateDirectory(targetDirectory);
		}

		var casFileMap = BuildCasFileMap(manifest);

		var nonDirectoryEntries = zipArchive.Entries
			.Where(e => !(string.IsNullOrWhiteSpace(e.Name) && e.FullName.EndsWith("/")))
			.ToList();

		ProcessEntriesWithProgress(nonDirectoryEntries, rootPrefix, targetDirectory, cas, casFileMap, progressCallback);
		ProcessManifestLinks(manifest, targetDirectory, cas);
	}

	private static void ProcessEntriesWithProgress(
		List<ZipArchiveEntry> entries,
		string rootPrefix,
		string targetDirectory,
		ContentAddressableStorage cas,
		Dictionary<string, string> casFileMap,
		Action<float>? progressCallback)
	{
		int totalEntries = Math.Max(1, entries.Count);
		int processed = 0;
		long lastProgressReportTicks = 0;

		foreach (var entry in entries)
		{
			ProcessArchiveEntry(entry, rootPrefix, targetDirectory, cas, casFileMap);

			processed++;
			long now = Environment.TickCount64;
			if (now - lastProgressReportTicks >= 50 || processed == totalEntries)
			{
				lastProgressReportTicks = now;
				progressCallback?.Invoke((float)processed / totalEntries);
			}
		}
	}

	private static void ProcessManifestLinks(MapManifest manifest, string targetDirectory, ContentAddressableStorage cas)
	{
		if (manifest.Files == null) return;

		foreach (var kvp in manifest.Files)
		{
			ProcessManifestFileLink(kvp, targetDirectory, cas);
		}
	}

	private static readonly string[] ModelSubFolders = { "items", "attachments", "projectiles", "weapons", "props", "units", "buildings", "resources" };

	private static Dictionary<string, string> BuildCasFileMap(MapManifest manifest)
	{
		var casFileMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
		if (manifest.Files == null) return casFileMap;

		foreach (var kvp in manifest.Files)
		{
			string relKey = NormalizeManifestKey(kvp.Key);
			casFileMap[relKey] = ContentAddressableStorage.NormalizeBlake3Hash(kvp.Value);
		}

		foreach (var kvp in manifest.Files)
		{
			string relKey = NormalizeManifestKey(kvp.Key);
			if (!relKey.StartsWith("Assets/models/", StringComparison.OrdinalIgnoreCase)) continue;

			string normHash = ContentAddressableStorage.NormalizeBlake3Hash(kvp.Value);
			string fileName = Path.GetFileName(relKey);
			foreach (var sub in ModelSubFolders)
			{
				casFileMap.TryAdd($"Assets/models/{sub}/{fileName}", normHash);
			}
		}

		return casFileMap;
	}

	private static string NormalizeManifestKey(string manifestKey)
	{
		string relativeKey = manifestKey.StartsWith("res://", StringComparison.OrdinalIgnoreCase) ? manifestKey.Substring(6) : manifestKey;
		return relativeKey.TrimStart('/', '\\').Replace('\\', '/');
	}

	private static void ProcessManifestFileLink(KeyValuePair<string, string> kvp, string targetDirectory, ContentAddressableStorage cas)
	{
		string rel = kvp.Key.StartsWith("res://", StringComparison.OrdinalIgnoreCase) ? kvp.Key.Substring(6) : kvp.Key;
		rel = rel.TrimStart('/', '\\').Replace('\\', '/');
		string destFilePath = Path.Combine(targetDirectory, rel.Replace('/', Path.DirectorySeparatorChar));

		if (File.Exists(destFilePath)) return;

		string normHash = ContentAddressableStorage.NormalizeBlake3Hash(kvp.Value);
		string? casFilePath = cas.FindAssetFilePath(normHash);

		if (casFilePath != null)
		{
			HardLinkHelper.CreateHardLinkOrCopy(destFilePath, casFilePath, overwrite: false);
		}
	}

	private static void ProcessArchiveEntry(
		ZipArchiveEntry entry,
		string rootPrefix,
		string targetDirectory,
		ContentAddressableStorage cas,
		Dictionary<string, string> casFileMap)
	{
		WriteEntryToTarget(entry, rootPrefix, targetDirectory, cas, casFileMap);
	}

	private static void WriteEntryToTarget(
		ZipArchiveEntry entry,
		string rootPrefix,
		string targetDirectory,
		ContentAddressableStorage cas,
		Dictionary<string, string> casFileMap)
	{
		string norm = entry.FullName.Replace('\\', '/');
		if (!string.IsNullOrEmpty(rootPrefix) && norm.StartsWith(rootPrefix, StringComparison.OrdinalIgnoreCase))
		{
			norm = norm.Substring(rootPrefix.Length);
		}

		norm = norm.TrimStart('/');
		if (string.IsNullOrEmpty(norm)) return;

		string destFilePath = Path.Combine(targetDirectory, norm.Replace('/', Path.DirectorySeparatorChar));

		string? destDir = Path.GetDirectoryName(destFilePath);
		if (!string.IsNullOrEmpty(destDir) && !Directory.Exists(destDir))
		{
			Directory.CreateDirectory(destDir);
		}

		using (var outStream = new FileStream(destFilePath, FileMode.Create, FileAccess.Write, FileShare.None, 81920))
		using (var inStream = entry.Open())
		{
			inStream.CopyTo(outStream, 81920);
		}

		if (!casFileMap.TryGetValue(norm, out string? normHash) || string.IsNullOrEmpty(normHash)) return;

		string extension = Path.GetExtension(norm).ToLowerInvariant();
		if (!cas.HasAsset(normHash))
		{
			cas.StoreAssetFromFile(destFilePath, normHash);
		}

		string? casFilePath = cas.FindAssetFilePath(normHash);
		if (casFilePath == null || !File.Exists(casFilePath)) return;

		if (ContentAddressableStorage.FilesHaveIdenticalContent(destFilePath, casFilePath))
		{
			HardLinkHelper.CreateHardLinkOrCopy(destFilePath, casFilePath, overwrite: true);
			return;
		}

		Console.Error.WriteLine($"[MapArchiveHelper] Archive entry '{norm}' does not match the cached asset '{normHash}{extension}'. Keeping the archive copy.");
	}

	public static void ExtractArchiveIntoCas(string archiveFilePath, ContentAddressableStorage cas, Action<float>? progressCallback = null)
	{
		if (string.IsNullOrWhiteSpace(archiveFilePath) || !File.Exists(archiveFilePath))
		{
			throw new FileNotFoundException($"Archive file '{archiveFilePath}' not found.");
		}

		if (cas == null)
		{
			throw new ArgumentNullException(nameof(cas));
		}

		var (manifestJson, rootPrefix) = ReadManifestFromArchive(archiveFilePath);
		if (string.IsNullOrWhiteSpace(manifestJson))
		{
			return;
		}

		var manifest = MapManifest.LoadFromJson(manifestJson);
		if (manifest == null || manifest.Files == null || manifest.Files.Count == 0)
		{
			return;
		}

		using var zipArchive = ZipFile.OpenRead(archiveFilePath);

		var entriesByKey = BuildEntriesMap(zipArchive, rootPrefix);
		ProcessManifestFilesIntoCas(manifest, entriesByKey, cas, progressCallback);
	}

	private static Dictionary<string, ZipArchiveEntry> BuildEntriesMap(ZipArchive zipArchive, string rootPrefix)
	{
		var entriesByKey = new Dictionary<string, ZipArchiveEntry>(StringComparer.OrdinalIgnoreCase);
		foreach (var entry in zipArchive.Entries)
		{
			string norm = entry.FullName.Replace('\\', '/');
			if (!string.IsNullOrEmpty(rootPrefix) && norm.StartsWith(rootPrefix, StringComparison.OrdinalIgnoreCase))
			{
				norm = norm.Substring(rootPrefix.Length);
			}

			norm = norm.TrimStart('/');
			entriesByKey[norm] = entry;
		}

		return entriesByKey;
	}

	private static void ProcessManifestFilesIntoCas(
		MapManifest manifest,
		Dictionary<string, ZipArchiveEntry> entriesByKey,
		ContentAddressableStorage cas,
		Action<float>? progressCallback)
	{
		int total = manifest.Files!.Count;
		int processed = 0;

		foreach (var kvp in manifest.Files)
		{
			string relPath = kvp.Key.StartsWith("res://", StringComparison.OrdinalIgnoreCase)
				? kvp.Key.Substring(6)
				: kvp.Key;
			relPath = relPath.TrimStart('/', '\\').Replace('\\', '/');

			string hashOrKey = kvp.Value;
			string normHash = ContentAddressableStorage.NormalizeBlake3Hash(hashOrKey);

			if (!cas.HasAsset(normHash) && entriesByKey.TryGetValue(relPath, out var zipEntry))
			{
				using var entryStream = zipEntry.Open();
				string ext = Path.GetExtension(relPath).ToLowerInvariant();
				cas.StoreAsset(entryStream, ext, precomputedBlake3: normHash);
			}

			processed++;
			if (total > 0)
			{
				progressCallback?.Invoke((float)processed / total);
			}
		}
	}

	public static void ExtractArchive(string archiveFilePath, string targetDirectory, Action<float>? progressCallback = null)
	{
		if (string.IsNullOrWhiteSpace(archiveFilePath) || !File.Exists(archiveFilePath))
		{
			throw new FileNotFoundException($"Archive file '{archiveFilePath}' not found.");
		}

		if (!Directory.Exists(targetDirectory))
		{
			Directory.CreateDirectory(targetDirectory);
		}

		using var zipArchive = ZipFile.OpenRead(archiveFilePath);
		var (_, rootPrefix) = ReadManifestFromArchive(zipArchive);
		var fileEntries = zipArchive.Entries
			.Where(e => !(string.IsNullOrWhiteSpace(e.Name) && e.FullName.EndsWith("/")))
			.ToList();
		int total = fileEntries.Count;
		int processed = 0;

		foreach (var entry in fileEntries)
		{
			string relativePath = StripRootPrefix(entry.FullName.Replace('\\', '/'), rootPrefix);
			if (string.IsNullOrEmpty(relativePath)) continue;

			string destinationPath = Path.Combine(targetDirectory, relativePath.Replace('/', Path.DirectorySeparatorChar));
			string? destinationDir = Path.GetDirectoryName(destinationPath);
			if (!string.IsNullOrEmpty(destinationDir) && !Directory.Exists(destinationDir))
			{
				Directory.CreateDirectory(destinationDir);
			}

			using (var outStream = new FileStream(destinationPath, FileMode.Create, FileAccess.Write, FileShare.None, 81920))
			using (var entryStream = entry.Open())
			{
				entryStream.CopyTo(outStream, 81920);
			}

			processed++;
			if (total > 0)
			{
				progressCallback?.Invoke((float)processed / total);
			}
		}
	}

	private static string StripRootPrefix(string entryPath, string rootPrefix)
	{
		string normalized = entryPath;
		if (!string.IsNullOrEmpty(rootPrefix) && normalized.StartsWith(rootPrefix, StringComparison.OrdinalIgnoreCase))
		{
			normalized = normalized.Substring(rootPrefix.Length);
		}

		return normalized.TrimStart('/');
	}

	private static bool IsWorkspaceApiLibFile(string fileName)
	{
		return fileName.Equals("Realm.MapAPI.dll", StringComparison.OrdinalIgnoreCase) ||
		       fileName.Equals("Realm.MapAPI.pdb", StringComparison.OrdinalIgnoreCase) ||
		       fileName.Equals("Realm.MapAPI.xml", StringComparison.OrdinalIgnoreCase) ||
		       fileName.Equals("Realm.Ecs.dll", StringComparison.OrdinalIgnoreCase) ||
		       fileName.Equals("Realm.Ecs.pdb", StringComparison.OrdinalIgnoreCase);
	}

	private static int GetFilePriority(string relPath)
	{
		if (relPath.Equals("manifest.json", StringComparison.OrdinalIgnoreCase)) return 0;
		if (relPath.Equals("metadata.json", StringComparison.OrdinalIgnoreCase)) return 1;
		return 10;
	}	/// <summary>
	///     Leaves the map carrying nothing but the one compiled script it runs: every other build artifact under
	///     <c>bin</c> is removed, so an export can only ever ship the artifact the last compile wrote.
	/// </summary>
	/// <param name="workspacePath">The map workspace.</param>
	public static void DeduplicateWorkspaceBuildArtifacts(string workspacePath)
	{
		if (string.IsNullOrEmpty(workspacePath) || !Directory.Exists(workspacePath)) return;

		try
		{
			string binDir = Path.Combine(workspacePath, "bin");
			if (Directory.Exists(binDir))
			{
				string scriptDirectory = MapFileService.ResolveScriptOutputDirectory(workspacePath);

				foreach (string entry in Directory.GetFileSystemEntries(binDir))
				{
					if (string.Equals(entry, scriptDirectory, StringComparison.OrdinalIgnoreCase)) continue;
					DeleteEntry(entry);
				}

				if (Directory.Exists(scriptDirectory))
				{
					foreach (string file in Directory.GetFiles(scriptDirectory, "Realm.*", SearchOption.TopDirectoryOnly))
					{
						if (!IsWorkspaceApiLibFile(Path.GetFileName(file))) continue;

						try { File.Delete(file); }
						catch (Exception exception)
						{
							Console.Error.WriteLine($"[MapArchiveHelper] could not remove the stale API library {file}: {exception.GetType().Name}: {exception.Message}");
						}
					}

					string retiredFlavorMarker = Path.Combine(scriptDirectory, MapFileService.RetiredBuildFlavorFileName);
					if (File.Exists(retiredFlavorMarker))
					{
						try { File.Delete(retiredFlavorMarker); }
						catch (Exception exception)
						{
							Console.Error.WriteLine($"[MapArchiveHelper] could not remove the retired build marker {retiredFlavorMarker}: {exception.GetType().Name}: {exception.Message}");
						}
					}
				}
			}

			var elfFiles = Directory.GetFiles(workspacePath, "*.elf", SearchOption.AllDirectories);
			foreach (var file in elfFiles)
			{
				try { File.Delete(file); }
				catch (Exception exception)
				{
					Console.Error.WriteLine($"[MapArchiveHelper] could not remove the stray ELF {file}: {exception.GetType().Name}: {exception.Message}");
				}
			}
		}
		catch (Exception ex)
		{
			Console.Error.WriteLine($"[MapArchiveHelper] could not deduplicate the workspace build artifacts under {workspacePath}: {ex.GetType().Name}: {ex.Message}");
		}
	}

	private static void DeleteEntry(string path)
	{
		try
		{
			if (Directory.Exists(path)) Directory.Delete(path, true);
			else File.Delete(path);
		}
		catch
		{
		}
	}
}