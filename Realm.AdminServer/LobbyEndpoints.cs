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
	/// <summary>
	///     Asks a lobby's host to send from its game port to one joining player. The player's own connection attempt cannot
	///     make a router that drops unsolicited traffic deliver it, so the host has to speak first and needs only the endpoint
	///     this node saw the player arrive from.
	/// </summary>
	/// <param name="hostSocket">The lobby host's signalling channel.</param>
	/// <param name="lobbyId">The lobby being joined, for the log line.</param>
	/// <param name="clientIp">The address the joining player was seen at.</param>
	/// <param name="clientPort">The port the joining player was seen at.</param>
	private static async Task RelayPunchAsync(WebSocket hostSocket, string lobbyId, string clientIp, int clientPort)
	{
		var message = JsonSerializer.Serialize(new { Action = "Punch", ClientIP = clientIp, ClientPort = clientPort });
		byte[] bytes = Encoding.UTF8.GetBytes(message);
		await hostSocket.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true, CancellationToken.None);
		Console.WriteLine($"[Registry] Relayed punch request to host {lobbyId} for client {clientIp}:{clientPort}");
	}

	/// <summary>
	///     Reads what a lobby's host states back over its own signalling channel. The only thing a host says here is that it
	///     sent the punch a joining screen asked for, which the screen waits on before it sends anything toward the lobby.
	/// </summary>
	/// <param name="json">The message the host sent.</param>
	/// <param name="joinerRelay">The relay holding the joins that are waiting.</param>
	private static void ReadHostMessage(string json, JoinerPunchRelay joinerRelay)
	{
		try
		{
			using var document = JsonDocument.Parse(json);
			JsonElement root = document.RootElement;
			if (!root.TryGetProperty("Action", out JsonElement action) || action.GetString() != "Punched") return;
			if (!root.TryGetProperty("Token", out JsonElement token)) return;

			joinerRelay.AcknowledgePunch(token.GetString() ?? string.Empty);
		}
		catch (JsonException)
		{
		}
	}

	public static void MapLobbyEndpoints(WebApplication app, ConcurrentDictionary<string, WebSocket> hostConnections, UdpReachabilityProbeService reachabilityProbe, JoinerPunchRelay joinerRelay, JoinFailureFeedback joinFailureFeedback)
	{
	app.MapPost(ReachabilityRoutes.Begin, (ReachabilityBeginRequest req, HttpContext context) =>
	{
		string sourceAddress = (context.Connection.RemoteIpAddress?.MapToIPv4() ?? IPAddress.Loopback).ToString();
		ReachabilityEndpointReply reply = ReachabilityEndpointHandlers.Begin(reachabilityProbe, req, sourceAddress, context.Connection.RemotePort);

		if (reply.StatusCode == 200) Console.WriteLine($"[Reachability] Checking {req.AdvertisedIp}:{req.AdvertisedPort} for a lobby from {sourceAddress}.");
		else if (reply.StatusCode == ReachabilityStatusCodes.CallerOnOwnNetwork) Console.WriteLine($"[Reachability] Refused to check {req.AdvertisedIp}:{req.AdvertisedPort}: {sourceAddress} is inside this node's own network, so the check would only describe that network.");
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
				lobby.MapCreator,
				lobby.ReachabilityClass
			);
		});

		return Results.Ok(list);
	});


	app.MapPost("/lobbies/register", async (RegisterRequest req, LobbyRegistry registry, PeerRegistry registeredPeers, GeoIpService geoIp, IHttpClientFactory httpClientFactory, HttpContext context) =>
	{
		// The host's NAT classification is stored with the lobby and handed back to whoever lists it, so the kind of
		// network a lobby was hosted behind stays measured. It never decides whether the lobby is registered: the probe
		// below, and the connections players make afterwards, decide whether it can actually be reached.

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

		// The port a lobby is listed at is the port its game transport holds, not the port the check saw answers come
		// from. An external port a router hands out belongs to one destination, so a player sent to it is sent to a port
		// the host's router has no mapping for; the port the transport holds is the one a joining player punches, and the
		// punch is what makes the router deliver its packets.
		int hostPort = req.HostPort;
		if (hostPort <= 0 || hostPort > 65535)
		{
			return Results.Json(new
			{
				Code = ReachabilityErrorCodes.PortNotStated,
				Message = "This node cannot list a lobby that does not state the UDP port its game transport holds."
			}, statusCode: StatusCodes.Status400BadRequest);
		}

		string reachabilityClass = ReachabilityClasses.FromReport(probeReport);
		if (!ReachabilityClasses.IsPublishable(reachabilityClass))
		{
			return Results.Json(ReachabilityEndpointHandlers.NotVerifiedRefusal(), statusCode: StatusCodes.Status400BadRequest);
		}

		if (!string.IsNullOrEmpty(probeReport.ObservedIp)) hostIp = probeReport.ObservedIp;

		// The endpoint this node measured is the one every player is sent to, so it has to be one a player elsewhere can
		// use at all. A check that answered at a loopback or private address passed for a reason that says nothing about
		// the internet, and listing the lobby there would publish an address no stranger can reach.
		if (!ReachabilityAddresses.IsPubliclyRoutable(hostIp))
		{
			Console.WriteLine($"[Registry] Refused to register a lobby at '{hostIp}:{hostPort}': that endpoint is inside a network no other player can be sent to.");
			return Results.Json(new
			{
				Code = ReachabilityErrorCodes.EndpointNotPublic,
				Message = $"This node measured the lobby at '{hostIp}', which is not an address a player elsewhere can reach. A lobby is listed only at a public endpoint."
			}, statusCode: StatusCodes.Status400BadRequest);
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
			LocalPort = req.LocalPort,
			ReachabilityClass = reachabilityClass,
			MappingBehaviour = req.MappingBehaviour,
			ObservedPort = probeReport.ObservedPort,
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

		string reportedNatType = string.IsNullOrEmpty(req.NatType) ? "unclassified" : req.NatType;
		Console.WriteLine($"[Registry] Registered lobby {lobbyId} for map {req.Map} {mapVersion} at {hostIp}:{hostPort} behind {reportedNatType} NAT, {reachabilityClass} (measured port {probeReport.ObservedPort}, mapping {req.MappingBehaviour ?? "unknown"})");

		return Results.Ok(new { LobbyId = lobbyId, Latitude = hostCoords.lat, Longitude = hostCoords.lon, HostToken = hostToken });
	});

	app.MapPost("/lobbies/propagate", (LobbyInfo propagatedLobby, LobbyRegistry registry) =>
	{
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
				joinFailureFeedback.Forget(req.LobbyId);

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
		JoinerPunchTicket ticket = joinerRelay.Register(lobby.LobbyId);

		if (hostConnections.TryGetValue(req.LobbyId, out var hostSocket) && hostSocket.State == WebSocketState.Open)
		{
			// A player that states the endpoint its socket holds is punched without waiting for its report, which is what
			// a screen built before reporting existed does. A screen that states none reports itself from the socket its
			// connection attempt holds instead, because only this node can see the endpoint that attempt arrives from.
			if (req.ClientPublicPort > 0) await RelayPunchAsync(hostSocket, lobby.LobbyId, clientIp, req.ClientPublicPort);

			return Results.Ok(new JoinResponseDto(lobby.HostIP, lobby.HostPort, lobby.LocalIP, lobby.LocalPort, ticket.Port, ticket.Token, lobby.ReachabilityClass));
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
						// The reporting socket belongs to the node the player reached, not to the node that holds the lobby's
						// signalling channel, so the ticket this node states is the one the player has to report itself with.
						return Results.Ok(joinResp with { PunchPort = ticket.Port, PunchToken = ticket.Token });
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


	app.MapPost(JoinFailureRoutes.Report, async (JoinFailureReport report, HttpContext context) =>
	{
		string sourceAddress = context.Connection.RemoteIpAddress?.MapToIPv4().ToString() ?? "unknown";
		JoinFailureFeedback.Outcome outcome = await joinFailureFeedback.ReportAsync(report, sourceAddress);
		if (outcome == JoinFailureFeedback.Outcome.UnknownLobby) return Results.NotFound(new { Message = "That lobby is not listed here." });
		if (outcome == JoinFailureFeedback.Outcome.RateLimited) return Results.Json(new { Message = "Too many join failures were reported for this lobby or from this address." }, statusCode: StatusCodes.Status429TooManyRequests);
		return Results.Ok(new { Reported = outcome == JoinFailureFeedback.Outcome.Reported, Collapsed = outcome == JoinFailureFeedback.Outcome.Collapsed });
	});

	app.MapGet($"{JoinerPunchRoutes.State}/{{token}}", (string token) =>
	{
		JoinerPunchRelay.JoinPunchState state = joinerRelay.ReadState(token);
		return Results.Ok(new { Reported = state.Reported, Punched = state.Punched });
	});

	app.MapPost("/lobbies/punch", async (PunchRequest req, LobbyRegistry registry, HttpContext context) =>
	{
		if (!registry.TryGet(req.LobbyId, out var lobby) || lobby == null) return Results.NotFound(new { Message = "Lobby not found" });
		if (string.IsNullOrWhiteSpace(req.ClientIP) || req.ClientPort <= 0 || req.ClientPort > 65535) return Results.BadRequest(new { Message = "A punch needs the endpoint the joining player was seen at." });

		if (!hostConnections.TryGetValue(req.LobbyId, out var hostSocket) || hostSocket.State != WebSocketState.Open)
		{
			return Results.BadRequest(new { Message = "Host signaling channel is disconnected" });
		}			await RelayPunchAsync(hostSocket, req.LobbyId, req.ClientIP, req.ClientPort);
			return Results.Ok(new { Punched = true });
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
				else if (result.MessageType == WebSocketMessageType.Text)
				{
					ReadHostMessage(Encoding.UTF8.GetString(buffer, 0, result.Count), joinerRelay);
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
