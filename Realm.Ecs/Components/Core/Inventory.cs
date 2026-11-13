using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

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
		if (string.IsNullOrEmpty(itemId)) return 0;

		int total = 0;
		for (int index = 0; index < Count; index++)
		{
			string knownItemId = StringRegistry.Get(Slots[index].ItemStringId);
			if (!string.Equals(knownItemId, itemId, StringComparison.OrdinalIgnoreCase)) continue;

			total += Slots[index].Count;
		}

		return total;
	}

	public bool HasItem(string? itemId)
	{
		return GetItemCount(itemId) > 0;
	}

	/// <summary>
	///     Answers how many more of an item the inventory could take when the match grants it the stated number of slots
	///     and each slot holds at most the stated number.
	/// </summary>
	/// <param name="itemId">The item definition identifier.</param>
	/// <param name="maxStacks">The greatest number one slot may hold.</param>
	/// <param name="slotLimit">The number of slots the unit may fill, which is the match's rule rather than the storage size.</param>
	/// <returns>The number of pieces that would fit, never negative.</returns>
	public int AvailableRoom(string? itemId, int maxStacks, int slotLimit)
	{
		if (string.IsNullOrEmpty(itemId)) return 0;

		int limit = Math.Max(1, maxStacks);
		int usableSlots = Math.Clamp(slotLimit, 0, InventorySlotArray.Capacity);
		int room = Math.Max(0, usableSlots - Count) * limit;

		for (int index = 0; index < Count && index < usableSlots; index++)
		{
			string knownItemId = StringRegistry.Get(Slots[index].ItemStringId);
			if (!string.Equals(knownItemId, itemId, StringComparison.OrdinalIgnoreCase)) continue;

			room += Math.Max(0, limit - Slots[index].Count);
		}

		return room;
	}

	public void SetItemCount(string? itemId, int count)
	{
		if (string.IsNullOrEmpty(itemId)) return;

		int index = IndexOf(itemId);
		if (count <= 0)
		{
			for (int slot = Count - 1; slot >= 0; slot--)
			{
				if (string.Equals(StringRegistry.Get(Slots[slot].ItemStringId), itemId, StringComparison.OrdinalIgnoreCase)) RemoveAt(slot);
			}

			return;
		}

		if (index < 0)
		{
			if (Count >= InventorySlotArray.Capacity) return;

			int newItemStringId = StringRegistry.GetOrAdd(itemId);
			if (newItemStringId == 0) return;

			Slots[Count] = new InventorySlot { ItemStringId = newItemStringId, Count = count };
			Count++;
			return;
		}

		Slots[index].Count = count;
		for (int slot = Count - 1; slot > index; slot--)
		{
			if (string.Equals(StringRegistry.Get(Slots[slot].ItemStringId), itemId, StringComparison.OrdinalIgnoreCase)) RemoveAt(slot);
		}
	}

	public bool AddItem(string? itemId, int count = 1)
	{
		return AddItem(itemId, count, int.MaxValue, InventorySlotArray.Capacity);
	}

	/// <summary>
	///     Adds an item, filling each slot only up to the stated stack limit and opening further slots for the overflow
	///     until the unit's granted slots are used up. A request the inventory cannot hold in full is refused without
	///     changing anything, so a caller that reads the result never has to undo a partial add.
	/// </summary>
	/// <param name="itemId">The item definition identifier.</param>
	/// <param name="count">The number of pieces to add; values below one are treated as one.</param>
	/// <param name="maxStacks">The greatest number one slot may hold.</param>
	/// <param name="slotLimit">The number of slots the unit may fill, which is the match's rule rather than the storage size.</param>
	/// <returns>True when every requested piece was added; otherwise false.</returns>
	public bool AddItem(string? itemId, int count, int maxStacks, int slotLimit)
	{
		if (string.IsNullOrEmpty(itemId)) return false;

		int limit = Math.Max(1, maxStacks);
		int usableSlots = Math.Clamp(slotLimit, 0, InventorySlotArray.Capacity);
		int remaining = Math.Max(1, count);
		int itemStringId = StringRegistry.GetOrAdd(itemId);
		if (itemStringId == 0) return false;

		long capacity = 0;
		for (int index = 0; index < Count && index < usableSlots; index++)
		{
			if (Slots[index].ItemStringId != itemStringId) continue;

			capacity += Math.Max(0, limit - Slots[index].Count);
		}

		capacity += (long)Math.Max(0, usableSlots - Count) * limit;
		if (capacity < remaining) return false;

		for (int index = 0; index < Count && index < usableSlots && remaining > 0; index++)
		{
			if (Slots[index].ItemStringId != itemStringId) continue;

			int room = limit - Slots[index].Count;
			if (room <= 0) continue;

			int moved = Math.Min(room, remaining);
			Slots[index].Count += moved;
			remaining -= moved;
		}

		while (remaining > 0 && Count < usableSlots)
		{
			int moved = Math.Min(limit, remaining);
			Slots[Count] = new InventorySlot { ItemStringId = itemStringId, Count = moved };
			Count++;
			remaining -= moved;
		}

		return remaining == 0;
	}

	public bool RemoveItem(string? itemId, int count = 1)
	{
		if (string.IsNullOrEmpty(itemId)) return false;

		int remaining = Math.Max(1, count);
		bool removedAny = false;

		for (int index = Count - 1; index >= 0 && remaining > 0; index--)
		{
			string knownItemId = StringRegistry.Get(Slots[index].ItemStringId);
			if (!string.Equals(knownItemId, itemId, StringComparison.OrdinalIgnoreCase)) continue;

			int taken = Math.Min(Slots[index].Count, remaining);
			Slots[index].Count -= taken;
			remaining -= taken;
			removedAny |= taken > 0;

			if (Slots[index].Count <= 0) RemoveAt(index);
		}

		return removedAny;
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
