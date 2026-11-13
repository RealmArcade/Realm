using Realm.Shared.Serialization;

namespace Realm.Shared.Metadata;

[SchemaDescription("One decal template: the picture projected onto the ground and everything about how it is tinted, lit, faded and animated.")]
public class DecalMetadata
{
	[SchemaDescription("Path to the decal image.")]
	[SchemaAsset(new[] { "rtex" }, new[] { "Assets/decals" })]
	public string? TexturePath { get; set; }

	[SchemaDescription("The colour multiplied over the decal image.")]
	[SchemaControl(RealmSchemaControl.Color)]
	public string? Tint { get; set; }

	[SchemaDescription("How bright the decal is drawn.")]
	public float Brightness { get; set; } = 1.0f;

	[SchemaDescription("How much contrast the decal is drawn with.")]
	public float Contrast { get; set; } = 1.0f;

	[SchemaDescription("How saturated the decal is drawn.")]
	public float Saturation { get; set; } = 1.0f;

	[SchemaDescription("How opaque the decal is. Zero makes it invisible.")]
	[SchemaUnit("%")]
	[SchemaRange(0, 1)]
	public float Opacity { get; set; } = 1.0f;

	[SchemaDescription("How much of the ground's own colour shows through the decal, from the decal alone to the ground alone.")]
	[SchemaUnit("%")]
	[SchemaRange(0, 1)]
	public float AlbedoMix { get; set; }

	[SchemaDescription("How strongly the decal's normal map bends the light on the ground under it.")]
	public float NormalStrength { get; set; }

	[SchemaDescription("How rough the ground reads under the decal.")]
	public float Roughness { get; set; }

	[SchemaDescription("How metallic the ground reads under the decal.")]
	public float Metallic { get; set; }

	[SchemaDescription("How the decal is mixed with the ground, such as alpha blending or additive glow.")]
	public string? BlendMode { get; set; }

	[SchemaDescription("Path to the decal's normal map, which gives the ground under it a raised or sunken look.")]
	[SchemaAsset(new[] { "rtex" }, new[] { "Assets/decals" })]
	public string? TextureNormal { get; set; }

	[SchemaDescription("Path to the decal's packed occlusion, roughness and metallic map.")]
	[SchemaAsset(new[] { "rtex" }, new[] { "Assets/decals" })]
	public string? TextureOrm { get; set; }

	[SchemaDescription("Path to the decal's emission map, which is the part of it that glows.")]
	[SchemaAsset(new[] { "rtex" }, new[] { "Assets/decals" })]
	public string? TextureEmission { get; set; }

	[SchemaDescription("How strongly the decal's emission glows.")]
	public float EmissionEnergy { get; set; }

	[SchemaDescription("Whether the decal's opacity pulses over time, which is how a torch's light pool flickers.")]
	public bool AnimateOpacity { get; set; }

	[SchemaDescription("How fast the decal's opacity pulses.")]
	public float OpacityPulseSpeed { get; set; }

	[SchemaDescription("The lowest opacity the decal's pulse reaches.")]
	[SchemaUnit("%")]
	[SchemaRange(0, 1)]
	public float MinOpacity { get; set; }

	[SchemaDescription("The highest opacity the decal's pulse reaches.")]
	[SchemaUnit("%")]
	[SchemaRange(0, 1)]
	public float MaxOpacity { get; set; }

	[SchemaDescription("Whether the decal's glow pulses over time.")]
	public bool AnimateEmission { get; set; }

	[SchemaDescription("How fast the decal's glow pulses.")]
	public float EmissionPulseSpeed { get; set; }

	[SchemaDescription("The lowest glow the decal's pulse reaches.")]
	public float MinEmission { get; set; }

	[SchemaDescription("The highest glow the decal's pulse reaches.")]
	public float MaxEmission { get; set; }

	[SchemaDescription("Whether the decal grows and shrinks over time, which is how a spreading pool of light moves.")]
	public bool AnimateScale { get; set; }

	[SchemaDescription("How fast the decal's size pulses.")]
	public float ScalePulseSpeed { get; set; }

	[SchemaDescription("The smallest size the decal's pulse reaches, as a fraction of its placed size.")]
	[SchemaUnit("%")]
	public float MinScaleRatio { get; set; }

	[SchemaDescription("The largest size the decal's pulse reaches, as a fraction of its placed size.")]
	[SchemaUnit("%")]
	public float MaxScaleRatio { get; set; }

	[SchemaDescription("How far up a vertical surface the decal fades out, so it does not stop in a hard line where the ground climbs.")]
	[SchemaUnit("m")]
	public float UpperFade { get; set; }

	[SchemaDescription("How far down a vertical surface the decal fades out.")]
	[SchemaUnit("m")]
	public float LowerFade { get; set; }

	[SchemaDescription("How the decal is anchored to the world, such as snapped to the surface under it or projected from above.")]
	public VfxPlacementMode PlacementMode { get; set; } = VfxPlacementMode.Free;

	[SchemaDescription("The width of the area the decal is projected onto when its placement mode projects from above.")]
	[SchemaUnit("m")]
	public float ProjectionSize { get; set; }

	[SchemaDescription("How far above the ground the decal is projected from.")]
	[SchemaUnit("m")]
	public float ProjectionHeight { get; set; }

	[SchemaDescription("How far above the ground the decal is lifted, so it does not fight with the terrain for the same depth.")]
	[SchemaUnit("m")]
	public float GroundOffset { get; set; } = 0.05f;

	[SchemaDescription("How sharply the decal fades where the surface under it turns away from the projection direction.")]
	public float NormalFade { get; set; }

	[SchemaDescription("Whether the decal fades out as the camera pulls away instead of staying equally strong at every zoom.")]
	public bool DistanceFadeEnabled { get; set; }

	[SchemaDescription("How far the camera has to be before the decal starts to fade.")]
	[SchemaUnit("m")]
	public float DistanceFadeBegin { get; set; } = 10.0f;

	[SchemaDescription("How far the camera has to be before the decal has faded away completely.")]
	[SchemaUnit("m")]
	public float DistanceFadeEnd { get; set; } = 30.0f;

	[SchemaDescription("The map the decal was imported from, kept so attribution survives a round trip through the editor.")]
	public string? Attribution_ImportedFromMap { get; set; }
}
