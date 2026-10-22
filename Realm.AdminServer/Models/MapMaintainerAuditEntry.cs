namespace Realm.AdminServer.Models;

public class MapMaintainerAuditEntry
{
	public string MapTitle { get; set; } = string.Empty;
	public string Action { get; set; } = string.Empty;
	public string MaintainerPublicKey { get; set; } = string.Empty;
	public string? MaintainerUsername { get; set; }
	public string RequesterPublicKey { get; set; } = string.Empty;
	public DateTime TimestampUtc { get; set; } = DateTime.UtcNow;
}
