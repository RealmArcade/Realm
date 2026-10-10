namespace Realm.Shared.Metadata;

public class ProceduralBombingDecalRule
{
	public string DecalId { get; set; } = "";
	public float Density { get; set; } = 0.5f;
	public float MinScale { get; set; } = 0.2f;
	public float MaxScale { get; set; } = 0.5f;
	public float MinRotationDeg { get; set; } = 0.0f;
	public float MaxRotationDeg { get; set; } = 360.0f;
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