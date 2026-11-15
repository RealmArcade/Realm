using System.Net;
using System.Net.Http.Json;
using System.Net.Sockets;
using System.Text.Json;

namespace Realm.Shared.Reachability;

/// <summary>Why a lobby could not be shown to be reachable from outside the host's own network.</summary>
public enum HostReachabilityFailure
{
	/// <summary>The lobby can be joined: this machine reached its own game port, and a registry node reached it from outside.</summary>
	None,

	/// <summary>This screen's own seat is not held over loopback, so a broken local path could stand between the host and its player.</summary>
	LoopbackNotOnLocalAddress,

	/// <summary>The authority never answered this screen's own client, so the host could not control its own player.</summary>
	LoopbackUnanswered,

	/// <summary>The registry node itself could not be talked to, so nothing could be checked.</summary>
	RegistryUnreachable,

	/// <summary>The registry node could not be reached over UDP, so the game port could not be checked.</summary>
	ProbeSocketUnknown,

	/// <summary>
	///     The registry node has no socket in the family this endpoint is reached in, so the endpoint was never checked.
	///     It is stated apart from <see cref="ProbeUnanswered" /> because nothing is wrong with this machine's network: the
	///     node in front of it cannot measure this kind of endpoint at all.
	/// </summary>
	ProbeFamilyUnavailable,

	/// <summary>The registry node's probes never reached the game port.</summary>
	ProbeUnanswered
}

/// <summary>
///     The outcome of rehearsing a join against a registry node. A verified lobby states the endpoint the registry measured,
///     which is the address a peer on the public internet must use; a refused one states what could not be shown.
/// </summary>
public record HostReachabilityResult(HostReachabilityFailure Failure, string ProbeId, string PublicIp, int PublicPort, string Detail)
{
	public bool Verified => Failure == HostReachabilityFailure.None;

	/// <summary>
	///     The endpoint the rehearsal punched, which is the endpoint the host's router made a mapping toward. A router keeps
	///     that mapping only while it sees traffic toward the endpoint it was made for, so this is what a published lobby has
	///     to keep sending to for as long as it advertises the port the mapping exposes.
	/// </summary>
	public string ProbeIp { get; init; } = string.Empty;

	/// <summary>The port the rehearsal punched, or zero when nothing was punched.</summary>
	public int ProbePort { get; init; }

	/// <summary>
	///     The family the verified endpoint is reached over, as the lobby protocol names it: what the endpoint list a lobby
	///     is published with states, and what decides whether a joining player needs a hole punch.
	/// </summary>
	public string Family { get; init; } = string.Empty;

	public static HostReachabilityResult Refused(HostReachabilityFailure failure, string detail) => new(failure, string.Empty, string.Empty, 0, detail);
}

/// <summary>
///     Rehearses the join a lobby is about to offer, before the lobby is registered and therefore before any other player
///     can see it. The registry node plays the part of the joining peer from its own socket: it sends the opening packet of
///     an ENet connection attempt to the host's game port and the host's authority punches that socket, so a game port
///     behind a router that drops unsolicited traffic is checked through the same route a real peer uses. What comes back is
///     the endpoint the registry measured, which replaces anything the host guessed about its own public address.
/// </summary>
public class HostReachabilityVerifier
{
	private const int PollIntervalMilliseconds = 200;

	private const int BeginTimeoutMilliseconds = 8000;

	private const int ReportTimeoutMilliseconds = 4000;

	private const int RequestedWindowMilliseconds = UdpReachabilityProbeService.DefaultWindowMilliseconds;

	private const int DeadlineGraceMilliseconds = 2000;

	private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

	private readonly HttpClient _httpClient;
	private readonly Action<string> _log;

	public HostReachabilityVerifier(HttpClient httpClient, Action<string> log)
	{
		_httpClient = httpClient;
		_log = log;
	}

