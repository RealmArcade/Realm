using System.Runtime.InteropServices;
namespace Realm.Ecs.Components.Core;

/// <summary>
///     Represents the current, maximum, and regeneration rate of mana for an entity.
/// </summary>
[StructLayout(LayoutKind.Sequential, Pack = 4)]
public struct Mana
{
	public float Current;
	public float Max;
	public float ManaRegen;

	public Mana(
		float current,
		float max,
		float manaRegen = 0f)
	{
		Current = current;
		Max = max;
		ManaRegen = manaRegen;
	}

	public float CurrentMana
	{
		get => Current;
		set => Current = value;
	}

	public float MaxMana
	{
		get => Max;
		set => Max = value;
	}
}
