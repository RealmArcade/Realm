using Blake3;
using Godot;
using Realm.Shared.Distribution;
using Realm.Shared.Metadata;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Tasks;

public static class MapAssetManager
{
    static MapAssetManager() {
        EnsureUgcLicense();
    }

    private static readonly object ArchiveLock = new object();

    public static bool IsGodotEngineRunning { get; set; } = true;

    public static void Log(string message)
    {
        if (IsGodotEngineRunning)
        {
            Godot.GD.Print(message);
        }
        else
        {
            Console.WriteLine(message);
        }
    }

    public static void LogErr(string message)
    {
        if (IsGodotEngineRunning)
        {
            Godot.GD.PrintErr(message);
        }
        else
        {
            Console.Error.WriteLine(message);
        }
    }

    private static readonly System.Threading.AsyncLocal<string?> AsyncLocalArchiveDirectory = new();

    public static string? ThreadLocalArchiveDirectory
    {
        get => AsyncLocalArchiveDirectory.Value;
        set => AsyncLocalArchiveDirectory.Value = value;
    }

    private static string? _configuredStoragePath;

    public static string? ConfiguredStoragePath
    {
        get
        {
            if (_configuredStoragePath != null)
            {
                return _configuredStoragePath;
            }

            try
            {
                string[] args = System.Environment.GetCommandLineArgs();
                for (int i = 0; i < args.Length; i++)
                {
                    if (args[i].StartsWith("--storage-path=", StringComparison.OrdinalIgnoreCase))
                    {
                        _configuredStoragePath = args[i].Substring("--storage-path=".Length).Trim('"');
                        return _configuredStoragePath;
                    }
                    if (string.Equals(args[i], "--storage-path", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
                    {
                        _configuredStoragePath = args[i + 1].Trim('"');
                        return _configuredStoragePath;
                    }
                }
            }
            catch { }

            return null;
        }
        set
        {
            _configuredStoragePath = value;
            _storage = null;
        }
    }

    private static Realm.Shared.Distribution.ContentAddressableStorage? _storage;
    public static Realm.Shared.Distribution.ContentAddressableStorage Storage
    {
        get
        {
            return _storage ??= new Realm.Shared.Distribution.ContentAddressableStorage(GlobalArchiveDirectory);
        }
    }

    private static Realm.Shared.Distribution.ContentAddressableStorage? _p2pStorage;
    public static Realm.Shared.Distribution.ContentAddressableStorage P2PStorage
    {
        get
        {
            return _p2pStorage ??= new Realm.Shared.Distribution.ContentAddressableStorage(P2PArchiveDirectory);
        }
    }

    public static string P2PArchiveDirectory
    {
        get
        {
            if (IsGodotEngineRunning)
            {
                string path = ProjectSettings.GlobalizePath("user://p2p_cache");
                if (!Directory.Exists(path))
                {
                    Directory.CreateDirectory(path);
                }
                return path;
            }
            else
            {
                string path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "user_p2p_cache");
                if (!Directory.Exists(path))
                {
                    Directory.CreateDirectory(path);
                }
                return path;
            }
        }
    }

    public static void ClearP2PCache()
    {
        lock (ArchiveLock)
        {
            try
            {
                AssetIndexService.Instance.ClearP2PIndex();
                if (Directory.Exists(P2PArchiveDirectory))
                {
                    Directory.Delete(P2PArchiveDirectory, true);
                    Directory.CreateDirectory(P2PArchiveDirectory);
                }
                _p2pStorage = null;
                MapAssetManager.Log("[MapAssetManager] P2P cache cleared successfully.");
            }
            catch (Exception ex)
            {
                MapAssetManager.LogErr($"[MapAssetManager] Error clearing P2P cache: {ex.Message}");
            }
        }
    }

    public static string GlobalArchiveDirectory
    {
        get
        {
            if (ThreadLocalArchiveDirectory != null)
            {
                if (!Directory.Exists(ThreadLocalArchiveDirectory))
                {
                    Directory.CreateDirectory(ThreadLocalArchiveDirectory);
                }
                return ThreadLocalArchiveDirectory;
            }

            string? configured = ConfiguredStoragePath;
            if (!string.IsNullOrEmpty(configured))
            {
                if (!Directory.Exists(configured))
                {
                    Directory.CreateDirectory(configured);
                }
                return configured;
            }

            if (IsGodotEngineRunning)
            {
                return ProjectSettings.GlobalizePath("user://maps");
            }
            else
            {
                string path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "user_maps");
                if (!Directory.Exists(path))
                {
                    Directory.CreateDirectory(path);
                }
                return path;
            }
        }
    }

