using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Godot;
using Realm.Client.Core;
using Realm.Client.Services;
using Realm.Client.UI;
using Realm.Client.Utils;
using Realm.Shared;
using Realm.Shared.Accounts;
using Realm.Shared.Distribution;
using Realm.Shared.Maps;
using Realm.Shared.Metadata;
using Realm.Shared.Network;
using Realm.Shared.Reachability;
using Realm.Shared.Services;
using Environment = System.Environment;
using FileAccess = System.IO.FileAccess;
using HttpClient = System.Net.Http.HttpClient;

namespace Realm.Client.Network;
public partial class LobbyManager
{
	/// <summary>
	///     Asks a registry node where a lobby is and states the address this screen joins from. The port this screen's own ENet
	///     socket will hold is not stated, because the socket is created when the join connects: the screen reports itself from
	///     that socket once it exists, and the registry node reads the endpoint the report arrived from and hands it to the
	///     host, which is the only way a host behind a filtering router can be told where to send.
	/// </summary>
	/// <param name="lobbyId">The lobby being joined.</param>
	/// <param name="clientPublicIp">The address this network is reached at from outside, as this screen measured it.</param>
	/// <returns>The registry node's answer, or null when no node answered.</returns>
	private async Task<HttpResponseMessage?> SendJoinRequestAsync(string lobbyId, string clientPublicIp)
	{
		var joinPayload = new
		{
			LobbyId = lobbyId,
			ClientPublicIP = clientPublicIp,
			ClientPublicPort = 0
		};

		GameLog.LogToConsole($"[LobbyManager] Joining Lobby {lobbyId} via registry server...");
		HttpResponseMessage? response = null;
		for (int i = 0; i < RegistryServers.Count; i++)
		{
			try
			{
				var jsonContent = new StringContent(JsonSerializer.Serialize(joinPayload), Encoding.UTF8, "application/json");
				response = await _httpClient.PostAsync($"{RegistryServerUrl}/lobbies/join", jsonContent);
				if (response.IsSuccessStatusCode) break;
			}
			catch (Exception ex)
			{
				GameLog.LogError($"[LobbyManager] Join failed on {RegistryServerUrl}: {ex.Message}");
			}

			SwitchToNextServer();
		}

		return response;
	}

	/// <summary>
	///     Reads where to connect and how to report this screen to the registry's host relay. Every field is read without
	///     assuming it is present: a lobby registered by an older node answers with fewer of them, and a missing field has to
	///     produce a stated reason rather than an exception message.
	/// </summary>
	/// <param name="response">The registry node's answer.</param>
	/// <returns>The endpoint to connect to and the ticket this screen reports itself with.</returns>
	private async Task<LobbyJoinTarget> ResolveHostConnectionInfoAsync(HttpResponseMessage response)
	{
		string respText = await response.Content.ReadAsStringAsync();
		using var doc = JsonDocument.Parse(respText);
		JsonElement root = doc.RootElement;

		string hostIp = root.TryGetProperty("hostIP", out JsonElement hostIpElement) ? hostIpElement.GetString() ?? string.Empty : string.Empty;
		int hostPort = root.TryGetProperty("hostPort", out JsonElement hostPortElement) ? hostPortElement.GetInt32() : 0;

		string localIp = root.TryGetProperty("localIP", out JsonElement localIpElement) ? localIpElement.GetString() ?? string.Empty : string.Empty;
		int localPort = root.TryGetProperty("localPort", out JsonElement localPortElement) && localPortElement.ValueKind == JsonValueKind.Number ? localPortElement.GetInt32() : hostPort;

		int punchPort = root.TryGetProperty("punchPort", out JsonElement punchPortElement) && punchPortElement.ValueKind == JsonValueKind.Number ? punchPortElement.GetInt32() : 0;
		string punchToken = root.TryGetProperty("punchToken", out JsonElement punchTokenElement) ? punchTokenElement.GetString() ?? string.Empty : string.Empty;

		var punch = new JoinerPunchTicket(RegistryServerHost, punchPort, punchToken);

		bool localAddressKnown = !string.IsNullOrEmpty(localIp) && LocalNetwork.Contains(localIp);
		if (localAddressKnown)
		{
			GameLog.LogToConsole($"[LobbyManager] The host offers {localIp}:{localPort} on a network this machine is attached to, so the join goes there first.");
			return new LobbyJoinTarget(localIp, localPort, true, punch);
		}

		return new LobbyJoinTarget(hostIp, hostPort, false, punch);
	}

