using System.Collections.Generic;
using System.Runtime.InteropServices;
using Realm.Ecs.Components.Stats;

namespace Realm.Ecs.Components.Core;

/// <summary>
/// Holds the stat modifiers currently shaping an entity, each with its own value, duration and source.
/// </summary>
[StructLayout(LayoutKind.Sequential, Pack = 4)]
public struct ModifierState
{
	public int Count;
	public ModifierSlotArray Slots;

	public ModifierState(IReadOnlyList<StatModifier>? modifiers)
	{
		this = default;
		if (modifiers == null) return;

		for (int index = 0; index < modifiers.Count; index++)
		{
			Add(modifiers[index]);
		}
	}

	public StatModifier Get(int index)
	{
		return index < 0 || index >= Count ? default : Slots[index];
	}

	public void Set(int index, in StatModifier modifier)
	{
		if (index < 0 || index >= Count) return;

		Slots[index] = modifier;
	}

	public void Add(in StatModifier modifier)
	{
		int slotIndex = Count;
		if (slotIndex >= ModifierSlotArray.Capacity)
		{
			slotIndex = IndexOfSoonestExpiring();
		}
		else
		{
			Count++;
		}

		Slots[slotIndex] = modifier;
	}

	public void RemoveAt(int index)
	{
		if (index < 0 || index >= Count) return;

		for (int move = index; move < Count - 1; move++)
		{
			Slots[move] = Slots[move + 1];
		}

		Count--;
		Slots[Count] = default;
	}

	public bool Advance(float delta)
	{
		bool removedAny = false;
		for (int index = Count - 1; index >= 0; index--)
		{
			float duration = Slots[index].Duration;
			if (duration <= 0f) continue;

			float remaining = duration - delta;
			if (remaining > 0f)
			{
				Slots[index].Duration = remaining;
				continue;
			}

			RemoveAt(index);
			removedAny = true;
		}

		return removedAny;
	}

	public void Clear()
	{
		Count = 0;
		Slots = default;
	}

	public List<StatModifier> ToList()
	{
		var modifiers = new List<StatModifier>(Count);
		for (int index = 0; index < Count; index++)
		{
			modifiers.Add(Slots[index]);
		}

		return modifiers;
	}

	private int IndexOfSoonestExpiring()
	{
		int soonestIndex = 0;
		float soonestDuration = float.MaxValue;
		for (int index = 0; index < Count; index++)
		{
			float duration = Slots[index].Duration;
			if (duration <= 0f) continue;
			if (duration >= soonestDuration) continue;

			soonestIndex = index;
			soonestDuration = duration;
		}

		return soonestIndex;
	}
}
