namespace Realm.Shared.Distribution;

public class MapMetricReportEventPayload
{
	public string MapTitle { get; set; } = string.Empty;
	public string MapVersion { get; set; } = "1.0";
	public long AccountId { get; set; }

	public string DisplayName { get; set; } = string.Empty;

	public double PlaytimeMinutes { get; set; }
	public int Stars { get; set; }
	public bool IsCompleteGame { get; set; }
	public string? AuthorPublicKey { get; set; }
	public DateTime TimestampUtc { get; set; } = DateTime.UtcNow;
}