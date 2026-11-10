using System.Buffers;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using MemoryPack;
using Realm.Ecs;
using Realm.Ecs.Replication;
using Realm.Ecs.Services;
using Realm.Shared;
using Realm.Shared.Diagnostics;
using Realm.Shared.Maps;
using Realm.Shared.Network;
using Realm.Shared.Simulation;

namespace Realm.ListenServer;

/// <summary>the per-tick and shutdown paths.</summary>
public partial class ListenServerHost
	{
	public void Tick(double nowSeconds)
	{
		if (!IsRunning) return;

		double lobbyDelta = _lastTickSeconds <= 0 ? 0 : Math.Max(0, nowSeconds - _lastTickSeconds);
		_lastTickSeconds = nowSeconds;
		_lobby?.Tick(lobbyDelta);
		if (!IsRunning) return;

		PrepareLobbyWorldIfDue();

		// A lobby whose script is already running keeps its roster current while the players are still choosing seats. Once
		// the match exists the same script is the match's, so the lobby stops ticking it.
		if (_simulation == null) _lobbyScript?.Tick((float)lobbyDelta);

		ServerSimulation? simulation = _simulation;
		if (simulation == null) return;

		// Whether the match is held or running, what it is doing about pausing is restated every second. A hold needs it
		// because a match that says nothing at all for longer than a peer's liveness window is indistinguishable from one
		// that has gone away, and a running match needs it for the opposite reason: a screen that missed the frame which
		// lifted a pause would otherwise keep drawing a world that never moves again, with nothing left to correct it.
		if (nowSeconds >= _nextPauseHeartbeatSeconds)
		{
			_nextPauseHeartbeatSeconds = nowSeconds + PauseHeartbeatSeconds;
			BroadcastPauseState();
		}

		// A paused match holds its world where it is: nothing is stepped and nothing is published, so every screen keeps
		// the world it was last told about instead of watching it drift while nobody can act in it.
		if (_paused)
		{
			if (_resumeCountdownRemaining > 0)
			{
				_resumeCountdownRemaining -= lobbyDelta;
				if (_resumeCountdownRemaining <= 0)
				{
					ResumeMatch("the resume countdown ran out, so the match resumes");
					_nextTickTime = nowSeconds;
					return;
				}
			}

			_nextTickTime = nowSeconds + MatchTiming.FixedTickSeconds;
			return;
		}

		if (_nextTickTime <= 0) _nextTickTime = nowSeconds;

		if (nowSeconds >= _nextObstacleCheckTime)
		{
			_nextObstacleCheckTime = nowSeconds + ObstacleCheckIntervalSeconds;
			BroadcastNavMeshObstaclesWhenChanged();
		}

		int stepped = 0;
		while (nowSeconds >= _nextTickTime && stepped < MaximumTicksPerCall)
		{
			long tickStarted = LagProbe.IsEnabled ? LagProbe.Timestamp() : 0L;

			// The interval is taken before this tick does anything. Read after the work instead it reported the period
			// plus a whole tick of that work - forty-two milliseconds for a match stepping every thirty-three - which
			// made a room publishing at its full cadence look like one running a quarter slow.
			if (tickStarted != 0L)
			{
				if (_previousStepTimestamp != 0L) LagProbe.Add(LagStage.AuthorityTickIntervalMilliseconds, LagProbe.ElapsedMilliseconds(_previousStepTimestamp));
				_previousStepTimestamp = tickStarted;
			}

			_commandsThisTick.Clear();

			long simulateStarted = LagProbe.IsEnabled ? LagProbe.Timestamp() : 0L;
			simulation.Step();
			if (simulateStarted != 0L) LagProbe.Add(LagStage.AuthoritySimulateMilliseconds, LagProbe.ElapsedMilliseconds(simulateStarted));

			PublishTick(simulation.AdvanceSnapshotSequence());

			if (LagProbe.IsEnabled)
			{
				LagProbe.Count(LagEvent.AuthorityTicks);
				LagProbe.Add(LagStage.AuthorityTickWorkMilliseconds, LagProbe.ElapsedMilliseconds(tickStarted));

				// What this step paid to the collector and what it allocated, so a tick that cost sixty milliseconds can be
				// told apart from a tick that was stopped for sixty: one is work this process chose to do, the other is the
				// process not being on a core, and only the second one is invisible in every stage measured inside the step.
				TimeSpan gcPaused = GC.GetTotalPauseDuration();
				if (_previousGcPauseTicks > 0) LagProbe.Add(LagStage.AuthorityGcPausePerTickMilliseconds, (gcPaused - _previousGcPause).TotalMilliseconds);
				_previousGcPause = gcPaused;
				_previousGcPauseTicks++;

				long allocatedBytes = GC.GetAllocatedBytesForCurrentThread();
				if (_previousAllocatedBytes >= 0) LagProbe.Add(LagStage.AuthorityAllocatedKilobytesPerTick, (allocatedBytes - _previousAllocatedBytes) / 1024d);
				_previousAllocatedBytes = allocatedBytes;
			}

			_nextTickTime += MatchTiming.FixedTickSeconds;
			stepped++;
		}

		if (stepped > 1) LagProbe.Count(LagEvent.AuthorityTickCatchUpCalls);

		if (nowSeconds >= _nextTickTime) _nextTickTime = nowSeconds + MatchTiming.FixedTickSeconds;
	}

	private long _previousStepTimestamp;
	private TimeSpan _previousGcPause;
	private int _previousGcPauseTicks;
	private long _previousAllocatedBytes = -1;

	private const int MaximumTicksPerCall = 5;

	private const double PauseHeartbeatSeconds = 1.0;

	/// <summary>How long a player must wait between the pauses it asks for.</summary>
	private const int PauseCooldownSeconds = 60;

	/// <summary>How many times one player may pause a match before it has to carry on without it.</summary>
	private const int PausesPerPlayerPerMatch = 3;

	/// <summary>
	///     True when this match belongs to the one screen playing it: a single-player game or a playtest started from the
	///     map editor. Such a match is reachable on the loopback address only and seats the one player it was started for,
	///     so a pause holds nobody else up and is not metered.
	/// </summary>
	private bool IsSinglePlayerMatch => _arguments.Cheats;

	/// <summary>How long everyone is warned for before a paused match resumes.</summary>
	private const int ResumeCountdownSeconds = 5;

	private readonly int[] _pausesRemainingPerPlayerSlot = new int[EcsWorld.MAX_PLAYERS];
	private readonly double[] _pauseCooldownEndsAtSeconds = new double[EcsWorld.MAX_PLAYERS];
	private double _resumeCountdownRemaining;

	private double _nextPauseHeartbeatSeconds;

	private const int MaximumCommandsPerPeerPerTick = 32;

	private const long RateLimitReportIntervalMilliseconds = 1000;

	private const double ObstacleCheckIntervalSeconds = 1.0;

	public void Stop()
	{
		if (!IsRunning)
		{
			ShutdownRequested = true;
			return;
		}

		IsRunning = false;
		ShutdownRequested = true;
		_transport.Stop();

		if (_readyAnnounced) _log.Write($"the match delivered {_deliveredPresentationCount} presentation messages");
		if (_readyAnnounced) _log.Write($"the command rate limit refused {_rateLimitedCommandCount} command(s)");
		if (_readyAnnounced) _log.Write($"snapshots: {_unreliableSnapshotCount} carried as deltas on the current stream, {_reliableSnapshotCount} carried in order, {_fragmentedSnapshotCount} split across datagrams");

		LagProbe.WriteReport();
		QueueTrafficReport(includeSequenceCoverage: true);

		_seatedPeers.Clear();
		_frameRings.Clear();
		_connectedPeers.Clear();
		_peerLanguages.Clear();
		_sessionTokenSlots.Clear();
		_lobbySessionSlots.Clear();
		_lobby = null;
		_mapShares = null;

		_lobbyScript?.Dispose();
		_lobbyScript = null;

		_preparedLobbyWorld?.Dispose();
		_preparedLobbyWorld = null;
	}

	public void Dispose()
	{
		if (_disposed) return;
		_disposed = true;

		Stop();
		_transport.Dispose();

		QueueTrafficReport(includeSequenceCoverage: true);

		_simulation?.Dispose();
		_simulation = null;

		_publishLaneWorkers?.Dispose();
		_publishLaneWorkers = null;

		for (int index = 0; index < _publishCompressors.Length; index++)
		{
			_publishCompressors[index]?.Dispose();
			_publishCompressors[index] = null;
		}

		_syncRecorder?.Dispose();
		_syncRecorder = null;

		_reportWriter?.Dispose();
		_reportWriter = null;

		if (_scriptLogSink != null)
		{
			WasmScriptHost.OnWasmLog -= _scriptLogSink;
			_scriptLogSink = null;
		}
	}
}
