using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Godot;
using MemoryPack;
using Realm.Client.Network;
using Realm.Client.Testing;
using Realm.Ecs.Replication;
using Realm.Shared;
using Realm.Shared.Maps;
using Realm.Shared.Network;

namespace Realm.Client.Services;

/// <summary>
///     What a client needs in order to have a match run for it: which map, the seed the match's scripts draw from, where
///     the authority writes its own log, where a map's saved data belongs, and the language this player reads in.
/// </summary>
/// <param name="OpenToOthers">
///     Whether peers other than the client that started it may join. A match this machine plays by itself is private to
///     it, because the ticket the authority was started with is one only its starter saw; a match whose port other
///     players are told about is started without one.
/// </param>
/// <param name="WasmDebug">
///     Whether the authority is started with the map script's debug information kept. A map tested from the editor asks for
///     it so its breakpoints reach the process the script runs in; every other match runs the script compiled for speed.
/// </param>
/// <param name="Roster">
///     The players the match is played by, as the lobby listed them. A map's script spawns each player's starting units
///     while it initializes, which is before the authority is listening, so the seats travel with the launch rather than
///     arriving with the peers that fill them.
/// </param>
/// <param name="AuthorityRecordDirectory">
///     The folder the authority writes what it knows about every unit on every published sequence into, or nothing for a
///     match that does not have to answer a synchronisation report. What a screen presents is measured against the
///     process that decided the movement, and no screen can speak for it.
/// </param>
public record ServerMatchStart(string MapFolderPath, string MapName, int Seed, string LogDirectory, string DataFolderPath, string Language, bool Cheats = false, bool OpenToOthers = false, bool WasmDebug = false, IReadOnlyList<MatchRosterSeat>? Roster = null, string? AuthorityRecordDirectory = null);

/// <summary>
///     What a lobby needs in order to have an authority: the map it will play, where the authority logs and keeps a map's
///     saved data, the language this player reads in, and how many players the map seats. The authority is started when
///     the lobby is created and binds the session's one public port, so the peers that join the lobby never reconnect.
/// </summary>
public record ServerLobbyStart(string MapFolderPath, string MapName, int MaxPlayers, string LogDirectory, string DataFolderPath, string Language, bool WasmDebug = false);

/// <summary>
///     What became of an attempt to start the authority a session belongs to. It reports the files the authority writes
///     even when the start failed, because a start that failed is one whose reason is in those files and a screen has
///     nowhere else to read it from.
/// </summary>
/// <param name="Started">Whether the authority is running and this session is reading from it.</param>
/// <param name="Failure">Why there is no authority, or nothing when there is one.</param>
/// <param name="LogPath">The log the authority was asked to write, whether or not it got far enough to write it.</param>
/// <param name="ConsoleLogPath">Where what the authority printed on its console was kept.</param>
public readonly record struct MatchStartResult(bool Started, string Failure, string LogPath, string ConsoleLogPath)
{
	/// <summary>A start that put the authority up.</summary>
	public static MatchStartResult Success(string logPath, string consoleLogPath) => new(true, string.Empty, logPath, consoleLogPath);

	/// <summary>A start that did not, with what the authority was going to write so a reader can find out why.</summary>
	public static MatchStartResult Failed(string failure, string logPath) => new(false, failure, logPath, logPath.Length == 0 ? string.Empty : ServerConsoleCapture.ResolvePath(logPath));
}

/// <summary>
///     One client's match. A client never simulates a match: it starts the authority beside it, joins it as an ordinary
///     ENet peer, sends its orders and is told what the world is. Everything a match produces arrives here — the world
///     as this player can see it, what the map asked a screen to show, and whether the authority took each order — and
///     nothing about the match is shared in memory with the process that runs it.
/// </summary>
public class ClientServerSession : IDisposable
{
	private readonly ClientNetworkChannel _channel;

	/// <summary>
	///     What the authority has said since the last time the match read it, in the order it arrived. A match reads this
	///     every frame, so it is only ever as long as the frames one frame's worth of reading missed - except while the
	///     screen is building the world it is about to play, where nothing reads it at all.
	/// </summary>
	private readonly List<(NetworkFrameKind Kind, byte[] Payload)> _incoming = new();
	private int _incomingIndex;
	private bool _descriptionsWereDropped;

	private ServerProcessLauncher? _launcher;
	private string _language = string.Empty;
	private string _token = string.Empty;

	/// <summary>
	///     The ticket this client proved to administer a lobby it started. It never travels: what the authority receives is
	///     an HMAC of it over the challenge it handed this connection, and the capability it grants is spent when the hello
	///     is answered.
	/// </summary>
	private string _adminToken = string.Empty;

	/// <summary>The name this player is seated under in a lobby.</summary>
	private string _playerName = string.Empty;

	/// <summary>The identity this connection presents so a lobby seat it dropped out of is handed back to it.</summary>
	private string _sessionToken = string.Empty;

