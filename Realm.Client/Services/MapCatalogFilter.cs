using System;
using System.Collections.Generic;
using System.Linq;
using Realm.Client.UI;

namespace Realm.Client.Services;

public static class MapCatalogFilter
{
	private const string AllCategory = "All";
	private const string FeaturedCategory = "Featured";
	private const string TowerDefenseCategory = "TD";
	private const float FeaturedMinimumRating = 4.5f;
	private const int FeaturedMinimumGames = 10;

	public static IEnumerable<MapData> ByCategory(IEnumerable<MapData> maps, string selectedCategory)
	{
		if (string.IsNullOrWhiteSpace(selectedCategory) ||
		    string.Equals(selectedCategory, AllCategory, StringComparison.OrdinalIgnoreCase))
		{
			return maps;
		}

		if (string.Equals(selectedCategory, FeaturedCategory, StringComparison.OrdinalIgnoreCase))
		{
			return maps.Where(m =>
				m.IsGreenlit ||
				(m.TotalReviews > 0 && m.RatingStars >= FeaturedMinimumRating) ||
				m.GamesPlayed >= FeaturedMinimumGames);
		}

		if (string.Equals(selectedCategory, TowerDefenseCategory, StringComparison.OrdinalIgnoreCase))
		{
			return maps.Where(m =>
				MatchesTerm(m, "TD") ||
				MatchesTerm(m, "Tower Defense") ||
				MatchesTerm(m, "TowerDefense") ||
				MatchesTerm(m, "Tower-Defense"));
		}

		return maps.Where(m => MatchesTerm(m, selectedCategory));
	}

	public static IEnumerable<MapData> BySearchQuery(IEnumerable<MapData> maps, string? searchQuery)
	{
		if (string.IsNullOrWhiteSpace(searchQuery))
		{
			return maps;
		}

		string query = searchQuery.Trim();
		return maps.Where(m =>
			(!string.IsNullOrEmpty(m.Title) && m.Title.Contains(query, StringComparison.OrdinalIgnoreCase)) ||
			(!string.IsNullOrEmpty(m.Creator) && m.Creator.Contains(query, StringComparison.OrdinalIgnoreCase)) ||
			(!string.IsNullOrEmpty(m.Description) && m.Description.Contains(query, StringComparison.OrdinalIgnoreCase)) ||
			(!string.IsNullOrEmpty(m.Genre) && m.Genre.Contains(query, StringComparison.OrdinalIgnoreCase)) ||
			(m.Features != null && m.Features.Any(f => !string.IsNullOrEmpty(f) && f.Contains(query, StringComparison.OrdinalIgnoreCase))));
	}

	private static bool MatchesTerm(MapData map, string term)
	{
		if (!string.IsNullOrEmpty(map.Genre) && map.Genre.Contains(term, StringComparison.OrdinalIgnoreCase))
		{
			return true;
		}

		if (map.Features != null && map.Features.Any(f => !string.IsNullOrEmpty(f) && f.Contains(term, StringComparison.OrdinalIgnoreCase)))
		{
			return true;
		}

		if (!string.IsNullOrEmpty(map.Title) && map.Title.Contains(term, StringComparison.OrdinalIgnoreCase))
		{
			return true;
		}

		return !string.IsNullOrEmpty(map.MapId) && map.MapId.Contains(term, StringComparison.OrdinalIgnoreCase);
	}
}
