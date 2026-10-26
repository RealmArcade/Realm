using MemoryPack;

namespace Realm.Shared.Network;

[MemoryPackable]
public partial struct ClientHello
{
	public int Protocol;
	public string Build;
	public string Token;
	public string WireTableHash;
}
