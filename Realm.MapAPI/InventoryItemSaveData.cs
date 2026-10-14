namespace Realm.MapAPI;

/// <summary>
/// Represents an item entry stored inside a unit or player inventory.
/// </summary>
public class InventoryItemSaveData
{
	/// <summary>
	/// Gets or sets the unique identifier of the item.
	/// </summary>
	public string ItemId { get; set; } = string.Empty;

	/// <summary>
	/// Gets or sets the zero-based inventory slot index.
	/// </summary>
	public int SlotIndex { get; set; }

	/// <summary>
	/// Gets or sets the stack count or remaining charges of the item.
	/// </summary>
	public int Charges { get; set; } = 1;

	/// <summary>
	/// Gets or sets custom arbitrary key-value metadata associated with the item.
	/// </summary>
	public Dictionary<string, string> CustomData { get; set; } = new();
}