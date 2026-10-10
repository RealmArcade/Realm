namespace Realm.Shared.Distribution;

public class MapMaintainersResponseDto
{
	public bool Success { get; set; }
	public string MapTitle { get; set; } = string.Empty;
	public string OwnerPublicKey { get; set; } = string.Empty;
	public List<string> Maintainers { get; set; } = new();
	public string Message { get; set; } = string.Empty;
}