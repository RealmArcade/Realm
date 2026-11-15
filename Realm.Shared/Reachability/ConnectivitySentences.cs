namespace Realm.Shared.Reachability;

/// <summary>
///     The English sentences the connectivity help is drawn with, stated as constants rather than as literals at the point
///     they are shown. Every one of them is also a translation key: a screen translates the constant it draws, so the keys
///     the localization build collects are the sentences a reader actually meets, and an issue added without a sentence for
///     its explanation or its diagnosis is a compile error instead of a blank line.
/// </summary>
/// <remarks>
///     Nothing in this file names a router model, an antivirus product, a provider or a third-party tunnel: a sentence that
///     would is a sentence that goes stale, and the game is not the place to keep it. The only numbers allowed are the game
///     port, the outside port a router gave the lobby, and the address ranges that identify a second router in the path.
/// </remarks>
public static class ConnectivitySentences
{
	/// <summary>The title a host's dialog carries when a lobby could not be published.</summary>
	public const string HostingTitle = "COULD NOT PUBLISH THE LOBBY";

	/// <summary>The title a host's dialog carries when the lobby could not even hold this computer's own seat.</summary>
	public const string HostingTitleLocalSeat = "COULD NOT HOLD YOUR OWN SEAT";

	/// <summary>The title a joining screen's dialog carries when a join failed.</summary>
	public const string JoiningTitle = "COULD NOT JOIN THIS LOBBY";

	/// <summary>The caption above the one line that names what the game measured.</summary>
	public const string LikelyCauseCaption = "LIKELY CAUSE";

	/// <summary>The caption above a joining player's own failure, which is the evidence no measurement of this machine can produce.</summary>
	public const string PlayerJoinFailureCaption = "A PLAYER TRIED TO JOIN AND COULD NOT GET IN";

	/// <summary>The caption above the list of causes, which is what a reader picks from.</summary>
	public const string CausesCaption = "WHAT IT USUALLY IS";

	/// <summary>The label of the button that runs one issue's check.</summary>
	public const string DiagnoseButton = "CHECK THIS ONE";

	/// <summary>The label of the button that runs every check and ranks what it finds.</summary>
	public const string RunAllButton = "RUN FULL DIAGNOSTICS";


	/// <summary>The label of the button that searches the web for the phrase beside it.</summary>
	public const string SearchButton = "SEARCH";

	/// <summary>The caption above the phrase a reader can search for their own hardware.</summary>
	public const string SearchCaption = "SEARCH FOR";

	/// <summary>The caption above what to do next in this game.</summary>
	public const string NextStepCaption = "NEXT STEP";

	/// <summary>The line that answers a check with a likely explanation.</summary>
	public const string VerdictConfirmed = "Likely";

	/// <summary>The line that answers a check with a shape that might be the cause.</summary>
	public const string VerdictInconclusive = "Possible";

	/// <summary>The line that answers a check with a shape that was measured not to be the cause.</summary>
	public const string VerdictRuledOut = "Not this one";

	/// <summary>The line shown while a check is running, and before any has.</summary>
	public const string VerdictUnchecked = "Not checked yet";

	/// <summary>The line that says a check cannot settle this issue on its own.</summary>
	public const string VerdictNeedsResearch = "The game cannot settle this one by testing it, so read the sentences above and check it yourself if players still cannot get in.";

	/// <summary>The line a ranked result list is opened with when something was found.</summary>
	public const string RankedFoundCaption = "WHAT THIS MACHINE SHOWED, MOST LIKELY FIRST";

	/// <summary>The line a ranked result list is opened with when nothing was found.</summary>
	public const string RankedNothingFound = "No check on this machine found a likely cause. Read the list, and check the one that matches what you see.";

	/// <summary>The line that names a host's own network address, which players on it join at.</summary>
	public const string DirectConnectCaption = "DIRECT CONNECT ADDRESS";

	/// <summary>
	///     What a next step says instead of an address when there is no address to offer, which is the case while this
	///     computer cannot hold its own seat: the sentence still reads as a sentence rather than as a gap.
	/// </summary>
	public const string NextStepAddressFallback = "the address the lobby room shows";

	/// <summary>The note beside the direct connect address that says who it works for.</summary>
	public const string DirectConnectNote = "Players on your own network join at this address, and it needs nothing changed on your router.";

