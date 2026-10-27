using System.Diagnostics;
using System.Globalization;
using System.Numerics;
using System.Threading;
using Realm.Ecs;
using Realm.Ecs.Common;
using Realm.Ecs.Components.Core;
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
	/// <summary>The rate the authoritative world advances at. Every client interpolates from the same cadence.</summary>
	public const double FixedTickSeconds = 1.0 / 20.0;

	private readonly LoadedMap _map;
	private readonly ServerLog _log;
	private readonly MatchTick _matchTick;
	private readonly CommandAuthorityService _commands;
	private readonly Dictionary<int, int> _peerPlayerSlots = new();
	private readonly Dictionary<int, int> _lastAnsweredCommandIdPerPeer = new();
	private readonly AuthoritativeSnapshotBuilder _snapshots;
	private readonly ServerProductionStep _production;
	private int _snapshotSequence;

	/// <summary>
	///     Serializes the tick against the peer threads that hand it commands. One world is stepped by one thread at a
	///     time, so a command cannot be applied halfway through the tick that is reading the same components.
	/// </summary>
	private readonly object _worldGate = new();

	private int _nextPlayerSlot = MatchRoster.NeutralSlot + 1;
	private bool _disposed;

	public ServerSimulation(LoadedMap map, ServerLog log)
	{
		_map = map;
		_log = log;
		_matchTick = new MatchTick(map.Simulation);
		_commands = new CommandAuthorityService(map.Simulation);
		_commands.DroppedOrderSink = log.Write;
		map.Catalog.BindTo(_commands);
		_snapshots = new AuthoritativeSnapshotBuilder(map.World, map.WorldEntity, map.Simulation.MapRules, PeerIdForPlayerSlot);
		_production = new ServerProductionStep(map.World, map, log);

		PlayerResourceService resources = map.Simulation.PlayerResourceService;
		map.Simulation.ResourceEconomyService.SetRuntimeReferences(
			map.Simulation.DefinitionManager,
			resources.GoldResourceId,
			resources.WoodResourceId,
			resources.StoneResourceId);
	}

	public LoadedMap Map => _map;

	/// <summary>The shared timed steps this match advances, so the server can report what it is configured to run.</summary>
	public MatchTick MatchTick => _matchTick;

	/// <summary>How far the match has advanced, in simulated seconds.</summary>
	public double MatchSeconds { get; private set; }

	public long TickCount { get; private set; }

	/// <summary>
	///     Advances the world by exactly one tick, in the order the simulation expects: the clock and cooldowns drain,
	///     movement resolves the orders on the board, fights resolve, harvesting and production advance, then timed
	///     statuses, regeneration and visibility catch up. A peer's order arrives from its own thread and is applied
	///     between ticks, so what a peer is told about is always a world one whole tick old.
	/// </summary>
	public void Step()
	{
		lock (_worldGate)
		{
			MatchSeconds += FixedTickSeconds;
			TickCount++;

			float deltaSeconds = (float)FixedTickSeconds;
			_matchTick.AdvanceCooldowns(deltaSeconds);
			_map.Simulation.MovementPathfindingService.StepMovement(deltaSeconds);
			_map.Simulation.CombatDamageService.StepCombat(deltaSeconds, _map.Simulation.MovementPathfindingService);
			_map.Simulation.ResourceEconomyService.StepEconomy(deltaSeconds);
			_production.Step(deltaSeconds);
			_matchTick.AdvanceStatusEffects(deltaSeconds);
			_matchTick.AdvanceRegeneration(deltaSeconds);
		}
	}

	/// <summary>
	///     Gives a peer the player it acts as, once. A peer that is not on the roster owns nothing, so its commands are
	///     refused rather than quietly applied to whoever happens to own the units. Returns false when the map has no
	///     slot left, which is a refusal the peer has to hear about.
	/// </summary>
	public bool TryAssignPlayerSlot(int peerId, out int playerSlot, out int team)
	{
		team = 0;

		lock (_worldGate)
		{
			if (_peerPlayerSlots.TryGetValue(peerId, out playerSlot))
			{
				team = ReadTeam(playerSlot);
				return true;
			}

			if (_nextPlayerSlot >= _map.PlayerEntities.Length)
			{
				playerSlot = -1;
				return false;
			}

			playerSlot = _nextPlayerSlot++;
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
		lock (_worldGate)
		{
			return ++_snapshotSequence;
		}
	}

	/// <summary>
	///     Describes the world to one peer as the player it acts as can see it. A peer that asks to be described from
	///     scratch is given a baseline rather than the changes since a description it may never have received.
	/// </summary>
	public WorldSnapshot BuildSnapshotForPeer(int peerId, int sequence, bool forceBaseline)
	{
		lock (_worldGate)
		{
			Entity viewer = TryGetPlayerEntity(peerId, out Entity playerEntity) ? playerEntity : Entity.Null;
			return _snapshots.Build(peerId, sequence, new PlayerEntity(viewer), forceBaseline);
		}
	}

	/// <summary>Forgets a peer that has gone, so the peer taking its place is described from scratch.</summary>
	public void ForgetPeer(int peerId)
	{
		lock (_worldGate)
		{
			_snapshots.ForgetPeer(peerId);
			_peerPlayerSlots.Remove(peerId);
			_lastAnsweredCommandIdPerPeer.Remove(peerId);
		}
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
		lock (_worldGate)
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
		_map.Dispose();
	}
}

