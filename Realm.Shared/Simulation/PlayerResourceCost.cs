using Realm.Ecs;
using Realm.Ecs.Components.Resources;
using Realm.Shared.Metadata;

namespace Realm.Shared.Simulation;

/// <summary>
///     What a player pays for a template. Charging lives here rather than at a call site so the authority and any
///     caller that only wants to know whether an order is affordable read the same costs out of the same catalog.
/// </summary>
public static class PlayerResourceCost
{
	/// <summary>
	///     Takes the template's cost from the player, or reports that the player cannot afford it. The caller decides
	///     what an unaffordable order means; nothing is charged when it returns false.
	/// </summary>
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
