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
///     Exercises the reachability check from both halves at once: a registry node's probe socket and the host's own
///     rehearsal of the join, against game ports behaving the way the machines this feature exists for behave.
/// </summary>
[TestFixture]
public class ReachabilityCheckTests
{
	private const int WindowMilliseconds = 1500;

	[Test]
	public async Task AnOpenGamePortIsReachedAndItsEndpointIsMeasured()
	{
		using var gamePort = new FakeGamePort(FakeGamePort.Behaviour.AnswerEverything);
		using var probes = new UdpReachabilityProbeService(FreePort.Udp());

		ReachabilityTicket ticket = probes.Begin(IPAddress.Loopback, gamePort.Port, IPAddress.Loopback, 5555, WindowMilliseconds);
		ReachabilityReport report = await ProbePolling.WaitForResultAsync(probes, ticket, TimeSpan.FromSeconds(6));

		Assert.That(report.State, Is.EqualTo(ReachabilityStates.Answered));
		Assert.That(report.Answered, Is.True);
		Assert.That(report.ObservedIp, Is.EqualTo(IPAddress.Loopback.ToString()));
		Assert.That(report.ObservedPort, Is.EqualTo(gamePort.Port));
		Assert.That(gamePort.ReceivedCount, Is.GreaterThan(0));
	}

	[Test]
	public async Task APortThatRefusesUnsolicitedTrafficIsReachedOnlyOnceTheHostPunches()
	{
		using var gamePort = new FakeGamePort(FakeGamePort.Behaviour.AnswerOnlySolicited);
		using var probes = new UdpReachabilityProbeService(FreePort.Udp());

		ReachabilityTicket withoutPunch = probes.Begin(IPAddress.Loopback, gamePort.Port, IPAddress.Loopback, 5555, 600);
		ReachabilityReport silent = await ProbePolling.WaitForResultAsync(probes, withoutPunch, TimeSpan.FromSeconds(6));

		Assert.That(silent.State, Is.EqualTo(ReachabilityStates.Silent));
		Assert.That(silent.Answered, Is.False);

		ReachabilityTicket withPunch = probes.Begin(IPAddress.Loopback, gamePort.Port, IPAddress.Loopback, 5555, WindowMilliseconds);
		ReachabilityReport answered = await PunchUntilAnsweredAsync(gamePort, probes, withPunch);

		Assert.That(answered.Answered, Is.True);
		Assert.That(answered.ObservedPort, Is.EqualTo(gamePort.Port));
	}

	[Test]
	public async Task APortWithNothingListeningIsReportedSilent()
	{
		int closedPort = FreePort.Udp();
		using var probes = new UdpReachabilityProbeService(FreePort.Udp());

		ReachabilityTicket ticket = probes.Begin(IPAddress.Loopback, closedPort, IPAddress.Loopback, 5555, 500);
		ReachabilityReport report = await ProbePolling.WaitForResultAsync(probes, ticket, TimeSpan.FromSeconds(6));

		Assert.That(report.State, Is.EqualTo(ReachabilityStates.Silent));
		Assert.That(report.Attempts, Is.GreaterThan(0));
	}

	[Test]
	public void ACheckReportsItsEndpointWhileItIsStillRunning()
	{
		using var gamePort = new FakeGamePort(FakeGamePort.Behaviour.Silent);
		using var probes = new UdpReachabilityProbeService(FreePort.Udp());

		ReachabilityTicket ticket = probes.Begin(IPAddress.Loopback, gamePort.Port, IPAddress.Loopback, 5555, 4000);
		ReachabilityReport running = probes.Read(ticket.ProbeId)!;

		Assert.That(running.State, Is.EqualTo(ReachabilityStates.Waiting));
		Assert.That(running.AdvertisedPort, Is.EqualTo(gamePort.Port));
		Assert.That(running.RequestSourceIp, Is.EqualTo(IPAddress.Loopback.ToString()));
	}

