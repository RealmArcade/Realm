// Extracted from IGameAPI.cs (task 6 decomposition).
using System.Numerics;


namespace Realm.MapAPI;


/// <summary>
/// Defines the safe, public API exposed to map scripts to interact with the game engine.
/// </summary>
public partial interface IGameAPI
{
	/// <summary>
	/// Spawns a pickup loot item at the specified 3D world space coordinates.
	/// </summary>
	/// <param name="itemTypeId">The identifier of the item type from the item registry.</param>
	/// <param name="position">The coordinates in 3D world space where the item will be spawned.</param>
	/// <param name="charges">The initial charge count or stacked quantity for the item.</param>
	/// <param name="lifetime">The duration in seconds before the item automatically despawns, or 0 if indefinite.</param>
	/// <returns>The spawned loot item identifier.</returns>
	LootItemId SpawnLootItem(string itemTypeId, Vector3 position, int charges = 1, float lifetime = 0f);

	/// <summary>
	/// Spawns a pickup loot item at the specified 3D world space coordinates.
	/// </summary>
	/// <param name="itemTypeId">The identifier of the item type from the item registry.</param>
	/// <param name="position">The coordinates in 3D world space where the item will be spawned.</param>
	/// <param name="charges">The initial charge count or stacked quantity for the item.</param>
	/// <param name="lifetime">The duration in seconds before the item automatically despawns, or 0 if indefinite.</param>
	/// <returns>The spawned loot item identifier.</returns>
	LootItemId SpawnLootItemAtLocation(string itemTypeId, Vector3 position, int charges = 1, float lifetime = 0f) =>
		SpawnLootItem(itemTypeId, position, charges, lifetime);

	/// <summary>
	/// Spawns a pickup loot item at the specified named coordinate marker.
	/// </summary>
	/// <param name="itemTypeId">The identifier of the item type from the item registry.</param>
	/// <param name="coordinateName">The name of the authored coordinate marker where the item will be spawned.</param>
	/// <param name="charges">The initial charge count or stacked quantity for the item.</param>
	/// <param name="lifetime">The duration in seconds before the item automatically despawns, or 0 if indefinite.</param>
	/// <returns>The spawned loot item identifier.</returns>
	LootItemId SpawnLootItem(string itemTypeId, string coordinateName, int charges = 1, float lifetime = 0f) =>
		TryGetCoordinate(coordinateName, out var coord)
			? SpawnLootItem(itemTypeId, coord.Center, charges, lifetime)
			: LootItemId.Invalid;

	/// <summary>
	/// Spawns a pickup loot item at the specified coordinate.
	/// </summary>
	/// <param name="itemTypeId">The identifier of the item type from the item registry.</param>
	/// <param name="coordinate">The coordinate marker where the item will be spawned.</param>
	/// <param name="charges">The initial charge count or stacked quantity for the item.</param>
	/// <param name="lifetime">The duration in seconds before the item automatically despawns, or 0 if indefinite.</param>
	/// <returns>The spawned loot item identifier.</returns>
	LootItemId SpawnLootItem(string itemTypeId, Coordinate coordinate, int charges = 1, float lifetime = 0f) =>
		SpawnLootItem(itemTypeId, coordinate.Center, charges, lifetime);

	/// <summary>
	/// Retrieves an active ground loot item by its unique runtime identifier.
	/// </summary>
	/// <param name="uniqueId">The unique ID of the loot item entity.</param>
	/// <returns>The loot item identifier.</returns>
	LootItemId GetLootItemById(int uniqueId);

	/// <summary>
	/// Retrieves all active ground loot items in the game world.
	/// </summary>
	/// <returns>A read-only span of all active ground loot item identifiers.</returns>
	ReadOnlySpan<LootItemId> GetAllLootItems();

	/// <summary>
	/// Retrieves all active ground loot items within the specified radial distance of a 3D position.
	/// </summary>
	/// <param name="center">The center point in 3D world space coordinates.</param>
	/// <param name="radius">The radial distance check.</param>
	/// <returns>A read-only span of ground loot item identifiers within the radius.</returns>
	ReadOnlySpan<LootItemId> GetLootItemsInRadius(Vector3 center, float radius);

