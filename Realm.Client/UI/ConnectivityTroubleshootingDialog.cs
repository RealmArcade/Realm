using System;
using Godot;
using Realm.Client.Services;
using Realm.Shared.Reachability;

namespace Realm.Client.UI;

/// <summary>
///     The one troubleshooting dialog: what the game measured about a connection that failed, which side of that connection is
///     being explained, one row per cause that side can actually fail on, and what the reader can do from here. A lobby that
///     would not publish, a join that never got in, a host asking about a lobby nothing has gone wrong with yet and a player
///     typing an address they were given all open this same card, because the causes are the same questions and only the
///     actions around them differ.
/// </summary>
/// <remarks>
///     The card states nothing about which failure sent the reader here. A heading naming the failure would go stale the moment
///     the reader switched scenario or re-ran a check, and it would tell them what they already know from the screen that sent
///     them; the measured line and the scenario they choose say everything the card has to say. The list, the scenario
///     selector, the checks and every explanation come from <see cref="ConnectivityIssuePanel" />; what is stated here is the
///     heading, the address this machine's own network joins at, and the actions a host has that a joining player does not -
///     hosting the lobby again, and copying what a report needs.
/// </remarks>
[GlobalClass]
public partial class ConnectivityTroubleshootingDialog : Control
{
	private const int CardWidth = ConnectivityIssuePanel.CardWidth;

	private ConnectivityAudience _audience;

	private ConnectivityIssuePanel _panel = null!;

	private Label _titleLabel = null!;

	private Label _copyFeedbackLabel = null!;

	private VBoxContainer _directSection = null!;

	private HFlowContainer _actions = null!;

	private bool _directBuilt;

	private bool _actionsBuilt;

	private bool _followUp;

	/// <summary>
	///     Raised once when the dialog goes away without an action having taken over what happens next. A screen that has to
	///     react to the failure - returning to the lobby list, for instance - listens here instead of keeping a second copy of
	///     the dialog's close button.
	/// </summary>
	public event Action? Closed;

	public override void _Ready()
	{
		Name = "ConnectivityTroubleshootingDialog";
		SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
		MouseFilter = MouseFilterEnum.Stop;

		BuildLayout();
	}

	/// <summary>
	///     Fills the dialog for one side of a connection and starts the full run. The side is only the starting point: nothing on
	///     the card states which failure opened it, so the reader can work through either side of a connection from here.
	/// </summary>
	/// <param name="audience">The side of the connection the card opens on, which the reader may then change.</param>
	/// <param name="onRetry">
	///     What hosting the lobby again does, or null when this card was opened where hosting is not what comes next. It is the
	///     only test this game has for a lobby that would not publish, so a host who fixes the one thing named here is one
	///     press from hosting.
	/// </param>
	public void Open(ConnectivityAudience audience, Action? onRetry = null)
	{
		_audience = audience;

		LocalizedText.Set(_titleLabel, ConnectivitySentences.ConnectionTroubleshootingTitle);
		if (!_directBuilt)
		{
			BuildDirectConnectSection();
			_directBuilt = true;
		}

		if (!_actionsBuilt)
		{
			BuildActions(onRetry);
			_actionsBuilt = true;
		}

		ApplyScenario();
		_panel.Open(audience);
	}

	/// <summary>
	///     Follows the reader's scenario choice. The address this machine's own network joins at belongs to a host's copy: a
	///     joining player reads what to try instead of an address that describes somebody else's lobby.
	/// </summary>
	/// <param name="audience">The side of the connection now being explained.</param>
	private void OnScenarioChanged(ConnectivityAudience audience)
	{
		_audience = audience;
		ApplyScenario();
	}

	/// <summary>
	///     Shows the hosting-only half of the card. The address is offered only while this screen still holds the lobby it
	///     belongs to: a host who gave a lobby up has no lobby for anyone to join at it, so an address there would invite a
	///     player into a room that no longer exists. The lobby manager itself outlives every lobby, so it is the lobby it holds
	///     rather than the manager that answers this.
	/// </summary>
	private void ApplyScenario()
	{
		if (_directSection == null || !IsInstanceValid(_directSection)) return;

		_directSection.Visible = _audience == ConnectivityAudience.Hosting && GameContext.Lobby is { HoldsLobby: true };
	}

