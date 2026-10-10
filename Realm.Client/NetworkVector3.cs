using MemoryPack;
using Vector3 = Godot.Vector3;

namespace Realm.Client;

/// <summary>Network-serializable representation of a 3D vector.</summary>
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

	public NetworkVector3(Vector3 v)
	{
		X = v.X;
		Y = v.Y;
		Z = v.Z;
	}

	public Vector3 ToGodot() => new Vector3(X, Y, Z);
	public System.Numerics.Vector3 ToNumerics() => new System.Numerics.Vector3(X, Y, Z);
}