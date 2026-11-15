namespace Realm.Shared.Reachability;

/// <summary>
///     The routes a screen uses to rehearse a join with a registry node before it registers a lobby.
/// </summary>
public static class ReachabilityRoutes
{
	public const string Begin = "/lobbies/reachability/begin";
	public const string Report = "/lobbies/reachability";
}

/// <summary>
///     The registry routes a joining screen uses to find out whether the punch its report asked for has been sent. The screen
///     sends the report from the socket it is about to connect with, and it must not send anything toward the lobby before the
///     host's punch has left the host's router: a packet that arrives first occupies the port the punch would have used.
/// </summary>
public static class JoinerPunchRoutes
{
	/// <summary>The route that states a join's report and punch state, with the join's token appended.</summary>
	public const string State = "/lobbies/join-state";
}

/// <summary>The states a registry node's probe of a host's public endpoint can be in.</summary>
public static class ReachabilityStates
{
	public const string Waiting = "Waiting";
	public const string Answered = "Answered";
	public const string Silent = "Silent";
}

/// <summary>
///     How reachable a lobby's game port was shown to be, which is what a listing states instead of a promise. A port a node
///     reached without the host punching it is <see cref="Open" />; one that answered only after the host punched is
///     <see cref="PunchOnly" />, which is the common home-router case and is joinable; one nothing reached is
///     <see cref="Unverified" /> and is not published at all.
/// </summary>
public static class ReachabilityClasses
{
	/// <summary>A node reached the game port without the host sending toward it first.</summary>
	public const string Open = "Open";

	/// <summary>The game port answered only after the host punched the node, so joining requires both sides to punch.</summary>
	public const string PunchOnly = "PunchOnly";

	/// <summary>Nothing reached the game port, so whether a player could join is unknown.</summary>
	public const string Unverified = "Unverified";

	/// <summary>Whether a lobby of this class may be published: everything but <see cref="Unverified" /> is listed.</summary>
	/// <param name="reachabilityClass">The class a check measured.</param>
	/// <returns>Whether a lobby measured this way may be listed.</returns>
	public static bool IsPublishable(string? reachabilityClass) => reachabilityClass is Open or PunchOnly;

	/// <summary>
	///     What a class becomes when real players fail to get in. A lobby shown to be reachable without a punch stops claiming
	///     that the moment players cannot reach it, and one that only punching opened stops claiming even that. Nothing is
	///     ever promoted: only a fresh check may state a better class, because a failure is evidence and a class is a
	///     measurement.
	/// </summary>
	/// <param name="reachabilityClass">The class the lobby was listed with.</param>
	/// <returns>The class a failing lobby is downgraded to.</returns>
	public static string Downgrade(string? reachabilityClass)
	{
		if (reachabilityClass == Open) return PunchOnly;
		if (reachabilityClass == PunchOnly) return Unverified;
		return Unverified;
	}

	/// <summary>
	///     The class a report states, which is the honest reading of what the check actually saw. A router that gives every
	///     destination its own outside port hands the game port a different external port for every peer, so a punch can
	///     open a way for the one endpoint it was sent to and for nobody else: the check answered, and its answer is not
	///     evidence that a player could join, which is what an unverified lobby states.
	/// </summary>
	/// <param name="report">The report a registry node published.</param>
	/// <param name="mappingBehaviour">The mapping behaviour a check measured, or <see cref="MappingBehaviours.Unknown" />.</param>
	/// <returns>One of the three class constants.</returns>
	public static string FromReport(ReachabilityReport? report, string? mappingBehaviour = MappingBehaviours.Unknown)
	{
		if (report == null || !report.Answered) return Unverified;
		if (mappingBehaviour == MappingBehaviours.PerDestination) return Unverified;
		return report.ColdAnswered ? Open : PunchOnly;
	}
}

/// <summary>
///     Whether a router reuses one external port for every destination (an endpoint-independent mapping) or makes a new one
///     per destination. The two need different advice: a reused mapping is what a punch opens for a joining player, while a
///     per-destination mapping hands the game port a different external port for every peer, so no punch can predict the
///     port a player should use. It is measured, not guessed, by comparing the external port the host's punch arrived from
///     with the port the lobby listens on.
/// </summary>
public static class MappingBehaviours
{
	/// <summary>The router gave the game port the same external port it holds, so one mapping serves every destination.</summary>
	public const string Reused = "Reused";

	/// <summary>The router gave the game port an external port of its own for the destination that was measured.</summary>
	public const string PerDestination = "PerDestination";

	/// <summary>Nothing was measured, so the router's behaviour is unknown.</summary>
	public const string Unknown = "Unknown";

	/// <summary>Reads the mapping behaviour from the external port a check observed and the port the lobby listens on.</summary>
	/// <param name="observedPort">The external port the host's punch arrived from, or zero when nothing was observed.</param>
	/// <param name="advertisedPort">The port the lobby's transport holds.</param>
	/// <returns>One of the three behaviour constants.</returns>
	public static string FromMeasurement(int observedPort, int advertisedPort)
	{
		if (observedPort <= 0 || advertisedPort <= 0) return Unknown;
		return observedPort == advertisedPort ? Reused : PerDestination;
	}
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
	int Attempts,
	bool ColdAnswered = false,
	int ColdAttempts = 0);

/// <summary>Marks the refusal a registry node states when a lobby arrives without a check it answered itself.</summary>
public static class ReachabilityErrorCodes
{
	public const string NotVerified = "lobby_reachability_not_verified";

	/// <summary>
	///     The endpoint a check measured is not one a player elsewhere can be sent to, so the lobby would be listed at an
	///     address only its own network can use. It is refused even when the check itself answered.
	/// </summary>
	public const string EndpointNotPublic = "lobby_endpoint_not_public";

	/// <summary>A lobby that does not state the port its game transport holds cannot be listed, because no player could be told where to connect.</summary>
	public const string PortNotStated = "lobby_port_not_stated";
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
