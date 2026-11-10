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

/// <summary>map_metric_report events.</summary>
public partial class ClusterEventService
	{
	private bool ApplyMapMetricReport(ClusterEventDto evt, DataStoreService db)
	{
		if (!TryGetMetricContext(evt, out var payload, out var mapTitle, out var mapVersion, out var authorPublicKey, out var compositeKey, out var authorCompositeKey, out var playerIdentifier, out var engagementKey))
			return false;

		var engagement = db.Get<PlayerMapEngagement>("player_engagement", engagementKey) ?? new PlayerMapEngagement { AccountId = payload.AccountId, DisplayName = payload.DisplayName };
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
		playerIdentifier = payload.AccountId != 0 ? payload.AccountId.ToString(System.Globalization.CultureInfo.InvariantCulture) : "anonymous";

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
		catch
		{
			return null;
		}
	}

	private void UpdatePlayerEngagement(PlayerMapEngagement engagement, MapMetricReportEventPayload payload)
	{
		engagement.TotalPlaytimeMinutes += Math.Max(0.0, payload.PlaytimeMinutes);
		if (payload.IsCompleteGame)
		{
			engagement.GamesPlayed += 1;
		}

		if (payload.AccountId != 0)
		{
			engagement.IsVerifiedAccount = true;
			engagement.AccountId = payload.AccountId;
			engagement.DisplayName = payload.DisplayName;
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
		MapStatsHelper.UpsertMapStats(db, mapTitle, authorPublicKey, authorCompositeKey, compositeKey, stats);
	}
}
