namespace Realm.Client.Core;

/// <summary>
///     A board seat as the editor holds it: the player the seat belongs to and the authored coordinate that seat begins
///     inside. A match picks a point within that coordinate, so the anchor is a region rather than a spot.
/// </summary>
public struct EditorSpawnAnchor
{
	public int Player;
	public string CoordinateName;
}
