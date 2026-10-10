namespace Realm.AdminServer;

public class MapStats
{
	public double TotalPlaytimeMinutes { get; set; }
	public int TotalGamesPlayed { get; set; }
	public int ReviewsCount { get; set; }
	public int TotalStars { get; set; }
	public int VerifiedGoodReviewsCount { get; set; }
	public bool AdminOverrideGreenlit { get; set; }

	public double AverageRating => ReviewsCount > 0 ? (double)TotalStars / ReviewsCount : 0.0;

	public bool IsGreenlit => AdminOverrideGreenlit || VerifiedGoodReviewsCount >= 100;
}