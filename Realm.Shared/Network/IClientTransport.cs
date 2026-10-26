using System;

namespace Realm.Shared.Network;

public interface IClientTransport : IDisposable
{
	bool IsConnected { get; }

	void Send(NetworkFrameKind kind, ReadOnlySpan<byte> payload);

	bool TryReceive(out NetworkFrame frame);

	void Close();
}
