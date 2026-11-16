using System;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Realm.Client.Core;
using Realm.Client.Services;
using Realm.Client.UI;
using Realm.Shared.Network.Relay;
using Realm.Shared.Reachability;

namespace Realm.Client.Network;

/// <summary>
///     A player's offer to carry other players' match traffic. It is the same bargain map seeding already is, applied to
///     live connections: off by default, bounded by a bandwidth budget and a peer ceiling, registered with the lobby server,
///     unregistered on stop, and only offered while this machine's own endpoint was measured as reachable. It carries bytes
///     it cannot read, on a lane of its own, and it stops the moment this player starts a match of their own.
/// </summary>
public class PeerRelayVolunteer
{
	private const int HeartbeatMilliseconds = 10_000;

	private const int RegistrationRetryMilliseconds = 15_000;

	private readonly string _volunteerId = Guid.NewGuid().ToString("N");

	private RelayForwarder? _forwarder;
	private CancellationTokenSource? _lifecycle;
	private bool _isVolunteering;
	private string _lastStatement = string.Empty;
	private int _startedBudgetBytesPerSecond;
	private int _startedMaximumConnections;

	/// <summary>Whether this machine is forwarding other players' connections right now.</summary>
	public bool IsVolunteering => _isVolunteering;

	/// <summary>The port this machine forwards on, or zero while it is not volunteering.</summary>
	public int Port => _forwarder?.Port ?? 0;

	/// <summary>
	///     Whether the lobby server this machine registered with pooled it. An offer that is open but not pooled is carrying
	///     nothing and is handed to nobody, so the screen states that rather than the offer it made.
	/// </summary>
	public bool IsPooled { get; private set; }

	/// <summary>What this machine has forwarded, which its settings page and its log state.</summary>
	public RelayForwarderStatistics? Usage => _forwarder?.Statistics;

	/// <summary>
	///     The sentence this volunteer's screen shows about itself: what it measured, what it was told, or why it is not
	///     offering anything. It never states a connection as carried when nothing measured it.
	/// </summary>
	public string Statement => _lastStatement;

	/// <summary>
	///     Runs the volunteer between matches. It is called from the same slow loop that keeps map seeding up to date, so a
	///     setting either way takes effect without the player restarting anything: the offer is opened with whatever the
	///     settings say now, and an offer opened with other limits is given up and made again, because the budget and the
	///     connection ceiling are the only control the player has over what this machine spends.
	/// </summary>
	public void CheckIdleAndVolunteerStatus()
	{
		bool shouldVolunteer = IsVolunteerConditionMet();
		if (shouldVolunteer && _isVolunteering && !OfferMatchesSettings()) Stop(unregister: false);

		if (shouldVolunteer && !_isVolunteering) Start();
		else if (!shouldVolunteer && _isVolunteering) Stop();
	}

	/// <summary>Whether the offer this machine is making was opened with the limits its settings state now.</summary>
	/// <returns>Whether the running forwarder is the one the settings describe.</returns>
	private bool OfferMatchesSettings() => _startedBudgetBytesPerSecond == GameSettings.VolunteerRelayBudgetBytesPerSecond && _startedMaximumConnections == GameSettings.VolunteerRelayMaximumConnections;

	private static bool IsVolunteerConditionMet()
	{
		if (!GameSettings.VolunteerRelay) return false;

		LobbyManager? lobby = GameContext.Lobby;
		if (lobby == null) return false;
		if (lobby.IsSinglePlayer || lobby.IsGameStarted) return false;

		// A machine behind a symmetric router cannot be reached by the players it would carry, which is the same measurement
		// that stops it hosting. Offering bandwidth it cannot deliver would only send those players at a wall.
		if (lobby.LocalNatType == NatType.Symmetric) return false;

		return !string.IsNullOrEmpty(lobby.RegistryServerUrl);
	}

