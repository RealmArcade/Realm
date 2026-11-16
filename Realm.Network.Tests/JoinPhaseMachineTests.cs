using System.Collections.Generic;
using NUnit.Framework;
using Realm.Shared.Reachability;

namespace Realm.Network.Tests;

/// <summary>
///     The join phase machine: which phase a join is in, which phase a failure is reported against, and when another
///     attempt is worth making. The four shapes a join takes - success, silence, refusal, and a punch that arrived - are
///     the contract, because a phase reported wrongly sends the player and the host's chat line after the wrong problem.
/// </summary>
[TestFixture]
public class JoinPhaseMachineTests
{
	[Test]
	public void ThePhasesAreInTheOrderAJoinGoesThroughThem()
	{
		Assert.That(JoinPhases.All, Is.EqualTo(new[]
		{
			JoinPhases.Registry,
			JoinPhases.Resolve,
			JoinPhases.Report,
			JoinPhases.Punch,
			JoinPhases.Answer,
			JoinPhases.Admission,
			JoinPhases.Session
		}));

		foreach (string phase in JoinPhases.All) Assert.That(JoinPhaseMachine.OrderOf(phase), Is.GreaterThanOrEqualTo(0));
		Assert.That(JoinPhaseMachine.OrderOf("something-else"), Is.EqualTo(-1));
		Assert.That(JoinPhaseMachine.OrderOf(null), Is.EqualTo(-1));
	}

	[Test]
	public void EveryPhaseIsOneThisBuildStates()
	{
		foreach (string phase in JoinPhases.All) Assert.That(JoinPhases.IsKnown(phase), Is.True, phase);
		Assert.That(JoinPhases.IsKnown("punch-through"), Is.False);
	}

	[Test]
	public void EveryPhaseAFailureIsReportedAgainstStatesTheOneRestrictionSentence()
	{
		foreach (string phase in JoinPhases.All)
		{
			Assert.That(JoinPhaseSentences.For(phase), Is.EqualTo(JoinPhaseSentences.RestrictiveNetwork), phase);
		}

		Assert.That(JoinPhaseSentences.For("not-a-phase"), Is.EqualTo(JoinPhaseSentences.RestrictiveNetwork));
		Assert.That(JoinPhaseSentences.Generic(), Is.EqualTo(JoinPhaseSentences.RestrictiveNetwork));
	}

	[Test]
	public void TheHostsChatLineAlsoNamesEveryPhaseWithoutFallingBackToTheUnnamedOne()
	{
		var lines = new HashSet<string>(System.StringComparer.Ordinal);
		foreach (string phase in JoinPhases.All)
		{
			string line = JoinPhaseSentences.HostLineFor(phase);
			Assert.That(line, Is.Not.Empty, phase);
			Assert.That(lines.Add(line), Is.True, $"{phase} states a step the host already reads for another phase");
		}

		Assert.That(JoinPhaseSentences.HostLineFor("not-a-phase"), Is.EqualTo(JoinPhaseSentences.HostUnnamed));
		Assert.That(lines.Contains(JoinPhaseSentences.HostUnnamed), Is.False, "a phase without its own line would read to the host as an unnamed step");
	}

	[Test]
	public void ASuccessfulJoinVisitsThePhasesItWentThroughWithoutReportingAFailure()
	{
		var visited = new List<string> { JoinPhases.Registry, JoinPhases.Resolve, JoinPhases.Report, JoinPhases.Answer, JoinPhases.Admission, JoinPhases.Session };

		int previous = -1;
		foreach (string phase in visited)
		{
			int order = JoinPhaseMachine.OrderOf(phase);
			Assert.That(order, Is.GreaterThan(previous), $"{phase} came after a phase that follows it");
			previous = order;
		}

		Assert.That(JoinPhaseMachine.IsRetryable(JoinPhaseMachine.OutcomeOf(answered: true, refused: false)), Is.False, "an admitted join is never attempted again");
	}

