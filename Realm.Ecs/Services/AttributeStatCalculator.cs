using Arch.Core;
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
	public const float STR_ATTACK_DAMAGE_SCALE = 1.5f;
	public const float STR_ARMOR_SCALE = 0.15f;
	public const float ARMOR_K = 50.0f;
	public const float STR_TENACITY_K = 100.0f;
	public const float MAX_TENACITY = 0.75f;

	public const float AGI_HASTE_PER_POINT = 1.0f;
	public const float AGI_BASE_MS_RATIO = 0.4f;
	public const float MS_SOFT_CAP_1 = 450.0f;
	public const float MS_SOFT_CAP_2 = 550.0f;
	public const float AGI_CDR_HASTE_SCALE = 1.0f;
	public const float MAX_CDR = 0.60f;

	public const float VIT_HP_PER_POINT = 18.0f;
	public const float VIT_MP_PER_POINT = 8.0f;
	public const float VIT_LIFESTEAL_K = 150.0f;
	public const float MAX_LIFESTEAL = 0.40f;

	public const float INT_SPELL_AMP_PER_POINT = 0.0075f;
	public const float INT_SPELL_AMP_EXPONENT = 1.05f;
	public const float INT_MANA_REGEN_PER_POINT = 0.05f;
	public const float INT_MAGIC_RESIST_K = 80.0f;
	public const float MAX_MAGIC_RESIST = 0.75f;

	public const float WIS_PENETRATION_K = 120.0f;
	public const float MAX_MAGIC_PEN = 0.50f;
	public const float WIS_HEAL_AMP_PER_POINT = 0.008f;
	public const float WIS_HP_REGEN_BASE_SCALE = 0.04f;
	public const float WIS_CRIT_MULT_PER_POINT = 0.006f;

	public const float FORTUNE_CRIT_CHANCE_K = 100.0f;
	public const float MAX_CRIT_CHANCE = 0.80f;
	public const float FORTUNE_EVASION_K = 150.0f;
	public const float MAX_EVASION = 0.50f;
	public const float FORTUNE_PROC_MOD_K = 200.0f;
	public const float MAX_PROC_BONUS = 0.50f;
	public const float FORTUNE_BOUNTY_EXP_PER_POINT = 0.005f;

	public static DerivedCombatStats CalculateDerivedStats(in UnitAttributes attr, in UnitBaseStats baseStats, float additionalAttackSpeedModifiers = 0f)
	{
		float str = MathF.Max(0f, attr.Strength);
		float agi = MathF.Max(0f, attr.Agility);
		float vit = MathF.Max(0f, attr.Vitality);
		float intel = MathF.Max(0f, attr.Intelligence);
		float wis = MathF.Max(0f, attr.Wisdom);
		float fort = MathF.Max(0f, attr.Fortune);

		float bonusAttackDamage = str * STR_ATTACK_DAMAGE_SCALE;
		float totalAttackDamage = MathF.Max(0f, baseStats.BaseDamage + bonusAttackDamage);
		float rawArmor = str * STR_ARMOR_SCALE;
		float totalArmor = baseStats.BaseArmor + rawArmor;
		float rawTenacity = str / (str + STR_TENACITY_K);
		float tenacity = MathF.Min(rawTenacity, MAX_TENACITY);

		float attackHaste = agi * AGI_HASTE_PER_POINT;
		float baseAps = baseStats.BaseAttackInterval > 0f ? (1.0f / baseStats.BaseAttackInterval) : 1.0f;
		float attacksPerSecond = baseAps * (1.0f + (attackHaste / 100.0f) + additionalAttackSpeedModifiers);
		float attackDelay = MathF.Max(0.01f, 1.0f / MathF.Max(0.01f, attacksPerSecond));

		float rawSpeed = baseStats.BaseSpeed + (agi * AGI_BASE_MS_RATIO);
		if (rawSpeed > MS_SOFT_CAP_2)
		{
			rawSpeed = MS_SOFT_CAP_2 + (rawSpeed - MS_SOFT_CAP_2) * 0.5f;
		}
		else if (rawSpeed > MS_SOFT_CAP_1)
		{
			rawSpeed = MS_SOFT_CAP_1 + (rawSpeed - MS_SOFT_CAP_1) * 0.8f;
		}

		float movementSpeed = MathF.Max(0f, rawSpeed);

		float abilityHaste = agi * AGI_CDR_HASTE_SCALE;
		float rawCDR = abilityHaste / (abilityHaste + 100.0f);
		float cooldownReduction = MathF.Min(rawCDR, MAX_CDR);

		float maxHp = MathF.Max(1f, baseStats.BaseMaxHp + (vit * VIT_HP_PER_POINT));
		float maxMana = MathF.Max(0f, baseStats.BaseMaxMana + (vit * VIT_MP_PER_POINT));
		float rawLifesteal = (vit / (vit + VIT_LIFESTEAL_K)) * MAX_LIFESTEAL;
		float lifeStealRate = MathF.Min(rawLifesteal, MAX_LIFESTEAL);

		float spellDamageAmp = MathF.Pow(intel * INT_SPELL_AMP_PER_POINT, INT_SPELL_AMP_EXPONENT);
		float manaRegen = baseStats.BaseManaRegen + (intel * INT_MANA_REGEN_PER_POINT);
		float rawMR = intel / (intel + INT_MAGIC_RESIST_K);
		float spellWard = MathF.Min(rawMR, MAX_MAGIC_RESIST);

		float magicPenetration = (wis / (wis + WIS_PENETRATION_K)) * MAX_MAGIC_PEN;
		float healingOutputMultiplier = 1.0f + (wis * WIS_HEAL_AMP_PER_POINT);
		float rawHpRegen = baseStats.BaseHpRegen + (wis * WIS_HP_REGEN_BASE_SCALE);
		float hpRegen = rawHpRegen * healingOutputMultiplier;
		float critMultiplier = MathF.Max(1f, baseStats.BaseCritMultiplier + (wis * WIS_CRIT_MULT_PER_POINT));

		float rawCritChance = (fort / (fort + FORTUNE_CRIT_CHANCE_K)) * MAX_CRIT_CHANCE;
		float critChance = MathF.Min(baseStats.BaseCritChance + rawCritChance, MAX_CRIT_CHANCE);
		float rawEvasion = (fort / (fort + FORTUNE_EVASION_K)) * MAX_EVASION;
		float evasionChance = MathF.Min(rawEvasion, MAX_EVASION);
		float procBonus = (fort / (fort + FORTUNE_PROC_MOD_K)) * MAX_PROC_BONUS;
		float procRateMultiplier = 1.0f + procBonus;
		float bountyMultiplier = 1.0f + (fort * FORTUNE_BOUNTY_EXP_PER_POINT);

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

	public static DerivedCombatStats RecalculateEntityStats(World world, Entity entity, float additionalAttackSpeedModifiers = 0f)
	{
		if (!world.IsAlive(entity))
		{
			return default;
		}

		// Step 1: Base Attribute Resolution
		var baseAttrs = world.Has<UnitAttributes>(entity) ? world.Get<UnitAttributes>(entity) : new UnitAttributes();

		// Step 2: Attribute Modifier Phase
		var modifiedAttrs = ApplyAttributeModifiers(world, entity, baseAttrs);

		// Step 3: Derived Stats Computation
		var baseStats = GetOrCreateUnitBaseStats(world, entity);
		var derived = CalculateDerivedStats(in modifiedAttrs, in baseStats, additionalAttackSpeedModifiers);

		// Step 4: Direct Stat Modifier Phase
		derived = ApplyDirectStatModifiers(world, entity, derived, baseStats);

		// Step 5: Entity Component Synchronization
		world.SetOrAdd(entity, derived);

		UpdateHealth(world, entity, in derived);
		UpdateMana(world, entity, in derived);
		UpdateArmor(world, entity, in derived);
		UpdateAttack(world, entity, in derived);
		UpdateMovementStats(world, entity, in derived);
		UpdateStatsDictionary(world, entity, in derived);

		return derived;
	}

	private static UnitAttributes ApplyAttributeModifiers(World world, Entity entity, UnitAttributes baseAttrs)
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

	private static DerivedCombatStats ApplyDirectStatModifiers(World world, Entity entity, DerivedCombatStats derived, UnitBaseStats baseStats)
	{
		float hpFlat = 0f, hpPctAdd = 0f, hpPctMult = 1f, hpFormula = 0f;
		float hpRegenFlat = 0f, hpRegenPctAdd = 0f, hpRegenPctMult = 1f, hpRegenFormula = 0f;
		float manaFlat = 0f, manaPctAdd = 0f, manaPctMult = 1f, manaFormula = 0f;
		float manaRegenFlat = 0f, manaRegenPctAdd = 0f, manaRegenPctMult = 1f, manaRegenFormula = 0f;
		float armorFlat = 0f, armorPctAdd = 0f, armorPctMult = 1f, armorFormula = 0f;
		float dmgFlat = 0f, dmgPctAdd = 0f, dmgPctMult = 1f, dmgFormula = 0f;
		float speedFlat = 0f, speedPctAdd = 0f, speedPctMult = 1f, speedFormula = 0f;

		AccumulateModifiersForStat(world, entity, CombatStatType.MaxHp, ref hpFlat, ref hpPctAdd, ref hpPctMult, ref hpFormula, derived.MaxHp);
		AccumulateModifiersForStat(world, entity, CombatStatType.HpRegen, ref hpRegenFlat, ref hpRegenPctAdd, ref hpRegenPctMult, ref hpRegenFormula, derived.HpRegen);
		AccumulateModifiersForStat(world, entity, CombatStatType.MaxMana, ref manaFlat, ref manaPctAdd, ref manaPctMult, ref manaFormula, derived.MaxMana);
		AccumulateModifiersForStat(world, entity, CombatStatType.ManaRegen, ref manaRegenFlat, ref manaRegenPctAdd, ref manaRegenPctMult, ref manaRegenFormula, derived.ManaRegen);
		AccumulateModifiersForStat(world, entity, CombatStatType.Armor, ref armorFlat, ref armorPctAdd, ref armorPctMult, ref armorFormula, derived.TotalArmor);
		AccumulateModifiersForStat(world, entity, CombatStatType.Damage, ref dmgFlat, ref dmgPctAdd, ref dmgPctMult, ref dmgFormula, derived.TotalAttackDamage);
		AccumulateModifiersForStat(world, entity, CombatStatType.Speed, ref speedFlat, ref speedPctAdd, ref speedPctMult, ref speedFormula, derived.MovementSpeed);

		return derived with
		{
			MaxHp = CombineStat(derived.MaxHp, hpFlat, hpPctAdd, hpPctMult, hpFormula),
			HpRegen = CombineStat(derived.HpRegen, hpRegenFlat, hpRegenPctAdd, hpRegenPctMult, hpRegenFormula),
			MaxMana = CombineStat(derived.MaxMana, manaFlat, manaPctAdd, manaPctMult, manaFormula),
			ManaRegen = CombineStat(derived.ManaRegen, manaRegenFlat, manaRegenPctAdd, manaRegenPctMult, manaRegenFormula),
			TotalArmor = CombineStat(derived.TotalArmor, armorFlat, armorPctAdd, armorPctMult, armorFormula),
			TotalAttackDamage = CombineStat(derived.TotalAttackDamage, dmgFlat, dmgPctAdd, dmgPctMult, dmgFormula),
			MovementSpeed = CombineStat(derived.MovementSpeed, speedFlat, speedPctAdd, speedPctMult, speedFormula)
		};
	}

	private static void AccumulateModifiersForStat(World world, Entity entity, AttributeType attribute, ref float flat, ref float pctAdd, ref float pctMult, ref float formulaBonus, float baseValue)
	{
		AccumulateModifiersForStat(world, entity, new StatId(attribute), ref flat, ref pctAdd, ref pctMult, ref formulaBonus, baseValue);
	}

	private static void AccumulateModifiersForStat(World world, Entity entity, CombatStatType combatStat, ref float flat, ref float pctAdd, ref float pctMult, ref float formulaBonus, float baseValue)
	{
		AccumulateModifiersForStat(world, entity, new StatId(combatStat), ref flat, ref pctAdd, ref pctMult, ref formulaBonus, baseValue);
	}

	private static void AccumulateModifiersForStat(World world, Entity entity, StatId statId, ref float flat, ref float pctAdd, ref float pctMult, ref float formulaBonus, float baseValue)
	{
		AccumulateModifiersForStat(world, entity, statId.Value, ref flat, ref pctAdd, ref pctMult, ref formulaBonus, baseValue);
	}

	private static void AccumulateModifiersForStat(World world, Entity entity, string statName, ref float flat, ref float pctAdd, ref float pctMult, ref float formulaBonus, float baseValue)
	{
		if (!world.Has<ModifierState>(entity)) return;

		var modState = world.Get<ModifierState>(entity);
		var list = modState.Value;
		for (int i = 0; i < list.Count; i++)
		{
			var mod = list[i];
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

	private static UnitBaseStats GetOrCreateUnitBaseStats(World world, Entity entity)
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
			bHp = health.Max;
			bHpReg = health.HpRegen;
		}

		float bMana = 0f;
		float bManaReg = 0f;
		if (world.Has<Mana>(entity))
		{
			var mana = world.Get<Mana>(entity);
			bMana = mana.Max;
			bManaReg = mana.ManaRegen;
		}

		float bArmor = world.Has<Armor>(entity) ? world.Get<Armor>(entity).FlatArmor : 0f;

		float bDmg = 10f;
		float bAtkCooldown = 1.5f;
		float bCritChance = 0f;
		float bCritMult = 1.5f;
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
			BaseCastPoint: 0.3f,
			BaseCritChance: bCritChance,
			BaseCritMultiplier: bCritMult,
			BaseFlatArmorPenetration: bFlatPen
		);
		world.Add(entity, baseStats);

		return baseStats;
	}

	private static void UpdateHealth(World world, Entity entity, in DerivedCombatStats derived)
	{
		if (!world.Has<Health>(entity))
		{
			return;
		}

		ref var health = ref world.Get<Health>(entity);
		float oldMax = health.Max;
		float oldCurrent = health.Current;
		float newMax = derived.MaxHp;
		float newCurrent = oldMax > 0f ? MathF.Min(newMax, oldCurrent * (newMax / oldMax)) : newMax;

		world.Set(entity, new Health(newCurrent, newMax, derived.HpRegen, health.HpRegenCombatDelay, health.TimeSinceLastDamage));
	}

	private static void UpdateMana(World world, Entity entity, in DerivedCombatStats derived)
	{
		if (derived.MaxMana <= 0f && !world.Has<Mana>(entity))
		{
			return;
		}

		if (world.Has<Mana>(entity))
		{
			ref var mana = ref world.Get<Mana>(entity);
			float newCurrent = MathF.Min(derived.MaxMana, mana.Current);
			world.Set(entity, new Mana(newCurrent, derived.MaxMana, derived.ManaRegen));
			return;
		}

		world.Add(entity, new Mana(derived.MaxMana, derived.MaxMana, derived.ManaRegen));
	}

	private static void UpdateArmor(World world, Entity entity, in DerivedCombatStats derived)
	{
		if (!world.Has<Armor>(entity))
		{
			return;
		}

		ref var armor = ref world.Get<Armor>(entity);
		world.Set(entity, new Armor(derived.TotalArmor, armor.RatedArmor, armor.ArmorType));
	}

	private static void UpdateAttack(World world, Entity entity, in DerivedCombatStats derived)
	{
		if (!world.Has<Attack>(entity))
		{
			return;
		}

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

	private static void UpdateMovementStats(World world, Entity entity, in DerivedCombatStats derived)
	{
		if (!world.Has<MovementStats>(entity))
		{
			return;
		}

		ref var move = ref world.Get<MovementStats>(entity);
		world.Set(entity, new MovementStats(derived.MovementSpeed, move.Acceleration, move.TurnRate, move.PushPriority, move.MovementType));
	}

	private static void UpdateStatsDictionary(World world, Entity entity, in DerivedCombatStats derived)
	{
		if (!world.Has<Stats>(entity))
		{
			return;
		}

		var statsDict = world.Get<Stats>(entity).Value;
		statsDict[new StatId(CombatStatType.Armor)] = derived.TotalArmor;
		statsDict[new StatId(CombatStatType.Attack)] = derived.TotalAttackDamage;
		statsDict[new StatId(CombatStatType.MovementSpeed)] = derived.MovementSpeed;
		statsDict[new StatId(CombatStatType.MaxHp)] = derived.MaxHp;
		statsDict[new StatId(CombatStatType.HpRegen)] = derived.HpRegen;
		statsDict[new StatId(CombatStatType.MaxMana)] = derived.MaxMana;
		statsDict[new StatId(CombatStatType.ManaRegen)] = derived.ManaRegen;
		statsDict[new StatId(CombatStatType.SpellPower)] = derived.SpellPower;
		statsDict[new StatId(CombatStatType.Tenacity)] = derived.Tenacity;
		statsDict[new StatId(CombatStatType.SpellWard)] = derived.SpellWard;
		statsDict[new StatId(CombatStatType.CooldownReduction)] = derived.CooldownReduction;
		statsDict[new StatId(CombatStatType.LifeSteal)] = derived.LifeSteal;
		statsDict[new StatId(CombatStatType.MagicPenetration)] = derived.MagicPenetration;
		statsDict[new StatId(CombatStatType.Evasion)] = derived.Evasion;
		statsDict[new StatId(CombatStatType.ProcRateMultiplier)] = derived.ProcRateMultiplier;
		statsDict[new StatId(CombatStatType.BountyMultiplier)] = derived.BountyMultiplier;
	}
}