	/// <summary>The note shown beside a direct connect address when the local seat is the failure.</summary>
	public const string DirectConnectWithheldNote = "There is no direct connect address to offer while this computer cannot reach its own lobby: a lobby nobody can hold a seat in is not one an unlisted address fixes.";

	/// <summary>The label of the button that copies the diagnostics page.</summary>
	public const string CopyDiagnosticsButton = "COPY NETWORK DETAILS";

	/// <summary>The label of the button that hosts the lobby again, which is the only test this game has.</summary>
	public const string TryAgainButton = "TRY AGAIN";

	/// <summary>The label of the button that opens the same lobby without listing it.</summary>
	public const string HostDirectButton = "HOST DIRECT INSTEAD";


	/// <summary>The report a host reads when the lobby server answered and the game port never did.</summary>
	public const string LikelyHostProbeUnanswered = "The lobby server reached for UDP port {0} and nothing answered, so your lobby was not listed.";

	/// <summary>The report a host reads when the router gave the game port a different outside port per player.</summary>
	public const string LikelyHostPerDestination = "The lobby server reached UDP port {0} but your router gave it a different outside port for every player it came from.";

	/// <summary>The report a host reads when the outside port was translated and the second measurement never arrived.</summary>
	public const string LikelyHostMappingUnmeasured = "The lobby server reached UDP port {0} from outside, but the check that shows your router keeps one outside port for every player did not finish.";

	/// <summary>The report a host reads when this computer could not reach its own lobby.</summary>
	public const string LikelyHostLoopback = "This computer could not reach its own lobby over loopback, so nothing about your router was measured at all.";

	/// <summary>The report a host reads when the lobby server could not be reached.</summary>
	public const string LikelyHostRegistry = "The lobby server itself could not be reached, so nothing about this computer could be measured.";

	/// <summary>The report a host reads when the lobby server cannot check this kind of address.</summary>
	public const string LikelyHostFamily = "The lobby server cannot check an address of the kind this lobby was offered at, so nothing about this computer could be measured.";

	/// <summary>The report a host reads when the lobby server reached it from inside its own network.</summary>
	public const string LikelyHostOwnNetwork = "The lobby server reached this computer from inside its own network, which proves nothing about players anywhere else.";

	/// <summary>The report a host reads when nothing has failed, which is the case when the help is opened from a working lobby.</summary>
	public const string LikelyHostNothingFailed = "The lobby server measured this lobby as reachable and nothing has failed since, so a player who cannot get in is more likely blocked by their own network.";

	/// <summary>The report a joining screen reads when no join has failed yet on this screen.</summary>
	public const string LikelyJoinNothingMeasured = "This screen has not recorded which step of a join failed, so read the causes below and check the ones that match what you see.";

	/// <summary>The report a joining screen reads when the lobby server could not be resolved through.</summary>
	public const string LikelyJoinRegistry = "The lobby server could not be reached, so this lobby could not be found or reached at all.";

	/// <summary>The report a joining screen reads when the host's answer never arrived.</summary>
	public const string LikelyJoinHostSide = "This screen reached the lobby server, and the host's answer never came back, which is usually the host's network.";

	/// <summary>The report a joining screen reads when its own traffic never left.</summary>
	public const string LikelyJoinOwnSide = "This screen could not report where it is to the lobby server, which is the network this machine is on.";

	/// <summary>The report a joining screen reads when the lobby refused it for a reason that is not its network.</summary>
	public const string LikelyJoinRefused = "The lobby answered and refused this screen, which is a lobby rule rather than a network problem.";

	// Links. Only stable reference pages: a concept on Wikipedia, a product page on Microsoft Learn.
	public const string LinkPortForwarding = "Port forwarding (Wikipedia)";
	public const string UrlPortForwarding = "https://en.wikipedia.org/wiki/Port_forwarding";
	public const string LinkNetworkAddressTranslation = "Network address translation (Wikipedia)";
	public const string UrlNetworkAddressTranslation = "https://en.wikipedia.org/wiki/Network_address_translation";
	public const string LinkCarrierGradeNat = "Carrier-grade NAT (Wikipedia)";
	public const string UrlCarrierGradeNat = "https://en.wikipedia.org/wiki/Carrier-grade_NAT";
	public const string LinkWindowsFirewall = "Windows Defender Firewall (Microsoft Learn)";
	public const string UrlWindowsFirewall = "https://learn.microsoft.com/en-us/windows/security/operating-system-security/network-security/windows-firewall/";
	public const string LinkVirtualPrivateNetwork = "Virtual private network (Wikipedia)";
	public const string UrlVirtualPrivateNetwork = "https://en.wikipedia.org/wiki/Virtual_private_network";
	public const string LinkHolePunching = "Hole punching (Wikipedia)";
	public const string UrlHolePunching = "https://en.wikipedia.org/wiki/Hole_punching_%28networking%29";

