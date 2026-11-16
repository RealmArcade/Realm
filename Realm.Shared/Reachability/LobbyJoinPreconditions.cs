namespace Realm.Shared.Reachability;

/// <summary>
///     What a join answer is missing before this screen can be admitted, judged from what the lobby server stated about
///     itself. A lobby behind a router is admitted only when the host is told where this screen is, and only the lobby
///     server can tell it: a node that cannot do that has to be named as the reason, because the failure a player sees
///     otherwise is a host that never answered, which is a different problem and a different fix.
/// </summary>
public enum LobbyJoinBlock
{
	/// <summary>Nothing is missing, or this lobby needs nothing from the node.</summary>
	None,

	/// <summary>The node stated that it does not relay a joining screen's endpoint to the host.</summary>
	NodeDoesNotRelayPunches,

	/// <summary>The node stated nothing about what it can do, which is a node older than the relay this lobby needs.</summary>
	NodeStatedNoCapabilities,

	/// <summary>The node stated that it relays punches but named no check to report against, which no attempt can fix.</summary>
	NodeNamedNoCheck
}

/// <summary>
///     Decides whether a join answer can lead to an admission before a single attempt is spent on it. A lobby that needs a
///     punch is joined only through a node that relays this screen's endpoint to the host, so an answer from a node that
///     cannot is refused with the reason instead of being dialled until the attempt budget runs out.
/// </summary>
public static class LobbyJoinPreconditions
{
	/// <summary>
	///     What this answer is missing, from what the node stated about what it can do.
	/// </summary>
	/// <param name="answer">The answer the lobby server gave.</param>
	/// <param name="needsPunch">Whether the endpoint this screen would dial has a router in front of it.</param>
	/// <returns>What is missing, or <see cref="LobbyJoinBlock.None" /> when an attempt is worth making.</returns>
	public static LobbyJoinBlock DescribeBlock(LobbyJoinAnswer answer, bool needsPunch)
	{
		if (!needsPunch) return LobbyJoinBlock.None;
		if (!string.IsNullOrWhiteSpace(answer.PunchToken) && answer.PunchPort > 0) return LobbyJoinBlock.None;

		RegistryCapabilities? capabilities = answer.Capabilities;
		if (capabilities == null) return LobbyJoinBlock.NodeStatedNoCapabilities;
		if (!capabilities.RelaysJoinPunch) return LobbyJoinBlock.NodeDoesNotRelayPunches;
		return LobbyJoinBlock.NodeNamedNoCheck;
	}

	/// <summary>
	///     States what the node said about itself, in the terms a player can act on: the families it measures and the classes
	///     it states. A node that stated nothing is said to have stated nothing rather than described as lacking everything.
	/// </summary>
	/// <param name="capabilities">The capabilities the node stated, or null when it stated none.</param>
	/// <returns>A short description for a failure line, or an empty string when the node stated nothing.</returns>
	public static string Describe(RegistryCapabilities? capabilities)
	{
		if (capabilities == null) return string.Empty;

		string families = capabilities.EndpointFamilies.Count == 0 ? "none" : string.Join(", ", capabilities.EndpointFamilies);
		string classes = capabilities.ReachabilityClasses.Count == 0 ? "none" : string.Join(", ", capabilities.ReachabilityClasses);
		return $"endpoint families {families}; reachability classes {classes}; join punch relay {(capabilities.RelaysJoinPunch ? "yes" : "no")}";
	}
}
