namespace Realm.Shared.Audio;

public class AudioConversionResult
{
	public bool Success { get; set; }
	public string InputPath { get; set; } = string.Empty;
	public string OutputPath { get; set; } = string.Empty;
	public string ErrorMessage { get; set; } = string.Empty;
	public string? AssetType { get; set; }
	public string? Author { get; set; }
	public string? PreferredFileName { get; set; }
	public byte[]? OutputBytes { get; set; }
}