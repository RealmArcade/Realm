using System;

namespace Realm.Shared.Network;

public interface IServerHost : IDisposable
{
	bool IsRunning { get; }

	int Port { get; }

	void Start();

	void Stop();
}
