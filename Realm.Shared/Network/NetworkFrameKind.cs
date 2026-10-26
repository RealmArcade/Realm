namespace Realm.Shared.Network;

public enum NetworkFrameKind : byte
{
	ClientHello = 1,
	ServerHello = 2,
	Shutdown = 3
}