    public static string ComputeBlake3(byte[] bytes, string? extensionOrPath = null)
    {
        return RealmMetadataHelper.ComputeBlake3(bytes, extensionOrPath);
    }

    public static string ComputeBlake3(Stream stream, string? extensionOrPath = null)
    {
        return RealmMetadataHelper.ComputeBlake3(stream, extensionOrPath);
    }

    public static string ComputeManifestBlake3(MapManifest manifest)
    {
        if (manifest == null) return string.Empty;
        return manifest.ComputeManifestBlake3();
    }

    public static List<string> GetMissingHashes(IEnumerable<string> hashes)
    {
        var missing = new List<string>();
        if (hashes == null) return missing;

        foreach (var hash in hashes)
        {
            string norm = Realm.Shared.Distribution.ContentAddressableStorage.NormalizeBlake3Hash(hash);
            if (!Storage.HasAsset(norm) && !P2PStorage.HasAsset(norm))
            {
                missing.Add(hash);
            }
        }
        return missing;
    }

    public static void AddOrUpdateGlobalArchive(Dictionary<string, byte[]> newFilesByHash)
    {
        if (newFilesByHash == null || newFilesByHash.Count == 0) return;

        foreach (var kvp in newFilesByHash)
        {
            string ext = Path.GetExtension(kvp.Key);
            Storage.StoreAsset(kvp.Value, ext);
        }
    }

    public static void AddOrUpdateP2PArchive(Dictionary<string, byte[]> newFilesByHash)
    {
        if (newFilesByHash == null || newFilesByHash.Count == 0) return;

        foreach (var kvp in newFilesByHash)
        {
            string ext = Path.GetExtension(kvp.Key);
            P2PStorage.StoreAsset(kvp.Value, ext);
        }
    }

    public static string GetMapDirectory(string mapName, string version = "1.0.0", string? manifestHash = null, bool isP2P = false)
    {
        string root = isP2P ? P2PArchiveDirectory : GlobalArchiveDirectory;
        if (!string.IsNullOrWhiteSpace(manifestHash))
        {
            string normHash = Realm.Shared.Distribution.ContentAddressableStorage.NormalizeBlake3Hash(manifestHash);
            return Path.Combine(root, mapName, version, normHash);
        }
        return Path.Combine(root, mapName, version);
    }

    public static string GetManifestPath(string mapName, string version = "1.0.0", string? manifestHash = null, bool isP2P = false)
    {
        return Path.Combine(GetMapDirectory(mapName, version, manifestHash, isP2P), "manifest.json");
    }

    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, bool> _ensuredDirectories = new(StringComparer.OrdinalIgnoreCase);

    public static bool ExtractSingleAsset(string virtualPath, string hash, string targetDirectory, bool isP2P = false)
    {
        if (string.IsNullOrWhiteSpace(virtualPath) || string.IsNullOrWhiteSpace(hash) || string.IsNullOrWhiteSpace(targetDirectory))
            return false;

        string relativePath = GetNormalizedVirtualPath(virtualPath);
        string norm = Realm.Shared.Distribution.ContentAddressableStorage.NormalizeBlake3Hash(hash);
        string destinationFilePath = Path.Combine(targetDirectory, relativePath);

        if (File.Exists(destinationFilePath))
            return true;

        EnsureDestinationDir(destinationFilePath);

        if (TryExtractFromCas(norm, destinationFilePath, isP2P))
            return true;

        MapAssetManager.LogErr($"[MapAssetManager] Could not extract {relativePath}: hash {hash} not found in CAS storage.");
        return false;
    }

    private static string GetNormalizedVirtualPath(string virtualPath)
    {
        string relativePath = virtualPath;
        if (relativePath.StartsWith("res://", StringComparison.OrdinalIgnoreCase))
            relativePath = relativePath.Substring(6);
        return relativePath.TrimStart('/', '\\');
    }

    private static void EnsureDestinationDir(string destinationFilePath)
    {
        string? destinationDir = Path.GetDirectoryName(destinationFilePath);
        if (string.IsNullOrEmpty(destinationDir) || !_ensuredDirectories.TryAdd(destinationDir, true))
            return;

        if (!Directory.Exists(destinationDir))
            Directory.CreateDirectory(destinationDir);
    }

