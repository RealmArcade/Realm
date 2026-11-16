using System;

namespace Realm.Shared.Reachability;

/// <summary>
///     How a lobby listing is ordered and which rows it hides by default. A player looking for a game should see the lobbies
///     they can join first, and should not be offered a lobby whose host was never shown to be reachable unless they ask for
///     it: a lobby that cannot be joined is not an opportunity, it is a wait for a timeout.
/// </summary>
/// <remarks>
///     The order is a total order so the list does not reshuffle between refreshes: the class first, the ping a player will
///     get second, and the map name last. A lobby that states no class at all is neither hidden nor ranked as reachable - a
///     lobby server from before reachability was classified says nothing about a lobby, and silence is not evidence that the
///     lobby is broken, so an unknown class is shown below the lobbies that were measured and above the ones that were
///     measured and failed.
/// </remarks>
public static class LobbyListingPolicy
{
	/// <summary>Whether a listing is hidden while the browser is not asked to show unreachable lobbies.</summary>
	/// <param name="reachabilityClass">The class the listing states, or empty when it states none.</param>
	/// <returns>Whether a player would be shown this lobby only after asking for it.</returns>
	public static bool IsHiddenByDefault(string? reachabilityClass) => reachabilityClass == ReachabilityClasses.Unverified;

	/// <summary>
	///     The band a listing is sorted into. A lower rank is offered first, and the ranking is what makes the class the
	///     primary sort key.
	/// </summary>
	/// <param name="reachabilityClass">The class the listing states, or empty when it states none.</param>
	/// <returns>Zero for a lobby reachable without a punch, then one that needs a punch, then one that stated nothing, then one that was shown to be unreachable.</returns>
	public static int Rank(string? reachabilityClass) => reachabilityClass switch
	{
		ReachabilityClasses.Open => 0,
		ReachabilityClasses.PunchOnly => 1,
		ReachabilityClasses.Relayed => 2,
		ReachabilityClasses.Unverified => 4,
		_ => 3
	};

	/// <summary>
	///     Orders two listings the way the browser shows them: by class, then by the ping this screen would get, then by name so
	///     the list is stable between refreshes.
	/// </summary>
	/// <param name="leftClass">The class of the first listing.</param>
	/// <param name="leftPingMilliseconds">The ping of the first listing, or a negative number when it is unknown.</param>
	/// <param name="leftName">The name of the first listing.</param>
	/// <param name="rightClass">The class of the second listing.</param>
	/// <param name="rightPingMilliseconds">The ping of the second listing, or a negative number when it is unknown.</param>
	/// <param name="rightName">The name of the second listing.</param>
	/// <returns>A negative number when the first comes first, zero when they are interchangeable, and a positive number otherwise.</returns>
	public static int Compare(string? leftClass, int leftPingMilliseconds, string? leftName, string? rightClass, int rightPingMilliseconds, string? rightName)
	{
		int byRank = Rank(leftClass).CompareTo(Rank(rightClass));
		if (byRank != 0) return byRank;

		int byPing = NormalizedPing(leftPingMilliseconds).CompareTo(NormalizedPing(rightPingMilliseconds));
		if (byPing != 0) return byPing;

		return string.Compare(leftName ?? string.Empty, rightName ?? string.Empty, StringComparison.OrdinalIgnoreCase);
	}

	/// <summary>
	///     A ping a listing does not state sorts after every ping it does state, rather than in front of them as an unknown
	///     value near zero would.
	/// </summary>
	/// <param name="pingMilliseconds">The stated ping, or a negative number when it is unknown.</param>
	/// <returns>The ping to sort by.</returns>
	private static int NormalizedPing(int pingMilliseconds) => pingMilliseconds < 0 ? int.MaxValue : pingMilliseconds;
}
