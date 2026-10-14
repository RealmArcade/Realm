namespace Realm.Shared.Distribution;

public class AdminGreenlightEventPayload
{
	public string MapTitle { get; set; } = string.Empty;
	public string? MapVersion { get; set; }
	public string AdminPublicKey { get; set; } = string.Empty;
	public string Signature { get; set; } = string.Empty;
}