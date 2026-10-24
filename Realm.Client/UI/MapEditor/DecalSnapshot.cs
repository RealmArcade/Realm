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

	public DecalAnimationConfig Animation { get; set; } = new();

	public float UpperFade { get; set; } = 0.3f;
	public float LowerFade { get; set; } = 0.3f;

	public VfxPlacementMode PlacementMode { get; set; } = VfxPlacementMode.Free;
	public float ProjectionSize { get; set; }
	public float ProjectionHeight { get; set; }
	public float GroundOffset { get; set; } = 0.05f;
	public float NormalFade { get; set; }
	public bool DistanceFadeEnabled { get; set; }
	public float DistanceFadeBegin { get; set; } = 10.0f;
	public float DistanceFadeEnd { get; set; } = 30.0f;

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
			Animation = new DecalAnimationConfig
			{
				AnimateOpacity = this.Animation.AnimateOpacity,
				OpacityPulseSpeed = this.Animation.OpacityPulseSpeed,
				MinOpacity = this.Animation.MinOpacity,
				MaxOpacity = this.Animation.MaxOpacity,
				AnimateEmission = this.Animation.AnimateEmission,
				EmissionPulseSpeed = this.Animation.EmissionPulseSpeed,
				MinEmission = this.Animation.MinEmission,
				MaxEmission = this.Animation.MaxEmission,
				AnimateScale = this.Animation.AnimateScale,
				ScalePulseSpeed = this.Animation.ScalePulseSpeed,
				MinScaleRatio = this.Animation.MinScaleRatio,
				MaxScaleRatio = this.Animation.MaxScaleRatio
			},
			UpperFade = this.UpperFade,
			LowerFade = this.LowerFade,
			PlacementMode = this.PlacementMode,
			ProjectionSize = this.ProjectionSize,
			ProjectionHeight = this.ProjectionHeight,
			GroundOffset = this.GroundOffset,
			NormalFade = this.NormalFade,
			DistanceFadeEnabled = this.DistanceFadeEnabled,
			DistanceFadeBegin = this.DistanceFadeBegin,
			DistanceFadeEnd = this.DistanceFadeEnd
		};
	}
}