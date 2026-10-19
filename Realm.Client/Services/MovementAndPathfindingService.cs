using System;
using System.Collections.Generic;
using System.Numerics;
using Realm.Ecs;
using DotRecast.Core.Numerics;
using Realm.Ecs.Common;
using Realm.Ecs.Components.Core;
using Realm.Ecs.Components.Movement;
using Realm.Ecs.Components.Tags;
using Realm.Ecs.Components.Terrain;
using Realm.Ecs.Services;
using PathingFlags = Realm.Ecs.Components.Movement.PathingFlags;

namespace Realm.Client.Services
{
	public class MovementAndPathfindingService
	{
		private Entity _resolvedWorldEntity = Entity.Null;

		private Entity ActiveWorldEntity
		{
			get
			{
				if (_resolvedWorldEntity == Entity.Null || !GameContext.World.IsAlive(_resolvedWorldEntity))
				{
					if (ECSContext.WorldEntity != Entity.Null && GameContext.World.IsAlive(ECSContext.WorldEntity))
					{
						_resolvedWorldEntity = ECSContext.WorldEntity;
					}
					else
					{
						var worldQuery = QueryCache.AllTerrainStateQuery;
						GameContext.World.Query(in worldQuery, entity => _resolvedWorldEntity = entity);
					}
				}

				return _resolvedWorldEntity;
			}
		}

		private float _fDelta;
		private readonly float _collisionCellSize = GameplayConstants.PathfindingGridSize;

		// Collision separation factor is defined in GameplayConstants so combat melee reach
		// can use the same value.
		private const float StuckEscalationTime = 1.5f;

		// Maximum climb between a unit's current position and the navmesh polygon it snaps
		// to. Kept low so units are not dragged across terrace/mountain lips (the navmesh
		// excludes the slope walls themselves, but a wide nearest-poly search can otherwise
		// pull a unit a whole tier step up a stepped hillside).
		private const float NavMeshMaxSnapClimb = 0.5f;
		private static readonly RcVec3f NavMeshSnapExtents = new RcVec3f(1.5f, 2.0f, 1.5f);
		private const float VerticalFollowRate = 30f;
		private const float DefaultAcceleration = 25f;

		private readonly struct SpatialGridEntry
		{
			public readonly Entity Entity;
			public readonly Vector3 Position;
			public readonly Vector3 Velocity;
			public readonly float CollisionRadius;
			public readonly bool HasVelocity;

			public SpatialGridEntry(Entity entity, Vector3 position, Vector3 velocity, float collisionRadius, bool hasVelocity)
			{
				Entity = entity;
				Position = position;
				Velocity = velocity;
				CollisionRadius = collisionRadius;
				HasVelocity = hasVelocity;
			}
		}

		private readonly Dictionary<long, List<SpatialGridEntry>> _unitGrid = new();
		private readonly Dictionary<long, List<SpatialGridEntry>> _propGrid = new();
		private readonly List<List<SpatialGridEntry>> _listPool = new();

		private readonly List<Entity> _tickArrivedUnits = new();

		private readonly QueryDescription _movementQuery = QueryCache.AllPositionAndMoveToAndMovementStatsNoneDeadQuery;
		private readonly QueryDescription _spatialQuery = QueryCache.AllPositionQuery;
		private EntityCallback<Position, MoveTo, MovementStats> _movementQueryDelegate;
		private EntityCallback<Position> _spatialQueryDelegate;

		private TerrainState _currentTerrainState;
		private bool _hasTerrainState;
		private Func<Vector3, float>? _editorHeightProvider;

		public Func<Vector3, float>? EditorHeightProvider
		{
			get => _editorHeightProvider;
			set => _editorHeightProvider = value;
		}

		public MovementAndPathfindingService()
		{
			_movementQueryDelegate = MovementQueryAction;
			_spatialQueryDelegate = SpatialGridQueryAction;
		}

