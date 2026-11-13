using Realm.Shared.Serialization;
using Realm.Shared.Terrain;

namespace Realm.Shared.Metadata;

[SchemaDescription("One visual effect attachment: the geometry it is wrapped in, the textures and colours it is drawn with, and how it is masked, faded, tinted and placed in the world.")]
public class VfxAttachmentConfig
{
	[SchemaDescription("The identifier abilities, weapons and attachments name this visual effect by.")]
	[SchemaSensitive]
	public string VfxId { get; set; } = string.Empty;

	[SchemaDescription("The name the visual effect shows in the editor.")]
	public string Name { get; set; } = string.Empty;

	[SchemaDescription("The shape the effect is drawn with, which decides how its texture is wrapped and how it moves over its life.")]
	public VfxPrimitiveType PrimitiveType { get; set; } = VfxPrimitiveType.VortexDisc;

	[SchemaDescription("The particle system the effect emits, when its primitive type is a real particle system rather than wrapped geometry.")]
	public SpellParticleConfig? ParticleConfig { get; set; }

	[SchemaDescription("The stages the effect plays over its life, each with its own trigger, timing and look.")]
	public List<VfxStageMetadata>? Stages { get; set; }

	[SchemaDescription("How the effect is mixed with what is behind it.")]
	public VfxBlendMode BlendMode { get; set; } = VfxBlendMode.Additive;

	[SchemaDescription("How the effect is placed against the world when it is spawned.")]
	public VfxPlacementMode PlacementMode { get; set; } = VfxPlacementMode.SurfaceSnap;

	[SchemaDescription("Whether the effect is tinted with the colour of the player it belongs to, so two armies' effects read apart.")]
	public bool TintWithPlayerColor { get; set; } = true;

	[SchemaDescription("The socket of the model the effect is parented to, such as a hand, a weapon tip or the model's root.")]
	public string TargetSocket { get; set; } = "Root";

	[SchemaDescription("The texture wrapped over the effect's geometry, which gives it its streaks, rings or flames.")]
	[SchemaAsset(new[] { "rtex" }, new[] { "Assets/vfx_spritesheets", "Assets/vfx_radial", "Assets/vfx_vertical" })]
	public string BaseTexture { get; set; } = "";

	[SchemaDescription("The noise texture the effect's distortion and erosion are read from.")]
	[SchemaAsset(new[] { "rtex" }, new[] { "Assets/noise" })]
	public string NoiseTexture { get; set; } = "";

	[SchemaDescription("The procedural motion profile the effect's geometry is animated with.")]
	[SchemaReference("CustomProceduralAnimations")]
	public string ProceduralMotionId { get; set; } = "";

	[SchemaDescription("How fast the base texture scrolls across the effect's geometry.")]
	public Vector2Data BaseUvScroll { get; set; } = new Vector2Data(0.2f, 0.0f);

	[SchemaDescription("How many times the base texture repeats over the effect's geometry.")]
	public Vector2Data BaseUvScale { get; set; } = Vector2Data.One;

	[SchemaDescription("Whether the effect is animated from a sprite sheet rather than from a scrolling texture.")]
	public bool UseFlipbook { get; set; } = false;

	[SchemaDescription("How many frames the sprite sheet holds across.")]
	public int FlipbookColumns { get; set; } = 1;

	[SchemaDescription("How many frames the sprite sheet holds down.")]
	public int FlipbookRows { get; set; } = 1;

	[SchemaDescription("How many sprite sheet frames the effect plays per second.")]
	public float FlipbookFps { get; set; } = 12.0f;

	[SchemaDescription("Whether two neighbouring sprite sheet frames are blended while the effect plays.")]
	public bool FlipbookSubframeBlend { get; set; } = true;

	[SchemaDescription("How fast the noise texture scrolls across the effect's geometry.")]
	public Vector2Data NoiseUvScroll { get; set; } = new Vector2Data(-0.15f, 0.25f);

	[SchemaDescription("How many times the noise texture repeats over the effect's geometry.")]
	public Vector2Data NoiseUvScale { get; set; } = Vector2Data.One;

	[SchemaDescription("How far the noise pushes the effect's texture around, which makes a flat shape read as turbulent.")]
	public float DistortionStrength { get; set; } = 0.2f;

	[SchemaDescription("The main colour the effect is drawn in.")]
	[SchemaControl(RealmSchemaControl.Color)]
	public string BaseColor { get; set; } = "#ff7711";

	[SchemaDescription("The colour blended with the main colour away from the core, which gives the effect depth.")]
	[SchemaControl(RealmSchemaControl.Color)]
	public string SecondaryColor { get; set; } = "#aa1100";

	[SchemaDescription("The colour of the brightest part of the effect, its core.")]
	[SchemaControl(RealmSchemaControl.Color)]
	public string CoreColor { get; set; } = "#ffffff";

	[SchemaDescription("How far the effect is mixed towards its secondary colour rather than its base colour.")]
	[SchemaRange(0, 1)]
	public float ColorMixRatio { get; set; } = 1.0f;

