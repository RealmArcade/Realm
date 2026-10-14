using Realm.Shared.Terrain;

namespace Realm.Shared.Metadata;

public class VfxAttachmentConfig
{
	public string VfxId { get; set; } = string.Empty;
	public string Name { get; set; } = string.Empty;

	public VfxPrimitiveType PrimitiveType { get; set; } = VfxPrimitiveType.VortexDisc;
	public SpellParticleConfig? ParticleConfig { get; set; }
	public VfxBlendMode BlendMode { get; set; } = VfxBlendMode.Additive;
	public VfxPlacementMode PlacementMode { get; set; } = VfxPlacementMode.SurfaceSnap;
	public string TargetSocket { get; set; } = "Root";

	public string BaseTexture { get; set; } = "";
	public string NoiseTexture { get; set; } = "";

	public Vector2Data BaseUvScroll { get; set; } = new Vector2Data(0.2f, 0.0f);
	public Vector2Data BaseUvScale { get; set; } = Vector2Data.One;
	public bool UseFlipbook { get; set; } = false;
	public int FlipbookColumns { get; set; } = 1;
	public int FlipbookRows { get; set; } = 1;
	public float FlipbookFps { get; set; } = 12.0f;
	public bool FlipbookSubframeBlend { get; set; } = true;
	public Vector2Data NoiseUvScroll { get; set; } = new Vector2Data(-0.15f, 0.25f);
	public Vector2Data NoiseUvScale { get; set; } = Vector2Data.One;

	public float DistortionStrength { get; set; } = 0.2f;

	public string BaseColor { get; set; } = "#ff7711";
	public string SecondaryColor { get; set; } = "#aa1100";
	public string CoreColor { get; set; } = "#ffffff";
	public float ColorMixRatio { get; set; } = 1.0f;
	public float EmissionBoost { get; set; } = 3.5f;
	public float CoreThreshold { get; set; } = 0.65f;

	public bool LuminanceToAlpha { get; set; } = true;
	public float LuminanceThreshold { get; set; } = 0.05f;
	public float LuminanceSmoothness { get; set; } = 0.08f;
	public bool UseGrayscale { get; set; } = true;
	public bool InvertMask { get; set; } = false;
	public float HighPassCutoff { get; set; } = 0.0f;

	public bool EnableRadialFalloff { get; set; } = true;
	public float RadialFalloffStart { get; set; } = 0.65f;
	public float RadialFalloffEnd { get; set; } = 1.0f;

	public bool EnableLengthFade { get; set; } = false;
	public float LengthFadeStart { get; set; } = 0.0f;
	public float LengthFadeEnd { get; set; } = 1.0f;
	public float ErosionProgress { get; set; } = 0.0f;

	public bool EnableFresnel { get; set; } = false;
	public float FresnelPower { get; set; } = 2.5f;
	public float FresnelIntensity { get; set; } = 1.5f;

	public bool EnableDepthFade { get; set; } = true;
	public float DepthFadeDistance { get; set; } = 0.35f;

	public float SurfaceNormalOffset { get; set; } = 0.02f;

	public Vector3Data PositionOffset { get; set; } = Vector3Data.Zero;
	public Vector3Data RotationOffset { get; set; } = Vector3Data.Zero;
	public Vector3Data ScaleOffset { get; set; } = Vector3Data.One;

	public VfxAttachmentConfig Clone()
	{
		return new VfxAttachmentConfig
		{
			VfxId = VfxId,
			Name = Name,
			PrimitiveType = PrimitiveType,
			BlendMode = BlendMode,
			PlacementMode = PlacementMode,
			TargetSocket = TargetSocket,
			BaseTexture = BaseTexture,
			NoiseTexture = NoiseTexture,
			BaseUvScroll = BaseUvScroll,
			BaseUvScale = BaseUvScale,
			UseFlipbook = UseFlipbook,
			FlipbookColumns = FlipbookColumns,
			FlipbookRows = FlipbookRows,
			FlipbookFps = FlipbookFps,
			FlipbookSubframeBlend = FlipbookSubframeBlend,
			NoiseUvScroll = NoiseUvScroll,
			NoiseUvScale = NoiseUvScale,
			DistortionStrength = DistortionStrength,
			BaseColor = BaseColor,
			SecondaryColor = SecondaryColor,
			CoreColor = CoreColor,
			ColorMixRatio = ColorMixRatio,
			EmissionBoost = EmissionBoost,
			CoreThreshold = CoreThreshold,
			LuminanceToAlpha = LuminanceToAlpha,
			LuminanceThreshold = LuminanceThreshold,
			LuminanceSmoothness = LuminanceSmoothness,
			UseGrayscale = UseGrayscale,
			InvertMask = InvertMask,
			HighPassCutoff = HighPassCutoff,
			EnableRadialFalloff = EnableRadialFalloff,
			RadialFalloffStart = RadialFalloffStart,
			RadialFalloffEnd = RadialFalloffEnd,
			EnableLengthFade = EnableLengthFade,
			LengthFadeStart = LengthFadeStart,
			LengthFadeEnd = LengthFadeEnd,
			ErosionProgress = ErosionProgress,
			EnableFresnel = EnableFresnel,
			FresnelPower = FresnelPower,
			FresnelIntensity = FresnelIntensity,
			EnableDepthFade = EnableDepthFade,
			DepthFadeDistance = DepthFadeDistance,
			SurfaceNormalOffset = SurfaceNormalOffset,
			PositionOffset = PositionOffset,
			RotationOffset = RotationOffset,
			ScaleOffset = ScaleOffset,
			ParticleConfig = ParticleConfig?.Clone()
		};
	}
}