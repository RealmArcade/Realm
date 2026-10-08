using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Realm.Shared.Metadata;

namespace Realm.Shared.Distribution;

public class DistributionClient
{
    private readonly HttpClient _httpClient;
    private readonly string _registryServerUrl;
    private readonly TokenBucketThrottle? _throttle;

    public DistributionClient(string? registryServerUrl = null, HttpClient? httpClient = null, TokenBucketThrottle? throttle = null)
    {
        _registryServerUrl = (string.IsNullOrWhiteSpace(registryServerUrl) ? ServersConfigHelper.GetDefaultServerUrl() : registryServerUrl).TrimEnd('/');
        _httpClient = httpClient ?? new HttpClient();
        _throttle = throttle;
    }

    public async Task<List<SeederNodeDto>> GetActiveSeedersAsync(CancellationToken cancellationToken = default)
    {
        string url = $"{_registryServerUrl}/api/seeders";
        try
        {
            var response = await _httpClient.GetAsync(url, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                return new List<SeederNodeDto>();
            }

            string json = await response.Content.ReadAsStringAsync(cancellationToken);
            var seeders = JsonSerializer.Deserialize<List<SeederNodeDto>>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            return seeders ?? new List<SeederNodeDto>();
        }
        catch
        {
            return new List<SeederNodeDto>();
        }
    }

    public async Task<MapPublishResponseDto> PublishManifestAsync(
        MapManifest manifest,
        string? adminBypassToken = null,
        CancellationToken cancellationToken = default)
    {
        string url = $"{_registryServerUrl}/api/manifests";
        var request = new HttpRequestMessage(HttpMethod.Post, url)
        {
            Content = new StringContent(manifest.ToJson(), Encoding.UTF8, "application/json")
        };

        if (!string.IsNullOrWhiteSpace(adminBypassToken))
        {
            request.Headers.Add("X-Admin-Bypass", adminBypassToken);
        }

        try
        {
            var response = await _httpClient.SendAsync(request, cancellationToken);
            string responseJson = await response.Content.ReadAsStringAsync(cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                return new MapPublishResponseDto
                {
                    Success = false,
                    Status = "Failed",
                    Message = $"Publish failed with HTTP {(int)response.StatusCode}: {responseJson}"
                };
            }

            var publishResponse = JsonSerializer.Deserialize<MapPublishResponseDto>(responseJson, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            return publishResponse ?? new MapPublishResponseDto { Success = true, Status = "Published" };
        }
        catch (Exception exception)
        {
            return new MapPublishResponseDto
            {
                Success = false,
                Status = "Error",
                Message = exception.Message
            };
        }
    }

    public async Task<AssetUploadResponseDto> UploadAssetAsync(
        string targetServerBaseUrl,
        byte[] assetBytes,
        string extensionOrPath,
        string? metadataHeadersJson = null,
        string? authorPublicKey = null,
        string? authorSignature = null,
        CancellationToken cancellationToken = default)
    {
        string extension = Path.GetExtension(extensionOrPath).ToLowerInvariant();
        string canonicalBlake3 = RealmMetadataHelper.ComputeBlake3(assetBytes, extension);
        string normalizedHash = ContentAddressableStorage.NormalizeBlake3Hash(canonicalBlake3);

        if (assetBytes.Length > ContentAddressableStorage.MaximumAssetSizeBytes)
        {
            double sizeMb = (double)assetBytes.Length / (1024 * 1024);
            double maxMb = (double)ContentAddressableStorage.MaximumAssetSizeBytes / (1024 * 1024);
            return new AssetUploadResponseDto
            {
                Success = false,
                Message = $"Upload rejected: Asset size ({sizeMb:F2} MB) exceeds maximum allowed size of {maxMb:F0} MB per asset.",
                Blake3Hash = normalizedHash
            };
        }

        string url = $"{targetServerBaseUrl.TrimEnd('/')}/api/assets/{normalizedHash}";

        using var content = new MultipartFormDataContent();
        var byteContent = new ByteArrayContent(assetBytes);
        byteContent.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
        content.Add(byteContent, "file", $"{normalizedHash}{extension}");

        AddUploadMultipartContent(content, metadataHeadersJson, authorPublicKey, authorSignature);

        using var request = new HttpRequestMessage(HttpMethod.Post, url);
        request.Content = content;

        AddUploadHeaders(request, extension, metadataHeadersJson, authorPublicKey, authorSignature);

        if (_throttle != null)
        {
            await _throttle.ConsumeAsync(assetBytes.Length, cancellationToken);
        }

        var response = await _httpClient.SendAsync(request, cancellationToken);
        string responseJson = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            return new AssetUploadResponseDto
            {
                Success = false,
                Message = $"Upload rejected: HTTP {(int)response.StatusCode} - {responseJson}",
                Blake3Hash = normalizedHash
            };
        }

        try
        {
            var result = JsonSerializer.Deserialize<AssetUploadResponseDto>(responseJson, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            return result ?? new AssetUploadResponseDto { Success = true, Blake3Hash = normalizedHash };
        }
        catch
        {
            return new AssetUploadResponseDto { Success = true, Blake3Hash = normalizedHash };
        }
    }

    private static void AddUploadMultipartContent(MultipartFormDataContent content, string? metadataHeadersJson, string? authorPublicKey, string? authorSignature)
    {
        if (!string.IsNullOrWhiteSpace(metadataHeadersJson))
        {
            content.Add(new StringContent(metadataHeadersJson, Encoding.UTF8), "metadata");
        }

        if (!string.IsNullOrWhiteSpace(authorPublicKey))
        {
            content.Add(new StringContent(authorPublicKey, Encoding.UTF8), "authorPublicKey");
        }

        if (!string.IsNullOrWhiteSpace(authorSignature))
        {
            content.Add(new StringContent(authorSignature, Encoding.UTF8), "authorSignature");
        }
    }

    private static void AddUploadHeaders(HttpRequestMessage request, string extension, string? metadataHeadersJson, string? authorPublicKey, string? authorSignature)
    {
        if (!string.IsNullOrEmpty(extension))
        {
            request.Headers.TryAddWithoutValidation("X-File-Extension", extension);
        }

        if (!string.IsNullOrWhiteSpace(metadataHeadersJson))
        {
            request.Headers.TryAddWithoutValidation("X-Asset-Metadata", Convert.ToBase64String(Encoding.UTF8.GetBytes(metadataHeadersJson)));
        }

        if (!string.IsNullOrWhiteSpace(authorPublicKey))
        {
            request.Headers.TryAddWithoutValidation("X-Author-Public-Key", authorPublicKey);
        }

        if (!string.IsNullOrWhiteSpace(authorSignature))
        {
            request.Headers.TryAddWithoutValidation("X-Author-Signature", authorSignature);
        }
    }

    /// <summary>
    /// Downloads a batched bundle of assets over HTTP in a single Zstandard-compressed response stream.
    /// </summary>
    public async Task<(bool Success, List<string> DownloadedHashes)> DownloadAssetBundleStreamAsync(
        string targetServerBaseUrl,
        List<string> requestedHashes,
        IReadOnlyDictionary<string, string> hashToExtensionMap,
        ContentAddressableStorage targetStorage,
        CancellationToken cancellationToken = default,
        Action<string>? onAssetDownloaded = null)
    {
        var downloadedHashes = new List<string>();
        if (requestedHashes.Count == 0)
        {
            return (true, downloadedHashes);
        }

        string url = $"{targetServerBaseUrl.TrimEnd('/')}/api/assets/bundle";
        var payload = new AssetBundleRequestDto { Hashes = requestedHashes };
        string jsonPayload = JsonSerializer.Serialize(payload);

        using var request = new HttpRequestMessage(HttpMethod.Post, url)
        {
            Content = new StringContent(jsonPayload, Encoding.UTF8, "application/json")
        };

        try
        {
            using var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                return (false, downloadedHashes);
            }

            using var responseStream = await response.Content.ReadAsStreamAsync(cancellationToken);

            await ZstdAssetBundleHelper.ExtractBundleFromStreamAsync(
                responseStream,
                async (assetKey, metadata, data) => await ProcessExtractedBundleAssetAsync(assetKey, metadata, data, hashToExtensionMap, targetStorage, downloadedHashes, onAssetDownloaded, cancellationToken),
                cancellationToken);

            return (true, downloadedHashes);
        }
        catch (Exception)
        {
            return (false, downloadedHashes);
        }
    }

