using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Text.Json;
using System.Threading.Tasks;
using NUnit.Framework;
using Realm.AdminServer.Models;
using Realm.Shared.Reachability;

namespace Realm.Network.Tests;

/// <summary>
///     Exercises the IPv6 half of the lobby protocol at contract level: an endpoint list that crosses the wire intact, a
///     check that is only ever made in the family of the endpoint it measures, and the one choice rule a joining screen
///     and a registry node both run.
/// </summary>
[TestFixture]
public class Ipv6EndpointTests
{
	private const int WindowMilliseconds = 1500;

	private static readonly JsonSerializerOptions NodeOptions = new(JsonSerializerDefaults.Web);
	private static readonly JsonSerializerOptions ClientOptions = new() { PropertyNameCaseInsensitive = true };

	[Test]
	public async Task AnIPv6GamePortIsReachedAndMeasuredInItsOwnFamily()
	{
		Assume.That(Socket.OSSupportsIPv6, "this machine has no IPv6 stack to check an endpoint on");

		using var gamePort = new FakeGamePort(FakeGamePort.Behaviour.AnswerEverything, IPAddress.IPv6Loopback);
		using var probes = new UdpReachabilityProbeService(FreePort.Udp(), string.Empty, IPAddress.IPv6Loopback.ToString());

		ReachabilityTicket ticket = probes.Begin(gamePort.Address, gamePort.Port, IPAddress.IPv6Loopback, 5555, WindowMilliseconds);
		ReachabilityReport report = await ProbePolling.WaitForResultAsync(probes, ticket, TimeSpan.FromSeconds(6));

		Assert.That(ticket.Family, Is.EqualTo("ipv6"));
		Assert.That(ticket.ProbeIp, Is.EqualTo(IPAddress.IPv6Loopback.ToString()), "the ticket has to name a probe address in the family it is checking");
		Assert.That(report.State, Is.EqualTo(ReachabilityStates.Answered));
		Assert.That(report.Family, Is.EqualTo("ipv6"));
		Assert.That(report.ObservedIp, Is.EqualTo(IPAddress.IPv6Loopback.ToString()));
		Assert.That(report.ObservedPort, Is.EqualTo(gamePort.Port));
	}

	[Test]
	public async Task AnIPv6PortThatRefusesUnsolicitedTrafficAnswersOnceTheHostPunches()
	{
		Assume.That(Socket.OSSupportsIPv6, "this machine has no IPv6 stack to check an endpoint on");

		using var gamePort = new FakeGamePort(FakeGamePort.Behaviour.AnswerOnlySolicited, IPAddress.IPv6Loopback);
		using var probes = new UdpReachabilityProbeService(FreePort.Udp(), string.Empty, IPAddress.IPv6Loopback.ToString());

		// The punch a v6 host sends names the check, and the check learns the endpoint the punch came from. It is the same
		// datagram an IPv4 host sends: only the family of the socket it leaves from is different.
		ReachabilityTicket ticket = probes.Begin(gamePort.Address, gamePort.Port, IPAddress.IPv6Loopback, 5555, WindowMilliseconds);
		DateTime deadline = DateTime.UtcNow + TimeSpan.FromSeconds(6);
		ReachabilityReport? report = null;

		while (DateTime.UtcNow < deadline)
		{
			gamePort.Punch(new IPEndPoint(IPAddress.IPv6Loopback, probes.ProbePort), ticket.ProbeId);
			report = probes.Read(ticket.ProbeId);
			if (report is { Answered: true }) break;
			await Task.Delay(50);
		}

		Assert.That(report?.Answered, Is.True, "a punch over IPv6 has to open the check the same way it does over IPv4");
		Assert.That(report!.ObservedPort, Is.EqualTo(gamePort.Port));
	}

