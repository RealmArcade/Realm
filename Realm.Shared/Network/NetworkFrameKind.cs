namespace Realm.Shared.Network;

/// <summary>
///     Every kind of frame that crosses the loopback transport. The numbers are part of the wire protocol, so a kind
///     keeps its value once a build ships it.
/// </summary>
public enum NetworkFrameKind : byte
{
	ClientHello = 1,
	ServerHello = 2,
	Shutdown = 3,

	/// <summary>A player's order, as the client authored it.</summary>
	Command = 4,

	/// <summary>Whether the authority took one command, and why not when it did not.</summary>
	CommandOutcome = 5,

	/// <summary>The world as the peer's player can see it.</summary>
	Snapshot = 6,

	/// <summary>The player slot the authority gave the peer that received it.</summary>
	Roster = 7,

	/// <summary>The newest snapshot a peer has applied, so the authority knows what it no longer has to repeat.</summary>
	SnapshotAck = 8,

	/// <summary>A peer asking to be described from scratch, which is what a client that lost its place does.</summary>
	BaselineRequest = 9
}