	/// <summary>The host name of the registry node this screen is talking to, which is where a join report is sent.</summary>
	private string RegistryServerHost => Uri.TryCreate(RegistryServerUrl, UriKind.Absolute, out Uri? uri) ? uri.Host : string.Empty;

	public void UnregisterActiveLobbyFromRegistry()
	{
		if (IsHost && !string.IsNullOrEmpty(ActiveLobbyId) && !string.IsNullOrEmpty(_hostToken))
		{
			string lobbyIdToClose = ActiveLobbyId;
			string tokenToClose = _hostToken;
			Task.Run(async () =>
			{
				try
				{
					var payload = new { LobbyId = lobbyIdToClose, HostToken = tokenToClose };
					var jsonContent = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");
					await _httpClient.PostAsync($"{RegistryServerUrl}/lobbies/close", jsonContent);
				}
				catch (Exception ex)
				{
					GameLog.LogError($"[LobbyManager] Failed to close lobby {lobbyIdToClose} on server: {ex.Message}");
				}
			});
			_hostToken = null;
		}

		_wsCts?.Cancel();
		_hostWebSocket?.Dispose();
		_hostWebSocket = null;
		ActiveLobbyId = null;
	}

	private void ReleaseHostedEndpoint(int port)
	{
		StopHostReachabilityKeepAlive();
		_publishedReachability = null;

		if (!IsHost) return;
		if (IsSinglePlayer) return;

		Diagnostics.StopHostListener();
		GameContext.Upnp.ClosePort(port > 0 ? port : ENetPort);
	}

	public void Disconnect()
	{
		GameLog.LogToConsole("[LobbyManager] Disconnecting...");

		ClientServerSession? session = _lobbySession;
		int hostedPort = session?.Port ?? ENetPort;
		bool hostedHere = IsHost;
		_lobbySession = null;

		UnregisterActiveLobbyFromRegistry();

		if (session != null)
		{
			DetachLobbySession(session);
			if (ReferenceEquals(GameContext.ServerSession, session)) GameContext.DetachServerSession();

			if (!hostedHere) session.SendLobbyCommand(new LobbyCommand { Kind = LobbyCommandKind.Leave });
			if (hostedHere) session.Dispose();
			else Callable.From(session.Dispose).CallDeferred();
		}

		ReleaseHostedEndpoint(hostedPort);
		ResetLobbyState();
	}

	private void ResetLobbyState()
	{
		_preparation.Cancel();

		bool hadPreparedWorld = _worldPrepared || GameContext.Host?.IsBuildingMatchWorld == true;
		_worldPrepared = false;
		_worldPreparedMapName = null;
		_loadReadyReported = false;
		_matchPresentationOpen = false;
		_isConnectedToHost = false;
		IsDirectLobby = false;
		IsHost = false;
		IsGameStarted = false;
		_countdownActive = false;
		_countdownRemaining = 0;
		_countdownMapName = null;
		_matchStarted = false;
		_lobbyRevision = 0;
		_downloadingMapName = null;
		PlayerList.Clear();
		PlayerListUpdated?.Invoke();

		GameContext.Host?.DiscardPreparedMatchWorld();
		if (hadPreparedWorld) GameContext.Host?.EndMatchAndClearWorld();
	}

	private bool _matchStarted;

	private void TeardownHostedLobby(ClientServerSession? launchedSession = null)
	{
		ClientServerSession? session = _lobbySession ?? launchedSession;
		int hostedPort = session?.Port ?? ENetPort;
		_lobbySession = null;
		if (session != null)
		{
			DetachLobbySession(session);
			session.Dispose();
		}

		UnregisterActiveLobbyFromRegistry();
		ReleaseHostedEndpoint(hostedPort);
		ResetLobbyState();
	}

	private void StartHostWebSocketSignaling(string lobbyId)
	{
		_wsCts = new CancellationTokenSource();
		var token = _wsCts.Token;

		Task.Run(async () =>
		{
			_hostWebSocket = new ClientWebSocket();
			var wsUrl = RegistryServerUrl.Replace("http://", "ws://").Replace("https://", "wss://");
			var uri = new Uri($"{wsUrl}/lobbies/ws?lobbyId={lobbyId}");

			try
			{
				await _hostWebSocket.ConnectAsync(uri, token);
				GameLog.LogToConsole("[LobbyManager] Host WebSocket signaling connected.");
				await ProcessWebSocketMessagesAsync(token);
			}
			catch (Exception ex) when (ex is OperationCanceledException || ex is ObjectDisposedException)
			{
			}
			catch (Exception ex)
			{
				GameLog.LogError($"[LobbyManager] Host WebSocket error: {ex.Message}");
			}
		}, token);
	}

