using System;
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
	/// <summary>The address a match that plays by itself is reachable on, and the only one a peer on this machine can name.</summary>
	private const string LoopbackAddress = "127.0.0.1";

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

	public int Port { get; private set; }

	/// <summary>
	///     How long a match waits for a port it was told to use to be released. A host hands its lobby over to the
	///     process running the match, so the socket was held by the client a moment ago and may take a frame or two to go.
	/// </summary>
	private const int BindAttempts = 60;

	/// <summary>How long one of those attempts waits before asking for the port again.</summary>
	private const int BindRetryMilliseconds = 100;

	/// <summary>
	///     Starts listening. A match started by a client asks for port zero, which ENet binds to whatever port this
	///     machine has free, and the port it got is reported through the readiness line so the client can reach it. A
	///     match this machine plays by itself binds the loopback address, so no other machine can open a connection to
	///     it however it learned the port.
	/// </summary>
	public bool TryStart(int maximumPeers, bool loopbackOnly, IServerTransportSink sink, out string failure)
	{
		failure = string.Empty;
		_sink = sink;

		var peer = new ENetMultiplayerPeer();
		if (loopbackOnly) peer.SetBindIP(LoopbackAddress);

		Error error = peer.CreateServer(_requestedPort, maximumPeers);
		for (int attempt = 0; error != Error.Ok && _requestedPort != 0 && attempt < BindAttempts; attempt++)
		{
			OS.DelayMsec(BindRetryMilliseconds);
			peer.Dispose();
			peer = new ENetMultiplayerPeer();
			if (loopbackOnly) peer.SetBindIP(LoopbackAddress);
			error = peer.CreateServer(_requestedPort, maximumPeers);
		}

		if (error != Error.Ok)
		{
			peer.Dispose();
			failure = loopbackOnly
				? $"ENet could not listen on {LoopbackAddress}:{_requestedPort}: {error}"
				: $"ENet could not listen on port {_requestedPort}: {error}";
			return false;
		}

		_peer = peer;
		Port = peer.GetHost().GetLocalPort();
		if (Port == 0)
		{
			failure = "ENet is listening but did not report the port it took";
			Stop();
			return false;
		}

		_channel.FrameArrived += OnFrameArrived;
		Attach();
		return true;
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

	public void Send(int peerId, NetworkFrameKind kind, byte[] payload, FrameDelivery delivery) => _channel.Send(peerId, kind, payload, delivery);

	public void Disconnect(int peerId)
	{
		if (_peer == null) return;

		_peer.DisconnectPeer(peerId);
	}

	/// <summary>
	///     Sends one datagram from the very socket the lobby is advertised on. A hole punch has to leave from that socket:
	///     a punch sent from any other port opens a mapping no peer can reach this authority through.
	/// </summary>
	public bool SendToAddress(string address, int port, ReadOnlySpan<byte> payload)
	{
		if (_peer == null || string.IsNullOrWhiteSpace(address) || port <= 0) return false;

		var host = _peer.GetHost();
		if (host == null) return false;

		host.SocketSend(address, port, payload.ToArray());
		return true;
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
