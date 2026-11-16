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
///     The issue list every connectivity surface is built from: the sentence that names what the game measured, one row per
///     cause this build can actually fail on, what one cause's check found, and the ranked answers of a full run. The host's
///     dialog, the joining player's dialog and the host troubleshooting screen all draw this panel, because a cause is the
///     same explanation whichever surface opened it; only the actions around it differ.
/// </summary>
/// <remarks>
///     Nothing here picks a cause for the reader. The measured failure is stated above the list as a sentence, and every row
///     is a question the reader chooses to ask: a measurement that names one cause is not the same thing as a check that
///     ruled the others out, so the panel opens with nothing selected and says so. Each row can be checked on its own, and
///     one button runs every check and ranks what this machine showed.
/// </remarks>
[GlobalClass]
public partial class ConnectivityIssuePanel : VBoxContainer
{
	/// <summary>The width a card carrying this panel is built at, which every column below is measured against.</summary>
	public const int CardWidth = 980;

	/// <summary>The width the issue list takes, leaving the rest of the card to the explanation beside it.</summary>
	public const int ListWidth = 400;

	/// <summary>The height the list and the explanation are given before either scrolls, so the card is a stable size.</summary>
	public const int ColumnsHeight = 340;

	private const string SearchEnginePrefix = "https://duckduckgo.com/?q=";

	private readonly Dictionary<ConnectivityIssue, Button> _issueRows = new();

	private readonly Dictionary<ConnectivityIssue, ConnectivityDiagnosis> _diagnoses = new();

	private readonly CancellationTokenSource _cancellation = new();

	private VBoxContainer _issueRowsBox = null!;

	private VBoxContainer _detailBox = null!;

	private VBoxContainer _playerJoinFailureBox = null!;

	private ScrollContainer _detailScroll = null!;

	private Label _likelyCauseLabel = null!;

	private Label _statusLabel = null!;

	private ConnectivityLikelyCause _likelyCause;

	private ConnectivityAudience _audience;

	private ConnectivityIssue? _selectedIssue;

	private IReadOnlyList<ConnectivityDiagnosis>? _ranked;

	private bool _running;

	private bool _closed;

	private bool _built;

	private string _statusKey = string.Empty;

