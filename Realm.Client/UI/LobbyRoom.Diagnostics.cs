using System;
using Godot;
using Realm.Client.Services;
using Realm.Shared.Reachability;

namespace Realm.Client.UI;

/// <summary>
///     The room's whole connectivity surface: one short line saying what the game measured about this lobby, and two buttons -
///     the page a host copies into a report, and the door to the screen that explains what to change. Troubleshooting is not
///     carried out in the room: a host who is seating players and reading chat cannot read a 980-pixel card laid over it, and
///     a failed join may be the joining player's network or something the host can do nothing about in the moment. The room
///     therefore reports and hands over, and the screen that explains does so after the lobby is given up.
/// </summary>
/// <remarks>
///     The summary is the room's own chat line rather than another label, because the room's layout is full at every seat
///     count and the chat is where every other network event already arrives. It says the measured state, or the report of a
///     player who could not get in, and then names the screen that explains it.
/// </remarks>
public partial class LobbyRoom
{
	/// <summary>The label on the button that copies the diagnostics block.</summary>
	private const string CopyDiagnosticsText = "COPY NETWORK DIAGNOSTICS";

	private Button? _diagnosticsButton;
	private Label? _diagnosticsFeedback;
	private Button? _troubleshootingButton;
	private string _postedConnectivitySummary = string.Empty;

	/// <summary>
	///     Puts the host's two network buttons beside the invite button the lobby already offers, so the host of a listed lobby
	///     and the host of a direct one find them in the same place. They are built in code rather than added to the scene
	///     because they follow nothing about the lobby's shape.
	/// </summary>
	private void CreateHostNetworkButtons()
	{
		if (_shareAddressButton.GetParent() is not Control roomRoot) return;

		_diagnosticsFeedback = new Label { Name = "DiagnosticsFeedback_Label" };
		_diagnosticsFeedback.OffsetLeft = 1180;
		_diagnosticsFeedback.OffsetTop = 90;
		_diagnosticsFeedback.OffsetRight = 1440;
		_diagnosticsFeedback.OffsetBottom = 114;
		_diagnosticsFeedback.Visible = false;
		_diagnosticsFeedback.AddThemeFontSizeOverride("font_size", 12);
		_diagnosticsFeedback.AddThemeColorOverride("font_color", UIStyle.ColorGoldDull);
		roomRoot.AddChild(_diagnosticsFeedback);

		_troubleshootingButton = UIStyle.CreateActionButton("HostTroubleshooting_Button", TranslationServer.Translate(ConnectivitySentences.TroubleshootingTitle), 13);
		_troubleshootingButton.CustomMinimumSize = new Vector2(280, 45);
		_troubleshootingButton.OffsetLeft = 880;
		_troubleshootingButton.OffsetTop = 40;
		_troubleshootingButton.OffsetRight = 1160;
		_troubleshootingButton.OffsetBottom = 85;
		_troubleshootingButton.AddThemeConstantOverride("icon_max_width", 0);
		LocalizedText.SetTooltip(_troubleshootingButton, "Leave this lobby and open the screen that checks each cause players cannot get in. Leaving closes the lobby for everybody in it.");
		_troubleshootingButton.Pressed += OpenHostTroubleshooting;
		roomRoot.AddChild(_troubleshootingButton);

		_diagnosticsButton = UIStyle.CreateActionButton("CopyDiagnostics_Button", TranslationServer.Translate(CopyDiagnosticsText), 13);
		_diagnosticsButton.CustomMinimumSize = new Vector2(260, 45);
		_diagnosticsButton.OffsetLeft = 1180;
		_diagnosticsButton.OffsetTop = 40;
		_diagnosticsButton.OffsetRight = 1440;
		_diagnosticsButton.OffsetBottom = 85;
		_diagnosticsButton.AddThemeConstantOverride("icon_max_width", 0);
		LocalizedText.SetTooltip(_diagnosticsButton, "Copy everything a report about this lobby's network needs, so it can be pasted into a message.");
		_diagnosticsButton.Pressed += CopyNetworkDiagnostics;
		roomRoot.AddChild(_diagnosticsButton);

		ApplyInviteVisibility();
	}

	/// <summary>
	///     What the game measured about this lobby, as one chat line, followed by where to go to act on it, and only once
	///     something has actually gone wrong: a lobby that is fine needs no summary, and its own chat already says it is listed.
	///     A player who tried and failed is stated instead of the measurement, because that report disproves a lobby the lobby
	///     server measured as reachable and is the more useful thing for the host to read first.
	/// </summary>
	private void PostConnectivitySummary()
	{
		if (!Lobby.IsHost || Lobby.IsSinglePlayer) return;
		if (_chatLog == null || !IsInstanceValid(_chatLog)) return;

		ConnectivityEvidence evidence = GameContext.Connectivity.CurrentEvidence;
		bool reported = !string.IsNullOrWhiteSpace(evidence.PlayerJoinFailure);

		// A lobby nothing has gone wrong with states nothing: its own chat already says it is listed, and a summary of a
		// measurement that is still running would claim a result before the lobby server has given one.
		if (!evidence.SomethingFailed && !reported) return;

		string measured = reported
			? ConnectivityHelpText.Text(ConnectivitySentences.PlayerJoinFailureCaption)
			: ConnectivityHelpText.LikelyCause(ConnectivityCheck.DescribeLikelyCause(evidence.ToFacts(ConnectivityAudience.Hosting)));

		string line = $"{measured} {ConnectivityHelpText.Text(ConnectivitySentences.RoomSummaryRecommendation)}";
		if (string.Equals(line, _postedConnectivitySummary, StringComparison.Ordinal)) return;
		_postedConnectivitySummary = line;

		_chatLog.Text += $"[color=#ffd700]{TranslationServer.Translate("System")}[/color]: {line}\n";
	}

	/// <summary>
	///     Restates the summary after the lobby said something new about its own network. A publication that was refused and a
	///     player's failed join both reach this host through the lobby, so the line is refreshed from the same moment; a line
	///     that has not changed is not repeated.
	/// </summary>
	private void RefreshConnectivitySummary()
	{
		PostConnectivitySummary();
	}

	/// <summary>
	///     Asks before the room's troubleshooting door gives the lobby up, because opening the screen closes the lobby for every
	///     player in it and a host may have pressed it to read rather than to leave.
	/// </summary>
	private void OpenHostTroubleshooting()
	{
		GameContext.UserInterface?.PlayClickSound();
		GameContext.UserInterface?.ShowConfirmationDialog(
			ConnectivityHelpText.Text(ConnectivitySentences.TroubleshootingLeaveMessage),
			LeaveForHostTroubleshooting,
			confirmText: ConnectivityHelpText.Text(ConnectivitySentences.TroubleshootingLeaveConfirm),
			cancelText: ConnectivityHelpText.Text(ConnectivitySentences.TroubleshootingStayCancel),
			titleOverride: ConnectivityHelpText.Text(ConnectivitySentences.TroubleshootingTitle));
	}

	/// <summary>
	///     Gives the lobby up and opens the troubleshooting screen. The measurements the screen reads belong to the lobby being
	///     given up, so they are kept before the lobby is closed rather than looked for afterwards.
	/// </summary>
	private void LeaveForHostTroubleshooting()
	{
		GameContext.Connectivity.RememberEvidence();
		Lobby.Disconnect();
		GameContext.UserInterface?.TransitionTo(GameScreen.HostTroubleshooting);
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
