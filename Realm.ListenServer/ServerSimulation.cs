using System.Globalization;
using System.Numerics;
using Realm.Ecs;
using Realm.Ecs.Common;
using Realm.Ecs.Components.Combat;
using Realm.Ecs.Components.Core;
using Realm.Ecs.Components.Terrain;
using Realm.Ecs.Services;
using Realm.Shared.Maps;
using Realm.Shared.Network;
using Realm.Shared.Simulation;

namespace Realm.ListenServer;

/// <summary>
///     The authoritative match: the world the map loaded, and the fixed-rate clock it advances on regardless of who is
///     connected or how fast they are rendering.
/// </summary>
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
	/// <summary>
	/// The roster seats no peer holds yet. Sorted rather than queued, so a peer that reconnects after losing its
	/// connection takes the same seat it had: a roster of two humans that both reconnect must not swap their slots.
	/// </summary>
	private readonly SortedSet<int> _unseatedPlayerSlots = new();
	private readonly bool _hasRoster;
	private int _snapshotSequence;

	private int _nextPlayerSlot = MatchRoster.NeutralSlot + 1;
	private bool _disposed;
	private long _navMeshRebakeTick = -1;

	/// <param name="map">The map the match plays.</param>
	/// <param name="paths">Where the match's files are.</param>
	/// <param name="log">The log the match writes what it decided.</param>
	/// <param name="randomSeed">The seed the map's script draws from.</param>
	/// <param name="cheatsEnabled">Whether a player of this match may cheat.</param>
	/// <param name="wasmDebug">
	///     Whether the map's script is kept debug-able, which is what the editor's playtest asks for so a breakpoint in the
	///     map reaches the process the script runs in.
	/// </param>
	/// <param name="mapWorkspacePath">The workspace the script was compiled from, or nothing to work it out from the map.</param>
	/// <param name="roster">
	///     The players the lobby arranged, or nothing for a match seated as its peers connect. A roster names the slots
	///     people play, so a peer that connects takes the seat the lobby gave it rather than the next free one.
	/// </param>
	public ServerSimulation(LoadedMap map, ServerMatchPaths paths, ServerLog log, int randomSeed = 0, bool cheatsEnabled = false, bool wasmDebug = false, string? mapWorkspacePath = null, IReadOnlyList<MatchRosterSeat>? roster = null)
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
		_matchTick = new MatchTick(map.Simulation);
		_commands = new CommandAuthorityService(map.Simulation);
		_commands.DroppedOrderSink = log.Write;
		map.Catalog.BindTo(_commands);
		_snapshots = new SnapshotFanOut(map.World, map.Simulation, map.WorldEntity, map.Simulation.MapRules, SnapshotFanOut.DefaultWorkerCount);
		EnsureMatchClock(map);
		_production = new ServerProductionStep(map.World, map, log);
		_construction = new ConstructionTick(map.Simulation, map.Catalog);
		Items = new ItemGrantService(map.Simulation, map.Catalog);
		_loot = new GroundLootService(map.Simulation, Items);
		_script = ServerMapScript.Load(map, paths, randomSeed, log, _presentation, Items, _loot, wasmDebug, mapWorkspacePath);
		_snapshots.Abilities = _script.Api.Abilities;
		_snapshots.TechLevels = _script.Api.TechLevels;
		_script.Api.PlayerDroppedSink = DropPlayer;

		_events = new ServerSimulationEvents(map.World);
		_events.Attach(_script.Api);
		WireSimulationCallbacks();

		// A research order is authorized against how far the player is up the track, and granting the next level is what
		// changes the units they already own. Both live with the match's own tech state, so the authority is pointed at it
		// rather than keeping a second copy that could drift.
		_commands.TechLevelReader = ReadPlayerTechLevel;
		_commands.TechLevelWriter = WritePlayerTechLevel;

		PlayerResourceService resources = map.Simulation.PlayerResourceService;
		map.Simulation.ResourceEconomyService.SetRuntimeReferences(
			map.Simulation.DefinitionManager,
			resources.GoldResourceId,
			resources.WoodResourceId,
			resources.StoneResourceId);
	}

	/// <summary>
	///     Gives the match the world state every clock reads: the elapsed time a script schedules against, the time of
	///     day, and the countdown a map can show. A map that says nothing about them starts at zero and lets the match
	///     advance them.
	/// </summary>
	private static void EnsureMatchClock(LoadedMap map)
	{
		Entity worldEntity = map.WorldEntity;
		if (worldEntity == Entity.Null || !map.World.IsAlive(worldEntity)) return;

		if (!map.World.Has<WorldState>(worldEntity)) map.World.Add(worldEntity, new WorldState(0f, 0, 0f, false));
		if (!map.World.Has<CountdownState>(worldEntity)) map.World.Add(worldEntity, new CountdownState(false, 0f, string.Empty));
	}

	public LoadedMap Map => _map;

	/// <summary>
	///     The surface the map's own script answers through. A host runs the match rather than the map, so this is what a
	///     diagnostic or a harness reads when it has to report what the script did.
	/// </summary>
	public ServerGameAPI ScriptApi => _script.Api;

	/// <summary>What puts items on units and takes them off again in this match, from the map's own catalog.</summary>
	public ItemGrantService Items { get; }

	/// <summary>What drops, expires and sweeps up the loot lying on this match's map.</summary>
	public GroundLootService Loot => _loot;

	/// <summary>The shared timed steps this match advances, so the server can report what it is configured to run.</summary>
	public MatchTick MatchTick => _matchTick;

	/// <summary>How far the match has advanced, in simulated seconds.</summary>
	public double MatchSeconds { get; private set; }

	public long TickCount { get; private set; }

	/// <summary>
	///     Advances the world by exactly one tick, in the order the simulation expects: the clock and cooldowns drain,
	///     movement resolves the orders on the board, fights resolve, harvesting and production advance, then timed
	///     statuses, regeneration and visibility catch up. A peer's order is applied between ticks, never during one, so
	///     what a peer is told about is always a world one whole tick old.
	/// </summary>
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

		// The opponents the match plays itself decide after the map has had its say this tick, so a map that seats one
		// and hands it a profile is answered by a bot that already exists rather than by the default one.
		_script.Api.Bots.Step(deltaSeconds);
		_matchTick.AdvanceStatusEffects(deltaSeconds);
		_matchTick.AdvanceRegeneration(deltaSeconds);

		// What entered the world this tick is announced last, so a map told about a unit can already read everything
		// about it that this tick decided.
		_events.Step();
	}

	/// <summary>
	///     How many ticks pass between two counts of the food a player has used. A count walks every owned unit and
	///     every training queue, so it runs on a cadence rather than every tick.
	/// </summary>
	private const int PopulationRecomputeIntervalTicks = 5;

	/// <summary>
	///     Counts what each player's units and training queues cost and stores it as the food they have used. The total
	///     is derived from the world rather than kept as a running figure, so a unit that spawns, dies or has its
	///     training cancelled lands in the count on the next pass without anything having to remember it. This ran on
	///     the screen that hosted the match before the authority owned the world; the match keeps its own books now.
	/// </summary>
	private void RecomputePopulationWhenDue()
	{
		if (TickCount % PopulationRecomputeIntervalTicks != 0) return;

		_map.Simulation.ResourceEconomyService.RecomputeCurrentPopulation();
	}

	/// <summary>
	///     Hands the shared simulation the decisions only the authority can make: which units are enemies, what weapons a
	///     template carries, what happens to a unit whose hit points ran out, and what a map is told about each of those.
	///     The services are the same ones a screen ticks; what differs is who answers for the world they change.
	/// </summary>
	private void WireSimulationCallbacks()
	{
		SimulationContext simulation = _map.Simulation;

		simulation.CombatDamageService.EnemyPredicate = ArePlayersEnemies;
		simulation.CombatDamageService.UnitWeaponsProvider = definitionId => _map.Catalog.FindUnitOrBuilding(definitionId)?.Weapons;
		simulation.CombatDamageService.ItemGrantedWeaponsProvider = itemId => _map.Catalog.FindItem(itemId)?.GrantedWeapons;
		simulation.CombatDamageService.UnlimitedPowerProvider = () => simulation.UnlimitedPowerEnabled;
		simulation.CombatDamageService.OnKillUnitRequested = KillUnit;
		simulation.CombatDamageService.OnUnitDamagedCallback = (target, attacker, damage) =>
			_script.Api.RaiseUnitDamaged(target.Id, attacker.Id, damage);
		simulation.CombatDamageService.OnUnitAttackedCallback = (attacker, target) =>
			_script.Api.RaiseUnitAttacked(attacker.Id, target.Id);

		_matchTick.SummonExpired += KillUnit;
		_construction.Completed += site => _script.Api.RaiseConstructionFinished(site.Id);
	}

	/// <summary>
	///     Whether two players are on opposite sides, which is what a fight is resolved against. A map that seats allies
	///     says so in the relationship table, so the table answers rather than the player indexes.
	/// </summary>
	private bool ArePlayersEnemies(int playerIndex, int otherPlayerIndex)
	{
		if (playerIndex == otherPlayerIndex) return false;

		return !_script.Api.ArePlayersAllied(playerIndex, otherPlayerIndex);
	}

	/// <summary>
	///     Takes a unit out of the world when its hit points ran out. The map is told first, with whatever struck the last
	///     blow, so its own death rules see the unit as it stood rather than as an entity that is already gone.
	/// </summary>
	private void KillUnit(Entity entity)
	{
		if (!_map.World.IsAlive(entity)) return;

		Entity killer = _map.World.TryGet<LastAttacker>(entity, out LastAttacker lastAttacker) ? lastAttacker.Value : Entity.Null;
		_script.Api.RaiseUnitDied(entity.Id, killer.Id);

		if (_map.World.IsAlive(entity)) _map.World.Destroy(entity);
	}

	/// <summary>
	/// How often the match looks for obstacles the last bake did not have, in ticks.
	/// </summary>
	private const int ObstacleScanIntervalTicks = 20;

	/// <summary>
	/// How long a change waits before the mesh is rebuilt, so a wave of spawns - a player's starting buildings, a
	/// completed construction site - costs one bake instead of one per prop. Measured in ticks.
	/// </summary>
	private const int NavMeshRebakeDelayTicks = 10;

	/// <summary>
	///     Rebuilds the authority's navigation mesh when the world holds obstacles the mesh was not built from. A map's
	///     script spawns its blockers after the map loads, so the single bake taken at load knows about none of them; the
	///     mesh the match paths units through is the one every peer bakes from, so it has to be rebuilt from the volumes
	///     the world actually holds.
	/// </summary>
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

		Entity worldEntity = _map.WorldEntity;
		if (!_map.World.IsAlive(worldEntity) || !_map.World.Has<TerrainState>(worldEntity)) return;

		ref TerrainState terrain = ref _map.World.Get<TerrainState>(worldEntity);
		navMesh.BakeNavMesh(ref terrain);
		_map.World.Set(worldEntity, terrain);

		// The mesh is rebuilt when any obstacle volume differs from the baked ones, which includes a prop or building
		// that was moved rather than spawned or destroyed. The movement obstacle grid watches the obstacle count on its
		// own, so a move is the one case that has to be handed to it.
		_map.Simulation.MovementPathfindingService.InvalidateObstacleGrid();
		_log.Write($"navigation mesh rebaked with {navMesh.LastBakedObstacleCount} obstacle volumes");
	}

	/// <summary>
	///     Gives a peer the player it acts as, once. A peer that is not on the roster owns nothing, so its commands are
	///     refused rather than quietly applied to whoever happens to own the units. Returns false when the map has no
	///     slot left, which is a refusal the peer has to hear about.
	/// </summary>
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

	/// <summary>
	///     What the match wants a screen to show since the last time the host collected it. Handing them over empties the
	///     queue, so a request is delivered once rather than on every tick that follows it.
	/// </summary>
	public ServerPresentationEvent[] CollectPresentation()
	{
		return _presentation.Drain();
	}

	/// <summary>
	///     What a player that has just been seated has to be told for its screen to look like everyone else's: the cards
	///     and colours the map declared while it was setting the match up, which happened before this peer could hear
	///     any of it, and the labels the map has put over what it created. A screen that never hears these offers the
	///     default orders and names for everything, which is what a match whose map runs on the authority must not do.
	/// </summary>
	public ServerPresentationEvent[] DescribePresentationStateFor(int playerSlot)
	{
		ServerPresentationEvent[] declared = _presentation.ReplayDurable(playerSlot);

		var description = new List<ServerPresentationEvent>(declared.Length + 16);
		description.AddRange(declared);
		_script.Api.AppendStaticTextDescription(description);

		return description.Count == 0 ? Array.Empty<ServerPresentationEvent>() : description.ToArray();
	}

	/// <summary>The player slot a peer acts as, once it has been seated on the roster.</summary>
	public bool TryGetPlayerSlot(int peerId, out int playerSlot) => _peerPlayerSlots.TryGetValue(peerId, out playerSlot);

	/// <summary>
	///     What the match asks the host to end: the connection of the peer acting as a player the map dropped. The peer
	///     and its slot are given up here as well, so the slot can be seated again by whoever takes it next.
	/// </summary>
	public event Action<int>? PeerDropped;

	/// <summary>Records the language a player reads in, which is what the map's text is answered in for that player.</summary>
	public void SetPlayerLanguage(int playerSlot, string? language) => _script.Api.Localization.SetPlayerLanguage(playerSlot, language);

	/// <summary>Where this match keeps the data its map asked to persist, so the host can report it.</summary>
	public string MapSavedDataDirectory() => _script.Api.SavedDataDirectory;

	/// <summary>
	///     Ends the connection a player is playing through, which is what a map asking to drop a player means. The slot is
	///     forgotten here so a match that carries on does not report a player whose connection has gone.
	/// </summary>
	private void DropPlayer(int playerSlot)
	{
		int peerId = PeerIdForPlayerSlot(playerSlot);
		if (peerId == 0) return;

		ForgetPeer(peerId);
		_script.Api.Localization.ForgetPlayer(playerSlot);
		PeerDropped?.Invoke(peerId);
	}

	/// <summary>The peer that acts as a player slot, which is who owns what the world says that player owns.</summary>
	public int PeerIdForPlayerSlot(int playerSlot)
	{
		foreach (var pair in _peerPlayerSlots)
		{
			if (pair.Value == playerSlot) return pair.Key;
		}

		return 0;
	}

	/// <summary>
	///     The next number a description of the world carries. Every peer is described at the same sequence on the same
	///     tick, so a client that reconnects cannot be handed a description of an older world than the one it held.
	/// </summary>
	public int AdvanceSnapshotSequence()
	{
		return ++_snapshotSequence;
	}

	/// <summary>How many peers this match may be describing at the same time.</summary>
	public int SnapshotWorkerCount => _snapshots.WorkerCount;

	/// <summary>
	///     Tells the description workers which peer acts as each player slot. A unit record names the peer that owns it, so
	///     the whole fan-out has to agree on that mapping before any of them describes a unit.
	/// </summary>
	public void PreparePeerDescriptions()
	{
		Span<int> peerIdsByPlayerSlot = stackalloc int[EcsWorld.MAX_PLAYERS];
		peerIdsByPlayerSlot.Clear();

		foreach (var pair in _peerPlayerSlots)
		{
			if ((uint)pair.Value >= (uint)peerIdsByPlayerSlot.Length) continue;
			peerIdsByPlayerSlot[pair.Value] = pair.Key;
		}

		_snapshots.PrepareFor(peerIdsByPlayerSlot);
	}

	/// <summary>
	///     Describes the world to one peer as the player it acts as can see it. A peer that asks to be described from
	///     scratch is given a baseline rather than the changes since a description it may never have received. It is safe
	///     to call from several threads for several peers at once, which is how a tick describes a whole room.
	/// </summary>
	public WorldSnapshot BuildSnapshotForPeer(int peerId, int sequence, bool forceBaseline)
	{
		Entity viewer = TryGetPlayerEntity(peerId, out Entity playerEntity) ? playerEntity : Entity.Null;

		AuthoritativeSnapshotBuilder builder = _snapshots.Rent();
		try
		{
			return builder.Build(peerId, sequence, new PlayerEntity(viewer), forceBaseline);
		}
		finally
		{
			_snapshots.Return(builder);
		}
	}

	/// <summary>Forgets a peer that has gone, so the peer taking its place is described from scratch.</summary>
	public void ForgetPeer(int peerId)
	{
		_snapshots.ForgetPeer(peerId);
		// The seat comes back to the roster. Leaving it with the peer that left meant a peer whose connection dropped
		// burned a roster seat for the rest of the match, and the player who reconnected to play on was refused because
		// every seat was 'taken' by connections that no longer existed.
		if (_hasRoster && _peerPlayerSlots.Remove(peerId, out int releasedSlot)) _unseatedPlayerSlots.Add(releasedSlot);
		_lastAnsweredCommandIdPerPeer.Remove(peerId);
		_peersNeedingBaseline.Remove(peerId);
	}

	/// <summary>
	///     Marks a peer to be described from scratch. The world is described to every peer on the same tick from the same
	///     sequence, so a baseline is owed to the peer that asked rather than assembled beside the stream, where it would
	///     arrive under a sequence the peer had already moved past.
	/// </summary>
	public void RequestBaseline(int peerId) => _peersNeedingBaseline.Add(peerId);

	/// <summary>Takes the baseline a peer is owed, if it is owed one. Asking twice is answered once.</summary>
	public bool ConsumeBaselineRequest(int peerId) => _peersNeedingBaseline.Remove(peerId);

	/// <summary>The name a player slot is shown under, so a line of chat says who sent it.</summary>
	public string ReadPlayerName(int playerSlot)
	{
		Entity worldEntity = _map.WorldEntity;
		if (!_map.World.IsAlive(worldEntity) || !_map.World.Has<ScriptPlayersState>(worldEntity)) return $"Player {playerSlot}";

		ScriptPlayersState players = _map.World.Get<ScriptPlayersState>(worldEntity);
		if (!players.IsValidSlot(playerSlot)) return $"Player {playerSlot}";

		string name = players.Get(playerSlot).Name;
		return string.IsNullOrWhiteSpace(name) ? $"Player {playerSlot}" : name;
	}

	/// <summary>
	///     Hands a line a player typed in chat to the map that owns its meaning. A map's own commands are chat lines, so
	///     the match passes them on with the player they came from and lets the map decide what they do.
	/// </summary>
	public void ApplyChatLine(int peerId, string message)
	{
		if (!_peerPlayerSlots.TryGetValue(peerId, out int playerSlot)) return;
		if (!ChatLineLimits.IsAcceptable(message)) return;

		_script.Api.RaisePlayerChatMessage(playerSlot, message.Trim(), 0);
	}

	/// <summary>The team a player slot belongs to, so a peer can tell who its allies are before it is told anything else.</summary>
	public int ReadTeam(int playerSlot)
	{
		Entity worldEntity = _map.WorldEntity;
		if (!_map.World.IsAlive(worldEntity) || !_map.World.Has<ScriptPlayersState>(worldEntity)) return 0;

		ScriptPlayersState players = _map.World.Get<ScriptPlayersState>(worldEntity);
		return players.IsValidSlot(playerSlot) ? players.Get(playerSlot).Team : 0;
	}

	/// <summary>
	///     Validates a peer's order against the world, applies the parts of it that are legal, and carries out what the
	///     order asked the world to do. An order the peer has already had answered is dropped rather than answered
	///     twice: a second answer would tell a predicting client to roll back an order it was already told to keep.
	/// </summary>
	public ServerCommandResult ApplyCommand(int peerId, NetworkCommand command)
	{
		if (IsAlreadyAnswered(peerId, command.CommandId))
		{
			_log.Write($"peer {peerId} repeated command {command.CommandId}; it was already answered and is dropped");
			return NoResult("command was already answered");
		}

		ServerCommandResult result = _commands.ExecuteServerCommand(peerId, command);
		PlaceBuildingOrderedBy(command, result);
		return result;
	}

	/// <summary>
	///     Hands a line a player typed to the map that owns its meaning. The map's own console commands are the map's
	///     business, so the authority passes the line on with the player it came from and the unit they had selected.
	/// </summary>
	public void ApplyConsoleCommand(int peerId, ScriptConsoleCommand command)
	{
		if (!_peerPlayerSlots.TryGetValue(peerId, out int playerSlot)) return;
		if (!ScriptConsoleCommandLimits.IsAcceptable(command.Command)) return;

		_script.Api.RaisePlayerChatMessage(playerSlot, command.Command.Trim(), command.SelectedUnitEntityId);
	}

	/// <summary>
	///     How far a player is up one of the map's tech tracks, which is what a research order is checked against. The
	///     match's own tech state answers, so an order is authorized against the levels the world is playing at rather
	///     than against whatever the peer asking for it believes.
	/// </summary>
	private int ReadPlayerTechLevel(int playerIndex, string techId)
	{
		return _script.Api.TechLevels.Get(playerIndex, techId);
	}

	/// <summary>
	///     Grants a player a level on a tech track. Granting it is what applies the level's modifiers to the units they
	///     already own, and the level is then published to every screen, so the ones offering the next level can show the
	///     one the player is on.
	/// </summary>
	private void WritePlayerTechLevel(int playerIndex, string techId, int level)
	{
		_script.Api.SetPlayerTechLevel(playerIndex, techId, level);
	}

	private bool IsAlreadyAnswered(int peerId, int commandId)
	{
		if (commandId <= 0) return true;
		if (_lastAnsweredCommandIdPerPeer.TryGetValue(peerId, out int lastAnswered) && commandId <= lastAnswered) return true;

		_lastAnsweredCommandIdPerPeer[peerId] = commandId;
		return false;
	}

	/// <summary>
	///     Builds what a peer's order asked for, once the order passed validation. A worker is handed the site and walks
	///     to it; an order from no worker places the finished building on the spot.
	/// </summary>
	private void PlaceBuildingOrderedBy(NetworkCommand command, in ServerCommandResult result)
	{
		if (!result.NeedsBuildUnit) return;
		if (!TryGetPlayerEntity(result.BuildPeerOwner, out Entity playerEntity)) return;

		Entity workerEntity = Entity.Null;
		if (result.BuildWorkerServerEntityId >= 0)
		{
			workerEntity = new Entity(result.BuildWorkerServerEntityId);
			if (!_map.World.IsAlive(workerEntity)) return;
			if (!_map.World.TryGet<Owner>(workerEntity, out Owner owner) || owner.PlayerEntity.Value != playerEntity) return;
		}

		var buildPosition = new Vector3(result.BuildPosition.X, result.BuildPosition.Y, result.BuildPosition.Z);
		if (!BuildingPlacementService.TryPlace(_map.World, _map.Catalog, result.BuildUnitType, buildPosition, new PlayerEntity(playerEntity), workerEntity, out Entity buildingEntity))
		{
			_log.Write($"'{command.CommandType}' naming '{result.BuildUnitType}' could not be placed: the player cannot pay for it");
			return;
		}

		_log.Write(workerEntity == Entity.Null
			? $"placed building {buildingEntity.Id} '{result.BuildUnitType}' for entity {playerEntity.Id}"
			: $"worker {workerEntity.Id} is building {buildingEntity.Id} '{result.BuildUnitType}' for entity {playerEntity.Id}");
	}

	private bool TryGetPlayerEntity(int peerId, out Entity playerEntity)
	{
		playerEntity = Entity.Null;
		if (!_peerPlayerSlots.TryGetValue(peerId, out int playerSlot)) return false;

		playerEntity = _map.GetPlayerEntity(playerSlot).Value;
		return playerEntity != Entity.Null && _map.World.IsAlive(playerEntity);
	}

	private static ServerCommandResult NoResult(string rejectionReason)
	{
		return new ServerCommandResult(
			false, string.Empty, Vector3.Zero, -1, -1,
			false, string.Empty, Vector3.Zero,
			new List<int>(), new List<int>(),
			new List<int>(), rejectionReason);
	}

	/// <summary>A one-line account of what the map loaded, for the match log.</summary>
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

		// The world's component arrays live inside the script sandbox's linear memory, so the world has to be cleared
		// while that memory is still mapped. Freeing the sandbox first would leave the world writing into memory it no
		// longer owns, which is a crash at the end of every match.
		_map.Dispose();
		_script.Dispose();
	}
}
