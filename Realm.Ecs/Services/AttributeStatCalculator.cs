using System;
using Realm.Ecs.Common;
using Realm.Ecs.Components.Combat;
using Realm.Ecs.Components.Core;
using Realm.Ecs.Components.Movement;
using Realm.Ecs.Components.Stats;

namespace Realm.Ecs.Services;

/// <summary>
/// Converts 6 core RPG attributes and base stats into derived combat stats and updates entity components.
/// </summary>
public static class AttributeStatCalculator
{
	public static DerivedCombatStats CalculateDerivedStats(in UnitAttributes attr, in UnitBaseStats baseStats, float additionalAttackSpeedModifiers = 0f, CombatTuning? tuning = null)
	{
		CombatTuning t = tuning ?? ECSContext.ActiveCombatTuning;
		float str = MathF.Max(0f, attr.Strength);
		float agi = MathF.Max(0f, attr.Agility);
		float vit = MathF.Max(0f, attr.Vitality);
		float intel = MathF.Max(0f, attr.Intelligence);
		float wis = MathF.Max(0f, attr.Wisdom);
		float fort = MathF.Max(0f, attr.Fortune);

		float bonusAttackDamage = str * t.StrengthAttackPerPoint;
		float totalAttackDamage = MathF.Max(0f, baseStats.BaseDamage + bonusAttackDamage);
		float rawArmor = str * t.StrengthArmorPerPoint;
		float totalArmor = baseStats.BaseArmor + rawArmor;
		float rawTenacity = str / (str + t.StrengthTenacityK);
		float tenacity = MathF.Min(rawTenacity, t.MaxTenacity);

		float attackHaste = agi * t.AgilityHastePerPoint;
		float baseAps = baseStats.BaseAttackInterval > 0f ? (1.0f / baseStats.BaseAttackInterval) : 1.0f;
		float attacksPerSecond = baseAps * (1.0f + (attackHaste / t.HastePercentDivisor) + additionalAttackSpeedModifiers);
		float attackDelay = MathF.Max(t.MinAttackInterval, 1.0f / MathF.Max(t.MinAttackInterval, attacksPerSecond));

		float rawSpeed = baseStats.BaseSpeed + (agi * t.AgilityMovementPerPoint);
		if (rawSpeed > t.MovementSoftCap2)
		{
			rawSpeed = t.MovementSoftCap2 + (rawSpeed - t.MovementSoftCap2) * t.MovementSoftCap2Taper;
		}
		else if (rawSpeed > t.MovementSoftCap1)
		{
			rawSpeed = t.MovementSoftCap1 + (rawSpeed - t.MovementSoftCap1) * t.MovementSoftCap1Taper;
		}

		float movementSpeed = MathF.Max(0f, rawSpeed);

		float abilityHaste = agi * t.AgilityCooldownHastePerPoint;
		float rawCDR = abilityHaste / (abilityHaste + t.CooldownHasteDivisor);
		float cooldownReduction = MathF.Min(rawCDR, t.MaxCooldownReduction);

		float maxHp = MathF.Max(1f, baseStats.BaseMaxHp + (vit * t.VitalityHpPerPoint));
		float maxMana = MathF.Max(0f, baseStats.BaseMaxMana + (vit * t.VitalityManaPerPoint));
		float rawLifesteal = (vit / (vit + t.VitalityLifeStealK)) * t.MaxLifeSteal;
		float lifeStealRate = MathF.Min(rawLifesteal, t.MaxLifeSteal);

		float spellDamageAmp = MathF.Pow(intel * t.IntelligenceSpellAmpPerPoint, t.IntelligenceSpellAmpExponent);
		float manaRegen = baseStats.BaseManaRegen + (intel * t.IntelligenceManaRegenPerPoint);
		float rawMR = intel / (intel + t.IntelligenceMagicResistK);
		float spellWard = MathF.Min(rawMR, t.MaxMagicResist);

		float magicPenetration = (wis / (wis + t.WisdomMagicPenetrationK)) * t.MaxMagicPenetration;
		float healingOutputMultiplier = 1.0f + (wis * t.WisdomHealingAmpPerPoint);
		float rawHpRegen = baseStats.BaseHpRegen + (wis * t.WisdomHpRegenPerPoint);
		float hpRegen = rawHpRegen * healingOutputMultiplier;
		float critMultiplier = MathF.Max(1f, baseStats.BaseCritMultiplier + (wis * t.WisdomCritMultiplierPerPoint));

		float rawCritChance = (fort / (fort + t.FortuneCritChanceK)) * t.MaxCritChance;
		float critChance = MathF.Min(baseStats.BaseCritChance + rawCritChance, t.MaxCritChance);
		float rawEvasion = (fort / (fort + t.FortuneEvasionK)) * t.MaxEvasion;
		float evasionChance = MathF.Min(rawEvasion, t.MaxEvasion);
		float procBonus = (fort / (fort + t.FortuneProcRateK)) * t.MaxProcRateBonus;
		float procRateMultiplier = 1.0f + procBonus;
		float bountyMultiplier = 1.0f + (fort * t.FortuneBountyPerPoint);

		return new DerivedCombatStats(
			MaxHp: maxHp,
			HpRegen: hpRegen,
			Tenacity: tenacity,
			KnockbackResistance: 0f,
			MovementSpeed: movementSpeed,
			CastPoint: baseStats.BaseCastPoint,
			CritChance: critChance,
			FlatArmorPenetration: baseStats.BaseFlatArmorPenetration,
			CritMultiplier: critMultiplier,
			MaxMana: maxMana,
			CooldownReduction: cooldownReduction,
			SpellPower: spellDamageAmp,
			ManaRegen: manaRegen,
			SpellWard: spellWard,
			StatusEffectBuffer: 0f,
			TotalArmor: totalArmor,
			TotalAttackDamage: totalAttackDamage,
			AttackDelay: attackDelay,
			LifeSteal: lifeStealRate,
			MagicPenetration: magicPenetration,
			Evasion: evasionChance,
			ProcRateMultiplier: procRateMultiplier,
			BountyMultiplier: bountyMultiplier,
			HealingMultiplier: healingOutputMultiplier
		);
	}

