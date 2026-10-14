namespace Realm.MapAPI;

/// <summary>
/// Represents persistent progress and completion state for an achievement or quest milestone.
/// </summary>
public class AchievementSaveData
{
	/// <summary>
	/// Gets or sets the unique identifier of the achievement.
	/// </summary>
	public string AchievementId { get; set; } = string.Empty;

	/// <summary>
	/// Gets or sets a value indicating whether the achievement has been completed.
	/// </summary>
	public bool IsUnlocked { get; set; }

	/// <summary>
	/// Gets or sets the current progress value toward completing the achievement.
	/// </summary>
	public float CurrentProgress { get; set; }

	/// <summary>
	/// Gets or sets the target progress value required to complete the achievement.
	/// </summary>
	public float MaxProgress { get; set; } = 1f;

	/// <summary>
	/// Gets or sets the ISO-8601 UTC timestamp string when the achievement was completed, or null if locked.
	/// </summary>
	public string? UnlockedAtUtc { get; set; }

	/// <summary>
	/// Gets or sets custom arbitrary key-value metadata associated with the achievement.
	/// </summary>
	public Dictionary<string, string> CustomData { get; set; } = new();
}