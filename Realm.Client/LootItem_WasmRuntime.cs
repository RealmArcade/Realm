using Godot;
using Realm.Client.Core;
using Realm.Client.Services;
using Realm.Ecs.Components.Core;
using Realm.MapAPI;
using Vector3 = System.Numerics.Vector3;

namespace Realm.Client;

public class LootItem_WasmRuntime
{
	private readonly int _uniqueId;
	private readonly LootItem3D? _lootNode;

	public LootItem_WasmRuntime(int uniqueId, LootItem3D? lootNode)
	{
		_uniqueId = uniqueId;
		_lootNode = lootNode;
	}

	public int UniqueId => _uniqueId;

	public string ItemTypeId
	{
		get
		{
			if (GodotObject.IsInstanceValid(_lootNode)) return _lootNode.ItemTypeId;
			return string.Empty;
		}
	}

	public Vector3 Position
	{
		get
		{
			if (GodotObject.IsInstanceValid(_lootNode))
			{
				var pos = _lootNode.GlobalPosition;
				return new Vector3(pos.X, pos.Y, pos.Z);
			}
			return Vector3.Zero;
		}
	}

	public int Charges
	{
		get
		{
			if (GodotObject.IsInstanceValid(_lootNode)) return _lootNode.Charges;
			return 0;
		}
	}

	public float Lifetime
	{
		get
		{
			if (GodotObject.IsInstanceValid(_lootNode)) return _lootNode.Lifetime;
			return 0f;
		}
	}

	public float RemainingLifetime
	{
		get
		{
			if (GodotObject.IsInstanceValid(_lootNode)) return _lootNode.RemainingLifetime;
			return 0f;
		}
	}

	public float PickupRadius
	{
		get
		{
			if (GodotObject.IsInstanceValid(_lootNode)) return _lootNode.PickupRadius;
			return 0f;
		}
		set
		{
			if (GodotObject.IsInstanceValid(_lootNode))
			{
				_lootNode.PickupRadius = value;
				if (GameContext.World != null && GameContext.World.IsAlive(_lootNode.Entity) && GameContext.World.Has<GroundLoot>(_lootNode.Entity))
				{
					ref var loot = ref GameContext.World.Get<GroundLoot>(_lootNode.Entity);
					loot.PickupRadius = value;
				}
			}
		}
	}

	public bool AutoPickup
	{
		get
		{
			if (GodotObject.IsInstanceValid(_lootNode)) return _lootNode.AutoPickup;
			return false;
		}
		set
		{
			if (GodotObject.IsInstanceValid(_lootNode))
			{
				_lootNode.AutoPickup = value;
				if (GameContext.World != null && GameContext.World.IsAlive(_lootNode.Entity) && GameContext.World.Has<GroundLoot>(_lootNode.Entity))
				{
					ref var loot = ref GameContext.World.Get<GroundLoot>(_lootNode.Entity);
					loot.AutoPickup = value;
				}
			}
		}
	}

	public int OwnerPlayerIndex
	{
		get
		{
			if (GodotObject.IsInstanceValid(_lootNode)) return _lootNode.OwnerPlayerIndex;
			return -1;
		}
		set
		{
			if (GodotObject.IsInstanceValid(_lootNode))
			{
				_lootNode.OwnerPlayerIndex = value;
				if (GameContext.World != null && GameContext.World.IsAlive(_lootNode.Entity) && GameContext.World.Has<GroundLoot>(_lootNode.Entity))
				{
					ref var loot = ref GameContext.World.Get<GroundLoot>(_lootNode.Entity);
					loot.OwnerPlayerIndex = value;
				}
			}
		}
	}

	public bool IsDespawned => !GodotObject.IsInstanceValid(_lootNode) || _lootNode.IsDespawned;

	public void Despawn()
	{
		GameContext.Host?.RemoveLootItem(_uniqueId);
	}
}
