using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Godot;
using Realm.Client.Services;
using Realm.Client.Utils;
using Realm.Shared.Reachability;

namespace Realm.Client.UI;

/// <summary>
///     The one dialog both sides of a failed connection are explained with. It lists the causes that are real failure paths
///     in this build in the order a reader should work through them, starts on the one the game measured, and shows for the
///     chosen cause only its short explanation, at most two reference pages, one phrase to search for their own hardware, and
///     what to do next in this game. A check per cause and one full run say what this machine actually shows, and the full
///     run ranks what it found so each explanation can be read on its own.
/// </summary>
/// <remarks>
///     The dialog is deliberately the same for a host and for a joiner: the vocabulary, the checks and the way a cause is
///     drawn are shared, and only the copy and the actions differ, because a router that gives every player its own outside
///     port is the host's rule to make and the joiner's nothing to do. Nothing here decides anything about a match, and the
///     only thing it changes on the machine is the browser a page is opened in.
/// </remarks>
[GlobalClass]
public partial class ConnectivityHelpDialog : Control
{
	private const int CardWidth = 980;

	private const int ListWidth = 400;

	private const int ColumnsHeight = 340;

	private const string SearchEnginePrefix = "https://duckduckgo.com/?q=";

	private ConnectivityHelpRequest _request = null!;

	private readonly Dictionary<ConnectivityIssue, Button> _issueRows = new();

	private readonly Dictionary<ConnectivityIssue, ConnectivityDiagnosis> _diagnoses = new();

	private readonly CancellationTokenSource _cancellation = new();

	private VBoxContainer _body = null!;

	private VBoxContainer _issueRowsBox = null!;

	private VBoxContainer _detailBox = null!;

	private ScrollContainer _detailScroll = null!;

	private Label _titleLabel = null!;

	private Label _likelyCauseLabel = null!;

	private Label _statusLabel = null!;

	private Label _copyFeedbackLabel = null!;

	private Control? _directSection;

	private ConnectivityLikelyCause _likelyCause;

	private ConnectivityIssue _selectedIssue = ConnectivityIssue.RouterBlocksInboundPath;

	private IReadOnlyList<ConnectivityDiagnosis>? _ranked;

	private bool _running;

	private bool _closed;

	private bool _followUp;

	private string _statusKey = string.Empty;

	/// <summary>
	///     Raised once when the dialog goes away, whether by its own button or because the screen it was opened from is being
	///     left. A screen that has to react to the failure - returning to the lobby list, for instance - listens here instead of
	///     keeping a second copy of the dialog's close button.
	/// </summary>
	public event System.Action? Closed;

	public override void _Ready()
	{
		Name = "ConnectivityHelpDialog";
		SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
		MouseFilter = MouseFilterEnum.Stop;

		BuildLayout();
	}

	public override void _ExitTree()
	{
		_closed = true;

		// The source is cancelled rather than disposed: a reading that is still in flight builds a token linked to this one,
		// and a source disposed under it turns an ordinary cancellation into an exception nobody asked for.
		_cancellation.Cancel();
		base._ExitTree();
	}

	/// <summary>
	///     Fills the dialog for one side of a failed connection and starts the full run. The measured failure is already
	///     known, so the dialog is readable the moment it opens and the checks fill in behind it.
	/// </summary>
	/// <param name="request">What to explain, and what a reader can do about it from here.</param>
	public void Open(ConnectivityHelpRequest request)
	{
		_request = request;

		LocalizedText.Bind(_titleLabel, () => ConnectivityHelpText.Text(_request.TitleKey));

		ConnectivityFacts known = GameContext.Connectivity.DescribeKnownFacts(request.Audience);
		_likelyCause = ConnectivityCheck.DescribeLikelyCause(known);
		LocalizedText.Bind(_likelyCauseLabel, RenderLikelyCause);

		BuildPlayerJoinFailureLine();

		BuildIssueRows();
		BuildDirectConnectSection();
		BuildActions();

		SelectIssue(ConnectivityCheck.InitialIssue(known));
		_ = RunFullDiagnosticsAsync();
	}

