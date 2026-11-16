using System;
using System.Threading;
using System.Threading.Tasks;
using Realm.Client.Services;
using Realm.Shared.Network;
using Realm.Shared.Reachability;

namespace Realm.Client.Network;

public partial class LobbyManager
{
	/// <summary>
	///     How often the published lobby's router mapping is refreshed. A router forgets a UDP mapping it has seen no traffic
	///     for, and how long that takes is the router's own business: thirty seconds is common and a couple of minutes is
	///     the longest a router that follows the recommended minimum allows. The mapping is refreshed well inside the
	///     shortest of those rather than at the edge of one.
	/// </summary>
	private const int ReachabilityKeepAliveIntervalMilliseconds = 20_000;

	/// <summary>
	///     How often the lobby is measured again while it is listed. Refreshing the mapping only keeps the router's memory of
	///     a port alive; it says nothing about whether the port still answers, so a lobby whose router stopped forwarding, or
	///     whose own seat stopped answering over loopback, stays listed until a player waits out a join. This is the interval
	///     the class is re-stated on, and it is well inside the minute the class promises to be true for.
	/// </summary>
	private const int ReachabilityReverifyIntervalMilliseconds = 45_000;

	/// <summary>How many times the listed lobby has been measured again, and what the latest re-check found, for the diagnostics block.</summary>
	private int _reachabilityReverifyCount;

	private string _lastReverifyOutcome = "not run";

	private CancellationTokenSource? _reachabilityKeepAliveStop;

	private HostReachabilityResult? _publishedReachability;

	/// <summary>
	///     Keeps a listed lobby's claim true for as long as it is listed. Two things are needed and they are different halves
	///     of the same promise: the router mapping the lobby was published at has to be refreshed, or the router forgets it,
	///     and the lobby has to be measured again, or a lobby that stopped being reachable keeps saying it is. A downgrade is
	///     the only change either half may make: a fresh check that answered better than the listed class leaves the listing
	///     alone, because a lobby is not promoted by a measurement that arrives after players were told what to expect.
	/// </summary>
	private void StartHostReachabilityKeepAlive()
	{
		StopHostReachabilityKeepAlive();

		// Only an IPv4 endpoint has a router mapping behind it. An IPv6 endpoint is the address this machine holds, so
		// there is no translation a router could forget and nothing to refresh. Both are re-measured, because a lobby listed
		// over IPv6 can still stop answering.
		bool usesIpv4 = _publishedEndpoints.Exists(endpoint => endpoint.FamilyValue == LobbyAddressFamily.IPv4);

		HostReachabilityResult? published = _publishedReachability;
		if (usesIpv4 && (published == null || string.IsNullOrEmpty(published.ProbeIp) || published.ProbePort <= 0))
		{
			GameLog.LogToConsole("[Reachability] The endpoint this lobby was published through is unknown, so its router mapping cannot be refreshed; the lobby stays joinable only while the router keeps that mapping by itself.");
		}

		string probeAddress = published?.ProbeIp ?? string.Empty;
		int probePort = published?.ProbePort ?? 0;
		var stop = new CancellationTokenSource();
		_reachabilityKeepAliveStop = stop;

		if (usesIpv4 && probePort > 0)
		{
			GameLog.LogToConsole($"[Reachability] Refreshing this lobby's router mapping through {probeAddress}:{probePort} every {ReachabilityKeepAliveIntervalMilliseconds / 1000} s, and measuring the lobby again every {ReachabilityReverifyIntervalMilliseconds / 1000} s, until the lobby is closed.");
		}
		else
		{
			GameLog.LogToConsole($"[Reachability] Measuring this lobby again every {ReachabilityReverifyIntervalMilliseconds / 1000} s until it is closed.");
		}

		_ = Task.Run(() => KeepHostReachabilityAliveAsync(probeAddress, probePort, stop.Token));
	}

	/// <summary>
	///     Stops refreshing the mapping, which is what closing the lobby does. The source is cancelled rather than disposed
	///     because the loop may still be inside a delay on that token, and the token outlives the last punch there.
	/// </summary>
	private void StopHostReachabilityKeepAlive()
	{
		CancellationTokenSource? stop = _reachabilityKeepAliveStop;
		_reachabilityKeepAliveStop = null;
		if (stop == null) return;

		stop.Cancel();
		GameLog.LogToConsole("[Reachability] Stopped refreshing this lobby.");
	}

