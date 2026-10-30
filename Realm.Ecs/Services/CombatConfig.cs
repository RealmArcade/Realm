namespace Realm.Ecs.Services;

/// <summary>
///     The three numbers every damage application shares: the resistance curve's K, the ceiling on
///     how much resistance may remove, and the floor a resisted hit still deals. A map overrides
///     them through its metadata's combat tuning block; a map that authors nothing gets these.
/// </summary>
public class CombatConfig
{
	/// <summary>
	///     K in the asymptotic curve <c>R / (R + K)</c>. Higher is flatter: at K points a unit takes
	///     half damage, and no amount of resistance ever reaches immunity.
	/// </summary>
	public float ResistTuningConstantK { get; set; } = 16.67f;

	/// <summary>Hard ceiling on the fraction of damage resistance can remove. Default 90%.</summary>
	public float MaxResistMitigation { get; set; } = 0.9f;

	/// <summary>Minimum damage a resisted hit still deals, so no hit is ever free.</summary>
	public float MinDamageFloor { get; set; } = 1.0f;
}
