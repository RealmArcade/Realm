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
///     The issue list every connectivity surface is built from: which side of a connection is being explained, the sentence
///     that names what the game measured, one entry per cause that side can actually fail on, what one cause's check found,
///     and what to do about it. One dialog draws this panel for every failure - a lobby that would not publish, a join that
///     did not get in, and a host asking about a lobby nothing has gone wrong with yet - so a cause is the same explanation
///     whichever door opened it; only the actions around the list differ.
/// </summary>
/// <remarks>
///     Nothing here picks a cause for the reader. The measured failure is stated above the list as a sentence, the list is one
///     dropdown holding only the causes that side can fail on, in the order they are the reason players cannot get in
///     anywhere in the world, and every entry can be checked on its own. A full run measures each cause and can then hide the
///     ones this machine was shown not to have, which is a filter over the same list rather than a second, differently
///     ordered list of the same causes.
/// </remarks>
[GlobalClass]
public partial class ConnectivityIssuePanel : VBoxContainer
{
	/// <summary>The width a card carrying this panel is built at, which every control below is measured against.</summary>
	public const int CardWidth = 980;

	/// <summary>The height the explanation is given before it scrolls, so a card that is showing one does not resize.</summary>
	public const int DetailHeight = 340;

	private const int ContentWidth = CardWidth - 60;

	private const string SearchEnginePrefix = "https://duckduckgo.com/?q=";

	private const int PlaceholderIndex = 0;

	private const int PlaceholderOffset = 1;

	private const int HostingScenarioIndex = 0;

	private const int JoiningScenarioIndex = 1;

	private readonly Dictionary<ConnectivityIssue, ConnectivityDiagnosis> _diagnoses = new();

	private readonly List<ConnectivityIssue> _listedIssues = new();

	private readonly CancellationTokenSource _cancellation = new();

	private VBoxContainer _detailBox = null!;

	private VBoxContainer _playerJoinFailureBox = null!;

	private ScrollContainer _detailScroll = null!;

	private OptionButton _scenarioPicker = null!;

	private OptionButton _issuePicker = null!;

	private CheckBox _likelyFilter = null!;

	private Label _likelyCauseLabel = null!;

	private Label _statusLabel = null!;

	private ConnectivityLikelyCause _likelyCause;

	private ConnectivityAudience _audience;

	private ConnectivityIssue? _selectedIssue;

	private bool _fullRunCompleted;

	private bool _closed;

	private bool _built;

	private string _statusKey = string.Empty;

	private int _runVersion;

	/// <summary>
	///     Raised when the reader changes which side of a connection is being explained. A card that draws an action around the
	///     list follows this, because what a host can do from here is not what a joining player can.
	/// </summary>
	public event Action<ConnectivityAudience>? ScenarioChanged;

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
	///     failure is stated as a sentence, and the reader picks the cause they want explained from the list.
	/// </summary>
	/// <param name="audience">Which side of the connection is being explained.</param>
	public void Open(ConnectivityAudience audience)
	{
		_audience = audience;
		if (!_built) BuildLayout();

		_scenarioPicker.Select(ScenarioIndexOf(audience));
		Restart();
	}

	private static int ScenarioIndexOf(ConnectivityAudience audience) => audience == ConnectivityAudience.Joining ? JoiningScenarioIndex : HostingScenarioIndex;

	private static ConnectivityAudience AudienceOf(int scenarioIndex) => scenarioIndex == JoiningScenarioIndex ? ConnectivityAudience.Joining : ConnectivityAudience.Hosting;

	/// <summary>
	///     Answers the same machine for the side of the connection the reader is on now: the measured line, the list that side
	///     can fail on, and the checks behind both. Every reading kept for the other side is dropped, because a conclusion
	///     about a host's router says nothing about a joining player's network.
	/// </summary>
	private void Restart()
	{
		ConnectivityFacts known = GameContext.Connectivity.DescribeKnownFacts(_audience);
		_likelyCause = ConnectivityCheck.DescribeLikelyCause(known);
		LocalizedText.Bind(_likelyCauseLabel, RenderLikelyCause);

		_diagnoses.Clear();
		_selectedIssue = null;
		_fullRunCompleted = false;
		_likelyFilter.Disabled = true;
		_likelyFilter.SetPressedNoSignal(false);

		BuildPlayerJoinFailureLine();
		BuildIssuePicker();
		RenderDetail();
		_ = RunFullDiagnosticsAsync();
	}

