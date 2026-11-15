namespace Realm.Shared.Reachability;

/// <summary>
///     Which side of a failed connection a help dialog is written for. The same issue is a different problem for the two
///     sides - a router that gives every player its own outside port is the host's rule to make and the joiner's nothing to
///     do - so the vocabulary is shared and the copy is not.
/// </summary>
public enum ConnectivityAudience
{
	/// <summary>The screen that tried to publish a lobby and was told it could not be shown reachable.</summary>
	Hosting,

	/// <summary>The screen that tried to join a lobby and never got in.</summary>
	Joining
}

/// <summary>
///     The issues a failed connection can actually be. Every one of them is a failure path this build measures or is told
///     about; none of them is a guess about a brand of router or a provider. They are stated once here so the dialog that
///     lists them, the check that ranks them and the sentence each one is drawn with cannot drift apart.
/// </summary>
public enum ConnectivityIssue
{
	/// <summary>No outside packet reaches the game port at all: the router has no inbound path, and UPnP did not make one.</summary>
	RouterBlocksInboundPath,

	/// <summary>The router translates the outside port per destination, so the port a player would be told changes per player.</summary>
	OutsidePortChangesPerPlayer,

	/// <summary>Windows or another security program is filtering the game's traffic.</summary>
	FirewallOrSecuritySoftware,

	/// <summary>Two routers, or a provider's box in front of the household router, so one forward is not enough.</summary>
	DoubleNat,

	/// <summary>The provider shares one public address among many customers, so no router setting can open a way in.</summary>
	CarrierGradeNat,

	/// <summary>A VPN or virtual adapter is up and is adding a layer of translation or filtering.</summary>
	VpnOrVirtualAdapter,

	/// <summary>The network itself - work, school, hotel, mobile or satellite - does not carry the traffic a join needs.</summary>
	RestrictiveNetwork,

	/// <summary>This computer cannot reach its own lobby: the seat it plays itself is not held.</summary>
	LocalSeatBroken,

	/// <summary>The lobby server could not be reached, or proved nothing about players anywhere else.</summary>
	LobbyServerUnreachable
}

/// <summary>
///     One page a reader can be sent to for a concept, with the title the dialog shows. Only stable reference pages belong
///     here: a manufacturer's walkthrough, a forum thread or an article about one router model is a page that moves, dies or
///     describes hardware the reader does not have.
/// </summary>
/// <param name="TitleKey">The title of the page, as the translation key that is also its English text.</param>
/// <param name="Url">The address the system browser is asked to open.</param>
public readonly record struct ConnectivityLink(string TitleKey, string Url);

/// <summary>
///     Everything one entry of the issue list holds: its name in the list, the one to three sentences that explain it, the
///     pages a reader can open, the phrase to search for their own hardware, and what to do next in this game. An entry is
///     deliberately this small: the game supplies the diagnosis and the vocabulary, and the internet supplies the steps.
/// </summary>
/// <param name="Issue">The issue this entry is about.</param>
/// <param name="TitleKey">The name of the entry in the list, which is also its English text.</param>
/// <param name="SummaryKey">The one to three sentences that explain the issue, which is also their English text.</param>
/// <param name="SearchPhraseKey">The phrase to search for, which is also its English text.</param>
/// <param name="NextStepKey">
///     What to do next in this game, which is also its English text. It may state one value, which is the address the
///     lobby is reached at on this machine's own network.
/// </param>
/// <param name="PrimaryLink">The most useful page for this issue, or a default link when there is none worth opening.</param>
/// <param name="SecondaryLink">The second page, or a default link when one is enough.</param>
public readonly record struct ConnectivityIssueContent(
	ConnectivityIssue Issue,
	string TitleKey,
	string SummaryKey,
	string SearchPhraseKey,
	string NextStepKey,
	ConnectivityLink PrimaryLink,
	ConnectivityLink SecondaryLink)
{
	/// <summary>Whether this entry has a page worth opening that is not a placeholder.</summary>
	public bool HasPrimaryLink => PrimaryLink.Url.Length > 0;

	/// <summary>Whether this entry has a second page.</summary>
	public bool HasSecondaryLink => SecondaryLink.Url.Length > 0;

	/// <summary>Whether the entry states the address the lobby is reached at on this network.</summary>
	public bool NextStepStatesAddress => NextStepKey.Contains("{0}", System.StringComparison.Ordinal);
}

