using System;
using System.Runtime.InteropServices;

namespace Realm.Ecs.Components.Core;

/// <summary>
///     Holds the items a unit holds that are bound to it, so they stay with that unit instead of being put down or
///     handed to another unit.
/// </summary>
[StructLayout(LayoutKind.Sequential, Pack = 4)]
public struct SoulboundItems
{
	public int Count;

	public SoulboundItemIdArray ItemStringIds;

	public bool Contains(string? itemId)
	{
		if (string.IsNullOrEmpty(itemId)) return false;

		for (int index = 0; index < Count; index++)
		{
			if (string.Equals(StringRegistry.Get(ItemStringIds[index]), itemId, StringComparison.OrdinalIgnoreCase)) return true;
		}

		return false;
	}

	public bool Add(string? itemId)
	{
		if (string.IsNullOrEmpty(itemId)) return false;
		if (Contains(itemId)) return false;
		if (Count >= SoulboundItemIdArray.Capacity) return false;

		int wanted = StringRegistry.GetOrAdd(itemId);
		if (wanted == 0) return false;

		ItemStringIds[Count] = wanted;
		Count++;
		return true;
	}

	public void Remove(string? itemId)
	{
		if (string.IsNullOrEmpty(itemId)) return;

		for (int index = Count - 1; index >= 0; index--)
		{
			if (!string.Equals(StringRegistry.Get(ItemStringIds[index]), itemId, StringComparison.OrdinalIgnoreCase)) continue;

			for (int move = index; move < Count - 1; move++)
			{
				ItemStringIds[move] = ItemStringIds[move + 1];
			}

			Count--;
			ItemStringIds[Count] = 0;
			return;
		}
	}
}
