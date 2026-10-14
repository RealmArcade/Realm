namespace Realm.Shared.Distribution;

public class AddMapMaintainerRequest
{
	public string MapTitle { get; set; } = string.Empty;
	public string MaintainerPublicKey { get; set; } = string.Empty;
	public string? MaintainerUsername { get; set; }
	public string RequesterPublicKey { get; set; } = string.Empty;
	public string Signature { get; set; } = string.Empty;
}