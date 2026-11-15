using System.Numerics;
using System.Text.Json.Serialization;
using Realm.Shared.Serialization;

namespace Realm.Shared.Terrain;

[SchemaDescription("A point, direction or size in three dimensions, written as one number per axis.")]
public struct Vector3Data
{
	[JsonPropertyName("X")]
	[SchemaDescription("The distance along the first horizontal axis.")]
	public float X { get; set; }

	[JsonPropertyName("Y")]
	[SchemaDescription("The distance along the vertical axis.")]
	public float Y { get; set; }

	[JsonPropertyName("Z")]
	[SchemaDescription("The distance along the second horizontal axis.")]
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