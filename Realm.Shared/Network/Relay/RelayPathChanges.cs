using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace Realm.Shared.Network.Relay;

/// <summary>
///     The vocabulary a connection's path changes are stated in. A connection that moves from one volunteer to another,
///     loses one, or gets one back is read out of two successive statements of its paths, so a captured log says which path
///     carried the peer, what the path it left measured, and why it left rather than only which path is used now. It is
///     stated once here so the screen's log and the authority's report describe the same event the same way.
/// </summary>
public static class RelayPathChanges
{
	/// <summary>
	///     States what changed between two statements of the same connection's paths.
	/// </summary>
	/// <param name="previous">What the connection's paths were the last time they were read.</param>
	/// <param name="current">What they are now.</param>
	/// <returns>One clause per change, joined by semicolons, or empty when nothing changed.</returns>
	public static string Describe(RelayPathSummary previous, RelayPathSummary current)
	{
		if (previous.Paths.Count == 0 && current.Paths.Count == 0) return string.Empty;

		var clauses = new List<string>();

		for (int index = 0; index < previous.Paths.Count; index++)
		{
			RelayPathSample before = previous.Paths[index];
			if (!Has(current.Paths, before.PathId))
			{
				clauses.Add($"path={before.PathId} was given up");
				continue;
			}

			RelayPathSample after = Find(current.Paths, before.PathId);
			if (before.Live && !after.Live)
			{
				clauses.Add($"path={before.PathId} stopped being answered ({Describe(before)})");
			}
			else if (!before.Live && after.Live)
			{
				clauses.Add($"path={before.PathId} answered again ({Describe(after)})");
			}
		}

		for (int index = 0; index < current.Paths.Count; index++)
		{
			RelayPathSample after = current.Paths[index];
			if (Has(previous.Paths, after.PathId)) continue;

			clauses.Add($"path={after.PathId} was opened ({(after.Answered ? Describe(after) : "not answered yet")})");
		}

		byte previousBest = previous.Best?.PathId ?? 0;
		byte currentBest = current.Best?.PathId ?? 0;
		if (previousBest != currentBest)
		{
			clauses.Add(current.Best is { } best
				? $"the best path moved {DescribePath(previousBest)}->{DescribePath(currentBest)} ({Describe(best)})"
				: $"the best path was given up ({DescribePath(previousBest)}->none)");
		}

		return clauses.Count == 0 ? string.Empty : string.Join("; ", clauses);
	}

	private static string DescribePath(byte pathId) => pathId == 0 ? "none" : pathId.ToString(CultureInfo.InvariantCulture);

	private static string Describe(RelayPathSample sample) =>
		string.Format(
			CultureInfo.InvariantCulture,
			"rtt {0} ms, jitter {1} ms, loss {2}%",
			sample.RoundTripMilliseconds,
			sample.JitterMilliseconds,
			sample.LossPercent.ToString("0.0", CultureInfo.InvariantCulture));

	private static RelayPathSample Find(IReadOnlyList<RelayPathSample> samples, byte pathId)
	{
		for (int index = 0; index < samples.Count; index++)
		{
			if (samples[index].PathId == pathId) return samples[index];
		}

		return default;
	}

	private static bool Has(IReadOnlyList<RelayPathSample> samples, byte pathId)
	{
		for (int index = 0; index < samples.Count; index++)
		{
			if (samples[index].PathId == pathId) return true;
		}

		return false;
	}
}