    private async Task ProcessExtractedBundleAssetAsync(
        string assetKey,
        string? metadata,
        byte[] data,
        IReadOnlyDictionary<string, string> hashToExtensionMap,
        ContentAddressableStorage targetStorage,
        List<string> downloadedHashes,
        Action<string>? onAssetDownloaded,
        CancellationToken cancellationToken)
    {
        string normalizedHash = ContentAddressableStorage.NormalizeBlake3Hash(assetKey);
        if (!hashToExtensionMap.TryGetValue(normalizedHash, out string? extension) || string.IsNullOrEmpty(extension))
        {
            extension = ".bin";
        }

        string computedBlake3 = RealmMetadataHelper.ComputeBlake3(data, extension);
        string computedNormalized = ContentAddressableStorage.NormalizeBlake3Hash(computedBlake3);

        if (!string.Equals(computedNormalized, normalizedHash, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        if (_throttle != null)
        {
            await _throttle.ConsumeAsync(data.Length, cancellationToken);
        }

        var storeResult = targetStorage.StoreAsset(data, extension, metadata, precomputedBlake3: computedBlake3);
        if (!storeResult.Success)
        {
            return;
        }

        lock (downloadedHashes)
        {
            downloadedHashes.Add(normalizedHash);
        }
        onAssetDownloaded?.Invoke(normalizedHash);
    }

    public async Task<bool> DownloadMissingAssetsMultiThreadedAsync(
        MapManifest manifest,
        ContentAddressableStorage targetStorage,
        List<SeederNodeDto>? availableSeeders = null,
        string? fallbackHostUrl = null,
        Action<float>? progressCallback = null,
        int maximumConcurrency = 4,
        CancellationToken cancellationToken = default,
        Action<string, string, string>? onAssetReady = null)
    {
        var (missingHashes, existingHashes) = CategorizeManifestAssets(manifest, targetStorage);

        NotifyExistingAssetsReady(existingHashes, onAssetReady);

        if (missingHashes.Count == 0)
        {
            progressCallback?.Invoke(1.0f);
            return true;
        }

        var seeders = availableSeeders ?? await GetActiveSeedersAsync(cancellationToken);
        int totalMissing = missingHashes.Count;
        int[] completedCountWrapper = new int[1];

        var (remainingMissingMap, hashToExtMap) = BuildMissingAssetMaps(missingHashes);

        string firstHash = missingHashes.Count > 0 ? missingHashes[0].NormalizedHash : "";
        var prioritizedUrls = GetPrioritizedServerUrls(firstHash, seeders, fallbackHostUrl);

        await DownloadBatchedAssetsFromSeedersAsync(
            prioritizedUrls,
            remainingMissingMap,
            hashToExtMap,
            targetStorage,
            totalMissing,
            progressCallback,
            onAssetReady,
            completedCountWrapper,
            cancellationToken);

        if (!remainingMissingMap.IsEmpty && !cancellationToken.IsCancellationRequested)
        {
            await DownloadRemainingAssetsConcurrentlyAsync(
                remainingMissingMap,
                hashToExtMap,
                targetStorage,
                seeders,
                fallbackHostUrl,
                maximumConcurrency,
                totalMissing,
                progressCallback,
                onAssetReady,
                completedCountWrapper,
                cancellationToken);
        }

        if (completedCountWrapper[0] >= totalMissing)
        {
            progressCallback?.Invoke(1.0f);
            return true;
        }

        return remainingMissingMap.IsEmpty;
    }

    private static (List<(string VirtualPath, string AssetKey, string NormalizedHash)> Missing, List<(string VirtualPath, string AssetKey, string NormalizedHash)> Existing) CategorizeManifestAssets(
        MapManifest manifest,
        ContentAddressableStorage targetStorage)
    {
        var missingHashes = new List<(string VirtualPath, string AssetKey, string NormalizedHash)>();
        var existingHashes = new List<(string VirtualPath, string AssetKey, string NormalizedHash)>();

        foreach (var filePair in manifest.Files)
        {
            string assetKey = filePair.Value;
            string normalizedHash = ContentAddressableStorage.NormalizeBlake3Hash(assetKey);

            if (targetStorage.HasAsset(normalizedHash))
            {
                existingHashes.Add((filePair.Key, assetKey, normalizedHash));
            }
            else
            {
                missingHashes.Add((filePair.Key, assetKey, normalizedHash));
            }
        }

        return (missingHashes, existingHashes);
    }

    private static void NotifyExistingAssetsReady(
        List<(string VirtualPath, string AssetKey, string NormalizedHash)> existingHashes,
        Action<string, string, string>? onAssetReady)
    {
        if (existingHashes.Count == 0 || onAssetReady == null)
        {
            return;
        }

        Parallel.ForEach(existingHashes, item =>
        {
            onAssetReady(item.VirtualPath, item.AssetKey, item.NormalizedHash);
        });
    }

    private static (ConcurrentDictionary<string, List<(string VirtualPath, string AssetKey, string NormalizedHash)>> RemainingMap, Dictionary<string, string> ExtMap) BuildMissingAssetMaps(
        List<(string VirtualPath, string AssetKey, string NormalizedHash)> missingHashes)
    {
        var remainingMissingMap = new ConcurrentDictionary<string, List<(string VirtualPath, string AssetKey, string NormalizedHash)>>(StringComparer.OrdinalIgnoreCase);
        var hashToExtMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var item in missingHashes)
        {
            string ext = Path.GetExtension(item.AssetKey).ToLowerInvariant();
            if (string.IsNullOrEmpty(ext))
            {
                ext = Path.GetExtension(item.VirtualPath).ToLowerInvariant();
            }
            hashToExtMap[item.NormalizedHash] = ext;

            if (!remainingMissingMap.TryGetValue(item.NormalizedHash, out var list))
            {
                list = new List<(string VirtualPath, string AssetKey, string NormalizedHash)>();
                remainingMissingMap[item.NormalizedHash] = list;
            }
            list.Add(item);
        }

        return (remainingMissingMap, hashToExtMap);
    }

    private async Task DownloadBatchedAssetsFromSeedersAsync(
        List<string> prioritizedUrls,
        ConcurrentDictionary<string, List<(string VirtualPath, string AssetKey, string NormalizedHash)>> remainingMissingMap,
        Dictionary<string, string> hashToExtMap,
        ContentAddressableStorage targetStorage,
        int totalMissing,
        Action<float>? progressCallback,
        Action<string, string, string>? onAssetReady,
        int[] completedCountWrapper,
        CancellationToken cancellationToken)
    {
        foreach (string baseUrl in prioritizedUrls)
        {
            if (remainingMissingMap.IsEmpty || cancellationToken.IsCancellationRequested)
            {
                break;
            }

            var circuit = GetCircuitState(baseUrl);
            if (circuit.IsOpen(DateTime.UtcNow))
            {
                continue;
            }

            await ProcessUrlBatchesAsync(
                baseUrl,
                remainingMissingMap,
                hashToExtMap,
                targetStorage,
                totalMissing,
                progressCallback,
                onAssetReady,
                completedCountWrapper,
                cancellationToken,
                circuit);
        }
    }

    private async Task ProcessUrlBatchesAsync(
        string baseUrl,
        ConcurrentDictionary<string, List<(string VirtualPath, string AssetKey, string NormalizedHash)>> remainingMissingMap,
        Dictionary<string, string> hashToExtMap,
        ContentAddressableStorage targetStorage,
        int totalMissing,
        Action<float>? progressCallback,
        Action<string, string, string>? onAssetReady,
        int[] completedCountWrapper,
        CancellationToken cancellationToken,
        SeederCircuitState circuit)
    {
        var currentMissingHashes = remainingMissingMap.Keys.ToList();
        if (currentMissingHashes.Count == 0) return;

        const int maxBatchCount = 100;
        for (int i = 0; i < currentMissingHashes.Count; i += maxBatchCount)
        {
            if (cancellationToken.IsCancellationRequested) break;

            var batch = currentMissingHashes.Skip(i).Take(maxBatchCount).ToList();
            var (bundleSuccess, downloadedHashes) = await DownloadAssetBundleStreamAsync(
                baseUrl,
                batch,
                hashToExtMap,
                targetStorage,
                cancellationToken,
                onAssetDownloaded: normHash => HandleAssetDownloaded(normHash, remainingMissingMap, onAssetReady, totalMissing, progressCallback, completedCountWrapper));

            if (bundleSuccess)
            {
                circuit.RecordSuccess();
            }
            else
            {
                break;
            }
        }
    }


    private void HandleAssetDownloaded(
        string normHash,
        ConcurrentDictionary<string, List<(string VirtualPath, string AssetKey, string NormalizedHash)>> remainingMissingMap,
        Action<string, string, string>? onAssetReady,
        int totalMissing,
        Action<float>? progressCallback,
        int[] completedCountWrapper)
    {
        if (remainingMissingMap.TryRemove(normHash, out var readyItems))
        {
            foreach (var item in readyItems)
            {
                onAssetReady?.Invoke(item.VirtualPath, item.AssetKey, item.NormalizedHash);
            }
            int currentCompleted = Interlocked.Increment(ref completedCountWrapper[0]);
            float progress = (float)currentCompleted / totalMissing;
            progressCallback?.Invoke(progress);
        }
    }

    private async Task DownloadRemainingAssetsConcurrentlyAsync(
        ConcurrentDictionary<string, List<(string VirtualPath, string AssetKey, string NormalizedHash)>> remainingMissingMap,
        Dictionary<string, string> hashToExtMap,
        ContentAddressableStorage targetStorage,
        List<SeederNodeDto> seeders,
        string? fallbackHostUrl,
        int maximumConcurrency,
        int totalMissing,
        Action<float>? progressCallback,
        Action<string, string, string>? onAssetReady,
        int[] completedCountWrapper,
        CancellationToken cancellationToken)
    {
        var remainingMissingItems = remainingMissingMap.Values.SelectMany(v => v).Distinct().ToList();

        using var semaphore = new SemaphoreSlim(maximumConcurrency);
        var downloadTasks = remainingMissingItems.Select(async item =>
        {
            await semaphore.WaitAsync(cancellationToken);
            try
            {
                return await DownloadSingleRemainingAssetAsync(
                    item,
                    remainingMissingMap,
                    hashToExtMap,
                    targetStorage,
                    seeders,
                    fallbackHostUrl,
                    totalMissing,
                    progressCallback,
                    onAssetReady,
                    completedCountWrapper,
                    cancellationToken);
            }
            finally
            {
                semaphore.Release();
            }
        }).ToList();

        await Task.WhenAll(downloadTasks);
    }

    private async Task<bool> DownloadSingleRemainingAssetAsync(
        (string VirtualPath, string AssetKey, string NormalizedHash) item,
        ConcurrentDictionary<string, List<(string VirtualPath, string AssetKey, string NormalizedHash)>> remainingMissingMap,
        Dictionary<string, string> hashToExtMap,
        ContentAddressableStorage targetStorage,
        List<SeederNodeDto> seeders,
        string? fallbackHostUrl,
        int totalMissing,
        Action<float>? progressCallback,
        Action<string, string, string>? onAssetReady,
        int[] completedCountWrapper,
        CancellationToken cancellationToken)
    {
        if (targetStorage.HasAsset(item.NormalizedHash))
        {
            HandleAssetDownloaded(item.NormalizedHash, remainingMissingMap, onAssetReady, totalMissing, progressCallback, completedCountWrapper);
            return true;
        }

        string effectiveAssetKey = item.AssetKey;
        if (string.IsNullOrEmpty(Path.GetExtension(effectiveAssetKey)) && hashToExtMap.TryGetValue(item.NormalizedHash, out var mappedExt) && !string.IsNullOrEmpty(mappedExt))
        {
            effectiveAssetKey = $"{item.AssetKey}{mappedExt}";
        }

        bool downloaded = await DownloadSingleAssetWithRetriesAsync(
            item.NormalizedHash,
            effectiveAssetKey,
            targetStorage,
            seeders,
            fallbackHostUrl,
            cancellationToken);

        if (downloaded)
        {
            HandleAssetDownloaded(item.NormalizedHash, remainingMissingMap, onAssetReady, totalMissing, progressCallback, completedCountWrapper);
        }

        return downloaded;
    }

    private int _roundRobinCounter = 0;
    private static readonly ConcurrentDictionary<string, SeederCircuitState> SeederCircuitBreakers = new(StringComparer.OrdinalIgnoreCase);

    public static SeederCircuitState GetCircuitState(string baseUrl)
    {
        return SeederCircuitBreakers.GetOrAdd(baseUrl.TrimEnd('/'), _ => new SeederCircuitState());
    }

    public static void ResetAllCircuitBreakers()
    {
        foreach (var pair in SeederCircuitBreakers)
        {
            pair.Value.Reset();
        }
    }

    private List<string> GetPrioritizedServerUrls(
        string normalizedHash,
        List<SeederNodeDto> seeders,
        string? fallbackHostUrl)
    {
        var candidateSeeders = seeders
            .Where(s => DistributionSharding.SeederAcceptsHash(s.SeederId, s.CapacityPercentage, normalizedHash))
            .ToList();

        var prioritizedUrls = new List<string>();

        if (candidateSeeders.Count > 0)
        {
            int startIndex = Interlocked.Increment(ref _roundRobinCounter) % candidateSeeders.Count;
            for (int i = 0; i < candidateSeeders.Count; i++)
            {
                int index = (startIndex + i) % candidateSeeders.Count;
                var seeder = candidateSeeders[index];
                prioritizedUrls.Add($"http://{seeder.IP}:{seeder.Port}");
            }
        }

        foreach (var seeder in seeders)
        {
            string url = $"http://{seeder.IP}:{seeder.Port}";
            if (!prioritizedUrls.Contains(url))
            {
                prioritizedUrls.Add(url);
            }
        }

        if (!string.IsNullOrEmpty(fallbackHostUrl) && !prioritizedUrls.Contains(fallbackHostUrl))
        {
            prioritizedUrls.Add(fallbackHostUrl.TrimEnd('/'));
        }

        if (!prioritizedUrls.Contains(_registryServerUrl))
        {
            prioritizedUrls.Add(_registryServerUrl);
        }

        return prioritizedUrls;
    }

    private async Task<bool> DownloadSingleAssetWithRetriesAsync(
        string normalizedHash,
        string assetKey,
        ContentAddressableStorage targetStorage,
        List<SeederNodeDto> seeders,
        string? fallbackHostUrl,
        CancellationToken cancellationToken)
    {
        var prioritizedUrls = GetPrioritizedServerUrls(normalizedHash, seeders, fallbackHostUrl);

        const int maxCycles = 3;
        for (int cycle = 0; cycle < maxCycles; cycle++)
        {
            if (cancellationToken.IsCancellationRequested) return false;

            if (cycle > 0)
            {
                await Task.Delay(150 * (int)Math.Pow(2, cycle - 1) + Random.Shared.Next(15, 75), cancellationToken);
            }

            var availableUrls = GetAvailableUrls(prioritizedUrls);

            if (await ProcessAvailableUrlsAsync(availableUrls, normalizedHash, assetKey, targetStorage, cancellationToken))
            {
                return true;
            }
        }

        return false;
    }

    private async Task<bool> ProcessAvailableUrlsAsync(
        List<string> availableUrls,
        string normalizedHash,
        string assetKey,
        ContentAddressableStorage targetStorage,
        CancellationToken cancellationToken)
    {
        foreach (string baseUrl in availableUrls)
        {
            if (cancellationToken.IsCancellationRequested) return false;
            
            if (await TryDownloadAssetFromUrlAsync(baseUrl, normalizedHash, assetKey, targetStorage, cancellationToken))
            {
                return true;
            }
        }
        return false;
    }

    private List<string> GetAvailableUrls(List<string> prioritizedUrls)
    {
        DateTime nowUtc = DateTime.UtcNow;
        var availableUrls = prioritizedUrls.Where(url => !GetCircuitState(url).IsOpen(nowUtc)).ToList();
        return availableUrls.Count == 0 ? prioritizedUrls : availableUrls;
    }

    private async Task<bool> TryDownloadAssetFromUrlAsync(
        string baseUrl,
        string normalizedHash,
        string assetKey,
        ContentAddressableStorage targetStorage,
        CancellationToken cancellationToken)
    {
        var circuit = GetCircuitState(baseUrl);
        try
        {
            string assetUrl = $"{baseUrl}/api/assets/{normalizedHash}";
            var response = await _httpClient.GetAsync(assetUrl, cancellationToken);
            
            if (response.IsSuccessStatusCode)
            {
                return await ProcessSuccessfulDownloadAsync(response, baseUrl, normalizedHash, assetKey, targetStorage, circuit, cancellationToken);
            }
            
            if ((int)response.StatusCode >= 500 || response.StatusCode == System.Net.HttpStatusCode.RequestTimeout)
            {
                circuit.RecordFailure(DateTime.UtcNow);
            }
            return false;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return false;
        }
        catch
        {
            circuit.RecordFailure(DateTime.UtcNow);
            return false;
        }
    }

    private async Task<bool> ProcessSuccessfulDownloadAsync(
        HttpResponseMessage response,
        string baseUrl,
        string normalizedHash,
        string assetKey,
        ContentAddressableStorage targetStorage,
        SeederCircuitState circuit,
        CancellationToken cancellationToken)
    {
        byte[] downloadedBytes = await response.Content.ReadAsByteArrayAsync(cancellationToken);
        if (_throttle != null)
        {
            await _throttle.ConsumeAsync(downloadedBytes.Length, cancellationToken);
        }

        string extension = Path.GetExtension(assetKey).ToLowerInvariant();
        string computedBlake3 = RealmMetadataHelper.ComputeBlake3(downloadedBytes, extension);
        string computedNormalized = ContentAddressableStorage.NormalizeBlake3Hash(computedBlake3);

        if (!string.Equals(computedNormalized, normalizedHash, StringComparison.OrdinalIgnoreCase))
        {
            circuit.RecordFailure(DateTime.UtcNow);
            return false;
        }

        string? metadataHeader = ExtractMetadataHeader(response);

        var storeResult = targetStorage.StoreAsset(downloadedBytes, extension, metadataHeader, precomputedBlake3: computedBlake3);
        if (storeResult.Success)
        {
            circuit.RecordSuccess();
            return true;
        }
        
        return false;
    }

    private string? ExtractMetadataHeader(HttpResponseMessage response)
    {
        if (!response.Headers.TryGetValues("X-Asset-Metadata", out var metaValues))
        {
            return null;
        }

        string? rawHeader = metaValues.FirstOrDefault();
        if (string.IsNullOrWhiteSpace(rawHeader))
        {
            return null;
        }

        try
        {
            byte[] metaBytes = Convert.FromBase64String(rawHeader);
            return Encoding.UTF8.GetString(metaBytes);
        }
        catch
        {
            return rawHeader;
        }
    }

    public async Task<PublishMapInitiateResponse> InitiatePublishAsync(PublishMapInitiateRequest request, CancellationToken cancellationToken = default)
    {
        string url = $"{_registryServerUrl}/api/publish_map/initiate";
        var content = new StringContent(JsonSerializer.Serialize(request), Encoding.UTF8, "application/json");

        try
        {
            var response = await _httpClient.PostAsync(url, content, cancellationToken);
            string responseJson = await response.Content.ReadAsStringAsync(cancellationToken);

            var result = JsonSerializer.Deserialize<PublishMapInitiateResponse>(responseJson, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            return result ?? new PublishMapInitiateResponse
            {
                Success = response.IsSuccessStatusCode,
                Message = responseJson
            };
        }
        catch (Exception ex)
        {
            return new PublishMapInitiateResponse
            {
                Success = false,
                Status = "Error",
                Message = ex.Message
            };
        }
    }

    public async Task<PublishMapFinalizeResponse> FinalizePublishAsync(PublishMapFinalizeRequest request, CancellationToken cancellationToken = default)
    {
        string url = $"{_registryServerUrl}/api/publish_map/finalize";
        var content = new StringContent(JsonSerializer.Serialize(request), Encoding.UTF8, "application/json");

        try
        {
            var response = await _httpClient.PostAsync(url, content, cancellationToken);
            string responseJson = await response.Content.ReadAsStringAsync(cancellationToken);

            var result = JsonSerializer.Deserialize<PublishMapFinalizeResponse>(responseJson, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            return result ?? new PublishMapFinalizeResponse
            {
                Success = response.IsSuccessStatusCode,
                Message = responseJson
            };
        }
        catch (Exception ex)
        {
            return new PublishMapFinalizeResponse
            {
                Success = false,
                Status = "Error",
                Message = ex.Message
            };
        }
    }

    public async Task<bool> CheckAssetExistsAsync(string hash, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(hash)) return false;
        string normalized = ContentAddressableStorage.NormalizeBlake3Hash(hash);
        string url = $"{_registryServerUrl}/api/assets/{normalized}";
        try
        {
            using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token);
            using var request = new HttpRequestMessage(HttpMethod.Head, url);
            using var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, linkedCts.Token);
            return response.IsSuccessStatusCode;
        }
        catch
        {
            return false;
        }
    }

