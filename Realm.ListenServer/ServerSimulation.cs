using System.Globalization;
using System.Numerics;
using Realm.Ecs;
using Realm.Ecs.Common;
using Realm.Ecs.Components.Combat;
using Realm.Ecs.Components.Core;
using Realm.Ecs.Components.Resources;
using Realm.Ecs.Components.Terrain;
using Realm.Ecs.Replication;
using Realm.Ecs.Services;
using Realm.Shared.Maps;
using Realm.Shared.Network;
using Realm.Shared.Simulation;

namespace Realm.ListenServer;

public class ServerSimulation : IDisposable
{
	private readonly LoadedMap _map;
	private readonly ServerLog _log;
	private readonly MatchTick _matchTick;
	private readonly CommandAuthorityService _commands;
	private readonly Dictionary<int, int> _peerPlayerSlots = new();
	private readonly Dictionary<int, int> _lastAnsweredCommandIdPerPeer = new();
	private readonly SnapshotFanOut _snapshots;
	private readonly ServerProductionStep _production;
	private readonly ConstructionTick _construction;
	private readonly ServerSimulationEvents _events;
	private readonly ServerPresentationQueue _presentation = new();
	private readonly ServerMapScript _script;
	private readonly GroundLootService _loot;
	private readonly HashSet<int> _peersNeedingBaseline = new();
	private readonly HashSet<int> _peersNeedingDescriptionRepeat = new();
	private readonly SortedSet<int> _unseatedPlayerSlots = new();
	private readonly bool _hasRoster;
	private int _snapshotSequence;

	private int _nextPlayerSlot = MatchRoster.NeutralSlot + 1;
	private bool _disposed;
	private long _navMeshRebakeTick = -1;

	public ServerSimulation(LoadedMap map, ServerMatchPaths paths, ServerLog log, int randomSeed = 0, bool cheatsEnabled = false, bool wasmDebug = false, string? mapWorkspacePath = null, IReadOnlyList<MatchRosterSeat>? roster = null, int hostPlayerSlot = 0, WasmScriptHost? preparedScriptHost = null, string? scriptPathOverride = null)
	{
		_map = map;
		_hasRoster = roster is { Count: > 0 };
		if (roster != null)
		{
			for (int index = 0; index < roster.Count; index++)
			{
				if (!roster[index].IsComputer) _unseatedPlayerSlots.Add(roster[index].Slot);
			}
		}
		_log = log;
		map.Simulation.CheatsEnabled = cheatsEnabled;
		map.Simulation.HostPlayerSlot = hostPlayerSlot;

		// The stream the simulation and its script draw from is established here, by the owner of the match, before
		// anything can step or describe it.
		SeededRandom.SeedShared(randomSeed);
		_matchTick = new MatchTick(map.Simulation);
		_commands = new CommandAuthorityService(map.Simulation);
		_commands.DroppedOrderSink = log.Write;
		_commands.CostsPaidSink = (playerSlot, goldAmount) => Metrics.RecordGoldSpent(playerSlot, goldAmount);
		map.Catalog.BindTo(_commands);
		_snapshots = new SnapshotFanOut(map.World, map.Simulation, map.WorldEntity, map.Simulation.MapRules, SnapshotFanOut.DefaultWorkerCount);
		_snapshots.Report = log.Write;
		_snapshots.WarmDescriptionQueries();
		EnsureMatchClock(map);
		_production = new ServerProductionStep(map.World, map, log);
		_construction = new ConstructionTick(map.Simulation, map.Catalog);
		Items = new ItemGrantService(map.Simulation, map.Catalog);
		_loot = new GroundLootService(map.Simulation, Items);
		_script = ServerMapScript.Load(map, paths, randomSeed, log, _presentation, Items, _loot, wasmDebug, mapWorkspacePath, preparedScriptHost, scriptPathOverride);
		if (roster != null) ApplyRosterBotDifficulties(roster);
		_script.Api.Metrics = Metrics;
		_commands.ConsumableChangedSink = (unitId, consumableId) => _script.Api.RaiseConsumableChanged(unitId, consumableId);
		_snapshots.Abilities = _script.Api.Abilities;
		_snapshots.TechLevels = _script.Api.TechLevels;
		_script.Api.PlayerDroppedSink = DropPlayer;

		_events = new ServerSimulationEvents(map.World);
		_events.Attach(_script.Api);
		WireSimulationCallbacks();

		_commands.TechLevelReader = ReadPlayerTechLevel;
		_commands.TechLevelWriter = WritePlayerTechLevel;

		PlayerResourceService resources = map.Simulation.PlayerResourceService;
		map.Simulation.ResourceEconomyService.SetRuntimeReferences(
			map.Simulation.DefinitionManager,
			resources.GoldResourceId,
			resources.WoodResourceId,
			resources.StoneResourceId);

		BakeNavMesh();
	}

