using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using Realm.Ecs;
using Godot;
using Realm.Client.Core;
using Realm.Client.Network;
using Realm.Ecs.AI;
using Realm.Ecs.AI.Affordances;
using Realm.Ecs.AI.Genres;
using Realm.Ecs.AI.Policy;
using Realm.Ecs.AI.Training;
using Realm.Ecs.Common;
using Realm.Ecs.Components.Abilities;
using Realm.Ecs.Components.Combat;
using Realm.Ecs.Components.Core;
using Realm.Ecs.Components.Meta;
using Realm.Ecs.Components.Movement;
using Realm.Ecs.Components.Resources;
using Realm.Ecs.Components.Stats;
using Realm.Ecs.Components.Tags;
using Realm.Ecs.Components.Terrain;
using Realm.Ecs.Services;
using UnitId = Realm.MapAPI.UnitId;
using Vector3 = System.Numerics.Vector3;
using Realm.MapAPI;

namespace Realm.Client.Services;

public class SimulationService
{
	private float _shroudSimulationTimer;

	public void ApplySpellEffectDomain(string spellId, Vector3 position, Entity casterEnt)
	{
		GameContext.RegistryService.AbilityDefinitions.TryGetValue(spellId, out var def);
		if (def == null) return;

		float aoe = def.AreaOfEffectRadius > 0f ? def.AreaOfEffectRadius : 4.0f;

		if (def.Damage > 0f)
		{
			DealSpellDamageAOE(position, aoe, def.Damage, casterEnt);
		}
		else if (def.Healing > 0f)
		{
			HealAOE(position, aoe, def.Healing, casterEnt);
		}
	}

	public void RegisterBuffModifierDomain(string buffId, string statName, bool isPercentage, float value)
	{
		var statId = new StatId(statName);
		var modType = isPercentage ? ModifierType.Percentage : ModifierType.Flat;
		var modifier = new StatModifier(statId, modType, value);
		if (!BuffRegistry.BuffModifiers.TryGetValue(buffId, out var list))
		{
			list = new List<StatModifier>();
			BuffRegistry.BuffModifiers[buffId] = list;
		}

		list.Add(modifier);
	}

	public void SetUnitFacingDomain(UnitId unit, float facingRadians)
	{
		if (TryGetEntity(unit, out var entity) && GameContext.World.IsAlive(entity))
		{
			GameContext.World.SetOrAdd(entity, new RotationY(facingRadians));
			if (GameContext.World.Has<InterpolationTarget>(entity))
			{
				ref var interp = ref GameContext.World.Get<InterpolationTarget>(entity);
				interp.RotationY = facingRadians;
			}
		}
	}

	public void AddBuffDomain(UnitId unit, string buffId, float duration)
	{
		var wrapper = GetUnitWrapper(unit);
		wrapper?.AddBuff(buffId, duration);
	}

	public void SetUnitSpellImmune(UnitId unit, bool immune)
	{
		if (TryGetEntity(unit, out var entity) && GameContext.World.IsAlive(entity))
		{
			if (immune)
			{
				if (!GameContext.World.Has<SpellImmune>(entity))
					GameContext.World.Add(entity, new SpellImmune());
			}
			else
			{
				if (GameContext.World.Has<SpellImmune>(entity))
					GameContext.World.Remove<SpellImmune>(entity);
			}
		}
	}

	public void DomainProcessCompletedBuilding(Entity buildingEntity, Action<Entity>? viewCallback, Action<Entity>? notifyCallback)
	{
		if (!GameContext.World.IsAlive(buildingEntity)) return;

		RemoveConstructionComponents(buildingEntity);
		viewCallback?.Invoke(buildingEntity);
		notifyCallback?.Invoke(buildingEntity);
	}

	public void RemoveConstructionComponents(Entity buildingEntity)
	{
		if (GameContext.World.Has<UnderConstruction>(buildingEntity))
		{
			GameContext.World.Remove<UnderConstruction>(buildingEntity);
		}

		if (GameContext.World.Has<ConstructionState>(buildingEntity))
		{
			GameContext.World.Remove<ConstructionState>(buildingEntity);
		}
	}

	public void ProcessBuildQueueEntity(Entity entity)
	{
		if (HasActiveTask(entity)) return;

		ref var q = ref GameContext.World.Get<BuildQueue>(entity);
		if (q.Count > 0)
		{
			if (q.TryDequeue(out string nextType, out var nextPos, out Entity nextTarget))
			{
				PendingQueuedCommands.Add((entity, nextType, nextPos, nextTarget));
			}
		}
		else
		{
			PendingQueuedCommands.Add((entity, "clear_queue_component", Vector3.Zero, Entity.Null));
		}
	}

	public bool HasActiveTask(Entity entity)
	{
		return GameContext.World.Has<MoveTo>(entity) ||
			   GameContext.World.Has<BuildTask>(entity) ||
			   GameContext.World.Has<AttackTarget>(entity) ||
			   GameContext.World.Has<AttackMove>(entity) ||
			   GameContext.World.Has<Follow>(entity) ||
			   GameContext.World.Has<Patrol>(entity) ||
			   GameContext.World.Has<Gatherer>(entity) ||
			   GameContext.World.Has<HealingTarget>(entity);
	}

	public void ProcessPendingQueuedCommands(Func<Entity, string?, Vector3, Entity, bool> executeCommandCallback)
	{
		foreach (var cmd in PendingQueuedCommands)
		{
			if (!GameContext.World.IsAlive(cmd.Entity)) continue;

			ProcessPendingCommand(cmd, executeCommandCallback);
		}
	}

	public void ProcessPendingCommand((Entity Entity, string? Type, Vector3 Position, Entity Target) cmd, Func<Entity, string?, Vector3, Entity, bool> executeCommandCallback)
	{
		if (cmd.Type == "clear_queue_component")
		{
			ClearQueueComponent(cmd.Entity);
			return;
		}

		bool success = executeCommandCallback(cmd.Entity, cmd.Type, cmd.Position, cmd.Target);
		ProcessRemainingQueue(cmd.Entity, success, executeCommandCallback);
	}


	public void ClearQueueComponent(Entity entity)
	{
		if (GameContext.World.Has<BuildQueue>(entity))
		{
			GameContext.World.Remove<BuildQueue>(entity);
		}
	}

	public void ProcessRemainingQueue(Entity entity, bool lastSuccess, Func<Entity, string?, Vector3, Entity, bool> executeCommandCallback)
	{
		while (!lastSuccess && GameContext.World.Has<BuildQueue>(entity))
		{
			ref var q = ref GameContext.World.Get<BuildQueue>(entity);
			if (q.TryDequeue(out string? nextType, out var nextPos, out Entity nextTarget))
			{
				lastSuccess = executeCommandCallback(entity, nextType ?? "", nextPos, nextTarget);
			}
			else
			{
				GameContext.World.Remove<BuildQueue>(entity);
				break;
			}
		}

		if (!lastSuccess && GameContext.World.Has<BuildQueue>(entity) && GameContext.World.Get<BuildQueue>(entity).Count == 0)
		{
			GameContext.World.Remove<BuildQueue>(entity);
		}
	}

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

	private readonly MovementAndPathfindingService _movementService;
	private readonly CombatAndDamageService _combatService;
	private readonly ResourceEconomyService _economyService;

	private float _fDelta;
	private int _tickCount = 0;

	private PlayerEntity _autoCastCasterOwnerEntity;
	private Entity _autoCastBestFriendlyEntity;
	private float _autoCastBestFriendlyHp;
	private Vector3 _autoCastBestFriendlyPos;
	private float _autoCastRange;
	private Vector3 _autoCastCasterPos;
	private readonly MovementAndPathfindingService.SpatialGridEntryCallback _autoCastFriendlyCallback;

	private PlayerEntity _autoCastCasterOwnerEntityEnemyCheck;
	private int _autoCastCasterPlayerIndex;
	private Entity _autoCastBestEnemyEntity;
	private float _autoCastClosestDist;
	private Vector3 _autoCastBestEnemyPos;
	private readonly MovementAndPathfindingService.SpatialGridEntryCallback _autoCastEnemyCallback;

	private readonly List<string> _tickExpiredBuffs = new();
	private readonly List<string> _tickBuffKeys = new();
	private readonly List<(Entity Entity, Patrol Patrol)> _tickPatrolToFlip = new();
	private readonly List<Entity> _tickFollowToStop = new();
	private readonly List<(Entity Follower, Vector3 TargetPos)> _tickFollowToMove = new();
	private readonly List<Entity> _tickFollowToRemoveMoveTo = new();
	private readonly List<Entity> _tickArrivedUnits = new();
	private readonly List<(Entity Entity, PathFollow PathFollow)> _tickAddPathFollow = new();
	private readonly List<Entity> _tickEntitiesToClearOrders = new();
	private readonly List<Entity> _tickEntitiesToStopGathering = new();
	private readonly List<SpawningRequest> _tickSpawningRequests = new();
	private bool _tickNeedsUiRefresh = false;
	private readonly Dictionary<int, BotController> _botControllers = new();

	private readonly QueryDescription _buffQuery = QueryCache.AllBuffsNoneDeadQuery;
	private readonly QueryDescription _buffStateQuery = QueryCache.AllBuffStateNoneDeadQuery;
	private readonly QueryDescription _statsRecalcQuery = new QueryDescription().WithAll<Stats>().WithNone<Dead>();
	private readonly QueryDescription _patrolArrivalQuery = QueryCache.AllPatrolAndPositionNoneDeadAndAttackTargetQuery;
	private readonly QueryDescription _followQuery = QueryCache.AllFollowAndPositionNoneDeadQuery;
	private readonly QueryDescription _attackCooldownQuery = QueryCache.AllAttackQuery;
	private readonly QueryDescription _healthRegenQuery = QueryCache.AllHealthNoneDeadQuery;
	private readonly QueryDescription _manaRegenQuery = QueryCache.AllManaAndManaRegenNoneDeadQuery;
	private readonly QueryDescription _prodQuery = QueryCache.AllProductionQueueQuery;
	private readonly QueryDescription _spellCooldownQuery = QueryCache.AllSpellCooldownsQuery;
	private readonly QueryDescription _cooldownsQuery = QueryCache.AllCooldownsQuery;
	private readonly QueryDescription _autoCastQuery = QueryCache.AllAutoCastQuery;

	private EntityCallback<Buffs> _buffsQueryDelegate = null!;
	private EntityCallback<BuffState> _buffStateQueryDelegate = null!;
	private EntityCallback<Stats> _statsRecalcQueryDelegate = null!;
	private EntityCallback<Patrol, Position> _patrolArrivalQueryDelegate = null!;
	private EntityCallback<Follow, Position> _followQueryDelegate = null!;
	private EntityCallback<Attack> _attackCooldownQueryDelegate = null!;
	private EntityCallback<Health> _healthRegenQueryDelegate = null!;
	private EntityCallback<Mana, ManaRegen> _manaRegenQueryDelegate = null!;
	private EntityCallback<ProductionQueue> _prodQueryDelegate = null!;
	private EntityCallback<InterpolationTarget> _interpolationQueryDelegate = null!;
	private EntityCallback<SpellCooldowns> _spellCooldownQueryDelegate = null!;
	private EntityCallback<Cooldowns> _cooldownsQueryDelegate = null!;
	private EntityCallback<AutoCastState, Position, DefinitionId, Owner> _autoCastQueryDelegate = null!;

