using Realm.Shared.Reachability;

namespace Realm.Shared.Distribution;

/// <summary>
///     Decides whether a seeder's stated endpoint may be offered to players, from the one thing a lobby server can measure
///     for itself: whether it reached that endpoint from the public internet. A seeder behind a router that forwards nothing
///     states an address that answers on its own network and nowhere else, and advertising it sends every player to a
///     download that never starts.
/// </summary>
public static class SeederAdvertisement
{
	/// <summary>
	///     How long a confirmation stands before it is worth asking again. A seeder's endpoint is confirmed by reaching it,
	///     and a router mapping that worked a minute ago is not evidence about now, so the confirmation is renewed rather
	///     than believed for ever.
	/// </summary>
	public const int ConfirmationLifetimeSeconds = 60;

	/// <summary>
	///     Whether a seeder that stated this endpoint may be offered to remote players. An endpoint that is not publicly
	///     routable is never offered, and neither is one nothing has confirmed.
	/// </summary>
	/// <param name="statedAddress">The address the seeder stated for itself.</param>
	/// <param name="confirmed">Whether this node reached that endpoint from outside.</param>
	/// <returns>Whether the seeder may be advertised.</returns>
	public static bool IsAdvertisable(string? statedAddress, bool confirmed)
	{
		if (!confirmed) return false;
		return ReachabilityAddresses.IsPubliclyRoutable(statedAddress);
	}

	/// <summary>Whether a confirmation made at this moment is still worth believing.</summary>
	/// <param name="confirmedAt">When the endpoint was last reached, or null when it never was.</param>
	/// <param name="now">The current time.</param>
	/// <returns>Whether the confirmation is still in date.</returns>
	public static bool IsConfirmationFresh(System.DateTime? confirmedAt, System.DateTime now)
	{
		if (confirmedAt == null) return false;
		return (now - confirmedAt.Value).TotalSeconds < ConfirmationLifetimeSeconds;
	}
}