	private void Start()
	{
		if (_isVolunteering) return;

		LobbyManager? lobby = GameContext.Lobby;
		if (lobby == null) return;

		if (!RelayForwarder.TryOpen(0, GameSettings.VolunteerRelayMaximumConnections, GameSettings.VolunteerRelayBudgetBytesPerSecond, out RelayForwarder? forwarder, out string failure))
		{
			_lastStatement = failure;
			GameLog.LogError($"[RelayVolunteer] {failure}");
			return;
		}

		var lifecycle = new CancellationTokenSource();
		_forwarder = forwarder;
		_lifecycle = lifecycle;
		_isVolunteering = true;
		IsPooled = false;
		_startedBudgetBytesPerSecond = GameSettings.VolunteerRelayBudgetBytesPerSecond;
		_startedMaximumConnections = GameSettings.VolunteerRelayMaximumConnections;

		CancellationToken token = lifecycle.Token;
		Task forwarding = Task.Run(() => forwarder!.Run(token), token);
		Task registration = Task.Run(() => MaintainRegistrationAsync(lobby, forwarder!, token), token);

		// The source is given up only once both lanes have finished with it: disposing it while one of them is waiting on
		// its own token is a race the stopping side would lose, and the lane it breaks is one nobody is watching.
		_ = Task.WhenAll(forwarding, registration).ContinueWith(
			_ => lifecycle.Dispose(),
			CancellationToken.None,
			TaskContinuationOptions.ExecuteSynchronously,
			TaskScheduler.Default);

		GameLog.LogToConsole($"[RelayVolunteer] Offering to carry other players' connections on UDP port {forwarder!.Port}, within {GameSettings.VolunteerRelayBudgetBytesPerSecond}B/s and {GameSettings.VolunteerRelayMaximumConnections} connection(s).");
	}

	/// <summary>
	///     Stops offering this machine's bandwidth. The forwarding socket is closed at once, so nothing it was carrying is
	///     delayed behind a shutdown, and the pool is told so it stops handing this machine out.
	/// </summary>
	public void Stop() => Stop(unregister: true);

	/// <summary>
	///     Gives up the forwarding socket and the lane that ran it.
	/// </summary>
	/// <param name="unregister">
	///     Whether the lobby server is told to forget this machine. A machine restarting its own offer keeps its place instead:
	///     the identifier it is pooled under is the identifier an unregister would remove, so a request that arrived after the
	///     new offer was pooled would take the volunteer out of the pool it had just joined.
	/// </param>
	private void Stop(bool unregister)
	{
		if (!_isVolunteering) return;

		_isVolunteering = false;
		IsPooled = false;
		_startedBudgetBytesPerSecond = 0;
		_startedMaximumConnections = 0;
		_lifecycle?.Cancel();
		_forwarder?.Dispose();
		_forwarder = null;
		_lifecycle = null;

		if (unregister) SendUnregisterRequest();

		_lastStatement = unregister
			? "This machine stopped carrying other players' connections."
			: "This machine is offering its bandwidth again with the limits this page states.";
		GameLog.LogToConsole(unregister
			? "[RelayVolunteer] Stopped carrying other players' connections."
			: "[RelayVolunteer] The offer is being made again with the limits the settings state now.");
	}

	/// <summary>
	///     Moves this volunteer between matches: it starts it when the setting is on and the machine is idle, and stops it when
	///     the player starts a match of their own, which is the one thing its own tick must never compete with.
	/// </summary>
	/// <param name="lobbyIsBusy">Whether this screen is hosting, joining or playing right now.</param>
	public void ApplyMatchState(bool lobbyIsBusy)
	{
		if (lobbyIsBusy && _isVolunteering)
		{
			Stop();
			_lastStatement = "This machine stopped carrying other players' connections because this player started a match.";
			return;
		}

		CheckIdleAndVolunteerStatus();
	}

