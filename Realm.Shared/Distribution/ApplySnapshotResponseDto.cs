namespace Realm.Shared.Distribution;

public class ApplySnapshotResponseDto
{
	public bool Success { get; set; }
	public string ComputedStateDigest { get; set; } = string.Empty;
	public string ExpectedStateDigest { get; set; } = string.Empty;
	public int CreatorsImported { get; set; }
	public int MapsImported { get; set; }
	public int StatsImported { get; set; }
	public string Message { get; set; } = string.Empty;
}