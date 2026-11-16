using Realm.Client.Network;
using Realm.Shared.Reachability;

namespace Realm.Client.Services;

/// <summary>
///     What one attempt to host a lobby measured about this machine and its network, kept after the lobby itself is gone.
///     A host troubleshooting a lobby that would not publish has to give the lobby up to open the screen that explains it,
///     and the failure, the port, the outside port, the mapping behaviour, the firewall's answer and the address measured
///     outside the network all belong to the lobby that was given up; without a copy they would be forgotten at the moment
///     the host needs them.
/// </summary>
/// <remarks>
///     This is evidence rather than state: every member is something the game already measured or was told, and a member
///     nothing measured stays empty so the checks read it as unmeasured rather than as a negative answer. Nothing here
///     probes, publishes or changes anything.
/// </remarks>
public readonly record struct ConnectivityEvidence
{
	/// <summary>What the last attempt to publish the lobby reported, or <see cref="HostReachabilityFailure.None" />.</summary>
	public HostReachabilityFailure HostFailure { get; init; }

	/// <summary>The port the lobby was listed at, or zero when no lobby was ever listed.</summary>
	public int ListedPort { get; init; }

	/// <summary>The port a lobby asks for when nothing else holds it, which is the port a router is asked to forward.</summary>
	public int PreferredPort { get; init; }

	/// <summary>The outside port the router gave the game port, or zero when nothing measured one.</summary>
	public int ObservedOutsidePort { get; init; }

	/// <summary>What the router's port mapping was measured to do, or the statement that nothing measured it.</summary>
	public string MappingBehaviour { get; init; }

	/// <summary>The class the lobby was listed with, as the lobby server measured it.</summary>
	public string ReachabilityClass { get; init; }

	/// <summary>What the local firewall answered when the lobby asked it for an incoming allowance.</summary>
	public WindowsFirewallGrant FirewallGrant { get; init; }

	/// <summary>Whether this machine's own NAT was classified at all.</summary>
	public bool LocalNatMeasured { get; init; }

	/// <summary>Whether this machine's own NAT was classified as symmetric, which is the shape a punch cannot open.</summary>
	public bool LocalNatIsSymmetric { get; init; }

	/// <summary>The address this machine measured for itself outside its own network, or an empty string.</summary>
	public string MeasuredOutsideAddress { get; init; }

	/// <summary>Whether the lobby was offered over IPv4 at all, which is the family a router translates.</summary>
	public bool OffersIpv4 { get; init; }

	/// <summary>The step the last join reached, or an empty string when no join was attempted.</summary>
	public string JoinPhase { get; init; }

	/// <summary>What a joining player was told when a join into this lobby failed, or null when none was reported.</summary>
	public string? PlayerJoinFailure { get; init; }

	/// <summary>
	///     The address players on this network join the lobby at, or null when there is none worth offering - a lobby that
	///     could not hold this computer's own seat is not one an unlisted address fixes.
	/// </summary>
	public string? DirectConnectAddress { get; init; }

	/// <summary>The page a report about this machine's network is written from, as it read when it was captured.</summary>
	public string DiagnosticsBlock { get; init; }

	/// <summary>Whether this evidence describes a lobby this machine could not hold its own seat in.</summary>
	public bool LocalSeatFailed => HostFailure is HostReachabilityFailure.LoopbackUnanswered or HostReachabilityFailure.LoopbackNotOnLocalAddress;

	/// <summary>Whether anything about this lobby failed, rather than the lobby being measured reachable.</summary>
	public bool SomethingFailed => HostFailure != HostReachabilityFailure.None;

	/// <summary>Whether another program was holding the port a lobby asks for when this lobby was opened.</summary>
	public bool PreferredPortHeldElsewhere => ListedPort > 0 && PreferredPort > 0 && ListedPort != PreferredPort;

	/// <summary>
	///     Reads this evidence the way the checks read a live machine. The live readings - whether the lobby server answers,
	///     what the firewall rule says now, whether a tunnel adapter is up - are left unanswered here, because they describe
	///     the machine as it is rather than the lobby as it was measured.
	/// </summary>
	/// <param name="audience">Which side of the connection is being explained.</param>
	/// <returns>What the checks may read.</returns>
	public ConnectivityFacts ToFacts(ConnectivityAudience audience)
	{
		return new ConnectivityFacts
		{
			Audience = audience,
			HostFailure = HostFailure,
			JoinPhase = JoinPhase ?? string.Empty,
			ListedPort = ListedPort,
			PreferredPort = PreferredPort > 0 ? PreferredPort : LobbyManager.DefaultGamePort,
			ObservedOutsidePort = ObservedOutsidePort,
			MappingBehaviour = string.IsNullOrEmpty(MappingBehaviour) ? MappingBehaviours.Unknown : MappingBehaviour,
			ReachabilityClass = string.IsNullOrEmpty(ReachabilityClass) ? ReachabilityClasses.Unverified : ReachabilityClass,
			FirewallGrant = FirewallGrant,
			FirewallRule = WindowsFirewallRuleState.Unknown,
			LocalNatMeasured = LocalNatMeasured,
			LocalNatIsSymmetric = LocalNatIsSymmetric,
			MeasuredOutsideAddress = MeasuredOutsideAddress ?? string.Empty,
			LobbyServerProbed = false,
			LobbyServerAnswered = false,
			TunnelAdapterUp = false,
			PreferredPortHeldElsewhere = PreferredPortHeldElsewhere,
			OffersIpv4 = OffersIpv4
		};
	}
}
