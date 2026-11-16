using Realm.Shared.Network.Relay;
using Realm.Shared.Reachability;

namespace Realm.AdminServer.Models;

public record JoinResponseDto(
	string HostIP,
	int HostPort,
	string? LocalIP = null,
	int? LocalPort = null,
	int? PunchPort = null,
	string? PunchToken = null,
	string? ReachabilityClass = null,
	IReadOnlyList<LobbyEndpointInfo>? Endpoints = null,
	RegistryCapabilities? Capabilities = null,

	/// <summary>
	///     The volunteers this connection may be carried through, in the order both this player and the lobby's host open
	///     them. It is empty when the lobby accepted no relays or this node pools none, which is a join that behaves exactly
	///     as it did before relays existed.
	/// </summary>
	IReadOnlyList<RelayCandidateMessage>? Relays = null
);