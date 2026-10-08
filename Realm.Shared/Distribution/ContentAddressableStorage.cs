using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Realm.Shared.Metadata;

namespace Realm.Shared.Distribution;

public class ContentAddressableStorage
{
    public const long MaximumAssetSizeBytes = 15 * 1024 * 1024;
    private readonly string _rootDirectory;
    private readonly string _assetsDirectory;
    private readonly string _sidecarCacheDirectory;
    private readonly ConcurrentDictionary<string, object> _fileLocks = new();
    private readonly ConcurrentDictionary<string, string> _assetPathCache = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, bool> _ensuredDirectories = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, string> _sidecarMemoryCache = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, bool> _writtenSidecars = new(StringComparer.OrdinalIgnoreCase);
    private long _lastDiskCheckTicks = 0;
    private double _lastDiskCheckPercentage = 1.0;

    public string RootDirectory => _rootDirectory;
    public string AssetsDirectory => _assetsDirectory;
    public string SidecarCacheDirectory => _sidecarCacheDirectory;

    public ContentAddressableStorage(string rootDirectory)
    {
        _rootDirectory = Path.GetFullPath(rootDirectory);
        _assetsDirectory = Path.Combine(_rootDirectory, "assets");
        _sidecarCacheDirectory = Path.Combine(_rootDirectory, ".sidecarcache");

        Directory.CreateDirectory(_assetsDirectory);
        Directory.CreateDirectory(_sidecarCacheDirectory);
    }

    public static string NormalizeBlake3Hash(string hashOrFileName)
    {
        string fileName = Path.GetFileName(hashOrFileName);
        int dotIndex = fileName.IndexOf('.');
        return (dotIndex >= 0 ? fileName.Substring(0, dotIndex) : fileName).Trim().ToLowerInvariant();
    }

    public string? FindAssetFilePath(string blake3Hash)
    {
        string normalizedHash = NormalizeBlake3Hash(blake3Hash);
        if (normalizedHash.Length < 2)
        {
            return null;
        }

        if (_assetPathCache.TryGetValue(normalizedHash, out string? cachedPath))
        {
            return string.IsNullOrEmpty(cachedPath) ? null : cachedPath;
        }

        string shard = normalizedHash.Substring(0, 2);
        string shardDirectory = Path.Combine(_assetsDirectory, shard);
        if (!Directory.Exists(shardDirectory))
        {
            _assetPathCache[normalizedHash] = string.Empty;
            return null;
        }

        ReadOnlySpan<string> commonExtensions = [".bin", ".rmesh", ".ranim", ".rtex", ".raud", ".png", ".wasm", ".json", ".rkey", ".glb", ".ogg", ".wav", ".mp3", ".jpg", ".jpeg", ".webp"];
        foreach (var ext in commonExtensions)
        {
            string candidatePath = Path.Combine(shardDirectory, $"{normalizedHash}{ext}");
            if (File.Exists(candidatePath))
            {
                _assetPathCache[normalizedHash] = candidatePath;
                return candidatePath;
            }
        }

        string[] matchingFiles = Directory.GetFiles(shardDirectory, $"{normalizedHash}*");
        if (matchingFiles.Length > 0)
        {
            string foundPath = matchingFiles[0];
            _assetPathCache[normalizedHash] = foundPath;
            return foundPath;
        }

        _assetPathCache[normalizedHash] = string.Empty;
        return null;
    }

    public bool HasAsset(string blake3Hash)
    {
        return FindAssetFilePath(blake3Hash) != null;
    }

    public byte[]? GetAssetBytes(string blake3Hash)
    {
        string? filePath = FindAssetFilePath(blake3Hash);
        if (filePath == null || !File.Exists(filePath))
        {
            return null;
        }

        string normalizedHash = NormalizeBlake3Hash(blake3Hash);
        object fileLock = _fileLocks.GetOrAdd(normalizedHash, _ => new object());

        lock (fileLock)
        {
            return File.ReadAllBytes(filePath);
        }
    }

