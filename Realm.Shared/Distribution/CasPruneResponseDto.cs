namespace Realm.Shared.Distribution;

public class CasPruneResponseDto
{
	public bool Success { get; set; }
	public int TotalScanned { get; set; }
	public int OrphansPruned { get; set; }
	public int CorruptPruned { get; set; }
	public long BytesFreed { get; set; }
	public string Message { get; set; } = string.Empty;
}