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
///     same machine could not host for anybody else.
/// </summary>
[TestFixture]
public class ReachabilityRouterTests
{
	[Test]
	public async Task AHostBehindARouterThatChangesItsPortIsVerifiedAtTheEndpointItsPunchCameFrom()
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
		Assert.That(result.Failure, Is.EqualTo(HostReachabilityFailure.None), string.Join(" | ", log));
		Assert.That(result.PublicIp, Is.EqualTo(IPAddress.Loopback.ToString()));
		Assert.That(result.PublicPort, Is.EqualTo(router.PublicPort), "the lobby must be published at the endpoint the host's punch came from, which is the only endpoint a peer can reach");

		Assert.That(result.ProbeIp, Is.EqualTo(IPAddress.Loopback.ToString()), "the lobby has to state the endpoint it punched, because that is the one its mapping is refreshed through");
		Assert.That(result.ProbePort, Is.EqualTo(probes.ProbePort));

		Assert.That(probes.TryReadVerified(result.ProbeId, out ReachabilityReport report), Is.True);
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