	public override void _Ready()
	{
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
	///     Fills the panel for one side of a failed connection and starts the full run. Nothing is selected: the measured
	///     failure is stated as a sentence, and the reader picks the cause they want explained or checks them all at once.
	/// </summary>
	/// <param name="audience">Which side of the connection is being explained.</param>
	public void Open(ConnectivityAudience audience)
	{
		_audience = audience;
		if (!_built) BuildLayout();

		ConnectivityFacts known = GameContext.Connectivity.DescribeKnownFacts(audience);
		_likelyCause = ConnectivityCheck.DescribeLikelyCause(known);
		LocalizedText.Bind(_likelyCauseLabel, RenderLikelyCause);

		BuildPlayerJoinFailureLine();
		BuildIssueRows();

		_selectedIssue = null;
		RenderDetail();
		_ = RunFullDiagnosticsAsync();
	}

	private void BuildLayout()
	{
		_built = true;
		AddThemeConstantOverride("separation", 10);
		SizeFlagsHorizontal = SizeFlags.ExpandFill;

		var causeCaption = new Label { Name = "LikelyCauseCaption_Label" };
		causeCaption.Text = ConnectivityHelpText.Text(ConnectivitySentences.LikelyCauseCaption);
		causeCaption.AddThemeFontSizeOverride("font_size", 11);
		causeCaption.AddThemeColorOverride("font_color", UIStyle.ColorGoldDull);
		AddChild(causeCaption);

		_likelyCauseLabel = new Label { Name = "LikelyCause_Label" };
		_likelyCauseLabel.AutowrapMode = TextServer.AutowrapMode.WordSmart;
		_likelyCauseLabel.CustomMinimumSize = new Vector2(CardWidth - 60, 0);
		_likelyCauseLabel.SizeFlagsHorizontal = SizeFlags.ExpandFill;
		_likelyCauseLabel.AddThemeFontSizeOverride("font_size", 14);
		_likelyCauseLabel.AddThemeColorOverride("font_color", UIStyle.ColorCyanGlow);
		AddChild(_likelyCauseLabel);

		_playerJoinFailureBox = new VBoxContainer { Name = "PlayerJoinFailure_VBox" };
		_playerJoinFailureBox.AddThemeConstantOverride("separation", 4);
		AddChild(_playerJoinFailureBox);

		var runRow = new HBoxContainer { Name = "CausesHeader_HBoxContainer" };
		runRow.AddThemeConstantOverride("separation", 12);
		AddChild(runRow);

		var causesCaption = new Label { Name = "CausesCaption_Label" };
		causesCaption.Text = ConnectivityHelpText.Text(ConnectivitySentences.CausesCaption);
		causesCaption.SizeFlagsHorizontal = SizeFlags.ExpandFill;
		causesCaption.VerticalAlignment = VerticalAlignment.Center;
		causesCaption.AddThemeFontSizeOverride("font_size", 11);
		causesCaption.AddThemeColorOverride("font_color", UIStyle.ColorGoldDull);
		runRow.AddChild(causesCaption);

		Button runAll = UIStyle.CreateActionButton("ConnectivityRunAll_Button", TranslationServer.Translate(ConnectivitySentences.RunAllButton), 13);
		runAll.CustomMinimumSize = new Vector2(250, 34);
		runAll.SizeFlagsHorizontal = SizeFlags.ShrinkEnd;
		LocalizedText.SetTooltip(runAll, "Check every cause in the list against this machine and the lobby server, and rank what was found.");
		runAll.Pressed += () =>
		{
			GameContext.UserInterface?.PlayClickSound();
			GameContext.Connectivity.DiscardReading();
			_ = RunFullDiagnosticsAsync();
		};
		runRow.AddChild(runAll);

		var columns = new HBoxContainer { Name = "ConnectivityHelpColumns_HBoxContainer" };
		columns.AddThemeConstantOverride("separation", 16);
		columns.CustomMinimumSize = new Vector2(CardWidth - 60, ColumnsHeight);
		AddChild(columns);

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
		AddChild(_statusLabel);
	}

	private void BuildIssueRows()
	{
		ForgetChildren(_issueRowsBox);
		_issueRows.Clear();
		_diagnoses.Clear();
		_ranked = null;

		IReadOnlyList<ConnectivityIssueContent> entries = ConnectivityIssueCatalog.For(_audience);
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
		ForgetChildren(_playerJoinFailureBox);
		if (_audience != ConnectivityAudience.Hosting) return;

		string? reported = GameContext.Connectivity.CurrentEvidence.PlayerJoinFailure;
		if (string.IsNullOrWhiteSpace(reported)) return;

		var caption = new Label { Name = "PlayerJoinFailureCaption_Label" };
		caption.Text = ConnectivityHelpText.Text(ConnectivitySentences.PlayerJoinFailureCaption);
		caption.AddThemeFontSizeOverride("font_size", 11);
		caption.AddThemeColorOverride("font_color", UIStyle.ColorGoldDull);
		_playerJoinFailureBox.AddChild(caption);

		var reportedLabel = new Label { Name = "PlayerJoinFailure_Label" };
		reportedLabel.AutowrapMode = TextServer.AutowrapMode.WordSmart;
		reportedLabel.CustomMinimumSize = new Vector2(CardWidth - 60, 0);
		reportedLabel.SizeFlagsHorizontal = SizeFlags.ExpandFill;
		reportedLabel.AddThemeFontSizeOverride("font_size", 12);
		reportedLabel.AddThemeColorOverride("font_color", new Color(0.95f, 0.55f, 0.35f));
		reportedLabel.Text = reported;
		_playerJoinFailureBox.AddChild(reportedLabel);
	}

	private void SelectIssue(ConnectivityIssue issue)
	{
		_selectedIssue = issue;

		foreach (KeyValuePair<ConnectivityIssue, Button> row in _issueRows)
		{
			if (row.Key == issue) row.Value.AddThemeColorOverride("font_color", UIStyle.ColorCyanGlow);
			else row.Value.RemoveThemeColorOverride("font_color");
		}

		RenderDetail();
	}

	/// <summary>
	///     Draws the chosen cause, or the list of what the full run found while nothing is chosen: the ranked answers, that
	///     issue's explanation, its pages, its search phrase, what this machine shows for it and what to do next. Everything
	///     here is redrawn rather than patched, so a language change and a new verdict reach the same controls the same way.
	/// </summary>
	private void RenderDetail()
	{
		ForgetChildren(_detailBox);

		if (_selectedIssue == null) RenderOpeningPane();
		else RenderIssueDetail(_selectedIssue.Value);

		_detailScroll.ScrollVertical = 0;
	}

	private void RenderOpeningPane()
	{
		var prompt = new Label { Name = "CausesPrompt_Label" };
		prompt.AutowrapMode = TextServer.AutowrapMode.WordSmart;
		prompt.CustomMinimumSize = new Vector2(CardWidth - ListWidth - 120, 0);
		prompt.SizeFlagsHorizontal = SizeFlags.ExpandFill;
		prompt.AddThemeFontSizeOverride("font_size", 13);
		prompt.AddThemeColorOverride("font_color", new Color(0.9f, 0.91f, 0.95f));
		LocalizedText.Bind(prompt, () => ConnectivityHelpText.Text(ConnectivitySentences.CausesPrompt));
		_detailBox.AddChild(prompt);

		if (_ranked != null) RenderRankedSection(_ranked);
	}

	private void RenderIssueDetail(ConnectivityIssue issue)
	{
		if (_ranked != null) RenderRankedSection(_ranked);

		ConnectivityIssueContent content = ConnectivityIssueCatalog.Describe(issue, _audience);

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
		LocalizedText.Bind(verdict, () => RenderSelectedVerdict(issue));
		_detailBox.AddChild(verdict);

		Button diagnose = UIStyle.CreateActionButton("IssueDiagnose_Button", TranslationServer.Translate(ConnectivitySentences.DiagnoseButton), 13);
		diagnose.CustomMinimumSize = new Vector2(190, 34);
		diagnose.SizeFlagsHorizontal = SizeFlags.ShrinkBegin;
		diagnose.Pressed += () =>
		{
			GameContext.UserInterface?.PlayClickSound();
			_ = RunSingleAsync(issue);
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
	}

	private void RenderRankedSection(IReadOnlyList<ConnectivityDiagnosis> ranked)
	{
		IReadOnlyList<ConnectivityDiagnosis> ordered = ConnectivityDiagnosisRanking.Order(ranked, _audience);
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
			ConnectivityIssueContent content = ConnectivityIssueCatalog.Describe(issue, _audience);
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
			IReadOnlyList<ConnectivityDiagnosis> results = await GameContext.Connectivity.RunAllAsync(_audience, _cancellation.Token);
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

		// One cause is checked after the reader changed something, so the reading the full run kept is not what they are
		// asking about: this machine is asked again.
		GameContext.Connectivity.DiscardReading();

		_running = true;
		SetStatus(ConnectivitySentences.CheckingOneCause);
		try
		{
			ConnectivityDiagnosis diagnosis = await GameContext.Connectivity.RunAsync(_audience, issue, _cancellation.Token);
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
		ConnectivityIssueContent content = ConnectivityIssueCatalog.Describe(issue, _audience);
		string title = ConnectivityHelpText.Title(content);
		if (!_diagnoses.TryGetValue(issue, out ConnectivityDiagnosis diagnosis)) return title;

		return $"{title}   ·   {RenderStatusText(diagnosis.Status)}";
	}

	private string RenderSelectedVerdict(ConnectivityIssue issue)
	{
		if (!_diagnoses.TryGetValue(issue, out ConnectivityDiagnosis diagnosis))
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
	///     address is when there is none to offer.
	/// </summary>
	private string RenderNextStepValue()
	{
		string? address = GameContext.Connectivity.CurrentEvidence.DirectConnectAddress;
		return string.IsNullOrEmpty(address) ? ConnectivityHelpText.Text(ConnectivitySentences.NextStepAddressFallback) : address;
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

	/// <summary>
	///     Empties a container before it is drawn again. The children are freed rather than only detached, because a panel
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
