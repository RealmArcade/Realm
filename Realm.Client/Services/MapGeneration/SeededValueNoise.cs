using System;

namespace Realm.Client.Services.MapGeneration;

public class SeededValueNoise
{
	private readonly int _seed;

	public SeededValueNoise(int seed)
	{
		_seed = seed;
	}

	public float Sample(float x, float y)
	{
		int x0 = FloorToInt(x);
		int y0 = FloorToInt(y);
		int x1 = x0 + 1;
		int y1 = y0 + 1;

		float tx = Smooth(x - x0);
		float ty = Smooth(y - y0);

		float bottomLeft = LatticeValue(x0, y0);
		float bottomRight = LatticeValue(x1, y0);
		float topLeft = LatticeValue(x0, y1);
		float topRight = LatticeValue(x1, y1);

		float bottom = Lerp(bottomLeft, bottomRight, tx);
		float top = Lerp(topLeft, topRight, tx);
		return Lerp(bottom, top, ty);
	}

	private float LatticeValue(int x, int y)
	{
		unchecked
		{
			uint hash = (uint)_seed;
			hash ^= (uint)x * 0x9e3779b1u;
			hash = (hash << 13) | (hash >> 19);
			hash ^= (uint)y * 0x85ebca6bu;
			hash ^= hash >> 16;
			hash *= 0x7feb352du;
			hash ^= hash >> 15;
			hash *= 0x846ca68bu;
			hash ^= hash >> 16;
			return (hash & 0xFFFFFF) / 16777215.0f;
		}
	}

	private static int FloorToInt(float value)
	{
		return (int)MathF.Floor(value);
	}

	private static float Smooth(float value)
	{
		return value * value * (3.0f - 2.0f * value);
	}

	private static float Lerp(float from, float to, float amount)
	{
		return from + (to - from) * amount;
	}
}
