namespace Realm.AdminServer;

/// <summary>
///     A metric report from a finished or abandoned match. The reporting identity is the session the client holds; a report
///     without a valid session is attributed to the anonymous account, never to a name the client typed.
/// </summary>
public class MapMetricsReport
{
	public string MapTitle { get; set; } = "";
	public string MapVersion { get; set; } = "";
	public double PlaytimeMinutes { get; set; }
	public int Stars { get; set; }
	public bool IsCompleteGame { get; set; }
	public string? SessionToken { get; set; }
	public string? AuthorPublicKey { get; set; }
}