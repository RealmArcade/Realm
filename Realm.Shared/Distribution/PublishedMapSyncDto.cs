namespace Realm.Shared.Distribution;

public class PublishedMapSyncDto
{
	public string MapId { get; set; } = string.Empty;
	public string MapTitle { get; set; } = string.Empty;
	public string ManifestJson { get; set; } = string.Empty;
	public string OwnerPublicKey { get; set; } = string.Empty;
	public string Signature { get; set; } = string.Empty;
	public List<string> ReferencedHashes { get; set; } = new();
}