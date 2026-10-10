using MemoryPack;

namespace Realm.Shared.Animation;

[MemoryPackable]
public partial struct RealmKeyframeQuaternion
{
	public float Time { get; set; }
	public float X { get; set; }
	public float Y { get; set; }
	public float Z { get; set; }
	public float W { get; set; }

	public RealmKeyframeQuaternion(float time, float x, float y, float z, float w)
	{
		Time = time;
		X = x;
		Y = y;
		Z = z;
		W = w;
	}
}