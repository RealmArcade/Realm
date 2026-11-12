using System;
using System.Collections.Generic;
using System.Linq;
using Realm.Client.UI;

namespace Realm.Client.Services;

/// <summary>
///     Which maps a discovery grid shows for the category tab and the search box. It lives here because "Featured"
///     having a rating floor is a catalogue rule, not a layout decision.
/// </summary>
public static class MapCatalogFilter
{
	private const string AllCategory = "All";
	private const string FeaturedCategory = "Featured";
	private const float FeaturedMinimumRating = 4.5f;

	public static IEnumerable<MapData> ByCategory(IEnumerable<MapData> maps, string selectedCategory)
	{
		if (selectedCategory == AllCategory) return maps;

		if (selectedCategory == FeaturedCategory)
		{
			return maps.Where(m => m.RatingStars >= FeaturedMinimumRating);
		}

		return maps.Where(m => m.Genre.Contains(selectedCategory) || m.Features.Contains(selectedCategory) || m.MapId.Contains(selectedCategory));
	}

	public static IEnumerable<MapData> BySearchQuery(IEnumerable<MapData> maps, string? searchQuery)
	{
		if (string.IsNullOrEmpty(searchQuery)) return maps;

		return maps.Where(m => m.Title.Contains(searchQuery, StringComparison.OrdinalIgnoreCase) ||
		                       m.Creator.Contains(searchQuery, StringComparison.OrdinalIgnoreCase) ||
		                       m.Description.Contains(searchQuery, StringComparison.OrdinalIgnoreCase));
	}
}