    public Stream? OpenAssetReadStream(string blake3Hash)
    {
        string? filePath = FindAssetFilePath(blake3Hash);
        if (filePath == null || !File.Exists(filePath))
        {
            return null;
        }

        return new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
    }

    public string? GetAssetMetadata(string blake3Hash)
    {
        string normalizedHash = NormalizeBlake3Hash(blake3Hash);
        if (_sidecarMemoryCache.TryGetValue(normalizedHash, out var cachedMeta))
        {
            return string.IsNullOrEmpty(cachedMeta) ? null : cachedMeta;
        }

        string sidecarPath = GetSidecarCachePath(normalizedHash);
        if (File.Exists(sidecarPath))
        {
            try
            {
                string text = File.ReadAllText(sidecarPath);
                _sidecarMemoryCache[normalizedHash] = text;
                _writtenSidecars[normalizedHash] = true;
                return text;
            }
            catch
            {
            }
        }

        string? filePath = FindAssetFilePath(normalizedHash);
        if (filePath == null)
        {
            _sidecarMemoryCache[normalizedHash] = string.Empty;
            return null;
        }

        string? extractedMetadata = RealmMetadataHelper.ExtractMetadata(filePath);
        if (!string.IsNullOrWhiteSpace(extractedMetadata))
        {
            UpdateSidecarCache(normalizedHash, extractedMetadata);
        }
        else
        {
            _sidecarMemoryCache[normalizedHash] = string.Empty;
        }

        return extractedMetadata;
    }

    public double GetFreeDiskSpacePercentage()
    {
        long now = Environment.TickCount64;
        if (now - _lastDiskCheckTicks < 5000)
        {
            return _lastDiskCheckPercentage;
        }

        try
        {
            string rootPath = Path.GetPathRoot(_rootDirectory) ?? _rootDirectory;
            var driveInfo = new DriveInfo(rootPath);
            if (driveInfo.TotalSize <= 0)
            {
                _lastDiskCheckPercentage = 1.0;
            }
            else
            {
                _lastDiskCheckPercentage = (double)driveInfo.AvailableFreeSpace / driveInfo.TotalSize;
            }
            _lastDiskCheckTicks = now;
            return _lastDiskCheckPercentage;
        }
        catch
        {
            _lastDiskCheckPercentage = 1.0;
            _lastDiskCheckTicks = now;
            return 1.0;
        }
    }

    public bool CheckFreeDiskSpace(double minimumFreePercentage = 0.10)
    {
        return GetFreeDiskSpacePercentage() >= minimumFreePercentage;
    }

    public bool CheckFreeDiskSpaceAcceptingUploads()
    {
        return CheckFreeDiskSpace(0.10);
    }

    public bool CheckFreeDiskSpaceAcceptingDownloads()
    {
        return CheckFreeDiskSpace(0.01);
    }

