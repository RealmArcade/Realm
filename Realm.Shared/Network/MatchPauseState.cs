using MemoryPack;

namespace Realm.Shared.Network;

/// <summary>
///     Whether the match is paused. The clock belongs to the process running the match, so a pause is a request that
///     process answers: a peer sends the state it wants and the authority replies with the state it holds, which is what
///     every client then shows. The same frame carries both directions so neither side can hold a pause the other was
///     never told about.
/// </summary>
[MemoryPackable]
public partial struct MatchPauseState
{
	/// <summary>Whether the match is paused.</summary>
	public bool Paused;

	/// <summary>
	///     The player slot that asked for the state the authority now holds, or nothing when the authority changed it
	///     itself. A request leaves this unset, because the authority names the requester.
	/// </summary>
	public int RequestedByPlayer;
}
