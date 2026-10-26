using System;
using Realm.Ecs.Common;
using Realm.Ecs.Components.Combat;
using Realm.Ecs.Components.Stats;

namespace Realm.Ecs.Services;

/// <summary>
///     One ordered damage pipeline for every source: attacks, splash, spells, abilities and
///     periodic effects. The stages, in order, are:
///     <list type="number">
///         <item>Raw damage: authored or scaled magnitude, plus variance, plus an optional critical.</item>
///         <item>Effective resistance: the target's resistance for the damage type, reduced by
///             percentage penetration and then by flat penetration, never below zero. Pure damage
///             skips this stage entirely.</item>
///         <item>Mitigation: <c>R / (R + K)</c>, capped by <see cref="CombatConfig.MaxResistMitigation" />.</item>
///         <item>Damage floor: a resisted hit still deals <see cref="CombatConfig.MinDamageFloor" />,
///             while pure damage keeps whatever it rolled.</item>
///     </list>
///     Percentage resistance bonuses never re-enter this pipeline: they are folded into the
///     resistance points once, by the stat pipeline, so no chain of percentage effects can
///     compound with itself.
/// </summary>
public static class CombatResolver
{
	/// <summary>Resolves one hit.</summary>
	public static DamageResult ResolveDamage(
		float baseDamage,
		float damageVariance,
		float critChance,
		float critMultiplier,
		float physicalResistance,
		float magicResistance,
		float flatPenetration,
		float percentPenetration,
		string? damageType,
		CombatConfig? config = null,
		Random? rng = null,
		SimulationContext? context = null)
	{
		config ??= context?.ActiveCombatConfig ?? new CombatConfig();
		rng ??= Random.Shared;

		// Stage 1: raw.
		float varianceOffset = damageVariance > 0f ? ((float)rng.NextDouble() * 2f - 1f) * damageVariance : 0f;
		float rawDamage = MathF.Max(0f, baseDamage + varianceOffset);

		bool isCrit = critChance > 0f && (float)rng.NextDouble() < critChance;
		if (isCrit)
		{
			rawDamage *= critMultiplier;
		}

		string type = DamageTypes.Normalize(damageType);
		bool pure = DamageTypes.IsPure(type);

		// Stage 2: effective resistance after penetration, percentage first then flat.
		float effectiveResistance = 0f;
		if (!pure)
		{
			float resistance = DamageTypes.IsMagic(type) ? magicResistance : physicalResistance;
			float afterPercent = MathF.Max(0f, resistance) * (1f - Math.Clamp(percentPenetration, 0f, 1f));
			effectiveResistance = MathF.Max(0f, afterPercent - MathF.Max(0f, flatPenetration));
		}

		// Stage 3: mitigation, then the floor that keeps a resisted hit from being worth nothing.
		float k = MathF.Max(0.0001f, config.ResistTuningConstantK);
		float mitigation = pure ? 0f : MathF.Min(config.MaxResistMitigation, effectiveResistance / (effectiveResistance + k));
		float mitigatedDamage = rawDamage * (1f - mitigation);

		// Stage 4: the floor. Pure damage is not a resisted hit, so it is exempt.
		float finalDamage = pure ? MathF.Max(0f, mitigatedDamage) : MathF.Max(config.MinDamageFloor, mitigatedDamage);

		return new DamageResult
		{
			RawDamage = rawDamage,
			EffectiveResistance = effectiveResistance,
			Mitigation = mitigation,
			MitigatedDamage = mitigatedDamage,
			FinalDamage = finalDamage,
			IsCritical = isCrit
		};
	}

	/// <summary>
	///     Resolves ability damage for one attacker and target through the same pipeline an attack
	///     uses. Ability damage carries no variance and does not roll a critical, but it does carry
	///     the caster's ability damage amplification and the penetration matching its damage type.
	/// </summary>
	public static DamageResult ResolveAbilityDamage(
		EcsWorld world,
		Entity attacker,
		Entity target,
		float amount,
		string? damageType,
		CombatConfig? config = null)
	{
		config ??= world.Context?.ActiveCombatConfig;

		Resistances resistances = world.Has<Resistances>(target) ? world.Get<Resistances>(target) : default;

		string type = DamageTypes.Normalize(damageType);
		bool magic = DamageTypes.IsMagic(type);

		float amplified = amount * ReadAbilityDamageMultiplier(world, attacker);
		float percentPenetration = magic
			? ReadStat(world, attacker, StatKeys.PercentMagicPenetration)
			: ReadStat(world, attacker, StatKeys.PercentArmorPenetration);
		float flatPenetration = magic
			? ReadStat(world, attacker, StatKeys.FlatMagicPenetration)
			: ReadStat(world, attacker, StatKeys.FlatArmorPenetration);

		return ResolveDamage(
			baseDamage: amplified,
			damageVariance: 0f,
			critChance: 0f,
			critMultiplier: 1f,
			physicalResistance: resistances.Physical,
			magicResistance: resistances.Magic,
			flatPenetration: flatPenetration,
			percentPenetration: percentPenetration,
			damageType: type,
			config: config,
			context: world.Context);
	}

	/// <summary>
	///     The caster's ability damage amplification, as a multiplier. Intelligence feeds the
	///     SpellPower stat and items or upgrades may add to it, so an ability's authored number is
	///     a base, exactly like an attack's.
	/// </summary>
	public static float ReadAbilityDamageMultiplier(EcsWorld world, Entity attacker)
	{
		if (attacker == Entity.Null || !world.IsAlive(attacker)) return 1f;

		return 1f + MathF.Max(0f, ReadStat(world, attacker, StatKeys.SpellPower));
	}

	private static float ReadStat(EcsWorld world, Entity entity, string statKey)
	{
		if (entity == Entity.Null || !world.IsAlive(entity) || !world.Has<Stats>(entity)) return 0f;

		ref var stats = ref world.Get<Stats>(entity);
		return stats.TryGetValue(new StatId(statKey), out float value) ? value : 0f;
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
