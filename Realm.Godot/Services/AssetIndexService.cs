using Godot;
using LiteDB;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using Realm.Shared.Metadata;
using JsonSerializer = System.Text.Json.JsonSerializer;

public class IndexedAsset
{
    public int Id { get; set; }
    public string FilePath { get; set; } = string.Empty;
    public string FileName { get; set; } = string.Empty;
    public string Extension { get; set; } = string.Empty;
    public string DirectoryPath { get; set; } = string.Empty;
    public long FileSizeBytes { get; set; }
    public DateTime LastModifiedUtc { get; set; }
    public List<string> Tags { get; set; } = new();
    public string MetadataJson { get; set; } = string.Empty;
    public bool HasRealmMetadata { get; set; }
    public string? AssetType { get; set; }
    public string? MapName { get; set; }
    public string? MapVersion { get; set; }
    public string? Blake3 { get; set; }
    public bool HasPlayerColorMask { get; set; }
    public string? ChromaKey { get; set; }
}

public class IndexedMapPackage
{
    public int Id { get; set; }
    public string MapName { get; set; } = string.Empty;
    public string MapVersion { get; set; } = string.Empty;
    public string ManifestPath { get; set; } = string.Empty;
    public DateTime DownloadedUtc { get; set; }
    public List<string> AssetHashes { get; set; } = new();
    public bool IsP2P { get; set; }
}

public class IndexedFolder
{
    public int Id { get; set; }
    public string DirectoryPath { get; set; } = string.Empty;
    public DateTime LastScannedUtc { get; set; }
}

public class AssetMetadataModel
{
    public List<string> Tags { get; set; } = new();
}

public record AssetIndexProgressUpdate(double ProgressPercentage, string Message);

public class AssetIndexService : IDisposable
{
    private static AssetIndexService? _instance;
    public static AssetIndexService Instance => _instance ??= ServiceLocator.TryGet<AssetIndexService>() ?? new AssetIndexService();

    public event Action<string, bool>? DirectoryIndexingStateChanged;
    public event Action<string>? DirectoryScanCompleted;

    private LiteDatabase _database;
    private ILiteCollection<IndexedAsset> _assetCollection;
    private ILiteCollection<IndexedFolder> _folderCollection;
    private ILiteCollection<IndexedMapPackage> _mapPackageCollection;
    private readonly object _syncLock = new();
    private readonly HashSet<string> _indexingDirectories = new(StringComparer.OrdinalIgnoreCase);

    private LiteDatabase? _p2pDatabase;
    private readonly object _p2pSyncLock = new();

    private static string GetUserDirectory()
    {
        if (MapAssetManager.IsGodotEngineRunning)
        {
            return ProjectSettings.GlobalizePath("user://");
        }
        else
        {
            string appData = System.Environment.GetFolderPath(System.Environment.SpecialFolder.ApplicationData);
            return Path.Combine(appData, "Godot", "app_userdata", "Realm");
        }
    }

    private static string GetCacheFilePath()
    {
        string userDirectory = GetUserDirectory();
        if (!Directory.Exists(userDirectory))
        {
            Directory.CreateDirectory(userDirectory);
        }
        return Path.Combine(userDirectory, "asset_index.cache");
    }

    private void EnsureIndexes()
    {
        _assetCollection.EnsureIndex(x => x.FilePath, true);
        _assetCollection.EnsureIndex(x => x.DirectoryPath);
        _assetCollection.EnsureIndex(x => x.Extension);
        _assetCollection.EnsureIndex(x => x.Tags);
        _assetCollection.EnsureIndex(x => x.FileName);
        _assetCollection.EnsureIndex(x => x.MapName);
        _assetCollection.EnsureIndex(x => x.MapVersion);
        _assetCollection.EnsureIndex(x => x.Blake3);
        _assetCollection.EnsureIndex(x => x.HasRealmMetadata);
        _assetCollection.EnsureIndex(x => x.AssetType);
        _assetCollection.EnsureIndex(x => x.HasPlayerColorMask);

        _folderCollection.EnsureIndex(x => x.DirectoryPath, true);

        _mapPackageCollection.EnsureIndex(x => x.MapName);
        _mapPackageCollection.EnsureIndex(x => x.MapVersion);
    }

    public string GetIndexGameBuildNumber()
    {
        lock (_syncLock)
        {
            try
            {
                var metaCol = _database.GetCollection<BsonDocument>("_metadata");
                var doc = metaCol.FindById(1);
                if (doc != null && doc.TryGetValue("GameBuildNumber", out var val) && val.IsString)
                {
                    return val.AsString;
                }
            }
            catch { }
            return string.Empty;
        }
    }

    public void SetIndexGameBuildNumber(string gameBuildNumber)
    {
        lock (_syncLock)
        {
            try
            {
                var metaCol = _database.GetCollection<BsonDocument>("_metadata");
                var doc = new BsonDocument
                {
                    ["_id"] = 1,
                    ["GameBuildNumber"] = gameBuildNumber
                };
                metaCol.Upsert(doc);
                _database.Checkpoint();
            }
            catch { }
        }
    }

    public bool IsIndexVersionMismatch()
    {
        string currentIndexVersion = GetIndexGameBuildNumber();
        return !string.Equals(currentIndexVersion, Realm.Shared.RealmVersion.GameBuildNumber, StringComparison.OrdinalIgnoreCase);
    }
    public bool HasIncorrectlyIndexedSample()
    {
        lock (_syncLock)
        {
            try
            {
                if (HasIncorrectlyIndexedManifestSample()) return true;
                if (HasIncorrectlyIndexedCategorySample()) return true;
            }
            catch (Exception ex)
            {
                GD.PrintErr($"[AssetIndexService] Error verifying asset index sample integrity: {ex.Message}");
                return true;
            }

            return false;
        }
    }

    private bool HasIncorrectlyIndexedManifestSample()
    {
        if (!Directory.Exists(MapAssetManager.GlobalArchiveDirectory)) return false;

        var manifestFiles = Directory.GetFiles(MapAssetManager.GlobalArchiveDirectory, "manifest.json", SearchOption.AllDirectories);
        if (manifestFiles.Length == 0) return false;

        string randomManifest = manifestFiles[Random.Shared.Next(manifestFiles.Length)];
        var manifest = MapManifest.LoadFromFile(randomManifest);
        if (manifest?.Files == null || manifest.Files.Count == 0) return false;

        var validEntries = manifest.Files
            .Where(kvp => !kvp.Key.EndsWith("manifest.json", StringComparison.OrdinalIgnoreCase) &&
                          !string.IsNullOrEmpty(kvp.Value) &&
                          Realm.Shared.Distribution.ContentAddressableStorage.NormalizeBlake3Hash(kvp.Value).Length >= 2)
            .ToList();

        if (validEntries.Count == 0) return false;

        var randomEntry = validEntries[Random.Shared.Next(validEntries.Count)];
        return IsManifestEntryIncorrect(randomManifest, randomEntry);
    }

    private bool IsManifestEntryIncorrect(string manifestPath, KeyValuePair<string, string> entry)
    {
        string normBlake = Realm.Shared.Distribution.ContentAddressableStorage.NormalizeBlake3Hash(entry.Value);
        string mapDir = Path.GetDirectoryName(manifestPath) ?? string.Empty;
        string mapRel = entry.Key.StartsWith("res://", StringComparison.OrdinalIgnoreCase)
            ? entry.Key.Substring(6)
            : entry.Key;
        mapRel = mapRel.TrimStart('/', '\\').Replace('/', Path.DirectorySeparatorChar);
        string mapFilePath = Path.Combine(mapDir, mapRel);

        if (!File.Exists(mapFilePath)) return true;

        string? casPath = MapAssetManager.Storage.FindAssetFilePath(normBlake);
        if (casPath == null || !File.Exists(casPath)) return true;

        string casSidecarPath = Path.Combine(MapAssetManager.Storage.SidecarCacheDirectory, normBlake.Substring(0, 2), $"{normBlake}.json");
        string mapSidecarPath = Path.Combine(mapDir, ".sidecarcache", normBlake.Substring(0, 2), $"{normBlake}.json");
        string mapFlatSidecarPath = Path.Combine(mapDir, ".sidecarcache", $"{normBlake}.json");

        return !File.Exists(casSidecarPath) && !File.Exists(mapSidecarPath) && !File.Exists(mapFlatSidecarPath);
    }

    private bool HasIncorrectlyIndexedCategorySample()
    {
        var categories = _assetCollection.Find(Query.Not("AssetType", BsonValue.Null))
            .Select(a => a.AssetType)
            .Where(t => !string.IsNullOrEmpty(t))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (categories.Count == 0) return false;

        foreach (var category in categories)
        {
            if (string.IsNullOrEmpty(category)) continue;

            var matchingAssets = _assetCollection.Find(Query.EQ("AssetType", category)).ToList();
            if (matchingAssets.Count == 0) continue;

            var sampledAsset = matchingAssets[Random.Shared.Next(matchingAssets.Count)];
            if (IsSampledAssetIncorrect(sampledAsset)) return true;
        }

        return false;
    }

    private bool IsSampledAssetIncorrect(IndexedAsset sampledAsset)
    {
        if (string.IsNullOrEmpty(sampledAsset.FilePath) || !File.Exists(sampledAsset.FilePath)) return true;

        var fileInfo = new FileInfo(sampledAsset.FilePath);
        if (fileInfo.Length != sampledAsset.FileSizeBytes) return true;

        string diskExtension = Path.GetExtension(sampledAsset.FilePath).ToLowerInvariant();
        if (!string.Equals(diskExtension, sampledAsset.Extension, StringComparison.OrdinalIgnoreCase)) return true;

        string? diskMetadataJson = RealmMetadataHelper.ExtractMetadata(sampledAsset.FilePath);
        bool diskHasMetadata = !string.IsNullOrEmpty(diskMetadataJson);

        if (diskHasMetadata != sampledAsset.HasRealmMetadata) return true;

        if (IsAssetTypeIncorrect(sampledAsset)) return true;
        if (IsBlake3Incorrect(sampledAsset, diskHasMetadata, diskMetadataJson)) return true;

        return false;
    }

    private bool IsAssetTypeIncorrect(IndexedAsset sampledAsset)
    {
        string? diskAssetType = RealmMetadataHelper.ExtractAssetType(sampledAsset.FilePath);
        if (!string.IsNullOrEmpty(diskAssetType))
        {
            return !string.Equals(diskAssetType, sampledAsset.AssetType, StringComparison.OrdinalIgnoreCase);
        }

        if (!RealmMetadataHelper.IsValidAssetTypeForExtension(sampledAsset.FilePath, sampledAsset.AssetType, out string canonicalType, out _))
        {
            return true;
        }

        return !string.Equals(canonicalType, sampledAsset.AssetType, StringComparison.OrdinalIgnoreCase);
    }

