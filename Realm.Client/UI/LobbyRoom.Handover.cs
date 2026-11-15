using System;
using System.Collections.Generic;
using System.Text;
using Godot;
using Realm.Client.Network;
using Realm.Shared.Network;
using static Godot.Control;

namespace Realm.Client.UI;

/// <summary>
///     The lobby's host handover, as the two people performing it read it: either side can start the conversation, the
///     rehearsal's numbers are shown side by side for every seated player with the worst and the best beside the mean, and
///     nothing changes until both have confirmed a rehearsal the rules passed.
/// </summary>
public partial class LobbyRoom
{
	private Label? _handoverStatusLabel;

	private Button? _handoverOfferButton;

	private PanelContainer? _handoverPanel;

	private Label? _handoverTitleLabel;

	private Label? _handoverNoticeLabel;

	private VBoxContainer? _handoverRowsContainer;

	private Label? _handoverSummaryLabel;

	private Label? _handoverVerdictLabel;

	private Button? _handoverAcceptButton;

	private Button? _handoverRefuseButton;

	private Button? _handoverConfirmButton;

	private Button? _handoverCancelButton;

	private Button? _handoverCloseButton;

	private bool _handoverCandidatePickerOpen;

	/// <summary>
	///     Builds the handover's status line and its button beside the invite controls, and follows the lobby's handover for as
	///     long as the room is open.
	/// </summary>
	private void SetupHandoverUi()
	{
		_handoverStatusLabel = new Label();
		_handoverStatusLabel.Name = "HandoverStatus_Label";
		_handoverStatusLabel.HorizontalAlignment = HorizontalAlignment.Center;
		_handoverStatusLabel.VerticalAlignment = VerticalAlignment.Center;
		_handoverStatusLabel.AddThemeFontSizeOverride("font_size", 14);
		_handoverStatusLabel.AddThemeColorOverride("font_color", UIStyle.ColorGoldDull);
		_handoverStatusLabel.AutowrapMode = TextServer.AutowrapMode.WordSmart;
		_handoverStatusLabel.CustomMinimumSize = new Vector2(760, 0);
		_handoverStatusLabel.SizeFlagsHorizontal = SizeFlags.ExpandFill;
		_handoverStatusLabel.Visible = false;
		AddChild(_handoverStatusLabel);
		LocalizedText.Bind(_handoverStatusLabel, () => DescribeHandoverStatus(Lobby.HandoverStage, Lobby.IsHandoverParty));
		_handoverStatusLabel.SetAnchorsPreset(LayoutPreset.CenterBottom);
		_handoverStatusLabel.GrowHorizontal = GrowDirection.Both;
		_handoverStatusLabel.GrowVertical = GrowDirection.Begin;
		_handoverStatusLabel.OffsetLeft = -380;
		_handoverStatusLabel.OffsetRight = 380;
		_handoverStatusLabel.OffsetTop = -120;
		_handoverStatusLabel.OffsetBottom = -70;

		CreateHandoverOfferButton();
		CreateHandoverPanel();

		Lobby.HandoverStateChanged += RefreshHandoverUi;
		Lobby.HandoverSuggested += OnHandoverSuggested;

		RefreshHandoverUi();
	}

	private void TeardownHandoverUi()
	{
		Lobby.HandoverStateChanged -= RefreshHandoverUi;
		Lobby.HandoverSuggested -= OnHandoverSuggested;
	}

	private void CreateHandoverOfferButton()
	{
		if (_shareAddressButton.GetParent() is not Control inviteRow) return;

		_handoverOfferButton = UIStyle.CreateActionButton("HandoverOffer_Button", TranslationServer.Translate("HOST HANDOVER"), 13);
		_handoverOfferButton.CustomMinimumSize = new Vector2(240, 40);
		_handoverOfferButton.AddThemeConstantOverride("icon_max_width", 0);
		_handoverOfferButton.TooltipText = TranslationServer.Translate("Offer a host handover. The lobby moves only after a measured rehearsal and both players confirming it.");
		_handoverOfferButton.Pressed += OnHandoverOfferPressed;

		inviteRow.AddChild(_handoverOfferButton);
		inviteRow.MoveChild(_handoverOfferButton, _shareAddressButton.GetIndex() + 1);
	}

