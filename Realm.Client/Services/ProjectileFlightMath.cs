using System;
using System.Numerics;
using Realm.Shared.Metadata;

namespace Realm.Client.Services;

/// <summary>
///     The curves a projectile flies by: speed over its flight, the arc it rises through, the spiral, zigzag and orbit
///     sway added on top, and the ease that shapes the parametric ones. They are rules about a weapon, not about a
///     node, so they take the metadata and plain vectors and can be checked without a scene.
/// </summary>
public static class ProjectileFlightMath
{
	public static float Speed(float baseSpeed, float elapsedTime, float totalDuration, ProjectileSpeedCurve speedCurve, float acceleration)
	{
		float speed = MathF.Max(0.1f, baseSpeed + acceleration * elapsedTime);

		if (speedCurve == ProjectileSpeedCurve.Constant)
			return speed;

		float progress = Math.Clamp(elapsedTime / MathF.Max(0.001f, totalDuration), 0.0f, 1.0f);

		return speedCurve switch
		{
			ProjectileSpeedCurve.Accelerate => speed * (0.3f + 1.4f * progress * progress),
			ProjectileSpeedCurve.Decelerate => speed * (1.7f - 1.4f * progress * progress),
			ProjectileSpeedCurve.EaseInOut => speed * (0.4f + 1.2f * (progress * progress * (3.0f - 2.0f * progress))),
			ProjectileSpeedCurve.RocketBoost => speed * (0.15f + 2.35f * MathF.Pow(progress, 3.0f)),
			ProjectileSpeedCurve.Burst => speed * (1.0f + 1.5f * MathF.Exp(-4.0f * progress)),
			_ => speed,
		};
	}

	public static float ScaleOverLifetime(float t, ProjectileScaleCurve scaleCurve)
	{
		if (scaleCurve == ProjectileScaleCurve.Constant)
			return 1.0f;

		return scaleCurve switch
		{
			ProjectileScaleCurve.Grow => Math.Clamp(t * 1.5f, 0.001f, 1.0f),
			ProjectileScaleCurve.Shrink => Math.Clamp(1.0f - t, 0.001f, 1.0f),
			ProjectileScaleCurve.GrowShrink => MathF.Max(0.001f, MathF.Sin(Math.Clamp(t, 0.0f, 1.0f) * MathF.PI)),
			ProjectileScaleCurve.SquashStretch => SquashStretchScale(t),
			ProjectileScaleCurve.ImpactShrink => t > 0.8f ? Math.Clamp((1.0f - t) / 0.2f, 0.001f, 1.0f) : 1.0f,
			_ => 1.0f,
		};
	}

	public static float Ease(float t, ProjectileEaseCurve easeCurve)
	{
		return easeCurve switch
		{
			ProjectileEaseCurve.EaseIn => t * t,
			ProjectileEaseCurve.EaseOut => 1.0f - (1.0f - t) * (1.0f - t),
			ProjectileEaseCurve.EaseInOut => t < 0.5f ? 2.0f * t * t : 1.0f - MathF.Pow(-2.0f * t + 2.0f, 2.0f) / 2.0f,
			_ => t,
		};
	}

	/// <summary>
	///     How far a projectile rises above its straight path at <paramref name="rawT" />: one hump, or one hump per
	///     bounce with each later bounce lower than the last.
	/// </summary>
	public static float ArcHeight(float rawT, float arcHeight, int maxBounces)
	{
		if (maxBounces > 0)
		{
			int segments = 1 + maxBounces;
			float segmentProgress = rawT * segments;
			int currentBounce = Math.Min((int)MathF.Floor(segmentProgress), segments - 1);
			float localU = segmentProgress - currentBounce;
			float bounceHeight = arcHeight / (1.0f + currentBounce * 0.6f);
			return 4.0f * bounceHeight * localU * (1.0f - localU);
		}

		if (arcHeight > 0.0f)
		{
			return 4.0f * arcHeight * rawT * (1.0f - rawT);
		}

		return 0.0f;
	}

	/// <summary>
	///     Everything that moves a projectile off its straight path: the arc, then the spiral, zigzag and — for the
	///     swarm-orbit trajectory only — the orbit sway, all measured in the projectile's own right/up basis.
	/// </summary>
	public static Vector3 CurveOffset(WeaponMetadata weapon, bool orbits, float elapsedTime, float rawT, Vector3 right, Vector3 up)
	{
		Vector3 offset = new(0f, ArcHeight(rawT, weapon.ArcHeight, weapon.MaxBounces), 0f);
		offset += SpiralOffset(elapsedTime, weapon.SpiralRadius, weapon.SpiralFrequency, right, up);
		offset += ZigzagOffset(elapsedTime, weapon.ZigzagAmplitude, weapon.ZigzagFrequency, right);
		if (orbits) offset += OrbitOffset(elapsedTime, weapon.OrbitRadius, weapon.OrbitSpeed, right, up);
		return offset;
	}