	/// <summary>
	/// Removes and despawns a ground loot item from the game world by its unique identifier.
	/// </summary>
	/// <param name="lootItemId">The unique identifier of the loot item to remove.</param>
	/// <returns>True if the loot item was found and removed; otherwise, false.</returns>
	bool RemoveLootItem(int lootItemId);

	/// <summary>
	/// Despawns and removes a ground loot item from the game world by its unique identifier.
	/// </summary>
	/// <param name="lootItemId">The unique identifier of the loot item to despawn.</param>
	/// <returns>True if the loot item was found and despawned; otherwise, false.</returns>
	bool DespawnLootItem(int lootItemId) => RemoveLootItem(lootItemId);

	/// <summary>
	/// Attempts to collect a ground loot item using the specified unit.
	/// </summary>
	/// <param name="lootItemId">The unique identifier of the ground loot item to collect.</param>
	/// <param name="unit">The collecting unit.</param>
	/// <returns>True if the unit successfully collected the loot item into inventory; otherwise, false.</returns>
	bool CollectLootItem(int lootItemId, UnitId unit);

	/// <summary>
	/// Attempts to collect a ground loot item using the most suitable eligible unit owned by the specified player.
	/// </summary>
	/// <param name="lootItemId">The unique identifier of the ground loot item to collect.</param>
	/// <param name="playerIndex">The zero-based player slot index whose unit should collect the item.</param>
	/// <returns>True if an eligible unit owned by the player successfully collected the item; otherwise, false.</returns>
	bool CollectLootItemByPlayer(int lootItemId, int playerIndex);

	/// <summary>
	/// Retrieves the item type identifier of the specified ground loot item.
	/// </summary>
	/// <param name="item">The loot item identifier.</param>
	/// <returns>The item type identifier.</returns>
	string GetLootItemTypeId(LootItemId item);

	/// <summary>
	/// Retrieves the 3D world space position of the specified ground loot item.
	/// </summary>
	/// <param name="item">The loot item identifier.</param>
	/// <returns>The 3D coordinates of the loot item.</returns>
	Vector3 GetLootItemPosition(LootItemId item);

	/// <summary>
	/// Retrieves the remaining charge count for the specified ground loot item.
	/// </summary>
	/// <param name="item">The loot item identifier.</param>
	/// <returns>The charge count.</returns>
	int GetLootItemCharges(LootItemId item);

	/// <summary>
	/// Retrieves the configured lifespan duration of the specified ground loot item in seconds.
	/// </summary>
	/// <param name="item">The loot item identifier.</param>
	/// <returns>The lifespan duration in seconds.</returns>
	float GetLootItemLifetime(LootItemId item);

	/// <summary>
	/// Retrieves the pickup trigger radius of the specified ground loot item in world units.
	/// </summary>
	/// <param name="item">The loot item identifier.</param>
	/// <returns>The pickup trigger radius.</returns>
	float GetLootItemPickupRadius(LootItemId item);

	/// <summary>
	/// Returns true if the specified ground loot item automatically triggers pickup upon proximity.
	/// </summary>
	/// <param name="item">The loot item identifier.</param>
	/// <returns>True if auto-pickup is enabled; otherwise, false.</returns>
	bool IsLootItemAutoPickup(LootItemId item);

	/// <summary>
	/// Retrieves the owning player slot index restricted to collect the specified loot item, or -1 for unowned items.
	/// </summary>
	/// <param name="item">The loot item identifier.</param>
	/// <returns>The owning player slot index, or -1.</returns>
	int GetLootItemOwner(LootItemId item);

	/// <summary>
	/// Returns true if the specified ground loot item has already despawned.
	/// </summary>
	/// <param name="item">The loot item identifier.</param>
	/// <returns>True if despawned; otherwise, false.</returns>
	bool IsLootItemDespawned(LootItemId item);
}