		public void StepMovement(float delta)
		{
			_fDelta = delta;
			_tickArrivedUnits.Clear();

			RefreshTerrainState();

			RebuildSpatialGrid();

			GameContext.World.Query(in _movementQuery, _movementQueryDelegate);

			foreach (var entity in _tickArrivedUnits)
			{
				if (GameContext.World.IsAlive(entity) && GameContext.World.Has<MoveTo>(entity))
				{
					if (GameContext.World.Has<PathFollow>(entity))
					{
						GameContext.World.Remove<PathFollow>(entity);
					}

					if (GameContext.World.Has<WaypointQueue>(entity))
					{
						var q = GameContext.World.Get<WaypointQueue>(entity);
						if (q.Count > 0)
						{
							var nextWaypoint = q.Dequeue();
							GameContext.World.Set(entity, q);
							GameContext.World.Set(entity, new MoveTo(nextWaypoint));
							continue;
						}
						else
						{
							GameContext.World.Remove<WaypointQueue>(entity);
						}
					}

					GameContext.World.Remove<MoveTo>(entity);
				}
			}
		}

		public void RefreshTerrainState()
		{
			_hasTerrainState = GameContext.World.IsAlive(ActiveWorldEntity) && GameContext.World.Has<TerrainState>(ActiveWorldEntity);
			if (_hasTerrainState)
			{
				_currentTerrainState = GameContext.World.Get<TerrainState>(ActiveWorldEntity);
			}
		}

		public EntityCallback<Position, MoveTo, MovementStats> EditorMovementQueryDelegate => _movementQueryDelegate;

		private static Vector3 MoveTowards(Vector3 from, Vector3 to, float maxDelta)
		{
			Vector3 diff = to - from;
			float len = diff.Length();
			if (len <= maxDelta || len <= 0.0001f)
			{
				return to;
			}

			return from + diff * (maxDelta / len);
		}

		private long GetCellKey(float x, float z)
		{
			int cx = (int)Math.Floor(x / _collisionCellSize);
			int cz = (int)Math.Floor(z / _collisionCellSize);
			return ((long)cx << 32) | (uint)cz;
		}

		private List<SpatialGridEntry> GetEntityListFromPool()
		{
			if (_listPool.Count > 0)
			{
				int lastIdx = _listPool.Count - 1;
				var list = _listPool[lastIdx];
				_listPool.RemoveAt(lastIdx);
				return list;
			}

			return new List<SpatialGridEntry>(16);
		}

		private void RebuildSpatialGrid()
		{
			foreach (var list in _unitGrid.Values)
			{
				list.Clear();
				_listPool.Add(list);
			}

			_unitGrid.Clear();

			foreach (var list in _propGrid.Values)
			{
				list.Clear();
				_listPool.Add(list);
			}

			_propGrid.Clear();

			GameContext.World.Query(in _spatialQuery, _spatialQueryDelegate);
		}

		private void SpatialGridQueryAction(Entity entity, ref Position p)
		{
			var world = GameContext.World;
			if (world.Has<Dead>(entity)) return;

			Vector3 pos = p.Value;
			long key = GetCellKey(pos.X, pos.Z);

			if (world.Has<DefinitionId>(entity))
			{
				if (!_unitGrid.TryGetValue(key, out var list))
				{
					list = GetEntityListFromPool();
					_unitGrid[key] = list;
				}

				float scale = world.Has<CollisionScale>(entity) ? world.Get<CollisionScale>(entity).Value : 1.0f;
				float radius = world.Has<CollisionRadius>(entity)
					? world.Get<CollisionRadius>(entity).Value * scale
					: scale * GameplayConstants.DefaultCollisionRadius;
				bool hasVel = world.Has<Velocity>(entity);
				Vector3 vel = hasVel ? world.Get<Velocity>(entity).Value : Vector3.Zero;

				list.Add(new SpatialGridEntry(entity, pos, vel, radius, hasVel));
			}
			else if (world.Has<PropIdentity>(entity))
			{
				if (!_propGrid.TryGetValue(key, out var list))
				{
					list = GetEntityListFromPool();
					_propGrid[key] = list;
				}

				float scaleProp = world.Has<CollisionScale>(entity) ? world.Get<CollisionScale>(entity).Value : 1.0f;
				float radius = world.Has<CollisionRadius>(entity)
					? world.Get<CollisionRadius>(entity).Value * scaleProp
					: scaleProp * GameplayConstants.DefaultPropCollisionRadius;

				list.Add(new SpatialGridEntry(entity, pos, Vector3.Zero, radius, false));
			}
		}