	/// <summary>
	///     The map this peer is playing, as the hash of the authored files it holds, or nothing when it cannot state one.
	///     A match this client launched states the hash the authority computed over the folder it was told to load, so a
	///     map edited between that launch and this handshake is refused instead of played.
	/// </summary>
	private string _mapHash = string.Empty;
	private bool _helloSent;

	/// <summary>The challenge the authority handed this connection, which the hello answers.</summary>
	private string _challenge = string.Empty;
	private bool _joined;
	private int _joinedPort;
	private string _address = string.Empty;
	private int _port;
	private bool _connectedBefore;
	private int _reconnectAttempts;
	private long _lastReconnectAttemptMilliseconds;
	private bool _disposed;

	/// <summary>What the authority said when it stopped, or nothing while it is still there.</summary>
	public string? TerminationReport { get; private set; }

	private const int ReconnectIntervalMilliseconds = 250;
	private const int MaximumReconnectAttempts = 240;

	/// <summary>
	///     How many frames may wait for the match to read them. A match drains this every frame, so a queue this deep is a
	///     screen that is not reading - a world still being built, or a main thread that stopped - and the stream behind
	///     it is thirty descriptions a second of a world that has moved on. Keeping the newest of them is what matters:
	///     the oldest is a moment this screen has already passed, and remembering it costs the memory the queue is bounded
	///     against.
	/// </summary>
	private const int MaximumQueuedFrames = 256;

	/// <summary>Whether this client is still connected to a living authority.</summary>
	public bool IsRunning => TerminationReport == null && (_launcher == null ? _joined : _launcher.IsRunning);

	/// <summary>The identity the authority gave this connection, or zero before it has been seated.</summary>
	public int LocalPeerId { get; private set; }

	/// <summary>The player slot this client acts as, or nothing before the authority has seated it.</summary>
	public int LocalPlayerSlot { get; private set; } = -1;

	/// <summary>Whether the authority granted this connection the capability that administers its lobby.</summary>
	public bool IsAdmin { get; private set; }

	/// <summary>Whether this connection was admitted to a lobby rather than seated in a running match.</summary>
	public bool InLobby { get; private set; }

	/// <summary>The team the player slot belongs to, which decides who this client may order its units against.</summary>
	public int LocalTeam { get; private set; }

	/// <summary>The log the authority writes for itself, so a match can show where to look when something goes wrong.</summary>
	public string LogPath { get; private set; } = string.Empty;

	/// <summary>Where the authority's own console output was kept.</summary>
	public string ConsoleLogPath { get; private set; } = string.Empty;

	/// <summary>The port the authority is listening on.</summary>
	public int Port => _launcher?.Port ?? _joinedPort;

	/// <summary>
	///     The process running the match this client started, or zero for a match joined remotely. It is the process the map
	///     script runs inside, so it is the one a debugger attaches to.
	/// </summary>
	public int AuthorityProcessId => _launcher?.ProcessId ?? 0;

	/// <summary>The wire table hash the two processes agreed on, so a mismatch is refused rather than played through.</summary>
	public string WireTableHash => _launcher?.WireTableHash ?? string.Empty;

	/// <summary>The authority seated this connection as a player.</summary>
	public event Action<PeerRosterAssignment>? Seated;

	/// <summary>The world as this player can see it, as the authority described it.</summary>
	public event Action<WorldSnapshot>? SnapshotReceived;

	/// <summary>Something the map asked a screen to show.</summary>
	public event Action<PresentationMessage>? PresentationReceived;

	/// <summary>
	///     The obstacle volumes the authority baked its navigation mesh with, sent once this peer is seated so the mesh
	///     baked here agrees with the one the match paths units through.
	/// </summary>
	public event Action<NavMeshObstacleVolumes>? NavMeshObstaclesReceived;

	/// <summary>
	///     The newest obstacle volumes the authority sent, kept so a screen that starts listening after they arrived still
	///     bakes the mesh the match paths with. A peer is seated before its screen exists, so the first set always arrives
	///     earlier than anything waiting for it.
	/// </summary>
	public NavMeshObstacleVolumes? LatestNavMeshObstacles { get; private set; }

	/// <summary>The pause state the match holds, which is what this screen shows.</summary>
	public event Action<MatchPauseState>? PauseStateReceived;

	/// <summary>The newest pause state the authority sent, for the same reason as <see cref="LatestNavMeshObstacles" />.</summary>
	public MatchPauseState? LatestPauseState { get; private set; }

	/// <summary>A line of chat the match relayed, as the player who sent it is named there.</summary>
	public event Action<ChatLine>? ChatReceived;

	/// <summary>Whether the authority took one of this client's orders, and why not when it did not.</summary>
	public event Action<CommandOutcome>? CommandAnswered;

	/// <summary>One of this client's probes came back, which is what the round trip is measured from.</summary>
	public event Action<int>? LatencyProbeAnswered;

