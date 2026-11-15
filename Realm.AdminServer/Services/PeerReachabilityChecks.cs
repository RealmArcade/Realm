using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Realm.Shared.Reachability;

namespace Realm.AdminServer.Services;

/// <summary>
///     Asks the other nodes in this deployment to check the same game port, and collects what they saw. A lobby is listed
///     by the node the host registered with, and that node alone can only say what it can reach; a player elsewhere is
///     another network. Every peer that answers is a second destination the host's router had to translate for, which is
///     also the measurement that tells a router which keeps one outside port for every destination from one that makes a
///     port per destination.
/// </summary>
public class PeerReachabilityChecks
{
	private const int AskTimeoutMilliseconds = 1500;

	private const int CollectTimeoutMilliseconds = 1200;

	private const int CollectFloorMilliseconds = 400;

	private const long RecordLifetimeMilliseconds = 120_000;

	private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

	private readonly IHttpClientFactory _httpClientFactory;

	private readonly PeerRegistry _peers;

	private readonly ConcurrentDictionary<string, PeerCheckRecord> _checks = new(StringComparer.Ordinal);

	private readonly Action<string> _log;

	public PeerReachabilityChecks(IHttpClientFactory httpClientFactory, PeerRegistry peers, Action<string> log)
	{
		_httpClientFactory = httpClientFactory;
		_peers = peers;
		_log = log;
	}

	/// <summary>Whether this deployment has another node to ask, which is what makes a second vantage point possible.</summary>
	public bool HasPeers => PeerUrls().Count > 0;

	/// <summary>
	///     Asks every other node in this deployment to check the same game port, and records the checks it started so the
	///     reports can be collected while the host is still reading its own. A peer that cannot be asked is skipped: a
	///     deployment of one reachable node still lists lobbies, judged on that one.
	/// </summary>
	/// <param name="localProbeId">The check this node started, which the peers' checks belong to.</param>
	/// <param name="advertisedIp">The endpoint the lobby offered, as the host stated it.</param>
	/// <param name="advertisedPort">The port the lobby's game transport holds.</param>
	/// <param name="windowMilliseconds">How long the host is willing to be measured for.</param>
	/// <param name="cancellationToken">Ends the fan-out when the request is abandoned.</param>
	/// <returns>The checks the peers started.</returns>
	public async Task<List<ReachabilityPeerCheck>> AskAsync(string localProbeId, string advertisedIp, int advertisedPort, int windowMilliseconds, CancellationToken cancellationToken)
	{
		PruneStale();

		var started = new List<ReachabilityPeerCheck>(4);
		List<string> peers = PeerUrls();
		HttpClient client = _httpClientFactory.CreateClient();

		for (int index = 0; index < peers.Count; index++)
		{
			string peerUrl = peers[index];
			using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
			timeout.CancelAfter(AskTimeoutMilliseconds);

			try
			{
				var request = new ReachabilityBeginRequest(advertisedIp, advertisedPort, windowMilliseconds) { AskPeers = false };
				using HttpResponseMessage response = await client.PostAsJsonAsync($"{peerUrl}{ReachabilityRoutes.Begin}", request, JsonOptions, timeout.Token).ConfigureAwait(false);
				if (!response.IsSuccessStatusCode)
				{
					_log($"{peerUrl} refused to check the lobby: {(int)response.StatusCode}");
					continue;
				}

				ReachabilityTicket? ticket = await response.Content.ReadFromJsonAsync<ReachabilityTicket>(JsonOptions, timeout.Token).ConfigureAwait(false);
				if (ticket == null || string.IsNullOrEmpty(ticket.ProbeId))
				{
					_log($"{peerUrl} answered the request to check the lobby without naming a check");
					continue;
				}

				started.Add(new ReachabilityPeerCheck(peerUrl, ticket.ProbeId, ticket.ProbeIp, ticket.ProbePort));
			}
			catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException or JsonException)
			{
				_log($"{peerUrl} could not be asked to check the lobby: {exception.GetType().Name}");
			}
		}

