namespace Realm.Ecs.Components.Core;

/// <summary>
/// Caps how many buildings a player may hold at once; a limit of zero leaves the player unlimited.
/// </summary>
public struct PlayerBuildingLimit
{
	public int Value;

	public PlayerBuildingLimit(int value)
	{
		Value = value;
	}
}
