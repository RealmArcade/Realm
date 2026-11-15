using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Godot;
using Realm.Client.Core;
using Realm.Client.Services;
using Realm.Client.UI;
using Realm.Client.Utils;
using Realm.Shared.Network;
using Realm.Shared.Reachability;

namespace Realm.Client.Network;

/// <summary>
///     The private half of a host handover: the measurements the two parties are shown, the commit that tells this screen
///     where its lobby moved to, and the move itself - a fresh credential, a reconnect without a dialog, and a rollback to
///     the host it had when a seat does not arrive.
/// </summary>
public partial class LobbyManager
{
	/// <summary>The ownership this screen's lobby is listed under, which is what a host message is proved with.</summary>
	private int _ownershipEpoch = 1;

	private bool _movingToNewHost;

	private void OnLobbyHandoverMessage(LobbyHandoverMessage message)
	{
		switch (message.Kind)
		{
			case LobbyHandoverMessageKind.Evidence:
				HandoverReport = message.Report;
				HasHandoverReport = true;
				if (!string.IsNullOrEmpty(message.Notice)) HandoverNoticeKey = message.Notice;
				else if (message.Report.Verdict != (int)LobbyHandoverVerdict.Passed) HandoverNoticeKey = LobbyHandoverNotice.VerdictRefused;
				GameLog.LogToConsole($"[LobbyManager] The handover rehearsal was judged {message.Report.Verdict}; host {message.Report.CurrentHostStabilityRank}, candidate {message.Report.CandidateStabilityRank}.");
				HandoverStateChanged?.Invoke();
				break;

			case LobbyHandoverMessageKind.Suggestion:
				HandoverReport = message.Report;
				HasHandoverReport = true;
				HandoverSuggested?.Invoke();
				HandoverStateChanged?.Invoke();
				break;

			case LobbyHandoverMessageKind.Commit:
				if (_movingToNewHost) return;

				_movingToNewHost = true;
				_ = Task.Run(async () => await MoveToHandoverAuthorityAsync(message).ConfigureAwait(false));
				break;

			case LobbyHandoverMessageKind.RolledBack:
				HandleHandoverRolledBack(message.Notice);
				break;
		}
	}

	private void HandleHandoverRolledBack(string notice)
	{
		CancelRehearsal(abandonAuthority: true);
		HandoverStage = LobbyHandoverStage.None;
		HandoverCandidatePeerId = 0;
		HandoverOffererPeerId = 0;
		HandoverSecondsRemaining = 0;
		HandoverReport = default;
		HasHandoverReport = false;
		HandoverNoticeKey = string.IsNullOrEmpty(notice) ? LobbyHandoverNotice.RolledBack : notice;
		NotifyHostLine(LocalizationManager.TranslateKey(HandoverNoticeKey));
		HandoverStateChanged?.Invoke();
	}

	/// <summary>
	///     Moves this screen to the authority its lobby was handed to. The seat is re-established by the credential it was
	///     told and by its account, never by the peer id it had, and nothing is asked of the player: a lobby that moved is
	///     still the lobby they were in.
	/// </summary>
	/// <param name="message">Where to go and which credential to present.</param>
	private async Task MoveToHandoverAuthorityAsync(LobbyHandoverMessage message)
	{
		try
		{
			if (string.IsNullOrEmpty(message.SessionToken))
			{
				GameLog.LogError("[LobbyManager] A host handover told this screen to move without stating a credential, so it stays where it is.");
				_movingToNewHost = false;
				return;
			}

			bool wasHost = IsHost;
			bool isCandidate = IsHandoverCandidate;
			string previousToken = LobbySessionToken;
			string previousHostAddress = _connectedHostIp ?? "127.0.0.1";
			int previousHostPort = _lobbySession?.Port ?? ENetPort;
			string previousAdminToken = _lobbySession?.AdminToken ?? string.Empty;
			string lobbyId = string.IsNullOrEmpty(message.LobbyId) ? ActiveLobbyId ?? string.Empty : message.LobbyId;

			_handoverExpectedHumanSeats = PlayerList.Count(player => !player.IsComputer && player.PeerId > 0 && !player.IsDisconnected);
			_movedSeatToken = message.SessionToken;

			GameLog.LogToConsole($"[LobbyManager] The lobby moved: this screen is connecting to {message.Address}:{message.Port} as the seat it already holds.");

			bool moved = isCandidate
				? await MoveToOwnAuthorityAsync(lobbyId)
				: await MoveToAnotherHostAsync(lobbyId, message);

			if (!moved)
			{
				GameLog.LogError("[LobbyManager] This screen could not reach the lobby's new host, so it is returning to the host it had.");
				_movedSeatToken = previousToken;
				bool returned = await ReturnToPreviousHostAsync(wasHost, previousToken, previousHostAddress, previousHostPort, previousAdminToken, lobbyId);
				_movingToNewHost = false;
				if (!returned)
				{
					LobbyJoinError = LocalizationManager.TranslateKey(LobbyHandoverNotice.RolledBack);
					LastHostError = LobbyJoinError;
					GameLog.LogError("[LobbyManager] The lobby could not be reached at either host; this screen is no longer in it.");
					Disconnect();
				}

				return;
			}

			StartHandoverSettleWatch(wasHost, isCandidate, previousToken, previousHostAddress, previousHostPort, previousAdminToken, lobbyId);
		}
		catch (Exception exception)
		{
			_movingToNewHost = false;
			GameLog.LogError($"[LobbyManager] The move to the lobby's new host stopped: {exception.GetType().Name}: {exception.Message}");
		}
	}