    private static bool TryExtractFromCas(string norm, string destinationFilePath, bool isP2P)
    {
        string? casFilePath = (isP2P ? P2PStorage.FindAssetFilePath(norm) : Storage.FindAssetFilePath(norm))
                           ?? Storage.FindAssetFilePath(norm)
                           ?? P2PStorage.FindAssetFilePath(norm);

        if (casFilePath != null && File.Exists(casFilePath))
            return HardLinkHelper.CreateHardLinkOrCopy(destinationFilePath, casFilePath);

        byte[]? casBytes = (isP2P ? P2PStorage.GetAssetBytes(norm) : Storage.GetAssetBytes(norm))
                        ?? Storage.GetAssetBytes(norm)
                        ?? P2PStorage.GetAssetBytes(norm);

        if (casBytes != null)
        {
            File.WriteAllBytes(destinationFilePath, casBytes);
            return true;
        }

        return false;
    }

    public static void ExtractManifestFiles(MapManifest manifest, string targetDirectory, bool isP2P = false)
    {
        if (manifest == null || manifest.Files == null || string.IsNullOrWhiteSpace(targetDirectory))
        {
            return;
        }

        if (!Directory.Exists(targetDirectory))
        {
            Directory.CreateDirectory(targetDirectory);
        }

        try
        {
            Parallel.ForEach(manifest.Files, kvp =>
            {
                ExtractSingleAsset(kvp.Key, kvp.Value, targetDirectory, isP2P);
            });

            string manifestPath = Path.Combine(targetDirectory, "manifest.json");
            if (!File.Exists(manifestPath))
            {
                File.WriteAllText(manifestPath, manifest.ToJson());
            }
        }
        catch (Exception ex)
        {
            MapAssetManager.LogErr($"[MapAssetManager] Error extracting manifest files to {targetDirectory}: {ex.Message}");
        }
    }

    private static List<string> GetMapNameVariations(string rawName)
    {
        var variations = new List<string>();
        if (string.IsNullOrWhiteSpace(rawName)) return variations;

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        AddMapVariation(rawName, seen, variations);

        string stripped = rawName.Trim();
        stripped = StripPrefixes(stripped);
        stripped = StripSuffixes(stripped);
        
        int dashIdx = stripped.LastIndexOf('-');
        if (dashIdx > 0)
        {
            string candidateTitle = stripped.Substring(0, dashIdx).Trim();
            if (!string.IsNullOrEmpty(candidateTitle))
                AddMapVariation(candidateTitle, seen, variations);
        }
        
        AddMapVariation(stripped, seen, variations);

        return variations;
    }

    private static void AddMapVariation(string name, HashSet<string> seen, List<string> variations)
    {
        name = name.Trim();
        if (string.IsNullOrEmpty(name)) return;
        
        if (seen.Add(name))
            variations.Add(name);
            
        string withUnderscores = name.Replace(' ', '_');
        if (seen.Add(withUnderscores))
            variations.Add(withUnderscores);
            
        string withSpaces = name.Replace('_', ' ');
        if (seen.Add(withSpaces))
            variations.Add(withSpaces);
    }

    private static string StripPrefixes(string name)
    {
        if (name.StartsWith("[Beta-Testing]", StringComparison.OrdinalIgnoreCase))
            name = name.Substring("[Beta-Testing]".Length).Trim();
            
        if (name.StartsWith("Realm_", StringComparison.OrdinalIgnoreCase) || name.StartsWith("Realm ", StringComparison.OrdinalIgnoreCase))
            name = name.Substring(6).Trim();
            
        return name;
    }

    private static string StripSuffixes(string name)
    {
        if (name.EndsWith("_Demo", StringComparison.OrdinalIgnoreCase))
            name = name.Substring(0, name.Length - 5).Trim();
            
        if (name.EndsWith(" Demo", StringComparison.OrdinalIgnoreCase))
            name = name.Substring(0, name.Length - 5).Trim();
            
        return name;
    }

    public static string? FindManifestPath(string mapName, string? version = null, string? manifestHash = null)
    {
        if (string.IsNullOrWhiteSpace(mapName))
            return null;

        ParseMapNameAndVersion(mapName, version, out string targetMapName, out string? targetVersion);

        var variations = GetMapNameVariations(targetMapName);

        if (!string.IsNullOrEmpty(targetVersion))
        {
            if (!string.IsNullOrEmpty(manifestHash))
            {
                string? hashPath = FindManifestWithVersionAndHash(variations, targetVersion, manifestHash);
                if (hashPath != null) return hashPath;
            }

            string? versionPath = FindManifestWithVersion(variations, targetVersion);
            if (versionPath != null) return versionPath;
            
            return SearchArchivesForManifestWithVersion(variations, targetVersion);
        }

        string? noVersionPath = FindManifestWithoutVersion(variations);
        if (noVersionPath != null) return noVersionPath;
        
        return SearchArchivesForManifestWithoutVersion(variations);
    }

