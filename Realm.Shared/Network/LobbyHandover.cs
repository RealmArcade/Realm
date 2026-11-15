using System;
using System.Collections.Generic;
using MemoryPack;

namespace Realm.Shared.Network;

/// <summary>
///     How far a lobby's host handover has got. The stage is published in the lobby state, so every seat knows that a
///     handover is being offered or rehearsed while only the two parties it names see the numbers behind it.
/// </summary>
public enum LobbyHandoverStage : byte
{
	/// <summary>No handover is being discussed, which is a lobby's state for its whole life unless somebody offers one.</summary>
	None = 0,

	/// <summary>One side offered and the other has not answered. A refusal or an expiry returns the stage to <see cref="None" />.</summary>
	Offered = 1,

	/// <summary>The offer was accepted and the candidate is measuring itself against every seated player.</summary>
	Rehearsing = 2,

	/// <summary>The candidate stated a measurement, the lobby judged it, and both parties are asked to confirm.</summary>
	Decided = 3,

	/// <summary>Both parties confirmed a passing rehearsal, so the lobby is moving its authority to the candidate.</summary>
	Committing = 4
}

/// <summary>
///     What a rehearsal concluded. The verdict is computed from the measurements by
///     <see cref="HostHandoverRules.Verdict" /> on both sides of the handover, so the number a player was shown and the
///     number the lobby acted on are the same number.
/// </summary>
public enum LobbyHandoverVerdict : byte
{
	/// <summary>Nothing was measured, so nothing was decided. It is never shown as a pass.</summary>
	NotMeasured = 0,

	/// <summary>Every seated player reached the candidate, no player was made materially worse off, and the candidate can hold a tick.</summary>
	Passed = 1,

	/// <summary>At least one seated player did not answer the candidate's probes.</summary>
	RefusedSeatUnreachable = 2,

	/// <summary>A seated player reached the candidate, but at a round trip or loss nobody could play the match on.</summary>
	RefusedSeatUnplayable = 3,

	/// <summary>No registry node could reach the candidate's own game port, so the lobby could not be published on it.</summary>
	RefusedEndpointUnreachable = 4,

	/// <summary>The candidate does not hold the lobby's map at the exact version the lobby is playing.</summary>
	RefusedMapMismatch = 5,

	/// <summary>The candidate's machine is measured as unable to hold the tick.</summary>
	RefusedMachineCannotHoldTick = 6,

	/// <summary>The candidate's machine stability was never measured, and unknown is not a reason to move a lobby.</summary>
	RefusedStabilityUnknown = 7,

	/// <summary>Moving the lobby would leave a seated player materially worse off than the current host does.</summary>
	RefusedPlayerMateriallyWorseOff = 8
}

/// <summary>
///     What one lobby publishes about the handover it is running. It travels in the lobby state, so it never carries a
///     measurement, an endpoint or a credential: those belong to the two parties and reach them alone.
/// </summary>
[MemoryPackable]
public partial struct LobbyHandoverState
{
	/// <summary>How far the handover has got.</summary>
	public int Stage;

	/// <summary>The seat that started the conversation, or zero while nobody has.</summary>
	public int OffererPeerId;

	/// <summary>The seat that would become the host, or zero while nobody has.</summary>
	public int CandidatePeerId;

	/// <summary>How long the current stage has left before it expires on its own.</summary>
	public int SecondsRemaining;

	/// <summary>
	///     The seat a measurement round suggested could carry this lobby better, or zero while nothing was suggested. It is
	///     advice every seat may read; the numbers behind it reach the candidate and the host alone.
	/// </summary>
	public int SuggestedCandidatePeerId;

	/// <summary>
	///     The sentence the lobby states about the handover, translated from <see cref="LobbyHandoverNotice" /> by the screen
	///     that reads it. Empty when there is nothing to say.
	/// </summary>
	public string Notice;

	/// <summary>
	///     The identifier the offer was opened under. A command that names a different one belongs to an offer that already
	///     ended and is ignored rather than answered.
	/// </summary>
	public long OfferRevision;
}

