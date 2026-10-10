namespace Realm.Ecs.Components.Core;

/// <summary>
///     A component for holding unit item slot contents (e.g. healing potions).
/// </summary>
public record struct Inventory
{
	public System.Collections.Generic.Dictionary<string, int> Items { get; set; }

	public Inventory()
	{
		Items = new System.Collections.Generic.Dictionary<string, int>(System.StringComparer.OrdinalIgnoreCase);
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
		Items ??= new System.Collections.Generic.Dictionary<string, int>(System.StringComparer.OrdinalIgnoreCase);
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
}