	private void ApplyRosterBotDifficulties(IReadOnlyList<MatchRosterSeat> roster)
	{
		for (int index = 0; index < roster.Count; index++)
		{
			MatchRosterSeat seat = roster[index];
			if (!seat.IsComputer) continue;

			LobbyBotDifficulty difficulty = LobbyBotDifficultyRules.Clamp(seat.Difficulty);
			_script.Api.Bots.SetHandicap(seat.Slot, LobbyBotDifficultyRules.MistakeChance(difficulty));
		}
	}

	private static void EnsureMatchClock(LoadedMap map)
	{
		Entity worldEntity = map.WorldEntity;
		if (worldEntity == Entity.Null || !map.World.IsAlive(worldEntity)) return;

		if (!map.World.Has<WorldState>(worldEntity)) map.World.Add(worldEntity, new WorldState(0f, 0, 0f, false));
		if (!map.World.Has<CountdownState>(worldEntity)) map.World.Add(worldEntity, new CountdownState(false, 0f, string.Empty));
		if (!map.World.Has<ScriptZonesState>(worldEntity)) map.World.Add(worldEntity, new ScriptZonesState(null));
	}

	public LoadedMap Map => _map;

	public ServerGameAPI ScriptApi => _script.Api;

	public ItemGrantService Items { get; }

	public GroundLootService Loot => _loot;

	public MatchMetrics Metrics { get; } = new();

	public bool IsGameOver => _map.Simulation.IsGameOver;

	public MatchTick MatchTick => _matchTick;

	public double MatchSeconds { get; private set; }

	public long TickCount { get; private set; }

	public void Step()
	{
		MatchSeconds += MatchTiming.FixedTickSeconds;
		TickCount++;

		float deltaSeconds = (float)MatchTiming.FixedTickSeconds;
		_matchTick.AdvanceCooldowns(deltaSeconds);
		_map.Simulation.MovementPathfindingService.StepMovement(deltaSeconds);
		_map.Simulation.CombatDamageService.StepCombat(deltaSeconds, _map.Simulation.MovementPathfindingService);
		_map.Simulation.ResourceEconomyService.StepEconomy(deltaSeconds);
		_production.Step(deltaSeconds);
		_construction.Step(deltaSeconds);
		_loot.Step(deltaSeconds);
		_script.Step(deltaSeconds);
		_script.Api.AdvanceTimers(deltaSeconds);
		RecomputePopulationWhenDue();
		RefreshNavMeshWhenObstaclesChange();

		_script.Api.Bots.Step(deltaSeconds);
		_matchTick.AdvanceStatusEffects(deltaSeconds);
		_matchTick.AdvanceRegeneration(deltaSeconds);

		_events.Step();
	}

	private const int PopulationRecomputeIntervalTicks = 5;

	private void RecomputePopulationWhenDue()
	{
		if (TickCount % PopulationRecomputeIntervalTicks != 0) return;

		_map.Simulation.ResourceEconomyService.RecomputeCurrentPopulation();
	}

