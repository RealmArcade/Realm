namespace Realm.Shared.Distribution;

public class PublishMapRequest
{
	public string ManifestJson { get; set; } = string.Empty;
	public string MapJson { get; set; } = string.Empty;
	public List<string> ReferencedHashes { get; set; } = new();
	public string Signature { get; set; } = string.Empty;
	public string PublicKey { get; set; } = string.Empty;
}