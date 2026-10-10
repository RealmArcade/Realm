namespace Realm.Shared.Distribution;

public class AssetUploadResponseDto
{
	public bool Success { get; set; }
	public string Message { get; set; } = string.Empty;
	public bool Deduplicated { get; set; }
	public bool Merged { get; set; }
	public string Blake3Hash { get; set; } = string.Empty;
}