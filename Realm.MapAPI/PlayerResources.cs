using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace Realm.MapAPI;

/// <summary>
/// A component holding all resource stockpile amounts for a player slot directly in unmanaged memory.
/// </summary>
[StructLayout(LayoutKind.Sequential, Pack = 4)]
public unsafe struct PlayerResources
{
	/// <summary>Maximum custom resource slots supported in fixed buffer.</summary>
	public const int MaxCustomSlots = 8;

	/// <summary>Gold resource stockpile amount.</summary>
	public int Gold;
	/// <summary>Wood resource stockpile amount.</summary>
	public int Wood;
	/// <summary>Stone resource stockpile amount.</summary>
	public int Stone;
	/// <summary>Ore resource stockpile amount.</summary>
	public int Ore;
	/// <summary>Fixed array buffer for custom resource categories.</summary>
	public fixed int CustomSlots[MaxCustomSlots];

	/// <summary>
	/// Initializes a new instance of the <see cref="PlayerResources"/> struct with standard resources.
	/// </summary>
	public PlayerResources(int gold, int wood = 0, int stone = 0, int ore = 0)
	{
		Gold = gold;
		Wood = wood;
		Stone = stone;
		Ore = ore;
	}

	/// <summary>
	/// Initializes a new instance of the <see cref="PlayerResources"/> struct from a resource dictionary.
	/// </summary>
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

	/// <summary>
	/// Retrieves the resource amount for the given resource identifier.
	/// </summary>
	public int Get(ResourceId id)
	{
		string name = id.Value;
		if (string.Equals(name, "gold", StringComparison.OrdinalIgnoreCase)) return Gold;
		if (string.Equals(name, "wood", StringComparison.OrdinalIgnoreCase)) return Wood;
		if (string.Equals(name, "stone", StringComparison.OrdinalIgnoreCase)) return Stone;
		if (string.Equals(name, "ore", StringComparison.OrdinalIgnoreCase)) return Ore;
		int slot = Math.Abs(name.GetHashCode()) % MaxCustomSlots;
		return CustomSlots[slot];
	}

	/// <summary>
	/// Sets the resource amount for the given resource identifier.
	/// </summary>
	public void Set(ResourceId id, int value)
	{
		string name = id.Value;
		if (string.Equals(name, "gold", StringComparison.OrdinalIgnoreCase)) { Gold = value; return; }
		if (string.Equals(name, "wood", StringComparison.OrdinalIgnoreCase)) { Wood = value; return; }
		if (string.Equals(name, "stone", StringComparison.OrdinalIgnoreCase)) { Stone = value; return; }
		if (string.Equals(name, "ore", StringComparison.OrdinalIgnoreCase)) { Ore = value; return; }
		int slot = Math.Abs(name.GetHashCode()) % MaxCustomSlots;
		CustomSlots[slot] = value;
	}

	/// <summary>
	/// Attempts to retrieve the resource amount for the given resource identifier.
	/// </summary>
	public bool TryGetValue(ResourceId id, out int value)
	{
		value = Get(id);
		return true;
	}

	/// <summary>
	/// Determines whether the resource type is supported.
	/// </summary>
	public bool ContainsKey(ResourceId id) => true;

	/// <summary>
	/// Gets or sets resource amounts by identifier.
	/// </summary>
	public int this[ResourceId id]
	{
		get => Get(id);
		set => Set(id, value);
	}

	/// <summary>
	/// Returns a copy of this struct.
	/// </summary>
	public PlayerResources Value => this;
}
