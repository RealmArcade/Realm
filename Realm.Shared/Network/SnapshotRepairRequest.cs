using MemoryPack;

namespace Realm.Shared.Network;

[MemoryPackable]
public partial struct SnapshotRepairRequest
{
	public int FirstSequence;

	public int Count;
}
