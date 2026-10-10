namespace Realm.Shared.Distribution;

public class PublishMapFinalizeRequest
{
	public string SessionId { get; set; } = string.Empty;
	public string MapTitle { get; set; } = string.Empty;
	public string MapVersion { get; set; } = "1.0";
	public string PublicKey { get; set; } = string.Empty;
	public string Signature { get; set; } = string.Empty;
}