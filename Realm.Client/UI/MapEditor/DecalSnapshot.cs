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
			AnimationSettings = new Realm.Shared.Metadata.DecalAnimationSettings
			{
				AnimateOpacity = this.AnimationSettings.AnimateOpacity,
				OpacityPulseSpeed = this.AnimationSettings.OpacityPulseSpeed,
				MinOpacity = this.AnimationSettings.MinOpacity,
				MaxOpacity = this.AnimationSettings.MaxOpacity,
				AnimateEmission = this.AnimationSettings.AnimateEmission,
				EmissionPulseSpeed = this.AnimationSettings.EmissionPulseSpeed,
				MinEmission = this.AnimationSettings.MinEmission,
				MaxEmission = this.AnimationSettings.MaxEmission,
				AnimateScale = this.AnimationSettings.AnimateScale,
				ScalePulseSpeed = this.AnimationSettings.ScalePulseSpeed,
				MinScaleRatio = this.AnimationSettings.MinScaleRatio,
				MaxScaleRatio = this.AnimationSettings.MaxScaleRatio
			},
			UpperFade = this.UpperFade,
			LowerFade = this.LowerFade
		};
	}
}