	/// <summary>The authority ended in the middle of a match, whether or not it meant to.</summary>
	public event Action<string>? ServerLost;

	/// <summary>The authority answered this connection's hello, and said whether it was admitted and whether it administers the lobby.</summary>
	public event Action<ServerHello>? HelloAnswered;

	/// <summary>The lobby as the authority holds it, sent whole after every change.</summary>
	public event Action<LobbyStateMessage>? LobbyStateReceived;

	/// <summary>A line of lobby chat the authority relayed.</summary>
	public event Action<LobbyChatMessage>? LobbyChatReceived;

	/// <summary>The lobby started its match, and this connection is one of its seats.</summary>
	public event Action<LobbyMatchStartedMessage>? LobbyMatchStartedEvent;

	/// <summary>This connection was removed from the lobby, and is being disconnected because of it.</summary>
	public event Action<LobbyKickedMessage>? LobbyKickedEvent;

	/// <summary>The authority answered a request for the lobby's map.</summary>
	public event Action<MapTransferReply>? MapTransferReplied;

	public ClientServerSession(ClientNetworkChannel channel)
	{
		_channel = channel;
		_channel.FrameArrived += Enqueue;
	}

	/// <summary>How many of the authority's last lines a failed start reports beside the reason it stopped.</summary>
	private const int FailedLaunchDetailLineCount = 8;

	/// <summary>
	///     Says why a launch that never reported listening failed, in the words of the process that knows. The authority is
	///     a separate process, so the only place it says that a map's script could not be loaded is its own console; a
	///     player handed two log paths instead of a reason has nothing to act on.
	/// </summary>
	private static MatchStartResult FailedLaunch(ServerLaunchAttempt attempt, string logPath)
	{
		string detail = ServerConsoleCapture.ReadLastLines(logPath, FailedLaunchDetailLineCount);
		return MatchStartResult.Failed(detail.Length == 0 ? attempt.Failure : $"{attempt.Failure}\n\n{detail}", logPath);
	}

	/// <summary>
	///     Starts the authority for this match and joins it. The token is minted here so an authority this client
	///     started is the only one that accepts it, and the map's name decides where its progress is kept.
	///     The load of a match happens on the authority, which takes as long as a map takes to load; this completes when
	///     that process reports it is listening, so the thread that awaits it keeps drawing its own screen. Waiting for it
	///     on that thread froze the editor for as long as the match took to load, and for the whole readiness timeout when
	///     it never became ready at all.
	/// </summary>
	/// <param name="start">What the match is, as this client knows it.</param>
	/// <param name="authorityPort">
	///     The port to listen on, or zero to let the authority pick one. A match this machine plays by itself is reached on
	///     whatever port the authority reports, while a match other machines are told to join has to be listening on the
	///     port they were told.
	/// </param>
	public async Task<MatchStartResult> TryStartAsync(ServerMatchStart start, int authorityPort = 0)
	{
		if (!ListenServerTarget.TryResolve(out ServerLaunchTarget? target, out string failure) || target == null) return MatchStartResult.Failed(failure, string.Empty);

		// The log is named after the folder the map lives in, never after the name the caller knows it by: a match started
		// for an unsaved map is named after a Godot path like 'user://temp_map_workspace', which is not a file name.
		string logName = Path.GetFileName(Path.TrimEndingDirectorySeparator(start.MapFolderPath));
		if (string.IsNullOrEmpty(logName)) logName = "map";
		string logPath = Path.Combine(start.LogDirectory, $"{logName}.listen_server.log");
		try
		{
			Directory.CreateDirectory(start.LogDirectory);
		}
		catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
		{
			return MatchStartResult.Failed($"the match log folder '{start.LogDirectory}' could not be created: {exception.Message}", logPath);
		}

		_token = start.OpenToOthers ? string.Empty : RealmProtocol.CreateToken();
		// A map being tested is played from the workspace it was compiled from, so the authority exports the script's JIT ELF
		// into that same workspace and the debugger configuration it already carries finds the module that is running.
		// The seats the lobby arranged travel beside the log, because the authority builds the world and runs the map's
		// script before it is listening: a roster that arrived with the peers would arrive after every starting unit was
		// already spawned for somebody else.
		string? rosterPath = null;
		if (start.Roster is { Count: > 0 })
		{
			rosterPath = Path.Combine(start.LogDirectory, $"{logName}{MatchRosterFile.FileSuffix}");
			MatchRosterFile.Write(rosterPath, start.Roster);
		}

		var request = new ServerLaunchRequest(target, start.MapFolderPath, logPath, _token, start.Seed)
		{
			DataFolderPath = start.DataFolderPath,
			Cheats = start.Cheats,
			WasmDebug = start.WasmDebug,
			MapWorkspacePath = start.WasmDebug ? start.MapFolderPath : null,
			RosterPath = rosterPath,
			AuthorityRecordFolderPath = start.AuthorityRecordDirectory,
			Port = authorityPort
		};

		ServerLaunchAttempt attempt = await ServerProcessLauncher.TryLaunchAsync(request);
		if (!attempt.Started || attempt.Launcher == null) return FailedLaunch(attempt, logPath);

		ServerProcessLauncher launcher = attempt.Launcher;
		if (!_channel.TryConnect("127.0.0.1", launcher.Port, out failure))
		{
			launcher.Dispose();
			return MatchStartResult.Failed(failure, logPath);
		}

		_launcher = launcher;
		_language = start.Language;
		_mapHash = launcher.MapHash;
		_address = "127.0.0.1";
		_port = launcher.Port;
		LogPath = launcher.LogPath;
		ConsoleLogPath = launcher.ConsoleLogPath;
		return MatchStartResult.Success(LogPath, ConsoleLogPath);
	}

