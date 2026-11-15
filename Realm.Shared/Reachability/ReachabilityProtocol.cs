namespace Realm.Shared.Reachability;

/// <summary>
///     The routes a screen uses to rehearse a join with a registry node before it registers a lobby.
/// </summary>
public static class ReachabilityRoutes
{
	public const string Begin = "/lobbies/reachability/begin";
	public const string Report = "/lobbies/reachability";

	/// <summary>
	///     The route a node answers with the endpoint families it can check. A client and a registry node are deployed
	///     separately, so a host asks what the node in front of it can check rather than assuming. A node that does not
	///     answer this route predates IPv6 endpoints and is read as an IPv4-only node, which is exactly what it is.
	/// </summary>
	public const string Capabilities = "/lobbies/capabilities";
}

/// <summary>
///     What a registry node can check, stated by the node itself. The endpoint families are the ones it has a probe
///     socket for and can therefore measure a lobby in; a node with no IPv6 socket can never verify an IPv6 endpoint, and
///     a host that only holds an IPv6 address needs to be told that instead of having its lobby refused as unreachable.
/// </summary>
/// <param name="ProtocolVersion">
///     The version of the lobby reachability protocol the node speaks. Version 1 is the single-endpoint protocol, which
///     only ever measured IPv4; version 2 checks a list of endpoints, one per family.
/// </param>
/// <param name="EndpointFamilies">The families the node can measure, as the lobby protocol names them.</param>
public sealed record RegistryCapabilities(int ProtocolVersion, IReadOnlyList<string> EndpointFamilies)
{
	/// <summary>The version a node that answers no capabilities route at all is read as.</summary>
	public const int SingleEndpointProtocolVersion = 1;

	/// <summary>The version of the protocol this build speaks.</summary>
	public const int EndpointListProtocolVersion = 2;

	/// <summary>Whether this node checks a list of endpoints rather than the one endpoint it was told about.</summary>
	public bool ChecksEndpointList => ProtocolVersion >= EndpointListProtocolVersion;

	/// <summary>Whether this node can measure an endpoint in a family.</summary>
	/// <param name="family">The family to look for.</param>
	/// <returns>Whether the node has a probe socket in that family.</returns>
	public bool CanCheck(LobbyAddressFamily family)
	{
		if (family == LobbyAddressFamily.Unknown) return false;
		if (!ChecksEndpointList) return family == LobbyAddressFamily.IPv4;

		string name = LobbyAddresses.FamilyName(family);
		for (int index = 0; EndpointFamilies != null && index < EndpointFamilies.Count; index++)
		{
			if (string.Equals(EndpointFamilies[index], name, StringComparison.OrdinalIgnoreCase)) return true;
		}

		return false;
	}
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
///     refuses unsolicited traffic can still answer it. <paramref name="Family" /> is the family the check is made in,
///     which tells the host which of the node's probe sockets will reach it and therefore which of its own sockets the
///     punch has to leave from; an IPv6 check needs no punch at all.
/// </summary>
public record ReachabilityTicket(string ProbeId, string ProbeIp, int ProbePort, int WindowMilliseconds, string Family = "");

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
	string Family = "");

/// <summary>Marks the refusal a registry node states when it cannot check endpoints in the family a lobby offers.</summary>
public static class ReachabilityRefusalCodes
{
	/// <summary>The node has no probe socket in the family the lobby advertised, so the endpoint can never be verified.</summary>
	public const string FamilyNotChecked = "lobby_endpoint_family_not_checked";
}

/// <summary>Marks the refusal a registry node states when a lobby arrives without a check it answered itself.</summary>
public static class ReachabilityErrorCodes
{
	public const string NotVerified = "lobby_reachability_not_verified";
}
