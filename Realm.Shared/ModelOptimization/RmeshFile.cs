using System;
using System.Buffers.Binary;
using System.IO;
using Realm.Shared.Metadata;

namespace Realm.Shared.ModelOptimization;

public class RmeshFile : RealmContainerFile
{
	public static readonly byte[] MagicBytes = [0x52, 0x4D, 0x53, 0x48]; // "RMSH"
	public static byte[] Magic => MagicBytes;
	public const uint CurrentVersion = 1;
	public const string Format = "rmesh";
	public const string Name = "RMESH";

	public override ReadOnlySpan<byte> ContainerMagic => MagicBytes;
	public override string ContainerFormatName => Name;
	public override string ContainerFormatExtension => Format;
	public override bool DefaultCompressed => true;

	public static bool IsRmeshBytes(ReadOnlySpan<byte> bytes)
	{
		return HasMagic(bytes, MagicBytes);
	}

	public static (string? MetadataJson, byte[] GlbBytes, uint Version) Parse(ReadOnlySpan<byte> bytes)
	{
		var (metadataJson, decompressedPayload, version) = ParsePayload(bytes, MagicBytes, Name, defaultCompressed: true);
		byte[] glbBytes = ExtractGlbFromDecompressedPayload(decompressedPayload);
		return (metadataJson, glbBytes, version);
	}

	public static byte[] Build(string? metadataJson, byte[] glbBytes, bool? compressed = null, uint version = CurrentVersion)
	{
		return BuildContainer(MagicBytes, Format, metadataJson, glbBytes ?? Array.Empty<byte>(), compressed, version, defaultCompressed: true);
	}

	public static byte[]? GetGlbBytes(ReadOnlySpan<byte> bytes)
	{
		if (!IsRmeshBytes(bytes)) return null;
		var (_, glbBytes, _) = Parse(bytes);
		return glbBytes;
	}

	public static byte[]? GetGlbBytes(Stream stream)
	{
		var (metadataJson, decompressedPayload, _) = ParsePayload(stream, MagicBytes, Name, defaultCompressed: true);
		if (decompressedPayload.Length == 0 && string.IsNullOrEmpty(metadataJson)) return null;
		return ExtractGlbFromDecompressedPayload(decompressedPayload);
	}

	public static byte[]? GetGlbBytesFromFile(string filePath)
	{
		if (!File.Exists(filePath)) return null;
		try
		{
			byte[] bytes = File.ReadAllBytes(filePath);
			return GetGlbBytes(bytes);
		}
		catch
		{
			return null;
		}
	}

	public static string? ExtractMetadata(ReadOnlySpan<byte> bytes)
	{
		return ExtractMetadataFromBytes(bytes, MagicBytes);
	}

	public static string? ExtractMetadata(Stream stream)
	{
		return ExtractMetadataFromStream(stream, MagicBytes);
	}

	public static string? ExtractMetadataFromFile(string filePath)
	{
		return ExtractMetadataFromPath(filePath, MagicBytes);
	}

	public static byte[] SetMetadata(ReadOnlySpan<byte> bytes, string? newMetadataJson)
	{
		return SetContainerMetadata(bytes, MagicBytes, Name, Format, newMetadataJson);
	}

	private static byte[] ExtractGlbFromDecompressedPayload(byte[] payload)
	{
		if (payload.Length >= 8)
		{
			uint glbLength = BinaryPrimitives.ReadUInt32LittleEndian(payload.AsSpan(0, 4));
			if (glbLength > 0 && glbLength <= (uint)(payload.Length - 4))
			{
				byte[] glbBytes = new byte[glbLength];
				Buffer.BlockCopy(payload, 4, glbBytes, 0, (int)glbLength);
				return glbBytes;
			}
		}
		return payload;
	}
}