	private void BuildLayout()
	{
		_built = true;
		AddThemeConstantOverride("separation", 10);
		SizeFlagsHorizontal = SizeFlags.ExpandFill;

		BuildScenarioRow();

		var causeCaption = new Label { Name = "LikelyCauseCaption_Label" };
		causeCaption.Text = ConnectivityHelpText.Text(ConnectivitySentences.LikelyCauseCaption);
		causeCaption.AddThemeFontSizeOverride("font_size", 11);
		causeCaption.AddThemeColorOverride("font_color", UIStyle.ColorGoldDull);
		AddChild(causeCaption);

		_likelyCauseLabel = new Label { Name = "LikelyCause_Label" };
		_likelyCauseLabel.AutowrapMode = TextServer.AutowrapMode.WordSmart;
		_likelyCauseLabel.CustomMinimumSize = new Vector2(ContentWidth, 0);
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

		_likelyFilter = new CheckBox { Name = "FilterForLikely_CheckBox" };
		_likelyFilter.Disabled = true;
		_likelyFilter.FocusMode = FocusModeEnum.None;
		_likelyFilter.VerticalIconAlignment = VerticalAlignment.Center;
		_likelyFilter.AddThemeFontSizeOverride("font_size", 12);
		_likelyFilter.AddThemeColorOverride("font_color", UIStyle.ColorGoldDull);
		_likelyFilter.Toggled += OnLikelyFilterToggled;
		LocalizedText.Set(_likelyFilter, "Filter for most likely issues");
		runRow.AddChild(_likelyFilter);

		Button runAll = UIStyle.CreateActionButton("ConnectivityRunAll_Button", TranslationServer.Translate(ConnectivitySentences.RunAllButton), 13);
		runAll.CustomMinimumSize = new Vector2(250, 34);
		runAll.SizeFlagsHorizontal = SizeFlags.ShrinkEnd;
		LocalizedText.SetTooltip(runAll, "Check every cause in the list against this machine and the lobby server.");
		runAll.Pressed += () =>
		{
			GameContext.UserInterface?.PlayClickSound();
			GameContext.Connectivity.DiscardReading();
			_ = RunFullDiagnosticsAsync();
		};
		runRow.AddChild(runAll);

		_issuePicker = new OptionButton { Name = "IssuePicker_OptionButton" };
		_issuePicker.Set("icon_max_width", 0);
		_issuePicker.CustomMinimumSize = new Vector2(ContentWidth, 40);
		_issuePicker.SizeFlagsHorizontal = SizeFlags.ExpandFill;
		_issuePicker.FocusMode = FocusModeEnum.None;
		UIStyle.ApplyButtonStyleBoxesClearingFocus(_issuePicker);
		_issuePicker.AddThemeColorOverride("font_color", UIStyle.ColorCyanGlow);
		_issuePicker.AddThemeColorOverride("font_hover_color", UIStyle.ColorGold);
		_issuePicker.AddThemeColorOverride("font_pressed_color", UIStyle.ColorGold);
		_issuePicker.AddThemeColorOverride("font_focus_color", UIStyle.ColorCyanGlow);
		_issuePicker.AddThemeFontSizeOverride("font_size", 14);
		_issuePicker.ItemSelected += OnIssuePicked;
		_issuePicker.MouseEntered += () => GameContext.UserInterface?.PlayHoverSound();
		AddChild(_issuePicker);

		_detailScroll = new ScrollContainer { Name = "IssueDetailScroller" };
		_detailScroll.CustomMinimumSize = new Vector2(ContentWidth, DetailHeight);
		_detailScroll.SizeFlagsHorizontal = SizeFlags.ExpandFill;
		_detailScroll.SizeFlagsVertical = SizeFlags.ExpandFill;
		_detailScroll.HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled;
		_detailScroll.Visible = false;
		AddChild(_detailScroll);

		_detailBox = new VBoxContainer { Name = "IssueDetailVBox" };
		_detailBox.AddThemeConstantOverride("separation", 8);
		_detailBox.SizeFlagsHorizontal = SizeFlags.ExpandFill;
		_detailScroll.AddChild(_detailBox);

		_statusLabel = new Label { Name = "ConnectivityHelpStatus_Label" };
		_statusLabel.AutowrapMode = TextServer.AutowrapMode.WordSmart;
		_statusLabel.CustomMinimumSize = new Vector2(ContentWidth, 0);
		_statusLabel.SizeFlagsHorizontal = SizeFlags.ExpandFill;
		_statusLabel.AddThemeFontSizeOverride("font_size", 11);
		_statusLabel.AddThemeColorOverride("font_color", UIStyle.ColorGoldDull);
		LocalizedText.Bind(_statusLabel, RenderStatus);
		AddChild(_statusLabel);
	}

