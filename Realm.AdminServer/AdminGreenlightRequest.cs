namespace Realm.AdminServer;

public class AdminGreenlightRequest
{
	public string MapTitle { get; set; } = "";
	public string? MapVersion { get; set; }

	/// <summary>
	///     The creator the greenlight belongs to. Their name is resolved through the name locks to the key that may
	///     publish the title, so the override cannot be used by somebody else who hears the title was greenlit.
	/// </summary>
	public string CreatorUsername { get; set; } = "";

	public string AdminPublicKey { get; set; } = "";
	public string Signature { get; set; } = "";
}
