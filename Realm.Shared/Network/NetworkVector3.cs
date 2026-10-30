using MemoryPack;

namespace Realm.Shared.Network;

[MemoryPackable]
public partial struct NetworkVector3
{
	public float X;
	public float Y;
	public float Z;

	public NetworkVector3(float x, float y, float z)
	{
		X = x;
		Y = y;
		Z = z;
	}

	public System.Numerics.Vector3 ToNumerics() => new System.Numerics.Vector3(X, Y, Z);
}