	private void WireSimulationCallbacks()
	{
		SimulationContext simulation = _map.Simulation;

		simulation.CombatDamageService.EnemyPredicate = ArePlayersEnemies;
		simulation.CombatDamageService.UnitWeaponsProvider = definitionId => _map.Catalog.FindUnitOrBuilding(definitionId)?.Weapons;
		simulation.CombatDamageService.ItemGrantedWeaponsProvider = itemId => _map.Catalog.FindItem(itemId)?.GrantedWeapons;
		simulation.CombatDamageService.UnlimitedPowerProvider = () => simulation.UnlimitedPowerEnabled;
		simulation.CombatDamageService.OnKillUnitRequested = KillUnit;
		simulation.CombatDamageService.OnUnitDamagedCallback = (target, attacker, damage) =>
		{
			if (_map.World.TryGet<UnitOwnerPlayer>(attacker, out UnitOwnerPlayer damagingOwner))
			{
				Metrics.RecordDamageDealt(damagingOwner.Value, damage);
			}

			_script.Api.RaiseUnitDamaged(target.Id, attacker.Id, damage);
		};
		simulation.CombatDamageService.OnUnitAttackedCallback = (attacker, target) =>
			_script.Api.RaiseUnitAttacked(attacker.Id, target.Id);

		_matchTick.SummonExpired += KillUnit;
		_construction.Completed += site => _script.Api.RaiseConstructionFinished(site.Id);
	}

	private bool ArePlayersEnemies(int playerIndex, int otherPlayerIndex)
	{
		if (playerIndex == otherPlayerIndex) return false;

		return !_script.Api.ArePlayersAllied(playerIndex, otherPlayerIndex);
	}

	private void KillUnit(Entity entity)
	{
		if (!_map.World.IsAlive(entity)) return;

		Entity killer = _map.World.TryGet<LastAttacker>(entity, out LastAttacker lastAttacker) ? lastAttacker.Value : Entity.Null;
		_script.Api.RaiseUnitDied(entity.Id, killer.Id);

		if (_map.World.IsAlive(entity)) _map.World.Destroy(entity);
	}

	private const int ObstacleScanIntervalTicks = 20;

	private const int NavMeshRebakeDelayTicks = 10;

	private void RefreshNavMeshWhenObstaclesChange()
	{
		if (TickCount % ObstacleScanIntervalTicks != 0) return;

		TerrainNavMeshService navMesh = _map.Simulation.TerrainNavMeshService;
		if (!navMesh.ObstaclesChangedSinceBake())
		{
			_navMeshRebakeTick = -1;
			return;
		}

		if (_navMeshRebakeTick < 0)
		{
			_navMeshRebakeTick = TickCount + NavMeshRebakeDelayTicks;
			return;
		}

		if (TickCount < _navMeshRebakeTick) return;
		_navMeshRebakeTick = -1;

		BakeNavMesh();
	}

	private void BakeNavMesh()
	{
		Entity worldEntity = _map.WorldEntity;
		if (!_map.World.IsAlive(worldEntity) || !_map.World.Has<TerrainState>(worldEntity)) return;

		TerrainNavMeshService navMesh = _map.Simulation.TerrainNavMeshService;
		ref TerrainState terrain = ref _map.World.Get<TerrainState>(worldEntity);
		navMesh.BakeNavMesh(ref terrain);
		_map.World.Set(worldEntity, terrain);

		_map.Simulation.MovementPathfindingService.InvalidateObstacleGrid();
		_log.Write($"navigation mesh baked with {navMesh.LastBakedObstacleCount} obstacle volumes");
	}

	public bool TryAssignPlayerSlot(int peerId, out int playerSlot, out int team)
	{
		team = 0;

		if (_peerPlayerSlots.TryGetValue(peerId, out playerSlot))
		{
			team = ReadTeam(playerSlot);
			return true;
		}

		if (_hasRoster)
		{
			if (_unseatedPlayerSlots.Count == 0)
			{
				playerSlot = -1;
				return false;
			}

			playerSlot = _unseatedPlayerSlots.Min;
			_unseatedPlayerSlots.Remove(playerSlot);
		}
		else
		{
			if (_nextPlayerSlot >= _map.PlayerEntities.Length)
			{
				playerSlot = -1;
				return false;
			}

			playerSlot = _nextPlayerSlot++;
		}

		_peerPlayerSlots[peerId] = playerSlot;

		Entity worldEntity = _map.WorldEntity;
		if (_map.World.IsAlive(worldEntity) && _map.World.Has<NetworkMappingState>(worldEntity))
		{
			NetworkMappingState mapping = _map.World.Get<NetworkMappingState>(worldEntity);
			mapping.PeerIdToPlayerEntityMap[peerId] = _map.GetPlayerEntity(playerSlot).Value;
		}

		team = ReadTeam(playerSlot);
		return true;
	}

