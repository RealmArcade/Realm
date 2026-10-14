using Arch.Core;
using Realm.Ecs.Components.Combat;
using Realm.Ecs.Components.Core;
using Realm.Ecs.Components.Meta;
using Realm.Ecs.Components.Movement;
using Realm.Ecs.Components.Stats;

namespace Realm.Ecs.Services;

public class VariableNode : IFormulaNode
{
	private readonly string _scope;
	private readonly string _name;

	public VariableNode(string fullVariable)
	{
		int dotIndex = fullVariable.IndexOf('.');
		if (dotIndex > 0)
		{
			_scope = fullVariable.Substring(0, dotIndex).ToLowerInvariant();
			_name = fullVariable.Substring(dotIndex + 1).ToLowerInvariant();
		}
		else
		{
			_scope = string.Empty;
			_name = fullVariable.ToLowerInvariant();
		}
	}

	public float Evaluate(in FormulaContext context)
	{
		return _scope switch
		{
			"caster" => ResolveEntityStat(context.World, context.Caster, _name),
			"target" => ResolveEntityStat(context.World, context.Target, _name),
			"spell" => TryGetDictionaryValue(context.SpellData, _name, out float spellVal) ? spellVal : 0f,
			"dynamic" => TryGetDictionaryValue(context.DynamicData, _name, out float dynamicVal) ? dynamicVal : 0f,
			_ => ResolveFallbackScope(in context)
		};
	}

	private float ResolveFallbackScope(in FormulaContext context)
	{
		if (TryGetDictionaryValue(context.SpellData, _name, out float spellVal))
		{
			return spellVal;
		}

		if (TryGetDictionaryValue(context.DynamicData, _name, out float dynamicVal))
		{
			return dynamicVal;
		}

		if (context.Caster != default)
		{
			return ResolveEntityStat(context.World, context.Caster, _name);
		}

		return 0f;
	}

	private static bool TryGetDictionaryValue(Dictionary<string, float>? dict, string key, out float value)
	{
		value = 0f;
		if (dict == null) return false;

		foreach (var kvp in dict)
		{
			if (string.Equals(kvp.Key, key, StringComparison.OrdinalIgnoreCase))
			{
				value = kvp.Value;
				return true;
			}
		}

		return false;
	}

	private static float ResolveEntityStat(World? world, Entity entity, string statName)
	{
		if (world == null || entity == default || !world.IsAlive(entity))
		{
			return 0f;
		}

		if (TryResolveAttributeStat(world, entity, statName, out float attrVal)) return attrVal;
		if (TryResolveCombatStat(world, entity, statName, out float combatVal)) return combatVal;
		if (TryResolveResourceStat(world, entity, statName, out float resourceVal)) return resourceVal;

		if (statName is "hero_level" or "lvl")
			return world.Has<Level>(entity) ? world.Get<Level>(entity).Value : 1f;

		return 0f;
	}

	private static bool TryResolveAttributeStat(World world, Entity entity, string statName, out float value)
	{
		value = 0f;
		if (!world.Has<UnitAttributes>(entity)) return false;

		var attrs = world.Get<UnitAttributes>(entity);
		value = statName switch
		{
			"vitality" or "vit" => attrs.Vitality,
			"strength" or "str" or "might" or "mig" => attrs.Strength,
			"agility" or "agi" => attrs.Agility,
			"intelligence" or "int" or "focus" or "foc" => attrs.Intelligence,
			"wisdom" or "wis" or "willpower" or "wil" => attrs.Wisdom,
			"fortune" or "fort" or "for" or "finesse" or "fin" => attrs.Fortune,
			_ => float.NaN
		};

		if (float.IsNaN(value))
		{
			value = 0f;
			return false;
		}

		return true;
	}

	private static float GetPhysicalPower(World world, Entity entity, bool hasDerived, bool hasAttack)
		=> hasDerived ? world.Get<DerivedCombatStats>(entity).TotalAttackDamage : (hasAttack ? world.Get<Attack>(entity).Damage : 0f);

	private static float GetArmor(World world, Entity entity, bool hasDerived)
		=> hasDerived ? world.Get<DerivedCombatStats>(entity).TotalArmor : (world.Has<Armor>(entity) ? world.Get<Armor>(entity).FlatArmor : 0f);

	private static float GetAttackSpeed(World world, Entity entity, bool hasDerived, bool hasAttack)
		=> hasDerived && world.Get<DerivedCombatStats>(entity).AttackDelay > 0f ? (1f / world.Get<DerivedCombatStats>(entity).AttackDelay) : (hasAttack && world.Get<Attack>(entity).Cooldown > 0f ? (1f / world.Get<Attack>(entity).Cooldown) : 1f);

	private static float GetAttackDelay(World world, Entity entity, bool hasDerived, bool hasAttack)
		=> hasDerived ? world.Get<DerivedCombatStats>(entity).AttackDelay : (hasAttack ? world.Get<Attack>(entity).Cooldown : 1.5f);