/// <summary>The sentences a lobby's handover can state to the seats watching it, as keys the screens translate.</summary>
public static class LobbyHandoverNotice
{
	public const string OfferedToHost = "A player offered to take over as host. Only the host can answer it.";
	public const string OfferedByHost = "The host offered to hand the lobby over. Only that player can answer it.";
	public const string HostRefused = "The host refused to hand the lobby over.";
	public const string CandidateRefused = "The player offered the lobby refused it.";
	public const string Withdrawn = "The host handover was withdrawn.";
	public const string Expired = "The host handover offer expired before it was answered.";
	public const string RehearsalStarted = "A host handover is being measured. Both parties can cancel it while it runs.";
	public const string RehearsalCancelled = "The host handover was cancelled; this lobby is unchanged.";
	public const string RehearsalTimedOut = "The host handover rehearsal ran out of time, so nothing changed.";
	public const string VerdictRefused = "The rehearsal refused the handover.";
	public const string AwaitingConfirmation = "The rehearsal passed. Both parties are confirming.";
	public const string Confirmed = "Both parties confirmed. The lobby is moving to its new host.";
	public const string LobbyChanged = "The lobby changed, so the host handover offer was cancelled.";
	public const string CountdownRunning = "A host handover cannot start while the countdown is running.";
	public const string OneAtATime = "This lobby is already discussing a host handover.";
	public const string UnknownSeat = "That player is not seated in this lobby.";
	public const string ComputerCannotHost = "A computer player cannot host a lobby.";
	public const string NotTheCandidate = "Only the two parties of a host handover can answer it.";
	public const string RolledBack = "The handover did not complete, so this lobby stays with its host.";
}

/// <summary>
///     What one seated player measured of the current host and of the candidate. Every field is stated as measured or not
///     measured, so a figure that could not be taken is never read as a good one.
/// </summary>
[MemoryPackable]
public partial struct LobbyHandoverSeatMetric
{
	/// <summary>The player slot the measurement is about, which survives a reconnect where a peer id does not.</summary>
	public int Slot;

	/// <summary>The account the seat belongs to, which is what the seat is restored by.</summary>
	public long AccountId;

	/// <summary>The name the seat plays under, so a screen can name the player the number is about.</summary>
	public string Name;

	/// <summary>Whether the row measured the lobby's current host at all.</summary>
	public bool CurrentHostMeasured;

	/// <summary>The round trip between this seat and the lobby's current host, or zero when it was not measured.</summary>
	public int CurrentHostRoundTripMilliseconds;

	/// <summary>The jitter between this seat and the lobby's current host, or zero when it was not measured.</summary>
	public int CurrentHostJitterMilliseconds;

	/// <summary>The loss between this seat and the lobby's current host, in percent, or zero when it was not measured.</summary>
	public float CurrentHostPacketLossPercent;

	/// <summary>Whether the row measured the candidate at all.</summary>
	public bool CandidateMeasured;

	/// <summary>The round trip between this seat and the candidate, or zero when it was not measured.</summary>
	public int CandidateRoundTripMilliseconds;

	/// <summary>The jitter between this seat and the candidate, or zero when it was not measured.</summary>
	public int CandidateJitterMilliseconds;

	/// <summary>The loss between this seat and the candidate, in percent, or zero when it was not measured.</summary>
	public float CandidatePacketLossPercent;

	/// <summary>How many answers the candidate's probes collected from this seat.</summary>
	public int CandidateSamples;
}

/// <summary>
///     The fresh credential the candidate mints for one seat. The old host delivers each seat its own over the lobby it
///     already trusts, and the new authority admits that seat by that credential, so no seat's secret is handed to a third
///     party and no seat is re-established by a peer id that a reconnect would have changed.
/// </summary>
[MemoryPackable]
public partial struct LobbyHandoverSeatCredential
{
	/// <summary>The player slot the credential seats.</summary>
	public int Slot;

	/// <summary>The account the credential belongs to, which the new authority checks against the seat.</summary>
	public long AccountId;

	/// <summary>The credential itself, which only this seat is ever told.</summary>
	public string SessionToken;
}

/// <summary>
///     Everything a rehearsal measured, stated by the candidate and judged by the same rules on both sides of the
///     handover. It reaches the two parties alone, because it carries the endpoint the new authority is listening on and
///     the credentials the new authority will admit the seats by.
/// </summary>
[MemoryPackable]
public partial struct LobbyHandoverReport
{
	/// <summary>One row per seated player, in slot order.</summary>
	public LobbyHandoverSeatMetric[] Seats;

	/// <summary>One credential per seated human player, in slot order.</summary>
	public LobbyHandoverSeatCredential[] Credentials;

	/// <summary>Whether a registry node reached the candidate's own game port, which is what publishing requires.</summary>
	public bool CandidateEndpointVerified;

