using System;
using ZstdSharp;

namespace Realm.Shared.Metadata;

public static class RealmCompressionHelper
{
	public const int DefaultCompressionLevel = 3;
	public static readonly byte[] ZstdMagic = [0x28, 0xB5, 0x2F, 0xFD];

	public static bool IsZstdCompressed(ReadOnlySpan<byte> bytes)
	{
		return bytes.Length >= 4 &&
		       bytes[0] == 0x28 &&
		       bytes[1] == 0xB5 &&
		       bytes[2] == 0x2F &&
		       bytes[3] == 0xFD;
	}

	public static byte[] Compress(ReadOnlySpan<byte> uncompressedBytes, int level = DefaultCompressionLevel)
	{
		if (uncompressedBytes.IsEmpty)
		{
			return Array.Empty<byte>();
		}

		using var compressor = new Compressor(level);
		return compressor.Wrap(uncompressedBytes).ToArray();
	}

	public static byte[] Decompress(ReadOnlySpan<byte> compressedBytes)
	{
		if (compressedBytes.IsEmpty)
		{
			return Array.Empty<byte>();
		}

		using var decompressor = new Decompressor();
		return decompressor.Unwrap(compressedBytes).ToArray();
	}
}