	private async Task MaintainRegistrationAsync(LobbyManager lobby, RelayForwarder forwarder, CancellationToken token)
	{
		using var httpClient = new HttpClient();
		string registryUrl = lobby.RegistryServerUrl;
		if (string.IsNullOrEmpty(registryUrl)) return;

		bool registered = false;
		while (!token.IsCancellationRequested && _isVolunteering)
		{
			try
			{
				if (!registered)
				{
					string measuredClass = await MeasureOwnReachabilityAsync(lobby, forwarder, token).ConfigureAwait(false);
					(bool pooled, string reason) = await RegisterAsync(httpClient, registryUrl, lobby, forwarder, measuredClass, token).ConfigureAwait(false);
					registered = pooled;
					IsPooled = pooled;
					_lastStatement = pooled
						? $"This machine is offering to carry up to {GameSettings.VolunteerRelayMaximumConnections} of another player's connections on port {forwarder.Port}, within {GameSettings.VolunteerRelayBudgetBytesPerSecond} bytes a second."
						: reason;

					if (!pooled)
					{
						GameLog.LogToConsole($"[RelayVolunteer] This machine is not offering bandwidth: {reason}");

						// A machine the pool would not take is not one players are sent to, so the forwarding socket is given up
						// rather than left open for traffic this node never handed out. The slow status loop tries again. The
						// reason is stated after the stop, because stopping states its own and this is the sentence the player
						// needs: without it the screen says only that nothing is being carried, never why.
						Stop();
						_lastStatement = reason;
						return;
					}
				}
				else if (!await HeartbeatAsync(httpClient, registryUrl, forwarder, token).ConfigureAwait(false))
				{
					// A node restarts, is redeployed, or forgets a volunteer that went quiet for longer than its membership lasts.
					// A volunteer that kept heartbeating into a pool that no longer holds it would be carrying nothing while its
					// own screen said it was offered, so the offer is made again rather than renewed into silence.
					GameLog.LogToConsole("[RelayVolunteer] The lobby server no longer pools this machine, so it is offering its bandwidth again.");
					registered = false;
					IsPooled = false;
				}
			}
			catch (OperationCanceledException)
			{
				return;
			}
			catch (Exception exception)
			{
				GameLog.LogError($"[RelayVolunteer] Registration with {registryUrl} failed, so this machine may not be pooled: {exception.GetType().Name}: {exception.Message}");
				registered = false;

				// Nothing this machine offers reached the node that hands volunteers out, so the screen stops claiming an offer
				// that was pooled and states the failure instead. The socket stays open and the loop keeps retrying, because a
				// node that is briefly away is not a reason to give up a setting the player turned on.
				IsPooled = false;
				_lastStatement = string.Empty;
			}

			await Task.Delay(registered ? HeartbeatMilliseconds : RegistrationRetryMilliseconds, token).ConfigureAwait(false);
		}
	}

	/// <summary>
	///     Measures the port this machine offers, from the socket it offers it on. A node cannot reach a port nothing opens,
	///     so the volunteer knocks from the socket it is offering and reads the class the node measured from the answer. A
	///     machine that cannot be reached states that it cannot, instead of registering and failing every player it is given.
	/// </summary>
	private static async Task<string> MeasureOwnReachabilityAsync(LobbyManager lobby, RelayForwarder forwarder, CancellationToken token)
	{
		string publicIp = lobby.PublicIP;
		if (string.IsNullOrEmpty(publicIp) || string.IsNullOrEmpty(lobby.RegistryServerUrl)) return ReachabilityClasses.Unverified;

		try
		{
			using var httpClient = new HttpClient();
			var verifier = new HostReachabilityVerifier(httpClient, message => GameLog.LogToConsole($"[RelayVolunteer] {message}"));
			HostReachabilityResult result = await verifier.VerifyAsync(
				lobby.RegistryServerUrl,
				publicIp,
				forwarder.Port,
				(address, port, probeId) =>
				{
					Span<byte> datagram = stackalloc byte[ReachabilityPunch.MaximumDatagramBytes];
					int written = ReachabilityPunch.TryWrite(datagram, probeId);
					return written > 0 && forwarder.TrySendFromOfferedPort(address, port, datagram[..written]);
				},
				token).ConfigureAwait(false);

			if (result.Verified) return result.ReachabilityClass.Length > 0 ? result.ReachabilityClass : ReachabilityClasses.Unverified;

			GameLog.LogToConsole($"[RelayVolunteer] The volunteer's own port could not be measured as reachable from outside: {result.Detail}");
			return result.ReachabilityClass.Length > 0 ? result.ReachabilityClass : ReachabilityClasses.Unverified;
		}
		catch (Exception exception) when (exception is HttpRequestException or OperationCanceledException)
		{
			return ReachabilityClasses.Unverified;
		}
	}

