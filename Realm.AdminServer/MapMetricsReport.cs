namespace Realm.AdminServer;

public class MapMetricsReport
{
	public string MapTitle { get; set; } = "";
	public string MapVersion { get; set; } = "";
	public double PlaytimeMinutes { get; set; }
	public int Stars { get; set; }
	public bool IsCompleteGame { get; set; }
	public string? PlayerId { get; set; }
	public string? AuthToken { get; set; }
	public string? AuthProvider { get; set; }
	public string? AuthorPublicKey { get; set; }
}