	public Action<Vector3, Vector3> OnArrowProjectileRequested;
	public Action<Vector3, Vector3, string?, Entity>? OnWeaponProjectileRequested;
	public Action<Entity> OnDamageFlashRequested;
	public Action<Vector3, Vector3> OnHealEffectRequested;
	public Action<Entity> OnHealFlashRequested;
	public Action<Entity, Entity, float> OnUnitDamagedCallback;
	public Action<Entity, Entity, float>? OnUnitHealedCallback;
	public Action<Entity, Entity>? OnUnitAttackedCallback;
	public Action<int, string> OnUnderAttackAlertRequested;
	public Action<Entity> OnKillUnitRequested;
	public Action<string, Vector3, Entity, bool>? OnSpawnUnitFromProductionRequested;
	public Action<Entity>? OnClearUnitOrdersRequested;
	public Action<Entity> OnStopGatheringMovementRequested;
	public Action OnUiRefreshRequested;
	public Action<Entity> OnPropDepleted;

	private readonly HashSet<long> _alliedPairs = new();
	private readonly Dictionary<long, PlayerRelationship> _explicitRelationships = new();

	private static long MakePairKey(int p1, int p2)
	{
		int min = Math.Min(p1, p2);
		int max = Math.Max(p1, p2);
		return ((long)min << 32) | (uint)max;
	}

	public Vector3 GetPlayerStartLocation(int playerIndex)
	{
		return Vector3.Zero;
	}

	private bool TryGetScriptPlayersState(out ScriptPlayersState playersState)
	{
		var worldEntity = ECSContext.WorldEntity;
		if (GameContext.World != null && worldEntity != Entity.Null && GameContext.World.IsAlive(worldEntity))
		{
			return GameContext.World.TryGet<ScriptPlayersState>(worldEntity, out playersState);
		}

		playersState = default;
		return false;
	}

	public void SetPlayerTeam(int playerIndex, int teamIndex)
	{
		int stringId = StringRegistry.GetOrAdd(teamIndex == 0 ? "Neutral" : $"Team {teamIndex}");

		if (TryGetScriptPlayersState(out var playersState) && playerIndex >= 0 && playerIndex < playersState.Players.Length)
		{
			playersState.Players[playerIndex].Team = teamIndex;
			playersState.Players[playerIndex].TeamNameStringId = stringId;
			GameContext.World.GetBuffer<ScriptPlayer>().Set(playerIndex, in playersState.Players[playerIndex]);
		}

		var worldEntity = ECSContext.WorldEntity;
		if (GameContext.World != null && worldEntity != Entity.Null && GameContext.World.Has<NetworkMappingState>(worldEntity))
		{
			var playerEntities = GameContext.World.Get<NetworkMappingState>(worldEntity).PlayerEntities;
			if (playerIndex >= 0 && playerIndex < playerEntities.Length && playerEntities[playerIndex] != Entity.Null && GameContext.World.IsAlive(playerEntities[playerIndex]))
			{
				if (GameContext.World.Has<PlayerTeam>(playerEntities[playerIndex]))
				{
					ref var pt = ref GameContext.World.Get<PlayerTeam>(playerEntities[playerIndex]);
					pt.TeamIndex = teamIndex;
					pt.TeamNameStringId = stringId;
				}
				else
				{
					GameContext.World.Add(playerEntities[playerIndex], new PlayerTeam(teamIndex, stringId));
				}
			}
		}

		if (LobbyManager.Instance != null && LobbyManager.Instance.PlayerList != null)
		{
			var list = LobbyManager.Instance.PlayerList;
			for (int i = 0; i < list.Count; i++)
			{
				if (list[i].Slot == playerIndex)
				{
					list[i].TeamIndex = teamIndex;
					list[i].Team = teamIndex == 0 ? "Neutral" : $"Team {teamIndex}";
					break;
				}
			}
		}

		RecalculatePlayerRelationships();
	}

	public int GetPlayerTeam(int playerIndex)
	{
		if (playerIndex >= 0 && playerIndex < 25 && GameContext.World != null)
		{
			var buffer = GameContext.World.GetBuffer<ScriptPlayer>();
			if (buffer != null)
			{
				return buffer.Get(playerIndex).Team;
			}
		}

		if (TryGetScriptPlayersState(out var playersState) && playerIndex >= 0 && playerIndex < playersState.Players.Length)
		{
			return playersState.Players[playerIndex].Team;
		}

		if (LobbyManager.Instance != null && LobbyManager.Instance.PlayerList != null)
		{
			var list = LobbyManager.Instance.PlayerList;
			for (int i = 0; i < list.Count; i++)
			{
				if (list[i].Slot == playerIndex)
				{
					return list[i].TeamIndex;
				}
			}
		}

		return 0;
	}

	public string GetPlayerTeamName(int playerIndex)
	{
		if (playerIndex >= 0 && playerIndex < 25 && GameContext.World != null)
		{
			var buffer = GameContext.World.GetBuffer<ScriptPlayer>();
			if (buffer != null)
			{
				int stringId = buffer.Get(playerIndex).TeamNameStringId;
				if (stringId != 0)
				{
					return StringRegistry.Get(stringId);
				}
			}
		}

		int teamIndex = GetPlayerTeam(playerIndex);
		return teamIndex == 0 ? "Neutral" : $"Team {teamIndex}";
	}

	public void SetPlayersAllied(int playerIndex, int otherPlayerIndex, bool allied)
	{
		long key = MakePairKey(playerIndex, otherPlayerIndex);
		if (allied)
		{
			_alliedPairs.Add(key);
			_explicitRelationships[key] = PlayerRelationship.Ally;
		}
		else
		{
			_alliedPairs.Remove(key);
			_explicitRelationships.Remove(key);
		}

		RecalculatePlayerRelationships();
	}

	public void SetPlayerRelationship(int playerIndex, int otherPlayerIndex, PlayerRelationship relationship)
	{
		long key = MakePairKey(playerIndex, otherPlayerIndex);
		_explicitRelationships[key] = relationship;
		if (relationship == PlayerRelationship.Ally)
		{
			_alliedPairs.Add(key);
		}
		else
		{
			_alliedPairs.Remove(key);
		}

		RecalculatePlayerRelationships();
	}

	public PlayerRelationship GetPlayerRelationship(int playerIndex, int otherPlayerIndex)
	{
		if (playerIndex == otherPlayerIndex) return PlayerRelationship.Ally;

		var worldEntity = ActiveWorldEntity;
		if (GameContext.World != null && worldEntity != Entity.Null && GameContext.World.Has<PlayerRelationshipMatrix>(worldEntity))
		{
			return GameContext.World.Get<PlayerRelationshipMatrix>(worldEntity).GetRelationship(playerIndex, otherPlayerIndex);
		}

		if (ArePlayersAllied(playerIndex, otherPlayerIndex))
		{
			return PlayerRelationship.Ally;
		}

		if (ArePlayersEnemies(playerIndex, otherPlayerIndex))
		{
			return PlayerRelationship.Enemy;
		}

		return PlayerRelationship.Neutral;
	}

	public bool ArePlayersAllied(int playerIndex, int otherPlayerIndex)
	{
		if (playerIndex == otherPlayerIndex) return true;

		var worldEntity = ActiveWorldEntity;
		if (GameContext.World != null && worldEntity != Entity.Null && GameContext.World.Has<PlayerRelationshipMatrix>(worldEntity))
		{
			return GameContext.World.Get<PlayerRelationshipMatrix>(worldEntity).AreAllied(playerIndex, otherPlayerIndex);
		}

		if (_alliedPairs.Contains(MakePairKey(playerIndex, otherPlayerIndex))) return true;

		int t1 = GetPlayerTeam(playerIndex);
		int t2 = GetPlayerTeam(otherPlayerIndex);
		if (t1 != 0 && t2 != 0 && t1 == t2) return true;

		return false;
	}

	public bool ArePlayersEnemies(int playerIndex, int otherPlayerIndex)
	{
		if (playerIndex == otherPlayerIndex) return false;

		var worldEntity = ActiveWorldEntity;
		if (GameContext.World != null && worldEntity != Entity.Null && GameContext.World.Has<PlayerRelationshipMatrix>(worldEntity))
		{
			return GameContext.World.Get<PlayerRelationshipMatrix>(worldEntity).AreEnemies(playerIndex, otherPlayerIndex);
		}

		if (_alliedPairs.Contains(MakePairKey(playerIndex, otherPlayerIndex))) return false;

		int t1 = GetPlayerTeam(playerIndex);
		int t2 = GetPlayerTeam(otherPlayerIndex);
		if (t1 != 0 && t2 != 0)
		{
			return t1 != t2;
		}

		return playerIndex != otherPlayerIndex;
	}

	public void RecalculatePlayerRelationships()
	{
		var worldEntity = ActiveWorldEntity;
		if (GameContext.World == null || worldEntity == Entity.Null || !GameContext.World.IsAlive(worldEntity)) return;

		PlayerRelationshipMatrix matrix = default;

		for (int i = 0; i < PlayerRelationshipMatrix.MaxPlayers; i++)
		{
			for (int j = 0; j < PlayerRelationshipMatrix.MaxPlayers; j++)
			{
				if (i == j)
				{
					matrix.SetRelationship(i, j, PlayerRelationship.Ally);
					continue;
				}

				long key = MakePairKey(i, j);
				if (_explicitRelationships.TryGetValue(key, out var explicitRel))
				{
					matrix.SetRelationship(i, j, explicitRel);
				}
				else if (_alliedPairs.Contains(key))
				{
					matrix.SetRelationship(i, j, PlayerRelationship.Ally);
				}
				else
				{
					int t1 = GetPlayerTeam(i);
					int t2 = GetPlayerTeam(j);
					if (t1 != 0 && t2 != 0)
					{
						matrix.SetRelationship(i, j, t1 == t2 ? PlayerRelationship.Ally : PlayerRelationship.Enemy);
					}
					else if (t1 == 0 && t2 == 0)
					{
						matrix.SetRelationship(i, j, PlayerRelationship.Neutral);
					}
					else
					{
						matrix.SetRelationship(i, j, PlayerRelationship.Enemy);
					}
				}
			}
		}

		if (GameContext.World.Has<PlayerRelationshipMatrix>(worldEntity))
		{
			GameContext.World.Set(worldEntity, matrix);
		}
		else
		{
			GameContext.World.Add(worldEntity, matrix);
		}
	}

	public void SetPlayerActiveDomain(int playerIndex, bool active)
	{
		if (TryGetScriptPlayersState(out var playersState) && playerIndex >= 0 && playerIndex < playersState.Players.Length)
		{
			playersState.Players[playerIndex].Active = active;
			GameContext.World.GetBuffer<ScriptPlayer>().Set(playerIndex, in playersState.Players[playerIndex]);
		}
	}

	public void IssueAttackMoveOrder(UnitId unit, Vector3 destination, Action<UnitId, string, Vector3>? onUnitOrdered)
	{
		var wrapper = GetUnitWrapper(unit);
		if (wrapper != null)
		{
			wrapper.AttackMove(destination);
			onUnitOrdered?.Invoke(unit, "AttackMove", destination);
		}
	}

	public void IssueCastOrder(UnitId caster, string abilityId, UnitId target, Action<UnitId, string, Vector3>? onUnitOrdered)
	{

		var casterWrapper = GetUnitWrapper(caster);
		if (casterWrapper != null)
		{
			casterWrapper.Attack(target);
			var targetWrapper = GetUnitWrapper(target);
			Vector3 pos = targetWrapper?.Position ?? Vector3.Zero;
			onUnitOrdered?.Invoke(caster, abilityId ?? "Cast", pos);
		}
	}

	public Action<Entity>? OnResourceHarvested;
	public Action<int, string, float> OnResourceDepositedForPlayer;
	public Action<int, string> OnProductionCompleted;
	public Func<string, float> GetProductionBuildTime;


