namespace Realm.Client.Core;

/// <summary>
///     A board seat as the editor holds it: the player slot it seats and the world point that seat begins at. It is
///     authored with the coordinate tool, whose drawn box is reduced to its centre.
/// </summary>
public struct EditorSpawnAnchor
{
	public int Player;
	public float PositionX;
	public float PositionZ;
}
