namespace Realm.Shared.Distribution;

public class MapPublishResponseDto
{
	public bool Success { get; set; }
	public string Status { get; set; } = string.Empty;
	public string MapId { get; set; } = string.Empty;
	public string Message { get; set; } = string.Empty;
	public List<string> MissingAssetHashes { get; set; } = new();
}