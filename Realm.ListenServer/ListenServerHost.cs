using System.Security.Cryptography;
using System.Text;
using MemoryPack;
using Realm.Ecs.Replication;
using Realm.Shared;
using Realm.Shared.Maps;
using Realm.Shared.Network;
using Realm.Shared.Simulation;

namespace Realm.ListenServer;

public class ListenServerHost : IServerHost
{
	private const int AcceptBacklog = 16;

	private readonly ServerArguments _arguments;
	private readonly ServerLog _log;
	private readonly IServerTransport _transport;
	private readonly ManualResetEventSlim _shutdownRequested = new(false);
	private readonly List<IClientTransport> _peers = new();
	private readonly List<IClientTransport> _seatedPeers = new();
	private readonly HashSet<IClientTransport> _peersNeedingBaseline = new();
	private readonly List<Thread> _peerThreads = new();
	private readonly object _peersLock = new();
	private long _lastLoggedAckSequence;

	private ServerSimulation? _simulation;
	private ServerTickLoop? _tickLoop;
	private Thread? _acceptThread;
	private bool _disposed;

	public ListenServerHost(ServerArguments arguments, ServerLog log)
	{
		_arguments = arguments;
		_log = log;
		_transport = new LoopbackServerTransport(arguments.Port, log);
	}

	public bool IsRunning { get; private set; }

	/// <summary>
	///     The seed a match's scripts draw from. It comes from the token the client generated for this match, so a
	///     match is reproducible from the token and two matches do not share a sequence.
	/// </summary>
	private int SeedFromToken()
	{
		uint hash = 2166136261;
		string token = _arguments.Token;
		for (int index = 0; index < token.Length; index++)
		{
			hash ^= token[index];
			hash *= 16777619;
		}

		return (int)(hash & 0x7FFFFFFF);
	}

	public int Port => _transport.BoundPort;

	public void Start()
	{
		if (IsRunning) return;
		if (!Directory.Exists(_arguments.MapPath))
		{
			throw new DirectoryNotFoundException($"The map folder '{_arguments.MapPath}' does not exist.");
		}

		LoadedMap map = MapWorldLoader.Load(_arguments.MapPath);
		try
		{
			_simulation = new ServerSimulation(map, _arguments.MapPath, _log, Path.GetDirectoryName(_arguments.LogPath), SeedFromToken());
			_log.Write(_simulation.Describe());
			for (int index = 0; index < map.Warnings.Count; index++)
			{
				_log.Write($"map warning: {map.Warnings[index]}");
			}

			_transport.Start(AcceptBacklog);
			IsRunning = true;
			RunSimulation(_simulation);
		}
		catch
		{
			_simulation?.Dispose();
			_simulation = null;
			throw;
		}
	}

	private void RunSimulation(ServerSimulation simulation)
	{
		string mapHash = MapContentHash.Compute(_arguments.MapPath, _arguments.LogPath);
		string wireTableHash = ReplicatedComponents.ComputeWireTableHash();
		var readyInfo = new ServerReadyInfo(_transport.BoundPort, RealmProtocol.Version, RealmVersion.GameBuildNumber, mapHash, wireTableHash);

		_log.Write($"ready protocol={readyInfo.Protocol} build={readyInfo.Build} mapHash={mapHash} wireTableHash={wireTableHash}");
		Console.Out.WriteLine(readyInfo.ToReadyLine());
		Console.Out.Flush();

		_tickLoop = new ServerTickLoop(simulation, _log, BroadcastSnapshots);
		_tickLoop.Start();

		_acceptThread = new Thread(AcceptLoop)
		{
			IsBackground = true,
			Name = "ListenServerAccept"
		};
		_acceptThread.Start();
	}

	public bool WaitForShutdown(int millisecondsTimeout = Timeout.Infinite) => _shutdownRequested.Wait(millisecondsTimeout);

	public void Stop()
	{
		if (!IsRunning) return;
		IsRunning = false;

		_shutdownRequested.Set();
		_transport.Dispose();
		ClosePeers();
		_tickLoop?.Stop();
		JoinThreads();
	}

