using System.Collections.Generic;
using Realm.Shared.Network.Relay;

namespace Realm.AdminServer.Models;

/// <summary>
///     What a volunteer states when it offers to carry other players' match traffic: the endpoint it forwards on, how much
///     of its connection it gives away, and the class its own reachability was measured at. The class is measured, never
///     claimed: a volunteer behind a router that cannot admit a stranger is refused rather than registered and failed later.
/// </summary>
public record RelayRegisterRequest(
	string RelayId,
	int Port,
	string? ReportedIP,
	int MaximumConnections,
	int BudgetBytesPerSecond,
	int MaximumPayloadBytes,
	string? Carrier,
	string? ReachabilityClass);

/// <summary>
///     What a volunteer states while it is carrying connections: how many it holds right now, which is what keeps a
///     listing from offering a volunteer that is already full.
/// </summary>
public record RelayHeartbeatRequest(string RelayId, int ActiveConnections);

/// <summary>What a volunteer states when it stops offering its bandwidth.</summary>
public record RelayUnregisterRequest(string RelayId);

/// <summary>
///     The candidates a node offers for one joining player, in the order both that player and the lobby's host open them.
///     The order is the whole contract: neither peer chooses a volunteer, so the same list produces the same paths on both
///     sides of the connection.
/// </summary>
public record RelayCandidatesAnswer(
	string Status,
	bool Advertised,
	string? Reason,
	IReadOnlyList<RelayCandidateMessage> Candidates);
