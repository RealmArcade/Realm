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
	public class CombatAndDamageService
	{
		private readonly SimulationContext _context;

		/// <summary>Answers whether the match is running with cooldowns switched off, which a cheat sets and a server does not.</summary>
		public Func<bool>? UnlimitedPowerProvider { get; set; }

		// The map's tuning replaces this on load; reading it through the context means an attack and
		// an ability in the same match can never resolve through two different matrices.
		public CombatConfig Config => _context.ActiveCombatConfig;

		private const float UnderAttackAlertCooldown = 8f;

		// Vertical terrain delta counts toward range so units at the foot of a cliff are not
		// "in range" of a tower on the summit (combat is 3D, not a top-down projection).
		private static float Distance(Vector3 a, Vector3 b)
		{
			return (a - b).Length();
		}

		private bool IsFlying(Entity entity)
		{
			return _context.World.Has<PathingFlags>(entity)
			       && ((TerrainPathingFlags)_context.World.Get<PathingFlags>(entity).Value & TerrainPathingFlags.Flying) != 0;
		}

		private bool CanAttackTarget(Entity attacker, Entity target)
		{
			if (!IsFlying(target))
			{
				return true;
			}

			if (!_context.World.Has<CombatTargeting>(attacker))
			{
				return true;
			}

			return _context.World.Get<CombatTargeting>(attacker).CanTargetAir;
		}

		public readonly struct TargetCandidateSnapshot
		{
			public readonly Entity Entity;
			public readonly Vector3 Position;
			public readonly int PlayerIndex;
			public readonly bool IsFlying;
			public readonly float TargetCollisionRadius;
			public readonly float UnitContactRadius;

			public TargetCandidateSnapshot(Entity entity, Vector3 position, int playerIndex, bool isFlying, float targetCollisionRadius, float unitContactRadius)
			{
				Entity = entity;
				Position = position;
				PlayerIndex = playerIndex;
				IsFlying = isFlying;
				TargetCollisionRadius = targetCollisionRadius;
				UnitContactRadius = unitContactRadius;
			}
		}

		public struct AttackerSnapshot
		{
			public Entity Entity;
			public Vector3 Position;
			public float AttackRange;
			public int PlayerIndex;
			public bool IsFlying;
			public bool CanTargetAir;
			public bool IsGroundMelee;
			public float ScanMaxDistSq;
			public float TargetCollisionRadius;
			public float UnitContactRadius;
			public int IncludeFlags;
			public Entity SelectedTarget;
		}

		private struct LocalScanCandidate
		{
			public Entity Enemy;
			public Vector3 Position;
			public float TargetCollisionRadius;
			public float UnitContactRadius;
			public bool IsFlying;
			public float DistSq;
		}

		private TargetCandidateSnapshot[] _targetCandidateArray = new TargetCandidateSnapshot[512];
		private int _targetCandidateCount = 0;
		private AttackerSnapshot[] _attackerArray = new AttackerSnapshot[256];
		private int _attackerCount = 0;

		private DtNavMesh? _currentNavMesh;
		private DtNavMeshQuery? _currentNavMeshQuery;
		private readonly ThreadLocal<NavMeshPathfinder> _threadLocalPathfinder = new(() => new NavMeshPathfinder());
		private readonly ThreadLocal<(DtNavMesh? Mesh, DtNavMeshQuery? Query)> _threadLocalNavQuery = new();

		private readonly List<(Entity Attacker, AttackTarget Target)> _tickNewAttackTargets = new();
		private readonly List<(Entity Target, Entity Attacker)> _tickActionsToAddLastAttacker = new();
		private readonly List<Entity> _tickActionsToRemoveTarget = new();
		private readonly List<(Entity Attacker, Vector3 TargetPos)> _tickActionsToChase = new();
		private readonly List<Entity> _tickActionsToStopChasing = new();
		private readonly List<Entity> _tickUnitsToKill = new();
		private readonly List<Entity> _clearedReferenceEntities = new();
		private readonly QueryDescription _attackTargetReferenceQuery = new QueryDescription().WithAll<AttackTarget>();
		private readonly QueryDescription _followReferenceQuery = new QueryDescription().WithAll<Follow>();
		private readonly QueryDescription _lastAttackerReferenceQuery = new QueryDescription().WithAll<LastAttacker>();
		private readonly EntityCallback<AttackTarget> _attackTargetReferenceDelegate;
		private readonly EntityCallback<Follow> _followReferenceDelegate;
		private readonly EntityCallback<LastAttacker> _lastAttackerReferenceDelegate;
		private Entity _clearedReferenceTarget;
		private readonly List<Entity> _tickHealRemoveTargets = new();
		private readonly List<(Entity Healer, Vector3 TargetPos)> _tickHealChaseTargets = new();
		private readonly List<Entity> _tickHealStopChasing = new();

		private float _combatDelta = 0f;
		private float _combatTotalTime = 0f;
		private const float ChaseProgressEpsilon = 0.1f;
		private const float AbandonedTargetCooldownSeconds = 5.0f;

		// An attack with range at or below this threshold is treated as melee: it needs to
		// close the contact distance the movement separation enforces, and melee vertical
		// reach caps how high above the attacker (or below) a target must be to be hittable.
		private const float MeleeRangeThreshold = 3.0f;
		private const float DefaultMeleeVerticalReach = 1.5f;
		private const float MeleeContactReachSafetyMargin = 1.05f;
		private const float VerticalReachTolerance = 1.0f;

		// Chase destinations are only rewritten when the target moved more than this, so the
		// whole navmesh path is not re-planned on every combat frame (which made chasers
		// jitter and wander off the flat path).
		private const float ChaseRetargetDistance = 1.0f;
		private readonly Dictionary<Entity, float> _chaseStuckTime = new();
		private readonly Dictionary<Entity, float> _lastChaseDist = new();
		private readonly ConcurrentDictionary<Entity, (Entity Target, float Remaining)> _abandonedTargetCooldown = new();

		// Route reachability is cached per (attacker, target) pair so the real navmesh route is
		// NOT recomputed every combat tick. The cache is invalidated when geometry changes: the
		// attacker moved a meaningful amount toward/away from the target, the target moved, or
		// the navmesh was rebuilt. Reachable verdicts expire after RouteCacheMaxAge so chasers
		// re-evaluate as the battlefield shifts. Unreachable verdicts use a longer but still
		// bounded lifetime: a wrong negative would otherwise stick forever, because an attacker
		// that cannot reach its target also never moves far enough to invalidate the entry.
		private const float RouteCacheAttackerMoveLimit = 8.0f;
		private const float RouteCacheTargetMoveLimit = 4.0f;
		private const float RouteReachMargin = 1.5f;
		private const float RouteCacheMaxAge = 1.5f;
		private const float RouteCacheNegativeMaxAge = 6.0f;
		private readonly ConcurrentDictionary<(Entity Attacker, Entity Target), RouteReachabilityEntry> _routeReachabilityCache = new();

		// Acquisition considers the few closest enemies, not just the single closest one.
		// The closest candidate can be unreachable (a flyer at altitude, a unit on a plateau) and
		// must not block the unit from engaging the next-reachable enemy standing behind it.
		private const int MaxAcquisitionCandidates = 4;

		private readonly QueryDescription _enemyQuery = QueryCache.AllPositionAndOwnerNoneDeadQuery;
		private readonly QueryDescription _targetAcquisitionQuery = QueryCache.AllPositionAndAttackAndOwnerNoneAttackTargetAndDeadQuery;
		private readonly QueryDescription _combatQuery = QueryCache.AllPositionAndAttackAndAttackTargetAndOwnerNoneDeadQuery;
		private readonly QueryDescription _healingExecutionQuery = QueryCache.AllPositionAndAttackAndHealingTargetAndOwnerNoneDeadQuery;

		private EntityCallback<Position, Attack, AttackTarget, Owner> _combatQueryDelegate;
		private EntityCallback<Position, Attack, HealingTarget, Owner> _healingExecutionQueryDelegate;

		private readonly List<Entity> _tickExpiredAbandonedCooldowns = new();
		private readonly List<(Entity Attacker, Entity Target)> _staleRouteReachabilityEntries = new();
		private readonly List<Entity> _aoeKillList = new();
		private readonly List<(Entity Target, Entity Attacker)> _aoeLastAttackerList = new();

		private int _combatTickCount = 0;
		private MovementAndPathfindingService? _currentMovementService;

		private Entity _splashAttacker;
		private Entity _splashPrimaryTarget;
		private Vector3 _splashImpactPos;
		private float _splashBaseDamage;
		private Attack _splashAtk;
		private Owner _splashAttackerOwner;
		private int _splashAttackerPlayer;
		private readonly MovementAndPathfindingService.SpatialGridEntryCallback _splashCandidateCallback;

		private Vector3 _aoePosition;
		private float _aoeRadius;
		private float _aoeDamage;
		private Entity _aoeCasterEntity;
		private bool _aoeEnemyOnly;
		private int _aoeCasterPlayer;
		private readonly MovementAndPathfindingService.SpatialGridEntryCallback _aoeDamageCandidateCallback;

		private Vector3 _healPosition;
		private float _healRadius;
		private float _healAmount;
		private Entity _healCasterEntity;
		private int _healCasterPlayer;
		private readonly MovementAndPathfindingService.SpatialGridEntryCallback _healCandidateCallback;

		private readonly Action<int> _evaluateAttackerSpatialDelegate;
		private readonly Action<int> _evaluateAttackerFallbackDelegate;

		public Action<Vector3, Vector3>? OnArrowProjectileRequested;
		public Action<Vector3, Vector3, string?, Entity, int>? OnWeaponProjectileRequested;
		public Func<string, string[]?>? UnitWeaponsProvider;
		public Action<Entity>? OnDamageFlashRequested;
		public Action<Vector3, Vector3>? OnHealEffectRequested;
		public Action<Entity>? OnHealFlashRequested;
		public Action<Entity, Entity, float>? OnUnitDamagedCallback;
		public Action<Entity, Entity, float>? OnUnitHealedCallback;
		public Action<Entity, Entity>? OnUnitAttackedCallback;
		public Action<int, string>? OnUnderAttackAlertRequested;
		public Action<Entity>? OnKillUnitRequested;

		public CombatAndDamageService(SimulationContext context)
		{
			_context = context;
			_combatQueryDelegate = CombatQueryAction;
			_healingExecutionQueryDelegate = HealingExecutionQueryAction;

			_splashCandidateCallback = OnSplashCandidateFound;
			_aoeDamageCandidateCallback = OnAoeDamageCandidateFound;
			_healCandidateCallback = OnAoeHealCandidateFound;
			_evaluateAttackerSpatialDelegate = EvaluateAttackerSpatialAction;
			_evaluateAttackerFallbackDelegate = EvaluateAttackerFallbackAction;
			_attackTargetReferenceDelegate = CollectAttackTargetOfClearedUnit;
			_followReferenceDelegate = CollectFollowOfClearedUnit;
			_lastAttackerReferenceDelegate = CollectLastAttackerOfClearedUnit;
		}

		/// <summary>
		///     Decides whether this service is the one that applies the damage it computed. A client predicts its own
		///     units and takes the authority's health, so it leaves this unset only when it is the authority; the
		///     authoritative process answers yes and owns the outcome.
		/// </summary>
		public Func<bool>? AuthoritativeProvider { get; set; }

		/// <summary>
		///     Decides whether an entity takes part in combat resolution. A client narrows this to the units it predicts;
		///     an authoritative context leaves it unset and resolves every entity in the world.
		/// </summary>
		public Func<Entity, bool>? SimulateEntityFilter { get; set; }

		/// <summary>Answers the weapons an inventory item grants, so an item that arms a unit is armed on screen as well.</summary>
		public Func<string, string[]?>? ItemGrantedWeaponsProvider { get; set; }

		/// <summary>
		///     Answers whether two players are enemies when the world carries no relationship matrix, which is what a
		///     lobby that has not started yet looks like. A match that has a matrix never asks.
		/// </summary>
		public Func<int, int, bool>? EnemyPredicate { get; set; }

		private bool ArePlayerIndicesEnemies(int playerIndex, int otherPlayerIndex)
		{
			if (playerIndex == otherPlayerIndex) return false;

			Entity worldEntity = _context.WorldEntity;
			if (worldEntity != Entity.Null && _context.World.IsAlive(worldEntity) && _context.World.Has<PlayerRelationshipMatrix>(worldEntity))
			{
				return _context.World.Get<PlayerRelationshipMatrix>(worldEntity).AreEnemies(playerIndex, otherPlayerIndex);
			}

			return EnemyPredicate?.Invoke(playerIndex, otherPlayerIndex) ?? playerIndex != otherPlayerIndex;
		}

		private bool HasAuthority => AuthoritativeProvider?.Invoke() ?? true;

		private bool ShouldSimulateEntity(Entity entity) => SimulateEntityFilter == null || SimulateEntityFilter(entity);

		public void StepCombat(float delta, MovementAndPathfindingService? movementService = null)
		{
			_currentMovementService = movementService;
			_combatTickCount++;
			_combatDelta = delta;
			_combatTotalTime += delta;
			TickCombatAlertTimer(delta);
			TickAbandonedTargetCooldowns(delta);

			if (_combatTickCount % 30 == 0)
			{
				PruneRouteReachabilityCache();
			}

			if (!TryGetNavMeshQuery(out _currentNavMeshQuery, out _currentNavMesh))
			{
				_currentNavMeshQuery = null;
				_currentNavMesh = null;
			}

			ProcessTargetAcquisition();
			ProcessCombatTicks();
			ProcessHealingTicks();
		}

		private void PruneRouteReachabilityCache()
		{
			if (_routeReachabilityCache.IsEmpty) return;
			_staleRouteReachabilityEntries.Clear();
			foreach (var kvp in _routeReachabilityCache)
			{
				if (!_context.World.IsAlive(kvp.Key.Attacker) || !_context.World.IsAlive(kvp.Key.Target))
				{
					_staleRouteReachabilityEntries.Add(kvp.Key);
					continue;
				}

				float maxAge = kvp.Value.Reachable ? RouteCacheMaxAge : RouteCacheNegativeMaxAge;
				if (_combatTotalTime - kvp.Value.Timestamp > maxAge)
				{
					_staleRouteReachabilityEntries.Add(kvp.Key);
				}
			}

			for (int i = 0; i < _staleRouteReachabilityEntries.Count; i++)
			{
				_routeReachabilityCache.TryRemove(_staleRouteReachabilityEntries[i], out _);
			}
		}

		private void TickAbandonedTargetCooldowns(float delta)
		{
			if (_abandonedTargetCooldown.IsEmpty) return;
			_tickExpiredAbandonedCooldowns.Clear();
			foreach (var kvp in _abandonedTargetCooldown)
			{
				var entry = kvp.Value;
				entry.Remaining -= delta;
				if (entry.Remaining <= 0f)
				{
					_tickExpiredAbandonedCooldowns.Add(kvp.Key);
				}
				else
				{
					_abandonedTargetCooldown[kvp.Key] = entry;
				}
			}

			for (int i = 0; i < _tickExpiredAbandonedCooldowns.Count; i++)
			{
				_abandonedTargetCooldown.TryRemove(_tickExpiredAbandonedCooldowns[i], out _);
			}
		}

		private Entity _cachedWorldEntity = Entity.Null;
		private Entity ActiveWorldEntity
		{
			get
			{
				if (_cachedWorldEntity == Entity.Null || !_context.World.IsAlive(_cachedWorldEntity))
				{
					if (_context.WorldEntity != Entity.Null && _context.World.IsAlive(_context.WorldEntity))
					{
						_cachedWorldEntity = _context.WorldEntity;
					}
					else
					{
						var query = QueryCache.AllWorldStateQuery;
						_context.World.Query(in query, (Entity entity) => _cachedWorldEntity = entity);
					}
				}
				return _cachedWorldEntity;
			}
		}

		private Entity _cachedTerrainEntity = Entity.Null;
		private Entity ActiveTerrainEntity
		{
			get
			{
				if (_cachedTerrainEntity == Entity.Null || !_context.World.IsAlive(_cachedTerrainEntity))
				{
					var we = ActiveWorldEntity;
					if (we != Entity.Null && _context.World.Has<TerrainState>(we))
					{
						_cachedTerrainEntity = we;
					}
					else
					{
						var query = QueryCache.AllTerrainStateQuery;
						_context.World.Query(in query, (Entity entity) => _cachedTerrainEntity = entity);
					}
				}
				return _cachedTerrainEntity;
			}
		}

		private Entity FindWorldEntity() => ActiveWorldEntity;
		private Entity FindTerrainEntity() => ActiveTerrainEntity;

		private int GetTimeOfDayIndex()
		{
			var worldEntity = FindWorldEntity();
			if (worldEntity != Entity.Null && _context.World.Has<WorldState>(worldEntity))
			{
				return _context.World.Get<WorldState>(worldEntity).TimeOfDayIndex;
			}

			return 0;
		}

		private float GetCombatAlertTimer()
		{
			Entity worldEntity = ActiveWorldEntity;
			if (worldEntity != Entity.Null && _context.World.Has<CombatAlertState>(worldEntity))
			{
				return _context.World.Get<CombatAlertState>(worldEntity).UnderAttackAlertTimer;
			}

			return 0f;
		}

		private void SetCombatAlertTimer(float value)
		{
			Entity worldEntity = ActiveWorldEntity;
			if (worldEntity != Entity.Null && _context.World.Has<CombatAlertState>(worldEntity))
			{
				ref var state = ref _context.World.Get<CombatAlertState>(worldEntity);
				state.UnderAttackAlertTimer = value;
			}
		}

		private void TickCombatAlertTimer(float fDelta)
		{
			Entity worldEntity = ActiveWorldEntity;
			if (worldEntity != Entity.Null && _context.World.Has<CombatAlertState>(worldEntity))
			{
				ref var state = ref _context.World.Get<CombatAlertState>(worldEntity);
				if (state.UnderAttackAlertTimer > 0f)
				{
					state.UnderAttackAlertTimer = Math.Max(0f, state.UnderAttackAlertTimer - fDelta);
				}
			}
		}

		private void ProcessTargetAcquisition()
		{
			if (!HasAuthority)
			{
				_attackerCount = 0;
				_targetCandidateCount = 0;
				return;
			}

			if (_currentMovementService == null)
			{
				_targetCandidateCount = 0;
				_context.World.Query(in _enemyQuery, (Entity potentialEnemy, ref Position enemyPos, ref Owner enemyOwner) =>
				{
					if (_context.World.Has<Invulnerable>(potentialEnemy)) return;
					int pIdx = GetPlayerIndexForEntity(potentialEnemy);
					bool flying = IsFlying(potentialEnemy);
					float colRadius = GetTargetCollisionRadius(potentialEnemy);
					float contactRadius = GetUnitContactRadius(potentialEnemy);

					if (_targetCandidateCount >= _targetCandidateArray.Length)
						Array.Resize(ref _targetCandidateArray, _targetCandidateArray.Length * 2);

					_targetCandidateArray[_targetCandidateCount++] = new TargetCandidateSnapshot(potentialEnemy, enemyPos.Value, pIdx, flying, colRadius, contactRadius);
				});

				if (_targetCandidateCount == 0) return;
			}

			_attackerCount = 0;
			int timeOfDay = GetTimeOfDayIndex();
			_context.World.Query(in _targetAcquisitionQuery, (Entity entity, ref Position pos, ref Attack atk, ref Owner owner) =>
			{
				if (atk.Damage <= 0f || _context.World.Has<Invulnerable>(entity) || _context.World.Has<CommandsFrozen>(entity)) return;
				if (!ShouldSimulateEntity(entity)) return;

				bool isAutoCombat = _context.World.Has<AutoCombatState>(entity) && _context.World.Get<AutoCombatState>(entity).Enabled;
				bool isAttackMove = _context.World.Has<AttackMove>(entity);
				bool isPatrol = _context.World.Has<Patrol>(entity);
				bool isIdle = !_context.World.Has<MoveTo>(entity) && !isAttackMove;

				if (!isIdle && !isAttackMove && !isPatrol && !isAutoCombat) return;

				// Stagger target acquisition across ticks in simulation so idle entities do not all scan simultaneously on every 30Hz tick
				if (_currentMovementService != null && (entity.Id + _combatTickCount) % 4 != 0) return;

				float scanRadius = _context.World.Has<ScanRadius>(entity) ? _context.World.Get<ScanRadius>(entity).Value : 15.0f;
				if (timeOfDay == 2) scanRadius *= 0.7f;

				bool isFlying = IsFlying(entity);
				bool canTargetAir = !_context.World.Has<CombatTargeting>(entity) || _context.World.Get<CombatTargeting>(entity).CanTargetAir;
				bool isGroundMelee = !isFlying && atk.Range <= MeleeRangeThreshold;
				int includeFlags = _context.World.Has<PathingFlags>(entity) ? _context.World.Get<PathingFlags>(entity).Value : (int)TerrainPathingFlags.Ground;

				if (_attackerCount >= _attackerArray.Length)
					Array.Resize(ref _attackerArray, _attackerArray.Length * 2);

				_attackerArray[_attackerCount++] = new AttackerSnapshot
				{
					Entity = entity,
					Position = pos.Value,
					AttackRange = atk.Range,
					PlayerIndex = GetPlayerIndexForEntity(entity),
					IsFlying = isFlying,
					CanTargetAir = canTargetAir,
					IsGroundMelee = isGroundMelee,
					ScanMaxDistSq = scanRadius * scanRadius,
					TargetCollisionRadius = GetTargetCollisionRadius(entity),
					UnitContactRadius = GetUnitContactRadius(entity),
					IncludeFlags = includeFlags,
					SelectedTarget = Entity.Null
				};
			});

			if (_attackerCount == 0) return;

			if (_currentMovementService != null)
			{
				ParallelRunner.Execute(_attackerCount, _evaluateAttackerSpatialDelegate);
			}
			else
			{
				ParallelRunner.Execute(_attackerCount, _evaluateAttackerFallbackDelegate);
			}

			for (int i = 0; i < _attackerCount; i++)
			{
				ref readonly var attacker = ref _attackerArray[i];
				if (attacker.SelectedTarget != Entity.Null && _context.World.IsAlive(attacker.Entity))
				{
					_context.World.SetOrAdd(attacker.Entity, new AttackTarget(attacker.SelectedTarget));
				}
			}
		}

		private void EvaluateAttackerSpatialAction(int index)
		{
			EvaluateAttackerTargetWithSpatialGrid(ref _attackerArray[index]);
		}

		private void EvaluateAttackerFallbackAction(int index)
		{
			EvaluateAttackerTarget(ref _attackerArray[index], _targetCandidateArray, _targetCandidateCount);
		}

		private void EvaluateAttackerTargetWithSpatialGrid(ref AttackerSnapshot attacker)
		{
			var movementService = _currentMovementService;
			if (movementService == null)
				return;

			Span<LocalScanCandidate> localCandidates = stackalloc LocalScanCandidate[MaxAcquisitionCandidates];
			int candidateCount = 0;
			float scanRadius = MathF.Sqrt(attacker.ScanMaxDistSq);
			float cellSize = movementService.CollisionCellSize;
			var center = attacker.Position;
			var world = _context.World;

			int minCx = (int)Math.Floor((center.X - scanRadius) / cellSize);
			int maxCx = (int)Math.Floor((center.X + scanRadius) / cellSize);
			int minCz = (int)Math.Floor((center.Z - scanRadius) / cellSize);
			int maxCz = (int)Math.Floor((center.Z + scanRadius) / cellSize);

			for (int cx = minCx; cx <= maxCx; cx++)
			{
				for (int cz = minCz; cz <= maxCz; cz++)
				{
					if (!movementService.TryGetUnitGridCell(cx, cz, out var list) || list == null)
						continue;

					for (int idx = 0; idx < list.Count; idx++)
					{
						var entry = list[idx];
						Entity potentialEnemy = entry.Entity;
						if (potentialEnemy == attacker.Entity) continue;
						if (!world.IsAlive(potentialEnemy) || world.Has<Dead>(potentialEnemy) || world.Has<Invulnerable>(potentialEnemy)) continue;

						int candidatePlayer = GetPlayerIndexForEntity(potentialEnemy);
						if (!ArePlayerIndicesEnemies(attacker.PlayerIndex, candidatePlayer)) continue;

						bool flying = IsFlying(potentialEnemy);
						if (flying && (attacker.IsGroundMelee || !attacker.CanTargetAir)) continue;

						float distSq = (center - entry.Position).LengthSquared();
						if (distSq < attacker.ScanMaxDistSq)
						{
							float colRadius = GetTargetCollisionRadius(potentialEnemy);
							float contactRadius = GetUnitContactRadius(potentialEnemy);
							TargetCandidateSnapshot candidate = new TargetCandidateSnapshot(potentialEnemy, entry.Position, candidatePlayer, flying, colRadius, contactRadius);
							InsertLocalCandidate(localCandidates, ref candidateCount, candidate, distSq);
						}
					}
				}
			}

			EvaluateLocalCandidates(ref attacker, localCandidates, candidateCount);
		}

		private void EvaluateAttackerTarget(ref AttackerSnapshot attacker, TargetCandidateSnapshot[] candidates, int candidateCountTotal)
		{
			Span<LocalScanCandidate> localCandidates = stackalloc LocalScanCandidate[MaxAcquisitionCandidates];
			int candidateCount = 0;

			for (int c = 0; c < candidateCountTotal; c++)
			{
				ref readonly var candidate = ref candidates[c];
				if (ArePlayerIndicesEnemies(attacker.PlayerIndex, candidate.PlayerIndex))
				{
					if (candidate.IsFlying && (attacker.IsGroundMelee || !attacker.CanTargetAir))
					{
						continue;
					}

					float distSq = (attacker.Position - candidate.Position).LengthSquared();
					if (distSq < attacker.ScanMaxDistSq)
					{
						InsertLocalCandidate(localCandidates, ref candidateCount, candidate, distSq);
					}
				}
			}

			EvaluateLocalCandidates(ref attacker, localCandidates, candidateCount);
		}


		private void EvaluateLocalCandidates(ref AttackerSnapshot attacker, Span<LocalScanCandidate> localCandidates, int candidateCount)
		{
			for (int i = 0; i < candidateCount; i++)
			{
				ref readonly var cand = ref localCandidates[i];
				if (cand.Enemy == Entity.Null) continue;
				if (IsTargetStillAbandoned(attacker.Entity, cand.Enemy))
				{
					continue;
				}

				float reach = attacker.AttackRange + cand.TargetCollisionRadius + attacker.TargetCollisionRadius;
				bool isMelee = !attacker.IsFlying && !cand.IsFlying && attacker.AttackRange <= MeleeRangeThreshold;
				float meleeVerticalReach = Math.Max(attacker.AttackRange, DefaultMeleeVerticalReach);
				if (isMelee)
				{
					float contactReach = GetMinimumMeleeContactReach(attacker.UnitContactRadius, cand.UnitContactRadius);
					if (reach < contactReach)
					{
						reach = contactReach;
					}
				}

				bool reachable = IsReachableWithinRangeThreadSafe(attacker.Entity, cand.Enemy, attacker.Position, cand.Position, reach, isMelee, meleeVerticalReach, attacker.IsFlying, attacker.IncludeFlags);
				if (!reachable)
				{
					continue;
				}

				attacker.SelectedTarget = cand.Enemy;
				break;
			}
		}

		private static void InsertLocalCandidate(Span<LocalScanCandidate> candidates, ref int count, in TargetCandidateSnapshot target, float distSq)
		{
			int n = count;
			if (n >= MaxAcquisitionCandidates && distSq >= candidates[MaxAcquisitionCandidates - 1].DistSq)
			{
				return;
			}

			int i = n < MaxAcquisitionCandidates ? n : MaxAcquisitionCandidates - 1;
			while (i > 0 && candidates[i - 1].DistSq > distSq)
			{
				candidates[i] = candidates[i - 1];
				i--;
			}

			candidates[i] = new LocalScanCandidate
			{
				Enemy = target.Entity,
				Position = target.Position,
				TargetCollisionRadius = target.TargetCollisionRadius,
				UnitContactRadius = target.UnitContactRadius,
				IsFlying = target.IsFlying,
				DistSq = distSq
			};

			if (n < MaxAcquisitionCandidates)
			{
				count = n + 1;
			}
		}

		private int GetPlayerIndexForEntity(Entity entity)
		{
			if (entity == Entity.Null || !_context.World.IsAlive(entity)) return 0;
			if (_context.World.Has<UnitOwnerPlayer>(entity))
				return _context.World.Get<UnitOwnerPlayer>(entity).PlayerIndex;
			if (_context.World.Has<Owner>(entity))
			{
				var pEnt = _context.World.Get<Owner>(entity).PlayerEntity.Value;
				if (_context.World.IsAlive(pEnt) && _context.World.Has<UnitOwnerPlayer>(pEnt))
					return _context.World.Get<UnitOwnerPlayer>(pEnt).PlayerIndex;
			}
			return 0;
		}

		private bool IsTargetStillAbandoned(Entity attacker, Entity target)
		{
			return _abandonedTargetCooldown.TryGetValue(attacker, out var entry)
			       && entry.Remaining > 0f
			       && entry.Target == target;
		}

		private void RegisterAbandonedTarget(Entity attacker, Entity target)
		{
			RegisterAbandonedTarget(attacker, target, AbandonedTargetCooldownSeconds);
		}

		private void RegisterAbandonedTarget(Entity attacker, Entity target, float cooldownSeconds)
		{
			_abandonedTargetCooldown[attacker] = (target, cooldownSeconds);
		}

		private void ClearChaseTracking(Entity entity)
		{
			_chaseStuckTime.Remove(entity);
			_lastChaseDist.Remove(entity);
		}

		private void ProcessCombatTicks()
		{
			_tickActionsToAddLastAttacker.Clear();
			_tickActionsToRemoveTarget.Clear();
			_tickActionsToChase.Clear();
			_tickActionsToStopChasing.Clear();
			_tickUnitsToKill.Clear();

			_context.World.Query(in _combatQuery, _combatQueryDelegate);

			ProcessLastAttackers();
			ProcessUnitsToKill();
			ProcessTargetRemovals();
			ProcessChaseActions();
			ProcessStopChasingActions();
			ProcessRetaliationTargets();
		}

		private void ProcessRetaliationTargets()
		{
			if (!HasAuthority)
			{
				_tickNewAttackTargets.Clear();
				return;
			}

			for (int i = 0; i < _tickNewAttackTargets.Count; i++)
			{
				var (attacker, target) = _tickNewAttackTargets[i];
				if (_context.World.IsAlive(attacker))
				{
					_context.World.SetOrAdd(attacker, target);
				}
			}
			_tickNewAttackTargets.Clear();
		}

		private void RecordLastAttacker(Entity target, Entity attacker)
		{
			if (!HasAuthority) return;
			if (_context.World.Has<LastAttacker>(target)) _context.World.Set(target, new LastAttacker(attacker));
			else _tickActionsToAddLastAttacker.Add((target, attacker));
		}

		private void ProcessLastAttackers()
		{
			foreach (var (targetEnt, attackerEnt) in _tickActionsToAddLastAttacker)
			{
				if (_context.World.IsAlive(targetEnt))
				{
					_context.World.AddOrGet(targetEnt, new LastAttacker(attackerEnt)) = new LastAttacker(attackerEnt);
				}
			}
		}

		private void ProcessUnitsToKill()
		{
			if (!HasAuthority)
			{
				_tickUnitsToKill.Clear();
				return;
			}

			foreach (var targetEntity in _tickUnitsToKill)
			{
				ClearChaseTracking(targetEntity);
				_abandonedTargetCooldown.TryRemove(targetEntity, out _);
				if (!_context.World.IsAlive(targetEntity) || _context.World.Has<Dead>(targetEntity)) continue;

				_context.World.Add<Dead>(targetEntity);
				ClearReferencesTo(targetEntity);
				OnKillUnitRequested?.Invoke(targetEntity);
			}
		}

		private void ClearReferencesTo(Entity deadEntity)
		{
			_clearedReferenceTarget = deadEntity;

			_clearedReferenceEntities.Clear();
			_context.World.Query(in _attackTargetReferenceQuery, _attackTargetReferenceDelegate);
			for (int index = 0; index < _clearedReferenceEntities.Count; index++) _context.World.Remove<AttackTarget>(_clearedReferenceEntities[index]);

			_clearedReferenceEntities.Clear();
			_context.World.Query(in _followReferenceQuery, _followReferenceDelegate);
			for (int index = 0; index < _clearedReferenceEntities.Count; index++) _context.World.Remove<Follow>(_clearedReferenceEntities[index]);

			_clearedReferenceEntities.Clear();
			_context.World.Query(in _lastAttackerReferenceQuery, _lastAttackerReferenceDelegate);
			for (int index = 0; index < _clearedReferenceEntities.Count; index++) _context.World.Remove<LastAttacker>(_clearedReferenceEntities[index]);
		}

		private void CollectAttackTargetOfClearedUnit(Entity entity, ref AttackTarget target)
		{
			if (target.Target != _clearedReferenceTarget) return;
			_clearedReferenceEntities.Add(entity);
		}

		private void CollectFollowOfClearedUnit(Entity entity, ref Follow follow)
		{
			if (follow.Target != _clearedReferenceTarget) return;
			_clearedReferenceEntities.Add(entity);
		}

		private void CollectLastAttackerOfClearedUnit(Entity entity, ref LastAttacker lastAttacker)
		{
			if (lastAttacker.Value != _clearedReferenceTarget) return;
			_clearedReferenceEntities.Add(entity);
		}

		private void ProcessTargetRemovals()
		{
			foreach (var ent in _tickActionsToRemoveTarget)
			{
				ClearChaseTracking(ent);
				if (!_context.World.IsAlive(ent)) continue;

				if (_context.World.Has<AttackTarget>(ent)) _context.World.Remove<AttackTarget>(ent);

				if (HandleAttackMoveRemoval(ent)) continue;
				if (HandlePatrolRemoval(ent)) continue;
				HandleMoveToRemoval(ent);
			}
		}

		private bool HandleAttackMoveRemoval(Entity ent)
		{
			if (!_context.World.Has<AttackMove>(ent)) return false;

			var am = _context.World.Get<AttackMove>(ent);
			if (!_context.World.Has<MoveTo>(ent)) _context.World.Add(ent, new MoveTo(am.Target));
			return true;
		}

		private bool HandlePatrolRemoval(Entity ent)
		{
			if (!_context.World.Has<Patrol>(ent)) return false;

			var patrol = _context.World.Get<Patrol>(ent);
			var destVec = patrol.GoingToB ? patrol.PointB : patrol.PointA;
			if (!_context.World.Has<MoveTo>(ent)) _context.World.Add(ent, new MoveTo(destVec));
			return true;
		}

		private void HandleMoveToRemoval(Entity ent)
		{
			if (!_context.World.Has<MoveTo>(ent)) return;

			_context.World.Remove<MoveTo>(ent);
			if (_context.World.Has<Velocity>(ent)) _context.World.Set(ent, new Velocity(Vector3.Zero));
		}

		private void ProcessChaseActions()
		{
			foreach (var (attacker, targetPos) in _tickActionsToChase)
			{
				if (!_context.World.IsAlive(attacker) || !_context.World.Has<AttackTarget>(attacker)) continue;

				bool needsRetarget = true;
				if (_context.World.Has<MoveTo>(attacker))
				{
					var existingTarget = _context.World.Get<MoveTo>(attacker).Target;
					if (Distance(existingTarget, targetPos) < ChaseRetargetDistance) needsRetarget = false;
				}

				if (needsRetarget) _context.World.SetOrAdd(attacker, new MoveTo(targetPos));
			}
		}

		private void ProcessStopChasingActions()
		{
			foreach (var attacker in _tickActionsToStopChasing)
			{
				if (!_context.World.IsAlive(attacker)) continue;

				if (_context.World.Has<MoveTo>(attacker)) _context.World.Remove<MoveTo>(attacker);
				if (_context.World.Has<Velocity>(attacker)) _context.World.Set(attacker, new Velocity(Vector3.Zero));
			}
		}

		private void CombatQueryAction(Entity entity, ref Position pos, ref Attack atk, ref AttackTarget target, ref Owner owner)
		{
			if (!ShouldSimulateEntity(entity)) return;

			if (!_context.World.IsAlive(target.Target) || _context.World.Has<Dead>(target.Target) || _context.World.Has<Invulnerable>(target.Target))
			{
				_tickActionsToRemoveTarget.Add(entity);
				return;
			}

			if (IsFlying(target.Target) && !CanAttackTarget(entity, target.Target))
			{
				_tickActionsToRemoveTarget.Add(entity);
				return;
			}

			if (TryProcessCombatAction(entity, ref pos, ref atk, ref target, ref owner)) return;
		}

		private bool TryProcessCombatAction(Entity entity, ref Position pos, ref Attack atk, ref AttackTarget target, ref Owner owner)
		{
			var currentPos = pos.Value;
			var targetPos = _context.World.Get<Position>(target.Target).Value;
			float effectiveRange = GetEffectiveRange(entity, target.Target, atk.Range, out bool isMelee, out float meleeVerticalReach);

			bool withinRange = isMelee
				? IsWithinMeleeRange(currentPos, targetPos, effectiveRange, meleeVerticalReach)
				: Distance(currentPos, targetPos) <= effectiveRange;

			if (withinRange)
			{
				ClearChaseTracking(entity);
				_tickActionsToStopChasing.Add(entity);

				if (atk.CurrentCooldown > 0) return true;

				if (_context.World.Has<Invulnerable>(target.Target))
				{
					atk.CurrentCooldown = (UnlimitedPowerProvider?.Invoke() == true) ? 0f : GetEffectiveAttackCooldown(entity, atk.Cooldown);
					return true;
				}

				ExecuteAttack(entity, target.Target, ref atk, currentPos, targetPos, owner);
			}
			else
			{
				HandleOutOfRangeAction(entity, target.Target, currentPos, targetPos, effectiveRange, isMelee, meleeVerticalReach);
			}

			return true;
		}

		private bool IsWithinMeleeRange(Vector3 currentPos, Vector3 targetPos, float effectiveRange, float meleeVerticalReach)
		{
			float horizontalDist = new Vector2(currentPos.X - targetPos.X, currentPos.Z - targetPos.Z).Length();
			float verticalDist = Math.Abs(currentPos.Y - targetPos.Y);
			return horizontalDist <= effectiveRange && verticalDist <= meleeVerticalReach;
		}

		private void ExecuteAttack(Entity entity, Entity target, ref Attack atk, Vector3 currentPos, Vector3 targetPos, Owner owner)
		{
			float baseDamage = ResolveAttackDamage(entity, target, ref atk, out float finalDamage);
			ApplyDamageAndEffects(entity, target, finalDamage, baseDamage, ref atk, targetPos, owner);

			atk.CurrentCooldown = (UnlimitedPowerProvider?.Invoke() == true) ? 0f : GetEffectiveAttackCooldown(entity, atk.Cooldown);

			HandleProjectile(entity, target, currentPos, targetPos, atk.Range);
		}

		private float ResolveAttackDamage(Entity entity, Entity target, ref Attack atk, out float finalDamage)
		{
			var resistances = _context.World.Has<Resistances>(target) ? _context.World.Get<Resistances>(target) : default;
			float baseDamage = _context.StatService != null ? _context.StatService.GetStatValue(entity, new StatId(StatKeys.Attack)) : 0f;
			if (baseDamage <= 0) baseDamage = atk.Damage;

			bool magic = DamageTypes.IsMagic(atk.DamageType);
			var damageResult = CombatResolver.ResolveDamage(
				baseDamage: baseDamage, damageVariance: atk.DamageVariance,
				critChance: atk.CritChance, critMultiplier: atk.CritMultiplier,
				physicalResistance: resistances.Physical, magicResistance: resistances.Magic,
				flatPenetration: ReadPenetration(entity, magic, atk.FlatArmorPenetration),
				percentPenetration: ReadPenetrationPercent(entity, magic, atk.PercentArmorPenetration),
				damageType: atk.DamageType, config: Config
			);

			finalDamage = damageResult.FinalDamage;
			return baseDamage;
		}

		/// <summary>
		///     The flat penetration that matches the hit's damage type, read from the stat snapshot so
		///     items and buffs are honoured. The attack component's authored value is the fallback for
		///     an entity whose stats have never been recalculated.
		/// </summary>
		private float ReadPenetration(Entity entity, bool magic, float authored)
		{
			string key = magic ? StatKeys.FlatMagicPenetration : StatKeys.FlatArmorPenetration;
			return ReadPenetrationStat(entity, key, magic ? 0f : authored);
		}

		private float ReadPenetrationPercent(Entity entity, bool magic, float authored)
		{
			string key = magic ? StatKeys.PercentMagicPenetration : StatKeys.PercentArmorPenetration;
			return ReadPenetrationStat(entity, key, magic ? 0f : authored);
		}

		private float ReadPenetrationStat(Entity entity, string statKey, float fallback)
		{
			float value = _context.StatService != null ? _context.StatService.GetStatValue(entity, new StatId(statKey)) : 0f;
			return value > 0f ? value : fallback;
		}

		private void ApplyDamageAndEffects(Entity entity, Entity target, float damage, float baseDamage, ref Attack atk, Vector3 targetPos, Owner owner)
		{
			RecordLastAttacker(target, entity);

			OnUnitAttackedCallback?.Invoke(entity, target);

			float dealt = AbsorbDamage(target, damage);
			if (dealt > 0f) OnUnitDamagedCallback?.Invoke(target, entity, dealt);

			float newHp = 0f;
			if (HasAuthority)
			{
				var targetHealth = _context.World.Get<Health>(target);
				newHp = Math.Max(0, targetHealth.Current - dealt);
				targetHealth.Current = newHp;
				targetHealth.TimeSinceLastDamage = 0f;
				_context.World.Set(target, targetHealth);
			}

			if (atk.SplashType != SplashType.None && atk.SplashOuterRadius > 0f)
				EvaluateSplashDamage(entity, target, targetPos, baseDamage, atk, owner);

			CheckCombatAlert(target);
			AcquireNewTargetFromAttacker(entity, target);

			if (HasAuthority && newHp <= 0) _tickUnitsToKill.Add(target);
			else OnDamageFlashRequested?.Invoke(target);
		}

		private void CheckCombatAlert(Entity target)
		{
			if (!_context.World.Has<DefinitionId>(target)) return;

			int targetPlayer = _context.World.Has<UnitOwnerPlayer>(target) ? _context.World.Get<UnitOwnerPlayer>(target).PlayerIndex : 0;
			string targetUnitId = _context.World.Get<DefinitionId>(target).Value;
			if (GetCombatAlertTimer() <= 0f)
			{
				SetCombatAlertTimer(UnderAttackAlertCooldown);
				OnUnderAttackAlertRequested?.Invoke(targetPlayer, targetUnitId);
			}
		}

		private void AcquireNewTargetFromAttacker(Entity entity, Entity target)
		{
			if (!_context.World.IsAlive(target) || _context.World.Has<Dead>(target) || _context.World.Has<AttackTarget>(target)) return;
			if (!_context.World.Has<Attack>(target)) return;

			bool hasMoveTo = _context.World.Has<MoveTo>(target);
			if (!hasMoveTo || _context.World.Has<AttackMove>(target))
			{
				if (IsFlying(entity) && !CanAttackTarget(target, entity)) return;
				_tickNewAttackTargets.Add((target, new AttackTarget(entity)));
			}
		}

		private void HandleProjectile(Entity entity, Entity target, Vector3 currentPos, Vector3 targetPos, float range)
		{
			if (range <= 3f) return;

			string? weaponId = null;
			if (_context.World.Has<Inventory>(entity))
			{
				var inv = _context.World.Get<Inventory>(entity);
				for (int itemIndex = 0; itemIndex < inv.Count; itemIndex++)
				{
					string? item = inv.GetItemId(itemIndex);
					if (item == null) continue;
					string[]? grantedWeapons = ItemGrantedWeaponsProvider?.Invoke(item);
					if (grantedWeapons != null && grantedWeapons.Length > 0)
					{
						weaponId = grantedWeapons[0];
						break;
					}
				}
			}

			if (weaponId == null && _context.World.Has<DefinitionId>(entity))
			{
				var defId = _context.World.Get<DefinitionId>(entity).Value;
				var weapons = UnitWeaponsProvider?.Invoke(defId);
				weaponId = (weapons != null && weapons.Length > 0) ? weapons[0] : defId;
			}

			OnWeaponProjectileRequested?.Invoke(currentPos, targetPos, weaponId, target, GetPlayerIndexForEntity(entity));
		}

		private void HandleOutOfRangeAction(Entity entity, Entity target, Vector3 currentPos, Vector3 targetPos, float effectiveRange, bool isMelee, float meleeVerticalReach)
		{
			if (_context.World.Has<Building>(entity))
			{
				_tickActionsToRemoveTarget.Add(entity);
				ClearChaseTracking(entity);
			}
			else if (!_context.World.Has<HoldPosition>(entity) && _context.World.Has<Movable>(entity))
			{
				if (!IsReachableWithinRange(entity, target, currentPos, targetPos, effectiveRange, isMelee, meleeVerticalReach))
				{
					RegisterAbandonedTarget(entity, target);
					_tickActionsToRemoveTarget.Add(entity);
					ClearChaseTracking(entity);
					return;
				}

				_tickActionsToChase.Add((entity, targetPos));
				UpdateChaseTracking(entity, Distance(currentPos, targetPos), target);
			}
			else
			{
				_tickActionsToRemoveTarget.Add(entity);
				ClearChaseTracking(entity);
			}
		}

		private void EvaluateSplashDamage(Entity attacker, Entity primaryTarget, Vector3 impactPos, float baseDamage, Attack atk, Owner attackerOwner)
		{
			int attackerPlayer = _context.World.Has<UnitOwnerPlayer>(attacker) ? _context.World.Get<UnitOwnerPlayer>(attacker).PlayerIndex : 0;

			if (_currentMovementService != null)
			{
				_splashAttacker = attacker;
				_splashPrimaryTarget = primaryTarget;
				_splashImpactPos = impactPos;
				_splashBaseDamage = baseDamage;
				_splashAtk = atk;
				_splashAttackerOwner = attackerOwner;
				_splashAttackerPlayer = attackerPlayer;

				_currentMovementService.QueryUnitsInRadius(impactPos, atk.SplashOuterRadius, _splashCandidateCallback);
				return;
			}

			var splashQuery = QueryCache.AllPositionAndHealthAndOwnerNoneDeadQuery;

			_context.World.Query(in splashQuery, (Entity splashEnt, ref Position sPos, ref Health sHp, ref Owner sOwner) =>
			{
				if (splashEnt == primaryTarget || splashEnt == attacker) return;
				if (_context.World.Has<Invulnerable>(splashEnt)) return;

				if (!atk.FriendlyFire)
				{
					int splashPlayer = _context.World.Has<UnitOwnerPlayer>(splashEnt) ? _context.World.Get<UnitOwnerPlayer>(splashEnt).PlayerIndex : 0;
					if (!ArePlayerIndicesEnemies(attackerPlayer, splashPlayer) || sOwner.PlayerEntity == attackerOwner.PlayerEntity) return;
				}

				float distToImpact = Distance(impactPos, sPos.Value);
				float splashRatio = CombatResolver.CalculateSplashRatio(
					distToImpact, atk.SplashType,
					atk.SplashInnerRadius, atk.SplashMediumRadius, atk.SplashOuterRadius,
					atk.SplashInnerRatio, atk.SplashMediumRatio, atk.SplashOuterRatio
				);

				if (splashRatio > 0f)
				{
					ApplySplashDamage(attacker, splashEnt, splashRatio, baseDamage, ref atk, ref sHp);
				}
			});
		}

		private void OnSplashCandidateFound(in MovementAndPathfindingService.SpatialGridEntry entry)
		{
			Entity splashEnt = entry.Entity;
			if (splashEnt == _splashPrimaryTarget || splashEnt == _splashAttacker) return;
			if (!_context.World.IsAlive(splashEnt) || _context.World.Has<Dead>(splashEnt) || !_context.World.Has<Health>(splashEnt)) return;
			if (_context.World.Has<Invulnerable>(splashEnt)) return;

			Owner sOwner = _context.World.Has<Owner>(splashEnt) ? _context.World.Get<Owner>(splashEnt) : default;

			if (!_splashAtk.FriendlyFire)
			{
				int splashPlayer = _context.World.Has<UnitOwnerPlayer>(splashEnt) ? _context.World.Get<UnitOwnerPlayer>(splashEnt).PlayerIndex : 0;
				if (!ArePlayerIndicesEnemies(_splashAttackerPlayer, splashPlayer) || sOwner.PlayerEntity == _splashAttackerOwner.PlayerEntity) return;
			}

			float distToImpact = Distance(_splashImpactPos, entry.Position);
			float splashRatio = CombatResolver.CalculateSplashRatio(
				distToImpact, _splashAtk.SplashType,
				_splashAtk.SplashInnerRadius, _splashAtk.SplashMediumRadius, _splashAtk.SplashOuterRadius,
				_splashAtk.SplashInnerRatio, _splashAtk.SplashMediumRatio, _splashAtk.SplashOuterRatio
			);

			if (splashRatio > 0f)
			{
				ref var sHp = ref _context.World.Get<Health>(splashEnt);
				ApplySplashDamage(_splashAttacker, splashEnt, splashRatio, _splashBaseDamage, ref _splashAtk, ref sHp);
			}
		}

		private void ApplySplashDamage(Entity attacker, Entity splashEnt, float splashRatio, float baseDamage, ref Attack atk, ref Health sHp)
		{
			var sResistances = _context.World.Has<Resistances>(splashEnt) ? _context.World.Get<Resistances>(splashEnt) : default;

			bool sMagic = DamageTypes.IsMagic(atk.DamageType);
			var sResult = CombatResolver.ResolveDamage(
				baseDamage: baseDamage * splashRatio, damageVariance: atk.DamageVariance,
				critChance: atk.CritChance, critMultiplier: atk.CritMultiplier,
				physicalResistance: sResistances.Physical, magicResistance: sResistances.Magic,
				flatPenetration: ReadPenetration(_splashAttacker, sMagic, atk.FlatArmorPenetration),
				percentPenetration: ReadPenetrationPercent(_splashAttacker, sMagic, atk.PercentArmorPenetration),
				damageType: atk.DamageType, config: Config
			);

			float splashDamage = AbsorbDamage(splashEnt, sResult.FinalDamage);
			if (splashDamage <= 0f) return;

			if (HasAuthority)
			{
				sHp.Current = Math.Max(0, sHp.Current - splashDamage);
				sHp.TimeSinceLastDamage = 0f;
				_context.World.Set(splashEnt, sHp);
			}

			OnUnitDamagedCallback?.Invoke(splashEnt, attacker, splashDamage);

			RecordLastAttacker(splashEnt, attacker);

			if (HasAuthority && sHp.Current <= 0) _tickUnitsToKill.Add(splashEnt);
			else OnDamageFlashRequested?.Invoke(splashEnt);
		}

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

		private bool TryGetNavMeshQuery(out DtNavMeshQuery navMeshQuery, out DtNavMesh navMesh)
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

		private void ProcessHealingTicks()
		{
			_tickHealRemoveTargets.Clear();
			_tickHealChaseTargets.Clear();
			_tickHealStopChasing.Clear();
			_context.World.Query(in _healingExecutionQuery, _healingExecutionQueryDelegate);

			ProcessHealRemovals();
			ProcessHealChases();
			ProcessHealStopChasing();
		}

		private void ProcessHealRemovals()
		{
			foreach (var ent in _tickHealRemoveTargets)
			{
				if (_context.World.IsAlive(ent) && _context.World.Has<HealingTarget>(ent))
				{
					_context.World.Remove<HealingTarget>(ent);
				}
			}
		}

		private void ProcessHealChases()
		{
			foreach (var (healer, targetPos) in _tickHealChaseTargets)
			{
				if (!_context.World.IsAlive(healer)) continue;

				bool needsRetarget = true;
				if (_context.World.Has<MoveTo>(healer))
				{
					var existingTarget = _context.World.Get<MoveTo>(healer).Target;
					if (Distance(existingTarget, targetPos) < ChaseRetargetDistance) needsRetarget = false;
				}

				if (needsRetarget) _context.World.SetOrAdd(healer, new MoveTo(targetPos));
			}
		}

		private void ProcessHealStopChasing()
		{
			foreach (var healer in _tickHealStopChasing)
			{
				if (!_context.World.IsAlive(healer)) continue;

				if (_context.World.Has<MoveTo>(healer)) _context.World.Remove<MoveTo>(healer);
				if (_context.World.Has<Velocity>(healer)) _context.World.Set(healer, new Velocity(Vector3.Zero));
			}
		}

		private void HealingExecutionQueryAction(Entity entity, ref Position pos, ref Attack atk, ref HealingTarget target, ref Owner owner)
		{
			if (!IsValidHealingTarget(entity, target.Target, out var targetHealth)) return;

			if (HandleHealingDistance(entity, pos.Value, target.Target, atk.Range)) return;

			_tickHealStopChasing.Add(entity);

			if (atk.CurrentCooldown > 0) return;

			float healAmount = atk.Damage * GetHealingMultiplier(entity);
			ApplyHealing(target.Target, healAmount, targetHealth);

			atk.CurrentCooldown = GetHealCooldown(entity, atk.Cooldown);

			InvokeHealEvents(entity, pos.Value, target.Target, healAmount);
		}

		private bool IsValidHealingTarget(Entity entity, Entity targetEntity, out Health targetHealth)
		{
			targetHealth = default;
			if (!_context.World.IsAlive(targetEntity) || _context.World.Has<Dead>(targetEntity))
			{
				_tickHealRemoveTargets.Add(entity);
				return false;
			}

			targetHealth = _context.World.Get<Health>(targetEntity);
			if (targetHealth.Current >= targetHealth.Max)
			{
				_tickHealRemoveTargets.Add(entity);
				return false;
			}

			return true;
		}

		private float GetHealingMultiplier(Entity casterEntity)
		{
			if (_context.StatService == null) return 1f;
			float multiplier = _context.StatService.GetStatValue(casterEntity, new StatId(StatKeys.HealingMultiplier));
			return multiplier > 0f ? multiplier : 1f;
		}

		private void ApplyHealing(Entity targetEntity, float healAmount, Health targetHealth)
		{
			if (!HasAuthority) return;

			float newHp = Math.Min(targetHealth.Max, targetHealth.Current + healAmount);
			_context.World.Set(targetEntity, new Health(newHp, targetHealth.Max));
		}

		private float GetHealCooldown(Entity entity, float baseCooldown)
		{
			if (UnlimitedPowerProvider?.Invoke() == true) return 0f;
			return GetEffectiveAttackCooldown(entity, baseCooldown);
		}

		private void InvokeHealEvents(Entity entity, Vector3 pos, Entity targetEntity, float healAmount)
		{
			OnHealEffectRequested?.Invoke(pos, _context.World.Get<Position>(targetEntity).Value);
			OnHealFlashRequested?.Invoke(targetEntity);
			OnUnitHealedCallback?.Invoke(targetEntity, entity, healAmount);
		}

		private bool HandleHealingDistance(Entity entity, Vector3 currentPos, Entity targetEntity, float atkRange)
		{
			var targetPos = _context.World.Get<Position>(targetEntity).Value;
			float effectiveRange = Math.Max(atkRange + GetTargetCollisionRadius(targetEntity), GetMinimumMeleeContactReach(entity, targetEntity));

			if (Distance(currentPos, targetPos) <= effectiveRange) return false;

			if (!_context.World.Has<HoldPosition>(entity) && _context.World.Has<Movable>(entity))
			{
				_tickHealChaseTargets.Add((entity, targetPos));
			}

			return true;
		}

		private string _aoeDamageType = "magic";

		/// <summary>
		/// Applies damage through the combat resolver so armor, penetration and the damage/armor type
		/// matrix apply. This is the single-target counterpart to <see cref="DealSpellDamageAOE"/>.
		/// </summary>
		public void DealResolvedDamage(Entity attacker, Entity target, float amount, string damageType)
		{
			if (amount <= 0f) return;
			if (!_context.World.IsAlive(target) || _context.World.Has<Dead>(target) || !_context.World.Has<Health>(target)) return;
			if (_context.World.Has<Invulnerable>(target)) return;

			_aoeKillList.Clear();
			_aoeLastAttackerList.Clear();

			UpdateAoeLastAttacker(target, attacker);

			ref var hp = ref _context.World.Get<Health>(target);
			var result = CombatResolver.ResolveAbilityDamage(_context.World, attacker, target, amount, damageType);
			ApplyResolvedDamage(target, ref hp, result.FinalDamage, attacker);

			ProcessAoeLastAttackers();
			ProcessAoeKillList();
		}

		public void DealSpellDamageAOE(Vector3 position, float radius, float damage, Entity casterEntity, bool enemyOnly = true, string damageType = "magic")
		{
			_aoeKillList.Clear();
			_aoeLastAttackerList.Clear();
			_aoeDamageType = damageType;

			int casterPlayer = GetPlayerIndexForEntity(casterEntity);

			if (_currentMovementService != null)
			{
				_aoePosition = position;
				_aoeRadius = radius;
				_aoeDamage = damage;
				_aoeCasterEntity = casterEntity;
				_aoeEnemyOnly = enemyOnly;
				_aoeCasterPlayer = casterPlayer;

				_currentMovementService.QueryUnitsInRadius(position, radius, _aoeDamageCandidateCallback);
			}
			else
			{
				var query = QueryCache.AllPositionAndHealthNoneDeadQuery;
				_context.World.Query(in query, (Entity entity, ref Position pos, ref Health hp) => { ProcessAoeTarget(entity, ref pos, ref hp, position, radius, damage, casterEntity, enemyOnly); });
			}

			ProcessAoeLastAttackers();
			ProcessAoeKillList();
		}

		private void OnAoeDamageCandidateFound(in MovementAndPathfindingService.SpatialGridEntry entry)
		{
			Entity entity = entry.Entity;
			if (!_context.World.IsAlive(entity) || _context.World.Has<Dead>(entity) || !_context.World.Has<Health>(entity)) return;

			ref var hp = ref _context.World.Get<Health>(entity);
			ProcessAoeTarget(entity, ref hp, _aoeDamage, _aoeCasterEntity, entry.Position, _aoePosition, _aoeRadius, _aoeEnemyOnly);
		}

		private void ProcessAoeTarget(Entity entity, ref Health hp, float damage, Entity casterEntity, Vector3 entityPos, Vector3 position, float radius, bool enemyOnly)
		{
			if (!IsAoeTargetValid(entity, entityPos, position, radius, enemyOnly, casterEntity)) return;

			UpdateAoeLastAttacker(entity, casterEntity);

			ApplyAoeDamage(entity, ref hp, damage, casterEntity);
		}

		private void ProcessAoeTarget(Entity entity, ref Position pos, ref Health hp, Vector3 position, float radius, float damage, Entity casterEntity, bool enemyOnly)
		{
			if (!IsAoeTargetValid(entity, pos.Value, position, radius, enemyOnly, casterEntity)) return;

			UpdateAoeLastAttacker(entity, casterEntity);

			ApplyAoeDamage(entity, ref hp, damage, casterEntity);
		}

		private bool IsAoeTargetValid(Entity entity, Vector3 entityPos, Vector3 aoePosition, float aoeRadius, bool enemyOnly, Entity casterEntity)
		{
			if (_context.World.Has<Invulnerable>(entity)) return false;

			if (enemyOnly)
			{
				int casterPlayer = GetPlayerIndexForEntity(casterEntity);
				int targetPlayer = GetPlayerIndexForEntity(entity);
				if (!ArePlayerIndicesEnemies(casterPlayer, targetPlayer)) return false;
			}

			if (Vector3.Distance(entityPos, aoePosition) > aoeRadius) return false;

			return true;
		}

		private void UpdateAoeLastAttacker(Entity entity, Entity casterEntity)
		{
			if (casterEntity != Entity.Null && _context.World.IsAlive(casterEntity))
			{
				if (!HasAuthority) return;
				if (_context.World.Has<LastAttacker>(entity)) _context.World.Set(entity, new LastAttacker(casterEntity));
				else _aoeLastAttackerList.Add((entity, casterEntity));
			}
		}

		private void ApplyAoeDamage(Entity entity, ref Health hp, float damage, Entity casterEntity)
		{
			var result = CombatResolver.ResolveAbilityDamage(_context.World, casterEntity, entity, damage, _aoeDamageType);
			ApplyResolvedDamage(entity, ref hp, result.FinalDamage, casterEntity);
		}

		/// <summary>
		///     Runs damage through the entity's absorb pools before health. Called by every damage
		///     source, so a shield is worth the same against an attack, a splash, a spell or an ability.
		/// </summary>
		private float AbsorbDamage(Entity entity, float damage)
		{
			if (damage <= 0f) return 0f;
			if (!_context.World.Has<AbsorbShield>(entity)) return damage;

			return _context.World.Get<AbsorbShield>(entity).Consume(damage);
		}

		/// <summary>Heals one unit, honouring the caster's healing multiplier.</summary>
		public void HealSingle(Entity casterEntity, Entity target, float healAmount)
		{
			if (healAmount <= 0f) return;
			if (!_context.World.IsAlive(target) || _context.World.Has<Dead>(target) || !_context.World.Has<Health>(target)) return;

			float scaled = healAmount * GetHealingMultiplier(casterEntity);
			ref var hp = ref _context.World.Get<Health>(target);
			float before = hp.Current;
			float after = Math.Min(hp.Max, before + scaled);
			float healed = after - before;
			if (healed <= 0f) return;

			if (HasAuthority) hp.Current = after;

			OnHealFlashRequested?.Invoke(target);
			OnUnitHealedCallback?.Invoke(target, casterEntity, healed);
		}

		private void ApplyResolvedDamage(Entity entity, ref Health hp, float finalDamage, Entity casterEntity)
		{
			finalDamage = AbsorbDamage(entity, finalDamage);
			if (finalDamage <= 0f) return;

			float newHp = Math.Max(0, hp.Current - finalDamage);
			if (HasAuthority) hp.Current = newHp;

			OnUnitDamagedCallback?.Invoke(entity, casterEntity, finalDamage);

			if (HasAuthority && newHp <= 0) _aoeKillList.Add(entity);
			else OnDamageFlashRequested?.Invoke(entity);
		}

		private void ProcessAoeLastAttackers()
		{
			foreach (var (targetEnt, attackerEnt) in _aoeLastAttackerList)
			{
				if (_context.World.IsAlive(targetEnt))
				{
					_context.World.AddOrGet(targetEnt, new LastAttacker(attackerEnt)) = new LastAttacker(attackerEnt);
				}
			}
		}

		private void ProcessAoeKillList()
		{
			if (!HasAuthority)
			{
				_aoeKillList.Clear();
				return;
			}

			foreach (var entity in _aoeKillList)
			{
				if (!_context.World.IsAlive(entity) || _context.World.Has<Dead>(entity)) continue;

				_context.World.Add<Dead>(entity);
				ClearReferencesTo(entity);
				OnKillUnitRequested?.Invoke(entity);
			}
		}

		public void HealAOE(Vector3 position, float radius, float healAmount, Entity casterEntity = default)
		{
			int casterPlayer = GetPlayerIndexForEntity(casterEntity);
			healAmount *= GetHealingMultiplier(casterEntity);

			if (_currentMovementService != null)
			{
				_healPosition = position;
				_healRadius = radius;
				_healAmount = healAmount;
				_healCasterEntity = casterEntity;
				_healCasterPlayer = casterPlayer;

				_currentMovementService.QueryUnitsInRadius(position, radius, _healCandidateCallback);
				return;
			}

			var query = QueryCache.AllPositionAndHealthNoneDeadQuery;
			_context.World.Query(in query, (Entity entity, ref Position pos, ref Health hp) =>
			{
				int targetPlayer = GetPlayerIndexForEntity(entity);
				if (ArePlayerIndicesEnemies(casterPlayer, targetPlayer)) return;

				if (Vector3.Distance(pos.Value, position) <= radius)
				{
					if (HasAuthority) hp.Current = Math.Min(hp.Max, hp.Current + healAmount);
					OnHealFlashRequested?.Invoke(entity);
					OnUnitHealedCallback?.Invoke(entity, casterEntity, healAmount);
				}
			});
		}

		private void OnAoeHealCandidateFound(in MovementAndPathfindingService.SpatialGridEntry entry)
		{
			Entity entity = entry.Entity;
			if (!_context.World.IsAlive(entity) || _context.World.Has<Dead>(entity) || !_context.World.Has<Health>(entity)) return;

			int targetPlayer = GetPlayerIndexForEntity(entity);
			if (ArePlayerIndicesEnemies(_healCasterPlayer, targetPlayer)) return;

			if (Vector3.Distance(entry.Position, _healPosition) <= _healRadius)
			{
				if (HasAuthority)
				{
					ref var hp = ref _context.World.Get<Health>(entity);
					hp.Current = Math.Min(hp.Max, hp.Current + _healAmount);
				}

				OnHealFlashRequested?.Invoke(entity);
				OnUnitHealedCallback?.Invoke(entity, _healCasterEntity, _healAmount);
			}
		}
	}
}
