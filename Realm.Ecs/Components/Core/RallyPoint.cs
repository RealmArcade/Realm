using Realm.Ecs.Components.Movement;
using System.Numerics;

namespace Realm.Ecs.Components.Core;

/// <summary>
///     Represents a rally point with a queue of waypoints for trained units to move along upon spawning.
/// </summary>
public struct RallyPoint
{
	public WaypointBuffer Waypoints;
	public int Count;

	public RallyPoint(Vector3 target)
	{
		Count = 1;
		Waypoints[0] = target;
	}

	public void Add(Vector3 wp)
	{
		if (Count < 16)
		{
			Waypoints[Count] = wp;
			Count++;
		}
	}
}
