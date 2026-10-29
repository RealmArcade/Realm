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

/// <summary>
///     One match's authority: the world the map loaded, and the peers the transport reports. Nothing here owns a
///     thread or a socket. Time is given to <see cref="Tick" /> by whatever drives the process, and frames are given to
///     <see cref="OnFrameReceived" /> from that same call path, so a command is applied between ticks and never inside
///     one.
/// </summary>
public class ListenServerHost : IServerTransportSink
{
	private readonly ServerArguments _arguments;
	private readonly ServerLog _log;
	private readonly IServerTransport _transport;
	private readonly Dictionary<int, int> _seatedPeers = new();
	private readonly Dictionary<int, int> _commandsThisTick = new();

	/// <summary>The challenge each connected peer has been handed and not yet answered.</summary>
	private readonly Dictionary<int, string> _pendingChallenges = new();

	private ServerSimulation? _simulation;

	/// <summary>The lobby this authority holds before its match starts, or nothing for a match started without one.</summary>
	private ServerLobby? _lobby;

	/// <summary>The files of the map the lobby selected, served to the peers that joined it.</summary>
	private ServerMapShares? _mapShares;

	/// <summary>
	///     The map this lobby built while it waited for its match: everything about it that does not depend on who is
	///     playing. The match starts on it by seating the roster the lobby arranged, or loads a map of its own when there
	///     is none.
	/// </summary>
	private LoadedMap? _preparedLobbyWorld;

	/// <summary>Whether this lobby has already tried to prepare its world, so a map that cannot be prepared is tried once.</summary>
	private bool _preparedLobbyWorldAttempted;

	/// <summary>Every connection the transport has reported, seated or not, so the match can be started without leaving anyone attached.</summary>
	private readonly HashSet<int> _connectedPeers = new();

	/// <summary>The language each peer said it reads in, applied to the seat the lobby gave it once the match seats it.</summary>
	private readonly Dictionary<int, string> _peerLanguages = new();

	private double _lastTickSeconds;
	private double _nextTickTime;
	private readonly Dictionary<int, long> _lastLoggedAckSequencePerPeer = new();
	private readonly Dictionary<int, int> _firstSnapshotSequencePerPeer = new();

	/// <summary>
	///     The last tick this match asked for a peer to be described from scratch, whichever reason asked for it. Every
	///     path that can entitle a peer to a baseline is answered from this one clock, which is what keeps a peer that
	///     asks - on every frame, or once for every acknowledgement it loses - from costing this process more than one
	///     baseline a second.
	/// </summary>
	private readonly Dictionary<int, long> _lastBaselineRequestTickPerPeer = new();
	private readonly Dictionary<int, long> _lastRefreshBaselineTickPerPeer = new();

	/// <summary>The tick a peer was seated on, which is what bounds the grace a screen that has not answered once is given.</summary>
	private readonly Dictionary<int, long> _seatedAtTickPerPeer = new();

	/// <summary>
	///     How many descriptions this peer has been sent since it last acknowledged one, or negative for a peer that has
	///     not acknowledged anything yet. This is not how far behind a screen is - an acknowledgement is one round trip old
	///     when it arrives, so a healthy link always leaves descriptions unanswered - but it is what says whether a screen
	///     is reading at all, and a peer that has left a quarter of a minute of publishing unanswered is dropped by it.
	/// </summary>
	private readonly Dictionary<int, int> _unacknowledgedPublicationsPerPeer = new();

	/// <summary>
	///     The newest sequence each peer has said it applied. Taken against the sequence being published, this is how far
	///     behind that screen actually is: a peer that is keeping up answers with a sequence one round trip of publishing
	///     behind, whatever the link's latency is, while a screen that stopped drawing falls further behind every tick.
	/// </summary>
	private readonly Dictionary<int, int> _acknowledgedSequencePerPeer = new();

	/// <summary>
	///     The round trip each peer last measured, in milliseconds, carried in its acknowledgement. It is what the gate
	///     above allows a peer to be behind for reasons that are the link's rather than the screen's.
	/// </summary>
	private readonly Dictionary<int, int> _roundTripMillisecondsPerPeer = new();

	/// <summary>
	///     How often a peer is described from scratch even though nothing was reported wrong, in ticks. Every section of a
	///     delta states a change against what that peer was already told, so a peer that lost track of one component
	///     without losing a whole snapshot — a replica it replaced, a component the authority dropped while the unit was
	///     out of its sight — would hold the stale value until something else happened to ask for a baseline. Ten seconds
	///     bounds any such drift to ten seconds: a baseline is 2.6 kB per peer, which is forty kilobytes a second of a
	///     full room's six point nine megabits.
	/// </summary>
	private const int SnapshotRefreshTicks = MatchTiming.SimulationTickRateHz * 10;
	private readonly Dictionary<int, int> _obstacleSignaturePerPeer = new();
	private readonly int[] _seatedPeerIds = new int[EcsWorld.MAX_PLAYERS];
	private readonly List<(int PlayerIndex, byte[] Payload)> _presentationPayloads = new();
	private readonly WorldSnapshot[] _publishedSnapshots = new WorldSnapshot[EcsWorld.MAX_PLAYERS];
	private readonly byte[][] _publishedFrames = new byte[EcsWorld.MAX_PLAYERS][];
	private readonly bool[] _pendingBaselineRequests = new bool[EcsWorld.MAX_PLAYERS];
	private readonly bool[] _pendingSkippedPeers = new bool[EcsWorld.MAX_PLAYERS];
	private readonly SnapshotBaselineReason[] _pendingBaselineReasons = new SnapshotBaselineReason[EcsWorld.MAX_PLAYERS];
	private readonly int[] _publishedSerializedBytes = new int[EcsWorld.MAX_PLAYERS];
	private readonly double[] _publishedFramingMilliseconds = new double[EcsWorld.MAX_PLAYERS];

	/// <summary>
	///     One scratch buffer per peer slot, which the thread describing that slot owns for the length of one tick. It is
	///     what a description is serialized into, so the per-tick path allocates the frame it sends rather than a copy of
	///     the snapshot beside it.
	/// </summary>
	private readonly ArrayBufferWriter<byte>[] _serializationWriters = CreateSerializationWriters();

	private static ArrayBufferWriter<byte>[] CreateSerializationWriters()
	{
		var writers = new ArrayBufferWriter<byte>[EcsWorld.MAX_PLAYERS];
		for (int index = 0; index < writers.Length; index++) writers[index] = new ArrayBufferWriter<byte>(4096);
		return writers;
	}

	private ParallelOptions? _publishParallelism;
	private Action<int>? _describeOnePeer;
	private int _publishSequence;
	private bool _readyAnnounced;
	private bool _disposed;
	private bool _paused;
	private int _pausedByPlayerSlot = -1;
	private double _nextObstacleCheckTime;
	private string _mapHash = string.Empty;

	/// <summary>
	///     How long this process has spent starting the match it is starting. The gap between the countdown reaching zero
	///     and the first description is what a player waits through, and this process is the only one that knows what it
	///     was spent on, so each stage of it is written to the match's own log.
	/// </summary>
	private readonly Stopwatch _startClock = new();

	/// <summary>Writes one stage of the start to the match's log, with how far into the start it finished.</summary>
	private void MarkStartStage(string stage)
	{
		_log.Write($"match start: {stage} at {_startClock.Elapsed.TotalMilliseconds:F0} ms");
	}

	/// <summary>
	///     Builds the part of the map that does not depend on who is playing it, once this lobby has someone to play it
	///     with. What is left when the countdown reaches zero is then the seats, the map's script and the first
	///     description, rather than the terrain and the catalog as well; a lobby nobody has joined costs nothing.
	///     It runs on the thread the match ticks on rather than a worker of its own, because the world it builds is the
	///     world the match will tick and one thread owns a world in this process. A lobby has no tick to hold up, so the
	///     work is done whole rather than in stages.
	/// </summary>
	private void PrepareLobbyWorldIfDue()
	{
		if (_preparedLobbyWorld != null || _preparedLobbyWorldAttempted) return;

		ServerLobby? lobby = _lobby;
		if (lobby == null || _simulation != null || !lobby.HasHumanSeat) return;

		_preparedLobbyWorldAttempted = true;
		Stopwatch clock = Stopwatch.StartNew();
		try
		{
			_preparedLobbyWorld = MapWorldLoader.LoadShell(_arguments.MapPath);
			_log.Write($"the map's world is prepared while this lobby is open ({clock.Elapsed.TotalMilliseconds:F0} ms); the match starts on its seats");
		}
		catch (Exception exception)
		{
			// Preparing early is work saved rather than work required: a map that cannot be prepared here is loaded when the
			// match starts, where the failure is reported as it always was rather than as a lobby that cannot open.
			_preparedLobbyWorld = null;
			_log.Write($"the map's world could not be prepared, so the match will load it when it starts: {exception.Message}");
		}
	}

	/// <summary>
	///     The map the match is about to play: the world this lobby prepared, seated with the roster it arranged, or a map
	///     loaded from scratch when nothing was prepared. The caller owns the result.
	/// </summary>
	private LoadedMap TakePreparedLobbyWorldOrLoad(IReadOnlyList<MatchRosterSeat> roster)
	{
		LoadedMap? prepared = _preparedLobbyWorld;
		_preparedLobbyWorld = null;

		// A folder the world was built from and that is no longer there is a map this match cannot play: the load path
		// reports that, where a world prepared from it would have quietly run without the script it can no longer find.
		if (prepared != null && !Directory.Exists(_arguments.MapPath))
		{
			prepared.Dispose();
			prepared = null;
		}

		if (prepared == null) return MapWorldLoader.Load(_arguments.MapPath, roster);

		try
		{
			MapWorldLoader.Seat(prepared, roster);
			return prepared;
		}
		catch
		{
			prepared.Dispose();
			throw;
		}
	}

	/// <summary>
	///     The seat each session identity owned when a lobby handed its roster to this match, keyed by the identity the
	///     client minted. A match a lobby arranged is closed to everyone the lobby did not seat, and a seat whose
	///     connection dropped is handed back to the identity that owned it rather than to whoever connects next.
	/// </summary>
	private readonly Dictionary<string, int> _lobbySessionSlots = new(StringComparer.Ordinal);
	private long _deliveredPresentationCount;
	private int _rateLimitedCommandCount;
	private long _nextRateLimitReportTicks;
	private Action<string>? _scriptLogSink;
	private AuthoritySyncRecorder? _syncRecorder;
	private SnapshotTrafficRecorder? _trafficRecorder;

