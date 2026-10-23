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
using Realm.Client.Core;
using PathingFlags = Realm.Ecs.Components.Movement.PathingFlags;

namespace Realm.Client.Services
{
	public class CombatAndDamageService
	{
		private readonly Func<bool>? _unlimitedPowerProvider;
		private readonly CombatConfig _combatConfig = new();
		public CombatConfig Config => _combatConfig;

		private const float UnderAttackAlertCooldown = 8f;

		// Vertical terrain delta counts toward range so units at the foot of a cliff are not
		// "in range" of a tower on the summit (combat is 3D, not a top-down projection).
		private static float Distance(Vector3 a, Vector3 b)
		{
			return (a - b).Length();
		}

		private bool IsFlying(Entity entity)
		{
			return GameContext.World.Has<PathingFlags>(entity)
			       && ((TerrainPathingFlags)GameContext.World.Get<PathingFlags>(entity).Value & TerrainPathingFlags.Flying) != 0;
		}

		private bool CanAttackTarget(Entity attacker, Entity target)
		{
			if (!IsFlying(target))
			{
				return true;
			}

			if (!GameContext.World.Has<CombatTargeting>(attacker))
			{
				return true;
			}

			return GameContext.World.Get<CombatTargeting>(attacker).CanTargetAir;
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

		public CombatAndDamageService(Func<bool>? unlimitedPowerProvider = null)
		{
			_unlimitedPowerProvider = unlimitedPowerProvider;
			_combatQueryDelegate = CombatQueryAction;
			_healingExecutionQueryDelegate = HealingExecutionQueryAction;

			_splashCandidateCallback = OnSplashCandidateFound;
			_aoeDamageCandidateCallback = OnAoeDamageCandidateFound;
			_healCandidateCallback = OnAoeHealCandidateFound;
			_evaluateAttackerSpatialDelegate = EvaluateAttackerSpatialAction;
			_evaluateAttackerFallbackDelegate = EvaluateAttackerFallbackAction;
		}

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
				if (!GameContext.World.IsAlive(kvp.Key.Attacker) || !GameContext.World.IsAlive(kvp.Key.Target))
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
				if (_cachedWorldEntity == Entity.Null || !GameContext.World.IsAlive(_cachedWorldEntity))
				{
					if (ECSContext.WorldEntity != Entity.Null && GameContext.World.IsAlive(ECSContext.WorldEntity))
					{
						_cachedWorldEntity = ECSContext.WorldEntity;
					}
					else
					{
						var query = QueryCache.AllWorldStateQuery;
						GameContext.World.Query(in query, (Entity entity) => _cachedWorldEntity = entity);
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
				if (_cachedTerrainEntity == Entity.Null || !GameContext.World.IsAlive(_cachedTerrainEntity))
				{
					var we = ActiveWorldEntity;
					if (we != Entity.Null && GameContext.World.Has<TerrainState>(we))
					{
						_cachedTerrainEntity = we;
					}
					else
					{
						var query = QueryCache.AllTerrainStateQuery;
						GameContext.World.Query(in query, (Entity entity) => _cachedTerrainEntity = entity);
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
			if (worldEntity != Entity.Null && GameContext.World.Has<WorldState>(worldEntity))
			{
				return GameContext.World.Get<WorldState>(worldEntity).TimeOfDayIndex;
			}

			return 0;
		}

		private float GetCombatAlertTimer()
		{
			Entity worldEntity = ActiveWorldEntity;
			if (worldEntity != Entity.Null && GameContext.World.Has<CombatAlertState>(worldEntity))
			{
				return GameContext.World.Get<CombatAlertState>(worldEntity).UnderAttackAlertTimer;
			}

			return 0f;
		}

		private void SetCombatAlertTimer(float value)
		{
			Entity worldEntity = ActiveWorldEntity;
			if (worldEntity != Entity.Null && GameContext.World.Has<CombatAlertState>(worldEntity))
			{
				ref var state = ref GameContext.World.Get<CombatAlertState>(worldEntity);
				state.UnderAttackAlertTimer = value;
			}
		}

		private void TickCombatAlertTimer(float fDelta)
		{
			Entity worldEntity = ActiveWorldEntity;
			if (worldEntity != Entity.Null && GameContext.World.Has<CombatAlertState>(worldEntity))
			{
				ref var state = ref GameContext.World.Get<CombatAlertState>(worldEntity);
				if (state.UnderAttackAlertTimer > 0f)
				{
					state.UnderAttackAlertTimer = Math.Max(0f, state.UnderAttackAlertTimer - fDelta);
				}
			}
		}

		private void ProcessTargetAcquisition()
		{
			if (_currentMovementService == null)
			{
				_targetCandidateCount = 0;
				GameContext.World.Query(in _enemyQuery, (Entity potentialEnemy, ref Position enemyPos, ref Owner enemyOwner) =>
				{
					if (GameContext.World.Has<Invulnerable>(potentialEnemy)) return;
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
			GameContext.World.Query(in _targetAcquisitionQuery, (Entity entity, ref Position pos, ref Attack atk, ref Owner owner) =>
			{
				if (atk.Damage <= 0f || GameContext.World.Has<Invulnerable>(entity) || GameContext.World.Has<CommandsFrozen>(entity)) return;

				bool isAutoCombat = GameContext.World.Has<AutoCombatState>(entity) && GameContext.World.Get<AutoCombatState>(entity).Enabled;
				bool isAttackMove = GameContext.World.Has<AttackMove>(entity);
				bool isPatrol = GameContext.World.Has<Patrol>(entity);
				bool isIdle = !GameContext.World.Has<MoveTo>(entity) && !isAttackMove;

				if (!isIdle && !isAttackMove && !isPatrol && !isAutoCombat) return;

				// Stagger target acquisition across ticks in simulation so idle entities do not all scan simultaneously on every 30Hz tick
				if (_currentMovementService != null && (entity.Id + _combatTickCount) % 4 != 0) return;

				float scanRadius = GameContext.World.Has<ScanRadius>(entity) ? GameContext.World.Get<ScanRadius>(entity).Value : 15.0f;
				if (timeOfDay == 2) scanRadius *= 0.7f;

				bool isFlying = IsFlying(entity);
				bool canTargetAir = !GameContext.World.Has<CombatTargeting>(entity) || GameContext.World.Get<CombatTargeting>(entity).CanTargetAir;
				bool isGroundMelee = !isFlying && atk.Range <= MeleeRangeThreshold;
				int includeFlags = GameContext.World.Has<PathingFlags>(entity) ? GameContext.World.Get<PathingFlags>(entity).Value : (int)TerrainPathingFlags.Ground;

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
				if (attacker.SelectedTarget != Entity.Null && GameContext.World.IsAlive(attacker.Entity))
				{
					GameContext.World.SetOrAdd(attacker.Entity, new AttackTarget(attacker.SelectedTarget));
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
			var world = GameContext.World;

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
						if (!NetworkService.ArePlayerIndicesEnemies(attacker.PlayerIndex, candidatePlayer)) continue;

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
				if (NetworkService.ArePlayerIndicesEnemies(attacker.PlayerIndex, candidate.PlayerIndex))
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
			if (entity == Entity.Null || !GameContext.World.IsAlive(entity)) return 0;
			if (GameContext.World.Has<UnitOwnerPlayer>(entity))
				return GameContext.World.Get<UnitOwnerPlayer>(entity).PlayerIndex;
			if (GameContext.World.Has<Owner>(entity))
			{
				var pEnt = GameContext.World.Get<Owner>(entity).PlayerEntity.Value;
				if (GameContext.World.IsAlive(pEnt) && GameContext.World.Has<UnitOwnerPlayer>(pEnt))
					return GameContext.World.Get<UnitOwnerPlayer>(pEnt).PlayerIndex;
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

			GameContext.World.Query(in _combatQuery, _combatQueryDelegate);

			ProcessLastAttackers();
			ProcessUnitsToKill();
			ProcessTargetRemovals();
			ProcessChaseActions();
			ProcessStopChasingActions();
			ProcessRetaliationTargets();
		}

		private void ProcessRetaliationTargets()
		{
			for (int i = 0; i < _tickNewAttackTargets.Count; i++)
			{
				var (attacker, target) = _tickNewAttackTargets[i];
				if (GameContext.World.IsAlive(attacker))
				{
					GameContext.World.SetOrAdd(attacker, target);
				}
			}
			_tickNewAttackTargets.Clear();
		}

		private void ProcessLastAttackers()
		{
			foreach (var (targetEnt, attackerEnt) in _tickActionsToAddLastAttacker)
			{
				if (GameContext.World.IsAlive(targetEnt))
				{
					GameContext.World.AddOrGet(targetEnt, new LastAttacker(attackerEnt)) = new LastAttacker(attackerEnt);
				}
			}
		}

		private void ProcessUnitsToKill()
		{
			foreach (var targetEntity in _tickUnitsToKill)
			{
				ClearChaseTracking(targetEntity);
				_abandonedTargetCooldown.TryRemove(targetEntity, out _);
				if (!GameContext.World.IsAlive(targetEntity) || GameContext.World.Has<Dead>(targetEntity)) continue;

				GameContext.World.Add<Dead>(targetEntity);
				OnKillUnitRequested?.Invoke(targetEntity);
			}
		}

		private void ProcessTargetRemovals()
		{
			foreach (var ent in _tickActionsToRemoveTarget)
			{
				ClearChaseTracking(ent);
				if (!GameContext.World.IsAlive(ent)) continue;

				if (GameContext.World.Has<AttackTarget>(ent)) GameContext.World.Remove<AttackTarget>(ent);

				if (HandleAttackMoveRemoval(ent)) continue;
				if (HandlePatrolRemoval(ent)) continue;
				HandleMoveToRemoval(ent);
			}
		}

		private bool HandleAttackMoveRemoval(Entity ent)
		{
			if (!GameContext.World.Has<AttackMove>(ent)) return false;

			var am = GameContext.World.Get<AttackMove>(ent);
			if (!GameContext.World.Has<MoveTo>(ent)) GameContext.World.Add(ent, new MoveTo(am.Target));
			return true;
		}

		private bool HandlePatrolRemoval(Entity ent)
		{
			if (!GameContext.World.Has<Patrol>(ent)) return false;

			var patrol = GameContext.World.Get<Patrol>(ent);
			var destVec = patrol.GoingToB ? patrol.PointB : patrol.PointA;
			if (!GameContext.World.Has<MoveTo>(ent)) GameContext.World.Add(ent, new MoveTo(destVec));
			return true;
		}

		private void HandleMoveToRemoval(Entity ent)
		{
			if (!GameContext.World.Has<MoveTo>(ent)) return;

			GameContext.World.Remove<MoveTo>(ent);
			if (GameContext.World.Has<Velocity>(ent)) GameContext.World.Set(ent, new Velocity(Vector3.Zero));
		}

		private void ProcessChaseActions()
		{
			foreach (var (attacker, targetPos) in _tickActionsToChase)
			{
				if (!GameContext.World.IsAlive(attacker) || !GameContext.World.Has<AttackTarget>(attacker)) continue;

				bool needsRetarget = true;
				if (GameContext.World.Has<MoveTo>(attacker))
				{
					var existingTarget = GameContext.World.Get<MoveTo>(attacker).Target;
					if (Distance(existingTarget, targetPos) < ChaseRetargetDistance) needsRetarget = false;
				}

				if (needsRetarget) GameContext.World.SetOrAdd(attacker, new MoveTo(targetPos));
			}
		}

		private void ProcessStopChasingActions()
		{
			foreach (var attacker in _tickActionsToStopChasing)
			{
				if (!GameContext.World.IsAlive(attacker)) continue;

				if (GameContext.World.Has<MoveTo>(attacker)) GameContext.World.Remove<MoveTo>(attacker);
				if (GameContext.World.Has<Velocity>(attacker)) GameContext.World.Set(attacker, new Velocity(Vector3.Zero));
			}
		}

		private void CombatQueryAction(Entity entity, ref Position pos, ref Attack atk, ref AttackTarget target, ref Owner owner)
		{
			if (!GameContext.World.IsAlive(target.Target) || GameContext.World.Has<Dead>(target.Target) || GameContext.World.Has<Invulnerable>(target.Target))
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
			var targetPos = GameContext.World.Get<Position>(target.Target).Value;
			float effectiveRange = GetEffectiveRange(entity, target.Target, atk.Range, out bool isMelee, out float meleeVerticalReach);

			bool withinRange = isMelee
				? IsWithinMeleeRange(currentPos, targetPos, effectiveRange, meleeVerticalReach)
				: Distance(currentPos, targetPos) <= effectiveRange;

			if (withinRange)
			{
				ClearChaseTracking(entity);
				_tickActionsToStopChasing.Add(entity);

				if (atk.CurrentCooldown > 0) return true;

				if (GameContext.World.Has<Invulnerable>(target.Target))
				{
					atk.CurrentCooldown = (_unlimitedPowerProvider?.Invoke() == true) ? 0f : GetEffectiveAttackCooldown(entity, atk.Cooldown);
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

			atk.CurrentCooldown = (_unlimitedPowerProvider?.Invoke() == true) ? 0f : GetEffectiveAttackCooldown(entity, atk.Cooldown);

			HandleProjectile(entity, target, currentPos, targetPos, atk.Range);
		}

		private float ResolveAttackDamage(Entity entity, Entity target, ref Attack atk, out float finalDamage)
		{
			var armorComp = GameContext.World.Has<Armor>(target) ? GameContext.World.Get<Armor>(target) : new Armor(0f);
			float baseDamage = ECSContext.StatService != null ? ECSContext.StatService.GetStatValue(entity, new StatId("Attack")) : 0f;
			if (baseDamage <= 0) baseDamage = atk.Damage;

			float flatArmor = ECSContext.StatService != null ? ECSContext.StatService.GetStatValue(target, new StatId("Armor")) : 0f;
			if (flatArmor <= 0) flatArmor = armorComp.FlatArmor;

			var damageResult = CombatResolver.ResolveDamage(
				baseDamage: baseDamage, damageVariance: atk.DamageVariance,
				critChance: atk.CritChance, critMultiplier: atk.CritMultiplier,
				flatArmor: flatArmor, ratedArmor: armorComp.RatedArmor,
				flatArmorPen: atk.FlatArmorPenetration, percentArmorPen: atk.PercentArmorPenetration,
				damageType: atk.DamageType, armorType: armorComp.ArmorType, config: _combatConfig
			);

			finalDamage = damageResult.FinalDamage;
			return baseDamage;
		}

		private void ApplyDamageAndEffects(Entity entity, Entity target, float damage, float baseDamage, ref Attack atk, Vector3 targetPos, Owner owner)
		{
			if (GameContext.World.Has<LastAttacker>(target)) GameContext.World.Set(target, new LastAttacker(entity));
			else _tickActionsToAddLastAttacker.Add((target, entity));

			OnUnitAttackedCallback?.Invoke(entity, target);
			OnUnitDamagedCallback?.Invoke(target, entity, damage);

			var targetHealth = GameContext.World.Get<Health>(target);
			float newHp = Math.Max(0, targetHealth.Current - damage);
			targetHealth.Current = newHp;
			targetHealth.TimeSinceLastDamage = 0f;
			GameContext.World.Set(target, targetHealth);

			if (atk.SplashType != SplashType.None && atk.SplashOuterRadius > 0f)
				EvaluateSplashDamage(entity, target, targetPos, baseDamage, atk, owner);

			CheckCombatAlert(target);
			AcquireNewTargetFromAttacker(entity, target);

			if (newHp <= 0) _tickUnitsToKill.Add(target);
			else OnDamageFlashRequested?.Invoke(target);
		}

		private void CheckCombatAlert(Entity target)
		{
			if (!GameContext.World.Has<DefinitionId>(target)) return;

			int targetPlayer = GameContext.World.Has<UnitOwnerPlayer>(target) ? GameContext.World.Get<UnitOwnerPlayer>(target).PlayerIndex : 0;
			string targetUnitId = GameContext.World.Get<DefinitionId>(target).Value;
			if (GetCombatAlertTimer() <= 0f)
			{
				SetCombatAlertTimer(UnderAttackAlertCooldown);
				OnUnderAttackAlertRequested?.Invoke(targetPlayer, targetUnitId);
			}
		}

		private void AcquireNewTargetFromAttacker(Entity entity, Entity target)
		{
			if (!GameContext.World.IsAlive(target) || GameContext.World.Has<Dead>(target) || GameContext.World.Has<AttackTarget>(target)) return;
			if (!GameContext.World.Has<Attack>(target)) return;

			bool hasMoveTo = GameContext.World.Has<MoveTo>(target);
			if (!hasMoveTo || GameContext.World.Has<AttackMove>(target))
			{
				if (IsFlying(entity) && !CanAttackTarget(target, entity)) return;
				_tickNewAttackTargets.Add((target, new AttackTarget(entity)));
			}
		}

		private void HandleProjectile(Entity entity, Entity target, Vector3 currentPos, Vector3 targetPos, float range)
		{
			if (range <= 3f) return;

			string? weaponId = null;
			if (GameContext.World.Has<Inventory>(entity))
			{
				var inv = GameContext.World.Get<Inventory>(entity);
				for (int itemIndex = 0; itemIndex < inv.Count; itemIndex++)
				{
					string? item = inv.GetItemId(itemIndex);
					if (item == null) continue;
					if (GameContext.RegistryService?.ItemRegistry != null && GameContext.RegistryService.ItemRegistry.TryGetValue(item, out var itemMeta) && itemMeta?.GrantedWeapons != null && itemMeta.GrantedWeapons.Length > 0)
					{
						weaponId = itemMeta.GrantedWeapons[0];
						break;
					}
				}
			}

			if (weaponId == null && GameContext.World.Has<DefinitionId>(entity))
			{
				var defId = GameContext.World.Get<DefinitionId>(entity).Value;
				var weapons = UnitWeaponsProvider?.Invoke(defId);
				weaponId = (weapons != null && weapons.Length > 0) ? weapons[0] : defId;
			}

			OnWeaponProjectileRequested?.Invoke(currentPos, targetPos, weaponId, target, GetPlayerIndexForEntity(entity));
		}

		private void HandleOutOfRangeAction(Entity entity, Entity target, Vector3 currentPos, Vector3 targetPos, float effectiveRange, bool isMelee, float meleeVerticalReach)
		{
			if (GameContext.World.Has<Building>(entity))
			{
				_tickActionsToRemoveTarget.Add(entity);
				ClearChaseTracking(entity);
			}
			else if (!GameContext.World.Has<HoldPosition>(entity) && GameContext.World.Has<Movable>(entity))
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
			int attackerPlayer = GameContext.World.Has<UnitOwnerPlayer>(attacker) ? GameContext.World.Get<UnitOwnerPlayer>(attacker).PlayerIndex : 0;

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

			GameContext.World.Query(in splashQuery, (Entity splashEnt, ref Position sPos, ref Health sHp, ref Owner sOwner) =>
			{
				if (splashEnt == primaryTarget || splashEnt == attacker) return;
				if (GameContext.World.Has<Invulnerable>(splashEnt)) return;

				if (!atk.FriendlyFire)
				{
					int splashPlayer = GameContext.World.Has<UnitOwnerPlayer>(splashEnt) ? GameContext.World.Get<UnitOwnerPlayer>(splashEnt).PlayerIndex : 0;
					if (!NetworkService.ArePlayerIndicesEnemies(attackerPlayer, splashPlayer) || sOwner.PlayerEntity == attackerOwner.PlayerEntity) return;
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
			if (!GameContext.World.IsAlive(splashEnt) || GameContext.World.Has<Dead>(splashEnt) || !GameContext.World.Has<Health>(splashEnt)) return;
			if (GameContext.World.Has<Invulnerable>(splashEnt)) return;

			Owner sOwner = GameContext.World.Has<Owner>(splashEnt) ? GameContext.World.Get<Owner>(splashEnt) : default;

			if (!_splashAtk.FriendlyFire)
			{
				int splashPlayer = GameContext.World.Has<UnitOwnerPlayer>(splashEnt) ? GameContext.World.Get<UnitOwnerPlayer>(splashEnt).PlayerIndex : 0;
				if (!NetworkService.ArePlayerIndicesEnemies(_splashAttackerPlayer, splashPlayer) || sOwner.PlayerEntity == _splashAttackerOwner.PlayerEntity) return;
			}

			float distToImpact = Distance(_splashImpactPos, entry.Position);
			float splashRatio = CombatResolver.CalculateSplashRatio(
				distToImpact, _splashAtk.SplashType,
				_splashAtk.SplashInnerRadius, _splashAtk.SplashMediumRadius, _splashAtk.SplashOuterRadius,
				_splashAtk.SplashInnerRatio, _splashAtk.SplashMediumRatio, _splashAtk.SplashOuterRatio
			);

			if (splashRatio > 0f)
			{
				ref var sHp = ref GameContext.World.Get<Health>(splashEnt);
				ApplySplashDamage(_splashAttacker, splashEnt, splashRatio, _splashBaseDamage, ref _splashAtk, ref sHp);
			}
		}

		private void ApplySplashDamage(Entity attacker, Entity splashEnt, float splashRatio, float baseDamage, ref Attack atk, ref Health sHp)
		{
			var sArmorComp = GameContext.World.Has<Armor>(splashEnt) ? GameContext.World.Get<Armor>(splashEnt) : new Armor(0f);
			float sFlatArmor = ECSContext.StatService != null ? ECSContext.StatService.GetStatValue(splashEnt, new StatId("Armor")) : 0f;
			if (sFlatArmor <= 0) sFlatArmor = sArmorComp.FlatArmor;

			var sResult = CombatResolver.ResolveDamage(
				baseDamage: baseDamage * splashRatio, damageVariance: atk.DamageVariance,
				critChance: atk.CritChance, critMultiplier: atk.CritMultiplier,
				flatArmor: sFlatArmor, ratedArmor: sArmorComp.RatedArmor,
				flatArmorPen: atk.FlatArmorPenetration, percentArmorPen: atk.PercentArmorPenetration,
				damageType: atk.DamageType, armorType: sArmorComp.ArmorType, config: _combatConfig
			);

			float splashDamage = sResult.FinalDamage;
			sHp.Current = Math.Max(0, sHp.Current - splashDamage);
			sHp.TimeSinceLastDamage = 0f;
			GameContext.World.Set(splashEnt, sHp);

			OnUnitDamagedCallback?.Invoke(splashEnt, attacker, splashDamage);

			if (GameContext.World.Has<LastAttacker>(splashEnt)) GameContext.World.Set(splashEnt, new LastAttacker(attacker));
			else _tickActionsToAddLastAttacker.Add((splashEnt, attacker));

			if (sHp.Current <= 0) _tickUnitsToKill.Add(splashEnt);
			else OnDamageFlashRequested?.Invoke(splashEnt);
		}

		private float GetEffectiveAttackCooldown(Entity entity, float baseCooldown)
		{
			if (GameContext.World.Has<AttackSpeed>(entity))
			{
				float multiplier = GameContext.World.Get<AttackSpeed>(entity).Multiplier;
				if (multiplier > 0f)
				{
					return baseCooldown / multiplier;
				}
			}

			return baseCooldown;
		}

		private float GetTargetCollisionRadius(Entity target)
		{
			if (!GameContext.World.Has<CollisionRadius>(target)) return 0f;

			float radius = GameContext.World.Get<CollisionRadius>(target).Value;
			if (GameContext.World.Has<CollisionScale>(target))
			{
				radius *= GameContext.World.Get<CollisionScale>(target).Value;
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
			float scale = GameContext.World.Has<CollisionScale>(entity) ? GameContext.World.Get<CollisionScale>(entity).Value : 1.0f;
			float authoredRadius = GameContext.World.Has<CollisionRadius>(entity) ? GameContext.World.Get<CollisionRadius>(entity).Value : 0f;
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
			int includeFlags = GameContext.World.Has<PathingFlags>(attacker) ? GameContext.World.Get<PathingFlags>(attacker).Value : (int)TerrainPathingFlags.Ground;
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

			if (GameContext.NavMeshPathfinder == null)
			{
				return IsVerticallyReachableForRange(attackerIsFlying, attackerPos, targetPos, effectiveRange, isMelee, meleeVerticalReach);
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
			var threadPathfinder = _threadLocalPathfinder.Value ?? GameContext.NavMeshPathfinder;

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
			if (terrainEntity == Entity.Null || !GameContext.World.Has<TerrainState>(terrainEntity)) return false;

			ref var ts = ref GameContext.World.Get<TerrainState>(terrainEntity);
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
			GameContext.World.Query(in _healingExecutionQuery, _healingExecutionQueryDelegate);

			ProcessHealRemovals();
			ProcessHealChases();
			ProcessHealStopChasing();
		}

		private void ProcessHealRemovals()
		{
			foreach (var ent in _tickHealRemoveTargets)
			{
				if (GameContext.World.IsAlive(ent) && GameContext.World.Has<HealingTarget>(ent))
				{
					GameContext.World.Remove<HealingTarget>(ent);
				}
			}
		}

		private void ProcessHealChases()
		{
			foreach (var (healer, targetPos) in _tickHealChaseTargets)
			{
				if (!GameContext.World.IsAlive(healer)) continue;

				bool needsRetarget = true;
				if (GameContext.World.Has<MoveTo>(healer))
				{
					var existingTarget = GameContext.World.Get<MoveTo>(healer).Target;
					if (Distance(existingTarget, targetPos) < ChaseRetargetDistance) needsRetarget = false;
				}

				if (needsRetarget) GameContext.World.SetOrAdd(healer, new MoveTo(targetPos));
			}
		}

		private void ProcessHealStopChasing()
		{
			foreach (var healer in _tickHealStopChasing)
			{
				if (!GameContext.World.IsAlive(healer)) continue;

				if (GameContext.World.Has<MoveTo>(healer)) GameContext.World.Remove<MoveTo>(healer);
				if (GameContext.World.Has<Velocity>(healer)) GameContext.World.Set(healer, new Velocity(Vector3.Zero));
			}
		}

		private void HealingExecutionQueryAction(Entity entity, ref Position pos, ref Attack atk, ref HealingTarget target, ref Owner owner)
		{
			if (!IsValidHealingTarget(entity, target.Target, out var targetHealth)) return;

			if (HandleHealingDistance(entity, pos.Value, target.Target, atk.Range)) return;

			_tickHealStopChasing.Add(entity);

			if (atk.CurrentCooldown > 0) return;

			float healAmount = atk.Damage;
			ApplyHealing(target.Target, healAmount, targetHealth);

			atk.CurrentCooldown = GetHealCooldown(entity, atk.Cooldown);

			InvokeHealEvents(entity, pos.Value, target.Target, healAmount);
		}

		private bool IsValidHealingTarget(Entity entity, Entity targetEntity, out Health targetHealth)
		{
			targetHealth = default;
			if (!GameContext.World.IsAlive(targetEntity) || GameContext.World.Has<Dead>(targetEntity))
			{
				_tickHealRemoveTargets.Add(entity);
				return false;
			}

			targetHealth = GameContext.World.Get<Health>(targetEntity);
			if (targetHealth.Current >= targetHealth.Max)
			{
				_tickHealRemoveTargets.Add(entity);
				return false;
			}

			return true;
		}

		private void ApplyHealing(Entity targetEntity, float healAmount, Health targetHealth)
		{
			float newHp = Math.Min(targetHealth.Max, targetHealth.Current + healAmount);
			GameContext.World.Set(targetEntity, new Health(newHp, targetHealth.Max));
		}

		private float GetHealCooldown(Entity entity, float baseCooldown)
		{
			if (_unlimitedPowerProvider?.Invoke() == true) return 0f;
			return GetEffectiveAttackCooldown(entity, baseCooldown);
		}

		private void InvokeHealEvents(Entity entity, Vector3 pos, Entity targetEntity, float healAmount)
		{
			OnHealEffectRequested?.Invoke(pos, GameContext.World.Get<Position>(targetEntity).Value);
			OnHealFlashRequested?.Invoke(targetEntity);
			OnUnitHealedCallback?.Invoke(targetEntity, entity, healAmount);
		}

		private bool HandleHealingDistance(Entity entity, Vector3 currentPos, Entity targetEntity, float atkRange)
		{
			var targetPos = GameContext.World.Get<Position>(targetEntity).Value;
			float effectiveRange = Math.Max(atkRange + GetTargetCollisionRadius(targetEntity), GetMinimumMeleeContactReach(entity, targetEntity));

			if (Distance(currentPos, targetPos) <= effectiveRange) return false;

			if (!GameContext.World.Has<HoldPosition>(entity) && GameContext.World.Has<Movable>(entity))
			{
				_tickHealChaseTargets.Add((entity, targetPos));
			}

			return true;
		}

		public void DealSpellDamageAOE(Vector3 position, float radius, float damage, Entity casterEntity, bool enemyOnly = true)
		{
			_aoeKillList.Clear();
			_aoeLastAttackerList.Clear();

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
				GameContext.World.Query(in query, (Entity entity, ref Position pos, ref Health hp) => { ProcessAoeTarget(entity, ref pos, ref hp, position, radius, damage, casterEntity, enemyOnly); });
			}

			ProcessAoeLastAttackers();
			ProcessAoeKillList();
		}

		private void OnAoeDamageCandidateFound(in MovementAndPathfindingService.SpatialGridEntry entry)
		{
			Entity entity = entry.Entity;
			if (!GameContext.World.IsAlive(entity) || GameContext.World.Has<Dead>(entity) || !GameContext.World.Has<Health>(entity)) return;

			ref var hp = ref GameContext.World.Get<Health>(entity);
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
			if (GameContext.World.Has<Invulnerable>(entity)) return false;

			if (enemyOnly)
			{
				int casterPlayer = GetPlayerIndexForEntity(casterEntity);
				int targetPlayer = GetPlayerIndexForEntity(entity);
				if (!NetworkService.ArePlayerIndicesEnemies(casterPlayer, targetPlayer)) return false;
			}

			if (Vector3.Distance(entityPos, aoePosition) > aoeRadius) return false;

			return true;
		}

		private void UpdateAoeLastAttacker(Entity entity, Entity casterEntity)
		{
			if (casterEntity != Entity.Null && GameContext.World.IsAlive(casterEntity))
			{
				if (GameContext.World.Has<LastAttacker>(entity)) GameContext.World.Set(entity, new LastAttacker(casterEntity));
				else _aoeLastAttackerList.Add((entity, casterEntity));
			}
		}

		private void ApplyAoeDamage(Entity entity, ref Health hp, float damage, Entity casterEntity)
		{
			float newHp = Math.Max(0, hp.Current - damage);
			hp.Current = newHp;

			OnUnitDamagedCallback?.Invoke(entity, casterEntity, damage);

			if (newHp <= 0) _aoeKillList.Add(entity);
			else OnDamageFlashRequested?.Invoke(entity);
		}

		private void ProcessAoeLastAttackers()
		{
			foreach (var (targetEnt, attackerEnt) in _aoeLastAttackerList)
			{
				if (GameContext.World.IsAlive(targetEnt))
				{
					GameContext.World.AddOrGet(targetEnt, new LastAttacker(attackerEnt)) = new LastAttacker(attackerEnt);
				}
			}
		}

		private void ProcessAoeKillList()
		{
			foreach (var entity in _aoeKillList)
			{
				if (!GameContext.World.IsAlive(entity) || GameContext.World.Has<Dead>(entity)) continue;

				GameContext.World.Add<Dead>(entity);
				OnKillUnitRequested?.Invoke(entity);
			}
		}

		public void HealAOE(Vector3 position, float radius, float healAmount, Entity casterEntity = default)
		{
			int casterPlayer = GetPlayerIndexForEntity(casterEntity);

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
			GameContext.World.Query(in query, (Entity entity, ref Position pos, ref Health hp) =>
			{
				int targetPlayer = GetPlayerIndexForEntity(entity);
				if (NetworkService.ArePlayerIndicesEnemies(casterPlayer, targetPlayer)) return;

				if (Vector3.Distance(pos.Value, position) <= radius)
				{
					float newHp = Math.Min(hp.Max, hp.Current + healAmount);
					hp.Current = newHp;
					OnHealFlashRequested?.Invoke(entity);
					OnUnitHealedCallback?.Invoke(entity, casterEntity, healAmount);
				}
			});
		}

		private void OnAoeHealCandidateFound(in MovementAndPathfindingService.SpatialGridEntry entry)
		{
			Entity entity = entry.Entity;
			if (!GameContext.World.IsAlive(entity) || GameContext.World.Has<Dead>(entity) || !GameContext.World.Has<Health>(entity)) return;

			int targetPlayer = GetPlayerIndexForEntity(entity);
			if (NetworkService.ArePlayerIndicesEnemies(_healCasterPlayer, targetPlayer)) return;

			if (Vector3.Distance(entry.Position, _healPosition) <= _healRadius)
			{
				ref var hp = ref GameContext.World.Get<Health>(entity);
				float newHp = Math.Min(hp.Max, hp.Current + _healAmount);
				hp.Current = newHp;
				OnHealFlashRequested?.Invoke(entity);
				OnUnitHealedCallback?.Invoke(entity, _healCasterEntity, _healAmount);
			}
		}
	}
}
