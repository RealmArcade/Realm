using System.Numerics;
using System.Text.Json.Serialization;

namespace Realm.Shared.Terrain;

public struct Vector3Data
{
	[JsonPropertyName("X")]
	public float X { get; set; }

	[JsonPropertyName("Y")]
	public float Y { get; set; }

	[JsonPropertyName("Z")]
	public float Z { get; set; }

	public Vector3Data(float x, float y, float z)
	{
		X = x;
		Y = y;
		Z = z;
	}

	public static implicit operator Vector3(Vector3Data v) => new(v.X, v.Y, v.Z);
	public static implicit operator Vector3Data(Vector3 v) => new(v.X, v.Y, v.Z);

	public static Vector3Data Zero => new(0f, 0f, 0f);
	public static Vector3Data One => new(1f, 1f, 1f);
}