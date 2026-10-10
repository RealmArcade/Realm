using Arch.Core;
using Realm.Ecs.Common;
using Realm.Ecs.Components.Combat;
using Realm.Ecs.Components.Core;
using Realm.Ecs.Components.Movement;
using Realm.Ecs.Components.Tags;
using Realm.Ecs.Services;
using System;
using System.Collections.Generic;

namespace Realm.Client.Services;

public class SimulationService
{
	private readonly WorldAccessor EcsWorldAccessor;
	private World EcsWorld => EcsWorldAccessor.Current;
	private readonly Entity _worldEntity;
	private Entity _resolvedWorldEntity;
	private Entity ActiveWorldEntity
	{
		get
		{
			if (_resolvedWorldEntity == Entity.Null || !EcsWorld.IsAlive(_resolvedWorldEntity))
			{
				if (_worldEntity != Entity.Null && EcsWorld.IsAlive(_worldEntity))
				{
					_resolvedWorldEntity = _worldEntity;
				}
				else
				{
					var worldQuery = QueryCache.AllTerrainStateQuery;
					EcsWorld.Query(in worldQuery, entity => _resolvedWorldEntity = entity);
				}
			}
			return _resolvedWorldEntity;
		}
	}
	private readonly NavMeshPathfinder _pathfinder;

	private readonly MovementAndPathfindingService _movementService;
	private readonly CombatAndDamageService _combatService;
	private readonly ResourceEconomyService _economyService;

	private float _fDelta;

	private readonly List<string> _tickExpiredBuffs = new();
	private readonly List<string> _tickBuffKeys = new();
	private readonly List<(Entity Entity, Patrol Patrol)> _tickPatrolToFlip = new();
	private readonly List<Entity> _tickFollowToStop = new();
	private readonly List<(Entity Follower, System.Numerics.Vector3 TargetPos)> _tickFollowToMove = new();
	private readonly List<Entity> _tickFollowToRemoveMoveTo = new();
	private readonly List<Entity> _tickArrivedUnits = new();
	private readonly List<(Entity Entity, PathFollow PathFollow)> _tickAddPathFollow = new();
	private readonly List<Entity> _tickEntitiesToClearOrders = new();
	private readonly List<Entity> _tickEntitiesToStopGathering = new();
	private readonly List<SpawningRequest> _tickSpawningRequests = new();
	private bool _tickNeedsUiRefresh = false;
	private readonly Dictionary<int, Realm.Ecs.AI.BotController> _botControllers = new();

	private readonly QueryDescription _buffQuery = Realm.Ecs.Common.QueryCache.AllBuffsNoneDeadQuery;
	private readonly QueryDescription _buffStateQuery = Realm.Ecs.Common.QueryCache.AllBuffStateNoneDeadQuery;
	private readonly QueryDescription _statsRecalcQuery = new QueryDescription().WithAll<Realm.Ecs.Components.Stats.Stats>().WithNone<Dead>();
	private readonly QueryDescription _patrolArrivalQuery = Realm.Ecs.Common.QueryCache.AllPatrolAndPositionNoneDeadAndAttackTargetQuery;
	private readonly QueryDescription _followQuery = Realm.Ecs.Common.QueryCache.AllFollowAndPositionNoneDeadQuery;
	private readonly QueryDescription _attackCooldownQuery = Realm.Ecs.Common.QueryCache.AllAttackQuery;
	private readonly QueryDescription _healthRegenQuery = Realm.Ecs.Common.QueryCache.AllHealthNoneDeadQuery;
	private readonly QueryDescription _manaRegenQuery = Realm.Ecs.Common.QueryCache.AllManaAndManaRegenNoneDeadQuery;
	private readonly QueryDescription _prodQuery = Realm.Ecs.Common.QueryCache.AllProductionQueueQuery;
	private readonly QueryDescription _spellCooldownQuery = Realm.Ecs.Common.QueryCache.AllSpellCooldownsQuery;
	private readonly QueryDescription _cooldownsQuery = Realm.Ecs.Common.QueryCache.AllCooldownsQuery;

	private ForEachWithEntity<Realm.Ecs.Components.Core.Buffs> _buffsQueryDelegate = null!;
	private ForEachWithEntity<Realm.Ecs.Components.Core.BuffState> _buffStateQueryDelegate = null!;
	private ForEachWithEntity<Realm.Ecs.Components.Stats.Stats> _statsRecalcQueryDelegate = null!;
	private ForEachWithEntity<Patrol, Position> _patrolArrivalQueryDelegate = null!;
	private ForEachWithEntity<Follow, Position> _followQueryDelegate = null!;
	private ForEachWithEntity<Attack> _attackCooldownQueryDelegate = null!;
	private ForEachWithEntity<Health> _healthRegenQueryDelegate = null!;
	private ForEachWithEntity<Mana, ManaRegen> _manaRegenQueryDelegate = null!;
	private ForEachWithEntity<Realm.Ecs.Components.Core.ProductionQueue> _prodQueryDelegate = null!;
	private ForEachWithEntity<InterpolationTarget> _interpolationQueryDelegate = null!;
	private ForEachWithEntity<SpellCooldowns> _spellCooldownQueryDelegate = null!;
	private ForEachWithEntity<Realm.Ecs.Components.Core.Cooldowns> _cooldownsQueryDelegate = null!;

