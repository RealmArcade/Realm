namespace Realm.Shared.Distribution;

public class HeaderSyncResponseDto
{
	public bool Updated { get; set; }
	public string CurrentMetadataHeadersJson { get; set; } = string.Empty;
}