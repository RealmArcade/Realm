using System.Collections.Concurrent;
using Realm.AdminServer.Models;
using Realm.Shared.Network.Relay;
using Realm.Shared.Reachability;

namespace Realm.AdminServer.Services;

/// <summary>
///     The pool of volunteers a node offers to hosts and joining players. Membership is measured, bounded and expiring: a
///     volunteer enters it only when its own endpoint was measured as open, leaves it when it misses its heartbeats or stops
///     offering, and is never offered once it has no room left. An empty pool is not a failure: every hosting and joining
///     path carries on exactly as it did before relays existed.
/// </summary>
public class RelayPool
{
	/// <summary>How long a volunteer stays pooled without a heartbeat.</summary>
	public const int MembershipSeconds = 30;

	/// <summary>How many candidates one join is offered, which is also how many paths a connection may open at once.</summary>
	public const int CandidatesPerConnection = RelayOffers.MaximumOffers;

	/// <summary>The most volunteers one node pools, which bounds the work a burst of registrations can cause it.</summary>
	public const int MaximumPoolSize = 512;

	private readonly ConcurrentDictionary<string, RelayCandidateMessage> _candidates = new(StringComparer.Ordinal);
	private readonly ConcurrentDictionary<string, DateTime> _lastSeen = new(StringComparer.Ordinal);

	/// <summary>
	///     Offers a volunteer a place in the pool. It is kept only when the endpoint it states was measured as open and it
	///     has room to carry anything at all; every refusal is a stated reason rather than a silent absence, because the
	///     volunteer's own screen shows it.
	/// </summary>
	/// <param name="request">What the volunteer stated.</param>
	/// <param name="statedAddress">The address the request arrived from, used when the volunteer stated none.</param>
	/// <param name="measuredReachability">The class this node measured the volunteer's endpoint at.</param>
	/// <returns>Whether the volunteer is now pooled, and the sentence its screen shows.</returns>
	public (bool Advertised, string Reason) Offer(RelayRegisterRequest request, string statedAddress, string measuredReachability)
	{
		PruneStale();

		if (string.IsNullOrWhiteSpace(request.RelayId)) return (false, "The volunteer stated no identifier, so nothing could be offered under it.");
		if (request.Port is <= 0 or > 65535) return (false, "The volunteer's forwarding port is not one a player can be sent to.");
		if (request.MaximumConnections <= 0) return (false, "The volunteer stated room for no connections, so it would carry nothing.");
		if (request.BudgetBytesPerSecond <= 0) return (false, "The volunteer stated no bandwidth budget, so there is nothing to enforce.");
		if (request.MaximumPayloadBytes < RelayEnvelope.HeaderBytes) return (false, "The volunteer's stated datagram size is smaller than the path layer's own header.");

		if (!string.Equals(measuredReachability, ReachabilityClasses.Open, StringComparison.OrdinalIgnoreCase))
		{
			return (false, measuredReachability switch
			{
				ReachabilityClasses.PunchOnly => "This node measured the volunteer's endpoint as reachable only after a punch, and a relay has to answer a stranger without one. Nothing was pooled.",
				_ => "This node could not reach the volunteer's endpoint from outside, so it cannot carry anyone else's connection. Nothing was pooled."
			});
		}

		if (_candidates.Count >= MaximumPoolSize && !_candidates.ContainsKey(request.RelayId)) return (false, "This node already pools as many volunteers as it offers.");

		string address = string.IsNullOrWhiteSpace(request.ReportedIP) || request.ReportedIP is "127.0.0.1" or "0.0.0.0" ? statedAddress : request.ReportedIP;
		if (!ReachabilityAddresses.IsPubliclyRoutable(address)) return (false, "The volunteer's stated address is not one a player elsewhere can be sent to, so nothing was pooled.");

		var candidate = new RelayCandidateMessage
		{
			RelayId = request.RelayId,
			Address = address,
			Port = request.Port,
			Carrier = string.IsNullOrWhiteSpace(request.Carrier) ? RelayCarrierKind.UdpTunnel.ToString() : request.Carrier,
			MaximumPayloadBytes = request.MaximumPayloadBytes,
			MaximumConnections = request.MaximumConnections,
			ActiveConnections = 0,
			BudgetBytesPerSecond = request.BudgetBytesPerSecond,
			ReachabilityClass = ReachabilityClasses.Open,
			ObservedLatencyMilliseconds = 0
		};

		_candidates[request.RelayId] = candidate;
		_lastSeen[request.RelayId] = DateTime.UtcNow;
		return (true, string.Empty);
	}

	/// <summary>Records a volunteer's heartbeat and how many connections it holds now.</summary>
	/// <param name="request">What the volunteer stated.</param>
	/// <returns>Whether the volunteer is still pooled.</returns>
	public bool Touch(RelayHeartbeatRequest request)
	{
		if (request.RelayId == null || !_candidates.TryGetValue(request.RelayId, out RelayCandidateMessage? candidate)) return false;

		_lastSeen[request.RelayId] = DateTime.UtcNow;
		_candidates[request.RelayId] = candidate with { ActiveConnections = Math.Max(0, request.ActiveConnections) };
		return true;
	}

	/// <summary>Takes a volunteer out of the pool at once, which is what stopping does.</summary>
	/// <param name="relayId">The volunteer that stopped.</param>
	public void Remove(string relayId)
	{
		_candidates.TryRemove(relayId, out _);
		_lastSeen.TryRemove(relayId, out _);
	}

	/// <summary>
	///     Chooses the volunteers one connection is offered. They are ordered by the room they report, then by identifier, so
	///     two peers told the same answer open the same paths, and a volunteer that is full is not offered at all.
	/// </summary>
	/// <param name="count">The most candidates to offer.</param>
	/// <returns>The candidates, in the order they are offered.</returns>
	public List<RelayCandidateMessage> Select(int count)
	{
		PruneStale();

		var available = new List<RelayCandidateMessage>(_candidates.Count);
		foreach (KeyValuePair<string, RelayCandidateMessage> entry in _candidates)
		{
			RelayCandidateMessage candidate = entry.Value;
			if (candidate.ActiveConnections >= candidate.MaximumConnections) continue;

			available.Add(candidate);
		}

		available.Sort(static (left, right) =>
		{
			int leftRoom = left.MaximumConnections - left.ActiveConnections;
			int rightRoom = right.MaximumConnections - right.ActiveConnections;
			if (leftRoom != rightRoom) return rightRoom.CompareTo(leftRoom);

			return string.CompareOrdinal(left.RelayId, right.RelayId);
		});

		int wanted = Math.Clamp(count, 0, CandidatesPerConnection);
		if (available.Count > wanted) available.RemoveRange(wanted, available.Count - wanted);
		return available;
	}

	/// <summary>How many volunteers this node currently offers.</summary>
	/// <returns>The pooled volunteers that still have room.</returns>
	public int AdvertisedCount() => Select(CandidatesPerConnection).Count;

	private void PruneStale()
	{
		DateTime cutoff = DateTime.UtcNow.AddSeconds(-MembershipSeconds);
		foreach (KeyValuePair<string, DateTime> entry in _lastSeen)
		{
			if (entry.Value >= cutoff) continue;

			Remove(entry.Key);
		}
	}
}
