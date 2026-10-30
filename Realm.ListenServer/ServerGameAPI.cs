using System.Numerics;
using Realm.Ecs;
using Realm.Ecs.Common;
using Realm.Ecs.Components.Combat;
using Realm.Ecs.Components.Core;
using Realm.Ecs.Components.Meta;
using Realm.Ecs.Components.Movement;
using Realm.Ecs.Components.Resources;
using Realm.Ecs.Components.Tags;
using Realm.Ecs.Services;
using Realm.Shared.Maps;
using Realm.Shared.Network;
using Realm.Shared.Simulation;
using Realm.MapAPI;

namespace Realm.ListenServer;

public partial class ServerGameAPI : IGameAPI, IDisposable
{
	private readonly SimulationContext _context;
	private readonly LoadedMap _map;
	private readonly ServerLog _log;
	private readonly ServerPresentationQueue _presentation;
	private readonly ServerMatchPaths _paths;
	private readonly ServerLocalization _localization;
	private readonly ServerSavedData _savedData;
	private readonly ServerTechLevels _techLevels;
	private readonly ServerBots _bots;
	private readonly AbilityEffectRunner _effects;
	private readonly PlayerId[] _activePlayersBuffer = new PlayerId[ScriptPlayersState.SlotCount];
	private int _countingScratch;

	public ServerGameAPI(LoadedMap map, ServerLog log, ServerPresentationQueue presentation, ItemGrantService items, GroundLootService loot, ServerMatchPaths paths)
	{
		_map = map;
		_context = map.Simulation;
		_log = log;
		_presentation = presentation;
		_paths = paths;
		_localization = new ServerLocalization(paths, log);
		_savedData = new ServerSavedData(paths.DataDirectory, log);
		_techLevels = new ServerTechLevels(_context, map);
		_abilities = new ScriptAbilityRegistry(map.Catalog);
		_bots = new ServerBots(_context, paths.MapName, paths.DataDirectory, log);
		_bots.CustomActionExecuted = ForwardBotCustomAction;
		_effects = new AbilityEffectRunner(_context, ArePlayersAllied, SpawnAbilitySummon);
		Items = items;
		Loot = loot;
		_countResourceNodes = CountResourceNodes;
		_countOwnedUnits = CountOwnedUnits;
		_collectOwnedUnits = CollectOwnedUnits;
		_collectResourceNodes = CollectResourceNodes;
	}

	public void Dispose() => _savedData.Dispose();

	public EcsWorld World => _context.World;

	public ServerLocalization Localization => _localization;

	public ServerTechLevels TechLevels => _techLevels;

	public ServerBots Bots => _bots;

	public ItemGrantService Items { get; }

	public GroundLootService Loot { get; }

	public float GameElapsedTime
	{
		get
		{
			Entity worldEntity = _context.WorldEntity;
			if (worldEntity == Entity.Null || !_context.World.IsAlive(worldEntity)) return 0f;
			if (!_context.World.Has<WorldState>(worldEntity)) return 0f;

			return _context.World.Get<WorldState>(worldEntity).GameElapsedTime;
		}
	}

	public bool CheatsEnabled => _context.CheatsEnabled;

	public bool IsHost(int playerIndex) => playerIndex > 0 && playerIndex == _context.HostPlayerSlot;

	public int PlayerCount
	{
		get
		{
			if (!TryReadPlayersState(out ScriptPlayersState playersState)) return 1;

			int count = 0;
			for (int playerIndex = 0; playerIndex < ScriptPlayersState.SlotCount; playerIndex++)
			{
				if (playersState.Get(playerIndex).Active) count++;
			}

			return Math.Max(1, count);
		}
	}

	public int ResourceNodeCount
	{
		get
		{
			_countingScratch = 0;
			_context.World.Query(in ResourceNodeQuery, _countResourceNodes);
			return _countingScratch;
		}
	}

	public ReadOnlySpan<PlayerId> GetActivePlayers()
	{
		if (!TryReadPlayersState(out ScriptPlayersState playersState))
		{
			_activePlayersBuffer[0] = new PlayerId(0);
			return new ReadOnlySpan<PlayerId>(_activePlayersBuffer, 0, 1);
		}

		int count = 0;
		for (int playerIndex = 0; playerIndex < ScriptPlayersState.SlotCount; playerIndex++)
		{
			if (playersState.Get(playerIndex).Active) _activePlayersBuffer[count++] = new PlayerId(playerIndex);
		}

		return new ReadOnlySpan<PlayerId>(_activePlayersBuffer, 0, count);
	}