	/// <summary>Whether the candidate holds the lobby's map at the exact version the lobby is playing.</summary>
	public bool MapMatched;

	/// <summary>The candidate's own machine stability rank, or <see cref="HostStabilityRanks.Unknown" /> when it stated none.</summary>
	public int CandidateStabilityRank;

	/// <summary>The current host's machine stability rank, or <see cref="HostStabilityRanks.Unknown" /> when it stated none.</summary>
	public int CurrentHostStabilityRank;

	/// <summary>The address the candidate's authority is listening on for the seats that move to it.</summary>
	public string CandidateEndpointAddress;

	/// <summary>The port the candidate's authority is listening on for the seats that move to it.</summary>
	public int CandidateEndpointPort;

	/// <summary>
	///     The measurement that could not be taken, as a short diagnostic sentence, or empty when every measurement was
	///     taken. It is for the log and the diagnostics page, never for the sentence a player reads.
	/// </summary>
	public string Failure;

	/// <summary>When the measurements were taken, as UTC ticks, so a stale report is visibly stale.</summary>
	public long MeasuredUtcTicks;

	/// <summary>The verdict the candidate's own reading of these numbers reached.</summary>
	public int Verdict;
}

/// <summary>
///     What the lobby tells the two parties of a handover, and what the seats are told when the authority is about to
///     move. Every message here is private to the seat it is addressed to.
/// </summary>
[MemoryPackable]
public partial struct LobbyHandoverMessage
{
	/// <summary>Which conversation this message belongs to.</summary>
	public LobbyHandoverMessageKind Kind;

	/// <summary>The measurements, for <see cref="LobbyHandoverMessageKind.Evidence" /> and for a suggestion.</summary>
	public LobbyHandoverReport Report;

	/// <summary>The offer the message belongs to, which a screen compares against the lobby state before it acts.</summary>
	public long OfferRevision;

	/// <summary>The address of the lobby's new authority, for <see cref="LobbyHandoverMessageKind.Commit" />.</summary>
	public string Address;

	/// <summary>The port of the lobby's new authority, for <see cref="LobbyHandoverMessageKind.Commit" />.</summary>
	public int Port;

	/// <summary>
	///     The credential the receiving seat is admitted by on the new authority, for
	///     <see cref="LobbyHandoverMessageKind.Commit" />. It is stated to that seat alone.
	/// </summary>
	public string SessionToken;

	/// <summary>The lobby id both authorities serve, so a screen can prove the lobby it reaches is the one it left.</summary>
	public string LobbyId;

	/// <summary>The diagnostic sentence a rollback or a refusal states, empty when there is nothing to say.</summary>
	public string Notice;
}

/// <summary>Which private handover message a seat is being sent.</summary>
public enum LobbyHandoverMessageKind : byte
{
	/// <summary>The rehearsal's measurements and the verdict they were judged by.</summary>
	Evidence = 1,

	/// <summary>A measurement round's numbers, offered to the host and the player they name as advice.</summary>
	Suggestion = 2,

	/// <summary>The authority is moving: where to go and which credential to present.</summary>
	Commit = 3,

	/// <summary>The handover ended, and the seats that moved or were about to move belong to the old host again.</summary>
	RolledBack = 4
}

/// <summary>The machine stability ratings the lobby already publishes, as the rank a handover compares.</summary>
public static class HostStabilityRanks
{
	/// <summary>The rating a host whose game ticked without trouble is published with.</summary>
	public const int Excellent = 0;

	/// <summary>The rating a host whose game ticked but not cleanly is published with.</summary>
	public const int Average = 1;

	/// <summary>The rating a host whose machine was measured as unable to hold the tick is published with.</summary>
	public const int Poor = 2;

	/// <summary>Nothing was measured, so the rating is not evidence of anything and never reads as a good machine.</summary>
	public const int Unknown = -1;

	/// <summary>Reads a published stability rating as its rank.</summary>
	/// <param name="rating">The rating a seat or a lobby published.</param>
	/// <returns>The rank, or <see cref="Unknown" /> for a rating this build does not recognise.</returns>
	public static int Rank(string? rating) => rating switch
	{
		"Excellent" => Excellent,
		"Average" => Average,
		"Poor" => Poor,
		_ => Unknown
	};
}

/// <summary>
///     The one definition of what a host handover requires, read by the candidate's own rehearsal, by the authority that
///     judges the report, and by the advice a lobby raises when somebody is clearly better placed to host. Keeping one
///     definition is what stops the suggestion and the verdict from disagreeing about the same lobby.
/// </summary>
public static class HostHandoverRules
{
	/// <summary>How long an offer waits for an answer before it expires.</summary>
	public const int OfferedSeconds = 60;

