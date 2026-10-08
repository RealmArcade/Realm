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
	private readonly CombatConfig _combatConfig = new();
	public CombatConfig Config => _combatConfig;

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
		if (EcsWorld.Has<DefinitionId>(entity) && EcsWorld.Get<DefinitionId>(entity).Value == "priest") return;

		bool isAttackMove = EcsWorld.Has<Realm.Ecs.Components.Movement.AttackMove>(entity);
		bool isPatrol     = EcsWorld.Has<Patrol>(entity);
		bool isIdle = !EcsWorld.Has<MoveTo>(entity) && !isAttackMove;

		if (!isIdle && !isAttackMove && !isPatrol) return;

		float scanRadius = EcsWorld.Has<ScanRadius>(entity) ? EcsWorld.Get<ScanRadius>(entity).Value : 15.0f;
		if (GetTimeOfDayIndex() == 2) scanRadius *= 0.7f;

		_scanAttackerPos = pos.Value;
		_scanAttackerOwner = owner.PlayerEntity;
		_scanAttacker = entity;
		_scanIsAttackerEnemy = EcsWorld.Has<UnitFaction>(entity) && EcsWorld.Get<UnitFaction>(entity).IsEnemy;

		// A ground-pathing melee attacker can never hit a unit hovering at flight
		// altitude, so flyers are excluded from its scan entirely.
		_scanAttackerIsGroundMelee = !IsFlying(entity) && atk.Range <= MeleeRangeThreshold;
		_scanMaxDistSq = scanRadius * scanRadius;
		_scanCandidateCount = 0;

		EcsWorld.Query(in _enemyQuery, _potentialEnemyQueryDelegate);
		FindAndSetBestReachableCandidate(entity, ref pos, ref atk);
	}

	private void FindAndSetBestReachableCandidate(Entity entity, ref Position pos, ref Attack atk)
	{
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

		ProcessLastAttackers();
		ProcessUnitsToKill();
		ProcessTargetRemovals();
		ProcessChaseActions();
		ProcessStopChasingActions();
	}

	private void ProcessLastAttackers()
	{
		foreach (var (targetEnt, attackerEnt) in _tickActionsToAddLastAttacker)
		{
			if (EcsWorld.IsAlive(targetEnt))
			{
				EcsWorld.AddOrGet(targetEnt, new LastAttacker(attackerEnt)) = new LastAttacker(attackerEnt);
			}
		}
	}

	private void ProcessUnitsToKill()
	{
		foreach (var targetEntity in _tickUnitsToKill)
		{
			ClearChaseTracking(targetEntity);
			_abandonedTargetCooldown.Remove(targetEntity);
			if (!EcsWorld.IsAlive(targetEntity) || EcsWorld.Has<Dead>(targetEntity)) continue;

			EcsWorld.Add<Dead>(targetEntity);
			OnKillUnitRequested?.Invoke(targetEntity);
		}
	}

	private void ProcessTargetRemovals()
	{
		foreach (var ent in _tickActionsToRemoveTarget)
		{
			ClearChaseTracking(ent);
			if (!EcsWorld.IsAlive(ent)) continue;

			if (EcsWorld.Has<AttackTarget>(ent)) EcsWorld.Remove<AttackTarget>(ent);

			if (EcsWorld.Has<Realm.Ecs.Components.Movement.AttackMove>(ent))
			{
				var am = EcsWorld.Get<Realm.Ecs.Components.Movement.AttackMove>(ent);
				if (!EcsWorld.Has<MoveTo>(ent)) EcsWorld.Add(ent, new MoveTo(am.Target));
			}
			else if (EcsWorld.Has<Patrol>(ent))
			{
				var patrol = EcsWorld.Get<Patrol>(ent);
				var destVec = patrol.GoingToB ? patrol.PointB : patrol.PointA;
				if (!EcsWorld.Has<MoveTo>(ent)) EcsWorld.Add(ent, new MoveTo(destVec));
			}
			else if (EcsWorld.Has<MoveTo>(ent))
			{
				EcsWorld.Remove<MoveTo>(ent);
				if (EcsWorld.Has<Velocity>(ent)) EcsWorld.Set(ent, new Velocity(System.Numerics.Vector3.Zero));
			}
		}
	}

	private void ProcessChaseActions()
	{
		foreach (var (attacker, targetPos) in _tickActionsToChase)
		{
			if (!EcsWorld.IsAlive(attacker) || !EcsWorld.Has<AttackTarget>(attacker)) continue;

			bool needsRetarget = true;
			if (EcsWorld.Has<MoveTo>(attacker))
			{
				var existingTarget = EcsWorld.Get<MoveTo>(attacker).Target;
				if (Distance(existingTarget, targetPos) < ChaseRetargetDistance) needsRetarget = false;
			}
			
			if (needsRetarget) EcsWorld.SetOrAdd(attacker, new MoveTo(targetPos));
		}
	}

	private void ProcessStopChasingActions()
	{
		foreach (var attacker in _tickActionsToStopChasing)
		{
			if (!EcsWorld.IsAlive(attacker)) continue;

			if (EcsWorld.Has<MoveTo>(attacker)) EcsWorld.Remove<MoveTo>(attacker);
			if (EcsWorld.Has<Velocity>(attacker)) EcsWorld.Set(attacker, new Velocity(System.Numerics.Vector3.Zero));
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

		var currentPos = pos.Value;
		var targetPos = EcsWorld.Get<Position>(target.Target).Value;
		float effectiveRange = GetEffectiveRange(entity, target.Target, atk.Range, out bool isMelee, out float meleeVerticalReach);

		bool withinRange = isMelee 
			? IsWithinMeleeRange(currentPos, targetPos, effectiveRange, meleeVerticalReach) 
			: Distance(currentPos, targetPos) <= effectiveRange;

		if (withinRange)
		{
			ClearChaseTracking(entity);
			_tickActionsToStopChasing.Add(entity);

			if (atk.CurrentCooldown > 0) return;
			
			if (EcsWorld.Has<Realm.Ecs.Components.Tags.Invulnerable>(target.Target))
			{
				atk.CurrentCooldown = (_unlimitedPowerProvider?.Invoke() == true) ? 0f : GetEffectiveAttackCooldown(entity, atk.Cooldown);
				return;
			}

			ExecuteAttack(entity, target.Target, ref atk, currentPos, targetPos, owner);
		}
		else
		{
			HandleOutOfRangeAction(entity, target.Target, currentPos, targetPos, effectiveRange, isMelee, meleeVerticalReach);
		}
	}

	private bool IsWithinMeleeRange(System.Numerics.Vector3 currentPos, System.Numerics.Vector3 targetPos, float effectiveRange, float meleeVerticalReach)
	{
		float horizontalDist = new System.Numerics.Vector2(currentPos.X - targetPos.X, currentPos.Z - targetPos.Z).Length();
		float verticalDist = Math.Abs(currentPos.Y - targetPos.Y);
		return horizontalDist <= effectiveRange && verticalDist <= meleeVerticalReach;
	}

	private void ExecuteAttack(Entity entity, Entity target, ref Attack atk, System.Numerics.Vector3 currentPos, System.Numerics.Vector3 targetPos, Owner owner)
	{
		float baseDamage = ResolveAttackDamage(entity, target, ref atk, out float finalDamage);
		ApplyDamageAndEffects(entity, target, finalDamage, baseDamage, ref atk, targetPos, owner);
		
		atk.CurrentCooldown = (_unlimitedPowerProvider?.Invoke() == true) ? 0f : GetEffectiveAttackCooldown(entity, atk.Cooldown);
		
		HandleProjectile(entity, target, currentPos, targetPos, atk.Range);
	}

	private float ResolveAttackDamage(Entity entity, Entity target, ref Attack atk, out float finalDamage)
	{
		var armorComp = EcsWorld.Has<Armor>(target) ? EcsWorld.Get<Armor>(target) : new Armor(0f);
		float baseDamage = _statService != null ? _statService.GetStatValue(entity, new Realm.Ecs.Common.StatId("Attack")) : 0f;
		if (baseDamage <= 0) baseDamage = atk.Damage;

		float flatArmor = _statService != null ? _statService.GetStatValue(target, new Realm.Ecs.Common.StatId("Armor")) : 0f;
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

	private void ApplyDamageAndEffects(Entity entity, Entity target, float damage, float baseDamage, ref Attack atk, System.Numerics.Vector3 targetPos, Owner owner)
	{
		if (EcsWorld.Has<LastAttacker>(target)) EcsWorld.Set(target, new LastAttacker(entity));
		else _tickActionsToAddLastAttacker.Add((target, entity));

		OnUnitAttackedCallback?.Invoke(entity, target);
		OnUnitDamagedCallback?.Invoke(target, entity, damage);

		var targetHealth = EcsWorld.Get<Health>(target);
		float newHp = Math.Max(0, targetHealth.Current - damage);
		targetHealth.Current = newHp;
		targetHealth.TimeSinceLastDamage = 0f;
		EcsWorld.Set(target, targetHealth);

		if (atk.SplashType != SplashType.None && atk.SplashOuterRadius > 0f)
			EvaluateSplashDamage(entity, target, targetPos, baseDamage, atk, owner);

		CheckCombatAlert(target);
		AcquireNewTargetFromAttacker(entity, target);

		if (newHp <= 0) _tickUnitsToKill.Add(target);
		else OnDamageFlashRequested?.Invoke(target);
	}

	private void CheckCombatAlert(Entity target)
	{
		if (!EcsWorld.Has<DefinitionId>(target)) return;
		
		string targetUnitId = EcsWorld.Get<DefinitionId>(target).Value;
		bool targetIsEnemy = EcsWorld.Has<UnitFaction>(target) && EcsWorld.Get<UnitFaction>(target).IsEnemy;
		
		if (targetIsEnemy) return;

		if (GetCombatAlertTimer() <= 0f)
		{
			SetCombatAlertTimer(UnderAttackAlertCooldown);
			OnUnderAttackAlertRequested?.Invoke(targetUnitId);
		}
	}

	private void AcquireNewTargetFromAttacker(Entity entity, Entity target)
	{
		if (!EcsWorld.IsAlive(target) || EcsWorld.Has<Dead>(target) || EcsWorld.Has<AttackTarget>(target)) return;
		if (!EcsWorld.Has<Attack>(target)) return;

		bool hasMoveTo = EcsWorld.Has<MoveTo>(target);
		if (!hasMoveTo || EcsWorld.Has<Realm.Ecs.Components.Movement.AttackMove>(target))
		{
			if (IsFlying(entity) && !CanAttackTarget(target, entity)) return;
			_tickNewAttackTargets.Add((target, new AttackTarget(entity)));
		}
	}

	private void HandleProjectile(Entity entity, Entity target, System.Numerics.Vector3 currentPos, System.Numerics.Vector3 targetPos, float range)
	{
		if (range <= 3f) return;

		string? weaponId = null;
		if (EcsWorld.Has<DefinitionId>(entity))
		{
			var defId = EcsWorld.Get<DefinitionId>(entity).Value;
			var weapons = UnitWeaponsProvider?.Invoke(defId);
			weaponId = (weapons != null && weapons.Length > 0) ? weapons[0] : defId;
		}
		OnWeaponProjectileRequested?.Invoke(currentPos, targetPos, weaponId, target);
	}

	private void HandleOutOfRangeAction(Entity entity, Entity target, System.Numerics.Vector3 currentPos, System.Numerics.Vector3 targetPos, float effectiveRange, bool isMelee, float meleeVerticalReach)
	{
		if (EcsWorld.Has<Building>(entity))
		{
			_tickActionsToRemoveTarget.Add(entity);
			ClearChaseTracking(entity);
		}
		else if (!EcsWorld.Has<Realm.Ecs.Components.Movement.HoldPosition>(entity) && EcsWorld.Has<Realm.Ecs.Components.Tags.Movable>(entity))
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

	private void EvaluateSplashDamage(Entity attacker, Entity primaryTarget, System.Numerics.Vector3 impactPos, float baseDamage, Attack atk, Owner attackerOwner)
	{
		bool attackerIsEnemy = EcsWorld.Has<UnitFaction>(attacker) && EcsWorld.Get<UnitFaction>(attacker).IsEnemy;
		var splashQuery = Realm.Ecs.Common.QueryCache.AllPositionAndHealthAndOwnerNoneDeadQuery;

		EcsWorld.Query(in splashQuery, (Entity splashEnt, ref Position sPos, ref Health sHp, ref Owner sOwner) =>
		{
			if (splashEnt == primaryTarget || splashEnt == attacker) return;
			if (EcsWorld.Has<Realm.Ecs.Components.Tags.Invulnerable>(splashEnt)) return;

			if (!atk.FriendlyFire)
			{
				bool splashIsEnemy = EcsWorld.Has<UnitFaction>(splashEnt) && EcsWorld.Get<UnitFaction>(splashEnt).IsEnemy;
				if (splashIsEnemy == attackerIsEnemy || sOwner.PlayerEntity == attackerOwner.PlayerEntity) return;
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

	private void ApplySplashDamage(Entity attacker, Entity splashEnt, float splashRatio, float baseDamage, ref Attack atk, ref Health sHp)
	{
		var sArmorComp = EcsWorld.Has<Armor>(splashEnt) ? EcsWorld.Get<Armor>(splashEnt) : new Armor(0f);
		float sFlatArmor = _statService != null ? _statService.GetStatValue(splashEnt, new Realm.Ecs.Common.StatId("Armor")) : 0f;
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
		EcsWorld.Set(splashEnt, sHp);

		OnUnitDamagedCallback?.Invoke(splashEnt, attacker, splashDamage);

		if (EcsWorld.Has<LastAttacker>(splashEnt)) EcsWorld.Set(splashEnt, new LastAttacker(attacker));
		else _tickActionsToAddLastAttacker.Add((splashEnt, attacker));

		if (sHp.Current <= 0) _tickUnitsToKill.Add(splashEnt);
		else OnDamageFlashRequested?.Invoke(splashEnt);
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
		if (IsRouteReachabilityCached(key, ts.NavMeshQuery, attackerPos, targetPos, out bool cachedResult))
		{
			return cachedResult;
		}

		int includeFlags = EcsWorld.Has<PathingFlags>(attacker) ? EcsWorld.Get<PathingFlags>(attacker).Value : (int)TerrainPathingFlags.Ground;
		PathFollow pf = default;
		_pathfinder.ComputePath(ts.NavMeshQuery, attackerPos, targetPos, (ushort)includeFlags, ref pf);

		bool reachable = pf.HasValidCorridor && pf.WaypointCount > 0;
		if (reachable)
		{
			var last = pf.Waypoints[pf.WaypointCount - 1];
			reachable = isMelee 
				? EvaluateMeleeReachability(last, targetPos, effectiveRange, meleeVerticalReach) 
				: Distance(last, targetPos) <= effectiveRange + RouteReachMargin;
		}

		_routeReachabilityCache[key] = new RouteReachabilityEntry(reachable, attackerPos, targetPos, ts.NavMeshQuery, _combatTotalTime);
		return reachable;
	}

	private bool IsRouteReachabilityCached((Entity, Entity) key, DotRecast.Detour.DtNavMeshQuery navMeshQuery, System.Numerics.Vector3 attackerPos, System.Numerics.Vector3 targetPos, out bool reachable)
	{
		reachable = false;
		if (!_routeReachabilityCache.TryGetValue(key, out var cached)) return false;
		if (cached.Query != navMeshQuery) return false;
		if (Distance(cached.AttackerPos, attackerPos) > RouteCacheAttackerMoveLimit) return false;
		if (Distance(cached.TargetPos, targetPos) > RouteCacheTargetMoveLimit) return false;
		if (cached.Reachable && _combatTotalTime - cached.Timestamp > RouteCacheMaxAge) return false;

		reachable = cached.Reachable;
		return true;
	}

	private bool EvaluateMeleeReachability(System.Numerics.Vector3 last, System.Numerics.Vector3 targetPos, float effectiveRange, float meleeVerticalReach)
	{
		float horiz = new System.Numerics.Vector2(last.X - targetPos.X, last.Z - targetPos.Z).Length();
		float vert = Math.Abs(last.Y - targetPos.Y);
		return horiz <= Math.Max(0.5f, effectiveRange) && vert <= meleeVerticalReach;
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
		ProcessNewHealingTargets();

		_tickHealRemoveTargets.Clear();
		_tickHealChaseTargets.Clear();
		_tickHealStopChasing.Clear();
		EcsWorld.Query(in _healingExecutionQuery, _healingExecutionQueryDelegate);

		ProcessHealRemovals();
		ProcessHealChases();
		ProcessHealStopChasing();
	}

	private void ProcessNewHealingTargets()
	{
		foreach (var (priest, target) in _tickNewHealingTargets)
		{
			if (EcsWorld.IsAlive(priest)) EcsWorld.SetOrAdd(priest, target);
		}
	}

	private void ProcessHealRemovals()
	{
		foreach (var ent in _tickHealRemoveTargets)
		{
			if (EcsWorld.IsAlive(ent) && EcsWorld.Has<HealingTarget>(ent))
			{
				EcsWorld.Remove<HealingTarget>(ent);
			}
		}
	}

	private void ProcessHealChases()
	{
		foreach (var (priest, targetPos) in _tickHealChaseTargets)
		{
			if (!EcsWorld.IsAlive(priest)) continue;

			bool needsRetarget = true;
			if (EcsWorld.Has<MoveTo>(priest))
			{
				var existingTarget = EcsWorld.Get<MoveTo>(priest).Target;
				if (Distance(existingTarget, targetPos) < ChaseRetargetDistance) needsRetarget = false;
			}
			
			if (needsRetarget) EcsWorld.SetOrAdd(priest, new MoveTo(targetPos));
		}
	}

	private void ProcessHealStopChasing()
	{
		foreach (var priest in _tickHealStopChasing)
		{
			if (!EcsWorld.IsAlive(priest)) continue;

			if (EcsWorld.Has<MoveTo>(priest)) EcsWorld.Remove<MoveTo>(priest);
			if (EcsWorld.Has<Velocity>(priest)) EcsWorld.Set(priest, new Velocity(System.Numerics.Vector3.Zero));
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

		var currentPos = pos.Value;
		var targetPos = EcsWorld.Get<Position>(target.Target).Value;

		float effectiveRange = Math.Max(atk.Range + GetTargetCollisionRadius(target.Target), GetMinimumMeleeContactReach(entity, target.Target));

		if (Distance(currentPos, targetPos) > effectiveRange)
		{
			if (!EcsWorld.Has<Realm.Ecs.Components.Movement.HoldPosition>(entity) && EcsWorld.Has<Realm.Ecs.Components.Tags.Movable>(entity))
			{
				_tickHealChaseTargets.Add((entity, targetPos));
			}
			return;
		}

		_tickHealStopChasing.Add(entity);

		if (atk.CurrentCooldown > 0) return;

		float healAmount = atk.Damage;
		float newHp = Math.Min(targetHealth.Max, targetHealth.Current + healAmount);
		EcsWorld.Set(target.Target, new Health(newHp, targetHealth.Max));

		atk.CurrentCooldown = (_unlimitedPowerProvider?.Invoke() == true) ? 0f : GetEffectiveAttackCooldown(entity, atk.Cooldown);

		OnHealEffectRequested?.Invoke(currentPos, targetPos);
		OnHealFlashRequested?.Invoke(target.Target);
		OnUnitHealedCallback?.Invoke(target.Target, entity, healAmount);
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

			if (System.Numerics.Vector3.Distance(pos.Value, position) > radius) return;

			if (casterEntity != Entity.Null && EcsWorld.IsAlive(casterEntity))
			{
				if (EcsWorld.Has<LastAttacker>(entity)) EcsWorld.Set(entity, new LastAttacker(casterEntity));
				else _aoeLastAttackerList.Add((entity, casterEntity));
			}

			float newHp = Math.Max(0, hp.Current - damage);
			hp.Current = newHp;

			OnUnitDamagedCallback?.Invoke(entity, casterEntity, damage);

			if (newHp <= 0) _aoeKillList.Add(entity);
			else OnDamageFlashRequested?.Invoke(entity);
		});

		ProcessAoeLastAttackers();
		ProcessAoeKillList();
	}

	private void ProcessAoeLastAttackers()
	{
		foreach (var (targetEnt, attackerEnt) in _aoeLastAttackerList)
		{
			if (EcsWorld.IsAlive(targetEnt))
			{
				EcsWorld.AddOrGet(targetEnt, new LastAttacker(attackerEnt)) = new LastAttacker(attackerEnt);
			}
		}
	}

	private void ProcessAoeKillList()
	{
		foreach (var entity in _aoeKillList)
		{
			if (!EcsWorld.IsAlive(entity) || EcsWorld.Has<Dead>(entity)) continue;

			EcsWorld.Add<Dead>(entity);
			OnKillUnitRequested?.Invoke(entity);
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
