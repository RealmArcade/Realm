using System;
using System.Collections.Generic;
namespace Realm.AdminServer.Services;

public static class MapStatsHelper
{
    public static MapStats? GetStats(DataStoreService dataStore, string mapTitle, string? mapVersion = null, string? authorPublicKey = null)
    {
        if (string.IsNullOrWhiteSpace(mapTitle))
        {
            return null;
        }

        string trimmedTitle = mapTitle.Trim();
        string trimmedVersion = string.IsNullOrWhiteSpace(mapVersion) ? "1.0" : mapVersion.Trim();
        string compositeKey = $"{trimmedTitle}_{trimmedVersion}";
        string authorCompositeKey = !string.IsNullOrEmpty(authorPublicKey) ? $"{trimmedTitle}_{trimmedVersion}_{authorPublicKey.Trim()}" : compositeKey;
        string authorKey = !string.IsNullOrEmpty(authorPublicKey) ? $"{trimmedTitle}_{authorPublicKey.Trim()}" : trimmedTitle;

        var keys = BuildKeys(authorCompositeKey, authorKey, compositeKey, trimmedTitle, authorPublicKey);

        var exactStats = FindExactMatch(dataStore, keys);
        if (exactStats != null)
        {
            return exactStats;
        }

        var lowerStats = FindLowerMatch(dataStore, keys);
        if (lowerStats != null)
        {
            return lowerStats;
        }

        return FindCaseInsensitiveMatch(dataStore, keys);
    }

    private static string[] BuildKeys(string authorCompositeKey, string authorKey, string compositeKey, string trimmedTitle, string? authorPublicKey)
    {
        if (!string.IsNullOrEmpty(authorPublicKey))
        {
            return new[] { authorCompositeKey, authorKey, compositeKey, trimmedTitle };
        }
        return new[] { compositeKey, trimmedTitle };
    }

    private static MapStats? FindExactMatch(DataStoreService dataStore, string[] keys)
    {
        foreach (var key in keys)
        {
            var stats = dataStore.Get<MapStats>("map_stats", key);
            if (stats != null)
            {
                return stats;
            }
        }
        return null;
    }

    private static MapStats? FindLowerMatch(DataStoreService dataStore, string[] keys)
    {
        foreach (var key in keys)
        {
            var stats = dataStore.Get<MapStats>("map_stats", key.ToLowerInvariant());
            if (stats != null)
            {
                return stats;
            }
        }
        return null;
    }

    private static MapStats? FindCaseInsensitiveMatch(DataStoreService dataStore, string[] keys)
    {
        var allStats = dataStore.GetAllWithKeys<MapStats>("map_stats");
        foreach (var pair in allStats)
        {
            if (IsKeyMatch(pair.Key, keys))
            {
                return pair.Value;
            }
        }
        return null;
    }

    private static bool IsKeyMatch(string pairKey, string[] keys)
    {
        foreach (var key in keys)
        {
            if (string.Equals(pairKey, key, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }
        return false;
    }

    public static bool IsMapGreenlit(DataStoreService dataStore, string mapTitle, string? mapVersion = null, string? authorPublicKey = null)
    {
        var stats = GetStats(dataStore, mapTitle, mapVersion, authorPublicKey);
        return stats != null && stats.IsGreenlit;
    }
}
