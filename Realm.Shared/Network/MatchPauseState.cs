using MemoryPack;

namespace Realm.Shared.Network;

[MemoryPackable]
public partial struct MatchPauseState
{
	public bool Paused;

	public int RequestedByPlayer;

	/// <summary>
	///     How many seconds remain before a paused match resumes for everyone, where zero means no resume has been asked
	///     for yet. A resume is a ready countdown rather than an instant flip, so every screen sees the same warning.
	/// </summary>
	public int ResumeCountdownSeconds;
}

/// <summary>
///     What the match answers one player about its own pause ability: how many pauses it has left this match, and how
///     long it must wait before pausing again. It carries why a pause was refused, if it was.
/// </summary>
[MemoryPackable]
public partial struct MatchPauseAllowance
{
	public const string RefusalCooldown = "cooldown";

	public const string RefusalExhausted = "exhausted";

	public int PausesRemaining;

	public int CooldownSecondsRemaining;

	public string Rejection;
}
