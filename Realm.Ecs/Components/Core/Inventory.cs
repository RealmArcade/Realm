using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace Realm.Ecs.Components.Core;

/// <summary>
/// A component for holding unit inventory item contents and charges.
/// </summary>
[StructLayout(LayoutKind.Sequential, Pack = 4)]
public struct Inventory
{
	public int Handle;

	public Inventory()
	{
		Handle = ColdPayloadRegistry<Dictionary<string, int>>.Register(new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase));
	}

	public Inventory(Dictionary<string, int> items)
	{
		Handle = ColdPayloadRegistry<Dictionary<string, int>>.Register(items ?? new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase));
	}

	public Dictionary<string, int> Items
	{
		get => ColdPayloadRegistry<Dictionary<string, int>>.Get(Handle);
		set => Handle = ColdPayloadRegistry<Dictionary<string, int>>.Register(value);
	}

	public int GetItemCount(string itemId)
	{
		var items = Items;
		if (items != null && !string.IsNullOrEmpty(itemId) && items.TryGetValue(itemId, out int count))
		{
			return count;
		}

		return 0;
	}

	public void SetItemCount(string itemId, int count)
	{
		var items = Items ?? new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
		if (string.IsNullOrEmpty(itemId)) return;
		if (count <= 0)
		{
			items.Remove(itemId);
		}
		else
		{
			items[itemId] = count;
		}
		Items = items;
	}

	public bool HasItem(string itemId)
	{
		return GetItemCount(itemId) > 0;
	}

	public bool AddItem(string itemId, int count = 1)
	{
		if (string.IsNullOrEmpty(itemId)) return false;
		int current = GetItemCount(itemId);
		SetItemCount(itemId, current + count);
		return true;
	}

	public bool RemoveItem(string itemId, int count = 1)
	{
		if (string.IsNullOrEmpty(itemId)) return false;
		int current = GetItemCount(itemId);
		if (current <= 0) return false;
		SetItemCount(itemId, current - count);
		return true;
	}
}