	/// <summary>
	///     Joins an authority this client did not start, which is what every player of a match someone else runs does.
	///     Nothing here assumes the authority is on this machine: the same call reaches a match on the loopback address and
	///     one on another computer, and this client presents no ticket because it never saw the command line the authority
	///     was started with.
	/// </summary>
	/// <param name="address">The address the authority is listening on.</param>
	/// <param name="port">The port the authority is listening on.</param>
	/// <param name="language">The language this player reads in, so the authority answers in it.</param>
	/// <param name="failure">What stopped the connection from being made, or nothing when it was made.</param>
	/// <returns>True once the connection to the authority is up and this session is reading from it.</returns>
	/// <param name="mapFolder">
	///     The folder this client holds the match's map in, or nothing when it cannot name one. A folder that is named is
	///     hashed and stated in the handshake, so an authority running a different build of the same map refuses this
	///     connection rather than describing a world this peer would draw wrong.
	/// </param>
	public bool TryJoin(string address, int port, string language, string? mapFolder, out string failure)
	{
		failure = string.Empty;

		if (!string.IsNullOrEmpty(mapFolder) && Directory.Exists(mapFolder))
		{
			_mapHash = MapContentHash.Compute(mapFolder);
		}

		if (!_channel.TryConnect(address, port, out failure)) return false;

		_language = language;
		_joined = true;
		_joinedPort = port;
		_address = address;
		_port = port;
		return true;
	}

	/// <summary>
	///     Sets who this connection is in the lobby it is about to join. A name is what the seat is shown under and a
	///     session identity is what a seat this connection dropped out of is handed back by; both are read when the hello
	///     is sent, so they are set before the authority is reached or before the reconnect is attempted.
	/// </summary>
	public void ConfigureLobbyIdentity(string playerName, string sessionToken)
	{
		_playerName = playerName ?? string.Empty;
		_sessionToken = sessionToken ?? string.Empty;
	}

	/// <summary>
	///     Starts the authority that holds a lobby and joins it as the seat that administers it. The authority is started
	///     for the lobby's map but builds no world: the map is loaded when the lobby's countdown ends, against the seats the
	///     lobby arranged. The admin ticket is minted here, so only this client can prove the capability the authority was
	///     spawned with; the loopback connection itself grants nothing.
	/// </summary>
	/// <param name="start">The lobby's map and where its authority writes.</param>
	/// <param name="lobbyPort">The port the lobby is advertised on, or zero to let the authority pick one.</param>
	public async Task<MatchStartResult> TryStartLobbyAsync(ServerLobbyStart start, int lobbyPort = 0)
	{
		if (!ListenServerTarget.TryResolve(out ServerLaunchTarget? target, out string failure) || target == null) return MatchStartResult.Failed(failure, string.Empty);

		string logName = Path.GetFileName(Path.TrimEndingDirectorySeparator(start.MapFolderPath));
		if (string.IsNullOrEmpty(logName)) logName = "map";
		string logPath = Path.Combine(start.LogDirectory, $"{logName}.listen_server.log");
		try
		{
			Directory.CreateDirectory(start.LogDirectory);
		}
		catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
		{
			return MatchStartResult.Failed($"the lobby log folder '{start.LogDirectory}' could not be created: {exception.Message}", logPath);
		}

		_token = string.Empty;
		_adminToken = RealmProtocol.CreateToken();

		var request = new ServerLaunchRequest(target, start.MapFolderPath, logPath, string.Empty, 0)
		{
			DataFolderPath = start.DataFolderPath,
			WasmDebug = start.WasmDebug,
			MapWorkspacePath = start.WasmDebug ? start.MapFolderPath : null,
			Port = lobbyPort,
			Lobby = true,
			AdminToken = _adminToken,
			MapName = start.MapName,
			MaxPlayers = start.MaxPlayers
		};

		ServerLaunchAttempt attempt = await ServerProcessLauncher.TryLaunchAsync(request);
		if (!attempt.Started || attempt.Launcher == null) return FailedLaunch(attempt, logPath);

		ServerProcessLauncher launcher = attempt.Launcher;
		if (!_channel.TryConnect("127.0.0.1", launcher.Port, 0, out failure))
		{
			launcher.Dispose();
			return MatchStartResult.Failed(failure, logPath);
		}

		_launcher = launcher;
		_language = start.Language;
		_mapHash = launcher.MapHash;
		_address = "127.0.0.1";
		_port = launcher.Port;
		LogPath = launcher.LogPath;
		ConsoleLogPath = launcher.ConsoleLogPath;
		return MatchStartResult.Success(LogPath, ConsoleLogPath);
	}

