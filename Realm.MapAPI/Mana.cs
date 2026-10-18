using System.Runtime.InteropServices;

namespace Realm.MapAPI;

/// <summary>
/// Represents the current, maximum, and regeneration rate of mana for an entity.
/// </summary>
[StructLayout(LayoutKind.Sequential, Pack = 4)]
public struct Mana
{
	/// <summary>
	/// The current mana points of the entity.
	/// </summary>
	public float Current;

	/// <summary>
	/// The maximum mana point capacity of the entity.
	/// </summary>
	public float Max;

	/// <summary>
	/// Mana point regeneration rate per second.
	/// </summary>
	public float ManaRegen;

	/// <summary>
	/// Initializes a new instance of the <see cref="Mana"/> struct.
	/// </summary>
	/// <param name="current">The current mana points.</param>
	/// <param name="max">The maximum mana capacity.</param>
	/// <param name="manaRegen">Mana point regeneration rate per second.</param>
	public Mana(
		float current,
		float max,
		float manaRegen = 0f)
	{
		Current = current;
		Max = max;
		ManaRegen = manaRegen;
	}

	/// <summary>
	/// Gets or sets the current mana points (alias for <see cref="Current"/>).
	/// </summary>
	public float CurrentMana
	{
		get => Current;
		set => Current = value;
	}

	/// <summary>
	/// Gets or sets the maximum mana capacity (alias for <see cref="Max"/>).
	/// </summary>
	public float MaxMana
	{
		get => Max;
		set => Max = value;
	}
}
