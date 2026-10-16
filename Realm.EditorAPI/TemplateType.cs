namespace Realm.EditorAPI;

/// <summary>
/// Specifies the template category type in map metadata.
/// </summary>
public enum TemplateType
{
	/// <summary>
	/// Unit entities.
	/// </summary>
	Units,

	/// <summary>
	/// Building structures.
	/// </summary>
	Buildings,

	/// <summary>
	/// Harvestable or harvest-related resource nodes.
	/// </summary>
	Resources,

	/// <summary>
	/// Environmental and decorative props.
	/// </summary>
	Props,

	/// <summary>
	/// Unit and hero abilities.
	/// </summary>
	Abilities,

	/// <summary>
	/// Combat weapons and projectile definitions.
	/// </summary>
	Weapons,

	/// <summary>
	/// Tech tree and unit upgrades.
	/// </summary>
	Upgrades,

	/// <summary>
	/// Inventory items and consumables.
	/// </summary>
	Items,

	/// <summary>
	/// Visual model attachments and socket mounts.
	/// </summary>
	Attachments,

	/// <summary>
	/// Visual effect configurations.
	/// </summary>
	Vfx
}
