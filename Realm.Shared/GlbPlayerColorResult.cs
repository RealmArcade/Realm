namespace Realm.Shared;

public class GlbPlayerColorResult
{
	public bool Success { get; set; }
	public string? ErrorMessage { get; set; }
	public string? OutputFilePath { get; set; }
	public int MaskedFaceCount { get; set; }
	public int TotalFaceCount { get; set; }
	public string? DetectedChromaKey { get; set; }
}