using System;
using Realm.Shared.Reachability;

namespace Realm.Client.UI;

/// <summary>
///     What one opening of the connectivity help dialog is about: which side of the connection failed, what a reader can do
///     about it from here, and which measurements the dialog may read. Everything that depends on the screen the dialog was
///     opened from is a delegate, so the dialog itself never reaches into a lobby, a browser or a room.
/// </summary>
public class ConnectivityHelpRequest
{
	/// <summary>Which side of the connection this dialog explains.</summary>
	public ConnectivityAudience Audience { get; init; }

	/// <summary>The failure-specific title the dialog carries, which is also its English text.</summary>
	public string TitleKey { get; init; } = ConnectivitySentences.HostingTitle;

	/// <summary>Whether the attempt can be repeated from here, which for a host is hosting again.</summary>
	public bool OffersRetry { get; init; }

	/// <summary>What repeating the attempt does, when it can be repeated.</summary>
	public Action? OnRetry { get; init; }

	/// <summary>Whether the same lobby can be opened without being listed.</summary>
	public bool OffersDirectHosting { get; init; }

	/// <summary>What opening the same lobby without listing it does.</summary>
	public Action? OnHostDirect { get; init; }

	/// <summary>
	///     The address players on this network join at, or null when there is none worth offering - a lobby nothing can hold
	///     a seat in is not one an unlisted address fixes.
	/// </summary>
	public Func<string?>? DirectConnectAddress { get; init; }

	/// <summary>The page a report about this machine's network is written from, or null when there is no lobby to describe.</summary>
	public Func<string>? DiagnosticsBlock { get; init; }

	/// <summary>
	///     What a joining player was told when a join into this lobby failed, as this host was told it, or null when no player has
	///     reported one. It is the only evidence about this lobby's reachability that this machine's own measurements cannot
	///     produce: a player who failed is a player the measurement that accepted the lobby did not describe.
	/// </summary>
	public Func<string?>? PlayerJoinFailure { get; init; }
}
