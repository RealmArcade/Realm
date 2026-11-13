using Realm.Shared.Serialization;

namespace Realm.Shared.Metadata;

[SchemaDescription("A gatherable node such as a tree, a gold vein or a stone outcrop: how much it holds, how fast workers empty it and how it grows back.")]
public class ResourceMetadata : PlaceableMetadata
{
	[SchemaDescription("How much of its resource the node holds when a match starts, before any per-level bonus.")]
	public float MaxCapacity { get; set; }

	[SchemaDescription("How much a worker takes from the node per harvest swing.")]
	public float HarvestRate { get; set; }

	[SchemaDescription("How much the node regrows per second while it is not being harvested. Zero means it never comes back.")]
	public float GrowthRate { get; set; }

	[SchemaDescription("How many workers may harvest this node at the same time.")]
	public int MaxWorkers { get; set; }

	[SchemaDescription("The level the node starts at, which its capacity and armour per level are multiplied by.")]
	public int Level { get; set; } = 1;

	[SchemaDescription("How much capacity every level above the first adds to the node.")]
	public float MaxCapacityPerLevel { get; set; }

	[SchemaDescription("Flat damage reduction the node applies to every hit, so a weak worker takes longer to empty it.")]
	public float Armor { get; set; }

	[SchemaDescription("How much armour every level above the first adds to the node.")]
	public float ArmorPerLevel { get; set; }

	[SchemaDescription("Flat reduction the node applies to magical damage.")]
	public float MagicResistance { get; set; }

	[SchemaDescription("How much magic resistance every level above the first adds to the node.")]
	public float MagicResistancePerLevel { get; set; }

	[SchemaDescription("The kind of resource the node yields, such as gold, wood or stone.")]
	public string ResourceType { get; set; } = string.Empty;

	[SchemaDescription("The item template one harvested piece of the node becomes when a worker carries it home.")]
	[SchemaReference("Items", "Consumables")]
	public string ItemTemplateId { get; set; } = string.Empty;

	[SchemaDescription("How many pieces of the node's item one point of damage after mitigation yields. One makes every point of a hit a piece; a smaller value makes the node give up its capacity more slowly than a hit can eat it.")]
	public float YieldPerDamage { get; set; } = 1f;

	[SchemaDescription("The multiplier applied to the node's model size, so a tree canopy or a boulder sits at the size the map wants.")]
	public override float Scale { get; set; } = 2.75f;
}
