using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Realm.Shared.Distribution;
using Realm.Shared.Metadata;
namespace Realm.AdminServer.Services;

public class ClusterEventService
{
    private readonly ConcurrentDictionary<string, byte> _processedEvents = new(StringComparer.OrdinalIgnoreCase);
    private static readonly JsonSerializerOptions JsonOpts = new() { PropertyNameCaseInsensitive = true };
    private readonly System.Threading.SemaphoreSlim _pruneLock = new(1, 1);
    private int _prunePending = 0;

    public bool HasEventBeenProcessed(string eventId, DataStoreService db)
    {
        if (string.IsNullOrWhiteSpace(eventId)) return false;
        if (_processedEvents.ContainsKey(eventId)) return true;
        var existing = db.Get<JsonDocument>("processed_events", eventId);
        if (existing != null)
        {
            _processedEvents.TryAdd(eventId, 0);
            return true;
        }
        return false;
    }

    public void MarkEventProcessed(string eventId, DataStoreService db)
    {
        if (string.IsNullOrWhiteSpace(eventId)) return;
        _processedEvents.TryAdd(eventId, 0);
        var doc = JsonSerializer.SerializeToDocument(new { eventId, processedAt = DateTime.UtcNow });
        db.Upsert("processed_events", eventId, doc);
    }

    public void RecordEvent(ClusterEventDto evt, DataStoreService db)
    {
        if (string.IsNullOrWhiteSpace(evt.EventId)) return;
        db.Upsert("cluster_events", evt.EventId, evt);
        MarkEventProcessed(evt.EventId, db);
    }

    public List<ClusterEventDto> GetEvents(DateTime? sinceUtc, int limit, DataStoreService db)
    {
        var allEvents = db.GetAll<ClusterEventDto>("cluster_events");
        var query = allEvents.AsEnumerable();
        if (sinceUtc.HasValue)
        {
            query = query.Where(e => e.TimestampUtc >= sinceUtc.Value);
        }
        return query
            .OrderBy(e => e.TimestampUtc)
            .Take(Math.Clamp(limit, 1, 1000))
            .ToList();
    }