	public static DerivedCombatStats RecalculateEntityStats(EcsWorld world, Entity entity, float additionalAttackSpeedModifiers = 0f, CombatTuning? tuning = null)
	{
		if (!world.IsAlive(entity))
		{
			return default;
		}

		var t = tuning ?? ECSContext.ActiveCombatTuning;
		var baseAttrs = world.Has<UnitAttributes>(entity) ? world.Get<UnitAttributes>(entity) : new UnitAttributes();
		var modifiedAttrs = ApplyAttributeModifiers(world, entity, baseAttrs);
		var baseStats = GetOrCreateUnitBaseStats(world, entity, t);
		var derived = CalculateDerivedStats(in modifiedAttrs, in baseStats, additionalAttackSpeedModifiers, t);

		derived = ApplyDirectStatModifiers(world, entity, derived, baseStats, t);

		world.SetOrAdd(entity, derived);

		UpdateHealth(world, entity, in derived);
		UpdateMana(world, entity, in derived);
		UpdateArmor(world, entity, in derived);
		UpdateAttack(world, entity, in derived);
		UpdateMovementStats(world, entity, in derived);
		UpdateStatsDictionary(world, entity, in derived, in modifiedAttrs);

		return derived;
	}

	private static UnitAttributes ApplyAttributeModifiers(EcsWorld world, Entity entity, UnitAttributes baseAttrs)
	{
		float strFlat = 0f, strPctAdd = 0f, strPctMult = 1f, strFormula = 0f;
		float agiFlat = 0f, agiPctAdd = 0f, agiPctMult = 1f, agiFormula = 0f;
		float vitFlat = 0f, vitPctAdd = 0f, vitPctMult = 1f, vitFormula = 0f;
		float intFlat = 0f, intPctAdd = 0f, intPctMult = 1f, intFormula = 0f;
		float wisFlat = 0f, wisPctAdd = 0f, wisPctMult = 1f, wisFormula = 0f;
		float fortFlat = 0f, fortPctAdd = 0f, fortPctMult = 1f, fortFormula = 0f;

		AccumulateModifiersForStat(world, entity, AttributeType.Strength, ref strFlat, ref strPctAdd, ref strPctMult, ref strFormula, baseAttrs.Strength);
		AccumulateModifiersForStat(world, entity, AttributeType.Agility, ref agiFlat, ref agiPctAdd, ref agiPctMult, ref agiFormula, baseAttrs.Agility);
		AccumulateModifiersForStat(world, entity, AttributeType.Vitality, ref vitFlat, ref vitPctAdd, ref vitPctMult, ref vitFormula, baseAttrs.Vitality);
		AccumulateModifiersForStat(world, entity, AttributeType.Intelligence, ref intFlat, ref intPctAdd, ref intPctMult, ref intFormula, baseAttrs.Intelligence);
		AccumulateModifiersForStat(world, entity, AttributeType.Wisdom, ref wisFlat, ref wisPctAdd, ref wisPctMult, ref wisFormula, baseAttrs.Wisdom);
		AccumulateModifiersForStat(world, entity, AttributeType.Fortune, ref fortFlat, ref fortPctAdd, ref fortPctMult, ref fortFormula, baseAttrs.Fortune);

		float str = CombineStat(baseAttrs.Strength, strFlat, strPctAdd, strPctMult, strFormula);
		float agi = CombineStat(baseAttrs.Agility, agiFlat, agiPctAdd, agiPctMult, agiFormula);
		float vit = CombineStat(baseAttrs.Vitality, vitFlat, vitPctAdd, vitPctMult, vitFormula);
		float intel = CombineStat(baseAttrs.Intelligence, intFlat, intPctAdd, intPctMult, intFormula);
		float wis = CombineStat(baseAttrs.Wisdom, wisFlat, wisPctAdd, wisPctMult, wisFormula);
		float fort = CombineStat(baseAttrs.Fortune, fortFlat, fortPctAdd, fortPctMult, fortFormula);

		return new UnitAttributes(str, agi, vit, intel, wis, fort);
	}