	public SimulationService()
	{
		_movementService = new MovementAndPathfindingService();
		_combatService = new CombatAndDamageService(() => GameHost.Instance != null && GameContext.CheatService.UnlimitedPowerEnabled);
		_economyService = new ResourceEconomyService();

		_combatService.OnArrowProjectileRequested = (p1, p2) => EnqueueVFXRequest("arrow", p1, p2, 1.0f, 40f);
		_combatService.UnitWeaponsProvider = defId =>
		{
			if (GameHost.UnitRegistry.TryGetValue(defId, out var meta))
				return meta.Weapons;
			return null;
		};
		_combatService.OnWeaponProjectileRequested = (p1, p2, weaponId, targetEnt, ownerPlayerIndex) =>
		{
			EnqueueVFXRequest(weaponId ?? "arrow", p1, p2, 1.0f, 40f, targetEnt.Id, ownerPlayerIndex);
		};
		_combatService.OnDamageFlashRequested = ent =>
		{
			if (GameContext.World.IsAlive(ent) && GameContext.World.Has<Position>(ent))
				EnqueueVFXRequest("damage_flash", GameContext.World.Get<Position>(ent).Value, GameContext.World.Get<Position>(ent).Value, 1.0f, 0f, ent.Id);
		};
		_combatService.OnHealEffectRequested = (p1, p2) => EnqueueVFXRequest("heal", p1, p2, 1.0f, 25f);
		_combatService.OnHealFlashRequested = ent =>
		{
			if (GameContext.World.IsAlive(ent) && GameContext.World.Has<Position>(ent))
				EnqueueVFXRequest("heal_flash", GameContext.World.Get<Position>(ent).Value, GameContext.World.Get<Position>(ent).Value, 1.0f, 0f, ent.Id);
		};
		_combatService.OnUnitDamagedCallback = (e1, e2, d) => OnUnitDamagedCallback?.Invoke(e1, e2, d);
		_combatService.OnUnitHealedCallback = (e1, e2, h) => OnUnitHealedCallback?.Invoke(e1, e2, h);
		_combatService.OnUnitAttackedCallback = (e1, e2) => OnUnitAttackedCallback?.Invoke(e1, e2);
		_combatService.OnUnderAttackAlertRequested = (player, id) => OnUnderAttackAlertRequested?.Invoke(player, id);
		_combatService.OnKillUnitRequested = ent => OnKillUnitRequested?.Invoke(ent);

		_economyService.OnResourceDepositedForPlayer = (player, res, amount) => OnResourceDepositedForPlayer?.Invoke(player, res, amount);
		_economyService.OnClearUnitOrdersRequested = ent => OnClearUnitOrdersRequested?.Invoke(ent);
		_economyService.OnStopGatheringMovementRequested = ent => OnStopGatheringMovementRequested?.Invoke(ent);
		_economyService.OnPropDepleted = ent => OnPropDepleted?.Invoke(ent);
		_economyService.OnResourceHarvested = ent => OnResourceHarvested?.Invoke(ent);

		_autoCastFriendlyCallback = OnAutoCastFriendlyCandidateFound;
		_autoCastEnemyCallback = OnAutoCastEnemyCandidateFound;
	}

	public void Initialize()
	{
		_buffsQueryDelegate = UpdateBuffsQueryAction;
		_buffStateQueryDelegate = UpdateBuffStateQueryAction;
		_statsRecalcQueryDelegate = StatsRecalcQueryAction;
		_patrolArrivalQueryDelegate = PatrolArrivalQueryAction;
		_followQueryDelegate = FollowQueryAction;
		_attackCooldownQueryDelegate = AttackCooldownQueryAction;
		_healthRegenQueryDelegate = HealthRegenQueryAction;
		_manaRegenQueryDelegate = ManaRegenQueryAction;
		_prodQueryDelegate = ProdQueryAction;
		_cooldownsQueryDelegate = CooldownsQueryAction;
		_interpolationQueryDelegate = InterpolationQueryAction;
		_spellCooldownQueryDelegate = SpellCooldownQueryAction;
		_autoCastQueryDelegate = AutoCastQueryAction;
	}

	public void TickEcs(float fDelta, bool advanceWorldClock = true)
	{
		_fDelta = fDelta;
		_tickCount++;
		_tickEntitiesToClearOrders.Clear();
		_tickEntitiesToStopGathering.Clear();
		_tickSpawningRequests.Clear();
		_tickAddPathFollow.Clear();
		_tickNeedsUiRefresh = false;

		if (ActiveWorldEntity != Entity.Null && GameContext.World.IsAlive(ActiveWorldEntity))
		{
			if (advanceWorldClock && GameContext.World.Has<WorldState>(ActiveWorldEntity))
			{
				var state = GameContext.World.Get<WorldState>(ActiveWorldEntity);
				float elapsed = state.GameElapsedTime + fDelta;
				float timer = state.TimeOfDayTimer;
				int index = state.TimeOfDayIndex;

				if (state.DayNightCycleEnabled)
				{
					timer += fDelta;
					const float cycleDuration = 90f;
					if (timer >= cycleDuration)
					{
						timer -= cycleDuration;
					}

					float progress = timer / cycleDuration;
					index = (int)(progress * 4f) % 4;
				}

				GameContext.World.Set(ActiveWorldEntity, new WorldState(elapsed, index, timer, state.DayNightCycleEnabled));
			}

			if (GameContext.World.Has<CountdownState>(ActiveWorldEntity))
			{
				var countdown = GameContext.World.Get<CountdownState>(ActiveWorldEntity);
				if (countdown.Active)
				{
					float newDuration = countdown.Duration - fDelta;
					if (newDuration <= 0f)
					{
						GameContext.World.Set(ActiveWorldEntity, new CountdownState(false, 0f, countdown.Text));
					}
					else
					{
						GameContext.World.Set(ActiveWorldEntity, new CountdownState(true, newDuration, countdown.Text));
					}
				}
			}
		}

		using (GameContext.PerformanceTrackingService.SampleFunction(ProfileFunctionId.SimulationSpellCooldownQuery))
		{
			GameContext.World.Query(in _spellCooldownQuery, _spellCooldownQueryDelegate);
		}

		using (GameContext.PerformanceTrackingService.SampleFunction(ProfileFunctionId.SimulationCooldownsQuery))
		{
			GameContext.World.Query(in _cooldownsQuery, _cooldownsQueryDelegate);
		}

		using (GameContext.PerformanceTrackingService.SampleFunction(ProfileFunctionId.SimulationMovementStep))
		{
			_movementService.StepMovement(fDelta);
		}

		using (GameContext.PerformanceTrackingService.SampleFunction(ProfileFunctionId.SimulationCombatStep))
		{
			_combatService.StepCombat(fDelta, _movementService);
		}

		using (GameContext.PerformanceTrackingService.SampleFunction(ProfileFunctionId.SimulationEconomyStep))
		{
			_economyService.StepEconomy(fDelta);
		}

		_shroudSimulationTimer += fDelta;
		if (_shroudSimulationTimer >= ShroudSimulationService.UpdateInterval)
		{
			_shroudSimulationTimer = 0f;
			using (GameContext.PerformanceTrackingService.SampleFunction(ProfileFunctionId.SimulationShroudStep))
			{
				ShroudSimulationService.Tick(GameContext.World, ActiveWorldEntity);
			}
		}

		using (GameContext.PerformanceTrackingService.SampleFunction(ProfileFunctionId.SimulationBuffQueries))
		{
			GameContext.World.Query(in _buffQuery, _buffsQueryDelegate);
			GameContext.World.Query(in _buffStateQuery, _buffStateQueryDelegate);
			GameContext.World.Query(in _statsRecalcQuery, _statsRecalcQueryDelegate);
		}

		using (GameContext.PerformanceTrackingService.SampleFunction(ProfileFunctionId.SimulationPatrolArrivals))
		{
			ProcessPatrolArrivals();
		}

		using (GameContext.PerformanceTrackingService.SampleFunction(ProfileFunctionId.SimulationFollowMovements))
		{
			ProcessFollowMovements();
		}

		using (GameContext.PerformanceTrackingService.SampleFunction(ProfileFunctionId.SimulationUnitQueries))
		{
			GameContext.World.Query(in _attackCooldownQuery, _attackCooldownQueryDelegate);
			GameContext.World.Query(in _healthRegenQuery, _healthRegenQueryDelegate);
			GameContext.World.Query(in _manaRegenQuery, _manaRegenQueryDelegate);
			GameContext.World.Query(in _prodQuery, _prodQueryDelegate);
			GameContext.World.Query(in _autoCastQuery, _autoCastQueryDelegate);
		}

		foreach (var (entity, pf) in _tickAddPathFollow)
		{
			if (GameContext.World.IsAlive(entity))
			{
				GameContext.World.Add(entity, pf);
			}
		}

		using (GameContext.PerformanceTrackingService.SampleFunction(ProfileFunctionId.SimulationBotControllers))
		{
			TickBotControllers(fDelta);
		}

		using (GameContext.PerformanceTrackingService.SampleFunction(ProfileFunctionId.SimulationDeferredCommands))
		{
			ApplyDeferredTickCommands();
		}
	}

	private readonly Dictionary<int, List<GenericAffordance>> _customDecisionsPerPlayer = new();
	public event Action<int, string, string, Vector3, string>? OnBotCustomActionExecuted;

	public void SetPlayerBotProfileDomain(int playerIndex, string profileJson)
	{
		if (string.IsNullOrWhiteSpace(profileJson)) return;
		try
		{
			var profile = BotProfile.FromJson(profileJson);
			SetBotProfile(playerIndex, profile);
		}
		catch
		{
		}
	}

	public void SetBotProfile(int playerIndex, BotProfile profile)
	{
		if (!_botControllers.TryGetValue(playerIndex, out var bot))
		{
			bot = new BotController(playerIndex, profile);
			bot.CustomActionCallback = (pIdx, actId, pos, intent) => { OnBotCustomActionExecuted?.Invoke(pIdx, actId, intent, pos, string.Empty); };
			_botControllers[playerIndex] = bot;
		}
		else
		{
			bot.LoadProfile(profile);
		}
	}

	public string GetPlayerBotProfileDomain(int playerIndex)
	{
		var profile = GetBotProfile(playerIndex);
		return profile != null ? profile.ToJson() : string.Empty;
	}

	public BotProfile? GetBotProfile(int playerIndex)
	{
		return _botControllers.TryGetValue(playerIndex, out var bot) ? bot.Profile : null;
	}

	public void SetPlayerBotGenreDomain(int playerIndex, string genreName, string? configJson)
	{
		SetBotGenre(playerIndex, genreName, configJson);
	}

	public void SetBotGenre(int playerIndex, string genreName, string? configJson = null)
	{
		var genreProvider = AiGenreRegistry.Get(genreName);
		ConfigureGenreProvider(genreProvider, configJson);
		ConfigureCustomProvider(genreName, genreProvider);
		UpdateOrCreateBotController(playerIndex, genreProvider);
	}

	private void ConfigureGenreProvider(IAiGenreProvider genreProvider, string? configJson)
	{
		if (string.IsNullOrWhiteSpace(configJson)) return;
		try
		{
			var dict = JsonSerializer.Deserialize<Dictionary<string, string>>(configJson);
			if (dict != null) genreProvider.ConfigureFromParameters(dict);
		}
		catch
		{
		}
	}

	private void ConfigureCustomProvider(string genreName, IAiGenreProvider genreProvider)
	{
		if (!string.Equals(genreName, "custom", StringComparison.OrdinalIgnoreCase)) return;
		if (genreProvider is not CustomGenreProvider customProvider) return;

		customProvider.CustomScanner = (world, pIdx, dest, ctx) =>
		{
			if (_customDecisionsPerPlayer.TryGetValue(pIdx, out var list) && list != null) dest.AddRange(list);
		};
	}