    public (bool Success, string Message, bool Deduplicated, bool Merged, string Blake3Hash) StoreAsset(
        Stream assetStream,
        string? fileExtensionOrPath,
        string? metadataHeadersJson = null,
        string? authorPublicKey = null,
        string? authorSignature = null,
        string? precomputedBlake3 = null)
    {
        if (assetStream == null)
        {
            return (false, "Empty asset stream payload.", false, false, string.Empty);
        }

        string extension = Path.GetExtension(fileExtensionOrPath ?? string.Empty).ToLowerInvariant();
        string normalizedHash = !string.IsNullOrEmpty(precomputedBlake3) ? NormalizeBlake3Hash(precomputedBlake3) : string.Empty;

        if (!string.IsNullOrEmpty(normalizedHash))
        {
            object fileLock = _fileLocks.GetOrAdd(normalizedHash, _ => new object());
            lock (fileLock)
            {
                string? existingFilePath = FindAssetFilePath(normalizedHash);
                if (existingFilePath != null)
                {
                    bool merged = false;
                    if (!string.IsNullOrWhiteSpace(metadataHeadersJson))
                    {
                        merged = UpdateExistingAssetHeaders(existingFilePath, normalizedHash, metadataHeadersJson, authorPublicKey, authorSignature);
                    }

                    _assetPathCache[normalizedHash] = existingFilePath;
                    return (true, "Asset already exists (deduplicated).", true, merged, normalizedHash);
                }
            }
        }

        if (!CheckFreeDiskSpaceAcceptingDownloads())
        {
            return (false, "Write rejected: available disk space is less than 1%.", false, false, normalizedHash);
        }

        string finalExtension = !string.IsNullOrEmpty(extension) ? extension : ".bin";

        if (!string.IsNullOrEmpty(normalizedHash))
        {
            object fileLock = _fileLocks.GetOrAdd(normalizedHash, _ => new object());
            lock (fileLock)
            {
                string shard = normalizedHash.Substring(0, 2);
                string shardDirectory = Path.Combine(_assetsDirectory, shard);
                if (!_ensuredDirectories.ContainsKey(shardDirectory))
                {
                    if (!Directory.Exists(shardDirectory))
                    {
                        Directory.CreateDirectory(shardDirectory);
                    }
                    _ensuredDirectories[shardDirectory] = true;
                }

                string finalFilePath = Path.Combine(shardDirectory, $"{normalizedHash}{finalExtension}");

                using (var outStream = new FileStream(finalFilePath, FileMode.Create, FileAccess.Write, FileShare.None, 81920))
                {
                    assetStream.CopyTo(outStream, 81920);
                }

                _assetPathCache[normalizedHash] = finalFilePath;

                string? metadataToEmbed = metadataHeadersJson;
                if (!string.IsNullOrWhiteSpace(authorPublicKey) && !string.IsNullOrWhiteSpace(authorSignature))
                {
                    metadataToEmbed = InjectAuthorKeysIntoMetadata(metadataToEmbed, authorPublicKey, authorSignature);
                }

                string? finalMetadata = metadataToEmbed;
                if (finalMetadata == null && (extension is ".rmesh" or ".ranim" or ".rtex" or ".raud" or ".rkey"))
                {
                    finalMetadata = RealmMetadataHelper.ExtractMetadata(finalFilePath);
                }
                if (!string.IsNullOrWhiteSpace(finalMetadata))
                {
                    UpdateSidecarCache(normalizedHash, finalMetadata);
                }
                else
                {
                    _sidecarMemoryCache[normalizedHash] = string.Empty;
                }

                return (true, "Asset stored successfully.", false, false, normalizedHash);
            }
        }
        else
        {
            string tempFilePath = Path.Combine(_rootDirectory, $"temp_{Guid.NewGuid():N}.tmp");
            try
            {
                using (var outStream = new FileStream(tempFilePath, FileMode.Create, FileAccess.Write, FileShare.None, 81920))
                {
                    assetStream.CopyTo(outStream, 81920);
                }

                string computedBlake3 = RealmMetadataHelper.ComputeBlake3(tempFilePath);
                normalizedHash = NormalizeBlake3Hash(computedBlake3);

                object fileLock = _fileLocks.GetOrAdd(normalizedHash, _ => new object());
                lock (fileLock)
                {
                    string? existingFilePath = FindAssetFilePath(normalizedHash);
                    if (existingFilePath != null)
                    {
                        try { File.Delete(tempFilePath); } catch { }
                        bool merged = false;
                        if (!string.IsNullOrWhiteSpace(metadataHeadersJson))
                        {
                            merged = UpdateExistingAssetHeaders(existingFilePath, normalizedHash, metadataHeadersJson, authorPublicKey, authorSignature);
                        }

                        _assetPathCache[normalizedHash] = existingFilePath;
                        return (true, "Asset already exists (deduplicated).", true, merged, normalizedHash);
                    }

                    string shard = normalizedHash.Substring(0, 2);
                    string shardDirectory = Path.Combine(_assetsDirectory, shard);
                    if (!_ensuredDirectories.ContainsKey(shardDirectory))
                    {
                        if (!Directory.Exists(shardDirectory))
                        {
                            Directory.CreateDirectory(shardDirectory);
                        }
                        _ensuredDirectories[shardDirectory] = true;
                    }

                    string finalFilePath = Path.Combine(shardDirectory, $"{normalizedHash}{finalExtension}");
                    File.Move(tempFilePath, finalFilePath, overwrite: true);
                    _assetPathCache[normalizedHash] = finalFilePath;

                    string? metadataToEmbed = metadataHeadersJson;
                    if (!string.IsNullOrWhiteSpace(authorPublicKey) && !string.IsNullOrWhiteSpace(authorSignature))
                    {
                        metadataToEmbed = InjectAuthorKeysIntoMetadata(metadataToEmbed, authorPublicKey, authorSignature);
                    }

                    string? finalMetadata = metadataToEmbed;
                    if (finalMetadata == null && (extension is ".rmesh" or ".ranim" or ".rtex" or ".raud" or ".rkey"))
                    {
                        finalMetadata = RealmMetadataHelper.ExtractMetadata(finalFilePath);
                    }
                    if (!string.IsNullOrWhiteSpace(finalMetadata))
                    {
                        UpdateSidecarCache(normalizedHash, finalMetadata);
                    }
                    else
                    {
                        _sidecarMemoryCache[normalizedHash] = string.Empty;
                    }

                    return (true, "Asset stored successfully.", false, false, normalizedHash);
                }
            }
            finally
            {
                if (File.Exists(tempFilePath))
                {
                    try { File.Delete(tempFilePath); } catch { }
                }
            }
        }
    }

