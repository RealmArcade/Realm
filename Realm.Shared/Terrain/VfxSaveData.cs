using Realm.Shared.Metadata;

namespace Realm.Shared.Terrain;

public class VfxSaveData
{
	public string VfxId { get; set; } = string.Empty;
	public float PosX { get; set; }
	public float PosY { get; set; }
	public float PosZ { get; set; }
	public float RotationX { get; set; }
	public float RotationY { get; set; }
	public float RotationZ { get; set; }
	public float ScaleX { get; set; } = 1.0f;
	public float ScaleY { get; set; } = 1.0f;
	public float ScaleZ { get; set; } = 1.0f;
	public float NormalOffset { get; set; }
	public VfxAttachmentConfig? Config { get; set; }
}