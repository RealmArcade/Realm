namespace Realm.Shared.Distribution;

public class MapMaintainersUpdatedEventPayload
{
	public string MapTitle { get; set; } = string.Empty;
	public string Action { get; set; } = "add";
	public string MaintainerPublicKey { get; set; } = string.Empty;
	public string? MaintainerUsername { get; set; }
	public string RequesterPublicKey { get; set; } = string.Empty;
	public string Signature { get; set; } = string.Empty;
	public DateTime TimestampUtc { get; set; } = DateTime.UtcNow;
}