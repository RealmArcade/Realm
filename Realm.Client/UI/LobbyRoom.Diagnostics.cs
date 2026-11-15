using Godot;
using Realm.Client.Network;
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

	private Button? _diagnosticsButton;
	private Label? _diagnosticsFeedback;

	/// <summary>
	///     Puts the copy button beside the invite button the lobby already offers, so the host of a listed lobby and the host
	///     of a direct one find it in the same place. It is built in code rather than added to the scene because it is one
	///     button that follows nothing about the lobby's shape.
	/// </summary>
	private void CreateDiagnosticsButton()
	{
		if (_shareAddressButton.GetParent() is not Control inviteRow) return;

		_diagnosticsFeedback = new Label();
		_diagnosticsFeedback.Name = "DiagnosticsFeedback_Label";
		_diagnosticsFeedback.Visible = false;
		_diagnosticsFeedback.AddThemeFontSizeOverride("font_size", 12);
		_diagnosticsFeedback.AddThemeColorOverride("font_color", UIStyle.ColorGoldDull);
		inviteRow.AddChild(_diagnosticsFeedback);

		_diagnosticsButton = UIStyle.CreateActionButton("CopyDiagnostics_Button", TranslationServer.Translate(CopyDiagnosticsText), 13);
		_diagnosticsButton.CustomMinimumSize = new Vector2(260, 40);
		_diagnosticsButton.AddThemeConstantOverride("icon_max_width", 0);
		_diagnosticsButton.TooltipText = TranslationServer.Translate("Copy everything a report about this lobby's network needs, so it can be pasted into a message.");
		_diagnosticsButton.Pressed += CopyNetworkDiagnostics;
		inviteRow.AddChild(_diagnosticsButton);
		inviteRow.MoveChild(_diagnosticsButton, _shareAddressButton.GetIndex() + 1);
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