	private void UpdateOrCreateBotController(int playerIndex, IAiGenreProvider genreProvider)
	{
		if (!_botControllers.TryGetValue(playerIndex, out var bot))
		{
			var profile = genreProvider.CreateDefaultProfile(ECSContext.ActiveMapName ?? "GenericMap");
			bot = new BotController(playerIndex, profile, genreProvider);
			bot.CustomActionCallback = (pIdx, actId, pos, intent) => { OnBotCustomActionExecuted?.Invoke(pIdx, actId, intent, pos, string.Empty); };
			_botControllers[playerIndex] = bot;
		}
		else
		{
			bot.GenreProvider = genreProvider;
			var profile = genreProvider.CreateDefaultProfile(bot.Profile.MapName);
			bot.LoadProfile(profile);
		}
	}

	public string GetPlayerBotGenreDomain(int playerIndex)
	{
		return GetBotGenre(playerIndex);
	}

	public string GetBotGenre(int playerIndex)
	{
		return _botControllers.TryGetValue(playerIndex, out var bot) ? bot.GenreProvider.GenreName : "rts";
	}

	public void RegisterCustomBotDecisionDomain(int playerIndex, string actionId, string intent, float[] featureVector, Vector3 position, string payload)
	{
		RegisterCustomBotDecision(playerIndex, actionId, intent, featureVector, position, payload);
	}

	public void RegisterCustomBotDecision(int playerIndex, string actionId, string intent, float[] featureVector, Vector3 position, string payload)
	{
		if (!_customDecisionsPerPlayer.TryGetValue(playerIndex, out var list))
		{
			list = new List<GenericAffordance>();
			_customDecisionsPerPlayer[playerIndex] = list;
		}

		CommandIntent cmdIntent = CommandIntent.Interact;
		if (Enum.TryParse<CommandIntent>(intent, true, out var parsedIntent))
		{
			cmdIntent = parsedIntent;
		}

		list.Add(new GenericAffordance(
			Entity.Null,
			cmdIntent,
			Entity.Null,
			position,
			actionId,
			featureVector
		));
	}

	public void ClearCustomBotDecisions(int playerIndex)
	{
		if (_customDecisionsPerPlayer.TryGetValue(playerIndex, out var list))
		{
			list.Clear();
		}
	}

	private void TickBotControllers(float fDelta)
	{
		if (GameContext.World == null) return;

		for (int pIdx = 0; pIdx < 8; pIdx++)
		{
			if (!IsPlayerBot(pIdx)) continue;

			if (!_botControllers.TryGetValue(pIdx, out var bot))
			{
				bot = InitializeDefaultBotController(pIdx);
				_botControllers[pIdx] = bot;
			}

			bot.Tick(GameContext.World, pIdx, fDelta);
		}
	}

	private bool IsPlayerBot(int pIdx)
	{
		if (GameHost.Instance == null) return false;
		return GameHost.Instance.IsPlayerComputer(pIdx);
	}

	private BotController InitializeDefaultBotController(int pIdx)
	{
		var bot = new BotController(pIdx);
		bot.CustomActionCallback = (botPIdx, actId, pos, intent) => { OnBotCustomActionExecuted?.Invoke(botPIdx, actId, intent, pos, string.Empty); };
		LoadBotProfileFromDisk(bot);
		return bot;
	}

	private void LoadBotProfileFromDisk(BotController bot)
	{
		string mapName = ECSContext.ActiveMapName ?? "";
		string botPath = Path.Combine(OS.GetUserDataDir(), $"{mapName}_bot.json");
		if (!File.Exists(botPath)) return;
		try
		{
			var profile = BotProfile.FromJson(File.ReadAllText(botPath));
			bot.LoadProfile(profile);
		}
		catch
		{
		}
	}

	public void TickEditorPhysics(float fDelta)
	{
		_fDelta = fDelta;
		_tickArrivedUnits.Clear();
		_tickAddPathFollow.Clear();
		_movementService.RefreshTerrainState();
	}

	public Func<Vector3, float>? EditorHeightProvider
	{
		get => _movementService.EditorHeightProvider;
		set => _movementService.EditorHeightProvider = value;
	}

	public void SetDelta(float fDelta)
	{
		_fDelta = fDelta;
	}

	public bool TryGetInterpolatedTransform(Entity entity, float fraction, out Vector3 position, out float rotationY)
	{
		position = Vector3.Zero;
		rotationY = 0f;

		if (GameContext.World == null || !GameContext.World.IsAlive(entity)) return false;

		if (GameContext.World.Has<Position>(entity))
		{
			Vector3 curPos = GameContext.World.Get<Position>(entity).Value;
			Vector3 prevPos = GameContext.World.Has<PreviousPosition>(entity) ? GameContext.World.Get<PreviousPosition>(entity).Value : curPos;
			position = Vector3.Lerp(prevPos, curPos, fraction);
		}

		if (GameContext.World.Has<RotationY>(entity))
		{
			float curRot = GameContext.World.Get<RotationY>(entity).Value;
			float prevRot = GameContext.World.Has<PreviousRotationY>(entity) ? GameContext.World.Get<PreviousRotationY>(entity).Value : curRot;
			rotationY = Mathf.LerpAngle(prevRot, curRot, fraction);
		}

		return true;
	}

	public (Vector3 Position, float RotationY) GetInterpolatedTransform(Entity entity, float fraction)
	{
		TryGetInterpolatedTransform(entity, fraction, out var pos, out var rotY);
		return (pos, rotY);
	}

	public EntityCallback<Position, MoveTo, MovementStats> EditorMovementQueryDelegate => _movementService.EditorMovementQueryDelegate;
	public EntityCallback<InterpolationTarget> InterpolationQueryDelegate => _interpolationQueryDelegate;

	public void SetRuntimeReferences(
		List<Unit3D> allUnits,
		List<Prop3D> allProps,
		DefinitionManager definitionManager,
		ResourceId goldResourceId,
		ResourceId woodResourceId,
		ResourceId stoneResourceId,
		RuntimeTerrain groundTerrain)
	{
		_economyService.SetRuntimeReferences(definitionManager, goldResourceId, woodResourceId, stoneResourceId);
	}

	private void ProcessPatrolArrivals()
	{
		_tickPatrolToFlip.Clear();
		GameContext.World.Query(in _patrolArrivalQuery, _patrolArrivalQueryDelegate);
		foreach (var (entity, patrol) in _tickPatrolToFlip)
		{
			if (GameContext.World.IsAlive(entity))
			{
				var newPatrol = patrol;
				newPatrol.GoingToB = !patrol.GoingToB;
				GameContext.World.Set(entity, newPatrol);

				var dest = newPatrol.GoingToB ? newPatrol.PointB : newPatrol.PointA;
				var moveTo = new MoveTo(dest);
				GameContext.World.SetOrAdd(entity, moveTo);
			}
		}
	}

	private void ProcessFollowMovements()
	{
		_tickFollowToStop.Clear();
		_tickFollowToMove.Clear();
		_tickFollowToRemoveMoveTo.Clear();

		GameContext.World.Query(in _followQuery, _followQueryDelegate);

		foreach (var entity in _tickFollowToStop)
		{
			if (GameContext.World.IsAlive(entity))
			{
				if (GameContext.World.Has<MoveTo>(entity))
				{
					GameContext.World.Remove<MoveTo>(entity);
				}

				if (GameContext.World.Has<Follow>(entity))
				{
					GameContext.World.Remove<Follow>(entity);
				}
			}
		}

		foreach (var entity in _tickFollowToRemoveMoveTo)
		{
			if (GameContext.World.IsAlive(entity) && GameContext.World.Has<MoveTo>(entity))
			{
				GameContext.World.Remove<MoveTo>(entity);
			}
		}

		foreach (var (follower, targetPos) in _tickFollowToMove)
		{
			if (GameContext.World.IsAlive(follower))
			{
				var moveTo = new MoveTo(targetPos);
				GameContext.World.SetOrAdd(follower, moveTo);
			}
		}
	}

	private void ApplyDeferredTickCommands()
	{
		ProcessClearOrders();
		ProcessStopGathering();
		ProcessSpawningRequests();
		ProcessUiRefresh();
	}

	private void ProcessClearOrders()
	{
		foreach (var ent in _tickEntitiesToClearOrders)
		{
			if (GameContext.World.IsAlive(ent)) OnClearUnitOrdersRequested?.Invoke(ent);
		}
	}

	private void ProcessStopGathering()
	{
		foreach (var ent in _tickEntitiesToStopGathering)
		{
			if (GameContext.World.IsAlive(ent)) OnStopGatheringMovementRequested?.Invoke(ent);
		}
	}

	private void ProcessSpawningRequests()
	{
		foreach (var req in _tickSpawningRequests)
		{
			OnSpawnUnitFromProductionRequested?.Invoke(req.UnitId, req.Position, req.BuildingEntity, req.IsFromQueue);
		}
	}

	private void ProcessUiRefresh()
	{
		if (_tickNeedsUiRefresh) OnUiRefreshRequested?.Invoke();
	}

	private void UpdateBuffsQueryAction(Entity entity, ref Buffs buffs)
	{
		UpdateBuffDictionary(buffs.Value, _fDelta);
	}

	private void UpdateBuffStateQueryAction(Entity entity, ref BuffState buffs)
	{
		UpdateBuffDictionary(buffs.Value, _fDelta);
	}

	private void UpdateBuffDictionary(Dictionary<string, float> buffsDict, float delta)
	{
		_tickBuffKeys.Clear();
		_tickExpiredBuffs.Clear();
		foreach (var key in buffsDict.Keys)
		{
			_tickBuffKeys.Add(key);
		}

		for (int i = 0; i < _tickBuffKeys.Count; i++)
		{
			string key = _tickBuffKeys[i];
			float newTime = buffsDict[key] - delta;
			if (newTime <= 0)
			{
				_tickExpiredBuffs.Add(key);
			}
			else
			{
				buffsDict[key] = newTime;
			}
		}

		for (int i = 0; i < _tickExpiredBuffs.Count; i++)
		{
			buffsDict.Remove(_tickExpiredBuffs[i]);
		}
	}

	private bool UpdateModifiers(Entity entity, ref ModifierState modState)
	{
		bool removedAny = false;
		var list = modState.Value;
		for (int i = list.Count - 1; i >= 0; i--)
		{
			var mod = list[i];
			if (mod.Duration > 0f)
			{
				float newDur = mod.Duration - _fDelta;
				if (newDur <= 0f)
				{
					list.RemoveAt(i);
					removedAny = true;
				}
				else
				{
					list[i] = new StatModifier(mod.StatTypeId, mod.Type, mod.Value, newDur);
				}
			}
		}
		return removedAny;
	}

	private static readonly StatId ArmorStatId = new StatId("Armor");
	private static readonly StatId AttackStatId = new StatId("Attack");
	private static readonly StatId MovementSpeedStatId = new StatId("MovementSpeed");

	private void StatsRecalcQueryAction(Entity entity, ref Stats stats)
	{
		bool hadExpiredModifiers = UpdateEntityModifiers(entity);

		bool hasModifiers = GameContext.World.Has<ModifierState>(entity) && GameContext.World.Get<ModifierState>(entity).Value.Count > 0;
		bool hasBuffs = GameContext.World.Has<BuffState>(entity) && GameContext.World.Get<BuffState>(entity).Value.Count > 0;

		if (!hasModifiers && !hasBuffs && !hadExpiredModifiers)
		{
			return;
		}

		if (!hadExpiredModifiers && ((entity.Id + _tickCount) % 6 != 0))
		{
			return;
		}

		float flatArmor = 0f, percentArmor = 1f;
		float flatAttack = 0f, percentAttack = 1f;
		float flatSpeed = 0f, percentSpeed = 1f;

		if (hasModifiers)
		{
			ApplyEntityModifiers(entity, ref flatArmor, ref percentArmor, ref flatAttack, ref percentAttack, ref flatSpeed, ref percentSpeed);
		}
		if (hasBuffs)
		{
			ApplyEntityBuffModifiers(entity, ref flatArmor, ref percentArmor, ref flatAttack, ref percentAttack, ref flatSpeed, ref percentSpeed);
		}

		ApplyCalculatedArmor(entity, stats.Value, flatArmor, percentArmor);
		ApplyCalculatedAttack(entity, stats.Value, flatAttack, percentAttack);
		ApplyCalculatedSpeed(entity, stats.Value, flatSpeed, percentSpeed);
	}

