using Realm.Shared.Serialization;

namespace Realm.Shared.Metadata;

[SchemaDescription("One rule that scatters decals of a given template over ground painted with a swatch, with the spread of size, angle and tint each one is given.")]
public class ProceduralBombingDecalRule
{
	[SchemaDescription("The decal template scattered by the rule.")]
	[SchemaDictionaryReference("decals")]
	public string DecalId { get; set; } = "";

	[SchemaDescription("How densely the decals are scattered over the ground, as a share of the available spots.")]
	[SchemaUnit("%")]
	[SchemaRange(0, 1)]
	public float Density { get; set; } = 0.5f;

	[SchemaDescription("The smallest size a scattered decal is placed at.")]
	public float MinScale { get; set; } = 0.2f;

	[SchemaDescription("The largest size a scattered decal is placed at.")]
	public float MaxScale { get; set; } = 0.5f;

	[SchemaDescription("The smallest angle a scattered decal is turned to.")]
	[SchemaUnit("°")]
	public float MinRotationDeg { get; set; } = 0.0f;

	[SchemaDescription("The largest angle a scattered decal is turned to.")]
	[SchemaUnit("°")]
	public float MaxRotationDeg { get; set; } = 360.0f;

	[SchemaDescription("The colour multiplied over every scattered decal, which lets one template serve several grounds.")]
	[SchemaControl(RealmSchemaControl.Color)]
	public string TintHex { get; set; } = "#FFFFFF";

	public ProceduralBombingDecalRule Clone()
	{
		return new ProceduralBombingDecalRule
		{
			DecalId = this.DecalId,
			Density = this.Density,
			MinScale = this.MinScale,
			MaxScale = this.MaxScale,
			MinRotationDeg = this.MinRotationDeg,
			MaxRotationDeg = this.MaxRotationDeg,
			TintHex = this.TintHex
		};
	}
}
