using System.Numerics;

namespace Realm.Ecs.Services;

/// <summary>
/// Caches the pathfinding waypoints for navigation to avoid GC allocations in the tick query loop.
/// </summary>
public struct PathFollow
{
	public WaypointBuffer Waypoints;
	public int WaypointCount;
	public int CurrentWaypointIndex;
	public Vector3 Target;
	public Vector3 LastPosition;
	public float StuckTime;
	public float TimeSinceLastReplan;
	public bool IsJitterReplanned;

	/// <summary>
	///     True when the corridor actually connected the start and end polygons. When false, the
	///     waypoints are a single fallback point to the nearest polygon and do NOT count as a real
	///     route — combat reachability uses this to avoid treating an unreachable island as a path.
	/// </summary>
	public bool HasValidCorridor;
}
