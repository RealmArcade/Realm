using System;

namespace Realm.Shared.Network;

public interface IServerTransportSink
{
	void OnPeerConnected(int peerId);

	void OnFrameReceived(int peerId, NetworkFrameKind kind, ReadOnlySpan<byte> payload);

	void OnPeerDisconnected(int peerId, string reason);
}

/// <summary>
///     What the transport itself has measured about one peer's link. ENet keeps these per connection, so they are read
///     rather than guessed: a round trip the authority observed, how far those trips land from each other, and how much
///     of what it sent that peer never acknowledged.
/// </summary>
public readonly record struct PeerLinkStatistics(int RoundTripMilliseconds, int JitterMilliseconds, float PacketLossPercent);

public interface IServerTransport : IDisposable
{
	int Port { get; }

	bool TryStart(int maximumPeers, bool loopbackOnly, IServerTransportSink sink, out string failure);

	void Send(int peerId, NetworkFrameKind kind, byte[] payload, FrameDelivery delivery = FrameDelivery.Reliable);

	void Disconnect(int peerId);

	bool SendToAddress(string address, int port, ReadOnlySpan<byte> payload);

	/// <summary>
	///     Reads the link quality of one connected peer, or answers false when this transport holds no measurement for it
	///     yet.
	/// </summary>
	bool TryReadPeerLink(int peerId, out PeerLinkStatistics statistics);

	void Stop();
}
