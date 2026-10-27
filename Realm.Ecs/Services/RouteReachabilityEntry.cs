using System.Numerics;
using DotRecast.Detour;

namespace Realm.Ecs.Services;

public readonly struct RouteReachabilityEntry
{
	public readonly bool Reachable;
	public readonly Vector3 AttackerPos;
	public readonly Vector3 TargetPos;
	public readonly DtNavMeshQuery? Query;
	public readonly float Timestamp;

	public RouteReachabilityEntry(bool reachable, Vector3 attackerPos, Vector3 targetPos, DtNavMeshQuery? query, float timestamp)
	{
		Reachable = reachable;
		AttackerPos = attackerPos;
		TargetPos = targetPos;
		Query = query;
		Timestamp = timestamp;
	}
}