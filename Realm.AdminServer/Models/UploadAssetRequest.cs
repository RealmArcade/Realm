namespace Realm.AdminServer.Models;

public class UploadAssetRequest
{
	public string Hash { get; set; } = "";
	public string Signature { get; set; } = "";
	public string AuthorUsername { get; set; } = "";
	public string PublicKey { get; set; } = "";
	public string FileBase64 { get; set; } = "";
}