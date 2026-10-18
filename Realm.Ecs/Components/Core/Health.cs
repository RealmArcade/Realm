using System.Runtime.InteropServices;

namespace Realm.Ecs.Components.Core;

/// <summary>
/// Represents the health point capacity and regeneration state of an entity.
/// </summary>
[StructLayout(LayoutKind.Sequential, Pack = 4)]
public struct Health
{
	public float Current;
	public float Max;
	public float HpRegen;
	public float HpRegenCombatDelay;
	public float TimeSinceLastDamage;

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

	public float CurrentHp
	{
		get => Current;
		set => Current = value;
	}

	public float MaxHp
	{
		get => Max;
		set => Max = value;
	}
}
