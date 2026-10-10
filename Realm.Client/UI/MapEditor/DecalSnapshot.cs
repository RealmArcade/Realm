using Godot;

namespace Realm.Client.UI.MapEditor;

public class DecalSnapshot
{
	public string DecalId { get; set; } = "";
	public string TexturePath { get; set; } = "";
	public float Brightness { get; set; } = 1.0f;
	public Color Tint { get; set; } = Colors.White;
	public float Contrast { get; set; } = 1.0f;
	public float Saturation { get; set; } = 1.0f;
	public float Opacity { get; set; } = 1.0f;
	public float AlbedoMix { get; set; } = 1.0f;
	public float NormalStrength { get; set; } = 0.0f;
	public float Roughness { get; set; } = 1.0f;
	public float Metallic { get; set; } = 0.0f;
	public string BlendMode { get; set; } = "Mix";

	public bool AnimateOpacity { get; set; } = false;
	public float OpacityPulseSpeed { get; set; } = 1.0f;
	public float MinOpacity { get; set; } = 0.2f;
	public float MaxOpacity { get; set; } = 1.0f;

	public bool AnimateEmission { get; set; } = false;
	public float EmissionPulseSpeed { get; set; } = 1.0f;
	public float MinEmission { get; set; } = 0.0f;
	public float MaxEmission { get; set; } = 2.0f;

	public bool AnimateScale { get; set; } = false;
	public float ScalePulseSpeed { get; set; } = 1.0f;
	public float MinScaleRatio { get; set; } = 0.8f;
	public float MaxScaleRatio { get; set; } = 1.2f;

	public float UpperFade { get; set; } = 0.3f;
	public float LowerFade { get; set; } = 0.3f;

	public DecalSnapshot Clone()
	{
		return new DecalSnapshot
		{
			DecalId = this.DecalId,
			TexturePath = this.TexturePath,
			Brightness = this.Brightness,
			Tint = this.Tint,
			Contrast = this.Contrast,
			Saturation = this.Saturation,
			Opacity = this.Opacity,
			AlbedoMix = this.AlbedoMix,
			NormalStrength = this.NormalStrength,
			Roughness = this.Roughness,
			Metallic = this.Metallic,
			BlendMode = this.BlendMode,
			AnimateOpacity = this.AnimateOpacity,
			OpacityPulseSpeed = this.OpacityPulseSpeed,
			MinOpacity = this.MinOpacity,
			MaxOpacity = this.MaxOpacity,
			AnimateEmission = this.AnimateEmission,
			EmissionPulseSpeed = this.EmissionPulseSpeed,
			MinEmission = this.MinEmission,
			MaxEmission = this.MaxEmission,
			AnimateScale = this.AnimateScale,
			ScalePulseSpeed = this.ScalePulseSpeed,
			MinScaleRatio = this.MinScaleRatio,
			MaxScaleRatio = this.MaxScaleRatio,
			UpperFade = this.UpperFade,
			LowerFade = this.LowerFade
		};
	}
}