    public async Task<bool> UploadMissingAssetAsync(
        string hash,
        byte[] fileBytes,
        string fileName,
        string currentUsername,
        string authorPublicKey,
        string signature,
        string mapTitle,
        string mapVersion,
        string? sessionId = null,
        CancellationToken cancellationToken = default)
    {
        if (fileBytes.Length == 0 || fileBytes.Length > ContentAddressableStorage.MaximumAssetSizeBytes)
        {
            return false;
        }

        if (await CheckAssetExistsAsync(hash, cancellationToken))
        {
            return true;
        }

        string url = $"{_registryServerUrl}/api/publish_map/upload_asset";
        using var form = new MultipartFormDataContent();
        form.Add(new StringContent(hash), "Hash");
        form.Add(new StringContent(signature), "Signature");
        form.Add(new StringContent(currentUsername), "AuthorUsername");
        form.Add(new StringContent(authorPublicKey), "PublicKey");
        form.Add(new StringContent(mapTitle), "MapTitle");
        form.Add(new StringContent(mapVersion), "MapVersion");
        if (!string.IsNullOrEmpty(sessionId))
        {
            form.Add(new StringContent(sessionId), "SessionId");
        }

        var fileContent = new ByteArrayContent(fileBytes);
        form.Add(fileContent, "File", fileName);

        if (_throttle != null)
        {
            await _throttle.ConsumeAsync(fileBytes.Length, cancellationToken);
        }

        try
        {
            var response = await _httpClient.PostAsync(url, form, cancellationToken);
            return response.IsSuccessStatusCode;
        }
        catch
        {
            return false;
        }
    }

