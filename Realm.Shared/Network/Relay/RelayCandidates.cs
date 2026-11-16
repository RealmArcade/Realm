using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Realm.Shared.Network.Relay;

/// <summary>
///     One volunteer as the registry offers it to a host and to the player joining that host. Everything here is what a
///     candidate has measured or stated about itself: which carrier it offers, how much one datagram may hold, how many
///     connections it has room for and what class its own reachability was measured at.
/// </summary>
public sealed record RelayCandidateMessage
{
	/// <summary>The identifier the volunteer is registered under, which is never a player name or an account.</summary>
	public string RelayId { get; init; } = string.Empty;

	/// <summary>The address the volunteer's forwarding socket is reached at.</summary>
	public string Address { get; init; } = string.Empty;

	/// <summary>The port the volunteer's forwarding socket is bound on.</summary>
	public int Port { get; init; }

	/// <summary>The <see cref="RelayCarrierKind" /> the volunteer offers.</summary>
	public string Carrier { get; init; } = string.Empty;

	/// <summary>The most one relayed datagram may hold through this volunteer.</summary>
	public int MaximumPayloadBytes { get; init; }

	/// <summary>How many connections this volunteer has room for at once.</summary>
	public int MaximumConnections { get; init; }

	/// <summary>How many connections the volunteer is already carrying.</summary>
	public int ActiveConnections { get; init; }

	/// <summary>The most bytes per second the volunteer forwards.</summary>
	public int BudgetBytesPerSecond { get; init; }

	/// <summary>The reachability class the registry measured the volunteer's endpoint at.</summary>
	public string ReachabilityClass { get; init; } = string.Empty;

	/// <summary>The round trip the registry last measured to the volunteer, or zero while it has measured none.</summary>
	public int ObservedLatencyMilliseconds { get; init; }

	/// <summary>Whether this candidate names an endpoint a connection can be carried through.</summary>
	public bool IsUsable => !string.IsNullOrWhiteSpace(Address) && Port is > 0 and <= 65535 && MaximumPayloadBytes >= RelayEnvelope.HeaderBytes;

	/// <summary>The carrier this candidate offers, resolved from the name it stated.</summary>
	/// <returns>The carrier kind, or the UDP tunnel when the volunteer stated a carrier this build does not know.</returns>
	public RelayCarrierKind ResolveCarrier()
	{
		return Enum.TryParse(Carrier, ignoreCase: true, out RelayCarrierKind kind) ? kind : RelayCarrierKind.UdpTunnel;
	}

	/// <summary>The capability record this candidate amounts to, which is what the path layer reads before using it.</summary>
	/// <returns>The carrier's capabilities as this candidate states them.</returns>
	public RelayCarrierCapabilities Capabilities()
	{
		return new RelayCarrierCapabilities(
			ResolveCarrier(),
			MaximumPayloadBytes,
			GuaranteesDelivery: false,
			ProvidesOwnEncryption: false,
			SupportsIndependentPaths: true,
			SetupCostMilliseconds: Math.Max(0, ObservedLatencyMilliseconds));
	}

	/// <summary>
	///     States the candidate in a line a log and a diagnostics page can compare, without naming the people on either end
	///     of the connections it carries.
	/// </summary>
	/// <returns>One line describing the candidate.</returns>
	public string Describe()
	{
		return $"relay candidate={RelayId} endpoint={Address}:{Port} carrier={ResolveCarrier()} payload={MaximumPayloadBytes} carry={ActiveConnections}/{MaximumConnections} class={ReachabilityClass} latency={ObservedLatencyMilliseconds}ms";
	}
}

/// <summary>
///     One relayed path both peers of a connection were offered, named by the path identifier both of them seal its
///     datagrams under. The identifier is the candidate's position in the offered list, so a host and a player that received
///     the same list open the same paths without either of them choosing a volunteer.
/// </summary>
/// <param name="PathId">The path identifier every datagram through this candidate is sealed under.</param>
/// <param name="Candidate">The volunteer to carry it.</param>
public readonly record struct RelayPathOffer(byte PathId, RelayCandidateMessage Candidate)
{
	/// <summary>Whether this offer can become a path at all.</summary>
	public bool IsUsable => PathId > 0 && Candidate != null && Candidate.IsUsable;

	/// <summary>The line a log states when a path is opened through this offer.</summary>
	/// <returns>One line naming the path and the volunteer that carries it.</returns>
	public string Describe()
	{
		return $"relay path={PathId} via={Candidate?.RelayId} endpoint={Candidate?.Address}:{Candidate?.Port} carrier={Candidate?.ResolveCarrier()}";
	}
}

