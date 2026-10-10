namespace Realm.Ecs.Components.Stats;

/// <summary>
/// Represents the baseline un-modified combat stats for an entity before RPG attribute contributions.
/// </summary>
public record struct UnitBaseStats(
	float BaseMaxHp = 100f,
	float BaseHpRegen = 0f,
	float BaseMaxMana = 0f,
	float BaseManaRegen = 0f,
	float BaseArmor = 0f,
	float BaseDamage = 10f,
	float BaseAttackInterval = 1.5f,
	float BaseSpeed = 5f,
	float BaseCastPoint = 0.3f,
	float BaseCritChance = 0f,
	float BaseCritMultiplier = 1.5f,
	float BaseFlatArmorPenetration = 0f);
