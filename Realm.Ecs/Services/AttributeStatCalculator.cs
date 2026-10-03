using Arch.Core;
using Realm.Ecs.Common;
using Realm.Ecs.Components.Combat;
using Realm.Ecs.Components.Core;
using Realm.Ecs.Components.Movement;
using Realm.Ecs.Components.Stats;
using System;
using System.Collections.Generic;

namespace Realm.Ecs.Services;

/// <summary>
/// Converts 6 core RPG attributes and base stats into derived combat stats and updates entity components.
/// </summary>
internal static class AttributeStatCalculator
{
	public static DerivedCombatStats CalculateDerivedStats(in UnitAttributes attr, in UnitBaseStats baseStats, float additionalAttackSpeedModifiers = 0f)
	{
		float vit = MathF.Max(0f, attr.Vitality);
		float mig = MathF.Max(0f, attr.Might);
		float agi = MathF.Max(0f, attr.Agility);
		float fin = MathF.Max(0f, attr.Finesse);
		float foc = MathF.Max(0f, attr.Focus);
		float wil = MathF.Max(0f, attr.Willpower);

		float maxHp = baseStats.BaseMaxHp + (vit * 22f);
		float hpRegen = baseStats.BaseHpRegen + (vit * 0.10f);
		float tenacity = 1f - MathF.Pow(1f - 0.0015f, vit);

		float knockbackResistance = mig * 1.2f;

		float movementSpeed = MathF.Max(0f, baseStats.BaseSpeed + (agi * 0.40f));
		float castPoint = MathF.Max(0f, baseStats.BaseCastPoint - (agi * 0.003f));

		float critChance = MathF.Max(0f, baseStats.BaseCritChance + (fin * 0.0035f));
		float flatArmorPen = MathF.Max(0f, baseStats.BaseFlatArmorPenetration + (fin * 0.65f));
		float critMultiplier = MathF.Max(1f, baseStats.BaseCritMultiplier + (fin * 0.0045f));

		float maxMana = MathF.Max(0f, baseStats.BaseMaxMana + (foc * 14f));
		float cdr = 1f - (1f / (1f + (foc * 0.005f)));
		float spellPower = foc * 0.80f;

		float manaRegen = baseStats.BaseManaRegen + (wil * 0.08f);
		float spellWard = 1f - (100f / (100f + (wil * 0.8f)));
		float statusBuffer = wil * 1.5f;

		float totalArmor = baseStats.BaseArmor + (mig * 0.18f) + (vit * 0.05f);
		float totalDamage = MathF.Max(0f, (baseStats.BaseDamage + (mig * 1.4f)) * (1f + (fin * 0.002f)));

		float attackSpeedDivisor = MathF.Max(0.01f, 1f + (agi * 0.014f) + (fin * 0.005f) + additionalAttackSpeedModifiers);
		float attackDelay = MathF.Max(0.01f, baseStats.BaseAttackInterval / attackSpeedDivisor);

		return new DerivedCombatStats(
			MaxHp: maxHp,
			HpRegen: hpRegen,
			Tenacity: tenacity,
			KnockbackResistance: knockbackResistance,
			MovementSpeed: movementSpeed,
			CastPoint: castPoint,
			CritChance: critChance,
			FlatArmorPenetration: flatArmorPen,
			CritMultiplier: critMultiplier,
			MaxMana: maxMana,
			CooldownReduction: cdr,
			SpellPower: spellPower,
			ManaRegen: manaRegen,
			SpellWard: spellWard,
			StatusEffectBuffer: statusBuffer,
			TotalArmor: totalArmor,
			TotalAttackDamage: totalDamage,
			AttackDelay: attackDelay
		);
	}

