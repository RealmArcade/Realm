using System.Numerics;

namespace Realm.Ecs.Components.Movement;

/// <summary>
/// Stores a queue of pending waypoints for sequential pathing.
/// </summary>
public struct WaypointQueue
{
	public WaypointBuffer Waypoints;
	public int Count;

	public WaypointQueue(Vector3 target)
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

	public Vector3 Dequeue()
	{
		if (Count == 0)
		{
			throw new InvalidOperationException("Queue is empty");
		}
		Vector3 first = Waypoints[0];
		for (int i = 1; i < Count; i++)
		{
			Waypoints[i - 1] = Waypoints[i];
		}
		Count--;
		return first;
	}
}
