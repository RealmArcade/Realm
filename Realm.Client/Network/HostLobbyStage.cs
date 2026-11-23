namespace Realm.Client.Network;

/// <summary>
///     The steps a host reads while their lobby is being opened. Steam owns the directory and the routing, so there is no
///     network to test, no port to open and nothing to register: what is left is loading the map's world and taking the
///     host's own seat in the lobby it just opened.
/// </summary>
public enum HostLobbyStage
{
	LoadingMap,
	AdmittingHost
}
