using System;
using System.Collections.Generic;
using System.Text.Json;

namespace Realm.MapAPI;

/// <summary>
/// Provides convenience extension methods for map authors using the <see cref="IGameAPI"/> interface.
/// </summary>
public static class GameAPIExtensions
{
    private static readonly JsonSerializerOptions s_jsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    /// <summary>
    /// Retrieves the preferred language code of the primary local player.
    /// </summary>
    /// <param name="api">The game API instance.</param>
    /// <returns>ISO language code string (e.g. "en", "es").</returns>
    public static string GetPlayerLanguage(this IGameAPI api)
    {
        return api.GetPlayerLanguage(0);
    }

    /// <summary>
    /// Translates a localization key for the current local player with English fallback.
    /// </summary>
    /// <param name="api">The game API instance.</param>
    /// <param name="key">Localization translation key.</param>
    /// <returns>Translated text string.</returns>
    public static string Translate(this IGameAPI api, string key)
    {
        return api.Translate(key, -1);
    }

    /// <summary>
    /// Serializes and saves arbitrary data into a signed, human-readable JSON envelope on disk.
    /// </summary>
    /// <typeparam name="T">The type of data to persist.</typeparam>
    /// <param name="api">The game API instance.</param>
    /// <param name="fileName">The target file name (e.g., "save0.rsav" or "profile.json").</param>
    /// <param name="data">The data object to serialize.</param>
    /// <param name="dataVersion">The schema version number of this save data.</param>
    /// <param name="mapName">The map name identifier, or empty to use a default.</param>
    /// <param name="playerName">The player name bound to the save, or null to use the primary player name.</param>
    public static void SaveData<T>(
        this IGameAPI api,
        string fileName,
        T data,
        int dataVersion = 1,
        string mapName = "",
        string? playerName = null)
    {
        string resolvedPlayerName = !string.IsNullOrWhiteSpace(playerName)
            ? playerName
            : (api.PlayerCount > 0 ? api.GetPlayerName(0) : "Player0");

        if (string.IsNullOrWhiteSpace(resolvedPlayerName))
        {
            resolvedPlayerName = "Player0";
        }

        string payloadJson = JsonSerializer.Serialize(data, s_jsonOptions);
        string signature = SaveSignatureHelper.ComputeSignature(mapName, resolvedPlayerName, dataVersion, payloadJson);

        var envelope = new SaveEnvelope<T>
        {
            DataVersion = dataVersion,
            MapName = mapName,
            PlayerName = resolvedPlayerName,
            TimestampUtc = DateTime.UtcNow.ToString("o"),
            Signature = signature,
            Payload = data
        };

        string fullJson = JsonSerializer.Serialize(envelope, s_jsonOptions);
        api.WriteSavedData(fileName, fullJson);
    }

    /// <summary>
    /// Attempts to load, verify, and deserialize saved data from disk, applying registered schema migrations if the file was saved at an older version.
    /// </summary>
    /// <typeparam name="T">The type of data to deserialize into.</typeparam>
    /// <param name="api">The game API instance.</param>
    /// <param name="fileName">The target file name to read.</param>
    /// <param name="result">When this method returns, contains the deserialized data if successful, or default if failed.</param>
    /// <param name="targetVersion">The expected schema version of the target type.</param>
    /// <param name="migrations">Optional migration registry to upgrade older save versions sequentially.</param>
    /// <param name="mapName">The expected map name, or empty to match any. When reading cross-map data from a campaign scenario, specify the source map name.</param>
    /// <param name="playerName">The expected player name, or null to use the primary player name.</param>
    /// <returns><see langword="true"/> if the save data was successfully loaded, verified, and deserialized; otherwise, <see langword="false"/>.</returns>
    public static bool TryLoadData<T>(
        this IGameAPI api,
        string fileName,
        out T? result,
        int targetVersion = 1,
        SaveMigrationRegistry? migrations = null,
        string mapName = "",
        string? playerName = null)
    {
        result = default;

        string rawContent = api.ReadSavedData(fileName, mapName);
        if (string.IsNullOrWhiteSpace(rawContent))
        {
            return false;
        }

        try
        {
            result = TryDeserializeData<T>(rawContent, targetVersion, migrations, mapName, playerName);
            return result != null;
        }
        catch
        {
            result = default;
            return false;
        }
    }

