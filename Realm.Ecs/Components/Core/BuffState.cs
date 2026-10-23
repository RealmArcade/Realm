using System.Collections.Generic;
using System.Runtime.InteropServices;
using Realm.Ecs.Common;

namespace Realm.Ecs.Components.Core;

/// <summary>
/// Holds the ongoing buffs and status effects that shape an entity's derived stats, with their timers.
/// </summary>
[StructLayout(LayoutKind.Sequential, Pack = 4)]
public struct BuffState
{
	public int Count;
	public BuffSlotArray Slots;

	public BuffState(IReadOnlyDictionary<string, float>? initialBuffs)
	{
		this = default;
		if (initialBuffs == null) return;

		foreach (var pair in initialBuffs)
		{
			Set(pair.Key, pair.Value, pair.Value);
		}
	}

	public bool TryGetValue(string? buffId, out float remaining)
	{
		return TimedStatusSlots.TryGetValue(in Slots, Count, buffId, out remaining);
	}

	public bool TryGetDuration(string? buffId, out float duration)
	{
		return TimedStatusSlots.TryGetDuration(in Slots, Count, buffId, out duration);
	}

	public void Set(string? buffId, float remaining, float duration)
	{
		TimedStatusSlots.Set(ref Slots, ref Count, buffId, remaining, duration);
	}

	public void Remove(string? buffId)
	{
		TimedStatusSlots.Remove(ref Slots, ref Count, buffId);
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