    private static void ParseMapNameAndVersion(string mapName, string? version, out string targetMapName, out string? targetVersion)
    {
        targetMapName = mapName.Trim();
        targetVersion = !string.IsNullOrWhiteSpace(version) ? version.Trim() : null;

        if (!string.IsNullOrEmpty(targetVersion) || !targetMapName.Contains('_'))
            return;

        int lastUnderscore = targetMapName.LastIndexOf('_');
        string candidateName = targetMapName.Substring(0, lastUnderscore).Trim();
        string candidateVer = targetMapName.Substring(lastUnderscore + 1).Trim();
        if (!string.IsNullOrEmpty(candidateName) && !string.IsNullOrEmpty(candidateVer) && (candidateVer.Contains('.') || char.IsDigit(candidateVer[0])))
        {
            targetMapName = candidateName;
            targetVersion = candidateVer;
        }
    }

    private static string? FindManifestWithVersionAndHash(List<string> variations, string version, string manifestHash)
    {
        foreach (var variation in variations)
        {
            string directPath = GetManifestPath(variation, version, manifestHash, false);
            if (File.Exists(directPath)) return directPath;
            string p2pPath = GetManifestPath(variation, version, manifestHash, true);
            if (File.Exists(p2pPath)) return p2pPath;
        }
        return null;
    }

    private static string? FindManifestWithVersion(List<string> variations, string version)
    {
        foreach (var variation in variations)
        {
            string directPath = GetManifestPath(variation, version, null, false);
            if (File.Exists(directPath)) return directPath;
            string p2pPath = GetManifestPath(variation, version, null, true);
            if (File.Exists(p2pPath)) return p2pPath;

            string[] baseDirs = new[]
            {
                GetMapDirectory(variation, version, null, false),
                GetMapDirectory(variation, version, null, true)
            };
            
            foreach (var bDir in baseDirs)
            {
                if (!Directory.Exists(bDir)) continue;
                foreach (var sub in Directory.GetDirectories(bDir))
                {
                    string subMf = Path.Combine(sub, "manifest.json");
                    if (File.Exists(subMf)) return subMf;
                }
            }
        }
        return null;
    }

    private static string? SearchArchivesForManifestWithVersion(List<string> variations, string version)
    {
        string[] roots = new[] { GlobalArchiveDirectory, P2PArchiveDirectory };
        foreach (var root in roots)
        {
            if (!Directory.Exists(root)) continue;
            foreach (var dir in Directory.GetDirectories(root))
            {
                string dirName = Path.GetFileName(dir);
                if (dirName.StartsWith(".")) continue;

                string candidate = Path.Combine(dir, version, "manifest.json");
                if (CheckManifestMatchesVariation(candidate, version, variations, dirName))
                    return candidate;

                string versionDir = Path.Combine(dir, version);
                if (!Directory.Exists(versionDir)) continue;
                
                foreach (var hashSub in Directory.GetDirectories(versionDir))
                {
                    string hashCandidate = Path.Combine(hashSub, "manifest.json");
                    if (CheckManifestMatchesVariation(hashCandidate, version, variations, dirName))
                        return hashCandidate;
                }
            }
        }
        return null;
    }

