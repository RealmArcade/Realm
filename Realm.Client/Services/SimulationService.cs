using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using Realm.Ecs;
using Godot;
using Realm.Client.Core;
using Realm.Client.Network;
using Realm.Client.UI;
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
using UnitId = Realm.Ecs.UnitId;
using Vector3 = System.Numerics.Vector3;
using Realm.Shared.Abilities;

namespace Realm.Client.Services;

public class SimulationService
{
	/// <summary>
	/// The world this service ticks. The simulation is only driven while a world exists, and
	/// every member here is a tick path, so reaching one without a world is a programming
	/// error; stating that once keeps the fifty reads below from each re-proving it.
	/// </summary>
	public static EcsWorld World => GameContext.World
		?? throw new InvalidOperationException("SimulationService used before a world existed.");

	public void ApplySpellEffectDomain(string spellId, Vector3 position, Entity casterEnt)
	{
		GameContext.RegistryService.AbilityDefinitions.TryGetValue(spellId, out var def);
		if (def == null) return;

		// A script's ability goes through the same executor the command card uses, so a spell triggered from map code
		// cannot skip buffs, shields, scaling or summons.
		if (AbilityEffectService.HasEffects(def))
		{
			AbilityEffectService.Execute(casterEnt, position, def, position);
		}
	}