	private void BuildLayout()
	{
		var backdrop = new ColorRect { Name = "ConnectivityTroubleshootingBackdrop" };
		backdrop.Color = new Color(0, 0, 0, 0.62f);
		backdrop.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
		backdrop.MouseFilter = MouseFilterEnum.Stop;
		AddChild(backdrop);

		var center = UIStyle.CreateCenteredOverlay();
		center.Name = "ConnectivityTroubleshootingCenter";
		AddChild(center);

		var card = new PanelContainer { Name = "ConnectivityTroubleshootingPanel" };
		card.AddThemeStyleboxOverride("panel", UIStyle.CreateStonePanel(true));
		card.CustomMinimumSize = new Vector2(CardWidth, 0);
		card.SizeFlagsHorizontal = SizeFlags.ShrinkCenter;
		card.SizeFlagsVertical = SizeFlags.ShrinkCenter;
		center.AddChild(card);

		var body = new VBoxContainer { Name = "ConnectivityTroubleshootingBodyVBox" };
		body.AddThemeConstantOverride("separation", 10);
		card.AddChild(body);

		_titleLabel = new Label { Name = "ConnectivityTroubleshootingTitle_Label" };
		UIStyle.ApplyTitle(_titleLabel, string.Empty, 20);
		_titleLabel.AddThemeColorOverride("font_color", new Color(0.9f, 0.3f, 0.3f));
		_titleLabel.HorizontalAlignment = HorizontalAlignment.Center;
		body.AddChild(_titleLabel);

		_panel = new ConnectivityIssuePanel { Name = "TroubleshootingIssues_Panel" };
		_panel.SizeFlagsHorizontal = SizeFlags.ExpandFill;
		_panel.ScenarioChanged += OnScenarioChanged;
		body.AddChild(_panel);

		_directSection = new VBoxContainer { Name = "DirectConnectSection_VBox" };
		_directSection.AddThemeConstantOverride("separation", 4);
		body.AddChild(_directSection);

		_actions = new HFlowContainer { Name = "ConnectivityTroubleshootingActions_HFlowContainer" };
		_actions.Alignment = FlowContainer.AlignmentMode.Center;
		_actions.AddThemeConstantOverride("h_separation", 12);
		_actions.AddThemeConstantOverride("v_separation", 8);
		body.AddChild(_actions);

		_copyFeedbackLabel = new Label { Name = "ConnectivityTroubleshootingCopyFeedback_Label" };
		_copyFeedbackLabel.Visible = false;
		_copyFeedbackLabel.HorizontalAlignment = HorizontalAlignment.Center;
		_copyFeedbackLabel.AddThemeFontSizeOverride("font_size", 11);
		_copyFeedbackLabel.AddThemeColorOverride("font_color", UIStyle.ColorGoldDull);
		body.AddChild(_copyFeedbackLabel);
	}

	/// <summary>
	///     States the address a host hands to the players on their own network, or the reason there is none to offer: a lobby
	///     that could not hold this computer's own seat is not one an unlisted address fixes, so the address and the note that
	///     explains its absence replace each other rather than being offered as a second way to fail.
	/// </summary>
	private void BuildDirectConnectSection()
	{
		string? address = GameContext.Connectivity.CurrentEvidence.DirectConnectAddress;
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

	/// <summary>
	///     The actions that still work from this card. They are built as one wrapping row rather than a plain one, because which
	///     of them exist depends on the failure, and a row that cannot wrap would push the whole card wider than the window on
	///     the failures that offer the most of them.
	/// </summary>
	/// <param name="onRetry">What hosting the lobby again does, or null when the card was opened where hosting is not next.</param>
	private void BuildActions(Action? onRetry)
	{
		if (onRetry != null)
		{
			Button retry = UIStyle.CreateActionButton("ConnectivityTroubleshootingRetry_Button", TranslationServer.Translate(ConnectivitySentences.TryAgainButton), 14);
			retry.CustomMinimumSize = new Vector2(190, 40);
			retry.Pressed += () => InvokeAndClose(onRetry);
			_actions.AddChild(retry);
		}

		string diagnosticsBlock = GameContext.Connectivity.CurrentEvidence.DiagnosticsBlock;
		if (!string.IsNullOrEmpty(diagnosticsBlock))
		{
			Button copyDiagnostics = UIStyle.CreateActionButton("ConnectivityTroubleshootingCopyDiagnostics_Button", TranslationServer.Translate(ConnectivitySentences.CopyDiagnosticsButton), 14);
			copyDiagnostics.CustomMinimumSize = new Vector2(230, 40);
			LocalizedText.SetTooltip(copyDiagnostics, "Copy everything a report about this machine's network needs, so it can be pasted into a message.");
			copyDiagnostics.Pressed += () =>
			{
				GameContext.UserInterface?.PlayClickSound();
				DisplayServer.ClipboardSet(GameContext.Connectivity.CurrentEvidence.DiagnosticsBlock);
				ShowCopyFeedback(TranslationServer.Translate("Network diagnostics copied to clipboard."));
			};
			_actions.AddChild(copyDiagnostics);
		}

		Button close = UIStyle.CreateActionButton("ConnectivityTroubleshootingClose_Button", TranslationServer.Translate("CLOSE"), 14);
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
