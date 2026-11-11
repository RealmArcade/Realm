namespace Realm.Shared.Metadata;

public class ResourceMetadata : PlaceableMetadata
{
	public float MaxCapacity { get; set; }
	public float HarvestRate { get; set; }
	public float GrowthRate { get; set; }
	public int MaxWorkers { get; set; }
	public int Level { get; set; } = 1;
	public float MaxCapacityPerLevel { get; set; }
	public float Armor { get; set; }
	public float ArmorPerLevel { get; set; }
	public float MagicResistance { get; set; }
	public float MagicResistancePerLevel { get; set; }
	public string ResourceType { get; set; } = string.Empty;
	public string ItemTemplateId { get; set; } = string.Empty;
	public override float Scale { get; set; } = 2.75f;
}