        public async Task<(bool Success, string? FailedAsset, string? ErrorMessage)> UploadMissingAssetsMultiThreadedAsync(
        string workspace,
        IReadOnlyList<string> missingHashes,
        IReadOnlyDictionary<string, string> hashToRelativePath,
        string currentUsername,
        string authorPublicKey,
        NSec.Cryptography.Key authorshipKey,
        string mapTitle,
        string mapVersion,
        string? sessionId = null,
        Action<int, int, string>? progressCallback = null,
        int maximumConcurrency = 4,
        CancellationToken cancellationToken = default)
    {
        int totalMissing = missingHashes.Count;
        int[] completedCountWrapper = new int[1];
        string?[] errorWrapper = new string?[2]; // [0] = firstErrorAsset, [1] = firstErrorMessage
        var errorLock = new object();

        using var semaphore = new SemaphoreSlim(Math.Max(1, maximumConcurrency));

        var tasks = missingHashes.Select(async missingHash =>
        {
            await ProcessMissingAssetUploadAsync(
                missingHash,
                workspace,
                hashToRelativePath,
                currentUsername,
                authorPublicKey,
                authorshipKey,
                mapTitle,
                mapVersion,
                sessionId,
                progressCallback,
                totalMissing,
                completedCountWrapper,
                errorLock,
                errorWrapper,
                semaphore,
                cancellationToken);
        });

        await Task.WhenAll(tasks);

        if (errorWrapper[0] != null)
        {
            return (false, errorWrapper[0], errorWrapper[1]);
        }

        return (true, null, null);
    }

