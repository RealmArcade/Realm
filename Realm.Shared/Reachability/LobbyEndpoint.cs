using System;
using System.Collections.Generic;

namespace Realm.Shared.Reachability;

/// <summary>
///     One endpoint a host asks a registry node to check, so the lobby is published only at an endpoint something outside
///     the host's own network actually reached. Each endpoint is checked on its own family, from the node's socket of that
///     family, and carries the name of the check it is measured by.
/// </summary>
/// <param name="Address">The address the host believes other players will use.</param>
/// <param name="Port">The UDP port the lobby listens on.</param>
/// <param name="Family">The family of the address, as the lobby protocol names it.</param>
/// <param name="ProbeId">The reachability check this endpoint is measured by, or empty when it could not be checked.</param>
public sealed record LobbyEndpointRegistration(string Address, int Port, string Family, string ProbeId);

/// <summary>
///     One endpoint a lobby is reachable at, and what became of it. An endpoint the registry node reached itself is
///     <see cref="Verified" /> and states the endpoint that was observed, which is the one a peer on another network must
///     use; an endpoint it could not reach is stated with the family so a joining player can be told exactly what is
///     missing rather than waiting out a timeout.
/// </summary>
/// <param name="Address">The address the lobby is reached at.</param>
/// <param name="Port">The UDP port the lobby listens on.</param>
/// <param name="Family">The family of the address, as the lobby protocol names it.</param>
/// <param name="Verified">Whether the registry node reached the lobby at this endpoint.</param>
/// <param name="ObservedAddress">The address the check answered from, or empty when it never answered.</param>
/// <param name="ObservedPort">The port the check answered from, or zero when it never answered.</param>
public sealed record LobbyEndpointInfo(
	string Address,
	int Port,
	string Family,
	bool Verified,
	string? ObservedAddress = null,
	int ObservedPort = 0)
{
	/// <summary>The family this endpoint belongs to.</summary>
	public LobbyAddressFamily FamilyValue => LobbyAddresses.ParseFamilyName(Family);

	/// <summary>
	///     The endpoint a player joining over this family must dial: what the registry node observed when it measured the
	///     endpoint, and what the lobby stated when the address is one only a direct measurement can be made of. An IPv6
	///     endpoint has no translation, so the observed endpoint is the stated one.
	/// </summary>
	public string DialAddress => string.IsNullOrEmpty(ObservedAddress) ? Address : ObservedAddress;

	/// <summary>The port a player joining over this family must dial.</summary>
	public int DialPort => ObservedPort > 0 ? ObservedPort : Port;

	/// <summary>Writes this endpoint in the form a player types it into the direct connect field.</summary>
	/// <returns>The address and port, with an IPv6 address bracketed.</returns>
	public string Describe() => LobbyAddresses.Describe(DialAddress, DialPort);
}

/// <summary>Why a screen cannot reach a lobby with the endpoints that lobby offers.</summary>
public enum LobbyEndpointSelectionFailure
{
	/// <summary>An endpoint this screen shares a family with was chosen.</summary>
	None,

	/// <summary>The lobby offered no endpoint at all, so there is nowhere to dial.</summary>
	NoEndpointOffered,

	/// <summary>
	///     The lobby is reachable only over an address family this screen has no address in. This is the one failure that
	///     is stated before anything is dialled, because no number of attempts can fix a family nobody shares.
	/// </summary>
	AddressFamilyMismatch
}

/// <summary>
///     The endpoint a screen should dial to reach a lobby, and what to say when there is none. It is a value rather than a
///     nullable endpoint because a failure is not only "nothing to dial": a lobby reachable over IPv6 alone, offered to a
///     screen with no IPv6, has to be stated as exactly that rather than as a host that did not answer.
/// </summary>
/// <param name="Failure">Why there is no endpoint to dial, or <see cref="LobbyEndpointSelectionFailure.None" />.</param>
/// <param name="Endpoint">The endpoint to dial, when there is one.</param>
/// <param name="LobbyFamilies">The families the lobby offers, which is what the failure message names.</param>
public readonly record struct LobbyEndpointChoice(LobbyEndpointSelectionFailure Failure, LobbyEndpointInfo? Endpoint, IReadOnlyList<LobbyAddressFamily> LobbyFamilies)
{
	/// <summary>Whether an endpoint was chosen.</summary>
	public bool Chosen => Failure == LobbyEndpointSelectionFailure.None && Endpoint != null;

	/// <summary>The family of the chosen endpoint, or <see cref="LobbyAddressFamily.Unknown" /> when nothing was chosen.</summary>
	public LobbyAddressFamily Family => Endpoint?.FamilyValue ?? LobbyAddressFamily.Unknown;

	/// <summary>Whether reaching the chosen endpoint needs a hole punch first.</summary>
	public bool NeedsHolePunch => Chosen && LobbyAddresses.NeedsHolePunch(Family);
}

/// <summary>
///     Chooses the endpoint a screen dials, by the one rule both ends of the lobby protocol share: a screen prefers the
///     lobby's IPv6 endpoint when it has an IPv6 address and the lobby verified one, and falls back to the lobby's IPv4
///     endpoint, and a lobby and a screen that share no family are stated as a family mismatch immediately.
/// </summary>
/// <remarks>
///     A registry node runs the same choice when it decides whether a joining player needs a punch, so the node's punch
///     and the player's dial can never disagree about which endpoint is in use.
/// </remarks>
public static class LobbyEndpointSelection
{
	/// <summary>Chooses the endpoint to dial.</summary>
	/// <param name="endpoints">The endpoints the lobby offers.</param>
	/// <param name="hasIpv4">Whether this screen has an IPv4 address it can reach other networks with.</param>
	/// <param name="hasIpv6">Whether this screen has a global IPv6 address it can reach other networks with.</param>
	/// <returns>The endpoint to dial, or why there is none.</returns>
	public static LobbyEndpointChoice Choose(IReadOnlyList<LobbyEndpointInfo>? endpoints, bool hasIpv4, bool hasIpv6)
	{
		var offered = new List<LobbyAddressFamily>();
		LobbyEndpointInfo? ipv6 = null;
		LobbyEndpointInfo? ipv4 = null;

		for (int index = 0; endpoints != null && index < endpoints.Count; index++)
		{
			LobbyEndpointInfo endpoint = endpoints[index];
			if (endpoint == null || !endpoint.Verified) continue;

			LobbyAddressFamily family = endpoint.FamilyValue;
			if (family == LobbyAddressFamily.Unknown) continue;
			if (!offered.Contains(family)) offered.Add(family);

			if (family == LobbyAddressFamily.IPv6 && ipv6 == null) ipv6 = endpoint;
			if (family == LobbyAddressFamily.IPv4 && ipv4 == null) ipv4 = endpoint;
		}

		if (ipv6 != null && hasIpv6) return new LobbyEndpointChoice(LobbyEndpointSelectionFailure.None, ipv6, offered);
		if (ipv4 != null && hasIpv4) return new LobbyEndpointChoice(LobbyEndpointSelectionFailure.None, ipv4, offered);
		if (offered.Count == 0) return new LobbyEndpointChoice(LobbyEndpointSelectionFailure.NoEndpointOffered, null, offered);

		return new LobbyEndpointChoice(LobbyEndpointSelectionFailure.AddressFamilyMismatch, null, offered);
	}
}
