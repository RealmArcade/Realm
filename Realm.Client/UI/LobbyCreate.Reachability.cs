using Godot;
using Realm.Client.Services;
using Realm.Shared.Reachability;

namespace Realm.Client.UI;

/// <summary>
///     The screen a host sees when a lobby could not be shown to be reachable. It hands the measured failure to the host's
///     connectivity dialog, which names what the game measured, lists the handful of causes that are real failure paths in
///     this build, and offers the one thing that still works on this screen: host again, which re-runs the measurement. The
///     lobby itself does not wait for the answer - it stays open to the players its address is handed to, and its host can
///     offer it in the browser again from its room once the network is fixed.
/// </summary>
public partial class LobbyCreate
{
	private void ShowReachabilityHelpModal()
	{
		GameContext.UserInterface?.PlayWarningSound();

		var dialog = new ConnectivityHostDialog { Name = "ReachabilityHelpDialog" };
		AddChild(dialog);

		dialog.Open(offersRetry: true, onRetry: OnCreatePressed);
	}
}