	private struct StatAccumulator
	{
		public float Flat;
		public float PctAdd;
		public float PctMult;
		public float Formula;

		public static StatAccumulator New() => new StatAccumulator { PctMult = 1f };

		public readonly float Combine(float baseValue) => (baseValue + Flat) * (1f + PctAdd) * PctMult + Formula;
	}

	private static StatAccumulator Accumulate(EcsWorld world, Entity entity, string statKey, float baseValue)
	{
		var acc = StatAccumulator.New();
		AccumulateModifiersForStat(world, entity, statKey, ref acc.Flat, ref acc.PctAdd, ref acc.PctMult, ref acc.Formula, baseValue);
		return acc;
	}

	private static DerivedCombatStats ApplyDirectStatModifiers(EcsWorld world, Entity entity, DerivedCombatStats derived, UnitBaseStats baseStats, CombatTuning t)
	{
		var hp = Accumulate(world, entity, StatKeys.MaxHp, derived.MaxHp);
		var hpRegen = Accumulate(world, entity, StatKeys.HpRegen, derived.HpRegen);
		var mana = Accumulate(world, entity, StatKeys.MaxMana, derived.MaxMana);
		var manaRegen = Accumulate(world, entity, StatKeys.ManaRegen, derived.ManaRegen);
		var armor = Accumulate(world, entity, StatKeys.Armor, derived.TotalArmor);
		var attack = Accumulate(world, entity, StatKeys.Attack, derived.TotalAttackDamage);
		var movementSpeed = Accumulate(world, entity, StatKeys.MovementSpeed, derived.MovementSpeed);
		var attackSpeed = Accumulate(world, entity, StatKeys.AttackSpeed, 1f);
		var attackDelay = Accumulate(world, entity, StatKeys.AttackDelay, derived.AttackDelay);
		var spellPower = Accumulate(world, entity, StatKeys.SpellPower, derived.SpellPower);
		var tenacity = Accumulate(world, entity, StatKeys.Tenacity, derived.Tenacity);
		var spellWard = Accumulate(world, entity, StatKeys.SpellWard, derived.SpellWard);
		var cooldownReduction = Accumulate(world, entity, StatKeys.CooldownReduction, derived.CooldownReduction);
		var lifeSteal = Accumulate(world, entity, StatKeys.LifeSteal, derived.LifeSteal);
		var magicPenetration = Accumulate(world, entity, StatKeys.MagicPenetration, derived.MagicPenetration);
		var evasion = Accumulate(world, entity, StatKeys.Evasion, derived.Evasion);
		var critChance = Accumulate(world, entity, StatKeys.CritChance, derived.CritChance);
		var critMultiplier = Accumulate(world, entity, StatKeys.CritMultiplier, derived.CritMultiplier);
		var procRate = Accumulate(world, entity, StatKeys.ProcRateMultiplier, derived.ProcRateMultiplier);
		var bounty = Accumulate(world, entity, StatKeys.BountyMultiplier, derived.BountyMultiplier);
		var healing = Accumulate(world, entity, StatKeys.HealingMultiplier, derived.HealingMultiplier);
		var castPoint = Accumulate(world, entity, StatKeys.CastPoint, derived.CastPoint);
		var flatPenetration = Accumulate(world, entity, StatKeys.FlatArmorPenetration, derived.FlatArmorPenetration);
		var knockbackResistance = Accumulate(world, entity, StatKeys.KnockbackResistance, derived.KnockbackResistance);
		var statusEffectBuffer = Accumulate(world, entity, StatKeys.StatusEffectBuffer, derived.StatusEffectBuffer);

		float resolvedAttackSpeed = Math.Clamp(attackSpeed.Combine(1f), t.MinAttackSpeed, t.MaxAttackSpeed);
		float resolvedAttackDelay = MathF.Max(t.MinAttackInterval, attackDelay.Combine(derived.AttackDelay / resolvedAttackSpeed));

		return derived with
		{
			MaxHp = MathF.Max(1f, hp.Combine(derived.MaxHp)),
			HpRegen = MathF.Max(0f, hpRegen.Combine(derived.HpRegen)),
			MaxMana = MathF.Max(0f, mana.Combine(derived.MaxMana)),
			ManaRegen = MathF.Max(0f, manaRegen.Combine(derived.ManaRegen)),
			TotalArmor = MathF.Max(0f, armor.Combine(derived.TotalArmor)),
			TotalAttackDamage = MathF.Max(0f, attack.Combine(derived.TotalAttackDamage)),
			MovementSpeed = MathF.Max(0f, movementSpeed.Combine(derived.MovementSpeed)),
			AttackDelay = resolvedAttackDelay,
			SpellPower = MathF.Max(0f, spellPower.Combine(derived.SpellPower)),
			Tenacity = Math.Clamp(tenacity.Combine(derived.Tenacity), 0f, t.MaxTenacity),
			SpellWard = Math.Clamp(spellWard.Combine(derived.SpellWard), 0f, t.MaxMagicResist),
			CooldownReduction = Math.Clamp(cooldownReduction.Combine(derived.CooldownReduction), 0f, t.MaxCooldownReduction),
			LifeSteal = Math.Clamp(lifeSteal.Combine(derived.LifeSteal), 0f, t.MaxLifeSteal),
			MagicPenetration = Math.Clamp(magicPenetration.Combine(derived.MagicPenetration), 0f, t.MaxMagicPenetration),
			Evasion = Math.Clamp(evasion.Combine(derived.Evasion), 0f, t.MaxEvasion),
			CritChance = Math.Clamp(critChance.Combine(derived.CritChance), 0f, t.MaxCritChance),
			CritMultiplier = MathF.Max(1f, critMultiplier.Combine(derived.CritMultiplier)),
			ProcRateMultiplier = MathF.Max(0f, procRate.Combine(derived.ProcRateMultiplier)),
			BountyMultiplier = MathF.Max(0f, bounty.Combine(derived.BountyMultiplier)),
			HealingMultiplier = MathF.Max(0f, healing.Combine(derived.HealingMultiplier)),
			CastPoint = MathF.Max(0f, castPoint.Combine(derived.CastPoint)),
			FlatArmorPenetration = MathF.Max(0f, flatPenetration.Combine(derived.FlatArmorPenetration)),
			KnockbackResistance = Math.Clamp(knockbackResistance.Combine(derived.KnockbackResistance), 0f, 1f),
			StatusEffectBuffer = MathF.Max(0f, statusEffectBuffer.Combine(derived.StatusEffectBuffer))
		};
	}

