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
		// The source address is kept in the family it arrived in. Rewriting it as IPv4 states an endpoint the request never
		// came from, which is the one address a check made over IPv6 can be handed for a host that stated none.
		string sourceAddress = (context.Connection.RemoteIpAddress ?? IPAddress.Loopback).ToString();
		ReachabilityEndpointReply reply = ReachabilityEndpointHandlers.Begin(reachabilityProbe, req, sourceAddress, context.Connection.RemotePort);

		if (reply.StatusCode == 200) Console.WriteLine($"[Reachability] Checking {req.AdvertisedIp}:{req.AdvertisedPort} for a lobby from {sourceAddress}.");
		else Console.WriteLine($"[Reachability] A lobby check could not start: {sourceAddress}");

		return Results.Json(reply.Body, statusCode: reply.StatusCode);
	});

	// A host is told which endpoint families this node can measure, so a host that holds only an IPv6 address learns that
	// this node cannot check it instead of having its lobby refused as unreachable.
	app.MapGet(ReachabilityRoutes.Capabilities, () =>
	{
		ReachabilityEndpointReply reply = ReachabilityEndpointHandlers.Capabilities(reachabilityProbe);
		return Results.Json(reply.Body, statusCode: reply.StatusCode);
	});

	app.MapGet($"{ReachabilityRoutes.Report}/{{probeId}}", (string probeId) =>
	{
		ReachabilityEndpointReply reply = ReachabilityEndpointHandlers.Read(reachabilityProbe, probeId);
		return Results.Json(reply.Body, statusCode: reply.StatusCode);
	});

	app.MapGet("/lobbies", (LobbyRegistry registry, GeoIpService geoIp, HttpContext context) =>
	{
		string clientIpStr = (context.Connection.RemoteIpAddress ?? IPAddress.Loopback).ToString();
		var clientCoords = geoIp.GetCoordinates(clientIpStr);

		var list = registry.GetAllLobbies().Where(lobby => !lobby.IsGameInProgress).Select(lobby =>
		{
			var distance = GeoIpService.CalculateDistance(
				clientCoords.lat, clientCoords.lon,
				lobby.Latitude, lobby.Longitude
			);


			int estimatedPing = 15 + (int)(distance / 100);
			const int localConnectionPingMilliseconds = 5;
			if (clientIpStr == lobby.HostIP || clientIpStr == "127.0.0.1" || lobby.HostIP == "127.0.0.1"
				|| clientIpStr == "::1" || lobby.HostIP == "::1" || lobby.Endpoints.Exists(endpoint => endpoint.Address == clientIpStr))
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
				lobby.Endpoints
			);
		});

		return Results.Ok(list);
	});


	app.MapPost("/lobbies/register", async (RegisterRequest req, LobbyRegistry registry, PeerRegistry registeredPeers, GeoIpService geoIp, IHttpClientFactory httpClientFactory, HttpContext context) =>
	{
		// The host's NAT classification is stored with the lobby and handed back to whoever lists it, so the kind of
		// network a lobby was hosted behind stays measured. It never decides whether the lobby is registered: the probe
		// below, and the connections players make afterwards, decide whether it can actually be reached.

		// A lobby is advertised only on the endpoints this node reached itself, so a host that cannot be reached is told
		// before it is registered rather than after every player who tries to join has waited out a timeout. Every
		// endpoint is judged in its own family: an IPv4 endpoint that answered and an IPv6 endpoint that did not publish
		// the lobby over IPv4, and the family that did not answer is dropped rather than guessed at.
		List<LobbyEndpointInfo> endpoints;
		string hostIp;
		int hostPort;

		if (req.Endpoints is { Count: > 0 })
		{
			endpoints = VerifyPublishedEndpoints(reachabilityProbe, req.Endpoints, out List<string> uncheckedFamilies);
			if (endpoints.Count == 0)
			{
				return Results.Json(
					uncheckedFamilies.Count > 0
						? ReachabilityEndpointHandlers.FamilyNotCheckedRefusal(string.Join(" and ", uncheckedFamilies))
						: ReachabilityEndpointHandlers.NotVerifiedRefusal(),
					statusCode: StatusCodes.Status400BadRequest);
			}

			LobbyEndpointInfo primary = PrimaryEndpoint(endpoints);
			hostIp = primary.DialAddress;
			hostPort = primary.DialPort;
		}
		else
		{
			// A client that states no endpoint list speaks the single-endpoint protocol, which only ever measured IPv4.
			if (!ReachabilityEndpointHandlers.IsLobbyVerified(reachabilityProbe, req.ReachabilityProbeId, out ReachabilityReport probeReport))
			{
				return Results.Json(ReachabilityEndpointHandlers.NotVerifiedRefusal(), statusCode: StatusCodes.Status400BadRequest);
			}

			hostIp = (context.Connection.RemoteIpAddress ?? IPAddress.Loopback).ToString();

			if (!string.IsNullOrEmpty(req.ReportedHostIP) && req.ReportedHostIP != "0.0.0.0" && req.ReportedHostIP != "127.0.0.1")
			{
				hostIp = req.ReportedHostIP;
			}

			hostPort = req.HostPort;
			if (!string.IsNullOrEmpty(probeReport.ObservedIp) && probeReport.ObservedPort > 0)
			{
				hostIp = probeReport.ObservedIp;
				hostPort = probeReport.ObservedPort;
			}

			endpoints = new List<LobbyEndpointInfo>(1)
			{
				new(hostIp, hostPort, LobbyAddresses.FamilyName(LobbyAddresses.FamilyOf(hostIp)), true, hostIp, hostPort)
			};
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
			MapCreator = mapCreator,
			Endpoints = endpoints
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
		Console.WriteLine($"[Registry] Registered lobby {lobbyId} for map {req.Map} {mapVersion} at {hostIp}:{hostPort} behind {reportedNatType} NAT");

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
			clientIp = (context.Connection.RemoteIpAddress ?? IPAddress.Loopback).ToString();
		}

		var effectiveReq = req with { ClientPublicIP = clientIp };

		// The endpoint this player will dial is chosen by the same rule the player's own screen uses, so the punch this
		// node relays is made toward the endpoint the player actually connects to. An IPv6 endpoint needs no punch at
		// all: there is no router in front of it and nothing to open.
		LobbyEndpointChoice choice = LobbyEndpointSelection.Choose(lobby.Endpoints, hasIpv4: true, hasIpv6: ClientCanUseIpv6(req));
		bool punchNeeded = choice.Chosen ? choice.NeedsHolePunch : LobbyAddresses.NeedsHolePunch(LobbyAddresses.FamilyOf(lobby.HostIP));
		var response = new JoinResponseDto(lobby.HostIP, lobby.HostPort, lobby.LocalIP, lobby.Endpoints);

		if (hostConnections.TryGetValue(req.LobbyId, out var hostSocket) && hostSocket.State == WebSocketState.Open)
		{
			if (punchNeeded)
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
			}

			return Results.Ok(response);
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
					var httpResponse = await httpClient.PostAsync($"{lobby.OriginServerUri.TrimEnd('/')}/lobbies/join", content);
					if (httpResponse.IsSuccessStatusCode)
					{
						var respText = await httpResponse.Content.ReadAsStringAsync();
						var joinResp = JsonSerializer.Deserialize<JoinResponseDto>(respText, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
						if (joinResp != null)
						{
							return Results.Ok(joinResp);
						}
					}
					else
					{
						var errText = await httpResponse.Content.ReadAsStringAsync();
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

	/// <summary>
	///     Checks every endpoint a host offered and keeps the ones this node reached itself. Each endpoint is judged in its
	///     own family, from this node's socket of that family, so an IPv6 endpoint that a filtered network drops is stated as
	///     unverified instead of having its lobby refused, and an endpoint the host could not ask this node to check is named
	///     as this node's own limitation.
	/// </summary>
	/// <param name="probe">This node's probe service.</param>
	/// <param name="offered">The endpoints the host offered.</param>
	/// <param name="uncheckedFamilies">The families this node has no probe socket for, when the host offered one.</param>
	/// <returns>The endpoints that answered, one per family.</returns>
	private static List<LobbyEndpointInfo> VerifyPublishedEndpoints(UdpReachabilityProbeService probe, IReadOnlyList<LobbyEndpointRegistration> offered, out List<string> uncheckedFamilies)
	{
		var verified = new List<LobbyEndpointInfo>(offered.Count);
		uncheckedFamilies = new List<string>(1);

		for (int index = 0; index < offered.Count; index++)
		{
			LobbyEndpointRegistration endpoint = offered[index];
			if (endpoint == null) continue;
			if (endpoint.Port <= 0 || endpoint.Port > 65535) continue;

			LobbyAddressFamily family = LobbyAddresses.ParseFamilyName(endpoint.Family);
			if (family == LobbyAddressFamily.Unknown) family = LobbyAddresses.FamilyOf(endpoint.Address);
			if (family == LobbyAddressFamily.Unknown) continue;
			if (verified.Exists(existing => existing.FamilyValue == family)) continue;

			if (string.IsNullOrEmpty(endpoint.ProbeId))
			{
				// A host states no check for an endpoint it could not ask this node to measure. When the reason is a family
				// this node has no socket in, that is this node's limit and not a lobby that cannot be reached.
				string name = LobbyAddresses.FamilyName(family);
				if (!probe.HasSocketFor(family) && !uncheckedFamilies.Contains(name)) uncheckedFamilies.Add(name);
				continue;
			}

			if (!ReachabilityEndpointHandlers.IsLobbyVerified(probe, endpoint.ProbeId, out ReachabilityReport report)) continue;

			string observedIp = string.IsNullOrEmpty(report.ObservedIp) ? endpoint.Address : report.ObservedIp;
			int observedPort = report.ObservedPort > 0 ? report.ObservedPort : endpoint.Port;
			verified.Add(new LobbyEndpointInfo(observedIp, observedPort, LobbyAddresses.FamilyName(family), true, observedIp, observedPort));
		}

		return verified;
	}

	/// <summary>
	///     The endpoint a lobby is listed at, and the one a client that cannot read the endpoint list dials. IPv4 is
	///     preferred because that is the family a client of any age can use, so a lobby published in both families stays
	///     reachable to everybody.
	/// </summary>
	/// <param name="endpoints">The verified endpoints.</param>
	/// <returns>The primary endpoint.</returns>
	private static LobbyEndpointInfo PrimaryEndpoint(List<LobbyEndpointInfo> endpoints)
	{
		LobbyEndpointInfo? ipv4 = endpoints.Find(endpoint => endpoint.FamilyValue == LobbyAddressFamily.IPv4);
		return ipv4 ?? endpoints[0];
	}

	/// <summary>Whether a joining player stated that it holds an IPv6 address, so this node can tell whether it needs a punch.</summary>
	/// <param name="req">The join request.</param>
	/// <returns>Whether the player can dial an IPv6 endpoint.</returns>
	private static bool ClientCanUseIpv6(JoinRequest req)
	{
		for (int index = 0; req.ClientEndpointFamilies != null && index < req.ClientEndpointFamilies.Count; index++)
		{
			if (LobbyAddresses.ParseFamilyName(req.ClientEndpointFamilies[index]) == LobbyAddressFamily.IPv6) return true;
		}

		return false;
	}
}
