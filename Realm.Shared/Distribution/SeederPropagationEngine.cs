using Realm.Shared.Metadata;
using System.Text;
using System.Text.Json;

namespace Realm.Shared.Distribution;

public class SeederPropagationEngine
{
    private readonly string _seederId;
    private readonly int _capacityPercentage;
    private readonly ContentAddressableStorage _storage;
    private readonly string _registryServerUrl;
    private readonly HttpClient _httpClient;
    private readonly TokenBucketThrottle? _throttle;
    private CancellationTokenSource? _cancellationTokenSource;

    public SeederPropagationEngine(
        string seederId,
        int capacityPercentage,
        ContentAddressableStorage storage,
        string? registryServerUrl = null,
        HttpClient? httpClient = null,
        TokenBucketThrottle? throttle = null)
    {
        _seederId = seederId;
        _capacityPercentage = capacityPercentage;
        _storage = storage;
        _registryServerUrl = (string.IsNullOrWhiteSpace(registryServerUrl) ? ServersConfigHelper.GetDefaultServerUrl() : registryServerUrl).TrimEnd('/');
        _httpClient = httpClient ?? new HttpClient();
        _throttle = throttle;
    }

    public async Task<int> RunPropagationCycleAsync(CancellationToken cancellationToken = default)
    {
        if (!_storage.CheckFreeDiskSpaceAcceptingUploads())
        {
            return 0;
        }

        var seeders = await FetchOtherSeedersAsync(cancellationToken);
        if (seeders.Count == 0)
        {
            return 0;
        }

        int propagatedCount = 0;

        foreach (var peerSeeder in seeders)
        {
            if (cancellationToken.IsCancellationRequested || !_storage.CheckFreeDiskSpaceAcceptingUploads())
            {
                break;
            }

            propagatedCount += await ProcessPeerSeederAsync(peerSeeder, cancellationToken);
        }

        int syncedHeaders = await RunBloomFilterHeaderSyncWithSeedersAsync(seeders, cancellationToken);
        return propagatedCount + syncedHeaders;
    }

    private async Task<int> ProcessPeerSeederAsync(SeederNodeDto peerSeeder, CancellationToken cancellationToken)
    {
        if (string.Equals(peerSeeder.SeederId, _seederId, StringComparison.OrdinalIgnoreCase))
        {
            return 0;
        }

        var peerHashes = await FetchPeerCatalogAsync(peerSeeder.IP, peerSeeder.Port, cancellationToken);
        if (peerHashes.Count == 0)
        {
            return 0;
        }

        var wantedHashes = peerHashes
            .Where(hash => DistributionSharding.SeederAcceptsHash(_seederId, _capacityPercentage, hash))
            .Where(hash => !_storage.HasAsset(hash))
            .ToList();

        int propagatedCount = 0;
        foreach (string hash in wantedHashes)
        {
            if (cancellationToken.IsCancellationRequested || !_storage.CheckFreeDiskSpaceAcceptingUploads())
            {
                break;
            }

            bool downloaded = await DownloadAssetFromPeerAsync(peerSeeder.IP, peerSeeder.Port, hash, cancellationToken);
            if (downloaded)
            {
                propagatedCount++;
            }
        }

        return propagatedCount;
    }

    public void StartBackgroundWorker(TimeSpan interval, CancellationToken cancellationToken = default)
    {
        _cancellationTokenSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var token = _cancellationTokenSource.Token;

        Task.Run(() => BackgroundWorkerLoopAsync(interval, token), token);
    }

