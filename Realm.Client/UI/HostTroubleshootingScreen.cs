using Godot;
using Realm.Client.Services;
using Realm.Client.Utils;
using Realm.Shared.Reachability;

namespace Realm.Client.UI;

/// <summary>
///     The screen a host opens when players cannot get into a lobby they are hosting. It carries the same issue list the
///     publish-failure dialog does, but it is a screen rather than a modal because it is reached from the lobby room, and a
///     room full of players waiting on a host is not a place to put a 980-pixel card over: the room shows a summary and hands
///     over to this screen, which the host opens by giving that lobby up.
/// </summary>
/// <remarks>
///     The measurements this screen reads belong to the lobby that was given up, so they are the copy the room captured before
///     it left rather than a live lobby's state. The lobby server's answer, the firewall's current rule and the adapters on
///     this machine are still measured live, because those describe the machine as it is now; nothing here publishes a lobby,
///     because the only test this game has is hosting one and that is what the button back to the host flow is for.
/// </remarks>
[GlobalClass]
public partial class HostTroubleshootingScreen : Control
{
	private Panel _backgroundPanel = null!;

	private Panel _leftPillar = null!;

	private Panel _rightPillar = null!;

	private Label _titleLabel = null!;

	private Button _backButton = null!;

	private ConnectivityIssuePanel _panel = null!;

	private Label _copyFeedbackLabel = null!;

	public override void _Ready()
	{
		(_backgroundPanel, _leftPillar, _rightPillar) = UIStyle.BindStandardChrome(this);
		_titleLabel = GetNode<Label>("Title");
		_backButton = GetNode<Button>("BackButton");

		BuildContent();
		ApplyStyles();

		_panel.Open(ConnectivityAudience.Hosting);
	}

	private void BuildContent()
	{
		var center = UIStyle.CreateCenteredOverlay();
		center.Name = "HostTroubleshootingCenter";
		AddChild(center);

		var card = new PanelContainer { Name = "HostTroubleshootingPanel" };
		card.AddThemeStyleboxOverride("panel", UIStyle.CreateStonePanel(true));
		card.CustomMinimumSize = new Vector2(ConnectivityIssuePanel.CardWidth, 0);
		card.SizeFlagsHorizontal = SizeFlags.ShrinkCenter;
		card.SizeFlagsVertical = SizeFlags.ShrinkCenter;
		center.AddChild(card);

		var body = new VBoxContainer { Name = "HostTroubleshootingBodyVBox" };
		body.AddThemeConstantOverride("separation", 10);
		card.AddChild(body);

		var intro = new Label { Name = "HostTroubleshootingIntro_Label" };
		intro.AutowrapMode = TextServer.AutowrapMode.WordSmart;
		intro.CustomMinimumSize = new Vector2(ConnectivityIssuePanel.CardWidth - 60, 0);
		intro.SizeFlagsHorizontal = SizeFlags.ExpandFill;
		intro.AddThemeFontSizeOverride("font_size", 12);
		intro.AddThemeColorOverride("font_color", UIStyle.ColorGoldDull);
		LocalizedText.Bind(intro, () => ConnectivityHelpText.Text(ConnectivitySentences.TroubleshootingIntro));
		body.AddChild(intro);

		_panel = new ConnectivityIssuePanel { Name = "HostTroubleshootingIssues_Panel" };
		_panel.SizeFlagsHorizontal = SizeFlags.ExpandFill;
		body.AddChild(_panel);

		var actions = new HFlowContainer { Name = "HostTroubleshootingActions_HFlowContainer" };
		actions.Alignment = FlowContainer.AlignmentMode.Center;
		actions.AddThemeConstantOverride("h_separation", 12);
		actions.AddThemeConstantOverride("v_separation", 8);
		body.AddChild(actions);

		Button copyDiagnostics = UIStyle.CreateActionButton("HostTroubleshootingCopy_Button", TranslationServer.Translate(ConnectivitySentences.CopyDiagnosticsButton), 14);
		copyDiagnostics.CustomMinimumSize = new Vector2(230, 40);
		LocalizedText.SetTooltip(copyDiagnostics, "Copy everything a report about this machine's network needs, so it can be pasted into a message.");
		copyDiagnostics.Pressed += () =>
		{
			GameContext.UserInterface?.PlayClickSound();
			DisplayServer.ClipboardSet(GameContext.Connectivity.CurrentEvidence.DiagnosticsBlock);
			ShowCopyFeedback(TranslationServer.Translate("Network diagnostics copied to clipboard."));
		};
		actions.AddChild(copyDiagnostics);

		_copyFeedbackLabel = new Label { Name = "HostTroubleshootingCopyFeedback_Label" };
		_copyFeedbackLabel.Visible = false;
		_copyFeedbackLabel.HorizontalAlignment = HorizontalAlignment.Center;
		_copyFeedbackLabel.AddThemeFontSizeOverride("font_size", 11);
		_copyFeedbackLabel.AddThemeColorOverride("font_color", UIStyle.ColorGoldDull);
		body.AddChild(_copyFeedbackLabel);

		// The card is added after the scene's own controls and spans the whole screen, so a back button left where the scene
		// put it is underneath the card and never sees a click.
		MoveChild(_backButton, GetChildCount() - 1);
	}

	private void ApplyStyles()
	{
		_backgroundPanel.AddThemeStyleboxOverride("panel", UIStyle.CreateBgGradient());
		_leftPillar.Visible = false;
		_rightPillar.Visible = false;

		UIStyle.ApplyTitle(_titleLabel, string.Empty, 30);
		LocalizedText.Set(_titleLabel, ConnectivitySentences.TroubleshootingTitle);
		_titleLabel.AddThemeColorOverride("font_color", UIStyle.ColorGold);
		_titleLabel.HorizontalAlignment = HorizontalAlignment.Center;

		UIStyle.ApplyBackButton(_backButton, () => GameContext.UserInterface?.TransitionTo(GameScreen.LobbyBrowser));
	}

	private void ShowCopyFeedback(string message)
	{
		if (_copyFeedbackLabel == null || !IsInstanceValid(_copyFeedbackLabel)) return;

		_copyFeedbackLabel.Text = message;
		_copyFeedbackLabel.Visible = true;
	}
}