	/// <summary>
	///     Writes what this match has put on the wire so far. It is written periodically as well as at the end, because an
	///     authority that is ended without being asked leaves the run that launched it nothing to read: a report rewritten
	///     every ten seconds is the difference between a measurement and a missing file.
	/// </summary>
	private void WriteTrafficReport()
	{
		SnapshotTrafficRecorder? recorder = _trafficRecorder;
		if (recorder == null) return;

		double recordedSeconds = (_simulation?.TickCount ?? 0) * MatchTiming.FixedTickSeconds;
		recorder.WriteReport(_arguments.RecordAuthorityPath, recordedSeconds, MatchTiming.SimulationTickRateHz);
	}

	/// <summary>How often the frame report is rewritten while a match runs, in published sequences.</summary>
	private const int TrafficReportIntervalSequences = MatchTiming.SnapshotRateHz * 10;

	public ListenServerHost(ServerArguments arguments, ServerLog log, IServerTransport transport)
	{
		_arguments = arguments;
		_log = log;
		_transport = transport;
	}

	public bool IsRunning { get; private set; }

	/// <summary>Whether the match has been asked to end, by a peer or by its own map.</summary>
	public bool ShutdownRequested { get; private set; }

	public int Port => _transport.Port;

	/// <summary>The authority's own log, so a failure can be reported with the file that explains it.</summary>
	public ServerLog Log => _log;

	/// <summary>
	///     Loads the map, builds the world, and starts listening. The line the launcher waits for is printed here, once,
	///     after the match is actually able to answer a peer.
	/// </summary>
	public void Start()
	{
		if (IsRunning) return;
		if (!Directory.Exists(_arguments.MapPath))
		{
			throw new DirectoryNotFoundException($"The map folder '{_arguments.MapPath}' does not exist.");
		}

		_startClock.Restart();

		if (_arguments.Lobby)
		{
			StartLobby();
			return;
		}

		// What the sandbox reports about the script it is running - the debug mode it started in, the process a debugger
		// attaches to and the JIT ELF it exported - belongs in this match's own log, beside the rest of what it decided.
		_scriptLogSink = _log.Write;
		WasmScriptHost.OnWasmLog += _scriptLogSink;

		MatchRosterSeat[] roster = ReadRoster();
		LoadedMap map = MapWorldLoader.Load(_arguments.MapPath, roster);
		MarkStartStage("map loaded");
		try
		{
			var paths = ServerMatchPaths.From(_arguments);
			_simulation = new ServerSimulation(map, paths, _log, _arguments.Seed, _arguments.Cheats, _arguments.WasmDebug, _arguments.WorkspacePath, roster);
			MarkStartStage("simulation built");
			_simulation.PeerDropped += DisconnectPeer;
			_log.Write($"map '{paths.MapName}' keeps its saved data under '{_simulation.MapSavedDataDirectory()}'");
			_log.Write(_simulation.Describe());
			for (int index = 0; index < map.Warnings.Count; index++)
			{
				_log.Write($"map warning: {map.Warnings[index]}");
			}

			if (_arguments.Cheats)
			{
				_log.Write("cheats are enabled: this match is reachable on the loopback address only and seats the one player it was started for");
			}

			if (_arguments.WasmDebug)
			{
				_log.Write($"map script debug mode is on: pid={Environment.ProcessId} workspace='{_arguments.WorkspacePath}'");
			}

			if (!_transport.TryStart(EcsWorld.MAX_PLAYERS, _arguments.Cheats, this, out string failure))
			{
				throw new IOException($"the match could not listen for peers: {failure}");
			}

			MarkStartStage("listening");

			if (_arguments.RecordAuthorityPath.Length > 0)
			{
				// A run that compares a screen's ECS against this one asks for the same folder, because the process that
				// decided the world is the only one that can say what a screen should hold after applying a sequence.
				_syncRecorder = new AuthoritySyncRecorder(map, _arguments.RecordAuthorityPath, out string syncRecordPath);
				_log.Write($"this match records the world it publishes into '{syncRecordPath}'");
				// What each peer's frame weighed and what framing it cost is known only here, because the frames are built in
				// this process; the folder the other records go to is where a run reads it back from.
				_trafficRecorder = new SnapshotTrafficRecorder();
			}

			IsRunning = true;
			_mapHash = MapContentHash.Compute(_arguments.MapPath, _arguments.LogPath);
			MarkStartStage("ready to be joined");
			AnnounceReady();
		}
		catch
		{
			_simulation?.Dispose();
			_simulation = null;
			throw;
		}
	}

	/// <summary>
	///     Opens the lobby this authority was started for and binds the session's one public port before it can be told
	///     about any player. No world exists yet: the map is loaded when the lobby's countdown ends, against the seats the
	///     lobby arranged, so the peers that joined the lobby are the peers the match is played with and nobody reconnects.
	/// </summary>
	private void StartLobby()
	{
		if (!_transport.TryStart(EcsWorld.MAX_PLAYERS, loopbackOnly: false, this, out string failure))
		{
			throw new IOException($"the lobby could not listen for peers: {failure}");
		}

		_mapShares = new ServerMapShares(_arguments.MapPath, _log);
		_lobby = new ServerLobby(
			_arguments,
			_log,
			(peerId, kind, payload, delivery) => _transport.Send(peerId, kind, payload, delivery),
			peerId => _transport.Disconnect(peerId),
			StartMatchFromLobby,
			Stop,
			(address, port) => _transport.SendToAddress(address, port, Encoding.UTF8.GetBytes("PUNCH")));

		IsRunning = true;
		_mapHash = MapContentHash.Compute(_arguments.MapPath, _arguments.LogPath);
		MarkStartStage("lobby listening");
		_log.Write($"lobby opened for map '{_lobby.MapName}' version {_lobby.MapVersion} with {_lobby.MaximumPlayers} playable slot(s)");
		AnnounceReady();
	}

	/// <summary>
	///     Builds the match the lobby arranged. It is the one place a lobby's seats become a world: the map is loaded with
	///     those seats so its script spawns what each of them starts with, and every attached seat is bound to the player
	///     slot the lobby already gave it, so the same connection carries the match from its first tick.
	/// </summary>
	/// <returns>Why the match could not start, or nothing when it did.</returns>
	private string? StartMatchFromLobby(IReadOnlyList<MatchRosterSeat> roster, IReadOnlyList<(int PeerId, int Slot)> assignments, IReadOnlyDictionary<string, int> sessionSlots, int seed)
	{
		if (_simulation != null) return "This lobby has already started its match.";

		// The countdown has reached zero, so this is the wait a player is in the middle of. Everything it is spent on is
		// written to the match's log, because the process that decides the world is the only one that can say what it cost.
		_startClock.Restart();
		_scriptLogSink = _log.Write;
		WasmScriptHost.OnWasmLog += _scriptLogSink;

		LoadedMap? worldOwnedHere = null;
		try
		{
			worldOwnedHere = TakePreparedLobbyWorldOrLoad(roster);
			MarkStartStage("map loaded");
			var paths = ServerMatchPaths.From(_arguments);
			_simulation = new ServerSimulation(worldOwnedHere, paths, _log, seed, _arguments.Cheats, _arguments.WasmDebug, _arguments.WorkspacePath, roster);

			// The simulation owns the world from here, and releases it when it is disposed.
			worldOwnedHere = null;
			MarkStartStage("simulation built");
			_simulation.PeerDropped += DisconnectPeer;
			_log.Write($"map '{paths.MapName}' keeps its saved data under '{_simulation.MapSavedDataDirectory()}'");
			_log.Write(_simulation.Describe());
		}
		catch (Exception exception)
		{
			_log.Write($"the lobby could not start its match: {exception}");
			worldOwnedHere?.Dispose();
			_simulation?.Dispose();
			_simulation = null;

			// The world a lobby prepared was spent on a start that failed, so the next start prepares its own rather than
			// beginning from nothing.
			_preparedLobbyWorldAttempted = false;
			if (_scriptLogSink != null)
			{
				WasmScriptHost.OnWasmLog -= _scriptLogSink;
				_scriptLogSink = null;
			}

			return "The match could not start; the authority's log explains why.";
		}

		_lobbySessionSlots.Clear();
		foreach (var pair in sessionSlots) _lobbySessionSlots[pair.Key] = pair.Value;

		for (int index = 0; index < assignments.Count; index++)
		{
			(int peerId, int slot) = assignments[index];
			if (!_simulation.SeatPeerAtSlot(peerId, slot))
			{
				_log.Write($"peer {peerId} could not be given lobby seat {slot}; it will not be part of the match");
				continue;
			}

			_seatedPeers[peerId] = slot;
			_unacknowledgedPublicationsPerPeer[peerId] = -1;
			_acknowledgedSequencePerPeer.Remove(peerId);
			_seatedAtTickPerPeer[peerId] = _simulation.TickCount;
			if (_peerLanguages.TryGetValue(peerId, out string? language)) _simulation.SetPlayerLanguage(slot, language);

			var assignment = new PeerRosterAssignment
			{
				PeerId = peerId,
				PlayerSlot = slot,
				Team = _simulation.ReadTeam(slot)
			};

			_transport.Send(peerId, NetworkFrameKind.Roster, MemoryPackSerializer.Serialize(assignment));
			_simulation.RequestBaseline(peerId);
			SendNavMeshObstacles(peerId);
			SendPauseState(peerId);
		}

		// A connection that never said who it is, or that the lobby never admitted, has no seat in this match. It is ended
		// here rather than left attached to a session whose traffic its role does not allow it to reach.
		var unseated = new List<int>();
		foreach (int peerId in _connectedPeers)
		{
			if (!_seatedPeers.ContainsKey(peerId)) unseated.Add(peerId);
		}

		for (int index = 0; index < unseated.Count; index++)
		{
			_log.Write($"peer {unseated[index]} was attached to the lobby but is not seated in the match; ending its connection");
			_transport.Disconnect(unseated[index]);
		}

		_log.Write($"the lobby handed {_seatedPeers.Count} connection(s) to the match; the port it was reached on belongs to the match now");
		MarkStartStage("match ready to describe");
		return null;
	}