	private void BuildLayout()
	{
		var backdrop = new ColorRect { Name = "ConnectivityHelpBackdrop", Color = new Color(0, 0, 0, 0.62f) };
		backdrop.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
		backdrop.MouseFilter = MouseFilterEnum.Stop;
		AddChild(backdrop);

		var center = UIStyle.CreateCenteredOverlay();
		center.Name = "ConnectivityHelpCenter";
		AddChild(center);

		var panel = new PanelContainer { Name = "ConnectivityHelpPanel" };
		panel.AddThemeStyleboxOverride("panel", UIStyle.CreateStonePanel(true));
		panel.CustomMinimumSize = new Vector2(CardWidth, 0);
		panel.SizeFlagsHorizontal = SizeFlags.ShrinkCenter;
		panel.SizeFlagsVertical = SizeFlags.ShrinkCenter;
		center.AddChild(panel);

		_body = new VBoxContainer { Name = "ConnectivityHelpBodyVBox" };
		_body.AddThemeConstantOverride("separation", 10);
		panel.AddChild(_body);

		_titleLabel = new Label { Name = "ConnectivityHelpTitle_Label" };
		UIStyle.ApplyTitle(_titleLabel, string.Empty, 20);
		_titleLabel.AddThemeColorOverride("font_color", new Color(0.9f, 0.3f, 0.3f));
		_titleLabel.HorizontalAlignment = HorizontalAlignment.Center;
		_body.AddChild(_titleLabel);

		var causeCaption = new Label { Name = "LikelyCauseCaption_Label" };
		causeCaption.Text = ConnectivityHelpText.Text(ConnectivitySentences.LikelyCauseCaption);
		causeCaption.AddThemeFontSizeOverride("font_size", 11);
		causeCaption.AddThemeColorOverride("font_color", UIStyle.ColorGoldDull);
		_body.AddChild(causeCaption);

		_likelyCauseLabel = new Label { Name = "LikelyCause_Label" };
		_likelyCauseLabel.AutowrapMode = TextServer.AutowrapMode.WordSmart;
		_likelyCauseLabel.CustomMinimumSize = new Vector2(CardWidth - 60, 0);
		_likelyCauseLabel.SizeFlagsHorizontal = SizeFlags.ExpandFill;
		_likelyCauseLabel.AddThemeFontSizeOverride("font_size", 14);
		_likelyCauseLabel.AddThemeColorOverride("font_color", UIStyle.ColorCyanGlow);
		_body.AddChild(_likelyCauseLabel);

		var causesCaption = new Label { Name = "CausesCaption_Label" };
		causesCaption.Text = ConnectivityHelpText.Text(ConnectivitySentences.CausesCaption);
		causesCaption.AddThemeFontSizeOverride("font_size", 11);
		causesCaption.AddThemeColorOverride("font_color", UIStyle.ColorGoldDull);
		_body.AddChild(causesCaption);

		var columns = new HBoxContainer { Name = "ConnectivityHelpColumns_HBoxContainer" };
		columns.AddThemeConstantOverride("separation", 16);
		columns.CustomMinimumSize = new Vector2(CardWidth - 60, ColumnsHeight);
		_body.AddChild(columns);

		var listScroll = new ScrollContainer { Name = "IssueListScroller" };
		listScroll.CustomMinimumSize = new Vector2(ListWidth, ColumnsHeight);
		listScroll.SizeFlagsVertical = SizeFlags.ExpandFill;
		listScroll.HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled;
		columns.AddChild(listScroll);

		_issueRowsBox = new VBoxContainer { Name = "IssueListVBox" };
		_issueRowsBox.AddThemeConstantOverride("separation", 6);
		_issueRowsBox.SizeFlagsHorizontal = SizeFlags.ExpandFill;
		listScroll.AddChild(_issueRowsBox);

		_detailScroll = new ScrollContainer { Name = "IssueDetailScroller" };
		_detailScroll.CustomMinimumSize = new Vector2(CardWidth - ListWidth - 90, ColumnsHeight);
		_detailScroll.SizeFlagsHorizontal = SizeFlags.ExpandFill;
		_detailScroll.SizeFlagsVertical = SizeFlags.ExpandFill;
		_detailScroll.HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled;
		columns.AddChild(_detailScroll);

		_detailBox = new VBoxContainer { Name = "IssueDetailVBox" };
		_detailBox.AddThemeConstantOverride("separation", 8);
		_detailBox.SizeFlagsHorizontal = SizeFlags.ExpandFill;
		_detailScroll.AddChild(_detailBox);

		_statusLabel = new Label { Name = "ConnectivityHelpStatus_Label" };
		_statusLabel.AutowrapMode = TextServer.AutowrapMode.WordSmart;
		_statusLabel.CustomMinimumSize = new Vector2(CardWidth - 60, 0);
		_statusLabel.SizeFlagsHorizontal = SizeFlags.ExpandFill;
		_statusLabel.AddThemeFontSizeOverride("font_size", 11);
		_statusLabel.AddThemeColorOverride("font_color", UIStyle.ColorGoldDull);
		LocalizedText.Bind(_statusLabel, RenderStatus);
		_body.AddChild(_statusLabel);
	}