	// Hosting: the nine entries of the issue list, in list order.
	public const string HostRouterTitle = "The router does not let an incoming connection through";
	public const string HostRouterSummary = "The lobby server could not reach this computer, so your lobby was never shown to anyone. The game already asks Windows for a firewall rule and already tries UPnP; what is missing is a path in from outside. Forward the game's UDP port to this computer, or turn on UPnP in the router.";
	public const string HostRouterSearch = "how to port forward my router";
	public const string HostRouterNextStep = "Forward the port on the router, then host again so the lobby server measures it. Players on your own network can still join at {0}.";

	public const string HostPerDestinationTitle = "The router gives every player a different outside port";
	public const string HostPerDestinationSummary = "The lobby server reached this computer, but the router gave it a different outside port for every player, and a port that changes per player is a port nobody can be told. A fixed forward rule, or a router that keeps one mapping, is the only fix; UPnP will not help here.";
	public const string HostPerDestinationSearch = "port forwarding my router model";
	public const string HostPerDestinationNextStep = "Forward the game's UDP port to this computer, then host again. Players on your own network can still join at {0}.";

	public const string HostFirewallTitle = "Windows Firewall or security software is blocking the game";
	public const string HostFirewallSummary = "Windows or another security program may be blocking incoming game traffic. The game asks Windows to allow the port each time you host, so approve the prompt it shows. Do not turn the firewall off.";
	public const string HostFirewallSearch = "my antivirus allow the game through the firewall";
	public const string HostFirewallNextStep = "Approve the prompt the game shows while hosting, then host again. Players on your own network can still join at {0}.";

	public const string HostDoubleNatTitle = "Two routers are in the path";
	public const string HostDoubleNatSummary = "Two routers, or a provider's box in front of your own, means forwarding on one device is not enough. Bridge mode or IP passthrough on the outer device is the usual fix. If the address the game measured outside your network is a private one (192.168.x.x, 10.x.x.x, 172.16-31.x.x), a second router is in the path.";
	public const string HostDoubleNatSearch = "fix double NAT my internet provider";
	public const string HostDoubleNatNextStep = "Put the outer device in bridge mode, then host again. Players on your own network can still join at {0}.";

	public const string HostCarrierGradeNatTitle = "The provider shares one public address (carrier-grade NAT)";
	public const string HostCarrierGradeNatSummary = "Your internet provider shares one public address among many customers (the 100.64.0.0/10 range), so no router setting can open a way in. Ask the provider for a public IPv4 address, or play over an overlay network instead.";
	public const string HostCarrierGradeNatSearch = "do I have a public IP or carrier-grade NAT my provider";
	public const string HostCarrierGradeNatNextStep = "Host the same lobby directly instead and send the address the lobby room shows to your friends, or join together through an overlay network that every player installs.";

	public const string HostVpnTitle = "A VPN or virtual adapter is in the way";
	public const string HostVpnSummary = "A VPN can block incoming traffic or add another layer of NAT. Disconnect it and host again. A virtual network between all the players is a valid alternative: everyone needs it, not just you.";
	public const string HostVpnSearch = "VPN port forwarding my provider";
	public const string HostVpnNextStep = "Disconnect the VPN and host again, then check whether the lobby publishes. Players on your own network can still join at {0}.";

	public const string HostRestrictiveTitle = "This network blocks the traffic the game needs";
	public const string HostRestrictiveSummary = "This network (work, school, hotel, some mobile or satellite links) blocks the traffic the game needs, including the lobby server itself. Try another network, or host over an overlay network.";
	public const string HostRestrictiveSearch = "hotel or office wifi blocks online games";
	public const string HostRestrictiveNextStep = "Try another network, or host the same lobby directly and keep the players you invite on this same network.";

	public const string HostLocalSeatTitle = "This computer cannot reach its own lobby";
	public const string HostLocalSeatSummary = "Your own game could not reach the lobby on this computer, so the router is not involved at all. Security software that filters local connections, or another program that holds the port, is the cause. Allow Realm and host again.";
	public const string HostLocalSeatSearch = "my security software blocks local connections";
	public const string HostLocalSeatNextStep = "Allow Realm through the security software, close whatever is holding UDP 8999, and host again.";

