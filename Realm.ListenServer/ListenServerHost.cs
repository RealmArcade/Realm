using System.Security.Cryptography;
using System.Text;
using MemoryPack;
using Realm.Ecs;
using Realm.Ecs.Replication;
using Realm.Ecs.Services;
using Realm.Shared;
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

	private ServerSimulation? _simulation;
	private double _nextTickTime;
	private long _lastLoggedAckSequence;
	private bool _readyAnnounced;
	private bool _disposed;
	private bool _paused;
	private int _pausedByPlayerSlot = -1;
	private int _lastBroadcastObstacleSignature = int.MinValue;
	private double _nextObstacleCheckTime;
	private long _deliveredPresentationCount;
	private int _rateLimitedCommandCount;
	private long _nextRateLimitReportTicks;
	private Action<string>? _scriptLogSink;
	private AuthoritySnapshotRecorder? _predictionRecorder;
	private AuthoritySyncRecorder? _syncRecorder;

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

		// What the sandbox reports about the script it is running - the debug mode it started in, the process a debugger
		// attaches to and the JIT ELF it exported - belongs in this match's own log, beside the rest of what it decided.
		_scriptLogSink = _log.Write;
		WasmScriptHost.OnWasmLog += _scriptLogSink;

		MatchRosterSeat[] roster = ReadRoster();
		LoadedMap map = MapWorldLoader.Load(_arguments.MapPath, roster);
		try
		{
			var paths = ServerMatchPaths.From(_arguments);
			_simulation = new ServerSimulation(map, paths, _log, _arguments.Seed, _arguments.Cheats, _arguments.WasmDebug, _arguments.WorkspacePath, roster);
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

			// A run that measures prediction asks the match to say where every unit really is, because only this process
			// decided it. Nothing else is asked for it, so a normal match writes no such record.
			if (_arguments.RecordPredictionPath.Length > 0)
			{
				_predictionRecorder = new AuthoritySnapshotRecorder(_arguments.RecordPredictionPath, map.World, out string recordPath);
				_log.Write($"this match records what it decides into '{recordPath}'");
				// A run that compares a screen's ECS against this one asks for the same folder, because the process that
				// decided the world is the only one that can say what a screen should hold after applying a sequence.
				_syncRecorder = new AuthoritySyncRecorder(map, _arguments.RecordPredictionPath, out string syncRecordPath);
				_log.Write($"this match records the world it publishes into '{syncRecordPath}'");
			}

			IsRunning = true;
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

		string mapHash = MapContentHash.Compute(_arguments.MapPath, _arguments.LogPath);
		string wireTableHash = ReplicatedComponents.ComputeWireTableHash();
		var readyInfo = new ServerReadyInfo(Port, RealmProtocol.Version, RealmVersion.GameBuildNumber, mapHash, wireTableHash);

		_log.Write($"ready protocol={readyInfo.Protocol} build={readyInfo.Build} mapHash={mapHash} wireTableHash={wireTableHash}");
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
		ServerSimulation? simulation = _simulation;
		if (!IsRunning || simulation == null) return;

		// A paused match holds its world where it is. Nothing is stepped and nothing is published, so every screen keeps
		// the world it was last told about instead of watching it drift while nobody can act in it.
		if (_paused)
		{
			_nextTickTime = nowSeconds + ServerSimulation.FixedTickSeconds;
			return;
		}

		if (_nextTickTime <= 0) _nextTickTime = nowSeconds;

		// Orders handed over since the last tick are this tick's budget. The window is cleared on the tick that consumes it
		// rather than on the frame, so a peer cannot send more work than a tick can be asked to do.
		_commandsThisTick.Clear();

		if (nowSeconds >= _nextObstacleCheckTime)
		{
			_nextObstacleCheckTime = nowSeconds + ObstacleCheckIntervalSeconds;
			BroadcastNavMeshObstaclesWhenChanged();
		}

		int stepped = 0;
		while (nowSeconds >= _nextTickTime && stepped < MaximumTicksPerCall)
		{
			simulation.Step();
			PublishTick(simulation.AdvanceSnapshotSequence());
			_nextTickTime += ServerSimulation.FixedTickSeconds;
			stepped++;
		}

		if (nowSeconds >= _nextTickTime) _nextTickTime = nowSeconds + ServerSimulation.FixedTickSeconds;
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

		_seatedPeers.Clear();
	}

	public void Dispose()
	{
		if (_disposed) return;
		_disposed = true;

		Stop();
		_transport.Dispose();
		_simulation?.Dispose();
		_simulation = null;

		_predictionRecorder?.Dispose();
		_predictionRecorder = null;
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
		_log.Write($"peer {peerId} connected");
	}

	public void OnPeerDisconnected(int peerId, string reason)
	{
		bool wasSeated = _seatedPeers.Remove(peerId);
		_log.Write(wasSeated ? $"peer {peerId} left: {reason}" : $"peer {peerId} dropped before it was seated: {reason}");

		_simulation?.ForgetPeer(peerId);

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
	///     Reads one frame from one peer. The first frame a peer may send is its hello; after that it may send orders,
	///     acknowledgements, a request to be described from scratch, and a request to end the match.
	/// </summary>
	public void OnFrameReceived(int peerId, NetworkFrameKind kind, ReadOnlySpan<byte> payload)
	{
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
				HandleSnapshotAck(payload);
				break;
			case NetworkFrameKind.BaselineRequest:
				simulation.RequestBaseline(peerId);
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

		string rejection = DescribeHandshakeRejection(in hello);
		if (rejection.Length > 0)
		{
			_log.Write($"peer {peerId} refused: {rejection}");
			SendHello(peerId, rejection);
			_transport.Disconnect(peerId);
			return;
		}

		if (!simulation.TryAssignPlayerSlot(peerId, out int playerSlot, out int team))
		{
			const string noSlot = "server_rejected_no_free_slot";
			_log.Write($"peer {peerId} refused: every player slot this map declares is taken");
			SendHello(peerId, noSlot);
			_transport.Disconnect(peerId);
			return;
		}

		_seatedPeers[peerId] = playerSlot;
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

	private void SendHello(int peerId, string rejection)
	{
		var response = new ServerHello
		{
			Protocol = RealmProtocol.Version,
			Build = RealmVersion.GameBuildNumber,
			Accepted = rejection.Length == 0,
			Rejection = rejection
		};

		_transport.Send(peerId, NetworkFrameKind.ServerHello, MemoryPackSerializer.Serialize(response));
	}

	/// <summary>
	///     What a peer is told when it tries to join a match that was started with cheats. Such a match is played by
	///     one person against the computer, so it takes one peer and nobody else is offered a seat.
	/// </summary>
	private const string CheatsMatchRejection = "server_rejected_cheats_match_is_local_only";

	private string DescribeHandshakeRejection(in ClientHello hello)
	{
		if (hello.Protocol != RealmProtocol.Version) return "server_rejected_protocol";
		if (!string.Equals(hello.Build, RealmVersion.GameBuildNumber, StringComparison.Ordinal)) return "server_rejected_build";
		if (!string.Equals(hello.WireTableHash, ReplicatedComponents.ComputeWireTableHash(), StringComparison.Ordinal)) return "server_rejected_wire_table";
		if (!TokenMatches(hello.Token)) return "server_rejected_token";
		return string.Empty;
	}

	/// <summary>
	///     Whether a peer presented the ticket this match was started with. A match started without one is open, which
	///     is what a dedicated authority needs: the players it accepts never saw the command line it was started with.
	/// </summary>
	private bool TokenMatches(string? presentedToken)
	{
		if (_arguments.Token.Length == 0) return true;
		if (presentedToken == null) return false;

		byte[] presented = Encoding.UTF8.GetBytes(presentedToken);
		byte[] expected = Encoding.UTF8.GetBytes(_arguments.Token);
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

		// The account of this sequence is taken whether or not anyone is listening: a run measures prediction against the
		// sequences a screen applied, and a gap in the record would hide the very ticks a peer was still catching up on.
		_predictionRecorder?.Record(sequence);

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

		int[] peerIds = new int[_seatedPeers.Count];
		_seatedPeers.Keys.CopyTo(peerIds, 0);

		for (int index = 0; index < peerIds.Length; index++)
		{
			SendSnapshot(peerIds[index], sequence, simulation.ConsumeBaselineRequest(peerIds[index]));
		}

		_syncRecorder?.EndSequence();

		if (presentation.Length > 0) SendPresentation(peerIds, presentation);
	}

	/// <summary>
	///     Tells each peer what the map asked its screen to show. A request addressed to one player reaches only the
	///     peer acting as that player; one addressed to everyone reaches all of them.
	/// </summary>
	private void SendPresentation(int[] peerIds, ServerPresentationEvent[] events)
	{
		for (int peerIndex = 0; peerIndex < peerIds.Length; peerIndex++)
		{
			int peerId = peerIds[peerIndex];
			bool seated = _seatedPeers.TryGetValue(peerId, out int playerSlot);

			for (int eventIndex = 0; eventIndex < events.Length; eventIndex++)
			{
				ServerPresentationEvent request = events[eventIndex];
				if (request.PlayerIndex != ServerPresentationEvent.Everyone && (!seated || request.PlayerIndex != playerSlot)) continue;

				_transport.Send(peerId, NetworkFrameKind.Presentation, MemoryPackSerializer.Serialize(request.ToMessage()));
				_deliveredPresentationCount++;
			}
		}
	}

	private void SendSnapshot(int peerId, int sequence, bool forceBaseline)
	{
		ServerSimulation? simulation = _simulation;
		if (simulation == null) return;

		WorldSnapshot snapshot = simulation.BuildSnapshotForPeer(peerId, sequence, forceBaseline);
		_syncRecorder?.AddPeerSnapshot(peerId, in snapshot);
		byte[] serialized = MemoryPackSerializer.Serialize(snapshot);
		byte[] framed = snapshot.IsBaseline ? SnapshotWireFormat.EncodeBaseline(serialized) : SnapshotWireFormat.EncodeDelta(serialized);
		_transport.Send(peerId, NetworkFrameKind.Snapshot, framed);

		if (!snapshot.IsBaseline || !_seatedPeers.TryGetValue(peerId, out int playerSlot)) return;

		// A baseline says what the world holds now, and the map declared its cards, its colours and its labels while
		// it was setting that world up, long before this peer could hear any of it. They follow the snapshot that
		// introduces what they describe.
		ServerPresentationEvent[] description = simulation.DescribePresentationStateFor(playerSlot);
		if (description.Length > 0) SendPresentation([peerId], description);
	}

	/// <summary>
	///     Records how far a peer has applied the world. The stream is reliable and ordered, so this is not what makes a
	///     peer catch up; it is what tells the match log whether a peer is keeping pace with the ticks it is sent.
	/// </summary>
	private void HandleSnapshotAck(ReadOnlySpan<byte> payload)
	{
		SnapshotAck ack;
		try
		{
			ack = MemoryPackSerializer.Deserialize<SnapshotAck>(payload.ToArray());
		}
		catch (Exception exception) when (exception is IOException or InvalidDataException or ArgumentException)
		{
			_log.Write($"a snapshot acknowledgement could not be read: {exception.Message}");
			return;
		}

		if (ack.Sequence - _lastLoggedAckSequence < AckReportInterval) return;

		_lastLoggedAckSequence = ack.Sequence;
		_log.Write($"peer acknowledged sequence {ack.Sequence} with mask {ack.AppliedMask:x16}");
	}

	private const long AckReportInterval = 100;

	/// <summary>
	///     Answers one order. The authority validates and applies it, and the peer is told whether it was taken, which
	///     is what lets a predicting client keep or roll back what it already showed.
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

		_lastBroadcastObstacleSignature = signature;
		_transport.Send(peerId, NetworkFrameKind.NavMeshObstacles, MemoryPackSerializer.Serialize(volumes));
		_log.Write($"peer {peerId} given the {count} obstacle volumes this match pathing was baked with");
	}

	/// <summary>
	///     Tells every seated peer the volumes the match now paths with, once and only when they have changed since the
	///     last set was sent. A script that spawns a blocker after the match started changes them, and a peer that kept
	///     the earlier set would predict a route through it.
	/// </summary>
	private void BroadcastNavMeshObstaclesWhenChanged()
	{
		if (_seatedPeers.Count == 0) return;
		if (!TryBuildObstacleVolumes(out NavMeshObstacleVolumes volumes, out int signature, out int count)) return;
		if (signature == _lastBroadcastObstacleSignature) return;

		_lastBroadcastObstacleSignature = signature;
		byte[] payload = MemoryPackSerializer.Serialize(volumes);

		int[] peerIds = new int[_seatedPeers.Count];
		_seatedPeers.Keys.CopyTo(peerIds, 0);
		for (int index = 0; index < peerIds.Length; index++) _transport.Send(peerIds[index], NetworkFrameKind.NavMeshObstacles, payload);

		_log.Write($"the match now paths with {count} obstacle volumes; every peer was given the new set");
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