	public Action<System.Numerics.Vector3, System.Numerics.Vector3> OnArrowProjectileRequested;
	public Action<System.Numerics.Vector3, System.Numerics.Vector3, string?, Entity>? OnWeaponProjectileRequested;
	public Action<Entity> OnDamageFlashRequested;
	public Action<System.Numerics.Vector3, System.Numerics.Vector3> OnHealEffectRequested;
	public Action<Entity> OnHealFlashRequested;
	public Action<Entity, Entity, float> OnUnitDamagedCallback;
	public Action<Entity, Entity, float>? OnUnitHealedCallback;
	public Action<Entity, Entity>? OnUnitAttackedCallback;
	public Action<string> OnUnderAttackAlertRequested;
	public Action<Entity> OnKillUnitRequested;
	public Action<string, System.Numerics.Vector3, bool, Entity, bool>? OnSpawnUnitFromProductionRequested;
	public Action<Entity>? OnClearUnitOrdersRequested;
	public Action<Entity> OnStopGatheringMovementRequested;
	public Action OnUiRefreshRequested;
	public Action<Entity> OnPropDepleted;
	public Action<Entity>? OnResourceHarvested;
	public Action<string, float> OnResourceDepositedForPlayer;
	public Action<string> OnProductionCompleted;
	public Func<string, float> GetProductionBuildTime;

	public struct SpawningRequest
	{
		public string UnitId;
		public System.Numerics.Vector3 Position;
		public bool IsEnemy;
		public Entity BuildingEntity;
		public bool IsFromQueue;
	}

	public SimulationService(WorldAccessor ecsWorldAccessor, Entity worldEntity, NavMeshPathfinder pathfinder)
	{
		EcsWorldAccessor = ecsWorldAccessor;
		_worldEntity = worldEntity;
		_pathfinder = pathfinder;

		_movementService = new MovementAndPathfindingService(ecsWorldAccessor, worldEntity, pathfinder);
		_combatService = new CombatAndDamageService(ecsWorldAccessor, () => Realm.Client.Core.GameHost.Instance != null && Realm.Client.Core.GameHost.Instance.UnlimitedPowerEnabled, pathfinder);
		_economyService = new ResourceEconomyService(ecsWorldAccessor);

		_combatService.OnArrowProjectileRequested = (p1, p2) => EnqueueVFXRequest("arrow", p1, p2, 1.0f, 40f);
		_combatService.UnitWeaponsProvider = defId => {
			if (Realm.Client.Core.GameHost.UnitRegistry.TryGetValue(defId, out var meta))
				return meta.Weapons;
			return null;
		};
		_combatService.OnWeaponProjectileRequested = (p1, p2, weaponId, targetEnt) => {
			EnqueueVFXRequest(weaponId ?? "arrow", p1, p2, 1.0f, 40f, targetEnt.Id);
			OnWeaponProjectileRequested?.Invoke(p1, p2, weaponId, targetEnt);
		};
		_combatService.OnDamageFlashRequested = ent => {
			if (EcsWorld.IsAlive(ent) && EcsWorld.Has<Position>(ent))
				EnqueueVFXRequest("damage_flash", EcsWorld.Get<Position>(ent).Value, EcsWorld.Get<Position>(ent).Value, 1.0f, 0f, ent.Id);
		};
		_combatService.OnHealEffectRequested = (p1, p2) => EnqueueVFXRequest("heal", p1, p2, 1.0f, 25f);
		_combatService.OnHealFlashRequested = ent => {
			if (EcsWorld.IsAlive(ent) && EcsWorld.Has<Position>(ent))
				EnqueueVFXRequest("heal_flash", EcsWorld.Get<Position>(ent).Value, EcsWorld.Get<Position>(ent).Value, 1.0f, 0f, ent.Id);
		};
		_combatService.OnUnitDamagedCallback = (e1, e2, d) => OnUnitDamagedCallback?.Invoke(e1, e2, d);
		_combatService.OnUnitHealedCallback = (e1, e2, h) => OnUnitHealedCallback?.Invoke(e1, e2, h);
		_combatService.OnUnitAttackedCallback = (e1, e2) => OnUnitAttackedCallback?.Invoke(e1, e2);
		_combatService.OnUnderAttackAlertRequested = id => OnUnderAttackAlertRequested?.Invoke(id);
		_combatService.OnKillUnitRequested = ent => OnKillUnitRequested?.Invoke(ent);

		_economyService.OnResourceDepositedForPlayer = (res, amount) => OnResourceDepositedForPlayer?.Invoke(res, amount);
		_economyService.OnClearUnitOrdersRequested = ent => OnClearUnitOrdersRequested?.Invoke(ent);
		_economyService.OnStopGatheringMovementRequested = ent => OnStopGatheringMovementRequested?.Invoke(ent);
		_economyService.OnPropDepleted = ent => OnPropDepleted?.Invoke(ent);
		_economyService.OnResourceHarvested = ent => OnResourceHarvested?.Invoke(ent);
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
	}