	[Test]
	public async Task AnIPv4ProbeAddressIsNeverUsedToMeasureAnIPv6Endpoint()
	{
		Assume.That(Socket.OSSupportsIPv6, "this machine has no IPv6 stack to construct the case");

		using var gamePort = new FakeGamePort(FakeGamePort.Behaviour.AnswerEverything, IPAddress.IPv6Loopback);

		// The node states only an IPv4 probe address, which is a node that cannot reach an IPv6 endpoint at all. The
		// rehearsal has to say so rather than probe the endpoint from a socket that cannot get there.
		using var probes = new UdpReachabilityProbeService(FreePort.Udp(), IPAddress.Loopback.ToString(), string.Empty);
		using var registry = new ProbeRegistryHost(probes);

		var verifier = new HostReachabilityVerifier(new HttpClient(), _ => { });
		HostReachabilityResult result = await verifier.VerifyAsync(registry.BaseUrl, IPAddress.IPv6Loopback.ToString(), gamePort.Port, (_, _, _) => true);

		Assert.That(result.Failure, Is.EqualTo(HostReachabilityFailure.ProbeFamilyUnavailable));
		Assert.That(result.Verified, Is.False);
	}

	[Test]
	public void TheEndpointListSurvivesRegistrationAndTheJoinAnswer()
	{
		var dualStack = new List<LobbyEndpointRegistration>
		{
			new("203.0.113.9", 8999, "ipv4", "probe-ipv4"),
			new("2001:db8::1", 8999, "ipv6", "probe-ipv6")
		};

		var request = new RegisterRequest("Melee", 8999, "Open", "203.0.113.9", 8, 1, 20, Endpoints: dualStack);
		string requestJson = JsonSerializer.Serialize(request, NodeOptions);
		RegisterRequest? readBack = JsonSerializer.Deserialize<RegisterRequest>(requestJson, ClientOptions);

		Assert.That(readBack, Is.Not.Null);
		Assert.That(readBack!.Endpoints, Is.Not.Null);
		Assert.That(readBack.Endpoints!.Count, Is.EqualTo(2));
		Assert.That(readBack.Endpoints[0].Address, Is.EqualTo("203.0.113.9"));
		Assert.That(readBack.Endpoints[1].Family, Is.EqualTo("ipv6"));
		Assert.That(readBack.Endpoints[1].ProbeId, Is.EqualTo("probe-ipv6"));

		var answer = new JoinResponseDto("203.0.113.9", 8999, "192.168.1.42", 8999, Endpoints: new List<LobbyEndpointInfo>
		{
			new("203.0.113.9", 8999, "ipv4", true, "203.0.113.9", 8999),
			new("2001:db8::1", 8999, "ipv6", true, "2001:db8::1", 8999)
		});

		string answerJson = JsonSerializer.Serialize(answer, NodeOptions);
		JoinResponseDto? answerBack = JsonSerializer.Deserialize<JoinResponseDto>(answerJson, ClientOptions);

		Assert.That(answerBack, Is.Not.Null);
		Assert.That(answerBack!.Endpoints!.Count, Is.EqualTo(2));
		Assert.That(answerBack.Endpoints![1].FamilyValue, Is.EqualTo(LobbyAddressFamily.IPv6));
		Assert.That(answerBack.Endpoints![1].Describe(), Is.EqualTo("[2001:db8::1]:8999"));
	}

	[Test]
	public void AV6OnlyLobbySurvivesRegistrationWithOneEndpoint()
	{
		var v6Only = new List<LobbyEndpointRegistration> { new("2001:db8::1", 8999, "ipv6", "probe-ipv6") };
		var request = new RegisterRequest("Melee", 8999, "Open", "2001:db8::1", 8, 1, 20, Endpoints: v6Only);

		string json = JsonSerializer.Serialize(request, NodeOptions);
		RegisterRequest? readBack = JsonSerializer.Deserialize<RegisterRequest>(json, ClientOptions);

		Assert.That(readBack!.Endpoints!.Count, Is.EqualTo(1));
		Assert.That(readBack.Endpoints[0].Family, Is.EqualTo("ipv6"));
		Assert.That(LobbyAddresses.FamilyOf(readBack.Endpoints[0].Address), Is.EqualTo(LobbyAddressFamily.IPv6));
	}