	/// <summary>How long a rehearsal may run before the lobby gives up on it.</summary>
	public const int RehearsalSeconds = 150;

	/// <summary>How long the seats have to be admitted by the new authority before the handover rolls back.</summary>
	public const int SettleSeconds = 30;

	/// <summary>The fewest probe answers a seat's row must carry to count as measured.</summary>
	public const int MinimumSamplesPerSeat = 8;

	/// <summary>The most round trip a seat may be left with and still be considered able to play the match.</summary>
	public const int MaximumPlayableRoundTripMilliseconds = 300;

	/// <summary>The most loss a seat may be left with and still be considered able to play the match.</summary>
	public const float MaximumPlayablePacketLossPercent = 5.0f;

	/// <summary>
	///     How much worse a seated player's round trip may become before the handover is refused. A change that helps four
	///     players and makes one unplayable is not a handover, which is why the limit is applied to every seat rather than
	///     to the average.
	/// </summary>
	public const int MaximumRoundTripRegressionMilliseconds = 40;

	/// <summary>How much worse a seated player's loss may become before the handover is refused.</summary>
	public const float MaximumPacketLossRegressionPercent = 1.0f;

	/// <summary>How much better the group's mean round trip must become for a suggestion to be worth raising.</summary>
	public const int MinimumSuggestedMeanImprovementMilliseconds = 20;

	/// <summary>
	///     How much better the group's worst round trip must become for a suggestion to be worth raising. Advice is only
	///     advice when it improves the player who is worst off, not merely the players who are already fine.
	/// </summary>
	public const int MinimumSuggestedWorstCaseImprovementMilliseconds = 10;

	/// <summary>How many probe answers one seat's advice row must carry before it is read at all.</summary>
	public const int MinimumSuggestedSamplesPerSeat = 6;

	/// <summary>
	///     Judges a rehearsal. Every seated player has to have reached the candidate, been measured, and been left able to
	///     play, and the candidate's machine has to be at least as steady as the host it would replace.
	/// </summary>
	/// <param name="report">The measurements the candidate stated.</param>
	/// <returns>What the numbers allow.</returns>
	public static LobbyHandoverVerdict Verdict(in LobbyHandoverReport report)
	{
		LobbyHandoverSeatMetric[] seats = report.Seats ?? [];
		if (seats.Length == 0) return LobbyHandoverVerdict.NotMeasured;

		if (!report.MapMatched) return LobbyHandoverVerdict.RefusedMapMismatch;
		if (!report.CandidateEndpointVerified) return LobbyHandoverVerdict.RefusedEndpointUnreachable;
		if (report.CandidateStabilityRank == HostStabilityRanks.Unknown) return LobbyHandoverVerdict.RefusedStabilityUnknown;
		if (report.CandidateStabilityRank >= HostStabilityRanks.Poor) return LobbyHandoverVerdict.RefusedMachineCannotHoldTick;
		if (report.CurrentHostStabilityRank != HostStabilityRanks.Unknown && report.CandidateStabilityRank > report.CurrentHostStabilityRank) return LobbyHandoverVerdict.RefusedMachineCannotHoldTick;

		LobbyHandoverVerdict measured = LobbyHandoverVerdict.Passed;
		for (int index = 0; index < seats.Length; index++)
		{
			LobbyHandoverSeatMetric seat = seats[index];
			if (!seat.CandidateMeasured) return LobbyHandoverVerdict.RefusedSeatUnreachable;
			if (seat.CandidateRoundTripMilliseconds > MaximumPlayableRoundTripMilliseconds) return LobbyHandoverVerdict.RefusedSeatUnplayable;
			if (seat.CandidatePacketLossPercent > MaximumPlayablePacketLossPercent) return LobbyHandoverVerdict.RefusedSeatUnplayable;

			if (!seat.CurrentHostMeasured) continue;
			if (seat.CandidateRoundTripMilliseconds - seat.CurrentHostRoundTripMilliseconds > MaximumRoundTripRegressionMilliseconds) return LobbyHandoverVerdict.RefusedPlayerMateriallyWorseOff;
			if (seat.CandidatePacketLossPercent - seat.CurrentHostPacketLossPercent > MaximumPacketLossRegressionPercent) return LobbyHandoverVerdict.RefusedPlayerMateriallyWorseOff;

			measured = LobbyHandoverVerdict.Passed;
		}

		return measured;
	}

