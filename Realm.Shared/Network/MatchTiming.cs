namespace Realm.Shared.Network;

public static class MatchTiming
{
	public const int SimulationTickRateHz = 30;

	public const int SnapshotRateHz = 30;

	public const double FixedTickSeconds = 1.0 / SimulationTickRateHz;

	public const float SnapshotIntervalSeconds = 1.0f / SnapshotRateHz;

	public const int BaselineRequestCooldownSeconds = 1;

	public const int BaselineRequestCooldownTicks = SimulationTickRateHz * BaselineRequestCooldownSeconds;

	public const int BaselineRequestCooldownMilliseconds = 1000 * BaselineRequestCooldownSeconds;

	/// <summary>
	///     How long the authority waits before answering another repair to the same peer. A screen holding a run it asked
	///     about has to allow for this besides the trip, or it gives up on a repair the match is still withholding.
	/// </summary>
	public const int SnapshotRepairCooldownTicks = SimulationTickRateHz / 4;

	public const int SnapshotRepairCooldownMilliseconds = 1000 * SnapshotRepairCooldownTicks / SimulationTickRateHz;

	/// <summary>
	///     How many descriptions the match keeps per peer for answering a hole by name, and therefore the widest run a
	///     screen may ask it to send again. It is stated here because both sides have to agree on it: a screen that
	///     asked for less than the match holds paid for a baseline where a handful of deltas would have done, which is a
	///     whole world over the reliable stream instead of the few descriptions that went missing. It also has to outlast
	///     the repair itself, because a run that has rotated out of the ring cannot be answered by name at all: a hole
	///     takes a round trip to fill and a retransmit on each leg of it, so the window is sized for a slow link rather
	///     than for a fast one and holds some 1.6 seconds of a match at thirty descriptions a second.
	/// </summary>
	public const int SnapshotHistoryDepth = 48;

	public static float TicksToSeconds(int ticks) => (float)(ticks * FixedTickSeconds);

	public static int SecondsToTicks(float seconds) => (int)(seconds / FixedTickSeconds);
}
