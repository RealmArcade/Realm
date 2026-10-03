using Arch.Core;
using Realm.Ecs.Services;
using Realm.Ecs.Common;
using Realm.Ecs.Components.Core;
using Realm.Ecs.Components.Combat;
using Realm.Ecs.Components.Movement;
using Realm.Ecs.Components.Tags;
using Realm.Ecs.Components.Terrain;
using System;
using System.Collections.Generic;
using System.Linq;

internal class CombatAndDamageService
{
	private readonly WorldAccessor EcsWorldAccessor;
	private World EcsWorld => EcsWorldAccessor.Current;
	private readonly StatService? _statService;
	private readonly Func<bool>? _unlimitedPowerProvider;
	private readonly NavMeshPathfinder? _pathfinder;

	private const float UnderAttackAlertCooldown = 8f;

	// Vertical terrain delta counts toward range so units at the foot of a cliff are not
	// "in range" of a tower on the summit (combat is 3D, not a top-down projection).
	private static float Distance(System.Numerics.Vector3 a, System.Numerics.Vector3 b)
	{
		return (a - b).Length();
	}

	private bool IsFlying(Entity entity)
	{
		return EcsWorld.Has<PathingFlags>(entity)
			&& ((TerrainPathingFlags)EcsWorld.Get<PathingFlags>(entity).Value & TerrainPathingFlags.Flying) != 0;
	}

	private bool CanAttackTarget(Entity attacker, Entity target)
	{
		if (!IsFlying(target))
		{
			return true;
		}
		if (!EcsWorld.Has<CombatTargeting>(attacker))
		{
			return true;
		}
		return EcsWorld.Get<CombatTargeting>(attacker).CanTargetAir;
	}

	private System.Numerics.Vector3 _scanAttackerPos;
	private PlayerEntity _scanAttackerOwner;
	private bool _scanIsAttackerEnemy;
	private bool _scanAttackerIsGroundMelee;
	private Entity _scanAttacker;
	private float _scanMaxDistSq;
	private readonly ScanCandidate[] _scanCandidates = new ScanCandidate[MaxAcquisitionCandidates];
	private int _scanCandidateCount;

	private System.Numerics.Vector3 _scanPriestPos;
	private PlayerEntity _scanFriendlyOwner;
	private float _scanFriendlyClosestDist;
	private Entity _scanClosestDamagedFriendly;

	private readonly List<(Entity Attacker, AttackTarget Target)> _tickNewAttackTargets = new();
	private readonly List<(Entity Target, Entity Attacker)> _tickActionsToAddLastAttacker = new();
	private readonly List<Entity> _tickActionsToRemoveTarget = new();
	private readonly List<(Entity Attacker, System.Numerics.Vector3 TargetPos)> _tickActionsToChase = new();
	private readonly List<Entity> _tickActionsToStopChasing = new();
	private readonly List<Entity> _tickUnitsToKill = new();
	private readonly List<(Entity Priest, HealingTarget Target)> _tickNewHealingTargets = new();
	private readonly List<Entity> _tickHealRemoveTargets = new();
	private readonly List<(Entity Priest, System.Numerics.Vector3 TargetPos)> _tickHealChaseTargets = new();
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
	private readonly Dictionary<Entity, (Entity Target, float Remaining)> _abandonedTargetCooldown = new();

	// Route reachability is cached per (attacker, target) pair so the real navmesh route is
	// NOT recomputed every combat tick. The cache is invalidated only when geometry changes:
	// the attacker moved a meaningful amount toward/away from the target, the target moved,
	// or the navmesh was rebuilt. Reachable verdicts expire after RouteCacheMaxAge so chasers
	// re-evaluate as the battlefield shifts; unreachable verdicts are deliberately sticky and
	// are never re-poked on a time basis while the geometry is unchanged.
	private const float RouteCacheAttackerMoveLimit = 8.0f;
	private const float RouteCacheTargetMoveLimit = 4.0f;
	private const float RouteReachMargin = 1.5f;
	private const float RouteCacheMaxAge = 1.5f;
	private readonly Dictionary<(Entity Attacker, Entity Target), RouteReachabilityEntry> _routeReachabilityCache = new();

	private readonly struct RouteReachabilityEntry
	{
		public readonly bool Reachable;
		public readonly System.Numerics.Vector3 AttackerPos;
		public readonly System.Numerics.Vector3 TargetPos;
		public readonly DotRecast.Detour.DtNavMeshQuery? Query;
		public readonly float Timestamp;

		public RouteReachabilityEntry(bool reachable, System.Numerics.Vector3 attackerPos, System.Numerics.Vector3 targetPos, DotRecast.Detour.DtNavMeshQuery? query, float timestamp)
		{
			Reachable = reachable;
			AttackerPos = attackerPos;
			TargetPos = targetPos;
			Query = query;
			Timestamp = timestamp;
		}
	}

	// Acquisition considers the few closest enemies, not just the single closest one.
	// The closest candidate can be unreachable (a flyer at altitude, a unit on a plateau) and
	// must not block the unit from engaging the next-reachable enemy standing behind it.
	private const int MaxAcquisitionCandidates = 4;

	private readonly struct ScanCandidate
	{
		public readonly Entity Enemy;
		public readonly float DistSq;

		public ScanCandidate(Entity enemy, float distSq)
		{
			Enemy = enemy;
			DistSq = distSq;
		}
	}