		private void MovementQueryAction(Entity entity, ref Position pos, ref MoveTo moveTo, ref MovementStats stats)
		{
			if (TryHandleStun(entity)) return;

			ushort pathingFlags = (ushort)(GameContext.World.Has<PathingFlags>(entity) ? GameContext.World.Get<PathingFlags>(entity).Value : 8);
			bool isFlying = ((TerrainPathingFlags)pathingFlags & TerrainPathingFlags.Flying) != 0;

			bool hasPf = GameContext.World.Has<PathFollow>(entity);
			PathFollow pf = GetOrCreatePathFollow(hasPf, entity, pos.Value, moveTo.Target);

			bool forceReplan = UpdateStuckState(hasPf, entity, pos.Value, stats, ref pf);

			UpdatePathIfNeeded(pathingFlags, isFlying, pos.Value, moveTo.Target, forceReplan, ref pf);

			if (CheckArrival(entity, pos.Value, moveTo.Target, stats, ref pf))
			{
				SavePathFollow(hasPf, entity, pf);
				return;
			}

			UpdateUnitMovement(entity, ref pos, moveTo.Target, stats, isFlying, ref pf);
			SavePathFollow(hasPf, entity, pf);
		}

		private bool TryHandleStun(Entity entity)
		{
			if (!GameContext.World.Has<Buffs>(entity) || !GameContext.World.Get<Buffs>(entity).Value.ContainsKey("stun"))
				return false;

			if (GameContext.World.Has<Velocity>(entity))
			{
				GameContext.World.Set(entity, new Velocity(Vector3.Zero));
			}

			return true;
		}

		private PathFollow GetOrCreatePathFollow(bool hasPf, Entity entity, Vector3 posValue, Vector3 target)
		{
			if (hasPf) return GameContext.World.Get<PathFollow>(entity);

			return new PathFollow
			{
				WaypointCount = 0,
				CurrentWaypointIndex = 0,
				Target = target,
				LastPosition = posValue,
				StuckTime = 0f,
				TimeSinceLastReplan = 0f,
				IsJitterReplanned = false
			};
		}

		private bool UpdateStuckState(bool hasPf, Entity entity, Vector3 posValue, MovementStats stats, ref PathFollow pf)
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

			float actualSpeed = GetActualSpeed(entity, stats);
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

		private void UpdatePathIfNeeded(ushort pathingFlags, bool isFlying, Vector3 start, Vector3 target, bool forceReplan, ref PathFollow pf)
		{
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
				GameContext.NavMeshPathfinder.ComputePath(_currentTerrainState.NavMeshQuery, start, target, pathingFlags, ref pf);
			}

			pf.Target = target;
		}

		private bool CheckArrival(Entity entity, Vector3 current, Vector3 target, MovementStats stats, ref PathFollow pf)
		{
			Vector3 currentTarget = GetCurrentWaypointTarget(target, pf);

			float diffX = current.X - currentTarget.X;
			float diffZ = current.Z - currentTarget.Z;
			float horizontalDist = MathF.Sqrt(diffX * diffX + diffZ * diffZ);
			float arrivalThreshold = Math.Max(0.5f, stats.Speed * _fDelta * 1.2f);

			if (horizontalDist < arrivalThreshold)
			{
				pf.CurrentWaypointIndex++;
			}

			if (pf.CurrentWaypointIndex >= pf.WaypointCount)
			{
				_tickArrivedUnits.Add(entity);
				GameContext.World.SetOrAdd(entity, new Velocity(Vector3.Zero));
				return true;
			}

			return false;
		}

		private Vector3 GetCurrentWaypointTarget(Vector3 finalTarget, PathFollow pf)
		{
			if (pf.CurrentWaypointIndex < pf.WaypointCount)
			{
				return pf.Waypoints[pf.CurrentWaypointIndex];
			}

			return finalTarget;
		}

		private static readonly StatId MovementSpeedStatId = new StatId("MovementSpeed");

		private float GetActualSpeed(Entity entity, MovementStats stats)
		{
			if (stats.Speed > 0f)
			{
				return stats.Speed;
			}

			float actualSpeed = ECSContext.StatService != null ? ECSContext.StatService.GetStatValue(entity, MovementSpeedStatId) : 0f;
			return actualSpeed <= 0 ? stats.Speed : actualSpeed;
		}

