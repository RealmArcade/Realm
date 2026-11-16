using Realm.Shared.Reachability;

namespace Realm.Client.UI;

public partial class LobbyRoom
{
	private bool _networkNoticeShown;

	private bool _publicationRefused;

	private void PostNetworkNotice()
	{
		if (!Lobby.IsHost || Lobby.IsSinglePlayer) return;
		if (_networkNoticeShown) return;

		bool listed = Lobby.ActiveLobbyId is { Length: > 0 } && !Lobby.IsUnlisted;
		bool playerReported = !string.IsNullOrWhiteSpace(Lobby.LastPlayerJoinFailure);
		bool measuredFailure = _publicationRefused || Lobby.LastHostFailure is { Failure: not HostReachabilityFailure.None };

		// A listed lobby has nothing to report about its own network: something outside this machine already reached it.
		// Only a player who failed to get in can still disprove that, and that report is worth opening the dialog for.
		if (listed && !playerReported) return;
		if (!measuredFailure && !playerReported) return;

		_networkNoticeShown = true;
		RestrictiveNetworkDialog.Show(this);
	}

	private void RefreshNetworkNotice()
	{
		PostNetworkNotice();
	}
}
