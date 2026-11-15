using Realm.Shared.Reachability;

namespace Realm.AdminServer.Models;

/// <summary>
///     What a screen that is taking a lobby over states to the node that owns the lobby's presence. The previous host's
///     token and the epoch the lobby is on are the proof that this is a handover the lobby agreed to, and the endpoints are
///     the ones this node reached itself, exactly as registration requires.
/// </summary>
public record LobbyHandoverRequest(
	string LobbyId,
	string HostToken,
	int OwnershipEpoch,
	IReadOnlyList<LobbyEndpointRegistration>? Endpoints,
	string? MappingBehaviour = null,
	string? NatType = null,
	int HostPingBaseline = 0,
	int SlotsUsed = 0,
	int MaxPlayers = 0,
	string? LocalIP = null,
	int? LocalPort = null,
	int ObservedPort = 0
);

/// <summary>
///     The ownership a registry node moved a lobby to, which the new host proves itself with from then on.
/// </summary>
public record LobbyHandoverResponse(
	string LobbyId,
	int OwnershipEpoch,
	string HostToken
);

/// <summary>
///     The refusal a registry node states when a host message was written by a host the lobby has already left. The epoch
///     it carries is the one the node holds, so a superseded authority can tell it was replaced rather than merely refused.
/// </summary>
public record LobbyOwnershipSuperseded(int OwnershipEpoch, string Code)
{
	public const string SupersededCode = "lobby_ownership_superseded";
}
