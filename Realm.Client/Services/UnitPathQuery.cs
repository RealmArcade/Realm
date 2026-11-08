using System.Collections.Generic;
using System.Numerics;
using Realm.Ecs;
using Realm.Ecs.Components.Combat;
using Realm.Ecs.Components.Core;
using Realm.Ecs.Components.Movement;
using Realm.Ecs.Services;

namespace Realm.Client.Services;

/// <summary>
///     Reads a unit's ordered paths out of the simulation for drawing: the movement path it is following, and the
///     rally path a producer sends new units along. Both answer in ECS vectors and leave the scene-graph conversion
///     to the caller, so the rules hold without a node.
/// </summary>
internal static class UnitPathQuery
{
	private const float DefaultRallyAimDistance = 8f;

	/// <summary>Where the entity stands, or the node's own position when the world has no answer.</summary>
	internal static Vector3 EcsOrFallback(Entity entity, Vector3 fallbackPosition)
	{
		if (GameContext.Host != null && GameContext.World != null && entity != default
		                            && GameContext.World.IsAlive(entity) && GameContext.World.Has<Position>(entity))
		{
			return GameContext.World.Get<Position>(entity).Value;
		}

		return fallbackPosition;
	}

	/// <summary>
	///     The ordered movement path: the followed path from its current waypoint, or the single move/preview target
	///     when no path exists yet, followed by anything still queued. <paramref name="isAttackPath" /> reports whether
	///     the order was an attack, which is what the caller colours the path by.
	/// </summary>
	internal static void FillMovementPath(Entity entity, Vector3 fallbackOrigin, List<Vector3> points, out bool isAttackPath)
	{
		points.Clear();
		isAttackPath = false;

		if (GameContext.Host == null || GameContext.World is not { } world || !world.IsAlive(entity)) return;

		if (world.Has<AttackTarget>(entity) || world.Has<AttackMove>(entity))
		{
			isAttackPath = true;
		}

		points.Add(EcsOrFallback(entity, fallbackOrigin));

		bool hasPathPoints = false;
		if (world.Has<PathFollow>(entity))
		{
			var follow = world.Get<PathFollow>(entity);
			for (int i = follow.CurrentWaypointIndex; i < follow.WaypointCount; i++)
			{
				points.Add(follow.Waypoints[i]);
				hasPathPoints = true;
			}
		}

		if (!hasPathPoints && world.Has<MoveTo>(entity))
		{
			points.Add(world.Get<MoveTo>(entity).Target);
		}

		if (!hasPathPoints && world.Has<PreviewMoveTo>(entity))
		{
			points.Add(world.Get<PreviewMoveTo>(entity).Target);
		}

		if (world.Has<WaypointQueue>(entity))
		{
			var queue = world.Get<WaypointQueue>(entity);
			for (int i = 0; i < queue.Count; i++)
			{
				points.Add(queue.Waypoints[i]);
			}
		}
	}

	/// <summary>
	///     The rally path: the producer's position followed by its rally waypoints, or — for a producer that was never
	///     given one — the point it aims at by default. A caller that sees two points has something to draw.
	/// </summary>
	internal static void FillRallyPath(Entity entity, Vector3 fallbackOrigin, List<Vector3> points)
	{
		points.Clear();
		if (GameContext.Host == null || GameContext.World is not { } world || !world.IsAlive(entity)) return;

		Vector3 origin = EcsOrFallback(entity, fallbackOrigin);
		points.Add(origin);

		if (world.Has<RallyPoint>(entity))
		{
			var rally = world.Get<RallyPoint>(entity);
			for (int i = 0; i < rally.Count; i++)
			{
				points.Add(rally.Waypoints[i]);
			}
		}
		else
		{
			points.Add(origin + new Vector3(0f, 0f, DefaultRallyAimDistance));
		}
	}
}
