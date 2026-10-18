using System.Runtime.InteropServices;

namespace Realm.MapAPI;

/// <summary>
/// Represents the health point capacity and regeneration state of an entity.
/// </summary>
[StructLayout(LayoutKind.Sequential, Pack = 4)]
public struct Health
{
	/// <summary>
	/// The current health points of the entity.
	/// </summary>
	public float Current;

	/// <summary>
	/// The maximum health point capacity of the entity.
	/// </summary>
	public float Max;

	/// <summary>
	/// Health point regeneration rate per second.
	/// </summary>
	public float HpRegen;

	/// <summary>
	/// Delay duration in seconds before health regeneration resumes after taking damage.
	/// </summary>
	public float HpRegenCombatDelay;

	/// <summary>
	/// Elapsed time in seconds since the entity last sustained damage.
	/// </summary>
	public float TimeSinceLastDamage;

	/// <summary>
	/// Initializes a new instance of the <see cref="Health"/> struct.
	/// </summary>
	/// <param name="current">The current health points.</param>
	/// <param name="max">The maximum health capacity.</param>
	/// <param name="hpRegen">Health point regeneration rate per second.</param>
	/// <param name="hpRegenCombatDelay">Regeneration combat delay in seconds.</param>
	/// <param name="timeSinceLastDamage">Time since last damage in seconds.</param>
	public Health(
		float current,
		float max,
		float hpRegen = 0f,
		float hpRegenCombatDelay = 0f,
		float timeSinceLastDamage = 0f)
	{
		Current = current;
		Max = max;
		HpRegen = hpRegen;
		HpRegenCombatDelay = hpRegenCombatDelay;
		TimeSinceLastDamage = timeSinceLastDamage;
	}

	/// <summary>
	/// Gets or sets the current health points (alias for <see cref="Current"/>).
	/// </summary>
	public float CurrentHp
	{
		get => Current;
		set => Current = value;
	}

	/// <summary>
	/// Gets or sets the maximum health capacity (alias for <see cref="Max"/>).
	/// </summary>
	public float MaxHp
	{
		get => Max;
		set => Max = value;
	}
}