	private async Task KeepHostReachabilityAliveAsync(string probeAddress, int probePort, CancellationToken cancellationToken)
	{
		long nextReverifyMilliseconds = Environment.TickCount64 + ReachabilityReverifyIntervalMilliseconds;

		while (!cancellationToken.IsCancellationRequested)
		{
			// The check this lobby was verified with has already been read and forgotten, so this punch names none: it exists
			// only for the endpoint it leaves from, which is what keeps the router's mapping for the game port alive.
			if (probePort > 0) PunchProbeEndpointAsync(probeAddress, probePort, string.Empty);

			// The wake-up is the mapping refresh, or the moment the lobby is due to be measured again, whichever comes
			// first: a lobby only refreshed on the mapping's cadence would be judged on the first refresh after it fell due,
			// which is up to one whole refresh later than the interval it promises. A lobby with no mapping to refresh has
			// nothing to do between checks and waits for the check.
			long now = Environment.TickCount64;
			long remaining = nextReverifyMilliseconds - now;
			int delay = remaining <= 0 ? 0 : (int)Math.Min(probePort > 0 ? ReachabilityKeepAliveIntervalMilliseconds : remaining, remaining);

			if (delay > 0)
			{
				try
				{
					await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
				}
				catch (OperationCanceledException)
				{
					return;
				}
			}

			if (Environment.TickCount64 < nextReverifyMilliseconds) continue;
			nextReverifyMilliseconds = Environment.TickCount64 + ReachabilityReverifyIntervalMilliseconds;

			try
			{
				await ReverifyPublishedLobbyAsync(cancellationToken).ConfigureAwait(false);
			}
			catch (OperationCanceledException)
			{
				return;
			}
			catch (Exception exception)
			{
				// A re-check that could not run is not evidence about the lobby, so nothing is claimed and nothing is
				// withdrawn: the next cycle tries again, and the listing keeps the class the last real measurement gave it.
				_lastReverifyOutcome = $"the re-check could not run ({exception.GetType().Name})";
				GameLog.LogError($"[Reachability] This lobby could not be measured again: {exception.Message}");
			}
		}
	}

	/// <summary>
	///     Measures the lobby again with the same registry node that listed it, and withdraws the class the listing states
	///     when the fresh measurement claims less. The two failures that matter are named apart: an endpoint that no longer
	///     answers, and a lobby this screen's own client can no longer reach, because the second one is this machine and not
	///     the network between it and everybody else.
	/// </summary>
	/// <param name="cancellationToken">Ends the re-check when the lobby is closed.</param>
	private async Task ReverifyPublishedLobbyAsync(CancellationToken cancellationToken)
	{
		if (!IsHost || string.IsNullOrEmpty(ReachabilityClass) || LobbyPort <= 0) return;

		string registryUrl = RegistryServerUrl;
		if (string.IsNullOrEmpty(registryUrl)) return;

		// A lobby carried through volunteers claims nothing about this machine's own port, so measuring that port again
		// would withdraw a lobby that is working. What its listing rests on is the volunteers the server pools, and that is
		// what is re-read here: when none is left, the lobby really has no way in and is withdrawn like any other.
		if (ReachabilityClasses.IsRelayed(ReachabilityClass))
		{
			int pooled = await CountVolunteerCandidatesAsync(registryUrl).ConfigureAwait(false);
			Interlocked.Increment(ref _reachabilityReverifyCount);
			_lastReverifyOutcome = pooled > 0 ? $"carried through volunteers ({pooled} pooled)" : "no volunteer is pooled any more";

			if (pooled == 0)
			{
				WithdrawReachability(ReachabilityClasses.Unverified, default, "the lobby server no longer pools a volunteer this lobby can be carried through");
				return;
			}

			GameLog.LogToConsole($"[Reachability] This lobby is still carried through volunteers after {Volatile.Read(ref _reachabilityReverifyCount)} re-check(s) ({_lastReverifyOutcome}).");
			return;
		}

		RegistryCapabilities capabilities = await ReadRegistryCapabilitiesAsync(registryUrl, cancellationToken).ConfigureAwait(false);
		LobbyReachabilitySet set = await VerifyPublishedEndpointsAsync(registryUrl, LobbyPort, capabilities, cancellationToken, reportStage: false).ConfigureAwait(false);
		Interlocked.Increment(ref _reachabilityReverifyCount);
		GameLog.LogToConsole($"[Reachability] Measuring this lobby again (re-check {Volatile.Read(ref _reachabilityReverifyCount)}): {set.Verified.Count} of {_offeredEndpoints.Length} endpoint(s) answered, listing says {ReachabilityClass}.");

		// The class of the set is the class of the strongest endpoint that answered, which for a lobby published over two
		// families is the IPv4 one: an IPv6 endpoint is reached as it was stated and carries no cabling of its own.
		string measured = set.Verified.Count == 0
			? ReachabilityClasses.Unverified
			: _publishedReachability?.ReachabilityClass ?? ReachabilityClasses.Open;

		// The session this screen plays its own seat over is the only admission a host can prove without a second player, and
		// it is the half of the promise the network check cannot see: a lobby that answers the internet and not its own
		// client is not a lobby anybody can play in.
		if (set.Verified.Count > 0 && _lobbySession is { } session)
		{
			HostReachabilityResult seat = await VerifyLobbyLoopbackAsync(session).ConfigureAwait(false);
			if (!seat.Verified) measured = ReachabilityClasses.Unverified;
		}

		_lastReverifyOutcome = set.Verified.Count == 0
			? $"no endpoint answered ({set.FirstFailure?.Failure ?? HostReachabilityFailure.ProbeUnanswered})"
			: $"{measured} at {DescribeVerifiedEndpoints(set)}";

		if (ReachabilityClasses.IsWeaker(measured, ReachabilityClass))
		{
			WithdrawReachability(measured, set);
			return;
		}

		// A lobby that is still reachable at a different outside port than the one it is listed at is a lobby nobody can
		// join, because every player is dialling the port the router no longer exposes. Nothing is re-registered behind the
		// host's back: the listing is withdrawn and the host is told to publish again, which is the only thing that states a
		// new endpoint to the node.
		if (set.Verified.Count > 0 && _publishedEndpoints.Count > 0 && set.Verified[0].DialPort != _publishedEndpoints[0].DialPort)
		{
			WithdrawReachability(ReachabilityClasses.Unverified, set, $"the router now exposes this lobby's game port at {set.Verified[0].Describe()} instead of {_publishedEndpoints[0].Describe()}, so every player is dialling a port it no longer exposes");
			return;
		}

		GameLog.LogToConsole($"[Reachability] This lobby is still {measured} after {Volatile.Read(ref _reachabilityReverifyCount)} re-check(s) ({_lastReverifyOutcome}).");
	}

