using System;
using System.Collections.Generic;
using System.Numerics;
using Arch.Core;
using DotRecast.Detour;
using Realm.Ecs.Common;
using Realm.Ecs.Components.Combat;
using Realm.Ecs.Components.Core;
using Realm.Ecs.Components.Movement;
using Realm.Ecs.Components.Tags;
using Realm.Ecs.Components.Terrain;
using Realm.Ecs.Services;

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

		private Vector3 _scanAttackerPos;
		private PlayerEntity _scanAttackerOwner;
		private bool _scanIsAttackerEnemy;
		private int _scanAttackerPlayerIndex;
		private bool _scanAttackerIsGroundMelee;
		private Entity _scanAttacker;
		private float _scanMaxDistSq;
		private readonly ScanCandidate[] _scanCandidates = new ScanCandidate[MaxAcquisitionCandidates];
		private int _scanCandidateCount;

		private Vector3 _scanPriestPos;
		private PlayerEntity _scanFriendlyOwner;
		private float _scanFriendlyClosestDist;
		private Entity _scanClosestDamagedFriendly;

		private readonly List<(Entity Attacker, AttackTarget Target)> _tickNewAttackTargets = new();
		private readonly List<(Entity Target, Entity Attacker)> _tickActionsToAddLastAttacker = new();
		private readonly List<Entity> _tickActionsToRemoveTarget = new();
		private readonly List<(Entity Attacker, Vector3 TargetPos)> _tickActionsToChase = new();
		private readonly List<Entity> _tickActionsToStopChasing = new();
		private readonly List<Entity> _tickUnitsToKill = new();
		private readonly List<(Entity Priest, HealingTarget Target)> _tickNewHealingTargets = new();
		private readonly List<Entity> _tickHealRemoveTargets = new();
		private readonly List<(Entity Priest, Vector3 TargetPos)> _tickHealChaseTargets = new();
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


		// Acquisition considers the few closest enemies, not just the single closest one.
		// The closest candidate can be unreachable (a flyer at altitude, a unit on a plateau) and
		// must not block the unit from engaging the next-reachable enemy standing behind it.
		private const int MaxAcquisitionCandidates = 4;


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

		public Action<Vector3, Vector3>? OnArrowProjectileRequested;
		public Action<Vector3, Vector3, string?, Entity>? OnWeaponProjectileRequested;
		public Func<string, string[]?>? UnitWeaponsProvider;
		public Action<Entity>? OnDamageFlashRequested;
		public Action<Vector3, Vector3>? OnHealEffectRequested;
		public Action<Entity>? OnHealFlashRequested;
		public Action<Entity, Entity, float>? OnUnitDamagedCallback;
		public Action<Entity, Entity, float>? OnUnitHealedCallback;
		public Action<Entity, Entity>? OnUnitAttackedCallback;
		public Action<string>? OnUnderAttackAlertRequested;
		public Action<Entity>? OnKillUnitRequested;

		public CombatAndDamageService(Func<bool>? unlimitedPowerProvider = null)
		{
			_unlimitedPowerProvider = unlimitedPowerProvider;
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
				if (!GameContext.World.IsAlive(kvp.Key.Attacker) || !GameContext.World.IsAlive(kvp.Key.Target))
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
			var query = QueryCache.AllWorldStateQuery;
			GameContext.World.Query(in query, (Entity entity) => worldEntity = entity);
			return worldEntity;
		}

		private Entity FindTerrainEntity()
		{
			Entity worldEntity = Entity.Null;
			var query = QueryCache.AllTerrainStateQuery;
			GameContext.World.Query(in query, (Entity entity) => worldEntity = entity);
			return worldEntity;
		}

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
			Entity worldEntity = Entity.Null;
			var query = QueryCache.AllCombatAlertStateQuery;
			GameContext.World.Query(in query, (Entity entity) => worldEntity = entity);

			if (worldEntity != Entity.Null && GameContext.World.Has<CombatAlertState>(worldEntity))
			{
				return GameContext.World.Get<CombatAlertState>(worldEntity).UnderAttackAlertTimer;
			}

			return 0f;
		}

		private void SetCombatAlertTimer(float value)
		{
			Entity worldEntity = Entity.Null;
			var query = QueryCache.AllCombatAlertStateQuery;
			GameContext.World.Query(in query, (Entity entity) => worldEntity = entity);

			if (worldEntity != Entity.Null && GameContext.World.Has<CombatAlertState>(worldEntity))
			{
				ref var state = ref GameContext.World.Get<CombatAlertState>(worldEntity);
				state.UnderAttackAlertTimer = value;
			}
		}

		private void TickCombatAlertTimer(float fDelta)
		{
			Entity worldEntity = Entity.Null;
			var query = QueryCache.AllCombatAlertStateQuery;
			GameContext.World.Query(in query, (Entity entity) => worldEntity = entity);

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
			_tickNewAttackTargets.Clear();
			GameContext.World.Query(in _targetAcquisitionQuery, _targetAcquisitionQueryDelegate);
			foreach (var (attacker, target) in _tickNewAttackTargets)
			{
				if (GameContext.World.IsAlive(attacker))
				{
					GameContext.World.SetOrAdd(attacker, target);
				}
			}
		}

		private void TargetAcquisitionQueryAction(Entity entity, ref Position pos, ref Attack atk, ref Owner owner)
		{
			if (GameContext.World.Has<DefinitionId>(entity) && GameContext.World.Get<DefinitionId>(entity).Value == "priest") return;

			bool isAutoCombat = GameContext.World.Has<AutoCombatState>(entity) && GameContext.World.Get<AutoCombatState>(entity).Enabled;
			bool isAttackMove = GameContext.World.Has<AttackMove>(entity);
			bool isPatrol = GameContext.World.Has<Patrol>(entity);
			bool isIdle = !GameContext.World.Has<MoveTo>(entity) && !isAttackMove;

			if (!isIdle && !isAttackMove && !isPatrol && !isAutoCombat) return;

			if (!TryGetTargetAcquisitionContext(entity, ref pos, ref atk, ref owner)) return;

			GameContext.World.Query(in _enemyQuery, _potentialEnemyQueryDelegate);
			FindAndSetBestReachableCandidate(entity, ref pos, ref atk);
		}

		private bool TryGetTargetAcquisitionContext(Entity entity, ref Position pos, ref Attack atk, ref Owner owner)
		{
			float scanRadius = GameContext.World.Has<ScanRadius>(entity) ? GameContext.World.Get<ScanRadius>(entity).Value : 15.0f;
			if (GetTimeOfDayIndex() == 2) scanRadius *= 0.7f;

			_scanAttackerPos = pos.Value;
			_scanAttackerOwner = owner.PlayerEntity;
			_scanAttacker = entity;
			_scanIsAttackerEnemy = GameContext.World.Has<UnitFaction>(entity) && GameContext.World.Get<UnitFaction>(entity).IsEnemy;
			_scanAttackerPlayerIndex = GameContext.World.Has<UnitOwnerPlayer>(entity)
				? GameContext.World.Get<UnitOwnerPlayer>(entity).PlayerIndex
				: (_scanIsAttackerEnemy ? 1 : 0);

			// A ground-pathing melee attacker can never hit a unit hovering at flight
			// altitude, so flyers are excluded from its scan entirely.
			_scanAttackerIsGroundMelee = !IsFlying(entity) && atk.Range <= MeleeRangeThreshold;
			_scanMaxDistSq = scanRadius * scanRadius;
			_scanCandidateCount = 0;
			return true;
		}

		private void FindAndSetBestReachableCandidate(Entity entity, ref Position pos, ref Attack atk)
		{
			for (int i = 0; i < _scanCandidateCount; i++)
			{
				var candidate = _scanCandidates[i];
				if (candidate.Enemy == Entity.Null) continue;
				if (IsTargetStillAbandoned(entity, candidate.Enemy)) continue;
				if (!GameContext.World.Has<Position>(candidate.Enemy)) continue;

				var enemyPos = GameContext.World.Get<Position>(candidate.Enemy).Value;
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
			int enemyPlayerIndex = GameContext.World.Has<UnitOwnerPlayer>(potentialEnemy)
				? GameContext.World.Get<UnitOwnerPlayer>(potentialEnemy).PlayerIndex
				: ((GameContext.World.Has<UnitFaction>(potentialEnemy) && GameContext.World.Get<UnitFaction>(potentialEnemy).IsEnemy) ? 1 : 0);

			if (NetworkService.ArePlayerIndicesEnemies(_scanAttackerPlayerIndex, enemyPlayerIndex))
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

			GameContext.World.Query(in _combatQuery, _combatQueryDelegate);

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
				_abandonedTargetCooldown.Remove(targetEntity);
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
			if (!GameContext.World.IsAlive(target.Target) || GameContext.World.Has<Dead>(target.Target))
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

			string targetUnitId = GameContext.World.Get<DefinitionId>(target).Value;
			bool targetIsEnemy = GameContext.World.Has<UnitFaction>(target) && GameContext.World.Get<UnitFaction>(target).IsEnemy;

			if (targetIsEnemy) return;

			if (GetCombatAlertTimer() <= 0f)
			{
				SetCombatAlertTimer(UnderAttackAlertCooldown);
				OnUnderAttackAlertRequested?.Invoke(targetUnitId);
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
			if (GameContext.World.Has<EquippedItems>(entity))
			{
				var eq = GameContext.World.Get<EquippedItems>(entity);
				if (eq.Value != null)
				{
					foreach (var item in eq.Value)
					{
						if (GameContext.RegistryService?.ItemRegistry != null && GameContext.RegistryService.ItemRegistry.TryGetValue(item, out var itemMeta) && itemMeta?.GrantedWeapons != null && itemMeta.GrantedWeapons.Length > 0)
						{
							weaponId = itemMeta.GrantedWeapons[0];
							break;
						}
					}
				}
			}

			if (weaponId == null && GameContext.World.Has<DefinitionId>(entity))
			{
				var defId = GameContext.World.Get<DefinitionId>(entity).Value;
				var weapons = UnitWeaponsProvider?.Invoke(defId);
				weaponId = (weapons != null && weapons.Length > 0) ? weapons[0] : defId;
			}

			OnWeaponProjectileRequested?.Invoke(currentPos, targetPos, weaponId, target);
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
			bool attackerIsEnemy = GameContext.World.Has<UnitFaction>(attacker) && GameContext.World.Get<UnitFaction>(attacker).IsEnemy;
			var splashQuery = QueryCache.AllPositionAndHealthAndOwnerNoneDeadQuery;

			GameContext.World.Query(in splashQuery, (Entity splashEnt, ref Position sPos, ref Health sHp, ref Owner sOwner) =>
			{
				if (splashEnt == primaryTarget || splashEnt == attacker) return;
				if (GameContext.World.Has<Invulnerable>(splashEnt)) return;

				if (!atk.FriendlyFire)
				{
					bool splashIsEnemy = GameContext.World.Has<UnitFaction>(splashEnt) && GameContext.World.Get<UnitFaction>(splashEnt).IsEnemy;
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
		///     scale when the unit has no authored radius. Combat uses the same value so melee
		///     reach is consistent with how close units can actually get to each other.
		/// </summary>
		private float GetUnitContactRadius(Entity entity)
		{
			float scale = GameContext.World.Has<CollisionScale>(entity) ? GameContext.World.Get<CollisionScale>(entity).Value : 1.0f;
			if (GameContext.World.Has<CollisionRadius>(entity))
			{
				return GameContext.World.Get<CollisionRadius>(entity).Value * scale;
			}

			return scale * GameplayConstants.DefaultCollisionRadius;
		}

		/// <summary>
		///     Minimum distance the movement separation enforces between two units' contact
		///     radii, pushed apart at CollisionSeparationFactor, plus a small safety margin so
		///     steering/clump variance never leaves a pair hovering just out of reach.
		/// </summary>
		private float GetMinimumMeleeContactReach(Entity a, Entity b)
		{
			return (GetUnitContactRadius(a) + GetUnitContactRadius(b))
			       * GameplayConstants.CollisionSeparationFactor * MeleeContactReachSafetyMargin;
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
		private bool IsReachableWithinRange(Entity attacker, Entity target, Vector3 attackerPos, Vector3 targetPos, float effectiveRange, bool isMelee, float meleeVerticalReach)
		{
			if (IsFlying(attacker) || !isMelee)
			{
				// Ranged attackers (like towers) shoot through the air and do not need a navmesh path.
				return Distance(attackerPos, targetPos) <= effectiveRange + RouteReachMargin;
			}

			if (GameContext.NavMeshPathfinder == null)
			{
				// No pathfinder available (e.g. headless unit tests) — fall back to the vertical
				// shortcut so behaviour is unchanged.
				return IsVerticallyReachableForRange(attacker, attackerPos, targetPos, effectiveRange, isMelee, meleeVerticalReach);
			}

			if (!TryGetNavMeshQuery(out var navMeshQuery))
			{
				return IsVerticallyReachableForRange(attacker, attackerPos, targetPos, effectiveRange, isMelee, meleeVerticalReach);
			}

			var key = (attacker, target);
			if (IsRouteReachabilityCached(key, navMeshQuery, attackerPos, targetPos, out bool cachedResult))
			{
				return cachedResult;
			}

			int includeFlags = GameContext.World.Has<PathingFlags>(attacker) ? GameContext.World.Get<PathingFlags>(attacker).Value : (int)TerrainPathingFlags.Ground;
			PathFollow pf = default;
			GameContext.NavMeshPathfinder.ComputePath(navMeshQuery, attackerPos, targetPos, (ushort)includeFlags, ref pf);

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

		private bool TryGetNavMeshQuery(out DtNavMeshQuery navMeshQuery)
		{
			navMeshQuery = null;
			Entity terrainEntity = FindTerrainEntity();
			if (terrainEntity == Entity.Null || !GameContext.World.Has<TerrainState>(terrainEntity)) return false;

			ref var ts = ref GameContext.World.Get<TerrainState>(terrainEntity);
			if (ts.NavMeshQuery == null) return false;

			navMeshQuery = ts.NavMeshQuery;
			return true;
		}

		private bool IsRouteReachabilityCached((Entity, Entity) key, DtNavMeshQuery navMeshQuery, Vector3 attackerPos, Vector3 targetPos, out bool reachable)
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

		private bool EvaluateMeleeReachability(Vector3 last, Vector3 targetPos, float effectiveRange, float meleeVerticalReach)
		{
			float horiz = new Vector2(last.X - targetPos.X, last.Z - targetPos.Z).Length();
			float vert = Math.Abs(last.Y - targetPos.Y);
			return horiz <= Math.Max(0.5f, effectiveRange) && vert <= meleeVerticalReach;
		}

		private bool IsVerticallyReachableForRange(Entity attacker, Vector3 attackerPos, Vector3 targetPos, float effectiveRange, bool isMelee, float meleeVerticalReach)
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
			GameContext.World.Query(in _priestScanQuery, _priestScanQueryDelegate);
			ProcessNewHealingTargets();

			_tickHealRemoveTargets.Clear();
			_tickHealChaseTargets.Clear();
			_tickHealStopChasing.Clear();
			GameContext.World.Query(in _healingExecutionQuery, _healingExecutionQueryDelegate);

			ProcessHealRemovals();
			ProcessHealChases();
			ProcessHealStopChasing();
		}

		private void ProcessNewHealingTargets()
		{
			foreach (var (priest, target) in _tickNewHealingTargets)
			{
				if (GameContext.World.IsAlive(priest)) GameContext.World.SetOrAdd(priest, target);
			}
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
			foreach (var (priest, targetPos) in _tickHealChaseTargets)
			{
				if (!GameContext.World.IsAlive(priest)) continue;

				bool needsRetarget = true;
				if (GameContext.World.Has<MoveTo>(priest))
				{
					var existingTarget = GameContext.World.Get<MoveTo>(priest).Target;
					if (Distance(existingTarget, targetPos) < ChaseRetargetDistance) needsRetarget = false;
				}

				if (needsRetarget) GameContext.World.SetOrAdd(priest, new MoveTo(targetPos));
			}
		}

		private void ProcessHealStopChasing()
		{
			foreach (var priest in _tickHealStopChasing)
			{
				if (!GameContext.World.IsAlive(priest)) continue;

				if (GameContext.World.Has<MoveTo>(priest)) GameContext.World.Remove<MoveTo>(priest);
				if (GameContext.World.Has<Velocity>(priest)) GameContext.World.Set(priest, new Velocity(Vector3.Zero));
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
				bool isIdle = !GameContext.World.Has<MoveTo>(entity);
				if (isIdle)
				{
					_scanClosestDamagedFriendly = Entity.Null;
					_scanFriendlyClosestDist = 15.0f;
					_scanPriestPos = pos.Value;
					_scanFriendlyOwner = owner.PlayerEntity;

					GameContext.World.Query(in _friendlyScanQuery, _friendlyScanQueryDelegate);

					if (_scanClosestDamagedFriendly != Entity.Null)
					{
						_tickNewHealingTargets.Add((entity, new HealingTarget(_scanClosestDamagedFriendly)));
					}
				}
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
			var query = QueryCache.AllPositionAndHealthNoneDeadQuery;
			_aoeKillList.Clear();
			_aoeLastAttackerList.Clear();
			GameContext.World.Query(in query, (Entity entity, ref Position pos, ref Health hp) => { ProcessAoeTarget(entity, ref pos, ref hp, position, radius, damage, casterEntity, enemyOnly); });

			ProcessAoeLastAttackers();
			ProcessAoeKillList();
		}

		private void ProcessAoeTarget(Entity entity, ref Position pos, ref Health hp, Vector3 position, float radius, float damage, Entity casterEntity, bool enemyOnly)
		{
			if (!IsAoeTargetValid(entity, pos.Value, position, radius, enemyOnly)) return;

			UpdateAoeLastAttacker(entity, casterEntity);

			ApplyAoeDamage(entity, ref hp, damage, casterEntity);
		}

		private bool IsAoeTargetValid(Entity entity, Vector3 entityPos, Vector3 aoePosition, float aoeRadius, bool enemyOnly)
		{
			if (GameContext.World.Has<Invulnerable>(entity)) return false;

			if (enemyOnly)
			{
				bool isEnemy = GameContext.World.Has<UnitFaction>(entity) && GameContext.World.Get<UnitFaction>(entity).IsEnemy;
				if (!isEnemy) return false;
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

		public void HealAOE(Vector3 position, float radius, float healAmount)
		{
			var query = QueryCache.AllPositionAndHealthNoneDeadQuery;
			GameContext.World.Query(in query, (Entity entity, ref Position pos, ref Health hp) =>
			{
				bool isEnemy = GameContext.World.Has<UnitFaction>(entity) && GameContext.World.Get<UnitFaction>(entity).IsEnemy;
				if (isEnemy) return;

				if (Vector3.Distance(pos.Value, position) <= radius)
				{
					float newHp = Math.Min(hp.Max, hp.Current + healAmount);
					hp.Current = newHp;
					OnHealFlashRequested?.Invoke(entity);
					OnUnitHealedCallback?.Invoke(entity, Entity.Null, healAmount);
				}
			});
		}
	}
}