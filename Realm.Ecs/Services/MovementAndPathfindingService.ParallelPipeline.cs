// Extracted from MovementAndPathfindingService.cs (task 6 decomposition).
using System;
using System.Collections.Generic;
using System.Numerics;
using System.Threading;
using Realm.Ecs;
using DotRecast.Core.Numerics;
using DotRecast.Detour;
using Realm.Ecs.Common;
using Realm.Ecs.Components.Core;
using Realm.Ecs.Components.Meta;
using Realm.Ecs.Components.Movement;
using Realm.Ecs.Components.Tags;
using Realm.Ecs.Components.Terrain;
using Realm.Ecs.Services;

namespace Realm.Ecs.Services
{
	public partial class MovementAndPathfindingService
	{
		private void ProcessMovementWorkItemParallel(int index)
		{
			ref var item = ref _movementWorkItems[index];
			Entity entity = item.Entity;
			Vector3 posValue = item.PosValue;
			Vector3 moveTarget = item.MoveTarget;
			MovementStats stats = item.Stats;
			ushort pathingFlags = item.TraversableTerrain;
			bool isFlying = item.IsFlying;
			bool hasPf = item.HasPf;
			ref PathFollow pf = ref item.Pf;
			float actualSpeed = item.ActualSpeed;

			bool forceReplan = UpdateStuckStateParallel(hasPf, posValue, actualSpeed, ref pf);
			NavMeshStartRecovery startRecovery;
			OffMeshReplanCause replanCause;
			UpdatePathIfNeededParallel(pathingFlags, isFlying, posValue, moveTarget, forceReplan, ref pf, out startRecovery, out replanCause);
			item.StartRecovery = startRecovery;
			item.ReplanCause = replanCause;

			Vector3 currentTarget = GetCurrentWaypointTarget(moveTarget, pf);
			float diffX = posValue.X - currentTarget.X;
			float diffZ = posValue.Z - currentTarget.Z;
			float horizontalDist = MathF.Sqrt(diffX * diffX + diffZ * diffZ);
			float arrivalThreshold = Math.Max(0.5f, stats.Speed * _fDelta * 1.2f);

			if (horizontalDist < arrivalThreshold)
			{
				pf.CurrentWaypointIndex++;
			}

			if (pf.CurrentWaypointIndex >= pf.WaypointCount)
			{
				item.HasArrived = true;
				return;
			}

			currentTarget = GetCurrentWaypointTarget(moveTarget, pf);
			Vector3 toTarget = currentTarget - posValue;
			if (isFlying) toTarget.Y = 0f;

			Vector3 desiredVelocity = toTarget.LengthSquared() < 0.000001f
				? Vector3.Zero
				: Vector3.Normalize(toTarget) * actualSpeed;

			Vector3 steering = CalculateSteering(entity, posValue, desiredVelocity, actualSpeed, pf.StuckTime >= StuckEscalationTime);
			Vector3 currentVelocity = _context.World.Has<Velocity>(entity) ? _context.World.Get<Velocity>(entity).Value : Vector3.Zero;

			Vector3 velocity = stats.Acceleration > 0f ? MoveTowards(currentVelocity, steering, stats.Acceleration * _fDelta) : steering;
			Vector3 nextPos = posValue + velocity * _fDelta;

			if (!isFlying)
			{
				ApplyCollisionAvoidance(entity, posValue, ref nextPos);
			}

			ApplyVerticalFollowParallel(posValue, isFlying, ref nextPos, ref velocity);

			item.NextPosition = nextPos;
			item.NextVelocity = velocity;
		}

