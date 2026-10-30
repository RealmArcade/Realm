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

public record ServerLobbyStart(string MapFolderPath, string MapName, int MaxPlayers, string LogDirectory, string DataFolderPath, string Language, bool WasmDebug = false);

public readonly record struct MatchStartResult(bool Started, string Failure, string LogPath, string ConsoleLogPath)
{
	public static MatchStartResult Success(string logPath, string consoleLogPath) => new(true, string.Empty, logPath, consoleLogPath);

	public static MatchStartResult Failed(string failure, string logPath) => new(false, failure, logPath, logPath.Length == 0 ? string.Empty : ServerConsoleCapture.ResolvePath(logPath));
}

public class ClientServerSession : IDisposable
{
	private readonly ClientNetworkChannel _channel;

	private readonly List<QueuedFrame> _incoming = new();
	private int _incomingIndex;
	private bool _descriptionsWereDropped;

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

	private ServerProcessLauncher? _launcher;
	private string _language = string.Empty;
	private string _token = string.Empty;

	private string _adminToken = string.Empty;

	private string _playerName = string.Empty;

	private string _sessionToken = string.Empty;

	private string _mapHash = string.Empty;
	private bool _helloSent;

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

	private const int MaximumQueuedFrames = 256;

	public bool IsRunning => TerminationReport == null && (_launcher == null ? _joined : _launcher.IsRunning);

	public int LocalPeerId { get; private set; }

	public int LocalPlayerSlot { get; private set; } = -1;

	public bool IsAdmin { get; private set; }

	public bool InLobby { get; private set; }

	public int LocalTeam { get; private set; }

	public string LogPath { get; private set; } = string.Empty;

	public string ConsoleLogPath { get; private set; } = string.Empty;

	public int Port => _launcher?.Port ?? _joinedPort;

	public int AuthorityProcessId => _launcher?.ProcessId ?? 0;

	public string WireTableHash => _launcher?.WireTableHash ?? string.Empty;

	public event Action<PeerRosterAssignment>? Seated;

	public event Action<WorldSnapshot>? SnapshotReceived;

	public event Action<PresentationMessage>? PresentationReceived;

	public event Action<NavMeshObstacleVolumes>? NavMeshObstaclesReceived;

	public NavMeshObstacleVolumes? LatestNavMeshObstacles { get; private set; }

	public event Action<MatchPauseState>? PauseStateReceived;

	public MatchPauseState? LatestPauseState { get; private set; }

	public event Action<ChatLine>? ChatReceived;

	public event Action<CommandOutcome>? CommandAnswered;

	public event Action<int, ulong>? LatencyProbeAnswered;

	public event Action<string>? ServerLost;

	public event Action<ServerHello>? HelloAnswered;

	public event Action<LobbyStateMessage>? LobbyStateReceived;

	public event Action<LobbyChatMessage>? LobbyChatReceived;

	public event Action<LobbyMatchStartedMessage>? LobbyMatchStartedEvent;

	public event Action<LobbyKickedMessage>? LobbyKickedEvent;

	public event Action<MapTransferReply>? MapTransferReplied;

	public ClientServerSession(ClientNetworkChannel channel)
	{
		_channel = channel;
		_channel.FrameArrived += Enqueue;
	}

	private const int FailedLaunchDetailLineCount = 8;

	private static MatchStartResult FailedLaunch(ServerLaunchAttempt attempt, string logPath)
	{
		string detail = ServerConsoleCapture.ReadLastLines(logPath, FailedLaunchDetailLineCount);
		return MatchStartResult.Failed(detail.Length == 0 ? attempt.Failure : $"{attempt.Failure}\n\n{detail}", logPath);
	}

	public async Task<MatchStartResult> TryStartAsync(ServerMatchStart start, int authorityPort = 0)
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
			return MatchStartResult.Failed($"the match log folder '{start.LogDirectory}' could not be created: {exception.Message}", logPath);
		}

		_token = start.OpenToOthers ? string.Empty : RealmProtocol.CreateToken();
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

	public void ConfigureLobbyIdentity(string playerName, string sessionToken)
	{
		_playerName = playerName ?? string.Empty;
		_sessionToken = sessionToken ?? string.Empty;
	}

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

	public bool TryJoinLobby(string address, int port, string language, int localPort, out string failure)
	{
		failure = string.Empty;

		_mapHash = string.Empty;
		if (!_channel.TryConnect(address, port, localPort, out failure)) return false;

		_language = language;
		_joined = true;
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

		_channel.Send(NetworkFrameKind.LatencyProbe, MemoryPackSerializer.Serialize(new LatencyProbe(sequence)));
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
		_channel.Send(NetworkFrameKind.SnapshotAck, MemoryPackSerializer.Serialize(ack));
		return true;
	}

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
			QueuedFrame frame = _incoming[_incomingIndex];
			_incomingIndex++;

			try
			{
				Dispatch(frame.Kind, frame.Payload, frame.ArrivalMilliseconds);
			}
			catch (Exception exception) when (exception is IOException or InvalidDataException or ArgumentException or MemoryPackSerializationException)
			{
				GD.PushWarning($"[Network] A {frame.Kind} frame could not be read and was dropped: {exception.Message}");

				if (!InLobby) GameContext.SnapshotApply.RequestBaseline();
			}

			handed = true;
		}

		if (_incomingIndex >= _incoming.Count)
		{
			_incoming.Clear();
			_incomingIndex = 0;
		}

		if (_descriptionsWereDropped)
		{
			_descriptionsWereDropped = false;
			if (!InLobby) GameContext.SnapshotApply.RequestBaseline();
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

	public bool HasServerEnded() => _launcher != null && !_launcher.IsRunning;

	public string DescribeStop()
	{
		if (_launcher == null) return _joined ? "the connection to the match ended" : "the match was never started";

		return _launcher.IsRunning
			? "the connection to the match ended"
			: $"the process running this match ended with code {_launcher.ExitCode}; see '{LogPath}' and '{ConsoleLogPath}'";
	}

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
			if (kind is NetworkFrameKind.Snapshot or NetworkFrameKind.Presentation) _descriptionsWereDropped = true;
			return;
		}

		ulong arrivalMilliseconds = kind == NetworkFrameKind.LatencyProbeEcho ? Time.GetTicksMsec() : 0UL;
		_incoming.Add(new QueuedFrame(kind, payload, arrivalMilliseconds));
	}

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
				PresentationReceived?.Invoke(MemoryPackSerializer.Deserialize<PresentationMessage>(payload));
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

		if (MultiplayerBandwidthMeter.IsRecording) MultiplayerBandwidthMeter.RecordReceivedSnapshot(payload.Length, snapshot.IsBaseline);

		SnapshotReceived?.Invoke(snapshot);
	}

	private void ReportLost(string report)
	{
		if (TerminationReport != null) return;

		TerminationReport = report;
		ServerLost?.Invoke(report);
	}

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

		_incoming.Clear();
		_incomingIndex = 0;
		_descriptionsWereDropped = false;

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