	private static Vector3 SpiralOffset(float elapsedTime, float radius, float frequency, Vector3 right, Vector3 up)
	{
		if (radius <= 0.0f || frequency <= 0.0f) return Vector3.Zero;
		float theta = MathF.Tau * frequency * elapsedTime;
		return (right * MathF.Cos(theta) + up * MathF.Sin(theta)) * radius;
	}

	private static Vector3 ZigzagOffset(float elapsedTime, float amplitude, float frequency, Vector3 right)
	{
		if (amplitude <= 0.0f || frequency <= 0.0f) return Vector3.Zero;
		float phi = MathF.Tau * frequency * elapsedTime;
		return right * (MathF.Sin(phi) * amplitude);
	}

	private static Vector3 OrbitOffset(float elapsedTime, float radius, float speed, Vector3 right, Vector3 up)
	{
		float effectiveRadius = radius > 0.0f ? radius : 1.0f;
		float effectiveSpeed = speed > 0.0f ? speed : 10.0f;
		float angle = elapsedTime * effectiveSpeed;
		return (right * MathF.Cos(angle) + up * MathF.Sin(angle)) * effectiveRadius;
	}

	private static float SquashStretchScale(float t)
	{
		if (t < 0.2f) return MathF.Max(0.001f, t / 0.2f * 1.2f);
		if (t < 0.4f) return MathF.Max(0.001f, 1.2f - (t - 0.2f) / 0.2f * 0.2f);
		if (t > 0.85f) return MathF.Max(0.001f, (1.0f - t) / 0.15f);
		return 1.0f;
	}

	/// <summary>The weapon metadata spells these curves as strings; anything unrecognised flies as the default.</summary>
	public static ProjectileTrajectoryType ParseTrajectoryType(string? raw)
	{
		if (string.IsNullOrEmpty(raw)) return ProjectileTrajectoryType.Parabolic;
		return raw.Trim().ToLowerInvariant() switch
		{
			"linearvector" or "linear" or "piercing" => ProjectileTrajectoryType.Linear,
			"boomerang" => ProjectileTrajectoryType.Boomerang,
			"swarmorbit" or "swarm" => ProjectileTrajectoryType.SwarmOrbit,
			_ => ProjectileTrajectoryType.Parabolic,
		};
	}

	public static ProjectileSpeedCurve ParseSpeedCurve(string? raw)
	{
		if (string.IsNullOrEmpty(raw)) return ProjectileSpeedCurve.Constant;
		return raw.Trim().ToLowerInvariant() switch
		{
			"accelerate" or "ease_in" => ProjectileSpeedCurve.Accelerate,
			"decelerate" or "ease_out" => ProjectileSpeedCurve.Decelerate,
			"ease_in_out" => ProjectileSpeedCurve.EaseInOut,
			"rocket_boost" => ProjectileSpeedCurve.RocketBoost,
			"burst" => ProjectileSpeedCurve.Burst,
			_ => ProjectileSpeedCurve.Constant,
		};
	}

	public static ProjectileScaleCurve ParseScaleCurve(string? raw)
	{
		if (string.IsNullOrEmpty(raw)) return ProjectileScaleCurve.Constant;
		return raw.Trim().ToLowerInvariant() switch
		{
			"grow" => ProjectileScaleCurve.Grow,
			"shrink" => ProjectileScaleCurve.Shrink,
			"grow_shrink" => ProjectileScaleCurve.GrowShrink,
			"squash_stretch" => ProjectileScaleCurve.SquashStretch,
			"impact_shrink" => ProjectileScaleCurve.ImpactShrink,
			_ => ProjectileScaleCurve.Constant,
		};
	}

	public static ProjectileEaseCurve ParseEaseCurve(string? raw)
	{
		if (string.IsNullOrEmpty(raw)) return ProjectileEaseCurve.Linear;
		return raw.Trim().ToLowerInvariant() switch
		{
			"ease_in" => ProjectileEaseCurve.EaseIn,
			"ease_out" => ProjectileEaseCurve.EaseOut,
			"ease_in_out" => ProjectileEaseCurve.EaseInOut,
			_ => ProjectileEaseCurve.Linear,
		};
	}
}
