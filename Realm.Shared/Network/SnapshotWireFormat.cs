using System;
using System.Buffers.Binary;
using System.IO;
using ZstdSharp;

namespace Realm.Shared.Network;

/// <summary>
///     The compressors and scratch buffer one publish lane encodes with. A lane keeps its own for the life of the match
///     and disposes them with it, so encoding no longer depends on which pool thread happens to run a lane's work, and
///     the native compressors have an owner that can release them instead of living until the process ends.
/// </summary>
public class SnapshotCompressor : IDisposable
{
	private Compressor? _deltaCompressor;
	private Compressor? _baselineCompressor;
	private byte[]? _compressionBuffer;
	private bool _disposed;

	public byte[] EncodeDelta(ReadOnlySpan<byte> serialized)
	{
		if (serialized.Length == 0) return Array.Empty<byte>();

		return SnapshotWireFormat.EncodeWith(_deltaCompressor ??= new Compressor(SnapshotWireFormat.DeltaCompressionLevel), ref _compressionBuffer, serialized);
	}

	public byte[] EncodeBaseline(ReadOnlySpan<byte> serialized)
	{
		if (serialized.Length == 0) return Array.Empty<byte>();

		return SnapshotWireFormat.EncodeWith(_baselineCompressor ??= new Compressor(SnapshotWireFormat.DeltaCompressionLevel), ref _compressionBuffer, serialized);
	}

	public void Dispose()
	{
		if (_disposed) return;
		_disposed = true;

		_deltaCompressor?.Dispose();
		_baselineCompressor?.Dispose();
		_deltaCompressor = null;
		_baselineCompressor = null;
		_compressionBuffer = null;
	}
}

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

		return EncodeWith(compressor, ref _compressionBuffer, serialized);
	}

	internal static byte[] EncodeWith(Compressor compressor, ref byte[]? compressionBuffer, ReadOnlySpan<byte> serialized)
	{
		int bound = Compressor.GetCompressBound(serialized.Length);
		byte[] buffer = compressionBuffer ??= new byte[bound];
		if (buffer.Length < bound) buffer = compressionBuffer = new byte[bound];

		int compressedLength = compressor.Wrap(serialized, buffer);
		byte[] frame = new byte[FrameHeaderBytes + compressedLength];
		BinaryPrimitives.WriteUInt32LittleEndian(frame, FrameMagic);
		BinaryPrimitives.WriteInt32LittleEndian(frame.AsSpan(4), serialized.Length);
		buffer.AsSpan(0, compressedLength).CopyTo(frame.AsSpan(FrameHeaderBytes));
		return frame;
	}

	/// <summary>
	///     Decompresses a frame into a buffer the caller keeps, so a screen reading thirty descriptions a second does not
	///     hand the allocator a new buffer of the frame's own size for each of them. A baseline is over a hundred kilobytes,
	///     which is a large-object allocation every time one arrives.
	/// </summary>
	public static ReadOnlySpan<byte> Decode(byte[] frame, ref byte[] serializedBuffer)
	{
		if (frame.Length == 0) return ReadOnlySpan<byte>.Empty;

		if (frame.Length < FrameHeaderBytes || BinaryPrimitives.ReadUInt32LittleEndian(frame) != FrameMagic)
		{
			throw new InvalidDataException($"A snapshot frame of {frame.Length} bytes does not carry the frame magic.");
		}

		int serializedLength = BinaryPrimitives.ReadInt32LittleEndian(frame.AsSpan(4));
		if (serializedLength <= 0 || serializedLength > MaximumSerializedSnapshotBytes)
		{
			throw new InvalidDataException($"A snapshot frame declares {serializedLength} serialized bytes, which is not a size a snapshot can be.");
		}

		if (serializedBuffer.Length < serializedLength) serializedBuffer = new byte[serializedLength];

		Decompressor decompressor = _decompressor ??= new Decompressor();
		int written = decompressor.Unwrap(frame.AsSpan(FrameHeaderBytes), serializedBuffer.AsSpan(0, serializedLength));
		if (written != serializedLength)
		{
			throw new InvalidDataException($"A snapshot frame of {frame.Length} bytes decompressed to {written} bytes instead of its declared {serializedLength}.");
		}

		return serializedBuffer.AsSpan(0, serializedLength);
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
