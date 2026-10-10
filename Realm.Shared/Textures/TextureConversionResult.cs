namespace Realm.Shared.Textures;

public class TextureConversionResult
{
	public bool Success { get; set; }
	public string InputPath { get; set; } = string.Empty;
	public string OutputPath { get; set; } = string.Empty;
	public string ErrorMessage { get; set; } = string.Empty;
	public float ScaleFactor { get; set; } = 1.0f;
}