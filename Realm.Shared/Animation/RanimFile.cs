using MemoryPack;
using Realm.Shared.Metadata;

namespace Realm.Shared.Animation;

public class RanimFile : RealmContainerFile
{
	public static readonly byte[] MagicBytes = "RANM"u8.ToArray();
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
			configureMetadata: metaObj => metaObj[AssetMetadataKeys.AssetType] ??= "Animation");
	}

	public static byte[] Build(string? metadataJson, RealmAnimationData animationData, bool? compressed = null, uint version = CurrentVersion)
	{
		byte[] memoryPackBytes = animationData != null
			? MemoryPackSerializer.Serialize(animationData)
			: Array.Empty<byte>();

		return Build(metadataJson, memoryPackBytes, compressed, version);
	}

}