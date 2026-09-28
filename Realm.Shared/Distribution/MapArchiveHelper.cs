using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Text;
using System.Text.Json;
using Realm.Shared.Metadata;

namespace Realm.Shared.Distribution;

public class RmapHeaderInfo
{
    public string MapName { get; set; } = string.Empty;
    public string Version { get; set; } = "1.0.0";
    public string GameBuildNumber { get; set; } = string.Empty;
    public string Author { get; set; } = "Unknown";
    public string Description { get; set; } = string.Empty;
    public List<string> Tags { get; set; } = new();
}

public static class MapArchiveHelper
{
    public static readonly byte[] RmapMagic = [0x52, 0x4D, 0x41, 0x50]; // "RMAP"
    public const uint CurrentVersion = 1;

    public static void CreateRmapArchive(
        string sourceDirectory,
        string destinationRmapPath,
        Action<float, string>? progressCallback = null,
        int compressionLevel = 1)
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

        var allFiles = Directory.GetFiles(sourceDirectory, "*.*", SearchOption.AllDirectories);
        var filesToArchive = new List<string>(allFiles.Length);
        foreach (var file in allFiles)
        {
            string relativePath = Path.GetRelativePath(sourceDirectory, file).Replace('\\', '/');
            if (relativePath.StartsWith(".git/", StringComparison.OrdinalIgnoreCase) ||
                relativePath.StartsWith(".backups/", StringComparison.OrdinalIgnoreCase) ||
                relativePath.StartsWith("obj/", StringComparison.OrdinalIgnoreCase) ||
                relativePath.Contains("/obj/", StringComparison.OrdinalIgnoreCase) ||
                relativePath.StartsWith(".godot/", StringComparison.OrdinalIgnoreCase) ||
                relativePath.StartsWith(".sidecarcache/", StringComparison.OrdinalIgnoreCase) ||
                relativePath.StartsWith(".vscode/", StringComparison.OrdinalIgnoreCase) ||
                relativePath.EndsWith(".tmp", StringComparison.OrdinalIgnoreCase) ||
                relativePath.EndsWith(".rmap", StringComparison.OrdinalIgnoreCase) ||
                relativePath.EndsWith(".zip", StringComparison.OrdinalIgnoreCase) ||
                relativePath.EndsWith(".tar", StringComparison.OrdinalIgnoreCase) ||
                relativePath.EndsWith(".gz", StringComparison.OrdinalIgnoreCase) ||
                relativePath.EndsWith(".bak", StringComparison.OrdinalIgnoreCase) ||
                relativePath.EndsWith(".backup", StringComparison.OrdinalIgnoreCase) ||
                relativePath.EndsWith(".rkey", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(Path.GetFileName(relativePath), "authorship_key.pem", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var fileInfo = new FileInfo(file);
            if (!fileInfo.Exists || fileInfo.Length == 0)
            {
                continue;
            }

            filesToArchive.Add(file);
        }

        filesToArchive.Sort((a, b) =>
        {
            string relA = Path.GetRelativePath(sourceDirectory, a).Replace('\\', '/');
            string relB = Path.GetRelativePath(sourceDirectory, b).Replace('\\', '/');
            int priorityA = GetFilePriority(relA);
            int priorityB = GetFilePriority(relB);
            if (priorityA != priorityB) return priorityA.CompareTo(priorityB);
            return string.Compare(relA, relB, StringComparison.OrdinalIgnoreCase);
        });

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

            return new RmapHeaderInfo
            {
                MapName = manifest.MapName ?? string.Empty,
                Version = manifest.Version ?? "1.0.0",
                Author = manifest.Author ?? "Unknown",
                Description = manifest.Description ?? string.Empty,
                Tags = manifest.Tags ?? new List<string>()
            };
        }
        catch
        {
            return null;
        }
    }

    public static (string? ManifestJson, string RootPrefix) ReadManifestFromArchive(string archiveFilePath)
    {
        if (string.IsNullOrWhiteSpace(archiveFilePath) || !File.Exists(archiveFilePath))
        {
            return (null, string.Empty);
        }

        using var zipArchive = ZipFile.OpenRead(archiveFilePath);
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

    public static void ExtractArchiveIntoCas(string archiveFilePath, ContentAddressableStorage cas)
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

        foreach (var kvp in manifest.Files)
        {
            string relPath = kvp.Key.StartsWith("res://", StringComparison.OrdinalIgnoreCase)
                ? kvp.Key.Substring(6)
                : kvp.Key;
            relPath = relPath.TrimStart('/', '\\').Replace('\\', '/');

            string hashOrKey = kvp.Value;
            string normHash = ContentAddressableStorage.NormalizeBlake3Hash(hashOrKey);

            if (cas.HasAsset(normHash))
            {
                continue;
            }

            if (entriesByKey.TryGetValue(relPath, out var zipEntry))
            {
                using var entryStream = zipEntry.Open();
                string ext = Path.GetExtension(relPath).ToLowerInvariant();
                cas.StoreAsset(entryStream, ext, precomputedBlake3: normHash);
            }
        }
    }

    public static void ExtractArchive(string archiveFilePath, string targetDirectory)
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
        foreach (var entry in zipArchive.Entries)
        {
            if (string.IsNullOrWhiteSpace(entry.Name) && entry.FullName.EndsWith("/"))
            {
                continue;
            }

            string destinationPath = Path.Combine(targetDirectory, entry.FullName.Replace('/', Path.DirectorySeparatorChar));
            string? destinationDir = Path.GetDirectoryName(destinationPath);
            if (!string.IsNullOrEmpty(destinationDir) && !Directory.Exists(destinationDir))
            {
                Directory.CreateDirectory(destinationDir);
            }

            using var outStream = new FileStream(destinationPath, FileMode.Create, FileAccess.Write, FileShare.None, 81920);
            using var entryStream = entry.Open();
            entryStream.CopyTo(outStream, 81920);
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

        string manifestPath = Path.Combine(sourceDirectory, "manifest.json");
        if (File.Exists(manifestPath))
        {
            try
            {
                string jsonText = File.ReadAllText(manifestPath);
                var manifest = MapManifest.LoadFromJson(jsonText);
                if (manifest != null)
                {
                    if (!string.IsNullOrWhiteSpace(manifest.MapName)) mapName = manifest.MapName.Trim();
                    if (!string.IsNullOrWhiteSpace(manifest.Version)) version = manifest.Version.Trim();
                    if (!string.IsNullOrWhiteSpace(manifest.Author)) author = manifest.Author.Trim();
                    if (!string.IsNullOrWhiteSpace(manifest.Description)) description = manifest.Description;
                    if (manifest.Tags != null) tags.AddRange(manifest.Tags);
                }

                using var doc = JsonDocument.Parse(jsonText);
                if (doc.RootElement.TryGetProperty("GameBuildNumber", out var gbnProp) && gbnProp.ValueKind == JsonValueKind.String)
                {
                    string? gbn = gbnProp.GetString();
                    if (!string.IsNullOrWhiteSpace(gbn)) gameBuildNumber = gbn.Trim();
                }
            }
            catch
            {
            }
        }

        string metadataPath = Path.Combine(sourceDirectory, "metadata.json");
        if (File.Exists(metadataPath))
        {
            try
            {
                string metaText = File.ReadAllText(metadataPath);
                using var metaDoc = JsonDocument.Parse(metaText);
                if (metaDoc.RootElement.TryGetProperty("GameBuildNumber", out var gbnProp) && gbnProp.ValueKind == JsonValueKind.String)
                {
                    string? gbn = gbnProp.GetString();
                    if (!string.IsNullOrWhiteSpace(gbn)) gameBuildNumber = gbn.Trim();
                }
            }
            catch
            {
            }
        }

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

    private static int GetFilePriority(string relPath)
    {
        if (relPath.Equals("manifest.json", StringComparison.OrdinalIgnoreCase)) return 0;
        if (relPath.Equals("metadata.json", StringComparison.OrdinalIgnoreCase)) return 1;
        return 10;
    }
}
