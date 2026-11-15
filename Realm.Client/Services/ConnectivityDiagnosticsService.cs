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

	private ConnectivityFacts _reading;

	private ConnectivityAudience _readingAudience;

	private long _readingTakenMilliseconds = -ReadingLifetimeMilliseconds;

	/// <summary>
	///     Builds what the lobby manager already knows, without asking this machine anything. The dialog opens on this, so the
	///     measured failure and the one-line likely cause are on screen before any check has run.
	/// </summary>
	/// <param name="audience">Which side of the connection is being explained.</param>
	/// <returns>What the checks may read, with every live reading left unanswered.</returns>
	public ConnectivityFacts DescribeKnownFacts(ConnectivityAudience audience)
	{
		LobbyManager? lobby = GameContext.Lobby;

		return new ConnectivityFacts
		{
			Audience = audience,
			HostFailure = lobby?.LastHostFailure?.Failure ?? HostReachabilityFailure.None,
			JoinPhase = lobby?.LastJoinPhase ?? string.Empty,
			ListedPort = lobby?.LobbyPort ?? 0,
			PreferredPort = LobbyManager.DefaultGamePort,
			ObservedOutsidePort = lobby?.LastHostFailure?.ObservedPort ?? 0,
			MappingBehaviour = lobby?.MappingBehaviour ?? MappingBehaviours.Unknown,
			ReachabilityClass = lobby?.ReachabilityClass ?? ReachabilityClasses.Unverified,
			FirewallGrant = lobby?.LastFirewallGrant.Grant ?? WindowsFirewallGrant.NotApplicable,
			FirewallRule = WindowsFirewallRuleState.Unknown,
			LocalNatMeasured = lobby?.HasLocalNatType ?? false,
			LocalNatIsSymmetric = lobby?.LocalNatType == NatType.Symmetric,
			MeasuredOutsideAddress = lobby?.MeasuredOutsideAddress ?? string.Empty,
			LobbyServerProbed = false,
			LobbyServerAnswered = false,
			TunnelAdapterUp = false,
			PreferredPortHeldElsewhere = false,
			OffersIpv4 = lobby?.OffersIpv4 ?? true
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
			PreferredPortHeldElsewhere = IsPreferredPortHeldByAnotherProgram(lobby, known.PreferredPort)
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
			using var client = new HttpClient { Timeout = Timeout.InfiniteTimeSpan };
			using HttpResponseMessage response = await client.GetAsync($"{registryServerUrl.TrimEnd('/')}/lobbies", timeout.Token).ConfigureAwait(false);
			return response.IsSuccessStatusCode || (int)response.StatusCode < 500;
		}
		catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException or OperationCanceledException or NotSupportedException or UriFormatException)
		{
			GameLog.LogToConsole($"[Connectivity] The lobby server '{registryServerUrl}' could not be asked whether it answers this machine: {exception.GetType().Name}");
			return false;
		}
	}

	/// <summary>
	///     Whether something other than this game holds the port a lobby is normally listed at. A lobby that had to take the
	///     port the system chose is a lobby nobody told where to connect, so the answer is worth reading before any router is
	///     looked at. The game's own socket is not counted against itself.
	/// </summary>
	/// <param name="lobby">The lobby this screen holds, when it holds one.</param>
	/// <param name="preferredPort">The port a lobby is normally listed at.</param>
	/// <returns>Whether another program holds that port.</returns>
	private static bool IsPreferredPortHeldByAnotherProgram(LobbyManager? lobby, int preferredPort)
	{
		if (preferredPort <= 0) return false;
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
