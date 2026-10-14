namespace Realm.Shared.Metadata;

public class TextureMetadata
{
	public int SwatchIndex { get; set; }
	public float ScaleFactor { get; set; }
	public string? TexturePath { get; set; }
	public int TextureSize { get; set; }
	public string? NoiseConfig { get; set; }
	public float Brightness { get; set; } = 1.0f;
	public string? Tint { get; set; }
	public float RoughnessScale { get; set; } = 1.0f;
	public float NormalScale { get; set; } = 1.0f;
	public float HeightScale { get; set; } = 1.0f;
	public float HeightOffset { get; set; } = 0.0f;
	public float CrevicePower { get; set; } = 1.0f;
	public string? TileMode { get; set; } = "Stochastic";
	public float UvScale { get; set; } = 1.0f;
	public float StochasticTileSize { get; set; } = 1.0f;
	public float CrossFade { get; set; } = 0.0f;
	public float Contrast { get; set; }
	public float Saturation { get; set; }
	public float Specular { get; set; }
	public float Roughness { get; set; }
	public float Metallic { get; set; }
	public int DefaultPathingCode { get; set; } = 8 | 32 | 4;
	public List<ProceduralBombingDecalRule> DecalBombingRules { get; set; } = new();
	public List<ProceduralBombingVfxRule> VfxBombingRules { get; set; } = new();
}