	public void Dispose()
	{
		if (_disposed) return;
		_disposed = true;

		Stop();
		_transport.Dispose();
		_shutdownRequested.Dispose();

		_tickLoop?.Dispose();
		_tickLoop = null;
		_simulation?.Dispose();
		_simulation = null;
	}

	private void AcceptLoop()
	{
		while (!_shutdownRequested.IsSet)
		{
			if (!_transport.TryAccept(out IClientTransport? peer) || peer is null) break;

			lock (_peersLock)
			{
				_peers.Add(peer);
			}

			var peerThread = new Thread(() => ServePeer(peer))
			{
				IsBackground = true,
				Name = "ListenServerPeer"
			};

			lock (_peersLock)
			{
				_peerThreads.Add(peerThread);
			}

			peerThread.Start();
		}
	}

	private void ServePeer(IClientTransport peer)
	{
		try
		{
			if (!TryHandshake(peer)) return;
			if (!TrySeatPeer(peer)) return;

			while (!_shutdownRequested.IsSet)
			{
				NetworkFrame frame;
				try
				{
					if (!peer.TryReceive(Timeout.Infinite, out frame)) return;
				}
				catch (IOException exception)
				{
					_log.Write($"peer read ended: {exception.Message}");
					return;
				}
				catch (InvalidDataException exception)
				{
					_log.Write($"peer frame refused: {exception.Message}");
					return;
				}

				if (frame.Kind == NetworkFrameKind.Shutdown)
				{
					_log.Write("shutdown frame received");
					_shutdownRequested.Set();
					return;
				}

				if (frame.Kind == NetworkFrameKind.Command)
				{
					HandleCommand(peer, in frame);
					continue;
				}

				if (frame.Kind == NetworkFrameKind.SnapshotAck)
				{
					NoteSnapshotAck(in frame);
					continue;
				}

				if (frame.Kind == NetworkFrameKind.BaselineRequest)
				{
					RequestBaseline(peer);
					continue;
				}

				_log.Write($"refusing a {frame.Kind} frame of {frame.PayloadLength} bytes: this build answers hellos, orders, acknowledgements, baseline requests and a shutdown");
				return;
			}
		}
		catch (Exception exception) when (exception is IOException or ObjectDisposedException)
		{
			_log.Write($"peer dropped: {exception.Message}");
		}
		finally
		{
			peer.Close();
			lock (_peersLock)
			{
				_peers.Remove(peer);
				_seatedPeers.Remove(peer);
				_peersNeedingBaseline.Remove(peer);
			}

			_simulation?.ForgetPeer(peer.PeerId);
		}
	}

	/// <summary>
	///     Seats a peer on the roster and tells it which player it is. A peer with no slot owns nothing, so it is
	///     refused here rather than allowed to send orders that would all be rejected one by one.
	/// </summary>
	private bool TrySeatPeer(IClientTransport peer)
	{
		ServerSimulation? simulation = _simulation;
		if (simulation == null) return false;

		if (!simulation.TryAssignPlayerSlot(peer.PeerId, out int playerSlot, out int team))
		{
			_log.Write($"peer {peer.PeerId} refused: every player slot this map declares is taken");
			return false;
		}

		var assignment = new PeerRosterAssignment
		{
			PeerId = peer.PeerId,
			PlayerSlot = playerSlot,
			Team = team
		};

		peer.Send(NetworkFrameKind.Roster, MemoryPackSerializer.Serialize(assignment));
		_log.Write($"peer {peer.PeerId} seated as player {playerSlot} (team {team})");

		lock (_peersLock)
		{
			_seatedPeers.Add(peer);
			_peersNeedingBaseline.Add(peer);
		}

		return true;
	}

	/// <summary>
	///     Marks a peer to be described from scratch. The world is described to every peer on the same tick from the same
	///     sequence, so a baseline is owed to the peer that asked rather than assembled beside the stream, where it would
	///     arrive under a sequence the peer had already moved past.
	/// </summary>
	private void RequestBaseline(IClientTransport peer)
	{
		lock (_peersLock)
		{
			if (_seatedPeers.Contains(peer)) _peersNeedingBaseline.Add(peer);
		}
	}

