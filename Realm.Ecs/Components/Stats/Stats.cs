using System.Collections.Generic;
using System.Runtime.InteropServices;
using Realm.Ecs.Common;

namespace Realm.Ecs.Components.Stats;

/// <summary>
/// Holds the stats that shape an entity together with the value it has for each of them.
/// </summary>
[StructLayout(LayoutKind.Sequential, Pack = 4)]
public struct Stats
{
	public int Count;
	public StatSlotArray Slots;

	public Stats(IReadOnlyDictionary<StatId, float>? values)
	{
		this = default;
		if (values == null) return;

		foreach (var pair in values)
		{
			Set(pair.Key, pair.Value);
		}
	}

	public bool TryGetValue(StatId statId, out float value)
	{
		int index = IndexOf(statId);
		if (index < 0)
		{
			value = 0f;
			return false;
		}

		value = Slots[index].Value;
		return true;
	}

	public float Get(StatId statId)
	{
		return TryGetValue(statId, out float value) ? value : 0f;
	}

	public void Set(StatId statId, float value)
	{
		if (statId.StringId == 0) return;

		int index = IndexOf(statId);
		if (index < 0)
		{
			if (Count >= StatSlotArray.Capacity) return;

			index = Count;
			Count++;
			Slots[index].StatStringId = statId.StringId;
		}

		Slots[index].Value = value;
	}

	public void Clear()
	{
		Count = 0;
		Slots = default;
	}

	private int IndexOf(StatId statId)
	{
		if (statId.StringId == 0) return -1;

		for (int index = 0; index < Count; index++)
		{
			if (Slots[index].StatStringId == statId.StringId) return index;
		}

		return -1;
	}
}
