using System.Net;
using System.Net.Http.Json;
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
	///     the registry's probes.
	/// </summary>
	public async Task<HostReachabilityResult> VerifyAsync(string registryBaseUrl, string advertisedIp, int advertisedPort, Func<string, int, bool> punch, CancellationToken cancellationToken = default)
	{
		ReachabilityTicket? ticket = await BeginAsync(registryBaseUrl, advertisedIp, advertisedPort, cancellationToken).ConfigureAwait(false);
		if (ticket == null) return HostReachabilityResult.Refused(HostReachabilityFailure.RegistryUnreachable, $"the registry node '{registryBaseUrl}' did not start a reachability check");

		string probeAddress = await ResolveProbeAddressAsync(registryBaseUrl, ticket, cancellationToken).ConfigureAwait(false);
		if (string.IsNullOrEmpty(probeAddress))
		{
			return HostReachabilityResult.Refused(HostReachabilityFailure.ProbeSocketUnknown, $"the registry node '{registryBaseUrl}' did not state the address its checks come from");
		}

		_log($"rehearsing a join to {advertisedIp}:{advertisedPort} through the registry node's probe socket {probeAddress}:{ticket.ProbePort}");

		long beginMilliseconds = Environment.TickCount64;
		long deadlineMilliseconds = beginMilliseconds + Math.Max(ticket.WindowMilliseconds, 0) + DeadlineGraceMilliseconds;
		ReachabilityReport? lastReport = null;
		bool lastPunchAccepted = false;

		while (Environment.TickCount64 < deadlineMilliseconds && !cancellationToken.IsCancellationRequested)
		{
			lastPunchAccepted |= punch(probeAddress, ticket.ProbePort);
			lastReport = await ReadAsync(registryBaseUrl, ticket.ProbeId, cancellationToken).ConfigureAwait(false);
			if (lastReport is { Answered: true }) return Verified(ticket.ProbeId, lastReport, advertisedIp, advertisedPort);

			await Task.Delay(PollIntervalMilliseconds, cancellationToken).ConfigureAwait(false);
		}

		string detail = DescribeSilence(ticket, probeAddress, advertisedIp, advertisedPort, lastReport, lastPunchAccepted);
		return HostReachabilityResult.Refused(HostReachabilityFailure.ProbeUnanswered, detail);
	}

	private static HostReachabilityResult Verified(string probeId, ReachabilityReport report, string advertisedIp, int advertisedPort)
	{
		string observedIp = string.IsNullOrEmpty(report.ObservedIp) ? advertisedIp : report.ObservedIp;
		int observedPort = report.ObservedPort > 0 ? report.ObservedPort : advertisedPort;
		return new HostReachabilityResult(HostReachabilityFailure.None, probeId, observedIp, observedPort, $"the registry node reached the game port and measured the public endpoint {observedIp}:{observedPort}");
	}

	private static string DescribeSilence(ReachabilityTicket ticket, string probeAddress, string advertisedIp, int advertisedPort, ReachabilityReport? lastReport, bool punchAccepted)
	{
		int attempts = lastReport?.Attempts ?? 0;
		string state = lastReport?.State ?? "no answer";
		return $"none of {attempts} probes from {probeAddress}:{ticket.ProbePort} to {advertisedIp}:{advertisedPort} came back (state '{state}', punch accepted: {punchAccepted})";
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

	private static async Task<string> ResolveProbeAddressAsync(string registryBaseUrl, ReachabilityTicket ticket, CancellationToken cancellationToken)
	{
		if (!string.IsNullOrEmpty(ticket.ProbeIp) && IPAddress.TryParse(ticket.ProbeIp, out IPAddress? statedAddress)) return statedAddress.ToString();
		if (!Uri.TryCreate(registryBaseUrl, UriKind.Absolute, out Uri? registryUri)) return string.Empty;

		try
		{
			IPAddress[] addresses = await Dns.GetHostAddressesAsync(registryUri.Host, cancellationToken).ConfigureAwait(false);
			foreach (IPAddress address in addresses)
			{
				if (address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork) return address.ToString();
			}

			return addresses.Length > 0 ? addresses[0].ToString() : string.Empty;
		}
		catch (Exception exception) when (exception is System.Net.Sockets.SocketException or ArgumentException)
		{
			return string.Empty;
		}
	}

	private static string Combine(string baseUrl, string route) => $"{baseUrl.TrimEnd('/')}{route}";
}