	/// <summary>
	///     The players the match was started for. The roster names every seat the lobby arranged, so the map's script
	///     seats each of them - and spawns what its rules give them - before the authority is listening.
	/// </summary>
	private MatchRosterSeat[] ReadRoster()
	{
		if (string.IsNullOrWhiteSpace(_arguments.RosterPath)) return Array.Empty<MatchRosterSeat>();

		if (!MatchRosterFile.TryRead(_arguments.RosterPath, out MatchRosterSeat[] roster, out string error))
		{
			throw new InvalidDataException(error);
		}

		int people = 0;
		int computers = 0;
		for (int index = 0; index < roster.Length; index++)
		{
			if (roster[index].IsComputer) computers++;
			else people++;
		}

		_log.Write($"the match was started for {people} player(s) and {computers} computer(s) on the roster");
		return roster;
	}

	/// <summary>
	///     Tells the launcher where to reach this match. It is printed once and only after the transport is listening,
	///     so a client that reads it can connect without waiting for anything else.
	/// </summary>
	private void AnnounceReady()
	{
		if (_readyAnnounced) return;
		_readyAnnounced = true;

		if (_mapHash.Length == 0) _mapHash = MapContentHash.Compute(_arguments.MapPath, _arguments.LogPath);
		string wireTableHash = ReplicatedComponents.ComputeWireTableHash();
		var readyInfo = new ServerReadyInfo(Port, RealmProtocol.Version, RealmVersion.GameBuildNumber, _mapHash, wireTableHash);

		_log.Write($"ready protocol={readyInfo.Protocol} build={readyInfo.Build} mapHash={_mapHash} wireTableHash={wireTableHash}");
		Console.Out.WriteLine(readyInfo.ToReadyLine());
		Console.Out.Flush();
	}

	/// <summary>
	///     Advances the match by however many whole ticks have come due since the last call. A slow frame costs the
	///     cadence of the tick it swallowed rather than accumulating a backlog of them, which is what keeps a match on
	///     a slow machine from running a burst of catch-up ticks the instant it recovers.
	/// </summary>
	public void Tick(double nowSeconds)
	{
		if (!IsRunning) return;

		double lobbyDelta = _lastTickSeconds <= 0 ? 0 : Math.Max(0, nowSeconds - _lastTickSeconds);
		_lastTickSeconds = nowSeconds;
		_lobby?.Tick(lobbyDelta);
		if (!IsRunning) return;

		PrepareLobbyWorldIfDue();

		ServerSimulation? simulation = _simulation;
		if (simulation == null) return;

		// A paused match holds its world where it is. Nothing is stepped and nothing is published, so every screen keeps
		// the world it was last told about instead of watching it drift while nobody can act in it.
		if (_paused)
		{
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
			// Orders handed over since the last tick are this tick's budget. The window is cleared by the tick that
			// consumes it rather than by the frame that happened to arrive before it, so a peer cannot spend the
			// budget once per physics frame: this process runs physics faster than the match ticks, and clearing on
			// the frame let a peer hand over three times the work a tick is allowed to do between two ticks.
			_commandsThisTick.Clear();

			simulation.Step();
			PublishTick(simulation.AdvanceSnapshotSequence());
			_nextTickTime += MatchTiming.FixedTickSeconds;
			stepped++;
		}

		if (nowSeconds >= _nextTickTime) _nextTickTime = nowSeconds + MatchTiming.FixedTickSeconds;
	}

	/// <summary>How many ticks one call may run, so a process that was suspended does not replay the whole pause.</summary>
	private const int MaximumTicksPerCall = 5;

	/// <summary>
	///     How many commands one peer may hand the match between two ticks. A player's order is one command however many
	///     units it names, so thirty-two is far more than a person can author in a tick and far less than a peer has to be
	///     allowed to spend the tick on: a peer that sends more has its excess refused and answered rather than simulated.
	/// </summary>
	private const int MaximumCommandsPerPeerPerTick = 32;

	/// <summary>How often a flood of refused orders restates the running refusal total in the authority's log.</summary>
	private const long RateLimitReportIntervalMilliseconds = 1000;

	/// <summary>
	///     How often the match looks for a change to the volumes its navigation mesh was baked with. A map's script can
	///     spawn a blocker after the match started, so the set is compared rather than sent once.
	/// </summary>
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

		// What the match managed to tell its screens, so a match that ran with nothing to show is distinguishable from
		// one whose presentation channel never delivered.
		if (_readyAnnounced) _log.Write($"the match delivered {_deliveredPresentationCount} presentation messages");
		if (_readyAnnounced) _log.Write($"the command rate limit refused {_rateLimitedCommandCount} command(s)");
		if (_readyAnnounced) _log.Write($"snapshots: {_unreliableSnapshotCount} carried as deltas on the current stream, {_reliableSnapshotCount} carried in order");

		// A match that was asked to end writes what it framed before it goes, because the run that asked for the record
		// reads the folder once this process is gone.
		WriteTrafficReport();

		_seatedPeers.Clear();
		_connectedPeers.Clear();
		_peerLanguages.Clear();
		_lobbySessionSlots.Clear();
		_lobby = null;
		_mapShares = null;

