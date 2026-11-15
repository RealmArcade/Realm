namespace Realm.Shared.Reachability;

/// <summary>
///     The routes a screen uses to rehearse a join with a registry node before it registers a lobby.
/// </summary>
public static class ReachabilityRoutes
{
	public const string Begin = "/lobbies/reachability/begin";
	public const string Report = "/lobbies/reachability";
}

/// <summary>The states a registry node's probe of a host's public endpoint can be in.</summary>
public static class ReachabilityStates
{
	public const string Waiting = "Waiting";
	public const string Answered = "Answered";
	public const string Silent = "Silent";
}

/// <summary>
///     What a host asks a registry node to check: the endpoint it believes other players will use. An empty or loopback
///     address asks the node to use the address the host's request arrived from, so a host that cannot measure its own
///     public address is still checked against the one the network shows.
/// </summary>
public record ReachabilityBeginRequest(string AdvertisedIp, int AdvertisedPort, int WindowMilliseconds);

/// <summary>
///     The identity of a running check and the endpoint the host must punch so that a game port behind a router that
///     refuses unsolicited traffic can still answer it.
/// </summary>
public record ReachabilityTicket(string ProbeId, string ProbeIp, int ProbePort, int WindowMilliseconds);

/// <summary>
///     What a registry node saw while it tried to reach a host's game port. The observed endpoint is what a peer on the
///     public internet must use, measured from a probe datagram the host's own authority answered.
/// </summary>
public record ReachabilityReport(
	string ProbeId,
	string State,
	bool Answered,
	string ObservedIp,
	int ObservedPort,
	string AdvertisedIp,
	int AdvertisedPort,
	string RequestSourceIp,
	int Attempts);

/// <summary>Marks the refusal a registry node states when a lobby arrives without a check it answered itself.</summary>
public static class ReachabilityErrorCodes
{
	public const string NotVerified = "lobby_reachability_not_verified";

	/// <summary>
	///     The endpoint a check measured is not one a player elsewhere can be sent to, so the lobby would be listed at an
	///     address only its own network can use. It is refused even when the check itself answered.
	/// </summary>
	public const string EndpointNotPublic = "lobby_endpoint_not_public";
}

/// <summary>
///     The answers a registry node gives to a request that is well formed but cannot be acted on. A check asked for by a
///     caller on the node's own network is refused rather than run, because the result of it would only ever describe that
///     network.
/// </summary>
public static class ReachabilityStatusCodes
{
	/// <summary>The caller reaches this node from inside its own network, so nothing this node measures about it is evidence.</summary>
	public const int CallerOnOwnNetwork = 409;
}