	[Test]
	public void AJoinerUsesTheEndpointItSharesAFamilyWith()
	{
		var dualStack = new List<LobbyEndpointInfo>
		{
			new("203.0.113.9", 8999, "ipv4", true),
			new("2001:db8::1", 8999, "ipv6", true)
		};

		LobbyEndpointChoice withIpv6 = LobbyEndpointSelection.Choose(dualStack, hasIpv4: true, hasIpv6: true);
		Assert.That(withIpv6.Chosen, Is.True);
		Assert.That(withIpv6.Family, Is.EqualTo(LobbyAddressFamily.IPv6), "a player with IPv6 prefers the lobby's IPv6 endpoint");
		Assert.That(withIpv6.NeedsHolePunch, Is.False, "an IPv6 endpoint is dialled directly");

		LobbyEndpointChoice ipv4OnlyScreen = LobbyEndpointSelection.Choose(dualStack, hasIpv4: true, hasIpv6: false);
		Assert.That(ipv4OnlyScreen.Family, Is.EqualTo(LobbyAddressFamily.IPv4));
		Assert.That(ipv4OnlyScreen.NeedsHolePunch, Is.True, "the IPv4 path keeps its punch");

		var v6Only = new List<LobbyEndpointInfo> { new("2001:db8::1", 8999, "ipv6", true) };
		LobbyEndpointChoice dualStackScreen = LobbyEndpointSelection.Choose(v6Only, hasIpv4: true, hasIpv6: true);
		Assert.That(dualStackScreen.Family, Is.EqualTo(LobbyAddressFamily.IPv6));
	}

	[Test]
	public void AV6OnlyLobbyIsAFamilyMismatchForAnIPv4OnlyScreen()
	{
		var v6Only = new List<LobbyEndpointInfo> { new("2001:db8::1", 8999, "ipv6", true) };
		LobbyEndpointChoice choice = LobbyEndpointSelection.Choose(v6Only, hasIpv4: true, hasIpv6: false);

		Assert.That(choice.Chosen, Is.False);
		Assert.That(choice.Failure, Is.EqualTo(LobbyEndpointSelectionFailure.AddressFamilyMismatch));
		Assert.That(choice.LobbyFamilies, Is.EqualTo(new[] { LobbyAddressFamily.IPv6 }), "the refusal has to name the family the lobby offers");
	}

	[Test]
	public void AnIPv4OnlyLobbyIsAFamilyMismatchForAScreenWithNoIpv4()
	{
		var endpoints = new[]
		{
			new LobbyEndpointInfo("203.0.113.9", 8999, "ipv4", true, "203.0.113.9", 8999)
		};

		LobbyEndpointChoice choice = LobbyEndpointSelection.Choose(endpoints, hasIpv4: false, hasIpv6: true);
		Assert.That(choice.Chosen, Is.False, "a screen with no IPv4 address of its own cannot reach an IPv4 endpoint");
		Assert.That(choice.Failure, Is.EqualTo(LobbyEndpointSelectionFailure.AddressFamilyMismatch));
		Assert.That(choice.LobbyFamilies, Is.EqualTo(new[] { LobbyAddressFamily.IPv4 }), "the refusal names the family it is missing");

		Assert.That(LobbyEndpointSelection.Choose(endpoints, hasIpv4: true, hasIpv6: false).Family, Is.EqualTo(LobbyAddressFamily.IPv4));
	}

	[Test]
	public void AnUnverifiedEndpointIsNotOffered()
	{
		var offered = new List<LobbyEndpointInfo>
		{
			new("203.0.113.9", 8999, "ipv4", false),
			new("2001:db8::1", 8999, "ipv6", true)
		};

		LobbyEndpointChoice choice = LobbyEndpointSelection.Choose(offered, hasIpv4: true, hasIpv6: true);
		Assert.That(choice.Family, Is.EqualTo(LobbyAddressFamily.IPv6), "an endpoint the registry never reached must not be dialled");

		var nothingVerified = new List<LobbyEndpointInfo> { new("203.0.113.9", 8999, "ipv4", false) };
		Assert.That(LobbyEndpointSelection.Choose(nothingVerified, true, true).Failure, Is.EqualTo(LobbyEndpointSelectionFailure.NoEndpointOffered));
	}

