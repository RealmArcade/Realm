using Arch.Core;
using Realm.Ecs.Common;
using DotRecast.Core.Numerics;
using Realm.Ecs.Components.Core;
using Realm.Ecs.Components.Movement;
using Realm.Ecs.Components.Tags;
using Realm.Ecs.Components.Terrain;
using Realm.Ecs.Services;
using System;
using System.Collections.Generic;

internal class MovementAndPathfindingService
{
	private readonly WorldAccessor _ecsWorldAccessor;
	private World EcsWorld => _ecsWorldAccessor.Current;
	private readonly Entity _worldEntity;
	private Entity _resolvedWorldEntity = Entity.Null;
	private Entity ActiveWorldEntity
	{
		get
		{
			if (_resolvedWorldEntity == Entity.Null || !EcsWorld.IsAlive(_resolvedWorldEntity))
			{
				if (_worldEntity != Entity.Null && EcsWorld.IsAlive(_worldEntity))
				{
					_resolvedWorldEntity = _worldEntity;
				}
				else
				{
					var worldQuery = Realm.Ecs.Common.QueryCache.AllTerrainStateQuery;
					EcsWorld.Query(in worldQuery, entity => _resolvedWorldEntity = entity);
				}
			}
			return _resolvedWorldEntity;
		}
	}
	private readonly NavMeshPathfinder _pathfinder;
	private readonly TerrainNavMeshService _terrainNavMeshService;
	private readonly StatService? _statService;

	private float _fDelta;
	private readonly float _collisionCellSize = Realm.Ecs.Common.GameplayConstants.PathfindingGridSize;

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

	private readonly Dictionary<long, List<Entity>> _unitGrid = new();
	private readonly Dictionary<long, List<Entity>> _propGrid = new();
	private readonly List<List<Entity>> _listPool = new();

	private readonly List<Entity> _tickArrivedUnits = new();

	private readonly QueryDescription _movementQuery = Realm.Ecs.Common.QueryCache.AllPositionAndMoveToAndMovementStatsNoneDeadQuery;
	private readonly QueryDescription _spatialQuery = Realm.Ecs.Common.QueryCache.AllPositionQuery;
	private ForEachWithEntity<Position, MoveTo, MovementStats> _movementQueryDelegate;

	private TerrainState _currentTerrainState;
	private bool _hasTerrainState;
	private Func<System.Numerics.Vector3, float>? _editorHeightProvider;
	public Func<System.Numerics.Vector3, float>? EditorHeightProvider
	{
		get => _editorHeightProvider;
		set => _editorHeightProvider = value;
	}

	public MovementAndPathfindingService(WorldAccessor ecsWorldAccessor, Entity worldEntity, NavMeshPathfinder pathfinder)
	{
		_ecsWorldAccessor = ecsWorldAccessor;
		_worldEntity = worldEntity;
		_pathfinder = pathfinder;
		_terrainNavMeshService = ServiceLocator.Get<TerrainNavMeshService>();
		_statService = ServiceLocator.TryGet<StatService>();
		_movementQueryDelegate = MovementQueryAction;
	}

	public void StepMovement(float delta)
	{
		_fDelta = delta;
		_tickArrivedUnits.Clear();

		RefreshTerrainState();

		RebuildSpatialGrid();

		EcsWorld.Query(in _movementQuery, _movementQueryDelegate);

		foreach (var entity in _tickArrivedUnits)
		{
			if (EcsWorld.IsAlive(entity) && EcsWorld.Has<MoveTo>(entity))
			{
				if (EcsWorld.Has<PathFollow>(entity))
				{
					EcsWorld.Remove<PathFollow>(entity);
				}
				if (EcsWorld.Has<WaypointQueue>(entity))
				{
					var q = EcsWorld.Get<WaypointQueue>(entity);
					if (q.Count > 0)
					{
						var nextWaypoint = q.Dequeue();
						EcsWorld.Set(entity, q);
						EcsWorld.Set(entity, new MoveTo(nextWaypoint));
						continue;
					}
					else
					{
						EcsWorld.Remove<WaypointQueue>(entity);
					}
				}
				EcsWorld.Remove<MoveTo>(entity);
			}
		}
	}

	public void RefreshTerrainState()
	{
		_hasTerrainState = EcsWorld.IsAlive(ActiveWorldEntity) && EcsWorld.Has<TerrainState>(ActiveWorldEntity);
		if (_hasTerrainState)
		{
			_currentTerrainState = EcsWorld.Get<TerrainState>(ActiveWorldEntity);
		}
	}

