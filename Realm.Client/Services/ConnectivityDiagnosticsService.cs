using System;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using Realm.Client.Network;
using Realm.Client.UI;
using Realm.Shared.Reachability;

namespace Realm.Client.Services;

/// <summary>
///     Gathers what this machine can actually be asked, and turns it into the answers the connectivity help shows. The
///     classification itself is the shared one, so the dialog and any later consumer read the same verdicts; what lives here
///     is the reading: the lobby manager's own measurements, one question to the lobby server, one to the firewall, one bind
///     of the game port, and one look at the machine's interfaces.
/// </summary>
/// <remarks>
///     Nothing here changes the machine, publishes a lobby, punches anything or waits on the match: it is a screen's
///     investigation of a failure that already happened. The results are kept for a short while so pressing a second check
///     reads the same measurement instead of asking the firewall again.
/// </remarks>
public class ConnectivityDiagnosticsService
{
	private const int LobbyServerProbeTimeoutMilliseconds = 4000;

	private const int FirewallReadTimeoutMilliseconds = 6000;

	/// <summary>How long one reading is reused, which is long enough for a reader to work through the list.</summary>
	private const int ReadingLifetimeMilliseconds = 20_000;

	/// <summary>
	///     One client for every probe, so a dialog opened and reopened does not leak a connection pool per press. The timeout
	///     is per question rather than on the client, because one question is all a probe ever asks.
	/// </summary>
	private static readonly HttpClient LobbyServerProbeClient = new() { Timeout = Timeout.InfiniteTimeSpan };

	private ConnectivityFacts _reading;

	private ConnectivityAudience _readingAudience;

	private long _readingTakenMilliseconds = -ReadingLifetimeMilliseconds;

	private ConnectivityEvidence _rememberedEvidence;

	private bool _hasRememberedEvidence;

	/// <summary>
	///     What the last hosted lobby measured, whether or not this screen still holds it. A live lobby is read as it is now;
	///     once it has been given up, the copy kept when the host left it is read instead, so a host who closed a lobby to open
	///     the troubleshooting screen still has the failure, the ports and the addresses that lobby was measured with.
	/// </summary>
	public ConnectivityEvidence CurrentEvidence
	{
		get
		{
			LobbyManager? lobby = GameContext.Lobby;
			if (lobby != null) return Capture(lobby);
			return _hasRememberedEvidence ? _rememberedEvidence : default;
		}
	}

	/// <summary>
	///     Keeps what the lobby this screen holds has measured, so it survives the lobby being given up. A screen that opens
	///     troubleshooting from inside a lobby calls this before it leaves, because leaving is what takes the evidence away.
	/// </summary>
	public void RememberEvidence()
	{
		LobbyManager? lobby = GameContext.Lobby;
		if (lobby == null) return;

		_rememberedEvidence = Capture(lobby);
		_hasRememberedEvidence = true;
		DiscardReading();
	}

	/// <summary>
	///     Builds what the lobby manager already knows, without asking this machine anything. A surface opens on this, so the
	///     measured failure and the one-line likely cause are on screen before any check has run.
	/// </summary>
	/// <param name="audience">Which side of the connection is being explained.</param>
	/// <returns>What the checks may read, with every live reading left unanswered.</returns>
	public ConnectivityFacts DescribeKnownFacts(ConnectivityAudience audience)
	{
		return CurrentEvidence.ToFacts(audience);
	}

