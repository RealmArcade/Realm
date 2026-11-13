using Realm.Ecs.Components.Terrain;
using Realm.Shared.Serialization;

namespace Realm.Shared.Metadata;

[SchemaDescription("One water profile: the colours, depth, waves, flow, refraction, glow and detail texture of water painted onto the terrain, and the pathing and bombing rules it carries.")]
public class WaterProfileSaveData
{
	[SchemaDescription("The identifier a texture's pathing profile names this water by.")]
	[SchemaSensitive]
	public string Id { get; set; } = "water_default";

	[SchemaDescription("The name the water profile shows in the editor.")]
	public string Name { get; set; } = "Water";

	[SchemaDescription("The numbered slot the water is painted with in the terrain editor.")]
	public byte ProfileIndex { get; set; } = 0;

	[SchemaDescription("The colour of water where it is shallow enough to see the ground through it.")]
	[SchemaControl(RealmSchemaControl.Color)]
	public string ShallowColorHex { get; set; } = "#0D4D618C";

	[SchemaDescription("The colour of water where it is too deep to see the ground through it.")]
	[SchemaControl(RealmSchemaControl.Color)]
	public string DeepColorHex { get; set; } = "#030F24FA";

	[SchemaDescription("The colour of the foam along the shoreline.")]
	[SchemaControl(RealmSchemaControl.Color)]
	public string FoamColorHex { get; set; } = "#D9F2FFD9";

	[SchemaDescription("How deep the water has to be before it is fully drawn in its deep colour.")]
	[SchemaUnit("m")]
	public float MaxDepth { get; set; } = 2.0f;

	[SchemaDescription("How deep the water has to be for the shoreline foam to have faded away.")]
	[SchemaUnit("m")]
	public float FoamDepth { get; set; } = 0.6f;

	[SchemaDescription("How fast the water's waves travel over its surface.")]
	public float WaveSpeed { get; set; } = 1.2f;

	[SchemaDescription("How far the water's waves push its surface up and down.")]
	[SchemaUnit("m")]
	public float WaveStrength { get; set; } = 0.06f;

	[SchemaDescription("Whether the water is rippled with a normal map rather than by its wave function alone.")]
	public bool UseNormalTexture { get; set; } = false;

	[SchemaDescription("Path to the normal map that ripples the water's surface.")]
	[SchemaAsset(new[] { "rtex" }, new[] { "Assets/textures", "Assets/noise" })]
	public string NormalTexturePath { get; set; } = "";

	[SchemaDescription("How strongly the water's normal map bends the light.")]
	public float NormalScale { get; set; } = 1.0f;

	[SchemaDescription("Which way the water flows along its surface, on the map's first horizontal axis.")]
	public float FlowDirectionX { get; set; } = 1.0f;

	[SchemaDescription("Which way the water flows along its surface, on the map's second horizontal axis.")]
	public float FlowDirectionY { get; set; } = 0.0f;

	[SchemaDescription("How fast the water's surface drifts along its flow direction.")]
	public float FlowSpeed { get; set; } = 0.5f;

	[SchemaDescription("Whether the water's flow follows a painted flow map instead of one direction for the whole body of water.")]
	public bool UseFlowMap { get; set; } = false;

	[SchemaDescription("Path to the flow map that steers the water's surface.")]
	[SchemaAsset(new[] { "rtex" }, new[] { "Assets/textures", "Assets/noise" })]
	public string FlowMapPath { get; set; } = "";

	[SchemaDescription("How strongly the water bends what is seen through it.")]
	public float RefractionStrength { get; set; } = 0.0f;

	[SchemaDescription("How strongly the moving light patterns on the bottom of shallow water are drawn.")]
	public float CausticStrength { get; set; } = 0.0f;

	[SchemaDescription("How large the caustic light patterns on the bottom are.")]
	public float CausticScale { get; set; } = 1.0f;

	[SchemaDescription("How fast the caustic light patterns on the bottom move.")]
	public float CausticSpeed { get; set; } = 1.0f;

