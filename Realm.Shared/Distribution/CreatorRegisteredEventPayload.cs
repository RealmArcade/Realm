namespace Realm.Shared.Distribution;

public class CreatorRegisteredEventPayload
{
	public string Username { get; set; } = string.Empty;
	public string PublicKey { get; set; } = string.Empty;
	public string Signature { get; set; } = string.Empty;
	public string? DonationLink { get; set; }
	public string? ContactInfo { get; set; }
	public string? AdminBypassToken { get; set; }
	public DateTime? RegisteredAt { get; set; }
}