/// <summary>
///     The ordered issue lists for both audiences. The order is the one the reader should try things in: what the game
///     measured first, then the shapes that are common, then the ones that are rare and hard to change. It is a list and not
///     a decision tree - every entry is readable without tapping anything else.
/// </summary>
public static class ConnectivityIssueCatalog
{
	private static readonly ConnectivityLink PortForwardingLink = new(ConnectivitySentences.LinkPortForwarding, ConnectivitySentences.UrlPortForwarding);

	private static readonly ConnectivityLink NatLink = new(ConnectivitySentences.LinkNetworkAddressTranslation, ConnectivitySentences.UrlNetworkAddressTranslation);

	private static readonly ConnectivityLink CarrierGradeNatLink = new(ConnectivitySentences.LinkCarrierGradeNat, ConnectivitySentences.UrlCarrierGradeNat);

	private static readonly ConnectivityLink FirewallLink = new(ConnectivitySentences.LinkWindowsFirewall, ConnectivitySentences.UrlWindowsFirewall);

	private static readonly ConnectivityLink VpnLink = new(ConnectivitySentences.LinkVirtualPrivateNetwork, ConnectivitySentences.UrlVirtualPrivateNetwork);

	private static readonly ConnectivityLink HolePunchingLink = new(ConnectivitySentences.LinkHolePunching, ConnectivitySentences.UrlHolePunching);

	private static readonly ConnectivityLink NoLink = new(string.Empty, string.Empty);

	private static readonly ConnectivityIssueContent[] Hosting =
	{
		new(
			ConnectivityIssue.RouterBlocksInboundPath,
			ConnectivitySentences.HostRouterTitle,
			ConnectivitySentences.HostRouterSummary,
			ConnectivitySentences.HostRouterSearch,
			ConnectivitySentences.HostRouterNextStep,
			PortForwardingLink,
			NatLink),
		new(
			ConnectivityIssue.OutsidePortChangesPerPlayer,
			ConnectivitySentences.HostPerDestinationTitle,
			ConnectivitySentences.HostPerDestinationSummary,
			ConnectivitySentences.HostPerDestinationSearch,
			ConnectivitySentences.HostPerDestinationNextStep,
			NatLink,
			PortForwardingLink),
		new(
			ConnectivityIssue.FirewallOrSecuritySoftware,
			ConnectivitySentences.HostFirewallTitle,
			ConnectivitySentences.HostFirewallSummary,
			ConnectivitySentences.HostFirewallSearch,
			ConnectivitySentences.HostFirewallNextStep,
			FirewallLink,
			NoLink),
		new(
			ConnectivityIssue.DoubleNat,
			ConnectivitySentences.HostDoubleNatTitle,
			ConnectivitySentences.HostDoubleNatSummary,
			ConnectivitySentences.HostDoubleNatSearch,
			ConnectivitySentences.HostDoubleNatNextStep,
			NatLink,
			PortForwardingLink),
		new(
			ConnectivityIssue.CarrierGradeNat,
			ConnectivitySentences.HostCarrierGradeNatTitle,
			ConnectivitySentences.HostCarrierGradeNatSummary,
			ConnectivitySentences.HostCarrierGradeNatSearch,
			ConnectivitySentences.HostCarrierGradeNatNextStep,
			CarrierGradeNatLink,
			NoLink),
		new(
			ConnectivityIssue.VpnOrVirtualAdapter,
			ConnectivitySentences.HostVpnTitle,
			ConnectivitySentences.HostVpnSummary,
			ConnectivitySentences.HostVpnSearch,
			ConnectivitySentences.HostVpnNextStep,
			VpnLink,
			NoLink),
		new(
			ConnectivityIssue.RestrictiveNetwork,
			ConnectivitySentences.HostRestrictiveTitle,
			ConnectivitySentences.HostRestrictiveSummary,
			ConnectivitySentences.HostRestrictiveSearch,
			ConnectivitySentences.HostRestrictiveNextStep,
			NoLink,
			NoLink),
		new(
			ConnectivityIssue.LocalSeatBroken,
			ConnectivitySentences.HostLocalSeatTitle,
			ConnectivitySentences.HostLocalSeatSummary,
			ConnectivitySentences.HostLocalSeatSearch,
			ConnectivitySentences.HostLocalSeatNextStep,
			FirewallLink,
			NoLink),
		new(
			ConnectivityIssue.LobbyServerUnreachable,
			ConnectivitySentences.HostLobbyServerTitle,
			ConnectivitySentences.HostLobbyServerSummary,
			ConnectivitySentences.HostLobbyServerSearch,
			ConnectivitySentences.HostLobbyServerNextStep,
			HolePunchingLink,
			NoLink)
	};

