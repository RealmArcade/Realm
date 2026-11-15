using Realm.Shared.Serialization;

namespace Realm.Shared.Metadata;

[SchemaDescription("One item a unit can carry or buy: a consumable, a relic, a piece of equipment or a container. Its card, its price, what it grants and where it may be used live here.")]
public class ItemMetadata
{
	[SchemaDescription("The identifier every other template, shop entry and script uses to name this item.")]
	[SchemaSensitive]
	public string TemplateID { get; set; } = string.Empty;

	[SchemaDescription("The name the item shows on its command card and in its tooltip.")]
	public string Name { get; set; } = string.Empty;

	[SchemaDescription("The tooltip text explaining what the item does when a player hovers over it.")]
	[SchemaControl(RealmSchemaControl.Textarea)]
	public string Description { get; set; } = string.Empty;

	[SchemaDescription("The class the item belongs to, which decides how shops group it and how many a unit may hold at once.")]
	public string ItemClass { get; set; } = "consumable";

	[SchemaDescription("Gold a player pays for the item. Zero makes it free.")]
	public float CostGold { get; set; }

	[SchemaDescription("Wood a player pays for the item. Zero makes it cost no wood.")]
	public float CostWood { get; set; }

	[SchemaDescription("Stone a player pays for the item. Zero makes it cost no stone.")]
	public float CostStone { get; set; }

	[SchemaDescription("Which units the item may be used on, such as the carrier alone or any friendly unit.")]
	public string TargetScope { get; set; } = "OwnUnit";

	[SchemaDescription("The slot group the item occupies, so two items of the same group cannot be carried at the same time.")]
	public string SlotGroup { get; set; } = "consumable";

	[SchemaDescription("How many of the item one unit may carry at once.")]
	public int MaxPerUnit { get; set; } = 1;

	[SchemaDescription("Whether using the item empties the slot it sat in, which is what a consumable does and a relic does not.")]
	public bool ClearsSlot { get; set; }

	[SchemaDescription("How close the buying unit has to be to a shop before it may purchase the item.")]
	[SchemaUnit("m")]
	public float PurchaseRange { get; set; } = 4f;

	[SchemaDescription("The ability the item casts when it is used.")]
	[SchemaReference("Abilities")]
	public string? UseAbility { get; set; }

	[SchemaDescription("The ability whose card, cooldown and effects the item presents, when the item is the face of an ability rather than a cast of its own.")]
	[SchemaReference("Abilities")]
	public string? AbilityId { get; set; }

	[SchemaDescription("The name the item's card is keyed by, which is its ability when it has one and its own identifier otherwise.")]
	public string CardId => string.IsNullOrWhiteSpace(AbilityId) ? TemplateID : AbilityId;

	[SchemaDescription("How many charges the item arrives with. A charge is spent on use and the item is gone when the last one is.")]
	public int ChargeCount { get; set; }

	[SchemaDescription("How many times the item may be bought and stacked on one unit before the shop refuses further copies.")]
	public int MaxStacks { get; set; } = 1;

	[SchemaDescription("The resource the item stands for when it is a gathered piece rather than equipment, such as gold or wood.")]
	public string? ResourceType { get; set; }

	[SchemaDescription("An ability whose cooldown this item shares, so using one puts both on cooldown.")]
	[SchemaReference("Abilities")]
	public string? CooldownLink { get; set; }

	[SchemaDescription("Whether a unit may drop the item on the ground for another unit to pick up.")]
	public bool CanDrop { get; set; }

	[SchemaDescription("The level the item counts as, which decides which shops stock it and which prerequisites accept it.")]
	public int ItemLevel { get; set; }

	[SchemaDescription("Path to the icon shown for the item on its command card.")]
	[SchemaAsset(new[] { "rtex" }, new[] { "Assets/icons" })]
	public string? IconPath { get; set; }

	[SchemaDescription("Path to the model shown for the item both as it lies on the ground and as a command card preview.")]
	[SchemaAsset(new[] { "rmesh" }, new[] { "Assets/models/items" })]
	public string? ModelPath { get; set; }

	[SchemaDescription("Status effects the carrier keeps for as long as it holds the item.")]
	public string[]? PassiveStatusEffects { get; set; }

	[SchemaDescription("Weapons holding the item adds to the carrier's attack.")]
	[SchemaReference("Weapons")]
	public string[]? GrantedWeapons { get; set; }

	[SchemaDescription("Abilities holding the item adds to the carrier's command card.")]
	[SchemaReference("Abilities")]
	public string[]? GrantedAbilities { get; set; }

	[SchemaDescription("Upgrades holding the item applies to its owner for as long as it is carried.")]
	[SchemaReference("Upgrades")]
	public string[]? GrantedUpgrades { get; set; }

	[SchemaDescription("Whether the item is a container that holds other items instead of being used up itself.")]
	public bool IsContainer { get; set; }

	[SchemaDescription("How many items a container item can hold at once.")]
	public int ContainerSize { get; set; }

	[SchemaDescription("The requirement expression that has to hold before a unit may buy or carry the item.")]
	public string Requirements { get; set; } = string.Empty;

	[SchemaDescription("The map the item was imported from, kept so attribution survives a round trip through the editor.")]
	public string? Attribution_ImportedFromMap { get; set; }
}