	private async Task ProcessWebSocketMessagesAsync(CancellationToken token)
	{
		byte[] buffer = new byte[1024 * 4];
		while (_hostWebSocket!.State == WebSocketState.Open && !token.IsCancellationRequested)
		{
			var result = await _hostWebSocket.ReceiveAsync(new ArraySegment<byte>(buffer), token);
			if (result.MessageType == WebSocketMessageType.Close) break;

			if (result.MessageType == WebSocketMessageType.Text)
			{
				var json = Encoding.UTF8.GetString(buffer, 0, result.Count);
				HandleWebSocketMessage(json);
			}
		}

		GameLog.LogToConsole("[LobbyManager] Host WebSocket message processing loop ended.");
	}

	private void HandleWebSocketMessage(string json)
	{
		try
		{
			using var doc = JsonDocument.Parse(json);
			var root = doc.RootElement;

			if (root.GetProperty("Action").GetString() == "Punch")
			{
				string clientIp = root.GetProperty("ClientIP").GetString() ?? "";
				int clientPort = root.GetProperty("ClientPort").GetInt32();
				string token = root.TryGetProperty("Token", out JsonElement tokenElement) ? tokenElement.GetString() ?? string.Empty : string.Empty;
				Callable.From(() => SendPunchRequest(clientIp, clientPort, token)).CallDeferred();
			}
			else if (root.GetProperty("Action").GetString() == "JoinFailed")
			{
				string phase = root.TryGetProperty("Phase", out JsonElement phaseElement) ? phaseElement.GetString() ?? string.Empty : string.Empty;
				string endpoint = root.TryGetProperty("Endpoint", out JsonElement endpointElement) ? endpointElement.GetString() ?? string.Empty : string.Empty;
				int distinctFailures = root.TryGetProperty("DistinctFailures", out JsonElement countElement) && countElement.ValueKind == JsonValueKind.Number ? countElement.GetInt32() : 0;
				string reachabilityClass = root.TryGetProperty("ReachabilityClass", out JsonElement classElement) ? classElement.GetString() ?? string.Empty : string.Empty;
				Callable.From(() => ReportJoinFailureToLobby(phase, endpoint, distinctFailures, reachabilityClass)).CallDeferred();
			}
		}
		catch (Exception ex)
		{
			GameLog.LogError($"[LobbyManager] Error handling WebSocket message: {ex.Message}");
		}
	}

	/// <summary>
	///     Asks the lobby's authority to send from the game port to a joining player, and states back to the registry node that
	///     it did. The joining screen waits for that statement before its socket sends anything toward this lobby, because a
	///     player's packet that arrives before the punch takes the port the punch would have used and leaves the punch
	///     translated to a port no player knows.
	/// </summary>
	/// <param name="clientIp">The address the joining player was seen at.</param>
	/// <param name="clientPort">The port the joining player was seen at.</param>
	/// <param name="token">The join the punch was asked for, or empty for a request that names none.</param>
	private void SendPunchRequest(string clientIp, int clientPort, string token)
	{
		if (string.IsNullOrEmpty(clientIp) || clientPort <= 0) return;

		bool sent = _lobbySession?.SendLobbyCommand(new LobbyCommand
		{
			Kind = LobbyCommandKind.PunchAddress,
			Address = clientIp,
			Port = clientPort
		}) ?? false;

		if (!sent || token.Length == 0) return;
		_ = SendHostSignalingAsync(new { Action = "Punched", Token = token });
	}

