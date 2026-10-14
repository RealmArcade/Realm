using System.Collections.Generic;
using Arch.Core;
using Realm.Client.Core;
using Realm.Ecs.Common;
using Realm.Ecs.Components.Combat;
using Realm.Ecs.Components.Core;

namespace Realm.Client.Services;

public class TechTreeService
{
	public TechTreeService()
	{
	}

	public bool BuyWeaponsUpgrade(Entity playerEntity)
	{
		if (!GameContext.World.IsAlive(playerEntity)) return false;
		ref var upgrades = ref GameContext.World.Get<PlayerUpgrades>(playerEntity);
		if (upgrades.WeaponsUpgrade) return false;

		upgrades.WeaponsUpgrade = true;

		var query = QueryCache.AllAttackAndOwnerNoneDeadAndBuildingQuery;
		GameContext.World.Query(in query, (Entity entity, ref Attack atk, ref Owner owner) =>
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
		if (!GameContext.World.IsAlive(playerEntity)) return false;
		ref var upgrades = ref GameContext.World.Get<PlayerUpgrades>(playerEntity);
		if (upgrades.ShieldsUpgrade) return false;

		upgrades.ShieldsUpgrade = true;

		var query = QueryCache.AllArmorAndOwnerNoneDeadQuery;
		GameContext.World.Query(in query, (Entity entity, ref Armor arm, ref Owner owner) =>
		{
			if (owner.PlayerEntity.Value == playerEntity)
			{
				arm.FlatArmor += 2f;
			}
		});
		return true;
	}

	public bool BuyHarvestingUpgrade(Entity playerEntity)
	{
		if (!GameContext.World.IsAlive(playerEntity)) return false;
		ref var upgrades = ref GameContext.World.Get<PlayerUpgrades>(playerEntity);
		if (upgrades.HarvestingUpgrade) return false;

		upgrades.HarvestingUpgrade = true;
		return true;
	}

	public Dictionary<(int PlayerIndex, string TechId), int> PlayerTechLevels { get; } = new();

	public int GetPlayerTechLevel(int playerIndex, string techId)
	{
		if (string.IsNullOrEmpty(techId)) return 0;
		return PlayerTechLevels.TryGetValue((playerIndex, techId), out int level) ? level : 0;
	}

	public void SetPlayerTechLevel(int playerIndex, string techId, int level)
	{
		if (string.IsNullOrEmpty(techId)) return;
		PlayerTechLevels[(playerIndex, techId)] = level;
	}

	public void AddPlayerTechLevel(int playerIndex, string techId, int delta)
	{
		if (string.IsNullOrEmpty(techId)) return;
		int current = GetPlayerTechLevel(playerIndex, techId);
		PlayerTechLevels[(playerIndex, techId)] = current + delta;
	}
}