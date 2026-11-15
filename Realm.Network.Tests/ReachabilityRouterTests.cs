using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using NUnit.Framework;
using Realm.Shared.Reachability;

namespace Realm.Network.Tests;

/// <summary>
///     Rehearses a lobby's join the way it happens away from a registry node that shares the host's network: the game port
///     sits behind a router that gives it an outside port of its own, so only the endpoint the host's punch came from can
///     reach it. A node on the same network never meets this shape, which is why a lobby could be published there while the
///     same machine could not host for anybody else.
/// </summary>
/// <remarks>
///     A router that translates the port and keeps one mapping for every destination is the ordinary home router, and the
///     lobby is listed at the outside port it measured, because that is the port every player's punch reaches. A router that
///     makes a mapping per destination hands the game port a different outside port per peer, so no player can be told a
///     port that would work and the lobby is refused. The two look identical from one destination, which is why the check
///     measures two.
/// </remarks>
[TestFixture]
public class ReachabilityRouterTests
{
	[Test]
	public async Task ARouterThatTranslatesThePortButKeepsOneMappingISListedAtTheOutsidePortItMeasured()
	{
		using var gamePort = new FakeGamePort(FakeGamePort.Behaviour.AnswerOnlySolicited);
		using var router = new SimulatedNat(gamePort.Port);
		using var probes = new UdpReachabilityProbeService(FreePort.Udp());
		using var registry = new ProbeRegistryHost(probes);

		var log = new List<string>();
		var verifier = new HostReachabilityVerifier(new HttpClient(), log.Add);

		bool Punch(string address, int port, string probeId)
		{
			var destination = new IPEndPoint(IPAddress.Parse(address), port);
			gamePort.Solicit(router.OutsideEndpointFor(destination));
			router.Punch(destination, probeId);
			return true;
		}

		HostReachabilityResult result = await verifier.VerifyAsync(registry.BaseUrl, IPAddress.Loopback.ToString(), gamePort.Port, Punch);

		Assert.That(router.PublicPort, Is.Not.EqualTo(gamePort.Port), "the router must give the game port a different outside port or this test proves nothing");
		Assert.That(result.Failure, Is.EqualTo(HostReachabilityFailure.None), string.Join(" | ", log));
		Assert.That(result.Verified, Is.True);
		Assert.That(result.MappingBehaviour, Is.EqualTo(MappingBehaviours.Reused));
		Assert.That(result.ReachabilityClass, Is.EqualTo(ReachabilityClasses.PunchOnly), "the router admits the lobby server's probes only because the host punched it");
		Assert.That(result.PublicPort, Is.EqualTo(router.PublicPort), "a lobby behind a translating router is listed at the outside port every player's punch reaches");
		Assert.That(result.PublicIp, Is.EqualTo(IPAddress.Loopback.ToString()));
		Assert.That(result.Detail, Does.Contain(router.PublicPort.ToString()), "the host is told the endpoint players will use");

		Assert.That(probes.TryReadVerified(result.ProbeId, out ReachabilityReport report), Is.True);
		Assert.That(report.Answered, Is.True);
		Assert.That(report.ObservedPort, Is.EqualTo(router.PublicPort));
		Assert.That(report.SecondObservedPort, Is.EqualTo(router.PublicPort), "both destinations must have seen the one outside port the router keeps for every peer");
	}

