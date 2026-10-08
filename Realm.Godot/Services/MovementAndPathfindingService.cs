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
		if (EcsWorld.Has<Buffs>(entity) && EcsWorld.Get<Buffs>(entity).Value.ContainsKey("stun"))
		{
			if (EcsWorld.Has<Velocity>(entity))
			{
				EcsWorld.Set(entity, new Velocity(System.Numerics.Vector3.Zero));
			}
			return;
		}

		int includeFlags = EcsWorld.Has<PathingFlags>(entity)
			? EcsWorld.Get<PathingFlags>(entity).Value
			: 8;

		ushort pathingFlags = (ushort)includeFlags;
		bool isFlying = ((TerrainPathingFlags)pathingFlags & TerrainPathingFlags.Flying) != 0;

		PathFollow pf;
		bool hasPf = EcsWorld.Has<PathFollow>(entity);
		if (hasPf)
		{
			pf = EcsWorld.Get<PathFollow>(entity);
		}
		else
		{
			pf = new PathFollow
			{
				WaypointCount = 0,
				CurrentWaypointIndex = 0,
				Target = moveTo.Target,
				LastPosition = pos.Value,
				StuckTime = 0f,
				TimeSinceLastReplan = 0f,
				IsJitterReplanned = false
			};
		}

		bool forceReplan = false;
		System.Numerics.Vector3 pathfindStart = pos.Value;
		System.Numerics.Vector3 pathfindEnd = moveTo.Target;

		if (hasPf)
		{
			float distMoved = System.Numerics.Vector3.Distance(pos.Value, pf.LastPosition);
			pf.LastPosition = pos.Value;

			float actualSpeed = _statService != null ? _statService.GetStatValue(entity, new Realm.Ecs.Common.StatId("MovementSpeed")) : 0f;
			if (actualSpeed <= 0) actualSpeed = stats.Speed; // Fallback if no stats component or if modified to 0

			float expectedDist = actualSpeed * _fDelta;
			if (distMoved < expectedDist * 0.1f)
			{
				pf.StuckTime += _fDelta;
				if (pf.StuckTime >= 0.1f)
				{
					pf.TimeSinceLastReplan += _fDelta;
					if (pf.TimeSinceLastReplan >= 1.5f)
					{
						pf.TimeSinceLastReplan = 0f;
						pf.IsJitterReplanned = false;
						forceReplan = true;
					}
				}
			}
			else
			{
				pf.StuckTime = 0f;
				pf.TimeSinceLastReplan = 0f;
				pf.IsJitterReplanned = false;
			}
		}
		else
		{
			pf.LastPosition = pos.Value;
			pf.StuckTime = 0f;
			pf.TimeSinceLastReplan = 0f;
			pf.IsJitterReplanned = false;
		}

		if (pf.Target != moveTo.Target || pf.WaypointCount == 0 || forceReplan)
		{
			if (_hasTerrainState)
			{
				if (((TerrainPathingFlags)pathingFlags & TerrainPathingFlags.Flying) != 0)
				{
					pf.WaypointCount = 1;
					pf.CurrentWaypointIndex = 0;
					pf.Waypoints[0] = moveTo.Target;
				}
				else
				{
					_pathfinder.ComputePath(_currentTerrainState.NavMeshQuery, pathfindStart, pathfindEnd, pathingFlags, ref pf);
				}
				pf.Target = moveTo.Target;
			}
		}
		var current = pos.Value;
		var target = moveTo.Target;
		if (pf.CurrentWaypointIndex < pf.WaypointCount)
		{
			target = pf.Waypoints[pf.CurrentWaypointIndex];
		}
		float diffX = current.X - target.X;
		float diffZ = current.Z - target.Z;
		float horizontalDist = MathF.Sqrt(diffX * diffX + diffZ * diffZ);
		float arrivalThreshold = Math.Max(0.5f, stats.Speed * _fDelta * 1.2f);
		// Arrival is measured horizontally only: vertical placement is handled by the
		// navmesh/terrain follow below, so requiring a Y match made units overshoot
		// waypoints sitting on raised territory and grind against walls/corners.
		if (horizontalDist < arrivalThreshold)
		{
			pf.CurrentWaypointIndex++;
			if (pf.CurrentWaypointIndex < pf.WaypointCount)
			{
				target = pf.Waypoints[pf.CurrentWaypointIndex];
			}
		}
		if (pf.CurrentWaypointIndex >= pf.WaypointCount)
		{
			_tickArrivedUnits.Add(entity);
			EcsWorld.SetOrAdd(entity, new Velocity(System.Numerics.Vector3.Zero));
		}
		else
		{
			float actualSpeed = _statService != null ? _statService.GetStatValue(entity, new Realm.Ecs.Common.StatId("MovementSpeed")) : 0f;
			if (actualSpeed <= 0) actualSpeed = stats.Speed;

			System.Numerics.Vector3 toTarget = target - current;
			if (isFlying)
			{
				toTarget.Y = 0f;
			}
			System.Numerics.Vector3 desiredVelocity;
			if (toTarget.LengthSquared() < 0.000001f)
			{
				desiredVelocity = System.Numerics.Vector3.Zero;
			}
			else
			{
				desiredVelocity = System.Numerics.Vector3.Normalize(toTarget) * actualSpeed;
			}
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
					if (_unitGrid.TryGetValue(key, out var list))
					{
						foreach (var other in list)
						{
							if (other == entity) continue;
							if (!EcsWorld.Has<Position>(other)) continue;

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
			}

			System.Numerics.Vector3 steering = desiredVelocity;
			if (neighborCount > 0)
			{
				cohesion = (cohesion / neighborCount) - current;
				if (cohesion.LengthSquared() > 0.001f) cohesion = System.Numerics.Vector3.Normalize(cohesion) * actualSpeed;

				alignment = alignment / neighborCount;
				if (alignment.LengthSquared() > 0.001f) alignment = System.Numerics.Vector3.Normalize(alignment) * actualSpeed;

				if (separation.LengthSquared() > 0.001f) separation = System.Numerics.Vector3.Normalize(separation) * actualSpeed;

				bool stuck = pf.StuckTime >= StuckEscalationTime;
				float desiredWeight = stuck ? 0.75f : 0.90f;
				float separationWeight = stuck ? 0.30f : 0.04f;
				steering = desiredVelocity * desiredWeight + separation * separationWeight + cohesion * 0.04f + alignment * 0.03f;
				if (steering.LengthSquared() > 0.001f)
				{
					steering = System.Numerics.Vector3.Normalize(steering) * actualSpeed;
				}
			}

			System.Numerics.Vector3 currentVelocity = EcsWorld.Has<Velocity>(entity)
				? EcsWorld.Get<Velocity>(entity).Value
				: System.Numerics.Vector3.Zero;
			float accel = stats.Acceleration;
			System.Numerics.Vector3 velocity = accel > 0f ? MoveTowards(currentVelocity, steering, accel * _fDelta) : steering;
			System.Numerics.Vector3 nextPos = current + velocity * _fDelta;

			float scale1 = EcsWorld.Has<CollisionScale>(entity) ? EcsWorld.Get<CollisionScale>(entity).Value : 1.0f;
			float r1 = EcsWorld.Has<CollisionRadius>(entity) 
				? EcsWorld.Get<CollisionRadius>(entity).Value * scale1 
				: scale1 * Realm.Ecs.Common.GameplayConstants.DefaultCollisionRadius;

				if (!isFlying)
			{
			int baseCx = (int)Math.Floor(nextPos.X / _collisionCellSize);
			int baseCz = (int)Math.Floor(nextPos.Z / _collisionCellSize);

			for (int dx = -1; dx <= 1; dx++)
			{
				for (int dz = -1; dz <= 1; dz++)
				{
					long key = ((long)(baseCx + dx) << 32) | (uint)(baseCz + dz);
					if (_unitGrid.TryGetValue(key, out var list))
					{
						foreach (var otherEntity in list)
						{
							if (otherEntity == entity) continue;

							float scale2 = EcsWorld.Has<CollisionScale>(otherEntity) ? EcsWorld.Get<CollisionScale>(otherEntity).Value : 1.0f;
							float r2 = EcsWorld.Has<CollisionRadius>(otherEntity) 
								? EcsWorld.Get<CollisionRadius>(otherEntity).Value * scale2 
								: scale2 * Realm.Ecs.Common.GameplayConstants.DefaultCollisionRadius;

							float minDist = (r1 + r2) * Realm.Ecs.Common.GameplayConstants.CollisionSeparationFactor;
							var otherPos = EcsWorld.Get<Position>(otherEntity).Value;
							float ox = nextPos.X - otherPos.X;
							float oz = nextPos.Z - otherPos.Z;
							float distSq = ox * ox + oz * oz;
							if (distSq < minDist * minDist)
							{
								float otherDist = (float)Math.Sqrt(distSq);
								System.Numerics.Vector3 pushDir;
								if (otherDist < 0.001f)
								{
									pushDir = new System.Numerics.Vector3(1f, 0f, 0f);
									otherDist = 1f;
								}
								else
								{
									pushDir = new System.Numerics.Vector3(ox / otherDist, 0f, oz / otherDist);
								}
								float overlap = minDist - otherDist;
								nextPos += pushDir * overlap;
							}
						}
					}
				}
			}

			for (int dx = -1; dx <= 1; dx++)
			{
				for (int dz = -1; dz <= 1; dz++)
				{
					long key = ((long)(baseCx + dx) << 32) | (uint)(baseCz + dz);
					if (_propGrid.TryGetValue(key, out var list))
					{
						foreach (var propEntity in list)
						{
							float scaleProp = EcsWorld.Has<CollisionScale>(propEntity) ? EcsWorld.Get<CollisionScale>(propEntity).Value : 1.0f;
							float r2 = EcsWorld.Has<CollisionRadius>(propEntity) 
								? EcsWorld.Get<CollisionRadius>(propEntity).Value * scaleProp 
								: scaleProp * Realm.Ecs.Common.GameplayConstants.DefaultPropCollisionRadius;

							float minDist = (r1 + r2) * Realm.Ecs.Common.GameplayConstants.CollisionSeparationFactor;
							var propPos = EcsWorld.Get<Position>(propEntity).Value;
							float ox = nextPos.X - propPos.X;
							float oz = nextPos.Z - propPos.Z;
							float distSq = ox * ox + oz * oz;
							if (distSq < minDist * minDist)
							{
								float otherDist = (float)Math.Sqrt(distSq);
								System.Numerics.Vector3 pushDir;
								if (otherDist < 0.001f)
								{
									pushDir = new System.Numerics.Vector3(1f, 0f, 0f);
									otherDist = 1f;
								}
								else
								{
									pushDir = new System.Numerics.Vector3(ox / otherDist, 0f, oz / otherDist);
								}
								float overlap = minDist - otherDist;
								nextPos += pushDir * overlap;
							}
						}
					}
				}
			}
			}

			bool snappedToNavMesh = false;
			float snappedSurfaceY = nextPos.Y;
			if (_hasTerrainState && _currentTerrainState.NavMeshQuery != null && !isFlying)
			{
				var snapPos = new RcVec3f(nextPos.X, nextPos.Y, nextPos.Z);
				_currentTerrainState.NavMeshQuery.FindNearestPoly(snapPos,
					NavMeshSnapExtents, _pathfinder.Filter,
					out long snapRef, out var snappedPt, out _);
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

			// When a ground unit is walking on the navmesh, its feet follow the walkable
			// polygon height rather than the raw terrain interpolation. At the lip of a
			// stepped hillside the terrain height ramps steeply even where Recast baked a
			// walkable poly, so following the ramp made units visibly climb/slide along
			// mountain faces. The navmesh (AgentMaxSlope = 30 degrees) already refuses
			// steeper slopes, so polygons are a safe surface to stand on.
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
				desiredY = pos.Value.Y;
			}
			float followFactor = Math.Clamp(VerticalFollowRate * _fDelta, 0f, 1f);
			nextPos.Y = pos.Value.Y + (desiredY - pos.Value.Y) * followFactor;
			pos.Value = nextPos;
			EcsWorld.SetOrAdd(entity, new Velocity(velocity));
		}

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
