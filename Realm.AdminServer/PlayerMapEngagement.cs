namespace Realm.AdminServer;

public class PlayerMapEngagement
{
	public string PlayerId { get; set; } = "";
	public double TotalPlaytimeMinutes { get; set; }
	public int GamesPlayed { get; set; }
	public int? SubmittedRating { get; set; }
	public bool IsVerifiedAccount { get; set; }
	public bool IsEligibleReviewer => IsVerifiedAccount && TotalPlaytimeMinutes >= 30.0 && GamesPlayed >= 3;
	public bool IsVerifiedGoodReview => IsEligibleReviewer && SubmittedRating.HasValue && SubmittedRating.Value >= 3;
}