	/// <summary>
	///     The one control that changes what this panel is about. It sits above everything else because it decides the list
	///     below it, and it exists so the same failure is never explained on the wrong side of the connection: a host reading a
	///     joining player's copy would go looking for a VPN on a machine that has none.
	/// </summary>
	private void BuildScenarioRow()
	{
		var row = new HBoxContainer { Name = "ScenarioRow_HBoxContainer" };
		row.AddThemeConstantOverride("separation", 12);
		AddChild(row);

		var caption = new Label { Name = "ScenarioCaption_Label" };
		caption.VerticalAlignment = VerticalAlignment.Center;
		caption.AddThemeFontSizeOverride("font_size", 11);
		caption.AddThemeColorOverride("font_color", UIStyle.ColorGoldDull);
		LocalizedText.Set(caption, "Troubleshooting scenario:");
		row.AddChild(caption);

		_scenarioPicker = new OptionButton { Name = "TroubleshootingScenario_OptionButton" };
		_scenarioPicker.Set("icon_max_width", 0);
		_scenarioPicker.CustomMinimumSize = new Vector2(ContentWidth - 220, 36);
		_scenarioPicker.SizeFlagsHorizontal = SizeFlags.ExpandFill;
		_scenarioPicker.FocusMode = FocusModeEnum.None;
		UIStyle.ApplyButtonStyleBoxesClearingFocus(_scenarioPicker);
		_scenarioPicker.AddThemeColorOverride("font_color", UIStyle.ColorGold);
		_scenarioPicker.AddThemeColorOverride("font_hover_color", UIStyle.ColorCyanGlow);
		_scenarioPicker.AddThemeColorOverride("font_pressed_color", UIStyle.ColorCyanGlow);
		_scenarioPicker.AddThemeColorOverride("font_focus_color", UIStyle.ColorGold);
		_scenarioPicker.AddThemeFontSizeOverride("font_size", 14);
		_scenarioPicker.MouseEntered += () => GameContext.UserInterface?.PlayHoverSound();

		_scenarioPicker.AddItem(string.Empty);
		LocalizedText.BindOptionItem(_scenarioPicker, HostingScenarioIndex, () => ConnectivityHelpText.Text(ConnectivitySentences.ScenarioHosting));
		_scenarioPicker.AddItem(string.Empty);
		LocalizedText.BindOptionItem(_scenarioPicker, JoiningScenarioIndex, () => ConnectivityHelpText.Text(ConnectivitySentences.ScenarioJoining));
		_scenarioPicker.ItemSelected += OnScenarioPicked;
		row.AddChild(_scenarioPicker);
	}

	private void OnScenarioPicked(long index)
	{
		ConnectivityAudience audience = AudienceOf((int)index);
		if (audience == _audience) return;

		GameContext.UserInterface?.PlayClickSound();
		GameContext.Connectivity.DiscardReading();
		_audience = audience;
		Restart();
		ScenarioChanged?.Invoke(audience);
	}

