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

/// <summary>
///     The map scripting surface as the authority answers it. A map script's effects land in the server's own ECS
///     through the same services the simulation steps with, so what a script asks for is simulated once, by the
///     authority, and reaches every client as replicated state. Effects that only a screen can express are handed to
///     the presentation queue, addressed to the players that can see them.
/// </summary>
public partial class ServerGameAPI : IGameAPI
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

	/// <summary>The world this surface writes to, so the host that runs a script can hand it the same world.</summary>
	public EcsWorld World => _context.World;

	/// <summary>The languages a map's text is available in and the language each player connected in.</summary>
	public ServerLocalization Localization => _localization;

	/// <summary>How far each player is up each of the map's tech tracks.</summary>
	public ServerTechLevels TechLevels => _techLevels;

	/// <summary>The opponents this match plays itself.</summary>
	public ServerBots Bots => _bots;

	/// <summary>What puts items on units and takes them off again in this match.</summary>
	public ItemGrantService Items { get; }

	/// <summary>What drops, expires and sweeps up the loot lying on this match's map.</summary>
	public GroundLootService Loot { get; }

	/// <summary>How long the match has run, which is the clock a map script schedules against.</summary>
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

	/// <summary>A match the authority runs gives a script no shortcuts, so cheating is always off here.</summary>
	public bool CheatsEnabled => _context.CheatsEnabled;

	/// <summary>How many players the map has seated and left in the match.</summary>
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

	/// <summary>How many harvestable nodes the map still has in the world.</summary>
	public int ResourceNodeCount
	{
		get
		{
			_countingScratch = 0;
			_context.World.Query(in ResourceNodeQuery, _countResourceNodes);
			return _countingScratch;
		}
	}

	/// <summary>
	///     The players still in the match, in slot order. The buffer belongs to this surface and is refilled per call, so
	///     a script can enumerate players without making the authority allocate.
	/// </summary>
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

	/// <summary>The name a player joined under, or the slot's own name when the roster has none.</summary>
	public string GetPlayerName(int playerIndex)
	{
		if (TryReadPlayersState(out ScriptPlayersState playersState) && playersState.IsValidSlot(playerIndex))
		{
			string name = playersState.Get(playerIndex).Name;
			if (!string.IsNullOrEmpty(name)) return name;
		}

		return playerIndex == 0 ? "Neutral" : $"Player {playerIndex}";
	}

	/// <summary>Where a player's units start, which is where the map put the player's entity.</summary>
	public Vector3 GetPlayerStartLocation(int playerIndex)
	{
		Entity playerEntity = _context.GetPlayerEntity(playerIndex);
		if (playerEntity == Entity.Null) return Vector3.Zero;
		if (!_context.World.TryGet<Position>(playerEntity, out Position position)) return Vector3.Zero;

		return position.Value;
	}

	/// <summary>The team a player is on, or zero for a player the map left neutral.</summary>
	public int GetPlayerTeam(int playerIndex)
	{
		if (!TryReadPlayersState(out ScriptPlayersState playersState) || !playersState.IsValidSlot(playerIndex)) return 0;
		return playersState.Get(playerIndex).Team;
	}

	public float GetPlayerGold(int playerIndex) => ReadPlayerResource(playerIndex, PlayerResourceKind.Gold);

	public float GetPlayerWood(int playerIndex) => ReadPlayerResource(playerIndex, PlayerResourceKind.Wood);

	public float GetPlayerStone(int playerIndex) => ReadPlayerResource(playerIndex, PlayerResourceKind.Stone);

	/// <summary>Sets a player's gold where the match and every client read it: the script state and the replicated store.</summary>
	public void SetPlayerGold(int playerIndex, float amount) => WritePlayerResource(playerIndex, PlayerResourceKind.Gold, amount);

	public void SetPlayerWood(int playerIndex, float amount) => WritePlayerResource(playerIndex, PlayerResourceKind.Wood, amount);

	public void SetPlayerStone(int playerIndex, float amount) => WritePlayerResource(playerIndex, PlayerResourceKind.Stone, amount);

	public void AdjustPlayerGold(int playerIndex, float delta) => SetPlayerGold(playerIndex, GetPlayerGold(playerIndex) + delta);

	public void AdjustPlayerWood(int playerIndex, float delta) => SetPlayerWood(playerIndex, GetPlayerWood(playerIndex) + delta);

	public void AdjustPlayerStone(int playerIndex, float delta) => SetPlayerStone(playerIndex, GetPlayerStone(playerIndex) + delta);

	/// <summary>The population a player's buildings entitle them to.</summary>
	public void SetPlayerMaxPopulation(int playerIndex, int max) => _context.ResourceEconomyService.SetPlayerMaxPopulation(playerIndex, max);

	/// <summary>How many kills a player's scoreboard shows.</summary>
	public void SetPlayerKills(int playerIndex, int kills)
	{
		MutatePlayersState(playerIndex, (ref ScriptPlayersState playersState, int slot) => playersState.SetKills(slot, kills));
	}

	/// <summary>Seats or removes a player from the match, which is what a map's victory rules read.</summary>
	public void SetPlayerActive(int playerIndex, bool active)
	{
		MutatePlayersState(playerIndex, (ref ScriptPlayersState playersState, int slot) => playersState.SetActive(slot, active));
	}

	/// <summary>Puts a player on a team, which is what decides who may attack whom.</summary>
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

	/// <summary>
	///     Whether two players are on the same side. A player is always allied with themselves; otherwise the match
	///     answers from the relationship table the map's own alliances wrote, falling back to who shares a team.
	/// </summary>
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

	/// <summary>Records an alliance between two players in the relationship table the match resolves fights with.</summary>
	public void SetPlayersAllied(int playerIndex, int otherPlayerIndex, bool allied)
	{
		Entity worldEntity = _context.WorldEntity;
		if (worldEntity == Entity.Null || !_context.World.IsAlive(worldEntity) || !_context.World.Has<PlayerRelationshipMatrix>(worldEntity)) return;

		ref PlayerRelationshipMatrix relationships = ref _context.World.Get<PlayerRelationshipMatrix>(worldEntity);
		PlayerRelationship relationship = allied ? PlayerRelationship.Ally : PlayerRelationship.Neutral;
		relationships.SetRelationship(playerIndex, otherPlayerIndex, relationship);
		relationships.SetRelationship(otherPlayerIndex, playerIndex, relationship);
	}

	/// <summary>How many living units a player owns right now, counted from the authority's own world.</summary>
	public int CountUnitsOwnedByPlayer(int playerIndex)
	{
		Entity playerEntity = _context.GetPlayerEntity(playerIndex);
		if (playerEntity == Entity.Null) return 0;

		_countingScratch = 0;
		_countedOwner = playerEntity;
		_context.World.Query(in PlayerUnitsQuery, _countOwnedUnits);
		return _countingScratch;
	}

	/// <summary>The definition a spawned unit was built from.</summary>
	public string GetUnitTemplateId(UnitId unit)
	{
		Entity entity = ResolveUnit(unit);
		if (entity == Entity.Null) return string.Empty;
		if (!_context.World.TryGet<DefinitionId>(entity, out DefinitionId definitionId)) return string.Empty;

		return definitionId.Value;
	}

	/// <summary>The name a unit shows on screen, which is what the map called it when it was created.</summary>
	public string GetUnitInstanceName(UnitId unit)
	{
		Entity entity = ResolveUnit(unit);
		if (entity == Entity.Null) return string.Empty;
		if (!_context.World.TryGet<Name>(entity, out Name name)) return string.Empty;

		return name.Value;
	}

	/// <summary>The unit a script-visible id names, or an invalid one when nothing alive carries that id.</summary>
	public UnitId GetUnitById(int uniqueId)
	{
		if (uniqueId <= 0) return UnitId.Invalid;

		var entity = new Entity(uniqueId);
		return _context.World.IsAlive(entity) ? new UnitId(uniqueId) : UnitId.Invalid;
	}

	/// <summary>Sets a unit's hit points, clamped into the unit's own range; a unit taken to nothing is destroyed.</summary>
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

	/// <summary>Sets the hit points a unit can hold.</summary>
	public void SetUnitMaxHealth(UnitId unit, float value)
	{
		Entity entity = ResolveUnit(unit);
		if (entity == Entity.Null) return;
		if (!_context.World.TryGet<Health>(entity, out Health health)) return;

		health.Max = Math.Max(1f, value);
		health.Current = Math.Min(health.Current, health.Max);
		_context.World.Set(entity, health);
	}

	/// <summary>Sets a unit's mana, clamped into the unit's own range.</summary>
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

	/// <summary>Sets how much mana a unit recovers each second.</summary>
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

	/// <summary>Sets a unit's attack damage, which is the number its attacks resolve with.</summary>
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

	/// <summary>Sets the interval between a unit's attacks, which is its attack speed.</summary>
	public void SetUnitAttackSpeed(UnitId unit, float value)
	{
		Entity entity = ResolveUnit(unit);
		if (entity == Entity.Null) return;
		if (!_context.World.TryGet<Attack>(entity, out Attack attack)) return;

		attack.Cooldown = Math.Max(0.01f, value);
		_context.World.Set(entity, attack);
	}

	/// <summary>Sets the armor a unit's physical damage is resolved against.</summary>
	public void SetUnitArmor(UnitId unit, float value)
	{
		Entity entity = ResolveUnit(unit);
		if (entity == Entity.Null) return;
		if (!_context.World.TryGet<Resistances>(entity, out Resistances resistances)) return;

		resistances.Physical = value;
		_context.World.Set(entity, resistances);
	}

	/// <summary>Sets how fast a unit moves, which the movement step reads every tick.</summary>
	public void SetUnitSpeed(UnitId unit, float value)
	{
		Entity entity = ResolveUnit(unit);
		if (entity == Entity.Null) return;
		if (!_context.World.TryGet<MovementStats>(entity, out MovementStats movement)) return;

		movement.Speed = value;
		_context.World.Set(entity, movement);
	}

	/// <summary>Puts a unit at a place without asking the movement step to walk it there.</summary>
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

	/// <summary>Sets how large a unit is drawn and collided with.</summary>
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

	/// <summary>Freezes a unit where it stands: it takes no further orders and stops animating until it is thawed.</summary>
	public void SetUnitFrozen(UnitId unit, bool value)
	{
		Entity entity = ResolveUnit(unit);
		if (entity == Entity.Null) return;

		SetFrozenFlags(entity, value);
		if (value) _context.World.Remove<PathFollow>(entity);
	}

	public void SetUnitCommandsFrozen(UnitId unit, bool value) => SetUnitFlag<CommandsFrozen>(unit, value);

	/// <summary>Turns a unit's own targeting on or off, which is what a script does before scripting its fights.</summary>
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

	/// <summary>Hands a unit to another player, which is what a script does when it converts a unit.</summary>
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

	/// <summary>Takes a unit out of the world, which is what a script asks for when it wants something gone.</summary>
	public void DestroyUnit(UnitId unit, bool executeDespawnShader = true, bool playDeathAnimation = true)
	{
		Entity entity = ResolveUnit(unit);
		if (entity == Entity.Null) return;

		_context.World.Destroy(entity);
	}

	/// <summary>Kills a unit, which takes it out of the world the same way its own death would.</summary>
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

	/// <summary>Publishes what a player should hear, addressed to that player.</summary>
	public void SendMessageToPlayer(int playerIndex, string message)
	{
		if (string.IsNullOrEmpty(message)) return;

		_presentation.WriteText(PresentationKind.PlayerMessage, playerIndex, message, Vector3.Zero);
		_log.Write($"script message to player {playerIndex}: {message}");
	}

	/// <summary>Publishes what every player should hear.</summary>
	public void BroadcastMessage(string message)
	{
		if (string.IsNullOrEmpty(message)) return;

		_presentation.WriteText(PresentationKind.BroadcastMessage, -1, message, Vector3.Zero);
		_log.Write($"script message: {message}");
	}

	/// <summary>Publishes a line of feedback to one player's screen.</summary>
	public void ShowPlayerFeedbackText(int playerIndex, string text, Vector3 color)
	{
		_presentation.WriteText(PresentationKind.PlayerFeedbackText, playerIndex, text, color);
	}

	/// <summary>Publishes a line of feedback to every player's screen.</summary>
	public void ShowFeedbackText(string text, Vector3 color)
	{
		_presentation.WriteText(PresentationKind.FeedbackText, -1, text, color);
	}

	/// <summary>Publishes a number that floats over a place on the map.</summary>
	public void CreateFloatingText(string text, Vector3 position, Vector3 color, float duration)
	{
		if (string.IsNullOrEmpty(text)) return;

		_presentation.Write(PresentationKind.FloatingText, -1, 0, string.Empty, text, string.Empty, string.Empty, null,
			position, Vector3.Zero, color, 0, 0, 0f, 0f, duration, false);
	}

	/// <summary>Marks a place on one player's minimap.</summary>
	public void PingPlayerMinimap(int playerIndex, Vector3 position)
	{
		_presentation.Write(PresentationKind.MinimapPing, playerIndex, position);
	}

	/// <summary>Marks a place on the map for every player.</summary>
	public void SpawnTargetIndicator(Vector3 position, Vector3 color)
	{
		_presentation.Write(PresentationKind.TargetIndicator, -1, 0, string.Empty, string.Empty, string.Empty, string.Empty, null,
			position, Vector3.Zero, color, 0, 0, 0f, 0f, 0f, false);
	}

	/// <summary>Ends the match in victory, which every client reads from the match state.</summary>
	public void TriggerVictory()
	{
		_context.IsGameOver = true;
		_log.Write("the map declared victory");
	}

	/// <summary>Ends the match in defeat, which every client reads from the match state.</summary>
	public void TriggerDefeat()
	{
		_context.IsGameOver = true;
		_log.Write("the map declared defeat");
	}

	/// <summary>Takes one player out of the match, which the rest of the match reads as that slot being gone.</summary>
	public void TriggerPlayerDefeat(int playerIndex, string reason = "")
	{
		SetPlayerActive(playerIndex, false);
		_presentation.WriteText(PresentationKind.PlayerDefeated, playerIndex, reason, Vector3.Zero);
		_log.Write(string.IsNullOrEmpty(reason) ? $"player {playerIndex} defeated" : $"player {playerIndex} defeated ({reason})");
	}

	/// <summary>Declares one player's victory, which the match reports to that player.</summary>
	public void TriggerPlayerVictory(int playerIndex)
	{
		_presentation.Write(PresentationKind.PlayerVictory, playerIndex, Vector3.Zero);
		_log.Write($"player {playerIndex} victorious");
	}

	/// <summary>
	///     Brings one of a spell's summons into the world as the caster's own unit, which is how a summon ends up owned,
	///     replicated and simulated like any other unit rather than being something only the caster's screen knows about.
	/// </summary>
	private UnitId SpawnAbilitySummon(string unitTypeId, Vector3 position, int playerIndex)
	{
		var spawnPosition = new Vector2(position.X, position.Z);
		return SpawnUnitForPlayer(unitTypeId, spawnPosition, playerIndex, executeSpawnShader: false, bypassPopulation: true);
	}

	/// <summary>What a bot's own decision asked for, handed to the map that taught it the decision.</summary>
	private void ForwardBotCustomAction(int playerIndex, string actionId, string intent, Vector3 position, string payload) =>
		OnBotCustomActionExecuted?.Invoke(playerIndex, actionId, intent, position, payload);

	/// <summary>A whole number in a script-visible range, drawn from the match's own deterministic sequence.</summary>
	public int RandomInt(int min, int max)
	{
		if (max <= min) return min;
		return DeterministicRandom.Shared.NextInt(min, max);
	}

	/// <summary>A fraction in a script-visible range, drawn from the match's own deterministic sequence.</summary>
	public float RandomFloat(float min, float max)
	{
		if (max <= min) return min;
		return min + DeterministicRandom.Shared.NextSingle() * (max - min);
	}
}
