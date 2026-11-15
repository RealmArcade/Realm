using System;
using System.Net;
using System.Net.Sockets;

namespace Realm.Shared.Reachability;

/// <summary>
///     The address family a lobby endpoint is reachable over. A lobby offers one endpoint per family it can be reached
///     in, and a joining player uses the one it shares with the host: IPv6 needs no router mapping and no hole punch,
///     while IPv4 keeps the measured public endpoint and the punch that path has always used.
/// </summary>
public enum LobbyAddressFamily
{
	/// <summary>The family of an address that could not be read, so nothing can be assumed about how to reach it.</summary>
	Unknown = 0,

	/// <summary>An IPv4 endpoint, which a host reaches through a router mapping and a hole punch.</summary>
	IPv4 = 1,

	/// <summary>An IPv6 endpoint, which a host reaches directly because IPv6 has no address translation.</summary>
	IPv6 = 2
}

/// <summary>
///     How far an address reaches. It is what orders the endpoints a host offers, so the address that works for the
///     most players is the one stated first, and what decides whether an address is worth offering at all.
/// </summary>
public enum LobbyAddressScope
{
	/// <summary>Only this machine reaches it, so it is never offered to anybody else.</summary>
	Loopback,

	/// <summary>The network this machine sits on reaches it: a private IPv4 address, a unique local IPv6 address.</summary>
	LocalNetwork,

	/// <summary>Any network that can route to it reaches it: a global IPv6 address, a public IPv4 address.</summary>
	Global
}

/// <summary>
///     The rules every part of the lobby protocol shares about a host address: which family it is, how far it reaches,
///     whether it may be offered to another player, and how it is written. One set of rules is what keeps the host, the
///     registry node, the reachability check and the joining screen from disagreeing about the same address.
/// </summary>
public static class LobbyAddresses
{
	/// <summary>The family an address belongs to.</summary>
	/// <param name="address">The address to read, or nothing.</param>
	/// <returns>The family, or <see cref="LobbyAddressFamily.Unknown" /> when the address is nothing.</returns>
	public static LobbyAddressFamily FamilyOf(IPAddress? address) => address?.AddressFamily switch
	{
		AddressFamily.InterNetwork => LobbyAddressFamily.IPv4,
		AddressFamily.InterNetworkV6 => LobbyAddressFamily.IPv6,
		_ => LobbyAddressFamily.Unknown
	};

	/// <summary>The family a written address belongs to, without keeping the address.</summary>
	/// <param name="address">The address to read.</param>
	/// <returns>The family, or <see cref="LobbyAddressFamily.Unknown" /> when the text is not an address.</returns>
	public static LobbyAddressFamily FamilyOf(string? address) => TryParse(address, out IPAddress? parsed) ? FamilyOf(parsed) : LobbyAddressFamily.Unknown;

	/// <summary>Reads an address that may still carry the brackets an IPv6 address is written with.</summary>
	/// <param name="address">The address to read, with or without brackets.</param>
	/// <param name="parsed">The address, or nothing when the text is not one.</param>
	/// <returns>Whether the text is an address.</returns>
	public static bool TryParse(string? address, out IPAddress? parsed)
	{
		parsed = null;
		if (string.IsNullOrWhiteSpace(address)) return false;

		string trimmed = address.Trim();
		if (trimmed.StartsWith('[') && trimmed.EndsWith(']')) trimmed = trimmed[1..^1];
		if (!IPAddress.TryParse(trimmed, out IPAddress? candidate)) return false;
		if (candidate.AddressFamily is not (AddressFamily.InterNetwork or AddressFamily.InterNetworkV6)) return false;

		parsed = candidate;
		return true;
	}

	/// <summary>The name a family is written with on the wire, which the lobby protocol states rather than leaving implied.</summary>
	/// <param name="family">The family.</param>
	/// <returns>The name the protocol uses.</returns>
	public static string FamilyName(LobbyAddressFamily family) => family switch
	{
		LobbyAddressFamily.IPv4 => "ipv4",
		LobbyAddressFamily.IPv6 => "ipv6",
		_ => string.Empty
	};

	/// <summary>Reads the family name the lobby protocol states.</summary>
	/// <param name="name">The name, in any casing.</param>
	/// <returns>The family, or <see cref="LobbyAddressFamily.Unknown" /> for a name this build does not know.</returns>
	public static LobbyAddressFamily ParseFamilyName(string? name)
	{
		if (string.IsNullOrWhiteSpace(name)) return LobbyAddressFamily.Unknown;

		string trimmed = name.Trim();
		if (trimmed.Equals("ipv4", StringComparison.OrdinalIgnoreCase)) return LobbyAddressFamily.IPv4;
		if (trimmed.Equals("ipv6", StringComparison.OrdinalIgnoreCase)) return LobbyAddressFamily.IPv6;
		return LobbyAddressFamily.Unknown;
	}

