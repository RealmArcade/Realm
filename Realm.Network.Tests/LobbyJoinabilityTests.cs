using System;
using System.Net;
using System.Threading.Tasks;
using NUnit.Framework;
using Realm.Shared.Reachability;

namespace Realm.Network.Tests;

/// <summary>
///     The rule that keeps an unjoinable lobby out of the registry: every hosted lobby needs the local round trip the host
///     plays its own seat over, and a lobby other players can see also needs a public node to have reached its game port.
/// </summary>
[TestFixture]
public class LobbyJoinabilityTests
{
	[Test]
	public void APublicLobbyIsOnlyPublishedWhenThisMachineAndTheRegistryBothAnswered()
	{
		Assert.That(LobbyJoinability.Decide(publicLobby: true, LoopbackJoinOutcome.Answered, HostReachabilityFailure.None), Is.EqualTo(HostReachabilityFailure.None));
	}

	[Test]
	public void APublicLobbyWhoseGamePortOutsidersCannotReachIsNotPublished()
	{
		Assert.That(LobbyJoinability.Decide(publicLobby: true, LoopbackJoinOutcome.Answered, HostReachabilityFailure.ProbeUnanswered), Is.EqualTo(HostReachabilityFailure.ProbeUnanswered));
		Assert.That(LobbyJoinability.Decide(publicLobby: true, LoopbackJoinOutcome.Answered, HostReachabilityFailure.RegistryUnreachable), Is.EqualTo(HostReachabilityFailure.RegistryUnreachable));
	}

	[Test]
	public void AHostThatCannotReachItsOwnLobbyPublishesNothingEvenWhenOutsidersCan()
	{
		Assert.That(LobbyJoinability.Decide(publicLobby: true, LoopbackJoinOutcome.NoAnswer, HostReachabilityFailure.None), Is.EqualTo(HostReachabilityFailure.LoopbackUnanswered));
		Assert.That(LobbyJoinability.Decide(publicLobby: true, LoopbackJoinOutcome.SessionNotOnLoopback, HostReachabilityFailure.None), Is.EqualTo(HostReachabilityFailure.LoopbackNotOnLocalAddress));
	}

	[Test]
	public void ASinglePlayerLobbyNeedsTheLocalRoundTripAndNothingElse()
	{
		Assert.That(LobbyJoinability.Decide(publicLobby: false, LoopbackJoinOutcome.Answered, HostReachabilityFailure.ProbeUnanswered), Is.EqualTo(HostReachabilityFailure.None), "a lobby nobody else can see does not depend on the router");
		Assert.That(LobbyJoinability.Decide(publicLobby: false, LoopbackJoinOutcome.NoAnswer, HostReachabilityFailure.None), Is.EqualTo(HostReachabilityFailure.LoopbackUnanswered));
		Assert.That(LobbyJoinability.Decide(publicLobby: false, LoopbackJoinOutcome.SessionNotOnLoopback, HostReachabilityFailure.None), Is.EqualTo(HostReachabilityFailure.LoopbackNotOnLocalAddress));
	}

	[Test]
	public void ARouterThatChangesThePortPerDestinationCostsALobbyOnlyTheEndpointsItTranslates()
	{
		Assert.That(
			LobbyJoinability.Decide(publicLobby: true, LoopbackJoinOutcome.Answered, HostReachabilityFailure.None, mappingBehaviour: MappingBehaviours.PerDestination),
			Is.EqualTo(HostReachabilityFailure.MappingPerDestination),
			"a lobby reached only through a router that cannot be punched through is not published");

		Assert.That(
			LobbyJoinability.Decide(publicLobby: true, LoopbackJoinOutcome.Answered, HostReachabilityFailure.None, mappingBehaviour: MappingBehaviours.PerDestination, hasEndpointNeedingNoRouter: true),
			Is.EqualTo(HostReachabilityFailure.None),
			"a lobby that also offers an endpoint no router translates is reachable, so it is published over that one");
	}

	[Test]
	public void AMappingIsMeasuredByComparingTwoDestinationsAndNeverByComparingTheOutsidePortToTheOneTheLobbyHolds()
	{
		Assert.That(MappingBehaviours.FromMeasurement(10000, 10000), Is.EqualTo(MappingBehaviours.Reused), "one outside port for both destinations is a router that keeps one mapping for every player");
		Assert.That(MappingBehaviours.FromMeasurement(10000, 10001), Is.EqualTo(MappingBehaviours.PerDestination), "a port per destination is a router no player can be told the port of");
		Assert.That(MappingBehaviours.FromMeasurement(10000, 0), Is.EqualTo(MappingBehaviours.Unknown), "one destination is not a measurement of the mapping");
		Assert.That(MappingBehaviours.FromMeasurement(0, 0), Is.EqualTo(MappingBehaviours.Unknown));
		Assert.That(MappingBehaviours.FromMeasurement(8999, 8999), Is.EqualTo(MappingBehaviours.Reused), "a router that kept the port happens to compare equal as well");
	}