	/// <summary>
	///     Joins a lobby an authority already holds. The connection made here is the connection the match is played over:
	///     the lobby and the match are the same session, so a player who has been in the lobby does not reconnect when the
	///     host presses Start.
	/// </summary>
	/// <param name="address">The address the authority listens on.</param>
	/// <param name="port">The port the authority listens on.</param>
	/// <param name="language">The language this player reads in.</param>
	/// <param name="localPort">The local port to send from, or zero to let the operating system pick one.</param>
	/// <param name="failure">What stopped the connection from being made, or nothing when it was made.</param>
	public bool TryJoinLobby(string address, int port, string language, int localPort, out string failure)
	{
		failure = string.Empty;

		// The map is downloaded while the lobby is open, so a peer joining a lobby states no map hash: it holds the map
		// the lobby distributes, not the folder the authority loaded, and the lobby is where that difference is settled.
		_mapHash = string.Empty;
		if (!_channel.TryConnect(address, port, localPort, out failure)) return false;

		_language = language;
		_joined = true;
		_joinedPort = port;
		_address = address;
		_port = port;
		return true;
	}

	/// <summary>Hands the authority one lobby command, which it validates before it changes the lobby it holds.</summary>
	public bool SendLobbyCommand(LobbyCommand command)
	{
		if (!IsRunning) return false;

		_channel.Send(NetworkFrameKind.LobbyCommand, MemoryPackSerializer.Serialize(command));
		return true;
	}

	/// <summary>Asks the authority for the lobby's map manifest or for one bundle of the files it lists.</summary>
	public bool RequestMapTransfer(MapTransferRequest request)
	{
		if (!IsRunning) return false;

		_channel.Send(NetworkFrameKind.MapTransferRequest, MemoryPackSerializer.Serialize(request));
		return true;
	}

	/// <summary>Hands the authority one of this client's orders, as the client authored it and already encoded.</summary>
	public bool Submit(byte[] payload)
	{
		if (!IsRunning || payload.Length == 0) return false;

		_channel.Send(NetworkFrameKind.Command, payload);
		return true;
	}

	/// <summary>Asks the authority to describe the world from scratch, which is what a client that lost its place does.</summary>
	public bool RequestBaseline()
	{
		if (!IsRunning) return false;

		_channel.Send(NetworkFrameKind.BaselineRequest, Array.Empty<byte>());
		return true;
	}

	/// <summary>Asks the authority to answer at once, so this client can measure the round trip it is playing over.</summary>
	/// <param name="sequence">Which probe this is, so a stale answer is not taken for a fresh one.</param>
	/// <returns>True once the probe was handed to the transport.</returns>
	public bool SendLatencyProbe(int sequence)
	{
		if (!IsRunning) return false;

		_channel.Send(NetworkFrameKind.LatencyProbe, MemoryPackSerializer.Serialize(new LatencyProbe(sequence)));
		return true;
	}

	/// <summary>
	///     Asks the match to pause or resume. Pausing is the match's decision, so this is a request rather than a
	///     change: the authority answers with the state it holds and this screen shows that.
	/// </summary>
	/// <returns>True once the request was handed to the transport.</returns>
	public bool RequestPause(bool paused)
	{
		if (!IsRunning) return false;

		_channel.Send(NetworkFrameKind.PauseRequest, MemoryPackSerializer.Serialize(new MatchPauseState { Paused = paused, RequestedByPlayer = -1 }));
		return true;
	}

	/// <summary>
	///     Hands the match a line this player typed into chat. A map's script reads the chat of the player it came from -
	///     that is how a map's own commands are typed - so the line is sent to the process that runs the script, which is
	///     also the only process that can show it to the other players.
	/// </summary>
	/// <returns>True once the line was handed to the transport.</returns>
	public bool SendChat(string message, bool alliesOnly)
	{
		if (!IsRunning) return false;
		if (!ChatLineLimits.IsAcceptable(message)) return false;

		var line = new ChatLine
		{
			Sender = string.Empty,
			Message = message.Trim(),
			AlliesOnly = alliesOnly,
			TimestampUtcTicks = DateTime.UtcNow.Ticks
		};

		_channel.Send(NetworkFrameKind.ChatLine, MemoryPackSerializer.Serialize(line));
		return true;
	}

