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
	///     reported, and is not consulted for a lobby only this machine plays.
	/// </summary>
	public static HostReachabilityFailure Decide(bool publicLobby, LoopbackJoinOutcome loopback, HostReachabilityFailure externalFailure)
	{
		if (loopback == LoopbackJoinOutcome.SessionNotOnLoopback) return HostReachabilityFailure.LoopbackNotOnLocalAddress;
		if (loopback != LoopbackJoinOutcome.Answered) return HostReachabilityFailure.LoopbackUnanswered;
		if (!RequiresExternalCheck(publicLobby)) return HostReachabilityFailure.None;
		return externalFailure;
	}
}