	public void RegisterBuffModifierDomain(string buffId, string statName, bool isPercentage, float value)
	{
		Realm.Ecs.Common.StatKeys.WarnIfUnknown($"buff '{buffId}'", statName);
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
		if (TryGetEntity(unit, out var entity) && World.IsAlive(entity))
		{
			World.SetOrAdd(entity, new RotationY(facingRadians));
			if (World.Has<LatestDescription>(entity))
			{
				ref var interp = ref World.Get<LatestDescription>(entity);
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
		if (TryGetEntity(unit, out var entity) && World.IsAlive(entity))
		{
			if (immune)
			{
				if (!World.Has<SpellImmune>(entity))
					World.Add(entity, new SpellImmune());
			}
			else
			{
				if (World.Has<SpellImmune>(entity))
					World.Remove<SpellImmune>(entity);
			}
		}
	}

	private readonly BuildQueueProcessor _buildQueue;
	private readonly PresentationTransform _presentation;
	private readonly UnitLookup _units;
	private readonly UnitAbilityCommands _abilities;

	public void DomainProcessCompletedBuilding(Entity buildingEntity, Action<Entity>? viewCallback, Action<Entity>? notifyCallback)
	{
		_buildQueue.DomainProcessCompletedBuilding(buildingEntity, viewCallback, notifyCallback);
	}

	public void RemoveConstructionComponents(Entity buildingEntity)
	{
		_buildQueue.RemoveConstructionComponents(buildingEntity);
	}

	public void ProcessBuildQueueEntity(Entity entity)
	{
		_buildQueue.ProcessBuildQueueEntity(entity);
	}

	public bool HasActiveTask(Entity entity)
	{
		return _buildQueue.HasActiveTask(entity);
	}

	public void ProcessPendingQueuedCommands(Func<Entity, string?, Vector3, Entity, bool> executeCommandCallback)
	{
		_buildQueue.ProcessPendingQueuedCommands(executeCommandCallback);
	}

	public void ProcessPendingCommand((Entity Entity, string? Type, Vector3 Position, Entity Target) cmd, Func<Entity, string?, Vector3, Entity, bool> executeCommandCallback)
	{
		_buildQueue.ProcessPendingCommand(cmd, executeCommandCallback);
	}

	public void ClearQueueComponent(Entity entity)
	{
		_buildQueue.ClearQueueComponent(entity);
	}

	public void ProcessRemainingQueue(Entity entity, bool lastSuccess, Func<Entity, string?, Vector3, Entity, bool> executeCommandCallback)
	{
		_buildQueue.ProcessRemainingQueue(entity, lastSuccess, executeCommandCallback);
	}










	private Entity _resolvedWorldEntity = Entity.Null;

	private Entity ActiveWorldEntity
	{
		get
		{
			if (_resolvedWorldEntity == Entity.Null || !World.IsAlive(_resolvedWorldEntity))
			{
				if (GameContext.Simulation.WorldEntity != Entity.Null && World.IsAlive(GameContext.Simulation.WorldEntity))
				{
					_resolvedWorldEntity = GameContext.Simulation.WorldEntity;
				}
				else
				{
					var worldQuery = QueryCache.AllTerrainStateQuery;
					World.Query(in worldQuery, entity => _resolvedWorldEntity = entity);
				}
			}

			return _resolvedWorldEntity;
		}
	}

	private readonly MovementAndPathfindingService _movementService;
	private readonly CombatAndDamageService _combatService;
	private readonly ResourceEconomyService _economyService;

	private float _fDelta;

	private readonly List<Entity> _tickArrivedUnits = new();


	private EntityCallback<LatestDescription> _presentationQueryDelegate = null!;


	public Action<Vector3, Vector3> OnArrowProjectileRequested = null!;
	public Action<Vector3, Vector3, string?, Entity>? OnWeaponProjectileRequested;
	public Action<Entity> OnDamageFlashRequested = null!;
	public Action<Vector3, Vector3> OnHealEffectRequested = null!;
	public Action<Entity> OnHealFlashRequested = null!;
	public Action<Entity, Entity, float> OnUnitDamagedCallback = null!;
	public Action<Entity, Entity, float>? OnUnitHealedCallback;
	public Action<Entity, Entity>? OnUnitAttackedCallback;
	public Action<int, string> OnUnderAttackAlertRequested = null!;
	public Action<Entity> OnKillUnitRequested = null!;
	public Action<string, Vector3, Entity, bool>? OnSpawnUnitFromProductionRequested;
	public Action<Entity>? OnClearUnitOrdersRequested;
	public Action<Entity> OnStopGatheringMovementRequested = null!;
	public Action OnUiRefreshRequested = null!;
	public Action<Entity> OnPropDepleted = null!;

	private readonly PlayerRelationshipTable _relationships;

	public void SetPlayerTeam(int playerIndex, int teamIndex)
	{
		_relationships.SetPlayerTeam(playerIndex, teamIndex);
	}

	public string GetPlayerTeamName(int playerIndex)
	{
		return _relationships.GetPlayerTeamName(playerIndex);
	}

	public void SetPlayersAllied(int playerIndex, int otherPlayerIndex, bool allied)
	{
		_relationships.SetPlayersAllied(playerIndex, otherPlayerIndex, allied);
	}

	public void SetPlayerRelationship(int playerIndex, int otherPlayerIndex, PlayerRelationship relationship)
	{
		_relationships.SetPlayerRelationship(playerIndex, otherPlayerIndex, relationship);
	}

	public PlayerRelationship GetPlayerRelationship(int playerIndex, int otherPlayerIndex)
	{
		return _relationships.GetPlayerRelationship(playerIndex, otherPlayerIndex);
	}

	public bool ArePlayersAllied(int playerIndex, int otherPlayerIndex)
	{
		return _relationships.ArePlayersAllied(playerIndex, otherPlayerIndex);
	}

	public bool ArePlayersEnemies(int playerIndex, int otherPlayerIndex)
	{
		return _relationships.ArePlayersEnemies(playerIndex, otherPlayerIndex);
	}

	public bool RelationshipsAreAuthoritative => _relationships.RelationshipsAreAuthoritative;

	public void MarkRelationshipsAuthoritative()
	{
		_relationships.MarkRelationshipsAuthoritative();
	}

	public void RestartRelationshipDerivation()
	{
		_relationships.RestartRelationshipDerivation();
	}

	public void RecalculatePlayerRelationships()
	{
		_relationships.RecalculatePlayerRelationships();
	}

	public void SetPlayerActiveDomain(int playerIndex, bool active)
	{
		_relationships.SetPlayerActiveDomain(playerIndex, active);
	}



	public Vector3 GetPlayerStartLocation(int playerIndex)
	{
		return Vector3.Zero;
	}

	public static bool HasScriptPlayersState()
	{
		var worldEntity = GameContext.Simulation.WorldEntity;
		return GameContext.World != null && worldEntity != Entity.Null && World.IsAlive(worldEntity) && World.Has<ScriptPlayersState>(worldEntity);
	}


	public int GetPlayerTeam(int playerIndex)
	{
		if (playerIndex >= 0 && playerIndex < 25 && GameContext.World != null)
		{
			var buffer = World.GetBuffer<ScriptPlayer>();
			if (buffer != null)
			{
				return buffer.Get(playerIndex).Team;
			}
		}

		if (HasScriptPlayersState())
		{
			ref var playersState = ref World.Get<ScriptPlayersState>(GameContext.Simulation.WorldEntity);
			if (playersState.IsValidSlot(playerIndex)) return playersState.Get(playerIndex).Team;
		}

		if (GameContext.Lobby != null && GameContext.Lobby.PlayerList != null)
		{
			var list = GameContext.Lobby.PlayerList;
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
	public Action<int, string, float> OnResourceDepositedForPlayer = null!;
	public Action<int, string> OnProductionCompleted = null!;
	public Func<string, float> GetProductionBuildTime = null!;

	private bool _positionWriterInvariantReported;

	public SimulationService()
	{
		_abilities = new UnitAbilityCommands(unit => TryGetEntity(unit, out var e) ? e : Entity.Null, GetUnitWrapper);
		_units = new UnitLookup(UnitWrapperCache);
		_presentation = new PresentationTransform();
		_bots = new PlayerBotDirector((pIdx, actionId, intent, pos) => OnBotCustomActionExecuted?.Invoke(pIdx, actionId, intent, pos, string.Empty));
		_destruction = new UnitDestruction(UnitWrapperCache);
		_buildQueue = new BuildQueueProcessor(PendingQueuedCommands);
		_relationships = new PlayerRelationshipTable(GetPlayerTeam, () => ActiveWorldEntity);
		_movementService = GameContext.Simulation.MovementPathfindingService;
		_movementService.SimulateEntityFilter = ShouldSimulateEntity;
		_movementService.PositionWrittenSink = entity =>
		{
			PositionWriteTrace.Mark(entity, PositionWriter.Movement);

			// The planning pass runs with positions withheld so that it cannot be a second writer of Position beside the
			// presentation step, which places every replicated unit from the descriptions it was sent. If this reports,
			// some path has started advancing a replicated unit's own route locally and the units it touches are pinned
			// where their descriptions last put them.
			if (!_positionWriterInvariantReported && GameContext.Host != null && GameContext.Host.IsMultiplayerActive())
			{
				_positionWriterInvariantReported = true;
				Godot.GD.PushWarning("[Simulation] The movement pass wrote a position for a replicated unit; the presentation step is the only writer of Position on a client.");
			}
		};
		_movementService.WarningSink = message => Godot.GD.PushWarning(message);
		_combatService = GameContext.Simulation.CombatDamageService;
		_combatService.UnlimitedPowerProvider = () => GameContext.Host != null && GameContext.CheatService.UnlimitedPowerEnabled;
		_combatService.AuthoritativeProvider = () => false;
		_combatService.SimulateEntityFilter = ShouldSimulateEntity;
		_combatService.ItemGrantedWeaponsProvider = itemId => GameContext.RegistryService?.ItemRegistry.TryGetValue(itemId, out var itemMeta) == true ? itemMeta.GrantedWeapons : null;
		_combatService.EnemyPredicate = ArePlayersEnemies;
		_economyService = GameContext.Simulation.ResourceEconomyService;

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
			if (World.IsAlive(ent) && World.Has<Position>(ent))
				EnqueueVFXRequest("damage_flash", World.Get<Position>(ent).Value, World.Get<Position>(ent).Value, 1.0f, 0f, ent.Id);
		};
		_combatService.OnHealEffectRequested = (p1, p2) => EnqueueVFXRequest("heal", p1, p2, 1.0f, 25f);
		_combatService.OnHealFlashRequested = ent =>
		{
			if (World.IsAlive(ent) && World.Has<Position>(ent))
				EnqueueVFXRequest("heal_flash", World.Get<Position>(ent).Value, World.Get<Position>(ent).Value, 1.0f, 0f, ent.Id);
		};
		_combatService.OnUnitDamagedCallback = (e1, e2, d, _) => OnUnitDamagedCallback?.Invoke(e1, e2, d);
		_combatService.OnUnitHealedCallback = (e1, e2, h) => OnUnitHealedCallback?.Invoke(e1, e2, h);
		_combatService.OnUnitAttackedCallback = (e1, e2) => OnUnitAttackedCallback?.Invoke(e1, e2);
		_combatService.OnUnderAttackAlertRequested = (player, id) => OnUnderAttackAlertRequested?.Invoke(player, id);
		_combatService.OnKillUnitRequested = ent => OnKillUnitRequested?.Invoke(ent);

		// A summon that times out dies like any other unit for visuals and cleanup, but the expiry itself is not a kill:
		// it has no last attacker, so no bounty or kill credit is awarded.
		OnSummonExpired = ent =>
		{
			if (!World.IsAlive(ent) || World.Has<Dead>(ent)) return;
			World.Add<Dead>(ent);
			OnKillUnitRequested?.Invoke(ent);
		};

		_economyService.OnResourceDepositedForPlayer = (player, res, amount) => OnResourceDepositedForPlayer?.Invoke(player, res, amount);
		_economyService.OnClearUnitOrdersRequested = ent => OnClearUnitOrdersRequested?.Invoke(ent);
		_economyService.OnStopGatheringMovementRequested = ent => OnStopGatheringMovementRequested?.Invoke(ent);
		_economyService.OnPropDepleted = ent => OnPropDepleted?.Invoke(ent);
		_economyService.OnResourceHarvested = ent => OnResourceHarvested?.Invoke(ent);

	}

	public void Initialize()
	{
		_presentationQueryDelegate = _presentation.PresentationQueryAction;
	}

	public bool ShouldSimulateEntity(Entity entity)
	{
		var host = GameContext.Host;
		if (host == null) return false;
		if (GameContext.World == null || !World.IsAlive(entity)) return false;

		int localPlayerIndex = host.LocalPlayerIndex;
		if (localPlayerIndex < 0) return false;

		return World.Has<UnitOwnerPlayer>(entity) && World.Get<UnitOwnerPlayer>(entity).PlayerIndex == localPlayerIndex;
	}

	public event Action<int, string, string, Vector3, string>? OnBotCustomActionExecuted;

	private readonly PlayerBotDirector _bots;

	public void SetPlayerBotProfileDomain(int playerIndex, string profileJson)
	{
		_bots.SetPlayerBotProfileDomain(playerIndex, profileJson);
	}

	public void SetBotProfile(int playerIndex, BotProfile profile)
	{
		_bots.SetBotProfile(playerIndex, profile);
	}

	public string GetPlayerBotProfileDomain(int playerIndex)
	{
		return _bots.GetPlayerBotProfileDomain(playerIndex);
	}

	public BotProfile? GetBotProfile(int playerIndex)
	{
		return _bots.GetBotProfile(playerIndex);
	}

	public void SetPlayerBotGenreDomain(int playerIndex, string genreName, string? configJson)
	{
		_bots.SetPlayerBotGenreDomain(playerIndex, genreName, configJson);
	}

	public void SetBotGenre(int playerIndex, string genreName, string? configJson = null)
	{
		_bots.SetBotGenre(playerIndex, genreName, configJson);
	}

	public string GetPlayerBotGenreDomain(int playerIndex)
	{
		return _bots.GetPlayerBotGenreDomain(playerIndex);
	}

	public string GetBotGenre(int playerIndex)
	{
		return _bots.GetBotGenre(playerIndex);
	}

	public void RegisterCustomBotDecisionDomain(int playerIndex, string actionId, string intent, float[] featureVector, Vector3 position, string payload)
	{
		_bots.RegisterCustomBotDecisionDomain(playerIndex, actionId, intent, featureVector, position, payload);
	}

	public void RegisterCustomBotDecision(int playerIndex, string actionId, string intent, float[] featureVector, Vector3 position, string payload)
	{
		_bots.RegisterCustomBotDecision(playerIndex, actionId, intent, featureVector, position, payload);
	}

	public void ClearCustomBotDecisions(int playerIndex)
	{
		_bots.ClearCustomBotDecisions(playerIndex);
	}















	public void TickEditorPhysics(float fDelta)
	{
		_fDelta = fDelta;
		_tickArrivedUnits.Clear();
		_movementService.RefreshTerrainState();
		_movementService.AdvanceElapsedTime(fDelta);
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

	public void StepMovementPlanOnly(float fDelta)
	{
		_fDelta = fDelta;
		_movementService.StepMovement(fDelta, true);
	}

	public bool TryGetInterpolatedTransform(Entity entity, float fraction, out Vector3 position, out float rotationY)
	{
		return _presentation.TryGetInterpolatedTransform(entity, fraction, out position, out rotationY);
	}

	public (Vector3 Position, float RotationY) GetInterpolatedTransform(Entity entity, float fraction)
	{
		return _presentation.GetInterpolatedTransform(entity, fraction);
	}



	public EntityCallback<Position, MoveTo, MovementStats> EditorMovementQueryDelegate => _movementService.EditorMovementQueryDelegate;
	public EntityCallback<LatestDescription> PresentationQueryDelegate => _presentationQueryDelegate;

	public void SetRuntimeReferences(
		List<Unit3D> allUnits,
		List<Prop3D> allProps,
		DefinitionManager definitionManager,
		ResourceId goldResourceId,
		ResourceId woodResourceId,
		ResourceId stoneResourceId)
	{
		_economyService.SetRuntimeReferences(definitionManager, goldResourceId, woodResourceId, stoneResourceId);
	}

	public Action<Entity>? OnSummonExpired;

	public void DisableShroud(Action viewAction)
	{
		Entity worldEntity = Entity.Null;
		var query = QueryCache.AllShroudStateQuery;
		World.Query(in query, ent => worldEntity = ent);

		if (worldEntity != Entity.Null && World.IsAlive(worldEntity) && World.Has<ShroudState>(worldEntity))
		{
			World.Mutate<ShroudState>(worldEntity, (ref ShroudState state) => { state.VisibilityMode = ShroudVisibilityMode.Revealed; });
		}

		viewAction?.Invoke();
	}

	private readonly UnitDestruction _destruction;

	public void DomainKillUnit(Entity entity, Action viewAction)
	{
		_destruction.DomainKillUnit(entity, viewAction);
	}

	public void DomainDestroyUnit(Entity entity, bool isBuilding, float radius, Vector3 unitPos, Action viewAction)
	{
		_destruction.DomainDestroyUnit(entity, isBuilding, radius, unitPos, viewAction);
	}

	public bool HandleEnemyDeathEconomyDomain(Entity unitEntity, string unitId, Entity playerEntity, int resourceCap)
	{
		return _destruction.HandleEnemyDeathEconomyDomain(unitEntity, unitId, playerEntity, resourceCap);
	}

	public (float Radius, System.Numerics.Vector3 Position) GetObstacleUncarveData(Entity unitEntity, System.Numerics.Vector3 fallbackPos)
	{
		return _destruction.GetObstacleUncarveData(unitEntity, fallbackPos);
	}

	public int GetDeathPopulationCost(Entity unitEntity, string unitId)
	{
		return _destruction.GetDeathPopulationCost(unitEntity, unitId);
	}

	public Entity GetKillerEntityDomain(Entity unitEntity)
	{
		return _destruction.GetKillerEntityDomain(unitEntity);
	}

	public void DestroyUnitEntityDomain(Entity unitEntity)
	{
		_destruction.DestroyUnitEntityDomain(unitEntity);
	}

	public void DestroyEntitiesDomain(IEnumerable<Entity> entities)
	{
		_destruction.DestroyEntitiesDomain(entities);
	}










	public Unit_WasmRuntime GetUnitWrapper(Entity entity)
	{
		return _units.GetUnitWrapper(entity);
	}

	public bool TryGetEntity(UnitId unit, out Entity entity)
	{
		return _units.TryGetEntity(unit, out entity);
	}

	public Unit_WasmRuntime? GetUnitWrapper(UnitId unit)
	{
		return _units.GetUnitWrapper(unit);
	}

	public int CountUnitsOwnedByPlayer(ReadOnlySpan<UnitId> units)
	{
		return _units.CountUnitsOwnedByPlayer(units);
	}




	public readonly Dictionary<int, Unit_WasmRuntime> UnitWrapperCache = new();
	public readonly HashSet<Entity> WarnedNonFinitePositions = new();
	public const int WarnedNonFinitePositionsLimit = 512;
	public bool FastBuildEnabled = false;
	public const float BaseConstructionWorkRatePerSecond = 1.0f;
	public const float LookTargetProximityDistance = 5.0f;
	public readonly List<(Entity Worker, BuildTask UpdatedTask)> PendingBuildTaskUpdates = new();
	public readonly List<Entity> CompletedBuildings = new();
	public readonly List<(Entity Entity, string? Type, Vector3 Position, Entity Target)> PendingQueuedCommands = new();

	public void EnqueueVFXRequest(string effectTypeId, Vector3 position, Vector3 targetPosition, float scale = 1.0f, float speed = 0f, int entityId = -1, int ownerPlayerIndex = 0)
	{
		var worldEntity = ActiveWorldEntity;
		if (worldEntity != Entity.Null && World.IsAlive(worldEntity) && World.Has<VFXQueue>(worldEntity))
		{
			ref var queue = ref World.Get<VFXQueue>(worldEntity);
			queue.Enqueue(new VFXRequest(effectTypeId, position, targetPosition, scale, speed, entityId, ownerPlayerIndex));
		}
	}


	private int GetTimeOfDayIndex()
		=> World.GetFieldOrDefault<WorldState, int>(ActiveWorldEntity, s => s.TimeOfDayIndex);

	public List<Entity> GetEditorArrivedUnits() => _tickArrivedUnits;

	public void DealResolvedDamageDomain(UnitId attacker, UnitId target, float amount, string damageType)
	{
		if (!TryGetEntity(target, out var targetEntity)) return;

		var attackerEntity = Entity.Null;
		TryGetEntity(attacker, out attackerEntity);

		_combatService.DealResolvedDamage(attackerEntity, targetEntity, amount, damageType);
	}

	public void DealSpellDamageAOE(Vector3 position, float radius, float damage, Entity casterEntity, bool enemyOnly = true)
	{
		_combatService.DealSpellDamageAOE(position, radius, damage, casterEntity, enemyOnly);
	}

	public void HealAOE(Vector3 position, float radius, float healAmount, Entity casterEntity = default)
	{
		_combatService.HealAOE(position, radius, healAmount, casterEntity);
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
		if (!TryGetEntity(unit, out var entity) || !World.IsAlive(entity)) return;

		level = Math.Max(1, level);
		World.SetOrAdd(entity, new Level(level));

		LevelService.Apply(World, entity, level, ResolveLevelGrowth(entity));
	}

	private static LevelGrowth ResolveLevelGrowth(Entity entity)
	{
		if (!World.Has<DefinitionId>(entity)) return LevelGrowth.None;

		string definitionId = World.Get<DefinitionId>(entity).Value;
		if (!GameContext.RegistryService.UnitRegistry.TryGetValue(definitionId, out var metadata)) return LevelGrowth.None;

		return metadata.LevelGrowth?.ToLevelGrowth() ?? LevelGrowth.None;
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
		_abilities.SetAbilityAutoCast(unit, abilityId, active);
	}

	public void EnableUnitAutoCombat(UnitId unit, bool enabled)
	{
		_abilities.EnableUnitAutoCombat(unit, enabled);
	}

	public void CastAbility(UnitId unit, string abilityId, Vector3 targetPosition, AbilityDefinition def, Action<UnitId, string, Vector3>? onSpellCast, Action<AbilityDefinition, Godot.Vector3>? spawnFx)
	{
		_abilities.CastAbility(unit, abilityId, targetPosition, def, onSpellCast, spawnFx);
	}

	public float GetAbilityCooldown(UnitId unit, string abilityId)
	{
		return _abilities.GetAbilityCooldown(unit, abilityId);
	}

	public void SetAbilityCooldown(UnitId unit, string abilityId, float cooldown)
	{
		_abilities.SetAbilityCooldown(unit, abilityId, cooldown);
	}

	public void SetAbilityCooldown(Unit_WasmRuntime unit, string abilityId, float cooldown)
	{
		_abilities.SetAbilityCooldown(unit, abilityId, cooldown);
	}

	public void CastAbility(Unit_WasmRuntime unit, string abilityId, Vector3 targetPosition, AbilityDefinition def, Action<UnitId, string, Vector3>? onSpellCast, Action<AbilityDefinition, Godot.Vector3>? spawnFx)
	{
		_abilities.CastAbility(unit, abilityId, targetPosition, def, onSpellCast, spawnFx);
	}



	public void SetPlayerComputerControlled(int playerIndex, bool isProxy)
	{
		if (HasScriptPlayersState())
		{
			ref var playersState = ref World.Get<ScriptPlayersState>(GameContext.Simulation.WorldEntity);
			if (playersState.IsValidSlot(playerIndex))
			{
				playersState.SetComputerControlled(playerIndex, isProxy);
				var player = playersState.Get(playerIndex);
				World.GetBuffer<ScriptPlayer>().Set(playerIndex, in player);
			}
		}
	}

	public void SetUnitOwner(UnitId unit, int playerIndex)
	{
		var wrapper = GetUnitWrapper(unit);
		if (wrapper != null) wrapper.Player = playerIndex;
	}


	public void DealResolvedDamage(Entity attacker, Entity target, float amount, string damageType)
	{
		_combatService.DealResolvedDamage(attacker, target, amount, damageType);
	}

	public void HealSingle(Entity casterEntity, Entity target, float amount)
	{
		_combatService.HealSingle(casterEntity, target, amount);
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
		float radius = World.Has<CollisionRadius>(propEntity) ? World.Get<CollisionRadius>(propEntity).Value : 1.0f;
		var propPos = World.Has<Position>(propEntity) ? World.Get<Position>(propEntity).Value : fallbackPos;
		if (World.IsAlive(propEntity))
		{
			World.Destroy(propEntity);
		}
		return (radius, propPos);
	}

	public bool ExecuteMoveCommandDomain(Entity entity, Vector3 targetPos)
	{
		var moveTo = new MoveTo(targetPos);
		World.SetOrAdd(entity, moveTo);
		return true;
	}

	public bool ExecuteAttackCommandDomain(Entity entity, Entity targetEntity)
	{
		if (targetEntity == Entity.Null || !World.IsAlive(targetEntity)) return false;
		var attackTarget = new AttackTarget(targetEntity);
		World.SetOrAdd(entity, attackTarget);
		return true;
	}

	public bool ExecuteAttackMoveCommandDomain(Entity entity, Vector3 targetPos)
	{
		var attackMove = new AttackMove(targetPos);
		World.SetOrAdd(entity, attackMove);

		var moveTo = new MoveTo(targetPos);
		World.SetOrAdd(entity, moveTo);
		return true;
	}

	public bool ExecuteFollowCommandDomain(Entity entity, Entity targetEntity)
	{
		if (targetEntity == Entity.Null || !World.IsAlive(targetEntity)) return false;

		var follow = new Follow(targetEntity);
		World.SetOrAdd(entity, follow);
		return true;
	}

	public bool ExecutePatrolCommandDomain(Entity entity, Vector3 targetPos)
	{
		var unitPos = World.Has<Position>(entity) ? World.Get<Position>(entity).Value : Vector3.Zero;
		var patrol = new Patrol(unitPos, targetPos);
		World.SetOrAdd(entity, patrol);

		var moveTo = new MoveTo(targetPos);
		World.SetOrAdd(entity, moveTo);
		return true;
	}

	public bool ExecuteGatherCommandDomain(Entity entity, Vector3 targetPos, Entity targetEntity)
	{
		if (targetEntity == Entity.Null || !World.IsAlive(targetEntity)) return false;

		string propId = World.Has<PropIdentity>(targetEntity)
			? World.Get<PropIdentity>(targetEntity).PropId
			: (World.Has<DefinitionId>(targetEntity) ? World.Get<DefinitionId>(targetEntity).Value : "");

		string? resType = propId switch
		{
			"goldmine" => "gold",
			"tree" => "wood",
			"rock" => "stone",
			_ => null
		};

		if (resType == null) return false;

		var gatherer = new Gatherer(resType, targetEntity);
		World.SetOrAdd(entity, gatherer);

		var moveTo = new MoveTo(targetPos);
		World.SetOrAdd(entity, moveTo);
		return true;
	}

	public bool ExecuteFallbackCommandDomain(Entity entity, string? commandType, Vector3 targetPos, Entity targetEntity, Action<Entity, string, Vector3> assignBuildTaskCallback)
	{
		if (targetEntity != Entity.Null && World.IsAlive(targetEntity) && World.Has<ConstructionState>(targetEntity))
		{
			var cState = World.Get<ConstructionState>(targetEntity);
			var newTask = new BuildTask(targetEntity, cState.TotalBuildTime)
			{
				Progress = cState.Progress
			};
			World.SetOrAdd(entity, newTask);

			var moveTo = new MoveTo(targetPos);
			World.SetOrAdd(entity, moveTo);
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
		GameContext.Simulation.IsGameOver = true;
	}

	public void TriggerDefeatDomain()
	{
		GameContext.Simulation.IsGameOver = true;
	}

	public string GetPlayerNameDomain(int playerIndex)
	{
		if (playerIndex == 0) return "Neutral";
		if (HasScriptPlayersState())
		{
			ref var playersState = ref World.Get<ScriptPlayersState>(GameContext.Simulation.WorldEntity);
			if (playersState.IsValidSlot(playerIndex)) return playersState.Get(playerIndex).Name;
		}
		return $"Player {playerIndex}";
	}

	public bool IsPlayerActiveDomain(int playerIndex)
	{
		if (HasScriptPlayersState())
		{
			ref var playersState = ref World.Get<ScriptPlayersState>(GameContext.Simulation.WorldEntity);
			if (playersState.IsValidSlot(playerIndex)) return playersState.Get(playerIndex).Active;
		}
		return false;
	}

	public float GetPlayerGoldDomain(int playerIndex)
	{
		if (HasScriptPlayersState())
		{
			ref var playersState = ref World.Get<ScriptPlayersState>(GameContext.Simulation.WorldEntity);
			if (playersState.IsValidSlot(playerIndex)) return playersState.Get(playerIndex).Gold;
		}
		return 0f;
	}

	public void SetPlayerGoldDomain(int playerIndex, float amount, Action<int, float> syncResourceAction)
	{
		if (HasScriptPlayersState())
		{
			ref var playersState = ref World.Get<ScriptPlayersState>(GameContext.Simulation.WorldEntity);
			if (playersState.IsValidSlot(playerIndex))
			{
				playersState.SetGold(playerIndex, Math.Max(0f, amount));
				var player = playersState.Get(playerIndex);
				World.GetBuffer<ScriptPlayer>().Set(playerIndex, in player);
			}
		}

		syncResourceAction(playerIndex, amount);
	}

	public int GetPlayerKillsDomain(int playerIndex)
	{
		if (HasScriptPlayersState())
		{
			ref var playersState = ref World.Get<ScriptPlayersState>(GameContext.Simulation.WorldEntity);
			if (playersState.IsValidSlot(playerIndex)) return playersState.Get(playerIndex).KillCount;
		}
		return 0;
	}

	public void SetPlayerKillsDomain(int playerIndex, int kills)
	{
		if (HasScriptPlayersState())
		{
			ref var playersState = ref World.Get<ScriptPlayersState>(GameContext.Simulation.WorldEntity);
			if (playersState.IsValidSlot(playerIndex))
			{
				playersState.SetKills(playerIndex, kills);
				var player = playersState.Get(playerIndex);
				World.GetBuffer<ScriptPlayer>().Set(playerIndex, in player);
			}
		}
	}
}
