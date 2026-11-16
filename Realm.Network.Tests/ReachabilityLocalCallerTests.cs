using System;
using System.Net;
using NUnit.Framework;
using Realm.Shared.Reachability;

namespace Realm.Network.Tests;

/// <summary>
///     Holds the rule that keeps a reachability check honest: a registry node may only judge a lobby from an address a
///     player elsewhere could be sent to. A node on the same machine or network as the host takes a route that never
///     leaves that network, so its answer describes the wrong thing entirely and must never publish a lobby.
/// </summary>
[TestFixture]
public class ReachabilityLocalCallerTests
{
	[TestCase("127.0.0.1")]
	[TestCase("10.0.0.5")]
	[TestCase("172.16.4.9")]
	[TestCase("172.31.255.1")]
	[TestCase("192.168.1.20")]
	[TestCase("169.254.10.11")]
	[TestCase("100.100.4.4")]
	[TestCase("0.0.0.0")]
	[TestCase("255.255.255.255")]
	[TestCase("224.0.0.251")]
	[TestCase("::1")]
	[TestCase("::")]
	[TestCase("fe80::1234")]
	[TestCase("fd12:3456::1")]
	[TestCase("::ffff:192.168.1.20")]
	[TestCase("")]
	[TestCase("not an address")]
	public void AnAddressInsideTheCallersOwnNetworkIsNotAPublicEndpoint(string address)
	{
		Assert.That(ReachabilityAddresses.IsPubliclyRoutable(address), Is.False, address);
	}

	[TestCase("8.8.8.8")]
	[TestCase("1.1.1.1")]
	[TestCase("203.0.113.10")]
	[TestCase("2001:4860:4860::8888")]
	[TestCase("::ffff:8.8.8.8")]
	public void AnAddressOnThePublicInternetIsAPublicEndpoint(string address)
	{
		Assert.That(ReachabilityAddresses.IsPubliclyRoutable(address), Is.True, address);
	}

	[Test]
	public void ANodeRefusesToJudgeACallerOnItsOwnNetworkAndOpensNoSocketForIt()
	{
		using var probes = new UdpReachabilityProbeService(FreePort.Udp());

		ReachabilityEndpointReply reply = ReachabilityEndpointHandlers.Begin(probes, Request(), IPAddress.Loopback.ToString(), 5555);

		Assert.That(reply.StatusCode, Is.EqualTo(ReachabilityStatusCodes.CallerOnOwnNetwork));
		Assert.That(probes.IsRunning, Is.False, "a check this node cannot judge must not even bind the probe socket");
	}

	[Test]
	public void ANodesOwnPrivateAddressIsRefusedTheSameWayAsLoopback()
	{
		using var probes = new UdpReachabilityProbeService(FreePort.Udp());

		ReachabilityEndpointReply reply = ReachabilityEndpointHandlers.Begin(probes, Request(), "192.168.0.14", 5555);

		Assert.That(reply.StatusCode, Is.EqualTo(ReachabilityStatusCodes.CallerOnOwnNetwork));
		Assert.That(probes.IsRunning, Is.False);
	}

	[Test]
	public void ANodeStartsACheckForACallerOnThePublicInternet()
	{
		using var probes = new UdpReachabilityProbeService(FreePort.Udp());

		ReachabilityEndpointReply reply = ReachabilityEndpointHandlers.Begin(probes, Request(), "203.0.113.10", 5555);

		Assert.That(reply.StatusCode, Is.EqualTo(200));
		Assert.That(reply.Body, Is.InstanceOf<ReachabilityTicket>());
		Assert.That(((ReachabilityTicket)reply.Body!).ProbeId.Length, Is.GreaterThan(0));
	}

	[Test]
	public void AnAnswerMeasuredInsideTheLobbysOwnNetworkDoesNotPublishIt()
	{
		HostReachabilityFailure decision = LobbyJoinability.Decide(publicLobby: true, LoopbackJoinOutcome.Answered, HostReachabilityFailure.None, externalEndpointIsPublic: false);

		Assert.That(decision, Is.EqualTo(HostReachabilityFailure.ProbeFromOwnNetwork));
	}

	[Test]
	public void ALobbyOnlyThisMachinePlaysDoesNotDependOnAPublicEndpoint()
	{
		HostReachabilityFailure decision = LobbyJoinability.Decide(publicLobby: false, LoopbackJoinOutcome.Answered, HostReachabilityFailure.None, externalEndpointIsPublic: false);

		Assert.That(decision, Is.EqualTo(HostReachabilityFailure.None));
	}

	[Test]
	public void AnAnswerMeasuredAtAPublicEndpointStillPublishesTheLobby()
	{
		HostReachabilityFailure decision = LobbyJoinability.Decide(publicLobby: true, LoopbackJoinOutcome.Answered, HostReachabilityFailure.None, externalEndpointIsPublic: true);

		Assert.That(decision, Is.EqualTo(HostReachabilityFailure.None));
	}

	private static ReachabilityBeginRequest Request() => new(IPAddress.Loopback.ToString(), 8999, 1500);
}