    private async Task ProcessMissingAssetUploadAsync(
        string missingHash,
        string workspace,
        IReadOnlyDictionary<string, string> hashToRelativePath,
        string currentUsername,
        string authorPublicKey,
        NSec.Cryptography.Key authorshipKey,
        string mapTitle,
        string mapVersion,
        string? sessionId,
        Action<int, int, string>? progressCallback,
        int totalMissing,
        int[] completedCountWrapper,
        object errorLock,
        string?[] errorWrapper,
        SemaphoreSlim semaphore,
        CancellationToken cancellationToken)
    {
        if (errorWrapper[0] != null || cancellationToken.IsCancellationRequested) return;

        if (!hashToRelativePath.TryGetValue(missingHash, out var relPath))
        {
            SetUploadError(errorLock, errorWrapper, missingHash, $"Missing hash '{missingHash}' is not present in manifest file mapping.");
            return;
        }

        string fullFilePath = Path.Combine(workspace, relPath);
        if (!File.Exists(fullFilePath))
        {
            SetUploadError(errorLock, errorWrapper, relPath, $"File '{relPath}' was not found on disk at '{fullFilePath}'.");
            return;
        }

        await semaphore.WaitAsync(cancellationToken);
        try
        {
            if (errorWrapper[0] != null || cancellationToken.IsCancellationRequested) return;

            byte[] fileBytes = await File.ReadAllBytesAsync(fullFilePath, cancellationToken);
            if (!ValidateAssetBytes(fileBytes, relPath, missingHash, fullFilePath, errorLock, errorWrapper))
            {
                return;
            }

            if (await CheckAssetExistsAsync(missingHash, cancellationToken))
            {
                NotifyAssetUploaded(fullFilePath, progressCallback, totalMissing, completedCountWrapper);
                return;
            }

            await PerformAssetUploadWithRetriesAsync(
                missingHash,
                fileBytes,
                relPath,
                fullFilePath,
                currentUsername,
                authorPublicKey,
                authorshipKey,
                mapTitle,
                mapVersion,
                sessionId,
                progressCallback,
                totalMissing,
                completedCountWrapper,
                errorLock,
                errorWrapper,
                cancellationToken);
        }
        finally
        {
            semaphore.Release();
        }
    }

