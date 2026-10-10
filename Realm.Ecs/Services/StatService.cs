using Arch.Core;
using Realm.Ecs.Common;
using Realm.Ecs.Components.Stats;

namespace Realm.Ecs.Services;

/// <summary>
///     Demonstrates how Stat and StatModifier components are used together.
/// </summary>
public class StatService
{
	private readonly WorldAccessor _ecsWorldAccessor;

	public StatService(WorldAccessor ecsWorldAccessor)
	{
		_ecsWorldAccessor = ecsWorldAccessor;
	}

	/// <summary>
	///     Gets the final calculated value of a stat after all modifiers are applied.
	///     An entity can have multiple StatModifier components.
	/// </summary>
	public float GetStatValue(Entity entity, StatId statId)
	{
		if (!_ecsWorldAccessor.Current.Has<Stats>(entity)) return 0f;

		var baseStats = _ecsWorldAccessor.Current.Get<Stats>(entity).Value;
		if (!baseStats.TryGetValue(statId, out var currentValue)) return 0f;

		var query = QueryCache.AllStatModifierQuery;
		var flatBonus = 0f;
		var percentBonus = 1.0f;

		_ecsWorldAccessor.Current.Query(in query, (ref Entity e, ref StatModifier mod) =>
		{
			if (e != entity || mod.StatTypeId != statId) return;

			if (mod.Type == ModifierType.Flat)
				flatBonus += mod.Value;
			else if (mod.Type == ModifierType.Percentage) percentBonus *= mod.Value;
		});

		return (currentValue + flatBonus) * percentBonus;
	}

	/// <summary>
	///     Adds a StatModifier to an entity. A 'BuffSystem' would be responsible
	///     for managing the duration and removing expired modifiers.
	/// </summary>
	public void ApplyStatModifier(Entity entity, StatModifier modifier)
	{
		_ecsWorldAccessor.Current.Add(entity, modifier);
	}
}