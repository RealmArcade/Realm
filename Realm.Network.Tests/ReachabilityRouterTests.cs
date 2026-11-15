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
///     sits behind a router that gives it a different external port, so only the endpoint the host's punch came from can
///     reach it. A node on the same network never meets this shape, which is why a lobby could be published there while the
///     same machine could not host for anybody else. A router that does this hands the game port a different outside port
///     for every destination, so the one endpoint a check can be aimed at is not the one any player would be told to use,
///     and the lobby is refused rather than listed at an endpoint that answered for this node alone.
/// </summary>
[TestFixture]
public class ReachabilityRouterTests
{
	[Test]
	public async Task AHostBehindARouterThatChangesItsPortIsRefusedRatherThanListedAtAnEndpointOneDestinationCanReach()
	{
		using var gamePort = new FakeGamePort(FakeGamePort.Behaviour.AnswerOnlySolicited);
		using var router = new SimulatedNat(gamePort.Port);
		using var probes = new UdpReachabilityProbeService(FreePort.Udp());
		using var registry = new ProbeRegistryHost(probes);

		var log = new List<string>();
		var verifier = new HostReachabilityVerifier(new HttpClient(), log.Add);

		bool Punch(string address, int port, string probeId)
		{
			gamePort.Solicit(router.PublicEndpoint);
			router.Punch(new IPEndPoint(IPAddress.Parse(address), port), probeId);
			return true;
		}

		HostReachabilityResult result = await verifier.VerifyAsync(registry.BaseUrl, IPAddress.Loopback.ToString(), gamePort.Port, Punch);

		Assert.That(router.PublicPort, Is.Not.EqualTo(gamePort.Port), "the router must give the game port a different external port or this test proves nothing");
		Assert.That(result.Failure, Is.EqualTo(HostReachabilityFailure.MappingPerDestination), string.Join(" | ", log));
		Assert.That(result.Verified, Is.False, "a lobby whose outside port changes per destination must never be published");
		Assert.That(result.MappingBehaviour, Is.EqualTo(MappingBehaviours.PerDestination));
		Assert.That(result.ReachabilityClass, Is.EqualTo(ReachabilityClasses.Unverified));
		Assert.That(result.Detail, Does.Contain(router.PublicPort.ToString()), "the refusal has to name the outside port the router handed the check");
		Assert.That(result.Detail, Does.Contain(gamePort.Port.ToString()), "the refusal has to name the port the lobby listens on, which is the one a forward would publish");

		Assert.That(result.PublicPort, Is.EqualTo(gamePort.Port), "the endpoint a lobby is listed at is the port its transport holds, which is what a forward would publish");

		Assert.That(probes.TryReadVerified(result.ProbeId, out ReachabilityReport report), Is.True, "the refusal reads a check that did answer, rather than one that never did");
		Assert.That(report.Answered, Is.True);
		Assert.That(report.ObservedPort, Is.EqualTo(router.PublicPort));
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
