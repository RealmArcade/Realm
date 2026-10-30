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

	public static float TicksToSeconds(int ticks) => (float)(ticks * FixedTickSeconds);

	public static int SecondsToTicks(float seconds) => (int)(seconds / FixedTickSeconds);
}
