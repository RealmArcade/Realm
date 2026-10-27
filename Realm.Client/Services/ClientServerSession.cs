using System;
using System.IO;
using MemoryPack;
using Realm.Shared.Network;

namespace Realm.Client.Services;

/// <summary>
///     What a client needs in order to have a match run for it: which map, where the authority writes its own log, where
///     a map's saved data belongs, and the language this player reads in.
/// </summary>
public record ServerMatchStart(string MapFolderPath, string MapName, string LogDirectory, string DataFolderPath, string Language);

/// <summary>
///     One client's match. A client never simulates a match: it starts the authority beside it, connects to it over the
///     loopback transport as an ordinary peer, sends its orders and is told what the world is. Everything a match
///     produces arrives here — the world as this player can see it, what the map asked a screen to show, and whether the
///     authority took each order — and nothing about the match is shared in memory with the process that runs it.
/// </summary>
public class ClientServerSession : IDisposable
{
	/// <summary>How long a single read waits before the caller gets its turn back. A match must never block on a socket.</summary>
	private const int ReadTimeoutMilliseconds = 1;

	private readonly Random _tokenSource = new();

	private ServerProcessLauncher? _launcher;
	private NetworkFrame _frame;
	private bool _disposed;

	/// <summary>What the authority said when it stopped, or nothing while it is still there.</summary>
	public string? TerminationReport { get; private set; }

	/// <summary>Whether this client is still connected to a living authority.</summary>
	public bool IsRunning => _launcher is { IsRunning: true } && _launcher.Transport.IsConnected;

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

	/// <summary>The port the authority is listening on, which a bridge for remote peers forwards to.</summary>
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

	/// <summary>
	///     Starts the authority for this match and connects to it. The token is minted here so an authority this client
	///     started is the only one that accepts it, and the map's name decides where its progress is kept.
	/// </summary>
	public bool TryStart(ServerMatchStart start, out string failure)
	{
		failure = string.Empty;

		if (!ListenServerLocator.TryFind(out string executablePath))
		{
			failure = "this build does not carry the authority that runs its matches";
			return false;
		}

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

		var request = new ServerLaunchRequest(executablePath, start.MapFolderPath, logPath, MintToken())
		{
			DataFolderPath = start.DataFolderPath,
			Language = start.Language
		};

		if (!ServerProcessLauncher.TryLaunch(request, out ServerProcessLauncher? launcher, out failure) || launcher == null) return false;

		_launcher = launcher;
		LogPath = launcher.LogPath;
		ConsoleLogPath = launcher.ConsoleLogPath;
		return true;
	}

	/// <summary>Hands the authority one of this client's orders, as the client authored it.</summary>
	public bool Submit(NetworkCommand command)
	{
		if (_launcher == null) return false;

		try
		{
			_launcher.Transport.Send(NetworkFrameKind.Command, MemoryPackSerializer.Serialize(command));
			return true;
		}
		catch (Exception exception) when (exception is IOException or ObjectDisposedException or InvalidDataException)
		{
			ReportLost($"the order could not be sent: {exception.Message}");
			return false;
		}
	}

	/// <summary>Asks the authority to describe the world from scratch, which is what a client that lost its place does.</summary>
	public bool RequestBaseline() => Send(NetworkFrameKind.BaselineRequest, ReadOnlySpan<byte>.Empty);

	/// <summary>Tells the authority how far this client has applied the world.</summary>
	public bool Acknowledge(int sequence)
	{
		if (sequence <= 0) return false;

		var ack = new SnapshotAck { Sequence = sequence };
		return Send(NetworkFrameKind.SnapshotAck, MemoryPackSerializer.Serialize(ack));
	}

	/// <summary>
	///     Reads at most one frame from the authority and hands it to whichever event it belongs to. A match calls this
	///     every frame: the transport is reliable and ordered, so what a client has to do is read what is waiting rather
	///     than poll for anything in particular.
	/// </summary>
	public bool Pump()
	{
		if (_launcher == null) return false;
		if (!IsRunning)
		{
			ReportLost(DescribeStop());
			return false;
		}

		bool received;
		try
		{
			received = _launcher.Transport.TryReceive(ReadTimeoutMilliseconds, out _frame);
		}
		catch (Exception exception) when (exception is IOException or InvalidDataException)
		{
			ReportLost($"the connection to the match ended: {exception.Message}");
			return false;
		}

		if (!received) return false;

		Dispatch(in _frame);
		return true;
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

	private void Dispatch(in NetworkFrame frame)
	{
		byte[] payload = frame.Payload.ToArray();

		switch (frame.Kind)
		{
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

	private void ApplyRoster(byte[] payload)
	{
		PeerRosterAssignment assignment = MemoryPackSerializer.Deserialize<PeerRosterAssignment>(payload);
		LocalPeerId = assignment.PeerId;
		LocalPlayerSlot = assignment.PlayerSlot;
		LocalTeam = assignment.Team;
		_launcher?.Transport.SeatPeer(assignment.PeerId);
		Seated?.Invoke(assignment);
	}

	private void ApplySnapshot(byte[] payload)
	{
		WorldSnapshot snapshot = MemoryPackSerializer.Deserialize<WorldSnapshot>(SnapshotWireFormat.Decode(payload));

		AppliedSequence = snapshot.Sequence;
		SnapshotReceived?.Invoke(snapshot);
	}

	private bool Send(NetworkFrameKind kind, ReadOnlySpan<byte> payload)
	{
		if (_launcher == null) return false;

		try
		{
			_launcher.Transport.Send(kind, payload);
			return true;
		}
		catch (Exception exception) when (exception is IOException or ObjectDisposedException or InvalidDataException)
		{
			ReportLost($"the connection to the match ended: {exception.Message}");
			return false;
		}
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
		launcher.Dispose();
	}

	/// <summary>
	///     A token for this match, minted so the authority only accepts the client that started it. It is not a secret
	///     between players; it is what tells an authority that has been left running whose connection it is answering.
	/// </summary>
	private string MintToken()
	{
		Span<byte> bytes = stackalloc byte[16];
		_tokenSource.NextBytes(bytes);
		return Convert.ToHexString(bytes);
	}

	public void Dispose() => Stop();
}
