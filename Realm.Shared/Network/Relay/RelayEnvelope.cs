using System;
using System.Buffers.Binary;

namespace Realm.Shared.Network.Relay;

/// <summary>
///     One relayed datagram as a volunteer carries it. Everything a volunteer needs in order to move the datagram sits in
///     front of the sealed payload and names nothing about the game, the map, the lobby or the players: the token, which
///     path the copy travelled, the sequence the duplicate window reads, and the tag that proves the copy came from the peer
///     holding this direction's key.
/// </summary>
public static class RelayEnvelope
{
	/// <summary>The bytes the connection token occupies, which is the first field of the header.</summary>
	public const int TokenOffset = 0;

	/// <summary>The bytes the path identifier occupies.</summary>
	public const int PathIdOffset = RelayConnectionToken.TextCharacters / 2;

	/// <summary>The bytes the delivery guarantee occupies.</summary>
	public const int DeliveryOffset = PathIdOffset + 1;

	/// <summary>The bytes the sequence the duplicate window reads occupies.</summary>
	public const int MessageSequenceOffset = DeliveryOffset + 1;

	/// <summary>The bytes the per-path sequence occupies.</summary>
	public const int PathSequenceOffset = MessageSequenceOffset + 3;

	/// <summary>The bytes the authentication tag occupies.</summary>
	public const int TagOffset = PathSequenceOffset + 2;

	/// <summary>The bytes one relayed datagram spends on its header and its tag.</summary>
	public const int HeaderBytes = TagOffset + RelayDatagramCipher.TagBytes;

	/// <summary>
	///     The widest inner datagram the path layer carries, which is the widest one the transport on either side can hand it.
	///     ENet fragments what it is given down to its own datagram budget and never sends more than that in one datagram, so
	///     this is that budget rather than a smaller number chosen for the frame layer: a datagram refused here is one the
	///     transport above already believes it sent, and on its ordered reliable channel the datagram behind it waits for a
	///     retransmission that would be refused for exactly the same reason. With the envelope in front of it, the datagram
	///     still lands inside one IP datagram, which is what keeps relays from introducing fragmentation of their own.
	/// </summary>
	public const int MaximumInnerDatagramBytes = 1400;

	/// <summary>The widest relayed datagram the path layer produces, header, tag and inner datagram together.</summary>
	public const int MaximumEncapsulatedDatagramBytes = HeaderBytes + MaximumInnerDatagramBytes;

	/// <summary>How many copies of one inner datagram the duplicate window remembers.</summary>
	public const int MessageSequenceMask = 0xFFFFFF;

	/// <summary>How many copies of one inner datagram one path counts before its own sequence returns to zero.</summary>
	public const int PathSequenceMask = 0xFFFF;

	/// <summary>
	///     Sets the bit of the delivery byte that marks a datagram as the path layer's own probe rather than one for the
	///     match. A probe is measured and answered by the path layer and is never handed on, so the two kinds of datagram
	///     travel as one envelope rather than as two protocols.
	/// </summary>
	public const byte ProbeFlag = 0x80;

	/// <summary>
	///     Sets the bit of the delivery byte that marks a probe as the answer to another probe, so one round trip is one
	///     request and one answer rather than a pair that keeps answering each other.
	/// </summary>
	public const byte ProbeResponseFlag = 0x40;

	/// <summary>Whether the delivery flag of a header marks a probe rather than a datagram for the match.</summary>
	/// <param name="deliveryFlag">The delivery byte a header carried.</param>
	/// <returns>Whether the datagram is a probe.</returns>
	public static bool IsProbe(byte deliveryFlag) => (deliveryFlag & ProbeFlag) != 0;

	/// <summary>Whether the delivery flag of a header marks a probe that answers another probe.</summary>
	/// <param name="deliveryFlag">The delivery byte a header carried.</param>
	/// <returns>Whether the datagram is a probe answer.</returns>
	public static bool IsProbeResponse(byte deliveryFlag) => (deliveryFlag & ProbeResponseFlag) != 0;

	/// <summary>Reads the guarantee a datagram needs, whichever way its probe bits point.</summary>
	/// <param name="deliveryFlag">The delivery byte a header carried.</param>
	/// <returns>The guarantee the routed datagram needs.</returns>
	public static FrameDelivery RoutedDeliveryOf(byte deliveryFlag) => (FrameDelivery)(deliveryFlag & 0x3F);

	/// <summary>Builds the delivery byte a header carries for one kind of datagram.</summary>
	/// <param name="delivery">The guarantee the routed datagram needs.</param>
	/// <param name="probe">Whether the datagram is the path layer's own probe.</param>
	/// <returns>The byte the header states.</returns>
	public static byte DeliveryFlag(FrameDelivery delivery, bool probe, bool probeResponse = false)
	{
		byte value = delivery == FrameDelivery.UnreliableOrdered ? (byte)FrameDelivery.UnreliableOrdered : (byte)FrameDelivery.Reliable;
		if (probe) value |= ProbeFlag;
		if (probeResponse) value |= ProbeResponseFlag;
		return value;
	}

	/// <summary>Reads the guarantee a header carries, ignoring whether the datagram is a probe.</summary>
	/// <param name="deliveryFlag">The delivery byte a header carried.</param>
	/// <returns>The guarantee the routed datagram needs.</returns>
	public static FrameDelivery DeliveryOf(byte deliveryFlag) => RoutedDeliveryOf(deliveryFlag);