	public void TickEcs(float fDelta)
	{
		_fDelta = fDelta;
		_tickEntitiesToClearOrders.Clear();
		_tickEntitiesToStopGathering.Clear();
		_tickSpawningRequests.Clear();
		_tickAddPathFollow.Clear();
		_tickNeedsUiRefresh = false;

		if (ActiveWorldEntity != default && EcsWorld.IsAlive(ActiveWorldEntity))
		{
			if (EcsWorld.Has<WorldState>(ActiveWorldEntity))
			{
				var state = EcsWorld.Get<WorldState>(ActiveWorldEntity);
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
				EcsWorld.Set(ActiveWorldEntity, new WorldState(elapsed, index, timer, state.DayNightCycleEnabled));
			}

			if (EcsWorld.Has<CountdownState>(ActiveWorldEntity))
			{
				var countdown = EcsWorld.Get<CountdownState>(ActiveWorldEntity);
				if (countdown.Active)
				{
					float newDuration = countdown.Duration - fDelta;
					if (newDuration <= 0f)
					{
						EcsWorld.Set(ActiveWorldEntity, new CountdownState(false, 0f, countdown.Text));
					}
					else
					{
						EcsWorld.Set(ActiveWorldEntity, new CountdownState(true, newDuration, countdown.Text));
					}
				}
			}
		}

		EcsWorld.Query(in _spellCooldownQuery, _spellCooldownQueryDelegate);
		EcsWorld.Query(in _cooldownsQuery, _cooldownsQueryDelegate);

		_movementService.StepMovement(fDelta);
		_combatService.StepCombat(fDelta);
		_economyService.StepEconomy(fDelta);

		EcsWorld.Query(in _buffQuery, _buffsQueryDelegate);
		EcsWorld.Query(in _buffStateQuery, _buffStateQueryDelegate);
		EcsWorld.Query(in _statsRecalcQuery, _statsRecalcQueryDelegate);

		ProcessPatrolArrivals();
		ProcessFollowMovements();

		EcsWorld.Query(in _attackCooldownQuery, _attackCooldownQueryDelegate);
		EcsWorld.Query(in _healthRegenQuery, _healthRegenQueryDelegate);
		EcsWorld.Query(in _manaRegenQuery, _manaRegenQueryDelegate);
		EcsWorld.Query(in _prodQuery, _prodQueryDelegate);

		foreach (var (entity, pf) in _tickAddPathFollow)
		{
			if (EcsWorld.IsAlive(entity))
			{
				EcsWorld.Add(entity, pf);
			}
		}

		TickBotControllers(fDelta);

		ApplyDeferredTickCommands();
	}

	private readonly Dictionary<int, List<Realm.Ecs.AI.Affordances.GenericAffordance>> _customDecisionsPerPlayer = new();
	public event Action<int, string, string, System.Numerics.Vector3, string>? OnBotCustomActionExecuted;

	public void SetBotProfile(int playerIndex, Realm.Ecs.AI.Policy.BotProfile profile)
	{
		if (!_botControllers.TryGetValue(playerIndex, out var bot))
		{
			bot = new Realm.Ecs.AI.BotController(playerIndex, profile);
			bot.CustomActionCallback = (pIdx, actId, pos, intent) =>
			{
				OnBotCustomActionExecuted?.Invoke(pIdx, actId, intent, pos, string.Empty);
			};
			_botControllers[playerIndex] = bot;
		}
		else
		{
			bot.LoadProfile(profile);
		}
	}

	public Realm.Ecs.AI.Policy.BotProfile? GetBotProfile(int playerIndex)
	{
		return _botControllers.TryGetValue(playerIndex, out var bot) ? bot.Profile : null;
	}

	public void SetBotGenre(int playerIndex, string genreName, string? configJson = null)
	{
		var genreProvider = Realm.Ecs.AI.Genres.AiGenreRegistry.Get(genreName);
		ConfigureGenreProvider(genreProvider, configJson);
		ConfigureCustomProvider(genreName, genreProvider);
		UpdateOrCreateBotController(playerIndex, genreProvider);
	}

	private void ConfigureGenreProvider(Realm.Ecs.AI.Genres.IAiGenreProvider genreProvider, string? configJson)
	{
		if (string.IsNullOrWhiteSpace(configJson)) return;
		try
		{
			var dict = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, string>>(configJson);
			if (dict != null) genreProvider.ConfigureFromParameters(dict);
		}
		catch { }
	}

