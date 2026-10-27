using System.Diagnostics;
using System.Globalization;
using Realm.Ecs.Services;
using Realm.Shared.Maps;
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
	private bool _disposed;

	public ServerSimulation(LoadedMap map, ServerLog log)
	{
		_map = map;
		_log = log;
		_matchTick = new MatchTick(map.Simulation);

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
	///     statuses, regeneration and visibility catch up. The command pipeline is not wired to the transport yet, so a
	///     match nobody has ordered anything in only ages.
	/// </summary>
	public void Step()
	{
		MatchSeconds += FixedTickSeconds;
		TickCount++;

		float deltaSeconds = (float)FixedTickSeconds;
		_matchTick.AdvanceCooldowns(deltaSeconds);
		_map.Simulation.MovementPathfindingService.StepMovement(deltaSeconds);
		_map.Simulation.CombatDamageService.StepCombat(deltaSeconds, _map.Simulation.MovementPathfindingService);
		_map.Simulation.ResourceEconomyService.StepEconomy(deltaSeconds);
		_matchTick.AdvanceStatusEffects(deltaSeconds);
		_matchTick.AdvanceRegeneration(deltaSeconds);
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
	private readonly ManualResetEventSlim _stopRequested = new(false);
	private readonly Stopwatch _clock = Stopwatch.StartNew();

	private Thread? _thread;
	private double _nextTickTime;
	private long _lastReportedTick;
	private bool _disposed;

	public ServerTickLoop(ServerSimulation simulation, ServerLog log)
	{
		_simulation = simulation;
		_log = log;
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
