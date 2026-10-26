using System;

namespace Realm.Shared.Network;

public interface IServerTransport : IDisposable
{
	int BoundPort { get; }

	bool TryAccept(out IClientTransport? peer);
}
