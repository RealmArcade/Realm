using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;

namespace Realm.Shared.Reachability;

/// <summary>What the address this machine measured outside its own network says about what stands in front of it.</summary>
public enum OutsideAddressKind
{
	/// <summary>Nothing was measured, so nothing can be said.</summary>
	Unmeasured,

	/// <summary>A publicly routable address: the outermost translation this machine can see is the public one.</summary>
	Public,

	/// <summary>A private address, which means a second router or the provider's own box translated it before the internet saw it.</summary>
	Private,

	/// <summary>An address in the range providers share among many customers.</summary>
	SharedProviderRange
}

/// <summary>
///     Reads the address a machine measured for itself the way the connectivity help has to read it: a private mapped
///     address is a second router in the path, a shared one is the provider's own NAT, and a public one is the ordinary
///     shape. It judges only what was measured - an address nothing measured is <see cref="OutsideAddressKind.Unmeasured" />,
///     because a guess about the router is exactly what this dialog exists to stop making.
/// </summary>
public static class OutsideAddresses
{
	/// <summary>Which kind of address this is, from the outside address a machine measured for itself.</summary>
	/// <param name="measuredAddress">The address, as text, or an empty string when nothing was measured.</param>
	/// <returns>The kind.</returns>
	public static OutsideAddressKind Classify(string? measuredAddress)
	{
		if (!IPAddress.TryParse((measuredAddress ?? string.Empty).Trim(), out IPAddress? address)) return OutsideAddressKind.Unmeasured;
		if (address.IsIPv4MappedToIPv6) address = address.MapToIPv4();
		if (IPAddress.IsLoopback(address) || address.Equals(IPAddress.Any)) return OutsideAddressKind.Unmeasured;
		if (address.AddressFamily != AddressFamily.InterNetwork) return OutsideAddressKind.Public;

		Span<byte> quad = stackalloc byte[4];
		address.TryWriteBytes(quad, out _);

		if (quad[0] == 100 && quad[1] >= 64 && quad[1] <= 127) return OutsideAddressKind.SharedProviderRange;
		if (quad[0] == 10) return OutsideAddressKind.Private;
		if (quad[0] == 172 && quad[1] >= 16 && quad[1] <= 31) return OutsideAddressKind.Private;
		if (quad[0] == 192 && quad[1] == 168) return OutsideAddressKind.Private;

		return ReachabilityAddresses.IsPubliclyRoutable(address) ? OutsideAddressKind.Public : OutsideAddressKind.Private;
	}
}

/// <summary>
///     What the checks may read about this machine and the attempt that failed. Every member is something the client
///     already measured or was told; nothing here is asked of the player and nothing is assumed. A member the screen could
///     not measure stays at its default, which every check reads as "not measured" rather than as a negative answer.
/// </summary>
public readonly record struct ConnectivityFacts
{
	/// <summary>Which side of the connection the dialog is written for.</summary>
	public ConnectivityAudience Audience { get; init; }

	/// <summary>What the last attempt to measure this lobby reported, or <see cref="HostReachabilityFailure.None" />.</summary>
	public HostReachabilityFailure HostFailure { get; init; }

	/// <summary>The step the last join reached, named from <see cref="JoinPhases" />, or an empty string when nothing failed.</summary>
	public string JoinPhase { get; init; }

	/// <summary>The port the lobby is listed at, which is the preferred port unless another program held it.</summary>
	public int ListedPort { get; init; }

	/// <summary>The port a router is asked to forward when the lobby could not be published.</summary>
	public int PreferredPort { get; init; }

	/// <summary>The outside port the router gave the game port, when a check measured one.</summary>
	public int ObservedOutsidePort { get; init; }

	/// <summary>Whether the router was measured to reuse one outside port for every destination, or to make one per destination.</summary>
	public string MappingBehaviour { get; init; }

	/// <summary>The class the lobby was listed with, as a registry node measured it.</summary>
	public string ReachabilityClass { get; init; }

	/// <summary>What the local firewall said when this lobby asked it for an incoming allowance.</summary>
	public WindowsFirewallGrant FirewallGrant { get; init; }

	/// <summary>What the firewall rule says now, or <see cref="WindowsFirewallRuleState.Unknown" /> when it could not be read.</summary>
	public WindowsFirewallRuleState FirewallRule { get; init; }

	/// <summary>Whether the local NAT was classified at all.</summary>
	public bool LocalNatMeasured { get; init; }

	/// <summary>Whether the local NAT was classified as symmetric, which is the shape a punch cannot open.</summary>
	public bool LocalNatIsSymmetric { get; init; }

	/// <summary>The address this machine measured for itself outside its own network, or an empty string when nothing was measured.</summary>
	public string MeasuredOutsideAddress { get; init; }

	/// <summary>Whether the lobby server was asked whether it answers this machine at all.</summary>
	public bool LobbyServerProbed { get; init; }

	/// <summary>Whether the lobby server answered, when it was asked.</summary>
	public bool LobbyServerAnswered { get; init; }

	/// <summary>Whether a VPN or tunnel adapter is up on this machine, which is the only part of a VPN a machine can see.</summary>
	public bool TunnelAdapterUp { get; init; }

	/// <summary>Whether UDP <see cref="PreferredPort" /> was held by something other than the game when it was asked.</summary>
	public bool PreferredPortHeldElsewhere { get; init; }

	/// <summary>Whether this lobby is offered over IPv4 at all, which decides whether a router is asked anything.</summary>
	public bool OffersIpv4 { get; init; }
}

