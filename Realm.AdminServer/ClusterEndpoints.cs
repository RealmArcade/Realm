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

/// <summary>Cluster state reads, event intake and peer synchronisation.</summary>
public static class ClusterEndpoints
{
	public static void MapClusterEndpoints(WebApplication app, ServerSetup setup)
	{
		app.MapGet("/api/cluster/events", (DateTime? sinceUtc, int? limit, ClusterEventService clusterEvents, DataStoreService db) =>
		{
			var events = clusterEvents.GetEvents(sinceUtc, limit ?? 100, db);
			return Results.Ok(events);
		});

		app.MapGet("/api/cluster/state_digest", (ClusterEventService clusterEvents, DataStoreService db, PeerRegistry registeredPeers) =>
		{
			var digest = clusterEvents.ComputeStateDigest(db);
			digest.OriginServerUrl = registeredPeers.SelfUrl;
			return Results.Ok(digest);
		});

		app.MapGet("/api/cluster/snapshot", (ClusterEventService clusterEvents, DataStoreService db) =>
		{
			var snapshot = clusterEvents.GenerateSnapshot(db);
			return Results.Ok(snapshot);
		});

		app.MapPost("/api/cluster/snapshot", (HttpRequest request, ClusterSnapshotDto snapshot, ClusterEventService clusterEvents, DataStoreService db, ContentAddressableStorage cas) =>
		{
			string? bypassHeader = request.Headers["X-Admin-Bypass"];
			string? sigHeader = request.Headers["X-Cluster-Signature"];
			string? pubKeyHeader = request.Headers["X-Admin-PublicKey"];

			bool isAuth = (!string.IsNullOrEmpty(bypassHeader) && setup.AdminPublicKeys.Count > 0 && AdminBypassAuth.VerifyBypassToken(setup.AdminPublicKeys, "cluster", "snapshot", bypassHeader))
			              || (!string.IsNullOrEmpty(sigHeader) && (
				              (!string.IsNullOrEmpty(pubKeyHeader) && setup.AdminPublicKeys.Contains(pubKeyHeader.ToString().Trim()) && AuthorSignatureHelper.VerifySignature(pubKeyHeader.ToString().Trim(), "cluster_snapshot", sigHeader.ToString()))
				              || AuthorSignatureHelper.VerifySignatureAny(setup.AdminPublicKeys, "cluster_snapshot", sigHeader.ToString())
			              ));

			if (!isAuth)
			{
				return Results.Json(new { Message = "Unauthorized snapshot import request." }, statusCode: StatusCodes.Status401Unauthorized);
			}

			var result = clusterEvents.ApplySnapshot(snapshot, db, cas, setup.AdminPublicKeys);
			return Results.Ok(result);
		});

		app.MapPost("/api/cluster/event", async (ClusterEventDto evt, ClusterEventService clusterEvents, DataStoreService db, ContentAddressableStorage cas) =>
		{
			bool applied = await clusterEvents.ApplyEventAsync(evt, db, cas, setup.AdminPublicKeys);
			if (!applied)
			{
				return Results.BadRequest(new { Message = "Event verification or application failed." });
			}

			return Results.Ok(new { Status = "Applied", EventId = evt.EventId });
		});

		app.MapPost("/api/cluster/sync_events", async (List<ClusterEventDto> events, ClusterEventService clusterEvents, DataStoreService db, ContentAddressableStorage cas) =>
		{
			int appliedCount = 0;
			if (events != null)
			{
				foreach (var evt in events)
				{
					if (await clusterEvents.ApplyEventAsync(evt, db, cas, setup.AdminPublicKeys))
					{
						appliedCount++;
					}
				}
			}

			return Results.Ok(new { Status = "Sync Complete", TotalEvents = events?.Count ?? 0, AppliedCount = appliedCount });
		});
	}