	/// <summary>
	///     Runs the rehearsal. <paramref name="punch" /> is how the host's authority is asked to send a datagram from the game
	///     port to an address, which is what an ENet peer performs before it connects and what opens a restricted router for
	///     the registry's probes. It is handed the name of the check it is punching for, which the datagram states so that a
	///     host sharing a public address with another host cannot move that neighbour's check.
	/// </summary>
	public async Task<HostReachabilityResult> VerifyAsync(string registryBaseUrl, string advertisedIp, int advertisedPort, Func<string, int, string, bool> punch, CancellationToken cancellationToken = default)
	{
		// An address the host could not state is the host that cannot measure its own public endpoint, which is exactly what
		// asking a registry node is for: the check is made in whatever family the node answers in, and the family is read from
		// the answer. Only an address the host did state is held to its own family.
		LobbyAddressFamily advertisedFamily = LobbyAddresses.FamilyOf(advertisedIp);

		ReachabilityTicket? ticket = await BeginAsync(registryBaseUrl, advertisedIp, advertisedPort, cancellationToken).ConfigureAwait(false);
		if (ticket == null) return HostReachabilityResult.Refused(HostReachabilityFailure.RegistryUnreachable, $"the registry node '{registryBaseUrl}' did not start a reachability check");

		string probeAddress = await ResolveProbeAddressAsync(registryBaseUrl, ticket, advertisedFamily, cancellationToken).ConfigureAwait(false);

		// A probe in one family can never measure an endpoint in another: a datagram does not cross families, and the
		// answer it produced would describe a socket none of this lobby's players can reach.
		if (probeAddress.Length == 0 || (advertisedFamily != LobbyAddressFamily.Unknown && LobbyAddresses.FamilyOf(probeAddress) != advertisedFamily))
		{
			return HostReachabilityResult.Refused(
				HostReachabilityFailure.ProbeFamilyUnavailable,
				$"the registry node '{registryBaseUrl}' checks lobbies from an address that is not the family {advertisedIp} is reached over");
		}

		_log($"rehearsing a join to {LobbyAddresses.Describe(advertisedIp, advertisedPort)} through the registry node's probe socket {LobbyAddresses.Describe(probeAddress, ticket.ProbePort)}");

		long beginMilliseconds = Environment.TickCount64;
		long deadlineMilliseconds = beginMilliseconds + Math.Max(ticket.WindowMilliseconds, 0) + DeadlineGraceMilliseconds;
		ReachabilityReport? lastReport = null;
		bool lastPunchAccepted = false;

		while (Environment.TickCount64 < deadlineMilliseconds && !cancellationToken.IsCancellationRequested)
		{
			// The punch is offered for every endpoint, including an IPv6 one: it costs one datagram, and a host that
			// reaches the node through a translation somewhere is exactly the host it exists for. A node that needs no
			// punch for an endpoint ignores it, and an IPv6 endpoint answers whether or not it was punched.
			lastPunchAccepted |= punch(probeAddress, ticket.ProbePort, ticket.ProbeId);
			lastReport = await ReadAsync(registryBaseUrl, ticket.ProbeId, cancellationToken).ConfigureAwait(false);
			if (lastReport is { Answered: true }) return Verified(ticket.ProbeId, lastReport, advertisedIp, advertisedPort, advertisedFamily, probeAddress, ticket.ProbePort);

			await Task.Delay(PollIntervalMilliseconds, cancellationToken).ConfigureAwait(false);
		}

		string detail = DescribeSilence(ticket, probeAddress, advertisedIp, advertisedPort, lastReport, lastPunchAccepted);
		return HostReachabilityResult.Refused(HostReachabilityFailure.ProbeUnanswered, detail);
	}

	private static HostReachabilityResult Verified(string probeId, ReachabilityReport report, string advertisedIp, int advertisedPort, LobbyAddressFamily family, string probeAddress, int probePort)
	{
		string observedIp = string.IsNullOrEmpty(report.ObservedIp) ? advertisedIp : report.ObservedIp;
		int observedPort = report.ObservedPort > 0 ? report.ObservedPort : advertisedPort;
		LobbyAddressFamily measuredFamily = family == LobbyAddressFamily.Unknown ? LobbyAddresses.FamilyOf(observedIp) : family;
		return new HostReachabilityResult(HostReachabilityFailure.None, probeId, observedIp, observedPort, $"the registry node reached the game port and measured the public endpoint {LobbyAddresses.Describe(observedIp, observedPort)}")
		{
			ProbeIp = probeAddress,
			ProbePort = probePort,
			Family = LobbyAddresses.FamilyName(measuredFamily)
		};
	}

	private static string DescribeSilence(ReachabilityTicket ticket, string probeAddress, string advertisedIp, int advertisedPort, ReachabilityReport? lastReport, bool punchAccepted)
	{
		int attempts = lastReport?.Attempts ?? 0;
		string state = lastReport?.State ?? "no answer";
		return $"none of {attempts} probes from {LobbyAddresses.Describe(probeAddress, ticket.ProbePort)} to {LobbyAddresses.Describe(advertisedIp, advertisedPort)} came back (state '{state}', punch accepted: {punchAccepted})";
	}

