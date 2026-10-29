using System;

namespace Realm.Shared.Network;

public interface IServerTransportSink
{
	void OnPeerConnected(int peerId);

	void OnFrameReceived(int peerId, NetworkFrameKind kind, ReadOnlySpan<byte> payload);

	void OnPeerDisconnected(int peerId, string reason);
}

public interface IServerTransport : IDisposable
{
	int Port { get; }

	bool TryStart(int maximumPeers, bool loopbackOnly, IServerTransportSink sink, out string failure);

	void Send(int peerId, NetworkFrameKind kind, byte[] payload, FrameDelivery delivery = FrameDelivery.Reliable);

	void Disconnect(int peerId);

	bool SendToAddress(string address, int port, ReadOnlySpan<byte> payload);

	void Stop();
}
