using System.Buffers.Binary;
using System.Diagnostics.CodeAnalysis;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Realm.Shared.Textures;
using SkiaSharp;

namespace Realm.Shared;

public static partial class GlbManifestUtils
	{
	private static readonly uint GlbMagic = BinaryPrimitives.ReadUInt32LittleEndian("glTF"u8);
	private static readonly uint JsonChunkType = BinaryPrimitives.ReadUInt32LittleEndian("JSON"u8);
	private static readonly uint BinChunkType = BinaryPrimitives.ReadUInt32LittleEndian("BIN\0"u8);

	private const byte GlbJsonPaddingSpace = (byte)' ';

	public static bool IsValidGlb(byte[] glbBytes)
	{
		if (glbBytes == null || glbBytes.Length < 20) return false;
		uint magic = BitConverter.ToUInt32(glbBytes, 0);
		return magic == GlbMagic;
	}

}
