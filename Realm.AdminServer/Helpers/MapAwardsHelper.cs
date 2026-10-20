using System.Collections.Generic;
using Realm.AdminServer.Models;

namespace Realm.AdminServer.Helpers
{
    public static class MapAwardsHelper
    {
        public static List<string> GetAwards(MapStats? stats)
        {
            float ratingStars = stats != null && stats.AverageRating > 0 ? (float)stats.AverageRating : 4.5f;
            int totalReviews = stats != null ? stats.ReviewsCount : 0;
            int verifiedReviews = stats != null ? stats.VerifiedGoodReviewsCount : 0;
            int playtimeMinutes = stats != null ? (int)stats.TotalPlaytimeMinutes : 0;
            int gamesPlayed = stats != null ? stats.TotalGamesPlayed : 0;
            bool isGreenlit = stats != null && stats.IsGreenlit;

            var awards = new List<string>();
            if (isGreenlit) awards.Add("res://Assets/UI/victory_flag.webp");
            if (ratingStars >= 4.5f) awards.Add("res://Assets/UI/gold_coin.webp");
            if (verifiedReviews >= 5 || totalReviews >= 20) awards.Add("res://Assets/UI/battle_shield.webp");
            if (gamesPlayed >= 10 || playtimeMinutes >= 30) awards.Add("res://Assets/UI/battle_axe.webp");
            if (awards.Count == 0) awards.Add("res://Assets/UI/gold_coin.webp");

            return awards;
        }
    }
}