	private void BuildIssueRows()
	{
		ForgetChildren(_issueRowsBox);
		_issueRows.Clear();
		_diagnoses.Clear();
		_ranked = null;

		IReadOnlyList<ConnectivityIssueContent> entries = ConnectivityIssueCatalog.For(_request.Audience);
		for (int index = 0; index < entries.Count; index++)
		{
			ConnectivityIssue issue = entries[index].Issue;
			Button row = UIStyle.CreateActionButton($"Issue_{issue}_Button", string.Empty, 13);
			row.Alignment = HorizontalAlignment.Left;
			row.AutowrapMode = TextServer.AutowrapMode.WordSmart;
			row.CustomMinimumSize = new Vector2(ListWidth - 24, 0);
			row.SizeFlagsHorizontal = SizeFlags.ExpandFill;
			row.Pressed += () =>
			{
				GameContext.UserInterface?.PlayClickSound();
				SelectIssue(issue);
			};
			row.MouseEntered += () => GameContext.UserInterface?.PlayHoverSound();
			LocalizedText.Bind(row, () => RenderIssueRow(issue));
			_issueRowsBox.AddChild(row);
			_issueRows[issue] = row;
		}
	}

	/// <summary>
	///     States a joining player's own failure under the likely cause, when one has been reported. It is the one piece of
	///     evidence that outranks this machine's measurements: a player who tried and failed disproves a lobby the lobby server
	///     measured as reachable, so it is shown as the lobby's own chat stated it rather than summarised.
	/// </summary>
	private void BuildPlayerJoinFailureLine()
	{
		string? reported = _request.PlayerJoinFailure?.Invoke();
		if (string.IsNullOrWhiteSpace(reported)) return;

		var caption = new Label { Name = "PlayerJoinFailureCaption_Label" };
		caption.Text = ConnectivityHelpText.Text(ConnectivitySentences.PlayerJoinFailureCaption);
		caption.AddThemeFontSizeOverride("font_size", 11);
		caption.AddThemeColorOverride("font_color", UIStyle.ColorGoldDull);
		_body.AddChild(caption);

		var reportedLabel = new Label { Name = "PlayerJoinFailure_Label" };
		reportedLabel.AutowrapMode = TextServer.AutowrapMode.WordSmart;
		reportedLabel.CustomMinimumSize = new Vector2(CardWidth - 60, 0);
		reportedLabel.SizeFlagsHorizontal = SizeFlags.ExpandFill;
		reportedLabel.AddThemeFontSizeOverride("font_size", 12);
		reportedLabel.AddThemeColorOverride("font_color", new Color(0.95f, 0.55f, 0.35f));
		LocalizedText.Bind(reportedLabel, () => _request.PlayerJoinFailure?.Invoke());
		_body.AddChild(reportedLabel);
	}