	public string GetPlayerName(int playerIndex)
	{
		if (TryReadPlayersState(out ScriptPlayersState playersState) && playersState.IsValidSlot(playerIndex))
		{
			string name = playersState.Get(playerIndex).Name;
			if (!string.IsNullOrEmpty(name)) return name;
		}

		return playerIndex == 0 ? "Neutral" : $"Player {playerIndex}";
	}

	public Vector3 GetPlayerStartLocation(int playerIndex)
	{
		Entity playerEntity = _context.GetPlayerEntity(playerIndex);
		if (playerEntity == Entity.Null) return Vector3.Zero;
		if (!_context.World.TryGet<Position>(playerEntity, out Position position)) return Vector3.Zero;

		return position.Value;
	}

	public int GetPlayerTeam(int playerIndex)
	{
		if (!TryReadPlayersState(out ScriptPlayersState playersState) || !playersState.IsValidSlot(playerIndex)) return 0;
		return playersState.Get(playerIndex).Team;
	}

	public float GetPlayerGold(int playerIndex) => ReadPlayerResource(playerIndex, PlayerResourceKind.Gold);

	public float GetPlayerWood(int playerIndex) => ReadPlayerResource(playerIndex, PlayerResourceKind.Wood);

	public float GetPlayerStone(int playerIndex) => ReadPlayerResource(playerIndex, PlayerResourceKind.Stone);

	public void SetPlayerGold(int playerIndex, float amount) => WritePlayerResource(playerIndex, PlayerResourceKind.Gold, amount);

	public void SetPlayerWood(int playerIndex, float amount) => WritePlayerResource(playerIndex, PlayerResourceKind.Wood, amount);

	public void SetPlayerStone(int playerIndex, float amount) => WritePlayerResource(playerIndex, PlayerResourceKind.Stone, amount);

	public void AdjustPlayerGold(int playerIndex, float delta) => SetPlayerGold(playerIndex, GetPlayerGold(playerIndex) + delta);

	public void AdjustPlayerWood(int playerIndex, float delta) => SetPlayerWood(playerIndex, GetPlayerWood(playerIndex) + delta);

	public void AdjustPlayerStone(int playerIndex, float delta) => SetPlayerStone(playerIndex, GetPlayerStone(playerIndex) + delta);

	public void SetPlayerMaxPopulation(int playerIndex, int max) => _context.ResourceEconomyService.SetPlayerMaxPopulation(playerIndex, max);

	public void SetPlayerKills(int playerIndex, int kills)
	{
		MutatePlayersState(playerIndex, (ref ScriptPlayersState playersState, int slot) => playersState.SetKills(slot, kills));
	}

	public void SetPlayerActive(int playerIndex, bool active)
	{
		MutatePlayersState(playerIndex, (ref ScriptPlayersState playersState, int slot) => playersState.SetActive(slot, active));
	}

	public void SetPlayerTeam(int playerIndex, int teamIndex)
	{
		int teamNameStringId = StringRegistry.GetOrAdd(teamIndex == 0 ? "Neutral" : $"Team {teamIndex}");
		MutatePlayersState(playerIndex, (ref ScriptPlayersState playersState, int slot) => playersState.SetTeam(slot, teamIndex, teamNameStringId));

		Entity playerEntity = _context.GetPlayerEntity(playerIndex);
		if (playerEntity == Entity.Null) return;

		if (_context.World.Has<PlayerTeam>(playerEntity))
		{
			ref PlayerTeam playerTeam = ref _context.World.Get<PlayerTeam>(playerEntity);
			playerTeam.TeamIndex = teamIndex;
			playerTeam.TeamNameStringId = teamNameStringId;
			return;
		}

		_context.World.Add(playerEntity, new PlayerTeam(teamIndex, teamNameStringId));
	}

	public bool ArePlayersAllied(int playerIndex, int otherPlayerIndex)
	{
		if (playerIndex == otherPlayerIndex) return true;

		Entity worldEntity = _context.WorldEntity;
		if (worldEntity != Entity.Null && _context.World.IsAlive(worldEntity) && _context.World.Has<PlayerRelationshipMatrix>(worldEntity))
		{
			return _context.World.Get<PlayerRelationshipMatrix>(worldEntity).AreAllied(playerIndex, otherPlayerIndex);
		}

		int firstTeam = GetPlayerTeam(playerIndex);
		int secondTeam = GetPlayerTeam(otherPlayerIndex);
		return firstTeam != 0 && firstTeam == secondTeam;
	}