	/// <summary>
	///     Describes the world to every seated peer for the tick that just finished. One order is sent to all of them:
	///     the tick is what they are all being told about, so a peer that joined a moment ago and one that has been here
	///     since the start agree on which world they are looking at.
	/// </summary>
	private void BroadcastSnapshots(int sequence)
	{
		if (_simulation == null) return;

		IClientTransport[] seatedPeers;
		bool[] needsBaseline;
		lock (_peersLock)
		{
			if (_seatedPeers.Count == 0) return;
			seatedPeers = _seatedPeers.ToArray();
			needsBaseline = new bool[seatedPeers.Length];
			for (int index = 0; index < seatedPeers.Length; index++)
			{
				needsBaseline[index] = _peersNeedingBaseline.Remove(seatedPeers[index]);
			}
		}

		for (int index = 0; index < seatedPeers.Length; index++)
		{
			SendSnapshot(seatedPeers[index], sequence, needsBaseline[index]);
		}

		SendPresentation(seatedPeers);
	}

	/// <summary>
	///     Tells each peer what the map asked its screen to show this tick. A request addressed to one player is delivered
	///     only to the peer acting as that player; one addressed to everyone goes to all of them. Nothing here depends on
	///     who is hosting, so a client is told what to show rather than deciding for itself.
	/// </summary>
	private void SendPresentation(IClientTransport[] seatedPeers)
	{
		ServerSimulation? simulation = _simulation;
		if (simulation == null) return;

		ServerPresentationEvent[] events = simulation.CollectPresentation();
		if (events.Length == 0) return;

		for (int peerIndex = 0; peerIndex < seatedPeers.Length; peerIndex++)
		{
			IClientTransport peer = seatedPeers[peerIndex];
			bool seated = simulation.TryGetPlayerSlot(peer.PeerId, out int playerSlot);

			for (int eventIndex = 0; eventIndex < events.Length; eventIndex++)
			{
				ServerPresentationEvent request = events[eventIndex];
				if (request.PlayerIndex != ServerPresentationEvent.Everyone && (!seated || request.PlayerIndex != playerSlot)) continue;

				try
				{
					peer.Send(NetworkFrameKind.Presentation, MemoryPackSerializer.Serialize(request.ToMessage()));
				}
				catch (Exception exception) when (exception is IOException or ObjectDisposedException)
				{
					_log.Write($"peer {peer.PeerId} could not be told what to show: {exception.Message}");
					break;
				}
			}
		}
	}

	private void SendSnapshot(IClientTransport peer, int sequence, bool forceBaseline)
	{
		ServerSimulation? simulation = _simulation;
		if (simulation == null) return;

		try
		{
			WorldSnapshot snapshot = simulation.BuildSnapshotForPeer(peer.PeerId, sequence, forceBaseline);
			byte[] serialized = MemoryPackSerializer.Serialize(snapshot);
			byte[] framed = snapshot.IsBaseline ? SnapshotWireFormat.EncodeBaseline(serialized) : SnapshotWireFormat.EncodeDelta(serialized);
			peer.Send(NetworkFrameKind.Snapshot, framed);
		}
		catch (Exception exception) when (exception is IOException or ObjectDisposedException)
		{
			_log.Write($"peer {peer.PeerId} could not be described: {exception.Message}");
		}
	}

