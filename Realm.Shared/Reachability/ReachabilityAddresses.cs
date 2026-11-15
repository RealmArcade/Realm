using System;
using System.Net;
using System.Net.Sockets;

namespace Realm.Shared.Reachability;

/// <summary>
///     Reads addresses the way a reachability check has to read them: whether an endpoint is one a player outside its own
///     network can use at all. A registry node that measures a lobby from an address inside that lobby's own network is not
///     measuring anything a stranger could repeat, so this is the rule that keeps a check honest.
/// </summary>
public static class ReachabilityAddresses
{
	/// <summary>
	///     Whether this address is one a player elsewhere on the internet can be sent to. Loopback, the wildcard address,
	///     link-local, IPv4 private ranges, the carrier-grade shared range and IPv6 unique-local addresses are all answered
	///     as false: traffic to any of them never leaves the network that already holds the machine, so reaching one of them
	///     says nothing about whether anybody else can reach it.
	/// </summary>
	/// <param name="address">The address to judge, or null when nothing was stated.</param>
	/// <returns>Whether the address is publicly routable.</returns>
	public static bool IsPubliclyRoutable(IPAddress? address)
	{
		if (address == null) return false;
		if (address.IsIPv4MappedToIPv6) return IsPubliclyRoutable(address.MapToIPv4());
		if (IPAddress.IsLoopback(address)) return false;

		if (address.AddressFamily == AddressFamily.InterNetworkV6)
		{
			if (address.Equals(IPAddress.IPv6Any)) return false;
			if (address.IsIPv6LinkLocal || address.IsIPv6SiteLocal || address.IsIPv6Multicast) return false;

			Span<byte> octets = stackalloc byte[16];
			address.TryWriteBytes(octets, out _);
			return (octets[0] & 0xFE) != 0xFC;
		}

		if (address.AddressFamily != AddressFamily.InterNetwork) return false;
		if (address.Equals(IPAddress.Any) || address.Equals(IPAddress.Broadcast)) return false;

		Span<byte> quad = stackalloc byte[4];
		address.TryWriteBytes(quad, out _);

		if (quad[0] == 0) return false;
		if (quad[0] == 10) return false;
		if (quad[0] == 169 && quad[1] == 254) return false;
		if (quad[0] == 172 && quad[1] >= 16 && quad[1] <= 31) return false;
		if (quad[0] == 192 && quad[1] == 168) return false;
		if (quad[0] == 100 && quad[1] >= 64 && quad[1] <= 127) return false;
		if (quad[0] >= 224) return false;

		return true;
	}

	/// <summary>
	///     Whether an address stated as text is one a player elsewhere on the internet can be sent to. Text this build cannot
	///     read is answered as false, because an address nothing could parse is not evidence of reachability.
	/// </summary>
	/// <param name="address">The address text to judge, or an empty string when nothing was stated.</param>
	/// <returns>Whether the address is publicly routable.</returns>
	public static bool IsPubliclyRoutable(string? address)
	{
		if (string.IsNullOrWhiteSpace(address)) return false;
		return IPAddress.TryParse(address.Trim(), out IPAddress? parsed) && IsPubliclyRoutable(parsed);
	}
}
