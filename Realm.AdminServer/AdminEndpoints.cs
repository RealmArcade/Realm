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

/// <summary>Administrative maintenance: CAS pruning and manifest removal.</summary>
public static class AdminEndpoints
{
	public static void MapAdminRemovalEndpoints(WebApplication app, ServerSetup setup)
	{
		app.MapPost("/api/admin/prune_cas", (HttpRequest request, ContentPruner pruner, DataStoreService db, ContentAddressableStorage cas) =>
		{
			string? bypassHeader = request.Headers["X-Admin-Bypass"];
			string? sigHeader = request.Headers["X-Cluster-Signature"];
			string? pubKeyHeader = request.Headers["X-Admin-PublicKey"];

			bool isAuth = (!string.IsNullOrEmpty(bypassHeader) && setup.AdminPublicKeys.Count > 0 && AdminBypassAuth.VerifyBypassToken(setup.AdminPublicKeys, "admin", "prune_cas", bypassHeader))
			              || (!string.IsNullOrEmpty(sigHeader) && (
				              (!string.IsNullOrEmpty(pubKeyHeader) && setup.AdminPublicKeys.Contains(pubKeyHeader.ToString().Trim()) && AuthorSignatureHelper.VerifySignature(pubKeyHeader.ToString().Trim(), "prune_cas", sigHeader.ToString()))
				              || AuthorSignatureHelper.VerifySignatureAny(setup.AdminPublicKeys, "prune_cas", sigHeader.ToString())
			              ));

			if (!isAuth)
			{
				return Results.Json(new { Message = "Unauthorized CAS pruning request." }, statusCode: StatusCodes.Status401Unauthorized);
			}

			pruner.QueueCasPrune(cas, db);
			return Results.Ok(new CasPruneResponseDto
			{
				Success = true,
				Message = "CAS prune operation has been queued on a background task."
			});
		});

		app.MapPost("/api/admin/remove_manifest", async (HttpRequest request, ClusterEventService clusterEvents, DataStoreService db, ContentAddressableStorage cas, PeerRegistry registeredPeers, IHttpClientFactory httpClientFactory) =>
		{
			string body = string.Empty;
			using (var reader = new StreamReader(request.Body, Encoding.UTF8))
			{
				body = await reader.ReadToEndAsync();
			}

			AdminRemoveManifestRequest? req = null;
			if (!string.IsNullOrWhiteSpace(body))
			{
				try
				{
					req = JsonSerializer.Deserialize<AdminRemoveManifestRequest>(body, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
				}
				catch (Exception ex)
				{
					Console.Error.WriteLine($"[AdminServer] the admin remove-manifest request body was not valid JSON: {ex.GetType().Name}: {ex.Message}");
				}
			}

			string mapTitle = req?.MapTitle ?? (request.Query.TryGetValue("map", out var qm) ? qm.ToString() : "");
			string? mapVersion = !string.IsNullOrWhiteSpace(req?.MapVersion) ? req.MapVersion : (request.Query.TryGetValue("version", out var qv) ? qv.ToString() : null);
			if (string.IsNullOrWhiteSpace(mapVersion)) mapVersion = null;

			if (string.IsNullOrWhiteSpace(mapTitle))
			{
				return Results.BadRequest(new RemoveManifestResponseDto { Success = false, Message = "MapTitle is required." });
			}

			if (setup.AdminPublicKeys.Count == 0)
			{
				return Results.Json(new RemoveManifestResponseDto { Success = false, Message = "Server has no AdminPublicKeys configured." }, statusCode: StatusCodes.Status403Forbidden);
			}

			string? sigHeader = request.Headers["X-Cluster-Signature"];
			string? pubKeyHeader = request.Headers["X-Admin-PublicKey"];

			string callerPubKey = req?.AdminPublicKey ?? pubKeyHeader?.ToString() ?? "";
			string callerSig = req?.Signature ?? sigHeader?.ToString() ?? "";

			return AdminEndpointsHelper.HandleRemoveManifest(mapTitle, mapVersion, callerPubKey, callerSig, request, clusterEvents, db, cas, registeredPeers, httpClientFactory, setup.AdminPublicKeys.ToList());
		});

		app.MapDelete("/api/admin/manifests/{mapTitle}", (string mapTitle, HttpRequest request, ClusterEventService clusterEvents, DataStoreService db, ContentAddressableStorage cas, PeerRegistry registeredPeers, IHttpClientFactory httpClientFactory) =>
		{
			string? callerPubKey = request.Headers["X-Admin-PublicKey"];
			string? callerSig = request.Headers["X-Cluster-Signature"];

			return AdminEndpointsHelper.HandleRemoveManifest(mapTitle, null, callerPubKey, callerSig, request, clusterEvents, db, cas, registeredPeers, httpClientFactory, setup.AdminPublicKeys.ToList());
		});

		app.MapDelete("/api/admin/manifests/{mapTitle}/{mapVersion}", (string mapTitle, string mapVersion, HttpRequest request, ClusterEventService clusterEvents, DataStoreService db, ContentAddressableStorage cas, PeerRegistry registeredPeers, IHttpClientFactory httpClientFactory) =>
		{
			if (string.IsNullOrWhiteSpace(mapTitle) || string.IsNullOrWhiteSpace(mapVersion))
			{
				return Results.BadRequest(new RemoveManifestResponseDto { Success = false, Message = "MapTitle and MapVersion are required." });
			}

			if (setup.AdminPublicKeys.Count == 0)
			{
				return Results.Json(new RemoveManifestResponseDto { Success = false, Message = "Server has no AdminPublicKeys configured." }, statusCode: StatusCodes.Status403Forbidden);
			}

			string? callerPubKey = request.Headers["X-Admin-PublicKey"];
			string? callerSig = request.Headers["X-Cluster-Signature"];

			return AdminEndpointsHelper.HandleRemoveManifest(mapTitle, mapVersion, callerPubKey, callerSig, request, clusterEvents, db, cas, registeredPeers, httpClientFactory, setup.AdminPublicKeys.ToList());
		});
	}

	public static void MapAdminInfoEndpoints(WebApplication app, ServerSetup setup)
	{
		app.MapGet("/api/admin/info", (DataStoreService db, ContentAddressableStorage cas, HttpContext context) =>
		{
			if (!AdminRequestAuthorization.IsAuthorized(context.Request, setup.AdminPublicKeys, "admin", "info")) return Results.Unauthorized();
			var adminsList = new List<object>();
			string? primaryAdminUsername = null;
			string? primaryAdminPublicKey = setup.AdminPublicKeys.FirstOrDefault();

			foreach (var key in setup.AdminPublicKeys)
			{
				string username = "Admin";
				var adminCreator = db.Get<JsonDocument>("creators", key);
				if (adminCreator != null && adminCreator.RootElement.TryGetProperty("username", out var uProp))
				{
					string? name = uProp.GetString();
					if (!string.IsNullOrEmpty(name)) username = name;
				}

				if (primaryAdminUsername == null && string.Equals(key, primaryAdminPublicKey, StringComparison.OrdinalIgnoreCase))
				{
					primaryAdminUsername = username;
				}

				adminsList.Add(new { PublicKey = key, Username = username });
			}

			long casTotalBytes = cas.GetTotalUsedBytes();
			string dataDirectory = db.DataDirectory;

			return Results.Ok(new
			{
				AdminUsername = primaryAdminUsername ?? (adminsList.Count > 0 ? "Admin" : "N/A"),
				AdminPublicKey = primaryAdminPublicKey ?? "N/A",
				AdminPublicKeys = setup.AdminPublicKeys.ToList(),
				Admins = adminsList,
				IsGraduatedAdmin = setup.AdminPublicKeys.Count > 0,
				DataDirectory = dataDirectory,
				ServesClientDownloads = setup.ServesClientDownloads,
				CasTotalSizeBytes = casTotalBytes,
				CasTotalSizeFormatted = ContentAddressableStorage.FormatByteSize(casTotalBytes)
			});
		});

		app.MapPost("/api/admin/greenlight", (AdminGreenlightRequest req, DataStoreService db, ClusterEventService clusterEvents, PeerRegistry registeredPeers, IHttpClientFactory httpClientFactory) =>
		{
			if (string.IsNullOrWhiteSpace(req.MapTitle))
			{
				return Results.BadRequest(new { Message = "MapTitle is required." });
			}

			if (setup.AdminPublicKeys.Count == 0)
			{
				return Results.Json(new { Message = "Server has no AdminPublicKeys configured in servers.json or appsettings.json." }, statusCode: StatusCodes.Status403Forbidden);
			}

			if (string.IsNullOrWhiteSpace(req.AdminPublicKey) || !setup.AdminPublicKeys.Contains(req.AdminPublicKey.Trim()))
			{
				return Results.Json(new { Message = "Unauthorized admin public key." }, statusCode: StatusCodes.Status401Unauthorized);
			}

			string mapTitle = req.MapTitle.Trim();
			string payload = $"greenlight:{mapTitle.ToLowerInvariant()}";

			bool isSigValid = AuthorSignatureHelper.VerifySignature(req.AdminPublicKey, payload, req.Signature)
			                  || (!string.IsNullOrEmpty(req.MapVersion) && AuthorSignatureHelper.VerifySignature(req.AdminPublicKey, $"greenlight:{mapTitle.ToLowerInvariant()}:{req.MapVersion.ToLowerInvariant()}", req.Signature))
			                  || (!string.IsNullOrEmpty(req.MapVersion) && AuthorSignatureHelper.VerifySignature(req.AdminPublicKey, $"{mapTitle}:{req.MapVersion}", req.Signature))
			                  || AuthorSignatureHelper.VerifySignature(req.AdminPublicKey, mapTitle, req.Signature)
			                  || AdminBypassAuth.VerifyBypassToken(req.AdminPublicKey, mapTitle, req.MapVersion ?? "1.0", req.Signature);

			if (!isSigValid)
			{
				return Results.BadRequest(new { Message = "Invalid admin signature." });
			}

			var stats = MapStatsHelper.GetStats(db, mapTitle, req.MapVersion, null) ?? new MapStats();
			stats.AdminOverrideGreenlit = true;
			db.Upsert("map_stats", mapTitle, stats);
			db.Upsert("map_stats", mapTitle.ToLowerInvariant(), stats);

			if (!string.IsNullOrWhiteSpace(req.MapVersion))
			{
				string compositeKey = $"{mapTitle}_{req.MapVersion.Trim()}";
				db.Upsert("map_stats", compositeKey, stats);
				db.Upsert("map_stats", compositeKey.ToLowerInvariant(), stats);
			}

			var greenlightEvent = new ClusterEventDto
			{
				EventType = "admin_greenlight",
				PublicKey = req.AdminPublicKey.Trim(),
				Signature = req.Signature.Trim(),
				PayloadJson = JsonSerializer.Serialize(new AdminGreenlightEventPayload
				{
					MapTitle = mapTitle,
					MapVersion = req.MapVersion,
					AdminPublicKey = req.AdminPublicKey.Trim(),
					Signature = req.Signature.Trim()
				})
			};
			clusterEvents.MarkEventProcessed(greenlightEvent.EventId, db);
			clusterEvents.BroadcastEvent(greenlightEvent, registeredPeers, httpClientFactory);

			return Results.Ok(new
			{
				Status = "Greenlit",
				MapTitle = mapTitle,
				IsGreenlit = true,
				AdminOverride = true
			});
		});
	}

	public static void MapBanEndpoints(WebApplication app, ServerSetup setup)
	{
		app.MapGet("/api/admin/bans", (DataStoreService db, HttpContext context) =>
		{
			if (!AdminRequestAuthorization.IsAuthorized(context.Request, setup.AdminPublicKeys, "admin", "bans")) return Results.Unauthorized();
			return Results.Ok(db.GetAll<JsonDocument>("bans"));
		});
		app.MapGet("/api/admin/bans/{id}", (string id, DataStoreService db, HttpContext context) =>
		{
			if (!AdminRequestAuthorization.IsAuthorized(context.Request, setup.AdminPublicKeys, "admin", "bans")) return Results.Unauthorized();
			var b = db.Get<JsonDocument>("bans", id);
			return b != null ? Results.Ok(b) : Results.NotFound();
		});
		app.MapPost("/api/admin/bans", (JsonDocument data, DataStoreService db, HttpContext context) =>
		{
			if (!AdminRequestAuthorization.IsAuthorized(context.Request, setup.AdminPublicKeys, "admin", "bans")) return Results.Unauthorized();
			var id = Guid.NewGuid().ToString("N");
			db.Upsert("bans", id, data);
			return Results.Ok(new { Id = id });
		});
		app.MapPut("/api/admin/bans/{id}", (string id, JsonDocument data, DataStoreService db, HttpContext context) =>
		{
			if (!AdminRequestAuthorization.IsAuthorized(context.Request, setup.AdminPublicKeys, "admin", "bans")) return Results.Unauthorized();
			db.Upsert("bans", id, data);
			return Results.Ok();
		});
		app.MapDelete("/api/admin/bans/{id}", (string id, DataStoreService db, HttpContext context) =>
		{
			if (!AdminRequestAuthorization.IsAuthorized(context.Request, setup.AdminPublicKeys, "admin", "bans")) return Results.Unauthorized();
			db.Delete("bans", id);
			return Results.Ok();
		});
	}
}
