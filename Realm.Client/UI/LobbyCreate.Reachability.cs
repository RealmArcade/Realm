using Godot;
using Realm.Client.Services;
using Realm.Shared.Reachability;

namespace Realm.Client.UI;

/// <summary>
///     The screen a host sees when a lobby could not be shown to be reachable. It hands the measured failure to the host's
///     connectivity dialog, which names what the game measured, lists the handful of causes that are real failure paths in
///     this build, and offers the two things that still work on this screen: host again, which re-runs the measurement, and
///     host the same lobby unlisted and send its address. Offering that help is the whole point of refusing to publish: a
///     lobby nobody can reach is otherwise only discovered by the players who waited for it to time out.
/// </summary>
public partial class LobbyCreate
{
	private void ShowReachabilityHelpModal(HostReachabilityResult failure, bool offerDirectHosting)
	{
		GameContext.UserInterface?.PlayWarningSound();

		var dialog = new ConnectivityHostDialog { Name = "ReachabilityHelpDialog" };
		AddChild(dialog);

		// A lobby nobody can hold a seat in is not one an unlisted address fixes, so the direct option is withheld together
		// with the address itself for a local seat failure rather than offered as a second way to fail.
		bool localSeatFailure = failure.Failure is HostReachabilityFailure.LoopbackUnanswered or HostReachabilityFailure.LoopbackNotOnLocalAddress;

		dialog.Open(offersRetry: true, onRetry: OnCreatePressed, offersDirectHosting: offerDirectHosting && !localSeatFailure, onHostDirect: HostAsDirectLobby);
	}
}
