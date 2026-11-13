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
		private readonly SimulationContext _context;
		private Entity _resolvedWorldEntity = Entity.Null;

		private Entity ActiveWorldEntity
		{
			get
			{
				if (_resolvedWorldEntity == Entity.Null || !_context.World.IsAlive(_resolvedWorldEntity))
				{
					if (_context.WorldEntity != Entity.Null && _context.World.IsAlive(_context.WorldEntity))
					{
						_resolvedWorldEntity = _context.WorldEntity;
					}
					else
					{
						var worldQuery = QueryCache.AllTerrainStateQuery;
						_context.World.Query(in worldQuery, entity => _resolvedWorldEntity = entity);
					}
				}

				return _resolvedWorldEntity;
			}
		}

		private float _fDelta;
		private readonly float _collisionCellSize = GameplayConstants.PathfindingGridSize;
		public float CollisionCellSize => _collisionCellSize;

		public bool TryGetUnitGridCell(int cx, int cz, out List<SpatialGridEntry>? list)
		{
			long key = ((long)cx << 32) | (uint)cz;
			return _unitGrid.TryGetValue(key, out list);
		}

		private const float StuckEscalationTime = 1.5f;
		private const float HeadOnLateralLeanThreshold = 0.02f;
		private const float HeadOnLateralLean = 0.35f;

		private const float NavMeshMaxSnapClimb = 0.5f;
		private static readonly RcVec3f NavMeshSnapExtents = new RcVec3f(1.5f, 2.0f, 1.5f);
		private const float VerticalFollowRate = 30f;
		private const float OffMeshStartRecoveryLogInterval = 2f;

		private float _elapsedTime;
		private int _offMeshStartRecoveryCount;
		private Entity _offMeshStartRecoveryWorldEntity;
		private readonly Dictionary<int, float> _offMeshStartRecoveryLogTimeByEntityId = new();
		private readonly List<int> _deadOffMeshStartRecoveryEntityIds = new();

		/// <summary>
		///     One body held in the collision grid: where it is, how fast it is moving and how much room it takes, so a radius
		///     query answers from the grid without touching components.
		/// </summary>
		public readonly struct SpatialGridEntry
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

		public delegate void SpatialGridEntryCallback(in SpatialGridEntry entry);

		private void QueryGridInRadius(Dictionary<long, List<SpatialGridEntry>> grid, Vector3 center, float radius, SpatialGridEntryCallback callback)
		{
			float radiusSq = radius * radius;
			int minCx = (int)Math.Floor((center.X - radius) / _collisionCellSize);
			int maxCx = (int)Math.Floor((center.X + radius) / _collisionCellSize);
			int minCz = (int)Math.Floor((center.Z - radius) / _collisionCellSize);
			int maxCz = (int)Math.Floor((center.Z + radius) / _collisionCellSize);

			for (int cx = minCx; cx <= maxCx; cx++)
			{
				for (int cz = minCz; cz <= maxCz; cz++)
				{
					long key = ((long)cx << 32) | (uint)cz;
					if (grid.TryGetValue(key, out var list))
					{
						for (int i = 0; i < list.Count; i++)
						{
							var item = list[i];
							if ((item.Position - center).LengthSquared() <= radiusSq)
							{
								callback(in item);
							}
						}
					}
				}
			}
		}

		public void QueryUnitsInRadius(Vector3 center, float radius, SpatialGridEntryCallback callback)
		{
			QueryGridInRadius(_unitGrid, center, radius, callback);
		}

		/// <summary>
		///     Visits every static obstacle body - a prop or a building - whose cell lies within the radius.
		/// </summary>
		public void QueryObstaclesInRadius(Vector3 center, float radius, SpatialGridEntryCallback callback)
		{
			QueryGridInRadius(_obstacleGrid, center, radius, callback);
		}

		private enum OffMeshReplanCause
		{
			None,
			NoPath,
			Retarget,
			Stuck
		}

		/// <summary>
		///     One unit's movement state copied out for a parallel pass, carrying everything the mover needs so no worker
		///     reads the world while another lane writes it.
		/// </summary>
		private struct MovementWorkItem
		{
			public Entity Entity;
			public Vector3 PosValue;
			public Vector3 MoveTarget;
			public MovementStats Stats;
			public ushort TraversableTerrain;
			public bool IsFlying;
			public bool HasPf;
			public PathFollow Pf;
			public float ActualSpeed;
			public NavMeshStartRecovery StartRecovery;
			public OffMeshReplanCause ReplanCause;

			public bool HasArrived;
			/// <summary>
			/// Set for a unit the client plans but never moves. Its route is produced so the path and the facing it draws
			/// agree with the corridor the authority uses, while its position stays the one the presentation step wrote.
			/// </summary>
			public bool IsPlanOnly;
			public Vector3 NextPosition;
			public Vector3 NextVelocity;
		}

		private MovementWorkItem[] _movementWorkItems = new MovementWorkItem[256];
		private int _movementWorkCount = 0;
		private readonly Action<int> _processMovementWorkItemDelegate;
		private readonly EntityCallback<Position, MoveTo, MovementStats> _collectMovementWorkItemDelegate;
		private readonly EntityCallback<Position, PreviewMoveTo, MovementStats> _collectPreviewMovementWorkItemDelegate;
		private readonly ThreadLocal<NavMeshPathfinder> _threadLocalPathfinder = new(() => new NavMeshPathfinder());
		private readonly ThreadLocal<(DtNavMesh? Mesh, DtNavMeshQuery? Query)> _threadLocalNavQuery = new();

		private readonly Dictionary<long, List<SpatialGridEntry>> _unitGrid = new();
		private readonly Dictionary<long, List<SpatialGridEntry>> _obstacleGrid = new();
		private readonly List<List<SpatialGridEntry>> _listPool = new();

		private readonly List<Entity> _tickArrivedUnits = new();

		private readonly QueryDescription _movementQuery = QueryCache.AllPositionAndMoveToAndMovementStatsNoneDeadQuery;
		private readonly QueryDescription _previewMovementQuery = QueryCache.AllPositionAndPreviewMoveToAndMovementStatsNoneDeadQuery;
		private readonly QueryDescription _unitSpatialQuery = QueryCache.AllPositionAndDefinitionIdNoneDeadQuery;
		private readonly QueryDescription _propObstacleQuery = QueryCache.AllPropIdentityAndPositionNoneDeadQuery;
		private readonly QueryDescription _buildingObstacleQuery = QueryCache.AllBuildingAndPositionNoneDeadQuery;
		private EntityCallback<Position, MoveTo, MovementStats> _movementQueryDelegate;
		private EntityCallback<Position> _unitSpatialQueryDelegate;
		private EntityCallback<Position> _propObstacleQueryDelegate;
		private EntityCallback<Position> _buildingObstacleQueryDelegate;
		private int _lastObstacleCount = -1;
		private EcsWorld? _lastObstacleWorld;
		private bool _planOnlyLocallyOwnedMovement;
		private bool _obstacleGridDirty = true;

		private TerrainState _currentTerrainState;
		private bool _hasTerrainState;
		private Func<Vector3, float>? _editorHeightProvider;

		public Func<Vector3, float>? EditorHeightProvider
		{
			get => _editorHeightProvider;
			set => _editorHeightProvider = value;
		}

		/// <summary>
		///     Decides whether an entity takes part in movement planning. A client narrows this to the units this player
		///     owns, whose routes it draws; an authoritative context leaves it unset and plans every entity in the world.
		/// </summary>
		public Func<Entity, bool>? SimulateEntityFilter { get; set; }

		/// <summary>Where the start-recovery diagnostics go. A headless process has no Godot logger, so it stays silent.</summary>
		public Action<string>? WarningSink { get; set; }

		/// <summary>Raised for an entity whose position this step wrote, so a client can attribute the write in its diagnostics.</summary>
		public Action<Entity>? PositionWrittenSink { get; set; }

		public MovementAndPathfindingService(SimulationContext context)
		{
			_context = context;
			_movementQueryDelegate = MovementQueryAction;
			_collectMovementWorkItemDelegate = CollectMovementWorkItemAction;
			_collectPreviewMovementWorkItemDelegate = CollectPreviewMovementWorkItemAction;
			_processMovementWorkItemDelegate = ProcessMovementWorkItemParallel;
			_unitSpatialQueryDelegate = UnitSpatialGridQueryAction;
			_propObstacleQueryDelegate = PropObstacleQueryAction;
			_buildingObstacleQueryDelegate = BuildingObstacleQueryAction;
		}

		private bool ShouldSimulateEntity(Entity entity) => SimulateEntityFilter == null || SimulateEntityFilter(entity);

		/// <summary>
		///     Asks for the static obstacle grid to be built again on the next step. A prop or a building that appeared,
		///     went away or was moved without the grid noticing is picked up by the next step, which is how a building
		///     spawned or destroyed mid-match starts and stops blocking movement.
		/// </summary>
		public void InvalidateObstacleGrid()
		{
			_obstacleGridDirty = true;
		}

		public void AdvanceElapsedTime(float delta)
		{
			_elapsedTime += delta;
		}

		public void StepMovement(float delta)
		{
			StepMovement(delta, false);
		}

		/// <summary>
		/// Steps movement for the given rate. With <paramref name="planOnlyLocallyOwned"/> set - which is how the client's
		/// presentation tick calls it - only units this player owns are planned. Their route is produced so the path and
		/// facing they are drawn with agree with the corridor the authority uses, and no position is written: every
		/// position arrives in a description and is placed by the presentation step, and a second writer here pinned such
		/// a unit at the position it spawned with because the client cannot advance the authority's own route.
		/// </summary>
		public void StepMovement(float delta, bool planOnlyLocallyOwned)
		{
			_fDelta = delta;
			_elapsedTime += delta;
			_tickArrivedUnits.Clear();
			_planOnlyLocallyOwnedMovement = planOnlyLocallyOwned;

			RefreshTerrainState();

			RebuildSpatialGrid();

			_movementWorkCount = 0;
			_context.World.Query(in _movementQuery, _collectMovementWorkItemDelegate);
			_context.World.Query(in _previewMovementQuery, _collectPreviewMovementWorkItemDelegate);

			if (_movementWorkCount > 0)
			{
				ParallelRunner.Execute(_movementWorkCount, _processMovementWorkItemDelegate, _context.World);

				var world = _context.World;
				for (int i = 0; i < _movementWorkCount; i++)
				{
					ref var item = ref _movementWorkItems[i];
					if (!world.IsAlive(item.Entity))
						continue;

					SavePathFollow(item.HasPf, item.Entity, item.Pf);

					if (item.IsPlanOnly) continue;

					ReportOffMeshStartRecovery(item.Entity, item.StartRecovery, item.ReplanCause);

					if (item.HasArrived)
					{
						_tickArrivedUnits.Add(item.Entity);
						world.SetOrAdd(item.Entity, new Velocity(Vector3.Zero));
					}
					else
					{
						PositionWrittenSink?.Invoke(item.Entity);
						world.Set(item.Entity, new Position(item.NextPosition));
						world.SetOrAdd(item.Entity, new Velocity(item.NextVelocity));
					}
				}
			}

			foreach (var entity in _tickArrivedUnits)
			{
				if (_context.World.IsAlive(entity) && _context.World.Has<MoveTo>(entity))
				{
					if (_context.World.Has<PathFollow>(entity))
					{
						_context.World.Remove<PathFollow>(entity);
					}

					if (_context.World.Has<WaypointQueue>(entity))
					{
						var q = _context.World.Get<WaypointQueue>(entity);
						if (q.Count > 0)
						{
							var nextWaypoint = q.Dequeue();
							_context.World.Set(entity, q);
							_context.World.Set(entity, new MoveTo(nextWaypoint));
							continue;
						}
						else
						{
							_context.World.Remove<WaypointQueue>(entity);
						}
					}

					if (_context.World.Has<Patrol>(entity))
					{
						var patrol = _context.World.Get<Patrol>(entity);
						patrol.GoingToB = !patrol.GoingToB;
						_context.World.Set(entity, patrol);
						_context.World.Set(entity, new MoveTo(patrol.GoingToB ? patrol.PointB : patrol.PointA));
						continue;
					}

					_context.World.Remove<MoveTo>(entity);
				}
			}
		}

		public void RefreshTerrainState()
		{
			_hasTerrainState = _context.World.IsAlive(ActiveWorldEntity) && _context.World.Has<TerrainState>(ActiveWorldEntity);
			if (_hasTerrainState)
			{
				Entity worldEntity = ActiveWorldEntity;
				TerrainState terrainState = _context.World.Get<TerrainState>(worldEntity);
				if (worldEntity != _offMeshStartRecoveryWorldEntity || !ReferenceEquals(terrainState.NavMesh, _currentTerrainState.NavMesh))
				{
					ResetOffMeshStartRecoveryDiagnostics();
					_offMeshStartRecoveryWorldEntity = worldEntity;
				}

				_currentTerrainState = terrainState;
			}
		}

		public EntityCallback<Position, MoveTo, MovementStats> EditorMovementQueryDelegate => _movementQueryDelegate;

		private void MovementQueryAction(Entity entity, ref Position pos, ref MoveTo moveTo, ref MovementStats stats)
		{
			if (!TryGetMovementContext(entity, pos.Value, moveTo.Target, out ushort pathingFlags, out bool isFlying, out bool hasPf, out PathFollow pf)) return;

			bool forceReplan = UpdateStuckState(hasPf, entity, pos.Value, stats, ref pf);

			NavMeshStartRecovery startRecovery;
			OffMeshReplanCause replanCause;
			UpdatePathIfNeeded(pathingFlags, isFlying, pos.Value, moveTo.Target, forceReplan, ref pf, out startRecovery, out replanCause);
			ReportOffMeshStartRecovery(entity, startRecovery, replanCause);

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
			if (!_context.World.Has<Buffs>(entity) || !_context.World.Get<Buffs>(entity).TryGetValue("stun", out _))
				return false;

			if (_context.World.Has<Velocity>(entity))
			{
				_context.World.Set(entity, new Velocity(Vector3.Zero));
			}

			return true;
		}

		private PathFollow GetOrCreatePathFollow(bool hasPf, Entity entity, Vector3 posValue, Vector3 target)
		{
			if (hasPf) return _context.World.Get<PathFollow>(entity);

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
			float actualSpeed = GetActualSpeed(entity, stats);
			return UpdateStuckStateParallel(hasPf, posValue, actualSpeed, ref pf);
		}

		private void UpdatePathIfNeeded(ushort pathingFlags, bool isFlying, Vector3 start, Vector3 target, bool forceReplan, ref PathFollow pf, out NavMeshStartRecovery startRecovery, out OffMeshReplanCause replanCause)
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
				var navQuery = _currentTerrainState.NavMeshQuery;
				if (navQuery != null)
				{
					_context.NavMeshPathfinder.ComputePath(navQuery, start, target, pathingFlags, ref pf, out startRecovery);
				}
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
				_context.World.SetOrAdd(entity, new Velocity(Vector3.Zero));
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

			float actualSpeed = _context.StatService != null ? _context.StatService.GetStatValue(entity, MovementSpeedStatId) : 0f;
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
			Vector3 currentVelocity = _context.World.Has<Velocity>(entity) ? _context.World.Get<Velocity>(entity).Value : Vector3.Zero;

			Vector3 velocity = stats.Acceleration > 0f ? MoveTowards(currentVelocity, steering, stats.Acceleration * _fDelta) : steering;
			Vector3 nextPos = current + velocity * _fDelta;

			if (!isFlying)
			{
				ApplyCollisionAvoidance(entity, current, ref nextPos);
			}

			ApplyVerticalFollow(pos.Value, isFlying, ref nextPos, ref velocity);

			pos.Value = nextPos;
			_context.World.SetOrAdd(entity, new Velocity(velocity));
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

		private void ApplyVerticalFollow(Vector3 currentPos, bool isFlying, ref Vector3 nextPos, ref Vector3 velocity)
		{
			ApplyVerticalFollowParallel(currentPos, isFlying, ref nextPos, ref velocity);
		}

		private void SavePathFollow(bool hasPf, Entity entity, PathFollow pf)
		{
			if (hasPf)
			{
				_context.World.Set(entity, pf);
			}
			else
			{
				_context.World.Add(entity, pf);
			}
		}
	}
}