	private bool UpdateEntityModifiers(Entity entity)
	{
		if (GameContext.World.Has<ModifierState>(entity))
		{
			ref var modState = ref GameContext.World.Get<ModifierState>(entity);
			return UpdateModifiers(entity, ref modState);
		}
		return false;
	}

	private void ApplyEntityModifiers(Entity entity, ref float flatArmor, ref float percentArmor, ref float flatAttack, ref float percentAttack, ref float flatSpeed, ref float percentSpeed)
	{
		if (!GameContext.World.Has<ModifierState>(entity)) return;
		var modState = GameContext.World.Get<ModifierState>(entity);
		var list = modState.Value;
		for (int i = 0; i < list.Count; i++) ApplyMod(list[i], ref flatArmor, ref percentArmor, ref flatAttack, ref percentAttack, ref flatSpeed, ref percentSpeed);
	}

	private void ApplyEntityBuffModifiers(Entity entity, ref float flatArmor, ref float percentArmor, ref float flatAttack, ref float percentAttack, ref float flatSpeed, ref float percentSpeed)
	{
		if (!GameContext.World.Has<BuffState>(entity)) return;
		var buffState = GameContext.World.Get<BuffState>(entity);
		foreach (var buffKey in buffState.Value.Keys)
		{
			if (!BuffRegistry.BuffModifiers.TryGetValue(buffKey, out var mods)) continue;
			for (int i = 0; i < mods.Count; i++) ApplyMod(mods[i], ref flatArmor, ref percentArmor, ref flatAttack, ref percentAttack, ref flatSpeed, ref percentSpeed);
		}
	}

	private void ApplyCalculatedArmor(Entity entity, Dictionary<StatId, float> baseStats, float flatArmor, float percentArmor)
	{
		if (!baseStats.TryGetValue(ArmorStatId, out var baseArmor)) return;
		float finalArmor = (baseArmor + flatArmor) * percentArmor;
		if (GameContext.World.Has<Armor>(entity)) GameContext.World.Set(entity, new Armor(finalArmor));
	}

	private void ApplyCalculatedAttack(Entity entity, Dictionary<StatId, float> baseStats, float flatAttack, float percentAttack)
	{
		if (!baseStats.TryGetValue(AttackStatId, out var baseAttack)) return;
		float finalAttack = (baseAttack + flatAttack) * percentAttack;
		if (GameContext.World.Has<Attack>(entity))
		{
			var atk = GameContext.World.Get<Attack>(entity);
			GameContext.World.Set(entity, new Attack(finalAttack, atk.Range, atk.Cooldown, atk.CurrentCooldown));
		}
	}

	private void ApplyCalculatedSpeed(Entity entity, Dictionary<StatId, float> baseStats, float flatSpeed, float percentSpeed)
	{
		if (!baseStats.TryGetValue(MovementSpeedStatId, out var baseSpeed)) return;
		float finalSpeed = (baseSpeed + flatSpeed) * percentSpeed;
		if (GameContext.World.Has<MovementStats>(entity))
		{
			var mv = GameContext.World.Get<MovementStats>(entity);
			GameContext.World.Set(entity, new MovementStats(finalSpeed, mv.Acceleration, mv.TurnRate));
		}
	}

	private void ApplyMod(StatModifier mod, ref float flatArmor, ref float percentArmor, ref float flatAttack, ref float percentAttack, ref float flatSpeed, ref float percentSpeed)
	{
		string statId = mod.StatTypeId.Value;
		if (string.Equals(statId, "Armor", StringComparison.OrdinalIgnoreCase))
		{
			ApplyModifierValue(mod, ref flatArmor, ref percentArmor);
			return;
		}

		if (string.Equals(statId, "Attack", StringComparison.OrdinalIgnoreCase) || string.Equals(statId, "AttackDamage", StringComparison.OrdinalIgnoreCase))
		{
			ApplyModifierValue(mod, ref flatAttack, ref percentAttack);
			return;
		}

		if (string.Equals(statId, "MovementSpeed", StringComparison.OrdinalIgnoreCase) || string.Equals(statId, "Speed", StringComparison.OrdinalIgnoreCase))
		{
			ApplyModifierValue(mod, ref flatSpeed, ref percentSpeed);
			return;
		}
	}

	private void ApplyModifierValue(StatModifier mod, ref float flatVal, ref float percentVal)
	{
		if (mod.Type == ModifierType.Flat) flatVal += mod.Value;
		else if (mod.Type == ModifierType.Percentage) percentVal *= mod.Value;
	}

	public void DisableShroud(Action viewAction)
	{
		Entity worldEntity = Entity.Null;
		var query = QueryCache.AllShroudStateQuery;
		GameContext.World.Query(in query, ent => worldEntity = ent);

		if (worldEntity != Entity.Null && GameContext.World.IsAlive(worldEntity) && GameContext.World.Has<ShroudState>(worldEntity))
		{
			GameContext.World.Mutate<ShroudState>(worldEntity, (ref ShroudState state) => { state.VisibilityMode = ShroudVisibilityMode.Revealed; });
		}

		viewAction?.Invoke();
	}

	public void DomainKillUnit(Entity entity, Action viewAction)
	{
		if (GameContext.World.IsAlive(entity) && !GameContext.World.Has<Dead>(entity))
		{
			GameContext.World.Add<Dead>(entity);
			viewAction?.Invoke();
		}
	}

	public void DomainDestroyUnit(Entity entity, bool isBuilding, float radius, Vector3 unitPos, Action viewAction)
	{
		if (!GameContext.World.IsAlive(entity)) return;

		if (isBuilding)
		{
			if (GameContext.World.IsAlive(ECSContext.WorldEntity) && GameContext.World.Has<TerrainState>(ECSContext.WorldEntity))
			{
				ref var state = ref GameContext.World.Get<TerrainState>(ECSContext.WorldEntity);
				ECSContext.TerrainNavMeshService?.UncarveObstacle(ref state, unitPos, radius);
			}
		}

		GameContext.NetworkService.RemoveMultiplayerMapping(entity.Id);

		int id = entity.Id;
		UnitWrapperCache.Remove(id);
		GameContext.World.Destroy(entity);

		viewAction?.Invoke();
	}

	public bool HandleEnemyDeathEconomyDomain(Entity unitEntity, string unitId, Entity playerEntity, int resourceCap)
	{
		if (!GameContext.RegistryService.UnitRegistry.TryGetValue(unitId, out var bountyMeta) || bountyMeta.GoldBounty <= 0f) return false;
		if (!GameContext.World.IsAlive(playerEntity) || !GameContext.World.Has<PlayerResources>(playerEntity)) return false;

		GameContext.World.Mutate<PlayerResources>(playerEntity, (ref PlayerResources r) =>
		{
			r.Gold = (int)Math.Min(resourceCap, r.Gold + bountyMeta.GoldBounty);
		});
		
		return true;
	}

	public (float Radius, System.Numerics.Vector3 Position) GetObstacleUncarveData(Entity unitEntity, System.Numerics.Vector3 fallbackPos)
	{
		float radius = GameContext.World.Has<CollisionRadius>(unitEntity) ? GameContext.World.Get<CollisionRadius>(unitEntity).Value : 2.0f;
		var unitPos = GameContext.World.Has<Position>(unitEntity) ? GameContext.World.Get<Position>(unitEntity).Value : fallbackPos;
		return (radius, unitPos);
	}

	public int GetDeathPopulationCost(Entity unitEntity, string unitId)
	{
		if (!GameContext.RegistryService.UnitRegistry.TryGetValue(unitId, out var killMeta)) return 0;
		
		int popCost = 0;
		if (!GameContext.World.Has<BypassPopulationTag>(unitEntity))
		{
			popCost = killMeta.PopCost;
		}

		return popCost;
	}

	public Entity GetKillerEntityDomain(Entity unitEntity)
	{
		Entity killerEntity = Entity.Null;
		if (!GameContext.World.IsAlive(unitEntity)) return killerEntity;

		if (GameContext.World.Has<LastAttacker>(unitEntity))
		{
			var att = GameContext.World.Get<LastAttacker>(unitEntity).Value;
			if (GameContext.World.IsAlive(att))
			{
				killerEntity = att;
			}
		}
		return killerEntity;
	}

	public void DestroyUnitEntityDomain(Entity unitEntity)
	{
		if (!GameContext.World.IsAlive(unitEntity)) return;
		UnitWrapperCache.Remove(unitEntity.Id);
		GameContext.World.Destroy(unitEntity);
	}

	public void DestroyEntitiesDomain(IEnumerable<Entity> entities)
	{
		foreach (var entity in entities)
		{
			if (GameContext.World.IsAlive(entity))
			{
				UnitWrapperCache.Remove(entity.Id);
				GameContext.World.Destroy(entity);
			}
		}
	}

	public Unit_WasmRuntime GetUnitWrapper(Entity entity)
	{
		if (!GameContext.World.IsAlive(entity))
		{
			throw new ArgumentException("Entity is not alive", nameof(entity));
		}

		if (UnitWrapperCache.TryGetValue(entity.Id, out var wrapper))
		{
			return wrapper;
		}

		wrapper = new Unit_WasmRuntime(entity, GameContext.World);
		UnitWrapperCache[entity.Id] = wrapper;
		return wrapper;
	}

	public bool TryGetEntity(UnitId unit, out Entity entity)
	{
		if (!unit.IsValid)
		{
			entity = Entity.Null;
			return false;
		}

		if (UnitWrapperCache.TryGetValue(unit.Value, out var wrapper) && GameContext.World != null && GameContext.World.IsAlive(((IEcsEntityWrapper)wrapper).Entity))
		{
			entity = ((IEcsEntityWrapper)wrapper).Entity;
			return true;
		}

		if (GameHost.EntityIdToUnit3D.TryGetValue(unit.Value, out var u) && GodotObject.IsInstanceValid(u) && GameContext.World != null && GameContext.World.IsAlive(u.Entity))
		{
			entity = u.Entity;
			return true;
		}

		entity = Entity.Null;
		return false;
	}

	public Unit_WasmRuntime? GetUnitWrapper(UnitId unit)
	{
		if (!unit.IsValid) return null;

		if (UnitWrapperCache.TryGetValue(unit.Value, out var wrapper) && GameContext.World != null && GameContext.World.IsAlive(((IEcsEntityWrapper)wrapper).Entity))
		{
			return wrapper;
		}

		if (GameHost.Instance != null)
		{
			var allUnits = GameHost.Instance.AllUnits;
			for (int i = 0; i < allUnits.Count; i++)
			{
				var u = allUnits[i];
				if (GodotObject.IsInstanceValid(u) && u.Entity.Id == unit.Value && GameContext.World != null && GameContext.World.IsAlive(u.Entity))
				{
					return GetUnitWrapper(u.Entity);
				}
			}
		}

		return null;
	}