	private void BuildDirectConnectSection()
	{
		// A joining screen has no address of its own to offer: the address that matters is the host's, which is in the lobby
		// it could not reach, and the next step of every entry says to ask for it.
		if (_request.Audience != ConnectivityAudience.Hosting) return;

		string? address = _request.DirectConnectAddress?.Invoke();
		var section = new VBoxContainer { Name = "DirectConnectSection_VBox" };
		section.AddThemeConstantOverride("separation", 4);
		_body.AddChild(section);
		_directSection = section;

		if (string.IsNullOrEmpty(address))
		{
			var withheld = new Label { Name = "DirectConnectWithheld_Label" };
			withheld.AutowrapMode = TextServer.AutowrapMode.WordSmart;
			withheld.CustomMinimumSize = new Vector2(CardWidth - 60, 0);
			withheld.SizeFlagsHorizontal = SizeFlags.ExpandFill;
			withheld.AddThemeFontSizeOverride("font_size", 12);
			withheld.AddThemeColorOverride("font_color", UIStyle.ColorGoldDull);
			LocalizedText.Bind(withheld, () => ConnectivityHelpText.Text(ConnectivitySentences.DirectConnectWithheldNote));
			section.AddChild(withheld);
			return;
		}

		var caption = new Label { Name = "DirectConnectCaption_Label" };
		caption.Text = ConnectivityHelpText.Text(ConnectivitySentences.DirectConnectCaption);
		caption.AddThemeFontSizeOverride("font_size", 11);
		caption.AddThemeColorOverride("font_color", UIStyle.ColorGoldDull);
		section.AddChild(caption);

		var row = new HBoxContainer { Name = "DirectConnectRow_HBoxContainer" };
		row.Alignment = BoxContainer.AlignmentMode.Center;
		row.AddThemeConstantOverride("separation", 10);
		section.AddChild(row);

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
		section.AddChild(note);
	}

	private void BuildActions()
	{
		// A wrapping row rather than a plain one: which actions are offered depends on the failure, and a row that cannot wrap
		// would push the whole card wider than the window on the failures that offer the most of them.
		var actions = new HFlowContainer { Name = "ConnectivityHelpActions_HFlowContainer" };
		actions.Alignment = FlowContainer.AlignmentMode.Center;
		actions.AddThemeConstantOverride("h_separation", 12);
		actions.AddThemeConstantOverride("v_separation", 8);
		_body.AddChild(actions);

		if (_request.OffersRetry && _request.OnRetry != null)
		{
			Button retry = UIStyle.CreateActionButton("ConnectivityHelpRetry_Button", TranslationServer.Translate(ConnectivitySentences.TryAgainButton), 14);
			retry.CustomMinimumSize = new Vector2(190, 40);
			retry.Pressed += () => InvokeAndClose(_request.OnRetry);
			actions.AddChild(retry);
		}

		if (_request.OffersDirectHosting && _request.OnHostDirect != null)
		{
			Button direct = UIStyle.CreateActionButton("ConnectivityHelpDirect_Button", TranslationServer.Translate(ConnectivitySentences.HostDirectButton), 14);
			direct.CustomMinimumSize = new Vector2(230, 40);
			LocalizedText.SetTooltip(direct, "Open the same lobby without listing it anywhere. You then send its address to the players you want to join.");
			direct.Pressed += () => InvokeAndClose(_request.OnHostDirect);
			actions.AddChild(direct);
		}

		Button runAll = UIStyle.CreateActionButton("ConnectivityHelpRunAll_Button", TranslationServer.Translate(ConnectivitySentences.RunAllButton), 14);
		runAll.CustomMinimumSize = new Vector2(250, 40);
		LocalizedText.SetTooltip(runAll, "Check every cause in the list against this machine and the lobby server, and rank what was found.");
		runAll.Pressed += () =>
		{
			GameContext.UserInterface?.PlayClickSound();
			GameContext.Connectivity.DiscardReading();
			_ = RunFullDiagnosticsAsync();
		};
		actions.AddChild(runAll);

		if (_request.DiagnosticsBlock != null)
		{
			Button copyDiagnostics = UIStyle.CreateActionButton("ConnectivityHelpCopyDiagnostics_Button", TranslationServer.Translate(ConnectivitySentences.CopyDiagnosticsButton), 14);
			copyDiagnostics.CustomMinimumSize = new Vector2(230, 40);
			LocalizedText.SetTooltip(copyDiagnostics, "Copy everything a report about this machine's network needs, so it can be pasted into a message.");
			copyDiagnostics.Pressed += () =>
			{
				GameContext.UserInterface?.PlayClickSound();
				DisplayServer.ClipboardSet(_request.DiagnosticsBlock.Invoke());
				ShowCopyFeedback(TranslationServer.Translate("Network diagnostics copied."));
			};
			actions.AddChild(copyDiagnostics);
		}

		Button close = UIStyle.CreateActionButton("ConnectivityHelpClose_Button", TranslationServer.Translate("CLOSE"), 14);
		close.CustomMinimumSize = new Vector2(150, 40);
		close.Pressed += () =>
		{
			GameContext.UserInterface?.PlayClickSound();
			Close();
		};
		actions.AddChild(close);

		_copyFeedbackLabel = new Label { Name = "ConnectivityHelpCopyFeedback_Label" };
		_copyFeedbackLabel.Visible = false;
		_copyFeedbackLabel.HorizontalAlignment = HorizontalAlignment.Center;
		_copyFeedbackLabel.AddThemeFontSizeOverride("font_size", 11);
		_copyFeedbackLabel.AddThemeColorOverride("font_color", UIStyle.ColorGoldDull);
		_body.AddChild(_copyFeedbackLabel);
	}