    private static bool CheckManifestMatchesVariation(string path, string version, List<string> variations, string dirName)
    {
        if (!File.Exists(path)) return false;
        try
        {
            var mf = MapManifest.LoadFromFile(path);
            if (mf == null || !string.Equals(mf.Version, version, StringComparison.OrdinalIgnoreCase))
                return false;

            foreach (var variation in variations)
            {
                if (string.Equals(mf.MapName, variation, StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(dirName, variation, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }
        }
        catch { }
        return false;
    }

    private static string? FindManifestWithoutVersion(List<string> variations)
    {
        foreach (var variation in variations)
        {
            string[] checkRoots = new[] { GlobalArchiveDirectory, P2PArchiveDirectory };
            foreach (var cRoot in checkRoots)
            {
                string mapDir = Path.Combine(cRoot, variation);
                if (!Directory.Exists(mapDir)) continue;

                string rootManifest = Path.Combine(mapDir, "manifest.json");
                if (File.Exists(rootManifest)) return rootManifest;

                var versionDirs = Directory.GetDirectories(mapDir);
                Array.Sort(versionDirs, StringComparer.OrdinalIgnoreCase);
                for (int i = versionDirs.Length - 1; i >= 0; i--)
                {
                    string candidate = Path.Combine(versionDirs[i], "manifest.json");
                    if (File.Exists(candidate)) return candidate;

                    var hashDirs = Directory.GetDirectories(versionDirs[i]);
                    Array.Sort(hashDirs, StringComparer.OrdinalIgnoreCase);
                    for (int j = hashDirs.Length - 1; j >= 0; j--)
                    {
                        string hashCandidate = Path.Combine(hashDirs[j], "manifest.json");
                        if (File.Exists(hashCandidate)) return hashCandidate;
                    }
                }
            }
        }
        return null;
    }

    private static string? SearchArchivesForManifestWithoutVersion(List<string> variations)
    {
        string[] searchRoots = new[] { GlobalArchiveDirectory, P2PArchiveDirectory };
        foreach (var root in searchRoots)
        {
            if (!Directory.Exists(root)) continue;

            foreach (var dir in Directory.GetDirectories(root))
            {
                string dirName = Path.GetFileName(dir);
                if (dirName.StartsWith(".")) continue;

                var manifestCandidates = GetManifestCandidatesFromDirectory(dir);

                foreach (var mfPath in manifestCandidates)
                {
                    try
                    {
                        var mf = MapManifest.LoadFromFile(mfPath);
                        if (mf == null) continue;
                        
                        foreach (var variation in variations)
                        {
                            if (string.Equals(mf.MapName, variation, StringComparison.OrdinalIgnoreCase) ||
                                string.Equals(dirName, variation, StringComparison.OrdinalIgnoreCase))
                            {
                                return mfPath;
                            }
                        }
                    }
                    catch { }
                }
            }
        }
        return null;
    }
    
    private static List<string> GetManifestCandidatesFromDirectory(string dir)
    {
        var candidates = new List<string>();
        string rootMf = Path.Combine(dir, "manifest.json");
        if (File.Exists(rootMf)) candidates.Add(rootMf);

        foreach (var sd in Directory.GetDirectories(dir))
        {
            string subMf = Path.Combine(sd, "manifest.json");
            if (File.Exists(subMf)) candidates.Add(subMf);

            foreach (var hsd in Directory.GetDirectories(sd))
            {
                string hsubMf = Path.Combine(hsd, "manifest.json");
                if (File.Exists(hsubMf)) candidates.Add(hsubMf);
            }
        }
        return candidates;
    }

    public static MapManifest? FindHostManifest(string mapName, string? version = null)
    {
        string? manifestPath = FindManifestPath(mapName, version);
        if (!string.IsNullOrEmpty(manifestPath) && File.Exists(manifestPath))
        {
            var manifest = MapManifest.LoadFromFile(manifestPath);
            if (manifest != null) return manifest;
        }

        return IngestHostMap(mapName);
    }

    public static bool IsMapDownloaded(string mapName, string? version = null, string? manifestHash = null)
    {
        if (string.IsNullOrWhiteSpace(mapName))
            return false;

        string? manifestPath = FindManifestPath(mapName, version, manifestHash);
        if (manifestPath == null || !File.Exists(manifestPath))
            return false;

        string mapDir = Path.GetDirectoryName(manifestPath) ?? string.Empty;

        try
        {
            var manifest = ParseManifest(manifestPath);
            if (manifest == null) return false;
            
            if (manifest.Files == null || manifest.Files.Count == 0)
                return true;

            var missingHashes = GetMissingHashes(manifest.Files.Values);
            if (missingHashes.Count > 0)
                return false;

            if (HasMissingDiskFile(manifest, mapDir))
            {
                bool isP2P = manifestPath.Contains("p2p_cache", StringComparison.OrdinalIgnoreCase);
                ExtractManifestFiles(manifest, mapDir, isP2P: isP2P);
            }
            
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static MapManifest? ParseManifest(string manifestPath)
    {
        string manifestJson = File.ReadAllText(manifestPath);
        return MapManifest.LoadFromJson(manifestJson) ?? JsonSerializer.Deserialize<MapManifest>(manifestJson);
    }

    private static bool HasMissingDiskFile(MapManifest manifest, string mapDir)
    {
        foreach (var kvp in manifest.Files)
        {
            string rel = kvp.Key.StartsWith("res://", StringComparison.OrdinalIgnoreCase) ? kvp.Key.Substring(6) : kvp.Key;
            rel = rel.TrimStart('/', '\\');
            if (!File.Exists(Path.Combine(mapDir, rel)))
                return true;
        }
        return false;
    }

    public static void PruneGlobalArchive()
    {
        string archiveDir = GlobalArchiveDirectory;
        Task.Run(() => PruneGlobalArchiveInternal(archiveDir));
    }

    public static void PruneGlobalArchiveSync()
    {
        string archiveDir = GlobalArchiveDirectory;
        PruneGlobalArchiveInternal(archiveDir);
    }

    private static void PruneGlobalArchiveInternal(string archiveDir)
    {
        lock (ArchiveLock)
        {
            try
            {
                MapAssetManager.Log("[MapAssetManager] Starting background pruning process...");

                if (!Directory.Exists(archiveDir)) return;

                CleanLegacyArchiveFiles(archiveDir);

                var referencedHashes = GetReferencedManifestHashes(archiveDir);
                MapAssetManager.Log($"[MapAssetManager] Total referenced BLAKE3 hashes found in manifests: {referencedHashes.Count}");

                PruneUnreferencedCasFiles(referencedHashes);

                MapAssetManager.Log("[MapAssetManager] Pruning complete.");
            }
            catch (Exception ex)
            {
                MapAssetManager.LogErr($"[MapAssetManager] Pruning failed: {ex.Message}");
            }
        }
    }

    private static void CleanLegacyArchiveFiles(string archiveDir)
    {
        string legacyGlobal7z = Path.Combine(archiveDir, "global_assets.7z");
        if (File.Exists(legacyGlobal7z))
        {
            try { File.Delete(legacyGlobal7z); } catch { }
        }
        
        string legacyPckCache = Path.Combine(archiveDir, "pck_cache.json");
        if (File.Exists(legacyPckCache))
        {
            try { File.Delete(legacyPckCache); } catch { }
        }
        
        try
        {
            var legacyPcks = Directory.GetFiles(archiveDir, "*.pck", SearchOption.AllDirectories);
            foreach (var pck in legacyPcks)
            {
                try { File.Delete(pck); } catch { }
            }
        }
        catch { }
    }

    private static HashSet<string> GetReferencedManifestHashes(string archiveDir)
    {
        var manifestFiles = Directory.GetFiles(archiveDir, "manifest.json", SearchOption.AllDirectories);
        var referencedHashes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var file in manifestFiles)
        {
            try
            {
                string content = File.ReadAllText(file);
                var manifest = MapManifest.LoadFromJson(content);
                if (manifest == null || manifest.Files == null) continue;
                
                foreach (var hash in manifest.Files.Values)
                {
                    referencedHashes.Add(hash);
                    referencedHashes.Add(Realm.Shared.Distribution.ContentAddressableStorage.NormalizeBlake3Hash(hash));
                }
            }
            catch (Exception ex)
            {
                MapAssetManager.LogErr($"[MapAssetManager] Error reading manifest {file}: {ex.Message}");
            }
        }
        
        return referencedHashes;
    }

    private static void PruneUnreferencedCasFiles(HashSet<string> referencedHashes)
    {
        if (!Directory.Exists(Storage.AssetsDirectory)) return;
        
        var casFiles = Directory.GetFiles(Storage.AssetsDirectory, "*.*", SearchOption.AllDirectories);
        foreach (var casFile in casFiles)
        {
            string fileHash = Realm.Shared.Distribution.ContentAddressableStorage.NormalizeBlake3Hash(Path.GetFileName(casFile));
            if (referencedHashes.Contains(fileHash)) continue;
            
            try
            {
                File.Delete(casFile);
                string sidecarShard = Path.Combine(Storage.SidecarCacheDirectory, fileHash.Substring(0, 2));
                string sidecarFile = Path.Combine(sidecarShard, $"{fileHash}.json");
                if (File.Exists(sidecarFile))
                {
                    File.Delete(sidecarFile);
                }
                MapAssetManager.Log($"[MapAssetManager] Pruned CAS asset: {casFile}");
            }
            catch { }
        }
    }

    public static void EnsureUgcLicense()
    {
        try
        {
            string mapsDir = GlobalArchiveDirectory;
            if (!Directory.Exists(mapsDir))
            {
                Directory.CreateDirectory(mapsDir);
            }

            string targetPath = Path.Combine(mapsDir, "RealmPlatform_UGC_License.txt");

            string[] candidateSourcePaths = new[]
            {
                PathUtils.FindPath("Terms/RealmPlatform_UGC_License.txt"),
                Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Terms", "RealmPlatform_UGC_License.txt"),
                Path.Combine(OS.GetExecutablePath().GetBaseDir(), "Terms", "RealmPlatform_UGC_License.txt"),
                Path.GetFullPath(Path.Combine(PathUtils.GetProjectRoot(), "..", "Terms", "RealmPlatform_UGC_License.txt")),
                Path.Combine(PathUtils.GetProjectRoot(), "Terms", "RealmPlatform_UGC_License.txt")
            };

            foreach (var candidate in candidateSourcePaths)
            {
                if (!string.IsNullOrEmpty(candidate) && File.Exists(candidate))
                {
                    File.Copy(candidate, targetPath, overwrite: true);
                    break;
                }
            }
        }
        catch (Exception ex)
        {
            LogErr($"[MapAssetManager] Failed to copy UGC license: {ex.Message}");
        }
    }

    public static MapManifest IngestHostMap(string mapPath)
    {
        var manifest = new MapManifest();
        manifest.MapName = Path.GetFileNameWithoutExtension(mapPath);
        manifest.Version = "1.0.0";

        var newFiles = new Dictionary<string, byte[]>();
        string mapDir = ResolveMapDirectory(mapPath);

        if (Directory.Exists(mapDir))
        {
            var existing = TryLoadExistingManifest(mapDir);
            if (existing != null) return existing;

            ProcessMapDirectoryFiles(mapDir, manifest, newFiles);
        }
        else if (File.Exists(mapPath))
        {
            ProcessSingleMapFile(mapPath, manifest, newFiles);
        }
        else
        {
            ProcessEmptyMapFallback(manifest, newFiles);
        }

        AddOrUpdateGlobalArchive(newFiles);
        SaveHostManifest(manifest);

        return manifest;
    }

    private static string ResolveMapDirectory(string mapPath)
    {
        if (Directory.Exists(mapPath)) return mapPath;
        
        if (File.Exists(mapPath) && mapPath.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
            return Path.GetDirectoryName(mapPath) ?? "Realm.MapScript";

        string? foundManifest = FindManifestPath(mapPath);
        if (foundManifest != null) return Path.GetDirectoryName(foundManifest) ?? "Realm.MapScript";

        try
        {
            string globalUser = ProjectSettings.GlobalizePath($"user://maps/{mapPath}");
            if (Directory.Exists(globalUser)) return globalUser;
        }
        catch { }

        try
        {
            string globalRes = ProjectSettings.GlobalizePath($"res://Maps/{mapPath}");
            if (Directory.Exists(globalRes)) return globalRes;
        }
        catch { }

        string combined = Path.Combine(GlobalArchiveDirectory, mapPath);
        if (Directory.Exists(combined)) return combined;

        return "Realm.MapScript";
    }

    private static MapManifest? TryLoadExistingManifest(string mapDir)
    {
        string manifestJsonPath = Path.Combine(mapDir, "manifest.json");
        if (!File.Exists(manifestJsonPath)) return null;

        try
        {
            var existing = MapManifest.LoadFromFile(manifestJsonPath);
            if (existing != null && existing.Files != null && existing.Files.Count > 0)
                return existing;
        }
        catch { }
        return null;
    }

    private static void ProcessMapDirectoryFiles(string mapDir, MapManifest manifest, Dictionary<string, byte[]> newFiles)
    {
        var files = Directory.GetFiles(mapDir, "*.*", SearchOption.AllDirectories);
        foreach (var file in files)
        {
            string relativePath = Path.GetRelativePath(mapDir, file).Replace("\\", "/");
            if (ShouldIgnoreFileForIngestion(relativePath)) continue;

            byte[] bytes = File.ReadAllBytes(file);
            string ext = Path.GetExtension(file).ToLowerInvariant();
            string blake3 = RealmMetadataHelper.ComputeBlake3(bytes, ext);
            string assetKey = string.IsNullOrEmpty(ext) ? blake3 : $"{blake3}{ext}";
            
            newFiles[assetKey] = bytes;
            manifest.Files[relativePath] = assetKey;
        }

        manifest.Assets = MapManifest.UnflattenFilesToAssets(manifest.Files);
    }

    private static bool ShouldIgnoreFileForIngestion(string relativePath)
    {
        return relativePath.StartsWith("bin/", StringComparison.OrdinalIgnoreCase) || 
               relativePath.StartsWith("obj/", StringComparison.OrdinalIgnoreCase) || 
               relativePath.StartsWith(".git/", StringComparison.OrdinalIgnoreCase) ||
               relativePath.StartsWith(".vscode/", StringComparison.OrdinalIgnoreCase) ||
               relativePath.StartsWith(".godot/", StringComparison.OrdinalIgnoreCase) ||
               relativePath.StartsWith(".sidecarcache/", StringComparison.OrdinalIgnoreCase) ||
               relativePath.EndsWith(".tmp", StringComparison.OrdinalIgnoreCase) ||
               relativePath.Equals("manifest.json", StringComparison.OrdinalIgnoreCase);
    }

    private static void ProcessSingleMapFile(string mapPath, MapManifest manifest, Dictionary<string, byte[]> newFiles)
    {
        byte[] bytes = File.ReadAllBytes(mapPath);
        string blake3 = RealmMetadataHelper.ComputeBlake3(bytes, ".json");
        string assetKey = $"{blake3}.json";
        newFiles[assetKey] = bytes;
        manifest.Files[Path.GetFileName(mapPath)] = assetKey;
    }

    private static void ProcessEmptyMapFallback(MapManifest manifest, Dictionary<string, byte[]> newFiles)
    {
        byte[] bytes = Encoding.UTF8.GetBytes("{\"Units\": []}");
        string blake3 = RealmMetadataHelper.ComputeBlake3(bytes, ".json");
        string assetKey = $"{blake3}.json";
        newFiles[assetKey] = bytes;
        manifest.Files["metadata.json"] = assetKey;
    }

    private static void SaveHostManifest(MapManifest manifest)
    {
        try
        {
            string version = !string.IsNullOrWhiteSpace(manifest.Version) ? manifest.Version.Trim() : "1.0.0";
            string manifestBlake3 = ComputeManifestBlake3(manifest);
            string manifestDir = GetMapDirectory(manifest.MapName, version, manifestBlake3, false);
            Directory.CreateDirectory(manifestDir);
            
            string manifestPath = Path.Combine(manifestDir, "manifest.json");
            File.WriteAllText(manifestPath, manifest.ToJson());
            ExtractManifestFiles(manifest, manifestDir, isP2P: false);
            
            MapAssetManager.Log($"[MapAssetManager] Saved host manifest and hardlinks to: {manifestPath}");
            AssetIndexService.Instance.RegisterManifest(manifest, manifestPath, isP2P: false);
        }
        catch (Exception ex)
        {
            MapAssetManager.LogErr($"[MapAssetManager] Failed to write host manifest: {ex.Message}");
        }
    }

    public static long GetMapTotalSizeBytes(string mapName, string? version = null)
    {
        if (string.IsNullOrWhiteSpace(mapName))
            return 0;

        try
        {
            var manifest = FindHostManifest(mapName, version);
            if (manifest != null)
            {
                long manifestSize = GetSizeFromManifest(manifest);
                if (manifestSize > 0) return manifestSize;
            }

            long fallbackSize = GetSizeFromDirectoryFallbacks(mapName, version);
            if (fallbackSize > 0) return fallbackSize;
        }
        catch { }

        return 0;
    }

    private static long GetSizeFromManifest(MapManifest manifest)
    {
        if (manifest.FileSizes != null && manifest.FileSizes.Count > 0)
        {
            long totalFromManifest = 0;
            foreach (var size in manifest.FileSizes.Values)
                totalFromManifest += size;
                
            if (totalFromManifest > 0)
                return totalFromManifest;
        }

        long totalCas = 0;
        if (manifest.Files != null && manifest.Files.Count > 0)
        {
            foreach (var hash in manifest.Files.Values)
            {
                string? assetPath = Storage.FindAssetFilePath(hash);
                if (assetPath != null && File.Exists(assetPath))
                {
                    totalCas += new FileInfo(assetPath).Length;
                }
            }
        }
        
        return totalCas;
    }

    private static long GetSizeFromDirectoryFallbacks(string mapName, string? version)
    {
        string? manifestPath = FindManifestPath(mapName, version);
        if (manifestPath != null && File.Exists(manifestPath))
        {
            string? dir = Path.GetDirectoryName(manifestPath);
            if (dir != null && Directory.Exists(dir))
            {
                long dirSize = CalculateDirectorySize(dir);
                if (dirSize > 0) return dirSize;
            }
        }

        string userMapDir = ProjectSettings.GlobalizePath($"user://maps/{mapName}");
        if (Directory.Exists(userMapDir))
        {
            long dirSize = CalculateDirectorySize(userMapDir);
            if (dirSize > 0) return dirSize;
        }

        string resMapDir = ProjectSettings.GlobalizePath($"res://Maps/{mapName}");
        if (Directory.Exists(resMapDir))
        {
            long dirSize = CalculateDirectorySize(resMapDir);
            if (dirSize > 0) return dirSize;
        }

        return 0;
    }

    private static long CalculateDirectorySize(string directoryPath)
    {
        if (!Directory.Exists(directoryPath))
        {
            return 0;
        }

        long total = 0;
        try
        {
            var files = Directory.GetFiles(directoryPath, "*.*", SearchOption.AllDirectories);
            foreach (var file in files)
            {
                total += new FileInfo(file).Length;
            }
        }
        catch
        {
        }

        return total;
    }

    public static string FormatSizeInMB(long bytes)
    {
        if (bytes <= 0)
        {
            return "0.0 MB";
        }
        double mb = bytes / (1024.0 * 1024.0);
        return $"{mb:F1} MB";
    }
}