/// <summary>One line naming what the game measured, and the value that line states.</summary>
/// <param name="Key">The sentence, which is also its English text.</param>
/// <param name="Value">The port the sentence states, or null when it states none.</param>
public readonly record struct ConnectivityLikelyCause(string Key, object? Value);

/// <summary>
///     Runs the connectivity checks. Every answer is either something this machine measured - the reachability failure the
///     lobby server reported, the firewall's own answer, the address this machine measured for itself, whether the lobby
///     server answers at all, whether a tunnel adapter is up, whether anything else holds the game port - or the statement
///     that the game cannot settle that issue by testing it. Nothing here probes, publishes, punches or changes anything:
///     the checks read what the game already knows and say what it means.
/// </summary>
public static class ConnectivityCheck
{
	/// <summary>The one line a dialog opens with, naming what the game actually measured.</summary>
	/// <param name="facts">What the checks may read.</param>
	/// <returns>The sentence and the value it states.</returns>
	public static ConnectivityLikelyCause DescribeLikelyCause(ConnectivityFacts facts)
	{
		if (facts.Audience == ConnectivityAudience.Hosting)
		{
			int port = facts.ListedPort > 0 ? facts.ListedPort : facts.PreferredPort;
			if (facts.HostFailure == HostReachabilityFailure.None) return new ConnectivityLikelyCause(ConnectivitySentences.LikelyHostNothingFailed, null);

			return facts.HostFailure switch
			{
				HostReachabilityFailure.ProbeUnanswered => new ConnectivityLikelyCause(ConnectivitySentences.LikelyHostProbeUnanswered, port),
				HostReachabilityFailure.MappingPerDestination => new ConnectivityLikelyCause(ConnectivitySentences.LikelyHostPerDestination, port),
				HostReachabilityFailure.MappingUnmeasured => new ConnectivityLikelyCause(ConnectivitySentences.LikelyHostMappingUnmeasured, port),
				HostReachabilityFailure.LoopbackUnanswered or HostReachabilityFailure.LoopbackNotOnLocalAddress => new ConnectivityLikelyCause(ConnectivitySentences.LikelyHostLoopback, null),
				HostReachabilityFailure.ProbeFromOwnNetwork => new ConnectivityLikelyCause(ConnectivitySentences.LikelyHostOwnNetwork, null),
				HostReachabilityFailure.ProbeFamilyUnavailable => new ConnectivityLikelyCause(ConnectivitySentences.LikelyHostFamily, null),
				HostReachabilityFailure.RegistryUnreachable or HostReachabilityFailure.ProbeSocketUnknown => new ConnectivityLikelyCause(ConnectivitySentences.LikelyHostRegistry, null),
				_ => new ConnectivityLikelyCause(ConnectivitySentences.LikelyHostProbeUnanswered, port)
			};
		}

		// A join that failed at a step the network is not part of - a lobby that refused this seat, an authority that
		// admitted this screen and said nothing more - is not a network failure, and a screen that never recorded a step at
		// all has nothing measured to name. Both say so instead of naming a cause nobody observed.
		if (!JoinPhases.IsKnown(facts.JoinPhase) || facts.JoinPhase is JoinPhases.Admission or JoinPhases.Session)
		{
			return new ConnectivityLikelyCause(ConnectivitySentences.LikelyJoinNothingMeasured, null);
		}

		return facts.JoinPhase switch
		{
			JoinPhases.Registry or JoinPhases.Resolve => new ConnectivityLikelyCause(ConnectivitySentences.LikelyJoinRegistry, null),
			JoinPhases.Report => new ConnectivityLikelyCause(ConnectivitySentences.LikelyJoinOwnSide, null),
			JoinPhases.Punch or JoinPhases.Answer => new ConnectivityLikelyCause(ConnectivitySentences.LikelyJoinHostSide, null),
			JoinPhases.Admission or JoinPhases.Session => new ConnectivityLikelyCause(ConnectivitySentences.LikelyJoinRefused, null),
			_ => new ConnectivityLikelyCause(ConnectivitySentences.LikelyJoinOwnSide, null)
		};
	}

