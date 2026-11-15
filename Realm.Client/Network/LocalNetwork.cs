using System;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;

namespace Realm.Client.Network;

/// <summary>
///     Answers whether an address sits on a network this machine is attached to, which is the only evidence that makes a
///     direct connection worth attempting before the lobby's public endpoint.
/// </summary>
/// <remarks>
///     Nothing about the host's public address can answer this: two players behind one carrier-grade NAT, one office router
///     or one VPN exit share a public address and cannot reach each other's private ones. What can answer it is the
///     machine's own interface table, which states the networks it is actually on.
/// </remarks>
public static class LocalNetwork
{
	/// <summary>
	///     Whether this machine holds an address in the same subnet as <paramref name="address" />. An address that cannot be
	///     read, an IPv6 address, and a machine with no usable interface all answer false, because none of them is evidence
	///     of a local route.
	/// </summary>
	/// <param name="address">The address to place, as the lobby stated it.</param>
	/// <returns>Whether a directly attached network contains the address.</returns>
	public static bool Contains(string? address)
	{
		if (!IPAddress.TryParse(address ?? string.Empty, out IPAddress? candidate)) return false;
		if (candidate.AddressFamily != AddressFamily.InterNetwork) return false;

		foreach (NetworkInterface networkInterface in NetworkInterface.GetAllNetworkInterfaces())
		{
			if (networkInterface.OperationalStatus != OperationalStatus.Up) continue;
			if (networkInterface.NetworkInterfaceType == NetworkInterfaceType.Loopback) continue;

			foreach (UnicastIPAddressInformation unicast in networkInterface.GetIPProperties().UnicastAddresses)
			{
				if (unicast.Address.AddressFamily != AddressFamily.InterNetwork) continue;
				if (IsInSubnet(unicast.Address, candidate, unicast.PrefixLength)) return true;
			}
		}

		return false;
	}

	private static bool IsInSubnet(IPAddress local, IPAddress candidate, int prefixLength)
	{
		if (prefixLength < 0 || prefixLength > 32) return false;

		Span<byte> localBytes = stackalloc byte[4];
		Span<byte> candidateBytes = stackalloc byte[4];
		if (!local.TryWriteBytes(localBytes, out _) || !candidate.TryWriteBytes(candidateBytes, out _)) return false;

		uint mask = prefixLength == 0 ? 0u : uint.MaxValue << (32 - prefixLength);
		uint localValue = (uint)(localBytes[0] << 24 | localBytes[1] << 16 | localBytes[2] << 8 | localBytes[3]);
		uint candidateValue = (uint)(candidateBytes[0] << 24 | candidateBytes[1] << 16 | candidateBytes[2] << 8 | candidateBytes[3]);
		return (localValue & mask) == (candidateValue & mask);
	}
}
