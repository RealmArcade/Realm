using System;
using System.Collections.Generic;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;

namespace Realm.Shared.Reachability;

/// <summary>
///     One endpoint this machine could offer other players: an address it holds, how far that address reaches, and the
///     family it belongs to.
/// </summary>
/// <param name="Address">The address this machine holds.</param>
/// <param name="Family">The family of the address.</param>
/// <param name="Scope">How far the address reaches.</param>
/// <param name="IsTemporary">
///     Whether this address is one the operating system rotates, which is the privacy address an IPv6 network hands out
///     beside the stable one. It is still a working address while it lives, so it is offered; it is offered last, because a
///     lobby that states an address which expires stops being joinable at that address.
/// </param>
public readonly record struct LobbyEndpointCandidate(IPAddress Address, LobbyAddressFamily Family, LobbyAddressScope Scope, bool IsTemporary = false)
{
	/// <summary>Whether reaching this endpoint needs the host's router to map a port.</summary>
	public bool NeedsRouterMapping => LobbyAddresses.NeedsRouterMapping(Family);
}

/// <summary>
///     Reads the addresses this machine holds and states which of them another player could be given, best first. It runs
///     when a lobby is opened or joined and never on a match tick: enumerating interfaces asks the operating system, which
///     is nothing a simulation may wait for.
/// </summary>
/// <remarks>
///     The order is the whole of the policy: a global IPv6 address first, because it needs no router mapping and no punch
///     and therefore works for the most players; then the IPv4 address this machine sits on, because the public IPv4
///     endpoint is measured rather than held; then a unique local IPv6 address, which reaches the network this machine is
///     on but nothing beyond it. A link-local address is never offered at all: it needs the scope identifier of the
///     interface it was read from, which no other player holds.
/// </remarks>
public static class LocalEndpointDiscovery
{
	/// <summary>Every address this machine could offer another player, best first.</summary>
	/// <returns>The candidates, ordered so the first one of each family is the one to offer.</returns>
	public static List<LobbyEndpointCandidate> Discover() => ReadCandidates();

	/// <summary>The best endpoint this machine holds in a family, or nothing when it holds none.</summary>
	/// <param name="family">The family to look for.</param>
	/// <returns>The endpoint to offer, or nothing.</returns>
	public static LobbyEndpointCandidate? Best(LobbyAddressFamily family)
	{
		List<LobbyEndpointCandidate> candidates = ReadCandidates();
		for (int index = 0; index < candidates.Count; index++)
		{
			if (candidates[index].Family == family) return candidates[index];
		}

		return null;
	}

	/// <summary>The best IPv6 address this machine holds, or nothing when it holds none another player could use.</summary>
	/// <returns>The global address when there is one, then a unique local address.</returns>
	public static IPAddress? BestIPv6Address() => Best(LobbyAddressFamily.IPv6)?.Address;

	/// <summary>
	///     Whether this machine holds a global IPv6 address, which is the only thing that lets it reach a lobby published
	///     over IPv6. A unique local address reaches this network alone, so a lobby on another network needs the global one.
	/// </summary>
	/// <returns>Whether this machine can reach another network over IPv6.</returns>
	public static bool HasGlobalIpv6()
	{
		List<LobbyEndpointCandidate> candidates = ReadCandidates();
		for (int index = 0; index < candidates.Count; index++)
		{
			if (candidates[index].Family == LobbyAddressFamily.IPv6 && candidates[index].Scope == LobbyAddressScope.Global) return true;
		}

		return false;
	}

	/// <summary>
	///     Whether this machine holds an IPv4 address other than loopback, which is what makes an IPv4 endpoint worth
	///     publishing and a router mapping worth asking for.
	/// </summary>
	/// <returns>Whether this machine can offer an IPv4 endpoint.</returns>
	public static bool HasIpv4()
	{
		List<LobbyEndpointCandidate> candidates = ReadCandidates();
		for (int index = 0; index < candidates.Count; index++)
		{
			if (candidates[index].Family == LobbyAddressFamily.IPv4) return true;
		}

		return false;
	}