	public bool SeatPeerAtSlot(int peerId, int playerSlot)
	{
		if (playerSlot <= 0 || playerSlot >= _map.PlayerEntities.Length) return false;
		if (_peerPlayerSlots.TryGetValue(peerId, out int existing)) return existing == playerSlot;

		foreach (var pair in _peerPlayerSlots)
		{
			if (pair.Value == playerSlot) return false;
		}

		_peerPlayerSlots[peerId] = playerSlot;
		_unseatedPlayerSlots.Remove(playerSlot);

		Entity worldEntity = _map.WorldEntity;
		if (_map.World.IsAlive(worldEntity) && _map.World.Has<NetworkMappingState>(worldEntity))
		{
			NetworkMappingState mapping = _map.World.Get<NetworkMappingState>(worldEntity);
			mapping.PeerIdToPlayerEntityMap[peerId] = _map.GetPlayerEntity(playerSlot).Value;
		}

		return true;
	}

	public ReadOnlySpan<ServerPresentationEvent> CollectPresentation()
	{
		return _presentation.Drain();
	}

	public ServerPresentationEvent[] DescribePresentationStateFor(int playerSlot)
	{
		ServerPresentationEvent[] declared = _presentation.ReplayDurable(playerSlot);

		var description = new List<ServerPresentationEvent>(declared.Length + 16);
		description.AddRange(declared);
		_script.Api.AppendStaticTextDescription(description);

		return description.Count == 0 ? Array.Empty<ServerPresentationEvent>() : description.ToArray();
	}

	public bool TryGetPlayerSlot(int peerId, out int playerSlot) => _peerPlayerSlots.TryGetValue(peerId, out playerSlot);

	public event Action<int>? PeerDropped;

	public void SetPlayerLanguage(int playerSlot, string? language) => _script.Api.Localization.SetPlayerLanguage(playerSlot, language);

	public string MapSavedDataDirectory() => _script.Api.SavedDataDirectory;

	private void DropPlayer(int playerSlot)
	{
		int peerId = PeerIdForPlayerSlot(playerSlot);
		if (peerId == 0) return;

		ForgetPeer(peerId);
		_script.Api.Localization.ForgetPlayer(playerSlot);
		PeerDropped?.Invoke(peerId);
	}

	public int PeerIdForPlayerSlot(int playerSlot)
	{
		foreach (var pair in _peerPlayerSlots)
		{
			if (pair.Value == playerSlot) return pair.Key;
		}

		return 0;
	}

	public int AdvanceSnapshotSequence()
	{
		return ++_snapshotSequence;
	}

	public int SnapshotWorkerCount => _snapshots.WorkerCount;

	private readonly List<AbilityStateDelta>?[] _abilityDeltasByPeerSlot = new List<AbilityStateDelta>?[EcsWorld.MAX_PLAYERS];

	private static readonly List<AbilityStateDelta> NoAbilityDeltas = new();

	public void PreparePeerDescriptions(int sequence)
	{
		Span<int> peerIdsByPlayerSlot = stackalloc int[EcsWorld.MAX_PLAYERS];
		peerIdsByPlayerSlot.Clear();

		foreach (var pair in _peerPlayerSlots)
		{
			if ((uint)pair.Value >= (uint)peerIdsByPlayerSlot.Length) continue;
			peerIdsByPlayerSlot[pair.Value] = pair.Key;
		}

		_snapshots.PrepareFor(peerIdsByPlayerSlot);

		// A board every peer is told about whole is collected once here rather than by each lane in turn, so the
		// description of a peer never waits on another lane describing the same board.
		if (_map.Simulation.ShroudDisabled) _snapshots.PrepareWholeBoard(sequence);
	}

	public WorldSnapshot BuildSnapshotForPeer(int peerId, int sequence, bool forceBaseline)
	{
		Entity viewer = TryGetPlayerEntity(peerId, out Entity playerEntity) ? playerEntity : Entity.Null;
		_snapshots.EnterPeerDescription(peerId);
		try
		{
			return _snapshots.GetLane(0).Build(peerId, sequence, new PlayerEntity(viewer), forceBaseline);
		}
		finally
		{
			_snapshots.ExitPeerDescription(peerId);
		}
	}