	[SchemaDescription("How strongly the effect's colours glow, which additive blending turns into brightness.")]
	public float EmissionBoost { get; set; } = 3.5f;

	[SchemaDescription("The texture value above which the effect is drawn in its core colour.")]
	[SchemaRange(0, 1)]
	public float CoreThreshold { get; set; } = 0.65f;

	[SchemaDescription("Whether the texture's brightness is turned into the effect's opacity, which is how a black-backed flame texture becomes a flame.")]
	public bool LuminanceToAlpha { get; set; } = true;

	[SchemaDescription("The brightness below which the texture is fully transparent when it is turned into opacity.")]
	[SchemaRange(0, 1)]
	public float LuminanceThreshold { get; set; } = 0.05f;

	[SchemaDescription("How softly the brightness-to-opacity cutoff is applied.")]
	[SchemaRange(0, 1)]
	public float LuminanceSmoothness { get; set; } = 0.08f;

	[SchemaDescription("Whether the effect works from the grayscale brightness of its texture rather than each colour channel.")]
	public bool UseGrayscale { get; set; } = true;

	[SchemaDescription("Whether the effect's mask is inverted, so the parts that would be hidden are the parts that are drawn.")]
	public bool InvertMask { get; set; } = false;

	[SchemaDescription("The brightness the effect's mask is cut from, which removes its dim background entirely.")]
	[SchemaRange(0, 1)]
	public float HighPassCutoff { get; set; } = 0.0f;

	[SchemaDescription("Whether the effect fades out towards the edge of its shape instead of ending in a hard outline.")]
	public bool EnableRadialFalloff { get; set; } = true;

	[SchemaDescription("How far out the effect stays fully drawn before its radial fade begins.")]
	[SchemaRange(0, 1)]
	public float RadialFalloffStart { get; set; } = 0.65f;

	[SchemaDescription("How far out the effect has faded away completely.")]
	[SchemaRange(0, 1)]
	public float RadialFalloffEnd { get; set; } = 1.0f;

	[SchemaDescription("Whether the effect fades along its length, which lets a beam or a trail end softly.")]
	public bool EnableLengthFade { get; set; } = false;

	[SchemaDescription("How far along its length the effect stays fully drawn before its length fade begins.")]
	[SchemaRange(0, 1)]
	public float LengthFadeStart { get; set; } = 0.0f;

	[SchemaDescription("How far along its length the effect has faded away completely.")]
	[SchemaRange(0, 1)]
	public float LengthFadeEnd { get; set; } = 1.0f;

	[SchemaDescription("How much the effect has eroded away, from whole to gone.")]
	[SchemaRange(0, 1)]
	public float ErosionProgress { get; set; } = 0.0f;

	[SchemaDescription("Whether the effect brightens where it turns away from the camera, which gives an aura a glowing rim.")]
	public bool EnableFresnel { get; set; } = false;

	[SchemaDescription("How tightly the effect's rim glow hugs its silhouette.")]
	public float FresnelPower { get; set; } = 2.5f;

	[SchemaDescription("How strong the effect's rim glow is.")]
	public float FresnelIntensity { get; set; } = 1.5f;

	[SchemaDescription("Whether the effect fades where it cuts into the ground or another solid surface.")]
	public bool EnableDepthFade { get; set; } = true;

	[SchemaDescription("How far the effect has to be inside another surface before it has faded away.")]
	[SchemaUnit("m")]
	public float DepthFadeDistance { get; set; } = 0.35f;

	[SchemaDescription("How far the effect is lifted along the surface normal, so it does not fight the surface for the same depth.")]
	[SchemaUnit("m")]
	public float SurfaceNormalOffset { get; set; } = 0.02f;

	[SchemaDescription("Where the effect sits relative to what it is attached to.")]
	public Vector3Data PositionOffset { get; set; } = Vector3Data.Zero;

	[SchemaDescription("How the effect is turned relative to what it is attached to, in degrees around each axis.")]
	public Vector3Data RotationOffset { get; set; } = Vector3Data.Zero;

	[SchemaDescription("The multiplier applied to the effect's size on each axis.")]
	public Vector3Data ScaleOffset { get; set; } = Vector3Data.One;

	[SchemaDescription("The map the effect was imported from, kept so attribution survives a round trip through the editor.")]
	public string? Attribution_ImportedFromMap { get; set; }

	public VfxAttachmentConfig Clone()
	{
		return new VfxAttachmentConfig
		{
			VfxId = VfxId,
			Name = Name,
			PrimitiveType = PrimitiveType,
			BlendMode = BlendMode,
			PlacementMode = PlacementMode,
			TintWithPlayerColor = TintWithPlayerColor,
			TargetSocket = TargetSocket,
			BaseTexture = BaseTexture,
			NoiseTexture = NoiseTexture,
			ProceduralMotionId = ProceduralMotionId,
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
			ParticleConfig = ParticleConfig?.Clone(),
			Stages = Stages?.Select(stage => stage.Clone()).ToList()
		};
	}
}