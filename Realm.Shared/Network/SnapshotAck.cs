using MemoryPack;

namespace Realm.Shared.Network;

/// <summary>
///     The newest snapshot a peer has applied, with a bit per sequence behind it stating whether that one was applied
///     whole. The mask is what lets the authority tell a peer that kept up from one that discarded part of a payload.
/// </summary>
[MemoryPackable]
public partial struct SnapshotAck
{
	public int Sequence;
	public ulong AppliedMask;
}
