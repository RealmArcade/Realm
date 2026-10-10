namespace Realm.Shared.Distribution;

public class RemoveMapMaintainerRequest
{
	public string MapTitle { get; set; } = string.Empty;
	public string MaintainerPublicKey { get; set; } = string.Empty;
	public string RequesterPublicKey { get; set; } = string.Empty;
	public string Signature { get; set; } = string.Empty;
}