	/// <summary>
	///     Collects what every peer's ability deltas are computed from, once per tick. The ability list a unit publishes
	///     and the disabled/hidden flags it carries are the same for every screen, so collecting them inside the per-peer
	///     pass made a room of twelve pay twelve times for work it had already done: with twelve peers that was the whole
	///     tick. It is a step of its own because it is once per tick and the per-peer pass beside it is not.
	/// </summary>
	public void PrepareAbilityBookkeeping() => _snapshots.Abilities?.PrepareTick(_map.World);

	/// <summary>
	///     The ability deltas each seated peer is owed this tick, collected once on the tick thread before the publish
	///     lanes are forked. The registry keeps one scratch table per process, so collecting per peer inside a lane would
	///     put every lane behind one lock for the whole pass.
	/// </summary>
	public void PrepareAbilityDeltas(ReadOnlySpan<int> peerIds, ReadOnlySpan<bool> describeInFull, ReadOnlySpan<bool> skipThisTick)
	{
		ScriptAbilityRegistry? abilities = _snapshots.Abilities;
		int count = Math.Min(peerIds.Length, _abilityDeltasByPeerSlot.Length);

		for (int index = 0; index < count; index++)
		{
			// A peer that is told nothing this tick is not collected for: collecting marks its deltas as told, and a
			// delta marked told for a peer that was never sent it is one that peer will never hear about.
			if (index < skipThisTick.Length && skipThisTick[index])
			{
				_abilityDeltasByPeerSlot[index] = null;
				continue;
			}

			// Each peer's deltas are its own list: one list handed to every peer left all of them holding the last peer's
			// deltas, so a screen was told about another screen's units and never about its own.
			List<AbilityStateDelta> buffer = _abilityDeltasByPeerSlot[index] ??= new List<AbilityStateDelta>();
			_abilityDeltasByPeerSlot[index] = abilities != null ? abilities.CollectDeltas(peerIds[index], _map.World, describeInFull[index], buffer) : NoAbilityDeltas;
		}
	}

	public void BuildSnapshotForPeerInto(ref WorldSnapshot snapshot, int peerId, int sequence, bool forceBaseline, int laneIndex, int peerSlot)
	{
		Entity viewer = TryGetPlayerEntity(peerId, out Entity playerEntity) ? playerEntity : Entity.Null;
		List<AbilityStateDelta> preparedAbilityDeltas = (uint)peerSlot < (uint)_abilityDeltasByPeerSlot.Length
			? _abilityDeltasByPeerSlot[peerSlot] ?? NoAbilityDeltas
			: NoAbilityDeltas;

		AuthoritativeSnapshotBuilder builder = _snapshots.GetLane(laneIndex);
		_snapshots.EnterPeerDescription(peerId);
		try
		{
			builder.BuildInto(ref snapshot, peerId, sequence, new PlayerEntity(viewer), forceBaseline, preparedAbilityDeltas);
		}
		finally
		{
			_snapshots.ExitPeerDescription(peerId);
		}
	}

	public void ForgetPeer(int peerId)
	{
		_snapshots.ForgetPeer(peerId);
		if (_hasRoster && _peerPlayerSlots.Remove(peerId, out int releasedSlot)) _unseatedPlayerSlots.Add(releasedSlot);
		_lastAnsweredCommandIdPerPeer.Remove(peerId);
		_peersNeedingBaseline.Remove(peerId);
		_peersNeedingDescriptionRepeat.Remove(peerId);
	}

	public void RequestBaseline(int peerId) => _peersNeedingBaseline.Add(peerId);

	public bool ConsumeBaselineRequest(int peerId) => _peersNeedingBaseline.Remove(peerId);

	/// <summary>
	///     Asks for this peer to be told about every unit and every text again. It is the answer to a description the
	///     screen reported it could not apply, and it is stated as an ordinary description rather than as a baseline: the
	///     screen is missing what one description carried, not the world it was built from.
	/// </summary>
	public void RequestDescriptionRepeat(int peerId) => _peersNeedingDescriptionRepeat.Add(peerId);

	public bool ConsumeDescriptionRepeatRequest(int peerId) => _peersNeedingDescriptionRepeat.Remove(peerId);

	/// <summary>
	///     Drops what this peer is remembered as having been told, so the next description restates it all. It is written on
	///     the tick thread before the publish lanes are forked, which is the only thread that may write a peer's record.
	/// </summary>
	public void ResetPeerDescription(int peerId) => _snapshots.ResetPeerDescription(peerId);