    private bool ValidateAssetBytes(
        byte[] fileBytes,
        string relPath,
        string missingHash,
        string fullFilePath,
        object errorLock,
        string?[] errorWrapper)
    {
        if (fileBytes.Length == 0)
        {
            SetUploadError(errorLock, errorWrapper, relPath, $"File '{relPath}' is empty (0 bytes) and cannot be published as an asset.");
            return false;
        }

        if (fileBytes.Length > ContentAddressableStorage.MaximumAssetSizeBytes)
        {
            double sizeMb = (double)fileBytes.Length / (1024 * 1024);
            double maxMb = (double)ContentAddressableStorage.MaximumAssetSizeBytes / (1024 * 1024);
            SetUploadError(errorLock, errorWrapper, relPath, $"Asset '{relPath}' ({sizeMb:F2} MB) exceeds maximum allowed size of {maxMb:F0} MB per asset.");
            return false;
        }

        string ext = Path.GetExtension(fullFilePath);
        string computedBlake3 = RealmMetadataHelper.ComputeBlake3(fileBytes, ext);
        string computedNorm = ContentAddressableStorage.NormalizeBlake3Hash(computedBlake3);
        string expectedNorm = ContentAddressableStorage.NormalizeBlake3Hash(missingHash);
        
        if (!string.Equals(computedNorm, expectedNorm, StringComparison.OrdinalIgnoreCase))
        {
            SetUploadError(errorLock, errorWrapper, relPath, $"Asset file '{relPath}' hash mismatch: expected {expectedNorm}, but file on disk has hash {computedNorm}.");
            return false;
        }
        
        return true;
    }

    private void SetUploadError(object errorLock, string?[] errorWrapper, string asset, string message)
    {
        lock (errorLock)
        {
            errorWrapper[0] ??= asset;
            errorWrapper[1] ??= message;
        }
    }

    private void NotifyAssetUploaded(string fullFilePath, Action<int, int, string>? progressCallback, int totalMissing, int[] completedCountWrapper)
    {
        int done = Interlocked.Increment(ref completedCountWrapper[0]);
        progressCallback?.Invoke(done, totalMissing, Path.GetFileName(fullFilePath));
    }

    private async Task PerformAssetUploadWithRetriesAsync(
        string missingHash,
        byte[] fileBytes,
        string relPath,
        string fullFilePath,
        string currentUsername,
        string authorPublicKey,
        NSec.Cryptography.Key authorshipKey,
        string mapTitle,
        string mapVersion,
        string? sessionId,
        Action<int, int, string>? progressCallback,
        int totalMissing,
        int[] completedCountWrapper,
        object errorLock,
        string?[] errorWrapper,
        CancellationToken cancellationToken)
    {
        byte[] hashBytes = Encoding.UTF8.GetBytes(missingHash);
        byte[] signatureBytes = NSec.Cryptography.SignatureAlgorithm.Ed25519.Sign(authorshipKey, hashBytes);
        string signatureStr = Convert.ToBase64String(signatureBytes);

        bool success = false;
        string? lastError = null;

        for (int attempt = 0; attempt < 3 && !success && !cancellationToken.IsCancellationRequested; attempt++)
        {
            if (attempt > 0)
            {
                await Task.Delay(200 * attempt, cancellationToken);
            }

            var (attemptSuccess, attemptError) = await AttemptSingleAssetUploadAsync(
                missingHash, signatureStr, currentUsername, authorPublicKey, mapTitle, mapVersion, sessionId, fileBytes, fullFilePath, cancellationToken);
            
            success = attemptSuccess;
            lastError = attemptError;
        }

        if (!success)
        {
            SetUploadError(errorLock, errorWrapper, relPath, lastError ?? "Upload failed after retries.");
        }
        else
        {
            NotifyAssetUploaded(fullFilePath, progressCallback, totalMissing, completedCountWrapper);
        }
    }

