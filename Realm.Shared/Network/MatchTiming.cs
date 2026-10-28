namespace Realm.Shared.Network;

/// <summary>
///     The one place a match's cadence is stated. The authority advances its world on
///     <see cref="SimulationTickRateHz" /> and describes that world to each peer on <see cref="SnapshotRateHz" />, and
///     the client derives how far apart two authoritative samples are from the same values. Both processes compile
///     against this type, so prediction, interpolation and the authority's clock cannot disagree about what a tick is.
///     The two rates are deliberately equal: a snapshot is the description of one tick, so describing the world more
///     often than it advances would send the same world twice and describing it less often would leave the client to
///     interpolate across ticks it never heard about.
/// </summary>
/// <remarks>
///     Not every frame of a match travels at this rate. A baseline is sent when a peer is seated or asks to be
///     described from scratch, presentation frames are sent only when the map asked a screen to show something, the
///     obstacle volumes are sent when they change (compared once a second), and an acknowledgement follows every
///     snapshot a peer applies. Only the delta snapshot stream is periodic.
/// </remarks>
public static class MatchTiming
{
	/// <summary>
	///     How often the authoritative world advances, in hertz. A match that measures 300 combat units at 24 players
	///     spends 0.63 ms of one core on the simulation tick and, once the peers are described across three workers, 3.3 ms
	///     on describing that tick to all of them with the shroud on: 3.9 ms per tick, or 18 percent of one core of an
	///     average gaming processor at this rate against the 50 percent a match is allowed to spend. Sixty hertz would cost
	///     36 percent and take the 24-player upstream from 6.9 to 13.8 Mbps for a difference in how fresh a remote unit
	///     looks that is a sixteenth of a second, so thirty hertz is the rate an RTS wants: it leaves the headroom to a
	///     little under three times this much work, which is unit count rather than rate.
	/// </summary>
	public const int SimulationTickRateHz = 30;

	/// <summary>
	///     How often a peer is told what changed, in hertz. A snapshot describes exactly one tick, so this matches
	///     <see cref="SimulationTickRateHz" />: describing the world more often than it advances sends the same world twice
	///     and describing it less often leaves the client interpolating across ticks it never heard about. At this rate one
	///     peer's delta is 1193 bytes with the shroud on, which the twenty-four of a full room send as 6.9 Mbps.
	/// </summary>
	public const int SnapshotRateHz = 30;

	/// <summary>How deeply the client buffers authoritative samples before it renders them, as a multiple of the snapshot interval.</summary>
	public const float InterpolationBufferSnapshotIntervals = 2f;

	/// <summary>The seconds one simulation tick covers.</summary>
	public const double FixedTickSeconds = 1.0 / SimulationTickRateHz;

	/// <summary>The seconds one snapshot interval covers.</summary>
	public const float SnapshotIntervalSeconds = 1.0f / SnapshotRateHz;

	/// <summary>The ticks that pass between two snapshots, which is one because a snapshot describes one tick.</summary>
	public const int TicksPerSnapshot = SimulationTickRateHz / SnapshotRateHz;

	/// <summary>The shortest span the client buffers before rendering, so a single late frame does not stall it.</summary>
	public const float MinimumInterpolationBufferSeconds = InterpolationBufferSnapshotIntervals * SnapshotIntervalSeconds;

	/// <summary>Converts a number of ticks into the simulated seconds they cover.</summary>
	public static float TicksToSeconds(int ticks) => (float)(ticks * FixedTickSeconds);

	/// <summary>Converts a number of simulated seconds into the ticks they cover, rounded down.</summary>
	public static int SecondsToTicks(float seconds) => (int)(seconds / FixedTickSeconds);
}