	/// <summary>
	///     Fills the dropdown from the catalog for the side of the connection being explained, in the order the catalog states
	///     - how often each cause is the reason anywhere in the world, commonest first. A cause the other side cannot fail on is
	///     not in the list at all. A completed full run never reorders it: it only lets the filter hide what this machine was
	///     shown not to have, so two players comparing the same screen read the same list.
	/// </summary>
	private void BuildIssuePicker()
	{
		_issuePicker.Clear();
		_listedIssues.Clear();

		_issuePicker.AddItem(ConnectivityHelpText.Text(ConnectivitySentences.CausesPlaceholder));
		_issuePicker.SetItemDisabled(PlaceholderIndex, true);

		IReadOnlyList<ConnectivityIssueContent> entries = ConnectivityIssueCatalog.For(_audience);
		for (int index = 0; index < entries.Count; index++)
		{
			ConnectivityIssue issue = entries[index].Issue;
			if (IsHiddenByFilter(issue)) continue;

			int itemIndex = _listedIssues.Count;
			_listedIssues.Add(issue);
			_issuePicker.AddItem(RenderIssueRow(issue));
			LocalizedText.BindOptionItem(_issuePicker, itemIndex + PlaceholderOffset, () => RenderIssueRow(_listedIssues[itemIndex]));
		}

		int listedIndex = _selectedIssue.HasValue ? _listedIssues.IndexOf(_selectedIssue.Value) : -1;
		if (listedIndex >= 0)
		{
			_issuePicker.Selected = listedIndex + PlaceholderOffset;
			return;
		}

		_selectedIssue = null;
		_issuePicker.Selected = PlaceholderIndex;
	}

	private bool IsHiddenByFilter(ConnectivityIssue issue)
	{
		if (!_fullRunCompleted || !_likelyFilter.ButtonPressed) return false;

		return _diagnoses.TryGetValue(issue, out ConnectivityDiagnosis diagnosis) && diagnosis.Status == ConnectivityDiagnosisStatus.RuledOut;
	}

	private void OnLikelyFilterToggled(bool pressed)
	{
		GameContext.UserInterface?.PlayClickSound();
		BuildIssuePicker();
		RenderDetail();
	}

	private void OnIssuePicked(long index)
	{
		int listedIndex = (int)index - PlaceholderOffset;
		if (listedIndex < 0 || listedIndex >= _listedIssues.Count) return;

		GameContext.UserInterface?.PlayClickSound();
		_selectedIssue = _listedIssues[listedIndex];
		RenderDetail();
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
		reportedLabel.CustomMinimumSize = new Vector2(ContentWidth, 0);
		reportedLabel.SizeFlagsHorizontal = SizeFlags.ExpandFill;
		reportedLabel.AddThemeFontSizeOverride("font_size", 12);
		reportedLabel.AddThemeColorOverride("font_color", new Color(0.95f, 0.55f, 0.35f));
		reportedLabel.Text = reported;
		_playerJoinFailureBox.AddChild(reportedLabel);
	}

	/// <summary>
	///     Draws the chosen cause: that cause's explanation, its pages, its search phrase, what this machine shows for it and
	///     what to do next. Everything here is redrawn rather than patched, so a language change and a new verdict reach the
	///     same controls the same way. The pane is only on screen while a cause is chosen, so the card opens the height of the
	///     list rather than leaving an empty pane under it.
	/// </summary>
	private void RenderDetail()
	{
		ForgetChildren(_detailBox);
		_detailScroll.Visible = _selectedIssue != null;

		if (_selectedIssue != null) RenderIssueDetail(_selectedIssue.Value);

		_detailScroll.ScrollVertical = 0;
	}

