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

public record ServerMatchStart(string MapFolderPath, string MapName, int Seed, string LogDirectory, string DataFolderPath, string Language, bool Cheats = false, bool OpenToOthers = false, bool WasmDebug = false, IReadOnlyList<MatchRosterSeat>? Roster = null, string? AuthorityRecordDirectory = null);

public record ServerLobbyStart(string MapFolderPath, string MapName, int MaxPlayers, string LogDirectory, string DataFolderPath, string Language, bool WasmDebug = false, bool Cheats = false, bool LoopbackOnly = false);

public readonly record struct MatchStartResult(bool Started, string Failure, string LogPath, string ConsoleLogPath)
{
	public static MatchStartResult Success(string logPath, string consoleLogPath) => new(true, string.Empty, logPath, consoleLogPath);

	public static MatchStartResult Failed(string failure, string logPath) => new(false, failure, logPath, logPath.Length == 0 ? string.Empty : ServerConsoleCapture.ResolvePath(logPath));
}

public class ClientServerSession : IDisposable
{
	private readonly ClientNetworkChannel _channel;

	private readonly SnapshotReassembler _snapshotReassembler = new();

	private readonly SnapshotDecompressor _snapshotDecompressor = new();

	private byte[] _snapshotBuffer = new byte[SnapshotWireFormat.FrameHeaderBytes];

	private readonly SessionFrameQueue _frames = new();

	private readonly struct QueuedFrame
	{
		public QueuedFrame(NetworkFrameKind kind, byte[] payload, ulong arrivalMilliseconds)
		{
			Kind = kind;
			Payload = payload;
			ArrivalMilliseconds = arrivalMilliseconds;
		}

		public NetworkFrameKind Kind { get; }
		public byte[] Payload { get; }

		public ulong ArrivalMilliseconds { get; }
	}

	/// <summary>
	///     Whether the match this session is talking to was put on the authority this screen owns. It is the difference
	///     between a path that has to be masked and one that does not: a loopback match has no transit to absorb, so
	///     charging it the latency a measurement reports would delay the host's own units behind its own clock for nothing.
	/// </summary>
	private bool _local;
	private int _localPort;
	private string _localWireTableHash = string.Empty;

	private string _language = string.Empty;
	private string _token = string.Empty;

	private string _adminToken = string.Empty;

	private string _playerName = string.Empty;

	private long _accountId;

	private string _sessionToken = string.Empty;

	private string _mapHash = string.Empty;
	private bool _helloSent;
	private bool _started;

	private string _challenge = string.Empty;
	private bool _joined;
	private int _joinedPort;
	private string _address = string.Empty;
	private int _port;
	private bool _connectedBefore;
	private int _reconnectAttempts;
	private long _lastReconnectAttemptMilliseconds;
	private bool _disposed;

	public string? TerminationReport { get; private set; }

	private const int ReconnectIntervalMilliseconds = 250;
	private const int MaximumReconnectAttempts = 240;

	/// <summary>
	///     How many descriptions one tick may apply. A tick that spent all of itself draining a backlog applied state the
	///     screen was about to be told again anyway, and everything after the first of them was older than the world it left
	///     behind; what is left keeps waiting for the next tick.
	/// </summary>
	private const int MaximumSnapshotsAppliedPerPump = 3;

	public bool IsRunning => TerminationReport == null && (_local ? GameContext.LocalAuthority.IsRunning : _joined);

	public int LocalPeerId { get; private set; }

	public int LocalPlayerSlot { get; private set; } = -1;

	public bool IsAdmin { get; private set; }

	public bool InLobby { get; private set; }

	public int LocalTeam { get; private set; }

	public string LogPath { get; private set; } = string.Empty;

	public string ConsoleLogPath { get; private set; } = string.Empty;

	public int Port => _local ? _localPort : _joinedPort;

	public int AuthorityProcessId => _local ? GameContext.LocalAuthority.ProcessId : 0;

	public bool IsLocalAuthority => _local;

