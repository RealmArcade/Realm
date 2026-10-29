using MemoryPack;

namespace Realm.Shared.Network;

[MemoryPackable]
public partial struct HandshakeChallenge
{
	public string Nonce;
}