	private readonly QueryDescription _enemyQuery = QueryCache.AllPositionAndOwnerNoneDeadQuery;
	private readonly QueryDescription _friendlyScanQuery = QueryCache.AllPositionAndHealthAndOwnerNoneDeadQuery;
	private readonly QueryDescription _targetAcquisitionQuery = QueryCache.AllPositionAndAttackAndOwnerNoneAttackTargetAndDeadQuery;
	private readonly QueryDescription _combatQuery = QueryCache.AllPositionAndAttackAndAttackTargetAndOwnerNoneDeadQuery;
	private readonly QueryDescription _priestScanQuery = QueryCache.AllPositionAndOwnerAndDefinitionIdNoneDeadAndHealingTargetQuery;
	private readonly QueryDescription _healingExecutionQuery = QueryCache.AllPositionAndAttackAndHealingTargetAndOwnerNoneDeadQuery;

	private ForEachWithEntity<Position, Attack, Owner> _targetAcquisitionQueryDelegate;
	private ForEachWithEntity<Position, Owner> _potentialEnemyQueryDelegate;
	private ForEachWithEntity<Position, Attack, AttackTarget, Owner> _combatQueryDelegate;
	private ForEachWithEntity<Position, Owner, DefinitionId> _priestScanQueryDelegate;
	private ForEachWithEntity<Position, Health, Owner> _friendlyScanQueryDelegate;
	private ForEachWithEntity<Position, Attack, HealingTarget, Owner> _healingExecutionQueryDelegate;

	private readonly List<Entity> _tickExpiredAbandonedCooldowns = new();
	private readonly List<(Entity Attacker, Entity Target)> _staleRouteReachabilityEntries = new();
	private readonly List<Entity> _aoeKillList = new();
	private readonly List<(Entity Target, Entity Attacker)> _aoeLastAttackerList = new();

	public Action<System.Numerics.Vector3, System.Numerics.Vector3>? OnArrowProjectileRequested;
	public Action<System.Numerics.Vector3, System.Numerics.Vector3, string?, Entity>? OnWeaponProjectileRequested;
	public Func<string, string[]?>? UnitWeaponsProvider;
	public Action<Entity>? OnDamageFlashRequested;
	public Action<System.Numerics.Vector3, System.Numerics.Vector3>? OnHealEffectRequested;
	public Action<Entity>? OnHealFlashRequested;
	public Action<Entity, Entity, float>? OnUnitDamagedCallback;
	public Action<Entity, Entity, float>? OnUnitHealedCallback;
	public Action<Entity, Entity>? OnUnitAttackedCallback;
	public Action<string>? OnUnderAttackAlertRequested;
	public Action<Entity>? OnKillUnitRequested;

	public CombatAndDamageService(WorldAccessor ecsWorldAccessor, Func<bool>? unlimitedPowerProvider = null, NavMeshPathfinder? pathfinder = null)
	{
		EcsWorldAccessor = ecsWorldAccessor;
		_unlimitedPowerProvider = unlimitedPowerProvider;
		_statService = ServiceLocator.TryGet<StatService>();
		_pathfinder = pathfinder ?? ServiceLocator.TryGet<NavMeshPathfinder>();
		_targetAcquisitionQueryDelegate = TargetAcquisitionQueryAction;
		_potentialEnemyQueryDelegate = ScanEnemyQueryAction;
		_combatQueryDelegate = CombatQueryAction;
		_priestScanQueryDelegate = PriestScanQueryAction;
		_friendlyScanQueryDelegate = ScanFriendlyQueryAction;
		_healingExecutionQueryDelegate = HealingExecutionQueryAction;
	}

	public void StepCombat(float delta)
	{
		_combatDelta = delta;
		_combatTotalTime += delta;
		TickCombatAlertTimer(delta);
		TickAbandonedTargetCooldowns(delta);
		PruneRouteReachabilityCache();

		ProcessTargetAcquisition();
		ProcessCombatTicks();
		ProcessHealingTicks();
	}

	private void PruneRouteReachabilityCache()
	{
		if (_routeReachabilityCache.Count == 0) return;
		_staleRouteReachabilityEntries.Clear();
		foreach (var kvp in _routeReachabilityCache)
		{
			if (!EcsWorld.IsAlive(kvp.Key.Attacker) || !EcsWorld.IsAlive(kvp.Key.Target))
			{
				_staleRouteReachabilityEntries.Add(kvp.Key);
			}
		}
		for (int i = 0; i < _staleRouteReachabilityEntries.Count; i++)
		{
			_routeReachabilityCache.Remove(_staleRouteReachabilityEntries[i]);
		}
	}

