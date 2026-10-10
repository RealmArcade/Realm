using MemoryPack;

namespace Realm.Shared.Animation;

[MemoryPackable]
public partial class RealmAnimationBoneTrack
{
	public string BoneName { get; set; } = string.Empty;
	public RealmKeyframeVector3[] PositionKeys { get; set; } = Array.Empty<RealmKeyframeVector3>();
	public RealmKeyframeQuaternion[] RotationKeys { get; set; } = Array.Empty<RealmKeyframeQuaternion>();
	public RealmKeyframeVector3[] ScaleKeys { get; set; } = Array.Empty<RealmKeyframeVector3>();
}