	private void CreateHandoverPanel()
	{
		_handoverPanel = new PanelContainer();
		_handoverPanel.Name = "HandoverPanel_PanelContainer";
		_handoverPanel.Visible = false;
		_handoverPanel.CustomMinimumSize = new Vector2(820, 0);
		AddChild(_handoverPanel);
		_handoverPanel.SetAnchorsPreset(LayoutPreset.Center);
		_handoverPanel.GrowHorizontal = GrowDirection.Both;
		_handoverPanel.GrowVertical = GrowDirection.Both;
		_handoverPanel.OffsetLeft = -410;
		_handoverPanel.OffsetRight = 410;
		_handoverPanel.OffsetTop = -300;
		_handoverPanel.OffsetBottom = 300;

		var margin = new MarginContainer();
		margin.Name = "HandoverPanel_MarginContainer";
		margin.AddThemeConstantOverride("margin_left", 24);
		margin.AddThemeConstantOverride("margin_right", 24);
		margin.AddThemeConstantOverride("margin_top", 18);
		margin.AddThemeConstantOverride("margin_bottom", 18);
		_handoverPanel.AddChild(margin);

		var column = new VBoxContainer();
		column.Name = "HandoverPanel_VBoxContainer";
		column.AddThemeConstantOverride("separation", 10);
		margin.AddChild(column);

		_handoverTitleLabel = new Label();
		_handoverTitleLabel.Name = "HandoverTitle_Label";
		_handoverTitleLabel.HorizontalAlignment = HorizontalAlignment.Center;
		_handoverTitleLabel.AddThemeFontSizeOverride("font_size", 20);
		LocalizedText.Set(_handoverTitleLabel, "HOST HANDOVER");
		column.AddChild(_handoverTitleLabel);

		_handoverNoticeLabel = new Label();
		_handoverNoticeLabel.Name = "HandoverNotice_Label";
		_handoverNoticeLabel.AddThemeFontSizeOverride("font_size", 14);
		_handoverNoticeLabel.AutowrapMode = TextServer.AutowrapMode.WordSmart;
		_handoverNoticeLabel.CustomMinimumSize = new Vector2(760, 0);
		_handoverNoticeLabel.SizeFlagsHorizontal = SizeFlags.ExpandFill;
		column.AddChild(_handoverNoticeLabel);
		LocalizedText.Bind(_handoverNoticeLabel, () => Lobby.HandoverNoticeText);

		var scroll = new ScrollContainer();
		scroll.Name = "HandoverRows_ScrollContainer";
		scroll.CustomMinimumSize = new Vector2(760, 260);
		scroll.SizeFlagsHorizontal = SizeFlags.ExpandFill;
		scroll.SizeFlagsVertical = SizeFlags.ExpandFill;
		column.AddChild(scroll);

		_handoverRowsContainer = new VBoxContainer();
		_handoverRowsContainer.Name = "HandoverRows_VBoxContainer";
		_handoverRowsContainer.AddThemeConstantOverride("separation", 6);
		_handoverRowsContainer.SizeFlagsHorizontal = SizeFlags.ExpandFill;
		scroll.AddChild(_handoverRowsContainer);

		_handoverSummaryLabel = new Label();
		_handoverSummaryLabel.Name = "HandoverSummary_Label";
		_handoverSummaryLabel.AddThemeFontSizeOverride("font_size", 14);
		_handoverSummaryLabel.AutowrapMode = TextServer.AutowrapMode.WordSmart;
		_handoverSummaryLabel.CustomMinimumSize = new Vector2(760, 0);
		_handoverSummaryLabel.SizeFlagsHorizontal = SizeFlags.ExpandFill;
		column.AddChild(_handoverSummaryLabel);
		LocalizedText.Bind(_handoverSummaryLabel, BuildHandoverSummaryText);

		_handoverVerdictLabel = new Label();
		_handoverVerdictLabel.Name = "HandoverVerdict_Label";
		_handoverVerdictLabel.AddThemeFontSizeOverride("font_size", 15);
		_handoverVerdictLabel.AutowrapMode = TextServer.AutowrapMode.WordSmart;
		_handoverVerdictLabel.CustomMinimumSize = new Vector2(760, 0);
		_handoverVerdictLabel.SizeFlagsHorizontal = SizeFlags.ExpandFill;
		column.AddChild(_handoverVerdictLabel);
		LocalizedText.Bind(_handoverVerdictLabel, BuildHandoverVerdictText);

		var buttons = new HBoxContainer();
		buttons.Name = "HandoverButtons_HBoxContainer";
		buttons.AddThemeConstantOverride("separation", 12);
		buttons.Alignment = BoxContainer.AlignmentMode.Center;
		column.AddChild(buttons);

		_handoverAcceptButton = AddHandoverButton(buttons, "HandoverAccept_Button", OnHandoverAcceptPressed);
		LocalizedText.Set(_handoverAcceptButton, "ACCEPT");
		_handoverRefuseButton = AddHandoverButton(buttons, "HandoverRefuse_Button", OnHandoverRefusePressed);
		LocalizedText.Set(_handoverRefuseButton, "REFUSE");
		_handoverConfirmButton = AddHandoverButton(buttons, "HandoverConfirm_Button", OnHandoverConfirmPressed);
		LocalizedText.Set(_handoverConfirmButton, "CONFIRM HANDOVER");
		_handoverCancelButton = AddHandoverButton(buttons, "HandoverCancel_Button", OnHandoverCancelPressed);
		LocalizedText.Set(_handoverCancelButton, "CANCEL");
		_handoverCloseButton = AddHandoverButton(buttons, "HandoverClose_Button", OnHandoverClosePressed);
		LocalizedText.Set(_handoverCloseButton, "CLOSE");
	}

