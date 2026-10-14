namespace Realm.Shared.Distribution;

public class CreatorSyncDto
{
	public string Username { get; set; } = string.Empty;
	public string PublicKey { get; set; } = string.Empty;
	public string Signature { get; set; } = string.Empty;
	public string DonationLink { get; set; } = string.Empty;
	public string ContactInfo { get; set; } = string.Empty;
	public string? AdminBypassToken { get; set; }
	public DateTime? RegisteredAt { get; set; }
}