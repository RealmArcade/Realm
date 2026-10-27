using System.Security.Cryptography;
using System.Text;
using MemoryPack;
using Realm.Ecs;
using Realm.Ecs.Replication;
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

	private ServerSimulation? _simulation;
	private double _nextTickTime;
	private long _lastLoggedAckSequence;
	private bool _readyAnnounced;
	private bool _disposed;

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

		LoadedMap map = MapWorldLoader.Load(_arguments.MapPath);
		try
		{
			var paths = ServerMatchPaths.From(_arguments);
			_simulation = new ServerSimulation(map, paths, _log, _arguments.Seed, _arguments.Cheats);
			_simulation.PeerDropped += DisconnectPeer;
			_log.Write($"map '{paths.MapName}' keeps its saved data under '{_simulation.MapSavedDataDirectory()}'");
			_log.Write(_simulation.Describe());
			for (int index = 0; index < map.Warnings.Count; index++)
			{
				_log.Write($"map warning: {map.Warnings[index]}");
			}

			if (!_transport.TryStart(EcsWorld.MAX_PLAYERS, this, out string failure))
			{
				throw new IOException($"the match could not listen for peers: {failure}");
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

		if (_nextTickTime <= 0) _nextTickTime = nowSeconds;

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
	///     Accepts or refuses a peer, and seats it on a player slot when it is accepted. A peer that is refused is told
	///     why before its connection ends, so the screen it belongs to can say what went wrong.
	/// </summary>
	private void HandleHello(int peerId, ReadOnlySpan<byte> payload)
	{
		ServerSimulation? simulation = _simulation;
		if (simulation == null) return;

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

		// What the map asked a screen to show this tick is collected whether or not anyone is listening, so a request
		// that nobody could receive is dropped rather than piling up behind the next one.
		ServerPresentationEvent[] presentation = simulation.CollectPresentation();
		if (_seatedPeers.Count == 0) return;

		int[] peerIds = new int[_seatedPeers.Count];
		_seatedPeers.Keys.CopyTo(peerIds, 0);

		for (int index = 0; index < peerIds.Length; index++)
		{
			SendSnapshot(peerIds[index], sequence, simulation.ConsumeBaselineRequest(peerIds[index]));
		}

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
			}
		}
	}

	private void SendSnapshot(int peerId, int sequence, bool forceBaseline)
	{
		ServerSimulation? simulation = _simulation;
		if (simulation == null) return;

		WorldSnapshot snapshot = simulation.BuildSnapshotForPeer(peerId, sequence, forceBaseline);
		byte[] serialized = MemoryPackSerializer.Serialize(snapshot);
		byte[] framed = snapshot.IsBaseline ? SnapshotWireFormat.EncodeBaseline(serialized) : SnapshotWireFormat.EncodeDelta(serialized);
		_transport.Send(peerId, NetworkFrameKind.Snapshot, framed);
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