	[SchemaDescription("The colour the water glows with, for a magical or volcanic pool.")]
	[SchemaControl(RealmSchemaControl.Color)]
	public string EmissionColorHex { get; set; } = "#000000FF";

	[SchemaDescription("How strongly the water glows in its emission colour.")]
	public float EmissionBoost { get; set; } = 0.0f;

	[SchemaDescription("The colour of the brightest part of the water's glow.")]
	[SchemaControl(RealmSchemaControl.Color)]
	public string CoreColorHex { get; set; } = "#FFE680FF";

	[SchemaDescription("The value above which the water is drawn in its core glow colour.")]
	[SchemaRange(0, 1)]
	public float CoreThreshold { get; set; } = 0.8f;

	[SchemaDescription("The colour light takes on after passing through the water, which is what gives a tropical sea its turquoise shallows.")]
	[SchemaControl(RealmSchemaControl.Color)]
	public string SubsurfaceColorHex { get; set; } = "#000000FF";

	[SchemaDescription("How strongly light takes on the subsurface colour as it passes through the water.")]
	public float SubsurfaceStrength { get; set; } = 0.0f;

	[SchemaDescription("Whether a detail texture is blended over the water's surface.")]
	public bool UseDetailTexture { get; set; } = false;

	[SchemaDescription("Path to the detail texture blended over the water's surface.")]
	[SchemaAsset(new[] { "rtex" }, new[] { "Assets/textures", "Assets/noise" })]
	public string DetailTexturePath { get; set; } = "";

	[SchemaDescription("How the detail texture is tiled over the water, such as a stochastic pattern that hides repetition.")]
	public string DetailTileMode { get; set; } = "Stochastic";

	[SchemaDescription("How many times the detail texture repeats across the water.")]
	public float DetailUvScaleX { get; set; } = 1.0f;

	[SchemaDescription("How many times the detail texture repeats along the water.")]
	public float DetailUvScaleY { get; set; } = 1.0f;

	[SchemaDescription("How fast the detail texture drifts across the water.")]
	public float DetailUvScrollX { get; set; } = 0.0f;

	[SchemaDescription("How fast the detail texture drifts along the water.")]
	public float DetailUvScrollY { get; set; } = 0.0f;

	[SchemaDescription("The size of one stochastic tile of the detail texture.")]
	[SchemaUnit("m")]
	public float DetailStochasticTileSize { get; set; } = 1.0f;

	[SchemaDescription("How softly two stochastic detail tiles blend where they meet.")]
	[SchemaRange(0, 1)]
	public float DetailCrossFade { get; set; } = 0.0f;

	[SchemaDescription("How strongly the detail texture shows through the water's own colour.")]
	[SchemaRange(0, 1)]
	public float DetailAlpha { get; set; } = 0.5f;

	[SchemaDescription("How the detail texture is blended with the water, such as multiplied, added or mixed.")]
	public int DetailBlendMode { get; set; } = 0;

	[SchemaDescription("The pathing the water provides to units crossing it, declared as named bits.")]
	[SchemaFlag(1, "ShallowWater")]
	[SchemaFlag(2, "DeepWater")]
	[SchemaFlag(4, "Flying")]
	[SchemaFlag(8, "Ground")]
	[SchemaFlag(32, "Buildable")]
	public int DefaultPathingCode { get; set; } = TerrainPathing.DefaultPathingCode_ShallowWater;
	[SchemaDescription("The decals scattered over water painted with this profile, such as foam flecks.")]
	public List<ProceduralBombingDecalRule> DecalBombingRules { get; set; } = new();

	[SchemaDescription("The visual effects scattered over water painted with this profile, such as drifting mist.")]
	public List<ProceduralBombingVfxRule> VfxBombingRules { get; set; } = new();

