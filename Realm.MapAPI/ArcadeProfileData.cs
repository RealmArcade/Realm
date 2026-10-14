namespace Realm.MapAPI;

/// <summary>
/// Represents a comprehensive composite arcade profile aggregating heroes, inventories, achievements, and grindable cosmetics.
/// </summary>
public class ArcadeProfileData
{
	/// <summary>
	/// Gets or sets the display name or unique handle of the player.
	/// </summary>
	public string PlayerName { get; set; } = string.Empty;

	/// <summary>
	/// Gets or sets the zero-based player slot index in the game lobby.
	/// </summary>
	public int PlayerSlot { get; set; }

	/// <summary>
	/// Gets or sets the accumulated total active gameplay time in seconds across sessions.
	/// </summary>
	public float TotalPlaytimeSeconds { get; set; }

	/// <summary>
	/// Gets or sets the total number of matches or rounds completed on this map.
	/// </summary>
	public int MatchesPlayed { get; set; }

	/// <summary>
	/// Gets or sets the collection of persistent hero progression records.
	/// </summary>
	public List<HeroSaveData> Heroes { get; set; } = new();

	/// <summary>
	/// Gets or sets the collection of active inventory item records.
	/// </summary>
	public List<InventoryItemSaveData> ActiveInventory { get; set; } = new();

	/// <summary>
	/// Gets or sets the collection of achievement progress records.
	/// </summary>
	public List<AchievementSaveData> Achievements { get; set; } = new();

	/// <summary>
	/// Gets or sets the collection of grindable map cosmetic unlock records.
	/// </summary>
	public List<MapCosmeticSaveData> Cosmetics { get; set; } = new();

	/// <summary>
	/// Gets or sets custom arbitrary key-value metadata associated with the profile.
	/// </summary>
	public Dictionary<string, string> CustomData { get; set; } = new();
}