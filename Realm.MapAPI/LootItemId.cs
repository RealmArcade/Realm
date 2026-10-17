using System.Numerics;

namespace Realm.MapAPI;

/// <summary>
/// Represents a strongly-typed, unmanaged entity identifier for a dropped loot item in the game world,
/// providing zero-allocation fluent access to loot properties and actions.
/// </summary>
public readonly record struct LootItemId(int Value)
{
	/// <summary>
	/// Implicitly converts a 32-bit integer identifier into a <see cref="LootItemId"/>.
	/// </summary>
	/// <param name="value">The raw integer loot item identifier.</param>
	public static implicit operator LootItemId(int value) => new(value);

	/// <summary>
	/// Explicitly converts a <see cref="LootItemId"/> into a 32-bit integer identifier.
	/// </summary>
	/// <param name="id">The loot item identifier.</param>
	public static explicit operator int(LootItemId id) => id.Value;

	/// <summary>
	/// Gets an invalid or unassigned loot item identifier.
	/// </summary>
	public static readonly LootItemId Invalid = new(0);

	/// <summary>
	/// Gets a value indicating whether this identifier represents a valid loot item index.
	/// </summary>
	public bool IsValid => Value > 0;

	/// <summary>
	/// Gets the raw integer identifier.
	/// </summary>
	public int UniqueId => Value;

	/// <summary>
	/// Gets the raw integer identifier (alias for <see cref="UniqueId"/>).
	/// </summary>
	public int Id => Value;

	/// <summary>
	/// Gets the item archetype identifier of this dropped item.
	/// </summary>
	public string ItemTypeId => GameAPI.Current.GetLootItemTypeId(this);

	/// <summary>
	/// Gets the current position of the loot item in 3D world space.
	/// </summary>
	public Vector3 Position => GameAPI.Current.GetLootItemPosition(this);

	/// <summary>
	/// Gets the remaining charge count for consumable loot items.
	/// </summary>
	public int Charges => GameAPI.Current.GetLootItemCharges(this);

	/// <summary>
	/// Gets the configured lifespan duration of the loot item in seconds.
	/// </summary>
	public float Lifetime => GameAPI.Current.GetLootItemLifetime(this);

	/// <summary>
	/// Gets the world-space pickup trigger radius for this loot item.
	/// </summary>
	public float PickupRadius => GameAPI.Current.GetLootItemPickupRadius(this);

	/// <summary>
	/// Gets a value indicating whether this item automatically triggers collection upon proximity.
	/// </summary>
	public bool AutoPickup => GameAPI.Current.IsLootItemAutoPickup(this);

	/// <summary>
	/// Gets the owning player slot index restricted to collect this item, or -1 for unowned items.
	/// </summary>
	public int OwnerPlayerIndex => GameAPI.Current.GetLootItemOwner(this);

	/// <summary>
	/// Gets a value indicating whether this loot item has already despawned.
	/// </summary>
	public bool IsDespawned => GameAPI.Current.IsLootItemDespawned(this);

	/// <summary>
	/// Removes and despawns the loot item from the game world.
	/// </summary>
	public void Despawn() => GameAPI.Current.RemoveLootItem(Value);

	/// <summary>
	/// Formats the loot item identifier as a string.
	/// </summary>
	/// <returns>A string representation of the loot item identifier.</returns>
	public override string ToString() => Value.ToString();
}
