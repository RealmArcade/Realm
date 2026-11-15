using Godot;
using Realm.Client.Network;
using Realm.Client.Services;
using Realm.Shared.Reachability;

namespace Realm.Client.UI;

/// <summary>
///     The screen a host sees when a lobby could not be shown to be reachable. It hands the measured failure to the one
///     connectivity help dialog, which names what the game measured, lists the handful of causes that are real failure paths
///     in this build, and offers the two things that still work on this screen: host again, which re-runs the measurement,
///     and host the same lobby unlisted and send its address. Offering that help is the whole point of refusing to publish:
///     a lobby nobody can reach is otherwise only discovered by the players who waited for it to time out.
/// </summary>
public partial class LobbyCreate
{
	private void ShowReachabilityHelpModal(HostReachabilityResult failure, bool offerDirectHosting)
	{
		GameContext.UserInterface?.PlayWarningSound();

		bool localSeatFailure = failure.Failure is HostReachabilityFailure.LoopbackUnanswered or HostReachabilityFailure.LoopbackNotOnLocalAddress;
		LobbyManager? lobby = LobbyForHelp();

		var dialog = new ConnectivityHelpDialog { Name = "ReachabilityHelpDialog" };
		AddChild(dialog);
		dialog.Open(new ConnectivityHelpRequest
		{
			Audience = ConnectivityAudience.Hosting,
			TitleKey = localSeatFailure ? ConnectivitySentences.HostingTitleLocalSeat : ConnectivitySentences.HostingTitle,
			OffersRetry = true,
			OnRetry = OnCreatePressed,

			// A lobby nobody can hold a seat in is not one an unlisted address fixes, so the direct option and the address
			// are withheld together for a local seat failure rather than offered as a second way to fail.
			OffersDirectHosting = offerDirectHosting && !localSeatFailure,
			OnHostDirect = HostAsDirectLobby,
			DirectConnectAddress = () => localSeatFailure ? null : lobby?.DirectConnectAddress,
			DiagnosticsBlock = lobby == null ? null : (System.Func<string>)lobby.DescribeNetworkDiagnostics,
			PlayerJoinFailure = lobby == null ? null : (System.Func<string?>)(() => lobby.LastPlayerJoinFailure)
		});
	}

	private static LobbyManager? LobbyForHelp() => GameContext.Lobby;
}
