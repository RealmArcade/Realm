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
	///     whole world over the reliable stream instead of the few descriptions that went missing.
	/// </summary>
	public const int SnapshotHistoryDepth = 24;

	public static float TicksToSeconds(int ticks) => (float)(ticks * FixedTickSeconds);

	public static int SecondsToTicks(float seconds) => (int)(seconds / FixedTickSeconds);
}
