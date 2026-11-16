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
using Realm.Shared.Reachability;
using Realm.Shared.Metadata;
using Realm.AdminServer.Helpers;

namespace Realm.AdminServer;

/// <summary>Seeder registration and the public seeder listings.</summary>
public static class SeederEndpoints
{
	private const int SeederConfirmationTimeoutMilliseconds = 3000;

	public static void MapSeederEndpoints(WebApplication app, ServerSetup setup)
	{
	app.MapPost("/seeders/register", async (SeederRegisterRequest req, SeederRegistry registry, IHttpClientFactory httpClientFactory, HttpContext context) =>
	{
		var ip = (context.Connection.RemoteIpAddress ?? System.Net.IPAddress.Loopback).ToString();
		if (!string.IsNullOrEmpty(req.ReportedIP) && req.ReportedIP != "0.0.0.0" && req.ReportedIP != "127.0.0.1")
		{
			ip = req.ReportedIP;
		}

		// A seeder states where it believes players should reach it, and a seeder behind a router that forwards nothing
		// states an address that answers on its own network and nowhere else. Nothing about the statement itself can be
		// believed, so the endpoint is reached from here before it is offered: a seeder this node cannot reach is kept for the
		// players its own network serves and is not advertised to anybody else.
		bool confirmed = await ConfirmSeederEndpointAsync(registry, httpClientFactory, req.SeederId, ip, req.Port).ConfigureAwait(false);

		var info = new SeederInfo
		{
			SeederId = req.SeederId,
			IP = ip,
			Port = req.Port,
			MapIds = req.MapIds,
			CapacityPercentage = req.CapacityPercentage,
			AcceptingUploads = req.AcceptingUploads,
			LanOnly = !SeederAdvertisement.IsAdvertisable(ip, confirmed),
			ConfirmedAt = confirmed ? DateTime.UtcNow : null
		};
		registry.Register(info);

		if (info.LanOnly) Console.WriteLine($"[Seeder] {req.SeederId} states {ip}:{req.Port}, which this node could not reach from outside, so it is kept as a LAN-only seeder and not offered to remote players.");
		return Results.Ok(new { Status = "Registered", Advertised = !info.LanOnly });
	});

	app.MapPost("/seeders/unregister", (SeederUnregisterRequest req, SeederRegistry registry) =>
	{
		registry.Unregister(req.SeederId);
		return Results.Ok(new { Status = "Unregistered" });
	});

	app.MapGet("/seeders", (SeederRegistry registry) =>
	{
		var list = registry.GetAdvertised().Select(s => new
		{
			ip = s.IP,
			port = s.Port,
			mapIds = s.MapIds,
			capacityPercentage = s.CapacityPercentage,
			acceptingUploads = s.AcceptingUploads
		});
		return Results.Ok(list);
	});

	app.MapGet("/api/seeders", (SeederRegistry registry) =>
	{
		var list = registry.GetAdvertised().Select(s => new SeederNodeDto
		{
			SeederId = s.SeederId,
			IP = s.IP,
			Port = s.Port,
			CapacityPercentage = s.CapacityPercentage,
			AcceptingUploads = s.AcceptingUploads,
			MapIds = s.MapIds
		});
		return Results.Ok(list);
	});

	app.MapGet("/api/seeders/catalog", (ContentAddressableStorage cas) =>
	{
		return Results.Ok(new SeederCatalogResponseDto
		{
			SeederId = setup.SeederId,
			CapacityPercentage = setup.CapacityPercent,
			AssetHashes = cas.GetAllStoredHashes().ToList()
		});
	});

	}