    private async Task<(bool Success, string? Error)> AttemptSingleAssetUploadAsync(
        string missingHash,
        string signatureStr,
        string currentUsername,
        string authorPublicKey,
        string mapTitle,
        string mapVersion,
        string? sessionId,
        byte[] fileBytes,
        string fullFilePath,
        CancellationToken cancellationToken)
    {
        string url = $"{_registryServerUrl}/api/publish_map/upload_asset";
        using var form = new MultipartFormDataContent();
        form.Add(new StringContent(missingHash), "Hash");
        form.Add(new StringContent(signatureStr), "Signature");
        form.Add(new StringContent(currentUsername), "AuthorUsername");
        form.Add(new StringContent(authorPublicKey), "PublicKey");
        form.Add(new StringContent(mapTitle), "MapTitle");
        form.Add(new StringContent(mapVersion), "MapVersion");
        if (!string.IsNullOrEmpty(sessionId))
        {
            form.Add(new StringContent(sessionId), "SessionId");
        }

        var fileContent = new ByteArrayContent(fileBytes);
        form.Add(fileContent, "File", Path.GetFileName(fullFilePath));

        if (_throttle != null)
        {
            await _throttle.ConsumeAsync(fileBytes.Length, cancellationToken);
        }

        try
        {
            var response = await _httpClient.PostAsync(url, form, cancellationToken);
            if (response.IsSuccessStatusCode)
            {
                return (true, null);
            }
            else
            {
                string err = await response.Content.ReadAsStringAsync(cancellationToken);
                return (false, $"HTTP {(int)response.StatusCode}: {err}");
            }
        }
        catch (Exception ex)
        {
            return (false, ex.Message);
        }
    }

public async Task<List<ClusterEventDto>> GetClusterEventsAsync(DateTime? sinceUtc = null, int limit = 100, CancellationToken cancellationToken = default)
    {
        string url = $"{_registryServerUrl}/api/cluster/events?limit={limit}";
        if (sinceUtc.HasValue)
        {
            url += $"&sinceUtc={Uri.EscapeDataString(sinceUtc.Value.ToString("o"))}";
        }

        try
        {
            var response = await _httpClient.GetAsync(url, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                return new List<ClusterEventDto>();
            }

            string json = await response.Content.ReadAsStringAsync(cancellationToken);
            var list = JsonSerializer.Deserialize<List<ClusterEventDto>>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            return list ?? new List<ClusterEventDto>();
        }
        catch
        {
            return new List<ClusterEventDto>();
        }
    }

    public async Task<bool> PostClusterEventAsync(ClusterEventDto evt, CancellationToken cancellationToken = default)
    {
        string url = $"{_registryServerUrl}/api/cluster/event";
        try
        {
            var content = new StringContent(JsonSerializer.Serialize(evt), Encoding.UTF8, "application/json");
            var response = await _httpClient.PostAsync(url, content, cancellationToken);
            return response.IsSuccessStatusCode;
        }
        catch
        {
            return false;
        }
    }

    public async Task<RemoveManifestResponseDto> RemoveManifestVersionAsync(string mapTitle, string mapVersion, string adminPrivateKeyBase64, CancellationToken cancellationToken = default)
    {
        string url = $"{_registryServerUrl}/api/admin/remove_manifest";
        string adminPublicKey = AuthorSignatureHelper.GetPublicKey(adminPrivateKeyBase64);
        string payload = $"remove_manifest:{mapTitle.Trim().ToLowerInvariant()}:{mapVersion.Trim().ToLowerInvariant()}";
        string signature = AuthorSignatureHelper.SignMessage(adminPrivateKeyBase64, payload);
        string bypassToken = AdminBypassAuth.CreateBypassToken(adminPrivateKeyBase64, mapTitle.Trim(), mapVersion.Trim());

        using var request = new HttpRequestMessage(HttpMethod.Post, url);
        request.Headers.Add("X-Admin-Bypass", bypassToken);
        request.Headers.Add("X-Admin-PublicKey", adminPublicKey);
        request.Headers.Add("X-Cluster-Signature", signature);

        var body = new AdminRemoveManifestRequest
        {
            MapTitle = mapTitle.Trim(),
            MapVersion = mapVersion.Trim(),
            AdminPublicKey = adminPublicKey,
            Signature = signature
        };
        request.Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json");

        try
        {
            var response = await _httpClient.SendAsync(request, cancellationToken);
            string json = await response.Content.ReadAsStringAsync(cancellationToken);
            var result = JsonSerializer.Deserialize<RemoveManifestResponseDto>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            return result ?? new RemoveManifestResponseDto
            {
                Success = response.IsSuccessStatusCode,
                MapTitle = mapTitle,
                MapVersion = mapVersion,
                Message = json
            };
        }
        catch (Exception ex)
        {
            return new RemoveManifestResponseDto
            {
                Success = false,
                MapTitle = mapTitle,
                MapVersion = mapVersion,
                Message = ex.Message
            };
        }
    }

    public async Task<RemoveManifestResponseDto> RemoveAllManifestVersionsAsync(string mapTitle, string adminPrivateKeyBase64, CancellationToken cancellationToken = default)
    {
        string url = $"{_registryServerUrl}/api/admin/remove_manifest";
        string adminPublicKey = AuthorSignatureHelper.GetPublicKey(adminPrivateKeyBase64);
        string payload = $"remove_manifest:{mapTitle.Trim().ToLowerInvariant()}:all";
        string signature = AuthorSignatureHelper.SignMessage(adminPrivateKeyBase64, payload);
        string bypassToken = AdminBypassAuth.CreateBypassToken(adminPrivateKeyBase64, mapTitle.Trim(), "all");

        using var request = new HttpRequestMessage(HttpMethod.Post, url);
        request.Headers.Add("X-Admin-Bypass", bypassToken);
        request.Headers.Add("X-Admin-PublicKey", adminPublicKey);
        request.Headers.Add("X-Cluster-Signature", signature);

        var body = new AdminRemoveManifestRequest
        {
            MapTitle = mapTitle.Trim(),
            MapVersion = null,
            AdminPublicKey = adminPublicKey,
            Signature = signature
        };
        request.Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json");

        try
        {
            var response = await _httpClient.SendAsync(request, cancellationToken);
            string json = await response.Content.ReadAsStringAsync(cancellationToken);
            var result = JsonSerializer.Deserialize<RemoveManifestResponseDto>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            return result ?? new RemoveManifestResponseDto
            {
                Success = response.IsSuccessStatusCode,
                MapTitle = mapTitle,
                AllVersionsRemoved = true,
                Message = json
            };
        }
        catch (Exception ex)
        {
            return new RemoveManifestResponseDto
            {
                Success = false,
                MapTitle = mapTitle,
                AllVersionsRemoved = true,
                Message = ex.Message
            };
        }
    }