	private static Button AddHandoverButton(Container row, string nodeName, Action onPressed)
	{
		Button button = UIStyle.CreateActionButton(nodeName, string.Empty, 14);
		button.CustomMinimumSize = new Vector2(170, 42);
		button.AddThemeConstantOverride("icon_max_width", 0);
		button.Pressed += onPressed;
		row.AddChild(button);
		return button;
	}

	private void OnHandoverOfferPressed()
	{
		if (Lobby.IsHandoverCandidate || (Lobby.IsHandoverParty && Lobby.HandoverStage != LobbyHandoverStage.None))
		{
			RefreshHandoverUi();
			return;
		}

		if (!Lobby.IsHost)
		{
			Lobby.OfferThisSeatAsHost();
			return;
		}

		_handoverCandidatePickerOpen = !_handoverCandidatePickerOpen;
		RefreshHandoverUi();
	}

	private void OnHandoverAcceptPressed() => Lobby.AcceptHandoverOffer();

	private void OnHandoverRefusePressed()
	{
		Lobby.RefuseHandoverOffer();
		RefreshHandoverUi();
	}

	private void OnHandoverConfirmPressed() => Lobby.ConfirmHandoverOffer();

	private void OnHandoverCancelPressed()
	{
		Lobby.WithdrawHandoverOffer();
		RefreshHandoverUi();
	}

	private void OnHandoverClosePressed()
	{
		_handoverCandidatePickerOpen = false;
		RefreshHandoverUi();
	}

	private void OnHandoverSuggested()
	{
		// A suggestion is advice, so it is said where the lobby already says things: a line the host and the candidate read,
		// with the numbers the advice is based on.
		PlayerInfo? suggested = Lobby.PlayerList.Find(player => player.PeerId == Lobby.SuggestedCandidatePeerId);
		if (suggested == null) return;

		if (Lobby.HasHandoverReport)
		{
			LobbyReportSummary summary = LobbyReportSummary.Read(Lobby.HandoverReport);
			OnLobbyChatReceived(TranslationServer.Translate("System"),
				string.Format(TranslationServer.Translate("A better-placed host was suggested: {0}. Round trip to that player would average {1} ms (worst {2} ms) instead of {3} ms (worst {4} ms)."),
					suggested.Name, summary.CandidateMean, summary.CandidateWorst, summary.CurrentMean, summary.CurrentWorst),
				false, DateTime.UtcNow.Ticks);
		}

		RefreshHandoverUi();
	}