	/// <summary>
	///     Writes a joining player's failure into this lobby's own chat, so the host reads it where it can act on it and every
	///     peer sees the same line. The sentence is composed here, in the language this screen is running in, and the lobby's
	///     authority is what stores and broadcasts it; the address, the step and the count are the only things taken from the
	///     report, and none of them is rendered as anything but part of this sentence.
	/// </summary>
	/// <param name="phase">The step of the handshake that failed.</param>
	/// <param name="endpoint">The endpoint the player tried.</param>
	/// <param name="distinctFailures">How many distinct players failed in the last minute.</param>
	/// <param name="reachabilityClass">The class the lobby is listed with now.</param>
	private void ReportJoinFailureToLobby(string phase, string endpoint, int distinctFailures, string reachabilityClass)
	{
		if (!IsHost) return;

		string step = phase switch
		{
			JoinPhases.Registry => Tr("the lobby could not be found through the lobby server"),
			JoinPhases.Resolve => Tr("the lobby server did not say where the lobby is"),
			JoinPhases.Report => Tr("the player could not report where it is, so this lobby could not send toward it"),
			JoinPhases.Punch => Tr("the player never received the answer this lobby sent toward it"),
			JoinPhases.Answer => Tr("this lobby's game port never answered the player"),
			JoinPhases.Admission => Tr("this lobby refused the player"),
			JoinPhases.Session => Tr("this lobby admitted the player but never described itself to it"),
			_ => Tr("the handshake failed")
		};

		string advice = reachabilityClass == ReachabilityClasses.Unverified
			? Tr("This lobby is no longer listed as reachable; enable UPnP on the router, forward this UDP port, or share the direct address meanwhile.")
			: MappingBehaviour == MappingBehaviours.PerDestination
				? Tr("This router gives every destination a different outside port, so punching cannot open it; forward this UDP port, or use a VPN or an overlay network.")
				: Tr("Other players may need UPnP enabled on this router, or this UDP port forwarded to this machine.");

		int players = Math.Max(1, distinctFailures);
		_lobbySession?.SendLobbyCommand(new LobbyCommand
		{
			Kind = LobbyCommandKind.ReportJoinFailure,
			Text = string.Format(Tr("{0} player(s) could not reach this lobby in the last minute: {1}; they tried {2}. {3}"), players, step, endpoint, advice)
		});
	}

