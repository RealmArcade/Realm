namespace Realm.Shared;

public struct UnoptimizationResult
{
	public bool Success;
	public byte[]? OutputGlbBytes;
	public string? OutputFilePath;
	public bool WasOptimized;
	public string? ErrorMessage;
}