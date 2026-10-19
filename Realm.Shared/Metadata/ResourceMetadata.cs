namespace Realm.Shared.Metadata;

public class ResourceMetadata : PlaceableMetadata
{
	public float MaxCapacity { get; set; }
	public float HarvestRate { get; set; }
	public float GrowthRate { get; set; }
	public int MaxWorkers { get; set; }
	public override float Scale { get; set; } = 2.75f;
}