	private void SelectIssue(ConnectivityIssue issue)
	{
		_selectedIssue = SelectableIssue(issue);

		foreach (KeyValuePair<ConnectivityIssue, Button> row in _issueRows)
		{
			bool selected = row.Key == _selectedIssue;
			if (selected) row.Value.AddThemeColorOverride("font_color", UIStyle.ColorCyanGlow);
			else row.Value.RemoveThemeColorOverride("font_color");
		}

		RenderDetail();
	}

	/// <summary>
	///     Keeps a selection inside the list this side of the connection actually carries. The two sides share the vocabulary
	///     but not the list, and a screen that selected an issue its own list does not state would show an entry nobody can go
	///     back to; the first entry is that case's honest answer.
	/// </summary>
	/// <param name="issue">The issue that wants selecting.</param>
	/// <returns>The issue to select.</returns>
	private ConnectivityIssue SelectableIssue(ConnectivityIssue issue)
	{
		IReadOnlyList<ConnectivityIssueContent> entries = ConnectivityIssueCatalog.For(_request.Audience);
		for (int index = 0; index < entries.Count; index++)
		{
			if (entries[index].Issue == issue) return issue;
		}

		return entries[0].Issue;
	}

	/// <summary>
	///     Draws the chosen cause: the ranked list of what the full run found, then that issue's explanation, pages, search
	///     phrase, what this machine shows for it and what to do next. Everything here is redrawn rather than patched, so a
	///     language change and a new verdict reach the same controls the same way.
	/// </summary>
	private void RenderDetail()
	{
		ForgetChildren(_detailBox);

		if (_ranked != null) RenderRankedSection(_ranked);

		ConnectivityIssueContent content = ConnectivityIssueCatalog.Describe(_selectedIssue, _request.Audience);

		var title = new Label { Name = "IssueTitle_Label" };
		title.AutowrapMode = TextServer.AutowrapMode.WordSmart;
		title.CustomMinimumSize = new Vector2(CardWidth - ListWidth - 120, 0);
		title.SizeFlagsHorizontal = SizeFlags.ExpandFill;
		title.AddThemeFontSizeOverride("font_size", 16);
		title.AddThemeColorOverride("font_color", UIStyle.ColorGold);
		LocalizedText.Bind(title, () => ConnectivityHelpText.Title(content));
		_detailBox.AddChild(title);

		var summary = new Label { Name = "IssueSummary_Label" };
		summary.AutowrapMode = TextServer.AutowrapMode.WordSmart;
		summary.CustomMinimumSize = new Vector2(CardWidth - ListWidth - 120, 0);
		summary.SizeFlagsHorizontal = SizeFlags.ExpandFill;
		summary.AddThemeFontSizeOverride("font_size", 13);
		summary.AddThemeColorOverride("font_color", new Color(0.9f, 0.91f, 0.95f));
		LocalizedText.Bind(summary, () => ConnectivityHelpText.Summary(content));
		_detailBox.AddChild(summary);

		var links = new HBoxContainer { Name = "IssueLinks_HBoxContainer" };
		links.AddThemeConstantOverride("separation", 10);
		_detailBox.AddChild(links);
		if (content.HasPrimaryLink) AddLinkButton(links, content.PrimaryLink, "IssuePrimaryLink_Button");
		if (content.HasSecondaryLink) AddLinkButton(links, content.SecondaryLink, "IssueSecondaryLink_Button");

		AddSearchRow(content);

		var verdict = new Label { Name = "IssueVerdict_Label" };
		verdict.AutowrapMode = TextServer.AutowrapMode.WordSmart;
		verdict.CustomMinimumSize = new Vector2(CardWidth - ListWidth - 120, 0);
		verdict.SizeFlagsHorizontal = SizeFlags.ExpandFill;
		verdict.AddThemeFontSizeOverride("font_size", 12);
		verdict.AddThemeColorOverride("font_color", UIStyle.ColorCyanGlow);
		LocalizedText.Bind(verdict, RenderSelectedVerdict);
		_detailBox.AddChild(verdict);

		Button diagnose = UIStyle.CreateActionButton("IssueDiagnose_Button", TranslationServer.Translate(ConnectivitySentences.DiagnoseButton), 13);
		diagnose.CustomMinimumSize = new Vector2(190, 34);
		diagnose.SizeFlagsHorizontal = SizeFlags.ShrinkBegin;
		diagnose.Pressed += () =>
		{
			GameContext.UserInterface?.PlayClickSound();
			GameContext.Connectivity.DiscardReading();
			_ = RunSingleAsync(_selectedIssue);
		};
		_detailBox.AddChild(diagnose);

		var nextCaption = new Label { Name = "IssueNextStepCaption_Label" };
		nextCaption.Text = ConnectivityHelpText.Text(ConnectivitySentences.NextStepCaption);
		nextCaption.AddThemeFontSizeOverride("font_size", 11);
		nextCaption.AddThemeColorOverride("font_color", UIStyle.ColorGoldDull);
		_detailBox.AddChild(nextCaption);

		var nextStep = new Label { Name = "IssueNextStep_Label" };
		nextStep.AutowrapMode = TextServer.AutowrapMode.WordSmart;
		nextStep.CustomMinimumSize = new Vector2(CardWidth - ListWidth - 120, 0);
		nextStep.SizeFlagsHorizontal = SizeFlags.ExpandFill;
		nextStep.AddThemeFontSizeOverride("font_size", 13);
		nextStep.AddThemeColorOverride("font_color", UIStyle.ColorGold);
		LocalizedText.Bind(nextStep, () => ConnectivityHelpText.NextStep(content, RenderNextStepValue()));
		_detailBox.AddChild(nextStep);

		_detailScroll.ScrollVertical = 0;
	}