	/// <summary>
	///     Hands the authority a line this player typed into the map script's console. A map's commands belong to the map,
	///     and the map runs on the authority, so the line is sent rather than interpreted here.
	/// </summary>
	public bool SubmitConsoleCommand(string command, int selectedUnitEntityId)
	{
		if (!IsRunning) return false;
		if (!ScriptConsoleCommandLimits.IsAcceptable(command)) return false;

		var payload = new ScriptConsoleCommand
		{
			Command = command.Trim(),
			SelectedUnitEntityId = selectedUnitEntityId > 0 ? selectedUnitEntityId : 0
		};

		_channel.Send(NetworkFrameKind.ConsoleCommand, MemoryPackSerializer.Serialize(payload));
		return true;
	}

	/// <summary>
	///     Tells the authority how far this client has applied the world, and which of the snapshots behind that one it
	///     applied whole. The mask is what lets the authority tell a peer that kept up from one that discarded part of
	///     a payload, so it travels with every acknowledgement rather than being dropped here. The round trip travels with
	///     it because the sequence named is that much older than the tick the answer is read on, and without it the match
	///     would count a slow link as a screen that stopped drawing.
	/// </summary>
	public bool Acknowledge(int sequence, ulong appliedMask, int roundTripMilliseconds)
	{
		if (sequence <= 0) return false;

		var ack = new SnapshotAck
		{
			Sequence = sequence,
			AppliedMask = appliedMask,
			RoundTripMilliseconds = roundTripMilliseconds > 0 ? roundTripMilliseconds : 0
		};
		_channel.Send(NetworkFrameKind.SnapshotAck, MemoryPackSerializer.Serialize(ack));
		return true;
	}

	/// <summary>
	///     Hands the match everything the authority said since the last time this was called, and says hello once the
	///     connection is up. A match calls this every frame: the transport is reliable and ordered, so what a client has
	///     to do is read what has arrived rather than poll for anything in particular.
	/// </summary>
	public bool Pump()
	{
		if (!IsRunning)
		{
			ReportLost(DescribeStop());
			return false;
		}

		if (_launcher == null && !_joined) return false;

		bool handed = false;

		if (_helloSent && _connectedBefore && !_channel.IsConnected)
		{
			_helloSent = false;
			_challenge = string.Empty;
		}

		if (!_helloSent && _channel.IsConnected)
		{
			// The authority hands every peer a challenge the moment it connects, and the hello is the answer to it, so
			// the hello waits for the challenge rather than the answer being guessable from the connection alone.
			if (_challenge.Length > 0)
			{
				SendHello();
				_helloSent = true;
				_connectedBefore = true;
				_reconnectAttempts = 0;
			}
		}
		else if (!_helloSent && !TryReconnect())
		{
			return false;
		}

		while (_incomingIndex < _incoming.Count)
		{
			(NetworkFrameKind kind, byte[] payload) = _incoming[_incomingIndex];
			_incomingIndex++;

			// One frame this build cannot read is not a match that cannot be played. It is dropped and the world is asked for
			// again from a baseline, because letting it throw out of the frame would abort every frame behind it in the same
			// queue and the tick the pump ran in, and the screen would sit on a world nothing advances.
			try
			{
				Dispatch(kind, payload);
			}
			catch (Exception exception) when (exception is IOException or InvalidDataException or ArgumentException or MemoryPackSerializationException)
			{
				GD.PushWarning($"[Network] A {kind} frame could not be read and was dropped: {exception.Message}");

				// A lobby frame is not a description of a world, so there is nothing to ask a baseline for. Asking anyway
				// would put a match frame on the lobby's connection, which the lobby answers by ending it.
				if (!InLobby) GameContext.SnapshotApply.RequestBaseline();
			}

			handed = true;
		}

		// What has been read is released here rather than by an index that walks a list growing for the length of a match.
		if (_incomingIndex >= _incoming.Count)
		{
			_incoming.Clear();
			_incomingIndex = 0;
		}

		// A description dropped before it could be read leaves a hole in the sequence this screen applies, and the frames
		// after it were written against a world this one never saw. The world is asked for again once the queue has been
		// drained, because asking before that would put a reliable baseline behind the stream that is already waiting.
		if (_descriptionsWereDropped)
		{
			_descriptionsWereDropped = false;
			if (!InLobby) GameContext.SnapshotApply.RequestBaseline();
		}

		return handed;
	}

	/// <summary>
	///     Closes this client's connection the way a lost authority would, so the path that rejoins a match is exercised
	///     without ending the match it is playing in.
	/// </summary>
	public void DropConnectionForHarness()
	{
		if (_address.Length == 0) return;

		_channel.Disconnect();
		_helloSent = false;
		_challenge = string.Empty;
	}

