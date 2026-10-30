using System;
using System.Buffers.Binary;
using System.IO;
using ZstdSharp;

namespace Realm.Shared.Network;

public static class SnapshotWireFormat
{
	public const int FrameHeaderBytes = 8;
	private static readonly uint FrameMagic = BinaryPrimitives.ReadUInt32LittleEndian("SZT1"u8);

	public const int MaximumSerializedSnapshotBytes = 8 * 1024 * 1024;

	public const int DeltaCompressionLevel = 1;

	private const int BaselineCompressionLevel = 1;

	[ThreadStatic]
	private static Compressor? _deltaCompressor;

	[ThreadStatic]
	private static Compressor? _baselineCompressor;

	[ThreadStatic]
	private static Decompressor? _decompressor;

	[ThreadStatic]
	private static byte[]? _compressionBuffer;

	public static byte[] EncodeBaseline(ReadOnlySpan<byte> serialized)
	{
		return Encode(serialized, isBaseline: true);
	}

	public static byte[] EncodeDelta(ReadOnlySpan<byte> serialized)
	{
		return Encode(serialized, isBaseline: false);
	}

	private static byte[] Encode(ReadOnlySpan<byte> serialized, bool isBaseline)
	{
		if (serialized.Length == 0) return Array.Empty<byte>();

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

	public static byte[] Decode(byte[] frame)
	{
		if (frame.Length == 0) return frame;

		if (frame.Length < FrameHeaderBytes || BinaryPrimitives.ReadUInt32LittleEndian(frame) != FrameMagic)
		{
			throw new InvalidDataException($"A snapshot frame of {frame.Length} bytes does not carry the frame magic.");
		}

		int serializedLength = BinaryPrimitives.ReadInt32LittleEndian(frame.AsSpan(4));
		if (serializedLength <= 0 || serializedLength > MaximumSerializedSnapshotBytes)
		{
			throw new InvalidDataException($"A snapshot frame declares {serializedLength} serialized bytes, which is not a size a snapshot can be.");
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