	/// <summary>
	///     Connects this screen to the authority it started for the lobby it is taking over, after telling the registry node
	///     that the lobby's ownership moved. The registry transfer is the one commit point: after it, only this screen may
	///     publish, admit or speak for the lobby.
	/// </summary>
	/// <param name="lobbyId">The lobby being handed over.</param>
	/// <returns>Whether this screen is the lobby's host on its own authority.</returns>
	private async Task<bool> MoveToOwnAuthorityAsync(string lobbyId)
	{
		if (_handoverRehearsalPort <= 0)
		{
			GameLog.LogError("[LobbyManager] This screen was told to take the lobby over but holds no authority for it.");
			return false;
		}

		bool transferred = await TransferLobbyOwnershipToThisScreenAsync(lobbyId);
		if (!transferred) return false;

		ClientServerSession? previous = _lobbySession;
		_lobbySession = null;
		if (previous != null)
		{
			DetachLobbySession(previous);
			Callable.From(previous.Dispose).CallDeferred();
		}

		_wsCts?.Cancel();
		_hostWebSocket?.Dispose();
		_hostWebSocket = null;

		bool joined = await JoinAuthorityDirectlyAsync("127.0.0.1", _handoverRehearsalPort, _movedSeatToken, _handoverRehearsalAdminToken, _handoverRehearsalMapHash);
		if (!joined) return false;

		IsHost = true;
		IsDirectLobby = false;
		ActiveLobbyId = string.IsNullOrEmpty(lobbyId) ? ActiveLobbyId : lobbyId;
		LobbyPort = _handoverRehearsalPort;
		HostedMapDisplayName = ActiveMapName;

		_lobbySession?.SendLobbyCommand(new LobbyCommand { Kind = LobbyCommandKind.ReportLobbyId, Text = ActiveLobbyId ?? string.Empty });
		if (!string.IsNullOrEmpty(ActiveLobbyId))
		{
			StartHostWebSocketSignaling(ActiveLobbyId);
			StartHeartbeatLoop(ActiveLobbyId);
		}

		NotifyHostLine(LocalizationManager.TranslateKey(LobbyHandoverNotice.Confirmed));
		PlayerListUpdated?.Invoke();
		return true;
	}

	/// <summary>
	///     Connects this screen to the screen that now hosts the lobby. The registry node answers where the lobby is, exactly
	///     as it does for any join, so the punch and endpoint selection a move needs are the ones a join already uses.
	/// </summary>
	/// <param name="lobbyId">The lobby being moved to.</param>
	/// <param name="message">Where the lobby's new authority is.</param>
	/// <returns>Whether this screen is seated in the lobby on its new host.</returns>
	private async Task<bool> MoveToAnotherHostAsync(string lobbyId, LobbyHandoverMessage message)
	{
		ClientServerSession? previous = _lobbySession;
		_lobbySession = null;
		if (previous != null)
		{
			DetachLobbySession(previous);
			Callable.From(previous.Dispose).CallDeferred();
		}

		StopHostReachabilityKeepAlive();
		_wsCts?.Cancel();
		_hostWebSocket?.Dispose();
		_hostWebSocket = null;
		_hostToken = null;

		if (!string.IsNullOrEmpty(lobbyId))
		{
			// The registry node is told the ownership moved by the screen that took it, and that statement is made as the
			// seats are told to go. A seat that asks a moment too early is answered with the host it left, so the question is
			// put again rather than taken as the answer.
			for (int attempt = 0; attempt < 4; attempt++)
			{
				if (await JoinLobbyAsync(lobbyId)) return true;
				await Task.Delay(700).ConfigureAwait(false);
			}
		}

		return await JoinAuthorityDirectlyAsync(message.Address, message.Port, _movedSeatToken, string.Empty, string.Empty);
	}