	/// <summary>
	///     The entry the measured failure selects when the dialog opens. It is the issue the evidence already in hand names,
	///     so a reader starts on the thing the game measured rather than on the first row of a list.
	/// </summary>
	/// <param name="facts">What the checks may read.</param>
	/// <returns>The issue to select.</returns>
	public static ConnectivityIssue InitialIssue(ConnectivityFacts facts)
	{
		if (facts.Audience == ConnectivityAudience.Hosting)
		{
			return facts.HostFailure switch
			{
				HostReachabilityFailure.ProbeUnanswered => ConnectivityIssue.RouterBlocksInboundPath,
				HostReachabilityFailure.MappingPerDestination or HostReachabilityFailure.MappingUnmeasured => ConnectivityIssue.OutsidePortChangesPerPlayer,
				HostReachabilityFailure.LoopbackUnanswered or HostReachabilityFailure.LoopbackNotOnLocalAddress => ConnectivityIssue.LocalSeatBroken,
				HostReachabilityFailure.RegistryUnreachable or HostReachabilityFailure.ProbeSocketUnknown => ConnectivityIssue.RestrictiveNetwork,
				HostReachabilityFailure.ProbeFamilyUnavailable or HostReachabilityFailure.ProbeFromOwnNetwork => ConnectivityIssue.LobbyServerUnreachable,
				_ => ConnectivityIssue.RouterBlocksInboundPath
			};
		}

		return facts.JoinPhase switch
		{
			JoinPhases.Registry or JoinPhases.Resolve => ConnectivityIssue.LobbyServerUnreachable,
			JoinPhases.Report => ConnectivityIssue.RestrictiveNetwork,
			JoinPhases.Punch or JoinPhases.Answer => ConnectivityIssue.RouterBlocksInboundPath,
			_ => ConnectivityIssue.RestrictiveNetwork
		};
	}

	/// <summary>Runs every check the side of the connection lists, in the order of that list.</summary>
	/// <param name="facts">What the checks may read.</param>
	/// <returns>One answer per listed issue, unordered.</returns>
	public static IReadOnlyList<ConnectivityDiagnosis> EvaluateAll(ConnectivityFacts facts)
	{
		IReadOnlyList<ConnectivityIssueContent> entries = ConnectivityIssueCatalog.For(facts.Audience);
		var diagnoses = new List<ConnectivityDiagnosis>(entries.Count);
		for (int index = 0; index < entries.Count; index++)
		{
			diagnoses.Add(Evaluate(facts, entries[index].Issue));
		}

		return diagnoses;
	}

	/// <summary>Runs the one check an issue has.</summary>
	/// <param name="facts">What the checks may read.</param>
	/// <param name="issue">The issue to check.</param>
	/// <returns>The answer.</returns>
	public static ConnectivityDiagnosis Evaluate(ConnectivityFacts facts, ConnectivityIssue issue)
	{
		if (facts.Audience == ConnectivityAudience.Joining) return EvaluateJoining(facts, issue);
		return EvaluateHosting(facts, issue);
	}

