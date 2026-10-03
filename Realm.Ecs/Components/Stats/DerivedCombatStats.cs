namespace Realm.Ecs.Components.Stats;

/// <summary>
/// Holds the derived combat stats calculated from RPG attributes and baseline stats.
/// </summary>
internal record struct DerivedCombatStats(
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
	float AttackDelay = 1.5f);
