using System.Collections.Concurrent;
using System.Globalization;
using System.Net;
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
using Realm.Shared.Reachability;
using Realm.AdminServer.Helpers;

namespace Realm.AdminServer;

/// <summary>Lobby registration, heartbeats, join and the host websocket.</summary>
public static class LobbyEndpoints
{
	public static void MapLobbyEndpoints(WebApplication app, ConcurrentDictionary<string, WebSocket> hostConnections, UdpReachabilityProbeService reachabilityProbe)
	{
	app.MapPost(ReachabilityRoutes.Begin, (ReachabilityBeginRequest req, HttpContext context) =>
	{
		string sourceAddress = (context.Connection.RemoteIpAddress?.MapToIPv4() ?? IPAddress.Loopback).ToString();
		ReachabilityEndpointReply reply = ReachabilityEndpointHandlers.Begin(reachabilityProbe, req, sourceAddress, context.Connection.RemotePort);

		if (reply.StatusCode == 200) Console.WriteLine($"[Reachability] Checking {req.AdvertisedIp}:{req.AdvertisedPort} for a lobby from {sourceAddress}.");
		else Console.WriteLine($"[Reachability] A lobby check could not start: {sourceAddress}");

		return Results.Json(reply.Body, statusCode: reply.StatusCode);
	});

	app.MapGet($"{ReachabilityRoutes.Report}/{{probeId}}", (string probeId) =>
	{
		ReachabilityEndpointReply reply = ReachabilityEndpointHandlers.Read(reachabilityProbe, probeId);
		return Results.Json(reply.Body, statusCode: reply.StatusCode);
	});

	app.MapGet("/lobbies", (LobbyRegistry registry, GeoIpService geoIp, HttpContext context) =>
	{
		var clientIpStr = context.Connection.RemoteIpAddress?.MapToIPv4().ToString() ?? "127.0.0.1";
		var clientCoords = geoIp.GetCoordinates(clientIpStr);

		var list = registry.GetAllLobbies().Where(lobby => !lobby.IsGameInProgress).Select(lobby =>
		{
			var distance = GeoIpService.CalculateDistance(
				clientCoords.lat, clientCoords.lon,
				lobby.Latitude, lobby.Longitude
			);


			int estimatedPing = 15 + (int)(distance / 100);
			const int localConnectionPingMilliseconds = 5;
			if (clientIpStr == lobby.HostIP || clientIpStr == "127.0.0.1" || lobby.HostIP == "127.0.0.1")
			{
				estimatedPing = localConnectionPingMilliseconds;
			}

			return new LobbyResponseDto(
				lobby.LobbyId,
				lobby.Map,
				lobby.HostIP,
				lobby.HostPort,
				lobby.NatType,
				lobby.SlotsUsed,
				lobby.MaxPlayers,
				lobby.Latitude,
				lobby.Longitude,
				distance,
				estimatedPing,
				lobby.OriginServerUri,
				lobby.HostPingBaseline,
				lobby.GameVersion,
				lobby.LocalIP,
				lobby.MapVersion,
				lobby.MapSizeBytes,
				lobby.IsGreenlit,
				lobby.MapCreator
			);
		});

		return Results.Ok(list);
	});


	app.MapPost("/lobbies/register", async (RegisterRequest req, LobbyRegistry registry, PeerRegistry registeredPeers, GeoIpService geoIp, IHttpClientFactory httpClientFactory, HttpContext context) =>
	{
		if (!string.IsNullOrEmpty(req.NatType) && req.NatType.Equals("Symmetric", StringComparison.OrdinalIgnoreCase))
		{
			return Results.BadRequest(new { Message = "Lobby creation rejected: Symmetric NAT is not supported." });
		}

		// A lobby is advertised only on the endpoint this node reached itself, so a host that cannot be reached is told
		// before it is registered rather than after every player who tries to join has waited out a timeout.
		if (!ReachabilityEndpointHandlers.IsLobbyVerified(reachabilityProbe, req.ReachabilityProbeId, out ReachabilityReport probeReport))
		{
			return Results.Json(ReachabilityEndpointHandlers.NotVerifiedRefusal(), statusCode: StatusCodes.Status400BadRequest);
		}

		var hostIp = context.Connection.RemoteIpAddress?.MapToIPv4().ToString() ?? "127.0.0.1";


		if (!string.IsNullOrEmpty(req.ReportedHostIP) && req.ReportedHostIP != "0.0.0.0" && req.ReportedHostIP != "127.0.0.1")
		{
			hostIp = req.ReportedHostIP;
		}

		int hostPort = req.HostPort;
		if (!string.IsNullOrEmpty(probeReport.ObservedIp) && probeReport.ObservedPort > 0)
		{
			hostIp = probeReport.ObservedIp;
			hostPort = probeReport.ObservedPort;
		}

		var hostCoords = geoIp.GetCoordinates(hostIp);
		var lobbyId = Guid.NewGuid().ToString();
		var hostToken = Guid.NewGuid().ToString();

		var db = context.RequestServices.GetRequiredService<DataStoreService>();

		bool isCustom = !string.IsNullOrEmpty(req.Signature) || !string.IsNullOrEmpty(req.PublicKey);

		if (isCustom)
		{
			if (string.IsNullOrEmpty(req.Signature) || string.IsNullOrEmpty(req.PublicKey) || string.IsNullOrEmpty(req.MapHash))
			{
				return Results.BadRequest(new { Message = "Map signature, public key, and hash are required for custom maps." });
			}

			try
			{
				byte[] pubKeyBytes = Convert.FromBase64String(req.PublicKey);
				byte[] sigBytes = Convert.FromBase64String(req.Signature);
				var pubKey = PublicKey.Import(SignatureAlgorithm.Ed25519, pubKeyBytes, KeyBlobFormat.RawPublicKey);
				byte[] hashBytes = Encoding.UTF8.GetBytes(req.MapHash);
				if (!SignatureAlgorithm.Ed25519.Verify(pubKey, hashBytes, sigBytes))
				{
					return Results.BadRequest(new { Message = "Invalid map signature." });
				}
			}
			catch
			{
				return Results.BadRequest(new { Message = "Invalid signature or public key format." });
			}

			if (MapMaintainerHelper.HasMapConflict(db, req.Map, req.PublicKey, out var conflictingMap))
			{
				return Results.BadRequest(new { Message = $"The map name '{req.Map}' conflicts with an officially published map ('{conflictingMap}'). Please rename your map to host a lobby." });
			}
		}

		string mapVersion = req.MapVersion ?? "1.0";
		string compositeKey = $"{req.Map}_{mapVersion}";
		var stats = MapStatsHelper.GetStats(db, req.Map, mapVersion, req.PublicKey);
		bool isGreenlit = !isCustom || (stats != null && stats.IsGreenlit);
		string? mapCreator = null;

		if (!isGreenlit)
		{
			string author = "Unknown";
			if (!string.IsNullOrEmpty(req.PublicKey))
			{
				var creator = db.Get<JsonDocument>("creators", req.PublicKey);
				if (creator != null && creator.RootElement.TryGetProperty("username", out var uProp))
				{
					author = uProp.GetString() ?? "Unknown";
				}
			}

			if (author == "Unknown")
			{
				var existingMap = db.Get<JsonDocument>("published_maps", compositeKey);
				if (existingMap != null)
				{
					var root = existingMap.RootElement;
					if (root.TryGetProperty("Contributors", out var conts) && conts.GetArrayLength() > 0)
					{
						author = conts[0].GetString() ?? "Unknown";
					}
				}
			}

			mapCreator = author;
		}

		var info = new LobbyInfo
		{
			LobbyId = lobbyId,
			Map = req.Map,
			MapVersion = mapVersion,
			HostIP = hostIp,
			HostPort = hostPort,
			NatType = req.NatType,
			PasswordHash = req.PasswordHash ?? "",
			MaxPlayers = req.MaxPlayers,
			SlotsUsed = req.SlotsUsed,
			GameVersion = req.GameVersion,
			Latitude = hostCoords.lat,
			Longitude = hostCoords.lon,
			LastHeartbeat = DateTime.UtcNow,
			OriginServerUri = registeredPeers.SelfUrl,
			HostToken = hostToken,
			HostPingBaseline = req.HostPingBaseline,
			LocalIP = req.LocalIP,
			MapSizeBytes = req.MapSizeBytes ?? 0,
			IsGreenlit = isGreenlit,
			MapCreator = mapCreator
		};

		registry.AddOrUpdate(info);

		_ = Task.Run(async () =>
		{
			using var httpClient = httpClientFactory.CreateClient();
			foreach (var peerUrl in registeredPeers.PeerUrls)
			{
				try
				{
					var content = new StringContent(JsonSerializer.Serialize(info), Encoding.UTF8, "application/json");
					var response = await httpClient.PostAsync($"{peerUrl.TrimEnd('/')}/lobbies/propagate", content);
					if (response.IsSuccessStatusCode)
					{
						Console.WriteLine($"[Registry] Propagated lobby {lobbyId} to peer {peerUrl}");
					}
					else
					{
						Console.WriteLine($"[Registry] Failed to propagate lobby {lobbyId} to peer {peerUrl}: {response.StatusCode}");
					}
				}
				catch (Exception ex)
				{
					Console.WriteLine($"[Registry] Error propagating lobby {lobbyId} to peer {peerUrl}: {ex.Message}");
				}
			}
		});

		return Results.Ok(new { LobbyId = lobbyId, Latitude = hostCoords.lat, Longitude = hostCoords.lon, HostToken = hostToken });
	});

	app.MapPost("/lobbies/propagate", (LobbyInfo propagatedLobby, LobbyRegistry registry) =>
	{
		if (!string.IsNullOrEmpty(propagatedLobby.NatType) && propagatedLobby.NatType.Equals("Symmetric", StringComparison.OrdinalIgnoreCase))
		{
			return Results.BadRequest(new { Message = "Symmetric NAT lobbies are not supported." });
		}

		registry.AddOrUpdate(propagatedLobby);
		Console.WriteLine($"[Registry] Received propagated lobby {propagatedLobby.LobbyId} from {propagatedLobby.OriginServerUri}");
		return Results.Ok(new { Status = "Propagated" });
	});


	app.MapPost("/lobbies/heartbeat", async (HeartbeatRequest req, LobbyRegistry registry, PeerRegistry registeredPeers, IHttpClientFactory httpClientFactory) =>
	{
		if (registry.TryGet(req.LobbyId, out var lobby) && lobby != null)
		{
			lobby.LastHeartbeat = DateTime.UtcNow;
			lobby.SlotsUsed = req.SlotsUsed;
			if (req.IsGameInProgress.HasValue)
			{
				lobby.IsGameInProgress = req.IsGameInProgress.Value;
			}


			_ = Task.Run(async () =>
			{
				using var httpClient = httpClientFactory.CreateClient();
				foreach (var peerUrl in registeredPeers.PeerUrls)
				{
					try
					{
						var content = new StringContent(JsonSerializer.Serialize(lobby), Encoding.UTF8, "application/json");
						var response = await httpClient.PostAsync($"{peerUrl.TrimEnd('/')}/lobbies/propagate", content);
						if (!response.IsSuccessStatusCode)
						{
							Console.WriteLine($"[Registry] Failed to propagate heartbeat for {lobby.LobbyId} to peer {peerUrl}: {response.StatusCode}");
						}
					}
					catch (Exception ex)
					{
						Console.WriteLine($"[Registry] Error propagating heartbeat for {lobby.LobbyId} to peer {peerUrl}: {ex.Message}");
					}
				}
			});

			return Results.Ok(new { Status = "Ok" });
		}

		return Results.NotFound(new { Message = "Lobby not found" });
	});

	app.MapPost("/lobbies/close", async (CloseLobbyRequest req, LobbyRegistry registry, PeerRegistry registeredPeers, IHttpClientFactory httpClientFactory) =>
	{
		if (registry.TryGet(req.LobbyId, out var lobby) && lobby != null)
		{
			if (lobby.HostToken == req.HostToken)
			{
				registry.TryRemove(req.LobbyId, out _);

				if (hostConnections.TryRemove(req.LobbyId, out var socket))
				{
					try
					{
						await socket.CloseAsync(WebSocketCloseStatus.NormalClosure, "Lobby closed by host", CancellationToken.None);
					}
					catch
					{
						/* Ignore socket close errors */
					}
				}

				_ = Task.Run(async () =>
				{
					using var httpClient = httpClientFactory.CreateClient();
					foreach (var peerUrl in registeredPeers.PeerUrls)
					{
						try
						{
							var content = new StringContent(JsonSerializer.Serialize(req), Encoding.UTF8, "application/json");
							await httpClient.PostAsync($"{peerUrl.TrimEnd('/')}/lobbies/propagate-close", content);
						}
						catch (Exception ex)
						{
							Console.WriteLine($"Error propagating close: {ex.Message}");
						}
					}
				});

				return Results.Ok(new { Status = "Closed" });
			}

			return Results.Json(new { Message = "Unauthorized" }, statusCode: StatusCodes.Status401Unauthorized);
		}

		return Results.NotFound(new { Message = "Lobby not found" });
	});

	app.MapPost("/lobbies/propagate-close", async (CloseLobbyRequest req, LobbyRegistry registry) =>
	{
		if (registry.TryGet(req.LobbyId, out var lobby) && lobby != null)
		{
			if (lobby.HostToken == req.HostToken)
			{
				registry.TryRemove(req.LobbyId, out _);

				if (hostConnections.TryRemove(req.LobbyId, out var socket))
				{
					try
					{
						await socket.CloseAsync(WebSocketCloseStatus.NormalClosure, "Lobby closed by host", CancellationToken.None);
					}
					catch
					{
						/* Ignore socket close errors */
					}
				}

				return Results.Ok(new { Status = "Closed" });
			}

			return Results.Json(new { Message = "Unauthorized" }, statusCode: StatusCodes.Status401Unauthorized);
		}

		return Results.NotFound(new { Message = "Lobby not found" });
	});


	app.MapPost("/lobbies/join", async (JoinRequest req, LobbyRegistry registry, IHttpClientFactory httpClientFactory, HttpContext context) =>
	{
		if (!registry.TryGet(req.LobbyId, out var lobby) || lobby == null)
		{
			return Results.NotFound(new { Message = "Lobby not found" });
		}

		var clientIp = req.ClientPublicIP;
		if (string.IsNullOrWhiteSpace(clientIp) || clientIp == "127.0.0.1" || clientIp == "0.0.0.0")
		{
			clientIp = context.Connection.RemoteIpAddress?.MapToIPv4().ToString() ?? clientIp;
		}

		var effectiveReq = req with { ClientPublicIP = clientIp };

		if (hostConnections.TryGetValue(req.LobbyId, out var hostSocket) && hostSocket.State == WebSocketState.Open)
		{
			var msg = JsonSerializer.Serialize(new
			{
				Action = "Punch",
				ClientIP = clientIp,
				ClientPort = req.ClientPublicPort
			});

			var bytes = Encoding.UTF8.GetBytes(msg);
			await hostSocket.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true, CancellationToken.None);

			Console.WriteLine($"[Registry] Relayed punch request to host {lobby.LobbyId} for client {clientIp}:{req.ClientPublicPort}");

			return Results.Ok(new JoinResponseDto(lobby.HostIP, lobby.HostPort, lobby.LocalIP));
		}
		else
		{
			if (!string.IsNullOrEmpty(lobby.OriginServerUri))
			{
				Console.WriteLine($"[Registry] Relaying join request for lobby {lobby.LobbyId} to origin {lobby.OriginServerUri}");
				try
				{
					using var httpClient = httpClientFactory.CreateClient();
					var content = new StringContent(JsonSerializer.Serialize(effectiveReq), Encoding.UTF8, "application/json");
					var response = await httpClient.PostAsync($"{lobby.OriginServerUri.TrimEnd('/')}/lobbies/join", content);
					if (response.IsSuccessStatusCode)
					{
						var respText = await response.Content.ReadAsStringAsync();
						var joinResp = JsonSerializer.Deserialize<JoinResponseDto>(respText, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
						if (joinResp != null)
						{
							return Results.Ok(joinResp);
						}
					}
					else
					{
						var errText = await response.Content.ReadAsStringAsync();
						return Results.BadRequest(new { Message = $"Relayed join failed: {errText}" });
					}
				}
				catch (Exception ex)
				{
					return Results.BadRequest(new { Message = $"Failed to relay join to origin: {ex.Message}" });
				}
			}
		}

		return Results.BadRequest(new { Message = "Host signaling channel is disconnected" });
	});


	app.Map("/lobbies/ws", async (HttpContext context, LobbyRegistry registry) =>
	{
		if (context.WebSockets.IsWebSocketRequest)
		{
			var lobbyId = context.Request.Query["lobbyId"].ToString();
			if (string.IsNullOrEmpty(lobbyId) || !registry.TryGet(lobbyId, out _))
			{
				context.Response.StatusCode = StatusCodes.Status400BadRequest;
				return;
			}

			using var webSocket = await context.WebSockets.AcceptWebSocketAsync();
			hostConnections[lobbyId] = webSocket;
			Console.WriteLine($"[Registry] Host connected WebSocket for Lobby {lobbyId}");

			var buffer = new byte[1024 * 4];
			while (webSocket.State == WebSocketState.Open)
			{
				var result = await webSocket.ReceiveAsync(new ArraySegment<byte>(buffer), CancellationToken.None);
				if (result.MessageType == WebSocketMessageType.Close)
				{
					await webSocket.CloseAsync(WebSocketCloseStatus.NormalClosure, "Closing", CancellationToken.None);
				}
			}

			hostConnections.TryRemove(lobbyId, out _);
			Console.WriteLine($"[Registry] Host disconnected WebSocket for Lobby {lobbyId}");
		}
		else
		{
			context.Response.StatusCode = StatusCodes.Status400BadRequest;
		}
	});
	}
}