	[Test]
	public void ATranslatedOutsidePortIsPublishableOnceItIsShownToBeTheSameForEveryDestination()
	{
		ReachabilityReport translated = PunchedCheck(observedPort: 10000, secondObservedPort: 10000, coldAnswered: false);
		Assert.That(ReachabilityClasses.FromReport(translated, MappingBehaviours.Reused), Is.EqualTo(ReachabilityClasses.PunchOnly), "a home router that translates the port is the case a punch exists for");

		ReachabilityReport perDestination = PunchedCheck(observedPort: 10000, secondObservedPort: 10001, coldAnswered: false);
		Assert.That(ReachabilityClasses.FromReport(perDestination, MappingBehaviours.PerDestination), Is.EqualTo(ReachabilityClasses.Unverified), "a port that is not the one another destination sees is not a port any player can be told");

		ReachabilityReport unmeasured = PunchedCheck(observedPort: 10000, secondObservedPort: 0, coldAnswered: false);
		Assert.That(ReachabilityClasses.FromReport(unmeasured, MappingBehaviours.Unknown), Is.EqualTo(ReachabilityClasses.Unverified), "an outside port nobody measured must not be published on the strength of one destination");
	}

	[Test]
	public void APortAnUnpunchedDatagramReachedNeedsNoMappingMeasurementBecauseItIsForwardedToEveryPlayer()
	{
		ReachabilityReport open = PunchedCheck(observedPort: 10000, secondObservedPort: 0, coldAnswered: true);
		Assert.That(ReachabilityClasses.FromReport(open, MappingBehaviours.Unknown), Is.EqualTo(ReachabilityClasses.Open), "a stranger reached the port at that outside port, which is what every player does");

		ReachabilityReport openAndMeasured = PunchedCheck(observedPort: 10000, secondObservedPort: 10000, coldAnswered: true);
		Assert.That(ReachabilityClasses.FromReport(openAndMeasured, MappingBehaviours.Reused), Is.EqualTo(ReachabilityClasses.Open));
	}

	[Test]
	public void OnlyAPublicLobbyIsCheckedFromOutside()
	{
		Assert.That(LobbyJoinability.RequiresExternalCheck(publicLobby: true), Is.True);
		Assert.That(LobbyJoinability.RequiresExternalCheck(publicLobby: false), Is.False);
	}

	[Test]
	public void ALocalSeatHeldOverAnotherAddressIsNotEvidenceOfLoopback()
	{
		Assert.That(LobbyJoinability.DescribeLoopback(connectedOverLoopback: false, lobbyStateReceived: true), Is.EqualTo(LoopbackJoinOutcome.SessionNotOnLoopback));
		Assert.That(LobbyJoinability.DescribeLoopback(connectedOverLoopback: true, lobbyStateReceived: false), Is.EqualTo(LoopbackJoinOutcome.NoAnswer));
		Assert.That(LobbyJoinability.DescribeLoopback(connectedOverLoopback: true, lobbyStateReceived: true), Is.EqualTo(LoopbackJoinOutcome.Answered));
	}

	/// <summary>A report from a check that answered at the stated outside ports, with the fields a case is not about left neutral.</summary>
	/// <param name="observedPort">The outside port the punched route was seen from.</param>
	/// <param name="secondObservedPort">The outside port the second destination's punch was seen from, or zero.</param>
	/// <param name="coldAnswered">Whether the port answered a datagram no punch opened the way for.</param>
	/// <returns>The report.</returns>
	private static ReachabilityReport PunchedCheck(int observedPort, int secondObservedPort, bool coldAnswered) =>
		new(
			"probe",
			ReachabilityStates.Answered,
			true,
			"203.0.113.9",
			observedPort,
			"203.0.113.4",
			8999,
			"10.10.0.2",
			1,
			LobbyAddresses.FamilyName(LobbyAddressFamily.IPv4),
			coldAnswered,
			coldAnswered ? 1 : 0,
			"203.0.113.4",
			secondObservedPort);
}
