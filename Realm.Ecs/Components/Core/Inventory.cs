using System;
using System.Collections.Generic;

namespace Realm.Ecs.Components.Core;

/// <summary>
/// A component for holding unit inventory item contents and charges.
/// </summary>
public record struct Inventory
{
	public Dictionary<string, int> Items { get; set; }

	public Inventory()
	{
		Items = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
	}

	public Inventory(Dictionary<string, int> items)
	{
		Items = items ?? new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
	}

	public int GetItemCount(string itemId)
	{
		if (Items != null && !string.IsNullOrEmpty(itemId) && Items.TryGetValue(itemId, out int count))
		{
			return count;
		}

		return 0;
	}

	public void SetItemCount(string itemId, int count)
	{
		Items ??= new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
		if (string.IsNullOrEmpty(itemId)) return;
		if (count <= 0)
		{
			Items.Remove(itemId);
		}
		else
		{
			Items[itemId] = count;
		}
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