	[Test]
	public void LinkLocalAddressesAreNeverOffered()
	{
		Assert.That(LobbyAddresses.IsAdvertisable(IPAddress.Parse("fe80::1")), Is.False, "a link-local address needs a scope identifier no other player holds");
		Assert.That(LobbyAddresses.IsAdvertisable(IPAddress.Parse("2001:db8::1")), Is.True);
		Assert.That(LobbyAddresses.IsAdvertisable(IPAddress.Parse("fd00::1")), Is.True, "a unique local address reaches the site this machine is on");
		Assert.That(LobbyAddresses.IsAdvertisable(IPAddress.Parse("127.0.0.1")), Is.False);
		Assert.That(LobbyAddresses.IsAdvertisable(IPAddress.Parse("169.254.4.5")), Is.False);
		Assert.That(LobbyAddresses.IsAdvertisable(IPAddress.Parse("192.168.1.42")), Is.True);
		Assert.That(LobbyAddresses.IsAdvertisable(IPAddress.Parse("::ffff:192.168.1.42")), Is.True, "an IPv4 address carried in an IPv6 envelope is still an address a player can be given");
		Assert.That(LobbyAddresses.FamilyOf(IPAddress.Parse("::ffff:192.168.1.42")), Is.EqualTo(LobbyAddressFamily.IPv4), "a mapped address is dialled over IPv4, so it is an IPv4 endpoint and needs the IPv4 mapping and punch");
		Assert.That(LobbyAddresses.FamilyOf(IPAddress.Parse("::ffff:203.0.113.9")), Is.EqualTo(LobbyAddressFamily.IPv4), "a mapped public address is a public IPv4 endpoint, not a global IPv6 one");
		Assert.That(LobbyAddresses.ScopeOf(IPAddress.Parse("::ffff:203.0.113.9")), Is.EqualTo(LobbyAddressScope.Global));
	}

	[Test]
	public void AnIPv6AddressIsWrittenWithBracketsEverywhere()
	{
		Assert.That(LobbyAddresses.Describe("2001:db8::1", 8999), Is.EqualTo("[2001:db8::1]:8999"));
		Assert.That(LobbyAddresses.Describe("[2001:db8::1]", 8999), Is.EqualTo("[2001:db8::1]:8999"));
		Assert.That(LobbyAddresses.Describe("203.0.113.9", 8999), Is.EqualTo("203.0.113.9:8999"));
		Assert.That(LobbyAddresses.Describe(IPAddress.IPv6Loopback, 8999), Is.EqualTo("[::1]:8999"));
	}

	[Test]
	public void OnlyAnIPv4EndpointNeedsARouterMapping()
	{
		Assert.That(LobbyAddresses.NeedsRouterMapping(LobbyAddressFamily.IPv4), Is.True);
		Assert.That(LobbyAddresses.NeedsRouterMapping(LobbyAddressFamily.IPv6), Is.False, "an IPv6 endpoint has no translation in front of it");
		Assert.That(LobbyAddresses.NeedsHolePunch(LobbyAddressFamily.IPv6), Is.False);

		// The host's guard is built from this, so a machine holding nothing but IPv6 has nothing to map and is never told
		// its router failed at something it was never asked to do.
		var candidates = new List<LobbyEndpointCandidate> { new(IPAddress.Parse("2001:db8::1"), LobbyAddressFamily.IPv6, LobbyAddressScope.Global) };
		Assert.That(candidates.Exists(candidate => candidate.NeedsRouterMapping), Is.False);
	}

