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

	/// <summary>The player slot the authority gave the peer that received it.</summary>
	Roster = 6
}
