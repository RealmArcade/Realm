using System.Security.Cryptography;
using System.Text;
using MemoryPack;
using Realm.Ecs.Replication;
using Realm.Shared;
using Realm.Shared.Maps;
using Realm.Shared.Network;

namespace Realm.ListenServer;

public class ListenServerHost : IServerHost
{
	private const int AcceptBacklog = 16;

	private readonly ServerArguments _arguments;
	private readonly ServerLog _log;
	private readonly IServerTransport _transport;
	private readonly ManualResetEventSlim _shutdownRequested = new(false);
	private readonly List<IClientTransport> _peers = new();
	private readonly List<Thread> _peerThreads = new();
	private readonly object _peersLock = new();

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
			_simulation = new ServerSimulation(map, _log);
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

		_tickLoop = new ServerTickLoop(simulation, _log);
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

				_log.Write($"refusing a {frame.Kind} frame of {frame.PayloadLength} bytes: this build answers nothing but a shutdown frame");
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
			}
		}
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