	/// <summary>
	///     Withdraws the class a lobby is listed with, tells the lobby server so the listing stops claiming it, and tells the
	///     host in the lobby's own chat where it can act on it. Only downward: the class never goes back up, because the
	///     players who were told what to expect are not told again when a later check happens to answer.
	/// </summary>
	/// <param name="measured">The weaker class the fresh measurement produced.</param>
	/// <param name="set">What the re-check produced, for the sentence and the log.</param>
	/// <param name="reason">What happened, or empty to describe the measurement's own failure.</param>
	private void WithdrawReachability(string measured, LobbyReachabilitySet set, string reason = "")
	{
		string previous = ReachabilityClass;
		if (reason.Length == 0) reason = DescribeWithdrawalReason(set);

		ReachabilityClass = measured;
		if (_publishedReachability != null) _publishedReachability = _publishedReachability with { ReachabilityClass = measured };

		string sentence = string.Format(
			Tr("This lobby is no longer listed as {0} but as {1}: {2}. Host again after fixing it, or share the direct address in the lobby room."),
			previous,
			measured,
			reason);

		LastLobbyWarning = sentence;
		GameLog.LogError($"[Reachability] {sentence}");

		_lobbySession?.SendLobbyCommand(new LobbyCommand
		{
			Kind = LobbyCommandKind.ReportReachability,
			Text = sentence
		});
	}

	/// <summary>
	///     Says what a re-check that withdrew a class found, in the terms the host can act on: the network between this
	///     machine and the lobby server, this machine's own router, or this machine itself.
	/// </summary>
	/// <param name="set">What the re-check produced.</param>
	/// <returns>One sentence naming the failure.</returns>
	private string DescribeWithdrawalReason(LobbyReachabilitySet set)
	{
		HostReachabilityFailure failure = set.FirstFailure?.Failure ?? HostReachabilityFailure.ProbeUnanswered;
		return failure switch
		{
			HostReachabilityFailure.MappingPerDestination => Tr("this router gives every destination a different outside port, which a punch cannot open"),
			HostReachabilityFailure.MappingUnmeasured => Tr("this router's outside port could no longer be shown to be the same for every player"),
			HostReachabilityFailure.ProbeFromOwnNetwork => Tr("the lobby server reaches this computer from inside its own network and measures nothing about the players outside it"),
			HostReachabilityFailure.RegistryUnreachable or HostReachabilityFailure.ProbeSocketUnknown => Tr("the lobby server could not be reached, so nothing about this lobby could be measured"),
			HostReachabilityFailure.ProbeFamilyUnavailable => Tr("the lobby server cannot check this lobby's endpoint family"),
			HostReachabilityFailure.LoopbackUnanswered => Tr("this computer's own client stopped reaching the lobby"),
			HostReachabilityFailure.LoopbackNotOnLocalAddress => Tr("this computer's own seat is not held over the loopback address"),
			_ => Tr("this lobby's game port stopped answering the lobby server")
		};
	}
}
