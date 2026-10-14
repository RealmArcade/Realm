namespace Realm.Shared.Distribution;

public class DiscoveryMapDto
{
	public string MapId { get; set; } = string.Empty;
	public string Title { get; set; } = string.Empty;
	public string Version { get; set; } = "1.0.0";
	public string Creator { get; set; } = "Unknown";
	public string Description { get; set; } = string.Empty;
	public string Genre { get; set; } = "Custom Map";
	public string ThumbnailHash { get; set; } = string.Empty;
	public string ThumbnailUrl { get; set; } = string.Empty;
	public List<string> Screenshots { get; set; } = new();
	public List<string> Features { get; set; } = new();
	public List<string> Tags { get; set; } = new();
	public float RatingStars { get; set; } = 5.0f;
	public int TotalReviews { get; set; }
	public int VerifiedGoodReviews { get; set; }
	public double AverageRating { get; set; } = 5.0;
	public int PlaytimeMinutes { get; set; }
	public int GamesPlayed { get; set; }
	public long TotalSizeBytes { get; set; }
	public string FileSizeFormatted { get; set; } = "0 MB";
	public string EngineVersion { get; set; } = "Godot Realm Engine v1.0";
	public string MaxPlayers { get; set; } = "8 Players";
	public bool IsGreenlit { get; set; } = true;
	public List<string> Awards { get; set; } = new();
}