	public string WireTableHash => _local ? _localWireTableHash : string.Empty;

	public event Action<PeerRosterAssignment>? Seated;

	public event Action<WorldSnapshot>? SnapshotReceived;

	public event Action<PresentationMessage>? PresentationReceived;

	public event Action<NavMeshObstacleVolumes>? NavMeshObstaclesReceived;

	public NavMeshObstacleVolumes? LatestNavMeshObstacles { get; private set; }

	public event Action<MatchPauseState>? PauseStateReceived;

	public event Action<MatchPauseAllowance>? PauseAllowanceReceived;

	public MatchPauseState? LatestPauseState { get; private set; }

	public MatchPauseAllowance? LatestPauseAllowance { get; private set; }

	public event Action<ChatLine>? ChatReceived;

	public event Action<CommandOutcome>? CommandAnswered;

	public event Action<int, ulong>? LatencyProbeAnswered;

	public event Action<string>? ServerLost;

	public event Action<ServerHello>? HelloAnswered;

	public event Action<LobbyStateMessage>? LobbyStateReceived;

	public event Action<LobbyChatMessage>? LobbyChatReceived;

	public event Action<LobbyMatchStartedMessage>? LobbyMatchStartedEvent;

	public event Action<LobbyKickedMessage>? LobbyKickedEvent;

	/// <summary>
	///     Raised when the lobby's map asks its host to read a warning before the match starts. The authority sends it to
	///     the host alone, and the match does not begin until that host starts again with the warning accepted.
	/// </summary>
	public event Action<LobbyStartWarningMessage>? LobbyStartWarningReceived;

	public event Action<MapTransferReply>? MapTransferReplied;

	public ClientServerSession(ClientNetworkChannel channel)
	{
		_channel = channel;
		_channel.FrameArrived += Enqueue;
	}

	private const int FailedLaunchDetailLineCount = 8;

	/// <summary>
	///     A start that failed names what the authority said, and the tail of the authority's console behind it, because a
	///     screen that asked for a match and did not get one has nowhere else to look.
	/// </summary>
	private static MatchStartResult FailedLaunch(string failure, string logPath)
	{
		string detail = ServerConsoleCapture.ReadLastLines(logPath, FailedLaunchDetailLineCount);
		return MatchStartResult.Failed(detail.Length == 0 ? failure : $"{failure}\n\n{detail}", logPath);
	}

