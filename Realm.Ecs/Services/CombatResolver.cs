using Realm.Ecs.Components.Combat;

namespace Realm.Ecs.Services;

/// <summary>
///     Encapsulates the result of a 3-stage combat resolution calculation.
/// </summary>
public struct DamageResult
{
	public float RawDamage;
	public float EffectiveFlatArmor;
	public float EffectiveRatedArmor;
	public float MitigatedDamage;
	public float FinalDamage;
	public bool IsCritical;
}

/// <summary>
///     Provides deterministic 3-stage combat resolution and splash damage evaluation.
/// </summary>
public static class CombatResolver
{
	public static CombatConfig DefaultConfig { get; } = new CombatConfig();

	/// <summary>
	///     Resolves damage through Stage 1 (Raw Damage), Stage 2 (Armor Mitigation &amp; Penetration),
	///     and Stage 3 (Type Matrix &amp; Clamping).
	/// </summary>
	public static DamageResult ResolveDamage(
		float baseDamage,
		float damageVariance,
		float critChance,
		float critMultiplier,
		float flatArmor,
		float ratedArmor,
		float flatArmorPen,
		float percentArmorPen,
		string damageType,
		string armorType,
		CombatConfig? config = null,
		Random? rng = null)
	{
		config ??= DefaultConfig;
		rng ??= Random.Shared;

		// Stage 1: Raw Damage Calculation
		float varianceOffset = damageVariance > 0f ? ((float)rng.NextDouble() * 2f - 1f) * damageVariance : 0f;
		float rawDamage = baseDamage + varianceOffset;

		bool isCrit = critChance > 0f && (float)rng.NextDouble() < critChance;
		if (isCrit)
		{
			rawDamage *= critMultiplier;
		}

		// Stage 2: Armor Mitigation & Penetration Layer
		float effectiveFlat = Math.Max(0f, (flatArmor - flatArmorPen) * (1f - percentArmorPen));
		float effectiveRated = Math.Max(0f, ratedArmor * (1f - percentArmorPen));

		float k = config.TuningConstantK;
		float asymptoticMitigation = effectiveRated > 0f ? (1f - (effectiveRated / (effectiveRated + k))) : 1f;
		float mitigatedDamage = Math.Max(config.MinDamageFloor, (rawDamage - effectiveFlat) * asymptoticMitigation);

		// Stage 3: Type Matrix & Health Allocation
		float matrixMultiplier = config.GetMultiplier(damageType, armorType);
		float finalDamage = mitigatedDamage * matrixMultiplier;

		return new DamageResult
		{
			RawDamage = rawDamage,
			EffectiveFlatArmor = effectiveFlat,
			EffectiveRatedArmor = effectiveRated,
			MitigatedDamage = mitigatedDamage,
			FinalDamage = finalDamage,
			IsCritical = isCrit
		};
	}

	/// <summary>
	///     Calculates the splash damage ratio based on distance and splash configuration.
	/// </summary>
	public static float CalculateSplashRatio(
		float distance,
		SplashType splashType,
		float innerRadius,
		float mediumRadius,
		float outerRadius,
		float innerRatio = 1.0f,
		float mediumRatio = 0.5f,
		float outerRatio = 0.25f)
	{
		if (splashType == SplashType.None || outerRadius <= 0f)
		{
			return 0f;
		}

		return splashType switch
		{
			SplashType.RadialStep => CalculateRadialStepRatio(distance, innerRadius, mediumRadius, outerRadius, innerRatio, mediumRatio, outerRatio),
			SplashType.RadialLinear => CalculateRadialLinearRatio(distance, innerRadius, outerRadius, innerRatio, outerRatio),
			_ => 0f
		};
	}

	private static float CalculateRadialStepRatio(
		float distance, float innerRadius, float mediumRadius, float outerRadius,
		float innerRatio, float mediumRatio, float outerRatio)
	{
		if (distance <= innerRadius) return innerRatio;
		if (distance <= mediumRadius) return mediumRatio;
		if (distance <= outerRadius) return outerRatio;
		return 0f;
	}

	private static float CalculateRadialLinearRatio(
		float distance, float innerRadius, float outerRadius,
		float innerRatio, float outerRatio)
	{
		if (distance <= innerRadius) return innerRatio;
		if (distance > outerRadius) return 0f;

		float span = outerRadius - innerRadius;
		if (span <= 0.0001f) return innerRatio;

		float t = (distance - innerRadius) / span;
		return innerRatio + t * (outerRatio - innerRatio);
	}
}