	[Test]
	public void AnAttemptNobodyAnsweredIsSilentAndWorthRepeating()
	{
		JoinStepOutcome outcome = JoinPhaseMachine.OutcomeOf(answered: false, refused: false);

		Assert.That(outcome, Is.EqualTo(JoinStepOutcome.Silent));
		Assert.That(JoinPhaseMachine.PhaseOfUnadmittedAttempt(refused: false), Is.EqualTo(JoinPhases.Answer));
		Assert.That(JoinPhaseMachine.IsRetryable(outcome), Is.True);
	}

	[Test]
	public void AnAttemptTheAuthorityRefusedIsReportedAgainstAdmissionAndIsNotRepeated()
	{
		JoinStepOutcome outcome = JoinPhaseMachine.OutcomeOf(answered: true, refused: true);

		Assert.That(outcome, Is.EqualTo(JoinStepOutcome.Refused));
		Assert.That(JoinPhaseMachine.PhaseOfUnadmittedAttempt(refused: true), Is.EqualTo(JoinPhases.Admission));
		Assert.That(JoinPhaseMachine.IsRetryable(outcome), Is.False);
	}

	[Test]
	public void ASilentJoinWhosePunchNeverArrivedIsReportedAgainstThePunch()
	{
		Assert.That(JoinPhaseMachine.PhaseOfSilenceAfterPunch(punchCoordinated: true, punchArrived: false), Is.EqualTo(JoinPhases.Punch));
	}

	[Test]
	public void ASilentJoinThatWasPunchedIsReportedAgainstTheAnswer()
	{
		Assert.That(JoinPhaseMachine.PhaseOfSilenceAfterPunch(punchCoordinated: true, punchArrived: true), Is.EqualTo(JoinPhases.Answer));
	}

	[Test]
	public void ASilentJoinWithNoPunchToBlameIsReportedAgainstTheAnswer()
	{
		Assert.That(JoinPhaseMachine.PhaseOfSilenceAfterPunch(punchCoordinated: false, punchArrived: false), Is.EqualTo(JoinPhases.Answer), "nothing was coordinated, so nothing about a punch can be said");
	}

	[Test]
	public void APunchThatArrivedTurnsASilenceIntoAPortThatNeverAnswered()
	{
		const string silencePhase = JoinPhases.Answer;

		Assert.That(JoinPhaseMachine.PhaseOfSilenceAfterPunch(true, false), Is.EqualTo(JoinPhases.Punch));
		Assert.That(JoinPhaseMachine.PhaseOfSilenceAfterPunch(true, true), Is.EqualTo(silencePhase));
	}

	[Test]
	public void AnAttemptThisScreenCouldNotMakeIsWorthRepeating()
	{
		Assert.That(JoinPhaseMachine.IsRetryable(JoinStepOutcome.Unusable), Is.True, "nothing was measured by an attempt that was never made");
	}

	[Test]
	public void TheTwoSilentShapesAreReportedAgainstDifferentPhases()
	{
		string punch = JoinPhaseMachine.PhaseOfSilenceAfterPunch(true, false);
		string answer = JoinPhaseMachine.PhaseOfSilenceAfterPunch(true, true);

		Assert.That(punch, Is.Not.EqualTo(answer), "the two silences need different sentences, so they need different phases");
		Assert.That(JoinPhaseMachine.OrderOf(punch), Is.LessThan(JoinPhaseMachine.OrderOf(answer)), "the punch is decided before the game port can answer");
	}

	[Test]
	public void EveryPhaseAFailureCanBeReportedAgainstIsOneTheJoinReaches()
	{
		var attributions = new List<string>
		{
			JoinPhases.Registry,
			JoinPhases.Resolve,
			JoinPhases.Report,
			JoinPhaseMachine.PhaseOfSilenceAfterPunch(true, false),
			JoinPhaseMachine.PhaseOfSilenceAfterPunch(true, true),
			JoinPhaseMachine.PhaseOfUnadmittedAttempt(true),
			JoinPhaseMachine.PhaseOfUnadmittedAttempt(false),
			JoinPhases.Session
		};

		foreach (string phase in attributions) Assert.That(JoinPhases.IsKnown(phase), Is.True, phase);
		Assert.That(new HashSet<string>(attributions), Is.EquivalentTo(JoinPhases.All), "every phase a join can fail at is one the machine names");
	}
}
