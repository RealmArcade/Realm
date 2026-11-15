using System.Threading.Tasks;
using Godot;
using Realm.Client.Network;
using Realm.Client.Services;
using Realm.Shared.Reachability;

namespace Realm.Client.UI;

/// <summary>
///     The block a host copies when a lobby it cannot get players into has to be explained to somebody else. Everything a
///     later reader needs to work out why a player could not join is on this one page: the two ports (the one the lobby is
///     listed at and the one its transport actually holds), the address this machine measured for itself and the address
///     something outside confirmed, the reachability class and the router's mapping behaviour, the local network's NAT and
///     firewall state, the lobby server the lobby was published through, and the last failure either side saw. It is copied
///     rather than drawn, so a host can paste it into a report without transcribing numbers.
/// </summary>
public partial class LobbyRoom
{
	/// <summary>The label on the button that copies the diagnostics block.</summary>
	private const string CopyDiagnosticsText = "COPY NETWORK DIAGNOSTICS";

	/// <summary>The label on the button that reopens the help about players not getting in.</summary>
	private const string HelpPlayersJoinText = "HELP OTHER PLAYERS JOIN";

	private Button? _diagnosticsButton;
	private Label? _diagnosticsFeedback;
	private Button? _helpPlayersJoinButton;

	/// <summary>
	///     Puts the two buttons a host needs beside the invite button the lobby already offers, so the host of a listed lobby
	///     and the host of a direct one find them in the same place: the page to paste into a report, and the help that
	///     explains what to change when players cannot get in. They are built in code rather than added to the scene because
	///     they are two buttons that follow nothing about the lobby's shape.
	/// </summary>
	private void CreateHostNetworkButtons()
	{
		if (_shareAddressButton.GetParent() is not Control inviteRow) return;

		_diagnosticsFeedback = new Label();
		_diagnosticsFeedback.Name = "DiagnosticsFeedback_Label";
		_diagnosticsFeedback.Visible = false;
		_diagnosticsFeedback.AddThemeFontSizeOverride("font_size", 12);
		_diagnosticsFeedback.AddThemeColorOverride("font_color", UIStyle.ColorGoldDull);
		inviteRow.AddChild(_diagnosticsFeedback);

		_helpPlayersJoinButton = UIStyle.CreateActionButton("HelpPlayersJoin_Button", TranslationServer.Translate(HelpPlayersJoinText), 13);
		_helpPlayersJoinButton.CustomMinimumSize = new Vector2(280, 40);
		_helpPlayersJoinButton.AddThemeConstantOverride("icon_max_width", 0);
		LocalizedText.SetTooltip(_helpPlayersJoinButton, "Read what this machine measured about the lobby, what usually stops players getting in, and what to change before hosting again.");
		_helpPlayersJoinButton.Pressed += OpenConnectivityHelp;
		inviteRow.AddChild(_helpPlayersJoinButton);
		inviteRow.MoveChild(_helpPlayersJoinButton, _shareAddressButton.GetIndex() + 1);

		_diagnosticsButton = UIStyle.CreateActionButton("CopyDiagnostics_Button", TranslationServer.Translate(CopyDiagnosticsText), 13);
		_diagnosticsButton.CustomMinimumSize = new Vector2(260, 40);
		_diagnosticsButton.AddThemeConstantOverride("icon_max_width", 0);
		LocalizedText.SetTooltip(_diagnosticsButton, "Copy everything a report about this lobby's network needs, so it can be pasted into a message.");
		_diagnosticsButton.Pressed += CopyNetworkDiagnostics;
		inviteRow.AddChild(_diagnosticsButton);
	}

	/// <summary>
	///     Reopens the connectivity help for a lobby that is already open. A host who read the announcement once, closed it,
	///     and later has a player who cannot get in has nothing left to read otherwise: the measurement is in the log, and the
	///     log is not the room.
	/// </summary>
	private void OpenConnectivityHelp()
	{
		GameContext.UserInterface?.PlayClickSound();

		bool localSeatFailure = Lobby.LastHostFailure is { Failure: HostReachabilityFailure.LoopbackUnanswered or HostReachabilityFailure.LoopbackNotOnLocalAddress };

		var dialog = new ConnectivityHelpDialog { Name = "LobbyRoomConnectivityHelpDialog" };
		AddChild(dialog);
		dialog.Open(new ConnectivityHelpRequest
		{
			Audience = ConnectivityAudience.Hosting,
			TitleKey = localSeatFailure ? ConnectivitySentences.HostingTitleLocalSeat : ConnectivitySentences.HostingTitle,
			OffersRetry = true,
			OnRetry = RepublishLobby,
			DirectConnectAddress = () => localSeatFailure ? null : Lobby.DirectConnectAddress,
			DiagnosticsBlock = Lobby.DescribeNetworkDiagnostics,
			PlayerJoinFailure = () => Lobby.LastPlayerJoinFailure
		});
	}

	/// <summary>
	///     Hosts this lobby again, which is the only connectivity test this game has: publishing asks a lobby server to reach
	///     the game port, so a change on the router or the firewall is confirmed by the same measurement that refused the
	///     listing. A failure is stated in the room's chat, where the address the host sends out is shown.
	/// </summary>
	private void RepublishLobby()
	{
		_ = RepublishLobbyAsync();
	}

	private async Task RepublishLobbyAsync()
	{
		bool listed = await Lobby.PublishPreparedLobbyDirectlyOnFailureAsync();
		if (!GodotObject.IsInstanceValid(this) || !IsInsideTree()) return;

		if (listed)
		{
			ShowInviteFeedback(TranslationServer.Translate("This lobby is listed again."));
			return;
		}

		GameContext.UserInterface?.PlayWarningSound();
		ShowInviteFeedback(TranslationServer.Translate("This lobby could not be listed again; the help says what to change."));
	}

	/// <summary>
	///     Writes the diagnostics block this screen's lobby manager composes to the clipboard, and says so beside the button
	///     so a host who pressed it knows it happened without opening anything else.
	/// </summary>
	private void CopyNetworkDiagnostics()
	{
		DisplayServer.ClipboardSet(Lobby.DescribeNetworkDiagnostics());

		if (_diagnosticsFeedback == null || !IsInstanceValid(_diagnosticsFeedback)) return;

		_diagnosticsFeedback.Text = TranslationServer.Translate("Network diagnostics copied.");
		_diagnosticsFeedback.Visible = true;
	}
}
