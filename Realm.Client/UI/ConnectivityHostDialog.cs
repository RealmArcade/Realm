using System;
using Godot;
using Realm.Client.Services;
using Realm.Client.Utils;
using Realm.Shared.Reachability;

namespace Realm.Client.UI;

/// <summary>
///     The host's troubleshooting dialog: what this machine measured about a lobby it could not publish, one row per cause a
///     host can actually change, and the two actions that still work on this screen - host the lobby again, which re-runs the
///     measurement, and open it unlisted and send its address. It is a dialog rather than a screen because the host flow that
///     refused the lobby is still behind it, and a host who fixes the one thing named here should be one press from hosting.
/// </summary>
/// <remarks>
///     The list, the checks and every explanation come from <see cref="ConnectivityIssuePanel" />, which the joining dialog and
///     the host troubleshooting screen draw too. What is stated here is only what a host can do, because a router that gives
///     every player its own outside port is the host's rule to make and the joining player's nothing to do.
/// </remarks>
[GlobalClass]
public partial class ConnectivityHostDialog : Control
{
	private const int CardWidth = ConnectivityIssuePanel.CardWidth;

	private ConnectivityIssuePanel _panel = null!;

	private Label _titleLabel = null!;

	private Label _copyFeedbackLabel = null!;

	private VBoxContainer _directSection = null!;

	private HFlowContainer _actions = null!;

	private string _titleKey = ConnectivitySentences.HostingTitle;

	private bool _followUp;

	/// <summary>
	///     Raised once when the dialog goes away without an action having taken over what happens next. A screen that has to
	///     react to the failure - returning to the lobby list, for instance - listens here instead of keeping a second copy of
	///     the dialog's close button.
	/// </summary>
	public event Action? Closed;

	public override void _Ready()
	{
		Name = "ConnectivityHostDialog";
		SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
		MouseFilter = MouseFilterEnum.Stop;

		BuildLayout();
	}

	/// <summary>
	///     Fills the dialog for a lobby that could not be published and starts the full run. Nothing is selected: the measured
	///     failure is stated above the list, and the reader chooses which cause to read or checks them all at once.
	/// </summary>
	/// <param name="offersRetry">Whether hosting the lobby again is offered, which is the only test this game has.</param>
	/// <param name="onRetry">What hosting the lobby again does.</param>
	public void Open(bool offersRetry, Action? onRetry)
	{
		ConnectivityEvidence evidence = GameContext.Connectivity.CurrentEvidence;
		_titleKey = evidence.LocalSeatFailed ? ConnectivitySentences.HostingTitleLocalSeat : ConnectivitySentences.HostingTitle;

		LocalizedText.Bind(_titleLabel, () => ConnectivityHelpText.Text(_titleKey));

		BuildDirectConnectSection(evidence);
		BuildActions(evidence, offersRetry, onRetry);

		_panel.Open(ConnectivityAudience.Hosting);
	}

	private void BuildLayout()
	{
		var backdrop = new ColorRect { Name = "ConnectivityHostBackdrop" };
		backdrop.Color = new Color(0, 0, 0, 0.62f);
		backdrop.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
		backdrop.MouseFilter = MouseFilterEnum.Stop;
		AddChild(backdrop);

		var center = UIStyle.CreateCenteredOverlay();
		center.Name = "ConnectivityHostCenter";
		AddChild(center);

		var card = new PanelContainer { Name = "ConnectivityHostPanel" };
		card.AddThemeStyleboxOverride("panel", UIStyle.CreateStonePanel(true));
		card.CustomMinimumSize = new Vector2(CardWidth, 0);
		card.SizeFlagsHorizontal = SizeFlags.ShrinkCenter;
		card.SizeFlagsVertical = SizeFlags.ShrinkCenter;
		center.AddChild(card);

		var body = new VBoxContainer { Name = "ConnectivityHostBodyVBox" };
		body.AddThemeConstantOverride("separation", 10);
		card.AddChild(body);

		_titleLabel = new Label { Name = "ConnectivityHostTitle_Label" };
		UIStyle.ApplyTitle(_titleLabel, string.Empty, 20);
		_titleLabel.AddThemeColorOverride("font_color", new Color(0.9f, 0.3f, 0.3f));
		_titleLabel.HorizontalAlignment = HorizontalAlignment.Center;
		body.AddChild(_titleLabel);

		_panel = new ConnectivityIssuePanel { Name = "HostConnectivityIssues_Panel" };
		_panel.SizeFlagsHorizontal = SizeFlags.ExpandFill;
		body.AddChild(_panel);

		_directSection = new VBoxContainer { Name = "DirectConnectSection_VBox" };
		_directSection.AddThemeConstantOverride("separation", 4);
		body.AddChild(_directSection);

		_actions = new HFlowContainer { Name = "ConnectivityHostActions_HFlowContainer" };
		_actions.Alignment = FlowContainer.AlignmentMode.Center;
		_actions.AddThemeConstantOverride("h_separation", 12);
		_actions.AddThemeConstantOverride("v_separation", 8);
		body.AddChild(_actions);

		_copyFeedbackLabel = new Label { Name = "ConnectivityHostCopyFeedback_Label" };
		_copyFeedbackLabel.Visible = false;
		_copyFeedbackLabel.HorizontalAlignment = HorizontalAlignment.Center;
		_copyFeedbackLabel.AddThemeFontSizeOverride("font_size", 11);
		_copyFeedbackLabel.AddThemeColorOverride("font_color", UIStyle.ColorGoldDull);
		body.AddChild(_copyFeedbackLabel);
	}

