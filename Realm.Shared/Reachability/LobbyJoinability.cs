namespace Realm.Shared.Reachability;

/// <summary>Whether this screen's own seat in a lobby it hosts is held the way it must be: over loopback.</summary>
public enum LoopbackJoinOutcome
{
	/// <summary>The authority admitted this screen's own client over the loopback address and described the lobby back to it.</summary>
	Answered,

	/// <summary>This screen's own seat is held over an address other than loopback, so a broken local network could stand between the host and its own player.</summary>
	SessionNotOnLoopback,

	/// <summary>The authority never described the lobby back to this screen, so the local round trip is not working.</summary>
	NoAnswer
}

/// <summary>
///     Decides whether a lobby may be published at all. Every hosted lobby needs the loopback round trip, because that is how
///     the host plays its own seat and a lobby nobody can control is a lobby nobody can play. A lobby other players can see
///     additionally needs the registry node's measurement of its game port, so a lobby that only this machine can reach is
///     never shown to anybody.
/// </summary>
public static class LobbyJoinability
{
	/// <summary>Whether a lobby that other players can see needs the registry node's measurement.</summary>
	public static bool RequiresExternalCheck(bool publicLobby) => publicLobby;

	/// <summary>Reads what the local round trip showed, from the facts the screen has about its own seat.</summary>
	public static LoopbackJoinOutcome DescribeLoopback(bool connectedOverLoopback, bool lobbyStateReceived)
	{
		if (!connectedOverLoopback) return LoopbackJoinOutcome.SessionNotOnLoopback;
		return lobbyStateReceived ? LoopbackJoinOutcome.Answered : LoopbackJoinOutcome.NoAnswer;
	}

	/// <summary>
	///     Whether the lobby may be published. <paramref name="externalFailure" /> is what the registry node's measurement
	///     reported, and is not consulted for a lobby only this machine plays. <paramref name="externalEndpointIsPublic" />
	///     is whether the endpoint that measurement named is one a player outside this network could be sent to: a check that
	///     answered from inside this network, or from outside it but at an address inside it, proves nothing about players
	///     elsewhere and does not publish the lobby. <paramref name="mappingBehaviour" /> is whether the router gave the game
	///     port one outside port for every destination or one per destination: a router that makes one per destination hands
	///     the port a different outside port for every peer, so a punch can open the way for the endpoint it was aimed at and
	///     for no other, which is a lobby no player could join by punching.
	/// </summary>
	/// <param name="publicLobby">Whether other players will be able to see and join the lobby.</param>
	/// <param name="loopback">What this screen's own seat looked like.</param>
	/// <param name="externalFailure">What the registry node's measurement reported, or <see cref="HostReachabilityFailure.None" />.</param>
	/// <param name="externalEndpointIsPublic">Whether the measured endpoint is publicly routable.</param>
	/// <param name="mappingBehaviour">The mapping behaviour the measurement showed, or <see cref="MappingBehaviours.Unknown" />.</param>
	/// <param name="hasEndpointNeedingNoRouter">
	///     Whether the lobby is reached at an endpoint no router stands in front of, which is what an IPv6 endpoint is. A
	///     router that maps a port per destination then costs the lobby the endpoints it translates and not the lobby itself.
	/// </param>
	/// <param name="carriedThroughVolunteers">
	///     Whether the lobby is carried through volunteers this node measured as open instead of at an endpoint of its own.
	///     Such a lobby has no address of its own to judge: a player is never sent there, so what decides whether it may be
	///     published is the carriers' measurement rather than the host's port.
	/// </param>
	/// <returns>Why the lobby may not be published, or <see cref="HostReachabilityFailure.None" /> when it may.</returns>
	public static HostReachabilityFailure Decide(bool publicLobby, LoopbackJoinOutcome loopback, HostReachabilityFailure externalFailure, bool externalEndpointIsPublic = true, string? mappingBehaviour = MappingBehaviours.Unknown, bool hasEndpointNeedingNoRouter = false, bool carriedThroughVolunteers = false)
	{
		if (loopback == LoopbackJoinOutcome.SessionNotOnLoopback) return HostReachabilityFailure.LoopbackNotOnLocalAddress;
		if (loopback != LoopbackJoinOutcome.Answered) return HostReachabilityFailure.LoopbackUnanswered;
		if (!RequiresExternalCheck(publicLobby)) return HostReachabilityFailure.None;
		if (carriedThroughVolunteers) return HostReachabilityFailure.None;
		if (externalFailure != HostReachabilityFailure.None) return externalFailure;
		if (!externalEndpointIsPublic) return HostReachabilityFailure.ProbeFromOwnNetwork;
		if (mappingBehaviour != MappingBehaviours.PerDestination) return HostReachabilityFailure.None;
		return hasEndpointNeedingNoRouter ? HostReachabilityFailure.None : HostReachabilityFailure.MappingPerDestination;
	}
}