	private static ConnectivityDiagnosis EvaluateHosting(ConnectivityFacts facts, ConnectivityIssue issue)
	{
		// The three failures that describe the lobby server rather than the game port: a check that never ran cannot say
		// anything about the port, and a dialog that turned its silence into advice would send a host to their router for
		// nothing.
		bool portWasMeasured = facts.HostFailure is HostReachabilityFailure.None or HostReachabilityFailure.ProbeUnanswered or HostReachabilityFailure.MappingPerDestination or HostReachabilityFailure.MappingUnmeasured;
		OutsideAddressKind outsideAddress = OutsideAddresses.Classify(facts.MeasuredOutsideAddress);
		int port = facts.ListedPort > 0 ? facts.ListedPort : facts.PreferredPort;
		string outsideText = string.IsNullOrWhiteSpace(facts.MeasuredOutsideAddress) ? string.Empty : facts.MeasuredOutsideAddress.Trim();

		switch (issue)
		{
			case ConnectivityIssue.RouterBlocksInboundPath:
				return facts.HostFailure switch
				{
					HostReachabilityFailure.ProbeUnanswered => Confirmed(issue, ConnectivitySentences.HostRouterConfirmed),
					HostReachabilityFailure.MappingPerDestination => Confirmed(issue, ConnectivitySentences.HostPerDestinationConfirmed),
					HostReachabilityFailure.MappingUnmeasured => Inconclusive(issue, ConnectivitySentences.HostRouterInconclusive),
					HostReachabilityFailure.None => RuledOut(issue, ConnectivitySentences.HostRouterRuledOut, port),
					_ => Inconclusive(issue, ConnectivitySentences.HostRouterInconclusive)
				};

			case ConnectivityIssue.OutsidePortChangesPerPlayer:
				if (facts.HostFailure == HostReachabilityFailure.MappingPerDestination) return Confirmed(issue, ConnectivitySentences.HostPerDestinationConfirmed);
				if (facts.HostFailure == HostReachabilityFailure.MappingUnmeasured) return Inconclusive(issue, ConnectivitySentences.HostPerDestinationInconclusive);
				if (portWasMeasured) return RuledOut(issue, ConnectivitySentences.HostPerDestinationRuledOut);
				return Inconclusive(issue, ConnectivitySentences.HostPerDestinationInconclusive);

			case ConnectivityIssue.FirewallOrSecuritySoftware:
				if (facts.FirewallGrant == WindowsFirewallGrant.Declined) return Confirmed(issue, ConnectivitySentences.HostFirewallDeclined);
				if (facts.FirewallGrant == WindowsFirewallGrant.NotApplicable) return RuledOut(issue, ConnectivitySentences.HostFirewallNoWindows);
				if (facts.FirewallRule == WindowsFirewallRuleState.Blocked) return Confirmed(issue, ConnectivitySentences.HostFirewallRuleBlocks);
				if (facts.FirewallRule == WindowsFirewallRuleState.Disabled) return Confirmed(issue, ConnectivitySentences.HostFirewallRuleDisabled);
				if (facts.FirewallRule == WindowsFirewallRuleState.Allowed) return RuledOut(issue, ConnectivitySentences.HostFirewallSecuritySoftwareUnknown);
				return Inconclusive(issue, ConnectivitySentences.HostFirewallUnreadable);

			case ConnectivityIssue.DoubleNat:
				return outsideAddress switch
				{
					OutsideAddressKind.Private => Confirmed(issue, ConnectivitySentences.DoubleNatConfirmed, outsideText),
					OutsideAddressKind.SharedProviderRange => Inconclusive(issue, ConnectivitySentences.DoubleNatProviderShared, outsideText),
					OutsideAddressKind.Public => RuledOut(issue, ConnectivitySentences.DoubleNatRuledOut, outsideText),
					_ => Inconclusive(issue, ConnectivitySentences.OutsideAddressUnmeasured)
				};

			case ConnectivityIssue.CarrierGradeNat:
				return outsideAddress switch
				{
					OutsideAddressKind.SharedProviderRange => Confirmed(issue, ConnectivitySentences.CarrierGradeNatConfirmed, outsideText),
					OutsideAddressKind.Private or OutsideAddressKind.Public => RuledOut(issue, ConnectivitySentences.CarrierGradeNatRuledOut, outsideText),
					_ => Inconclusive(issue, ConnectivitySentences.OutsideAddressUnmeasured)
				};

			case ConnectivityIssue.VpnOrVirtualAdapter:
				if (facts.TunnelAdapterUp && (facts.HostFailure == HostReachabilityFailure.ProbeUnanswered || facts.HostFailure == HostReachabilityFailure.MappingPerDestination || facts.LocalNatIsSymmetric)) return Likely(issue, ConnectivitySentences.VpnConfirmed);
				if (facts.TunnelAdapterUp) return Inconclusive(issue, ConnectivitySentences.VpnInconclusive);
				return RuledOut(issue, ConnectivitySentences.VpnRuledOut);

			case ConnectivityIssue.RestrictiveNetwork:
				if (facts.HostFailure is HostReachabilityFailure.RegistryUnreachable or HostReachabilityFailure.ProbeSocketUnknown) return Confirmed(issue, ConnectivitySentences.RestrictiveConfirmed);
				if (facts.LobbyServerProbed && !facts.LobbyServerAnswered) return Confirmed(issue, ConnectivitySentences.RestrictiveConfirmed);
				if (facts.LobbyServerProbed) return RuledOut(issue, ConnectivitySentences.RestrictiveRuledOut);
				return Inconclusive(issue, ConnectivitySentences.RestrictiveUnprobed);

			case ConnectivityIssue.LocalSeatBroken:
				if (facts.HostFailure is HostReachabilityFailure.LoopbackUnanswered or HostReachabilityFailure.LoopbackNotOnLocalAddress) return Confirmed(issue, ConnectivitySentences.LocalSeatLoopbackConfirmed);
				if (facts.PreferredPortHeldElsewhere) return Confirmed(issue, ConnectivitySentences.LocalSeatPortHeld);
				if (portWasMeasured) return RuledOut(issue, ConnectivitySentences.LocalSeatRuledOut, facts.ListedPort > 0 ? facts.ListedPort : facts.PreferredPort);
				return Inconclusive(issue, ConnectivitySentences.LocalSeatUnmeasured);

			case ConnectivityIssue.LobbyServerUnreachable:
				if (facts.HostFailure is HostReachabilityFailure.RegistryUnreachable or HostReachabilityFailure.ProbeSocketUnknown) return Confirmed(issue, ConnectivitySentences.LobbyServerRegistryConfirmed);
				if (facts.HostFailure == HostReachabilityFailure.ProbeFromOwnNetwork) return Confirmed(issue, ConnectivitySentences.LobbyServerOwnNetworkConfirmed);
				if (facts.HostFailure == HostReachabilityFailure.ProbeFamilyUnavailable) return Confirmed(issue, ConnectivitySentences.LobbyServerFamilyConfirmed);
				if (facts.LobbyServerProbed && !facts.LobbyServerAnswered) return Confirmed(issue, ConnectivitySentences.LobbyServerRegistryConfirmed);
				if (facts.LobbyServerProbed) return RuledOut(issue, ConnectivitySentences.LobbyServerRuledOut);
				return Inconclusive(issue, ConnectivitySentences.LobbyServerUnprobed);

			default:
				return Inconclusive(issue, ConnectivitySentences.RestrictiveUnprobed);
		}
	}

