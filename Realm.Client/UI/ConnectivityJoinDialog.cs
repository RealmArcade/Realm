using System;
using Godot;
using Realm.Client.Services;
using Realm.Client.Utils;
using Realm.Shared.Reachability;

namespace Realm.Client.UI;

/// <summary>
///     The joining player's troubleshooting dialog: what the last join this screen attempted actually stopped at, one row per
///     cause a joining player can change, and what that player can do from here. It is a separate dialog from the host's
///     because the same cause is a different problem on the two sides of a connection: a router that gives every player its
///     own outside port is the host's rule to make, and a joining player's nothing to do.
/// </summary>
/// <remarks>
///     The list, the checks and every explanation come from <see cref="ConnectivityIssuePanel" />, which the host's dialog and
///     the host troubleshooting screen draw too. A joining screen cannot publish a lobby, so its measurement is the join that
///     already failed rather than a fresh one; the only test it can repeat is joining again, which the lobby list offers.
/// </remarks>
[GlobalClass]
public partial class ConnectivityJoinDialog : Control
{
	private const int CardWidth = ConnectivityIssuePanel.CardWidth;

	private ConnectivityIssuePanel _panel = null!;

	private Label _titleLabel = null!;

	private Label _copyFeedbackLabel = null!;

	/// <summary>
	///     Raised once when the dialog is closed by its own button. A screen opened from inside a lobby returns to the lobby
	///     list here, because a player whose join failed has nothing left to do in the lobby they never got into.
	/// </summary>
	public event Action? Closed;

	public override void _Ready()
	{
		Name = "ConnectivityJoinDialog";
		SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
		MouseFilter = MouseFilterEnum.Stop;

		BuildLayout();
	}

	/// <summary>
	///     Fills the dialog for a join this screen could not complete and starts the full run. Nothing is selected: the step
	///     the join stopped at is stated above the list, and the reader chooses which cause to read or checks them all at once.
	/// </summary>
	public void Open()
	{
		LocalizedText.Bind(_titleLabel, () => ConnectivityHelpText.Text(ConnectivitySentences.JoiningTitle));
		_panel.Open(ConnectivityAudience.Joining);
	}

	private void BuildLayout()
	{
		var backdrop = new ColorRect { Name = "ConnectivityJoinBackdrop" };
		backdrop.Color = new Color(0, 0, 0, 0.62f);
		backdrop.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
		backdrop.MouseFilter = MouseFilterEnum.Stop;
		AddChild(backdrop);

		var center = UIStyle.CreateCenteredOverlay();
		center.Name = "ConnectivityJoinCenter";
		AddChild(center);

		var card = new PanelContainer { Name = "ConnectivityJoinPanel" };
		card.AddThemeStyleboxOverride("panel", UIStyle.CreateStonePanel(true));
		card.CustomMinimumSize = new Vector2(CardWidth, 0);
		card.SizeFlagsHorizontal = SizeFlags.ShrinkCenter;
		card.SizeFlagsVertical = SizeFlags.ShrinkCenter;
		center.AddChild(card);

		var body = new VBoxContainer { Name = "ConnectivityJoinBodyVBox" };
		body.AddThemeConstantOverride("separation", 10);
		card.AddChild(body);

		_titleLabel = new Label { Name = "ConnectivityJoinTitle_Label" };
		UIStyle.ApplyTitle(_titleLabel, string.Empty, 20);
		_titleLabel.AddThemeColorOverride("font_color", new Color(0.9f, 0.3f, 0.3f));
		_titleLabel.HorizontalAlignment = HorizontalAlignment.Center;
		body.AddChild(_titleLabel);

		_panel = new ConnectivityIssuePanel { Name = "JoinConnectivityIssues_Panel" };
		_panel.SizeFlagsHorizontal = SizeFlags.ExpandFill;
		body.AddChild(_panel);

		var actions = new HFlowContainer { Name = "ConnectivityJoinActions_HFlowContainer" };
		actions.Alignment = FlowContainer.AlignmentMode.Center;
		actions.AddThemeConstantOverride("h_separation", 12);
		actions.AddThemeConstantOverride("v_separation", 8);
		body.AddChild(actions);

		Button copyDiagnostics = UIStyle.CreateActionButton("ConnectivityJoinCopyDiagnostics_Button", TranslationServer.Translate(ConnectivitySentences.CopyDiagnosticsButton), 14);
		copyDiagnostics.CustomMinimumSize = new Vector2(230, 40);
		LocalizedText.SetTooltip(copyDiagnostics, "Copy everything a report about this machine's network needs, so it can be pasted into a message.");
		copyDiagnostics.Pressed += () =>
		{
			GameContext.UserInterface?.PlayClickSound();
			DisplayServer.ClipboardSet(GameContext.Connectivity.CurrentEvidence.DiagnosticsBlock);
			ShowCopyFeedback(TranslationServer.Translate("Network diagnostics copied to clipboard."));
		};
		actions.AddChild(copyDiagnostics);

		Button close = UIStyle.CreateActionButton("ConnectivityJoinClose_Button", TranslationServer.Translate("CLOSE"), 14);
		close.CustomMinimumSize = new Vector2(150, 40);
		close.Pressed += () =>
		{
			GameContext.UserInterface?.PlayClickSound();
			QueueFree();
			Closed?.Invoke();
		};
		actions.AddChild(close);

		_copyFeedbackLabel = new Label { Name = "ConnectivityJoinCopyFeedback_Label" };
		_copyFeedbackLabel.Visible = false;
		_copyFeedbackLabel.HorizontalAlignment = HorizontalAlignment.Center;
		_copyFeedbackLabel.AddThemeFontSizeOverride("font_size", 11);
		_copyFeedbackLabel.AddThemeColorOverride("font_color", UIStyle.ColorGoldDull);
		body.AddChild(_copyFeedbackLabel);
	}

	private void ShowCopyFeedback(string message)
	{
		if (_copyFeedbackLabel == null || !IsInstanceValid(_copyFeedbackLabel)) return;

		_copyFeedbackLabel.Text = message;
		_copyFeedbackLabel.Visible = true;
	}
}
