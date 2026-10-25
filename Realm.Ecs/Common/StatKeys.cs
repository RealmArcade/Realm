using System;
using System.Collections.Generic;

namespace Realm.Ecs.Common;

/// <summary>
/// The complete set of stat keys the engine consumes. Exactly one key per stat: an authoring key
/// outside this set is a mistake and is reported by <see cref="WarnIfUnknown"/> rather than
/// silently resolved.
/// </summary>
public static class StatKeys
{
	// Attributes
	public const string Strength = "Strength";
	public const string Agility = "Agility";
	public const string Vitality = "Vitality";
	public const string Intelligence = "Intelligence";
	public const string Wisdom = "Wisdom";
	public const string Fortune = "Fortune";

	// Derived combat stats
	public const string MaxHp = "MaxHp";
	public const string HpRegen = "HpRegen";
	public const string MaxMana = "MaxMana";
	public const string ManaRegen = "ManaRegen";
	public const string Armor = "Armor";
	public const string Attack = "Attack";
	public const string MovementSpeed = "MovementSpeed";
	public const string AttackSpeed = "AttackSpeed";
	public const string AttackDelay = "AttackDelay";
	public const string SpellPower = "SpellPower";
	public const string Tenacity = "Tenacity";
	public const string SpellWard = "SpellWard";
	public const string CooldownReduction = "CooldownReduction";
	public const string LifeSteal = "LifeSteal";
	public const string MagicPenetration = "MagicPenetration";
	public const string Evasion = "Evasion";
	public const string ProcRateMultiplier = "ProcRateMultiplier";
	public const string BountyMultiplier = "BountyMultiplier";
	public const string CritChance = "CritChance";
	public const string CritMultiplier = "CritMultiplier";
	public const string FlatArmorPenetration = "FlatArmorPenetration";
	public const string CastPoint = "CastPoint";
	public const string HealingMultiplier = "HealingMultiplier";
	public const string KnockbackResistance = "KnockbackResistance";
	public const string StatusEffectBuffer = "StatusEffectBuffer";

	private static readonly HashSet<string> CanonicalKeys = new(StringComparer.OrdinalIgnoreCase)
	{
		Strength, Agility, Vitality, Intelligence, Wisdom, Fortune,
		MaxHp, HpRegen, MaxMana, ManaRegen, Armor, Attack, MovementSpeed, AttackSpeed,
		AttackDelay, SpellPower, Tenacity, SpellWard, CooldownReduction, LifeSteal,
		MagicPenetration, Evasion, ProcRateMultiplier, BountyMultiplier, CritChance,
		CritMultiplier, FlatArmorPenetration, CastPoint, HealingMultiplier,
		KnockbackResistance, StatusEffectBuffer
	};

	private static readonly HashSet<string> WarnedKeys = new(StringComparer.OrdinalIgnoreCase);

	public static bool IsKnown(string? key) => !string.IsNullOrWhiteSpace(key) && CanonicalKeys.Contains(key);

	/// <summary>
	/// Reports a stat key that no pipeline consumes. Called where authored data enters the world
	/// (upgrades, items, buffs, script) so a typo fails visibly instead of silently doing nothing.
	/// </summary>
	public static void WarnIfUnknown(string source, string? key)
	{
		if (string.IsNullOrWhiteSpace(key) || IsKnown(key)) return;
		if (!WarnedKeys.Add(key)) return;

		Console.WriteLine($"[Stats] Unknown stat key '{key}' from {source}. It will have no effect. Known keys: {string.Join(", ", CanonicalKeys)}");
	}
}