	/// <summary>
	///     Redraws the handover's status line and panel from the lobby's own state. Every sentence is built where it is drawn,
	///     so a language change rebuilds it rather than leaving a formatted string behind.
	/// </summary>
	private void RefreshHandoverUi()
	{
		if (_handoverOfferButton == null || _handoverStatusLabel == null || _handoverPanel == null) return;
		if (Lobby.IsSinglePlayer)
		{
			_handoverOfferButton.Visible = false;
			_handoverStatusLabel.Visible = false;
			_handoverPanel.Visible = false;
			return;
		}

		LobbyHandoverStage stage = Lobby.HandoverStage;
		bool party = Lobby.IsHandoverParty;

		_handoverOfferButton.Visible = !Lobby.IsGameStarted;
		// A lobby whose authority could not open its probe relay cannot measure a rehearsal at all, so the offer is not
		// offered and the status line says why instead of promising a handover that could never pass.
		_handoverOfferButton.Disabled = Lobby.ProbeRelayPort <= 0 || stage != LobbyHandoverStage.None || Lobby.IsCountdownActive;
		RefreshHandoverOfferButton();

		string status = DescribeHandoverStatus(stage, party);
		_handoverStatusLabel.Visible = status.Length > 0;

		bool showPanel = _handoverCandidatePickerOpen || (stage != LobbyHandoverStage.None && (party || Lobby.IsHost));
		_handoverPanel.Visible = showPanel;
		if (!showPanel) return;

		_handoverAcceptButton!.Visible = stage == LobbyHandoverStage.Offered && (party || Lobby.IsHost);
		_handoverRefuseButton!.Visible = party && (stage is LobbyHandoverStage.Offered or LobbyHandoverStage.Rehearsing or LobbyHandoverStage.Decided);
		_handoverConfirmButton!.Visible = party && stage == LobbyHandoverStage.Decided;
		_handoverConfirmButton.Disabled = !Lobby.CanConfirmHandover;
		_handoverCancelButton!.Visible = party && (stage is LobbyHandoverStage.Offered or LobbyHandoverStage.Rehearsing or LobbyHandoverStage.Decided);
		_handoverCloseButton!.Visible = true;

		PopulateHandoverRows(stage);
	}

	private string DescribeHandoverStatus(LobbyHandoverStage stage, bool party)
	{
		if (stage == LobbyHandoverStage.None)
		{
			if (Lobby.IsSuggestedHostCandidate) return TranslationServer.Translate("A handover would improve this lobby for everybody; the numbers are in the advice this screen was sent.");
			if (Lobby.ProbeRelayPort <= 0 && !Lobby.IsGameStarted) return HandoverNoticeTexts.Translate(LobbyHandoverNotice.NoProbeRelay);
			return string.Empty;
		}

		string sentence = Lobby.HandoverNoticeText;
		if (sentence.Length == 0) sentence = TranslationServer.Translate("A host handover is in progress.");
		return party && Lobby.HandoverSecondsRemaining > 0 ? $"{sentence} ({Lobby.HandoverSecondsRemaining}s)" : sentence;
	}

	/// <summary>
	///     Names the offer button for what this screen can do with it, which is offering itself when it is not the host and
	///     offering the role to somebody else when it is.
	/// </summary>
	private void RefreshHandoverOfferButton()
	{
		if (_handoverOfferButton == null) return;

		if (Lobby.IsHost) LocalizedText.Set(_handoverOfferButton, "HAND OVER HOST");
		else LocalizedText.Set(_handoverOfferButton, "OFFER TO HOST");
	}

