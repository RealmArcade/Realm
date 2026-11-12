using System.Collections.Generic;
using Realm.AdminServer.Models;

namespace Realm.AdminServer.Helpers
{
	public static class MapAwardsHelper
	{
		public static List<string> GetAwards(MapStats? stats)
		{
			if (stats == null)
			{
				return new List<string>();
			}

			int totalReviews = stats.ReviewsCount;
			int verifiedReviews = stats.VerifiedGoodReviewsCount;
			int playtimeMinutes = (int)stats.TotalPlaytimeMinutes;
			int gamesPlayed = stats.TotalGamesPlayed;

			var awards = new List<string>();
			if (stats.IsGreenlit) awards.Add("res://Assets/UI/victory_flag.webp");
			if (totalReviews >= 5 && stats.AverageRating >= 4.5) awards.Add("res://Assets/UI/gold_coin.webp");
			if (verifiedReviews >= 10 || totalReviews >= 25) awards.Add("res://Assets/UI/battle_shield.webp");
			if (gamesPlayed >= 20 || playtimeMinutes >= 60) awards.Add("res://Assets/UI/battle_axe.webp");

			return awards;
		}
	}
}