    private static T? TryDeserializeData<T>(
        string rawContent,
        int targetVersion,
        SaveMigrationRegistry? migrations,
        string mapName,
        string? playerName)
    {
        using var doc = JsonDocument.Parse(rawContent);
        var root = doc.RootElement;

        if (!root.TryGetProperty("Payload", out var payloadElement) &&
            !root.TryGetProperty("payload", out payloadElement))
        {
            return JsonSerializer.Deserialize<T>(rawContent, s_jsonOptions);
        }

        int fileVersion = GetIntProperty(root, "DataVersion", "dataVersion", 1);
        string filePlayer = GetStringProperty(root, "PlayerName", "playerName");
        string fileMap = GetStringProperty(root, "MapName", "mapName");
        string fileSignature = GetStringProperty(root, "Signature", "signature");

        string payloadJson = payloadElement.GetRawText();

        string checkMap = !string.IsNullOrEmpty(mapName) ? mapName : fileMap;
        string checkPlayer = !string.IsNullOrEmpty(playerName) ? playerName : filePlayer;

        if (!string.IsNullOrEmpty(fileSignature) &&
            !SaveSignatureHelper.VerifySignature(checkMap, checkPlayer, fileVersion, payloadJson, fileSignature))
        {
            return default;
        }

        if (fileVersion < targetVersion && migrations != null)
        {
            payloadJson = migrations.ApplyMigrations(payloadJson, fileVersion, targetVersion);
        }

        return JsonSerializer.Deserialize<T>(payloadJson, s_jsonOptions);
    }

    private static int GetIntProperty(JsonElement root, string key1, string key2, int defaultValue)
    {
        if (root.TryGetProperty(key1, out var element) ||
            root.TryGetProperty(key2, out element))
        {
            return element.GetInt32();
        }
        return defaultValue;
    }

    private static string GetStringProperty(JsonElement root, string key1, string key2)
    {
        if (root.TryGetProperty(key1, out var element) ||
            root.TryGetProperty(key2, out element))
        {
            return element.GetString() ?? "";
        }
        return "";
    }

    /// <summary>
    /// Loads and deserializes saved data from disk, returning default if not found or if signature verification fails.
    /// </summary>
    /// <typeparam name="T">The type of data to deserialize into.</typeparam>
    /// <param name="api">The game API instance.</param>
    /// <param name="fileName">The target file name to read.</param>
    /// <param name="targetVersion">The expected schema version of the target type.</param>
    /// <param name="migrations">Optional migration registry to upgrade older save versions sequentially.</param>
    /// <param name="mapName">The expected map name, or empty to match any. When reading cross-map data from a campaign scenario, specify the source map name.</param>
    /// <param name="playerName">The expected player name, or null to use the primary player name.</param>
    /// <returns>The deserialized data instance, or default if loading fails.</returns>
    public static T? LoadData<T>(
        this IGameAPI api,
        string fileName,
        int targetVersion = 1,
        SaveMigrationRegistry? migrations = null,
        string mapName = "",
        string? playerName = null)
    {
        TryLoadData<T>(api, fileName, out var result, targetVersion, migrations, mapName, playerName);
        return result;
    }

    /// <summary>
    /// Saves an <see cref="ArcadeProfileData"/> record into a signed, human-readable file.
    /// </summary>
    /// <param name="api">The game API instance.</param>
    /// <param name="fileName">The target file name (e.g., "profile.rsav").</param>
    /// <param name="profile">The arcade profile instance to persist.</param>
    /// <param name="dataVersion">The schema version number.</param>
    /// <param name="mapName">The map name identifier, or empty to use a default.</param>
    public static void SaveArcadeProfile(
        this IGameAPI api,
        string fileName,
        ArcadeProfileData profile,
        int dataVersion = 1,
        string mapName = "")
    {
        SaveData(api, fileName, profile, dataVersion, mapName, profile.PlayerName);
    }

    /// <summary>
    /// Attempts to load and verify an <see cref="ArcadeProfileData"/> record from disk.
    /// </summary>
    /// <param name="api">The game API instance.</param>
    /// <param name="fileName">The target file name to read.</param>
    /// <param name="profile">When this method returns, contains the loaded arcade profile if successful.</param>
    /// <param name="targetVersion">The expected schema version.</param>
    /// <param name="migrations">Optional migration registry to upgrade older profile schemas.</param>
    /// <param name="mapName">The expected map name, or empty to match any. When reading cross-map data from a campaign scenario, specify the source map name.</param>
    /// <param name="playerName">The expected player name, or null to match the active player.</param>
    /// <returns><see langword="true"/> if the profile was successfully loaded and verified; otherwise, <see langword="false"/>.</returns>
    public static bool TryLoadArcadeProfile(
        this IGameAPI api,
        string fileName,
        out ArcadeProfileData? profile,
        int targetVersion = 1,
        SaveMigrationRegistry? migrations = null,
        string mapName = "",
        string? playerName = null)
    {
        return TryLoadData(api, fileName, out profile, targetVersion, migrations, mapName, playerName);
    }