	private readonly List<string> _tickExpiredCooldowns = new();
	public readonly Dictionary<int, Unit_WasmRuntime> UnitWrapperCache = new();
	public readonly HashSet<Entity> WarnedNonFinitePositions = new();
	public const int WarnedNonFinitePositionsLimit = 512;
	public bool FastBuildEnabled = false;
	public const float BaseConstructionWorkRatePerSecond = 1.0f;
	public const float LookTargetProximityDistance = 5.0f;
	public readonly List<(Entity Worker, BuildTask UpdatedTask)> PendingBuildTaskUpdates = new();
	public readonly List<Entity> CompletedBuildings = new();
	public readonly List<(Entity Entity, string? Type, Vector3 Position, Entity Target)> PendingQueuedCommands = new();

	private readonly List<string> _tickCooldownKeys = new();

	private void CooldownsQueryAction(Entity entity, ref Cooldowns cooldowns)
	{
		var dict = cooldowns.Value;
		if (GameHost.Instance != null && GameContext.CheatService.UnlimitedPowerEnabled)
		{
			dict.Clear();
			return;
		}

		_tickCooldownKeys.Clear();
		_tickExpiredCooldowns.Clear();
		foreach (var key in dict.Keys)
		{
			_tickCooldownKeys.Add(key);
		}

		for (int i = 0; i < _tickCooldownKeys.Count; i++)
		{
			string key = _tickCooldownKeys[i];
			float newTime = dict[key] - _fDelta;
			if (newTime <= 0)
			{
				_tickExpiredCooldowns.Add(key);
			}
			else
			{
				dict[key] = newTime;
			}
		}

		for (int i = 0; i < _tickExpiredCooldowns.Count; i++)
		{
			dict.Remove(_tickExpiredCooldowns[i]);
		}
	}

	public void EnqueueVFXRequest(string effectTypeId, Vector3 position, Vector3 targetPosition, float scale = 1.0f, float speed = 0f, int entityId = -1, int ownerPlayerIndex = 0)
	{
		var worldEntity = ActiveWorldEntity;
		if (worldEntity != Entity.Null && GameContext.World.IsAlive(worldEntity) && GameContext.World.Has<VFXQueue>(worldEntity))
		{
			ref var queue = ref GameContext.World.Get<VFXQueue>(worldEntity);
			queue.Requests.Add(new VFXRequest(effectTypeId, position, targetPosition, scale, speed, entityId, ownerPlayerIndex));
		}
	}

	private void PatrolArrivalQueryAction(Entity entity, ref Patrol patrol, ref Position pos)
	{
		var current = pos.Value;
		var dest = patrol.GoingToB ? patrol.PointB : patrol.PointA;
		if (Vector3.Distance(current, dest) < 1.5f)
		{
			_tickPatrolToFlip.Add((entity, patrol));
		}
	}

	private const float FollowRetargetThreshold = 1.0f;

	private void FollowQueryAction(Entity entity, ref Follow follow, ref Position pos)
	{
		if (!GameContext.World.IsAlive(follow.Target) || GameContext.World.Has<Dead>(follow.Target))
		{
			_tickFollowToStop.Add(entity);
			return;
		}

		var currentPos = pos.Value;
		var targetPos = GameContext.World.Get<Position>(follow.Target).Value;

		float dist = Vector3.Distance(currentPos, targetPos);
		if (dist <= 3.0f)
		{
			if (GameContext.World.Has<MoveTo>(entity))
			{
				_tickFollowToRemoveMoveTo.Add(entity);
			}
		}
		else
		{
			bool needsRetarget = true;
			if (GameContext.World.Has<MoveTo>(entity))
			{
				var existingTarget = GameContext.World.Get<MoveTo>(entity).Target;
				if (Vector3.Distance(existingTarget, targetPos) < FollowRetargetThreshold)
				{
					needsRetarget = false;
				}
			}
			else if (GameContext.World.Has<PathFollow>(entity))
			{
				var existingTarget = GameContext.World.Get<PathFollow>(entity).Target;
				if (Vector3.Distance(existingTarget, targetPos) < FollowRetargetThreshold)
				{
					needsRetarget = false;
				}
			}

			if (needsRetarget)
			{
				_tickFollowToMove.Add((entity, targetPos));
			}
		}
	}

	private void AttackCooldownQueryAction(Entity entity, ref Attack atk)
	{
		if (GameHost.Instance != null && GameContext.CheatService.UnlimitedPowerEnabled)
		{
			atk.CurrentCooldown = 0f;
			return;
		}

		if (atk.CurrentCooldown > 0)
		{
			atk.CurrentCooldown = Math.Max(0, atk.CurrentCooldown - _fDelta);
		}
	}

	private void HealthRegenQueryAction(Entity entity, ref Health health)
	{
		health.TimeSinceLastDamage += _fDelta;
		if (health.HpRegen <= 0f || health.Current >= health.Max)
		{
			return;
		}

		if (health.HpRegenCombatDelay > 0f && health.TimeSinceLastDamage < health.HpRegenCombatDelay)
		{
			return;
		}

		health.Current = Math.Min(health.Max, health.Current + health.HpRegen * _fDelta);
	}

	private void ManaRegenQueryAction(Entity entity, ref Mana mana, ref ManaRegen regen)
	{
		if (regen.PerSecond <= 0f || mana.Current >= mana.Max)
		{
			return;
		}

		mana.Current = Math.Min(mana.Max, mana.Current + regen.PerSecond * _fDelta);
	}

	private readonly List<string> _tickExpiredSpellCooldowns = new();
	private readonly List<string> _tickSpellCooldownKeys = new();

	private void SpellCooldownQueryAction(Entity entity, ref SpellCooldowns spellCooldowns)
	{
		var dict = spellCooldowns.Value;
		if (dict == null) return;

		if (GameHost.Instance != null && GameContext.CheatService.UnlimitedPowerEnabled)
		{
			dict.Clear();
			return;
		}

		_tickSpellCooldownKeys.Clear();
		_tickExpiredSpellCooldowns.Clear();
		foreach (var key in dict.Keys)
		{
			_tickSpellCooldownKeys.Add(key);
		}

		for (int i = 0; i < _tickSpellCooldownKeys.Count; i++)
		{
			string key = _tickSpellCooldownKeys[i];
			float newTime = dict[key] - _fDelta;
			if (newTime <= 0f)
			{
				_tickExpiredSpellCooldowns.Add(key);
			}
			else
			{
				dict[key] = newTime;
			}
		}

		for (int i = 0; i < _tickExpiredSpellCooldowns.Count; i++)
		{
			dict.Remove(_tickExpiredSpellCooldowns[i]);
		}
	}

	private void ProdQueryAction(Entity entity, ref ProductionQueue prod)
	{
		if (prod.UnitIds.Count == 0) return;

		float multiplier = (GameHost.Instance != null && GameContext.SimulationService.FastBuildEnabled) ? 10f : 1f;
		prod.CurrentProgress += _fDelta * multiplier;
		if (prod.CurrentProgress < prod.BuildTime) return;

		string unitToSpawn = prod.UnitIds[0];
		prod.UnitIds.RemoveAt(0);
		prod.CurrentProgress = 0f;

		if (prod.UnitIds.Count > 0)
		{
			string nextUnitId = prod.UnitIds[0];
			prod.BuildTime = GetProductionBuildTime != null ? GetProductionBuildTime(nextUnitId) : 5f;
		}

		SpawnProductionUnit(entity, unitToSpawn);
		_tickNeedsUiRefresh = true;
	}

	private void SpawnProductionUnit(Entity entity, string unitToSpawn)
	{
		if (!GameContext.World.Has<Position>(entity)) return;

		var buildingPos = GameContext.World.Get<Position>(entity).Value;
		Vector3 spawnOffset = GameContext.World.Has<BuildingSpawnOffset>(entity)
			? GameContext.World.Get<BuildingSpawnOffset>(entity).Value
			: new Vector3(0f, 0f, 8f);
		var spawnPos = buildingPos + spawnOffset;

		int buildingPlayerIndex = GameContext.World.Has<UnitOwnerPlayer>(entity) ? GameContext.World.Get<UnitOwnerPlayer>(entity).PlayerIndex : 0;

		_tickSpawningRequests.Add(new SpawningRequest
		{
			UnitId = unitToSpawn,
			Position = spawnPos,
			BuildingEntity = entity,
			IsFromQueue = true
		});

		OnProductionCompleted?.Invoke(buildingPlayerIndex, unitToSpawn);
	}

	private void InterpolationQueryAction(Entity entity, ref InterpolationTarget target)
	{
		if (!GameContext.World.Has<Position>(entity)) return;

		var currentPos = GameContext.World.Get<Position>(entity).Value;
		int unitPlayer = GameContext.World.Has<UnitOwnerPlayer>(entity) ? GameContext.World.Get<UnitOwnerPlayer>(entity).PlayerIndex : 0;
		bool isEnemy = GameContext.PerspectiveFilter.IsHostileToObserved(unitPlayer);

		Vector3 finalPos;
		Vector3 finalVel;

		if (isEnemy)
		{
			float factor = GetDynamicInterpolationFactor();
			finalPos = Vector3.Lerp(currentPos, target.Position, Math.Min(1f, factor * _fDelta));
			finalVel = target.Velocity;
		}
		else
		{
			CalculateFriendlyInterpolation(entity, currentPos, target.Position, target.Velocity, out finalPos, out finalVel);
		}

		GameContext.World.Set(entity, new Position(finalPos));
		GameContext.World.SetOrAdd(entity, new Velocity(finalVel));
	}

	private void CalculateFriendlyInterpolation(Entity entity, Vector3 currentPos, Vector3 targetPos, Vector3 targetVel, out Vector3 finalPos, out Vector3 finalVel)
	{
		bool hasMovementTarget = GameContext.World.Has<MoveTo>(entity) || GameContext.World.Has<Follow>(entity);
		if (hasMovementTarget && GameContext.World.Has<MovementStats>(entity))
		{
			CalculateMovementInterpolation(entity, currentPos, out finalPos, out finalVel);
		}
		else
		{
			CalculateIdleInterpolation(currentPos, targetPos, targetVel, out finalPos, out finalVel);
		}
	}

	private void CalculateMovementInterpolation(Entity entity, Vector3 currentPos, out Vector3 finalPos, out Vector3 finalVel)
	{
		var dest = GetMovementDestination(entity, currentPos);
		float distToDest = Vector3.Distance(currentPos, dest);
		float threshold = GameContext.World.Has<Follow>(entity) ? 3.0f : 0.05f;

		if (distToDest > threshold)
		{
			var stats = GameContext.World.Get<MovementStats>(entity);
			Vector3 dir = Vector3.Normalize(dest - currentPos);
			float step = Math.Min(stats.Speed * _fDelta, distToDest);
			finalPos = currentPos + dir * step;
			finalVel = dir * stats.Speed;
		}
		else
		{
			finalPos = GameContext.World.Has<Follow>(entity) ? currentPos : dest;
			finalVel = Vector3.Zero;
			if (GameContext.World.Has<MoveTo>(entity)) GameContext.World.Remove<MoveTo>(entity);
		}

		Console.WriteLine($"[CLIENT_ESTIMATED] Unit={entity.Id} Pos={finalPos} Target={dest}");
	}

	private Vector3 GetMovementDestination(Entity entity, Vector3 currentPos)
	{
		if (GameContext.World.Has<MoveTo>(entity)) return GameContext.World.Get<MoveTo>(entity).Target;

		var follow = GameContext.World.Get<Follow>(entity);
		if (GameContext.World.IsAlive(follow.Target) && GameContext.World.Has<Position>(follow.Target))
		{
			return GameContext.World.Get<Position>(follow.Target).Value;
		}

		return currentPos;
	}