	public ForEachWithEntity<Position, MoveTo, MovementStats> EditorMovementQueryDelegate => _movementQueryDelegate;

	private static System.Numerics.Vector3 MoveTowards(System.Numerics.Vector3 from, System.Numerics.Vector3 to, float maxDelta)
	{
		System.Numerics.Vector3 diff = to - from;
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

	private List<Entity> GetEntityListFromPool()
	{
		if (_listPool.Count > 0)
		{
			int lastIdx = _listPool.Count - 1;
			var list = _listPool[lastIdx];
			_listPool.RemoveAt(lastIdx);
			return list;
		}
		return new List<Entity>(16);
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

		EcsWorld.Query(in _spatialQuery, (Entity entity, ref Position p) =>
		{
			if (EcsWorld.Has<Dead>(entity)) return;

			float x = p.Value.X;
			float z = p.Value.Z;
			long key = GetCellKey(x, z);

			if (EcsWorld.Has<DefinitionId>(entity))
			{
				if (!_unitGrid.TryGetValue(key, out var list))
				{
					list = GetEntityListFromPool();
					_unitGrid[key] = list;
				}
				list.Add(entity);
			}
			else if (EcsWorld.Has<PropIdentity>(entity))
			{
				if (!_propGrid.TryGetValue(key, out var list))
				{
					list = GetEntityListFromPool();
					_propGrid[key] = list;
				}
				list.Add(entity);
			}
		});
	}

	private void MovementQueryAction(Entity entity, ref Position pos, ref MoveTo moveTo, ref MovementStats stats)
	{
		if (TryHandleStun(entity)) return;

		ushort pathingFlags = (ushort)(EcsWorld.Has<PathingFlags>(entity) ? EcsWorld.Get<PathingFlags>(entity).Value : 8);
		bool isFlying = ((TerrainPathingFlags)pathingFlags & TerrainPathingFlags.Flying) != 0;

		bool hasPf = EcsWorld.Has<PathFollow>(entity);
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
		if (!EcsWorld.Has<Buffs>(entity) || !EcsWorld.Get<Buffs>(entity).Value.ContainsKey("stun"))
			return false;

		if (EcsWorld.Has<Velocity>(entity))
		{
			EcsWorld.Set(entity, new Velocity(System.Numerics.Vector3.Zero));
		}
		return true;
	}

	private PathFollow GetOrCreatePathFollow(bool hasPf, Entity entity, System.Numerics.Vector3 posValue, System.Numerics.Vector3 target)
	{
		if (hasPf) return EcsWorld.Get<PathFollow>(entity);
		
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

	private bool UpdateStuckState(bool hasPf, Entity entity, System.Numerics.Vector3 posValue, MovementStats stats, ref PathFollow pf)
	{
		if (!hasPf)
		{
			pf.LastPosition = posValue;
			pf.StuckTime = 0f;
			pf.TimeSinceLastReplan = 0f;
			pf.IsJitterReplanned = false;
			return false;
		}

		float distMoved = System.Numerics.Vector3.Distance(posValue, pf.LastPosition);
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

	private void UpdatePathIfNeeded(ushort pathingFlags, bool isFlying, System.Numerics.Vector3 start, System.Numerics.Vector3 target, bool forceReplan, ref PathFollow pf)
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
			_pathfinder.ComputePath(_currentTerrainState.NavMeshQuery, start, target, pathingFlags, ref pf);
		}
		pf.Target = target;
	}

	private bool CheckArrival(Entity entity, System.Numerics.Vector3 current, System.Numerics.Vector3 target, MovementStats stats, ref PathFollow pf)
	{
		System.Numerics.Vector3 currentTarget = GetCurrentWaypointTarget(target, pf);

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
			EcsWorld.SetOrAdd(entity, new Velocity(System.Numerics.Vector3.Zero));
			return true;
		}
		
		return false;
	}

	private System.Numerics.Vector3 GetCurrentWaypointTarget(System.Numerics.Vector3 finalTarget, PathFollow pf)
	{
		if (pf.CurrentWaypointIndex < pf.WaypointCount)
		{
			return pf.Waypoints[pf.CurrentWaypointIndex];
		}
		return finalTarget;
	}

	private float GetActualSpeed(Entity entity, MovementStats stats)
	{
		float actualSpeed = _statService != null ? _statService.GetStatValue(entity, new Realm.Ecs.Common.StatId("MovementSpeed")) : 0f;
		return actualSpeed <= 0 ? stats.Speed : actualSpeed;
	}

