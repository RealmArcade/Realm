using System;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using NUnit.Framework;
using Realm.Shared.Reachability;

namespace Realm.Network.Tests;

/// <summary>
///     The reachability check, measured against the authority this game actually ships. These are the tests that prove the
///     hand-written opening packet is answered by real ENet, and that the check separates a game port other players can
///     reach from the one machine-specific failure this feature exists for: a host that is told it succeeded while nobody
///     can get in.
/// </summary>
[TestFixture]
public class RealAuthorityReachabilityTests
{
	[Test]
	public async Task TheChecksOpeningPacketIsAnsweredByTheRealAuthority()
	{
		RealAuthorityHost.IgnoreWhenUnavailable();

		int port = FreePort.Udp();
		await using RealAuthorityHost authority = await RealAuthorityHost.StartLobbyAsync(port);
		using var probes = new UdpReachabilityProbeService(FreePort.Udp());

		ReachabilityTicket ticket = probes.Begin(IPAddress.Loopback, port, IPAddress.Loopback, 5555, 2000);
		ReachabilityReport report = await ProbePolling.WaitForResultAsync(probes, ticket, TimeSpan.FromSeconds(8));

		Assert.That(report.Answered, Is.True, $"the authority holding a lobby on {port} did not answer the reachability check");
		Assert.That(report.ObservedPort, Is.EqualTo(port));
	}

	[Test]
	public async Task ALobbyOnTheRealAuthorityIsVerifiedBeforeItIsPublished()
	{
		RealAuthorityHost.IgnoreWhenUnavailable();

		int port = FreePort.Udp();
		await using RealAuthorityHost authority = await RealAuthorityHost.StartLobbyAsync(port);
		using var probes = new UdpReachabilityProbeService(FreePort.Udp());
		using var registry = new ProbeRegistryHost(probes);

		var verifier = new HostReachabilityVerifier(new HttpClient(), _ => { });
		HostReachabilityResult result = await verifier.VerifyAsync(registry.BaseUrl, IPAddress.Loopback.ToString(), port, PunchLikeAJoiningPlayer());

		Assert.That(result.Verified, Is.True, result.Detail);
		Assert.That(result.PublicPort, Is.EqualTo(port));
		Assert.That(ReachabilityEndpointHandlers.IsLobbyVerified(probes, result.ProbeId, out _), Is.True, "the registry node must accept this lobby");
	}

	[Test]
	public async Task AHostAdvertisedOnAPortNothingAnswersIsRefusedAndPublishesNothing()
	{
		RealAuthorityHost.IgnoreWhenUnavailable();

		int port = FreePort.Udp();
		await using RealAuthorityHost authority = await RealAuthorityHost.StartLobbyAsync(port);
		using var probes = new UdpReachabilityProbeService(FreePort.Udp());
		using var registry = new ProbeRegistryHost(probes);

		// This is the reported failure reproduced without touching a single setting: the lobby is up and healthy on its own
		// port, but the endpoint that would be published is one no player can reach.
		int unreachablePort = FreePort.Udp();
		var verifier = new HostReachabilityVerifier(new HttpClient(), _ => { });
		HostReachabilityResult result = await verifier.VerifyAsync(registry.BaseUrl, IPAddress.Loopback.ToString(), unreachablePort, PunchLikeAJoiningPlayer());

		Assert.That(result.Failure, Is.EqualTo(HostReachabilityFailure.ProbeUnanswered));
		Assert.That(result.Verified, Is.False);
		Assert.That(ReachabilityEndpointHandlers.IsLobbyVerified(probes, result.ProbeId, out _), Is.False, "an unanswered check must never let a lobby be published");
	}

	/// <summary>
	///     A host's authority sends a datagram from its game port towards the peer that is about to join, which is what opens
	///     a router that drops unsolicited traffic. The screen drives that through its lobby command; this sends the same
	///     datagram so the check is rehearsed the way it runs on a machine.
	/// </summary>
	private static Func<string, int, bool> PunchLikeAJoiningPlayer()
	{
		System.Net.Sockets.UdpClient? punchSocket = null;

		return (address, port) =>
		{
			punchSocket ??= new System.Net.Sockets.UdpClient(new IPEndPoint(IPAddress.Loopback, 0));
			punchSocket.Send("PUNCH"u8.ToArray(), new IPEndPoint(IPAddress.Parse(address), port));
			return true;
		};
	}
}
