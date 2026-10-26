using System.Net;
using System.Net.Sockets;
using Realm.Shared.Network;

namespace Realm.ListenServer;

public class LoopbackServerTransport : IServerTransport
{
	private readonly TcpListener _listener;
	private readonly ServerLog _log;

	private int _nextPeerId = 1;
	private bool _disposed;

	public LoopbackServerTransport(int port, ServerLog log)
	{
		_log = log;
		_listener = new TcpListener(IPAddress.Loopback, port);
	}

	public int BoundPort => ((IPEndPoint)_listener.LocalEndpoint).Port;

	public void Start(int backlog)
	{
		_listener.Start(backlog);
		_log.Write($"listening on {IPAddress.Loopback}:{BoundPort}");
	}

	public bool TryAccept(out IClientTransport? peer)
	{
		peer = null;
		if (_disposed) return false;

		TcpClient client;
		try
		{
			client = _listener.AcceptTcpClient();
		}
		catch (SocketException exception)
		{
			_log.Write($"accept stopped: {exception.SocketErrorCode}");
			return false;
		}
		catch (InvalidOperationException)
		{
			return false;
		}

		var loopbackPeer = new LoopbackPeer(client, _nextPeerId++);
		_log.Write($"accepted peer {loopbackPeer.PeerId} from {client.Client.RemoteEndPoint}");
		peer = loopbackPeer;
		return true;
	}

	public void Dispose()
	{
		if (_disposed) return;
		_disposed = true;
		_listener.Stop();
	}
}
