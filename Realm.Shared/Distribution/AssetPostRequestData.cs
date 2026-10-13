namespace Realm.Shared.Distribution;

public class AssetPostRequestData
{
	public byte[] AssetBytes { get; set; } = Array.Empty<byte>();
	public string? MetadataJson { get; set; }
	public string? AuthorPublicKey { get; set; }
	public string? AuthorSignature { get; set; }
	public string FileExtension { get; set; } = ".bin";
	public string NormalizedHash { get; set; } = string.Empty;
}