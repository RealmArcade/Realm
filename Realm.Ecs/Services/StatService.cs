using Arch.Core;
using Realm.Ecs.Common;
using Realm.Ecs.Components.Stats;

namespace Realm.Ecs.Services;

/// <summary>
///     Demonstrates how Stat and StatModifier components are used together.
/// </summary>
public class StatService
{
	public StatService()
	{
	}

	/// <summary>
	///     Gets the final calculated value of a stat after all modifiers are applied.
	///     An entity can have multiple StatModifier components.
	/// </summary>
	public float GetStatValue(Entity entity, StatId statId)
	{
		if (!ECSContext.World.Has<Stats>(entity)) return 0f;

		var baseStats = ECSContext.World.Get<Stats>(entity).Value;
		if (!baseStats.TryGetValue(statId, out var currentValue)) return 0f;

		var query = QueryCache.AllStatModifierQuery;
		var flatBonus = 0f;
		var percentAdditive = 0f;
		var percentMultiplicative = 1.0f;
		var formulaBonus = 0f;

		ECSContext.World.Query(in query, (ref Entity e, ref StatModifier mod) =>
		{
			if (e != entity || mod.StatTypeId != statId) return;

			switch (mod.Type)
			{
				case ModifierType.Flat:
					flatBonus += mod.Value;
					break;
				case ModifierType.PercentAdditive:
					percentAdditive += mod.Value;
					break;
				case ModifierType.PercentMultiplicative:
					percentMultiplicative *= mod.Value;
					break;
				case ModifierType.CustomFormula:
					if (!string.IsNullOrWhiteSpace(mod.Formula))
					{
						var dict = new Dictionary<string, float>(StringComparer.OrdinalIgnoreCase)
						{
							["base"] = currentValue,
							["level"] = mod.SourceLevel,
							["val"] = mod.Value
						};
						var context = new FormulaContext(ECSContext.World, entity, default, null, dict);
						formulaBonus += FormulaEvaluator.Evaluate(mod.Formula, in context);
					}
					else
					{
						formulaBonus += mod.Value;
					}
					break;
			}
		});

		return (currentValue + flatBonus) * (1.0f + percentAdditive) * percentMultiplicative + formulaBonus;
	}

	/// <summary>
	///     Adds a StatModifier to an entity. A 'BuffSystem' would be responsible
	///     for managing the duration and removing expired modifiers.
	/// </summary>
	public void ApplyStatModifier(Entity entity, StatModifier modifier)
	{
		ECSContext.World.Add(entity, modifier);
	}
}