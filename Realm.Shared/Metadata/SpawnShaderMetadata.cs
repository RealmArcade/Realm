namespace Realm.Shared.Metadata;

public class SpawnShaderMetadata
{
	public string Name { get; set; } = string.Empty;
	public int TransitionMode { get; set; }
	public int Direction { get; set; }
	public string EdgeColor { get; set; } = "#ff661aff";
	public float EdgeWidth { get; set; } = 0.06f;
	public float EdgeEmission { get; set; } = 5.0f;
	public float NoiseScale { get; set; } = 12.0f;
	public float NoiseRoughness { get; set; } = 0.5f;
	public float FresnelPower { get; set; } = 2.5f;
	public float VertexDisplacement { get; set; } = 0.0f;
	public float AlphaFade { get; set; } = 1.0f;
	public float Duration { get; set; } = 1.2f;
	public string AssetType { get; set; } = "SpawnShader";
}