	/// <summary>
	///     Rejoins the authority after the connection ended. The world is described from scratch when the peer is seated
	///     again, so a client that lost a moment of the match catches up rather than sitting on a world nobody advances.
	/// </summary>
	/// <returns>False once reconnecting has been given up on, having reported the loss.</returns>
	private bool TryReconnect()
	{
		if (_address.Length == 0 || _port <= 0) return true;

		long now = System.Environment.TickCount64;
		if (_lastReconnectAttemptMilliseconds != 0 && now - _lastReconnectAttemptMilliseconds < ReconnectIntervalMilliseconds) return true;

		_lastReconnectAttemptMilliseconds = now;
		_reconnectAttempts++;
		if (_reconnectAttempts > MaximumReconnectAttempts)
		{
			ReportLost("the connection to the match could not be re-established");
			return false;
		}

		_channel.TryConnect(_address, _port, out _);
		return true;
	}

	/// <summary>Whether the authority already ended, without reading anything, so a match can report a loss at once.</summary>
	public bool HasServerEnded() => _launcher != null && !_launcher.IsRunning;

	/// <summary>The reason to report when the authority is no longer there.</summary>
	public string DescribeStop()
	{
		if (_launcher == null) return _joined ? "the connection to the match ended" : "the match was never started";

		return _launcher.IsRunning
			? "the connection to the match ended"
			: $"the process running this match ended with code {_launcher.ExitCode}; see '{LogPath}' and '{ConsoleLogPath}'";
	}

	/// <summary>
	///     Says who this client is. The ticket is the one the authority was started with, and what travels is the answer
	///     to the challenge that authority handed this connection rather than the ticket itself, so an authority left
	///     running by an earlier match refuses this one instead of seating it as a stranger, and a frame read off the wire
	///     is worth nothing on any other connection.
	/// </summary>
	private void SendHello()
	{
		var hello = new ClientHello
		{
			Protocol = RealmProtocol.Version,
			Build = RealmVersion.GameBuildNumber,
			TokenProof = _token.Length > 0 && _challenge.Length > 0 ? RealmProtocol.ProveToken(_token, _challenge) : string.Empty,
			AdminProof = _adminToken.Length > 0 && _challenge.Length > 0 ? RealmProtocol.ProveToken(_adminToken, _challenge) : string.Empty,
			WireTableHash = _launcher?.WireTableHash ?? ReplicatedComponents.ComputeWireTableHash(),
			MapHash = _launcher?.MapHash ?? _mapHash,
			Language = _language,
			PlayerName = _playerName,
			SessionToken = _sessionToken
		};

		_channel.Send(NetworkFrameKind.ClientHello, MemoryPackSerializer.Serialize(hello));
	}

	private void Enqueue(NetworkFrameKind kind, byte[] payload)
	{
		if (_incoming.Count >= MaximumQueuedFrames && !DropOldestDescription())
		{
			// Nothing waiting describes a world, so nothing waiting is restated by a later frame: the frame that has just
			// arrived is the one that is refused, and the world it carried is asked for again from a baseline.
			if (kind is NetworkFrameKind.Snapshot or NetworkFrameKind.Presentation) _descriptionsWereDropped = true;
			return;
		}

		_incoming.Add((kind, payload));
	}

	/// <summary>
	///     Makes room for one more frame by discarding the oldest one that describes a world. A frame the session has to act
	///     on - a hello, a roster, an answer to an order - is never the one dropped: what a full queue holds is a stream of
	///     descriptions, and the newest of them is the one worth keeping.
	/// </summary>
	private bool DropOldestDescription()
	{
		for (int index = _incomingIndex; index < _incoming.Count; index++)
		{
			if (_incoming[index].Kind is not (NetworkFrameKind.Snapshot or NetworkFrameKind.Presentation)) continue;

			_incoming.RemoveAt(index);
			_descriptionsWereDropped = true;
			return true;
		}

		return false;
	}