		// A world prepared for a match that never started is this process's to release, because nothing else ever saw it.
		_preparedLobbyWorld?.Dispose();
		_preparedLobbyWorld = null;
	}

	public void Dispose()
	{
		if (_disposed) return;
		_disposed = true;

		Stop();
		_transport.Dispose();

		// The report states how much match time it covers, and the clock that says so belongs to the simulation, so it is
		// written before the world is torn down. A match that was already stopped has written it.
		WriteTrafficReport();

		_simulation?.Dispose();
		_simulation = null;

		_syncRecorder?.Dispose();
		_syncRecorder = null;

		if (_scriptLogSink != null)
		{
			WasmScriptHost.OnWasmLog -= _scriptLogSink;
			_scriptLogSink = null;
		}
	}

	/// <summary>
	///     A peer connected but has not been seated: it owns nothing until it says who it is and this match has a player
	///     slot to give it. Nothing is sent to it before then, so a connection that never says hello receives nothing.
	/// </summary>
	public void OnPeerConnected(int peerId)
	{
		_connectedPeers.Add(peerId);
		_log.Write($"peer {peerId} connected");

		// The challenge is the first thing a peer is told, because the hello is the first thing it may send. The ticket
		// the match was started with has to prove itself without travelling: an open match still hands one out, so a peer
		// never has to know whether the authority it reached was started with a ticket.
		string challenge = RealmProtocol.CreateChallenge();
		_pendingChallenges[peerId] = challenge;
		_transport.Send(peerId, NetworkFrameKind.HandshakeChallenge, MemoryPackSerializer.Serialize(new HandshakeChallenge { Nonce = challenge }));
	}

	public void OnPeerDisconnected(int peerId, string reason)
	{
		_connectedPeers.Remove(peerId);
		_peerLanguages.Remove(peerId);

		// While the lobby owns the session, a seat belongs to the lobby: it is held for the identity that dropped, or
		// the lobby is destroyed when the connection that administered it is gone.
		if (_lobby != null && _simulation == null)
		{
			_log.Write($"peer {peerId} left the lobby: {reason}");
			_lobby.OnPeerDisconnected(peerId);
			return;
		}

		bool wasSeated = _seatedPeers.Remove(peerId);
		_log.Write(wasSeated ? $"peer {peerId} left: {reason}" : $"peer {peerId} dropped before it was seated: {reason}");

		_simulation?.ForgetPeer(peerId);
		ForgetPeerBookkeeping(peerId);

		// A pause belongs to whoever asked for it. If that player is gone, nobody left in the match can resume it, so the
		// match resumes itself rather than staying stopped for the rest of the session.
		if (!wasSeated || !_paused || _seatedPeers.Count == 0) return;
		if (_pausedByPlayerSlot < 0) return;

		foreach (int seatedPlayerSlot in _seatedPeers.Values)
		{
			if (seatedPlayerSlot == _pausedByPlayerSlot) return;
		}

		_paused = false;
		_pausedByPlayerSlot = -1;
		_log.Write("the player who paused the match left, so the match resumes");
		BroadcastPauseState(-1);
	}

	/// <summary>
	///     Drops everything this match remembered about a peer that has gone, so the peer that takes its place starts
	///     from nothing rather than inheriting a description some other connection was given.
	/// </summary>
	private void ForgetPeerBookkeeping(int peerId)
	{
		_pendingChallenges.Remove(peerId);
		_lastLoggedAckSequencePerPeer.Remove(peerId);
		_firstSnapshotSequencePerPeer.Remove(peerId);
		_lastBaselineRequestTickPerPeer.Remove(peerId);
		_lastRefreshBaselineTickPerPeer.Remove(peerId);
		_unacknowledgedPublicationsPerPeer.Remove(peerId);
		_acknowledgedSequencePerPeer.Remove(peerId);
		_roundTripMillisecondsPerPeer.Remove(peerId);
		_seatedAtTickPerPeer.Remove(peerId);
		_obstacleSignaturePerPeer.Remove(peerId);
		_commandsThisTick.Remove(peerId);
	}

	/// <summary>
	///     Reads one frame from one peer. The first frame a peer may send is its hello; after that it may send orders,
	///     acknowledgements, a request to be described from scratch, and a request to end the match.
	/// </summary>
	public void OnFrameReceived(int peerId, NetworkFrameKind kind, ReadOnlySpan<byte> payload)
	{
		// A session is either a lobby or a match, and a peer's role decides which traffic it may reach. While the
		// lobby owns the session there is no world to order or acknowledge, so a match frame from a lobby member is
		// refused rather than queued for a match that does not exist yet.
		if (_lobby != null && _simulation == null)
		{
			HandleLobbyFrame(peerId, kind, payload);
			return;
		}

		ServerSimulation? simulation = _simulation;
		if (simulation == null) return;

		if (!_seatedPeers.ContainsKey(peerId))
		{
			if (kind != NetworkFrameKind.ClientHello)
			{
				_log.Write($"peer {peerId} sent a {kind} frame before its hello");
				_transport.Disconnect(peerId);
				return;
			}

			HandleHello(peerId, payload);
			return;
		}

		switch (kind)
		{
			case NetworkFrameKind.Command:
				HandleCommand(peerId, payload);
				break;
			case NetworkFrameKind.SnapshotAck:
				HandleSnapshotAck(peerId, payload);
				break;
			case NetworkFrameKind.BaselineRequest:
				// A peer asking to be described from scratch is answered at the same cadence as every other reason one is
				// owed, so a build that asked on every frame would be answered once a second rather than once a frame.
				if (TryClaimBaselineRequest(peerId, simulation.TickCount)) simulation.RequestBaseline(peerId);
				break;
			case NetworkFrameKind.LatencyProbe:
				_transport.Send(peerId, NetworkFrameKind.LatencyProbeEcho, payload.ToArray());
				break;
			case NetworkFrameKind.PauseRequest:
				HandlePauseRequest(peerId, payload);
				break;
			case NetworkFrameKind.ConsoleCommand:
				HandleConsoleCommand(peerId, payload);
				break;
			case NetworkFrameKind.ChatLine:
				HandleChatLine(peerId, payload);
				break;
			case NetworkFrameKind.Shutdown:
				_log.Write($"peer {peerId} asked the match to end");
				Stop();
				break;
			default:
				_log.Write($"peer {peerId} sent a {kind} frame this build does not accept");
				_transport.Disconnect(peerId);
				break;
		}
	}

	/// <summary>
	///     Reads one frame from a connection the lobby owns. The lobby's own messages are accepted; an order, an
	///     acknowledgement or anything else only a seated match peer may send ends the connection, because a peer that is
	///     still in the lobby must not reach match traffic.
	/// </summary>
	private void HandleLobbyFrame(int peerId, NetworkFrameKind kind, ReadOnlySpan<byte> payload)
	{
		ServerLobby? lobby = _lobby;
		if (lobby == null) return;

		if (kind == NetworkFrameKind.ClientHello)
		{
			HandleLobbyHello(peerId, payload);
			return;
		}

		if (!lobby.TryGetSeat(peerId, out LobbySeat seat))
		{
			_log.Write($"peer {peerId} sent a {kind} frame before it was admitted to the lobby");
			_transport.Disconnect(peerId);
			return;
		}

		switch (kind)
		{
			case NetworkFrameKind.LobbyCommand:
			{
				LobbyCommand command;
				try
				{
					command = MemoryPackSerializer.Deserialize<LobbyCommand>(payload.ToArray());
				}
				catch (Exception exception) when (exception is IOException or InvalidDataException or ArgumentException)
				{
					_log.Write($"peer {peerId} sent a lobby command that could not be read: {exception.Message}");
					return;
				}

				lobby.HandleCommand(peerId, command);
				break;
			}

			case NetworkFrameKind.MapTransferRequest:
				HandleMapTransfer(peerId, payload);
				break;

			case NetworkFrameKind.LatencyProbe:
				_transport.Send(peerId, NetworkFrameKind.LatencyProbeEcho, payload.ToArray());
				break;

			case NetworkFrameKind.Shutdown:
				if (peerId != lobby.AdminPeerId)
				{
					_log.Write($"peer {peerId} asked to end a lobby it does not administer; the request is refused");
					return;
				}

				_log.Write($"the peer administering the lobby asked it to end");
				Stop();
				break;

			default:
				// A seat is not a match peer yet, whatever frame it sends. Nothing it could say to a running match is
				// accepted here, so the connection that said it is the thing that ends.
				_log.Write($"lobby seat {seat.Slot} ('{seat.Name}') sent a {kind} frame, which a lobby member may not send");
				_transport.Disconnect(peerId);
				break;
		}
	}

	/// <summary>
	///     Admits a peer that has said who it is, and answers it with the seat it was given and the lobby it joined. The
	///     ticket this authority was spawned with is spent on the hello whatever the answer, so an answer read off one
	///     connection is worth nothing on another.
	/// </summary>
	private void HandleLobbyHello(int peerId, ReadOnlySpan<byte> payload)
	{
		ServerLobby? lobby = _lobby;
		if (lobby == null) return;

		ClientHello hello;
		try
		{
			hello = MemoryPackSerializer.Deserialize<ClientHello>(payload.ToArray());
		}
		catch (Exception exception) when (exception is IOException or InvalidDataException or ArgumentException)
		{
			_log.Write($"peer {peerId} sent a hello that could not be read: {exception.Message}");
			_transport.Disconnect(peerId);
			return;
		}

		string rejection = DescribeLobbyHandshakeRejection(peerId, in hello);
		if (rejection.Length > 0)
		{
			_log.Write($"peer {peerId} refused by the lobby: {rejection}");
			SendHello(peerId, rejection, isAdmin: false, inLobby: true);
			_transport.Disconnect(peerId);
			return;
		}

		bool proveAdmin = ProvesAdmin(peerId, hello.AdminProof);
		string admitted = lobby.AdmitPeer(peerId, hello.PlayerName, hello.SessionToken, proveAdmin, out bool isAdmin);
		if (admitted.Length > 0)
		{
			_log.Write($"peer {peerId} refused by the lobby: {admitted}");
			SendHello(peerId, admitted, isAdmin: false, inLobby: true);
			_transport.Disconnect(peerId);
			return;
		}

		_peerLanguages[peerId] = hello.Language ?? string.Empty;
		SendHello(peerId, string.Empty, isAdmin, inLobby: true);
		lobby.SendStateTo(peerId);
	}

	private string DescribeLobbyHandshakeRejection(int peerId, in ClientHello hello)
	{
		if (hello.Protocol != RealmProtocol.Version) return "server_rejected_protocol";
		if (!string.Equals(hello.Build, RealmVersion.GameBuildNumber, StringComparison.Ordinal)) return "server_rejected_build";
		if (!string.Equals(hello.WireTableHash, ReplicatedComponents.ComputeWireTableHash(), StringComparison.Ordinal)) return "server_rejected_wire_table";

		// A lobby is joined before every peer holds the map: the map is downloaded while the lobby is open, so a peer
		// with no map of its own states no hash and is admitted on the protocol and the wire table alone.
		if (!string.IsNullOrEmpty(hello.MapHash) && _mapHash.Length > 0 && !string.Equals(hello.MapHash, _mapHash, StringComparison.Ordinal)) return "server_rejected_map";
		return string.Empty;
	}

	/// <summary>
	///     Whether a lobby hello proved possession of the ticket this authority was spawned with. The challenge is spent
	///     here whatever the answer, and a peer that proves nothing is still admitted - as an ordinary member.
	/// </summary>
	private bool ProvesAdmin(int peerId, string? presentedProof)
	{
		if (_arguments.AdminToken.Length == 0) return false;
		if (string.IsNullOrEmpty(presentedProof)) return false;
		if (!_pendingChallenges.Remove(peerId, out string? challenge)) return false;

		byte[] presented = Encoding.UTF8.GetBytes(presentedProof);
		byte[] expected = Encoding.UTF8.GetBytes(RealmProtocol.ProveToken(_arguments.AdminToken, challenge));
		return CryptographicOperations.FixedTimeEquals(presented, expected);
	}

	/// <summary>Serves one request for the lobby's map, or tells the peer why it cannot be served.</summary>
	private void HandleMapTransfer(int peerId, ReadOnlySpan<byte> payload)
	{
		MapTransferRequest request;
		try
		{
			request = MemoryPackSerializer.Deserialize<MapTransferRequest>(payload.ToArray());
		}
		catch (Exception exception) when (exception is IOException or InvalidDataException or ArgumentException)
		{
			_log.Write($"peer {peerId} sent a map transfer request that could not be read: {exception.Message}");
			return;
		}

		MapTransferReply reply = _mapShares?.Handle(peerId, request) ?? new MapTransferReply
		{
			Kind = MapTransferReplyKind.Refused,
			TransferId = request.TransferId,
			MapName = request.MapName,
			Refusal = "this lobby has no map to serve"
		};

		_transport.Send(peerId, NetworkFrameKind.MapTransferReply, MemoryPackSerializer.Serialize(reply));
	}

	/// <summary>
	///     Reads a line a player typed into the map script's console and hands it to the map. A line the transport could not
	///     carry intact is dropped rather than shown to the map as a half-read command.
	/// </summary>
	private void HandleConsoleCommand(int peerId, ReadOnlySpan<byte> payload)
	{
		ServerSimulation? simulation = _simulation;
		if (simulation == null) return;

		ScriptConsoleCommand command;
		try
		{
			command = MemoryPackSerializer.Deserialize<ScriptConsoleCommand>(payload.ToArray());
		}
		catch (Exception exception) when (exception is IOException or InvalidDataException or ArgumentException)
		{
			_log.Write($"peer {peerId} sent a console command that could not be read: {exception.Message}");
			return;
		}

		simulation.ApplyConsoleCommand(peerId, command);
	}

	/// <summary>
	///     Reads a line of chat, hands it to the map whose script reads it, and shows it to every player it is meant for.
	///     A map's own commands are typed in chat, so a line that only reached the other screens would never reach the
	///     script that is supposed to act on it.
	/// </summary>
	private void HandleChatLine(int peerId, ReadOnlySpan<byte> payload)
	{
		ServerSimulation? simulation = _simulation;
		if (simulation == null) return;
		if (!_seatedPeers.TryGetValue(peerId, out int playerSlot)) return;

		ChatLine line;
		try
		{
			line = MemoryPackSerializer.Deserialize<ChatLine>(payload.ToArray());
		}
		catch (Exception exception) when (exception is IOException or InvalidDataException or ArgumentException)
		{
			_log.Write($"peer {peerId} sent a chat line that could not be read: {exception.Message}");
			return;
		}

		if (!ChatLineLimits.IsAcceptable(line.Message)) return;

		simulation.ApplyChatLine(peerId, line.Message);

		var relayed = new ChatLine
		{
			Sender = simulation.ReadPlayerName(playerSlot),
			Message = line.Message.Trim(),
			AlliesOnly = line.AlliesOnly,
			TimestampUtcTicks = line.TimestampUtcTicks > 0 ? line.TimestampUtcTicks : DateTime.UtcNow.Ticks
		};

		byte[] bytes = MemoryPackSerializer.Serialize(relayed);
		int senderTeam = simulation.ReadTeam(playerSlot);
		foreach (var seated in _seatedPeers)
		{
			if (relayed.AlliesOnly && simulation.ReadTeam(seated.Value) != senderTeam) continue;

			_transport.Send(seated.Key, NetworkFrameKind.ChatLine, bytes);
		}
	}

	/// <summary>
	///     Accepts or refuses a peer, and seats it on a player slot when it is accepted. A peer that is refused is told
	///     why before its connection ends, so the screen it belongs to can say what went wrong.
	/// </summary>
	private void HandleHello(int peerId, ReadOnlySpan<byte> payload)
	{
		ServerSimulation? simulation = _simulation;
		if (simulation == null) return;

		if (_arguments.Cheats && _seatedPeers.Count > 0)
		{
			_log.Write($"peer {peerId} refused: this match was started with cheats, so it belongs to the single player it was started for");
			SendHello(peerId, CheatsMatchRejection);
			_transport.Disconnect(peerId);
			return;
		}

		ClientHello hello;
		try
		{
			hello = MemoryPackSerializer.Deserialize<ClientHello>(payload.ToArray());
		}
		catch (Exception exception) when (exception is IOException or InvalidDataException or ArgumentException)
		{
			_log.Write($"peer {peerId} sent a hello that could not be read: {exception.Message}");
			_transport.Disconnect(peerId);
			return;
		}

		string rejection = DescribeHandshakeRejection(peerId, in hello);
		if (rejection.Length > 0)
		{
			_log.Write($"peer {peerId} refused: {rejection}");
			SendHello(peerId, rejection);
			_transport.Disconnect(peerId);
			return;
		}

		int playerSlot;
		int team;
		if (_lobbySessionSlots.Count > 0)
		{
			// The lobby arranged this roster before the world existed, so this match seats nobody it did not seat. A seat
			// whose connection dropped is handed back only to the identity that owned it, which is what makes a
			// reconnecting player the same player rather than whoever happened to reach the port next.
			if (string.IsNullOrEmpty(hello.SessionToken) || !_lobbySessionSlots.TryGetValue(hello.SessionToken, out playerSlot))
			{
				_log.Write($"peer {peerId} refused: this match's roster was arranged by a lobby, and this connection is not one of its seats");
				SendHello(peerId, LobbyRosterClosedRejection);
				_transport.Disconnect(peerId);
				return;
			}

			if (!simulation.SeatPeerAtSlot(peerId, playerSlot))
			{
				_log.Write($"peer {peerId} refused: lobby seat {playerSlot} already belongs to another connection");
				SendHello(peerId, LobbySeatTakenRejection);
				_transport.Disconnect(peerId);
				return;
			}

			team = simulation.ReadTeam(playerSlot);
		}
		else if (!simulation.TryAssignPlayerSlot(peerId, out playerSlot, out team))
		{
			const string noSlot = "server_rejected_no_free_slot";
			_log.Write($"peer {peerId} refused: every player slot this map declares is taken");
			SendHello(peerId, noSlot);
			_transport.Disconnect(peerId);
			return;
		}

		_seatedPeers[peerId] = playerSlot;
		_unacknowledgedPublicationsPerPeer[peerId] = -1;
		_acknowledgedSequencePerPeer.Remove(peerId);
		_roundTripMillisecondsPerPeer.Remove(peerId);
		_seatedAtTickPerPeer[peerId] = simulation.TickCount;
		simulation.SetPlayerLanguage(playerSlot, hello.Language);
		SendHello(peerId, string.Empty);

		var assignment = new PeerRosterAssignment
		{
			PeerId = peerId,
			PlayerSlot = playerSlot,
			Team = team
		};

		_transport.Send(peerId, NetworkFrameKind.Roster, MemoryPackSerializer.Serialize(assignment));
		_log.Write($"peer {peerId} seated as player {playerSlot} (team {team})");
		simulation.RequestBaseline(peerId);

		// A peer that has just been seated bakes its navigation mesh from the same obstacles this match pathed with, and
		// is told the pause state the match is already in rather than having to ask for it.
		SendNavMeshObstacles(peerId);
		SendPauseState(peerId);
	}

	private void SendHello(int peerId, string rejection, bool isAdmin = false, bool inLobby = false)
	{
		var response = new ServerHello
		{
			Protocol = RealmProtocol.Version,
			Build = RealmVersion.GameBuildNumber,
			Accepted = rejection.Length == 0,
			Rejection = rejection,
			MapHash = _mapHash,
			PeerId = peerId,
			IsAdmin = isAdmin,
			InLobby = inLobby
		};

		_transport.Send(peerId, NetworkFrameKind.ServerHello, MemoryPackSerializer.Serialize(response));
	}

	/// <summary>
	///     What a peer is told when it tries to join a match that was started with cheats. Such a match is played by
	///     one person against the computer, so it takes one peer and nobody else is offered a seat.
	/// </summary>
	private const string CheatsMatchRejection = "server_rejected_cheats_match_is_local_only";

	/// <summary>
	///     What a peer is told when it reaches a match a lobby arranged without holding one of the lobby's seats. The
	///     roster was settled before the match loaded the map, so a peer the lobby never seated has nothing to join.
	/// </summary>
	private const string LobbyRosterClosedRejection = "server_rejected_lobby_roster_is_closed";

	/// <summary>What a peer is told when the lobby seat its identity owned is already played by a live connection.</summary>
	private const string LobbySeatTakenRejection = "server_rejected_lobby_seat_is_taken";

	private string DescribeHandshakeRejection(int peerId, in ClientHello hello)
	{
		if (hello.Protocol != RealmProtocol.Version) return "server_rejected_protocol";
		if (!string.Equals(hello.Build, RealmVersion.GameBuildNumber, StringComparison.Ordinal)) return "server_rejected_build";
		if (!string.Equals(hello.WireTableHash, ReplicatedComponents.ComputeWireTableHash(), StringComparison.Ordinal)) return "server_rejected_wire_table";

		// A peer that states the map it holds is playing this world or it is not. A peer that cannot state one - a joined
		// match whose map arrived through the lobby rather than from a folder this machine named - is seated on the
		// protocol and the wire table, which is every guarantee there was before this field existed.
		if (!string.IsNullOrEmpty(hello.MapHash) && !string.Equals(hello.MapHash, _mapHash, StringComparison.Ordinal)) return "server_rejected_map";

		if (!TokenMatches(peerId, hello.TokenProof)) return "server_rejected_token";
		return string.Empty;
	}

	/// <summary>
	///     Whether a peer proved it holds the ticket this match was started with. A match started without one is open,
	///     which is what a dedicated authority needs: the players it accepts never saw the command line it was started
	///     with. The challenge it issued is spent here whatever the answer, so an answer read off one connection is
	///     worth nothing on another and nothing on this one.
	/// </summary>
	private bool TokenMatches(int peerId, string? presentedProof)
	{
		if (_arguments.Token.Length == 0)
		{
			_pendingChallenges.Remove(peerId);
			return true;
		}

		if (!_pendingChallenges.Remove(peerId, out string? challenge)) return false;
		if (string.IsNullOrEmpty(presentedProof)) return false;

		byte[] presented = Encoding.UTF8.GetBytes(presentedProof);
		byte[] expected = Encoding.UTF8.GetBytes(RealmProtocol.ProveToken(_arguments.Token, challenge));
		return CryptographicOperations.FixedTimeEquals(presented, expected);
	}

	/// <summary>
	///     Describes the world to every seated peer for the tick that just finished, and tells them what the map asked
	///     their screens to show. One tick is what all of them are being told about, so a peer that joined a moment ago
	///     and one that has been here since the start agree on which world they are looking at.
	/// </summary>
	private void PublishTick(int sequence)
	{
		ServerSimulation? simulation = _simulation;
		if (simulation == null) return;

		// The account of this sequence is taken whether or not anyone is listening: a run measures what a screen presents
		// against the sequences it applied, and a gap in the record would hide the very ticks a peer was catching up on.

		// The world this tick described is recorded as it is sent, because a screen can only be judged against the world
		// the process that decided it held, and only a run that asked for the record pays for it.
		_syncRecorder?.BeginSequence(sequence, simulation.TickCount);

		// What the map asked a screen to show this tick is collected whether or not anyone is listening, so a request
		// that nobody could receive is dropped rather than piling up behind the next one.
		ServerPresentationEvent[] presentation = simulation.CollectPresentation();
		if (_seatedPeers.Count == 0)
		{
			_syncRecorder?.EndSequence();
			return;
		}

		int peerCount = CopySeatedPeerIds();
		_trafficRecorder?.BeginSequence(sequence);
		for (int index = 0; index < peerCount; index++)
		{
			int seatedPeerId = _seatedPeerIds[index];
			bool resyncRequested = simulation.ConsumeBaselineRequest(seatedPeerId);
			bool refreshRequested = !resyncRequested
				&& IsRefreshDue(seatedPeerId, simulation.TickCount)
				&& TryClaimBaselineRequest(seatedPeerId, simulation.TickCount);
			_pendingBaselineRequests[index] = resyncRequested || refreshRequested;
			_pendingBaselineReasons[index] = resyncRequested
				? SnapshotBaselineReason.Resync
				: refreshRequested ? SnapshotBaselineReason.Safety : SnapshotBaselineReason.None;
			_pendingSkippedPeers[index] = ShouldSkipSlowPeer(seatedPeerId, simulation.TickCount, index);
		}

		// A unit record names the peer that owns the unit, so every worker has to agree on which peer acts as which
		// player before any of them describes a unit. This is the lookup the description used to do once per unit.
		simulation.PreparePeerDescriptions();

		_publishSequence = sequence;

		if (peerCount == 1)
		{
			DescribeAndFrameOnePeer(0);
		}
		else
		{
			_publishParallelism ??= new ParallelOptions();
			_publishParallelism.MaxDegreeOfParallelism = Math.Min(peerCount, simulation.SnapshotWorkerCount);
			_describeOnePeer ??= DescribeAndFrameOnePeer;
			Parallel.For(0, peerCount, _publishParallelism, _describeOnePeer);
		}

		// Everything that touches the transport or the record runs here rather than on a worker: the channel is the
		// engine's, so it is spoken to from the thread the engine runs the match on.
		for (int index = 0; index < peerCount; index++) SendPublishedSnapshot(index);

		_syncRecorder?.EndSequence();

		if (_trafficRecorder != null && sequence % TrafficReportIntervalSequences == 0) WriteTrafficReport();

		if (presentation.Length > 0) SendPresentation(peerCount, presentation);
	}

	/// <summary>
	///     Describes one seated peer and frames what it is told, on whichever thread this runs. It touches nothing but its
	///     own slot of the tick's arrays, so several of them run at once without coordinating.
	/// </summary>
	private void DescribeAndFrameOnePeer(int index)
	{
		ServerSimulation simulation = _simulation!;
		int peerId = _seatedPeerIds[index];

		if (_pendingSkippedPeers[index])
		{
			// A peer this far behind is told nothing this tick: describing it would be work its screen cannot draw, and
			// the frame would only wait behind the ones it has not read. What it is owed is a baseline, which the tick
			// that decided to skip it already asked for. The description this slot holds is left where it is rather than
			// discarded, because its containers are what the next description of this peer is written into; a null frame
			// is what says this peer is told nothing.
			_publishedFrames[index] = null;
			return;
		}

		// The description is written into the slot this peer already has, so describing a room replaces nothing and empties
		// what it held instead of allocating a set of lists per peer per tick.
		simulation.BuildSnapshotForPeerInto(ref _publishedSnapshots[index], peerId, _publishSequence, _pendingBaselineRequests[index]);
		WorldSnapshot snapshot = _publishedSnapshots[index];

		// Serializing and framing is the host's cost of describing one peer, and it is measured where it is paid so the
		// report states the real figure rather than the cost of a frame somebody else built. The serializer writes into a
		// buffer this peer's slot already owns, so a tick that describes a full room allocates the frames it sends and
		// nothing else.
		long framingStarted = _trafficRecorder == null ? 0 : System.Diagnostics.Stopwatch.GetTimestamp();
		ArrayBufferWriter<byte> serialized = _serializationWriters[index];
		serialized.ResetWrittenCount();
		MemoryPackSerializer.Serialize(serialized, snapshot);
		_publishedFrames[index] = snapshot.IsBaseline
			? SnapshotWireFormat.EncodeBaseline(serialized.WrittenSpan)
			: SnapshotWireFormat.EncodeDelta(serialized.WrittenSpan);
		if (_trafficRecorder == null) return;

		_publishedSerializedBytes[index] = serialized.WrittenCount;
		_publishedFramingMilliseconds[index] = (System.Diagnostics.Stopwatch.GetTimestamp() - framingStarted) * 1000.0 / System.Diagnostics.Stopwatch.Frequency;
	}

	/// <summary>
	///     Hands one peer what was described for it, and follows a baseline with the cards, colours and labels the map
	///     declared while it was setting the match up.
	/// </summary>
	private void SendPublishedSnapshot(int index)
	{
		ServerSimulation? simulation = _simulation;
		if (simulation == null) return;

		int peerId = _seatedPeerIds[index];
		if (_publishedFrames[index] == null) return;

		WorldSnapshot snapshot = _publishedSnapshots[index];

		// Taken before this frame is recorded below, which is what stops a peer looking like a newcomer afterwards.
		bool isFirstFrameForPeer = !_firstSnapshotSequencePerPeer.ContainsKey(peerId);

		_syncRecorder?.AddPeerSnapshot(peerId, in snapshot);

		// The first frame a peer is sent restates the world, so it is the initial baseline whatever asked for it; after that
		// the reason it was sent one is what the tick decided when it asked the simulation for the snapshot.
		if (_trafficRecorder != null)
		{
			SnapshotBaselineReason reason = !snapshot.IsBaseline
				? SnapshotBaselineReason.None
				: isFirstFrameForPeer ? SnapshotBaselineReason.Initial : _pendingBaselineReasons[index];
			_trafficRecorder.RecordFrame(
				peerId,
				_publishSequence,
				_publishedSerializedBytes[index],
				_publishedFrames[index]?.Length ?? 0,
				snapshot.IsBaseline,
				reason,
				_publishedFramingMilliseconds[index],
				simulation.TickCount,
				System.Diagnostics.Stopwatch.GetTimestamp());
		}

		// A delta describes a world the next delta supersedes, so it travels on the stream that would rather be current
		// than complete: losing one costs the change it carried until the acknowledgement reports the sequence missing
		// and this peer is described from a baseline, while carrying it reliably stalls every delta behind it for as long
		// as the retransmission takes. A baseline is the only thing a peer can rebuild the world from, so it is carried
		// reliably whatever its size, and so is a delta too large to depend on arriving whole.
		bool carriedUnreliably = !snapshot.IsBaseline && (_publishedFrames[index]?.Length ?? 0) <= UnreliableSnapshotLimitBytes;
		_transport.Send(peerId, NetworkFrameKind.Snapshot, _publishedFrames[index], carriedUnreliably ? FrameDelivery.UnreliableOrdered : FrameDelivery.Reliable);
		if (carriedUnreliably) _unreliableSnapshotCount++;
		else _reliableSnapshotCount++;

		// The first sequence this peer was described with bounds what its acknowledgement mask can claim: a bit for a
		// sequence the match never sent was never meant to be set, so counting it as a loss would describe the peer
		// from scratch on every acknowledgement. A baseline starts that bound over, because it restates the world and
		// the peer is no longer expected to have applied anything the match described before it.
		if (snapshot.IsBaseline || !_firstSnapshotSequencePerPeer.ContainsKey(peerId)) _firstSnapshotSequencePerPeer[peerId] = _publishSequence;
		if (snapshot.IsBaseline) _lastRefreshBaselineTickPerPeer[peerId] = simulation.TickCount;

		// What the map declared about the match - the cards, colours and labels, and the place it put the camera - is what a
		// peer that has just been seated has to hear, and only that peer. It used to follow every baseline, so the ten
		// second safety refresh told an established peer all of it again: a camera pan the map made once, when the match
		// started, was replayed every ten seconds and snapped the screen back to where it began, with a duration of zero,
		// while the player was looking somewhere else. State the map set once is stated once.
		if (!snapshot.IsBaseline || !isFirstFrameForPeer || !_seatedPeers.TryGetValue(peerId, out int playerSlot)) return;

		ServerPresentationEvent[] description = simulation.DescribePresentationStateFor(playerSlot);
		if (description.Length > 0) SendPresentationToPeer(peerId, description);
	}

	/// <summary>
	///     Copies the seated peers into the scratch list a tick walks, so publishing a tick allocates nothing. The list is
	///     only read inside the call that filled it.
	/// </summary>
	private int CopySeatedPeerIds()
	{
		int count = 0;
		foreach (int peerId in _seatedPeers.Keys)
		{
			if (count == _seatedPeerIds.Length) break;
			_seatedPeerIds[count++] = peerId;
		}

		return count;
	}

	/// <summary>
	///     Tells each peer what the map asked its screen to show. A request addressed to one player reaches only the
	///     peer acting as that player; one addressed to everyone reaches all of them. Every request carries the tick it
	///     belongs to, so a screen shows it when it draws that tick rather than when the frame happens to arrive.
	/// </summary>
	private void SendPresentation(int peerCount, ServerPresentationEvent[] events) => SendPresentation(_seatedPeerIds.AsSpan(0, peerCount), events, _publishSequence);

	/// <summary>
	///     Tells one peer the state the match holds for it, rather than what one tick did: the cards, colours and labels a
	///     peer is given when it is seated describe the match now and are shown on arrival.
	/// </summary>
	private void SendPresentationToPeer(int peerId, ServerPresentationEvent[] events)
	{
		Span<int> singlePeer = stackalloc int[1];
		singlePeer[0] = peerId;
		SendPresentation(singlePeer, events, PresentationMessage.StateDescriptionSequence);
	}

	private void SendPresentation(ReadOnlySpan<int> peerIds, ServerPresentationEvent[] events, int sequence)
	{
		// Each request is serialized once and handed to every peer it addresses. Serializing it inside the per-peer
		// loop made a room of twenty-four pay for the same event twenty-four times.
		_presentationPayloads.Clear();
		for (int eventIndex = 0; eventIndex < events.Length; eventIndex++)
		{
			ServerPresentationEvent request = events[eventIndex];
			_presentationPayloads.Add((request.PlayerIndex, MemoryPackSerializer.Serialize(request.ToMessage(sequence))));
		}

		for (int peerIndex = 0; peerIndex < peerIds.Length; peerIndex++)
		{
			int peerId = peerIds[peerIndex];
			bool seated = _seatedPeers.TryGetValue(peerId, out int playerSlot);

			for (int payloadIndex = 0; payloadIndex < _presentationPayloads.Count; payloadIndex++)
			{
				(int playerIndex, byte[] payload) = _presentationPayloads[payloadIndex];
				if (playerIndex != ServerPresentationEvent.Everyone && (!seated || playerIndex != playerSlot)) continue;

				_transport.Send(peerId, NetworkFrameKind.Presentation, payload);
				_deliveredPresentationCount++;
			}
		}
	}

	/// <summary>
	///     Records how far a peer has applied the world, and repairs the peer when the mask says it discarded part of a
	///     snapshot it was sent. A sequence inside the window that the match described but the peer did not apply is
	///     state the peer will never be told again from the delta stream, so it is described from scratch instead of
	///     being left to drift against a world the authority has already moved past.
	/// </summary>
	private void HandleSnapshotAck(int peerId, ReadOnlySpan<byte> payload)
	{
		ServerSimulation? simulation = _simulation;
		if (simulation == null) return;

		// A peer that answers is a peer that is reading, whatever else the answer says: the count of descriptions it has not
		// answered starts over here, which is what tells the gate above that a screen has caught up.
		if (_unacknowledgedPublicationsPerPeer.ContainsKey(peerId)) _unacknowledgedPublicationsPerPeer[peerId] = 0;

		SnapshotAck ack;
		try
		{
			ack = MemoryPackSerializer.Deserialize<SnapshotAck>(payload.ToArray());
		}
		catch (Exception exception) when (exception is IOException or InvalidDataException or ArgumentException)
		{
			_log.Write($"peer {peerId} sent a snapshot acknowledgement that could not be read: {exception.Message}");
			return;
		}

		// How far behind this peer is is measured from what it says it applied, not from how many descriptions it has left
		// unanswered: an answer is one round trip old when it arrives, so a perfectly healthy peer on a long link always
		// has descriptions in flight. The round trip it states travels with the answer for the same reason, and a peer that
		// reports an older sequence than it did before - which is a restarted screen rather than a peer going backwards -
		// is taken at the value it now holds.
		if (!_acknowledgedSequencePerPeer.TryGetValue(peerId, out int acknowledgedSequence) || ack.Sequence >= acknowledgedSequence)
		{
			_acknowledgedSequencePerPeer[peerId] = ack.Sequence;
		}

		_roundTripMillisecondsPerPeer[peerId] = Math.Max(0, ack.RoundTripMilliseconds);

		LogAcknowledgedSequence(peerId, ack);

		if (!_firstSnapshotSequencePerPeer.TryGetValue(peerId, out int firstDescribedSequence)) return;

		// Only sequences this peer was actually described with can be missing. The peer reports the newest one it
		// applied in the low bit, so the mask covers as many bits back as the match has described to it.
		int describedSequences = ack.Sequence - firstDescribedSequence;
		if (describedSequences <= 0) return;

		int coveredBits = Math.Min(64, describedSequences + 1);
		ulong expected = coveredBits >= 64 ? ulong.MaxValue : (1UL << coveredBits) - 1UL;
		if ((ack.AppliedMask & expected) == expected) return;

		if (!TryClaimBaselineRequest(peerId, simulation.TickCount)) return;

		simulation.RequestBaseline(peerId);
		_log.Write($"peer {peerId} is missing a snapshot it was sent; it is described from scratch");
	}

	private void LogAcknowledgedSequence(int peerId, in SnapshotAck ack)
	{
		if (_lastLoggedAckSequencePerPeer.TryGetValue(peerId, out long lastLogged) && ack.Sequence - lastLogged < AckReportInterval) return;

		_lastLoggedAckSequencePerPeer[peerId] = ack.Sequence;
		_log.Write($"peer {peerId} acknowledged sequence {ack.Sequence} with mask {ack.AppliedMask:x16}");
	}

	/// <summary>
	///     Whether this peer's description should be refreshed from scratch at this tick. A peer that has never been given
	///     a baseline is already due one because seating asks for it, so only a peer that has been described measures the
	///     interval from its last baseline.
	/// </summary>
	private bool IsRefreshDue(int peerId, long tick)
	{
		return _lastRefreshBaselineTickPerPeer.TryGetValue(peerId, out long lastBaselineTick) && tick - lastBaselineTick >= SnapshotRefreshTicks;
	}

	/// <summary>
	///     Entitles one peer to a description from scratch, if this peer is due one. Every path that can ask - an
	///     acknowledgement that reports a hole, a peer that is too far behind to be described, the safety refresh, and the
	///     peer itself - asks here first, so a peer that asks on every frame costs the same as one that asks once a second
	///     rather than a full description and a reliable frame of it per request.
	///     It claims the entitlement rather than delivering it: a caller that is publishing a tick describes the peer on
	///     that tick, and one that is not asks the simulation to describe it on the next, so the same entitlement never
	///     becomes two baselines.
	/// </summary>
	/// <returns>Whether the entitlement was granted, or false because one was granted too recently to grant another.</returns>
	private bool TryClaimBaselineRequest(int peerId, long tick)
	{
		if (_lastBaselineRequestTickPerPeer.TryGetValue(peerId, out long lastRequestTick) && tick - lastRequestTick < BaselineRequestCooldownTicks) return false;

		_lastBaselineRequestTickPerPeer[peerId] = tick;
		return true;
	}

	/// <summary>
	///     Whether this peer is too far behind to be described this tick, and what it is owed instead. A peer that has
	///     never acknowledged anything is not behind: it is a screen still loading the map it was told to play, and it is
	///     left alone for the grace a load is allowed. One that has answered and then stopped is measurable, and a peer more
	///     than half a second of publishing behind what it last acknowledged - plus whatever its own round trip accounts
	///     for - is skipped and described from a baseline once a second, so it can catch up rather than be starved by the
	///     stream it is not reading. A peer that has left a quarter of a minute of publishing unanswered is dropped, so the
	///     seat and the transport state it holds do not outlive the screen that forgot it. A peer that never answers at all
	///     leaves the grace and enters this same measure, so a screen that reached the handshake and then stopped reading
	///     costs a baseline a second for a while and is then dropped, rather than being described in full thirty times a
	///     second for the length of the match.
	/// </summary>
	private bool ShouldSkipSlowPeer(int peerId, long tick, int index)
	{
		if (!_unacknowledgedPublicationsPerPeer.TryGetValue(peerId, out int unacknowledged)) return false;

		if (unacknowledged < 0)
		{
			if (!_seatedAtTickPerPeer.TryGetValue(peerId, out long seatedAtTick)) return false;
			if (tick - seatedAtTick < NeverAnsweredGraceTicks) return false;

			_log.Write($"peer {peerId} has been described for {NeverAnsweredGraceTicks / MatchTiming.SimulationTickRateHz}s without answering once; it is measured as a peer that is behind");
			_unacknowledgedPublicationsPerPeer[peerId] = 0;
			unacknowledged = 0;
		}

		_unacknowledgedPublicationsPerPeer[peerId] = unacknowledged + 1;

		// Behind is measured from the sequence the peer says it applied. Counting unanswered descriptions instead measured
		// the link rather than the screen: on a healthy connection that count settles at one round trip of publishing plus
		// the descriptions sent since the newest answer, so past about four hundred milliseconds of round trip a peer that
		// was drawing everything it was sent tripped the bound, had its deltas suppressed and was re-baselined once a
		// second - penalising exactly the peers whose link needed the smooth stream most.
		if (_acknowledgedSequencePerPeer.TryGetValue(peerId, out int acknowledgedSequence))
		{
			int lagSequences = _publishSequence - acknowledgedSequence;
			if (lagSequences <= MaximumLagBehindSequences + RoundTripSequences(peerId)) return false;
		}

		// A baseline asked for here is the only thing this peer is sent while it stays silent. One the cooldown refuses
		// means a description from scratch is already on its way, so the tick it would have been described on is skipped
		// rather than spent describing towards a screen that is not reading.
		if (TryClaimBaselineRequest(peerId, tick))
		{
			_pendingBaselineRequests[index] = true;
			_pendingBaselineReasons[index] = SnapshotBaselineReason.Resync;
			return false;
		}

		if (unacknowledged < MaximumUnacknowledgedBeforeDisconnect) return true;

		_log.Write($"peer {peerId} has left {unacknowledged} descriptions unanswered; the match drops it");
		_seatedPeers.Remove(peerId);
		_transport.Disconnect(peerId);
		return true;
	}

	/// <summary>
	///     How many sequences the round trip this peer last measured covers. An acknowledgement states a sequence the peer
	///     applied one round trip before it arrives here, so that much of the apparent lag is the link carrying the answer
	///     rather than the screen falling behind, and a peer is only behind by what is left over.
	/// </summary>
	private int RoundTripSequences(int peerId)
	{
		if (!_roundTripMillisecondsPerPeer.TryGetValue(peerId, out int roundTripMilliseconds)) return 0;

		return (int)MathF.Ceiling(roundTripMilliseconds * 0.001f * MatchTiming.SnapshotRateHz);
	}

	private const long AckReportInterval = 100;

	/// <summary>
	///     How long one peer waits between two descriptions from scratch, in ticks, whatever asked for them. A peer that
	///     keeps losing state is described from scratch once a second rather than on every snapshot, and one that asks on
	///     every frame is answered on the same cadence, which bounds what a broken or hostile peer can cost the match. The
	///     duration is stated once in <see cref="MatchTiming" />, because the client asks on the same policy in its own
	///     clock and two figures for it were two policies that could drift apart.
	/// </summary>
	private const long BaselineRequestCooldownTicks = MatchTiming.BaselineRequestCooldownTicks;

	/// <summary>
	///     How long a peer that has never acknowledged a description is given before it is measured like any other. A
	///     match built by a lobby seats its peers on a world that already exists, so a peer here is loading the map it was
	///     told to play; forty-five seconds is several times the slowest load this build allows, and it is a bound rather
	///     than a description of a healthy peer, because a screen that stopped reading has to stop costing a slot.
	/// </summary>
	private const int NeverAnsweredGraceTicks = MatchTiming.SimulationTickRateHz * 45;

	/// <summary>
	///     How far behind the newest sequence a peer's own acknowledgement may name before the match stops spending a tick
	///     describing it. Half a second is twice the deepest buffer a screen renders from, so a peer further behind than
	///     this is not drawing the world it is being sent: the description states a moment its screen has already passed,
	///     while the frames it does not read wait in the transport behind it. The link's own round trip is added to this
	///     figure, because an acknowledgement is that old when it arrives and a peer cannot answer for the sequences still
	///     in flight. The figure is a duration rather than the screen's own bound, because how deeply a screen buffers
	///     belongs to the link it is on and nothing this process can read.
	/// </summary>
	private const int MaximumLagBehindSequences = MatchTiming.SnapshotRateHz / 2;

	/// <summary>
	///     How many descriptions a peer may leave unanswered before the match ends its connection, counted in published
	///     ticks rather than seconds so a paused match - which publishes nothing and owes nothing - never counts against a
	///     peer. Together with the gate above this bounds what a screen that stopped reading can leave queued: past the
	///     gate it is sent one baseline a second, so fifteen seconds of it is under forty kilobytes rather than a stream.
	/// </summary>
	private const int MaximumUnacknowledgedBeforeDisconnect = MatchTiming.SnapshotRateHz * 15;

	/// <summary>
	///     The largest delta carried on the unreliable stream, in framed bytes. It is one datagram: a delta is about 1.2 kB
	///     for a peer in a three hundred unit room and fits one, and anything above this is fragmented by the transport into
	///     several datagrams, any one of which being lost loses the delta whole. The old limit of eight kilobytes was
	///     written as if fragmentation started there, so a large delta was offered a loss chance multiplied by the number of
	///     fragments it turned into; past one datagram the reliable stream is the better bet.
	/// </summary>
	private const int UnreliableSnapshotLimitBytes = 1400;

	private long _unreliableSnapshotCount;
	private long _reliableSnapshotCount;

	/// <summary>