    public (bool Success, string Message, bool Deduplicated, bool Merged, string Blake3Hash) StoreAssetFromFile(
        string sourceFilePath,
        string precomputedBlake3,
        string? metadataHeadersJson = null,
        string? authorPublicKey = null,
        string? authorSignature = null)
    {
        if (string.IsNullOrWhiteSpace(sourceFilePath) || !File.Exists(sourceFilePath))
        {
            return (false, "Source file does not exist.", false, false, string.Empty);
        }

        string extension = Path.GetExtension(sourceFilePath).ToLowerInvariant();
        string normalizedHash = NormalizeBlake3Hash(precomputedBlake3);

        if (string.IsNullOrEmpty(normalizedHash))
        {
            return (false, "Precomputed hash required.", false, false, string.Empty);
        }

        object fileLock = _fileLocks.GetOrAdd(normalizedHash, _ => new object());
        lock (fileLock)
        {
            var existingResult = TryHandleExistingAsset(normalizedHash, metadataHeadersJson, authorPublicKey, authorSignature);
            if (existingResult.HasValue) return existingResult.Value;

            if (!CheckFreeDiskSpaceAcceptingDownloads())
            {
                return (false, "Write rejected: available disk space is less than 1%.", false, false, normalizedHash);
            }

            string finalExtension = !string.IsNullOrEmpty(extension) ? extension : ".bin";
            string shardDirectory = GetAndEnsureShardDirectory(normalizedHash);
            string finalFilePath = Path.Combine(shardDirectory, $"{normalizedHash}{finalExtension}");

            HardLinkHelper.CreateHardLinkOrCopy(finalFilePath, sourceFilePath, overwrite: true);
            _assetPathCache[normalizedHash] = finalFilePath;

            FinalizeAssetMetadata(finalFilePath, normalizedHash, extension, metadataHeadersJson, authorPublicKey, authorSignature);

            return (true, "Asset stored successfully.", false, false, normalizedHash);
        }
    }

