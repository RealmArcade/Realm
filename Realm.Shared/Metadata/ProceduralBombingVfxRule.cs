namespace Realm.Shared.Metadata;

public class ProceduralBombingVfxRule
{
	public string VfxId { get; set; } = "";
	public float Density { get; set; } = 0.5f;
	public float MinScale { get; set; } = 0.2f;
	public float MaxScale { get; set; } = 0.5f;
	public float NormalOffset { get; set; } = 0.0f;
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