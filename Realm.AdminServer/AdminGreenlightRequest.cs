namespace Realm.AdminServer;

public class AdminGreenlightRequest
{
	public string MapTitle { get; set; } = "";
	public string? MapVersion { get; set; }
	public string AdminPublicKey { get; set; } = "";
	public string Signature { get; set; } = "";
}