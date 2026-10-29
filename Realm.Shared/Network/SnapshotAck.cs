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

	/// <summary>
	///     The round trip this screen measured when it answered, in milliseconds, or zero before it has a sample. An
	///     acknowledgement names a sequence this peer applied one round trip ago, so the match cannot tell how far behind a
	///     screen is from that sequence alone: the link's own latency has to be measured off it. This is the only place the
	///     authority can learn it, because latency is a property of the peer's connection and the transport reports only
	///     what it was handed.
	/// </summary>
	public int RoundTripMilliseconds;
}
