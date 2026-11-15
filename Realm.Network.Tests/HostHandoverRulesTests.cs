using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using Realm.Shared.Network;

namespace Realm.Network.Tests;

/// <summary>
///     Holds the rules that decide whether a lobby may move to a new host, because every one of them is a promise to a
///     player: unknown is never read as good, a seat that was not measured is never a pass, nobody is made materially
///     worse off, and the advice a lobby raises about a better-placed host answers to the same tolerance the rehearsal
///     does. Each case is stated against the named limit rather than a number, so tuning a limit does not change what the
///     rule is.
/// </summary>
[TestFixture]
public class HostHandoverRulesTests
{
	[Test]
	public void ARehearsalThatMeasuredNothingIsNeverAPass()
	{
		Assert.That(HostHandoverRules.Verdict(new LobbyHandoverReport()), Is.EqualTo(LobbyHandoverVerdict.NotMeasured));
	}

	[Test]
	public void ASeatThatNeverAnsweredTheCandidateRefusesTheHandover()
	{
		LobbyHandoverReport report = HealthyReport(4);
		report.Seats[2].CandidateMeasured = false;

		Assert.That(HostHandoverRules.Verdict(report), Is.EqualTo(LobbyHandoverVerdict.RefusedSeatUnreachable));
	}

	[Test]
	public void AMachineNobodyMeasuredRefusesTheHandover()
	{
		LobbyHandoverReport report = HealthyReport(3);
		report.CandidateStabilityRank = HostStabilityRanks.Unknown;

		Assert.That(HostHandoverRules.Verdict(report), Is.EqualTo(LobbyHandoverVerdict.RefusedStabilityUnknown));
	}

	[Test]
	public void AMachineThatCannotHoldTheTickRefusesTheHandover()
	{
		LobbyHandoverReport report = HealthyReport(3);
		report.CandidateStabilityRank = HostStabilityRanks.Poor;

		Assert.That(HostHandoverRules.Verdict(report), Is.EqualTo(LobbyHandoverVerdict.RefusedMachineCannotHoldTick));
	}

	[Test]
	public void ALobbyThatCouldNotBePublishedOnTheCandidateRefusesTheHandover()
	{
		LobbyHandoverReport report = HealthyReport(3);
		report.CandidateEndpointVerified = false;

		Assert.That(HostHandoverRules.Verdict(report), Is.EqualTo(LobbyHandoverVerdict.RefusedEndpointUnreachable));
	}

	[Test]
	public void ACandidateWithoutTheLobbysMapRefusesTheHandover()
	{
		LobbyHandoverReport report = HealthyReport(3);
		report.MapMatched = false;

		Assert.That(HostHandoverRules.Verdict(report), Is.EqualTo(LobbyHandoverVerdict.RefusedMapMismatch));
	}

	[Test]
	public void ASeatLeftBeyondWhatAnybodyCouldPlayOnRefusesTheHandover()
	{
		LobbyHandoverReport report = HealthyReport(3);
		report.Seats[1].CandidateRoundTripMilliseconds = HostHandoverRules.MaximumPlayableRoundTripMilliseconds + 1;
		report.Seats[1].CurrentHostRoundTripMilliseconds = report.Seats[1].CandidateRoundTripMilliseconds;

		Assert.That(HostHandoverRules.Verdict(report), Is.EqualTo(LobbyHandoverVerdict.RefusedSeatUnplayable));
	}

	[Test]
	public void ASeatMadeMateriallyWorseOffRefusesTheHandover()
	{
		LobbyHandoverReport report = HealthyReport(3);
		report.Seats[1].CurrentHostRoundTripMilliseconds = 30;
		report.Seats[1].CandidateRoundTripMilliseconds = 30 + HostHandoverRules.MaximumRoundTripRegressionMilliseconds + 1;

		Assert.That(HostHandoverRules.Verdict(report), Is.EqualTo(LobbyHandoverVerdict.RefusedPlayerMateriallyWorseOff));
	}

	[Test]
	public void TheToleranceIsInclusiveSoTuningItDoesNotMoveTheBoundary()
	{
		LobbyHandoverReport report = HealthyReport(3);
		report.Seats[1].CurrentHostRoundTripMilliseconds = 30;
		report.Seats[1].CandidateRoundTripMilliseconds = 30 + HostHandoverRules.MaximumRoundTripRegressionMilliseconds;

		Assert.That(HostHandoverRules.Verdict(report), Is.EqualTo(LobbyHandoverVerdict.Passed));
	}

	[Test]
	public void AReportThatMeasuredTheLinksItComparedPasses()
	{
		Assert.That(HostHandoverRules.Verdict(HealthyReport(4)), Is.EqualTo(LobbyHandoverVerdict.Passed));
	}

	[Test]
	public void TheSummaryReportsTheWorstAndTheBestRatherThanOnlyTheMean()
	{
		LobbyHandoverReport report = HealthyReport(4);
		report.Seats[0].CandidateRoundTripMilliseconds = 10;
		report.Seats[1].CandidateRoundTripMilliseconds = 90;
		report.Seats[2].CandidateMeasured = false;
		report.Seats[3].CandidateRoundTripMilliseconds = 50;

		Assert.That(HostHandoverRules.TrySummarize(report, candidateSide: true, out int minimum, out int maximum, out int mean, out int measured), Is.True);
		Assert.That(minimum, Is.EqualTo(10));
		Assert.That(maximum, Is.EqualTo(90));
		Assert.That(mean, Is.EqualTo(50));
		Assert.That(measured, Is.EqualTo(3), "a row that was not measured is not averaged into the players who were");
	}