	private void RenderRankedSection(IReadOnlyList<ConnectivityDiagnosis> ranked)
	{
		IReadOnlyList<ConnectivityDiagnosis> ordered = ConnectivityDiagnosisRanking.Order(ranked, _request.Audience);
		int shown = 0;
		for (int index = 0; index < ordered.Count; index++)
		{
			if (ordered[index].Status == ConnectivityDiagnosisStatus.RuledOut) break;
			shown++;
		}

		var caption = new Label { Name = "RankedCaption_Label" };
		caption.AutowrapMode = TextServer.AutowrapMode.WordSmart;
		caption.CustomMinimumSize = new Vector2(CardWidth - ListWidth - 120, 0);
		caption.SizeFlagsHorizontal = SizeFlags.ExpandFill;
		caption.AddThemeFontSizeOverride("font_size", 11);
		caption.AddThemeColorOverride("font_color", UIStyle.ColorGoldDull);
		LocalizedText.Bind(caption, () => shown > 0
			? ConnectivityHelpText.Text(ConnectivitySentences.RankedFoundCaption)
			: ConnectivityHelpText.Text(ConnectivitySentences.RankedNothingFound));
		_detailBox.AddChild(caption);

		for (int index = 0; index < shown; index++)
		{
			ConnectivityIssue issue = ordered[index].Issue;
			ConnectivityIssueContent content = ConnectivityIssueCatalog.Describe(issue, _request.Audience);
			Button entry = UIStyle.CreateActionButton($"Ranked_{issue}_Button", string.Empty, 13);
			entry.Alignment = HorizontalAlignment.Left;
			entry.AutowrapMode = TextServer.AutowrapMode.WordSmart;
			entry.CustomMinimumSize = new Vector2(CardWidth - ListWidth - 140, 0);
			entry.SizeFlagsHorizontal = SizeFlags.ExpandFill;
			entry.Pressed += () =>
			{
				GameContext.UserInterface?.PlayClickSound();
				SelectIssue(issue);
			};
			ConnectivityDiagnosis diagnosis = ordered[index];
			LocalizedText.Bind(entry, () => $"{ConnectivityHelpText.Title(content)}   ·   {RenderDiagnosisSentence(diagnosis)}");
			_detailBox.AddChild(entry);
		}

		var spacer = new Control { Name = "RankedSpacer_Control", CustomMinimumSize = new Vector2(0, 6) };
		_detailBox.AddChild(spacer);
	}

