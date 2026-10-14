namespace Realm.Shared.Distribution;

public class MapPublishedEventPayload
{
	public string MapTitle { get; set; } = string.Empty;
	public string MapVersion { get; set; } = "1.0";
	public string ManifestJson { get; set; } = string.Empty;
	public List<string> ReferencedHashes { get; set; } = new();
	public string PublicKey { get; set; } = string.Empty;
	public string Signature { get; set; } = string.Empty;
}