    public (bool Success, string Message, bool Deduplicated, bool Merged, string Blake3Hash) StoreAsset(
        byte[] assetBytes,
        string? fileExtensionOrPath,
        string? metadataHeadersJson = null,
        string? authorPublicKey = null,
        string? authorSignature = null,
        string? precomputedBlake3 = null)
    {
        if (assetBytes == null || assetBytes.Length == 0)
        {
            return (false, "Empty asset payload.", false, false, string.Empty);
        }

        if (assetBytes.Length > MaximumAssetSizeBytes)
        {
            return (false, $"Asset exceeds maximum size limit of {MaximumAssetSizeBytes} bytes.", false, false, string.Empty);
        }

        string extension = Path.GetExtension(fileExtensionOrPath ?? string.Empty).ToLowerInvariant();
        string canonicalBlake3 = !string.IsNullOrEmpty(precomputedBlake3)
            ? precomputedBlake3
            : RealmMetadataHelper.ComputeBlake3(assetBytes, extension);
        string normalizedHash = NormalizeBlake3Hash(canonicalBlake3);

        object fileLock = _fileLocks.GetOrAdd(normalizedHash, _ => new object());

        lock (fileLock)
        {
            var existingResult = TryHandleExistingAsset(normalizedHash, metadataHeadersJson, authorPublicKey, authorSignature);
            if (existingResult.HasValue) return existingResult.Value;

            if (!CheckFreeDiskSpaceAcceptingDownloads())
            {
                return (false, "Write rejected: available disk space is less than 1%.", false, false, normalizedHash);
            }

            string finalExtension = !string.IsNullOrEmpty(extension) ? extension : ".bin";
            string shardDirectory = GetAndEnsureShardDirectory(normalizedHash);
            string finalFilePath = Path.Combine(shardDirectory, $"{normalizedHash}{finalExtension}");

            File.WriteAllBytes(finalFilePath, assetBytes);
            _assetPathCache[normalizedHash] = finalFilePath;

            FinalizeAssetMetadata(finalFilePath, normalizedHash, extension, metadataHeadersJson, authorPublicKey, authorSignature);

            return (true, "Asset stored successfully.", false, false, normalizedHash);
        }
    }