	/// <summary>
	///     Reaches the seeder's stated endpoint from this node, which is on the public internet, so a success is evidence that
	///     a player elsewhere can reach it too. A confirmation still in date is believed rather than repeated: a seeder
	///     registers on a timer and a request per registration would be a lot of requests for one fact.
	/// </summary>
	private static async Task<bool> ConfirmSeederEndpointAsync(SeederRegistry registry, IHttpClientFactory httpClientFactory, string seederId, string ip, int port)
	{
		if (!ReachabilityAddresses.IsPubliclyRoutable(ip) || port is <= 0 or > 65535) return false;

		SeederInfo? existing = registry.Get(seederId);
		if (existing != null && SeederAdvertisement.IsConfirmationFresh(existing.ConfirmedAt, DateTime.UtcNow) && existing.IP == ip && existing.Port == port) return true;

		try
		{
			using var timeout = new CancellationTokenSource(SeederConfirmationTimeoutMilliseconds);
			using HttpClient client = httpClientFactory.CreateClient();
			using HttpResponseMessage response = await client.GetAsync($"http://{ip}:{port}/api/seeders/catalog", timeout.Token).ConfigureAwait(false);
			return response.IsSuccessStatusCode;
		}
		catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException or UriFormatException)
		{
			return false;
		}
	}

	public static void MapSeederDownloadEndpoints(WebApplication app, ServerSetup setup)
	{
		app.MapPost("/seeders/download", async (SeederDownloadRequest req, SeederRegistry registry) =>
		{
			var seeders = registry.GetSeedersForMap(req.MapId);
			if (seeders.Count == 0)
			{
				return Results.NotFound(new { Message = "No seeders found for this map", seeders = Array.Empty<object>() });
			}

			if (!string.IsNullOrEmpty(req.ClientPublicIP) && req.ClientPublicPort > 0)
			{
				foreach (var seeder in seeders)
				{
					var ws = registry.GetConnection(seeder.SeederId);
					if (ws != null && ws.State == WebSocketState.Open)
					{
						try
						{
							var msg = JsonSerializer.Serialize(new
							{
								Action = "Punch",
								ClientIP = req.ClientPublicIP,
								ClientPort = req.ClientPublicPort
							});
							var bytes = Encoding.UTF8.GetBytes(msg);
							await ws.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true, CancellationToken.None);
						}
						catch
						{
							registry.Unregister(seeder.SeederId);
						}
					}
				}
			}

			var resultList = seeders.Select(s => new { ip = s.IP, port = s.Port, seederId = s.SeederId }).ToList();
			return Results.Ok(new { seeders = resultList });
		});

		app.MapGet("/seeders/by_manifest/{manifestHash}", (string manifestHash, SeederRegistry registry) =>
		{
			var seeders = registry.GetSeedersForMap(manifestHash);
			var resultList = seeders.Select(s => new { ip = s.IP, port = s.Port, seederId = s.SeederId }).ToList();
			return Results.Ok(new { seeders = resultList });
		});
	}

	public static void MapSeederWebSocketEndpoints(WebApplication app, ServerSetup setup)
	{
		app.Map("/seeders/ws", async (HttpContext context, SeederRegistry registry) =>
		{
			if (context.WebSockets.IsWebSocketRequest)
			{
				var seederId = context.Request.Query["seederId"].ToString();
				if (string.IsNullOrEmpty(seederId))
				{
					context.Response.StatusCode = StatusCodes.Status400BadRequest;
					return;
				}

				using var webSocket = await context.WebSockets.AcceptWebSocketAsync();
				registry.AddConnection(seederId, webSocket);
				Console.WriteLine($"[Registry] Seeder connected WebSocket for Seeder {seederId}");

				var buffer = new byte[1024 * 4];
				try
				{
					while (webSocket.State == WebSocketState.Open)
					{
						var result = await webSocket.ReceiveAsync(new ArraySegment<byte>(buffer), CancellationToken.None);
						if (result.MessageType == WebSocketMessageType.Close)
						{
							await webSocket.CloseAsync(WebSocketCloseStatus.NormalClosure, "Closing", CancellationToken.None);
							break;
						}
					}
				}
				catch (Exception ex)
				{
					Console.WriteLine($"[Registry] Seeder {seederId} WebSocket error: {ex.Message}");
				}
				finally
				{
					registry.RemoveConnection(seederId);
					Console.WriteLine($"[Registry] Seeder disconnected WebSocket for Seeder {seederId}");
				}
			}
			else
			{
				context.Response.StatusCode = StatusCodes.Status400BadRequest;
			}
		});
	}
}
