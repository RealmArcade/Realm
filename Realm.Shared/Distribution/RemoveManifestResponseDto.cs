namespace Realm.Shared.Distribution;

public class RemoveManifestResponseDto
{
	public bool Success { get; set; }
	public string MapTitle { get; set; } = string.Empty;
	public string? MapVersion { get; set; }
	public bool AllVersionsRemoved { get; set; }
	public int ManifestsDeleted { get; set; }
	public int DbRecordsRemoved { get; set; }
	public List<string> DeletedManifestFiles { get; set; } = new();
	public string Message { get; set; } = string.Empty;
}