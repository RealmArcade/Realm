using System.Collections.Concurrent;
using System.Text;
using System.Text.Json;
using DBreeze.Transactions;
using Realm.AdminServer.Accounts;
using Realm.Shared;
using Realm.Shared.Accounts;
using Realm.Shared.Distribution;
using Realm.Shared.Metadata;

namespace Realm.AdminServer.Services;

/// <summary>absorbing a peer snapshot.</summary>
public partial class ClusterEventService
	{
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
			catch (Exception ex)
			{
				Console.Error.WriteLine($"[ClusterEvents] could not store a name lock in the database: {ex.GetType().Name}: {ex.Message}");
			}
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
			catch (Exception ex)
			{
				Console.Error.WriteLine($"[ClusterEvents] could not import a map from a cluster peer: {ex.GetType().Name}: {ex.Message}");
			}
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
			catch (Exception ex)
			{
				Console.Error.WriteLine($"[ClusterEvents] could not store an asset signature in the database: {ex.GetType().Name}: {ex.Message}");
			}
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
			catch (Exception ex)
			{
				Console.Error.WriteLine($"[ClusterEvents] could not store player engagement in the database: {ex.GetType().Name}: {ex.Message}");
			}
		}
	}
}