	private static ConnectivityDiagnosis EvaluateJoining(ConnectivityFacts facts, ConnectivityIssue issue)
	{
		OutsideAddressKind outsideAddress = OutsideAddresses.Classify(facts.MeasuredOutsideAddress);
		string outsideText = string.IsNullOrWhiteSpace(facts.MeasuredOutsideAddress) ? string.Empty : facts.MeasuredOutsideAddress.Trim();
		bool hostSidePhase = facts.JoinPhase is JoinPhases.Punch or JoinPhases.Answer;
		bool ownSidePhase = facts.JoinPhase is JoinPhases.Report or JoinPhases.Punch or JoinPhases.Answer;

		switch (issue)
		{
			case ConnectivityIssue.RestrictiveNetwork:
				if (facts.LobbyServerProbed && !facts.LobbyServerAnswered) return Confirmed(issue, ConnectivitySentences.JoinRestrictiveConfirmed);
				if (facts.LobbyServerProbed) return Inconclusive(issue, ConnectivitySentences.JoinRestrictiveHostSide);
				return Inconclusive(issue, ConnectivitySentences.JoinRestrictiveUnprobed);

			case ConnectivityIssue.VpnOrVirtualAdapter:
				if (facts.TunnelAdapterUp && (ownSidePhase || facts.LocalNatIsSymmetric)) return Likely(issue, ConnectivitySentences.VpnConfirmed);
				if (facts.TunnelAdapterUp) return Inconclusive(issue, ConnectivitySentences.VpnInconclusive);
				return RuledOut(issue, ConnectivitySentences.VpnRuledOut);

			case ConnectivityIssue.CarrierGradeNat:
				return outsideAddress switch
				{
					OutsideAddressKind.SharedProviderRange => Confirmed(issue, ConnectivitySentences.CarrierGradeNatConfirmed, outsideText),
					OutsideAddressKind.Private or OutsideAddressKind.Public => RuledOut(issue, ConnectivitySentences.CarrierGradeNatRuledOut, outsideText),
					_ => Inconclusive(issue, ConnectivitySentences.OutsideAddressUnmeasured)
				};

			case ConnectivityIssue.DoubleNat:
				return outsideAddress switch
				{
					OutsideAddressKind.Private => Confirmed(issue, ConnectivitySentences.DoubleNatConfirmed, outsideText),
					OutsideAddressKind.SharedProviderRange => Inconclusive(issue, ConnectivitySentences.DoubleNatProviderShared, outsideText),
					OutsideAddressKind.Public => RuledOut(issue, ConnectivitySentences.DoubleNatRuledOut, outsideText),
					_ => Inconclusive(issue, ConnectivitySentences.OutsideAddressUnmeasured)
				};

			case ConnectivityIssue.FirewallOrSecuritySoftware:
				if (facts.LobbyServerAnswered && ownSidePhase) return Confirmed(issue, ConnectivitySentences.JoinFirewallConfirmed);
				return Inconclusive(issue, ConnectivitySentences.JoinFirewallInconclusive);

			case ConnectivityIssue.RouterBlocksInboundPath:
				if (facts.LobbyServerAnswered && hostSidePhase) return Confirmed(issue, ConnectivitySentences.JoinHostSideConfirmed);
				return Inconclusive(issue, ConnectivitySentences.JoinHostSideInconclusive);

			case ConnectivityIssue.LobbyServerUnreachable:
				if (facts.LobbyServerProbed && !facts.LobbyServerAnswered) return Confirmed(issue, ConnectivitySentences.JoinRestrictiveConfirmed);
				if (facts.LobbyServerProbed) return RuledOut(issue, ConnectivitySentences.LobbyServerRuledOut);
				return Inconclusive(issue, ConnectivitySentences.LobbyServerUnprobed);

			default:
				return Inconclusive(issue, ConnectivitySentences.JoinRestrictiveUnprobed);
		}
	}

	private static ConnectivityDiagnosis Confirmed(ConnectivityIssue issue, string detailKey, object? detailValue = null) =>
		new(issue, ConnectivityDiagnosisStatus.Confirmed, true, detailKey, detailValue);

	/// <summary>
	///     An issue the evidence points at without settling it: the shape that would explain the failure is present, and the
	///     check that could prove it does not exist. It is ranked with the confirmed answers and says that it could not be
	///     settled, so the reader is not told a guess is a measurement.
	/// </summary>
	private static ConnectivityDiagnosis Likely(ConnectivityIssue issue, string detailKey, object? detailValue = null) =>
		new(issue, ConnectivityDiagnosisStatus.Confirmed, false, detailKey, detailValue);

	private static ConnectivityDiagnosis Inconclusive(ConnectivityIssue issue, string detailKey, object? detailValue = null) =>
		new(issue, ConnectivityDiagnosisStatus.Inconclusive, false, detailKey, detailValue);

	private static ConnectivityDiagnosis RuledOut(ConnectivityIssue issue, string detailKey, object? detailValue = null) =>
		new(issue, ConnectivityDiagnosisStatus.RuledOut, true, detailKey, detailValue);
}
