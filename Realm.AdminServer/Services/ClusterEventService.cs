using Realm.Shared;
using Realm.Shared.Distribution;
using Realm.Shared.Metadata;
using System.Collections.Concurrent;
using System.Text;
using System.Text.Json;
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

        bool success = RouteEvent(evt, db, cas, adminPublicKeys);

        if (success)
        {
            RecordEvent(evt, db);
        }

        return await Task.FromResult(success);
    }

    private bool RouteEvent(ClusterEventDto evt, DataStoreService db, ContentAddressableStorage cas, HashSet<string> adminPublicKeys)
    {
        return evt.EventType.ToLowerInvariant() switch
        {
            "map_published" => ApplyMapPublished(evt, db, cas),
            "creator_registered" => ApplyCreatorRegistered(evt, db, adminPublicKeys),
            "admin_greenlight" => ApplyAdminGreenlight(evt, db, adminPublicKeys),
            "admin_remove_manifest" => ApplyAdminRemoveManifest(evt, db, cas, adminPublicKeys),
            "map_maintainers_updated" or "map_maintainer_updated" => ApplyMapMaintainersUpdated(evt, db, adminPublicKeys),
            "map_metric_report" => ApplyMapMetricReport(evt, db),
            _ => false
        };
    }

    public ClusterStateDigestDto ComputeStateDigest(DataStoreService db)
    {
        var colHashes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var colCounts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        ComputeCreatorsDigest(db, colCounts, colHashes);
        ComputeNameLocksDigest(db, colCounts, colHashes);
        ComputeMapOwnershipDigest(db, colCounts, colHashes);
        ComputeMapMaintainersDigest(db, colCounts, colHashes);
        ComputePublishedMapsDigest(db, colCounts, colHashes);
        ComputeMapStatsDigest(db, colCounts, colHashes);
        ComputeAssetSignaturesDigest(db, colCounts, colHashes);
        ComputePlayerEngagementDigest(db, colCounts, colHashes);

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

    private void ComputeCreatorsDigest(DataStoreService db, Dictionary<string, int> colCounts, Dictionary<string, string> colHashes)
    {
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
    }

    private void ComputeNameLocksDigest(DataStoreService db, Dictionary<string, int> colCounts, Dictionary<string, string> colHashes)
    {
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
    }

    private void ComputeMapOwnershipDigest(DataStoreService db, Dictionary<string, int> colCounts, Dictionary<string, string> colHashes)
    {
        var mapOwnership = db.GetAllWithKeys<string>("map_ownership");
        colCounts["map_ownership"] = mapOwnership.Count;
        var ownSb = new StringBuilder();
        foreach (var key in mapOwnership.Keys.OrderBy(k => k, StringComparer.Ordinal))
        {
            ownSb.Append($"{key}:{mapOwnership[key]};");
        }
        colHashes["map_ownership"] = RealmMetadataHelper.ComputeBlake3(Encoding.UTF8.GetBytes(ownSb.ToString()), ".txt");
    }

    private void ComputeMapMaintainersDigest(DataStoreService db, Dictionary<string, int> colCounts, Dictionary<string, string> colHashes)
    {
        var mapMaintainers = db.GetAllWithKeys<List<string>>("map_maintainers");
        colCounts["map_maintainers"] = mapMaintainers.Count;
        var maintSb = new StringBuilder();
        foreach (var key in mapMaintainers.Keys.OrderBy(k => k, StringComparer.Ordinal))
        {
            var sortedM = (mapMaintainers[key] ?? new List<string>()).OrderBy(m => m, StringComparer.Ordinal);
            maintSb.Append($"{key}:{string.Join(",", sortedM)};");
        }
        colHashes["map_maintainers"] = RealmMetadataHelper.ComputeBlake3(Encoding.UTF8.GetBytes(maintSb.ToString()), ".txt");
    }

    private void ComputePublishedMapsDigest(DataStoreService db, Dictionary<string, int> colCounts, Dictionary<string, string> colHashes)
    {
        var publishedMaps = db.GetAllWithKeys<JsonDocument>("published_maps");
        colCounts["published_maps"] = publishedMaps.Count;
        var mapSb = new StringBuilder();
        foreach (var key in publishedMaps.Keys.OrderBy(k => k, StringComparer.Ordinal))
        {
            string json = publishedMaps[key].RootElement.GetRawText();
            mapSb.Append($"{key}:{json};");
        }
        colHashes["published_maps"] = RealmMetadataHelper.ComputeBlake3(Encoding.UTF8.GetBytes(mapSb.ToString()), ".txt");
    }

    private void ComputeMapStatsDigest(DataStoreService db, Dictionary<string, int> colCounts, Dictionary<string, string> colHashes)
    {
        var mapStats = db.GetAllWithKeys<MapStats>("map_stats");
        colCounts["map_stats"] = mapStats.Count;
        var statSb = new StringBuilder();
        foreach (var key in mapStats.Keys.OrderBy(k => k, StringComparer.Ordinal))
        {
            var st = mapStats[key];
            statSb.Append($"{key}:{st.TotalPlaytimeMinutes:F1}:{st.TotalGamesPlayed}:{st.ReviewsCount}:{st.TotalStars}:{st.VerifiedGoodReviewsCount}:{st.AdminOverrideGreenlit};");
        }
        colHashes["map_stats"] = RealmMetadataHelper.ComputeBlake3(Encoding.UTF8.GetBytes(statSb.ToString()), ".txt");
    }

    private void ComputeAssetSignaturesDigest(DataStoreService db, Dictionary<string, int> colCounts, Dictionary<string, string> colHashes)
    {
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
    }

    private void ComputePlayerEngagementDigest(DataStoreService db, Dictionary<string, int> colCounts, Dictionary<string, string> colHashes)
    {
        var playerEngagements = db.GetAllWithKeys<PlayerMapEngagement>("player_engagement");
        colCounts["player_engagement"] = playerEngagements.Count;
        var engSb = new StringBuilder();
        foreach (var key in playerEngagements.Keys.OrderBy(k => k, StringComparer.Ordinal))
        {
            var en = playerEngagements[key];
            engSb.Append($"{key}:{en.PlayerId}:{en.TotalPlaytimeMinutes:F1}:{en.GamesPlayed}:{en.SubmittedRating}:{en.IsVerifiedAccount};");
        }
        colHashes["player_engagement"] = RealmMetadataHelper.ComputeBlake3(Encoding.UTF8.GetBytes(engSb.ToString()), ".txt");
    }

    public ClusterSnapshotDto GenerateSnapshot(DataStoreService db)
    {
        var digest = ComputeStateDigest(db);

        return new ClusterSnapshotDto
        {
            StateDigest = digest.StateDigest,
            GeneratedAtUtc = DateTime.UtcNow,
            Creators = GenerateCreatorsSnapshot(db),
            PublishedMaps = GeneratePublishedMapsSnapshot(db),
            MapOwnership = db.GetAllWithKeys<string>("map_ownership"),
            MapMaintainers = db.GetAllWithKeys<List<string>>("map_maintainers"),
            MapStats = GenerateMapStatsSnapshot(db),
            NameLocks = GenerateNameLocksSnapshot(db),
            AssetSignatures = GenerateAssetSignaturesSnapshot(db),
            PlayerEngagement = GeneratePlayerEngagementSnapshot(db)
        };
    }

    private List<CreatorSyncDto> GenerateCreatorsSnapshot(DataStoreService db)
    {
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
        return creators;
    }

    private List<PublishedMapSyncDto> GeneratePublishedMapsSnapshot(DataStoreService db)
    {
        var publishedMaps = new List<PublishedMapSyncDto>();
        foreach (var pair in db.GetAllWithKeys<JsonDocument>("published_maps"))
        {
            publishedMaps.Add(new PublishedMapSyncDto
            {
                MapId = pair.Key,
                ManifestJson = pair.Value.RootElement.GetRawText()
            });
        }
        return publishedMaps;
    }

    private List<MapStatsSyncDto> GenerateMapStatsSnapshot(DataStoreService db)
    {
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
        return mapStats;
    }

    private Dictionary<string, string> GenerateNameLocksSnapshot(DataStoreService db)
    {
        var nameLocks = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var pair in db.GetAllWithKeys<JsonDocument>("name_locks"))
        {
            nameLocks[pair.Key] = pair.Value.RootElement.GetRawText();
        }
        return nameLocks;
    }

    private Dictionary<string, string> GenerateAssetSignaturesSnapshot(DataStoreService db)
    {
        var assetSignatures = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var pair in db.GetAllWithKeys<JsonDocument>("asset_signatures"))
        {
            assetSignatures[pair.Key] = pair.Value.RootElement.GetRawText();
        }
        return assetSignatures;
    }

    private Dictionary<string, string> GeneratePlayerEngagementSnapshot(DataStoreService db)
    {
        var playerEngagement = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var pair in db.GetAllWithKeys<PlayerMapEngagement>("player_engagement"))
        {
            playerEngagement[pair.Key] = JsonSerializer.Serialize(pair.Value, JsonOpts);
        }
        return playerEngagement;
    }

    public ApplySnapshotResponseDto ApplySnapshot(ClusterSnapshotDto snapshot, DataStoreService db, ContentAddressableStorage cas, HashSet<string> adminPublicKeys)
    {
        if (snapshot == null)
        {
            return new ApplySnapshotResponseDto { Success = false, Message = "Snapshot was null." };
        }

        int creatorsCount = ApplyCreatorsSnapshot(snapshot.Creators, db);
        ApplyNameLocksSnapshot(snapshot.NameLocks, db);
        ApplyMapOwnershipSnapshot(snapshot.MapOwnership, db);
        ApplyMapMaintainersSnapshot(snapshot.MapMaintainers, db);
        int mapsCount = ApplyPublishedMapsSnapshot(snapshot.PublishedMaps, db, cas);
        int statsCount = ApplyMapStatsSnapshot(snapshot.MapStats, db);
        ApplyAssetSignaturesSnapshot(snapshot.AssetSignatures, db);
        ApplyPlayerEngagementSnapshot(snapshot.PlayerEngagement, db);

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

    private int ApplyCreatorsSnapshot(IEnumerable<CreatorSyncDto> creators, DataStoreService db)
    {
        int creatorsCount = 0;
        foreach (var creator in creators)
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
        return creatorsCount;
    }

    private void ApplyNameLocksSnapshot(Dictionary<string, string> nameLocks, DataStoreService db)
    {
        foreach (var pair in nameLocks)
        {
            try
            {
                var doc = JsonDocument.Parse(pair.Value);
                db.Upsert("name_locks", pair.Key, doc);
            }
            catch { }
        }
    }

    private void ApplyMapOwnershipSnapshot(Dictionary<string, string> mapOwnership, DataStoreService db)
    {
        foreach (var pair in mapOwnership)
        {
            db.Upsert("map_ownership", pair.Key, pair.Value);
        }
    }

    private void ApplyMapMaintainersSnapshot(Dictionary<string, List<string>>? mapMaintainers, DataStoreService db)
    {
        if (mapMaintainers != null)
        {
            foreach (var pair in mapMaintainers)
            {
                db.Upsert("map_maintainers", pair.Key, pair.Value);
            }
        }
    }

    private int ApplyPublishedMapsSnapshot(IEnumerable<PublishedMapSyncDto> publishedMaps, DataStoreService db, ContentAddressableStorage cas)
    {
        int mapsCount = 0;
        string manifestDir = Path.Combine(cas.RootDirectory, "manifests");
        if (!Directory.Exists(manifestDir)) Directory.CreateDirectory(manifestDir);

        foreach (var map in publishedMaps)
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
        return mapsCount;
    }

    private int ApplyMapStatsSnapshot(IEnumerable<MapStatsSyncDto> mapStats, DataStoreService db)
    {
        int statsCount = 0;
        foreach (var stat in mapStats)
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
        return statsCount;
    }

    private void ApplyAssetSignaturesSnapshot(Dictionary<string, string> assetSignatures, DataStoreService db)
    {
        foreach (var pair in assetSignatures)
        {
            try
            {
                var doc = JsonDocument.Parse(pair.Value);
                db.Upsert("asset_signatures", pair.Key, doc);
            }
            catch { }
        }
    }

    private void ApplyPlayerEngagementSnapshot(Dictionary<string, string> playerEngagement, DataStoreService db)
    {
        foreach (var pair in playerEngagement)
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
    }

    public async Task RunQuorumSyncAsync(PeerRegistry peerRegistry, IHttpClientFactory httpClientFactory, DataStoreService db, ContentAddressableStorage cas, HashSet<string> adminPublicKeys)
    {
        if (peerRegistry.PeerUrls.Count == 0) return;

        var localDigest = ComputeStateDigest(db);
        using var client = httpClientFactory.CreateClient();

        var peerDigests = await PollPeerDigestsAsync(peerRegistry, client);
        if (peerDigests.Count == 0) return;

        var digestGroups = peerDigests.GroupBy(p => p.Value.StateDigest, StringComparer.OrdinalIgnoreCase)
            .OrderByDescending(g => g.Count())
            .ToList();

        if (digestGroups.Count == 0) return;

        var majorityGroup = digestGroups[0];
        string majorityDigest = majorityGroup.Key;
        int majorityCount = majorityGroup.Count();
        int totalResponding = peerDigests.Count;

        if (majorityCount <= (totalResponding / 2))
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
        await SelfHealFromPeerAsync(majorityGroup.First().Key, client, db, cas, adminPublicKeys);
    }

    private async Task<ConcurrentDictionary<string, ClusterStateDigestDto>> PollPeerDigestsAsync(PeerRegistry peerRegistry, HttpClient client)
    {
        var peerDigests = new ConcurrentDictionary<string, ClusterStateDigestDto>(StringComparer.OrdinalIgnoreCase);
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
            catch { }
        });
        await Task.WhenAll(pollTasks);
        return peerDigests;
    }

    private async Task SelfHealFromPeerAsync(string candidatePeer, HttpClient client, DataStoreService db, ContentAddressableStorage cas, HashSet<string> adminPublicKeys)
    {
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

        var referencedHashes = GetReferencedHashes(cas, db);
        var signaturesToDelete = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        PruneDirectories(cas, referencedHashes, signaturesToDelete, ref totalScanned, ref orphansPruned, ref corruptPruned, ref bytesFreed);
        PruneSignatures(db, cas, referencedHashes, signaturesToDelete);
        PruneSidecarCache(cas, referencedHashes);

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

    private HashSet<string> GetReferencedHashes(ContentAddressableStorage cas, DataStoreService db)
    {
        var referencedHashes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var doc in db.GetAll<JsonDocument>("published_maps"))
        {
            try
            {
                var mf = MapManifest.LoadFromJson(doc.RootElement.GetRawText());
                AddManifestHashes(mf, referencedHashes);
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
                    AddManifestHashes(mf, referencedHashes);
                }
                catch { }
            }
        }
        return referencedHashes;
    }

    private void AddManifestHashes(MapManifest? mf, HashSet<string> referencedHashes)
    {
        if (mf == null) return;
        foreach (var hashVal in mf.Files.Values)
        {
            string h = ContentAddressableStorage.NormalizeBlake3Hash(hashVal);
            if (!string.IsNullOrEmpty(h)) referencedHashes.Add(h);
        }
    }

    private void PruneDirectories(ContentAddressableStorage cas, HashSet<string> referencedHashes, HashSet<string> signaturesToDelete, ref int totalScanned, ref int orphansPruned, ref int corruptPruned, ref long bytesFreed)
    {
        var scannedPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var searchLocations = new List<(string DirectoryPath, SearchOption Option)>();

        if (Directory.Exists(cas.AssetsDirectory))
            searchLocations.Add((cas.AssetsDirectory, SearchOption.AllDirectories));
        if (Directory.Exists(cas.RootDirectory))
            searchLocations.Add((cas.RootDirectory, SearchOption.TopDirectoryOnly));

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
                PruneFile(cas, filePath, fileName, normalizedHash, referencedHashes, signaturesToDelete, ref orphansPruned, ref corruptPruned, ref bytesFreed);
            }
        }
    }

    private void PruneFile(ContentAddressableStorage cas, string filePath, string fileName, string normalizedHash, HashSet<string> referencedHashes, HashSet<string> signaturesToDelete, ref int orphansPruned, ref int corruptPruned, ref long bytesFreed)
    {
        try
        {
            var fileInfo = new FileInfo(filePath);
            long fileSize = fileInfo.Length;

            if (!referencedHashes.Contains(normalizedHash))
            {
                DeleteAssetFile(cas, filePath, normalizedHash, signaturesToDelete);
                orphansPruned++;
                bytesFreed += fileSize;
                return;
            }

            byte[] bytes = File.ReadAllBytes(filePath);
            string ext = Path.GetExtension(fileName).ToLowerInvariant();
            string computedHash = ContentAddressableStorage.NormalizeBlake3Hash(RealmMetadataHelper.ComputeBlake3(bytes, ext));

            if (!string.Equals(computedHash, normalizedHash, StringComparison.OrdinalIgnoreCase))
            {
                DeleteAssetFile(cas, filePath, normalizedHash, signaturesToDelete);
                corruptPruned++;
                bytesFreed += fileSize;
            }
        }
        catch { }
    }

    private void DeleteAssetFile(ContentAddressableStorage cas, string filePath, string normalizedHash, HashSet<string> signaturesToDelete)
    {
        File.Delete(filePath);
        cas.RemoveSidecarCache(normalizedHash);
        signaturesToDelete.Add(normalizedHash);
    }

    private void PruneSignatures(DataStoreService db, ContentAddressableStorage cas, HashSet<string> referencedHashes, HashSet<string> signaturesToDelete)
    {
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
    }

    private void PruneSidecarCache(ContentAddressableStorage cas, HashSet<string> referencedHashes)
    {
        if (!Directory.Exists(cas.SidecarCacheDirectory)) return;

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
        if (!TryGetMapPublishedContext(evt, db, out var manifestJson, out var mapTitle, out var mapVersion, out var publicKey, out var compositeKey))
            return false;

        return WriteManifestAndUpsert(manifestJson, mapTitle, mapVersion, compositeKey, publicKey, db, cas);
    }

    private bool TryGetMapPublishedContext(ClusterEventDto evt, DataStoreService db, out string manifestJson, out string mapTitle, out string mapVersion, out string publicKey, out string compositeKey)
    {
        PopulateMapPublishedValues(evt, out manifestJson, out mapTitle, out mapVersion, out publicKey, out var signature);

        if (!ValidateMapPublishedRequiredFields(manifestJson, publicKey, signature, ref mapTitle, ref mapVersion))
        {
            compositeKey = "";
            return false;
        }

        if (!ValidateMapPublishedAuthorization(db, mapTitle, publicKey))
        {
            compositeKey = "";
            return false;
        }

        compositeKey = $"{mapTitle}_{mapVersion}";
        return ValidateMapPublishedGreenlight(db, mapTitle, mapVersion, publicKey);
    }

    private void PopulateMapPublishedValues(ClusterEventDto evt, out string manifestJson, out string mapTitle, out string mapVersion, out string publicKey, out string signature)
    {
        manifestJson = "";
        publicKey = evt.PublicKey;
        signature = evt.Signature;
        mapTitle = "";
        mapVersion = "1.0";

        var payload = ParseMapPublishedPayload(evt);
        if (payload == null) return;

        if (payload.ManifestJson != null) manifestJson = payload.ManifestJson;
        if (payload.PublicKey != null) publicKey = payload.PublicKey;
        if (payload.Signature != null) signature = payload.Signature;
        if (payload.MapTitle != null) mapTitle = payload.MapTitle;
        if (payload.MapVersion != null) mapVersion = payload.MapVersion;
    }

    private bool ValidateMapPublishedRequiredFields(string manifestJson, string publicKey, string signature, ref string mapTitle, ref string mapVersion)
    {
        if (string.IsNullOrWhiteSpace(manifestJson) || string.IsNullOrWhiteSpace(publicKey) || string.IsNullOrWhiteSpace(signature))
            return false;

        if (string.IsNullOrWhiteSpace(mapTitle))
            ExtractMapDetailsFromManifest(manifestJson, ref mapTitle, ref mapVersion);

        if (string.IsNullOrWhiteSpace(mapTitle) || !VerifyMapPublishedSignature(manifestJson, publicKey, signature))
            return false;

        return true;
    }

    private bool ValidateMapPublishedAuthorization(DataStoreService db, string mapTitle, string publicKey)
    {
        if (!MapMaintainerHelper.IsAuthorizedMaintainer(db, mapTitle, publicKey))
        {
            Console.WriteLine($"[ClusterEventService] Rejected map_published event: Map '{mapTitle}' is owned by another key");
            return false;
        }
        return true;
    }

    private bool ValidateMapPublishedGreenlight(DataStoreService db, string mapTitle, string mapVersion, string publicKey)
    {
        var stats = MapStatsHelper.GetStats(db, mapTitle, mapVersion, publicKey);
        if (stats == null || !stats.IsGreenlit)
        {
            Console.WriteLine($"[ClusterEventService] Rejected map_published event: Map '{mapTitle}' is not greenlit");
            return false;
        }
        return true;
    }


    private MapPublishedEventPayload? ParseMapPublishedPayload(ClusterEventDto evt)
    {
        if (string.IsNullOrWhiteSpace(evt.PayloadJson)) return null;
        try
        {
            return JsonSerializer.Deserialize<MapPublishedEventPayload>(evt.PayloadJson, JsonOpts);
        }
        catch { return null; }
    }

    private void ExtractMapDetailsFromManifest(string manifestJson, ref string mapTitle, ref string mapVersion)
    {
        try
        {
            using var doc = JsonDocument.Parse(manifestJson);
            if (doc.RootElement.TryGetProperty("MapName", out var mn)) mapTitle = mn.GetString() ?? "";
            if (doc.RootElement.TryGetProperty("Version", out var mv)) mapVersion = mv.GetString() ?? "1.0";
        }
        catch { }
    }

    private bool VerifyMapPublishedSignature(string manifestJson, string publicKey, string signature)
    {
        byte[] mapBytes = Encoding.UTF8.GetBytes(manifestJson);
        string canonicalBlake3 = RealmMetadataHelper.ComputeBlake3(mapBytes, ".json");
        string mapHashStr = $"{canonicalBlake3}.json";

        bool sigValid = AuthorSignatureHelper.VerifySignature(publicKey, mapHashStr, signature)
                     || AuthorSignatureHelper.VerifySignature(publicKey, canonicalBlake3, signature)
                     || AuthorSignatureHelper.VerifySignature(publicKey, manifestJson, signature);

        if (!sigValid)
            Console.WriteLine($"[ClusterEventService] Rejected map_published event: Invalid signature for key {publicKey}");

        return sigValid;
    }

    private bool WriteManifestAndUpsert(string manifestJson, string mapTitle, string mapVersion, string compositeKey, string publicKey, DataStoreService db, ContentAddressableStorage cas)
    {
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
        if (!TryGetMaintainersContext(evt, db, adminPublicKeys, out var action, out var mapTitle, out var maintainerPublicKey))
            return false;

        if (action == "remove")
            MapMaintainerHelper.RemoveMaintainer(db, mapTitle, maintainerPublicKey);
        else
            MapMaintainerHelper.AddMaintainer(db, mapTitle, maintainerPublicKey);

        return true;
    }

    private bool TryGetMaintainersContext(ClusterEventDto evt, DataStoreService db, HashSet<string> adminPublicKeys, out string action, out string mapTitle, out string maintainerPublicKey)
    {
        PopulateMaintainersValues(evt, out action, out mapTitle, out maintainerPublicKey, out var requesterPublicKey, out var signature);

        if (!ValidateMaintainersRequiredFields(mapTitle, maintainerPublicKey, requesterPublicKey, signature))
            return false;

        if (!ValidateMaintainersAuthorization(db, adminPublicKeys, mapTitle, requesterPublicKey))
            return false;

        return VerifyMapMaintainersUpdatedSignature(action, mapTitle, maintainerPublicKey, requesterPublicKey, signature, evt.PayloadJson);
    }

    private void PopulateMaintainersValues(ClusterEventDto evt, out string action, out string mapTitle, out string maintainerPublicKey, out string requesterPublicKey, out string signature)
    {
        action = "add";
        mapTitle = "";
        maintainerPublicKey = "";
        requesterPublicKey = evt.PublicKey;
        signature = evt.Signature;

        var payload = ParseMapMaintainersUpdatedPayload(evt);
        if (payload == null) return;

        if (payload.Action != null) action = payload.Action.ToLowerInvariant();
        if (payload.MapTitle != null) mapTitle = payload.MapTitle;
        if (payload.MaintainerPublicKey != null) maintainerPublicKey = payload.MaintainerPublicKey;
        if (payload.RequesterPublicKey != null) requesterPublicKey = payload.RequesterPublicKey;
        if (payload.Signature != null) signature = payload.Signature;
    }

    private bool ValidateMaintainersRequiredFields(string mapTitle, string maintainerPublicKey, string requesterPublicKey, string signature)
    {
        return !string.IsNullOrWhiteSpace(mapTitle) 
            && !string.IsNullOrWhiteSpace(maintainerPublicKey) 
            && !string.IsNullOrWhiteSpace(requesterPublicKey) 
            && !string.IsNullOrWhiteSpace(signature);
    }

    private bool ValidateMaintainersAuthorization(DataStoreService db, HashSet<string> adminPublicKeys, string mapTitle, string requesterPublicKey)
    {
        bool isAdmin = adminPublicKeys.Count > 0 && adminPublicKeys.Contains(requesterPublicKey.Trim());
        bool isMaintainer = MapMaintainerHelper.IsAuthorizedMaintainer(db, mapTitle, requesterPublicKey);

        if (!isAdmin && !isMaintainer)
        {
            Console.WriteLine($"[ClusterEventService] Rejected map_maintainers_updated: Key {requesterPublicKey} is neither admin nor authorized maintainer of '{mapTitle}'");
            return false;
        }
        return true;
    }


    private MapMaintainersUpdatedEventPayload? ParseMapMaintainersUpdatedPayload(ClusterEventDto evt)
    {
        if (string.IsNullOrWhiteSpace(evt.PayloadJson)) return null;
        try
        {
            return JsonSerializer.Deserialize<MapMaintainersUpdatedEventPayload>(evt.PayloadJson, JsonOpts);
        }
        catch { return null; }
    }

    private bool VerifyMapMaintainersUpdatedSignature(string action, string mapTitle, string maintainerPublicKey, string requesterPublicKey, string signature, string? payloadJson)
    {
        string canonicalPayload = $"{action}_maintainer:{mapTitle.ToLowerInvariant()}:{maintainerPublicKey.Trim()}";
        string fallbackPayload1 = $"{action}_maintainer:{mapTitle}:{maintainerPublicKey}";
        string fallbackPayload2 = $"{action}:{mapTitle}:{maintainerPublicKey}";

        bool sigValid = AuthorSignatureHelper.VerifySignature(requesterPublicKey.Trim(), canonicalPayload, signature)
                     || AuthorSignatureHelper.VerifySignature(requesterPublicKey.Trim(), fallbackPayload1, signature)
                     || AuthorSignatureHelper.VerifySignature(requesterPublicKey.Trim(), fallbackPayload2, signature)
                     || (!string.IsNullOrWhiteSpace(payloadJson) && AuthorSignatureHelper.VerifySignature(requesterPublicKey.Trim(), payloadJson, signature));

        if (!sigValid)
            Console.WriteLine($"[ClusterEventService] Rejected map_maintainers_updated: Invalid signature for requester {requesterPublicKey}");

        return sigValid;
    }

    private bool ApplyCreatorRegistered(ClusterEventDto evt, DataStoreService db, HashSet<string> adminPublicKeys)
    {
        if (!TryGetCreatorContext(evt, db, adminPublicKeys, out var username, out var publicKey, out var payload, out var slug))
            return false;

        UpsertNameLock(db, slug, username, publicKey, payload?.RegisteredAt);
        UpsertCreatorInfo(db, username, publicKey, payload?.DonationLink, payload?.ContactInfo, payload?.RegisteredAt);

        return true;
    }

    private bool TryGetCreatorContext(ClusterEventDto evt, DataStoreService db, HashSet<string> adminPublicKeys, out string username, out string publicKey, out CreatorRegisteredEventPayload? payload, out string slug)
    {
        PopulateCreatorValues(evt, out username, out publicKey, out payload, out var signature);
        
        slug = "";
        
        if (!ValidateCreatorRequiredFields(username, publicKey, signature))
            return false;

        if (!VerifyCreatorRegisteredSignature(username, publicKey, signature))
            return false;

        slug = GetUsernameSlug(username);

        return ValidateNameLock(db, slug, username, publicKey, payload?.AdminBypassToken, adminPublicKeys);
    }

    private void PopulateCreatorValues(ClusterEventDto evt, out string username, out string publicKey, out CreatorRegisteredEventPayload? payload, out string signature)
    {
        payload = ParseCreatorRegisteredPayload(evt);
        username = payload?.Username ?? "";
        publicKey = payload?.PublicKey ?? evt.PublicKey;
        signature = payload?.Signature ?? evt.Signature;
    }

    private bool ValidateCreatorRequiredFields(string username, string publicKey, string signature)
    {
        return !string.IsNullOrWhiteSpace(username) 
            && !string.IsNullOrWhiteSpace(publicKey) 
            && !string.IsNullOrWhiteSpace(signature);
    }





    private CreatorRegisteredEventPayload? ParseCreatorRegisteredPayload(ClusterEventDto evt)
    {
        if (string.IsNullOrWhiteSpace(evt.PayloadJson)) return null;
        try
        {
            return JsonSerializer.Deserialize<CreatorRegisteredEventPayload>(evt.PayloadJson, JsonOpts);
        }
        catch { return null; }
    }

    private bool VerifyCreatorRegisteredSignature(string username, string publicKey, string signature)
    {
        bool sigValid = AuthorSignatureHelper.VerifySignature(publicKey, $"{username}:{publicKey}", signature)
                     || AuthorSignatureHelper.VerifySignature(publicKey, username, signature);

        if (!sigValid)
            Console.WriteLine($"[ClusterEventService] Rejected creator_registered event: Invalid signature for {username}");

        return sigValid;
    }

    private string GetUsernameSlug(string username)
    {
        string slug = NameNormalizationHelper.NormalizeUsername(username);
        if (string.IsNullOrEmpty(slug))
        {
            slug = username.ToLowerInvariant().Replace(" ", "-");
        }
        return slug;
    }

    private bool ValidateNameLock(DataStoreService db, string slug, string username, string publicKey, string? adminBypassToken, HashSet<string> adminPublicKeys)
    {
        var existingLock = db.Get<JsonDocument>("name_locks", slug)
            ?? db.Get<JsonDocument>("name_locks", username.ToLowerInvariant().Replace(" ", "-"));
        
        if (existingLock == null) return true;

        var root = existingLock.RootElement;
        string owner = root.TryGetProperty("owner_public_key", out var op) ? op.GetString() ?? "" : "";
        if (string.Equals(owner, publicKey, StringComparison.OrdinalIgnoreCase)) return true;

        bool bypassValid = !string.IsNullOrEmpty(adminBypassToken)
            && adminPublicKeys.Count > 0
            && AdminBypassAuth.VerifyBypassToken(adminPublicKeys, "admin", "override", adminBypassToken);

        if (!bypassValid)
        {
            Console.WriteLine($"[ClusterEventService] Rejected creator_registered event: Username '{username}' already registered");
            return false;
        }

        return true;
    }

    private void UpsertNameLock(DataStoreService db, string slug, string username, string publicKey, DateTime? registeredAt)
    {
        var lockDoc = JsonSerializer.SerializeToDocument(new
        {
            username = username,
            owner_public_key = publicKey,
            registered_at = registeredAt ?? DateTime.UtcNow
        });
        db.Upsert("name_locks", slug, lockDoc);
    }

    private void UpsertCreatorInfo(DataStoreService db, string username, string publicKey, string? donationLink, string? contactInfo, DateTime? registeredAt)
    {
        var creatorDoc = JsonSerializer.SerializeToDocument(new
        {
            username = username,
            public_key = publicKey,
            donation_link = donationLink ?? "",
            contact_info = contactInfo ?? "",
            registered_at = registeredAt ?? DateTime.UtcNow
        });
        db.Upsert("creators", publicKey, creatorDoc);
    }

    private bool ApplyAdminGreenlight(ClusterEventDto evt, DataStoreService db, HashSet<string> adminPublicKeys)
    {
        if (!TryGetAdminGreenlightContext(evt, adminPublicKeys, out var mapTitle, out var mapVersion))
            return false;

        UpsertAdminGreenlightStats(db, mapTitle, mapVersion);

        return true;
    }

    private bool TryGetAdminGreenlightContext(ClusterEventDto evt, HashSet<string> adminPublicKeys, out string mapTitle, out string? mapVersion)
    {
        PopulateAdminGreenlightValues(evt, out mapTitle, out mapVersion, out var adminPublicKey, out var signature);

        if (!ValidateAdminGreenlightRequiredFields(mapTitle, adminPublicKey, signature))
            return false;

        if (!ValidateAdminGreenlightAuthorization(adminPublicKeys, adminPublicKey))
            return false;

        return VerifyAdminGreenlightSignature(mapTitle, mapVersion, adminPublicKey, signature);
    }

    private void PopulateAdminGreenlightValues(ClusterEventDto evt, out string mapTitle, out string? mapVersion, out string adminPublicKey, out string signature)
    {
        var payload = ParseAdminGreenlightPayload(evt);
        mapTitle = payload?.MapTitle ?? "";
        mapVersion = payload?.MapVersion;
        adminPublicKey = payload?.AdminPublicKey ?? evt.PublicKey;
        signature = payload?.Signature ?? evt.Signature;
    }

    private bool ValidateAdminGreenlightRequiredFields(string mapTitle, string adminPublicKey, string signature)
    {
        return !string.IsNullOrWhiteSpace(mapTitle) 
            && !string.IsNullOrWhiteSpace(adminPublicKey) 
            && !string.IsNullOrWhiteSpace(signature);
    }

    private bool ValidateAdminGreenlightAuthorization(HashSet<string> adminPublicKeys, string adminPublicKey)
    {
        if (adminPublicKeys.Count == 0 || !adminPublicKeys.Contains(adminPublicKey.Trim()))
        {
            Console.WriteLine($"[ClusterEventService] Rejected admin_greenlight event: Unauthorized admin key {adminPublicKey}");
            return false;
        }
        return true;
    }


    private AdminGreenlightEventPayload? ParseAdminGreenlightPayload(ClusterEventDto evt)
    {
        if (string.IsNullOrWhiteSpace(evt.PayloadJson)) return null;
        try
        {
            return JsonSerializer.Deserialize<AdminGreenlightEventPayload>(evt.PayloadJson, JsonOpts);
        }
        catch { return null; }
    }

    private bool VerifyAdminGreenlightSignature(string mapTitle, string? mapVersion, string adminPublicKey, string signature)
    {
        string sigPayload = $"greenlight:{mapTitle.ToLowerInvariant()}";
        string oldSigPayload = !string.IsNullOrWhiteSpace(mapVersion) ? $"greenlight:{mapTitle.ToLowerInvariant()}:{mapVersion.ToLowerInvariant()}" : sigPayload;
        
        bool isValid = AuthorSignatureHelper.VerifySignature(adminPublicKey.Trim(), sigPayload, signature) ||
                       AuthorSignatureHelper.VerifySignature(adminPublicKey.Trim(), oldSigPayload, signature);

        if (!isValid)
            Console.WriteLine($"[ClusterEventService] Rejected admin_greenlight event: Invalid admin signature");

        return isValid;
    }

    private void UpsertAdminGreenlightStats(DataStoreService db, string mapTitle, string? mapVersion)
    {
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
    }

    private bool ApplyAdminRemoveManifest(ClusterEventDto evt, DataStoreService db, ContentAddressableStorage cas, HashSet<string> adminPublicKeys)
    {
        var payload = ParseAdminRemoveManifestPayload(evt);
        string mapTitle = payload?.MapTitle ?? "";
        string? mapVersion = payload?.MapVersion;
        string adminPublicKey = payload?.AdminPublicKey ?? evt.PublicKey;
        string signature = payload?.Signature ?? evt.Signature;

        if (string.IsNullOrWhiteSpace(mapTitle))
            return false;

        if (!VerifyAdminRemoveManifestSignature(mapTitle, mapVersion, adminPublicKey, signature, adminPublicKeys))
            return false;

        var result = RemoveManifest(cas, db, mapTitle, mapVersion);
        return result.Success;
    }

    private AdminRemoveManifestEventPayload? ParseAdminRemoveManifestPayload(ClusterEventDto evt)
    {
        if (string.IsNullOrWhiteSpace(evt.PayloadJson)) return null;
        try
        {
            return JsonSerializer.Deserialize<AdminRemoveManifestEventPayload>(evt.PayloadJson, JsonOpts);
        }
        catch { return null; }
    }

    private bool VerifyAdminRemoveManifestSignature(string mapTitle, string? mapVersion, string adminPublicKey, string signature, HashSet<string> adminPublicKeys)
    {
        if (adminPublicKeys.Count == 0) return true;

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
            Console.WriteLine($"[ClusterEventService] Rejected admin_remove_manifest event: Invalid admin signature for {mapTitle}");

        return isSigValid;
    }

    public RemoveManifestResponseDto RemoveManifest(ContentAddressableStorage cas, DataStoreService db, string mapTitle, string? mapVersion = null)
    {
        if (string.IsNullOrWhiteSpace(mapTitle))
            return new RemoveManifestResponseDto { Success = false, Message = "Map title cannot be empty." };

        string targetMap = mapTitle.Trim();
        string? targetVersion = string.IsNullOrWhiteSpace(mapVersion) ? null : mapVersion.Trim();
        bool allVersions = targetVersion == null;

        int manifestsDeleted = 0;
        int dbRecordsRemoved = 0;
        var deletedFiles = new List<string>();

        if (allVersions)
            RemoveAllManifestVersions(cas, db, targetMap, ref manifestsDeleted, ref dbRecordsRemoved, deletedFiles);
        else
            RemoveSpecificManifestVersion(cas, db, targetMap, targetVersion!, ref manifestsDeleted, ref dbRecordsRemoved, deletedFiles);

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

    private void RemoveAllManifestVersions(ContentAddressableStorage cas, DataStoreService db, string targetMap, ref int manifestsDeleted, ref int dbRecordsRemoved, List<string> deletedFiles)
    {
        string manifestDir = Path.Combine(cas.RootDirectory, "manifests");
        
        DeleteMatchingManifests(manifestDir, targetMap, ref manifestsDeleted, deletedFiles);
        RemovePublishedMapRecords(db, targetMap, ref dbRecordsRemoved);

        db.DeleteMany("map_ownership", new[] { targetMap, targetMap.ToLowerInvariant().Replace(" ", "-"), targetMap.Replace(" ", "_") });
        db.DeleteMany("map_maintainers", new[] { targetMap, targetMap.ToLowerInvariant().Replace(" ", "-"), targetMap.Replace(" ", "_") });
        db.Delete("map_stats", targetMap);
    }

    private void DeleteMatchingManifests(string manifestDir, string targetMap, ref int manifestsDeleted, List<string> deletedFiles)
    {
        if (!Directory.Exists(manifestDir)) return;

        foreach (var filePath in Directory.EnumerateFiles(manifestDir, "*.json"))
        {
            ProcessManifestForDeletion(filePath, targetMap, ref manifestsDeleted, deletedFiles);
        }
    }

    private void ProcessManifestForDeletion(string filePath, string targetMap, ref int manifestsDeleted, List<string> deletedFiles)
    {
        string fileName = Path.GetFileName(filePath);
        bool shouldDelete = fileName.StartsWith($"{targetMap}_", StringComparison.OrdinalIgnoreCase)
            || fileName.StartsWith($"{targetMap.Replace(' ', '_')}_", StringComparison.OrdinalIgnoreCase)
            || string.Equals(fileName, $"{targetMap}.json", StringComparison.OrdinalIgnoreCase)
            || string.Equals(fileName, $"{targetMap.Replace(' ', '_')}.json", StringComparison.OrdinalIgnoreCase);

        if (!shouldDelete)
            shouldDelete = ShouldDeleteManifestByContent(filePath, targetMap, null);

        if (shouldDelete)
            AttemptDeleteManifest(filePath, fileName, ref manifestsDeleted, deletedFiles);
    }

    private bool ShouldDeleteManifestByContent(string filePath, string targetMap, string? targetVersion)
    {
        try
        {
            var mf = MapManifest.LoadFromFile(filePath);
            if (mf == null) return false;

            bool nameMatch = string.Equals(mf.MapName, targetMap, StringComparison.OrdinalIgnoreCase) 
                || string.Equals(mf.MapName?.Replace(' ', '_'), targetMap.Replace(' ', '_'), StringComparison.OrdinalIgnoreCase);
            
            if (!nameMatch) return false;

            if (targetVersion != null && !string.Equals(mf.Version, targetVersion, StringComparison.OrdinalIgnoreCase))
                return false;

            return true;
        }
        catch { return false; }
    }

    private void AttemptDeleteManifest(string filePath, string fileName, ref int manifestsDeleted, List<string> deletedFiles)
    {
        try
        {
            File.Delete(filePath);
            manifestsDeleted++;
            deletedFiles.Add(fileName);
        }
        catch { }
    }

    private void RemovePublishedMapRecords(DataStoreService db, string targetMap, ref int dbRecordsRemoved)
    {
        var publishedKeysToDelete = new List<string>();
        foreach (var pair in db.GetAllWithKeys<JsonDocument>("published_maps"))
        {
            if (IsPublishedMapRecordMatch(pair, targetMap))
            {
                publishedKeysToDelete.Add(pair.Key);
            }
        }

        if (publishedKeysToDelete.Count > 0)
        {
            db.DeleteMany("published_maps", publishedKeysToDelete);
            dbRecordsRemoved += publishedKeysToDelete.Count;
        }
    }

    private bool IsPublishedMapRecordMatch(KeyValuePair<string, JsonDocument> pair, string targetMap)
    {
        if (IsKeyMatch(pair.Key, targetMap))
            return true;

        return IsDocumentNameMatch(pair.Value, targetMap);
    }

    private bool IsKeyMatch(string key, string targetMap)
    {
        return string.Equals(key, targetMap, StringComparison.OrdinalIgnoreCase)
            || string.Equals(key.Replace(' ', '_'), targetMap.Replace(' ', '_'), StringComparison.OrdinalIgnoreCase)
            || key.StartsWith($"{targetMap}_", StringComparison.OrdinalIgnoreCase)
            || key.StartsWith($"{targetMap.Replace(' ', '_')}_", StringComparison.OrdinalIgnoreCase);
    }

    private bool IsDocumentNameMatch(JsonDocument document, string targetMap)
    {
        try
        {
            var root = document.RootElement;
            string name = root.TryGetProperty("MapName", out var mn) ? mn.GetString() ?? "" : "";
            return string.Equals(name, targetMap, StringComparison.OrdinalIgnoreCase) 
                || string.Equals(name.Replace(' ', '_'), targetMap.Replace(' ', '_'), StringComparison.OrdinalIgnoreCase);
        }
        catch 
        { 
            return false;
        }
    }


    private void RemoveSpecificManifestVersion(ContentAddressableStorage cas, DataStoreService db, string targetMap, string targetVersion, ref int manifestsDeleted, ref int dbRecordsRemoved, List<string> deletedFiles)
    {
        string manifestDir = Path.Combine(cas.RootDirectory, "manifests");
        string compositeKey = $"{targetMap}_{targetVersion}";
        
        DeleteSpecificManifests(manifestDir, targetMap, targetVersion, compositeKey, ref manifestsDeleted, deletedFiles);
        RemoveSpecificPublishedRecords(db, targetMap, targetVersion, compositeKey, ref dbRecordsRemoved);

        HandleRemainingVersionsFallback(cas, db, targetMap, targetVersion, manifestDir, ref manifestsDeleted, deletedFiles);
        db.Delete("map_stats", compositeKey);
    }

    private void DeleteSpecificManifests(string manifestDir, string targetMap, string targetVersion, string compositeKey, ref int manifestsDeleted, List<string> deletedFiles)
    {
        var candidateNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            $"{compositeKey}_manifest.json", $"{compositeKey}.json",
            $"{compositeKey.Replace(' ', '_')}_manifest.json", $"{compositeKey.Replace(' ', '_')}.json",
            $"{compositeKey.Replace('_', ' ')}_manifest.json", $"{compositeKey.Replace('_', ' ')}.json"
        };

        if (!Directory.Exists(manifestDir)) return;

        foreach (var filePath in Directory.EnumerateFiles(manifestDir, "*.json"))
        {
            ProcessSpecificManifestForDeletion(filePath, targetMap, targetVersion, candidateNames, ref manifestsDeleted, deletedFiles);
        }
    }

    private void ProcessSpecificManifestForDeletion(string filePath, string targetMap, string targetVersion, HashSet<string> candidateNames, ref int manifestsDeleted, List<string> deletedFiles)
    {
        string fileName = Path.GetFileName(filePath);
        bool shouldDelete = candidateNames.Contains(fileName);

        if (!shouldDelete)
            shouldDelete = ShouldDeleteManifestByContent(filePath, targetMap, targetVersion);

        if (shouldDelete)
            AttemptDeleteManifest(filePath, fileName, ref manifestsDeleted, deletedFiles);
    }

    private void RemoveSpecificPublishedRecords(DataStoreService db, string targetMap, string targetVersion, string compositeKey, ref int dbRecordsRemoved)
    {
        var publishedKeysToDelete = new List<string>();
        foreach (var pair in db.GetAllWithKeys<JsonDocument>("published_maps"))
        {
            if (IsSpecificPublishedRecordMatch(pair, targetMap, targetVersion, compositeKey))
            {
                publishedKeysToDelete.Add(pair.Key);
            }
        }

        if (publishedKeysToDelete.Count > 0)
        {
            db.DeleteMany("published_maps", publishedKeysToDelete);
            dbRecordsRemoved += publishedKeysToDelete.Count;
        }
    }

    private bool IsSpecificPublishedRecordMatch(KeyValuePair<string, JsonDocument> pair, string targetMap, string targetVersion, string compositeKey)
    {
        if (IsSpecificKeyMatch(pair.Key, compositeKey))
            return true;

        return IsSpecificDocumentMatch(pair.Value, targetMap, targetVersion);
    }

    private bool IsSpecificKeyMatch(string key, string compositeKey)
    {
        return string.Equals(key, compositeKey, StringComparison.OrdinalIgnoreCase) 
            || string.Equals(key.Replace(' ', '_'), compositeKey.Replace(' ', '_'), StringComparison.OrdinalIgnoreCase);
    }

    private bool IsSpecificDocumentMatch(JsonDocument document, string targetMap, string targetVersion)
    {
        try
        {
            var root = document.RootElement;
            string name = root.TryGetProperty("MapName", out var mn) ? mn.GetString() ?? "" : "";
            string ver = root.TryGetProperty("Version", out var vr) ? vr.GetString() ?? "" : "";
            
            bool nameMatch = string.Equals(name, targetMap, StringComparison.OrdinalIgnoreCase) 
                          || string.Equals(name.Replace(' ', '_'), targetMap.Replace(' ', '_'), StringComparison.OrdinalIgnoreCase);
            bool versionMatch = string.Equals(ver, targetVersion, StringComparison.OrdinalIgnoreCase);
            
            return nameMatch && versionMatch;
        }
        catch 
        { 
            return false;
        }
    }


    private void HandleRemainingVersionsFallback(ContentAddressableStorage cas, DataStoreService db, string targetMap, string targetVersion, string manifestDir, ref int manifestsDeleted, List<string> deletedFiles)
    {
        var remainingVersions = new List<(string version, JsonDocument doc, string? filePath)>();
        
        CollectRemainingVersions(db, manifestDir, targetMap, targetVersion, remainingVersions);
        ApplyFallbackOperations(cas, db, manifestDir, targetMap, remainingVersions, ref manifestsDeleted, deletedFiles);
    }


    private void CollectRemainingVersions(DataStoreService db, string manifestDir, string targetMap, string targetVersion, List<(string version, JsonDocument doc, string? filePath)> remainingVersions)
    {
        CollectRemainingVersionsFromDirectory(manifestDir, targetMap, targetVersion, remainingVersions);
        CollectRemainingVersionsFromDatabase(db, targetMap, targetVersion, remainingVersions);
    }

    private void CollectRemainingVersionsFromDirectory(string manifestDir, string targetMap, string targetVersion, List<(string version, JsonDocument doc, string? filePath)> remainingVersions)
    {
        if (!Directory.Exists(manifestDir)) return;

        foreach (var filePath in Directory.EnumerateFiles(manifestDir, "*.json"))
        {
            TryAddVersionFromManifestFile(filePath, targetMap, targetVersion, remainingVersions);
        }
    }

    private void TryAddVersionFromManifestFile(string filePath, string targetMap, string targetVersion, List<(string version, JsonDocument doc, string? filePath)> remainingVersions)
    {
        try
        {
            var mf = MapManifest.LoadFromFile(filePath);
            
            bool isMapMatch = string.Equals(mf?.MapName, targetMap, StringComparison.OrdinalIgnoreCase) 
                           || string.Equals(mf?.MapName?.Replace(' ', '_'), targetMap.Replace(' ', '_'), StringComparison.OrdinalIgnoreCase);
                           
            if (!isMapMatch || string.Equals(mf?.Version, targetVersion, StringComparison.OrdinalIgnoreCase))
                return;

            var doc = JsonDocument.Parse(File.ReadAllText(filePath));
            remainingVersions.Add((mf!.Version ?? "1.0", doc, filePath));
        }
        catch { }
    }

    private void CollectRemainingVersionsFromDatabase(DataStoreService db, string targetMap, string targetVersion, List<(string version, JsonDocument doc, string? filePath)> remainingVersions)
    {
        foreach (var pair in db.GetAllWithKeys<JsonDocument>("published_maps"))
        {
            if (IsDbKeyMatchingTargetMapExact(pair.Key, targetMap)) continue;
            
            TryAddVersionFromDatabaseRecord(pair.Value, targetMap, targetVersion, remainingVersions);
        }
    }

    private bool IsDbKeyMatchingTargetMapExact(string dbKey, string targetMap)
    {
        return string.Equals(dbKey, targetMap, StringComparison.OrdinalIgnoreCase) 
            || string.Equals(dbKey.Replace(' ', '_'), targetMap.Replace(' ', '_'), StringComparison.OrdinalIgnoreCase);
    }

    private void TryAddVersionFromDatabaseRecord(JsonDocument doc, string targetMap, string targetVersion, List<(string version, JsonDocument doc, string? filePath)> remainingVersions)
    {
        try
        {
            var root = doc.RootElement;
            string name = root.TryGetProperty("MapName", out var mn) ? mn.GetString() ?? "" : "";
            string ver = root.TryGetProperty("Version", out var vr) ? vr.GetString() ?? "" : "";
            
            bool isNameMatch = string.Equals(name, targetMap, StringComparison.OrdinalIgnoreCase) 
                            || string.Equals(name.Replace(' ', '_'), targetMap.Replace(' ', '_'), StringComparison.OrdinalIgnoreCase);

            if (!isNameMatch || string.Equals(ver, targetVersion, StringComparison.OrdinalIgnoreCase))
                return;

            if (!HasVersionAlready(remainingVersions, ver))
                remainingVersions.Add((ver, doc, null));
        }
        catch { }
    }

    private bool HasVersionAlready(List<(string version, JsonDocument doc, string? filePath)> remainingVersions, string version)
    {
        return remainingVersions.Any(r => string.Equals(r.version, version, StringComparison.OrdinalIgnoreCase));
    }

    private void ApplyFallbackOperations(ContentAddressableStorage cas, DataStoreService db, string manifestDir, string targetMap, List<(string version, JsonDocument doc, string? filePath)> remainingVersions, ref int manifestsDeleted, List<string> deletedFiles)
    {
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
    }

    private bool ApplyMapMetricReport(ClusterEventDto evt, DataStoreService db)
    {
        if (!TryGetMetricContext(evt, out var payload, out var mapTitle, out var mapVersion, out var authorPublicKey, out var compositeKey, out var authorCompositeKey, out var playerIdentifier, out var engagementKey))
            return false;

        var engagement = db.Get<PlayerMapEngagement>("player_engagement", engagementKey) ?? new PlayerMapEngagement { PlayerId = playerIdentifier };
        bool wasVerifiedGood = engagement.IsVerifiedGoodReview;
        bool hadSubmittedRating = engagement.SubmittedRating.HasValue;
        int prevRating = engagement.SubmittedRating ?? 0;

        UpdatePlayerEngagement(engagement, payload);
        
        var stats = MapStatsHelper.GetStats(db, mapTitle, mapVersion, authorPublicKey) ?? new MapStats();
        UpdateMapStats(stats, payload, engagement, hadSubmittedRating, prevRating, wasVerifiedGood);

        db.Upsert("player_engagement", engagementKey, engagement);
        UpsertMapStatsToDb(db, stats, mapTitle, compositeKey, authorPublicKey, authorCompositeKey);

        return true;
    }

    private bool TryGetMetricContext(ClusterEventDto evt, out MapMetricReportEventPayload payload, out string mapTitle, out string mapVersion, out string authorPublicKey, out string compositeKey, out string authorCompositeKey, out string playerIdentifier, out string engagementKey)
    {
        payload = ParseMapMetricReportPayload(evt)!;
        mapTitle = "";
        mapVersion = "1.0";
        authorPublicKey = "";
        compositeKey = "";
        authorCompositeKey = "";
        playerIdentifier = "Anonymous";
        engagementKey = "";

        if (payload == null || string.IsNullOrWhiteSpace(payload.MapTitle))
            return false;

        mapTitle = payload.MapTitle.Trim();
        mapVersion = string.IsNullOrWhiteSpace(payload.MapVersion) ? "1.0" : payload.MapVersion.Trim();
        authorPublicKey = payload.AuthorPublicKey?.Trim() ?? "";
        compositeKey = $"{mapTitle}_{mapVersion}";
        authorCompositeKey = !string.IsNullOrEmpty(authorPublicKey) ? $"{mapTitle}_{mapVersion}_{authorPublicKey}" : compositeKey;
        playerIdentifier = !string.IsNullOrWhiteSpace(payload.PlayerId) ? payload.PlayerId.Trim() : "Anonymous";

        engagementKey = !string.IsNullOrEmpty(authorPublicKey)
            ? $"{playerIdentifier}_{authorCompositeKey}".ToLowerInvariant()
            : $"{playerIdentifier}_{compositeKey}".ToLowerInvariant();

        return true;
    }


    private MapMetricReportEventPayload? ParseMapMetricReportPayload(ClusterEventDto evt)
    {
        if (string.IsNullOrWhiteSpace(evt.PayloadJson)) return null;
        try
        {
            return JsonSerializer.Deserialize<MapMetricReportEventPayload>(evt.PayloadJson, JsonOpts);
        }
        catch { return null; }
    }

    private void UpdatePlayerEngagement(PlayerMapEngagement engagement, MapMetricReportEventPayload payload)
    {
        engagement.TotalPlaytimeMinutes += Math.Max(0.0, payload.PlaytimeMinutes);
        if (payload.IsCompleteGame)
        {
            engagement.GamesPlayed += 1;
        }
        if (!string.IsNullOrWhiteSpace(payload.AuthProvider) && !payload.AuthProvider.Equals("guest", StringComparison.OrdinalIgnoreCase))
        {
            engagement.IsVerifiedAccount = true;
        }

        if (payload.Stars >= 1 && payload.Stars <= 5)
        {
            engagement.SubmittedRating = payload.Stars;
        }
    }

    private void UpdateMapStats(MapStats stats, MapMetricReportEventPayload payload, PlayerMapEngagement engagement, bool hadSubmittedRating, int prevRating, bool wasVerifiedGood)
    {
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
    }

    private void UpsertMapStatsToDb(DataStoreService db, MapStats stats, string mapTitle, string compositeKey, string authorPublicKey, string authorCompositeKey)
    {
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
    }
}