    public void BroadcastEvent(ClusterEventDto evt, PeerRegistry peerRegistry, IHttpClientFactory httpClientFactory)
    {
        if (peerRegistry.PeerUrls.Count == 0) return;

        evt.OriginServerUrl = peerRegistry.SelfUrl;
        string json = JsonSerializer.Serialize(evt, JsonOpts);

        _ = Task.Run(async () =>
        {
            using var client = httpClientFactory.CreateClient();
            foreach (var peerUrl in peerRegistry.PeerUrls)
            {
                if (!string.IsNullOrWhiteSpace(evt.OriginServerUrl) &&
                    string.Equals(peerUrl.TrimEnd('/'), evt.OriginServerUrl.TrimEnd('/'), StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                try
                {
                    var content = new StringContent(json, Encoding.UTF8, "application/json");
                    var response = await client.PostAsync($"{peerUrl.TrimEnd('/')}/api/cluster/event", content);
                    if (response.IsSuccessStatusCode)
                    {
                        Console.WriteLine($"[ClusterEventService] Replicated event '{evt.EventType}' ({evt.EventId}) to peer {peerUrl}");
                    }
                    else
                    {
                        Console.WriteLine($"[ClusterEventService] Failed to replicate event '{evt.EventType}' ({evt.EventId}) to peer {peerUrl}: {response.StatusCode}");
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[ClusterEventService] Error replicating event '{evt.EventType}' ({evt.EventId}) to peer {peerUrl}: {ex.Message}");
                }
            }
        });
    }

    public async Task RunCatchUpSyncAsync(PeerRegistry peerRegistry, IHttpClientFactory httpClientFactory, DataStoreService db, ContentAddressableStorage cas, HashSet<string> adminPublicKeys)
    {
        if (peerRegistry.PeerUrls.Count == 0) return;

        var allEvents = db.GetAll<ClusterEventDto>("cluster_events").ToList();
        DateTime? latestUtc = allEvents.Count > 0 ? allEvents.Max(e => e.TimestampUtc) : null;

        using var client = httpClientFactory.CreateClient();
        foreach (var peerUrl in peerRegistry.PeerUrls)
        {
            try
            {
                var distClient = new DistributionClient(peerUrl, client);
                var events = await distClient.GetClusterEventsAsync(latestUtc, 200);
                foreach (var evt in events)
                {
                    await ApplyEventAsync(evt, db, cas, adminPublicKeys);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[ClusterEventService] Error catching up from peer {peerUrl}: {ex.Message}");
            }
        }
    }

    public async Task<bool> ApplyEventAsync(ClusterEventDto evt, DataStoreService db, ContentAddressableStorage cas, HashSet<string> adminPublicKeys)
    {
        if (string.IsNullOrWhiteSpace(evt.EventId) || string.IsNullOrWhiteSpace(evt.EventType))
        {
            return false;
        }

        if (HasEventBeenProcessed(evt.EventId, db))
        {
            return true;
        }

        bool success = evt.EventType.ToLowerInvariant() switch
        {
            "map_published" => ApplyMapPublished(evt, db, cas),
            "creator_registered" => ApplyCreatorRegistered(evt, db, adminPublicKeys),
            "admin_greenlight" => ApplyAdminGreenlight(evt, db, adminPublicKeys),
            "admin_remove_manifest" => ApplyAdminRemoveManifest(evt, db, cas, adminPublicKeys),
            "map_maintainers_updated" or "map_maintainer_updated" => ApplyMapMaintainersUpdated(evt, db, adminPublicKeys),
            "map_metric_report" => ApplyMapMetricReport(evt, db),
            _ => false
        };

        if (success)
        {
            RecordEvent(evt, db);
        }

        return await Task.FromResult(success);
    }

    public ClusterStateDigestDto ComputeStateDigest(DataStoreService db)
    {
        var colHashes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var colCounts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        var creators = db.GetAllWithKeys<JsonDocument>("creators");
        colCounts["creators"] = creators.Count;
        var creatorSb = new StringBuilder();
        foreach (var key in creators.Keys.OrderBy(k => k, StringComparer.Ordinal))
        {
            var root = creators[key].RootElement;
            string un = root.TryGetProperty("username", out var u) ? u.GetString() ?? "" : "";
            string pk = root.TryGetProperty("public_key", out var p) ? p.GetString() ?? "" : "";
            string dl = root.TryGetProperty("donation_link", out var d) ? d.GetString() ?? "" : "";
            string ci = root.TryGetProperty("contact_info", out var c) ? c.GetString() ?? "" : "";
            creatorSb.Append($"{key}:{un}:{pk}:{dl}:{ci};");
        }
        colHashes["creators"] = RealmMetadataHelper.ComputeBlake3(Encoding.UTF8.GetBytes(creatorSb.ToString()), ".txt");

        var nameLocks = db.GetAllWithKeys<JsonDocument>("name_locks");
        colCounts["name_locks"] = nameLocks.Count;
        var lockSb = new StringBuilder();
        foreach (var key in nameLocks.Keys.OrderBy(k => k, StringComparer.Ordinal))
        {
            var root = nameLocks[key].RootElement;
            string un = root.TryGetProperty("username", out var u) ? u.GetString() ?? "" : "";
            string op = root.TryGetProperty("owner_public_key", out var o) ? o.GetString() ?? "" : "";
            lockSb.Append($"{key}:{un}:{op};");
        }
        colHashes["name_locks"] = RealmMetadataHelper.ComputeBlake3(Encoding.UTF8.GetBytes(lockSb.ToString()), ".txt");

        var mapOwnership = db.GetAllWithKeys<string>("map_ownership");
        colCounts["map_ownership"] = mapOwnership.Count;
        var ownSb = new StringBuilder();
        foreach (var key in mapOwnership.Keys.OrderBy(k => k, StringComparer.Ordinal))
        {
            ownSb.Append($"{key}:{mapOwnership[key]};");
        }
        colHashes["map_ownership"] = RealmMetadataHelper.ComputeBlake3(Encoding.UTF8.GetBytes(ownSb.ToString()), ".txt");

        var mapMaintainers = db.GetAllWithKeys<List<string>>("map_maintainers");
        colCounts["map_maintainers"] = mapMaintainers.Count;
        var maintSb = new StringBuilder();
        foreach (var key in mapMaintainers.Keys.OrderBy(k => k, StringComparer.Ordinal))
        {
            var sortedM = (mapMaintainers[key] ?? new List<string>()).OrderBy(m => m, StringComparer.Ordinal);
            maintSb.Append($"{key}:{string.Join(",", sortedM)};");
        }
        colHashes["map_maintainers"] = RealmMetadataHelper.ComputeBlake3(Encoding.UTF8.GetBytes(maintSb.ToString()), ".txt");

        var publishedMaps = db.GetAllWithKeys<JsonDocument>("published_maps");
        colCounts["published_maps"] = publishedMaps.Count;
        var mapSb = new StringBuilder();
        foreach (var key in publishedMaps.Keys.OrderBy(k => k, StringComparer.Ordinal))
        {
            string json = publishedMaps[key].RootElement.GetRawText();
            mapSb.Append($"{key}:{json};");
        }
        colHashes["published_maps"] = RealmMetadataHelper.ComputeBlake3(Encoding.UTF8.GetBytes(mapSb.ToString()), ".txt");

        var mapStats = db.GetAllWithKeys<MapStats>("map_stats");
        colCounts["map_stats"] = mapStats.Count;
        var statSb = new StringBuilder();
        foreach (var key in mapStats.Keys.OrderBy(k => k, StringComparer.Ordinal))
        {
            var st = mapStats[key];
            statSb.Append($"{key}:{st.TotalPlaytimeMinutes:F1}:{st.TotalGamesPlayed}:{st.ReviewsCount}:{st.TotalStars}:{st.VerifiedGoodReviewsCount}:{st.AdminOverrideGreenlit};");
        }
        colHashes["map_stats"] = RealmMetadataHelper.ComputeBlake3(Encoding.UTF8.GetBytes(statSb.ToString()), ".txt");

        var assetSignatures = db.GetAllWithKeys<JsonDocument>("asset_signatures");
        colCounts["asset_signatures"] = assetSignatures.Count;
        var sigSb = new StringBuilder();
        foreach (var key in assetSignatures.Keys.OrderBy(k => k, StringComparer.Ordinal))
        {
            var root = assetSignatures[key].RootElement;
            string h = root.TryGetProperty("Hash", out var hp) ? hp.GetString() ?? "" : "";
            string a = root.TryGetProperty("AuthorUsername", out var ap) ? ap.GetString() ?? "" : "";
            string s = root.TryGetProperty("Signature", out var sp) ? sp.GetString() ?? "" : "";
            string pk = root.TryGetProperty("PublicKey", out var pkp) ? pkp.GetString() ?? "" : "";
            sigSb.Append($"{key}:{h}:{a}:{s}:{pk};");
        }
        colHashes["asset_signatures"] = RealmMetadataHelper.ComputeBlake3(Encoding.UTF8.GetBytes(sigSb.ToString()), ".txt");

        var playerEngagements = db.GetAllWithKeys<PlayerMapEngagement>("player_engagement");
        colCounts["player_engagement"] = playerEngagements.Count;
        var engSb = new StringBuilder();
        foreach (var key in playerEngagements.Keys.OrderBy(k => k, StringComparer.Ordinal))
        {
            var en = playerEngagements[key];
            engSb.Append($"{key}:{en.PlayerId}:{en.TotalPlaytimeMinutes:F1}:{en.GamesPlayed}:{en.SubmittedRating}:{en.IsVerifiedAccount};");
        }
        colHashes["player_engagement"] = RealmMetadataHelper.ComputeBlake3(Encoding.UTF8.GetBytes(engSb.ToString()), ".txt");

        var rootSb = new StringBuilder();
        foreach (var colName in colHashes.Keys.OrderBy(c => c, StringComparer.Ordinal))
        {
            rootSb.Append($"{colName}:{colHashes[colName]};");
        }
        string rootDigest = RealmMetadataHelper.ComputeBlake3(Encoding.UTF8.GetBytes(rootSb.ToString()), ".txt");

        return new ClusterStateDigestDto
        {
            StateDigest = rootDigest,
            CollectionHashes = colHashes,
            CollectionCounts = colCounts,
            ServerTimestampUtc = DateTime.UtcNow
        };
    }

    public ClusterSnapshotDto GenerateSnapshot(DataStoreService db)
    {
        var digest = ComputeStateDigest(db);

        var creators = new List<CreatorSyncDto>();
        foreach (var pair in db.GetAllWithKeys<JsonDocument>("creators"))
        {
            var root = pair.Value.RootElement;
            creators.Add(new CreatorSyncDto
            {
                PublicKey = pair.Key,
                Username = root.TryGetProperty("username", out var un) ? un.GetString() ?? "" : "",
                DonationLink = root.TryGetProperty("donation_link", out var dl) ? dl.GetString() ?? "" : "",
                ContactInfo = root.TryGetProperty("contact_info", out var ci) ? ci.GetString() ?? "" : ""
            });
        }

        var publishedMaps = new List<PublishedMapSyncDto>();
        foreach (var pair in db.GetAllWithKeys<JsonDocument>("published_maps"))
        {
            publishedMaps.Add(new PublishedMapSyncDto
            {
                MapId = pair.Key,
                ManifestJson = pair.Value.RootElement.GetRawText()
            });
        }

        var mapStats = new List<MapStatsSyncDto>();
        foreach (var pair in db.GetAllWithKeys<MapStats>("map_stats"))
        {
            var st = pair.Value;
            mapStats.Add(new MapStatsSyncDto
            {
                MapId = pair.Key,
                TotalPlaytimeMinutes = (int)Math.Round(st.TotalPlaytimeMinutes),
                TotalGamesPlayed = st.TotalGamesPlayed,
                TotalReviewsCount = st.ReviewsCount,
                VerifiedGoodReviewsCount = st.VerifiedGoodReviewsCount,
                AverageRating = st.AverageRating,
                AdminOverrideGreenlit = st.AdminOverrideGreenlit
            });
        }

        var nameLocks = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var pair in db.GetAllWithKeys<JsonDocument>("name_locks"))
        {
            nameLocks[pair.Key] = pair.Value.RootElement.GetRawText();
        }

        var assetSignatures = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var pair in db.GetAllWithKeys<JsonDocument>("asset_signatures"))
        {
            assetSignatures[pair.Key] = pair.Value.RootElement.GetRawText();
        }

        var playerEngagement = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var pair in db.GetAllWithKeys<PlayerMapEngagement>("player_engagement"))
        {
            playerEngagement[pair.Key] = JsonSerializer.Serialize(pair.Value, JsonOpts);
        }

        return new ClusterSnapshotDto
        {
            StateDigest = digest.StateDigest,
            GeneratedAtUtc = DateTime.UtcNow,
            Creators = creators,
            PublishedMaps = publishedMaps,
            MapOwnership = db.GetAllWithKeys<string>("map_ownership"),
            MapMaintainers = db.GetAllWithKeys<List<string>>("map_maintainers"),
            MapStats = mapStats,
            NameLocks = nameLocks,
            AssetSignatures = assetSignatures,
            PlayerEngagement = playerEngagement
        };
    }

    public ApplySnapshotResponseDto ApplySnapshot(ClusterSnapshotDto snapshot, DataStoreService db, ContentAddressableStorage cas, HashSet<string> adminPublicKeys)
    {
        if (snapshot == null)
        {
            return new ApplySnapshotResponseDto { Success = false, Message = "Snapshot was null." };
        }

        int creatorsCount = 0;
        int mapsCount = 0;
        int statsCount = 0;

        foreach (var creator in snapshot.Creators)
        {
            if (string.IsNullOrWhiteSpace(creator.PublicKey)) continue;
            var creatorDoc = JsonSerializer.SerializeToDocument(new
            {
                username = creator.Username,
                public_key = creator.PublicKey,
                donation_link = creator.DonationLink ?? "",
                contact_info = creator.ContactInfo ?? "",
                registered_at = creator.RegisteredAt ?? DateTime.UtcNow
            });
            db.Upsert("creators", creator.PublicKey, creatorDoc);
            creatorsCount++;
        }

        foreach (var pair in snapshot.NameLocks)
        {
            try
            {
                var doc = JsonDocument.Parse(pair.Value);
                db.Upsert("name_locks", pair.Key, doc);
            }
            catch { }
        }

        foreach (var pair in snapshot.MapOwnership)
        {
            db.Upsert("map_ownership", pair.Key, pair.Value);
        }

        if (snapshot.MapMaintainers != null)
        {
            foreach (var pair in snapshot.MapMaintainers)
            {
                db.Upsert("map_maintainers", pair.Key, pair.Value);
            }
        }

        string manifestDir = Path.Combine(cas.RootDirectory, "manifests");
        if (!Directory.Exists(manifestDir)) Directory.CreateDirectory(manifestDir);

        foreach (var map in snapshot.PublishedMaps)
        {
            if (string.IsNullOrWhiteSpace(map.MapId) || string.IsNullOrWhiteSpace(map.ManifestJson)) continue;
            try
            {
                var doc = JsonDocument.Parse(map.ManifestJson);
                db.Upsert("published_maps", map.MapId, doc);

                string manifestPath = Path.Combine(manifestDir, $"{map.MapId}_manifest.json");
                File.WriteAllText(manifestPath, map.ManifestJson);

                if (!string.IsNullOrWhiteSpace(map.MapTitle))
                {
                    string defaultPath = Path.Combine(manifestDir, $"{map.MapTitle}_manifest.json");
                    File.WriteAllText(defaultPath, map.ManifestJson);
                }

                mapsCount++;
            }
            catch { }
        }

        foreach (var stat in snapshot.MapStats)
        {
            if (string.IsNullOrWhiteSpace(stat.MapId)) continue;
            var s = new MapStats
            {
                TotalPlaytimeMinutes = stat.TotalPlaytimeMinutes,
                TotalGamesPlayed = stat.TotalGamesPlayed,
                ReviewsCount = stat.TotalReviewsCount,
                VerifiedGoodReviewsCount = stat.VerifiedGoodReviewsCount,
                TotalStars = (int)Math.Round(stat.AverageRating * stat.TotalReviewsCount),
                AdminOverrideGreenlit = stat.AdminOverrideGreenlit
            };
            db.Upsert("map_stats", stat.MapId, s);
            statsCount++;
        }

        foreach (var pair in snapshot.AssetSignatures)
        {
            try
            {
                var doc = JsonDocument.Parse(pair.Value);
                db.Upsert("asset_signatures", pair.Key, doc);
            }
            catch { }
        }

        foreach (var pair in snapshot.PlayerEngagement)
        {
            try
            {
                var eng = JsonSerializer.Deserialize<PlayerMapEngagement>(pair.Value, JsonOpts);
                if (eng != null)
                {
                    db.Upsert("player_engagement", pair.Key, eng);
                }
            }
            catch { }
        }

        var localDigest = ComputeStateDigest(db);
        bool digestMatches = string.Equals(localDigest.StateDigest, snapshot.StateDigest, StringComparison.OrdinalIgnoreCase);

        return new ApplySnapshotResponseDto
        {
            Success = true,
            ComputedStateDigest = localDigest.StateDigest,
            ExpectedStateDigest = snapshot.StateDigest,
            CreatorsImported = creatorsCount,
            MapsImported = mapsCount,
            StatsImported = statsCount,
            Message = digestMatches ? "Snapshot applied successfully with matching state digest." : $"Snapshot applied with digest mismatch: computed {localDigest.StateDigest}, expected {snapshot.StateDigest}."
        };
    }

    public async Task RunQuorumSyncAsync(PeerRegistry peerRegistry, IHttpClientFactory httpClientFactory, DataStoreService db, ContentAddressableStorage cas, HashSet<string> adminPublicKeys)
    {
        if (peerRegistry.PeerUrls.Count == 0) return;

        var localDigest = ComputeStateDigest(db);
        var peerDigests = new ConcurrentDictionary<string, ClusterStateDigestDto>(StringComparer.OrdinalIgnoreCase);

        using var client = httpClientFactory.CreateClient();

        var pollTasks = peerRegistry.PeerUrls.Select(async peerUrl =>
        {
            try
            {
                var distClient = new DistributionClient(peerUrl, client);
                var digest = await distClient.GetStateDigestAsync();
                if (digest != null && !string.IsNullOrEmpty(digest.StateDigest))
                {
                    peerDigests[peerUrl] = digest;
                }
            }
            catch
            {
            }
        });

        await Task.WhenAll(pollTasks);

        if (peerDigests.Count == 0)
        {
            return;
        }

        var digestGroups = peerDigests.GroupBy(p => p.Value.StateDigest, StringComparer.OrdinalIgnoreCase)
            .OrderByDescending(g => g.Count())
            .ToList();

        if (digestGroups.Count == 0) return;

        var majorityGroup = digestGroups[0];
        string majorityDigest = majorityGroup.Key;
        int majorityCount = majorityGroup.Count();
        int totalResponding = peerDigests.Count;

        bool hasMajority = majorityCount > (totalResponding / 2);

        if (!hasMajority)
        {
            Console.WriteLine($"[QuorumSync] No clear majority state digest reached ({majorityCount}/{totalResponding} votes). Preserving current state.");
            return;
        }

        if (string.Equals(localDigest.StateDigest, majorityDigest, StringComparison.OrdinalIgnoreCase))
        {
            await RunCatchUpSyncAsync(peerRegistry, httpClientFactory, db, cas, adminPublicKeys);
            return;
        }

        Console.WriteLine($"[QuorumSync] Local state digest ({localDigest.StateDigest}) diverged from majority consensus ({majorityDigest}, {majorityCount}/{totalResponding} votes). Fetching snapshot...");

        var candidatePeer = majorityGroup.First().Key;
        try
        {
            var candidateClient = new DistributionClient(candidatePeer, client);
            var snapshot = await candidateClient.GetSnapshotAsync();
            if (snapshot != null)
            {
                var result = ApplySnapshot(snapshot, db, cas, adminPublicKeys);
                Console.WriteLine($"[QuorumSync] Self-healing snapshot applied from {candidatePeer}: {result.Message}");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[QuorumSync] Error applying snapshot from {candidatePeer}: {ex.Message}");
        }
    }

    public void PruneOldEvents(DataStoreService db, TimeSpan retentionWindow)
    {
        DateTime cutoffUtc = DateTime.UtcNow - retentionWindow;
        var allEvents = db.GetAll<ClusterEventDto>("cluster_events").ToList();
        int pruned = 0;
        foreach (var evt in allEvents)
        {
            if (evt.TimestampUtc < cutoffUtc)
            {
                db.Delete("cluster_events", evt.EventId);
                _processedEvents.TryRemove(evt.EventId, out _);
                pruned++;
            }
        }

        if (pruned > 0)
        {
            Console.WriteLine($"[ClusterEventService] Pruned {pruned} cluster events older than {retentionWindow.TotalHours:F0} hours.");
        }
    }

    public void QueueCasPrune(ContentAddressableStorage cas, DataStoreService db)
    {
        Interlocked.Exchange(ref _prunePending, 1);
        _ = Task.Run(async () =>
        {
            await _pruneLock.WaitAsync();
            try
            {
                if (Interlocked.Exchange(ref _prunePending, 0) == 1)
                {
                    PruneCas(cas, db);
                }
            }
            finally
            {
                _pruneLock.Release();
            }
        });
    }

    public CasPruneResponseDto PruneCas(ContentAddressableStorage cas, DataStoreService db)
    {
        int totalScanned = 0;
        int orphansPruned = 0;
        int corruptPruned = 0;
        long bytesFreed = 0;

        var referencedHashes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        var publishedMaps = db.GetAll<JsonDocument>("published_maps");
        foreach (var doc in publishedMaps)
        {
            try
            {
                var mf = MapManifest.LoadFromJson(doc.RootElement.GetRawText());
                if (mf != null)
                {
                    foreach (var hashVal in mf.Files.Values)
                    {
                        string h = ContentAddressableStorage.NormalizeBlake3Hash(hashVal);
                        if (!string.IsNullOrEmpty(h)) referencedHashes.Add(h);
                    }
                }
            }
            catch { }
        }

        var manifestDir = Path.Combine(cas.RootDirectory, "manifests");
        if (Directory.Exists(manifestDir))
        {
            foreach (var file in Directory.EnumerateFiles(manifestDir, "*.json"))
            {
                try
                {
                    string json = File.ReadAllText(file);
                    var mf = MapManifest.LoadFromJson(json);
                    if (mf != null)
                    {
                        foreach (var hashVal in mf.Files.Values)
                        {
                            string h = ContentAddressableStorage.NormalizeBlake3Hash(hashVal);
                            if (!string.IsNullOrEmpty(h)) referencedHashes.Add(h);
                        }
                    }
                }
                catch { }
            }
        }

        var scannedPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var searchLocations = new List<(string DirectoryPath, SearchOption Option)>();

        if (Directory.Exists(cas.AssetsDirectory))
        {
            searchLocations.Add((cas.AssetsDirectory, SearchOption.AllDirectories));
        }

        if (Directory.Exists(cas.RootDirectory))
        {
            searchLocations.Add((cas.RootDirectory, SearchOption.TopDirectoryOnly));
        }

        var signaturesToDelete = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var (directoryPath, option) in searchLocations)
        {
            if (!Directory.Exists(directoryPath)) continue;

            foreach (var filePath in Directory.EnumerateFiles(directoryPath, "*", option))
            {
                string fullPath = Path.GetFullPath(filePath);
                if (!scannedPaths.Add(fullPath)) continue;

                string fileName = Path.GetFileName(filePath);
                if (fileName.EndsWith(".json", StringComparison.OrdinalIgnoreCase)) continue;

                string normalizedHash = ContentAddressableStorage.NormalizeBlake3Hash(fileName);
                if (string.IsNullOrEmpty(normalizedHash) || normalizedHash.Length < 32) continue;

                totalScanned++;
                try
                {
                    var fileInfo = new FileInfo(filePath);
                    long fileSize = fileInfo.Length;

                    if (!referencedHashes.Contains(normalizedHash))
                    {
                        File.Delete(filePath);
                        cas.RemoveSidecarCache(normalizedHash);
                        signaturesToDelete.Add(normalizedHash);
                        orphansPruned++;
                        bytesFreed += fileSize;
                        continue;
                    }

                    byte[] bytes = File.ReadAllBytes(filePath);
                    string ext = Path.GetExtension(fileName).ToLowerInvariant();
                    string computedHash = ContentAddressableStorage.NormalizeBlake3Hash(RealmMetadataHelper.ComputeBlake3(bytes, ext));

                    if (!string.Equals(computedHash, normalizedHash, StringComparison.OrdinalIgnoreCase))
                    {
                        File.Delete(filePath);
                        cas.RemoveSidecarCache(normalizedHash);
                        signaturesToDelete.Add(normalizedHash);
                        corruptPruned++;
                        bytesFreed += fileSize;
                    }
                }
                catch { }
            }
        }

        var allSignatures = db.GetAllWithKeys<JsonDocument>("asset_signatures");
        foreach (var pair in allSignatures)
        {
            string normalizedHash = ContentAddressableStorage.NormalizeBlake3Hash(pair.Key);
            if (!referencedHashes.Contains(normalizedHash) || !cas.HasAsset(normalizedHash))
            {
                signaturesToDelete.Add(pair.Key);
            }
        }

        if (signaturesToDelete.Count > 0)
        {
            db.DeleteMany("asset_signatures", signaturesToDelete);
        }

        if (Directory.Exists(cas.SidecarCacheDirectory))
        {
            foreach (var sidecarFile in Directory.EnumerateFiles(cas.SidecarCacheDirectory, "*.json", SearchOption.AllDirectories))
            {
                try
                {
                    string sidecarHash = ContentAddressableStorage.NormalizeBlake3Hash(Path.GetFileName(sidecarFile));
                    if (!referencedHashes.Contains(sidecarHash) || !cas.HasAsset(sidecarHash))
                    {
                        File.Delete(sidecarFile);
                    }
                }
                catch { }
            }
        }

        CleanEmptySubdirectories(cas.AssetsDirectory);
        CleanEmptySubdirectories(cas.SidecarCacheDirectory);

        return new CasPruneResponseDto
        {
            Success = true,
            TotalScanned = totalScanned,
            OrphansPruned = orphansPruned,
            CorruptPruned = corruptPruned,
            BytesFreed = bytesFreed,
            Message = $"Prune completed: {totalScanned} scanned, {orphansPruned} orphans deleted, {corruptPruned} corrupt files deleted, {bytesFreed} bytes freed."
        };
    }

    private static void CleanEmptySubdirectories(string rootDirectory)
    {
        if (!Directory.Exists(rootDirectory)) return;

        try
        {
            foreach (var subDirectory in Directory.EnumerateDirectories(rootDirectory))
            {
                if (!Directory.EnumerateFileSystemEntries(subDirectory).Any())
                {
                    try
                    {
                        Directory.Delete(subDirectory);
                    }
                    catch { }
                }
            }
        }
        catch { }
    }

    private bool ApplyMapPublished(ClusterEventDto evt, DataStoreService db, ContentAddressableStorage cas)
    {
        MapPublishedEventPayload? payload = null;
        if (!string.IsNullOrWhiteSpace(evt.PayloadJson))
        {
            try
            {
                payload = JsonSerializer.Deserialize<MapPublishedEventPayload>(evt.PayloadJson, JsonOpts);
            }
            catch { }
        }

        string manifestJson = payload?.ManifestJson ?? "";
        string publicKey = payload?.PublicKey ?? evt.PublicKey;
        string signature = payload?.Signature ?? evt.Signature;
        string mapTitle = payload?.MapTitle ?? "";
        string mapVersion = payload?.MapVersion ?? "1.0";

        if (string.IsNullOrWhiteSpace(manifestJson) || string.IsNullOrWhiteSpace(publicKey) || string.IsNullOrWhiteSpace(signature))
        {
            return false;
        }

        if (string.IsNullOrWhiteSpace(mapTitle))
        {
            try
            {
                using var doc = JsonDocument.Parse(manifestJson);
                if (doc.RootElement.TryGetProperty("MapName", out var mn)) mapTitle = mn.GetString() ?? "";
                if (doc.RootElement.TryGetProperty("Version", out var mv)) mapVersion = mv.GetString() ?? "1.0";
            }
            catch { }
        }

        if (string.IsNullOrWhiteSpace(mapTitle))
        {
            return false;
        }

        byte[] mapBytes = Encoding.UTF8.GetBytes(manifestJson);
        string canonicalBlake3 = RealmMetadataHelper.ComputeBlake3(mapBytes, ".json");
        string mapHashStr = $"{canonicalBlake3}.json";

        bool sigValid = AuthorSignatureHelper.VerifySignature(publicKey, mapHashStr, signature)
                     || AuthorSignatureHelper.VerifySignature(publicKey, canonicalBlake3, signature)
                     || AuthorSignatureHelper.VerifySignature(publicKey, manifestJson, signature);

        if (!sigValid)
        {
            Console.WriteLine($"[ClusterEventService] Rejected map_published event: Invalid signature for key {publicKey}");
            return false;
        }

        if (!MapMaintainerHelper.IsAuthorizedMaintainer(db, mapTitle, publicKey))
        {
            Console.WriteLine($"[ClusterEventService] Rejected map_published event: Map '{mapTitle}' is owned by another key");
            return false;
        }

        string compositeKey = $"{mapTitle}_{mapVersion}";
        var stats = MapStatsHelper.GetStats(db, mapTitle, mapVersion, publicKey);
        if (stats == null || !stats.IsGreenlit)
        {
            Console.WriteLine($"[ClusterEventService] Rejected map_published event: Map '{mapTitle}' is not greenlit");
            return false;
        }

        string manifestDir = Path.Combine(cas.RootDirectory, "manifests");
        if (!Directory.Exists(manifestDir)) Directory.CreateDirectory(manifestDir);

        string manifestPath = Path.Combine(manifestDir, $"{compositeKey}_manifest.json");
        File.WriteAllText(manifestPath, manifestJson);
        string defaultPath = Path.Combine(manifestDir, $"{mapTitle}_manifest.json");
        File.WriteAllText(defaultPath, manifestJson);

        try
        {
            var doc = JsonDocument.Parse(manifestJson);
            db.Upsert("published_maps", compositeKey, doc);
            db.Upsert("published_maps", mapTitle, doc);
            MapMaintainerHelper.AddMaintainer(db, mapTitle, publicKey);
        }
        catch
        {
            return false;
        }

        return true;
    }

    private bool ApplyMapMaintainersUpdated(ClusterEventDto evt, DataStoreService db, HashSet<string> adminPublicKeys)
    {
        MapMaintainersUpdatedEventPayload? payload = null;
        if (!string.IsNullOrWhiteSpace(evt.PayloadJson))
        {
            try
            {
                payload = JsonSerializer.Deserialize<MapMaintainersUpdatedEventPayload>(evt.PayloadJson, JsonOpts);
            }
            catch { }
        }

        string mapTitle = payload?.MapTitle ?? "";
        string action = (payload?.Action ?? "add").ToLowerInvariant();
        string maintainerPublicKey = payload?.MaintainerPublicKey ?? "";
        string requesterPublicKey = payload?.RequesterPublicKey ?? evt.PublicKey;
        string signature = payload?.Signature ?? evt.Signature;

        if (string.IsNullOrWhiteSpace(mapTitle) || string.IsNullOrWhiteSpace(maintainerPublicKey) || string.IsNullOrWhiteSpace(requesterPublicKey) || string.IsNullOrWhiteSpace(signature))
        {
            return false;
        }

        bool isAdmin = adminPublicKeys.Count > 0 && adminPublicKeys.Contains(requesterPublicKey.Trim());
        bool isMaintainer = MapMaintainerHelper.IsAuthorizedMaintainer(db, mapTitle, requesterPublicKey);

        if (!isAdmin && !isMaintainer)
        {
            Console.WriteLine($"[ClusterEventService] Rejected map_maintainers_updated: Key {requesterPublicKey} is neither admin nor authorized maintainer of '{mapTitle}'");
            return false;
        }

        string canonicalPayload = $"{action}_maintainer:{mapTitle.ToLowerInvariant()}:{maintainerPublicKey.Trim()}";
        string fallbackPayload1 = $"{action}_maintainer:{mapTitle}:{maintainerPublicKey}";
        string fallbackPayload2 = $"{action}:{mapTitle}:{maintainerPublicKey}";

        bool sigValid = AuthorSignatureHelper.VerifySignature(requesterPublicKey.Trim(), canonicalPayload, signature)
                     || AuthorSignatureHelper.VerifySignature(requesterPublicKey.Trim(), fallbackPayload1, signature)
                     || AuthorSignatureHelper.VerifySignature(requesterPublicKey.Trim(), fallbackPayload2, signature)
                     || (!string.IsNullOrWhiteSpace(evt.PayloadJson) && AuthorSignatureHelper.VerifySignature(requesterPublicKey.Trim(), evt.PayloadJson, signature));

        if (!sigValid)
        {
            Console.WriteLine($"[ClusterEventService] Rejected map_maintainers_updated: Invalid signature for requester {requesterPublicKey}");
            return false;
        }

        if (action == "remove")
        {
            MapMaintainerHelper.RemoveMaintainer(db, mapTitle, maintainerPublicKey);
        }
        else
        {
            MapMaintainerHelper.AddMaintainer(db, mapTitle, maintainerPublicKey);
        }

        return true;
    }

    private bool ApplyCreatorRegistered(ClusterEventDto evt, DataStoreService db, HashSet<string> adminPublicKeys)
    {
        CreatorRegisteredEventPayload? payload = null;
        if (!string.IsNullOrWhiteSpace(evt.PayloadJson))
        {
            try
            {
                payload = JsonSerializer.Deserialize<CreatorRegisteredEventPayload>(evt.PayloadJson, JsonOpts);
            }
            catch { }
        }

        string username = payload?.Username ?? "";
        string publicKey = payload?.PublicKey ?? evt.PublicKey;
        string signature = payload?.Signature ?? evt.Signature;
        string? donationLink = payload?.DonationLink;
        string? contactInfo = payload?.ContactInfo;
        string? adminBypassToken = payload?.AdminBypassToken;

        if (string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(publicKey) || string.IsNullOrWhiteSpace(signature))
        {
            return false;
        }

        bool sigValid = AuthorSignatureHelper.VerifySignature(publicKey, $"{username}:{publicKey}", signature)
                     || AuthorSignatureHelper.VerifySignature(publicKey, username, signature);

        if (!sigValid)
        {
            Console.WriteLine($"[ClusterEventService] Rejected creator_registered event: Invalid signature for {username}");
            return false;
        }

        string slug = username.ToLowerInvariant().Replace(" ", "-");
        var existingLock = db.Get<JsonDocument>("name_locks", slug);
        if (existingLock != null)
        {
            var root = existingLock.RootElement;
            string owner = root.TryGetProperty("owner_public_key", out var op) ? op.GetString() ?? "" : "";
            if (!string.Equals(owner, publicKey, StringComparison.OrdinalIgnoreCase))
            {
                bool bypassValid = !string.IsNullOrEmpty(adminBypassToken)
                    && adminPublicKeys.Count > 0
                    && AdminBypassAuth.VerifyBypassToken(adminPublicKeys, "admin", "override", adminBypassToken);

                if (!bypassValid)
                {
                    Console.WriteLine($"[ClusterEventService] Rejected creator_registered event: Username '{username}' already registered");
                    return false;
                }
            }
        }

        var lockDoc = JsonSerializer.SerializeToDocument(new
        {
            username = username,
            owner_public_key = publicKey,
            registered_at = payload?.RegisteredAt ?? DateTime.UtcNow
        });
        db.Upsert("name_locks", slug, lockDoc);

        var creatorDoc = JsonSerializer.SerializeToDocument(new
        {
            username = username,
            public_key = publicKey,
            donation_link = donationLink ?? "",
            contact_info = contactInfo ?? "",
            registered_at = payload?.RegisteredAt ?? DateTime.UtcNow
        });
        db.Upsert("creators", publicKey, creatorDoc);

        return true;
    }

    private bool ApplyAdminGreenlight(ClusterEventDto evt, DataStoreService db, HashSet<string> adminPublicKeys)
    {
        AdminGreenlightEventPayload? payload = null;
        if (!string.IsNullOrWhiteSpace(evt.PayloadJson))
        {
            try
            {
                payload = JsonSerializer.Deserialize<AdminGreenlightEventPayload>(evt.PayloadJson, JsonOpts);
            }
            catch { }
        }

        string mapTitle = payload?.MapTitle ?? "";
        string? mapVersion = payload?.MapVersion;
        string adminPublicKey = payload?.AdminPublicKey ?? evt.PublicKey;
        string signature = payload?.Signature ?? evt.Signature;

        if (string.IsNullOrWhiteSpace(mapTitle) || string.IsNullOrWhiteSpace(adminPublicKey) || string.IsNullOrWhiteSpace(signature))
        {
            return false;
        }

        if (adminPublicKeys.Count == 0 || !adminPublicKeys.Contains(adminPublicKey.Trim()))
        {
            Console.WriteLine($"[ClusterEventService] Rejected admin_greenlight event: Unauthorized admin key {adminPublicKey}");
            return false;
        }

        string sigPayload = $"greenlight:{mapTitle.ToLowerInvariant()}";
        string oldSigPayload = !string.IsNullOrWhiteSpace(mapVersion) ? $"greenlight:{mapTitle.ToLowerInvariant()}:{mapVersion.ToLowerInvariant()}" : sigPayload;
        if (!AuthorSignatureHelper.VerifySignature(adminPublicKey.Trim(), sigPayload, signature) &&
            !AuthorSignatureHelper.VerifySignature(adminPublicKey.Trim(), oldSigPayload, signature))
        {
            Console.WriteLine($"[ClusterEventService] Rejected admin_greenlight event: Invalid admin signature");
            return false;
        }

        var stats = MapStatsHelper.GetStats(db, mapTitle, mapVersion, null) ?? new MapStats();
        stats.AdminOverrideGreenlit = true;
        db.Upsert("map_stats", mapTitle, stats);
        db.Upsert("map_stats", mapTitle.ToLowerInvariant(), stats);

        if (!string.IsNullOrWhiteSpace(mapVersion))
        {
            string compositeKey = $"{mapTitle}_{mapVersion.Trim()}";
            db.Upsert("map_stats", compositeKey, stats);
            db.Upsert("map_stats", compositeKey.ToLowerInvariant(), stats);
        }

        return true;
    }

    private bool ApplyAdminRemoveManifest(ClusterEventDto evt, DataStoreService db, ContentAddressableStorage cas, HashSet<string> adminPublicKeys)
    {
        AdminRemoveManifestEventPayload? payload = null;
        if (!string.IsNullOrWhiteSpace(evt.PayloadJson))
        {
            try
            {
                payload = JsonSerializer.Deserialize<AdminRemoveManifestEventPayload>(evt.PayloadJson, JsonOpts);
            }
            catch { }
        }

        string mapTitle = payload?.MapTitle ?? "";
        string? mapVersion = payload?.MapVersion;
        string adminPublicKey = payload?.AdminPublicKey ?? evt.PublicKey;
        string signature = payload?.Signature ?? evt.Signature;

        if (string.IsNullOrWhiteSpace(mapTitle))
        {
            return false;
        }

        if (adminPublicKeys.Count > 0)
        {
            string targetVer = mapVersion ?? "all";
            string sigPayload1 = $"remove_manifest:{mapTitle.Trim().ToLowerInvariant()}:{targetVer.ToLowerInvariant()}";
            string sigPayload2 = $"remove_manifest:{mapTitle.Trim().ToLowerInvariant()}";

            bool isSigValid = false;
            if (!string.IsNullOrWhiteSpace(adminPublicKey) && adminPublicKeys.Contains(adminPublicKey.Trim()))
            {
                isSigValid = AuthorSignatureHelper.VerifySignature(adminPublicKey.Trim(), sigPayload1, signature)
                    || AuthorSignatureHelper.VerifySignature(adminPublicKey.Trim(), sigPayload2, signature)
                    || AdminBypassAuth.VerifyBypassToken(adminPublicKey.Trim(), mapTitle, targetVer, signature);
            }
            else
            {
                isSigValid = AuthorSignatureHelper.VerifySignatureAny(adminPublicKeys, sigPayload1, signature)
                    || AuthorSignatureHelper.VerifySignatureAny(adminPublicKeys, sigPayload2, signature)
                    || AdminBypassAuth.VerifyBypassToken(adminPublicKeys, mapTitle, targetVer, signature);
            }

            if (!isSigValid)
            {
                Console.WriteLine($"[ClusterEventService] Rejected admin_remove_manifest event: Invalid admin signature for {mapTitle}");
                return false;
            }
        }

        var result = RemoveManifest(cas, db, mapTitle, mapVersion);
        return result.Success;
    }

    public RemoveManifestResponseDto RemoveManifest(ContentAddressableStorage cas, DataStoreService db, string mapTitle, string? mapVersion = null)
    {
        if (string.IsNullOrWhiteSpace(mapTitle))
        {
            return new RemoveManifestResponseDto
            {
                Success = false,
                Message = "Map title cannot be empty."
            };
        }

        string targetMap = mapTitle.Trim();
        string? targetVersion = string.IsNullOrWhiteSpace(mapVersion) ? null : mapVersion.Trim();
        bool allVersions = targetVersion == null;

        int manifestsDeleted = 0;
        int dbRecordsRemoved = 0;
        var deletedFiles = new List<string>();

        string manifestDir = Path.Combine(cas.RootDirectory, "manifests");

        if (!allVersions)
        {
            string compositeKey = $"{targetMap}_{targetVersion}";
            var candidateNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                $"{compositeKey}_manifest.json",
                $"{compositeKey}.json",
                $"{compositeKey.Replace(' ', '_')}_manifest.json",
                $"{compositeKey.Replace(' ', '_')}.json",
                $"{compositeKey.Replace('_', ' ')}_manifest.json",
                $"{compositeKey.Replace('_', ' ')}.json"
            };

            if (Directory.Exists(manifestDir))
            {
                foreach (var filePath in Directory.EnumerateFiles(manifestDir, "*.json"))
                {
                    string fileName = Path.GetFileName(filePath);
                    bool shouldDelete = candidateNames.Contains(fileName);

                    if (!shouldDelete)
                    {
                        try
                        {
                            var mf = MapManifest.LoadFromFile(filePath);
                            if (mf != null &&
                                (string.Equals(mf.MapName, targetMap, StringComparison.OrdinalIgnoreCase) ||
                                 string.Equals(mf.MapName?.Replace(' ', '_'), targetMap.Replace(' ', '_'), StringComparison.OrdinalIgnoreCase)) &&
                                string.Equals(mf.Version, targetVersion, StringComparison.OrdinalIgnoreCase))
                            {
                                shouldDelete = true;
                            }
                        }
                        catch { }
                    }

                    if (shouldDelete)
                    {
                        try
                        {
                            File.Delete(filePath);
                            manifestsDeleted++;
                            deletedFiles.Add(fileName);
                        }
                        catch { }
                    }
                }
            }

            var allPublished = db.GetAllWithKeys<JsonDocument>("published_maps");
            var publishedKeysToDelete = new List<string>();
            foreach (var pair in allPublished)
            {
                bool match = string.Equals(pair.Key, compositeKey, StringComparison.OrdinalIgnoreCase)
                    || string.Equals(pair.Key.Replace(' ', '_'), compositeKey.Replace(' ', '_'), StringComparison.OrdinalIgnoreCase);

                if (!match)
                {
                    try
                    {
                        var root = pair.Value.RootElement;
                        string name = root.TryGetProperty("MapName", out var mn) ? mn.GetString() ?? "" : "";
                        string ver = root.TryGetProperty("Version", out var vr) ? vr.GetString() ?? "" : "";
                        if ((string.Equals(name, targetMap, StringComparison.OrdinalIgnoreCase) ||
                             string.Equals(name.Replace(' ', '_'), targetMap.Replace(' ', '_'), StringComparison.OrdinalIgnoreCase)) &&
                            string.Equals(ver, targetVersion, StringComparison.OrdinalIgnoreCase))
                        {
                            match = true;
                        }
                    }
                    catch { }
                }

                if (match)
                {
                    publishedKeysToDelete.Add(pair.Key);
                }
            }

            if (publishedKeysToDelete.Count > 0)
            {
                db.DeleteMany("published_maps", publishedKeysToDelete);
                dbRecordsRemoved += publishedKeysToDelete.Count;
            }

            var remainingVersions = new List<(string version, JsonDocument doc, string? filePath)>();
            if (Directory.Exists(manifestDir))
            {
                foreach (var filePath in Directory.EnumerateFiles(manifestDir, "*.json"))
                {
                    try
                    {
                        var mf = MapManifest.LoadFromFile(filePath);
                        if (mf != null &&
                            (string.Equals(mf.MapName, targetMap, StringComparison.OrdinalIgnoreCase) ||
                             string.Equals(mf.MapName?.Replace(' ', '_'), targetMap.Replace(' ', '_'), StringComparison.OrdinalIgnoreCase)) &&
                            !string.Equals(mf.Version, targetVersion, StringComparison.OrdinalIgnoreCase))
                        {
                            var doc = JsonDocument.Parse(File.ReadAllText(filePath));
                            remainingVersions.Add((mf.Version ?? "1.0", doc, filePath));
                        }
                    }
                    catch { }
                }
            }

            var remainingDb = db.GetAllWithKeys<JsonDocument>("published_maps");
            foreach (var pair in remainingDb)
            {
                if (string.Equals(pair.Key, targetMap, StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(pair.Key.Replace(' ', '_'), targetMap.Replace(' ', '_'), StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                try
                {
                    var root = pair.Value.RootElement;
                    string name = root.TryGetProperty("MapName", out var mn) ? mn.GetString() ?? "" : "";
                    string ver = root.TryGetProperty("Version", out var vr) ? vr.GetString() ?? "" : "";
                    if ((string.Equals(name, targetMap, StringComparison.OrdinalIgnoreCase) ||
                         string.Equals(name.Replace(' ', '_'), targetMap.Replace(' ', '_'), StringComparison.OrdinalIgnoreCase)) &&
                        !string.Equals(ver, targetVersion, StringComparison.OrdinalIgnoreCase))
                    {
                        if (!remainingVersions.Any(r => string.Equals(r.version, ver, StringComparison.OrdinalIgnoreCase)))
                        {
                            remainingVersions.Add((ver, pair.Value, null));
                        }
                    }
                }
                catch { }
            }

            if (remainingVersions.Count > 0)
            {
                var latest = remainingVersions.OrderByDescending(r => r.version, StringComparer.OrdinalIgnoreCase).First();
                string defaultManifestPath = Path.Combine(manifestDir, $"{targetMap}_manifest.json");
                try
                {
                    File.WriteAllText(defaultManifestPath, JsonSerializer.Serialize(latest.doc));
                }
                catch { }
                db.Upsert("published_maps", targetMap, latest.doc);
            }
            else
            {
                if (Directory.Exists(manifestDir))
                {
                    var unversionedCandidates = new List<string>
                    {
                        Path.Combine(manifestDir, $"{targetMap}_manifest.json"),
                        Path.Combine(manifestDir, $"{targetMap}.json"),
                        Path.Combine(manifestDir, $"{targetMap.Replace(' ', '_')}_manifest.json"),
                        Path.Combine(manifestDir, $"{targetMap.Replace(' ', '_')}.json")
                    };
                    foreach (var unvPath in unversionedCandidates)
                    {
                        if (File.Exists(unvPath))
                        {
                            try
                            {
                                File.Delete(unvPath);
                                manifestsDeleted++;
                                deletedFiles.Add(Path.GetFileName(unvPath));
                            }
                            catch { }
                        }
                    }
                }

                db.DeleteMany("published_maps", new[] { targetMap, targetMap.Replace(' ', '_'), targetMap.Replace('_', ' ') });
            }

            db.Delete("map_stats", compositeKey);
        }
        else
        {
            if (Directory.Exists(manifestDir))
            {
                foreach (var filePath in Directory.EnumerateFiles(manifestDir, "*.json"))
                {
                    string fileName = Path.GetFileName(filePath);
                    bool shouldDelete = fileName.StartsWith($"{targetMap}_", StringComparison.OrdinalIgnoreCase)
                        || fileName.StartsWith($"{targetMap.Replace(' ', '_')}_", StringComparison.OrdinalIgnoreCase)
                        || string.Equals(fileName, $"{targetMap}.json", StringComparison.OrdinalIgnoreCase)
                        || string.Equals(fileName, $"{targetMap.Replace(' ', '_')}.json", StringComparison.OrdinalIgnoreCase);

                    if (!shouldDelete)
                    {
                        try
                        {
                            var mf = MapManifest.LoadFromFile(filePath);
                            if (mf != null &&
                                (string.Equals(mf.MapName, targetMap, StringComparison.OrdinalIgnoreCase) ||
                                 string.Equals(mf.MapName?.Replace(' ', '_'), targetMap.Replace(' ', '_'), StringComparison.OrdinalIgnoreCase)))
                            {
                                shouldDelete = true;
                            }
                        }
                        catch { }
                    }

                    if (shouldDelete)
                    {
                        try
                        {
                            File.Delete(filePath);
                            manifestsDeleted++;
                            deletedFiles.Add(fileName);
                        }
                        catch { }
                    }
                }
            }

            var allPublished = db.GetAllWithKeys<JsonDocument>("published_maps");
            var publishedKeysToDelete = new List<string>();
            foreach (var pair in allPublished)
            {
                bool match = string.Equals(pair.Key, targetMap, StringComparison.OrdinalIgnoreCase)
                    || string.Equals(pair.Key.Replace(' ', '_'), targetMap.Replace(' ', '_'), StringComparison.OrdinalIgnoreCase)
                    || pair.Key.StartsWith($"{targetMap}_", StringComparison.OrdinalIgnoreCase)
                    || pair.Key.StartsWith($"{targetMap.Replace(' ', '_')}_", StringComparison.OrdinalIgnoreCase);

                if (!match)
                {
                    try
                    {
                        var root = pair.Value.RootElement;
                        string name = root.TryGetProperty("MapName", out var mn) ? mn.GetString() ?? "" : "";
                        if (string.Equals(name, targetMap, StringComparison.OrdinalIgnoreCase) ||
                            string.Equals(name.Replace(' ', '_'), targetMap.Replace(' ', '_'), StringComparison.OrdinalIgnoreCase))
                        {
                            match = true;
                        }
                    }
                    catch { }
                }

                if (match)
                {
                    publishedKeysToDelete.Add(pair.Key);
                }
            }

            if (publishedKeysToDelete.Count > 0)
            {
                db.DeleteMany("published_maps", publishedKeysToDelete);
                dbRecordsRemoved += publishedKeysToDelete.Count;
            }

            db.DeleteMany("map_ownership", new[] { targetMap, targetMap.ToLowerInvariant().Replace(" ", "-"), targetMap.Replace(" ", "_") });
            db.DeleteMany("map_maintainers", new[] { targetMap, targetMap.ToLowerInvariant().Replace(" ", "-"), targetMap.Replace(" ", "_") });
            db.Delete("map_stats", targetMap);
        }

        return new RemoveManifestResponseDto
        {
            Success = true,
            MapTitle = targetMap,
            MapVersion = targetVersion,
            AllVersionsRemoved = allVersions,
            ManifestsDeleted = manifestsDeleted,
            DbRecordsRemoved = dbRecordsRemoved,
            DeletedManifestFiles = deletedFiles,
            Message = allVersions
                ? $"Successfully removed all published manifest versions for map '{targetMap}'."
                : $"Successfully removed published manifest for map '{targetMap}' version '{targetVersion}'."
        };
    }

    private bool ApplyMapMetricReport(ClusterEventDto evt, DataStoreService db)
    {
        MapMetricReportEventPayload? payload = null;
        if (!string.IsNullOrWhiteSpace(evt.PayloadJson))
        {
            try
            {
                payload = JsonSerializer.Deserialize<MapMetricReportEventPayload>(evt.PayloadJson, JsonOpts);
            }
            catch { }
        }

        if (payload == null || string.IsNullOrWhiteSpace(payload.MapTitle))
        {
            return false;
        }

        string mapTitle = payload.MapTitle.Trim();
        string mapVersion = string.IsNullOrWhiteSpace(payload.MapVersion) ? "1.0" : payload.MapVersion.Trim();
        string authorPublicKey = payload.AuthorPublicKey?.Trim() ?? "";
        string compositeKey = $"{mapTitle}_{mapVersion}";
        string authorCompositeKey = !string.IsNullOrEmpty(authorPublicKey) ? $"{mapTitle}_{mapVersion}_{authorPublicKey}" : compositeKey;
        string playerIdentifier = !string.IsNullOrWhiteSpace(payload.PlayerId) ? payload.PlayerId.Trim() : "Anonymous";

        string engagementKey = !string.IsNullOrEmpty(authorPublicKey)
            ? $"{playerIdentifier}_{authorCompositeKey}".ToLowerInvariant()
            : $"{playerIdentifier}_{compositeKey}".ToLowerInvariant();
        var engagement = db.Get<PlayerMapEngagement>("player_engagement", engagementKey)
            ?? new PlayerMapEngagement { PlayerId = playerIdentifier };

        bool wasVerifiedGood = engagement.IsVerifiedGoodReview;
        bool hadSubmittedRating = engagement.SubmittedRating.HasValue;
        int prevRating = engagement.SubmittedRating ?? 0;

        engagement.TotalPlaytimeMinutes += Math.Max(0.0, payload.PlaytimeMinutes);
        if (payload.IsCompleteGame)
        {
            engagement.GamesPlayed += 1;
        }
        if (!string.IsNullOrWhiteSpace(payload.AuthProvider) && !payload.AuthProvider.Equals("guest", StringComparison.OrdinalIgnoreCase))
        {
            engagement.IsVerifiedAccount = true;
        }

        var stats = MapStatsHelper.GetStats(db, mapTitle, mapVersion, authorPublicKey) ?? new MapStats();

        stats.TotalPlaytimeMinutes += Math.Max(0.0, payload.PlaytimeMinutes);
        if (payload.IsCompleteGame)
        {
            stats.TotalGamesPlayed += 1;
        }

        if (payload.Stars >= 1 && payload.Stars <= 5)
        {
            if (hadSubmittedRating)
            {
                stats.TotalStars += (payload.Stars - prevRating);
            }
            else
            {
                stats.ReviewsCount += 1;
                stats.TotalStars += payload.Stars;
            }
            engagement.SubmittedRating = payload.Stars;
        }

        bool isNowVerifiedGood = engagement.IsVerifiedGoodReview;
        if (!wasVerifiedGood && isNowVerifiedGood)
        {
            stats.VerifiedGoodReviewsCount += 1;
        }
        else if (wasVerifiedGood && !isNowVerifiedGood)
        {
            stats.VerifiedGoodReviewsCount = Math.Max(0, stats.VerifiedGoodReviewsCount - 1);
        }

        db.Upsert("player_engagement", engagementKey, engagement);
        db.Upsert("map_stats", authorCompositeKey, stats);
        db.Upsert("map_stats", authorCompositeKey.ToLowerInvariant(), stats);
        if (!string.IsNullOrEmpty(authorPublicKey))
        {
            db.Upsert("map_stats", $"{mapTitle}_{authorPublicKey}", stats);
            db.Upsert("map_stats", $"{mapTitle}_{authorPublicKey}".ToLowerInvariant(), stats);
        }
        if (stats.IsGreenlit)
        {
            db.Upsert("map_stats", compositeKey, stats);
            db.Upsert("map_stats", mapTitle, stats);
            db.Upsert("map_stats", compositeKey.ToLowerInvariant(), stats);
            db.Upsert("map_stats", mapTitle.ToLowerInvariant(), stats);
        }

        return true;
    }
}