	public WaterProfileSaveData Clone()
	{
		var clone = new WaterProfileSaveData
		{
			Id = this.Id,
			Name = this.Name,
			ProfileIndex = this.ProfileIndex,
			ShallowColorHex = this.ShallowColorHex,
			DeepColorHex = this.DeepColorHex,
			FoamColorHex = this.FoamColorHex,
			MaxDepth = this.MaxDepth,
			FoamDepth = this.FoamDepth,
			WaveSpeed = this.WaveSpeed,
			WaveStrength = this.WaveStrength,
			UseNormalTexture = this.UseNormalTexture,
			NormalTexturePath = this.NormalTexturePath,
			NormalScale = this.NormalScale,
			FlowDirectionX = this.FlowDirectionX,
			FlowDirectionY = this.FlowDirectionY,
			FlowSpeed = this.FlowSpeed,
			UseFlowMap = this.UseFlowMap,
			FlowMapPath = this.FlowMapPath,
			RefractionStrength = this.RefractionStrength,
			CausticStrength = this.CausticStrength,
			CausticScale = this.CausticScale,
			CausticSpeed = this.CausticSpeed,
			EmissionColorHex = this.EmissionColorHex,
			EmissionBoost = this.EmissionBoost,
			CoreColorHex = this.CoreColorHex,
			CoreThreshold = this.CoreThreshold,
			SubsurfaceColorHex = this.SubsurfaceColorHex,
			SubsurfaceStrength = this.SubsurfaceStrength,
			UseDetailTexture = this.UseDetailTexture,
			DetailTexturePath = this.DetailTexturePath,
			DetailTileMode = this.DetailTileMode,
			DetailUvScaleX = this.DetailUvScaleX,
			DetailUvScaleY = this.DetailUvScaleY,
			DetailUvScrollX = this.DetailUvScrollX,
			DetailUvScrollY = this.DetailUvScrollY,
			DetailStochasticTileSize = this.DetailStochasticTileSize,
			DetailCrossFade = this.DetailCrossFade,
			DetailAlpha = this.DetailAlpha,
			DetailBlendMode = this.DetailBlendMode,
			DefaultPathingCode = this.DefaultPathingCode,
			DecalBombingRules = new List<ProceduralBombingDecalRule>(),
			VfxBombingRules = new List<ProceduralBombingVfxRule>()
		};
		if (this.DecalBombingRules != null)
		{
			foreach (var r in this.DecalBombingRules) clone.DecalBombingRules.Add(r.Clone());
		}

		if (this.VfxBombingRules != null)
		{
			foreach (var r in this.VfxBombingRules) clone.VfxBombingRules.Add(r.Clone());
		}

		return clone;
	}

	public static List<WaterProfileSaveData> CreateDefaultProfiles()
	{
		return new List<WaterProfileSaveData>
		{
			new WaterProfileSaveData
			{
				Id = "water_shallow",
				Name = "Shallow Water",
				ProfileIndex = 0,
				ShallowColorHex = "#4AD4E655",
				DeepColorHex = "#0A5C8AE0",
				FoamColorHex = "#F0FBFFC0",
				MaxDepth = 1.8f,
				FoamDepth = 0.20f,
				WaveSpeed = 1.2f,
				WaveStrength = 0.04f,
				NormalScale = 0.4f,
				RefractionStrength = 0.20f,
				CausticStrength = 0.50f,
				CausticScale = 1.5f,
				CausticSpeed = 1.2f,
				DefaultPathingCode = 1 | 4
			},
			new WaterProfileSaveData
			{
				Id = "water_deep",
				Name = "Deep Ocean",
				ProfileIndex = 1,
				ShallowColorHex = "#082B5ECC",
				DeepColorHex = "#010614FA",
				FoamColorHex = "#D0EBFFB0",
				MaxDepth = 3.0f,
				FoamDepth = 0.22f,
				WaveSpeed = 1.0f,
				WaveStrength = 0.06f,
				NormalScale = 0.1f,
				RefractionStrength = 0.08f,
				CausticStrength = 0.20f,
				CausticScale = 0.8f,
				CausticSpeed = 0.8f,
				DefaultPathingCode = 2 | 4
			}
		};
	}
}