using Realm.Ecs.AI.Affordances;

namespace Realm.Ecs.AI.Policy;

public class FixedFirstPickDecisionPolicy : IBotDecisionPolicy
{
	private readonly string _preferredPayloadId;
	private readonly LinearUtilityPolicy _uniformPolicy = new();
	private readonly bool _onlyFirstDecision;
	private bool _preferredServed;

	public FixedFirstPickDecisionPolicy(string preferredPayloadId, bool onlyFirstDecision = false)
	{
		_preferredPayloadId = preferredPayloadId ?? string.Empty;
		_onlyFirstDecision = onlyFirstDecision;
	}

	public GenericAffordance? SelectAction(
		IReadOnlyList<GenericAffordance> affordances,
		float[] weights,
		float temperature,
		float aggressionMultiplier,
		SeededRandom random)
	{
		if (affordances == null || affordances.Count == 0)
		{
			return null;
		}

		if (!string.IsNullOrEmpty(_preferredPayloadId) && !(_onlyFirstDecision && _preferredServed))
		{
			for (int index = 0; index < affordances.Count; index++)
			{
				if (string.Equals(affordances[index].PayloadId, _preferredPayloadId, StringComparison.OrdinalIgnoreCase))
				{
					_preferredServed = true;
					return affordances[index];
				}
			}
		}

		return _uniformPolicy.SelectAction(affordances, weights, 1.0f, temperature, aggressionMultiplier, random);
	}
}
