using System;
using Godot;
using Realm.Shared.Network;

namespace Realm.ListenServerHost.Network;

public class EnetServerTransport : IServerTransport
{
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
	///     Binds are retried at all because a port a previous match let go of is not free the instant its process ends. They
	///     are retried for a second and no longer: this runs on the authority's main thread before it can announce itself
	///     ready, and a port held by something else is not going to be released because the match waited six seconds for it.
	/// </summary>
	private const int BindAttempts = 10;

	private const int BindRetryMilliseconds = 100;

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

	public bool TryReadPeerLink(int peerId, out PeerLinkStatistics statistics)
	{
		statistics = default;
		if (_peer == null || peerId <= 0) return false;

		ENetPacketPeer? peer = _peer.GetPeer(peerId);
		if (peer == null) return false;
		if (peer.GetState() != ENetPacketPeer.PeerState.Connected) return false;

		int roundTripMilliseconds = (int)Math.Round(peer.GetStatistic(ENetPacketPeer.PeerStatistic.RoundTripTime));
		int jitterMilliseconds = (int)Math.Round(peer.GetStatistic(ENetPacketPeer.PeerStatistic.RoundTripTimeVariance));
		float packetLossPercent = (float)peer.GetStatistic(ENetPacketPeer.PeerStatistic.PacketLoss) / 65536f * 100f;

		statistics = new PeerLinkStatistics(
			Math.Clamp(roundTripMilliseconds, 0, int.MaxValue),
			Math.Clamp(jitterMilliseconds, 0, int.MaxValue),
			Math.Clamp(packetLossPercent, 0f, 100f));
		return true;
	}

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