	[Test]
	public void ADiscoveredEndpointListIsOrderedByHowFarItReaches()
	{
		List<LobbyEndpointCandidate> candidates = LocalEndpointDiscovery.Discover();

		// Whatever this machine holds, the order is the policy: nothing loopback, nothing link-local, global IPv6 before
		// anything else, and a unique local IPv6 address last.
		int lastRank = -1;
		for (int index = 0; index < candidates.Count; index++)
		{
			LobbyEndpointCandidate candidate = candidates[index];
			Assert.That(IPAddress.IsLoopback(candidate.Address), Is.False, "a loopback address is never offered to another player");
			Assert.That(candidate.Address.IsIPv6LinkLocal, Is.False, "a link-local address is never offered to another player");
			Assert.That(LobbyAddresses.FamilyOf(candidate.Address), Is.EqualTo(candidate.Family));

			int rank = candidate.Family == LobbyAddressFamily.IPv6
				? (candidate.Scope == LobbyAddressScope.Global ? 0 : 3)
				: (candidate.Scope == LobbyAddressScope.LocalNetwork ? 1 : 2);

			Assert.That(rank, Is.GreaterThanOrEqualTo(lastRank), "the candidates are ordered by the policy, not by interface order");
			lastRank = rank;
		}
	}

	[Test]
	public void AMachineWithNoIpv4OffersNothingThatNeedsARouter()
	{
		var candidates = new List<LobbyEndpointCandidate>
		{
			new(IPAddress.Parse("2001:db8::5"), LobbyAddressFamily.IPv6, LobbyAddressScope.Global)
		};

		LobbyEndpointOffer[] offers = LobbyEndpointPlan.Build(candidates, 8999, null);

		Assert.That(offers.Length, Is.EqualTo(1));
		Assert.That(offers[0].Family, Is.EqualTo(LobbyAddressFamily.IPv6));
		Assert.That(offers[0].Describe(), Is.EqualTo("[2001:db8::5]:8999"));
		Assert.That(LobbyEndpointPlan.NeedsRouterMapping(offers), Is.False, "an IPv6-only host has no mapping to ask a router for and must not be told one failed");
	}

	[Test]
	public void ADualStackMachineOffersBothFamiliesWithTheGlobalIpv6First()
	{
		var candidates = new List<LobbyEndpointCandidate>
		{
			new(IPAddress.Parse("192.168.1.42"), LobbyAddressFamily.IPv4, LobbyAddressScope.LocalNetwork),
			new(IPAddress.Parse("2001:db8::5"), LobbyAddressFamily.IPv6, LobbyAddressScope.Global),
			new(IPAddress.Parse("fd00::5"), LobbyAddressFamily.IPv6, LobbyAddressScope.LocalNetwork)
		};

		LobbyEndpointOffer[] offers = LobbyEndpointPlan.Build(candidates, 8999, null);

		Assert.That(offers.Length, Is.EqualTo(2), "one endpoint per family");
		Assert.That(offers[0].Family, Is.EqualTo(LobbyAddressFamily.IPv6));
		Assert.That(offers[0].Address.ToString(), Is.EqualTo("2001:db8::5"));
		Assert.That(offers[1].Family, Is.EqualTo(LobbyAddressFamily.IPv4));
		Assert.That(LobbyEndpointPlan.NeedsRouterMapping(offers), Is.True, "the IPv4 endpoint still needs its router mapping");
	}

	[Test]
	public void AnEndpointAFamilyIsTheOnlyOneOfferedEvenWhenMoreAreHeld()
	{
		var candidates = new List<LobbyEndpointCandidate>
		{
			new(IPAddress.Parse("2001:db8::5"), LobbyAddressFamily.IPv6, LobbyAddressScope.Global),
			new(IPAddress.Parse("2001:db8::6"), LobbyAddressFamily.IPv6, LobbyAddressScope.Global)
		};

		LobbyEndpointOffer[] offers = LobbyEndpointPlan.Build(candidates, 8999, null);
		Assert.That(offers.Length, Is.EqualTo(1), "a lobby is offered at one address per family, not at every address a machine holds");
	}

	[Test]
	public void AnOfferStatesWhetherTheNodeAboutToPublishItCanCheckIt()
	{
		var candidates = new List<LobbyEndpointCandidate>
		{
			new(IPAddress.Parse("2001:db8::5"), LobbyAddressFamily.IPv6, LobbyAddressScope.Global),
			new(IPAddress.Parse("192.168.1.42"), LobbyAddressFamily.IPv4, LobbyAddressScope.LocalNetwork)
		};

		var nodeWithNoIpv6 = new RegistryCapabilities(RegistryCapabilities.EndpointListProtocolVersion, new[] { "ipv4" });
		LobbyEndpointOffer[] offers = LobbyEndpointPlan.Build(candidates, 8999, nodeWithNoIpv6);

		Assert.That(offers[0].CanBeChecked, Is.False);
		Assert.That(offers[1].CanBeChecked, Is.True);
	}

