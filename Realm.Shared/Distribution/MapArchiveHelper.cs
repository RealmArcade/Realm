using System.IO.Compression;
using System.Text;

namespace Realm.Shared.Distribution;

public static class MapArchiveHelper
{
    public static readonly byte[] RmapMagic = [0x52, 0x4D, 0x41, 0x50]; // "RMAP"
    public const uint CurrentVersion = 1;

    public static void CreateRmapArchive(
        string sourceDirectory,
        string destinationRmapPath,
        Action<float, string>? progressCallback = null,
        int compressionLevel = 1, // Note: Not currently used for files in code below, uses NoCompression, keeping for signature match.
        bool fullExport = false,
        IReadOnlyCollection<string>? excludedRelativePaths = null)
    {
        PrepareArchiveDestination(sourceDirectory, destinationRmapPath);

        var filesToArchive = GetFilesToArchive(sourceDirectory, fullExport, excludedRelativePaths);
        SortFilesForArchive(filesToArchive, sourceDirectory);

        WriteFilesToZipArchive(filesToArchive, sourceDirectory, destinationRmapPath, progressCallback);
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

    private static bool IsIgnoredPath(string relPath)
    {
        string[] prefixes = [".git/", ".backups/", "obj/", ".godot/", ".sidecarcache/", ".vscode/", ".vs/"];
        string[] suffixes = [".tmp", ".rmap", ".zip", ".tar", ".gz", ".bak", ".backup", ".rkey"];
        
        if (prefixes.Any(p => relPath.StartsWith(p, StringComparison.OrdinalIgnoreCase))) return true;
        if (suffixes.Any(s => relPath.EndsWith(s, StringComparison.OrdinalIgnoreCase))) return true;
        if (relPath.Contains("/obj/", StringComparison.OrdinalIgnoreCase)) return true;
        
        return string.Equals(Path.GetFileName(relPath), "authorship_key.pem", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsAllowedExcludedFile(string fn)
    {
        string[] allowedExact = ["manifest.json", "metadata.json", "terrain.json"];
        string[] allowedSuffixes = [".cs", ".wasm", ".csproj"];
        
        if (allowedExact.Any(a => fn.Equals(a, StringComparison.OrdinalIgnoreCase))) return true;
        if (allowedSuffixes.Any(s => fn.EndsWith(s, StringComparison.OrdinalIgnoreCase))) return true;
        
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
            string fn = Path.GetFileName(relativePath);
            if (!IsAllowedExcludedFile(fn)) return false;
        }

        var fileInfo = new FileInfo(file);
        if (!fileInfo.Exists || fileInfo.Length == 0) return false;

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

    private static void WriteFilesToZipArchive(List<string> filesToArchive, string sourceDirectory, string destinationRmapPath, Action<float, string>? progressCallback)
    {
        using var fileStream = new FileStream(destinationRmapPath, FileMode.Create, FileAccess.Write, FileShare.None, 81920);
        using var zipArchive = new ZipArchive(fileStream, ZipArchiveMode.Create, leaveOpen: false);

        int total = filesToArchive.Count;
        for (int i = 0; i < total; i++)
        {
            string file = filesToArchive[i];
            string relativePath = Path.GetRelativePath(sourceDirectory, file).Replace('\\', '/');
            progressCallback?.Invoke((float)(i + 1) / Math.Max(1, total), relativePath);

            var entry = zipArchive.CreateEntry(relativePath, CompressionLevel.NoCompression);
            using var entryStream = entry.Open();
            using var inputFs = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, FileOptions.SequentialScan);
            inputFs.CopyTo(entryStream, 81920);
        }
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
            long now = System.Environment.TickCount64;
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

    private static Dictionary<string, string> BuildCasFileMap(MapManifest manifest)
    {
        var casFileMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (manifest.Files == null) return casFileMap;

        foreach (var kvp in manifest.Files)
        {
            string relKey = kvp.Key.StartsWith("res://", StringComparison.OrdinalIgnoreCase) ? kvp.Key.Substring(6) : kvp.Key;
            relKey = relKey.TrimStart('/', '\\').Replace('\\', '/');
            string normHash = ContentAddressableStorage.NormalizeBlake3Hash(kvp.Value);
            casFileMap[relKey] = normHash;
        }

        return casFileMap;
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
        string norm = entry.FullName.Replace('\\', '/');
        if (!string.IsNullOrEmpty(rootPrefix) && norm.StartsWith(rootPrefix, StringComparison.OrdinalIgnoreCase))
        {
            norm = norm.Substring(rootPrefix.Length);
        }

        norm = norm.TrimStart('/');
        if (string.IsNullOrEmpty(norm)) return;

        string destFilePath = Path.Combine(targetDirectory, norm.Replace('/', Path.DirectorySeparatorChar));

        if (casFileMap.TryGetValue(norm, out string? normHash) && !string.IsNullOrEmpty(normHash))
        {
            if (!cas.HasAsset(normHash))
            {
                using var entryStream = entry.Open();
                string ext = Path.GetExtension(norm).ToLowerInvariant();
                cas.StoreAsset(entryStream, ext, precomputedBlake3: normHash);
            }

            string? casFilePath = cas.FindAssetFilePath(normHash);
            if (casFilePath != null)
            {
                HardLinkHelper.CreateHardLinkOrCopy(destFilePath, casFilePath, overwrite: false);
            }
            return;
        }

        string? destDir = Path.GetDirectoryName(destFilePath);
        if (!string.IsNullOrEmpty(destDir) && !Directory.Exists(destDir))
        {
            Directory.CreateDirectory(destDir);
        }

        using var outStream = new FileStream(destFilePath, FileMode.Create, FileAccess.Write, FileShare.None, 81920);
        using var inStream = entry.Open();
        inStream.CopyTo(outStream, 81920);
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
        var fileEntries = zipArchive.Entries
            .Where(e => !(string.IsNullOrWhiteSpace(e.Name) && e.FullName.EndsWith("/")))
            .ToList();
        int total = fileEntries.Count;
        int processed = 0;

        foreach (var entry in fileEntries)
        {
            string destinationPath = Path.Combine(targetDirectory, entry.FullName.Replace('/', Path.DirectorySeparatorChar));
            string? destinationDir = Path.GetDirectoryName(destinationPath);
            if (!string.IsNullOrEmpty(destinationDir) && !Directory.Exists(destinationDir))
            {
                Directory.CreateDirectory(destinationDir);
            }

            using var outStream = new FileStream(destinationPath, FileMode.Create, FileAccess.Write, FileShare.None, 81920);
            using var entryStream = entry.Open();
            entryStream.CopyTo(outStream, 81920);

            processed++;
            if (total > 0)
            {
                progressCallback?.Invoke((float)processed / total);
            }
        }
    }

    private static RmapHeaderInfo ExtractRmapHeaderInfoFromDirectory(string sourceDirectory)
    {
        string mapName = Path.GetFileName(sourceDirectory);
        string version = "1.0.0";
        string gameBuildNumber = RealmVersion.GameBuildNumber;
        string author = "Unknown";
        string description = string.Empty;
        var tags = new List<string>();

        ApplyManifestToHeaderInfo(sourceDirectory, ref mapName, ref version, ref author, ref description, tags);
        ApplyMetadataToHeaderInfo(sourceDirectory, ref gameBuildNumber);

        return new RmapHeaderInfo
        {
            MapName = mapName,
            Version = version,
            GameBuildNumber = string.IsNullOrWhiteSpace(gameBuildNumber) ? RealmVersion.GameBuildNumber : gameBuildNumber,
            Author = author,
            Description = description,
            Tags = tags
        };
    }

    private static void ApplyManifestToHeaderInfo(
        string sourceDirectory,
        ref string mapName,
        ref string version,
        ref string author,
        ref string description,
        List<string> tags)
    {
        string manifestPath = Path.Combine(sourceDirectory, "manifest.json");
        if (!File.Exists(manifestPath)) return;

        try
        {
            var manifest = Services.MapFileService.LoadManifest(manifestPath);
            if (!string.IsNullOrWhiteSpace(manifest.MapName)) mapName = manifest.MapName.Trim();
            if (!string.IsNullOrWhiteSpace(manifest.Version)) version = manifest.Version.Trim();
            if (!string.IsNullOrWhiteSpace(manifest.Author)) author = manifest.Author.Trim();
            if (!string.IsNullOrWhiteSpace(manifest.Description)) description = manifest.Description;
            if (manifest.Tags != null) tags.AddRange(manifest.Tags);
        }
        catch
        {
        }
    }

    private static void ApplyMetadataToHeaderInfo(string sourceDirectory, ref string gameBuildNumber)
    {
        string metadataPath = Path.Combine(sourceDirectory, "metadata.json");
        if (!File.Exists(metadataPath)) return;

        try
        {
            var metadata = Services.MapFileService.LoadMetadata(metadataPath);
            if (!string.IsNullOrWhiteSpace(metadata.GameBuildNumber))
            {
                gameBuildNumber = metadata.GameBuildNumber.Trim();
            }
        }
        catch
        {
        }
    }

    private static int GetFilePriority(string relPath)
    {
        if (relPath.Equals("manifest.json", StringComparison.OrdinalIgnoreCase)) return 0;
        if (relPath.Equals("metadata.json", StringComparison.OrdinalIgnoreCase)) return 1;
        return 10;
    }
}