	/// <summary>
	///     Summarises one side of a report over the seated players it measured: the best and the worst trip, and the mean
	///     that hides neither.
	/// </summary>
	/// <param name="report">The measurements.</param>
	/// <param name="candidateSide">Whether to summarise the candidate rather than the current host.</param>
	/// <param name="minimum">The best round trip among the players measured.</param>
	/// <param name="maximum">The worst round trip among the players measured.</param>
	/// <param name="mean">The mean round trip among the players measured.</param>
	/// <param name="measuredCount">How many players were measured.</param>
	/// <returns>Whether any player was measured at all.</returns>
	public static bool TrySummarize(in LobbyHandoverReport report, bool candidateSide, out int minimum, out int maximum, out int mean, out int measuredCount)
	{
		minimum = 0;
		maximum = 0;
		mean = 0;
		measuredCount = 0;

		LobbyHandoverSeatMetric[] seats = report.Seats ?? [];
		long total = 0;
		for (int index = 0; index < seats.Length; index++)
		{
			LobbyHandoverSeatMetric seat = seats[index];
			bool measured = candidateSide ? seat.CandidateMeasured : seat.CurrentHostMeasured;
			if (!measured) continue;

			int roundTrip = candidateSide ? seat.CandidateRoundTripMilliseconds : seat.CurrentHostRoundTripMilliseconds;
			if (measuredCount == 0)
			{
				minimum = roundTrip;
				maximum = roundTrip;
			}
			else
			{
				if (roundTrip < minimum) minimum = roundTrip;
				if (roundTrip > maximum) maximum = roundTrip;
			}

			total += roundTrip;
			measuredCount++;
		}

		if (measuredCount == 0) return false;
		mean = (int)Math.Round((double)total / measuredCount);
		return true;
	}
}