    private bool UpdateExistingAssetHeaders(
        string existingFilePath,
        string normalizedHash,
        string incomingMetadataJson,
        string? authorPublicKey,
        string? authorSignature)
    {
        try
        {
            string? existingMetadata = GetAssetMetadata(normalizedHash);
            bool isAuthorized = IsAuthorizedToUpdateHeaders(existingMetadata, normalizedHash, authorPublicKey, authorSignature);

            string incomingWithKeys = InjectAuthorKeysIntoMetadata(incomingMetadataJson, authorPublicKey, authorSignature);
            string mergedMetadata = AuthorSignatureHelper.MergeMetadataHeaders(existingMetadata, incomingWithKeys, isAuthorized);

            UpdateSidecarCache(normalizedHash, mergedMetadata);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private bool IsAuthorizedToUpdateHeaders(string? existingMetadata, string normalizedHash, string? authorPublicKey, string? authorSignature)
    {
        if (string.IsNullOrWhiteSpace(authorPublicKey) || string.IsNullOrWhiteSpace(authorSignature))
        {
            return false;
        }

        bool signatureValid = AuthorSignatureHelper.VerifySignature(authorPublicKey, normalizedHash, authorSignature);
        if (!signatureValid)
        {
            return false;
        }

        string? existingAuthorKey = ExtractAuthorPublicKey(existingMetadata);
        return string.IsNullOrEmpty(existingAuthorKey) || string.Equals(existingAuthorKey, authorPublicKey, StringComparison.OrdinalIgnoreCase);
    }

    private static string? ExtractAuthorPublicKey(string? metadataJson)
    {
        if (string.IsNullOrWhiteSpace(metadataJson))
        {
            return null;
        }

        try
        {
            using var document = JsonDocument.Parse(metadataJson);
            if (document.RootElement.TryGetProperty("AuthorPublicKey", out var property))
            {
                return property.GetString();
            }
            if (document.RootElement.TryGetProperty("author_public_key", out var snakeProperty))
            {
                return snakeProperty.GetString();
            }
        }
        catch
        {
        }

        return null;
    }

    private static string InjectAuthorKeysIntoMetadata(string? metadataJson, string? authorPublicKey, string? authorSignature)
    {
        JsonObject jsonObject;
        if (!string.IsNullOrWhiteSpace(metadataJson))
        {
            try
            {
                jsonObject = JsonNode.Parse(metadataJson)?.AsObject() ?? new JsonObject();
            }
            catch
            {
                jsonObject = new JsonObject();
            }
        }
        else
        {
            jsonObject = new JsonObject();
        }

        if (!string.IsNullOrWhiteSpace(authorPublicKey))
        {
            jsonObject["AuthorPublicKey"] = authorPublicKey;
        }

        if (!string.IsNullOrWhiteSpace(authorSignature))
        {
            jsonObject["AuthorSignature"] = authorSignature;
        }

        return jsonObject.ToJsonString();
    }

    private string GetSidecarCachePath(string normalizedHash)
    {
        string shard = normalizedHash.Substring(0, 2);
        string shardDirectory = Path.Combine(_sidecarCacheDirectory, shard);
        if (!_ensuredDirectories.ContainsKey(shardDirectory))
        {
            if (!Directory.Exists(shardDirectory))
            {
                Directory.CreateDirectory(shardDirectory);
            }
            _ensuredDirectories[shardDirectory] = true;
        }
        return Path.Combine(shardDirectory, $"{normalizedHash}.json");
    }

    public void UpdateSidecarCache(string normalizedHash, string metadataJson)
    {
        try
        {
            _sidecarMemoryCache[normalizedHash] = metadataJson;
            if (!_writtenSidecars.ContainsKey(normalizedHash))
            {
                string path = GetSidecarCachePath(normalizedHash);
                if (!File.Exists(path))
                {
                    File.WriteAllText(path, metadataJson, Encoding.UTF8);
                }
                _writtenSidecars[normalizedHash] = true;
            }
        }
        catch
        {
        }
    }

    public void RebuildSidecarCache()
    {
        if (!Directory.Exists(_assetsDirectory))
        {
            return;
        }

        string[] files = Directory.GetFiles(_assetsDirectory, "*.*", SearchOption.AllDirectories);
        foreach (string file in files)
        {
            string normalizedHash = NormalizeBlake3Hash(Path.GetFileName(file));
            string? metadata = RealmMetadataHelper.ExtractMetadata(file);
            if (!string.IsNullOrWhiteSpace(metadata))
            {
                UpdateSidecarCache(normalizedHash, metadata);
            }
        }
    }

    public void DeleteSidecarCache()
    {
        _writtenSidecars.Clear();
        _sidecarMemoryCache.Clear();
        if (Directory.Exists(_sidecarCacheDirectory))
        {
            Directory.Delete(_sidecarCacheDirectory, true);
            Directory.CreateDirectory(_sidecarCacheDirectory);
        }
    }

    public void RemoveSidecarCache(string normalizedHash)
    {
        try
        {
            string cleanHash = NormalizeBlake3Hash(normalizedHash);
            _writtenSidecars.TryRemove(cleanHash, out _);
            _sidecarMemoryCache.TryRemove(cleanHash, out _);
            _assetPathCache.TryRemove(cleanHash, out _);
            if (cleanHash.Length >= 2 && Directory.Exists(_sidecarCacheDirectory))
            {
                string shardDirectory = Path.Combine(_sidecarCacheDirectory, cleanHash.Substring(0, 2));
                string sidecarPath = Path.Combine(shardDirectory, $"{cleanHash}.json");
                if (File.Exists(sidecarPath))
                {
                    File.Delete(sidecarPath);
                }
            }
        }
        catch
        {
        }
    }

    public HashSet<string> GetAllStoredHashes()
    {
        var hashes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (!Directory.Exists(_assetsDirectory))
        {
            return hashes;
        }

        string[] files = Directory.GetFiles(_assetsDirectory, "*.*", SearchOption.AllDirectories);
        foreach (string file in files)
        {
            string normalizedHash = NormalizeBlake3Hash(Path.GetFileName(file));
            if (normalizedHash.Length == 64)
            {
                hashes.Add(normalizedHash);
            }
        }

        return hashes;
    }

    public long GetTotalUsedBytes()
    {
        if (!Directory.Exists(_rootDirectory))
        {
            return 0;
        }

        long totalBytes = 0;
        try
        {
            var directoryInfo = new DirectoryInfo(_rootDirectory);
            foreach (var file in directoryInfo.EnumerateFiles("*", SearchOption.AllDirectories))
            {
                totalBytes += GetFileLengthSafe(file);
            }
        }
        catch
        {
        }

        return totalBytes;
    }

    private long GetFileLengthSafe(FileInfo fileInfo)
    {
        try
        {
            return fileInfo.Length;
        }
        catch
        {
            return 0;
        }
    }

    public static string FormatByteSize(long bytes)
    {
        if (bytes < 1024) return $"{bytes} B";
        if (bytes < 1024 * 1024) return $"{bytes / 1024.0:F1} KB";
        if (bytes < 1024 * 1024 * 1024) return $"{bytes / (1024.0 * 1024.0):F1} MB";
        return $"{bytes / (1024.0 * 1024.0 * 1024.0):F2} GB";
    }

    private string GetAndEnsureShardDirectory(string normalizedHash)
    {
        string shard = normalizedHash.Substring(0, 2);
        string shardDirectory = Path.Combine(_assetsDirectory, shard);
        if (!_ensuredDirectories.ContainsKey(shardDirectory))
        {
            if (!Directory.Exists(shardDirectory))
            {
                Directory.CreateDirectory(shardDirectory);
            }
            _ensuredDirectories[shardDirectory] = true;
        }
        return shardDirectory;
    }

    private void FinalizeAssetMetadata(
        string finalFilePath, 
        string normalizedHash, 
        string extension, 
        string? metadataHeadersJson, 
        string? authorPublicKey, 
        string? authorSignature)
    {
        string? metadataToEmbed = metadataHeadersJson;
        if (!string.IsNullOrWhiteSpace(authorPublicKey) && !string.IsNullOrWhiteSpace(authorSignature))
        {
            metadataToEmbed = InjectAuthorKeysIntoMetadata(metadataToEmbed, authorPublicKey, authorSignature);
        }

        string? finalMetadata = metadataToEmbed;
        if (finalMetadata == null && (extension is ".rmesh" or ".ranim" or ".rtex" or ".raud" or ".rkey"))
        {
            finalMetadata = RealmMetadataHelper.ExtractMetadata(finalFilePath);
        }
        
        if (!string.IsNullOrWhiteSpace(finalMetadata))
        {
            UpdateSidecarCache(normalizedHash, finalMetadata);
        }
        else
        {
            _sidecarMemoryCache[normalizedHash] = string.Empty;
        }
    }

    private (bool Success, string Message, bool Deduplicated, bool Merged, string Blake3Hash)? TryHandleExistingAsset(
        string normalizedHash, 
        string? metadataHeadersJson, 
        string? authorPublicKey, 
        string? authorSignature)
    {
        string? existingFilePath = FindAssetFilePath(normalizedHash);
        if (existingFilePath == null)
        {
            return null;
        }

        bool merged = false;
        if (!string.IsNullOrWhiteSpace(metadataHeadersJson))
        {
            merged = UpdateExistingAssetHeaders(existingFilePath, normalizedHash, metadataHeadersJson, authorPublicKey, authorSignature);
        }

        _assetPathCache[normalizedHash] = existingFilePath;
        return (true, "Asset already exists (deduplicated).", true, merged, normalizedHash);
    }
}
