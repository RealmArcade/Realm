using Realm.Ecs.AI.Affordances;

namespace Realm.Ecs.AI.Policy;

/// <summary>
/// Picks uniformly at random among the candidate actions whose payload starts with a given prefix, and otherwise
/// hands the whole list to the policy it wraps. It exists so a match can keep one brain for everything it scores
/// while one family of decisions, such as a map's draft candidates, is played by luck instead.
/// </summary>
public class PayloadRandomDecisionPolicy : IBotDecisionPolicy
{
	private readonly string _payloadPrefix;
	private readonly IBotDecisionPolicy _fallbackPolicy;

	public PayloadRandomDecisionPolicy(string payloadPrefix, IBotDecisionPolicy fallbackPolicy)
	{
		_payloadPrefix = payloadPrefix ?? string.Empty;
		_fallbackPolicy = fallbackPolicy ?? new LinearUtilityDecisionPolicy();
	}

	public GenericAffordance? SelectAction(
		IReadOnlyList<GenericAffordance> affordances,
		float[] weights,
		float temperature,
		float aggressionMultiplier,
		SeededRandom random)
	{
		int candidateCount = 0;
		for (int index = 0; index < affordances.Count; index++)
		{
			if (MatchesPrefix(affordances[index])) candidateCount++;
		}

		if (candidateCount == 0)
		{
			return _fallbackPolicy.SelectAction(affordances, weights, temperature, aggressionMultiplier, random);
		}

		SeededRandom stream = random ?? SeededRandom.Shared;
		int selectedCandidate = stream.Next(candidateCount);

		for (int index = 0; index < affordances.Count; index++)
		{
			if (!MatchesPrefix(affordances[index])) continue;

			if (selectedCandidate == 0) return affordances[index];
			selectedCandidate--;
		}

		return affordances[affordances.Count - 1];
	}

	private bool MatchesPrefix(in GenericAffordance affordance)
	{
		return !string.IsNullOrEmpty(affordance.PayloadId)
			&& affordance.PayloadId.StartsWith(_payloadPrefix, StringComparison.Ordinal);
	}
}