	/// <summary>Writes the plain part of a header, which is everything a volunteer may read.</summary>
	/// <param name="destination">The datagram being built.</param>
	/// <param name="token">The connection the datagram belongs to.</param>
	/// <param name="pathId">The path the copy travels.</param>
	/// <param name="deliveryFlag">The delivery byte the header states.</param>
	/// <param name="messageSequence">The sequence the duplicate window reads.</param>
	/// <param name="pathSequence">The per-path sequence, which is what that path's loss is measured from.</param>
	public static void WriteHeader(Span<byte> destination, RelayConnectionToken token, byte pathId, byte deliveryFlag, int messageSequence, int pathSequence)
	{
		token.WriteTo(destination.Slice(TokenOffset, RelayConnectionToken.TextCharacters / 2));
		destination[PathIdOffset] = pathId;
		destination[DeliveryOffset] = deliveryFlag;
		WriteInt24(destination.Slice(MessageSequenceOffset, 3), messageSequence & MessageSequenceMask);
		BinaryPrimitives.WriteUInt16LittleEndian(destination.Slice(PathSequenceOffset, 2), (ushort)(pathSequence & PathSequenceMask));
	}

	/// <summary>Reads the plain part of a header, which is what a volunteer is allowed to know about a datagram.</summary>
	/// <param name="datagram">The datagram as it arrived.</param>
	/// <param name="header">The fields the datagram states.</param>
	/// <returns>Whether the datagram is long enough to hold a header at all.</returns>
	public static bool TryReadHeader(ReadOnlySpan<byte> datagram, out RelayPacketHeader header)
	{
		header = default;
		if (datagram.Length < HeaderBytes) return false;

		RelayConnectionToken token = RelayConnectionToken.ReadFrom(datagram.Slice(TokenOffset, RelayConnectionToken.TextCharacters / 2));
		if (!token.IsSome) return false;

		header = new RelayPacketHeader(
			token,
			datagram[PathIdOffset],
			datagram[DeliveryOffset],
			ReadInt24(datagram.Slice(MessageSequenceOffset, 3)),
			BinaryPrimitives.ReadUInt16LittleEndian(datagram.Slice(PathSequenceOffset, 2)));
		return true;
	}

	/// <summary>The sealed payload of a datagram, which a volunteer cannot read and carries as it arrived.</summary>
	/// <param name="datagram">The datagram as it arrived.</param>
	/// <returns>The bytes after the tag.</returns>
	public static ReadOnlySpan<byte> CiphertextOf(ReadOnlySpan<byte> datagram) => datagram.Slice(HeaderBytes);

	/// <summary>The authentication tag of a datagram.</summary>
	/// <param name="datagram">The datagram as it arrived.</param>
	/// <returns>The sixteen bytes in front of the sealed payload.</returns>
	public static ReadOnlySpan<byte> TagOf(ReadOnlySpan<byte> datagram) => datagram.Slice(TagOffset, RelayDatagramCipher.TagBytes);

	/// <summary>Reads a three-byte sequence, which is written little-endian so the window arithmetic stays one subtraction.</summary>
	/// <param name="source">The three bytes the sequence occupies.</param>
	/// <returns>The sequence those bytes name.</returns>
	private static int ReadInt24(ReadOnlySpan<byte> source) => source[0] | source[1] << 8 | source[2] << 16;

	/// <summary>Writes a three-byte sequence, which is written little-endian so the window arithmetic stays one subtraction.</summary>
	/// <param name="destination">The three bytes the sequence occupies.</param>
	/// <param name="value">The sequence to write.</param>
	private static void WriteInt24(Span<byte> destination, int value)
	{
		destination[0] = (byte)value;
		destination[1] = (byte)(value >> 8);
		destination[2] = (byte)(value >> 16);
	}
}

/// <summary>
///     The header of a relayed datagram as it arrived. It is the whole of what a carrier and a volunteer read, and none of
///     it says anything about the match the datagram belongs to.
/// </summary>
/// <param name="Token">The connection the datagram belongs to.</param>
/// <param name="PathId">The path the copy travelled.</param>
/// <param name="DeliveryFlag">The guarantee the inner datagram needs, and whether this datagram is a probe.</param>
/// <param name="MessageSequence">The sequence the duplicate window reads, shared by every copy of one inner datagram.</param>
/// <param name="PathSequence">The per-path sequence, which is what that path's loss is measured from.</param>
public readonly record struct RelayPacketHeader(RelayConnectionToken Token, byte PathId, byte DeliveryFlag, int MessageSequence, int PathSequence)
{
	/// <summary>Whether this datagram is the path layer's own probe rather than one for the match.</summary>
	public bool IsProbe => RelayEnvelope.IsProbe(DeliveryFlag);

	/// <summary>Whether this datagram answers a probe rather than asking one.</summary>
	public bool IsProbeResponse => RelayEnvelope.IsProbeResponse(DeliveryFlag);

	/// <summary>The guarantee the inner datagram needs.</summary>
	public FrameDelivery Delivery => RelayEnvelope.DeliveryOf(DeliveryFlag);
}
