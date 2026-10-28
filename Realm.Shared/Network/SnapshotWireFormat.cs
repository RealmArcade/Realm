using System;
using System.Buffers.Binary;
using System.IO;
using ZstdSharp;

namespace Realm.Shared.Network;

/// <summary>
/// The wire form of every snapshot the host sends. A baseline carries the entire replicated world — a component record
/// for every visible unit, the ability state of every unit, the catalog and the cold payloads — and a delta carries the
/// component records, unit records and (for a peer in combat) the cooldown state that changed since the last one. Both
/// are dominated by repeated ability ids, repeated component type ids and repeated catalog strings, and a delta pays a
/// thirteen byte header per component record, so both are framed through Zstandard before they reach the transport: a
/// per-tick delta falls to about a fifth of its serialized size and a baseline to about a seventh. A delta is now mostly
/// one <see cref="UnitSection" />, which states the units field by field rather than one record at a time: homogeneous
/// columns are what an encoder finds redundancy in, and they measured a tenth off the frame of a peer in a three hundred
/// unit room against the same units as records.
/// A frame states its uncompressed length, so a receiver allocates the exact buffer once. Its magic is the protocol
/// version: both instances come from the same build, so a frame that does not carry it is a defect rather than
/// something to tolerate.
/// </summary>
public static class SnapshotWireFormat
{
	/// <summary>How many bytes a frame spends stating its magic and the length of the snapshot behind it.</summary>
	public const int FrameHeaderBytes = 8;
	private const uint FrameMagic = 0x31545A53;

	/// <summary>
	/// A delta is framed on a per-tick path, so its level is the cheapest one that reaches the size the frame settles at.
	/// Measured over 120 consecutive ticks of one peer, a delta of about 3.1 kB frames to 658 bytes at level 1 and 671
	/// bytes at level 3, for 44 and 62 microseconds respectively; the levels above 3 buy 3 percent of bytes for five to
	/// forty times the encoder's time, which is a bad trade for a stream sent thirty times a second.
	/// </summary>
	public const int DeltaCompressionLevel = 1;

	/// <summary>
	/// A baseline is two orders of magnitude larger and is framed once per peer that is due one, several of them on the
	/// same tick. Its level is chosen so that framing does not cost the host more than a millisecond per baseline, which
	/// is worth more than the five percent of a baseline that a slower level would save.
	/// </summary>
	private const int BaselineCompressionLevel = 1;

	/// <summary>
	///     The encoder of one thread. A match describes several peers at once, so each of those threads frames its own
	///     peer with its own encoder: a compressor carries state between the frames it produces, and two threads inside
	///     one would corrupt both streams.
	/// </summary>
	[ThreadStatic]
	private static Compressor? _deltaCompressor;

	[ThreadStatic]
	private static Compressor? _baselineCompressor;

	[ThreadStatic]
	private static Decompressor? _decompressor;

	[ThreadStatic]
	private static byte[]? _compressionBuffer;

	public static byte[] EncodeBaseline(byte[] serialized)
	{
		return Encode(serialized, isBaseline: true);
	}

	public static byte[] EncodeDelta(byte[] serialized)
	{
		return Encode(serialized, isBaseline: false);
	}

	/// <summary>
	/// Compresses a serialized snapshot into a frame. Both the compressor and the scratch buffer are reused, so framing
	/// a per-tick payload allocates only the frame the transport is about to copy.
	/// </summary>
	private static byte[] Encode(byte[] serialized, bool isBaseline)
	{
		if (serialized.Length == 0) return serialized;

		Compressor compressor = isBaseline
			? _baselineCompressor ??= new Compressor(BaselineCompressionLevel)
			: _deltaCompressor ??= new Compressor(DeltaCompressionLevel);
		int bound = Compressor.GetCompressBound(serialized.Length);
		byte[] buffer = _compressionBuffer ??= new byte[bound];
		if (buffer.Length < bound) buffer = _compressionBuffer = new byte[bound];

		int compressedLength = compressor.Wrap(serialized, buffer);
		byte[] frame = new byte[FrameHeaderBytes + compressedLength];
		BinaryPrimitives.WriteUInt32LittleEndian(frame, FrameMagic);
		BinaryPrimitives.WriteInt32LittleEndian(frame.AsSpan(4), serialized.Length);
		buffer.AsSpan(0, compressedLength).CopyTo(frame.AsSpan(FrameHeaderBytes));
		return frame;
	}

	/// <summary>
	/// Restores the serialized snapshot a frame carries. The declared size is read before decompressing so the exact
	/// buffer is allocated once, and a frame whose header or contents do not match it fails loudly.
	/// </summary>
	public static byte[] Decode(byte[] frame)
	{
		if (frame.Length == 0) return frame;

		if (frame.Length < FrameHeaderBytes || BinaryPrimitives.ReadUInt32LittleEndian(frame) != FrameMagic)
		{
			throw new InvalidDataException($"A snapshot frame of {frame.Length} bytes does not carry the frame magic.");
		}

		int serializedLength = BinaryPrimitives.ReadInt32LittleEndian(frame.AsSpan(4));
		if (serializedLength <= 0)
		{
			throw new InvalidDataException($"A snapshot frame declares {serializedLength} serialized bytes.");
		}

		Decompressor decompressor = _decompressor ??= new Decompressor();
		byte[] serialized = new byte[serializedLength];
		int written = decompressor.Unwrap(frame.AsSpan(FrameHeaderBytes), serialized);
		if (written != serializedLength)
		{
			throw new InvalidDataException($"A snapshot frame of {frame.Length} bytes decompressed to {written} bytes instead of its declared {serializedLength}.");
		}

		return serialized;
	}
}
