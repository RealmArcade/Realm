using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Numerics;
using System.Threading;
using System.Threading.Tasks;
using Realm.Ecs;
using DotRecast.Detour;
using Realm.Ecs.Common;
using Realm.Ecs.Components.Combat;
using Realm.Ecs.Components.Core;
using Realm.Ecs.Components.Movement;
using Realm.Ecs.Components.Tags;
using Realm.Ecs.Components.Terrain;
using Realm.Ecs.Services;
using PathingFlags = Realm.Ecs.Components.Movement.PathingFlags;

namespace Realm.Ecs.Services
{
	/// <summary>range, reach and the navmesh route check behind them.</summary>
	public partial class CombatAndDamageService
	{
		private float GetEffectiveAttackCooldown(Entity entity, float baseCooldown)
		{
			if (_context.World.Has<AttackSpeedMultiplier>(entity))
			{
				float multiplier = _context.World.Get<AttackSpeedMultiplier>(entity).Multiplier;
				if (multiplier > 0f)
				{
					return baseCooldown / multiplier;
				}
			}

			return baseCooldown;
		}

		private float GetTargetCollisionRadius(Entity target)
		{
			if (!_context.World.Has<CollisionRadius>(target)) return 0f;

			float radius = _context.World.Get<CollisionRadius>(target).Value;
			if (_context.World.Has<CollisionScale>(target))
			{
				radius *= _context.World.Get<CollisionScale>(target).Value;
			}

			return radius;
		}

		/// <summary>
		///     Physical contact radius used by the movement separation system: the authored
		///     CollisionRadius scaled by CollisionScale, or the shared default fraction of the
		///     scale when the unit has no authored radius. A non-positive authored radius means the
		///     value was never set, so it falls back to the default instead of describing a
		///     zero-width body. Combat uses the same rule so melee reach is consistent with how
		///     close units can actually get to each other.
		/// </summary>
		private float GetUnitContactRadius(Entity entity)
		{
			float scale = _context.World.Has<CollisionScale>(entity) ? _context.World.Get<CollisionScale>(entity).Value : 1.0f;
			float authoredRadius = _context.World.Has<CollisionRadius>(entity) ? _context.World.Get<CollisionRadius>(entity).Value : 0f;
			return GameplayConstants.ResolveUnitContactRadius(authoredRadius, scale);
		}

		/// <summary>
		///     Distance at which movement separation parks two unit bodies against each other:
		///     the combined contact radii scaled by CollisionSeparationFactor, plus a small safety
		///     margin so steering/clump variance never leaves a pair hovering just out of reach.
		///     Melee reach must never be smaller than the distance the bodies rest at, otherwise a
		///     crowd of attackers parks the front rank just outside their own attack range.
		/// </summary>
		private float GetMinimumMeleeContactReach(Entity a, Entity b)
		{
			return GetMinimumMeleeContactReach(GetUnitContactRadius(a), GetUnitContactRadius(b));
		}

		private static float GetMinimumMeleeContactReach(float attackerContactRadius, float targetContactRadius)
		{
			return (attackerContactRadius + targetContactRadius) * GameplayConstants.CollisionSeparationFactor * MeleeContactReachSafetyMargin;
		}

		/// <summary>
		///     Effective distance at which an attacker can land a hit on the target. Ranged
		///     attacks use range plus both collision radii. Melee attacks also need to cover the
		///     distance at which the two bodies come to rest against each other; otherwise units
		///     whose attack range is shorter than their own body width can never land a hit.
		/// </summary>
		private float GetEffectiveRange(Entity attacker, Entity target, float attackRange, out bool isMelee, out float meleeVerticalReach)
		{
			float reach = attackRange + GetTargetCollisionRadius(target) + GetTargetCollisionRadius(attacker);
			isMelee = !IsFlying(attacker) && !IsFlying(target) && attackRange <= MeleeRangeThreshold;
			meleeVerticalReach = Math.Max(attackRange, DefaultMeleeVerticalReach);
			if (isMelee)
			{
				float contactReach = GetMinimumMeleeContactReach(attacker, target);
				if (reach < contactReach)
				{
					reach = contactReach;
				}
			}

			return reach;
		}

		// Decides whether a ground attacker can ever fight a target by checking the REAL navmesh
		// route instead of a vertical-heuristic shortcut. A unit can commit to a target only if a
		// valid corridor exists AND its closest approach lands within effective range. A tower on a
		// raised mountain has no corridor to the summit, so the route resolves to the walkable base
		// at the attacker's elevation, whose distance to the elevated target exceeds range — that is
		// exactly the unreachable case this replaces the crude vertical guess with.
		// Flying attackers ignore the check.
		//
		// The result is cached per (attacker, target). Reachable verdicts are recomputed only when
		// the attacker or target moved meaningfully or the navmesh was rebuilt. Negative verdicts
		// also expire after RouteCacheNegativeMaxAge, because an attacker that cannot reach its
		// target never moves far enough to invalidate the entry by position change alone.
		private bool IsReachableWithinRange(Entity attacker, Entity target, Vector3 attackerPos, Vector3 targetPos, float effectiveRange, bool isMelee, float meleeVerticalReach)
		{
			bool attackerIsFlying = IsFlying(attacker);
			int includeFlags = _context.World.Has<PathingFlags>(attacker) ? _context.World.Get<PathingFlags>(attacker).Value : (int)TerrainPathingFlags.Ground;
			return IsReachableWithinRangeThreadSafe(attacker, target, attackerPos, targetPos, effectiveRange, isMelee, meleeVerticalReach, attackerIsFlying, includeFlags);
		}

