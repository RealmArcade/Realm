using System;
using System.Buffers.Binary;
using System.IO;
using ZstdSharp;

namespace Realm.Client;

/// <summary>
/// The wire form of every snapshot the host sends. A baseline carries the entire replicated world — a component record
/// for every visible unit, the ability state of every unit, the catalog and the cold payloads — and a delta carries the
/// component records, unit records and (for a peer in combat) the cooldown state that changed since the last one. Both
/// are dominated by repeated ability ids, repeated component type ids and repeated catalog strings, and a delta pays a
/// thirteen byte header per component record, so both are framed through Zstandard before they reach the transport: a
/// per-tick delta falls to about a third of its serialized size and a baseline to about a seventh.
/// A frame states its uncompressed length, so a receiver allocates the exact buffer once. Its magic is the protocol
/// version: both instances come from the same build, so a frame that does not carry it is a defect rather than
/// something to tolerate.
/// </summary>
public static class SnapshotWireFormat
{
	private const int FrameHeaderBytes = 8;
	private const uint FrameMagic = 0x31545A53;

	/// <summary>
	/// A delta is a few hundred bytes and travels on a per-tick path, so its level is the one that removes the most
	/// redundancy per byte: the managed Zstandard encoder spends about twenty microseconds on it either way.
	/// </summary>
	private const int DeltaCompressionLevel = 3;

	/// <summary>
	/// A baseline is two orders of magnitude larger and is framed once per peer that is due one, several of them on the
	/// same tick. Its level is chosen so that framing does not cost the host more than a millisecond per baseline, which
	/// is worth more than the five percent of a baseline that a slower level would save.
	/// </summary>
	private const int BaselineCompressionLevel = 1;

	private static Compressor? _deltaCompressor;
	private static Compressor? _baselineCompressor;
	private static Decompressor? _decompressor;

	[ThreadStatic]
	private static byte[]? _compressionBuffer;

	public static byte[] EncodeBaseline(byte[] serialized)
	{
		return Encode(serialized, BaselineCompressionLevel, ref _baselineCompressor);
	}

	public static byte[] EncodeDelta(byte[] serialized)
	{
		return Encode(serialized, DeltaCompressionLevel, ref _deltaCompressor);
	}

	/// <summary>
	/// Compresses a serialized snapshot into a frame. Both the compressor and the scratch buffer are reused, so framing
	/// a per-tick payload allocates only the frame the transport is about to copy.
	/// </summary>
	private static byte[] Encode(byte[] serialized, int compressionLevel, ref Compressor? cachedCompressor)
	{
		if (serialized.Length == 0) return serialized;

		Compressor compressor = cachedCompressor ??= new Compressor(compressionLevel);
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