    public async Task<RemoveManifestResponseDto> RemoveManifestAsync(string mapTitle, string? mapVersion, string adminPrivateKeyBase64, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(mapVersion))
        {
            return await RemoveAllManifestVersionsAsync(mapTitle, adminPrivateKeyBase64, cancellationToken);
        }
        return await RemoveManifestVersionAsync(mapTitle, mapVersion, adminPrivateKeyBase64, cancellationToken);
    }

    public async Task<CasPruneResponseDto> PruneServerCasAsync(string adminPrivateKeyBase64, CancellationToken cancellationToken = default)
    {
        string url = $"{_registryServerUrl}/api/admin/prune_cas";
        string bypassToken = AdminBypassAuth.CreateBypassToken(adminPrivateKeyBase64, "admin", "prune_cas");

        using var request = new HttpRequestMessage(HttpMethod.Post, url);
        request.Headers.Add("X-Admin-Bypass", bypassToken);

        try
        {
            var response = await _httpClient.SendAsync(request, cancellationToken);
            string json = await response.Content.ReadAsStringAsync(cancellationToken);

            var result = JsonSerializer.Deserialize<CasPruneResponseDto>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            return result ?? new CasPruneResponseDto
            {
                Success = response.IsSuccessStatusCode,
                Message = json
            };
        }
        catch (Exception ex)
        {
            return new CasPruneResponseDto
            {
                Success = false,
                Message = ex.Message
            };
        }
    }

    public async Task<ClusterStateDigestDto?> GetStateDigestAsync(CancellationToken cancellationToken = default)
    {
        string url = $"{_registryServerUrl}/api/cluster/state_digest";
        try
        {
            var response = await _httpClient.GetAsync(url, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                return null;
            }

            string json = await response.Content.ReadAsStringAsync(cancellationToken);
            return JsonSerializer.Deserialize<ClusterStateDigestDto>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        }
        catch
        {
            return null;
        }
    }

    public async Task<ClusterSnapshotDto?> GetSnapshotAsync(CancellationToken cancellationToken = default)
    {
        string url = $"{_registryServerUrl}/api/cluster/snapshot";
        try
        {
            var response = await _httpClient.GetAsync(url, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                return null;
            }

            string json = await response.Content.ReadAsStringAsync(cancellationToken);
            return JsonSerializer.Deserialize<ClusterSnapshotDto>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        }
        catch
        {
            return null;
        }
    }

    public async Task<ApplySnapshotResponseDto> ApplySnapshotAsync(ClusterSnapshotDto snapshot, string? adminPrivateKeyBase64 = null, CancellationToken cancellationToken = default)
    {
        string url = $"{_registryServerUrl}/api/cluster/snapshot";
        using var request = new HttpRequestMessage(HttpMethod.Post, url);
        request.Content = new StringContent(JsonSerializer.Serialize(snapshot), Encoding.UTF8, "application/json");

        if (!string.IsNullOrWhiteSpace(adminPrivateKeyBase64))
        {
            string bypassToken = AdminBypassAuth.CreateBypassToken(adminPrivateKeyBase64, "cluster", "snapshot");
            request.Headers.Add("X-Admin-Bypass", bypassToken);
        }

        try
        {
            var response = await _httpClient.SendAsync(request, cancellationToken);
            string json = await response.Content.ReadAsStringAsync(cancellationToken);

            var result = JsonSerializer.Deserialize<ApplySnapshotResponseDto>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            return result ?? new ApplySnapshotResponseDto
            {
                Success = response.IsSuccessStatusCode,
                Message = json
            };
        }
        catch (Exception ex)
        {
            return new ApplySnapshotResponseDto
            {
                Success = false,
                Message = ex.Message
            };
        }
    }

    public async Task<MapManifest?> GetManifestAsync(string mapId, CancellationToken cancellationToken = default)
    {
        string url = $"{_registryServerUrl}/api/manifests/{Uri.EscapeDataString(mapId)}";
        try
        {
            var response = await _httpClient.GetAsync(url, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                return null;
            }

            string json = await response.Content.ReadAsStringAsync(cancellationToken);
            return MapManifest.LoadFromJson(json);
        }
        catch
        {
            return null;
        }
    }

    public async Task<List<DiscoveryMapDto>> GetDiscoveryMapsAsync(CancellationToken cancellationToken = default)
    {
        string url = $"{_registryServerUrl}/api/discovery/maps";
        try
        {
            var response = await _httpClient.GetAsync(url, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                return new List<DiscoveryMapDto>();
            }

            string json = await response.Content.ReadAsStringAsync(cancellationToken);
            var maps = JsonSerializer.Deserialize<List<DiscoveryMapDto>>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            return maps ?? new List<DiscoveryMapDto>();
        }
        catch
        {
            return new List<DiscoveryMapDto>();
        }
    }

    public async Task<MapMaintainersResponseDto> GetMapMaintainersAsync(string mapTitle, CancellationToken cancellationToken = default)
    {
        string url = $"{_registryServerUrl}/api/maps/{Uri.EscapeDataString(mapTitle)}/maintainers";
        try
        {
            var response = await _httpClient.GetAsync(url, cancellationToken);
            string json = await response.Content.ReadAsStringAsync(cancellationToken);
            var dto = JsonSerializer.Deserialize<MapMaintainersResponseDto>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            return dto ?? new MapMaintainersResponseDto { Success = false, Message = "Failed to deserialize server response." };
        }
        catch (Exception ex)
        {
            return new MapMaintainersResponseDto { Success = false, Message = ex.Message };
        }
    }

    public async Task<MapMaintainersResponseDto> AddMapMaintainerAsync(AddMapMaintainerRequest request, CancellationToken cancellationToken = default)
    {
        string url = $"{_registryServerUrl}/api/maps/{Uri.EscapeDataString(request.MapTitle)}/maintainers/add";
        try
        {
            var content = new StringContent(JsonSerializer.Serialize(request), Encoding.UTF8, "application/json");
            var response = await _httpClient.PostAsync(url, content, cancellationToken);
            string json = await response.Content.ReadAsStringAsync(cancellationToken);
            var dto = JsonSerializer.Deserialize<MapMaintainersResponseDto>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            return dto ?? new MapMaintainersResponseDto { Success = false, Message = "Failed to deserialize server response." };
        }
        catch (Exception ex)
        {
            return new MapMaintainersResponseDto { Success = false, Message = ex.Message };
        }
    }

    public async Task<MapMaintainersResponseDto> RemoveMapMaintainerAsync(RemoveMapMaintainerRequest request, CancellationToken cancellationToken = default)
    {
        string url = $"{_registryServerUrl}/api/maps/{Uri.EscapeDataString(request.MapTitle)}/maintainers/remove";
        try
        {
            var content = new StringContent(JsonSerializer.Serialize(request), Encoding.UTF8, "application/json");
            var response = await _httpClient.PostAsync(url, content, cancellationToken);
            string json = await response.Content.ReadAsStringAsync(cancellationToken);
            var dto = JsonSerializer.Deserialize<MapMaintainersResponseDto>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            return dto ?? new MapMaintainersResponseDto { Success = false, Message = "Failed to deserialize server response." };
        }
        catch (Exception ex)
        {
            return new MapMaintainersResponseDto { Success = false, Message = ex.Message };
        }
    }
}

