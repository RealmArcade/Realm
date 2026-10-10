namespace Realm.Client.Services;

public class MigrationResult
{
	public bool Success { get; set; }
	public string? ErrorMessage { get; set; }
	public string FromVersion { get; set; } = string.Empty;
	public string ToVersion { get; set; } = string.Empty;
}