	private void RenderIssueDetail(ConnectivityIssue issue)
	{
		ConnectivityIssueContent content = ConnectivityIssueCatalog.Describe(issue, _audience);

		var title = new Label { Name = "IssueTitle_Label" };
		title.AutowrapMode = TextServer.AutowrapMode.WordSmart;
		title.CustomMinimumSize = new Vector2(ContentWidth - 20, 0);
		title.SizeFlagsHorizontal = SizeFlags.ExpandFill;
		title.AddThemeFontSizeOverride("font_size", 16);
		title.AddThemeColorOverride("font_color", UIStyle.ColorGold);
		LocalizedText.Bind(title, () => ConnectivityHelpText.Title(content));
		_detailBox.AddChild(title);

		var summary = new Label { Name = "IssueSummary_Label" };
		summary.AutowrapMode = TextServer.AutowrapMode.WordSmart;
		summary.CustomMinimumSize = new Vector2(ContentWidth - 20, 0);
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
		verdict.CustomMinimumSize = new Vector2(ContentWidth - 20, 0);
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
		nextStep.CustomMinimumSize = new Vector2(ContentWidth - 20, 0);
		nextStep.SizeFlagsHorizontal = SizeFlags.ExpandFill;
		nextStep.AddThemeFontSizeOverride("font_size", 13);
		nextStep.AddThemeColorOverride("font_color", UIStyle.ColorGold);
		LocalizedText.Bind(nextStep, () => ConnectivityHelpText.NextStep(content, RenderNextStepValue()));
		_detailBox.AddChild(nextStep);
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
		phrase.CustomMinimumSize = new Vector2(ContentWidth - 260, 0);
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

	/// <summary>
	///     Runs every check, then lets the filter hide the causes this machine was shown not to have. The first completed run
	///     turns the filter on by itself, because a reader who asked every question wants the answers they still have to act
	///     on; a reader who turns it off keeps it off through the runs that follow. A run started before the reader switched
	///     sides is discarded when it arrives, so the answers on screen always belong to the side being asked about.
	/// </summary>
	private async Task RunFullDiagnosticsAsync()
	{
		if (_closed) return;

		int version = ++_runVersion;
		ConnectivityAudience audience = _audience;
		SetStatus(ConnectivitySentences.CheckingEveryCause);
		try
		{
			IReadOnlyList<ConnectivityDiagnosis> results = await GameContext.Connectivity.RunAllAsync(audience, _cancellation.Token);
			if (!CanDraw() || version != _runVersion) return;

			_diagnoses.Clear();
			for (int index = 0; index < results.Count; index++)
			{
				_diagnoses[results[index].Issue] = results[index];
			}

			bool firstRun = !_fullRunCompleted;
			_fullRunCompleted = true;
			_likelyFilter.Disabled = false;
			if (firstRun) _likelyFilter.SetPressedNoSignal(true);

			BuildIssuePicker();
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
			if (version == _runVersion) SetStatus(string.Empty);
		}
	}

	/// <summary>
	///     Runs one cause's check. Its answer is stated in the explanation the reader is already reading and on its own entry,
	///     and the list is not rebuilt: a cause that was just measured not to be the problem stays where the reader is looking
	///     for it until the next full run reshapes the list.
	/// </summary>
	/// <param name="issue">The cause to check.</param>
	private async Task RunSingleAsync(ConnectivityIssue issue)
	{
		if (_closed) return;

		// One cause is checked after the reader changed something, so the reading the full run kept is not what they are
		// asking about: this machine is asked again.
		GameContext.Connectivity.DiscardReading();

		int version = ++_runVersion;
		ConnectivityAudience audience = _audience;
		SetStatus(ConnectivitySentences.CheckingOneCause);
		try
		{
			ConnectivityDiagnosis diagnosis = await GameContext.Connectivity.RunAsync(audience, issue, _cancellation.Token);
			if (!CanDraw() || version != _runVersion) return;

			_diagnoses[issue] = diagnosis;
			RefreshIssuePickerText();
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
			if (version == _runVersion) SetStatus(string.Empty);
		}
	}

	private void RefreshIssuePickerText()
	{
		for (int index = 0; index < _listedIssues.Count; index++)
		{
			ConnectivityIssue issue = _listedIssues[index];
			LocalizedText.BindOptionItem(_issuePicker, index + PlaceholderOffset, () => RenderIssueRow(issue));
		}
	}

	private string RenderIssueRow(ConnectivityIssue issue)
	{
		return ConnectivityHelpText.Title(ConnectivityIssueCatalog.Describe(issue, _audience));
	}

	/// <summary>
	///     What one check measured, in the sentence that states it. No verdict word is added: the sentence itself says whether
	///     the lobby server reached this machine, and a word in front of it would have to be read as a rank the game never
	///     measured.
	/// </summary>
	private string RenderSelectedVerdict(ConnectivityIssue issue)
	{
		if (!_diagnoses.TryGetValue(issue, out ConnectivityDiagnosis diagnosis))
		{
			return ConnectivityHelpText.Text(ConnectivitySentences.VerdictUnchecked);
		}

		string sentence = ConnectivityHelpText.DiagnosisSentence(diagnosis);
		return diagnosis.TestDecides ? sentence : $"{sentence}\n{TranslationServer.Translate(ConnectivitySentences.VerdictNeedsResearch)}";
	}

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
