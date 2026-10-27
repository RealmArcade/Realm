using System;
using System.Collections.Generic;
using System.IO;
using MemoryPack;
using Realm.Client.Network;
using Realm.Ecs.Replication;
using Realm.Shared;
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
public record ServerMatchStart(string MapFolderPath, string MapName, int Seed, string LogDirectory, string DataFolderPath, string Language, bool Cheats = false, bool OpenToOthers = false);

/// <summary>
///     One client's match. A client never simulates a match: it starts the authority beside it, joins it as an ordinary
///     ENet peer, sends its orders and is told what the world is. Everything a match produces arrives here — the world
///     as this player can see it, what the map asked a screen to show, and whether the authority took each order — and
///     nothing about the match is shared in memory with the process that runs it.
/// </summary>
public class ClientServerSession : IDisposable
{
	private readonly ClientNetworkChannel _channel;
	private readonly Queue<(NetworkFrameKind Kind, byte[] Payload)> _incoming = new();

	private ServerProcessLauncher? _launcher;
	private string _language = string.Empty;
	private string _token = string.Empty;
	private bool _helloSent;
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

	/// <summary>Whether this client is still connected to a living authority.</summary>
	public bool IsRunning => TerminationReport == null && (_launcher == null ? _joined : _launcher.IsRunning);

	/// <summary>The identity the authority gave this connection, or zero before it has been seated.</summary>
	public int LocalPeerId { get; private set; }

	/// <summary>The player slot this client acts as, or nothing before the authority has seated it.</summary>
	public int LocalPlayerSlot { get; private set; } = -1;

	/// <summary>The team the player slot belongs to, which decides who this client may order its units against.</summary>
	public int LocalTeam { get; private set; }

	/// <summary>The newest snapshot this client has applied, which is what it acknowledges to the authority.</summary>
	public int AppliedSequence { get; private set; }

	/// <summary>The log the authority writes for itself, so a match can show where to look when something goes wrong.</summary>
	public string LogPath { get; private set; } = string.Empty;

	/// <summary>Where the authority's own console output was kept.</summary>
	public string ConsoleLogPath { get; private set; } = string.Empty;

	/// <summary>The port the authority is listening on.</summary>
	public int Port => _launcher?.Port ?? _joinedPort;

	/// <summary>The wire table hash the two processes agreed on, so a mismatch is refused rather than played through.</summary>
	public string WireTableHash => _launcher?.WireTableHash ?? string.Empty;

	/// <summary>The authority seated this connection as a player.</summary>
	public event Action<PeerRosterAssignment>? Seated;

	/// <summary>The world as this player can see it, as the authority described it.</summary>
	public event Action<WorldSnapshot>? SnapshotReceived;

	/// <summary>Something the map asked a screen to show.</summary>
	public event Action<PresentationMessage>? PresentationReceived;

	/// <summary>Whether the authority took one of this client's orders, and why not when it did not.</summary>
	public event Action<CommandOutcome>? CommandAnswered;

	/// <summary>One of this client's probes came back, which is what the round trip is measured from.</summary>
	public event Action<int>? LatencyProbeAnswered;

	/// <summary>The authority ended in the middle of a match, whether or not it meant to.</summary>
	public event Action<string>? ServerLost;

	public ClientServerSession(ClientNetworkChannel channel)
	{
		_channel = channel;
		_channel.FrameArrived += Enqueue;
	}