	private void TickAbandonedTargetCooldowns(float delta)
	{
		if (_abandonedTargetCooldown.Count == 0) return;
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
			_abandonedTargetCooldown.Remove(_tickExpiredAbandonedCooldowns[i]);
		}
	}

	private Entity FindWorldEntity()
	{
		Entity worldEntity = Entity.Null;
		var query = Realm.Ecs.Common.QueryCache.AllWorldStateQuery;
		EcsWorld.Query(in query, (Entity entity) => worldEntity = entity);
		return worldEntity;
	}

	private Entity FindTerrainEntity()
	{
		Entity worldEntity = Entity.Null;
		var query = Realm.Ecs.Common.QueryCache.AllTerrainStateQuery;
		EcsWorld.Query(in query, (Entity entity) => worldEntity = entity);
		return worldEntity;
	}

	private int GetTimeOfDayIndex()
	{
		var worldEntity = FindWorldEntity();
		if (worldEntity != Entity.Null && EcsWorld.Has<WorldState>(worldEntity))
		{
			return EcsWorld.Get<WorldState>(worldEntity).TimeOfDayIndex;
		}
		return 0;
	}

	private float GetCombatAlertTimer()
	{
		Entity worldEntity = Entity.Null;
		var query = Realm.Ecs.Common.QueryCache.AllCombatAlertStateQuery;
		EcsWorld.Query(in query, (Entity entity) => worldEntity = entity);

		if (worldEntity != Entity.Null && EcsWorld.Has<CombatAlertState>(worldEntity))
		{
			return EcsWorld.Get<CombatAlertState>(worldEntity).UnderAttackAlertTimer;
		}
		return 0f;
	}

	private void SetCombatAlertTimer(float value)
	{
		Entity worldEntity = Entity.Null;
		var query = Realm.Ecs.Common.QueryCache.AllCombatAlertStateQuery;
		EcsWorld.Query(in query, (Entity entity) => worldEntity = entity);

		if (worldEntity != Entity.Null && EcsWorld.Has<CombatAlertState>(worldEntity))
		{
			ref var state = ref EcsWorld.Get<CombatAlertState>(worldEntity);
			state.UnderAttackAlertTimer = value;
		}
	}

	private void TickCombatAlertTimer(float fDelta)
	{
		Entity worldEntity = Entity.Null;
		var query = Realm.Ecs.Common.QueryCache.AllCombatAlertStateQuery;
		EcsWorld.Query(in query, (Entity entity) => worldEntity = entity);

		if (worldEntity != Entity.Null && EcsWorld.Has<CombatAlertState>(worldEntity))
		{
			ref var state = ref EcsWorld.Get<CombatAlertState>(worldEntity);
			if (state.UnderAttackAlertTimer > 0f)
			{
				state.UnderAttackAlertTimer = Math.Max(0f, state.UnderAttackAlertTimer - fDelta);
			}
		}
	}

	private void ProcessTargetAcquisition()
	{
		_tickNewAttackTargets.Clear();
		EcsWorld.Query(in _targetAcquisitionQuery, _targetAcquisitionQueryDelegate);
		foreach (var (attacker, target) in _tickNewAttackTargets)
		{
			if (EcsWorld.IsAlive(attacker))
			{
				EcsWorld.SetOrAdd(attacker, target);
			}
		}
	}

	private void TargetAcquisitionQueryAction(Entity entity, ref Position pos, ref Attack atk, ref Owner owner)
	{
		if (EcsWorld.Has<DefinitionId>(entity) && EcsWorld.Get<DefinitionId>(entity).Value == "priest")
		{
			return;
		}

		bool isAttackMove = EcsWorld.Has<Realm.Ecs.Components.Movement.AttackMove>(entity);
		bool isPatrol     = EcsWorld.Has<Patrol>(entity);
		bool isIdle = !EcsWorld.Has<MoveTo>(entity) && !isAttackMove;

		if (isIdle || isAttackMove || isPatrol)
		{
			float scanRadius = EcsWorld.Has<ScanRadius>(entity)
				? EcsWorld.Get<ScanRadius>(entity).Value
				: 15.0f;

			if (GetTimeOfDayIndex() == 2)
			{
				scanRadius *= 0.7f;
			}

			_scanAttackerPos = pos.Value;
			_scanAttackerOwner = owner.PlayerEntity;
			_scanAttacker = entity;
			_scanIsAttackerEnemy = false;
			if (EcsWorld.Has<UnitFaction>(entity))
			{
				_scanIsAttackerEnemy = EcsWorld.Get<UnitFaction>(entity).IsEnemy;
			}
			// A ground-pathing melee attacker can never hit a unit hovering at flight
			// altitude, so flyers are excluded from its scan entirely (they used to become
			// the closest candidate and block acquisition for every reachable enemy).
			_scanAttackerIsGroundMelee = !IsFlying(entity) && atk.Range <= MeleeRangeThreshold;
			_scanMaxDistSq = scanRadius * scanRadius;
			_scanCandidateCount = 0;

			EcsWorld.Query(in _enemyQuery, _potentialEnemyQueryDelegate);

			// Walk the scan candidates in distance order and commit to the closest one that
			// is not abandoned and is truly reachable. A single unreachable nearest enemy
			// (plateau mount, hovering flyer) must not make the unit ignore everyone else.
			for (int i = 0; i < _scanCandidateCount; i++)
			{
				var candidate = _scanCandidates[i];
				if (candidate.Enemy == Entity.Null) continue;
				if (IsTargetStillAbandoned(entity, candidate.Enemy)) continue;
				if (!EcsWorld.Has<Position>(candidate.Enemy)) continue;

				var enemyPos = EcsWorld.Get<Position>(candidate.Enemy).Value;
				float effectiveRange = GetEffectiveRange(entity, candidate.Enemy, atk.Range, out bool isMelee, out float meleeVerticalReach);
				if (!IsReachableWithinRange(entity, candidate.Enemy, pos.Value, enemyPos, effectiveRange, isMelee, meleeVerticalReach))
				{
					continue;
				}

				_tickNewAttackTargets.Add((entity, new AttackTarget(candidate.Enemy)));
				break;
			}
		}
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
		if (_abandonedTargetCooldown.ContainsKey(attacker))
		{
			_abandonedTargetCooldown.Remove(attacker);
		}
		_abandonedTargetCooldown[attacker] = (target, cooldownSeconds);
	}

	private void ClearChaseTracking(Entity entity)
	{
		_chaseStuckTime.Remove(entity);
		_lastChaseDist.Remove(entity);
	}

	// A ground unit scans the nearest few enemies in distance order and only engages the
	// closest one that is reachable. Flying units are excluded for ground-melee attackers.
	private void ScanEnemyQueryAction(Entity potentialEnemy, ref Position enemyPos, ref Owner enemyOwner)
	{
		if (enemyOwner.PlayerEntity != _scanAttackerOwner)
		{
			bool isEnemyEntity = EcsWorld.Has<UnitFaction>(potentialEnemy) && EcsWorld.Get<UnitFaction>(potentialEnemy).IsEnemy;
			if (isEnemyEntity != _scanIsAttackerEnemy)
			{
				if (IsFlying(potentialEnemy) && (_scanAttackerIsGroundMelee || !CanAttackTarget(_scanAttacker, potentialEnemy)))
				{
					return;
				}
				float distSq = (_scanAttackerPos - enemyPos.Value).LengthSquared();
				if (distSq < _scanMaxDistSq)
				{
					InsertScanCandidate(potentialEnemy, distSq);
				}
			}
		}
	}

	private void InsertScanCandidate(Entity enemy, float distSq)
	{
		int n = _scanCandidateCount;

		if (n >= MaxAcquisitionCandidates && distSq >= _scanCandidates[MaxAcquisitionCandidates - 1].DistSq)
		{
			return;
		}
		int i = n < MaxAcquisitionCandidates ? n : MaxAcquisitionCandidates - 1;
		while (i > 0 && _scanCandidates[i - 1].DistSq > distSq)
		{
			_scanCandidates[i] = _scanCandidates[i - 1];
			i--;
		}
		_scanCandidates[i] = new ScanCandidate(enemy, distSq);
		if (n < MaxAcquisitionCandidates)
		{
			_scanCandidateCount = n + 1;
		}
	}

	private void ProcessCombatTicks()
	{
		_tickActionsToAddLastAttacker.Clear();
		_tickActionsToRemoveTarget.Clear();
		_tickActionsToChase.Clear();
		_tickActionsToStopChasing.Clear();
		_tickUnitsToKill.Clear();

		EcsWorld.Query(in _combatQuery, _combatQueryDelegate);

		foreach (var (targetEnt, attackerEnt) in _tickActionsToAddLastAttacker)
		{
			if (EcsWorld.IsAlive(targetEnt))
			{
				// Several attackers can hit the same target in the same tick, so the target may
				// appear here more than once. AddOrGet overwrites instead of re-adding the
				// component (re-adding would move the entity to its own archetype and trip
				// Arch's structural-change assertion in Debug builds).
				EcsWorld.AddOrGet(targetEnt, new LastAttacker(attackerEnt)) = new LastAttacker(attackerEnt);
			}
		}

		foreach (var targetEntity in _tickUnitsToKill)
		{
			ClearChaseTracking(targetEntity);
			_abandonedTargetCooldown.Remove(targetEntity);
			if (EcsWorld.IsAlive(targetEntity))
			{
				if (!EcsWorld.Has<Dead>(targetEntity))
				{
					EcsWorld.Add<Dead>(targetEntity);
					OnKillUnitRequested?.Invoke(targetEntity);
				}
			}
		}

		foreach (var ent in _tickActionsToRemoveTarget)
		{
			ClearChaseTracking(ent);
			if (EcsWorld.IsAlive(ent))
			{
				if (EcsWorld.Has<AttackTarget>(ent))
				{
					EcsWorld.Remove<AttackTarget>(ent);
				}

				if (EcsWorld.Has<Realm.Ecs.Components.Movement.AttackMove>(ent))
				{
					var am = EcsWorld.Get<Realm.Ecs.Components.Movement.AttackMove>(ent);
					if (!EcsWorld.Has<MoveTo>(ent))
						EcsWorld.Add(ent, new MoveTo(am.Target));
				}
				else if (EcsWorld.Has<Patrol>(ent))
				{
					var patrol = EcsWorld.Get<Patrol>(ent);
					var destVec = patrol.GoingToB ? patrol.PointB : patrol.PointA;
					if (!EcsWorld.Has<MoveTo>(ent))
						EcsWorld.Add(ent, new MoveTo(destVec));
				}
				else if (EcsWorld.Has<MoveTo>(ent))
				{
					// Plain unit (no AttackMove/Patrol): the MoveTo, if present, is a chase
					// order pointing at the dead/escaped target's position. Clear it (and the
					// leftover velocity) so the unit re-acquires immediately instead of
					// marching to the corpse while enemies walk past it.
					EcsWorld.Remove<MoveTo>(ent);
					if (EcsWorld.Has<Velocity>(ent))
					{
						EcsWorld.Set(ent, new Velocity(System.Numerics.Vector3.Zero));
					}
				}
			}
		}

		foreach (var (attacker, targetPos) in _tickActionsToChase)
		{
			if (EcsWorld.IsAlive(attacker) && EcsWorld.Has<AttackTarget>(attacker))
			{
				// Do not re-push a chase destination every combat frame. Re-planning the
				// whole navmesh path per frame made chasers jitter and wander off the flat
				// path; only refresh when the target has actually moved a meaningful amount.
				bool needsRetarget = true;
				if (EcsWorld.Has<MoveTo>(attacker))
				{
					var existingTarget = EcsWorld.Get<MoveTo>(attacker).Target;
					if (Distance(existingTarget, targetPos) < ChaseRetargetDistance)
					{
						needsRetarget = false;
					}
				}
				if (!needsRetarget) continue;

				var moveTo = new MoveTo(targetPos);
				EcsWorld.SetOrAdd(attacker, moveTo);
			}
		}

		foreach (var attacker in _tickActionsToStopChasing)
		{
			if (EcsWorld.IsAlive(attacker))
			{
				if (EcsWorld.Has<MoveTo>(attacker))
				{
					EcsWorld.Remove<MoveTo>(attacker);
				}
				if (EcsWorld.Has<Velocity>(attacker))
				{
					EcsWorld.Set(attacker, new Velocity(System.Numerics.Vector3.Zero));
				}
			}
		}
	}

	private void CombatQueryAction(Entity entity, ref Position pos, ref Attack atk, ref AttackTarget target, ref Owner owner)
	{
		if (!EcsWorld.IsAlive(target.Target) || EcsWorld.Has<Dead>(target.Target))
		{
			_tickActionsToRemoveTarget.Add(entity);
			return;
		}

		if (IsFlying(target.Target) && !CanAttackTarget(entity, target.Target))
		{
			_tickActionsToRemoveTarget.Add(entity);
			return;
		}

		var targetPosComp = EcsWorld.Get<Position>(target.Target);
		var currentPos = pos.Value;
		var targetPos = targetPosComp.Value;

		float effectiveRange = GetEffectiveRange(entity, target.Target, atk.Range, out bool isMelee, out float meleeVerticalReach);

		bool withinRange = false;
		if (isMelee)
		{
			float horizontalDist = new System.Numerics.Vector2(currentPos.X - targetPos.X, currentPos.Z - targetPos.Z).Length();
			float verticalDist = Math.Abs(currentPos.Y - targetPos.Y);
			withinRange = horizontalDist <= effectiveRange && verticalDist <= meleeVerticalReach;
		}
		else
		{
			withinRange = Distance(currentPos, targetPos) <= effectiveRange;
		}

		if (withinRange)
		{
			ClearChaseTracking(entity);
			_tickActionsToStopChasing.Add(entity);

			if (atk.CurrentCooldown <= 0)
			{
				if (EcsWorld.Has<Realm.Ecs.Components.Tags.Invulnerable>(target.Target))
				{
					atk.CurrentCooldown = (_unlimitedPowerProvider?.Invoke() == true) ? 0f : GetEffectiveAttackCooldown(entity, atk.Cooldown);
					return;
				}

				var targetHealth = EcsWorld.Get<Health>(target.Target);

				float actualDamage = _statService != null ? _statService.GetStatValue(entity, new Realm.Ecs.Common.StatId("Attack")) : 0f;
				if (actualDamage <= 0) actualDamage = atk.Damage;

				float actualArmor = _statService != null ? _statService.GetStatValue(target.Target, new Realm.Ecs.Common.StatId("Armor")) : 0f;
				if (actualArmor <= 0 && EcsWorld.Has<Armor>(target.Target)) actualArmor = EcsWorld.Get<Armor>(target.Target).Value;

				float damage = actualDamage - actualArmor;
				if (damage < 1f) damage = 1f;

				if (EcsWorld.Has<LastAttacker>(target.Target))
				{
					EcsWorld.Set(target.Target, new LastAttacker(entity));
				}
				else
				{
					_tickActionsToAddLastAttacker.Add((target.Target, entity));
				}

				OnUnitAttackedCallback?.Invoke(entity, target.Target);
				OnUnitDamagedCallback?.Invoke(target.Target, entity, damage);

				float newHp = Math.Max(0, targetHealth.Current - damage);
				EcsWorld.Set(target.Target, new Health(newHp, targetHealth.Max));

				if (EcsWorld.Has<DefinitionId>(target.Target))
				{
					string targetUnitId = EcsWorld.Get<DefinitionId>(target.Target).Value;
					bool targetIsEnemy = EcsWorld.Has<UnitFaction>(target.Target) && EcsWorld.Get<UnitFaction>(target.Target).IsEnemy;
					if (!targetIsEnemy)
					{
						float currentTimer = GetCombatAlertTimer();
						if (currentTimer <= 0f)
						{
							SetCombatAlertTimer(UnderAttackAlertCooldown);
							OnUnderAttackAlertRequested?.Invoke(targetUnitId);
						}
					}
				}

				if (EcsWorld.IsAlive(target.Target) && !EcsWorld.Has<Dead>(target.Target) && !EcsWorld.Has<AttackTarget>(target.Target))
				{
					if (EcsWorld.Has<Attack>(target.Target))
					{
						bool hasMoveTo = EcsWorld.Has<MoveTo>(target.Target);
						if (!hasMoveTo || EcsWorld.Has<Realm.Ecs.Components.Movement.AttackMove>(target.Target))
						{
							if (IsFlying(entity) && !CanAttackTarget(target.Target, entity))
							{
								return;
							}
							_tickNewAttackTargets.Add((target.Target, new AttackTarget(entity)));
						}
					}
				}

				atk.CurrentCooldown = (_unlimitedPowerProvider?.Invoke() == true) ? 0f : GetEffectiveAttackCooldown(entity, atk.Cooldown);

				if (atk.Range > 3f)
				{
					string? weaponId = null;
					if (EcsWorld.Has<DefinitionId>(entity))
					{
						var defId = EcsWorld.Get<DefinitionId>(entity).Value;
						var weapons = UnitWeaponsProvider?.Invoke(defId);
						if (weapons != null && weapons.Length > 0)
						{
							weaponId = weapons[0];
						}
						else
						{
							weaponId = defId;
						}
					}
					OnWeaponProjectileRequested?.Invoke(currentPos, targetPos, weaponId, target.Target);
				}

				if (newHp <= 0)
				{
					_tickUnitsToKill.Add(target.Target);
				}
				else
				{
					OnDamageFlashRequested?.Invoke(target.Target);
				}
			}
		}
		else
		{
			if (EcsWorld.Has<Building>(entity))
			{
				_tickActionsToRemoveTarget.Add(entity);
				ClearChaseTracking(entity);
			}
			else if (!EcsWorld.Has<Realm.Ecs.Components.Movement.HoldPosition>(entity) && EcsWorld.Has<Realm.Ecs.Components.Tags.Movable>(entity))
			{
				// Do not let a ground unit mill around at the foot of a cliff trying to
				// reach a target that sits above its effective range (mountain-top towers,
				// targets on stepped terrain). Step 1: real navmesh route must exist AND
				// bring the attacker within range. Step 2: if it ever cannot, drop it
				// immediately and let it resume its order instead of poke-walk-turn-back looping.
				// The route cache makes the verdict stick while geometry is unchanged, so the
				// unit does not re-acquire the unreachable target on a later combat frame.
				if (!IsReachableWithinRange(entity, target.Target, currentPos, targetPos, effectiveRange, isMelee, meleeVerticalReach))
				{
					RegisterAbandonedTarget(entity, target.Target);
					_tickActionsToRemoveTarget.Add(entity);
					ClearChaseTracking(entity);
					return;
				}
				_tickActionsToChase.Add((entity, targetPos));
				float dist = Distance(currentPos, targetPos);
				UpdateChaseTracking(entity, dist, target.Target);
			}
			else
			{
				_tickActionsToRemoveTarget.Add(entity);
				ClearChaseTracking(entity);
			}
		}
	}

	private float GetEffectiveAttackCooldown(Entity entity, float baseCooldown)
	{
		if (EcsWorld.Has<AttackSpeed>(entity))
		{
			float multiplier = EcsWorld.Get<AttackSpeed>(entity).Multiplier;
			if (multiplier > 0f)
			{
				return baseCooldown / multiplier;
			}
		}
		return baseCooldown;
	}

	private float GetTargetCollisionRadius(Entity target)
	{
		if (!EcsWorld.Has<CollisionRadius>(target)) return 0f;

		float radius = EcsWorld.Get<CollisionRadius>(target).Value;
		if (EcsWorld.Has<CollisionScale>(target))
		{
			radius *= EcsWorld.Get<CollisionScale>(target).Value;
		}
		return radius;
	}

	/// <summary>
	///     Physical contact radius used by the movement separation system: the authored
	///     CollisionRadius scaled by CollisionScale, or the shared default fraction of the
	///     scale when the unit has no authored radius. Combat uses the same value so melee
	///     reach is consistent with how close units can actually get to each other.
	/// </summary>
	private float GetUnitContactRadius(Entity entity)
	{
		float scale = EcsWorld.Has<CollisionScale>(entity) ? EcsWorld.Get<CollisionScale>(entity).Value : 1.0f;
		if (EcsWorld.Has<CollisionRadius>(entity))
		{
			return EcsWorld.Get<CollisionRadius>(entity).Value * scale;
		}
		return scale * Realm.Ecs.Common.GameplayConstants.DefaultCollisionRadius;
	}

	/// <summary>
	///     Minimum distance the movement separation enforces between two units' contact
	///     radii, pushed apart at CollisionSeparationFactor, plus a small safety margin so
	///     steering/clump variance never leaves a pair hovering just out of reach.
	/// </summary>
	private float GetMinimumMeleeContactReach(Entity a, Entity b)
	{
		return (GetUnitContactRadius(a) + GetUnitContactRadius(b))
			* Realm.Ecs.Common.GameplayConstants.CollisionSeparationFactor * MeleeContactReachSafetyMargin;
	}

	/// <summary>
	///     Effective distance at which an attacker can land a hit on the target. Ranged
	///     attacks use range plus both collision radii. Melee attacks also need to close to
	///     the separation distance the movement system enforces between the two bodies;
	///     otherwise big-scaled units push each other apart forever without ever reaching
	///     attack range.
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
	// The result is cached per (attacker, target). There is NO time-based retry: the verdict is
	// recomputed only when the attacker or target moved meaningfully or the navmesh was rebuilt,
	// so an unreachable mountain tower is never re-poked every few seconds.
	private bool IsReachableWithinRange(Entity attacker, Entity target, System.Numerics.Vector3 attackerPos, System.Numerics.Vector3 targetPos, float effectiveRange, bool isMelee, float meleeVerticalReach)
	{
		if (IsFlying(attacker) || !isMelee)
		{
			// Ranged attackers (like towers) shoot through the air and do not need a navmesh path.
			return Distance(attackerPos, targetPos) <= effectiveRange + RouteReachMargin;
		}
		if (_pathfinder == null)
		{
			// No pathfinder available (e.g. headless unit tests) — fall back to the vertical
			// shortcut so behaviour is unchanged.
			return IsVerticallyReachableForRange(attacker, attackerPos, targetPos, effectiveRange, isMelee, meleeVerticalReach);
		}

		Entity terrainEntity = FindTerrainEntity();
		if (terrainEntity == Entity.Null || !EcsWorld.Has<TerrainState>(terrainEntity))
		{
			return IsVerticallyReachableForRange(attacker, attackerPos, targetPos, effectiveRange, isMelee, meleeVerticalReach);
		}
		ref var ts = ref EcsWorld.Get<TerrainState>(terrainEntity);
		if (ts.NavMeshQuery == null)
		{
			return IsVerticallyReachableForRange(attacker, attackerPos, targetPos, effectiveRange, isMelee, meleeVerticalReach);
		}

		var key = (attacker, target);
		if (_routeReachabilityCache.TryGetValue(key, out var cached)
			&& cached.Query == ts.NavMeshQuery
			&& Distance(cached.AttackerPos, attackerPos) <= RouteCacheAttackerMoveLimit
			&& Distance(cached.TargetPos, targetPos) <= RouteCacheTargetMoveLimit
			&& (!cached.Reachable || _combatTotalTime - cached.Timestamp <= RouteCacheMaxAge))
		{
			return cached.Reachable;
		}

		int includeFlags = EcsWorld.Has<PathingFlags>(attacker)
			? EcsWorld.Get<PathingFlags>(attacker).Value
			: (int)TerrainPathingFlags.Ground;

		PathFollow pf = default;
		_pathfinder.ComputePath(ts.NavMeshQuery, attackerPos, targetPos, (ushort)includeFlags, ref pf);

		bool reachable = pf.HasValidCorridor && pf.WaypointCount > 0;
		if (reachable)
		{
			var last = pf.Waypoints[pf.WaypointCount - 1];
			if (isMelee)
			{
				float horiz = new System.Numerics.Vector2(last.X - targetPos.X, last.Z - targetPos.Z).Length();
				float vert = Math.Abs(last.Y - targetPos.Y);
				// effectiveRange already includes both collision radii (plus the movement
				// separation contact distance for melee), so it represents the physical
				// distance the attacker can close to. Comparing the route endpoint against the
				// FULL effectiveRange (no padding subtraction) keeps targets reachable at
				// walls and large structures; previously the padding shortfall made the
				// verdict flip to "unreachable" and combat dropped the target, leaving the
				// melee fighter frozen and idle instead of attacking.
				reachable = horiz <= Math.Max(0.5f, effectiveRange) && vert <= meleeVerticalReach;
			}
			else
			{
				reachable = Distance(last, targetPos) <= effectiveRange + RouteReachMargin;
			}
		}

		_routeReachabilityCache[key] = new RouteReachabilityEntry(reachable, attackerPos, targetPos, ts.NavMeshQuery, _combatTotalTime);
		return reachable;
	}

	private bool IsVerticallyReachableForRange(Entity attacker, System.Numerics.Vector3 attackerPos, System.Numerics.Vector3 targetPos, float effectiveRange, bool isMelee, float meleeVerticalReach)
	{
		if (IsFlying(attacker))
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
		_tickNewHealingTargets.Clear();
		EcsWorld.Query(in _priestScanQuery, _priestScanQueryDelegate);
		foreach (var (priest, target) in _tickNewHealingTargets)
		{
			if (EcsWorld.IsAlive(priest))
			{
				EcsWorld.SetOrAdd(priest, target);
			}
		}

		_tickHealRemoveTargets.Clear();
		_tickHealChaseTargets.Clear();
		_tickHealStopChasing.Clear();

		EcsWorld.Query(in _healingExecutionQuery, _healingExecutionQueryDelegate);

		foreach (var ent in _tickHealRemoveTargets)
		{
			if (EcsWorld.IsAlive(ent) && EcsWorld.Has<HealingTarget>(ent))
			{
				EcsWorld.Remove<HealingTarget>(ent);
			}
		}

		foreach (var (priest, targetPos) in _tickHealChaseTargets)
		{
			if (EcsWorld.IsAlive(priest))
			{
				bool needsRetarget = true;
				if (EcsWorld.Has<MoveTo>(priest))
				{
					var existingTarget = EcsWorld.Get<MoveTo>(priest).Target;
					if (Distance(existingTarget, targetPos) < ChaseRetargetDistance)
					{
						needsRetarget = false;
					}
				}
				if (!needsRetarget) continue;

				var moveTo = new MoveTo(targetPos);
				EcsWorld.SetOrAdd(priest, moveTo);
			}
		}

		foreach (var priest in _tickHealStopChasing)
		{
			if (EcsWorld.IsAlive(priest))
			{
				if (EcsWorld.Has<MoveTo>(priest)) EcsWorld.Remove<MoveTo>(priest);
				if (EcsWorld.Has<Velocity>(priest))
				{
					EcsWorld.Set(priest, new Velocity(System.Numerics.Vector3.Zero));
				}
			}
		}
	}

	private void ScanFriendlyQueryAction(Entity potentialFriendly, ref Position fPosComp, ref Health fHealth, ref Owner fOwner)
	{
		if (fOwner.PlayerEntity == _scanFriendlyOwner && fHealth.Current < fHealth.Max)
		{
			float dist = Distance(_scanPriestPos, fPosComp.Value);
			if (dist < _scanFriendlyClosestDist)
			{
				_scanFriendlyClosestDist = dist;
				_scanClosestDamagedFriendly = potentialFriendly;
			}
		}
	}

	private void PriestScanQueryAction(Entity entity, ref Position pos, ref Owner owner, ref DefinitionId defId)
	{
		if (defId.Value == "priest")
		{
			bool isIdle = !EcsWorld.Has<MoveTo>(entity);
			if (isIdle)
			{
				_scanClosestDamagedFriendly = Entity.Null;
				_scanFriendlyClosestDist = 15.0f;
				_scanPriestPos = pos.Value;
				_scanFriendlyOwner = owner.PlayerEntity;

				EcsWorld.Query(in _friendlyScanQuery, _friendlyScanQueryDelegate);

				if (_scanClosestDamagedFriendly != Entity.Null)
				{
					_tickNewHealingTargets.Add((entity, new HealingTarget(_scanClosestDamagedFriendly)));
				}
			}
		}
	}

	private void HealingExecutionQueryAction(Entity entity, ref Position pos, ref Attack atk, ref HealingTarget target, ref Owner owner)
	{
		if (!EcsWorld.IsAlive(target.Target) || EcsWorld.Has<Dead>(target.Target))
		{
			_tickHealRemoveTargets.Add(entity);
			return;
		}

		var targetHealth = EcsWorld.Get<Health>(target.Target);
		if (targetHealth.Current >= targetHealth.Max)
		{
			_tickHealRemoveTargets.Add(entity);
			return;
		}

		var targetPosComp = EcsWorld.Get<Position>(target.Target);
		var currentPos = pos.Value;
		var targetPos = targetPosComp.Value;

		float dist = Distance(currentPos, targetPos);
		// A heal must be able to reach the same contact distance the movement separation
		// enforces, otherwise a big-scaled damaged unit is permanently out of heal range.
		float effectiveRange = atk.Range + GetTargetCollisionRadius(target.Target);
		float minimumContactReach = GetMinimumMeleeContactReach(entity, target.Target);
		if (effectiveRange < minimumContactReach)
		{
			effectiveRange = minimumContactReach;
		}
		if (dist <= effectiveRange)
		{
			_tickHealStopChasing.Add(entity);

			if (atk.CurrentCooldown <= 0)
			{
				float healAmount = atk.Damage;
				float newHp = Math.Min(targetHealth.Max, targetHealth.Current + healAmount);
				EcsWorld.Set(target.Target, new Health(newHp, targetHealth.Max));

				atk.CurrentCooldown = (_unlimitedPowerProvider?.Invoke() == true) ? 0f : GetEffectiveAttackCooldown(entity, atk.Cooldown);

				OnHealEffectRequested?.Invoke(currentPos, targetPos);
				OnHealFlashRequested?.Invoke(target.Target);
				OnUnitHealedCallback?.Invoke(target.Target, entity, healAmount);
			}
		}
		else
		{
			if (!EcsWorld.Has<Realm.Ecs.Components.Movement.HoldPosition>(entity) && EcsWorld.Has<Realm.Ecs.Components.Tags.Movable>(entity))
			{
				_tickHealChaseTargets.Add((entity, targetPos));
			}
		}
	}

	public void DealSpellDamageAOE(System.Numerics.Vector3 position, float radius, float damage, Entity casterEntity, bool enemyOnly = true)
	{
		var query = Realm.Ecs.Common.QueryCache.AllPositionAndHealthNoneDeadQuery;
		_aoeKillList.Clear();
		_aoeLastAttackerList.Clear();
		EcsWorld.Query(in query, (Entity entity, ref Position pos, ref Health hp) =>
		{
			if (EcsWorld.Has<Realm.Ecs.Components.Tags.Invulnerable>(entity)) return;

			if (enemyOnly)
			{
				bool isEnemy = EcsWorld.Has<UnitFaction>(entity) && EcsWorld.Get<UnitFaction>(entity).IsEnemy;
				if (!isEnemy) return;
			}

			if (System.Numerics.Vector3.Distance(pos.Value, position) <= radius)
			{
				if (casterEntity != Entity.Null && EcsWorld.IsAlive(casterEntity))
				{
					if (EcsWorld.Has<LastAttacker>(entity))
					{
						EcsWorld.Set(entity, new LastAttacker(casterEntity));
					}
					else
					{
						_aoeLastAttackerList.Add((entity, casterEntity));
					}
				}

				float newHp = Math.Max(0, hp.Current - damage);
				hp.Current = newHp;

				OnUnitDamagedCallback?.Invoke(entity, casterEntity, damage);

				if (newHp <= 0)
				{
					_aoeKillList.Add(entity);
				}
				else
				{
					OnDamageFlashRequested?.Invoke(entity);
				}
			}
		});

		foreach (var (targetEnt, attackerEnt) in _aoeLastAttackerList)
		{
			if (EcsWorld.IsAlive(targetEnt))
			{
				// AddOrGet so a target that just received LastAttacker from another AOE this
				// frame is overwritten instead of re-adding the component.
				EcsWorld.AddOrGet(targetEnt, new LastAttacker(attackerEnt)) = new LastAttacker(attackerEnt);
			}
		}

		foreach (var entity in _aoeKillList)
		{
			if (EcsWorld.IsAlive(entity) && !EcsWorld.Has<Dead>(entity))
			{
				EcsWorld.Add<Dead>(entity);
				OnKillUnitRequested?.Invoke(entity);
			}
		}
	}

	public void HealAOE(System.Numerics.Vector3 position, float radius, float healAmount)
	{
		var query = Realm.Ecs.Common.QueryCache.AllPositionAndHealthNoneDeadQuery;
		EcsWorld.Query(in query, (Entity entity, ref Position pos, ref Health hp) =>
		{
			bool isEnemy = EcsWorld.Has<UnitFaction>(entity) && EcsWorld.Get<UnitFaction>(entity).IsEnemy;
			if (isEnemy) return;

			if (System.Numerics.Vector3.Distance(pos.Value, position) <= radius)
			{
				float newHp = Math.Min(hp.Max, hp.Current + healAmount);
				hp.Current = newHp;
				OnHealFlashRequested?.Invoke(entity);
				OnUnitHealedCallback?.Invoke(entity, Entity.Null, healAmount);
			}
		});
	}
}
