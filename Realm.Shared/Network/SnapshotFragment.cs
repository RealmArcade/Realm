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

	private static readonly uint FragmentMagic = BinaryPrimitives.ReadUInt32LittleEndian("SZF1"u8);

	public static int CountFor(int frameLength)
	{
		int payloadBytes = MaximumDatagramBytes - HeaderBytes;
		if (frameLength <= payloadBytes) return 1;

		return (frameLength + payloadBytes - 1) / payloadBytes;
	}

	public static void Write(byte[] frame, int sequence, int fragmentIndex, int fragmentCount, Span<byte> destination)
	{
		BinaryPrimitives.WriteUInt32LittleEndian(destination, FragmentMagic);
		BinaryPrimitives.WriteInt32LittleEndian(destination.Slice(4), sequence);
		BinaryPrimitives.WriteUInt16LittleEndian(destination.Slice(8), (ushort)fragmentIndex);
		BinaryPrimitives.WriteUInt16LittleEndian(destination.Slice(10), (ushort)fragmentCount);

		int payloadBytes = MaximumDatagramBytes - HeaderBytes;
		int offset = fragmentIndex * payloadBytes;
		int length = Math.Min(payloadBytes, frame.Length - offset);
		frame.AsSpan(offset, length).CopyTo(destination.Slice(HeaderBytes));
	}

	public static int PayloadLengthFor(int frameLength, int fragmentIndex)
	{
		int payloadBytes = MaximumDatagramBytes - HeaderBytes;
		return Math.Min(payloadBytes, frameLength - fragmentIndex * payloadBytes);
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