	public async Task<MatchStartResult> TryStartAsync(ServerMatchStart start, int authorityPort = 0)
	{
		if (!ListenServerTarget.TryResolve(out ServerLaunchTarget? target, out string failure) || target == null) return MatchStartResult.Failed(failure, string.Empty);

		string logName = Path.GetFileName(Path.TrimEndingDirectorySeparator(start.MapFolderPath));
		if (string.IsNullOrEmpty(logName)) logName = "map";
		string authorityLogPath = Path.Combine(start.LogDirectory, LocalAuthority.LogFileName);
		try
		{
			Directory.CreateDirectory(start.LogDirectory);
		}
		catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
		{
			return MatchStartResult.Failed($"the match log folder '{start.LogDirectory}' could not be created: {exception.Message}", authorityLogPath);
		}

		_token = start.OpenToOthers ? string.Empty : RealmProtocol.CreateToken();
		string? rosterPath = null;
		if (start.Roster is { Count: > 0 })
		{
			rosterPath = Path.Combine(start.LogDirectory, $"{logName}{MatchRosterFile.FileSuffix}");
			MatchRosterFile.Write(rosterPath, start.Roster);
		}

		var command = new ServerControlCommand
		{
			Kind = ServerControlKind.StartMatch,
			MapFolderPath = start.MapFolderPath,
			Port = authorityPort,
			Seed = start.Seed,
			Token = _token,
			Cheats = start.Cheats,
			WasmDebug = start.WasmDebug,
			MapWorkspacePath = start.WasmDebug ? start.MapFolderPath : string.Empty,
			RosterPath = rosterPath ?? string.Empty,
			AuthorityRecordFolderPath = start.AuthorityRecordDirectory ?? string.Empty
		};

		return await StartOnLocalAuthorityAsync(target, command, start.LogDirectory, start.DataFolderPath, start.Language);
	}

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
		_started = true;
		_joinedPort = port;
		_address = address;
		_port = port;
		return true;
	}

	public void ConfigureLobbyIdentity(string playerName, string sessionToken, long accountId)
	{
		_playerName = playerName ?? string.Empty;
		_sessionToken = sessionToken ?? string.Empty;
		_accountId = accountId;
	}

	public async Task<MatchStartResult> TryStartLobbyAsync(ServerLobbyStart start, int lobbyPort = 0)
	{
		if (!ListenServerTarget.TryResolve(out ServerLaunchTarget? target, out string failure) || target == null) return MatchStartResult.Failed(failure, string.Empty);

		string authorityLogPath = Path.Combine(start.LogDirectory, LocalAuthority.LogFileName);
		try
		{
			Directory.CreateDirectory(start.LogDirectory);
		}
		catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
		{
			return MatchStartResult.Failed($"the lobby log folder '{start.LogDirectory}' could not be created: {exception.Message}", authorityLogPath);
		}

		_token = string.Empty;
		_adminToken = RealmProtocol.CreateToken();

		var command = new ServerControlCommand
		{
			Kind = ServerControlKind.StartMatch,
			MapFolderPath = start.MapFolderPath,
			Port = lobbyPort,
			WasmDebug = start.WasmDebug,
			MapWorkspacePath = start.WasmDebug ? start.MapFolderPath : string.Empty,
			Cheats = start.Cheats,
			Lobby = true,
			LoopbackOnly = start.LoopbackOnly,
			AdminToken = _adminToken,
			MapName = start.MapName,
			MaxPlayers = start.MaxPlayers
		};

		return await StartOnLocalAuthorityAsync(target, command, start.LogDirectory, start.DataFolderPath, start.Language);
	}

	/// <summary>
	///     Puts a match on the authority this screen owns and joins it. The authority is already running - it came up while
	///     the screen was in its menus - so this hands it the match rather than starting a process to hold it.
	/// </summary>
	private async Task<MatchStartResult> StartOnLocalAuthorityAsync(ServerLaunchTarget target, ServerControlCommand command, string logDirectory, string dataFolderPath, string language)
	{
		LocalAuthority authority = GameContext.LocalAuthority;
		authority.Configure(target, logDirectory, dataFolderPath);

		LocalAuthorityReply started = await authority.StartMatchAsync(command);
		if (!started.Started) return FailedLaunch(started.Failure, started.LogPath);

		if (!_channel.TryConnect("127.0.0.1", started.Port, out string failure))
		{
			authority.Pause();
			return MatchStartResult.Failed(failure, started.LogPath);
		}

		_local = true;
		_localPort = started.Port;
		_localWireTableHash = started.WireTableHash;
		_started = true;
		_language = language;
		_mapHash = started.MapHash;
		_address = "127.0.0.1";
		_port = started.Port;
		LogPath = started.LogPath;
		ConsoleLogPath = started.ConsoleLogPath;
		return MatchStartResult.Success(LogPath, ConsoleLogPath);
	}

	public bool TryJoinLobby(string address, int port, string language, int localPort, out string failure)
	{
		failure = string.Empty;

		_mapHash = string.Empty;
		if (!_channel.TryConnect(address, port, localPort, out failure)) return false;

		_language = language;
		_joined = true;
		_started = true;
		_joinedPort = port;
		_address = address;
		_port = port;
		return true;
	}

	public bool SendLobbyCommand(LobbyCommand command)
	{
		if (!IsRunning) return false;

		_channel.Send(NetworkFrameKind.LobbyCommand, MemoryPackSerializer.Serialize(command));
		return true;
	}

	public bool RequestMapTransfer(MapTransferRequest request)
	{
		if (!IsRunning) return false;

		_channel.Send(NetworkFrameKind.MapTransferRequest, MemoryPackSerializer.Serialize(request));
		return true;
	}

	public bool Submit(byte[] payload)
	{
		if (!IsRunning || payload.Length == 0) return false;

		_channel.Send(NetworkFrameKind.Command, payload);
		return true;
	}

	public bool RequestBaseline()
	{
		if (!IsRunning) return false;

		_channel.Send(NetworkFrameKind.BaselineRequest, Array.Empty<byte>());
		return true;
	}

	/// <summary>
	///     Reports a description that arrived and could not be applied here, so the match states every unit and every text
	///     again. Sending it again would be the same bytes this screen has just failed on, and waiting for a baseline pays
	///     for one unreadable description with the whole world: what this screen is missing is what that one description
	///     carried, and a description stated from nothing carries all of it.
	/// </summary>
	public bool RequestDescriptionRepeat()
	{
		if (!IsRunning) return false;

		_channel.Send(NetworkFrameKind.SnapshotDescriptionRepeat, Array.Empty<byte>());
		return true;
	}

	/// <summary>
	///     Names a run of descriptions this screen never received, so the match can send exactly those again instead of
	///     describing the whole world to it.
	/// </summary>
	public bool RequestSnapshotRepair(int firstSequence, int count)
	{
		if (!IsRunning || firstSequence <= 0 || count <= 0) return false;

		var request = new SnapshotRepairRequest
		{
			FirstSequence = firstSequence,
			Count = count
		};
		_channel.Send(NetworkFrameKind.SnapshotRepairRequest, MemoryPackSerializer.Serialize(request));
		return true;
	}

	public bool SendLatencyProbe(int sequence)
	{
		if (!IsRunning) return false;

		_channel.SendUnreliable(NetworkFrameKind.LatencyProbe, MemoryPackSerializer.Serialize(new LatencyProbe(sequence)));
		return true;
	}

	public bool RequestPause(bool paused)
	{
		if (!IsRunning) return false;

		_channel.Send(NetworkFrameKind.PauseRequest, MemoryPackSerializer.Serialize(new MatchPauseState { Paused = paused, RequestedByPlayer = -1 }));
		return true;
	}

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

	public bool Acknowledge(int sequence, ulong appliedMask, int roundTripMilliseconds)
	{
		if (sequence <= 0) return false;

		var ack = new SnapshotAck
		{
			Sequence = sequence,
			AppliedMask = appliedMask,
			RoundTripMilliseconds = roundTripMilliseconds > 0 ? roundTripMilliseconds : 0
		};

		// An answer is worth nothing once the next one is written: it restates where this screen has got to, and a later
		// answer restates it more recently. Held behind a baseline on the reliable stream it would report a peer as silent
		// for the whole time that baseline took to arrive, which is what asked for the next one.
		_channel.SendUnreliable(NetworkFrameKind.SnapshotAck, MemoryPackSerializer.Serialize(ack));
		return true;
	}

	public bool Pump()
	{
		if (!IsRunning)
		{
			if (_started) ReportLost(DescribeStop());
			return false;
		}

		if (!_local && !_joined) return false;

		bool handed = false;

		_frames.DiscardSuperseded();

		if (_helloSent && _connectedBefore && !_channel.IsConnected)
		{
			_helloSent = false;
			_challenge = string.Empty;
		}

		if (!_helloSent && _channel.IsConnected)
		{
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

		// Everything the match cannot proceed without is handed over whole and in the order it arrived. Descriptions are
		// the only frames with a budget, because they are the only frames a later one can stand in for.
		int reliableToDrain = _frames.ReliableCount;
		while (reliableToDrain-- > 0 && _frames.TryDequeueReliable(out SessionFrameQueue.QueuedFrame reliable))
		{
			DispatchFrame(reliable);
			handed = true;
		}

		int snapshotsApplied = 0;
		while (_frames.TryPeekDescription(out SessionFrameQueue.QueuedFrame frame))
		{
			if (frame.Kind == NetworkFrameKind.Snapshot && snapshotsApplied >= MaximumSnapshotsAppliedPerPump) break;

			_frames.PopDescription();
			if (frame.Kind == NetworkFrameKind.Snapshot) snapshotsApplied++;

			DispatchFrame(frame);
			handed = true;
		}

		if (_frames.TakeDroppedDescriptionMark() && !InLobby)
		{
			GameContext.SnapshotApply.RequestBaseline();
		}

		return handed;
	}

	public void DropConnectionForHarness()
	{
		if (_address.Length == 0) return;

		_channel.Disconnect();
		_helloSent = false;
		_challenge = string.Empty;
	}

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

	public string DescribeStop()
	{
		if (_local)
		{
			return GameContext.LocalAuthority.IsRunning
				? "the connection to the match ended"
				: $"the local authority this match ran on ended; see '{LogPath}' and '{ConsoleLogPath}'";
		}

		return _joined ? "the connection to the match ended" : "the match was never started";
	}

	private void SendHello()
	{
		var hello = new ClientHello
		{
			Build = RealmVersion.GameBuildNumber,
			TokenProof = _token.Length > 0 && _challenge.Length > 0 ? RealmProtocol.ProveToken(_token, _challenge) : string.Empty,
			AdminProof = _adminToken.Length > 0 && _challenge.Length > 0 ? RealmProtocol.ProveToken(_adminToken, _challenge) : string.Empty,
			WireTableHash = _local ? _localWireTableHash : ReplicatedComponents.ComputeWireTableHash(),
			MapHash = _mapHash,
			Language = _language,
			PlayerName = _playerName,
			AccountId = _accountId,
			SessionToken = _sessionToken
		};

		_channel.Send(NetworkFrameKind.ClientHello, MemoryPackSerializer.Serialize(hello));
	}

	private void Enqueue(NetworkFrameKind kind, byte[] payload) => _frames.Enqueue(kind, payload);

	/// <summary>
	///     Hands one queued frame to the session, and reports what it could make of it. A description this screen cannot
	///     apply is state it never held, so the match is asked to state every unit and text again; only a screen that fails
	///     the one it is sent in answer asks for the world instead. Any other frame it cannot read is one it never received,
	///     and nothing else would report it missing.
	/// </summary>
	private void DispatchFrame(SessionFrameQueue.QueuedFrame frame)
	{
		try
		{
			Dispatch(frame.Kind, frame.Payload, frame.ArrivalMilliseconds);
		}
		catch (Exception exception) when (exception is IOException or InvalidDataException or ArgumentException or MemoryPackSerializationException)
		{
			GD.PushWarning($"[Network] A {frame.Kind} frame could not be read and was dropped: {exception.GetType().Name}: {exception.Message}\n{FirstFramesOf(exception)}");

			if (frame.Kind != NetworkFrameKind.Snapshot)
			{
				if (!InLobby) GameContext.SnapshotApply.RequestBaseline();
				return;
			}

			// The first description this screen fails on is answered by stating the world to it again, which is one ordinary
			// description rather than a world rebuilt on the reliable stream. A screen that fails the one after that too is
			// not being told something it can apply at all, and a baseline is the only statement left that differs.
			_unreadableDescriptions++;
			if (_unreadableDescriptions == 1) RequestDescriptionRepeat();
			else if (!InLobby) GameContext.SnapshotApply.RequestBaseline();
		}
	}

	private static string FirstFramesOf(Exception exception)
	{
		string? stack = exception.StackTrace;
		if (string.IsNullOrEmpty(stack)) return "no stack";

		string[] lines = stack.Split('\n');
		int shown = Math.Min(4, lines.Length);
		var text = new System.Text.StringBuilder();
		for (int index = 0; index < shown; index++) text.Append(lines[index].Trim()).Append("\n");
		return text.ToString();
	}

	private void Dispatch(NetworkFrameKind kind, byte[] payload, ulong arrivalMilliseconds)
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
				ApplyPresentationBatch(payload);
				break;

			case NetworkFrameKind.CommandOutcome:
				CommandAnswered?.Invoke(MemoryPackSerializer.Deserialize<CommandOutcome>(payload));
				break;

			case NetworkFrameKind.LatencyProbeEcho:
				LatencyProbeAnswered?.Invoke(MemoryPackSerializer.Deserialize<LatencyProbe>(payload).Sequence, arrivalMilliseconds);
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

			case NetworkFrameKind.PauseAllowance:
			{
				MatchPauseAllowance allowance = MemoryPackSerializer.Deserialize<MatchPauseAllowance>(payload);
				LatestPauseAllowance = allowance;
				PauseAllowanceReceived?.Invoke(allowance);
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

			case NetworkFrameKind.LobbyStartWarning:
				LobbyStartWarningReceived?.Invoke(MemoryPackSerializer.Deserialize<LobbyStartWarningMessage>(payload));
				break;

			case NetworkFrameKind.MapTransferReply:
				MapTransferReplied?.Invoke(MemoryPackSerializer.Deserialize<MapTransferReply>(payload));
				break;

			case NetworkFrameKind.Shutdown:
				ReportLost("the authority ended the match");
				break;
		}
	}

	/// <summary>
	///     Hands on every request a tick's batch carried, in the order the map raised them. A screen applies the ones that
	///     name a description sequence where that sequence is drawn, so a batch arriving late describes a moment in the
	///     past that is either still ahead of the cursor or already behind it.
	/// </summary>
	private void ApplyPresentationBatch(byte[] payload)
	{
		PresentationBatch batch = MemoryPackSerializer.Deserialize<PresentationBatch>(payload);
		PresentationMessage[] messages = batch.Messages;
		if (messages == null) return;

		for (int index = 0; index < messages.Length; index++) PresentationReceived?.Invoke(messages[index]);
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

	private int _unreadableDescriptions;

	private void ApplySnapshot(byte[] payload)
	{
		// A description too large for one datagram arrives as several. A group a piece short is a description this screen
		// was not told: nothing is applied for it, the acknowledgement reports the hole, and the match sends it again.
		if (!_snapshotReassembler.TryTake(payload, out byte[]? frame)) return;

		ReadOnlySpan<byte> serialized = _snapshotDecompressor.Decode(frame, ref _snapshotBuffer);
		WorldSnapshot snapshot = MemoryPackSerializer.Deserialize<WorldSnapshot>(serialized);

		if (MultiplayerBandwidthMeter.IsRecording) MultiplayerBandwidthMeter.RecordReceivedSnapshot(frame.Length, snapshot.IsBaseline);

		SnapshotReceived?.Invoke(snapshot);

		_unreadableDescriptions = 0;
	}

	private void ReportLost(string report)
	{
		if (TerminationReport != null) return;

		TerminationReport = report;
		ServerLost?.Invoke(report);
	}

	/// <summary>
	///     Lets go of the match. A match that was put on this screen's own authority is not killed with it: the authority is
	///     told to pause, which gives the match up and keeps the process that served it, so nothing has to be started or
	///     stopped between one session and the next.
	/// </summary>
	public void Stop()
	{
		if (!_local && !_joined) return;

		bool heldHere = _local;
		_local = false;
		_joined = false;
		_localPort = 0;
		_localWireTableHash = string.Empty;
		_mapHash = string.Empty;
		_address = string.Empty;
		_port = 0;
		_channel.FrameArrived -= Enqueue;

		_frames.Clear();

		if (heldHere) GameContext.LocalAuthority.Pause();
		_channel.Disconnect();
	}

	public void Dispose()
	{
		Stop();

		if (_disposed) return;
		_disposed = true;

		_snapshotDecompressor.Dispose();
	}
}
