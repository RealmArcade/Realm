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
	/// <summary>How long the current join has been running, which every phase line states beside it.</summary>
	private long _joinStartedMilliseconds;

	/// <summary>The step the current join has reached, which is the step a failure is reported against.</summary>
	private string _joinPhase = JoinPhases.Registry;

	/// <summary>The endpoint the current join is aimed at, as the phase lines and the failure report state it.</summary>
	private string _joinEndpoint = string.Empty;

	/// <summary>How many connection attempts the current join has made, which a failure report states.</summary>
	private int _joinAttempts;

	public async Task<bool> JoinLobbyAsync(string lobbyId)
	{
		InitializeClientState(lobbyId);
		await EnsureNatTypeMeasuredAsync();

		string clientPublicIp = _hostPublicIp ?? "127.0.0.1";

		_joinStartedMilliseconds = Environment.TickCount64;
		_joinPhase = JoinPhases.Registry;
		_joinEndpoint = string.Empty;
		_joinAttempts = 0;

		try
		{
			long registryStarted = Environment.TickCount64;
			var response = await SendJoinRequestAsync(lobbyId, clientPublicIp);
			if (response == null || !response.IsSuccessStatusCode)
			{
				var errorText = response != null ? await response.Content.ReadAsStringAsync() : "All registry nodes offline";
				MarkJoinPhase(JoinPhases.Registry, "refused", registryStarted);
				GameLog.LogError($"[LobbyManager] Failed to join lobby: {errorText}");
				await ReportJoinFailureAsync(lobbyId);
				LastHostError = JoinFailureSentence(JoinPhases.Registry);
				LobbyJoinError = LastHostError;
				ConnectionFailed?.Invoke("Failed to coordinate join.");
				return false;
			}

			MarkJoinPhase(JoinPhases.Registry, "answered", registryStarted);

			long resolveStarted = Environment.TickCount64;
			_joinPhase = JoinPhases.Resolve;
			LobbyJoinTarget target = await ResolveHostConnectionInfoAsync(response);
			if (string.IsNullOrEmpty(target.Ip) || target.Port <= 0)
			{
				MarkJoinPhase(JoinPhases.Resolve, "unusable", resolveStarted);
				LastHostError = JoinFailureSentence(JoinPhases.Resolve);
				LobbyJoinError = LastHostError;
				await ReportJoinFailureAsync(lobbyId);
				ConnectionFailed?.Invoke(LastHostError);
				return false;
			}

			_joinEndpoint = $"{target.Ip}:{target.Port}";
			MarkJoinPhase(JoinPhases.Resolve, "resolved", resolveStarted);

			_connectedHostIp = target.Ip;
			return await EstablishLobbyConnectionAsync(target);
		}
		catch (Exception ex)
		{
			GameLog.LogError($"[LobbyManager] Join error: {ex.Message}");
			ConnectionFailed?.Invoke(ex.Message);
			return false;
		}
	}

	/// <summary>
	///     Writes one line for a step of the join, carrying the step, what it did, how long it took and the endpoint it was
	///     aimed at. A captured log then says which step broke without anybody reading two consoles side by side.
	/// </summary>
	/// <param name="phase">The step that finished.</param>
	/// <param name="outcome">What the step did.</param>
	/// <param name="startedMilliseconds">When the step started, on the same clock the join started on.</param>
	private void MarkJoinPhase(string phase, string outcome, long startedMilliseconds)
	{
		GameLog.LogToConsole($"[Join] phase={phase} outcome={outcome} elapsed={Environment.TickCount64 - startedMilliseconds}ms total={Environment.TickCount64 - _joinStartedMilliseconds}ms endpoint={(_joinEndpoint.Length > 0 ? _joinEndpoint : "none")}");
	}

	/// <summary>
	///     The sentence a failure at one step of the join is shown with. Each step has its own, because the thing a player has
	///     to change is different for each: a lobby server that is down, a lobby server that will not say where the lobby is, a
	///     host that never sent toward this screen, and a host whose port never answered are four different problems.
	/// </summary>
	/// <param name="phase">The step that failed.</param>
	/// <returns>The sentence to show.</returns>
	private static string JoinFailureSentence(string phase)
	{
		return phase switch
		{
			JoinPhases.Registry => Tr("The lobby server could not be reached, so this lobby could not be found."),
			JoinPhases.Resolve => Tr("The lobby server answered without saying where this lobby is."),
			JoinPhases.Report => Tr("This screen could not tell the lobby where it is, so the host could not send toward it."),
			JoinPhases.Punch => Tr("The lobby's host never sent toward this screen, so the way back was never opened. The host may need UPnP enabled or this UDP port forwarded."),
			JoinPhases.Answer => Tr("The lobby's host sent toward this screen but its game port never answered. The host may need this UDP port forwarded, or this network may need a VPN or an overlay network."),
			JoinPhases.Admission => Tr("The lobby's authority refused this screen."),
			JoinPhases.Session => Tr("The lobby admitted this screen but never described itself to it."),
			_ => Tr("The lobby's authority did not answer in time.")
		};
	}

	public async Task<bool> JoinByIpAsync(string targetIp, int targetPort = DefaultGamePort)
	{
		string directLobbyId = $"direct-{Guid.NewGuid():N}";
		InitializeClientState(directLobbyId);
		_connectedHostIp = targetIp;
		bool joined = await EstablishLobbyConnectionAsync(new LobbyJoinTarget(targetIp, targetPort, IsPrivateIp(targetIp), default));

		// Only an address that seated this player is worth going back to: a typo, or a lobby that never answered, is not
		// somewhere to send them a second time.
		if (joined) DirectConnectHistory.Remember(targetIp, targetPort);
		return joined;
	}

	private void InitializeClientState(string lobbyId)
	{
		ReleaseSeedingSlot();

		IsHost = false;
		IsGameStarted = false;
		ActiveLobbyId = lobbyId;
		LastLobbyWarning = null;
		LastHostError = null;
		_downloadingMapName = null;
		PlayerList.Clear();
	}

	/// <summary>
	///     How long one join attempt waits for the lobby's authority to admit this screen. Hole punching on a slow link
	///     legitimately takes seconds, so the budget is spent on one attempt instead of being divided between several: a
	///     retry is for silence, and a refusal is an answer that a second attempt would only repeat.
	/// </summary>
	private const int JoinAttemptTimeoutMilliseconds = 15000;

	private const int JoinRetryDelayMilliseconds = 300;

	/// <summary>
	///     How many times a join that produced no answer at all is tried before the screen gives up. A lobby that answered is
	///     never asked again, because its answer is already a decision.
	/// </summary>
	private const int JoinAttemptCount = 2;

	/// <summary>
	///     How long a join to an address on this machine's own network is given before the join falls back to the lobby's
	///     outside address. Two networks that both number themselves 192.168.1.0/24 look like one network from inside, and
	///     nothing a screen can measure for itself tells them apart, so the address is tried and disproved rather than
	///     trusted: a neighbouring host answers in milliseconds, and one that is not a neighbour spends only this long.
	/// </summary>
	private const int LocalJoinAttemptTimeoutMilliseconds = 2500;

	private bool _establishingLobbyConnection;

	/// <summary>
	///     Where a join is aimed, and the ticket this screen reports itself with so the host can reach it. A host that
	///     advertised an address on a network this machine is attached to offers two ways in, and the join tries the near
	///     one first and falls back to the far one: an address that merely looks local is not a promise that it is.
	/// </summary>
	/// <param name="Ip">The address to connect to.</param>
	/// <param name="Port">The port to connect to.</param>
	/// <param name="IsLocalConnection">Whether the address is on a network this machine is directly attached to.</param>
	/// <param name="Punch">The ticket the registry node stated for this join, or a default one when it stated none.</param>
	/// <param name="PublicIp">The address the lobby is reachable at from outside, or empty when there is no other way in.</param>
	/// <param name="PublicPort">The port the lobby is reachable at from outside, or zero when there is no other way in.</param>
	private readonly record struct LobbyJoinTarget(string Ip, int Port, bool IsLocalConnection, JoinerPunchTicket Punch, string PublicIp = "", int PublicPort = 0);

	private async Task<bool> EstablishLobbyConnectionAsync(LobbyJoinTarget target)
	{
		var channel = GetNodeOrNull<ClientNetworkChannel>("/root/RealmNetwork");
		if (channel == null)
		{
			LastHostError = "This build has no channel to an authority.";
			ConnectionFailed?.Invoke(LastHostError);
			return false;
		}

		GameContext.ServerSession?.Dispose();
		GameContext.DetachServerSession();

		string? failure = null;
		bool rejected = false;
		bool punchCoordinated = false;

		// A host that named an address on a network this machine is attached to is tried there first, because a join that
		// stays inside one network is the fastest and the only one that needs no punching. The address is only tried, not
		// trusted: two networks that number themselves the same look like one from inside, so an address that does not
		// answer in the time a neighbour needs is given up and the lobby's outside address is joined instead.
		bool nearAddressOffered = target.IsLocalConnection && !string.IsNullOrEmpty(target.PublicIp) && target.PublicPort > 0 && (target.PublicIp != target.Ip || target.PublicPort != target.Port);

		_establishingLobbyConnection = true;
		try
		{
			for (int route = 0; route < (nearAddressOffered ? 2 : 1) && !rejected; route++)
			{
				bool nearRoute = route == 0 && nearAddressOffered;
				string connectIp = nearRoute ? target.Ip : target.PublicIp.Length > 0 ? target.PublicIp : target.Ip;
				int connectPort = nearRoute ? target.Port : target.PublicPort > 0 ? target.PublicPort : target.Port;
				int attemptTimeoutMilliseconds = nearRoute ? LocalJoinAttemptTimeoutMilliseconds : JoinAttemptTimeoutMilliseconds;

				// The report of this screen's endpoint leaves from the port the join will connect from, so that the endpoint
				// the host punches and the endpoint that connects are the same one. The connection is made at once rather
				// than after the punch: this side's mapping for the lobby has to exist before the host's punch arrives,
				// because a router that keeps no mapping of its own for an unanswered packet would take this side's mapping
				// away from it.
				int joinLocalPort = 0;
				punchCoordinated = false;
				if (!nearRoute && target.Punch.IsUsable)
				{
					long reportStarted = Environment.TickCount64;
					_joinPhase = JoinPhases.Report;
					joinLocalPort = await ReportJoinerEndpointFromJoinPortAsync(target.Punch, connectIp, connectPort);
					punchCoordinated = joinLocalPort > 0;
					MarkJoinPhase(JoinPhases.Report, punchCoordinated ? "sent" : "refused", reportStarted);
				}

				for (int attempt = 0; attempt < JoinAttemptCount && !rejected; attempt++)
				{
					if (attempt > 0)
					{
						await Task.Delay(JoinRetryDelayMilliseconds);
						GameLog.LogToConsole($"[LobbyManager] Joining {connectIp}:{connectPort} again after '{failure}' (attempt {attempt + 1} of {JoinAttemptCount}).");
					}

					var session = new ClientServerSession(channel);
					session.ConfigureLobbyIdentity(PlayerDisplayName, LobbySessionToken, AccountId, GameContext.Steam.SteamId);
					AttachLobbySession(session);

					_joinAttempts++;
					long answerStarted = Environment.TickCount64;
					_joinPhase = JoinPhases.Answer;
					if (!session.TryJoinLobby(connectIp, connectPort, LocalizationManager.GetCurrentLanguageCode(), joinLocalPort, out string joinFailure))
					{
						failure = joinFailure;
						MarkJoinPhase(JoinPhases.Answer, "unusable", answerStarted);
						DropLobbySession(session);
						continue;
					}

					_isConnectedToHost = true;

					// A lobby that punched this screen back does not need the punch that opens this side of the route, and
					// sending it would take the external port the host's punch is using for this screen.
					if (!nearRoute && !punchCoordinated) PunchFromGamePort(channel, connectIp, connectPort);

					var admission = await WaitForLobbyAdmissionAsync(session, TimeSpan.FromMilliseconds(attemptTimeoutMilliseconds));
					if (admission.Failure == null)
					{
						IsHost = session.IsAdmin;
						MarkJoinPhase(JoinPhases.Admission, "admitted", answerStarted);
						return true;
					}

					failure = admission.Failure;
					rejected = admission.Answered;
					_joinPhase = rejected ? JoinPhases.Admission : JoinPhases.Answer;
					MarkJoinPhase(_joinPhase, rejected ? "refused" : "silent", answerStarted);
					DropLobbySession(session);
				}

				if (nearRoute && !rejected)
				{
					GameLog.LogToConsole($"[LobbyManager] The host's own-network address {target.Ip}:{target.Port} did not answer in {LocalJoinAttemptTimeoutMilliseconds} ms, so this screen is not on that network after all; joining at {target.PublicIp}:{target.PublicPort} instead.");
					_joinEndpoint = $"{target.PublicIp}:{target.PublicPort}";
				}
			}
		}
		finally
		{
			_establishingLobbyConnection = false;
		}

		if (!string.IsNullOrEmpty(failure) && !rejected && punchCoordinated)
		{
			JoinPunchOutcome punchOutcome = await ReadPunchStateAsync(target.Punch.Token);
			_joinPhase = punchOutcome == JoinPunchOutcome.Punched ? JoinPhases.Answer : JoinPhases.Punch;
			failure = JoinFailureSentence(_joinPhase);
		}

		LobbyJoinError = string.Equals(failure, LobbyLimits.KickedRejection, StringComparison.Ordinal) ? "You were removed from this lobby." : failure;
		LastHostError = failure;

		GameLog.LogError($"[LobbyManager] {failure}");
		await ReportJoinFailureAsync(ActiveLobbyId ?? string.Empty);
		Disconnect();
		ConnectionFailed?.Invoke("Direct connection handshake failed.");
		return false;
	}

	private void DropLobbySession(ClientServerSession session)
	{
		DetachLobbySession(session);
		session.Dispose();
		if (ReferenceEquals(_lobbySession, session)) _lobbySession = null;
	}

	private static void PunchFromGamePort(ClientNetworkChannel channel, string address, int port)
	{
		if (channel.SendToAddress(address, port, ReachabilityPunch.Datagram)) return;

		GameLog.LogError($"[LobbyManager] The punch to {address}:{port} could not be sent from the game port, so the host's router may not let its answer in.");
	}

	/// <summary>
	///     Reports this screen's own endpoint to the registry node from a socket bound to a port the join then connects from,
	///     and hands that port back. The report is what tells the lobby's host where to send: the host cannot read an address
	///     from the connection attempt itself, because a router that drops unsolicited traffic drops exactly that, and this
	///     screen cannot measure the external port its own router hands out. The socket is bound and closed here so the join's
	///     own socket can hold the same port, which is the only way the endpoint that was reported and the endpoint that
	///     connects are the same one. A punch is sent toward the lobby from that port before the report leaves: a router that
	///     refuses unsolicited traffic admits the host's answer only from an endpoint this side has already sent to, and the
	///     host punches as soon as the registry node reads the report, so an answer can meet a router that is still waiting for
	///     this side to speak and be dropped.
	/// </summary>
	/// <param name="ticket">The ticket the registry node stated for this join.</param>
	/// <param name="lobbyIp">The lobby's address, which the punch that opens this side of the route is aimed at.</param>
	/// <param name="lobbyPort">The lobby's port, which that punch is aimed at.</param>
	/// <returns>The local port the join must connect from, or zero when no report could be sent.</returns>
	private async Task<int> ReportJoinerEndpointFromJoinPortAsync(JoinerPunchTicket ticket, string lobbyIp, int lobbyPort)
	{
		var datagram = new byte[JoinerPunch.MaximumDatagramBytes];
		int written = JoinerPunch.TryWrite(datagram, ticket.Token);
		if (written == 0)
		{
			GameLog.LogError("[LobbyManager] The join's report ticket could not be written, so the host may never be told where to send.");
			return 0;
		}

		IPAddress? registryAddress = await ResolveRegistryAddressAsync(ticket.Address);
		if (registryAddress == null)
		{
			GameLog.LogError($"[LobbyManager] The registry node '{ticket.Address}' has no address this screen could report itself to, so the host may never be told where to send.");
			return 0;
		}

		try
		{
			using var socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
			socket.Bind(new IPEndPoint(IPAddress.Any, 0));

			int localPort = ((IPEndPoint)socket.LocalEndPoint!).Port;

			if (IPAddress.TryParse(lobbyIp, out IPAddress? lobbyAddress))
			{
				socket.SendTo(ReachabilityPunch.Datagram, new IPEndPoint(lobbyAddress, lobbyPort));
			}

			socket.SendTo(datagram.AsSpan(0, written), new IPEndPoint(registryAddress, ticket.Port));

			GameLog.LogToConsole($"[LobbyManager] Punched the lobby at {lobbyIp}:{lobbyPort} and reported this screen's endpoint to {ticket.Address}:{ticket.Port}, both from local port {localPort}, which this join then connects from.");
			return localPort;
		}
		catch (Exception exception) when (exception is SocketException or ArgumentException)
		{
			GameLog.LogError($"[LobbyManager] This screen could not report its endpoint to {ticket.Address}:{ticket.Port} ({exception.Message}), so the host may never be told where to send.");
			return 0;
		}
	}

	/// <summary>
	///     Resolves the registry node's address from the host name the join answer stated, so the report leaves from one
	///     address the node can read the source endpoint of.
	/// </summary>
	/// <param name="host">The host name or address text the ticket states.</param>
	/// <returns>The address to send the report to, or null when nothing could be resolved.</returns>
	private static async Task<IPAddress?> ResolveRegistryAddressAsync(string host)
	{
		if (IPAddress.TryParse(host, out IPAddress? parsed)) return parsed;

		try
		{
			IPAddress[] addresses = await Dns.GetHostAddressesAsync(host);
			foreach (IPAddress address in addresses)
			{
				if (address.AddressFamily == AddressFamily.InterNetwork) return address;
			}

			return addresses.Length > 0 ? addresses[0] : null;
		}
		catch (Exception exception) when (exception is SocketException or ArgumentException)
		{
			return null;
		}
	}

	/// <summary>
	///     Tells the registry node that this join failed, which is what lets the lobby's host read that real players could not
	///     get in. It states the step that broke, the endpoint it was aimed at, how many attempts were made and how long the
	///     join was given, and nothing about the player: the host needs to know somebody could not get in, not who. A direct
	///     lobby has no registry to tell and reports nothing.
	/// </summary>
	/// <param name="lobbyId">The lobby the join was for.</param>
	private async Task ReportJoinFailureAsync(string lobbyId)
	{
		if (string.IsNullOrEmpty(lobbyId) || lobbyId.StartsWith("direct-", StringComparison.Ordinal)) return;

		try
		{
			JoinFailureReport report = new(
				lobbyId,
				_joinPhase,
				_joinEndpoint,
				_joinAttempts,
				(int)Math.Clamp(Environment.TickCount64 - _joinStartedMilliseconds, 0, int.MaxValue));

			using var timeout = new CancellationTokenSource(TimeSpan.FromMilliseconds(2000));
			var content = new StringContent(JsonSerializer.Serialize(report), Encoding.UTF8, "application/json");
			using HttpResponseMessage response = await _httpClient.PostAsync($"{RegistryServerUrl}{JoinFailureRoutes.Report}", content, timeout.Token);
			if (!response.IsSuccessStatusCode) GameLog.LogToConsole($"[LobbyManager] The lobby server would not take this join failure report: {(int)response.StatusCode}");
		}
		catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException or NotSupportedException)
		{
			GameLog.LogToConsole($"[LobbyManager] This join failure could not be reported to the lobby server: {exception.GetType().Name}");
		}
	}

	/// <summary>What the registry node saw of one join's report and punch.</summary>
	private enum JoinPunchOutcome
	{
		/// <summary>The report never arrived, so the host was never told where to send.</summary>
		Unreported,

		/// <summary>The report arrived and the host was told, but the host never stated that it punched.</summary>
		Reported,

		/// <summary>The host stated that it punched this screen's endpoint.</summary>
		Punched
	}

	/// <summary>Reads what the registry node saw of a join's report and punch.</summary>
	/// <param name="token">The join the report was sent for.</param>
	/// <returns>The furthest step the registry node saw.</returns>
	private async Task<JoinPunchOutcome> ReadPunchStateAsync(string token)
	{
		try
		{
			using var timeout = new CancellationTokenSource(TimeSpan.FromMilliseconds(1500));
			using HttpResponseMessage response = await _httpClient.GetAsync($"{RegistryServerUrl}{JoinerPunchRoutes.State}/{token}", timeout.Token);
			if (!response.IsSuccessStatusCode) return JoinPunchOutcome.Unreported;

			using JsonDocument document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(timeout.Token));
			if (ReadBoolean(document.RootElement, "Punched")) return JoinPunchOutcome.Punched;
			return ReadBoolean(document.RootElement, "Reported") ? JoinPunchOutcome.Reported : JoinPunchOutcome.Unreported;
		}
		catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException or JsonException)
		{
			return JoinPunchOutcome.Unreported;
		}
	}

	/// <summary>Reads one boolean a registry node stated, accepting the casing the node writes.</summary>
	/// <param name="root">The answer the node stated.</param>
	/// <param name="name">The field to read.</param>
	/// <returns>Whether the field is true.</returns>
	private static bool ReadBoolean(JsonElement root, string name)
	{
		foreach (string candidate in new[] { name, $"{char.ToLowerInvariant(name[0])}{name[1..]}" })
		{
			if (root.TryGetProperty(candidate, out JsonElement value) && value.ValueKind == JsonValueKind.True) return true;
		}

		return false;
	}

	private async Task<string?> WaitForLobbyAdmissionAsync(ClientServerSession session)
	{
		var (failure, _) = await WaitForLobbyAdmissionAsync(session, TimeSpan.FromSeconds(15));
		return failure;
	}

	/// <summary>
	///     Waits for the authority to admit this screen. The second member separates a lobby that refused this join from one
	///     that never heard it: only the second is worth asking again, because a refusal is a decision the authority already
	///     made and a retry would put the same question to it.
	/// </summary>
	/// <param name="session">The session waiting for its answer.</param>
	/// <param name="budget">How long the authority has to answer before this attempt is given up.</param>
	/// <returns>Why the join failed or null when the authority admitted it, and whether a hello arrived at all.</returns>
	private async Task<(string? Failure, bool Answered)> WaitForLobbyAdmissionAsync(ClientServerSession session, TimeSpan budget)
	{
		var decided = new TaskCompletionSource<string?>(TaskCreationOptions.RunContinuationsAsynchronously);
		_admissionTcs = decided;
		bool helloAnswered = false;

		void OnHello(ServerHello hello)
		{
			helloAnswered = true;
			decided.TrySetResult(hello.Accepted ? null : hello.Rejection);
		}

		void OnLost(string reason) => decided.TrySetResult(reason);

		session.HelloAnswered += OnHello;
		session.ServerLost += OnLost;

		try
		{
			var completed = await Task.WhenAny(decided.Task, Task.Delay(budget));
			string? failure = completed == decided.Task ? decided.Task.Result : "The lobby's authority did not answer in time.";
			return (failure, helloAnswered);
		}
		finally
		{
			session.HelloAnswered -= OnHello;
			session.ServerLost -= OnLost;
			_admissionTcs = null;
		}
	}

	private void AttachLobbySession(ClientServerSession session)
	{
		_lobbySession = session;
		session.HelloAnswered += OnLobbyHelloAnswered;
		session.LobbyStateReceived += OnLobbyState;
		session.LobbyChatReceived += OnLobbyChat;
		session.LobbyMatchStartedEvent += OnLobbyMatchStarted;
		session.LobbyKickedEvent += OnLobbyKicked;
		session.LobbyStartWarningReceived += OnLobbyStartWarning;
		session.MapTransferReplied += OnMapTransferReplied;
		session.ServerLost += OnLobbyAuthorityLost;
	}

	private void DetachLobbySession(ClientServerSession session)
	{
		session.HelloAnswered -= OnLobbyHelloAnswered;
		session.LobbyStateReceived -= OnLobbyState;
		session.LobbyChatReceived -= OnLobbyChat;
		session.LobbyMatchStartedEvent -= OnLobbyMatchStarted;
		session.LobbyKickedEvent -= OnLobbyKicked;
		session.LobbyStartWarningReceived -= OnLobbyStartWarning;
		session.MapTransferReplied -= OnMapTransferReplied;
		session.ServerLost -= OnLobbyAuthorityLost;
	}

	private void OnLobbyStartWarning(LobbyStartWarningMessage warning)
	{
		if (!IsHost) return;

		StartWarningReceived?.Invoke(warning.Message ?? string.Empty);
	}

	private void OnLobbyHelloAnswered(ServerHello hello)
	{
		if (!hello.Accepted) return;

		IsHost = hello.IsAdmin;
		if (IsHost)
		{
			ReportLocalMapReadyState(true);
			_lobbySession?.SendLobbyCommand(new LobbyCommand
			{
				Kind = LobbyCommandKind.ReportHostStability,
				Text = HostStability
			});
		}

		PlayerListUpdated?.Invoke();
	}

	private void OnLobbyState(LobbyStateMessage state)
	{
		if (state.Revision < _lobbyRevision) return;
		_lobbyRevision = state.Revision;

		ApplyOpenSlots(state);

		ApplyMatchSettings(state);
		ColorOverlays = NormaliseColorArray(state.ColorOverlays);
		MinimapPlayerColors = NormaliseColorArray(state.MinimapPlayerColors);
		MinimapTeamColors = NormaliseColorArray(state.MinimapTeamColors);
		ProxyOwners = NormaliseIndexArray(state.ProxyOwners);
		LobbyFactions = state.Factions ?? Array.Empty<string>();
		LobbyTeamCount = state.TeamCount > 0 ? state.TeamCount : LobbyLimits.DefaultTeamCount;
		SpectatorDelay = state.SpectatorDelay;
		ShroudMode = Enum.IsDefined(typeof(MatchShroudMode), state.ShroudMode) ? (MatchShroudMode)state.ShroudMode : MatchShroudMode.FromMap;

		if (TeamsTogether != state.TeamsTogether)
		{
			TeamsTogether = state.TeamsTogether;
			TeamsTogetherChanged?.Invoke(TeamsTogether);
		}
		if (!string.IsNullOrEmpty(state.HostStability) && !string.Equals(state.HostStability, HostStability, StringComparison.Ordinal))
		{
			HostStability = state.HostStability;
			HostStabilityUpdated?.Invoke(HostStability);
		}

		if (!string.IsNullOrEmpty(state.MapName) && !string.Equals(state.MapName, ActiveMapName, StringComparison.Ordinal))
		{
			ActiveMapName = state.MapName;
			ActiveMapVersion = string.IsNullOrEmpty(state.MapVersion) ? "1.0.0" : state.MapVersion;
			ActiveMapChanged?.Invoke(ActiveMapName);
			if (!IsHost) _ = EnsureMapDownloadedAsync(ActiveMapName, ActiveMapVersion);
		}

		ApplySeats(state.Seats);
		ApplyCountdown(state);
		MakeSureThisSeatReportsItsMap();
		StartMatchPreparationForCurrentMap();
		PlayerListUpdated?.Invoke();
	}

	/// <summary>
	///     Copies one of the colour arrays the lobby published into a table indexed by player index, replacing one that is
	///     missing or the wrong length with an empty table, because a lobby that states no colour for an index must leave that
	///     index to the colour its seat carries.
	/// </summary>
	/// <param name="published">The array the lobby published.</param>
	/// <returns>One entry per player slot, zero for an index the lobby stated nothing for.</returns>
	private static uint[] NormaliseColorArray(uint[]? published)
	{
		var colors = new uint[LobbyLimits.MaximumSeats + 1];
		if (published == null) return colors;

		int count = Math.Min(published.Length, colors.Length);
		Array.Copy(published, colors, count);
		return colors;
	}

	/// <summary>
	///     Copies the standing-in index array the lobby published into a table indexed by player index, replacing one that is
	///     missing or the wrong length with an empty table, because a lobby that states nothing for an index leaves it drawn
	///     by its own relations.
	/// </summary>
	/// <param name="published">The array the lobby published.</param>
	/// <returns>One entry per player slot, zero for an index that stands in for nobody.</returns>
	private static int[] NormaliseIndexArray(int[]? published)
	{
		var owners = new int[LobbyLimits.MaximumSeats + 1];
		if (published == null) return owners;

		int count = Math.Min(published.Length, owners.Length);
		Array.Copy(published, owners, count);
		return owners;
	}

	/// <summary>
	///     Reads which player slots the lobby leaves open, which is the whole of how the lobby is sized: every row it
	///     published is drawn, and only the open ones accept a player.
	/// </summary>
	/// <param name="state">The lobby state the authority published.</param>
	private void ApplyOpenSlots(LobbyStateMessage state)
	{
		SlotCount = state.SlotCount > 0 ? Math.Min(state.SlotCount, LobbyLimits.MaximumSeats) : LobbyLimits.MaximumSeats;

		for (int slot = 0; slot < _openSlots.Length; slot++) _openSlots[slot] = false;

		int openSlots = 0;
		for (int slot = 1; slot <= SlotCount; slot++)
		{
			bool open = state.OpenSlots != null && slot < state.OpenSlots.Length ? state.OpenSlots[slot] : true;
			_openSlots[slot] = open;
			if (open) openSlots++;
		}

		OpenSlotCount = openSlots;
	}

	/// <summary>Reads the match settings the lobby's map declared, which the panel that edits them is drawn from.</summary>
	/// <param name="state">The lobby state the authority published.</param>
	private void ApplyMatchSettings(LobbyStateMessage state)
	{
		LobbyMatchSettingMessage[] published = state.Settings ?? Array.Empty<LobbyMatchSettingMessage>();

		MatchSettings.Clear();

		for (int index = 0; index < published.Length; index++)
		{
			LobbyMatchSettingMessage message = published[index];
			var setting = new LobbyMatchSetting
			{
				SettingId = message.SettingId,
				Label = message.Label ?? string.Empty,
				Tooltip = message.Tooltip ?? string.Empty,
				Control = Enum.IsDefined(typeof(LobbySettingControl), message.Control) ? (LobbySettingControl)message.Control : LobbySettingControl.Toggle,
				Value = message.Value,
				Minimum = message.Minimum,
				Maximum = message.Maximum,
				Options = message.Options ?? Array.Empty<string>()
			};

			MatchSettings.Add(setting);
		}

		MatchSettingsChanged?.Invoke();
	}

	private void StartMatchPreparationForCurrentMap()
	{
		if (string.IsNullOrWhiteSpace(ActiveMapName)) return;

		bool holdsMap;
		if (IsHost)
		{
			string? hostMapDirectory = GameHost.ResolveMapDirectory(ActiveMapName, ActiveMapVersion);
			holdsMap = !string.IsNullOrEmpty(hostMapDirectory) && Directory.Exists(hostMapDirectory);
		}
		else
		{
			holdsMap = MapAssetManager.IsMapDownloaded(ActiveMapName, ActiveMapVersion);
		}

		if (!holdsMap) return;

		bool preparedForThisMap = _preparation.IsPrepared && string.Equals(_preparation.MapName, ActiveMapName, StringComparison.Ordinal);
		if (preparedForThisMap)
		{
			if (!_worldPrepared || !string.Equals(_worldPreparedMapName, ActiveMapName, StringComparison.Ordinal)) BeginMatchWorldBuild();
			ReportWorldPreparedIfReady();
			return;
		}

		if (_preparation.IsPreparing(ActiveMapName)) return;

		if (_worldPrepared) GameContext.Host?.DiscardPreparedMatchWorld();
		_worldPrepared = false;
		_worldPreparedMapName = null;

		ReportLocalLoadReadyState(false);
		MatchLoadTrace.Begin("lobby_preparation");
		_preparation.Prepare(ActiveMapName, ActiveMapVersion);
		MatchLoadTrace.Mark("preparation_started");
		MatchLoadTrace.WriteSummary("preparation_started");

		if (_preparation.HasWork) return;
		BeginMatchWorldBuild();
	}

	private void MakeSureThisSeatReportsItsMap()
	{
		if (IsHost) return;

		PlayerInfo? me = PlayerList.Find(IsLocalPlayer);
		if (me == null || me.IsMapReady) return;
		if (string.IsNullOrWhiteSpace(ActiveMapName)) return;

		if (MapAssetManager.IsMapDownloaded(ActiveMapName, ActiveMapVersion))
		{
			ReportLocalMapReadyState(true);
			return;
		}

		if (_downloadingMapName != ActiveMapName) _ = EnsureMapDownloadedAsync(ActiveMapName, ActiveMapVersion);
	}

	private void ApplySeats(LobbySeat[] seats)
	{
		PlayerList.Clear();
		if (seats == null) return;

		for (int index = 0; index < seats.Length; index++)
		{
			LobbySeat seat = seats[index];
			PlayerList.Add(new PlayerInfo
			{
				PeerId = seat.PeerId,
				Slot = seat.Slot,
				AccountId = seat.AccountId,
				SteamId = seat.SteamId,
				Name = seat.Name ?? string.Empty,
				Faction = seat.Faction ?? string.Empty,
				Team = seat.TeamName ?? string.Empty,
				Color = seat.ColorRgba == 0 ? PlayerColorConfig.GetColor(seat.Slot) : UnpackColor(seat.ColorRgba),
				IsHost = seat.IsHost,
				IsComputer = seat.IsComputer,
				Difficulty = LobbyBotDifficultyRules.Clamp(seat.Difficulty),
				IsReady = seat.IsReady,
				IsMapReady = seat.IsMapReady,
				MapDownloadPercent = seat.MapDownloadPercent,
				IsLoadReady = seat.IsLoadReady,
				IsDisconnected = seat.IsDisconnected,
				Latency = FormatLinkMilliseconds(seat.LatencyMilliseconds),
				Jitter = FormatLinkMilliseconds(seat.JitterMilliseconds),
				PacketLoss = FormatLinkPercent(seat.PacketLossPercent),
				BinaryVersion = RealmVersion.GameBinaryVersion,
				SessionToken = seat.SessionToken ?? string.Empty
			});
		}
	}

	private void ApplyCountdown(LobbyStateMessage state)
	{
		if (state.CountdownActive)
		{
			bool started = !_countdownActive;
			_countdownActive = true;
			_countdownMapName = state.MapName;

			if (started)
			{
				_countdownRemaining = state.CountdownRemainingSeconds;
				CountdownStarted?.Invoke(state.MapName, state.CountdownRemainingSeconds);

				BeginMatchPresentation(state.MapName);
			}
			else if (state.CountdownRemainingSeconds != _countdownRemaining)
			{
				_countdownRemaining = state.CountdownRemainingSeconds;
				CountdownTick?.Invoke(_countdownRemaining);
			}

			return;
		}

		if (!_countdownActive) return;

		_countdownActive = false;
		_countdownMapName = null;
		_countdownRemaining = 0;
		CountdownCancelled?.Invoke();
		EndMatchPresentation();
	}

	private void BeginMatchPresentation(string mapName)
	{
		if (_matchPresentationOpen || _matchStarted) return;

		ActiveMapName = mapName;
		GameContext.ReplayPlayback.StopReplay();

		GameHost? host = GameContext.Host;
		while (host != null && host.IsBuildingMatchWorld) host.AdvanceMatchWorldPreparation();
		if (host != null && host.HasPreparedMatchWorld)
		{
			_worldPrepared = true;
			_worldPreparedMapName = mapName;
		}

		if (!_worldPrepared) return;

		_matchPresentationOpen = true;
		MatchLoadTrace.Begin("start_presentation");
		MatchLoadTrace.Mark("countdown_received");
		GameLog.LogToConsole($"[LobbyManager] Showing the match world for '{mapName}' under the countdown");

		host?.ShowPreparedMatchWorld();
		GameContext.UserInterface?.TransitionTo(GameScreen.InGameHUD);
	}

	private void EndMatchPresentation()
	{
		if (!_matchPresentationOpen) return;

		_matchPresentationOpen = false;
		GameLog.LogToConsole("[LobbyManager] Countdown cancelled; returning to the lobby");
		GameContext.UserInterface?.TransitionTo(GameScreen.LobbyRoom);
	}

	public void ReportLocalLoadReadyState(bool isLoadReady)
	{
		_lobbySession?.SendLobbyCommand(new LobbyCommand
		{
			Kind = LobbyCommandKind.ReportLoadReady,
			Flag = isLoadReady
		});
	}

	private void OnLobbyChat(LobbyChatMessage line)
	{
		string sender = string.IsNullOrEmpty(line.SenderName) ? "System" : line.SenderName;
		if (line.SenderPeerId > 0 && line.SenderAccountId == 0) sender = UnverifiedNameMark.Apply(sender);

		int senderSlot = PlayerList.Find(p => p.PeerId == line.SenderPeerId)?.Slot ?? 0;
		if (line.SenderPeerId > 0) ServerChatCommandReceived?.Invoke(senderSlot, line.Message);
		ChatReceived?.Invoke(sender, line.Message, false, line.TimestampUtcTicks);
	}

	private void OnLobbyKicked(LobbyKickedMessage kicked)
	{
		GameLog.LogToConsole($"[LobbyManager] Removed from the lobby: {kicked.Reason}");
		KickReceived?.Invoke(string.IsNullOrEmpty(kicked.Reason) ? "Kicked by Host" : kicked.Reason);

		bool wasBuilding = _matchPresentationOpen;
		var session = _lobbySession;
		if (session != null)
		{
			DetachLobbySession(session);
			session.Dispose();
			_lobbySession = null;
		}

		ResetLobbyState();
		if (wasBuilding) GameContext.UserInterface?.TransitionTo(GameScreen.MainMenu);
	}

	private void OnLobbyAuthorityLost(string reason)
	{
		if (_matchStarted) return;
		if (_establishingLobbyConnection) return;

		// A kicked connection drops before its reason has been read, and the reconnect that follows is refused by
		// name. That refusal is the kick arriving late rather than a lobby that failed.
		bool kicked = reason.Contains(LobbyLimits.KickedRejection, StringComparison.Ordinal);
		bool wasBuilding = _matchPresentationOpen;
		bool administeredHere = IsHost;
		if (administeredHere && !kicked)
		{
			LastHostError = reason;
			GameLog.LogError($"[LobbyManager] The lobby's authority is gone: {reason}");
		}

		var session = _lobbySession;
		int hostedPort = session?.Port ?? ENetPort;
		if (session != null)
		{
			DetachLobbySession(session);
			session.Dispose();
			_lobbySession = null;
		}

		ReleaseHostedEndpoint(hostedPort);
		ResetLobbyState();

		if (kicked)
		{
			KickReceived?.Invoke("Kicked by Host");
			return;
		}

		ConnectionFailed?.Invoke(administeredHere ? reason : "The host's lobby is no longer running.");
		if (wasBuilding) GameContext.UserInterface?.TransitionTo(GameScreen.MainMenu);
	}

	private void OnLobbyMatchStarted(LobbyMatchStartedMessage started)
	{
		if (_matchStarted) return;
		_matchStarted = true;
		IsGameStarted = true;
		_countdownActive = false;
		CountdownFinished?.Invoke();

		if (IsHost) UnregisterActiveLobbyFromRegistry();

		ClientServerSession? session = _lobbySession;
		if (session == null) return;

		if (_matchPresentationOpen)
		{
			_matchPresentationOpen = false;
			DetachLobbySession(session);
			_lobbySession = null;
			MatchSeed = started.Seed;
			GameContext.RandomService.SetSeed(started.Seed);
			GameSessionStartTime = DateTime.UtcNow;
			GameContext.AttachServerSession(session);
			GameContext.Host?.AttachMatchSession();
			GameContext.Host?.StartMatchRecording();
			MatchLoadTrace.Mark("match_started");
			return;
		}

		DetachLobbySession(session);
		_lobbySession = null;
		GameContext.AttachServerSession(session);
		EnterMatchScene(started.MapName, started.Seed);
	}

	private void EnterMatchScene(string mapName, int matchSeed)
	{
		GameLog.LogToConsole($"[LobbyManager] Match started for '{mapName}' with deterministic random seed {matchSeed}");
		GameContext.ReplayPlayback.StopReplay();
		ActiveMapName = mapName;
		MatchSeed = matchSeed;
		GameContext.RandomService.SetSeed(matchSeed);

		IsGameStarted = true;
		GameSessionStartTime = DateTime.UtcNow;

		GetTree().ChangeSceneToFile("res://GameHost.tscn");
	}

}
