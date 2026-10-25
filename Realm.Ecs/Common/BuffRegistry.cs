using Realm.Ecs.Components.Stats;

namespace Realm.Ecs.Common;

public static class BuffRegistry
{
	// Percentage modifiers are additive fractions: 0.2 means +20% on top of the stat.
	public static readonly Dictionary<string, List<StatModifier>> BuffModifiers = new()
	{
		{
			"morale_boost", new List<StatModifier>
			{
				new StatModifier(new StatId("Armor"), ModifierType.Flat, 2f),
				new StatModifier(new StatId("Attack"), ModifierType.Flat, 5f),
				new StatModifier(new StatId("MovementSpeed"), ModifierType.Percentage, 0.2f)
			}
		},
		{
			"melee_charge", new List<StatModifier>
			{
				new StatModifier(new StatId("MovementSpeed"), ModifierType.Percentage, 0.5f),
				new StatModifier(new StatId("Attack"), ModifierType.Flat, 10f)
			}
		}
	};

	public static void RegisterBuff(string buffId, IEnumerable<StatModifier> modifiers)
	{
		BuffModifiers[buffId] = new List<StatModifier>(modifiers);
	}
}