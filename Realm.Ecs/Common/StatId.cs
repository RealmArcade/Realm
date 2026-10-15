using Realm.Ecs.Components.Stats;

namespace Realm.Ecs.Common;

/// <summary>
///     A type-safe wrapper for a Stat ID.
/// </summary>
public readonly record struct StatId
{
	public StatId(string value)
	{
		Value = value;
	}

	public StatId(AttributeType attribute)
	{
		Value = attribute switch
		{
			AttributeType.Strength => nameof(AttributeType.Strength),
			AttributeType.Agility => nameof(AttributeType.Agility),
			AttributeType.Vitality => nameof(AttributeType.Vitality),
			AttributeType.Intelligence => nameof(AttributeType.Intelligence),
			AttributeType.Wisdom => nameof(AttributeType.Wisdom),
			AttributeType.Fortune => nameof(AttributeType.Fortune),
			_ => attribute.ToString()
		};
	}

	public StatId(CombatStatType combatStat)
	{
		Value = combatStat switch
		{
			CombatStatType.MaxHp => nameof(CombatStatType.MaxHp),
			CombatStatType.HpRegen => nameof(CombatStatType.HpRegen),
			CombatStatType.MaxMana => nameof(CombatStatType.MaxMana),
			CombatStatType.ManaRegen => nameof(CombatStatType.ManaRegen),
			CombatStatType.Armor => nameof(CombatStatType.Armor),
			CombatStatType.Damage => nameof(CombatStatType.Damage),
			CombatStatType.Speed => nameof(CombatStatType.Speed),
			CombatStatType.Attack => nameof(CombatStatType.Attack),
			CombatStatType.MovementSpeed => nameof(CombatStatType.MovementSpeed),
			CombatStatType.SpellPower => nameof(CombatStatType.SpellPower),
			CombatStatType.Tenacity => nameof(CombatStatType.Tenacity),
			CombatStatType.SpellWard => nameof(CombatStatType.SpellWard),
			CombatStatType.CooldownReduction => nameof(CombatStatType.CooldownReduction),
			CombatStatType.LifeSteal => nameof(CombatStatType.LifeSteal),
			CombatStatType.MagicPenetration => nameof(CombatStatType.MagicPenetration),
			CombatStatType.Evasion => nameof(CombatStatType.Evasion),
			CombatStatType.ProcRateMultiplier => nameof(CombatStatType.ProcRateMultiplier),
			CombatStatType.BountyMultiplier => nameof(CombatStatType.BountyMultiplier),
			_ => combatStat.ToString()
		};
	}

	public string Value { get; }
}