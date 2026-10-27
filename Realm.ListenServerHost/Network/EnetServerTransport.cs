using System.Net;
using System.Net.Sockets;
using Godot;
using Realm.Shared.Network;

namespace Realm.ListenServerHost.Network;

/// <summary>
///     The listen server's transport: the ENet peer a match listens on, wired to the channel the clients speak to. ENet
///     is polled by the engine this process runs inside, so this type never reads a socket itself; it is told what
///     arrived and answers by peer id.
/// </summary>
public class EnetServerTransport : IServerTransport
{
	/// <summary>How many ports are tried before a match gives up on finding one that is free.</summary>
	private const int PortAttempts = 5;

	private readonly ServerNetworkChannel _channel;
	private readonly int _requestedPort;

	private ENetMultiplayerPeer? _peer;
	private IServerTransportSink? _sink;
	private bool _connected;
	private bool _disposed;

	public EnetServerTransport(ServerNetworkChannel channel, int requestedPort)
	{
		_channel = channel;
		_requestedPort = requestedPort;
	}

	public int BoundPort { get; private set; }

	public bool HasPeers => _peer != null && _channel.Multiplayer.GetPeers().Length > 0;

	/// <summary>
	///     Starts listening. A match started by a client asks for port zero, which means "any port this machine has
	///     free", and reports the one it was given through the readiness line.
	/// </summary>
	public bool TryStart(int maximumPeers, IServerTransportSink sink, out string failure)
	{
		failure = string.Empty;
		_sink = sink;

		_channel.FrameArrived += OnFrameArrived;

		for (int attempt = 0; attempt < PortAttempts; attempt++)
		{
			int port = _requestedPort != 0 ? _requestedPort : FindFreePort();
			if (port == 0)
			{
				failure = "no free port could be found for the match";
				return false;
			}

			var peer = new ENetMultiplayerPeer();
			Error error = peer.CreateServer(port, maximumPeers);
			if (error != Error.Ok)
			{
				peer.Dispose();
				if (_requestedPort != 0)
				{
					failure = $"ENet could not listen on port {port}: {error}";
					return false;
				}

				continue;
			}

			_peer = peer;
			BoundPort = port;
			Attach();
			return true;
		}

		failure = "ENet could not listen on any of the ports that were tried";
		return false;
	}

	/// <summary>
	///     Gives the match's peer to the engine, which is what makes the channel's RPC route to it, and listens for the
	///     peers the engine reports coming and going.
	/// </summary>
	private void Attach()
	{
		_channel.Multiplayer.MultiplayerPeer = _peer;
		_channel.Multiplayer.PeerConnected += OnPeerConnected;
		_channel.Multiplayer.PeerDisconnected += OnPeerDisconnected;
		_connected = true;
	}

	/// <summary>
	///     Asks the operating system which UDP port it would give this process, then hands that port to ENet. A match
	///     started by a client cannot be told its port ahead of time and must report one it actually got.
	/// </summary>
	private static int FindFreePort()
	{
		try
		{
			using var probe = new UdpClient(new IPEndPoint(IPAddress.Any, 0));
			return ((IPEndPoint)probe.Client.LocalEndPoint!).Port;
		}
		catch (SocketException)
		{
			return 0;
		}
	}

	public void Send(int peerId, NetworkFrameKind kind, byte[] payload) => _channel.Send(peerId, kind, payload);

	public void Disconnect(int peerId)
	{
		if (_peer == null) return;

		_peer.DisconnectPeer(peerId);
	}

	public void Stop()
	{
		if (!_connected) return;
		_connected = false;

		_channel.Multiplayer.PeerConnected -= OnPeerConnected;
		_channel.Multiplayer.PeerDisconnected -= OnPeerDisconnected;
		_channel.FrameArrived -= OnFrameArrived;

		_channel.Multiplayer.MultiplayerPeer = null;
		_peer?.Close();
		_peer?.Dispose();
		_peer = null;
	}

	private void OnFrameArrived(int peerId, NetworkFrameKind kind, byte[] payload) => _sink?.OnFrameReceived(peerId, kind, payload);

	private void OnPeerConnected(long peerId) => _sink?.OnPeerConnected((int)peerId);

	private void OnPeerDisconnected(long peerId) => _sink?.OnPeerDisconnected((int)peerId, "the peer left");

	public void Dispose()
	{
		if (_disposed) return;
		_disposed = true;

		Stop();
	}
}