	private void PopulateHandoverRows(LobbyHandoverStage stage)
	{
		if (_handoverRowsContainer == null || _handoverSummaryLabel == null || _handoverVerdictLabel == null) return;

		foreach (Node child in _handoverRowsContainer.GetChildren()) child.QueueFree();

		if (stage == LobbyHandoverStage.None && _handoverCandidatePickerOpen)
		{
			PopulateHandoverCandidatePicker();
			LocalizedText.RefreshAll();
			return;
		}

		if (!Lobby.HasHandoverReport)
		{
			AddHandoverLine("Waiting for the rehearsal's measurements of every seated player against {0}.", HandoverCandidateName());
			LocalizedText.RefreshAll();
			return;
		}

		LobbyHandoverReport report = Lobby.HandoverReport;
		LobbyHandoverSeatMetric[] seats = report.Seats ?? [];

		AddHandoverLine("Player — now (to this host) — proposed (to {0})", HandoverCandidateName());
		for (int index = 0; index < seats.Length; index++)
		{
			LobbyHandoverSeatMetric seat = seats[index];
			string now = DescribeSeatLink(seat.Name, seat.CurrentHostMeasured, seat.CurrentHostRoundTripMilliseconds, seat.CurrentHostJitterMilliseconds, seat.CurrentHostPacketLossPercent);
			string proposed = DescribeSeatLink(seat.Name, seat.CandidateMeasured, seat.CandidateRoundTripMilliseconds, seat.CandidateJitterMilliseconds, seat.CandidatePacketLossPercent);
			AddHandoverLine("{0}: {1} → {2}", seat.Name, now, proposed);
		}

		LocalizedText.RefreshAll();

		LobbyHandoverVerdict verdict = (LobbyHandoverVerdict)report.Verdict;
		_handoverVerdictLabel.AddThemeColorOverride("font_color", verdict == LobbyHandoverVerdict.Passed ? UIStyle.ColorGold : new Color(0.9f, 0.35f, 0.3f));
	}

	/// <summary>
	///     Renders one seat's measured link, stating a measurement that was not taken as such rather than as a good number.
	/// </summary>
	private static string DescribeSeatLink(string name, bool measured, int roundTripMilliseconds, int jitterMilliseconds, float packetLossPercent)
	{
		if (!measured) return TranslationServer.Translate("not measured");

		return string.Format(TranslationServer.Translate("{0} ms, {1} ms jitter, {2}% loss"), roundTripMilliseconds, jitterMilliseconds, Math.Round(packetLossPercent));
	}

	/// <summary>
	///     Builds the rehearsal's summary where it is drawn, so a language change redraws it in the new language instead of
	///     leaving the sentence the screen happened to build once.
	/// </summary>
	private string BuildHandoverSummaryText()
	{
		if (!Lobby.HasHandoverReport) return string.Empty;

		LobbyHandoverReport report = Lobby.HandoverReport;
		LobbyReportSummary summary = LobbyReportSummary.Read(report);

		var summaryText = new StringBuilder();
		summaryText.AppendLine(string.Format(TranslationServer.Translate("Round trip now: best {0} ms, worst {1} ms, mean {2} ms."), summary.CurrentBest, summary.CurrentWorst, summary.CurrentMean));
		summaryText.AppendLine(string.Format(TranslationServer.Translate("Round trip proposed: best {0} ms, worst {1} ms, mean {2} ms."), summary.CandidateBest, summary.CandidateWorst, summary.CandidateMean));
		summaryText.AppendLine(string.Format(TranslationServer.Translate("Machine stability now: {0}; proposed: {1}."), DescribeStability(report.CurrentHostStabilityRank), DescribeStability(report.CandidateStabilityRank)));
		summaryText.AppendLine(string.Format(TranslationServer.Translate("The new host can be reached from outside: {0}; it holds this lobby's map at its version: {1}."),
			report.CandidateEndpointVerified ? TranslationServer.Translate("measured") : TranslationServer.Translate("not measured"),
			report.MapMatched ? TranslationServer.Translate("yes") : TranslationServer.Translate("no")));
		return summaryText.ToString().TrimEnd();
	}

	/// <summary>Builds the rehearsal's verdict where it is drawn, for the same reason the summary is.</summary>
	private string BuildHandoverVerdictText()
	{
		return Lobby.HasHandoverReport ? DescribeVerdict((LobbyHandoverVerdict)Lobby.HandoverReport.Verdict) : string.Empty;
	}

	private void PopulateHandoverCandidatePicker()
	{
		AddHandoverLine("Choose the player to offer the host role to. They answer it, and nothing changes until a rehearsal passes and both of you confirm it.");

		foreach (PlayerInfo player in Lobby.PlayerList)
		{
			if (player.IsComputer || player.PeerId <= 0 || player.IsHost || player.IsDisconnected) continue;

			int peerId = player.PeerId;
			Button offer = UIStyle.CreateActionButton($"HandoverOfferTo_{player.Slot}_Button", player.Name, 14);
			offer.CustomMinimumSize = new Vector2(720, 34);
			offer.AddThemeConstantOverride("icon_max_width", 0);
			LocalizedText.SetTooltip(offer, "Offer this player the lobby's host role.");
			offer.Pressed += () =>
			{
				Lobby.OfferHandoverTo(peerId);
				_handoverCandidatePickerOpen = false;
				RefreshHandoverUi();
			};
			_handoverRowsContainer!.AddChild(offer);
		}
	}

