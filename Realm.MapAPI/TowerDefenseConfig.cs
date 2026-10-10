namespace Realm.MapAPI;

/// <summary>
/// Configuration settings for tower defense combat behavior, including range, damage, cooldowns, and visual effects.
/// </summary>
public readonly struct TowerDefenseConfig
{
	/// <summary>
	/// Gets the attack range of the tower defense unit.
	/// </summary>
	public float Range { get; }

	/// <summary>
	/// Gets the base attack damage dealt by the tower defense unit.
	/// </summary>
	public float Damage { get; }

	/// <summary>
	/// Gets the cooldown interval between attacks in seconds.
	/// </summary>
	public float AttackCooldownSeconds { get; }

	/// <summary>
	/// Gets the identifier of the visual effect spawned when attacking.
	/// </summary>
	public string VisualEffectId { get; }

	/// <summary>
	/// Initializes a new instance of the <see cref="TowerDefenseConfig"/> struct.
	/// </summary>
	/// <param name="range">The attack range of the tower.</param>
	/// <param name="damage">The damage dealt per attack.</param>
	/// <param name="attackCooldownSeconds">The cooldown duration in seconds between attacks.</param>
	/// <param name="visualEffectId">The identifier of the visual effect to spawn when attacking, or an empty string for none.</param>
	public TowerDefenseConfig(float range, float damage, float attackCooldownSeconds, string visualEffectId)
	{
		Range = range;
		Damage = damage;
		AttackCooldownSeconds = attackCooldownSeconds;
		VisualEffectId = visualEffectId;
	}
}