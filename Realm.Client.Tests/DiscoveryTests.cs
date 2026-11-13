using System.Collections.Generic;
using System.Linq;
using GdUnit4;
using Realm.Client.Services;
using Realm.Client.UI;
using Realm.Shared.Distribution;

namespace Realm.Client.Tests;

[TestSuite]
public partial class DiscoveryTests
{
	[TestCase]
	public void MapDataFromDtoDoesNotInventFakeDefaults()
	{
		var dto = new DiscoveryMapDto
		{
			MapId = "NewArena_1.0.0",
			Title = "NewArena",
			Version = "1.0.0",
			Creator = "",
			Description = "",
			Genre = "Melee",
			TotalReviews = 0,
			GamesPlayed = 0,
			PlaytimeMinutes = 0,
			TotalSizeBytes = 1024 * 512,
			FileSizeFormatted = "512 KB"
		};

		MapData data = MapData.FromDto(dto);

		Assertions.AssertThat(data.RatingStars).IsEqual(0f);
		Assertions.AssertThat(data.TotalReviews).IsEqual(0);
		Assertions.AssertThat(data.AvgRating).IsEqual(string.Empty);
		Assertions.AssertThat(data.CompletionRate).IsEqual(string.Empty);
		Assertions.AssertThat(data.Screenshots.Length).IsEqual(0);
		Assertions.AssertThat(data.ScreenshotHashes.Length).IsEqual(0);
		Assertions.AssertThat(data.Features.Length).IsEqual(0);
		Assertions.AssertThat(data.Awards.Length).IsEqual(0);
		Assertions.AssertThat(data.Creator).IsEqual("Unknown");
		Assertions.AssertThat(data.IsGreenlit).IsFalse();
	}

	[TestCase]
	public void MapDataFromDtoPreservesRealStatsAndMediaHashes()
	{
		var dto = new DiscoveryMapDto
		{
			MapId = "ChaosArena_2.0.0",
			Title = "ChaosArena",
			Version = "2.0.0",
			Creator = "Realm",
			Description = "Arena battleground",
			Genre = "Tower Defense",
			ThumbnailHash = "thumbhash.webp",
			Screenshots = new List<string> { "screen1.webp", "screen2.webp" },
			Tags = new List<string> { "Tower Defense", "Custom Units" },
			RatingStars = 4.6f,
			AverageRating = 4.6,
			TotalReviews = 15,
			VerifiedGoodReviews = 12,
			GamesPlayed = 42,
			PlaytimeMinutes = 420,
			IsGreenlit = true,
			Awards = new List<string> { "res://Assets/UI/victory_flag.webp" }
		};

		MapData data = MapData.FromDto(dto);

		Assertions.AssertThat(data.RatingStars).IsEqual(4.6f);
		Assertions.AssertThat(data.TotalReviews).IsEqual(15);
		Assertions.AssertThat(data.VerifiedGoodReviews).IsEqual(12);
		Assertions.AssertThat(data.GamesPlayed).IsEqual(42);
		Assertions.AssertThat(data.AvgPlaytime).IsEqual("10 min");
		Assertions.AssertThat(data.ThumbnailHash).IsEqual("thumbhash.webp");
		Assertions.AssertThat(data.ScreenshotHashes.Length).IsEqual(2);
		Assertions.AssertThat(data.Features.Length).IsEqual(2);
		Assertions.AssertThat(data.IsGreenlit).IsTrue();
		Assertions.AssertThat(data.Awards.Length).IsEqual(1);
	}

	[TestCase]
	public void MapCatalogFilterHandlesTdAliasesFeaturedAndCaseInsensitiveCategories()
	{
		var maps = new[]
		{
			new MapData
			{
				MapId = "TdOne_1.0",
				Title = "Fortress Defense",
				Creator = "Alice",
				Description = "Defend the gates",
				Genre = "Tower Defense",
				Features = new[] { "Tower Defense", "Boss Waves" },
				RatingStars = 0f,
				TotalReviews = 0,
				IsGreenlit = false,
				GamesPlayed = 2
			},
			new MapData
			{
				MapId = "MeleeOne_1.0",
				Title = "Highland Clash",
				Creator = "Bob",
				Description = "Classic 1v1",
				Genre = "melee",
				Features = new[] { "Competitive" },
				RatingStars = 4.8f,
				TotalReviews = 10,
				IsGreenlit = true,
				GamesPlayed = 35
			}
		};

		var tdResults = MapCatalogFilter.ByCategory(maps, "TD").ToList();
		Assertions.AssertThat(tdResults.Count).IsEqual(1);
		Assertions.AssertThat(tdResults[0].Title).IsEqual("Fortress Defense");

		var meleeResults = MapCatalogFilter.ByCategory(maps, "Melee").ToList();
		Assertions.AssertThat(meleeResults.Count).IsEqual(1);
		Assertions.AssertThat(meleeResults[0].Title).IsEqual("Highland Clash");

		var featuredResults = MapCatalogFilter.ByCategory(maps, "Featured").ToList();
		Assertions.AssertThat(featuredResults.Count).IsEqual(1);
		Assertions.AssertThat(featuredResults[0].Title).IsEqual("Highland Clash");

		var queryResults = MapCatalogFilter.BySearchQuery(maps, "boss waves").ToList();
		Assertions.AssertThat(queryResults.Count).IsEqual(1);
		Assertions.AssertThat(queryResults[0].Title).IsEqual("Fortress Defense");
	}
}