/// <summary>
///     The advice a lobby raises when one seated player is clearly better placed to carry it than the player hosting it.
///     It is advice: nothing moves until both named parties start and confirm a handover, and a suggestion that would
///     leave one seated player materially worse off is never raised at all.
/// </summary>
public readonly record struct HostHandoverSuggestion(
	int CandidateSlot,
	int CandidatePeerId,
	int MeanImprovementMilliseconds,
	int WorstCaseImprovementMilliseconds,
	int CurrentWorstRoundTripMilliseconds,
	int CandidateWorstRoundTripMilliseconds,
	int SeatsCompared)
{
	/// <summary>
	///     Reads a matrix of measured links and answers whether one seated player is clearly better placed than the host.
	///     Each seat's row states what that seat measured toward every other seat, so the candidate's column is what each
	///     player would reach if the lobby moved to it, and the current host's column is what each player reaches now.
	/// </summary>
	/// <param name="rows">One row per seated human player, keyed by the slot of the player that measured.</param>
	/// <param name="humanSlots">Every slot a human player holds, including the host's.</param>
	/// <param name="hostSlot">The slot of the player hosting the lobby.</param>
	/// <param name="participantSamples">How many answers a row must carry before it is read.</param>
	/// <returns>Whether a suggestion is warranted, and the numbers behind it.</returns>
	public static bool TryAdvise(IReadOnlyList<LobbyHandoverLinkRow> rows, IReadOnlyList<int> humanSlots, int hostSlot, int participantSamples, out HostHandoverSuggestion suggestion)
	{
		suggestion = default;
		if (rows == null || humanSlots == null) return false;
		if (humanSlots.Count < 3) return false;

		if (!TryReadColumn(rows, humanSlots, hostSlot, participantSamples, out int hostWorst, out _, out int hostMean, out int hostCount)) return false;
		if (hostCount < humanSlots.Count - 1) return false;

		long bestMean = long.MaxValue;
		int bestWorst = int.MaxValue;
		int bestCandidateSlot = 0;

		for (int index = 0; index < humanSlots.Count; index++)
		{
			int candidateSlot = humanSlots[index];
			if (candidateSlot == hostSlot) continue;

			if (!TryReadColumn(rows, humanSlots, candidateSlot, participantSamples, out int candidateWorst, out _, out int candidateMean, out int candidateCount)) continue;
			if (candidateCount < humanSlots.Count - 1) continue;

			// Nobody may be made materially worse off, which is measured per seat rather than averaged away.
			if (!Compare(rows, humanSlots, hostSlot, candidateSlot, participantSamples)) continue;

			if (candidateWorst > hostWorst - HostHandoverRules.MinimumSuggestedWorstCaseImprovementMilliseconds) continue;
			if (candidateMean > hostMean - HostHandoverRules.MinimumSuggestedMeanImprovementMilliseconds) continue;

			if (candidateMean >= bestMean) continue;

			bestMean = candidateMean;
			bestWorst = candidateWorst;
			bestCandidateSlot = candidateSlot;
		}

		if (bestCandidateSlot == 0) return false;

		suggestion = new HostHandoverSuggestion(bestCandidateSlot, 0, hostMean - (int)bestMean, hostWorst - bestWorst, hostWorst, bestWorst, humanSlots.Count - 1);
		return true;
	}

	private static bool Compare(IReadOnlyList<LobbyHandoverLinkRow> rows, IReadOnlyList<int> humanSlots, int hostSlot, int candidateSlot, int participantSamples)
	{
		for (int index = 0; index < humanSlots.Count; index++)
		{
			int observerSlot = humanSlots[index];
			if (observerSlot == hostSlot || observerSlot == candidateSlot) continue;

			if (!TryRead(rows, observerSlot, hostSlot, participantSamples, out int current)) return false;
			if (!TryRead(rows, observerSlot, candidateSlot, participantSamples, out int candidate)) return false;
			if (candidate - current > HostHandoverRules.MaximumRoundTripRegressionMilliseconds) return false;
		}

		return true;
	}

	private static bool TryReadColumn(IReadOnlyList<LobbyHandoverLinkRow> rows, IReadOnlyList<int> humanSlots, int targetSlot, int participantSamples, out int worst, out int best, out int mean, out int count)
	{
		worst = 0;
		best = 0;
		mean = 0;
		count = 0;
		long total = 0;

		for (int index = 0; index < humanSlots.Count; index++)
		{
			int observerSlot = humanSlots[index];
			if (observerSlot == targetSlot) continue;
			if (!TryRead(rows, observerSlot, targetSlot, participantSamples, out int roundTrip)) continue;

			if (count == 0)
			{
				best = roundTrip;
				worst = roundTrip;
			}
			else
			{
				if (roundTrip < best) best = roundTrip;
				if (roundTrip > worst) worst = roundTrip;
			}

			total += roundTrip;
			count++;
		}

		if (count == 0) return false;
		mean = (int)Math.Round((double)total / count);
		return true;
	}

	private static bool TryRead(IReadOnlyList<LobbyHandoverLinkRow> rows, int observerSlot, int targetSlot, int participantSamples, out int roundTrip)
	{
		roundTrip = 0;
		for (int rowIndex = 0; rowIndex < rows.Count; rowIndex++)
		{
			LobbyHandoverLinkRow row = rows[rowIndex];
			if (row.ObserverSlot != observerSlot) continue;
			LobbyHandoverLinkSample[] samples = row.Samples ?? [];
			for (int sampleIndex = 0; sampleIndex < samples.Length; sampleIndex++)
			{
				if (samples[sampleIndex].TargetSlot != targetSlot) continue;
				if (!samples[sampleIndex].Measured) return false;
				if (samples[sampleIndex].Samples < participantSamples) return false;
				roundTrip = samples[sampleIndex].RoundTripMilliseconds;
				return true;
			}

			return false;
		}

		return false;
	}
}

/// <summary>
///     What one seated player measured toward every other seated player, which is the matrix a suggestion is read from.
/// </summary>
[MemoryPackable]
public partial struct LobbyHandoverLinkRow
{
	/// <summary>The slot of the player that did the measuring.</summary>
	public int ObserverSlot;

	/// <summary>What that player measured toward each other seat.</summary>
	public LobbyHandoverLinkSample[] Samples;
}

/// <summary>What one player measured toward one other player, stated as measured or not measured.</summary>
[MemoryPackable]
public partial struct LobbyHandoverLinkSample
{
	/// <summary>The slot of the player that was probed.</summary>
	public int TargetSlot;

	/// <summary>Whether the probe collected answers at all.</summary>
	public bool Measured;

	/// <summary>The round trip the probe measured.</summary>
	public int RoundTripMilliseconds;

	/// <summary>The jitter the probe measured.</summary>
	public int JitterMilliseconds;

	/// <summary>The loss the probe measured, in percent.</summary>
	public float PacketLossPercent;

	/// <summary>How many answers the probe collected.</summary>
	public int Samples;
}
