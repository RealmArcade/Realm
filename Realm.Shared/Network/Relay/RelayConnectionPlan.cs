using System;
using System.Collections.Generic;
using System.Text.Json;

namespace Realm.Shared.Network.Relay;

/// <summary>
///     Everything one side needs in order to carry a connection through volunteers: the volunteers both peers were offered,
///     and the join ticket their keys are derived from. It is the same object on both sides of the connection, built from
///     the same answer, so the two peers open identical paths without exchanging anything further.
/// </summary>
/// <param name="Offers">The volunteers to open, in the order the lobby server stated them.</param>
/// <param name="JoinTicket">The ticket the lobby server issued for this join.</param>
/// <param name="DuplicationEnabled">Whether a second path may carry a copy of a datagram the best path is struggling with.</param>
/// <param name="DuplicationBudgetBytesPerSecond">The most duplication this connection may spend.</param>
public sealed record RelayConnectionPlan(
	IReadOnlyList<RelayPathOffer> Offers,
	string JoinTicket,
	bool DuplicationEnabled,
	int DuplicationBudgetBytesPerSecond)
{
	/// <summary>Whether this plan can carry anything at all.</summary>
	public bool IsUsable => Offers is { Count: > 0 } && RelayJoinSecret.IsUsable(JoinTicket);

	/// <summary>Builds the plan both peers derive from one join.</summary>
	/// <param name="joinTicket">The ticket the lobby server issued for this join.</param>
	/// <param name="candidates">The volunteers the server offered, in its own order.</param>
	/// <param name="duplicationEnabled">Whether the connection may duplicate over a second path.</param>
	/// <param name="duplicationBudgetBytesPerSecond">The most duplication it may spend.</param>
	/// <returns>The plan, which is unusable when the server offered nothing.</returns>
	public static RelayConnectionPlan From(string joinTicket, IReadOnlyList<RelayCandidateMessage>? candidates, bool duplicationEnabled, int duplicationBudgetBytesPerSecond)
	{
		return new RelayConnectionPlan(RelayOffers.Build(candidates), joinTicket ?? string.Empty, duplicationEnabled, duplicationBudgetBytesPerSecond);
	}

	/// <summary>States this plan as the lobby command a host's own process sends its authority.</summary>
	/// <returns>The JSON line the command carries.</returns>
	public string Serialize()
	{
		var candidates = new List<RelayCandidateMessage>(Offers.Count);
		for (int index = 0; index < Offers.Count; index++) candidates.Add(Offers[index].Candidate);

		return JsonSerializer.Serialize(new RelayNegotiationMessage
		{
			JoinTicket = JoinTicket,
			DuplicationEnabled = DuplicationEnabled,
			DuplicationBudgetBytesPerSecond = DuplicationBudgetBytesPerSecond,
			Candidates = candidates
		});
	}

	/// <summary>Reads the plan a lobby command carried.</summary>
	/// <param name="json">The JSON line the command carried.</param>
	/// <returns>The plan, or null when the line states none.</returns>
	public static RelayConnectionPlan? TryParse(string? json)
	{
		if (string.IsNullOrWhiteSpace(json)) return null;

		try
		{
			RelayNegotiationMessage? message = JsonSerializer.Deserialize<RelayNegotiationMessage>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
			if (message == null) return null;

			return From(message.JoinTicket, message.Candidates, message.DuplicationEnabled, message.DuplicationBudgetBytesPerSecond);
		}
		catch (JsonException)
		{
			return null;
		}
	}
}

/// <summary>The shape a relayed connection's offer list travels in between the process holding a lobby and its authority.</summary>
public sealed class RelayNegotiationMessage
{
	/// <summary>The ticket the lobby server issued for this join.</summary>
	public string JoinTicket { get; init; } = string.Empty;

	/// <summary>The volunteers both peers open, in the order the server stated them.</summary>
	public List<RelayCandidateMessage> Candidates { get; init; } = [];

	/// <summary>Whether a second path may carry a copy of a datagram the best path is struggling with.</summary>
	public bool DuplicationEnabled { get; init; }

	/// <summary>The most duplication this connection may spend, in bytes per second.</summary>
	public int DuplicationBudgetBytesPerSecond { get; init; }
}

/// <summary>
///     The line the traffic report and the diagnostics page state about one relayed connection, in the shape two hosts'
///     outputs are compared by. It is produced by the path layer and carries no match, map or player content: the relay's
///     identity, the paths, their measured cost and the volumes they carried.
/// </summary>
public static class RelayDiagnostics
{
	/// <summary>
	///     States whether a connection is carried through a relay, and by which paths, in one line a log can be compared by.
	/// </summary>
	/// <param name="shim">The connection's shim, or null when the connection takes the direct path.</param>
	/// <param name="relayIds">The volunteers the connection opened, in path order.</param>
	/// <returns>The line to log, which names the direct path when nothing is carried through a relay.</returns>
	public static string DescribeRelayedConnection(RelayUdpShim? shim, IReadOnlyList<string> relayIds)
	{
		if (shim == null) return "relay carrier=direct";

		string pathNames = relayIds == null || relayIds.Count == 0 ? "none" : string.Join(',', relayIds);
		return $"relay carrier=volunteer localPort={shim.Port} volunteers={pathNames}\n{shim.Describe()}";
	}
}