///     Answers one order. The authority validates and applies it, and the peer is told whether it was taken, which is
///     what lets a screen keep the route it drew for the order or take it back.
	/// </summary>
	private void HandleCommand(int peerId, ReadOnlySpan<byte> payload)
	{
		ServerSimulation? simulation = _simulation;
		if (simulation == null) return;

		NetworkCommand command;
		try
		{
			command = MemoryPackSerializer.Deserialize<NetworkCommand>(payload.ToArray());
		}
		catch (Exception exception) when (exception is IOException or InvalidDataException or ArgumentException)
		{
			_log.Write($"peer {peerId} sent a command that could not be read: {exception.Message}");
			return;
		}

		int commandId = command.CommandId;
		_commandsThisTick.TryGetValue(peerId, out int commandsThisTick);
		if (commandsThisTick >= MaximumCommandsPerPeerPerTick)
		{
			if (commandsThisTick == MaximumCommandsPerPeerPerTick)
			{
				_log.Write($"peer {peerId} is above the {MaximumCommandsPerPeerPerTick} command(s) a tick allows; its further orders are refused");
			}

			_commandsThisTick[peerId] = commandsThisTick + 1;
			_rateLimitedCommandCount++;
			// A peer that floods keeps being refused long after the first refusal, and a reader watching the match live has
			// to be able to see the budget doing its job. The running total is restated at most once a second rather than
			// once per refused order, so the report itself cannot become the flood.
			long nowTicks = Environment.TickCount64;
			if (nowTicks >= _nextRateLimitReportTicks)
			{
				_nextRateLimitReportTicks = nowTicks + RateLimitReportIntervalMilliseconds;
				_log.Write($"the command rate limit has refused {_rateLimitedCommandCount} command(s) so far");
			}

			SendOutcome(peerId, commandId, false, "the command rate limit for one tick was exceeded");
			return;
		}

		_commandsThisTick[peerId] = commandsThisTick + 1;

		if (_paused)
		{
			_log.Write($"peer {peerId} command {commandId} '{command.CommandType}' refused: the match is paused");
			SendOutcome(peerId, commandId, false, "the match is paused");
			return;
		}

		if (!NetworkCommandLimits.TrySanitize(ref command, out string rejection))
		{
			_log.Write($"peer {peerId} command {commandId} refused: {rejection}");
			SendOutcome(peerId, commandId, false, rejection);
			return;
		}

		ServerCommandResult result = simulation.ApplyCommand(peerId, command);
		bool accepted = !result.HasRejection;

		_log.Write(accepted
			? $"peer {peerId} command {commandId} '{command.CommandType}' accepted"
			: $"peer {peerId} command {commandId} '{command.CommandType}' refused: {result.RejectionReason}");

		// A repeat of an order that was already answered is not answered again. The peer already holds the answer - the
		// transport is reliable and ordered, so it arrived unless the connection that carried it is gone, and a peer that
		// reconnects is described from scratch under a new connection that has no record to repeat. Sending a second,
		// negative answer here took back the route preview for an order this match had accepted.
		if (result.WasAlreadyAnswered) return;

		SendOutcome(peerId, commandId, accepted, result.RejectionReason);
	}

	private void SendOutcome(int peerId, int commandId, bool accepted, string rejectionReason)
	{
		var outcome = new CommandOutcome
		{
			CommandId = commandId,
			Accepted = accepted,
			RejectionReason = rejectionReason ?? string.Empty
		};

		_transport.Send(peerId, NetworkFrameKind.CommandOutcome, MemoryPackSerializer.Serialize(outcome));
	}

	/// <summary>
	///     Applies a peer's request to pause or resume. Pausing is a match-wide decision, so any seated player may ask
	///     for either state and the answer is broadcast to everyone: two clients can never end up showing one paused
		///     match and one running one.
	/// </summary>
	private void HandlePauseRequest(int peerId, ReadOnlySpan<byte> payload)
	{
		if (!_seatedPeers.TryGetValue(peerId, out int playerSlot))
		{
			_log.Write($"peer {peerId} asked to change the pause state before it was seated");
			return;
		}

		MatchPauseState request;
		try
		{
			request = MemoryPackSerializer.Deserialize<MatchPauseState>(payload.ToArray());
		}
		catch (Exception exception) when (exception is IOException or InvalidDataException or ArgumentException)
		{
			_log.Write($"peer {peerId} sent a pause request that could not be read: {exception.Message}");
			return;
		}

		if (_paused == request.Paused)
		{
			SendPauseState(peerId);
			return;
		}

		_paused = request.Paused;
		_pausedByPlayerSlot = request.Paused ? playerSlot : -1;
		_log.Write(request.Paused
			? $"player {playerSlot} paused the match"
			: $"player {playerSlot} resumed the match");

		BroadcastPauseState(playerSlot);
	}

	/// <summary>Tells one peer the pause state this match holds.</summary>
	private void SendPauseState(int peerId) =>
		_transport.Send(peerId, NetworkFrameKind.MatchPaused, MemoryPackSerializer.Serialize(new MatchPauseState { Paused = _paused, RequestedByPlayer = -1 }));

	/// <summary>Tells every seated peer the pause state this match now holds, naming who asked for it.</summary>
	private void BroadcastPauseState(int requestedByPlayer)
	{
		if (_seatedPeers.Count == 0) return;

		var state = new MatchPauseState { Paused = _paused, RequestedByPlayer = requestedByPlayer };
		byte[] payload = MemoryPackSerializer.Serialize(state);

		int[] peerIds = new int[_seatedPeers.Count];
		_seatedPeers.Keys.CopyTo(peerIds, 0);
		for (int index = 0; index < peerIds.Length; index++) _transport.Send(peerIds[index], NetworkFrameKind.MatchPaused, payload);
	}

	/// <summary>
	///     Sends the obstacle volumes this match baked with to one peer. The volumes are pure geometry and carry no
	///     gameplay authority: a peer only learns which cells the authority pathing treats as blocked, so its own mesh
	///     agrees instead of routing through a blocker the map's script spawned.
	/// </summary>
	private void SendNavMeshObstacles(int peerId)
	{
		if (!TryBuildObstacleVolumes(out NavMeshObstacleVolumes volumes, out int signature, out int count)) return;

		// What one peer was given is remembered per peer. Recording it in a value the whole match shares made a peer
		// joining after a change tell the match that every peer already had the new set, so the peers that were here
		// first kept the set they were given before the blocker appeared.
		_obstacleSignaturePerPeer[peerId] = signature;
		_transport.Send(peerId, NetworkFrameKind.NavMeshObstacles, MemoryPackSerializer.Serialize(volumes));
		_log.Write($"peer {peerId} given the {count} obstacle volumes this match pathing was baked with");
	}

	/// <summary>
