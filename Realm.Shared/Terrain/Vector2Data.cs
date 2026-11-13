using System.Numerics;
using System.Text.Json.Serialization;
using Realm.Shared.Serialization;

namespace Realm.Shared.Terrain;

[SchemaDescription("A pair of numbers such as a uv scale or a scroll speed, written as one number per axis.")]
public struct Vector2Data
{
	[JsonPropertyName("X")]
	[SchemaDescription("The value along the first axis.")]
	public float X { get; set; }

	[JsonPropertyName("Y")]
	[SchemaDescription("The value along the second axis.")]
	public float Y { get; set; }

	public Vector2Data(float x, float y)
	{
		X = x;
		Y = y;
	}

	public static implicit operator Vector2(Vector2Data v) => new(v.X, v.Y);
	public static implicit operator Vector2Data(Vector2 v) => new(v.X, v.Y);

	public static Vector2Data Zero => new(0f, 0f);
	public static Vector2Data One => new(1f, 1f);
}