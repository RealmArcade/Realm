using MemoryPack;

namespace Realm.Shared.Network;

[MemoryPackable]
public partial struct ServerHello
{
	public int Protocol;
	public string Build;
	public bool Accepted;
	public string Rejection;
}
