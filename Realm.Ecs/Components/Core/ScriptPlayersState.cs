namespace Realm.Ecs.Components.Core;

/// <summary>
/// Stores the simulation state for all 25 script players (slots 0..24).
/// </summary>
public struct ScriptPlayersState
{
	public ScriptPlayersState(ScriptPlayer[] players)
	{
		Players = players;
	}

	public ScriptPlayer[] Players { get; }
}