	private void UpdateUnitMovement(Entity entity, ref Position pos, System.Numerics.Vector3 target, MovementStats stats, bool isFlying, ref PathFollow pf)
	{
		System.Numerics.Vector3 currentTarget = GetCurrentWaypointTarget(target, pf);
		System.Numerics.Vector3 current = pos.Value;
		float actualSpeed = GetActualSpeed(entity, stats);

		System.Numerics.Vector3 toTarget = currentTarget - current;
		if (isFlying) toTarget.Y = 0f;

		System.Numerics.Vector3 desiredVelocity = toTarget.LengthSquared() < 0.000001f 
			? System.Numerics.Vector3.Zero 
			: System.Numerics.Vector3.Normalize(toTarget) * actualSpeed;

		System.Numerics.Vector3 steering = CalculateSteering(entity, current, desiredVelocity, actualSpeed, pf.StuckTime >= StuckEscalationTime);
		System.Numerics.Vector3 currentVelocity = EcsWorld.Has<Velocity>(entity) ? EcsWorld.Get<Velocity>(entity).Value : System.Numerics.Vector3.Zero;
		
		System.Numerics.Vector3 velocity = stats.Acceleration > 0f ? MoveTowards(currentVelocity, steering, stats.Acceleration * _fDelta) : steering;
		System.Numerics.Vector3 nextPos = current + velocity * _fDelta;

		if (!isFlying)
		{
			ApplyCollisionAvoidance(entity, current, ref nextPos);
		}

		ApplyVerticalFollow(pos.Value, isFlying, ref nextPos, ref velocity);
		
		pos.Value = nextPos;
		EcsWorld.SetOrAdd(entity, new Velocity(velocity));
	}

	private System.Numerics.Vector3 CalculateSteering(Entity entity, System.Numerics.Vector3 current, System.Numerics.Vector3 desiredVelocity, float actualSpeed, bool stuck)
	{
		System.Numerics.Vector3 cohesion = System.Numerics.Vector3.Zero;
		System.Numerics.Vector3 alignment = System.Numerics.Vector3.Zero;
		System.Numerics.Vector3 separation = System.Numerics.Vector3.Zero;
		int neighborCount = 0;

		int currentCellX = (int)Math.Floor(current.X / _collisionCellSize);
		int currentCellZ = (int)Math.Floor(current.Z / _collisionCellSize);

		for (int dx = -1; dx <= 1; dx++)
		{
			for (int dz = -1; dz <= 1; dz++)
			{
				long key = ((long)(currentCellX + dx) << 32) | (uint)(currentCellZ + dz);
				if (!_unitGrid.TryGetValue(key, out var list)) continue;

				foreach (var other in list)
				{
					if (other == entity || !EcsWorld.Has<Position>(other)) continue;

					var otherPos = EcsWorld.Get<Position>(other).Value;
					float neighborDist = System.Numerics.Vector3.Distance(current, otherPos);
					if (neighborDist > 0f && neighborDist < 4.0f)
					{
						cohesion += otherPos;
						if (EcsWorld.Has<Velocity>(other))
						{
							alignment += EcsWorld.Get<Velocity>(other).Value;
						}
						separation += System.Numerics.Vector3.Normalize(current - otherPos) / neighborDist;
						neighborCount++;
					}
				}
			}
		}

		if (neighborCount == 0) return desiredVelocity;

		cohesion = (cohesion / neighborCount) - current;
		if (cohesion.LengthSquared() > 0.001f) cohesion = System.Numerics.Vector3.Normalize(cohesion) * actualSpeed;

		alignment = alignment / neighborCount;
		if (alignment.LengthSquared() > 0.001f) alignment = System.Numerics.Vector3.Normalize(alignment) * actualSpeed;

		if (separation.LengthSquared() > 0.001f) separation = System.Numerics.Vector3.Normalize(separation) * actualSpeed;

		float desiredWeight = stuck ? 0.75f : 0.90f;
		float separationWeight = stuck ? 0.30f : 0.04f;
		System.Numerics.Vector3 steering = desiredVelocity * desiredWeight + separation * separationWeight + cohesion * 0.04f + alignment * 0.03f;
		
		return steering.LengthSquared() > 0.001f ? System.Numerics.Vector3.Normalize(steering) * actualSpeed : steering;
	}

