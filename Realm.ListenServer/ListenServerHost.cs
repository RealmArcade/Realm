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

public class ListenServerHost : IServerTransportSink
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
	private readonly bool[] _pendingSkippedPeers = new bool[EcsWorld.MAX_PLAYERS];
	private readonly Dictionary<int, SnapshotFrameRing> _frameRings = new();
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
	private ParallelOptions? _publishParallelism;
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
		}
		catch (Exception exception)
		{
			_preparedLobbyWorld = null;
			_log.Write($"the map's world could not be prepared, so the match will load it when it starts: {exception.Message}");
		}
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

	public ListenServerHost(ServerArguments arguments, ServerLog log, IServerTransport transport)
	{
		_arguments = arguments;
		_log = log;
		_transport = transport;
	}

	public bool IsRunning { get; private set; }

	public bool ShutdownRequested { get; private set; }

	public int Port => _transport.Port;

	public ServerLog Log => _log;

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
				_syncRecorder = new AuthoritySyncRecorder(map, _arguments.RecordAuthorityPath, out string syncRecordPath);
				_log.Write($"this match records the world it publishes into '{syncRecordPath}'");
				_trafficRecorder = new SnapshotTrafficRecorder();
				_reportWriter = new ServerReportWriter();
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

	private string? StartMatchFromLobby(IReadOnlyList<MatchRosterSeat> roster, IReadOnlyList<(int PeerId, int Slot)> assignments, IReadOnlyDictionary<string, int> sessionSlots, int seed)
	{
		if (_simulation != null) return "This lobby has already started its match.";

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

			_preparedLobbyWorldAttempted = false;
			if (_scriptLogSink != null)
			{
				WasmScriptHost.OnWasmLog -= _scriptLogSink;
				_scriptLogSink = null;
			}

			return "The match could not start; the authority's log explains why.";
		}

		_lobbySessionSlots.Clear();
		foreach (var pair in sessionSlots)
		{
			_lobbySessionSlots[pair.Key] = pair.Value;
			_sessionTokenSlots[pair.Key] = pair.Value;
		}

		for (int index = 0; index < assignments.Count; index++)
		{
			(int peerId, int slot) = assignments[index];
			if (!_simulation.SeatPeerAtSlot(peerId, slot))
			{
				_log.Write($"peer {peerId} could not be given lobby seat {slot}; it will not be part of the match");
				continue;
			}

			BeginSeatedPeerBookkeeping(peerId, slot, _simulation.TickCount);
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

		// A paused match holds its world where it is: nothing is stepped and nothing is published, so every screen keeps
		// the world it was last told about instead of watching it drift while nobody can act in it. The pause itself is
		// restated while it is held, because a match that says nothing at all for longer than a peer's liveness window is
		// indistinguishable from one that has gone away.
		if (_paused)
		{
			_nextTickTime = nowSeconds + MatchTiming.FixedTickSeconds;
			if (nowSeconds >= _nextPauseHeartbeatSeconds)
			{
				_nextPauseHeartbeatSeconds = nowSeconds + PauseHeartbeatSeconds;
				BroadcastPauseState(-1);
			}

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
			_commandsThisTick.Clear();

			simulation.Step();
			PublishTick(simulation.AdvanceSnapshotSequence());
			_nextTickTime += MatchTiming.FixedTickSeconds;
			stepped++;
		}

		if (nowSeconds >= _nextTickTime) _nextTickTime = nowSeconds + MatchTiming.FixedTickSeconds;
	}

	private const int MaximumTicksPerCall = 5;

	private const double PauseHeartbeatSeconds = 1.0;

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

		QueueTrafficReport(includeSequenceCoverage: true);

		_seatedPeers.Clear();
		_frameRings.Clear();
		_connectedPeers.Clear();
		_peerLanguages.Clear();
		_sessionTokenSlots.Clear();
		_lobbySessionSlots.Clear();
		_lobby = null;
		_mapShares = null;

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

	public void OnPeerConnected(int peerId)
	{
		_connectedPeers.Add(peerId);
		_log.Write($"peer {peerId} connected");

		// The challenge is the first thing a peer is told, because the hello is the first thing it may send. The ticket
		// has to prove itself without travelling; an open match still hands a challenge out, so a peer never has to know
		// whether the authority it reached was started with a ticket.
		string challenge = RealmProtocol.CreateChallenge();
		_pendingChallenges[peerId] = challenge;
		_transport.Send(peerId, NetworkFrameKind.HandshakeChallenge, MemoryPackSerializer.Serialize(new HandshakeChallenge { Nonce = challenge }));
	}

	public void OnPeerDisconnected(int peerId, string reason)
	{
		_connectedPeers.Remove(peerId);

		if (_lobby != null && _simulation == null)
		{
			_log.Write($"peer {peerId} left the lobby: {reason}");
			_lobby.OnPeerDisconnected(peerId);
			ForgetPeerBookkeeping(peerId);
			return;
		}

		bool wasSeated = _seatedPeers.Remove(peerId);
		_log.Write(wasSeated ? $"peer {peerId} left: {reason}" : $"peer {peerId} dropped before it was seated: {reason}");

		_simulation?.ForgetPeer(peerId);
		ForgetPeerBookkeeping(peerId);

		// A pause belongs to whoever asked for it. If that player is gone, nobody left can resume it, so the match resumes
		// itself rather than staying stopped for the rest of the session.
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

	private bool TryReclaimSessionSlot(ServerSimulation simulation, int peerId, string sessionToken, out int playerSlot, out int team)
	{
		playerSlot = 0;
		team = 0;

		if (string.IsNullOrEmpty(sessionToken)) return false;
		if (!_sessionTokenSlots.TryGetValue(sessionToken, out playerSlot)) return false;

		if (!simulation.SeatPeerAtSlot(peerId, playerSlot))
		{
			playerSlot = 0;
			return false;
		}

		team = simulation.ReadTeam(playerSlot);
		_log.Write($"peer {peerId} took back player slot {playerSlot} with the session it was seated with");
		return true;
	}

	private void BeginSeatedPeerBookkeeping(int peerId, int playerSlot, long tick)
	{
		_seatedPeers[peerId] = playerSlot;
		_seatedAtTickPerPeer[peerId] = tick;
		_unacknowledgedPublicationsPerPeer[peerId] = -1;
		_acknowledgedSequencePerPeer.Remove(peerId);
		_roundTripMillisecondsPerPeer.Remove(peerId);
		_firstSnapshotSequencePerPeer.Remove(peerId);
		_lastLoggedAckSequencePerPeer.Remove(peerId);
		_lastBaselineRequestTickPerPeer.Remove(peerId);
		_lastAcknowledgedProgressTickPerPeer[peerId] = tick;
		_lastRepairSentTickPerPeer.Remove(peerId);
		_obstacleSignaturePerPeer.Remove(peerId);
	}

	private void ForgetPeerBookkeeping(int peerId)
	{
		_pendingChallenges.Remove(peerId);
		_lastLoggedAckSequencePerPeer.Remove(peerId);
		_firstSnapshotSequencePerPeer.Remove(peerId);
		_lastBaselineRequestTickPerPeer.Remove(peerId);
		_lastAcknowledgedProgressTickPerPeer.Remove(peerId);
		_unacknowledgedPublicationsPerPeer.Remove(peerId);
		_acknowledgedSequencePerPeer.Remove(peerId);
		_roundTripMillisecondsPerPeer.Remove(peerId);
		_seatedAtTickPerPeer.Remove(peerId);
		_lastRepairSentTickPerPeer.Remove(peerId);
		_obstacleSignaturePerPeer.Remove(peerId);
		_commandsThisTick.Remove(peerId);
		_peerLanguages.Remove(peerId);

		// Nothing this peer was sent may answer a repair once it is gone, and a peer id is not a session: one that leaves
		// and comes back under the same id must not be answered from the descriptions it was sent before it went.
		_frameRings.Remove(peerId);
	}

	public void OnFrameReceived(int peerId, NetworkFrameKind kind, ReadOnlySpan<byte> payload)
	{
		// A session is either a lobby or a match, and a peer's role decides which traffic it may reach. While the lobby
		// owns the session there is no world to order or acknowledge, so match frames are refused rather than queued for
		// a match that does not exist yet.
		if (_lobby != null && _simulation == null)
		{
			HandleLobbyFrame(peerId, kind, payload);
			return;
		}

		ServerSimulation? simulation = _simulation;
		if (simulation == null) return;

		// A seat is not a match peer yet, whatever frame it sends: nothing it could say to a running match is accepted
		// here, so the connection that said it is the thing that ends.
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
				if (TryClaimBaselineRequest(peerId, simulation.TickCount)) simulation.RequestBaseline(peerId);
				break;
			case NetworkFrameKind.SnapshotRepairRequest:
				HandleSnapshotRepairRequest(peerId, payload);
				break;
			case NetworkFrameKind.LatencyProbe:
				// The echo travels the same stream the probe did. Answering it reliably would time the probe's loss and
				// the retransmission that recovered it rather than the path, and that figure is what every screen sizes
				// its presentation buffer from.
				_transport.Send(peerId, NetworkFrameKind.LatencyProbeEcho, payload.ToArray(), FrameDelivery.UnreliableOrdered);
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
				// The echo travels the same stream the probe did. Answering it reliably would time the probe's loss and
				// the retransmission that recovered it rather than the path, and that figure is what every screen sizes
				// its presentation buffer from.
				_transport.Send(peerId, NetworkFrameKind.LatencyProbeEcho, payload.ToArray(), FrameDelivery.UnreliableOrdered);
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
				_log.Write($"lobby seat {seat.Slot} ('{seat.Name}') sent a {kind} frame, which a lobby member may not send");
				_transport.Disconnect(peerId);
				break;
		}
	}

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

		if (!string.IsNullOrEmpty(hello.MapHash) && _mapHash.Length > 0 && !string.Equals(hello.MapHash, _mapHash, StringComparison.Ordinal)) return "server_rejected_map";
		return string.Empty;
	}

	private bool ProvesAdmin(int peerId, string? presentedProof)
	{
		if (_arguments.AdminToken.Length == 0) return false;
		if (string.IsNullOrEmpty(presentedProof)) return false;
		if (!_pendingChallenges.Remove(peerId, out string? challenge)) return false;

		byte[] presented = Encoding.UTF8.GetBytes(presentedProof);
		byte[] expected = Encoding.UTF8.GetBytes(RealmProtocol.ProveToken(_arguments.AdminToken, challenge));
		return CryptographicOperations.FixedTimeEquals(presented, expected);
	}

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
		else if (!TryReclaimSessionSlot(simulation, peerId, hello.SessionToken, out playerSlot, out team)
		         && !simulation.TryAssignPlayerSlot(peerId, out playerSlot, out team))
		{
			const string noSlot = "server_rejected_no_free_slot";
			_log.Write($"peer {peerId} refused: every player slot this map declares is taken");
			SendHello(peerId, noSlot);
			_transport.Disconnect(peerId);
			return;
		}

		if (!string.IsNullOrEmpty(hello.SessionToken)) _sessionTokenSlots[hello.SessionToken] = playerSlot;
		BeginSeatedPeerBookkeeping(peerId, playerSlot, simulation.TickCount);
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
		TryClaimBaselineRequest(peerId, simulation.TickCount);
		simulation.RequestBaseline(peerId);

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

	private const string CheatsMatchRejection = "server_rejected_cheats_match_is_local_only";

	private const string LobbyRosterClosedRejection = "server_rejected_lobby_roster_is_closed";

	private const string LobbySeatTakenRejection = "server_rejected_lobby_seat_is_taken";

	private string DescribeHandshakeRejection(int peerId, in ClientHello hello)
	{
		if (hello.Protocol != RealmProtocol.Version) return "server_rejected_protocol";
		if (!string.Equals(hello.Build, RealmVersion.GameBuildNumber, StringComparison.Ordinal)) return "server_rejected_build";
		if (!string.Equals(hello.WireTableHash, ReplicatedComponents.ComputeWireTableHash(), StringComparison.Ordinal)) return "server_rejected_wire_table";

		if (!string.IsNullOrEmpty(hello.MapHash) && !string.Equals(hello.MapHash, _mapHash, StringComparison.Ordinal)) return "server_rejected_map";

		if (!TokenMatches(peerId, hello.TokenProof)) return "server_rejected_token";
		return string.Empty;
	}

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

	private void PublishTick(int sequence)
	{
		ServerSimulation? simulation = _simulation;
		if (simulation == null) return;


		_syncRecorder?.BeginSequence(sequence, simulation.TickCount);

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
			_pendingSkippedPeers[index] = ShouldSkipSlowPeer(seatedPeerId, sequence, simulation.TickCount, index);
		}

		simulation.PreparePeerDescriptions(sequence);

		// Peer work is split over fixed lanes, so a lane owns its builder for the whole match instead of queuing for one.
		// The ability deltas are collected for every peer here, on the tick thread, because the registry behind them keeps
		// one scratch table per process and cannot be entered by two lanes at once.
		int laneCount = Math.Min(peerCount, simulation.SnapshotWorkerCount);
		simulation.PrepareAbilityDeltas(_seatedPeerIds.AsSpan(0, peerCount), _pendingBaselineRequests.AsSpan(0, peerCount), _pendingSkippedPeers.AsSpan(0, peerCount));

		_publishSequence = sequence;

		if (laneCount == 1)
		{
			for (int peerIndex = 0; peerIndex < peerCount; peerIndex++) DescribeAndFrameOnePeer(peerIndex, 0);
		}
		else
		{
			_publishPeerCount = peerCount;
			_publishLaneCount = laneCount;
			_publishParallelism ??= new ParallelOptions();
			_publishParallelism.MaxDegreeOfParallelism = laneCount;
			_describeOneLane ??= DescribeOneLane;

			// The lanes only read the world, and the lease is what says so: a lane that starts writing it instead is
			// reported by the debug assertion a structural change raises rather than corrupting a buffer mid-description.
			EcsWorld world = simulation.Map.World;
			world.EnterWorkerLease();
			try
			{
				Parallel.For(0, laneCount, _publishParallelism, _describeOneLane);
			}
			finally
			{
				world.ExitWorkerLease();
			}
		}

		for (int index = 0; index < peerCount; index++) SendPublishedSnapshot(index);

		_syncRecorder?.EndSequence();

		if (_trafficRecorder != null && sequence % TrafficReportIntervalSequences == 0) QueueTrafficReport();

		if (presentation.Length > 0) SendPresentation(peerCount, presentation);
	}

	private void DescribeOneLane(int laneIndex)
	{
		for (int peerIndex = laneIndex; peerIndex < _publishPeerCount; peerIndex += _publishLaneCount)
		{
			DescribeAndFrameOnePeer(peerIndex, laneIndex);
		}
	}

	private void DescribeAndFrameOnePeer(int index, int laneIndex)
	{
		ServerSimulation simulation = _simulation!;
		int peerId = _seatedPeerIds[index];

		// A peer this far behind is told nothing this tick: describing it would be work its screen cannot draw. What it
		// is owed is a baseline, which the tick that decided to skip it already asked for. The slot keeps its description
		// fields - its containers are what the next description is written into - and a null frame is what says it was
		// told nothing.
		if (_pendingSkippedPeers[index])
		{
			_publishedFrames[index] = null;
			return;
		}

		// The description is written into the slot this peer already has, so describing a room replaces nothing and
		// empties what it held instead of allocating a set of lists per peer per tick.
		simulation.BuildSnapshotForPeerInto(ref _publishedSnapshots[index], peerId, _publishSequence, _pendingBaselineRequests[index], laneIndex, index);
		WorldSnapshot snapshot = _publishedSnapshots[index];

		// Serializing and framing is the host's cost of describing one peer, measured where it is paid, and the serializer
		// writes into a buffer this peer's slot already owns.
		long framingStarted = _trafficRecorder == null ? 0 : System.Diagnostics.Stopwatch.GetTimestamp();
		ArrayBufferWriter<byte> serialized = _serializationWriters[index];
		serialized.ResetWrittenCount();
		MemoryPackSerializer.Serialize(serialized, snapshot);
		SnapshotCompressor compressor = _publishCompressors[laneIndex] ??= new SnapshotCompressor();
		_publishedFrames[index] = snapshot.IsBaseline
			? compressor.EncodeBaseline(serialized.WrittenSpan)
			: compressor.EncodeDelta(serialized.WrittenSpan);
		if (_trafficRecorder == null) return;

		_publishedSerializedBytes[index] = serialized.WrittenCount;
		_publishedFramingMilliseconds[index] = (System.Diagnostics.Stopwatch.GetTimestamp() - framingStarted) * 1000.0 / System.Diagnostics.Stopwatch.Frequency;
	}

	private void SendPublishedSnapshot(int index)
	{
		ServerSimulation? simulation = _simulation;
		if (simulation == null) return;

		int peerId = _seatedPeerIds[index];
		if (_publishedFrames[index] == null) return;

		WorldSnapshot snapshot = _publishedSnapshots[index];

		bool isFirstFrameForPeer = !_firstSnapshotSequencePerPeer.ContainsKey(peerId);

		_syncRecorder?.AddPeerSnapshot(peerId, in snapshot);

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
		// than complete; a lost one costs the change it carried until the acknowledgement reports the gap. A baseline is
		// the only thing a peer can rebuild from, so it travels reliably whatever its size.
		// A delta too large for one datagram is split rather than moved to the reliable stream: on the reliable stream it
		// would hold every description queued behind it, which on a busy match turns one large frame into the stall that
		// costs the peer everything it was about to be told.
		bool carriedUnreliably = !snapshot.IsBaseline;
		if (snapshot.IsBaseline)
		{
			_transport.Send(peerId, NetworkFrameKind.Snapshot, _publishedFrames[index]);
		}
		else if (_publishedFrames[index].Length <= SnapshotFragment.MaximumDatagramBytes)
		{
			_transport.Send(peerId, NetworkFrameKind.Snapshot, _publishedFrames[index], FrameDelivery.UnreliableOrdered);
		}
		else
		{
			SnapshotFragmentBuffer fragments = _fragmentBuffers[index];
			fragments.Split(_publishedFrames[index], _publishSequence);
			ReadOnlySpan<byte[]> datagrams = fragments.Datagrams;
			for (int datagramIndex = 0; datagramIndex < datagrams.Length; datagramIndex++)
			{
				_transport.Send(peerId, NetworkFrameKind.Snapshot, datagrams[datagramIndex], FrameDelivery.UnreliableOrdered);
			}

			_fragmentedSnapshotCount++;
		}

		if (carriedUnreliably) _unreliableSnapshotCount++;
		else _reliableSnapshotCount++;

		// The description this peer was just sent is kept until the ring's width has passed it, so a hole the
		// acknowledgement reports is answered with the description that went missing rather than with the whole world.
		// The frame is the peer's own buffer and the next tick writes a new one, so the ring holds it unshared.
		FrameRingFor(peerId).Add(_publishSequence, _publishedFrames[index]);

		// The first sequence this peer was described with bounds what its acknowledgement mask can claim: a bit for a
		// sequence the match never sent was never meant to be set. A baseline starts that bound over.
		if (snapshot.IsBaseline || !_firstSnapshotSequencePerPeer.ContainsKey(peerId)) _firstSnapshotSequencePerPeer[peerId] = _publishSequence;

		// What the map declared about the match - cards, colours, labels and the camera it placed - is stated once, to the
		// peer that has just been seated, and only to it. Sending it after every baseline replayed a camera pan the map
		// made at match start every ten seconds and snapped the screen back while the player looked elsewhere.
		if (!snapshot.IsBaseline || !isFirstFrameForPeer || !_seatedPeers.TryGetValue(peerId, out int playerSlot)) return;

		ServerPresentationEvent[] description = simulation.DescribePresentationStateFor(playerSlot);
		if (description.Length > 0) SendPresentationToPeer(peerId, description);
	}

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

	private void SendPresentation(int peerCount, ServerPresentationEvent[] events) => SendPresentation(_seatedPeerIds.AsSpan(0, peerCount), events, _publishSequence);

	private void SendPresentationToPeer(int peerId, ServerPresentationEvent[] events)
	{
		Span<int> singlePeer = stackalloc int[1];
		singlePeer[0] = peerId;

		// What a map declares about a match is stated once to the peer that has just been seated, so all of it travels on
		// the stream that cannot lose it: there is no later request to state it again.
		_statePresentationBatch.Clear();
		for (int index = 0; index < events.Length; index++) _statePresentationBatch.Add(events[index].ToMessage(PresentationMessage.StateDescriptionSequence));

		SendPresentationBatch(peerId, _statePresentationBatch, FrameDelivery.Reliable);
	}

	/// <summary>
	///     Tells each of these peers what the map raised this tick, in one frame per peer and per stream. A request that
	///     states something the screen cannot be left without travels on the stream that cannot lose it, and a moment it is
	///     only allowed to miss travels on the stream that would rather be current: putting an effect behind a baseline
	///     held every effect queued after it, and giving each one its own reliable frame cost a header apiece for a shower
	///     of sparks.
	/// </summary>
	private void SendPresentation(ReadOnlySpan<int> peerIds, ServerPresentationEvent[] events, int sequence)
	{
		for (int peerIndex = 0; peerIndex < peerIds.Length; peerIndex++)
		{
			int peerId = peerIds[peerIndex];
			bool seated = _seatedPeers.TryGetValue(peerId, out int playerSlot);

			_statePresentationBatch.Clear();
			_momentPresentationBatch.Clear();

			for (int eventIndex = 0; eventIndex < events.Length; eventIndex++)
			{
				ServerPresentationEvent request = events[eventIndex];
				if (request.PlayerIndex != ServerPresentationEvent.Everyone && (!seated || request.PlayerIndex != playerSlot)) continue;

				if (PresentationBatch.IsState(request.Kind)) _statePresentationBatch.Add(request.ToMessage(sequence));
				else _momentPresentationBatch.Add(request.ToMessage(sequence));
			}

			SendPresentationBatch(peerId, _statePresentationBatch, FrameDelivery.Reliable);
			SendPresentationBatch(peerId, _momentPresentationBatch, FrameDelivery.UnreliableOrdered);
		}
	}

	private void SendPresentationBatch(int peerId, List<PresentationMessage> messages, FrameDelivery delivery)
	{
		if (messages.Count == 0) return;

		var batch = new PresentationBatch
		{
			Messages = new PresentationMessage[messages.Count]
		};

		for (int index = 0; index < messages.Count; index++) batch.Messages[index] = messages[index];

		_transport.Send(peerId, NetworkFrameKind.Presentation, MemoryPackSerializer.Serialize(batch), delivery);
		_deliveredPresentationCount += messages.Count;
	}

	/// <summary>
	///     Answers a peer that names a run of descriptions it never received. The run is only answered while this match still
	///     holds every one of them: a run wider than the ring, or one the ring has already passed, says the peer stopped
	///     reading rather than that a datagram went missing, and nothing short of a baseline puts that screen back.
	/// </summary>
	private void HandleSnapshotRepairRequest(int peerId, ReadOnlySpan<byte> payload)
	{
		ServerSimulation? simulation = _simulation;
		if (simulation == null) return;

		SnapshotRepairRequest request;
		try
		{
			request = MemoryPackSerializer.Deserialize<SnapshotRepairRequest>(payload.ToArray());
		}
		catch (Exception exception) when (exception is IOException or InvalidDataException or ArgumentException)
		{
			_log.Write($"peer {peerId} sent a repair request that could not be read: {exception.Message}");
			return;
		}

		if (request.Count <= 0 || request.Count > SnapshotFrameRing.Capacity || request.FirstSequence <= 0) return;

		long tick = simulation.TickCount;
		if (_lastRepairSentTickPerPeer.TryGetValue(peerId, out long lastRepairTick) && tick - lastRepairTick < RepairCooldownTicks) return;

		Span<int> missingSequences = stackalloc int[request.Count];
		for (int index = 0; index < missingSequences.Length; index++) missingSequences[index] = request.FirstSequence + index;

		if (TryResendMissingDescriptions(peerId, missingSequences))
		{
			_lastRepairSentTickPerPeer[peerId] = tick;
			_log.Write($"peer {peerId} asked for {request.Count} description(s) from sequence {request.FirstSequence}; they are sent again");
			return;
		}

		if (!TryClaimBaselineRequest(peerId, tick)) return;

		simulation.RequestBaseline(peerId);
		_log.Write($"peer {peerId} asked for a run this match no longer holds; it is described from scratch");
	}

	/// <summary>
	///     Sends each of these descriptions again, exactly as the peer was first sent them. Every one is checked before any
	///     is sent, because half a repaired run is a peer told the wrong thing about where the world is.
	/// </summary>
	private bool TryResendMissingDescriptions(int peerId, ReadOnlySpan<int> missingSequences)
	{
		if (missingSequences.Length == 0) return false;
		if (!_frameRings.TryGetValue(peerId, out SnapshotFrameRing? ring)) return false;

		for (int index = 0; index < missingSequences.Length; index++)
		{
			if (!ring.TryGet(missingSequences[index], out _)) return false;
		}

		for (int index = 0; index < missingSequences.Length; index++)
		{
			ring.TryGet(missingSequences[index], out byte[] frame);

			// A repair is asked for once and answered over the stream that cannot lose it: the peer has already shown
			// that it is missing state, and losing the repair as well would cost another round trip to learn the same.
			_transport.Send(peerId, NetworkFrameKind.Snapshot, frame, FrameDelivery.Reliable);
		}

		return true;
	}

	/// <summary>
	///     The ring this peer's descriptions are kept in, created the first time the peer is described. A ring belongs to
	///     the peer rather than to the seat it was published in, so no seat taken or given up elsewhere can address it.
	/// </summary>
	private SnapshotFrameRing FrameRingFor(int peerId)
	{
		if (_frameRings.TryGetValue(peerId, out SnapshotFrameRing? ring)) return ring;

		ring = new SnapshotFrameRing(peerId);
		_frameRings[peerId] = ring;
		return ring;
	}

	private void HandleSnapshotAck(int peerId, ReadOnlySpan<byte> payload)
	{
		ServerSimulation? simulation = _simulation;
		if (simulation == null) return;

		// A peer that answers is a peer that is reading, whatever else the answer says: the count of descriptions it has
		// not answered starts over here, which is what tells the gate below that a screen has caught up.
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

		// A peer that reports an older sequence than it did before is a restarted screen rather than one going
		// backwards, so it is taken at the value it now holds.
		if (!_acknowledgedSequencePerPeer.TryGetValue(peerId, out int acknowledgedSequence) || ack.Sequence >= acknowledgedSequence)
		{
			// Answering with a description this peer had already acknowledged says it is still reading but not that it has
			// got any further, so only a sequence it had not reported before counts as progress.
			if (ack.Sequence > acknowledgedSequence) _lastAcknowledgedProgressTickPerPeer[peerId] = simulation.TickCount;
			_acknowledgedSequencePerPeer[peerId] = ack.Sequence;
		}

		_roundTripMillisecondsPerPeer[peerId] = Math.Max(0, ack.RoundTripMilliseconds);

		LogAcknowledgedSequence(peerId, ack);

		if (!_firstSnapshotSequencePerPeer.TryGetValue(peerId, out int firstDescribedSequence)) return;

		int describedSequences = ack.Sequence - firstDescribedSequence;
		if (describedSequences <= 0) return;

		// Only sequences this peer was actually described with can be missing: the mask cannot claim a bit for a
		// sequence the match never sent, or every acknowledgement would describe the peer from scratch.
		int coveredBits = Math.Min(64, describedSequences + 1);
		ulong expected = coveredBits >= 64 ? ulong.MaxValue : (1UL << coveredBits) - 1UL;
		ulong missing = expected & ~ack.AppliedMask;
		if ((ack.AppliedMask & expected) == expected) return;

		int missingCount = 0;
		int missingBits = 0;
		Span<int> missingSequences = stackalloc int[SnapshotFrameRing.Capacity];
		for (int bit = 0; bit < 64; bit++)
		{
			if ((missing & (1UL << bit)) == 0) continue;

			missingBits++;
			if (missingCount < missingSequences.Length) missingSequences[missingCount++] = ack.Sequence - bit;
		}

		// A repair answered once is not asked for again on every answer that still reports the same hole: the
		// description is already on its way, and asking again each time spent the request budget on nothing.
		long tick = simulation.TickCount;
		if (_lastRepairSentTickPerPeer.TryGetValue(peerId, out long lastRepairTick) && tick - lastRepairTick < RepairCooldownTicks) return;

		if (missingBits <= SnapshotFrameRing.Capacity && TryResendMissingDescriptions(peerId, missingSequences.Slice(0, missingCount)))
		{
			_lastRepairSentTickPerPeer[peerId] = tick;
			_log.Write($"peer {peerId} is missing {missingBits} description(s); the missing ones are sent again");
			return;
		}

		if (!TryClaimBaselineRequest(peerId, tick)) return;

		simulation.RequestBaseline(peerId);
		_log.Write($"peer {peerId} is missing a description this match no longer holds; it is described from scratch");
	}

	private void LogAcknowledgedSequence(int peerId, in SnapshotAck ack)
	{
		if (_lastLoggedAckSequencePerPeer.TryGetValue(peerId, out long lastLogged) && ack.Sequence - lastLogged < AckReportInterval) return;

		_lastLoggedAckSequencePerPeer[peerId] = ack.Sequence;
		_log.Write($"peer {peerId} acknowledged sequence {ack.Sequence} with mask {ack.AppliedMask:x16}");
	}

	/// <summary>
	///     Whether this peer needs the world stated again rather than described again. The safety refresh used to run every
	///     ten seconds whatever the peer was saying, which put a hundred kilobytes on that peer's reliable stream for a
	///     screen whose every acknowledgement was proving continuity, and held everything queued behind it while it
	///     travelled. What is left is the case it was written for: a peer that has stopped acknowledging cannot say whether
	///     it is still holding a world, and only a description from scratch puts that beyond doubt.
	/// </summary>
	private bool IsRefreshDue(int peerId, long tick)
	{
		return _lastAcknowledgedProgressTickPerPeer.TryGetValue(peerId, out long lastProgressTick) && tick - lastProgressTick >= SnapshotRefreshStallTicks;
	}

	private bool TryClaimBaselineRequest(int peerId, long tick)
	{
		if (_lastBaselineRequestTickPerPeer.TryGetValue(peerId, out long lastRequestTick) && tick - lastRequestTick < BaselineRequestCooldownTicks) return false;

		_lastBaselineRequestTickPerPeer[peerId] = tick;
		return true;
	}

	private bool ShouldSkipSlowPeer(int peerId, int sequence, long tick, int index)
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

		// Behind is measured from the sequence the peer says it applied, plus one round trip, not from how many
		// descriptions it has left unanswered: counting those measures the link rather than the screen. On a healthy long
		// link that count settles at the round trip's worth of publishing, so past about four hundred milliseconds a peer
		// drawing everything sent it tripped the bound and was re-baselined once a second.
		if (_acknowledgedSequencePerPeer.TryGetValue(peerId, out int acknowledgedSequence))
		{
			int lagSequences = sequence - acknowledgedSequence;
			if (lagSequences <= MaximumLagBehindSequences + RoundTripSequences(peerId)) return false;
		}

		// A baseline asked for here is the only thing this peer is sent while it stays silent. One the cooldown refuses
		// means a description from scratch is already on its way, so this tick is skipped rather than spent describing
		// towards a screen that is not reading.
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

	private int RoundTripSequences(int peerId)
	{
		if (!_roundTripMillisecondsPerPeer.TryGetValue(peerId, out int roundTripMilliseconds)) return 0;

		return (int)MathF.Ceiling(roundTripMilliseconds * 0.001f * MatchTiming.SnapshotRateHz);
	}

	private const long AckReportInterval = 100;

	private const long BaselineRequestCooldownTicks = MatchTiming.BaselineRequestCooldownTicks;

	private const int NeverAnsweredGraceTicks = MatchTiming.SimulationTickRateHz * 45;

	private const int MaximumLagBehindSequences = MatchTiming.SnapshotRateHz / 2;

	private const int MaximumUnacknowledgedBeforeDisconnect = MatchTiming.SnapshotRateHz * 15;

	private const long RepairCooldownTicks = MatchTiming.SnapshotRepairCooldownTicks;

	private readonly Dictionary<int, long> _lastRepairSentTickPerPeer = new();

	private long _unreliableSnapshotCount;
	private long _reliableSnapshotCount;
	private long _fragmentedSnapshotCount;

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

		// Orders handed over since the last tick are this tick's budget. The window is cleared by the tick that consumes
		// it rather than by the frame that happened to arrive before it: this process runs physics faster than the match
		// ticks, and clearing on the frame let a peer hand over three times the work a tick allows.
		int commandId = command.CommandId;
		_commandsThisTick.TryGetValue(peerId, out int commandsThisTick);
		if (commandsThisTick >= MaximumCommandsPerPeerPerTick)
		{
			if (commandsThisTick == MaximumCommandsPerPeerPerTick)
			{
				_log.Write($"peer {peerId} is above the {MaximumCommandsPerPeerPerTick} command(s) a tick allows; its further orders are refused");
			}

			_commandsThisTick[peerId] = commandsThisTick + 1;
			// A peer that floods keeps being refused, and a reader watching the match live has to see the budget doing its
			// job. The running total is restated at most once a second rather than once per refused order, so the report
			// cannot become the flood.
			_rateLimitedCommandCount++;
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

		// A repeat of an order that was already answered is not answered again. The transport is reliable and ordered, so
		// the first answer arrived unless the connection is gone, and a reconnecting peer is described from scratch under
		// a new connection with no record to repeat. A second, negative answer took back a route preview this match had
		// already accepted.
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

	private void SendPauseState(int peerId) =>
		_transport.Send(peerId, NetworkFrameKind.MatchPaused, MemoryPackSerializer.Serialize(new MatchPauseState { Paused = _paused, RequestedByPlayer = -1 }));

	private void BroadcastPauseState(int requestedByPlayer)
	{
		if (_seatedPeers.Count == 0) return;

		var state = new MatchPauseState { Paused = _paused, RequestedByPlayer = requestedByPlayer };
		byte[] payload = MemoryPackSerializer.Serialize(state);

		int[] peerIds = new int[_seatedPeers.Count];
		_seatedPeers.Keys.CopyTo(peerIds, 0);
		for (int index = 0; index < peerIds.Length; index++) _transport.Send(peerIds[index], NetworkFrameKind.MatchPaused, payload);
	}

	private void SendNavMeshObstacles(int peerId)
	{
		if (!TryBuildObstacleVolumes(out NavMeshObstacleVolumes volumes, out int signature, out int count)) return;

		_obstacleSignaturePerPeer[peerId] = signature;
		_transport.Send(peerId, NetworkFrameKind.NavMeshObstacles, MemoryPackSerializer.Serialize(volumes));
		_log.Write($"peer {peerId} given the {count} obstacle volumes this match pathing was baked with");
	}

	private void BroadcastNavMeshObstaclesWhenChanged()
	{
		if (_seatedPeers.Count == 0) return;
		if (!TryBuildObstacleVolumes(out NavMeshObstacleVolumes volumes, out int signature, out int count)) return;

		int staleCount = 0;
		foreach (int seatedPeerId in _seatedPeers.Keys)
		{
			if (staleCount == _peersOwedObstacleVolumes.Length) break;
			if (_obstacleSignaturePerPeer.TryGetValue(seatedPeerId, out int sentSignature) && sentSignature == signature) continue;

			_peersOwedObstacleVolumes[staleCount++] = seatedPeerId;
		}

		if (staleCount == 0) return;

		byte[] payload = MemoryPackSerializer.Serialize(volumes);
		for (int index = 0; index < staleCount; index++)
		{
			_obstacleSignaturePerPeer[_peersOwedObstacleVolumes[index]] = signature;
			_transport.Send(_peersOwedObstacleVolumes[index], NetworkFrameKind.NavMeshObstacles, payload);
		}

		_log.Write($"the match now paths with {count} obstacle volumes; {staleCount} peer(s) were given the new set");
	}

	private bool TryBuildObstacleVolumes(out NavMeshObstacleVolumes volumes, out int signature, out int count)
	{
		volumes = default;
		signature = 0;
		count = 0;

		TerrainNavMeshService? navMesh = _simulation?.Map.Simulation.TerrainNavMeshService;
		if (navMesh == null) return false;

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

	private void DisconnectPeer(int peerId)
	{
		if (!_seatedPeers.Remove(peerId)) return;

		_log.Write($"peer {peerId} was dropped by the map");
		_transport.Send(peerId, NetworkFrameKind.Shutdown, Array.Empty<byte>());
		_transport.Disconnect(peerId);
	}
}