    /// <summary>
    /// Loads an <see cref="ArcadeProfileData"/> record from disk, returning null if not found or verification fails.
    /// </summary>
    /// <param name="api">The game API instance.</param>
    /// <param name="fileName">The target file name to read.</param>
    /// <param name="targetVersion">The expected schema version.</param>
    /// <param name="migrations">Optional migration registry to upgrade older profile schemas.</param>
    /// <param name="mapName">The expected map name, or empty to match any. When reading cross-map data from a campaign scenario, specify the source map name.</param>
    /// <param name="playerName">The expected player name, or null to match the active player.</param>
    /// <returns>The loaded arcade profile instance, or null if loading fails.</returns>
    public static ArcadeProfileData? LoadArcadeProfile(
        this IGameAPI api,
        string fileName,
        int targetVersion = 1,
        SaveMigrationRegistry? migrations = null,
        string mapName = "",
        string? playerName = null)
    {
        TryLoadArcadeProfile(api, fileName, out var profile, targetVersion, migrations, mapName, playerName);
        return profile;
    }

    /// <summary>
    /// Captures the persistent progression and combat statistics of an active unit into a <see cref="HeroSaveData"/> record.
    /// </summary>
    /// <param name="unit">The unit to snapshot.</param>
    /// <param name="experience">Optional experience value to assign.</param>
    /// <param name="skillPoints">Optional unspent skill points to assign.</param>
    /// <returns>A new <see cref="HeroSaveData"/> containing the unit's current state.</returns>
    public static HeroSaveData SnapshotHero(this IUnit unit, float experience = 0f, int skillPoints = 0)
    {
        var hero = new HeroSaveData
        {
            HeroTypeId = unit.UnitId,
            Name = unit.Name,
            Level = unit.Level > 0 ? unit.Level : 1,
            Experience = unit.Experience > 0 ? unit.Experience : experience,
            SkillPoints = skillPoints,
            Health = unit.Health,
            MaxHealth = unit.MaxHealth,
            Mana = unit.Mana,
            MaxMana = unit.MaxMana,
            Damage = unit.Damage,
            Armor = unit.Armor,
            Speed = unit.Speed
        };

        return hero;
    }

    /// <summary>
    /// Restores persistent hero stats, level, experience, and custom attributes onto an active unit instance.
    /// </summary>
    /// <param name="unit">The target unit to update.</param>
    /// <param name="heroData">The saved hero progression data to apply.</param>
    public static void RestoreHero(this IUnit unit, HeroSaveData heroData)
    {
        if (heroData == null) return;

        RestoreHeroCoreStats(unit, heroData);
        RestoreHeroCombatStats(unit, heroData);
        RestoreHeroCustomData(unit, heroData);
    }

    private static void RestoreHeroCoreStats(IUnit unit, HeroSaveData heroData)
    {
        if (!string.IsNullOrWhiteSpace(heroData.Name))
            unit.Name = heroData.Name;

        if (heroData.Level > 0)
            unit.Level = heroData.Level;

        if (heroData.Experience > 0)
            unit.Experience = heroData.Experience;

        if (heroData.MaxHealth > 0)
            unit.MaxHealth = heroData.MaxHealth;

        if (heroData.Health > 0)
            unit.Health = heroData.Health;
    }

    private static void RestoreHeroCombatStats(IUnit unit, HeroSaveData heroData)
    {
        if (heroData.MaxMana > 0)
            unit.MaxMana = heroData.MaxMana;

        if (heroData.Mana > 0)
            unit.Mana = heroData.Mana;

        if (heroData.Damage > 0)
            unit.Damage = heroData.Damage;

        if (heroData.Armor > 0)
            unit.Armor = heroData.Armor;

        if (heroData.Speed > 0)
            unit.Speed = heroData.Speed;
    }

    private static void RestoreHeroCustomData(IUnit unit, HeroSaveData heroData)
    {
        if (heroData.CustomData == null) return;

        foreach (var kvp in heroData.CustomData)
        {
            unit.SetCustomData(kvp.Key, kvp.Value);
        }
    }

    /// <summary>
    /// Captures all items currently held in the unit's inventory into a list of <see cref="InventoryItemSaveData"/>.
    /// </summary>
    /// <param name="unit">The unit whose inventory will be snapshotted.</param>
    /// <returns>A list of persistent inventory item records.</returns>
    public static List<InventoryItemSaveData> SnapshotInventory(this IUnit unit)
    {
        var result = new List<InventoryItemSaveData>();
        int slotIndex = 0;

        foreach (string itemId in unit.GetItems())
        {
            result.Add(new InventoryItemSaveData
            {
                ItemId = itemId,
                SlotIndex = slotIndex++,
                Charges = 1
            });
        }

        return result;
    }

    /// <summary>
    /// Populates a unit's inventory from a collection of <see cref="InventoryItemSaveData"/> records.
    /// </summary>
    /// <param name="unit">The unit to receive items.</param>
    /// <param name="items">The collection of saved inventory items to add.</param>
    public static void RestoreInventory(this IUnit unit, IEnumerable<InventoryItemSaveData> items)
    {
        if (items == null)
        {
            return;
        }

        foreach (var item in items)
        {
            if (!string.IsNullOrWhiteSpace(item.ItemId))
            {
                unit.AddItem(item.ItemId);
            }
        }
    }
}
