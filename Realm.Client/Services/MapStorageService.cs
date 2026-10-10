using Godot;
using Realm.Ecs.Services;
using Realm.Client.Network;
using Realm.Shared.Distribution;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Realm.Client.Services;

public class MapStorageService
{
    private readonly WorldAccessor _ecsWorldAccessor;

    public MapStorageService(WorldAccessor ecsWorldAccessor)
    {
        _ecsWorldAccessor = ecsWorldAccessor;
    }

    public static string FormatBytes(long bytes)
    {
        if (bytes < 1024) return $"{bytes} B";
        if (bytes < 1024 * 1024) return $"{(bytes / 1024.0):F1} KB";
        if (bytes < 1024 * 1024 * 1024) return $"{(bytes / (1024.0 * 1024.0)):F1} MB";
        return $"{(bytes / (1024.0 * 1024.0 * 1024.0)):F2} GB";
    }

    public IReadOnlyList<DownloadedMapInfo> GetDownloadedMaps()
    {
        var mapDictionary = new Dictionary<string, DownloadedMapInfo>(StringComparer.OrdinalIgnoreCase);
        var searchRoots = new List<string>();

        string globalArchive = MapAssetManager.GlobalArchiveDirectory;
        if (Directory.Exists(globalArchive))
        {
            searchRoots.Add(globalArchive);
        }

        try
        {
            string resMaps = ProjectSettings.GlobalizePath("res://Maps");
            if (Directory.Exists(resMaps) && !searchRoots.Contains(resMaps, StringComparer.OrdinalIgnoreCase))
            {
                searchRoots.Add(resMaps);
            }
        }
        catch { }

        foreach (string root in searchRoots)
        {
            if (!Directory.Exists(root)) continue;

            ProcessTopLevelFiles(root, mapDictionary);
            ProcessMapFolders(root, mapDictionary);
        }

        var result = mapDictionary.Values.Where(m => m.Versions.Count > 0).ToList();
        foreach (var map in result)
        {
            map.Versions.Sort((a, b) => CompareVersionsDescending(a.Version, b.Version));
        }

        result.Sort((a, b) => string.Compare(a.Title, b.Title, StringComparison.OrdinalIgnoreCase));
        MapAssetManager.Log($"[MapStorageService] Discovered {result.Count} downloaded map(s).");
        return result;
    }

    private void ProcessTopLevelFiles(string root, Dictionary<string, DownloadedMapInfo> mapDictionary)
    {
        string[] topLevelFiles = Array.Empty<string>();
        try
        {
            topLevelFiles = Directory.GetFiles(root, "*.json", SearchOption.TopDirectoryOnly);
        }
        catch { return; }

        foreach (string filePath in topLevelFiles)
        {
            string fileName = Path.GetFileName(filePath);
            if (IsIgnoredTopLevelFile(fileName)) continue;

            TryProcessManifestFile(filePath, root, mapDictionary);
        }
    }