	[Test]
	public async Task ARouterThatGivesEveryDestinationItsOwnOutsidePortIsRefusedRatherThanListedAtOneThatAnswered()
	{
		using var gamePort = new FakeGamePort(FakeGamePort.Behaviour.AnswerOnlySolicited);
		using var router = new SimulatedNat(gamePort.Port, MappingBehaviours.PerDestination);
		using var probes = new UdpReachabilityProbeService(FreePort.Udp());
		using var registry = new ProbeRegistryHost(probes);

		var log = new List<string>();
		var verifier = new HostReachabilityVerifier(new HttpClient(), log.Add);

		bool Punch(string address, int port, string probeId)
		{
			var destination = new IPEndPoint(IPAddress.Parse(address), port);
			gamePort.Solicit(router.OutsideEndpointFor(destination));
			router.Punch(destination, probeId);
			return true;
		}

		HostReachabilityResult result = await verifier.VerifyAsync(registry.BaseUrl, IPAddress.Loopback.ToString(), gamePort.Port, Punch);

		Assert.That(result.Failure, Is.EqualTo(HostReachabilityFailure.MappingPerDestination), string.Join(" | ", log));
		Assert.That(result.Verified, Is.False, "a lobby whose outside port changes per destination must never be published");
		Assert.That(result.MappingBehaviour, Is.EqualTo(MappingBehaviours.PerDestination));
		Assert.That(result.ReachabilityClass, Is.EqualTo(ReachabilityClasses.Unverified));
		Assert.That(result.Detail, Does.Contain(router.PublicPort.ToString()), "the refusal has to name an outside port the router handed one of the destinations");

		Assert.That(probes.TryReadVerified(result.ProbeId, out ReachabilityReport report), Is.True, "the refusal reads a check that did answer, rather than one that never did");
		Assert.That(report.Answered, Is.True);
		Assert.That(report.ObservedPort, Is.Not.EqualTo(report.SecondObservedPort), "the two destinations saw different outside ports, which is what the refusal was measured from");
		Assert.That(report.ObservedPort, Is.EqualTo(router.PublicPort), "one of the two observations is the outside port the first destination was handed");
	}

	[Test]
	public async Task ATranslatingRouterWhoseSecondDestinationWasNeverMeasuredIsNotPublishedAtAnUnmeasuredPort()
	{
		using var gamePort = new FakeGamePort(FakeGamePort.Behaviour.AnswerOnlySolicited);
		using var router = new SimulatedNat(gamePort.Port);
		using var probes = new UdpReachabilityProbeService(FreePort.Udp());
		using var registry = new ProbeRegistryHost(probes);

		var log = new List<string>();
		var verifier = new HostReachabilityVerifier(new HttpClient(), log.Add);
		int measuredDestination = -1;

		bool Punch(string address, int port, string probeId)
		{
			// The first destination is the one a player takes, and only it is punched: the second one's punch is dropped
			// the way a firewall upstream of the router drops it, so the outside port this router exposed is measured
			// toward one destination and unknown toward every other.
			var destination = new IPEndPoint(IPAddress.Parse(address), port);
			if (measuredDestination < 0) measuredDestination = port;
			if (port != measuredDestination) return true;

			gamePort.Solicit(router.OutsideEndpointFor(destination));
			router.Punch(destination, probeId);
			return true;
		}

		HostReachabilityResult result = await verifier.VerifyAsync(registry.BaseUrl, IPAddress.Loopback.ToString(), gamePort.Port, Punch);

		Assert.That(result.Failure, Is.EqualTo(HostReachabilityFailure.MappingUnmeasured), string.Join(" | ", log));
		Assert.That(result.Verified, Is.False, "an outside port nobody showed to be the same for every player must not be published");
		Assert.That(result.MappingBehaviour, Is.EqualTo(MappingBehaviours.Unknown));
		Assert.That(result.Detail, Does.Contain(router.PublicPort.ToString()), "the refusal has to name the outside port that was measured");
		Assert.That(result.Detail, Does.Contain(gamePort.Port.ToString()), "the refusal has to name the port a forward would publish");
	}

	[Test]
	public async Task AHostBehindARouterThatWasNeverPunchedStaysUnreachable()
	{
		using var gamePort = new FakeGamePort(FakeGamePort.Behaviour.AnswerOnlySolicited);
		using var router = new SimulatedNat(gamePort.Port);
		using var probes = new UdpReachabilityProbeService(FreePort.Udp());
		using var registry = new ProbeRegistryHost(probes);

		var verifier = new HostReachabilityVerifier(new HttpClient(), _ => { });
		HostReachabilityResult result = await verifier.VerifyAsync(registry.BaseUrl, IPAddress.Loopback.ToString(), gamePort.Port, (_, _, _) => true);

		Assert.That(result.Verified, Is.False, "a router that was never punched must not let a probe through, or the check is not measuring the network");
		Assert.That(result.Failure, Is.EqualTo(HostReachabilityFailure.ProbeUnanswered));
	}
}