	[Test]
	public void ALobbyIsOnlyAcceptedFromACheckThatAnswered()
	{
		using var gamePort = new FakeGamePort(FakeGamePort.Behaviour.Silent);
		using var probes = new UdpReachabilityProbeService(FreePort.Udp());

		ReachabilityTicket ticket = probes.Begin(IPAddress.Loopback, gamePort.Port, IPAddress.Loopback, 5555, 4000);

		Assert.That(probes.TryReadVerified(ticket.ProbeId, out _), Is.False, "a check that has not answered must not publish a lobby");
		Assert.That(probes.TryReadVerified("a-check-this-node-never-ran", out _), Is.False);
		Assert.That(probes.TryReadVerified(string.Empty, out _), Is.False);
	}

	[Test]
	public void AnUnusableAdvertisementIsCheckedAgainstTheAddressTheRequestCameFrom()
	{
		var requestSource = IPAddress.Parse("203.0.113.9");
		var advertisedPublicAddress = IPAddress.Parse("198.51.100.4");

		Assert.That(UdpReachabilityProbeService.ChooseDestinationAddress(IPAddress.Any, requestSource).ToString(), Is.EqualTo("203.0.113.9"));
		Assert.That(UdpReachabilityProbeService.ChooseDestinationAddress(IPAddress.Loopback, requestSource).ToString(), Is.EqualTo("203.0.113.9"));
		Assert.That(UdpReachabilityProbeService.ChooseDestinationAddress(advertisedPublicAddress, requestSource).ToString(), Is.EqualTo("198.51.100.4"));
	}

	[Test]
	public void ThePortOfTheAdvertisementIsAlwaysKeptWhenTheAddressIsDerived()
	{
		ProbeTarget[] fromSourceOnly = UdpReachabilityProbeService.BuildTargets(IPAddress.Any, 8999, IPAddress.Parse("203.0.113.9"));
		ProbeTarget[] fromBoth = UdpReachabilityProbeService.BuildTargets(IPAddress.Parse("198.51.100.4"), 8999, IPAddress.Parse("203.0.113.9"));

		Assert.That(fromSourceOnly.Length, Is.EqualTo(1));
		Assert.That(fromSourceOnly[0].Address.ToString(), Is.EqualTo("203.0.113.9"));
		Assert.That(fromSourceOnly[0].Port, Is.EqualTo(8999));

		Assert.That(fromBoth.Length, Is.EqualTo(2), "a stated address is tried as well as the one the node sees, because either can be the host's real address");
		Assert.That(fromBoth[1].Address.ToString(), Is.EqualTo("203.0.113.9"));
		Assert.That(fromBoth[0].Port, Is.EqualTo(8999));
		Assert.That(fromBoth[1].Port, Is.EqualTo(8999));
	}

	[Test]
	public async Task AHostWhoseGamePortAnswersIsVerifiedAndGainsTheMeasuredEndpoint()
	{
		using var gamePort = new FakeGamePort(FakeGamePort.Behaviour.AnswerEverything);
		using var probes = new UdpReachabilityProbeService(FreePort.Udp());
		using var registry = new ProbeRegistryHost(probes);

		var log = new List<string>();
		var verifier = new HostReachabilityVerifier(new HttpClient(), log.Add);
		HostReachabilityResult result = await verifier.VerifyAsync(registry.BaseUrl, IPAddress.Loopback.ToString(), gamePort.Port, (_, _) => true);

		Assert.That(result.Failure, Is.EqualTo(HostReachabilityFailure.None), string.Join(" | ", log));
		Assert.That(result.Verified, Is.True);
		Assert.That(result.PublicIp, Is.EqualTo(IPAddress.Loopback.ToString()));
		Assert.That(result.PublicPort, Is.EqualTo(gamePort.Port));
		Assert.That(probes.TryReadVerified(result.ProbeId, out _), Is.True);
	}

