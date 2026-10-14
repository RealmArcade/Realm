namespace Realm.AdminServer;

public class RegisterCreatorRequest
{
	public string Username { get; set; } = "";
	public string PublicKey { get; set; } = "";
	public string Signature { get; set; } = "";
	public string? DonationLink { get; set; }
	public string? ContactInfo { get; set; }
}