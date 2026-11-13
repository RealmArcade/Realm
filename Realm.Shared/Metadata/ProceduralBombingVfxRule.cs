using Realm.Shared.Serialization;

namespace Realm.Shared.Metadata;

[SchemaDescription("One rule that scatters visual effects of a given template over ground painted with a swatch, with the spread of size and the distance each is lifted from the ground.")]
public class ProceduralBombingVfxRule
{
	[SchemaDescription("The visual effect scattered by the rule.")]
	[SchemaReference("Vfx")]
	public string VfxId { get; set; } = "";

	[SchemaDescription("How densely the effects are scattered over the ground, as a share of the available spots.")]
	[SchemaUnit("%")]
	[SchemaRange(0, 1)]
	public float Density { get; set; } = 0.5f;

	[SchemaDescription("The smallest size a scattered effect is placed at.")]
	public float MinScale { get; set; } = 0.2f;

	[SchemaDescription("The largest size a scattered effect is placed at.")]
	public float MaxScale { get; set; } = 0.5f;

	[SchemaDescription("How far each effect is lifted along the ground's normal, so it does not sink into the surface.")]
	[SchemaUnit("m")]
	public float NormalOffset { get; set; } = 0.0f;

	[SchemaDescription("The configuration each scattered effect is spawned with, when it should differ from the template it names.")]
	public VfxAttachmentConfig? Config { get; set; }

	public ProceduralBombingVfxRule Clone()
	{
		return new ProceduralBombingVfxRule
		{
			VfxId = this.VfxId,
			Density = this.Density,
			MinScale = this.MinScale,
			MaxScale = this.MaxScale,
			NormalOffset = this.NormalOffset,
			Config = this.Config?.Clone()
		};
	}
}
