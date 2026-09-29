using System;
using System.Collections.Generic;

namespace Realm.MapAPI;

/// <summary>
/// Represents a structured envelope wrapping a save payload with versioning, player identity, and cryptographic integrity data.
/// </summary>
/// <typeparam name="T">The type of the saved game state payload.</typeparam>
public class SaveEnvelope<T>
{
    /// <summary>
    /// Gets or sets the schema version number of the save payload.
    /// </summary>
    public int DataVersion { get; set; } = 1;

    /// <summary>
    /// Gets or sets the name of the map script associated with this save file.
    /// </summary>
    public string MapName { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the player identifier or username bound to this save file.
    /// </summary>
    public string PlayerName { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the ISO-8601 UTC timestamp string representing when the save file was created or last written.
    /// </summary>
    public string TimestampUtc { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the cryptographic signature string used for integrity verification and anti-tamper checking.
    /// </summary>
    public string Signature { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the typed game state payload.
    /// </summary>
    public T? Payload { get; set; }
}

/// <summary>
/// Represents persistent hero progression state, including level, experience, skill points, and stats.
/// </summary>
public class HeroSaveData
{
    /// <summary>
    /// Gets or sets the archetype or unit type identifier of the hero.
    /// </summary>
    public string HeroTypeId { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the display name of the hero.
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the current level of the hero.
    /// </summary>
    public int Level { get; set; } = 1;

    /// <summary>
    /// Gets or sets the accumulated experience points of the hero.
    /// </summary>
    public float Experience { get; set; }

    /// <summary>
    /// Gets or sets the number of unspent or allocated skill points.
    /// </summary>
    public int SkillPoints { get; set; }

    /// <summary>
    /// Gets or sets the current health points of the hero.
    /// </summary>
    public float Health { get; set; }

    /// <summary>
    /// Gets or sets the maximum health points of the hero.
    /// </summary>
    public float MaxHealth { get; set; }

    /// <summary>
    /// Gets or sets the current mana points of the hero.
    /// </summary>
    public float Mana { get; set; }

    /// <summary>
    /// Gets or sets the maximum mana capacity of the hero.
    /// </summary>
    public float MaxMana { get; set; }

    /// <summary>
    /// Gets or sets the base attack damage of the hero.
    /// </summary>
    public float Damage { get; set; }

    /// <summary>
    /// Gets or sets the armor rating of the hero.
    /// </summary>
    public float Armor { get; set; }

    /// <summary>
    /// Gets or sets the movement speed of the hero.
    /// </summary>
    public float Speed { get; set; }

    /// <summary>
    /// Gets or sets custom arbitrary key-value metadata associated with the hero.
    /// </summary>
    public Dictionary<string, string> CustomData { get; set; } = new();
}

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
