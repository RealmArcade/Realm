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

public partial class ListenServerHost : IServerTransportSink
	{
	private readonly ServerArguments _arguments;
	private readonly ServerLog _log;
	private readonly IServerTransport _transport;
	private readonly Dictionary<int, int> _seatedPeers = new();
	private readonly Dictionary<int, int> _commandsThisTick = new();

	private readonly Dictionary<int, string> _pendingChallenges = new();

	private ServerSimulation? _simulation;

	private ServerLobby? _lobby;

	private ServerMapShares? _mapShares;

	private LoadedMap? _preparedLobbyWorld;

	private ServerLobbyScript? _lobbyScript;

	private bool _preparedLobbyWorldAttempted;

	private readonly HashSet<int> _connectedPeers = new();

	private readonly Dictionary<int, string> _peerLanguages = new();

	private readonly Dictionary<string, int> _sessionTokenSlots = new();

	private double _lastTickSeconds;
	private double _nextTickTime;
	private readonly Dictionary<int, long> _lastLoggedAckSequencePerPeer = new();
	private readonly Dictionary<int, int> _firstSnapshotSequencePerPeer = new();

	private readonly Dictionary<int, long> _lastBaselineRequestTickPerPeer = new();
	private readonly Dictionary<int, long> _lastAcknowledgedProgressTickPerPeer = new();

	private readonly Dictionary<int, long> _seatedAtTickPerPeer = new();

	private readonly Dictionary<int, int> _unacknowledgedPublicationsPerPeer = new();

	private readonly Dictionary<int, int> _acknowledgedSequencePerPeer = new();

	private readonly Dictionary<int, int> _roundTripMillisecondsPerPeer = new();

	/// <summary>
	///     How long a peer may go without acknowledging a description it had not acknowledged before. An acknowledgement is
	///     what proves a screen is still holding a world, so a peer that has stopped answering is the only peer the safety
	///     refresh is still for.
	///     <para>
	///         It is a span of match time rather than an absence of fresh answers, because a screen working through the
	///         description it was just sent answers nothing in the meantime, and the refresh shares one cooldown with the
	///         resync a peer that far behind is owed. A stall short enough to spend that cooldown on a screen that was only
	///         catching up left the resync to starve: the peer was skipped every tick, re-described from scratch once per
	///         cooldown, and never described in between.
	///     </para>
	/// </summary>
	private const int SnapshotRefreshStallTicks = MatchTiming.SimulationTickRateHz * 10;
	private readonly Dictionary<int, int> _obstacleSignaturePerPeer = new();
	private readonly int[] _seatedPeerIds = new int[EcsWorld.MAX_PLAYERS];
	private readonly int[] _peersOwedObstacleVolumes = new int[EcsWorld.MAX_PLAYERS];
	private readonly List<PresentationMessage> _statePresentationBatch = new();
	private readonly List<PresentationMessage> _momentPresentationBatch = new();
	private readonly WorldSnapshot[] _publishedSnapshots = new WorldSnapshot[EcsWorld.MAX_PLAYERS];
	private readonly byte[][] _publishedFrames = new byte[EcsWorld.MAX_PLAYERS][];
	private readonly bool[] _pendingBaselineRequests = new bool[EcsWorld.MAX_PLAYERS];
	private readonly bool[] _pendingDescribeInFull = new bool[EcsWorld.MAX_PLAYERS];
	private readonly bool[] _pendingSkippedPeers = new bool[EcsWorld.MAX_PLAYERS];
	private PublishLaneWorkers? _publishLaneWorkers;
	private readonly Dictionary<int, SnapshotFrameRing> _frameRings = new();

	/// <summary>Peers that have asked for a description they never received, and are therefore sent each one twice.</summary>
	private readonly HashSet<int> _peersThatLoseDescriptions = new();

	private readonly Dictionary<int, int> _descriptionsLostByPeer = new();

	private readonly Dictionary<int, long> _lossWindowStartedTickPerPeer = new();

	private int _startupSettlesAtSequence;

	/// <summary>
	///     How long a peer that has lost a description keeps being sent every description twice after the last one it asked
	///     for. A link that has stopped naming lost descriptions has stopped losing them; keeping the repeat on after that
	///     doubles its bandwidth for a fault that has gone.
	/// </summary>
	private const long LossyPeerMemoryTicks = MatchTiming.SimulationTickRateHz * 5;

	private const int LossyPeerMinimumLostDescriptions = 3;
	private readonly SnapshotFragmentBuffer[] _fragmentBuffers = CreateFragmentBuffers();
	private readonly SnapshotBaselineReason[] _pendingBaselineReasons = new SnapshotBaselineReason[EcsWorld.MAX_PLAYERS];
	private readonly int[] _publishedSerializedBytes = new int[EcsWorld.MAX_PLAYERS];
	private readonly double[] _publishedFramingMilliseconds = new double[EcsWorld.MAX_PLAYERS];

	private readonly ArrayBufferWriter<byte>[] _serializationWriters = CreateSerializationWriters();

	private static ArrayBufferWriter<byte>[] CreateSerializationWriters()
	{
		var writers = new ArrayBufferWriter<byte>[EcsWorld.MAX_PLAYERS];
		for (int index = 0; index < writers.Length; index++) writers[index] = new ArrayBufferWriter<byte>(4096);
		return writers;
	}

	private static SnapshotFragmentBuffer[] CreateFragmentBuffers()
	{
		var buffers = new SnapshotFragmentBuffer[EcsWorld.MAX_PLAYERS];
		for (int index = 0; index < buffers.Length; index++) buffers[index] = new SnapshotFragmentBuffer();
		return buffers;
	}

	private readonly SnapshotCompressor?[] _publishCompressors = new SnapshotCompressor?[SnapshotFanOut.MaximumWorkerCount];

	private Action<int>? _describeOneLane;
	private int _publishPeerCount;
	private int _publishLaneCount;
	private int _publishSequence;
	private bool _readyAnnounced;
	private bool _disposed;
	private bool _paused;
	private int _pausedByPlayerSlot = -1;
	private double _nextObstacleCheckTime;
	private string _mapHash = string.Empty;

	private readonly Stopwatch _startClock = new();

	private void MarkStartStage(string stage)
	{
		_log.Write($"match start: {stage} at {_startClock.Elapsed.TotalMilliseconds:F0} ms");
	}

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
			StartLobbyScript(_preparedLobbyWorld);
		}
		catch (Exception exception)
		{
			_preparedLobbyWorld = null;
			_log.Write($"the map's world could not be prepared, so the match will load it when it starts: {exception.Message}");
		}
	}

	/// <summary>
	///     Asks the lobby's map whether its host must read a warning before the match starts. The script is started after
	///     the lobby that asks about it, so this resolves the script every time rather than holding it.
	/// </summary>
	/// <returns>The text the host has to accept, or nothing when the map has no warning.</returns>
	private string? DescribeLobbyStartWarning()
	{
		ServerLobbyScript? script = _lobbyScript;
		if (script == null) return null;

		return script.TryGetStartWarning(out string warning) ? warning : null;
	}

	/// <summary>
	///     Starts the lobby's script at tick zero, on the world the lobby prepared, so the roster it states is already in
	///     force while the lobby fills and the match has nothing left to initialize when it starts.
	/// </summary>
	/// <param name="world">The world the lobby prepared.</param>
	private void StartLobbyScript(LoadedMap world)
	{
		ServerLobbyRoster? roster = _lobby?.Roster;
		if (roster == null) return;

		var paths = ServerMatchPaths.From(_arguments);
		_lobbyScript = ServerLobbyScript.Start(world, paths, _log, roster, _arguments.WasmDebug, _arguments.WorkspacePath);
	}

	private LoadedMap TakePreparedLobbyWorldOrLoad(IReadOnlyList<MatchRosterSeat> roster)
	{
		LoadedMap? prepared = _preparedLobbyWorld;
		_preparedLobbyWorld = null;

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

	private readonly Dictionary<string, int> _lobbySessionSlots = new(StringComparer.Ordinal);
	private long _deliveredPresentationCount;
	private int _rateLimitedCommandCount;
	private long _nextRateLimitReportTicks;
	private Action<string>? _scriptLogSink;
	private AuthoritySyncRecorder? _syncRecorder;
	private SnapshotTrafficRecorder? _trafficRecorder;
	private ServerReportWriter? _reportWriter;

	private void QueueTrafficReport(bool includeSequenceCoverage = false)
	{
		SnapshotTrafficRecorder? recorder = _trafficRecorder;
		ServerReportWriter? writer = _reportWriter;
		if (recorder == null || writer == null) return;

		double recordedSeconds = (_simulation?.TickCount ?? 0) * MatchTiming.FixedTickSeconds;
		writer.Enqueue(_arguments.RecordAuthorityPath, recorder.BuildReport(recordedSeconds, MatchTiming.SimulationTickRateHz, includeSequenceCoverage));
	}

	private const int TrafficReportIntervalSequences = MatchTiming.SnapshotRateHz * 10;
}
