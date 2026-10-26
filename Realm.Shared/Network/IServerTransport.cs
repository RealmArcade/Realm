using System;

namespace Realm.Shared.Network;

public interface IServerTransport : IDisposable
{
	int BoundPort { get; }

	void Start(int backlog);

	bool TryAccept(out IClientTransport? peer);
}
