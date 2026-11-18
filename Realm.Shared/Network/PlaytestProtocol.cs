using MemoryPack;

namespace Realm.Shared.Network;

/// <summary>
///     What a player in a hosted match tells the authority about the map they just played. It states the answer and
///     nothing else: the SteamID the vote is recorded against is the one the handshake already established for that
///     connection, because a client that could name an identity could vote for somebody else.
/// </summary>
[MemoryPackable]
public partial struct PlaytestVoteMessage
{
	/// <summary>The vote, as <see cref="Realm.Shared.Steam.PlaytestVote" />.</summary>
	public int Vote;
}

/// <summary>
///     The authority's answer to a vote: whether it counted, and the opaque code the tester copies and hands to the map's
///     author. The code is minted by the authority from the identity it seated the voter as.
/// </summary>
[MemoryPackable]
public partial struct PlaytestVoteAnswerMessage
{
	/// <summary>Whether the vote was recorded.</summary>
	public bool Accepted;

	/// <summary>The code the tester copies, or empty when the vote was refused.</summary>
	public string Code;

	/// <summary>Why the vote was refused, empty when it counted.</summary>
	public string Failure;
}

/// <summary>
///     One accepted vote as the authority hands it to the screens in the match. Every seat receives it so a vote that was
///     cast in a lobby where the map's author is playing reaches that author's own ledger, and each screen stores it in the
///     ledger it keeps on its own machine.
/// </summary>
[MemoryPackable]
public partial struct PlaytestVoteRecordMessage
{
	/// <summary>The key of the map the vote is about.</summary>
	public string MapKey;

	/// <summary>The vote, as <see cref="Realm.Shared.Steam.PlaytestVote" />.</summary>
	public int Vote;

	/// <summary>The same opaque code the voter was given, which is what the ledger stores.</summary>
	public string Code;
}
