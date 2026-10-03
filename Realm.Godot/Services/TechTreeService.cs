using Arch.Core;
using Realm.Ecs.Components.Core;
using Realm.Ecs.Components.Combat;
using Realm.Ecs.Components.Tags;
using System;
using Realm.Ecs.Services;

internal class TechTreeService
{
	private readonly WorldAccessor _ecsWorldAccessor;
	private World EcsWorld => _ecsWorldAccessor.Current;

	public TechTreeService(WorldAccessor ecsWorldAccessor)
	{
		_ecsWorldAccessor = ecsWorldAccessor;
	}

	public bool BuyWeaponsUpgrade(Entity playerEntity)
	{
		if (!EcsWorld.IsAlive(playerEntity)) return false;
		ref var upgrades = ref EcsWorld.Get<PlayerUpgrades>(playerEntity);
		if (upgrades.WeaponsUpgrade) return false;

		upgrades.WeaponsUpgrade = true;

		var query = Realm.Ecs.Common.QueryCache.AllAttackAndOwnerNoneDeadAndBuildingQuery;
		EcsWorld.Query(in query, (Entity entity, ref Attack atk, ref Owner owner) =>
		{
			if (owner.PlayerEntity.Value == playerEntity)
			{
				atk.Damage += 3f;
			}
		});
		return true;
	}

	public bool BuyShieldsUpgrade(Entity playerEntity)
	{
		if (!EcsWorld.IsAlive(playerEntity)) return false;
		ref var upgrades = ref EcsWorld.Get<PlayerUpgrades>(playerEntity);
		if (upgrades.ShieldsUpgrade) return false;

		upgrades.ShieldsUpgrade = true;

		var query = Realm.Ecs.Common.QueryCache.AllArmorAndOwnerNoneDeadQuery;
		EcsWorld.Query(in query, (Entity entity, ref Armor arm, ref Owner owner) =>
		{
			if (owner.PlayerEntity.Value == playerEntity)
			{
				// arm.Value += 2f;
			}
		});
		return true;
	}

	public bool BuyHarvestingUpgrade(Entity playerEntity)
	{
		if (!EcsWorld.IsAlive(playerEntity)) return false;
		ref var upgrades = ref EcsWorld.Get<PlayerUpgrades>(playerEntity);
		if (upgrades.HarvestingUpgrade) return false;

		upgrades.HarvestingUpgrade = true;
		return true;
	}
}
