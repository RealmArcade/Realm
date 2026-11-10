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

/// <summary>state digest and its per-collection hashes.</summary>
public partial class ClusterEventService
	{
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
			engSb.Append($"{key}:{en.AccountId}:{en.DisplayName}:{en.TotalPlaytimeMinutes:F1}:{en.GamesPlayed}:{en.SubmittedRating}:{en.IsVerifiedAccount};");
		}

		colHashes["player_engagement"] = RealmMetadataHelper.ComputeBlake3(Encoding.UTF8.GetBytes(engSb.ToString()), ".txt");
	}
}