	private void AddHandoverLine(string key, params object?[] arguments)
	{
		var label = new Label();
		label.Name = "HandoverLine_Label";
		LocalizedText.Set(label, key, arguments);
		label.AddThemeFontSizeOverride("font_size", 13);
		label.AutowrapMode = TextServer.AutowrapMode.WordSmart;
		label.CustomMinimumSize = new Vector2(740, 0);
		label.SizeFlagsHorizontal = SizeFlags.ExpandFill;
		_handoverRowsContainer!.AddChild(label);
	}

	private string HandoverCandidateName()
	{
		foreach (PlayerInfo player in Lobby.PlayerList)
		{
			if (player.PeerId == Lobby.HandoverCandidatePeerId) return player.Name;
		}

		return TranslationServer.Translate("the candidate");
	}

	private static string DescribeStability(int rank)
	{
		return rank switch
		{
			HostStabilityRanks.Excellent => TranslationServer.Translate("excellent"),
			HostStabilityRanks.Average => TranslationServer.Translate("average"),
			HostStabilityRanks.Poor => TranslationServer.Translate("poor"),
			_ => TranslationServer.Translate("not measured")
		};
	}

	private static string DescribeVerdict(LobbyHandoverVerdict verdict)
	{
		return verdict switch
		{
			LobbyHandoverVerdict.Passed => TranslationServer.Translate("The rehearsal passed: every seated player reached the candidate and nobody is left materially worse off. Both parties confirm to continue."),
			LobbyHandoverVerdict.RefusedSeatUnreachable => TranslationServer.Translate("Refused automatically: a seated player did not reach the candidate."),
			LobbyHandoverVerdict.RefusedSeatUnplayable => TranslationServer.Translate("Refused automatically: a seated player would be left on a link nobody could play the match on."),
			LobbyHandoverVerdict.RefusedEndpointUnreachable => TranslationServer.Translate("Refused automatically: no lobby server reached the candidate's own game port, so the lobby could not be published on it."),
			LobbyHandoverVerdict.RefusedMapMismatch => TranslationServer.Translate("Refused automatically: the candidate does not hold this lobby's map at its exact version."),
			LobbyHandoverVerdict.RefusedMachineCannotHoldTick => TranslationServer.Translate("Refused automatically: the candidate's machine is measured as unable to hold the tick."),
			LobbyHandoverVerdict.RefusedStabilityUnknown => TranslationServer.Translate("Refused: the candidate's machine stability was never measured, and unknown is not a reason to move a lobby."),
			LobbyHandoverVerdict.RefusedPlayerMateriallyWorseOff => TranslationServer.Translate("Refused automatically: moving the lobby would leave a seated player materially worse off."),
			_ => TranslationServer.Translate("Not measured yet, so nothing has been decided.")
		};
	}
}

/// <summary>
///     The best, worst and mean of both sides of a handover rehearsal, read once so every screen draws the same summary
///     from the same measurements.
/// </summary>
public readonly record struct LobbyReportSummary(int CurrentBest, int CurrentWorst, int CurrentMean, int CandidateBest, int CandidateWorst, int CandidateMean)
{
	/// <summary>Summarises a report's two sides.</summary>
	/// <param name="report">The rehearsal's measurements.</param>
	/// <returns>The two summaries, each zero when nothing was measured.</returns>
	public static LobbyReportSummary Read(LobbyHandoverReport report)
	{
		HostHandoverRules.TrySummarize(report, candidateSide: false, out int currentMinimum, out int currentMaximum, out int currentMean, out _);
		HostHandoverRules.TrySummarize(report, candidateSide: true, out int candidateMinimum, out int candidateMaximum, out int candidateMean, out _);
		return new LobbyReportSummary(currentMinimum, currentMaximum, currentMean, candidateMinimum, candidateMaximum, candidateMean);
	}
}
