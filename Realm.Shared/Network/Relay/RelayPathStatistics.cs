using System;

namespace Realm.Shared.Network.Relay;

/// <summary>
///     What one path of a connection measured, as a value a screen can read without keeping the path layer. It is stated
///     rather than formatted so the connection status and the diagnostics page describe a path from the same numbers.
/// </summary>
/// <param name="PathId">The path identifier this measurement belongs to.</param>
/// <param name="RoundTripMilliseconds">The round trip the path last measured, or zero while it has measured none.</param>
/// <param name="JitterMilliseconds">The jitter the path last measured.</param>
/// <param name="LossPercent">The share of this path's probes that was never answered.</param>
/// <param name="Answered">Whether a volunteer has ever answered on this path, which is what a measurement requires.</param>
public readonly record struct RelayPathSample(byte PathId, int RoundTripMilliseconds, int JitterMilliseconds, float LossPercent, bool Answered);

/// <summary>
///     One statement of what a connection's paths are worth right now: how many it holds and what the best of them
///     measured. It is published as a whole by the lane that measures the paths, so a screen reads one immutable answer
///     instead of fields that are being written underneath it.
/// </summary>
/// <param name="PathCount">How many paths the connection holds.</param>
/// <param name="Best">What the path carrying the connection measured, or null when no path is answered.</param>
public sealed record RelayPathSummary(int PathCount, RelayPathSample? Best)
{
	/// <summary>The statement of a connection that holds no paths, which is what a direct connection reports.</summary>
	public static RelayPathSummary Empty { get; } = new(0, null);
}

/// <summary>
///     What one path of a connection has measured about itself. Every field is counted from datagrams this side sent,
///     received or refused, so a path is never described by anything but what it did.
/// </summary>
public sealed class RelayPathStatistics
{
	/// <summary>How many inner datagrams this path carried for the match.</summary>
	public int Sent { get; set; }

	/// <summary>How many relayed datagrams this path delivered intact.</summary>
	public int Received { get; set; }

	/// <summary>How many copies this path delivered that the connection had already seen on another path.</summary>
	public int Duplicates { get; set; }

	/// <summary>How many datagrams this path refused, for whatever reason the carrier stated.</summary>
	public int Dropped { get; set; }

	/// <summary>How many datagrams this path refused because the tag did not prove them.</summary>
	public int Rejected { get; set; }

	/// <summary>How many probes this path sent.</summary>
	public int ProbesSent { get; set; }

	/// <summary>How many probes this path had answered.</summary>
	public int ProbesAnswered { get; set; }

	/// <summary>The bytes this path carried, which the traffic report reads.</summary>
	public long Bytes { get; set; }

	/// <summary>The round trip this path last measured, in milliseconds, or zero while it has measured none.</summary>
	public int RoundTripMilliseconds { get; set; }

	/// <summary>The jitter this path last measured, in milliseconds.</summary>
	public int JitterMilliseconds { get; set; }

	/// <summary>
	///     The share of this path's probes that was never answered, as a percentage. It is measured rather than assumed: a
	///     path that has sent no probe states no loss, which is not the same as a path that lost nothing.
	/// </summary>
	public float LossPercent => ProbesSent == 0 ? 0f : Math.Clamp((ProbesSent - ProbesAnswered) * 100f / ProbesSent, 0f, 100f);

	/// <summary>
	///     The single number paths are compared by. A path is better the lower it is: the trip a datagram takes, widened by
	///     the distance those trips land from each other and by every percent of them that never arrived.
	/// </summary>
	public int Score => RoundTripMilliseconds + 2 * JitterMilliseconds + (int)(LossPercent * 20f);

	/// <summary>States this path's measurements as a value, so a screen can describe it without holding the path layer.</summary>
	/// <param name="pathId">The path the measurements belong to.</param>
	/// <returns>The path's measurements.</returns>
	public RelayPathSample Sample(byte pathId) => new(pathId, RoundTripMilliseconds, JitterMilliseconds, LossPercent, ProbesAnswered > 0);

	/// <summary>Forgets every measurement, which a path that moved to a new endpoint starts from.</summary>
	public void Reset()
	{
		Sent = 0;
		Received = 0;
		Duplicates = 0;
		Dropped = 0;
		Rejected = 0;
		ProbesSent = 0;
		ProbesAnswered = 0;
		Bytes = 0;
		RoundTripMilliseconds = 0;
		JitterMilliseconds = 0;
	}
}

/// <summary>
///     The rule that decides which path carries a datagram and whether any other path carries a copy of it. It is stated
///     once here rather than in each caller, so a direct path and a relayed one are compared by the same measurements.
/// </summary>
public static class RelayPathScoring
{
	/// <summary>The loss a best path may measure before a second copy is worth its bandwidth.</summary>
	public const float DuplicationLossPercent = 1.5f;

	/// <summary>The jitter a best path may measure before a second copy is worth its bandwidth.</summary>
	public const int DuplicationJitterMilliseconds = 12;

	/// <summary>The round trip a best path may measure before a second copy is worth its bandwidth.</summary>
	public const int DuplicationRoundTripMilliseconds = 180;

	/// <summary>
	///     Whether the connection should put a copy of this datagram on a second path. Duplication is for a best path that
	///     is measurably unreliable or slow, never for one that is merely not the fastest it could be, and it is always
	///     bounded by the budget its owner stated.
	/// </summary>
	/// <param name="best">What the chosen path has measured.</param>
	/// <param name="budgetAllows">Whether the connection still has duplication budget for another copy.</param>
	/// <returns>Whether a second copy is worth sending.</returns>
	public static bool ShouldDuplicate(RelayPathStatistics best, bool budgetAllows)
	{
		if (!budgetAllows) return false;
		if (best.ProbesAnswered == 0) return false;

		return best.LossPercent >= DuplicationLossPercent
		       || best.JitterMilliseconds >= DuplicationJitterMilliseconds
		       || best.RoundTripMilliseconds >= DuplicationRoundTripMilliseconds;
	}

	/// <summary>
	///     Whether one path is a better carrier than another. A path that has measured nothing is never preferred over one
	///     that has, because an unmeasured path is a hope rather than an observation.
	/// </summary>
	/// <param name="candidate">The path being considered.</param>
	/// <param name="current">The best path so far, or null when none has been chosen.</param>
	/// <returns>Whether the candidate should carry the next datagram.</returns>
	public static bool IsBetterThan(RelayPathStatistics candidate, RelayPathStatistics? current)
	{
		if (current == null) return true;
		if (candidate.ProbesAnswered == 0) return current.ProbesAnswered == 0 && candidate.Score < current.Score;

		return candidate.Score < current.Score;
	}
}