	private static float GetMovementSpeed(World world, Entity entity, bool hasDerived)
		=> hasDerived ? world.Get<DerivedCombatStats>(entity).MovementSpeed : (world.Has<MovementStats>(entity) ? world.Get<MovementStats>(entity).Speed : 5f);

	private static float GetCritChance(World world, Entity entity, bool hasDerived, bool hasAttack)
		=> hasDerived ? world.Get<DerivedCombatStats>(entity).CritChance : (hasAttack ? world.Get<Attack>(entity).CritChance : 0f);

	private static float GetCritMultiplier(World world, Entity entity, bool hasDerived, bool hasAttack)
		=> hasDerived ? world.Get<DerivedCombatStats>(entity).CritMultiplier : (hasAttack ? world.Get<Attack>(entity).CritMultiplier : 1.5f);

	private static float GetArmorPenetration(World world, Entity entity, bool hasDerived, bool hasAttack)
		=> hasDerived ? world.Get<DerivedCombatStats>(entity).FlatArmorPenetration : (hasAttack ? world.Get<Attack>(entity).FlatArmorPenetration : 0f);

	private static float TryResolveOffenseStat(World world, Entity entity, string statName, bool hasDerived, bool hasAttack)
	{
		return statName switch
		{
			"physical_power" or "base_damage" => GetPhysicalPower(world, entity, hasDerived, hasAttack),
			"spell_power" or "magic_amp" => hasDerived ? world.Get<DerivedCombatStats>(entity).SpellPower : 0f,
			"crit_chance" => GetCritChance(world, entity, hasDerived, hasAttack),
			"crit_multiplier" => GetCritMultiplier(world, entity, hasDerived, hasAttack),
			"armor_penetration" => GetArmorPenetration(world, entity, hasDerived, hasAttack),
			_ => float.NaN
		};
	}

	private static float TryResolveDefenseStat(World world, Entity entity, string statName, bool hasDerived)
	{
		return statName switch
		{
			"armor" => GetArmor(world, entity, hasDerived),
			"spell_ward" or "magic_resistance" => hasDerived ? world.Get<DerivedCombatStats>(entity).SpellWard : 0f,
			"tenacity" or "cc_reduction" => hasDerived ? world.Get<DerivedCombatStats>(entity).Tenacity : 0f,
			_ => float.NaN
		};
	}

	private static float TryResolveSpeedStat(World world, Entity entity, string statName, bool hasDerived, bool hasAttack)
	{
		return statName switch
		{
			"attack_speed" => GetAttackSpeed(world, entity, hasDerived, hasAttack),
			"attack_delay" => GetAttackDelay(world, entity, hasDerived, hasAttack),
			"movement_speed" => GetMovementSpeed(world, entity, hasDerived),
			"cast_point" => hasDerived ? world.Get<DerivedCombatStats>(entity).CastPoint : 0.3f,
			"cooldown_reduction" or "cdr" => hasDerived ? world.Get<DerivedCombatStats>(entity).CooldownReduction : 0f,
			_ => float.NaN
		};
	}

	private static bool TryResolveCombatStat(World world, Entity entity, string statName, out float value)
	{
		bool hasDerived = world.Has<DerivedCombatStats>(entity);
		bool hasAttack = world.Has<Attack>(entity);

		value = TryResolveOffenseStat(world, entity, statName, hasDerived, hasAttack);
		if (!float.IsNaN(value)) return true;

		value = TryResolveDefenseStat(world, entity, statName, hasDerived);
		if (!float.IsNaN(value)) return true;

		value = TryResolveSpeedStat(world, entity, statName, hasDerived, hasAttack);
		if (!float.IsNaN(value)) return true;

		value = 0f;
		return false;
	}

	private static float TryResolveHealthStat(World world, Entity entity, string statName)
	{
		bool hasHealth = world.Has<Health>(entity);
		return statName switch
		{
			"hp" or "current_hp" => hasHealth ? world.Get<Health>(entity).Current : 0f,
			"max_hp" => hasHealth ? world.Get<Health>(entity).Max : 0f,
			"hp_regen" => hasHealth ? world.Get<Health>(entity).HpRegen : 0f,
			_ => float.NaN
		};
	}

	private static float TryResolveManaStat(World world, Entity entity, string statName)
	{
		bool hasMana = world.Has<Mana>(entity);
		return statName switch
		{
			"mana" or "current_mana" => hasMana ? world.Get<Mana>(entity).Current : 0f,
			"max_mana" => hasMana ? world.Get<Mana>(entity).Max : 0f,
			"mana_regen" => hasMana ? world.Get<Mana>(entity).ManaRegen : 0f,
			_ => float.NaN
		};
	}

	private static bool TryResolveResourceStat(World world, Entity entity, string statName, out float value)
	{
		value = TryResolveHealthStat(world, entity, statName);
		if (!float.IsNaN(value)) return true;

		value = TryResolveManaStat(world, entity, statName);
		if (!float.IsNaN(value)) return true;

		value = 0f;
		return false;
	}
}