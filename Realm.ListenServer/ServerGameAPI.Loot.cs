using System.Numerics;
using Realm.Ecs;
using Realm.Ecs.Common;
using Realm.Ecs.Components.Core;
using Realm.MapAPI;

namespace Realm.ListenServer;

/// <summary>
///     Loot and the items a unit carries, as the authority answers them. Every one of these reads and writes the
///     authoritative world, so an item on the ground, the moment it is picked up and what it does to the unit that took
///     it are one decision the clients are told about rather than one each client makes for itself.
/// </summary>
public partial class ServerGameAPI
{
	private readonly LootItemId[] _lootBuffer = new LootItemId[LootBufferCapacity];
	private const int LootBufferCapacity = 4096;

	/// <summary>Drops an item on the map, which is what a script does when a unit or a building leaves something behind.</summary>
	public LootItemId SpawnLootItem(string itemTypeId, Vector3 position, int charges = 1, float lifetime = 0f)
	{
		LootItemId lootItemId = Loot.Spawn(itemTypeId, position, charges, lifetime);
		if (lootItemId.IsValid) _log.Write($"dropped '{itemTypeId}' x{Math.Max(1, charges)} as loot {lootItemId.Value}");

		return lootItemId;
	}

	public LootItemId GetLootItemById(int uniqueId)
	{
		return Loot.TryRead(uniqueId, out _, out _) ? (LootItemId)uniqueId : LootItemId.Invalid;
	}

	/// <summary>Every item lying on the map. The buffer belongs to this surface and is refilled per call.</summary>
	public ReadOnlySpan<LootItemId> GetAllLootItems()
	{
		ReadOnlySpan<LootItemId> all = Loot.All();
		all.CopyTo(_lootBuffer);
		return new ReadOnlySpan<LootItemId>(_lootBuffer, 0, all.Length);
	}

	public ReadOnlySpan<LootItemId> GetLootItemsInRadius(Vector3 center, float radius)
	{
		ReadOnlySpan<LootItemId> withinRadius = Loot.InRadius(center, radius);
		withinRadius.CopyTo(_lootBuffer);
		return new ReadOnlySpan<LootItemId>(_lootBuffer, 0, withinRadius.Length);
	}

	/// <summary>Takes an item off the map, whether it was swept up or the map asked for it gone.</summary>
	public bool RemoveLootItem(int lootItemId) => Loot.Despawn(lootItemId);

	/// <summary>Hands a dropped item to a unit, which is what a script does when it decides who picked something up.</summary>
	public bool CollectLootItem(int lootItemId, UnitId unit)
	{
		Entity entity = ResolveUnit(unit);
		if (entity == Entity.Null) return false;

		if (!Loot.Collect(lootItemId, entity)) return false;

		OnLootItemCollected?.Invoke((LootItemId)lootItemId, unit);
		return true;
	}

	/// <summary>Hands a dropped item straight to a player's units, which is what a script does for a slot rather than a unit.</summary>
	public bool CollectLootItemByPlayer(int lootItemId, int playerIndex)
	{
		if (!Loot.CollectForPlayer(lootItemId, playerIndex)) return false;

		OnLootItemCollected?.Invoke((LootItemId)lootItemId, UnitId.Invalid);
		return true;
	}

	public string GetLootItemTypeId(LootItemId item) => Loot.TryRead(item.Value, out GroundLoot loot, out _) ? loot.ItemTypeId : string.Empty;

	public Vector3 GetLootItemPosition(LootItemId item) => Loot.TryRead(item.Value, out _, out Vector3 position) ? position : Vector3.Zero;

	public int GetLootItemCharges(LootItemId item) => Loot.TryRead(item.Value, out GroundLoot loot, out _) ? loot.Charges : 0;

	/// <summary>The lifespan an item was dropped with, which is zero for an item that never runs out.</summary>
	public float GetLootItemLifetime(LootItemId item) => Loot.TryRead(item.Value, out GroundLoot loot, out _) ? loot.Lifetime : 0f;

	public float GetLootItemPickupRadius(LootItemId item) => Loot.TryRead(item.Value, out GroundLoot loot, out _) ? loot.PickupRadius : 0f;

	public bool IsLootItemAutoPickup(LootItemId item) => Loot.TryRead(item.Value, out GroundLoot loot, out _) && loot.AutoPickup;

	public int GetLootItemOwner(LootItemId item) => Loot.TryRead(item.Value, out GroundLoot loot, out _) ? loot.OwnerPlayerIndex : -1;

	/// <summary>Whether an item is gone. A handle to an item that was taken or ran out names nothing alive.</summary>
	public bool IsLootItemDespawned(LootItemId item) => !Loot.TryRead(item.Value, out _, out _);

	/// <summary>Gives a unit an item it can carry, which is what a script does when an item is granted rather than dropped.</summary>
	public bool AddItemToUnit(UnitId unit, string itemId) => AddItemToUnitWithCharges(unit, itemId, 1);

	public bool AddItemToUnitWithCharges(UnitId unit, string itemId, int charges)
	{
		Entity entity = ResolveUnit(unit);
		if (entity == Entity.Null) return false;

		return Items.Equip(entity, itemId, charges);
	}

	public bool RemoveItemFromUnit(UnitId unit, string itemId)
	{
		Entity entity = ResolveUnit(unit);
		if (entity == Entity.Null) return false;

		return Items.Unequip(entity, itemId);
	}

	public bool UnitHasItem(UnitId unit, string itemId)
	{
		Entity entity = ResolveUnit(unit);
		if (entity == Entity.Null) return false;

		return Items.Carries(entity, itemId);
	}

	public IReadOnlyList<string> GetUnitItems(UnitId unit)
	{
		Entity entity = ResolveUnit(unit);
		if (entity == Entity.Null) return Array.Empty<string>();

		return Items.List(entity);
	}

	public int GetUnitItemCharges(UnitId unit, string itemId)
	{
		Entity entity = ResolveUnit(unit);
		if (entity == Entity.Null) return 0;

		return Items.Charges(entity, itemId);
	}

	/// <summary>
	///     Changes how many charges of an item a unit carries. An item spent down to nothing leaves the unit, which is
	///     what a consumable running out means.
	/// </summary>
	public void SetUnitItemCharges(UnitId unit, string itemId, int charges)
	{
		Entity entity = ResolveUnit(unit);
		if (entity == Entity.Null) return;

		Items.SetCharges(entity, itemId, charges);
	}
}