	[Test]
	public async Task AHostWhoseStatedAddressIsWrongIsStillMeasuredAtTheAddressTheNodeSees()
	{
		using var gamePort = new FakeGamePort(FakeGamePort.Behaviour.AnswerEverything);
		using var probes = new UdpReachabilityProbeService(FreePort.Udp());
		using var registry = new ProbeRegistryHost(probes);

		// A host that measured its own public address badly still hosts on the address the network shows, so a wrong
		// measurement is repaired instead of being turned into a refusal that nobody can act on.
		var verifier = new HostReachabilityVerifier(new HttpClient(), _ => { });
		HostReachabilityResult result = await verifier.VerifyAsync(registry.BaseUrl, "198.51.100.4", gamePort.Port, (_, _) => true);

		Assert.That(result.Verified, Is.True, result.Detail);
		Assert.That(result.PublicIp, Is.EqualTo(IPAddress.Loopback.ToString()));
		Assert.That(result.PublicPort, Is.EqualTo(gamePort.Port));
	}

	[Test]
	public async Task AHostWhoseGamePortCannotBeReachedIsRefused()
	{
		using var gamePort = new FakeGamePort(FakeGamePort.Behaviour.Silent);
		using var probes = new UdpReachabilityProbeService(FreePort.Udp());
		using var registry = new ProbeRegistryHost(probes);

		var verifier = new HostReachabilityVerifier(new HttpClient(), _ => { });
		HostReachabilityResult result = await verifier.VerifyAsync(registry.BaseUrl, IPAddress.Loopback.ToString(), gamePort.Port, (_, _) => true);

		Assert.That(result.Failure, Is.EqualTo(HostReachabilityFailure.ProbeUnanswered));
		Assert.That(result.Verified, Is.False);
		Assert.That(result.Detail, Does.Contain(gamePort.Port.ToString()));
	}

	[Test]
	public async Task AHostBehindARestrictedRouterIsVerifiedByPunchingFirst()
	{
		using var gamePort = new FakeGamePort(FakeGamePort.Behaviour.AnswerOnlySolicited);
		using var probes = new UdpReachabilityProbeService(FreePort.Udp());
		using var registry = new ProbeRegistryHost(probes);

		int punches = 0;

		bool Punch(string address, int port)
		{
			Interlocked.Increment(ref punches);
			gamePort.Punch(new IPEndPoint(IPAddress.Parse(address), port));
			return true;
		}

		var verifier = new HostReachabilityVerifier(new HttpClient(), _ => { });
		HostReachabilityResult result = await verifier.VerifyAsync(registry.BaseUrl, IPAddress.Loopback.ToString(), gamePort.Port, Punch);

		Assert.That(result.Verified, Is.True, "a host that can only be reached after punching must be allowed to host");
		Assert.That(punches, Is.GreaterThan(0), "the host must punch, because that is what opens the router for the probes");
	}

	[Test]
	public async Task ARegistryThatCannotBeReachedRefusesTheLobbyInsteadOfGuessing()
	{
		var verifier = new HostReachabilityVerifier(new HttpClient { Timeout = TimeSpan.FromSeconds(3) }, _ => { });
		HostReachabilityResult result = await verifier.VerifyAsync($"http://127.0.0.1:{FreePort.Tcp()}", string.Empty, 8999, (_, _) => true);

		Assert.That(result.Failure, Is.EqualTo(HostReachabilityFailure.RegistryUnreachable));
		Assert.That(result.Verified, Is.False);
	}

	private static async Task<ReachabilityReport> PunchUntilAnsweredAsync(FakeGamePort gamePort, UdpReachabilityProbeService probes, ReachabilityTicket ticket)
	{
		DateTime deadline = DateTime.UtcNow + TimeSpan.FromSeconds(6);
		ReachabilityReport? report = null;

		while (DateTime.UtcNow < deadline)
		{
			gamePort.Punch(new IPEndPoint(IPAddress.Loopback, probes.ProbePort));
			report = probes.Read(ticket.ProbeId);
			if (report is { Answered: true }) return report;
			await Task.Delay(50);
		}

		return report ?? new ReachabilityReport(ticket.ProbeId, ReachabilityStates.Silent, false, string.Empty, 0, string.Empty, 0, string.Empty, 0);
	}
}
