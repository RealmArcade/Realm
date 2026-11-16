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

