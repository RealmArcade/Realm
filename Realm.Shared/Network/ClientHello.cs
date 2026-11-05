using MemoryPack;

namespace Realm.Shared.Network;

[MemoryPackable]
public partial struct ClientHello
{
	public string Build;

	public string TokenProof;

	public string WireTableHash;

	public string MapHash;

	public string Language;

	public string PlayerName;

	public long AccountId;

	public string SessionToken;

	public string AdminProof;
}
