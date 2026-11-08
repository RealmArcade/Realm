using System;
using System.Numerics;

namespace Realm.Client.Services;

/// <summary>
///     Whether the line of sight from an eye point to a target passes through an axis-aligned box: the slab test a
///     building occluder is decided by. It works on plain vectors so the answer does not need a scene.
/// </summary>
internal static class OcclusionSegmentQuery
{
	internal static bool SegmentIntersectsBox(Vector3 start, Vector3 end, Vector3 center, Vector3 halfExtents)
	{
		Vector3 direction = end - start;
		float minimum = 0.0f;
		float maximum = 1.0f;

		if (!IntersectAxis(start.X, direction.X, center.X, halfExtents.X, ref minimum, ref maximum)) return false;
		if (!IntersectAxis(start.Y, direction.Y, center.Y, halfExtents.Y, ref minimum, ref maximum)) return false;
		if (!IntersectAxis(start.Z, direction.Z, center.Z, halfExtents.Z, ref minimum, ref maximum)) return false;

		return maximum >= minimum;
	}

	private static bool IntersectAxis(float origin, float direction, float center, float halfExtent, ref float minimum, ref float maximum)
	{
		float boxMinimum = center - halfExtent;
		float boxMaximum = center + halfExtent;

		if (MathF.Abs(direction) < 0.0001f)
		{
			return origin >= boxMinimum && origin <= boxMaximum;
		}

		float first = (boxMinimum - origin) / direction;
		float second = (boxMaximum - origin) / direction;
		if (first > second)
		{
			(first, second) = (second, first);
		}

		minimum = MathF.Max(minimum, first);
		maximum = MathF.Min(maximum, second);
		return maximum >= minimum;
	}
}
