using Realm.Ecs.Components.Terrain;
using Realm.Shared.Serialization;

namespace Realm.Shared.Metadata;

[SchemaDescription("The pathing and bombing rules a swatch gives to the ground painted with it, without the look of the texture itself.")]
public class TerrainSwatchProfileData
{
	[SchemaDescription("The pathing the ground inherits from the swatch, declared as named bits.")]
	[SchemaFlag(1, "ShallowWater")]
	[SchemaFlag(2, "DeepWater")]
	[SchemaFlag(4, "Flying")]
	[SchemaFlag(8, "Ground")]
	[SchemaFlag(32, "Buildable")]
	public int DefaultPathingCode { get; set; } = TerrainPathing.DefaultPathingCode_Ground;

	[SchemaDescription("The decals scattered over ground painted with the swatch.")]
	public List<ProceduralBombingDecalRule> DecalBombingRules { get; set; } = new();

	[SchemaDescription("The visual effects scattered over ground painted with the swatch.")]
	public List<ProceduralBombingVfxRule> VfxBombingRules { get; set; } = new();

	public TerrainSwatchProfileData Clone()
	{
		var clone = new TerrainSwatchProfileData
		{
			DefaultPathingCode = DefaultPathingCode,
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
}