	[Test]
	public void ACapabilityStatementDecidesWhatANodeCanCheck()
	{
		var node = new RegistryCapabilities(RegistryCapabilities.EndpointListProtocolVersion, new[] { "ipv4", "ipv6" });
		Assert.That(node.CanCheck(LobbyAddressFamily.IPv6), Is.True);
		Assert.That(node.CanCheck(LobbyAddressFamily.IPv4), Is.True);
		Assert.That(node.CanCheck(LobbyAddressFamily.Unknown), Is.False);

		var nodeWithNoIpv6 = new RegistryCapabilities(RegistryCapabilities.EndpointListProtocolVersion, new[] { "ipv4" });
		Assert.That(nodeWithNoIpv6.CanCheck(LobbyAddressFamily.IPv6), Is.False);

		var oldNode = new RegistryCapabilities(RegistryCapabilities.SingleEndpointProtocolVersion, Array.Empty<string>());
		Assert.That(oldNode.ChecksEndpointList, Is.False);
		Assert.That(oldNode.CanCheck(LobbyAddressFamily.IPv4), Is.True, "the single-endpoint protocol only ever measured IPv4");
		Assert.That(oldNode.CanCheck(LobbyAddressFamily.IPv6), Is.False);
	}

	[Test]
	public void AnOlderNodeAnswerIsTheSingleEndpointItStates()
	{
		LobbyJoinAnswer? answer = LobbyJoinAnswers.TryParse("{\"hostIP\":\"203.0.113.9\",\"hostPort\":8999,\"localIP\":\"192.168.0.240\"}");

		Assert.That(answer, Is.Not.Null, "a node from before endpoint lists is not an unreadable answer");
		Assert.That(answer!.Value.Endpoints, Is.Empty);

		IReadOnlyList<LobbyEndpointInfo> endpoints = answer.Value.EndpointsOrStatedAddress();
		Assert.That(endpoints.Count, Is.EqualTo(1));
		Assert.That(endpoints[0].FamilyValue, Is.EqualTo(LobbyAddressFamily.IPv4), "the two fields an older node states have always been the IPv4 endpoint");
		Assert.That(endpoints[0].Describe(), Is.EqualTo("203.0.113.9:8999"));
		Assert.That(answer.Value.LocalAddress, Is.EqualTo("192.168.0.240"));
	}

	[Test]
	public void AV6OnlyJoinAnswerStatesTheEndpointAndItsFamily()
	{
		LobbyJoinAnswer? answer = LobbyJoinAnswers.TryParse(
			"{\"HostIP\":\"2001:db8::1\",\"HostPort\":8999,\"LocalIP\":null,\"Endpoints\":[{\"Address\":\"2001:db8::1\",\"Port\":8999,\"Family\":\"ipv6\",\"Verified\":true,\"ObservedAddress\":\"2001:db8::1\",\"ObservedPort\":8999}]}");

		Assert.That(answer, Is.Not.Null);
		IReadOnlyList<LobbyEndpointInfo> endpoints = answer!.Value.EndpointsOrStatedAddress();
		Assert.That(endpoints.Count, Is.EqualTo(1), "the list is read instead of being rebuilt from the older fields");
		Assert.That(endpoints[0].FamilyValue, Is.EqualTo(LobbyAddressFamily.IPv6));
		Assert.That(endpoints[0].Describe(), Is.EqualTo("[2001:db8::1]:8999"));

		LobbyEndpointChoice choice = LobbyEndpointSelection.Choose(endpoints, hasIpv4: true, hasIpv6: false);
		Assert.That(choice.Failure, Is.EqualTo(LobbyEndpointSelectionFailure.AddressFamilyMismatch), "a screen without IPv6 has nowhere to dial in that answer");
	}