	private void BuildDirectConnectSection(ConnectivityEvidence evidence)
	{
		// A lobby that could not hold this computer's own seat is not one an unlisted address fixes, so the address and the
		// note that explains its absence replace each other rather than being offered as a second way to fail.
		string? address = evidence.DirectConnectAddress;
		if (string.IsNullOrEmpty(address))
		{
			var withheld = new Label { Name = "DirectConnectWithheld_Label" };
			withheld.AutowrapMode = TextServer.AutowrapMode.WordSmart;
			withheld.CustomMinimumSize = new Vector2(CardWidth - 60, 0);
			withheld.SizeFlagsHorizontal = SizeFlags.ExpandFill;
			withheld.AddThemeFontSizeOverride("font_size", 12);
			withheld.AddThemeColorOverride("font_color", UIStyle.ColorGoldDull);
			LocalizedText.Bind(withheld, () => ConnectivityHelpText.Text(ConnectivitySentences.DirectConnectWithheldNote));
			_directSection.AddChild(withheld);
			return;
		}

		var caption = new Label { Name = "DirectConnectCaption_Label" };
		caption.Text = ConnectivityHelpText.Text(ConnectivitySentences.DirectConnectCaption);
		caption.AddThemeFontSizeOverride("font_size", 11);
		caption.AddThemeColorOverride("font_color", UIStyle.ColorGoldDull);
		_directSection.AddChild(caption);

		var row = new HBoxContainer { Name = "DirectConnectRow_HBoxContainer" };
		row.Alignment = BoxContainer.AlignmentMode.Center;
		row.AddThemeConstantOverride("separation", 10);
		_directSection.AddChild(row);

		var value = new Label { Name = "DirectConnectAddress_Label" };
		value.Text = address;
		value.VerticalAlignment = VerticalAlignment.Center;
		value.CustomMinimumSize = new Vector2(CardWidth - 260, 0);
		value.AddThemeFontSizeOverride("font_size", 18);
		value.AddThemeColorOverride("font_color", UIStyle.ColorCyanGlow);
		row.AddChild(value);

		var copy = UIStyle.CreateActionButton("DirectConnectCopy_Button", TranslationServer.Translate("COPY"), 13);
		copy.CustomMinimumSize = new Vector2(110, 32);
		copy.Pressed += () =>
		{
			GameContext.UserInterface?.PlayClickSound();
			DisplayServer.ClipboardSet(address);
			ShowCopyFeedback(TranslationServer.Translate("Address copied."));
		};
		row.AddChild(copy);

		var note = new Label { Name = "DirectConnectNote_Label" };
		note.AutowrapMode = TextServer.AutowrapMode.WordSmart;
		note.CustomMinimumSize = new Vector2(CardWidth - 60, 0);
		note.SizeFlagsHorizontal = SizeFlags.ExpandFill;
		note.AddThemeFontSizeOverride("font_size", 11);
		note.AddThemeColorOverride("font_color", UIStyle.ColorGoldDull);
		LocalizedText.Bind(note, () => ConnectivityHelpText.Text(ConnectivitySentences.DirectConnectNote));
		_directSection.AddChild(note);
	}

	private void BuildActions(ConnectivityEvidence evidence, bool offersRetry, Action? onRetry)
	{
		// A wrapping row rather than a plain one: which actions are offered depends on the failure, and a row that cannot wrap
		// would push the whole card wider than the window on the failures that offer the most of them.
		if (offersRetry && onRetry != null)
		{
			Button retry = UIStyle.CreateActionButton("ConnectivityHostRetry_Button", TranslationServer.Translate(ConnectivitySentences.TryAgainButton), 14);
			retry.CustomMinimumSize = new Vector2(190, 40);
			retry.Pressed += () => InvokeAndClose(onRetry);
			_actions.AddChild(retry);
		}

		if (!string.IsNullOrEmpty(evidence.DiagnosticsBlock))
		{
			Button copyDiagnostics = UIStyle.CreateActionButton("ConnectivityHostCopyDiagnostics_Button", TranslationServer.Translate(ConnectivitySentences.CopyDiagnosticsButton), 14);
			copyDiagnostics.CustomMinimumSize = new Vector2(230, 40);
			LocalizedText.SetTooltip(copyDiagnostics, "Copy everything a report about this machine's network needs, so it can be pasted into a message.");
			copyDiagnostics.Pressed += () =>
			{
				GameContext.UserInterface?.PlayClickSound();
				DisplayServer.ClipboardSet(evidence.DiagnosticsBlock);
				ShowCopyFeedback(TranslationServer.Translate("Network diagnostics copied to clipboard."));
			};
			_actions.AddChild(copyDiagnostics);
		}

		Button close = UIStyle.CreateActionButton("ConnectivityHostClose_Button", TranslationServer.Translate("CLOSE"), 14);
		close.CustomMinimumSize = new Vector2(150, 40);
		close.Pressed += () =>
		{
			GameContext.UserInterface?.PlayClickSound();
			Close();
		};
		_actions.AddChild(close);
	}

	private void ShowCopyFeedback(string message)
	{
		if (_copyFeedbackLabel == null || !IsInstanceValid(_copyFeedbackLabel)) return;

		_copyFeedbackLabel.Text = message;
		_copyFeedbackLabel.Visible = true;
	}

	private void InvokeAndClose(Action action)
	{
		GameContext.UserInterface?.PlayClickSound();
		_followUp = true;
		Close();
		action.Invoke();
	}

	private void Close()
	{
		QueueFree();

		if (!_followUp) Closed?.Invoke();
	}
}