	private async Task<bool> JoinAuthorityDirectlyAsync(string address, int port, string seatToken, string adminToken, string mapHash)
	{
		var channel = GetNodeOrNull<ClientNetworkChannel>("/root/RealmNetwork");
		if (channel == null)
		{
			GameLog.LogError("[LobbyManager] This build has no channel to an authority.");
			return false;
		}

		var session = new ClientServerSession(channel);
		session.ConfigureLobbyIdentity(PlayerDisplayName, seatToken, AccountId, GameContext.Steam.SteamId);
		if (!string.IsNullOrEmpty(adminToken)) session.ConfigureAdoptedAuthority(adminToken, mapHash);
		AttachLobbySession(session);

		if (!session.TryJoinLobby(address, port, LocalizationManager.GetCurrentLanguageCode(), 0, out string failure, mapHash))
		{
			GameLog.LogError($"[LobbyManager] The lobby's new host at {address}:{port} could not be connected: {failure}");
			DropLobbySession(session);
			return false;
		}

		_isConnectedToHost = true;
		_connectedHostIp = address;

		(string? admissionFailure, bool answered) = await WaitForLobbyAdmissionAsync(session, TimeSpan.FromSeconds(15));
		if (admissionFailure != null)
		{
			GameLog.LogError($"[LobbyManager] The lobby's new host refused this seat: {admissionFailure} (answered: {answered})");
			DropLobbySession(session);
			return false;
		}

		IsHost = session.IsAdmin;
		return true;
	}

	/// <summary>
	///     Sends the lobby back to the host it had. The host it had kept every seat while the seats were moving, so the
	///     credential this screen already held is the credential that restores it, on the same slot and the same team.
	/// </summary>
	private async Task<bool> ReturnToPreviousHostAsync(bool wasHost, string seatToken, string address, int port, string adminToken, string lobbyId)
	{
		if (wasHost && port > 0)
		{
			bool returnedToOwnAuthority = await JoinAuthorityDirectlyAsync("127.0.0.1", port, seatToken, adminToken, string.Empty);
			if (returnedToOwnAuthority)
			{
				ActiveLobbyId = string.IsNullOrEmpty(lobbyId) ? ActiveLobbyId : lobbyId;
				_lobbySession?.SendLobbyCommand(new LobbyCommand { Kind = LobbyCommandKind.AbortHostHandover, Text = "this host could not complete the handover, so it kept the lobby" });
				return true;
			}
		}

		if (string.IsNullOrEmpty(address) || port <= 0) return false;
		return await JoinAuthorityDirectlyAsync(address, port, seatToken, string.Empty, string.Empty);
	}

	/// <summary>
	///     Waits for the seats to appear on the lobby's new authority. Every seat has to arrive inside the transfer window;
	///     a seat that does not is a lobby half the group was dropped from, which is rolled back instead. The screen that was
	///     the host gives its own authority up only once every seat is there, because a seat that is still moving may have
	///     to come back to it.
	/// </summary>
	private void StartHandoverSettleWatch(bool wasHost, bool isCandidate, string previousToken, string previousAddress, int previousPort, string previousAdminToken, string lobbyId)
	{
		_settleWork?.Cancel();
		var work = new CancellationTokenSource();
		_settleWork = work;

		int expectedSeats = _handoverExpectedHumanSeats;
		_ = Task.Run(async () =>
		{
			bool settled = await WaitForHandoverSettlementAsync(expectedSeats, work.Token).ConfigureAwait(false);
			Callable.From(() =>
			{
				if (work.Token.IsCancellationRequested) return;
				CompleteHandoverMove(settled, wasHost, isCandidate, previousToken, previousAddress, previousPort, previousAdminToken, lobbyId);
			}).CallDeferred();
		});
	}

	private async Task<bool> WaitForHandoverSettlementAsync(int expectedSeats, CancellationToken token)
	{
		long deadline = System.Environment.TickCount64 + HostHandoverRules.SettleSeconds * 1000L;
		while (!token.IsCancellationRequested && System.Environment.TickCount64 < deadline)
		{
			await Task.Delay(400, token).ConfigureAwait(false);

			PlayerInfo[] snapshot = PlayerList.ToArray();
			int connected = snapshot.Count(player => !player.IsComputer && player.PeerId > 0 && !player.IsDisconnected);
			if (connected >= expectedSeats && HasLobbyState) return true;
		}

		return false;
	}

