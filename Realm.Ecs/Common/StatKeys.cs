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

	/// <summary>Armor points: the physical damage resistance.</summary>
	public const string Armor = "Armor";

	/// <summary>Magic resistance points: the magic damage resistance.</summary>
	public const string MagicResistance = "MagicResistance";

	public const string Attack = "Attack";
	public const string MovementSpeed = "MovementSpeed";
	public const string AttackSpeed = "AttackSpeed";
	public const string AttackDelay = "AttackDelay";
	public const string SpellPower = "SpellPower";
	public const string Tenacity = "Tenacity";
	public const string CooldownReduction = "CooldownReduction";
	public const string LifeSteal = "LifeSteal";
	public const string Evasion = "Evasion";
	public const string ProcRateMultiplier = "ProcRateMultiplier";
	public const string BountyMultiplier = "BountyMultiplier";
	public const string CritChance = "CritChance";
	public const string CritMultiplier = "CritMultiplier";

	/// <summary>Armor points ignored by a physical hit.</summary>
	public const string FlatArmorPenetration = "FlatArmorPenetration";

	/// <summary>Fraction of the target's armor a physical hit ignores, as 0..1.</summary>
	public const string PercentArmorPenetration = "PercentArmorPenetration";

	/// <summary>Magic resistance points ignored by a magic hit.</summary>
	public const string FlatMagicPenetration = "FlatMagicPenetration";

	/// <summary>Fraction of the target's magic resistance a magic hit ignores, as 0..1.</summary>
	public const string PercentMagicPenetration = "PercentMagicPenetration";

	public const string CastPoint = "CastPoint";
	public const string HealingMultiplier = "HealingMultiplier";
	public const string KnockbackResistance = "KnockbackResistance";
	public const string StatusEffectBuffer = "StatusEffectBuffer";

	private static readonly HashSet<string> CanonicalKeys = new(StringComparer.OrdinalIgnoreCase)
	{
		Strength, Agility, Vitality, Intelligence, Wisdom, Fortune,
		MaxHp, HpRegen, MaxMana, ManaRegen, Armor, MagicResistance, Attack, MovementSpeed, AttackSpeed,
		AttackDelay, SpellPower, Tenacity, CooldownReduction, LifeSteal,
		FlatArmorPenetration, PercentArmorPenetration, FlatMagicPenetration, PercentMagicPenetration,
		Evasion, ProcRateMultiplier, BountyMultiplier, CritChance,
		CritMultiplier, CastPoint, HealingMultiplier,
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
