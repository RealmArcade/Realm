using System;
using System.Collections.Generic;
using System.IO;
using MemoryPack;
using Realm.Client.Network;
using Realm.Shared;
using Realm.Shared.Network;

namespace Realm.Client.Services;

/// <summary>What a client needs in order to have a match run for it: which map, where the authority writes its own log, where a map's saved data belongs, and the language this player reads in.</summary>
public record ServerMatchStart(string MapFolderPath, string MapName, string LogDirectory, string DataFolderPath, string Language);

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
	private bool _disposed;

	/// <summary>What the authority said when it stopped, or nothing while it is still there.</summary>
	public string? TerminationReport { get; private set; }

	/// <summary>Whether this client is still connected to a living authority.</summary>
	public bool IsRunning => _launcher is { IsRunning: true } && TerminationReport == null;

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
	public int Port => _launcher?.Port ?? 0;

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
	public bool TryStart(ServerMatchStart start, out string failure)
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

		_token = RealmProtocol.CreateToken();
		var request = new ServerLaunchRequest(target, start.MapFolderPath, logPath, _token)
		{
			DataFolderPath = start.DataFolderPath
		};

		if (!ServerProcessLauncher.TryLaunch(request, out ServerProcessLauncher? launcher, out failure) || launcher == null) return false;

		if (!_channel.TryConnect("127.0.0.1", launcher.Port, out failure))
		{
			launcher.Dispose();
			return false;
		}

		_launcher = launcher;
		_language = start.Language;
		LogPath = launcher.LogPath;
		ConsoleLogPath = launcher.ConsoleLogPath;
		return true;
	}

	/// <summary>Hands the authority one of this client's orders, as the client authored it.</summary>
	public bool Submit(NetworkCommand command)
	{
		if (_launcher == null) return false;

		_channel.Send(NetworkFrameKind.Command, MemoryPackSerializer.Serialize(command));
		return true;
	}

	/// <summary>Asks the authority to describe the world from scratch, which is what a client that lost its place does.</summary>
	public bool RequestBaseline()
	{
		if (_launcher == null) return false;

		_channel.Send(NetworkFrameKind.BaselineRequest, Array.Empty<byte>());
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
		if (_launcher == null) return false;
		if (!_launcher.IsRunning)
		{
			ReportLost(DescribeStop());
			return false;
		}

		bool handed = false;

		if (!_helloSent && _channel.IsConnected)
		{
			SendHello();
			_helloSent = true;
		}

		while (_incoming.Count > 0)
		{
			(NetworkFrameKind kind, byte[] payload) = _incoming.Dequeue();
			Dispatch(kind, payload);
			handed = true;
		}

		return handed;
	}

	/// <summary>Whether the authority already ended, without reading anything, so a match can report a loss at once.</summary>
	public bool HasServerEnded() => _launcher != null && !_launcher.IsRunning;

	/// <summary>The reason to report when the authority is no longer there.</summary>
	public string DescribeStop()
	{
		if (_launcher == null) return "the match was never started";

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
			WireTableHash = _launcher?.WireTableHash ?? string.Empty,
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
		if (launcher == null) return;

		_launcher = null;
		_channel.FrameArrived -= Enqueue;
		_channel.Send(NetworkFrameKind.Shutdown, Array.Empty<byte>());
		_channel.Disconnect();
		launcher.Dispose();
	}

	public void Dispose()
	{
		if (_disposed) return;
		_disposed = true;

		Stop();
	}
}
