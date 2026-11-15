using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Godot;
using Realm.Client.Services;
using Realm.Shared.Reachability;

namespace Realm.Client.Network;

/// <summary>What a lobby has to be published with: the endpoints a registry node verified, and the ones it refused.</summary>
/// <param name="EndpointRegistrations">The endpoints to register the lobby with, one per family it was measured in.</param>
/// <param name="Verified">The endpoints that answered, in the order they were measured.</param>
/// <param name="UncheckedFamilies">The families this lobby offers that the registry node has no socket to check.</param>
/// <param name="FirstFailure">What the first endpoint that did not verify reported, for a lobby that verified none.</param>
public readonly record struct LobbyReachabilitySet(
	List<LobbyEndpointRegistration> EndpointRegistrations,
	List<LobbyEndpointInfo> Verified,
	List<string> UncheckedFamilies,
	HostReachabilityResult? FirstFailure);

/// <summary>
///     Where a joining player dials a lobby, and what that path needs. The family is carried because everything after the
///     choice depends on it: an IPv4 endpoint is punched and a connection over it is stated as IPv4, and an IPv6 endpoint
///     is dialled directly.
/// </summary>
/// <param name="Address">The address to dial.</param>
/// <param name="Port">The port to dial.</param>
/// <param name="Family">The family of the address.</param>
/// <param name="NeedsPunch">Whether a hole punch has to be sent before the connection can be made.</param>
/// <param name="Failure">Why there is nowhere to dial, or empty when there is.</param>
public readonly record struct HostConnection(string Address, int Port, LobbyAddressFamily Family, bool NeedsPunch, string Failure)
{
	/// <summary>Whether this states an address to dial.</summary>
	public bool Chosen => Failure.Length == 0 && Address.Length > 0 && Port > 0;
}

/// <summary>
///     The address half of hosting a lobby: what this machine can offer, what a registry node is able to check, and which
///     of the offered endpoints were actually reached. An IPv4 endpoint goes through the measurement and the punch this
///     game has always used, because a router stands in front of it; an IPv6 endpoint is offered as the address this
///     machine holds, because IPv6 has no address translation and therefore nothing to measure, map or punch.
/// </summary>
public partial class LobbyManager
{
	private const int CapabilitiesTimeoutMilliseconds = 8000;

	/// <summary>The endpoints this lobby is offered at, best family first, and whether each can be checked by the node.</summary>
	private LobbyEndpointOffer[] _offeredEndpoints = Array.Empty<LobbyEndpointOffer>();

	/// <summary>The IPv6 endpoint this lobby offers, or nothing when this machine holds no address another player can use.</summary>
	private LobbyEndpointOffer? _offeredIpv6Endpoint => FirstOffer(LobbyAddressFamily.IPv6);

	/// <summary>Whether this machine holds an IPv4 address, which is what makes a router mapping worth asking for.</summary>
	private bool _offersIpv4 => FirstOffer(LobbyAddressFamily.IPv4) != null;

	private LobbyEndpointOffer? FirstOffer(LobbyAddressFamily family)
	{
		for (int index = 0; index < _offeredEndpoints.Length; index++)
		{
			if (_offeredEndpoints[index].Family == family) return _offeredEndpoints[index];
		}

		return null;
	}

	/// <summary>
	///     Reads the addresses this machine holds and decides what the lobby can be offered over. It runs once per attempt
	///     to publish, rather than being remembered, because a player can enable or lose an interface between attempts and
	///     a lobby published at an address that has since gone is worse than one that was never listed.
	/// </summary>
	/// <param name="port">The port the lobby listens on.</param>
	/// <param name="capabilities">What the registry node can check, or nothing before it has been asked.</param>
	private void DiscoverOfferedEndpoints(int port, RegistryCapabilities? capabilities = null) =>
		_offeredEndpoints = LobbyEndpointPlan.Build(LocalEndpointDiscovery.Discover(), port, capabilities);

