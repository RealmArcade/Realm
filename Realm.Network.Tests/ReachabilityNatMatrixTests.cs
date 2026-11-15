using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Realm.Shared.Reachability;

namespace Realm.Network.Tests;

/// <summary>
///     The matrix of network shapes a hosted lobby meets, judged by the one rule the whole feature rests on: a lobby is
///     published only when a node outside the host's network reached its game port, and no shape is refused for a reason
///     other than the one it actually shows. Each case pairs what the game port does with whether the host punched, so a
///     regression tells which half broke instead of only that hosting failed.
/// </summary>
[TestFixture]
public class ReachabilityNatMatrixTests
{
	[TestCase(FakeGamePort.Behaviour.AnswerEverything, false, HostReachabilityFailure.None)]
	[TestCase(FakeGamePort.Behaviour.AnswerEverything, true, HostReachabilityFailure.None)]
	[TestCase(FakeGamePort.Behaviour.AnswerOnlySolicited, true, HostReachabilityFailure.None)]
	[TestCase(FakeGamePort.Behaviour.AnswerOnlySolicited, false, HostReachabilityFailure.ProbeUnanswered)]
	[TestCase(FakeGamePort.Behaviour.Silent, true, HostReachabilityFailure.ProbeUnanswered)]
	public async Task AGamePortIsJudgedOnlyByWhetherAnExternalNodeReachedIt(FakeGamePort.Behaviour behaviour, bool punch, HostReachabilityFailure expected)
	{
		using var gamePort = new FakeGamePort(behaviour);
		using var probes = new UdpReachabilityProbeService(FreePort.Udp());
		using var registry = new ProbeRegistryHost(probes);

		int punchRequests = 0;

		bool Punch(string address, int port, string probeId)
		{
			Interlocked.Increment(ref punchRequests);
			if (!punch) return true;

			gamePort.Punch(new IPEndPoint(IPAddress.Parse(address), port), probeId);
			return true;
		}

		var verifier = new HostReachabilityVerifier(new HttpClient(), _ => { });
		HostReachabilityResult result = await verifier.VerifyAsync(registry.BaseUrl, IPAddress.Loopback.ToString(), gamePort.Port, Punch);

		Assert.That(result.Failure, Is.EqualTo(expected), result.Detail);

		if (expected == HostReachabilityFailure.None)
		{
			Assert.That(result.PublicPort, Is.EqualTo(gamePort.Port), "an open or punched port is published at the port that answered");
			Assert.That(probes.TryReadVerified(result.ProbeId, out ReachabilityReport report), Is.True);
			Assert.That(report.ObservedPort, Is.EqualTo(gamePort.Port));
			Assert.That(punchRequests, Is.GreaterThan(0), "the rehearsal always asks the authority to punch, because that is what opens a filtering router");
			return;
		}

		Assert.That(probes.TryReadVerified(result.ProbeId, out _), Is.False, "a lobby must never be publishable from a check that did not answer");
		Assert.That(result.Detail, Does.Contain(gamePort.Port.ToString()), "the refusal has to name the endpoint that was probed");
	}

	[Test]
	public async Task ARouterThatTranslatesThePortIsJudgedAgainOnEveryAttemptAndNeverCarriesAnOutsidePortOver()
	{
		using var gamePort = new FakeGamePort(FakeGamePort.Behaviour.AnswerOnlySolicited);
		using var probes = new UdpReachabilityProbeService(FreePort.Udp());
		using var registry = new ProbeRegistryHost(probes);

		// Two attempts, each meeting a router that translates the port: one keeps a mapping for every destination and the
		// other makes a new one per destination. Both are measured on their own, so what the first attempt saw is never what
		// the second is judged by.
		using var stableRouter = new SimulatedNat(gamePort.Port);
		using var driftingRouter = new SimulatedNat(gamePort.Port, MappingBehaviours.PerDestination);

		var verifier = new HostReachabilityVerifier(new HttpClient(), _ => { });
		var observedPorts = new List<int>();

		foreach (SimulatedNat router in new[] { stableRouter, driftingRouter })
		{
			bool Punch(string address, int port, string probeId)
			{
				var destination = new IPEndPoint(IPAddress.Parse(address), port);
				gamePort.Solicit(router.OutsideEndpointFor(destination));
				router.Punch(destination, probeId);
				return true;
			}

			HostReachabilityResult result = await verifier.VerifyAsync(registry.BaseUrl, IPAddress.Loopback.ToString(), gamePort.Port, Punch);

			Assert.That(result.ObservedPort, Is.EqualTo(router.PublicPort), "each attempt is judged by the endpoint the punch of that attempt exposed, never by one remembered from an earlier one");
			Assert.That(result.MappingBehaviour, Is.EqualTo(router == stableRouter ? MappingBehaviours.Reused : MappingBehaviours.PerDestination));
			observedPorts.Add(result.ObservedPort);
		}

		Assert.That(observedPorts[0], Is.Not.EqualTo(observedPorts[1]), "the two attempts must have measured different outside ports, which is what shows nothing was carried over");
	}

	[Test]
	public async Task APunchThatLandsAfterTheCheckStartedStillVerifiesTheLobby()
	{
		using var gamePort = new FakeGamePort(FakeGamePort.Behaviour.AnswerOnlySolicited);
		using var probes = new UdpReachabilityProbeService(FreePort.Udp());
		using var registry = new ProbeRegistryHost(probes);

		int punchRequests = 0;

		bool Punch(string address, int port, string probeId)
		{
			if (Interlocked.Increment(ref punchRequests) < 3) return true;

			gamePort.Punch(new IPEndPoint(IPAddress.Parse(address), port), probeId);
			return true;
		}

		var verifier = new HostReachabilityVerifier(new HttpClient(), _ => { });
		HostReachabilityResult result = await verifier.VerifyAsync(registry.BaseUrl, IPAddress.Loopback.ToString(), gamePort.Port, Punch);

		Assert.That(result.Verified, Is.True, result.Detail);
		Assert.That(punchRequests, Is.GreaterThanOrEqualTo(3), "the check keeps asking for the punch instead of refusing on the first silent probe");
	}

	[Test]
	public async Task ARegistryThatIsDownRefusesTheLobbyWithoutAskingForAPunch()
	{
		int punchRequests = 0;

		var verifier = new HostReachabilityVerifier(new HttpClient { Timeout = TimeSpan.FromSeconds(3) }, _ => { });
		HostReachabilityResult result = await verifier.VerifyAsync($"http://127.0.0.1:{FreePort.Tcp()}", string.Empty, 8999, (_, _, _) =>
		{
			Interlocked.Increment(ref punchRequests);
			return true;
		});

		Assert.That(result.Failure, Is.EqualTo(HostReachabilityFailure.RegistryUnreachable));
		Assert.That(result.Verified, Is.False);
		Assert.That(punchRequests, Is.Zero, "no node started a check, so nothing should be punched and no router mapping should be created for one");
	}
}
