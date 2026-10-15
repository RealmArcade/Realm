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

	public Realm.Shared.Metadata.DecalAnimationSettings AnimationSettings { get; set; } = new Realm.Shared.Metadata.DecalAnimationSettings();

	public bool AnimateOpacity { get => AnimationSettings.AnimateOpacity; set => AnimationSettings.AnimateOpacity = value; }
	public float OpacityPulseSpeed { get => AnimationSettings.OpacityPulseSpeed; set => AnimationSettings.OpacityPulseSpeed = value; }
	public float MinOpacity { get => AnimationSettings.MinOpacity; set => AnimationSettings.MinOpacity = value; }
	public float MaxOpacity { get => AnimationSettings.MaxOpacity; set => AnimationSettings.MaxOpacity = value; }

	public bool AnimateEmission { get => AnimationSettings.AnimateEmission; set => AnimationSettings.AnimateEmission = value; }
	public float EmissionPulseSpeed { get => AnimationSettings.EmissionPulseSpeed; set => AnimationSettings.EmissionPulseSpeed = value; }
	public float MinEmission { get => AnimationSettings.MinEmission; set => AnimationSettings.MinEmission = value; }
	public float MaxEmission { get => AnimationSettings.MaxEmission; set => AnimationSettings.MaxEmission = value; }

	public bool AnimateScale { get => AnimationSettings.AnimateScale; set => AnimationSettings.AnimateScale = value; }
	public float ScalePulseSpeed { get => AnimationSettings.ScalePulseSpeed; set => AnimationSettings.ScalePulseSpeed = value; }
	public float MinScaleRatio { get => AnimationSettings.MinScaleRatio; set => AnimationSettings.MinScaleRatio = value; }
	public float MaxScaleRatio { get => AnimationSettings.MaxScaleRatio; set => AnimationSettings.MaxScaleRatio = value; }

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