namespace Realm.Shared.Metadata;

public class DecalMetadata
{
	public string? TexturePath { get; set; }
	public string? Tint { get; set; }
	public float Brightness { get; set; } = 1.0f;
	public float Contrast { get; set; } = 1.0f;
	public float Saturation { get; set; } = 1.0f;
	public float Opacity { get; set; } = 1.0f;
	public float AlbedoMix { get; set; }
	public float NormalStrength { get; set; }
	public float Roughness { get; set; }
	public float Metallic { get; set; }
	public string? BlendMode { get; set; }
	public string? TextureNormal { get; set; }
	public string? TextureOrm { get; set; }
	public string? TextureEmission { get; set; }
	public float EmissionEnergy { get; set; }
	public bool AnimateOpacity { get; set; }
	public float OpacityPulseSpeed { get; set; }
	public float MinOpacity { get; set; }
	public float MaxOpacity { get; set; }
	public bool AnimateEmission { get; set; }
	public float EmissionPulseSpeed { get; set; }
	public float MinEmission { get; set; }
	public float MaxEmission { get; set; }
	public bool AnimateScale { get; set; }
	public float ScalePulseSpeed { get; set; }
	public float MinScaleRatio { get; set; }
	public float MaxScaleRatio { get; set; }
	public float UpperFade { get; set; }
	public float LowerFade { get; set; }
}