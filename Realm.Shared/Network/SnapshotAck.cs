using MemoryPack;

namespace Realm.Shared.Network;

[MemoryPackable]
public partial struct SnapshotAck
{
	public int Sequence;
	public ulong AppliedMask;

	public int RoundTripMilliseconds;
}
