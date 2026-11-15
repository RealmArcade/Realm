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

	/// <summary>The permission the lobby's registry node granted its handover, or empty while none was granted.</summary>
	private string _handoverGrantToken = string.Empty;

	/// <summary>The ownership the handover was permitted under, which the transfer is completed with.</summary>
	private int _handoverGrantEpoch;

	/// <summary>The lobby this screen is taking over, kept from the commit so the transfer can be completed later.</summary>
	private string _movedSourceLobbyId = string.Empty;

	/// <summary>
	///     Whether the lobby was listed and therefore has an ownership transfer to complete. A direct or unlisted lobby has
	///     none: its seats are told where to go over the channel they already trust.
	/// </summary>
	private bool _movedLobbyNeedsOwnershipTransfer;

	/// <summary>
	///     Whether the registry node refused the lobby's ownership transfer. A move whose listing did not move is undone,
	///     because a lobby the browser points at a host that just gave it up is worse than one that never moved.
	/// </summary>
	private bool _handoverOwnershipFailed;

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

			case LobbyHandoverMessageKind.Ownership:
				if (!IsHandoverCandidate) return;

				_handoverGrantToken = message.HandoverToken ?? string.Empty;
				_handoverGrantEpoch = message.SourceEpoch;
				_ = Task.Run(async () => await CompleteHandoverOwnershipAsync().ConfigureAwait(false));
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
		NotifyHostLine(HandoverNoticeTexts.Translate(HandoverNoticeKey));
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
			string previousHostToken = _hostToken ?? string.Empty;
			int previousOwnershipEpoch = _ownershipEpoch;
			string lobbyId = string.IsNullOrEmpty(message.LobbyId) ? ActiveLobbyId ?? string.Empty : message.LobbyId;

			_handoverExpectedHumanSeats = PlayerList.Count(player => !player.IsComputer && player.PeerId > 0 && !player.IsDisconnected);
			_movedSeatToken = message.SessionToken;
			_movedSourceLobbyId = lobbyId;
			_movedLobbyNeedsOwnershipTransfer = !string.IsNullOrEmpty(lobbyId) && !string.IsNullOrEmpty(previousHostToken);
			_handoverOwnershipFailed = false;

			// The lobby's listing belongs to the screen that takes it over from here, so nothing this screen does while it
			// moves may close or unregister the lobby the others are still being sent to.
			IsHost = false;

			GameLog.LogToConsole($"[LobbyManager] The lobby moved: this screen is connecting to {message.Address}:{message.Port} as the seat it already holds.");

			if (wasHost) BeginHandoverOwnershipTransfer(previousHostToken, previousOwnershipEpoch);

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
					LobbyJoinError = HandoverNoticeTexts.Translate(LobbyHandoverNotice.RolledBack);
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
	///     Connects this screen to the authority it started for the lobby it is taking over. The registry's ownership of
	///     the lobby is completed with the permission the leaving host obtains, so this screen serves the seats first and the
	///     listing follows in the same step the host that left loses it.
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
		IsDirectLobby = string.IsNullOrEmpty(lobbyId);
		ActiveLobbyId = string.IsNullOrEmpty(lobbyId) ? ActiveLobbyId : lobbyId;
		LobbyPort = _handoverRehearsalPort;
		HostedMapDisplayName = ActiveMapName;

		_lobbySession?.SendLobbyCommand(new LobbyCommand { Kind = LobbyCommandKind.ReportLobbyId, Text = ActiveLobbyId ?? string.Empty });

		await CompleteHandoverOwnershipAsync().ConfigureAwait(false);

		NotifyHostLine(HandoverNoticeTexts.Translate(LobbyHandoverNotice.Confirmed));
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

		bool returned = await JoinAuthorityDirectlyAsync(address, port, seatToken, string.Empty, string.Empty);
		if (returned)
		{
			await Task.Delay(400).ConfigureAwait(false);
			_lobbySession?.SendLobbyCommand(new LobbyCommand { Kind = LobbyCommandKind.AbortHostHandover, Text = "this seat returned to the host it had" });
		}

		return returned;
	}

	/// <summary>
	///     Waits for the seats to appear on the lobby's new authority and for the lobby's listing to have moved with them.
	///     Every seat has to arrive inside the transfer window; a seat that does not, or a listing that never moved, is a
	///     lobby the group has been split from, which is rolled back instead. The screen that was the host gives its own
	///     authority up only once the transfer is whole, because a seat still moving may have to come back to it.
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
		long started = System.Environment.TickCount64;
		long deadline = started + HostHandoverRules.SettleSeconds * 1000L;
		while (!token.IsCancellationRequested && System.Environment.TickCount64 < deadline)
		{
			await Task.Delay(400, token).ConfigureAwait(false);
			if (_handoverOwnershipFailed) return false;

			// A listed lobby whose ownership never moved would leave the browser pointing at the host that gave it up, so
			// the move is only whole when the listing moved with the seats.
			if (_movedLobbyNeedsOwnershipTransfer && _ownershipEpoch <= _handoverGrantEpoch && System.Environment.TickCount64 - started > 6000) return false;

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

		GameLog.LogError($"[LobbyManager] The lobby's handover did not complete within {HostHandoverRules.SettleSeconds} s, so it is rolled back.");
		if (isCandidate) GameContext.LocalAuthority.Pause();
		CancelRehearsal(abandonAuthority: isCandidate);
		_movedSeatToken = previousToken;

		_ = Task.Run(async () =>
		{
			bool returned = await ReturnToPreviousHostAsync(wasHost, previousToken, previousAddress, previousPort, previousAdminToken, lobbyId).ConfigureAwait(false);
			Callable.From(() =>
			{
				HandoverNoticeKey = LobbyHandoverNotice.RolledBack;
				if (!returned) LobbyJoinError = HandoverNoticeTexts.Translate(LobbyHandoverNotice.RolledBack);
				NotifyHostLine(HandoverNoticeTexts.Translate(LobbyHandoverNotice.RolledBack));
				HandoverStateChanged?.Invoke();
			}).CallDeferred();
		});
	}

	/// <summary>
	///     Asks the lobby's registry node to permit the handover, which is the host that is leaving saying it agrees. The
	///     permission is then passed to the candidate over the lobby's own channel, so the screen taking the lobby over never
	///     holds the leaving host's token.
	/// </summary>
	/// <param name="hostToken">The token this screen published the lobby with.</param>
	/// <param name="ownershipEpoch">The ownership the lobby is listed under.</param>
	private void BeginHandoverOwnershipTransfer(string hostToken, int ownershipEpoch)
	{
		string lobbyId = _movedSourceLobbyId;
		if (string.IsNullOrEmpty(lobbyId) || string.IsNullOrEmpty(hostToken))
		{
			// An unlisted or direct lobby has no listing to move. The seats were told where to go over the channel they
			// already trust, which is the whole of what such a lobby needs - and the authority is told that no permission is
			// coming so the seats do not wait for one.
			GameLog.LogToConsole("[LobbyManager] This lobby is not listed, so its ownership moves only to the seats it holds.");
			_lobbySession?.SendLobbyCommand(new LobbyCommand { Kind = LobbyCommandKind.ReportHandoverOwnership, Text = $"{-ownershipEpoch}|" });
			return;
		}

		_ = Task.Run(async () =>
		{
			try
			{
				var payload = new { LobbyId = lobbyId, HostToken = hostToken, OwnershipEpoch = ownershipEpoch };
				using var content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");
				using HttpResponseMessage response = await _httpClient.PostAsync($"{RegistryServerUrl}/lobbies/handover/begin", content).ConfigureAwait(false);
				if (!response.IsSuccessStatusCode)
				{
					GameLog.LogError($"[LobbyManager] The registry node refused to permit the lobby's handover: {response.StatusCode}");
					return;
				}

				string answer = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
				using var document = JsonDocument.Parse(answer);
				JsonElement root = document.RootElement;
				if (!root.TryGetProperty("handoverToken", out JsonElement token)) return;
				int epoch = root.TryGetProperty("ownershipEpoch", out JsonElement epochElement) ? epochElement.GetInt32() : ownershipEpoch;

				_lobbySession?.SendLobbyCommand(new LobbyCommand
				{
					Kind = LobbyCommandKind.ReportHandoverOwnership,
					Text = $"{epoch}|{token.GetString()}"
				});
			}
			catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException or JsonException)
			{
				GameLog.LogError($"[LobbyManager] The lobby's handover could not be permitted by the registry node: {exception.Message}");
			}
		});
	}

	/// <summary>
	///     Completes the lobby's ownership transfer with the permission its previous host obtained. The seats are already
	///     inside this screen's authority by then; what moves here is the listing, the browser entry and the right to speak
	///     for the lobby, which the epoch moves forward and the old host loses in the same step.
	/// </summary>
	private async Task CompleteHandoverOwnershipAsync()
	{
		if (string.IsNullOrEmpty(_handoverGrantToken) || string.IsNullOrEmpty(_movedSourceLobbyId)) return;
		if (_ownershipEpoch > _handoverGrantEpoch) return;

		LobbyReachabilitySet? reachability = _handoverReachability;
		if (reachability == null || reachability.Value.Verified.Count == 0)
		{
			NoteHandoverOwnershipFailure("the lobby's new endpoint was never measured");
			return;
		}

		try
		{
			var payload = new
			{
				LobbyId = _movedSourceLobbyId,
				HandoverToken = _handoverGrantToken,
				OwnershipEpoch = _handoverGrantEpoch,
				Endpoints = reachability.Value.EndpointRegistrations,
				MappingBehaviour = _publishedReachability?.MappingBehaviour ?? MappingBehaviours.Unknown,
				NatType = LocalNatType.ToString(),
				HostPingBaseline = await MeasurePingToRegistryAsync().ConfigureAwait(false),
				SlotsUsed = PlayerList.Count(player => player.Slot > 0),
				MaxPlayers = OpenSlotCount,
				LocalIP = GetLocalIPAddress(),
				LocalPort = _handoverRehearsalPort,
				ObservedPort = 0
			};

			using var content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");
			using HttpResponseMessage response = await _httpClient.PostAsync($"{RegistryServerUrl}/lobbies/handover/complete", content).ConfigureAwait(false);
			if (!response.IsSuccessStatusCode)
			{
				string body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
				GameLog.LogError($"[LobbyManager] The registry node refused to complete the lobby's handover: {response.StatusCode} {body}");
				NoteHandoverOwnershipFailure("the registry node refused the lobby's ownership transfer");
				return;
			}

			string answer = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
			using var document = JsonDocument.Parse(answer);
			JsonElement root = document.RootElement;
			if (root.TryGetProperty("ownershipEpoch", out JsonElement epoch)) _ownershipEpoch = epoch.GetInt32();
			if (root.TryGetProperty("hostToken", out JsonElement token)) _hostToken = token.GetString();
			_handoverGrantToken = string.Empty;

			GameLog.LogToConsole($"[LobbyManager] The lobby's ownership moved to this screen as epoch {_ownershipEpoch}.");
			if (!string.IsNullOrEmpty(ActiveLobbyId))
			{
				StartHostWebSocketSignaling(ActiveLobbyId);
				StartHeartbeatLoop(ActiveLobbyId);
			}
		}
		catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException or JsonException)
		{
			GameLog.LogError($"[LobbyManager] The lobby's handover could not be completed: {exception.Message}");
			NoteHandoverOwnershipFailure("the lobby's ownership transfer could not be stated");
		}
	}

	private void NoteHandoverOwnershipFailure(string reason)
	{
		GameLog.LogError($"[LobbyManager] {reason}; the lobby stays with the host that still holds it.");
		_handoverOwnershipFailed = true;
		NotifyHostLine(HandoverNoticeTexts.Translate(LobbyHandoverNotice.RolledBack));
	}
}