/// <summary>
///     Runs the authoritative match on its own thread at a fixed rate, sleeping only the time that is left over after
///     each tick, so a slow tick costs cadence instead of piling up.
/// </summary>
public class ServerTickLoop : IDisposable
{
	private const int IdleSleepMilliseconds = 1;

	private readonly ServerSimulation _simulation;
	private readonly ServerLog _log;
	private readonly Action<int>? _onTickCompleted;
	private readonly ManualResetEventSlim _stopRequested = new(false);
	private readonly Stopwatch _clock = Stopwatch.StartNew();

	private Thread? _thread;
	private double _nextTickTime;
	private long _lastReportedTick;
	private bool _disposed;

	public ServerTickLoop(ServerSimulation simulation, ServerLog log, Action<int>? onTickCompleted = null)
	{
		_simulation = simulation;
		_log = log;
		_onTickCompleted = onTickCompleted;
	}

	public void Start()
	{
		if (_thread != null) return;

		_nextTickTime = _clock.Elapsed.TotalSeconds;
		_thread = new Thread(Run)
		{
			IsBackground = true,
			Name = "ListenServerTick"
		};
		_thread.Start();
	}

	public void Stop()
	{
		_stopRequested.Set();
		Thread? thread = _thread;
		_thread = null;
		if (thread != null && thread != Thread.CurrentThread) thread.Join();
	}

	public void Dispose()
	{
		if (_disposed) return;
		_disposed = true;
		Stop();
		_stopRequested.Dispose();
	}

	private void Run()
	{
		while (!_stopRequested.IsSet)
		{
			double now = _clock.Elapsed.TotalSeconds;
			if (now < _nextTickTime)
			{
				_stopRequested.Wait(IdleSleepMilliseconds);
				continue;
			}

			_simulation.Step();
			_onTickCompleted?.Invoke(_simulation.AdvanceSnapshotSequence());

			_nextTickTime += ServerSimulation.FixedTickSeconds;
			if (_nextTickTime < now)
			{
				_nextTickTime = now + ServerSimulation.FixedTickSeconds;
			}

			ReportProgress();
		}
	}

	private void ReportProgress()
	{
		long tick = _simulation.TickCount;
		if (tick - _lastReportedTick < TicksPerReport) return;

		_lastReportedTick = tick;
		_log.Write(string.Format(CultureInfo.InvariantCulture, "tick={0} match={1:F1}s elapsed={2:F1}s", tick, _simulation.MatchSeconds, _clock.Elapsed.TotalSeconds));
	}

	private const long TicksPerReport = 100;
}