	private async Task<ReachabilityTicket?> BeginAsync(string registryBaseUrl, string advertisedIp, int advertisedPort, CancellationToken cancellationToken)
	{
		try
		{
			using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
			timeout.CancelAfter(BeginTimeoutMilliseconds);

			var request = new ReachabilityBeginRequest(advertisedIp ?? string.Empty, advertisedPort, RequestedWindowMilliseconds);
			using HttpResponseMessage response = await _httpClient.PostAsJsonAsync(Combine(registryBaseUrl, ReachabilityRoutes.Begin), request, JsonOptions, timeout.Token).ConfigureAwait(false);
			if (!response.IsSuccessStatusCode)
			{
				_log($"the registry node refused to start a reachability check: {(int)response.StatusCode} {response.ReasonPhrase}");
				return null;
			}

			return await response.Content.ReadFromJsonAsync<ReachabilityTicket>(JsonOptions, timeout.Token).ConfigureAwait(false);
		}
		catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException or JsonException or NotSupportedException)
		{
			_log($"the registry node could not be asked to check this lobby's endpoint: {exception.GetType().Name}: {exception.Message}");
			return null;
		}
	}

	private async Task<ReachabilityReport?> ReadAsync(string registryBaseUrl, string probeId, CancellationToken cancellationToken)
	{
		try
		{
			using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
			timeout.CancelAfter(ReportTimeoutMilliseconds);

			string url = Combine(registryBaseUrl, $"{ReachabilityRoutes.Report}/{probeId}");
			using HttpResponseMessage response = await _httpClient.GetAsync(url, timeout.Token).ConfigureAwait(false);
			if (!response.IsSuccessStatusCode) return null;

			return await response.Content.ReadFromJsonAsync<ReachabilityReport>(JsonOptions, timeout.Token).ConfigureAwait(false);
		}
		catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException or JsonException or NotSupportedException)
		{
			return null;
		}
	}

	/// <summary>
	///     Works out the address the registry node's checks will arrive from, in the family the endpoint is reached in. The
	///     node states it when it was told what its public address is, which a node behind a name it does not know itself
	///     needs; otherwise this node's name is resolved in the family the check is made in. Resolving it in any other
	///     family would name an address the check can never come from.
	/// </summary>
	private static async Task<string> ResolveProbeAddressAsync(string registryBaseUrl, ReachabilityTicket ticket, LobbyAddressFamily family, CancellationToken cancellationToken)
	{
		if (!string.IsNullOrEmpty(ticket.ProbeIp) && IPAddress.TryParse(ticket.ProbeIp, out IPAddress? statedAddress))
		{
			if (family == LobbyAddressFamily.Unknown || LobbyAddresses.FamilyOf(statedAddress) == family) return statedAddress.ToString();
		}

		if (ticket.Family.Length > 0 && family != LobbyAddressFamily.Unknown && LobbyAddresses.ParseFamilyName(ticket.Family) != family) return string.Empty;
		if (!Uri.TryCreate(registryBaseUrl, UriKind.Absolute, out Uri? registryUri)) return string.Empty;

		try
		{
			IPAddress[] addresses = await Dns.GetHostAddressesAsync(registryUri.Host, cancellationToken).ConfigureAwait(false);

			// An address the host did not state leaves the family to this node's name, which is resolved in the order that
			// reaches an IPv4-only host first and an IPv6-only node at all.
			if (family == LobbyAddressFamily.Unknown)
			{
				foreach (IPAddress candidate in addresses)
				{
					if (candidate.AddressFamily == AddressFamily.InterNetwork) return candidate.ToString();
				}

				return addresses.Length > 0 ? addresses[0].ToString() : string.Empty;
			}

			AddressFamily wanted = family == LobbyAddressFamily.IPv6 ? AddressFamily.InterNetworkV6 : AddressFamily.InterNetwork;
			foreach (IPAddress candidate in addresses)
			{
				if (candidate.AddressFamily == wanted) return candidate.ToString();
			}

			return string.Empty;
		}
		catch (Exception exception) when (exception is SocketException or ArgumentException)
		{
			return string.Empty;
		}
	}

	private static string Combine(string baseUrl, string route) => $"{baseUrl.TrimEnd('/')}{route}";
}
