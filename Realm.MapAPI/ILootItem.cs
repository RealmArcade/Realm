using System.Numerics;

namespace Realm.MapAPI;

/// <summary>
/// Represents a pickup loot item resting on the ground in the game world that can be collected by units.
/// </summary>
public interface ILootItem
{
	/// <summary>
	/// Gets the unique runtime identifier of this loot item entity.
	/// </summary>
	int UniqueId { get; }

	/// <summary>
	/// Gets the item type identifier corresponding to the item definition in the item registry.
	/// </summary>
	string ItemTypeId { get; }

	/// <summary>
	/// Gets the position of this loot item in 3D world space coordinates.
	/// </summary>
	Vector3 Position { get; }

	/// <summary>
	/// Gets the number of charges or stacked quantity granted to the collecting unit's inventory.
	/// </summary>
	int Charges { get; }

	/// <summary>
	/// Gets the initial lifetime duration in seconds before this item automatically despawns, or 0 if it persists indefinitely.
	/// </summary>
	float Lifetime { get; }

	/// <summary>
	/// Gets the remaining lifetime duration in seconds before this item automatically despawns.
	/// </summary>
	float RemainingLifetime { get; }

	/// <summary>
	/// Gets or sets the radial distance in world units within which eligible units can pick up this item.
	/// </summary>
	float PickupRadius { get; set; }

	/// <summary>
	/// Gets or sets a value indicating whether eligible units within range automatically collect this item.
	/// </summary>
	bool AutoPickup { get; set; }

	/// <summary>
	/// Gets or sets the zero-based player slot index restricted to collect this item, or -1 if any player's units may collect it.
	/// </summary>
	int OwnerPlayerIndex { get; set; }

	/// <summary>
	/// Gets a value indicating whether this loot item has been collected or removed from the game world.
	/// </summary>
	bool IsDespawned { get; }

	/// <summary>
	/// Despawns and removes this loot item from the game world.
	/// </summary>
	void Despawn();
}
