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

namespace Realm.Ecs.Services
{
	public partial class CombatAndDamageService
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
			return _context.World.Has<TraversableTerrain>(entity)
			       && ((TerrainPathingFlags)_context.World.Get<TraversableTerrain>(entity).Value & TerrainPathingFlags.Flying) != 0;
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

		/// <summary>
		///     One enemy captured during an acquisition scan, holding the values the scan and the range check need so the
		///     decision is made from the snapshot instead of re-reading the world per candidate.
		/// </summary>
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

		/// <summary>
		///     Everything one attacker needs for a target scan, copied out once so the scan does not re-read the attacker's
		///     components for every enemy it considers.
		/// </summary>
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

		/// <summary>
		///     One enemy found by the grid-free local scan, carrying the squared distance it was found at so the nearest can be
		///     chosen without measuring it again.
		/// </summary>
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

		private readonly Dictionary<Entity, (Entity Target, float Remaining)> _abandonedTargetCooldown = new();

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

		private readonly Dictionary<(Entity Attacker, Entity Target), RouteReachabilityEntry> _routeReachabilityCache = new();

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

		/// <summary>
		///     Called with (victim, attacker, dealt damage, damage before the victim's mitigation), so a rule can weigh what
		///     was swung rather than what landed.
		/// </summary>
		public Action<Entity, Entity, float, float>? OnUnitDamagedCallback;

		/// <summary>
		///     Answers the multiplier this hit's damage carries, which is how a rule scales one seat's damage against
		///     another. A null provider, or one answering a value that is not a finite, non-negative number, leaves the damage
		///     as resolved; zero is a stated multiplier like any other and means the hit lands for nothing.
		/// </summary>
		public Func<Entity, Entity, float>? DamageEffectivenessProvider;

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
		///     Decides whether this service is the one that applies the damage it computed. A client takes every unit's
		///     health from the authority's descriptions and never owns the outcome, so it answers no; the authoritative
		///     process answers yes and owns the damage it resolves.
		/// </summary>
		public Func<bool>? AuthoritativeProvider { get; set; }

		/// <summary>
		///     Decides whether an entity takes part in combat resolution. A client narrows this to the units this player
		///     owns, whose presentation it is responsible for; an authoritative context leaves it unset and resolves every
		///     entity in the world.
		/// </summary>
		public Func<Entity, bool>? SimulateEntityFilter { get; set; }

		/// <summary>Answers the weapons an inventory item grants, so an item that arms a unit is armed on screen as well.</summary>
		public Func<string, string[]?>? ItemGrantedWeaponsProvider { get; set; }

		/// <summary>
		///     Answers whether two players are enemies when the world carries no relationship matrix, which is what a
		///     lobby that has not started yet looks like. A match that has a matrix never asks.
		/// </summary>
		public Func<int, int, bool>? EnemyPredicate { get; set; }
	}
}