	private void Dispatch(NetworkFrameKind kind, byte[] payload)
	{
		switch (kind)
		{
			case NetworkFrameKind.HandshakeChallenge:
				_challenge = MemoryPackSerializer.Deserialize<HandshakeChallenge>(payload).Nonce ?? string.Empty;
				break;

			case NetworkFrameKind.ServerHello:
				ApplyHello(payload);
				break;

			case NetworkFrameKind.Roster:
				ApplyRoster(payload);
				break;

			case NetworkFrameKind.Snapshot:
				ApplySnapshot(payload);
				break;

			case NetworkFrameKind.Presentation:
				PresentationReceived?.Invoke(MemoryPackSerializer.Deserialize<PresentationMessage>(payload));
				break;

			case NetworkFrameKind.CommandOutcome:
				CommandAnswered?.Invoke(MemoryPackSerializer.Deserialize<CommandOutcome>(payload));
				break;

			case NetworkFrameKind.LatencyProbeEcho:
				LatencyProbeAnswered?.Invoke(MemoryPackSerializer.Deserialize<LatencyProbe>(payload).Sequence);
				break;

			case NetworkFrameKind.ChatLine:
				ChatReceived?.Invoke(MemoryPackSerializer.Deserialize<ChatLine>(payload));
				break;

			case NetworkFrameKind.NavMeshObstacles:
			{
				NavMeshObstacleVolumes volumes = MemoryPackSerializer.Deserialize<NavMeshObstacleVolumes>(payload);
				LatestNavMeshObstacles = volumes;
				NavMeshObstaclesReceived?.Invoke(volumes);
				break;
			}

			case NetworkFrameKind.MatchPaused:
			{
				MatchPauseState state = MemoryPackSerializer.Deserialize<MatchPauseState>(payload);
				LatestPauseState = state;
				PauseStateReceived?.Invoke(state);
				break;
			}

			case NetworkFrameKind.LobbyState:
				LobbyStateReceived?.Invoke(MemoryPackSerializer.Deserialize<LobbyStateMessage>(payload));
				break;

			case NetworkFrameKind.LobbyChat:
				LobbyChatReceived?.Invoke(MemoryPackSerializer.Deserialize<LobbyChatMessage>(payload));
				break;

			case NetworkFrameKind.LobbyMatchStarted:
			{
				LobbyMatchStartedMessage started = MemoryPackSerializer.Deserialize<LobbyMatchStartedMessage>(payload);
				InLobby = false;
				LobbyMatchStartedEvent?.Invoke(started);
				break;
			}

			case NetworkFrameKind.LobbyKicked:
				LobbyKickedEvent?.Invoke(MemoryPackSerializer.Deserialize<LobbyKickedMessage>(payload));
				break;

			case NetworkFrameKind.MapTransferReply:
				MapTransferReplied?.Invoke(MemoryPackSerializer.Deserialize<MapTransferReply>(payload));
				break;

			case NetworkFrameKind.Shutdown:
				ReportLost("the authority ended the match");
				break;
		}
	}

	private void ApplyHello(byte[] payload)
	{
		ServerHello response = MemoryPackSerializer.Deserialize<ServerHello>(payload);
		if (response.PeerId > 0) LocalPeerId = response.PeerId;
		IsAdmin = response.IsAdmin;
		InLobby = response.InLobby;
		HelloAnswered?.Invoke(response);

		if (!response.Accepted)
		{
			ReportLost($"the authority refused the connection: {response.Rejection}");
			return;
		}

		// The authority states the map it is running, and a peer that holds one of its own refuses to play a world it was
		// not built for rather than showing units walk through geometry this build does not have. A peer that could not
		// state a hash has nothing to compare, and the protocol and wire table remain the whole of what it agreed to.
		if (_mapHash.Length > 0 && response.MapHash.Length > 0 && !string.Equals(_mapHash, response.MapHash, StringComparison.Ordinal))
		{
			ReportLost("the authority is running a different build of this map");
		}
	}

	private void ApplyRoster(byte[] payload)
	{
		PeerRosterAssignment assignment = MemoryPackSerializer.Deserialize<PeerRosterAssignment>(payload);
		LocalPeerId = assignment.PeerId;
		LocalPlayerSlot = assignment.PlayerSlot;
		LocalTeam = assignment.Team;
		Seated?.Invoke(assignment);
	}

	private void ApplySnapshot(byte[] payload)
	{
		WorldSnapshot snapshot = MemoryPackSerializer.Deserialize<WorldSnapshot>(SnapshotWireFormat.Decode(payload));

		// What a peer's link carries is measured here rather than in the authority, because the authority builds these
		// frames in another process; a run that asks for the figure is the run that reads this counter.
		if (MultiplayerBandwidthMeter.IsRecording) MultiplayerBandwidthMeter.RecordReceivedSnapshot(payload.Length, snapshot.IsBaseline);

		SnapshotReceived?.Invoke(snapshot);
	}

	private void ReportLost(string report)
	{
		if (TerminationReport != null) return;

		TerminationReport = report;
		ServerLost?.Invoke(report);
	}

	/// <summary>
	///     Leaves the match: the authority is told the match is over, and it is ended if it does not leave on its own. A
	///     client that exits without this leaves a process behind, so this is what a match calls on its way out.
	/// </summary>
	public void Stop()
	{
		ServerProcessLauncher? launcher = _launcher;
		if (launcher == null && !_joined) return;

		bool startedHere = launcher != null;
		_launcher = null;
		_joined = false;
		_mapHash = string.Empty;
		_address = string.Empty;
		_port = 0;
		_channel.FrameArrived -= Enqueue;

		// What arrived for a match this session no longer belongs to is a world something else will be told about.
		_incoming.Clear();
		_incomingIndex = 0;
		_descriptionsWereDropped = false;

		// A player leaving a match someone else runs does not end it for everyone, so only the authority this client
		// started is asked to stop.
		if (startedHere) _channel.Send(NetworkFrameKind.Shutdown, Array.Empty<byte>());
		_channel.Disconnect();
		launcher?.Dispose();
	}

	public void Dispose()
	{
		if (_disposed) return;
		_disposed = true;

		Stop();
	}
}
