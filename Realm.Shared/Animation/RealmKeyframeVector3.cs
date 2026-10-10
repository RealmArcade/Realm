using MemoryPack;

namespace Realm.Shared.Animation;

[MemoryPackable]
public partial struct RealmKeyframeVector3
{
	public float Time { get; set; }
	public float X { get; set; }
	public float Y { get; set; }
	public float Z { get; set; }

	public RealmKeyframeVector3(float time, float x, float y, float z)
	{
		Time = time;
		X = x;
		Y = y;
		Z = z;
	}
}