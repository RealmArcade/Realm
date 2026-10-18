using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Realm.Ecs.Common;

namespace Realm.Ecs.Components.Resources;

/// <summary>
/// A component holding all resource amounts for a player entity directly in unmanaged memory.
/// </summary>
[StructLayout(LayoutKind.Sequential, Pack = 4)]
public unsafe struct PlayerResources
{
	public const int MaxCustomSlots = 8;

	public int Gold;
	public int Wood;
	public int Stone;
	public int Ore;
	public fixed int CustomSlots[MaxCustomSlots];

	public PlayerResources(int gold, int wood = 0, int stone = 0, int ore = 0)
	{
		Gold = gold;
		Wood = wood;
		Stone = stone;
		Ore = ore;
	}

	public PlayerResources(IDictionary<ResourceId, int> initial)
	{
		Gold = 0;
		Wood = 0;
		Stone = 0;
		Ore = 0;
		if (initial != null)
		{
			foreach (var (k, v) in initial)
			{
				Set(k, v);
			}
		}
	}

	public int Get(ResourceId id)
	{
		string name = id.Value;
		if (string.Equals(name, "gold", StringComparison.OrdinalIgnoreCase)) return Gold;
		if (string.Equals(name, "wood", StringComparison.OrdinalIgnoreCase)) return Wood;
		if (string.Equals(name, "stone", StringComparison.OrdinalIgnoreCase)) return Stone;
		if (string.Equals(name, "ore", StringComparison.OrdinalIgnoreCase)) return Ore;
		int slot = Math.Abs(id.StringId) % MaxCustomSlots;
		return CustomSlots[slot];
	}

	public void Set(ResourceId id, int value)
	{
		string name = id.Value;
		if (string.Equals(name, "gold", StringComparison.OrdinalIgnoreCase)) { Gold = value; return; }
		if (string.Equals(name, "wood", StringComparison.OrdinalIgnoreCase)) { Wood = value; return; }
		if (string.Equals(name, "stone", StringComparison.OrdinalIgnoreCase)) { Stone = value; return; }
		if (string.Equals(name, "ore", StringComparison.OrdinalIgnoreCase)) { Ore = value; return; }
		int slot = Math.Abs(id.StringId) % MaxCustomSlots;
		CustomSlots[slot] = value;
	}

	public bool TryGetValue(ResourceId id, out int value)
	{
		value = Get(id);
		return true;
	}

	public bool ContainsKey(ResourceId id) => true;

	public int this[ResourceId id]
	{
		get => Get(id);
		set => Set(id, value);
	}

	public PlayerResources Value => this;
}