	private void ApplyCollisionAvoidance(Entity entity, System.Numerics.Vector3 current, ref System.Numerics.Vector3 nextPos)
	{
		float scale1 = EcsWorld.Has<CollisionScale>(entity) ? EcsWorld.Get<CollisionScale>(entity).Value : 1.0f;
		float r1 = EcsWorld.Has<CollisionRadius>(entity) 
			? EcsWorld.Get<CollisionRadius>(entity).Value * scale1 
			: scale1 * Realm.Ecs.Common.GameplayConstants.DefaultCollisionRadius;

		int baseCx = (int)Math.Floor(nextPos.X / _collisionCellSize);
		int baseCz = (int)Math.Floor(nextPos.Z / _collisionCellSize);

		ApplyUnitCollision(entity, baseCx, baseCz, r1, ref nextPos);
		ApplyPropCollision(baseCx, baseCz, r1, ref nextPos);
	}

	private void ApplyUnitCollision(Entity entity, int baseCx, int baseCz, float r1, ref System.Numerics.Vector3 nextPos)
	{
		for (int dx = -1; dx <= 1; dx++)
		{
			for (int dz = -1; dz <= 1; dz++)
			{
				long key = ((long)(baseCx + dx) << 32) | (uint)(baseCz + dz);
				if (!_unitGrid.TryGetValue(key, out var list)) continue;

				foreach (var otherEntity in list)
				{
					if (otherEntity == entity) continue;

					float scale2 = EcsWorld.Has<CollisionScale>(otherEntity) ? EcsWorld.Get<CollisionScale>(otherEntity).Value : 1.0f;
					float r2 = EcsWorld.Has<CollisionRadius>(otherEntity) 
						? EcsWorld.Get<CollisionRadius>(otherEntity).Value * scale2 
						: scale2 * Realm.Ecs.Common.GameplayConstants.DefaultCollisionRadius;

					ResolveCollision(r1, r2, EcsWorld.Get<Position>(otherEntity).Value, ref nextPos);
				}
			}
		}
	}

	private void ApplyPropCollision(int baseCx, int baseCz, float r1, ref System.Numerics.Vector3 nextPos)
	{
		for (int dx = -1; dx <= 1; dx++)
		{
			for (int dz = -1; dz <= 1; dz++)
			{
				long key = ((long)(baseCx + dx) << 32) | (uint)(baseCz + dz);
				if (!_propGrid.TryGetValue(key, out var list)) continue;

				foreach (var propEntity in list)
				{
					float scaleProp = EcsWorld.Has<CollisionScale>(propEntity) ? EcsWorld.Get<CollisionScale>(propEntity).Value : 1.0f;
					float r2 = EcsWorld.Has<CollisionRadius>(propEntity) 
						? EcsWorld.Get<CollisionRadius>(propEntity).Value * scaleProp 
						: scaleProp * Realm.Ecs.Common.GameplayConstants.DefaultPropCollisionRadius;

					ResolveCollision(r1, r2, EcsWorld.Get<Position>(propEntity).Value, ref nextPos);
				}
			}
		}
	}

	private void ResolveCollision(float r1, float r2, System.Numerics.Vector3 otherPos, ref System.Numerics.Vector3 nextPos)
	{
		float minDist = (r1 + r2) * Realm.Ecs.Common.GameplayConstants.CollisionSeparationFactor;
		float ox = nextPos.X - otherPos.X;
		float oz = nextPos.Z - otherPos.Z;
		float distSq = ox * ox + oz * oz;
		
		if (distSq >= minDist * minDist) return;

		float otherDist = (float)Math.Sqrt(distSq);
		System.Numerics.Vector3 pushDir = otherDist < 0.001f 
			? new System.Numerics.Vector3(1f, 0f, 0f) 
			: new System.Numerics.Vector3(ox / otherDist, 0f, oz / otherDist);
			
		if (otherDist < 0.001f) otherDist = 1f;

		float overlap = minDist - otherDist;
		nextPos += pushDir * overlap;
	}

	private void ApplyVerticalFollow(System.Numerics.Vector3 currentPos, bool isFlying, ref System.Numerics.Vector3 nextPos, ref System.Numerics.Vector3 velocity)
	{
		bool snappedToNavMesh = false;
		float snappedSurfaceY = nextPos.Y;
		
		if (_hasTerrainState && _currentTerrainState.NavMeshQuery != null && !isFlying)
		{
			var snapPos = new RcVec3f(nextPos.X, nextPos.Y, nextPos.Z);
			_currentTerrainState.NavMeshQuery.FindNearestPoly(snapPos, NavMeshSnapExtents, _pathfinder.Filter, out long snapRef, out var snappedPt, out _);
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
			_terrainNavMeshService.GetHeightAndNormal(in _currentTerrainState, nextPos.X, nextPos.Z, out float groundHeight, out _);
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
			EcsWorld.Set(entity, pf);
		}
		else
		{
			EcsWorld.Add(entity, pf);
		}
	}
}
