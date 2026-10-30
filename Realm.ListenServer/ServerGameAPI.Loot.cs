using System.Numerics;
using Realm.Ecs;
using Realm.Ecs.Common;
using Realm.Ecs.Components.Core;
using Realm.MapAPI;

namespace Realm.ListenServer;

public partial class ServerGameAPI
{
	private readonly LootItemId[] _lootBuffer = new LootItemId[LootBufferCapacity];
	private const int LootBufferCapacity = 4096;

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

	public bool RemoveLootItem(int lootItemId) => Loot.Despawn(lootItemId);

	public bool CollectLootItem(int lootItemId, UnitId unit)
	{
		Entity entity = ResolveUnit(unit);
		if (entity == Entity.Null) return false;

		if (!Loot.Collect(lootItemId, entity)) return false;

		OnLootItemCollected?.Invoke((LootItemId)lootItemId, unit);
		return true;
	}

	public bool CollectLootItemByPlayer(int lootItemId, int playerIndex)
	{
		if (!Loot.CollectForPlayer(lootItemId, playerIndex)) return false;

		OnLootItemCollected?.Invoke((LootItemId)lootItemId, UnitId.Invalid);
		return true;
	}

	public string GetLootItemTypeId(LootItemId item) => Loot.TryRead(item.Value, out GroundLoot loot, out _) ? loot.ItemTypeId : string.Empty;

	public Vector3 GetLootItemPosition(LootItemId item) => Loot.TryRead(item.Value, out _, out Vector3 position) ? position : Vector3.Zero;

	public int GetLootItemCharges(LootItemId item) => Loot.TryRead(item.Value, out GroundLoot loot, out _) ? loot.Charges : 0;

	public float GetLootItemLifetime(LootItemId item) => Loot.TryRead(item.Value, out GroundLoot loot, out _) ? loot.Lifetime : 0f;

	public float GetLootItemPickupRadius(LootItemId item) => Loot.TryRead(item.Value, out GroundLoot loot, out _) ? loot.PickupRadius : 0f;

	public bool IsLootItemAutoPickup(LootItemId item) => Loot.TryRead(item.Value, out GroundLoot loot, out _) && loot.AutoPickup;

	public int GetLootItemOwner(LootItemId item) => Loot.TryRead(item.Value, out GroundLoot loot, out _) ? loot.OwnerPlayerIndex : -1;

	public bool IsLootItemDespawned(LootItemId item) => !Loot.TryRead(item.Value, out _, out _);

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

	public void SetUnitItemCharges(UnitId unit, string itemId, int charges)
	{
		Entity entity = ResolveUnit(unit);
		if (entity == Entity.Null) return;

		Items.SetCharges(entity, itemId, charges);
	}
}