	public static DerivedCombatStats RecalculateEntityStats(World world, Entity entity, float additionalAttackSpeedModifiers = 0f)
	{
		if (!world.IsAlive(entity)) return default;

		var attributes = world.Has<UnitAttributes>(entity)
			? world.Get<UnitAttributes>(entity)
			: new UnitAttributes();

		UnitBaseStats baseStats;
		if (world.Has<UnitBaseStats>(entity))
		{
			baseStats = world.Get<UnitBaseStats>(entity);
		}
		else
		{
			float bHp = world.Has<Health>(entity) ? world.Get<Health>(entity).Max : 100f;
			float bHpReg = world.Has<Health>(entity) ? world.Get<Health>(entity).HpRegen : 0f;
			float bMana = world.Has<Mana>(entity) ? world.Get<Mana>(entity).Max : 0f;
			float bManaReg = world.Has<Mana>(entity) ? world.Get<Mana>(entity).ManaRegen : 0f;
			float bArmor = world.Has<Armor>(entity) ? world.Get<Armor>(entity).FlatArmor : 0f;
			float bDmg = world.Has<Attack>(entity) ? world.Get<Attack>(entity).Damage : 10f;
			float bAtkCooldown = world.Has<Attack>(entity) ? world.Get<Attack>(entity).Cooldown : 1.5f;
			float bSpeed = world.Has<MovementStats>(entity) ? world.Get<MovementStats>(entity).Speed : 5f;
			float bCritChance = world.Has<Attack>(entity) ? world.Get<Attack>(entity).CritChance : 0f;
			float bCritMult = world.Has<Attack>(entity) ? world.Get<Attack>(entity).CritMultiplier : 1.5f;
			float bFlatPen = world.Has<Attack>(entity) ? world.Get<Attack>(entity).FlatArmorPenetration : 0f;

			baseStats = new UnitBaseStats(
				BaseMaxHp: bHp,
				BaseHpRegen: bHpReg,
				BaseMaxMana: bMana,
				BaseManaRegen: bManaReg,
				BaseArmor: bArmor,
				BaseDamage: bDmg,
				BaseAttackInterval: bAtkCooldown,
				BaseSpeed: bSpeed,
				BaseCastPoint: 0.3f,
				BaseCritChance: bCritChance,
				BaseCritMultiplier: bCritMult,
				BaseFlatArmorPenetration: bFlatPen
			);
			world.Add(entity, baseStats);
		}

		var derived = CalculateDerivedStats(in attributes, in baseStats, additionalAttackSpeedModifiers);
		world.SetOrAdd(entity, derived);

		if (world.Has<Health>(entity))
		{
			ref var health = ref world.Get<Health>(entity);
			float oldMax = health.Max;
			float oldCurrent = health.Current;
			float newMax = derived.MaxHp;
			float newCurrent = oldMax > 0f ? MathF.Min(newMax, oldCurrent * (newMax / oldMax)) : newMax;
			world.Set(entity, new Health(newCurrent, newMax, derived.HpRegen, health.HpRegenCombatDelay, health.TimeSinceLastDamage));
		}

		if (derived.MaxMana > 0f || world.Has<Mana>(entity))
		{
			if (world.Has<Mana>(entity))
			{
				ref var mana = ref world.Get<Mana>(entity);
				float newCurrent = MathF.Min(derived.MaxMana, mana.Current);
				world.Set(entity, new Mana(newCurrent, derived.MaxMana, derived.ManaRegen));
			}
			else if (derived.MaxMana > 0f)
			{
				world.Add(entity, new Mana(derived.MaxMana, derived.MaxMana, derived.ManaRegen));
			}
		}

		if (world.Has<Armor>(entity))
		{
			ref var armor = ref world.Get<Armor>(entity);
			world.Set(entity, new Armor(derived.TotalArmor, armor.RatedArmor, armor.ArmorType));
		}

		if (world.Has<Attack>(entity))
		{
			ref var attack = ref world.Get<Attack>(entity);
			world.Set(entity, new Attack(
				Damage: derived.TotalAttackDamage,
				Range: attack.Range,
				Cooldown: derived.AttackDelay,
				CurrentCooldown: attack.CurrentCooldown,
				DamageVariance: attack.DamageVariance,
				DamageType: attack.DamageType,
				FlatArmorPenetration: derived.FlatArmorPenetration,
				PercentArmorPenetration: attack.PercentArmorPenetration,
				CritChance: derived.CritChance,
				CritMultiplier: derived.CritMultiplier,
				SplashType: attack.SplashType,
				SplashInnerRadius: attack.SplashInnerRadius,
				SplashMediumRadius: attack.SplashMediumRadius,
				SplashOuterRadius: attack.SplashOuterRadius,
				SplashInnerRatio: attack.SplashInnerRatio,
				SplashMediumRatio: attack.SplashMediumRatio,
				SplashOuterRatio: attack.SplashOuterRatio,
				FriendlyFire: attack.FriendlyFire
			));
		}

		if (world.Has<MovementStats>(entity))
		{
			ref var move = ref world.Get<MovementStats>(entity);
			world.Set(entity, new MovementStats(derived.MovementSpeed, move.Acceleration, move.TurnRate, move.PushPriority, move.MovementType));
		}

		if (world.Has<Stats>(entity))
		{
			var statsDict = world.Get<Stats>(entity).Value;
			statsDict[new StatId("Armor")] = derived.TotalArmor;
			statsDict[new StatId("Attack")] = derived.TotalAttackDamage;
			statsDict[new StatId("MovementSpeed")] = derived.MovementSpeed;
			statsDict[new StatId("MaxHp")] = derived.MaxHp;
			statsDict[new StatId("HpRegen")] = derived.HpRegen;
			statsDict[new StatId("MaxMana")] = derived.MaxMana;
			statsDict[new StatId("ManaRegen")] = derived.ManaRegen;
			statsDict[new StatId("SpellPower")] = derived.SpellPower;
			statsDict[new StatId("Tenacity")] = derived.Tenacity;
			statsDict[new StatId("SpellWard")] = derived.SpellWard;
			statsDict[new StatId("CooldownReduction")] = derived.CooldownReduction;
		}

		return derived;
	}
}
