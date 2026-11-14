using System;
using System.Threading;
using System.Threading.Tasks;
using Realm.Client.Services;
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

	private CancellationTokenSource? _reachabilityKeepAliveStop;

	private HostReachabilityResult? _publishedReachability;

	/// <summary>
	///     Keeps the router mapping the lobby was published at alive until the lobby is torn down. The mapping is made by
	///     the punch the reachability check sends and lasts only while the router keeps seeing traffic from the game port
	///     toward that endpoint, so a lobby that sits open without this is registered at a port the router has already
	///     forgotten, and every player who tries to join it waits out a timeout.
	/// </summary>
	private void StartHostReachabilityKeepAlive()
	{
		StopHostReachabilityKeepAlive();

		HostReachabilityResult? published = _publishedReachability;
		if (published == null || string.IsNullOrEmpty(published.ProbeIp) || published.ProbePort <= 0)
		{
			GameLog.LogToConsole("[Reachability] The endpoint this lobby was published through is unknown, so its router mapping cannot be refreshed; the lobby stays joinable only while the router keeps that mapping by itself.");
			return;
		}

		string probeAddress = published.ProbeIp;
		int probePort = published.ProbePort;
		var stop = new CancellationTokenSource();
		_reachabilityKeepAliveStop = stop;

		GameLog.LogToConsole($"[Reachability] Refreshing this lobby's router mapping through {probeAddress}:{probePort} every {ReachabilityKeepAliveIntervalMilliseconds / 1000} s until the lobby is closed.");

		_ = Task.Run(() => KeepHostReachabilityAliveAsync(probeAddress, probePort, stop.Token));
	}

	/// <summary>
	///     Stops refreshing the mapping, which is what closing the lobby does. The source is cancelled rather than disposed
	///     because the loop may still be inside a delay on this token, and the token outlives the last punch there.
	/// </summary>
	private void StopHostReachabilityKeepAlive()
	{
		CancellationTokenSource? stop = _reachabilityKeepAliveStop;
		_reachabilityKeepAliveStop = null;
		if (stop == null) return;

		stop.Cancel();
		GameLog.LogToConsole("[Reachability] Stopped refreshing this lobby's router mapping.");
	}

	private async Task KeepHostReachabilityAliveAsync(string probeAddress, int probePort, CancellationToken cancellationToken)
	{
		while (!cancellationToken.IsCancellationRequested)
		{
			PunchProbeEndpointAsync(probeAddress, probePort);

			try
			{
				await Task.Delay(ReachabilityKeepAliveIntervalMilliseconds, cancellationToken).ConfigureAwait(false);
			}
			catch (OperationCanceledException)
			{
				return;
			}
		}
	}
}
