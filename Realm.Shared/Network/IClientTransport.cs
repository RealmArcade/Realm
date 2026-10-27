using System;

namespace Realm.Shared.Network;

public interface IClientTransport : IDisposable
{
	/// <summary>
	///     The connection identity the accepting side assigned. Commands and roster entries are keyed by it, so a peer
	///     that has not been accepted yet, or a client that has not yet been told which peer it is, reports zero.
	/// </summary>
	int PeerId { get; }

	bool IsConnected { get; }

	void Send(NetworkFrameKind kind, ReadOnlySpan<byte> payload);

	bool TryReceive(int timeoutMilliseconds, out NetworkFrame frame);

	void Close();
}