		private bool UpdateStuckStateParallel(bool hasPf, Vector3 posValue, float actualSpeed, ref PathFollow pf)
		{
			if (!hasPf)
			{
				pf.LastPosition = posValue;
				pf.StuckTime = 0f;
				pf.TimeSinceLastReplan = 0f;
				pf.IsJitterReplanned = false;
				return false;
			}

			float distMoved = Vector3.Distance(posValue, pf.LastPosition);
			pf.LastPosition = posValue;

			float expectedDist = actualSpeed * _fDelta;

			if (distMoved >= expectedDist * 0.1f)
			{
				pf.StuckTime = 0f;
				pf.TimeSinceLastReplan = 0f;
				pf.IsJitterReplanned = false;
				return false;
			}

			pf.StuckTime += _fDelta;
			if (pf.StuckTime < 0.1f) return false;

			pf.TimeSinceLastReplan += _fDelta;
			if (pf.TimeSinceLastReplan < 1.5f) return false;

			pf.TimeSinceLastReplan = 0f;
			pf.IsJitterReplanned = false;
			return true;
		}

		private void UpdatePathIfNeededParallel(ushort pathingFlags, bool isFlying, Vector3 start, Vector3 target, bool forceReplan, ref PathFollow pf, out NavMeshStartRecovery startRecovery, out OffMeshReplanCause replanCause)
		{
			startRecovery = default;
			replanCause = OffMeshReplanCause.None;

			if (pf.Target == target && pf.WaypointCount != 0 && !forceReplan) return;
			if (!_hasTerrainState) return;

			if (isFlying)
			{
				pf.WaypointCount = 1;
				pf.CurrentWaypointIndex = 0;
				pf.Waypoints[0] = target;
			}
			else
			{
				replanCause = ResolveReplanCause(pf, target);
				var navQuery = GetThreadLocalNavMeshQuery() ?? _currentTerrainState.NavMeshQuery;
				if (navQuery != null)
				{
					var pathfinder = _threadLocalPathfinder.Value!;
					pathfinder.ComputePath(navQuery, start, target, pathingFlags, ref pf, out startRecovery);
				}
			}

			pf.Target = target;
		}

		private static OffMeshReplanCause ResolveReplanCause(PathFollow pf, Vector3 target)
		{
			if (pf.WaypointCount == 0)
			{
				return OffMeshReplanCause.NoPath;
			}

			if (pf.Target != target)
			{
				return OffMeshReplanCause.Retarget;
			}

			return OffMeshReplanCause.Stuck;
		}

		private void ApplyVerticalFollowParallel(Vector3 currentPos, bool isFlying, ref Vector3 nextPos, ref Vector3 velocity)
		{
			bool snappedToNavMesh = false;
			float snappedSurfaceY = nextPos.Y;

			if (_hasTerrainState && !isFlying)
			{
				var navQuery = GetThreadLocalNavMeshQuery() ?? _currentTerrainState.NavMeshQuery;
				if (navQuery != null)
				{
					var pathfinder = _threadLocalPathfinder.Value!;
					var snapPos = new RcVec3f(nextPos.X, nextPos.Y, nextPos.Z);
					navQuery.FindNearestPoly(snapPos, NavMeshSnapExtents, pathfinder.Filter, out long snapRef, out var snappedPt, out _);
					if (snapRef != 0)
					{
						float climb = MathF.Abs(snappedPt.Y - nextPos.Y);
						if (climb <= NavMeshMaxSnapClimb)
						{
							snappedToNavMesh = true;
							snappedSurfaceY = snappedPt.Y;
						}
					}
				}
			}

			float desiredY;
			if (snappedToNavMesh)
			{
				desiredY = snappedSurfaceY;
				velocity.Y = 0f;
			}
			else if (_hasTerrainState)
			{
				_context.TerrainNavMeshService.GetHeightAndNormal(in _currentTerrainState, nextPos.X, nextPos.Z, out float groundHeight, out _);
				desiredY = groundHeight;
				velocity.Y = 0f;
			}
			else if (_editorHeightProvider != null)
			{
				desiredY = _editorHeightProvider(nextPos);
				velocity.Y = 0f;
			}
			else
			{
				desiredY = currentPos.Y;
			}

			float followFactor = Math.Clamp(VerticalFollowRate * _fDelta, 0f, 1f);
			nextPos.Y = currentPos.Y + (desiredY - currentPos.Y) * followFactor;
		}
	}
}
