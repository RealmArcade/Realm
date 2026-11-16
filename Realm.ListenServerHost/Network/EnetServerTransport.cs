using System;
using Godot;
using Realm.Shared.Network;
using Realm.Shared.Network.Relay;
using Realm.Shared.Reachability;

namespace Realm.ListenServerHost.Network;

public class EnetServerTransport : IServerTransport
{
	private const string LoopbackAddress = "127.0.0.1";

	private const int LongSilenceTimeoutLimit = 32;

	private const int LongSilenceTimeoutMilliseconds = 86_400_000;

	private readonly ServerNetworkChannel _channel;
	private readonly int _requestedPort;

	private readonly bool _allowLongSilence;

	private ENetMultiplayerPeer? _peer;
	private IServerTransportSink? _sink;
	private RelayPeerDemux? _relayedPeers;
	private bool _connected;
	private bool _disposed;

	public EnetServerTransport(ServerNetworkChannel channel, int requestedPort, bool allowLongSilence = false)
	{
		_channel = channel;
		_requestedPort = requestedPort;
		_allowLongSilence = allowLongSilence;
	}

	public int Port { get; private set; }

	/// <summary>
	///     The port this transport asked for, which is the one a lobby is meant to be found on. It is stated separately from
	///     <see cref="Port" /> because the two differ when the requested port was already held by something else on this
	///     machine, which is what hosting a second lobby from one computer looks like.
	/// </summary>
	public int RequestedPort => _requestedPort;

	/// <summary>
	///     Whether this transport is listening somewhere other than the port it asked for, so that the lobby can say so
	///     rather than leaving a host to compare two numbers on their own.
	/// </summary>
	public bool IsListeningOnAnotherPort => _requestedPort != 0 && Port != _requestedPort;

	/// <summary>
	///     How many times the requested port is tried before the operating system is asked for one instead. A retry is for the
	///     window where the last match's socket has been released and the next bind has not happened yet; a port another
	///     program holds is not going to be released because this waited, so the second attempt is the last one and the
	///     fallback answers that case. This runs on the authority's main thread before it can announce itself ready, so the
	///     whole budget is a tenth of a second.
	/// </summary>
	private const int BindAttempts = 2;

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

		// A port this machine already holds is not going to be released by waiting, and two lobbies on one computer is a
		// normal thing to do, so the port the lobby asked for is given up for one the operating system picks. What the lobby
		// is listening on is what it publishes, so a lobby that moved is still found.
		if (error != Error.Ok && _requestedPort != 0)
		{
			peer.Dispose();
			peer = new ENetMultiplayerPeer();
			if (loopbackOnly) peer.SetBindIP(LoopbackAddress);
			error = peer.CreateServer(0, maximumPeers);
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
		_relayedPeers = new RelayPeerDemux(Port);
		Attach();
		return true;
	}

	/// <summary>
	///     Opens one relayed entry in front of this transport. A lobby that names no volunteers never calls this, and the
	///     transport then listens for exactly the peers it always did.
	/// </summary>
	/// <param name="plan">The volunteers both peers were offered and the ticket their keys come from.</param>
	/// <param name="failure">Why no entry was opened.</param>
	/// <returns>Whether the connection is being carried.</returns>
	public bool TryOpenRelayedEntry(RelayConnectionPlan plan, out string failure)
	{
		if (_relayedPeers == null)
		{
			failure = "this transport is not listening, so there is nothing for a relayed connection to be seated in";
			return false;
		}

		return _relayedPeers.TryOpen(plan, out failure);
	}

	/// <summary>How many relayed connections this transport is carrying.</summary>
	public int RelayedEntryCount => _relayedPeers?.Count ?? 0;

	/// <summary>States each relayed connection and the paths behind it.</summary>
	/// <returns>One block per connection, or a line stating that the transport is direct only.</returns>
	public string DescribeRelayedEntries() => _relayedPeers?.Describe() ?? "relay carrier=direct";

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

		byte[] packet = payload.ToArray();
		for (int attempt = 0; attempt < ReachabilityPunch.DatagramSendCount; attempt++)
		{
			host.SocketSend(address, port, packet);
		}

		return true;
	}

	public void Stop()
	{
		if (!_connected) return;
		_connected = false;

		_channel.Multiplayer.PeerConnected -= OnPeerConnected;
		_channel.Multiplayer.PeerDisconnected -= OnPeerDisconnected;
		_channel.FrameArrived -= OnFrameArrived;

		_relayedPeers?.Dispose();
		_relayedPeers = null;

		_channel.Multiplayer.MultiplayerPeer = null;
		_peer?.Close();
		_peer?.Dispose();
		_peer = null;
	}

	private void OnFrameArrived(int peerId, NetworkFrameKind kind, byte[] payload) => _sink?.OnFrameReceived(peerId, kind, payload);

	private void OnPeerConnected(long peerId)
	{
		ApplyLongSilenceTimeout((int)peerId);
		_sink?.OnPeerConnected((int)peerId);
	}

	private void ApplyLongSilenceTimeout(int peerId)
	{
		if (!_allowLongSilence || _peer == null) return;

		ENetPacketPeer? peer = _peer.GetPeer(peerId);
		peer?.SetTimeout(LongSilenceTimeoutLimit, LongSilenceTimeoutMilliseconds, LongSilenceTimeoutMilliseconds);
	}

	private void OnPeerDisconnected(long peerId) => _sink?.OnPeerDisconnected((int)peerId, "the peer left");

	public void Dispose()
	{
		if (_disposed) return;
		_disposed = true;

		Stop();
	}
}