	public void SetPlayersAllied(int playerIndex, int otherPlayerIndex, bool allied)
	{
		Entity worldEntity = _context.WorldEntity;
		if (worldEntity == Entity.Null || !_context.World.IsAlive(worldEntity) || !_context.World.Has<PlayerRelationshipMatrix>(worldEntity)) return;

		ref PlayerRelationshipMatrix relationships = ref _context.World.Get<PlayerRelationshipMatrix>(worldEntity);
		PlayerRelationship relationship = allied ? PlayerRelationship.Ally : PlayerRelationship.Neutral;
		relationships.SetRelationship(playerIndex, otherPlayerIndex, relationship);
		relationships.SetRelationship(otherPlayerIndex, playerIndex, relationship);
	}

	public int CountUnitsOwnedByPlayer(int playerIndex)
	{
		Entity playerEntity = _context.GetPlayerEntity(playerIndex);
		if (playerEntity == Entity.Null) return 0;

		_countingScratch = 0;
		_countedOwner = playerEntity;
		_context.World.Query(in PlayerUnitsQuery, _countOwnedUnits);
		return _countingScratch;
	}

	public string GetUnitTemplateId(UnitId unit)
	{
		Entity entity = ResolveUnit(unit);
		if (entity == Entity.Null) return string.Empty;
		if (!_context.World.TryGet<DefinitionId>(entity, out DefinitionId definitionId)) return string.Empty;

		return definitionId.Value;
	}

	public string GetUnitInstanceName(UnitId unit)
	{
		Entity entity = ResolveUnit(unit);
		if (entity == Entity.Null) return string.Empty;
		if (!_context.World.TryGet<Name>(entity, out Name name)) return string.Empty;

		return name.Value;
	}

	public UnitId GetUnitById(int uniqueId)
	{
		if (uniqueId <= 0) return UnitId.Invalid;

		var entity = new Entity(uniqueId);
		return _context.World.IsAlive(entity) ? new UnitId(uniqueId) : UnitId.Invalid;
	}

	public void SetUnitHealth(UnitId unit, float value)
	{
		Entity entity = ResolveUnit(unit);
		if (entity == Entity.Null) return;
		if (!_context.World.TryGet<Health>(entity, out Health health)) return;

		health.Current = Math.Clamp(value, 0f, health.Max);
		_context.World.Set(entity, health);

		if (health.Current > 0f) return;
		DestroyUnit(unit);
	}

	public void SetUnitMaxHealth(UnitId unit, float value)
	{
		Entity entity = ResolveUnit(unit);
		if (entity == Entity.Null) return;
		if (!_context.World.TryGet<Health>(entity, out Health health)) return;

		health.Max = Math.Max(1f, value);
		health.Current = Math.Min(health.Current, health.Max);
		_context.World.Set(entity, health);
	}

	public void SetUnitMana(UnitId unit, float value)
	{
		Entity entity = ResolveUnit(unit);
		if (entity == Entity.Null) return;
		if (!_context.World.TryGet<Mana>(entity, out Mana mana)) return;

		mana.Current = Math.Clamp(value, 0f, mana.Max);
		_context.World.Set(entity, mana);
	}

	public void SetUnitMaxMana(UnitId unit, float value)
	{
		Entity entity = ResolveUnit(unit);
		if (entity == Entity.Null) return;
		if (!_context.World.TryGet<Mana>(entity, out Mana mana)) return;

		mana.Max = Math.Max(0f, value);
		mana.Current = Math.Min(mana.Current, mana.Max);
		_context.World.Set(entity, mana);
	}

	public void SetUnitManaRegen(UnitId unit, float value)
	{
		Entity entity = ResolveUnit(unit);
		if (entity == Entity.Null) return;

		if (_context.World.TryGet<Mana>(entity, out Mana mana))
		{
			mana.ManaRegen = value;
			_context.World.Set(entity, mana);
		}

		if (_context.World.Has<ManaRegen>(entity))
		{
			_context.World.Set(entity, new ManaRegen(value));
		}
	}

	public void SetUnitDamage(UnitId unit, float value)
	{
		Entity entity = ResolveUnit(unit);
		if (entity == Entity.Null) return;
		if (!_context.World.TryGet<Attack>(entity, out Attack attack)) return;

		attack.Damage = value;
		_context.World.Set(entity, attack);
	}