	/// <summary>
	///     Asks the registry node what it can check. A node that does not answer speaks the single-endpoint protocol this
	///     game used before IPv6 endpoints existed, which only ever measured IPv4 - so the absence of an answer is read as
	///     exactly that, and never as "nothing is wrong".
	/// </summary>
	/// <param name="registryUrl">The registry node about to publish the lobby.</param>
	/// <param name="cancellationToken">Cancels the question when the attempt is abandoned.</param>
	/// <returns>What the node can check.</returns>
	private async Task<RegistryCapabilities> ReadRegistryCapabilitiesAsync(string registryUrl, CancellationToken cancellationToken)
	{
		var singleEndpoint = new RegistryCapabilities(RegistryCapabilities.SingleEndpointProtocolVersion, new[] { LobbyAddresses.FamilyName(LobbyAddressFamily.IPv4) });

		try
		{
			using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
			timeout.CancelAfter(CapabilitiesTimeoutMilliseconds);

			string url = $"{registryUrl.TrimEnd('/')}{ReachabilityRoutes.Capabilities}";
			using HttpResponseMessage response = await _httpClient.GetAsync(url, timeout.Token).ConfigureAwait(false);
			if (!response.IsSuccessStatusCode) return singleEndpoint;

			RegistryCapabilities? capabilities = await response.Content.ReadFromJsonAsync<RegistryCapabilities>(Options, timeout.Token).ConfigureAwait(false);
			if (capabilities == null || capabilities.ProtocolVersion <= 0) return singleEndpoint;

			return capabilities;
		}
		catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException or JsonException or NotSupportedException)
		{
			// An older node has no capabilities route at all. That is a node which can only check IPv4, which is what the
			// value returned here states.
			return singleEndpoint;
		}
	}

	/// <summary>
	///     Rehearses the join the lobby is about to offer, once per endpoint it will be published at. Each endpoint is
	///     measured on its own family by the registry node, so an IPv4 endpoint that a router refuses and an IPv6 endpoint
	///     that a carrier filters are judged separately rather than one standing in for the other.
	/// </summary>
	/// <param name="registryUrl">The registry node about to publish the lobby.</param>
	/// <param name="lobbyPort">The game port the lobby listens on.</param>
	/// <param name="capabilities">What the node stated it can check.</param>
	/// <param name="cancellationToken">Cancels the rehearsal.</param>
	/// <returns>What the lobby can be registered with, and what could not be shown.</returns>
	private async Task<LobbyReachabilitySet> VerifyPublishedEndpointsAsync(string registryUrl, int lobbyPort, RegistryCapabilities capabilities, CancellationToken cancellationToken)
	{
		var registrations = new List<LobbyEndpointRegistration>(2);
		var verified = new List<LobbyEndpointInfo>(2);
		var uncheckedFamilies = new List<string>(1);
		HostReachabilityResult? firstFailure = null;

		// IPv4 is measured first because it is the endpoint every client can use, so a dual-stack lobby keeps the endpoint
		// list an older client reads as its primary one.
		if (_offersIpv4)
		{
			await MeasureOfferedEndpointAsync(registryUrl, _hostPublicIp ?? string.Empty, lobbyPort, LobbyAddressFamily.IPv4, capabilities, registrations, verified, uncheckedFamilies, cancellationToken).ConfigureAwait(false);
			firstFailure ??= _lastMeasuredFailure;
		}

		if (_offeredIpv6Endpoint is { } ipv6)
		{
			await MeasureOfferedEndpointAsync(registryUrl, ipv6.Address.ToString(), lobbyPort, LobbyAddressFamily.IPv6, capabilities, registrations, verified, uncheckedFamilies, cancellationToken).ConfigureAwait(false);
			firstFailure ??= _lastMeasuredFailure;
		}


		return new LobbyReachabilitySet(registrations, verified, uncheckedFamilies, firstFailure);
	}

	private HostReachabilityResult? _lastMeasuredFailure;

	private async Task MeasureOfferedEndpointAsync(
		string registryUrl,
		string advertisedAddress,
		int lobbyPort,
		LobbyAddressFamily family,
		RegistryCapabilities capabilities,
		List<LobbyEndpointRegistration> registrations,
		List<LobbyEndpointInfo> verified,
		List<string> uncheckedFamilies,
		CancellationToken cancellationToken)
	{
		_lastMeasuredFailure = null;
		string familyName = LobbyAddresses.FamilyName(family);

		if (!capabilities.CanCheck(family))
		{
			// The node cannot measure this kind of endpoint. The lobby still states it, with no check, so the node's refusal
			// and the log beneath it name the node's limit rather than this machine's network.
			uncheckedFamilies.Add(familyName);
			registrations.Add(new LobbyEndpointRegistration(advertisedAddress, lobbyPort, familyName, string.Empty));
			GameLog.LogToConsole($"[Reachability] {registryUrl} cannot check a {familyName} endpoint, so this lobby's {familyName} address was not measured by it.");
			return;
		}

		ReportHostLobbyStage(HostLobbyStage.VerifyingNetwork);

		var verifier = new HostReachabilityVerifier(_httpClient, message => GameLog.LogToConsole($"[Reachability] {message}"));
		HostReachabilityResult result = await verifier.VerifyAsync(registryUrl, advertisedAddress, lobbyPort, PunchProbeEndpointAsync, cancellationToken).ConfigureAwait(false);
		if (!result.Verified)
		{
			_lastMeasuredFailure = result;
			GameLog.LogError($"[Reachability] {familyName}: {result.Failure}: {result.Detail}");
			return;
		}

		string observedFamily = string.IsNullOrEmpty(result.Family) ? familyName : result.Family;
		registrations.Add(new LobbyEndpointRegistration(result.PublicIp, result.PublicPort, observedFamily, result.ProbeId));
		verified.Add(new LobbyEndpointInfo(result.PublicIp, result.PublicPort, observedFamily, true, result.PublicIp, result.PublicPort));

		GameLog.LogToConsole($"[Reachability] The registry reached this lobby's game port over {familyName} and measured {LobbyAddresses.Describe(result.PublicIp, result.PublicPort)} ({LocalNatType} NAT).");

		if (family != LobbyAddressFamily.IPv4) return;

		// The IPv4 endpoint is the one the router and the punch keep alive, so the values the rest of the lobby screen reads
		// are the ones this measurement produced, exactly as they were before a lobby could be offered over two families.
		_hostPublicIp = result.PublicIp;
		_hostPublicPort = result.PublicPort;
		_routerReachability = RouterReachability.Confirmed;
		_publishedReachability = result;
	}

	/// <summary>
	///     Remembers which endpoints the registry node verified, which is what the lobby room states and what the invite
	///     message offers. The endpoint the router mapping is refreshed through is recorded where it is measured, because
	///     that is the half of the measurement that knows the socket the mapping was made toward.
	/// </summary>
	/// <param name="verified">The endpoints that answered.</param>
	private void ApplyPublishedEndpoints(List<LobbyEndpointInfo> verified) => _publishedEndpoints = verified;

	/// <summary>
	///     The endpoint families this screen holds, as the lobby protocol names them. A joining player states them so the
	///     registry node can relay a punch only when the endpoint the player will dial actually needs one.
	/// </summary>
	/// <returns>The family names this machine can reach other networks over.</returns>
	private static List<string> LocalEndpointFamilyNames()
	{
		var families = new List<string>(2);
		if (LocalEndpointDiscovery.HasIpv4()) families.Add(LobbyAddresses.FamilyName(LobbyAddressFamily.IPv4));
		if (LocalEndpointDiscovery.HasGlobalIpv6()) families.Add(LobbyAddresses.FamilyName(LobbyAddressFamily.IPv6));
		return families;
	}

	/// <summary>
	///     States why a lobby cannot be joined over the families it offers. A lobby reachable over a family this machine has
	///     no address in is a sentence about both ends, said before anything is dialled: waiting out an attempt that cannot
	///     succeed would describe the same fact much later and much less clearly.
	/// </summary>
	/// <param name="choice">The choice that found no endpoint.</param>
	/// <returns>The sentence to show the player.</returns>
	private static string DescribeJoinFamilyFailure(LobbyEndpointChoice choice)
	{
		string offered = DescribeFamilyList(choice.LobbyFamilies);

		return choice.Failure switch
		{
			LobbyEndpointSelectionFailure.AddressFamilyMismatch => string.Format(
				TranslationServer.Translate("This lobby is reachable over {0} only, and this computer has no address in that family. Mixing IPv4 and IPv6 players in one lobby is not supported yet."),
				offered),
			_ => string.Format(TranslationServer.Translate("The lobby server did not state an address this lobby can be reached at ({0})."), offered)
		};
	}

	private static string DescribeFamilyList(IReadOnlyList<LobbyAddressFamily> families)
	{
		if (families.Count == 0) return TranslationServer.Translate("no address family");

		var names = new List<string>(families.Count);
		for (int index = 0; index < families.Count; index++)
		{
			names.Add(families[index] == LobbyAddressFamily.IPv6
				? TranslationServer.Translate("IPv6")
				: TranslationServer.Translate("IPv4"));
		}

		return string.Join(" + ", names);
	}
}