	/// <summary>
	///     Whether reaching an endpoint in this family needs the host's router to map a port, which is what a STUN
	///     measurement and a UPnP request are for. IPv6 has no address translation, so a host with nothing but an IPv6
	///     endpoint has nothing to map and must never be told its router failed when there was nothing to ask it for.
	/// </summary>
	/// <param name="family">The family of the endpoint.</param>
	/// <returns>Whether a router mapping is needed.</returns>
	public static bool NeedsRouterMapping(LobbyAddressFamily family) => family == LobbyAddressFamily.IPv4;

	/// <summary>
	///     Whether a joining player has to punch a path to an endpoint in this family before it can connect. A router that
	///     drops unsolicited traffic is an IPv4 fact: an IPv6 endpoint is dialled directly.
	/// </summary>
	/// <param name="family">The family of the endpoint.</param>
	/// <returns>Whether a hole punch is needed.</returns>
	public static bool NeedsHolePunch(LobbyAddressFamily family) => family == LobbyAddressFamily.IPv4;

	/// <summary>How far an address reaches.</summary>
	/// <param name="address">The address to read.</param>
	/// <returns>The scope of the address.</returns>
	public static LobbyAddressScope ScopeOf(IPAddress? address)
	{
		if (address == null) return LobbyAddressScope.Loopback;
		if (IPAddress.IsLoopback(address)) return LobbyAddressScope.Loopback;

		if (address.AddressFamily == AddressFamily.InterNetworkV6)
		{
			byte[] bytes = address.GetAddressBytes();
			bool uniqueLocal = (bytes[0] & 0xFE) == 0xFC;
			bool deprecatedSiteLocal = bytes[0] == 0xFE && (bytes[1] & 0xC0) == 0xC0;
			return uniqueLocal || deprecatedSiteLocal ? LobbyAddressScope.LocalNetwork : LobbyAddressScope.Global;
		}

		if (address.AddressFamily != AddressFamily.InterNetwork) return LobbyAddressScope.Loopback;

		byte[] ipv4 = address.GetAddressBytes();
		bool privateRange = ipv4[0] == 10
			|| (ipv4[0] == 172 && ipv4[1] >= 16 && ipv4[1] <= 31)
			|| (ipv4[0] == 192 && ipv4[1] == 168)
			|| (ipv4[0] == 100 && ipv4[1] >= 64 && ipv4[1] <= 127);

		return privateRange ? LobbyAddressScope.LocalNetwork : LobbyAddressScope.Global;
	}

	/// <summary>
	///     Whether an address is one another player could be given. A loopback address belongs to one machine, an
	///     unspecified address belongs to no machine, a multicast address belongs to a group rather than a host, and a
	///     link-local address needs the scope identifier of the interface it was read from, which no other player holds.
	///     Everything else is stated so the player holding it can decide.
	/// </summary>
	/// <param name="address">The address to judge.</param>
	/// <returns>Whether the address may be offered to another player.</returns>
	public static bool IsAdvertisable(IPAddress? address)
	{
		if (address == null) return false;
		if (address.AddressFamily is not (AddressFamily.InterNetwork or AddressFamily.InterNetworkV6)) return false;
		if (IPAddress.IsLoopback(address)) return false;
		if (address.Equals(IPAddress.Any) || address.Equals(IPAddress.None) || address.Equals(IPAddress.IPv6Any) || address.Equals(IPAddress.IPv6None)) return false;
		if (address.IsIPv6LinkLocal) return false;
		if (address.IsIPv6Multicast) return false;

		byte[] bytes = address.GetAddressBytes();
		if (bytes.Length == 4)
		{
			if (bytes[0] >= 224) return false;
			if (bytes[0] == 0 || (bytes[0] == 169 && bytes[1] == 254)) return false;
		}

		return true;
	}

	/// <summary>
	///     Writes an address and a port the way a player types and reads them back. An IPv6 address carries colons of its
	///     own, so it is bracketed or the port would be read as part of the address.
	/// </summary>
	/// <param name="host">The host to reach, with or without brackets.</param>
	/// <param name="port">The port to reach it on.</param>
	/// <returns>The address in the form the direct connect field takes.</returns>
	public static string Describe(string host, int port)
	{
		string trimmed = (host ?? string.Empty).Trim().Trim('[', ']');
		return trimmed.Contains(':') ? $"[{trimmed}]:{port}" : $"{trimmed}:{port}";
	}

	/// <summary>Writes an address and a port the way a player types and reads them back.</summary>
	/// <param name="address">The address to reach.</param>
	/// <param name="port">The port to reach it on.</param>
	/// <returns>The address in the form the direct connect field takes.</returns>
	public static string Describe(IPAddress address, int port) => Describe(address.ToString(), port);
}