		private bool IsReachableWithinRangeThreadSafe(Entity attacker, Entity target, Vector3 attackerPos, Vector3 targetPos, float effectiveRange, bool isMelee, float meleeVerticalReach, bool attackerIsFlying, int includeFlags)
		{
			if (attackerIsFlying || !isMelee)
			{
				// Ranged attackers (like towers) shoot through the air and do not need a navmesh path.
				return Distance(attackerPos, targetPos) <= effectiveRange + RouteReachMargin;
			}

			var navMeshQuery = _currentNavMeshQuery;
			if (navMeshQuery == null)
			{
				return IsVerticallyReachableForRange(attackerIsFlying, attackerPos, targetPos, effectiveRange, isMelee, meleeVerticalReach);
			}

			var key = (attacker, target);
			if (IsRouteReachabilityCached(key, navMeshQuery, attackerPos, targetPos, out bool cachedResult))
			{
				return cachedResult;
			}

			var threadQuery = GetThreadLocalNavMeshQuery(navMeshQuery, _currentNavMesh) ?? navMeshQuery;
			var threadPathfinder = _threadLocalPathfinder.Value ?? _context.NavMeshPathfinder;

			PathFollow pf = default;
			threadPathfinder.ComputePath(threadQuery, attackerPos, targetPos, (ushort)includeFlags, ref pf);

			bool reachable = pf.HasValidCorridor && pf.WaypointCount > 0;
			if (reachable)
			{
				var last = pf.Waypoints[pf.WaypointCount - 1];
				reachable = isMelee
					? EvaluateMeleeReachability(last, targetPos, effectiveRange, meleeVerticalReach)
					: Distance(last, targetPos) <= effectiveRange + RouteReachMargin;
			}

			_routeReachabilityCache[key] = new RouteReachabilityEntry(reachable, attackerPos, targetPos, navMeshQuery, _combatTotalTime);

			return reachable;
		}

		private DtNavMeshQuery? GetThreadLocalNavMeshQuery(DtNavMeshQuery fallbackQuery, DtNavMesh? navMesh)
		{
			if (navMesh == null) return fallbackQuery;
			var local = _threadLocalNavQuery.Value;
			if (local.Mesh != navMesh || local.Query == null)
			{
				local = (navMesh, new DtNavMeshQuery(navMesh));
				_threadLocalNavQuery.Value = local;
			}
			return local.Query;
		}

		private bool TryGetNavMeshQuery(out DtNavMeshQuery? navMeshQuery, out DtNavMesh? navMesh)
		{
			navMeshQuery = null;
			navMesh = null;
			Entity terrainEntity = FindTerrainEntity();
			if (terrainEntity == Entity.Null || !_context.World.Has<TerrainState>(terrainEntity)) return false;

			ref var ts = ref _context.World.Get<TerrainState>(terrainEntity);
			if (ts.NavMeshQuery == null) return false;

			navMeshQuery = ts.NavMeshQuery;
			navMesh = ts.NavMesh;
			return true;
		}

		private bool IsRouteReachabilityCached((Entity, Entity) key, DtNavMeshQuery navMeshQuery, Vector3 attackerPos, Vector3 targetPos, out bool reachable)
		{
			reachable = false;
			if (!_routeReachabilityCache.TryGetValue(key, out var cached)) return false;
			if (cached.Query != navMeshQuery) return false;
			if (Distance(cached.AttackerPos, attackerPos) > RouteCacheAttackerMoveLimit) return false;
			if (Distance(cached.TargetPos, targetPos) > RouteCacheTargetMoveLimit) return false;
			float maxAge = cached.Reachable ? RouteCacheMaxAge : RouteCacheNegativeMaxAge;
			if (_combatTotalTime - cached.Timestamp > maxAge) return false;

			reachable = cached.Reachable;
			return true;
		}

		private bool EvaluateMeleeReachability(Vector3 last, Vector3 targetPos, float effectiveRange, float meleeVerticalReach)
		{
			float horiz = new Vector2(last.X - targetPos.X, last.Z - targetPos.Z).Length();
			float vert = Math.Abs(last.Y - targetPos.Y);
			return horiz <= Math.Max(0.5f, effectiveRange) && vert <= meleeVerticalReach;
		}

		private bool IsVerticallyReachableForRange(bool isFlying, Vector3 attackerPos, Vector3 targetPos, float effectiveRange, bool isMelee, float meleeVerticalReach)
		{
			if (isFlying)
			{
				return true;
			}

			float climbRequired = targetPos.Y - attackerPos.Y;
			if (isMelee)
				return climbRequired <= meleeVerticalReach + VerticalReachTolerance;
			else
				return climbRequired <= effectiveRange + VerticalReachTolerance;
		}

		private void UpdateChaseTracking(Entity entity, float dist, Entity target)
		{
			if (!_lastChaseDist.TryGetValue(entity, out float prevDist))
			{
				_chaseStuckTime.Remove(entity);
				_lastChaseDist[entity] = dist;
				return;
			}

			float progress = prevDist - dist;
			if (progress > ChaseProgressEpsilon)
			{
				_chaseStuckTime.Remove(entity);
			}
			else
			{
				_chaseStuckTime[entity] = _chaseStuckTime.TryGetValue(entity, out float stuck)
					? stuck + _combatDelta
					: _combatDelta;

				// Do not abandon target if stuck; allow units to jostle and swarm
			}

			_lastChaseDist[entity] = dist;
		}
	}
}