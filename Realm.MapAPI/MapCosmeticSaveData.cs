namespace Realm.MapAPI;

/// <summary>
/// Represents persistent state for an in-map grindable cosmetic unlock, such as a title, aura, or skin variation.
/// </summary>
public class MapCosmeticSaveData
{
	/// <summary>
	/// Gets or sets the unique identifier of the cosmetic item.
	/// </summary>
	public string CosmeticId { get; set; } = string.Empty;

	/// <summary>
	/// Gets or sets the category or type of the cosmetic (e.g. "Title", "Aura", "Skin", "Badge").
	/// </summary>
	public string CosmeticType { get; set; } = "Title";

	/// <summary>
	/// Gets or sets a value indicating whether the player has unlocked this cosmetic through map gameplay.
	/// </summary>
	public bool IsUnlocked { get; set; }

	/// <summary>
	/// Gets or sets a value indicating whether this cosmetic is currently active or equipped.
	/// </summary>
	public bool IsEquipped { get; set; }

	/// <summary>
	/// Gets or sets the ISO-8601 UTC timestamp string when the cosmetic was unlocked, or null if locked.
	/// </summary>
	public string? UnlockedAtUtc { get; set; }

	/// <summary>
	/// Gets or sets custom arbitrary key-value metadata associated with the cosmetic.
	/// </summary>
	public Dictionary<string, string> CustomData { get; set; } = new();
}