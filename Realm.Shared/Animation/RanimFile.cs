using System;
using System.IO;
using MemoryPack;
using Realm.Shared.Metadata;

namespace Realm.Shared.Animation;

public class RanimFile : RealmContainerFile
{
	public static readonly byte[] MagicBytes = [0x52, 0x41, 0x4E, 0x4D]; // "RANM"
	public static byte[] Magic => MagicBytes;
	public const uint CurrentVersion = 1;
	public const string Format = "ranim";
	public const string Name = "RANIM";

	public override ReadOnlySpan<byte> ContainerMagic => MagicBytes;
	public override string ContainerFormatName => Name;
	public override string ContainerFormatExtension => Format;
	public override bool DefaultCompressed => true;

	public static bool IsRanimBytes(ReadOnlySpan<byte> bytes) => HasMagic(bytes, MagicBytes);

	public static (string? MetadataJson, RealmAnimationData AnimationData, uint Version) Parse(ReadOnlySpan<byte> bytes)
	{
		var (metadataJson, decompressedPayload, version) = ParsePayload(bytes, MagicBytes, Name, defaultCompressed: true);
		var animationData = decompressedPayload.Length > 0
			? (MemoryPackSerializer.Deserialize<RealmAnimationData>(decompressedPayload) ?? new RealmAnimationData())
			: new RealmAnimationData();
		return (metadataJson, animationData, version);
	}

	public static byte[] Build(string? metadataJson, byte[] memoryPackBytes, bool? compressed = null, uint version = CurrentVersion)
	{
		return BuildContainer(
			MagicBytes,
			Format,
			metadataJson,
			memoryPackBytes ?? Array.Empty<byte>(),
			compressed,
			version,
			defaultCompressed: true,
			configureMetadata: metaObj => metaObj["asset_type"] ??= "Animation");
	}

	public static byte[] Build(string? metadataJson, RealmAnimationData animationData, bool? compressed = null, uint version = CurrentVersion)
	{
		byte[] memoryPackBytes = animationData != null
			? MemoryPackSerializer.Serialize(animationData)
			: Array.Empty<byte>();

		return Build(metadataJson, memoryPackBytes, compressed, version);
	}

	public static string? ExtractMetadata(ReadOnlySpan<byte> bytes) => ExtractMetadataFromBytes(bytes, MagicBytes);
	public static string? ExtractMetadata(Stream stream) => ExtractMetadataFromStream(stream, MagicBytes);
	public static string? ExtractMetadataFromFile(string filePath) => ExtractMetadataFromPath(filePath, MagicBytes);

	public static byte[] SetMetadata(ReadOnlySpan<byte> bytes, string? newMetadataJson)
	{
		return SetContainerMetadata(bytes, MagicBytes, Name, Format, newMetadataJson, configureMetadata: metaObj => metaObj["asset_type"] ??= "Animation");
	}
}
