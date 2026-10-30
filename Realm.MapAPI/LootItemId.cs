using System.Numerics;
using Realm.Ecs;

namespace Realm.MapAPI;

/// <summary>
/// Adds the map script surface of a dropped loot item: what the item is, where it lies, who may take it and how it
/// leaves the world.
/// </summary>
/// <remarks>
/// The handle itself is a plain entity index owned by the shared simulation, so the simulation can name loot without
/// the script surface. Reads answer straight out of the world the process is simulating and writes route through the
/// authoritative host imports, which is what keeps a map script unable to invent state of its own.
/// </remarks>
public static class LootItemIdScriptExtensions
{
	extension(LootItemId item)
	{
		/// <summary>
		/// Gets the item archetype identifier of this dropped item.
		/// </summary>
		public string ItemTypeId => GameAPI.Current.GetLootItemTypeId(item);

		/// <summary>
		/// Gets the current position of the loot item in 3D world space.
		/// </summary>
		public Vector3 Position => GameAPI.Current.GetLootItemPosition(item);

		/// <summary>
		/// Gets the remaining charge count for consumable loot items.
		/// </summary>
		public int Charges => GameAPI.Current.GetLootItemCharges(item);

		/// <summary>
		/// Gets the configured lifespan duration of the loot item in seconds.
		/// </summary>
		public float Lifetime => GameAPI.Current.GetLootItemLifetime(item);

		/// <summary>
		/// Gets the world-space pickup trigger radius for this loot item.
		/// </summary>
		public float PickupRadius => GameAPI.Current.GetLootItemPickupRadius(item);

		/// <summary>
		/// Gets a value indicating whether this item automatically triggers collection upon proximity.
		/// </summary>
		public bool AutoPickup => GameAPI.Current.IsLootItemAutoPickup(item);

		/// <summary>
		/// Gets the owning player slot index restricted to collect this item, or -1 for unowned items.
		/// </summary>
		public int OwnerPlayerIndex => GameAPI.Current.GetLootItemOwner(item);

		/// <summary>
		/// Gets a value indicating whether this loot item has already despawned.
		/// </summary>
		public bool IsDespawned => GameAPI.Current.IsLootItemDespawned(item);

		/// <summary>
		/// Removes and despawns the loot item from the game world.
		/// </summary>
		public void Despawn() => GameAPI.Current.RemoveLootItem(item.Value);
	}
}
