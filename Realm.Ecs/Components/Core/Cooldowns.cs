using System.Collections.Generic;
using System.Runtime.InteropServices;
using Realm.Ecs.Common;

namespace Realm.Ecs.Components.Core;

/// <summary>
/// Holds the ability and action cooldown timers running on an entity.
/// </summary>
[StructLayout(LayoutKind.Sequential, Pack = 4)]
public struct Cooldowns
{
	public int Count;
	public BuffSlotArray Slots;

	public Cooldowns(IReadOnlyDictionary<string, float>? initialCooldowns)
	{
		this = default;
		if (initialCooldowns == null) return;

		foreach (var pair in initialCooldowns)
		{
			Set(pair.Key, pair.Value, pair.Value);
		}
	}

	public bool TryGetValue(string? actionId, out float remaining)
	{
		return TimedStatusSlots.TryGetValue(in Slots, Count, actionId, out remaining);
	}

	public bool TryGetDuration(string? actionId, out float duration)
	{
		return TimedStatusSlots.TryGetDuration(in Slots, Count, actionId, out duration);
	}

	public void Set(string? actionId, float remaining, float duration)
	{
		TimedStatusSlots.Set(ref Slots, ref Count, actionId, remaining, duration);
	}

	public void Remove(string? actionId)
	{
		TimedStatusSlots.Remove(ref Slots, ref Count, actionId);
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
