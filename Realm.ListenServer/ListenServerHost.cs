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

	private double _lastTickSeconds;
	private double _nextTickTime;
	private readonly Dictionary<int, long> _lastLoggedAckSequencePerPeer = new();
	private readonly Dictionary<int, int> _firstSnapshotSequencePerPeer = new();

	private readonly Dictionary<int, long> _lastBaselineRequestTickPerPeer = new();
	private readonly Dictionary<int, long> _lastRefreshBaselineTickPerPeer = new();

	private readonly Dictionary<int, long> _seatedAtTickPerPeer = new();

	private readonly Dictionary<int, int> _unacknowledgedPublicationsPerPeer = new();

	private readonly Dictionary<int, int> _acknowledgedSequencePerPeer = new();

	private readonly Dictionary<int, int> _roundTripMillisecondsPerPeer = new();

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

	private void WriteTrafficReport()
	{
		SnapshotTrafficRecorder? recorder = _trafficRecorder;
		if (recorder == null) return;

		double recordedSeconds = (_simulation?.TickCount ?? 0) * MatchTiming.FixedTickSeconds;
		recorder.WriteReport(_arguments.RecordAuthorityPath, recordedSeconds, MatchTiming.SimulationTickRateHz);
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
			_commandsThisTick.Clear();

			simulation.Step();
			PublishTick(simulation.AdvanceSnapshotSequence());
			_nextTickTime += MatchTiming.FixedTickSeconds;
			stepped++;
		}

		if (nowSeconds >= _nextTickTime) _nextTickTime = nowSeconds + MatchTiming.FixedTickSeconds;
	}

	private const int MaximumTicksPerCall = 5;

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
		if (_readyAnnounced) _log.Write($"snapshots: {_unreliableSnapshotCount} carried as deltas on the current stream, {_reliableSnapshotCount} carried in order");

		WriteTrafficReport();

		_seatedPeers.Clear();
		_connectedPeers.Clear();
		_peerLanguages.Clear();
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

	public void OnPeerConnected(int peerId)
	{
		_connectedPeers.Add(peerId);
		_log.Write($"peer {peerId} connected");

		string challenge = RealmProtocol.CreateChallenge();
		_pendingChallenges[peerId] = challenge;
		_transport.Send(peerId, NetworkFrameKind.HandshakeChallenge, MemoryPackSerializer.Serialize(new HandshakeChallenge { Nonce = challenge }));
	}

	public void OnPeerDisconnected(int peerId, string reason)
	{
		_connectedPeers.Remove(peerId);
		_peerLanguages.Remove(peerId);

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

	public void OnFrameReceived(int peerId, NetworkFrameKind kind, ReadOnlySpan<byte> payload)
	{
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
			_pendingSkippedPeers[index] = ShouldSkipSlowPeer(seatedPeerId, simulation.TickCount, index);
		}

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

		for (int index = 0; index < peerCount; index++) SendPublishedSnapshot(index);

		_syncRecorder?.EndSequence();

		if (_trafficRecorder != null && sequence % TrafficReportIntervalSequences == 0) WriteTrafficReport();

		if (presentation.Length > 0) SendPresentation(peerCount, presentation);
	}

	private void DescribeAndFrameOnePeer(int index)
	{
		ServerSimulation simulation = _simulation!;
		int peerId = _seatedPeerIds[index];

		if (_pendingSkippedPeers[index])
		{
			_publishedFrames[index] = null;
			return;
		}

		simulation.BuildSnapshotForPeerInto(ref _publishedSnapshots[index], peerId, _publishSequence, _pendingBaselineRequests[index]);
		WorldSnapshot snapshot = _publishedSnapshots[index];

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

		bool carriedUnreliably = !snapshot.IsBaseline && (_publishedFrames[index]?.Length ?? 0) <= UnreliableSnapshotLimitBytes;
		_transport.Send(peerId, NetworkFrameKind.Snapshot, _publishedFrames[index], carriedUnreliably ? FrameDelivery.UnreliableOrdered : FrameDelivery.Reliable);
		if (carriedUnreliably) _unreliableSnapshotCount++;
		else _reliableSnapshotCount++;

		if (snapshot.IsBaseline || !_firstSnapshotSequencePerPeer.ContainsKey(peerId)) _firstSnapshotSequencePerPeer[peerId] = _publishSequence;
		if (snapshot.IsBaseline) _lastRefreshBaselineTickPerPeer[peerId] = simulation.TickCount;

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
		SendPresentation(singlePeer, events, PresentationMessage.StateDescriptionSequence);
	}

	private void SendPresentation(ReadOnlySpan<int> peerIds, ServerPresentationEvent[] events, int sequence)
	{
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

	private void HandleSnapshotAck(int peerId, ReadOnlySpan<byte> payload)
	{
		ServerSimulation? simulation = _simulation;
		if (simulation == null) return;

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

		if (!_acknowledgedSequencePerPeer.TryGetValue(peerId, out int acknowledgedSequence) || ack.Sequence >= acknowledgedSequence)
		{
			_acknowledgedSequencePerPeer[peerId] = ack.Sequence;
		}

		_roundTripMillisecondsPerPeer[peerId] = Math.Max(0, ack.RoundTripMilliseconds);

		LogAcknowledgedSequence(peerId, ack);

		if (!_firstSnapshotSequencePerPeer.TryGetValue(peerId, out int firstDescribedSequence)) return;

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

	private bool IsRefreshDue(int peerId, long tick)
	{
		return _lastRefreshBaselineTickPerPeer.TryGetValue(peerId, out long lastBaselineTick) && tick - lastBaselineTick >= SnapshotRefreshTicks;
	}

	private bool TryClaimBaselineRequest(int peerId, long tick)
	{
		if (_lastBaselineRequestTickPerPeer.TryGetValue(peerId, out long lastRequestTick) && tick - lastRequestTick < BaselineRequestCooldownTicks) return false;

		_lastBaselineRequestTickPerPeer[peerId] = tick;
		return true;
	}

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

		if (_acknowledgedSequencePerPeer.TryGetValue(peerId, out int acknowledgedSequence))
		{
			int lagSequences = _publishSequence - acknowledgedSequence;
			if (lagSequences <= MaximumLagBehindSequences + RoundTripSequences(peerId)) return false;
		}

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

	private const int UnreliableSnapshotLimitBytes = 1400;

	private long _unreliableSnapshotCount;
	private long _reliableSnapshotCount;

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
