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
	private async Task<HttpResponseMessage?> SendJoinRequestAsync(string lobbyId, string clientPublicIp, int clientPublicPort)
	{
		var joinPayload = new
		{
			LobbyId = lobbyId,
			ClientPublicIP = clientPublicIp,
			ClientPublicPort = clientPublicPort
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

	private async Task<(string Ip, int Port)> ResolveHostConnectionInfoAsync(HttpResponseMessage response, string clientPublicIp)
	{
		var respText = await response.Content.ReadAsStringAsync();
		using var doc = JsonDocument.Parse(respText);

		string hostIp = doc.RootElement.GetProperty("hostIP").GetString() ?? "";
		int hostPort = doc.RootElement.GetProperty("hostPort").GetInt32();

		string? localIp = null;
		if (doc.RootElement.TryGetProperty("localIP", out var localIpProp))
		{
			localIp = localIpProp.GetString();
		}

		string connectIp = hostIp;
		int connectPort = hostPort;
		if (!string.IsNullOrEmpty(localIp) && hostIp == clientPublicIp)
		{
			GameLog.LogToConsole($"[LobbyManager] Host is on the same LAN (Public IP: {hostIp}). Connecting to local IP: {localIp}:{ENetPort}");
			connectIp = localIp;
			connectPort = ENetPort;
		}

		return (connectIp, connectPort);
	}

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
				Callable.From(() => SendPunchRequest(clientIp, clientPort)).CallDeferred();
			}
		}
		catch (Exception ex)
		{
			GameLog.LogError($"[LobbyManager] Error handling WebSocket message: {ex.Message}");
		}
	}

	private void SendPunchRequest(string clientIp, int clientPort)
	{
		if (string.IsNullOrEmpty(clientIp) || clientPort <= 0) return;

		_lobbySession?.SendLobbyCommand(new LobbyCommand
		{
			Kind = LobbyCommandKind.PunchAddress,
			Address = clientIp,
			Port = clientPort
		});
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
			HostReachabilityFailure decision = LobbyJoinability.Decide(publicLobby: true, LoopbackJoinOutcome.Answered, reachability.Failure);
			if (decision != HostReachabilityFailure.None)
			{
				LastHostFailure = reachability;
				LastHostError = DescribeReachabilityFailure(reachability, lobbyPort);
				GameLog.LogError($"[LobbyManager] {LastHostError}");
				return false;
			}

			_hostPublicIp = reachability.PublicIp;
			_hostPublicPort = reachability.PublicPort;

			ReportHostLobbyStage(HostLobbyStage.RegisteringLobby);

			try
			{
				int hostPingBaseline = await MeasurePingToRegistryAsync();
				var mapInfo = GetMapMetadataInfo(mapPathName, mapDisplayName, explicitVersion);

				var registerPayload = new
				{
					Map = mapDisplayName,
					HostPort = reachability.PublicPort,
					NatType = LocalNatType.ToString(),
					ReportedHostIP = reachability.PublicIp,
					PasswordHash = "",
					MaxPlayers = MaxPlayers,
					SlotsUsed = PlayerList.Count,
					HostPingBaseline = hostPingBaseline,
					GameVersion = RealmVersion.GameBinaryVersion,
					LocalIP = GetLocalIPAddress(),
					MapVersion = mapInfo.Version,
					Signature = mapInfo.Signature,
					PublicKey = mapInfo.PublicKey,
					MapHash = mapInfo.MapHash,
					MapSizeBytes = mapInfo.SizeBytes,
					ReachabilityProbeId = reachability.ProbeId
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
