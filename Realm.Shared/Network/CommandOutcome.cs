using MemoryPack;

namespace Realm.Shared.Network;

/// <summary>
///     What the authority decided about one command. A client predicts its own units, so an accepted command tells it
///     to keep the prediction it already made and a rejected one tells it to roll that prediction back.
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