	private void ConfigureCustomProvider(string genreName, Realm.Ecs.AI.Genres.IAiGenreProvider genreProvider)
	{
		if (!string.Equals(genreName, "custom", StringComparison.OrdinalIgnoreCase)) return;
		if (genreProvider is not Realm.Ecs.AI.Genres.CustomGenreProvider customProvider) return;

		customProvider.CustomScanner = (world, pIdx, dest, ctx) =>
		{
			if (_customDecisionsPerPlayer.TryGetValue(pIdx, out var list) && list != null) dest.AddRange(list);
		};
	}

	private void UpdateOrCreateBotController(int playerIndex, Realm.Ecs.AI.Genres.IAiGenreProvider genreProvider)
	{
		if (!_botControllers.TryGetValue(playerIndex, out var bot))
		{
			var profile = genreProvider.CreateDefaultProfile(Realm.Client.Core.GameHost.Instance?.ActiveMapName ?? "GenericMap");
			bot = new Realm.Ecs.AI.BotController(playerIndex, profile, genreProvider);
			bot.CustomActionCallback = (pIdx, actId, pos, intent) =>
			{
				OnBotCustomActionExecuted?.Invoke(pIdx, actId, intent, pos, string.Empty);
			};
			_botControllers[playerIndex] = bot;
		}
		else
		{
			bot.GenreProvider = genreProvider;
			var profile = genreProvider.CreateDefaultProfile(bot.Profile.MapName);
			bot.LoadProfile(profile);
		}
	}

	public string GetBotGenre(int playerIndex)
	{
		return _botControllers.TryGetValue(playerIndex, out var bot) ? bot.GenreProvider.GenreName : "rts";
	}

