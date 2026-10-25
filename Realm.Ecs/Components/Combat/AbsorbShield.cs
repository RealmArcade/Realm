using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Realm.MapAPI;

namespace Realm.Ecs.Components.Combat;

/// <summary>One absorb pool: what granted it, how much is left, and when it expires.</summary>
[StructLayout(LayoutKind.Sequential, Pack = 4)]
public struct AbsorbShieldSlot
{
	public int SourceStringId;
	public float Amount;
	public float Remaining;
	public float Duration;
}

[InlineArray(Capacity)]
public struct AbsorbShieldSlotArray
{
	public const int Capacity = 8;

	private AbsorbShieldSlot _first;
}

/// <summary>
///     Absorb pools standing in front of an entity's health. Damage is taken from the oldest pool
///     first, so a shield applied later never shields damage that an earlier one should have eaten.
///     A pool with no duration lasts until it is used up.
/// </summary>
[StructLayout(LayoutKind.Sequential, Pack = 4)]
public struct AbsorbShield
{
	public int Count;
	public AbsorbShieldSlotArray Slots;

	public void Add(string? sourceId, float amount, float duration)
	{
		if (amount <= 0f) return;

		int sourceStringId = StringRegistry.GetOrAdd(sourceId);
		if (sourceStringId == 0) return;

		// A shield from the same source refreshes rather than stacking endlessly: two casts of the
		// same ability top the pool up to the larger of the two, which is what a recast should do.
		for (int index = 0; index < Count; index++)
		{
			if (Slots[index].SourceStringId != sourceStringId) continue;

			Slots[index].Amount = MathF.Max(Slots[index].Amount, amount);
			Slots[index].Remaining = MathF.Max(Slots[index].Remaining, duration);
			Slots[index].Duration = MathF.Max(Slots[index].Duration, duration);
			return;
		}

		if (Count >= AbsorbShieldSlotArray.Capacity)
		{
			// Overflow drops the pool closest to expiring, mirroring the timed-status policy.
			int soonestIndex = 0;
			for (int index = 1; index < Count; index++)
			{
				if (Slots[index].Remaining >= Slots[soonestIndex].Remaining) continue;
				soonestIndex = index;
			}

			Slots[soonestIndex] = default;
			Count--;
		}

		Slots[Count] = new AbsorbShieldSlot
		{
			SourceStringId = sourceStringId,
			Amount = amount,
			Remaining = duration,
			Duration = duration
		};
		Count++;
	}

	/// <summary>Consumes as much of the incoming damage as the pools can take and returns the rest.</summary>
	public float Consume(float damage)
	{
		if (damage <= 0f || Count == 0) return damage;

		float remaining = damage;
		for (int index = 0; index < Count && remaining > 0f; index++)
		{
			float absorbed = MathF.Min(Slots[index].Amount, remaining);
			Slots[index].Amount -= absorbed;
			remaining -= absorbed;

			if (Slots[index].Amount > 0.0001f) continue;

			Count--;
			Slots[index] = Slots[Count];
			Slots[Count] = default;
			index--;
		}

		return remaining;
	}

	public void Advance(float delta)
	{
		if (Count == 0) return;

		for (int index = Count - 1; index >= 0; index--)
		{
			if (Slots[index].Duration <= 0f) continue;

			Slots[index].Remaining -= delta;
			if (Slots[index].Remaining > 0f) continue;

			Count--;
			Slots[index] = Slots[Count];
			Slots[Count] = default;
		}
	}

	public readonly float Total()
	{
		float total = 0f;
		for (int index = 0; index < Count; index++)
		{
			total += Slots[index].Amount;
		}

		return total;
	}

	public void Clear()
	{
		Count = 0;
		Slots = default;
	}
}