	private void CalculateIdleInterpolation(Vector3 currentPos, Vector3 targetPos, Vector3 targetVel, out Vector3 finalPos, out Vector3 finalVel)
	{
		float dist = Vector3.Distance(currentPos, targetPos);
		finalPos = currentPos;
		finalVel = targetVel;

		if (dist > 2.0f) finalPos = targetPos;
		else if (dist > 0.5f) finalPos = currentPos + (targetPos - currentPos) * (_fDelta / 0.2f);
		else if (dist > 0.01f) finalPos = currentPos + (targetPos - currentPos) * (_fDelta / 0.5f);
	}

	private int GetTimeOfDayIndex()
		=> GameContext.World.GetFieldOrDefault<WorldState, int>(ActiveWorldEntity, s => s.TimeOfDayIndex);

	private float GetDynamicInterpolationFactor()
		=> GameContext.World.GetFieldOrDefault<NetworkState, float>(ActiveWorldEntity, s => s.DynamicInterpolationFactor, 10f);

	public List<Entity> GetEditorArrivedUnits() => _tickArrivedUnits;

	public void DealSpellDamageAOE(Vector3 position, float radius, float damage, Entity casterEntity, bool enemyOnly = true)
	{
		_combatService.DealSpellDamageAOE(position, radius, damage, casterEntity, enemyOnly);
	}

	public void HealAOE(Vector3 position, float radius, float healAmount, Entity casterEntity = default)
	{
		_combatService.HealAOE(position, radius, healAmount, casterEntity);
	}

	public int CountUnitsOwnedByPlayer(ReadOnlySpan<UnitId> units)
	{
		int count = 0;
		for (int i = 0; i < units.Length; i++)
		{
			var wrapper = GetUnitWrapper(units[i]);
			if (wrapper != null && !wrapper.IsDead)
				count++;
		}

		return count;
	}

	public void SetUnitRouteState(UnitId unit, int state)
	{
		var wrapper = GetUnitWrapper(unit);
		wrapper?.SetCustomData("__routeState", state);
	}

	public int GetUnitRouteState(UnitId unit)
	{
		var wrapper = GetUnitWrapper(unit);
		string? data = wrapper?.GetCustomDataString("__routeState");
		return int.TryParse(data, out int i) ? i : 0;
	}

	public void SetUnitLevel(UnitId unit, int level)
	{
		if (TryGetEntity(unit, out var entity) && GameContext.World.IsAlive(entity))
		{
			GameContext.World.SetOrAdd(entity, new Level(level));
		}
	}

	public void IssueAttackMoveOrderToPlayer(Vector3 destination, ReadOnlySpan<UnitId> units)
	{
		for (int i = 0; i < units.Length; i++)
		{
			var wrapper = GetUnitWrapper(units[i]);
			if (wrapper != null && !wrapper.IsDead && !wrapper.IsBuilding)
				wrapper.AttackMove(destination);
		}
	}

	public void IssueCastOrderAt(UnitId caster, string abilityId, Vector3 position, Action<UnitId, string, Vector3>? onUnitOrdered)
	{

		var wrapper = GetUnitWrapper(caster);
		if (wrapper != null)
		{
			wrapper.AttackMove(position);
			onUnitOrdered?.Invoke(caster, abilityId ?? "CastAt", position);
		}
	}

	public void SetAbilityAutoCast(UnitId unit, string abilityId, bool active)
	{
		if (TryGetEntity(unit, out var entity) && GameContext.World.IsAlive(entity))
		{
			var wrapper = GetUnitWrapper(unit);
			string unitTypeId = wrapper?.UnitId ?? string.Empty;
			var abilities = GameContext.RegistryService.GetUnitTypeAbilities(unitTypeId);
			int slotIdx = -1;
			for (int i = 0; i < abilities.Count; i++)
			{
				if (string.Equals(abilities[i], abilityId, StringComparison.OrdinalIgnoreCase) ||
					string.Equals(Path.GetFileNameWithoutExtension(abilities[i]), Path.GetFileNameWithoutExtension(abilityId), StringComparison.OrdinalIgnoreCase))
				{
					slotIdx = i;
					break;
				}
			}

			uint bit = (slotIdx >= 0 && slotIdx < 32) ? (1u << slotIdx) : 1u;
			if (GameContext.World.Has<AutoCastState>(entity))
			{
				ref var ac = ref GameContext.World.Get<AutoCastState>(entity);
				if (active) ac.ActiveAbilityBitmask |= bit;
				else ac.ActiveAbilityBitmask &= ~bit;
				ac.Enabled = (ac.ActiveAbilityBitmask != 0);
			}
			else if (active)
			{
				GameContext.World.Add(entity, new AutoCastState(true, bit));
			}
		}
	}

	public void EnableUnitAutoCombat(UnitId unit, bool enabled)
	{
		if (TryGetEntity(unit, out var entity) && GameContext.World.IsAlive(entity))
		{
			if (enabled)
			{
				GameContext.World.SetOrAdd(entity, new AutoCombatState(true));
			}
			else
			{
				if (GameContext.World.Has<AutoCombatState>(entity))
				{
					GameContext.World.Remove<AutoCombatState>(entity);
				}
			}
		}
	}

	private void OnAutoCastFriendlyCandidateFound(in MovementAndPathfindingService.SpatialGridEntry entry)
	{
		Entity fEnt = entry.Entity;
		if (!GameContext.World.IsAlive(fEnt) || GameContext.World.Has<Dead>(fEnt) || !GameContext.World.Has<Health>(fEnt) || !GameContext.World.Has<Owner>(fEnt)) return;

		Owner fOwner = GameContext.World.Get<Owner>(fEnt);
		if (fOwner.PlayerEntity == _autoCastCasterOwnerEntity)
		{
			Health fHp = GameContext.World.Get<Health>(fEnt);
			if (fHp.Current < fHp.Max)
			{
				float dist = Vector3.Distance(_autoCastCasterPos, entry.Position);
				if (dist <= _autoCastRange && fHp.Current < _autoCastBestFriendlyHp)
				{
					_autoCastBestFriendlyHp = fHp.Current;
					_autoCastBestFriendlyEntity = fEnt;
					_autoCastBestFriendlyPos = entry.Position;
				}
			}
		}
	}

	private void OnAutoCastEnemyCandidateFound(in MovementAndPathfindingService.SpatialGridEntry entry)
	{
		Entity eEnt = entry.Entity;
		if (!GameContext.World.IsAlive(eEnt) || GameContext.World.Has<Dead>(eEnt) || !GameContext.World.Has<Owner>(eEnt)) return;

		Owner eOwner = GameContext.World.Get<Owner>(eEnt);
		if (eOwner.PlayerEntity != _autoCastCasterOwnerEntityEnemyCheck)
		{
			int targetPlayerIndex = GameContext.World.Has<UnitOwnerPlayer>(eEnt) ? GameContext.World.Get<UnitOwnerPlayer>(eEnt).PlayerIndex : 0;
			if (NetworkService.ArePlayerIndicesEnemies(_autoCastCasterPlayerIndex, targetPlayerIndex))
			{
				float dist = Vector3.Distance(_autoCastCasterPos, entry.Position);
				if (dist <= _autoCastClosestDist)
				{
					_autoCastClosestDist = dist;
					_autoCastBestEnemyEntity = eEnt;
					_autoCastBestEnemyPos = entry.Position;
				}
			}
		}
	}

	private void AutoCastQueryAction(Entity entity, ref AutoCastState autoCast, ref Position pos, ref DefinitionId defId, ref Owner owner)
	{
		if (!autoCast.Enabled || autoCast.ActiveAbilityBitmask == 0) return;

		// Stagger auto-cast checks per entity across ticks so idle units do not all scan simultaneously
		if ((entity.Id + _tickCount) % 4 != 0) return;

		if (GameContext.World.Has<BuffState>(entity))
		{
			var buffs = GameContext.World.Get<BuffState>(entity).Value;
			if (buffs.ContainsKey("silence") || buffs.ContainsKey("stun")) return;
		}

		if (GameContext.World.Has<Buffs>(entity))
		{
			var buffs = GameContext.World.Get<Buffs>(entity).Value;
			if (buffs.ContainsKey("silence") || buffs.ContainsKey("stun")) return;
		}

		string unitTypeId = defId.Value;
		var abilities = GameContext.RegistryService.GetUnitTypeAbilities(unitTypeId);
		if (abilities.Count == 0) return;

		var unitWrapper = GetUnitWrapper(entity);
		var casterOwnerEntity = owner.PlayerEntity;

		for (int i = 0; i < abilities.Count && i < 32; i++)
		{
			if ((autoCast.ActiveAbilityBitmask & (1u << i)) == 0) continue;

			string abilityId = abilities[i];
			if (GetAbilityCooldown(unitWrapper, abilityId) > 0f) continue;

			float manaCost = GameContext.RegistryService.GetAbilityManaCostForUnit(unitWrapper, abilityId);
			if (GameContext.World.Has<Mana>(entity) && GameContext.World.Get<Mana>(entity).Current < manaCost) continue;

			var def = GameContext.RegistryService.GetAbilityDefinition(abilityId);
			if (def == null) continue;

			Vector3 casterPos = pos.Value;
			float range = def.TargetRange > 0f ? def.TargetRange : (def.AreaOfEffectRadius > 0f ? def.AreaOfEffectRadius : 12f);

			Vector3 targetPos = Vector3.Zero;
			bool targetFound = false;

			if (def.Healing > 0f)
			{
				_autoCastCasterOwnerEntity = casterOwnerEntity;
				_autoCastBestFriendlyEntity = Entity.Null;
				_autoCastBestFriendlyHp = float.MaxValue;
				_autoCastBestFriendlyPos = Vector3.Zero;
				_autoCastRange = range;
				_autoCastCasterPos = casterPos;

				_movementService.QueryUnitsInRadius(casterPos, range, _autoCastFriendlyCallback);

				if (_autoCastBestFriendlyEntity != Entity.Null)
				{
					targetPos = _autoCastBestFriendlyPos;
					targetFound = true;
				}
			}
			else if (def.Damage > 0f || !def.IsInstant)
			{
				int casterPlayerIndex = GameContext.World.Has<UnitOwnerPlayer>(entity) ? GameContext.World.Get<UnitOwnerPlayer>(entity).PlayerIndex : 0;

				_autoCastCasterOwnerEntityEnemyCheck = casterOwnerEntity;
				_autoCastCasterPlayerIndex = casterPlayerIndex;
				_autoCastBestEnemyEntity = Entity.Null;
				_autoCastClosestDist = range;
				_autoCastBestEnemyPos = Vector3.Zero;
				_autoCastCasterPos = casterPos;

				_movementService.QueryUnitsInRadius(casterPos, range, _autoCastEnemyCallback);

				if (_autoCastBestEnemyEntity != Entity.Null)
				{
					targetPos = _autoCastBestEnemyPos;
					targetFound = true;
				}
			}
			else
			{
				targetPos = casterPos;
				targetFound = true;
			}

			if (targetFound)
			{
				if (GameContext.World.Has<Mana>(entity) && manaCost > 0f)
				{
					ref var mana = ref GameContext.World.Get<Mana>(entity);
					mana.Current = Math.Max(0f, mana.Current - manaCost);
				}

				SetAbilityCooldown(unitWrapper, abilityId, def.Cooldown > 0f ? def.Cooldown : 5f);
				CastAbility(unitWrapper, abilityId, targetPos, def, null, (d, p) => GameContext.FXService?.SpawnAbilityEffect(GameHost.Instance, d, p));
				break;
			}
		}
	}

