namespace Realm.Shared.Distribution;

public class AdminGreenlightEventPayload
{
	public string MapTitle { get; set; } = string.Empty;
	public string? MapVersion { get; set; }

	/// <summary>The creator the override names, whose name lock decides who may publish the title.</summary>
	public string CreatorUsername { get; set; } = string.Empty;

	/// <summary>The key the name resolved to on the server that accepted the override, kept so peers can report drift.</summary>
	public string CreatorPublicKey { get; set; } = string.Empty;

	public string AdminPublicKey { get; set; } = string.Empty;
	public string Signature { get; set; } = string.Empty;
}
