using Realm.Client.Core;
using Realm.Client.Services;
using Realm.Ecs;
using Realm.Ecs.Components.Core;
using Realm.Ecs.Components.Movement;
using Vector3 = System.Numerics.Vector3;

namespace Realm.Client;

public class LootItem_WasmRuntime
{
	private readonly Entity _entity;

	public LootItem_WasmRuntime(Entity entity)
	{
		_entity = entity;
	}

	public int UniqueId => _entity.Id;

	public string ItemTypeId
	{
		get => TryGetLoot(out GroundLoot loot) ? loot.ItemTypeId : string.Empty;
	}

	public Vector3 Position
	{
		get
		{
			var world = GameContext.World;
			if (world == null || !world.Has<Position>(_entity)) return Vector3.Zero;
			return world.Get<Position>(_entity).Value;
		}
	}

	public int Charges
	{
		get => TryGetLoot(out GroundLoot loot) ? loot.Charges : 0;
	}

	public float Lifetime
	{
		get => TryGetLoot(out GroundLoot loot) ? loot.Lifetime : 0f;
	}

	public float RemainingLifetime
	{
		get => TryGetLoot(out GroundLoot loot) ? loot.RemainingLifetime : 0f;
	}

	public float PickupRadius
	{
		get => TryGetLoot(out GroundLoot loot) ? loot.PickupRadius : 0f;
		set
		{
			var world = GameContext.World;
			if (world == null || !world.IsAlive(_entity) || !world.Has<GroundLoot>(_entity)) return;
			ref var loot = ref world.Get<GroundLoot>(_entity);
			loot.PickupRadius = value;
		}
	}

	public bool AutoPickup
	{
		get => TryGetLoot(out GroundLoot loot) && loot.AutoPickup;
		set
		{
			var world = GameContext.World;
			if (world == null || !world.IsAlive(_entity) || !world.Has<GroundLoot>(_entity)) return;
			ref var loot = ref world.Get<GroundLoot>(_entity);
			loot.AutoPickup = value;
		}
	}

	public int OwnerPlayerIndex
	{
		get => TryGetLoot(out GroundLoot loot) ? loot.OwnerPlayerIndex : -1;
		set
		{
			var world = GameContext.World;
			if (world == null || !world.IsAlive(_entity) || !world.Has<GroundLoot>(_entity)) return;
			ref var loot = ref world.Get<GroundLoot>(_entity);
			loot.OwnerPlayerIndex = value;
		}
	}

	public bool IsDespawned => !GameHost.IdToLootItem.ContainsKey(_entity.Id);

	public void Despawn()
	{
		GameHost.Instance?.RemoveLootItem(_entity.Id);
	}

	private bool TryGetLoot(out GroundLoot loot)
	{
		loot = default;
		var world = GameContext.World;
		if (world == null || !world.IsAlive(_entity) || !world.Has<GroundLoot>(_entity)) return false;
		loot = world.Get<GroundLoot>(_entity);
		return true;
	}
}