	private void AddLinkButton(Container parent, ConnectivityLink link, string nodeName)
	{
		Button button = UIStyle.CreateActionButton(nodeName, string.Empty, 12);
		button.CustomMinimumSize = new Vector2(0, 32);
		button.SizeFlagsHorizontal = SizeFlags.ShrinkBegin;
		button.Icon = UIStyle.GetUnicodeIconTexture(UnicodeIcons.LINK, 12, UIStyle.ColorGold);
		button.AddThemeConstantOverride("icon_max_width", 12);
		button.AddThemeConstantOverride("h_separation", 8);
		button.TooltipText = link.Url;
		LocalizedText.Bind(button, () => ConnectivityHelpText.LinkTitle(link));
		button.Pressed += () =>
		{
			GameContext.UserInterface?.PlayClickSound();
			OS.ShellOpen(link.Url);
		};
		parent.AddChild(button);
	}

	private void AddSearchRow(ConnectivityIssueContent content)
	{
		var caption = new Label { Name = "IssueSearchCaption_Label" };
		caption.Text = ConnectivityHelpText.Text(ConnectivitySentences.SearchCaption);
		caption.AddThemeFontSizeOverride("font_size", 11);
		caption.AddThemeColorOverride("font_color", UIStyle.ColorGoldDull);
		_detailBox.AddChild(caption);

		var row = new HBoxContainer { Name = "IssueSearchRow_HBoxContainer" };
		row.AddThemeConstantOverride("separation", 10);
		_detailBox.AddChild(row);

		var phrase = new Label { Name = "IssueSearchPhrase_Label" };
		phrase.AutowrapMode = TextServer.AutowrapMode.WordSmart;
		phrase.CustomMinimumSize = new Vector2(CardWidth - ListWidth - 260, 0);
		phrase.SizeFlagsHorizontal = SizeFlags.ExpandFill;
		phrase.AddThemeFontSizeOverride("font_size", 12);
		phrase.AddThemeColorOverride("font_color", new Color(0.88f, 0.9f, 0.95f));
		LocalizedText.Bind(phrase, () => ConnectivityHelpText.SearchPhrase(content));
		row.AddChild(phrase);

		Button search = UIStyle.CreateActionButton("IssueSearch_Button", TranslationServer.Translate(ConnectivitySentences.SearchButton), 12);
		search.CustomMinimumSize = new Vector2(130, 30);
		search.Icon = UIStyle.GetUnicodeIconTexture(UnicodeIcons.SEARCH, 12, UIStyle.ColorGold);
		search.AddThemeConstantOverride("icon_max_width", 12);
		search.AddThemeConstantOverride("h_separation", 8);
		LocalizedText.SetTooltip(search, "Open a web search for this phrase, in your browser, so you can add the model or the product you actually have.");
		search.Pressed += () =>
		{
			GameContext.UserInterface?.PlayClickSound();
			OS.ShellOpen(SearchEnginePrefix + Uri.EscapeDataString(ConnectivityHelpText.SearchPhrase(content)));
		};
		row.AddChild(search);
	}

	private async Task RunFullDiagnosticsAsync()
	{
		if (_running || _closed) return;

		_running = true;
		SetStatus(ConnectivitySentences.CheckingEveryCause);
		try
		{
			IReadOnlyList<ConnectivityDiagnosis> results = await GameContext.Connectivity.RunAllAsync(_request.Audience, _cancellation.Token);
			if (!CanDraw()) return;

			_ranked = results;
			for (int index = 0; index < results.Count; index++)
			{
				_diagnoses[results[index].Issue] = results[index];
			}

			RefreshIssueRows();
			RenderDetail();
		}
		catch (OperationCanceledException)
		{
		}
		catch (Exception exception)
		{
			GameLog.LogError($"[ConnectivityHelp] The full connectivity check could not run: {exception.GetType().Name}: {exception.Message}");
		}
		finally
		{
			_running = false;
			SetStatus(string.Empty);
		}
	}

