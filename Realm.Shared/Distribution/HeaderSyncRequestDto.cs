namespace Realm.Shared.Distribution;

public class HeaderSyncRequestDto
{
	public string Blake3Hash { get; set; } = string.Empty;
	public string? MetadataHeadersJson { get; set; }
	public string? AuthorPublicKey { get; set; }
	public string? AuthorSignature { get; set; }
}