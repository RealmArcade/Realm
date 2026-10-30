using Realm.Ecs;
using Realm.Ecs.Components.Resources;
using Realm.Shared.Metadata;

namespace Realm.Shared.Simulation;

public static class PlayerResourceCost
{
	public static bool TryCharge(EcsWorld world, Entity playerEntity, UnitMetadata metadata)
	{
		if (playerEntity == Entity.Null || metadata == null) return false;
		if (!world.TryGet<PlayerResources>(playerEntity, out PlayerResources resources)) return false;

		int goldCost = (int)metadata.CostGold;
		int woodCost = (int)metadata.CostWood;
		int stoneCost = (int)metadata.CostStone;

		if (resources.Gold < goldCost || resources.Wood < woodCost || resources.Stone < stoneCost) return false;

		resources.Gold -= goldCost;
		resources.Wood -= woodCost;
		resources.Stone -= stoneCost;
		world.Set(playerEntity, resources);
		return true;
	}
}
