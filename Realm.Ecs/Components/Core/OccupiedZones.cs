namespace Realm.Ecs.Components.Core;

/// <summary>
/// Tracks which script zones are currently occupied by a unit entity.
/// </summary>
public struct OccupiedZones
{
	public OccupiedZones(HashSet<int> zoneIds)
	{
		ZoneIds = zoneIds;
	}

	public HashSet<int> ZoneIds { get; }
}