	/// <summary>
	///     Starts the authority for this match and joins it. The token is minted here so an authority this client
	///     started is the only one that accepts it, and the map's name decides where its progress is kept.
	/// </summary>
	/// <param name="start">What the match is, as this client knows it.</param>
	/// <param name="failure">What stopped the match from starting, or nothing when it started.</param>
	/// <param name="authorityPort">
	///     The port to listen on, or zero to let the authority pick one. A match this machine plays by itself is reached on
	///     whatever port the authority reports, while a match other machines are told to join has to be listening on the
	///     port they were told.
	/// </param>
	public bool TryStart(ServerMatchStart start, out string failure, int authorityPort = 0)
	{
		failure = string.Empty;

		if (!ListenServerTarget.TryResolve(out ServerLaunchTarget? target, out failure) || target == null) return false;

		string logPath = Path.Combine(start.LogDirectory, $"{start.MapName}.listen_server.log");
		try
		{
			Directory.CreateDirectory(start.LogDirectory);
		}
		catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
		{
			failure = $"the match log folder '{start.LogDirectory}' could not be created: {exception.Message}";
			return false;
		}

		_token = start.OpenToOthers ? string.Empty : RealmProtocol.CreateToken();
		var request = new ServerLaunchRequest(target, start.MapFolderPath, logPath, _token, start.Seed)
		{
			DataFolderPath = start.DataFolderPath,
			Cheats = start.Cheats,
			Port = authorityPort
		};

		if (!ServerProcessLauncher.TryLaunch(request, out ServerProcessLauncher? launcher, out failure) || launcher == null) return false;

		if (!_channel.TryConnect("127.0.0.1", launcher.Port, out failure))
		{
			launcher.Dispose();
			return false;
		}

		_launcher = launcher;
		_language = start.Language;
		_address = "127.0.0.1";
		_port = launcher.Port;
		LogPath = launcher.LogPath;
		ConsoleLogPath = launcher.ConsoleLogPath;
		return true;
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
	public bool TryJoin(string address, int port, string language, out string failure)
	{
		failure = string.Empty;

		if (!_channel.TryConnect(address, port, out failure)) return false;

		_language = language;
		_joined = true;
		_joinedPort = port;
		_address = address;
		_port = port;
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

	/// <summary>Tells the authority how far this client has applied the world.</summary>
	public bool Acknowledge(int sequence)
	{
		if (sequence <= 0) return false;

		var ack = new SnapshotAck { Sequence = sequence };
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

		if (_helloSent && _connectedBefore && !_channel.IsConnected) _helloSent = false;

		if (!_helloSent && _channel.IsConnected)
		{
			SendHello();
			_helloSent = true;
			_connectedBefore = true;
			_reconnectAttempts = 0;
		}
		else if (!_helloSent && !TryReconnect())
		{
			return false;
		}

		while (_incoming.Count > 0)
		{
			(NetworkFrameKind kind, byte[] payload) = _incoming.Dequeue();
			Dispatch(kind, payload);
			handed = true;
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
	}

	/// <summary>
	///     Rejoins the authority after the connection ended. The world is described from scratch when the peer is seated
	///     again, so a client that lost a moment of the match catches up rather than sitting on a world nobody advances.
	/// </summary>
	/// <returns>False once reconnecting has been given up on, having reported the loss.</returns>
	private bool TryReconnect()
	{
		if (_address.Length == 0 || _port <= 0) return true;

		long now = Environment.TickCount64;
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
	///     Says who this client is. The token is the one the authority was started with, so an authority that was left
	///     running by an earlier match refuses this one instead of seating it as a stranger.
	/// </summary>
	private void SendHello()
	{
		var hello = new ClientHello
		{
			Protocol = RealmProtocol.Version,
			Build = RealmVersion.GameBuildNumber,
			Token = _token,
			WireTableHash = _launcher?.WireTableHash ?? ReplicatedComponents.ComputeWireTableHash(),
			Language = _language
		};

		_channel.Send(NetworkFrameKind.ClientHello, MemoryPackSerializer.Serialize(hello));
	}

	private void Enqueue(NetworkFrameKind kind, byte[] payload) => _incoming.Enqueue((kind, payload));

	private void Dispatch(NetworkFrameKind kind, byte[] payload)
	{
		switch (kind)
		{
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

			case NetworkFrameKind.Shutdown:
				ReportLost("the authority ended the match");
				break;
		}
	}

	private void ApplyHello(byte[] payload)
	{
		ServerHello response = MemoryPackSerializer.Deserialize<ServerHello>(payload);
		if (response.Accepted) return;

		ReportLost($"the authority refused the connection: {response.Rejection}");
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

		AppliedSequence = snapshot.Sequence;
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
		_address = string.Empty;
		_port = 0;
		_channel.FrameArrived -= Enqueue;

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
