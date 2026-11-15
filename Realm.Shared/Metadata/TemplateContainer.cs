using System.Text.Json.Serialization;
using Realm.Shared.Serialization;

namespace Realm.Shared.Metadata;

[SchemaDescription("The shelves of a map's templates, one list per kind of thing a map can define. Each list is a category, and a member that names a record names the category it lives in.")]
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public class TemplateContainer
{
	[JsonPropertyName("Units")]
	[SchemaDescription("The units a player trains and orders around.")]
	public List<UnitMetadata> Units { get; set; } = new();

	[JsonPropertyName("Buildings")]
	[SchemaDescription("The buildings a player raises, which are units that stay where they are put.")]
	public List<UnitMetadata> Buildings { get; set; } = new();

	[JsonPropertyName("Resources")]
	[SchemaDescription("The gatherable nodes workers harvest from, such as trees, gold veins and stone outcrops.")]
	public List<ResourceMetadata> Resources { get; set; } = new();

	[JsonPropertyName("Props")]
	[SchemaDescription("The decorative scenery a map places for looks or to block movement.")]
	public List<PropMetadata> Props { get; set; } = new();

	[JsonPropertyName("Abilities")]
	[SchemaDescription("The abilities units cast from their command cards.")]
	public List<AbilityMetadata> Abilities { get; set; } = new();

	[JsonPropertyName("Weapons")]
	[SchemaDescription("The weapons units attack with, including the projectiles they fly.")]
	public List<WeaponMetadata> Weapons { get; set; } = new();

	[JsonPropertyName("Upgrades")]
	[SchemaDescription("The upgrades a player researches to change their army.")]
	public List<UpgradeMetadata> Upgrades { get; set; } = new();

	[JsonPropertyName("Items")]
	[SchemaDescription("The items units carry and shops sell.")]
	public List<ItemMetadata> Items { get; set; } = new();

	[JsonPropertyName("Consumables")]
	[SchemaDescription("The consumable items that are used up, kept apart from carried equipment so a shop can list them separately.")]
	public List<ItemMetadata> Consumables { get; set; } = new();

	[JsonPropertyName("Attachments")]
	[SchemaDescription("The models units carry in their hands or on named sockets.")]
	public List<AttachmentMetadata> Attachments { get; set; } = new();

	[JsonPropertyName("Vfx")]
	[SchemaDescription("The visual effects abilities, weapons, attachments and the map's script spawn.")]
	public List<VfxAttachmentConfig> Vfx { get; set; } = new();
}
