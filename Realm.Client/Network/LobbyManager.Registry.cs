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
	///     host, which is the only way a host behind a filtering router can be told where to send. The families this screen
	///     holds are stated, because a punch is worth relaying only toward an endpoint that needs one.
	/// </summary>
	/// <param name="lobbyId">The lobby being joined.</param>
	/// <param name="clientPublicIp">The address this network is reached at from outside, as this screen measured it.</param>
	/// <param name="clientFamilies">The address families this machine can reach other networks over.</param>
	/// <returns>The registry node's answer, or null when no node answered.</returns>
	private async Task<HttpResponseMessage?> SendJoinRequestAsync(string lobbyId, string clientPublicIp, IReadOnlyList<string> clientFamilies)
	{
		var joinPayload = new
		{
			LobbyId = lobbyId,
			ClientPublicIP = clientPublicIp,
			ClientPublicPort = 0,
			ClientEndpointFamilies = clientFamilies
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
	/// <summary>
	///     Reads where to connect and how to report this screen to the registry's host relay, from the endpoints the registry
	///     node states for the lobby. Every field is read without assuming it is present: a lobby registered by an older node
	///     answers with fewer of them, and a missing field has to produce a stated reason rather than an exception message. The
	///     endpoint is the one this screen shares a family with, and a lobby and a screen that share none is a refusal said
	///     here rather than a dial that waits out its budget.
	/// </summary>
	/// <param name="response">The registry node's answer.</param>
	/// <param name="clientPublicIp">This screen's own public IPv4 endpoint, as measured before the join.</param>
	/// <param name="hasIpv4">Whether this screen holds an IPv4 address at all.</param>
	/// <param name="hasIpv6">Whether this screen holds a global IPv6 address.</param>
	/// <returns>The endpoint to connect to and the ticket this screen reports itself with, and why there is none.</returns>
	private async Task<(LobbyJoinTarget Target, string Failure)> ResolveHostConnectionInfoAsync(HttpResponseMessage response, string clientPublicIp, bool hasIpv4, bool hasIpv6)
	{
		string respText = await response.Content.ReadAsStringAsync();
		LobbyJoinAnswer? answer = LobbyJoinAnswers.TryParse(respText);
		if (answer == null) return (default, Tr(JoinPhaseSentences.RestrictiveNetwork));

		LobbyJoinAnswer joinAnswer = answer.Value;
		IReadOnlyList<LobbyEndpointInfo> endpoints = joinAnswer.EndpointsOrStatedAddress();

		LobbyEndpointChoice choice = LobbyEndpointSelection.Choose(endpoints, hasIpv4, hasIpv6);
		if (!choice.Chosen || choice.Endpoint == null) return (default, DescribeJoinFamilyFailure(choice));

		string connectIp = choice.Endpoint.DialAddress;
		int connectPort = choice.Endpoint.DialPort;
		var punch = new JoinerPunchTicket(RegistryServerHost, joinAnswer.PunchPort, joinAnswer.PunchToken);

		// The same-network shortcut only makes sense for IPv4: it exists because a router hands both players the same public
		// address and the private one is the shorter path. An IPv6 endpoint is already the host's own address.
		string localIp = joinAnswer.LocalAddress ?? string.Empty;
		if (choice.Family == LobbyAddressFamily.IPv4 && localIp.Length > 0 && LocalNetwork.Contains(localIp) && !string.Equals(localIp, connectIp, StringComparison.Ordinal))
		{
			GameLog.LogToConsole($"[LobbyManager] The host offers {localIp}:{connectPort} on a network this machine is attached to, so the join goes there first and falls back to {connectIp}:{connectPort}.");
			return (new LobbyJoinTarget(localIp, connectPort, true, punch, connectIp, connectPort), string.Empty);
		}

		// A lobby behind a router is admitted only when the node relays this screen's endpoint to the host, and a node older
		// than the relay says nothing at all about itself. Both are decided here, before an attempt is spent on them: the
		// alternative is a join that reports a host which never answered, when the host was never going to be told.
		// A lobby the node measured as Open answers unsolicited traffic and needs nothing relayed, whatever the family says:
		// only a lobby that needs a punch, or one the node never classified, depends on the relay being there.
		bool lobbyNeedsPunch = LobbyAddresses.NeedsHolePunch(choice.Family) && !string.Equals(joinAnswer.ReachabilityClass, ReachabilityClasses.Open, StringComparison.OrdinalIgnoreCase);
		LobbyJoinBlock block = LobbyJoinPreconditions.DescribeBlock(joinAnswer, lobbyNeedsPunch);
		if (block != LobbyJoinBlock.None)
		{
			string stated = LobbyJoinPreconditions.Describe(joinAnswer.Capabilities);
			return (default, block switch
			{
				LobbyJoinBlock.NodeDoesNotRelayPunches => string.Format(Tr("This lobby server does not relay this screen to the lobby's host, which a lobby behind a router needs. It states: {0}."), stated),
				LobbyJoinBlock.NodeStatedNoCapabilities => Tr("This lobby server is older than the join relay this lobby needs, and states nothing about what it can do."),
				_ => string.Format(Tr("This lobby server relays joins but named no check to report this screen against. It states: {0}."), stated)
			});
		}

		// Stated only for an IPv6 connection, which is the one path that is new: an IPv4 join logs exactly what it always has.
		if (choice.Family == LobbyAddressFamily.IPv6)
		{
			GameLog.LogToConsole($"[LobbyManager] Joining {LobbyAddresses.Describe(connectIp, connectPort)} over IPv6, which needs no router forward and no hole punch.");
		}

		return (new LobbyJoinTarget(connectIp, connectPort, false, punch, connectIp, connectPort), string.Empty);
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
		_publishedEndpoints = new List<LobbyEndpointInfo>();

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
		IsUnlisted = false;
		IsHost = false;
		IsGameStarted = false;
		_countdownActive = false;
		_countdownRemaining = 0;
		_countdownMapName = null;
		_matchStarted = false;
		JoinedLobbyFamily = LobbyAddressFamily.Unknown;
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
	///     it did. The player's join is already under way rather than waiting on this, because its own mapping has to exist
	///     before the punch arrives; the statement is what a join that never answered is read against, so it is only made by a
	///     screen that is still administering a running lobby, and a screen that is not says in its log why none was sent.
	/// </summary>
	/// <param name="clientIp">The address the joining player was seen at.</param>
	/// <param name="clientPort">The port the joining player was seen at.</param>
	/// <param name="token">The join the punch was asked for, or empty for a request that names none.</param>
	private void SendPunchRequest(string clientIp, int clientPort, string token)
	{
		if (string.IsNullOrEmpty(clientIp) || clientPort <= 0) return;

		ClientServerSession? session = _lobbySession;
		if (!IsHost || session == null || !session.IsRunning)
		{
			GameLog.LogToConsole($"[LobbyManager] A punch toward {clientIp}:{clientPort} was asked for, but this screen is not administering a running lobby, so none was sent.");
			return;
		}

		if (!session.SendLobbyCommand(new LobbyCommand
		{
			Kind = LobbyCommandKind.PunchAddress,
			Address = clientIp,
			Port = clientPort
		}))
		{
			GameLog.LogError($"[LobbyManager] The punch toward {clientIp}:{clientPort} could not be handed to the lobby's authority, so none was sent.");
			return;
		}

		if (token.Length == 0) return;
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
			JoinPhases.Registry => Tr(JoinPhaseSentences.HostRegistry),
			JoinPhases.Resolve => Tr(JoinPhaseSentences.HostResolve),
			JoinPhases.Report => Tr(JoinPhaseSentences.HostReport),
			JoinPhases.Punch => Tr(JoinPhaseSentences.HostPunch),
			JoinPhases.Answer => Tr(JoinPhaseSentences.HostAnswer),
			JoinPhases.Admission => Tr(JoinPhaseSentences.HostAdmission),
			JoinPhases.Session => Tr(JoinPhaseSentences.HostSession),
			_ => Tr(JoinPhaseSentences.HostUnnamed)
		};

		string advice = reachabilityClass == ReachabilityClasses.Unverified
			? Tr("This lobby is no longer listed as reachable; enable UPnP on the router, forward this UDP port, or share the direct address meanwhile.")
			: MappingBehaviour == MappingBehaviours.PerDestination
				? Tr("This router gives every destination a different outside port, so punching cannot open it; forward this UDP port, or use a VPN or an overlay network.")
				: Tr("Other players may need UPnP enabled on this router, or this UDP port forwarded to this machine.");

		int players = Math.Max(1, distinctFailures);
		string sentence = string.Format(Tr("{0} player(s) could not reach this lobby in the last minute: {1}; they tried {2}. {3}"), players, step, endpoint, advice);
		LastPlayerJoinFailure = sentence;
		_lobbySession?.SendLobbyCommand(new LobbyCommand
		{
			Kind = LobbyCommandKind.ReportJoinFailure,
			Text = sentence
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
				await SendHeartbeatAsync(lobbyId, token);
				await Task.Delay(1000, token);
			}
		}, token);
	}

	private async Task SendHeartbeatAsync(string lobbyId, CancellationToken token)
	{
		string? hostToken = _hostToken;

		// A beat states the lobby's listing and its reachability class, so the node takes one only from the screen holding
		// the token it issued for this lobby. Closing a lobby clears the token before the beat loop stops, and a beat that
		// has no token is not worth sending.
		if (string.IsNullOrEmpty(hostToken)) return;

		try
		{
			// The class travels with the heartbeat so a lobby that withdraws its own claim stops being listed as
			// reachable without waiting for a player to fail: the node only ever takes this downward, so a screen
			// cannot promote its own lobby back by stating a better class. The unlisted statement travels the same way,
			// so a host that makes its lobby private or offers it in the browser again does not wait for the next beat.
			// The token is what says this beat comes from the screen that registered the lobby: it carries the unlisted
			// statement and the reachability class, and the node refuses a beat without it.
			var heartbeat = new { LobbyId = lobbyId, HostToken = hostToken, SlotsUsed = PlayerList.Count, IsGameInProgress = IsGameStarted, ReachabilityClass = ReachabilityClass, Listed = !IsUnlisted };
			var jsonContent = new StringContent(JsonSerializer.Serialize(heartbeat), Encoding.UTF8, "application/json");
			using HttpResponseMessage response = await _httpClient.PostAsync($"{RegistryServerUrl}/lobbies/heartbeat", jsonContent, token);
			if (response.StatusCode == HttpStatusCode.Unauthorized)
			{
				GameLog.LogError($"the lobby heartbeat to {RegistryServerUrl} was refused, so this screen is no longer the host of {lobbyId} there and the registry will drop it");
			}
		}
		catch (Exception ex)
		{
			GameLog.LogError($"the lobby heartbeat to {RegistryServerUrl} failed, so the registry may drop this lobby: {ex.GetType().Name}: {ex.Message}");
		}
	}

	private async Task<bool> RegisterLobbyWithRegistryAsync(string mapPathName, string mapDisplayName, string? explicitVersion, int lobbyPort)
	{
		int attempts = Math.Max(1, RegistryServers.Count);

		for (int attempt = 0; attempt < attempts; attempt++)
		{
			// The lobby is published only at the endpoints a public node reached itself, and every registry node publishes
			// only what it checked itself, so the rehearsal is run against the node that will accept the lobby - once per
			// endpoint, in the family that endpoint is reached in.
			ReportHostLobbyStage(HostLobbyStage.VerifyingNetwork);
			LobbyReachabilitySet reachability = await RehearsePublishedEndpointsAsync(RegistryServerUrl, lobbyPort);

			// The class and the mapping behaviour belong to the IPv4 endpoint, which is the one a router stands in front of and
			// the only one a punch can fail on; an IPv6 endpoint is reached as it was stated and adds no caveat of its own.
			ReachabilityClass = _publishedReachability?.ReachabilityClass ?? string.Empty;
			MappingBehaviour = _publishedReachability?.MappingBehaviour ?? MappingBehaviours.Unknown;
			HostReachabilityFailure decision = LobbyJoinability.Decide(
				publicLobby: true,
				LoopbackJoinOutcome.Answered,
				reachability.Verified.Count > 0 ? HostReachabilityFailure.None : DescribeReachabilityRefusal(RegistryServerUrl, reachability).Failure,
				reachability.AllowsLocalCallers || AllowNonPublicEndpointForDevelopment || reachability.Verified.Exists(endpoint => ReachabilityAddresses.IsPubliclyRoutable(endpoint.DialAddress)),
				MappingBehaviour,
				// A router that maps a port per destination stands in front of the IPv4 endpoints only. A lobby that also
				// offers a family no router translates keeps that family, and the registration drops the rest.
				reachability.Verified.Exists(endpoint => !LobbyAddresses.NeedsHolePunch(endpoint.FamilyValue)));
			if (decision != HostReachabilityFailure.None)
			{
				// A refusal that is about the endpoint rather than about a check that never answered is described from the
				// endpoint, because "no endpoint of this lobby was measured" would name the wrong problem.
				HostReachabilityResult refusal = DescribeReachabilityRefusal(RegistryServerUrl, reachability, decision);
				LastHostFailure = refusal;
				LastHostError = DescribeReachabilityFailure(refusal, lobbyPort);
				GameLog.LogError($"[LobbyManager] {LastHostError}");
				return false;
			}

			LobbyEndpointInfo primary = reachability.Verified.Find(endpoint => endpoint.FamilyValue == LobbyAddressFamily.IPv4) ?? reachability.Verified[0];
			LobbyEndpointRegistration primaryRegistration = reachability.EndpointRegistrations.Find(registration => LobbyAddresses.ParseFamilyName(registration.Family) == primary.FamilyValue) ?? reachability.EndpointRegistrations[0];
			if (_routerReachability != RouterReachability.Confirmed) _routerReachability = RouterReachability.ConfirmedFromOutside;

			ReportHostLobbyStage(HostLobbyStage.RegisteringLobby);

			try
			{
				int hostPingBaseline = await MeasurePingToRegistryAsync();
				var mapInfo = GetMapMetadataInfo(mapPathName, mapDisplayName, explicitVersion);

				var registerPayload = new
				{
					Map = mapDisplayName,
					// The port a joining player must use is the outside port the router exposed to the check, which is the
					// port every player's punch reaches. On a router that does not translate the port it is the port this
					// lobby's transport holds, and a router that translates it was measured to keep one mapping for every
					// destination before anything was published at it.
					HostPort = primary.Port,
					NatType = LocalNatType.ToString(),
					ReportedHostIP = primary.DialAddress,
					Listed = !IsUnlisted,
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
					ReachabilityProbeId = primaryRegistration.ProbeId,
					ObservedPort = _publishedReachability?.ObservedPort ?? 0,
					Endpoints = reachability.EndpointRegistrations
				};

				var jsonContent = new StringContent(JsonSerializer.Serialize(registerPayload), Encoding.UTF8, "application/json");
				using HttpResponseMessage response = await _httpClient.PostAsync($"{RegistryServerUrl}/lobbies/register", jsonContent);

				if (response.IsSuccessStatusCode)
				{
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
			HostReachabilityFailure.MappingPerDestination => $"This router gives every destination a different outside port, so the port other players would punch is a different one for each of them and none of them can be told where to connect. Forward UDP port {lobbyPort} to this machine, or play over a VPN or an overlay network such as Tailscale; the lobby was not published.",
			HostReachabilityFailure.MappingUnmeasured => $"Your router gives this game an outside port of its own ({lobbyPort} is what the lobby listens on), and the lobby server could not reach the second destination it measures that with, so whether that one outside port is the same for every player was not measured. Host again so the check can measure it, forward UDP port {lobbyPort}, or play over a VPN or an overlay network; the lobby was not published.",
			HostReachabilityFailure.ProbeFamilyUnavailable => "The lobby server cannot check this kind of address, so this lobby was not published there. Another lobby server, or an IPv4 address, would fix it.",
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
