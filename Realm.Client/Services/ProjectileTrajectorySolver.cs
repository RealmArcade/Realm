using System;
using System.Numerics;

namespace Realm.Client.Services;

/// <summary>
///     Where a projectile moves next for each trajectory kind: straight along its heading, out and back for a
///     boomerang, or turning towards a target that may itself be moving. Each step reads and writes the caller's
///     position (and heading, for homing) and reports whether the flight is over, so the node keeps the state and the
///     rules live here.
/// </summary>
internal static class ProjectileTrajectorySolver
{
	/// <summary>Straight flight: advance along the heading. Ends when the flight time is spent.</summary>
	internal static bool StepLinear(Vector3 direction, float speed, float dt, float rawT, ref Vector3 position)
	{
		position += direction * (speed * dt);
		return rawT >= 1.0f;
	}

	/// <summary>
	///     Out to the target, a pause, then back to where it was fired from. The return delay falls back to 0.2s
	///     when the weapon does not name one, and each leg is eased by the weapon's curve.
	/// </summary>
	internal static bool StepBoomerang(float elapsedTime, float flightDuration, float returnDelay,
		ProjectileEaseCurve easeCurve, Vector3 start, Vector3 target, ref Vector3 position)
	{
		float delay = returnDelay >= 0.0f ? returnDelay : 0.2f;
		float outboundDuration = flightDuration;
		float returnDuration = flightDuration;

		if (elapsedTime <= outboundDuration)
		{
			float outT = Math.Clamp(elapsedTime / outboundDuration, 0.0f, 1.0f);
			position = Vector3.Lerp(start, target, ProjectileFlightMath.Ease(outT, easeCurve));
			return false;
		}

		if (elapsedTime <= outboundDuration + delay)
		{
			position = target;
			return false;
		}

		if (elapsedTime <= outboundDuration + delay + returnDuration)
		{
			float returnT = Math.Clamp((elapsedTime - outboundDuration - delay) / returnDuration, 0.0f, 1.0f);
			position = Vector3.Lerp(target, start, ProjectileFlightMath.Ease(returnT, easeCurve));
			return false;
		}

		return true;
	}

	/// <summary>
	///     Homing with a turn limit, or the parabolic blend between where the target was and where it is. A weapon with
	///     both a turn limit and a homing weight flies the first way; otherwise the path is the eased straight line to
	///     the blended target and the heading is left alone.
	/// </summary>
	internal static bool StepHomingOrParabolic(Vector3 start, Vector3 initialTarget, Vector3 currentTarget,
		float speed, float dt, float rawT, float turnRateLimit, float homingWeight, ProjectileEaseCurve easeCurve,
		ref Vector3 position, ref Vector3 direction)
	{
		float easedT = ProjectileFlightMath.Ease(rawT, easeCurve);

		if (turnRateLimit > 0.0f && homingWeight > 0.0f)
		{
			Vector3 toTarget = currentTarget - position;
			float distToTarget = toTarget.Length();
			if (distToTarget > 0.001f)
			{
				Vector3 desiredDirection = toTarget / distToTarget;
				float maxTurnRadians = turnRateLimit * (MathF.PI / 180f) * dt * homingWeight;
				float angleBetween = AngleBetween(direction, desiredDirection);
				if (angleBetween > 0.0001f)
				{
					float step = MathF.Min(1.0f, maxTurnRadians / angleBetween);
					direction = Vector3.Normalize(Slerp(direction, desiredDirection, step));
				}
			}

			position += direction * (speed * dt);
			return distToTarget <= MathF.Max(0.5f, speed * dt * 1.5f) || rawT >= 1.0f;
		}

		Vector3 effectiveTarget = Vector3.Lerp(initialTarget, currentTarget, Math.Clamp(homingWeight * easedT, 0.0f, 1.0f));
		position = Vector3.Lerp(start, effectiveTarget, easedT);
		return rawT >= 1.0f;
	}

	private static float AngleBetween(Vector3 from, Vector3 to)
	{
		float dot = Math.Clamp(Vector3.Dot(from, to), -1.0f, 1.0f);
		return MathF.Acos(dot);
	}

	/// <summary>
	///     Spherical interpolation along the great arc between two unit vectors, matching the engine's
	///     <c>Vector3.Slerp</c> for normalised input.
	/// </summary>
	private static Vector3 Slerp(Vector3 from, Vector3 to, float weight)
	{
		float dot = Math.Clamp(Vector3.Dot(from, to), -1.0f, 1.0f);
		float theta = MathF.Acos(dot);
		float sinTheta = MathF.Sin(theta);

		if (MathF.Abs(sinTheta) < 1e-6f)
		{
			return weight < 1.0f ? from : to;
		}

		float fromWeight = MathF.Sin((1.0f - weight) * theta) / sinTheta;
		float toWeight = MathF.Sin(weight * theta) / sinTheta;
		return from * fromWeight + to * toWeight;
	}
}
