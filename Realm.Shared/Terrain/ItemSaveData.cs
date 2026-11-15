using Realm.Shared.Serialization;

namespace Realm.Shared.Terrain;

/// <summary>
///     One item the map places on the ground, waiting for a unit to walk over and collect it. The entry names the item
///     template and where it lies; the item's own template states what it looks like and how much of it one pickup grants.
/// </summary>
[SchemaDescription("One item the map places on the ground, waiting for a unit to walk over and collect it.")]
public class ItemSaveData
{
	[SchemaDescription("The item template lying here.")]
	[SchemaReference("Items", "Consumables")]
	public string TemplateId { get; set; } = string.Empty;

	[SchemaDescription("Where the item lies across the map.")]
	[SchemaUnit("m")]
	public float PosX { get; set; }

	[SchemaDescription("How high above the ground the item lies.")]
	[SchemaUnit("m")]
	public float PosY { get; set; }

	[SchemaDescription("Where the item lies along the map.")]
	[SchemaUnit("m")]
	public float PosZ { get; set; }
}
