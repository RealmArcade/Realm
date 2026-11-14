namespace Realm.Ecs;

/// <summary>
///     How much of the world a match shows. The core game offers this setting to every lobby, a map states the mode its
///     board starts on, and the lobby's host may choose another one for the match it starts.
/// </summary>
public enum MatchShroudMode : int
{
	/// <summary>Leaves the mode to the map's board properties, which is what a lobby whose map stated nothing plays.</summary>
	FromMap = 0,

	/// <summary>Shows the whole board to every player.</summary>
	None = 1,

	/// <summary>Hides what nobody is watching while keeping every explored place remembered.</summary>
	Partial = 2,

	/// <summary>Hides the ground itself until one of a player's units has walked over it.</summary>
	Full = 3
}