	public void SetPlayerComputerControlled(int playerIndex, bool isProxy)
	{
		if (TryGetScriptPlayersState(out var playersState))
		{
			if (playerIndex >= 0 && playerIndex < playersState.Players.Length)
			{
				playersState.Players[playerIndex].IsComputer = isProxy;
				GameContext.World.GetBuffer<ScriptPlayer>().Set(playerIndex, in playersState.Players[playerIndex]);
			}
		}
	}

	public void SetUnitOwner(UnitId unit, int playerIndex)
	{
		var wrapper = GetUnitWrapper(unit);
		if (wrapper != null) wrapper.Player = playerIndex;
	}

	public void CastAbility(UnitId unit, string abilityId, Vector3 targetPosition, AbilityDefinition def, Action<UnitId, string, Vector3>? onSpellCast, Action<AbilityDefinition, Godot.Vector3>? spawnFx)
	{
		var godotPos = new Godot.Vector3(targetPosition.X, targetPosition.Y, targetPosition.Z);
		TryGetEntity(unit, out var casterEnt);

		if (def != null)
		{
			spawnFx?.Invoke(def, godotPos);
			if (def.Damage > 0f)
			{
				float aoe = def.AreaOfEffectRadius > 0f ? def.AreaOfEffectRadius : 4.0f;
				DealSpellDamageAOE(targetPosition, aoe, def.Damage, casterEnt);
			}
			else if (def.Healing > 0f)
			{
				float aoe = def.AreaOfEffectRadius > 0f ? def.AreaOfEffectRadius : 4.0f;
				HealAOE(targetPosition, aoe, def.Healing, casterEnt);
			}
			else
			{
				onSpellCast?.Invoke(unit, abilityId, targetPosition);
			}
		}
		else
		{
			onSpellCast?.Invoke(unit, abilityId, targetPosition);
		}
	}

	public float GetAbilityCooldown(UnitId unit, string abilityId)
	{
		if (TryGetEntity(unit, out var entity) && GameContext.World.IsAlive(entity))
		{
			if (GameContext.World.Has<Cooldowns>(entity))
			{
				var cds = GameContext.World.Get<Cooldowns>(entity).Value;
				if (cds.TryGetValue(abilityId, out var val))
				{
					return val;
				}
			}

			if (GameContext.World.Has<SpellCooldowns>(entity))
			{
				var scds = GameContext.World.Get<SpellCooldowns>(entity).Value;
				if (scds != null && scds.TryGetValue(abilityId, out var val))
				{
					return val;
				}
			}
		}

		return 0f;
	}

	public void SetAbilityCooldown(UnitId unit, string abilityId, float cooldown)
	{
		if (TryGetEntity(unit, out var entity) && GameContext.World.IsAlive(entity))
		{
			Dictionary<string, float> dict;
			if (GameContext.World.Has<Cooldowns>(entity))
			{
				dict = GameContext.World.Get<Cooldowns>(entity).Value;
			}
			else
			{
				dict = new Dictionary<string, float>(StringComparer.OrdinalIgnoreCase);
				GameContext.World.Add(entity, new Cooldowns(dict));
			}

			dict[abilityId] = cooldown;

			if (GameContext.World.Has<SpellCooldowns>(entity))
			{
				var scd = GameContext.World.Get<SpellCooldowns>(entity).Value;
				if (scd != null) scd[abilityId] = cooldown;
			}
			else
			{
				var scdDict = new Dictionary<string, float>(StringComparer.OrdinalIgnoreCase) { [abilityId] = cooldown };
				GameContext.World.Add(entity, new SpellCooldowns(scdDict));
			}
		}
	}

	public void SetAbilityCooldown(Unit_WasmRuntime unit, string abilityId, float cooldown)
	{
		SetAbilityCooldown((UnitId)unit.UniqueId, abilityId, cooldown);
	}

	public void CastAbility(Unit_WasmRuntime unit, string abilityId, Vector3 targetPosition, AbilityDefinition def, Action<UnitId, string, Vector3>? onSpellCast, Action<AbilityDefinition, Godot.Vector3>? spawnFx)
	{
		CastAbility((UnitId)unit.UniqueId, abilityId, targetPosition, def, onSpellCast, spawnFx);
	}

	public void RemoveBuff(UnitId unit, string buffId)
	{
		var wrapper = GetUnitWrapper(unit);
		wrapper?.RemoveBuff(buffId);
	}

	public IEnumerable<string> GetModifiers(UnitId unit)
	{
		var wrapper = GetUnitWrapper(unit);
		return wrapper?.GetModifiers() ?? Array.Empty<string>();
	}

	public void SpawnProjectile(string projectileTypeId, Vector3 start, Vector3 target, float speed, Action<string, Vector3, Vector3, float> spawnView)
	{
		if (GameContext.ReplayService != null && GameContext.ReplayService.IsRecording)
		{
			GameContext.ReplayService.RecordProjectile(projectileTypeId, start, target);
		}

		spawnView?.Invoke(projectileTypeId, start, target, speed);
	}

	public string TrainBotProfile(string mapName, int generations, int populationSize, int matchesPerEvaluation, string genre)
	{
		var genreProvider = AiGenreRegistry.Get(genre);
		var trainer = new SelfPlayTrainer();
		var profile = trainer.TrainSelfPlay(mapName, generations, populationSize, matchesPerEvaluation, genreProvider: genreProvider);
		return profile.ToJson();
	}

	public (float Radius, Vector3 PropPos) DomainDepleteProp(Entity propEntity, Vector3 fallbackPos)
	{
		float radius = GameContext.World.Has<CollisionRadius>(propEntity) ? GameContext.World.Get<CollisionRadius>(propEntity).Value : 1.0f;
		var propPos = GameContext.World.Has<Position>(propEntity) ? GameContext.World.Get<Position>(propEntity).Value : fallbackPos;
		if (GameContext.World.IsAlive(propEntity))
		{
			GameContext.World.Destroy(propEntity);
		}
		return (radius, propPos);
	}

	public bool ExecuteMoveCommandDomain(Entity entity, Vector3 targetPos)
	{
		var moveTo = new MoveTo(targetPos);
		GameContext.World.SetOrAdd(entity, moveTo);
		return true;
	}

	public bool ExecuteAttackCommandDomain(Entity entity, Entity targetEntity)
	{
		if (targetEntity == Entity.Null || !GameContext.World.IsAlive(targetEntity)) return false;
		var attackTarget = new AttackTarget(targetEntity);
		GameContext.World.SetOrAdd(entity, attackTarget);
		return true;
	}

	public bool ExecuteAttackMoveCommandDomain(Entity entity, Vector3 targetPos)
	{
		var attackMove = new AttackMove(targetPos);
		GameContext.World.SetOrAdd(entity, attackMove);

		var moveTo = new MoveTo(targetPos);
		GameContext.World.SetOrAdd(entity, moveTo);
		return true;
	}

	public bool ExecuteFollowCommandDomain(Entity entity, Entity targetEntity)
	{
		if (targetEntity == Entity.Null || !GameContext.World.IsAlive(targetEntity)) return false;

		var follow = new Follow(targetEntity);
		GameContext.World.SetOrAdd(entity, follow);
		return true;
	}

	public bool ExecutePatrolCommandDomain(Entity entity, Vector3 targetPos)
	{
		var unitPos = GameContext.World.Has<Position>(entity) ? GameContext.World.Get<Position>(entity).Value : Vector3.Zero;
		var patrol = new Patrol(unitPos, targetPos);
		GameContext.World.SetOrAdd(entity, patrol);

		var moveTo = new MoveTo(targetPos);
		GameContext.World.SetOrAdd(entity, moveTo);
		return true;
	}

	public bool ExecuteGatherCommandDomain(Entity entity, Vector3 targetPos, Entity targetEntity)
	{
		if (targetEntity == Entity.Null || !GameContext.World.IsAlive(targetEntity)) return false;

		string propId = GameContext.World.Has<PropIdentity>(targetEntity)
			? GameContext.World.Get<PropIdentity>(targetEntity).PropId
			: (GameContext.World.Has<DefinitionId>(targetEntity) ? GameContext.World.Get<DefinitionId>(targetEntity).Value : "");

		string? resType = propId switch
		{
			"goldmine" => "gold",
			"tree" => "wood",
			"rock" => "stone",
			_ => null
		};

		if (resType == null) return false;

		var gatherer = new Gatherer(resType, targetEntity);
		GameContext.World.SetOrAdd(entity, gatherer);

		var moveTo = new MoveTo(targetPos);
		GameContext.World.SetOrAdd(entity, moveTo);
		return true;
	}

	public bool ExecuteFallbackCommandDomain(Entity entity, string? commandType, Vector3 targetPos, Entity targetEntity, Action<Entity, string, Vector3> assignBuildTaskCallback)
	{
		if (targetEntity != Entity.Null && GameContext.World.IsAlive(targetEntity) && GameContext.World.Has<ConstructionState>(targetEntity))
		{
			var cState = GameContext.World.Get<ConstructionState>(targetEntity);
			var newTask = new BuildTask(targetEntity, cState.TotalBuildTime)
			{
				Progress = cState.Progress
			};
			GameContext.World.SetOrAdd(entity, newTask);

			var moveTo = new MoveTo(targetPos);
			GameContext.World.SetOrAdd(entity, moveTo);
			return true;
		}

		if (!string.IsNullOrEmpty(commandType) && (GameContext.RegistryService.UnitRegistry.ContainsKey(commandType) || GameContext.RegistryService.BuildingRegistry.ContainsKey(commandType)))
		{
			assignBuildTaskCallback(entity, commandType, targetPos);
			return true;
		}

		return false;
	}

	public void TriggerVictoryDomain()
	{
		ECSContext.IsGameOver = true;
	}

	public void TriggerDefeatDomain()
	{
		ECSContext.IsGameOver = true;
	}

	public string GetPlayerNameDomain(int playerIndex)
	{
		if (playerIndex == 0) return "Neutral";
		if (TryGetScriptPlayersState(out var playersState) && playerIndex >= 0 && playerIndex < playersState.Players.Length)
		{
			return playersState.Players[playerIndex].Name;
		}
		return $"Player {playerIndex}";
	}

	public bool IsPlayerActiveDomain(int playerIndex)
	{
		if (TryGetScriptPlayersState(out var playersState) && playerIndex >= 0 && playerIndex < playersState.Players.Length)
		{
			return playersState.Players[playerIndex].Active;
		}
		return false;
	}

	public float GetPlayerGoldDomain(int playerIndex)
	{
		if (TryGetScriptPlayersState(out var playersState) && playerIndex >= 0 && playerIndex < playersState.Players.Length)
		{
			return playersState.Players[playerIndex].Gold;
		}
		return 0f;
	}

	public void SetPlayerGoldDomain(int playerIndex, float amount, Action<int, float> syncResourceAction)
	{
		if (TryGetScriptPlayersState(out var playersState) && playerIndex >= 0 && playerIndex < playersState.Players.Length)
		{
			playersState.Players[playerIndex].Gold = Math.Max(0f, amount);
			GameContext.World.GetBuffer<ScriptPlayer>().Set(playerIndex, in playersState.Players[playerIndex]);
		}

		syncResourceAction(playerIndex, amount);
	}

	public int GetPlayerKillsDomain(int playerIndex)
	{
		if (TryGetScriptPlayersState(out var playersState) && playerIndex >= 0 && playerIndex < playersState.Players.Length)
		{
			return playersState.Players[playerIndex].KillCount;
		}
		return 0;
	}

	public void SetPlayerKillsDomain(int playerIndex, int kills)
	{
		if (TryGetScriptPlayersState(out var playersState) && playerIndex >= 0 && playerIndex < playersState.Players.Length)
		{
			playersState.Players[playerIndex].KillCount = kills;
			GameContext.World.GetBuffer<ScriptPlayer>().Set(playerIndex, in playersState.Players[playerIndex]);
		}
	}
}
