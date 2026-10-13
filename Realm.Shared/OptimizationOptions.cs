namespace Realm.Shared;

public struct OptimizationOptions
{
	public float SimplificationRatio { get; set; } = 0.5f;
	public float AllowedPixelError { get; set; } = 1.5f;
	public int MaxTextureResolution { get; set; } = 1024;
	public bool ForceReDecimate { get; set; } = false;
	public bool CompressTextures { get; set; } = true;
	public bool GenerateLods { get; set; } = true;

	public OptimizationOptions()
	{
	}
}