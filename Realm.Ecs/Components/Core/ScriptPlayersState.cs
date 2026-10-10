namespace Realm.Ecs.Components.Core;

/// <summary>
/// Stores the simulation state for all 12 script players.
/// </summary>
public struct ScriptPlayersState
{
	public ScriptPlayersState(ScriptPlayer[] players)
	{
		Players = players;
	}

	public ScriptPlayer[] Players { get; }
}
