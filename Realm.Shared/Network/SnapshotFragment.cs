using System;
using System.Buffers.Binary;

namespace Realm.Shared.Network;

/// <summary>
///     The header one piece of a description carries while it is being carried as several datagrams. A description too
///     large for one datagram cannot travel on the unordered stream, and sending it on the reliable one blocks every
///     description queued behind it, so it is split instead: a piece that never arrives costs the description it belonged
///     to, which the next description supersedes, rather than costing the stream.
/// </summary>
public static class SnapshotFragment
{
	public const int HeaderBytes = 12;

	/// <summary>
	///     The largest payload one datagram carries, header included. It sits well under the path MTU so a description
	///     that fills it is never fragmented again by the transport.
	/// </summary>
	public const int MaximumDatagramBytes = 1200;

	/// <summary>
	///     The bytes one piece carries once the header is taken out of the datagram.
	/// </summary>
	public const int FragmentPayloadBytes = MaximumDatagramBytes - HeaderBytes;

	/// <summary>
	///     The most pieces one description may be split into. Both ends of the link state it, so a description this match
	///     splits is one the screen it is sent to is built to put back together: a piece count the receiver refused would be
	///     dropped on arrival, asked for again by the acknowledgement, and sent again as the same unassemblable run. It is
	///     far wider than a description reaches in a busy match and far narrower than the serialized snapshot cap, so it
	///     bounds the pieces a screen holds while still leaving the size of a description to be a property of the match.
	/// </summary>
	public const int MaximumFragmentsPerDescription = 1024;

	/// <summary>
	///     The widest description one peer can reassemble, which is what the piece ceiling amounts to in bytes.
	/// </summary>
	public const int MaximumReassembledBytes = MaximumFragmentsPerDescription * FragmentPayloadBytes;

	private static readonly uint FragmentMagic = BinaryPrimitives.ReadUInt32LittleEndian("SZF1"u8);

	/// <summary>
	///     How many datagrams a description of this length travels in. One means it travels as itself, with no header, so
	///     the whole-datagram case and the split case are stated by one rule rather than by each caller deciding.
	/// </summary>
	public static int CountFor(int frameLength)
	{
		if (frameLength <= MaximumDatagramBytes) return 1;

		return (frameLength + FragmentPayloadBytes - 1) / FragmentPayloadBytes;
	}

	public static void Write(byte[] frame, int sequence, int fragmentIndex, int fragmentCount, Span<byte> destination)
	{
		BinaryPrimitives.WriteUInt32LittleEndian(destination, FragmentMagic);
		BinaryPrimitives.WriteInt32LittleEndian(destination.Slice(4), sequence);
		BinaryPrimitives.WriteUInt16LittleEndian(destination.Slice(8), (ushort)fragmentIndex);
		BinaryPrimitives.WriteUInt16LittleEndian(destination.Slice(10), (ushort)fragmentCount);

		int offset = fragmentIndex * FragmentPayloadBytes;
		int length = Math.Min(FragmentPayloadBytes, frame.Length - offset);
		frame.AsSpan(offset, length).CopyTo(destination.Slice(HeaderBytes));
	}

	public static int PayloadLengthFor(int frameLength, int fragmentIndex)
	{
		return Math.Min(FragmentPayloadBytes, frameLength - fragmentIndex * FragmentPayloadBytes);
	}

	public static bool TryReadHeader(ReadOnlySpan<byte> payload, out int sequence, out int fragmentIndex, out int fragmentCount)
	{
		sequence = 0;
		fragmentIndex = 0;
		fragmentCount = 0;

		if (payload.Length < HeaderBytes) return false;
		if (BinaryPrimitives.ReadUInt32LittleEndian(payload) != FragmentMagic) return false;

		sequence = BinaryPrimitives.ReadInt32LittleEndian(payload.Slice(4));
		fragmentIndex = BinaryPrimitives.ReadUInt16LittleEndian(payload.Slice(8));
		fragmentCount = BinaryPrimitives.ReadUInt16LittleEndian(payload.Slice(10));

		return sequence > 0 && fragmentCount > 0 && fragmentIndex < fragmentCount;
	}

	public static ReadOnlySpan<byte> ReadPayload(ReadOnlySpan<byte> payload) => payload.Slice(HeaderBytes);
}