	/// <summary>
	///     Reads everything a lobby manager holds about its own network as one snapshot, including the address players on this
	///     network join at - withheld when the lobby could not hold this computer's own seat, because an unlisted address does
	///     not fix a seat nobody can be given.
	/// </summary>
	/// <param name="lobby">The lobby to read.</param>
	/// <returns>The snapshot.</returns>
	private static ConnectivityEvidence Capture(LobbyManager lobby)
	{
		bool localSeatFailure = lobby.LastHostFailure is { Failure: HostReachabilityFailure.LoopbackUnanswered or HostReachabilityFailure.LoopbackNotOnLocalAddress };

		return new ConnectivityEvidence
		{
			HostFailure = lobby.LastHostFailure?.Failure ?? HostReachabilityFailure.None,
			ListedPort = lobby.LobbyPort,
			PreferredPort = LobbyManager.DefaultGamePort,
			ObservedOutsidePort = lobby.LastHostFailure?.ObservedPort ?? 0,
			MappingBehaviour = lobby.MappingBehaviour,
			ReachabilityClass = lobby.ReachabilityClass,
			FirewallGrant = lobby.LastFirewallGrant.Grant,
			LocalNatMeasured = lobby.HasLocalNatType,
			LocalNatIsSymmetric = lobby.LocalNatType == NatType.Symmetric,
			MeasuredOutsideAddress = lobby.MeasuredOutsideAddress,
			OffersIpv4 = lobby.OffersIpv4,
			JoinPhase = lobby.LastJoinPhase,
			PlayerJoinFailure = lobby.LastPlayerJoinFailure,
			DirectConnectAddress = localSeatFailure ? null : lobby.DirectConnectAddress,
			DiagnosticsBlock = lobby.DescribeNetworkDiagnostics()
		};
	}

	/// <summary>
	///     Reads everything the checks may consult for one side of the connection, keeping the previous reading when it is
	///     recent enough to still describe this machine.
	/// </summary>
	/// <param name="audience">Which side of the connection is being explained.</param>
	/// <param name="cancellationToken">Cancels the reading when the dialog closes.</param>
	/// <returns>What the checks may read.</returns>
	public async Task<ConnectivityFacts> MeasureAsync(ConnectivityAudience audience, CancellationToken cancellationToken = default)
	{
		long now = Environment.TickCount64;
		if (_readingTakenMilliseconds >= 0 && now - _readingTakenMilliseconds < ReadingLifetimeMilliseconds && _readingAudience == audience) return _reading;

		ConnectivityFacts known = DescribeKnownFacts(audience);
		LobbyManager? lobby = GameContext.Lobby;
		int portForFirewall = known.ListedPort > 0 ? known.ListedPort : known.PreferredPort;

		bool answered = lobby != null && await AskLobbyServerAsync(lobby.RegistryServerUrl, cancellationToken).ConfigureAwait(false);

		WindowsFirewallRuleState rule = WindowsFirewallRuleState.Unknown;
		if (OperatingSystem.IsWindows() && portForFirewall > 0)
		{
			using var firewallTimeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
			firewallTimeout.CancelAfter(FirewallReadTimeoutMilliseconds);
			try
			{
				rule = await GameContext.WindowsFirewall.ReadRuleStateAsync(portForFirewall, firewallTimeout.Token).ConfigureAwait(false);
			}
			catch (OperationCanceledException)
			{
				rule = WindowsFirewallRuleState.Unknown;
			}
		}

		ConnectivityFacts facts = known with
		{
			FirewallRule = rule,
			LobbyServerProbed = lobby != null,
			LobbyServerAnswered = answered,
			TunnelAdapterUp = LocalTunnelAdapters.AnyUp(),
			PreferredPortHeldElsewhere = IsPreferredPortHeldByAnotherProgram(lobby, known.PreferredPort, known.PreferredPortHeldElsewhere)
		};

		_reading = facts;
		_readingAudience = audience;
		_readingTakenMilliseconds = Environment.TickCount64;
		return facts;
	}

	/// <summary>Runs every check for one side of the connection, keeping the reading for the checks that follow.</summary>
	/// <param name="audience">Which side of the connection is being explained.</param>
	/// <param name="cancellationToken">Cancels the reading when the dialog closes.</param>
	/// <returns>One answer per listed issue.</returns>
	public async Task<System.Collections.Generic.IReadOnlyList<ConnectivityDiagnosis>> RunAllAsync(ConnectivityAudience audience, CancellationToken cancellationToken = default)
	{
		ConnectivityFacts facts = await MeasureAsync(audience, cancellationToken).ConfigureAwait(false);
		return ConnectivityCheck.EvaluateAll(facts);
	}

