using Realm.Ecs;

namespace Realm.Client.UI;

/// <summary>
/// Everything a tooltip template may read: the unit whose card is showing, an optional target,
/// the ability or item the card describes, and the player the numbers belong to.
/// A card keeps the context and resolves its template when the player hovers it, so the numbers
/// on screen are the numbers the game would use at that moment.
/// </summary>
public struct TooltipContext
{
	public Entity Source;
	public Entity Target;

	public string? AbilityId;
	public string? ItemId;
	public string? UpgradeId;

	/// <summary>Player whose resources the <c>Owner</c> scope reads. Negative means the local player.</summary>
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