    private async Task BackgroundWorkerLoopAsync(TimeSpan interval, CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(interval, token);
                await RunPropagationCycleAsync(token);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch
            {
            }
        }
    }

    public void Stop()
    {
        _cancellationTokenSource?.Cancel();
    }

    private async Task<List<SeederNodeDto>> FetchOtherSeedersAsync(CancellationToken cancellationToken)
    {
        try
        {
            string url = $"{_registryServerUrl}/api/seeders";
            var response = await _httpClient.GetAsync(url, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                return new List<SeederNodeDto>();
            }

            string json = await response.Content.ReadAsStringAsync(cancellationToken);
            var list = JsonSerializer.Deserialize<List<SeederNodeDto>>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            return list?.Where(s => !string.Equals(s.SeederId, _seederId, StringComparison.OrdinalIgnoreCase)).ToList() ?? new List<SeederNodeDto>();
        }
        catch
        {
            return new List<SeederNodeDto>();
        }
    }

    private async Task<List<string>> FetchPeerCatalogAsync(string ip, int port, CancellationToken cancellationToken)
    {
        try
        {
            string url = $"http://{ip}:{port}/api/seeders/catalog";
            var response = await _httpClient.GetAsync(url, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                return new List<string>();
            }

            string json = await response.Content.ReadAsStringAsync(cancellationToken);
            var catalog = JsonSerializer.Deserialize<SeederCatalogResponseDto>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            return catalog?.AssetHashes ?? new List<string>();
        }
        catch
        {
            return new List<string>();
        }
    }

    private async Task<bool> DownloadAssetFromPeerAsync(string ip, int port, string hash, CancellationToken cancellationToken)
    {
        try
        {
            string url = $"http://{ip}:{port}/api/assets/{hash}";
            var response = await _httpClient.GetAsync(url, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                return false;
            }

            byte[] bytes = await response.Content.ReadAsByteArrayAsync(cancellationToken);
            if (_throttle != null)
            {
                await _throttle.ConsumeAsync(bytes.Length, cancellationToken);
            }

            string extension = string.Empty;
            if (response.Content.Headers.ContentDisposition?.FileName != null)
            {
                extension = System.IO.Path.GetExtension(response.Content.Headers.ContentDisposition.FileName);
            }

            string canonicalBlake3 = RealmMetadataHelper.ComputeBlake3(bytes, extension);
            string normalizedHash = ContentAddressableStorage.NormalizeBlake3Hash(canonicalBlake3);

            if (!string.Equals(normalizedHash, hash, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            string? metadataHeader = null;
            if (response.Headers.TryGetValues("X-Asset-Metadata", out var metaValues))
            {
                metadataHeader = metaValues.FirstOrDefault();
            }

            var result = _storage.StoreAsset(bytes, extension, metadataHeader);
            return result.Success;
        }
        catch
        {
            return false;
        }
    }

    public async Task<int> RunBloomFilterHeaderSyncAsync(CancellationToken cancellationToken = default)
    {
        var seeders = await FetchOtherSeedersAsync(cancellationToken);
        if (seeders.Count == 0)
        {
            return 0;
        }

        return await RunBloomFilterHeaderSyncWithSeedersAsync(seeders, cancellationToken);
    }

    public async Task<int> RunBloomFilterHeaderSyncWithSeedersAsync(List<SeederNodeDto> seeders, CancellationToken cancellationToken = default)
    {
        int syncedHeadersCount = 0;
        var localHashes = _storage.GetAllStoredHashes().ToList();
        if (localHashes.Count == 0)
        {
            return 0;
        }

        foreach (var peerSeeder in seeders)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                break;
            }

            if (string.Equals(peerSeeder.SeederId, _seederId, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var peerFilter = await FetchPeerHeaderBloomFilterAsync(peerSeeder.IP, peerSeeder.Port, cancellationToken);
            if (peerFilter == null)
            {
                continue;
            }

            syncedHeadersCount += await SyncLocalHashesWithPeerAsync(peerSeeder, peerFilter, localHashes, cancellationToken);
        }

        return syncedHeadersCount;
    }

    private async Task<int> SyncLocalHashesWithPeerAsync(SeederNodeDto peerSeeder, BloomFilter peerFilter, List<string> localHashes, CancellationToken cancellationToken)
    {
        int syncedHeadersCount = 0;
        foreach (string hash in localHashes)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                break;
            }

            string? localMetadataJson = _storage.GetAssetMetadata(hash);
            string headerKey = BloomFilter.CreateHeaderKey(hash, localMetadataJson);

            if (peerFilter.Contains(headerKey))
            {
                continue;
            }

            bool synced = await SyncHeaderWithPeerAsync(peerSeeder.IP, peerSeeder.Port, hash, localMetadataJson, cancellationToken);
            if (synced)
            {
                syncedHeadersCount++;
            }
        }

        return syncedHeadersCount;
    }

    public async Task<BloomFilter?> FetchPeerHeaderBloomFilterAsync(string ip, int port, CancellationToken cancellationToken = default)
    {
        try
        {
            string url = $"http://{ip}:{port}/api/seeders/bloom_headers";
            var response = await _httpClient.GetAsync(url, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                return null;
            }

            string json = await response.Content.ReadAsStringAsync(cancellationToken);
            var dto = JsonSerializer.Deserialize<BloomHeadersResponseDto>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            if (dto == null || string.IsNullOrEmpty(dto.FilterDataBase64))
            {
                return null;
            }

            return BloomFilter.FromBase64(dto.FilterDataBase64, dto.BitCount, dto.HashCount, dto.ItemCount);
        }
        catch
        {
            return null;
        }
    }

    private async Task<bool> SyncHeaderWithPeerAsync(
        string ip,
        int port,
        string blake3Hash,
        string? localMetadataJson,
        CancellationToken cancellationToken)
    {
        try
        {
            string? authorPublicKey = ExtractAuthorPublicKey(localMetadataJson);
            string? authorSignature = ExtractAuthorSignature(localMetadataJson);

            var requestDto = new HeaderSyncRequestDto
            {
                Blake3Hash = blake3Hash,
                MetadataHeadersJson = localMetadataJson,
                AuthorPublicKey = authorPublicKey,
                AuthorSignature = authorSignature
            };

            string requestJson = JsonSerializer.Serialize(requestDto);
            var content = new StringContent(requestJson, Encoding.UTF8, "application/json");

            string url = $"http://{ip}:{port}/api/seeders/sync_headers";
            var response = await _httpClient.PostAsync(url, content, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                return false;
            }

            string responseJson = await response.Content.ReadAsStringAsync(cancellationToken);
            var syncResponse = JsonSerializer.Deserialize<HeaderSyncResponseDto>(responseJson, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

            if (syncResponse != null && !string.IsNullOrWhiteSpace(syncResponse.CurrentMetadataHeadersJson))
            {
                string returnedMeta = syncResponse.CurrentMetadataHeadersJson;
                if (!string.Equals(returnedMeta, localMetadataJson, StringComparison.Ordinal))
                {
                    byte[] existingBytes = _storage.GetAssetBytes(blake3Hash) ?? Array.Empty<byte>();
                    string? filePath = _storage.FindAssetFilePath(blake3Hash);
                    string extension = filePath != null ? System.IO.Path.GetExtension(filePath) : ".bin";

                    _storage.StoreAsset(
                        existingBytes,
                        extension,
                        returnedMeta,
                        ExtractAuthorPublicKey(returnedMeta),
                        ExtractAuthorSignature(returnedMeta));
                    return true;
                }
            }

            return syncResponse?.Updated ?? false;
        }
        catch
        {
            return false;
        }
    }

    private static string? ExtractAuthorPublicKey(string? metadataJson)
    {
        if (string.IsNullOrWhiteSpace(metadataJson)) return null;
        try
        {
            using var doc = JsonDocument.Parse(metadataJson);
            if (doc.RootElement.TryGetProperty("AuthorPublicKey", out var prop)) return prop.GetString();
            if (doc.RootElement.TryGetProperty("author_public_key", out var sProp)) return sProp.GetString();
        }
        catch { }
        return null;
    }

    private static string? ExtractAuthorSignature(string? metadataJson)
    {
        if (string.IsNullOrWhiteSpace(metadataJson)) return null;
        try
        {
            using var doc = JsonDocument.Parse(metadataJson);
            if (doc.RootElement.TryGetProperty("AuthorSignature", out var prop)) return prop.GetString();
            if (doc.RootElement.TryGetProperty("author_signature", out var sProp)) return sProp.GetString();
        }
        catch { }
        return null;
    }
}
