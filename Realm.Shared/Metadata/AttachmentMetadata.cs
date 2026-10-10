using Realm.Shared.Terrain;

namespace Realm.Shared.Metadata;

public class AttachmentMetadata
{
	public string AttachmentId { get; set; } = string.Empty;
	public string Name { get; set; } = string.Empty;
	public string ModelPath { get; set; } = string.Empty;
	public float Scale { get; set; } = 1.0f;
	public Vector3Data PositionOffset { get; set; }
	public Vector3Data RotationOffset { get; set; }
	public string DefaultHand { get; set; } = "RightHand";
	public string? ChildVfxId { get; set; }
	public Vector3Data ChildVfxPosition { get; set; }
	public Vector3Data ChildVfxRotation { get; set; }
	public Vector3Data ChildVfxScale { get; set; } = Vector3Data.One;
}