		private void UpdateUnitMovement(Entity entity, ref Position pos, Vector3 target, MovementStats stats, bool isFlying, ref PathFollow pf)
		{
			Vector3 currentTarget = GetCurrentWaypointTarget(target, pf);
			Vector3 current = pos.Value;
			float actualSpeed = GetActualSpeed(entity, stats);

			Vector3 toTarget = currentTarget - current;
			if (isFlying) toTarget.Y = 0f;

			Vector3 desiredVelocity = toTarget.LengthSquared() < 0.000001f
				? Vector3.Zero
				: Vector3.Normalize(toTarget) * actualSpeed;

			Vector3 steering = CalculateSteering(entity, current, desiredVelocity, actualSpeed, pf.StuckTime >= StuckEscalationTime);
			Vector3 currentVelocity = GameContext.World.Has<Velocity>(entity) ? GameContext.World.Get<Velocity>(entity).Value : Vector3.Zero;

			Vector3 velocity = stats.Acceleration > 0f ? MoveTowards(currentVelocity, steering, stats.Acceleration * _fDelta) : steering;
			Vector3 nextPos = current + velocity * _fDelta;

			if (!isFlying)
			{
				ApplyCollisionAvoidance(entity, current, ref nextPos);
			}

			ApplyVerticalFollow(pos.Value, isFlying, ref nextPos, ref velocity);

			pos.Value = nextPos;
			GameContext.World.SetOrAdd(entity, new Velocity(velocity));
		}

		private Vector3 CalculateSteering(Entity entity, Vector3 current, Vector3 desiredVelocity, float actualSpeed, bool stuck)
		{
			Vector3 cohesion = Vector3.Zero;
			Vector3 alignment = Vector3.Zero;
			Vector3 separation = Vector3.Zero;
			int neighborCount = 0;

			int currentCellX = (int)Math.Floor(current.X / _collisionCellSize);
			int currentCellZ = (int)Math.Floor(current.Z / _collisionCellSize);

			for (int dx = -1; dx <= 1; dx++)
			{
				for (int dz = -1; dz <= 1; dz++)
				{
					long key = ((long)(currentCellX + dx) << 32) | (uint)(currentCellZ + dz);
					AccumulateSteeringForces(entity, current, key, ref cohesion, ref alignment, ref separation, ref neighborCount);
				}
			}

			if (neighborCount == 0) return desiredVelocity;

			return FinalizeSteeringForce(current, desiredVelocity, actualSpeed, stuck, cohesion, alignment, separation, neighborCount);
		}

		private void AccumulateSteeringForces(Entity entity, Vector3 current, long key, ref Vector3 cohesion, ref Vector3 alignment, ref Vector3 separation, ref int neighborCount)
		{
			if (!_unitGrid.TryGetValue(key, out var list)) return;

			for (int i = 0; i < list.Count; i++)
			{
				var other = list[i];
				ProcessNeighborSteering(entity, current, in other, ref cohesion, ref alignment, ref separation, ref neighborCount);
			}
		}

		private void ProcessNeighborSteering(Entity entity, Vector3 current, in SpatialGridEntry other, ref Vector3 cohesion, ref Vector3 alignment, ref Vector3 separation, ref int neighborCount)
		{
			if (other.Entity == entity) return;

			Vector3 otherPos = other.Position;
			Vector3 diff = current - otherPos;
			float distSq = diff.LengthSquared();
			if (distSq <= 0f || distSq >= 16.0f) return;

			cohesion += otherPos;
			if (other.HasVelocity)
			{
				alignment += other.Velocity;
			}

			separation += diff / distSq;
			neighborCount++;
		}

		private Vector3 FinalizeSteeringForce(Vector3 current, Vector3 desiredVelocity, float actualSpeed, bool stuck, Vector3 cohesion, Vector3 alignment, Vector3 separation, int neighborCount)
		{
			cohesion = (cohesion / neighborCount) - current;
			if (cohesion.LengthSquared() > 0.001f) cohesion = Vector3.Normalize(cohesion) * actualSpeed;

			alignment = alignment / neighborCount;
			if (alignment.LengthSquared() > 0.001f) alignment = Vector3.Normalize(alignment) * actualSpeed;

			if (separation.LengthSquared() > 0.001f) separation = Vector3.Normalize(separation) * actualSpeed;

			float desiredWeight = stuck ? 0.75f : 0.90f;
			float separationWeight = stuck ? 0.30f : 0.04f;
			Vector3 steering = desiredVelocity * desiredWeight + separation * separationWeight + cohesion * 0.04f + alignment * 0.03f;

			return steering.LengthSquared() > 0.001f ? Vector3.Normalize(steering) * actualSpeed : steering;
		}

