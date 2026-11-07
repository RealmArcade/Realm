using System.Collections.Concurrent;
using System.Globalization;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Http.Features;
using NSec.Cryptography;
using Realm.AdminServer.Accounts;
using Realm.AdminServer.Models;
using Realm.AdminServer.Services;
using Realm.Shared;
using Realm.Shared.Accounts;
using Realm.Shared.Distribution;
using Realm.Shared.Metadata;
using Realm.AdminServer.Helpers;

namespace Realm.AdminServer;

/// <summary>Map greenlight status and the maintainer roster.</summary>
internal static class MapRegistryEndpoints
{
	internal static void MapMapRegistryEndpoints(WebApplication app, ServerSetup setup)
	{
		app.MapGet("/api/maps/greenlight_status/{mapId}", (string mapId, DataStoreService db) =>
		{
			string title = mapId;
			string? ver = null;
			string? pubKey = null;

			if (mapId.Contains('_'))
			{
				var parts = mapId.Split('_');
				if (parts.Length >= 3)
				{
					title = parts[0];
					ver = parts[1];
					pubKey = parts[2];
				}
				else if (parts.Length == 2)
				{
					title = parts[0];
					ver = parts[1];
				}
			}

			var stats = MapStatsHelper.GetStats(db, title, ver, pubKey) ?? new MapStats();

			return Results.Ok(new
			{
				MapId = mapId,
				IsGreenlit = stats.IsGreenlit,
				AdminOverride = stats.AdminOverrideGreenlit,
				VerifiedGoodReviewsCount = stats.VerifiedGoodReviewsCount,
				RequiredVerifiedGoodReviewsCount = 100,
				TotalReviewsCount = stats.ReviewsCount,
				AverageRating = stats.AverageRating
			});
		});

		app.MapGet("/api/maps/{mapTitle}/maintainers", (string mapTitle, DataStoreService db) =>
		{
			var maintainers = MapMaintainerHelper.GetMaintainers(db, mapTitle);
			string owner = MapMaintainerHelper.GetOwner(db, mapTitle);
			return Results.Ok(new MapMaintainersResponseDto
			{
				Success = true,
				MapTitle = mapTitle,
				OwnerPublicKey = owner,
				Maintainers = maintainers,
				Message = $"Found {maintainers.Count} maintainer(s) for map '{mapTitle}'."
			});
		});

		app.MapPost("/api/maps/{mapTitle}/maintainers/add", (string mapTitle, AddMapMaintainerRequest req, DataStoreService db, ClusterEventService clusterEvents, PeerRegistry registeredPeers, IHttpClientFactory httpClientFactory) =>
		{
			return MapMaintainerEndpointsHelper.HandleAddMapMaintainer(req.MapTitle, mapTitle, req, db, clusterEvents, registeredPeers, httpClientFactory, setup.AdminPublicKeys);
		});

		app.MapPost("/api/maps/maintainers/add", (AddMapMaintainerRequest req, DataStoreService db, ClusterEventService clusterEvents, PeerRegistry registeredPeers, IHttpClientFactory httpClientFactory) =>
		{
			return MapMaintainerEndpointsHelper.HandleAddMapMaintainer(req.MapTitle, req.MapTitle, req, db, clusterEvents, registeredPeers, httpClientFactory, setup.AdminPublicKeys);
		});

		app.MapPost("/api/maps/{mapTitle}/maintainers/remove", (string mapTitle, RemoveMapMaintainerRequest req, DataStoreService db, ClusterEventService clusterEvents, PeerRegistry registeredPeers, IHttpClientFactory httpClientFactory) =>
		{
			return MapMaintainerEndpointsHelper.HandleRemoveMapMaintainer(req.MapTitle, mapTitle, req, db, clusterEvents, registeredPeers, httpClientFactory, setup.AdminPublicKeys);
		});

		app.MapPost("/api/maps/maintainers/remove", (RemoveMapMaintainerRequest req, DataStoreService db, ClusterEventService clusterEvents, PeerRegistry registeredPeers, IHttpClientFactory httpClientFactory) =>
		{
			return MapMaintainerEndpointsHelper.HandleRemoveMapMaintainer(req.MapTitle, req.MapTitle, req, db, clusterEvents, registeredPeers, httpClientFactory, setup.AdminPublicKeys);
		});
	}

