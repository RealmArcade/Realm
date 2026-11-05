namespace Realm.Shared.Distribution;

public class ServersConfig
{
	public string AccountAuthorityUrl { get; set; } = string.Empty;

	public List<string> AdminPublicKeys { get; set; } = new();

	public List<string> AdminPublicKey
	{
		get => AdminPublicKeys;
		set => AdminPublicKeys = value;
	}

	public List<string> Servers { get; set; } = new();
}