	private static void AccumulateModifiersForStat(EcsWorld world, Entity entity, AttributeType attribute, ref float flat, ref float pctAdd, ref float pctMult, ref float formulaBonus, float baseValue)
	{
		AccumulateModifiersForStat(world, entity, new StatId(attribute), ref flat, ref pctAdd, ref pctMult, ref formulaBonus, baseValue);
	}

	private static void AccumulateModifiersForStat(EcsWorld world, Entity entity, CombatStatType combatStat, ref float flat, ref float pctAdd, ref float pctMult, ref float formulaBonus, float baseValue)
	{
		AccumulateModifiersForStat(world, entity, new StatId(combatStat), ref flat, ref pctAdd, ref pctMult, ref formulaBonus, baseValue);
	}

	private static void AccumulateModifiersForStat(EcsWorld world, Entity entity, StatId statId, ref float flat, ref float pctAdd, ref float pctMult, ref float formulaBonus, float baseValue)
	{
		AccumulateModifiersForStat(world, entity, statId.Value, ref flat, ref pctAdd, ref pctMult, ref formulaBonus, baseValue);
	}

	private static void AccumulateModifiersForStat(EcsWorld world, Entity entity, string statName, ref float flat, ref float pctAdd, ref float pctMult, ref float formulaBonus, float baseValue)
	{
		if (!world.Has<ModifierState>(entity)) return;

		ref var modState = ref world.Get<ModifierState>(entity);
		for (int i = 0; i < modState.Count; i++)
		{
			var mod = modState.Get(i);
			if (!string.Equals(mod.StatTypeId.Value, statName, StringComparison.OrdinalIgnoreCase)) continue;

			switch (mod.Type)
			{
				case ModifierType.Flat:
					flat += mod.Value;
					break;
				case ModifierType.PercentAdditive:
					pctAdd += mod.Value;
					break;
				case ModifierType.PercentMultiplicative:
					pctMult *= mod.Value;
					break;
				case ModifierType.CustomFormula:
					if (!string.IsNullOrWhiteSpace(mod.WASMFunctionName) && ECSContext.EvaluationEngine != null)
					{
						try
						{
							var context = new FormulaContext(world, entity, default, mod.WASMFunctionName);
							formulaBonus += ECSContext.EvaluationEngine.Evaluate(in context, baseValue, mod.Value, mod.SourceLevel);
						}
						catch
						{
						}
					}
					break;
			}
		}
	}

