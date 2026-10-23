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
		public float CollisionCellSize => _collisionCellSize;

		public bool TryGetUnitGridCell(int cx, int cz, out List<SpatialGridEntry>? list)
		{
			long key = ((long)cx << 32) | (uint)cz;
			return _unitGrid.TryGetValue(key, out list);
		}

		private const float StuckEscalationTime = 1.5f;

		private const float NavMeshMaxSnapClimb = 0.5f;
		private static readonly RcVec3f NavMeshSnapExtents = new RcVec3f(1.5f, 2.0f, 1.5f);
		private const float VerticalFollowRate = 30f;
		private const float DefaultAcceleration = 25f;
		private const float OffMeshStartRecoveryLogInterval = 2f;

		private float _elapsedTime;
		private int _offMeshStartRecoveryCount;
		private Entity _offMeshStartRecoveryWorldEntity;
		private readonly Dictionary<int, float> _offMeshStartRecoveryLogTimeByEntityId = new();
		private readonly List<int> _deadOffMeshStartRecoveryEntityIds = new();

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

		public void QueryPropsInRadius(Vector3 center, float radius, SpatialGridEntryCallback callback)
		{
			QueryGridInRadius(_propGrid, center, radius, callback);
		}

		private enum OffMeshReplanCause
		{
			None,
			NoPath,
			Retarget,
			Stuck
		}

		private struct MovementWorkItem
		{
			public Entity Entity;
			public Vector3 PosValue;
			public Vector3 MoveTarget;
			public MovementStats Stats;
			public ushort PathingFlags;
			public bool IsFlying;
			public bool HasPf;
			public PathFollow Pf;
			public float ActualSpeed;
			public NavMeshStartRecovery StartRecovery;
			public OffMeshReplanCause ReplanCause;

			public bool HasArrived;
			public Vector3 NextPosition;
			public Vector3 NextVelocity;
		}

		private MovementWorkItem[] _movementWorkItems = new MovementWorkItem[256];
		private int _movementWorkCount = 0;
		private readonly Action<int> _processMovementWorkItemDelegate;
		private readonly EntityCallback<Position, MoveTo, MovementStats> _collectMovementWorkItemDelegate;
		private readonly ThreadLocal<NavMeshPathfinder> _threadLocalPathfinder = new(() => new NavMeshPathfinder());
		private readonly ThreadLocal<(DtNavMesh? Mesh, DtNavMeshQuery? Query)> _threadLocalNavQuery = new();

		private readonly Dictionary<long, List<SpatialGridEntry>> _unitGrid = new();
		private readonly Dictionary<long, List<SpatialGridEntry>> _propGrid = new();
		private readonly List<List<SpatialGridEntry>> _listPool = new();

		private readonly List<Entity> _tickArrivedUnits = new();

		private readonly QueryDescription _movementQuery = QueryCache.AllPositionAndMoveToAndMovementStatsNoneDeadQuery;
		private readonly QueryDescription _unitSpatialQuery = QueryCache.AllPositionAndDefinitionIdNoneDeadQuery;
		private readonly QueryDescription _propSpatialQuery = QueryCache.AllPropIdentityAndPositionQuery;
		private EntityCallback<Position, MoveTo, MovementStats> _movementQueryDelegate;
		private EntityCallback<Position> _unitSpatialQueryDelegate;
		private EntityCallback<Position> _propSpatialQueryDelegate;
		private int _lastPropEntityCount = -1;
		private EcsWorld? _lastPropWorld;
		private bool _propGridDirty = true;

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
			_collectMovementWorkItemDelegate = CollectMovementWorkItemAction;
			_processMovementWorkItemDelegate = ProcessMovementWorkItemParallel;
			_unitSpatialQueryDelegate = UnitSpatialGridQueryAction;
			_propSpatialQueryDelegate = PropSpatialGridQueryAction;
		}

		public void InvalidatePropGrid()
		{
			_propGridDirty = true;
		}

		public void AdvanceElapsedTime(float delta)
		{
			_elapsedTime += delta;
		}

		public void StepMovement(float delta)
		{
			_fDelta = delta;
			_elapsedTime += delta;
			_tickArrivedUnits.Clear();

			RefreshTerrainState();

			RebuildSpatialGrid();

			_movementWorkCount = 0;
			GameContext.World.Query(in _movementQuery, _collectMovementWorkItemDelegate);

			if (_movementWorkCount > 0)
			{
				ParallelRunner.Execute(_movementWorkCount, _processMovementWorkItemDelegate);

				var world = GameContext.World;
				for (int i = 0; i < _movementWorkCount; i++)
				{
					ref var item = ref _movementWorkItems[i];
					if (!world.IsAlive(item.Entity))
						continue;

					SavePathFollow(item.HasPf, item.Entity, item.Pf);

					ReportOffMeshStartRecovery(item.Entity, item.StartRecovery, item.ReplanCause);

					if (item.HasArrived)
					{
						_tickArrivedUnits.Add(item.Entity);
						world.SetOrAdd(item.Entity, new Velocity(Vector3.Zero));
					}
					else
					{
						world.Set(item.Entity, new Position(item.NextPosition));
						world.SetOrAdd(item.Entity, new Velocity(item.NextVelocity));
					}
				}
			}

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
				Entity worldEntity = ActiveWorldEntity;
				TerrainState terrainState = GameContext.World.Get<TerrainState>(worldEntity);
				if (worldEntity != _offMeshStartRecoveryWorldEntity || !ReferenceEquals(terrainState.NavMesh, _currentTerrainState.NavMesh))
				{
					ResetOffMeshStartRecoveryDiagnostics();
					_offMeshStartRecoveryWorldEntity = worldEntity;
				}

				_currentTerrainState = terrainState;
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

			var world = GameContext.World;
			world.Query(in _unitSpatialQuery, _unitSpatialQueryDelegate);

			int currentPropCount = world.CountEntities(in _propSpatialQuery);
			if (_propGridDirty || !ReferenceEquals(_lastPropWorld, world) || currentPropCount != _lastPropEntityCount)
			{
				_propGridDirty = false;
				_lastPropWorld = world;
				_lastPropEntityCount = currentPropCount;

				foreach (var list in _propGrid.Values)
				{
					list.Clear();
					_listPool.Add(list);
				}

				_propGrid.Clear();

				world.Query(in _propSpatialQuery, _propSpatialQueryDelegate);
			}
		}

		private void UnitSpatialGridQueryAction(Entity entity, ref Position p)
		{
			var world = GameContext.World;
			Vector3 pos = p.Value;
			long key = GetCellKey(pos.X, pos.Z);

			if (!_unitGrid.TryGetValue(key, out var list))
			{
				list = GetEntityListFromPool();
				_unitGrid[key] = list;
			}

			float scale = world.Has<CollisionScale>(entity) ? world.Get<CollisionScale>(entity).Value : 1.0f;
			float authoredRadius = world.Has<CollisionRadius>(entity) ? world.Get<CollisionRadius>(entity).Value : 0f;
			float radius = GameplayConstants.ResolveUnitContactRadius(authoredRadius, scale);
			bool hasVel = world.Has<Velocity>(entity);
			Vector3 vel = hasVel ? world.Get<Velocity>(entity).Value : Vector3.Zero;

			list.Add(new SpatialGridEntry(entity, pos, vel, radius, hasVel));
		}

		private void PropSpatialGridQueryAction(Entity entity, ref Position p)
		{
			var world = GameContext.World;
			if (world.Has<Dead>(entity)) return;

			Vector3 pos = p.Value;
			long key = GetCellKey(pos.X, pos.Z);

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

		private bool TryGetMovementContext(Entity entity, Vector3 pos, Vector3 target, out ushort pathingFlags, out bool isFlying, out bool hasPf, out PathFollow pf)
		{
			if (TryHandleStun(entity))
			{
				pathingFlags = 0;
				isFlying = false;
				hasPf = false;
				pf = default;
				return false;
			}

			pathingFlags = (ushort)(GameContext.World.Has<PathingFlags>(entity) ? GameContext.World.Get<PathingFlags>(entity).Value : 8);
			isFlying = ((TerrainPathingFlags)pathingFlags & TerrainPathingFlags.Flying) != 0;
			hasPf = GameContext.World.Has<PathFollow>(entity);
			pf = GetOrCreatePathFollow(hasPf, entity, pos, target);

			return true;
		}

		private void CollectMovementWorkItemAction(Entity entity, ref Position pos, ref MoveTo moveTo, ref MovementStats stats)
		{
			if (GameContext.World.Has<CommandsFrozen>(entity)) return;

			if (!TryGetMovementContext(entity, pos.Value, moveTo.Target, out ushort pathingFlags, out bool isFlying, out bool hasPf, out PathFollow pf)) return;

			if (_movementWorkCount >= _movementWorkItems.Length)
			{
				Array.Resize(ref _movementWorkItems, _movementWorkItems.Length * 2);
			}

			_movementWorkItems[_movementWorkCount++] = new MovementWorkItem
			{
				Entity = entity,
				PosValue = pos.Value,
				MoveTarget = moveTo.Target,
				Stats = stats,
				PathingFlags = pathingFlags,
				IsFlying = isFlying,
				HasPf = hasPf,
				Pf = pf,
				ActualSpeed = GetActualSpeed(entity, stats)
			};
		}

		private DtNavMeshQuery? GetThreadLocalNavMeshQuery()
		{
			if (!_hasTerrainState || _currentTerrainState.NavMesh == null)
				return null;

			var targetMesh = _currentTerrainState.NavMesh;
			var local = _threadLocalNavQuery.Value;
			if (!ReferenceEquals(local.Mesh, targetMesh) || local.Query == null)
			{
				local = (targetMesh, new DtNavMeshQuery(targetMesh));
				_threadLocalNavQuery.Value = local;
			}
			return local.Query;
		}

		private void ProcessMovementWorkItemParallel(int index)
		{
			ref var item = ref _movementWorkItems[index];
			Entity entity = item.Entity;
			Vector3 posValue = item.PosValue;
			Vector3 moveTarget = item.MoveTarget;
			MovementStats stats = item.Stats;
			ushort pathingFlags = item.PathingFlags;
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
			Vector3 currentVelocity = GameContext.World.Has<Velocity>(entity) ? GameContext.World.Get<Velocity>(entity).Value : Vector3.Zero;

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
				var pathfinder = _threadLocalPathfinder.Value!;
				pathfinder.ComputePath(navQuery, start, target, pathingFlags, ref pf, out startRecovery);
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
				GameContext.NavMeshPathfinder.ComputePath(_currentTerrainState.NavMeshQuery, start, target, pathingFlags, ref pf, out startRecovery);
			}

			pf.Target = target;
		}

		private void ReportOffMeshStartRecovery(Entity entity, in NavMeshStartRecovery startRecovery, OffMeshReplanCause replanCause)
		{
			if (!startRecovery.UsedFallback) return;

			PurgeDeadOffMeshStartRecoveryEntries();

			int entityId = entity.Id;
			if (_offMeshStartRecoveryLogTimeByEntityId.TryGetValue(entityId, out float lastLogTime) && _elapsedTime - lastLogTime < OffMeshStartRecoveryLogInterval) return;

			_offMeshStartRecoveryLogTimeByEntityId[entityId] = _elapsedTime;
			_offMeshStartRecoveryCount++;

			var world = GameContext.World;
			string entityName = world != null && world.Has<Name>(entity) ? world.Get<Name>(entity).Value : "-";
			string definitionId = world != null && world.Has<DefinitionId>(entity) ? world.Get<DefinitionId>(entity).Value : "-";
			string snappedText = startRecovery.Found
				? $"({startRecovery.Snapped.X:0.00},{startRecovery.Snapped.Y:0.00},{startRecovery.Snapped.Z:0.00})"
				: "na";
			string lateralText = startRecovery.Found ? startRecovery.LateralDistance.ToString("0.00") : "na";
			string verticalText = startRecovery.Found ? startRecovery.VerticalDelta.ToString("0.00") : "na";

			Godot.GD.PushWarning(
				$"[NavMesh.StartRecovery] incident=#{_offMeshStartRecoveryCount} entity={entityId} name={entityName} def={definitionId} cause={GetOffMeshReplanCauseLabel(replanCause)} found={startRecovery.Found} startRef={startRecovery.StartRef} requested=({startRecovery.Requested.X:0.00},{startRecovery.Requested.Y:0.00},{startRecovery.Requested.Z:0.00}) snapped={snappedText} lateral={lateralText} dy={verticalText}");
		}

		private void ResetOffMeshStartRecoveryDiagnostics()
		{
			_offMeshStartRecoveryLogTimeByEntityId.Clear();
			_deadOffMeshStartRecoveryEntityIds.Clear();
			_elapsedTime = 0f;
			_offMeshStartRecoveryCount = 0;
		}

		private static string GetOffMeshReplanCauseLabel(OffMeshReplanCause replanCause)
		{
			return replanCause switch
			{
				OffMeshReplanCause.NoPath => "nopath",
				OffMeshReplanCause.Retarget => "retarget",
				OffMeshReplanCause.Stuck => "stuck",
				_ => "none"
			};
		}

		private void PurgeDeadOffMeshStartRecoveryEntries()
		{
			if (_offMeshStartRecoveryLogTimeByEntityId.Count == 0) return;

			var world = GameContext.World;
			if (world == null) return;

			_deadOffMeshStartRecoveryEntityIds.Clear();
			foreach (int entityId in _offMeshStartRecoveryLogTimeByEntityId.Keys)
			{
				if (!world.IsAlive(new Entity(entityId)))
				{
					_deadOffMeshStartRecoveryEntityIds.Add(entityId);
				}
			}

			foreach (int entityId in _deadOffMeshStartRecoveryEntityIds)
			{
				_offMeshStartRecoveryLogTimeByEntityId.Remove(entityId);
			}
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
			float authoredRadius = GameContext.World.Has<CollisionRadius>(entity) ? GameContext.World.Get<CollisionRadius>(entity).Value : 0f;
			float r1 = GameplayConstants.ResolveUnitContactRadius(authoredRadius, scale1);

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
			ApplyVerticalFollowParallel(currentPos, isFlying, ref nextPos, ref velocity);
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
