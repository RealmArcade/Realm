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

	public static void GetMapDiscoveryStatsAndAwards(MapStats? stats, bool isGreenlit, out float ratingStars, out int totalReviews, out int verifiedReviews, out int playtimeMinutes, out int gamesPlayed, out List<string> awards)
	{
		ratingStars = stats != null && stats.AverageRating > 0 ? (float)stats.AverageRating : 4.5f;
		totalReviews = stats != null ? stats.ReviewsCount : 0;
		verifiedReviews = stats != null ? stats.VerifiedGoodReviewsCount : 0;
		playtimeMinutes = stats != null ? (int)stats.TotalPlaytimeMinutes : 0;
		gamesPlayed = stats != null ? stats.TotalGamesPlayed : 0;

		awards = new List<string>();
		if (isGreenlit) awards.Add("res://Assets/UI/victory_flag.png");
		if (ratingStars >= 4.5f) awards.Add("res://Assets/UI/gold_coin.png");
		if (verifiedReviews >= 5 || totalReviews >= 20) awards.Add("res://Assets/UI/battle_shield.png");
		if (gamesPlayed >= 10 || playtimeMinutes >= 30) awards.Add("res://Assets/UI/battle_axe.png");
		if (awards.Count == 0) awards.Add("res://Assets/UI/gold_coin.png");
	}

	public static void UpsertMapStats(DataStoreService db, MapStats stats, string mapTitle, string compositeKey, string authorCompositeKey, string? authorPublicKey)
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