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

/// <summary>generating a cluster snapshot from the database.</summary>
public partial class ClusterEventService
	{
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
}
