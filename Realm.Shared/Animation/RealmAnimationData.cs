using MemoryPack;

namespace Realm.Shared.Animation;

[MemoryPackable]
public partial class RealmAnimationData
{
	public int FormatVersion { get; set; } = 1;
	public string Name { get; set; } = string.Empty;
	public float Duration { get; set; }
	public float FrameRate { get; set; } = 30.0f;
	public RealmAnimationLoopMode LoopMode { get; set; } = RealmAnimationLoopMode.Linear;
	public RealmAnimationBoneTrack[] Tracks { get; set; } = Array.Empty<RealmAnimationBoneTrack>();
}