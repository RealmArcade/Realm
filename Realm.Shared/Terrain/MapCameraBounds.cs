using System;

namespace Realm.Shared.Terrain;

public readonly struct MapCameraBounds
{
	public const float LegacyDefaultLeft = -95.0f;
	public const float LegacyDefaultRight = 95.0f;
	public const float LegacyDefaultTop = -95.0f;
	public const float LegacyDefaultBottom = 125.0f;

	private const float MatchTolerance = 0.05f;
	private const double SnapTolerance = 0.0005;

	public MapCameraBounds(float left, float right, float top, float bottom)
	{
		Left = left;
		Right = right;
		Top = top;
		Bottom = bottom;
	}

	public float Left { get; }

	public float Right { get; }

	public float Top { get; }

	public float Bottom { get; }

	public float Width => Right - Left;

	public float Depth => Bottom - Top;

	public static MapCameraBounds FromTerrain(int width, int depth, float quadSize)
	{
		float halfWidth = width / 2.0f * quadSize;
		float halfDepth = depth / 2.0f * quadSize;
		return new MapCameraBounds(-halfWidth, halfWidth, -halfDepth, halfDepth);
	}

	public static MapCameraBounds LegacyDefaults => new(LegacyDefaultLeft, LegacyDefaultRight, LegacyDefaultTop, LegacyDefaultBottom);

	public bool Matches(MapCameraBounds other, float tolerance = MatchTolerance)
	{
		return MathF.Abs(Left - other.Left) <= tolerance
		       && MathF.Abs(Right - other.Right) <= tolerance
		       && MathF.Abs(Top - other.Top) <= tolerance
		       && MathF.Abs(Bottom - other.Bottom) <= tolerance;
	}

	public bool MatchesTerrain(int width, int depth, float quadSize)
	{
		return Matches(FromTerrain(width, depth, quadSize));
	}

	public MapCameraBounds MapTo(MapCameraBounds oldMapBounds, MapCameraBounds newMapBounds)
	{
		if (Matches(oldMapBounds) || Matches(LegacyDefaults))
		{
			return newMapBounds;
		}

		return new MapCameraBounds(
			MapAxis(Left, oldMapBounds.Left, oldMapBounds.Width, newMapBounds.Left, newMapBounds.Width),
			MapAxis(Right, oldMapBounds.Left, oldMapBounds.Width, newMapBounds.Left, newMapBounds.Width),
			MapAxis(Top, oldMapBounds.Top, oldMapBounds.Depth, newMapBounds.Top, newMapBounds.Depth),
			MapAxis(Bottom, oldMapBounds.Top, oldMapBounds.Depth, newMapBounds.Top, newMapBounds.Depth));
	}

	private static float MapAxis(float value, float oldStart, float oldSize, float newStart, float newSize)
	{
		if (MathF.Abs(oldSize) < 0.0001f)
		{
			return Snap(value, newStart, newStart + newSize);
		}

		double ratio = newSize / (double)oldSize;
		double mapped = newStart + (value - oldStart) * ratio;

		return Snap((float)mapped, newStart, newStart + newSize);
	}

	private static float Snap(float value, float rangeStart, float rangeEnd)
	{
		if (Math.Abs(value - rangeStart) <= SnapTolerance) return rangeStart;
		if (Math.Abs(value - rangeEnd) <= SnapTolerance) return rangeEnd;
		if (Math.Abs(value) <= SnapTolerance) return 0.0f;
		return value;
	}
}
