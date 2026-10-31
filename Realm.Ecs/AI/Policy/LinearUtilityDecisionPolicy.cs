using Realm.Ecs.AI.Affordances;

namespace Realm.Ecs.AI.Policy;

public class LinearUtilityDecisionPolicy : IBotDecisionPolicy
{
	private readonly LinearUtilityPolicy _policy = new();

	public GenericAffordance? SelectAction(
		IReadOnlyList<GenericAffordance> affordances,
		float[] weights,
		float temperature,
		float aggressionMultiplier,
		SeededRandom random)
	{
		return _policy.SelectAction(affordances, weights, 0.0f, temperature, aggressionMultiplier, random);
	}
}