	public const string HostLobbyServerTitle = "The lobby server could not reach this lobby";
	public const string HostLobbyServerSummary = "The lobby server could not be reached, or it reached this computer from inside your own network and so proved nothing about players elsewhere. A lobby like that is never listed.";
	public const string HostLobbyServerSearch = "how to play with friends without a lobby browser";
	public const string HostLobbyServerNextStep = "Host the same lobby directly instead and send the address the lobby room shows, or try again in a moment so another lobby server is used.";

	// Joining: the seven entries of the issue list, in list order.
	public const string JoinRestrictiveTitle = "This network blocks the traffic the game needs";
	public const string JoinRestrictiveSummary = "This network (work, school, hotel, some mobile or satellite links) filters the traffic a join needs, and can block the lobby server as well. Try another network, or use an overlay network that every player installs.";
	public const string JoinRestrictiveSearch = "hotel or office wifi blocks online games";
	public const string JoinRestrictiveNextStep = "Try another network, then join the same address again. A phone hotspot is the quickest way to prove it is this network.";

	public const string JoinVpnTitle = "A VPN or virtual adapter is in the way";
	public const string JoinVpnSummary = "A VPN can block the answer the host sends back, or add another layer of NAT in front of you. Disconnect it and join again. A virtual network between all the players is a valid alternative: everyone needs it, not just you.";
	public const string JoinVpnSearch = "VPN port forwarding my provider";
	public const string JoinVpnNextStep = "Disconnect the VPN, then join the same address again.";

	public const string JoinCarrierGradeNatTitle = "Your provider shares one public address (carrier-grade NAT)";
	public const string JoinCarrierGradeNatSummary = "Your internet provider shares one public address among many customers (the 100.64.0.0/10 range), so the host's answer cannot be routed back to you. Ask the provider for a public IPv4 address, or play over an overlay network.";
	public const string JoinCarrierGradeNatSearch = "do I have a public IP or carrier-grade NAT my provider";
	public const string JoinCarrierGradeNatNextStep = "Join from a network with its own public address, or ask the others to play over an overlay network with you.";

	public const string JoinDoubleNatTitle = "Two routers are in the path";
	public const string JoinDoubleNatSummary = "Two routers on your side, or a provider's box in front of your own, can drop the answer the host sends back. If the address the game measured outside your network is a private one (192.168.x.x, 10.x.x.x, 172.16-31.x.x), a second router is in the path.";
	public const string JoinDoubleNatSearch = "fix double NAT my internet provider";
	public const string JoinDoubleNatNextStep = "Bridge the outer device, then join the same address again.";

	public const string JoinFirewallTitle = "Security software is blocking the game";
	public const string JoinFirewallSummary = "Security software can block the traffic a join needs even when Windows lets it through. Allow Realm in it, and do not turn the firewall off. This is not the same rule the game asks for when you host a lobby.";
	public const string JoinFirewallSearch = "my antivirus allow the game through the firewall";
	public const string JoinFirewallNextStep = "Allow Realm through the security software, then join the same address again.";

	public const string JoinHostRouterTitle = "The host's network does not let players in";
	public const string JoinHostRouterSummary = "The lobby's port never answered, which is usually the host's network rather than yours. The host's router has to let one incoming connection through, or the host has to open the lobby as a direct one and send its address.";
	public const string JoinHostRouterSearch = "why can't my friends join my game";
	public const string JoinHostRouterNextStep = "Send this to the host: the lobby's UDP port needs a forward on their router, or the lobby should be opened with HOST DIRECT INSTEAD.";

	public const string JoinLobbyServerTitle = "The lobby server could not be reached";
	public const string JoinLobbyServerSummary = "The lobby server that keeps the list of lobbies could not be reached, so this lobby could not be found. Check your internet connection and try again, or ask the host for an address to connect to directly.";
	public const string JoinLobbyServerSearch = "my internet provider blocks game servers";
	public const string JoinLobbyServerNextStep = "Ask the host for the Direct Connect address and join with PLAY, then Join Private Lobby via IP Address.";

	// Diagnosis: what one issue's check found, per issue and verdict.
	public const string HostRouterConfirmed = "The last attempt measured this: the lobby server sent toward the game port from outside and nothing answered.";
	public const string HostRouterInconclusive = "The game cannot test your router without hosting a lobby, and the last attempt did not measure the game port at all.";
	public const string HostRouterRuledOut = "The lobby server reached UDP port {0} from outside, so a path in exists.";

