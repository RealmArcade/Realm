using Realm.Shared;

namespace Realm.Client.Network;

/// <summary>
///     The page a host copies when a lobby has to be explained to somebody who cannot see the machine it runs on. Every
///     number a later reader needs to work out why a player could not get in is on it, and every number on it is one this
///     screen actually measured, or the statement that nothing measured it.
/// </summary>
public partial class LobbyManager
{
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