	private static float CombineStat(float baseVal, float flatSum, float pctAddSum, float pctMultProd, float formulaBonus)
	{
		return (baseVal + flatSum) * (1.0f + pctAddSum) * pctMultProd + formulaBonus;
	}

	private static UnitBaseStats GetOrCreateUnitBaseStats(EcsWorld world, Entity entity, CombatTuning t)
	{
		if (world.Has<UnitBaseStats>(entity))
		{
			return world.Get<UnitBaseStats>(entity);
		}

		float bHp = 100f;
		float bHpReg = 0f;
		if (world.Has<Health>(entity))
		{
			var health = world.Get<Health>(entity);
			bHp = health.MaxHp;
			bHpReg = health.HpRegen;
		}

		float bMana = 0f;
		float bManaReg = 0f;
		if (world.Has<Mana>(entity))
		{
			var mana = world.Get<Mana>(entity);
			bMana = mana.MaxMana;
			bManaReg = mana.ManaRegen;
		}

		float bArmor = world.Has<Armor>(entity) ? world.Get<Armor>(entity).FlatArmor : 0f;

		float bDmg = 10f;
		float bAtkCooldown = 1.5f;
		float bCritChance = 0f;
		float bCritMult = t.BaseCritMultiplier;
		float bFlatPen = 0f;
		if (world.Has<Attack>(entity))
		{
			var attack = world.Get<Attack>(entity);
			bDmg = attack.Damage;
			bAtkCooldown = attack.Cooldown;
			bCritChance = attack.CritChance;
			bCritMult = attack.CritMultiplier;
			bFlatPen = attack.FlatArmorPenetration;
		}

		float bSpeed = world.Has<MovementStats>(entity) ? world.Get<MovementStats>(entity).Speed : 5f;

		var baseStats = new UnitBaseStats(
			BaseMaxHp: bHp,
			BaseHpRegen: bHpReg,
			BaseMaxMana: bMana,
			BaseManaRegen: bManaReg,
			BaseArmor: bArmor,
			BaseDamage: bDmg,
			BaseAttackInterval: bAtkCooldown,
			BaseSpeed: bSpeed,
			BaseCastPoint: t.BaseCastPoint,
			BaseCritChance: bCritChance,
			BaseCritMultiplier: bCritMult,
			BaseFlatArmorPenetration: bFlatPen
		);
		world.Add(entity, baseStats);

		return baseStats;
	}

