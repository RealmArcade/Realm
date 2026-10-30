using MemoryPack;

namespace Realm.Shared.Network;

[MemoryPackable]
public partial struct CommandOutcome
{
	public int CommandId;
	public bool Accepted;

	public string RejectionReason;
}

[MemoryPackable]
public partial struct PeerRosterAssignment
{
	public int PeerId;
	public int PlayerSlot;
	public int Team;
}