	public const string HostPerDestinationConfirmed = "The lobby server saw the game port from two different outside ports, so the router gives every player a port of their own.";
	public const string HostPerDestinationInconclusive = "The lobby server reached the game port but its second check never arrived, so whether every player is given the same outside port was not measured.";
	public const string HostPerDestinationRuledOut = "The lobby server measured one outside port for every player it checked.";

	public const string HostFirewallDeclined = "You dismissed the Windows prompt, so the port stayed closed for the last attempt.";
	public const string HostFirewallRuleBlocks = "Windows reports the allowance for this program as blocking incoming traffic.";
	public const string HostFirewallRuleDisabled = "Windows holds an allowance for this program but has it switched off.";
	public const string HostFirewallUnreadable = "The firewall rule could not be read on this machine, so nothing can be said about it.";
	public const string HostFirewallRuledOut = "Windows holds an enabled allowance for this program's incoming traffic.";
	public const string HostFirewallSecuritySoftwareUnknown = "Windows allows the game, which says nothing about other security software on this machine.";
	public const string HostFirewallNoWindows = "This machine is not on Windows, so there is no Windows firewall rule to read.";

	public const string DoubleNatConfirmed = "The address this machine measured outside your network is {0}, which is a private address, so another router is in front of yours.";
	public const string DoubleNatRuledOut = "The address this machine measured outside your network is {0}, which is a public address, so no second router was seen between you and the internet.";
	public const string OutsideAddressUnmeasured = "This machine measured no address outside its own network, so nothing can be said about what stands between you and the internet.";
	public const string DoubleNatProviderShared = "The address this machine measured outside your network is {0}, which the provider shares among customers, so the extra translation belongs to the provider rather than to a second router.";

	public const string CarrierGradeNatConfirmed = "The address this machine measured outside your network is {0}, which is in the range providers share among customers, so no router setting can open a way in.";
	public const string CarrierGradeNatRuledOut = "The address this machine measured outside your network is {0}, which is not a shared provider address.";

	public const string VpnConfirmed = "A VPN or tunnel adapter is up on this machine, and the last measurement failed the way one interferes.";
	public const string VpnInconclusive = "A VPN or tunnel adapter is up on this machine, so it may be adding a layer in the way.";
	public const string VpnRuledOut = "No VPN or tunnel adapter is up on this machine.";

	public const string RestrictiveConfirmed = "The lobby server itself could not be reached from this network.";
	public const string RestrictiveRuledOut = "The lobby server answered this network, so it is not blocking everything.";
	public const string RestrictiveUnprobed = "The game could not ask the lobby server, so nothing can be said about this network.";

	public const string LocalSeatLoopbackConfirmed = "This machine's own client never got its lobby back from the game, which is a local connection the router is not part of.";
	public const string LocalSeatPortHeld = "Another program is holding UDP 8999, so this lobby had to take a port the system chose. Close that program and host again.";
	public const string LocalSeatRuledOut = "This machine holds UDP port {0} and answered its own lobby over loopback.";
	public const string LocalSeatUnmeasured = "The last attempt did not get as far as this machine's own seat, so nothing was measured about it.";

	public const string LobbyServerRegistryConfirmed = "The lobby server could not be reached from this computer.";
	public const string LobbyServerOwnNetworkConfirmed = "The lobby server reached this computer from inside its own network, so its answer describes that network and no other.";
	public const string LobbyServerFamilyConfirmed = "The lobby server has no socket in the address family this lobby is offered at, so it could not check the port at all.";
	public const string LobbyServerRuledOut = "The lobby server answered and measured this lobby from outside this network.";
	public const string LobbyServerUnprobed = "The game could not ask the lobby server, so nothing can be said about it.";

	public const string JoinRestrictiveConfirmed = "The lobby server could not be reached from this network.";
	public const string JoinRestrictiveHostSide = "The lobby server answered this network, so this join failed after it, which points at the host's side.";
	public const string JoinRestrictiveUnprobed = "The game could not ask the lobby server, so nothing can be said about this network.";

	public const string JoinFirewallInconclusive = "The game has no test for security software on a joining screen, so this has to be checked by hand.";

	public const string JoinHostSideConfirmed = "The join stopped at the punch or the answer, which is the host's side of the connection.";
	public const string JoinHostSideInconclusive = "The join did not reach the step where the host's network is what fails.";
}
