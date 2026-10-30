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
		if (TryGetEntity(unit, out var entity) && GameContext.World.IsAlive(entity))
		{
			GameContext.World.SetOrAdd(entity, new RotationY(facingRadians));
			if (GameContext.World.Has<LatestDescription>(entity))
			{
				ref var interp = ref GameContext.World.Get<LatestDescription>(entity);
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
				if (GameContext.Simulation.WorldEntity != Entity.Null && GameContext.World.IsAlive(GameContext.Simulation.WorldEntity))
				{
					_resolvedWorldEntity = GameContext.Simulation.WorldEntity;
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

	private readonly MovementAndPathfindingService.SpatialGridEntryCallback _autoCastFriendlyCallback;

	private readonly MovementAndPathfindingService.SpatialGridEntryCallback _autoCastEnemyCallback;

	private readonly List<Entity> _tickArrivedUnits = new();
	private readonly Dictionary<int, BotController> _botControllers = new();


	private EntityCallback<LatestDescription> _presentationQueryDelegate = null!;


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
	private readonly List<Entity> _referenceHoldersScratch = new();

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

	private bool HasScriptPlayersState()
	{
		var worldEntity = GameContext.Simulation.WorldEntity;
		return GameContext.World != null && worldEntity != Entity.Null && GameContext.World.IsAlive(worldEntity) && GameContext.World.Has<ScriptPlayersState>(worldEntity);
	}

	public void SetPlayerTeam(int playerIndex, int teamIndex)
	{
		int stringId = StringRegistry.GetOrAdd(teamIndex == 0 ? "Neutral" : $"Team {teamIndex}");

		if (HasScriptPlayersState())
		{
			ref var playersState = ref GameContext.World.Get<ScriptPlayersState>(GameContext.Simulation.WorldEntity);
			if (playersState.IsValidSlot(playerIndex))
			{
				playersState.SetTeam(playerIndex, teamIndex, stringId);
				var player = playersState.Get(playerIndex);
				GameContext.World.GetBuffer<ScriptPlayer>().Set(playerIndex, in player);
			}
		}

		var worldEntity = GameContext.Simulation.WorldEntity;
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

		if (GameContext.Lobby != null && GameContext.Lobby.PlayerList != null)
		{
			var list = GameContext.Lobby.PlayerList;
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

		if (HasScriptPlayersState())
		{
			ref var playersState = ref GameContext.World.Get<ScriptPlayersState>(GameContext.Simulation.WorldEntity);
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
		if (HasScriptPlayersState())
		{
			ref var playersState = ref GameContext.World.Get<ScriptPlayersState>(GameContext.Simulation.WorldEntity);
			if (playersState.IsValidSlot(playerIndex))
			{
				playersState.SetActive(playerIndex, active);
				var player = playersState.Get(playerIndex);
				GameContext.World.GetBuffer<ScriptPlayer>().Set(playerIndex, in player);
			}
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

	private bool _positionWriterInvariantReported;

	public SimulationService()
	{
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

		// A summon that times out dies like any other unit for visuals and cleanup, but the expiry itself is not a kill:
		// it has no last attacker, so no bounty or kill credit is awarded.
		OnSummonExpired = ent =>
		{
			if (!GameContext.World.IsAlive(ent) || GameContext.World.Has<Dead>(ent)) return;
			GameContext.World.Add<Dead>(ent);
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
		_presentationQueryDelegate = PresentationQueryAction;
	}

	public bool ShouldSimulateEntity(Entity entity)
	{
		var host = GameContext.Host;
		if (host == null) return false;
		if (GameContext.World == null || !GameContext.World.IsAlive(entity)) return false;

		int localPlayerIndex = host.LocalPlayerIndex;
		if (localPlayerIndex < 0) return false;

		return GameContext.World.Has<UnitOwnerPlayer>(entity) && GameContext.World.Get<UnitOwnerPlayer>(entity).PlayerIndex == localPlayerIndex;
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
			var profile = genreProvider.CreateDefaultProfile(GameContext.Simulation.ActiveMapName ?? "GenericMap");
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
	public EntityCallback<LatestDescription> PresentationQueryDelegate => _presentationQueryDelegate;

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

	public Action<Entity>? OnSummonExpired;

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

		// Only the editor opens ground itself. In a match the mesh is baked from the authority's obstacle set, so a screen
		// that uncarved here would path through a blocker the authority still has.
		if (isBuilding && GameContext.SessionState.IsMapEditorMode)
		{
			if (GameContext.World.IsAlive(GameContext.Simulation.WorldEntity) && GameContext.World.Has<TerrainState>(GameContext.Simulation.WorldEntity))
			{
				ref var state = ref GameContext.World.Get<TerrainState>(GameContext.Simulation.WorldEntity);
				GameContext.Simulation.TerrainNavMeshService?.UncarveObstacle(ref state, unitPos, radius);
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
		GameContext.NetworkService.ForgetReferencesHeldBy(unitEntity.Id);
		GameContext.World.Destroy(unitEntity);
		RemoveReferencesToDestroyedEntity(unitEntity);
	}

	private void RemoveReferencesToDestroyedEntity(Entity destroyedEntity)
	{
		EcsWorld? world = GameContext.World;
		if (world == null) return;

		_referenceHoldersScratch.Clear();
		foreach (Entity entity in world.Query<LastAttacker>())
		{
			if (world.Get<LastAttacker>(entity).Value != destroyedEntity) continue;
			_referenceHoldersScratch.Add(entity);
		}

		for (int index = 0; index < _referenceHoldersScratch.Count; index++)
		{
			world.Remove<LastAttacker>(_referenceHoldersScratch[index]);
		}
	}

	public void DestroyEntitiesDomain(IEnumerable<Entity> entities)
	{
		foreach (var entity in entities)
		{
			if (GameContext.World.IsAlive(entity))
			{
				UnitWrapperCache.Remove(entity.Id);
				GameContext.World.Destroy(entity);
				RemoveReferencesToDestroyedEntity(entity);
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

		if (GameContext.Host != null)
		{
			var allUnits = GameContext.Host.AllUnits;
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
		if (worldEntity != Entity.Null && GameContext.World.IsAlive(worldEntity) && GameContext.World.Has<VFXQueue>(worldEntity))
		{
			ref var queue = ref GameContext.World.Get<VFXQueue>(worldEntity);
			queue.Enqueue(new VFXRequest(effectTypeId, position, targetPosition, scale, speed, entityId, ownerPlayerIndex));
		}
	}

	private const float FollowRetargetThreshold = 1.0f;

	private const float StationaryVelocitySquared = 0.01f;

	/// <summary>
	///     The one thing that writes <see cref="Position" /> for a replicated unit, from the description sampled where the
	///     match clock is being rendered rather than where the authority has got to. A unit is drawn between the two
	///     descriptions either side of the cursor, so it neither trails a smoothed target nor has to be caught up.
	/// </summary>
	private void PresentationQueryAction(Entity entity, ref LatestDescription target)
	{
		if (!GameContext.World.Has<Position>(entity)) return;

		if (MultiplayerSyncDiagnostics.IsRecording) MultiplayerSyncDiagnostics.RecordPresentationStep(entity.Id, GameContext.NetworkService?.LastAppliedSnapshotSequenceForDiagnostics ?? -1);

		// Only a unit this screen ordered to move is drawn ahead of its newest description, and the step is bounded to one
		// interval and 0.75 m along the direction it was already moving. Everything else is drawn strictly between
		// descriptions this screen holds. Drawing another player's unit ahead of its description would invent motion this
		// screen was never told about, so the step masks one interval of the delay for the local player's own orders only.
		// Whether such an order is held was stamped by the description this screen applied, where the sequence it came at
		// is known. All this frame does is read it, so the lead a completed order leaves behind is released when the
		// completion is drawn rather than when some later frame happened to notice it.
		int localPlayerIndex = GameContext.Host?.LocalPlayerIndex ?? -1;
		bool localOrderConfirmed = GameContext.World.Has<MoveTo>(entity)
		                           && GameContext.World.Has<UnitOwnerPlayer>(entity)
		                           && GameContext.World.Get<UnitOwnerPlayer>(entity).PlayerIndex == localPlayerIndex;
		if (!GameContext.SnapshotTimeline.TrySample(entity.Id, localOrderConfirmed, out Vector3 sampledPosition, out float sampledRotationY, out Vector3 sampledVelocity)) return;

		Vector3 currentPos = GameContext.World.Get<Position>(entity).Value;

		if (MultiplayerSyncDiagnostics.IsRecording && (sampledPosition - currentPos).LengthSquared() > 4f)
		{
			MultiplayerSyncDiagnostics.RecordPresentationStepDetail(entity.Id, currentPos, sampledPosition, 1f, sampledPosition);
		}

		PositionWriteTrace.MarkPresentation(entity, 1f, currentPos, sampledPosition, sampledPosition, target.Position);
		GameContext.World.Set(entity, new Position(sampledPosition));
		GameContext.World.SetOrAdd(entity, new Velocity(sampledVelocity));

		// A unit standing still is drawn facing where the authority had it facing at the moment being rendered. The
		// renderer smooths towards this value, so a unit that turns in place turns when it is drawn rather than one
		// presentation delay before it. A moving unit is turned along its own movement by the renderer instead.
		if (sampledVelocity.LengthSquared() <= StationaryVelocitySquared) target.RotationY = sampledRotationY;
	}

	private int GetTimeOfDayIndex()
		=> GameContext.World.GetFieldOrDefault<WorldState, int>(ActiveWorldEntity, s => s.TimeOfDayIndex);

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
		if (!TryGetEntity(unit, out var entity) || !GameContext.World.IsAlive(entity)) return;

		level = Math.Max(1, level);
		GameContext.World.SetOrAdd(entity, new Level(level));

		LevelService.Apply(GameContext.World, entity, level, ResolveLevelGrowth(entity));
	}

	private static LevelGrowth ResolveLevelGrowth(Entity entity)
	{
		if (!GameContext.World.Has<DefinitionId>(entity)) return LevelGrowth.None;

		string definitionId = GameContext.World.Get<DefinitionId>(entity).Value;
		if (!GameContext.RegistryService.UnitRegistry.TryGetValue(definitionId, out var metadata)) return LevelGrowth.None;

		var growth = metadata.LevelGrowth;
		if (growth == null) return LevelGrowth.None;

		return new LevelGrowth(
			growth.StrengthPerLevel,
			growth.AgilityPerLevel,
			growth.VitalityPerLevel,
			growth.IntelligencePerLevel,
			growth.WisdomPerLevel,
			growth.FortunePerLevel);
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

	public void SetPlayerComputerControlled(int playerIndex, bool isProxy)
	{
		if (HasScriptPlayersState())
		{
			ref var playersState = ref GameContext.World.Get<ScriptPlayersState>(GameContext.Simulation.WorldEntity);
			if (playersState.IsValidSlot(playerIndex))
			{
				playersState.SetComputerControlled(playerIndex, isProxy);
				var player = playersState.Get(playerIndex);
				GameContext.World.GetBuffer<ScriptPlayer>().Set(playerIndex, in player);
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
			if (AbilityEffectService.HasEffects(def))
			{
				Vector3 casterPosition = GameContext.World.IsAlive(casterEnt) && GameContext.World.Has<Position>(casterEnt)
					? GameContext.World.Get<Position>(casterEnt).Value
					: targetPosition;

				AbilityEffectService.Execute(casterEnt, casterPosition, def, targetPosition);
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

	public void DealResolvedDamage(Entity attacker, Entity target, float amount, string damageType)
	{
		_combatService.DealResolvedDamage(attacker, target, amount, damageType);
	}

	public void HealSingle(Entity casterEntity, Entity target, float amount)
	{
		_combatService.HealSingle(casterEntity, target, amount);
	}

	public float GetAbilityCooldown(UnitId unit, string abilityId)
	{
		if (TryGetEntity(unit, out var entity) && GameContext.World.IsAlive(entity))
		{
			if (GameContext.World.Has<Cooldowns>(entity))
			{
				var cooldowns = GameContext.World.Get<Cooldowns>(entity);
				if (cooldowns.TryGetValue(abilityId, out float remaining)) return remaining;
			}

			if (GameContext.World.Has<SpellCooldowns>(entity))
			{
				var spellCooldowns = GameContext.World.Get<SpellCooldowns>(entity);
				if (spellCooldowns.TryGetValue(abilityId, out float remaining)) return remaining;
			}
		}

		return 0f;
	}

	public void SetAbilityCooldown(UnitId unit, string abilityId, float cooldown)
	{
		if (TryGetEntity(unit, out var entity) && GameContext.World.IsAlive(entity))
		{
			if (!GameContext.World.Has<Cooldowns>(entity))
			{
				GameContext.World.Add(entity, new Cooldowns(null));
			}
			ref var cooldowns = ref GameContext.World.Get<Cooldowns>(entity);
			cooldowns.Set(abilityId, cooldown, cooldown);

			if (!GameContext.World.Has<SpellCooldowns>(entity))
			{
				GameContext.World.Add(entity, new SpellCooldowns(null));
			}
			ref var spellCooldowns = ref GameContext.World.Get<SpellCooldowns>(entity);
			spellCooldowns.Set(abilityId, cooldown, cooldown);
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
			ref var playersState = ref GameContext.World.Get<ScriptPlayersState>(GameContext.Simulation.WorldEntity);
			if (playersState.IsValidSlot(playerIndex)) return playersState.Get(playerIndex).Name;
		}
		return $"Player {playerIndex}";
	}

	public bool IsPlayerActiveDomain(int playerIndex)
	{
		if (HasScriptPlayersState())
		{
			ref var playersState = ref GameContext.World.Get<ScriptPlayersState>(GameContext.Simulation.WorldEntity);
			if (playersState.IsValidSlot(playerIndex)) return playersState.Get(playerIndex).Active;
		}
		return false;
	}

	public float GetPlayerGoldDomain(int playerIndex)
	{
		if (HasScriptPlayersState())
		{
			ref var playersState = ref GameContext.World.Get<ScriptPlayersState>(GameContext.Simulation.WorldEntity);
			if (playersState.IsValidSlot(playerIndex)) return playersState.Get(playerIndex).Gold;
		}
		return 0f;
	}

	public void SetPlayerGoldDomain(int playerIndex, float amount, Action<int, float> syncResourceAction)
	{
		if (HasScriptPlayersState())
		{
			ref var playersState = ref GameContext.World.Get<ScriptPlayersState>(GameContext.Simulation.WorldEntity);
			if (playersState.IsValidSlot(playerIndex))
			{
				playersState.SetGold(playerIndex, Math.Max(0f, amount));
				var player = playersState.Get(playerIndex);
				GameContext.World.GetBuffer<ScriptPlayer>().Set(playerIndex, in player);
			}
		}

		syncResourceAction(playerIndex, amount);
	}

	public int GetPlayerKillsDomain(int playerIndex)
	{
		if (HasScriptPlayersState())
		{
			ref var playersState = ref GameContext.World.Get<ScriptPlayersState>(GameContext.Simulation.WorldEntity);
			if (playersState.IsValidSlot(playerIndex)) return playersState.Get(playerIndex).KillCount;
		}
		return 0;
	}

	public void SetPlayerKillsDomain(int playerIndex, int kills)
	{
		if (HasScriptPlayersState())
		{
			ref var playersState = ref GameContext.World.Get<ScriptPlayersState>(GameContext.Simulation.WorldEntity);
			if (playersState.IsValidSlot(playerIndex))
			{
				playersState.SetKills(playerIndex, kills);
				var player = playersState.Get(playerIndex);
				GameContext.World.GetBuffer<ScriptPlayer>().Set(playerIndex, in player);
			}
		}
	}
}
