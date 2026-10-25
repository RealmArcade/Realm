using System;
using Realm.Ecs.Common;
using Realm.Ecs.Components.Core;
using Realm.Ecs.Components.Stats;

namespace Realm.Ecs.Services;

/// <summary>
/// Attribute gain per level, independent of where the authored curve came from.
/// </summary>
public readonly record struct LevelGrowth(
	float Strength,
	float Agility,
	float Vitality,
	float Intelligence,
	float Wisdom,
	float Fortune)
{
	public static readonly LevelGrowth None = new(0f, 0f, 0f, 0f, 0f, 0f);

	public bool IsZero => Strength == 0f && Agility == 0f && Vitality == 0f
		&& Intelligence == 0f && Wisdom == 0f && Fortune == 0f;
}

/// <summary>
/// Turns a unit's level into attribute growth through the shared modifier pipeline, so level
/// scaling composes with items, upgrades and buffs instead of bypassing them.
/// </summary>
public static class LevelService
{
	public const string ModifierSourcePrefix = "level:";

	public static void Apply(EcsWorld world, Entity entity, int level, in LevelGrowth growth)
	{
		if (!world.IsAlive(entity)) return;

		bool hasState = world.Has<ModifierState>(entity);
		if (!hasState && (growth.IsZero || level <= 1)) return;

		if (!hasState)
		{
			world.Add(entity, new ModifierState(null));
		}

		ref var state = ref world.Get<ModifierState>(entity);
		RemoveLevelModifiers(ref state);

		int steps = Math.Max(0, level - 1);
		if (steps > 0)
		{
			Add(ref state, StatKeys.Strength, growth.Strength, steps);
			Add(ref state, StatKeys.Agility, growth.Agility, steps);
			Add(ref state, StatKeys.Vitality, growth.Vitality, steps);
			Add(ref state, StatKeys.Intelligence, growth.Intelligence, steps);
			Add(ref state, StatKeys.Wisdom, growth.Wisdom, steps);
			Add(ref state, StatKeys.Fortune, growth.Fortune, steps);
		}

		AttributeStatCalculator.RecalculateEntityStats(world, entity);
	}

	private static void Add(ref ModifierState state, string statKey, float perLevel, int steps)
	{
		if (perLevel == 0f) return;

		state.Add(new StatModifier(
			new StatId(statKey),
			ModifierType.Flat,
			perLevel * steps,
			-1f,
			string.Empty,
			steps + 1,
			ModifierSourcePrefix + statKey));
	}

	private static void RemoveLevelModifiers(ref ModifierState state)
	{
		for (int i = state.Count - 1; i >= 0; i--)
		{
			if (state.Get(i).SourceUpgradeId.StartsWith(ModifierSourcePrefix, StringComparison.Ordinal))
			{
				state.RemoveAt(i);
			}
		}
	}
}
