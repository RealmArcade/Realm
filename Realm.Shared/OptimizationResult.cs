namespace Realm.Shared;

public struct OptimizationResult
{
	public bool Success;
	public byte[]? OutputGlbBytes;
	public string? OutputFilePath;
	public int OriginalSize;
	public int OptimizedSize;
	public bool DecimationSkipped;
	public int[]? LodTriangleCounts;
	public string? ErrorMessage;
}