namespace Realm.Ecs.Components.Stats;

/// <summary>
/// Holds the derived combat stats calculated from RPG attributes and baseline stats.
/// </summary>
public record struct DerivedCombatStats(
	float MaxHp = 100f,
	float HpRegen = 0f,
	float Tenacity = 0f,
	float KnockbackResistance = 0f,
	float MovementSpeed = 5f,
	float CastPoint = 0.3f,
	float CritChance = 0f,
	float FlatArmorPenetration = 0f,
	float CritMultiplier = 1.5f,
	float MaxMana = 0f,
	float CooldownReduction = 0f,
	float SpellPower = 0f,
	float ManaRegen = 0f,
	float SpellWard = 0f,
	float StatusEffectBuffer = 0f,
	float TotalArmor = 0f,
	float TotalAttackDamage = 10f,
	float AttackDelay = 1.5f,
	float LifeSteal = 0f,
	float MagicPenetration = 0f,
	float Evasion = 0f,
	float ProcRateMultiplier = 1f,
	float BountyMultiplier = 1f,
	float HealingMultiplier = 1f)
{
	public readonly float Armor => TotalArmor;
	public readonly float Attack => TotalAttackDamage;
	public readonly float Damage => TotalAttackDamage;
	public readonly float Speed => MovementSpeed;

	public readonly float Get(CombatStatType statType) => statType switch
	{
		CombatStatType.MaxHp => MaxHp,
		CombatStatType.HpRegen => HpRegen,
		CombatStatType.MaxMana => MaxMana,
		CombatStatType.ManaRegen => ManaRegen,
		CombatStatType.Armor => TotalArmor,
		CombatStatType.Damage => TotalAttackDamage,
		CombatStatType.Attack => TotalAttackDamage,
		CombatStatType.Speed => MovementSpeed,
		CombatStatType.MovementSpeed => MovementSpeed,
		CombatStatType.SpellPower => SpellPower,
		CombatStatType.Tenacity => Tenacity,
		CombatStatType.SpellWard => SpellWard,
		CombatStatType.CooldownReduction => CooldownReduction,
		CombatStatType.LifeSteal => LifeSteal,
		CombatStatType.MagicPenetration => MagicPenetration,
		CombatStatType.Evasion => Evasion,
		CombatStatType.ProcRateMultiplier => ProcRateMultiplier,
		CombatStatType.BountyMultiplier => BountyMultiplier,
		_ => 0f
	};
}