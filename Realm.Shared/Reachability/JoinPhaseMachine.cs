namespace Realm.Shared.Reachability;

/// <summary>What one step of a join produced, in the terms the phase the join is in has to be told apart by.</summary>
public enum JoinStepOutcome
{
	/// <summary>The step did what it was for.</summary>
	Success,

	/// <summary>Nothing answered, which is the only outcome another attempt can improve.</summary>
	Silent,

	/// <summary>The other side answered and refused, which no further attempt can change.</summary>
	Refused,

	/// <summary>This screen could not even make the attempt, which is a local failure rather than an answer.</summary>
	Unusable
}

/// <summary>
///     The decisions the join state machine makes about phases, kept apart from the screen that runs a join so they can be
///     exercised without a network. A join reports exactly one phase when it fails, and a silent attempt has two candidates:
///     the host never punched this screen, which is a punch that never arrived, or it punched and the game port stayed
///     silent, which is a port that never answered. Those two are different problems with different fixes, and telling them
///     apart is the one thing that must not be guessed.
/// </summary>
public static class JoinPhaseMachine
{
	/// <summary>
	///     The phase an attempt that was not admitted belongs to: a refusal is the authority deciding, and silence is the
	///     transport never answering, which are two different sentences for the player.
	/// </summary>
	/// <param name="refused">Whether the authority answered and refused.</param>
	/// <returns>The phase a failure at this point is reported against.</returns>
	public static string PhaseOfUnadmittedAttempt(bool refused) => refused ? JoinPhases.Admission : JoinPhases.Answer;

	/// <summary>
	///     The phase a silent attempt is reported against once the punch relay has been asked what happened. A lobby nothing
	///     punched is a punch that never arrived, because this side cannot tell a host that ignored the request from a router
	///     that dropped the answer; a lobby that did punch and still stayed silent is a game port that never answered.
	/// </summary>
	/// <param name="punchCoordinated">Whether this screen had a ticket and reported itself, so a punch was expected.</param>
	/// <param name="punchArrived">Whether the punch relay recorded the host's punch as having arrived.</param>
	/// <returns>The phase the silence is reported against.</returns>
	public static string PhaseOfSilenceAfterPunch(bool punchCoordinated, bool punchArrived) => punchCoordinated && !punchArrived ? JoinPhases.Punch : JoinPhases.Answer;

	/// <summary>
	///     Whether another attempt is worth making after this outcome. Silence is: nothing was measured, so nothing is known.
	///     An attempt this screen could not make is too, because nothing was measured by it either. A refusal is an answer a
	///     second attempt would only be told again, and a step that succeeded has nothing left to retry.
	/// </summary>
	/// <param name="outcome">What the step produced.</param>
	/// <returns>Whether the join should try again.</returns>
	public static bool IsRetryable(JoinStepOutcome outcome) => outcome is JoinStepOutcome.Silent or JoinStepOutcome.Unusable;

	/// <summary>
	///     How far through the phases a name is, which is what lets a join be checked for having visited the phases it says
	///     it did, in the order a join goes through them.
	/// </summary>
	/// <param name="phase">The phase name.</param>
	/// <returns>Its position, or -1 when the name is not one this build states.</returns>
	public static int OrderOf(string? phase)
	{
		if (phase == null) return -1;

		IReadOnlyList<string> all = JoinPhases.All;
		for (int index = 0; index < all.Count; index++)
		{
			if (string.Equals(all[index], phase, System.StringComparison.Ordinal)) return index;
		}

		return -1;
	}

	/// <summary>
	///     The outcome a step produced, read from what the attempt did rather than from a sentence: an answered attempt is
	///     either admitted or refused, and an unanswered one is silent.
	/// </summary>
	/// <param name="answered">Whether the other side answered at all.</param>
	/// <param name="refused">Whether the answer was a refusal.</param>
	/// <returns>The outcome of the step.</returns>
	public static JoinStepOutcome OutcomeOf(bool answered, bool refused)
	{
		if (!answered) return JoinStepOutcome.Silent;
		return refused ? JoinStepOutcome.Refused : JoinStepOutcome.Success;
	}
}