	public void SetUnitRange(UnitId unit, float value)
	{
		Entity entity = ResolveUnit(unit);
		if (entity == Entity.Null) return;
		if (!_context.World.TryGet<Attack>(entity, out Attack attack)) return;

		attack.Range = value;
		_context.World.Set(entity, attack);
	}

	public void SetUnitAttackSpeed(UnitId unit, float value)
	{
		Entity entity = ResolveUnit(unit);
		if (entity == Entity.Null) return;
		if (!_context.World.TryGet<Attack>(entity, out Attack attack)) return;

		attack.Cooldown = Math.Max(0.01f, value);
		_context.World.Set(entity, attack);
	}

	public void SetUnitArmor(UnitId unit, float value)
	{
		Entity entity = ResolveUnit(unit);
		if (entity == Entity.Null) return;
		if (!_context.World.TryGet<Resistances>(entity, out Resistances resistances)) return;

		resistances.Physical = value;
		_context.World.Set(entity, resistances);
	}

	public void SetUnitSpeed(UnitId unit, float value)
	{
		Entity entity = ResolveUnit(unit);
		if (entity == Entity.Null) return;
		if (!_context.World.TryGet<MovementStats>(entity, out MovementStats movement)) return;

		movement.Speed = value;
		_context.World.Set(entity, movement);
	}

	public void TeleportUnit(UnitId unit, Vector3 position)
	{
		Entity entity = ResolveUnit(unit);
		if (entity == Entity.Null) return;

		_context.World.Set(entity, new Position(position));
		_context.World.Set(entity, new PreviousPosition(position));
		_context.World.Remove<PathFollow>(entity);
	}

	public void SetUnitFacing(UnitId unit, float facingRadians)
	{
		Entity entity = ResolveUnit(unit);
		if (entity == Entity.Null) return;

		_context.World.SetOrAdd(entity, new RotationY(facingRadians));
	}

	public void SetUnitScale(UnitId unit, float value)
	{
		Entity entity = ResolveUnit(unit);
		if (entity == Entity.Null) return;

		_context.World.SetOrAdd(entity, new ModelScale(value));
		_context.World.SetOrAdd(entity, new CollisionScale(value));
	}

	public void SetUnitExperience(UnitId unit, float value)
	{
		Entity entity = ResolveUnit(unit);
		if (entity == Entity.Null) return;

		_context.World.SetOrAdd(entity, new Experience(value));
	}

	public void SetUnitLevel(UnitId unit, int level)
	{
		Entity entity = ResolveUnit(unit);
		if (entity == Entity.Null) return;

		_context.World.SetOrAdd(entity, new Level(level));
		LevelService.Apply(_context.World, entity, level, LevelGrowth.None);
	}

	public void SetUnitSpellImmune(UnitId unit, bool immune) => SetUnitFlag<SpellImmune>(unit, immune);

	public void SetUnitInvulnerable(UnitId unit, bool value) => SetUnitFlag<Invulnerable>(unit, value);

	public void SetUnitFrozen(UnitId unit, bool value)
	{
		Entity entity = ResolveUnit(unit);
		if (entity == Entity.Null) return;

		SetFrozenFlags(entity, value);
		if (value) _context.World.Remove<PathFollow>(entity);
	}

	public void SetUnitCommandsFrozen(UnitId unit, bool value) => SetUnitFlag<CommandsFrozen>(unit, value);

	public void EnableUnitAutoCombat(UnitId unit, bool enabled)
	{
		Entity entity = ResolveUnit(unit);
		if (entity == Entity.Null) return;

		if (_context.World.TryGet<AutoCombatState>(entity, out AutoCombatState autoCombat))
		{
			autoCombat.Enabled = enabled;
			_context.World.Set(entity, autoCombat);
			return;
		}

		var state = new AutoCombatState();
		state.Enabled = enabled;
		_context.World.Add(entity, state);
	}

	public void SetUnitOwner(UnitId unit, int playerIndex)
	{
		Entity entity = ResolveUnit(unit);
		if (entity == Entity.Null) return;

		Entity playerEntity = _context.GetPlayerEntity(playerIndex);
		if (playerEntity == Entity.Null) return;

		_context.World.SetOrAdd(entity, new Owner(new PlayerEntity(playerEntity)));

		if (_context.World.TryGet<UnitOwnerPlayer>(entity, out UnitOwnerPlayer owner))
		{
			owner.Value = playerIndex;
			_context.World.Set(entity, owner);
			return;
		}

		_context.World.Add(entity, new UnitOwnerPlayer(playerIndex));
	}