	private static void UpdateHealth(EcsWorld world, Entity entity, in DerivedCombatStats derived)
	{
		if (!world.Has<Health>(entity))
		{
			return;
		}

		ref var health = ref world.Get<Health>(entity);
		float oldMax = health.MaxHp;
		float oldCurrent = health.CurrentHp;
		float newMax = derived.MaxHp;
		float newCurrent = oldMax > 0f ? MathF.Min(newMax, oldCurrent * (newMax / oldMax)) : newMax;

		world.Set(entity, new Health(newCurrent, newMax, derived.HpRegen, health.HpRegenCombatDelay, health.TimeSinceLastDamage));
	}

	private static void UpdateMana(EcsWorld world, Entity entity, in DerivedCombatStats derived)
	{
		if (derived.MaxMana <= 0f && !world.Has<Mana>(entity))
		{
			return;
		}

		if (world.Has<Mana>(entity))
		{
			ref var mana = ref world.Get<Mana>(entity);
			float newCurrent = MathF.Min(derived.MaxMana, mana.CurrentMana);
			world.Set(entity, new Mana(newCurrent, derived.MaxMana, derived.ManaRegen));
			return;
		}

		world.Add(entity, new Mana(derived.MaxMana, derived.MaxMana, derived.ManaRegen));
	}

	private static void UpdateArmor(EcsWorld world, Entity entity, in DerivedCombatStats derived)
	{
		if (!world.Has<Armor>(entity))
		{
			return;
		}

		ref var armor = ref world.Get<Armor>(entity);
		world.Set(entity, new Armor(derived.TotalArmor, armor.RatedArmor, armor.ArmorType));
	}

	private static void UpdateAttack(EcsWorld world, Entity entity, in DerivedCombatStats derived)
	{
		if (!world.Has<Attack>(entity))
		{
			return;
		}

		ref var attack = ref world.Get<Attack>(entity);
		world.Set(entity, new Attack(
			damage: derived.TotalAttackDamage,
			range: attack.Range,
			cooldown: derived.AttackDelay,
			currentCooldown: attack.CurrentCooldown,
			damageVariance: attack.DamageVariance,
			damageType: attack.DamageType,
			flatArmorPenetration: derived.FlatArmorPenetration,
			percentArmorPenetration: attack.PercentArmorPenetration,
			critChance: derived.CritChance,
			critMultiplier: derived.CritMultiplier,
			splashType: attack.SplashType,
			splashInnerRadius: attack.SplashInnerRadius,
			splashMediumRadius: attack.SplashMediumRadius,
			splashOuterRadius: attack.SplashOuterRadius,
			splashInnerRatio: attack.SplashInnerRatio,
			splashMediumRatio: attack.SplashMediumRatio,
			splashOuterRatio: attack.SplashOuterRatio,
			friendlyFire: attack.FriendlyFire
		));
	}

