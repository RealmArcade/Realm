using System.Collections.Generic;

namespace Realm.Shared.Reachability;

/// <summary>What one issue's check found on this machine.</summary>
public enum ConnectivityDiagnosisStatus
{
	/// <summary>The issue was measured to be present, or the evidence available points at it.</summary>
	Confirmed,

	/// <summary>Nothing can be said either way: the shape is there, or the check that would settle it never ran.</summary>
	Inconclusive,

	/// <summary>The issue was measured not to be present.</summary>
	RuledOut
}

/// <summary>
///     The answer one issue's check gives: what was found, the sentence that says it, and whether the check settles the
///     issue or only narrows it. A check that only narrows it is stated as such rather than presented as proof, because a
///     dialog that claims a cause it never measured is worse than one that says it could not.
/// </summary>
/// <param name="Issue">The issue this is the answer for.</param>
/// <param name="Status">What was found.</param>
/// <param name="TestDecides">
///     Whether this check can settle the issue on this machine. A check that cannot says so, so the reader knows to look at
///     the sentences and the page instead of trusting a verdict.
/// </param>
/// <param name="DetailKey">The sentence that states what was measured, which is also its English text.</param>
/// <param name="DetailValue">The one value the sentence states - a port, or an address - or null when it states none.</param>
public readonly record struct ConnectivityDiagnosis(
	ConnectivityIssue Issue,
	ConnectivityDiagnosisStatus Status,
	bool TestDecides,
	string DetailKey,
	object? DetailValue = null)
{
	/// <summary>Whether this check found the issue present.</summary>
	public bool IsLikely => Status == ConnectivityDiagnosisStatus.Confirmed;
}

/// <summary>
///     Orders the answers of a full run so the reader meets the most likely cause first. Confirmed comes before
///     inconclusive, inconclusive before ruled out, and issues the list already puts first win their ties, which is what
///     makes the same run present the same order on every screen.
/// </summary>
public static class ConnectivityDiagnosisRanking
{
	/// <summary>Where a status sits in the ranked list, lowest first.</summary>
	/// <param name="status">The status to rank.</param>
	/// <returns>Zero for a confirmed issue, one for an inconclusive one, two for a ruled-out one.</returns>
	public static int Rank(ConnectivityDiagnosisStatus status)
	{
		return status switch
		{
			ConnectivityDiagnosisStatus.Confirmed => 0,
			ConnectivityDiagnosisStatus.Inconclusive => 1,
			_ => 2
		};
	}

	/// <summary>Orders one run's answers, most likely first, keeping the issue list's order between equals.</summary>
	/// <param name="diagnoses">The answers to order.</param>
	/// <param name="audience">The list whose order breaks the ties.</param>
	/// <returns>The answers, in the order they are shown.</returns>
	public static IReadOnlyList<ConnectivityDiagnosis> Order(IReadOnlyList<ConnectivityDiagnosis> diagnoses, ConnectivityAudience audience)
	{
		var ordered = new List<ConnectivityDiagnosis>(diagnoses.Count);
		for (int index = 0; index < diagnoses.Count; index++)
		{
			ordered.Add(diagnoses[index]);
		}

		ordered.Sort((left, right) =>
		{
			int rankComparison = Rank(left.Status).CompareTo(Rank(right.Status));
			if (rankComparison != 0) return rankComparison;
			return ConnectivityIssueCatalog.IndexOf(left.Issue, audience).CompareTo(ConnectivityIssueCatalog.IndexOf(right.Issue, audience));
		});

		return ordered;
	}

	/// <summary>The answers that found their issue present, most likely first.</summary>
	/// <param name="diagnoses">The answers of one run.</param>
	/// <param name="audience">The list whose order breaks the ties.</param>
	/// <returns>The confirmed answers, in show order, empty when nothing was found.</returns>
	public static IReadOnlyList<ConnectivityDiagnosis> Likely(IReadOnlyList<ConnectivityDiagnosis> diagnoses, ConnectivityAudience audience)
	{
		IReadOnlyList<ConnectivityDiagnosis> ordered = Order(diagnoses, audience);
		var likely = new List<ConnectivityDiagnosis>(ordered.Count);
		for (int index = 0; index < ordered.Count; index++)
		{
			if (ordered[index].IsLikely) likely.Add(ordered[index]);
		}

		return likely;
	}
}