/// <summary>
///     The rules both peers apply to the relay candidates a lobby server hands them, so that a host and a player who are
///     told the same thing open the same paths. A relay is never chosen by either of them: the order the server stated is
///     the order they both use.
/// </summary>
public static class RelayOffers
{
	/// <summary>The most relayed paths one connection opens from one offer list.</summary>
	public const int MaximumOffers = 3;

	/// <summary>
	///     Reads the offers a lobby server stated. A server that stated none leaves the list empty, which is a connection
	///     that carries on exactly as it did before relays existed.
	/// </summary>
	/// <param name="candidates">The candidates the server stated, in the order it prefers them.</param>
	/// <returns>The paths both peers open, in the order the server stated them.</returns>
	public static List<RelayPathOffer> Build(IReadOnlyList<RelayCandidateMessage>? candidates)
	{
		var offers = new List<RelayPathOffer>(MaximumOffers);
		if (candidates == null) return offers;

		for (int index = 0; index < candidates.Count && offers.Count < MaximumOffers; index++)
		{
			RelayCandidateMessage candidate = candidates[index];
			if (candidate == null || !candidate.IsUsable) continue;

			offers.Add(new RelayPathOffer((byte)(offers.Count + 1), candidate));
		}

		return offers;
	}

	/// <summary>
	///     Reads the offers a peer stated in a line a lobby command can carry. The shape is one offer per entry, so a host
	///     that was told the same list opens the same paths under the same identifiers.
	/// </summary>
	/// <param name="json">The JSON line the lobby command carried.</param>
	/// <returns>The offers, or an empty list when the line states none.</returns>
	public static List<RelayPathOffer> Parse(string? json)
	{
		if (string.IsNullOrWhiteSpace(json)) return [];

		try
		{
			List<RelayCandidateMessage>? candidates = JsonSerializer.Deserialize<List<RelayCandidateMessage>>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
			return Build(candidates);
		}
		catch (JsonException)
		{
			return [];
		}
	}

	/// <summary>States offers in the shape <see cref="Parse" /> reads them.</summary>
	/// <param name="offers">The offers to state.</param>
	/// <returns>The JSON line.</returns>
	public static string Serialize(IReadOnlyList<RelayPathOffer> offers)
	{
		var candidates = new List<RelayCandidateMessage>(offers.Count);
		for (int index = 0; index < offers.Count; index++) candidates.Add(offers[index].Candidate);

		return JsonSerializer.Serialize(candidates);
	}
}

/// <summary>
///     The secret a relayed connection's keys are derived from, and the token it is named by. Both peers compute it from the
///     join ticket the lobby server issued them, which is a value no volunteer is ever given: the ticket reaches the host
///     over the lobby's own signalling channel and the player over the answer to the join.
/// </summary>
public static class RelayJoinSecret
{
	private static ReadOnlySpan<byte> TokenLabel => "realm-relay-connection-token-v1"u8;

	/// <summary>
	///     Derives the connection token both peers name a relayed connection by. It is a function of the join ticket alone,
	///     so two peers that agreed a ticket agree the token without either of them stating it.
	/// </summary>
	/// <param name="joinTicket">The ticket the lobby server issued for this join.</param>
	/// <returns>The connection token, or the absent token when no usable ticket was stated.</returns>
	public static RelayConnectionToken DeriveToken(string? joinTicket)
	{
		byte[] secret = ReadSecret(joinTicket);
		if (secret.Length == 0) return RelayConnectionToken.None;

		Span<byte> derived = stackalloc byte[sizeof(ulong)];
		HKDF.DeriveKey(HashAlgorithmName.SHA256, secret, derived, ReadOnlySpan<byte>.Empty, TokenLabel);
		ulong value = System.Buffers.Binary.BinaryPrimitives.ReadUInt64LittleEndian(derived);
		return new RelayConnectionToken(value == 0 ? 1UL : value);
	}

	/// <summary>Whether a join ticket is enough to carry a connection through a relay at all.</summary>
	/// <param name="joinTicket">The ticket the lobby server issued for this join.</param>
	/// <returns>Whether a relayed path may be derived from it.</returns>
	public static bool IsUsable(string? joinTicket) => ReadSecret(joinTicket).Length > 0;

	/// <summary>
	///     Reads the key material a join ticket amounts to. It is the ticket's own text, so nothing about it has to travel
	///     beside the ticket both peers already exchanged.
	/// </summary>
	/// <param name="joinTicket">The ticket the lobby server issued for this join.</param>
	/// <returns>The key material, or an empty array when the ticket is unusable.</returns>
	public static byte[] ReadSecret(string? joinTicket)
	{
		if (string.IsNullOrWhiteSpace(joinTicket) || joinTicket.Length < 16) return [];

		return SHA256.HashData(Encoding.UTF8.GetBytes(joinTicket));
	}
}
