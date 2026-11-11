using MemoryPack;

namespace Realm.Shared.Network;

/// <summary>
///     Why a screen is asking for descriptions again. Both a link that lost datagrams and a screen that stopped reading
///     its own stream name a run they cannot apply, and only the first says anything about the connection: the match
///     sends a peer that loses descriptions every one of them twice, which is bandwidth spent on a fault a healthy link
///     does not have.
/// </summary>
public enum SnapshotRepairReason
{
	/// <summary>A run of descriptions was sent to this screen and never arrived.</summary>
	WireLoss,

	/// <summary>No description has arrived for long enough that the stream is presumed stopped.</summary>
	Silence
}

[MemoryPackable]
public partial struct SnapshotRepairRequest
{
	public int FirstSequence;

	public int Count;

	public SnapshotRepairReason Reason;
}
