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

	[Test]
	public async Task ALobbyOnlyThisMachinePlaysStillAnswersOnLoopback()
	{
		RealAuthorityHost.IgnoreWhenUnavailable();

		int port = FreePort.Udp();
		await using RealAuthorityHost authority = await RealAuthorityHost.StartLobbyAsync(port, loopbackOnly: true);
		using var probes = new UdpReachabilityProbeService(FreePort.Udp());

		ReachabilityTicket ticket = probes.Begin(IPAddress.Loopback, port, IPAddress.Loopback, 5555, 2000);
		ReachabilityReport report = await ProbePolling.WaitForResultAsync(probes, ticket, TimeSpan.FromSeconds(8));

		Assert.That(report.Answered, Is.True, "a single-player lobby has to answer its own machine, or the host could not control its own player");
		Assert.That(report.ObservedIp, Is.EqualTo(IPAddress.Loopback.ToString()));
	}
}
