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
}
