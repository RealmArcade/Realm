namespace Realm.Client.Utils;

public readonly struct SwatchSlotInfo
{
	public readonly int SlotIndex;
	public readonly string? BaseName;
	public readonly string? FileName;
	public readonly bool IsFiller;
	public readonly TextureMetadata? MetadataNode;

	public SwatchSlotInfo(int slotIndex, string? baseName, string? fileName, bool isFiller, TextureMetadata? metadataNode)
	{
		SlotIndex = slotIndex;
		BaseName = baseName;
		FileName = fileName;
		IsFiller = isFiller;
		MetadataNode = metadataNode;
	}
}