	private async Task RunSingleAsync(ConnectivityIssue issue)
	{
		if (_running || _closed) return;

		_running = true;
		SetStatus(ConnectivitySentences.CheckingOneCause);
		try
		{
			ConnectivityDiagnosis diagnosis = await GameContext.Connectivity.RunAsync(_request.Audience, issue, _cancellation.Token);
			if (!CanDraw()) return;

			_diagnoses[issue] = diagnosis;
			RefreshIssueRows();
			RenderDetail();
		}
		catch (OperationCanceledException)
		{
		}
		catch (Exception exception)
		{
			GameLog.LogError($"[ConnectivityHelp] The check for '{issue}' could not run: {exception.GetType().Name}: {exception.Message}");
		}
		finally
		{
			_running = false;
			SetStatus(string.Empty);
		}
	}

	private void RefreshIssueRows()
	{
		foreach (KeyValuePair<ConnectivityIssue, Button> row in _issueRows)
		{
			ConnectivityIssue issue = row.Key;
			LocalizedText.Bind(row.Value, () => RenderIssueRow(issue));
		}
	}

	private string RenderIssueRow(ConnectivityIssue issue)
	{
		ConnectivityIssueContent content = ConnectivityIssueCatalog.Describe(issue, _request.Audience);
		string title = ConnectivityHelpText.Title(content);
		if (!_diagnoses.TryGetValue(issue, out ConnectivityDiagnosis diagnosis)) return title;

		return $"{title}   ·   {RenderStatusText(diagnosis.Status)}";
	}

	private string RenderSelectedVerdict()
	{
		if (!_diagnoses.TryGetValue(_selectedIssue, out ConnectivityDiagnosis diagnosis))
		{
			return ConnectivityHelpText.Text(ConnectivitySentences.VerdictUnchecked);
		}

		string sentence = RenderDiagnosisSentence(diagnosis);
		return diagnosis.TestDecides ? sentence : $"{sentence}\n{TranslationServer.Translate(ConnectivitySentences.VerdictNeedsResearch)}";
	}

	private static string RenderDiagnosisSentence(ConnectivityDiagnosis diagnosis)
	{
		string status = RenderStatusText(diagnosis.Status);
		return $"{status}: {ConnectivityHelpText.DiagnosisSentence(diagnosis)}";
	}

	private static string RenderStatusText(ConnectivityDiagnosisStatus status) => ConnectivityHelpText.Verdict(status);

	private string RenderLikelyCause()
	{
		return ConnectivityHelpText.LikelyCause(_likelyCause);
	}

	/// <summary>
	///     The value a next step states: the address players on this network join at, or the sentence that says where that
	///     address is when this dialog has none to offer.
	/// </summary>
	private string RenderNextStepValue()
	{
		string? address = _request.DirectConnectAddress?.Invoke();
		return string.IsNullOrEmpty(address) ? ConnectivityHelpText.Text(ConnectivitySentences.NextStepAddressFallback) : address;
	}

	private void ShowCopyFeedback(string message)
	{
		if (_copyFeedbackLabel == null || !IsInstanceValid(_copyFeedbackLabel)) return;

		_copyFeedbackLabel.Text = message;
		_copyFeedbackLabel.Visible = true;
	}

	private string RenderStatus()
	{
		return _statusKey.Length == 0 ? string.Empty : ConnectivityHelpText.Text(_statusKey);
	}

	private void SetStatus(string key)
	{
		_statusKey = key;
		if (_statusLabel == null || !IsInstanceValid(_statusLabel)) return;
		_statusLabel.Text = RenderStatus();
	}

	private bool CanDraw() => !_closed && GodotObject.IsInstanceValid(this) && IsInsideTree();

	private void InvokeAndClose(Action action)
	{
		GameContext.UserInterface?.PlayClickSound();
		_followUp = true;
		Close();
		action.Invoke();
	}

	private void Close()
	{
		_closed = true;
		_cancellation.Cancel();
		QueueFree();

		// A button that does something afterwards has already taken over what happens next, so the screen that opened the
		// dialog is not also told it was only closed: leaving the lobby room for the same press that re-hosts the lobby
		// would undo it.
		if (!_followUp) Closed?.Invoke();
	}

	/// <summary>
	///     Empties a container before it is drawn again. The children are freed rather than only detached, because a dialog
	///     that rebuilds its pane on every selection would otherwise leave one invisible tree behind per click.
	/// </summary>
	private static void ForgetChildren(Node parent)
	{
		foreach (Node child in parent.GetChildren())
		{
			parent.RemoveChild(child);
			child.QueueFree();
		}
	}
}
