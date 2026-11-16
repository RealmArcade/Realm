using Realm.Shared;
using Realm.Shared.Reachability;

namespace Realm.Client.Network;

/// <summary>
///     The page a host copies when a lobby has to be explained to somebody who cannot see the machine it runs on. Every
///     number a later reader needs to work out why a player could not get in is on it, and every number on it is one this
///     screen actually measured, or the statement that nothing measured it.
/// </summary>
public partial class LobbyManager
{
	/// <summary>
	///     The address a STUN query saw this machine's router map its game port to, or an empty string when nothing was
	///     measured. It is what the connectivity help reads to say whether a second router or the provider's own translation
	///     stands in front of this machine, so it is the measurement itself rather than the address the game offers.
	/// </summary>
	public string MeasuredOutsideAddress => _hostPublicIp ?? string.Empty;

	/// <summary>
	///     The step the last join reached, named from <see cref="JoinPhases" />. A failure names the step that broke, which is
	///     what tells a joining screen whether the thing to change is on this machine or on the host's.
	/// </summary>
	public string LastJoinPhase => _joinPhase;

	/// <summary>Whether this screen's own NAT was classified, which is what makes a symmetric classification worth reading.</summary>
	public bool HasLocalNatType { get; private set; }

	/// <summary>
	///     Writes the diagnostics block, in the same shape every time so that two hosts' pages can be compared line by line.
	///     The game port is stated twice on purpose: the port a lobby is listed at and the port its transport holds being
	///     different is the failure this page exists to make visible, and a page that showed one of them would hide it.
	/// </summary>
	/// <returns>The block, one statement per line.</returns>
	public string DescribeNetworkDiagnostics()
	{
		string listedPort = LobbyPort > 0 ? LobbyPort.ToString() : "none";
		string firewall = LastFirewallGrant.Detail;
		if (string.IsNullOrEmpty(firewall)) firewall = "not asked for";

		return string.Join('\n', new[]
		{
			"Realm lobby network diagnostics",
			$"game version: {RealmVersion.GameBinaryVersion}",
			$"lobby id: {ActiveLobbyId ?? "none"}",
			$"lobby server: {RegistryServerUrl}",
			$"game port (listed): {listedPort}",
			$"game port (transport holds): {BoundPort}",
			$"address on this network: {DirectConnectAddress}",
			$"address measured for this machine: {MeasuredPublicConnectAddress ?? "not measured"}",
			$"address confirmed from outside: {PublicConnectAddress ?? "not confirmed by anything outside this network"}",
			$"reachability class: {ReachabilityClass}",
			$"reachability re-checks: {_reachabilityReverifyCount} (last: {_lastReverifyOutcome})",
			$"router mapping: {MappingBehaviour}",
			$"local NAT: {LocalNatType}",
			$"router: {RouterReachability}",
			$"firewall: {firewall}",
			$"last hosting failure: {LastHostError ?? "none"}",
			$"last player join failure: {LastPlayerJoinFailure ?? "none"}",
			$"last warning shown: {LastLobbyWarning ?? "none"}"
		});
	}
}
