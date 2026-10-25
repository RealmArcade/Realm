namespace Realm.Shared.Metadata;

/// <summary>
/// Autocast policy for one ability. Defaults keep an ability castable whenever it is off cooldown
/// and affordable, so an existing ability without a policy behaves as before.
/// </summary>
public class AutoCastMetadata
{
	/// <summary>When false, this ability is never auto-cast even if its slot is enabled.</summary>
	public bool Enabled { get; set; } = true;

	/// <summary>
	/// When false the ability is cast at the caster's own position instead of requiring a nearby
	/// enemy, which is what self-buffs and auras want.
	/// </summary>
	public bool RequiresEnemyTarget { get; set; } = true;

	/// <summary>
	/// For healing abilities, only cast on an ally whose health fraction is at or below this value.
	/// 1 means any wounded ally qualifies.
	/// </summary>
	public float AllyHealthPercentBelow { get; set; } = 1f;

	/// <summary>Flat mana the caster keeps in reserve, on top of the ability's cost.</summary>
	public float ManaReserve { get; set; }

	/// <summary>Fraction of maximum mana the caster must still hold after paying the cost.</summary>
	public float MinManaPercent { get; set; }
}
