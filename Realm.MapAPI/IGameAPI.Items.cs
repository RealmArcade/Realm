// Extracted from IGameAPI.cs (task 6 decomposition).
using System.Numerics;


namespace Realm.MapAPI;


/// <summary>
/// Defines the safe, public API exposed to map scripts to interact with the game engine.
/// </summary>
public partial interface IGameAPI
{
	/// <summary>
	/// Adds an item archetype to the specified unit's inventory. An item granted this way is bound to the unit that
	/// received it: it stays with that unit and cannot be put down, unlike an item collected from the ground.
	/// </summary>
	/// <param name="unit">The unit identifier.</param>
	/// <param name="itemId">The item definition identifier.</param>
	/// <returns>True if added successfully; otherwise, false.</returns>
	bool AddItemToUnit(UnitId unit, string itemId);

	/// <summary>
	/// Adds an item archetype to the specified unit's inventory with a stack of the given charge count. The granted
	/// pieces are bound to the unit that received them, so they cannot be put down.
	/// </summary>
	/// <param name="unit">The unit identifier.</param>
	/// <param name="itemId">The item definition identifier.</param>
	/// <param name="charges">The number of stacked charges to grant; values below one are treated as one.</param>
	/// <returns>True if added successfully; otherwise, false.</returns>
	bool AddItemToUnitWithCharges(UnitId unit, string itemId, int charges);

	/// <summary>
	/// Removes an item archetype from the specified unit's inventory.
	/// </summary>
	/// <param name="unit">The unit identifier.</param>
	/// <param name="itemId">The item definition identifier.</param>
	/// <returns>True if removed successfully; otherwise, false.</returns>
	bool RemoveItemFromUnit(UnitId unit, string itemId);

	/// <summary>
	/// Returns true if the specified unit's inventory carries the item.
	/// </summary>
	/// <param name="unit">The unit identifier.</param>
	/// <param name="itemId">The item definition identifier.</param>
	/// <returns>True if carried; otherwise, false.</returns>
	bool UnitHasItem(UnitId unit, string itemId);

	/// <summary>
	/// Retrieves the item archetype identifiers currently held in the unit's inventory, naming each item once even when
	/// its charges spill across several slots.
	/// </summary>
	/// <param name="unit">The unit identifier.</param>
	/// <returns>A read-only collection of the distinct item archetype identifiers, in the order they were first carried.</returns>
	/// <remarks>
	/// Read the number of charges of a named item with <see cref="GetUnitItemCharges" />: the total it holds, not what one
	/// slot of it holds, so copying a unit's items is a walk over this list and one charge read per entry.
	/// </remarks>
	IReadOnlyList<string> GetUnitItems(UnitId unit);

	/// <summary>
	/// Returns the stacked charge count of an item in the specified unit's inventory.
	/// </summary>
	/// <param name="unit">The unit identifier.</param>
	/// <param name="itemId">The item definition identifier.</param>
	/// <returns>The charge count, or zero when the unit does not carry the item.</returns>
	int GetUnitItemCharges(UnitId unit, string itemId);

	/// <summary>
	/// Sets the stacked charge count of an item in the specified unit's inventory and reapplies its grants.
	/// </summary>
	/// <param name="unit">The unit identifier.</param>
	/// <param name="itemId">The item definition identifier.</param>
	/// <param name="charges">The new charge count; zero removes the item's grants.</param>
	void SetUnitItemCharges(UnitId unit, string itemId, int charges);

	/// <summary>
	/// Returns true if the specified unit currently has an active buff with the given identifier.
	/// </summary>
	/// <param name="unit">The unit identifier.</param>
	/// <param name="buffId">The buff identifier.</param>
	/// <returns>True if active; otherwise, false.</returns>
	bool UnitHasBuff(UnitId unit, string buffId);
}
