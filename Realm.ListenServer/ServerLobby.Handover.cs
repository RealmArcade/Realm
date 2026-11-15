using System.Numerics;
using MemoryPack;
using Realm.Ecs;
using Realm.Shared.Network;

namespace Realm.ListenServer;

/// <summary>the host handover a lobby offers, rehearses and hands itself over by.</summary>
public partial class ServerLobby
	{
	private LobbyHandoverStage _handoverStage;

	private long _handoverOfferRevision;

	private int _handoverOffererPeerId;

	private int _handoverCandidatePeerId;

	/// <summary>The party that has not answered an offer yet, which is the one the offer is aimed at.</summary>
	private int _handoverAwaitingPeerId;

	private double _handoverStageRemainingSeconds;

	private bool _handoverHostConfirmed;

	private bool _handoverCandidateConfirmed;

	private string _handoverNotice = string.Empty;

	private int _suggestedCandidatePeerId;

	/// <summary>
	///     What the candidate stated it measured. It carries the new authority's endpoint and the credentials it will admit
	///     the seats by, so it is held here and never published in the lobby state every seat reads.
	/// </summary>
	private LobbyHandoverReport _handoverReport;

	private readonly Dictionary<int, LobbyHandoverLinkRow> _handoverMeasurements = new();

	private double _handoverMeasurementsRemainingSeconds;

	private const double HandoverMeasurementWindowSeconds = 20.0;

	/// <summary>
	///     Whether the seats have been told where their lobby is moving to. The seats are told once the host that is leaving
	///     has permitted the transfer, so the screen taking the lobby over completes the registry's ownership before any
	///     other seat starts moving.
	/// </summary>
	private bool _handoverCommitsSent;

	private const double HandoverOwnershipPatienceSeconds = 5.0;

	/// <summary>
	///     The port of this lobby's handover probe relay, so every seated screen can register with it and measure a path to
	///     another seat. Zero when the authority could not open one, which is what stops a lobby from rehearsing at all.
	/// </summary>
	public int ProbeRelayPort { get; set; }

	/// <summary>
	///     How far this lobby's host handover has got, which every seat reads. A lobby nobody offered a handover in stays at
	///     <see cref="LobbyHandoverStage.None" /> and behaves exactly as it did before the handover existed.
	/// </summary>
	public LobbyHandoverStage HandoverStage => _handoverStage;

	/// <summary>Whether a handover has been committed and the seats are moving to the candidate's authority.</summary>
	public bool IsHandoverCommitting => _handoverStage == LobbyHandoverStage.Committing;

	private static bool IsHandoverCommand(LobbyCommandKind kind)
	{
		return kind is LobbyCommandKind.OfferHostHandover
			or LobbyCommandKind.AcceptHostHandover
			or LobbyCommandKind.RefuseHostHandover
			or LobbyCommandKind.WithdrawHostHandover
			or LobbyCommandKind.ConfirmHostHandover
			or LobbyCommandKind.ReportHostHandoverReport
			or LobbyCommandKind.ReportHandoverMeasurements
			or LobbyCommandKind.ReportSeatStability
			or LobbyCommandKind.AbortHostHandover;
	}

	/// <summary>
	///     Answers whether a token is the probe token of a seat this lobby holds, which is the whole of what the probe relay
	///     trusts: a relay that introduced strangers would be a way to make a lobby's authority hand out endpoints.
	/// </summary>
	/// <param name="probeToken">The token a probe datagram stated.</param>
	/// <returns>Whether a seat of this lobby holds that token.</returns>
	public bool HoldsProbeToken(string probeToken)
	{
		if (string.IsNullOrEmpty(probeToken)) return false;

		foreach (LobbySeat seat in _seats.Values)
		{
			if (seat.IsComputer) continue;
			if (string.IsNullOrEmpty(seat.ProbeToken)) continue;
			if (string.Equals(seat.ProbeToken, probeToken, StringComparison.Ordinal)) return true;
		}

		return false;
	}

	private void HandleHandoverCommand(int peerId, bool isAdmin, LobbySeat sender, LobbyCommand command)
	{
		switch (command.Kind)
		{
			case LobbyCommandKind.ReportSeatStability:
				ReportSeatStability(sender, command.Text);
				break;
			case LobbyCommandKind.OfferHostHandover:
				OfferHandover(peerId, isAdmin, sender, command.TargetPeerId);
				break;
			case LobbyCommandKind.AcceptHostHandover:
				AcceptHandover(peerId);
				break;
			case LobbyCommandKind.RefuseHostHandover:
				RefuseHandover(peerId, refusedByHost: peerId == _adminPeerId);
				break;
			case LobbyCommandKind.WithdrawHostHandover:
				WithdrawHandover(peerId);
				break;
			case LobbyCommandKind.ConfirmHostHandover:
				ConfirmHandover(peerId);
				break;
			case LobbyCommandKind.ReportHostHandoverReport:
				AcceptRehearsalReport(peerId, command.Report);
				break;
			case LobbyCommandKind.ReportHandoverMeasurements:
				RecordMeasurements(sender, command.Measurements);
				break;
			case LobbyCommandKind.AbortHostHandover:
				AbortCommittedHandover(peerId, command.Text);
				break;
			default:
				_log.Write($"peer {peerId} sent a handover command this build does not accept");
				break;
		}
	}

	private void ReportSeatStability(LobbySeat sender, string stability)
	{
		string stated = LobbyLimits.Sanitize(stability, 16);
		if (string.Equals(sender.HostStability, stated, StringComparison.Ordinal)) return;

		sender.HostStability = stated;
		_seats[sender.Slot] = sender;
		BroadcastState();
	}

	private void OfferHandover(int peerId, bool isAdmin, LobbySeat sender, int targetPeerId)
	{
		if (_handoverStage != LobbyHandoverStage.None)
		{
			AnswerHandoverRefusal(peerId, LobbyHandoverNotice.OneAtATime);
			return;
		}

		if (_countdownActive)
		{
			AnswerHandoverRefusal(peerId, LobbyHandoverNotice.CountdownRunning);
			return;
		}

		int candidatePeerId;
		int offererPeerId;
		int awaitingPeerId;

		if (targetPeerId <= 0)
		{
			candidatePeerId = peerId;
			offererPeerId = peerId;
			awaitingPeerId = _adminPeerId;
		}
		else
		{
			if (!isAdmin)
			{
				_log.Write($"peer {peerId} offered the lobby to peer {targetPeerId} without administering it; the offer is refused");
				AnswerHandoverRefusal(peerId, LobbyHandoverNotice.NotTheCandidate);
				return;
			}

			candidatePeerId = targetPeerId;
			offererPeerId = peerId;
			awaitingPeerId = targetPeerId;
		}

		if (candidatePeerId == _adminPeerId)
		{
			AnswerHandoverRefusal(peerId, LobbyHandoverNotice.AlreadyHost);
			return;
		}

		if (!TryGetSeat(candidatePeerId, out LobbySeat candidate))
		{
			AnswerHandoverRefusal(peerId, LobbyHandoverNotice.UnknownSeat);
			return;
		}

		if (candidate.IsComputer)
		{
			AnswerHandoverRefusal(peerId, LobbyHandoverNotice.ComputerCannotHost);
			return;
		}

		if (candidate.IsDisconnected)
		{
			AnswerHandoverRefusal(peerId, LobbyHandoverNotice.UnknownSeat);
			return;
		}

		_handoverStage = LobbyHandoverStage.Offered;
		_handoverOfferRevision++;
		_handoverOffererPeerId = offererPeerId;
		_handoverCandidatePeerId = candidatePeerId;
		_handoverAwaitingPeerId = awaitingPeerId;
		_handoverStageRemainingSeconds = HostHandoverRules.OfferedSeconds;
		_handoverHostConfirmed = false;
		_handoverCandidateConfirmed = false;
		_handoverReport = default;
		_handoverNotice = targetPeerId <= 0 ? LobbyHandoverNotice.OfferedToHost : LobbyHandoverNotice.OfferedByHost;
		_suggestedCandidatePeerId = 0;

		_log.Write($"peer {peerId} ('{sender.Name}') offered the lobby to peer {candidatePeerId} ('{candidate.Name}'); the answer waits for peer {awaitingPeerId}");
		BroadcastState();
	}

	private void AcceptHandover(int peerId)
	{
		if (_handoverStage != LobbyHandoverStage.Offered || peerId != _handoverAwaitingPeerId)
		{
			_log.Write($"peer {peerId} answered a handover offer it is not a party to; nothing changed");
			return;
		}

		_handoverStage = LobbyHandoverStage.Rehearsing;
		_handoverStageRemainingSeconds = HostHandoverRules.RehearsalSeconds;
		_handoverAwaitingPeerId = 0;
		_handoverNotice = LobbyHandoverNotice.RehearsalStarted;
		_log.Write($"peer {peerId} accepted the handover offer; peer {_handoverCandidatePeerId} now rehearses it against every seat");
		BroadcastState();
	}

	private void RefuseHandover(int peerId, bool refusedByHost)
	{
		if (_handoverStage is not (LobbyHandoverStage.Offered or LobbyHandoverStage.Rehearsing or LobbyHandoverStage.Decided)) return;
		if (peerId != _handoverCandidatePeerId && peerId != _adminPeerId && peerId != _handoverAwaitingPeerId) return;

		string notice = refusedByHost ? LobbyHandoverNotice.HostRefused : LobbyHandoverNotice.CandidateRefused;
		CloseHandover(notice, notifySeats: true);
	}

	private void WithdrawHandover(int peerId)
	{
		if (_handoverStage == LobbyHandoverStage.None || _handoverStage == LobbyHandoverStage.Committing) return;
		if (peerId != _handoverCandidatePeerId && peerId != _adminPeerId && peerId != _handoverAwaitingPeerId) return;

		CloseHandover(_handoverStage == LobbyHandoverStage.Rehearsing ? LobbyHandoverNotice.RehearsalCancelled : LobbyHandoverNotice.Withdrawn, notifySeats: true);
	}

	private void ConfirmHandover(int peerId)
	{
		if (_handoverStage != LobbyHandoverStage.Decided) return;
		if (peerId != _handoverCandidatePeerId && peerId != _adminPeerId) return;
		if (HostHandoverRules.Verdict(_handoverReport) != LobbyHandoverVerdict.Passed) return;

		if (peerId == _adminPeerId) _handoverHostConfirmed = true;
		else _handoverCandidateConfirmed = true;

		_log.Write($"peer {peerId} confirmed the handover; host confirmed {_handoverHostConfirmed}, candidate confirmed {_handoverCandidateConfirmed}");
		if (!_handoverHostConfirmed || !_handoverCandidateConfirmed)
		{
			BroadcastState();
			return;
		}

		BeginHandoverCommit();
	}

	/// <summary>
	///     Judges the report a candidate stated, publishes the verdict to both parties, and moves the lobby to the
	///     confirmation stage. A report that does not describe every seat or does not state a usable endpoint is refused
	///     without changing anything, because a rehearsal that could not measure is never a pass.
	/// </summary>
	/// <param name="peerId">The seat that stated the report.</param>
	/// <param name="report">What it measured.</param>
	private void AcceptRehearsalReport(int peerId, in LobbyHandoverReport report)
	{
		if (_handoverStage != LobbyHandoverStage.Rehearsing || peerId != _handoverCandidatePeerId)
		{
			_log.Write($"peer {peerId} stated a handover rehearsal outside the rehearsal it is a party to; the report is ignored");
			return;
		}

		string complaint = DescribeUnusableReport(report);
		if (complaint.Length > 0)
		{
			_log.Write($"the handover rehearsal of peer {peerId} was not usable: {complaint}");
			_handoverNotice = LobbyHandoverNotice.RehearsalIncomplete;
			LobbyHandoverReport refused = report;
			refused.Verdict = (int)LobbyHandoverVerdict.NotMeasured;
			refused.Failure = complaint;
			NotifyHandoverParty(_adminPeerId, LobbyHandoverMessageKind.Evidence, refused, LobbyHandoverNotice.RehearsalIncomplete);
			NotifyHandoverParty(_handoverCandidatePeerId, LobbyHandoverMessageKind.Evidence, refused, LobbyHandoverNotice.RehearsalIncomplete);
			BroadcastState();
			return;
		}

		_handoverReport = report;
		_handoverReport.CurrentHostStabilityRank = HostStabilityRanks.Rank(HostStability);
		_handoverReport.Verdict = (int)HostHandoverRules.Verdict(_handoverReport);
		_handoverStage = LobbyHandoverStage.Decided;
		_handoverStageRemainingSeconds = HostHandoverRules.OfferedSeconds;
		_handoverHostConfirmed = false;
		_handoverCandidateConfirmed = false;
		_handoverNotice = _handoverReport.Verdict == (int)LobbyHandoverVerdict.Passed ? LobbyHandoverNotice.AwaitingConfirmation : LobbyHandoverNotice.VerdictRefused;

		_log.Write($"peer {peerId} rehearsed the handover and the lobby judged it {_handoverReport.Verdict}");
		NotifyHandoverParty(_adminPeerId, LobbyHandoverMessageKind.Evidence, _handoverReport, _handoverNotice);
		NotifyHandoverParty(_handoverCandidatePeerId, LobbyHandoverMessageKind.Evidence, _handoverReport, _handoverNotice);
		BroadcastState();
	}

	private string DescribeUnusableReport(in LobbyHandoverReport report)
	{
		LobbyHandoverSeatMetric[] seats = report.Seats ?? [];
		if (seats.Length == 0) return "the report states no seat measurements";
		if (string.IsNullOrWhiteSpace(report.CandidateEndpointAddress)) return "the report states no endpoint for the new authority";
		if (report.CandidateEndpointPort <= 0 || report.CandidateEndpointPort > 65535) return "the report states no usable port for the new authority";

		LobbyHandoverSeatCredential[] credentials = report.Credentials ?? [];
		foreach (LobbySeat seat in _seats.Values)
		{
			if (seat.IsComputer) continue;

			bool found = false;
			for (int index = 0; index < credentials.Length; index++)
			{
				if (credentials[index].Slot != seat.Slot) continue;
				if (credentials[index].AccountId != seat.AccountId) continue;
				if (string.IsNullOrEmpty(credentials[index].SessionToken)) continue;
				found = true;
				break;
			}

			if (!found) return $"the report states no credential for seat {seat.Slot}";
		}

		return string.Empty;
	}

	/// <summary>
	///     Moves the lobby to the transfer: every seat is told where the new authority is and which credential it is
	///     admitted by, and the candidate is told the same. Nothing about the lobby has changed yet, and a transfer that
	///     does not complete returns it to the host it had.
	/// </summary>
	private void BeginHandoverCommit()
	{
		_handoverStage = LobbyHandoverStage.Committing;
		_handoverStageRemainingSeconds = HostHandoverRules.SettleSeconds;
		_handoverNotice = LobbyHandoverNotice.Confirmed;
		_handoverCommitsSent = false;
		_log.Write($"the handover of this lobby to peer {_handoverCandidatePeerId} is committed; the host is being asked to permit the transfer");

		// The host that is leaving speaks to its registry node first, so the screen taking the lobby over holds the
		// permission before any other seat starts moving. Once that permission is stated, every seat is told where to go.
		NotifyHandoverParty(_adminPeerId, LobbyHandoverMessageKind.Commit, default, string.Empty, DescribeCommitFor(_adminPeerId));

		AddSystemChat($"Host handover to {HandoverCandidateName()} is committed. Reconnecting every seat to the new host.");
		BroadcastState();
	}

	private LobbyHandoverMessage DescribeCommitFor(int peerId)
	{
		LobbySeat seat = default;
		if (_slotByPeer.TryGetValue(peerId, out int slot)) _seats.TryGetValue(slot, out seat);

		return new LobbyHandoverMessage
		{
			Kind = LobbyHandoverMessageKind.Commit,
			OfferRevision = _handoverOfferRevision,
			Address = _handoverReport.CandidateEndpointAddress,
			Port = _handoverReport.CandidateEndpointPort,
			SessionToken = CredentialForSlot(seat.Slot),
			LobbyId = ActiveLobbyIdOrEmpty()
		};
	}

	/// <summary>
	///     Tells every connected seat where its lobby moved to and which credential it is admitted by. It is sent once, as
	///     soon as the leaving host has permitted the transfer, or after a short patience when it never states one, because
	///     a lobby that stalled on a registry node would be a lobby nobody could get out of.
	/// </summary>
	private void SendHandoverCommitsToSeats()
	{
		if (_handoverCommitsSent) return;
		_handoverCommitsSent = true;

		foreach (LobbySeat seat in _seats.Values)
		{
			if (seat.IsComputer || seat.IsDisconnected) continue;
			if (seat.PeerId <= 0) continue;

			NotifyHandoverParty(seat.PeerId, LobbyHandoverMessageKind.Commit, default, string.Empty, DescribeCommitFor(seat.PeerId));
		}
	}

	/// <summary>
	///     Passes the permission the lobby's host obtained from its registry node to the screen taking the lobby over, which
	///     is the only screen that can complete the ownership transfer with it. A statement that arrives outside a committed
	///     handover is ignored rather than kept for later.
	/// </summary>
	/// <param name="stated">The epoch and token the host stated, as "<c>epoch|token</c>".</param>
	private void ForwardHandoverOwnership(string stated)
	{
		if (_handoverStage != LobbyHandoverStage.Committing) return;
		if (string.IsNullOrWhiteSpace(stated)) return;

		int separator = stated.IndexOf('|');
		if (separator <= 0) return;
		if (!int.TryParse(stated.AsSpan(0, separator), out int epoch)) return;

		string token = LobbyLimits.Sanitize(stated[(separator + 1)..], 128);
		if (token.Length == 0)
		{
			// The host stated that its lobby has no listing to move, so there is no permission to pass on and nothing more
			// to wait for. The seats are told where they are going.
			SendHandoverCommitsToSeats();
			return;
		}

		_log.Write($"the lobby's host permitted the handover under epoch {epoch}; the permission is being passed to the candidate");
		NotifyHandoverParty(_handoverCandidatePeerId, LobbyHandoverMessageKind.Ownership, default, string.Empty, new LobbyHandoverMessage
		{
			Kind = LobbyHandoverMessageKind.Ownership,
			OfferRevision = _handoverOfferRevision,
			HandoverToken = token,
			SourceEpoch = epoch,
			LobbyId = ActiveLobbyIdOrEmpty()
		});

		SendHandoverCommitsToSeats();
	}

	private string HandoverCandidateName()
	{
		return TryGetSeat(_handoverCandidatePeerId, out LobbySeat seat) ? seat.Name : "the new host";
	}

	private string CredentialForSlot(int slot)
	{
		LobbyHandoverSeatCredential[] credentials = _handoverReport.Credentials ?? [];
		for (int index = 0; index < credentials.Length; index++)
		{
			if (credentials[index].Slot == slot) return credentials[index].SessionToken;
		}

		return string.Empty;
	}

	/// <summary>
	///     The lobby id the seats are told to prove they arrived at, which is the id the registry lists this lobby under. A
	///     lobby nobody listed holds an empty id, which is what an unlisted lobby states instead of an invented one.
	/// </summary>
	/// <returns>The lobby id, or empty.</returns>
	private string ActiveLobbyIdOrEmpty() => _lobbyId ?? string.Empty;

	/// <summary>
	///     The id the registry lists this lobby under, stated by the screen that published it. An unlisted lobby states
	///     nothing, because there is no listing for a handover to keep.
	/// </summary>
	public string? LobbyId
	{
		get => _lobbyId;
		set => _lobbyId = string.IsNullOrWhiteSpace(value) ? null : LobbyLimits.Sanitize(value, 64);
	}

	private string? _lobbyId;

	/// <summary>
	///     Says that the handover the lobby committed did not complete. The authority kept every seat while the seats were
	///     moving, so the lobby is exactly the one it was: the seats that moved reconnect by the tokens they already hold,
	///     and the candidate's authority is given up on its own side.
	/// </summary>
	/// <param name="peerId">The seat that reported the failure.</param>
	/// <param name="reason">What went wrong, as a diagnostic sentence.</param>
	private void AbortCommittedHandover(int peerId, string reason)
	{
		if (peerId != _handoverCandidatePeerId && peerId != _adminPeerId) return;

		RollBackCommittedHandover(reason);
	}

	private void CloseHandover(string notice, bool notifySeats)
	{
		int offerer = _adminPeerId;
		int candidate = _handoverCandidatePeerId;

		_handoverStage = LobbyHandoverStage.None;
		_handoverOffererPeerId = 0;
		_handoverCandidatePeerId = 0;
		_handoverAwaitingPeerId = 0;
		_handoverStageRemainingSeconds = 0;
		_handoverHostConfirmed = false;
		_handoverCandidateConfirmed = false;
		_handoverReport = default;
		_handoverNotice = notice;
		_handoverCommitsSent = false;

		if (notifySeats)
		{
			NotifyHandoverParty(offerer, LobbyHandoverMessageKind.RolledBack, default, notice);
			if (candidate != offerer) NotifyHandoverParty(candidate, LobbyHandoverMessageKind.RolledBack, default, notice);
			AddSystemChat(notice);
		}

		BroadcastState();
	}

	/// <summary>
	///     Cancels a handover because the lobby underneath it changed. A seat leaving, the countdown starting, the map
	///     being replaced or the host leaving each leaves nothing for the offer to be about, so the offer ends and every
	///     seat may speak for itself again.
	/// </summary>
	/// <param name="notice">The sentence the lobby states about why the handover ended.</param>
	public void CancelHandoverForLobbyChange(string notice)
	{
		if (_handoverStage == LobbyHandoverStage.None) return;
		if (_handoverStage == LobbyHandoverStage.Committing) return;

		CloseHandover(notice, notifySeats: true);
	}

	/// <summary>Advances the handover and the measurement round a suggestion is read from.</summary>
	/// <param name="deltaSeconds">How much time the lobby tick advanced.</param>
	private void TickHandover(double deltaSeconds)
	{
		double elapsed = Math.Max(0.0, deltaSeconds);
		ApplyAdoptedSettings();

		if (_handoverMeasurements.Count > 0)
		{
			_handoverMeasurementsRemainingSeconds -= elapsed;
			if (_handoverMeasurementsRemainingSeconds <= 0) EvaluateSuggestion(completedEarly: false);
		}

		if (_handoverStage == LobbyHandoverStage.None) return;

		_handoverStageRemainingSeconds -= elapsed;

		if (_handoverStage == LobbyHandoverStage.Committing && !_handoverCommitsSent)
		{
			// The host was given a few seconds to permit the transfer before the seats were told to go. A permission that
			// never arrives must not hold the seats in a lobby that has committed.
			if (_handoverStageRemainingSeconds > HostHandoverRules.SettleSeconds - HandoverOwnershipPatienceSeconds) return;

			_log.Write("the lobby's host stated no permission for its handover, so the seats are being told to move without one");
			SendHandoverCommitsToSeats();
			return;
		}

		if (_handoverStageRemainingSeconds > 0) return;

		switch (_handoverStage)
		{
			case LobbyHandoverStage.Offered:
				CloseHandover(LobbyHandoverNotice.Expired, notifySeats: true);
				break;
			case LobbyHandoverStage.Rehearsing:
			case LobbyHandoverStage.Decided:
				CloseHandover(LobbyHandoverNotice.RehearsalTimedOut, notifySeats: true);
				break;
			case LobbyHandoverStage.Committing:
				_log.Write("the handover did not settle within its window, so the lobby stays with the host it had");
				AbortCommittedHandover(_handoverCandidatePeerId, "the transfer window closed before every seat arrived");
				break;
		}
	}

	private void RecordMeasurements(LobbySeat sender, in LobbyHandoverLinkRow row)
	{
		if (sender.IsComputer) return;
		if (_handoverStage != LobbyHandoverStage.None) return;
		if (row.Samples == null || row.Samples.Length == 0) return;

		LobbyHandoverLinkRow stored = row;
		stored.ObserverSlot = sender.Slot;
		_handoverMeasurements[sender.Slot] = stored;

		if (_handoverMeasurementsRemainingSeconds <= 0) _handoverMeasurementsRemainingSeconds = HandoverMeasurementWindowSeconds;

		int humanSeatCount = 0;
		foreach (LobbySeat seat in _seats.Values)
		{
			if (!seat.IsComputer && !seat.IsDisconnected) humanSeatCount++;
		}

		if (_handoverMeasurements.Count >= humanSeatCount) EvaluateSuggestion(completedEarly: true);
	}

	/// <summary>
	///     Reads the measurement matrix every seat reported and, when one seated player is clearly better placed to carry
	///     the lobby, tells that player and the host with the numbers behind it. Nobody else is asked anything, and no
	///     handover starts from it.
	/// </summary>
	/// <param name="completedEarly">Whether every seated player has reported, rather than the window running out.</param>
	private void EvaluateSuggestion(bool completedEarly)
	{
		var rows = new List<LobbyHandoverLinkRow>(_handoverMeasurements.Count);
		var slots = new List<int>(_handoverMeasurements.Count + 1);
		foreach (KeyValuePair<int, LobbyHandoverLinkRow> pair in _handoverMeasurements) rows.Add(pair.Value);

		int hostSlot = 0;
		foreach (LobbySeat seat in _seats.Values)
		{
			if (seat.IsComputer || seat.IsDisconnected) continue;
			slots.Add(seat.Slot);
			if (seat.PeerId == _adminPeerId) hostSlot = seat.Slot;
		}

		_handoverMeasurements.Clear();
		_handoverMeasurementsRemainingSeconds = 0;

		if (!completedEarly || hostSlot == 0) return;
		if (_handoverStage != LobbyHandoverStage.None) return;

		if (!HostHandoverSuggestion.TryAdvise(rows, slots, hostSlot, HostHandoverRules.MinimumSuggestedSamplesPerSeat, out HostHandoverSuggestion suggestion)) return;
		if (!TryGetSeatBySlot(suggestion.CandidateSlot, out LobbySeat candidate)) return;
		if (candidate.IsComputer || candidate.IsDisconnected || candidate.PeerId <= 0) return;
		if (candidate.PeerId == _adminPeerId) return;

		string candidateStability = candidate.HostStability;
		if (HostStabilityRanks.Rank(candidateStability) is not (HostStabilityRanks.Excellent or HostStabilityRanks.Average)) return;
		if (HostStabilityRanks.Rank(candidateStability) > HostStabilityRanks.Rank(HostStability)) return;

		_suggestedCandidatePeerId = candidate.PeerId;
		LobbyHandoverReport advice = DescribeSuggestionReport(rows, slots, hostSlot, suggestion.CandidateSlot);
		_log.Write($"a measurement round suggested peer {candidate.PeerId} ('{candidate.Name}') as a better host: mean {suggestion.MeanImprovementMilliseconds} ms better, worst case {suggestion.WorstCaseImprovementMilliseconds} ms better over {suggestion.SeatsCompared} seat(s)");

		NotifyHandoverParty(_adminPeerId, LobbyHandoverMessageKind.Suggestion, advice, string.Empty);
		NotifyHandoverParty(candidate.PeerId, LobbyHandoverMessageKind.Suggestion, advice, string.Empty);
		AddSystemChat($"A better-placed host was suggested: {candidate.Name}. The host and that player can see the numbers.");
		BroadcastState();
	}

	private LobbyHandoverReport DescribeSuggestionReport(List<LobbyHandoverLinkRow> rows, List<int> slots, int hostSlot, int candidateSlot)
	{
		var seats = new LobbyHandoverSeatMetric[slots.Count];
		for (int index = 0; index < slots.Count; index++)
		{
			int slot = slots[index];
			TryGetSeatBySlot(slot, out LobbySeat seat);
			var metric = new LobbyHandoverSeatMetric
			{
				Slot = slot,
				AccountId = seat.AccountId,
				Name = seat.Name
			};

			if (slot == hostSlot)
			{
				metric.CurrentHostMeasured = true;
				metric.CurrentHostRoundTripMilliseconds = 0;
			}
			else
			{
				metric.CurrentHostMeasured = seat.LatencyMilliseconds >= 0;
				metric.CurrentHostRoundTripMilliseconds = Math.Max(0, seat.LatencyMilliseconds);
				metric.CurrentHostJitterMilliseconds = Math.Max(0, seat.JitterMilliseconds);
				metric.CurrentHostPacketLossPercent = Math.Max(0f, seat.PacketLossPercent);
			}

			if (slot == candidateSlot)
			{
				metric.CandidateMeasured = true;
				metric.CandidateRoundTripMilliseconds = 0;
				metric.CandidateSamples = HostHandoverRules.MinimumSamplesPerSeat;
			}
			else
			{
				foreach (LobbyHandoverLinkRow row in rows)
				{
					if (row.ObserverSlot != slot) continue;
					foreach (LobbyHandoverLinkSample sample in row.Samples)
					{
						if (sample.TargetSlot != candidateSlot) continue;
						metric.CandidateMeasured = sample.Measured;
						metric.CandidateRoundTripMilliseconds = sample.RoundTripMilliseconds;
						metric.CandidateJitterMilliseconds = sample.JitterMilliseconds;
						metric.CandidatePacketLossPercent = sample.PacketLossPercent;
						metric.CandidateSamples = sample.Samples;
					}
				}
			}

			seats[index] = metric;
		}

		TryGetSeatBySlot(candidateSlot, out LobbySeat candidateSeat);
		return new LobbyHandoverReport
		{
			Seats = seats,
			CandidateEndpointVerified = true,
			MapMatched = true,
			CandidateStabilityRank = HostStabilityRanks.Rank(candidateSeat.HostStability),
			CurrentHostStabilityRank = HostStabilityRanks.Rank(HostStability),
			MeasuredUtcTicks = DateTime.UtcNow.Ticks,
			Verdict = (int)HostHandoverRules.Verdict(new LobbyHandoverReport { Seats = seats, CandidateEndpointVerified = true, MapMatched = true, CandidateStabilityRank = HostStabilityRanks.Rank(candidateSeat.HostStability), CurrentHostStabilityRank = HostStabilityRanks.Rank(HostStability) })
		};
	}

	private void AnswerHandoverRefusal(int peerId, string notice)
	{
		_handoverNotice = notice;
		NotifyHandoverParty(peerId, LobbyHandoverMessageKind.RolledBack, default, notice);
		_log.Write($"peer {peerId} was answered about a handover: {notice}");
		BroadcastState();
	}

	private void NotifyHandoverParty(int peerId, LobbyHandoverMessageKind kind, in LobbyHandoverReport report, string notice, LobbyHandoverMessage? stated = null)
	{
		if (peerId <= 0) return;
		if (!_slotByPeer.TryGetValue(peerId, out int slot)) return;
		if (!_seats.TryGetValue(slot, out LobbySeat seat)) return;
		if (seat.IsDisconnected) return;

		LobbyHandoverMessage message = stated ?? new LobbyHandoverMessage
		{
			Kind = kind,
			Report = report,
			OfferRevision = _handoverOfferRevision,
			LobbyId = ActiveLobbyIdOrEmpty()
		};
		if (message.Kind != LobbyHandoverMessageKind.Commit) message.Notice = notice;

		_send(peerId, NetworkFrameKind.LobbyHandover, MemoryPackSerializer.Serialize(message), FrameDelivery.Reliable);
	}

	/// <summary>
	///     Creates the lobby this authority is taking over: the seats it left, each held for the credential the screen sitting
	///     in it was given, the settings it had chosen, and the board it was playing. Every adopted seat starts disconnected
	///     and unready, because the settings its readiness was given for are not necessarily the ones the new host starts
	///     with, and a seat that does not arrive is undone by the handover rather than papered over.
	/// </summary>
	/// <param name="adoption">What the lobby's previous authority stated it held.</param>
	/// <returns>Whether the lobby now holds those seats.</returns>
	public bool Adopt(in LobbyHandoverAdoption adoption)
	{
		if (_matchStarted || adoption == null || adoption.Seats == null) return false;

		LobbyId = adoption.LobbyId;
		_seats.Clear();
		_slotByPeer.Clear();
		_adoptionPendingSeats = 0;

		for (int index = 0; index < adoption.Seats.Length; index++)
		{
			LobbyHandoverAdoptedSeat adopted = adoption.Seats[index];
			if (adopted == null || adopted.Slot <= 0 || adopted.Slot >= _openSlots.Length) continue;

			bool isComputerSeat = string.IsNullOrEmpty(adopted.SessionToken) && adopted.Difficulty > 0;
			if (string.IsNullOrEmpty(adopted.SessionToken) && !isComputerSeat) continue;

			var seat = new LobbySeat
			{
				IsComputer = isComputerSeat,
				PeerId = isComputerSeat ? -100 - _nextComputerNumber++ : 0,
				ProbeToken = isComputerSeat ? string.Empty : RealmProtocol.CreateToken(),
				Slot = adopted.Slot,
				AccountId = adopted.AccountId,
				Name = LobbyLimits.SanitizeDisplayName(adopted.Name),
				Faction = LobbyLimits.Sanitize(adopted.Faction, LobbyLimits.MaximumFactionLength),
				TeamName = LobbyLimits.Sanitize(adopted.TeamName, LobbyLimits.MaximumTeamNameLength),
				Team = adopted.Team,
				ColorRgba = adopted.ColorRgba,
				Difficulty = adopted.Difficulty,
				SessionToken = LobbyLimits.Sanitize(adopted.SessionToken, MaximumSessionTokenLength),
				IsDisconnected = true,
				LatencyMilliseconds = -1,
				JitterMilliseconds = -1,
				PacketLossPercent = -1f
			};

			_seats[seat.Slot] = seat;
			if (!isComputerSeat) _adoptionPendingSeats++;
		}

		if (adoption.OpenSlots != null && adoption.OpenSlots.Length == _openSlots.Length) Array.Copy(adoption.OpenSlots, _openSlots, _openSlots.Length);
		_openSlotCount = CountOpenSlots();

		_adoptionPendingSettings.Clear();
		if (adoption.Settings != null) _adoptionPendingSettings.AddRange(adoption.Settings);

		if (adoption.Factions != null)
		{
			for (int index = 0; index < adoption.Factions.Length; index++) Roster?.AddFaction(adoption.Factions[index]);
		}

		Roster?.SetTeamCount(adoption.TeamCount);
		Roster?.TrySetTeamsTogether(adoption.TeamsTogether);
		if (Enum.IsDefined(typeof(MatchShroudMode), adoption.ShroudMode)) _hostShroudMode = (MatchShroudMode)adoption.ShroudMode;
		_hostChoseSpectatorDelay = true;
		SpectatorDelay = adoption.SpectatorDelay;
		_revision++;
		_log.Write($"this lobby was taken over from '{adoption.LobbyId}': {_seats.Count} seat(s) are held for the credentials they were given");

		return _seats.Count > 0;
	}

	private void ApplyAdoptedSettings()
	{
		if (_adoptionPendingSettings.Count == 0) return;

		for (int index = _adoptionPendingSettings.Count - 1; index >= 0; index--)
		{
			LobbyHandoverAdoptedSetting setting = _adoptionPendingSettings[index];
			if (!_matchSettings.ContainsKey(setting.SettingId)) continue;

			if (TrySetLobbySetting(setting.SettingId, setting.Value)) _adoptionPendingSettings.RemoveAt(index);
		}
	}

	private readonly List<LobbyHandoverAdoptedSetting> _adoptionPendingSettings = new();

	private int _adoptionPendingSeats;

	/// <summary>Whether this authority took a lobby over and is still holding seats nobody has claimed yet.</summary>
	public bool IsAdopting => _adoptionPendingSeats > 0;

	private void NoteAdoptedSeatArrived(int slot)
	{
		if (!_seats.TryGetValue(slot, out LobbySeat seat)) return;
		if (string.IsNullOrEmpty(seat.SessionToken)) return;

		_adoptionPendingSeats = Math.Max(0, _adoptionPendingSeats - 1);
		seat.ProbeToken = RealmProtocol.CreateToken();
		_seats[slot] = seat;
	}

	/// <summary>
	///     Returns a committed handover to the host it had. The seats this authority kept are exactly the seats it had, so a
	///     seat that comes back by the credential it already holds is the same seat, on the same slot and the same team.
	/// </summary>
	/// <param name="reason">What went wrong, as a diagnostic sentence.</param>
	public void RollBackCommittedHandover(string reason)
	{
		if (_handoverStage != LobbyHandoverStage.Committing) return;

		_log.Write($"the committed handover did not complete: {LobbyLimits.Sanitize(reason, 200)}");
		foreach (LobbySeat seat in _seats.Values)
		{
			if (seat.IsComputer || seat.PeerId == 0) continue;
			NotifyHandoverParty(seat.PeerId, LobbyHandoverMessageKind.RolledBack, default, LobbyHandoverNotice.RolledBack);
		}

		AddSystemChat(LobbyHandoverNotice.RolledBack);
		CloseHandover(LobbyHandoverNotice.RolledBack, notifySeats: false);
	}

	private LobbyHandoverState DescribeHandoverState()
	{
		if (_handoverStage == LobbyHandoverStage.None && _suggestedCandidatePeerId == 0 && _handoverNotice.Length == 0) return default;

		int remaining = _handoverStage == LobbyHandoverStage.None ? 0 : Math.Max(0, (int)Math.Ceiling(_handoverStageRemainingSeconds));
		return new LobbyHandoverState
		{
			Stage = (int)_handoverStage,
			OffererPeerId = _handoverOffererPeerId,
			CandidatePeerId = _handoverCandidatePeerId,
			SuggestedCandidatePeerId = _suggestedCandidatePeerId,
			SecondsRemaining = remaining,
			Notice = _handoverNotice,
			OfferRevision = _handoverOfferRevision
		};
	}
}