	/// <summary>Runs one issue's check.</summary>
	/// <param name="audience">Which side of the connection is being explained.</param>
	/// <param name="issue">The issue to check.</param>
	/// <param name="cancellationToken">Cancels the reading when the dialog closes.</param>
	/// <returns>The answer for that issue.</returns>
	public async Task<ConnectivityDiagnosis> RunAsync(ConnectivityAudience audience, ConnectivityIssue issue, CancellationToken cancellationToken = default)
	{
		ConnectivityFacts facts = await MeasureAsync(audience, cancellationToken).ConfigureAwait(false);
		return ConnectivityCheck.Evaluate(facts, issue);
	}

	/// <summary>The one line that names what the game measured, from the last reading.</summary>
	/// <param name="audience">Which side of the connection is being explained.</param>
	/// <param name="cancellationToken">Cancels the reading when the dialog closes.</param>
	/// <returns>The sentence and the value it states.</returns>
	public async Task<ConnectivityLikelyCause> DescribeLikelyCauseAsync(ConnectivityAudience audience, CancellationToken cancellationToken = default)
	{
		ConnectivityFacts facts = await MeasureAsync(audience, cancellationToken).ConfigureAwait(false);
		return ConnectivityCheck.DescribeLikelyCause(facts);
	}

	/// <summary>Forgets the last reading, so the next check asks this machine again.</summary>
	public void DiscardReading()
	{
		_readingTakenMilliseconds = -ReadingLifetimeMilliseconds;
	}

	/// <summary>
	///     Whether this machine can reach the lobby server at all. The lobby list is the smallest thing a node serves that
	///     answers without anything about a lobby, so it is what the question is asked of: a node that cannot answer this is
	///     a node this network cannot reach, which is the difference between a restrictive network and a lobby that failed.
	/// </summary>
	/// <param name="registryServerUrl">The lobby server the game is pointed at.</param>
	/// <param name="cancellationToken">Cancels the question.</param>
	/// <returns>Whether the node answered.</returns>
	private static async Task<bool> AskLobbyServerAsync(string registryServerUrl, CancellationToken cancellationToken)
	{
		if (string.IsNullOrWhiteSpace(registryServerUrl)) return false;

		try
		{
			using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
			timeout.CancelAfter(LobbyServerProbeTimeoutMilliseconds);
			using HttpResponseMessage response = await LobbyServerProbeClient.GetAsync($"{registryServerUrl.TrimEnd('/')}/lobbies", timeout.Token).ConfigureAwait(false);
			return response.IsSuccessStatusCode || (int)response.StatusCode < 500;
		}
		catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException or OperationCanceledException or NotSupportedException or UriFormatException)
		{
			GameLog.LogToConsole($"[Connectivity] The lobby server '{registryServerUrl}' could not be asked whether it answers this machine: {exception.GetType().Name}");
			return false;
		}
	}

	/// <summary>
	///     Whether something other than this game holds the port a lobby is normally listed at. Two things say so, and the
	///     first is the stronger: a lobby that is listening on any other port already states that the named port was refused,
	///     and a machine that is not hosting at all is asked by trying to hold the port itself. The game's own socket is never
	///     counted against it.
	/// </summary>
	/// <param name="lobby">The lobby this screen holds, when it holds one.</param>
	/// <param name="preferredPort">The port a lobby is normally listed at.</param>
	/// <param name="measuredHeld">Whether the evidence says a lobby of this screen already took a port other than that one.</param>
	/// <returns>Whether another program holds that port.</returns>
	private static bool IsPreferredPortHeldByAnotherProgram(LobbyManager? lobby, int preferredPort, bool measuredHeld)
	{
		if (preferredPort <= 0) return false;

		// A lobby that was given up keeps stating this: it asked for the named port first and took the system's choice only
		// after that was refused, so a lobby on any other port is a lobby whose port another program was holding then.
		if (lobby == null) return measuredHeld;
		if (lobby is { LobbyPort: > 0 } && lobby.LobbyPort != preferredPort) return true;
		if (lobby != null && lobby.BoundPort == preferredPort) return false;

		try
		{
			using var socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
			socket.ExclusiveAddressUse = true;
			socket.Bind(new IPEndPoint(IPAddress.Any, preferredPort));
			return false;
		}
		catch (SocketException exception) when (exception.SocketErrorCode == SocketError.AddressAlreadyInUse)
		{
			return true;
		}
		catch (SocketException)
		{
			return false;
		}
	}
}
