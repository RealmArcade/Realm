using System.Numerics;
using System.Text.Json.Serialization;

namespace Realm.Shared.Terrain;

public struct Vector2Data
{
	[JsonPropertyName("X")]
	public float X { get; set; }

	[JsonPropertyName("Y")]
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