	public static void MapClusterSyncEndpoints(WebApplication app, ServerSetup setup)
	{
		app.MapPost("/api/cluster/sync_metrics", async (HttpRequest request, ClusterEventService clusterEvents, DataStoreService db, ContentAddressableStorage cas) =>
		{
			using var reader = new StreamReader(request.Body, Encoding.UTF8);
			string body = await reader.ReadToEndAsync();

			bool isAuth = await AdminEndpointsHelper.VerifyClusterSyncRequest(request, body, setup.AdminPublicKeys.ToList());

			if (!isAuth)
			{
				return Results.Json(new { Message = "Unauthorized cluster sync request." }, statusCode: StatusCodes.Status401Unauthorized);
			}

			var metricsList = JsonSerializer.Deserialize<List<MapStatsSyncDto>>(body, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
			if (metricsList != null)
			{
				foreach (var item in metricsList)
				{
					if (string.IsNullOrWhiteSpace(item.MapId)) continue;

					var existing = db.Get<MapStats>("map_stats", item.MapId) ?? new MapStats();
					existing.TotalPlaytimeMinutes = Math.Max(existing.TotalPlaytimeMinutes, item.TotalPlaytimeMinutes);
					existing.TotalGamesPlayed = Math.Max(existing.TotalGamesPlayed, item.TotalGamesPlayed);
					existing.ReviewsCount = Math.Max(existing.ReviewsCount, item.TotalReviewsCount);
					existing.VerifiedGoodReviewsCount = Math.Max(existing.VerifiedGoodReviewsCount, item.VerifiedGoodReviewsCount);
					if (item.AverageRating > 0 && item.TotalReviewsCount > 0)
					{
						existing.TotalStars = Math.Max(existing.TotalStars, (int)Math.Round(item.AverageRating * item.TotalReviewsCount));
					}

					if (item.AdminOverrideGreenlit)
					{
						existing.AdminOverrideGreenlit = true;
					}

					db.Upsert("map_stats", item.MapId, existing);
				}
			}

			return Results.Ok(new { Status = "Metrics Synchronized", Count = metricsList?.Count ?? 0 });
		});

		app.MapPost("/api/cluster/sync_published_maps", async (HttpRequest request, ClusterEventService clusterEvents, DataStoreService db, ContentAddressableStorage cas) =>
		{
			using var reader = new StreamReader(request.Body, Encoding.UTF8);
			string body = await reader.ReadToEndAsync();

			bool isAuth = await AdminEndpointsHelper.VerifyClusterSyncRequest(request, body, setup.AdminPublicKeys.ToList());

			if (!isAuth)
			{
				return Results.Json(new { Message = "Unauthorized cluster sync request." }, statusCode: StatusCodes.Status401Unauthorized);
			}

			var mapsList = JsonSerializer.Deserialize<List<PublishedMapSyncDto>>(body, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
			if (mapsList != null)
			{
				string manifestDir = Path.Combine(cas.RootDirectory, "manifests");
				if (!Directory.Exists(manifestDir)) Directory.CreateDirectory(manifestDir);

				foreach (var map in mapsList)
				{
					if (string.IsNullOrWhiteSpace(map.MapId)) continue;

					if (!string.IsNullOrWhiteSpace(map.ManifestJson))
					{
						string manifestPath = Path.Combine(manifestDir, $"{map.MapId}_manifest.json");
						File.WriteAllText(manifestPath, map.ManifestJson);
						if (!string.IsNullOrWhiteSpace(map.MapTitle))
						{
							string defaultPath = Path.Combine(manifestDir, $"{map.MapTitle}_manifest.json");
							File.WriteAllText(defaultPath, map.ManifestJson);
						}

						try
						{
							var doc = JsonDocument.Parse(map.ManifestJson);
							db.Upsert("published_maps", map.MapId, doc);
						}
						catch (Exception ex)
						{
							Console.Error.WriteLine($"[AdminServer] could not store the manifest of published map {map.MapId} in the database: {ex.GetType().Name}: {ex.Message}");
						}
					}

					if (!string.IsNullOrWhiteSpace(map.OwnerPublicKey) && !string.IsNullOrWhiteSpace(map.MapTitle))
					{
						var existingOwner = db.Get<string>("map_ownership", map.MapTitle);
						if (existingOwner == null)
						{
							db.Upsert("map_ownership", map.MapTitle, map.OwnerPublicKey);
						}
					}
				}
			}

			return Results.Ok(new { Status = "Published Maps Synchronized", Count = mapsList?.Count ?? 0 });
		});

		app.MapPost("/api/cluster/sync_creators", async (HttpRequest request, ClusterEventService clusterEvents, DataStoreService db) =>
		{
			using var reader = new StreamReader(request.Body, Encoding.UTF8);
			string body = await reader.ReadToEndAsync();

			bool isAuth = await AdminEndpointsHelper.VerifyClusterSyncRequest(request, body, setup.AdminPublicKeys.ToList());

			if (!isAuth)
			{
				return Results.Json(new { Message = "Unauthorized cluster sync request." }, statusCode: StatusCodes.Status401Unauthorized);
			}

			var creatorsList = JsonSerializer.Deserialize<List<CreatorSyncDto>>(body, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
			if (creatorsList != null)
			{
				foreach (var creator in creatorsList)
				{
					if (string.IsNullOrWhiteSpace(creator.PublicKey) || string.IsNullOrWhiteSpace(creator.Username)) continue;

					string slug = NameNormalizationHelper.NormalizeUsername(creator.Username);
					if (string.IsNullOrEmpty(slug))
					{
						slug = creator.Username.ToLowerInvariant().Replace(" ", "-");
					}

					var existingLock = db.Get<JsonDocument>("name_locks", slug)
					                   ?? db.Get<JsonDocument>("name_locks", creator.Username.ToLowerInvariant().Replace(" ", "-"));
					if (existingLock == null)
					{
						var lockDoc = JsonSerializer.SerializeToDocument(new
						{
							username = creator.Username,
							owner_public_key = creator.PublicKey,
							registered_at = creator.RegisteredAt ?? DateTime.UtcNow
						});
						db.Upsert("name_locks", slug, lockDoc);
					}

					var existingCreator = db.Get<JsonDocument>("creators", creator.PublicKey);
					if (existingCreator == null)
					{
						var creatorDoc = JsonSerializer.SerializeToDocument(new
						{
							username = creator.Username,
							public_key = creator.PublicKey,
							donation_link = creator.DonationLink ?? "",
							contact_info = creator.ContactInfo ?? "",
							registered_at = creator.RegisteredAt ?? DateTime.UtcNow
						});
						db.Upsert("creators", creator.PublicKey, creatorDoc);
					}
				}
			}

			return Results.Ok(new { Status = "Creators Synchronized", Count = creatorsList?.Count ?? 0 });
		});
	}
}
