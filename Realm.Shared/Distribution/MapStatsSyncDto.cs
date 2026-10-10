namespace Realm.Shared.Distribution;

public class MapStatsSyncDto
{
	public string MapId { get; set; } = string.Empty;
	public int TotalPlaytimeMinutes { get; set; }
	public int TotalGamesPlayed { get; set; }
	public int TotalReviewsCount { get; set; }
	public int VerifiedGoodReviewsCount { get; set; }
	public double AverageRating { get; set; }
	public bool AdminOverrideGreenlit { get; set; }
}