	public void DestroyUnit(UnitId unit, bool executeDespawnShader = true, bool playDeathAnimation = true)
	{
		Entity entity = ResolveUnit(unit);
		if (entity == Entity.Null) return;

		_context.World.Destroy(entity);
	}

	public void KillUnit(UnitId unit, bool executeDespawnShader = true, bool playDeathAnimation = true)
	{
		Entity entity = ResolveUnit(unit);
		if (entity == Entity.Null) return;

		if (_context.World.TryGet<Health>(entity, out Health health))
		{
			health.Current = 0f;
			_context.World.Set(entity, health);
		}

		_context.World.Destroy(entity);
	}

	public void SendMessageToPlayer(int playerIndex, string message)
	{
		if (string.IsNullOrEmpty(message)) return;

		_presentation.WriteText(PresentationKind.PlayerMessage, playerIndex, message, Vector3.Zero);
		_log.Write($"script message to player {playerIndex}: {message}");
	}

	public void BroadcastMessage(string message)
	{
		if (string.IsNullOrEmpty(message)) return;

		_presentation.WriteText(PresentationKind.BroadcastMessage, -1, message, Vector3.Zero);
		_log.Write($"script message: {message}");
	}

	public void ShowPlayerFeedbackText(int playerIndex, string text, Vector3 color)
	{
		_presentation.WriteText(PresentationKind.PlayerFeedbackText, playerIndex, text, color);
	}

	public void ShowFeedbackText(string text, Vector3 color)
	{
		_presentation.WriteText(PresentationKind.FeedbackText, -1, text, color);
	}

	public void CreateFloatingText(string text, Vector3 position, Vector3 color, float duration)
	{
		if (string.IsNullOrEmpty(text)) return;

		_presentation.Write(PresentationKind.FloatingText, -1, 0, string.Empty, text, string.Empty, string.Empty, null,
			position, Vector3.Zero, color, 0, 0, 0f, 0f, duration, false);
	}

	public void PingPlayerMinimap(int playerIndex, Vector3 position)
	{
		_presentation.Write(PresentationKind.MinimapPing, playerIndex, position);
	}

	public void SpawnTargetIndicator(Vector3 position, Vector3 color)
	{
		_presentation.Write(PresentationKind.TargetIndicator, -1, 0, string.Empty, string.Empty, string.Empty, string.Empty, null,
			position, Vector3.Zero, color, 0, 0, 0f, 0f, 0f, false);
	}

	public void TriggerVictory()
	{
		_context.IsGameOver = true;
		_log.Write("the map declared victory");
	}

	public void TriggerDefeat()
	{
		_context.IsGameOver = true;
		_log.Write("the map declared defeat");
	}

	public void TriggerPlayerDefeat(int playerIndex, string reason = "")
	{
		SetPlayerActive(playerIndex, false);
		_presentation.WriteText(PresentationKind.PlayerDefeated, playerIndex, reason, Vector3.Zero);
		_log.Write(string.IsNullOrEmpty(reason) ? $"player {playerIndex} defeated" : $"player {playerIndex} defeated ({reason})");
	}

	public void TriggerPlayerVictory(int playerIndex)
	{
		_presentation.Write(PresentationKind.PlayerVictory, playerIndex, Vector3.Zero);
		_log.Write($"player {playerIndex} victorious");
	}

	private UnitId SpawnAbilitySummon(string unitTypeId, Vector3 position, int playerIndex)
	{
		var spawnPosition = new Vector2(position.X, position.Z);
		return SpawnUnitForPlayer(unitTypeId, spawnPosition, playerIndex, executeSpawnShader: false, bypassPopulation: true);
	}

	private void ForwardBotCustomAction(int playerIndex, string actionId, string intent, Vector3 position, string payload) =>
		OnBotCustomActionExecuted?.Invoke(playerIndex, actionId, intent, position, payload);

	public int RandomInt(int min, int max)
	{
		if (max <= min) return min;
		return SeededRandom.Shared.NextInt(min, max);
	}

	public float RandomFloat(float min, float max)
	{
		if (max <= min) return min;
		return min + SeededRandom.Shared.NextSingle() * (max - min);
	}
}