	public string ReadPlayerName(int playerSlot)
	{
		Entity worldEntity = _map.WorldEntity;
		if (!_map.World.IsAlive(worldEntity) || !_map.World.Has<ScriptPlayersState>(worldEntity)) return $"Player {playerSlot}";

		ScriptPlayersState players = _map.World.Get<ScriptPlayersState>(worldEntity);
		if (!players.IsValidSlot(playerSlot)) return $"Player {playerSlot}";

		string name = players.Get(playerSlot).Name;
		return string.IsNullOrWhiteSpace(name) ? $"Player {playerSlot}" : name;
	}

	public void ApplyChatLine(int peerId, string message)
	{
		if (!_peerPlayerSlots.TryGetValue(peerId, out int playerSlot)) return;
		if (!ChatLineLimits.IsAcceptable(message)) return;

		_script.Api.RaisePlayerChatMessage(playerSlot, message.Trim(), 0);
	}

	public int ReadTeam(int playerSlot)
	{
		Entity worldEntity = _map.WorldEntity;
		if (!_map.World.IsAlive(worldEntity) || !_map.World.Has<ScriptPlayersState>(worldEntity)) return 0;

		ScriptPlayersState players = _map.World.Get<ScriptPlayersState>(worldEntity);
		return players.IsValidSlot(playerSlot) ? players.Get(playerSlot).Team : 0;
	}

	public ServerCommandResult ApplyCommand(int peerId, NetworkCommand command)
	{
		if (IsAlreadyAnswered(peerId, command.CommandId))
		{
			_log.Write($"peer {peerId} repeated command {command.CommandId}; it was already answered and is dropped");
			return NoResult("command was already answered", wasAlreadyAnswered: true);
		}

		ServerCommandResult result = _commands.ExecuteServerCommand(peerId, command);
		result = PlaceBuildingOrderedBy(command, result);
		return CastSpellOrderedBy(command, result);
	}

	public void ApplyConsoleCommand(int peerId, ScriptConsoleCommand command)
	{
		if (!_peerPlayerSlots.TryGetValue(peerId, out int playerSlot)) return;
		if (!ScriptConsoleCommandLimits.IsAcceptable(command.Command)) return;

		_script.Api.RaisePlayerChatMessage(playerSlot, command.Command.Trim(), command.SelectedUnitEntityId);
	}

	private int ReadPlayerTechLevel(int playerIndex, string techId)
	{
		return _script.Api.TechLevels.Get(playerIndex, techId);
	}

	private void WritePlayerTechLevel(int playerIndex, string techId, int level)
	{
		_script.Api.SetPlayerTechLevel(playerIndex, techId, level);
		Metrics.RecordUpgradeBought(playerIndex);
	}

	private bool IsAlreadyAnswered(int peerId, int commandId)
	{
		if (commandId <= 0) return true;
		if (_lastAnsweredCommandIdPerPeer.TryGetValue(peerId, out int lastAnswered) && commandId <= lastAnswered) return true;

		_lastAnsweredCommandIdPerPeer[peerId] = commandId;
		return false;
	}

