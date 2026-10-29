using MemoryPack;

namespace Realm.Shared.Network;

/// <summary>
///     What the authority decided about one command. A screen draws a route preview for an order until this answers it:
///     an accepted order is kept and the authority's own destination soon replaces the preview, while a refused one is
///     taken back by its command id.
/// </summary>
[MemoryPackable]
public partial struct CommandOutcome
{
	public int CommandId;
	public bool Accepted;

	/// <summary>Why the command was refused, empty when it was taken. This is a code, not a sentence to show.</summary>
	public string RejectionReason;
}

/// <summary>
///     The player slot the authority gave a peer. A peer cannot act as a player it was not assigned, so this is what
///     makes an order attributable rather than anonymous.
/// </summary>
[MemoryPackable]
public partial struct PeerRosterAssignment
{
	public int PeerId;
	public int PlayerSlot;
	public int Team;
}
