namespace Realm.Shared.Animation;

public class RanimExportResult
{
	public bool Success { get; set; }
	public string InputPath { get; set; } = string.Empty;
	public string OutputPath { get; set; } = string.Empty;
	public int FrameCount { get; set; }
	public string ErrorMessage { get; set; } = string.Empty;
}