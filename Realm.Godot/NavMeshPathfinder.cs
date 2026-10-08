using DotRecast.Core.Numerics;
using DotRecast.Detour;
using Realm.Ecs.Components.Terrain;
using Realm.Ecs.Services;
using System;
using System.Numerics;

internal class NavMeshPathfinder
{
	private readonly long[] _pathCorridorBuffer = new long[512];
	private readonly DtStraightPath[] _straightPathBuffer = new DtStraightPath[512];
	public static readonly RcVec3f PathfindingExtents = new RcVec3f(2f, 4f, 2f);
	private static readonly RcVec3f TargetPathfindingExtents = new RcVec3f(2f, 4f, 2f);
	private static readonly RcVec3f WideTargetExtents = new RcVec3f(50f, 50f, 50f);

	// Probe for the end of a *ground* move/pursuit. Ground units path to the terrain at
	// the clicked XZ (the "foot" of a building or a mountain), not to the exact height of
	// the target, and the probe is kept tight vertically so steep/unwalkable peaks do not
	// grab the destination — a summit is a disconnected navmesh island and would otherwise
	// send the unit walking into the wall below forever.
	private static readonly RcVec3f GroundTargetExtents = new RcVec3f(50f, 2.5f, 50f);

	private readonly DtQueryDefaultFilter _filter = new DtQueryDefaultFilter();

	public DtQueryDefaultFilter Filter => _filter;

	public void ComputePath(DtNavMeshQuery query, Vector3 start, Vector3 end, ushort includeFlags, ref PathFollow pf)
	{
		pf.Target = end;

		pf.WaypointCount = 0;
		pf.CurrentWaypointIndex = 0;
		pf.HasValidCorridor = false;

		_filter.SetIncludeFlags(includeFlags);
		_filter.SetExcludeFlags(0);

		var startPos = new RcVec3f(start.X, start.Y, start.Z);
		var endPos = new RcVec3f(end.X, end.Y, end.Z);
		bool isGround = ((TerrainPathingFlags)includeFlags & TerrainPathingFlags.Flying) == 0;

		if (!TryFindStartPoly(query, startPos, out long startRef, out var startPt))
		{
			return;
		}

		if (!TryFindEndPoly(query, startPos, endPos, isGround, out long endRef, out var endPt))
		{
			return;
		}

		int corridorCount = FindPathCorridor(query, startRef, endRef, startPt, endPt, startPos, endPos, isGround, out endPt);

		if (corridorCount <= 0)
		{
			return;
		}

		PopulateWaypoints(query, startPt, endPt, corridorCount, ref pf);
	}

	private bool TryFindStartPoly(DtNavMeshQuery query, RcVec3f startPos, out long startRef, out RcVec3f startPt)
	{
		query.FindNearestPoly(startPos, PathfindingExtents, _filter, out startRef, out startPt, out _);
		if (startRef == 0)
		{
			// If the unit was pushed or spawned off the valid navmesh, expand the search drastically
			// so they can find a path back onto the walkable area instead of becoming paralyzed.
			query.FindNearestPoly(startPos, new RcVec3f(10f, 10f, 10f), _filter, out startRef, out startPt, out _);
		}
		return startRef != 0;
	}

	private bool TryFindEndPoly(DtNavMeshQuery query, RcVec3f startPos, RcVec3f endPos, bool isGround, out long endRef, out RcVec3f endPt)
	{
		query.FindNearestPoly(endPos, TargetPathfindingExtents, _filter, out endRef, out endPt, out _);

		if (endRef == 0 && isGround)
		{
			// Ground moves walk toward the terrain at the destination XZ; a vertical probe
			// centered on the start elevation keeps unreachable peaks from hijacking the path.
			var groundEnd = new RcVec3f(endPos.X, startPos.Y, endPos.Z);
			query.FindNearestPoly(groundEnd, GroundTargetExtents, _filter, out endRef, out endPt, out _);
		}

		if (endRef == 0)
		{
			// Expand search to try to find anything nearby
			query.FindNearestPoly(endPos, WideTargetExtents, _filter, out endRef, out endPt, out _);
		}

		return endRef != 0;
	}

	private int FindPathCorridor(DtNavMeshQuery query, long startRef, long endRef, RcVec3f startPt, RcVec3f endPt, RcVec3f startPos, RcVec3f endPos, bool isGround, out RcVec3f finalEndPt)
	{
		finalEndPt = endPt;
		query.FindPath(startRef, endRef, startPt, endPt, _filter, _pathCorridorBuffer, out int corridorCount, _pathCorridorBuffer.Length);
		
		// If pathing fails due to disconnected islands (e.g. clicking a mountain peak),
		// try to path to the ground base directly underneath the target.
		if (corridorCount == 0 && isGround)
		{
			var groundEnd = new RcVec3f(endPos.X, startPos.Y, endPos.Z);
			query.FindNearestPoly(groundEnd, GroundTargetExtents, _filter, out long groundEndRef, out var groundEndPt, out _);
			if (groundEndRef != 0)
			{
				query.FindPath(startRef, groundEndRef, startPt, groundEndPt, _filter, _pathCorridorBuffer, out corridorCount, _pathCorridorBuffer.Length);
				finalEndPt = groundEndPt;
			}
		}

		return corridorCount;
	}

	private void PopulateWaypoints(DtNavMeshQuery query, RcVec3f startPt, RcVec3f endPt, int corridorCount, ref PathFollow pf)
	{
		query.FindStraightPath(startPt, endPt, _pathCorridorBuffer, corridorCount, _straightPathBuffer, out int straightPathCount, _straightPathBuffer.Length, 0);
		pf.WaypointCount = Math.Min(straightPathCount, Realm.Ecs.Components.Movement.WaypointBuffer.Length);
		pf.CurrentWaypointIndex = 0;
		for (int i = 0; i < pf.WaypointCount; i++)
		{
			pf.Waypoints[i] = new Vector3(_straightPathBuffer[i].pos.X, _straightPathBuffer[i].pos.Y, _straightPathBuffer[i].pos.Z);
		}
		pf.HasValidCorridor = pf.WaypointCount > 0;
	}
}
