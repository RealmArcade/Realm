namespace Realm.Shared.Metadata;

public class AuthorshipKeyEncryption
{
	public string Algorithm { get; set; } = string.Empty;
	public long MemorySizeKibibytes { get; set; }
	public long NumberOfPasses { get; set; }
	public int DegreeOfParallelism { get; set; }
	public string SaltBase64 { get; set; } = string.Empty;
	public string NonceBase64 { get; set; } = string.Empty;
	public string CiphertextBase64 { get; set; } = string.Empty;
}