	[Test]
	public void ADualStackJoinAnswerKeepsBothEndpointsInTheFamilyEachBelongsTo()
	{
		LobbyJoinAnswer? answer = LobbyJoinAnswers.TryParse(
			"{\"hostIP\":\"203.0.113.9\",\"hostPort\":8999,\"endpoints\":[" +
			"{\"address\":\"2001:db8::1\",\"port\":8999,\"family\":\"ipv6\",\"verified\":true}," +
			"{\"address\":\"203.0.113.9\",\"port\":8999,\"family\":\"ipv4\",\"verified\":true}]}");

		Assert.That(answer, Is.Not.Null);
		IReadOnlyList<LobbyEndpointInfo> endpoints = answer!.Value.EndpointsOrStatedAddress();
		Assert.That(endpoints.Count, Is.EqualTo(2));
		Assert.That(endpoints[0].FamilyValue, Is.EqualTo(LobbyAddressFamily.IPv6));
		Assert.That(endpoints[1].FamilyValue, Is.EqualTo(LobbyAddressFamily.IPv4));

		Assert.That(LobbyEndpointSelection.Choose(endpoints, hasIpv4: true, hasIpv6: true).Family, Is.EqualTo(LobbyAddressFamily.IPv6), "a dual-stack screen prefers the IPv6 endpoint");
		Assert.That(LobbyEndpointSelection.Choose(endpoints, hasIpv4: true, hasIpv6: false).Family, Is.EqualTo(LobbyAddressFamily.IPv4), "an IPv4-only screen still has an endpoint to dial");
	}

	[Test]
	public void AJoinAnswerThatSaysNothingUsableIsUnreadable()
	{
		Assert.That(LobbyJoinAnswers.TryParse(string.Empty), Is.Null);
		Assert.That(LobbyJoinAnswers.TryParse("not json"), Is.Null);
		Assert.That(LobbyJoinAnswers.TryParse("{\"status\":\"ok\"}"), Is.Null, "an answer that names no address is not an address");
		Assert.That(LobbyJoinAnswers.TryParse("[1,2,3]"), Is.Null);

		LobbyJoinAnswer? filtered = LobbyJoinAnswers.TryParse(
			"{\"hostIP\":\"\",\"hostPort\":0,\"endpoints\":[{\"address\":\"\",\"port\":0,\"family\":\"ipv4\",\"verified\":true}]}");
		Assert.That(filtered, Is.Null, "an endpoint with no address is not something to dial");
	}

	[Test]
	public void TheGamePortAnswersItsOwnLoopbackQuestion()
	{
		using var port = new FakeGamePort(FakeGamePort.Behaviour.AnswerEverything);
		Assert.That(LobbyPortCheck.AnswersAsync(IPAddress.Loopback, port.Port, 3000).GetAwaiter().GetResult(), Is.True, "the check has to be able to say yes");
		Assert.That(LobbyPortCheck.AnswersAsync(IPAddress.Loopback, FreePort.Udp(), 300).GetAwaiter().GetResult(), Is.False, "and to say no when nothing is listening");
	}

	[Test]
	public async Task ANodeStatesTheFamiliesItCanCheck()
	{
		using var probes = new UdpReachabilityProbeService(FreePort.Udp());
		using var registry = new ProbeRegistryHost(probes);

		using var httpClient = new HttpClient();
		string json = await httpClient.GetStringAsync($"{registry.BaseUrl}{ReachabilityRoutes.Capabilities}");
		RegistryCapabilities? capabilities = JsonSerializer.Deserialize<RegistryCapabilities>(json, ClientOptions);

		Assert.That(capabilities, Is.Not.Null);
		Assert.That(capabilities!.ProtocolVersion, Is.EqualTo(RegistryCapabilities.EndpointListProtocolVersion));
		Assert.That(capabilities.CanCheck(LobbyAddressFamily.IPv4), Is.True);
		if (Socket.OSSupportsIPv6) Assert.That(capabilities.CanCheck(LobbyAddressFamily.IPv6), Is.True, "a node with an IPv6 socket states that it can check IPv6 endpoints");
	}
}
