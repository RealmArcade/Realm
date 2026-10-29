using System.Numerics;
using Realm.Ecs;
using Realm.Ecs.Common;
using Realm.Ecs.Components.Combat;
using Realm.Ecs.Components.Core;
using Realm.Ecs.Components.Tags;
using Realm.Ecs.Components.Terrain;
using Realm.Ecs.Services;

namespace Realm.Shared.Simulation;

public class GroundLootService
{
	public const float DefaultPickupRadius = 2.0f;

	private const float GroundOffset = 0.1f;

	private const int LootBufferCapacity = 4096;

	private static readonly QueryDescription AllLootQuery = new QueryDescription().WithAll<GroundLoot, Position>();
	private static readonly QueryDescription CandidateQuery = new QueryDescription().WithAll<Position, Owner, Health>().WithNone<Dead, Building>();

	private readonly SimulationContext _context;
	private readonly ItemGrantService _items;

	private readonly LootItemId[] _lootBuffer = new LootItemId[LootBufferCapacity];
	private readonly List<Entity> _expired = new();
	private readonly List<Entity> _sweepResult = new();
	private readonly EntityCallback<GroundLoot, Position> _collectAll;
	private readonly EntityCallback<Position, Owner, Health> _pickupCandidates;

	private Vector3 _pickupCenter;
	private float _pickupRadiusSquared;
	private int _collectedCount;
	private string _pickupItemTypeId = string.Empty;
	private int _pickupOwnerPlayerIndex = -1;

	public GroundLootService(SimulationContext context, ItemGrantService items)
	{
		_context = context;
		_items = items;
		_collectAll = CollectAll;
		_pickupCandidates = CollectPickupCandidates;
	}

	public EcsWorld World => _context.World;

	public LootItemId Spawn(string itemTypeId, Vector3 position, int charges = 1, float lifetime = 0f, float pickupRadius = DefaultPickupRadius, bool autoPickup = true, int ownerPlayerIndex = -1)
	{
		if (string.IsNullOrWhiteSpace(itemTypeId)) return LootItemId.Invalid;

		if (_context.WorldEntity != Entity.Null && World.IsAlive(_context.WorldEntity) && World.Has<TerrainState>(_context.WorldEntity))
		{
			_context.TerrainNavMeshService.GetHeightAndNormal(World.Get<TerrainState>(_context.WorldEntity), position.X, position.Z, out float groundHeight, out _);
			position = new Vector3(position.X, groundHeight + GroundOffset, position.Z);
		}

		var loot = new GroundLoot(0, itemTypeId, Math.Max(1, charges), Math.Max(0f, lifetime), pickupRadius, autoPickup, ownerPlayerIndex);
		Entity entity = World.Create(new Position(position), loot);

		ref GroundLoot placed = ref World.Get<GroundLoot>(entity);
		placed.UniqueId = entity.Id;
		return (LootItemId)entity.Id;
	}

	public bool TryRead(int lootItemId, out GroundLoot loot, out Vector3 position)
	{
		loot = default;
		position = Vector3.Zero;
		if (lootItemId <= 0) return false;

		var entity = new Entity(lootItemId);
		if (!World.IsAlive(entity)) return false;
		if (!World.Has<GroundLoot>(entity)) return false;

		loot = World.Get<GroundLoot>(entity);
		position = World.Has<Position>(entity) ? World.Get<Position>(entity).Value : Vector3.Zero;
		return true;
	}

	public ReadOnlySpan<LootItemId> All()
	{
		_collectedCount = 0;
		World.Query(in AllLootQuery, _collectAll);
		return new ReadOnlySpan<LootItemId>(_lootBuffer, 0, _collectedCount);
	}

	public ReadOnlySpan<LootItemId> InRadius(Vector3 center, float radius)
	{
		ReadOnlySpan<LootItemId> all = All();
		_pickupCenter = center;
		_pickupRadiusSquared = radius * radius;

		int kept = 0;
		for (int index = 0; index < all.Length; index++)
		{
			if (!TryRead(all[index].Value, out _, out Vector3 position)) continue;
			if (Vector3.DistanceSquared(position, center) > _pickupRadiusSquared) continue;

			_lootBuffer[kept++] = all[index];
		}

		return new ReadOnlySpan<LootItemId>(_lootBuffer, 0, kept);
	}

	public bool Despawn(int lootItemId)
	{
		if (lootItemId <= 0) return false;

		var entity = new Entity(lootItemId);
		if (!World.IsAlive(entity) || !World.Has<GroundLoot>(entity)) return false;

		World.Destroy(entity);
		return true;
	}

