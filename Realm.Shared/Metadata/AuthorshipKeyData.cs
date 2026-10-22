namespace Realm.Shared.Metadata;

public class AuthorshipKeyData
{
	public string UserName { get; set; } = string.Empty;
	public string PublicKey { get; set; } = string.Empty;
	public string PrivateKey { get; set; } = string.Empty;
	public AuthorshipKeyEncryption? EncryptedPrivateKey { get; set; }
}