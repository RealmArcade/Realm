namespace Realm.Ecs;

/// <summary>
///     Selects which of a minimap's two colourings a stated colour applies to. The minimap draws a player's units in one
///     of two schemes, and a colour stated for one of them does not change the other: <see cref="PlayerColors" /> paints
///     every player in the colour it was given, and <see cref="TeamColors" /> paints the map by relation to the screen
///     looking at it - the player this screen holds, that player's allies and its enemies - so a map that wants one of its
///     players drawn differently in only one of those two readings states it under that reading alone.
/// </summary>
public enum MinimapColorMode : int
{
	/// <summary>
	///     The scheme that paints a player by its relation to the screen looking at it: the player this screen holds, the
	///     players allied to it, and the players hostile to it.
	/// </summary>
	TeamColors = 0,

	/// <summary>
	///     The scheme that paints every player in its own colour, which is the colour its seat states.
	/// </summary>
	PlayerColors = 1
}