    private bool IsBlake3Incorrect(IndexedAsset sampledAsset, bool diskHasMetadata, string? diskMetadataJson)
    {
        if (string.IsNullOrEmpty(sampledAsset.Blake3) || !diskHasMetadata || diskMetadataJson == null) return false;

        try
        {
            var node = JsonNode.Parse(diskMetadataJson);
            if (node is JsonObject obj && obj.ContainsKey("blake3"))
            {
                string? metaBlake3 = obj["blake3"]?.ToString();
                if (!string.IsNullOrEmpty(metaBlake3) && !string.Equals(metaBlake3, sampledAsset.Blake3, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }
        }
        catch
        {
            return true;
        }

        return false;
    }


    public void SynchronizeCasAndMapPackages(IProgress<AssetIndexProgressUpdate>? progress = null)
    {
        SynchronizeArchiveDirectory(MapAssetManager.GlobalArchiveDirectory, MapAssetManager.Storage, isP2P: false, progress, 0.02, 0.08);
        if (Directory.Exists(MapAssetManager.P2PArchiveDirectory))
        {
            SynchronizeArchiveDirectory(MapAssetManager.P2PArchiveDirectory, MapAssetManager.P2PStorage, isP2P: true, progress, 0.08, 0.12);
        }
    }
    private void SynchronizeArchiveDirectory(string archiveDirectory, Realm.Shared.Distribution.ContentAddressableStorage cas, bool isP2P, IProgress<AssetIndexProgressUpdate>? progress, double startPercentage, double endPercentage)
    {
        if (string.IsNullOrEmpty(archiveDirectory) || !Directory.Exists(archiveDirectory) || cas == null) return;

        var manifestFiles = Directory.GetFiles(archiveDirectory, "manifest.json", SearchOption.AllDirectories);
        for (int i = 0; i < manifestFiles.Length; i++)
        {
            ProcessSynchronizeManifest(manifestFiles[i], cas, progress, startPercentage, endPercentage, i, manifestFiles.Length);
        }
    }

    private void ProcessSynchronizeManifest(string manifestFile, Realm.Shared.Distribution.ContentAddressableStorage cas, IProgress<AssetIndexProgressUpdate>? progress, double startPercentage, double endPercentage, int index, int totalFiles)
    {
        try
        {
            string mapDir = Path.GetDirectoryName(manifestFile) ?? string.Empty;
            var manifest = MapManifest.LoadFromFile(manifestFile);
            if (manifest?.Files == null || manifest.Files.Count == 0) return;

            string mapName = !string.IsNullOrWhiteSpace(manifest.MapName) ? manifest.MapName : Path.GetFileName(mapDir);
            double currentPercentage = startPercentage + (endPercentage - startPercentage) * ((double)index / Math.Max(1, totalFiles));
            progress?.Report(new AssetIndexProgressUpdate(currentPercentage, string.Format(TranslationServer.Translate("Synchronizing files for {0}..."), mapName)));

            foreach (var kvp in manifest.Files)
            {
                ProcessSynchronizeManifestEntry(kvp.Key, kvp.Value, mapDir, cas);
            }
        }
        catch (Exception ex)
        {
            GD.PrintErr($"[AssetIndexService] SynchronizeArchiveDirectory error on {manifestFile}: {ex.Message}");
        }
    }

    private void ProcessSynchronizeManifestEntry(string virtualPath, string hash, string mapDir, Realm.Shared.Distribution.ContentAddressableStorage cas)
    {
        string blake3 = Realm.Shared.Distribution.ContentAddressableStorage.NormalizeBlake3Hash(hash);
        if (string.IsNullOrEmpty(blake3) || blake3.Length < 2) return;
        if (virtualPath.EndsWith("manifest.json", StringComparison.OrdinalIgnoreCase)) return;

        string mapRelativePath = virtualPath.StartsWith("res://", StringComparison.OrdinalIgnoreCase) ? virtualPath.Substring(6) : virtualPath;
        mapRelativePath = mapRelativePath.TrimStart('/', '\\').Replace('/', Path.DirectorySeparatorChar);
        string mapFilePath = Path.Combine(mapDir, mapRelativePath);

        string? casFilePath = cas.FindAssetFilePath(blake3);
        casFilePath = EnsureCasFileExists(casFilePath, blake3, virtualPath, mapDir, mapFilePath, cas);

        if (casFilePath != null && !File.Exists(mapFilePath))
        {
            Realm.Shared.Distribution.HardLinkHelper.CreateHardLinkOrCopy(mapFilePath, casFilePath);
        }

        EnsureSidecarCacheExists(blake3, mapDir, casFilePath, mapFilePath, cas);
    }

    private string? EnsureCasFileExists(string? casFilePath, string blake3, string virtualPath, string mapDir, string mapFilePath, Realm.Shared.Distribution.ContentAddressableStorage cas)
    {
        if (casFilePath != null && File.Exists(casFilePath)) return casFilePath;

        string? sourceFile = FindSourceFileForCas(mapDir, virtualPath, mapFilePath);
        if (sourceFile == null || !File.Exists(sourceFile)) return null;

        string extension = Path.GetExtension(virtualPath);
        if (string.IsNullOrEmpty(extension)) extension = Path.GetExtension(sourceFile);

        string shard = blake3.Substring(0, 2);
        string shardDirectory = Path.Combine(cas.AssetsDirectory, shard);
        if (!Directory.Exists(shardDirectory)) Directory.CreateDirectory(shardDirectory);

        string targetCasPath = Path.Combine(shardDirectory, $"{blake3}{extension.ToLowerInvariant()}");
        Realm.Shared.Distribution.HardLinkHelper.CreateHardLinkOrCopy(targetCasPath, sourceFile);

        if (!File.Exists(mapFilePath))
        {
            Realm.Shared.Distribution.HardLinkHelper.CreateHardLinkOrCopy(mapFilePath, targetCasPath);
        }

        return targetCasPath;
    }

    private string? FindSourceFileForCas(string mapDir, string virtualPath, string mapFilePath)
    {
        if (File.Exists(mapFilePath)) return mapFilePath;

        string? fallback = MapAssetHelper.FindModelOnDisk(mapDir, null, Path.GetFileName(virtualPath));
        if (!string.IsNullOrEmpty(fallback) && File.Exists(fallback)) return fallback;

        fallback = MapAssetHelper.FindAssetOnDisk(mapDir, "other", virtualPath);
        if (!string.IsNullOrEmpty(fallback) && File.Exists(fallback)) return fallback;

        string candidate = Path.Combine(mapDir, Path.GetFileName(virtualPath));
        if (File.Exists(candidate)) return candidate;

        return null;
    }

    private void EnsureSidecarCacheExists(string blake3, string mapDir, string? casFilePath, string mapFilePath, Realm.Shared.Distribution.ContentAddressableStorage cas)
    {
        string shardName = blake3.Substring(0, 2);
        string casSidecarPath = Path.Combine(cas.SidecarCacheDirectory, shardName, $"{blake3}.json");
        string mapSidecarPath = Path.Combine(mapDir, ".sidecarcache", shardName, $"{blake3}.json");
        string mapFlatSidecarPath = Path.Combine(mapDir, ".sidecarcache", $"{blake3}.json");

        if (!File.Exists(casSidecarPath))
        {
            PopulateCasSidecar(blake3, casSidecarPath, mapSidecarPath, mapFlatSidecarPath, casFilePath, mapFilePath, cas);
        }

        if (File.Exists(casSidecarPath) && !File.Exists(mapSidecarPath))
        {
            Realm.Shared.Distribution.HardLinkHelper.CreateHardLinkOrCopy(mapSidecarPath, casSidecarPath);
        }
    }

    private static void PopulateCasSidecar(string blake3, string casSidecarPath, string mapSidecarPath, string mapFlatSidecarPath, string? casFilePath, string mapFilePath, Realm.Shared.Distribution.ContentAddressableStorage cas)
    {
        if (TryCopyExistingSidecar(casSidecarPath, mapSidecarPath)) return;
        if (TryCopyExistingSidecar(casSidecarPath, mapFlatSidecarPath)) return;

        ExtractAndSaveCasSidecar(blake3, casFilePath, mapFilePath, cas);
    }

    private static bool TryCopyExistingSidecar(string destPath, string srcPath)
    {
        if (!File.Exists(srcPath)) return false;
        Realm.Shared.Distribution.HardLinkHelper.CreateHardLinkOrCopy(destPath, srcPath);
        return true;
    }

    private static void ExtractAndSaveCasSidecar(string blake3, string? casFilePath, string mapFilePath, Realm.Shared.Distribution.ContentAddressableStorage cas)
    {
        string? assetForMetadata = casFilePath ?? (File.Exists(mapFilePath) ? mapFilePath : null);
        if (assetForMetadata == null || !File.Exists(assetForMetadata)) return;

        string? extractedMetadata = Realm.Shared.Metadata.RealmMetadataHelper.ExtractMetadata(assetForMetadata);
        if (!string.IsNullOrWhiteSpace(extractedMetadata))
        {
            cas.UpdateSidecarCache(blake3, extractedMetadata);
        }
    }


    public void RebuildIndexFromCas(IProgress<AssetIndexProgressUpdate>? progress = null)
    {
        lock (_syncLock)
        {
            progress?.Report(new AssetIndexProgressUpdate(0.02, TranslationServer.Translate("Synchronizing CAS and map packages...")));
            SynchronizeCasAndMapPackages(progress);

            progress?.Report(new AssetIndexProgressUpdate(0.12, TranslationServer.Translate("Resetting asset database cache...")));

            try
            {
                _database.Dispose();
            }
            catch { }

            ClearP2PIndex();

            string cacheFilePath = GetCacheFilePath();
            try
            {
                if (File.Exists(cacheFilePath))
                {
                    File.Delete(cacheFilePath);
                }
                string logFile = cacheFilePath + "-log";
                if (File.Exists(logFile))
                {
                    File.Delete(logFile);
                }
                string tempFile = cacheFilePath + "-temp";
                if (File.Exists(tempFile))
                {
                    File.Delete(tempFile);
                }
            }
            catch (Exception ex)
            {
                GD.PrintErr($"[AssetIndexService] Failed to delete cache files during rebuild: {ex.Message}");
            }

            progress?.Report(new AssetIndexProgressUpdate(0.18, TranslationServer.Translate("Initializing database schema and indexes...")));

            var connectionString = new ConnectionString
            {
                Filename = cacheFilePath,
                Connection = ConnectionType.Shared
            };

            _database = new LiteDatabase(connectionString);
            _assetCollection = _database.GetCollection<IndexedAsset>("assets");
            _folderCollection = _database.GetCollection<IndexedFolder>("folders");
            _mapPackageCollection = _database.GetCollection<IndexedMapPackage>("map_packages");

            EnsureIndexes();
            SetIndexGameBuildNumber(Realm.Shared.RealmVersion.GameBuildNumber);

            InitializeDefaultDirectories();
            ScanAllCasManifests(progress);

            progress?.Report(new AssetIndexProgressUpdate(1.0, TranslationServer.Translate("Asset index repair complete!")));
        }
    }

    public AssetIndexService()
    {
        string cacheFilePath = GetCacheFilePath();
        var connectionString = new ConnectionString
        {
            Filename = cacheFilePath,
            Connection = ConnectionType.Shared
        };

        _database = new LiteDatabase(connectionString);
        _assetCollection = _database.GetCollection<IndexedAsset>("assets");
        _folderCollection = _database.GetCollection<IndexedFolder>("folders");
        _mapPackageCollection = _database.GetCollection<IndexedMapPackage>("map_packages");

        EnsureIndexes();

        if (!IsIndexVersionMismatch())
        {
            InitializeDefaultDirectories();
        }
    }

    private LiteDatabase GetP2PDatabase()
    {
        lock (_p2pSyncLock)
        {
            if (_p2pDatabase != null) return _p2pDatabase;
            string p2pDir = MapAssetManager.P2PArchiveDirectory;
            if (!Directory.Exists(p2pDir)) Directory.CreateDirectory(p2pDir);
            string cacheFilePath = Path.Combine(p2pDir, "p2p_asset_index.cache");
            var connectionString = new ConnectionString
            {
                Filename = cacheFilePath,
                Connection = ConnectionType.Shared
            };
            _p2pDatabase = new LiteDatabase(connectionString);
            var p2pAssetCol = _p2pDatabase.GetCollection<IndexedAsset>("assets");
            var p2pMapCol = _p2pDatabase.GetCollection<IndexedMapPackage>("map_packages");
            p2pAssetCol.EnsureIndex(x => x.FilePath, true);
            p2pAssetCol.EnsureIndex(x => x.MapName);
            p2pAssetCol.EnsureIndex(x => x.MapVersion);
            p2pAssetCol.EnsureIndex(x => x.HasPlayerColorMask);
            p2pMapCol.EnsureIndex(x => x.MapName);
            p2pMapCol.EnsureIndex(x => x.MapVersion);
            return _p2pDatabase;
        }
    }

    public void ClearP2PIndex()
    {
        lock (_p2pSyncLock)
        {
            if (_p2pDatabase != null)
            {
                _p2pDatabase.Dispose();
                _p2pDatabase = null;
            }
        }
    }

    public static string GlobalCasAssetsDirectory => NormalizePath(MapAssetManager.Storage.AssetsDirectory);
    private void InitializeDefaultDirectories()
    {
        lock (_syncLock)
        {
            string legacyArchive = NormalizePath(Path.Combine(MapAssetManager.GlobalArchiveDirectory, "global_assets.rmap"));
            string casAssetsDirectory = GlobalCasAssetsDirectory;
            string globalArchive = NormalizePath(MapAssetManager.GlobalArchiveDirectory);
            string p2pArchive = NormalizePath(MapAssetManager.P2PArchiveDirectory);

            RemoveForbiddenFolders(legacyArchive, casAssetsDirectory);

            if (!Directory.Exists(casAssetsDirectory))
            {
                Directory.CreateDirectory(casAssetsDirectory);
            }

            RemoveOrphanedAssets(casAssetsDirectory, globalArchive, p2pArchive);
            RemoveOrphanedPackages();

            _database.Checkpoint();
        }
    }

    private void RemoveForbiddenFolders(string legacyArchive, string casAssetsDirectory)
    {
        var forbiddenFolders = _folderCollection.FindAll()
            .Where(f => IsForbiddenPath(f.DirectoryPath) ||
                        string.Equals(f.DirectoryPath, legacyArchive, StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(f.DirectoryPath, casAssetsDirectory, StringComparison.OrdinalIgnoreCase) ||
                        f.DirectoryPath.EndsWith(".rmap", StringComparison.OrdinalIgnoreCase) ||
                        f.DirectoryPath.EndsWith(".7z", StringComparison.OrdinalIgnoreCase))
            .Select(f => (BsonValue)f.Id)
            .ToArray();

        if (forbiddenFolders.Length > 0)
        {
            _folderCollection.DeleteMany(Query.In("_id", forbiddenFolders));
        }
    }

    private void RemoveOrphanedAssets(string casAssetsDirectory, string globalArchive, string p2pArchive)
    {
        var validFolders = _folderCollection.FindAll()
            .Select(f => f.DirectoryPath)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var orphanedAssets = _assetCollection.FindAll()
            .Where(a => IsAssetOrphaned(a, validFolders, casAssetsDirectory, globalArchive, p2pArchive))
            .Select(a => (BsonValue)a.Id)
            .ToArray();

        if (orphanedAssets.Length > 0)
        {
            _assetCollection.DeleteMany(Query.In("_id", orphanedAssets));
        }
    }

    private bool IsAssetOrphaned(IndexedAsset asset, HashSet<string> validFolders, string casAssetsDirectory, string globalArchive, string p2pArchive)
    {
        if (IsForbiddenAsset(asset)) return true;
        if (IsAssetInValidLocation(asset, validFolders, casAssetsDirectory, globalArchive, p2pArchive)) return false;

        return true;
    }

    private bool IsForbiddenAsset(IndexedAsset asset)
    {
        if (IsForbiddenPath(asset.DirectoryPath)) return true;
        if (IsForbiddenPath(asset.FilePath)) return true;
        if (asset.DirectoryPath.EndsWith(".rmap", StringComparison.OrdinalIgnoreCase)) return true;
        if (asset.DirectoryPath.EndsWith(".7z", StringComparison.OrdinalIgnoreCase)) return true;
        if (asset.FilePath.Contains("/extracted/", StringComparison.OrdinalIgnoreCase)) return true;
        if (asset.FilePath.Contains("\\extracted\\", StringComparison.OrdinalIgnoreCase)) return true;

        return false;
    }

    private bool IsAssetInValidLocation(IndexedAsset asset, HashSet<string> validFolders, string casAssetsDirectory, string globalArchive, string p2pArchive)
    {
        if (validFolders.Contains(asset.DirectoryPath)) return true;
        if (string.Equals(asset.DirectoryPath, casAssetsDirectory, StringComparison.OrdinalIgnoreCase)) return true;
        if (asset.FilePath.StartsWith(globalArchive, StringComparison.OrdinalIgnoreCase)) return true;
        if (asset.FilePath.StartsWith(p2pArchive, StringComparison.OrdinalIgnoreCase)) return true;
        if (!string.IsNullOrEmpty(asset.MapName)) return true;

        return false;
    }

    private void RemoveOrphanedPackages()
    {
        var orphanedPackages = _mapPackageCollection.FindAll()
            .Where(p => string.IsNullOrWhiteSpace(p.ManifestPath) || !File.Exists(p.ManifestPath))
            .Select(p => (BsonValue)p.Id)
            .ToArray();

        if (orphanedPackages.Length > 0)
        {
            _mapPackageCollection.DeleteMany(Query.In("_id", orphanedPackages));
        }
    }
    public void ScanAllCasManifests(IProgress<AssetIndexProgressUpdate>? progress = null)
    {
        var referencedCasPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        ScanGlobalManifests(referencedCasPaths, progress);
        CleanupOrphanedCasData(referencedCasPaths);
        ScanP2PManifests(progress);
    }

    private void ScanGlobalManifests(HashSet<string> referencedCasPaths, IProgress<AssetIndexProgressUpdate>? progress)
    {
        if (!Directory.Exists(MapAssetManager.GlobalArchiveDirectory)) return;

        var manifestFiles = Directory.GetFiles(MapAssetManager.GlobalArchiveDirectory, "manifest.json", SearchOption.AllDirectories);
        for (int i = 0; i < manifestFiles.Length; i++)
        {
            ProcessGlobalManifestFile(manifestFiles[i], i, manifestFiles.Length, referencedCasPaths, progress);
        }
    }

    private void ProcessGlobalManifestFile(string file, int index, int total, HashSet<string> referencedCasPaths, IProgress<AssetIndexProgressUpdate>? progress)
    {
        try
        {
            var manifest = MapManifest.LoadFromFile(file);
            if (manifest?.Files == null || manifest.Files.Count == 0) return;

            string mapName = !string.IsNullOrWhiteSpace(manifest.MapName) ? manifest.MapName : Path.GetFileName(Path.GetDirectoryName(file)) ?? "Map";
            double pct = 0.20 + 0.65 * ((double)index / Math.Max(1, total));
            progress?.Report(new AssetIndexProgressUpdate(pct, string.Format(TranslationServer.Translate("Indexing manifest {0}/{1}: {2}..."), index + 1, total, mapName)));

            var paths = RegisterManifestInternal(manifest, file, isP2P: false);
            foreach (var p in paths)
            {
                referencedCasPaths.Add(p);
            }
        }
        catch { }
    }

    private void CleanupOrphanedCasData(HashSet<string> referencedCasPaths)
    {
        lock (_syncLock)
        {
            var orphanedCasAssets = _assetCollection.Find(Query.EQ("DirectoryPath", GlobalCasAssetsDirectory))
                .Where(a => !referencedCasPaths.Contains(a.FilePath) || !File.Exists(a.FilePath))
                .Select(a => (BsonValue)a.Id)
                .ToArray();

            if (orphanedCasAssets.Length > 0)
            {
                _assetCollection.DeleteMany(Query.In("_id", orphanedCasAssets));
            }

            var orphanedPackages = _mapPackageCollection.FindAll()
                .Where(p => !p.IsP2P && (string.IsNullOrWhiteSpace(p.ManifestPath) || !File.Exists(p.ManifestPath)))
                .Select(p => (BsonValue)p.Id)
                .ToArray();

            if (orphanedPackages.Length > 0)
            {
                _mapPackageCollection.DeleteMany(Query.In("_id", orphanedPackages));
            }

            _database.Checkpoint();
        }
    }

    private void ScanP2PManifests(IProgress<AssetIndexProgressUpdate>? progress)
    {
        if (!Directory.Exists(MapAssetManager.P2PArchiveDirectory)) return;

        var p2pManifestFiles = Directory.GetFiles(MapAssetManager.P2PArchiveDirectory, "manifest.json", SearchOption.AllDirectories);
        for (int j = 0; j < p2pManifestFiles.Length; j++)
        {
            ProcessP2PManifestFile(p2pManifestFiles[j], j, p2pManifestFiles.Length, progress);
        }
    }

    private void ProcessP2PManifestFile(string file, int index, int total, IProgress<AssetIndexProgressUpdate>? progress)
    {
        try
        {
            var manifest = MapManifest.LoadFromFile(file);
            if (manifest?.Files == null || manifest.Files.Count == 0) return;

            double pct = 0.85 + 0.12 * ((double)index / Math.Max(1, total));
            progress?.Report(new AssetIndexProgressUpdate(pct, string.Format(TranslationServer.Translate("Indexing P2P archive {0}/{1}..."), index + 1, total)));
            RegisterManifest(manifest, file, isP2P: true);
        }
        catch { }
    }


    public void RegisterManifest(Realm.Shared.Distribution.MapManifest manifest, string manifestPath, bool isP2P = false)
    {
        if (manifest == null) return;
        lock (isP2P ? _p2pSyncLock : _syncLock)
        {
            RegisterManifestInternal(manifest, manifestPath, isP2P);
        }
    }
    private List<string> RegisterManifestInternal(Realm.Shared.Distribution.MapManifest manifest, string manifestPath, bool isP2P)
    {
        var indexedPaths = new List<string>();
        if (manifest == null) return indexedPaths;

        string mapName = !string.IsNullOrWhiteSpace(manifest.MapName) ? manifest.MapName.Trim() : Path.GetFileNameWithoutExtension(manifestPath).Replace("_manifest", "");
        string mapVersion = !string.IsNullOrWhiteSpace(manifest.Version) ? manifest.Version.Trim() : "1.0.0";

        var database = isP2P ? GetP2PDatabase() : _database;
        var mapPackageCollection = isP2P ? database.GetCollection<IndexedMapPackage>("map_packages") : _mapPackageCollection;
        var assetCollection = isP2P ? database.GetCollection<IndexedAsset>("assets") : _assetCollection;
        var storage = isP2P ? MapAssetManager.P2PStorage : MapAssetManager.Storage;
        string storageDirectory = isP2P ? MapAssetManager.P2PArchiveDirectory : GlobalCasAssetsDirectory;

        UpsertManifestPackage(manifest, manifestPath, isP2P, mapName, mapVersion, mapPackageCollection);

        if (manifest.Files != null)
        {
            ProcessManifestFiles(manifest, mapName, mapVersion, storage, storageDirectory, assetCollection, indexedPaths);
        }

        database.Checkpoint();
        return indexedPaths;
    }

    private void UpsertManifestPackage(Realm.Shared.Distribution.MapManifest manifest, string manifestPath, bool isP2P, string mapName, string mapVersion, ILiteCollection<IndexedMapPackage> mapPackageCollection)
    {
        var pkg = mapPackageCollection.FindOne(p => p.MapName == mapName && p.MapVersion == mapVersion);
        pkg ??= new IndexedMapPackage();
        pkg.MapName = mapName;
        pkg.MapVersion = mapVersion;
        pkg.ManifestPath = manifestPath;
        pkg.DownloadedUtc = DateTime.UtcNow;
        pkg.IsP2P = isP2P;
        pkg.AssetHashes = manifest.Files != null ? manifest.Files.Values.Select(v => Realm.Shared.Distribution.ContentAddressableStorage.NormalizeBlake3Hash(v)).ToList() : new List<string>();
        mapPackageCollection.Upsert(pkg);
    }

    private void ProcessManifestFiles(Realm.Shared.Distribution.MapManifest manifest, string mapName, string mapVersion, Realm.Shared.Distribution.ContentAddressableStorage storage, string storageDirectory, ILiteCollection<IndexedAsset> assetCollection, List<string> indexedPaths)
    {
        var existingAssetMap = assetCollection.Find(Query.EQ("DirectoryPath", storageDirectory)).ToDictionary(x => x.FilePath, StringComparer.OrdinalIgnoreCase);
        var processedPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var assetsToUpdate = new List<IndexedAsset>();
        var assetsToInsert = new List<IndexedAsset>();

        foreach (var kvp in manifest.Files)
        {
            ProcessSingleManifestFile(manifest, kvp.Key, kvp.Value, mapName, mapVersion, storage, storageDirectory, existingAssetMap, processedPaths, assetsToUpdate, assetsToInsert, indexedPaths);
        }

        if (assetsToUpdate.Count > 0) assetCollection.Update(assetsToUpdate);
        if (assetsToInsert.Count > 0) assetCollection.Insert(assetsToInsert);
    }

    private void ProcessSingleManifestFile(Realm.Shared.Distribution.MapManifest manifest, string virtualPath, string hash, string mapName, string mapVersion, Realm.Shared.Distribution.ContentAddressableStorage storage, string storageDirectory, Dictionary<string, IndexedAsset> existingAssetMap, HashSet<string> processedPaths, List<IndexedAsset> assetsToUpdate, List<IndexedAsset> assetsToInsert, List<string> indexedPaths)
    {
        string norm = Realm.Shared.Distribution.ContentAddressableStorage.NormalizeBlake3Hash(hash);
        string? resolvedPath = storage.FindAssetFilePath(norm);
        if (resolvedPath == null || !File.Exists(resolvedPath)) return;

        string normPath = NormalizePath(resolvedPath);
        indexedPaths.Add(normPath);

        if (!processedPaths.Add(normPath)) return;

        if (existingAssetMap.TryGetValue(normPath, out var existingAsset))
        {
            UpdateExistingManifestAsset(existingAsset, virtualPath, norm, normPath, mapName, mapVersion, storage, storageDirectory, assetsToUpdate);
        }
        else
        {
            InsertNewManifestAsset(manifest, virtualPath, norm, normPath, mapName, mapVersion, storage, storageDirectory, existingAssetMap, assetsToInsert);
        }
    }

    private void UpdateExistingManifestAsset(IndexedAsset existingAsset, string virtualPath, string norm, string normPath, string mapName, string mapVersion, Realm.Shared.Distribution.ContentAddressableStorage storage, string storageDirectory, List<IndexedAsset> assetsToUpdate)
    {
        bool needsUpdate = false;
        string? existingMetaJson = storage.GetAssetMetadata(norm) ?? Realm.Shared.Metadata.RealmMetadataHelper.ExtractMetadata(normPath);
        var parsed = ParseMetadataHeaders(existingMetaJson, virtualPath);

        needsUpdate |= TryUpdateAssetType(existingAsset, virtualPath, parsed.AssetType, existingMetaJson);
        needsUpdate |= TryUpdateAssetProperties(existingAsset, parsed.SupportsTeamColor, parsed.ChromaKey, existingMetaJson, normPath);
        needsUpdate |= TryUpdateAssetMapInfo(existingAsset, mapName, mapVersion, storageDirectory);

        if (needsUpdate) assetsToUpdate.Add(existingAsset);

        EnqueueThumbnailRequestIfNeeded(existingAsset.Extension, normPath, norm);
    }

    private bool TryUpdateAssetType(IndexedAsset existingAsset, string virtualPath, string? parsedType, string? existingMetaJson)
    {
        string? existingAssetType = ResolveAssetTypeFromParsed(parsedType, virtualPath);
        if (string.IsNullOrEmpty(existingAssetType) || existingAsset.AssetType == existingAssetType) return false;

        existingAsset.AssetType = existingAssetType;
        existingAsset.HasRealmMetadata = !string.IsNullOrEmpty(existingMetaJson) || existingAsset.HasRealmMetadata || !string.IsNullOrEmpty(existingAssetType);
        return true;
    }

    private bool TryUpdateAssetProperties(IndexedAsset existingAsset, bool? parsedSupportsTeamColor, string? parsedChromaKey, string? existingMetaJson, string normPath)
    {
        bool existingHasMask = parsedSupportsTeamColor ?? DetermineHasPlayerColorMask(existingMetaJson, normPath);
        string resolvedChroma = parsedChromaKey ?? string.Empty;

        if (existingAsset.HasPlayerColorMask == existingHasMask && existingAsset.ChromaKey == resolvedChroma) return false;

        existingAsset.HasPlayerColorMask = existingHasMask;
        existingAsset.ChromaKey = resolvedChroma;
        return true;
    }

    private bool TryUpdateAssetMapInfo(IndexedAsset existingAsset, string mapName, string mapVersion, string storageDirectory)
    {
        bool updated = false;

        if (string.IsNullOrEmpty(existingAsset.MapName) || existingAsset.MapName != mapName)
        {
            existingAsset.MapName = mapName;
            updated = true;
        }

        if (string.IsNullOrEmpty(existingAsset.MapVersion) || existingAsset.MapVersion != mapVersion)
        {
            existingAsset.MapVersion = mapVersion;
            updated = true;
        }

        if (existingAsset.DirectoryPath != storageDirectory)
        {
            existingAsset.DirectoryPath = storageDirectory;
            updated = true;
        }

        return updated;
    }

    private void EnqueueThumbnailRequestIfNeeded(string extension, string normPath, string norm)
    {
        if (extension == ".rmesh" && !GlbThumbnailRenderer.HasDiskCache(normPath, norm))
        {
            GlbThumbnailRenderer.EnqueueRequest(normPath, new FileInfo(normPath).LastWriteTimeUtc, norm, isHighPriority: false);
        }
    }

    private string? ResolveAssetTypeFromParsed(string? parsedType, string virtualPath)
    {
        if (!string.IsNullOrEmpty(parsedType)) return parsedType;

        string? dir = Path.GetDirectoryName(virtualPath);
        if (string.IsNullOrEmpty(dir)) return null;

        string[] dirParts = dir.Split(new[] { '/', '\\' }, StringSplitOptions.RemoveEmptyEntries);
        foreach (var part in dirParts.Reverse())
        {
            if (Realm.Shared.Metadata.RealmMetadataHelper.IsValidAssetTypeForExtension(virtualPath, part, out string canonical, out _))
            {
                return canonical;
            }
        }
        return null;
    }

    private void InsertNewManifestAsset(Realm.Shared.Distribution.MapManifest manifest, string virtualPath, string norm, string normPath, string mapName, string mapVersion, Realm.Shared.Distribution.ContentAddressableStorage storage, string storageDirectory, Dictionary<string, IndexedAsset> existingAssetMap, List<IndexedAsset> assetsToInsert)
    {
        var fi = new FileInfo(normPath);
        var asset = CreateBaseManifestAsset(virtualPath, norm, normPath, mapName, mapVersion, storageDirectory, fi);

        string? metaJson = storage.GetAssetMetadata(norm) ?? Realm.Shared.Metadata.RealmMetadataHelper.ExtractMetadata(normPath);
        var parsedMeta = ParseMetadataHeaders(metaJson, virtualPath);
        var tags = BuildManifestAssetTags(manifest, virtualPath, normPath, storageDirectory, asset.FileName, parsedMeta.Tags);

        string? assetType = ResolveFallbackAssetType(parsedMeta.AssetType, virtualPath, tags);

        asset.Tags = tags;
        asset.MetadataJson = JsonSerializer.Serialize(new AssetMetadataModel { Tags = tags });
        asset.HasPlayerColorMask = parsedMeta.SupportsTeamColor ?? DetermineHasPlayerColorMask(metaJson, normPath);
        asset.ChromaKey = parsedMeta.ChromaKey ?? string.Empty;
        asset.AssetType = assetType;
        asset.HasRealmMetadata = !string.IsNullOrEmpty(metaJson) || !string.IsNullOrEmpty(assetType);

        assetsToInsert.Add(asset);
        existingAssetMap[normPath] = asset;

        EnqueueThumbnailRequestIfNeeded(asset.Extension, normPath, norm);
    }

    private IndexedAsset CreateBaseManifestAsset(string virtualPath, string norm, string normPath, string mapName, string mapVersion, string storageDirectory, FileInfo fi)
    {
        return new IndexedAsset
        {
            FilePath = normPath,
            FileName = Path.GetFileName(virtualPath),
            Extension = !string.IsNullOrEmpty(Path.GetExtension(virtualPath)) ? Path.GetExtension(virtualPath).ToLowerInvariant() : Path.GetExtension(normPath).ToLowerInvariant(),
            DirectoryPath = storageDirectory,
            FileSizeBytes = fi.Length,
            LastModifiedUtc = fi.LastWriteTimeUtc,
            MapName = mapName,
            MapVersion = mapVersion,
            Blake3 = norm
        };
    }

    private string? ResolveFallbackAssetType(string? parsedType, string virtualPath, List<string> tags)
    {
        string? assetType = ResolveAssetTypeFromParsed(parsedType, virtualPath);
        if (!string.IsNullOrEmpty(assetType)) return assetType;

        foreach (var tag in tags)
        {
            if (Realm.Shared.Metadata.RealmMetadataHelper.IsValidAssetTypeForExtension(virtualPath, tag, out string canonical, out _))
            {
                return canonical;
            }
        }
        return null;
    }

    private List<string> BuildManifestAssetTags(Realm.Shared.Distribution.MapManifest manifest, string virtualPath, string normPath, string storageDirectory, string fileName, List<string> parsedTags)
    {
        var tags = parsedTags;
        if (tags.Count == 0) tags = LoadTagsForFile(normPath, storageDirectory, tags);

        string preferredNameNoExt = Path.GetFileNameWithoutExtension(fileName);
        var nameTokens = preferredNameNoExt.Split(new[] { '_', '-', ' ', '.', '@' }, StringSplitOptions.RemoveEmptyEntries);
        foreach (var token in nameTokens)
        {
            if (token.Length > 1 && !char.IsDigit(token[0]) && !tags.Contains(token, StringComparer.OrdinalIgnoreCase))
            {
                tags.Add(token.ToLowerInvariant());
            }
        }

        if (manifest.Tags != null)
        {
            foreach (var tag in manifest.Tags)
            {
                if (!string.IsNullOrWhiteSpace(tag) && !tags.Contains(tag.Trim(), StringComparer.OrdinalIgnoreCase))
                {
                    tags.Add(tag.Trim().ToLowerInvariant());
                }
            }
        }
        return tags;
    }


    public IReadOnlyList<IndexedMapPackage> GetDownloadedMapPackages()
    {
        lock (_syncLock)
        {
            return _mapPackageCollection.FindAll()
                .Where(p => !p.IsP2P && !string.IsNullOrWhiteSpace(p.ManifestPath) && File.Exists(p.ManifestPath))
                .OrderBy(p => p.MapName, StringComparer.OrdinalIgnoreCase)
                .ThenByDescending(p => p.MapVersion, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }
    }

    public string? GetLatestVersionForMap(string mapName)
    {
        if (string.IsNullOrWhiteSpace(mapName)) return null;
        lock (_syncLock)
        {
            var packages = _mapPackageCollection.Find(p => p.MapName == mapName && !p.IsP2P).ToList();
            if (packages.Count == 0) return null;
            packages.Sort((a, b) => CompareVersions(b.MapVersion, a.MapVersion));
            return packages[0].MapVersion;
        }
    }

    public static int CompareVersions(string v1, string v2)
    {
        if (System.Version.TryParse(v1, out var ver1) && System.Version.TryParse(v2, out var ver2))
        {
            return ver1.CompareTo(ver2);
        }
        return string.Compare(v1, v2, StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsForbiddenPath(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) return true;
        string norm = path.Replace('\\', '/').ToLowerInvariant();
        return norm.Contains(MapWorkspaceService.DefaultWorkspaceFolder) || norm.Contains("maptemplate");
    }

    public bool IsDirectoryIndexing(string directoryPath)
    {
        if (string.IsNullOrWhiteSpace(directoryPath)) return false;
        lock (_syncLock)
        {
            return _indexingDirectories.Contains(NormalizePath(directoryPath));
        }
    }

    public IReadOnlyList<string> GetIndexedDirectories()
    {
        lock (_syncLock)
        {
            return _folderCollection.FindAll()
                .Select(f => f.DirectoryPath)
                .OrderBy(p => p, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }
    }

    public int GetAssetCount()
    {
        lock (_syncLock)
        {
            return _assetCollection.Count();
        }
    }
    public void AddDirectory(string directoryPath)
    {
        if (string.IsNullOrWhiteSpace(directoryPath)) return;

        string normalizedPath = NormalizePath(directoryPath);
        if (!IsValidDirectoryForIndexing(normalizedPath)) return;

        if (!TryRegisterDirectory(normalizedPath)) return;

        DirectoryIndexingStateChanged?.Invoke(normalizedPath, true);
        StartDirectoryScanTask(normalizedPath);
    }

    private bool IsValidDirectoryForIndexing(string normalizedPath)
    {
        if (IsForbiddenPath(normalizedPath) || !Directory.Exists(normalizedPath)) return false;

        if (normalizedPath.EndsWith(".rmap", StringComparison.OrdinalIgnoreCase) ||
            normalizedPath.EndsWith(".7z", StringComparison.OrdinalIgnoreCase) ||
            normalizedPath.EndsWith(".zip", StringComparison.OrdinalIgnoreCase) ||
            normalizedPath.EndsWith(".rar", StringComparison.OrdinalIgnoreCase) ||
            normalizedPath.EndsWith(".tar", StringComparison.OrdinalIgnoreCase) ||
            normalizedPath.EndsWith(".gz", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return true;
    }

    private bool TryRegisterDirectory(string normalizedPath)
    {
        lock (_syncLock)
        {
            var existingFolder = _folderCollection.FindOne(x => x.DirectoryPath == normalizedPath);
            if (existingFolder == null)
            {
                existingFolder = new IndexedFolder
                {
                    DirectoryPath = normalizedPath,
                    LastScannedUtc = DateTime.MinValue
                };
                _folderCollection.Insert(existingFolder);
                _database.Checkpoint();
            }

            if (_indexingDirectories.Contains(normalizedPath)) return false;
            _indexingDirectories.Add(normalizedPath);
            return true;
        }
    }

    private void StartDirectoryScanTask(string normalizedPath)
    {
        Task.Run(() =>
        {
            try
            {
                ScanDirectoryInternal(normalizedPath);
            }
            catch (Exception ex)
            {
                GD.PrintErr($"[AssetIndexService] Scan error on {normalizedPath}: {ex.Message}");
            }
            finally
            {
                lock (_syncLock)
                {
                    _indexingDirectories.Remove(normalizedPath);
                }
                DirectoryIndexingStateChanged?.Invoke(normalizedPath, false);
                DirectoryScanCompleted?.Invoke(normalizedPath);
            }
        });
    }


    public void RemoveDirectory(string directoryPath)
    {
        if (string.IsNullOrWhiteSpace(directoryPath))
        {
            return;
        }

        string normalizedPath = NormalizePath(directoryPath);
        if (string.Equals(normalizedPath, GlobalCasAssetsDirectory, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        lock (_syncLock)
        {
            _indexingDirectories.Remove(normalizedPath);

            _folderCollection.DeleteMany(f => f.DirectoryPath == normalizedPath);
            var leftoverFolders = _folderCollection.FindAll()
                .Where(f => string.Equals(f.DirectoryPath, normalizedPath, StringComparison.OrdinalIgnoreCase))
                .Select(f => (BsonValue)f.Id)
                .ToArray();
            if (leftoverFolders.Length > 0)
            {
                _folderCollection.DeleteMany(Query.In("_id", leftoverFolders));
            }

            _assetCollection.DeleteMany(Query.EQ("DirectoryPath", normalizedPath));
            _assetCollection.DeleteMany(Query.StartsWith("FilePath", normalizedPath + "/"));
            _assetCollection.DeleteMany(Query.StartsWith("FilePath", normalizedPath + "\\"));

            _database.Checkpoint();
        }

        DirectoryIndexingStateChanged?.Invoke(normalizedPath, false);
    }

    public void RescanDirectory(string directoryPath)
    {
        if (string.IsNullOrWhiteSpace(directoryPath))
        {
            return;
        }

        string normalizedPath = NormalizePath(directoryPath);
        if (IsForbiddenPath(normalizedPath) || !Directory.Exists(normalizedPath))
        {
            return;
        }

        lock (_syncLock)
        {
            if (_indexingDirectories.Contains(normalizedPath))
            {
                return;
            }
            _indexingDirectories.Add(normalizedPath);
        }

        DirectoryIndexingStateChanged?.Invoke(normalizedPath, true);

        Task.Run(() =>
        {
            try
            {
                ScanDirectoryInternal(normalizedPath);
            }
            catch (Exception ex)
            {
                GD.PrintErr($"[AssetIndexService] Rescan error on {normalizedPath}: {ex.Message}");
            }
            finally
            {
                lock (_syncLock)
                {
                    _indexingDirectories.Remove(normalizedPath);
                }
                DirectoryIndexingStateChanged?.Invoke(normalizedPath, false);
                DirectoryScanCompleted?.Invoke(normalizedPath);
            }
        });
    }
    public void RescanAllDirectories()
    {
        List<string> dirsToScan = PrepareDirectoriesForRescan();

        foreach (var dir in dirsToScan)
        {
            DirectoryIndexingStateChanged?.Invoke(dir, true);
        }

        Task.Run(() => ExecuteRescanTask(dirsToScan));
    }

    private List<string> PrepareDirectoriesForRescan()
    {
        List<string> dirsToScan;
        lock (_syncLock)
        {
            CleanOrphanedAssetsFromIndex();

            dirsToScan = _folderCollection.FindAll()
                .Select(f => f.DirectoryPath)
                .Where(p => Directory.Exists(p) && !_indexingDirectories.Contains(p) && !string.Equals(p, GlobalCasAssetsDirectory, StringComparison.OrdinalIgnoreCase))
                .ToList();

            foreach (var dir in dirsToScan)
            {
                _indexingDirectories.Add(dir);
            }

            _database.Checkpoint();
        }
        return dirsToScan;
    }

    private void CleanOrphanedAssetsFromIndex()
    {
        var validFolders = _folderCollection.FindAll()
            .Select(f => f.DirectoryPath)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        string globalArchive = NormalizePath(MapAssetManager.GlobalArchiveDirectory);
        string p2pArchive = NormalizePath(MapAssetManager.P2PArchiveDirectory);

        var orphanedAssets = _assetCollection.FindAll()
            .Where(a => (!validFolders.Contains(a.DirectoryPath) &&
                         !string.Equals(a.DirectoryPath, GlobalCasAssetsDirectory, StringComparison.OrdinalIgnoreCase) &&
                         !a.FilePath.StartsWith(globalArchive, StringComparison.OrdinalIgnoreCase) &&
                         !a.FilePath.StartsWith(p2pArchive, StringComparison.OrdinalIgnoreCase) &&
                         string.IsNullOrEmpty(a.MapName)) ||
                        IsForbiddenPath(a.DirectoryPath))
            .Select(a => (BsonValue)a.Id)
            .ToArray();

        if (orphanedAssets.Length > 0)
        {
            _assetCollection.DeleteMany(Query.In("_id", orphanedAssets));
        }
    }

    private void ExecuteRescanTask(List<string> dirsToScan)
    {
        try
        {
            ScanAllCasManifests();
            DirectoryScanCompleted?.Invoke(GlobalCasAssetsDirectory);
        }
        catch (Exception ex)
        {
            GD.PrintErr($"[AssetIndexService] RescanAll CAS manifest scan error: {ex.Message}");
        }

        foreach (var dir in dirsToScan)
        {
            RescanSingleDirectory(dir);
        }
    }

    private void RescanSingleDirectory(string dir)
    {
        try
        {
            ScanDirectoryInternal(dir);
        }
        catch (Exception ex)
        {
            GD.PrintErr($"[AssetIndexService] RescanAll error on {dir}: {ex.Message}");
        }
        finally
        {
            lock (_syncLock)
            {
                _indexingDirectories.Remove(dir);
            }
            DirectoryIndexingStateChanged?.Invoke(dir, false);
            DirectoryScanCompleted?.Invoke(dir);
        }
    }
    private void ScanDirectoryInternal(string normalizedDirectoryPath)
    {
        if (!Directory.Exists(normalizedDirectoryPath)) return;

        if (string.Equals(normalizedDirectoryPath, GlobalCasAssetsDirectory, StringComparison.OrdinalIgnoreCase))
        {
            ScanAllCasManifests();
            DirectoryScanCompleted?.Invoke(GlobalCasAssetsDirectory);
            return;
        }

        var discoveredFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var files = Directory.EnumerateFiles(normalizedDirectoryPath, "*.*", SearchOption.AllDirectories);
        var existingAssets = GetExistingAssetsInDirectory(normalizedDirectoryPath);
        var batchToUpsert = new List<IndexedAsset>();

        foreach (string filePath in files)
        {
            ProcessScannedFile(filePath, normalizedDirectoryPath, discoveredFiles, existingAssets, batchToUpsert);
        }

        if (batchToUpsert.Count > 0)
        {
            lock (_syncLock) { _assetCollection.Upsert(batchToUpsert); }
            batchToUpsert.Clear();
        }

        CleanupStaleAssetsAndRecordScan(normalizedDirectoryPath, existingAssets, discoveredFiles);
    }

    private Dictionary<string, IndexedAsset> GetExistingAssetsInDirectory(string normalizedDirectoryPath)
    {
        lock (_syncLock)
        {
            return _assetCollection.Find(Query.EQ("DirectoryPath", normalizedDirectoryPath))
                .ToDictionary(x => x.FilePath, StringComparer.OrdinalIgnoreCase);
        }
    }

    private void ProcessScannedFile(string filePath, string normalizedDirectoryPath, HashSet<string> discoveredFiles, Dictionary<string, IndexedAsset> existingAssets, List<IndexedAsset> batchToUpsert)
    {
        string normalizedFilePath = NormalizePath(filePath);
        string extension = Path.GetExtension(normalizedFilePath).ToLowerInvariant();

        if (IsIgnoredFileExtension(extension) || IsMetadataSidecarFile(normalizedFilePath)) return;

        discoveredFiles.Add(normalizedFilePath);
        try
        {
            var fileInfo = new FileInfo(normalizedFilePath);
            string? expectedDirType = GetExpectedAssetTypeFromDirectory(normalizedFilePath);

            existingAssets.TryGetValue(normalizedFilePath, out var existingAsset);
            if (IsExistingAssetUpToDate(existingAsset, fileInfo, expectedDirType, normalizedFilePath)) return;

            var newAsset = GenerateIndexedAssetFromFile(normalizedFilePath, normalizedDirectoryPath, fileInfo, extension, expectedDirType, existingAsset);
            batchToUpsert.Add(newAsset);

            if (newAsset.Extension == ".rmesh" && !GlbThumbnailRenderer.HasDiskCache(normalizedFilePath, newAsset.Blake3))
            {
                GlbThumbnailRenderer.EnqueueRequest(normalizedFilePath, fileInfo.LastWriteTimeUtc, newAsset.Blake3, isHighPriority: false);
            }

            if (batchToUpsert.Count >= 250)
            {
                lock (_syncLock) { _assetCollection.Upsert(batchToUpsert); }
                batchToUpsert.Clear();
            }
        }
        catch (Exception ex)
        {
            GD.PrintErr($"[AssetIndexService] Scan error on {normalizedFilePath}: {ex.Message}");
        }
    }

    private bool IsIgnoredFileExtension(string extension)
    {
        return extension == ".cache" || extension == ".log" || extension == ".tmp" || extension == ".uid";
    }

    private bool IsMetadataSidecarFile(string normalizedFilePath)
    {
        return normalizedFilePath.EndsWith(".json", StringComparison.OrdinalIgnoreCase) &&
               File.Exists(normalizedFilePath.Substring(0, normalizedFilePath.Length - 5));
    }

    private string? GetExpectedAssetTypeFromDirectory(string normalizedFilePath)
    {
        string? dir = Path.GetDirectoryName(normalizedFilePath);
        if (string.IsNullOrEmpty(dir)) return null;

        string[] dirParts = dir.Split(new[] { '/', '\\' }, StringSplitOptions.RemoveEmptyEntries);
        foreach (var part in dirParts.Reverse())
        {
            if (Realm.Shared.Metadata.RealmMetadataHelper.IsValidAssetTypeForExtension(normalizedFilePath, part, out string canonical, out _))
            {
                return canonical;
            }
        }
        return null;
    }

    private bool IsExistingAssetUpToDate(IndexedAsset? existingAsset, FileInfo fileInfo, string? expectedDirType, string normalizedFilePath)
    {
        if (existingAsset == null) return false;
        if (existingAsset.FileSizeBytes != fileInfo.Length) return false;
        if (existingAsset.LastModifiedUtc != fileInfo.LastWriteTimeUtc) return false;
        if (!IsAssetMetadataValid(existingAsset, expectedDirType)) return false;

        EnqueueThumbnailRequestIfNeeded(existingAsset.Extension, normalizedFilePath, existingAsset.Blake3);

        return true;
    }

    private bool IsAssetMetadataValid(IndexedAsset existingAsset, string? expectedDirType)
    {
        if (string.IsNullOrEmpty(existingAsset.Blake3)) return false;
        if (string.IsNullOrEmpty(existingAsset.AssetType)) return false;
        if (existingAsset.ChromaKey == null) return false;
        if (expectedDirType != null && !string.Equals(existingAsset.AssetType, expectedDirType, StringComparison.OrdinalIgnoreCase)) return false;

        return true;
    }

    private IndexedAsset GenerateIndexedAssetFromFile(string normalizedFilePath, string normalizedDirectoryPath, FileInfo fileInfo, string extension, string? expectedDirType, IndexedAsset? existingAsset)
    {
        string? metaJson = Realm.Shared.Metadata.RealmMetadataHelper.ExtractMetadata(normalizedFilePath);
        var parsedMeta = ExtractParsedMetadata(metaJson, normalizedFilePath);
        var tags = LoadTagsForFile(normalizedFilePath, normalizedDirectoryPath, parsedMeta.Tags);

        var asset = existingAsset ?? new IndexedAsset();
        PopulateBasicAssetData(asset, normalizedFilePath, normalizedDirectoryPath, fileInfo, extension, tags, existingAsset);
        PopulateAssetMetadata(asset, normalizedFilePath, expectedDirType, metaJson, parsedMeta, tags);

        return asset;
    }

    private void PopulateBasicAssetData(IndexedAsset asset, string normalizedFilePath, string normalizedDirectoryPath, FileInfo fileInfo, string extension, List<string> tags, IndexedAsset? existingAsset)
    {
        asset.FilePath = normalizedFilePath;
        asset.FileName = Path.GetFileName(normalizedFilePath);
        asset.Extension = extension;
        asset.DirectoryPath = normalizedDirectoryPath;
        asset.FileSizeBytes = fileInfo.Length;
        asset.LastModifiedUtc = fileInfo.LastWriteTimeUtc;
        asset.Tags = tags;
        asset.MetadataJson = JsonSerializer.Serialize(new AssetMetadataModel { Tags = tags });
        asset.MapName = existingAsset?.MapName ?? asset.MapName;
        asset.MapVersion = existingAsset?.MapVersion ?? asset.MapVersion;
    }

    private void PopulateAssetMetadata(IndexedAsset asset, string normalizedFilePath, string? expectedDirType, string? metaJson, (string? AssetType, string? Blake3, List<string> Tags, bool? HasMask, string? ChromaKey) parsedMeta, List<string> tags)
    {
        string? assetType = DetermineAssetType(parsedMeta.AssetType, expectedDirType, normalizedFilePath, tags);
        string normBlake3 = GetOrComputeBlake3(parsedMeta.Blake3, normalizedFilePath);

        asset.HasRealmMetadata = !string.IsNullOrEmpty(metaJson) || !string.IsNullOrEmpty(assetType);
        asset.AssetType = assetType;
        asset.HasPlayerColorMask = parsedMeta.HasMask ?? DetermineHasPlayerColorMask(metaJson, normalizedFilePath);

        string? extractedChroma = Realm.Shared.Metadata.RealmMetadataHelper.ExtractChromaKeyFromMetadataJson(metaJson) ?? Realm.Shared.Metadata.RealmMetadataHelper.ExtractChromaKey(normalizedFilePath);
        asset.ChromaKey = parsedMeta.ChromaKey ?? extractedChroma ?? string.Empty;
        asset.Blake3 = !string.IsNullOrEmpty(normBlake3) ? Realm.Shared.Distribution.ContentAddressableStorage.NormalizeBlake3Hash(normBlake3) : string.Empty;
    }

    private (string? AssetType, string? Blake3, List<string> Tags, bool? HasMask, string? ChromaKey) ExtractParsedMetadata(string? metaJson, string normalizedFilePath)
    {
        if (string.IsNullOrEmpty(metaJson))
        {
            return (null, null, new List<string>(), null, null);
        }

        return ExtractParsedMetadataFromJson(metaJson, normalizedFilePath);
    }

    private (string? AssetType, string? Blake3, List<string> Tags, bool? HasMask, string? ChromaKey) ExtractParsedMetadataFromJson(string metaJson, string normalizedFilePath)
    {
        try
        {
            var node = JsonNode.Parse(metaJson);
            if (node is JsonObject obj)
            {
                string? assetType = ExtractAssetTypeFromJson(obj, normalizedFilePath);
                string? normBlake3 = obj.TryGetPropertyValue("blake3", out var blake3Node) ? blake3Node?.ToString() : null;
                var embeddedTags = ExtractTagsFromJson(obj);

                return (assetType, normBlake3, embeddedTags, null, null);
            }
        }
        catch { }

        return (null, null, new List<string>(), null, null);
    }

    private string? ExtractAssetTypeFromJson(JsonObject obj, string normalizedFilePath)
    {
        string? typeVal = null;
        if (obj.TryGetPropertyValue("asset_type", out var assetTypeNode)) typeVal = assetTypeNode?.ToString();
        else if (obj.TryGetPropertyValue("AssetType", out var AssetTypeNodeUpper)) typeVal = AssetTypeNodeUpper?.ToString();
        else if (obj.TryGetPropertyValue("default_asset_type", out var defaultAssetTypeNode)) typeVal = defaultAssetTypeNode?.ToString();

        if (!string.IsNullOrEmpty(typeVal) && Realm.Shared.Metadata.RealmMetadataHelper.IsValidAssetTypeForExtension(normalizedFilePath, typeVal, out string canonical, out _))
        {
            return canonical;
        }
        return null;
    }

    private List<string> ExtractTagsFromJson(JsonObject obj)
    {
        var embeddedTags = new List<string>();
        if (obj.TryGetPropertyValue("tags", out var tagsNode) && tagsNode is JsonArray arr)
        {
            foreach (var item in arr)
            {
                string? t = item?.ToString()?.Trim();
                if (!string.IsNullOrEmpty(t) && !embeddedTags.Contains(t, StringComparer.OrdinalIgnoreCase))
                {
                    embeddedTags.Add(t);
                }
            }
        }
        return embeddedTags;
    }

    private string? DetermineAssetType(string? parsedType, string? expectedDirType, string normalizedFilePath, List<string> tags)
    {
        if (!string.IsNullOrEmpty(parsedType)) return parsedType;
        if (!string.IsNullOrEmpty(expectedDirType)) return expectedDirType;

        if (tags != null)
        {
            foreach (var tag in tags)
            {
                if (Realm.Shared.Metadata.RealmMetadataHelper.IsValidAssetTypeForExtension(normalizedFilePath, tag, out string canonical, out _))
                {
                    return canonical;
                }
            }
        }
        return null;
    }

    private string GetOrComputeBlake3(string? parsedBlake3, string normalizedFilePath)
    {
        if (!string.IsNullOrEmpty(parsedBlake3)) return parsedBlake3;
        try
        {
            return Realm.Shared.Metadata.RealmMetadataHelper.ComputeBlake3(normalizedFilePath);
        }
        catch { return string.Empty; }
    }

    private void CleanupStaleAssetsAndRecordScan(string normalizedDirectoryPath, Dictionary<string, IndexedAsset> existingAssets, HashSet<string> discoveredFiles)
    {
        lock (_syncLock)
        {
            var idsToDelete = existingAssets.Values
                .Where(x => !discoveredFiles.Contains(x.FilePath))
                .Select(x => (BsonValue)x.Id)
                .ToArray();

            if (idsToDelete.Length > 0)
            {
                _assetCollection.DeleteMany(Query.In("_id", idsToDelete));
            }

            var folderRecord = _folderCollection.FindOne(x => x.DirectoryPath == normalizedDirectoryPath);
            if (folderRecord != null)
            {
                folderRecord.LastScannedUtc = DateTime.UtcNow;
                _folderCollection.Update(folderRecord);
            }

            _database.Checkpoint();
        }
    }


    private struct ParsedAssetMetadata
    {
        public string? AssetType;
        public bool? SupportsTeamColor;
        public string? ChromaKey;
        public List<string> Tags;
    }

    private static ParsedAssetMetadata ParseMetadataHeaders(string? metaJson, string filePath)
    {
        var result = new ParsedAssetMetadata { Tags = new List<string>() };
        if (string.IsNullOrWhiteSpace(metaJson)) return result;

        try
        {
            if (JsonNode.Parse(metaJson) is JsonObject obj)
            {
                result.AssetType = ExtractAssetTypeFromMetadata(obj, filePath);
                result.SupportsTeamColor = ExtractSupportsTeamColor(obj);
                result.ChromaKey = ExtractChromaKeyFromMetadata(obj);
                ExtractTagsFromMetadata(obj, result.Tags);
            }
        }
        catch { }

        return result;
    }

    private static string? ExtractAssetTypeFromMetadata(JsonObject obj, string filePath)
    {
        string? typeVal = obj["asset_type"]?.ToString() ?? obj["AssetType"]?.ToString() ?? obj["default_asset_type"]?.ToString();
        if (!string.IsNullOrEmpty(typeVal) && Realm.Shared.Metadata.RealmMetadataHelper.IsValidAssetTypeForExtension(filePath, typeVal, out string canonical, out _))
        {
            return canonical;
        }
        return null;
    }

    private static bool? ExtractSupportsTeamColor(JsonObject obj)
    {
        if (obj["team_color"] != null && bool.TryParse(obj["team_color"]?.ToString(), out bool tcVal)) return tcVal;
        if (obj["supports_team_color"] != null && bool.TryParse(obj["supports_team_color"]?.ToString(), out bool stcVal)) return stcVal;
        if (obj["has_player_color_mask"] != null && bool.TryParse(obj["has_player_color_mask"]?.ToString(), out bool hpcmVal)) return hpcmVal;
        return null;
    }

    private static string? ExtractChromaKeyFromMetadata(JsonObject obj)
    {
        return obj["chroma_key"]?.ToString() ?? obj["chromaKey"]?.ToString() ?? obj["target_hex"]?.ToString() ?? obj["targetHex"]?.ToString();
    }

    private static void ExtractTagsFromMetadata(JsonObject obj, List<string> resultTags)
    {
        if (obj["tags"] is JsonArray arr)
        {
            foreach (var item in arr)
            {
                string? t = item?.ToString()?.Trim();
                if (!string.IsNullOrEmpty(t) && !resultTags.Contains(t, StringComparer.OrdinalIgnoreCase))
                {
                    resultTags.Add(t);
                }
            }
        }
    }


    private static bool DetermineHasPlayerColorMask(string? metaJson, string? filePath)
    {
        if (!string.IsNullOrEmpty(metaJson) && Realm.Shared.Metadata.RealmMetadataHelper.ExtractSupportsTeamColorFromMetadataJson(metaJson))
        {
            return true;
        }

        if (!string.IsNullOrEmpty(filePath))
        {
            bool? supports = Realm.Shared.Metadata.RealmMetadataHelper.ExtractSupportsTeamColorWithMetadata(metaJson, filePath);
            if (supports.HasValue)
            {
                return supports.Value;
            }
        }

        return false;
    }

    private static List<string> ExtractTagsFromMetadataJson(string? metaJson)
    {
        var list = new List<string>();
        if (string.IsNullOrWhiteSpace(metaJson)) return list;
        try
        {
            var node = JsonNode.Parse(metaJson);
            if (node is JsonObject obj && obj["tags"] is JsonArray arr)
            {
                foreach (var item in arr)
                {
                    string? t = item?.ToString()?.Trim();
                    if (!string.IsNullOrEmpty(t) && !list.Contains(t, StringComparer.OrdinalIgnoreCase))
                    {
                        list.Add(t);
                    }
                }
            }
        }
        catch { }
        return list;
    }
    private List<string> LoadTagsForFile(string filePath, string rootDirectory, List<string>? preloadedTags = null)
    {
        var tagSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        PopulateInitialTags(filePath, preloadedTags, tagSet);

        if (tagSet.Count == 0 && !filePath.StartsWith(GlobalCasAssetsDirectory, StringComparison.OrdinalIgnoreCase))
        {
            TryLoadTagsFromSidecar(filePath, tagSet);
        }

        if (tagSet.Count == 0)
        {
            GenerateFallbackTags(filePath, rootDirectory, tagSet);
        }

        return tagSet.OrderBy(t => t, StringComparer.OrdinalIgnoreCase).ToList();
    }

    private void PopulateInitialTags(string filePath, List<string>? preloadedTags, HashSet<string> tagSet)
    {
        if (preloadedTags != null && preloadedTags.Count > 0)
        {
            foreach (var tag in preloadedTags) tagSet.Add(tag);
        }
        else
        {
            var embeddedTags = RealmMetadataHelper.ExtractTags(filePath);
            foreach (var tag in embeddedTags) tagSet.Add(tag);
        }
    }

    private void TryLoadTagsFromSidecar(string filePath, HashSet<string> tagSet)
    {
        string sidecarJsonWithExt = filePath + ".json";
        string sidecarJsonNoExt = Path.Combine(Path.GetDirectoryName(filePath)!, Path.GetFileNameWithoutExtension(filePath) + ".json");

        string? foundMetadataPath = null;
        if (File.Exists(sidecarJsonWithExt)) foundMetadataPath = sidecarJsonWithExt;
        else if (File.Exists(sidecarJsonNoExt) && !string.Equals(sidecarJsonNoExt, filePath, StringComparison.OrdinalIgnoreCase)) foundMetadataPath = sidecarJsonNoExt;

        if (foundMetadataPath != null)
        {
            ParseTagsFromSidecarJson(foundMetadataPath, tagSet);
        }
    }

    private void ParseTagsFromSidecarJson(string foundMetadataPath, HashSet<string> tagSet)
    {
        try
        {
            string jsonContent = File.ReadAllText(foundMetadataPath);
            if (JsonNode.Parse(jsonContent) is JsonObject jsonObject && jsonObject["tags"] is JsonArray tagsArray)
            {
                foreach (var item in tagsArray)
                {
                    string? tagStr = item?.ToString()?.Trim();
                    if (!string.IsNullOrEmpty(tagStr)) tagSet.Add(tagStr);
                }
            }
        }
        catch { }
    }

    private void GenerateFallbackTags(string filePath, string rootDirectory, HashSet<string> tagSet)
    {
        string nameWithoutExt = Path.GetFileNameWithoutExtension(filePath);
        var nameTokens = nameWithoutExt.Split(new[] { '_', '-', ' ', '.', '@' }, StringSplitOptions.RemoveEmptyEntries);
        foreach (var token in nameTokens)
        {
            if (token.Length > 1 && !char.IsDigit(token[0])) tagSet.Add(token.ToLowerInvariant());
        }

        string relativeDir = Path.GetRelativePath(rootDirectory, Path.GetDirectoryName(filePath) ?? rootDirectory);
        if (!string.IsNullOrEmpty(relativeDir) && relativeDir != ".")
        {
            var dirTokens = relativeDir.Split(new[] { '/', '\\' }, StringSplitOptions.RemoveEmptyEntries);
            foreach (var dir in dirTokens)
            {
                if (!string.IsNullOrWhiteSpace(dir)) tagSet.Add(dir.ToLowerInvariant());
            }
        }

        string ext = Path.GetExtension(filePath).TrimStart('.').ToLowerInvariant();
        if (!string.IsNullOrEmpty(ext)) tagSet.Add(ext);
    }


    public void UpdateAssetTags(string filePath, List<string> newTags)
    {
        if (string.IsNullOrWhiteSpace(filePath))
        {
            return;
        }

        string normalizedFilePath = NormalizePath(filePath);
        lock (_syncLock)
        {
            var asset = _assetCollection.FindOne(x => x.FilePath == normalizedFilePath);
            if (asset == null)
            {
                return;
            }

            asset.Tags = newTags.Where(t => !string.IsNullOrWhiteSpace(t)).Select(t => t.Trim().ToLowerInvariant()).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            asset.MetadataJson = JsonSerializer.Serialize(new AssetMetadataModel { Tags = asset.Tags });
            _assetCollection.Update(asset);

            RealmMetadataHelper.SetTags(normalizedFilePath, asset.Tags);

            bool isCasFile = normalizedFilePath.StartsWith(GlobalCasAssetsDirectory + "/", StringComparison.OrdinalIgnoreCase);
            if (isCasFile)
            {
                string hash = Realm.Shared.Distribution.ContentAddressableStorage.NormalizeBlake3Hash(Path.GetFileName(normalizedFilePath));
                string? updatedMeta = RealmMetadataHelper.ExtractMetadata(normalizedFilePath);
                if (!string.IsNullOrWhiteSpace(updatedMeta))
                {
                    MapAssetManager.Storage.UpdateSidecarCache(hash, updatedMeta);
                }
            }
            else
            {
                try
                {
                    string sidecarPath = normalizedFilePath + ".json";
                    var root = new JsonObject
                    {
                        ["tags"] = new JsonArray(asset.Tags.Select(t => (JsonNode)JsonValue.Create(t)!).ToArray())
                    };
                    File.WriteAllText(sidecarPath, root.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
                }
                catch (Exception ex)
                {
                    GD.PrintErr($"[AssetIndexService] UpdateAssetTags error writing sidecar: {ex.Message}");
                }
            }

            _database.Checkpoint();
        }
    }

    public void UpdateAssetType(string filePath, string newAssetType)
    {
        if (string.IsNullOrWhiteSpace(filePath))
        {
            return;
        }

        string normalizedFilePath = NormalizePath(filePath);
        lock (_syncLock)
        {
            var asset = _assetCollection.FindOne(x => x.FilePath == normalizedFilePath);
            if (asset == null)
            {
                return;
            }

            asset.AssetType = newAssetType;
            asset.HasRealmMetadata = true;
            _assetCollection.Update(asset);

            RealmMetadataHelper.SetAssetType(normalizedFilePath, newAssetType);

            bool isCasFile = normalizedFilePath.StartsWith(GlobalCasAssetsDirectory + "/", StringComparison.OrdinalIgnoreCase);
            if (isCasFile)
            {
                string hash = Realm.Shared.Distribution.ContentAddressableStorage.NormalizeBlake3Hash(Path.GetFileName(normalizedFilePath));
                string? updatedMeta = RealmMetadataHelper.ExtractMetadata(normalizedFilePath);
                if (!string.IsNullOrWhiteSpace(updatedMeta))
                {
                    MapAssetManager.Storage.UpdateSidecarCache(hash, updatedMeta);
                }
            }

            _database.Checkpoint();
        }
    }

    public static string GetCanonicalBlake3(IndexedAsset asset)
    {
        if (!string.IsNullOrEmpty(asset.Blake3))
        {
            return asset.Blake3;
        }

        if (asset.FilePath.StartsWith(GlobalCasAssetsDirectory + "/", StringComparison.OrdinalIgnoreCase) ||
            asset.FilePath.StartsWith(GlobalCasAssetsDirectory + "\\", StringComparison.OrdinalIgnoreCase))
        {
            return Realm.Shared.Distribution.ContentAddressableStorage.NormalizeBlake3Hash(Path.GetFileName(asset.FilePath));
        }

        return Realm.Shared.Distribution.ContentAddressableStorage.NormalizeBlake3Hash(asset.FileName);
    }

    public IndexedAsset? GetAssetByPath(string filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath))
        {
            return null;
        }

        string normalizedFilePath = NormalizePath(filePath);
        lock (_syncLock)
        {
            return _assetCollection.FindOne(x => x.FilePath == normalizedFilePath);
        }
    }

    public IndexedAsset? GetAssetByBlake3(string blake3Hash)
    {
        if (string.IsNullOrWhiteSpace(blake3Hash))
        {
            return null;
        }

        string norm = Realm.Shared.Distribution.ContentAddressableStorage.NormalizeBlake3Hash(blake3Hash);
        lock (_syncLock)
        {
            return _assetCollection.FindOne(x => x.Blake3 == norm);
        }
    }

    public static bool IsHexHash(string? str)
    {
        if (string.IsNullOrWhiteSpace(str) || str.Length != 64) return false;
        for (int i = 0; i < str.Length; i++)
        {
            char c = str[i];
            if (!((c >= '0' && c <= '9') || (c >= 'a' && c <= 'f') || (c >= 'A' && c <= 'F')))
            {
                return false;
            }
        }
        return true;
    }

    public string ResolvePrettyFileName(string filePath, string? fallbackFileName = null)
    {
        string rawFileName = !string.IsNullOrEmpty(fallbackFileName) ? fallbackFileName : Path.GetFileName(filePath);
        string rawNoExt = Path.GetFileNameWithoutExtension(rawFileName);

        if (!IsHexHash(rawNoExt))
        {
            return rawFileName;
        }

        var asset = GetAssetByPath(filePath) ?? GetAssetByBlake3(rawNoExt);
        if (asset != null && !string.IsNullOrWhiteSpace(asset.FileName) && !IsHexHash(Path.GetFileNameWithoutExtension(asset.FileName)))
        {
            return asset.FileName;
        }

        return rawFileName;
    }
    public List<IndexedAsset> SearchAssets(
        string? searchTerm,
        IReadOnlyCollection<string>? allowedExtensions = null,
        string? directoryFilter = null,
        bool requireRealmMetadata = false,
        string? requiredAssetType = null,
        string? mapNameFilter = null,
        string? mapVersionFilter = null,
        bool requirePlayerColorMask = false)
    {
        try
        {
            lock (_syncLock)
            {
                var query = _assetCollection.Query();

                query = ApplyMapAndDirectoryFilters(query, mapNameFilter, mapVersionFilter, directoryFilter);

                HashSet<string>? extensionFilterSet = null;
                query = ApplyExtensionFilters(query, allowedExtensions, out extensionFilterSet);

                query = ApplyMetadataFilters(query, requireRealmMetadata, requiredAssetType, requirePlayerColorMask);

                var candidateList = query.ToList();

                if (extensionFilterSet != null)
                {
                    candidateList = candidateList.Where(x => extensionFilterSet.Contains(x.Extension)).ToList();
                }

                var filtered = ApplySearchTermFilter(candidateList, searchTerm);
                return DeduplicateSearchResults(filtered);
            }
        }
        catch (Exception ex)
        {
            GD.PrintErr($"[AssetIndexService] SearchAssets error: {ex.Message}");
            return new List<IndexedAsset>();
        }
    }

    private ILiteQueryable<IndexedAsset> ApplyMapAndDirectoryFilters(ILiteQueryable<IndexedAsset> query, string? mapNameFilter, string? mapVersionFilter, string? directoryFilter)
    {
        if (!string.IsNullOrWhiteSpace(mapNameFilter))
        {
            string targetVersion = mapVersionFilter ?? "latest";
            if (string.Equals(targetVersion, "latest", StringComparison.OrdinalIgnoreCase))
            {
                targetVersion = GetLatestVersionForMap(mapNameFilter) ?? "";
            }

            if (!string.IsNullOrEmpty(targetVersion))
            {
                return query.Where(x => x.MapName == mapNameFilter && x.MapVersion == targetVersion);
            }
            return query.Where(x => x.MapName == mapNameFilter);
        }
        else if (!string.IsNullOrWhiteSpace(directoryFilter))
        {
            string normalizedDir = NormalizePath(directoryFilter);
            return query.Where(x => x.DirectoryPath == normalizedDir);
        }
        return query;
    }

    private ILiteQueryable<IndexedAsset> ApplyExtensionFilters(ILiteQueryable<IndexedAsset> query, IReadOnlyCollection<string>? allowedExtensions, out HashSet<string>? extensionFilterSet)
    {
        extensionFilterSet = null;
        if (allowedExtensions != null && allowedExtensions.Count > 0)
        {
            var normalizedExtensions = allowedExtensions
                .Select(e => e.Trim().ToLowerInvariant())
                .Select(e => e.StartsWith(".") ? e : "." + e)
                .ToArray();

            extensionFilterSet = normalizedExtensions.ToHashSet(StringComparer.OrdinalIgnoreCase);

            if (normalizedExtensions.Length == 1)
            {
                string singleExt = normalizedExtensions[0];
                return query.Where(x => x.Extension == singleExt);
            }
            else
            {
                var bsonExtensions = normalizedExtensions.Select(e => new BsonValue(e)).ToArray();
                return query.Where(Query.In("Extension", bsonExtensions));
            }
        }
        return query;
    }

    private ILiteQueryable<IndexedAsset> ApplyMetadataFilters(ILiteQueryable<IndexedAsset> query, bool requireRealmMetadata, string? requiredAssetType, bool requirePlayerColorMask)
    {
        var resultQuery = query;
        if (requireRealmMetadata)
        {
            resultQuery = resultQuery.Where(x => x.HasRealmMetadata);
        }

        if (!string.IsNullOrWhiteSpace(requiredAssetType))
        {
            string normalizedRequiredType = RealmMetadataHelper.NormalizeAssetType(requiredAssetType);
            if (string.Equals(requiredAssetType, normalizedRequiredType, StringComparison.OrdinalIgnoreCase))
            {
                resultQuery = resultQuery.Where(x => x.AssetType == requiredAssetType);
            }
            else
            {
                var bsonTypes = new BsonValue[] { new BsonValue(requiredAssetType), new BsonValue(normalizedRequiredType) };
                resultQuery = resultQuery.Where(Query.In("AssetType", bsonTypes));
            }
        }

        if (requirePlayerColorMask)
        {
            resultQuery = resultQuery.Where(x => x.HasPlayerColorMask);
        }

        return resultQuery;
    }

    private List<IndexedAsset> ApplySearchTermFilter(List<IndexedAsset> candidateList, string? searchTerm)
    {
        if (string.IsNullOrWhiteSpace(searchTerm))
        {
            return candidateList.OrderBy(x => x.FileName, StringComparer.OrdinalIgnoreCase).ToList();
        }

        string queryTerm = searchTerm.Trim().ToLowerInvariant();
        return candidateList
            .Where(asset => MatchesSearchTerm(asset, queryTerm))
            .OrderBy(x => x.FileName, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private bool MatchesSearchTerm(IndexedAsset asset, string queryTerm)
    {
        return (asset.Tags != null && asset.Tags.Any(t => t.IndexOf(queryTerm, StringComparison.OrdinalIgnoreCase) >= 0)) ||
               asset.FileName.IndexOf(queryTerm, StringComparison.OrdinalIgnoreCase) >= 0 ||
               asset.FilePath.IndexOf(queryTerm, StringComparison.OrdinalIgnoreCase) >= 0 ||
               (!string.IsNullOrEmpty(asset.MapName) && asset.MapName.IndexOf(queryTerm, StringComparison.OrdinalIgnoreCase) >= 0);
    }

    private List<IndexedAsset> DeduplicateSearchResults(List<IndexedAsset> filtered)
    {
        var seenBlake3 = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var deduplicated = new List<IndexedAsset>();

        foreach (var item in filtered)
        {
            string blake3 = GetCanonicalBlake3(item);
            if (string.IsNullOrEmpty(blake3) || seenBlake3.Add(blake3))
            {
                deduplicated.Add(item);
            }
        }

        return deduplicated;
    }


    private static string NormalizePath(string path)
    {
        return Path.GetFullPath(path).Replace('\\', '/').TrimEnd('/');
    }

    public void Dispose()
    {
        lock (_p2pSyncLock)
        {
            _p2pDatabase?.Dispose();
            _p2pDatabase = null;
        }
        _database.Dispose();
    }
}