	private static readonly ConnectivityIssueContent[] Joining =
	{
		new(
			ConnectivityIssue.RestrictiveNetwork,
			ConnectivitySentences.JoinRestrictiveTitle,
			ConnectivitySentences.JoinRestrictiveSummary,
			ConnectivitySentences.JoinRestrictiveSearch,
			ConnectivitySentences.JoinRestrictiveNextStep,
			NoLink,
			NoLink),
		new(
			ConnectivityIssue.VpnOrVirtualAdapter,
			ConnectivitySentences.JoinVpnTitle,
			ConnectivitySentences.JoinVpnSummary,
			ConnectivitySentences.JoinVpnSearch,
			ConnectivitySentences.JoinVpnNextStep,
			VpnLink,
			NoLink),
		new(
			ConnectivityIssue.CarrierGradeNat,
			ConnectivitySentences.JoinCarrierGradeNatTitle,
			ConnectivitySentences.JoinCarrierGradeNatSummary,
			ConnectivitySentences.JoinCarrierGradeNatSearch,
			ConnectivitySentences.JoinCarrierGradeNatNextStep,
			CarrierGradeNatLink,
			NoLink),
		new(
			ConnectivityIssue.DoubleNat,
			ConnectivitySentences.JoinDoubleNatTitle,
			ConnectivitySentences.JoinDoubleNatSummary,
			ConnectivitySentences.JoinDoubleNatSearch,
			ConnectivitySentences.JoinDoubleNatNextStep,
			NatLink,
			NoLink),
		new(
			ConnectivityIssue.FirewallOrSecuritySoftware,
			ConnectivitySentences.JoinFirewallTitle,
			ConnectivitySentences.JoinFirewallSummary,
			ConnectivitySentences.JoinFirewallSearch,
			ConnectivitySentences.JoinFirewallNextStep,
			FirewallLink,
			NoLink),
		new(
			ConnectivityIssue.RouterBlocksInboundPath,
			ConnectivitySentences.JoinHostRouterTitle,
			ConnectivitySentences.JoinHostRouterSummary,
			ConnectivitySentences.JoinHostRouterSearch,
			ConnectivitySentences.JoinHostRouterNextStep,
			PortForwardingLink,
			HolePunchingLink),
		new(
			ConnectivityIssue.LobbyServerUnreachable,
			ConnectivitySentences.JoinLobbyServerTitle,
			ConnectivitySentences.JoinLobbyServerSummary,
			ConnectivitySentences.JoinLobbyServerSearch,
			ConnectivitySentences.JoinLobbyServerNextStep,
			NoLink,
			NoLink)
	};

	/// <summary>The issue list for one side of a failed connection, in the order a reader should work through them.</summary>
	/// <param name="audience">The side the dialog is written for.</param>
	/// <returns>The entries, in list order.</returns>
	public static System.Collections.Generic.IReadOnlyList<ConnectivityIssueContent> For(ConnectivityAudience audience)
	{
		return audience == ConnectivityAudience.Hosting ? Hosting : Joining;
	}

	/// <summary>The entry one issue is drawn from, for the side the dialog is written for.</summary>
	/// <param name="issue">The issue to describe.</param>
	/// <param name="audience">The side the dialog is written for.</param>
	/// <returns>The entry.</returns>
	/// <exception cref="System.ArgumentOutOfRangeException">When the issue is not one this audience lists.</exception>
	public static ConnectivityIssueContent Describe(ConnectivityIssue issue, ConnectivityAudience audience)
	{
		System.Collections.Generic.IReadOnlyList<ConnectivityIssueContent> entries = For(audience);
		for (int index = 0; index < entries.Count; index++)
		{
			if (entries[index].Issue == issue) return entries[index];
		}

		throw new System.ArgumentOutOfRangeException(nameof(issue), issue, "the issue is not listed for this audience");
	}

	/// <summary>Where an issue sits in its list, which is what breaks a tie between two issues a check ranked equally.</summary>
	/// <param name="issue">The issue to place.</param>
	/// <param name="audience">The side the list belongs to.</param>
	/// <returns>The position, or the length of the list when the issue is not on it.</returns>
	public static int IndexOf(ConnectivityIssue issue, ConnectivityAudience audience)
	{
		System.Collections.Generic.IReadOnlyList<ConnectivityIssueContent> entries = For(audience);
		for (int index = 0; index < entries.Count; index++)
		{
			if (entries[index].Issue == issue) return index;
		}

		return entries.Count;
	}
}
