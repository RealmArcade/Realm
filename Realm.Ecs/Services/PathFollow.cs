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
	public byte IsJitterReplannedByte;

	public bool IsJitterReplanned
	{
		get => IsJitterReplannedByte != 0;
		set => IsJitterReplannedByte = value ? (byte)1 : (byte)0;
	}

	public byte HasValidCorridorByte;

	public bool HasValidCorridor
	{
		get => HasValidCorridorByte != 0;
		set => HasValidCorridorByte = value ? (byte)1 : (byte)0;
	}
}