	private ServerCommandResult PlaceBuildingOrderedBy(NetworkCommand command, in ServerCommandResult result)
	{
		if (!result.NeedsBuildUnit) return result;
		string? templateId = result.BuildUnitType;
		if (templateId == null) return RefusePlacement($"'{command.CommandType}' asked for a building without naming one");
		if (!TryGetPlayerEntity(result.BuildPeerOwner, out Entity playerEntity)) return RefusePlacement($"'{command.CommandType}' naming '{templateId}' arrived from peer {result.BuildPeerOwner}, which holds no player entity");

		Entity workerEntity = Entity.Null;
		if (result.BuildWorkerServerEntityId >= 0)
		{
			workerEntity = new Entity(result.BuildWorkerServerEntityId);
			if (!_map.World.IsAlive(workerEntity)) return RefusePlacement($"'{command.CommandType}' naming '{templateId}' named worker {result.BuildWorkerServerEntityId}, which is not a live unit");
			if (!_map.World.TryGet<Owner>(workerEntity, out Owner owner) || owner.PlayerEntity.Value != playerEntity)
			{
				return RefusePlacement($"'{command.CommandType}' naming '{templateId}' named worker {result.BuildWorkerServerEntityId}, which this player does not own");
			}
		}

		var buildPosition = new Vector3(result.BuildPosition.X, result.BuildPosition.Y, result.BuildPosition.Z);
		if (!BuildingPlacementService.TryPlace(_map.World, _map.Catalog, templateId, buildPosition, new PlayerEntity(playerEntity), workerEntity, out Entity buildingEntity))
		{
			return RefusePlacement(_map.Catalog.FindUnitOrBuilding(templateId) == null
				? $"'{command.CommandType}' naming '{templateId}' could not be placed: this map defines no such template"
				: $"'{command.CommandType}' naming '{templateId}' could not be placed: the player cannot pay for it");
		}

		_log.Write(workerEntity == Entity.Null
			? $"placed building {buildingEntity.Id} '{templateId}' for entity {playerEntity.Id}"
			: $"worker {workerEntity.Id} is building {buildingEntity.Id} '{templateId}' for entity {playerEntity.Id}");

		// A placement nothing has to be built for is finished the moment it is made, and the tick that builds sites never
		// sees it, so its completion is raised here. A map that reacts to a finished construction - a draft that is played
		// out by the site it consumes, a building that unlocks something - would otherwise never hear about a placement it
		// accepted, and the site would stand there finished and inert.
		if (!_map.World.Has<ConstructionState>(buildingEntity)) _script.Api.RaiseConstructionFinished(buildingEntity.Id);

		return result;
	}

	/// <summary>
	///     Runs an accepted spell order through the map script. Command authority has already checked that the peer owns
	///     the caster, can pay for the spell and is aiming inside its range; this is where the cast actually happens, so a
	///     script that reacts to <see cref="ServerGameAPI.OnSpellCast"/> - a tile reroll, a swap, a script-defined spell -
	///     sees the order instead of the click being authorized and then dropped.
	/// </summary>
	private ServerCommandResult CastSpellOrderedBy(NetworkCommand command, in ServerCommandResult result)
	{
		if (!result.NeedsSpellEffect) return result;
		if (command.UnitEntityIds == null || command.UnitEntityIds.Count == 0) return result;

		Entity casterEntity = _commands.Targets.Resolve(command.UnitEntityIds[0]);
		if (casterEntity == Entity.Null) return result;

		_script.Api.CastAbility(new UnitId(casterEntity.Id), result.SpellId ?? string.Empty, result.SpellPosition);
		return result;
	}

	/// <summary>
	///     Answers a placement this match will not make. Returning from the placement without an answer reported it as
	///     accepted, so the screen that sent it had nothing to show and the player only saw a click do nothing.
	/// </summary>
	private ServerCommandResult RefusePlacement(string reason)
	{
		_log.Write(reason);
		return NoResult(reason);
	}

	private bool TryGetPlayerEntity(int peerId, out Entity playerEntity)
	{
		playerEntity = Entity.Null;
		if (!_peerPlayerSlots.TryGetValue(peerId, out int playerSlot)) return false;

		playerEntity = _map.GetPlayerEntity(playerSlot).Value;
		return playerEntity != Entity.Null && _map.World.IsAlive(playerEntity);
	}

	private static ServerCommandResult NoResult(string rejectionReason, bool wasAlreadyAnswered = false)
	{
		return new ServerCommandResult(
			false, string.Empty, Vector3.Zero, -1, -1,
			false, string.Empty, Vector3.Zero,
			new List<int>(), new List<int>(),
			new List<int>(), rejectionReason, wasAlreadyAnswered);
	}

	public string Describe()
	{
		return string.Format(
			CultureInfo.InvariantCulture,
			"map '{0}' grid={1}x{2} players={3} units={4} props={5} navmesh={6}",
			_map.Metadata.MapProperties?.MapName ?? "unnamed",
			_map.Width,
			_map.Depth,
			_map.PlayerCount,
			_map.UnitCount,
			_map.PropCount,
			_map.HasNavMesh ? "baked" : "absent");
	}

	public void Dispose()
	{
		if (_disposed) return;
		_disposed = true;

		_map.Dispose();
		_script.Dispose();
	}
}
