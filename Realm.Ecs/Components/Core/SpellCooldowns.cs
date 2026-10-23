using System.Collections.Generic;
using System.Runtime.InteropServices;
using Realm.Ecs.Common;

namespace Realm.Ecs.Components.Core;

/// <summary>
/// Holds the individual spell cooldown timers running on an entity or player.
/// </summary>
[StructLayout(LayoutKind.Sequential, Pack = 4)]
public struct SpellCooldowns
{
	public int Count;
	public BuffSlotArray Slots;

	public SpellCooldowns(IReadOnlyDictionary<string, float>? initialCooldowns)
	{
		this = default;
		if (initialCooldowns == null) return;

		foreach (var pair in initialCooldowns)
		{
			Set(pair.Key, pair.Value, pair.Value);
		}
	}

	public bool TryGetValue(string? spellId, out float remaining)
	{
		return TimedStatusSlots.TryGetValue(in Slots, Count, spellId, out remaining);
	}

	public bool TryGetDuration(string? spellId, out float duration)
	{
		return TimedStatusSlots.TryGetDuration(in Slots, Count, spellId, out duration);
	}

	public void Set(string? spellId, float remaining, float duration)
	{
		TimedStatusSlots.Set(ref Slots, ref Count, spellId, remaining, duration);
	}

	public void Remove(string? spellId)
	{
		TimedStatusSlots.Remove(ref Slots, ref Count, spellId);
	}

	public void Advance(float delta)
	{
		TimedStatusSlots.Advance(ref Slots, ref Count, delta);
	}

	public void Clear()
	{
		Count = 0;
		Slots = default;
	}

	public string? GetKey(int index)
	{
		return TimedStatusSlots.GetKey(in Slots, index);
	}

	public float GetRemaining(int index)
	{
		return TimedStatusSlots.GetRemaining(in Slots, index);
	}

	public float GetDuration(int index)
	{
		return TimedStatusSlots.GetDuration(in Slots, index);
	}

	public void CopyKeys(List<string> destination)
	{
		TimedStatusSlots.CopyKeys(in Slots, Count, destination);
	}
}