	public void RegisterCustomBotDecision(int playerIndex, string actionId, string intent, float[] featureVector, System.Numerics.Vector3 position, string payload)
	{
		if (!_customDecisionsPerPlayer.TryGetValue(playerIndex, out var list))
		{
			list = new List<Realm.Ecs.AI.Affordances.GenericAffordance>();
			_customDecisionsPerPlayer[playerIndex] = list;
		}

		Realm.Ecs.AI.Affordances.CommandIntent cmdIntent = Realm.Ecs.AI.Affordances.CommandIntent.Interact;
		if (Enum.TryParse<Realm.Ecs.AI.Affordances.CommandIntent>(intent, true, out var parsedIntent))
		{
			cmdIntent = parsedIntent;
		}

		list.Add(new Realm.Ecs.AI.Affordances.GenericAffordance(
			Arch.Core.Entity.Null,
			cmdIntent,
			Arch.Core.Entity.Null,
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
		if (EcsWorld == null) return;

		for (int pIdx = 0; pIdx < 8; pIdx++)
		{
			if (!IsPlayerBot(pIdx)) continue;
			
			if (!_botControllers.TryGetValue(pIdx, out var bot))
			{
				bot = InitializeDefaultBotController(pIdx);
				_botControllers[pIdx] = bot;
			}
			bot.Tick(EcsWorld, pIdx, fDelta);
		}
	}

	private bool IsPlayerBot(int pIdx)
	{
		if (Realm.Client.Core.GameHost.Instance == null) return false;
		return ((Realm.MapAPI.IGameAPI)Realm.Client.Core.GameHost.Instance).IsPlayerComputer(pIdx);
	}

	private Realm.Ecs.AI.BotController InitializeDefaultBotController(int pIdx)
	{
		var bot = new Realm.Ecs.AI.BotController(pIdx);
		bot.CustomActionCallback = (botPIdx, actId, pos, intent) =>
		{
			OnBotCustomActionExecuted?.Invoke(botPIdx, actId, intent, pos, string.Empty);
		};
		LoadBotProfileFromDisk(bot);
		return bot;
	}

	private void LoadBotProfileFromDisk(Realm.Ecs.AI.BotController bot)
	{
		string mapName = Realm.Client.Core.GameHost.Instance?.ActiveMapName ?? "";
		string botPath = System.IO.Path.Combine(global::Godot.OS.GetUserDataDir(), $"{mapName}_bot.json");
		if (!System.IO.File.Exists(botPath)) return;
		try
		{
			var profile = Realm.Ecs.AI.Policy.BotProfile.FromJson(System.IO.File.ReadAllText(botPath));
			bot.LoadProfile(profile);
		}
		catch { }
	}

	public void TickEditorPhysics(float fDelta)
	{
		_fDelta = fDelta;
		_tickArrivedUnits.Clear();
		_tickAddPathFollow.Clear();
		_movementService.RefreshTerrainState();
	}

	public Func<System.Numerics.Vector3, float>? EditorHeightProvider
	{
		get => _movementService.EditorHeightProvider;
		set => _movementService.EditorHeightProvider = value;
	}

	public void SetDelta(float fDelta)
	{
		_fDelta = fDelta;
	}

	public ForEachWithEntity<Position, MoveTo, MovementStats> EditorMovementQueryDelegate => _movementService.EditorMovementQueryDelegate;
	public ForEachWithEntity<InterpolationTarget> InterpolationQueryDelegate => _interpolationQueryDelegate;

	public void SetRuntimeReferences(
		List<Realm.Client.Unit3D> allUnits,
		List<Realm.Client.Prop3D> allProps,
		List<Realm.Client.Unit3D> castlesList,
		DefinitionManager definitionManager,
		ResourceId goldResourceId,
		ResourceId woodResourceId,
		ResourceId stoneResourceId,
		Realm.Client.RuntimeTerrain groundTerrain)
	{
		_economyService.SetRuntimeReferences(definitionManager, goldResourceId, woodResourceId, stoneResourceId);
	}

	private void ProcessPatrolArrivals()
	{
		_tickPatrolToFlip.Clear();
		EcsWorld.Query(in _patrolArrivalQuery, _patrolArrivalQueryDelegate);
		foreach (var (entity, patrol) in _tickPatrolToFlip)
		{
			if (EcsWorld.IsAlive(entity))
			{
				var newPatrol = patrol;
				newPatrol.GoingToB = !patrol.GoingToB;
				EcsWorld.Set(entity, newPatrol);

				var dest = newPatrol.GoingToB ? newPatrol.PointB : newPatrol.PointA;
				var moveTo = new MoveTo(dest);
				EcsWorld.SetOrAdd(entity, moveTo);
			}
		}
	}

	private void ProcessFollowMovements()
	{
		_tickFollowToStop.Clear();
		_tickFollowToMove.Clear();
		_tickFollowToRemoveMoveTo.Clear();

		EcsWorld.Query(in _followQuery, _followQueryDelegate);

		foreach (var entity in _tickFollowToStop)
		{
			if (EcsWorld.IsAlive(entity))
			{
				if (EcsWorld.Has<MoveTo>(entity))
				{
					EcsWorld.Remove<MoveTo>(entity);
				}
				if (EcsWorld.Has<Follow>(entity))
				{
					EcsWorld.Remove<Follow>(entity);
				}
			}
		}

		foreach (var entity in _tickFollowToRemoveMoveTo)
		{
			if (EcsWorld.IsAlive(entity) && EcsWorld.Has<MoveTo>(entity))
			{
				EcsWorld.Remove<MoveTo>(entity);
			}
		}

		foreach (var (follower, targetPos) in _tickFollowToMove)
		{
			if (EcsWorld.IsAlive(follower))
			{
				var moveTo = new MoveTo(targetPos);
				EcsWorld.SetOrAdd(follower, moveTo);
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
			if (EcsWorld.IsAlive(ent)) OnClearUnitOrdersRequested?.Invoke(ent);
		}
	}

	private void ProcessStopGathering()
	{
		foreach (var ent in _tickEntitiesToStopGathering)
		{
			if (EcsWorld.IsAlive(ent)) OnStopGatheringMovementRequested?.Invoke(ent);
		}
	}

	private void ProcessSpawningRequests()
	{
		foreach (var req in _tickSpawningRequests)
		{
			OnSpawnUnitFromProductionRequested?.Invoke(req.UnitId, req.Position, req.IsEnemy, req.BuildingEntity, req.IsFromQueue);
		}
	}

	private void ProcessUiRefresh()
	{
		if (_tickNeedsUiRefresh) OnUiRefreshRequested?.Invoke();
	}

	private void UpdateBuffsQueryAction(Entity entity, ref Realm.Ecs.Components.Core.Buffs buffs)
	{
		var buffsDict = buffs.Value;
		_tickBuffKeys.Clear();
		_tickExpiredBuffs.Clear();
		foreach (var key in buffsDict.Keys)
		{
			_tickBuffKeys.Add(key);
		}
		for (int i = 0; i < _tickBuffKeys.Count; i++)
		{
			string key = _tickBuffKeys[i];
			float newTime = buffsDict[key] - _fDelta;
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

	private void UpdateBuffStateQueryAction(Entity entity, ref Realm.Ecs.Components.Core.BuffState buffs)
	{
		var buffsDict = buffs.Value;
		_tickBuffKeys.Clear();
		_tickExpiredBuffs.Clear();
		foreach (var key in buffsDict.Keys)
		{
			_tickBuffKeys.Add(key);
		}
		for (int i = 0; i < _tickBuffKeys.Count; i++)
		{
			string key = _tickBuffKeys[i];
			float newTime = buffsDict[key] - _fDelta;
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

	private void UpdateModifiers(Entity entity, ref Realm.Ecs.Components.Core.ModifierState modState)
	{
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
				}
				else
				{
					list[i] = new Realm.Ecs.Components.Stats.StatModifier(mod.StatTypeId, mod.Type, mod.Value, newDur);
				}
			}
		}
	}

	private void StatsRecalcQueryAction(Entity entity, ref Realm.Ecs.Components.Stats.Stats stats)
	{
		UpdateEntityModifiers(entity);

		float flatArmor = 0f, percentArmor = 1f;
		float flatAttack = 0f, percentAttack = 1f;
		float flatSpeed = 0f, percentSpeed = 1f;

		ApplyEntityModifiers(entity, ref flatArmor, ref percentArmor, ref flatAttack, ref percentAttack, ref flatSpeed, ref percentSpeed);
		ApplyEntityBuffModifiers(entity, ref flatArmor, ref percentArmor, ref flatAttack, ref percentAttack, ref flatSpeed, ref percentSpeed);

		ApplyCalculatedArmor(entity, stats.Value, flatArmor, percentArmor);
		ApplyCalculatedAttack(entity, stats.Value, flatAttack, percentAttack);
		ApplyCalculatedSpeed(entity, stats.Value, flatSpeed, percentSpeed);
	}

	private void UpdateEntityModifiers(Entity entity)
	{
		if (EcsWorld.Has<Realm.Ecs.Components.Core.ModifierState>(entity))
		{
			ref var modState = ref EcsWorld.Get<Realm.Ecs.Components.Core.ModifierState>(entity);
			UpdateModifiers(entity, ref modState);
		}
	}

	private void ApplyEntityModifiers(Entity entity, ref float flatArmor, ref float percentArmor, ref float flatAttack, ref float percentAttack, ref float flatSpeed, ref float percentSpeed)
	{
		if (!EcsWorld.Has<Realm.Ecs.Components.Core.ModifierState>(entity)) return;
		var modState = EcsWorld.Get<Realm.Ecs.Components.Core.ModifierState>(entity);
		var list = modState.Value;
		for (int i = 0; i < list.Count; i++) ApplyMod(list[i], ref flatArmor, ref percentArmor, ref flatAttack, ref percentAttack, ref flatSpeed, ref percentSpeed);
	}

	private void ApplyEntityBuffModifiers(Entity entity, ref float flatArmor, ref float percentArmor, ref float flatAttack, ref float percentAttack, ref float flatSpeed, ref float percentSpeed)
	{
		if (!EcsWorld.Has<Realm.Ecs.Components.Core.BuffState>(entity)) return;
		var buffState = EcsWorld.Get<Realm.Ecs.Components.Core.BuffState>(entity);
		foreach (var buffKey in buffState.Value.Keys)
		{
			if (!Realm.Ecs.Common.BuffRegistry.BuffModifiers.TryGetValue(buffKey, out var mods)) continue;
			for (int i = 0; i < mods.Count; i++) ApplyMod(mods[i], ref flatArmor, ref percentArmor, ref flatAttack, ref percentAttack, ref flatSpeed, ref percentSpeed);
		}
	}

	private void ApplyCalculatedArmor(Entity entity, Dictionary<Realm.Ecs.Common.StatId, float> baseStats, float flatArmor, float percentArmor)
	{
		if (!baseStats.TryGetValue(new Realm.Ecs.Common.StatId("Armor"), out var baseArmor)) return;
		float finalArmor = (baseArmor + flatArmor) * percentArmor;
		if (EcsWorld.Has<Armor>(entity)) EcsWorld.Set(entity, new Armor(finalArmor));
	}

	private void ApplyCalculatedAttack(Entity entity, Dictionary<Realm.Ecs.Common.StatId, float> baseStats, float flatAttack, float percentAttack)
	{
		if (!baseStats.TryGetValue(new Realm.Ecs.Common.StatId("Attack"), out var baseAttack)) return;
		float finalAttack = (baseAttack + flatAttack) * percentAttack;
		if (EcsWorld.Has<Attack>(entity))
		{
			var atk = EcsWorld.Get<Attack>(entity);
			EcsWorld.Set(entity, new Attack(finalAttack, atk.Range, atk.Cooldown, atk.CurrentCooldown));
		}
	}

	private void ApplyCalculatedSpeed(Entity entity, Dictionary<Realm.Ecs.Common.StatId, float> baseStats, float flatSpeed, float percentSpeed)
	{
		if (!baseStats.TryGetValue(new Realm.Ecs.Common.StatId("MovementSpeed"), out var baseSpeed)) return;
		float finalSpeed = (baseSpeed + flatSpeed) * percentSpeed;
		if (EcsWorld.Has<MovementStats>(entity))
		{
			var mv = EcsWorld.Get<MovementStats>(entity);
			EcsWorld.Set(entity, new MovementStats(finalSpeed, mv.Acceleration, mv.TurnRate));
		}
	}

	private void ApplyMod(Realm.Ecs.Components.Stats.StatModifier mod, ref float flatArmor, ref float percentArmor, ref float flatAttack, ref float percentAttack, ref float flatSpeed, ref float percentSpeed)
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

	private void ApplyModifierValue(Realm.Ecs.Components.Stats.StatModifier mod, ref float flatVal, ref float percentVal)
	{
		if (mod.Type == Realm.Ecs.Components.Stats.ModifierType.Flat) flatVal += mod.Value;
		else if (mod.Type == Realm.Ecs.Components.Stats.ModifierType.Percentage) percentVal *= mod.Value;
	}

	private readonly List<string> _tickExpiredCooldowns = new();
	private readonly List<string> _tickCooldownKeys = new();

	private void CooldownsQueryAction(Entity entity, ref Realm.Ecs.Components.Core.Cooldowns cooldowns)
	{
		var dict = cooldowns.Value;
		if (Realm.Client.Core.GameHost.Instance != null && Realm.Client.Core.GameHost.Instance.UnlimitedPowerEnabled)
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

	public void EnqueueVFXRequest(string effectTypeId, System.Numerics.Vector3 position, System.Numerics.Vector3 targetPosition, float scale = 1.0f, float speed = 0f, int entityId = -1)
	{
		if (_worldEntity != Entity.Null && EcsWorld.IsAlive(_worldEntity) && EcsWorld.Has<Realm.Ecs.Components.Core.VFXQueue>(_worldEntity))
		{
			ref var queue = ref EcsWorld.Get<Realm.Ecs.Components.Core.VFXQueue>(_worldEntity);
			queue.Requests.Add(new Realm.Ecs.Components.Core.VFXRequest(effectTypeId, position, targetPosition, scale, speed, entityId));
		}
	}

	private void PatrolArrivalQueryAction(Entity entity, ref Patrol patrol, ref Position pos)
	{
		var current = pos.Value;
		var dest = patrol.GoingToB ? patrol.PointB : patrol.PointA;
		if (System.Numerics.Vector3.Distance(current, dest) < 1.5f)
		{
			_tickPatrolToFlip.Add((entity, patrol));
		}
	}

	private void FollowQueryAction(Entity entity, ref Follow follow, ref Position pos)
	{
		if (!EcsWorld.IsAlive(follow.Target) || EcsWorld.Has<Dead>(follow.Target))
		{
			_tickFollowToStop.Add(entity);
			return;
		}

		var currentPos = pos.Value;
		var targetPos = EcsWorld.Get<Position>(follow.Target).Value;

		float dist = System.Numerics.Vector3.Distance(currentPos, targetPos);
		if (dist <= 3.0f)
		{
			if (EcsWorld.Has<MoveTo>(entity))
			{
				_tickFollowToRemoveMoveTo.Add(entity);
			}
		}
		else
		{
			_tickFollowToMove.Add((entity, targetPos));
		}
	}

	private void AttackCooldownQueryAction(Entity entity, ref Attack atk)
	{
		if (Realm.Client.Core.GameHost.Instance != null && Realm.Client.Core.GameHost.Instance.UnlimitedPowerEnabled)
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

		if (Realm.Client.Core.GameHost.Instance != null && Realm.Client.Core.GameHost.Instance.UnlimitedPowerEnabled)
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

	private void ProdQueryAction(Entity entity, ref Realm.Ecs.Components.Core.ProductionQueue prod)
	{
		if (prod.UnitIds.Count == 0) return;

		float multiplier = (Realm.Client.Core.GameHost.Instance != null && Realm.Client.Core.GameHost.Instance.FastBuildEnabled) ? 10f : 1f;
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
		if (!EcsWorld.Has<Position>(entity)) return;

		var buildingPos = EcsWorld.Get<Position>(entity).Value;
		System.Numerics.Vector3 spawnOffset = EcsWorld.Has<BuildingSpawnOffset>(entity)
			? EcsWorld.Get<BuildingSpawnOffset>(entity).Value
			: new System.Numerics.Vector3(0f, 0f, 8f);
		var spawnPos = buildingPos + spawnOffset;

		var ownerComp = EcsWorld.Get<Owner>(entity);
		var playerEntity = EcsWorld.Get<NetworkMappingState>(ActiveWorldEntity).PlayerEntity;
		bool isEnemy = ownerComp.PlayerEntity != playerEntity.AsPlayerEntity(EcsWorld);

		_tickSpawningRequests.Add(new SpawningRequest
		{
			UnitId = unitToSpawn,
			Position = spawnPos,
			IsEnemy = isEnemy,
			BuildingEntity = entity,
			IsFromQueue = true
		});

		if (!isEnemy) OnProductionCompleted?.Invoke(unitToSpawn);
	}

	private void InterpolationQueryAction(Entity entity, ref InterpolationTarget target)
	{
		if (!EcsWorld.Has<Position>(entity)) return;
		
		var currentPos = EcsWorld.Get<Position>(entity).Value;
		bool isEnemy = EcsWorld.Has<UnitFaction>(entity) && EcsWorld.Get<UnitFaction>(entity).IsEnemy;

		System.Numerics.Vector3 finalPos;
		System.Numerics.Vector3 finalVel;

		if (isEnemy)
		{
			float factor = GetDynamicInterpolationFactor();
			finalPos = System.Numerics.Vector3.Lerp(currentPos, target.Position, Math.Min(1f, factor * _fDelta));
			finalVel = target.Velocity;
		}
		else
		{
			CalculateFriendlyInterpolation(entity, currentPos, target.Position, target.Velocity, out finalPos, out finalVel);
		}

		EcsWorld.Set(entity, new Position(finalPos));
		EcsWorld.SetOrAdd(entity, new Velocity(finalVel));
	}

	private void CalculateFriendlyInterpolation(Entity entity, System.Numerics.Vector3 currentPos, System.Numerics.Vector3 targetPos, System.Numerics.Vector3 targetVel, out System.Numerics.Vector3 finalPos, out System.Numerics.Vector3 finalVel)
	{
		bool hasMovementTarget = EcsWorld.Has<MoveTo>(entity) || EcsWorld.Has<Follow>(entity);
		if (hasMovementTarget && EcsWorld.Has<MovementStats>(entity))
		{
			CalculateMovementInterpolation(entity, currentPos, out finalPos, out finalVel);
		}
		else
		{
			CalculateIdleInterpolation(currentPos, targetPos, targetVel, out finalPos, out finalVel);
		}
	}

	private void CalculateMovementInterpolation(Entity entity, System.Numerics.Vector3 currentPos, out System.Numerics.Vector3 finalPos, out System.Numerics.Vector3 finalVel)
	{
		var dest = GetMovementDestination(entity, currentPos);
		float distToDest = System.Numerics.Vector3.Distance(currentPos, dest);
		float threshold = EcsWorld.Has<Follow>(entity) ? 3.0f : 0.05f;

		if (distToDest > threshold)
		{
			var stats = EcsWorld.Get<MovementStats>(entity);
			System.Numerics.Vector3 dir = System.Numerics.Vector3.Normalize(dest - currentPos);
			float step = Math.Min(stats.Speed * _fDelta, distToDest);
			finalPos = currentPos + dir * step;
			finalVel = dir * stats.Speed;
		}
		else
		{
			finalPos = EcsWorld.Has<Follow>(entity) ? currentPos : dest;
			finalVel = System.Numerics.Vector3.Zero;
			if (EcsWorld.Has<MoveTo>(entity)) EcsWorld.Remove<MoveTo>(entity);
		}
		Console.WriteLine($"[CLIENT_ESTIMATED] Unit={entity.Id} Pos={finalPos} Target={dest}");
	}

	private System.Numerics.Vector3 GetMovementDestination(Entity entity, System.Numerics.Vector3 currentPos)
	{
		if (EcsWorld.Has<MoveTo>(entity)) return EcsWorld.Get<MoveTo>(entity).Target;

		var follow = EcsWorld.Get<Follow>(entity);
		if (EcsWorld.IsAlive(follow.Target) && EcsWorld.Has<Position>(follow.Target))
		{
			return EcsWorld.Get<Position>(follow.Target).Value;
		}
		return currentPos;
	}

	private void CalculateIdleInterpolation(System.Numerics.Vector3 currentPos, System.Numerics.Vector3 targetPos, System.Numerics.Vector3 targetVel, out System.Numerics.Vector3 finalPos, out System.Numerics.Vector3 finalVel)
	{
		float dist = System.Numerics.Vector3.Distance(currentPos, targetPos);
		finalPos = currentPos;
		finalVel = targetVel;

		if (dist > 2.0f) finalPos = targetPos;
		else if (dist > 0.5f) finalPos = currentPos + (targetPos - currentPos) * (_fDelta / 0.2f);
		else if (dist > 0.01f) finalPos = currentPos + (targetPos - currentPos) * (_fDelta / 0.5f);
	}

	private int GetTimeOfDayIndex()
		=> EcsWorld.GetFieldOrDefault<WorldState, int>(ActiveWorldEntity, s => s.TimeOfDayIndex);

	private float GetDynamicInterpolationFactor()
		=> EcsWorld.GetFieldOrDefault<NetworkState, float>(ActiveWorldEntity, s => s.DynamicInterpolationFactor, 10f);

	public List<Entity> GetEditorArrivedUnits() => _tickArrivedUnits;

	public void DealSpellDamageAOE(System.Numerics.Vector3 position, float radius, float damage, Entity casterEntity, bool enemyOnly = true)
	{
		_combatService.DealSpellDamageAOE(position, radius, damage, casterEntity, enemyOnly);
	}

	public void HealAOE(System.Numerics.Vector3 position, float radius, float healAmount)
	{
		_combatService.HealAOE(position, radius, healAmount);
	}
}