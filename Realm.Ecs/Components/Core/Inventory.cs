using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Realm.MapAPI;

namespace Realm.Ecs.Components.Core;

/// <summary>
/// Holds the items a unit carries together with the number of charges left for each, in the order they were picked up.
/// </summary>
[StructLayout(LayoutKind.Sequential, Pack = 4)]
public struct Inventory
{
	public int Count;
	public InventorySlotArray Slots;

	public Inventory(IReadOnlyDictionary<string, int>? items)
	{
		this = default;
		if (items == null) return;

		foreach (var pair in items)
		{
			SetItemCount(pair.Key, pair.Value);
		}
	}

	public string? GetItemId(int index)
	{
		if (index < 0 || index >= Count) return null;

		return StringRegistry.Get(Slots[index].ItemStringId);
	}

	public int GetCharges(int index)
	{
		return index < 0 || index >= Count ? 0 : Slots[index].Count;
	}

	public int GetItemCount(string? itemId)
	{
		int index = IndexOf(itemId);
		return index < 0 ? 0 : Slots[index].Count;
	}

	public bool HasItem(string? itemId)
	{
		return GetItemCount(itemId) > 0;
	}

	public void SetItemCount(string? itemId, int count)
	{
		if (string.IsNullOrEmpty(itemId)) return;

		int index = IndexOf(itemId);
		if (count <= 0)
		{
			if (index >= 0) RemoveAt(index);
			return;
		}

		if (index >= 0)
		{
			Slots[index].Count = count;
			return;
		}

		if (Count >= InventorySlotArray.Capacity) return;

		int itemStringId = StringRegistry.GetOrAdd(itemId);
		if (itemStringId == 0) return;

		Slots[Count] = new InventorySlot { ItemStringId = itemStringId, Count = count };
		Count++;
	}

	public bool AddItem(string? itemId, int count = 1)
	{
		if (string.IsNullOrEmpty(itemId)) return false;

		int index = IndexOf(itemId);
		if (index < 0 && Count >= InventorySlotArray.Capacity) return false;

		int added = Math.Max(1, count);
		SetItemCount(itemId, (index < 0 ? 0 : Slots[index].Count) + added);
		return true;
	}

	public bool RemoveItem(string? itemId, int count = 1)
	{
		int index = IndexOf(itemId);
		if (index < 0) return false;

		SetItemCount(itemId, Slots[index].Count - Math.Max(1, count));
		return true;
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

	public void Clear()
	{
		Count = 0;
		Slots = default;
	}

	public List<string> ToList()
	{
		var itemIds = new List<string>(Count);
		for (int index = 0; index < Count; index++)
		{
			string? itemId = GetItemId(index);
			if (itemId != null) itemIds.Add(itemId);
		}

		return itemIds;
	}

	private int IndexOf(string? itemId)
	{
		if (string.IsNullOrEmpty(itemId)) return -1;

		for (int index = 0; index < Count; index++)
		{
			string knownItemId = StringRegistry.Get(Slots[index].ItemStringId);
			if (string.Equals(knownItemId, itemId, StringComparison.OrdinalIgnoreCase)) return index;
		}

		return -1;
	}
}
