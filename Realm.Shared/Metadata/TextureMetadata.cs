using Realm.Ecs.Components.Terrain;
using Realm.Shared.Serialization;

namespace Realm.Shared.Metadata;

[SchemaDescription("One terrain swatch: the texture painted onto the ground, how it is tiled and tinted, and the pathing and bombing rules the ground inherits from it.")]
public class TextureMetadata
{
	[SchemaDescription("The numbered slot the swatch is painted with in the terrain editor.")]
	public int SwatchIndex { get; set; }

	[SchemaDescription("How large one tile of the texture is drawn on the ground.")]
	[SchemaUnit("m")]
	public float ScaleFactor { get; set; }

	[SchemaDescription("Path to the ground texture drawn for this swatch.")]
	[SchemaAsset(new[] { "rtex" }, new[] { "Assets/textures" })]
	public string? TexturePath { get; set; }

	[SchemaDescription("The edge length of the texture in pixels, which the terrain shader uses to keep its noise at a fixed scale.")]
	public int TextureSize { get; set; }

	[SchemaDescription("The noise pattern blended into the texture so a repeating tile does not read as a grid.")]
	[SchemaControl(RealmSchemaControl.Json)]
	public string? NoiseConfig { get; set; }

	[SchemaDescription("How bright the ground painted with this swatch is.")]
	public float Brightness { get; set; } = 1.0f;

	[SchemaDescription("The colour multiplied over the texture, which turns one texture into several biomes.")]
	[SchemaControl(RealmSchemaControl.Color)]
	public string? Tint { get; set; }

	[SchemaDescription("How rough the ground painted with this swatch reads, where higher is duller.")]
	public float RoughnessScale { get; set; } = 1.0f;

	[SchemaDescription("How strongly the texture's normal map bends the light on the ground.")]
	public float NormalScale { get; set; } = 1.0f;

	[SchemaDescription("How much the texture's height map displaces the ground.")]
	public float HeightScale { get; set; } = 1.0f;

	[SchemaDescription("How far up or down the texture's height map sits before it is applied.")]
	public float HeightOffset { get; set; } = 0.0f;

	[SchemaDescription("How dark the crevices of the texture's height map are shaded.")]
	public float CrevicePower { get; set; } = 1.0f;

	[SchemaDescription("How the texture is tiled over the ground, such as a stochastic pattern that hides repetition or a plain grid.")]
	public string? TileMode { get; set; } = "Stochastic";

	[SchemaDescription("How many times the texture repeats over one world unit.")]
	public float UvScale { get; set; } = 1.0f;

	[SchemaDescription("The size of one stochastic tile, which decides how much variation is visible at once.")]
	[SchemaUnit("m")]
	public float StochasticTileSize { get; set; } = 1.0f;

	[SchemaDescription("How softly two stochastic tiles blend where they meet.")]
	[SchemaRange(0, 1)]
	public float CrossFade { get; set; } = 0.0f;

	[SchemaDescription("How much contrast is added to the texture when it is drawn.")]
	public float Contrast { get; set; }

	[SchemaDescription("How much saturation is added to the texture when it is drawn.")]
	public float Saturation { get; set; }

	[SchemaDescription("How shiny the ground painted with this swatch reads.")]
	public float Specular { get; set; }

	[SchemaDescription("How rough the ground's specular highlight is.")]
	public float Roughness { get; set; }

	[SchemaDescription("How metallic the ground reads, which tints its reflections with its own colour.")]
	public float Metallic { get; set; }

	[SchemaDescription("The pathing the ground inherits from this swatch, declared as named bits.")]
	[SchemaFlag(1, "ShallowWater")]
	[SchemaFlag(2, "DeepWater")]
	[SchemaFlag(4, "Flying")]
	[SchemaFlag(8, "Ground")]
	[SchemaFlag(32, "Buildable")]
	public int DefaultPathingCode { get; set; } = TerrainPathing.DefaultPathingCode_Ground;

	[SchemaDescription("The decals scattered over ground painted with this swatch, such as grass tufts or gravel.")]
	public List<ProceduralBombingDecalRule> DecalBombingRules { get; set; } = new();

	[SchemaDescription("The visual effects scattered over ground painted with this swatch, such as drifting embers or pollen.")]
	public List<ProceduralBombingVfxRule> VfxBombingRules { get; set; } = new();

	[SchemaDescription("The map the texture was imported from, kept so attribution survives a round trip through the editor.")]
	public string? Attribution_ImportedFromMap { get; set; }
}
