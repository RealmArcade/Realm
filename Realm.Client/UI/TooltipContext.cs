using Realm.Ecs;

namespace Realm.Client.UI;

public struct TooltipContext
{
	public Entity Source;
	public Entity Target;

	public string? AbilityId;
	public string? ItemId;
	public string? UpgradeId;

	public int PlayerIndex;

	public static TooltipContext ForAbility(Entity source, string? abilityId, int playerIndex = -1)
	{
		return new TooltipContext { Source = source, AbilityId = abilityId, PlayerIndex = playerIndex };
	}

	public static TooltipContext ForItem(Entity source, string? itemId, int playerIndex = -1)
	{
		return new TooltipContext { Source = source, ItemId = itemId, PlayerIndex = playerIndex };
	}

	public static TooltipContext ForUpgrade(Entity source, string? upgradeId, int playerIndex = -1)
	{
		return new TooltipContext { Source = source, UpgradeId = upgradeId, PlayerIndex = playerIndex };
	}

	public static TooltipContext ForUnit(Entity source)
	{
		return new TooltipContext { Source = source, PlayerIndex = -1 };
	}
}
