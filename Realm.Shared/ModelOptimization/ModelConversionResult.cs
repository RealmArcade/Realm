namespace Realm.Shared.ModelOptimization;

public class ModelConversionResult
{
	public bool Success { get; set; }
	public string InputPath { get; set; } = string.Empty;
	public string OutputPath { get; set; } = string.Empty;
	public string ErrorMessage { get; set; } = string.Empty;
	public bool SupportsTeamColor { get; set; }
	public string? AssetType { get; set; }
	public string? Author { get; set; }
	public string? PreferredFileName { get; set; }
	public int OriginalSize { get; set; }
	public int OptimizedSize { get; set; }
	public byte[]? OutputBytes { get; set; }
}