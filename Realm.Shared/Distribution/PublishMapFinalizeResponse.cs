namespace Realm.Shared.Distribution;

public class PublishMapFinalizeResponse
{
	public bool Success { get; set; }
	public string MapId { get; set; } = string.Empty;
	public string Status { get; set; } = string.Empty;
	public string Message { get; set; } = string.Empty;
	public List<string> MissingHashes { get; set; } = new();
}