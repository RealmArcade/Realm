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

		var stats = FindExactMatch(dataStore, keys) ?? FindLowerMatch(dataStore, keys) ?? FindCaseInsensitiveMatch(dataStore, keys);
		if (stats == null)
		{
			return null;
		}

		ApplyTitleAdminOverride(dataStore, stats, trimmedTitle);
		return stats;
	}

	private static void ApplyTitleAdminOverride(DataStoreService dataStore, MapStats stats, string trimmedTitle)
	{
		if (stats.AdminOverrideGreenlit)
		{
			return;
		}

		var titleStats = dataStore.Get<MapStats>("map_stats", trimmedTitle);
		if (titleStats != null && titleStats.AdminOverrideGreenlit)
		{
			stats.AdminOverrideGreenlit = true;
			return;
		}

		var lowercaseTitleStats = dataStore.Get<MapStats>("map_stats", trimmedTitle.ToLowerInvariant());
		if (lowercaseTitleStats != null && lowercaseTitleStats.AdminOverrideGreenlit)
		{
			stats.AdminOverrideGreenlit = true;
		}
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

        public static void UpsertMapStats(
            DataStoreService db, 
            string mapTitle, 
            string? authorPublicKey, 
            string authorCompositeKey, 
            string compositeKey, 
            MapStats stats)
        {
            db.Upsert("map_stats", authorCompositeKey, stats);
            db.Upsert("map_stats", authorCompositeKey.ToLowerInvariant(), stats);

            if (!string.IsNullOrEmpty(authorPublicKey))
            {
                db.Upsert("map_stats", $"{mapTitle}_{authorPublicKey}", stats);
                db.Upsert("map_stats", $"{mapTitle}_{authorPublicKey}".ToLowerInvariant(), stats);
            }

            if (stats.IsGreenlit)
            {
                db.Upsert("map_stats", compositeKey, stats);
                db.Upsert("map_stats", mapTitle, stats);
                db.Upsert("map_stats", compositeKey.ToLowerInvariant(), stats);
                db.Upsert("map_stats", mapTitle.ToLowerInvariant(), stats);
            }
        }


    }