	private static void UpdateMovementStats(EcsWorld world, Entity entity, in DerivedCombatStats derived)
	{
		if (!world.Has<MovementStats>(entity))
		{
			return;
		}

		ref var move = ref world.Get<MovementStats>(entity);
		world.Set(entity, new MovementStats(derived.MovementSpeed, move.Acceleration, move.TurnRate, move.PushPriority, move.MovementType));
	}

	private static void UpdateStatsDictionary(EcsWorld world, Entity entity, in DerivedCombatStats derived, in UnitAttributes attributes)
	{
		if (!world.Has<Stats>(entity))
		{
			return;
		}

		// The snapshot is the single read path for scripts and UI: attributes and every derived
		// stat land here once per recalculation, never re-combined on read.
		ref var stats = ref world.Get<Stats>(entity);
		stats.Set(new StatId(AttributeType.Strength), attributes.Strength);
		stats.Set(new StatId(AttributeType.Agility), attributes.Agility);
		stats.Set(new StatId(AttributeType.Vitality), attributes.Vitality);
		stats.Set(new StatId(AttributeType.Intelligence), attributes.Intelligence);
		stats.Set(new StatId(AttributeType.Wisdom), attributes.Wisdom);
		stats.Set(new StatId(AttributeType.Fortune), attributes.Fortune);
		stats.Set(new StatId(StatKeys.AttackDelay), derived.AttackDelay);
		stats.Set(new StatId(StatKeys.CastPoint), derived.CastPoint);
		stats.Set(new StatId(StatKeys.CritChance), derived.CritChance);
		stats.Set(new StatId(StatKeys.CritMultiplier), derived.CritMultiplier);
		stats.Set(new StatId(StatKeys.FlatArmorPenetration), derived.FlatArmorPenetration);
		stats.Set(new StatId(StatKeys.HealingMultiplier), derived.HealingMultiplier);
		stats.Set(new StatId(StatKeys.KnockbackResistance), derived.KnockbackResistance);
		stats.Set(new StatId(StatKeys.StatusEffectBuffer), derived.StatusEffectBuffer);
		stats.Set(new StatId(CombatStatType.Armor), derived.TotalArmor);
		stats.Set(new StatId(CombatStatType.Attack), derived.TotalAttackDamage);
		stats.Set(new StatId(CombatStatType.MovementSpeed), derived.MovementSpeed);
		stats.Set(new StatId(CombatStatType.MaxHp), derived.MaxHp);
		stats.Set(new StatId(CombatStatType.HpRegen), derived.HpRegen);
		stats.Set(new StatId(CombatStatType.MaxMana), derived.MaxMana);
		stats.Set(new StatId(CombatStatType.ManaRegen), derived.ManaRegen);
		stats.Set(new StatId(CombatStatType.SpellPower), derived.SpellPower);
		stats.Set(new StatId(CombatStatType.Tenacity), derived.Tenacity);
		stats.Set(new StatId(CombatStatType.SpellWard), derived.SpellWard);
		stats.Set(new StatId(CombatStatType.CooldownReduction), derived.CooldownReduction);
		stats.Set(new StatId(CombatStatType.LifeSteal), derived.LifeSteal);
		stats.Set(new StatId(CombatStatType.MagicPenetration), derived.MagicPenetration);
		stats.Set(new StatId(CombatStatType.Evasion), derived.Evasion);
		stats.Set(new StatId(CombatStatType.ProcRateMultiplier), derived.ProcRateMultiplier);
		stats.Set(new StatId(CombatStatType.BountyMultiplier), derived.BountyMultiplier);
	}
}
