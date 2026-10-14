namespace Realm.Shared.Animation;

public class MixamoFbxConversionResult
{
	public bool Success { get; set; }
	public string InputPath { get; set; } = string.Empty;
	public string OutputPath { get; set; } = string.Empty;
	public List<string> ConvertedAnimationNames { get; set; } = new();
	public string ErrorMessage { get; set; } = string.Empty;
}