	private async Task<(bool Pooled, string Reason)> RegisterAsync(HttpClient httpClient, string registryUrl, LobbyManager lobby, RelayForwarder forwarder, string measuredClass, CancellationToken token)
	{
		var payload = new
		{
			RelayId = _volunteerId,
			ReportedIP = lobby.PublicIP,
			Port = forwarder.Port,
			MaximumConnections = GameSettings.VolunteerRelayMaximumConnections,
			BudgetBytesPerSecond = GameSettings.VolunteerRelayBudgetBytesPerSecond,
			MaximumPayloadBytes = RelayForwarder.MaximumDatagramBytes,
			Carrier = RelayCarrierKind.UdpTunnel.ToString(),
			ReachabilityClass = measuredClass
		};

		var content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");
		using HttpResponseMessage response = await httpClient.PostAsync($"{registryUrl.TrimEnd('/')}/relays/register", content, token).ConfigureAwait(false);
		if (!response.IsSuccessStatusCode) return (false, $"The lobby server refused this machine's offer ({response.StatusCode}).");

		string body = await response.Content.ReadAsStringAsync(token).ConfigureAwait(false);
		if (!RelayRegistrationAnswer.TryParse(body, out RelayRegistrationAnswer answer)) return (false, answer.Reason);

		return (answer.Advertised, answer.Reason.Length > 0 ? answer.Reason : "The lobby server did not pool this machine, so it is carrying nothing.");
	}

	/// <summary>
	///     Tells the lobby server this volunteer is still here and how many connections it holds.
	/// </summary>
	/// <param name="httpClient">The client the request is sent with.</param>
	/// <param name="registryUrl">The lobby server that pooled this machine.</param>
	/// <param name="forwarder">The forwarder whose usage is reported.</param>
	/// <param name="token">What stops the request when this machine stops offering.</param>
	/// <returns>Whether the node still pools this machine.</returns>
	private async Task<bool> HeartbeatAsync(HttpClient httpClient, string registryUrl, RelayForwarder forwarder, CancellationToken token)
	{
		var payload = new { RelayId = _volunteerId, ActiveConnections = forwarder.Statistics.Connections };
		var content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");
		using HttpResponseMessage response = await httpClient.PostAsync($"{registryUrl.TrimEnd('/')}/relays/heartbeat", content, token).ConfigureAwait(false);
		if (!response.IsSuccessStatusCode) return false;

		string body = await response.Content.ReadAsStringAsync(token).ConfigureAwait(false);
		return RelayHeartbeatAnswer.TryParse(body, out RelayHeartbeatAnswer answer) && answer.Known;
	}

	private void SendUnregisterRequest()
	{
		string? registryUrl = GameContext.Lobby?.RegistryServerUrl;
		if (string.IsNullOrEmpty(registryUrl)) return;

		Task.Run(async () =>
		{
			try
			{
				using var httpClient = new HttpClient();
				var content = new StringContent(JsonSerializer.Serialize(new { RelayId = _volunteerId }), Encoding.UTF8, "application/json");
				await httpClient.PostAsync($"{registryUrl.TrimEnd('/')}/relays/unregister", content).ConfigureAwait(false);
			}
			catch (Exception exception)
			{
				GameLog.LogError($"[RelayVolunteer] This machine could not be taken out of the volunteer pool, so it may stay listed until it expires: {exception.GetType().Name}: {exception.Message}");
			}
		});
	}
}