	internal static void MapMapMetricsEndpoints(WebApplication app, ServerSetup setup)
	{
		app.MapPost("/api/maps/report_metrics", (MapMetricsReport req, AccountStoreService accounts, DataStoreService db, ClusterEventService clusterEvents, PeerRegistry registeredPeers, IHttpClientFactory httpClientFactory) =>
		{
			if (string.IsNullOrWhiteSpace(req.MapTitle))
			{
				return Results.BadRequest(new { Message = "MapTitle is required." });
			}

			string mapTitle = req.MapTitle.Trim();
			string mapVersion = string.IsNullOrWhiteSpace(req.MapVersion) ? "1.0" : req.MapVersion.Trim();
			string authorPublicKey = req.AuthorPublicKey?.Trim() ?? "";
			string compositeKey = $"{mapTitle}_{mapVersion}";
			string authorCompositeKey = !string.IsNullOrEmpty(authorPublicKey) ? $"{mapTitle}_{mapVersion}_{authorPublicKey}" : compositeKey;

			long accountId = 0;
			string displayName = string.Empty;
			if (accounts.TryValidateSession(req.SessionToken, out AccountRecord? reportingAccount, out _, out _) && reportingAccount != null)
			{
				accountId = reportingAccount.AccountId;
				displayName = reportingAccount.DisplayName;
			}

			string playerIdentifier = accountId != 0 ? accountId.ToString(CultureInfo.InvariantCulture) : "anonymous";
			bool isVerifiedAccount = accountId != 0;

			string engagementKey = !string.IsNullOrEmpty(authorPublicKey)
				? $"{playerIdentifier}_{authorCompositeKey}".ToLowerInvariant()
				: $"{playerIdentifier}_{compositeKey}".ToLowerInvariant();
			var engagement = db.Get<PlayerMapEngagement>("player_engagement", engagementKey)
			                 ?? new PlayerMapEngagement { AccountId = accountId, DisplayName = displayName };

			if (accountId != 0)
			{
				engagement.DisplayName = displayName;
			}

			bool wasVerifiedGood = engagement.IsVerifiedGoodReview;
			bool hadSubmittedRating = engagement.SubmittedRating.HasValue;
			int prevRating = engagement.SubmittedRating ?? 0;

			engagement.TotalPlaytimeMinutes += Math.Max(0.0, req.PlaytimeMinutes);
			if (req.IsCompleteGame)
			{
				engagement.GamesPlayed += 1;
			}

			if (isVerifiedAccount)
			{
				engagement.IsVerifiedAccount = true;
			}

			var stats = MapStatsHelper.GetStats(db, mapTitle, mapVersion, authorPublicKey) ?? new MapStats();

			stats.TotalPlaytimeMinutes += Math.Max(0.0, req.PlaytimeMinutes);
			if (req.IsCompleteGame)
			{
				stats.TotalGamesPlayed += 1;
			}

			if (req.Stars >= 1 && req.Stars <= 5)
			{
				if (hadSubmittedRating)
				{
					stats.TotalStars += (req.Stars - prevRating);
				}
				else
				{
					stats.ReviewsCount += 1;
					stats.TotalStars += req.Stars;
				}

				engagement.SubmittedRating = req.Stars;
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
			MapStatsHelper.UpsertMapStats(db, mapTitle, authorPublicKey, authorCompositeKey, compositeKey, stats);

			var metricEvent = new ClusterEventDto
			{
				EventType = "map_metric_report",
				PayloadJson = JsonSerializer.Serialize(new MapMetricReportEventPayload
				{
					MapTitle = mapTitle,
					MapVersion = mapVersion,
					AccountId = accountId,
					DisplayName = displayName,
					PlaytimeMinutes = Math.Max(0.0, req.PlaytimeMinutes),
					Stars = req.Stars,
					IsCompleteGame = req.IsCompleteGame,
					AuthorPublicKey = authorPublicKey
				})
			};
			clusterEvents.MarkEventProcessed(metricEvent.EventId, db);
			clusterEvents.BroadcastEvent(metricEvent, registeredPeers, httpClientFactory);

			return Results.Ok(new
			{
				Success = true,
				MapId = compositeKey,
				IsVerifiedAccount = engagement.IsVerifiedAccount,
				IsEligibleReviewer = engagement.IsEligibleReviewer,
				IsVerifiedGoodReview = engagement.IsVerifiedGoodReview,
				PlayerTotalPlaytime = engagement.TotalPlaytimeMinutes,
				PlayerGamesPlayed = engagement.GamesPlayed,
				MapStats = stats,
				IsGreenlit = stats.IsGreenlit
			});
		});
	}
}
