using Realm.Ecs.AI.Affordances;

namespace Realm.Ecs.AI.Policy;

public interface IBotDecisionPolicy
{
	GenericAffordance? SelectAction(
		IReadOnlyList<GenericAffordance> affordances,
		float[] weights,
		float temperature,
		float aggressionMultiplier,
		SeededRandom random);
}
