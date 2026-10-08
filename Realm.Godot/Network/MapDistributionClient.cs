using Godot;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Realm.Shared.Distribution;

public class MapDistributionClient
{
    private readonly System.Net.Http.HttpClient _httpClient = new System.Net.Http.HttpClient { Timeout = Timeout.InfiniteTimeSpan };

    public event Action<float>? DownloadProgressChanged;

    private static LobbyManager? GetLobbyManager()
    {
        if (LobbyManager.Instance != null && GodotObject.IsInstanceValid(LobbyManager.Instance))
        {
            return LobbyManager.Instance;
        }

        var tree = Engine.GetMainLoop() as SceneTree;
        if (tree?.Root != null)
        {
            var existing = tree.Root.GetNodeOrNull<LobbyManager>("LobbyManager");
            if (existing != null && GodotObject.IsInstanceValid(existing))
            {
                return existing;
            }

            var lm = new LobbyManager();
            lm.Name = "LobbyManager";
            tree.Root.AddChild(lm);
            return lm;
        }

        return null;
    }

    public async Task<bool> DownloadMapAsync(string hostIp, int port, string mapName, Action<float>? progressCallback = null)
    {
        if (port == 80 || port == 5000 || port == 443)
        {
            string hostBaseUrl = $"http://{hostIp}:{port}";
            bool httpSuccess = await DownloadMapPackageFromRegistryAsync(mapName, hostBaseUrl, progressCallback, CancellationToken.None);
            if (httpSuccess)
            {
                return true;
            }
        }

        var lm = GetLobbyManager();
        if (lm != null)
        {
            return await lm.DownloadMapEphemerallyAsync(hostIp, port, mapName, p =>
            {
                progressCallback?.Invoke(p);
                DownloadProgressChanged?.Invoke(p);
            }, CancellationToken.None);
        }

        return false;
    }

    public async Task<bool> DownloadMapPackageFromRegistryAsync(
        string mapId,
        string registryServerUrl,
        Action<float>? progressCallback = null,
        CancellationToken cancellationToken = default)
    {
        using var overallTimeoutCts = new CancellationTokenSource(TimeSpan.FromMinutes(30));
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, overallTimeoutCts.Token);
        var effectiveToken = linkedCts.Token;

        if (!MapAssetManager.Storage.CheckFreeDiskSpaceAcceptingDownloads())
        {
            GD.PrintErr("[MapDistributionClient] Download aborted: Insufficient disk space on target storage drive (< 1% free).");
            return false;
        }

        var lm = GetLobbyManager();
        List<string> serverUrls = lm != null && lm.OfficialServers.Count > 0
            ? lm.OfficialServers
            : new List<string> { registryServerUrl };

        if (!serverUrls.Contains(registryServerUrl, StringComparer.OrdinalIgnoreCase))
        {
            serverUrls.Add(registryServerUrl);
        }

        foreach (var serverUrl in serverUrls)
        {
            if (effectiveToken.IsCancellationRequested) break;

            try
            {
                string baseUrl = serverUrl.TrimEnd('/');
                var distClient = new Realm.Shared.Distribution.DistributionClient(baseUrl, _httpClient);
                var manifest = await distClient.GetManifestAsync(mapId, effectiveToken);
                if (manifest != null && manifest.Files != null && manifest.Files.Count > 0)
                {
                    string version = !string.IsNullOrWhiteSpace(manifest.Version) ? manifest.Version.Trim() : "1.0.0";
                    string manifestMapName = !string.IsNullOrWhiteSpace(manifest.MapName) ? manifest.MapName.Trim() : mapId;
                    string manifestBlake3 = MapAssetManager.ComputeManifestBlake3(manifest);
                    string localMapDir = MapAssetManager.GetMapDirectory(manifestMapName, version, manifestBlake3, false);
                    if (!Directory.Exists(localMapDir))
                    {
                        Directory.CreateDirectory(localMapDir);
                    }

                    string localManifestPath = Path.Combine(localMapDir, "manifest.json");
                    await File.WriteAllTextAsync(localManifestPath, manifest.ToJson(), effectiveToken);

                    var seeders = await distClient.GetActiveSeedersAsync(effectiveToken);
                    bool httpSuccess = await distClient.DownloadMissingAssetsMultiThreadedAsync(
                        manifest,
                        MapAssetManager.Storage,
                        seeders,
                        fallbackHostUrl: baseUrl,
                        progressCallback: p =>
                        {
                            progressCallback?.Invoke(p);
                            DownloadProgressChanged?.Invoke(p);
                        },
                        maximumConcurrency: 12,
                        cancellationToken: effectiveToken,
                        onAssetReady: (virtualPath, assetKey, normalizedHash) =>
                        {
                            MapAssetManager.ExtractSingleAsset(virtualPath, normalizedHash, localMapDir, isP2P: false);
                        });

                    if (httpSuccess)
                    {
                        MapAssetManager.ExtractManifestFiles(manifest, localMapDir, isP2P: false);
                        AssetIndexService.Instance.RegisterManifest(manifest, localManifestPath, isP2P: false);
                        progressCallback?.Invoke(1.0f);
                        DownloadProgressChanged?.Invoke(1.0f);
                        return true;
                    }
                }
            }
            catch (Exception ex)
            {
                GD.PrintErr($"[MapDistributionClient] HTTP server download error from {serverUrl}: {ex.Message}");
            }
        }

        if (lm == null)
        {
            return false;
        }

        var candidateSeeders = new List<(string IP, int Port)>();
        try
        {
            string baseUrl = registryServerUrl.TrimEnd('/');
            using var requestMessage = new HttpRequestMessage(HttpMethod.Post, $"{baseUrl}/seeders/download");
            var payload = new { MapId = mapId };
            requestMessage.Content = new StringContent(JsonSerializer.Serialize(payload), System.Text.Encoding.UTF8, "application/json");

            var response = await _httpClient.SendAsync(requestMessage, cancellationToken);
            if (response.IsSuccessStatusCode)
            {
                string json = await response.Content.ReadAsStringAsync(cancellationToken);
                using var doc = JsonDocument.Parse(json);
                if (doc.RootElement.TryGetProperty("seeders", out var seedersElem) && seedersElem.ValueKind == JsonValueKind.Array)
                {
                    foreach (var seeder in seedersElem.EnumerateArray())
                    {
                        string ip = seeder.GetProperty("ip").GetString() ?? seeder.GetProperty("IP").GetString() ?? "";
                        int port = seeder.GetProperty("port").GetInt32();
                        if (!string.IsNullOrEmpty(ip) && port > 0)
                        {
                            candidateSeeders.Add((ip, port));
                        }
                    }
                }
            }
        }
        catch (Exception ex)
        {
            GD.PrintErr($"[MapDistributionClient] Seeder query error: {ex.Message}");
        }

        var rng = new Random();
        candidateSeeders = candidateSeeders.OrderBy(_ => rng.Next()).ToList();

        foreach (var (seederIp, seederPort) in candidateSeeders)
        {
            if (cancellationToken.IsCancellationRequested) break;

            bool ok = await lm.DownloadMapEphemerallyAsync(seederIp, seederPort, mapId, p =>
            {
                progressCallback?.Invoke(p);
                DownloadProgressChanged?.Invoke(p);
            }, cancellationToken);

            if (ok) return true;
        }

        return false;
    }
}