	[Test]
	public void AMeasurementThatCouldNotBeTakenIsStatedAsNotMeasured()
	{
		var report = new LobbyHandoverReport
		{
			Seats = [new LobbyHandoverSeatMetric { Slot = 1, Name = "a" }, new LobbyHandoverSeatMetric { Slot = 2, Name = "b" }]
		};

		Assert.That(HostHandoverRules.TrySummarize(report, candidateSide: true, out _, out _, out _, out _), Is.False);
		Assert.That(HostHandoverRules.TrySummarize(report, candidateSide: false, out _, out _, out _, out _), Is.False);
	}

	[Test]
	public void AdviceIsRaisedWhenTheCandidateIsBetterForEverybody()
	{
		IReadOnlyList<LobbyHandoverLinkRow> rows = AdviceRows(candidateToFirst: 30, candidateToSecond: 30, out List<int> slots);

		Assert.That(HostHandoverSuggestion.TryAdvise(rows, slots, hostSlot: 1, participantSamples: HostHandoverRules.MinimumSuggestedSamplesPerSeat, out HostHandoverSuggestion suggestion), Is.True);
		Assert.That(suggestion.CandidateSlot, Is.EqualTo(2));
	}

	[Test]
	public void AdviceIsNeverRaisedWhenOneSeatWouldBeWorseOff()
	{
		IReadOnlyList<LobbyHandoverLinkRow> rows = AdviceRows(candidateToFirst: 30, candidateToSecond: HostHandoverRules.MaximumRoundTripRegressionMilliseconds + 100, out List<int> slots);

		Assert.That(HostHandoverSuggestion.TryAdvise(rows, slots, hostSlot: 1, participantSamples: HostHandoverRules.MinimumSuggestedSamplesPerSeat, out _), Is.False);
	}

	[Test]
	public void AdviceIsNeverRaisedForALobbyTooSmallToCompare()
	{
		var rows = new List<LobbyHandoverLinkRow>
		{
			Row(2, Sample(1, 40), Sample(3, 30))
		};

		Assert.That(HostHandoverSuggestion.TryAdvise(rows, new List<int> { 1, 2 }, hostSlot: 1, participantSamples: HostHandoverRules.MinimumSuggestedSamplesPerSeat, out _), Is.False);
	}

	private static LobbyHandoverReport HealthyReport(int seats)
	{
		var metrics = new LobbyHandoverSeatMetric[seats];
		for (int index = 0; index < seats; index++)
		{
			metrics[index] = new LobbyHandoverSeatMetric
			{
				Slot = index + 1,
				AccountId = 1000 + index,
				Name = $"player-{index + 1}",
				CurrentHostMeasured = true,
				CurrentHostRoundTripMilliseconds = index == 0 ? 0 : 60,
				CandidateMeasured = true,
				CandidateRoundTripMilliseconds = index == 0 ? 0 : 40,
				CandidateSamples = HostHandoverRules.MinimumSamplesPerSeat
			};
		}

		return new LobbyHandoverReport
		{
			Seats = metrics,
			Credentials = metrics.Select(metric => new LobbyHandoverSeatCredential { Slot = metric.Slot, AccountId = metric.AccountId, SessionToken = $"credential-{metric.Slot}" }).ToArray(),
			CandidateEndpointVerified = true,
			MapMatched = true,
			CandidateStabilityRank = HostStabilityRanks.Excellent,
			CurrentHostStabilityRank = HostStabilityRanks.Excellent,
			CandidateEndpointAddress = "127.0.0.1",
			CandidateEndpointPort = 7778
		};
	}

	/// <summary>
	///     A lobby of the host and two players where the second seat is better placed for everybody: the authority
	///     measured the host's link to that seat, and both other seats measured a shorter trip to it than they have now.
	/// </summary>
	private static IReadOnlyList<LobbyHandoverLinkRow> AdviceRows(int candidateToFirst, int candidateToSecond, out List<int> slots)
	{
		slots = [1, 2, 3];
		return
		[
			Row(1, Sample(2, 30), Sample(3, 100)),
			Row(2, Sample(1, 30), Sample(3, candidateToFirst)),
			Row(3, Sample(1, 100), Sample(2, candidateToSecond))
		];
	}

	private static LobbyHandoverLinkRow Row(int observerSlot, params LobbyHandoverLinkSample[] samples)
	{
		return new LobbyHandoverLinkRow { ObserverSlot = observerSlot, Samples = samples };
	}

	private static LobbyHandoverLinkSample Sample(int targetSlot, int roundTrip)
	{
		return new LobbyHandoverLinkSample
		{
			TargetSlot = targetSlot,
			Measured = true,
			RoundTripMilliseconds = roundTrip,
			JitterMilliseconds = 2,
			Samples = HostHandoverRules.MinimumSuggestedSamplesPerSeat
		};
	}
}