///     Tells every seated peer the volumes the match now paths with, once and only when they have changed since the
///     last set was sent. A script that spawns a blocker after the match started changes them, and a peer that kept
///     the earlier set would plan a route through it.
	/// </summary>
	private void BroadcastNavMeshObstaclesWhenChanged()
	{
		if (_seatedPeers.Count == 0) return;
		if (!TryBuildObstacleVolumes(out NavMeshObstacleVolumes volumes, out int signature, out int count)) return;

		int staleCount = 0;
		foreach (int seatedPeerId in _seatedPeers.Keys)
		{
			if (staleCount == _seatedPeerIds.Length) break;
			if (_obstacleSignaturePerPeer.TryGetValue(seatedPeerId, out int sentSignature) && sentSignature == signature) continue;

			_seatedPeerIds[staleCount++] = seatedPeerId;
		}

		if (staleCount == 0) return;

		byte[] payload = MemoryPackSerializer.Serialize(volumes);
		for (int index = 0; index < staleCount; index++)
		{
			_obstacleSignaturePerPeer[_seatedPeerIds[index]] = signature;
			_transport.Send(_seatedPeerIds[index], NetworkFrameKind.NavMeshObstacles, payload);
		}

		_log.Write($"the match now paths with {count} obstacle volumes; {staleCount} peer(s) were given the new set");
	}

	/// <summary>
	///     Copies the volumes the last bake used into the shape the wire carries, and fingerprints them so a resend is
	///     only made when the set actually changed.
	/// </summary>
	private bool TryBuildObstacleVolumes(out NavMeshObstacleVolumes volumes, out int signature, out int count)
	{
		volumes = default;
		signature = 0;
		count = 0;

		TerrainNavMeshService? navMesh = _simulation?.Map.Simulation.TerrainNavMeshService;
		if (navMesh == null) return false;

		// A mesh that has not been baked yet has no volumes to report, and reporting an empty set would tell the peer the
		// map blocks nothing. Peers are seated before the map finishes loading, so this is the state every one of them
		// starts in: it is told nothing and keeps waiting for the set the first bake produces.
		if (!navMesh.HasBakedMesh) return false;

		ReadOnlySpan<System.Numerics.Vector3> positions = navMesh.BakedObstaclePositions;
		ReadOnlySpan<float> radii = navMesh.BakedObstacleRadii;
		count = Math.Min(positions.Length, radii.Length);

		volumes = new NavMeshObstacleVolumes
		{
			Positions = new NetworkVector3[count],
			Radii = new float[count]
		};

		int hash = 17;
		for (int index = 0; index < count; index++)
		{
			System.Numerics.Vector3 position = positions[index];
			float radius = radii[index];
			volumes.Positions[index] = new NetworkVector3(position.X, position.Y, position.Z);
			volumes.Radii[index] = radius;

			hash = hash * 31 + BitConverter.SingleToInt32Bits(position.X);
			hash = hash * 31 + BitConverter.SingleToInt32Bits(position.Y);
			hash = hash * 31 + BitConverter.SingleToInt32Bits(position.Z);
			hash = hash * 31 + BitConverter.SingleToInt32Bits(radius);
		}

		signature = hash;
		return true;
	}

	/// <summary>
	///     Ends one peer's connection because the match dropped the player it was playing as. The peer is told the match
	///     is over for it before the connection closes, so the screen it belongs to reports a dropped player rather than
	///     a connection that stopped answering.
	/// </summary>
	private void DisconnectPeer(int peerId)
	{
		if (!_seatedPeers.Remove(peerId)) return;

		_log.Write($"peer {peerId} was dropped by the map");
		_transport.Send(peerId, NetworkFrameKind.Shutdown, Array.Empty<byte>());
		_transport.Disconnect(peerId);
	}
}