	/// <summary>
	///     Records how far a peer has applied the world. The stream is reliable, so this is not what makes a peer catch
	///     up; it is what tells the match log whether a peer is actually keeping pace with the tick it is being sent.
	/// </summary>
	private void NoteSnapshotAck(in NetworkFrame frame)
	{
		SnapshotAck ack;
		try
		{
			ack = MemoryPackSerializer.Deserialize<SnapshotAck>(frame.Payload);
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
	private void HandleCommand(IClientTransport peer, in NetworkFrame frame)
	{
		ServerSimulation? simulation = _simulation;
		if (simulation == null) return;

		NetworkCommand command;
		try
		{
			command = MemoryPackSerializer.Deserialize<NetworkCommand>(frame.Payload);
		}
		catch (Exception exception) when (exception is IOException or InvalidDataException or ArgumentException)
		{
			_log.Write($"peer {peer.PeerId} sent a command that could not be read: {exception.Message}");
			return;
		}

		int commandId = command.CommandId;
		string rejection;
		if (!NetworkCommandLimits.TrySanitize(ref command, out rejection))
		{
			_log.Write($"peer {peer.PeerId} command {commandId} refused: {rejection}");
			SendOutcome(peer, commandId, false, rejection);
			return;
		}

		ServerCommandResult result = simulation.ApplyCommand(peer.PeerId, command);
		bool accepted = !result.HasRejection;

		_log.Write(accepted
			? $"peer {peer.PeerId} command {commandId} '{command.CommandType}' accepted"
			: $"peer {peer.PeerId} command {commandId} '{command.CommandType}' refused: {result.RejectionReason}");

		SendOutcome(peer, commandId, accepted, result.RejectionReason);
	}

	private void SendOutcome(IClientTransport peer, int commandId, bool accepted, string rejectionReason)
	{
		var outcome = new CommandOutcome
		{
			CommandId = commandId,
			Accepted = accepted,
			RejectionReason = rejectionReason ?? string.Empty
		};

		peer.Send(NetworkFrameKind.CommandOutcome, MemoryPackSerializer.Serialize(outcome));
	}

	private bool TryHandshake(IClientTransport peer)
	{
		NetworkFrame frame;
		try
		{
			if (!peer.TryReceive(RealmProtocol.HandshakeTimeoutMilliseconds, out frame))
			{
				_log.Write("peer closed before its hello");
				return false;
			}
		}
		catch (Exception exception) when (exception is IOException or InvalidDataException)
		{
			_log.Write($"peer hello failed: {exception.Message}");
			return false;
		}

		if (frame.Kind != NetworkFrameKind.ClientHello)
		{
			_log.Write($"refusing a {frame.Kind} frame where a hello was expected");
			return false;
		}

		ClientHello hello = MemoryPackSerializer.Deserialize<ClientHello>(frame.Payload);
		string rejection = DescribeHandshakeRejection(in hello);

		var response = new ServerHello
		{
			Protocol = RealmProtocol.Version,
			Build = RealmVersion.GameBuildNumber,
			Accepted = rejection.Length == 0,
			Rejection = rejection
		};

		peer.Send(NetworkFrameKind.ServerHello, MemoryPackSerializer.Serialize(response));

		if (rejection.Length > 0)
		{
			_log.Write($"peer refused: {rejection}");
			return false;
		}

		_log.Write($"peer accepted protocol={hello.Protocol} build={hello.Build}");
		return true;
	}

	private string DescribeHandshakeRejection(in ClientHello hello)
	{
		if (hello.Protocol != RealmProtocol.Version) return "server_rejected_protocol";
		if (!string.Equals(hello.Build, RealmVersion.GameBuildNumber, StringComparison.Ordinal)) return "server_rejected_build";
		if (!string.Equals(hello.WireTableHash, ReplicatedComponents.ComputeWireTableHash(), StringComparison.Ordinal)) return "server_rejected_wire_table";
		if (!TokenMatches(hello.Token)) return "server_rejected_token";
		return string.Empty;
	}

	private bool TokenMatches(string? presentedToken)
	{
		if (presentedToken == null) return false;

		byte[] presented = Encoding.UTF8.GetBytes(presentedToken);
		byte[] expected = Encoding.UTF8.GetBytes(_arguments.Token);
		return CryptographicOperations.FixedTimeEquals(presented, expected);
	}

	private void ClosePeers()
	{
		IClientTransport[] peers;
		lock (_peersLock)
		{
			peers = _peers.ToArray();
		}

		for (int index = 0; index < peers.Length; index++)
		{
			peers[index].Close();
		}
	}

	private void JoinThreads()
	{
		Thread? acceptThread = _acceptThread;
		_acceptThread = null;
		JoinThread(acceptThread);

		Thread[] peerThreads;
		lock (_peersLock)
		{
			peerThreads = _peerThreads.ToArray();
			_peerThreads.Clear();
		}

		for (int index = 0; index < peerThreads.Length; index++)
		{
			JoinThread(peerThreads[index]);
		}
	}

	private static void JoinThread(Thread? thread)
	{
		if (thread == null || thread == Thread.CurrentThread) return;
		thread.Join();
	}
}