	/// <summary>
	///     Sends one message to the registry node holding this lobby, from the signalling channel the punch requests arrive on.
	///     It is fired and forgotten because a punch that has been asked for must not be delayed by the report of itself.
	/// </summary>
	/// <param name="message">The message to state.</param>
	private async Task SendHostSignalingAsync(object message)
	{
		ClientWebSocket? socket = _hostWebSocket;
		if (socket == null || socket.State != WebSocketState.Open) return;

		try
		{
			byte[] bytes = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(message));
			await socket.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true, CancellationToken.None);
		}
		catch (Exception exception) when (exception is WebSocketException or ObjectDisposedException or InvalidOperationException)
		{
			GameLog.LogToConsole($"[LobbyManager] A message to the lobby's registry node could not be sent: {exception.Message}");
		}
	}

	private void StartHeartbeatLoop(string lobbyId)
	{
		var token = _wsCts!.Token;
		Task.Run(async () =>
		{
			while (!token.IsCancellationRequested)
			{
				try
				{
					var heartbeat = new { LobbyId = lobbyId, SlotsUsed = PlayerList.Count, IsGameInProgress = IsGameStarted };
					var jsonContent = new StringContent(JsonSerializer.Serialize(heartbeat), Encoding.UTF8, "application/json");
					await _httpClient.PostAsync($"{RegistryServerUrl}/lobbies/heartbeat", jsonContent, token);
				}
				catch (Exception ex)
				{
					GameLog.LogError($"the lobby heartbeat to {RegistryServerUrl} failed, so the registry may drop this lobby: {ex.GetType().Name}: {ex.Message}");
				}

				await Task.Delay(1000, token);
			}
		}, token);
	}

	private async Task<bool> RegisterLobbyWithRegistryAsync(string mapPathName, string mapDisplayName, string? explicitVersion, int lobbyPort)
	{
		int attempts = Math.Max(1, RegistryServers.Count);

		for (int attempt = 0; attempt < attempts; attempt++)
		{
			// The lobby is published only on an endpoint a public node has reached, and every registry node publishes
			// only what it checked itself, so the rehearsal is run against the node that will accept the lobby.
			ReportHostLobbyStage(HostLobbyStage.VerifyingNetwork);
			HostReachabilityResult reachability = await VerifyLobbyReachabilityAsync(RegistryServerUrl, lobbyPort);
			HostReachabilityFailure decision = LobbyJoinability.Decide(publicLobby: true, LoopbackJoinOutcome.Answered, reachability.Failure, ReachabilityAddresses.IsPubliclyRoutable(reachability.PublicIp));
			if (decision != HostReachabilityFailure.None)
			{
				LastHostFailure = reachability;
				LastHostError = DescribeReachabilityFailure(reachability, lobbyPort);
				GameLog.LogError($"[LobbyManager] {LastHostError}");
				return false;
			}

			_hostPublicIp = reachability.PublicIp;
			_hostPublicPort = reachability.PublicPort;
			ReachabilityClass = reachability.ReachabilityClass;
			MappingBehaviour = reachability.MappingBehaviour;
			_routerReachability = RouterReachability.Confirmed;

			ReportHostLobbyStage(HostLobbyStage.RegisteringLobby);

			try
			{
				int hostPingBaseline = await MeasurePingToRegistryAsync();
				var mapInfo = GetMapMetadataInfo(mapPathName, mapDisplayName, explicitVersion);

				var registerPayload = new
				{
					Map = mapDisplayName,
					// The port a joining player must use is the one this lobby's transport holds: an external port a check
					// observed belongs to one router mapping for one destination, and a player sent to it would be sent to a
					// port the host's router has no mapping for.
					HostPort = lobbyPort,
					NatType = LocalNatType.ToString(),
					ReportedHostIP = reachability.PublicIp,
					PasswordHash = "",
					MaxPlayers = OpenSlotCount,
					SlotsUsed = PlayerList.Count,
					HostPingBaseline = hostPingBaseline,
					GameVersion = RealmVersion.GameBinaryVersion,
					LocalIP = GetLocalIPAddress(),
					LocalPort = lobbyPort,
					MapVersion = mapInfo.Version,
					Signature = mapInfo.Signature,
					PublicKey = mapInfo.PublicKey,
					MapHash = mapInfo.MapHash,
					MapSizeBytes = mapInfo.SizeBytes,
					ReachabilityProbeId = reachability.ProbeId,
					MappingBehaviour = reachability.MappingBehaviour,
					ObservedPort = reachability.ObservedPort
				};

				var jsonContent = new StringContent(JsonSerializer.Serialize(registerPayload), Encoding.UTF8, "application/json");
				using HttpResponseMessage response = await _httpClient.PostAsync($"{RegistryServerUrl}/lobbies/register", jsonContent);

				if (response.IsSuccessStatusCode)
				{
					_publishedReachability = reachability;
					await ProcessSuccessfulRegistrationAsync(response);
					return true;
				}

				if (await ReadRegistrationErrorCodeAsync(response) != ReachabilityErrorCodes.NotVerified)
				{
					return await HandleRegisterFailureAsync(response);
				}

				GameLog.LogToConsole($"[LobbyManager] {RegistryServerUrl} did not accept the reachability check this screen ran, so it is checked again against the next registry node.");
			}
			catch (Exception ex)
			{
				GameLog.LogError($"[LobbyManager] Registry registration error on {RegistryServerUrl}: {ex.Message}");
				LastHostError = ex.Message;
			}

			if (attempt + 1 >= attempts) break;
			SwitchToNextServer();
		}

		LastHostError ??= "Registry server registration failed.";
		return false;
	}

	private static string DescribeReachabilityFailure(HostReachabilityResult reachability, int lobbyPort)
	{
		return reachability.Failure switch
		{
			HostReachabilityFailure.RegistryUnreachable => "The lobby server could not be reached, so whether other players can reach this machine was never checked.",
			HostReachabilityFailure.ProbeSocketUnknown => "The lobby server could not say where its reachability check comes from, so this lobby was not published.",
			HostReachabilityFailure.ProbeFromOwnNetwork => "The lobby server reached this machine from inside its own network, so the check only proved that players sharing that network can see the lobby. Point the game at a lobby server on the internet and host again.",
			_ => $"Other players could not reach this machine on UDP port {lobbyPort}, so this lobby was not published ({reachability.Detail})."
		};
	}

	private static async Task<string> ReadRegistrationErrorCodeAsync(HttpResponseMessage? response)
	{
		if (response == null) return string.Empty;

		try
		{
			string body = await response.Content.ReadAsStringAsync();
			using var document = JsonDocument.Parse(body);
			foreach (string propertyName in new[] { "code", "Code" })
			{
				if (document.RootElement.TryGetProperty(propertyName, out JsonElement codeElement) && codeElement.ValueKind == JsonValueKind.String)
				{
					return codeElement.GetString() ?? string.Empty;
				}
			}
		}
		catch (JsonException)
		{
		}

		return string.Empty;
	}

}
