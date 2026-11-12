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

	/// <summary>
	///     How many pieces of the node's item one point of post-mitigation damage yields. A value of one makes every point
	///     of a worker's hit a piece; a smaller value makes the node give up its capacity more slowly than a hit can eat it.
	/// </summary>
	public float YieldPerDamage { get; set; } = 1f;
	public override float Scale { get; set; } = 2.75f;
}