	private static List<LobbyEndpointCandidate> ReadCandidates()
	{
		var candidates = new List<LobbyEndpointCandidate>(4);
		if (!NetworkInterface.GetIsNetworkAvailable()) return candidates;

		NetworkInterface[] interfaces;
		try
		{
			interfaces = NetworkInterface.GetAllNetworkInterfaces();
		}
		catch (NetworkInformationException)
		{
			return candidates;
		}

		for (int index = 0; index < interfaces.Length; index++)
		{
			NetworkInterface networkInterface = interfaces[index];
			if (networkInterface.OperationalStatus != OperationalStatus.Up) continue;
			if (networkInterface.NetworkInterfaceType is NetworkInterfaceType.Loopback) continue;

			UnicastIPAddressInformationCollection unicast;
			try
			{
				unicast = networkInterface.GetIPProperties().UnicastAddresses;
			}
			catch (NetworkInformationException)
			{
				continue;
			}

			for (int unicastIndex = 0; unicastIndex < unicast.Count; unicastIndex++)
			{
				// An address written as IPv6 but holding an IPv4 one is offered as the IPv4 address it is, because that is the
				// family it is dialled in and the only one a check can reach it from.
				UnicastIPAddressInformation unicastAddress = unicast[unicastIndex];
				IPAddress? address = LobbyAddresses.Normalize(unicastAddress.Address);
				if (address == null || !LobbyAddresses.IsAdvertisable(address)) continue;

				candidates.Add(new LobbyEndpointCandidate(address, LobbyAddresses.FamilyOf(address), LobbyAddresses.ScopeOf(address), IsTemporary(unicastAddress)));
			}
		}

		candidates.Sort(CompareCandidates);
		return candidates;
	}

	/// <summary>
	///     Whether an address is one the operating system rotates. IPv6 privacy addresses are formed from a
	///     router-advertised prefix and a random interface identifier, and they are replaced on a timer, so a lobby that
	///     stated one stops being joinable at the address its players were given when it is retired.
	/// </summary>
	/// <param name="address">The address as the interface reported it.</param>
	/// <returns>Whether the address is a temporary one.</returns>
	private static bool IsTemporary(UnicastIPAddressInformation address)
	{
		try
		{
			return address.PrefixOrigin == PrefixOrigin.RouterAdvertisement && address.SuffixOrigin == SuffixOrigin.Random;
		}
		catch (Exception exception) when (exception is PlatformNotSupportedException or NotImplementedException)
		{
			// An operating system that cannot say treats every address as stable, which is what this build did before it
			// could ask: the ordering falls back to the policy above rather than failing to offer anything.
			return false;
		}
	}

	/// <summary>
	///     Orders the candidates by the policy above. The comparison is a total order so the endpoint a host offers is the
	///     same one on every run, rather than whichever interface the operating system happened to enumerate first.
	/// </summary>
	private static int CompareCandidates(LobbyEndpointCandidate left, LobbyEndpointCandidate right)
	{
		int leftRank = Rank(left);
		int rightRank = Rank(right);
		if (leftRank != rightRank) return leftRank.CompareTo(rightRank);
		if (left.IsTemporary != right.IsTemporary) return left.IsTemporary ? 1 : -1;

		return string.CompareOrdinal(left.Address.ToString(), right.Address.ToString());
	}

	private static int Rank(LobbyEndpointCandidate candidate)
	{
		if (candidate.Family == LobbyAddressFamily.IPv6) return candidate.Scope == LobbyAddressScope.Global ? 0 : 3;
		if (candidate.Family == LobbyAddressFamily.IPv4) return candidate.Scope == LobbyAddressScope.LocalNetwork ? 1 : 2;
		return 4;
	}
}