	private void CompleteHandoverMove(bool settled, bool wasHost, bool isCandidate, string previousToken, string previousAddress, int previousPort, string previousAdminToken, string lobbyId)
	{
		_movingToNewHost = false;

		if (settled)
		{
			GameLog.LogToConsole($"[LobbyManager] The lobby's handover to {(isCandidate ? "this screen" : "its new host")} completed with every seat present.");
			if (wasHost)
			{
				// The lobby belongs to the new host now, so the authority this screen was holding has nothing left to serve.
				GameContext.LocalAuthority.Pause();
				NotifyHostLine(LocalizationManager.TranslateKey(LobbyHandoverNotice.Confirmed));
			}

			_handoverRehearsalPort = 0;
			_rehearsalAuthorityWasStarted = false;
			CancelRehearsal(abandonAuthority: false);
			HandoverStage = LobbyHandoverStage.None;
			HandoverCandidatePeerId = 0;
			HandoverOffererPeerId = 0;
			SuggestedCandidatePeerId = 0;
			HandoverNoticeKey = LobbyHandoverNotice.Confirmed;
			HandoverReport = default;
			HasHandoverReport = false;
			PlayerListUpdated?.Invoke();
			HandoverStateChanged?.Invoke();
			return;
		}

		GameLog.LogError($"[LobbyManager] Not every seat arrived at the lobby's new host within {HostHandoverRules.SettleSeconds} s, so the handover is rolled back.");
		if (isCandidate) GameContext.LocalAuthority.Pause();
		CancelRehearsal(abandonAuthority: isCandidate);
		_movedSeatToken = previousToken;

		_lobbySession?.SendLobbyCommand(new LobbyCommand { Kind = LobbyCommandKind.AbortHostHandover, Text = "not every seat arrived at the new host" });

		_ = Task.Run(async () =>
		{
			bool returned = await ReturnToPreviousHostAsync(wasHost, previousToken, previousAddress, previousPort, previousAdminToken, lobbyId).ConfigureAwait(false);
			Callable.From(() =>
			{
				LobbyJoinError = returned ? LocalizationManager.TranslateKey(LobbyHandoverNotice.RolledBack) : LastHostError;
				NotifyHostLine(LocalizationManager.TranslateKey(LobbyHandoverNotice.RolledBack));
				HandoverNoticeKey = LobbyHandoverNotice.RolledBack;
				HandoverStateChanged?.Invoke();
			}).CallDeferred();
		});
	}

	/// <summary>
	///     Moves the lobby's ownership on the registry node that lists it. The epoch this screen states is the one the lobby
	///     is on, and the epoch it is answered with is the one this screen publishes and admits under from then on.
	/// </summary>
	/// <param name="lobbyId">The lobby being handed over.</param>
	/// <returns>Whether the lobby now belongs to this screen on the registry node.</returns>
	private async Task<bool> TransferLobbyOwnershipToThisScreenAsync(string lobbyId)
	{
		if (string.IsNullOrEmpty(lobbyId) || string.IsNullOrEmpty(_hostToken))
		{
			// An unlisted or direct lobby has no ownership to move. The seats are told where to go over the channel they
			// already trust, which is the whole of what such a lobby needs.
			GameLog.LogToConsole("[LobbyManager] This lobby is not listed, so its ownership moves only to the seats it holds.");
			return true;
		}

		LobbyReachabilitySet? reachability = _handoverReachability;
		if (reachability == null || reachability.Value.Verified.Count == 0)
		{
			GameLog.LogError("[LobbyManager] The lobby's new endpoint was never measured, so its ownership cannot be moved.");
			return false;
		}

		try
		{
			var payload = new
			{
				LobbyId = lobbyId,
				HostToken = _hostToken,
				OwnershipEpoch = _ownershipEpoch,
				Endpoints = reachability.Value.EndpointRegistrations,
				MappingBehaviour = _publishedReachability?.MappingBehaviour ?? MappingBehaviours.Unknown,
				NatType = LocalNatType.ToString(),
				HostPingBaseline = await MeasurePingToRegistryAsync(),
				SlotsUsed = PlayerList.Count(player => player.Slot > 0),
				MaxPlayers = OpenSlotCount,
				LocalIP = GetLocalIPAddress(),
				LocalPort = _handoverRehearsalPort,
				ObservedPort = 0
			};

			using var content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");
			using HttpResponseMessage response = await _httpClient.PostAsync($"{RegistryServerUrl}/lobbies/handover", content);
			if (!response.IsSuccessStatusCode)
			{
				string body = await response.Content.ReadAsStringAsync();
				GameLog.LogError($"[LobbyManager] The registry node refused the lobby's handover: {response.StatusCode} {body}");
				return false;
			}

			string answer = await response.Content.ReadAsStringAsync();
			using var document = JsonDocument.Parse(answer);
			JsonElement root = document.RootElement;
			if (root.TryGetProperty("ownershipEpoch", out JsonElement epoch)) _ownershipEpoch = epoch.GetInt32();
			if (root.TryGetProperty("hostToken", out JsonElement token)) _hostToken = token.GetString();
			GameLog.LogToConsole($"[LobbyManager] The lobby's ownership moved to this screen as epoch {_ownershipEpoch}.");
			return true;
		}
		catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException or JsonException)
		{
			GameLog.LogError($"[LobbyManager] The lobby's handover could not be stated to the registry node: {exception.Message}");
			return false;
		}
	}
}
