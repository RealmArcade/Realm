namespace Realm.Shared.Distribution;

public class PublishMapInitiateResponse
{
	public bool Success { get; set; }
	public string SessionId { get; set; } = string.Empty;
	public string MapId { get; set; } = string.Empty;
	public string Status { get; set; } = string.Empty;
	public string Message { get; set; } = string.Empty;
	public List<string> MissingHashes { get; set; } = new();
	public bool IsGreenlit { get; set; }
}