		if (started.Count > 0) _checks[localProbeId] = new PeerCheckRecord(started, Environment.TickCount64);
		return started;
	}

	/// <summary>
	///     Reads what the peer nodes saw, at most once per collection floor while any of them is still measuring. The host
	///     polls this node's answer while it rehearses its join, so the peer reports are collected once rather than once per
	///     poll, and a peer that reports late is still read on a later poll.
	/// </summary>
	/// <param name="localProbeId">The check this node started.</param>
	/// <param name="cancellationToken">Ends the collection when the request is abandoned.</param>
	/// <returns>Every peer report read so far, which may be fewer than the peers that were asked.</returns>
	public async Task<IReadOnlyList<ReachabilityReport>> CollectAsync(string localProbeId, CancellationToken cancellationToken)
	{
		if (!_checks.TryGetValue(localProbeId, out PeerCheckRecord? record)) return Array.Empty<ReachabilityReport>();
		if (record.Reports != null && Environment.TickCount64 < record.NextCollectMilliseconds) return record.Reports;

		// A peer whose report is still waiting is read again after the floor rather than kept: the first read of a peer's
		// report happens as soon as the host asks, which is before a peer that had to be punched has answered, and a report
		// that is held there would state a silence the peer never measured.

		var reports = new List<ReachabilityReport>(record.Checks.Count);
		HttpClient client = _httpClientFactory.CreateClient();

		for (int index = 0; index < record.Checks.Count; index++)
		{
			ReachabilityPeerCheck check = record.Checks[index];
			using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
			timeout.CancelAfter(CollectTimeoutMilliseconds);

			try
			{
				using HttpResponseMessage response = await client.GetAsync($"{check.NodeUrl}{ReachabilityRoutes.Report}/{check.ProbeId}", timeout.Token).ConfigureAwait(false);
				if (!response.IsSuccessStatusCode) continue;

				ReachabilityReport? report = await response.Content.ReadFromJsonAsync<ReachabilityReport>(JsonOptions, timeout.Token).ConfigureAwait(false);
				if (report != null) reports.Add(report);
			}
			catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException or JsonException)
			{
				_log($"{check.NodeUrl} could not report what it measured: {exception.GetType().Name}");
			}
		}

		record.Reports = reports;
		record.NextCollectMilliseconds = Environment.TickCount64 + CollectFloorMilliseconds;
		return reports;
	}

	private List<string> PeerUrls()
	{
		var peers = new List<string>(_peers.PeerUrls.Count);
		string self = (_peers.SelfUrl ?? string.Empty).TrimEnd('/');

		for (int index = 0; index < _peers.PeerUrls.Count; index++)
		{
			string peer = (_peers.PeerUrls[index] ?? string.Empty).TrimEnd('/');
			if (peer.Length == 0) continue;
			if (self.Length > 0 && string.Equals(peer, self, StringComparison.OrdinalIgnoreCase)) continue;
			if (!peers.Contains(peer, StringComparer.OrdinalIgnoreCase)) peers.Add(peer);
		}

		return peers;
	}

	private void PruneStale()
	{
		long now = Environment.TickCount64;
		foreach (KeyValuePair<string, PeerCheckRecord> entry in _checks)
		{
			if (now - entry.Value.StartedMilliseconds < RecordLifetimeMilliseconds) continue;
			_checks.TryRemove(entry.Key, out _);
		}
	}

	private sealed class PeerCheckRecord
	{
		public PeerCheckRecord(List<ReachabilityPeerCheck> checks, long startedMilliseconds)
		{
			Checks = checks;
			StartedMilliseconds = startedMilliseconds;
			NextCollectMilliseconds = startedMilliseconds;
		}

		public List<ReachabilityPeerCheck> Checks { get; }

		public long StartedMilliseconds { get; }

		public List<ReachabilityReport>? Reports { get; set; }

		public long NextCollectMilliseconds { get; set; }
	}
}