    private bool IsIgnoredTopLevelFile(string fileName)
    {
        return fileName.StartsWith(".") ||
               string.Equals(fileName, "pck_cache.json", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(fileName, "servers.json", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(fileName, "asset_index.cache", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(fileName, "settings.json", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(fileName, "editor_settings.json", StringComparison.OrdinalIgnoreCase);
    }

    private void ProcessMapFolders(string root, Dictionary<string, DownloadedMapInfo> mapDictionary)
    {
        string[] mapFolders = Array.Empty<string>();
        try
        {
            mapFolders = Directory.GetDirectories(root);
        }
        catch { return; }

        foreach (string mapFolder in mapFolders)
        {
            string folderName = Path.GetFileName(mapFolder);
            if (IsIgnoredFolder(folderName)) continue;

            ProcessMapFolder(mapFolder, mapDictionary);
        }
    }

    private bool IsIgnoredFolder(string folderName)
    {
        return folderName.StartsWith(".") ||
               string.Equals(folderName, "assets", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(folderName, "temp_map_workspace", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(folderName, "user_p2p_cache", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(folderName, "p2p_cache", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(folderName, "bin", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(folderName, "obj", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(folderName, "temp_pck", StringComparison.OrdinalIgnoreCase);
    }

    private void ProcessMapFolder(string mapFolder, Dictionary<string, DownloadedMapInfo> mapDictionary)
    {
        var candidateFiles = new List<string>();
        try
        {
            var foundManifests = Directory.GetFiles(mapFolder, "manifest.json", SearchOption.AllDirectories);
            candidateFiles.AddRange(foundManifests);
        }
        catch { }

        if (candidateFiles.Count == 0)
        {
            try
            {
                var foundMetadatas = Directory.GetFiles(mapFolder, "metadata.json", SearchOption.AllDirectories);
                candidateFiles.AddRange(foundMetadatas);
            }
            catch { }
        }

        foreach (string candidatePath in candidateFiles)
        {
            TryProcessManifestFile(candidatePath, mapFolder, mapDictionary);
        }
    }

    private void TryProcessManifestFile(string filePath, string fallbackFolder, Dictionary<string, DownloadedMapInfo> mapDictionary)
    {
        try
        {
            string jsonText = File.ReadAllText(filePath);
            if (string.IsNullOrWhiteSpace(jsonText)) return;

            string fileName = Path.GetFileName(filePath);
            string versionDir = Path.GetDirectoryName(filePath) ?? fallbackFolder;
            string mapFolderName = Path.GetFileName(fallbackFolder);

            var manifest = MapManifest.LoadFromJson(jsonText);
            if (IsManifestValid(manifest) && !ValidateManifestFilesExist(manifest!, versionDir))
            {
                return;
            }

            ExtractManifestData(manifest, jsonText, fileName, mapFolderName, out string mapName, out string author, out string version, out string description, out List<string> tags, out string manifestHash);

            long versionSize = CalculateDirectorySize(versionDir);
            var versionInfo = new DownloadedMapVersionInfo
            {
                Version = version,
                ManifestHash = manifestHash,
                DirectoryPath = versionDir,
                ManifestFilePath = filePath,
                TotalSizeBytes = versionSize,
                FormattedSize = FormatBytes(versionSize),
                LastModified = File.GetLastWriteTime(filePath)
            };

            UpdateMapDictionary(mapDictionary, versionDir, mapName, author, description, tags, manifestHash, version, versionInfo);
        }
        catch (Exception ex)
        {
            MapAssetManager.LogErr($"[MapStorageService] Error parsing map manifest {filePath}: {ex.Message}");
        }
    }

    private void ExtractManifestData(MapManifest? manifest, string jsonText, string fileName, string mapFolderName, out string mapName, out string author, out string version, out string description, out List<string> tags, out string manifestHash)
    {
        tags = new List<string>();
        if (IsManifestValid(manifest))
        {
            mapName = !string.IsNullOrWhiteSpace(manifest!.MapName) ? manifest.MapName.Trim() : mapFolderName;
            author = !string.IsNullOrWhiteSpace(manifest.Author) ? manifest.Author.Trim() : "Unknown";
            version = !string.IsNullOrWhiteSpace(manifest.Version) ? manifest.Version.Trim() : "1.0.0";
            description = !string.IsNullOrWhiteSpace(manifest.Description) ? manifest.Description : string.Empty;
            if (manifest.Tags != null) tags.AddRange(manifest.Tags);
            manifestHash = MapAssetManager.ComputeManifestBlake3(manifest);
        }
        else
        {
            ExtractFallbackManifestInfo(jsonText, fileName, mapFolderName, out mapName, out author, out version, out description, out manifestHash);
        }

        if (string.IsNullOrWhiteSpace(mapName))
        {
            mapName = mapFolderName;
        }
    }

    private bool IsManifestValid(MapManifest? manifest)
    {
        return manifest != null && (!string.IsNullOrWhiteSpace(manifest.MapName) || manifest.Assets != null || (manifest.Files != null && manifest.Files.Count > 0));
    }

    private bool ValidateManifestFilesExist(MapManifest manifest, string versionDir)
    {
        if (manifest.Files == null || manifest.Files.Count == 0) return true;

        var missing = MapAssetManager.GetMissingHashes(manifest.Files.Values);
        if (missing.Count == 0) return true;

        bool allFilesOnDisk = true;
        foreach (var kvp in manifest.Files)
        {
            string rel = kvp.Key.StartsWith("res://", StringComparison.OrdinalIgnoreCase) ? kvp.Key.Substring(6) : kvp.Key;
            rel = rel.TrimStart('/', '\\');
            if (!File.Exists(Path.Combine(versionDir, rel)))
            {
                allFilesOnDisk = false;
                break;
            }
        }

        return allFilesOnDisk || missing.Count != manifest.Files.Count;
    }

    private void ExtractFallbackManifestInfo(string jsonText, string fileName, string mapFolderName, out string mapName, out string author, out string version, out string description, out string manifestHash)
    {
        using var jsonDoc = JsonDocument.Parse(jsonText);
        var root = jsonDoc.RootElement;

        mapName = ExtractFallbackMapName(root, fileName, mapFolderName);
        author = ExtractFallbackAuthor(root);
        version = ExtractFallbackVersion(root);
        description = ExtractFallbackDescription(root);
        manifestHash = string.Empty;

        try
        {
            manifestHash = RealmMetadataHelper.ComputeBlake3(System.Text.Encoding.UTF8.GetBytes(jsonText), ".json");
        }
        catch { }
    }

    private string ExtractFallbackMapName(JsonElement root, string fileName, string mapFolderName)
    {
        string mapName = TryExtractMapNameFromJson(root);
        if (!string.IsNullOrWhiteSpace(mapName))
            return mapName;

        return TryExtractMapNameFromFileName(fileName, mapFolderName);
    }

    private string TryExtractMapNameFromJson(JsonElement root)
    {
        if (root.TryGetProperty("MapName", out var mnProp) && mnProp.ValueKind == JsonValueKind.String)
        {
            return mnProp.GetString() ?? string.Empty;
        }

        if (root.TryGetProperty("MapProperties", out var mp) && mp.TryGetProperty("MapName", out var mpn) && mpn.ValueKind == JsonValueKind.String)
        {
            return mpn.GetString() ?? string.Empty;
        }

        return string.Empty;
    }

    private string TryExtractMapNameFromFileName(string fileName, string mapFolderName)
    {
        if (fileName.EndsWith("_manifest.json", StringComparison.OrdinalIgnoreCase))
            return fileName.Substring(0, fileName.Length - "_manifest.json".Length);
            
        if (fileName.EndsWith(".manifest.json", StringComparison.OrdinalIgnoreCase))
            return fileName.Substring(0, fileName.Length - ".manifest.json".Length);
            
        if (fileName.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
            return fileName.Substring(0, fileName.Length - ".json".Length);

        return mapFolderName;
    }

    private string ExtractFallbackAuthor(JsonElement root)
    {
        if (root.TryGetProperty("Author", out var aProp) && aProp.ValueKind == JsonValueKind.String)
        {
            return aProp.GetString() ?? "Unknown";
        }
        return "Unknown";
    }

    private string ExtractFallbackVersion(JsonElement root)
    {
        if (root.TryGetProperty("Version", out var vProp) && vProp.ValueKind == JsonValueKind.String)
        {
            return vProp.GetString() ?? "1.0.0";
        }
        return "1.0.0";
    }

    private string ExtractFallbackDescription(JsonElement root)
    {
        if (root.TryGetProperty("Description", out var dProp) && dProp.ValueKind == JsonValueKind.String)
        {
            return dProp.GetString() ?? string.Empty;
        }
        if (root.TryGetProperty("MapProperties", out var mProps) && mProps.TryGetProperty("MapDescription", out var mdProp) && mdProp.ValueKind == JsonValueKind.String)
        {
            return mdProp.GetString() ?? string.Empty;
        }
        return string.Empty;
    }

    private void UpdateMapDictionary(Dictionary<string, DownloadedMapInfo> mapDictionary, string versionDir, string mapName, string author, string description, List<string> tags, string manifestHash, string version, DownloadedMapVersionInfo versionInfo)
    {
        if (!mapDictionary.TryGetValue(mapName, out var mapInfo))
        {
            mapInfo = new DownloadedMapInfo
            {
                Title = mapName,
                Author = author,
                Description = description,
                Tags = tags,
                ThumbnailPath = FindThumbnailPath(versionDir)
            };
            mapDictionary[mapName] = mapInfo;
        }

        if (string.IsNullOrEmpty(mapInfo.ThumbnailPath))
        {
            mapInfo.ThumbnailPath = FindThumbnailPath(versionDir);
        }

        if (!mapInfo.Versions.Any(v => string.Equals(v.Version, version, StringComparison.OrdinalIgnoreCase) &&
                                       (string.IsNullOrEmpty(manifestHash) || string.IsNullOrEmpty(v.ManifestHash) || string.Equals(v.ManifestHash, manifestHash, StringComparison.OrdinalIgnoreCase))))
        {
            mapInfo.Versions.Add(versionInfo);
        }
    }

    public bool DeleteMapVersion(string mapTitle, string mapVersion, string? manifestHash = null)
    {
        if (string.IsNullOrWhiteSpace(mapTitle) || string.IsNullOrWhiteSpace(mapVersion))
        {
            return false;
        }

        string? targetDir = FindTargetDirectory(mapTitle, mapVersion, manifestHash);

        if (string.IsNullOrEmpty(targetDir) || !Directory.Exists(targetDir))
        {
            return false;
        }

        try
        {
            Directory.Delete(targetDir, true);
            CleanupEmptyParentDirectories(targetDir);

            if (!string.IsNullOrEmpty(manifestHash))
            {
                MapAssetManager.Storage.RemoveSidecarCache(manifestHash);
            }

            MapAssetManager.PruneGlobalArchiveSync();
            return true;
        }
        catch (Exception ex)
        {
            MapAssetManager.LogErr($"[MapStorageService] Error deleting map version '{mapTitle}' v{mapVersion}: {ex.Message}");
            return false;
        }
    }

    private string? FindTargetDirectory(string mapTitle, string mapVersion, string? manifestHash)
    {
        string? manifestPath = MapAssetManager.FindManifestPath(mapTitle, mapVersion, manifestHash);
        if (manifestPath != null && File.Exists(manifestPath))
        {
            return Path.GetDirectoryName(manifestPath);
        }

        string globalArchive = MapAssetManager.GlobalArchiveDirectory;
        string candidate = Path.Combine(globalArchive, mapTitle, mapVersion);
        
        if (Directory.Exists(candidate))
        {
            return candidate;
        }
        
        if (!string.IsNullOrEmpty(manifestHash))
        {
            string normHash = ContentAddressableStorage.NormalizeBlake3Hash(manifestHash);
            string candidateWithHash = Path.Combine(globalArchive, mapTitle, mapVersion, normHash);
            if (Directory.Exists(candidateWithHash))
            {
                return candidateWithHash;
            }
        }
        
        return null;
    }

    private void CleanupEmptyParentDirectories(string targetDir)
    {
        string? parentDir = Path.GetDirectoryName(targetDir);
        if (!string.IsNullOrEmpty(parentDir) && Directory.Exists(parentDir) && Directory.GetFileSystemEntries(parentDir).Length == 0)
        {
            Directory.Delete(parentDir, true);
            string? grandparentDir = Path.GetDirectoryName(parentDir);
            if (!string.IsNullOrEmpty(grandparentDir) && Directory.Exists(grandparentDir) && Directory.GetFileSystemEntries(grandparentDir).Length == 0)
            {
                Directory.Delete(grandparentDir, true);
            }
        }
    }

    public async Task<(bool HasNewer, string? NewerVersion, string? MapId)> CheckForNewerVersionAsync(
        string mapTitle,
        List<string> localVersions,
        string? registryServerUrl = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(mapTitle)) return (false, null, null);

        string serverUrl = GetRegistryServerUrl(registryServerUrl);
        var distClient = new DistributionClient(serverUrl);

        try
        {
            var discoveryResult = await CheckDiscoveryMapsAsync(distClient, mapTitle, localVersions, cancellationToken);
            if (discoveryResult.MapId != null) return discoveryResult;

            return await CheckManifestAsync(distClient, mapTitle, localVersions, cancellationToken);
        }
        catch
        {
            return (false, null, null);
        }
    }

    private string GetRegistryServerUrl(string? registryServerUrl)
    {
        return !string.IsNullOrWhiteSpace(registryServerUrl)
            ? registryServerUrl
            : (GodotObject.IsInstanceValid(Network.LobbyManager.Instance) ? Network.LobbyManager.Instance.RegistryServerUrl : ServersConfigHelper.GetDefaultServerUrl());
    }

    private async Task<(bool HasNewer, string? NewerVersion, string? MapId)> CheckDiscoveryMapsAsync(DistributionClient distClient, string mapTitle, List<string> localVersions, CancellationToken cancellationToken)
    {
        var discoveryMaps = await distClient.GetDiscoveryMapsAsync(cancellationToken);
        var matchingDto = discoveryMaps.FirstOrDefault(m =>
            string.Equals(m.Title, mapTitle, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(m.MapId, mapTitle, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(m.Title.Replace('_', ' '), mapTitle.Replace('_', ' '), StringComparison.OrdinalIgnoreCase));

        if (matchingDto != null && !string.IsNullOrWhiteSpace(matchingDto.Version))
        {
            bool isNewer = IsVersionNewer(matchingDto.Version, localVersions);
            return (isNewer, isNewer ? matchingDto.Version : null, matchingDto.MapId);
        }
        return (false, null, null);
    }

    private async Task<(bool HasNewer, string? NewerVersion, string? MapId)> CheckManifestAsync(DistributionClient distClient, string mapTitle, List<string> localVersions, CancellationToken cancellationToken)
    {
        var manifest = await distClient.GetManifestAsync(mapTitle, cancellationToken);
        if (manifest != null && !string.IsNullOrWhiteSpace(manifest.Version))
        {
            bool isNewer = IsVersionNewer(manifest.Version, localVersions);
            return (isNewer, isNewer ? manifest.Version : null, mapTitle);
        }
        return (false, null, null);
    }

    public async Task<bool> DownloadUpdatedVersionAsync(
        string mapId,
        string? registryServerUrl = null,
        Action<float>? progressCallback = null,
        CancellationToken cancellationToken = default)
    {
        string serverUrl = !string.IsNullOrWhiteSpace(registryServerUrl)
            ? registryServerUrl
            : (GodotObject.IsInstanceValid(Network.LobbyManager.Instance) ? Network.LobbyManager.Instance.RegistryServerUrl : ServersConfigHelper.GetDefaultServerUrl());

        var distClient = new MapDistributionClient();
        return await distClient.DownloadMapPackageFromRegistryAsync(mapId, serverUrl, progressCallback, cancellationToken);
    }

    public async Task<bool> ExportMapAsync(string sourceDirectory, string destinationRmapPath, Action<float, string>? progressCallback = null, int compressionLevel = 1)
    {
        if (string.IsNullOrWhiteSpace(sourceDirectory) || !Directory.Exists(sourceDirectory))
        {
            return false;
        }

        try
        {
            MapWorkspaceService.EnsureLicenseFile(sourceDirectory);
            await Task.Run(() => MapArchiveHelper.CreateRmapArchive(
                sourceDirectory,
                destinationRmapPath,
                progressCallback: progressCallback,
                compressionLevel: compressionLevel));
            return true;
        }
        catch (Exception ex)
        {
            MapAssetManager.LogErr($"[MapStorageService] Export failed: {ex.Message}");
            return false;
        }
    }

    public Task<(bool Success, string Message, string? MapTitle, string? MapVersion)> ImportMapAsync(
        string sourcePath,
        Action<float>? progressCallback = null)
    {
        if (string.IsNullOrWhiteSpace(sourcePath))
        {
            return Task.FromResult((false, "Invalid source path provided.", (string?)null, (string?)null));
        }

        try
        {
            if (File.Exists(sourcePath))
            {
                if (sourcePath.EndsWith(".rmap", StringComparison.OrdinalIgnoreCase))
                {
                    return ImportMapFromArchiveAsync(sourcePath, progressCallback);
                }
                else if (sourcePath.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
                {
                    string folder = Path.GetDirectoryName(sourcePath) ?? sourcePath;
                    return ImportMapFromFolderAsync(folder, progressCallback);
                }
                else
                {
                    return Task.FromResult((false, "Unsupported file format. Please select an .rmap map package or a map folder.", (string?)null, (string?)null));
                }
            }
            else if (Directory.Exists(sourcePath))
            {
                return ImportMapFromFolderAsync(sourcePath, progressCallback);
            }
            else
            {
                return Task.FromResult((false, $"Specified source path does not exist: {sourcePath}", (string?)null, (string?)null));
            }
        }
        catch (Exception ex)
        {
            MapAssetManager.LogErr($"[MapStorageService] Import failed: {ex.Message}");
            return Task.FromResult((false, $"Import failed: {ex.Message}", (string?)null, (string?)null));
        }
    }

    private Task<(bool Success, string Message, string? MapTitle, string? MapVersion)> ImportMapFromArchiveAsync(
        string archivePath,
        Action<float>? progressCallback)
    {
        using var zipArchive = System.IO.Compression.ZipFile.OpenRead(archivePath);
        var (manifestJson, rootPrefix) = MapArchiveHelper.ReadManifestFromArchive(zipArchive);
        if (string.IsNullOrWhiteSpace(manifestJson))
        {
            return Task.FromResult((false, "No manifest.json found in the selected map package.", (string?)null, (string?)null));
        }

        var manifest = MapManifest.LoadFromJson(manifestJson);
        if (manifest == null)
        {
            return Task.FromResult((false, "Failed to parse manifest.json.", (string?)null, (string?)null));
        }

        var headerInfo = MapArchiveHelper.ReadHeaderFromManifest(manifest);
        (string mapTitle, string mapVersion) = DetermineMapTitleAndVersion(manifest, headerInfo, archivePath);
        manifest.MapName = mapTitle;
        manifest.Version = mapVersion;

        string manifestBlake3 = MapAssetManager.ComputeManifestBlake3(manifest);
        byte[] manifestBytes = Encoding.UTF8.GetBytes(manifest.ToJson());
        MapAssetManager.Storage.StoreAsset(manifestBytes, ".json", precomputedBlake3: manifestBlake3);

        string targetDirectory = Path.Combine(MapAssetManager.GlobalArchiveDirectory, mapTitle, mapVersion, manifestBlake3);
        if (Directory.Exists(targetDirectory))
        {
            Directory.Delete(targetDirectory, true);
        }
        Directory.CreateDirectory(targetDirectory);

        MapArchiveHelper.ExtractArchiveToCasAndTarget(
            zipArchive,
            MapAssetManager.Storage,
            targetDirectory,
            manifest,
            rootPrefix,
            p => progressCallback?.Invoke(p * 0.90f)
        );

        string targetManifestPath = Path.Combine(targetDirectory, "manifest.json");
        File.WriteAllText(targetManifestPath, manifest.ToJson());

        var validation = ValidateImportedMap(targetDirectory, manifest);
        if (!validation.IsValid)
        {
            CleanupOnValidationFailure(targetDirectory);
            return Task.FromResult((false, validation.ErrorMessage, (string?)null, (string?)null));
        }

        progressCallback?.Invoke(0.95f);
        AssetIndexService.Instance.RegisterManifest(manifest, targetManifestPath, isP2P: false);
        progressCallback?.Invoke(1.0f);
        return Task.FromResult((true, "Map imported successfully.", (string?)mapTitle, (string?)mapVersion));
    }

    private (string MapTitle, string MapVersion) DetermineMapTitleAndVersion(MapManifest manifest, RmapHeaderInfo? headerInfo, string archivePath)
    {
        string mapTitle = headerInfo != null && !string.IsNullOrWhiteSpace(headerInfo.MapName)
            ? headerInfo.MapName.Trim()
            : (!string.IsNullOrWhiteSpace(manifest.MapName) ? manifest.MapName.Trim() : Path.GetFileNameWithoutExtension(archivePath));
        string mapVersion = headerInfo != null && !string.IsNullOrWhiteSpace(headerInfo.Version)
            ? headerInfo.Version.Trim()
            : (!string.IsNullOrWhiteSpace(manifest.Version) ? manifest.Version.Trim() : "1.0.0");
        return (mapTitle, mapVersion);
    }

    private void CleanupOnValidationFailure(string targetDirectory)
    {
        try
        {
            if (Directory.Exists(targetDirectory))
            {
                Directory.Delete(targetDirectory, true);
            }
        }
        catch { }
    }

    private static void FlattenRootPrefix(string targetDirectory, string rootPrefix)
    {
        string subDir = Path.Combine(targetDirectory, rootPrefix.Trim('/', '\\'));
        if (!Directory.Exists(subDir))
        {
            return;
        }

        foreach (string file in Directory.GetFiles(subDir, "*", SearchOption.AllDirectories))
        {
            string rel = Path.GetRelativePath(subDir, file);
            string dest = Path.Combine(targetDirectory, rel);
            string? dir = Path.GetDirectoryName(dest);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
            }
            if (File.Exists(dest))
            {
                File.Delete(dest);
            }
            File.Move(file, dest);
        }

        try
        {
            Directory.Delete(subDir, true);
        }
        catch
        {
        }
    }

    private Task<(bool Success, string Message, string? MapTitle, string? MapVersion)> ImportMapFromFolderAsync(
        string folderPath,
        Action<float>? progressCallback)
    {
        var manifestFiles = FindManifestFilesExcludingBackups(folderPath);
        if (manifestFiles.Count == 0)
        {
            return Task.FromResult((false, "No manifest.json found in the selected map folder.", (string?)null, (string?)null));
        }

        if (manifestFiles.Count > 1)
        {
            throw new InvalidOperationException($"Multiple manifest.json files found in the selected folder ({manifestFiles.Count} found). Import aborted.");
        }

        string manifestFilePath = manifestFiles[0];
        string manifestSourceDir = Path.GetDirectoryName(manifestFilePath) ?? folderPath;
        string manifestJson = File.ReadAllText(manifestFilePath);
        var manifest = MapManifest.LoadFromJson(manifestJson);
        if (manifest == null)
        {
            return Task.FromResult((false, "Failed to parse manifest.json.", (string?)null, (string?)null));
        }

        string mapTitle = !string.IsNullOrWhiteSpace(manifest.MapName)
            ? manifest.MapName.Trim()
            : Path.GetFileName(manifestSourceDir) ?? "ImportedMap";
        string mapVersion = !string.IsNullOrWhiteSpace(manifest.Version) ? manifest.Version.Trim() : "1.0.0";
        manifest.MapName = mapTitle;
        manifest.Version = mapVersion;

        string manifestBlake3 = MapAssetManager.ComputeManifestBlake3(manifest);
        byte[] manifestBytes = Encoding.UTF8.GetBytes(manifest.ToJson());
        MapAssetManager.Storage.StoreAsset(manifestBytes, ".json", precomputedBlake3: manifestBlake3);

        string targetDirectory = Path.Combine(MapAssetManager.GlobalArchiveDirectory, mapTitle, mapVersion, manifestBlake3);
        Directory.CreateDirectory(targetDirectory);

        string targetManifestPath = Path.Combine(targetDirectory, "manifest.json");
        File.WriteAllText(targetManifestPath, manifest.ToJson());

        ProcessImportedManifestFiles(manifest, manifestSourceDir, targetDirectory, progressCallback);

        CopyFolderCandidateFiles(manifestSourceDir, targetDirectory, manifest);

        var validation = ValidateImportedMap(targetDirectory, manifest);
        if (!validation.IsValid)
        {
            CleanupOnValidationFailure(targetDirectory);
            return Task.FromResult((false, validation.ErrorMessage, (string?)null, (string?)null));
        }

        AssetIndexService.Instance.RegisterManifest(manifest, targetManifestPath, isP2P: false);
        return Task.FromResult((true, "Map imported successfully.", (string?)mapTitle, (string?)mapVersion));
    }

    private void ProcessImportedManifestFiles(MapManifest manifest, string manifestSourceDir, string targetDirectory, Action<float>? progressCallback)
    {
        if (manifest.Files == null) return;

        int totalFiles = manifest.Files.Count;
        int processed = 0;
        long lastProgressReportTicks = 0;

        foreach (var kvp in manifest.Files)
        {
            ProcessSingleImportedFile(kvp.Key, kvp.Value, manifestSourceDir, targetDirectory);

            processed++;
            if (totalFiles > 0)
            {
                long now = System.Environment.TickCount64;
                if (now - lastProgressReportTicks >= 100 || processed == totalFiles)
                {
                    lastProgressReportTicks = now;
                    progressCallback?.Invoke((float)processed / totalFiles);
                }
            }
        }
    }

    private void ProcessSingleImportedFile(string fileKey, string hash, string manifestSourceDir, string targetDirectory)
    {
        string relativePath = GetRelativePath(fileKey);
        string normHash = ContentAddressableStorage.NormalizeBlake3Hash(hash);
        string candidateFilePath = GetCandidateFilePath(manifestSourceDir, relativePath);
        
        string? casFilePath = EnsureAssetInCas(candidateFilePath, normHash);
        
        CreateDestinationLink(targetDirectory, relativePath, casFilePath, candidateFilePath);
    }

    private string GetRelativePath(string fileKey)
    {
        string relativePath = fileKey.StartsWith("res://", StringComparison.OrdinalIgnoreCase)
            ? fileKey.Substring(6)
            : fileKey;
        return relativePath.TrimStart('/', '\\');
    }

    private string GetCandidateFilePath(string manifestSourceDir, string relativePath)
    {
        string candidateFilePath = Path.Combine(manifestSourceDir, relativePath);
        if (File.Exists(candidateFilePath)) return candidateFilePath;

        string fileName = Path.GetFileName(relativePath);
        string altPath = Path.Combine(manifestSourceDir, "Assets", fileName);
        return File.Exists(altPath) ? altPath : candidateFilePath;
    }

    private string? EnsureAssetInCas(string candidateFilePath, string normHash)
    {
        string? casFilePath = MapAssetManager.Storage.FindAssetFilePath(normHash);
        if ((casFilePath == null || !File.Exists(casFilePath)) && File.Exists(candidateFilePath))
        {
            using var fileStream = new FileStream(candidateFilePath, System.IO.FileMode.Open, System.IO.FileAccess.Read, System.IO.FileShare.Read, 81920, System.IO.FileOptions.SequentialScan);
            string ext = Path.GetExtension(candidateFilePath).ToLowerInvariant();
            MapAssetManager.Storage.StoreAsset(fileStream, ext, precomputedBlake3: normHash);
            casFilePath = MapAssetManager.Storage.FindAssetFilePath(normHash);
        }
        return casFilePath;
    }

    private void CreateDestinationLink(string targetDirectory, string relativePath, string? casFilePath, string candidateFilePath)
    {
        string destFilePath = Path.Combine(targetDirectory, relativePath);
        string? destFileDir = Path.GetDirectoryName(destFilePath);
        if (!string.IsNullOrEmpty(destFileDir) && !Directory.Exists(destFileDir))
        {
            Directory.CreateDirectory(destFileDir);
        }

        if (casFilePath != null && File.Exists(casFilePath))
        {
            HardLinkHelper.CreateHardLinkOrCopy(destFilePath, casFilePath);
        }
        else if (File.Exists(candidateFilePath))
        {
            HardLinkHelper.CreateHardLinkOrCopy(destFilePath, candidateFilePath);
        }
    }

    private static void CopyFolderCandidateFiles(string sourceDir, string targetDir, MapManifest manifest)
    {
        if (!Directory.Exists(sourceDir))
        {
            return;
        }

        var candidateFiles = Directory.GetFiles(sourceDir, "*", SearchOption.AllDirectories)
            .Where(file => ShouldCopyCandidateFile(file, sourceDir, manifest));

        foreach (string file in candidateFiles)
        {
            CopySingleCandidateFile(file, sourceDir, targetDir);
        }
    }

    private static bool ShouldCopyCandidateFile(string file, string sourceDir, MapManifest manifest)
    {
        string rel = Path.GetRelativePath(sourceDir, file).Replace('\\', '/');
        if (IsIgnoredRelativePath(rel))
        {
            return false;
        }
        
        return manifest.IsCandidateFile(rel);
    }

    private static bool IsIgnoredRelativePath(string rel)
    {
        return rel.StartsWith(".git/", StringComparison.OrdinalIgnoreCase) ||
               rel.StartsWith(".backups/", StringComparison.OrdinalIgnoreCase) ||
               rel.StartsWith("obj/", StringComparison.OrdinalIgnoreCase) ||
               rel.Contains("/obj/", StringComparison.OrdinalIgnoreCase) ||
               rel.Equals("manifest.json", StringComparison.OrdinalIgnoreCase);
    }

    private static void CopySingleCandidateFile(string file, string sourceDir, string targetDir)
    {
        string rel = Path.GetRelativePath(sourceDir, file).Replace('\\', '/');
        string dest = Path.Combine(targetDir, rel.Replace('/', Path.DirectorySeparatorChar));
        string? parent = Path.GetDirectoryName(dest);
        
        if (!string.IsNullOrEmpty(parent) && !Directory.Exists(parent))
        {
            Directory.CreateDirectory(parent);
        }
        
        if (!File.Exists(dest))
        {
            HardLinkHelper.CreateHardLinkOrCopy(dest, file);
        }
    }

    private static (bool IsValid, string ErrorMessage) ValidateImportedMap(string targetDirectory, MapManifest manifest)
    {
        var wasmValidation = ValidateWasmFiles(targetDirectory);
        if (!wasmValidation.IsValid)
        {
            return wasmValidation;
        }

        var missingFilesValidation = ValidateMissingFiles(targetDirectory, manifest);
        if (!missingFilesValidation.IsValid)
        {
            return missingFilesValidation;
        }

        return (true, string.Empty);
    }

    private static (bool IsValid, string ErrorMessage) ValidateWasmFiles(string targetDirectory)
    {
        bool hasCsproj = Directory.GetFiles(targetDirectory, "*.csproj", SearchOption.TopDirectoryOnly).Length > 0;
        bool hasCsFiles = Directory.GetFiles(targetDirectory, "*.cs", SearchOption.AllDirectories).Any(f => !f.Contains("obj"));
        
        if (!hasCsproj && !hasCsFiles)
        {
            return (true, string.Empty);
        }

        var wasmFiles = Directory.GetFiles(targetDirectory, "*.wasm", SearchOption.AllDirectories)
            .Where(f => !f.Contains("native") && !f.Contains("obj"))
            .ToList();

        if (wasmFiles.Count == 0)
        {
            return (false, "Map is missing pre-compiled WASM binary. Maps containing scripts must be exported from the Map Editor before importing.");
        }

        return (true, string.Empty);
    }

    private static (bool IsValid, string ErrorMessage) ValidateMissingFiles(string targetDirectory, MapManifest manifest)
    {
        if (manifest.Files == null || manifest.Files.Count == 0)
        {
            return (true, string.Empty);
        }

        var missingFiles = new List<string>();
        foreach (var kvp in manifest.Files)
        {
            string rel = kvp.Key.StartsWith("res://", StringComparison.OrdinalIgnoreCase) ? kvp.Key.Substring(6) : kvp.Key;
            rel = rel.TrimStart('/', '\\');
            string destFilePath = Path.Combine(targetDirectory, rel);
            if (!File.Exists(destFilePath))
            {
                missingFiles.Add(rel);
            }
        }
        
        if (missingFiles.Count > 0)
        {
            string missingFilesStr = string.Join(", ", missingFiles.Take(5));
            string suffix = missingFiles.Count > 5 ? "..." : "";
            return (false, $"Map is missing required asset files: {missingFilesStr}{suffix}");
        }

        return (true, string.Empty);
    }

    private static string FindThumbnailPath(string versionDirectory)
    {
        string p = Path.Combine(versionDirectory, "thumbnail.png");
        if (File.Exists(p))
        {
            return p;
        }

        return string.Empty;
    }

    private static long CalculateDirectorySize(string directoryPath)
    {
        if (!Directory.Exists(directoryPath)) return 0;
        try
        {
            var dirInfo = new DirectoryInfo(directoryPath);
            long total = 0;
            foreach (var file in dirInfo.EnumerateFiles("*", SearchOption.AllDirectories))
            {
                total += file.Length;
            }
            return total;
        }
        catch
        {
            return 0;
        }
    }

    private static bool IsVersionNewer(string serverVersionStr, List<string> localVersionStrs)
    {
        if (string.IsNullOrWhiteSpace(serverVersionStr)) return false;

        if (!TryParseVersion(serverVersionStr, out var serverVer))
        {
            return !localVersionStrs.Any(lv => string.Equals(lv.Trim(), serverVersionStr.Trim(), StringComparison.OrdinalIgnoreCase));
        }

        foreach (var localStr in localVersionStrs)
        {
            if (TryParseVersion(localStr, out var localVer))
            {
                if (serverVer <= localVer)
                {
                    return false;
                }
            }
        }

        return true;
    }

    private static int CompareVersionsDescending(string v1, string v2)
    {
        bool p1 = TryParseVersion(v1, out var ver1);
        bool p2 = TryParseVersion(v2, out var ver2);

        if (p1 && p2)
        {
            return ver2.CompareTo(ver1);
        }
        return string.Compare(v2, v1, StringComparison.OrdinalIgnoreCase);
    }

    private static bool TryParseVersion(string vStr, out Version version)
    {
        vStr = vStr.Trim().TrimStart('v', 'V');
        if (Version.TryParse(vStr, out version!))
        {
            return true;
        }
        int dotCount = vStr.Count(c => c == '.');
        if (dotCount == 0 && int.TryParse(vStr, out int major))
        {
            version = new Version(major, 0);
            return true;
        }
        if (dotCount == 1 && Version.TryParse(vStr, out version!))
        {
            version = new Version(version.Major, version.Minor);
            return true;
        }
        version = new Version(0, 0);
        return false;
    }

    private static List<string> FindManifestFilesExcludingBackups(string rootDirectory)
    {
        var results = new List<string>();
        if (!Directory.Exists(rootDirectory))
        {
            return results;
        }

        var queue = new Queue<string>();
        queue.Enqueue(rootDirectory);

        while (queue.Count > 0)
        {
            string currentDir = queue.Dequeue();
            string candidate = Path.Combine(currentDir, "manifest.json");
            if (File.Exists(candidate))
            {
                results.Add(candidate);
            }

            try
            {
                foreach (string subDir in Directory.GetDirectories(currentDir))
                {
                    string dirName = Path.GetFileName(subDir);
                    if (string.Equals(dirName, ".backups", StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }
                    queue.Enqueue(subDir);
                }
            }
            catch
            {
            }
        }

        return results;
    }
}