	public bool Collect(int lootItemId, Entity unit)
	{
		if (!TryRead(lootItemId, out GroundLoot loot, out _)) return false;
		if (!World.IsAlive(unit) || World.Has<Dead>(unit) || World.Has<Building>(unit)) return false;

		int ownerPlayerIndex = OwnerPlayerIndex(unit);
		if (loot.OwnerPlayerIndex >= 0 && ownerPlayerIndex != loot.OwnerPlayerIndex) return false;

		if (!_items.Equip(unit, loot.ItemTypeId, loot.Charges)) return false;

		Despawn(lootItemId);
		return true;
	}

	public bool CollectForPlayer(int lootItemId, int playerIndex)
	{
		if (!TryRead(lootItemId, out GroundLoot loot, out Vector3 position)) return false;
		if (loot.OwnerPlayerIndex >= 0 && loot.OwnerPlayerIndex != playerIndex) return false;

		_sweepResult.Clear();
		_pickupCenter = position;
		_pickupItemTypeId = loot.ItemTypeId;
		_pickupOwnerPlayerIndex = playerIndex;
		World.Query(in CandidateQuery, _pickupCandidates);

		for (int index = 0; index < _sweepResult.Count; index++)
		{
			if (!_items.Equip(_sweepResult[index], loot.ItemTypeId, loot.Charges)) continue;

			Despawn(lootItemId);
			return true;
		}

		return false;
	}

	public void Step(float deltaSeconds)
	{
		_expired.Clear();
		World.Query(in AllLootQuery, _collectAll);
		if (_collectedCount == 0) return;

		for (int index = 0; index < _collectedCount; index++)
		{
			var entity = new Entity(_lootBuffer[index].Value);
			if (!World.IsAlive(entity) || !World.Has<GroundLoot>(entity)) continue;

			GroundLoot loot = World.Get<GroundLoot>(entity);
			if (loot.Lifetime > 0f)
			{
				loot.RemainingLifetime -= deltaSeconds;
				World.Set(entity, loot);

				if (loot.RemainingLifetime <= 0f)
				{
					_expired.Add(entity);
					continue;
				}
			}

			if (!loot.AutoPickup) continue;

			Vector3 position = World.Has<Position>(entity) ? World.Get<Position>(entity).Value : Vector3.Zero;
			_sweepResult.Clear();
			_pickupCenter = position;
			_pickupRadiusSquared = loot.PickupRadius * loot.PickupRadius;
			_pickupItemTypeId = loot.ItemTypeId;
			_pickupOwnerPlayerIndex = loot.OwnerPlayerIndex;
			World.Query(in CandidateQuery, _pickupCandidates);

			Entity nearest = NearestCandidate(position);
			if (nearest == Entity.Null) continue;
			if (!_items.Equip(nearest, loot.ItemTypeId, loot.Charges)) continue;

			_expired.Add(entity);
		}

		for (int index = 0; index < _expired.Count; index++)
		{
			if (World.IsAlive(_expired[index])) World.Destroy(_expired[index]);
		}
	}

	private int OwnerPlayerIndex(Entity unit)
	{
		return World.TryGet<UnitOwnerPlayer>(unit, out UnitOwnerPlayer owner) ? owner.Value : -1;
	}

	private void CollectAll(Entity entity, ref GroundLoot loot, ref Position position)
	{
		if (_collectedCount == LootBufferCapacity) return;

		_lootBuffer[_collectedCount++] = (LootItemId)entity.Id;
	}

	private void CollectPickupCandidates(Entity entity, ref Position position, ref Owner owner, ref Health health)
	{
		if (health.Current <= 0f) return;
		if (owner.PlayerEntity.Value == Entity.Null) return;

		int playerIndex = World.TryGet<UnitOwnerPlayer>(entity, out UnitOwnerPlayer unitOwner) ? unitOwner.Value : -1;
		if (_pickupOwnerPlayerIndex >= 0 && playerIndex != _pickupOwnerPlayerIndex) return;
		if (!_items.CanCarry(entity, _pickupItemTypeId)) return;

		_sweepResult.Add(entity);
	}

	private Entity NearestCandidate(Vector3 position)
	{
		Entity nearest = Entity.Null;
		float nearestDistanceSquared = float.MaxValue;

		for (int index = 0; index < _sweepResult.Count; index++)
		{
			if (!World.TryGet<Position>(_sweepResult[index], out Position candidate)) continue;

			float distanceSquared = Vector3.DistanceSquared(candidate.Value, position);
			if (distanceSquared > _pickupRadiusSquared) continue;
			if (distanceSquared >= nearestDistanceSquared) continue;

			nearestDistanceSquared = distanceSquared;
			nearest = _sweepResult[index];
		}

		return nearest;
	}
}