		private void ApplyCollisionAvoidance(Entity entity, Vector3 current, ref Vector3 nextPos)
		{
			float scale1 = GameContext.World.Has<CollisionScale>(entity) ? GameContext.World.Get<CollisionScale>(entity).Value : 1.0f;
			float r1 = GameContext.World.Has<CollisionRadius>(entity)
				? GameContext.World.Get<CollisionRadius>(entity).Value * scale1
				: scale1 * GameplayConstants.DefaultCollisionRadius;

			int baseCx = (int)Math.Floor(nextPos.X / _collisionCellSize);
			int baseCz = (int)Math.Floor(nextPos.Z / _collisionCellSize);

			ApplyUnitCollision(entity, baseCx, baseCz, r1, ref nextPos);
			ApplyPropCollision(baseCx, baseCz, r1, ref nextPos);
		}

		private void ApplyUnitCollision(Entity entity, int baseCx, int baseCz, float r1, ref Vector3 nextPos)
		{
			for (int dx = -1; dx <= 1; dx++)
			{
				for (int dz = -1; dz <= 1; dz++)
				{
					long key = ((long)(baseCx + dx) << 32) | (uint)(baseCz + dz);
					if (!_unitGrid.TryGetValue(key, out var list)) continue;

					for (int i = 0; i < list.Count; i++)
					{
						var other = list[i];
						if (other.Entity == entity) continue;

						ResolveCollision(r1, other.CollisionRadius, other.Position, ref nextPos);
					}
				}
			}
		}

		private void ApplyPropCollision(int baseCx, int baseCz, float r1, ref Vector3 nextPos)
		{
			for (int dx = -1; dx <= 1; dx++)
			{
				for (int dz = -1; dz <= 1; dz++)
				{
					long key = ((long)(baseCx + dx) << 32) | (uint)(baseCz + dz);
					if (!_propGrid.TryGetValue(key, out var list)) continue;

					for (int i = 0; i < list.Count; i++)
					{
						var prop = list[i];
						ResolveCollision(r1, prop.CollisionRadius, prop.Position, ref nextPos);
					}
				}
			}
		}

		private void ResolveCollision(float r1, float r2, Vector3 otherPos, ref Vector3 nextPos)
		{
			float minDist = (r1 + r2) * GameplayConstants.CollisionSeparationFactor;
			float ox = nextPos.X - otherPos.X;
			float oz = nextPos.Z - otherPos.Z;
			float distSq = ox * ox + oz * oz;

			if (distSq >= minDist * minDist) return;

			float otherDist = (float)Math.Sqrt(distSq);
			Vector3 pushDir = otherDist < 0.001f
				? new Vector3(1f, 0f, 0f)
				: new Vector3(ox / otherDist, 0f, oz / otherDist);

			if (otherDist < 0.001f) otherDist = 1f;

			float overlap = minDist - otherDist;
			nextPos += pushDir * overlap;
		}

		private void ApplyVerticalFollow(Vector3 currentPos, bool isFlying, ref Vector3 nextPos, ref Vector3 velocity)
		{
			bool snappedToNavMesh = false;
			float snappedSurfaceY = nextPos.Y;

			if (_hasTerrainState && _currentTerrainState.NavMeshQuery != null && !isFlying)
			{
				var snapPos = new RcVec3f(nextPos.X, nextPos.Y, nextPos.Z);
				_currentTerrainState.NavMeshQuery.FindNearestPoly(snapPos, NavMeshSnapExtents, GameContext.NavMeshPathfinder.Filter, out long snapRef, out var snappedPt, out _);
				if (snapRef != 0)
				{
					float climb = MathF.Abs(snappedPt.Y - nextPos.Y);
					if (climb <= NavMeshMaxSnapClimb)
					{
						nextPos.X = snappedPt.X;
						nextPos.Z = snappedPt.Z;
						snappedToNavMesh = true;
						snappedSurfaceY = snappedPt.Y;
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
				ECSContext.TerrainNavMeshService.GetHeightAndNormal(in _currentTerrainState, nextPos.X, nextPos.Z, out float groundHeight, out _);
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

		private void SavePathFollow(bool hasPf, Entity entity, PathFollow pf)
		{
			if (hasPf)
			{
				GameContext.World.Set(entity, pf);
			}
			else
			{
				GameContext.World.Add(entity, pf);
			}
		}
	}
}